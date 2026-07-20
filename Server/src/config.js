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

  return Object.freeze({
    host: env.HOST || "127.0.0.1",
    port: integer(env.PORT, 8787, 1, 65535, "PORT"),
    environment: env.NODE_ENV || "development",
    logLevel: env.LOG_LEVEL || "info",
    storage,
    databaseUrl: env.DATABASE_URL || "",
    databaseSsl: boolean(env.DATABASE_SSL, false),
    authMode,
    defaultUserId: env.DEFAULT_USER_ID || LOCAL_USER_ID,
    geminiApiKey: env.GEMINI_API_KEY || "",
    geminiTimeoutMs: integer(env.GEMINI_TIMEOUT_MS, 4000, 250, 15000, "GEMINI_TIMEOUT_MS"),
    geminiFastModel: env.GEMINI_FAST_MODEL || "gemini-2.5-flash-lite",
    geminiQualityModel: env.GEMINI_QUALITY_MODEL || env.GEMINI_FAST_MODEL || "gemini-2.5-flash-lite",
    geminiFastOutputTokens: integer(env.GEMINI_FAST_OUTPUT_TOKENS, 384, 128, 1024, "GEMINI_FAST_OUTPUT_TOKENS"),
    geminiQualityOutputTokens: integer(env.GEMINI_QUALITY_OUTPUT_TOKENS, 640, 256, 1536, "GEMINI_QUALITY_OUTPUT_TOKENS"),
    llmProvider,
    vllmBaseUrl: env.VLLM_BASE_URL || "",
    vllmApiKey: env.VLLM_API_KEY || "",
    vllmModel: env.VLLM_MODEL || "game-director",
    vllmTimeoutMs: integer(env.VLLM_TIMEOUT_MS, 8000, 250, 30000, "VLLM_TIMEOUT_MS"),
    vllmFastOutputTokens: integer(env.VLLM_FAST_OUTPUT_TOKENS, 384, 128, 1024, "VLLM_FAST_OUTPUT_TOKENS"),
    vllmQualityOutputTokens: integer(env.VLLM_QUALITY_OUTPUT_TOKENS, 640, 256, 1536, "VLLM_QUALITY_OUTPUT_TOKENS"),
    corsOrigins: (env.CORS_ORIGINS || "").split(",").map((value) => value.trim()).filter(Boolean)
  });
}
