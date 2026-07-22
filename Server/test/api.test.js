import test from "node:test";
import assert from "node:assert/strict";
import { createApplication } from "../src/app.js";
import { loadConfig } from "../src/config.js";
import { FixedD20Source } from "../src/domain/turn-engine.js";
import { MemoryStore } from "../src/store/memory-store.js";
import { GeminiNarrator } from "../src/llm/gemini-narrator.js";

const USER_ID = "11111111-1111-4111-8111-111111111111";
const OTHER_USER_ID = "22222222-2222-4222-8222-222222222222";
const silentLogger = { debug() {}, info() {}, warn() {}, error() {} };

function decodeTiles(world) {
  const tiles = [];
  for (const [code, count] of world.tilesRle) {
    for (let index = 0; index < count; index += 1) tiles.push(code);
  }
  assert.equal(tiles.length, world.width * world.height);
  return tiles;
}

function adjacentWalkable(world, origin) {
  const tiles = decodeTiles(world);
  const blocked = new Set(["wall", "water"]);
  for (const [dx, dy] of [[1, 0], [0, 1], [-1, 0], [0, -1]]) {
    const point = { x: origin.x + dx, y: origin.y + dy };
    if (point.x < 0 || point.y < 0 || point.x >= world.width || point.y >= world.height) continue;
    const tileName = world.tileLegend[tiles[point.y * world.width + point.x]];
    if (!blocked.has(tileName)) return point;
  }
  assert.fail("Generated entry must have an adjacent walkable tile.");
}

class FakeNarrator {
  async narrate(context) {
    return {
      summary: `${context.area}에서 제한된 장면이 이어진다`,
      body: "서버가 선택한 키보드 기술을 적용했다. 확정된 상태는 봉인된 코드리아 세계 안에 유지된다.",
      dialogue: [],
      proposedOps: [],
      fallbackUsed: false,
      model: "fake-narrator"
    };
  }
}

class DuplicateChoiceNarrator extends FakeNarrator {
  async narrate(context) {
    const base = await super.narrate(context);
    const duplicateText = "잠시 멈춰 서서 주변 상황을 차분하게 살핀다.";
    return {
      ...base,
      storySequence: [{
        type: "NARRATION",
        speakerId: null,
        actionId: null,
        text: "방금 선택의 여파가 가라앉고, 넙죽이는 다음 움직임을 정해야 한다."
      }],
      nextIntervention: {
        reason: "다음 행동을 선택한다.",
        choices: [
          {
            choiceId: "duplicate.wait",
            text: duplicateText,
            choiceKind: "ATTITUDE",
            intentTag: "CAUTIOUS",
            resolutionMode: "NONE",
            skillId: null,
            targetEntityId: null,
            destinationRef: null
          },
          {
            choiceId: "duplicate.observe",
            text: duplicateText,
            choiceKind: "DIALOGUE",
            intentTag: "CURIOUS",
            resolutionMode: "NONE",
            skillId: null,
            targetEntityId: null,
            destinationRef: null
          }
        ]
      }
    };
  }
}

class SlowSceneNarrator extends FakeNarrator {
  constructor() {
    super();
    this.planSceneCalls = 0;
  }

  async planScene(context) {
    this.planSceneCalls += 1;
    await new Promise((resolve) => setTimeout(resolve, 500));
    return {
      sceneGoal: "원격 장면 계획이 늦게 도착했다.",
      selectedActionIds: [context.candidates[0].candidateId],
      proposedActions: [],
      dialogue: [],
      fallbackUsed: false,
      model: "slow-remote-scene-planner"
    };
  }
}

class StructuredActionNarrator extends FakeNarrator {
  async planPlayerAction(context) {
    return {
      kind: "ACQUIRE",
      targetEntityIds: [],
      itemIds: [],
      destinationRef: null,
      resultItem: {
        name: "마을의 등불 조각",
        kind: "material",
        description: "고향의 온기를 희미하게 머금은 차가운 금속성 파편."
      },
      reason: `전체 문맥상 ${context.playerText.length}자의 입력은 균열 속 물체를 확보하려는 시도다.`
    };
  }
}

class FalseAcquisitionNarrator extends StructuredActionNarrator {
  async narrate() {
    return {
      summary: "거짓 획득 결과",
      body: "균열에서 마을의 등불 조각을 꺼냈다. 차가운 파편이 손에 잡혔다.",
      dialogue: [], proposedOps: [], fallbackUsed: false, model: "false-acquisition-narrator"
    };
  }
}

class MoveActionNarrator extends FakeNarrator {
  async planPlayerAction(context) {
    const destination = context.destinations.find((item) => item.ref.startsWith("step."));
    return {
      kind: "MOVE", targetEntityIds: [], itemIds: [], destinationRef: destination.ref,
      resultItem: null, reason: "입력 전체가 가까운 방향으로 이동하려는 시도다."
    };
  }
}

class MismatchedEncounterChoiceNarrator extends FakeNarrator {
  constructor() {
    super();
    this.contexts = [];
  }

  async narrate(context) {
    this.contexts.push(structuredClone(context));
    const encounterTargetId = context.spatialContext?.activeEncounter?.sourceEntityId || null;
    const enemy = context.visibleEntities.find((entity) => entity.id === encounterTargetId && entity.kind === "enemy");
    const npc = context.visibleEntities.find((entity) => entity.kind === "npc");
    if (!enemy || !npc) return super.narrate(context);
    return {
      summary: "적대 신호와 동료의 경고가 동시에 들린다",
      body: `${enemy.name}의 적대 신호가 가까운 곳에서 요동친다. ${npc.name}은 넙죽이에게 성급히 움직이지 말라고 경고한다.`,
      dialogue: [],
      storySequence: [{
        type: "NARRATION",
        speakerId: null,
        actionId: null,
        text: `${enemy.name}의 신호와 ${npc.name}의 경고가 서로 다른 방향에서 겹쳐 들렸다.`
      }],
      nextIntervention: {
        reason: "눈앞의 적대 신호에 어떻게 대응할지 선택한다.",
        choices: [
          {
            choiceId: "choice.attack.delete",
            text: `${enemy.name}에게 삭제 명령으로 직접 맞선다.`,
            choiceKind: "SKILL",
            intentTag: "ASSERTIVE",
            resolutionMode: "D20",
            skillId: "DELETE",
            targetEntityId: npc.id,
            destinationRef: null
          },
          {
            choiceId: "choice.dialogue.ask",
            text: `${npc.name}에게 방금 본 신호가 무엇인지 자세히 묻는다.`,
            choiceKind: "DIALOGUE",
            intentTag: "CURIOUS",
            resolutionMode: "NONE",
            skillId: null,
            targetEntityId: npc.id,
            destinationRef: null
          }
        ]
      },
      proposedOps: [],
      elementalEffectId: null,
      fallbackUsed: false,
      model: "mismatched-encounter-choice-test"
    };
  }
}

class ProtectedCombinationNarrator extends FakeNarrator {
  constructor() {
    super();
    this.contexts = [];
  }

  async planPlayerAction(context) {
    if (context.inventory.length === 1) {
      return {
        kind: "ACQUIRE", targetEntityIds: [], itemIds: [], destinationRef: null,
        resultItem: { name: "빛나는 파편", kind: "material", description: "땅에서 발견한 빛나는 파편." },
        reason: "빛나는 파편을 확보하려는 시도다."
      };
    }
    return {
      kind: "COMBINE", targetEntityIds: [], itemIds: context.inventory.map((item) => item.id).slice(0, 2), destinationRef: null,
      resultItem: { name: "개조된 관리자 키보드", kind: "key_item", description: "파편을 결합한 관리자 키보드." },
      reason: "관리자 키보드에 빛나는 파편을 결합하려는 시도다."
    };
  }

  async narrate(context) {
    this.contexts.push(structuredClone(context));
    const rejected = context.confirmedEffects?.find((event) => event.type === "player_action_rejected");
    if (!rejected) return super.narrate(context);
    return {
      summary: "키보드와 파편의 조합이 성립하지 않았다",
      body: `파편을 키보드에 맞춰 봤지만 결합되지 않는다. ${rejected.reason}`,
      dialogue: [], proposedOps: [], fallbackUsed: false, model: "protected-combination-narrator"
    };
  }
}

class ContinuityNarrator {
  constructor() {
    this.contexts = [];
  }

  async narrate(context) {
    this.contexts.push(structuredClone(context));
    const sceneNo = this.contexts.length;
    return {
      summary: `연속성 요약 표식 ${sceneNo}이 기록된다`,
      body: `연속성 본문 표식 ${sceneNo}이 장면에 남았다. 다음 반응은 이 기억을 바탕으로 이어진다.`,
      dialogue: [],
      storySequence: [{
        type: "NARRATION",
        speakerId: null,
        actionId: null,
        text: `청록 종소리 장면 표식 ${sceneNo}이 폐허에 울렸다.`
      }],
      nextIntervention: {
        reason: "방금 들은 종소리에 어떻게 반응할지 선택한다.",
        choices: [
          {
            choiceId: "continuity.listen",
            text: "종소리가 남긴 의미를 조용히 되짚어 본다.",
            choiceKind: "DIALOGUE",
            intentTag: "CURIOUS",
            resolutionMode: "NONE",
            skillId: null,
            targetEntityId: null,
            destinationRef: null
          },
          {
            choiceId: "continuity.wait",
            text: "성급히 판단하지 않고 다음 울림을 기다린다.",
            choiceKind: "ATTITUDE",
            intentTag: "CAUTIOUS",
            resolutionMode: "NONE",
            skillId: null,
            targetEntityId: null,
            destinationRef: null
          }
        ]
      },
      elementalEffectId: null,
      proposedOps: [],
      fallbackUsed: false,
      model: "continuity-narrator"
    };
  }
}

async function startServer({ d20Source = new FixedD20Source(20), narrator = new FakeNarrator(), configEnv = {} } = {}) {
  const config = loadConfig({ AUTH_MODE: "required", STORAGE: "memory", LOG_LEVEL: "silent", ...configEnv });
  const application = await createApplication({
    config,
    store: new MemoryStore(),
    narrator,
    d20Source,
    logger: silentLogger
  });
  await new Promise((resolve) => application.server.listen(0, "127.0.0.1", resolve));
  const address = application.server.address();
  return { application, baseUrl: `http://127.0.0.1:${address.port}` };
}

async function jsonRequest(baseUrl, path, { method = "GET", body, userId = USER_ID, origin } = {}) {
  const headers = { "x-user-id": userId };
  if (body !== undefined) headers["content-type"] = "application/json";
  if (origin) headers.origin = origin;
  const response = await fetch(`${baseUrl}${path}`, {
    method,
    headers,
    body: body === undefined ? undefined : JSON.stringify(body)
  });
  const payload = response.status === 204 ? null : await response.json();
  return { response, payload };
}

test("duplicate generated choice text falls back before sealing instead of rolling back the selected turn", async (t) => {
  const { application, baseUrl } = await startServer({ narrator: new DuplicateChoiceNarrator() });
  t.after(() => application.close());
  const campaign = (await jsonRequest(baseUrl, "/v1/campaigns", {
    method: "POST",
    body: { title: "Choice seal fallback", worldSeed: 20260722, turnLimit: 40 }
  })).payload.campaign;
  const created = (await jsonRequest(baseUrl, `/v1/campaigns/${campaign.id}/runs`, {
    method: "POST",
    body: {}
  })).payload.run;
  const choice = created.pendingChoiceSet.choices[0];

  const selected = await jsonRequest(baseUrl, `/v1/runs/${created.id}/choices`, {
    method: "POST",
    body: {
      choiceSetId: created.pendingChoiceSet.choiceSetId,
      choiceId: choice.choiceId,
      idempotencyKey: "duplicate-choice-seal-fallback-0001",
      expectedRunVersion: created.version
    }
  });

  assert.equal(selected.response.status, 201, JSON.stringify(selected.payload));
  assert.equal(selected.payload.run.currentTurn, 1);
  assert.equal(selected.payload.turn.narrative.fallbackUsed, true);
  const texts = selected.payload.run.pendingChoiceSet.choices.map((item) => item.text);
  assert.equal(new Set(texts).size, texts.length);
});

test("health and campaign endpoints expose deterministic previews while each run seals its own generated world", async (t) => {
  const { application, baseUrl } = await startServer();
  t.after(() => application.close());

  const health = await jsonRequest(baseUrl, "/health", { userId: undefined });
  assert.equal(health.response.status, 200);
  assert.equal(health.payload.authoritativeTurns, true);
  assert.equal(health.payload.butterflySceneDirector, true);
  assert.equal(health.payload.productContract.world.id, "WORLD_CODRIA");
  assert.deepEqual(health.payload.productContract.skills, ["COPY", "DELETE", "CONNECT", "RESTORE", "UNDO", "SEARCH", "SELECT_ALL"]);

  const created = await jsonRequest(baseUrl, "/v1/campaigns", {
    method: "POST",
    body: { title: "Seeded Run", worldSeed: 9917, turnLimit: 40 }
  });
  assert.equal(created.response.status, 201);
  assert.equal(created.payload.campaign.world.width, 160);
  assert.equal(created.payload.campaign.world.height, 160);
  assert.ok(created.payload.campaign.world.tilesRle.length > 0);
  assert.equal(created.payload.campaign.world.areas.length, 12);
  assert.ok(created.payload.campaign.world.placementSlots.length >= 72);
  assert.equal(new Set(created.payload.campaign.world.biomes.map((item) => item.id)).size, 6);
  assert.equal(new Set(created.payload.campaign.world.areas.map((item) => item.campaignRole).filter(Boolean)).size, 6);
  assert.ok(created.payload.campaign.world.routes.length >= 13);
  assert.equal(created.payload.campaign.world.generationReport.status, "valid");
  assert.equal(created.payload.campaign.archetype, "codria-admin-keyboard-roguelike");
  assert.equal(created.payload.campaign.gameTitle, "Ninja Adventure");
  assert.equal(created.payload.campaign.worldId, "WORLD_CODRIA");
  assert.ok(created.payload.campaign.premise.length > 20);
  assert.equal(created.payload.campaign.requiredStoryBeats.length, 9);
  assert.equal(created.payload.campaign.campaignMacroPhases.length, 7);
  assert.equal(created.payload.campaign.endingCandidates.length, 5);
  assert.equal("tiles" in created.payload.campaign.world, false);

  const duplicateSeed = await jsonRequest(baseUrl, "/v1/campaigns", {
    method: "POST",
    body: { title: "Same Seed", worldSeed: 9917, turnLimit: 40 }
  });
  assert.equal(duplicateSeed.payload.campaign.world.layoutHash, created.payload.campaign.world.layoutHash);

  const campaignId = created.payload.campaign.id;
  const runCreated = await jsonRequest(baseUrl, `/v1/campaigns/${campaignId}/runs`, {
    method: "POST",
    body: { worldSeed: 9918, turnLimit: 40, themeHint: "오래된 약속과 변화하는 공동체" }
  });
  assert.equal(runCreated.response.status, 201);
  assert.equal(runCreated.payload.run.version, 1);
  assert.equal(runCreated.payload.run.currentTurn, 0);
  assert.notEqual(runCreated.payload.run.world.layoutHash, created.payload.campaign.world.layoutHash);
  assert.equal(runCreated.payload.run.currentBeat, runCreated.payload.run.currentStoryBeat.title);
  assert.equal(runCreated.payload.run.currentMacroPhase.id, "MACRO_ARRIVAL_AWAKENING");
  assert.equal(runCreated.payload.run.campaignTitle, "Ninja Adventure");
  assert.notEqual(runCreated.payload.run.campaignContentHash, created.payload.campaign.contentHash);
  assert.equal(runCreated.payload.run.generationPlan.generationMetadata.fallbackUsed, true);
  assert.equal(runCreated.payload.run.canonicalFacts.length, 7);
  assert.equal(runCreated.payload.run.rumors.length, 1);
  assert.ok(runCreated.payload.run.npcRelationships.length >= 6);
  assert.equal(runCreated.payload.run.health, 12);
  assert.equal(runCreated.payload.run.maxHealth, 12);
  assert.equal(runCreated.payload.run.maxFocus, 10);
  assert.deepEqual(runCreated.payload.run.abilities, ["copy", "delete", "connect", "restore", "undo", "search", "select_all"]);
  assert.deepEqual(runCreated.payload.run.inputTypes, ["MOVE", "USE_SKILL", "NARRATIVE_CHOICE"]);
  assert.match(runCreated.payload.run.pendingChoiceSet.choiceSetId, /^[0-9a-f-]{36}$/i);
  assert.ok(runCreated.payload.run.pendingChoiceSet.choices.length >= 2);
  assert.ok(runCreated.payload.run.pendingChoiceSet.choices.some((choice) => choice.choiceKind !== "SKILL"));

  const inheritedSeedRun = await jsonRequest(baseUrl, `/v1/campaigns/${campaignId}/runs`, {
    method: "POST",
    body: {}
  });
  assert.equal(inheritedSeedRun.response.status, 201);
  assert.equal(inheritedSeedRun.payload.run.world.worldSeed, created.payload.campaign.world.worldSeed);
  assert.equal(inheritedSeedRun.payload.run.world.layoutHash, created.payload.campaign.world.layoutHash);

  const hidden = await jsonRequest(baseUrl, `/v1/campaigns/${campaignId}`, { userId: OTHER_USER_ID });
  assert.equal(hidden.response.status, 404);
});

test("ambient wander authoritatively moves visible NPCs on the grid", async (t) => {
  const { application, baseUrl } = await startServer();
  t.after(() => application.close());
  const campaign = await jsonRequest(baseUrl, "/v1/campaigns", {
    method: "POST", body: { title: "Wander Test", worldSeed: 22017, turnLimit: 40 }
  });
  const created = await jsonRequest(baseUrl, `/v1/campaigns/${campaign.payload.campaign.id}/runs`, {
    method: "POST", body: {}
  });
  const before = created.payload.run;
  const positions = new Map(before.entities.filter((item) => item.kind === "npc").map((item) => [item.id, item.position]));
  const wandered = await jsonRequest(baseUrl, `/v1/runs/${before.id}/ambient-wander`, {
    method: "POST",
    body: { expectedRunVersion: before.version, minX: 0, minY: 0, maxX: 159, maxY: 159 }
  });
  assert.equal(wandered.response.status, 200);
  assert.ok(wandered.payload.movedEntityIds.length > 0);
  assert.equal(wandered.payload.run.version, before.version + 1);
  for (const id of wandered.payload.movedEntityIds) {
    const after = wandered.payload.run.entities.find((item) => item.id === id).position;
    const origin = positions.get(id);
    assert.equal(Math.abs(after.x - origin.x) + Math.abs(after.y - origin.y), 1);
  }
});

test("ambient wander cannot mutate abandoned runs", async (t) => {
  const { application, baseUrl } = await startServer();
  t.after(() => application.close());
  const campaign = (await jsonRequest(baseUrl, "/v1/campaigns", {
    method: "POST", body: { title: "Terminal Wander", worldSeed: 22018, turnLimit: 40 }
  })).payload.campaign;
  const created = (await jsonRequest(baseUrl, `/v1/campaigns/${campaign.id}/runs`, {
    method: "POST", body: {}
  })).payload.run;
  const abandoned = (await jsonRequest(baseUrl, `/v1/runs/${created.id}/abandon`, {
    method: "POST", body: { expectedRunVersion: created.version }
  })).payload.run;
  const before = structuredClone(await application.store.getRun(USER_ID, created.id));
  const wandered = await jsonRequest(baseUrl, `/v1/runs/${created.id}/ambient-wander`, {
    method: "POST",
    body: { expectedRunVersion: abandoned.version, minX: 0, minY: 0, maxX: 159, maxY: 159 }
  });
  assert.equal(wandered.response.status, 409);
  assert.equal(wandered.payload.error.code, "RUN_NOT_ACTIVE");
  assert.deepEqual(await application.store.getRun(USER_ID, created.id), before);
});

test("turn submit is authoritative, versioned and idempotent without rebuilding the map", async (t) => {
  const { application, baseUrl } = await startServer();
  t.after(() => application.close());
  const campaign = (await jsonRequest(baseUrl, "/v1/campaigns", {
    method: "POST",
    body: { title: "Turns", worldSeed: 12345, turnLimit: 30 }
  })).payload.campaign;
  const run = (await jsonRequest(baseUrl, `/v1/campaigns/${campaign.id}/runs`, { method: "POST", body: {} })).payload.run;
  const playerBefore = run.entities.find((entity) => entity.id === run.playerEntityId);
  const entry = run.world.points.find((point) => point.id === "entry");
  assert.deepEqual(playerBefore.position, { x: entry.x, y: entry.y });

  const stored = application.store.runs.get(run.id);
  const target = stored.entities.find((entity) => entity.kind === "enemy" && entity.active &&
    !entity.state?.adminAccessLevelId);
  target.assetId = "enemy.slime.v1";
  const storedPlayer = stored.entities.find((entity) => entity.id === stored.playerEntityId);
  for (const entity of stored.entities) {
    if (entity.id !== target.id && entity.id !== storedPlayer.id
      && entity.position.x === storedPlayer.position.x && entity.position.y === storedPlayer.position.y) entity.active = false;
  }
  target.position = { ...storedPlayer.position };
  target.state.hp = 5;
  target.state.revealed = true;
  stored.pendingChoiceSet.choices[2] = {
    choiceId: "test.delete",
    text: "지금 길을 막는 존재에게 삭제 명령으로 경계를 분명히 한다.",
    choiceKind: "SKILL",
    intentTag: "ASSERTIVE",
    resolutionMode: "D20",
    skillId: "DELETE",
    targetEntityId: target.id,
    destinationRef: null
  };
  stored.pendingChoiceSet.suggestedSkillIds = ["DELETE"];
  application.store.runs.set(run.id, stored);
  const legacyAction = await jsonRequest(baseUrl, `/v1/runs/${run.id}/actions`, {
    method: "POST",
    body: { idempotencyKey: "legacy-action-0001", expectedRunVersion: 1, ability: "delete", targetEntityId: target.id, intent: "지워" }
  });
  assert.equal(legacyAction.response.status, 400);
  assert.equal(legacyAction.payload.error.code, "TURN_REQUEST_INVALID");
  const legacyTravel = await jsonRequest(baseUrl, `/v1/runs/${run.id}/travel`, {
    method: "POST",
    body: { idempotencyKey: "legacy-travel-0001", expectedRunVersion: 1, destination: adjacentWalkable(run.world, playerBefore.position) }
  });
  assert.equal(legacyTravel.response.status, 400);
  assert.equal(legacyTravel.payload.error.code, "INPUT_TYPE_INVALID");
  const request = {
    inputType: "USE_SKILL",
    idempotencyKey: "turn-0001",
    expectedRunVersion: run.version,
    skillId: "DELETE",
    targetIds: [target.id]
  };
  const bypassed = await jsonRequest(baseUrl, `/v1/runs/${run.id}/actions`, { method: "POST", body: request });
  assert.equal(bypassed.response.status, 409);
  assert.equal(bypassed.payload.error.code, "CHOICE_REQUIRED");
  const choiceRequest = {
    choiceSetId: stored.pendingChoiceSet.choiceSetId,
    choiceId: "test.delete",
    idempotencyKey: "turn-0001",
    expectedRunVersion: run.version
  };
  const first = await jsonRequest(baseUrl, `/v1/runs/${run.id}/choices`, { method: "POST", body: choiceRequest });
  assert.equal(first.response.status, 201, JSON.stringify(first.payload));
  assert.equal(first.payload.turn.d20, 20);
  assert.equal(first.payload.turn.outcome, "critical_success");
  assert.equal(first.payload.turn.dice.raw, 20);
  assert.equal(first.payload.turn.dice.difficulty, 12);
  assert.equal(first.payload.turn.dice.modifier, 3);
  assert.equal(typeof first.payload.turn.dice.intentAlignment, "number");
  assert.ok(first.payload.turn.dice.outcomeExplanation.includes("difficulty"));
  assert.ok(Array.isArray(first.payload.turn.stateDelta.events));
  assert.ok(Array.isArray(first.payload.turn.narrative.dialogue));
  assert.ok(Array.isArray(first.payload.turn.narrative.dialogueDetails));
  assert.ok(Array.isArray(first.payload.turn.sceneSequence));
  assert.equal(first.payload.turn.runtime.resolution.required, true);
  assert.equal(first.payload.turn.runtime.resolution.roll.resultTier, "STRONG_SUCCESS");
  assert.equal(first.payload.turn.runtime.resolution.roll.total, 23);
  assert.equal(first.payload.turn.runtime.resolution.roll.mechanicalScore, first.payload.turn.mechanicalScore);
  assert.equal(first.payload.turn.runtime.unity.renderRequired, true);
  assert.equal(first.payload.turn.runtime.unity.events[0].type, "DELETE");
  assert.equal(first.payload.turn.runtime.unity.events[0].actorId, "PROTAGONIST_NUPJUKYI");
  assert.equal(first.payload.turn.runtime.gameplayResult.schemaVersion, "1.0");
  assert.equal(first.payload.turn.runtime.gameplayResult.actionType, "DELETE");
  assert.equal(first.payload.turn.runtime.gameplayResult.context, "COMBAT");
  assert.equal(first.payload.turn.runtime.gameplayResult.outcome, "STRONG_SUCCESS");
  assert.equal(first.payload.turn.runtime.gameplayResult.fx.scaleTier, "SCREEN");
  assert.equal(first.payload.turn.runtime.gameplayResult.fx.element, "ROCK_SPIKE");
  assert.equal(first.payload.turn.runtime.gameplayResult.fx.effectId, "ELEMENTAL_ROCK_SPIKE");
  assert.equal(first.payload.turn.runtime.unity.events[0].payload.gameplayResult.rollId, first.payload.turn.runtime.resolution.roll.rollId);
  assert.equal(first.payload.turn.sceneDecision.decisionNo, 1);
  assert.equal(first.payload.run.version, 2);
  assert.equal(first.payload.run.currentTurn, 1);
  assert.equal(first.payload.run.world.layoutHash, run.world.layoutHash);
  assert.equal(first.payload.turn.actionContext, "COMBAT");
  const attackedTarget = first.payload.run.entities.find((entity) => entity.id === target.id);
  assert.equal(attackedTarget, undefined);
  assert.ok(first.payload.turn.events.some((event) => event.type === "health_changed" && event.entityId === target.id && event.delta === -5 && event.hp === 0));
  assert.ok(first.payload.turn.events.some((event) => event.type === "entity_removed" && event.entityId === target.id && event.reason === "combat_defeat"));
  assert.equal(first.payload.turn.runtime.gameplayResult.result.damage, 5);
  assert.equal(first.payload.turn.runtime.gameplayResult.result.destroyed, true);
  assert.equal(first.payload.run.experience, 3);
  assert.equal(first.payload.run.gold, 7);
  assert.equal(first.payload.run.abilityUsageHistory.length, 1);
  assert.equal(first.payload.run.directorState.decisionNo, 1);

  const replay = await jsonRequest(baseUrl, `/v1/runs/${run.id}/choices`, { method: "POST", body: choiceRequest });
  assert.equal(replay.response.status, 200);
  assert.equal(replay.payload.fromIdempotencyCache, true);
  assert.equal(replay.payload.run.currentTurn, 1);
  assert.equal(replay.payload.turn.id, first.payload.turn.id);

  const conflict = await jsonRequest(baseUrl, `/v1/runs/${run.id}/choices`, {
    method: "POST",
    body: { ...choiceRequest, choiceId: "opening.listen" }
  });
  assert.equal(conflict.response.status, 409);
  assert.equal(conflict.payload.error.code, "IDEMPOTENCY_CONFLICT");

  const stale = await jsonRequest(baseUrl, `/v1/runs/${run.id}/choices`, {
    method: "POST",
    body: { ...choiceRequest, idempotencyKey: "turn-0002" }
  });
  assert.equal(stale.response.status, 409);
  assert.equal(stale.payload.error.code, "RUN_VERSION_CONFLICT");
  assert.equal(stale.payload.error.details.currentVersion, 2);

  const fetched = await jsonRequest(baseUrl, `/v1/runs/${run.id}/turns/1`);
  assert.equal(fetched.response.status, 200);
  assert.equal(fetched.payload.turn.narrative.model, "fake-narrator");
});

test("Root targetless skills reject out-of-range recipe targets without consuming state and DELETE binds threat at the boundary", async (t) => {
  const { application, baseUrl } = await startServer({ d20Source: new FixedD20Source(20) });
  t.after(() => application.close());
  const campaign = (await jsonRequest(baseUrl, "/v1/campaigns", {
    method: "POST",
    body: { title: "Root target orchestration", worldSeed: 7403, turnLimit: 40 }
  })).payload.campaign;
  const created = (await jsonRequest(baseUrl, `/v1/campaigns/${campaign.id}/runs`, {
    method: "POST",
    body: {}
  })).payload.run;
  const stored = application.store.runs.get(created.id);
  const player = stored.entities.find((entity) => entity.id === stored.playerEntityId);
  const components = Object.fromEntries(stored.entities
    .filter((entity) => entity.state?.finaleComponent)
    .map((entity) => [entity.state.finaleComponent, entity]));
  const staging = stored.world.validation.finaleInteractionAnchor;
  assert.ok(player && components.anchor && components.safeguard && components.threat && staging);
  player.position = { ...staging };
  components.anchor.position = { ...player.position };
  components.safeguard.position = { x: player.position.x + 6, y: player.position.y };
  components.threat.position = { x: player.position.x + 4, y: player.position.y };
  stored.adminAccessAcquisitionHistory = stored.adminAccessLevels.map((access, index) => ({
    accessLevelId: access.id,
    turnNo: index + 1
  }));
  stored.progressLevel = 3;
  stored.progressTokens = stored.adminAccessLevels.map((access) => access.id);
  stored.canonicalFacts.find((fact) => fact.subject === "collapse_origin"
    && fact.predicate === "inside_admin_control_system").value = true;
  stored.pendingChoiceSet = null;
  stored.focus = 10;
  application.store.runs.set(stored.id, stored);
  const before = await application.store.getRun(USER_ID, stored.id);

  const rejectedConnect = await jsonRequest(baseUrl, `/v1/runs/${stored.id}/actions`, {
    method: "POST",
    body: {
      inputType: "USE_SKILL",
      idempotencyKey: "root-connect-out-of-range-1",
      expectedRunVersion: stored.version,
      skillId: "CONNECT",
      targetIds: []
    }
  });
  assert.equal(rejectedConnect.response.status, 422);
  assert.equal(rejectedConnect.payload.error.code, "OUT_OF_RANGE");
  assert.match(rejectedConnect.payload.error.message, /connection targets must be within 5 tiles/i);
  assert.deepEqual(await application.store.getRun(USER_ID, stored.id), before);

  const rejectedDelete = await jsonRequest(baseUrl, `/v1/runs/${stored.id}/actions`, {
    method: "POST",
    body: {
      inputType: "USE_SKILL",
      idempotencyKey: "root-delete-out-of-range-1",
      expectedRunVersion: stored.version,
      skillId: "DELETE",
      targetIds: []
    }
  });
  assert.equal(rejectedDelete.response.status, 422);
  assert.equal(rejectedDelete.payload.error.code, "OUT_OF_RANGE");
  assert.match(rejectedDelete.payload.error.message, /Delete target must be within 3 tiles/i);
  const afterRejections = await application.store.getRun(USER_ID, stored.id);
  assert.deepEqual(afterRejections, before);
  assert.equal(afterRejections.version, created.version);
  assert.equal(afterRejections.currentTurn, created.currentTurn);
  assert.equal(afterRejections.focus, 10);
  assert.deepEqual(await application.store.listTurns(USER_ID, stored.id), []);

  const boundary = application.store.runs.get(stored.id);
  const boundaryPlayer = boundary.entities.find((entity) => entity.id === boundary.playerEntityId);
  const boundaryThreat = boundary.entities.find((entity) => entity.state?.finaleComponent === "threat");
  boundaryThreat.position = { x: boundaryPlayer.position.x + 3, y: boundaryPlayer.position.y };
  application.store.runs.set(boundary.id, boundary);
  const acceptedDelete = await jsonRequest(baseUrl, `/v1/runs/${stored.id}/actions`, {
    method: "POST",
    body: {
      inputType: "USE_SKILL",
      idempotencyKey: "root-delete-boundary-1",
      expectedRunVersion: stored.version,
      skillId: "DELETE",
      targetIds: []
    }
  });
  assert.equal(acceptedDelete.response.status, 201, JSON.stringify(acceptedDelete.payload));
  assert.equal(acceptedDelete.payload.turn.request.targetEntityId, boundaryThreat.id);
  assert.deepEqual(acceptedDelete.payload.turn.runtime.unity.events[0].targetIds, [boundaryThreat.id]);
  assert.deepEqual(acceptedDelete.payload.run.abilityUsageHistory.at(-1).targetIds, [boundaryThreat.id]);
  assert.ok(acceptedDelete.payload.turn.events.some((event) => event.type === "entity_removed"
    && event.entityId === boundaryThreat.id));
});

test("a submitted prepared D20 must match the server roll for the current version", async (t) => {
  const { application, baseUrl } = await startServer({ d20Source: new FixedD20Source(12) });
  t.after(() => application.close());
  const campaign = (await jsonRequest(baseUrl, "/v1/campaigns", {
    method: "POST", body: { title: "Authoritative dice", worldSeed: 4132, turnLimit: 40 }
  })).payload.campaign;
  const run = (await jsonRequest(baseUrl, `/v1/campaigns/${campaign.id}/runs`, { method: "POST", body: {} })).payload.run;
  const skillChoice = run.pendingChoiceSet.choices.find((choice) => choice.choiceKind === "SKILL");
  assert.ok(skillChoice);

  const prepared = await jsonRequest(baseUrl, `/v1/runs/${run.id}/dice`, {
    method: "POST", body: { expectedRunVersion: run.version }
  });
  assert.equal(prepared.response.status, 200);
  assert.equal(prepared.payload.d20, 12);

  const wrong = await jsonRequest(baseUrl, `/v1/runs/${run.id}/choices`, {
    method: "POST",
    body: {
      choiceSetId: run.pendingChoiceSet.choiceSetId,
      choiceId: skillChoice.choiceId,
      idempotencyKey: "d20-mismatch-0001",
      expectedRunVersion: run.version,
      preparedD20: 20
    }
  });
  assert.equal(wrong.response.status, 409);
  assert.equal(wrong.payload.error.code, "D20_MISMATCH");
  assert.equal((await jsonRequest(baseUrl, `/v1/runs/${run.id}`)).payload.run.version, run.version);

  const accepted = await jsonRequest(baseUrl, `/v1/runs/${run.id}/choices`, {
    method: "POST",
    body: {
      choiceSetId: run.pendingChoiceSet.choiceSetId,
      choiceId: skillChoice.choiceId,
      idempotencyKey: "d20-authority-0001",
      expectedRunVersion: run.version,
      preparedD20: prepared.payload.d20
    }
  });
  assert.equal(accepted.response.status, 201, JSON.stringify(accepted.payload));
  assert.equal(accepted.payload.turn.d20, prepared.payload.d20);
});

test("sealed dialogue choices do not prepare, expose, or apply D20 and survive ambient wander version changes", async (t) => {
  let preparedRolls = 0;
  const { application, baseUrl } = await startServer({
    d20Source: { roll() { preparedRolls += 1; return 20; } }
  });
  t.after(() => application.close());
  const campaign = (await jsonRequest(baseUrl, "/v1/campaigns", {
    method: "POST", body: { title: "Dialogue choices", worldSeed: 777, turnLimit: 40 }
  })).payload.campaign;
  const run = (await jsonRequest(baseUrl, `/v1/campaigns/${campaign.id}/runs`, { method: "POST", body: {} })).payload.run;
  const stored = application.store.runs.get(run.id);
  const player = stored.entities.find((entity) => entity.id === stored.playerEntityId);
  const npc = stored.entities.find((entity) => entity.kind === "npc" && entity.active);
  npc.position = { x: player.position.x + 1, y: player.position.y };
  application.store.runs.set(run.id, stored);

  const wandered = await jsonRequest(baseUrl, `/v1/runs/${run.id}/ambient-wander`, {
    method: "POST",
    body: { expectedRunVersion: 1, minX: 0, minY: 0, maxX: 159, maxY: 159 }
  });
  assert.equal(wandered.response.status, 200);
  assert.equal(wandered.payload.run.version, 2);
  assert.equal(wandered.payload.run.pendingChoiceSet.choiceSetId, run.pendingChoiceSet.choiceSetId);

  const request = {
    choiceSetId: run.pendingChoiceSet.choiceSetId,
    choiceId: "opening.listen",
    idempotencyKey: "dialogue-choice-0001",
    expectedRunVersion: 2
  };
  const selected = await jsonRequest(baseUrl, `/v1/runs/${run.id}/choices`, { method: "POST", body: request });
  assert.equal(selected.response.status, 201, JSON.stringify(selected.payload));
  assert.equal(selected.payload.turn.resolutionMode, "NONE");
  assert.equal(selected.payload.turn.d20, null);
  assert.equal(selected.payload.turn.dice, null);
  assert.equal(preparedRolls, 0);
  assert.equal(selected.payload.turn.outcome, "narrative");
  assert.equal(selected.payload.turn.runtime.resolution.required, false);
  assert.equal(selected.payload.turn.runtime.resolution.roll, null);
  assert.equal(selected.payload.turn.runtime.unity.renderRequired, false);
  assert.deepEqual(selected.payload.turn.runtime.unity.events, []);
  assert.equal(selected.payload.run.currentTurn, 1);
  assert.equal(selected.payload.run.version, 3);
  assert.equal(selected.payload.run.choiceHistory.length, 1);
  assert.equal(selected.payload.run.choiceHistory[0].text, run.pendingChoiceSet.choices[0].text);
  assert.ok(selected.payload.run.npcMemories.some((memory) => memory.sourceChoiceId === selected.payload.run.choiceHistory[0].id));
  assert.ok(selected.payload.run.npcRelationships.find((relationship) => relationship.npcId === npc.id).trust >= 1);
  assert.notEqual(selected.payload.run.pendingChoiceSet.choiceSetId, run.pendingChoiceSet.choiceSetId);
  assert.equal(selected.payload.turn.narrative.nextIntervention.choiceSetId, selected.payload.run.pendingChoiceSet.choiceSetId);
  assert.ok(selected.payload.turn.narrative.nextIntervention.choices.length >= 2);

  const replay = await jsonRequest(baseUrl, `/v1/runs/${run.id}/choices`, { method: "POST", body: request });
  assert.equal(replay.response.status, 200);
  assert.equal(replay.payload.fromIdempotencyCache, true);
  assert.equal(replay.payload.turn.id, selected.payload.turn.id);
  assert.equal(preparedRolls, 0);

  const stale = await jsonRequest(baseUrl, `/v1/runs/${run.id}/choices`, {
    method: "POST",
    body: { ...request, idempotencyKey: "dialogue-choice-0002", expectedRunVersion: 3 }
  });
  assert.equal(stale.response.status, 409);
  assert.equal(stale.payload.error.code, "CHOICE_SET_STALE");
});

test("a freeform item search uses SEARCH resolution and commits the discovered inventory item", async (t) => {
  const { application, baseUrl } = await startServer();
  t.after(() => application.close());
  const campaign = (await jsonRequest(baseUrl, "/v1/campaigns", {
    method: "POST", body: { title: "Natural search", worldSeed: 1701, turnLimit: 40 }
  })).payload.campaign;
  const run = (await jsonRequest(baseUrl, `/v1/campaigns/${campaign.id}/runs`, { method: "POST", body: {} })).payload.run;
  const result = await jsonRequest(baseUrl, `/v1/runs/${run.id}/messages`, {
    method: "POST",
    body: {
      text: "주변을 둘러보다 쓸 만한 아이템을 찾아본다",
      idempotencyKey: "freeform-search-item-0001",
      expectedRunVersion: run.version
    }
  });
  assert.equal(result.response.status, 201, JSON.stringify(result.payload));
  assert.equal(result.payload.turn.request.skillId, "SEARCH");
  assert.equal(result.payload.turn.runtime.resolution.required, true);
  assert.equal(result.payload.turn.runtime.unity.events[0].type, "SEARCH");
  assert.equal(result.payload.turn.runtime.gameplayResult.actionType, "SEARCH");
  assert.equal(result.payload.turn.runtime.gameplayResult.result.newInformation, true);
  assert.ok(result.payload.turn.runtime.resolution.inventoryChanges.some((event) => event.type === "inventory_item_acquired"));
  assert.ok(result.payload.run.inventory.some((item) => item.source === "search_discovery"));
});

test("a structured action proposal, not an input keyword, commits the exact narrated acquisition candidate", async (t) => {
  const { application, baseUrl } = await startServer({ narrator: new StructuredActionNarrator(), d20Source: new FixedD20Source(20) });
  t.after(() => application.close());
  const campaign = (await jsonRequest(baseUrl, "/v1/campaigns", {
    method: "POST", body: { title: "Context acquisition", worldSeed: 1702, turnLimit: 40 }
  })).payload.campaign;
  const run = (await jsonRequest(baseUrl, `/v1/campaigns/${campaign.id}/runs`, { method: "POST", body: {} })).payload.run;
  const result = await jsonRequest(baseUrl, `/v1/runs/${run.id}/messages`, {
    method: "POST",
    body: {
      text: "불안정한 균열 안으로 손을 뻗어 닿은 것을 안전하게 확보하려 한다",
      idempotencyKey: "context-acquire-0001",
      expectedRunVersion: run.version
    }
  });
  assert.equal(result.response.status, 201, JSON.stringify(result.payload));
  assert.equal(result.payload.turn.request.actionProposal.kind, "ACQUIRE");
  assert.equal(result.payload.turn.request.actionProposal.source, "llm_structured_proposal");
  assert.ok(result.payload.run.inventory.some((item) => item.name === "마을의 등불 조각"));
  assert.ok(result.payload.turn.runtime.resolution.confirmedEffects.some((event) => event.type === "inventory_item_acquired" && event.itemName === "마을의 등불 조각"));
});

test("failed acquisition rejects false success prose and leaves inventory unchanged", async (t) => {
  const { application, baseUrl } = await startServer({ narrator: new FalseAcquisitionNarrator(), d20Source: new FixedD20Source(1) });
  t.after(() => application.close());
  const campaign = (await jsonRequest(baseUrl, "/v1/campaigns", {
    method: "POST", body: { title: "Failed acquisition", worldSeed: 1703, turnLimit: 40 }
  })).payload.campaign;
  const run = (await jsonRequest(baseUrl, `/v1/campaigns/${campaign.id}/runs`, { method: "POST", body: {} })).payload.run;
  const result = await jsonRequest(baseUrl, `/v1/runs/${run.id}/messages`, {
    method: "POST",
    body: { text: "균열 속에 손을 넣어 닿은 것을 꺼낸다", idempotencyKey: "failed-acquire-0001", expectedRunVersion: run.version }
  });
  assert.equal(result.response.status, 201, JSON.stringify(result.payload));
  assert.equal(result.payload.turn.outcome, "failure");
  assert.equal(result.payload.turn.narrative.fallbackUsed, true);
  assert.ok(!result.payload.turn.runtime.resolution.inventoryChanges.some((event) => event.type === "inventory_item_acquired"));
  assert.deepEqual(result.payload.run.inventory.map((item) => item.name), ["관리자 키보드"]);
  assert.doesNotMatch(result.payload.turn.narrative.body, /등불 조각|손에 잡혔다|꺼냈다/u);
});

test("a free-form one-step destination resolves as an authoritative D20 MOVE", async (t) => {
  const { application, baseUrl } = await startServer({ narrator: new MoveActionNarrator(), d20Source: new FixedD20Source(20) });
  t.after(() => application.close());
  const campaign = (await jsonRequest(baseUrl, "/v1/campaigns", {
    method: "POST", body: { title: "Natural move", worldSeed: 1704, turnLimit: 40 }
  })).payload.campaign;
  const run = (await jsonRequest(baseUrl, `/v1/campaigns/${campaign.id}/runs`, { method: "POST", body: {} })).payload.run;
  const result = await jsonRequest(baseUrl, `/v1/runs/${run.id}/messages`, {
    method: "POST",
    body: { text: "가까운 안전한 방향으로 한 걸음 이동한다", idempotencyKey: "freeform-move-0001", expectedRunVersion: run.version }
  });
  assert.equal(result.response.status, 201, JSON.stringify(result.payload));
  assert.equal(result.payload.turn.request.skillId, "MOVE");
  assert.equal(result.payload.turn.runtime.resolution.required, true);
  assert.ok(result.payload.turn.runtime.resolution.movementChanges.some((event) => event.type === "entity_moved"));
  assert.equal(result.payload.turn.runtime.unity.events[0].type, "MOVE");
});

test("a 1,000-character free-form action keeps the full intent and a bounded narration note", async (t) => {
  const narrator = new MoveActionNarrator();
  const { application, baseUrl } = await startServer({ narrator, d20Source: new FixedD20Source(20) });
  t.after(() => application.close());
  const campaign = (await jsonRequest(baseUrl, "/v1/campaigns", {
    method: "POST", body: { title: "Long Natural Action", worldSeed: 17041, turnLimit: 40 }
  })).payload.campaign;
  const run = (await jsonRequest(baseUrl, `/v1/campaigns/${campaign.id}/runs`, { method: "POST", body: {} })).payload.run;
  const suffix = "마지막으로 가까운 안전한 방향으로 한 걸음 이동한다";
  const text = `${"가".repeat(1000 - suffix.length)}${suffix}`;
  assert.equal(text.length, 1000);
  const result = await jsonRequest(baseUrl, `/v1/runs/${run.id}/messages`, {
    method: "POST",
    body: { text, idempotencyKey: "freeform-long-move-0001", expectedRunVersion: run.version }
  });
  assert.equal(result.response.status, 201, JSON.stringify(result.payload));
  assert.equal(result.payload.turn.request.intent, text);
  assert.equal(result.payload.turn.request.narrativeChoice.text, text);
  assert.ok(result.payload.turn.request.playerNote.length <= 400);
  assert.match(result.payload.turn.request.playerNote, /한 걸음 이동한다$/u);
  assert.equal(result.payload.turn.request.skillId, "MOVE");
});

test("seed 20260718 seals a mismatched DELETE choice onto the nearby active encounter enemy", async (t) => {
  const narrator = new MismatchedEncounterChoiceNarrator();
  const { application, baseUrl } = await startServer({ narrator, d20Source: new FixedD20Source(20) });
  t.after(() => application.close());
  const campaign = (await jsonRequest(baseUrl, "/v1/campaigns", {
    method: "POST", body: { title: "CUA encounter target regression", worldSeed: 20260718, turnLimit: 40 }
  })).payload.campaign;
  const created = await jsonRequest(baseUrl, `/v1/campaigns/${campaign.id}/runs`, { method: "POST", body: {} });
  const run = created.payload.run;
  const suffix = "몬스터를 찾아 주변을 수색하고 적과 조우한다.";
  const text = `${"가".repeat(133 - suffix.length)}${suffix}`;
  assert.equal(text.length, 133);

  const encountered = await jsonRequest(baseUrl, `/v1/runs/${run.id}/messages`, {
    method: "POST",
    body: { text, idempotencyKey: "cua-target-repro-0001", expectedRunVersion: run.version }
  });
  assert.equal(encountered.response.status, 201, JSON.stringify(encountered.payload));
  const encounter = encountered.payload.run.activeEncounter;
  const encounterEntityId = encounter.sourceEntityId || encounter.entityId;
  const enemy = encountered.payload.run.entities.find((entity) => entity.id === encounterEntityId);
  const comment = encountered.payload.run.entities.find((entity) => entity.name === "코멘트");
  const currentPlayer = encountered.payload.run.entities.find((entity) => entity.id === encountered.payload.run.playerEntityId);
  assert(enemy && enemy.kind === "enemy" && enemy.capabilities.canDelete);
  assert(comment && !comment.capabilities.canDelete);
  assert.equal(encounter.sourceEntityId, enemy.id);
  assert.ok(Math.abs(currentPlayer.position.x - enemy.position.x) + Math.abs(currentPlayer.position.y - enemy.position.y) <= 3);
  assert.equal(encountered.payload.turn.selectedChoice.targetEntityId, enemy.id);
  assert.equal(encountered.payload.run.npcMemories.some((memory) => memory.npcId === comment.id && memory.createdTurn === 1), false);

  const contextEnemy = narrator.contexts[0].visibleEntities.find((entity) => entity.id === enemy.id);
  assert.equal(contextEnemy.activeEncounterTarget, true);
  assert.equal(contextEnemy.capabilities.canDelete, true);
  assert.equal(narrator.contexts[0].spatialContext.activeEncounter.sourceEntityId, enemy.id);

  const stored = application.store.runs.get(run.id);
  // This seed may classify the forced encounter as a Root Process, for which
  // the first legal response is SEARCH. Reveal it explicitly so this fixture
  // isolates the separate contract it was written for: repairing a persisted
  // DELETE choice whose target UUID was accidentally bound to the nearby NPC.
  stored.entities.find((entity) => entity.id === enemy.id).state.revealed = true;
  const corruptedDeleteChoice = stored.pendingChoiceSet.choices.find((choice) => choice.choiceId === "choice.attack.delete");
  corruptedDeleteChoice.skillId = "DELETE";
  corruptedDeleteChoice.intentTag = "ASSERTIVE";
  corruptedDeleteChoice.targetEntityId = comment.id;
  application.store.runs.set(run.id, stored);
  const recovered = await jsonRequest(baseUrl, `/v1/runs/${run.id}`);
  assert.equal(recovered.response.status, 200);
  const deleteChoice = recovered.payload.run.pendingChoiceSet.choices.find((choice) => choice.choiceId === "choice.attack.delete");
  const dialogueChoice = recovered.payload.run.pendingChoiceSet.choices.find((choice) => choice.choiceId === "choice.dialogue.ask");
  assert.equal(deleteChoice.skillId, "DELETE");
  assert.equal(deleteChoice.targetEntityId, enemy.id);
  assert.match(deleteChoice.text, new RegExp(enemy.name));
  assert.equal(dialogueChoice.targetEntityId, comment.id);

  const selected = await jsonRequest(baseUrl, `/v1/runs/${run.id}/choices`, {
    method: "POST",
    body: {
      choiceSetId: encountered.payload.run.pendingChoiceSet.choiceSetId,
      choiceId: deleteChoice.choiceId,
      idempotencyKey: "cua-target-delete-0001",
      expectedRunVersion: recovered.payload.run.version
    }
  });
  assert.equal(selected.response.status, 201, JSON.stringify(selected.payload));
  assert.equal(selected.payload.turn.request.targetEntityId, enemy.id);
  assert.ok(selected.payload.turn.events.some((event) => event.type === "health_changed" && event.entityId === enemy.id));
});

test("one HTTP turn shares an overall LLM deadline and remains playable through fallback", async (t) => {
  const { application, baseUrl } = await startServer();
  t.after(() => application.close());
  const campaign = (await jsonRequest(baseUrl, "/v1/campaigns", {
    method: "POST", body: { title: "Deadline Recovery", worldSeed: 17042, turnLimit: 40 }
  })).payload.campaign;
  const run = (await jsonRequest(baseUrl, `/v1/campaigns/${campaign.id}/runs`, { method: "POST", body: {} })).payload.run;
  let calls = 0;
  application.service.narrator = new GeminiNarrator({
    apiKey: "deadline-test-key",
    timeoutMs: 1000,
    circuitCooldownMs: 1000,
    logger: silentLogger,
    fetchImpl: async (_url, options) => {
      calls += 1;
      return new Promise((_resolve, reject) => options.signal.addEventListener("abort", () => reject(options.signal.reason), { once: true }));
    }
  });
  application.service.llmTurnDeadlineMs = 25;
  application.service.llmTurnMaxCalls = 6;
  const startedAt = Date.now();
  const first = await jsonRequest(baseUrl, `/v1/runs/${run.id}/messages`, {
    method: "POST",
    body: { text: "주변을 자세히 살펴본다", idempotencyKey: "deadline-recovery-0001", expectedRunVersion: run.version }
  });
  assert.equal(first.response.status, 201, JSON.stringify(first.payload));
  assert.equal(first.payload.turn.narrative.fallbackUsed, true);
  assert.equal(calls, 1);
  assert.ok(Date.now() - startedAt < 500);

  const second = await jsonRequest(baseUrl, `/v1/runs/${run.id}/messages`, {
    method: "POST",
    body: { text: "잠시 기다리며 상황을 지켜본다", idempotencyKey: "deadline-recovery-0002", expectedRunVersion: first.payload.run.version }
  });
  assert.equal(second.response.status, 201, JSON.stringify(second.payload));
  assert.equal(calls, 1, "the open circuit must make the next turn recover without another wait");
});

test("an impossible protected-item combination returns narrated failure instead of HTTP 422", async (t) => {
  const narrator = new ProtectedCombinationNarrator();
  const { application, baseUrl } = await startServer({ narrator, d20Source: new FixedD20Source(20) });
  t.after(() => application.close());
  const campaign = (await jsonRequest(baseUrl, "/v1/campaigns", {
    method: "POST", body: { title: "Narrated rejection", worldSeed: 1705, turnLimit: 40 }
  })).payload.campaign;
  let run = (await jsonRequest(baseUrl, `/v1/campaigns/${campaign.id}/runs`, { method: "POST", body: {} })).payload.run;
  const acquired = await jsonRequest(baseUrl, `/v1/runs/${run.id}/messages`, {
    method: "POST",
    body: { text: "땅의 빛나는 파편을 줍는다", idempotencyKey: "protected-combine-acquire", expectedRunVersion: run.version }
  });
  assert.equal(acquired.response.status, 201, JSON.stringify(acquired.payload));
  run = acquired.payload.run;

  const combined = await jsonRequest(baseUrl, `/v1/runs/${run.id}/messages`, {
    method: "POST",
    body: { text: "관리자 키보드에 빛나는 파편을 조합한다", idempotencyKey: "protected-combine-attempt", expectedRunVersion: run.version }
  });
  assert.equal(combined.response.status, 201, JSON.stringify(combined.payload));
  assert.equal(combined.payload.turn.runtime.resolution.required, false);
  assert.equal(combined.payload.turn.runtime.unity.renderRequired, false);
  assert.ok(combined.payload.turn.events.some((event) => event.type === "player_action_rejected" && event.code === "INVENTORY_ITEM_PROTECTED"));
  assert.match(combined.payload.turn.narrative.body, /결합되지 않는다|소모할 수 없다/u);
  assert.deepEqual(combined.payload.run.inventory.map((item) => item.name), ["관리자 키보드", "빛나는 파편"]);
  assert.ok(!combined.payload.turn.events.some((event) => event.type === "relationship_changed" || event.type === "npc_memory_added"));
  assert.ok(narrator.contexts.at(-1).confirmedEffects.some((event) => event.type === "player_action_rejected"));
});

test("committed LLM scene text remains in the bounded ledger supplied to the following turn", async (t) => {
  const narrator = new ContinuityNarrator();
  const { application, baseUrl } = await startServer({
    narrator,
    d20Source: new FixedD20Source(20)
  });
  t.after(() => application.close());
  const campaign = (await jsonRequest(baseUrl, "/v1/campaigns", {
    method: "POST", body: { title: "Narrative continuity", worldSeed: 1777, turnLimit: 40 }
  })).payload.campaign;
  const run = (await jsonRequest(baseUrl, `/v1/campaigns/${campaign.id}/runs`, { method: "POST", body: {} })).payload.run;

  const first = await jsonRequest(baseUrl, `/v1/runs/${run.id}/choices`, {
    method: "POST",
    body: {
      choiceSetId: run.pendingChoiceSet.choiceSetId,
      choiceId: "opening.listen",
      idempotencyKey: "continuity-choice-0001",
      expectedRunVersion: run.version
    }
  });
  assert.equal(first.response.status, 201, JSON.stringify(first.payload));
  const firstLedger = first.payload.run.storyLedger.find((entry) => entry.turnNo === 1);
  assert.match(firstLedger.narrativeDigest, /연속성 요약 표식 1/);
  assert.match(firstLedger.narrativeDigest, /연속성 본문 표식 1/);
  assert.ok(firstLedger.narrativeFragments.some((fragment) => fragment.includes("청록 종소리 장면 표식 1")));
  assert.ok(firstLedger.narrativeDigest.length <= 900);
  assert.ok(firstLedger.narrativeFragments.length <= 8);
  assert.ok(firstLedger.narrativeFragments.every((fragment) => fragment.length <= 320));

  const second = await jsonRequest(baseUrl, `/v1/runs/${run.id}/choices`, {
    method: "POST",
    body: {
      choiceSetId: first.payload.run.pendingChoiceSet.choiceSetId,
      choiceId: "continuity.listen",
      idempotencyKey: "continuity-choice-0002",
      expectedRunVersion: first.payload.run.version
    }
  });
  assert.equal(second.response.status, 201, JSON.stringify(second.payload));
  assert.equal(narrator.contexts.length, 2);
  const suppliedPriorLedger = narrator.contexts[1].storyLedger.find((entry) => entry.turnNo === 1);
  assert.match(suppliedPriorLedger.narrativeDigest, /연속성 본문 표식 1/);
  assert.ok(suppliedPriorLedger.narrativeFragments.some((fragment) => fragment.includes("청록 종소리 장면 표식 1")));
});

test("safe travel bypasses a slow remote planner and commits one deterministic butterfly-effect scene without consuming a campaign turn", async (t) => {
  const narrator = new SlowSceneNarrator();
  const { application, baseUrl } = await startServer({ narrator });
  t.after(() => application.close());
  const campaign = (await jsonRequest(baseUrl, "/v1/campaigns", {
    method: "POST",
    body: { title: "Travel scene", worldSeed: 24680, turnLimit: 30 }
  })).payload.campaign;
  const run = (await jsonRequest(baseUrl, `/v1/campaigns/${campaign.id}/runs`, { method: "POST", body: {} })).payload.run;
  const player = run.entities.find((entity) => entity.id === run.playerEntityId);
  const occupied = new Set(run.entities.filter((entity) => entity.id !== player.id && entity.blocking)
    .map((entity) => `${entity.position.x},${entity.position.y}`));
  const destination = [
    { x: player.position.x + 1, y: player.position.y },
    { x: player.position.x, y: player.position.y + 1 },
    { x: player.position.x - 1, y: player.position.y },
    { x: player.position.x, y: player.position.y - 1 }
  ].find((point) => !occupied.has(`${point.x},${point.y}`) &&
    point.x >= 0 && point.y >= 0 && point.x < run.world.width && point.y < run.world.height &&
    !["wall", "water"].includes(run.world.tileLegend[decodeTiles(run.world)[point.y * run.world.width + point.x]]));
  assert.ok(destination);
  const request = {
    inputType: "MOVE",
    idempotencyKey: "travel-scene-0001",
    expectedRunVersion: run.version,
    destination
  };
  const first = await jsonRequest(baseUrl, `/v1/runs/${run.id}/travel`, { method: "POST", body: request });
  assert.equal(first.response.status, 201);
  assert.equal(first.payload.run.pendingChoiceSet, null);
  assert.ok(first.payload.run.choiceHistory.some((item) => item.type === "NARRATIVE_CHOICE_SKIPPED"));
  assert.equal(first.payload.navigation.campaignTurnConsumed, false);
  assert.equal(first.payload.run.currentTurn, 0);
  assert.equal(first.payload.navigation.campaignTurnBefore, 0);
  assert.equal(first.payload.navigation.campaignTurnAfter, 0);
  assert.equal(first.payload.run.version, 2);
  assert.equal(first.payload.navigation.sceneDecision.decisionNo, 1);
  assert.ok(first.payload.navigation.sceneSequence.length >= 1);
  assert.equal(first.payload.navigation.narrative.summary, first.payload.navigation.sceneDecision.sceneGoal);
  assert.equal(first.payload.navigation.narrative.fallbackUsed, true);
  assert.match(first.payload.navigation.narrative.model, /^deterministic-scene-director-/);
  assert.equal(first.payload.run.directorState.decisionNo, 1);
  assert.equal(first.payload.run.world.layoutHash, run.world.layoutHash);
  assert.equal(narrator.planSceneCalls, 0, "non-consuming safe travel must never invoke the remote scene planner");

  const replay = await jsonRequest(baseUrl, `/v1/runs/${run.id}/travel`, { method: "POST", body: request });
  assert.equal(replay.response.status, 200);
  assert.equal(replay.payload.fromIdempotencyCache, true);
  assert.equal(replay.payload.navigation.id, first.payload.navigation.id);
  assert.deepEqual(replay.payload.navigation.narrative, first.payload.navigation.narrative);
  assert.equal(replay.payload.run.directorState.decisionNo, 1);
  assert.equal(narrator.planSceneCalls, 0, "an idempotent replay must not invoke the remote scene planner");
});

test("free-form input remains valid after safe travel dismisses the optional choice set", async (t) => {
  const { application, baseUrl } = await startServer();
  t.after(() => application.close());
  const campaign = (await jsonRequest(baseUrl, "/v1/campaigns", {
    method: "POST",
    body: { title: "Travel then free-form", worldSeed: 24680, turnLimit: 30 }
  })).payload.campaign;
  const run = (await jsonRequest(baseUrl, `/v1/campaigns/${campaign.id}/runs`, { method: "POST", body: {} })).payload.run;
  const player = run.entities.find((entity) => entity.id === run.playerEntityId);
  const occupied = new Set(run.entities.filter((entity) => entity.id !== player.id && entity.blocking)
    .map((entity) => `${entity.position.x},${entity.position.y}`));
  const tiles = decodeTiles(run.world);
  const destination = [
    { x: player.position.x + 1, y: player.position.y },
    { x: player.position.x, y: player.position.y + 1 },
    { x: player.position.x - 1, y: player.position.y },
    { x: player.position.x, y: player.position.y - 1 }
  ].find((point) => !occupied.has(`${point.x},${point.y}`) &&
    point.x >= 0 && point.y >= 0 && point.x < run.world.width && point.y < run.world.height &&
    !["wall", "water"].includes(run.world.tileLegend[tiles[point.y * run.world.width + point.x]]));
  assert.ok(destination);

  const moved = await jsonRequest(baseUrl, `/v1/runs/${run.id}/travel`, {
    method: "POST",
    body: {
      inputType: "MOVE",
      idempotencyKey: "travel-before-freeform-0001",
      expectedRunVersion: run.version,
      destination
    }
  });
  assert.equal(moved.response.status, 201, JSON.stringify(moved.payload));
  assert.equal(moved.payload.run.pendingChoiceSet, null);

  const message = await jsonRequest(baseUrl, `/v1/runs/${run.id}/messages`, {
    method: "POST",
    body: {
      text: "주변의 균열을 살피고 동료에게 지금까지 확인한 사실을 설명한다.",
      idempotencyKey: "freeform-after-travel-0001",
      expectedRunVersion: moved.payload.run.version
    }
  });
  assert.equal(message.response.status, 201, JSON.stringify(message.payload));
  assert.equal(message.payload.run.currentTurn, 1);
  assert.equal(message.payload.run.choiceHistory.at(-1).choiceId, "player.freeform");
  assert.notEqual(message.payload.run.pendingChoiceSet, null);
});

test("abandon and resume require the current run version", async (t) => {
  const { application, baseUrl } = await startServer();
  t.after(() => application.close());
  const campaign = (await jsonRequest(baseUrl, "/v1/campaigns", {
    method: "POST",
    body: { title: "Lifecycle", worldSeed: 17, turnLimit: 30 }
  })).payload.campaign;
  const run = (await jsonRequest(baseUrl, `/v1/campaigns/${campaign.id}/runs`, { method: "POST", body: {} })).payload.run;

  const abandoned = await jsonRequest(baseUrl, `/v1/runs/${run.id}/abandon`, {
    method: "POST",
    body: { expectedRunVersion: 1 }
  });
  assert.equal(abandoned.payload.run.status, "abandoned");
  assert.equal(abandoned.payload.run.version, 2);

  const staleResume = await jsonRequest(baseUrl, `/v1/runs/${run.id}/resume`, {
    method: "POST",
    body: { expectedRunVersion: 1 }
  });
  assert.equal(staleResume.response.status, 409);

  const resumed = await jsonRequest(baseUrl, `/v1/runs/${run.id}/resume`, {
    method: "POST",
    body: { expectedRunVersion: 2 }
  });
  assert.equal(resumed.payload.run.status, "active");
  assert.equal(resumed.payload.run.version, 3);
});

test("localhost CORS is allowed and non-local browser origins are rejected", async (t) => {
  const { application, baseUrl } = await startServer();
  t.after(() => application.close());
  const local = await fetch(`${baseUrl}/health`, { method: "OPTIONS", headers: { origin: "http://localhost:5173" } });
  assert.equal(local.status, 204);
  assert.equal(local.headers.get("access-control-allow-origin"), "http://localhost:5173");

  const remote = await fetch(`${baseUrl}/health`, { headers: { origin: "https://example.com" } });
  assert.equal(remote.status, 403);
});

test("HTTP guards close debug and public development tools by default", async (t) => {
  const { application, baseUrl } = await startServer();
  t.after(() => application.close());

  const debug = await jsonRequest(baseUrl, "/v1/runs/not-a-run/debug");
  assert.equal(debug.response.status, 404);
  assert.equal(debug.payload.error.code, "ROUTE_NOT_FOUND");

  const staticFile = await fetch(`${baseUrl}/public/test-client.html`);
  assert.equal(staticFile.status, 404);
  assert.equal((await staticFile.json()).error.code, "ROUTE_NOT_FOUND");

  const arrayBody = await fetch(`${baseUrl}/v1/campaigns`, {
    method: "POST",
    headers: { "content-type": "application/json", "x-user-id": USER_ID },
    body: "[]"
  });
  assert.equal(arrayBody.status, 400);
  assert.equal((await arrayBody.json()).error.code, "JSON_INVALID");

  const oversized = await fetch(`${baseUrl}/v1/campaigns`, {
    method: "POST",
    headers: { "content-type": "application/json", "x-user-id": USER_ID },
    body: JSON.stringify({ title: "x".repeat(70 * 1024) })
  });
  assert.equal(oversized.status, 413);
  assert.equal((await oversized.json()).error.code, "BODY_TOO_LARGE");
});

test("public development tools require development opt-in and remain closed in production", async (t) => {
  const development = await startServer({
    configEnv: { NODE_ENV: "development", ENABLE_DEBUG_ROUTES: "true" }
  });
  t.after(() => development.application.close());

  for (const name of ["test-client.html", "narrative-lab.html"]) {
    const response = await fetch(`${development.baseUrl}/public/${name}`);
    assert.equal(response.status, 200);
    assert.match(response.headers.get("content-type"), /^text\/html/);
  }

  const production = await startServer({
    configEnv: { NODE_ENV: "production", ENABLE_DEBUG_ROUTES: "true" }
  });
  t.after(() => production.application.close());

  const publicPage = await fetch(`${production.baseUrl}/public/test-client.html`);
  assert.equal(publicPage.status, 404);
  assert.equal((await publicPage.json()).error.code, "ROUTE_NOT_FOUND");

  const debug = await jsonRequest(production.baseUrl, "/v1/runs/not-a-run/debug");
  assert.equal(debug.response.status, 404);
  assert.equal(debug.payload.error.code, "ROUTE_NOT_FOUND");
});

test("GM narration endpoint has no state access and rejects mechanical operations", async (t) => {
  const { application, baseUrl } = await startServer();
  t.after(() => application.close());
  const compact = {
    turnNo: 4,
    remainingTurns: 26,
    area: "버퍼 마을 외곽",
    intent: "Ask the memory warden about the sealed access log",
    ability: "interact",
    d20: 14,
    outcome: "success",
    normalizedAttempt: "Speak to the nearby witness",
    allowedEffects: ["npc_memory_hint", "quest_hint"],
    recentFacts: ["Root System remains sealed until all three administrator access levels and the essential clue are established."]
  };
  const narrated = await jsonRequest(baseUrl, "/v1/gm/narrate", { method: "POST", body: compact });
  assert.equal(narrated.response.status, 200);
  assert.deepEqual(Object.keys(narrated.payload).sort(), ["body", "dialogue", "fallbackUsed", "model", "proposedOps", "summary"]);

  const forbidden = await jsonRequest(baseUrl, "/v1/gm/narrate", {
    method: "POST",
    body: { ...compact, allowedEffects: ["delete_entity"] }
  });
  assert.equal(forbidden.response.status, 400);
  assert.equal(forbidden.payload.error.code, "NARRATION_EFFECT_FORBIDDEN");
});
