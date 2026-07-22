import test from "node:test";
import assert from "node:assert/strict";
import { randomUUID } from "node:crypto";
import { createApplication } from "../src/app.js";
import { loadConfig } from "../src/config.js";
import { addUnityContractAliases } from "../src/compat/unityContract.js";
import {
  FixedD20Source,
  normalizeTravelRequest,
  normalizeTurnRequest,
  resolveSafeTravel,
  resolveTurn,
  turnFingerprint
} from "../src/domain/turn-engine.js";
import { areaAt, isWalkable } from "../src/domain/world.js";

const databaseUrl = process.env.TEST_DATABASE_URL;
const USER_ID = "44444444-4444-4444-8444-444444444444";
const ADMIN_USER_ID = "44444444-4444-4444-8444-444444444445";
const SLOT_USER_ID = "44444444-4444-4444-8444-444444444446";
const AMBIENT_USER_ID = "44444444-4444-4444-8444-444444444447";
const SOFT_HORIZON_USER_ID = "44444444-4444-4444-8444-444444444448";
const PROJECTION_USER_ID = "44444444-4444-4444-8444-444444444450";
const CACHE_REPLICA_USER_ID = "44444444-4444-4444-8444-444444444451";
const DEBT_BACKFLOW_USER_ID = "44444444-4444-4444-8444-444444444452";
const SENSITIVE_API_KEY = "integration-only-api-key-that-must-not-persist";
const silentLogger = { debug() {}, info() {}, warn() {}, error() {} };

function distance(left, right) {
  return Math.abs(left.x - right.x) + Math.abs(left.y - right.y);
}

const fakeNarrator = {
  async narrate() {
    return {
      summary: "위험 권역의 경계가 조용히 열렸다.",
      body: "넙죽이는 서버가 확정한 편집 결과를 확인했다. 봉인된 코드리아의 geometry는 그대로 유지됐다.",
      dialogue: [],
      proposedOps: [],
      fallbackUsed: false,
      model: "fake-integration-model"
    };
  }
};

test("PostgreSQL adapter atomically commits mechanics and validated director state", { skip: !databaseUrl }, async (t) => {
  const config = loadConfig({
    STORAGE: "postgres",
    DATABASE_URL: databaseUrl,
    DATABASE_SSL: "false",
    AUTH_MODE: "required",
    LOG_LEVEL: "silent",
    GEMINI_API_KEY: SENSITIVE_API_KEY
  });
  const applicationOptions = {
    config,
    d20Source: new FixedD20Source(20),
    logger: silentLogger,
    narrator: fakeNarrator
  };
  const application = await createApplication(applicationOptions);
  let primaryClosed = false;
  let reopenedApplication = null;
  t.after(async () => {
    let cleanupApplication = reopenedApplication;
    if (!cleanupApplication && !primaryClosed) cleanupApplication = application;
    if (!cleanupApplication) cleanupApplication = await createApplication(applicationOptions);
    await cleanupApplication.store.pool.query("delete from keyboard_wanderer.profiles where id = $1", [USER_ID]);
    await cleanupApplication.close();
  });
  await new Promise((resolve) => application.server.listen(0, "127.0.0.1", resolve));
  const address = application.server.address();
  const baseUrl = `http://127.0.0.1:${address.port}`;

  const request = async (path, body) => {
    const response = await fetch(`${baseUrl}${path}`, {
      method: "POST",
      headers: { "content-type": "application/json", "x-user-id": USER_ID },
      body: JSON.stringify(body)
    });
    return { response, payload: await response.json() };
  };

  const campaignResult = await request("/v1/campaigns", { worldSeed: 88, turnLimit: 40 });
  assert.equal(campaignResult.response.status, 201);
  assert.equal(campaignResult.payload.campaign.worldId, "WORLD_CODRIA");
  const runResult = await request(`/v1/campaigns/${campaignResult.payload.campaign.id}/runs`, {
    worldSeed: 88,
    turnLimit: 40
  });
  assert.equal(runResult.response.status, 201, JSON.stringify(runResult.payload));
  let run = runResult.payload.run;
  assert.equal(run.worldId, "WORLD_CODRIA");
  assert.equal(typeof run.campaignTitle, "string");
  assert.ok(run.campaignTitle.length > 0);
  const storedInitial = await application.store.getRun(USER_ID, run.id);
  assert.equal(storedInitial.worldId, "WORLD_CODRIA");
  assert.match(String(storedInitial.worldInstanceId), /^[0-9a-f-]{36}$/);
  const playerBefore = storedInitial.entities.find((item) => item.id === storedInitial.playerEntityId);
  const initialProjection = await application.store.pool.query(
    `select a.energy, a.max_energy, ep.facing,
            i.id as inventory_id, it.quantity, it.state_json
       from keyboard_wanderer.actors a
       join keyboard_wanderer.entity_positions ep
         on ep.entity_id = a.entity_id and ep.owner_id = a.owner_id and ep.run_id = a.run_id
       join keyboard_wanderer.inventories i
         on i.actor_id = a.entity_id and i.owner_id = a.owner_id and i.run_id = a.run_id
       left join keyboard_wanderer.items it
         on it.inventory_id = i.id and it.owner_id = i.owner_id and it.run_id = i.run_id
      where a.run_id = $1 and a.owner_id = $2 and a.entity_id = $3`,
    [run.id, USER_ID, storedInitial.playerEntityId]
  );
  assert.equal(initialProjection.rowCount, playerBefore.state.inventory.length);
  assert.equal(Number(initialProjection.rows[0].energy), storedInitial.focus);
  assert.equal(Number(initialProjection.rows[0].max_energy), storedInitial.maxFocus);
  assert.equal(initialProjection.rows[0].facing, playerBefore.state.facing.toLowerCase());
  assert.equal(initialProjection.rows[0].state_json.id, playerBefore.state.inventory[0].id);
  assert.equal(initialProjection.rows[0].state_json.name, playerBefore.state.inventory[0].name);
  assert.equal(Number(initialProjection.rows[0].quantity), playerBefore.state.inventory[0].quantity);
  let travelSetup = null;
  for (let radius = 1; radius <= 4 && !travelSetup; radius += 1) {
    for (let dx = -radius; dx <= radius; dx += 1) {
      const dyMagnitude = radius - Math.abs(dx);
      for (const dy of new Set([dyMagnitude, -dyMagnitude])) {
        const point = { x: playerBefore.position.x + dx, y: playerBefore.position.y + dy };
        if (!isWalkable(storedInitial.world, point)) continue;
        const destination = { areaId: areaAt(storedInitial.world, point).id, ...point };
        try {
          const probe = resolveSafeTravel({
            run: structuredClone(storedInitial),
            request: normalizeTravelRequest({
              inputType: "MOVE",
              idempotencyKey: `postgres-probe-${point.x}-${point.y}`,
              expectedRunVersion: 1,
              destination
            }),
            now: "2026-07-17T00:00:00.000Z"
          });
          const investigationTarget = storedInitial.entities.find((candidate) => candidate.active
            && candidate.id !== storedInitial.playerEntityId && candidate.kind !== "enemy"
            && !candidate.state?.adminAccessLevelId && distance(candidate.position, point) <= 6);
          if (investigationTarget && probe.navigation.encounterOpened === false
            && probe.navigation.to.x === point.x && probe.navigation.to.y === point.y) {
            travelSetup = { investigationTarget, destination, staging: point };
            break;
          }
        } catch {
          // Probe the next nearby walkable tile.
        }
      }
      if (travelSetup) break;
    }
  }
  assert.ok(travelSetup, "the entry area must expose a safe move followed by a nearby investigation target");
  const investigationTarget = travelSetup.investigationTarget;
  const staging = travelSetup.staging;
  assert.equal(isWalkable(storedInitial.world, staging), true);
  const travelRequest = {
    inputType: "MOVE",
    idempotencyKey: "postgres-travel-0001",
    expectedRunVersion: 1,
    destination: travelSetup.destination,
    playerNote: "가까운 조사 대상으로 이동"
  };
  const travelled = await request(`/v1/runs/${run.id}/travel`, travelRequest);
  assert.equal(travelled.response.status, 201);
  assert.equal(travelled.payload.run.currentTurn, 0);
  assert.equal(travelled.payload.run.version, 2);
  assert.ok(travelled.payload.run.travelTimeUnits > 0);
  assert.equal(travelled.payload.navigation.campaignTurnConsumed, false);
  assert.equal(travelled.payload.navigation.encounterOpened, false);
  assert.deepEqual(travelled.payload.navigation.to, travelRequest.destination);
  assert.ok(travelled.payload.navigation.sceneSequence.length >= 1);
  assert.equal(travelled.payload.navigation.narrative.summary, travelled.payload.navigation.sceneDecision.sceneGoal);
  assert.equal(travelled.payload.navigation.narrative.fallbackUsed, true);
  assert.match(travelled.payload.navigation.narrative.model, /^deterministic-scene-director-/);
  const travelReplay = await request(`/v1/runs/${run.id}/travel`, travelRequest);
  assert.equal(travelReplay.response.status, 200);
  assert.equal(travelReplay.payload.fromIdempotencyCache, true);
  assert.deepEqual(travelReplay.payload.run, travelled.payload.run, "first commit and idempotent replay must return the same persisted run DTO");
  for (const field of ["id", "sequence", "requestedDestination", "to", "path", "pathCost", "travelTimeUnits", "cumulativeTravelTimeUnits", "enteredAreaId", "enteredBiomeId", "campaignRole", "traversedAreaIds", "reachedPoiIds", "encounterOpened", "encounter", "campaignTurnConsumed", "campaignTurnBefore", "campaignTurnAfter", "layoutHash", "sceneDecision", "sceneSequence", "events", "narrative"]) {
    assert.deepEqual(travelReplay.payload.navigation[field], travelled.payload.navigation[field], `travel replay field ${field}`);
  }

  const turnRequest = {
    inputType: "USE_SKILL",
    idempotencyKey: "postgres-turn-0001",
    expectedRunVersion: 2,
    skillId: "SEARCH",
    targetIds: [investigationTarget.id]
  };
  const committed = await request(`/v1/runs/${run.id}/actions`, turnRequest);
  assert.equal(committed.response.status, 201);
  assert.equal(committed.payload.run.version, 3);
  assert.equal(committed.payload.run.currentTurn, 1);
  assert.equal(committed.payload.run.activeEncounter, null);
  assert.equal(committed.payload.turn.actionContext, "INVESTIGATION");
  assert.equal(committed.payload.turn.runtime.gameplayResult.result.target.id, investigationTarget.id);
  assert.equal(committed.payload.turn.runtime.gameplayResult.result.newInformation, true);
  assert.equal(committed.payload.run.entities.find((item) => item.id === investigationTarget.id).state.revealed, true);
  assert.equal(committed.payload.turn.narrative.fallbackUsed, false);
  assert.equal(committed.payload.turn.narrative.model, "fake-integration-model");

  const replay = await request(`/v1/runs/${run.id}/actions`, turnRequest);
  assert.equal(replay.response.status, 200);
  assert.equal(replay.payload.fromIdempotencyCache, true);
  assert.equal(replay.payload.run.currentTurn, 1);

  const abandoned = await request(`/v1/runs/${run.id}/abandon`, { expectedRunVersion: 3 });
  assert.equal(abandoned.response.status, 200);
  assert.equal(abandoned.payload.run.status, "abandoned");
  assert.equal(abandoned.payload.run.version, 4);
  const resumed = await request(`/v1/runs/${run.id}/resume`, { expectedRunVersion: 4 });
  assert.equal(resumed.response.status, 200);
  assert.equal(resumed.payload.run.status, "active");
  assert.equal(resumed.payload.run.version, 5);

  const ledger = await application.store.pool.query(
    `select tr.status, tr.fallback_used, tr.model, tr.command_schema_version,
            tr.input_type, tr.skill_id, tr.target_ids, tr.action_context,
            tr.campaign_turn_before, tr.campaign_turn_after, tr.campaign_turn_consumed,
            (select count(*)::int from keyboard_wanderer.llm_logs l where l.turn_record_id = tr.id) as llm_log_count
       from keyboard_wanderer.turn_records tr
      where tr.run_id = $1`,
    [run.id]
  );
  assert.equal(ledger.rows[0].status, "committed");
  assert.equal(ledger.rows[0].fallback_used, false);
  assert.equal(ledger.rows[0].model, "fake-integration-model");
  assert.equal(ledger.rows[0].command_schema_version, "codria-action.v4");
  assert.equal(ledger.rows[0].input_type, "USE_SKILL");
  assert.equal(ledger.rows[0].skill_id, "SEARCH");
  assert.deepEqual(ledger.rows[0].target_ids, [investigationTarget.id]);
  assert.equal(ledger.rows[0].action_context, "INVESTIGATION");
  assert.equal(Number(ledger.rows[0].campaign_turn_before), 0);
  assert.equal(Number(ledger.rows[0].campaign_turn_after), 1);
  assert.equal(ledger.rows[0].campaign_turn_consumed, true);
  assert.equal(ledger.rows[0].llm_log_count, 1);

  const codriaLedger = await application.store.pool.query(
    `select
        (select count(*)::int from keyboard_wanderer.world_region_axis_bindings where world_id = r.world_id) as axis_count,
        (select count(distinct wad.biome_id)::int from keyboard_wanderer.world_area_descriptors wad where wad.world_id = r.world_id) as biome_count,
        (select count(*)::int from keyboard_wanderer.safe_travels st where st.run_id = r.id and st.command_schema_version = 'codria-action.v4' and st.input_type = 'MOVE' and not st.campaign_turn_consumed) as move_count,
        (select count(*)::int from keyboard_wanderer.ability_usage_history au where au.run_id = r.id and au.skill_id = 'SEARCH') as ability_count,
        (select count(*)::int from keyboard_wanderer.technical_debt_entries td where td.run_id = r.id and td.operation_type = 'DELETE' and td.debt_delta > 0) as debt_count,
        (select count(*)::int from keyboard_wanderer.npc_relationships nr where nr.run_id = r.id) as relationship_count,
        (select count(*)::int from keyboard_wanderer.world_facts wf where wf.run_id = r.id and wf.superseded_by_fact_id is null) as fact_count,
        (select count(*)::int from keyboard_wanderer.unresolved_hooks uh where uh.run_id = r.id and uh.status = 'OPEN') as hook_count,
        (select root_system_eligible from keyboard_wanderer.run_admin_access_states where run_id = r.id) as root_system_eligible
      from keyboard_wanderer.runs r where r.id = $1`,
    [run.id]
  );
  assert.equal(codriaLedger.rows[0].axis_count, 6);
  assert.equal(codriaLedger.rows[0].biome_count, 6);
  assert.equal(codriaLedger.rows[0].move_count, 1);
  assert.equal(codriaLedger.rows[0].ability_count, 1);
  assert.equal(codriaLedger.rows[0].debt_count, 0);
  assert.ok(codriaLedger.rows[0].relationship_count >= 6);
  assert.ok(codriaLedger.rows[0].fact_count >= storedInitial.canonicalFacts.length);
  assert.ok(codriaLedger.rows[0].hook_count >= 1);
  assert.equal(codriaLedger.rows[0].root_system_eligible, false);

  const storedCommittedTurn = await application.store.getTurn(USER_ID, run.id, 1);
  const ruleLedger = await application.store.pool.query(
    `select rr.*, tr.id as committed_turn_record_id
       from keyboard_wanderer.turn_rule_resolutions rr
       join keyboard_wanderer.turn_records tr
         on tr.id = rr.turn_record_id and tr.run_id = rr.run_id and tr.owner_id = rr.owner_id
      where rr.run_id = $1`,
    [run.id]
  );
  assert.equal(ruleLedger.rowCount, 1);
  const rule = ruleLedger.rows[0];
  assert.equal(rule.turn_record_id, committed.payload.turn.id);
  assert.equal(rule.committed_turn_record_id, committed.payload.turn.id);
  assert.equal(Number(rule.turn_no), 1);
  assert.equal(Number(rule.d20_raw), committed.payload.turn.d20);
  assert.equal(rule.outcome, committed.payload.turn.outcome);
  assert.equal(rule.ruleset_version, storedCommittedTurn.rulesetVersion);
  assert.match(rule.state_hash_before, /^[0-9a-f]{64}$/);
  assert.match(rule.state_hash_after, /^[0-9a-f]{64}$/);
  assert.notEqual(rule.state_hash_before, rule.state_hash_after);
  assert.equal(rule.state_hash_before, storedCommittedTurn.stateHashBefore);
  assert.equal(rule.state_hash_after, storedCommittedTurn.stateHashAfter);
  assert.equal(rule.rng_audit.secretRedacted, true);
  assert.equal(rule.rng_audit.replayProtected, true);
  assert.equal(rule.rng_audit.runId, run.id);
  assert.equal(Number(rule.rng_audit.turnNo), 1);
  const progressState = await application.store.pool.query(
    `select ps.status, ps.state_version, ps.last_turn_no,
            jsonb_array_length(ps.progress_state->'facts') as fact_count,
            jsonb_array_length(ps.open_threads) as loop_count,
            jsonb_array_length(ps.progress_state->'npcRelationships') as relationship_count,
            jsonb_typeof(ps.rule_state) as rule_state_type,
            jsonb_typeof(ps.convergence_state) as convergence_state_type,
            gp.plan_hash, gp.generator_version, gp.validation_status, gp.fallback_used
       from keyboard_wanderer.run_progress_states ps
       join keyboard_wanderer.run_generation_plans gp
         on gp.id = ps.generation_plan_id and gp.run_id = ps.run_id
      where ps.run_id = $1`,
    [run.id]
  );
  assert.equal(progressState.rows[0].status, "active");
  assert.ok(Number(progressState.rows[0].state_version) >= 5);
  assert.equal(Number(progressState.rows[0].last_turn_no), 1);
  assert.ok(Number(progressState.rows[0].fact_count) >= 3);
  assert.ok(Number(progressState.rows[0].loop_count) >= 1);
  assert.ok(Number(progressState.rows[0].relationship_count) >= 6);
  assert.equal(progressState.rows[0].rule_state_type, "object");
  assert.equal(progressState.rows[0].convergence_state_type, "object");
  assert.match(progressState.rows[0].plan_hash, /^[0-9a-f]{64}$/);
  assert.equal(typeof progressState.rows[0].generator_version, "string");
  assert.ok(["validated", "fallback_validated"].includes(progressState.rows[0].validation_status));
  assert.equal(typeof progressState.rows[0].fallback_used, "boolean");

  const deepSnapshot = await application.store.pool.query(
    `select ss.id, ss.snapshot_kind, ss.run_version, ss.current_turn, ss.checksum_sha256,
            ss.plan_hash, ss.layout_hash, ss.last_turn_record_id, ss.last_event_id,
            ss.resume_metadata, ss.state_json,
            gp.id as sealed_generation_plan_id, gp.plan_hash as sealed_plan_hash,
            w.layout_hash as sealed_layout_hash,
            tr.turn_no as cursor_turn_no, te.turn_record_id as event_turn_record_id,
            te.event_index as cursor_event_index
       from keyboard_wanderer.save_snapshots ss
       join keyboard_wanderer.run_generation_plans gp
         on gp.id = ss.generation_plan_id and gp.run_id = ss.run_id
       join keyboard_wanderer.worlds w on w.id = ss.world_id
       left join keyboard_wanderer.turn_records tr on tr.id = ss.last_turn_record_id
       left join keyboard_wanderer.turn_events te on te.id = ss.last_event_id
      where ss.run_id = $1 and ss.snapshot_kind <> 'legacy'
      order by ss.run_version desc, ss.created_at desc
      limit 1`,
    [run.id]
  );
  assert.equal(deepSnapshot.rowCount, 1);
  const snapshot = deepSnapshot.rows[0];
  assert.equal(snapshot.snapshot_kind, "recovery");
  assert.equal(Number(snapshot.run_version), resumed.payload.run.version);
  assert.equal(Number(snapshot.current_turn), resumed.payload.run.currentTurn);
  assert.match(snapshot.checksum_sha256, /^[0-9a-f]{64}$/);
  assert.equal(snapshot.plan_hash, snapshot.sealed_plan_hash);
  assert.equal(snapshot.plan_hash, resumed.payload.run.campaignContentHash);
  assert.equal(snapshot.layout_hash, snapshot.sealed_layout_hash);
  assert.equal(snapshot.layout_hash, resumed.payload.run.world.layoutHash);
  assert.equal(snapshot.last_turn_record_id, committed.payload.turn.id);
  assert.ok(snapshot.last_event_id !== null);
  assert.equal(snapshot.event_turn_record_id, committed.payload.turn.id);
  assert.equal(Number(snapshot.cursor_turn_no), 1);
  assert.equal(Number(snapshot.cursor_event_index), committed.payload.turn.events.length - 1);
  assert.equal(snapshot.resume_metadata.authoritative, true);
  assert.equal(snapshot.resume_metadata.worldGeneratedOnce, true);
  assert.ok(snapshot.resume_metadata.secretFieldsRedacted.includes("resolutionSeed"));
  assert.equal(Object.hasOwn(snapshot.state_json, "resolutionSeed"), false);

  const resumeAudit = await application.store.pool.query(
    `select rv.validation_status, rv.attempt_no, rv.observed_checksum_sha256,
            rv.observed_plan_hash, rv.observed_layout_hash, rv.checks_json, rv.errors_json,
            ss.checksum_sha256 as snapshot_checksum, ss.plan_hash as snapshot_plan_hash,
            ss.layout_hash as snapshot_layout_hash, ss.run_version as snapshot_run_version
       from keyboard_wanderer.resume_validation_records rv
       join keyboard_wanderer.save_snapshots ss on ss.id = rv.snapshot_id
      where rv.run_id = $1
      order by rv.created_at desc
      limit 1`,
    [run.id]
  );
  assert.equal(resumeAudit.rowCount, 1);
  const resumeRecord = resumeAudit.rows[0];
  assert.equal(resumeRecord.validation_status, "accepted");
  assert.ok(Number(resumeRecord.attempt_no) >= 1);
  assert.equal(resumeRecord.observed_checksum_sha256, resumeRecord.snapshot_checksum);
  assert.equal(resumeRecord.observed_plan_hash, resumeRecord.snapshot_plan_hash);
  assert.equal(resumeRecord.observed_layout_hash, resumeRecord.snapshot_layout_hash);
  assert.equal(Number(resumeRecord.snapshot_run_version), abandoned.payload.run.version);
  assert.ok(Object.values(resumeRecord.checks_json).every((value) => value === true));
  assert.deepEqual(resumeRecord.errors_json, []);

  const sensitiveAudit = await application.store.pool.query(
    `select concat_ws('|',
              (select settings::text from keyboard_wanderer.campaigns where id = r.campaign_id),
              r.world_state::text,
              (select string_agg(concat_ws('|', map_json::text, generation_metadata::text), '|')
                 from keyboard_wanderer.worlds where campaign_id = r.campaign_id),
              (select string_agg(concat_ws('|', plan_json::text, validation_report::text, validation_errors::text), '|')
                 from keyboard_wanderer.run_generation_plans where run_id = r.id),
              (select string_agg(concat_ws('|', progress_state::text, rule_state::text, convergence_state::text), '|')
                 from keyboard_wanderer.run_progress_states where run_id = r.id),
              (select string_agg(concat_ws('|', normalized_attempt::text, state_delta::text, rng_audit::text), '|')
                 from keyboard_wanderer.turn_rule_resolutions where run_id = r.id),
              (select string_agg(concat_ws('|', state_json::text, resume_metadata::text), '|')
                 from keyboard_wanderer.save_snapshots where run_id = r.id)
            ) as persisted_json,
            (select string_agg(concat_ws('|', redacted_input_json::text, redacted_output_json::text), '|')
               from keyboard_wanderer.llm_logs where run_id = r.id) as persisted_logs
       from keyboard_wanderer.runs r
      where r.id = $1`,
    [run.id]
  );
  assert.equal(sensitiveAudit.rowCount, 1);
  const persistedJson = sensitiveAudit.rows[0].persisted_json || "";
  const persistedLogs = sensitiveAudit.rows[0].persisted_logs || "";
  assert.equal(typeof storedInitial.resolutionSeed, "string");
  assert.ok(storedInitial.resolutionSeed.length > 0);
  assert.equal(persistedJson.includes(SENSITIVE_API_KEY), false, "API keys must never enter persisted JSON");
  assert.equal(persistedLogs.includes(SENSITIVE_API_KEY), false, "API keys must never enter LLM logs");
  assert.equal(persistedJson.includes(storedInitial.resolutionSeed), false, "the resolution seed must be redacted from persisted JSON");
  assert.equal(persistedLogs.includes(storedInitial.resolutionSeed), false, "the resolution seed must be redacted from LLM logs");

  const normalized = await application.store.pool.query(
    `select count(*)::int as entity_count,
            count(distinct ep.area_id)::int as area_count,
            bool_and(ep.x between 0 and a.width - 1
                     and ep.y between 0 and a.height - 1) as all_local
       from keyboard_wanderer.entity_positions ep
       join keyboard_wanderer.areas a on a.id = ep.area_id
      where ep.run_id = $1`,
    [run.id]
  );
  assert.equal(normalized.rows[0].entity_count, storedInitial.entities.length);
  assert.ok(normalized.rows[0].area_count >= 6);
  assert.equal(normalized.rows[0].all_local, true);
  assert.ok(resumed.payload.run.travelTimeUnits > 0);
  assert.equal(resumed.payload.run.activeEncounter, null);

  const playerProjection = await application.store.pool.query(
    `select ep.x as local_x, ep.y as local_y, ep.facing,
            a.origin_x, a.origin_y, actor.energy, actor.max_energy,
            entity.state_json as entity_state
       from keyboard_wanderer.entity_positions ep
       join keyboard_wanderer.areas a on a.id = ep.area_id
       join keyboard_wanderer.actors actor on actor.entity_id = ep.entity_id
       join keyboard_wanderer.entities entity on entity.id = ep.entity_id
      where ep.run_id = $1 and ep.entity_id = $2 and ep.removed_at is null`,
    [run.id, resumed.payload.run.playerEntityId]
  );
  const apiPlayer = resumed.payload.run.entities.find((item) => item.id === resumed.payload.run.playerEntityId);
  assert.equal(Number(playerProjection.rows[0].local_x) + Number(playerProjection.rows[0].origin_x), apiPlayer.position.x);
  assert.equal(Number(playerProjection.rows[0].local_y) + Number(playerProjection.rows[0].origin_y), apiPlayer.position.y);
  assert.equal(playerProjection.rows[0].facing, apiPlayer.state.facing.toLowerCase());
  assert.equal(playerProjection.rows[0].entity_state.facing, apiPlayer.state.facing);
  assert.equal(Number(playerProjection.rows[0].energy), resumed.payload.run.focus);
  assert.equal(Number(playerProjection.rows[0].max_energy), resumed.payload.run.maxFocus);

  const inventoryProjection = await application.store.pool.query(
    `select it.quantity, it.state_json
       from keyboard_wanderer.inventories i
       join keyboard_wanderer.items it
         on it.inventory_id = i.id and it.owner_id = i.owner_id and it.run_id = i.run_id
      where i.run_id = $1 and i.owner_id = $2 and i.actor_id = $3
      order by it.slot_index`,
    [run.id, USER_ID, resumed.payload.run.playerEntityId]
  );
  assert.deepEqual(
    inventoryProjection.rows.map((row) => ({ ...row.state_json, quantity: Number(row.quantity) })),
    apiPlayer.state.inventory
  );

  const beforeRestartDto = JSON.parse(JSON.stringify(await application.service.getRun(USER_ID, run.id)));
  assert.deepEqual(addUnityContractAliases(structuredClone(beforeRestartDto)), resumed.payload.run);
  await application.close();
  primaryClosed = true;

  reopenedApplication = await createApplication(applicationOptions);
  const afterRestartDto = JSON.parse(JSON.stringify(await reopenedApplication.service.getRun(USER_ID, run.id)));
  assert.deepEqual(afterRestartDto, beforeRestartDto, "a fresh store process must reconstruct the identical public run DTO");
  assert.equal(afterRestartDto.worldId, "WORLD_CODRIA");
});

test("PostgreSQL persists ordered administrator access and the mapped Root clue", { skip: !databaseUrl }, async (t) => {
  const config = loadConfig({
    STORAGE: "postgres",
    DATABASE_URL: databaseUrl,
    DATABASE_SSL: "false",
    AUTH_MODE: "required",
    LOG_LEVEL: "silent"
  });
  const application = await createApplication({ config, d20Source: new FixedD20Source(20), logger: silentLogger, narrator: fakeNarrator });
  t.after(async () => {
    await application.store.pool.query("delete from keyboard_wanderer.profiles where id = $1", [ADMIN_USER_ID]);
    await application.close();
  });
  const campaign = await application.service.createCampaign(ADMIN_USER_ID, { worldSeed: 88, turnLimit: 40 });
  const created = await application.service.createRun(ADMIN_USER_ID, campaign.id, { worldSeed: 88, turnLimit: 40 });
  const activateBeat = (state, beatId) => {
    const activeIndex = state.requiredStoryBeats.findIndex((beat) => beat.id === beatId);
    assert.ok(activeIndex >= 0);
    state.requiredStoryBeats.forEach((beat, index) => {
      beat.status = index < activeIndex ? "completed" : index === activeIndex ? "active" : "pending";
    });
    state.currentStoryBeat = structuredClone(state.requiredStoryBeats[activeIndex]);
    state.currentAct = state.currentStoryBeat.phaseId;
    state.campaignPhase = state.currentStoryBeat.phaseId;
  };
  const stagePlayerNear = (state, target) => {
    const player = state.entities.find((entity) => entity.id === state.playerEntityId);
    player.position = { x: target.position.x - 1, y: target.position.y };
  };
  const commit = async ({ beatId, skillId, targetId, secondaryTargetId = null, destination = null, resolvesDebtEntryId = null, key }) => {
    const before = await application.store.getRun(ADMIN_USER_ID, created.id);
    const request = normalizeTurnRequest({
      inputType: "USE_SKILL",
      idempotencyKey: key,
      expectedRunVersion: before.version,
      skillId,
      targetIds: [targetId, secondaryTargetId].filter(Boolean),
      ...(destination ? { destination } : {}),
      ...(resolvesDebtEntryId ? { resolvesDebtEntryId } : {})
    });
    return application.store.commitTurn({
      ownerId: ADMIN_USER_ID,
      runId: created.id,
      idempotencyKey: key,
      requestFingerprint: turnFingerprint(request),
      expectedRunVersion: before.version,
      resolve: (state) => {
        activateBeat(state, beatId);
        stagePlayerNear(state, state.entities.find((entity) => entity.id === targetId));
        state.focus = state.maxFocus;
        return resolveTurn({ run: state, request, forcedD20: 20, now: `2026-07-17T00:00:0${state.currentTurn + 1}.000Z` });
      }
    });
  };

  let state = await application.store.getRun(ADMIN_USER_ID, created.id);
  const candidateFor = (level) => state.adminAccessCandidates
    .filter((candidate) => candidate.accessLevelId === level && candidate.skillId !== "CONNECT")
    .sort((left, right) => ({ COPY: 1, DELETE: 1, RESTORE: 3 }[left.skillId] - { COPY: 1, DELETE: 1, RESTORE: 3 }[right.skillId]))[0];
  const entityForCandidate = (candidate) => state.entities.find((entity) => entity.state?.candidateId === candidate.id);
  for (const [level, beatId, key] of [
    ["ADMIN_ACCESS_LEVEL_1", "beat.admin_access_1", "admin-access-v4-0001"],
    ["ADMIN_ACCESS_LEVEL_2", "beat.admin_access_2", "admin-access-v4-0002"]
  ]) {
    const candidate = candidateFor(level);
    assert.ok(candidate);
    const committed = await commit({ beatId, skillId: candidate.skillId, targetId: entityForCandidate(candidate).id, key });
    state = committed.run;
  }

  const clue = state.entities.find((entity) => entity.kind === "prop" && entity.cloneable && entity.state?.evidenceKey === "STORY_REVELATION");
  state = (await commit({ beatId: "beat.internal_cause", skillId: "SEARCH", targetId: clue.id, key: "root-clue-v4-0001" })).run;
  const recoveryPartner = state.entities.find((entity) => entity.active && entity.kind === "npc" && !entity.state?.disabled);
  assert.ok(recoveryPartner);
  state = (await commit({ beatId: "beat.technical_debt_return", skillId: "CONNECT", targetId: recoveryPartner.id, secondaryTargetId: state.playerEntityId, key: "debt-return-v4-0001" })).run;
  const finalCandidate = candidateFor("ADMIN_ACCESS_LEVEL_3");
  state = (await commit({ beatId: "beat.admin_access_3", skillId: finalCandidate.skillId, targetId: entityForCandidate(finalCandidate).id, key: "admin-access-v4-0003" })).run;

  const repairTarget = state.entities.find((entity) => entity.active && entity.kind === "enemy" && !entity.state?.adminAccessLevelId);
  assert.ok(repairTarget);
  state = (await commit({ beatId: "beat.root_system_entry", skillId: "SEARCH", targetId: repairTarget.id, key: "debt-target-reveal-v4-0001" })).run;
  state = (await commit({ beatId: "beat.root_system_entry", skillId: "DELETE", targetId: repairTarget.id, key: "debt-cause-v4-0001" })).run;
  const debtMetricBeforeRecovery = state.metrics.technicalDebt;
  const debtToResolve = state.technicalDebtEntries.find((entry) => entry.turnNo === state.currentTurn && entry.resolvedAt === null);
  assert.ok(debtToResolve);
  state = (await commit({
    beatId: "beat.root_system_entry",
    skillId: "RESTORE",
    targetId: repairTarget.id,
    resolvesDebtEntryId: debtToResolve.id,
    key: "debt-recovery-v4-0001"
  })).run;
  assert.equal(state.metrics.technicalDebt, debtMetricBeforeRecovery - 2);
  assert.equal(state.technicalDebtEntries.find((entry) => entry.id === debtToResolve.id).resolvedAt, state.currentTurn);

  assert.equal(state.adminAccessAcquisitionHistory.length, 3);
  assert.equal(state.canonicalFacts.find((fact) => fact.subject === "collapse_origin").value, true);
  const projection = await application.store.pool.query(
    `select * from keyboard_wanderer.run_admin_access_states where run_id = $1 and owner_id = $2`,
    [created.id, ADMIN_USER_ID]
  );
  assert.equal(Number(projection.rows[0].acquired_level_count), 3);
  assert.equal(projection.rows[0].all_admin_access_acquired, true);
  assert.equal(projection.rows[0].essential_clue_acquired, true);
  assert.equal(projection.rows[0].root_system_eligible, true);
  const persisted = await application.service.getRun(ADMIN_USER_ID, created.id);
  assert.equal(persisted.rootSystemGate.eligible, true);
  const history = await application.store.pool.query(
    `select admin_access_code from keyboard_wanderer.admin_access_acquisition_history where run_id = $1 order by turn_no`,
    [created.id]
  );
  assert.deepEqual(history.rows.map((row) => row.admin_access_code), ["ADMIN_ACCESS_LEVEL_1", "ADMIN_ACCESS_LEVEL_2", "ADMIN_ACCESS_LEVEL_3"]);
  const histories = await application.store.pool.query(
    `select
       (select count(*)::int from keyboard_wanderer.major_choices where run_id = $1) as major_choice_count,
       (select count(*)::int from keyboard_wanderer.region_outcomes where run_id = $1) as region_outcome_count,
       (select count(*)::int from keyboard_wanderer.ability_usage_history where run_id = $1) as ability_usage_count,
       (select count(*)::int from keyboard_wanderer.technical_debt_entries where run_id = $1 and debt_delta > 0) as debt_entry_count,
       (select count(*)::int from keyboard_wanderer.technical_debt_entries where run_id = $1 and resolved_at is not null and resolution_type = 'RECOVERY') as resolved_debt_count,
       (select count(*)::int from keyboard_wanderer.world_facts where run_id = $1 and predicate = 'ROOT_CAUSE_ESSENTIAL_CLUE_ACQUIRED' and object_json @> '{"acquired":true}'::jsonb) as clue_fact_count`,
    [created.id]
  );
  assert.equal(histories.rows[0].major_choice_count, 3);
  assert.equal(histories.rows[0].region_outcome_count, 3);
  assert.equal(histories.rows[0].ability_usage_count, 8);
  assert.ok(histories.rows[0].debt_entry_count >= 1);
  assert.equal(histories.rows[0].resolved_debt_count, 1);
  assert.equal(histories.rows[0].clue_fact_count, 1);
});

test("PostgreSQL preserves collocated dormant slot reservations with exclusive live occupancy", { skip: !databaseUrl }, async (t) => {
  const config = loadConfig({
    STORAGE: "postgres",
    DATABASE_URL: databaseUrl,
    DATABASE_SSL: "false",
    AUTH_MODE: "required",
    LOG_LEVEL: "silent"
  });
  const application = await createApplication({ config, d20Source: new FixedD20Source(20), logger: silentLogger, narrator: fakeNarrator });
  t.after(async () => {
    await application.store.pool.query("delete from keyboard_wanderer.profiles where id = $1", [SLOT_USER_ID]);
    await application.close();
  });

  const campaign = await application.service.createCampaign(SLOT_USER_ID, { worldSeed: 88, turnLimit: 40 });
  const created = await application.service.createRun(SLOT_USER_ID, campaign.id, { worldSeed: 88, turnLimit: 40 });
  const stored = await application.store.getRun(SLOT_USER_ID, created.id);
  const expectedBoundEntities = stored.entities.filter((entity) => entity.state?.slotId);
  const sharedDomainSlots = new Map();
  for (const entity of expectedBoundEntities) {
    const entities = sharedDomainSlots.get(entity.state.slotId) || [];
    entities.push(entity);
    sharedDomainSlots.set(entity.state.slotId, entities);
  }
  const sharedDormantSlots = [...sharedDomainSlots.entries()]
    .filter(([, entities]) => entities.length > 1 && entities.every((entity) => !entity.active));
  assert.ok(sharedDormantSlots.length > 0, "fixture must exercise multiple dormant candidates reserved for one sealed slot");

  const bindings = await application.store.pool.query(
    `select ps.slot_key, rsb.entity_id, rsb.binding_key, rsb.status, rsb.activation_turn
       from keyboard_wanderer.run_slot_bindings rsb
       join keyboard_wanderer.placement_slots ps on ps.id = rsb.slot_id
      where rsb.run_id = $1 and rsb.owner_id = $2`,
    [created.id, SLOT_USER_ID]
  );
  assert.equal(bindings.rowCount, expectedBoundEntities.length);
  assert.equal(new Set(bindings.rows.map((row) => row.binding_key)).size, bindings.rowCount);
  for (const [slotKey, entities] of sharedDormantSlots) {
    const rows = bindings.rows.filter((row) => row.slot_key === slotKey);
    assert.equal(rows.length, entities.length);
    assert.ok(rows.every((row) => row.status === "reserved" && row.activation_turn === null));
  }
  const duplicateLiveOccupants = await application.store.pool.query(
    `select slot_id
       from keyboard_wanderer.run_slot_bindings
      where run_id = $1 and status in ('active', 'fulfilled')
      group by slot_id having count(*) > 1`,
    [created.id]
  );
  assert.equal(duplicateLiveOccupants.rowCount, 0);

  const [activationSlotKey, activationCandidates] = sharedDormantSlots.find(([slotKey]) => {
    const slot = stored.world.placementSlots.find((candidate) => candidate.id === slotKey);
    return slot && !stored.entities.some((entity) => entity.active
      && entity.position.x === slot.x && entity.position.y === slot.y);
  });
  const activatedEntityId = activationCandidates[0].id;
  const activationKey = "slot-reservation-activation-0001";
  await application.store.commitTurn({
    ownerId: SLOT_USER_ID,
    runId: created.id,
    idempotencyKey: activationKey,
    requestFingerprint: "slot-reservation-activation-fingerprint",
    expectedRunVersion: stored.version,
    resolve: (run) => {
      const createdAt = "2026-07-22T11:00:01.000Z";
      const gameEntity = run.entities.find((entity) => entity.id === activatedEntityId);
      const slot = run.world.placementSlots.find((candidate) => candidate.id === activationSlotKey);
      gameEntity.active = true;
      gameEntity.blocking = true;
      gameEntity.position = { x: slot.x, y: slot.y };
      gameEntity.state = {
        ...gameEntity.state,
        dormant: false,
        activationState: "ACTIVE",
        activatedDecisionNo: 1,
        slotId: slot.id
      };
      run.currentTurn = 1;
      run.version += 1;
      run.updatedAt = createdAt;
      return {
        run,
        turn: {
          id: randomUUID(),
          runId: run.id,
          ownerId: run.ownerId,
          turnNo: 1,
          committedRunVersion: run.version,
          request: { inputType: "NARRATIVE_CHOICE", idempotencyKey: activationKey, expectedRunVersion: stored.version },
          resolutionMode: "NONE",
          actionContext: "NARRATIVE",
          events: [
            { type: "entity_activated", entityId: gameEntity.id, position: { ...gameEntity.position }, activationSlotId: slot.id },
            { type: "turn_committed", turnNo: 1 }
          ],
          stateDelta: { facts: [], relationships: [], adminAccessHistory: [], majorChoices: [], regionOutcomes: [], openLoops: [], technicalDebtEntries: [] },
          narrative: {
            summary: "예약된 적 하나가 슬롯에서 활성화됐다.",
            body: "한 후보만 모습을 드러냈고 나머지 후보는 대기 상태를 유지했다.",
            dialogue: [], proposedOps: [], appliedOps: [], rejectedOps: [],
            fallbackUsed: true, model: "deterministic-slot-activation"
          },
          createdAt
        }
      };
    }
  });
  const activatedBindings = await application.store.pool.query(
    `select rsb.entity_id, rsb.status, rsb.activation_turn, e.is_active
       from keyboard_wanderer.run_slot_bindings rsb
       join keyboard_wanderer.placement_slots ps on ps.id = rsb.slot_id
       join keyboard_wanderer.entities e on e.id = rsb.entity_id
      where rsb.run_id = $1 and ps.slot_key = $2
      order by rsb.entity_id`,
    [created.id, activationSlotKey]
  );
  const activatedBinding = activatedBindings.rows.find((row) => row.entity_id === activatedEntityId);
  assert.equal(activatedBinding.status, "active");
  assert.equal(Number(activatedBinding.activation_turn), 1);
  assert.equal(activatedBinding.is_active, true);
  assert.ok(activatedBindings.rows
    .filter((row) => row.entity_id !== activatedEntityId)
    .every((row) => row.status === "reserved" && row.activation_turn === null && row.is_active === false));
});

test("PostgreSQL ambient wander persists database area UUIDs and area-local coordinates", { skip: !databaseUrl }, async (t) => {
  const config = loadConfig({
    STORAGE: "postgres",
    DATABASE_URL: databaseUrl,
    DATABASE_SSL: "false",
    AUTH_MODE: "required",
    LOG_LEVEL: "silent"
  });
  const application = await createApplication({ config, d20Source: new FixedD20Source(20), logger: silentLogger, narrator: fakeNarrator });
  t.after(async () => {
    await application.store.pool.query("delete from keyboard_wanderer.profiles where id = $1", [AMBIENT_USER_ID]);
    await application.close();
  });

  const campaign = await application.service.createCampaign(AMBIENT_USER_ID, { worldSeed: 22017, turnLimit: 40 });
  const created = await application.service.createRun(AMBIENT_USER_ID, campaign.id, { worldSeed: 22017, turnLimit: 40 });
  const wandered = await application.service.ambientWander(AMBIENT_USER_ID, created.id, {
    expectedRunVersion: created.version,
    minX: 0,
    minY: 0,
    maxX: 159,
    maxY: 159
  });
  assert.ok(wandered.movedEntityIds.length > 0);
  assert.equal(wandered.run.version, created.version + 1);

  const positions = await application.store.pool.query(
    `select ep.entity_id, ep.x, ep.y, ep.area_id,
            a.area_key, a.origin_x, a.origin_y, a.width, a.height
       from keyboard_wanderer.entity_positions ep
       join keyboard_wanderer.areas a
         on a.id = ep.area_id and a.owner_id = ep.owner_id and a.world_id = ep.world_id
      where ep.run_id = $1 and ep.owner_id = $2 and ep.entity_id = any($3::uuid[])`,
    [created.id, AMBIENT_USER_ID, wandered.movedEntityIds]
  );
  assert.equal(positions.rowCount, wandered.movedEntityIds.length);
  for (const row of positions.rows) {
    const entity = wandered.run.entities.find((candidate) => candidate.id === row.entity_id);
    assert.ok(entity);
    assert.match(row.area_id, /^[0-9a-f-]{36}$/i);
    assert.equal(Number(row.x) + Number(row.origin_x), entity.position.x);
    assert.equal(Number(row.y) + Number(row.origin_y), entity.position.y);
    assert.equal(row.area_key, areaAt(wandered.run.world, entity.position).id);
    assert.ok(Number(row.x) >= 0 && Number(row.x) < Number(row.width));
    assert.ok(Number(row.y) >= 0 && Number(row.y) < Number(row.height));
  }
  const reloaded = await application.store.getRun(AMBIENT_USER_ID, created.id);
  for (const entityId of wandered.movedEntityIds) {
    assert.deepEqual(
      reloaded.entities.find((entity) => entity.id === entityId).position,
      wandered.run.entities.find((entity) => entity.id === entityId).position
    );
  }
});

test("PostgreSQL keeps actor energy and split inventory stacks equal to authoritative JSON", { skip: !databaseUrl }, async (t) => {
  const config = loadConfig({
    STORAGE: "postgres", DATABASE_URL: databaseUrl, DATABASE_SSL: "false",
    AUTH_MODE: "required", LOG_LEVEL: "silent"
  });
  const application = await createApplication({ config, d20Source: new FixedD20Source(20), logger: silentLogger, narrator: fakeNarrator });
  t.after(async () => {
    await application.store.pool.query("delete from keyboard_wanderer.profiles where id = $1", [PROJECTION_USER_ID]);
    await application.close();
  });
  const campaign = await application.service.createCampaign(PROJECTION_USER_ID, { worldSeed: 22019, turnLimit: 40 });
  const created = await application.service.createRun(PROJECTION_USER_ID, campaign.id, { worldSeed: 22019, turnLimit: 40 });
  const consumableId = randomUUID();
  const transferId = randomUUID();
  const staged = await application.store.commitRunMutation({
    ownerId: PROJECTION_USER_ID,
    runId: created.id,
    expectedRunVersion: created.version,
    resolve: (state) => {
      const player = state.entities.find((entity) => entity.id === state.playerEntityId);
      const npc = state.entities.find((entity) => entity.active && entity.kind === "npc");
      const occupied = new Set(state.entities.filter((entity) => entity.active && entity.blocking && entity.id !== npc.id)
        .map((entity) => `${entity.position.x},${entity.position.y}`));
      const adjacent = [[1, 0], [0, 1], [-1, 0], [0, -1]]
        .map(([dx, dy]) => ({ x: player.position.x + dx, y: player.position.y + dy }))
        .find((point) => isWalkable(state.world, point) && !occupied.has(`${point.x},${point.y}`));
      assert.ok(adjacent, "the player must have one free adjacent tile for inventory transfer");
      npc.position = adjacent;
      npc.state.inventory = [];
      state.focus = state.maxFocus - 3;
      player.state.inventory.push({
        id: consumableId, kind: "consumable", name: "집중 조각", description: "집중력을 회복한다.",
        quantity: 2, protected: false, effect: "restore_focus", effectValue: 2, acquiredTurn: 0, source: "projection_test"
      });
      player.state.inventory.push({
        id: transferId, kind: "material", name: "분할 파편", description: "나눠 건넬 수 있다.",
        quantity: 2, protected: false, acquiredTurn: 0, source: "projection_test"
      });
      state.version += 1;
      state.updatedAt = "2026-07-22T01:00:00.000Z";
      return { run: state, inventoryAction: null };
    }
  });
  const npc = staged.run.entities.find((entity) => entity.active && entity.kind === "npc"
    && Array.isArray(entity.state.inventory));
  const used = await application.service.mutateInventory(PROJECTION_USER_ID, created.id, {
    action: "USE", itemId: consumableId, quantity: 1, expectedRunVersion: staged.run.version
  });
  const dropped = await application.service.mutateInventory(PROJECTION_USER_ID, created.id, {
    action: "DROP", itemId: consumableId, quantity: 1, expectedRunVersion: used.run.version
  });
  const transferred = await application.service.mutateInventory(PROJECTION_USER_ID, created.id, {
    action: "TRANSFER_OUT", itemId: transferId, otherEntityId: npc.id,
    quantity: 1, expectedRunVersion: dropped.run.version
  });

  const actorEnergy = await application.store.pool.query(
    `select energy, max_energy from keyboard_wanderer.actors
      where run_id = $1 and owner_id = $2 and entity_id = $3`,
    [created.id, PROJECTION_USER_ID, created.playerEntityId]
  );
  assert.equal(Number(actorEnergy.rows[0].energy), transferred.run.focus);
  assert.equal(Number(actorEnergy.rows[0].max_energy), transferred.run.maxFocus);
  const inventoryRows = await application.store.pool.query(
    `select i.actor_id, it.quantity, it.state_json
       from keyboard_wanderer.inventories i
       join keyboard_wanderer.items it
         on it.inventory_id = i.id and it.owner_id = i.owner_id and it.run_id = i.run_id
      where i.run_id = $1 and i.owner_id = $2 and it.state_json->>'id' in ($3, $4)
      order by i.actor_id`,
    [created.id, PROJECTION_USER_ID, consumableId, transferId]
  );
  assert.equal(inventoryRows.rows.some((row) => row.state_json.id === consumableId), false);
  const transferredRows = inventoryRows.rows.filter((row) => row.state_json.id === transferId);
  assert.equal(transferredRows.length, 2);
  assert.deepEqual(new Set(transferredRows.map((row) => row.actor_id)), new Set([created.playerEntityId, npc.id]));
  assert.deepEqual(transferredRows.map((row) => Number(row.quantity)).sort(), [1, 1]);
});

test("PostgreSQL commits active turn 31 when turnLimit 30 is only a soft horizon", { skip: !databaseUrl }, async (t) => {
  const config = loadConfig({
    STORAGE: "postgres",
    DATABASE_URL: databaseUrl,
    DATABASE_SSL: "false",
    AUTH_MODE: "required",
    LOG_LEVEL: "silent"
  });
  const application = await createApplication({ config, d20Source: new FixedD20Source(20), logger: silentLogger, narrator: fakeNarrator });
  t.after(async () => {
    await application.store.pool.query("delete from keyboard_wanderer.profiles where id = $1", [SOFT_HORIZON_USER_ID]);
    await application.close();
  });

  const campaign = await application.service.createCampaign(SOFT_HORIZON_USER_ID, { worldSeed: 33031, turnLimit: 30 });
  const created = await application.service.createRun(SOFT_HORIZON_USER_ID, campaign.id, { worldSeed: 33031, turnLimit: 30 });

  // Advance the authoritative cursor one legal transition at a time.  The
  // focused test then exercises the complete PostgresStore commit path at 31,
  // including the turn/event ledgers, progress projection, and deep snapshot.
  await application.store.withOwner(SOFT_HORIZON_USER_ID, async (client) => {
    for (let turnNo = 1; turnNo <= 30; turnNo += 1) {
      const advanced = await client.query(
        `update keyboard_wanderer.runs
            set version = version + 1,
                current_turn = current_turn + 1,
                world_state = jsonb_set(
                  jsonb_set(world_state, '{version}', to_jsonb(version + 1), true),
                  '{currentTurn}', to_jsonb(current_turn + 1), true
                ),
                updated_at = clock_timestamp()
          where id = $1 and owner_id = $2
            and current_turn = $3 and version = $4
        returning version, current_turn`,
        [created.id, SOFT_HORIZON_USER_ID, turnNo - 1, turnNo]
      );
      assert.equal(advanced.rowCount, 1);
      const progress = await client.query(
        `update keyboard_wanderer.run_progress_states
            set last_turn_no = $3, state_version = state_version + 1,
                updated_at = clock_timestamp()
          where run_id = $1 and owner_id = $2
        returning last_turn_no`,
        [created.id, SOFT_HORIZON_USER_ID, turnNo]
      );
      assert.equal(progress.rowCount, 1);
    }
  });

  const before = await application.store.getRun(SOFT_HORIZON_USER_ID, created.id);
  assert.equal(before.currentTurn, 30);
  assert.equal(before.turnLimit, 30);
  assert.equal(before.status, "active");
  const idempotencyKey = "soft-horizon-turn-0031";
  const requestFingerprint = "soft-horizon-turn-0031-fingerprint";
  const committed = await application.store.commitTurn({
    ownerId: SOFT_HORIZON_USER_ID,
    runId: created.id,
    idempotencyKey,
    requestFingerprint,
    expectedRunVersion: before.version,
    resolve: (run) => {
      const createdAt = "2026-07-22T12:00:31.000Z";
      run.currentTurn += 1;
      run.version += 1;
      run.updatedAt = createdAt;
      return {
        run,
        turn: {
          id: randomUUID(),
          runId: run.id,
          ownerId: run.ownerId,
          turnNo: run.currentTurn,
          committedRunVersion: run.version,
          request: {
            inputType: "NARRATIVE_CHOICE",
            idempotencyKey,
            expectedRunVersion: before.version
          },
          resolutionMode: "NONE",
          actionContext: "NARRATIVE",
          events: [{ type: "turn_committed", turnNo: run.currentTurn }],
          stateDelta: {
            facts: [],
            relationships: [],
            adminAccessHistory: [],
            majorChoices: [],
            regionOutcomes: [],
            openLoops: [],
            technicalDebtEntries: []
          },
          narrative: {
            summary: "소프트 한계를 넘어 이야기가 계속됐다.",
            body: "서른 번째 턴은 결말을 강제하지 않았다. 다음 선택이 정상적으로 기록됐다.",
            dialogue: [],
            proposedOps: [],
            appliedOps: [],
            rejectedOps: [],
            fallbackUsed: true,
            model: "deterministic-soft-horizon"
          },
          createdAt
        }
      };
    }
  });

  assert.equal(committed.run.currentTurn, 31);
  assert.equal(committed.run.status, "active");
  assert.equal(committed.turn.turnNo, 31);
  const persisted = await application.store.pool.query(
    `select r.current_turn, r.turn_limit, r.status,
            ps.last_turn_no,
            tr.status as turn_status,
            ss.current_turn as snapshot_turn
       from keyboard_wanderer.runs r
       join keyboard_wanderer.run_progress_states ps on ps.run_id = r.id
       join keyboard_wanderer.turn_records tr on tr.run_id = r.id and tr.turn_no = 31
       join keyboard_wanderer.save_snapshots ss
         on ss.run_id = r.id and ss.run_version = r.version
      where r.id = $1 and r.owner_id = $2`,
    [created.id, SOFT_HORIZON_USER_ID]
  );
  assert.equal(persisted.rowCount, 1);
  assert.equal(Number(persisted.rows[0].current_turn), 31);
  assert.equal(Number(persisted.rows[0].turn_limit), 30);
  assert.equal(persisted.rows[0].status, "playing");
  assert.equal(Number(persisted.rows[0].last_turn_no), 31);
  assert.equal(persisted.rows[0].turn_status, "committed");
  assert.equal(Number(persisted.rows[0].snapshot_turn), 31);
});

test("PostgreSQL atomically projects a defeated Cache Replicator and its spawned copy", { skip: !databaseUrl }, async (t) => {
  const config = loadConfig({
    STORAGE: "postgres",
    DATABASE_URL: databaseUrl,
    DATABASE_SSL: "false",
    AUTH_MODE: "required",
    LOG_LEVEL: "silent"
  });
  const application = await createApplication({
    config,
    d20Source: new FixedD20Source(20),
    logger: silentLogger,
    narrator: fakeNarrator
  });
  t.after(async () => {
    await application.store.pool.query("delete from keyboard_wanderer.profiles where id = $1", [CACHE_REPLICA_USER_ID]);
    await application.close();
  });

  const campaign = await application.service.createCampaign(CACHE_REPLICA_USER_ID,
    { worldSeed: 20260719, turnLimit: 40 });
  const created = await application.service.createRun(CACHE_REPLICA_USER_ID, campaign.id,
    { worldSeed: 20260719, turnLimit: 40 });
  const before = await application.store.getRun(CACHE_REPLICA_USER_ID, created.id);
  const player = before.entities.find((entity) => entity.id === before.playerEntityId);
  const target = before.entities.find((entity) => entity.kind === "enemy");
  assert.ok(player && target, "the generated run must contain a projected player and enemy");

  const occupied = new Set(before.entities
    .filter((entity) => entity.active && entity.blocking && entity.id !== player.id && entity.id !== target.id)
    .map((entity) => `${entity.position.x},${entity.position.y}`));
  const targetPosition = [[1, 0], [0, 1], [-1, 0], [0, -1]]
    .map(([dx, dy]) => ({ x: player.position.x + dx, y: player.position.y + dy }))
    .find((point) => isWalkable(before.world, point) && !occupied.has(`${point.x},${point.y}`));
  assert.ok(targetPosition, "the player must expose one adjacent combat tile");

  const cacheAssetId = "enemy.mushroom-blue.v1";

  const idempotencyKey = "postgres-cache-replicator-delete-0001";
  const request = normalizeTurnRequest({
    inputType: "USE_SKILL",
    idempotencyKey,
    expectedRunVersion: before.version,
    skillId: "DELETE",
    targetIds: [target.id]
  });
  const committed = await application.store.commitTurn({
    ownerId: CACHE_REPLICA_USER_ID,
    runId: before.id,
    idempotencyKey,
    requestFingerprint: turnFingerprint(request),
    expectedRunVersion: before.version,
    resolve: (run) => {
      const livePlayer = run.entities.find((entity) => entity.id === run.playerEntityId);
      const liveTarget = run.entities.find((entity) => entity.id === target.id);
      livePlayer.position = { ...player.position };
      liveTarget.assetId = cacheAssetId;
      liveTarget.position = { ...targetPosition };
      liveTarget.active = true;
      liveTarget.blocking = true;
      liveTarget.protected = false;
      liveTarget.state = { ...liveTarget.state, hp: 3, maxHp: 4, revealed: false,
        cacheReplicated: false, dormant: false, activationState: "ACTIVE" };
      return resolveTurn({
        run,
        request,
        d20Source: new FixedD20Source(20),
        skillSelection: { kind: "entity", entityIds: [liveTarget.id] },
        now: "2026-07-22T12:34:56.000Z"
      });
    }
  });

  const spawnEvent = committed.turn.events.find((event) => event.type === "entity_spawned" &&
    event.sourceEntityId === target.id);
  assert.ok(spawnEvent, "the committed turn must publish the cache copy as an entity spawn");
  const projection = await application.store.pool.query(
    `select e.source_entity_id, e.is_active, a.hp, ep.removed_at
       from keyboard_wanderer.entities e
       join keyboard_wanderer.actors a
         on a.entity_id = e.id and a.owner_id = e.owner_id and a.run_id = e.run_id
       join keyboard_wanderer.entity_positions ep
         on ep.entity_id = e.id and ep.owner_id = e.owner_id and ep.run_id = e.run_id
      where e.id = $1 and e.run_id = $2 and e.owner_id = $3`,
    [spawnEvent.entityId, before.id, CACHE_REPLICA_USER_ID]
  );
  assert.equal(projection.rowCount, 1);
  assert.equal(projection.rows[0].source_entity_id, target.id);
  assert.equal(projection.rows[0].is_active, true);
  assert.equal(Number(projection.rows[0].hp), 3);
  assert.equal(projection.rows[0].removed_at, null);

  const originalProjection = await application.store.pool.query(
    `select is_active, despawned_turn from keyboard_wanderer.entities
      where id = $1 and run_id = $2 and owner_id = $3`,
    [target.id, before.id, CACHE_REPLICA_USER_ID]
  );
  assert.equal(originalProjection.rows[0].is_active, false);
  assert.equal(Number(originalProjection.rows[0].despawned_turn), committed.run.currentTurn);
});

test("PostgreSQL projects a technical-debt backflow hostile in the same authoritative commit", { skip: !databaseUrl }, async (t) => {
  const config = loadConfig({
    STORAGE: "postgres",
    DATABASE_URL: databaseUrl,
    DATABASE_SSL: "false",
    AUTH_MODE: "required",
    LOG_LEVEL: "silent"
  });
  const application = await createApplication({ config, d20Source: new FixedD20Source(20), logger: silentLogger, narrator: fakeNarrator });
  t.after(async () => {
    await application.store.pool.query("delete from keyboard_wanderer.profiles where id = $1", [DEBT_BACKFLOW_USER_ID]);
    await application.close();
  });

  const campaign = await application.service.createCampaign(DEBT_BACKFLOW_USER_ID, { worldSeed: 44052, turnLimit: 40 });
  const created = await application.service.createRun(DEBT_BACKFLOW_USER_ID, campaign.id, { worldSeed: 44052, turnLimit: 40 });
  const before = await application.store.getRun(DEBT_BACKFLOW_USER_ID, created.id);
  const player = before.entities.find((entity) => entity.id === before.playerEntityId);
  const occupied = new Set(before.entities.filter((entity) => entity.active && entity.blocking)
    .map((entity) => `${entity.position.x},${entity.position.y}`));
  const position = [[1, 0], [0, 1], [-1, 0], [0, -1]]
    .map(([dx, dy]) => ({ x: player.position.x + dx, y: player.position.y + dy }))
    .find((point) => isWalkable(before.world, point) && !occupied.has(`${point.x},${point.y}`));
  assert.ok(position, "fixture must expose one adjacent backflow spawn tile");

  const idempotencyKey = "postgres-debt-backflow-spawn-0001";
  const request = normalizeTurnRequest({
    inputType: "USE_SKILL",
    idempotencyKey,
    expectedRunVersion: before.version,
    skillId: "REST",
    targetIds: []
  });
  const committed = await application.store.commitTurn({
    ownerId: DEBT_BACKFLOW_USER_ID,
    runId: before.id,
    idempotencyKey,
    requestFingerprint: turnFingerprint(request),
    expectedRunVersion: before.version,
    resolve: (run) => {
      run.metrics.technicalDebt = 50;
      run.debtThresholdsTriggered = [25];
      run.focus = Math.max(0, run.maxFocus - 1);
      return resolveTurn({
        run,
        request,
        d20Source: new FixedD20Source(20),
        now: "2026-07-22T13:45:00.000Z"
      });
    }
  });

  const spawnEvent = committed.turn.events.find((event) => event.type === "debt_backflow_hostile_spawned");
  assert.ok(spawnEvent, "crossing the threshold must publish the hostile spawn event");
  const entityId = spawnEvent.entityId;
  assert.equal(committed.run.entities.some((entity) => entity.id === entityId), true);
  const projection = await application.store.pool.query(
    `select e.is_active, a.hp, ep.removed_at
       from keyboard_wanderer.entities e
       join keyboard_wanderer.actors a on a.entity_id = e.id and a.run_id = e.run_id and a.owner_id = e.owner_id
       join keyboard_wanderer.entity_positions ep on ep.entity_id = e.id and ep.run_id = e.run_id and ep.owner_id = e.owner_id
      where e.id = $1 and e.run_id = $2 and e.owner_id = $3`,
    [entityId, before.id, DEBT_BACKFLOW_USER_ID]
  );
  assert.equal(projection.rowCount, 1);
  assert.equal(projection.rows[0].is_active, true);
  assert.equal(Number(projection.rows[0].hp), 3);
  assert.equal(projection.rows[0].removed_at, null);
});
