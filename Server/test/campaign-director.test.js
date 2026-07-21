import test from "node:test";
import assert from "node:assert/strict";
import { randomUUID } from "node:crypto";
import { advanceArcDirector, advanceStoryDirector, createCampaignBlueprint } from "../src/domain/campaign.js";
import { generateWorld } from "../src/domain/world.js";
import { FixedD20Source, createRunState, directorContext, normalizeTurnRequest, resolveTurn } from "../src/domain/turn-engine.js";
import { validateNarrationOutput } from "../src/llm/narration.js";

const OWNER_ID = "55555555-5555-4555-8555-555555555555";

function adjacentWalkable(run) {
  const player = run.entities.find((item) => item.id === run.playerEntityId);
  const occupied = new Set(run.entities.filter((item) => item.active && item.blocking && item.id !== player.id)
    .map((item) => `${item.position.x},${item.position.y}`));
  for (const [dx, dy] of [[1, 0], [0, 1], [-1, 0], [0, -1]]) {
    const point = { x: player.position.x + dx, y: player.position.y + dy };
    const tile = run.world.tiles[point.y * run.world.width + point.x];
    if (point.x > 0 && point.y > 0 && point.x < run.world.width - 1 && point.y < run.world.height - 1
      && tile !== 1 && tile !== 4 && !occupied.has(`${point.x},${point.y}`)) return point;
  }
  assert.fail("Generated player entry must expose a walkable neighbor.");
}

function campaign(seed, turnLimit = 40) {
  return {
    id: randomUUID(), ownerId: OWNER_ID, title: "generated",
    turnLimit, ...createCampaignBlueprint({ worldSeed: seed, turnLimit }), world: generateWorld(seed)
  };
}

function deleteRequest(run, idempotencyKey, expectedRunVersion = run.version) {
  const player = run.entities.find((item) => item.id === run.playerEntityId);
  const target = run.entities.find((item) => item.kind === "enemy" && item.active);
  target.position = { ...player.position };
  target.state.revealed = true;
  return normalizeTurnRequest({
    inputType: "USE_SKILL",
    idempotencyKey,
    expectedRunVersion,
    skillId: "DELETE",
    targetIds: [target.id]
  });
}

test("campaign seeds preserve the nine-beat Codria contract while varying bounded run content", () => {
  const layouts = new Set();
  const roleMappings = new Set();
  const titles = new Set();
  const premises = new Set();
  const npcSignatures = new Set();
  const questSignatures = new Set();
  for (const seed of [0, 1, 2, 3, 4, 5]) {
    const blueprint = createCampaignBlueprint({ worldSeed: seed, turnLimit: 40 });
    const world = generateWorld(seed);
    assert.equal(blueprint.archetype, "codria-admin-keyboard-roguelike");
    assert.equal(blueprint.worldId, "WORLD_CODRIA");
    assert.equal(blueprint.protagonistId, "PROTAGONIST_NUPJUKYI");
    assert.equal(blueprint.requiredStoryBeats.length, 9);
    assert.equal(blueprint.endingCandidates.length, 5);
    assert.ok(/[가-힣]/.test(blueprint.premise));
    titles.add(blueprint.generatedTitle);
    premises.add(blueprint.premise);
    npcSignatures.add(blueprint.npcRoles.map((npc) => npc.displayName).join("|"));
    questSignatures.add(blueprint.questSeeds.map((quest) => quest.title).join("|"));
    layouts.add(world.layoutHash);
    roleMappings.add(world.areas.filter((area) => area.campaignRole)
      .map((area) => `${area.campaignRole}:${area.biomeId}`).sort().join("|"));
  }
  assert.deepEqual([...titles], ["Ninja Adventure"]);
  assert.ok(premises.size > 1);
  assert.ok(npcSignatures.size > 1);
  assert.ok(questSignatures.size > 1);
  assert.ok(layouts.size > 1);
  assert.ok(roleMappings.size > 1);
});

test("validated director operations enrich narrative state only through provided IDs, assets and budget", () => {
  const source = campaign(6);
  const run = createRunState({ campaign: source, ownerId: OWNER_ID, resolutionSeed: "director-test", now: "2026-07-17T00:00:00.000Z" });
  const player = run.entities.find((item) => item.id === run.playerEntityId);
  const npc = run.entities.find((item) => item.kind === "npc");
  npc.position = { ...player.position };
  const request = deleteRequest(run, "director-turn-1");
  const preview = resolveTurn({ run, request, d20Source: new FixedD20Source(10) });
  const context = directorContext(preview.run, preview.turn);
  assert.equal(context.spatialContext.authority, "SERVER");
  assert.ok(context.visibleEntities.every((entity) => !Object.hasOwn(entity, "position")));
  assert.ok(context.placementSlots.every((candidate) => !Object.hasOwn(candidate, "x") && !Object.hasOwn(candidate, "y")));
  const slot = context.readOnlySlots[0];
  assert.ok(slot, "a pre-generated free placement slot is required");
  const raw = {
    summary: "증언이 길 위에 흔적을 남겼다",
    body: "증인은 넙죽이의 신중한 편집을 기억한다. 오래된 소문과 물체의 흔적이 같은 방향을 가리킨다.",
    dialogue: [{ speakerId: npc.id, line: "좌표는 그대로지만, 우리가 기억하는 길은 달라졌어요." }],
    proposedOps: [
      { op: "SET_WORLD_FACT", summary: "증언이 확인됐다", key: "run.clue.witness", value: "증인은 같은 흔적을 두 번 보았다.", budgetCost: 0 },
      { op: "ADD_RUMOR", summary: "동쪽 경계에서 같은 목소리가 들린다.", ttlTurns: 5, budgetCost: 0 },
      { op: "ADD_NPC_MEMORY", summary: "플레이어가 증언을 강요하지 않고 확인했다.", targetId: npc.id, ttlTurns: 8, importance: 0.7, budgetCost: 1 },
      { op: "CHANGE_AFFINITY", summary: "신중한 태도를 신뢰한다.", targetId: npc.id, delta: 2, budgetCost: 1 },
      { op: "SET_VISUAL_INTENT", summary: "증언이 가리킨 작은 표식", slotId: slot.id, value: "낡은 표식 위로 희미한 패킷 빛이 맴돈다", budgetCost: 0 }
    ]
  };
  const validated = validateNarrationOutput(raw, context);
  const committed = resolveTurn({ run, request, forcedD20: preview.turn.d20, directorOutput: { ...validated, fallbackUsed: false, model: "fake-director" } });
  assert.equal(committed.run.world.layoutHash, run.world.layoutHash);
  assert.ok(committed.run.canonicalFacts.some((fact) => fact.predicate === "witness"));
  assert.ok(committed.run.rumors.some((rumor) => rumor.summary.includes("동쪽 경계")));
  assert.ok(committed.run.npcMemories.some((memory) => memory.npcId === npc.id));
  assert.equal(committed.run.npcRelationships.find((item) => item.npcId === npc.id).affinity, 2);
  assert.ok(committed.run.slotEnrichments.some((item) => item.slotId === slot.id));
  assert.equal(committed.turn.narrative.appliedOps.length, 5);
  assert.equal(committed.turn.narrative.rejectedOps.length, 0);
});

test("director validation rejects coordinates, unknown IDs, invalid assets and final-window quests", () => {
  const source = campaign(2);
  const run = createRunState({ campaign: source, ownerId: OWNER_ID, resolutionSeed: "validator-test" });
  const request = deleteRequest(run, "validator-turn-1");
  const preview = resolveTurn({ run, request, d20Source: new FixedD20Source(10) });
  const context = directorContext(preview.run, preview.turn);
  const base = { summary: "요약", body: "서버가 판정한 결과가 적용됐다. 월드 geometry는 그대로 유지된다.", dialogue: [] };
  const ambientContext = { ...context, normalizedAttempt: "Delete ambient system residue with DELETE" };
  assert.throws(() => validateNarrationOutput({ ...base, body: "계절의 파편이 깨끗이 사라졌어. 시간의 주파수도 완전히 정화되었어.", proposedOps: [] }, ambientContext), /persistent world result/);
  assert.throws(() => validateNarrationOutput({ ...base, proposedOps: [{ op: "SET_VISUAL_INTENT", summary: "불법 좌표", slotId: "missing", value: "x=999", x: 999, budgetCost: 0 }] }, context), /Unknown fields/);
  assert.throws(() => validateNarrationOutput({ ...base, proposedOps: [{ op: "ADD_NPC_MEMORY", summary: "없는 NPC", targetId: randomUUID(), budgetCost: 0 }] }, context), /outside the provided scene/);
  const visibleNpcId = context.visibleEntities.find((item) => item.kind === "npc")?.id;
  if (visibleNpcId) assert.throws(() => validateNarrationOutput({ ...base, proposedOps: [{ op: "CHANGE_AFFINITY", summary: "근거 없는 적대", targetId: visibleNpcId, delta: -3, budgetCost: 0 }] }, context), /requires consequence budget/);
  const endingContext = { ...context, remainingTurns: 3 };
  assert.throws(() => validateNarrationOutput({ ...base, proposedOps: [{ op: "START_QUEST", summary: "너무 늦은 장기 퀘스트", questTemplateId: "late", budgetCost: 0 }] }, endingContext), /final five turns/);

  const engineRejected = resolveTurn({
    run,
    request,
    forcedD20: preview.turn.d20,
    directorOutput: { ...base, proposedOps: [{ op: "SET_WORLD_FACT", summary: "좌표를 섞은 불법 연산", key: "run.bad.coordinate", value: "no", x: 999, budgetCost: 0 }], fallbackUsed: false, model: "untrusted-adapter" }
  });
  assert.equal(engineRejected.turn.narrative.appliedOps.length, 0);
  assert.equal(engineRejected.turn.narrative.rejectedOps[0].reason, "OP_SCHEMA_INVALID");
});

test("story beats require successful matching evidence and remain open beyond the soft convergence horizon", () => {
  const source = campaign(0, 40);
  const run = createRunState({ campaign: source, ownerId: OWNER_ID, resolutionSeed: "beat-evidence" });
  const first = run.requiredStoryBeats[0];
  const events = [];
  advanceStoryDirector(run, 7, events, { ability: "move", outcome: "success", contextualActions: [], campaignRole: first.requiredCampaignRole, targetEvidenceKeys: [] });
  assert.notEqual(first.status, "completed", "passing a target turn must not auto-complete a beat");
  advanceStoryDirector(run, 8, events, {
    ability: first.requiredAbility,
    outcome: "success",
    contextualActions: [first.requiredAbility],
    campaignRole: first.requiredCampaignRole,
    targetEvidenceKeys: [first.requiredEvidenceKey]
  });
  assert.equal(first.status, "completed");
  assert.ok(events.some((event) => event.beatId === first.id && event.status === "completed" && event.evidence.outcome === "success"));

  advanceStoryDirector(run, 40, events, { ability: "move", outcome: "failure", contextualActions: [], campaignRole: first.requiredCampaignRole, targetEvidenceKeys: [] });
  assert.ok(run.requiredStoryBeats.some((beat) => ["active", "pending"].includes(beat.status)));
  assert.ok(events.some((event) => event.type === "soft_convergence_pressure" && event.forcedEnding === false));
});

test("fixed arc deadlines stay disabled while meaningful committed events drive emergent story density", () => {
  const source = campaign(81, 40);
  const run = createRunState({ campaign: source, ownerId: OWNER_ID, resolutionSeed: "arc-ledger" });
  for (let turnNo = 1; turnNo <= 7; turnNo += 1) {
    advanceArcDirector(run, turnNo, [], { ability: turnNo % 2 ? "search" : "delete", outcome: turnNo === 3 ? "failure" : "success", campaignRole: "LOCAL_STAKES", targetEvidenceKeys: [], eventTypes: ["ambient_fallback_applied"] });
  }
  const events = [];
  advanceArcDirector(run, 8, events, { ability: "connect", outcome: "partial_success", campaignRole: "LOCAL_STAKES", targetEvidenceKeys: [], eventTypes: ["connection_created"] });
  assert.ok(run.arcQuestions.every((arc) => arc.status === "legacy_disabled"));
  assert.equal(run.resolvedArcOutcomes.length, 0);
  assert.equal(run.storyLedger.length, 8);
  assert.equal(run.emergentStory.phase, "entangled");
  assert.equal(run.emergentStory.meaningfulTurns, 8);
  assert.equal(run.emergentStory.forcedEnding, false);
  assert.ok(events.some((event) => event.type === "emergent_story_updated"));
  assert.ok(!events.some((event) => event.type === "arc_question_resolved"));
});

test("the soft turn horizon never forces an ending", () => {
  const source = campaign(8, 30);
  const run = createRunState({ campaign: source, ownerId: OWNER_ID, resolutionSeed: "ending-fallback" });
  run.currentTurn = 29;
  run.version = 30;
  const request = deleteRequest(run, "ending-turn-30", 30);
  const result = resolveTurn({ run, request, d20Source: new FixedD20Source(12) });
  assert.equal(result.run.status, "active");
  assert.equal(result.run.endingCode, null);
  assert.equal(result.run.currentTurn, 30);
  assert.equal(result.run.world.layoutHash, run.world.layoutHash);
  assert.ok(!result.turn.events.some((event) => event.type === "run_completed"));
});
