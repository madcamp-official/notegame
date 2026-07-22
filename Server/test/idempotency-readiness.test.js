import test from "node:test";
import assert from "node:assert/strict";
import { createApplication } from "../src/app.js";
import { loadConfig } from "../src/config.js";
import { AppError } from "../src/errors.js";
import { GameService } from "../src/services/game-service.js";
import { MemoryStore } from "../src/store/memory-store.js";
import { PostgresStore } from "../src/store/postgres-store.js";

const OWNER_ID = "77777777-7777-4777-8777-777777777777";
const silentLogger = { debug() {}, info() {}, warn() {}, error() {} };

function sleep(milliseconds) {
  return new Promise((resolve) => setTimeout(resolve, milliseconds));
}

class CountingNarrator {
  constructor() {
    this.planCalls = 0;
    this.narrateCalls = 0;
  }

  async planCampaign() {
    this.planCalls += 1;
    await sleep(40);
    return { fallbackUsed: true, fallbackReason: "focused-test", model: "focused-test" };
  }

  async narrate(context) {
    this.narrateCalls += 1;
    await sleep(40);
    return {
      summary: `${context.area}에서 하나의 장면만 확정됐다.`,
      body: "동시에 도착한 동일 요청은 한 번만 처리되고 같은 결과를 공유했다.",
      dialogue: [],
      proposedOps: [],
      fallbackUsed: false,
      model: "counting-narrator"
    };
  }
}

async function createKeyedRun(service) {
  const campaign = await service.createCampaign(OWNER_ID, {
    worldSeed: 12031,
    turnLimit: 40,
    idempotencyKey: "campaign-create-focused-0001"
  });
  const run = await service.createRun(OWNER_ID, campaign.id, {
    worldSeed: 12032,
    turnLimit: 40,
    idempotencyKey: "run-create-focused-00000001"
  });
  return { campaign, run };
}

test("memory creation idempotency replays one campaign/run and calls the planner once", async () => {
  const store = new MemoryStore();
  const narrator = new CountingNarrator();
  const service = new GameService({ store, narrator, logger: silentLogger });
  const campaignInput = {
    title: "동일 생성 요청",
    worldSeed: 13001,
    turnLimit: 40,
    idempotencyKey: "campaign-create-concurrent-0001"
  };
  const campaigns = await Promise.all(Array.from({ length: 6 }, () => service.createCampaign(OWNER_ID, campaignInput)));
  assert.equal(new Set(campaigns.map((campaign) => campaign.id)).size, 1);
  assert.equal(campaigns.filter((campaign) => campaign.fromIdempotencyCache === false).length, 1);
  assert.equal(campaigns.filter((campaign) => campaign.fromIdempotencyCache === true).length, 5);
  assert.equal((await service.listCampaigns(OWNER_ID)).length, 1);

  const runInput = {
    worldSeed: 13002,
    turnLimit: 40,
    themeHint: "같은 요청은 같은 세계를 가리킨다",
    idempotencyKey: "run-create-concurrent-00000001"
  };
  const runs = await Promise.all(Array.from({ length: 6 }, () => service.createRun(OWNER_ID, campaigns[0].id, runInput)));
  assert.equal(new Set(runs.map((run) => run.id)).size, 1);
  assert.equal(store.runs.size, 1);
  assert.equal(narrator.planCalls, 1);
  const replay = await service.createRun(OWNER_ID, campaigns[0].id, runInput);
  assert.equal(replay.id, runs[0].id);
  assert.equal(replay.fromIdempotencyCache, true);

  await assert.rejects(
    service.createRun(OWNER_ID, campaigns[0].id, { ...runInput, turnLimit: 41 }),
    (error) => error instanceof AppError && error.status === 409 && error.code === "IDEMPOTENCY_CONFLICT"
  );
});

test("concurrent identical turns execute narration once and replay one committed turn", async () => {
  const store = new MemoryStore();
  const narrator = new CountingNarrator();
  const service = new GameService({ store, narrator, logger: silentLogger });
  const { run } = await createKeyedRun(service);
  const choice = run.pendingChoiceSet.choices.find((item) => item.choiceId === "opening.attack");
  const input = {
    choiceSetId: run.pendingChoiceSet.choiceSetId,
    choiceId: choice.choiceId,
    idempotencyKey: "turn-concurrent-focused-000001",
    expectedRunVersion: run.version
  };
  const results = await Promise.all(Array.from({ length: 10 }, () => service.submitChoice(OWNER_ID, run.id, input)));
  assert.equal(narrator.narrateCalls, 1);
  assert.equal(new Set(results.map((result) => result.turn.id)).size, 1);
  assert.equal(results.filter((result) => result.fromIdempotencyCache === false).length, 1);
  assert.equal(results.filter((result) => result.fromIdempotencyCache === true).length, 9);
  assert.equal((await service.listTurns(OWNER_ID, run.id)).length, 1);
  assert.equal((await service.getRun(OWNER_ID, run.id)).currentTurn, 1);

  await assert.rejects(
    service.submitChoice(OWNER_ID, run.id, { ...input, choiceId: "opening.fabricated" }),
    (error) => error instanceof AppError && error.code === "IDEMPOTENCY_CONFLICT"
  );
  assert.equal(narrator.narrateCalls, 1);
});

test("a failed memory lease can be released and safely acquired again", async () => {
  const store = new MemoryStore();
  const request = {
    ownerId: OWNER_ID,
    operation: "turn:77777777-7777-4777-8777-777777777778",
    idempotencyKey: "failed-leader-focused-0001",
    requestFingerprint: "a".repeat(64),
    leaseMs: 5000
  };
  assert.deepEqual(await store.claimIdempotency({ ...request, leaseToken: "leader-one" }), { state: "acquired" });
  assert.equal(await store.releaseIdempotency({ ...request, leaseToken: "leader-one" }), true);
  assert.deepEqual(await store.claimIdempotency({ ...request, leaseToken: "leader-two" }), { state: "acquired" });

  const creation = { ...request, operation: "campaign.create", idempotencyKey: "missing-create-result-000001" };
  assert.deepEqual(await store.claimIdempotency({ ...creation, leaseToken: "creator-one" }), { state: "acquired" });
  await assert.rejects(
    store.completeIdempotency({ ...creation, leaseToken: "creator-one", response: null }),
    (error) => error instanceof AppError && error.code === "IDEMPOTENCY_RESULT_MISSING"
  );

  const service = new GameService({ store, narrator: {}, logger: silentLogger });
  const envelope = {
    ownerId: OWNER_ID,
    operation: "campaign.create",
    idempotencyKey: "failed-service-leader-000001",
    requestFingerprint: "c".repeat(64),
    persistResponse: true,
    markReplay: true
  };
  await assert.rejects(
    service._runIdempotent(envelope, async () => { throw new AppError(503, "LEADER_FAILED", "retryable"); }),
    (error) => error.code === "LEADER_FAILED"
  );
  const recovered = await service._runIdempotent(envelope, async () => ({ id: "recovered", fromIdempotencyCache: false }));
  assert.deepEqual(recovered, { id: "recovered", fromIdempotencyCache: false });
  const replayed = await service._runIdempotent(envelope, async () => assert.fail("completed response must replay"));
  assert.deepEqual(replayed, { id: "recovered", fromIdempotencyCache: true });
});

test("creation Idempotency-Key header is optional, replayable, and must match the body", async (t) => {
  const config = loadConfig({ AUTH_MODE: "required", STORAGE: "memory", LOG_LEVEL: "silent" });
  assert.equal(config.idempotencyLeaseMs, 30000);
  assert.equal(config.idempotencyWaitTimeoutMs, 25000);
  const application = await createApplication({
    config,
    store: new MemoryStore(),
    narrator: new CountingNarrator(),
    logger: silentLogger
  });
  t.after(() => application.close());
  await new Promise((resolve) => application.server.listen(0, "127.0.0.1", resolve));
  const { port } = application.server.address();
  const url = `http://127.0.0.1:${port}/v1/campaigns`;
  const body = JSON.stringify({ title: "헤더 재생", worldSeed: 14001, turnLimit: 40 });
  const request = (headers = {}) => fetch(url, {
    method: "POST",
    headers: { "content-type": "application/json", "x-user-id": OWNER_ID, ...headers },
    body
  });
  const first = await request({ "idempotency-key": "campaign-header-focused-0001" });
  const replay = await request({ "idempotency-key": "campaign-header-focused-0001" });
  assert.equal(first.status, 201);
  assert.equal(replay.status, 200);
  const firstPayload = await first.json();
  const replayPayload = await replay.json();
  assert.equal(firstPayload.campaign.id, replayPayload.campaign.id);
  assert.equal(firstPayload.campaign.fromIdempotencyCache, false);
  assert.equal(replayPayload.campaign.fromIdempotencyCache, true);

  const mismatch = await fetch(url, {
    method: "POST",
    headers: {
      "content-type": "application/json",
      "x-user-id": OWNER_ID,
      "idempotency-key": "campaign-header-focused-0002"
    },
    body: JSON.stringify({ worldSeed: 14002, idempotencyKey: "campaign-body-focused-000003" })
  });
  assert.equal(mismatch.status, 400);
  assert.equal((await mismatch.json()).error.code, "IDEMPOTENCY_KEY_INVALID");
});

test("PostgreSQL readiness fails closed for missing relations or migration version", async () => {
  const requiredRows = ["campaigns", "runs", "turn_records", "safe_travels", "request_idempotency", "schema_migrations"]
    .map((relation_name) => ({ relation_name, present: true }));

  const missingStore = new PostgresStore({
    async query() {
      return { rows: requiredRows.map((row) => row.relation_name === "request_idempotency" ? { ...row, present: false } : row) };
    }
  });
  await assert.rejects(
    missingStore.health(),
    (error) => error instanceof AppError && error.status === 503 && error.code === "DATABASE_SCHEMA_OUTDATED"
      && error.details.missingRelations.includes("request_idempotency")
  );

  let deniedQueryNo = 0;
  const deniedStore = new PostgresStore({
    async query() {
      deniedQueryNo += 1;
      return deniedQueryNo === 1
        ? { rows: requiredRows }
        : { rows: [{ relation_name: "runs", privilege: "UPDATE", allowed: false }] };
    }
  });
  await assert.rejects(
    deniedStore.health(),
    (error) => error instanceof AppError && error.status === 503 && error.code === "DATABASE_SCHEMA_ACCESS_DENIED"
      && error.details.deniedPrivileges.includes("runs:UPDATE")
  );

  let queryNo = 0;
  const outdatedStore = new PostgresStore({
    async query() {
      queryNo += 1;
      if (queryNo === 1) return { rows: requiredRows };
      if (queryNo === 2) return { rows: [{ relation_name: "campaigns", privilege: "SELECT", allowed: true }] };
      return { rowCount: 0, rows: [] };
    }
  });
  await assert.rejects(
    outdatedStore.health(),
    (error) => error instanceof AppError && error.status === 503 && error.code === "DATABASE_SCHEMA_OUTDATED"
  );

  queryNo = 0;
  const readyStore = new PostgresStore({
    async query() {
      queryNo += 1;
      if (queryNo === 1) return { rows: requiredRows };
      if (queryNo === 2) return { rows: [{ relation_name: "campaigns", privilege: "SELECT", allowed: true }] };
      return { rowCount: 1, rows: [{ version: "012_request_idempotency_and_schema_readiness" }] };
    }
  });
  assert.deepEqual(await readyStore.health(), {
    ok: true,
    storage: "postgres",
    schemaVersion: "012_request_idempotency_and_schema_readiness"
  });
});
