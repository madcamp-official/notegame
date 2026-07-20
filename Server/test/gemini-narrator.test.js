import test from "node:test";
import assert from "node:assert/strict";
import { DEFAULT_MODEL_PROFILES, GeminiNarrator } from "../src/llm/gemini-narrator.js";
import { FALLBACK_MODEL } from "../src/llm/narration.js";

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
