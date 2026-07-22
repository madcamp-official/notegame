import test from "node:test";
import assert from "node:assert/strict";
import { createApplication } from "../src/app.js";
import { loadConfig } from "../src/config.js";

const databaseUrl = process.env.TEST_DATABASE_URL;
const OWNER_ID = "91919191-9191-4191-8191-919191919191";
const silentLogger = { debug() {}, info() {}, warn() {}, error() {} };

function idSet(records) {
  return new Set(records.map((record) => String(record.id)));
}

function assertDisjoint(left, right, message) {
  assert.deepEqual([...left].filter((id) => right.has(id)), [], message);
}

test("PostgreSQL creates repeated same-seed runs with disjoint run-owned identities", { skip: !databaseUrl }, async (t) => {
  const config = loadConfig({
    STORAGE: "postgres",
    DATABASE_URL: databaseUrl,
    DATABASE_SSL: "false",
    AUTH_MODE: "required",
    LOG_LEVEL: "silent"
  });
  const application = await createApplication({ config, narrator: {}, logger: silentLogger });
  t.after(async () => {
    await application.store.pool.query("delete from keyboard_wanderer.profiles where id = $1", [OWNER_ID]);
    await application.close();
  });

  const campaign = await application.service.createCampaign(OWNER_ID, {
    worldSeed: 20260718,
    turnLimit: 40
  });
  const publicRuns = [];
  for (let index = 0; index < 3; index += 1) {
    publicRuns.push(await application.service.createRun(OWNER_ID, campaign.id, {
      worldSeed: 20260718,
      turnLimit: 40
    }));
  }
  assert.equal(new Set(publicRuns.map((run) => run.id)).size, 3);

  const runs = await Promise.all(publicRuns.map((run) => application.store.getRun(OWNER_ID, run.id)));
  for (let leftIndex = 0; leftIndex < runs.length; leftIndex += 1) {
    for (let rightIndex = leftIndex + 1; rightIndex < runs.length; rightIndex += 1) {
      assertDisjoint(idSet(runs[leftIndex].canonicalFacts), idSet(runs[rightIndex].canonicalFacts),
        "canonical fact instance UUIDs must be disjoint across runs");
      assertDisjoint(idSet(runs[leftIndex].rumors), idSet(runs[rightIndex].rumors),
        "initial rumor instance UUIDs must be disjoint across runs");
      assertDisjoint(idSet(runs[leftIndex].entities), idSet(runs[rightIndex].entities),
        "authoritative entity UUIDs must be disjoint across runs");
    }
  }

  const runIds = runs.map((run) => run.id);
  const storedState = await application.store.pool.query(
    `select r.id as run_id, r.world_state, ps.progress_state
       from keyboard_wanderer.runs r
       join keyboard_wanderer.run_progress_states ps
         on ps.run_id = r.id and ps.owner_id = r.owner_id
      where r.owner_id = $1 and r.id = any($2::uuid[])
      order by r.id`,
    [OWNER_ID, runIds]
  );
  assert.equal(storedState.rowCount, runs.length);
  for (const row of storedState.rows) {
    const run = runs.find((candidate) => candidate.id === row.run_id);
    assert.deepEqual(row.world_state.canonicalFacts, run.canonicalFacts,
      "runs.world_state facts must match the authoritative run JSON");
    assert.deepEqual(row.progress_state.facts, run.canonicalFacts,
      "run_progress_states facts must match the authoritative run JSON");
    assert.deepEqual(row.world_state.rumors, run.rumors,
      "runs.world_state rumors must match the authoritative run JSON");
    assert.deepEqual(row.progress_state.rumors, run.rumors,
      "run_progress_states rumors must match the authoritative run JSON");
  }

  const factRows = await application.store.pool.query(
    `select run_id, id from keyboard_wanderer.world_facts
      where owner_id = $1 and run_id = any($2::uuid[])
      order by run_id, id`,
    [OWNER_ID, runIds]
  );
  for (const run of runs) {
    const projectedIds = idSet(factRows.rows.filter((row) => row.run_id === run.id));
    assert.deepEqual(projectedIds, idSet(run.canonicalFacts),
      "world_facts primary IDs must exactly match that run's authoritative fact JSON");
  }

  const projectionQueries = [
    ["entities", "id"],
    ["actors", "entity_id"],
    ["entity_positions", "entity_id"],
    ["inventories", "id"],
    ["items", "id"],
    ["world_facts", "id"],
    ["npc_relationships", "id"],
    ["unresolved_hooks", "id"],
    ["run_generation_plans", "id"],
    ["run_slot_bindings", "id"],
    ["save_snapshots", "id"],
    ["llm_logs", "id"]
  ];
  for (const [table, idColumn] of projectionQueries) {
    const projection = await application.store.pool.query(
      `select run_id, ${idColumn}::text as id
         from keyboard_wanderer.${table}
        where owner_id = $1 and run_id = any($2::uuid[])
        order by run_id, ${idColumn}`,
      [OWNER_ID, runIds]
    );
    assert.ok(projection.rowCount > 0, `${table} must contain an initial run projection`);
    const byRun = runs.map((run) => idSet(projection.rows.filter((row) => row.run_id === run.id)));
    for (let leftIndex = 0; leftIndex < byRun.length; leftIndex += 1) {
      assert.ok(byRun[leftIndex].size > 0, `${table} must project every created run`);
      for (let rightIndex = leftIndex + 1; rightIndex < byRun.length; rightIndex += 1) {
        assertDisjoint(byRun[leftIndex], byRun[rightIndex],
          `${table}.${idColumn} must be disjoint across runs`);
      }
    }
  }
});
