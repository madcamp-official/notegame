import test from "node:test";
import assert from "node:assert/strict";
import { createApplication } from "../src/app.js";
import { loadConfig } from "../src/config.js";

const databaseUrl = process.env.TEST_DATABASE_URL;
const OWNER_ID = "88888888-8888-4888-8888-888888888888";
const silentLogger = { debug() {}, info() {}, warn() {}, error() {} };

function sleep(milliseconds) {
  return new Promise((resolve) => setTimeout(resolve, milliseconds));
}

class SlowCountingNarrator {
  constructor() {
    this.planCalls = 0;
    this.narrateCalls = 0;
  }

  async planCampaign() {
    this.planCalls += 1;
    await sleep(50);
    return { fallbackUsed: true, fallbackReason: "postgres-idempotency-test", model: "postgres-idempotency-test" };
  }

  async narrate(context) {
    this.narrateCalls += 1;
    await sleep(50);
    return {
      summary: `${context.area}의 요청이 한 번만 확정됐다.`,
      body: "PostgreSQL lease의 승자만 서술을 생성하고 나머지는 권위 턴을 재생했다.",
      dialogue: [], proposedOps: [], fallbackUsed: false, model: "postgres-idempotency-test"
    };
  }
}

test("PostgreSQL creation replay survives restart and concurrent turns call the narrator once", { skip: !databaseUrl }, async (t) => {
  const config = loadConfig({
    STORAGE: "postgres",
    DATABASE_URL: databaseUrl,
    DATABASE_SSL: "false",
    AUTH_MODE: "required",
    LOG_LEVEL: "silent"
  });
  const narrator = new SlowCountingNarrator();
  const options = { config, narrator, logger: silentLogger };
  let application = await createApplication(options);
  let closed = false;
  t.after(async () => {
    if (closed) {
      application = await createApplication(options);
      closed = false;
    }
    await application.store.pool.query("delete from keyboard_wanderer.profiles where id = $1", [OWNER_ID]);
    await application.close();
  });

  const health = await application.store.health();
  assert.equal(health.schemaVersion, "012_request_idempotency_and_schema_readiness");

  const campaignInput = {
    worldSeed: 15001,
    turnLimit: 40,
    idempotencyKey: "postgres-campaign-create-0001"
  };
  const [campaign, campaignReplay] = await Promise.all([
    application.service.createCampaign(OWNER_ID, campaignInput),
    application.service.createCampaign(OWNER_ID, campaignInput)
  ]);
  assert.equal(campaign.id, campaignReplay.id);
  assert.equal([campaign, campaignReplay].filter((item) => item.fromIdempotencyCache === false).length, 1);
  assert.equal([campaign, campaignReplay].filter((item) => item.fromIdempotencyCache === true).length, 1);

  const runInput = {
    worldSeed: 15002,
    turnLimit: 40,
    idempotencyKey: "postgres-run-create-00000001"
  };
  let run;
  let runReplay;
  try {
    [run, runReplay] = await Promise.all([
      application.service.createRun(OWNER_ID, campaign.id, runInput),
      application.service.createRun(OWNER_ID, campaign.id, runInput)
    ]);
  } catch (error) {
    throw new Error(`PostgreSQL run creation failed: ${JSON.stringify({ code: error?.code, details: error?.details })}`, { cause: error });
  }
  assert.equal(run.id, runReplay.id);
  assert.equal(narrator.planCalls, 1);

  await application.close();
  closed = true;
  application = await createApplication(options);
  closed = false;
  const restartReplay = await application.service.createRun(OWNER_ID, campaign.id, runInput);
  assert.equal(restartReplay.id, run.id);
  assert.equal(restartReplay.fromIdempotencyCache, true);
  assert.equal(narrator.planCalls, 1, "restart replay must not call the campaign planner again");

  const current = await application.service.getRun(OWNER_ID, run.id);
  const input = {
    choiceSetId: current.pendingChoiceSet.choiceSetId,
    choiceId: "opening.listen",
    idempotencyKey: "postgres-turn-concurrent-00001",
    expectedRunVersion: current.version
  };
  const results = await Promise.all(Array.from({ length: 6 }, () => application.service.submitChoice(OWNER_ID, run.id, input)));
  assert.equal(narrator.narrateCalls, 1);
  assert.equal(new Set(results.map((result) => result.turn.id)).size, 1);
  assert.equal(results.filter((result) => result.fromIdempotencyCache === false).length, 1);
  assert.equal(results.filter((result) => result.fromIdempotencyCache === true).length, 5);
});
