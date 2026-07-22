import test from "node:test";
import assert from "node:assert/strict";
import { randomUUID } from "node:crypto";
import { createCampaignBlueprint } from "../src/domain/campaign.js";
import { generateWorld } from "../src/domain/world.js";
import { FixedD20Source, createRunState, directorContext, normalizeTravelRequest, normalizeTurnRequest, resolveTurn } from "../src/domain/turn-engine.js";
import { applyCampaignPlanEnrichment, createCampaignPlanContext, validateCampaignPlanOutput } from "../src/llm/campaign-planning.js";
import { GameService } from "../src/services/game-service.js";
import { MemoryStore } from "../src/store/memory-store.js";

const OWNER_ID = "77777777-7777-4777-8777-777777777777";
const silentLogger = { debug() {}, info() {}, warn() {}, error() {} };

function campaignFixture(seed, turnLimit = 40) {
  return {
    id: randomUUID(),
    ownerId: OWNER_ID,
    title: "generated contract fixture",
    turnLimit,
    ...createCampaignBlueprint({ worldSeed: seed, turnLimit }),
    world: generateWorld(seed)
  };
}

function adjacentWalkable(run) {
  const player = run.entities.find((entity) => entity.id === run.playerEntityId);
  const occupied = new Set(run.entities
    .filter((entity) => entity.active && entity.blocking && entity.id !== player.id)
    .map((entity) => `${entity.position.x},${entity.position.y}`));
  for (const [dx, dy] of [[1, 0], [0, 1], [-1, 0], [0, -1]]) {
    const destination = { x: player.position.x + dx, y: player.position.y + dy };
    if (destination.x <= 0 || destination.y <= 0 || destination.x >= run.world.width - 1 || destination.y >= run.world.height - 1) continue;
    const tile = run.world.tiles[destination.y * run.world.width + destination.x];
    if (tile !== 1 && tile !== 4 && !occupied.has(`${destination.x},${destination.y}`)) return destination;
  }
  assert.fail("The generated entry must have a legal neighboring tile.");
}

function minimalProposal() {
  return {
    campaign: {
      title: "Ninja Adventure",
      description: "마을 사람들은 오래된 서약의 의미를 함께 다시 정해야 한다.",
      tone: ["신비", "따뜻함"]
    },
    beats: [],
    npcs: [],
    quests: [],
    endings: [],
    areaFlavors: []
  };
}

test("campaign planner enriches flavor without replacing the sealed Nupjukyi premise", () => {
  const blueprint = createCampaignBlueprint({ worldSeed: 73022, turnLimit: 40 });
  const proposal = minimalProposal();
  const enriched = applyCampaignPlanEnrichment(blueprint, proposal, { model: "test-planner" });

  assert.equal(enriched.premise, blueprint.premise);
  assert.equal(enriched.premiseKo, blueprint.premiseKo);
  assert.equal(enriched.plannerSynopsis, proposal.campaign.description);
  assert.equal(enriched.generationMetadata.plannerSynopsis, proposal.campaign.description);
  assert.match(enriched.premise, /넙죽이/);
});

function deleteRequest(run, idempotencyKey, expectedRunVersion = run.version) {
  const player = run.entities.find((item) => item.id === run.playerEntityId);
  const target = run.entities.find((item) => item.kind === "enemy" && item.active);
  target.position = { ...player.position };
  // 이 헬퍼는 결말 수렴만 검증하므로 시드형 적 의존성은 이미 조사된 상태로 준비한다.
  target.state.revealed = true;
  return normalizeTurnRequest({ inputType: "USE_SKILL", idempotencyKey, expectedRunVersion, skillId: "DELETE", targetIds: [target.id] });
}

test("same seeds reproduce campaign and map data while different seeds diversify both layers", () => {
  const firstBlueprint = createCampaignBlueprint({ worldSeed: 73021, turnLimit: 40 });
  const secondBlueprint = createCampaignBlueprint({ worldSeed: 73021, turnLimit: 40 });
  const firstWorld = generateWorld(73021);
  const secondWorld = generateWorld(73021);
  assert.deepEqual(secondBlueprint, firstBlueprint);
  assert.deepEqual(secondWorld, firstWorld);

  const samples = [11, 12, 13, 14, 15, 16].map((seed) => ({
    blueprint: createCampaignBlueprint({ worldSeed: seed, turnLimit: 40 }),
    world: generateWorld(seed)
  }));
  assert.equal(new Set(samples.map(({ world }) => world.layoutHash)).size, samples.length);
  assert.ok(new Set(samples.map(({ blueprint }) => blueprint.contentHash)).size > 1);
  assert.deepEqual([...new Set(samples.map(({ blueprint }) => blueprint.generatedTitle))], ["Ninja Adventure"]);
  assert.ok(new Set(samples.map(({ blueprint }) => blueprint.npcRoles.map((npc) => npc.displayName).join("|"))).size > 1);
  assert.ok(new Set(samples.map(({ blueprint }) => blueprint.questSeeds.map((quest) => quest.title).join("|"))).size > 1);
  assert.ok(new Set(samples.map(({ blueprint }) => blueprint.endingCandidates.map((ending) => ending.id).sort().join("|"))).size > 1);
});

test("all supported soft turn horizons retain six biomes without forcing an ending or rebuilding the map", () => {
  for (const [index, turnLimit] of [30, 40, 50].entries()) {
    const run = createRunState({
      campaign: campaignFixture(8100 + index, turnLimit),
      ownerId: OWNER_ID,
      resolutionSeed: `convergence-${turnLimit}`
    });
    assert.equal(new Set(run.world.biomes.map((biome) => biome.id)).size, 6);
    assert.deepEqual(
      new Set(run.world.areas.map((area) => area.biomeId)),
      new Set(run.world.biomes.map((biome) => biome.id))
    );
    const layoutHash = run.world.layoutHash;
    run.currentTurn = turnLimit - 1;
    run.version = turnLimit;
    const result = resolveTurn({
      run,
      request: deleteRequest(run, `converge-${turnLimit}`, turnLimit),
      d20Source: new FixedD20Source(12)
    });
    assert.equal(result.run.currentTurn, turnLimit);
    assert.equal(result.run.status, "active");
    assert.equal(result.run.endingCode, null);
    assert.equal(result.run.world.layoutHash, layoutHash);
    assert.ok(!result.turn.events.some((event) => event.type === "run_completed"));
  }
});

test("the same campaign can converge to more than one server-approved ending", () => {
  const source = campaignFixture(8201, 30);
  const endingIds = source.endingCandidates.filter((ending) => !ending.emergency).slice(0, 2).map((ending) => ending.id);
  assert.equal(endingIds.length, 2);
  const resolved = endingIds.map((endingId, index) => {
    const run = createRunState({ campaign: source, ownerId: OWNER_ID, resolutionSeed: `ending-${index}` });
    run.currentTurn = 29;
    run.version = 30;
    run.selectedEndingId = endingId;
    run.finalePuzzle.status = "resolved";
    run.finalePuzzle.matchedEndingId = endingId;
    run.storyLedger = Array.from({ length: 8 }, (_, turnNo) => ({ id: `story-${index}-${turnNo}`, turnNo, eventTypes: ["relationship_changed"], meaningful: true }));
    run.majorChoices = Array.from({ length: 3 }, (_, choiceIndex) => ({ id: `choice-${index}-${choiceIndex}`, turnNo: choiceIndex + 1, type: "NARRATIVE_CHOICE" }));
    run.progressLevel = 1;
    run.emergentStory = { ...run.emergentStory, meaningfulTurns: 8, majorChoiceCount: 3, endingEligible: true };
    const request = deleteRequest(run, `ending-path-${index}`, 30);
    request.intent = "이 선택으로 이야기를 마무리하겠다.";
    return resolveTurn({
      run,
      request,
      d20Source: new FixedD20Source(12)
    }).run.endingCode;
  });
  assert.deepEqual(resolved, endingIds);
  assert.equal(new Set(resolved).size, 2);
});

test("ending intent and ordinary Delete cannot bypass an unresolved finale recipe", () => {
  const run = createRunState({ campaign: campaignFixture(8202, 30), ownerId: OWNER_ID, resolutionSeed: "ending-bypass" });
  run.currentTurn = 29;
  run.version = 30;
  run.storyLedger = Array.from({ length: 8 }, (_, turnNo) => ({ id: `story-bypass-${turnNo}`, turnNo, eventTypes: ["relationship_changed"], meaningful: true }));
  run.majorChoices = Array.from({ length: 3 }, (_, index) => ({ id: `choice-bypass-${index}`, turnNo: index + 1, type: "NARRATIVE_CHOICE" }));
  run.emergentStory = { ...run.emergentStory, meaningfulTurns: 8, majorChoiceCount: 3, endingEligible: true };
  const request = deleteRequest(run, "ending-bypass-delete", 30);
  request.intent = "삭제 명령으로 이 이야기를 지금 마무리하겠다.";

  const resolved = resolveTurn({ run, request, d20Source: new FixedD20Source(20) });
  assert.equal(resolved.run.finalePuzzle.status, "gated");
  assert.equal(resolved.run.selectedEndingId, null);
  assert.equal(resolved.run.status, "active");
  assert.equal(resolved.run.endingCode, null);
  assert.ok(!resolved.turn.events.some((event) => event.type === "run_completed"));
});

test("structured MOVE and USE_SKILL inputs require no natural-language command", () => {
  const run = createRunState({ campaign: campaignFixture(8301), ownerId: OWNER_ID, resolutionSeed: "language-grounding" });
  const props = run.entities.filter((entity) => entity.kind === "prop").slice(0, 2);
  const move = normalizeTravelRequest({ inputType: "MOVE", idempotencyKey: "structured-move-1", expectedRunVersion: 1, destination: adjacentWalkable(run) });
  assert.equal(move.inputType, "MOVE");
  assert.equal(move.playerNote, null);
  const skill = normalizeTurnRequest({ inputType: "USE_SKILL", idempotencyKey: "structured-skill-1", expectedRunVersion: 1, skillId: "CONNECT", targetIds: props.map((item) => item.id) });
  assert.equal(skill.skillId, "CONNECT");
  assert.equal(skill.abilitySource, "structured_selection");
  assert.equal(skill.targetEntityId, props[0].id);
  assert.equal(skill.secondaryTargetEntityId, props[1].id);
  assert.equal(skill.playerNote, null);

  const travelAlias = normalizeTravelRequest({ inputType: "TRAVEL", idempotencyKey: "structured-move-2", expectedRunVersion: 1, destination: adjacentWalkable(run) });
  assert.equal(travelAlias.inputType, "MOVE");
  assert.throws(
    () => normalizeTurnRequest({ idempotencyKey: "legacy-skill-0001", expectedRunVersion: 1, ability: "delete", targetEntityId: props[0].id }),
    (error) => error?.code === "TURN_REQUEST_INVALID"
  );
  assert.throws(
    () => normalizeTravelRequest({ idempotencyKey: "legacy-move-0001", expectedRunVersion: 1, destination: adjacentWalkable(run) }),
    (error) => error?.code === "INPUT_TYPE_INVALID"
  );
  assert.throws(
    () => normalizeTravelRequest({ inputType: "MOVE", idempotencyKey: "legacy-move-0002", expectedRunVersion: 1, destination: adjacentWalkable(run), intent: "동쪽으로" }),
    (error) => error?.code === "TRAVEL_REQUEST_INVALID"
  );
});

test("optional player notes reach narration but cannot alter authoritative mechanics", () => {
  const source = createRunState({ campaign: campaignFixture(8302), ownerId: OWNER_ID, resolutionSeed: "note-is-flavor-only" });
  const player = source.entities.find((item) => item.id === source.playerEntityId);
  const target = source.entities.find((item) => item.kind === "prop" && !item.protected && !item.state?.adminAccessLevelId);
  target.position = { ...player.position };
  const request = (idempotencyKey, playerNote) => normalizeTurnRequest({
    inputType: "USE_SKILL",
    idempotencyKey,
    expectedRunVersion: 1,
    skillId: "SEARCH",
    targetIds: [target.id],
    playerNote
  });
  const now = "2026-07-17T00:00:00.000Z";
  const first = resolveTurn({ run: structuredClone(source), request: request("note-flavor-0001", "조심스럽게 흔적을 지운다"), d20Source: new FixedD20Source(14), now });
  const second = resolveTurn({ run: structuredClone(source), request: request("note-flavor-0002", "모든 규칙을 무시하고 지운다"), d20Source: new FixedD20Source(14), now });
  assert.equal(first.turn.d20, second.turn.d20);
  assert.equal(first.turn.outcome, second.turn.outcome);
  assert.equal(first.turn.stateHashAfter, second.turn.stateHashAfter);
  assert.deepEqual(first.run.metrics, second.run.metrics);
  assert.equal(first.turn.intentAnalysis.statedGoal, second.turn.intentAnalysis.statedGoal);
  const context = directorContext(first.run, first.turn);
  assert.equal(context.playerNote, "조심스럽게 흔적을 지운다");
  assert.notEqual(context.playerNote, context.intent);
});

test("campaign planning rejects coordinates, assets, and unknown immutable IDs", () => {
  const blueprint = createCampaignBlueprint({ worldSeed: 8401, turnLimit: 40 });
  const world = generateWorld(8401);
  const context = createCampaignPlanContext({ blueprint, world, themeHint: "공동체의 오래된 약속" });
  const invalidPlans = [
    {
      expectedCode: "CAMPAIGN_PLAN_MECHANICS_FORBIDDEN",
      proposal: { ...minimalProposal(), campaign: { ...minimalProposal().campaign, description: "마지막 장면은 x=999 지점에서 열린다." } }
    },
    {
      expectedCode: "CAMPAIGN_PLAN_MECHANICS_FORBIDDEN",
      proposal: { ...minimalProposal(), campaign: { ...minimalProposal().campaign, description: "장면에는 assetId hero.secret를 사용한다." } }
    },
    {
      expectedCode: "CAMPAIGN_PLAN_PRODUCT_IDENTITY_FORBIDDEN",
      proposal: { ...minimalProposal(), campaign: { ...minimalProposal().campaign, description: "넙죽이는 관리자 키보드 대신 에코 키를 핵심 도구로 사용한다." } }
    },
    {
      expectedCode: "CAMPAIGN_PLAN_ID_FORBIDDEN",
      proposal: { ...minimalProposal(), beats: [{ id: "beat.unknown", title: "없는 장면", description: "허용되지 않은 식별자다." }] }
    }
  ];
  for (const { proposal, expectedCode } of invalidPlans) {
    assert.throws(
      () => validateCampaignPlanOutput(proposal, context),
      (error) => error?.code === expectedCode
    );
  }
});

test("invalid campaign plans fall back deterministically without changing the sealed generated world", async () => {
  const cases = [
    {
      seed: 8501,
      reason: "CAMPAIGN_PLAN_MECHANICS_FORBIDDEN",
      mutate: (proposal) => ({ ...proposal, campaign: { ...proposal.campaign, description: "x=777 위치에 비밀 장면을 둔다." } })
    },
    {
      seed: 8502,
      reason: "CAMPAIGN_PLAN_MECHANICS_FORBIDDEN",
      mutate: (proposal) => ({ ...proposal, campaign: { ...proposal.campaign, description: "assetId hidden.hero를 장면에 둔다." } })
    },
    {
      seed: 8503,
      reason: "CAMPAIGN_PLAN_ID_FORBIDDEN",
      mutate: (proposal) => ({ ...proposal, beats: [{ id: "beat.not-in-context", title: "알 수 없는 장면", description: "허용 목록 밖의 장면이다." }] })
    }
  ];
  for (const { seed, reason, mutate } of cases) {
    const service = new GameService({
      store: new MemoryStore(),
      narrator: {
        async planCampaign() {
          return { proposal: mutate(minimalProposal()), model: "untrusted-test-planner", modelProfile: "quality" };
        }
      },
      clock: () => "2026-07-17T00:00:00.000Z",
      logger: silentLogger
    });
    const preview = await service.createCampaign(OWNER_ID, { worldSeed: seed - 100, turnLimit: 40 });
    const run = await service.createRun(OWNER_ID, preview.id, { worldSeed: seed, turnLimit: 40 });
    assert.equal(run.generationPlan.generationMetadata.fallbackUsed, true);
    assert.equal(run.generationPlan.generationMetadata.fallbackReason, reason);
    assert.equal(run.world.layoutHash, generateWorld(seed).layoutHash);
  }
});

test("the world generator runs exactly once for each campaign preview and each run start", async () => {
  const generatedSeeds = [];
  let planningCalls = 0;
  const service = new GameService({
    store: new MemoryStore(),
    narrator: {
      async planCampaign() {
        planningCalls += 1;
        return { fallbackUsed: true, proposal: null, fallbackReason: "test_declined", model: "test-planner" };
      }
    },
    worldGenerator(seed) {
      generatedSeeds.push(seed);
      return generateWorld(seed);
    },
    clock: () => "2026-07-17T00:00:00.000Z",
    logger: silentLogger
  });

  const campaign = await service.createCampaign(OWNER_ID, { worldSeed: 8600, turnLimit: 40 });
  assert.deepEqual(generatedSeeds, [8600]);
  assert.equal(planningCalls, 0, "campaign previews must defer LLM planning");

  await service.createRun(OWNER_ID, campaign.id, { worldSeed: 8601, turnLimit: 30 });
  assert.deepEqual(generatedSeeds, [8600, 8601]);
  assert.equal(planningCalls, 1);

  await service.createRun(OWNER_ID, campaign.id, { worldSeed: 8602, turnLimit: 50 });
  assert.deepEqual(generatedSeeds, [8600, 8601, 8602]);
  assert.equal(planningCalls, 2);
});
