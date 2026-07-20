import { AppError, notFound } from "../errors.js";
import { clone, fingerprint } from "../domain/serialization.js";
import { areaAt } from "../domain/world.js";
import { WORLD_CODRIA } from "../domain/codria-contract.js";

const SCHEMA = "keyboard_wanderer";

export async function createPostgresStore({ connectionString, ssl = false } = {}) {
  let pg;
  try {
    pg = await import("pg");
  } catch {
    throw new AppError(500, "POSTGRES_DRIVER_MISSING", "Install Server dependencies before using postgres storage.");
  }
  const pool = new pg.Pool({
    connectionString,
    ssl: ssl ? { rejectUnauthorized: true } : false,
    max: 10,
    idleTimeoutMillis: 30_000,
    connectionTimeoutMillis: 5_000,
    application_name: "codria-v4-game-server"
  });
  return new PostgresStore(pool);
}

export class PostgresStore {
  constructor(pool) {
    this.pool = pool;
  }

  async health() {
    await this.pool.query("select 1 as ok");
    return { ok: true, storage: "postgres" };
  }

  async createCampaign(campaign) {
    return this.withOwner(campaign.ownerId, async (client) => {
      await client.query(
        `insert into ${SCHEMA}.profiles (id, display_name)
         values ($1, 'Codria Player')
         on conflict (id) do nothing`,
        [campaign.ownerId]
      );
      const campaignResult = await client.query(
        `insert into ${SCHEMA}.campaigns
           (id, owner_id, title, world_seed, turn_limit, status, ruleset_version, premise, settings, created_at, updated_at)
         values ($1, $2, $3, $4, $5, 'active', $6, $7, $8::jsonb, $9, $9)
         returning *`,
        [campaign.id, campaign.ownerId, campaign.title, campaign.worldSeed, campaign.turnLimit,
          campaign.templateVersion, campaign.premise, JSON.stringify(campaignSettingsForDatabase(campaign)), campaign.createdAt]
      );
      const worldResult = await client.query(
        `insert into ${SCHEMA}.worlds
           (campaign_id, owner_id, world_scope, run_scope_key, generator_version, layout_hash, width, height, map_json, generation_metadata, generated_at, created_at, updated_at)
         values ($1, $2, 'campaign_preview', null, $3, $4, $5, $6, $7::jsonb, $8::jsonb, $9, $9, $9)
         returning id`,
        [campaign.id, campaign.ownerId, campaign.world.generatorVersion, campaign.world.layoutHash,
          campaign.world.width, campaign.world.height, JSON.stringify(campaign.world),
          JSON.stringify(worldGenerationMetadata(campaign)), campaign.createdAt]
      );
      const worldId = worldResult.rows[0].id;
      const regionResult = await client.query(
        `insert into ${SCHEMA}.regions
           (world_id, owner_id, region_key, display_name, region_kind, origin_x, origin_y,
            width, height, layout_hash, map_json, created_at, updated_at)
         values ($1, $2, 'world.main', '코드리아', 'overworld', 0, 0,
                 $3, $4, $5, $6::jsonb, $7, $7)
         returning id`,
        [worldId, campaign.ownerId, campaign.world.width, campaign.world.height,
          campaign.world.layoutHash,
          JSON.stringify({ pointIds: campaign.world.points.map((point) => point.id) }), campaign.createdAt]
      );
      const areaIds = new Map();
      const areasByKey = new Map(campaign.world.areas.map((area) => [area.id, area]));
      for (const area of campaign.world.areas) {
        const anchorX = area.anchor.x - area.bounds.x;
        const anchorY = area.anchor.y - area.bounds.y;
        const areaResult = await client.query(
          `insert into ${SCHEMA}.areas
             (world_id, region_id, owner_id, area_key, display_name, area_kind, origin_x, origin_y, width, height, entry_x, entry_y, exit_x, exit_y, layout_hash, tile_json, created_at, updated_at)
           values ($1,$2,$3,$4,$5,'campaign_region',$6,$7,$8,$9,$10,$11,$10,$11,$12,$13::jsonb,$14,$14) returning id`,
          [worldId, regionResult.rows[0].id, campaign.ownerId, area.id, area.nameKo || area.name, area.bounds.x, area.bounds.y, area.bounds.width, area.bounds.height, anchorX, anchorY, campaign.world.layoutHash, JSON.stringify({ biomeId: area.biomeId, campaignRole: area.campaignRole, anchor: area.anchor, sealedGeometry: true }), campaign.createdAt]
        );
        areaIds.set(area.id, areaResult.rows[0].id);
        await client.query(
          `insert into ${SCHEMA}.world_area_descriptors (world_id, owner_id, area_id, area_key, biome_id, campaign_role, descriptor_json) values ($1,$2,$3,$4,$5,$6,$7::jsonb)`,
          [worldId, campaign.ownerId, areaResult.rows[0].id, area.id, area.biomeId, area.campaignRole || null, JSON.stringify({ name: area.name, nameKo: area.nameKo, summary: area.summary, bounds: area.bounds, anchor: area.anchor, index: area.index })]
        );
      }
      const entry = campaign.world.points.find((point) => point.id === "entry");
      if (!entry) throw new AppError(500, "WORLD_ENTRY_MISSING", "The generated world has no entry POI.");
      const entryAreaId = areaIds.get(entry.areaId);
      for (const route of routesForPersistence(campaign.world, entry)) {
        const from = toAreaLocalPosition(requiredWorldArea(areasByKey, route.fromAreaId), route.from);
        const to = toAreaLocalPosition(requiredWorldArea(areasByKey, route.toAreaId), route.to);
        await client.query(
          `insert into ${SCHEMA}.area_connections
             (owner_id, world_id, from_area_id, to_area_id, from_x, from_y, to_x, to_y,
              direction, traversal_kind, requirement_json)
          values ($1,$2,$3,$4,$5,$6,$7,$8,$9,$10,$11::jsonb)`,
          [campaign.ownerId, worldId, databaseAreaId(areaIds, route.fromAreaId), databaseAreaId(areaIds, route.toAreaId),
            from.x, from.y, to.x, to.y, route.direction, route.traversalKind,
            JSON.stringify(route.requirementJson)]
        );
      }
      for (const pointItem of campaign.world.points) {
        const pointArea = requiredWorldArea(areasByKey, pointItem.areaId);
        const point = toAreaLocalPosition(pointArea, pointItem);
        const gate = gateContractFor(pointItem);
        await client.query(
          `insert into ${SCHEMA}.world_pois
             (world_id, owner_id, area_id, poi_key, poi_kind, display_name, x, y, biome_id,
              campaign_role, visual_intent, is_gated, gate_requirements, tags)
          values ($1,$2,$3,$4,$5,$6,$7,$8,$9,$10,$11,$12,$13::jsonb,$14::jsonb)`,
          [worldId, campaign.ownerId, databaseAreaId(areaIds, pointItem.areaId), pointItem.id, pointItem.kind,
            pointItem.nameKo || pointItem.name, point.x, point.y, pointItem.biomeId || pointArea.biomeId,
            pointItem.campaignRole || pointArea.campaignRole || null, visualIntentFor(pointItem), gate.gated,
            JSON.stringify(gate.requirements), JSON.stringify(pointItem.tags || [])]
        );
      }
      for (const beat of campaign.requiredStoryBeats) {
        await client.query(
          `insert into ${SCHEMA}.campaign_story_beats
             (campaign_id, owner_id, beat_key, title, description, required_ability, target_turn, display_order)
           values ($1, $2, $3, $4, $5, $6, $7, $8)`,
          [campaign.id, campaign.ownerId, beat.id, beat.title, beat.description, beat.requiredAbility, beat.targetTurn, campaign.requiredStoryBeats.indexOf(beat)]
        );
      }
      for (const slot of campaign.world.placementSlots) {
        const slotArea = requiredWorldArea(areasByKey, slot.areaId);
        const point = toAreaLocalPosition(slotArea, slot);
        await client.query(
          `insert into ${SCHEMA}.placement_slots
          (slot_key, owner_id, world_id, area_id, slot_kind, x, y, tags, allowed_asset_ids, biome_id, campaign_role, purpose, reserved_for, is_gated, gate_requirements)
          values ($1,$2,$3,$4,$5,$6,$7,$8::jsonb,$9::jsonb,$10,$11,$12,$13,$14,$15::jsonb)`,
          [slot.id, campaign.ownerId, worldId, databaseAreaId(areaIds, slot.areaId), slot.kind, point.x, point.y,
            JSON.stringify(slot.tags), JSON.stringify(slot.allowedAssetIds), slot.biomeId, slot.campaignRole || null,
            slot.purpose || "ambient", slot.reservedFor ?? null, Boolean(slot.gated),
            JSON.stringify(gateContractFor(slot).requirements)]
        );
      }
      return {
        ...campaign,
        worldId: campaign.worldId || WORLD_CODRIA,
        worldInstanceId: worldId,
        areaId: entryAreaId,
        status: campaignResult.rows[0].status
      };
    });
  }

  async listCampaigns(ownerId) {
    return this.withOwner(ownerId, async (client) => {
      const result = await client.query(campaignSelect("where c.owner_id = $1 order by c.updated_at desc"), [ownerId]);
      return result.rows.map(rowToCampaign);
    }, { readOnly: true });
  }

  async getCampaign(ownerId, campaignId) {
    return this.withOwner(ownerId, async (client) => {
      const result = await client.query(campaignSelect("where c.owner_id = $1 and c.id = $2"), [ownerId, campaignId]);
      if (result.rowCount === 0) throw notFound("Campaign");
      return rowToCampaign(result.rows[0]);
    }, { readOnly: true });
  }

  async createRun(run) {
    return this.withOwner(run.ownerId, async (client) => {
      const persistedWorld = await persistWorldGraph(client, {
        campaignId: run.campaignId,
        ownerId: run.ownerId,
        world: run.world,
        createdAt: run.createdAt,
        worldScope: "run",
        runScopeKey: run.id,
        metadata: worldGenerationMetadata(run)
      });
      const worldId = persistedWorld.worldId;
      const databaseRun = { ...run, worldId: run.worldId || WORLD_CODRIA, worldInstanceId: worldId };
      const areaRows = persistedWorld.areaRows;
      const player = run.entities.find((item) => item.id === run.playerEntityId && item.active);
      if (!player) throw new AppError(500, "PLAYER_MISSING", "The initial run has no active player entity.");
      const activePlacement = databasePlacementForGlobalPosition(databaseRun, areaRows, player.position);
      const areaId = activePlacement.areaId;
      await client.query(
        `insert into ${SCHEMA}.runs
           (id, campaign_id, world_id, owner_id, status, version, current_turn, turn_limit,
            focus, pressure, active_area_id, player_entity_id, world_state, resolution_seed,
            ending_code, started_at, completed_at, created_at, updated_at)
         values ($1, $2, $3, $4, 'playing', $5, $6, $7, $8, $9, $10, $11, $12::jsonb, $13, null, $14, null, $14, $14)`,
        [run.id, run.campaignId, worldId, run.ownerId, run.version, run.currentTurn, run.turnLimit,
          run.focus, run.pressure, areaId, run.playerEntityId, JSON.stringify(runStateForDatabase(run)),
          run.resolutionSeed, run.createdAt]
      );

      const plan = runGenerationPlanForDatabase(databaseRun);
      const planResult = await client.query(
        `insert into ${SCHEMA}.run_generation_plans
           (run_id, owner_id, world_id, schema_version, generator_version, generation_seed,
            plan_hash, source, provider, model, prompt_version, prompt_hash, fallback_used,
            validation_status, validation_report, validation_errors, plan_json, validated_at, created_at)
         values ($1,$2,$3,$4,$5,$6,$7,$8,$9,$10,$11,$12,$13,$14,$15::jsonb,$16::jsonb,$17::jsonb,$18,$18)
         returning id`,
        [run.id, run.ownerId, worldId, plan.schemaVersion, run.world.generatorVersion, run.world.worldSeed,
          plan.planHash, plan.source, plan.provider, plan.model, plan.promptVersion, plan.promptHash,
          plan.fallbackUsed, plan.validationStatus, JSON.stringify(plan.validationReport),
          JSON.stringify(plan.validationErrors), JSON.stringify(plan.planJson), run.createdAt]
      );
      const generationPlanId = planResult.rows[0].id;

      for (const gameEntity of run.entities) {
        const placement = databasePlacementForGlobalPosition(databaseRun, areaRows, gameEntity.position);
        await insertEntity(client, { run: { ...databaseRun, activeAreaId: areaId }, gameEntity, spawnedTurn: 0, ...placement });
      }

      const storedRun = { ...databaseRun, activeAreaId: areaId };
      await persistInitialCodriaState(client, storedRun);
      await insertInitialProgressState(client, storedRun, generationPlanId);
      await bindInitialEntitiesToSlots(client, storedRun, generationPlanId, persistedWorld.slotIds);
      await insertRunGenerationLog(client, storedRun, plan);
      await writeDeepSnapshot(client, storedRun, { generationPlanId, snapshotKind: "autosave" });
      return clone(storedRun);
    });
  }

  async getRun(ownerId, runId) {
    return this.withOwner(ownerId, async (client) => {
      const result = await client.query(
        `select * from ${SCHEMA}.runs where id = $1 and owner_id = $2`,
        [runId, ownerId]
      );
      if (result.rowCount === 0) throw notFound("Run");
      return rowToRun(result.rows[0]);
    }, { readOnly: true });
  }

  async findTurnByIdempotency(ownerId, runId, idempotencyKey) {
    return this.withOwner(ownerId, async (client) => {
      const run = await client.query(`select 1 from ${SCHEMA}.runs where id = $1 and owner_id = $2`, [runId, ownerId]);
      if (run.rowCount === 0) throw notFound("Run");
      const result = await client.query(
        `select * from ${SCHEMA}.turn_records where run_id = $1 and owner_id = $2 and idempotency_key = $3 and status = 'committed'`,
        [runId, ownerId, idempotencyKey]
      );
      return result.rowCount === 0 ? null : rowToTurn(result.rows[0]);
    }, { readOnly: true });
  }

  async findNavigationByIdempotency(ownerId, runId, idempotencyKey) {
    return this.withOwner(ownerId, async (client) => {
      const result = await client.query(`select * from ${SCHEMA}.safe_travels where run_id = $1 and owner_id = $2 and idempotency_key = $3`, [runId, ownerId, idempotencyKey]);
      return result.rowCount === 0 ? null : rowToNavigation(result.rows[0]);
    }, { readOnly: true });
  }

  async commitNavigation({ ownerId, runId, idempotencyKey, requestFingerprint, expectedRunVersion, resolve }) {
    return this.withOwner(ownerId, async (client) => {
      const runResult = await client.query(`select * from ${SCHEMA}.runs where id = $1 and owner_id = $2 for update`, [runId, ownerId]);
      if (runResult.rowCount === 0) throw notFound("Run");
      const currentRun = rowToRun(runResult.rows[0]);
      const existingResult = await client.query(`select * from ${SCHEMA}.safe_travels where run_id = $1 and owner_id = $2 and idempotency_key = $3`, [runId, ownerId, idempotencyKey]);
      if (existingResult.rowCount > 0) {
        const existing = rowToNavigation(existingResult.rows[0]);
        if (existing.requestFingerprint !== requestFingerprint) throw new AppError(409, "IDEMPOTENCY_CONFLICT", "The travel idempotency key was used with a different payload.");
        return { navigation: existing, run: currentRun, fromIdempotencyCache: true };
      }
      if (currentRun.status !== "active") throw new AppError(409, "RUN_NOT_ACTIVE", "The run does not accept travel.");
      if (currentRun.version !== expectedRunVersion) throw new AppError(409, "RUN_VERSION_CONFLICT", "The run version is stale.", { currentVersion: currentRun.version });
      const committed = resolve(clone(currentRun));
      if (!committed || committed.run.version !== currentRun.version + 1 || committed.run.currentTurn !== currentRun.currentTurn || committed.navigation.campaignTurnConsumed !== false) throw new AppError(500, "TRAVEL_INVARIANT_FAILED", "Safe travel violated campaign-turn or version invariants.");
      const activePlacement = await synchronizeActiveArea(client, committed.run);
      await updateRunRow(client, committed.run);
      await client.query(`update ${SCHEMA}.entity_positions set area_id = $4, x = $5, y = $6, revision = revision + 1, updated_at = $7 where entity_id = $1 and owner_id = $2 and run_id = $3 and removed_at is null`, [committed.run.playerEntityId, ownerId, runId, activePlacement.placement.areaId, activePlacement.placement.localPosition.x, activePlacement.placement.localPosition.y, committed.run.updatedAt]);
      if (committed.navigation.events?.length > 0) {
        await synchronizeEntityState(client, committed.run, {
          turnNo: committed.run.currentTurn,
          request: {},
          events: committed.navigation.events
        });
      }
      const requestedDestination = committed.navigation.requestedDestination || committed.navigation.to;
      const requestedPlacement = databasePlacementForGlobalPosition(
        committed.run, activePlacement.areaRows, requestedDestination
      );
      const travelContext = {
        requestedAreaKey: requestedPlacement.areaKey,
        enteredAreaKey: activePlacement.placement.areaKey,
        enteredRegionAxis: areaAt(committed.run.world, committed.navigation.to).regionAxis,
        encounterOpened: Boolean(committed.navigation.encounterOpened),
        layoutHash: committed.run.world.layoutHash,
        rulesAuthority: "server",
        sceneDecision: committed.navigation.sceneDecision || null,
        sceneSequence: committed.navigation.sceneSequence || [],
        events: committed.navigation.events || [],
        narrative: committed.navigation.narrative || null
      };
      await client.query(
        `insert into ${SCHEMA}.safe_travels
           (id, run_id, owner_id, sequence_no, idempotency_key, request_fingerprint, expected_run_version,
            committed_run_version, from_x, from_y, requested_x, requested_y, to_x, to_y, path_cost,
            travel_time_units, cumulative_travel_time_units, entered_area_key, entered_biome_id, campaign_role,
            traversed_area_ids, reached_poi_ids, path_json, encounter_opened, encounter_json,
            campaign_turn_consumed, campaign_turn_before, campaign_turn_after, layout_hash, created_at,
            command_schema_version, input_type, world_id, destination_area_id, turn_context)
         values ($1,$2,$3,$4,$5,$6,$7,$8,$9,$10,$11,$12,$13,$14,$15,$16,$17,$18,$19,$20,$21::jsonb,$22::jsonb,$23::jsonb,$24,$25::jsonb,$26,$27,$28,$29,$30,$31,$32,$33,$34,$35::jsonb)`,
        [committed.navigation.id, runId, ownerId, committed.navigation.sequence, idempotencyKey,
          requestFingerprint, expectedRunVersion, committed.run.version, committed.navigation.from.x,
          committed.navigation.from.y, requestedDestination.x, requestedDestination.y,
          committed.navigation.to.x, committed.navigation.to.y, committed.navigation.pathCost,
          committed.navigation.travelTimeUnits,
          committed.navigation.cumulativeTravelTimeUnits ?? committed.run.travelTimeUnits ?? committed.navigation.travelTimeUnits,
          committed.navigation.enteredAreaId, committed.navigation.enteredBiomeId,
          committed.navigation.campaignRole || null, JSON.stringify(committed.navigation.traversedAreaIds || []),
          JSON.stringify(committed.navigation.reachedPoiIds || []), JSON.stringify(committed.navigation.path || []),
          Boolean(committed.navigation.encounterOpened), committed.navigation.encounter ? JSON.stringify(committed.navigation.encounter) : null,
          false, committed.navigation.campaignTurnBefore, committed.navigation.campaignTurnAfter,
          committed.navigation.layoutHash || committed.run.world.layoutHash, committed.navigation.createdAt,
          "codria-action.v4", "MOVE", databaseWorldId(committed.run), requestedPlacement.areaId,
          JSON.stringify(travelContext)]
      );
      await writeDeepSnapshot(client, committed.run, { snapshotKind: "autosave" });
      return { ...committed, fromIdempotencyCache: false };
    }, { isolation: "serializable" });
  }

  async commitTurn({ ownerId, runId, idempotencyKey, requestFingerprint, expectedRunVersion, resolve }) {
    return this.withOwner(ownerId, async (client) => {
      const runResult = await client.query(
        `select * from ${SCHEMA}.runs where id = $1 and owner_id = $2 for update`,
        [runId, ownerId]
      );
      if (runResult.rowCount === 0) throw notFound("Run");
      const currentRun = rowToRun(runResult.rows[0]);
      const existingResult = await client.query(
        `select * from ${SCHEMA}.turn_records where run_id = $1 and owner_id = $2 and idempotency_key = $3`,
        [runId, ownerId, idempotencyKey]
      );
      if (existingResult.rowCount > 0) {
        const existing = rowToTurn(existingResult.rows[0]);
        if (existing.requestFingerprint !== requestFingerprint) {
          throw new AppError(409, "IDEMPOTENCY_CONFLICT", "The idempotency key was already used with a different payload.");
        }
        return { turn: existing, run: currentRun, fromIdempotencyCache: true };
      }
      if (currentRun.status !== "active") throw new AppError(409, "RUN_NOT_ACTIVE", "The run does not accept turns.");
      if (currentRun.version !== expectedRunVersion) {
        throw new AppError(409, "RUN_VERSION_CONFLICT", "The run version is stale.", { currentVersion: currentRun.version });
      }

      const committed = resolve(clone(currentRun));
      if (!committed || committed.run.version !== currentRun.version + 1 || committed.turn.turnNo !== currentRun.currentTurn + 1) {
        throw new AppError(500, "TURN_INVARIANT_FAILED", "Turn resolver violated a commit invariant.");
      }
      await synchronizeActiveArea(client, committed.run);
      await updateRunRow(client, committed.run);
      await synchronizeEntityState(client, committed.run, committed.turn);
      const narrative = committed.turn.narrative;
      const committedArea = areaAt(
        committed.run.world,
        committed.run.entities.find((item) => item.id === committed.run.playerEntityId).position
      );
      const targetIds = [committed.turn.request.targetEntityId, committed.turn.request.secondaryTargetEntityId].filter(Boolean);
      const turnContext = {
        regionAxis: committedArea.regionAxis,
        domainAreaId: committedArea.id,
        encounterResolved: committed.turn.events.some((event) => event.type === "encounter_resolved"),
        playerNotePresent: Boolean(committed.turn.request.playerNote),
        rulesAuthority: "server"
      };
      await client.query(
        `insert into ${SCHEMA}.turn_records
           (id, run_id, owner_id, turn_no, idempotency_key, request_fingerprint,
            expected_run_version, committed_run_version, status, request_json, result_json,
            narrative_json, fallback_used, model, received_at, completed_at, created_at, updated_at,
            command_schema_version, input_type, skill_id, target_ids, action_context, turn_context,
            campaign_turn_before, campaign_turn_after, campaign_turn_consumed)
         values ($1, $2, $3, $4, $5, $6, $7, $8, 'committed', $9::jsonb, $10::jsonb,
                 $11::jsonb, $12, $13, $14, $14, $14, $14,
                 $15,$16,$17,$18::jsonb,$19,$20::jsonb,$21,$22,$23)`,
        [committed.turn.id, runId, ownerId, committed.turn.turnNo, idempotencyKey,
          requestFingerprint, expectedRunVersion, committed.turn.committedRunVersion,
          JSON.stringify(committed.turn.request), JSON.stringify(committed.turn),
          JSON.stringify(narrative), narrative.fallbackUsed, narrative.model, committed.turn.createdAt,
          "codria-action.v4", "USE_SKILL", committed.turn.request.skillId,
          JSON.stringify(targetIds), committed.turn.actionContext, JSON.stringify(turnContext),
          currentRun.currentTurn, committed.run.currentTurn, true]
      );
      await insertTurnRuleResolution(client, committed.turn);
      for (let index = 0; index < committed.turn.events.length; index += 1) {
        const event = committed.turn.events[index];
        await client.query(
          `insert into ${SCHEMA}.turn_events
             (turn_record_id, run_id, owner_id, event_index, event_type, payload)
           values ($1, $2, $3, $4, $5, $6::jsonb)`,
          [committed.turn.id, runId, ownerId, index, eventCatalogCode(event.type), JSON.stringify(event)]
        );
      }
      await persistCodriaTurnHistory(client, currentRun, committed.run, committed.turn);
      await synchronizeReversibleActions(client, committed.run, committed.turn);
      await client.query(
        `insert into ${SCHEMA}.llm_logs
           (owner_id, run_id, turn_record_id, purpose, provider, model, prompt_version,
            prompt_hash, status, fallback_used, redacted_input_json, redacted_output_json)
         values ($1, $2, $3, 'turn_director', $4, $5, 'gm.v2', $6, $7, $8, $9::jsonb, $10::jsonb)`,
        [ownerId, runId, committed.turn.id,
          narrative.fallbackUsed ? 'deterministic' : 'google-gemini', narrative.model,
          requestFingerprint, narrative.fallbackUsed ? 'fallback' : 'succeeded', narrative.fallbackUsed,
          JSON.stringify({ turnNo: committed.turn.turnNo, ability: committed.turn.request.ability, contextRedacted: true }),
          JSON.stringify({ summary: narrative.summary, appliedOps: narrative.appliedOps.map((operation) => operation.op) })]
      );
      await writeDeepSnapshot(client, committed.run, { snapshotKind: "autosave" });
      return { turn: committed.turn, run: committed.run, fromIdempotencyCache: false };
    }, { isolation: "serializable" });
  }

  async getTurn(ownerId, runId, turnNo) {
    return this.withOwner(ownerId, async (client) => {
      const result = await client.query(
        `select tr.* from ${SCHEMA}.turn_records tr
          join ${SCHEMA}.runs r on r.id = tr.run_id and r.owner_id = tr.owner_id
         where tr.run_id = $1 and tr.owner_id = $2 and tr.turn_no = $3 and tr.status = 'committed'
         order by tr.committed_run_version desc
         limit 1`,
        [runId, ownerId, turnNo]
      );
      if (result.rowCount === 0) throw notFound("Turn");
      return rowToTurn(result.rows[0]);
    }, { readOnly: true });
  }

  async listTurns(ownerId, runId) {
    return this.withOwner(ownerId, async (client) => {
      const run = await client.query(`select 1 from ${SCHEMA}.runs where id = $1 and owner_id = $2`, [runId, ownerId]);
      if (run.rowCount === 0) throw notFound("Run");
      const result = await client.query(
        `select distinct on (turn_no) * from ${SCHEMA}.turn_records
          where run_id = $1 and owner_id = $2 and status = 'committed'
          order by turn_no, committed_run_version desc`,
        [runId, ownerId]
      );
      return result.rows.map(rowToTurn);
    }, { readOnly: true });
  }

  async abandonRun(ownerId, runId, expectedRunVersion, now) {
    return this.changeRunStatus(ownerId, runId, expectedRunVersion, "abandoned", now);
  }

  async resumeRun(ownerId, runId, expectedRunVersion, now) {
    return this.changeRunStatus(ownerId, runId, expectedRunVersion, "active", now);
  }

  async changeRunStatus(ownerId, runId, expectedRunVersion, targetStatus, now) {
    const result = await this.withOwner(ownerId, async (client) => {
      const result = await client.query(
        `select * from ${SCHEMA}.runs where id = $1 and owner_id = $2 for update`,
        [runId, ownerId]
      );
      if (result.rowCount === 0) throw notFound("Run");
      const run = rowToRun(result.rows[0]);
      if (run.version !== expectedRunVersion) throw new AppError(409, "RUN_VERSION_CONFLICT", "The run version is stale.", { currentVersion: run.version });
      if (run.status === "completed") throw new AppError(409, "RUN_COMPLETED", "A completed run cannot change lifecycle state.");
      if (targetStatus === "active" && run.status !== "active") {
        const validation = await validateLatestDeepSnapshot(client, run, now);
        if (!validation.accepted) return { resumeError: validation.errors };
      }
      if (run.status !== targetStatus) {
        run.status = targetStatus;
        run.version += 1;
        run.updatedAt = now;
        await updateRunRow(client, run);
        await writeDeepSnapshot(client, run, { snapshotKind: targetStatus === "active" ? "recovery" : "autosave" });
      }
      return { run };
    });
    if (result.resumeError) throw new AppError(409, "RESUME_VALIDATION_FAILED", "The latest deep save did not match the sealed run state.", { errors: result.resumeError });
    return result.run;
  }

  async close() {
    await this.pool.end();
  }

  async withOwner(ownerId, callback, { isolation = "read committed", readOnly = false } = {}) {
    const client = await this.pool.connect();
    try {
      const mode = readOnly ? " read only" : "";
      await client.query(`begin isolation level ${isolation}${mode}`);
      await client.query("select set_config('app.user_id', $1, true)", [ownerId]);
      const value = await callback(client);
      await client.query("commit");
      return value;
    } catch (error) {
      await client.query("rollback").catch(() => {});
      throw mapDatabaseError(error);
    } finally {
      client.release();
    }
  }
}

function campaignSelect(suffix) {
  return `select c.*, w.id as world_id, w.map_json as world_map
            from ${SCHEMA}.campaigns c
            join ${SCHEMA}.worlds w on w.campaign_id = c.id and w.owner_id = c.owner_id
             and w.world_scope = 'campaign_preview'
            ${suffix}`;
}

function rowToCampaign(row) {
  return {
    ...(row.settings || {}),
    id: row.id,
    ownerId: row.owner_id,
    title: row.title,
    worldSeed: Number(row.world_seed),
    turnLimit: Number(row.turn_limit),
    status: row.status,
    premise: row.premise || row.settings?.premise || "",
    worldId: row.settings?.worldId || row.world_map?.worldId || WORLD_CODRIA,
    worldInstanceId: row.world_id,
    world: row.world_map,
    createdAt: timestamp(row.created_at),
    updatedAt: timestamp(row.updated_at)
  };
}

function worldGenerationMetadata(campaign) {
  return {
    generatedOnce: true,
    placementSlotsSealed: true,
    routesSealed: true,
    coordinateSpace: "world_global",
    templateId: campaign.templateId,
    templateVersion: campaign.templateVersion,
    worldName: campaign.world?.worldName,
    worldNameKo: campaign.world?.worldNameKo,
    progressionGraph: campaign.world?.progressionGraph,
    progressionMetadata: campaign.progressionMetadata || campaign.world?.generationReport,
    generationReport: campaign.world?.generationReport
  };
}

async function persistWorldGraph(client, {
  campaignId,
  ownerId,
  world,
  createdAt,
  worldScope,
  runScopeKey = null,
  metadata = {}
}) {
  const worldResult = await client.query(
    `insert into ${SCHEMA}.worlds
       (campaign_id, owner_id, world_scope, run_scope_key, generator_version, layout_hash,
        width, height, map_json, generation_metadata, generated_at, created_at, updated_at)
     values ($1,$2,$3,$4,$5,$6,$7,$8,$9::jsonb,$10::jsonb,$11,$11,$11)
     returning id`,
    [campaignId, ownerId, worldScope, runScopeKey, world.generatorVersion, world.layoutHash,
      world.width, world.height, JSON.stringify(world), JSON.stringify(metadata), createdAt]
  );
  const worldId = worldResult.rows[0].id;
  const regionResult = await client.query(
    `insert into ${SCHEMA}.regions
       (world_id, owner_id, region_key, display_name, region_kind, origin_x, origin_y,
        width, height, layout_hash, map_json, created_at, updated_at)
     values ($1,$2,'world.main',$3,'overworld',0,0,$4,$5,$6,$7::jsonb,$8,$8)
     returning id`,
    [worldId, ownerId, world.worldNameKo || world.worldName || "코드리아",
      world.width, world.height, world.layoutHash,
      JSON.stringify({ pointIds: world.points.map((pointItem) => pointItem.id) }), createdAt]
  );

  const regionId = regionResult.rows[0].id;
  const areaIds = new Map();
  const areaRows = [];
  const areasByKey = new Map(world.areas.map((area) => [area.id, area]));
  for (const area of world.areas) {
    const anchorX = area.anchor.x - area.bounds.x;
    const anchorY = area.anchor.y - area.bounds.y;
    const areaResult = await client.query(
      `insert into ${SCHEMA}.areas
         (world_id, region_id, owner_id, area_key, display_name, area_kind, origin_x, origin_y,
          width, height, entry_x, entry_y, exit_x, exit_y, layout_hash, tile_json, created_at, updated_at)
       values ($1,$2,$3,$4,$5,'campaign_region',$6,$7,$8,$9,$10,$11,$10,$11,$12,$13::jsonb,$14,$14)
       returning id`,
      [worldId, regionId, ownerId, area.id, area.nameKo || area.name, area.bounds.x, area.bounds.y,
        area.bounds.width, area.bounds.height, anchorX, anchorY, world.layoutHash,
        JSON.stringify({ biomeId: area.biomeId, campaignRole: area.campaignRole, anchor: area.anchor, sealedGeometry: true }), createdAt]
    );
    const areaId = areaResult.rows[0].id;
    areaIds.set(area.id, areaId);
    areaRows.push({
      id: areaId,
      area_key: area.id,
      origin_x: area.bounds.x,
      origin_y: area.bounds.y,
      width: area.bounds.width,
      height: area.bounds.height
    });
    await client.query(
      `insert into ${SCHEMA}.world_area_descriptors
         (world_id, owner_id, area_id, area_key, biome_id, campaign_role, descriptor_json)
       values ($1,$2,$3,$4,$5,$6,$7::jsonb)`,
      [worldId, ownerId, areaId, area.id, area.biomeId, area.campaignRole || null,
        JSON.stringify({ name: area.name, nameKo: area.nameKo, summary: area.summary, bounds: area.bounds, anchor: area.anchor, index: area.index })]
    );
  }

  const entry = world.points.find((pointItem) => pointItem.id === "entry");
  if (!entry) throw new AppError(500, "WORLD_ENTRY_MISSING", "The generated world has no entry POI.");
  for (const route of routesForPersistence(world, entry)) {
    const from = toAreaLocalPosition(requiredWorldArea(areasByKey, route.fromAreaId), route.from);
    const to = toAreaLocalPosition(requiredWorldArea(areasByKey, route.toAreaId), route.to);
    await client.query(
      `insert into ${SCHEMA}.area_connections
         (owner_id, world_id, from_area_id, to_area_id, from_x, from_y, to_x, to_y,
          direction, traversal_kind, requirement_json)
       values ($1,$2,$3,$4,$5,$6,$7,$8,$9,$10,$11::jsonb)`,
      [ownerId, worldId, databaseAreaId(areaIds, route.fromAreaId), databaseAreaId(areaIds, route.toAreaId),
        from.x, from.y, to.x, to.y, route.direction, route.traversalKind, JSON.stringify(route.requirementJson)]
    );
  }

  const pointIds = new Map();
  for (const pointItem of world.points) {
    const pointArea = requiredWorldArea(areasByKey, pointItem.areaId);
    const pointPosition = toAreaLocalPosition(pointArea, pointItem);
    const gate = gateContractFor(pointItem);
    const result = await client.query(
      `insert into ${SCHEMA}.world_pois
         (world_id, owner_id, area_id, poi_key, poi_kind, display_name, x, y, biome_id,
          campaign_role, visual_intent, is_gated, gate_requirements, tags)
       values ($1,$2,$3,$4,$5,$6,$7,$8,$9,$10,$11,$12,$13::jsonb,$14::jsonb)
       returning id`,
      [worldId, ownerId, databaseAreaId(areaIds, pointItem.areaId), pointItem.id, pointItem.kind,
        pointItem.nameKo || pointItem.name, pointPosition.x, pointPosition.y,
        pointItem.biomeId || pointArea.biomeId, pointItem.campaignRole || pointArea.campaignRole || null,
        visualIntentFor(pointItem), gate.gated, JSON.stringify(gate.requirements), JSON.stringify(pointItem.tags || [])]
    );
    pointIds.set(pointItem.id, result.rows[0].id);
  }

  await persistRegionAxisBindings(client, { ownerId, worldId, world, areaIds, pointIds });

  const slotIds = new Map();
  for (const slot of world.placementSlots) {
    const slotArea = requiredWorldArea(areasByKey, slot.areaId);
    const slotPosition = toAreaLocalPosition(slotArea, slot);
    const gate = gateContractFor(slot);
    const result = await client.query(
      `insert into ${SCHEMA}.placement_slots
         (slot_key, owner_id, world_id, area_id, slot_kind, x, y, tags, allowed_asset_ids,
          biome_id, campaign_role, purpose, reserved_for, is_gated, gate_requirements)
       values ($1,$2,$3,$4,$5,$6,$7,$8::jsonb,$9::jsonb,$10,$11,$12,$13,$14,$15::jsonb)
       returning id`,
      [slot.id, ownerId, worldId, databaseAreaId(areaIds, slot.areaId), slot.kind,
        slotPosition.x, slotPosition.y, JSON.stringify(slot.tags || []), JSON.stringify(slot.allowedAssetIds || []),
        slot.biomeId, slot.campaignRole || null, slot.purpose || "ambient", slot.reservedFor ?? null,
        Boolean(slot.gated), JSON.stringify(gate.requirements)]
    );
    slotIds.set(slot.id, result.rows[0].id);
  }
  return { worldId, regionId, areaIds, areaRows, pointIds, slotIds };
}

async function persistRegionAxisBindings(client, { ownerId, worldId, world, areaIds, pointIds }) {
  const roles = Array.isArray(world.campaignRegionRoles) ? world.campaignRegionRoles : [];
  if (roles.length !== 6 || new Set(roles.map((role) => role.regionAxis)).size !== 6) {
    throw new AppError(500, "WORLD_REGION_AXIS_BINDINGS_INVALID", "A sealed Codria world requires exactly six semantic region-axis bindings.");
  }
  for (const role of roles) {
    const area = world.areas.find((candidate) => candidate.campaignRole === role.id);
    const primaryPoint = world.points.find((candidate) => candidate.id === `poi.${role.id.toLowerCase()}`)
      || world.points.find((candidate) => candidate.campaignRole === role.id);
    const areaId = area ? areaIds.get(area.id) : null;
    const primaryPoiId = primaryPoint ? pointIds.get(primaryPoint.id) : null;
    if (!area || !areaId || !primaryPoint || !primaryPoiId) {
      throw new AppError(500, "WORLD_REGION_AXIS_BINDING_INCOMPLETE", `Region axis ${String(role.regionAxis)} has no sealed area and primary POI.`);
    }
    await client.query(
      `insert into ${SCHEMA}.world_region_axis_bindings
         (world_id, owner_id, region_axis_code, area_id, terrain_biome_id,
          primary_poi_id, binding_seed, binding_metadata)
       values ($1,$2,$3,$4,$5,$6,$7,$8::jsonb)`,
      [worldId, ownerId, role.regionAxis, areaId, area.biomeId, primaryPoiId, world.worldSeed,
        JSON.stringify({ campaignRole: role.id, domainAreaId: area.id, domainPoiId: primaryPoint.id, layoutHash: world.layoutHash, sealed: true })]
    );
  }
}

function routesForPersistence(world, entry) {
  const areasByKey = new Map(world.areas.map((area) => [area.id, area]));
  const hasDeclaredRoutes = Object.prototype.hasOwnProperty.call(world, "routes");
  if (hasDeclaredRoutes && !Array.isArray(world.routes)) {
    throw new AppError(500, "WORLD_ROUTES_INVALID", "Declared world routes must be an array.");
  }
  const sourceRoutes = hasDeclaredRoutes
    ? world.routes
    : world.areas
      .filter((area) => area.id !== entry.areaId)
      .map((area) => ({
        id: `fallback.${entry.areaId}.${area.id}`,
        fromAreaId: entry.areaId,
        toAreaId: area.id,
        from: { x: entry.x, y: entry.y },
        to: { ...area.anchor },
        traversalKind: "safe_route",
        width: 1,
        loop: false,
        gated: area.campaignRole === "FINAL_CONVERGENCE",
        requiresProgressLevel: area.campaignRole === "FINAL_CONVERGENCE" ? 3 : 0,
        requiresProgressTokens: area.campaignRole === "FINAL_CONVERGENCE"
          ? ["ADMIN_ACCESS_LEVEL_1", "ADMIN_ACCESS_LEVEL_2", "ADMIN_ACCESS_LEVEL_3"]
          : []
      }));

  return sourceRoutes.map((route, index) => normalizeRouteForPersistence(route, index, areasByKey));
}

function normalizeRouteForPersistence(route, index, areasByKey) {
  const fromAreaId = routeAreaId(route, "from");
  const toAreaId = routeAreaId(route, "to");
  const fromArea = requiredWorldArea(areasByKey, fromAreaId);
  const toArea = requiredWorldArea(areasByKey, toAreaId);
  if (fromAreaId === toAreaId) throw new AppError(500, "WORLD_ROUTE_INVALID", "A persisted world route must connect two distinct areas.");

  const from = routeEndpoint(route, "from", fromArea.anchor);
  const to = routeEndpoint(route, "to", toArea.anchor);
  const requirements = routeRequirements(route);
  const gated = Boolean(route.gated ?? route.isGated ?? requirements.gated
    ?? (requirements.requiresProgressLevel > 0 || requirements.requiresProgressTokens.length > 0));
  delete requirements.gated;
  const width = Number.isInteger(route.width) && route.width > 0 ? route.width : 1;
  const loop = Boolean(route.loop ?? route.isLoop);
  const routeId = String(route.id || `route.${fromAreaId}.${toAreaId}.${index}`);
  const traversalKind = String(route.traversalKind || route.traversal_kind || route.kind || "safe_route");
  const direction = route.direction === "one_way" || route.direction === "oneWay" ? "one_way" : "bidirectional";

  return {
    routeId,
    fromAreaId,
    toAreaId,
    from,
    to,
    direction,
    traversalKind,
    requirementJson: {
      routeId,
      kind: route.kind || traversalKind,
      width,
      loop,
      gated,
      requirements,
      campaignTurnConsumed: false,
      immutable: true,
      coordinateSpace: "area_local"
    }
  };
}

function routeAreaId(route, side) {
  const prefix = side === "from" ? "source" : "target";
  const candidates = [
    route[`${side}AreaId`],
    route[`${prefix}AreaId`],
    route[`${side}Area`],
    route[`${prefix}Area`],
    route[side],
    route[prefix]
  ];
  for (const candidate of candidates) {
    if (typeof candidate === "string" && candidate) return candidate;
    if (candidate && typeof candidate === "object") {
      const key = candidate.areaId || candidate.areaKey || candidate.id;
      if (typeof key === "string" && key) return key;
    }
  }
  throw new AppError(500, "WORLD_ROUTE_AREA_MISSING", `A generated route has no ${side} area identifier.`);
}

function routeEndpoint(route, side, fallback) {
  const prefix = side === "from" ? "source" : "target";
  const pathPoint = Array.isArray(route.path)
    ? route.path[side === "from" ? 0 : route.path.length - 1]
    : null;
  const candidates = [route[side], route[`${side}Point`], route[`${prefix}Point`], pathPoint, fallback];
  const point = candidates.find((candidate) => Number.isInteger(candidate?.x) && Number.isInteger(candidate?.y));
  if (!point) throw new AppError(500, "WORLD_ROUTE_ENDPOINT_MISSING", `A generated route has no ${side} endpoint.`);
  return { x: point.x, y: point.y };
}

function routeRequirements(route) {
  const declared = route.requirements && typeof route.requirements === "object" && !Array.isArray(route.requirements)
    ? clone(route.requirements)
    : {};
  const progressLevel = route.requiresProgressLevel ?? declared.requiresProgressLevel ?? declared.progressLevel ?? 0;
  const tokens = route.requiresProgressTokens ?? declared.requiresProgressTokens ?? declared.progressTokens ?? [];
  return {
    ...declared,
    requiresProgressLevel: Number.isInteger(progressLevel) && progressLevel >= 0 ? progressLevel : 0,
    requiresProgressTokens: Array.isArray(tokens) ? [...tokens] : []
  };
}

function requiredWorldArea(areasByKey, areaId) {
  const area = areasByKey.get(areaId);
  if (!area) throw new AppError(500, "WORLD_AREA_MAPPING_FAILED", `Generated area ${String(areaId)} is not persisted.`);
  return area;
}

function toAreaLocalPosition(area, globalPosition) {
  const bounds = area?.bounds;
  if (!bounds
      || !Number.isInteger(globalPosition?.x)
      || !Number.isInteger(globalPosition?.y)
      || globalPosition.x < bounds.x
      || globalPosition.y < bounds.y
      || globalPosition.x >= bounds.x + bounds.width
      || globalPosition.y >= bounds.y + bounds.height) {
    throw new AppError(500, "DATABASE_AREA_MAPPING_FAILED", `Global coordinate is outside generated area ${String(area?.id || "unknown")}.`);
  }
  return { x: globalPosition.x - bounds.x, y: globalPosition.y - bounds.y };
}

function databaseAreaId(areaIds, areaKey) {
  const areaId = areaIds.get(areaKey);
  if (!areaId) throw new AppError(500, "WORLD_AREA_MAPPING_FAILED", `Generated area ${String(areaKey)} has no database identity.`);
  return areaId;
}

function gateContractFor(item) {
  const requirements = routeRequirements(item);
  return {
    gated: Boolean(item.gated ?? item.isGated
      ?? (requirements.requiresProgressLevel > 0 || requirements.requiresProgressTokens.length > 0)),
    requirements
  };
}

function visualIntentFor(item) {
  if (typeof item.visualIntent === "string" && item.visualIntent.trim()) return item.visualIntent;
  if (Array.isArray(item.visualIntents) && item.visualIntents.length > 0) return item.visualIntents.join(", ");
  return "navigation anchor";
}

function campaignSettingsForDatabase(campaign) {
  return {
    authoritativeMap: true,
    templateId: campaign.templateId,
    templateVersion: campaign.templateVersion,
    archetype: campaign.archetype,
    baseArchetype: campaign.baseArchetype,
    variant: campaign.variant,
    generatedTitle: campaign.generatedTitle,
    generatedTitleKo: campaign.generatedTitleKo,
    premise: campaign.premise,
    premiseKo: campaign.premiseKo,
    tone: campaign.tone,
    forbiddenEvents: campaign.forbiddenEvents,
    npcRoles: campaign.npcRoles,
    canonicalFactTemplates: campaign.canonicalFactTemplates,
    initialRumors: campaign.initialRumors,
    requiredStoryBeats: campaign.requiredStoryBeats,
    endingCandidates: campaign.endingCandidates,
    worldName: campaign.worldName || campaign.world?.worldName,
    worldNameKo: campaign.worldNameKo || campaign.world?.worldNameKo,
    progressTokenDefinitions: campaign.progressTokenDefinitions,
    metricDefinitions: campaign.metricDefinitions,
    campaignPhases: campaign.campaignPhases,
    endingWindow: campaign.endingWindow,
    progressionGraph: campaign.progressionGraph || campaign.world?.progressionGraph,
    progressionMetadata: campaign.progressionMetadata || campaign.world?.generationReport,
    progression: campaign.progression,
    genome: campaign.genome,
    questSeeds: campaign.questSeeds,
    generationPlan: campaign.generationPlan,
    generationMetadata: campaign.generationMetadata,
    contentHash: campaign.contentHash,
    roleAssignments: campaign.roleAssignments || campaign.world?.areas?.filter((area) => area.campaignRole)
      .map((area) => ({ areaId: area.id, campaignRole: area.campaignRole }))
  };
}

function runStateForDatabase(run) {
  const state = clone(run);
  delete state.resolutionSeed;
  delete state.worldInstanceId;
  return state;
}

function databaseWorldId(run) {
  return run.worldInstanceId || run.worldId;
}

function runGenerationPlanForDatabase(run) {
  const metadata = run.generationPlan?.generationMetadata || {};
  const fallbackUsed = metadata.fallbackUsed === true;
  const modelEnriched = metadata.enrichment === "validated" && typeof metadata.model === "string";
  const source = fallbackUsed ? "fallback" : modelEnriched ? "hybrid" : "deterministic";
  const planHash = run.campaignContentHash;
  if (!/^[0-9a-f]{64}$/.test(planHash || "")) {
    throw new AppError(500, "RUN_PLAN_HASH_INVALID", "The validated run campaign plan has no canonical SHA-256 content hash.");
  }
  return {
    schemaVersion: "codria-run-plan.v4",
    planHash,
    source,
    provider: modelEnriched ? "google-gemini" : null,
    model: modelEnriched ? metadata.model : null,
    promptVersion: modelEnriched ? "campaign-plan.v1" : null,
    promptHash: modelEnriched ? fingerprint({ seed: run.world.worldSeed, immutablePlan: run.generationPlan }) : null,
    fallbackUsed,
    validationStatus: fallbackUsed ? "fallback_validated" : "validated",
    validationReport: {
      schemaValid: true,
      immutableGeometryValidated: true,
      placementSlotsValidated: true,
      planner: metadata.planner || "deterministic-campaign-genome",
      enrichment: metadata.enrichment || "deterministic"
    },
    validationErrors: fallbackUsed
      ? [{ code: String(metadata.fallbackReason || "planner_unavailable"), recoveredWith: "deterministic_seeded_plan" }]
      : [],
    planJson: {
      title: run.campaignTitle,
      worldName: run.worldName,
      premise: run.premise,
      beats: run.requiredStoryBeats,
      npcs: run.entities.filter((entityItem) => entityItem.kind === "npc").map((entityItem) => ({
        id: entityItem.id,
        name: entityItem.name,
        role: entityItem.state?.campaignRole,
        slotId: entityItem.state?.slotId
      })),
      quests: run.questTemplates,
      endings: run.endingCandidates,
      generationPlan: run.generationPlan
    }
  };
}

function progressProjection(run) {
  const beats = run.requiredStoryBeats || [];
  return {
    status: run.status === "completed" ? "completed"
      : run.currentTurn >= Math.max(1, run.turnLimit - 5) ? "converging" : "active",
    currentNodeKey: run.currentStoryBeat?.id || run.currentAct || null,
    completedNodeKeys: beats.filter((beat) => beat.status === "completed").map((beat) => beat.id),
    failedNodeKeys: beats.filter((beat) => ["failed", "skipped"].includes(beat.status)).map((beat) => beat.id),
    endingCandidateKeys: (run.endingCandidates || []).map((ending) => ending.id),
    openThreads: clone(run.openLoops || []),
    progressState: {
      level: run.progressLevel || 0,
      tokens: clone(run.progressTokens || []),
      currentStoryBeat: clone(run.currentStoryBeat || null),
      activeQuests: clone(run.activeQuests || []),
      facts: clone(run.canonicalFacts || []),
      rumors: clone(run.rumors || []),
      npcMemories: clone(run.npcMemories || []),
      npcRelationships: clone(run.npcRelationships || [])
    },
    ruleState: {
      focus: run.focus,
      pressure: run.pressure,
      metrics: clone(run.metrics || {}),
      finalePuzzle: clone(run.finalePuzzle || {})
    },
    convergenceState: {
      endingWindow: clone(run.endingWindow || {}),
      selectedEndingId: run.selectedEndingId || null,
      endingCode: run.endingCode || null,
      finaleResolution: clone(run.finaleResolution || null)
    }
  };
}

async function insertInitialProgressState(client, run, generationPlanId) {
  const state = progressProjection(run);
  await client.query(
    `insert into ${SCHEMA}.run_progress_states
       (run_id, owner_id, generation_plan_id, status, current_node_key, state_version,
        last_turn_no, completed_node_keys, failed_node_keys, ending_candidate_keys,
        open_threads, progress_state, rule_state, convergence_state, created_at, updated_at)
     values ($1,$2,$3,$4,$5,1,$6,$7::jsonb,$8::jsonb,$9::jsonb,$10::jsonb,$11::jsonb,$12::jsonb,$13::jsonb,$14,$14)`,
    [run.id, run.ownerId, generationPlanId, state.status, state.currentNodeKey, run.currentTurn,
      JSON.stringify(state.completedNodeKeys), JSON.stringify(state.failedNodeKeys),
      JSON.stringify(state.endingCandidateKeys), JSON.stringify(state.openThreads),
      JSON.stringify(state.progressState), JSON.stringify(state.ruleState),
      JSON.stringify(state.convergenceState), run.createdAt]
  );
}

async function synchronizeProgressState(client, run) {
  const state = progressProjection(run);
  const result = await client.query(
    `update ${SCHEMA}.run_progress_states
        set status = $3,
            current_node_key = $4,
            state_version = state_version + 1,
            last_turn_no = $5,
            completed_node_keys = $6::jsonb,
            failed_node_keys = $7::jsonb,
            ending_candidate_keys = $8::jsonb,
            open_threads = $9::jsonb,
            progress_state = $10::jsonb,
            rule_state = $11::jsonb,
            convergence_state = $12::jsonb,
            updated_at = $13
      where run_id = $1 and owner_id = $2`,
    [run.id, run.ownerId, state.status, state.currentNodeKey, run.currentTurn,
      JSON.stringify(state.completedNodeKeys), JSON.stringify(state.failedNodeKeys),
      JSON.stringify(state.endingCandidateKeys), JSON.stringify(state.openThreads),
      JSON.stringify(state.progressState), JSON.stringify(state.ruleState),
      JSON.stringify(state.convergenceState), run.updatedAt]
  );
  if (result.rowCount !== 1) throw new AppError(500, "RUN_PROGRESS_STATE_MISSING", "The generic run progress projection is missing.");
}

async function bindInitialEntitiesToSlots(client, run, generationPlanId, slotIds) {
  for (const gameEntity of run.entities) {
    const domainSlotId = gameEntity.state?.slotId;
    const slotId = slotIds.get(domainSlotId);
    if (!slotId) continue;
    const planNodeKey = String(gameEntity.state?.campaignRole || gameEntity.state?.evidenceKey
      || gameEntity.state?.finaleComponent || domainSlotId);
    await client.query(
      `insert into ${SCHEMA}.run_slot_bindings
         (run_id, owner_id, world_id, generation_plan_id, slot_id, binding_key,
          binding_kind, plan_node_key, entity_id, status, activation_turn, binding_payload,
          created_at, updated_at)
       values ($1,$2,$3,$4,$5,$6,'entity',$7,$8,'active',0,$9::jsonb,$10,$10)`,
      [run.id, run.ownerId, databaseWorldId(run), generationPlanId, slotId, `entity:${gameEntity.id}`,
        planNodeKey, gameEntity.id,
        JSON.stringify({ domainSlotId, assetId: gameEntity.assetId, geometryOwnedByWorld: true }), run.createdAt]
    );
  }
}

async function insertRunGenerationLog(client, run, plan) {
  await client.query(
    `insert into ${SCHEMA}.llm_logs
       (owner_id, run_id, turn_record_id, purpose, provider, model, prompt_version,
        prompt_hash, status, fallback_used, redacted_input_json, redacted_output_json, created_at)
     values ($1,$2,null,'run_generation',$3,$4,'campaign-plan.v1',$5,$6,$7,$8::jsonb,$9::jsonb,$10)`,
    [run.ownerId, run.id, plan.provider || "deterministic", plan.model || "deterministic-campaign-genome",
      plan.promptHash || plan.planHash, plan.fallbackUsed ? "fallback" : "succeeded", plan.fallbackUsed,
      JSON.stringify({ worldSeed: run.world.worldSeed, themeRedacted: true, immutableGeometry: true }),
      JSON.stringify({ planHash: plan.planHash, source: plan.source, validationStatus: plan.validationStatus }),
      run.createdAt]
  );
}

async function generationPlanIdentity(client, run, explicitId = null) {
  const result = await client.query(
    `select id, plan_hash from ${SCHEMA}.run_generation_plans
      where run_id = $1 and owner_id = $2${explicitId ? " and id = $3" : ""}`,
    explicitId ? [run.id, run.ownerId, explicitId] : [run.id, run.ownerId]
  );
  if (result.rowCount !== 1) throw new AppError(500, "RUN_GENERATION_PLAN_MISSING", "The sealed generation plan is missing.");
  return { id: result.rows[0].id, planHash: result.rows[0].plan_hash };
}

async function writeDeepSnapshot(client, run, { generationPlanId = null, snapshotKind = "autosave" } = {}) {
  const plan = await generationPlanIdentity(client, run, generationPlanId);
  const slotResult = await client.query(
    `insert into ${SCHEMA}.save_slots (owner_id, campaign_id, slot_no, title, created_at, updated_at)
     values ($1,$2,1,$3,$4,$4)
     on conflict (owner_id, campaign_id, slot_no) do update
       set title = excluded.title, updated_at = excluded.updated_at
     returning id`,
    [run.ownerId, run.campaignId, `${run.campaignTitle || "Ninja Adventure"} · 자동 저장`, run.updatedAt || run.createdAt]
  );
  let lastTurnRecordId = null;
  let lastEventId = null;
  if (run.currentTurn > 0) {
    const cursor = await client.query(
      `select tr.id as turn_record_id, te.id as event_id
         from ${SCHEMA}.turn_records tr
         join ${SCHEMA}.turn_events te on te.turn_record_id = tr.id
        where tr.run_id = $1 and tr.owner_id = $2 and tr.turn_no = $3 and tr.status = 'committed'
        order by te.event_index desc limit 1`,
      [run.id, run.ownerId, run.currentTurn]
    );
    if (cursor.rowCount !== 1) throw new AppError(500, "SAVE_EVENT_CURSOR_MISSING", "A deep save requires the latest committed turn event cursor.");
    lastTurnRecordId = cursor.rows[0].turn_record_id;
    lastEventId = cursor.rows[0].event_id;
  }
  const state = JSON.parse(JSON.stringify(runStateForDatabase(run)));
  const snapshotResult = await client.query(
    `insert into ${SCHEMA}.save_snapshots
       (slot_id, owner_id, campaign_id, run_id, run_version, current_turn, schema_version,
        state_json, checksum_sha256, snapshot_kind, world_id, generation_plan_id, plan_hash,
        layout_hash, last_turn_record_id, last_event_id, resume_metadata, created_at)
     values ($1,$2,$3,$4,$5,$6,'codria-save.v4',$7::jsonb,$8,$9,$10,$11,$12,$13,$14,$15,$16::jsonb,$17)
     returning id`,
    [slotResult.rows[0].id, run.ownerId, run.campaignId, run.id, run.version, run.currentTurn,
      JSON.stringify(state), fingerprint(state), snapshotKind, databaseWorldId(run), plan.id, plan.planHash,
      run.world.layoutHash, lastTurnRecordId, lastEventId,
      JSON.stringify({ authoritative: true, worldGeneratedOnce: true, secretFieldsRedacted: ["resolutionSeed"] }),
      run.updatedAt || run.createdAt]
  );
  await client.query(
    `update ${SCHEMA}.save_slots set latest_snapshot_id = $2, updated_at = $3 where id = $1`,
    [slotResult.rows[0].id, snapshotResult.rows[0].id, run.updatedAt || run.createdAt]
  );
  return snapshotResult.rows[0].id;
}

async function validateLatestDeepSnapshot(client, run, now) {
  const snapshotResult = await client.query(
    `select ss.*, rgp.plan_hash as sealed_plan_hash, w.layout_hash as sealed_layout_hash
       from ${SCHEMA}.save_snapshots ss
       join ${SCHEMA}.run_generation_plans rgp
         on rgp.id = ss.generation_plan_id and rgp.run_id = ss.run_id and rgp.owner_id = ss.owner_id
       join ${SCHEMA}.worlds w on w.id = ss.world_id and w.owner_id = ss.owner_id
      where ss.run_id = $1 and ss.owner_id = $2 and ss.snapshot_kind <> 'legacy'
      order by ss.run_version desc, ss.created_at desc limit 1`,
    [run.id, run.ownerId]
  );
  if (snapshotResult.rowCount !== 1) {
    return { accepted: false, errors: [{ code: "DEEP_SNAPSHOT_MISSING" }] };
  }
  const snapshot = snapshotResult.rows[0];
  const observedChecksum = fingerprint(snapshot.state_json);
  const canonicalState = JSON.parse(JSON.stringify(runStateForDatabase(run)));
  const canonicalChecksum = fingerprint(canonicalState);
  const checks = {
    checksum: observedChecksum === snapshot.checksum_sha256,
    canonicalState: observedChecksum === canonicalChecksum,
    planHash: snapshot.plan_hash === snapshot.sealed_plan_hash && snapshot.plan_hash === run.campaignContentHash,
    layoutHash: snapshot.layout_hash === snapshot.sealed_layout_hash && snapshot.layout_hash === run.world.layoutHash,
    runVersion: Number(snapshot.run_version) === run.version,
    currentTurn: Number(snapshot.current_turn) === run.currentTurn
  };
  const errors = Object.entries(checks)
    .filter(([, passed]) => !passed)
    .map(([check]) => ({ code: `RESUME_${check.replace(/[A-Z]/g, (character) => `_${character}`).toUpperCase()}_MISMATCH` }));
  const attemptResult = await client.query(
    `select coalesce(max(attempt_no), 0) + 1 as attempt_no
       from ${SCHEMA}.resume_validation_records where snapshot_id = $1`,
    [snapshot.id]
  );
  await client.query(
    `insert into ${SCHEMA}.resume_validation_records
       (snapshot_id, run_id, owner_id, attempt_no, validation_status,
        observed_checksum_sha256, observed_plan_hash, observed_layout_hash,
        checks_json, errors_json, created_at)
     values ($1,$2,$3,$4,$5,$6,$7,$8,$9::jsonb,$10::jsonb,$11)`,
    [snapshot.id, run.id, run.ownerId, Number(attemptResult.rows[0].attempt_no),
      errors.length === 0 ? "accepted" : "rejected", observedChecksum,
      snapshot.plan_hash, snapshot.layout_hash, JSON.stringify(checks), JSON.stringify(errors), now]
  );
  return { accepted: errors.length === 0, errors };
}

function rowToRun(row) {
  return {
    ...row.world_state,
    id: row.id,
    campaignId: row.campaign_id,
    worldId: row.world_state.worldId || WORLD_CODRIA,
    worldInstanceId: row.world_id,
    ownerId: row.owner_id,
    activeAreaId: row.active_area_id,
    status: databaseStatusToDomain(row.status),
    version: Number(row.version),
    currentTurn: Number(row.current_turn),
    turnLimit: Number(row.turn_limit),
    focus: Number(row.focus),
    pressure: Number(row.pressure),
    playerEntityId: row.player_entity_id || row.world_state.playerEntityId,
    resolutionSeed: row.resolution_seed,
    endingCode: row.ending_code,
    createdAt: timestamp(row.created_at),
    updatedAt: timestamp(row.updated_at)
  };
}

async function insertEntity(client, { run, gameEntity, spawnedTurn, sourceEntityId = null, areaId = run.activeAreaId, localPosition = gameEntity.position }) {
  await client.query(
    `insert into ${SCHEMA}.entities
       (id, owner_id, run_id, world_id, entity_kind, asset_id, display_name,
        source_entity_id, is_protected, is_cloneable, is_active, state_json, spawned_turn)
     values ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10, $11, $12::jsonb, $13)`,
    [gameEntity.id, run.ownerId, run.id, databaseWorldId(run), entityKindCode(gameEntity.kind),
      gameEntity.assetId, gameEntity.name, sourceEntityId, gameEntity.protected,
      gameEntity.cloneable, gameEntity.active,
      JSON.stringify({ blocking: gameEntity.blocking, ...gameEntity.state }), spawnedTurn]
  );
  if (["player", "npc", "enemy"].includes(gameEntity.kind)) {
    await client.query(
      `insert into ${SCHEMA}.actors
         (entity_id, owner_id, run_id, actor_role, hp, max_hp, energy, max_energy)
       values ($1, $2, $3, $4, $5, $6, $7, $7)`,
      [gameEntity.id, run.ownerId, run.id, gameEntity.kind,
        Number.isInteger(gameEntity.state?.hp) ? gameEntity.state.hp : 1,
        Number.isInteger(gameEntity.state?.maxHp) ? gameEntity.state.maxHp : Math.max(1, gameEntity.state?.hp || 1),
        gameEntity.kind === "player" ? run.focus : 0]
    );
  }
  await client.query(
    `insert into ${SCHEMA}.entity_positions
       (entity_id, owner_id, run_id, world_id, area_id, x, y, blocks_movement)
     values ($1, $2, $3, $4, $5, $6, $7, $8)`,
    [gameEntity.id, run.ownerId, run.id, databaseWorldId(run), areaId,
      localPosition.x, localPosition.y, gameEntity.blocking]
  );
}

async function loadDatabaseAreas(client, run) {
  const result = await client.query(`select id, area_key, origin_x, origin_y, width, height from ${SCHEMA}.areas where world_id = $1 and owner_id = $2`, [databaseWorldId(run), run.ownerId]);
  return result.rows;
}

function databasePlacementForGlobalPosition(run, areaRows, position) {
  if (!Number.isInteger(position?.x) || !Number.isInteger(position?.y)
      || position.x < 0 || position.y < 0
      || position.x >= run.world.width || position.y >= run.world.height) {
    throw new AppError(500, "DATABASE_AREA_MAPPING_FAILED", "A database position must be an integer global world coordinate.");
  }
  const domainAreaKey = areaAt(run.world, position)?.id;
  const area = areaRows.find((candidate) => candidate.area_key === domainAreaKey);
  if (!area) throw new AppError(500, "DATABASE_AREA_MAPPING_FAILED", "A global world coordinate did not map to a generated database area.");
  return {
    areaId: area.id,
    areaKey: area.area_key,
    localPosition: toAreaLocalPosition({
      id: area.area_key,
      bounds: {
        x: Number(area.origin_x),
        y: Number(area.origin_y),
        width: Number(area.width),
        height: Number(area.height)
      }
    }, position)
  };
}

async function synchronizeActiveArea(client, run) {
  const areaRows = await loadDatabaseAreas(client, run);
  const player = run.entities.find((item) => item.id === run.playerEntityId && item.active);
  if (!player) throw new AppError(500, "PLAYER_MISSING", "The authoritative player entity is missing.");
  const placement = databasePlacementForGlobalPosition(run, areaRows, player.position);
  run.activeAreaId = placement.areaId;
  return { areaRows, placement };
}

async function synchronizeEntityState(client, run, turn) {
  const areaRows = await loadDatabaseAreas(client, run);
  for (const event of turn.events) {
    if (event.type === "entity_moved") {
      const placement = databasePlacementForGlobalPosition(run, areaRows, event.to);
      await client.query(
        `update ${SCHEMA}.entity_positions
            set area_id = $4, x = $5, y = $6, revision = revision + 1, updated_at = $7
          where entity_id = $1 and owner_id = $2 and run_id = $3 and removed_at is null`,
        [event.entityId, run.ownerId, run.id, placement.areaId, placement.localPosition.x, placement.localPosition.y, run.updatedAt]
      );
    } else if (event.type === "entity_spawned" || event.type === "slot_enriched" || event.type === "entity_bound_to_slot") {
      const gameEntity = run.entities.find((candidate) => candidate.id === event.entityId);
      if (!gameEntity) throw new AppError(500, "BOUND_ENTITY_MISSING", "A persisted spawn event has no authoritative entity.");
      await insertEntity(client, {
        run,
        gameEntity,
        spawnedTurn: turn.turnNo,
        sourceEntityId: event.type === "entity_spawned" ? turn.request.targetEntityId : null,
        ...databasePlacementForGlobalPosition(run, areaRows, gameEntity.position)
      });
      if (gameEntity.kind === "npc") {
        const relationship = run.npcRelationships.find((item) => item.npcId === gameEntity.id);
        if (relationship) await upsertRelationshipProjection(client, run, relationship, run.updatedAt);
      }
      if (event.type === "entity_bound_to_slot") await bindRuntimeEntityToSlot(client, run, turn, event, gameEntity);
    } else if (event.type === "entity_removed") {
      await client.query(
        `update ${SCHEMA}.entities
            set is_active = false, despawned_turn = $4, updated_at = $5
          where id = $1 and owner_id = $2 and run_id = $3`,
        [event.entityId, run.ownerId, run.id, turn.turnNo, run.updatedAt]
      );
      await client.query(
        `update ${SCHEMA}.entity_positions
            set removed_at = $4, revision = revision + 1, updated_at = $4
          where entity_id = $1 and owner_id = $2 and run_id = $3 and removed_at is null`,
        [event.entityId, run.ownerId, run.id, run.updatedAt]
      );
      await client.query(
        `update ${SCHEMA}.run_slot_bindings
            set status = 'released', released_turn = $4, updated_at = $5
          where entity_id = $1 and owner_id = $2 and run_id = $3 and status <> 'released'`,
        [event.entityId, run.ownerId, run.id, turn.turnNo, run.updatedAt]
      );
    } else if (event.type === "entity_restored") {
      const gameEntity = run.entities.find((candidate) => candidate.id === event.entityId);
      const placement = databasePlacementForGlobalPosition(run, areaRows, gameEntity.position);
      await client.query(
        `update ${SCHEMA}.entities
            set is_active = true, despawned_turn = null, state_json = $4::jsonb, updated_at = $5
          where id = $1 and owner_id = $2 and run_id = $3`,
        [event.entityId, run.ownerId, run.id, JSON.stringify({ blocking: gameEntity.blocking, ...gameEntity.state }), run.updatedAt]
      );
      await client.query(
        `update ${SCHEMA}.entity_positions
            set area_id = $4, x = $5, y = $6, removed_at = null, revision = revision + 1, updated_at = $7
          where entity_id = $1 and owner_id = $2 and run_id = $3`,
        [event.entityId, run.ownerId, run.id, placement.areaId, placement.localPosition.x, placement.localPosition.y, run.updatedAt]
      );
      await client.query(
        `update ${SCHEMA}.run_slot_bindings
            set status = 'active', released_turn = null,
                activation_turn = coalesce(activation_turn, $4), updated_at = $5
          where entity_id = $1 and owner_id = $2 and run_id = $3 and status = 'released'`,
        [event.entityId, run.ownerId, run.id, turn.turnNo, run.updatedAt]
      );
    } else if (["entity_state_restored", "health_changed", "entity_defeated", "entity_fled", "entity_defended", "clue_revealed"].includes(event.type)) {
      const gameEntity = run.entities.find((candidate) => candidate.id === event.entityId);
      await client.query(
        `update ${SCHEMA}.entities set state_json = $4::jsonb, updated_at = $5 where id = $1 and owner_id = $2 and run_id = $3`,
        [event.entityId, run.ownerId, run.id, JSON.stringify({ blocking: gameEntity.blocking, ...gameEntity.state }), run.updatedAt]
      );
      if (Number.isInteger(gameEntity.state?.hp)) {
        await client.query(
          `update ${SCHEMA}.actors set hp = $4, updated_at = $5 where entity_id = $1 and owner_id = $2 and run_id = $3`,
          [event.entityId, run.ownerId, run.id, gameEntity.state.hp, run.updatedAt]
        );
      }
    } else if (event.type === "prop_looted") {
      for (const entityId of [event.entityId, event.actorId]) {
        const gameEntity = run.entities.find((candidate) => candidate.id === entityId);
        if (!gameEntity) continue;
        await client.query(
          `update ${SCHEMA}.entities set state_json = $4::jsonb, updated_at = $5 where id = $1 and owner_id = $2 and run_id = $3`,
          [entityId, run.ownerId, run.id, JSON.stringify({ blocking: gameEntity.blocking, ...gameEntity.state }), run.updatedAt]
        );
      }
    }
  }
}

async function bindRuntimeEntityToSlot(client, run, turn, event, gameEntity) {
  const slot = await client.query(
    `select id from ${SCHEMA}.placement_slots
      where world_id = $1 and owner_id = $2 and slot_key = $3`,
    [databaseWorldId(run), run.ownerId, event.slotId]
  );
  if (slot.rowCount !== 1) throw new AppError(500, "BOUND_SLOT_MISSING", "A validated runtime binding references no persisted world slot.");
  const plan = await generationPlanIdentity(client, run);
  await client.query(
    `insert into ${SCHEMA}.run_slot_bindings
       (run_id, owner_id, world_id, generation_plan_id, slot_id, binding_key, binding_kind,
        plan_node_key, entity_id, status, activation_turn, binding_payload, created_at, updated_at)
     values ($1,$2,$3,$4,$5,$6,'entity',$7,$8,'active',$9,$10::jsonb,$11,$11)`,
    [run.id, run.ownerId, databaseWorldId(run), plan.id, slot.rows[0].id, `entity:${gameEntity.id}`,
      String(gameEntity.state?.campaignRole || gameEntity.state?.evidenceKey || event.slotId),
      gameEntity.id, turn.turnNo,
      JSON.stringify({ domainSlotId: event.slotId, assetId: gameEntity.assetId, geometryOwnedByWorld: true }),
      run.updatedAt]
  );
}

function databaseFactShape(fact) {
  if (fact.subject === "collapse_origin" && fact.predicate === "inside_admin_control_system") {
    return {
      subject: "REGION_DATA_GRAND_LIBRARY",
      predicate: "ROOT_CAUSE_ESSENTIAL_CLUE_ACQUIRED",
      object: {
        acquired: fact.value === true,
        domainSubject: fact.subject,
        domainPredicate: fact.predicate
      }
    };
  }
  return {
    subject: String(fact.subject),
    predicate: String(fact.predicate),
    object: {
      value: clone(fact.value),
      label: fact.label || null,
      factType: fact.type || "canonical"
    }
  };
}

async function persistWorldFact(client, run, fact, updatedAt) {
  const shape = databaseFactShape(fact);
  const current = await client.query(
    `select id from ${SCHEMA}.world_facts
      where run_id = $1 and owner_id = $2 and subject_key = $3 and predicate = $4
        and superseded_by_fact_id is null`,
    [run.id, run.ownerId, shape.subject, shape.predicate]
  );
  if (current.rowCount > 0) {
    await client.query(
      `update ${SCHEMA}.world_facts
          set object_json = $4::jsonb, confidence = 1.000,
              valid_from_turn = $5, valid_until_turn = null, updated_at = $6
        where id = $1 and owner_id = $2 and run_id = $3 and superseded_by_fact_id is null`,
      [current.rows[0].id, run.ownerId, run.id, JSON.stringify(shape.object),
        Number(fact.establishedTurn || 0), updatedAt]
    );
    return;
  }
  await client.query(
    `insert into ${SCHEMA}.world_facts
       (id, owner_id, run_id, subject_key, predicate, object_json,
        confidence, valid_from_turn, created_at, updated_at)
     values ($1,$2,$3,$4,$5,$6::jsonb,1.000,$7,$8,$8)`,
    [fact.id, run.ownerId, run.id, shape.subject, shape.predicate,
      JSON.stringify(shape.object), Number(fact.establishedTurn || 0), updatedAt]
  );
}

async function upsertRelationshipProjection(client, run, relationship, updatedAt) {
  const result = await client.query(
    `insert into ${SCHEMA}.npc_relationships
       (owner_id, run_id, subject_actor_id, object_actor_id, affinity, trust, fear,
        relationship_state, notes, last_changed_turn, created_at, updated_at)
     values ($1,$2,$3,$4,$5,$6,$7,$8,$9::jsonb,$10,$11,$11)
     on conflict (run_id, subject_actor_id, object_actor_id) do update
       set affinity = excluded.affinity,
           trust = excluded.trust,
           fear = excluded.fear,
           relationship_state = excluded.relationship_state,
           notes = excluded.notes,
           last_changed_turn = excluded.last_changed_turn,
           updated_at = excluded.updated_at
     returning id`,
    [run.ownerId, run.id, run.playerEntityId, relationship.npcId,
      Number(relationship.affinity || 0), Number(relationship.trust || 0), Number(relationship.fear || 0),
      relationship.stance || "neutral", JSON.stringify({ domainNpcId: relationship.npcId }),
      Number(relationship.lastChangedTurn || 0), updatedAt]
  );
  return result.rows[0].id;
}

function hookKey(hook) {
  return `hook.${String(hook.id).toLowerCase()}`;
}

async function insertUnresolvedHook(client, run, hook, turnId, updatedAt) {
  await client.query(
    `insert into ${SCHEMA}.unresolved_hooks
       (id, run_id, owner_id, hook_key, introduced_turn_id, introduced_turn_no,
        summary, hook_payload, status, deadline_turn, created_at, updated_at)
     values ($1,$2,$3,$4,$5,$6,$7,$8::jsonb,'OPEN',$9,$10,$10)
     on conflict (run_id, hook_key) do nothing`,
    [hook.id, run.id, run.ownerId, hookKey(hook), turnId,
      Number(hook.createdTurn || 0), hook.summary,
      JSON.stringify({ source: hook.source || "campaign_director" }),
      Number.isInteger(hook.expiresTurn) ? hook.expiresTurn : null, updatedAt]
  );
}

async function persistInitialCodriaState(client, run) {
  for (const fact of run.canonicalFacts || []) {
    await persistWorldFact(client, run, fact, run.createdAt);
  }
  for (const relationship of run.npcRelationships || []) {
    await upsertRelationshipProjection(client, run, relationship, run.createdAt);
  }
  for (const hook of run.unresolvedHooks || []) {
    await insertUnresolvedHook(client, run, hook, null, run.createdAt);
  }
}

async function persistAbilityUsage(client, run, turn) {
  const usage = (turn.stateDelta?.abilityUsageHistory || [])[0];
  if (!usage) throw new AppError(500, "ABILITY_USAGE_HISTORY_MISSING", "A committed Codria skill action requires normalized usage history.");
  await client.query(
    `insert into ${SCHEMA}.ability_usage_history
       (id, run_id, owner_id, turn_id, turn_no, skill_id, action_context,
        target_ids, outcome, effects_json, created_at)
     values ($1,$2,$3,$4,$5,$6,$7,$8::jsonb,$9,$10::jsonb,$11)`,
    [usage.id, run.id, run.ownerId, turn.id, turn.turnNo, usage.skillId,
      usage.actionContext, JSON.stringify(usage.targetIds || []), usage.outcome,
      JSON.stringify({ d20: usage.d20, forcedOverride: usage.forcedOverride === true,
        eventTypes: [...new Set((turn.events || []).map((event) => event.type))] }), turn.createdAt]
  );
}

async function persistAdminAccessHistory(client, run, turn, areaIds) {
  for (const acquisition of turn.stateDelta?.adminAccessHistory || []) {
    const areaId = areaIds.get(acquisition.areaId);
    if (!areaId) throw new AppError(500, "ADMIN_ACCESS_AREA_MISSING", "Administrator access history references no persisted sealed area.");
    await client.query(
      `insert into ${SCHEMA}.admin_access_acquisition_history
         (id, run_id, owner_id, world_id, turn_id, turn_no, admin_access_code,
          region_axis_code, area_id, action_context, acquisition_method, skill_id,
          evidence, acquired_at)
       values ($1,$2,$3,$4,$5,$6,$7,$8,$9,$10,$11,$12,$13::jsonb,$14)`,
      [acquisition.id, run.id, run.ownerId, databaseWorldId(run), turn.id, turn.turnNo,
        acquisition.accessLevelId, acquisition.regionAxis, areaId, acquisition.actionContext,
        `${acquisition.skillId}:${acquisition.actionContext}:${acquisition.candidateId}`,
        acquisition.skillId, JSON.stringify({ candidateId: acquisition.candidateId,
          targetIds: acquisition.targetIds || [], domainAreaId: acquisition.areaId }), turn.createdAt]
    );
  }
}

function choiceKeyPart(value) {
  return String(value || "unknown").toLowerCase().replace(/[^a-z0-9_.:-]+/g, "-");
}

async function persistMajorChoices(client, run, turn) {
  for (const choice of turn.stateDelta?.majorChoices || []) {
    const choiceKey = `choice.${choiceKeyPart(choice.type)}.${choiceKeyPart(choice.accessLevelId || choice.id)}`;
    const optionKey = `path.${choiceKeyPart(choice.regionAxis)}.${choiceKeyPart(choice.skillId)}.${choiceKeyPart(choice.actionContext)}`;
    await client.query(
      `insert into ${SCHEMA}.major_choices
         (id, run_id, owner_id, turn_id, turn_no, choice_key, option_key,
          region_axis_code, action_context, immediate_effects, long_term_tags, created_at)
       values ($1,$2,$3,$4,$5,$6,$7,$8,$9,$10::jsonb,$11::jsonb,$12)`,
      [choice.id, run.id, run.ownerId, turn.id, turn.turnNo, choiceKey, optionKey,
        choice.regionAxis || null, choice.actionContext || turn.actionContext,
        JSON.stringify({ adminAccessCode: choice.accessLevelId || null, skillId: choice.skillId || turn.request.skillId }),
        JSON.stringify(["admin_access_path", choice.regionAxis, choice.accessLevelId].filter(Boolean)), turn.createdAt]
    );
  }
}

async function persistRegionOutcomes(client, run, turn) {
  for (const outcome of turn.stateDelta?.regionOutcomes || []) {
    const sequence = await client.query(
      `select coalesce(max(sequence_no), 0) + 1 as sequence_no
         from ${SCHEMA}.region_outcomes where run_id = $1 and owner_id = $2 and region_axis_code = $3`,
      [run.id, run.ownerId, outcome.regionAxis]
    );
    await client.query(
      `insert into ${SCHEMA}.region_outcomes
         (run_id, owner_id, turn_id, turn_no, region_axis_code, sequence_no,
          outcome_key, outcome_status, outcome_state, ending_tags, created_at)
       values ($1,$2,$3,$4,$5,$6,$7,'STABILIZED',$8::jsonb,$9::jsonb,$10)`,
      [run.id, run.ownerId, turn.id, turn.turnNo, outcome.regionAxis,
        Number(sequence.rows[0].sequence_no), choiceKeyPart(outcome.outcome || "region_updated"),
        JSON.stringify(outcome), JSON.stringify([outcome.regionAxis, outcome.accessLevelId].filter(Boolean)), turn.createdAt]
    );
  }
}

async function persistRelationshipHistory(client, beforeRun, run, turn) {
  for (const relationship of turn.stateDelta?.relationships || []) {
    const before = (beforeRun.npcRelationships || []).find((candidate) => candidate.npcId === relationship.npcId)
      || { affinity: 0, trust: 0, fear: 0, stance: "neutral" };
    const affinityDelta = Number(relationship.affinity || 0) - Number(before.affinity || 0);
    const trustDelta = Number(relationship.trust || 0) - Number(before.trust || 0);
    const fearDelta = Number(relationship.fear || 0) - Number(before.fear || 0);
    const relationshipId = await upsertRelationshipProjection(client, run, relationship, turn.createdAt);
    if (affinityDelta === 0 && trustDelta === 0 && fearDelta === 0) continue;
    await client.query(
      `insert into ${SCHEMA}.npc_relationship_history
         (relationship_id, run_id, owner_id, turn_id, turn_no,
          affinity_delta, trust_delta, fear_delta,
          affinity_after, trust_after, fear_after, relationship_state_after,
          reason_code, context, created_at)
       values ($1,$2,$3,$4,$5,$6,$7,$8,$9,$10,$11,$12,$13,$14::jsonb,$15)`,
      [relationshipId, run.id, run.ownerId, turn.id, turn.turnNo,
        affinityDelta, trustDelta, fearDelta, Number(relationship.affinity || 0),
        Number(relationship.trust || 0), Number(relationship.fear || 0),
        relationship.stance || "neutral", "DIRECTOR_CONFIRMED_CHANGE",
        JSON.stringify({ actionContext: turn.actionContext, skillId: turn.request.skillId }), turn.createdAt]
    );
  }
}

async function persistHookTransitions(client, run, turn) {
  for (const hook of turn.stateDelta?.openLoops || []) {
    await insertUnresolvedHook(client, run, hook, turn.id, turn.createdAt);
  }
  for (const event of turn.events || []) {
    if (event.type !== "open_loop_closed") continue;
    await client.query(
      `update ${SCHEMA}.unresolved_hooks
          set status = 'RESOLVED', resolution_turn_id = $4,
              resolution_turn_no = $5, resolution_kind = $6,
              resolved_at = $7, updated_at = $7
        where id = $1 and run_id = $2 and owner_id = $3 and status = 'OPEN'`,
      [event.loopId, run.id, run.ownerId, turn.id, turn.turnNo,
        choiceKeyPart(event.reason || "campaign_resolution").toUpperCase(), turn.createdAt]
    );
  }
}

function debtResolutionType(skillId) {
  return skillId === "RESTORE" ? "RECOVERY" : "ACCEPT_RESPONSIBILITY";
}

async function persistTechnicalDebt(client, run, turn) {
  for (const entry of turn.stateDelta?.technicalDebtEntries || []) {
    if (entry.turnNo === turn.turnNo && entry.debtDelta > 0) {
      await client.query(
        `insert into ${SCHEMA}.technical_debt_entries
           (id, run_id, owner_id, turn_id, turn_no, skill_id, operation_type,
            target_id, forced_override, debt_delta, deferred_consequence_type,
            metadata, created_at)
         values ($1,$2,$3,$4,$5,$6,$7,$8,$9,$10,$11,$12::jsonb,$13)`,
        [entry.id, run.id, run.ownerId, turn.id, turn.turnNo, entry.skillId,
          entry.operationType, entry.targetId, entry.forcedOverride === true,
          entry.debtDelta, entry.deferredConsequenceType,
          JSON.stringify({ actionContext: entry.actionContext || turn.actionContext }), turn.createdAt]
      );
    }
    if (entry.resolvedAt === turn.turnNo) {
      const result = await client.query(
        `update ${SCHEMA}.technical_debt_entries
            set resolved_at = $4, resolved_by_turn_id = $5, resolution_type = $6
          where id = $1 and run_id = $2 and owner_id = $3 and resolved_at is null`,
        [entry.id, run.id, run.ownerId, turn.createdAt, turn.id,
          debtResolutionType(turn.request.skillId)]
      );
      if (result.rowCount !== 1) {
        throw new AppError(500, "TECHNICAL_DEBT_HISTORY_MISSING", "A resolved technical-debt cause was not present in PostgreSQL.");
      }
    }
  }
}

async function persistCodriaTurnHistory(client, beforeRun, run, turn) {
  const areaRows = await loadDatabaseAreas(client, run);
  const areaIds = new Map(areaRows.map((row) => [row.area_key, row.id]));
  for (const fact of turn.stateDelta?.facts || []) {
    await persistWorldFact(client, run, fact, turn.createdAt);
  }
  await persistRelationshipHistory(client, beforeRun, run, turn);
  await persistAbilityUsage(client, run, turn);
  await persistAdminAccessHistory(client, run, turn, areaIds);
  await persistMajorChoices(client, run, turn);
  await persistRegionOutcomes(client, run, turn);
  await persistHookTransitions(client, run, turn);
  await persistTechnicalDebt(client, run, turn);
}

async function synchronizeReversibleActions(client, run, turn) {
  for (const entry of run.reversibleLedger) {
    await client.query(
      `insert into ${SCHEMA}.reversible_actions
         (run_id, owner_id, turn_no, ability, inverse_ops, consumed, consumed_turn, created_at, updated_at)
       values ($1, $2, $3, $4, $5::jsonb, $6, $7, $8, $8)
       on conflict (run_id, turn_no) do update
       set inverse_ops = excluded.inverse_ops,
           consumed = excluded.consumed,
           consumed_turn = excluded.consumed_turn,
           updated_at = excluded.updated_at`,
      [run.id, run.ownerId, entry.turnNo, entry.ability, JSON.stringify(entry.inverseOps), entry.consumed,
        entry.consumed ? turn.turnNo : null, run.updatedAt]
    );
  }
}

async function insertTurnRuleResolution(client, turn) {
  const modifier = Number(turn.dice?.modifier || 0);
  const d20 = Number(turn.d20);
  const costs = {};
  for (const event of turn.events || []) {
    if (event.type === "resource_changed" && typeof event.resource === "string" && Number.isFinite(event.delta)) {
      costs[event.resource] = (costs[event.resource] || 0) + event.delta;
    }
  }
  await client.query(
    `insert into ${SCHEMA}.turn_rule_resolutions
       (turn_record_id, run_id, owner_id, turn_no, ruleset_version, normalized_attempt,
        d20_raw, modifier_total, modifier_breakdown, roll_total, difficulty_class, outcome,
        consequence_budget, costs_json, guaranteed_operations, allowed_effects, state_delta,
        state_hash_before, state_hash_after, rng_audit, created_at)
     values ($1,$2,$3,$4,$5,$6::jsonb,$7,$8,$9::jsonb,$10,$11,$12,$13,$14::jsonb,
             $15::jsonb,$16::jsonb,$17::jsonb,$18,$19,$20::jsonb,$21)`,
    [turn.id, turn.runId, turn.ownerId, turn.turnNo, turn.rulesetVersion,
      JSON.stringify({ text: turn.normalizedAttempt, ability: turn.request?.ability,
        abilitySource: turn.request?.abilitySource, intentAnalysis: turn.intentAnalysis }),
      d20, modifier, JSON.stringify(turn.dice?.modifiers || []), d20 + modifier,
      turn.dice?.difficulty, turn.outcome, turn.consequenceBudget, JSON.stringify(costs),
      JSON.stringify(turn.stateDelta?.appliedOps || []),
      JSON.stringify([...new Set((turn.events || []).map((event) => event.type))]),
      JSON.stringify(turn.stateDelta || {}), turn.stateHashBefore, turn.stateHashAfter,
      JSON.stringify(turn.dice?.rngAudit || { secretRedacted: true }), turn.createdAt]
  );
}

function entityKindCode(kind) {
  const code = { player: "PLAYER", npc: "NPC", enemy: "ENEMY", prop: "PROP", item: "ITEM" }[kind];
  if (!code) throw new AppError(500, "ENTITY_KIND_INVALID", "An authoritative entity kind has no database mapping.");
  return code;
}

function eventCatalogCode(type) {
  return {
    entity_moved: "ENTITY_MOVED",
    entity_spawned: "ENTITY_COPIED",
    entity_removed: "ENTITY_DELETED",
    entity_restored: "ENTITY_RESTORED",
    entity_state_restored: "ENTITY_RESTORED",
    entity_interacted: "ENTITY_INTERACTED",
    health_changed: "CONSEQUENCE_APPLIED",
    connection_created: "CONNECTION_CREATED",
    connection_removed: "CONNECTION_REMOVED",
    connection_expired: "CONNECTION_EXPIRED",
    story_beat_changed: "STORY_BEAT_CHANGED",
    open_loop_created: "OPEN_LOOP_CREATED",
    open_loop_closed: "OPEN_LOOP_CLOSED",
    rumor_added: "RUMOR_ADDED",
    rumor_closed: "RUMOR_CLOSED",
    slot_enriched: "SLOT_ENRICHED",
    entity_bound_to_slot: "SLOT_BOUND",
    reversible_reward_spent: "REVERSAL_APPLIED",
    fact_established: "FACT_ESTABLISHED",
    npc_memory_added: "NPC_MEMORY_ADDED",
    npc_memory_expired: "NPC_MEMORY_EXPIRED",
    relationship_changed: "RELATIONSHIP_CHANGED",
    quest_started: "QUEST_STARTED",
    quest_updated: "QUEST_UPDATED",
    quest_closed: "QUEST_CLOSED",
    resource_changed: "CONSEQUENCE_APPLIED",
    progress_token_granted: "PROGRESS_STATE_CHANGED",
    progress_level_changed: "PROGRESS_STATE_CHANGED",
    admin_access_acquired: "ADMIN_ACCESS_ACQUIRED",
    major_choice_recorded: "MAJOR_CHOICE_RECORDED",
    canonical_fact_confirmed: "FACT_ESTABLISHED",
    technical_debt_recorded: "TECHNICAL_DEBT_CHANGED",
    technical_debt_resolved: "DEFERRED_CONSEQUENCE_RESOLVED",
    campaign_metrics_changed: "CAMPAIGN_METRICS_CHANGED",
    encounter_resolved: "ENCOUNTER_RESOLVED",
    finale_puzzle_matched: "PROGRESS_STATE_CHANGED",
    finale_resolved: "PROGRESS_STATE_CHANGED",
    negotiation_resolved: "NEGOTIATION_RESOLVED",
    visual_intent_recorded: "VISUAL_INTENT_RECORDED",
    status_added: "CONSEQUENCE_APPLIED",
    pressure_changed: "CONSEQUENCE_APPLIED",
    turn_committed: "TURN_COMMITTED",
    run_completed: "RUN_COMPLETED"
  }[type] || "CONSEQUENCE_APPLIED";
}

function rowToTurn(row) {
  return {
    ...row.result_json,
    id: row.id,
    runId: row.run_id,
    ownerId: row.owner_id,
    turnNo: Number(row.turn_no),
    idempotencyKey: row.idempotency_key,
    requestFingerprint: row.request_fingerprint,
    expectedRunVersion: Number(row.expected_run_version),
    committedRunVersion: Number(row.committed_run_version),
    request: row.request_json,
    narrative: row.narrative_json,
    createdAt: timestamp(row.created_at)
  };
}

function rowToNavigation(row) {
  return {
    id: row.id,
    runId: row.run_id,
    sequence: Number(row.sequence_no),
    idempotencyKey: row.idempotency_key,
    requestFingerprint: row.request_fingerprint,
    expectedRunVersion: Number(row.expected_run_version),
    committedRunVersion: Number(row.committed_run_version),
    from: { x: Number(row.from_x), y: Number(row.from_y) },
    requestedDestination: {
      ...(row.turn_context?.requestedAreaKey ? { areaId: row.turn_context.requestedAreaKey } : {}),
      x: Number(row.requested_x),
      y: Number(row.requested_y)
    },
    to: {
      ...(row.turn_context?.enteredAreaKey ? { areaId: row.turn_context.enteredAreaKey } : {}),
      x: Number(row.to_x),
      y: Number(row.to_y)
    },
    path: row.path_json,
    pathCost: Number(row.path_cost),
    travelTimeUnits: Number(row.travel_time_units),
    cumulativeTravelTimeUnits: Number(row.cumulative_travel_time_units),
    enteredAreaId: row.entered_area_key,
    enteredBiomeId: row.entered_biome_id,
    campaignRole: row.campaign_role,
    traversedAreaIds: row.traversed_area_ids,
    reachedPoiIds: row.reached_poi_ids,
    encounterOpened: row.encounter_opened,
    encounter: row.encounter_json,
    sceneDecision: row.turn_context?.sceneDecision || null,
    sceneSequence: row.turn_context?.sceneSequence || [],
    events: row.turn_context?.events || [],
    narrative: row.turn_context?.narrative || null,
    campaignTurnConsumed: row.campaign_turn_consumed,
    campaignTurnBefore: Number(row.campaign_turn_before),
    campaignTurnAfter: Number(row.campaign_turn_after),
    layoutHash: row.layout_hash,
    createdAt: timestamp(row.created_at)
  };
}

async function updateRunRow(client, run) {
  const completedAt = run.status === "completed" || run.status === "abandoned" ? run.updatedAt : null;
  const result = await client.query(
    `update ${SCHEMA}.runs
        set status = $3,
            version = $4,
            current_turn = $5,
            focus = $6,
            pressure = $7,
            player_entity_id = $8,
            active_area_id = $9,
            world_state = $10::jsonb,
            ending_code = $11,
            completed_at = $12,
            updated_at = $13
      where id = $1 and owner_id = $2
      returning updated_at`,
    [run.id, run.ownerId, domainStatusToDatabase(run.status), run.version, run.currentTurn,
      run.focus, run.pressure, run.playerEntityId, run.activeAreaId, JSON.stringify(runStateForDatabase(run)),
      run.endingCode, completedAt, run.updatedAt]
  );
  if (result.rowCount !== 1) throw new AppError(500, "DATABASE_RUN_UPDATE_FAILED", "The authoritative run update did not affect exactly one row.");
  run.updatedAt = timestamp(result.rows[0].updated_at);
  await synchronizeProgressState(client, run);
}

function databaseStatusToDomain(status) {
  return status === "playing" ? "active" : status;
}

function domainStatusToDatabase(status) {
  return status === "active" ? "playing" : status;
}

function timestamp(value) {
  return value instanceof Date ? value.toISOString() : value;
}

function mapDatabaseError(error) {
  if (error instanceof AppError) return error;
  if (error?.code === "23505") return new AppError(409, "DATABASE_CONFLICT", "A unique database constraint was violated.");
  if (error?.code === "23503") return new AppError(409, "DATABASE_REFERENCE_CONFLICT", "A sealed run, world, plan, or owner reference did not match.", { constraint: error.constraint, databaseMessage: error.message });
  if (error?.code === "23514") return new AppError(422, "DATABASE_CONTRACT_VIOLATION", "Persisted state violated a PostgreSQL authority contract.", { constraint: error.constraint, databaseMessage: error.message });
  if (error?.code === "40001" || error?.code === "40P01") return new AppError(409, "TRANSACTION_RETRY", "Concurrent state changed; retry the request with fresh run state.");
  if (error?.code === "42P01" || error?.code === "3F000") return new AppError(500, "DATABASE_SCHEMA_MISSING", "Apply Database migrations before starting postgres storage.");
  return error;
}
