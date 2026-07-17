import test from "node:test";
import assert from "node:assert/strict";
import { createApplication } from "../src/app.js";
import { loadConfig } from "../src/config.js";
import {
  FixedD20Source,
  normalizeTravelRequest,
  normalizeTurnRequest,
  resolveSafeTravel,
  resolveTurn,
  turnFingerprint
} from "../src/domain/turn-engine.js";
import { TILE, areaAt, isWalkable } from "../src/domain/world.js";

const databaseUrl = process.env.TEST_DATABASE_URL;
const USER_ID = "44444444-4444-4444-8444-444444444444";
const ADMIN_USER_ID = "44444444-4444-4444-8444-444444444445";
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
  const hostiles = storedInitial.entities.filter((item) => item.active && item.kind === "enemy");
  const deletionTargets = storedInitial.entities
    .filter((item) => item.active && item.kind === "prop" && !item.blocking
      && !item.protected && !item.state?.adminAccessLevelId
      && storedInitial.world.tiles[item.position.y * storedInitial.world.width + item.position.x] !== TILE.HAZARD
      && !hostiles.some((hostile) => distance(hostile.position, item.position) <= 1))
    .sort((left, right) => distance(left.position, playerBefore.position) - distance(right.position, playerBefore.position));
  const deletionTarget = deletionTargets.find((candidate) => {
    const destination = {
      areaId: areaAt(storedInitial.world, candidate.position).id,
      ...candidate.position
    };
    try {
      const probe = resolveSafeTravel({
        run: structuredClone(storedInitial),
        request: normalizeTravelRequest({
          inputType: "MOVE",
          idempotencyKey: `postgres-probe-${candidate.id}`,
          expectedRunVersion: 1,
          destination
        }),
        now: "2026-07-17T00:00:00.000Z"
      });
      return probe.navigation.encounterOpened === false
        && probe.navigation.to.areaId === destination.areaId
        && probe.navigation.to.x === destination.x
        && probe.navigation.to.y === destination.y;
    } catch {
      return false;
    }
  });
  assert.ok(deletionTarget);
  const staging = { ...deletionTarget.position };
  assert.equal(isWalkable(storedInitial.world, staging), true);
  const travelRequest = {
    inputType: "MOVE",
    idempotencyKey: "postgres-travel-0001",
    expectedRunVersion: 1,
    destination: { areaId: areaAt(storedInitial.world, staging).id, ...staging },
    playerNote: "가까운 편집 대상으로 이동"
  };
  const travelled = await request(`/v1/runs/${run.id}/travel`, travelRequest);
  assert.equal(travelled.response.status, 201);
  assert.equal(travelled.payload.run.currentTurn, 0);
  assert.equal(travelled.payload.run.version, 2);
  assert.ok(travelled.payload.run.travelTimeUnits > 0);
  assert.equal(travelled.payload.navigation.campaignTurnConsumed, false);
  assert.equal(travelled.payload.navigation.encounterOpened, false);
  assert.deepEqual(travelled.payload.navigation.to, travelRequest.destination);
  const travelReplay = await request(`/v1/runs/${run.id}/travel`, travelRequest);
  assert.equal(travelReplay.response.status, 200);
  assert.equal(travelReplay.payload.fromIdempotencyCache, true);
  assert.deepEqual(travelReplay.payload.run, travelled.payload.run, "first commit and idempotent replay must return the same persisted run DTO");
  for (const field of ["id", "sequence", "requestedDestination", "to", "path", "pathCost", "travelTimeUnits", "cumulativeTravelTimeUnits", "enteredAreaId", "enteredBiomeId", "campaignRole", "traversedAreaIds", "reachedPoiIds", "encounterOpened", "encounter", "campaignTurnConsumed", "campaignTurnBefore", "campaignTurnAfter", "layoutHash"]) {
    assert.deepEqual(travelReplay.payload.navigation[field], travelled.payload.navigation[field], `travel replay field ${field}`);
  }

  const turnRequest = {
    inputType: "USE_SKILL",
    idempotencyKey: "postgres-turn-0001",
    expectedRunVersion: 2,
    skillId: "DELETE",
    targetIds: [deletionTarget.id]
  };
  const committed = await request(`/v1/runs/${run.id}/actions`, turnRequest);
  assert.equal(committed.response.status, 201);
  assert.equal(committed.payload.run.version, 3);
  assert.equal(committed.payload.run.currentTurn, 1);
  assert.equal(committed.payload.run.activeEncounter, null);
  assert.equal(committed.payload.turn.actionContext, "INVESTIGATION");
  assert.equal(committed.payload.run.entities.some((item) => item.id === deletionTarget.id), false);
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
  assert.equal(ledger.rows[0].skill_id, "DELETE");
  assert.deepEqual(ledger.rows[0].target_ids, [deletionTarget.id]);
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
        (select count(*)::int from keyboard_wanderer.ability_usage_history au where au.run_id = r.id and au.skill_id = 'DELETE') as ability_count,
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
  assert.equal(codriaLedger.rows[0].debt_count, 1);
  assert.ok(codriaLedger.rows[0].relationship_count >= 6);
  assert.equal(codriaLedger.rows[0].fact_count, storedInitial.canonicalFacts.length);
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
    `select ep.x as local_x, ep.y as local_y, a.origin_x, a.origin_y
       from keyboard_wanderer.entity_positions ep
       join keyboard_wanderer.areas a on a.id = ep.area_id
      where ep.run_id = $1 and ep.entity_id = $2 and ep.removed_at is null`,
    [run.id, resumed.payload.run.playerEntityId]
  );
  const apiPlayer = resumed.payload.run.entities.find((item) => item.id === resumed.payload.run.playerEntityId);
  assert.equal(Number(playerProjection.rows[0].local_x) + Number(playerProjection.rows[0].origin_x), apiPlayer.position.x);
  assert.equal(Number(playerProjection.rows[0].local_y) + Number(playerProjection.rows[0].origin_y), apiPlayer.position.y);

  const beforeRestartDto = JSON.parse(JSON.stringify(await application.service.getRun(USER_ID, run.id)));
  assert.deepEqual(beforeRestartDto, resumed.payload.run);
  await application.close();
  primaryClosed = true;

  reopenedApplication = await createApplication(applicationOptions);
  const afterRestartDto = JSON.parse(JSON.stringify(await reopenedApplication.service.getRun(USER_ID, run.id)));
  assert.deepEqual(afterRestartDto, beforeRestartDto, "a fresh store process must reconstruct the identical public run DTO");
});
