import test from "node:test";
import assert from "node:assert/strict";
import { createApplication } from "../src/app.js";
import { loadConfig } from "../src/config.js";
import { FixedD20Source } from "../src/domain/turn-engine.js";
import { MemoryStore } from "../src/store/memory-store.js";

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

async function startServer({ d20Source = new FixedD20Source(20), narrator = new FakeNarrator() } = {}) {
  const config = loadConfig({ AUTH_MODE: "required", STORAGE: "memory", LOG_LEVEL: "silent" });
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
  assert.equal(first.response.status, 201);
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
  assert.equal(attackedTarget.state.hp, 5);
  assert.notEqual(attackedTarget.state.disabled, true);
  assert.ok(first.payload.turn.events.some((event) => ["encounter_intervention", "ambient_fallback_applied"].includes(event.type)));
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

test("sealed dialogue choices prepare but do not expose or apply D20 and survive ambient wander version changes", async (t) => {
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
  assert.equal(preparedRolls, 1);
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
  assert.equal(preparedRolls, 1);

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

test("safe travel commits one butterfly-effect scene without consuming a campaign turn", async (t) => {
  const { application, baseUrl } = await startServer();
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
  const blocked = await jsonRequest(baseUrl, `/v1/runs/${run.id}/travel`, { method: "POST", body: request });
  assert.equal(blocked.response.status, 409);
  assert.equal(blocked.payload.error.code, "CHOICE_REQUIRED");
  assert.equal(blocked.payload.error.details.choiceSetId, run.pendingChoiceSet.choiceSetId);
  const unchanged = (await jsonRequest(baseUrl, `/v1/runs/${run.id}`)).payload.run;
  assert.equal(unchanged.version, run.version);
  assert.equal(unchanged.pendingChoiceSet.choiceSetId, run.pendingChoiceSet.choiceSetId);
  assert.deepEqual(unchanged.entities.find((entity) => entity.id === unchanged.playerEntityId).position, player.position);

  const legacyRun = application.store.runs.get(run.id);
  legacyRun.pendingChoiceSet = null;
  application.store.runs.set(run.id, legacyRun);
  const first = await jsonRequest(baseUrl, `/v1/runs/${run.id}/travel`, { method: "POST", body: request });
  assert.equal(first.response.status, 201);
  assert.equal(first.payload.navigation.campaignTurnConsumed, false);
  assert.equal(first.payload.run.currentTurn, 0);
  assert.equal(first.payload.navigation.campaignTurnBefore, 0);
  assert.equal(first.payload.navigation.campaignTurnAfter, 0);
  assert.equal(first.payload.run.version, 2);
  assert.equal(first.payload.navigation.sceneDecision.decisionNo, 1);
  assert.ok(first.payload.navigation.sceneSequence.length >= 1);
  assert.equal(first.payload.run.directorState.decisionNo, 1);
  assert.equal(first.payload.run.world.layoutHash, run.world.layoutHash);

  const replay = await jsonRequest(baseUrl, `/v1/runs/${run.id}/travel`, { method: "POST", body: request });
  assert.equal(replay.response.status, 200);
  assert.equal(replay.payload.fromIdempotencyCache, true);
  assert.equal(replay.payload.navigation.id, first.payload.navigation.id);
  assert.equal(replay.payload.run.directorState.decisionNo, 1);
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
