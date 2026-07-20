import { AppError } from "../errors.js";
import { CAMPAIGN_PLAN_RESPONSE_JSON_SCHEMA, validateCampaignPlanContext, validateCampaignPlanOutput } from "./campaign-planning.js";
import { createFallbackNarration, responseSchemaForContext, validateNarrationContext, validateNarrationOutput } from "./narration.js";
import { CAMPAIGN_SYSTEM_PROMPT, TURN_SYSTEM_PROMPT } from "./gemini-narrator.js";

const MAX_ATTEMPTS = 2;
const CAMPAIGN_PLAN_MAX_ATTEMPTS = 2;

export const DEFAULT_MODEL = "game-director";

export const DEFAULT_MODEL_PROFILES = Object.freeze({
  fast: Object.freeze({ model: DEFAULT_MODEL, maxOutputTokens: 384 }),
  quality: Object.freeze({ model: DEFAULT_MODEL, maxOutputTokens: 640 })
});

/**
 * Narrator/director adapter for a local vLLM OpenAI-compatible server (e.g. Qwen served as
 * "game-director"). It exposes the same duck-typed surface as GeminiNarrator — narrate,
 * planCampaign, modelProfiles — so GameService can use either provider interchangeably.
 *
 * Only the transport (endpoint, request body, response shape) differs from the Gemini adapter;
 * the prompts, validation, retry policy, and deterministic fallback are shared. The API key is
 * sent as a Bearer token and is never logged or echoed into the request body.
 */
export class VllmNarrator {
  constructor({ baseUrl = "", apiKey = "", timeoutMs = 8000, fetchImpl = globalThis.fetch, logger = console, modelProfiles = DEFAULT_MODEL_PROFILES } = {}) {
    this.baseUrl = String(baseUrl || "").replace(/\/+$/, "");
    this.apiKey = apiKey;
    this.timeoutMs = timeoutMs;
    this.fetchImpl = fetchImpl;
    this.logger = logger;
    this.modelProfiles = modelProfiles;
  }

  async narrate(contextInput) {
    const context = contextInput?.mode ? contextInput : validateNarrationContext(contextInput);
    if (!this.baseUrl || typeof this.fetchImpl !== "function") return createFallbackNarration(context);
    const profileName = context.mode === "director" && context.act === "ending" ? "quality" : "fast";
    const profile = this.modelProfiles[profileName] || this.modelProfiles.fast;
    for (let attempt = 1; attempt <= MAX_ATTEMPTS; attempt += 1) {
      try {
        const raw = await this.generateOnce(context, profile);
        const output = validateNarrationOutput(raw, context);
        return { ...output, fallbackUsed: false, model: profile.model, modelProfile: profileName };
      } catch (error) {
        // Never log prompts, model responses, request headers, raw intent, or the API key.
        this.logger?.warn?.({ event: "vllm_director_attempt_failed", attempt, profile: profileName, category: safeCategory(error) });
      }
    }
    return createFallbackNarration(context);
  }

  async planCampaign(contextInput) {
    const context = validateCampaignPlanContext(contextInput);
    if (!this.baseUrl || typeof this.fetchImpl !== "function") return deterministicPlanFallback("base_url_unavailable");
    const profileName = "campaign-lite";
    const fastProfile = this.modelProfiles.fast || DEFAULT_MODEL_PROFILES.fast;
    const profile = { model: fastProfile.model, maxOutputTokens: Math.min(640, Math.max(384, fastProfile.maxOutputTokens || 384)) };
    for (let attempt = 1; attempt <= CAMPAIGN_PLAN_MAX_ATTEMPTS; attempt += 1) {
      try {
        const raw = await this.generateCampaignPlanOnce(context, profile);
        const output = validateCampaignPlanOutput(raw, context);
        return { ...output, fallbackUsed: false, model: profile.model, modelProfile: profileName };
      } catch (error) {
        // Never log context, model responses, request headers, or the API key.
        this.logger?.warn?.({ event: "vllm_campaign_plan_attempt_failed", attempt, profile: profileName, category: safeCategory(error) });
      }
    }
    return deterministicPlanFallback("retry_exhausted");
  }

  async generateOnce(context, profile) {
    return this.requestJson({
      systemText: TURN_SYSTEM_PROMPT,
      userPayload: { untrustedPlayerAndDirectorContext: context },
      responseJsonSchema: responseSchemaForContext(context),
      schemaName: "director_output",
      profile,
      temperature: context.mode === "director" ? 0.4 : 0.3,
      errorLabel: "director"
    });
  }

  async generateCampaignPlanOnce(context, profile) {
    return this.requestJson({
      systemText: CAMPAIGN_SYSTEM_PROMPT,
      userPayload: { untrustedReadOnlyCampaignContext: context },
      responseJsonSchema: CAMPAIGN_PLAN_RESPONSE_JSON_SCHEMA,
      schemaName: "campaign_plan",
      profile,
      temperature: 0.45,
      errorLabel: "campaign planner"
    });
  }

  async requestJson({ systemText, userPayload, responseJsonSchema, schemaName, profile, temperature, errorLabel }) {
    const controller = new AbortController();
    const timeout = setTimeout(() => controller.abort(), this.timeoutMs);
    try {
      const headers = { "content-type": "application/json" };
      if (this.apiKey) headers.authorization = `Bearer ${this.apiKey}`;
      const response = await this.fetchImpl(`${this.baseUrl}/chat/completions`, {
        method: "POST",
        headers,
        signal: controller.signal,
        body: JSON.stringify({
          model: profile.model,
          messages: [
            { role: "system", content: systemText },
            { role: "user", content: JSON.stringify(userPayload) }
          ],
          temperature,
          max_tokens: profile.maxOutputTokens,
          chat_template_kwargs: { enable_thinking: false },
          response_format: {
            type: "json_schema",
            json_schema: { name: schemaName, strict: true, schema: responseJsonSchema }
          }
        })
      });
      if (!response.ok) throw new AppError(502, "VLLM_HTTP_ERROR", `vLLM ${errorLabel} request failed.`);
      const payload = await response.json();
      const choice = payload?.choices?.[0];
      if (!choice) throw new AppError(502, "VLLM_RESPONSE_EMPTY", "vLLM returned no candidate.");
      if (choice.finish_reason && choice.finish_reason !== "stop") {
        throw new AppError(502, "VLLM_RESPONSE_TRUNCATED", `vLLM stopped with reason ${choice.finish_reason}.`);
      }
      const text = typeof choice.message?.content === "string" ? choice.message.content.trim() : "";
      if (!text) throw new AppError(502, "VLLM_RESPONSE_EMPTY", "vLLM returned empty output.");
      try { return JSON.parse(text); } catch { throw new AppError(502, "VLLM_JSON_INVALID", "vLLM returned invalid JSON."); }
    } finally {
      clearTimeout(timeout);
    }
  }
}

function deterministicPlanFallback(reason) {
  return {
    proposal: null,
    fallbackUsed: true,
    fallbackReason: reason,
    model: "deterministic-campaign-genome",
    modelProfile: "deterministic"
  };
}

function safeCategory(error) {
  if (error?.name === "AbortError") return "timeout";
  if (error instanceof AppError) return error.code;
  return "transport_or_validation";
}
