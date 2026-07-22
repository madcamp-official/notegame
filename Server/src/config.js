import { AppError } from "./errors.js";

const LOCAL_USER_ID = "00000000-0000-4000-8000-000000000001";

function integer(value, fallback, minimum, maximum, name) {
  if (value === undefined || value === "") return fallback;
  const parsed = Number(value);
  if (!Number.isInteger(parsed) || parsed < minimum || parsed > maximum) {
    throw new AppError(500, "CONFIG_INVALID", `${name} is invalid.`);
  }
  return parsed;
}

function boolean(value, fallback) {
  if (value === undefined || value === "") return fallback;
  return value === "true" || value === "1";
}

export function loadConfig(env = process.env) {
  const storage = env.STORAGE || "memory";
  const authMode = env.AUTH_MODE || "local";
  const environment = env.NODE_ENV || "development";
  if (!["memory", "postgres"].includes(storage)) {
    throw new AppError(500, "CONFIG_INVALID", "STORAGE must be memory or postgres.");
  }
  if (!["local", "required"].includes(authMode)) {
    throw new AppError(500, "CONFIG_INVALID", "AUTH_MODE must be local or required.");
  }
  if (storage === "postgres" && !env.DATABASE_URL) {
    throw new AppError(500, "CONFIG_INVALID", "DATABASE_URL is required for postgres storage.");
  }
  const llmProvider = env.LLM_PROVIDER || "gemini";
  if (!["gemini", "vllm"].includes(llmProvider)) {
    throw new AppError(500, "CONFIG_INVALID", "LLM_PROVIDER must be gemini or vllm.");
  }

  const corsOrigins = (env.CORS_ORIGINS ||
    "http://localhost:3000,http://localhost:5173,http://127.0.0.1:3000,http://127.0.0.1:5173")
    .split(",").map((value) => value.trim()).filter(Boolean);

  return Object.freeze({
    host: env.HOST || "127.0.0.1",
    port: integer(env.PORT, 8787, 1, 65535, "PORT"),
    environment,
    logLevel: env.LOG_LEVEL || "info",
    storage,
    databaseUrl: env.DATABASE_URL || "",
    databaseSsl: boolean(env.DATABASE_SSL, false),
    idempotencyLeaseMs: integer(env.IDEMPOTENCY_LEASE_MS, 30000, 5000, 300000, "IDEMPOTENCY_LEASE_MS"),
    idempotencyWaitTimeoutMs: integer(env.IDEMPOTENCY_WAIT_TIMEOUT_MS, 25000, 1000, 600000, "IDEMPOTENCY_WAIT_TIMEOUT_MS"),
    authMode,
    defaultUserId: env.DEFAULT_USER_ID || LOCAL_USER_ID,
    geminiApiKey: env.GEMINI_API_KEY || "",
    geminiTimeoutMs: integer(env.GEMINI_TIMEOUT_MS, 15000, 250, 15000, "GEMINI_TIMEOUT_MS"),
    geminiCircuitCooldownMs: integer(env.GEMINI_CIRCUIT_COOLDOWN_MS, 30000, 1000, 120000, "GEMINI_CIRCUIT_COOLDOWN_MS"),
    geminiFastModel: env.GEMINI_FAST_MODEL || "gemini-3.1-flash-lite",
    geminiQualityModel: env.GEMINI_QUALITY_MODEL || env.GEMINI_FAST_MODEL || "gemini-3.1-flash-lite",
    geminiFastOutputTokens: integer(env.GEMINI_FAST_OUTPUT_TOKENS, 1024, 256, 2048, "GEMINI_FAST_OUTPUT_TOKENS"),
    geminiQualityOutputTokens: integer(env.GEMINI_QUALITY_OUTPUT_TOKENS, 1536, 512, 3072, "GEMINI_QUALITY_OUTPUT_TOKENS"),
    llmResponseTrace: boolean(env.LLM_RESPONSE_TRACE, environment === "development"),
    llmResponseTraceFile: env.LLM_RESPONSE_TRACE_FILE || "logs/llm-responses.jsonl",
    llmProvider,
    llmTurnDeadlineMs: integer(env.LLM_TURN_DEADLINE_MS, 20000, 1000, 120000, "LLM_TURN_DEADLINE_MS"),
    llmTurnMaxCalls: integer(env.LLM_TURN_MAX_CALLS, 6, 1, 20, "LLM_TURN_MAX_CALLS"),
    llmMaxConcurrentRequests: integer(env.LLM_MAX_CONCURRENT_REQUESTS, 2, 1, 16, "LLM_MAX_CONCURRENT_REQUESTS"),
    vllmBaseUrl: env.VLLM_BASE_URL || "",
    vllmApiKey: env.VLLM_API_KEY || "",
    vllmModel: env.VLLM_MODEL || "game-director",
    vllmTimeoutMs: integer(env.VLLM_TIMEOUT_MS, 8000, 250, 30000, "VLLM_TIMEOUT_MS"),
    vllmCircuitCooldownMs: integer(env.VLLM_CIRCUIT_COOLDOWN_MS, 30000, 1000, 120000, "VLLM_CIRCUIT_COOLDOWN_MS"),
    vllmFastOutputTokens: integer(env.VLLM_FAST_OUTPUT_TOKENS, 768, 128, 2048, "VLLM_FAST_OUTPUT_TOKENS"),
    vllmQualityOutputTokens: integer(env.VLLM_QUALITY_OUTPUT_TOKENS, 1024, 256, 2048, "VLLM_QUALITY_OUTPUT_TOKENS"),
    corsOrigins,
    enableDebugRoutes: boolean(env.ENABLE_DEBUG_ROUTES, false) && environment === "development"
  });
}
