import test from "node:test";
import assert from "node:assert/strict";
import {
  compactDecisionSchema,
  compactPayloadForModel,
  createFallbackScenePlan,
  flattenPaths,
  restoreScenePlan,
  validateSceneTransitionRequest
} from "../src/llm/scene-transition.js";
import { VllmNarrator } from "../src/llm/vllm-director.js";

const silentLogger = { warn() {} };

// The worked example from the LLM communication reference (§16.1), in the server's camelCase.
function docExampleRequest() {
  return {
    protocolVersion: "1.0",
    schemaVersion: "1.0",
    requestType: "SCENE_TRANSITION_PLAN",
    requestId: "req-001-ab12cd34",
    run: { runId: "run-06", turnId: "turn-031", turnNo: 31, expectedRunVersion: 18 },
    context: {
      worldLayoutHash: "world-34140fe6",
      contextHash: "context-dffe69e0",
      currentAreaId: "AREA_OLD_STATION",
      playerIntent: "단서를 추적한다",
      storySummary: "플레이어는 끊긴 무전 신호의 발신자를 찾고 있다."
    },
    candidates: {
      destinations: [
        {
          destinationAreaId: "AREA_MOON_MARKET",
          routeIds: ["ROUTE_BRIDGE", "ROUTE_ALLEY"],
          entrySlotIds: ["ENTRY_GATE", "ENTRY_BELL"]
        }
      ],
      storyBeatIds: ["BEAT_TRACE_SIGNAL", "BEAT_FACE_RIVAL"],
      sceneTemplateIds: ["SCENE_EXPLORE", "SCENE_DISCOVERY"],
      transitionStyleIds: ["TRANSITION_FADE", "TRANSITION_WIPE"],
      bgmCueIds: ["BGM_SUSPENSE_LOW", "BGM_MYSTERY_RAIN"],
      sfxCueIds: ["SFX_RAIN", "SFX_RADIO", "SFX_FOOTSTEP"],
      revealIds: ["REVEAL_SIGIL", "REVEAL_FALSE_MAP"]
    }
  };
}

// The worked compact decision from §18.1.
function docExampleDecision() {
  return {
    p: 0,
    b: 1,
    t: 1,
    x: 0,
    m: 0,
    s: [0, 2],
    v: [0],
    g: "무전 신호를 추적한다.",
    f: "습격자가 접근한다.",
    n: "시장으로 이동해 신호를 다시 찾는다.",
    c: [
      { l: "신호 추적", i: "조사" },
      { l: "몸을 숨긴다", i: "회피" }
    ]
  };
}

test("the reference example request validates and flattens into compatible path triples", () => {
  const request = validateSceneTransitionRequest(docExampleRequest());
  const paths = flattenPaths(request);

  assert.equal(paths.length, 4);
  assert.deepEqual(paths[0], { destinationAreaId: "AREA_MOON_MARKET", routeId: "ROUTE_BRIDGE", entrySlotId: "ENTRY_GATE" });
  assert.deepEqual(paths[1], { destinationAreaId: "AREA_MOON_MARKET", routeId: "ROUTE_BRIDGE", entrySlotId: "ENTRY_BELL" });

  const payload = compactPayloadForModel(request, paths);
  assert.equal(payload.state.area, "AREA_OLD_STATION");
  assert.deepEqual(payload.paths[0], { destination: "AREA_MOON_MARKET", route: "ROUTE_BRIDGE", entry: "ENTRY_GATE" });
  assert.deepEqual(payload.beats, ["BEAT_TRACE_SIGNAL", "BEAT_FACE_RIVAL"]);
});

test("unknown fields, duplicate ids, and unsupported versions are rejected", () => {
  assert.throws(() => validateSceneTransitionRequest({ ...docExampleRequest(), rawText: "hack" }), /unknown fields/i);
  const duplicated = docExampleRequest();
  duplicated.candidates.bgmCueIds = ["BGM_A", "BGM_A"];
  assert.throws(() => validateSceneTransitionRequest(duplicated), /duplicates/);
  assert.throws(() => validateSceneTransitionRequest({ ...docExampleRequest(), protocolVersion: "2.0" }), /protocolVersion/);
});

test("the dynamic schema sizes every index enum to this request's candidates", () => {
  const request = validateSceneTransitionRequest(docExampleRequest());
  const schema = compactDecisionSchema(request, flattenPaths(request));

  assert.deepEqual(schema.properties.p.enum, [0, 1, 2, 3]);
  assert.deepEqual(schema.properties.b.enum, [0, 1]);
  assert.deepEqual(schema.properties.s.items.enum, [0, 1, 2]);
  assert.equal(schema.properties.s.maxItems, 2);
  assert.equal(schema.properties.v.maxItems, 1);
  assert.equal(schema.additionalProperties, false);
  assert.equal(schema.properties.c.minItems, 2);
  assert.equal(schema.properties.c.maxItems, 2);
});

test("empty sfx and reveal candidate lists become structurally empty selections", () => {
  const input = docExampleRequest();
  input.candidates.sfxCueIds = [];
  input.candidates.revealIds = [];
  const request = validateSceneTransitionRequest(input);
  const schema = compactDecisionSchema(request, flattenPaths(request));

  assert.equal(schema.properties.s.maxItems, 0);
  assert.equal(schema.properties.v.maxItems, 0);
});

test("restoring the reference decision reproduces the reference v1.0 plan", () => {
  const request = validateSceneTransitionRequest(docExampleRequest());
  const usage = { modelProfile: "compact-decision-v1", modelId: "game-director", inputTokens: 399, outputTokens: 134, latencyMs: 1534, finishReason: "stop" };
  const plan = restoreScenePlan(request, flattenPaths(request), docExampleDecision(), usage);

  // Matches the worked §19 response.
  assert.equal(plan.status, "OK");
  assert.equal(plan.requestId, "req-001-ab12cd34");
  assert.deepEqual(plan.selection, {
    destinationAreaId: "AREA_MOON_MARKET",
    routeId: "ROUTE_BRIDGE",
    entrySlotId: "ENTRY_GATE",
    storyBeatId: "BEAT_FACE_RIVAL",
    sceneTemplateId: "SCENE_DISCOVERY"
  });
  assert.equal(plan.transition.transitionStyleId, "TRANSITION_FADE");
  assert.equal(plan.transition.bgmCueId, "BGM_SUSPENSE_LOW");
  assert.deepEqual(plan.transition.sfxCueIds, ["SFX_RAIN", "SFX_FOOTSTEP"]);
  assert.equal(plan.transition.cameraCue, "CAMERA_FOLLOW");
  assert.equal(plan.transition.summary, "시장으로 이동해 신호를 다시 찾는다.");
  assert.equal(plan.transition.body, plan.transition.summary);
  assert.equal(plan.scenePlan.sceneGoal, "무전 신호를 추적한다.");
  assert.deepEqual(plan.scenePlan.revealIds, ["REVEAL_SIGIL"]);
  assert.deepEqual(plan.scenePlan.suggestedChoices.map((choice) => choice.choiceId), ["choice-1", "choice-2"]);
  assert.deepEqual(plan.echo, {
    runId: "run-06",
    turnId: "turn-031",
    turnNo: 31,
    expectedRunVersion: 18,
    worldLayoutHash: "world-34140fe6",
    contextHash: "context-dffe69e0"
  });
  assert.deepEqual(plan.proposedOps, []);
  assert.equal(plan.usage, usage);
});

test("out-of-range indices and oversized prose are rejected as 502 contract violations", () => {
  const request = validateSceneTransitionRequest(docExampleRequest());
  const paths = flattenPaths(request);
  const usage = { modelProfile: "x", modelId: "x", inputTokens: 0, outputTokens: 0, latencyMs: 0, finishReason: "stop" };

  assert.throws(() => restoreScenePlan(request, paths, { ...docExampleDecision(), p: 4 }, usage), /index is outside/);
  assert.throws(() => restoreScenePlan(request, paths, { ...docExampleDecision(), g: "가".repeat(81) }, usage), /length limit/);
  assert.throws(() => restoreScenePlan(request, paths, { ...docExampleDecision(), c: [{ l: "하나", i: "의도" }] }, usage), /exactly 2/);
});

test("the deterministic fallback picks first candidates and flags itself", () => {
  const plan = createFallbackScenePlan(docExampleRequest(), "test_reason");

  assert.equal(plan.fallbackUsed, true);
  assert.equal(plan.status, "OK");
  assert.equal(plan.selection.destinationAreaId, "AREA_MOON_MARKET");
  assert.equal(plan.selection.routeId, "ROUTE_BRIDGE");
  assert.equal(plan.selection.storyBeatId, "BEAT_TRACE_SIGNAL");
  assert.deepEqual(plan.transition.sfxCueIds, []);
  assert.equal(plan.scenePlan.suggestedChoices.length, 2);
  assert.equal(plan.usage.finishReason, "test_reason");
});

test("VllmNarrator.planSceneTransition sends the doc-shaped request and restores the plan", async () => {
  const requests = [];
  const narrator = new VllmNarrator({
    baseUrl: "http://127.0.0.1:8000/v1",
    apiKey: "unit-test-token",
    logger: silentLogger,
    fetchImpl: async (url, options) => {
      requests.push({ url, options });
      return {
        ok: true,
        async json() {
          return {
            usage: { prompt_tokens: 399, completion_tokens: 134 },
            choices: [{ finish_reason: "stop", message: { role: "assistant", content: JSON.stringify(docExampleDecision()) } }]
          };
        }
      };
    }
  });

  const plan = await narrator.planSceneTransition(docExampleRequest());

  assert.equal(plan.fallbackUsed, false);
  assert.equal(plan.selection.destinationAreaId, "AREA_MOON_MARKET");
  assert.equal(plan.usage.inputTokens, 399);
  assert.equal(plan.usage.finishReason, "stop");

  const body = JSON.parse(requests[0].options.body);
  assert.equal(body.temperature, 0.7);
  assert.equal(body.top_p, 0.9);
  assert.equal(body.max_tokens, 384);
  assert.equal(body.response_format.json_schema.name, "compact_game_decision_v1");
  assert.deepEqual(body.response_format.json_schema.schema.properties.p.enum, [0, 1, 2, 3]);
  const userPayload = JSON.parse(body.messages[1].content);
  assert.equal(userPayload.state.intent, "단서를 추적한다");
});

test("VllmNarrator.planSceneTransition retries once then returns the deterministic fallback", async () => {
  let calls = 0;
  const narrator = new VllmNarrator({
    baseUrl: "http://127.0.0.1:8000/v1",
    apiKey: "unit-test-token",
    logger: silentLogger,
    fetchImpl: async () => {
      calls += 1;
      return {
        ok: true,
        async json() {
          // p index outside the candidate range: schema-violating backend behaviour.
          return { choices: [{ finish_reason: "stop", message: { role: "assistant", content: JSON.stringify({ ...docExampleDecision(), p: 99 }) } }] };
        }
      };
    }
  });

  const plan = await narrator.planSceneTransition(docExampleRequest());
  assert.equal(calls, 2);
  assert.equal(plan.fallbackUsed, true);
  assert.equal(plan.selection.destinationAreaId, "AREA_MOON_MARKET");
});
