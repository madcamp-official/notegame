import test from "node:test";
import assert from "node:assert/strict";
import { DEFAULT_MODEL_PROFILES, GeminiNarrator } from "../src/llm/gemini-narrator.js";
import { FALLBACK_MODEL } from "../src/llm/narration.js";
import { createCampaignBlueprint } from "../src/domain/campaign.js";
import { createRunState, directorContext, FixedD20Source, normalizeTurnRequest, resolveTurn } from "../src/domain/turn-engine.js";
import { generateWorld } from "../src/domain/world.js";

const context = {
  turnNo: 3,
  remainingTurns: 27,
  area: "Rain Ruins",
  intent: "Inspect the broken lantern",
  ability: "interact",
  d20: 16,
  outcome: "success",
  normalizedAttempt: "Inspect the lantern",
  allowedEffects: ["ambient_cue", "fact_hint"],
  recentFacts: ["Rain has covered the western path."]
};

const silentLogger = { warn() {} };

function responseWith(value) {
  return {
    ok: true,
    async json() {
      return { candidates: [{ content: { parts: [{ text: JSON.stringify(value) }] } }] };
    }
  };
}

test("Gemini adapter retries one semantically invalid response and sends bounded structured config", async () => {
  const requests = [];
  const outputs = [
    { summary: "Unsafe", body: "Bad op", dialogue: null, proposedOps: [{ type: "delete_entity", text: "Delete it" }] },
    { summary: "깨진 등불 발견", body: "빗물이 깨진 등불의 유리 위로 흘러내린다.", dialogue: null, proposedOps: [{ type: "ambient_cue", text: "잔잔한 빗소리가 들린다." }] }
  ];
  const narrator = new GeminiNarrator({
    apiKey: "unit-test-token",
    logger: silentLogger,
    fetchImpl: async (_url, options) => {
      requests.push(options);
      return responseWith(outputs.shift());
    }
  });

  const result = await narrator.narrate(context);
  assert.equal(requests.length, 2);
  assert.equal(result.fallbackUsed, false);
  assert.equal(result.model, DEFAULT_MODEL_PROFILES.fast.model);
  assert.equal(result.proposedOps[0].type, "ambient_cue");
  const requestBody = JSON.parse(requests[0].body);
  assert.equal(requestBody.generationConfig.thinkingConfig.thinkingLevel, "minimal");
  assert.equal(requestBody.generationConfig.maxOutputTokens, 1024);
  assert.equal(requestBody.generationConfig.responseMimeType, "application/json");
  assert.equal(requestBody.generationConfig.responseJsonSchema.additionalProperties, false);
  assert.match(requestBody.systemInstruction.parts[0].text, /may be explicitly targeted/);
  assert.match(requestBody.systemInstruction.parts[0].text, /NUPJUK : The Last Commit/);
  assert.doesNotMatch(requestBody.systemInstruction.parts[0].text, /Ninja Adventure/);
  assert.deepEqual(requestBody.generationConfig.responseJsonSchema.properties.proposedOps.items.properties.type.enum,
    ["ambient_cue", "fact_hint"]);
  assert.equal(requests[0].headers["x-goog-api-key"], "unit-test-token");
  assert.equal(requests[0].body.includes("unit-test-token"), false);
});

test("Gemini adapter performs only one retry then returns deterministic fallback", async () => {
  let calls = 0;
  const narrator = new GeminiNarrator({
    apiKey: "unit-test-token",
    logger: silentLogger,
    fetchImpl: async () => {
      calls += 1;
      return responseWith({ summary: "", body: "", dialogue: null, proposedOps: [] });
    }
  });
  const result = await narrator.narrate(context);
  assert.equal(calls, 2);
  assert.equal(result.fallbackUsed, true);
  assert.equal(result.model, FALLBACK_MODEL);
  assert.ok(result.body.length > 0);
});

test("Gemini transport timeout falls back immediately and opens a bounded circuit cooldown", async () => {
  let calls = 0;
  let now = 1000;
  let mode = "timeout";
  const narrator = new GeminiNarrator({
    apiKey: "unit-test-token",
    timeoutMs: 5,
    circuitCooldownMs: 1000,
    clock: () => now,
    logger: silentLogger,
    fetchImpl: async (_url, options) => {
      calls += 1;
      if (mode === "success") {
        return responseWith({
          summary: "깨진 등불 발견",
          body: "빗물이 깨진 등불의 유리 위로 흘러내린다.",
          dialogue: null,
          proposedOps: [{ type: "ambient_cue", text: "잔잔한 빗소리가 들린다." }]
        });
      }
      await new Promise((resolve, reject) => {
        options.signal.addEventListener("abort", () => {
          const error = new Error("timed out");
          error.name = "AbortError";
          reject(error);
        }, { once: true });
      });
    }
  });

  const timedOut = await narrator.narrate(context);
  assert.equal(timedOut.fallbackUsed, true);
  assert.equal(calls, 1, "transport failures must not consume the semantic repair attempt");

  mode = "success";
  const duringCooldown = await narrator.narrate(context);
  assert.equal(duringCooldown.fallbackUsed, true);
  assert.equal(calls, 1, "subsequent planner/narrator calls must not wait behind an open circuit");

  now += 1000;
  const recovered = await narrator.narrate(context);
  assert.equal(recovered.fallbackUsed, false);
  assert.equal(calls, 2);
});

test("missing API key never performs a network request", async () => {
  let calls = 0;
  const narrator = new GeminiNarrator({
    apiKey: "",
    logger: silentLogger,
    fetchImpl: async () => {
      calls += 1;
      throw new Error("should not run");
    }
  });
  const result = await narrator.narrate(context);
  assert.equal(calls, 0);
  assert.equal(result.fallbackUsed, true);
  assert.match(result.body, /[가-힣]/);
});

test("a request-scoped player API key overrides the server key without entering the payload", async () => {
  const requests = [];
  const narrator = new GeminiNarrator({
    apiKey: "server-default-token",
    logger: silentLogger,
    fetchImpl: async (_url, options) => {
      requests.push(options);
      return responseWith({
        summary: "깨진 등불 발견",
        body: "빗물이 깨진 등불의 유리 위로 흘러내린다.",
        dialogue: null,
        proposedOps: [{ type: "ambient_cue", text: "잔잔한 빗소리가 들린다." }]
      });
    }
  });

  const result = await narrator.withApiKey("player-provided-token", () => narrator.narrate(context));

  assert.equal(result.fallbackUsed, false);
  assert.equal(requests[0].headers["x-goog-api-key"], "player-provided-token");
  assert.equal(requests[0].body.includes("player-provided-token"), false);
  assert.equal(narrator._activeApiKey(), "server-default-token");
});

test("an invalid rolled proposal is preserved as a narrated rejection instead of phantom success", async () => {
  const keyboardId = "11111111-1111-4111-8111-111111111111";
  const narrator = new GeminiNarrator({
    apiKey: "unit-test-token",
    logger: silentLogger,
    fetchImpl: async () => responseWith({
      kind: "COMBINE",
      targetEntityIds: [],
      itemIds: [keyboardId],
      destinationRef: null,
      resultItem: null,
      reason: "관리자 키보드에 존재하지 않는 파편을 조합하려는 시도다."
    })
  });
  const result = await narrator.planPlayerAction({
    playerText: "관리자 키보드에 방금 주운 빛나는 파편을 조합한다",
    currentArea: "동굴",
    recentNarrative: [],
    visibleEntities: [],
    inventory: [{ id: keyboardId, name: "관리자 키보드", kind: "key_item", protected: true }],
    destinations: [],
    allowedKinds: []
  });
  assert.equal(result.requiresRoll, false);
  assert.equal(result.rejectedAction.kind, "COMBINE");
  assert.match(result.rejectedAction.reason, /인벤토리/u);
});

test("English-only narration is rejected and falls back to Korean", async () => {
  let calls = 0;
  const narrator = new GeminiNarrator({
    apiKey: "unit-test-token",
    logger: silentLogger,
    fetchImpl: async () => {
      calls += 1;
      return responseWith({ summary: "Clue found", body: "The witness reveals a hidden record.", dialogue: null, proposedOps: [] });
    }
  });
  const result = await narrator.narrate(context);
  assert.equal(calls, 2);
  assert.equal(result.fallbackUsed, true);
  assert.match(result.summary, /[가-힣]/);
  assert.match(result.body, /[가-힣]/);
});

test("director narration requires an independent unchanged full-scene and choice review", async () => {
  const worldSeed = 9182;
  const blueprint = createCampaignBlueprint({ worldSeed, turnLimit: 40 });
  const run = createRunState({
    campaign: { ...blueprint, id: "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa", turnLimit: 40, world: generateWorld(worldSeed) },
    ownerId: "bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb",
    runId: "cccccccc-cccc-4ccc-8ccc-cccccccccccc",
    resolutionSeed: "scene-review"
  });
  const resolved = resolveTurn({
    run,
    request: normalizeTurnRequest({
      inputType: "USE_SKILL",
      idempotencyKey: "review-scene-0001",
      expectedRunVersion: 1,
      skillId: "SEARCH",
      targetIds: []
    }),
    d20Source: new FixedD20Source(14)
  });
  const contextInput = directorContext(resolved.run, resolved.turn);
  contextInput.allowedEffects = [];
  contextInput.consequenceBudget = 0;
  contextInput.canonicalFacts = [{ id: "review-fact-marker", summary: "검수 사실 표식" }];
  contextInput.openLoops = [{ id: "review-loop-marker", summary: "검수 열린 고리 표식" }];
  contextInput.episodeSummaries = [{ arcId: "review-episode-marker", summary: "검수 에피소드 표식" }];
  contextInput.majorChoices = [{ id: "review-choice-marker", summary: "검수 주요 선택 표식" }];
  contextInput.regionOutcomes = [{ id: "review-region-marker", summary: "검수 지역 결과 표식" }];
  contextInput.unresolvedHooks = [{ id: "review-hook-marker", summary: "검수 미해결 훅 표식" }];
  const generated = {
    summary: "낯선 흔적을 바라본다",
    body: "나는 바닥에 남은 희미한 흔적을 발견했다. 아직 누구의 것인지는 알 수 없다.",
    dialogue: [],
    storySequence: [
      { type: "MONOLOGUE", speakerId: null, actionId: null, text: "이 흔적을 어떻게 받아들일지 결정해야겠어." }
    ],
    nextIntervention: {
      reason: "흔적의 의미를 정하기 전에 넙죽이의 반응이 필요하다.",
      choices: [
        { choiceId: "trace.ask", text: "주변 사람에게 이 흔적을 본 적이 있는지 묻는다.", choiceKind: "DIALOGUE", intentTag: "CURIOUS", resolutionMode: "NONE", skillId: null, targetEntityId: null, destinationRef: null },
        { choiceId: "trace.search", text: "관리자 키보드로 흔적을 조금 더 자세히 조사한다.", choiceKind: "SKILL", intentTag: "INVESTIGATE", resolutionMode: "D20", skillId: "SEARCH", targetEntityId: null, destinationRef: null }
      ]
    },
    proposedOps: [],
    elementalEffectId: "ELEMENTAL_PLANT"
  };
  const requests = [];
  const outputs = [
    generated,
    { approved: true, reason: "확정된 장면과 이어지며 선택지가 서로 다르다.", storyBeatCount: 1, choiceIds: ["trace.ask", "trace.search"] }
  ];
  const narrator = new GeminiNarrator({
    apiKey: "unit-test-token",
    logger: silentLogger,
    fetchImpl: async (_url, options) => {
      requests.push(options);
      return responseWith(outputs.shift());
    }
  });
  const result = await narrator.narrate(contextInput);
  assert.equal(result.fallbackUsed, false);
  assert.equal(result.elementalEffectId, null);
  assert.equal(requests.length, 2);
  assert.deepEqual(result.nextIntervention.choices.map((choice) => choice.choiceId), ["trace.ask", "trace.search"]);
  const generationRequest = JSON.parse(requests[0].body);
  assert.equal(generationRequest.generationConfig.maxOutputTokens, 1536);
  assert.equal(generationRequest.generationConfig.responseJsonSchema.properties.storySequence.minItems, 4);
  assert.match(generationRequest.systemInstruction.parts[0].text, /advance a small chapter/);
  const reviewPayload = JSON.parse(requests[1].body);
  assert.match(reviewPayload.systemInstruction.parts[0].text, /complete proposed storySequence and nextIntervention/);
  assert.ok(reviewPayload.contents[0].parts[0].text.includes("untrustedProposedScene"));
  assert.ok(reviewPayload.contents[0].parts[0].text.includes("trace.search"));
  const reviewInput = JSON.parse(reviewPayload.contents[0].parts[0].text);
  for (const field of ["canonicalFacts", "openLoops", "episodeSummaries", "majorChoices", "regionOutcomes", "unresolvedHooks"]) {
    assert.deepEqual(reviewInput.readOnlyContinuity[field], contextInput[field], `review continuity must include ${field}`);
  }
});
