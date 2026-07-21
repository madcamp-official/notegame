import { appendFileSync, mkdirSync } from "node:fs";
import { dirname, resolve } from "node:path";

export class LlmResponseTrace {
  constructor({ enabled = false, file = "logs/llm-responses.jsonl", clock = () => new Date().toISOString(), logger = console } = {}) {
    this.enabled = enabled;
    this.file = resolve(file);
    this.clock = clock;
    this.logger = logger;
  }

  write(entry) {
    if (!this.enabled) return;
    try {
      mkdirSync(dirname(this.file), { recursive: true });
      appendFileSync(this.file, JSON.stringify({ timestamp: this.clock(), ...entry }) + "\n", "utf8");
    } catch (error) {
      this.logger?.warn?.({ event: "llm_response_trace_failed", category: error?.code || "write_failed" });
    }
  }
}
