import { AppError } from "../errors.js";
import { CAMPAIGN_PLAN_RESPONSE_JSON_SCHEMA, validateCampaignPlanContext, validateCampaignPlanOutput } from "./campaign-planning.js";
import { createFallbackNarration, responseSchemaForContext, validateNarrationContext, validateNarrationOutput } from "./narration.js";
import { CAMPAIGN_SYSTEM_PROMPT, NARRATIVE_TARGET_POLICY_PROMPT, TURN_SYSTEM_PROMPT } from "./gemini-narrator.js";
import { createProviderRequestScope, ProviderConcurrencyGate } from "./request-budget.js";
import {
  compactDecisionSchema,
  compactPayloadForModel,
  createFallbackScenePlan,
  flattenPaths,
  restoreScenePlan,
  SCENE_SYSTEM_PROMPT,
  validateSceneTransitionRequest
} from "./scene-transition.js";

const MAX_ATTEMPTS = 2;
const CAMPAIGN_PLAN_MAX_ATTEMPTS = 2;
const SCENE_MAX_ATTEMPTS = 2;
// The compact decision averages ~150 output tokens (§17.2), so 384 is a safe ceiling.
const SCENE_MAX_OUTPUT_TOKENS = 384;

export const DEFAULT_MODEL = "game-director";

// Korean output plus the director JSON envelope costs noticeably more tokens than the Gemini
// path, and a truncated completion is unrecoverable, so the local model gets a larger budget.
export const DEFAULT_MODEL_PROFILES = Object.freeze({
  fast: Object.freeze({ model: DEFAULT_MODEL, maxOutputTokens: 768 }),
  quality: Object.freeze({ model: DEFAULT_MODEL, maxOutputTokens: 1024 })
});

/**
 * Narrows the shared response schema to the operations this turn actually allows. vLLM enforces
 * JSON Schema with xgrammar during decoding, so an empty allowlist becomes a structural guarantee
 * of no proposed operations instead of a post-hoc validation failure.
 */
export function narrowSchemaForContext(schema, context) {
  const allowed = Array.isArray(context?.allowedEffects) ? context.allowedEffects : null;
  if (context?.mode !== "director" || !allowed) return schema;
  const narrowed = structuredClone(schema);
  const proposedOps = narrowed?.properties?.proposedOps;
  if (!proposedOps) return schema;
  if (allowed.length === 0) {
    proposedOps.maxItems = 0;
  } else if (proposedOps.items?.properties?.op) {
    proposedOps.items.properties.op = { type: "string", enum: [...allowed] };
  }
  return narrowed;
}

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
  constructor({
    baseUrl = "", apiKey = "", timeoutMs = 8000, fetchImpl = globalThis.fetch,
    logger = console, modelProfiles = DEFAULT_MODEL_PROFILES,
    circuitCooldownMs = 30000, maxConcurrentRequests = 2, clock = () => Date.now()
  } = {}) {
    this.baseUrl = String(baseUrl || "").replace(/\/+$/, "");
    this.apiKey = apiKey;
    this.timeoutMs = timeoutMs;
    this.fetchImpl = fetchImpl;
    this.logger = logger;
    this.modelProfiles = modelProfiles;
    this.circuitCooldownMs = circuitCooldownMs;
    this.clock = clock;
    this.circuitOpenUntil = 0;
    this.halfOpenProbeInFlight = false;
    this.requestGate = new ProviderConcurrencyGate(maxConcurrentRequests);
  }

  _remoteAvailable() {
    if (!this.baseUrl || typeof this.fetchImpl !== "function") return false;
    if (this.clock() < this.circuitOpenUntil) return false;
    return !(this.circuitOpenUntil > 0 && this.halfOpenProbeInFlight);
  }

  _acquireCircuitToken() {
    const now = this.clock();
    if (now < this.circuitOpenUntil) throw new AppError(503, "VLLM_CIRCUIT_OPEN", "The vLLM circuit is cooling down.");
    const token = { probe: false, observedOpenUntil: this.circuitOpenUntil };
    if (this.circuitOpenUntil > 0) {
      if (this.halfOpenProbeInFlight) throw new AppError(503, "VLLM_CIRCUIT_OPEN", "A vLLM recovery probe is already in flight.");
      this.halfOpenProbeInFlight = true;
      token.probe = true;
    }
    return token;
  }

  _recordCircuitSuccess(token) {
    if (token.probe || this.circuitOpenUntil === token.observedOpenUntil) this.circuitOpenUntil = 0;
    if (token.probe) this.halfOpenProbeInFlight = false;
  }

  _recordCircuitFailure(error, token) {
    if (isVllmTransportFailure(error)) {
      this.circuitOpenUntil = Math.max(this.circuitOpenUntil, this.clock() + this.circuitCooldownMs);
    } else if (token.probe) {
      // A syntactically invalid model answer still proves transport recovery.
      this.circuitOpenUntil = 0;
    }
    if (token.probe) this.halfOpenProbeInFlight = false;
  }

  async narrate(contextInput) {
    const context = contextInput?.mode ? contextInput : validateNarrationContext(contextInput);
    if (!this._remoteAvailable()) return createFallbackNarration(context);
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
    if (!this._remoteAvailable()) return deterministicPlanFallback(this.baseUrl ? "transport_circuit_open" : "base_url_unavailable");
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

  /**
   * SCENE_TRANSITION_PLAN (§14–22): compacts the request, asks the model for index picks under a
   * request-sized enum schema, and deterministically restores the v1.0 plan. Any transport or
   * contract violation retries once, then falls back to the deterministic first-candidate plan —
   * the caller always receives a valid plan.
   */
  async planSceneTransition(requestInput) {
    const request = validateSceneTransitionRequest(requestInput);
    if (!this._remoteAvailable()) return createFallbackScenePlan(request, this.baseUrl ? "transport_circuit_open" : "base_url_unavailable");
    const paths = flattenPaths(request);
    const model = (this.modelProfiles.fast || DEFAULT_MODEL_PROFILES.fast).model;
    for (let attempt = 1; attempt <= SCENE_MAX_ATTEMPTS; attempt += 1) {
      const startedAt = Date.now();
      try {
        const { output, usage, finishReason } = await this.requestOnce({
          systemText: SCENE_SYSTEM_PROMPT,
          userPayload: compactPayloadForModel(request, paths),
          responseJsonSchema: compactDecisionSchema(request, paths),
          schemaName: "compact_game_decision_v1",
          profile: { model, maxOutputTokens: SCENE_MAX_OUTPUT_TOKENS },
          temperature: 0.7,
          topP: 0.9,
          errorLabel: "scene director"
        });
        return restoreScenePlan(request, paths, output, {
          modelProfile: "compact-decision-v1",
          modelId: model,
          inputTokens: usage?.prompt_tokens ?? 0,
          outputTokens: usage?.completion_tokens ?? 0,
          latencyMs: Date.now() - startedAt,
          finishReason
        });
      } catch (error) {
        // Never log prompts, model responses, request headers, or the API key.
        this.logger?.warn?.({ event: "vllm_scene_plan_attempt_failed", attempt, category: safeCategory(error) });
      }
    }
    return createFallbackScenePlan(request, "retry_exhausted");
  }

  async generateOnce(context, profile) {
    // The sentence-count rule lives in the shared system prompt, but the smaller local model
    // follows it far more reliably when it is restated next to the data it must satisfy.
    const userPayload = context.mode === "director"
      ? { outputConstraints: { bodyMustContainSentences: "2 to 4", countedBy: "terminal . ! ? 。！？" }, untrustedPlayerAndDirectorContext: context }
      : { untrustedPlayerAndDirectorContext: context };
    return this.requestJson({
      systemText: TURN_SYSTEM_PROMPT + NARRATIVE_TARGET_POLICY_PROMPT,
      userPayload,
      responseJsonSchema: narrowSchemaForContext(responseSchemaForContext(context), context),
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

  async requestJson(options) {
    const { output } = await this.requestOnce(options);
    return output;
  }

  async requestOnce({ systemText, userPayload, responseJsonSchema, schemaName, profile, temperature, topP, errorLabel }) {
    const circuitToken = this._acquireCircuitToken();
    let scope;
    try {
      scope = createProviderRequestScope({
        timeoutMs: this.timeoutMs,
        timeoutCode: "VLLM_TIMEOUT",
        timeoutMessage: `vLLM ${errorLabel} request timed out.`
      });
      const payload = await this.requestGate.run(scope.signal, async () => {
        const headers = { "content-type": "application/json" };
        if (this.apiKey) headers.authorization = `Bearer ${this.apiKey}`;
        const body = {
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
        };
        if (typeof topP === "number") body.top_p = topP;
        const response = await this.fetchImpl(`${this.baseUrl}/chat/completions`, {
          method: "POST",
          headers,
          signal: scope.signal,
          body: JSON.stringify(body)
        });
        if (!response.ok) throw new AppError(502, "VLLM_HTTP_ERROR", `vLLM ${errorLabel} request failed.`);
        return response.json();
      });
      const choice = payload?.choices?.[0];
      if (!choice) throw new AppError(502, "VLLM_RESPONSE_EMPTY", "vLLM returned no candidate.");
      if (choice.finish_reason && choice.finish_reason !== "stop") {
        throw new AppError(502, "VLLM_RESPONSE_TRUNCATED", `vLLM stopped with reason ${choice.finish_reason}.`);
      }
      const text = typeof choice.message?.content === "string" ? choice.message.content.trim() : "";
      if (!text) throw new AppError(502, "VLLM_RESPONSE_EMPTY", "vLLM returned empty output.");
      try {
        const result = { output: JSON.parse(text), usage: payload?.usage ?? null, finishReason: choice.finish_reason ?? "stop" };
        this._recordCircuitSuccess(circuitToken);
        return result;
      } catch {
        throw new AppError(502, "VLLM_JSON_INVALID", "vLLM returned invalid JSON.");
      }
    } catch (error) {
      let normalized = error;
      if (scope?.signal.aborted && scope.signal.reason instanceof Error) normalized = scope.signal.reason;
      else if (!(error instanceof AppError)) normalized = error?.name === "AbortError"
        ? new AppError(504, "VLLM_TIMEOUT", `vLLM ${errorLabel} request timed out.`)
        : new AppError(502, "VLLM_TRANSPORT_ERROR", `vLLM ${errorLabel} transport failed.`);
      this._recordCircuitFailure(normalized, circuitToken);
      throw normalized;
    } finally {
      scope?.cleanup();
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
  if (error?.name === "AbortError" || error?.code === "VLLM_TIMEOUT" || error?.code === "LLM_TURN_DEADLINE") return "timeout";
  if (error instanceof AppError) return error.code;
  return "transport_or_validation";
}

function isVllmTransportFailure(error) {
  return error?.name === "AbortError" || [
    "VLLM_TIMEOUT", "VLLM_TRANSPORT_ERROR", "VLLM_HTTP_ERROR", "LLM_TURN_DEADLINE"
  ].includes(error?.code);
}
