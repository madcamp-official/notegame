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
      summary: `A bounded scene unfolds in ${context.area}`,
      body: "The world responds to the player's declared intent without adding mechanical claims.",
      dialogue: [],
      proposedOps: [],
      fallbackUsed: false,
      model: "fake-narrator"
    };
  }
}

async function startServer() {
  const config = loadConfig({ AUTH_MODE: "required", STORAGE: "memory", LOG_LEVEL: "silent" });
  const application = await createApplication({
    config,
    store: new MemoryStore(),
    narrator: new FakeNarrator(),
    d20Source: new FixedD20Source(20),
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
  assert.equal(created.payload.campaign.archetype, "generative-keyboard-fantasy");
  assert.ok(created.payload.campaign.premise.length > 20);
  assert.equal(created.payload.campaign.requiredStoryBeats.length, 6);
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
  assert.notEqual(runCreated.payload.run.campaignTitle, created.payload.campaign.generatedTitle);
  assert.notEqual(runCreated.payload.run.campaignContentHash, created.payload.campaign.contentHash);
  assert.equal(runCreated.payload.run.generationPlan.generationMetadata.fallbackUsed, true);
  assert.equal(runCreated.payload.run.canonicalFacts.length, 5);
  assert.equal(runCreated.payload.run.rumors.length, 1);
  assert.equal(runCreated.payload.run.npcRelationships.length, 6);
  assert.equal(runCreated.payload.run.health, 12);
  assert.equal(runCreated.payload.run.maxHealth, 12);
  assert.equal(runCreated.payload.run.maxFocus, 10);
  assert.deepEqual(runCreated.payload.run.abilities, ["move", "copy", "delete", "connect", "restore", "undo"]);

  const hidden = await jsonRequest(baseUrl, `/v1/campaigns/${campaignId}`, { userId: OTHER_USER_ID });
  assert.equal(hidden.response.status, 404);
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
  const destination = adjacentWalkable(run.world, playerBefore.position);
  const entry = run.world.points.find((point) => point.id === "entry");
  assert.deepEqual(playerBefore.position, { x: entry.x, y: entry.y });

  const request = {
    idempotencyKey: "turn-0001",
    expectedRunVersion: run.version,
    ability: "move",
    destination,
    intent: "Move toward the next witness"
  };
  const first = await jsonRequest(baseUrl, `/v1/runs/${run.id}/turns`, { method: "POST", body: request });
  assert.equal(first.response.status, 201);
  assert.equal(first.payload.turn.d20, 20);
  assert.equal(first.payload.turn.outcome, "critical_success");
  assert.equal(first.payload.turn.dice.raw, 20);
  assert.equal(first.payload.turn.dice.difficulty, 9);
  assert.equal(first.payload.turn.dice.modifier, 3);
  assert.equal(typeof first.payload.turn.dice.intentAlignment, "number");
  assert.ok(first.payload.turn.dice.outcomeExplanation.includes("difficulty"));
  assert.ok(Array.isArray(first.payload.turn.stateDelta.events));
  assert.ok(Array.isArray(first.payload.turn.narrative.dialogue));
  assert.ok(Array.isArray(first.payload.turn.narrative.dialogueDetails));
  assert.equal(first.payload.run.version, 2);
  assert.equal(first.payload.run.currentTurn, 1);
  assert.equal(first.payload.run.world.layoutHash, run.world.layoutHash);
  assert.deepEqual(first.payload.run.entities.find((entity) => entity.id === run.playerEntityId).position, destination);

  const replay = await jsonRequest(baseUrl, `/v1/runs/${run.id}/turns`, { method: "POST", body: request });
  assert.equal(replay.response.status, 200);
  assert.equal(replay.payload.fromIdempotencyCache, true);
  assert.equal(replay.payload.run.currentTurn, 1);
  assert.equal(replay.payload.turn.id, first.payload.turn.id);

  const conflict = await jsonRequest(baseUrl, `/v1/runs/${run.id}/turns`, {
    method: "POST",
    body: { ...request, intent: "A different payload" }
  });
  assert.equal(conflict.response.status, 409);
  assert.equal(conflict.payload.error.code, "IDEMPOTENCY_CONFLICT");

  const stale = await jsonRequest(baseUrl, `/v1/runs/${run.id}/turns`, {
    method: "POST",
    body: { ...request, idempotencyKey: "turn-0002" }
  });
  assert.equal(stale.response.status, 409);
  assert.equal(stale.payload.error.code, "RUN_VERSION_CONFLICT");
  assert.equal(stale.payload.error.details.currentVersion, 2);

  const fetched = await jsonRequest(baseUrl, `/v1/runs/${run.id}/turns/1`);
  assert.equal(fetched.response.status, 200);
  assert.equal(fetched.payload.turn.narrative.model, "fake-narrator");
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
    recentFacts: ["The final region remains sealed until all three story milestones are resolved."]
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
