import { assert, AppError } from "../errors.js";
import { DIRECTOR_OPS } from "../domain/turn-engine.js";
import { containsProtectedFactReference } from "../domain/protected-mechanics.js";
import { choicesFromLegacySkills, NARRATIVE_CHOICE_SKILLS, NARRATIVE_INTENT_TAGS, validateNarrativeChoices } from "../domain/narrative-choices.js";

export const FALLBACK_MODEL = "deterministic-fallback-v2";
export const LEGACY_NARRATIVE_OPS = Object.freeze(["fact_hint", "rumor_hint", "npc_memory_hint", "quest_hint", "ambient_cue"]);
export const ELEMENTAL_EFFECT_IDS = Object.freeze(["ELEMENTAL_EXPLOSION", "ELEMENTAL_FLAME", "ELEMENTAL_ICE", "ELEMENTAL_ICE_B", "ELEMENTAL_ICE_FLAKE", "ELEMENTAL_PLANT", "ELEMENTAL_PLANT_B", "ELEMENTAL_ROCK", "ELEMENTAL_ROCK_B", "ELEMENTAL_ROCK_SPIKE", "ELEMENTAL_THUNDER", "ELEMENTAL_WATER", "ELEMENTAL_WATER_PILLAR"]);

const OUTCOME_VALUES = new Set(["critical_failure", "failure", "partial_success", "success", "critical_success", "narrative"]);
const ABILITY_PATTERN = /^[a-z][a-z0-9_]{1,31}$/;
const STORY_SEQUENCE_TYPES = new Set(["NARRATION", "MONOLOGUE", "DIALOGUE", "WORLD_ACTION"]);
const INTERVENTION_SKILLS = new Set(["COPY", "DELETE", "CONNECT", "RESTORE", "UNDO", "SEARCH", "SELECT_ALL"]);
const RESOLUTION_SKILLS = new Set([...NARRATIVE_CHOICE_SKILLS, "INTERACT", "ATTACK", "MOVE", "NEGOTIATE", "REST", "USE_ITEM", "COMBINE"]);
const METRIC_MECHANICAL_ASSERTION = /(?:\b(?:world[\s._-]*stability|world[\s._-]*autonomy|public[\s._-]*trust|technical[\s._-]*debt|companion[\s._-]*bond|turn[\s._-]*pressure)\b|세계\s*안정성|세계\s*자율성|공공\s*신뢰|기술\s*부채|동료\s*유대|턴\s*압박)/i;
const NARRATIVE_MECHANICAL_ASSERTION = /(?:\bprogress\s*level\b|\badmin\s*access.{0,80}\b(?:grant(?:ed)?|gain(?:ed)?|unlock(?:ed)?)\b|\bmilestone\s*token.{0,80}\b(?:grant(?:ed)?|gain(?:ed)?|receive[ds]?|award(?:ed)?|unlock(?:ed)?)\b|\bfinale\s*(?:is\s*)?(?:resolved|completed|unlocked|confirmed)\b|\bending\s*(?:is\s*)?(?:chosen|confirmed|locked|reached)\b|\b(?:hp|health|focus|damage|turn|version|reward)\s*(?::|=|is|to|became|changed)?\s*[+-]?\d+\b|\bcoordinate\s*\(|\bposition\s*(?:is|=|became|changed)\b|진행\s*(?:레벨|단계).{0,40}(?:\d+|획득|상승|확정|부여)|관리자\s*권한.{0,60}(?:획득|지급|부여|해금)|이정표\s*(?:조각|토큰|증표).{0,60}(?:획득|지급|부여|해금)|피날레.{0,40}(?:해결|완료|해금|확정)|결말.{0,40}(?:선택|확정|도달|고정)|(?:체력|집중|피해|턴|버전|보상)\s*(?::|=|은|는|이|가)?\s*[+-]?\d+|좌표\s*\(|위치\s*(?:는|가|은|이)?\s*\()/i;
const AMBIENT_PERSISTENCE_ASSERTION = /(?:(?:깨끗이|완전히|영구적으로).{0,16}(?:사라|삭제|소거|제거|정화|복구|회복|치유|해결)|(?:정화|복구|회복|치유|해결)(?:됐|되었|되니|했다|완료))/i;
const INVENTORY_ACQUISITION_ASSERTION = /(?:손에\s*(?:잡힌|넣었|들어온)|꺼내든\s*것|꺼냈다|건져냈다|주웠다|챙겼다|얻었다|획득했다|소지품에\s*(?:넣|추가))/u;
const INVENTORY_USE_ASSERTION = /(?:(?:아이템|물건|도구|약|파편).{0,24}(?:사용했다|써버렸다|소모했다)|(?:사용한|소모한)\s*(?:아이템|물건|도구))/u;
const INVENTORY_COMBINE_ASSERTION = /(?:조합(?:했|됐|되었)|합쳐(?:졌|졌다)|결합(?:했|됐|되었))/u;
const MOVEMENT_ASSERTION = /(?:목적지에\s*도착했다|그곳으로\s*이동했다|자리를\s*옮겼다)/u;
const ATTACK_ASSERTION = /(?:공격이\s*(?:적중|명중)|상대를\s*(?:베었|가격했|때렸)|일격을\s*가했다)/u;

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

function koreanPlayerText(value, name) {
  if (!/[가-힣]/u.test(value)) throw new AppError(502, "LLM_LANGUAGE_INVALID", `${name} must contain natural Korean player-facing text.`);
  return value;
}

function validateBase(input) {
  assert(Number.isInteger(input.turnNo) && input.turnNo >= 0 && input.turnNo <= 10000, 400, "NARRATION_CONTEXT_INVALID", "turnNo is invalid.");
  assert(Number.isInteger(input.remainingTurns) && input.remainingTurns >= 0 && input.remainingTurns <= 1000, 400, "NARRATION_CONTEXT_INVALID", "remainingTurns is invalid.");
  const area = boundedString(input.area, { name: "area", minimum: 1, maximum: 120 });
  const intent = boundedString(input.intent, { name: "intent", minimum: 1, maximum: 800 });
  const ability = boundedString(input.ability, { name: "ability", minimum: 2, maximum: 32 }).toLowerCase();
  assert(ABILITY_PATTERN.test(ability), 400, "NARRATION_CONTEXT_INVALID", "ability has an invalid format.");
  const resolutionMode = String(input.resolutionMode || "D20").toUpperCase();
  assert(["D20", "NONE"].includes(resolutionMode), 400, "NARRATION_CONTEXT_INVALID", "resolutionMode is invalid.");
  if (resolutionMode === "NONE") {
    assert(input.d20 === null && input.outcome === "narrative", 400, "NARRATION_CONTEXT_INVALID", "Narrative choices must not contain a D20 result.");
  } else {
    assert(Number.isInteger(input.d20) && input.d20 >= 1 && input.d20 <= 20, 400, "NARRATION_CONTEXT_INVALID", "d20 must be between 1 and 20.");
    assert(typeof input.outcome === "string" && OUTCOME_VALUES.has(input.outcome) && input.outcome !== "narrative", 400, "NARRATION_CONTEXT_INVALID", "outcome is invalid.");
  }
  const normalizedAttempt = boundedString(input.normalizedAttempt, { name: "normalizedAttempt", minimum: 1, maximum: 800 });
  return { area, intent, ability, normalizedAttempt, resolutionMode };
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
  exactKeys(input, ["schemaVersion", "requestType", "campaign", "progression", "macroPhase", "turnNo", "remainingTurns", "act", "currentStoryBeat", "currentArcQuestion", "emergentStory", "resolvedArcOutcomes", "episodeSummaries", "storyLedger", "choiceHistory", "selectedChoice", "resolutionMode", "area", "areaSummary", "spatialContext", "intent", "playerNote", "ability", "skillId", "actionContext", "normalizedAttempt", "intentAnalysis", "d20", "outcome", "dice", "consequenceBudget", "rulesetVersion", "stateHashBefore", "stateHashAfter", "allowedEffects", "allowedEntityIds", "allowedQuestIds", "allowedQuestTemplateIds", "activeQuests", "visibleEntities", "placementSlots", "readOnlyPlaces", "readOnlySlots", "geometryPolicy", "canonicalFacts", "openLoops", "rumors", "npcRelationships", "recentMemories", "majorChoices", "regionOutcomes", "abilityUsageHistory", "adminAccessHistory", "technicalDebtEntries", "unresolvedHooks", "confirmedEffects", "sceneSequence", "endingFactors"], "NARRATION_CONTEXT_INVALID");
  const base = validateBase(input);
  assert(input.schemaVersion === "2.0" && input.requestType === "TURN_NARRATION", 400, "NARRATION_CONTEXT_INVALID", "Director context version or type is invalid.");
  if (base.resolutionMode === "NONE") {
    assert(input.skillId === "NARRATIVE" && input.actionContext === "NARRATIVE", 400, "NARRATION_CONTEXT_INVALID", "Pure narrative choices require the NARRATIVE context.");
    assert(input.selectedChoice && typeof input.selectedChoice === "object" && ["DIALOGUE", "ATTITUDE"].includes(input.selectedChoice.choiceKind), 400, "NARRATION_CONTEXT_INVALID", "A pure narrative turn requires its sealed selectedChoice.");
  } else {
    assert(RESOLUTION_SKILLS.has(input.skillId), 400, "NARRATION_CONTEXT_INVALID", "skillId must be a server resolution action.");
    assert(["COMBAT", "INVESTIGATION", "NEGOTIATION", "DEPLOYMENT"].includes(input.actionContext), 400, "NARRATION_CONTEXT_INVALID", "actionContext must be server classified.");
  }
  assert(input.playerNote === null || (typeof input.playerNote === "string" && input.playerNote.length >= 1 && input.playerNote.length <= 400), 400, "NARRATION_CONTEXT_INVALID", "playerNote must be null or bounded optional flavor text.");
  assert(Number.isInteger(input.consequenceBudget) && input.consequenceBudget >= 0 && input.consequenceBudget <= 4, 400, "NARRATION_CONTEXT_INVALID", "consequenceBudget is invalid.");
  assert(Array.isArray(input.allowedEffects) && input.allowedEffects.every((effect) => DIRECTOR_OPS.includes(effect)), 400, "NARRATION_EFFECT_FORBIDDEN", "Director allowedEffects contains an unsupported operation.");
  assert(input.campaign && typeof input.campaign === "object" && typeof input.campaign.title === "string" && typeof input.campaign.premise === "string", 400, "NARRATION_CONTEXT_INVALID", "campaign must contain the sealed run premise.");
  assert(input.progression && typeof input.progression === "object" && Number.isInteger(input.progression.level) && Array.isArray(input.progression.tokens), 400, "NARRATION_CONTEXT_INVALID", "progression must contain server-owned administrator-access state.");
  assert(input.emergentStory === null || (input.emergentStory && typeof input.emergentStory === "object"), 400, "NARRATION_CONTEXT_INVALID", "emergentStory must be null or an object.");
  assert(input.spatialContext && input.spatialContext.authority === "SERVER" && Array.isArray(input.spatialContext.nearbyEntities)
    && Array.isArray(input.spatialContext.availableDestinations), 400, "NARRATION_CONTEXT_INVALID", "spatialContext must be server-derived semantic space.");
  assert(input.macroPhase && typeof input.macroPhase.id === "string" && Number.isInteger(input.macroPhase.order), 400, "NARRATION_CONTEXT_INVALID", "macroPhase must contain the immutable seven-phase story state.");
  assert(typeof input.rulesetVersion === "string" && input.rulesetVersion.length >= 3 && input.rulesetVersion.length <= 80, 400, "NARRATION_CONTEXT_INVALID", "rulesetVersion is invalid.");
  assert(/^[0-9a-f]{64}$/.test(input.stateHashBefore || "") && /^[0-9a-f]{64}$/.test(input.stateHashAfter || ""), 400, "NARRATION_CONTEXT_INVALID", "Director state hashes must be canonical SHA-256 values.");
  for (const field of ["resolvedArcOutcomes", "episodeSummaries", "storyLedger", "choiceHistory", "allowedEntityIds", "allowedQuestIds", "allowedQuestTemplateIds", "activeQuests", "visibleEntities", "placementSlots", "readOnlyPlaces", "readOnlySlots", "canonicalFacts", "openLoops", "rumors", "npcRelationships", "recentMemories", "majorChoices", "regionOutcomes", "abilityUsageHistory", "adminAccessHistory", "technicalDebtEntries", "unresolvedHooks", "confirmedEffects", "sceneSequence"]) assert(Array.isArray(input[field]), 400, "NARRATION_CONTEXT_INVALID", `${field} must be an array.`);
  assert(input.intentAnalysis && typeof input.intentAnalysis === "object" && typeof input.intentAnalysis.score === "number" && input.intentAnalysis.score >= 0 && input.intentAnalysis.score <= 1, 400, "NARRATION_CONTEXT_INVALID", "intentAnalysis is invalid.");
  assert(input.geometryPolicy === "read_only_ids_and_visual_intent_only", 400, "NARRATION_CONTEXT_INVALID", "geometryPolicy must forbid model geometry mutation.");
  assert(input.activeQuests.length <= 6 && input.visibleEntities.length <= 32 && input.placementSlots.length <= 8 && input.readOnlyPlaces.length <= 24 && input.readOnlySlots.length <= 12 && input.canonicalFacts.length <= 16
    && input.resolvedArcOutcomes.length <= 5 && input.episodeSummaries.length <= 5 && input.storyLedger.length <= 12 && input.choiceHistory.length <= 12
    && input.majorChoices.length <= 8 && input.regionOutcomes.length <= 6 && input.abilityUsageHistory.length <= 8 && input.adminAccessHistory.length <= 3 && input.technicalDebtEntries.length <= 8 && input.unresolvedHooks.length <= 8 && input.confirmedEffects.length <= 32 && input.sceneSequence.length <= 16,
  400, "NARRATION_CONTEXT_INVALID", "Director context exceeds its bounded collection limits.");
  return { mode: "director", ...input, ...base, allowedEffects: [...new Set(input.allowedEffects)] };
}

// A small local model reliably produces structurally valid JSON but occasionally fills fields
// with values that violate the game's semantic rules in cosmetic, non-authoritative ways:
// choiceIds like "1"/"2" instead of the required identifier format, or a WORLD_ACTION beat on a
// turn with no confirmed server action to back it. Rather than reject the whole turn to the
// deterministic fallback for these, normalize only the presentation so the rest of the output can
// still reach Unity. This never fabricates or hides game state — authority checks (mechanics,
// inventory, entity references, budget) still run afterward in validateNarrationOutput and any
// genuine violation there still falls back.
export function repairDirectorOutput(input, contextInput) {
  const context = contextInput?.mode ? contextInput : validateNarrationContext(contextInput);
  if (context.mode !== "director" || !input || typeof input !== "object" || Array.isArray(input)) return input;
  const output = { ...input };
  const player = (context.visibleEntities || []).find((item) => item.kind === "player");
  const allowedActionIds = new Set((context.sceneSequence || []).map((action) => action.actionId).filter(Boolean));
  if (Array.isArray(output.storySequence)) {
    output.storySequence = output.storySequence.map((beat) => {
      if (!beat || typeof beat !== "object" || Array.isArray(beat)) return beat;
      const next = { ...beat };
      const type = typeof next.type === "string" ? next.type.toUpperCase() : next.type;
      // A world action with no confirmed server action behind it is just narration.
      if (type === "WORLD_ACTION" && (!next.actionId || !allowedActionIds.has(next.actionId))) {
        next.type = "NARRATION";
        next.actionId = null;
        next.speakerId = null;
      } else if (type === "NARRATION") {
        next.speakerId = null;
      } else if (type === "MONOLOGUE" && next.speakerId && next.speakerId !== player?.id) {
        // The model tends to write PROTAGONIST_NUPJUKYI here; the monologue is the player's regardless.
        next.speakerId = null;
      }
      return next;
    });
  }
  const choices = output.nextIntervention?.choices;
  if (Array.isArray(choices) && choices.length > 0) {
    const allValid = choices.every((choice) => typeof choice?.choiceId === "string" && /^[a-z][a-z0-9_.:-]{2,63}$/.test(choice.choiceId));
    const allUnique = new Set(choices.map((choice) => choice?.choiceId)).size === choices.length;
    if (!allValid || !allUnique) {
      // choiceId is an opaque per-set identifier, so reassigning it changes nothing the player sees.
      output.nextIntervention = { ...output.nextIntervention, choices: choices.map((choice, index) => ({ ...choice, choiceId: `choice.${index + 1}` })) };
    }
  }
  return output;
}

export function validateNarrationOutput(input, contextInput) {
  const context = contextInput.mode ? contextInput : validateNarrationContext(contextInput);
  assert(input && typeof input === "object" && !Array.isArray(input), 502, "LLM_OUTPUT_INVALID", "Narration output must be a JSON object.");
  exactKeys(input, ["summary", "body", "dialogue", "proposedOps", "storySequence", "nextIntervention", "elementalEffectId"], "LLM_OUTPUT_INVALID", 502);
  const summary = boundedString(input.summary, { name: "summary", minimum: 1, maximum: 160, status: 502 });
  const body = boundedString(input.body, { name: "body", minimum: 1, maximum: 700, status: 502 });
  koreanPlayerText(summary, "summary");
  koreanPlayerText(body, "body");
  if (context.mode === "director") {
    const sentenceCount = body.split(/(?<=[.!?。！？])\s*/u).map((item) => item.trim()).filter(Boolean).length;
    assert(sentenceCount >= 2 && sentenceCount <= 4, 502, "LLM_NARRATIVE_LENGTH_INVALID", "Turn narration body must contain 2-4 sentences.");
  }
  validateNarrativeMechanicalClaims(summary, body, context);
  if (!Array.isArray(input.proposedOps) || input.proposedOps.length > 5) throw new AppError(502, "LLM_OUTPUT_INVALID", "proposedOps must contain at most 5 items.");
  if (context.mode === "legacy") return validateLegacyOutput({ summary, body, dialogue: input.dialogue, proposedOps: input.proposedOps }, context);
  const dialogue = validateDialogue(input.dialogue, context);
  const storySequence = validateStorySequence(input.storySequence, { ...context, body, dialogue });
  const nextIntervention = validateNextIntervention(input.nextIntervention, context);
  const proposedOps = input.proposedOps.map((operation) => validateDirectorOperation(operation, context));
  const spent = proposedOps.reduce((sum, operation) => sum + operation.budgetCost, 0);
  if (spent > context.consequenceBudget) throw new AppError(502, "LLM_BUDGET_EXCEEDED", "The model exceeded the server consequence budget.");
  const elementalEffectId = input.elementalEffectId === null || input.elementalEffectId === undefined
    ? null
    : boundedString(input.elementalEffectId, { name: "elementalEffectId", minimum: 1, maximum: 40, status: 502 });
  if (elementalEffectId !== null && !ELEMENTAL_EFFECT_IDS.includes(elementalEffectId)) throw new AppError(502, "LLM_EFFECT_FORBIDDEN", "Unknown Elemental effect.");
  if (elementalEffectId !== null && !["ATTACK", "DELETE", "SELECT_ALL"].includes(context.skillId)) throw new AppError(502, "LLM_EFFECT_FORBIDDEN", "Elemental effects are restricted to attack skills.");
  return { summary, body, dialogue, storySequence, nextIntervention, proposedOps, elementalEffectId };
}

function validateStorySequence(value, context) {
  const player = context.visibleEntities.find((item) => item.kind === "player");
  const source = value === undefined
    ? [{ type: "MONOLOGUE", speakerId: player?.id || null, text: context.body }, ...context.dialogue.map((item) => ({ type: "DIALOGUE", speakerId: item.speakerId, text: item.line }))]
    : value;
  if (!Array.isArray(source) || source.length < 1 || source.length > 8) throw new AppError(502, "LLM_STORY_SEQUENCE_INVALID", "storySequence must contain 1-8 ordered beats.");
  return source.map((item) => {
    if (!item || typeof item !== "object" || Array.isArray(item)) throw new AppError(502, "LLM_STORY_SEQUENCE_INVALID", "Each story beat must be an object.");
    exactKeys(item, ["type", "speakerId", "actionId", "text"], "LLM_STORY_SEQUENCE_INVALID", 502);
    const type = boundedString(item.type, { name: "storySequence.type", minimum: 1, maximum: 20, status: 502 }).toUpperCase();
    if (!STORY_SEQUENCE_TYPES.has(type)) throw new AppError(502, "LLM_STORY_SEQUENCE_INVALID", "Unknown story beat type.");
    const speakerId = item.speakerId === null || item.speakerId === undefined ? null : boundedString(item.speakerId, { name: "storySequence.speakerId", minimum: 1, maximum: 80, status: 502 });
    const actionId = item.actionId === null || item.actionId === undefined ? null : boundedString(item.actionId, { name: "storySequence.actionId", minimum: 1, maximum: 80, status: 502 });
    if (type === "DIALOGUE" && (!speakerId || !context.allowedEntityIds.includes(speakerId) || speakerId === player?.id)) throw new AppError(502, "LLM_ENTITY_FORBIDDEN", "Dialogue must use a visible non-player speaker.");
    if (type === "MONOLOGUE" && speakerId && speakerId !== player?.id) throw new AppError(502, "LLM_ENTITY_FORBIDDEN", "Monologue can only belong to the player.");
    if (["NARRATION", "WORLD_ACTION"].includes(type) && speakerId !== null) throw new AppError(502, "LLM_STORY_SEQUENCE_INVALID", "Narration and world action cannot have a speaker.");
    const allowedActionIds = new Set(context.sceneSequence.map((action) => action.actionId).filter(Boolean));
    if (type === "WORLD_ACTION" && (!actionId || !allowedActionIds.has(actionId))) throw new AppError(502, "LLM_STORY_ACTION_FORBIDDEN", "World action must reference one confirmed server action.");
    if (type !== "WORLD_ACTION" && actionId !== null) throw new AppError(502, "LLM_STORY_SEQUENCE_INVALID", "Only world action beats may reference an actionId.");
    const text = koreanPlayerText(boundedString(item.text, { name: "storySequence.text", minimum: 1, maximum: 320, status: 502 }), "storySequence.text");
    validateNarrativeMechanicalClaims(text, "", context);
    return { type, speakerId, actionId, text };
  });
}

function validateNextIntervention(value, context) {
  const source = value === undefined ? { reason: "이 장면을 지켜본 뒤, 넙죽이의 다음 개입이 필요하다.", suggestedSkillIds: [context.skillId] } : value;
  if (!source || typeof source !== "object" || Array.isArray(source)) throw new AppError(502, "LLM_INTERVENTION_INVALID", "nextIntervention must be an object.");
  exactKeys(source, ["reason", "choices", "suggestedSkillIds"], "LLM_INTERVENTION_INVALID", 502);
  const reason = koreanPlayerText(boundedString(source.reason, { name: "nextIntervention.reason", minimum: 1, maximum: 220, status: 502 }), "nextIntervention.reason");
  const legacySkills = Array.isArray(source.suggestedSkillIds)
    ? [...new Set(source.suggestedSkillIds.map((skill) => String(skill).toUpperCase()))]
    : [];
  if (!legacySkills.every((skill) => INTERVENTION_SKILLS.has(skill))) throw new AppError(502, "LLM_INTERVENTION_INVALID", "nextIntervention contains an unknown skill.");
  const choices = validateNarrativeChoices(source.choices || choicesFromLegacySkills(legacySkills, context.skillId), {
    status: 502,
    code: "LLM_INTERVENTION_INVALID",
    allowedEntityIds: context.allowedEntityIds,
    allowedDestinationRefs: context.readOnlyPlaces.map((place) => place.id),
    allowTravel: false
  });
  const suggestedSkillIds = [...new Set(choices.filter((choice) => choice.choiceKind === "SKILL").map((choice) => choice.skillId))];
  return { reason, choices, suggestedSkillIds };
}

function validateNarrativeMechanicalClaims(summary, body, context) {
  const text = `${summary} ${body}`;
  if (METRIC_MECHANICAL_ASSERTION.test(text) || NARRATIVE_MECHANICAL_ASSERTION.test(text)) throw new AppError(502, "LLM_MECHANICS_CLAIM_FORBIDDEN", "Narration cannot assert protected mechanical state.");
  if (/\bambient\b/i.test(context.normalizedAttempt || "") && AMBIENT_PERSISTENCE_ASSERTION.test(text)) throw new AppError(502, "LLM_AMBIENT_PERSISTENCE_FORBIDDEN", "Ambient narration cannot claim a persistent world result.");
  for (const match of text.matchAll(/(?:d20|주사위|판정)\s*[:=]?\s*(\d{1,2})/gi)) {
    if (Number(match[1]) !== context.d20) throw new AppError(502, "LLM_MECHANICS_CONTRADICTION", "Narration contradicted the authoritative D20 result.");
  }
  if (["failure", "critical_failure"].includes(context.outcome) && /(?:\b(?:the command|action|check)\s+(?:succeeds?|succeeded)\b|명령(?:은|이)?\s*성공|판정(?:은|이)?\s*성공)/i.test(text)) throw new AppError(502, "LLM_MECHANICS_CONTRADICTION", "Narration contradicted the authoritative outcome.");
  if (["success", "critical_success"].includes(context.outcome) && /(?:\b(?:the command|action|check)\s+(?:fails?|failed)\b|명령(?:은|이)?\s*실패|판정(?:은|이)?\s*실패)/i.test(text)) throw new AppError(502, "LLM_MECHANICS_CONTRADICTION", "Narration contradicted the authoritative outcome.");
  const effects = context.confirmedEffects || [];
  const hasEffect = (pattern) => effects.some((effect) => pattern.test(String(effect?.type || "")));
  if (!hasEffect(/inventory_item_acquired/i) && INVENTORY_ACQUISITION_ASSERTION.test(text)) throw new AppError(502, "LLM_INVENTORY_CLAIM_FORBIDDEN", "Narration claimed an item acquisition without a confirmed inventory event.");
  if (!hasEffect(/inventory_item_used/i) && INVENTORY_USE_ASSERTION.test(text)) throw new AppError(502, "LLM_INVENTORY_CLAIM_FORBIDDEN", "Narration claimed item use without a confirmed inventory event.");
  if (!hasEffect(/inventory_items_combined/i) && INVENTORY_COMBINE_ASSERTION.test(text)) throw new AppError(502, "LLM_INVENTORY_CLAIM_FORBIDDEN", "Narration claimed item combination without a confirmed inventory event.");
  if (!hasEffect(/entity_moved/i) && MOVEMENT_ASSERTION.test(text)) throw new AppError(502, "LLM_MOVEMENT_CLAIM_FORBIDDEN", "Narration claimed movement without a confirmed movement event.");
  if (!hasEffect(/health_changed/i) && ATTACK_ASSERTION.test(text)) throw new AppError(502, "LLM_ATTACK_CLAIM_FORBIDDEN", "Narration claimed a landed attack without a confirmed health event.");
}

function validateLegacyOutput(output, context) {
  const dialogue = output.dialogue === null ? null : boundedString(output.dialogue, { name: "dialogue", minimum: 1, maximum: 280, status: 502 });
  if (dialogue !== null) {
    koreanPlayerText(dialogue, "dialogue");
    validateNarrativeMechanicalClaims(dialogue, "", context);
  }
  const proposedOps = output.proposedOps.map((operation) => {
    if (!operation || typeof operation !== "object" || Array.isArray(operation)) throw new AppError(502, "LLM_OUTPUT_INVALID", "Each legacy proposed operation must be an object.");
    exactKeys(operation, ["type", "text"], "LLM_OUTPUT_INVALID", 502);
    const type = boundedString(operation.type, { name: "proposedOps.type", minimum: 1, maximum: 40, status: 502 });
    if (!LEGACY_NARRATIVE_OPS.includes(type) || !context.allowedEffects.includes(type)) throw new AppError(502, "LLM_OPERATION_FORBIDDEN", "The model proposed a non-allowlisted legacy hint.");
    const text = koreanPlayerText(boundedString(operation.text, { name: "proposedOps.text", minimum: 1, maximum: 180, status: 502 }), "proposedOps.text");
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
    if (speakerId === null) throw new AppError(502, "LLM_DIALOGUE_SPEAKER_REQUIRED", "Director dialogue must identify a visible NPC speaker.");
    if (speakerId !== null && !context.allowedEntityIds.includes(speakerId)) throw new AppError(502, "LLM_ENTITY_FORBIDDEN", "Dialogue referenced an entity outside the provided scene.");
    const line = koreanPlayerText(boundedString(item.line, { name: "dialogue.line", minimum: 1, maximum: 220, status: 502 }), "dialogue.line");
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
  const outcomeText = {
    critical_success: "!! 예상보다 선명한 흔적을 붙잡았어. 이 발견이 다음 장면을 크게 바꾸겠군.",
    success: "좋아, 분명한 변화가 생겼어. 이 흔적이 어디로 이어지는지 지켜보자.",
    partial_success: "변화는 만들었지만 찜찜한 반동이 남았어. 이 대가는 나중에 다시 돌아오겠군.",
    failure: "……아무것도 붙잡지 못했어. 괜히 힘만 빠졌군.",
    critical_failure: "……완전히 빗나갔어. 이 반동은 쉽게 끝나지 않겠군.",
    narrative: "내가 고른 말이 장면의 공기를 조금 바꿨어. 이제 상대가 어떻게 받아들이는지 지켜보자."
  }[context.outcome];
  const abilityLabel = { copy: "복제", delete: "삭제", connect: "연결", restore: "복구", undo: "되돌리기",
    search: "조사", select_all: "광역 선택", interact: "상호작용", attack: "공격", negotiate: "협상", rest: "휴식" }[context.ability]
    || "키보드 명령";
  const urgency = context.remainingTurns <= 5 ? " 남은 선택은 이제 결말로 수렴한다." : "";
  const confirmedSceneText = context.mode === "director"
    ? context.sceneSequence.map((item) => item?.text).filter(Boolean).slice(0, 2).join(" ")
    : "";
  const confirmedEffects = context.mode === "director" ? context.confirmedEffects || [] : [];
  const acquired = confirmedEffects.find((effect) => effect.type === "inventory_item_acquired");
  const used = confirmedEffects.find((effect) => effect.type === "inventory_item_used");
  const combined = confirmedEffects.find((effect) => effect.type === "inventory_items_combined");
  const moved = confirmedEffects.find((effect) => effect.type === "entity_moved");
  const healthChanged = confirmedEffects.find((effect) => effect.type === "health_changed" && Number(effect.delta) < 0);
  const rejected = confirmedEffects.find((effect) => effect.type === "player_action_rejected");
  const confirmedEffectText = rejected
    ? `시도해 봤지만 뜻대로 되지 않는다. ${rejected.reason} 다른 방법을 찾아봐야겠다.`
    : acquired
    ? `손을 빼자 ${acquired.itemName}이 손에 남았다. 이제 이 물건이 장면에 어떤 변화를 만들지 확인해 봐야겠다.`
    : combined
      ? `${combined.resultItemName} 조합을 완성했다. 두 재료가 만든 결과를 다음 행동에서 확인해 봐야겠다.`
      : used
        ? `${used.itemName}을 이 행동에 사용했다. 그 결과가 다음 장면에 어떻게 이어질지 지켜봐야겠다.`
        : moved
          ? "원한 곳으로 자리를 옮겼다. 새 위치에서 무엇이 달라졌는지 살펴봐야겠다."
          : healthChanged
            ? "공격이 실제 충격으로 이어졌다. 상대의 다음 반응을 경계해야겠다."
            : "";
  const fallbackBody = confirmedEffectText || (confirmedSceneText
    ? `${abilityLabel} 결과가 확정됐다. ${confirmedSceneText} 이제 이 변화가 다음 선택에 어떤 의미인지 살펴봐야겠다.${urgency}`
    : `${outcomeText}${urgency || " 무엇이 달라졌는지 조금 더 지켜보자."}`);
  const effectSummary = rejected ? `${rejected.actionKind} 시도 실패`
    : acquired ? `${acquired.itemName} 획득`
    : combined ? `${combined.resultItemName} 조합`
      : used ? `${used.itemName} 사용` : `${abilityLabel} 뒤에 남은 생각`;
  return {
    summary: effectSummary,
    body: fallbackBody,
    dialogue: context.mode === "legacy" ? null : [],
    ...(context.mode === "director" ? {
      storySequence: [
        ...context.sceneSequence.filter((item) => item.actionId && item.type !== "DIALOGUE").slice(0, 4).map((item) => ({ type: "WORLD_ACTION", speakerId: null, actionId: item.actionId, text: item.text || "주변 세계가 선택의 결과에 반응했다." })),
        { type: "MONOLOGUE", speakerId: null, actionId: null, text: confirmedEffectText || (confirmedSceneText ? `확정된 변화를 확인했다. 이제 이 결과의 의미를 살펴봐야겠다.${urgency}` : `${outcomeText}${urgency || " 무엇이 달라졌는지 조금 더 지켜보자."}`) }
      ],
      nextIntervention: {
        reason: "장면이 잠잠해졌다. 넙죽이의 다음 반응을 선택한다.",
        choices: choicesFromLegacySkills([], context.skillId),
        suggestedSkillIds: NARRATIVE_CHOICE_SKILLS.includes(context.skillId) ? [context.skillId] : ["SEARCH"]
      }
    } : {}),
    elementalEffectId: context.mode === "director" && context.skillId === "DELETE" ? "ELEMENTAL_ROCK_SPIKE"
      : context.mode === "director" && context.skillId === "SELECT_ALL" ? "ELEMENTAL_EXPLOSION"
        : context.mode === "director" && context.skillId === "ATTACK" ? "ELEMENTAL_FLAME" : null,
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
  required: ["summary", "body", "dialogue", "storySequence", "nextIntervention", "proposedOps", "elementalEffectId"],
  properties: {
    summary: { type: "string" },
    body: { type: "string" },
    dialogue: { type: "array", maxItems: 3, items: { type: "object", additionalProperties: false, required: ["speakerId", "line"], properties: { speakerId: { type: ["string", "null"] }, line: { type: "string" } } } },
    storySequence: { type: "array", minItems: 1, maxItems: 8, items: { type: "object", additionalProperties: false, required: ["type", "speakerId", "actionId", "text"], properties: { type: { type: "string", enum: ["NARRATION", "MONOLOGUE", "DIALOGUE", "WORLD_ACTION"] }, speakerId: { type: ["string", "null"] }, actionId: { type: ["string", "null"] }, text: { type: "string" } } } },
    nextIntervention: {
      type: "object",
      additionalProperties: false,
      required: ["reason", "choices"],
      properties: {
        reason: { type: "string" },
        choices: {
          type: "array", minItems: 2, maxItems: 4,
          items: {
            type: "object", additionalProperties: false,
            required: ["choiceId", "text", "choiceKind", "intentTag", "resolutionMode", "skillId", "targetEntityId", "destinationRef"],
            properties: {
              choiceId: { type: "string" }, text: { type: "string" },
              choiceKind: { type: "string", enum: ["DIALOGUE", "ATTITUDE", "SKILL"] },
              intentTag: { type: "string", enum: NARRATIVE_INTENT_TAGS },
              resolutionMode: { type: "string", enum: ["NONE", "D20"] },
              skillId: { type: ["string", "null"], enum: [null, ...NARRATIVE_CHOICE_SKILLS] },
              targetEntityId: { type: ["string", "null"] },
              destinationRef: { type: "null" }
            }
          }
        },
        suggestedSkillIds: { type: "array", minItems: 0, maxItems: 4, items: { type: "string", enum: NARRATIVE_CHOICE_SKILLS } }
      }
    },
    elementalEffectId: { type: ["string", "null"], enum: [...ELEMENTAL_EFFECT_IDS, null] },
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
  const base = context.mode === "legacy" ? LEGACY_RESPONSE_JSON_SCHEMA : DIRECTOR_RESPONSE_JSON_SCHEMA;
  const allowed = Array.isArray(context.allowedEffects) ? [...new Set(context.allowedEffects)] : [];
  const baseOps = base.properties.proposedOps;
  const baseItems = baseOps.items;
  const constrainedItems = context.mode === "legacy"
    ? {
        ...baseItems,
        properties: {
          ...baseItems.properties,
          type: allowed.length > 0 ? { type: "string", enum: allowed } : baseItems.properties.type
        }
      }
    : {
        ...baseItems,
        properties: {
          ...baseItems.properties,
          op: allowed.length > 0 ? { type: "string", enum: allowed } : baseItems.properties.op
        }
      };
  const actionIds = context.mode === "director" ? context.sceneSequence.map((item) => item.actionId).filter(Boolean) : [];
  const rejectedAction = context.mode === "director" && context.confirmedEffects.some((item) => item.type === "player_action_rejected");
  const successfulAction = context.mode === "director" && ["partial_success", "success", "critical_success"].includes(context.outcome);
  const minimumStoryBeats = rejectedAction ? 3 : successfulAction ? 4 : context.mode === "director" ? 3 : 1;
  const storySequence = context.mode === "director" ? {
    ...base.properties.storySequence,
    minItems: minimumStoryBeats,
    items: {
      ...base.properties.storySequence.items,
      properties: {
        ...base.properties.storySequence.items.properties,
        actionId: actionIds.length > 0 ? { type: ["string", "null"], enum: [null, ...actionIds] } : { type: "null" }
      }
    }
  } : undefined;
  const nextIntervention = context.mode === "director" ? {
    ...base.properties.nextIntervention,
    properties: {
      ...base.properties.nextIntervention.properties,
      choices: {
        ...base.properties.nextIntervention.properties.choices,
        items: {
          ...base.properties.nextIntervention.properties.choices.items,
          properties: {
            ...base.properties.nextIntervention.properties.choices.items.properties,
            targetEntityId: context.allowedEntityIds.length > 0
              ? { type: ["string", "null"], enum: [null, ...context.allowedEntityIds] }
              : { type: "null" }
          }
        }
      }
    }
  } : undefined;
  // Elemental effects are legal only for attack skills, so structurally forbid them elsewhere
  // instead of letting the model emit one and failing post-hoc validation.
  const elementalEffectId = context.mode === "director" && !["ATTACK", "DELETE", "SELECT_ALL"].includes(context.skillId)
    ? { type: "null" }
    : undefined;
  return {
    ...base,
    properties: {
      ...base.properties,
      ...(storySequence ? { storySequence } : {}),
      ...(nextIntervention ? { nextIntervention } : {}),
      ...(elementalEffectId ? { elementalEffectId } : {}),
      proposedOps: {
        ...baseOps,
        maxItems: allowed.length > 0 ? baseOps.maxItems : 0,
        items: constrainedItems
      }
    }
  };
}
