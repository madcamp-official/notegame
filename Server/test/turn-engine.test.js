import test from "node:test";
import assert from "node:assert/strict";
import { randomUUID } from "node:crypto";
import { AppError } from "../src/errors.js";
import { createCampaignBlueprint } from "../src/domain/campaign.js";
import { areaAt, generateWorld, isWalkable } from "../src/domain/world.js";
import { FixedD20Source, createRunState, normalizeTravelRequest, normalizeTurnRequest, publicRun, publicTurn, resolveSafeTravel, resolveTurn } from "../src/domain/turn-engine.js";
import { capabilitiesFor } from "../src/domain/entity-capabilities.js";
import { enemyArchetype } from "../src/domain/enemy-archetypes.js";
import { detectIntentActions } from "../src/domain/intent.js";

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

function runFixture(seed = 44) {
  const now = "2026-07-17T00:00:00.000Z";
  const campaign = {
    id: randomUUID(),
    ownerId: OWNER_ID,
    turnLimit: 30,
    ...createCampaignBlueprint({ worldSeed: seed, turnLimit: 30 }),
    world: generateWorld(seed)
  };
  return createRunState({ campaign, ownerId: OWNER_ID, now, resolutionSeed: "private-test-seed" });
}

test("entity capabilities expose world-editing rules independently from entity kind", () => {
  const removable = capabilitiesFor({ kind: "prop", active: true, protected: false, cloneable: false, state: { finaleComponent: "threat" } });
  const ambient = capabilitiesFor({ kind: "prop", active: true, protected: false, cloneable: true, state: {} });
  const hostile = capabilitiesFor({ kind: "enemy", active: true, protected: false, cloneable: false, state: {} });
  assert.deepEqual({ canDelete: removable.canDelete, requiredAdminAccess: removable.requiredAdminAccess, reward: removable.grantsDefeatReward }, { canDelete: true, requiredAdminAccess: 3, reward: false });
  assert.equal(ambient.canCopy, true);
  assert.equal(ambient.canInteract, true);
  assert.equal(hostile.canConnect, true);
  assert.equal(hostile.grantsDefeatReward, true);
});

test("Delete changes a monster relationship without reducing HP", () => {
  const run = runFixture(5701);
  const player = run.entities.find((item) => item.id === run.playerEntityId);
  const target = run.entities.find((item) => item.kind === "enemy" && item.active);
  player.position = { x: target.position.x - 1, y: target.position.y };
  target.state.revealed = true;
  const hpBefore = target.state.hp;
  const result = resolveTurn({
    run,
    request: normalizeSkillRequest({ idempotencyKey: "relationship-delete-5701", expectedRunVersion: 1,
      ability: "delete", targetEntityId: target.id }),
    d20Source: new FixedD20Source(20)
  });
  assert.equal(result.run.entities.find((item) => item.id === target.id).state.hp, hpBefore);
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

test("Root Process requires Search before Delete can change its encounter relationship", () => {
  const run = runFixture(61);
  const player = run.entities.find((item) => item.id === run.playerEntityId);
  const target = run.entities.find((item) => item.kind === "enemy");
  target.assetId = "enemy.dragon.v1";
  player.position = { x: target.position.x - 1, y: target.position.y };
  run.entities = [player, target];
  assert.equal(enemyArchetype(target.assetId), "root_process");
  const deleteRequest = normalizeSkillRequest({ idempotencyKey: "root-process-delete-1",
    expectedRunVersion: run.version, ability: "delete", targetEntityId: target.id });
  const skipped = resolveTurn({ run, request: deleteRequest, d20Source: new FixedD20Source(20) });
  assert.ok(skipped.turn.events.some((event) => event.type === "ambient_fallback_applied" && event.ability === "delete"));
  const searched = resolveTurn({ run, request: normalizeSkillRequest({ idempotencyKey: "root-process-search",
    expectedRunVersion: run.version, ability: "search", targetEntityId: target.id }), d20Source: new FixedD20Source(20),
    skillSelection: { kind: "entity", entityIds: [target.id] } });
  const hpBefore = searched.run.entities.find((item) => item.id === target.id).state.hp;
  const deleted = resolveTurn({ run: searched.run, request: normalizeSkillRequest({
    idempotencyKey: "root-process-delete-2", expectedRunVersion: searched.run.version,
    ability: "delete", targetEntityId: target.id }), d20Source: new FixedD20Source(20),
    skillSelection: { kind: "entity", entityIds: [target.id] } });
  assert.equal(deleted.run.entities.find((item) => item.id === target.id).state.hp, hpBefore);
  assert.ok(deleted.turn.events.some((event) => event.type === "encounter_intervention" && event.entityId === target.id));
  assert.ok(deleted.turn.events.some((event) => event.type === "relationship_changed" && event.npcId === target.id));
});

test("Delete pressures an unsearched Cache Replicator without forced HP damage or replication", () => {
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
  assert.equal(result.run.entities.find((item) => item.id === target.id).state.hp, hpBefore);
  assert.equal(result.run.entities.filter((item) => item.state?.sourceEntityId === target.id && item.state?.cacheReplica).length, 0);
  assert.ok(result.turn.events.some((event) => event.type === "relationship_changed" && event.npcId === target.id));
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

test("Ctrl F investigates one target and Ctrl A changes nearby encounter relationships without HP damage", () => {
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
  assert.equal(selected.run.entities.find((entity) => entity.id === nearby.id).state.hp, hpBefore);
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
    request: normalizeSkillRequest({ ability: "search", targetEntityId: npc.id,
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

test("Delete ignores a supplied protected entity and resolves as an ambient action", () => {
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
  const resolved = resolveTurn({ run, request, d20Source: new FixedD20Source(20) });
  assert.ok(resolved.turn.events.some((event) => event.type === "ambient_fallback_applied" && event.ability === "delete"));
  assert.equal(resolved.run.gold, run.gold + 20);
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

test("ambient skills ignore client target and destination payloads", () => {
  const run = runFixture(1);
  const player = run.entities.find((item) => item.id === run.playerEntityId);
  const target = run.entities.find((item) => item.kind === "prop" && !item.protected && !item.blocking
    && run.world.placementSlots.some((slot) => slot.id === item.state?.slotId && slot.purpose === "ambient"));
  assert.ok(target, "the fixture must contain an ambient prop");
  player.position = { x: target.position.x - 1, y: target.position.y };

  run.entities = run.entities.filter((entity) => [player.id, target.id].includes(entity.id));
  const request = normalizeTurnRequest({
    inputType: "USE_SKILL", idempotencyKey: "delete-ambient-1", expectedRunVersion: 1,
    skillId: "DELETE", targetIds: ["not-a-uuid"], destination: { invalid: true }
  });
  assert.equal(request.targetEntityId, null);
  assert.equal(request.destination, null);
  const resolved = resolveTurn({ run, request, d20Source: new FixedD20Source(20) });
  assert.ok(resolved.turn.events.some((event) => event.type === "ambient_fallback_applied"));
  assert.equal(target.active, true);
  assert.equal(run.currentTurn, 0);
});

test("Restore repairs recent active-target damage but never rewinds the roll, turn, or map", () => {
  const run = runFixture(49);
  const player = run.entities.find((item) => item.id === run.playerEntityId);
  const npc = run.entities.find((item) => item.kind === "npc");
  const prop = run.entities.find((item) => item.kind === "prop");
  player.position = { x: 2, y: 2 };
  npc.position = { x: 3, y: 2 };
  prop.position = { x: 4, y: 2 };
  const damaged = resolveTurn({
    run,
    request: normalizeSkillRequest({
      idempotencyKey: "damage-connect-1", expectedRunVersion: 1, ability: "connect",
      targetEntityId: npc.id, secondaryTargetEntityId: prop.id, intent: "위험한 연결을 시도한다"
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
