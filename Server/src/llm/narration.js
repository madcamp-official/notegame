import { assert, AppError } from "../errors.js";
import { DIRECTOR_OPS } from "../domain/turn-engine.js";
import { containsProtectedFactReference } from "../domain/protected-mechanics.js";

export const FALLBACK_MODEL = "deterministic-fallback-v2";
export const LEGACY_NARRATIVE_OPS = Object.freeze(["fact_hint", "rumor_hint", "npc_memory_hint", "quest_hint", "ambient_cue"]);

const OUTCOME_VALUES = new Set(["critical_failure", "failure", "partial_success", "success", "critical_success"]);
const ABILITY_PATTERN = /^[a-z][a-z0-9_]{1,31}$/;
const METRIC_MECHANICAL_ASSERTION = /(?:\b(?:world[\s._-]*stability|world[\s._-]*autonomy|public[\s._-]*trust|technical[\s._-]*debt|companion[\s._-]*bond|turn[\s._-]*pressure)\b|세계\s*안정성|세계\s*자율성|공공\s*신뢰|기술\s*부채|동료\s*유대|턴\s*압박)/i;
const NARRATIVE_MECHANICAL_ASSERTION = /(?:\bprogress\s*level\b|\bmilestone\s*token.{0,80}\b(?:grant(?:ed)?|gain(?:ed)?|receive[ds]?|award(?:ed)?|unlock(?:ed)?)\b|\bfinale\s*(?:is\s*)?(?:resolved|completed|unlocked|confirmed)\b|\bending\s*(?:is\s*)?(?:chosen|confirmed|locked|reached)\b|\b(?:hp|health|focus|damage|turn|version|reward)\s*(?::|=|is|to|became|changed)?\s*[+-]?\d+\b|\bcoordinate\s*\(|\bposition\s*(?:is|=|became|changed)\b|진행\s*(?:레벨|단계).{0,40}(?:\d+|획득|상승|확정|부여)|이정표\s*(?:조각|토큰|증표).{0,60}(?:획득|지급|부여|해금)|피날레.{0,40}(?:해결|완료|해금|확정)|결말.{0,40}(?:선택|확정|도달|고정)|(?:체력|집중|피해|턴|버전|보상)\s*(?::|=|은|는|이|가)?\s*[+-]?\d+|좌표\s*\(|위치\s*(?:는|가|은|이)?\s*\()/i;

function exactKeys(object, allowed, code, status = 400) {
  const unknown = Object.keys(object).filter((key) => !allowed.includes(key));
  if (unknown.length > 0) throw new AppError(status, code, `Unknown fields: ${unknown.join(", ")}.`);
}

function boundedString(value, { name, minimum = 0, maximum, status = 400 }) {
  if (typeof value !== "string") throw new AppError(status, status === 400 ? "NARRATION_CONTEXT_INVALID" : "LLM_OUTPUT_INVALID", `${name} must be a string.`);
  const normalized = value.trim();
  if (normalized.length < minimum || normalized.length > maximum) throw new AppError(status, status === 400 ? "NARRATION_CONTEXT_INVALID" : "LLM_OUTPUT_INVALID", `${name} is outside its length limit.`);
  return normalized;
}

function validateBase(input) {
  assert(Number.isInteger(input.turnNo) && input.turnNo >= 0 && input.turnNo <= 10000, 400, "NARRATION_CONTEXT_INVALID", "turnNo is invalid.");
  assert(Number.isInteger(input.remainingTurns) && input.remainingTurns >= 0 && input.remainingTurns <= 1000, 400, "NARRATION_CONTEXT_INVALID", "remainingTurns is invalid.");
  const area = boundedString(input.area, { name: "area", minimum: 1, maximum: 120 });
  const intent = boundedString(input.intent, { name: "intent", minimum: 1, maximum: 800 });
  const ability = boundedString(input.ability, { name: "ability", minimum: 2, maximum: 32 }).toLowerCase();
  assert(ABILITY_PATTERN.test(ability), 400, "NARRATION_CONTEXT_INVALID", "ability has an invalid format.");
  assert(Number.isInteger(input.d20) && input.d20 >= 1 && input.d20 <= 20, 400, "NARRATION_CONTEXT_INVALID", "d20 must be between 1 and 20.");
  assert(typeof input.outcome === "string" && OUTCOME_VALUES.has(input.outcome), 400, "NARRATION_CONTEXT_INVALID", "outcome is invalid.");
  const normalizedAttempt = boundedString(input.normalizedAttempt, { name: "normalizedAttempt", minimum: 1, maximum: 800 });
  return { area, intent, ability, normalizedAttempt };
}

export function validateNarrationContext(input) {
  assert(input && typeof input === "object" && !Array.isArray(input), 400, "NARRATION_CONTEXT_INVALID", "A compact narration context object is required.");
  if (input.schemaVersion === "2.0" || input.requestType === "TURN_NARRATION") return validateDirectorContext(input);
  exactKeys(input, ["turnNo", "remainingTurns", "area", "intent", "ability", "d20", "outcome", "normalizedAttempt", "allowedEffects", "recentFacts"], "NARRATION_CONTEXT_INVALID");
  const base = validateBase(input);
  assert(Array.isArray(input.allowedEffects) && input.allowedEffects.length <= LEGACY_NARRATIVE_OPS.length, 400, "NARRATION_CONTEXT_INVALID", "allowedEffects must be a short array.");
  const allowedEffects = [...new Set(input.allowedEffects.map((effect) => boundedString(effect, { name: "allowedEffects item", minimum: 1, maximum: 40 })))];
  assert(allowedEffects.every((effect) => LEGACY_NARRATIVE_OPS.includes(effect)), 400, "NARRATION_EFFECT_FORBIDDEN", "allowedEffects contains a non-narrative operation.");
  assert(Array.isArray(input.recentFacts) && input.recentFacts.length <= 12, 400, "NARRATION_CONTEXT_INVALID", "recentFacts must contain at most 12 items.");
  return { mode: "legacy", ...input, ...base, allowedEffects, recentFacts: input.recentFacts.map((fact) => boundedString(fact, { name: "recentFacts item", minimum: 1, maximum: 240 })) };
}

function validateDirectorContext(input) {
  exactKeys(input, ["schemaVersion", "requestType", "campaign", "progression", "turnNo", "remainingTurns", "act", "currentStoryBeat", "area", "areaSummary", "intent", "ability", "normalizedAttempt", "intentAnalysis", "d20", "outcome", "dice", "consequenceBudget", "rulesetVersion", "stateHashBefore", "stateHashAfter", "allowedEffects", "allowedEntityIds", "allowedQuestIds", "allowedQuestTemplateIds", "activeQuests", "visibleEntities", "placementSlots", "readOnlyPlaces", "readOnlySlots", "geometryPolicy", "canonicalFacts", "openLoops", "rumors", "npcRelationships", "recentMemories"], "NARRATION_CONTEXT_INVALID");
  const base = validateBase(input);
  assert(input.schemaVersion === "2.0" && input.requestType === "TURN_NARRATION", 400, "NARRATION_CONTEXT_INVALID", "Director context version or type is invalid.");
  assert(Number.isInteger(input.consequenceBudget) && input.consequenceBudget >= 0 && input.consequenceBudget <= 4, 400, "NARRATION_CONTEXT_INVALID", "consequenceBudget is invalid.");
  assert(Array.isArray(input.allowedEffects) && input.allowedEffects.every((effect) => DIRECTOR_OPS.includes(effect)), 400, "NARRATION_EFFECT_FORBIDDEN", "Director allowedEffects contains an unsupported operation.");
  assert(input.campaign && typeof input.campaign === "object" && typeof input.campaign.title === "string" && typeof input.campaign.premise === "string", 400, "NARRATION_CONTEXT_INVALID", "campaign must contain the sealed run premise.");
  assert(input.progression && typeof input.progression === "object" && Number.isInteger(input.progression.level) && Array.isArray(input.progression.tokens), 400, "NARRATION_CONTEXT_INVALID", "progression must contain server-owned milestone state.");
  assert(typeof input.rulesetVersion === "string" && input.rulesetVersion.length >= 3 && input.rulesetVersion.length <= 80, 400, "NARRATION_CONTEXT_INVALID", "rulesetVersion is invalid.");
  assert(/^[0-9a-f]{64}$/.test(input.stateHashBefore || "") && /^[0-9a-f]{64}$/.test(input.stateHashAfter || ""), 400, "NARRATION_CONTEXT_INVALID", "Director state hashes must be canonical SHA-256 values.");
  for (const field of ["allowedEntityIds", "allowedQuestIds", "allowedQuestTemplateIds", "activeQuests", "visibleEntities", "placementSlots", "readOnlyPlaces", "readOnlySlots", "canonicalFacts", "openLoops", "rumors", "npcRelationships", "recentMemories"]) assert(Array.isArray(input[field]), 400, "NARRATION_CONTEXT_INVALID", `${field} must be an array.`);
  assert(input.intentAnalysis && typeof input.intentAnalysis === "object" && typeof input.intentAnalysis.score === "number" && input.intentAnalysis.score >= 0 && input.intentAnalysis.score <= 1, 400, "NARRATION_CONTEXT_INVALID", "intentAnalysis is invalid.");
  assert(input.geometryPolicy === "read_only_ids_and_visual_intent_only", 400, "NARRATION_CONTEXT_INVALID", "geometryPolicy must forbid model geometry mutation.");
  assert(input.activeQuests.length <= 6 && input.visibleEntities.length <= 32 && input.placementSlots.length <= 8 && input.readOnlyPlaces.length <= 24 && input.readOnlySlots.length <= 12 && input.canonicalFacts.length <= 16, 400, "NARRATION_CONTEXT_INVALID", "Director context exceeds its bounded collection limits.");
  return { mode: "director", ...input, ...base, allowedEffects: [...new Set(input.allowedEffects)] };
}

export function validateNarrationOutput(input, contextInput) {
  const context = contextInput.mode ? contextInput : validateNarrationContext(contextInput);
  assert(input && typeof input === "object" && !Array.isArray(input), 502, "LLM_OUTPUT_INVALID", "Narration output must be a JSON object.");
  exactKeys(input, ["summary", "body", "dialogue", "proposedOps"], "LLM_OUTPUT_INVALID", 502);
  const summary = boundedString(input.summary, { name: "summary", minimum: 1, maximum: 160, status: 502 });
  const body = boundedString(input.body, { name: "body", minimum: 1, maximum: 700, status: 502 });
  validateNarrativeMechanicalClaims(summary, body, context);
  if (!Array.isArray(input.proposedOps) || input.proposedOps.length > 5) throw new AppError(502, "LLM_OUTPUT_INVALID", "proposedOps must contain at most 5 items.");
  if (context.mode === "legacy") return validateLegacyOutput({ summary, body, dialogue: input.dialogue, proposedOps: input.proposedOps }, context);
  const dialogue = validateDialogue(input.dialogue, context);
  const proposedOps = input.proposedOps.map((operation) => validateDirectorOperation(operation, context));
  const spent = proposedOps.reduce((sum, operation) => sum + operation.budgetCost, 0);
  if (spent > context.consequenceBudget) throw new AppError(502, "LLM_BUDGET_EXCEEDED", "The model exceeded the server consequence budget.");
  return { summary, body, dialogue, proposedOps };
}

function validateNarrativeMechanicalClaims(summary, body, context) {
  const text = `${summary} ${body}`;
  if (METRIC_MECHANICAL_ASSERTION.test(text) || NARRATIVE_MECHANICAL_ASSERTION.test(text)) throw new AppError(502, "LLM_MECHANICS_CLAIM_FORBIDDEN", "Narration cannot assert protected mechanical state.");
  for (const match of text.matchAll(/(?:d20|주사위|판정)\s*[:=]?\s*(\d{1,2})/gi)) {
    if (Number(match[1]) !== context.d20) throw new AppError(502, "LLM_MECHANICS_CONTRADICTION", "Narration contradicted the authoritative D20 result.");
  }
  if (["failure", "critical_failure"].includes(context.outcome) && /(?:\b(?:the command|action|check)\s+(?:succeeds?|succeeded)\b|명령(?:은|이)?\s*성공|판정(?:은|이)?\s*성공)/i.test(text)) throw new AppError(502, "LLM_MECHANICS_CONTRADICTION", "Narration contradicted the authoritative outcome.");
  if (["success", "critical_success"].includes(context.outcome) && /(?:\b(?:the command|action|check)\s+(?:fails?|failed)\b|명령(?:은|이)?\s*실패|판정(?:은|이)?\s*실패)/i.test(text)) throw new AppError(502, "LLM_MECHANICS_CONTRADICTION", "Narration contradicted the authoritative outcome.");
}

function validateLegacyOutput(output, context) {
  const dialogue = output.dialogue === null ? null : boundedString(output.dialogue, { name: "dialogue", minimum: 1, maximum: 280, status: 502 });
  if (dialogue !== null) validateNarrativeMechanicalClaims(dialogue, "", context);
  const proposedOps = output.proposedOps.map((operation) => {
    if (!operation || typeof operation !== "object" || Array.isArray(operation)) throw new AppError(502, "LLM_OUTPUT_INVALID", "Each legacy proposed operation must be an object.");
    exactKeys(operation, ["type", "text"], "LLM_OUTPUT_INVALID", 502);
    const type = boundedString(operation.type, { name: "proposedOps.type", minimum: 1, maximum: 40, status: 502 });
    if (!LEGACY_NARRATIVE_OPS.includes(type) || !context.allowedEffects.includes(type)) throw new AppError(502, "LLM_OPERATION_FORBIDDEN", "The model proposed a non-allowlisted legacy hint.");
    const text = boundedString(operation.text, { name: "proposedOps.text", minimum: 1, maximum: 180, status: 502 });
    validateNarrativeMechanicalClaims(text, "", context);
    return { type, text };
  });
  return { ...output, dialogue, proposedOps };
}

function validateDialogue(value, context) {
  if (!Array.isArray(value) || value.length > 3) throw new AppError(502, "LLM_OUTPUT_INVALID", "dialogue must be an array of at most 3 lines.");
  return value.map((item) => {
    if (!item || typeof item !== "object" || Array.isArray(item)) throw new AppError(502, "LLM_OUTPUT_INVALID", "Each dialogue line must be an object.");
    exactKeys(item, ["speakerId", "line"], "LLM_OUTPUT_INVALID", 502);
    const speakerId = item.speakerId === null ? null : boundedString(item.speakerId, { name: "dialogue.speakerId", minimum: 1, maximum: 80, status: 502 });
    if (speakerId !== null && !context.allowedEntityIds.includes(speakerId)) throw new AppError(502, "LLM_ENTITY_FORBIDDEN", "Dialogue referenced an entity outside the provided scene.");
    const line = boundedString(item.line, { name: "dialogue.line", minimum: 1, maximum: 220, status: 502 });
    validateNarrativeMechanicalClaims(line, "", context);
    return { speakerId, line };
  });
}

function validateDirectorOperation(operation, context) {
  if (!operation || typeof operation !== "object" || Array.isArray(operation)) throw new AppError(502, "LLM_OUTPUT_INVALID", "Each proposed operation must be an object.");
  exactKeys(operation, ["op", "summary", "targetId", "slotId", "assetId", "key", "value", "delta", "ttlTurns", "importance", "questTemplateId", "budgetCost"], "LLM_OUTPUT_INVALID", 502);
  const op = boundedString(operation.op, { name: "proposedOps.op", minimum: 2, maximum: 40, status: 502 });
  if (!DIRECTOR_OPS.includes(op) || !context.allowedEffects.includes(op)) throw new AppError(502, "LLM_OPERATION_FORBIDDEN", "The model proposed an operation outside the per-turn allowlist.");
  const summary = boundedString(operation.summary, { name: "proposedOps.summary", minimum: 1, maximum: 200, status: 502 });
  const value = operation.value === undefined || operation.value === null
    ? operation.value
    : boundedString(operation.value, { name: "proposedOps.value", maximum: 400, status: 502 });
  validateNarrativeMechanicalClaims(summary, value || "", context);
  const result = { ...operation, op, summary, value, budgetCost: operation.budgetCost ?? 0 };
  if (!Number.isInteger(result.budgetCost) || result.budgetCost < 0 || result.budgetCost > 4) throw new AppError(502, "LLM_BUDGET_INVALID", "Operation budgetCost is invalid.");
  if (["ADD_NPC_MEMORY", "CHANGE_AFFINITY"].includes(op) && !context.allowedEntityIds.includes(operation.targetId)) throw new AppError(502, "LLM_ENTITY_FORBIDDEN", "The operation referenced an entity outside the provided scene.");
  if (op === "ADVANCE_QUEST" && !context.allowedQuestIds.includes(operation.targetId)) throw new AppError(502, "LLM_QUEST_FORBIDDEN", "The operation referenced an unavailable quest.");
  if (op === "START_QUEST" && context.remainingTurns <= 5) throw new AppError(502, "LLM_QUEST_HORIZON", "New quests are forbidden in the final five turns.");
  if (op === "START_QUEST" && !context.allowedQuestTemplateIds.includes(operation.questTemplateId)) throw new AppError(502, "LLM_QUEST_FORBIDDEN", "The operation referenced an unavailable generated quest template.");
  if (op === "SET_VISUAL_INTENT") {
    const slot = context.readOnlySlots.find((item) => item.id === operation.slotId);
    if (!slot || typeof value !== "string" || value.length < 1 || value.length > 160) throw new AppError(502, "LLM_SLOT_FORBIDDEN", "Visual intent must reference a provided read-only slot and contain no geometry.");
    if (operation.assetId || /(?:coordinate|position|\bx\s*=|\by\s*=|좌표|위치\s*변경)/i.test(value)) throw new AppError(502, "LLM_GEOMETRY_FORBIDDEN", "The model cannot provide assets, coordinates, or geometry.");
  }
  if (op === "BIND_SLOT_ENTITY") {
    const slot = context.placementSlots.find((item) => item.id === operation.slotId);
    if (!slot || slot.purpose !== "ambient") throw new AppError(502, "LLM_SLOT_FORBIDDEN", "Entity binding must reference an available ambient slot.");
    if (typeof operation.assetId !== "string" || !slot.allowedAssetIds?.includes(operation.assetId)) throw new AppError(502, "LLM_ASSET_FORBIDDEN", "Entity binding must use an asset allowlisted by that slot.");
    const minimumCost = slot.kind === "enemy" ? 2 : 1;
    if (result.budgetCost < minimumCost) throw new AppError(502, "LLM_BUDGET_INVALID", "Entity binding requires bounded consequence budget.");
    if (operation.targetId || /(?:coordinate|position|\bx\s*=|\by\s*=|좌표|위치\s*변경)/i.test(value || "")) throw new AppError(502, "LLM_GEOMETRY_FORBIDDEN", "The model can bind an allowlisted asset to a server slot but cannot provide coordinates.");
  }
  if (op === "SET_WORLD_FACT") {
    if (!/^run\.[a-z0-9_.-]{2,80}$/.test(operation.key || "")) throw new AppError(502, "LLM_FACT_FORBIDDEN", "Run facts must use the run.* namespace.");
    if (containsProtectedFactReference(`${operation.key} ${value || ""} ${summary}`)) throw new AppError(502, "LLM_FACT_FORBIDDEN", "Run facts cannot assert protected mechanics, authority, geometry, or ending state.");
  }
  if (op === "CHANGE_AFFINITY" && (!Number.isInteger(operation.delta) || Math.abs(operation.delta) > 5)) throw new AppError(502, "LLM_AFFINITY_FORBIDDEN", "Affinity delta exceeds the per-turn bound.");
  if (op === "CHANGE_AFFINITY" && operation.delta < 0 && result.budgetCost < 1) throw new AppError(502, "LLM_BUDGET_INVALID", "Negative affinity requires consequence budget.");
  if (op === "CREATE_HOOK" && context.remainingTurns <= 3) throw new AppError(502, "LLM_ENDING_HORIZON", "New hooks are forbidden in the final three turns.");
  return result;
}

export function createFallbackNarration(contextInput) {
  const context = contextInput?.mode ? contextInput : validateNarrationContext(contextInput);
  const korean = /[가-힣]/.test(context.intent);
  const outcomeText = korean ? {
    critical_success: "명령이 서버 규칙 안에서 가장 의도에 가깝게 실현됐다.",
    success: "세계가 합법적인 명령을 받아들였다.",
    partial_success: "명령은 작동했지만 작은 대가와 다음 선택을 남겼다.",
    failure: "판정이 난이도에 미치지 못해 확정 상태가 보존됐다.",
    critical_failure: "명령이 크게 빗나갔지만, 복구 가능한 대가와 새 단서를 남겼다."
  }[context.outcome] : {
    critical_success: "The legal command lands as close to the intent as the rules permit.",
    success: "The world accepts the legal command.",
    partial_success: "The command works, leaving a bounded cost and another choice.",
    failure: "The check misses its difficulty and preserves the confirmed state.",
    critical_failure: "The command fails sharply, but its bounded cost still creates a way forward."
  }[context.outcome];
  const urgency = context.remainingTurns <= 5 ? (korean ? " 남은 선택은 이제 결말로 수렴한다." : " Every remaining choice now converges on an ending.") : "";
  return {
    summary: korean ? `${context.area} · ${context.ability} · D20 ${context.d20}` : `${context.area} · ${context.ability} · D20 ${context.d20}`,
    body: `${context.normalizedAttempt}. ${outcomeText}${urgency}`,
    dialogue: context.mode === "legacy" ? null : [],
    proposedOps: [],
    fallbackUsed: true,
    model: FALLBACK_MODEL
  };
}

const OP_PROPERTIES = {
  op: { type: "string", enum: DIRECTOR_OPS },
  summary: { type: "string" },
  targetId: { type: ["string", "null"] },
  slotId: { type: ["string", "null"] },
  assetId: { type: ["string", "null"] },
  key: { type: ["string", "null"] },
  value: { type: ["string", "null"] },
  delta: { type: ["integer", "null"], minimum: -5, maximum: 5 },
  ttlTurns: { type: ["integer", "null"], minimum: 1, maximum: 20 },
  importance: { type: ["number", "null"], minimum: 0, maximum: 1 },
  questTemplateId: { type: ["string", "null"] },
  budgetCost: { type: "integer", minimum: 0, maximum: 4 }
};

export const DIRECTOR_RESPONSE_JSON_SCHEMA = Object.freeze({
  type: "object",
  additionalProperties: false,
  required: ["summary", "body", "dialogue", "proposedOps"],
  properties: {
    summary: { type: "string" },
    body: { type: "string" },
    dialogue: { type: "array", maxItems: 3, items: { type: "object", additionalProperties: false, required: ["speakerId", "line"], properties: { speakerId: { type: ["string", "null"] }, line: { type: "string" } } } },
    proposedOps: { type: "array", maxItems: 5, items: { type: "object", additionalProperties: false, required: ["op", "summary", "budgetCost"], properties: OP_PROPERTIES } }
  }
});

export const LEGACY_RESPONSE_JSON_SCHEMA = Object.freeze({
  type: "object",
  additionalProperties: false,
  required: ["summary", "body", "dialogue", "proposedOps"],
  properties: {
    summary: { type: "string" }, body: { type: "string" }, dialogue: { type: ["string", "null"] },
    proposedOps: { type: "array", maxItems: 5, items: { type: "object", additionalProperties: false, required: ["type", "text"], properties: { type: { type: "string", enum: LEGACY_NARRATIVE_OPS }, text: { type: "string" } } } }
  }
});

export function responseSchemaForContext(context) {
  return context.mode === "legacy" ? LEGACY_RESPONSE_JSON_SCHEMA : DIRECTOR_RESPONSE_JSON_SCHEMA;
}
