import { createServer } from "node:http";
import { loadConfig } from "./config.js";
import { createLogger } from "./logger.js";
import { createRequestHandler } from "./http/handler.js";
import { GeminiNarrator } from "./llm/gemini-narrator.js";
import { MemoryStore } from "./store/memory-store.js";
import { createPostgresStore } from "./store/postgres-store.js";
import { GameService } from "./services/game-service.js";

export async function createApplication(options = {}) {
  const config = options.config || loadConfig();
  const logger = options.logger || createLogger(config.logLevel);
  const store = options.store || (config.storage === "postgres"
    ? await createPostgresStore({ connectionString: config.databaseUrl, ssl: config.databaseSsl })
    : new MemoryStore());
  const narrator = options.narrator || new GeminiNarrator({
    apiKey: config.geminiApiKey,
    timeoutMs: config.geminiTimeoutMs,
    modelProfiles: {
      fast: { model: config.geminiFastModel, maxOutputTokens: config.geminiFastOutputTokens },
      quality: { model: config.geminiQualityModel, maxOutputTokens: config.geminiQualityOutputTokens }
    },
    logger
  });
  const service = options.service || new GameService({
    store,
    narrator,
    d20Source: options.d20Source,
    clock: options.clock,
    logger
  });
  const server = createServer(createRequestHandler({ service, config, logger }));
  return {
    config,
    logger,
    store,
    narrator,
    service,
    server,
    async close() {
      if (server.listening) await new Promise((resolve, reject) => server.close((error) => error ? reject(error) : resolve()));
      await store.close?.();
    }
  };
}
