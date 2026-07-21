import { assert, AppError } from "../errors.js";
import { clone, deterministicUuid, fingerprint } from "./serialization.js";

export const NARRATIVE_CHOICE_KINDS = Object.freeze(["DIALOGUE", "ATTITUDE", "SKILL", "TRAVEL"]);
export const NARRATIVE_RESOLUTION_MODES = Object.freeze(["NONE", "D20"]);
export const NARRATIVE_INTENT_TAGS = Object.freeze([
  "CURIOUS", "EMPATHETIC", "CAUTIOUS", "ASSERTIVE", "PLAYFUL",
  "INVESTIGATE", "PROTECT", "WITHDRAW", "TRAVEL"
]);
export const NARRATIVE_CHOICE_SKILLS = Object.freeze(["COPY", "DELETE", "CONNECT", "RESTORE", "UNDO", "SEARCH", "SELECT_ALL"]);

const CHOICE_ID_PATTERN = /^[a-z][a-z0-9_.:-]{2,63}$/;
const CHOICE_SET_ID_PATTERN = /^[0-9a-f]{8}-[0-9a-f]{4}-[1-8][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i;
const IDEMPOTENCY_PATTERN = /^[A-Za-z0-9][A-Za-z0-9_.:-]{7,127}$/;
const SKILL_LABELS = Object.freeze({
  COPY: "복제", DELETE: "삭제", CONNECT: "연결", RESTORE: "복구",
  UNDO: "되돌리기", SEARCH: "조사", SELECT_ALL: "광역 선택"
});

function exactKeys(object, allowed, code, status) {
  const unknown = Object.keys(object).filter((key) => !allowed.includes(key));
  if (unknown.length > 0) throw new AppError(status, code, `Unknown fields: ${unknown.join(", ")}.`);
}

function boundedString(value, name, minimum, maximum, status, code) {
  if (typeof value !== "string") throw new AppError(status, code, `${name} must be a string.`);
  const normalized = value.trim();
  if (normalized.length < minimum || normalized.length > maximum) throw new AppError(status, code, `${name} is outside its length limit.`);
  return normalized;
}

function nullableReference(value, name, maximum, status, code) {
  if (value === undefined || value === null) return null;
  return boundedString(value, name, 1, maximum, status, code);
}

export function validateNarrativeChoices(value, {
  status = 502,
  code = "LLM_INTERVENTION_INVALID",
  allowedEntityIds = [],
  allowedDestinationRefs = [],
  allowTravel = true
} = {}) {
  if (!Array.isArray(value) || value.length < 2 || value.length > 4) {
    throw new AppError(status, code, "choices must contain 2-4 options.");
  }
  const entityIds = new Set(allowedEntityIds);
  const destinationRefs = new Set(allowedDestinationRefs);
  const choices = value.map((item, index) => {
    if (!item || typeof item !== "object" || Array.isArray(item)) throw new AppError(status, code, `choices[${index}] must be an object.`);
    exactKeys(item, ["choiceId", "text", "choiceKind", "intentTag", "resolutionMode", "skillId", "targetEntityId", "destinationRef"], code, status);
    const choiceId = boundedString(item.choiceId, `choices[${index}].choiceId`, 3, 64, status, code).toLowerCase();
    if (!CHOICE_ID_PATTERN.test(choiceId)) throw new AppError(status, code, `choices[${index}].choiceId has an invalid format.`);
    const text = boundedString(item.text, `choices[${index}].text`, 2, 180, status, code);
    if (!/[가-힣]/u.test(text)) throw new AppError(status, code, `choices[${index}].text must contain natural Korean.`);
    const choiceKind = boundedString(item.choiceKind, `choices[${index}].choiceKind`, 1, 20, status, code).toUpperCase();
    const intentTag = boundedString(item.intentTag, `choices[${index}].intentTag`, 1, 24, status, code).toUpperCase();
    const resolutionMode = boundedString(item.resolutionMode, `choices[${index}].resolutionMode`, 1, 8, status, code).toUpperCase();
    const skillId = nullableReference(item.skillId, `choices[${index}].skillId`, 32, status, code)?.toUpperCase() || null;
    const targetEntityId = nullableReference(item.targetEntityId, `choices[${index}].targetEntityId`, 80, status, code);
    const destinationRef = nullableReference(item.destinationRef, `choices[${index}].destinationRef`, 120, status, code);
    if (!NARRATIVE_CHOICE_KINDS.includes(choiceKind)) throw new AppError(status, code, `choices[${index}] has an unknown choiceKind.`);
    if (!NARRATIVE_INTENT_TAGS.includes(intentTag)) throw new AppError(status, code, `choices[${index}] has an unknown intentTag.`);
    if (!NARRATIVE_RESOLUTION_MODES.includes(resolutionMode)) throw new AppError(status, code, `choices[${index}] has an unknown resolutionMode.`);
    if (targetEntityId !== null && !entityIds.has(targetEntityId)) throw new AppError(status, code, `choices[${index}] references an entity outside the visible scene.`);
    if (choiceKind === "SKILL") {
      if (resolutionMode !== "D20" || !NARRATIVE_CHOICE_SKILLS.includes(skillId)) throw new AppError(status, code, "SKILL choices require an allowed skillId and D20 resolution.");
      if (destinationRef !== null) throw new AppError(status, code, "SKILL choices cannot carry a destinationRef.");
    } else if (choiceKind === "TRAVEL") {
      if (!allowTravel) throw new AppError(status, code, "TRAVEL choices are not enabled for generated interventions yet.");
      if (resolutionMode !== "NONE" || skillId !== null || !destinationRef || !destinationRefs.has(destinationRef)) throw new AppError(status, code, "TRAVEL choices require one supplied destinationRef and NONE resolution.");
    } else if (resolutionMode !== "NONE" || skillId !== null || destinationRef !== null) {
      throw new AppError(status, code, "DIALOGUE and ATTITUDE choices require NONE resolution and no skill or destination.");
    }
    return { choiceId, text, choiceKind, intentTag, resolutionMode, skillId, targetEntityId, destinationRef };
  });
  if (new Set(choices.map((choice) => choice.choiceId)).size !== choices.length) throw new AppError(status, code, "choiceId values must be unique within a choice set.");
  if (new Set(choices.map((choice) => choice.text)).size !== choices.length) throw new AppError(status, code, "Choice text must be meaningfully distinct.");
  if (!choices.some((choice) => choice.choiceKind !== "SKILL")) throw new AppError(status, code, "At least one non-skill narrative choice is required.");
  return choices;
}

export function choicesFromLegacySkills(skillIds = [], contextSkill = null) {
  const normalized = [...new Set([...(Array.isArray(skillIds) ? skillIds : []), contextSkill]
    .filter(Boolean).map((skill) => String(skill).toUpperCase()).filter((skill) => NARRATIVE_CHOICE_SKILLS.includes(skill)))].slice(0, 3);
  if (normalized.length === 0) normalized.push("SEARCH");
  return [
    {
      choiceId: "continue.cautiously",
      text: "조금 더 지켜보며 지금 상황을 이해해 보자.",
      choiceKind: "ATTITUDE",
      intentTag: "CAUTIOUS",
      resolutionMode: "NONE",
      skillId: null,
      targetEntityId: null,
      destinationRef: null
    },
    ...normalized.map((skillId) => ({
      choiceId: `skill.${skillId.toLowerCase()}`,
      text: `${SKILL_LABELS[skillId]} 명령으로 이 장면에 개입한다.`,
      choiceKind: "SKILL",
      intentTag: skillId === "CONNECT" ? "EMPATHETIC" : "INVESTIGATE",
      resolutionMode: "D20",
      skillId,
      targetEntityId: null,
      destinationRef: null
    }))
  ].slice(0, 4);
}

export function createInitialChoiceSet({ runId, runVersion = 1, turnNo = 0, openingNpcId = null, openingNpcName = "낯선 주민", reason = null }) {
  return sealNarrativeIntervention({
    reason: reason || `${openingNpcName}이 먼저 말을 걸었다. 그 말에 어떻게 반응할지 선택한다.`,
    choices: [
      {
        choiceId: "opening.listen",
        text: `${openingNpcName}의 말을 끊지 않고, 무슨 일이 있었는지 자세히 묻는다.`,
        choiceKind: "DIALOGUE",
        intentTag: "CURIOUS",
        resolutionMode: "NONE",
        skillId: null,
        targetEntityId: openingNpcId,
        destinationRef: null
      },
      {
        choiceId: "opening.cautious",
        text: `${openingNpcName}의 의도를 경계하며, 알고 있는 근거부터 보여 달라고 한다.`,
        choiceKind: "ATTITUDE",
        intentTag: "CAUTIOUS",
        resolutionMode: "NONE",
        skillId: null,
        targetEntityId: openingNpcId,
        destinationRef: null
      },
      {
        choiceId: "opening.search",
        text: "관리자 키보드로 이 장소에 남은 흔적을 조사한다.",
        choiceKind: "SKILL",
        intentTag: "INVESTIGATE",
        resolutionMode: "D20",
        skillId: "SEARCH",
        targetEntityId: null,
        destinationRef: null
      }
    ]
  }, { runId, turnNo, runVersion, allowedEntityIds: openingNpcId ? [openingNpcId] : [] });
}

export function sealNarrativeIntervention(intervention, {
  runId,
  turnNo,
  runVersion,
  allowedEntityIds = [],
  allowedDestinationRefs = [],
  allowTravel = false
}) {
  assert(typeof runId === "string" && runId.length > 0, 500, "CHOICE_SEAL_INVALID", "A run ID is required to seal choices.");
  assert(Number.isInteger(turnNo) && turnNo >= 0, 500, "CHOICE_SEAL_INVALID", "A turn number is required to seal choices.");
  assert(Number.isInteger(runVersion) && runVersion >= 1, 500, "CHOICE_SEAL_INVALID", "A run version is required to seal choices.");
  const reason = boundedString(intervention?.reason, "nextIntervention.reason", 1, 220, 500, "CHOICE_SEAL_INVALID");
  const choices = validateNarrativeChoices(intervention?.choices, {
    status: 500,
    code: "CHOICE_SEAL_INVALID",
    allowedEntityIds,
    allowedDestinationRefs,
    allowTravel
  });
  const payloadHash = fingerprint({ reason, choices, turnNo, runVersion });
  const choiceSetId = deterministicUuid(`${runId}:choice-set:${turnNo}:v${runVersion}:${payloadHash}`);
  return {
    choiceSetId,
    reason,
    choices,
    suggestedSkillIds: [...new Set(choices.filter((choice) => choice.choiceKind === "SKILL").map((choice) => choice.skillId))],
    issuedTurn: turnNo,
    issuedRunVersion: runVersion
  };
}

export function normalizeChoiceSelectionRequest(input) {
  assert(input && typeof input === "object" && !Array.isArray(input), 400, "CHOICE_REQUEST_INVALID", "A JSON choice request is required.");
  exactKeys(input, ["choiceSetId", "choiceId", "idempotencyKey", "expectedRunVersion"], "CHOICE_REQUEST_INVALID", 400);
  assert(typeof input.choiceSetId === "string" && CHOICE_SET_ID_PATTERN.test(input.choiceSetId), 400, "CHOICE_SET_ID_INVALID", "choiceSetId must be a server-issued UUID.");
  const choiceId = boundedString(input.choiceId, "choiceId", 3, 64, 400, "CHOICE_ID_INVALID").toLowerCase();
  assert(CHOICE_ID_PATTERN.test(choiceId), 400, "CHOICE_ID_INVALID", "choiceId has an invalid format.");
  assert(typeof input.idempotencyKey === "string" && IDEMPOTENCY_PATTERN.test(input.idempotencyKey), 400, "IDEMPOTENCY_KEY_INVALID", "idempotencyKey must be 8-128 safe characters.");
  assert(Number.isSafeInteger(input.expectedRunVersion) && input.expectedRunVersion >= 1, 400, "RUN_VERSION_INVALID", "expectedRunVersion must be a positive integer.");
  return {
    choiceSetId: input.choiceSetId.toLowerCase(),
    choiceId,
    idempotencyKey: input.idempotencyKey,
    expectedRunVersion: input.expectedRunVersion
  };
}

export function normalizePlayerMessageRequest(input) {
  assert(input && typeof input === "object" && !Array.isArray(input), 400, "PLAYER_MESSAGE_INVALID", "A JSON player message is required.");
  exactKeys(input, ["text", "idempotencyKey", "expectedRunVersion"], "PLAYER_MESSAGE_INVALID", 400);
  const text = boundedString(input.text, "text", 1, 1000, 400, "PLAYER_MESSAGE_INVALID");
  assert(typeof input.idempotencyKey === "string" && IDEMPOTENCY_PATTERN.test(input.idempotencyKey), 400, "IDEMPOTENCY_KEY_INVALID", "idempotencyKey must be 8-128 safe characters.");
  assert(Number.isSafeInteger(input.expectedRunVersion) && input.expectedRunVersion >= 1, 400, "RUN_VERSION_INVALID", "expectedRunVersion must be a positive integer.");
  return { text, idempotencyKey: input.idempotencyKey, expectedRunVersion: input.expectedRunVersion };
}

export function playerMessageFingerprint(message) {
  return fingerprint({ inputType: "PLAYER_MESSAGE", expectedRunVersion: message.expectedRunVersion, text: message.text });
}

export function playerMessageRequest({ run, message, requestFingerprint }) {
  const pending = run?.pendingChoiceSet;
  const targetEntityId = pending?.choices?.find((choice) => choice?.targetEntityId)?.targetEntityId || null;
  return {
    inputType: "NARRATIVE_CHOICE",
    idempotencyKey: message.idempotencyKey,
    expectedRunVersion: message.expectedRunVersion,
    skillId: "NARRATIVE",
    ability: "dialogue",
    targetEntityId,
    secondaryTargetEntityId: null,
    destination: null,
    intent: message.text,
    playerNote: message.text,
    abilitySource: "player_freeform",
    forcedOverride: false,
    resolvesDebtEntryId: null,
    resolutionMode: "NONE",
    narrativeChoice: {
      choiceSetId: pending?.choiceSetId || deterministicUuid(`${run.id}:freeform:${run.currentTurn}`),
      choiceId: "player.freeform",
      text: message.text,
      choiceKind: "DIALOGUE",
      intentTag: "ASSERTIVE",
      resolutionMode: "NONE",
      skillId: null,
      targetEntityId,
      destinationRef: null
    },
    choiceRequestFingerprint: requestFingerprint
  };
}

export function choiceSelectionFingerprint(request) {
  return fingerprint({
    inputType: "NARRATIVE_CHOICE",
    expectedRunVersion: request.expectedRunVersion,
    choiceSetId: request.choiceSetId,
    choiceId: request.choiceId
  });
}

export function selectedChoiceForRun(run, request) {
  if (run && (!run.pendingChoiceSet || !Array.isArray(run.pendingChoiceSet.choices))) {
    run.pendingChoiceSet = createInitialChoiceSet({
      runId: run.id,
      runVersion: run.version,
      turnNo: run.currentTurn,
      reason: "이전 저장에는 선택지가 남아 있지 않았다. 현재 장면에서 다시 반응을 선택한다."
    });
  }
  const pending = run?.pendingChoiceSet;
  if (!pending || !Array.isArray(pending.choices)) throw new AppError(409, "CHOICE_SET_MISSING", "The run has no pending narrative choice set.");
  if (pending.choiceSetId !== request.choiceSetId) {
    throw new AppError(409, "CHOICE_SET_STALE", "The selected choice set is no longer current.", {
      currentChoiceSetId: pending.choiceSetId,
      currentVersion: run.version
    });
  }
  // Ambient NPC wandering may advance the run version without changing the
  // story decision. expectedRunVersion still protects the commit, while the
  // sealed choiceSetId is replaced by every story-changing turn.
  const choice = pending.choices.find((candidate) => candidate.choiceId === request.choiceId);
  if (!choice) throw new AppError(422, "CHOICE_NOT_OFFERED", "The choice was not offered by the current server-sealed choice set.");
  return clone(choice);
}

export function narrativeChoiceRequest({ selection, choice, requestFingerprint }) {
  return {
    inputType: "NARRATIVE_CHOICE",
    idempotencyKey: selection.idempotencyKey,
    expectedRunVersion: selection.expectedRunVersion,
    skillId: "NARRATIVE",
    ability: choice.choiceKind.toLowerCase(),
    targetEntityId: choice.targetEntityId,
    secondaryTargetEntityId: null,
    destination: null,
    intent: choice.text,
    playerNote: null,
    abilitySource: "server_sealed_choice",
    forcedOverride: false,
    resolvesDebtEntryId: null,
    resolutionMode: "NONE",
    narrativeChoice: { choiceSetId: selection.choiceSetId, ...clone(choice) },
    choiceRequestFingerprint: requestFingerprint
  };
}
