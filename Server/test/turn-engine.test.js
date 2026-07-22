import test from "node:test";
import assert from "node:assert/strict";
import { randomUUID } from "node:crypto";
import { AppError } from "../src/errors.js";
import { createCampaignBlueprint, prioritizedFinaleConnectionPairs, prioritizedFinaleRemovalTargets } from "../src/domain/campaign.js";
import { areaAt, generateWorld, isWalkable } from "../src/domain/world.js";
import { FixedD20Source, createRunState, isMonsterEncounterRequest, normalizeCommittedNarrative, normalizeTravelRequest, normalizeTurnRequest, publicRun, publicTurn, resolveSafeTravel, resolveTurn, tryForceMonsterEncounter } from "../src/domain/turn-engine.js";
import { capabilitiesFor } from "../src/domain/entity-capabilities.js";
import { enemyArchetype } from "../src/domain/enemy-archetypes.js";
import { detectIntentActions } from "../src/domain/intent.js";
import { buildSkillTargetContext, planSkillTarget } from "../src/domain/skill-target-orchestrator.js";
import { planDeterministicDecisionScene, resolveTravelDecision } from "../src/domain/decision-orchestrator.js";

test("Korean investigation language maps to SEARCH rather than generic interaction", () => {
  assert.deepEqual(detectIntentActions("관리자 키보드로 숲의 균열을 조사해 본다"), ["search"]);
  assert.deepEqual(detectIntentActions("주변을 살펴보고 기록을 찾는다"), ["search"]);
});

test("seeded enemy dependencies are deterministic and vary while explicit archetypes stay fixed", () => {
  const id = "12345678-1234-5678-9abc-def012345678";
  const first = enemyArchetype("enemy.slime.v1", 100, id);
  assert.equal(enemyArchetype("enemy.slime.v1", 100, id), first);
  const variants = new Set(Array.from({ length: 60 }, (_, index) =>
    enemyArchetype("enemy.slime.v1", 100 + index, id)));
  assert.ok(variants.size > 1);
  assert.equal(enemyArchetype("enemy.dragon.v1", 100, id), "root_process");
});

const OWNER_ID = "33333333-3333-4333-8333-333333333333";

function runFixture(seed = 44, runId = randomUUID()) {
  const now = "2026-07-17T00:00:00.000Z";
  const campaign = {
    id: randomUUID(),
    ownerId: OWNER_ID,
    turnLimit: 30,
    ...createCampaignBlueprint({ worldSeed: seed, turnLimit: 30 }),
    world: generateWorld(seed)
  };
  return createRunState({ campaign, ownerId: OWNER_ID, runId, now, resolutionSeed: "private-test-seed" });
}

test("committed narrative keeps one authoritative page when the model repeats a confirmed action as narration", () => {
  const actionId = "11111111-1111-4111-8111-111111111111";
  const actionText = "넙죽이가 1의 피해를 입었다.";
  const narrative = normalizeCommittedNarrative({
    summary: "피격 직후의 선택",
    body: "공격의 여파가 남았다.",
    dialogue: [],
    storySequence: [
      { type: "NARRATION", speakerId: null, actionId: null, text: actionText },
      { type: "MONOLOGUE", speakerId: null, actionId: null, text: "이대로 물러설 수는 없다." }
    ]
  }, { appliedOps: [], rejectedOps: [] }, "fallback", {
    sceneSequence: [{ type: "DAMAGE", actionId, text: actionText }]
  });

  assert.deepEqual(narrative.storySequence, [
    { type: "WORLD_ACTION", speakerId: null, actionId, text: actionText },
    { type: "MONOLOGUE", speakerId: null, actionId: null, text: "이대로 물러설 수는 없다." }
  ]);
});

test("campaign initial records receive deterministic run-scoped identities", () => {
  const firstRunId = "11111111-1111-4111-8111-111111111111";
  const secondRunId = "22222222-2222-4222-8222-222222222222";
  const first = runFixture(20260718, firstRunId);
  const repeated = runFixture(20260718, firstRunId);
  const second = runFixture(20260718, secondRunId);
  const withoutIds = (records) => records.map(({ id, ...record }) => record);

  assert.deepEqual(first.canonicalFacts, repeated.canonicalFacts,
    "the same run identity and seed must reproduce exactly the same fact identities");
  assert.deepEqual(first.rumors, repeated.rumors,
    "the same run identity and seed must reproduce exactly the same rumor identities");
  assert.deepEqual(withoutIds(first.canonicalFacts), withoutIds(second.canonicalFacts));
  assert.deepEqual(withoutIds(first.rumors), withoutIds(second.rumors));

  const firstIds = new Set([...first.canonicalFacts, ...first.rumors].map((record) => record.id));
  const secondIds = new Set([...second.canonicalFacts, ...second.rumors].map((record) => record.id));
  assert.equal(firstIds.size, first.canonicalFacts.length + first.rumors.length);
  assert.equal(secondIds.size, second.canonicalFacts.length + second.rumors.length);
  assert.deepEqual([...firstIds].filter((id) => secondIds.has(id)), [],
    "different runs from one campaign seed must never share a run-owned record UUID");
});

test("forced monster encounter deterministically spawns when no dormant enemy remains", () => {
  const exhaustedRun = (seed) => {
    const run = runFixture(seed);
    const player = run.entities.find((entity) => entity.id === run.playerEntityId);
    run.entities = run.entities.filter((entity) => !(entity.kind === "enemy" && entity.active === false
      && entity.state?.activationState === "DORMANT"));
    for (const entity of run.entities) {
      if (entity.id === player.id || !entity.active || !entity.blocking) continue;
      if (Math.abs(entity.position.x - player.position.x) + Math.abs(entity.position.y - player.position.y) <= 1) entity.active = false;
    }
    return run;
  };
  const firstRun = exhaustedRun(31);
  const secondRun = exhaustedRun(32);
  const first = tryForceMonsterEncounter(firstRun, "몬스터와 조우한다", 1, "forced-monster-fallback-1");
  const second = tryForceMonsterEncounter(secondRun, "몬스터와 조우한다", 1, "forced-monster-fallback-1");

  assert.ok(first);
  assert.ok(second);
  assert.equal(first.assetId, second.assetId);
  assert.equal(first.state.activationState, "ACTIVE");
  assert.equal(firstRun.activeEncounter.entityId, first.id);
  assert.equal(firstRun.activeEncounter.spawnKind, "enemy");
});

test("natural Korean encounter conjugations match without treating 목적지 as an enemy", () => {
  for (const text of ["몬스터를 만난다", "괴물과 마주쳤다", "적을 찾아 대결한다", "마물이 나타났다"])
    assert.equal(isMonsterEncounterRequest(text), true, text);
  for (const text of ["summon a monster nearby", "spawn an enemy", "find a monster to fight"])
    assert.equal(isMonsterEncounterRequest(text), true, text);
  for (const text of ["목적지를 찾는다", "이 목적에 맞는 기록을 수색한다", "적절한 길을 발견한다"])
    assert.equal(isMonsterEncounterRequest(text), false, text);
});

test("entity capabilities expose world-editing rules independently from entity kind", () => {
  const removable = capabilitiesFor({ kind: "prop", active: true, protected: false, cloneable: false, state: { finaleComponent: "threat" } });
  const finaleAnchor = capabilitiesFor({ kind: "prop", active: true, protected: true, cloneable: false, state: { finaleComponent: "anchor" } });
  const ambient = capabilitiesFor({ kind: "prop", active: true, protected: false, cloneable: true, state: {} });
  const hostile = capabilitiesFor({ kind: "enemy", active: true, protected: false, cloneable: false, state: {} });
  assert.deepEqual({ canDelete: removable.canDelete, requiredAdminAccess: removable.requiredAdminAccess, reward: removable.grantsDefeatReward }, { canDelete: true, requiredAdminAccess: 3, reward: false });
  assert.equal(removable.canConnect, true);
  assert.equal(finaleAnchor.canConnect, true);
  assert.equal(ambient.canCopy, true);
  assert.equal(ambient.canInteract, true);
  assert.equal(ambient.canConnect, false);
  assert.equal(hostile.canConnect, true);
  assert.equal(hostile.grantsDefeatReward, true);
});

test("Delete deals five damage, records defeat, and changes the monster relationship", () => {
  const run = runFixture(5701);
  const player = run.entities.find((item) => item.id === run.playerEntityId);
  const target = run.entities.find((item) => item.kind === "enemy" && item.active);
  player.position = { x: target.position.x - 3, y: target.position.y };
  target.state.revealed = true;
  const hpBefore = target.state.hp;
  const result = resolveTurn({
    run,
    request: normalizeSkillRequest({ idempotencyKey: "relationship-delete-5701", expectedRunVersion: 1,
      ability: "delete", targetEntityId: target.id }),
    d20Source: new FixedD20Source(20)
  });
  const committedTarget = result.run.entities.find((item) => item.id === target.id);
  assert.equal(committedTarget.state.hp, Math.max(0, hpBefore - 5));
  assert.ok(result.turn.events.some((event) => event.type === "health_changed" && event.entityId === target.id));
  if (hpBefore <= 5) {
    assert.equal(committedTarget.active, false);
    assert.ok(result.turn.events.some((event) => event.type === "entity_removed" && event.entityId === target.id));
  }
  const relationship = result.run.npcRelationships.find((item) => item.npcId === target.id);
  assert.equal(relationship.encounterStatus, "withdrawn");
  assert.ok(result.turn.events.some((event) => event.type === "encounter_intervention"));
  assert.ok(result.turn.events.some((event) => event.type === "relationship_changed" && event.npcId === target.id));
});

test("partial success emits an ability-specific deterministic consequence", () => {
  const run = runFixture(57);
  const player = run.entities.find((item) => item.id === run.playerEntityId);
  const target = run.entities.find((item) => item.active && item.id !== player.id);
  player.position = { x: target.position.x - 1, y: target.position.y };
  const result = resolveTurn({
    run,
    request: normalizeSkillRequest({
      idempotencyKey: "partial-search-0001", expectedRunVersion: run.version,
      ability: "search", targetEntityId: target.id, intent: "불완전한 단서를 추적한다"
    }),
    d20Source: new FixedD20Source(2)
  });
  assert.equal(result.turn.outcome, "partial_success");
  assert.ok(result.turn.events.some((event) => event.type === "partial_search_noise"));
  assert.ok(!result.turn.events.some((event) => event.type === "status_added"));
  assert.equal(result.run.exposed, true);
});

test("an encounter persists across uncertain actions and closes only on a confirmed resolution", () => {
  const run = runFixture(5710);
  const player = run.entities.find((item) => item.id === run.playerEntityId);
  const enemy = run.entities.find((item) => item.kind === "enemy" && item.active);
  player.position = { x: enemy.position.x - 1, y: enemy.position.y };
  enemy.state.revealed = true;
  run.activeEncounter = {
    id: "encounter-lifecycle-test", status: "active", mode: "confrontation", escalation: "stable",
    kind: "COMBAT", sourceEntityId: enemy.id, title: "시험 조우"
  };
  run.encounterHistory = [structuredClone(run.activeEncounter)];
  const observed = resolveTurn({
    run,
    request: normalizeSkillRequest({ idempotencyKey: "encounter-search-5710", expectedRunVersion: run.version, ability: "search", targetEntityId: enemy.id }),
    d20Source: new FixedD20Source(2)
  });
  assert.equal(observed.run.activeEncounter?.status, "active");
  assert.equal(observed.run.activeEncounter?.mode, "observation");
  assert.ok(observed.turn.events.some((event) => event.type === "encounter_continued"));

  const closed = resolveTurn({
    run: observed.run,
    request: normalizeSkillRequest({ idempotencyKey: "encounter-delete-5710", expectedRunVersion: observed.run.version, ability: "delete", targetEntityId: enemy.id }),
    d20Source: new FixedD20Source(20)
  });
  assert.equal(closed.run.activeEncounter, null);
  assert.ok(closed.turn.events.some((event) => event.type === "encounter_resolved" && event.resolution === "defeated_or_withdrawn"));
});

test("a lethal partial success closes combat instead of targeting a removed enemy", () => {
  const run = runFixture(5712);
  const player = run.entities.find((item) => item.id === run.playerEntityId);
  const enemy = run.entities.find((item) => item.kind === "enemy" && item.active);
  player.position = { x: enemy.position.x - 1, y: enemy.position.y };
  enemy.state.revealed = true;
  enemy.state.hp = 1;
  run.activeEncounter = {
    id: "encounter-partial-defeat-test", status: "active", mode: "combat", escalation: "stable",
    kind: "COMBAT", sourceEntityId: enemy.id, title: "부분 성공 처치 시험"
  };
  run.encounterHistory = [structuredClone(run.activeEncounter)];

  const defeated = resolveTurn({
    run,
    request: normalizeSkillRequest({ idempotencyKey: "encounter-partial-defeat-5712", expectedRunVersion: run.version, ability: "delete", targetEntityId: enemy.id }),
    d20Source: new FixedD20Source(8)
  });

  assert.equal(defeated.turn.outcome, "partial_success");
  assert.equal(defeated.run.activeEncounter, null);
  assert.ok(defeated.turn.events.some((event) => event.type === "enemy_defeated" && event.entityId === enemy.id));
  assert.ok(defeated.turn.events.some((event) => event.type === "encounter_resolved" && event.resolution === "defeated_or_withdrawn"));
  assert.ok(!defeated.turn.events.some((event) => event.type === "encounter_continued"));
});

test("a legacy save with an orphaned combat encounter self-heals on the next action", () => {
  const run = runFixture(5713);
  const enemy = run.entities.find((item) => item.kind === "enemy" && item.active);
  enemy.active = false;
  enemy.state.disabled = true;
  run.focus = Math.max(0, run.maxFocus - 1);
  run.activeEncounter = {
    id: "legacy-orphaned-encounter", status: "active", mode: "combat", escalation: "stable",
    kind: "COMBAT", sourceEntityId: enemy.id, title: "이미 끝난 조우", lastAction: "delete",
    lastOutcome: "partial_success"
  };
  run.encounterHistory = [structuredClone(run.activeEncounter)];

  const recovered = resolveTurn({
    run,
    request: normalizeSkillRequest({ idempotencyKey: "legacy-orphaned-recovery-5713", expectedRunVersion: run.version, ability: "rest" }),
    d20Source: new FixedD20Source(20)
  });

  assert.equal(recovered.run.activeEncounter, null);
  assert.ok(recovered.turn.events.some((event) => event.type === "encounter_state_reconciled"
    && event.encounterId === "legacy-orphaned-encounter"));
  assert.ok(!recovered.turn.events.some((event) => event.type === "encounter_continued"));
});

test("an exhausted player can recover focus without escaping an active encounter", () => {
  const run = runFixture(5711);
  const enemy = run.entities.find((item) => item.kind === "enemy" && item.active);
  run.activeEncounter = {
    id: "encounter-rest-test", status: "active", mode: "combat", escalation: "stable",
    kind: "COMBAT", sourceEntityId: enemy.id, title: "회복 시험 조우"
  };
  run.encounterHistory = [structuredClone(run.activeEncounter)];

  assert.throws(() => resolveTurn({
    run: structuredClone(run),
    request: normalizeSkillRequest({ idempotencyKey: "encounter-rest-blocked-5711", expectedRunVersion: run.version, ability: "rest" }),
    d20Source: new FixedD20Source(20)
  }), (error) => error instanceof AppError && error.code === "ENCOUNTER_ACTION_REQUIRED");

  run.focus = 0;
  const recovered = resolveTurn({
    run,
    request: normalizeSkillRequest({ idempotencyKey: "encounter-rest-recovery-5711", expectedRunVersion: run.version, ability: "rest" }),
    d20Source: new FixedD20Source(20)
  });
  assert.equal(recovered.run.focus, 3);
  assert.equal(recovered.run.activeEncounter?.status, "active");
  assert.equal(recovered.run.activeEncounter?.lastAction, "rest");
  assert.ok(recovered.turn.events.some((event) => event.type === "encounter_continued" && event.action === "rest"));
});

test("technical debt thresholds mutate authoritative state exactly once", () => {
  const run = runFixture(58);
  run.metrics.technicalDebt = 75;
  const player = run.entities.find((item) => item.id === run.playerEntityId);
  const target = run.entities.find((item) => item.active && item.id !== player.id);
  player.position = { x: target.position.x - 1, y: target.position.y };
  const first = resolveTurn({
    run,
    request: normalizeSkillRequest({ idempotencyKey: "debt-trigger-0001", expectedRunVersion: run.version,
      ability: "search", targetEntityId: target.id }),
    d20Source: new FixedD20Source(20)
  });
  assert.deepEqual(first.run.debtThresholdsTriggered, [25, 50, 75]);
  assert.ok(first.turn.events.some((event) => event.type === "debt_backflow_clone_drift"));
  assert.ok(first.turn.events.some((event) => ["debt_backflow_hostile_spawned", "debt_backflow_blocked"].includes(event.type)));
  assert.ok(first.turn.events.some((event) => event.type === "debt_paradox_surge"));

  const second = resolveTurn({
    run: first.run,
    request: normalizeSkillRequest({ idempotencyKey: "debt-trigger-0002", expectedRunVersion: first.run.version,
      ability: "search", targetEntityId: target.id }),
    d20Source: new FixedD20Source(20)
  });
  assert.ok(!second.turn.events.some((event) => event.type.startsWith("debt_backflow_") || event.type.startsWith("debt_paradox_")));
});

test("public ending board reports concrete missing recipe conditions", () => {
  const run = runFixture(59);
  run.progressLevel = 3;
  const snapshot = publicRun(run);
  assert.equal(snapshot.endingConditionReports.length, run.endingCandidates.filter((ending) => !ending.emergency).length);
  assert.ok(snapshot.endingConditionReports.every((report) => report.totalCount > 1));
  assert.ok(snapshot.endingConditionReports.some((report) => report.conditions.some((condition) => !condition.satisfied)));
  assert.ok(snapshot.endingConditionReports.every((report) => report.satisfiedCount === report.conditions.filter((condition) => condition.satisfied).length));
});

test("connecting an NPC creates a persistent promise and nearby support bonus", () => {
  const run = runFixture(60);
  const player = run.entities.find((item) => item.id === run.playerEntityId);
  const npc = run.entities.find((item) => item.kind === "npc");
  player.position = { x: npc.position.x - 1, y: npc.position.y };
  const connected = resolveTurn({
    run,
    request: normalizeSkillRequest({ idempotencyKey: "npc-promise-connect", expectedRunVersion: run.version,
      ability: "connect", targetEntityId: player.id, secondaryTargetEntityId: npc.id }),
    d20Source: new FixedD20Source(20)
  });
  assert.equal(connected.run.npcPromises.length, 1);
  assert.equal(connected.run.npcPromises[0].status, "made");
  assert.ok(connected.turn.events.some((event) => event.type === "npc_promise_made"));

  const searched = resolveTurn({
    run: connected.run,
    request: normalizeSkillRequest({ idempotencyKey: "npc-promise-search", expectedRunVersion: connected.run.version,
      ability: "search", targetEntityId: npc.id }),
    d20Source: new FixedD20Source(20)
  });
  assert.ok(searched.turn.events.some((event) => event.type === "companion_support_applied" && event.modifier === 1));
  assert.equal(searched.turn.dice.modifier, 6);
});

test("Root Process rejects an explicit Delete target until Search reveals it", () => {
  const run = runFixture(61);
  const player = run.entities.find((item) => item.id === run.playerEntityId);
  const target = run.entities.find((item) => item.kind === "enemy");
  target.assetId = "enemy.dragon.v1";
  player.position = { x: target.position.x - 1, y: target.position.y };
  run.entities = [player, target];
  assert.equal(enemyArchetype(target.assetId), "root_process");
  const deleteRequest = normalizeSkillRequest({ idempotencyKey: "root-process-delete-1",
    expectedRunVersion: run.version, ability: "delete", targetEntityId: target.id });
  assert.throws(
    () => resolveTurn({ run, request: deleteRequest, d20Source: new FixedD20Source(20) }),
    (error) => error instanceof AppError && error.code === "DEPENDENCY_NOT_REVEALED"
  );
  const searched = resolveTurn({ run, request: normalizeSkillRequest({ idempotencyKey: "root-process-search",
    expectedRunVersion: run.version, ability: "search", targetEntityId: target.id }), d20Source: new FixedD20Source(20),
    skillSelection: { kind: "entity", entityIds: [target.id] } });
  const hpBefore = searched.run.entities.find((item) => item.id === target.id).state.hp;
  const deleted = resolveTurn({ run: searched.run, request: normalizeSkillRequest({
    idempotencyKey: "root-process-delete-2", expectedRunVersion: searched.run.version,
    ability: "delete", targetEntityId: target.id }), d20Source: new FixedD20Source(20),
    skillSelection: { kind: "entity", entityIds: [target.id] } });
  assert.equal(deleted.run.entities.find((item) => item.id === target.id).state.hp, Math.max(0, hpBefore - 5));
  assert.ok(deleted.turn.events.some((event) => event.type === "health_changed" && event.entityId === target.id));
  assert.ok(deleted.turn.events.some((event) => event.type === "encounter_intervention" && event.entityId === target.id));
  assert.ok(deleted.turn.events.some((event) => event.type === "relationship_changed" && event.npcId === target.id));
});

test("Delete deals five damage and an unrevealed defeated Cache Replicator reproduces once", () => {
  const run = runFixture(62);
  const player = run.entities.find((item) => item.id === run.playerEntityId);
  const target = run.entities.find((item) => item.kind === "enemy");
  target.assetId = "enemy.mushroom-blue.v1";
  target.state.hp = Math.min(5, target.state.hp);
  player.position = { x: target.position.x - 1, y: target.position.y };
  const hpBefore = target.state.hp;
  const result = resolveTurn({ run, request: normalizeSkillRequest({ idempotencyKey: "cache-replicator-delete",
    expectedRunVersion: run.version, ability: "delete", targetEntityId: target.id }), d20Source: new FixedD20Source(20),
    skillSelection: { kind: "entity", entityIds: [target.id] } });
  const defeated = result.run.entities.find((item) => item.id === target.id);
  assert.equal(defeated.state.hp, Math.max(0, hpBefore - 5));
  assert.equal(defeated.active, false);
  assert.equal(result.run.entities.filter((item) => item.state?.sourceEntityId === target.id && item.state?.cacheReplica).length, 1);
  const spawned = result.turn.events.find((event) => event.type === "entity_spawned" && event.sourceEntityId === target.id);
  assert.ok(spawned, "a cache replica must use the generic authoritative spawn lifecycle");
  assert.equal(result.run.entities.some((item) => item.id === spawned.entityId && item.state?.cacheReplica), true);
  assert.ok(result.turn.events.some((event) => event.type === "entity_removed" && event.entityId === target.id));
  assert.ok(result.turn.events.some((event) => event.type === "defeat_reward_granted" && event.entityId === target.id));
  assert.ok(result.turn.events.some((event) => event.type === "relationship_changed" && event.npcId === target.id));

  const replica = result.run.entities.find((item) => item.state?.sourceEntityId === target.id && item.state?.cacheReplica);
  const livePlayer = result.run.entities.find((item) => item.id === result.run.playerEntityId);
  livePlayer.position = { x: replica.position.x - 1, y: replica.position.y };
  replica.state.hp = Math.min(5, replica.state.hp);
  const second = resolveTurn({ run: result.run, request: normalizeSkillRequest({
    idempotencyKey: "cache-replicator-delete-copy", expectedRunVersion: result.run.version,
    ability: "delete", targetEntityId: replica.id }), d20Source: new FixedD20Source(20),
    skillSelection: { kind: "entity", entityIds: [replica.id] } });
  assert.equal(second.run.entities.find((item) => item.id === replica.id).active, false);
  assert.equal(second.turn.events.some((event) => event.type === "cache_enemy_replicated"), false,
    "a cache copy must not create an unbounded replication chain when defeated");
  assert.equal(second.run.entities.filter((item) => item.state?.cacheReplica).length, 1);
});

function adjacentWalkable(run) {
  const player = run.entities.find((entity) => entity.id === run.playerEntityId);
  const occupied = new Set(run.entities.filter((entity) => entity.active && entity.blocking && entity.id !== player.id)
    .map((entity) => `${entity.position.x},${entity.position.y}`));
  for (const [dx, dy] of [[1, 0], [0, 1], [-1, 0], [0, -1]]) {
    const point = { x: player.position.x + dx, y: player.position.y + dy };
    const tile = run.world.tiles[point.y * run.world.width + point.x];
    if (point.x > 0 && point.y > 0 && point.x < run.world.width - 1 && point.y < run.world.height - 1
      && tile !== 1 && tile !== 4 && !occupied.has(`${point.x},${point.y}`)) return point;
  }
  assert.fail("Generated entry must expose an adjacent walkable tile.");
}

function normalizeSkillRequest({ ability, targetEntityId = null, secondaryTargetEntityId = null, intent, ...rest }) {
  return normalizeTurnRequest({
    ...rest,
    inputType: "USE_SKILL",
    skillId: String(ability || "").toUpperCase(),
    targetIds: [targetEntityId, secondaryTargetEntityId].filter(Boolean),
    ...(intent ? { playerNote: intent } : {})
  });
}

test("Ctrl F investigates one target and Ctrl A deals three damage within four tiles", () => {
  const run = runFixture(117);
  const player = run.entities.find((entity) => entity.id === run.playerEntityId);
  const nearby = run.entities.find((entity) => entity.kind === "enemy" && entity.active);
  const searchable = run.entities.find((entity) => entity.kind === "prop" && entity.active && !entity.state?.revealed);
  for (const entity of run.entities) {
    if (entity.id !== searchable.id && ["prop", "npc"].includes(entity.kind)) entity.state = { ...(entity.state || {}), revealed: true };
  }
  searchable.position = { x: player.position.x + 1, y: player.position.y };
  nearby.position = { x: player.position.x + 2, y: player.position.y };
  const hpBefore = nearby.state.hp;

  const searched = resolveTurn({
    run,
    request: normalizeSkillRequest({ ability: "search", targetEntityId: nearby.id, idempotencyKey: "ctrl-f-search-0001", expectedRunVersion: 1 }),
    d20Source: new FixedD20Source(20)
  });
  const investigation = searched.turn.events.find((event) => event.type === "entity_investigated");
  assert.ok(investigation);
  assert.equal(searched.run.entities.find((entity) => entity.id === investigation.entityId).state.revealed, true);
  assert.ok(searched.turn.events.some((event) => event.type === "search_completed"));

  const selected = resolveTurn({
    run: searched.run,
    request: normalizeSkillRequest({ ability: "select_all", idempotencyKey: "ctrl-a-area-0001", expectedRunVersion: 2 }),
    d20Source: new FixedD20Source(20)
  });
  assert.equal(selected.run.entities.find((entity) => entity.id === nearby.id).state.hp, Math.max(0, hpBefore - 3));
  assert.ok(selected.turn.events.some((event) => event.type === "health_changed" && event.entityId === nearby.id && event.delta === -3));
  assert.ok(selected.turn.events.some((event) => event.type === "group_intervention_resolved"));
  assert.ok(selected.turn.events.some((event) => event.type === "relationship_changed" && event.npcId === nearby.id));
});

test("NPC investigation reveals one persistent clue and subsequent empty search uses ambient fallback", () => {
  const run = runFixture(118);
  const player = run.entities.find((entity) => entity.id === run.playerEntityId);
  const npc = run.entities.find((entity) => entity.kind === "npc" && entity.active);
  player.position = { x: npc.position.x - 1, y: npc.position.y };
  const first = resolveTurn({
    run,
    request: normalizeSkillRequest({ ability: "search", targetEntityId: npc.id,
      idempotencyKey: "npc-investigation-0001", expectedRunVersion: 1 }),
    d20Source: new FixedD20Source(20)
  });
  const firstRelationship = first.run.npcRelationships.find((item) => item.npcId === npc.id);
  const clueEvent = first.turn.events.find((event) => event.type === "npc_clue_revealed");
  assert.ok(clueEvent?.line);
  assert.ok(clueEvent?.clueTitle);
  assert.equal(clueEvent?.clueContent, npc.state.secret);
  assert.match(clueEvent?.clueMeaning || "", /관리자|기록|붕괴|통제/);
  assert.match(clueEvent?.storyConnection || "", /붕괴|관리자|통제/);
  assert.ok(clueEvent?.nextObjective);
  assert.equal(first.run.entities.find((item) => item.id === npc.id).state.revealedClues.length, 1);
  assert.ok(first.run.canonicalFacts.some((fact) => fact.subject === npc.id && fact.predicate === "testimony"));
  assert.ok(first.run.npcMemories.some((memory) => memory.npcId === npc.id && memory.expired === false));

  for (const entity of first.run.entities) {
    if (["npc", "prop"].includes(entity.kind)) entity.state = { ...(entity.state || {}), revealed: true };
  }

  const repeat = resolveTurn({
    run: first.run,
    request: normalizeSkillRequest({ ability: "search",
      idempotencyKey: "npc-investigation-0002", expectedRunVersion: first.run.version }),
    d20Source: new FixedD20Source(20)
  });
  assert.ok(repeat.turn.events.some((event) => event.type === "ambient_fallback_applied" && event.ability === "search"));
  assert.equal(repeat.run.npcRelationships.find((item) => item.npcId === npc.id).trust, firstRelationship.trust);
  assert.equal(repeat.run.entities.find((item) => item.id === npc.id).state.revealedClues.length, 1);
});

test("books and crates block travel and support bounded nearby interaction", () => {
  const run = runFixture(91);
  const player = run.entities.find((entity) => entity.id === run.playerEntityId);
  const book = run.entities.find((entity) => entity.assetId === "item.rune-book.v1");
  const crate = run.entities.find((entity) => entity.assetId === "item.crate.v1");
  assert.equal(book.blocking, true);
  assert.equal(crate.blocking, true);

  assert.throws(() => resolveSafeTravel({
    run,
    request: normalizeTravelRequest({
      inputType: "MOVE", idempotencyKey: "travel-prop-0001", expectedRunVersion: 1,
      destination: book.position
    })
  }), (error) => error instanceof AppError && error.code === "TRAVEL_PATH_BLOCKED");

  player.position = { x: crate.position.x - 1, y: crate.position.y };
  run.focus = Math.max(0, run.maxFocus - 1);
  const interacted = resolveTurn({
    run,
    request: normalizeSkillRequest({
      idempotencyKey: "interact-crate-0001", expectedRunVersion: 1, ability: "interact",
      targetEntityId: crate.id, intent: "보급 상자를 확인한다"
    }),
    d20Source: new FixedD20Source(20)
  });
  const committedCrate = interacted.run.entities.find((entity) => entity.id === crate.id);
  assert.equal(committedCrate.state.opened, true);
  assert.equal(committedCrate.state.interactionCount, 1);
  assert.equal(interacted.run.focus, run.focus + 1);
  assert.ok(interacted.turn.events.some((event) => event.type === "entity_interacted"));

  interacted.run.focus = interacted.run.maxFocus - 3;
  interacted.run.gold = 5;
  const purchased = resolveTurn({
    run: interacted.run,
    request: normalizeSkillRequest({ idempotencyKey: "interact-crate-0002",
      expectedRunVersion: interacted.run.version, ability: "interact", targetEntityId: crate.id }),
    d20Source: new FixedD20Source(20)
  });
  assert.equal(purchased.run.gold, 3);
  assert.equal(purchased.run.focus, purchased.run.maxFocus - 1);
  assert.ok(purchased.turn.events.some((event) => event.type === "supply_purchased"));
});

test("copy validates and mutates only the cloned committed state", () => {
  const original = runFixture();
  const initialEntityCount = original.entities.length;
  const player = original.entities.find((entity) => entity.id === original.playerEntityId);
  const book = original.entities.find((entity) => entity.assetId === "item.rune-book.v1");
  player.position = { x: book.position.x - 1, y: book.position.y };
  const destination = { x: book.position.x, y: book.position.y + 1 };
  // The guaranteed road may approach horizontally; explicitly use an adjacent
  // walkable destination from the immutable generated layout.
  const candidates = [
    { x: book.position.x + 1, y: book.position.y },
    { x: book.position.x - 1, y: book.position.y },
    destination
  ];
  const available = candidates.find((candidate) => {
    const tile = original.world.tiles[candidate.y * original.world.width + candidate.x];
    return tile !== 1 && tile !== 4 && !(candidate.x === book.position.x && candidate.y === book.position.y);
  });
  player.position = { x: available.x - 1, y: available.y };
  const request = normalizeSkillRequest({
    idempotencyKey: "copy-0001",
    expectedRunVersion: 1,
    ability: "copy",
    targetEntityId: book.id,
    destination: available,
    intent: "Copy the unwritten ledger"
  });

  const result = resolveTurn({ run: original, request, d20Source: new FixedD20Source(20) });
  assert.equal(original.entities.length, initialEntityCount);
  assert.equal(result.run.entities.length, initialEntityCount + 1);
  assert.equal(result.run.focus, original.focus - 1);
  assert.equal(result.run.world.layoutHash, original.world.layoutHash);
  assert.ok(result.turn.events.some((event) => event.type === "entity_spawned"));
  const copyDto = publicTurn(result.turn);
  assert.equal(copyDto.runtime.gameplayResult.actionType, "COPY");
  assert.equal(copyDto.runtime.gameplayResult.result.target.entityType, "PROP");
  assert.equal(copyDto.runtime.gameplayResult.result.clone.entityType, "PROP");
  assert.equal(copyDto.runtime.gameplayResult.result.copyLocked, true);

  const repeated = resolveTurn({
    run: result.run,
    request: normalizeSkillRequest({ idempotencyKey: "copy-repeat-0002", expectedRunVersion: result.run.version, ability: "copy" }),
    d20Source: new FixedD20Source(20),
    skillSelection: { kind: "entity", entityIds: [book.id] }
  });
  assert.equal(repeated.turn.outcome, "critical_failure");
  assert.equal(repeated.run.entities.length, result.run.entities.length);
  assert.ok(repeated.turn.events.some((event) => event.type === "copy_repeat_rejected"));
  const repeatedDto = publicTurn(repeated.turn);
  assert.equal(repeatedDto.runtime.gameplayResult.result.copyLocked, true);
  assert.equal(repeatedDto.runtime.gameplayResult.result.rejectionReason, "COPY_LINEAGE_LOCKED");
});

test("Delete rejects a supplied protected entity instead of silently changing the target", () => {
  const run = runFixture(45);
  const player = run.entities.find((entity) => entity.id === run.playerEntityId);
  const warden = run.entities.find((entity) => entity.kind === "npc");
  player.position = { x: warden.position.x - 1, y: warden.position.y };
  const request = normalizeSkillRequest({
    idempotencyKey: "delete-0001",
    expectedRunVersion: 1,
    ability: "delete",
    targetEntityId: warden.id,
    intent: "Delete the warden"
  });

  run.entities = run.entities.filter((entity) => [player.id, warden.id].includes(entity.id));
  assert.throws(
    () => resolveTurn({ run, request, d20Source: new FixedD20Source(20) }),
    (error) => error instanceof AppError && error.code === "TARGET_NOT_HOSTILE"
  );
  assert.equal(run.currentTurn, 0);
  assert.equal(run.version, 1);
  assert.equal(warden.active, true);
});

test("an approved LLM discovery remains narrative while D20 controls its reward intensity", () => {
  const run = runFixture(451);
  const player = run.entities.find((entity) => entity.id === run.playerEntityId);
  run.entities = [player];
  const request = normalizeSkillRequest({
    idempotencyKey: "search-llm-event-0001",
    expectedRunVersion: 1,
    ability: "search",
    intent: "Search the empty corridor"
  });
  const skillSelection = {
    candidateId: "ambient_hidden_log",
    kind: "ambient",
    entityIds: [],
    discoveryType: "hidden_log",
    generatedEvent: {
      title: "지워진 점검 기록",
      description: "벽면 단자에서 짧게 끊긴 점검 기록의 일부가 복원된다.",
      discoveryType: "hidden_log"
    }
  };

  const resolved = resolveTurn({ run, request, d20Source: new FixedD20Source(20), skillSelection });
  assert.equal(resolved.run.experience, run.experience + 20);
  assert.ok(resolved.turn.events.some((event) => event.type === "llm_discovery_event" && event.title === "지워진 점검 기록" && event.reward === 20));
});

test("MOVE can be D20-resolved while blocked safe travel still never consumes a turn", () => {
  const run = runFixture(46);
  const playerBefore = structuredClone(run.entities.find((entity) => entity.id === run.playerEntityId).position);
  const destination = adjacentWalkable(run);
  const moved = resolveTurn({
    run,
    request: normalizeSkillRequest({ idempotencyKey: "move-turn-0001", expectedRunVersion: 1, ability: "move", destination }),
    d20Source: new FixedD20Source(20)
  });
  assert.deepEqual(moved.run.entities.find((entity) => entity.id === run.playerEntityId).position, destination);
  assert.ok(moved.turn.events.some((event) => event.type === "entity_moved"));
  const dto = publicTurn(moved.turn);
  assert.equal(dto.runtime.spatial.authoritative, true);
  assert.deepEqual(dto.runtime.spatial.player.position, destination);
  assert.ok(["NORTH", "SOUTH", "EAST", "WEST"].includes(dto.runtime.spatial.player.facing));
  assert.deepEqual(dto.runtime.gameplayResult.result.path.at(-1), destination);
  assert.equal(dto.runtime.gameplayResult.result.arrived, true);
  const travel = normalizeTravelRequest({ inputType: "MOVE", idempotencyKey: "move-fail-0002", expectedRunVersion: 1, destination: { x: 0, y: 0 } });
  assert.throws(() => resolveSafeTravel({ run, request: travel }), (error) => error.code === "TRAVEL_PATH_BLOCKED");
  assert.deepEqual(run.entities.find((entity) => entity.id === run.playerEntityId).position, playerBefore);
  assert.equal(run.currentTurn, 0);
  assert.equal(run.version, 1);
});

test("one-tile safe travel atomically invalidates the pending narrative choice without consuming a turn", () => {
  const run = runFixture(4600);
  const pendingChoiceSetId = run.pendingChoiceSet.choiceSetId;
  const destination = adjacentWalkable(run);
  const resolved = resolveSafeTravel({
    run,
    request: normalizeTravelRequest({
      inputType: "MOVE",
      idempotencyKey: "short-travel-clears-choice-0001",
      expectedRunVersion: run.version,
      destination
    })
  });

  assert.equal(resolved.navigation.path.length, 2);
  assert.deepEqual(resolved.run.entities.find((entity) => entity.id === run.playerEntityId).position,
    destination);
  assert.equal(resolved.run.pendingChoiceSet, null);
  assert.equal(resolved.run.choiceHistory.filter((entry) => entry.type === "NARRATIVE_CHOICE_SKIPPED"
    && entry.choiceSetId === pendingChoiceSetId).length, 1);
  assert.equal(resolved.run.currentTurn, run.currentTurn);
  assert.equal(resolved.run.version, run.version + 1);
  assert.equal(run.pendingChoiceSet.choiceSetId, pendingChoiceSetId,
    "safe-travel resolution must not mutate the caller's pre-commit snapshot");
});

test("WASD-sized travel commits every tile but schedules scene work only at the seeded 15-20 tile checkpoint", () => {
  const run = runFixture(4602);
  const origin = structuredClone(run.entities.find((entity) => entity.id === run.playerEntityId).position);
  const firstDestination = adjacentWalkable(run);
  run.nextStoryEventDistance = run.travelDistance + 2;
  const directorDecisionBefore = run.directorState.decisionNo;
  const firstRequest = normalizeTravelRequest({
    inputType: "MOVE",
    idempotencyKey: "unity-event-wasd-step-0001",
    expectedRunVersion: run.version,
    destination: firstDestination
  });
  const first = resolveTravelDecision({ run, request: firstRequest, sceneDecision: null });

  assert.deepEqual(first.run.entities.find((entity) => entity.id === run.playerEntityId).position,
    firstDestination);
  assert.equal(first.run.travelDistance, 1);
  assert.equal(first.navigation.storyEventTriggered, false,
    "an idempotency-key prefix must not force an early story event");
  assert.deepEqual(first.navigation.sceneSequence, []);
  assert.equal(first.navigation.narrative, null);
  assert.equal(first.run.directorState.decisionNo, directorDecisionBefore,
    "lightweight coordinate commits must not advance the scene director");

  const secondRequest = normalizeTravelRequest({
    inputType: "MOVE",
    idempotencyKey: "unity-travel-wasd-step-0002",
    expectedRunVersion: first.run.version,
    destination: origin
  });
  const preview = resolveSafeTravel({ run: first.run, request: secondRequest });
  assert.equal(preview.run.storyEventDue, true);
  const sceneDecision = planDeterministicDecisionScene({
    run: preview.run,
    decisionType: "TRAVEL",
    navigation: preview.navigation
  });
  const second = resolveTravelDecision({ run: first.run, request: secondRequest, sceneDecision });

  assert.equal(second.navigation.storyEventTriggered, true);
  assert.equal(second.run.storyEventSequence, 1);
  assert.equal(second.run.directorState.decisionNo, directorDecisionBefore + 1);
  assert.ok(second.navigation.sceneSequence.length >= 1);
  const nextInterval = second.run.nextStoryEventDistance - second.run.travelDistance;
  assert.ok(nextInterval >= 15 && nextInterval <= 20,
    `the next server checkpoint must be 15-20 tiles away, got ${nextInterval}`);
});

test("long safe POI travel invalidates the pending choice and reaches its destination across a story checkpoint", () => {
  const run = runFixture(4601);
  run.nextStoryEventDistance = 1;
  const pendingChoiceSetId = run.pendingChoiceSet.choiceSetId;
  const player = run.entities.find((entity) => entity.id === run.playerEntityId);
  let resolved = null;
  let destination = null;
  for (const point of run.world.points) {
    if (Math.abs(point.x - player.position.x) + Math.abs(point.y - player.position.y) < 8)
      continue;
    try {
      const candidate = resolveSafeTravel({
        run,
        request: normalizeTravelRequest({
          inputType: "MOVE",
          idempotencyKey: `long-safe-travel-${point.id}`,
          expectedRunVersion: run.version,
          destination: { x: point.x, y: point.y }
        })
      });
      if (!candidate.navigation.encounterOpened && candidate.navigation.path.length >= 8) {
        resolved = candidate;
        destination = { x: point.x, y: point.y };
        break;
      }
    } catch {
      // Some generated POIs are intentionally behind a gate or unsafe encounter.
    }
  }
  assert.ok(resolved && destination, "fixture must expose at least one distant safe POI");
  assert.deepEqual(resolved.navigation.to, {
    areaId: resolved.navigation.to.areaId,
    ...destination
  });
  assert.deepEqual(resolved.run.entities.find((entity) => entity.id === run.playerEntityId).position,
    destination);
  assert.equal(resolved.run.storyEventDue, true,
    "crossing the checkpoint still schedules one destination scene without truncating travel");
  assert.equal(resolved.run.pendingChoiceSet, null);
  assert.equal(resolved.run.choiceHistory.filter((entry) => entry.type === "NARRATIVE_CHOICE_SKIPPED"
    && entry.choiceSetId === pendingChoiceSetId).length, 1);
  assert.equal(resolved.run.currentTurn, run.currentTurn);
  assert.equal(resolved.run.version, run.version + 1);
  assert.equal(run.pendingChoiceSet.choiceSetId, pendingChoiceSetId,
    "long travel must leave the pre-commit snapshot intact for optimistic concurrency");
});

test("public runs hide pre-generated dormant actors while reporting their bounded pool", () => {
  const run = runFixture(461);
  const dormant = run.entities.filter((entity) => entity.active === false && entity.state?.activationState === "DORMANT");
  assert.ok(dormant.some((entity) => entity.kind === "npc"));
  assert.ok(dormant.some((entity) => entity.kind === "enemy" && entity.state?.boss !== true));
  assert.ok(dormant.some((entity) => entity.state?.boss === true));
  const dto = publicRun(run);
  assert.equal(dto.dormantEntityCount, dormant.length);
  assert.ok(dto.entities.every((entity) => !dormant.some((candidate) => candidate.id === entity.id)));
  assert.equal(dto.spatialContext.authority, "SERVER");
});

test("free-form combination consumes only owned ingredients and creates the server-confirmed result item", () => {
  const run = runFixture(146);
  const player = run.entities.find((entity) => entity.id === run.playerEntityId);
  const left = { id: "material-left", kind: "material", name: "금속성 파편", description: "차가운 파편", quantity: 1, protected: false };
  const right = { id: "material-right", kind: "material", name: "등불 심지", description: "남은 심지", quantity: 1, protected: false };
  player.state.inventory.push(left, right);
  const request = normalizeTurnRequest({
    inputType: "USE_SKILL", idempotencyKey: "combine-owned-0001", expectedRunVersion: 1,
    skillId: "COMBINE", targetIds: [], destination: null, itemIds: [left.id, right.id],
    playerNote: "금속성 파편과 등불 심지를 조합한다",
    actionProposal: {
      kind: "COMBINE", targetEntityIds: [], itemIds: [left.id, right.id], destinationRef: null,
      resultItem: { name: "되살린 작은 등불", kind: "tool", description: "두 재료를 조합해 되살린 작은 등불." }
    }
  });
  const resolved = resolveTurn({ run, request, d20Source: new FixedD20Source(20) });
  const inventory = resolved.run.entities.find((entity) => entity.id === run.playerEntityId).state.inventory;
  assert.ok(!inventory.some((item) => item.id === left.id || item.id === right.id));
  assert.ok(inventory.some((item) => item.name === "되살린 작은 등불" && item.source === "item_combination"));
  assert.ok(resolved.turn.events.some((event) => event.type === "inventory_items_combined"));
});

test("Undo compensates two reversible turns while turn and version stay monotonic", () => {
  const run = runFixture(47);
  const player = run.entities.find((item) => item.id === run.playerEntityId);
  const targets = run.entities.filter((item) => ["npc", "enemy"].includes(item.kind) && item.active).slice(0, 2);
  assert.equal(targets.length, 2);
  player.position = { x: 2, y: 2 };
  targets[0].position = { x: 3, y: 2 };
  targets[1].position = { x: 4, y: 2 };
  const layoutHash = run.world.layoutHash;
  const searched = resolveTurn({
    run,
    request: normalizeSkillRequest({
      idempotencyKey: "search-before-connect-1", expectedRunVersion: 1,
      ability: "search", targetEntityId: targets[0].id
    }),
    d20Source: new FixedD20Source(20),
    skillSelection: { kind: "entity", entityIds: [targets[0].id] }
  });
  const connected = resolveTurn({
    run: searched.run,
    request: normalizeSkillRequest({
      idempotencyKey: "connect-0001",
      expectedRunVersion: 2,
      ability: "connect",
      targetEntityId: targets[0].id,
      secondaryTargetEntityId: targets[1].id,
      intent: "두 증거가 같은 약속을 가리키도록 연결한다"
    }),
    d20Source: new FixedD20Source(20),
    skillSelection: { kind: "entity_pair", entityIds: [targets[0].id, targets[1].id] }
  });
  assert.equal(connected.run.connections.filter((item) => item.active).length, 1);

  const undone = resolveTurn({
    run: connected.run,
    request: normalizeSkillRequest({ idempotencyKey: "undo-0000001", expectedRunVersion: 3, ability: "undo", intent: "직전 두 턴을 시간 역행한다" }),
    d20Source: new FixedD20Source(20)
  });
  assert.equal(undone.run.connections.filter((item) => item.active).length, 0);
  assert.equal(undone.run.currentTurn, 3);
  assert.equal(undone.run.world.layoutHash, layoutHash);
  assert.equal(undone.turn.events.filter((event) => event.type === "turn_compensated").length, 2);
  assert.ok(undone.turn.events.some((event) => event.type === "undo_compensation_completed"));
  assert.ok(undone.turn.events.some((event) => event.type === "undo_compensated"));

  const replayed = resolveTurn({
    run: undone.run,
    request: normalizeSkillRequest({
      idempotencyKey: "search-after-rewind-1", expectedRunVersion: 4,
      ability: "search", targetEntityId: targets[0].id
    }),
    d20Source: new FixedD20Source(20)
  });
  assert.equal(replayed.turn.turnNo, 4);
  assert.notEqual(replayed.turn.id, searched.turn.id);
});

test("Restore compensates a recent investigation snapshot without introducing HP combat", () => {
  const run = runFixture(48);
  const player = run.entities.find((item) => item.id === run.playerEntityId);
  const target = run.entities.find((item) => item.kind === "prop" && item.active && !item.state?.revealed);
  player.position = { x: 2, y: 2 };
  target.position = { x: 3, y: 2 };
  const hpBefore = target.state.hp;
  const searched = resolveTurn({
    run,
    request: normalizeSkillRequest({ idempotencyKey: "restore-presearch-1", expectedRunVersion: 1,
      ability: "search", targetEntityId: target.id, intent: "남은 흔적을 먼저 조사한다" }),
    d20Source: new FixedD20Source(20),
    skillSelection: { kind: "entity", entityIds: [target.id] }
  });
  assert.equal(searched.run.entities.find((item) => item.id === target.id).state.revealed, true);
  const restored = resolveTurn({
    run: searched.run,
    request: normalizeSkillRequest({ idempotencyKey: "restore-0001", expectedRunVersion: 2, ability: "restore", targetEntityId: target.id, intent: "조사 전 상태를 복구한다" }),
    d20Source: new FixedD20Source(20),
    skillSelection: { kind: "entity", entityIds: [target.id] }
  });
  assert.equal(restored.run.entities.find((item) => item.id === target.id).state.revealed, false);
  assert.equal(restored.run.entities.find((item) => item.id === target.id).state.hp, hpBefore);
  assert.equal(restored.run.world.layoutHash, run.world.layoutHash);
  assert.ok(restored.turn.events.some((event) => event.type === "entity_state_restored"));
});

test("ambient-capable skills validate and preserve explicit targets and destinations", () => {
  const run = runFixture(1);
  const targets = run.entities.filter((item) => item.id !== run.playerEntityId).slice(0, 2);
  const destination = { x: 10, y: 11 };
  assert.throws(() => normalizeTurnRequest({
    inputType: "USE_SKILL", idempotencyKey: "delete-invalid-1", expectedRunVersion: 1,
    skillId: "DELETE", targetIds: ["not-a-uuid"]
  }), (error) => error instanceof AppError && error.code === "TARGET_INVALID");

  const copy = normalizeTurnRequest({
    inputType: "USE_SKILL", idempotencyKey: "copy-explicit-1", expectedRunVersion: 1,
    skillId: "COPY", targetIds: [targets[0].id], destination
  });
  assert.equal(copy.targetEntityId, targets[0].id);
  assert.deepEqual(copy.destination, destination);
  assert.equal(copy.abilitySource, "structured_selection");

  const connect = normalizeTurnRequest({
    inputType: "USE_SKILL", idempotencyKey: "connect-explicit-1", expectedRunVersion: 1,
    skillId: "CONNECT", targetIds: targets.map((item) => item.id)
  });
  assert.equal(connect.targetEntityId, targets[0].id);
  assert.equal(connect.secondaryTargetEntityId, targets[1].id);

  const ambient = normalizeTurnRequest({
    inputType: "USE_SKILL", idempotencyKey: "search-ambient-1", expectedRunVersion: 1,
    skillId: "SEARCH", targetIds: []
  });
  assert.equal(ambient.targetEntityId, null);
  assert.equal(ambient.abilitySource, "server_auto_target");

  assert.throws(() => normalizeTurnRequest({
    inputType: "USE_SKILL", idempotencyKey: "delete-dest-1", expectedRunVersion: 1,
    skillId: "DELETE", targetIds: [targets[0].id], destination
  }), (error) => error instanceof AppError && error.code === "DESTINATION_INVALID");
});

test("Restore repairs recent active-target damage but never rewinds the roll, turn, or map", () => {
  const run = runFixture(49);
  const player = run.entities.find((item) => item.id === run.playerEntityId);
  const [npc, secondNpc] = run.entities.filter((item) => item.kind === "npc").slice(0, 2);
  player.position = { x: 2, y: 2 };
  npc.position = { x: 3, y: 2 };
  secondNpc.position = { x: 4, y: 2 };
  const damaged = resolveTurn({
    run,
    request: normalizeSkillRequest({
      idempotencyKey: "damage-connect-1", expectedRunVersion: 1, ability: "connect",
      targetEntityId: npc.id, secondaryTargetEntityId: secondNpc.id, intent: "위험한 연결을 시도한다"
    }),
    d20Source: new FixedD20Source(1)
  });
  assert.equal(damaged.turn.outcome, "critical_failure");
  assert.equal(damaged.run.entities.find((item) => item.id === player.id).state.hp, 11);
  const repaired = resolveTurn({
    run: damaged.run,
    request: normalizeSkillRequest({ idempotencyKey: "restore-damage-1", expectedRunVersion: 2, ability: "restore", targetEntityId: player.id, intent: "최근의 허용된 손상만 복구한다" }),
    d20Source: new FixedD20Source(20)
  });
  assert.equal(repaired.run.entities.find((item) => item.id === player.id).state.hp, 12);
  assert.equal(repaired.run.currentTurn, 2);
  assert.equal(damaged.turn.d20, 1);
  assert.equal(repaired.run.world.layoutHash, run.world.layoutHash);
});

test("all five ambient keyboard skills resolve without client targets", () => {
  for (const [index, ability] of ["search", "copy", "delete", "connect", "restore"].entries()) {
    const run = runFixture(50 + index);
    const player = run.entities.find((item) => item.id === run.playerEntityId);
    run.entities = [player];
    const request = normalizeSkillRequest({ idempotencyKey: `ambient-${index + 1000}`, expectedRunVersion: 1, ability });
    const resolved = resolveTurn({ run, request, d20Source: new FixedD20Source(20) });
    assert.ok(resolved.turn.events.some((event) => event.type === "ambient_fallback_applied" && event.ability === ability));
  }
});

test("SEARCH establishes the essential collapse clue used by the Root System gate", () => {
  const run = runFixture(72);
  const player = run.entities.find((item) => item.id === run.playerEntityId);
  const clueEntity = run.entities.find((item) => item.active && item.state?.evidenceKey === "STORY_REVELATION");
  const clueFact = run.canonicalFacts.find((fact) => fact.subject === "collapse_origin" && fact.predicate === "inside_admin_control_system");
  assert.ok(clueEntity);
  assert.equal(clueFact.value, false);
  player.position = { x: 2, y: 2 };
  clueEntity.position = { x: 3, y: 2 };

  const searched = resolveTurn({
    run,
    request: normalizeSkillRequest({
      idempotencyKey: "essential-clue-search-1", expectedRunVersion: 1,
      ability: "search", targetEntityId: clueEntity.id
    }),
    d20Source: new FixedD20Source(20)
  });

  assert.equal(searched.run.canonicalFacts.find((fact) => fact.id === clueFact.id).value, true);
  assert.ok(searched.turn.events.some((event) => event.type === "canonical_fact_confirmed" && event.factId === clueFact.id));
  assert.ok(searched.turn.events.some((event) => event.type === "essential_clue_acquired" && event.entityId === clueEntity.id));
  searched.run.adminAccessAcquisitionHistory = searched.run.adminAccessLevels.map((access, index) => ({ accessLevelId: access.id, turnNo: index + 1 }));
  assert.equal(publicRun(searched.run).finaleGate.eligible, true);
});

test("finale component props connect only as authorized Root puzzle endpoints", () => {
  const run = runFixture(27);
  const player = run.entities.find((item) => item.id === run.playerEntityId);
  const anchor = run.entities.find((item) => item.state?.finaleComponent === "anchor");
  const freedom = run.entities.find((item) => item.state?.finaleComponent === "freedom");
  const ordinaryProp = run.entities.find((item) => item.active && item.kind === "prop"
    && !item.state?.finaleComponent && !item.state?.adminAccessLevelId);
  const npc = run.entities.find((item) => item.active && item.kind === "npc" && !item.state?.adminAccessLevelId);
  assert.ok(anchor && freedom && ordinaryProp && npc);
  player.position = { ...anchor.position };
  freedom.position = { ...anchor.position };
  ordinaryProp.position = { ...anchor.position };
  npc.position = { ...anchor.position };
  const connect = (left, right, key) => normalizeSkillRequest({
    idempotencyKey: key, expectedRunVersion: run.version, ability: "connect",
    targetEntityId: left.id, secondaryTargetEntityId: right.id
  });

  assert.throws(
    () => resolveTurn({ run, request: connect(ordinaryProp, npc, "ordinary-prop-connect-1"), d20Source: new FixedD20Source(20) }),
    (error) => error instanceof AppError && error.code === "TARGET_NOT_CONNECTABLE"
  );
  assert.throws(
    () => resolveTurn({ run, request: connect(anchor, freedom, "gated-finale-connect-1"), d20Source: new FixedD20Source(20) }),
    (error) => error instanceof AppError && error.code === "FINALE_ACCESS_DENIED"
  );

  run.adminAccessAcquisitionHistory = run.adminAccessLevels.map((access, index) => ({ accessLevelId: access.id, turnNo: index + 1 }));
  run.canonicalFacts.find((fact) => fact.subject === "collapse_origin").value = true;
  assert.throws(
    () => resolveTurn({ run, request: connect(anchor, npc, "mixed-finale-connect-1"), d20Source: new FixedD20Source(20) }),
    (error) => error instanceof AppError && error.code === "FINALE_ACCESS_DENIED"
  );

  const resolved = resolveTurn({
    run,
    request: connect(anchor, freedom, "open-frontier-connect-1"),
    d20Source: new FixedD20Source(20)
  });
  assert.equal(resolved.turn.actionContext, "DEPLOYMENT");
  assert.ok(resolved.turn.events.some((event) => event.type === "connection_created"
    && event.fromId === anchor.id && event.toId === freedom.id));
  assert.ok(resolved.run.connections.some((connection) => connection.active
    && connection.fromId === anchor.id && connection.toId === freedom.id));
});

test("finale threat props never inherit the enemy-only Root Process reveal gate across seeded runs", () => {
  for (const seed of [3, 7, 10, 11]) {
    const runId = `00000000-0000-4000-8000-${String(seed).padStart(12, "0")}`;
    const run = runFixture(seed, runId);
    const player = run.entities.find((item) => item.id === run.playerEntityId);
    const threat = run.entities.find((item) => item.state?.finaleComponent === "threat");
    assert.ok(player && threat);
    assert.equal(threat.kind, "prop");
    assert.equal(enemyArchetype(threat.assetId, run.worldSeed ?? run.world?.worldSeed, threat.id), "root_process",
      `Seed ${seed} must preserve the formerly failing hash classification.`);
    player.position = { ...threat.position };
    run.adminAccessAcquisitionHistory = run.adminAccessLevels.map((access, index) => ({
      accessLevelId: access.id,
      turnNo: index + 1
    }));
    run.canonicalFacts.find((fact) => fact.subject === "collapse_origin").value = true;

    const resolved = resolveTurn({
      run,
      request: normalizeSkillRequest({
        idempotencyKey: `finale-threat-delete-${seed}`,
        expectedRunVersion: run.version,
        ability: "delete",
        targetEntityId: threat.id
      }),
      d20Source: new FixedD20Source(20)
    });

    assert.ok(resolved.turn.events.some((event) => event.type === "entity_removed"
      && event.entityId === threat.id));
    assert.equal(resolved.run.entities.find((item) => item.id === threat.id).active, false);
  }
});

test("MOVE respects the administrator access and clue gate at Root System", () => {
  const run = runFixture(73);
  const player = run.entities.find((item) => item.id === run.playerEntityId);
  const occupied = new Set(run.entities.filter((item) => item.active && item.blocking && item.id !== player.id)
    .map((item) => `${item.position.x},${item.position.y}`));
  let boundary = null;
  for (let y = 1; y < run.world.height - 1 && !boundary; y += 1) {
    for (let x = 1; x < run.world.width - 1 && !boundary; x += 1) {
      const outside = { x, y };
      if (areaAt(run.world, outside).campaignRole === "FINAL_CONVERGENCE" || !isWalkable(run.world, outside)) continue;
      for (const [dx, dy] of [[1, 0], [0, 1], [-1, 0], [0, -1]]) {
        const inside = { x: x + dx, y: y + dy };
        if (areaAt(run.world, inside).campaignRole === "FINAL_CONVERGENCE"
          && isWalkable(run.world, inside) && !occupied.has(`${inside.x},${inside.y}`)) {
          boundary = { outside, inside };
          break;
        }
      }
    }
  }
  assert.ok(boundary, "generated finale region must have a walkable boundary for gate verification");
  player.position = boundary.outside;
  const request = normalizeTravelRequest({
    inputType: "MOVE",
    idempotencyKey: "finale-gate-move-1",
    expectedRunVersion: run.version,
    destination: boundary.inside
  });
  assert.throws(
    () => resolveSafeTravel({ run, request }),
    (error) => error instanceof AppError && error.code === "ROOT_SYSTEM_ACCESS_DENIED"
  );

  run.adminAccessAcquisitionHistory = run.adminAccessLevels.map((access, index) => ({ accessLevelId: access.id, turnNo: index + 1 }));
  run.canonicalFacts.find((fact) => fact.subject === "collapse_origin").value = true;
  const allowed = resolveSafeTravel({ run, request });
  assert.equal(allowed.navigation.campaignTurnConsumed, false);
  assert.equal(allowed.run.currentTurn, 0);
});

test("public Root System eligibility uses exactly three access levels and the required clue", () => {
  const run = runFixture(74);
  const clue = run.canonicalFacts.find((fact) => fact.subject === "collapse_origin");
  clue.value = true;
  run.adminAccessAcquisitionHistory = run.adminAccessLevels.map((access, index) => ({ accessLevelId: access.id, turnNo: index + 1 }));
  assert.equal(publicRun(run).finaleGate.eligible, true);

  run.adminAccessAcquisitionHistory = run.adminAccessAcquisitionHistory.slice(0, 2);
  assert.equal(publicRun(run).finaleGate.eligible, false);

  run.adminAccessAcquisitionHistory = run.adminAccessLevels.map((access, index) => ({ accessLevelId: access.id, turnNo: index + 1 }));
  clue.value = false;
  assert.equal(publicRun(run).finaleGate.eligible, false);
});

test("smart non-targeted skills fallback to ambient rewards when no target is present", () => {
  const run = runFixture(74);

  // Clear any nearby targets to simulate empty corridor/space
  run.entities = run.entities.filter(e => e.id === run.playerEntityId);

  const request = normalizeTurnRequest({
    inputType: "USE_SKILL",
    idempotencyKey: "test-ambient-search-1",
    expectedRunVersion: run.version,
    skillId: "SEARCH",
    targetIds: []
  });

  const source = new FixedD20Source(14); // Success roll
  const resolved = resolveTurn({ run, request, d20Source: source });

  assert.equal(resolved.run.experience, 10); // Experience reward for SEARCH ambient fallback
  assert.ok(resolved.turn.events.some(e => e.type === "ambient_fallback_applied" && e.ability === "search"));
});

test("smart non-targeted skills auto-target the closest valid entity", () => {
  const run = runFixture(74);
  const player = run.entities.find(e => e.id === run.playerEntityId);
  for (const entity of run.entities) {
    if (["prop", "npc"].includes(entity.kind)) entity.state = { ...(entity.state || {}), revealed: true };
  }

  // Spawn an unrevealed prop near player
  const prop = {
    id: "near-prop-id",
    kind: "prop",
    name: "Near Prop",
    active: true,
    position: { x: player.position.x + 1, y: player.position.y },
    state: { revealed: false, evidenceKey: "CLUE_NEAR" }
  };
  run.entities.push(prop);

  const request = normalizeTurnRequest({
    inputType: "USE_SKILL",
    idempotencyKey: "test-auto-search-1",
    expectedRunVersion: run.version,
    skillId: "SEARCH",
    targetIds: []
  });

  const source = new FixedD20Source(15);
  const resolved = resolveTurn({ run, request, d20Source: source });

  const resolvedProp = resolved.run.entities.find(e => e.id === prop.id);
  assert.equal(resolvedProp.state.revealed, true); // Auto-targeted and successfully revealed!
  assert.ok(resolved.turn.events.some(e => e.type === "entity_investigated" && e.entityId === prop.id));
});

test("legacy-disabled emergent runs expose and resolve the next administrator access candidate", async () => {
  const run = runFixture(7401);
  run.currentArcQuestion = null;
  for (const arc of run.arcQuestions) arc.status = "legacy_disabled";
  const player = run.entities.find((entity) => entity.id === run.playerEntityId);
  const candidateRecord = run.adminAccessCandidates.find((candidate) =>
    candidate.accessLevelId === "ADMIN_ACCESS_LEVEL_1" && candidate.skillId === "SEARCH");
  const target = run.entities.find((entity) => entity.state?.candidateId === candidateRecord.id);
  assert.ok(player && target);
  player.position = { x: target.position.x - 1, y: target.position.y };

  const context = buildSkillTargetContext(run, { skillId: "SEARCH", playerNote: null }, 15);
  assert.ok(context.candidates.some((candidate) => candidate.entityIds?.[0] === target.id),
    "mechanically legal access must remain visible when the retired arc scheduler is null");

  const resolved = resolveTurn({
    run,
    request: normalizeTurnRequest({
      inputType: "USE_SKILL",
      idempotencyKey: "legacy-null-admin-search-1",
      expectedRunVersion: run.version,
      skillId: "SEARCH",
      targetIds: []
    }),
    d20Source: new FixedD20Source(20)
  });
  assert.deepEqual(resolved.run.progressTokens, ["ADMIN_ACCESS_LEVEL_1"]);
  assert.equal(resolved.run.progressLevel, 1);
  const consumedTarget = resolved.run.entities.find((entity) => entity.id === target.id);
  assert.equal(consumedTarget.state.adminAccessResolved, true);
  assert.equal(consumedTarget.state.disabled, true,
    "acquired access anchors must leave the active world and target set");
  assert.ok(resolved.turn.events.some((event) => event.type === "admin_access_acquired"
    && event.candidateId === candidateRecord.id));
  assert.deepEqual(resolved.turn.request.targetEntityId, target.id,
    "the committed request must expose the authoritative auto-selected target");
  assert.deepEqual(resolved.run.abilityUsageHistory.at(-1).targetIds, [target.id],
    "usage history must agree with the entity changed by the turn");

  const llmTriedAmbient = {
    planSkillTarget: async () => ({
      candidateId: "ambient_resource_trace",
      rationale: "주변 신호를 먼저 살핀다."
    })
  };
  const planned = await planSkillTarget({
    narrator: llmTriedAmbient,
    run,
    request: { skillId: "SEARCH", playerNote: null },
    d20: 15
  });
  assert.equal(planned.selection.entityIds[0], target.id,
    "a nearby next access level must outrank an LLM-selected ambient event");
  assert.equal(planned.selection.model, "deterministic-campaign-target");

  const outOfOrder = runFixture(7402);
  outOfOrder.currentArcQuestion = null;
  const outOfOrderPlayer = outOfOrder.entities.find((entity) => entity.id === outOfOrder.playerEntityId);
  const levelTwoRecord = outOfOrder.adminAccessCandidates.find((candidate) =>
    candidate.accessLevelId === "ADMIN_ACCESS_LEVEL_2" && candidate.skillId === "RESTORE");
  const levelTwoTarget = outOfOrder.entities.find((entity) => entity.state?.candidateId === levelTwoRecord.id);
  outOfOrderPlayer.position = { x: levelTwoTarget.position.x - 1, y: levelTwoTarget.position.y };
  assert.throws(() => resolveTurn({
    run: outOfOrder,
    request: normalizeSkillRequest({
      idempotencyKey: "out-of-order-admin-search-1",
      expectedRunVersion: outOfOrder.version,
      ability: "restore",
      targetEntityId: levelTwoTarget.id
    }),
    d20Source: new FixedD20Source(20)
  }), (error) => error instanceof AppError && error.code === "ADMIN_ACCESS_SEQUENCE_INVALID");
});

test("administrator access entities always use an asset compatible with their public kind", () => {
  const run = runFixture(7404);
  const accessEntities = run.entities.filter((entity) => entity.state?.candidateId);
  assert.ok(accessEntities.length >= 6);
  for (const entity of accessEntities) {
    if (entity.kind === "enemy")
      assert.match(entity.assetId, /^(?:enemy|boss)\./, `${entity.name} exposed ${entity.assetId}`);
    else if (entity.kind === "npc")
      assert.match(entity.assetId, /^npc\./, `${entity.name} exposed ${entity.assetId}`);
    else
      assert.match(entity.assetId, /^(?:item|prop)\./, `${entity.name} exposed ${entity.assetId}`);
  }
});

test("targetless CONNECT binds an eligible administrator-access NPC to the player when no second NPC is nearby", async () => {
  const run = runFixture(7403);
  run.progressTokens = ["ADMIN_ACCESS_LEVEL_1"];
  run.progressLevel = 1;
  run.adminAccessAcquisitionHistory = [{
    id: randomUUID(), accessLevelId: "ADMIN_ACCESS_LEVEL_1", turnNo: 1,
    candidateId: "prior-level-one", skillId: "RESTORE", targetIds: []
  }];
  const player = run.entities.find((entity) => entity.id === run.playerEntityId);
  const candidateRecord = run.adminAccessCandidates.find((candidate) =>
    candidate.accessLevelId === "ADMIN_ACCESS_LEVEL_2" && candidate.skillId === "CONNECT");
  const target = run.entities.find((entity) => entity.state?.candidateId === candidateRecord?.id);
  assert.ok(player && target);
  player.position = target.position.x >= 5
    ? { x: target.position.x - 5, y: target.position.y }
    : { x: target.position.x + 5, y: target.position.y };
  for (const entity of run.entities) {
    if (entity.id === player.id || entity.id === target.id) continue;
    if (Math.abs(entity.position.x - player.position.x) + Math.abs(entity.position.y - player.position.y) <= 5)
      entity.active = false;
  }

  const context = buildSkillTargetContext(run, { skillId: "CONNECT", playerNote: null }, 20);
  const pair = context.candidates.find((candidate) => candidate.candidateId.startsWith("admin_pair_"));
  assert.deepEqual(pair?.entityIds, [target.id, player.id]);

  const planned = await planSkillTarget({
    narrator: { planSkillTarget: async () => ({ candidateId: "ambient_dormant_signal", rationale: "주변 신호를 고른다." }) },
    run,
    request: { skillId: "CONNECT", playerNote: null },
    d20: 20
  });
  assert.deepEqual(planned.selection.entityIds, [target.id, player.id]);
  assert.equal(planned.selection.model, "deterministic-campaign-target");

  const resolved = resolveTurn({
    run,
    request: normalizeTurnRequest({
      inputType: "USE_SKILL", idempotencyKey: "targetless-admin-connect-1",
      expectedRunVersion: run.version, skillId: "CONNECT", targetIds: []
    }),
    d20Source: new FixedD20Source(20),
    skillSelection: planned.selection
  });
  assert.equal(resolved.run.progressLevel, 2);
  assert.deepEqual(resolved.run.progressTokens, ["ADMIN_ACCESS_LEVEL_1", "ADMIN_ACCESS_LEVEL_2"]);
  assert.deepEqual(resolved.turn.request.targetEntityId, target.id);
  assert.deepEqual(resolved.turn.request.secondaryTargetEntityId, player.id);
  assert.ok(resolved.turn.events.some((event) => event.type === "admin_access_acquired"
    && event.candidateId === candidateRecord.id));
});

test("targetless SEARCH binds required HIDDEN_TRUTH evidence at the six-tile boundary", async () => {
  const run = runFixture(7405);
  const player = run.entities.find((entity) => entity.id === run.playerEntityId);
  const clue = run.entities.find((entity) => entity.active
    && entity.state?.designatedCampaignEvidence === true
    && entity.state?.campaignRole === "HIDDEN_TRUTH"
    && entity.state?.evidenceKey === "STORY_REVELATION");
  const clueFact = run.canonicalFacts.find((fact) => fact.subject === "collapse_origin"
    && fact.predicate === "inside_admin_control_system");
  assert.ok(player && clue && clueFact);
  assert.equal(clueFact.value, false);

  const boundaryPosition = clue.position.x >= 6
    ? { x: clue.position.x - 6, y: clue.position.y }
    : { x: clue.position.x + 6, y: clue.position.y };
  player.position = boundaryPosition;
  assert.equal(Math.abs(player.position.x - clue.position.x) + Math.abs(player.position.y - clue.position.y), 6);
  const nextAdminRecord = run.adminAccessCandidates.find((candidate) =>
    candidate.accessLevelId === "ADMIN_ACCESS_LEVEL_1" && candidate.skillId === "SEARCH");
  const nextAdminEntity = run.entities.find((entity) => entity.state?.candidateId === nextAdminRecord?.id);
  assert.ok(nextAdminEntity);
  nextAdminEntity.position = { ...player.position };

  // More than eight nearer generic candidates reproduce the cap that could
  // previously hide campaign evidence before the LLM target decision.
  for (let index = 0; index < 9; index += 1) {
    run.entities.push({
      id: `00000000-0000-4000-8000-${String(740500 + index).padStart(12, "0")}`,
      kind: "prop",
      assetId: "item.rune-book.v1",
      name: `일반 조사 대상 ${index + 1}`,
      active: true,
      blocking: false,
      protected: false,
      cloneable: false,
      position: { x: player.position.x, y: player.position.y },
      state: { revealed: false }
    });
  }

  const request = normalizeTurnRequest({
    inputType: "USE_SKILL",
    idempotencyKey: "required-story-revelation-boundary-1",
    expectedRunVersion: run.version,
    skillId: "SEARCH",
    targetIds: []
  });
  const narrator = {
    planSkillTarget: async () => ({
      candidateId: "ambient_resource_trace",
      rationale: "주변의 일반 신호를 조사한다."
    })
  };
  const planned = await planSkillTarget({ narrator, run, request, d20: 20 });
  assert.equal(planned.selection.kind, "entity");
  assert.deepEqual(planned.selection.entityIds, [clue.id]);
  assert.equal(planned.selection.distance, 6);
  assert.equal(planned.selection.model, "deterministic-campaign-target");
  assert.ok(planned.context.candidates.some((candidate) => candidate.entityIds?.[0] === nextAdminEntity.id),
    "the missing Root clue must preempt even a nearer eligible administrator-access candidate");

  run.adminAccessAcquisitionHistory = run.adminAccessLevels.map((access, index) => ({
    accessLevelId: access.id,
    turnNo: index + 1
  }));
  run.progressLevel = 3;
  run.progressTokens = run.adminAccessLevels.map((access) => access.id);

  const searched = resolveTurn({
    run,
    request,
    d20Source: new FixedD20Source(20),
    skillSelection: planned.selection
  });
  assert.equal(searched.turn.request.targetEntityId, clue.id);
  assert.deepEqual(searched.run.abilityUsageHistory.at(-1).targetIds, [clue.id]);
  assert.equal(searched.run.entities.find((entity) => entity.id === clue.id).state.revealed, true);
  assert.ok(searched.turn.events.some((event) => event.type === "essential_clue_acquired"
    && event.entityId === clue.id));
  assert.equal(publicRun(searched.run).rootSystemGate.requiredClueEstablished, true);
  assert.equal(publicRun(searched.run).rootSystemGate.eligible, true);
});

test("targetless SEARCH does not promote out-of-range story evidence or nearby non-evidence", async () => {
  const run = runFixture(7406);
  const player = run.entities.find((entity) => entity.id === run.playerEntityId);
  const requiredEvidence = run.entities.filter((entity) => entity.active
    && entity.state?.designatedCampaignEvidence === true
    && entity.state?.campaignRole === "HIDDEN_TRUTH"
    && entity.state?.evidenceKey === "STORY_REVELATION");
  assert.ok(player && requiredEvidence.length > 0);
  const reference = requiredEvidence[0];
  player.position = reference.position.x >= 7
    ? { x: reference.position.x - 7, y: reference.position.y }
    : { x: reference.position.x + 7, y: reference.position.y };
  for (const entity of requiredEvidence) entity.position = { ...reference.position };
  const ordinary = {
    id: "00000000-0000-4000-8000-000000007406",
    kind: "prop",
    assetId: "item.rune-book.v1",
    name: "가까운 일반 기록",
    active: true,
    blocking: false,
    protected: false,
    cloneable: false,
    position: { x: player.position.x + 1, y: player.position.y },
    state: { revealed: false, evidenceKey: "OPTIONAL_RECORD" }
  };
  run.entities.push(ordinary);
  run.adminAccessAcquisitionHistory = run.adminAccessLevels.map((access, index) => ({
    accessLevelId: access.id,
    turnNo: index + 1
  }));

  const planned = await planSkillTarget({
    narrator: {
      planSkillTarget: async () => ({ candidateId: "ambient_resource_trace", rationale: "주변 신호를 택한다." })
    },
    run,
    request: { skillId: "SEARCH", playerNote: null },
    d20: 15
  });
  assert.ok(planned.context.candidates.some((candidate) => candidate.entityIds?.[0] === ordinary.id));
  assert.equal(planned.context.candidates.some((candidate) => requiredEvidence.some((entity) => candidate.entityIds?.[0] === entity.id)), false);
  assert.equal(planned.selection.kind, "ambient");
  assert.deepEqual(planned.selection.entityIds, []);
});

test("targetless SEARCH stops forcing HIDDEN_TRUTH evidence after the clue is established", async () => {
  const run = runFixture(7407);
  const player = run.entities.find((entity) => entity.id === run.playerEntityId);
  const clue = run.entities.find((entity) => entity.active
    && entity.state?.designatedCampaignEvidence === true
    && entity.state?.campaignRole === "HIDDEN_TRUTH"
    && entity.state?.evidenceKey === "STORY_REVELATION");
  const clueFact = run.canonicalFacts.find((fact) => fact.subject === "collapse_origin"
    && fact.predicate === "inside_admin_control_system");
  assert.ok(player && clue && clueFact);
  player.position = clue.position.x >= 6
    ? { x: clue.position.x - 6, y: clue.position.y }
    : { x: clue.position.x + 6, y: clue.position.y };
  clue.state.revealed = false;
  clueFact.value = true;
  clueFact.establishedTurn = 1;
  run.adminAccessAcquisitionHistory = run.adminAccessLevels.map((access, index) => ({
    accessLevelId: access.id,
    turnNo: index + 1
  }));

  const planned = await planSkillTarget({
    narrator: {
      planSkillTarget: async () => ({ candidateId: "ambient_resource_trace", rationale: "이미 확보한 단서는 건너뛴다." })
    },
    run,
    request: { skillId: "SEARCH", playerNote: null },
    d20: 15
  });
  assert.ok(planned.context.candidates.some((candidate) => candidate.entityIds?.[0] === clue.id),
    "the revealed state, not candidate deletion, should control ordinary post-clue selection");
  assert.equal(planned.selection.kind, "ambient");
  assert.deepEqual(planned.selection.entityIds, []);
});

test("targetless Root CONNECT prioritizes missing ending-recipe pairs before the eight-pair cap", () => {
  const run = runFixture(7403);
  const player = run.entities.find((entity) => entity.id === run.playerEntityId);
  const components = Object.fromEntries(run.entities
    .filter((entity) => entity.state?.finaleComponent)
    .map((entity) => [entity.state.finaleComponent, entity]));
  const staging = run.world.validation.finaleInteractionAnchor;
  assert.ok(player && components.anchor && components.safeguard && components.memory && staging);
  player.position = { ...staging };

  const lockedContext = buildSkillTargetContext(run, { skillId: "CONNECT", playerNote: null }, 15);
  assert.equal(lockedContext.candidates.some((candidate) => candidate.candidateId.startsWith("finale_pair_")), false,
    "recipe priority must not bypass the Root permission gate");

  run.adminAccessAcquisitionHistory = run.adminAccessLevels.map((access, index) => ({
    accessLevelId: access.id,
    turnNo: index + 1
  }));
  run.progressLevel = 3;
  run.progressTokens = run.adminAccessLevels.map((access) => access.id);
  run.canonicalFacts.find((fact) => fact.subject === "collapse_origin"
    && fact.predicate === "inside_admin_control_system").value = true;

  const context = buildSkillTargetContext(run, { skillId: "CONNECT", playerNote: null }, 15);
  const mechanicalCandidates = context.candidates.filter((candidate) => candidate.kind === "entity_pair");
  const pairKey = (left, right) => [left, right].sort().join(":");
  const candidateKeys = new Set(mechanicalCandidates.map((candidate) => pairKey(...candidate.entityIds)));
  assert.ok(mechanicalCandidates.length <= 8);
  assert.ok(candidateKeys.has(pairKey(components.anchor.id, components.safeguard.id)));
  assert.ok(candidateKeys.has(pairKey(player.id, components.memory.id)),
    "player-component recipes must not be lost because generic endpoints exclude the player");
  assert.deepEqual(mechanicalCandidates[0].entityIds, [components.anchor.id, components.safeguard.id]);

  const resolved = resolveTurn({
    run,
    request: normalizeTurnRequest({
      inputType: "USE_SKILL",
      idempotencyKey: "targetless-finale-connect-1",
      expectedRunVersion: run.version,
      skillId: "CONNECT",
      targetIds: []
    }),
    d20Source: new FixedD20Source(20)
  });
  assert.ok(resolved.turn.events.some((event) => event.type === "connection_created"
    && pairKey(event.fromId, event.toId) === pairKey(components.anchor.id, components.safeguard.id)),
    "deterministic fallback must consume the same prioritized recipe pair as the LLM context");
});

test("targetless Root CONNECT continues one viable ending recipe and records the selected pair", async () => {
  const run = runFixture(7403);
  const player = run.entities.find((entity) => entity.id === run.playerEntityId);
  const components = Object.fromEntries(run.entities
    .filter((entity) => entity.state?.finaleComponent)
    .map((entity) => [entity.state.finaleComponent, entity]));
  const staging = run.world.validation.finaleInteractionAnchor;
  assert.ok(player && components.anchor && components.safeguard && components.memory && staging);
  player.position = { ...staging };
  run.adminAccessAcquisitionHistory = run.adminAccessLevels.map((access, index) => ({
    accessLevelId: access.id,
    turnNo: index + 1
  }));
  run.progressLevel = 3;
  run.progressTokens = run.adminAccessLevels.map((access) => access.id);
  run.canonicalFacts.find((fact) => fact.subject === "collapse_origin"
    && fact.predicate === "inside_admin_control_system").value = true;
  const reconciliation = run.endingCandidates.find((ending) => ending.id === "ENDING_REWEAVE_TOGETHER");
  assert.ok(reconciliation, "The fixture must expose the preferred reconciliation route.");
  run.endingCandidates = [
    ...run.endingCandidates.filter((ending) => ending.id !== reconciliation.id),
    reconciliation
  ];
  run.connections.push({
    id: "00000000-0000-4000-8000-000000007403",
    fromId: components.anchor.id,
    toId: components.safeguard.id,
    relation: "temporary_link",
    createdTurn: 0,
    expiresTurn: 5,
    active: true
  });

  const pairs = prioritizedFinaleConnectionPairs(run, 5);
  assert.equal(pairs.length, 1, "Only the next missing pair from the chosen recipe should be offered.");
  assert.deepEqual(pairs[0].components, ["player", "memory"]);
  assert.deepEqual(pairs[0].endingIds, ["ENDING_REWEAVE_TOGETHER"]);

  const request = normalizeTurnRequest({
    inputType: "USE_SKILL",
    idempotencyKey: "targetless-finale-connect-next-1",
    expectedRunVersion: run.version,
    skillId: "CONNECT",
    targetIds: []
  });
  const planned = await planSkillTarget({
    narrator: {
      planSkillTarget: async () => ({
        candidateId: "ambient_dormant_signal",
        rationale: "주변의 일반 신호를 연결한다."
      })
    },
    run,
    request,
    d20: 20
  });
  assert.deepEqual(planned.selection.entityIds, [player.id, components.memory.id]);
  assert.equal(planned.selection.model, "deterministic-campaign-target");

  const resolved = resolveTurn({
    run,
    request,
    d20Source: new FixedD20Source(20),
    skillSelection: planned.selection
  });
  assert.equal(resolved.turn.request.targetEntityId, player.id);
  assert.equal(resolved.turn.request.secondaryTargetEntityId, components.memory.id);
  assert.deepEqual(resolved.run.abilityUsageHistory.at(-1).targetIds, [player.id, components.memory.id]);
  assert.ok(resolved.turn.events.some((event) => event.type === "connection_created"
    && event.fromId === player.id && event.toId === components.memory.id));
});

test("targetless Root DELETE selects the required threat at range three and never a nearer component", async () => {
  const run = runFixture(7403);
  const player = run.entities.find((entity) => entity.id === run.playerEntityId);
  const components = Object.fromEntries(run.entities
    .filter((entity) => entity.state?.finaleComponent)
    .map((entity) => [entity.state.finaleComponent, entity]));
  const staging = run.world.validation.finaleInteractionAnchor;
  assert.ok(player && components.anchor && components.threat && staging);
  player.position = { ...staging };
  components.anchor.position = { ...player.position };
  components.threat.position = { x: player.position.x + 3, y: player.position.y };
  run.adminAccessAcquisitionHistory = run.adminAccessLevels.map((access, index) => ({
    accessLevelId: access.id,
    turnNo: index + 1
  }));
  run.progressLevel = 3;
  run.progressTokens = run.adminAccessLevels.map((access) => access.id);
  run.canonicalFacts.find((fact) => fact.subject === "collapse_origin"
    && fact.predicate === "inside_admin_control_system").value = true;

  const removals = prioritizedFinaleRemovalTargets(run, 3);
  assert.equal(removals[0].target.id, components.threat.id);
  assert.equal(removals[0].component, "threat");
  assert.deepEqual(removals[0].endingIds, ["ENDING_REWEAVE_TOGETHER"]);

  const request = normalizeTurnRequest({
    inputType: "USE_SKILL",
    idempotencyKey: "targetless-finale-delete-boundary-1",
    expectedRunVersion: run.version,
    skillId: "DELETE",
    targetIds: []
  });
  const planned = await planSkillTarget({
    narrator: {
      planSkillTarget: async () => ({
        candidateId: "ambient_system_residue",
        rationale: "가까운 잔류 신호를 지운다."
      })
    },
    run,
    request,
    d20: 20
  });
  assert.deepEqual(planned.selection.entityIds, [components.threat.id]);
  assert.equal(planned.selection.distance, 3);
  assert.equal(planned.selection.model, "deterministic-campaign-target");

  const resolved = resolveTurn({
    run,
    request,
    d20Source: new FixedD20Source(20),
    skillSelection: planned.selection
  });
  assert.equal(resolved.turn.request.targetEntityId, components.threat.id);
  assert.deepEqual(resolved.run.abilityUsageHistory.at(-1).targetIds, [components.threat.id]);
  assert.ok(resolved.turn.events.some((event) => event.type === "entity_removed"
    && event.entityId === components.threat.id));
  assert.equal(resolved.run.entities.find((entity) => entity.id === components.anchor.id).active, true);
});

test("targetless Root keyboard sequence reaches REWEAVE with exactly five focus", () => {
  let run = runFixture(7403);
  const player = run.entities.find((entity) => entity.id === run.playerEntityId);
  const components = Object.fromEntries(run.entities
    .filter((entity) => entity.state?.finaleComponent)
    .map((entity) => [entity.state.finaleComponent, entity]));
  const staging = run.world.validation.finaleInteractionAnchor;
  assert.ok(player && components.anchor && components.safeguard
    && components.memory && components.threat && staging);
  player.position = { ...staging };
  components.anchor.position = { x: player.position.x + 1, y: player.position.y };
  components.safeguard.position = { x: player.position.x + 2, y: player.position.y };
  components.memory.position = { x: player.position.x, y: player.position.y + 2 };
  components.threat.position = { x: player.position.x - 1, y: player.position.y };
  run.adminAccessAcquisitionHistory = run.adminAccessLevels.map((access, index) => ({
    accessLevelId: access.id,
    turnNo: index + 1
  }));
  run.progressLevel = 3;
  run.progressTokens = run.adminAccessLevels.map((access) => access.id);
  run.canonicalFacts.find((fact) => fact.subject === "collapse_origin"
    && fact.predicate === "inside_admin_control_system").value = true;
  run.storyLedger = Array.from({ length: 8 }, (_, index) => ({
    id: `00000000-0000-4000-8000-${String(750000 + index).padStart(12, "0")}`,
    turnNo: index - 8,
    skillId: "SEARCH",
    outcome: "success",
    campaignRole: "HIDDEN_TRUTH",
    targetEvidenceKeys: ["STORY_REVELATION"],
    eventTypes: ["entity_investigated"],
    meaningful: true
  }));
  run.majorChoices = Array.from({ length: 3 }, (_, index) => ({
    id: `00000000-0000-4000-8000-${String(760000 + index).padStart(12, "0")}`,
    turnNo: index - 3,
    choiceType: "NARRATIVE_CHOICE"
  }));
  run.focus = 10;

  const useTargetless = (current, skillId, key) => resolveTurn({
    run: current,
    request: normalizeTurnRequest({
      inputType: "USE_SKILL",
      idempotencyKey: key,
      expectedRunVersion: current.version,
      skillId,
      targetIds: []
    }),
    d20Source: new FixedD20Source(20)
  });
  const firstConnect = useTargetless(run, "CONNECT", "reweave-sequence-connect-anchor-1");
  assert.deepEqual(firstConnect.run.abilityUsageHistory.at(-1).targetIds,
    [components.anchor.id, components.safeguard.id]);
  assert.equal(firstConnect.run.focus, 8);
  run = firstConnect.run;

  const secondConnect = useTargetless(run, "CONNECT", "reweave-sequence-connect-memory-1");
  assert.deepEqual(secondConnect.run.abilityUsageHistory.at(-1).targetIds,
    [player.id, components.memory.id]);
  assert.equal(secondConnect.run.focus, 6);
  run = secondConnect.run;

  const deleteThreat = useTargetless(run, "DELETE", "reweave-sequence-delete-threat-1");
  assert.deepEqual(deleteThreat.run.abilityUsageHistory.at(-1).targetIds, [components.threat.id]);
  assert.equal(deleteThreat.run.focus, 5);
  assert.equal(deleteThreat.run.finalePuzzle.matchedEndingId, "ENDING_REWEAVE_TOGETHER");
  assert.equal(deleteThreat.run.status, "completed");
  assert.equal(deleteThreat.run.endingCode, "ENDING_REWEAVE_TOGETHER");
  assert.ok(deleteThreat.turn.events.some((event) => event.type === "run_completed"
    && event.endingCode === "ENDING_REWEAVE_TOGETHER"));
});

test("Root recipe links survive the turn-25 expiry boundary until REWEAVE resolves", () => {
  let run = runFixture(7403);
  const player = run.entities.find((entity) => entity.id === run.playerEntityId);
  const components = Object.fromEntries(run.entities
    .filter((entity) => entity.state?.finaleComponent)
    .map((entity) => [entity.state.finaleComponent, entity]));
  const staging = run.world.validation.finaleInteractionAnchor;
  assert.ok(player && components.anchor && components.safeguard
    && components.memory && components.threat && staging);
  player.position = { ...staging };
  player.state.hp = 1;
  components.anchor.position = { x: player.position.x + 1, y: player.position.y };
  components.safeguard.position = { x: player.position.x + 2, y: player.position.y };
  components.memory.position = { x: player.position.x, y: player.position.y + 2 };
  components.threat.position = { x: player.position.x - 1, y: player.position.y };
  run.currentTurn = 19;
  run.adminAccessAcquisitionHistory = run.adminAccessLevels.map((access, index) => ({
    accessLevelId: access.id,
    turnNo: index + 1
  }));
  run.progressLevel = 3;
  run.progressTokens = run.adminAccessLevels.map((access) => access.id);
  run.canonicalFacts.find((fact) => fact.subject === "collapse_origin"
    && fact.predicate === "inside_admin_control_system").value = true;
  run.storyLedger = Array.from({ length: 8 }, (_, index) => ({
    id: `00000000-0000-4000-8000-${String(770000 + index).padStart(12, "0")}`,
    turnNo: index + 1,
    skillId: "SEARCH",
    outcome: "success",
    campaignRole: "HIDDEN_TRUTH",
    targetEvidenceKeys: ["STORY_REVELATION"],
    eventTypes: ["entity_investigated"],
    meaningful: true
  }));
  run.majorChoices = Array.from({ length: 3 }, (_, index) => ({
    id: `00000000-0000-4000-8000-${String(780000 + index).padStart(12, "0")}`,
    turnNo: index + 10,
    choiceType: "NARRATIVE_CHOICE"
  }));

  const use = (current, skillId, key, targetIds = []) => resolveTurn({
    run: current,
    request: normalizeTurnRequest({
      inputType: "USE_SKILL",
      idempotencyKey: key,
      expectedRunVersion: current.version,
      skillId,
      targetIds
    }),
    d20Source: new FixedD20Source(20)
  });

  const anchorLink = use(run, "CONNECT", "expiry-root-connect-anchor-20",
    [components.anchor.id, components.safeguard.id]);
  const anchorConnectionId = anchorLink.turn.events
    .find((event) => event.type === "connection_created")?.id;
  assert.ok(anchorConnectionId);
  assert.equal(anchorLink.run.currentTurn, 20);
  assert.equal(anchorLink.run.connections.find((item) => item.id === anchorConnectionId).relation,
    "finale_recipe_link");
  assert.equal(anchorLink.run.connections.find((item) => item.id === anchorConnectionId).expiresTurn,
    null);
  run = use(anchorLink.run, "REST", "expiry-root-rest-21").run;

  const memoryLink = use(run, "CONNECT", "expiry-root-connect-memory-22",
    [player.id, components.memory.id]);
  const memoryConnectionId = memoryLink.turn.events
    .find((event) => event.type === "connection_created")?.id;
  assert.ok(memoryConnectionId);
  assert.equal(memoryLink.run.currentTurn, 22);
  assert.equal(memoryLink.run.connections.find((item) => item.id === memoryConnectionId).relation,
    "finale_recipe_link");
  assert.equal(memoryLink.run.connections.find((item) => item.id === memoryConnectionId).expiresTurn,
    null);
  run = memoryLink.run;

  for (const turnNo of [23, 24, 25]) {
    const rested = use(run, "REST", `expiry-root-rest-${turnNo}`);
    assert.equal(rested.run.currentTurn, turnNo);
    assert.ok(!rested.turn.events.some((event) => event.type === "connection_expired"));
    assert.equal(rested.run.connections.find((item) => item.id === anchorConnectionId).active, true);
    assert.equal(rested.run.connections.find((item) => item.id === memoryConnectionId).active, true);
    run = rested.run;
  }

  const resolved = use(run, "DELETE", "expiry-root-delete-threat-26", [components.threat.id]);
  assert.equal(resolved.run.currentTurn, 26);
  assert.equal(resolved.run.status, "completed");
  assert.equal(resolved.run.endingCode, "ENDING_REWEAVE_TOGETHER");
  assert.equal(resolved.run.connections.find((item) => item.id === anchorConnectionId).active, true);
  assert.equal(resolved.run.connections.find((item) => item.id === memoryConnectionId).active, true);
  assert.ok(!resolved.turn.events.some((event) => event.type === "connection_expired"));
});

test("non-Root connections retain the ordinary five-turn expiry", () => {
  let run = runFixture(7404);
  const player = run.entities.find((entity) => entity.id === run.playerEntityId);
  const npc = run.entities.find((entity) => entity.active && entity.kind === "npc");
  assert.ok(player && npc);
  player.position = { x: npc.position.x - 1, y: npc.position.y };
  player.state.hp = 1;

  const connected = resolveTurn({
    run,
    request: normalizeTurnRequest({
      inputType: "USE_SKILL",
      idempotencyKey: "expiry-non-root-connect-1",
      expectedRunVersion: run.version,
      skillId: "CONNECT",
      targetIds: [player.id, npc.id]
    }),
    d20Source: new FixedD20Source(20)
  });
  const connection = connected.run.connections.at(-1);
  assert.equal(connection.relation, "temporary_link");
  assert.equal(connection.createdTurn, 1);
  assert.equal(connection.expiresTurn, 6);
  run = connected.run;

  for (const turnNo of [2, 3, 4, 5, 6]) {
    const rested = resolveTurn({
      run,
      request: normalizeTurnRequest({
        inputType: "USE_SKILL",
        idempotencyKey: `expiry-non-root-rest-${turnNo}`,
        expectedRunVersion: run.version,
        skillId: "REST",
        targetIds: []
      }),
      d20Source: new FixedD20Source(20)
    });
    run = rested.run;
    if (turnNo < 6) {
      assert.equal(run.connections.find((item) => item.id === connection.id).active, true);
      assert.ok(!rested.turn.events.some((event) => event.type === "connection_expired"));
    } else {
      assert.equal(run.connections.find((item) => item.id === connection.id).active, false);
      assert.ok(rested.turn.events.some((event) => event.type === "connection_expired"
        && event.connectionId === connection.id));
    }
  }
});

test("targetless Root finale actions reject out-of-range recipe targets without mutating nearer components", () => {
  const run = runFixture(7403);
  const player = run.entities.find((entity) => entity.id === run.playerEntityId);
  const components = Object.fromEntries(run.entities
    .filter((entity) => entity.state?.finaleComponent)
    .map((entity) => [entity.state.finaleComponent, entity]));
  const staging = run.world.validation.finaleInteractionAnchor;
  assert.ok(player && components.anchor && components.safeguard && components.threat && staging);
  player.position = { ...staging };
  components.anchor.position = { ...player.position };
  components.safeguard.position = { x: player.position.x + 6, y: player.position.y };
  components.threat.position = { x: player.position.x + 4, y: player.position.y };
  run.adminAccessAcquisitionHistory = run.adminAccessLevels.map((access, index) => ({
    accessLevelId: access.id,
    turnNo: index + 1
  }));
  run.progressLevel = 3;
  run.progressTokens = run.adminAccessLevels.map((access) => access.id);
  run.canonicalFacts.find((fact) => fact.subject === "collapse_origin"
    && fact.predicate === "inside_admin_control_system").value = true;
  const before = structuredClone(run);

  assert.throws(() => resolveTurn({
    run,
    request: normalizeTurnRequest({
      inputType: "USE_SKILL",
      idempotencyKey: "targetless-finale-delete-out-of-range-1",
      expectedRunVersion: run.version,
      skillId: "DELETE",
      targetIds: []
    }),
    d20Source: new FixedD20Source(20)
  }), (error) => error instanceof AppError && error.code === "OUT_OF_RANGE");
  assert.throws(() => resolveTurn({
    run,
    request: normalizeTurnRequest({
      inputType: "USE_SKILL",
      idempotencyKey: "targetless-finale-connect-out-of-range-1",
      expectedRunVersion: run.version,
      skillId: "CONNECT",
      targetIds: []
    }),
    d20Source: new FixedD20Source(20)
  }), (error) => error instanceof AppError && error.code === "OUT_OF_RANGE");
  assert.deepEqual(run, before, "Rejected targetless finale actions must not consume focus or change entities.");
});
