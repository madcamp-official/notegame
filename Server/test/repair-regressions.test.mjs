import assert from "node:assert/strict";
import test from "node:test";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import { Readable } from "node:stream";
import { addUnityContractAliases } from "../src/compat/unityContract.js";
import { loadConfig } from "../src/config.js";
import {
  allowedCorsOrigin,
  debugRoutesEnabled,
  publicHttpError,
  readJsonBody,
  resolveStaticFile,
  HttpInputError
} from "../src/http/httpGuards.js";

test("Unity response aliases preserve canonical fields", () => {
  const state = { progressLevel: 3, progressTokens: 7, rootSystemGate: { open: true }, finaleStarted: true };
  addUnityContractAliases(state);
  assert.equal(state.adminLevel, 3);
  assert.equal(state.accessTokens, 7);
  assert.equal(state.rootGate, state.rootSystemGate);
  assert.equal(state.rootStarted, true);
  assert.equal(state.progressLevel, 3);
});

test("contract alias walk is cycle-safe", () => {
  const value = { progressLevel: 1 };
  value.self = value;
  assert.doesNotThrow(() => addUnityContractAliases(value));
});

test("static path resolver blocks traversal and symlink escape", () => {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), "game-static-"));
  const outside = fs.mkdtempSync(path.join(os.tmpdir(), "game-outside-"));
  fs.writeFileSync(path.join(root, "index.html"), "ok");
  fs.writeFileSync(path.join(outside, "secret.txt"), "secret");
  assert.equal(resolveStaticFile(root, "/index.html"), fs.realpathSync(path.join(root, "index.html")));
  assert.throws(() => resolveStaticFile(root, "/../secret.txt"));
  try {
    fs.symlinkSync(path.join(outside, "secret.txt"), path.join(root, "escape.txt"));
    assert.throws(() => resolveStaticFile(root, "/escape.txt"));
  } catch (error) {
    if (error.code !== "EPERM") throw error;
  }
});

test("CORS and development routes are fail-closed outside explicit development opt-in", () => {
  assert.equal(allowedCorsOrigin("https://evil.example", "http://localhost:3000"), null);
  assert.equal(allowedCorsOrigin("http://localhost:3000", "http://localhost:3000"), "http://localhost:3000");
  assert.equal(debugRoutesEnabled({ NODE_ENV: "development" }), false);
  assert.equal(debugRoutesEnabled({ NODE_ENV: "development", ENABLE_DEBUG_ROUTES: "true" }), true);
  assert.equal(debugRoutesEnabled({ NODE_ENV: "test", ENABLE_DEBUG_ROUTES: "true" }), false);
  assert.equal(debugRoutesEnabled({ NODE_ENV: "production", ENABLE_DEBUG_ROUTES: "true" }), false);
  assert.equal(debugRoutesEnabled({ NODE_ENV: "prodution", ENABLE_DEBUG_ROUTES: "true" }), false);
  assert.equal(loadConfig({ NODE_ENV: "development", ENABLE_DEBUG_ROUTES: "true" }).enableDebugRoutes, true);
  assert.equal(loadConfig({ NODE_ENV: "production", ENABLE_DEBUG_ROUTES: "true" }).enableDebugRoutes, false);
  assert.equal(loadConfig({ NODE_ENV: "prodution", ENABLE_DEBUG_ROUTES: "true" }).enableDebugRoutes, false);
});

test("unexpected errors do not leak internal messages", () => {
  assert.deepEqual(publicHttpError(new Error("database password leaked")), { statusCode: 500, body: { error: "Internal server error" } });
  assert.deepEqual(publicHttpError(new HttpInputError(400, "Bad input")), { statusCode: 400, body: { error: "Bad input" } });
});

test("bounded JSON parsing accepts objects and rejects arrays and oversized bodies", async () => {
  assert.deepEqual(await readJsonBody(Readable.from([Buffer.from('{"ok":true}')])), { ok: true });
  await assert.rejects(
    readJsonBody(Readable.from([Buffer.from("[]")])),
    (error) => error instanceof HttpInputError && error.statusCode === 400
  );
  await assert.rejects(
    readJsonBody(Readable.from([Buffer.from("12345")]), { limitBytes: 4 }),
    (error) => error instanceof HttpInputError && error.statusCode === 413
  );
});
