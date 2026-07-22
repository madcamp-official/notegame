import test from "node:test";
import assert from "node:assert/strict";
import { PostgresStore } from "../src/store/postgres-store.js";

test("PostgreSQL store retries a transient serialization failure as one owner transaction", async () => {
  let connectionCount = 0;
  let callbackCount = 0;
  let rollbackCount = 0;
  let releaseCount = 0;

  const pool = {
    async connect() {
      connectionCount += 1;
      const thisConnection = connectionCount;
      return {
        async query(sql) {
          if (sql === "commit" && thisConnection === 1) {
            const error = new Error("serialization failure");
            error.code = "40001";
            throw error;
          }
          if (sql === "rollback") rollbackCount += 1;
          return { rows: [], rowCount: 0 };
        },
        release() {
          releaseCount += 1;
        }
      };
    }
  };

  const store = new PostgresStore(pool);
  const result = await store.withOwner(
    "11111111-1111-4111-8111-111111111111",
    async () => {
      callbackCount += 1;
      return "committed";
    },
    { isolation: "serializable" }
  );

  assert.equal(result, "committed");
  assert.equal(connectionCount, 2);
  assert.equal(callbackCount, 2);
  assert.equal(rollbackCount, 1);
  assert.equal(releaseCount, 2);
});
