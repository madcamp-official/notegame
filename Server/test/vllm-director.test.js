import test from "node:test";
import assert from "node:assert/strict";
import { DEFAULT_MODEL, DEFAULT_MODEL_PROFILES, narrowSchemaForContext, VllmNarrator } from "../src/llm/vllm-director.js";
import { DIRECTOR_RESPONSE_JSON_SCHEMA, FALLBACK_MODEL } from "../src/llm/narration.js";

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

function responseWith(value, finishReason = "stop") {
  return {
    ok: true,
    async json() {
      return { choices: [{ finish_reason: finishReason, message: { role: "assistant", content: JSON.stringify(value) } }] };
    }
  };
}

test("vLLM adapter retries one semantically invalid response and sends OpenAI structured config", async () => {
  const requests = [];
  const outputs = [
    { summary: "Unsafe", body: "Bad op", dialogue: null, proposedOps: [{ type: "delete_entity", text: "Delete it" }] },
    { summary: "Lantern found", body: "Rain beads on the lantern's cracked glass.", dialogue: null, proposedOps: [{ type: "ambient_cue", text: "Play a soft rain cue." }] }
  ];
  const narrator = new VllmNarrator({
    baseUrl: "http://127.0.0.1:8000/v1",
    apiKey: "unit-test-token",
    logger: silentLogger,
    fetchImpl: async (url, options) => {
      requests.push({ url, options });
      return responseWith(outputs.shift());
    }
  });

  const result = await narrator.narrate(context);
  assert.equal(requests.length, 2);
  assert.equal(result.fallbackUsed, false);
  assert.equal(result.model, DEFAULT_MODEL);
  assert.equal(result.proposedOps[0].type, "ambient_cue");
  assert.equal(requests[0].url, "http://127.0.0.1:8000/v1/chat/completions");
  const requestBody = JSON.parse(requests[0].options.body);
  assert.equal(requestBody.model, DEFAULT_MODEL);
  assert.equal(requestBody.max_tokens, DEFAULT_MODEL_PROFILES.fast.maxOutputTokens);
  assert.equal(requestBody.chat_template_kwargs.enable_thinking, false);
  assert.equal(requestBody.response_format.type, "json_schema");
  assert.equal(requestBody.response_format.json_schema.strict, true);
  assert.equal(requestBody.response_format.json_schema.schema.additionalProperties, false);
  assert.equal(requestBody.messages[0].role, "system");
  assert.equal(requests[0].options.headers.authorization, "Bearer unit-test-token");
  assert.equal(requests[0].options.body.includes("unit-test-token"), false);
});

test("vLLM adapter performs only one retry then returns deterministic fallback", async () => {
  let calls = 0;
  const narrator = new VllmNarrator({
    baseUrl: "http://127.0.0.1:8000/v1",
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

test("vLLM adapter treats a truncated (non-stop) response as a failed attempt", async () => {
  let calls = 0;
  const valid = { summary: "ok", body: "A calm, confirmed result unfolds.", dialogue: null, proposedOps: [] };
  const narrator = new VllmNarrator({
    baseUrl: "http://127.0.0.1:8000/v1",
    apiKey: "unit-test-token",
    logger: silentLogger,
    fetchImpl: async () => {
      calls += 1;
      return responseWith(valid, "length");
    }
  });
  const result = await narrator.narrate(context);
  assert.equal(calls, 2);
  assert.equal(result.fallbackUsed, true);
});

test("an empty director allowlist becomes a structural no-operations schema", () => {
  const narrowed = narrowSchemaForContext(DIRECTOR_RESPONSE_JSON_SCHEMA, { mode: "director", allowedEffects: [] });

  assert.equal(narrowed.properties.proposedOps.maxItems, 0);
  // The shared frozen schema must never be mutated in place.
  assert.equal(DIRECTOR_RESPONSE_JSON_SCHEMA.properties.proposedOps.maxItems, 5);
});

test("a populated director allowlist constrains the op enum", () => {
  const narrowed = narrowSchemaForContext(DIRECTOR_RESPONSE_JSON_SCHEMA, { mode: "director", allowedEffects: ["ADD_FACT"] });

  assert.deepEqual(narrowed.properties.proposedOps.items.properties.op, { type: "string", enum: ["ADD_FACT"] });
  assert.equal(narrowed.properties.proposedOps.maxItems, 5);
});

test("legacy contexts keep the shared schema untouched", () => {
  const schema = narrowSchemaForContext(DIRECTOR_RESPONSE_JSON_SCHEMA, { mode: "legacy", allowedEffects: [] });

  assert.equal(schema, DIRECTOR_RESPONSE_JSON_SCHEMA);
});

test("missing base URL never performs a network request", async () => {
  let calls = 0;
  const narrator = new VllmNarrator({
    baseUrl: "",
    apiKey: "unit-test-token",
    logger: silentLogger,
    fetchImpl: async () => {
      calls += 1;
      throw new Error("should not run");
    }
  });
  const result = await narrator.narrate(context);
  assert.equal(calls, 0);
  assert.equal(result.fallbackUsed, true);
});
