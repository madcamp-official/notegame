import { spawnSync } from "node:child_process";
import { readdirSync, statSync } from "node:fs";
import path from "node:path";

const ignored = new Set(["node_modules", ".git", "coverage"]);
function *walk(dir) {
  for (const name of readdirSync(dir)) {
    if (ignored.has(name)) continue;
    const file = path.join(dir, name);
    const stat = statSync(file);
    if (stat.isDirectory()) yield *walk(file);
    else if (/\.(?:c?js|mjs)$/.test(name)) yield file;
  }
}
let failed = false;
for (const file of walk("src")) {
  const result = spawnSync(process.execPath, ["--check", file], { encoding: "utf8" });
  if (result.status !== 0) {
    failed = true;
    process.stderr.write(`\n${file}\n${result.stderr || result.stdout}`);
  }
}
if (failed) process.exit(1);
console.log("JavaScript syntax check passed.");
