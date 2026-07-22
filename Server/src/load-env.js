import process from "node:process";
import { existsSync, readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";

const DEFAULT_ENV_PATH = fileURLToPath(new URL("../.env", import.meta.url));

export function loadLocalEnv(path = DEFAULT_ENV_PATH) {
  if (!existsSync(path)) return false;
  if (typeof process.loadEnvFile === "function") {
    process.loadEnvFile(path);
    return true;
  }

  // Compatibility for early Node 20 releases. Existing process variables keep
  // precedence, matching Node's native --env-file/loadEnvFile behavior.
  for (const sourceLine of readFileSync(path, "utf8").split(/\r?\n/u)) {
    const line = sourceLine.trim();
    if (!line || line.startsWith("#")) continue;
    const match = /^(?:export\s+)?([A-Za-z_][A-Za-z0-9_]*)\s*=\s*(.*)$/u.exec(line);
    if (!match || Object.hasOwn(process.env, match[1])) continue;
    let value = match[2].trim();
    if ((value.startsWith('"') && value.endsWith('"')) || (value.startsWith("'") && value.endsWith("'"))) {
      const quote = value[0];
      value = value.slice(1, -1);
      if (quote === '"') value = value.replace(/\\n/gu, "\n").replace(/\\r/gu, "\r").replace(/\\t/gu, "\t").replace(/\\"/gu, '"').replace(/\\\\/gu, "\\");
    } else {
      value = value.replace(/\s+#.*$/u, "").trim();
    }
    process.env[match[1]] = value;
  }
  return true;
}
