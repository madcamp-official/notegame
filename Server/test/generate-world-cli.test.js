import test from "node:test";
import assert from "node:assert/strict";
import { access, mkdtemp, rm } from "node:fs/promises";
import { tmpdir } from "node:os";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";
import { spawnSync } from "node:child_process";

const TEST_DIRECTORY = dirname(fileURLToPath(import.meta.url));
const SERVER_DIRECTORY = dirname(TEST_DIRECTORY);
const SCRIPT_PATH = join(SERVER_DIRECTORY, "scripts", "generate-world.js");

function runCli(args) {
  return spawnSync(process.execPath, [SCRIPT_PATH, ...args], {
    cwd: SERVER_DIRECTORY,
    encoding: "utf8"
  });
}

test("world generation CLI rejects an unsafe batch seed range before writing artifacts", async () => {
  const temporaryDirectory = await mkdtemp(join(tmpdir(), "keyboard-wanderer-world-cli-"));
  const outputDirectory = join(temporaryDirectory, "batch-output");
  try {
    const result = runCli([
      "--seed", String(Number.MAX_SAFE_INTEGER),
      "--count", "2",
      "--width", "120",
      "--height", "120",
      "--output", outputDirectory
    ]);

    assert.equal(result.status, 1, result.stdout || result.stderr);
    assert.match(result.stderr, /entire seed range must contain only safe integers/i);
    assert.doesNotMatch(result.stderr, /AppError|at generateWorld/);
    await assert.rejects(access(outputDirectory), (error) => error?.code === "ENOENT");
  } finally {
    await rm(temporaryDirectory, { recursive: true, force: true });
  }
});

test("world generation CLI accepts the maximum safe seed for a one-world batch", () => {
  const result = runCli([
    "--seed", String(Number.MAX_SAFE_INTEGER),
    "--count", "1",
    "--width", "120",
    "--height", "120",
    "--summary-only"
  ]);

  assert.equal(result.status, 0, result.stderr);
  assert.equal(JSON.parse(result.stdout).seed, Number.MAX_SAFE_INTEGER);
});
