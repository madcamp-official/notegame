import { assert, AppError } from "../errors.js";
import { capabilitiesFor } from "./entity-capabilities.js";
import { enemyArchetype } from "./enemy-archetypes.js";
import { clone, deterministicUuid, fingerprint } from "./serialization.js";
import { eligibleAdminAccessCandidate } from "./codria-contract.js";

export const NARRATIVE_CHOICE_KINDS = Object.freeze(["DIALOGUE", "ATTITUDE", "SKILL", "TRAVEL"]);
export const NARRATIVE_RESOLUTION_MODES = Object.freeze(["NONE", "D20"]);
export const NARRATIVE_INTENT_TAGS = Object.freeze([
  "CURIOUS", "EMPATHETIC", "CAUTIOUS", "ASSERTIVE", "PLAYFUL",
  "INVESTIGATE", "PROTECT", "WITHDRAW", "TRAVEL"
]);
export const NARRATIVE_CHOICE_SKILLS = Object.freeze(["COPY", "DELETE", "CONNECT", "RESTORE", "UNDO", "SEARCH", "SELECT_ALL", "REST"]);

const CHOICE_ID_PATTERN = /^[a-z][a-z0-9_.:-]{2,63}$/;
const CHOICE_SET_ID_PATTERN = /^[0-9a-f]{8}-[0-9a-f]{4}-[1-8][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i;
const IDEMPOTENCY_PATTERN = /^[A-Za-z0-9][A-Za-z0-9_.:-]{7,127}$/;
const SKILL_LABELS = Object.freeze({
  COPY: "복제", DELETE: "삭제", CONNECT: "연결", RESTORE: "복구",
  UNDO: "되돌리기", SEARCH: "조사", SELECT_ALL: "광역 선택", REST: "휴식"
});

const NARRATION_NOTE_LIMIT = 400;
const ENCOUNTER_TARGET_SKILLS = new Set(["DELETE", "SEARCH", "CONNECT"]);
const TARGETLESS_SKILLS = new Set(["UNDO", "SELECT_ALL", "REST"]);

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

// Player messages are authoritative input and may contain up to 1,000 UTF-16
// code units.  The director's playerNote is deliberately smaller optional
// flavour text.  Keep the complete message in intent/narrativeChoice and the
// request fingerprint, while producing a bounded head-and-tail note so a long
// preamble cannot hide the actual action at the end of the message.
export function narrationNoteFromPlayerText(value) {
  const normalized = String(value || "").trim().replace(/\s+/gu, " ");
  if (normalized.length <= NARRATION_NOTE_LIMIT) return normalized || null;
  const separator = " … ";
  const available = NARRATION_NOTE_LIMIT - separator.length;
  let head = normalized.slice(0, Math.ceil(available / 2));
  let tail = normalized.slice(-Math.floor(available / 2));
  if (/[\uD800-\uDBFF]$/u.test(head)) head = head.slice(0, -1);
  if (/^[\uDC00-\uDFFF]/u.test(tail)) tail = tail.slice(1);
  return `${head}${separator}${tail}`;
}

function entityDistance(left, right) {
  if (!left?.position || !right?.position) return Number.POSITIVE_INFINITY;
  return Math.abs(left.position.x - right.position.x) + Math.abs(left.position.y - right.position.y);
}

function activeEncounterEntity(run) {
  if (run?.activeEncounter?.status !== "active") return null;
  const entityId = run.activeEncounter.sourceEntityId || run.activeEncounter.entityId || null;
  return entityId ? run.entities?.find((entity) => entity.id === entityId && entity.active) || null : null;
}

export function authoritativeNarrativeEntityIds(run) {
  const player = run?.entities?.find((entity) => entity.id === run.playerEntityId && entity.active) || null;
  const encounter = activeEncounterEntity(run);
  return (run?.entities || [])
    .filter((entity) => entity.active && (entityDistance(player, entity) <= 8 || entity.id === encounter?.id))
    .map((entity) => entity.id);
}

function pendingAdminSkill(run, entity, skillId) {
  return Boolean(eligibleAdminAccessCandidate(run, entity, skillId));
}

function hasRecentRestoration(run, entityId) {
  return [...(run.reversibleLedger || [])].reverse().some((item) => !item.consumed
    && run.currentTurn - item.turnNo <= 8
    && (item.inverseOps || []).some((operation) => (operation.type === "restore_entity" && operation.entity?.id === entityId)
      || (operation.type === "restore_state" && operation.entityId === entityId)));
}

function legalNarrativeSkillTarget(run, player, skillId, entity) {
  if (!entity || !entity.active || entity.id === player?.id || entity.state?.disabled || entity.state?.defeated || entity.state?.fled) return false;
  const focusCost = { COPY: 1, DELETE: 1, CONNECT: 2, RESTORE: 3, SEARCH: 1 }[skillId] || 0;
  if (Number(run.focus || 0) < focusCost) return false;
  const distance = entityDistance(player, entity);
  if (pendingAdminSkill(run, entity, skillId)) return distance <= ({ COPY: 4, DELETE: 3, CONNECT: 5, RESTORE: 5, SEARCH: 6 }[skillId] || 0);
  const capabilities = capabilitiesFor(entity);
  if (skillId === "COPY") return distance <= 4 && capabilities.canCopy;
  if (skillId === "DELETE") {
    if (distance > 3 || !capabilities.canDelete) return false;
    return entity.kind !== "enemy"
      || enemyArchetype(entity.assetId, run.worldSeed ?? run.world?.worldSeed, entity.id) !== "root_process"
      || entity.state?.revealed === true;
  }
  if (skillId === "CONNECT") {
    if (distance > 5 || !capabilities.canConnect) return false;
    return !(run.connections || []).some((item) => item.active
      && ((item.fromId === player.id && item.toId === entity.id) || (item.fromId === entity.id && item.toId === player.id)));
  }
  if (skillId === "RESTORE") return distance <= 5 && capabilities.canRestore && hasRecentRestoration(run, entity.id);
  if (skillId === "SEARCH") return distance <= 6;
  return false;
}

function deterministicSkillText(skillId, entity) {
  const name = String(entity?.name || "현재 대상").trim().slice(0, 72);
  return {
    COPY: `관리자 키보드로 “${name}”의 안전한 복제를 시도한다.`,
    DELETE: `R 키로 “${name}”에게 관리자 키보드의 삭제 명령을 내려 직접 공격한다.`,
    CONNECT: `관리자 키보드로 “${name}”와 직접 연결을 시도한다.`,
    RESTORE: `관리자 키보드로 “${name}”의 최근 손상을 복구한다.`,
    SEARCH: `관리자 키보드로 “${name}”의 숨은 의도와 약점을 조사한다.`
  }[skillId] || `관리자 키보드로 “${name}”에게 개입한다.`;
}

function deterministicTargetlessSkillText(skillId) {
  if (skillId === "REST") return "잠시 숨을 고르며 집중력과 체력을 회복한다.";
  return skillId === "SELECT_ALL"
    ? "관리자 키보드로 주변의 적대 신호를 한꺼번에 선택한다."
    : "관리자 키보드로 최근 두 행동의 영향을 되돌릴 준비를 한다.";
}

function safeChoiceFallback(choice, encounter, index) {
  const skillId = String(choice?.skillId || "").toUpperCase();
  const subject = encounter?.name ? `“${String(encounter.name).slice(0, 72)}”에게서 거리를 두고` : null;
  const fallbackText = {
    COPY: "지금은 바로 복제할 수 없다. 대상의 상태와 안전한 복제 조건을 다시 살핀다.",
    DELETE: "지금은 바로 삭제할 수 없다. 오염의 범위와 다른 해결책을 다시 살핀다.",
    CONNECT: "지금은 바로 연결할 수 없다. 연결할 대상과 안전한 경로를 다시 살핀다.",
    RESTORE: "지금은 바로 복구할 수 없다. 손상 상태와 가능한 복구 경로를 다시 살핀다.",
    SEARCH: "지금은 조사할 대상을 찾지 못했다. 주변 단서와 다음 조사 방향을 다시 살핀다.",
    SELECT_ALL: "지금은 광역 선택할 적대 신호가 없다. 주변 위험과 다음 대응을 다시 살핀다.",
    REST: "지금은 더 회복할 필요가 없다. 주변 상황과 다음 행동을 다시 살핀다."
  }[skillId] || `지금은 이 행동을 실행할 수 없다. ${["첫", "두", "세", "네"][index] || "다음"} 대안을 다시 살핀다.`;
  return {
    ...choice,
    text: subject ? `${subject} ${fallbackText}` : fallbackText,
    choiceKind: "ATTITUDE",
    intentTag: "CAUTIOUS",
    resolutionMode: "NONE",
    skillId: null,
    targetEntityId: encounter?.id || null,
    destinationRef: null
  };
}

/**
 * Reconcile model-authored skill options with the authoritative run immediately
 * before sealing. A visible UUID is not enough: the target must support the
 * skill, and an active encounter option must act on that encounter actor.
 */
export function reconcileNarrativeSkillChoices(value, { run, allowedEntityIds = [] } = {}) {
  if (!run || !Array.isArray(value)) return value;
  const player = run.entities?.find((entity) => entity.id === run.playerEntityId && entity.active) || null;
  const allowed = new Set(allowedEntityIds);
  const entities = (run.entities || []).filter((entity) => entity.active && allowed.has(entity.id) && entity.id !== player?.id)
    .sort((left, right) => entityDistance(player, left) - entityDistance(player, right) || left.id.localeCompare(right.id));
  const encounter = activeEncounterEntity(run);
  const authorizedEncounter = encounter && allowed.has(encounter.id) ? encounter : null;

  return value.map((sourceChoice, index) => {
    const choice = clone(sourceChoice);
    if (choice?.choiceKind !== "SKILL") return choice;
    const skillId = String(choice.skillId || "").toUpperCase();

    if (TARGETLESS_SKILLS.has(skillId)) {
      if (skillId === "REST") {
        const hp = Number(player?.state?.hp || 0);
        const maxHp = Number(player?.state?.maxHp || hp);
        if (Number(run.focus || 0) >= 10 && hp >= maxHp)
          return safeChoiceFallback(choice, authorizedEncounter, index);
        choice.targetEntityId = null;
        choice.text = deterministicTargetlessSkillText(skillId);
        return choice;
      }
      const selectAllTargets = skillId === "SELECT_ALL"
        ? entities.filter((entity) => entity.kind === "enemy" && entityDistance(player, entity) <= 4)
        : [];
      if (skillId === "SELECT_ALL" && (Number(run.focus || 0) < 3 || selectAllTargets.length === 0
        || (authorizedEncounter && !selectAllTargets.some((entity) => entity.id === authorizedEncounter.id)))) {
        return safeChoiceFallback(choice, authorizedEncounter, index);
      }
      if (choice.targetEntityId !== null && choice.targetEntityId !== undefined) {
        choice.targetEntityId = null;
        choice.text = deterministicTargetlessSkillText(skillId);
      }
      return choice;
    }

    const current = entities.find((entity) => entity.id === choice.targetEntityId) || null;
    const mentioned = entities
      .filter((entity) => entity.name && String(choice.text || "").includes(entity.name))
      .sort((left, right) => right.name.length - left.name.length || entityDistance(player, left) - entityDistance(player, right) || left.id.localeCompare(right.id))[0] || null;
    let selected = null;
    let correctedSkillId = skillId;

    if (authorizedEncounter && ENCOUNTER_TARGET_SKILLS.has(skillId)) {
      if (legalNarrativeSkillTarget(run, player, skillId, authorizedEncounter)) {
        selected = authorizedEncounter;
      } else if (skillId !== "SEARCH" && legalNarrativeSkillTarget(run, player, "SEARCH", authorizedEncounter)) {
        correctedSkillId = "SEARCH";
        selected = authorizedEncounter;
      } else {
        return safeChoiceFallback(choice, authorizedEncounter, index);
      }
    } else if (mentioned && legalNarrativeSkillTarget(run, player, skillId, mentioned)) {
      selected = mentioned;
    } else if (current && legalNarrativeSkillTarget(run, player, skillId, current)) {
      selected = current;
    } else if (choice.targetEntityId) {
      selected = entities.find((entity) => legalNarrativeSkillTarget(run, player, skillId, entity)) || null;
      if (!selected) return safeChoiceFallback(choice, authorizedEncounter, index);
    } else {
      return choice;
    }

    const targetChanged = choice.targetEntityId !== selected.id;
    const mentionedDifferentTarget = Boolean(mentioned && mentioned.id !== selected.id);
    const skillChanged = correctedSkillId !== skillId;
    choice.skillId = correctedSkillId;
    choice.targetEntityId = selected.id;
    if (correctedSkillId === "SEARCH") choice.intentTag = "INVESTIGATE";
    if (targetChanged || mentionedDifferentTarget || skillChanged) choice.text = deterministicSkillText(correctedSkillId, selected);
    return choice;
  });
}

function preserveActiveEncounterAction(value, run, allowedEntityIds) {
  if (!run || !Array.isArray(value) || run.activeEncounter?.status !== "active" ||
      String(run.activeEncounter?.kind || "").toUpperCase() !== "COMBAT") return value;
  const encounter = activeEncounterEntity(run);
  if (!encounter || !new Set(allowedEntityIds).has(encounter.id)) return value;
  const needsRest = Number(run.focus || 0) < 1;
  const requiredSkill = needsRest ? "REST" : "DELETE";
  if (value.some((choice) => choice?.choiceKind === "SKILL" && String(choice.skillId).toUpperCase() === requiredSkill)) {
    return value;
  }

  // A narrator may pivot a failed combat beat into conversation, but it must not
  // silently remove every direct encounter response while the authoritative
  // encounter is still active. Preserve all dialogue/attitude branches and add one
  // deterministic action that the existing legality reconciliation can retarget or
  // downgrade (for example DELETE -> SEARCH for an unrevealed Root Process).
  const directChoice = needsRest ? {
    choiceId: "encounter.recover_focus",
    text: deterministicTargetlessSkillText("REST"),
    choiceKind: "SKILL",
    intentTag: "CAUTIOUS",
    resolutionMode: "D20",
    skillId: "REST",
    targetEntityId: null,
    destinationRef: null
  } : {
    choiceId: "encounter.continue_action",
    text: deterministicSkillText("DELETE", encounter),
    choiceKind: "SKILL",
    intentTag: "ASSERTIVE",
    resolutionMode: "D20",
    skillId: "DELETE",
    targetEntityId: encounter.id,
    destinationRef: null
  };
  const choices = value.map(clone);
  if (choices.length < 4) choices.push(directChoice);
  else {
    const replaceAt = choices.findLastIndex((choice) => choice?.choiceKind === "SKILL" &&
      String(choice.skillId).toUpperCase() !== requiredSkill);
    choices[replaceAt >= 0 ? replaceAt : choices.length - 1] = directChoice;
  }
  return choices;
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
  allowTravel = true,
  minimumChoices = 2,
  requireNonSkill = true
} = {}) {
  if (!Array.isArray(value) || value.length < minimumChoices || value.length > 4) {
    throw new AppError(status, code, `choices must contain ${minimumChoices}-4 options.`);
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
  if (requireNonSkill && !choices.some((choice) => choice.choiceKind !== "SKILL")) throw new AppError(status, code, "At least one non-skill narrative choice is required.");
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

/**
 * A new run begins with one mandatory, mechanically real combat action.  The
 * player learns that the keyboard is the world-editing artifact by using its R
 * key before ordinary dialogue or exploration can begin.
 */
export function createOpeningCombatChoiceSet({
  runId,
  runVersion = 1,
  turnNo = 0,
  monsterId,
  monsterName = "오염된 몬스터"
}) {
  return sealNarrativeIntervention({
    reason: `${monsterName}이 길을 막았다. R 키로 관리자 키보드의 삭제 명령을 내려 첫 공격을 실행하세요.`,
    choices: [{
      choiceId: "opening.attack",
      text: `R 키로 “${monsterName}”에게 관리자 키보드의 삭제 명령을 내려 공격한다.`,
      choiceKind: "SKILL",
      intentTag: "ASSERTIVE",
      resolutionMode: "D20",
      skillId: "DELETE",
      targetEntityId: monsterId,
      destinationRef: null
    }]
  }, {
    runId,
    turnNo,
    runVersion,
    allowedEntityIds: monsterId ? [monsterId] : [],
    allowSingleSkillOnly: true
  });
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
  allowTravel = false,
  allowSingleSkillOnly = false,
  authoritativeRun = null
}) {
  assert(typeof runId === "string" && runId.length > 0, 500, "CHOICE_SEAL_INVALID", "A run ID is required to seal choices.");
  assert(Number.isInteger(turnNo) && turnNo >= 0, 500, "CHOICE_SEAL_INVALID", "A turn number is required to seal choices.");
  assert(Number.isInteger(runVersion) && runVersion >= 1, 500, "CHOICE_SEAL_INVALID", "A run version is required to seal choices.");
  const reason = boundedString(intervention?.reason, "nextIntervention.reason", 1, 220, 500, "CHOICE_SEAL_INVALID");
  const encounterSafeChoices = preserveActiveEncounterAction(intervention?.choices, authoritativeRun, allowedEntityIds);
  const reconciledChoices = reconcileNarrativeSkillChoices(encounterSafeChoices, { run: authoritativeRun, allowedEntityIds });
  const choices = validateNarrativeChoices(reconciledChoices, {
    status: 500,
    code: "CHOICE_SEAL_INVALID",
    allowedEntityIds,
    allowedDestinationRefs,
    allowTravel,
    minimumChoices: allowSingleSkillOnly ? 1 : 2,
    requireNonSkill: !allowSingleSkillOnly
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
  exactKeys(input, ["choiceSetId", "choiceId", "idempotencyKey", "expectedRunVersion", "preparedD20"], "CHOICE_REQUEST_INVALID", 400);
  assert(typeof input.choiceSetId === "string" && CHOICE_SET_ID_PATTERN.test(input.choiceSetId), 400, "CHOICE_SET_ID_INVALID", "choiceSetId must be a server-issued UUID.");
  const choiceId = boundedString(input.choiceId, "choiceId", 3, 64, 400, "CHOICE_ID_INVALID").toLowerCase();
  assert(CHOICE_ID_PATTERN.test(choiceId), 400, "CHOICE_ID_INVALID", "choiceId has an invalid format.");
  assert(typeof input.idempotencyKey === "string" && IDEMPOTENCY_PATTERN.test(input.idempotencyKey), 400, "IDEMPOTENCY_KEY_INVALID", "idempotencyKey must be 8-128 safe characters.");
  assert(Number.isSafeInteger(input.expectedRunVersion) && input.expectedRunVersion >= 1, 400, "RUN_VERSION_INVALID", "expectedRunVersion must be a positive integer.");
  const preparedD20 = input.preparedD20 ?? null;
  assert(preparedD20 === null || (Number.isInteger(preparedD20) && preparedD20 >= 1 && preparedD20 <= 20), 400, "D20_INVALID", "preparedD20 must be between 1 and 20.");
  return {
    choiceSetId: input.choiceSetId.toLowerCase(),
    choiceId,
    idempotencyKey: input.idempotencyKey,
    expectedRunVersion: input.expectedRunVersion,
    preparedD20
  };
}

export function normalizePlayerMessageRequest(input) {
  assert(input && typeof input === "object" && !Array.isArray(input), 400, "PLAYER_MESSAGE_INVALID", "A JSON player message is required.");
  exactKeys(input, ["text", "idempotencyKey", "expectedRunVersion", "preparedD20"], "PLAYER_MESSAGE_INVALID", 400);
  const text = boundedString(input.text, "text", 1, 1000, 400, "PLAYER_MESSAGE_INVALID");
  assert(/[\p{L}\p{N}]/u.test(text), 400, "PLAYER_MESSAGE_INVALID", "Player text must contain at least one letter or number.");
  assert(typeof input.idempotencyKey === "string" && IDEMPOTENCY_PATTERN.test(input.idempotencyKey), 400, "IDEMPOTENCY_KEY_INVALID", "idempotencyKey must be 8-128 safe characters.");
  assert(Number.isSafeInteger(input.expectedRunVersion) && input.expectedRunVersion >= 1, 400, "RUN_VERSION_INVALID", "expectedRunVersion must be a positive integer.");
  const preparedD20 = input.preparedD20 ?? null;
  assert(preparedD20 === null || (Number.isInteger(preparedD20) && preparedD20 >= 1 && preparedD20 <= 20), 400, "D20_INVALID", "preparedD20 must be between 1 and 20.");
  return { text, idempotencyKey: input.idempotencyKey, expectedRunVersion: input.expectedRunVersion, preparedD20 };
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
    playerNote: narrationNoteFromPlayerText(message.text),
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
  const authoritativeChoices = reconcileNarrativeSkillChoices(pending.choices, {
    run,
    allowedEntityIds: authoritativeNarrativeEntityIds(run)
  });
  pending.choices = authoritativeChoices;
  pending.suggestedSkillIds = [...new Set(authoritativeChoices
    .filter((candidate) => candidate.choiceKind === "SKILL")
    .map((candidate) => candidate.skillId))];
  const choice = authoritativeChoices.find((candidate) => candidate.choiceId === request.choiceId);
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
