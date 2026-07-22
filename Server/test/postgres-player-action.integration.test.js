import test from "node:test";
import assert from "node:assert/strict";
import { createApplication } from "../src/app.js";
import { loadConfig } from "../src/config.js";
import { FixedD20Source } from "../src/domain/turn-engine.js";

const databaseUrl = process.env.TEST_DATABASE_URL;
const PLAYER_ACTION_USER_ID = "44444444-4444-4444-8444-444444444449";
const silentLogger = { debug() {}, info() {}, warn() {}, error() {} };

function config() {
  return loadConfig({
    STORAGE: "postgres",
    DATABASE_URL: databaseUrl,
    DATABASE_SSL: "false",
    AUTH_MODE: "required",
    LOG_LEVEL: "silent",
    GEMINI_API_KEY: ""
  });
}

test("PostgreSQL commits and reconstructs a deterministic free-form MOVE action", { skip: !databaseUrl }, async (t) => {
  const options = { config: config(), d20Source: new FixedD20Source(20), logger: silentLogger };
  let application = await createApplication(options);
  t.after(async () => {
    if (!application) application = await createApplication(options);
    await application.store.pool.query("delete from keyboard_wanderer.profiles where id = $1", [PLAYER_ACTION_USER_ID]);
    await application.close();
  });

  const campaign = await application.service.createCampaign(PLAYER_ACTION_USER_ID,
    { worldSeed: 93001, turnLimit: 30 });
  const created = await application.service.createRun(PLAYER_ACTION_USER_ID, campaign.id,
    { worldSeed: 93001, turnLimit: 30 });
  const playerBefore = created.entities.find((entity) => entity.id === created.playerEntityId);

  const committed = await application.service.submitPlayerMessage(PLAYER_ACTION_USER_ID, created.id, {
    text: "갈 수 있는 방향으로 한 걸음 이동한다",
    idempotencyKey: "postgres-freeform-move-0001",
    expectedRunVersion: created.version
  });
  const playerAfter = committed.run.entities.find((entity) => entity.id === committed.run.playerEntityId);
  assert.equal(committed.turn.request.skillId, "MOVE");
  assert.equal(committed.turn.request.actionProposal.kind, "MOVE");
  assert.equal(committed.run.currentTurn, 1);
  assert.notDeepEqual(playerAfter.position, playerBefore.position);
  assert.equal(committed.run.abilityUsageHistory.at(-1).skillId, "MOVE");

  const persisted = await application.store.pool.query(
    `select skill_id, turn_no from keyboard_wanderer.ability_usage_history
      where run_id = $1 and owner_id = $2 order by turn_no`,
    [created.id, PLAYER_ACTION_USER_ID]
  );
  assert.deepEqual(persisted.rows, [{ skill_id: "MOVE", turn_no: 1 }]);

  const expectedPosition = structuredClone(playerAfter.position);
  await application.close();
  application = await createApplication(options);
  const reconstructed = await application.service.getRun(PLAYER_ACTION_USER_ID, created.id);
  const reconstructedPlayer = reconstructed.entities.find((entity) => entity.id === reconstructed.playerEntityId);
  assert.equal(reconstructed.currentTurn, 1);
  assert.deepEqual(reconstructedPlayer.position, expectedPosition);
  assert.equal(reconstructed.abilityUsageHistory.at(-1).skillId, "MOVE");
});
