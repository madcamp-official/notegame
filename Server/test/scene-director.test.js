import test from "node:test";
import assert from "node:assert/strict";
import { randomUUID } from "node:crypto";
import {
  CAMPAIGN_MACRO_PHASES,
  createCampaignBlueprint,
  macroPhaseForBeat
} from "../src/domain/campaign.js";
import { buildConsequenceCandidates } from "../src/domain/consequence-candidates.js";
import { planDecisionScene, planDeterministicDecisionScene } from "../src/domain/decision-orchestrator.js";
import { applyScenePlan, sanitizePlayerFacingHookSummary } from "../src/domain/consequence-resolver.js";
import {
  BOSS_CATALOG,
  CORE_NPC_CATALOG,
  MONSTER_CATALOG,
  NPC_CATALOG,
  SPECIAL_SKILL_TEMPLATES
} from "../src/domain/content-catalog.js";
import { FixedD20Source, createRunState, normalizeTurnRequest, publicRun, resolveTurn } from "../src/domain/turn-engine.js";
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
  assert.ok(!context.candidates.some((candidate) => ["START_QUEST", "ADVANCE_QUEST"].includes(candidate.type)));
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

test("deterministic travel scene planning is stable and keeps every action inside the server allowlist", () => {
  const source = runFixture(218);
  const first = planDeterministicDecisionScene({
    run: structuredClone(source),
    decisionType: "TRAVEL",
    navigation: { from: { x: 1, y: 1 }, to: { x: 2, y: 1 }, encounterOpened: false }
  });
  const second = planDeterministicDecisionScene({
    run: structuredClone(source),
    decisionType: "TRAVEL",
    navigation: { from: { x: 1, y: 1 }, to: { x: 2, y: 1 }, encounterOpened: false }
  });
  assert.deepEqual(first, second);
  assert.equal(first.plan.fallbackUsed, true);
  assert.match(first.plan.model, /^deterministic-scene-director-/);
  const allowed = new Set(first.candidates.map((candidate) => candidate.candidateId));
  assert.ok(first.plan.selectedActionIds.length >= 1);
  assert.ok(first.plan.selectedActionIds.every((candidateId) => allowed.has(candidateId)));
});

test("an active encounter is presented once when opened, not replayed on every combat or recovery turn", () => {
  const run = runFixture(20260722);
  const player = run.entities.find((entity) => entity.id === run.playerEntityId);
  const enemy = run.entities.find((entity) => entity.kind === "enemy");
  enemy.active = true;
  enemy.position = { x: player.position.x + 1, y: player.position.y };
  run.activeEncounter = {
    id: "11111111-1111-4111-8111-111111111111",
    status: "active",
    kind: "COMBAT",
    sourceEntityId: enemy.id,
    description: `${enemy.name} 조우!`
  };

  const continued = buildConsequenceCandidates(run, {
    decisionType: "ACTION",
    turn: { events: [{ type: "encounter_continued", encounterId: run.activeEncounter.id }] }
  });
  assert.equal(continued.candidates.some((candidate) => candidate.type === "START_ENCOUNTER"), false);

  const opened = buildConsequenceCandidates(run, {
    decisionType: "ACTION",
    turn: { events: [{ type: "entity_activated", entityId: enemy.id }] }
  });
  assert.equal(opened.candidates.some((candidate) => candidate.type === "START_ENCOUNTER"), true);
});

test("an assertive dialogue can legally escalate into a server-confirmed attack animation action", () => {
  const run = runFixture(219);
  const player = run.entities.find((entity) => entity.id === run.playerEntityId);
  const enemy = run.entities.find((entity) => entity.kind === "enemy");
  enemy.position = { x: player.position.x + 1, y: player.position.y };
  const built = buildConsequenceCandidates(run, {
    decisionType: "ACTION",
    turn: { outcome: "narrative", actionContext: "NARRATIVE", selectedChoice: { intentTag: "ASSERTIVE" } }
  });
  const attack = built.candidates.find((item) => item.type === "ATTACK_ENTITY" && item.actorId === enemy.id && item.targetId === player.id);
  assert.ok(attack);
  const result = applyScenePlan(run, {
    candidates: built.candidates,
    plan: { sceneGoal: "대화가 결렬되자 오류 개체가 먼저 덤벼든다.", selectedActionIds: [attack.candidateId], dialogue: [] },
    decisionType: "ACTION"
  });
  assert.equal(result.sceneSequence[0].type, "ATTACK");
  assert.equal(result.sceneSequence[0].actionStyle, "DIALOGUE_BREAKDOWN");
  assert.match(result.sceneSequence[0].actionId, /^[0-9a-f-]{36}$/i);
});

test("the player HP floor never reports phantom scene damage", () => {
  const run = runFixture(222);
  run.id = "11111111-1111-4111-8111-111111111111";
  run.resolutionSeed = "scene-director-test-seed";
  const player = run.entities.find((entity) => entity.id === run.playerEntityId);
  const enemy = run.entities.find((entity) => entity.kind === "enemy");
  player.id = "33333333-3333-4333-8333-333333333333";
  run.playerEntityId = player.id;
  enemy.id = "22222222-2222-4222-8222-222222222222";
  player.position = { x: 4, y: 4 };
  enemy.position = { x: 5, y: 4 };
  player.state.hp = 1;
  player.state.maxHp = 12;
  const attack = {
    candidateId: "44444444-4444-4444-8444-444444444444",
    type: "ATTACK_ENTITY",
    actorId: enemy.id,
    targetId: player.id,
    priority: 96,
    cost: 2,
    reason: "인접한 적이 최후 저항 상태의 플레이어를 공격한다.",
    actionStyle: "MELEE"
  };

  const result = applyScenePlan(run, {
    candidates: [attack],
    plan: { sceneGoal: "HP 보호 경계의 실제 변화량을 검증한다.", selectedActionIds: [attack.candidateId], dialogue: [] },
    decisionType: "ACTION"
  });

  const attackAction = result.sceneSequence.find((action) => action.type === "ATTACK");
  assert.equal(attackAction.hit, true);
  assert.equal(attackAction.damage, 0);
  assert.equal(player.state.hp, 1);
  assert.equal(result.events.some((event) => event.type === "health_changed" && event.entityId === player.id), false);
  assert.equal(result.sceneSequence.some((action) => action.type === "DAMAGE" && action.actorId === player.id), false);
  assert.equal(result.events.some((event) => event.type === "scene_attack_missed"), false);
});

test("scene damage uses the correct Korean subject particle for the protagonist", () => {
  const run = runFixture(222);
  run.id = "11111111-1111-4111-8111-111111111111";
  run.resolutionSeed = "scene-director-test-seed";
  const player = run.entities.find((entity) => entity.id === run.playerEntityId);
  const enemy = run.entities.find((entity) => entity.kind === "enemy");
  player.id = "33333333-3333-4333-8333-333333333333";
  run.playerEntityId = player.id;
  enemy.id = "22222222-2222-4222-8222-222222222222";
  player.name = "넙죽이";
  player.position = { x: 4, y: 4 };
  enemy.position = { x: 5, y: 4 };
  player.state.hp = 12;
  player.state.maxHp = 12;
  const attack = {
    candidateId: "44444444-4444-4444-8444-444444444444",
    type: "ATTACK_ENTITY",
    actorId: enemy.id,
    targetId: player.id,
    priority: 96,
    cost: 2,
    reason: "인접한 적이 플레이어를 공격한다.",
    actionStyle: "MELEE"
  };

  const result = applyScenePlan(run, {
    candidates: [attack],
    plan: { sceneGoal: "피해 문장의 조사를 검증한다.", selectedActionIds: [attack.candidateId], dialogue: [] },
    decisionType: "ACTION"
  });

  assert.equal(result.sceneSequence.find((action) => action.type === "DAMAGE")?.text,
    "넙죽이가 1의 피해를 입었다.");
});

test("the model may propose a coherent action that was not in the recommendation list", async () => {
  const run = runFixture(220);
  const player = run.entities.find((entity) => entity.id === run.playerEntityId);
  const enemy = run.entities.find((entity) => entity.kind === "enemy");
  enemy.position = { x: player.position.x + 1, y: player.position.y };
  const narrator = {
    async planScene(context) {
      assert.equal(context.candidates.some((item) => item.type === "ATTACK_ENTITY"), false);
      return {
        sceneGoal: "설득이 끝내 통하지 않자 오류 개체가 먼저 공격한다.",
        selectedActionIds: [],
        proposedActions: [{
          type: "ATTACK_ENTITY", actorId: enemy.id, targetId: player.id,
          actionStyle: "FAILED_NEGOTIATION", text: "대화가 결렬되자 오류 개체가 칼을 뽑아 달려든다.", displayName: null
        }],
        dialogue: []
      };
    }
  };
  const decision = await planDecisionScene({
    narrator, run, decisionType: "ACTION",
    turn: { outcome: "narrative", actionContext: "NARRATIVE", selectedChoice: { intentTag: "CAUTIOUS" } },
    logger: { warn() {} }
  });
  const result = applyScenePlan(run, { candidates: decision.candidates, plan: decision.plan, decisionType: "ACTION" });
  assert.equal(result.sceneSequence[0].type, "ATTACK");
  assert.equal(result.sceneSequence[0].actionStyle, "FAILED_NEGOTIATION");
});

test("free proposals still reject unknown actors and protected action types", () => {
  const run = runFixture(221);
  const context = buildConsequenceCandidates(run, { decisionType: "ACTION" }).context;
  assert.throws(() => validateScenePlan({
    sceneGoal: "존재하지 않는 인물이 결말을 강제로 바꾼다.", selectedActionIds: [], dialogue: [],
    proposedActions: [{ type: "CHANGE_ENDING", actorId: randomUUID(), targetId: null, actionStyle: null, text: "결말을 즉시 확정한다.", displayName: null }]
  }, context), (error) => ["SCENE_PLAN_ACTION_FORBIDDEN", "SCENE_PLAN_ENTITY_FORBIDDEN"].includes(error.code));
  assert.throws(() => validateScenePlan({
    sceneGoal: "사소한 대화가 세계의 결말을 바꾼다.", selectedActionIds: [], dialogue: [],
    proposedActions: [{ type: "NARRATIVE_EVENT", actorId: null, targetId: null, actionStyle: null, text: "대화 한 번으로 관리자 권한을 획득하고 최종 결말을 확정한다.", displayName: null }]
  }, context), (error) => error.code === "SCENE_PLAN_PROTECTED_RESULT");
  assert.throws(() => validateScenePlan({
    sceneGoal: "모델이 서버의 등장 규칙을 건너뛰어 새 인물을 만든다.", selectedActionIds: [], dialogue: [],
    proposedActions: [{ type: "INTRODUCE_NPC", actorId: null, targetId: null, actionStyle: "ARRIVAL", text: "새 인물이 갑자기 나타난다.", displayName: "임의 인물" }]
  }, context), (error) => error.code === "SCENE_PLAN_ACTION_FORBIDDEN");
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
  assert.match(leftResult.sceneSequence[0].actionId, /^[0-9a-f-]{36}$/i);
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
  const callbackHook = left.openLoops.find((item) => item.id === left.directorState.pendingConsequences.find((item) => item.id === pending.id).resolvedHookId);
  assert.equal(callbackHook.summary, `${npc.name}가 전한 충돌의 여파`);
  assert.doesNotMatch(callbackHook.summary, /[A-Z]+_[A-Z_]+/);
  assert.equal(sanitizePlayerFacingHookSummary("코멘트이 전한 과거 선택의 역류: START_ENCOUNTER"), "코멘트가 전한 새로운 조우의 여파");
});

test("monster HP never forces a combat exit and a nearby monster can speak as a relationship actor", () => {
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
  const hpBefore = enemy.state.hp;
  const { candidates } = buildConsequenceCandidates(run, { decisionType: "ACTION" });
  assert.equal(candidates.some((candidate) => candidate.type === "FLEE_ENTITY" && candidate.actorId === enemy.id), false);
  const dialogue = candidates.find((candidate) => candidate.type === "START_DIALOGUE" && candidate.actorId === enemy.id);
  assert.ok(dialogue);
  const result = applyScenePlan(run, {
    candidates,
    plan: {
      sceneGoal: "경계하던 오류 개체가 먼저 자신의 입장을 말한다.",
      selectedActionIds: [dialogue.candidateId],
      dialogue: [{ speakerId: enemy.id, line: "나를 지워야만 이 길을 지날 수 있다고 생각해?" }]
    },
    decisionType: "ACTION"
  });
  assert.equal(result.sceneSequence[0].type, "DIALOGUE");
  assert.equal(result.sceneSequence[0].speakerId, enemy.id);
  assert.equal(enemy.state.hp, hpBefore);
  assert.ok(run.npcMemories.some((memory) => memory.npcId === enemy.id));
});

test("boss spawning and special-skill rewards are resolved from server catalogs only", () => {
  const run = runFixture(217);
  run.currentMacroPhase = { ...CAMPAIGN_MACRO_PHASES[6] };
  const player = run.entities.find((entity) => entity.id === run.playerEntityId);
  const npc = run.entities.find((entity) => entity.kind === "npc");
  const dormantBoss = run.entities.find((entity) => entity.active === false && entity.state?.activationState === "DORMANT" && entity.state?.boss === true
    && !run.entities.some((other) => other.active && (other.state?.slotId === entity.state.activationSlotId || (other.position.x === entity.position.x && other.position.y === entity.position.y))));
  assert.ok(dormantBoss);
  const boss = BOSS_CATALOG.find((entry) => entry.assetId === dormantBoss.assetId);
  const entityCountBeforeBoss = run.entities.length;
  const bossCandidate = {
    candidateId: randomUUID(), type: "SPAWN_FROM_SLOT", actorId: null, targetId: player.id,
    priority: 90, cost: 3, reason: "서버 카탈로그의 보스 조우", actionStyle: "ENCOUNTER",
    entityId: dormantBoss.id, slotId: dormantBoss.state.activationSlotId, assetId: boss.assetId, traitIds: [...boss.traits]
  };
  assert.doesNotThrow(() => validateSceneDirectorContext({
    ...buildConsequenceCandidates(run, { decisionType: "TRAVEL" }).context,
    candidates: [bossCandidate]
  }));
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
  assert.match(bossResult.sceneSequence[0].actionId, /^[0-9a-f-]{36}$/i);
  assert.equal(run.entities.length, entityCountBeforeBoss);
  assert.equal(dormantBoss.active, true);
  assert.equal(bossResult.events[0].type, "entity_activated");
  assert.ok(run.entities.some((entity) => entity.assetId === boss.assetId && entity.state?.boss));

  const rewardResult = applyScenePlan(run, {
    candidates: [rewardCandidate],
    plan: { sceneGoal: "동료가 키보드 변형을 건넨다.", selectedActionIds: [rewardCandidate.candidateId], dialogue: [] },
    decisionType: "ACTION"
  });
  assert.equal(rewardResult.sceneSequence[0].type, "REWARD");
  assert.match(rewardResult.sceneSequence[0].text, /특수 스킬을 얻었다/);
  assert.equal(run.directorState.specialSkills[0].templateId, reward.id);
  assert.equal(publicRun(run).specialSkills[0].name, "경량 복제");

  const dormantNpc = run.entities.find((entity) => entity.active === false && entity.kind === "npc" && entity.state?.activationState === "DORMANT"
    && !run.entities.some((other) => other.active && (other.state?.slotId === entity.state.activationSlotId || (other.position.x === entity.position.x && other.position.y === entity.position.y))));
  assert.ok(dormantNpc);
  const generatedNpcCandidate = {
    candidateId: randomUUID(), type: "SPAWN_FROM_SLOT", actorId: null, targetId: player.id,
    priority: 50, cost: 2, reason: "서버 NPC 카탈로그의 런 전용 인물", actionStyle: "ARRIVAL",
    entityId: dormantNpc.id, slotId: dormantNpc.state.activationSlotId, assetId: dormantNpc.assetId, spawnKind: "npc", displayName: dormantNpc.name, traitIds: dormantNpc.state.roleTags
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
