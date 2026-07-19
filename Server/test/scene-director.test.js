import test from "node:test";
import assert from "node:assert/strict";
import { randomUUID } from "node:crypto";
import {
  CAMPAIGN_MACRO_PHASES,
  createCampaignBlueprint,
  macroPhaseForBeat
} from "../src/domain/campaign.js";
import { buildConsequenceCandidates } from "../src/domain/consequence-candidates.js";
import { applyScenePlan } from "../src/domain/consequence-resolver.js";
import {
  BOSS_CATALOG,
  CORE_NPC_CATALOG,
  MONSTER_CATALOG,
  NPC_CATALOG,
  SPECIAL_SKILL_TEMPLATES
} from "../src/domain/content-catalog.js";
import { FixedD20Source, createRunState, normalizeTurnRequest, resolveTurn } from "../src/domain/turn-engine.js";
import { generateWorld, isWalkable } from "../src/domain/world.js";
import {
  createFallbackScenePlan,
  validateSceneDirectorContext,
  validateScenePlan
} from "../src/llm/scene-director.js";

const OWNER_ID = "77777777-7777-4777-8777-777777777777";

function runFixture(seed = 211) {
  const campaign = {
    id: randomUUID(),
    ownerId: OWNER_ID,
    turnLimit: 40,
    ...createCampaignBlueprint({ worldSeed: seed, turnLimit: 40 }),
    world: generateWorld(seed)
  };
  return createRunState({
    campaign,
    ownerId: OWNER_ID,
    now: "2026-07-18T00:00:00.000Z",
    resolutionSeed: "scene-director-test-seed"
  });
}

test("the nine internal beats map to the immutable seven-phase campaign spine", () => {
  const run = runFixture();
  assert.equal(CAMPAIGN_MACRO_PHASES.length, 7);
  assert.deepEqual(run.campaignMacroPhases.map((phase) => phase.id), CAMPAIGN_MACRO_PHASES.map((phase) => phase.id));
  assert.equal(macroPhaseForBeat("beat.codria_crash").id, "MACRO_ARRIVAL_AWAKENING");
  assert.equal(macroPhaseForBeat("beat.first_region_problem").id, "MACRO_ARRIVAL_AWAKENING");
  assert.equal(macroPhaseForBeat("beat.internal_cause").id, "MACRO_COLLAPSE_TRUTH");
  assert.equal(macroPhaseForBeat("beat.root_system_entry").id, "MACRO_ROOT_DECISION");
  assert.equal(macroPhaseForBeat("beat.final_deployment").id, "MACRO_ROOT_DECISION");
  assert.equal(run.currentMacroPhase.id, "MACRO_ARRIVAL_AWAKENING");
});

test("the authoritative content catalog exposes fixed core NPCs, ten monsters, and bounded boss recipes", () => {
  const run = runFixture(215);
  assert.equal(CORE_NPC_CATALOG.length, 6);
  assert.equal(NPC_CATALOG.length, 10);
  assert.equal(MONSTER_CATALOG.length, 10);
  assert.equal(BOSS_CATALOG.length, 18);
  assert.equal(new Set(MONSTER_CATALOG.map((entry) => entry.assetId)).size, 10);
  assert.equal(new Set(BOSS_CATALOG.map((entry) => entry.assetId)).size, 18);
  assert.ok(BOSS_CATALOG.every((entry) => entry.hp >= 16 && entry.patterns.length >= 2 && entry.minMacroOrder >= 2));
  const coreNpcs = run.entities.filter((entity) => entity.kind === "npc" && entity.state?.canonicalNpcId);
  assert.deepEqual(coreNpcs.map((entity) => entity.state.canonicalNpcId), CORE_NPC_CATALOG.map((entry) => entry.id));
  assert.ok(coreNpcs.every((entity) => entity.state.goal && entity.state.motivation && entity.state.factionId));
});

test("scene plans can select only server-issued candidate IDs", () => {
  const run = runFixture(212);
  const built = buildConsequenceCandidates(run, { decisionType: "TRAVEL" });
  const context = validateSceneDirectorContext(built.context);
  const fallback = createFallbackScenePlan(context);
  const validated = validateScenePlan({
    sceneGoal: fallback.sceneGoal,
    selectedActionIds: fallback.selectedActionIds,
    dialogue: []
  }, context);
  assert.deepEqual(validated.selectedActionIds, fallback.selectedActionIds);
  assert.throws(() => validateScenePlan({
    sceneGoal: "서버가 주지 않은 사건을 강제로 고른다.",
    selectedActionIds: [randomUUID()],
    dialogue: []
  }, context), (error) => error.code === "SCENE_PLAN_CANDIDATE_FORBIDDEN");
});

test("the deterministic fallback returns the same bounded legal scene", () => {
  const run = runFixture(213);
  const { context } = buildConsequenceCandidates(run, { decisionType: "ACTION" });
  const first = createFallbackScenePlan(context);
  const second = createFallbackScenePlan(structuredClone(context));
  assert.deepEqual(first, second);
  assert.ok(first.selectedActionIds.length >= 1 && first.selectedActionIds.length <= 3);
  const allowed = new Set(context.candidates.map((candidate) => candidate.candidateId));
  assert.ok(first.selectedActionIds.every((id) => allowed.has(id)));
  const cost = first.selectedActionIds.reduce((sum, id) => sum + context.candidates.find((item) => item.candidateId === id).cost, 0);
  assert.ok(cost <= 4);
});

test("scene resolution is deterministic, bounded per actor, and never changes world geometry", () => {
  const source = runFixture(214);
  const player = source.entities.find((entity) => entity.id === source.playerEntityId);
  const enemy = source.entities.find((entity) => entity.kind === "enemy");
  enemy.position = { x: player.position.x + 1, y: player.position.y };
  enemy.state.hp = Math.max(1, enemy.state.hp || 1);
  player.state.hp = Math.max(3, player.state.hp || 3);
  const candidates = [
    {
      candidateId: randomUUID(), type: "ATTACK_ENTITY", actorId: enemy.id, targetId: player.id,
      priority: 90, cost: 2, reason: "인접한 적이 공격한다.", actionStyle: "MELEE"
    },
    {
      candidateId: randomUUID(), type: "ATTACK_ENTITY", actorId: enemy.id, targetId: player.id,
      priority: 80, cost: 2, reason: "같은 적의 두 번째 공격 후보.", actionStyle: "MELEE"
    }
  ];
  const plan = {
    sceneGoal: "인접한 오류 개체가 이동의 결과로 반응한다.",
    selectedActionIds: candidates.map((candidate) => candidate.candidateId),
    dialogue: []
  };
  const left = structuredClone(source);
  const right = structuredClone(source);
  const leftResult = applyScenePlan(left, { candidates, plan, decisionType: "TRAVEL", now: "2026-07-18T00:01:00.000Z" });
  const rightResult = applyScenePlan(right, { candidates, plan, decisionType: "TRAVEL", now: "2026-07-18T00:01:00.000Z" });

  assert.deepEqual(leftResult, rightResult);
  assert.equal(leftResult.appliedActionIds.length, 1);
  assert.equal(leftResult.rejectedActions[0].reason, "ACTOR_ACTION_BUDGET_EXCEEDED");
  assert.equal(leftResult.sceneSequence[0].type, "ATTACK");
  assert.equal(left.directorState.decisionNo, 1);
  assert.equal(left.world.layoutHash, source.world.layoutHash);
  assert.deepEqual(left.world.tiles, source.world.tiles);
  const pending = left.directorState.pendingConsequences[0];
  assert.equal(pending.sourceActionType, "ATTACK_ENTITY");
  const npc = left.entities.find((entity) => entity.kind === "npc");
  const leftPlayer = left.entities.find((entity) => entity.id === left.playerEntityId);
  npc.position = { ...leftPlayer.position };
  left.directorState.decisionNo = pending.dueDecisionNo - 1;
  const callbackBundle = buildConsequenceCandidates(left, { decisionType: "ACTION" });
  const callback = callbackBundle.candidates.find((candidate) => candidate.pendingId === pending.id);
  assert.ok(callback, "a due prior choice must return as a high-priority legal candidate");
  const callbackResult = applyScenePlan(left, {
    candidates: callbackBundle.candidates,
    plan: { sceneGoal: "과거 공격의 결과가 복선으로 돌아온다.", selectedActionIds: [callback.candidateId], dialogue: [] },
    decisionType: "ACTION"
  });
  assert.equal(left.directorState.pendingConsequences.find((item) => item.id === pending.id).status, "resolved", JSON.stringify(callbackResult.rejectedActions));
});

test("low-health monster traits produce a legal flee action resolved on the fixed grid", () => {
  const run = runFixture(216);
  const player = run.entities.find((entity) => entity.id === run.playerEntityId);
  const enemy = run.entities.find((entity) => entity.kind === "enemy");
  const occupied = new Set(run.entities.filter((entity) => entity.active && ![player.id, enemy.id].includes(entity.id))
    .map((entity) => `${entity.position.x},${entity.position.y}`));
  let lane = null;
  for (let y = 2; y < run.world.height - 2 && !lane; y += 1) {
    for (let x = 2; x < run.world.width - 3 && !lane; x += 1) {
      const points = [{ x, y }, { x: x + 1, y }, { x: x + 2, y }];
      if (points.every((point) => isWalkable(run.world, point) && !occupied.has(`${point.x},${point.y}`))) lane = points;
    }
  }
  assert.ok(lane);
  player.position = lane[0];
  enemy.position = lane[1];
  enemy.state.hp = 1;
  enemy.state.maxHp = 5;
  enemy.state.traits = ["FLEE_AT_LOW_HP"];
  const { candidates } = buildConsequenceCandidates(run, { decisionType: "ACTION" });
  const flee = candidates.find((candidate) => candidate.type === "FLEE_ENTITY" && candidate.actorId === enemy.id);
  assert.ok(flee);
  const beforeDistance = Math.abs(enemy.position.x - player.position.x) + Math.abs(enemy.position.y - player.position.y);
  const result = applyScenePlan(run, {
    candidates,
    plan: { sceneGoal: "손상된 오류 개체가 생존을 택한다.", selectedActionIds: [flee.candidateId], dialogue: [] },
    decisionType: "ACTION"
  });
  const afterDistance = Math.abs(enemy.position.x - player.position.x) + Math.abs(enemy.position.y - player.position.y);
  assert.equal(result.sceneSequence[0].type, "FLEE");
  assert.ok(afterDistance > beforeDistance);
});

test("boss spawning and special-skill rewards are resolved from server catalogs only", () => {
  const run = runFixture(217);
  run.currentMacroPhase = { ...CAMPAIGN_MACRO_PHASES[6] };
  const player = run.entities.find((entity) => entity.id === run.playerEntityId);
  const npc = run.entities.find((entity) => entity.kind === "npc");
  const freeSlot = run.world.placementSlots.find((slot) => slot.kind === "enemy" &&
    !run.entities.some((entity) => entity.active && (entity.state?.slotId === slot.id || (entity.position.x === slot.x && entity.position.y === slot.y))));
  assert.ok(freeSlot);
  const boss = BOSS_CATALOG.find((entry) => entry.minMacroOrder <= 7);
  const bossCandidate = {
    candidateId: randomUUID(), type: "SPAWN_FROM_SLOT", actorId: null, targetId: player.id,
    priority: 90, cost: 3, reason: "서버 카탈로그의 보스 조우", actionStyle: "ENCOUNTER",
    slotId: freeSlot.id, assetId: boss.assetId, traitIds: [...boss.traits]
  };
  const reward = SPECIAL_SKILL_TEMPLATES.find((entry) => entry.id === "RUN_SKILL_LIGHTWEIGHT_COPY");
  const rewardCandidate = {
    candidateId: randomUUID(), type: "GRANT_SPECIAL_REWARD", actorId: npc.id, targetId: player.id,
    priority: 70, cost: 2, reason: "서버 카탈로그의 런 전용 보상", actionStyle: "REWARD", rewardId: reward.id
  };
  const bossResult = applyScenePlan(run, {
    candidates: [bossCandidate],
    plan: { sceneGoal: "루트 단계가 희귀 보스를 호출한다.", selectedActionIds: [bossCandidate.candidateId], dialogue: [] },
    decisionType: "TRAVEL"
  });
  assert.equal(bossResult.sceneSequence[0].type, "SPAWN");
  assert.ok(run.entities.some((entity) => entity.assetId === boss.assetId && entity.state?.boss));

  const rewardResult = applyScenePlan(run, {
    candidates: [rewardCandidate],
    plan: { sceneGoal: "동료가 키보드 변형을 건넨다.", selectedActionIds: [rewardCandidate.candidateId], dialogue: [] },
    decisionType: "ACTION"
  });
  assert.equal(rewardResult.sceneSequence[0].type, "REWARD");
  assert.equal(run.directorState.specialSkills[0].templateId, reward.id);

  const freeNpcSlot = run.world.placementSlots.find((slot) => slot.kind === "npc" &&
    !run.entities.some((entity) => entity.active && (entity.state?.slotId === slot.id || (entity.position.x === slot.x && entity.position.y === slot.y))));
  assert.ok(freeNpcSlot);
  const generatedNpcCandidate = {
    candidateId: randomUUID(), type: "SPAWN_FROM_SLOT", actorId: null, targetId: player.id,
    priority: 50, cost: 2, reason: "서버 NPC 카탈로그의 런 전용 인물", actionStyle: "ARRIVAL",
    slotId: freeNpcSlot.id, assetId: NPC_CATALOG[0].assetId, spawnKind: "npc", displayName: "포인터", traitIds: ["WITNESS"]
  };
  applyScenePlan(run, {
    candidates: [generatedNpcCandidate],
    plan: { sceneGoal: "선택의 파장을 좇는 새 인물이 등장한다.", selectedActionIds: [generatedNpcCandidate.candidateId], dialogue: [] },
    decisionType: "TRAVEL"
  });
  assert.equal(run.directorState.generatedCharacters.length, 1);
  assert.ok(run.npcRelationships.some((relationship) => relationship.npcId === run.directorState.generatedCharacters[0].entityId));

  const book = run.entities.find((entity) => entity.assetId === "item.rune-book.v1");
  const occupied = new Set(run.entities.filter((entity) => entity.active && entity.id !== player.id && entity.id !== book.id)
    .map((entity) => `${entity.position.x},${entity.position.y}`));
  let placement = null;
  for (let y = 2; y < run.world.height - 2 && !placement; y += 1) {
    for (let x = 2; x < run.world.width - 2 && !placement; x += 1) {
      const origin = { x, y };
      const source = { x: x + 1, y };
      const destination = { x, y: y + 1 };
      if ([origin, source, destination].every((point) => isWalkable(run.world, point) && !occupied.has(`${point.x},${point.y}`))) placement = { origin, source, destination };
    }
  }
  assert.ok(placement);
  player.position = placement.origin;
  book.position = placement.source;
  run.focus = 0;
  const copied = resolveTurn({
    run,
    request: normalizeTurnRequest({
      inputType: "USE_SKILL", idempotencyKey: "special-copy-0001", expectedRunVersion: run.version,
      skillId: "COPY", targetIds: [book.id], destination: placement.destination
    }),
    d20Source: new FixedD20Source(20)
  });
  assert.equal(copied.run.focus, 0, "REDUCED_FOCUS_COST must make COPY free");
  assert.equal(copied.run.directorState.specialSkills[0].charges, 1);
  assert.ok(copied.run.entities.some((entity) => entity.state?.sourceEntityId === book.id && entity.state?.temporary));
});
