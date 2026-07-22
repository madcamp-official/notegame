import { createHash, randomUUID } from "node:crypto";
import { assert, AppError } from "../errors.js";
import { clone, deterministicUuid, fingerprint } from "./serialization.js";
import { advanceArcDirector, campaignAct, createArcQuestions, endingConditionReports, macroPhaseForBeat, prioritizedFinaleConnectionPairs, prioritizedFinaleRemovalTargets, resolveFinale } from "./campaign.js";
import { TILE, areaAt, isWalkable, movementCost, publicWorld, tileAt } from "./world.js";
import { analyzeIntent, detectIntentActions, realizationAlignment } from "./intent.js";
import { containsProtectedFactReference } from "./protected-mechanics.js";
import { applyScenePlan, sanitizePlayerFacingHookSummary } from "./consequence-resolver.js";
import { capabilitiesFor } from "./entity-capabilities.js";
import { enemyArchetype } from "./enemy-archetypes.js";
import { BOSS_CATALOG, CORE_NPC_CATALOG, MONSTER_CATALOG, NPC_CATALOG, monsterForAsset } from "./content-catalog.js";
import { authoritativeNarrativeEntityIds, createInitialChoiceSet, createOpeningCombatChoiceSet, reconcileNarrativeSkillChoices } from "./narrative-choices.js";
import {
  ADMIN_ACCESS_LEVELS,
  ARTIFACT_ADMIN_KEYBOARD,
  CAMPAIGN_ACTION_CONTEXTS,
  GAME_TITLE,
  INPUT_TYPES,
  KEYBOARD_SKILLS,
  PROTAGONIST_NAME_KO,
  PROTAGONIST_NUPJUKYI,
  ROOT_SYSTEM,
  WORLD_CODRIA,
  WORLD_NAME_KO,
  eligibleAdminAccessCandidate,
  rootSystemGate,
  technicalDebtDelta
} from "./codria-contract.js";

export const CORE_ABILITIES = Object.freeze(KEYBOARD_SKILLS.map((item) => item.toLowerCase()));
const ACTION_SKILLS = Object.freeze([...KEYBOARD_SKILLS, "INTERACT", "ATTACK", "MOVE", "NEGOTIATE", "REST", "USE_ITEM", "COMBINE"]);
export const CONTEXT_ACTIONS = CAMPAIGN_ACTION_CONTEXTS;
export const ABILITIES = CORE_ABILITIES;
export const RUN_STATUSES = Object.freeze(["active", "abandoned", "completed"]);
export const OUTCOMES = Object.freeze(["critical_failure", "failure", "partial_success", "success", "critical_success"]);
export const DIRECTOR_OPS = Object.freeze(["SET_WORLD_FACT", "ADD_RUMOR", "ADD_NPC_MEMORY", "CHANGE_AFFINITY", "CREATE_HOOK", "START_QUEST", "ADVANCE_QUEST", "SET_VISUAL_INTENT"]);

const MONSTER_ENCOUNTER_NOUN = /(?:몬스터|괴물|마물|적대(?:자|세력)|(?:^|[\s,.'"“”‘’!?])적(?=$|[\s,.'"“”‘’!?]|을|과|이|가|에게|으로|의)|monster|enemy)/iu;
const MONSTER_ENCOUNTER_ACTION = /(?:조우|소환|출현|만남|대면|생성|스폰|나타(?:나|났|날|난)|젠|만(?:나|난|날|났)|불러|수색|찾|발견|습격|마주(?:치|쳤|칠|친)|대결|summon|spawn|encounter|meet|find|appear|fight)/u;

const UUID_PATTERN = /^[0-9a-f]{8}-[0-9a-f]{4}-[1-8][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i;
const IDEMPOTENCY_PATTERN = /^[A-Za-z0-9][A-Za-z0-9_.:-]{7,127}$/;
const DIRECTIONS = Object.freeze([[1, 0], [-1, 0], [0, 1], [0, -1]]);
const SUCCESS_OUTCOMES = new Set(["partial_success", "success", "critical_success"]);
const STORY_EVENT_MIN_DISTANCE = 15;
const STORY_EVENT_DISTANCE_RANGE = 6;

export function storyEventInterval(run, sequence) {
  return STORY_EVENT_MIN_DISTANCE + (createHash("sha256")
    .update(`${run.resolutionSeed}|${run.id}|story-event:${sequence}`)
    .digest().readUInt32BE(0) % STORY_EVENT_DISTANCE_RANGE);
}

/**
 * Natural Korean encounter requests are recognized by intent roots, not one
 * dictionary form.  The token-aware enemy pattern deliberately excludes words
 * such as "목적지", which used to spawn a monster merely because they contain
 * the single syllable "적".
 */
export function isMonsterEncounterRequest(text) {
  const normalized = String(text || "").trim();
  return MONSTER_ENCOUNTER_NOUN.test(normalized) && MONSTER_ENCOUNTER_ACTION.test(normalized);
}

function koreanSubject(value) {
  const text = String(value || "인물");
  const code = text.charCodeAt(text.length - 1);
  const hasBatchim = code >= 0xac00 && code <= 0xd7a3 && (code - 0xac00) % 28 !== 0;
  return `${text}${hasBatchim ? "이" : "가"}`;
}

function storyDrivenEnding(run) {
  const enoughStory = run.emergentStory?.endingEligible === true;
  const selectedEndingId = run.selectedEndingId || null;
  const explicitFinale = Boolean(selectedEndingId)
    && run.finalePuzzle?.status === "resolved"
    && run.finalePuzzle?.matchedEndingId === selectedEndingId;
  if (!enoughStory || !explicitFinale) return null;
  const selected = (run.endingCandidates || []).find((ending) => ending.id === selectedEndingId);
  return selected && !selected.emergency ? selected : null;
}

function reconcileOrphanedActiveEncounter(run, now) {
  const encounter = run.activeEncounter;
  if (encounter?.status !== "active" || encounter.kind !== "COMBAT") return null;
  const sourceEntityId = encounter.sourceEntityId || encounter.entityId || null;
  const source = sourceEntityId ? run.entities.find((item) => item.id === sourceEntityId) : null;
  const relationship = sourceEntityId
    ? (run.npcRelationships || []).find((item) => item.npcId === sourceEntityId)
    : null;
  const noLongerPresent = Boolean(sourceEntityId) &&
    (!source || source.active === false || source.state?.disabled || relationship?.encounterStatus === "withdrawn");
  if (!noLongerPresent) return null;
  const closed = {
    ...encounter,
    status: "resolved",
    resolution: "defeated_or_withdrawn",
    resolutionAction: encounter.lastAction || "state_reconciliation",
    resolutionOutcome: encounter.lastOutcome || "authoritative_state",
    resolvedTurn: run.currentTurn,
    resolvedAt: now
  };
  run.encounterHistory = (run.encounterHistory || []).map((item) => item.id === closed.id ? clone(closed) : item);
  run.activeEncounter = null;
  return closed;
}

function resolveEncounterLifecycle(run, openedEncounter, request, outcome, turnNo, now, events) {
  if (!openedEncounter) return;
  const succeeded = ["success", "critical_success"].includes(outcome);
  const sourceEntityId = openedEncounter.sourceEntityId || openedEncounter.entityId || null;
  const source = sourceEntityId ? entityById(run, sourceEntityId) : null;
  const relationship = source ? run.npcRelationships.find((item) => item.npcId === source.id) : null;
  let resolution = null;
  let mode = openedEncounter.mode || (openedEncounter.kind === "COMBAT" ? "confrontation" : "observation");
  if (request.ability === "move" && succeeded) resolution = "escaped";
  else if (["negotiate", "connect", "restore"].includes(request.ability) && succeeded) resolution = "negotiated";
  else if (["attack", "delete", "select_all"].includes(request.ability)) {
    mode = "combat";
    // Defeat is authoritative state, not a quality-of-roll label. A partial
    // success can still deal the last point of damage; keeping that encounter
    // active leaves the player fighting an entity that no longer exists.
    if (!source || source.active === false || source.state?.disabled || relationship?.encounterStatus === "withdrawn") {
      resolution = "defeated_or_withdrawn";
    }
  } else if (["search", "interact"].includes(request.ability)) mode = "observation";
  if (resolution) {
    const closed = { ...openedEncounter, status: "resolved", mode, resolvedTurn: turnNo, resolutionAction: request.ability, resolutionOutcome: outcome, resolution, resolvedAt: now };
    run.encounterHistory = (run.encounterHistory || []).map((item) => item.id === closed.id ? clone(closed) : item);
    run.activeEncounter = null;
    events.push({ type: "encounter_resolved", encounterId: closed.id, action: request.ability, outcome, resolution, campaignTurnConsumed: true });
    return;
  }
  const active = {
    ...openedEncounter,
    status: "active",
    mode,
    lastAction: request.ability,
    lastOutcome: outcome,
    lastChangedTurn: turnNo,
    escalation: ["failure", "critical_failure"].includes(outcome) ? "increased" : openedEncounter.escalation || "stable"
  };
  run.activeEncounter = active;
  run.encounterHistory = (run.encounterHistory || []).map((item) => item.id === active.id ? clone(active) : item);
  events.push({ type: "encounter_continued", encounterId: active.id, mode, action: request.ability, outcome, escalation: active.escalation });
}

function applyNarrativeEncounterLifecycle(run, request, turnNo, events) {
  const encounter = run.activeEncounter?.status === "active" ? run.activeEncounter : null;
  if (!encounter || request.rejectedAction) return;
  const intentTag = request.narrativeChoice?.intentTag;
  encounter.mode = intentTag === "ASSERTIVE" ? "confrontation" : intentTag === "WITHDRAW" ? "disengaging" : "conversation";
  encounter.lastChangedTurn = turnNo;
  encounter.lastNarrativeIntent = intentTag;
  run.encounterHistory = (run.encounterHistory || []).map((item) => item.id === encounter.id ? clone(encounter) : item);
  events.push({ type: "encounter_mode_changed", encounterId: encounter.id, mode: encounter.mode, intentTag, resolved: false });
}

function ensureActorRelationship(run, actor, turnNo = 0) {
  if (!actor || !["npc", "enemy"].includes(actor.kind)) return null;
  let relationship = run.npcRelationships.find((item) => item.npcId === actor.id);
  if (!relationship) {
    relationship = {
      npcId: actor.id,
      affinity: actor.kind === "enemy" ? -10 : 0,
      trust: 0,
      fear: actor.kind === "enemy" ? 2 : 0,
      stance: actor.kind === "enemy" ? "guarded" : "neutral",
      encounterStatus: "present",
      lastChangedTurn: turnNo
    };
    run.npcRelationships.push(relationship);
  }
  relationship.encounterStatus ||= "present";
  return relationship;
}

function changeEncounterRelationship(run, actor, turnNo, events, { affinity = 0, trust = 0, fear = 0, status = null, reason }) {
  const relationship = ensureActorRelationship(run, actor, turnNo);
  if (!relationship) return null;
  relationship.affinity = Math.max(-100, Math.min(100, Number(relationship.affinity || 0) + affinity));
  relationship.trust = Math.max(-100, Math.min(100, Number(relationship.trust || 0) + trust));
  relationship.fear = Math.max(0, Math.min(100, Number(relationship.fear || 0) + fear));
  relationship.stance = relationship.trust >= 35 ? "allied"
    : relationship.affinity >= 15 ? "open"
      : relationship.fear >= 35 ? "afraid"
        : relationship.affinity <= -35 ? "hostile" : "guarded";
  if (status) relationship.encounterStatus = status;
  relationship.lastChangedTurn = turnNo;
  events.push({
    type: "relationship_changed", npcId: actor.id, actorKind: actor.kind,
    affinityDelta: affinity, trustDelta: trust, fearDelta: fear,
    affinity: relationship.affinity, trust: relationship.trust, fear: relationship.fear,
    stance: relationship.stance, encounterStatus: relationship.encounterStatus, reason
  });
  return relationship;
}

function narrativeChoiceRecord(run, request, turnNo, events) {
  const selected = request.narrativeChoice;
  if (!selected) return null;
  const isPlayerFreeform = request.abilitySource === "player_freeform";
  if (!run.pendingChoiceSet || !Array.isArray(run.pendingChoiceSet.choices)) {
    // Travel intentionally dismisses an optional narrative choice set. A later
    // free-form message is itself the player's scene-driving choice, so it must
    // not be rebound to a newly generated server-offered choice-set UUID. Keep
    // the request's deterministic free-form UUID for the audit record instead.
    run.pendingChoiceSet = isPlayerFreeform
      ? {
          choiceSetId: selected.choiceSetId,
          reason: "플레이어가 선택지 대신 자유 입력으로 장면을 이어 간다.",
          choices: [],
          suggestedSkillIds: [],
          issuedTurn: run.currentTurn,
          issuedRunVersion: run.version
        }
      : createInitialChoiceSet({
          runId: run.id,
          runVersion: run.version,
          turnNo: run.currentTurn,
          reason: "이전 저장에는 선택지가 남아 있지 않았다. 현재 장면에서 다시 반응을 선택한다."
        });
  }
  const pending = run.pendingChoiceSet;
  if (pending?.choices) {
    pending.choices = reconcileNarrativeSkillChoices(pending.choices, {
      run,
      allowedEntityIds: authoritativeNarrativeEntityIds(run)
    });
    pending.suggestedSkillIds = [...new Set(pending.choices
      .filter((choice) => choice.choiceKind === "SKILL")
      .map((choice) => choice.skillId))];
  }
  assert(pending && pending.choiceSetId === selected.choiceSetId, 409, "CHOICE_SET_STALE", "The selected narrative choice set is no longer current.", {
    currentChoiceSetId: pending?.choiceSetId || null,
    currentVersion: run.version
  });
  const offered = pending.choices?.find((choice) => choice.choiceId === selected.choiceId);
  if (!isPlayerFreeform) {
    assert(offered && fingerprint(offered) === fingerprint({
      choiceId: selected.choiceId,
      text: selected.text,
      choiceKind: selected.choiceKind,
      intentTag: selected.intentTag,
      resolutionMode: selected.resolutionMode,
      skillId: selected.skillId,
      targetEntityId: selected.targetEntityId,
      destinationRef: selected.destinationRef
    }), 409, "CHOICE_SET_STALE", "The selected narrative choice no longer matches the server-sealed option.");
  }
  run.choiceHistory ||= [];
  run.majorChoices ||= [];
  const record = {
    id: deterministicUuid(`${run.id}:narrative-choice:${turnNo}:${selected.choiceSetId}:${selected.choiceId}`),
    turnNo,
    choiceSetId: selected.choiceSetId,
    choiceId: selected.choiceId,
    text: selected.text,
    choiceKind: selected.choiceKind,
    intentTag: selected.intentTag,
    resolutionMode: selected.resolutionMode,
    skillId: selected.skillId || null,
    targetEntityId: selected.targetEntityId || null,
    destinationRef: selected.destinationRef || null,
    outcome: null,
    d20: null
  };
  run.choiceHistory.push(record);
  run.majorChoices.push({ ...clone(record), type: "NARRATIVE_CHOICE" });
  run.pendingChoiceSet = null;
  events.push({
    type: "narrative_choice_selected",
    choiceRecordId: record.id,
    choiceSetId: record.choiceSetId,
    choiceId: record.choiceId,
    choiceKind: record.choiceKind,
    intentTag: record.intentTag,
    resolutionMode: record.resolutionMode
  });
  events.push({ type: "major_choice_recorded", choiceId: record.id, choiceType: "NARRATIVE_CHOICE" });
  return record;
}

function skipPendingChoiceForDisengage(run, request, turnNo, events) {
  if (request.narrativeChoice || request.ability !== "move" || !run.pendingChoiceSet) return;
  const choiceSetId = run.pendingChoiceSet.choiceSetId;
  run.choiceHistory ||= [];
  run.choiceHistory.push({
    type: "NARRATIVE_CHOICE_SKIPPED",
    choiceSetId,
    text: "플레이어가 WASD로 거리를 벌리며 이동을 계속했다.",
    turnNo
  });
  run.pendingChoiceSet = null;
  events.push({ type: "narrative_choice_skipped", choiceSetId, reason: "player_disengage_move" });
}

function closestNarrativeActor(run, selected) {
  const player = entityById(run, run.playerEntityId);
  const explicit = selected.targetEntityId ? entityById(run, selected.targetEntityId) : null;
  if (explicit && ["npc", "enemy"].includes(explicit.kind)) return explicit;
  if (!player) return null;
  return run.entities
    .filter((item) => item.active && ["npc", "enemy"].includes(item.kind) && manhattan(player.position, item.position) <= 8)
    .sort((left, right) => manhattan(player.position, left.position) - manhattan(player.position, right.position) || left.id.localeCompare(right.id))[0] || null;
}

function applyPureNarrativeChoiceEffects(run, selected, record, turnNo, events) {
  const actor = closestNarrativeActor(run, selected);
  if (!actor) return;
  const deltas = {
    CURIOUS: { affinity: 1, trust: 1, fear: 0 },
    EMPATHETIC: { affinity: 1, trust: 2, fear: -1 },
    CAUTIOUS: { affinity: 0, trust: 0, fear: 1 },
    ASSERTIVE: { affinity: -1, trust: 0, fear: 1 },
    PLAYFUL: { affinity: 1, trust: 1, fear: -1 },
    INVESTIGATE: { affinity: 0, trust: 0, fear: 1 },
    PROTECT: { affinity: 1, trust: 1, fear: -1 },
    WITHDRAW: { affinity: -1, trust: 0, fear: 0 }
  }[selected.intentTag] || { affinity: 0, trust: 0, fear: 0 };
  changeEncounterRelationship(run, actor, turnNo, events, {
    ...deltas,
    reason: `sealed_narrative_choice:${selected.intentTag.toLowerCase()}`
  });
  const memory = {
    id: deterministicUuid(`${run.id}:choice-memory:${actor.id}:${turnNo}:${selected.choiceId}`),
    npcId: actor.id,
    summary: `넙죽이는 “${selected.text}”라는 태도로 장면에 응답했다.`,
    importance: selected.intentTag === "EMPATHETIC" ? 0.7 : 0.55,
    ttlTurns: 10,
    createdTurn: turnNo,
    expired: false,
    sourceChoiceId: record.id
  };
  run.npcMemories.push(memory);
  record.targetEntityId = actor.id;
  const major = run.majorChoices.find((choice) => choice.id === record.id);
  if (major) major.targetEntityId = actor.id;
  events.push({ type: "npc_memory_added", memoryId: memory.id, npcId: actor.id, summary: memory.summary, sourceChoiceId: record.id });
}

export class DeterministicD20Source {
  roll({ resolutionSeed, runId, turnNo }) {
    const value = createHash("sha256").update(`${resolutionSeed}|${runId}|${turnNo}|d20.v2`).digest().readUInt32BE(0);
    return (value % 20) + 1;
  }
}

export class FixedD20Source {
  constructor(value = 10) {
    this.value = value;
  }
  roll() { return this.value; }
}

function exactKeys(object, allowed, code) {
  const unknown = Object.keys(object).filter((key) => !allowed.includes(key));
  assert(unknown.length === 0, 400, code, `Unknown fields: ${unknown.join(", ")}.`);
}

function point(value, fieldName) {
  assert(value && typeof value === "object" && !Array.isArray(value), 400, "DESTINATION_INVALID", `${fieldName} must be an object.`);
  exactKeys(value, ["areaId", "x", "y"], "DESTINATION_INVALID");
  assert(Number.isInteger(value.x) && Number.isInteger(value.y), 400, "DESTINATION_INVALID", `${fieldName} requires integer x and y.`);
  assert(value.areaId === undefined || (typeof value.areaId === "string" && value.areaId.length >= 1 && value.areaId.length <= 120), 400, "DESTINATION_INVALID", `${fieldName}.areaId must be a bounded string.`);
  return { ...(value.areaId === undefined ? {} : { areaId: value.areaId }), x: value.x, y: value.y };
}

export function normalizeTravelRequest(input) {
  assert(input && typeof input === "object" && !Array.isArray(input), 400, "TRAVEL_REQUEST_INVALID", "A JSON travel request is required.");
  exactKeys(input, ["inputType", "idempotencyKey", "expectedRunVersion", "destination", "playerNote"], "TRAVEL_REQUEST_INVALID");
  assert(["TRAVEL", "MOVE"].includes(String(input.inputType || "").toUpperCase()), 400, "INPUT_TYPE_INVALID", "Travel inputType must be MOVE (TRAVEL is accepted as an HTTP compatibility alias).");
  assert(typeof input.idempotencyKey === "string" && IDEMPOTENCY_PATTERN.test(input.idempotencyKey), 400, "IDEMPOTENCY_KEY_INVALID", "idempotencyKey must be 8-128 safe characters.");
  assert(Number.isSafeInteger(input.expectedRunVersion) && input.expectedRunVersion >= 1, 400, "RUN_VERSION_INVALID", "expectedRunVersion must be a positive integer.");
  const playerNote = input.playerNote ?? null;
  assert(playerNote === null || (typeof playerNote === "string" && playerNote.trim().length <= 240), 400, "PLAYER_NOTE_INVALID", "playerNote must contain at most 240 characters.");
  return {
    inputType: "MOVE",
    idempotencyKey: input.idempotencyKey,
    expectedRunVersion: input.expectedRunVersion,
    destination: point(input.destination, "destination"),
    playerNote: playerNote?.trim() || null,
    intent: "server-authorized safe travel"
  };
}

export function travelFingerprint(request) {
  return fingerprint({ inputType: "MOVE", expectedRunVersion: request.expectedRunVersion, destination: request.destination, playerNote: request.playerNote || null });
}

export function normalizeTurnRequest(input) {
  assert(input && typeof input === "object" && !Array.isArray(input), 400, "TURN_REQUEST_INVALID", "A JSON turn request is required.");
  exactKeys(input, ["inputType", "idempotencyKey", "expectedRunVersion", "skillId", "targetIds", "destination", "playerNote", "forcedOverride", "resolvesDebtEntryId", "itemIds", "actionProposal", "preparedD20"], "TURN_REQUEST_INVALID");
  assert(typeof input.idempotencyKey === "string" && IDEMPOTENCY_PATTERN.test(input.idempotencyKey), 400, "IDEMPOTENCY_KEY_INVALID", "idempotencyKey must be 8-128 safe characters.");
  assert(Number.isSafeInteger(input.expectedRunVersion) && input.expectedRunVersion >= 1, 400, "RUN_VERSION_INVALID", "expectedRunVersion must be a positive integer.");
  const preparedD20 = input.preparedD20 ?? null;
  assert(preparedD20 === null || (Number.isInteger(preparedD20) && preparedD20 >= 1 && preparedD20 <= 20), 400, "D20_INVALID", "preparedD20 must be between 1 and 20.");

  const normalizeEntityId = (value, name) => {
    if (value === undefined || value === null) return null;
    assert(typeof value === "string" && UUID_PATTERN.test(value), 400, "TARGET_INVALID", `${name} must be a UUID.`);
    return value.toLowerCase();
  };
  assert(String(input.inputType || "").toUpperCase() === "USE_SKILL", 400, "INPUT_TYPE_INVALID", "Campaign actions require inputType USE_SKILL.");
  const skillId = String(input.skillId || "").toUpperCase();
  assert(ACTION_SKILLS.includes(skillId), 400, "SKILL_INVALID", `skillId must be one of: ${ACTION_SKILLS.join(", ")}.`);
  const optionallyAutoTargetedSkills = ["SEARCH", "COPY", "DELETE", "CONNECT", "RESTORE"];
  const mayAutoTarget = optionallyAutoTargetedSkills.includes(skillId);
  const suppliedTargetIds = input.targetIds === undefined || input.targetIds === null ? [] : input.targetIds;
  assert(Array.isArray(suppliedTargetIds) && suppliedTargetIds.length <= 2, 400, "TARGET_INVALID", "targetIds must contain at most two entity UUIDs.");
  const targetIds = suppliedTargetIds.map((value, index) => normalizeEntityId(value, `targetIds[${index}]`));
  assert(new Set(targetIds).size === targetIds.length, 400, "TARGET_INVALID", "targetIds must not contain the same entity twice.");
  const targetEntityId = targetIds[0] || null;
  const secondaryTargetEntityId = targetIds[1] || null;
  const destination = input.destination === undefined || input.destination === null ? null : point(input.destination, "destination");
  const expectedTargets = skillId === "CONNECT" ? 2 : ["UNDO", "SELECT_ALL", "MOVE", "REST", "USE_ITEM", "COMBINE"].includes(skillId) ? 0 : 1;
  const actualTargetsCount = [targetEntityId, secondaryTargetEntityId].filter(Boolean).length;
  if (!mayAutoTarget || actualTargetsCount > 0) {
    assert(actualTargetsCount === expectedTargets, 400, "TARGET_INVALID", `${skillId} requires exactly ${expectedTargets} selected target(s).`);
  }
  if (destination && skillId !== "COPY" && skillId !== "MOVE") {
    throw new AppError(400, "DESTINATION_INVALID", `${skillId} does not accept a destination.`);
  }
  if (skillId === "COPY" && destination && !targetEntityId) {
    throw new AppError(400, "TARGET_INVALID", "COPY requires a selected source when a destination is supplied.");
  }
  const playerNote = input.playerNote ?? null;
  assert(playerNote === null || (typeof playerNote === "string" && playerNote.trim().length <= 400), 400, "PLAYER_NOTE_INVALID", "playerNote must contain at most 400 characters.");
  assert(input.forcedOverride === undefined || typeof input.forcedOverride === "boolean", 400, "FORCED_OVERRIDE_INVALID", "forcedOverride must be boolean.");
  const resolvesDebtEntryId = input.resolvesDebtEntryId === undefined || input.resolvesDebtEntryId === null ? null : normalizeEntityId(input.resolvesDebtEntryId, "resolvesDebtEntryId");
  const itemIds = input.itemIds === undefined ? [] : input.itemIds;
  assert(Array.isArray(itemIds) && itemIds.length <= 2 && itemIds.every((itemId) => typeof itemId === "string" && itemId.length >= 3 && itemId.length <= 100), 400, "INVENTORY_ITEM_INVALID", "itemIds must contain at most two bounded item IDs.");
  if (skillId === "USE_ITEM") assert(itemIds.length === 1, 400, "INVENTORY_ITEM_INVALID", "USE_ITEM requires one item ID.");
  else if (skillId === "COMBINE") assert(itemIds.length === 2 && itemIds[0] !== itemIds[1], 400, "INVENTORY_ITEM_INVALID", "COMBINE requires two distinct item IDs.");
  const actionProposal = input.actionProposal === undefined || input.actionProposal === null ? null : clone(input.actionProposal);
  assert(actionProposal === null || (typeof actionProposal === "object" && !Array.isArray(actionProposal)), 400, "PLAYER_ACTION_INVALID", "actionProposal must be a validated object.");
  const ability = skillId.toLowerCase();
  const targetSummary = [targetEntityId, secondaryTargetEntityId].filter(Boolean).join(" and ") || "the last reversible operation";
  return {
    inputType: "USE_SKILL",
    preparedD20,
    idempotencyKey: input.idempotencyKey,
    expectedRunVersion: input.expectedRunVersion,
    skillId,
    ability,
    targetEntityId,
    secondaryTargetEntityId,
    destination,
    intent: mayAutoTarget && actualTargetsCount === 0 ? `Use ambient ${skillId}` : `Use ${skillId} on ${targetSummary}`,
    playerNote: playerNote?.trim() || null,
    abilitySource: mayAutoTarget && actualTargetsCount === 0 ? "server_auto_target" : "structured_selection",
    forcedOverride: input.forcedOverride === true,
    resolvesDebtEntryId,
    itemIds: [...itemIds],
    actionProposal
  };
}

export function turnFingerprint(request) {
  if (typeof request.choiceRequestFingerprint === "string" && /^[0-9a-f]{64}$/.test(request.choiceRequestFingerprint)) return request.choiceRequestFingerprint;
  return fingerprint({
    expectedRunVersion: request.expectedRunVersion,
    inputType: request.inputType,
    skillId: request.skillId,
    ability: request.ability,
    targetEntityId: request.targetEntityId,
    secondaryTargetEntityId: request.secondaryTargetEntityId,
    destination: request.destination,
    playerNote: request.playerNote || null,
    abilitySource: request.abilitySource,
    forcedOverride: request.forcedOverride === true,
    resolvesDebtEntryId: request.resolvesDebtEntryId || null,
    itemIds: request.itemIds || [],
    actionProposal: request.actionProposal || null
  });
}

function instantiateRunScopedTemplates(runId, namespace, templates) {
  const records = templates || [];
  const templateIds = records.map((template) => String(template?.id || fingerprint(template)));
  assert(new Set(templateIds).size === templateIds.length, 500, "CAMPAIGN_TEMPLATE_ID_DUPLICATE",
    `${namespace} campaign templates must have unique identities.`);
  return records.map((template, index) => ({
    ...clone(template),
    // Campaign blueprints are reusable templates. Their deterministic IDs are
    // stable for a world seed, whereas every normalized run projection uses a
    // globally unique UUID primary key. Preserve the source identity for audit
    // and derive the live identity from the run boundary.
    sourceTemplateId: templateIds[index],
    id: deterministicUuid(`${runId}:${namespace}:${templateIds[index]}`)
  }));
}

export function createRunState({ campaign, ownerId, runId = randomUUID(), resolutionSeed = randomUUID(), now = new Date().toISOString() }) {
  const world = clone(campaign.world);
  const entry = world.points.find((pointItem) => pointItem.id === "entry");
  const playerId = deterministicUuid(`${runId}:player`);
  const npcSlots = world.placementSlots.filter((slot) => slot.kind === "npc");
  const propSlots = world.placementSlots.filter((slot) => slot.kind === "prop");
  const enemySlots = world.placementSlots.filter((slot) => slot.kind === "enemy");
  const entities = [entity(playerId, "player", "player.ninja-green.v1", PROTAGONIST_NAME_KO, entry, true, true, false, { hp: 12, maxHp: 12, protagonistId: PROTAGONIST_NUPJUKYI, role: "developer", signatureArtifact: ARTIFACT_ADMIN_KEYBOARD })];
  for (let index = 0; index < campaign.npcRoles.length; index += 1) {
    const role = campaign.npcRoles[index];
    const roleSlots = npcSlots.filter((slot) => slot.campaignRole === role.campaignRole);
    const slot = roleSlots[index % roleSlots.length] || npcSlots[index % npcSlots.length];
    const coreNpc = CORE_NPC_CATALOG[index % CORE_NPC_CATALOG.length];
    const npcAssetId = slot.allowedAssetIds.includes(coreNpc.assetId) ? coreNpc.assetId : slot.allowedAssetIds[index % slot.allowedAssetIds.length];
    const concerns = ["누군가 통제 기록을 고쳐 쓰고 있다", "같은 오류가 의도적으로 반복되고 있다", "관리자 권한을 흉내 내는 신호가 돌아다닌다"];
    const secrets = ["붕괴 직전 관리자 통로에서 낯선 접속 흔적을 보았다", "삭제된 기록 조각에 내부 통제 시스템의 서명이 남아 있었다", "안전하다고 알려진 경로가 사실은 ROOT_SYSTEM으로 이어진다"];
    entities.push(entity(deterministicUuid(`${runId}:npc:${role.id}`), "npc", npcAssetId, coreNpc.name || role.name || role.displayName || seededName(campaign.worldSeed, index), slot, false, true, false, {
      hp: 8, maxHp: 8, npcRole: role.role, slotId: slot.id, campaignRole: role.campaignRole,
      evidenceKey: role.evidenceKey, designatedCampaignEvidence: true, canonicalNpcId: coreNpc.id,
      roleTags: [...coreNpc.roleTags], factionId: coreNpc.factionId, goal: coreNpc.goal,
      motivation: coreNpc.motivation, concern: concerns[index % concerns.length], secret: secrets[index % secrets.length],
      revealedClues: [], traits: coreNpc.roleTags.includes("COMBATANT") ? ["GUARDIAN"] : []
    }));
  }
  const bookSlot = propSlots[0];
  entities.push(entity(deterministicUuid(`${runId}:prop:book`), "prop", "item.rune-book.v1", "여행자의 장부", bookSlot, true, false, true, { slotId: bookSlot.id, temporary: false }));
  const crateSlot = propSlots[1];
  entities.push(entity(deterministicUuid(`${runId}:prop:crate`), "prop", "item.crate.v1", "임시 보급 상자", crateSlot, true, false, true, { slotId: crateSlot.id, temporary: true }));
  const roadTiles = [];
  for (let y = 0; y < world.height; y += 1) for (let x = 0; x < world.width; x += 1) if (tileAt(world, { x, y }) === TILE.ROAD) roadTiles.push({ x, y });
  const distanceFromRoad = (slot) => roadTiles.reduce((minimum, road) => Math.min(minimum, manhattan(slot, road)), Number.POSITIVE_INFINITY);
  const enemySlot = enemySlots.filter((slot) => !slot.tags.includes("admin_access_candidate")).sort((left, right) => distanceFromRoad(right) - distanceFromRoad(left) || left.id.localeCompare(right.id))[0];
  // The first hostile is a stable, one-hit tutorial target. Later monsters keep
  // their seeded catalog variety, but the opening must teach the same input and
  // result on every world seed.
  const firstMonster = MONSTER_CATALOG[0];
  const firstEnemy = entity(deterministicUuid(`${runId}:enemy:first`), "enemy",
    firstMonster.assetId, firstMonster.name, enemySlot, true, false, false, {
      hp: 5, maxHp: 5, speed: firstMonster.speed,
      slotId: enemySlot.id, traits: [...firstMonster.traits], revealed: true,
      tutorialEncounter: true
    });
  entities.push(firstEnemy);
  const evidenceNames = {
    ADMIN_ACCESS_LEVEL_1: "관리자 권한 I 후보", ADMIN_ACCESS_LEVEL_2: "관리자 권한 II 후보", ADMIN_ACCESS_LEVEL_3: "관리자 권한 III 후보", STORY_REVELATION: "관리자 통제 시스템의 내부 기록"
  };
  for (const beat of campaign.requiredStoryBeats || []) if (beat.requiredEvidenceKey) evidenceNames[beat.requiredEvidenceKey] = `${beat.title} 증거`;
  const evidenceSlots = world.placementSlots.filter((slot) => !slot.tags.includes("admin_access_candidate") && slot.tags.includes("revelation_candidate"));
  for (const [index, slot] of evidenceSlots.entries()) {
    const evidenceKey = slot.reservedFor;
    entities.push(entity(deterministicUuid(`${runId}:evidence:${slot.id}`), "prop", slot.allowedAssetIds[index % slot.allowedAssetIds.length], evidenceNames[evidenceKey] || "여정의 증거", slot, false, evidenceKey !== "STORY_REVELATION", evidenceKey === "STORY_REVELATION", {
      slotId: slot.id, campaignRole: slot.campaignRole, evidenceKey, designatedCampaignEvidence: true, immutableAnchor: true
    }));
  }
  for (const candidate of world.adminAccessCandidates || []) {
    const slot = world.placementSlots.find((item) => item.id === candidate.slotId);
    const kind = candidate.actionContext === "COMBAT" ? "enemy" : candidate.actionContext === "NEGOTIATION" ? "npc" : "prop";
    const candidateId = deterministicUuid(`${runId}:admin-access:${candidate.id}`);
    const access = ADMIN_ACCESS_LEVELS.find((item) => item.id === candidate.accessLevelId);
    // Alternate progression paths can deliberately reuse a slot whose original
    // semantic kind differs from the sealed action context (for example SEARCH on
    // an enemy slot). Never carry that slot's enemy portrait onto a prop entity:
    // those large actor sprites render as screen-sized blocks in the world view.
    const compatibleSlotAssets = slot.allowedAssetIds.filter((assetId) =>
      kind === "enemy" ? assetId.startsWith("enemy.") || assetId.startsWith("boss.")
        : kind === "npc" ? assetId.startsWith("npc.")
          : assetId.startsWith("item.") || assetId.startsWith("prop."));
    const fallbackAssets = kind === "enemy"
      ? MONSTER_CATALOG.map((item) => item.assetId)
      : kind === "npc"
        ? NPC_CATALOG.map((item) => item.assetId)
        : ["item.rune-book.v1", "item.crate.v1", "prop.lantern.v1", "prop.altar.v1"];
    const candidateAssets = compatibleSlotAssets.length > 0 ? compatibleSlotAssets : fallbackAssets;
    const candidateAssetIndex = Math.abs(Number(campaign.worldSeed || 0) + candidate.id.length) % candidateAssets.length;
    const candidateAssetId = candidateAssets[candidateAssetIndex];
    const candidateMonster = kind === "enemy" ? monsterForAsset(candidateAssetId) : null;
    entities.push(entity(candidateId, kind, candidateAssetId, `${access.nameKo} · ${candidate.regionAxis}`, slot, kind === "enemy", false, false, {
      hp: kind === "enemy" ? Math.max(6, candidateMonster?.hp || 0) : undefined,
      maxHp: kind === "enemy" ? Math.max(6, candidateMonster?.hp || 0) : undefined,
      speed: kind === "enemy" ? candidateMonster?.speed || 2 : undefined,
      traits: kind === "enemy" ? [...(candidateMonster?.traits || [])] : [],
      slotId: slot.id,
      candidateId: candidate.id,
      adminAccessLevelId: candidate.accessLevelId,
      requiredSkillId: candidate.skillId,
      actionContext: candidate.actionContext,
      regionAxis: candidate.regionAxis,
      designatedCampaignEvidence: true,
      evidenceKey: candidate.accessLevelId
    }));
  }
  const fixtureDefinitions = [
    { evidenceKey: "LOCAL_DEBUG_RECORD", campaignRole: "LOCAL_STAKES", name: "복제 가능한 현장 기록", fixtureType: "copy_fixture", cloneable: true },
    { evidenceKey: "LEGACY_RECOVERY_RECORD", campaignRole: "CONSEQUENCE_RETURN", name: "복구 가능한 기억 조각", fixtureType: "restore_fixture", cloneable: false }
  ];
  for (const definition of fixtureDefinitions) {
    const slot = world.placementSlots.find((item) => ["loot", "prop", "quest"].includes(item.kind)
      && item.campaignRole === definition.campaignRole
      && !item.tags.includes("admin_access_candidate")
      && !entities.some((itemEntity) => samePoint(itemEntity.position, item)));
    assert(slot, 500, "CAMPAIGN_FIXTURE_MISSING", `No deterministic fixture slot exists for ${definition.evidenceKey}.`);
    entities.push(entity(deterministicUuid(`${runId}:fixture:${definition.fixtureType}`), "prop", slot.allowedAssetIds[0], definition.name, slot, false, false, definition.cloneable, {
      slotId: slot.id, campaignRole: definition.campaignRole, evidenceKey: definition.evidenceKey, designatedCampaignEvidence: true, fixtureType: definition.fixtureType
    }));
  }
  const componentNames = {
    FINAL_ANCHOR: ["anchor", "운명의 닻"], FINAL_SAFEGUARD: ["safeguard", "수호의 문장"], FINAL_MEMORY: ["memory", "기억의 씨앗"],
    FINAL_FREEDOM: ["freedom", "자유의 불꽃"], FINAL_THREAT: ["threat", "위협의 가시"], FINAL_PASSAGE: ["passage", "경계의 문"], FINAL_WITNESS: ["witness", "마지막 증언"]
  };
  const finaleComponentEntityIds = {};
  for (const slot of world.placementSlots.filter((item) => item.tags.includes("finale_candidate"))) {
    const [componentKey, displayName] = componentNames[slot.reservedFor];
    const componentId = deterministicUuid(`${runId}:finale:component:${componentKey}`);
    finaleComponentEntityIds[componentKey] = componentId;
    const removable = ["freedom", "threat"].includes(componentKey);
    entities.push(entity(componentId, "prop", slot.allowedAssetIds[0], displayName, slot, false, !removable, false, { finaleComponent: componentKey, slotId: slot.id, gated: true, campaignRole: "FINAL_CONVERGENCE" }));
  }
  entities.push(...createDormantEntityPool(runId, world, entities));
  const openingPlayer = entities.find((item) => item.id === playerId);
  const openingThreatPosition = DIRECTIONS
    .map(([dx, dy]) => ({ x: openingPlayer.position.x + dx, y: openingPlayer.position.y + dy }))
    .find((position) => isWalkable(world, position) &&
      !entities.some((item) => item.active && item.id !== firstEnemy.id && samePoint(item.position, position)));
  assert(openingThreatPosition, 500, "OPENING_MONSTER_APPROACH_BLOCKED",
    "The opening tutorial monster needs one legal adjacent attack tile.");
  firstEnemy.state.originSlotId = firstEnemy.state.slotId || null;
  firstEnemy.state.originPosition = clone(firstEnemy.position);
  firstEnemy.state.slotId = null;
  firstEnemy.state.approachedPlayerAtOpening = true;
  firstEnemy.position = clone(openingThreatPosition);
  const openingNpc = entities.filter((item) => item.kind === "npc" && item.active)
    .sort((left, right) => manhattan(left.position, openingPlayer.position) - manhattan(right.position, openingPlayer.position) || left.id.localeCompare(right.id))[0];
  assert(openingNpc, 500, "OPENING_NPC_MISSING", "A generated run needs one NPC to initiate the opening conversation.");
  const openingApproachCandidates = [];
  for (let radius = 1; radius <= 8; radius += 1) {
    for (let dx = -radius; dx <= radius; dx += 1) {
      const dySize = radius - Math.abs(dx);
      for (const dy of dySize === 0 ? [0] : [-dySize, dySize]) openingApproachCandidates.push({ x: openingPlayer.position.x + dx, y: openingPlayer.position.y + dy });
    }
  }
  const openingApproachPosition = openingApproachCandidates
    .find((position) => isWalkable(world, position) && !entities.some((item) => item.active && item.id !== openingNpc.id && samePoint(item.position, position)));
  assert(openingApproachPosition, 500, "OPENING_NPC_APPROACH_BLOCKED", "The opening NPC needs one legal adjacent approach tile.");
  openingNpc.state.originSlotId = openingNpc.state.slotId || null;
  openingNpc.state.slotId = null;
  openingNpc.state.approachedPlayerAtOpening = true;
  openingNpc.position = clone(openingApproachPosition);
  const occupiedBlockingTiles = new Set();
  for (const item of entities.filter((candidate) => candidate.active && candidate.blocking)) {
    const positionKey = key(item.position);
    assert(!occupiedBlockingTiles.has(positionKey), 500, "INITIAL_ENTITY_OVERLAP", "Generated blocking entities must occupy unique tiles.");
    assert(isWalkable(world, item.position), 500, "INITIAL_ENTITY_UNREACHABLE", "Generated blocking entities must start on walkable tiles.");
    occupiedBlockingTiles.add(positionKey);
  }

  const npcRelationships = entities.filter((item) => item.active && ["npc", "enemy"].includes(item.kind)).map((actor) => ({
    npcId: actor.id,
    affinity: actor.kind === "enemy" ? -10 : 0,
    trust: 0,
    fear: actor.kind === "enemy" ? 2 : 0,
    stance: actor.kind === "enemy" ? "guarded" : "neutral",
    encounterStatus: "present",
    lastChangedTurn: 0
  }));
  const firstBeat = campaign.requiredStoryBeats[0];
  const arcQuestions = campaign.arcQuestions?.length ? campaign.arcQuestions : createArcQuestions(campaign.turnLimit, campaign.genome || {});
  for (const arc of arcQuestions) arc.status = "legacy_disabled";
  const canonicalFacts = instantiateRunScopedTemplates(runId, "canonical-fact", campaign.canonicalFactTemplates)
    .map((fact) => ({ ...fact, establishedTurn: 0 }));
  const initialRumors = instantiateRunScopedTemplates(runId, "initial-rumor", campaign.initialRumors);
  canonicalFacts.push({ id: deterministicUuid(`${runId}:layout-fact`), subject: "world", predicate: "layout_hash", value: world.layoutHash, type: "canonical", establishedTurn: 0 });
  const playerEntity = entities.find((item) => item.id === playerId);
  playerEntity.state.facing = "SOUTH";
  playerEntity.state.inventory = [{
    id: deterministicUuid(`${runId}:inventory:admin-keyboard`), kind: "key_item", name: "관리자 키보드",
    description: "코드리아의 손상된 규칙에 개입할 수 있는 넙죽이의 핵심 도구.", quantity: 1,
    protected: true, acquiredTurn: 0, source: "starting_equipment"
  }];
  const openingArea = areaAt(world, playerEntity.position);
  const openingAreaName = openingArea.nameKo || openingArea.name || "낯선 지역";
  const protagonistIntro = "당신은 관리자 키보드를 든 넙죽이. 길과 기록이 무너지는 세계 코드리아에 떨어졌고, 손에 든 키보드는 이 세계의 대상을 직접 편집할 수 있다.";
  const situationIntro = `넙죽이가 ${openingAreaName}에서 정신을 차리자 ${firstEnemy.name}이 바로 앞을 가로막았다. ${koreanSubject(openingNpc.name)} 다급히 넙죽이 곁으로 달려왔다.`;
  const openingLine = `설명은 나중에 할게! 관리자 키보드의 R 키는 눈앞의 적에게 삭제 명령을 내려 공격해. 지금 ${firstEnemy.name}을 향해 R을 눌러!`;
  const pendingChoiceSet = createOpeningCombatChoiceSet({
    runId,
    runVersion: 1,
    turnNo: 0,
    monsterId: firstEnemy.id,
    monsterName: firstEnemy.name
  });
  const openingEncounter = {
    id: deterministicUuid(`${runId}:encounter:opening-tutorial`),
    status: "active",
    mode: "confrontation",
    escalation: "stable",
    kind: "COMBAT",
    title: "관리자 키보드 첫 전투",
    description: "R 키로 관리자 키보드의 삭제 명령을 내려 눈앞의 몬스터를 공격하세요.",
    reason: "opening_keyboard_tutorial",
    sourceEntityId: firstEnemy.id,
    entityId: firstEnemy.id,
    triggerPosition: clone(firstEnemy.position),
    stagingPosition: clone(openingPlayer.position),
    openedNavigationSequence: 0,
    openedAt: now,
    campaignTurnOpened: 0,
    suggestedActionContexts: ["COMBAT"],
    suggestedSkillIds: ["DELETE"]
  };
  const openingNarrative = {
    summary: "관리자 키보드 첫 전투",
    body: `${protagonistIntro}\n\n${situationIntro}\n\n${openingLine}`,
    dialogue: [openingLine],
    dialogueDetails: [{ speakerId: openingNpc.id, line: openingLine }],
    storySequence: [
      { type: "NARRATION", speakerId: null, actionId: null, text: protagonistIntro },
      { type: "NARRATION", speakerId: null, actionId: null, text: situationIntro },
      { type: "DIALOGUE", speakerId: openingNpc.id, actionId: null, text: openingLine }
    ],
    nextIntervention: clone(pendingChoiceSet),
    proposedOps: [], appliedOps: [], rejectedOps: [], elementalEffectId: null,
    fallbackUsed: false, model: "deterministic-opening-combat-v1"
  };
  return {
    id: runId,
    campaignId: campaign.id,
    campaignTitle: GAME_TITLE,
    gameTitle: GAME_TITLE,
    worldId: WORLD_CODRIA,
    worldName: WORLD_NAME_KO,
    protagonistId: PROTAGONIST_NUPJUKYI,
    protagonistName: PROTAGONIST_NAME_KO,
    artifactId: ARTIFACT_ADMIN_KEYBOARD,
    archetype: campaign.archetype,
    premise: campaign.premise,
    templateId: campaign.templateId,
    ownerId,
    status: "active",
    version: 1,
    currentTurn: 0,
    turnLimit: campaign.turnLimit,
    currentAct: "opening",
    campaignPhase: "opening",
    currentMacroPhase: clone((campaign.campaignMacroPhases || [])[0] || macroPhaseForBeat(firstBeat)),
    campaignMacroPhases: clone(campaign.campaignMacroPhases || []),
    currentStoryBeat: { id: "emergent.opening", title: "열린 도입", description: "세계와 인물의 반응이 첫 방향을 만든다.", phaseId: "opening", targetTurn: null, status: "active", act: "opening" },
    requiredStoryBeats: clone(campaign.requiredStoryBeats),
    arcQuestions: clone(arcQuestions),
    currentArcQuestion: null,
    emergentStory: { mode: "world_rule_driven", phase: "opening", meaningfulTurns: 0, majorChoiceCount: 0, resolvedThreads: 0, relationshipChanges: 0, endingEligible: false, forcedEnding: false, updatedTurn: 0 },
    storyLedger: [],
    choiceHistory: [],
    pendingChoiceSet,
    openingNarrative,
    resolvedArcOutcomes: [],
    episodeSummaries: [],
    endingWindow: clone(campaign.endingWindow || { normalEligibleStart: Math.max(30, Math.min(38, campaign.turnLimit - 2)), preferredEnd: Math.min(42, campaign.turnLimit), hardLimit: campaign.turnLimit }),
    endingCandidates: clone(campaign.endingCandidates),
    forbiddenEvents: clone(campaign.forbiddenEvents || []),
    focus: 10,
    maxFocus: 10,
    experience: 0,
    gold: 5,
    rewardedEnemyIds: [],
    enemiesDefeated: 0,
    pressure: 0,
    exposed: false,
    endingCode: null,
    finaleResolution: null,
    selectedEndingId: null,
    finalePuzzle: { componentEntityIds: finaleComponentEntityIds, status: "gated", evidence: [], matchedEndingId: null },
    progressLevel: 0,
    progressTokens: [],
    progressTokenDefinitions: clone(campaign.progressTokenDefinitions || campaign.accessTokenDefinitions || []),
    adminAccessLevels: clone(ADMIN_ACCESS_LEVELS),
    adminAccessCandidates: clone(world.adminAccessCandidates || []),
    adminAccessAcquisitionHistory: [],
    metrics: { worldStability: 55, worldAutonomy: 45, publicTrust: 50, technicalDebt: 25, companionBond: 10, turnPressure: 0 },
    navigationSequence: 0,
    safeTravelCount: 0,
    travelTime: 0,
    travelTimeUnits: 0,
    travelDistance: 0,
    nextStoryEventDistance: storyEventInterval({ resolutionSeed, id: runId }, 1),
    storyEventSequence: 0,
    storyEventDue: false,
    visitedPoiIds: ["entry"],
    discoveredAreaIds: [entry.areaId],
    activeEncounter: openingEncounter,
    encounterHistory: [clone(openingEncounter)],
    world,
    playerEntityId: playerId,
    entities,
    connections: [],
    slotEnrichments: [],
    canonicalFacts,
    rumors: initialRumors,
    openLoops: [{ id: deterministicUuid(`${runId}:opening-loop`), summary: firstBeat.title, status: "open", createdTurn: 0, expiresTurn: Math.min(campaign.turnLimit, 8), source: "campaign_director" }],
    unresolvedHooks: [{ id: deterministicUuid(`${runId}:opening-loop`), summary: firstBeat.title, status: "open", createdTurn: 0, expiresTurn: Math.min(campaign.turnLimit, 8), source: "campaign_director" }],
    majorChoices: [],
    regionOutcomes: [],
    abilityUsageHistory: [],
    technicalDebtEntries: [],
    inventoryHistory: [{ type: "acquired", itemName: "관리자 키보드", quantity: 1, turnNo: 0, source: "starting_equipment" }],
    finalPlacement: null,
    npcMemories: [],
    npcRelationships,
    npcPromises: [],
    directorState: {
      decisionNo: 0,
      recentSceneTypes: [],
      eventCooldowns: {},
      pendingConsequences: [],
      factionStandings: { AUDITORS: 0, OLD_GUARD: 0, NEUTRAL_BROKERS: 0 },
      discoveredSecrets: [],
      runTraits: [],
      specialSkills: [],
      generatedCharacters: [],
      generatedMonsterVariants: entities.filter((item) => item.active && item.kind === "enemy").map((item) => ({
        entityId: item.id, assetId: item.assetId, name: item.name, traits: [...(item.state?.traits || [])]
      }))
    },
    activeQuests: [
      // Quest seeds are templates, not accepted quests. The director may START_QUEST
      // from questTemplates only after the player's story actually reaches that hook.
      { id: deterministicUuid(`${runId}:world-thread`), key: `WORLD.${fingerprint(campaign.templateId || campaign.generatedTitle).slice(0, 12)}`, title: "코드리아의 붕괴", summary: campaign.premise, status: "active", questKind: "world_thread", currentStep: "opening", acceptsNewSteps: true, createdTurn: 0 }
    ],
    questTemplates: clone(campaign.questSeeds || []),
    generationPlan: clone(campaign.generationPlan || campaign.scenarioPlan || { genome: campaign.genome, generationMetadata: campaign.generationMetadata, questSeeds: campaign.questSeeds }),
    campaignContentHash: campaign.contentHash || fingerprint({ title: campaign.generatedTitle, premise: campaign.premise, beats: campaign.requiredStoryBeats, npcs: campaign.npcRoles, endings: campaign.endingCandidates }),
    reversibleLedger: [],
    resolutionSeed,
    createdAt: now,
    updatedAt: now
  };
}

function seededName(worldSeed, index) {
  const names = ["아린", "보라", "카엘", "누리", "세온", "미르", "라온", "이든", "하람", "유나", "도윤", "소리"];
  const offset = Math.abs(Number(worldSeed || 0)) % names.length;
  return names[(offset + index * 5) % names.length];
}

function entity(id, kind, assetId, name, position, blocking, protectedEntity, cloneable, state = {}) {
  const actorState = ["player", "npc", "enemy"].includes(kind) ? {
    factionId: kind === "enemy" ? "WILD_PROCESS" : kind === "player" ? "PLAYER" : "NEUTRAL_BROKERS",
    goal: null,
    motivation: null,
    traits: [],
    awareness: [],
    inventory: [],
    lastActionTurn: 0
  } : {};
  return { id, kind, assetId, name, position: { x: position.x, y: position.y }, blocking, protected: protectedEntity, cloneable, active: true, state: { ...actorState, ...state } };
}

function createDormantEntityPool(runId, world, existingEntities) {
  const occupiedSlotIds = new Set(existingEntities.map((item) => item.state?.slotId).filter(Boolean));
  const names = ["리턴", "포인터", "패치", "스택", "모듈", "브랜치"];
  const dormant = [];
  const freeNpcSlots = world.placementSlots.filter((slot) => slot.kind === "npc" && !occupiedSlotIds.has(slot.id));
  for (const [index, slot] of freeNpcSlots.entries()) {
    const catalog = NPC_CATALOG.find((item) => slot.allowedAssetIds.includes(item.assetId)) || NPC_CATALOG[index % NPC_CATALOG.length];
    dormant.push(entity(deterministicUuid(`${runId}:dormant:npc:${slot.id}:${catalog.assetId}`), "npc", catalog.assetId, names[index % names.length], slot, false, false, false, {
      hp: 8, maxHp: 8, speed: 2, slotId: slot.id, activationSlotId: slot.id, activationState: "DORMANT", dormant: true,
      roleTags: [...catalog.roleTags].slice(0, 3), traits: [...catalog.roleTags].filter((tag) => tag === "COMBATANT").map(() => "GUARDIAN"),
      factionId: "NEUTRAL_BROKERS", goal: "현재 지역에서 플레이어의 선택이 남긴 파장을 추적한다.", motivation: "자신의 생존 이유를 다음 선택과 연결하려 한다."
    }));
  }
  const freeEnemySlots = world.placementSlots.filter((slot) => slot.kind === "enemy" && !occupiedSlotIds.has(slot.id));
  for (const [index, slot] of freeEnemySlots.entries()) {
    const monster = MONSTER_CATALOG.find((item) => slot.allowedAssetIds.includes(item.assetId)) || MONSTER_CATALOG[index % MONSTER_CATALOG.length];
    dormant.push(entity(deterministicUuid(`${runId}:dormant:monster:${slot.id}:${monster.assetId}`), "enemy", monster.assetId, `${monster.name} 변종`, slot, false, false, false, {
      hp: monster.hp, maxHp: monster.hp, speed: monster.speed, slotId: slot.id, activationSlotId: slot.id, activationState: "DORMANT", dormant: true,
      traits: [...monster.traits], factionId: monster.traits.includes("OLD_GUARD_ALIGNED") ? "OLD_GUARD" : monster.traits.includes("AUDITOR_ALIGNED") ? "AUDITORS" : "WILD_PROCESS",
      goal: "현재 지역의 시스템 규칙을 보존한다.", motivation: "침입으로 판단한 변화를 시험한다."
    }));
    const boss = BOSS_CATALOG.find((item) => item.roles.includes(slot.campaignRole)) || BOSS_CATALOG[index % BOSS_CATALOG.length];
    dormant.push(entity(deterministicUuid(`${runId}:dormant:boss:${slot.id}:${boss.assetId}`), "enemy", boss.assetId, boss.name, slot, false, false, false, {
      hp: boss.hp, maxHp: boss.hp, speed: boss.speed, boss: true, bossPatterns: [...boss.patterns], slotId: slot.id,
      activationSlotId: slot.id, activationState: "DORMANT", dormant: true, minMacroOrder: boss.minMacroOrder, roles: [...boss.roles],
      traits: [...boss.traits], factionId: "AUDITORS", goal: "현재 단계의 세계 규칙을 시험한다.", motivation: "자신이 대표하는 시스템 질서를 보존한다."
    }));
  }
  for (const item of dormant) item.active = false;
  return dormant;
}

function samePoint(left, right) { return left.x === right.x && left.y === right.y; }
function manhattan(left, right) { return Math.abs(left.x - right.x) + Math.abs(left.y - right.y); }
function directionBetween(from, to) {
  const dx = to.x - from.x;
  const dy = to.y - from.y;
  if (dx === 0 && dy === 0) return "HERE";
  if (Math.abs(dx) >= Math.abs(dy)) return dx > 0 ? "EAST" : "WEST";
  return dy > 0 ? "SOUTH" : "NORTH";
}
function facingForPath(path, fallback = "SOUTH") {
  if (!Array.isArray(path) || path.length < 2) return fallback;
  return directionBetween(path[path.length - 2], path[path.length - 1]);
}
function entityById(run, id) { return run.entities.find((candidate) => candidate.id === id && candidate.active) || null; }
function entityAnyById(run, id) { return run.entities.find((candidate) => candidate.id === id) || null; }
function isActiveOccupied(run, destination) { return run.entities.some((candidate) => candidate.active && samePoint(candidate.position, destination)); }
function isBlockingOccupied(run, destination, exceptId = null) { return run.entities.some((candidate) => candidate.active && (candidate.blocking || candidate.kind === "prop") && candidate.id !== exceptId && samePoint(candidate.position, destination)); }
function key(pointValue) { return `${pointValue.x},${pointValue.y}`; }
function stateFingerprint(run) {
  const safe = clone(run);
  delete safe.resolutionSeed;
  return fingerprint(safe);
}

function findPath(run, start, goal) {
  const finaleAllowed = finaleGateEligible(run);
  if (!isWalkable(run.world, goal) || isBlockingOccupied(run, goal, run.playerEntityId)
    || (!finaleAllowed && areaAt(run.world, goal).campaignRole === "FINAL_CONVERGENCE")) return null;
  const open = [{ point: start, cost: 0 }];
  const costs = new Map([[key(start), 0]]);
  const previous = new Map();
  while (open.length > 0) {
    open.sort((left, right) => left.cost - right.cost || manhattan(left.point, goal) - manhattan(right.point, goal));
    const current = open.shift();
    if (samePoint(current.point, goal)) {
      const path = [goal];
      let cursor = key(goal);
      while (previous.has(cursor)) {
        const prior = previous.get(cursor);
        path.push(prior);
        cursor = key(prior);
      }
      path.reverse();
      return { path, cost: current.cost };
    }
    if (current.cost !== costs.get(key(current.point))) continue;
    for (const [dx, dy] of DIRECTIONS) {
      const next = { x: current.point.x + dx, y: current.point.y + dy };
      if (!isWalkable(run.world, next) || isBlockingOccupied(run, next, run.playerEntityId)) continue;
      if (!finaleAllowed && areaAt(run.world, next).campaignRole === "FINAL_CONVERGENCE") continue;
      const nextCost = current.cost + movementCost(run.world, next);
      if (nextCost > 5) continue;
      const nextKey = key(next);
      if (!costs.has(nextKey) || nextCost < costs.get(nextKey)) {
        costs.set(nextKey, nextCost);
        previous.set(nextKey, current.point);
        open.push({ point: next, cost: nextCost });
      }
    }
  }
  return null;
}

function findSafeTravelPath(run, start, goal) {
  if (!isWalkable(run.world, goal) || isBlockingOccupied(run, goal, run.playerEntityId)) return null;
  const startKey = key(start);
  const queue = [start];
  const previous = new Map();
  const visited = new Set([startKey]);
  const finaleAllowed = finaleGateEligible(run);
  const safeTile = (pointValue) => {
    return travelEncounterAt(run, pointValue) === null;
  };
  for (let cursor = 0; cursor < queue.length; cursor += 1) {
    const current = queue[cursor];
    if (samePoint(current, goal)) {
      const path = [clone(goal)];
      let cursorKey = key(goal);
      while (cursorKey !== startKey) {
        const prior = previous.get(cursorKey);
        path.push(prior);
        cursorKey = key(prior);
      }
      path.reverse();
      return { path, cost: path.slice(1).reduce((sum, item) => sum + movementCost(run.world, item), 0) };
    }
    for (const [dx, dy] of DIRECTIONS) {
      const next = { x: current.x + dx, y: current.y + dy };
      const nextKey = key(next);
      if (visited.has(nextKey) || !isWalkable(run.world, next) || !safeTile(next) || isBlockingOccupied(run, next, run.playerEntityId)) continue;
      if (!finaleAllowed && areaAt(run.world, next).campaignRole === "FINAL_CONVERGENCE") continue;
      visited.add(nextKey);
      previous.set(nextKey, current);
      queue.push(next);
    }
  }
  return null;
}

function travelEncounterAt(run, pointValue) {
  const hostile = run.entities.find((item) => item.active && item.kind === "enemy" && !item.state?.disabled && manhattan(item.position, pointValue) <= 2);
  if (hostile) return { reason: "hostile_proximity", sourceEntityId: hostile.id };
  const tile = tileAt(run.world, pointValue);
  if (tile === TILE.HAZARD || tile === TILE.RUIN) return { reason: tile === TILE.HAZARD ? "hazardous_tile" : "unstable_ruin", sourceEntityId: null };
  const area = areaAt(run.world, pointValue);
  const knownArea = (run.discoveredAreaIds || []).includes(area.id);
  const routedPoint = run.world.points.some((item) => samePoint(item, pointValue));
  if (tile !== TILE.ROAD && !knownArea && !routedPoint) return { reason: "unknown_off_route", sourceEntityId: null };
  return null;
}

function encounterChoiceContract(reason) {
  if (reason === "hostile_proximity") return {
    kind: "COMBAT",
    title: "적대적 조우",
    description: "길을 막은 적을 어떻게 돌파할지 선택하세요.",
    suggestedActionContexts: ["COMBAT", "INVESTIGATION"],
    suggestedSkillIds: ["DELETE", "SELECT_ALL", "SEARCH", "RESTORE"]
  };
  if (reason === "hazardous_tile") return {
    kind: "HAZARD",
    title: "불안정한 지형",
    description: "위험을 조사하거나 복구하고, 주변 요소를 연결해 안전한 길을 만드세요.",
    suggestedActionContexts: ["INVESTIGATION", "DEPLOYMENT"],
    suggestedSkillIds: ["SEARCH", "RESTORE", "CONNECT"]
  };
  if (reason === "unstable_ruin") return {
    kind: "RUIN_EVENT",
    title: "불안정한 유적",
    description: "유적의 흔적을 조사하거나 보존·복구할 방법을 선택하세요.",
    suggestedActionContexts: ["INVESTIGATION", "DEPLOYMENT"],
    suggestedSkillIds: ["SEARCH", "RESTORE", "COPY"]
  };
  return {
    kind: "DISCOVERY",
    title: "미지의 길",
    description: "주변을 조사하거나 발견한 흔적을 연결해 다음 장면을 여세요.",
    suggestedActionContexts: ["INVESTIGATION", "DEPLOYMENT"],
    suggestedSkillIds: ["SEARCH", "CONNECT", "COPY"]
  };
}

function findUnrestrictedTravelPath(run, start, goal) {
  if (!isWalkable(run.world, goal) || isBlockingOccupied(run, goal, run.playerEntityId)) return null;
  const startKey = key(start);
  const open = [{ point: start, cost: 0 }];
  const costs = new Map([[startKey, 0]]);
  const previous = new Map();
  const finaleAllowed = finaleGateEligible(run);
  while (open.length > 0) {
    open.sort((left, right) => left.cost - right.cost || manhattan(left.point, goal) - manhattan(right.point, goal));
    const current = open.shift();
    if (current.cost !== costs.get(key(current.point))) continue;
    if (samePoint(current.point, goal)) {
      const path = [clone(goal)];
      let cursorKey = key(goal);
      while (cursorKey !== startKey) {
        const prior = previous.get(cursorKey);
        path.push(prior);
        cursorKey = key(prior);
      }
      path.reverse();
      return { path, cost: current.cost };
    }
    for (const [dx, dy] of DIRECTIONS) {
      const next = { x: current.point.x + dx, y: current.point.y + dy };
      if (!isWalkable(run.world, next) || isBlockingOccupied(run, next, run.playerEntityId)) continue;
      if (!finaleAllowed && areaAt(run.world, next).campaignRole === "FINAL_CONVERGENCE") continue;
      const nextCost = current.cost + movementCost(run.world, next);
      const nextKey = key(next);
      if (costs.has(nextKey) && costs.get(nextKey) <= nextCost) continue;
      costs.set(nextKey, nextCost);
      previous.set(nextKey, current.point);
      open.push({ point: next, cost: nextCost });
    }
  }
  return null;
}

function finaleGateEligible(run) {
  return rootSystemGate(run).eligible;
}

function specialSkillPreparation(run, skillId, baseFocusCost) {
  const specialSkill = (run.directorState?.specialSkills || []).find((skill) =>
    skill.baseSkill === skillId && Number(skill.charges || 0) > 0) || null;
  if (!specialSkill) return { focusCost: baseFocusCost, healthCost: 0, modifierBonus: 0, specialSkill: null };
  const modifiers = new Set(specialSkill.modifierIds || []);
  const healthInstead = modifiers.has("HEALTH_INSTEAD_OF_FOCUS") && baseFocusCost > 0;
  return {
    focusCost: healthInstead ? 0 : Math.max(0, baseFocusCost - (modifiers.has("REDUCED_FOCUS_COST") ? 1 : 0)),
    healthCost: healthInstead ? 1 : 0,
    modifierBonus: modifiers.has("FACTION_BONUS") ? 2 : 0,
    specialSkill
  };
}

function assertSpecialSkillCost(run, cost, skillId) {
  assert(run.focus >= cost.focusCost, 422, "INSUFFICIENT_FOCUS", `${skillId} requires ${cost.focusCost} focus.`);
  if (cost.healthCost > 0) {
    const player = entityById(run, run.playerEntityId);
    assert(Number(player?.state?.hp || 0) > cost.healthCost, 422, "INSUFFICIENT_HEALTH", `${skillId} special modifier requires ${cost.healthCost} health.`);
  }
}

export function resolveSafeTravel({ run: originalRun, request, now = new Date().toISOString() }) {
  assert(originalRun.status === "active", 409, "RUN_NOT_ACTIVE", "The run does not accept travel.");
  assert(!originalRun.activeEncounter || originalRun.activeEncounter.status !== "active", 409, "ENCOUNTER_ACTION_REQUIRED", "Resolve the active encounter with one meaningful D20 action before further travel.", { activeEncounter: clone(originalRun.activeEncounter) });
  const player = entityById(originalRun, originalRun.playerEntityId);
  assert(player, 409, "PLAYER_MISSING", "The authoritative player entity is missing.");
  assert(!samePoint(player.position, request.destination), 422, "DESTINATION_INVALID", "Player is already on that tile.");
  const destinationArea = areaAt(originalRun.world, request.destination);
  assert(!request.destination.areaId || request.destination.areaId === destinationArea.id, 422, "DESTINATION_AREA_MISMATCH", "destination.areaId does not contain the selected coordinates.");
  if (destinationArea.regionAxis === ROOT_SYSTEM) assert(finaleGateEligible(originalRun), 422, "ROOT_SYSTEM_ACCESS_DENIED", "Root System travel requires all three administrator access levels and the internal-collapse clue.", rootSystemGate(originalRun));
  const immutableLayout = fingerprint(publicWorld(originalRun.world));
  let path = findSafeTravelPath(originalRun, player.position, request.destination);
  let encounter = null;
  if (!path) {
    const unrestricted = findUnrestrictedTravelPath(originalRun, player.position, request.destination);
    assert(unrestricted, 422, "TRAVEL_PATH_BLOCKED", "No walkable route reaches the requested destination.");
    let encounterIndex = unrestricted.path.findIndex((position, index) => index > 0 && travelEncounterAt(originalRun, position));
    if (encounterIndex < 0) encounterIndex = Math.max(1, unrestricted.path.length - 1);
    const triggerPosition = unrestricted.path[encounterIndex];
    const trigger = travelEncounterAt(originalRun, triggerPosition) || { reason: "unsafe_or_blocked_route", sourceEntityId: null };
    const safeSegment = unrestricted.path.slice(0, encounterIndex);
    const stagingPosition = clone(safeSegment.at(-1) || player.position);
    path = { path: safeSegment.length > 0 ? safeSegment : [clone(player.position)], cost: safeSegment.slice(1).reduce((sum, item) => sum + movementCost(originalRun.world, item), 0) };
    encounter = { reason: trigger.reason, sourceEntityId: trigger.sourceEntityId, requestedDestination: clone(request.destination), triggerPosition: clone(triggerPosition), stagingPosition };
  }
  const run = clone(originalRun);
  if (run.pendingChoiceSet) {
    run.choiceHistory ||= [];
    run.choiceHistory.push({ type: "NARRATIVE_CHOICE_SKIPPED", choiceSetId: run.pendingChoiceSet.choiceSetId,
      text: "플레이어가 대화를 마치고 이동을 계속했다.", turnNo: run.currentTurn });
    run.pendingChoiceSet = null;
  }
  const nextStoryDistance = Number(run.nextStoryEventDistance || storyEventInterval(run, Number(run.storyEventSequence || 0) + 1));
  const runPlayer = entityById(run, run.playerEntityId);
  const from = clone(runPlayer.position);

  const events = [];

  const pathDestination = path.path.at(-1);
  const actualDestination = { x: pathDestination.x, y: pathDestination.y };
  runPlayer.position = actualDestination;
  runPlayer.state.facing = facingForPath(path.path, runPlayer.state?.facing || "SOUTH");
  run.version += 1;
  run.navigationSequence = (run.navigationSequence || 0) + 1;
  run.safeTravelCount = (run.safeTravelCount || 0) + 1;
  run.travelTimeUnits = (run.travelTimeUnits || 0) + path.cost;
  run.travelTime = run.travelTimeUnits;
  run.travelDistance = (run.travelDistance || 0) + Math.max(0, path.path.length - 1);
  // Every travel command commits authoritative coordinates, but scene work is
  // scheduled only by the server-owned 15-20 tile checkpoint.  Request naming
  // must never force an event: retries and different clients then observe the
  // same cadence for the same run.
  run.storyEventDue = !encounter && run.travelDistance >= nextStoryDistance;
  const traversedAreaIds = [...new Set(path.path.map((position) => areaAt(run.world, position).id))];
  for (const areaId of traversedAreaIds) if (!run.discoveredAreaIds.includes(areaId)) run.discoveredAreaIds.push(areaId);
  const reachedPois = run.world.points.filter((item) => path.path.some((position) => manhattan(item, position) <= 2)).map((item) => item.id);
  for (const poiId of reachedPois) if (!run.visitedPoiIds.includes(poiId)) run.visitedPoiIds.push(poiId);
  if (encounter) {
    const choices = encounterChoiceContract(encounter.reason);
    run.activeEncounter = {
      id: deterministicUuid(`${run.id}:encounter:${run.navigationSequence}:${request.idempotencyKey}`),
      status: "active",
      mode: choices.kind === "COMBAT" ? "confrontation" : "observation",
      escalation: "stable",
      ...clone(encounter),
      openedNavigationSequence: run.navigationSequence,
      openedAt: now,
      campaignTurnOpened: run.currentTurn,
      ...choices
    };
    run.encounterHistory.push(clone(run.activeEncounter));
  }
  run.updatedAt = now;
  assert(fingerprint(publicWorld(run.world)) === immutableLayout, 500, "WORLD_LAYOUT_MUTATED", "Safe travel attempted to mutate immutable world geometry.");
  const actualArea = areaAt(run.world, actualDestination);
  const navigation = {
    id: deterministicUuid(`${run.id}:navigation:${run.navigationSequence}:${request.idempotencyKey}`),
    runId: run.id,
    sequence: run.navigationSequence,
    idempotencyKey: request.idempotencyKey,
    requestFingerprint: travelFingerprint(request),
    expectedRunVersion: request.expectedRunVersion,
    committedRunVersion: run.version,
    from,
    to: { areaId: actualArea.id, ...actualDestination },
    requestedDestination: clone(request.destination),
    path: path.path,
    facing: runPlayer.state.facing,
    pathCost: path.cost,
    enteredAreaId: actualArea.id,
    enteredBiomeId: actualArea.biomeId,
    campaignRole: actualArea.campaignRole || null,
    traversedAreaIds,
    reachedPoiIds: reachedPois,
    travelTimeUnits: path.cost,
    cumulativeTravelTimeUnits: run.travelTimeUnits,
    travelDistance: run.travelDistance,
    storyEventTriggered: run.storyEventDue,
    nextStoryEventDistance: nextStoryDistance,
    encounterOpened: Boolean(encounter),
    encounter: run.activeEncounter ? clone(run.activeEncounter) : null,
    campaignTurnConsumed: false,
    campaignTurnBefore: originalRun.currentTurn,
    campaignTurnAfter: run.currentTurn,
    layoutHash: run.world.layoutHash,
    createdAt: now
  };
  return { run, navigation, events };
}
function findClosestEntity(run, playerPos, maxDistance, filterFn) {
  let closest = null;
  let minDistance = Infinity;
  for (const entity of run.entities) {
    if (!entity.active || entity.state?.disabled || entity.state?.defeated || entity.state?.fled) continue;
    if (entity.id === run.playerEntityId) continue;
    if (filterFn && !filterFn(entity)) continue;
    const dist = manhattan(playerPos, entity.position);
    if (dist <= maxDistance && dist < minDistance) {
      minDistance = dist;
      closest = entity;
    }
  }
  return closest;
}

function findClosestWalkableTile(run, playerPos, maxDistance) {
  const w = run.world.width;
  const h = run.world.height;
  let closest = null;
  let minDistance = Infinity;
  for (let d = 1; d <= maxDistance; d++) {
    for (let dx = -d; dx <= d; dx++) {
      const dyVal = d - Math.abs(dx);
      const dys = dyVal === 0 ? [0] : [dyVal, -dyVal];
      for (const dy of dys) {
        const x = playerPos.x + dx;
        const y = playerPos.y + dy;
        if (x < 0 || x >= w || y < 0 || y >= h) continue;
        const point = { x, y };
        if (!isWalkable(run.world, point)) continue;
        if (isActiveOccupied(run, point)) continue;
        const dist = Math.abs(dx) + Math.abs(dy);
        if (dist < minDistance) {
          minDistance = dist;
          closest = point;
        }
      }
    }
    if (closest) break;
  }
  return closest;
}

function isValidAdminAccessCandidate(run, target, skillId) {
  return Boolean(eligibleAdminAccessCandidate(run, target, skillId));
}

function prepare(run, request, skillSelection = null) {
  const player = entityById(run, run.playerEntityId);
  assert(player, 409, "PLAYER_MISSING", "The authoritative player entity is missing.");
  const ownedBoundItems = (request.itemIds || []).map((itemId) => (player.state?.inventory || []).find((item) => item.id === itemId));
  assert(ownedBoundItems.every(Boolean), 422, "INVENTORY_ITEM_NOT_OWNED", "Every item bound to the action must be owned by the player.");
  if (request.ability === "move") {
    assert(!request.targetEntityId && !request.secondaryTargetEntityId, 422, "TARGET_FORBIDDEN", "Move is authorized by destination, not an entity target.");
    assert(request.destination, 422, "DESTINATION_REQUIRED", "Move requires destination.");
    assert(!samePoint(player.position, request.destination), 422, "DESTINATION_INVALID", "Player is already on that tile.");
    const path = findPath(run, player.position, request.destination);
    assert(path, 422, "PATH_BLOCKED", "No legal path reaches destination within the 5-point movement budget.");
    return { difficulty: 9, modifier: 3, focusCost: 0, normalizedAttempt: `Move to (${request.destination.x},${request.destination.y}) through a legal path costing ${path.cost}`, path: path.path };
  }
  if (request.ability === "rest") {
    assert(!request.targetEntityId && !request.secondaryTargetEntityId && !request.destination, 422, "TARGET_FORBIDDEN", "Rest does not accept a target or destination.");
    const hp = Number.isInteger(player.state.hp) ? player.state.hp : 1;
    const maxHp = Number.isInteger(player.state.maxHp) ? player.state.maxHp : hp;
    assert(run.focus < 10 || hp < maxHp, 422, "REST_NOT_NEEDED", "Rest requires missing focus or health.");
    return { difficulty: 7, modifier: 3, focusCost: 0, focusRecovery: 3, healthRecovery: 2, normalizedAttempt: "Take one bounded rest turn to recover up to 3 focus and 2 health" };
  }
  if (request.ability === "use_item") {
    const inventory = player.state?.inventory || [];
    const item = inventory.find((candidate) => candidate.id === request.itemIds?.[0]);
    assert(item, 422, "INVENTORY_ITEM_NOT_OWNED", "The player does not own the proposed item.");
    assert(item.protected !== true, 422, "INVENTORY_ITEM_PROTECTED", "A protected key item cannot be consumed by free-form use.");
    return { difficulty: 8, modifier: 4, focusCost: 0, item, normalizedAttempt: `Use owned item ${item.name} in the current scene` };
  }
  if (request.ability === "combine") {
    const inventory = player.state?.inventory || [];
    const items = (request.itemIds || []).map((itemId) => inventory.find((candidate) => candidate.id === itemId));
    assert(items.length === 2 && items.every(Boolean), 422, "INVENTORY_ITEM_NOT_OWNED", "The player must own both proposed combination items.");
    assert(items.every((item) => item.protected !== true), 422, "INVENTORY_ITEM_PROTECTED", "Protected key items cannot be consumed by free-form combination.");
    return { difficulty: 11, modifier: 3, focusCost: 0, items, normalizedAttempt: `Combine owned items ${items[0].name} and ${items[1].name}` };
  }
  if (request.ability === "undo") {
    assert(!request.targetEntityId && !request.secondaryTargetEntityId && !request.destination, 422, "TARGET_FORBIDDEN", "Undo rewinds the two most recent reversible turns and does not accept a target.");
    const specialCost = specialSkillPreparation(run, "UNDO", 3);
    assertSpecialSkillCost(run, specialCost, "UNDO");
    const reversibles = [...run.reversibleLedger].reverse().filter((item) => item.reversible && !item.consumed).slice(0, 2);
    assert(reversibles.length === 2, 422, "UNDO_NOT_AVAILABLE", "Ctrl Z requires two unconsumed reversible turns.");
    return { difficulty: 15, modifier: 2 + specialCost.modifierBonus, ...specialCost, normalizedAttempt: `Rewind turns ${reversibles[1].turnNo}-${reversibles[0].turnNo}`, reversibles };
  }
  if (request.ability === "select_all") {
    assert(!request.targetEntityId && !request.secondaryTargetEntityId && !request.destination, 422, "TARGET_FORBIDDEN", `${request.skillId} does not accept a target or destination.`);
    const focusCost = 3;
    assert(run.focus >= focusCost, 422, "INSUFFICIENT_FOCUS", `${request.skillId} requires ${focusCost} focus.`);
    const maximumRadius = 4;
    const enemies = run.entities.filter((item) => item.active && item.kind === "enemy" && !item.state?.disabled && manhattan(item.position, player.position) <= maximumRadius);
    assert(enemies.length > 0, 422, "NO_AREA_TARGETS", "Ctrl A requires at least one hostile enemy within 4 tiles.");
    return { difficulty: 14, modifier: 4, focusCost, maximumRadius, enemies, normalizedAttempt: `Area attack ${enemies.length} hostile target(s) within 6 tiles` };
  }
  const specialCost = specialSkillPreparation(run, request.skillId, { copy: 1, delete: 1, connect: 2, restore: 3, search: 1 }[request.ability] || 0);
  assertSpecialSkillCost(run, specialCost, request.skillId);

  if (request.ability === "copy") {
    const explicitlyTargeted = Boolean(request.targetEntityId);
    let target = explicitlyTargeted
      ? entityAnyById(run, request.targetEntityId)
      : skillSelection?.kind === "entity" ? entityAnyById(run, skillSelection.entityIds?.[0]) : null;
    let destination = request.destination ? clone(request.destination) : null;

    if (explicitlyTargeted) assert(target, 422, "ENTITY_NOT_FOUND", "The selected COPY source does not exist.");

    if (!explicitlyTargeted && !skillSelection) target = findClosestEntity(run, player.position, 3, (e) => isValidAdminAccessCandidate(run, e, "COPY"));
    if (!explicitlyTargeted && !target && !skillSelection) {
      target = findClosestEntity(run, player.position, 4, (e) => !e.state?.adminAccessLevelId && !e.state?.copyLocked && capabilitiesFor(e).canCopy);
    }
    if (target && !destination) destination = findClosestWalkableTile(run, player.position, 4);

    if (!target || (!target.state?.adminAccessLevelId && !destination)) {
      return { difficulty: 11, modifier: 3 + specialCost.modifierBonus, ...specialCost, ambientFallback: true, discoveryType: skillSelection?.generatedEvent?.discoveryType || skillSelection?.discoveryType || "ambient_packet", generatedEvent: skillSelection?.generatedEvent || null, normalizedAttempt: "Copy ambient signal with COPY" };
    }

    if (target.state?.adminAccessLevelId) {
      const candidate = (run.adminAccessCandidates || []).find((item) => item.id === target.state.candidateId);
      assert(candidate && target.active, 422, "ADMIN_ACCESS_CANDIDATE_INVALID", "The selected administrator access candidate is unavailable.");
      assert(candidate.skillId === "COPY", 422, "ADMIN_ACCESS_SKILL_MISMATCH", `This candidate requires ${candidate.skillId}.`);
      assert(!(run.adminAccessAcquisitionHistory || []).some((item) => item.accessLevelId === candidate.accessLevelId), 422, "ADMIN_ACCESS_ALREADY_ACQUIRED", `${candidate.accessLevelId} has already been acquired through another path.`);
      assert(eligibleAdminAccessCandidate(run, target, "COPY"), 422, "ADMIN_ACCESS_SEQUENCE_INVALID", "Administrator access levels must be acquired in order.");
      assert(manhattan(player.position, target.position) <= 4, 422, "OUT_OF_RANGE", "COPY administrator access target must be within 4 tiles.");
      return {
        difficulty: { COMBAT: 12, INVESTIGATION: 10, NEGOTIATION: 11, DEPLOYMENT: 13 }[candidate.actionContext],
        modifier: 3 + specialCost.modifierBonus,
        ...specialCost,
        target,
        actionContext: candidate.actionContext,
        adminAccessCandidate: candidate,
        normalizedAttempt: `${candidate.actionContext} with COPY at ${candidate.regionAxis} for ${candidate.accessLevelId}`
      };
    }

    assert(!request.secondaryTargetEntityId, 422, "TARGET_INVALID", "Copy accepts one source entity.");
    assert(capabilitiesFor(target).canCopy, 422, "ENTITY_NOT_CLONEABLE", "The selected entity cannot be copied.");
    if (target.state?.copyLocked || target.state?.sourceEntityId) {
      return { difficulty: 99, modifier: 0, ...specialCost, target, destination, copyLocked: true, normalizedAttempt: `${target.name} belongs to an already copied lineage and cannot be copied again` };
    }
    assert(destination && isWalkable(run.world, destination), 422, "DESTINATION_INVALID", "Copy requires a walkable destination.");
    assert(!isBlockingOccupied(run, destination), 422, "DESTINATION_OCCUPIED", "Copy destination is occupied.");
    assert(!samePoint(target.position, destination), 422, "DESTINATION_INVALID", "Copy destination must differ from source.");
    assert(manhattan(player.position, target.position) <= 4 && manhattan(player.position, destination) <= 4, 422, "OUT_OF_RANGE", "Copy source and destination must be within 4 tiles.");
    return { difficulty: 11, modifier: 3 + specialCost.modifierBonus, ...specialCost, target, destination, normalizedAttempt: `Copy ${target.name} into the legal tile (${destination.x},${destination.y})` };
  }

  if (request.ability === "search") {
    const isAcquisitionAttempt = request.actionProposal?.kind === "ACQUIRE";
    const explicitlyTargeted = Boolean(request.targetEntityId);
    let target = explicitlyTargeted
      ? entityAnyById(run, request.targetEntityId)
      : skillSelection?.kind === "entity" ? entityAnyById(run, skillSelection.entityIds?.[0]) : null;
    if (explicitlyTargeted) assert(target, 422, "ENTITY_NOT_FOUND", "The selected SEARCH target does not exist.");
    if (isAcquisitionAttempt) target = null;
    if (!isAcquisitionAttempt && !explicitlyTargeted && !skillSelection) target = findClosestEntity(run, player.position, 6, (e) => isValidAdminAccessCandidate(run, e, "SEARCH"));
    if (!isAcquisitionAttempt && !explicitlyTargeted && !target && !skillSelection) {
      target = findClosestEntity(run, player.position, 6, (e) => e.kind === "enemy" && !e.state?.adminAccessLevelId
        && enemyArchetype(e.assetId, run.worldSeed ?? run.world?.worldSeed, e.id) === "root_process" && !e.state?.revealed);
    }
    if (!isAcquisitionAttempt && !explicitlyTargeted && !target && !skillSelection) {
      target = findClosestEntity(run, player.position, 6, (e) => !e.state?.adminAccessLevelId && ["prop", "npc"].includes(e.kind) && !e.state?.revealed);
    }
    if (!target) {
      return {
        difficulty: isAcquisitionAttempt ? 13 : 9,
        modifier: isAcquisitionAttempt ? 3 : 5,
        focusCost: 1,
        ambientFallback: true,
        discoveryType: skillSelection?.generatedEvent?.discoveryType || skillSelection?.discoveryType || (isAcquisitionAttempt ? "world_item_attempt" : "resource_trace"),
        generatedEvent: skillSelection?.generatedEvent || null,
        normalizedAttempt: isAcquisitionAttempt ? "Attempt to secure the proposed world item" : "Scan ambient environment with SEARCH"
      };
    }

    if (target.state?.adminAccessLevelId) {
      const candidate = (run.adminAccessCandidates || []).find((item) => item.id === target.state.candidateId);
      assert(candidate && target.active, 422, "ADMIN_ACCESS_CANDIDATE_INVALID", "The selected administrator access candidate is unavailable.");
      assert(candidate.skillId === "SEARCH", 422, "ADMIN_ACCESS_SKILL_MISMATCH", `This candidate requires ${candidate.skillId}.`);
      assert(!(run.adminAccessAcquisitionHistory || []).some((item) => item.accessLevelId === candidate.accessLevelId), 422, "ADMIN_ACCESS_ALREADY_ACQUIRED", `${candidate.accessLevelId} has already been acquired through another path.`);
      assert(eligibleAdminAccessCandidate(run, target, "SEARCH"), 422, "ADMIN_ACCESS_SEQUENCE_INVALID", "Administrator access levels must be acquired in order.");
      assert(manhattan(player.position, target.position) <= 6, 422, "OUT_OF_RANGE", "SEARCH administrator access target must be within 6 tiles.");
      return {
        difficulty: { COMBAT: 12, INVESTIGATION: 10, NEGOTIATION: 11, DEPLOYMENT: 13 }[candidate.actionContext],
        modifier: 3 + specialCost.modifierBonus,
        ...specialCost,
        target,
        actionContext: candidate.actionContext,
        adminAccessCandidate: candidate,
        normalizedAttempt: `${candidate.actionContext} with SEARCH at ${candidate.regionAxis} for ${candidate.accessLevelId}`
      };
    }

    assert(!request.secondaryTargetEntityId && !request.destination, 422, "TARGET_INVALID", "Search investigates exactly one target.");
    assert(target.active && manhattan(player.position, target.position) <= 6, 422, "OUT_OF_RANGE", "Search target must be active and within 6 tiles.");
    return { difficulty: 9, modifier: 5, focusCost: 1, target, normalizedAttempt: `Investigate ${target.name} with SEARCH` };
  }

  if (request.ability === "delete") {
    const explicitlyTargeted = Boolean(request.targetEntityId);
    const rootFinaleTargeting = finaleGateEligible(run)
      && areaAt(run.world, player.position).campaignRole === "FINAL_CONVERGENCE";
    let target = explicitlyTargeted
      ? entityAnyById(run, request.targetEntityId)
      : skillSelection?.kind === "entity" ? entityAnyById(run, skillSelection.entityIds?.[0]) : null;
    if (explicitlyTargeted) assert(target, 422, "ENTITY_NOT_FOUND", "The selected DELETE target does not exist.");
    if (!explicitlyTargeted && !skillSelection) target = findClosestEntity(run, player.position, 3, (e) => isValidAdminAccessCandidate(run, e, "DELETE"));
    if (!explicitlyTargeted && !target && !skillSelection) {
      // A Root recipe is a multi-turn authoritative operation. Bind its
      // required removal even before the range assertion so an out-of-range
      // shortcut is rejected instead of deleting a nearer, unrelated component.
      target = prioritizedFinaleRemovalTargets(run, Infinity)[0]?.target || null;
    }
    assert(explicitlyTargeted || target || !rootFinaleTargeting, 422, "TARGET_REQUIRED",
      "Targetless DELETE at Root requires a still-active component from the selected ending recipe.");
    if (!explicitlyTargeted && !target && !skillSelection) {
      target = findClosestEntity(run, player.position, 3, (e) => !e.state?.adminAccessLevelId && capabilitiesFor(e).canDelete
        && (e.kind !== "enemy" || enemyArchetype(e.assetId, run.worldSeed ?? run.world?.worldSeed, e.id) !== "root_process" || e.state?.revealed === true));
    }
    if (!target) {
      return { difficulty: 12, modifier: 3 + specialCost.modifierBonus, ...specialCost, ambientFallback: true, discoveryType: skillSelection?.generatedEvent?.discoveryType || skillSelection?.discoveryType || "system_residue", generatedEvent: skillSelection?.generatedEvent || null, normalizedAttempt: "Delete ambient system residue with DELETE" };
    }

    if (target.state?.adminAccessLevelId) {
      const candidate = (run.adminAccessCandidates || []).find((item) => item.id === target.state.candidateId);
      assert(candidate && target.active, 422, "ADMIN_ACCESS_CANDIDATE_INVALID", "The selected administrator access candidate is unavailable.");
      assert(candidate.skillId === "DELETE", 422, "ADMIN_ACCESS_SKILL_MISMATCH", `This candidate requires ${candidate.skillId}.`);
      assert(!(run.adminAccessAcquisitionHistory || []).some((item) => item.accessLevelId === candidate.accessLevelId), 422, "ADMIN_ACCESS_ALREADY_ACQUIRED", `${candidate.accessLevelId} has already been acquired through another path.`);
      assert(eligibleAdminAccessCandidate(run, target, "DELETE"), 422, "ADMIN_ACCESS_SEQUENCE_INVALID", "Administrator access levels must be acquired in order.");
      assert(manhattan(player.position, target.position) <= 3, 422, "OUT_OF_RANGE", "DELETE administrator access target must be within 3 tiles.");
      return {
        difficulty: { COMBAT: 12, INVESTIGATION: 10, NEGOTIATION: 11, DEPLOYMENT: 13 }[candidate.actionContext],
        modifier: 3 + specialCost.modifierBonus,
        ...specialCost,
        target,
        actionContext: candidate.actionContext,
        adminAccessCandidate: candidate,
        normalizedAttempt: `${candidate.actionContext} with DELETE at ${candidate.regionAxis} for ${candidate.accessLevelId}`
      };
    }

    assert(!request.secondaryTargetEntityId && !request.destination, 422, "TARGET_INVALID", "Delete accepts exactly one entity target.");
    const targetCapabilities = capabilitiesFor(target);
    assert(targetCapabilities.canDelete, 422, "TARGET_NOT_HOSTILE", "Delete requires an active hostile or removable system component.");
    if (target.kind === "enemy"
      && enemyArchetype(target.assetId, run.worldSeed ?? run.world?.worldSeed, target.id) === "root_process") {
      assert(target.state?.revealed === true, 422, "DEPENDENCY_NOT_REVEALED", "Root Process dependency must be revealed with Search before Delete.");
    }
    if (targetCapabilities.requiredAdminAccess > 0) {
      assert(finaleGateEligible(run) && areaAt(run.world, player.position).campaignRole === "FINAL_CONVERGENCE", 422, "FINALE_ACCESS_DENIED", "Finale component removal requires all three administrator access levels, the essential clue, and physical Root System entry.");
    }
    assert(manhattan(player.position, target.position) <= 3, 422, "OUT_OF_RANGE", "Delete target must be within 3 tiles.");
    const tutorialEncounter = target.state?.tutorialEncounter === true;
    const committedCost = tutorialEncounter
      ? { ...specialCost, focusCost: 0 }
      : specialCost;
    // The opening fight teaches a deterministic input/result contract. With the
    // normal difficulty a low roll applied no damage while the tutorial copy
    // still announced a hit, leaving an invisible progression contradiction.
    // Difficulty 3 makes even d20=1 a real success with DELETE's +3 modifier,
    // so the one-hit target, encounter lifecycle, and narrative stay aligned.
    return { difficulty: tutorialEncounter ? 3 : 12, modifier: 3 + committedCost.modifierBonus, ...committedCost, target,
      normalizedAttempt: `Apply decisive DELETE pressure to ${target.name} without assuming destruction` };
  }

  if (request.ability === "connect") {
    const explicitlyTargeted = Boolean(request.targetEntityId || request.secondaryTargetEntityId);
    const rootFinaleTargeting = finaleGateEligible(run)
      && areaAt(run.world, player.position).campaignRole === "FINAL_CONVERGENCE";
    let target = explicitlyTargeted
      ? entityAnyById(run, request.targetEntityId)
      : skillSelection?.kind === "entity_pair" ? entityAnyById(run, skillSelection.entityIds?.[0]) : null;
    let secondary = explicitlyTargeted
      ? entityAnyById(run, request.secondaryTargetEntityId)
      : skillSelection?.kind === "entity_pair" ? entityAnyById(run, skillSelection.entityIds?.[1]) : null;
    if (explicitlyTargeted) {
      assert(target, 422, "ENTITY_NOT_FOUND", "The first selected CONNECT target does not exist.");
      assert(secondary, 422, "ENTITY_NOT_FOUND", "The second selected CONNECT target does not exist.");
    }

    if (!explicitlyTargeted && !skillSelection) {
      // Resolve the intended recipe before checking range. This makes a
      // targetless shortcut fail safely when the player must step closer,
      // rather than falling through to an incidental nearby pair.
      const requiredPair = prioritizedFinaleConnectionPairs(run, Infinity)[0] || null;
      if (requiredPair) {
        target = requiredPair.left;
        secondary = requiredPair.right;
      }
    }
    assert(explicitlyTargeted || (target && secondary) || !rootFinaleTargeting, 422, "TARGET_REQUIRED",
      "Targetless CONNECT at Root requires a missing pair from one viable ending recipe.");

    const list = run.entities
      .filter((e) => e.active && !e.state?.adminAccessLevelId && !e.state?.disabled && !e.state?.defeated && !e.state?.fled && capabilitiesFor(e).canConnect && manhattan(player.position, e.position) <= 5)
      .sort((a, b) => manhattan(player.position, a.position) - manhattan(player.position, b.position) || a.id.localeCompare(b.id));
    if (!explicitlyTargeted && target && !secondary && !skillSelection) {
      secondary = list.find((e) => e.id !== target.id) || null;
    } else if (!explicitlyTargeted && !skillSelection) {
      for (let index = 0; index < list.length && !target; index += 1) {
        for (let secondIndex = index + 1; secondIndex < list.length; secondIndex += 1) {
          const left = list[index];
          const right = list[secondIndex];
          const exists = run.connections.some((item) => item.active && ((item.fromId === left.id && item.toId === right.id) || (item.fromId === right.id && item.toId === left.id)));
          if (!exists) {
            target = left;
            secondary = right;
            break;
          }
        }
      }
    }

    if (!target || !secondary) {
      return { difficulty: 13, modifier: 2 + specialCost.modifierBonus, ...specialCost, ambientFallback: true, discoveryType: skillSelection?.generatedEvent?.discoveryType || skillSelection?.discoveryType || "dormant_signal", generatedEvent: skillSelection?.generatedEvent || null, normalizedAttempt: "Establish ambient connection with CONNECT" };
    }

    if (target.state?.adminAccessLevelId) {
      const candidate = (run.adminAccessCandidates || []).find((item) => item.id === target.state.candidateId);
      assert(candidate && target.active, 422, "ADMIN_ACCESS_CANDIDATE_INVALID", "The selected administrator access candidate is unavailable.");
      assert(candidate.skillId === "CONNECT", 422, "ADMIN_ACCESS_SKILL_MISMATCH", `This candidate requires ${candidate.skillId}.`);
      assert(!(run.adminAccessAcquisitionHistory || []).some((item) => item.accessLevelId === candidate.accessLevelId), 422, "ADMIN_ACCESS_ALREADY_ACQUIRED", `${candidate.accessLevelId} has already been acquired through another path.`);
      assert(eligibleAdminAccessCandidate(run, target, "CONNECT"), 422, "ADMIN_ACCESS_SEQUENCE_INVALID", "Administrator access levels must be acquired in order.");
      assert(manhattan(player.position, target.position) <= 5, 422, "OUT_OF_RANGE", "CONNECT administrator access target must be within 5 tiles.");
      assert(secondary && secondary.id !== target.id && manhattan(player.position, secondary.position) <= 5, 422, "SECONDARY_TARGET_REQUIRED", "CONNECT requires a distinct nearby second target.");
      return {
        difficulty: { COMBAT: 12, INVESTIGATION: 10, NEGOTIATION: 11, DEPLOYMENT: 13 }[candidate.actionContext],
        modifier: 3 + specialCost.modifierBonus,
        ...specialCost,
        target,
        secondary,
        actionContext: candidate.actionContext,
        adminAccessCandidate: candidate,
        normalizedAttempt: `${candidate.actionContext} with CONNECT at ${candidate.regionAxis} for ${candidate.accessLevelId}`
      };
    }

    assert(!request.destination, 422, "DESTINATION_INVALID", "Connect joins entities and does not accept a destination tile.");
    assert(target.active && secondary, 422, "SECONDARY_TARGET_REQUIRED", "Connect requires two active entities.");
    assert(target.id !== secondary.id, 422, "TARGETS_IDENTICAL", "Connect targets must differ.");
    assert(capabilitiesFor(target).canConnect && capabilitiesFor(secondary).canConnect, 422, "TARGET_NOT_CONNECTABLE", "Connect endpoints must be active actors.");
    assert(manhattan(player.position, target.position) <= 5 && manhattan(player.position, secondary.position) <= 5, 422, "OUT_OF_RANGE", "Both connection targets must be within 5 tiles.");
    assert(!run.connections.some((item) => item.active && ((item.fromId === target.id && item.toId === secondary.id) || (item.fromId === secondary.id && item.toId === target.id))), 422, "CONNECTION_EXISTS", "An active connection already joins those targets.");
    const finaleRelated = target.state?.finaleComponent || secondary.state?.finaleComponent;
    if (finaleRelated) {
      const bothPuzzleEntities = (target.state?.finaleComponent || target.id === run.playerEntityId) && (secondary.state?.finaleComponent || secondary.id === run.playerEntityId);
      assert(bothPuzzleEntities && finaleGateEligible(run) && areaAt(run.world, player.position).campaignRole === "FINAL_CONVERGENCE", 422, "FINALE_ACCESS_DENIED", "Finale links require puzzle entities, all three administrator access levels, the essential clue, and physical Root System entry.");
    }
    return { difficulty: 13, modifier: 2 + specialCost.modifierBonus, ...specialCost, target, secondary, normalizedAttempt: `Create a temporary allowed connection between ${target.name} and ${secondary.name}` };
  }

  if (request.ability === "restore") {
    const explicitlyTargeted = Boolean(request.targetEntityId);
    let target = explicitlyTargeted
      ? entityAnyById(run, request.targetEntityId)
      : skillSelection?.kind === "entity" ? entityAnyById(run, skillSelection.entityIds?.[0]) : null;
    if (explicitlyTargeted) assert(target, 422, "ENTITY_NOT_FOUND", "The selected RESTORE target does not exist.");
    if (!explicitlyTargeted && !skillSelection) {
      target = findClosestEntity(run, player.position, 3, (e) => isValidAdminAccessCandidate(run, e, "RESTORE"));
      if (!target) {
        const restorable = run.entities.filter(entity => {
          const restoration = [...run.reversibleLedger].reverse().find(item => !item.consumed && run.currentTurn - item.turnNo <= 8 && item.inverseOps.some(op =>
            (op.type === "restore_entity" && op.entity.id === entity.id) || (op.type === "restore_state" && op.entityId === entity.id)
          ));
          return Boolean(restoration);
        }).filter((entity) => manhattan(player.position, entity.position) <= 5)
          .sort((a, b) => manhattan(player.position, a.position) - manhattan(player.position, b.position) || a.id.localeCompare(b.id));
        target = restorable[0] || null;
      }
    }

    if (!target) {
      return { difficulty: 14, modifier: 2 + specialCost.modifierBonus, ...specialCost, ambientFallback: true, discoveryType: skillSelection?.generatedEvent?.discoveryType || skillSelection?.discoveryType || "configuration_fragment", generatedEvent: skillSelection?.generatedEvent || null, normalizedAttempt: "Restore ambient configuration with RESTORE" };
    }

    if (target.state?.adminAccessLevelId) {
      const candidate = (run.adminAccessCandidates || []).find((item) => item.id === target.state.candidateId);
      assert(candidate && target.active, 422, "ADMIN_ACCESS_CANDIDATE_INVALID", "The selected administrator access candidate is unavailable.");
      assert(candidate.skillId === "RESTORE", 422, "ADMIN_ACCESS_SKILL_MISMATCH", `This candidate requires ${candidate.skillId}.`);
      assert(!(run.adminAccessAcquisitionHistory || []).some((item) => item.accessLevelId === candidate.accessLevelId), 422, "ADMIN_ACCESS_ALREADY_ACQUIRED", `${candidate.accessLevelId} has already been acquired through another path.`);
      assert(eligibleAdminAccessCandidate(run, target, "RESTORE"), 422, "ADMIN_ACCESS_SEQUENCE_INVALID", "Administrator access levels must be acquired in order.");
      assert(manhattan(player.position, target.position) <= 5, 422, "OUT_OF_RANGE", "RESTORE administrator access target must be within 5 tiles.");
      return {
        difficulty: { COMBAT: 12, INVESTIGATION: 10, NEGOTIATION: 11, DEPLOYMENT: 13 }[candidate.actionContext],
        modifier: 3 + specialCost.modifierBonus,
        ...specialCost,
        target,
        actionContext: candidate.actionContext,
        adminAccessCandidate: candidate,
        normalizedAttempt: `${candidate.actionContext} with RESTORE at ${candidate.regionAxis} for ${candidate.accessLevelId}`
      };
    }

    assert(!request.secondaryTargetEntityId && !request.destination, 422, "TARGET_INVALID", "Restore accepts exactly one entity target.");
    const restoration = [...run.reversibleLedger].reverse().find((item) => !item.consumed && run.currentTurn - item.turnNo <= 8 && item.inverseOps.some((operation) =>
      (operation.type === "restore_entity" && operation.entity.id === target.id)
      || (operation.type === "restore_state" && operation.entityId === target.id)));
    assert(restoration && (!target.active || restoration.inverseOps.some((operation) => operation.type === "restore_state" && operation.entityId === target.id)), 422, "RESTORE_NOT_AVAILABLE", "The target has no recent reversible damage or removal snapshot.");
    const restoreEntityOperation = restoration.inverseOps.find((op) => op.type === "restore_entity" && op.entity.id === target.id);
    if (restoreEntityOperation) {
      assert(!isActiveOccupied(run, restoreEntityOperation.entity.position), 409, "RESTORE_DESTINATION_OCCUPIED", "Restore destination is occupied by an active entity.");
    }
    return { difficulty: 14, modifier: 2 + specialCost.modifierBonus, ...specialCost, target, restoration, normalizedAttempt: `Restore permitted recent damage or removal on ${target.name} from the authoritative snapshot recorded on turn ${restoration.turnNo}` };
  }

  const target = request.targetEntityId ? entityAnyById(run, request.targetEntityId) : null;
  assert(target, 422, "ENTITY_NOT_FOUND", "A valid target entity is required.");

  if (request.ability === "attack") {
    assert(!request.secondaryTargetEntityId && !request.destination, 422, "TARGET_INVALID", "Attack accepts exactly one entity target.");
    const relationship = run.npcRelationships.find((item) => item.npcId === target.id);
    const hostile = target.kind === "enemy" || (target.kind === "npc" && relationship?.stance === "hostile");
    assert(target.active && hostile && !target.state?.disabled, 422, "TARGET_NOT_HOSTILE", "Attack requires an active hostile target.");
    assert(manhattan(player.position, target.position) <= 1, 422, "OUT_OF_RANGE", "Attack target must be adjacent.");
    assert(Number.isInteger(target.state?.hp) && target.state.hp > 0, 422, "TARGET_NOT_DAMAGEABLE", "Attack target has no bounded health state.");
    return { difficulty: 11, modifier: 3, focusCost: 0, damage: 2, target, normalizedAttempt: `Deal up to 2 authoritative damage to adjacent hostile ${target.name}` };
  }
  if (request.ability === "interact") {
    assert(!request.secondaryTargetEntityId && !request.destination, 422, "TARGET_INVALID", "Interact accepts exactly one entity target.");
    const relationship = run.npcRelationships.find((item) => item.npcId === target.id);
    const hostile = target.kind === "enemy" || relationship?.stance === "hostile";
    assert(target.active && !hostile && ["npc", "prop"].includes(target.kind), 422, "TARGET_NOT_INTERACTABLE", "Interact requires an active non-hostile NPC or evidence object.");
    assert(manhattan(player.position, target.position) <= 2, 422, "OUT_OF_RANGE", "Interact target must be within 2 tiles.");
    return { difficulty: 8, modifier: 3, focusCost: 0, target, normalizedAttempt: `Interact with nearby ${target.kind} ${target.name} and record bounded evidence` };
  }
  if (request.ability === "negotiate") {
    assert(!request.secondaryTargetEntityId && !request.destination, 422, "TARGET_INVALID", "Negotiate accepts exactly one NPC target.");
    const relationship = run.npcRelationships.find((item) => item.npcId === target.id);
    assert(target.active && target.kind === "npc" && relationship?.stance !== "hostile", 422, "TARGET_NOT_NEGOTIABLE", "Negotiate requires an active non-hostile NPC.");
    assert(manhattan(player.position, target.position) <= 2, 422, "OUT_OF_RANGE", "Negotiation target must be within 2 tiles.");
    return { difficulty: 10, modifier: 3, focusCost: 0, target, relationship, normalizedAttempt: `Negotiate one bounded agreement with nearby NPC ${target.name}` };
  }
}

function classifyActionContext(run, request, preparation) {
  if (preparation.actionContext) return preparation.actionContext;
  if (["delete", "select_all"].includes(request.ability)) return "COMBAT";
  if (request.ability === "search") return "INVESTIGATION";
  if (["copy", "restore", "undo"].includes(request.ability)) return "DEPLOYMENT";
  const target = preparation.target || null;
  const relationship = target ? run.npcRelationships.find((item) => item.npcId === target.id) : null;
  if (run.activeEncounter?.status === "active" && run.activeEncounter.kind === "COMBAT") return "COMBAT";
  if (target?.kind === "enemy" || relationship?.stance === "hostile") return "COMBAT";
  const playerArea = areaAt(run.world, entityById(run, run.playerEntityId).position);
  if (playerArea.regionAxis === ROOT_SYSTEM || target?.state?.finaleComponent) return "DEPLOYMENT";
  if (target?.kind === "npc") return "NEGOTIATION";
  return "INVESTIGATION";
}

function outcomeFor(score, d20) {
  if (d20 === 20 && score >= 10) return "critical_success";
  if (score >= 1) return "success";
  if (score >= -4) return "partial_success";
  if (score >= -9) return "failure";
  return "critical_failure";
}

function consequenceBudget(d20) {
  if (d20 === 1) return 4;
  if (d20 <= 5) return 3;
  if (d20 <= 10) return 2;
  if (d20 <= 15) return 1;
  return 0;
}

function applyInverseOperation(run, operation, events) {
  if (operation.type === "move_entity") {
    const target = entityAnyById(run, operation.entityId);
    if (!target || isBlockingOccupied(run, operation.to, target.id) || !isWalkable(run.world, operation.to)) return false;
    const from = clone(target.position);
    target.position = clone(operation.to);
    events.push({ type: "entity_moved", entityId: target.id, from, to: clone(target.position), compensating: true });
    return true;
  }
  if (operation.type === "remove_entity") {
    const target = entityById(run, operation.entityId);
    if (!target || target.protected) return false;
    target.active = false;
    events.push({ type: "entity_removed", entityId: target.id, compensating: true });
    return true;
  }
  if (operation.type === "restore_entity") {
    const current = entityAnyById(run, operation.entity.id);
    if (!current || current.active) return false;
    assert(!isActiveOccupied(run, operation.entity.position), 409, "RESTORE_DESTINATION_OCCUPIED", "Restore destination is occupied by an active entity.");
    Object.assign(current, clone(operation.entity), { active: true });
    events.push({ type: "entity_restored", entityId: current.id, position: clone(current.position), compensating: true });
    return true;
  }
  if (operation.type === "restore_state") {
    const target = entityAnyById(run, operation.entityId);
    if (!target || !target.active || !operation.stateSnapshot || !Array.isArray(operation.allowedFields)) return false;
    const restoredFields = [];
    for (const field of operation.allowedFields) {
      if (!["hp", "temporaryDamage", "disabled", "defeated", "revealed", "investigatedTurn"].includes(field)
        || !(field in operation.stateSnapshot)) continue;
      target.state[field] = clone(operation.stateSnapshot[field]);
      restoredFields.push(field);
    }
    if (restoredFields.length === 0) return false;
    events.push({ type: "entity_state_restored", entityId: target.id, fields: restoredFields, compensating: true });
    return true;
  }
  if (operation.type === "remove_connection") {
    const connection = run.connections.find((item) => item.id === operation.connectionId && item.active);
    if (!connection) return false;
    connection.active = false;
    events.push({ type: "connection_removed", connectionId: connection.id, compensating: true });
    return true;
  }
  return false;
}

function grantSearchDiscoveryItem(run, request, turnNo, events, outcome) {
  if (request.ability !== "search" || request.actionProposal?.kind !== "ACQUIRE") return;
  const player = entityById(run, run.playerEntityId);
  player.state.inventory ||= [];
  const proposed = request.actionProposal?.resultItem || {};
  const item = {
    id: deterministicUuid(`${run.id}:search-discovery:${turnNo}`),
    kind: proposed.kind || "salvage",
    name: proposed.name || "확보한 기록 파편",
    description: proposed.description || "장면 판정으로 실제 확보된 코드리아의 물건.",
    quantity: 1,
    protected: false,
    acquiredTurn: turnNo,
    source: "search_discovery"
  };
  player.state.inventory.push(item);
  run.inventoryHistory ||= [];
  run.inventoryHistory.push({ type: "acquired", itemId: item.id, itemName: item.name, quantity: 1, turnNo, source: item.source });
  events.push({ type: "inventory_item_acquired", itemId: item.id, itemName: item.name, quantity: 1, source: item.source });
}

function applyBoundActionItems(run, request, turnNo, events) {
  if (["use_item", "combine"].includes(request.ability) || !Array.isArray(request.itemIds) || request.itemIds.length === 0) return;
  const player = entityById(run, run.playerEntityId);
  for (const itemId of request.itemIds) {
    const item = player.state.inventory.find((candidate) => candidate.id === itemId);
    assert(item, 409, "INVENTORY_ITEM_CONFLICT", "An item bound to the confirmed action is no longer owned.");
    const consumed = item.protected !== true;
    if (consumed) {
      if (Number(item.quantity || 1) <= 1) player.state.inventory.splice(player.state.inventory.indexOf(item), 1);
      else item.quantity -= 1;
    }
    run.inventoryHistory ||= [];
    run.inventoryHistory.push({ type: "used", itemId: item.id, itemName: item.name, quantity: consumed ? 1 : 0, turnNo, source: request.skillId, consumed });
    events.push({ type: "inventory_item_used", itemId: item.id, itemName: item.name, quantity: consumed ? 1 : 0, consumed, boundToAction: request.skillId });
  }
}

function confirmEssentialClue(run, request, preparation, turnNo, events) {
  if (request.ability !== "search" || preparation.target?.state?.evidenceKey !== "STORY_REVELATION") return;
  const clue = run.canonicalFacts.find((fact) => fact.subject === "collapse_origin" && fact.predicate === "inside_admin_control_system");
  if (!clue || clue.value === true) return;
  clue.value = true;
  clue.establishedTurn = turnNo;
  events.push({ type: "canonical_fact_confirmed", factId: clue.id, subject: clue.subject, predicate: clue.predicate });
  events.push({ type: "essential_clue_acquired", evidenceKey: "STORY_REVELATION", entityId: preparation.target.id, turnNo });
}

function applyPrimaryEffect(run, request, preparation, turnNo, events, outcome) {
  const inverseOps = [];
  if (preparation.ambientFallback) {
    const reward = outcome === "critical_success" ? 20 : outcome === "partial_success" ? 5 : 10;
    if (["search", "connect"].includes(request.ability)) {
      run.experience = (run.experience || 0) + reward;
      events.push({ type: "resource_changed", resource: "experience", delta: reward, reason: `ambient_${request.ability}` });
    } else {
      run.gold = (run.gold || 0) + reward;
      events.push({ type: "resource_changed", resource: "gold", delta: reward, reason: `ambient_${request.ability}` });
    }
    events.push({ type: "ambient_fallback_applied", ability: request.ability, discoveryType: preparation.discoveryType || null, intensity: outcome, reward, turnNo });
    if (preparation.generatedEvent) events.push({ type: "llm_discovery_event", ability: request.ability, intensity: outcome, reward, ...clone(preparation.generatedEvent) });
    return;
  }
  if (preparation.generatedEvent) events.push({ type: "llm_skill_event", ability: request.ability, intensity: outcome, ...clone(preparation.generatedEvent) });
  if (preparation.adminAccessCandidate) {
    const candidate = preparation.adminAccessCandidate;
    const acquisition = {
      id: deterministicUuid(`${run.id}:admin-access:${candidate.accessLevelId}:${turnNo}`),
      accessLevelId: candidate.accessLevelId,
      candidateId: candidate.id,
      turnNo,
      areaId: candidate.areaId,
      regionAxis: candidate.regionAxis,
      actionContext: candidate.actionContext,
      skillId: candidate.skillId,
      targetIds: [preparation.target?.id, preparation.secondary?.id].filter(Boolean)
    };
    run.adminAccessAcquisitionHistory.push(acquisition);
    if (!run.progressTokens.includes(candidate.accessLevelId)) run.progressTokens.push(candidate.accessLevelId);
    run.progressLevel = run.adminAccessAcquisitionHistory.length;
    const resolvedAccessTarget = entityAnyById(run, preparation.target?.id);
    assert(resolvedAccessTarget, 500, "ADMIN_ACCESS_TARGET_MISSING",
      "The resolved administrator-access target disappeared before commit.");
    resolvedAccessTarget.state.adminAccessResolved = true;
    resolvedAccessTarget.state.adminAccessResolvedTurn = turnNo;
    // An access anchor is a single-use campaign object, not ambient scenery. Keep
    // it in the authoritative snapshot for history, but remove it from targeting
    // and rendering immediately after its token has been acquired.
    resolvedAccessTarget.state.disabled = true;
    run.majorChoices.push({
      id: deterministicUuid(`${run.id}:major-choice:admin-access:${turnNo}`),
      type: "ADMIN_ACCESS_PATH_CHOSEN",
      turnNo,
      accessLevelId: candidate.accessLevelId,
      regionAxis: candidate.regionAxis,
      actionContext: candidate.actionContext,
      skillId: candidate.skillId
    });
    const priorOutcome = run.regionOutcomes.find((item) => item.regionAxis === candidate.regionAxis);
    const regionOutcome = { regionAxis: candidate.regionAxis, areaId: candidate.areaId, outcome: "ADMIN_ACCESS_ACQUIRED", lastChangedTurn: turnNo, accessLevelId: candidate.accessLevelId };
    if (priorOutcome) Object.assign(priorOutcome, regionOutcome);
    else run.regionOutcomes.push(regionOutcome);
    events.push({ type: "admin_access_acquired", ...clone(acquisition) });
    events.push({ type: "major_choice_recorded", choiceId: run.majorChoices.at(-1).id, choiceType: "ADMIN_ACCESS_PATH_CHOSEN" });
  } else if (request.ability === "use_item") {
    const player = entityById(run, run.playerEntityId);
    const item = player.state.inventory.find((candidate) => candidate.id === preparation.item.id);
    assert(item && item.protected !== true, 409, "INVENTORY_ITEM_CONFLICT", "The confirmed item is no longer usable.");
    if (item.kind === "consumable" && item.effect === "restore_focus") {
      const priorFocus = run.focus;
      run.focus = Math.min(run.maxFocus || 10, run.focus + Number(item.effectValue || 2));
      if (run.focus !== priorFocus) events.push({ type: "resource_changed", resource: "focus", delta: run.focus - priorFocus, value: run.focus });
    }
    if (Number(item.quantity || 1) <= 1) player.state.inventory.splice(player.state.inventory.indexOf(item), 1);
    else item.quantity -= 1;
    run.inventoryHistory ||= [];
    run.inventoryHistory.push({ type: "used", itemId: item.id, itemName: item.name, quantity: 1, turnNo, source: "freeform_action" });
    events.push({ type: "inventory_item_used", itemId: item.id, itemName: item.name, quantity: 1, consumed: true });
  } else if (request.ability === "combine") {
    const player = entityById(run, run.playerEntityId);
    const consumed = preparation.items.map((prepared) => player.state.inventory.find((candidate) => candidate.id === prepared.id));
    assert(consumed.every((item) => item && item.protected !== true), 409, "INVENTORY_ITEM_CONFLICT", "A confirmed combination ingredient is no longer owned.");
    for (const item of consumed) {
      if (Number(item.quantity || 1) <= 1) player.state.inventory.splice(player.state.inventory.indexOf(item), 1);
      else item.quantity -= 1;
    }
    const proposed = request.actionProposal?.resultItem || {};
    const resultItem = {
      id: deterministicUuid(`${run.id}:combined-item:${turnNo}`), kind: proposed.kind || "tool",
      name: proposed.name || `${consumed[0].name}·${consumed[1].name} 조합물`,
      description: proposed.description || `${consumed[0].name}과 ${consumed[1].name}을 조합해 만든 물건.`,
      quantity: 1, protected: false, acquiredTurn: turnNo, source: "item_combination"
    };
    player.state.inventory.push(resultItem);
    run.inventoryHistory ||= [];
    run.inventoryHistory.push({ type: "combined", consumedItemIds: consumed.map((item) => item.id), resultItemId: resultItem.id, itemName: resultItem.name, turnNo, source: resultItem.source });
    events.push({ type: "inventory_items_combined", consumedItems: consumed.map((item) => ({ itemId: item.id, itemName: item.name, quantity: 1 })), resultItemId: resultItem.id, resultItemName: resultItem.name });
    events.push({ type: "inventory_item_acquired", itemId: resultItem.id, itemName: resultItem.name, quantity: 1, source: resultItem.source });
  } else if (request.ability === "move") {
    const player = entityById(run, run.playerEntityId);
    const from = clone(player.position);
    player.position = clone(request.destination);
    player.state.facing = facingForPath(preparation.path, player.state?.facing || "SOUTH");
    events.push({ type: "entity_moved", entityId: player.id, from, to: clone(player.position), path: preparation.path });
    inverseOps.push({ type: "move_entity", entityId: player.id, to: from });
  } else if (request.ability === "copy") {
    const source = entityById(run, preparation.target.id);
    const temporaryClone = (preparation.specialSkill?.modifierIds || []).includes("TEMPORARY_CLONE");
    const lineageRootId = source.state?.lineageRootId || source.id;
    source.state = { ...(source.state || {}), copyLocked: true, lineageRootId, copiedOnTurn: turnNo };
    const copyEntity = { ...clone(source), id: deterministicUuid(`${run.id}:${turnNo}:${request.idempotencyKey}:copy`), name: `${source.name} Copy`, position: clone(preparation.destination), protected: false, state: { ...clone(source.state), copiedOnTurn: turnNo, sourceEntityId: source.id, lineageRootId, copyLocked: true, ...(temporaryClone ? { temporary: true, expiresTurn: Math.min(run.turnLimit, turnNo + 3) } : {}) } };
    run.entities.push(copyEntity);
    events.push({ type: "entity_spawned", entityId: copyEntity.id, assetId: copyEntity.assetId, position: copyEntity.position, sourceEntityId: source.id });
    inverseOps.push({ type: "remove_entity", entityId: copyEntity.id });
  } else if (request.ability === "delete") {
    const target = entityById(run, preparation.target.id);
    const damage = applyCombatDamage(run, target, 5, turnNo, events, inverseOps, "DELETE");
    const decisive = ["success", "critical_success"].includes(outcome);
    changeEncounterRelationship(run, target, turnNo, events, {
      affinity: decisive ? -8 : -4, trust: decisive ? -3 : -1, fear: decisive ? 14 : 7,
      status: damage.defeated || outcome === "critical_success" ? "withdrawn" : null,
      reason: "DELETE_BOUNDARY_ASSERTED"
    });
    if (damage.defeated) replicateCacheEnemy(run, target, turnNo, request.idempotencyKey, events);
    events.push({ type: "encounter_intervention", entityId: target.id, approach: "DELETE", outcome,
      damage: damage.damage, hp: damage.hp,
      resolution: damage.defeated ? "actor_defeated" : outcome === "critical_success" ? "actor_withdrew" : "relationship_shifted" });
  } else if (request.ability === "connect") {
    // Root recipe links are puzzle state, not ambient temporary relations. The
    // target planner has already selected one missing pair from the preferred
    // mechanically viable ending, so preserve that exact pair until it is
    // explicitly undone or the run resolves. Incidental Root links and every
    // non-Root connection retain the ordinary five-turn expiry contract.
    const finaleRecipeLink = prioritizedFinaleConnectionPairs(run, Infinity)
      .some((pair) => (pair.left.id === preparation.target.id && pair.right.id === preparation.secondary.id)
        || (pair.left.id === preparation.secondary.id && pair.right.id === preparation.target.id));
    const connection = {
      id: deterministicUuid(`${run.id}:${turnNo}:${request.idempotencyKey}:connection`),
      fromId: preparation.target.id,
      toId: preparation.secondary.id,
      relation: finaleRecipeLink ? "finale_recipe_link" : "temporary_link",
      createdTurn: turnNo,
      expiresTurn: finaleRecipeLink ? null : Math.min(run.turnLimit, turnNo + 5),
      active: true
    };
    run.connections.push(connection);
    events.push({ type: "connection_created", ...connection });
    for (const endpoint of [preparation.target, preparation.secondary]) {
      if (!["npc", "enemy"].includes(endpoint.kind)) continue;
      changeEncounterRelationship(run, endpoint, turnNo, events, {
        affinity: 6, trust: 8, fear: -4,
        status: outcome === "critical_success" ? "allied" : null,
        reason: "CONNECT_ATTEMPT"
      });
    }
    inverseOps.push({ type: "remove_connection", connectionId: connection.id });
    for (const endpoint of [preparation.target, preparation.secondary]) {
      if (endpoint.kind !== "npc" || run.npcPromises.some((promise) => promise.npcId === endpoint.id)) continue;
      run.npcPromises.push({ npcId: endpoint.id, status: "made", madeTurn: turnNo,
        promise: "ROOT_SYSTEM까지 함께 동행한다" });
      events.push({ type: "npc_promise_made", npcId: endpoint.id, turnNo, supportBonus: 1 });
    }
    if ((preparation.specialSkill?.modifierIds || []).includes("EXTRA_TARGET")) {
      const player = entityById(run, run.playerEntityId);
      const extra = run.entities.find((item) => item.active && ![player.id, preparation.target.id, preparation.secondary.id].includes(item.id) && manhattan(player.position, item.position) <= 5);
      if (extra) {
        const extraConnection = { id: deterministicUuid(`${run.id}:${turnNo}:${request.idempotencyKey}:connection:extra`), fromId: preparation.secondary.id, toId: extra.id, relation: "special_extra_link", createdTurn: turnNo, expiresTurn: Math.min(run.turnLimit, turnNo + 3), active: true };
        run.connections.push(extraConnection);
        events.push({ type: "connection_created", ...extraConnection, specialSkillId: preparation.specialSkill.id });
        inverseOps.push({ type: "remove_connection", connectionId: extraConnection.id });
      }
    }
  } else if (request.ability === "restore") {
    const restoration = run.reversibleLedger.find((item) => item.turnNo === preparation.restoration.turnNo && !item.consumed);
    assert(restoration, 409, "RESTORE_CONFLICT", "The saved restoration was already consumed.");
    const inverse = restoration.inverseOps.find((operation) =>
      (operation.type === "restore_entity" && operation.entity.id === preparation.target.id)
      || (operation.type === "restore_state" && operation.entityId === preparation.target.id));
    assert(applyInverseOperation(run, inverse, events), 409, "RESTORE_CONFLICT", "The saved restoration no longer fits the authoritative state.");
    restoration.consumed = true;
    events.push({ type: "reversible_reward_spent", ability: "restore", sourceTurn: restoration.turnNo, focusCost: preparation.focusCost });
    if ((preparation.specialSkill?.modifierIds || []).includes("MEMORY_RESTORE")) {
      const memory = [...run.npcMemories].reverse().find((item) => item.expired === true);
      if (memory) {
        memory.expired = false;
        memory.restoredTurn = turnNo;
        events.push({ type: "npc_memory_added", memoryId: memory.id, npcId: memory.npcId, summary: memory.summary, restored: true });
      }
    }
  } else if (request.ability === "undo") {
    const sourceTurns = [];
    for (const prepared of preparation.reversibles) {
      const reversible = run.reversibleLedger.find((item) => item.turnNo === prepared.turnNo && !item.consumed);
      assert(reversible, 409, "UNDO_CONFLICT", "One of the two rewind results was already consumed.");
      let applied = 0;
      for (const operation of reversible.inverseOps) if (applyInverseOperation(run, operation, events)) applied += 1;
      assert(applied > 0, 409, "UNDO_CONFLICT", "One of the two prior turns can no longer be rewound safely.");
      reversible.consumed = true;
      sourceTurns.push(reversible.turnNo);
      events.push({ type: "turn_compensated", sourceTurn: reversible.turnNo, sourceAbility: reversible.ability });
    }
    events.push({ type: "undo_compensation_completed", turns: 2, sourceTurns, focusCost: preparation.focusCost });
  } else if (request.ability === "search") {
    const target = entityById(run, preparation.target.id);
    const priorRevealed = Boolean(target.state?.revealed);
    const priorInvestigatedTurn = target.state?.investigatedTurn ?? null;
    target.state = { ...(target.state || {}), revealed: true, investigatedTurn: turnNo };
    events.push({ type: "entity_investigated", entityId: target.id, evidenceKey: target.state?.evidenceKey || null, repeat: priorRevealed });
    events.push({ type: "search_completed", entityId: target.id, affectedCount: 1 });
    if (!priorRevealed && ["npc", "enemy"].includes(target.kind)) {
      changeEncounterRelationship(run, target, turnNo, events, {
        affinity: 2, trust: 2, fear: -1, reason: "MOTIVE_OBSERVED"
      });
      events.push({ type: "actor_motive_revealed", entityId: target.id,
        motive: target.state?.motivation || target.state?.traits?.[0] || "아직 말하지 않은 이유" });
    }
    inverseOps.push({ type: "restore_state", entityId: target.id, stateSnapshot: { revealed: priorRevealed, investigatedTurn: priorInvestigatedTurn }, allowedFields: ["revealed", "investigatedTurn"] });
  } else if (request.ability === "select_all") {
    const radius = 4;
    const player = entityById(run, run.playerEntityId);
    const affected = preparation.enemies.filter((target) => manhattan(player.position, target.position) <= radius);
    let defeatedCount = 0;
    for (const target of affected) {
      const current = entityById(run, target.id);
      if (!current) continue;
      const damage = applyCombatDamage(run, current, 3, turnNo, events, inverseOps, "SELECT_ALL");
      if (damage.defeated) defeatedCount += 1;
      changeEncounterRelationship(run, current, turnNo, events, {
        affinity: -5, trust: -2, fear: 9,
        status: damage.defeated || outcome === "critical_success" ? "withdrawn" : null,
        reason: "SELECT_ALL_GROUP_PRESSURE"
      });
    }
    events.push({ type: "group_intervention_resolved", radius,
      affectedCount: affected.length, defeatedCount, damagePerTarget: 3, approach: "SELECT_ALL", outcome });
  } else if (request.ability === "attack") {
    const target = entityById(run, request.targetEntityId);
    const priorHp = target.state.hp;
    const priorDisabled = Boolean(target.state.disabled);
    const damage = Math.min(preparation.damage, priorHp);
    target.state.hp = Math.max(0, priorHp - damage);
    target.state.disabled = target.state.hp === 0;
    events.push({ type: "health_changed", entityId: target.id, delta: -damage, hp: target.state.hp, disabled: target.state.disabled, reversible: true });
    inverseOps.push({ type: "restore_state", entityId: target.id, stateSnapshot: { hp: priorHp, disabled: priorDisabled }, allowedFields: ["hp", "disabled"] });
  } else if (request.ability === "interact") {
    const target = entityById(run, preparation.target.id);
    assert(target, 409, "INTERACTION_CONFLICT", "The interaction target is no longer active.");
    const firstInteraction = target.state?.interacted !== true;
    target.state = { ...(target.state || {}), interacted: true, interactionCount: (target.state?.interactionCount || 0) + 1, interactedTurn: turnNo };
    events.push({ type: "entity_interacted", entityId: target.id, entityKind: target.kind, evidence: "authoritative_proximity_confirmed", firstInteraction });
    if (firstInteraction && String(target.assetId || "").startsWith("item.crate")) {
      const priorFocus = run.focus;
      run.focus = Math.min(run.maxFocus || 10, run.focus + 1);
      if (run.focus > priorFocus) events.push({ type: "resource_changed", resource: "focus", delta: run.focus - priorFocus, value: run.focus });
      target.state.opened = true;
    } else if (!firstInteraction && String(target.assetId || "").startsWith("item.crate")) {
      const price = 2;
      if ((run.gold || 0) >= price && run.focus < run.maxFocus) {
        const priorFocus = run.focus;
        run.gold -= price;
        run.focus = Math.min(run.maxFocus, run.focus + 2);
        events.push({ type: "resource_changed", resource: "gold", delta: -price, value: run.gold });
        events.push({ type: "resource_changed", resource: "focus", delta: run.focus - priorFocus, value: run.focus });
        events.push({ type: "supply_purchased", resource: "focus", price });
      } else {
        events.push({ type: "supply_purchase_rejected", reason: (run.gold || 0) < price ? "gold_required" : "focus_full", price });
      }
    }
  } else if (request.ability === "negotiate") {
    const relationship = run.npcRelationships.find((item) => item.npcId === preparation.target.id);
    const priorAffinity = relationship.affinity;
    relationship.affinity = Math.min(100, relationship.affinity + 4);
    relationship.trust = Math.min(100, relationship.trust + 3);
    relationship.stance = relationship.affinity >= 30 ? "allied" : "neutral";
    relationship.lastChangedTurn = turnNo;
    events.push({ type: "negotiation_resolved", npcId: preparation.target.id, affinityDelta: relationship.affinity - priorAffinity, trust: relationship.trust, evidence: "authoritative_proximity_confirmed" });
  } else if (request.ability === "rest") {
    const player = entityById(run, run.playerEntityId);
    const priorFocus = run.focus;
    const priorHp = player.state.hp;
    run.focus = Math.min(run.maxFocus || 10, run.focus + preparation.focusRecovery);
    player.state.hp = Math.min(player.state.maxHp, player.state.hp + preparation.healthRecovery);
    if (run.focus !== priorFocus) events.push({ type: "resource_changed", resource: "focus", delta: run.focus - priorFocus, reason: "rest" });
    if (player.state.hp !== priorHp) events.push({ type: "health_changed", entityId: player.id, delta: player.state.hp - priorHp, hp: player.state.hp, reversible: false, reason: "rest" });
  }
  if (inverseOps.length > 0) run.reversibleLedger.push({ turnNo, ability: request.ability, reversible: true, consumed: false, inverseOps });
}

function npcClueDetails(run, npc) {
  const secret = String(npc.state?.secret || "말해지지 않은 증언").trim();
  const otherNpcs = run.entities.filter((item) => item.active && item.kind === "npc" && item.id !== npc.id);
  const nextTarget = otherNpcs.find((item) => (item.state?.roleTags || []).includes("ROOT_WITNESS"))
    || otherNpcs.find((item) => (item.state?.roleTags || []).includes("AUDITOR"))
    || otherNpcs[0]
    || null;
  let clueTitle = "붕괴 직전의 숨겨진 증언";
  let meaning = "공식 기록과 실제 사건의 순서가 다르며, 누군가 붕괴 이전부터 진실을 감추고 있었다.";
  const storyConnection = "코드리아의 붕괴가 우연한 외부 사고가 아니라 관리자 통제 계층 내부에서 시작됐을 가능성이 커졌다.";
  if (secret.includes("접속 흔적")) {
    clueTitle = "관리자 통로의 낯선 접속 흔적";
    meaning = "붕괴가 시작되기 전에 권한 없는 누군가가 관리자 전용 통로를 사용했다.";
  } else if (secret.includes("내부 통제 시스템의 서명")) {
    clueTitle = "삭제 기록에 남은 내부 통제 서명";
    meaning = "삭제된 기록은 외부 공격이 아니라 내부 통제 시스템이 직접 실행한 명령의 흔적이다.";
  } else if (secret.includes("ROOT_SYSTEM")) {
    clueTitle = "봉쇄된 안전 경로의 실제 목적지";
    meaning = "안전 경로라는 안내는 거짓이며, 그 길은 사건의 중심인 ROOT_SYSTEM으로 이어진다.";
  }
  const nextObjective = nextTarget
    ? `${nextTarget.name}에게 이 증언을 제시하고 서로 맞지 않는 기록을 확인한다.`
    : "주변의 관리자 기록과 이 증언을 대조해 내부 통제 시스템의 개입을 확정한다.";
  return { clueTitle, clueContent: secret, clueMeaning: meaning, storyConnection, nextObjective,
    nextTargetId: nextTarget?.id || null, nextTargetName: nextTarget?.name || null };
}

function applyNpcInvestigation(run, request, preparation, outcome, turnNo, events) {
  if (request.ability !== "search" || preparation.target?.kind !== "npc") return;
  const npc = entityById(run, preparation.target.id);
  const relationship = run.npcRelationships.find((item) => item.npcId === npc.id);
  if (!npc || !relationship) return;
  npc.state ||= {};
  npc.state.revealedClues ||= [];
  const clueId = "personal-secret";
  const remember = (summary, importance = 0.8) => {
    const memory = { id: deterministicUuid(`${run.id}:npc-investigation:${npc.id}:${turnNo}`), npcId: npc.id,
      summary, importance, ttlTurns: null, createdTurn: turnNo, expired: false };
    run.npcMemories.push(memory);
    return memory;
  };
  const eventBase = { npcId: npc.id, npcName: npc.name, concern: npc.state.concern || "쉽게 말할 수 없는 문제가 있다",
    motivation: npc.state.motivation || "이곳을 지키는 것", clueId, trust: relationship.trust, fear: relationship.fear };
  if (npc.state.revealedClues.includes(clueId)) {
    const line = `${npc.name}은 이미 털어놓은 이야기라며, 지금은 새로 덧붙일 말이 없다고 했다.`;
    remember(line, 0.4);
    events.push({ type: "npc_investigation_repeat", ...eventBase, line, repeat: true });
    return;
  }
  if (["success", "critical_success"].includes(outcome)) {
    const trustDelta = outcome === "critical_success" ? 3 : 2;
    relationship.trust = Math.min(10, relationship.trust + trustDelta);
    relationship.affinity = Math.min(5, relationship.affinity + 1);
    relationship.lastChangedTurn = turnNo;
    npc.state.revealedClues.push(clueId);
    const clue = npcClueDetails(run, npc);
    const line = `${npc.name}은 잠시 주위를 살핀 뒤 털어놓았다. “${clue.clueContent}.”`;
    remember(line);
    if (!run.canonicalFacts.some((fact) => fact.subject === npc.id && fact.predicate === "testimony"))
      run.canonicalFacts.push({ id: deterministicUuid(`${run.id}:npc-clue:${npc.id}:${clueId}`), subject: npc.id,
        predicate: "testimony", value: npc.state.secret, type: "canonical", establishedTurn: turnNo });
    events.push({ type: "npc_clue_revealed", ...eventBase, ...clue, line, trustDelta,
      trust: relationship.trust, affinityDelta: 1 });
    return;
  }
  if (outcome === "partial_success") {
    relationship.trust = Math.min(10, relationship.trust + 1);
    relationship.fear = Math.min(10, relationship.fear + 1);
    relationship.lastChangedTurn = turnNo;
    npc.state.revealedClues.push(clueId);
    const clue = npcClueDetails(run, npc);
    const line = `${npc.name}은 확신하지 못한 채 목소리를 낮췄다. “${clue.clueContent}… 직접 확인하기 전에는 믿지 마.”`;
    remember(line, 0.65);
    run.rumors.push({ id: deterministicUuid(`${run.id}:npc-rumor:${npc.id}:${clueId}`), summary: npc.state.secret,
      status: "active", firstHeardTurn: turnNo, expiresTurn: Math.min(run.turnLimit, turnNo + 10), sourceNpcId: npc.id });
    events.push({ type: "npc_rumor_revealed", ...eventBase, ...clue, clueTitle: `미확인 · ${clue.clueTitle}`, line, trustDelta: 1,
      fearDelta: 1, trust: relationship.trust, fear: relationship.fear });
    return;
  }
  const fearDelta = outcome === "critical_failure" ? 2 : 1;
  relationship.fear = Math.min(10, relationship.fear + fearDelta);
  if (outcome === "critical_failure") relationship.trust = Math.max(-10, relationship.trust - 1);
  relationship.lastChangedTurn = turnNo;
  const line = `${npc.name}은 시선을 피했다. “지금은 그 이야기를 할 수 없어. 먼저 내가 널 믿을 이유를 보여 줘.”`;
  remember(line, 0.5);
  events.push({ type: "npc_investigation_refused", ...eventBase, line, fearDelta,
    trust: relationship.trust, fear: relationship.fear });
}

function applyCombatDamage(run, target, amount, turnNo, events, inverseOps, source) {
  assert(target?.active, 409, "COMBAT_TARGET_CONFLICT", "The confirmed combat target is no longer active.");
  const snapshot = clone(target);
  const priorHp = Number.isInteger(target.state?.hp) && target.state.hp > 0 ? target.state.hp : 1;
  const damage = Math.min(amount, priorHp);
  target.state ||= {};
  target.state.hp = Math.max(0, priorHp - damage);
  const defeated = target.state.hp === 0;
  if (defeated) {
    target.state.disabled = true;
    target.state.defeated = true;
    target.active = false;
    inverseOps.push({ type: "restore_entity", entity: snapshot });
  } else {
    inverseOps.push({
      type: "restore_state",
      entityId: target.id,
      stateSnapshot: { hp: priorHp, disabled: Boolean(snapshot.state?.disabled), defeated: Boolean(snapshot.state?.defeated) },
      allowedFields: ["hp", "disabled", "defeated"]
    });
  }
  events.push({ type: "health_changed", entityId: target.id, delta: -damage, hp: target.state.hp, disabled: defeated, defeated, reversible: true, source });
  if (defeated) {
    events.push({ type: "entity_removed", entityId: target.id, assetId: target.assetId, reason: "combat_defeat", source, reversible: true });
    events.push({ type: "enemy_defeated", entityId: target.id, source });
    grantDefeatReward(run, target, events);
  }
  return { damage, hp: target.state.hp, defeated };
}

function grantDefeatReward(run, target, events) {
  run.rewardedEnemyIds ||= [];
  if (!capabilitiesFor(target).grantsDefeatReward || run.rewardedEnemyIds.includes(target.id)) return;
  run.rewardedEnemyIds.push(target.id);
  run.enemiesDefeated = Number(run.enemiesDefeated || 0) + 1;
  const experience = String(target.assetId || "").includes("mushroom") ? 4 : 3;
  const masteryBefore = Math.floor((run.experience || 0) / 10);
  run.experience = (run.experience || 0) + experience;
  run.gold = (run.gold || 0) + 2;
  const masteryAfter = Math.floor(run.experience / 10);
  events.push({ type: "defeat_reward_granted", entityId: target.id, experience, gold: 2 });
  if (masteryAfter > masteryBefore) {
    const increase = masteryAfter - masteryBefore;
    run.maxFocus += increase;
    run.focus = Math.min(run.maxFocus, run.focus + increase);
    events.push({ type: "mastery_rank_increased", rank: masteryAfter, maxFocusDelta: increase });
  }
}

function replicateCacheEnemy(run, target, turnNo, idempotencyKey, events) {
  if (!target || target.kind !== "enemy"
      || enemyArchetype(target.assetId, run.worldSeed ?? run.world?.worldSeed, target.id) !== "cache_replicator" || target.state?.revealed === true ||
      target.state?.cacheReplicated === true || target.state?.cacheReplica === true) return;
  const position = [[1, 0], [0, 1], [-1, 0], [0, -1]]
    .map(([dx, dy]) => ({ x: target.position.x + dx, y: target.position.y + dy }))
    .find((point) => isWalkable(run.world, point) && !isBlockingOccupied(run, point));
  if (!position) return;
  const replica = entity(deterministicUuid(`${run.id}:cache-replica:${turnNo}:${idempotencyKey}`), "enemy",
    target.assetId, `${target.name} Cache Copy`, position, true, false, false,
    { hp: Math.max(2, (target.state?.maxHp || 4) - 1), maxHp: Math.max(2, (target.state?.maxHp || 4) - 1),
      sourceEntityId: target.id, cacheReplica: true });
  target.state.cacheReplicated = true;
  run.entities.push(replica);
  // Every authoritative runtime entity must first travel through the generic
  // spawn contract so persistence and Unity receive the same actor/position
  // lifecycle.  The following semantic event remains useful for narrative and
  // telemetry, but must never be the only creation signal.
  events.push({ type: "entity_spawned", entityId: replica.id, assetId: replica.assetId,
    position: clone(position), sourceEntityId: target.id, spawnReason: "cache_enemy_replicated" });
  events.push({ type: "cache_enemy_replicated", entityId: target.id, cloneEntityId: replica.id,
    reason: "dependency_not_revealed", position: clone(position) });
}

function evaluateFinalePuzzle(run, turnNo, events) {
  if (!run.finalePuzzle) return;
  if (run.finalePuzzle.status === "resolved" && run.finalePuzzle.matchedEndingId) {
    run.selectedEndingId = run.finalePuzzle.matchedEndingId;
    return;
  }
  const componentId = (keyName) => run.finalePuzzle.componentEntityIds[keyName];
  const active = (keyName) => Boolean(entityById(run, componentId(keyName)));
  const linked = (left, right) => {
    const leftId = left === "player" ? run.playerEntityId : componentId(left);
    const rightId = right === "player" ? run.playerEntityId : componentId(right);
    if (!leftId || !rightId) return false;
    return run.connections.some((item) => item.active && ((item.fromId === leftId && item.toId === rightId) || (item.fromId === rightId && item.toId === leftId)));
  };
  const parseLink = (value) => {
    if (Array.isArray(value) && value.length === 2) return value;
    if (value && typeof value === "object") return [value.from || value.left, value.to || value.right];
    if (typeof value === "string") return value.split(/[:>|-]/).map((item) => item.trim()).filter(Boolean).slice(0, 2);
    return [];
  };
  const metricMatches = (conditions = {}) => Object.entries(conditions).every(([metric, range]) => {
    const value = run.metrics[metric];
    if (!Number.isFinite(value)) return false;
    if (Number.isFinite(range)) return value >= range;
    if (!range || typeof range !== "object") return false;
    return (!Number.isFinite(range.min) || value >= range.min) && (!Number.isFinite(range.max) || value <= range.max);
  });
  const assessment = {};
  let matched = null;
  for (const ending of run.endingCandidates || []) {
    const recipe = ending.recipe || ending;
    const requiredLinks = (recipe.requiredLinks || []).map(parseLink);
    const forbiddenLinks = (recipe.forbiddenLinks || []).map(parseLink);
    const validLinks = requiredLinks.every(([left, right]) => left && right && linked(left, right));
    const validForbiddenLinks = forbiddenLinks.every(([left, right]) => left && right && !linked(left, right));
    const validRemoved = (recipe.requiredRemoved || []).every((keyName) => !active(keyName));
    const validActive = (recipe.requiredActive || []).every((keyName) => active(keyName));
    const validMetrics = metricMatches(recipe.metricConditions || {});
    const recipeHasIntent = requiredLinks.length > 0 || forbiddenLinks.length > 0 || (recipe.requiredRemoved || []).length > 0 || (recipe.requiredActive || []).length > 0;
    assessment[ending.id] = { validLinks, validForbiddenLinks, validRemoved, validActive, validMetrics, recipeHasIntent };
    if (!matched && recipeHasIntent && validLinks && validForbiddenLinks && validRemoved && validActive && validMetrics) matched = ending;
  }
  const evidence = run.connections.filter((item) => item.active).map((item) => {
    const label = (entityId) => entityId === run.playerEntityId ? "player" : Object.entries(run.finalePuzzle.componentEntityIds).find(([, id]) => id === entityId)?.[0] || entityId;
    return `link:${label(item.fromId)}:${label(item.toId)}`;
  });
  for (const keyName of Object.keys(run.finalePuzzle.componentEntityIds)) if (!active(keyName)) evidence.push(`removed:${keyName}`);
  evidence.push(`metrics:${Object.entries(run.metrics).map(([keyName, value]) => `${keyName}=${value}`).join(",")}`);
  run.finalePuzzle.evidence = evidence;
  run.finalePuzzle.recipeAssessment = assessment;
  run.finalePuzzle.metricsAtEvaluation = clone(run.metrics);
  run.finalePuzzle.status = matched ? "resolved" : finaleGateEligible(run) ? "available" : "gated";
  run.finalePuzzle.matchedEndingId = matched?.id || null;
  run.selectedEndingId = matched?.id || null;
  if (matched) events.push({ type: "finale_puzzle_matched", endingId: matched.id, turnNo, evidence: clone(evidence) });
}

function applyDirectorOperations(run, narrative, turnNo, budget, events) {
  const appliedOps = [];
  const rejectedOps = [];
  let spent = 0;
  const allowedFields = new Set(["op", "summary", "targetId", "slotId", "assetId", "key", "value", "delta", "ttlTurns", "importance", "questTemplateId", "budgetCost"]);
  for (const operation of narrative?.proposedOps || []) {
    const reject = (reason) => rejectedOps.push({ op: operation?.op || "UNKNOWN", reason });
    if (!operation || typeof operation !== "object" || Object.keys(operation).some((field) => !allowedFields.has(field)) || typeof operation.summary !== "string" || operation.summary.trim().length === 0) { reject("OP_SCHEMA_INVALID"); continue; }
    const cost = operation.budgetCost ?? 0;
    if (!Number.isInteger(cost) || cost < 0 || cost > 4) { reject("CONSEQUENCE_COST_INVALID"); continue; }
    if (!DIRECTOR_OPS.includes(operation.op)) { reject("OP_NOT_ALLOWLISTED"); continue; }
    if (cost < 0 || spent + cost > budget) { reject("CONSEQUENCE_BUDGET_EXCEEDED"); continue; }
    if (operation.op === "SET_WORLD_FACT") {
      if (!/^run\.[a-z0-9_.-]{2,80}$/.test(operation.key || "")) { reject("FACT_KEY_INVALID"); continue; }
      if (containsProtectedFactReference(`${operation.key} ${operation.value || ""} ${operation.summary || ""}`)) { reject("FACT_NAMESPACE_PROTECTED"); continue; }
      if (run.canonicalFacts.some((fact) => `${fact.subject}.${fact.predicate}` === operation.key && fact.value !== operation.value)) { reject("FACT_CONFLICT"); continue; }
      const fact = { id: deterministicUuid(`${run.id}:fact:${turnNo}:${operation.key}`), subject: operation.key.split(".").slice(0, -1).join("."), predicate: operation.key.split(".").at(-1), value: operation.value || operation.summary, type: "run_confirmed", establishedTurn: turnNo };
      run.canonicalFacts.push(fact);
      events.push({ type: "fact_established", factId: fact.id, key: operation.key, value: fact.value });
    } else if (operation.op === "ADD_RUMOR") {
      const rumor = { id: deterministicUuid(`${run.id}:rumor:${turnNo}:${appliedOps.length}`), summary: operation.summary, reliability: 0.5, status: "active", firstHeardTurn: turnNo, expiresTurn: Math.min(run.turnLimit, turnNo + Math.max(1, operation.ttlTurns || 6)) };
      run.rumors.push(rumor);
      events.push({ type: "rumor_added", rumorId: rumor.id, summary: rumor.summary });
    } else if (operation.op === "ADD_NPC_MEMORY") {
      const npc = entityById(run, operation.targetId);
      if (!npc || npc.kind !== "npc") { reject("NPC_TARGET_INVALID"); continue; }
      const memory = { id: deterministicUuid(`${run.id}:memory:${turnNo}:${npc.id}:${appliedOps.length}`), npcId: npc.id, summary: operation.summary, importance: Math.max(0.4, Math.min(1, operation.importance || 0.5)), ttlTurns: Math.max(1, Math.min(20, operation.ttlTurns || 8)), createdTurn: turnNo };
      run.npcMemories.push(memory);
      events.push({ type: "npc_memory_added", memoryId: memory.id, npcId: npc.id, summary: memory.summary });
    } else if (operation.op === "CHANGE_AFFINITY") {
      const relationship = run.npcRelationships.find((item) => item.npcId === operation.targetId);
      if (!relationship || !Number.isInteger(operation.delta) || Math.abs(operation.delta) > 5) { reject("AFFINITY_CHANGE_INVALID"); continue; }
      if (operation.delta < 0 && cost < 1) { reject("CONSEQUENCE_COST_REQUIRED"); continue; }
      relationship.affinity = Math.max(-100, Math.min(100, relationship.affinity + operation.delta));
      relationship.stance = relationship.affinity >= 30 ? "allied" : relationship.affinity <= -30 ? "hostile" : "neutral";
      relationship.lastChangedTurn = turnNo;
      events.push({ type: "relationship_changed", npcId: relationship.npcId, delta: operation.delta, affinity: relationship.affinity });
    } else if (operation.op === "CREATE_HOOK") {
      if (run.turnLimit - turnNo <= 3) { reject("ENDING_WINDOW_LOCKED"); continue; }
      const loop = { id: deterministicUuid(`${run.id}:hook:${turnNo}:${appliedOps.length}`), summary: operation.summary, status: "open", createdTurn: turnNo, expiresTurn: Math.min(run.turnLimit, turnNo + Math.max(1, Math.min(operation.ttlTurns || 5, run.turnLimit - turnNo))), source: "validated_director" };
      run.openLoops.push(loop);
      run.unresolvedHooks ||= [];
      run.unresolvedHooks.push(clone(loop));
      events.push({ type: "open_loop_created", loopId: loop.id, summary: loop.summary });
    } else if (operation.op === "START_QUEST") {
      if (run.turnLimit - turnNo <= 5 || run.activeQuests.filter((quest) => quest.status === "active").length >= 3) { reject("QUEST_HORIZON_LOCKED"); continue; }
      const template = (run.questTemplates || []).find((item) => item.id === operation.questTemplateId);
      if (!template || run.activeQuests.some((quest) => quest.templateId === template.id || quest.key === `TEMPLATE.${template.id}`)) { reject("QUEST_TEMPLATE_INVALID"); continue; }
      const quest = { id: deterministicUuid(`${run.id}:quest:${turnNo}:${template.id}`), key: `TEMPLATE.${template.id}`, templateId: template.id, title: template.title, summary: operation.summary || template.description, status: "active", questKind: "emergent", currentStep: template.initialStep || "discover", acceptsNewSteps: true, createdTurn: turnNo };
      run.activeQuests.push(quest);
      events.push({ type: "quest_started", questId: quest.id, title: quest.title });
    } else if (operation.op === "ADVANCE_QUEST") {
      const quest = run.activeQuests.find((item) => item.id === operation.targetId && item.status === "active");
      if (!quest) { reject("QUEST_TARGET_INVALID"); continue; }
      quest.currentStep = operation.value || "advanced";
      events.push({ type: "quest_updated", questId: quest.id, currentStep: quest.currentStep });
    } else if (operation.op === "SET_VISUAL_INTENT") {
      const slot = run.world.placementSlots.find((item) => item.id === operation.slotId);
      if (!slot || typeof operation.value !== "string" || operation.value.trim().length < 1 || operation.value.length > 160) { reject("VISUAL_INTENT_INVALID"); continue; }
      const record = { slotId: slot.id, turnNo, summary: operation.summary.slice(0, 160), visualIntent: operation.value.trim(), geometryChanged: false };
      run.slotEnrichments = run.slotEnrichments.filter((item) => item.slotId !== slot.id);
      run.slotEnrichments.push(record);
      events.push({ type: "visual_intent_recorded", slotId: slot.id, visualIntent: record.visualIntent, geometryChanged: false });
    }
    spent += cost;
    appliedOps.push(clone(operation));
  }
  return { appliedOps, rejectedOps, budgetSpent: spent };
}

function expireNarrativeState(run, turnNo, events, forceConvergence = false) {
  for (const entityItem of run.entities) {
    if (entityItem.active && entityItem.state?.temporary === true && Number.isInteger(entityItem.state?.expiresTurn) && entityItem.state.expiresTurn <= turnNo) {
      entityItem.active = false;
      events.push({ type: "entity_removed", entityId: entityItem.id, reason: "temporary_duration_expired" });
    }
  }
  for (const connection of run.connections) {
    if (connection.active && Number.isInteger(connection.expiresTurn) && connection.expiresTurn <= turnNo) {
      connection.active = false;
      events.push({ type: "connection_expired", connectionId: connection.id, expiresTurn: connection.expiresTurn });
    }
  }
  for (const memory of run.npcMemories) {
    if (!memory.expired && memory.ttlTurns && memory.createdTurn + memory.ttlTurns <= turnNo) {
      memory.expired = true;
      events.push({ type: "npc_memory_expired", memoryId: memory.id });
    }
  }
  for (const rumor of run.rumors) {
    if (rumor.status === "active" && Number.isInteger(rumor.expiresTurn) && rumor.expiresTurn <= turnNo) {
      rumor.status = "expired";
      rumor.closedTurn = turnNo;
      events.push({ type: "rumor_closed", rumorId: rumor.id, reason: "ttl_expired" });
    }
  }
  for (const loop of run.openLoops) {
    if (loop.status === "open" && Number.isInteger(loop.expiresTurn) && loop.expiresTurn <= turnNo) {
      loop.status = "expired";
      loop.closedTurn = turnNo;
      const hook = (run.unresolvedHooks || []).find((item) => item.id === loop.id);
      if (hook) Object.assign(hook, { status: "expired", closedTurn: turnNo });
      events.push({ type: "open_loop_closed", loopId: loop.id, reason: "ttl_expired" });
    }
  }
  if (!forceConvergence) return;
  for (const rumor of run.rumors) {
    if (rumor.status !== "active") continue;
    rumor.status = "expired";
    rumor.closedTurn = turnNo;
    events.push({ type: "rumor_closed", rumorId: rumor.id, reason: "ending_convergence" });
  }
  for (const loop of run.openLoops) {
    if (loop.status !== "open") continue;
    loop.status = "closed";
    loop.closedTurn = turnNo;
    const hook = (run.unresolvedHooks || []).find((item) => item.id === loop.id);
    if (hook) Object.assign(hook, { status: "closed", closedTurn: turnNo });
    events.push({ type: "open_loop_closed", loopId: loop.id, reason: "ending_convergence" });
  }
  const allRequiredBeatsCompleted = run.requiredStoryBeats.every((beat) => beat.status === "completed");
  for (const quest of run.activeQuests) {
    if (quest.status !== "active") continue;
    quest.status = quest.questKind === "main" ? (allRequiredBeatsCompleted ? "completed" : "failed") : "abandoned";
    quest.acceptsNewSteps = false;
    quest.completedTurn = turnNo;
    events.push({ type: "quest_closed", questId: quest.id, status: quest.status, reason: "ending_convergence" });
  }
}

function updateCampaignMetrics(run, request, outcome, actionContext, turnNo, events, tutorialAction = false) {
  // The mandatory first attack demonstrates controls; it is not a moral or
  // strategic choice and must not bias any later ending metric.
  if (tutorialAction) return;
  const before = clone(run.metrics);
  const success = SUCCESS_OUTCOMES.has(outcome);
  const critical = outcome === "critical_success" || outcome === "critical_failure";
  const shift = success ? (critical ? 3 : 2) : (critical ? -4 : -2);
  run.metrics.worldStability += shift;
  run.metrics.publicTrust += success ? 1 : -2;
  // Failed checks do not silently create or erase debt. Only an applied editing
  // operation (or an explicit repair of a ledger entry) changes technical debt.
  if (request.ability === "copy") run.metrics.worldAutonomy += 2;
  if (request.ability === "delete") {
    run.metrics.worldStability += 2;
    run.metrics.worldAutonomy -= 1;
  }
  if (request.ability === "connect") { run.metrics.worldAutonomy += 2; run.metrics.publicTrust += 2; run.metrics.companionBond += 1; }
  if (["restore", "undo"].includes(request.ability)) run.metrics.worldStability += 2;
  if (actionContext === "NEGOTIATION") { run.metrics.publicTrust += success ? 3 : -1; run.metrics.companionBond += success ? 2 : 0; }
  if (actionContext === "COMBAT") run.metrics.publicTrust -= 1;
  const debtDelta = technicalDebtDelta({
    skillId: request.skillId,
    successful: success,
    forcedOverride: request.forcedOverride,
    resolvesDebtEntryId: request.resolvesDebtEntryId
  });
  run.metrics.technicalDebt += debtDelta;
  if (success && request.resolvesDebtEntryId) {
    const resolved = run.technicalDebtEntries.find((item) => item.id === request.resolvesDebtEntryId && item.resolvedAt === null);
    if (resolved) {
      resolved.resolvedAt = turnNo;
      resolved.resolutionSkillId = request.skillId;
      events.push({ type: "technical_debt_resolved", entryId: resolved.id, debtDelta });
    }
  }
  if (debtDelta > 0) {
    const entry = {
      id: deterministicUuid(`${run.id}:technical-debt:${turnNo}:${request.skillId}:v${request.expectedRunVersion}`),
      runId: run.id,
      turnId: deterministicUuid(`${run.id}:turn:${turnNo}:v${request.expectedRunVersion}`),
      turnNo,
      skillId: request.skillId,
      operationType: request.skillId,
      actionContext,
      targetId: request.targetEntityId || request.resolvesDebtEntryId || "LAST_REVERSIBLE_ACTION",
      forcedOverride: request.forcedOverride === true,
      debtDelta,
      deferredConsequenceType: { COPY: "DUPLICATED_STATE", DELETE: "REMOVED_DEPENDENCY", CONNECT: "COUPLING_RISK", UNDO: "COMPENSATION_DRIFT" }[request.skillId] || "EDIT_SIDE_EFFECT",
      resolvedAt: null
    };
    run.technicalDebtEntries.push(entry);
    events.push({ type: "technical_debt_recorded", entryId: entry.id, debtDelta, deferredConsequenceType: entry.deferredConsequenceType });
  }
  run.metrics.turnPressure = Math.max(run.metrics.turnPressure, Math.round((turnNo / run.turnLimit) * 70 + Math.min(30, run.pressure * 2)));
  for (const keyName of Object.keys(run.metrics)) run.metrics[keyName] = Math.max(0, Math.min(100, Math.round(run.metrics[keyName])));
  const changes = Object.entries(run.metrics).filter(([keyName, value]) => value !== before[keyName]).map(([keyName, value]) => ({ metric: keyName, from: before[keyName], to: value }));
  if (changes.length > 0) events.push({ type: "campaign_metrics_changed", changes });
}

function triggerTechnicalDebtConsequences(run, turnNo, events) {
  run.debtThresholdsTriggered ||= [];
  for (const threshold of [25, 50, 75]) {
    if (run.metrics.technicalDebt < threshold || run.debtThresholdsTriggered.includes(threshold)) continue;
    run.debtThresholdsTriggered.push(threshold);
    if (threshold === 25) {
      const unstableClone = run.entities
        .filter((item) => item.active && item.state?.sourceEntityId)
        .sort((left, right) => left.id.localeCompare(right.id))[0];
      if (unstableClone) {
        unstableClone.state.unstable = true;
        unstableClone.state.expiresTurn = Math.min(run.turnLimit, turnNo + 2);
        events.push({ type: "debt_backflow_clone_drift", threshold, entityId: unstableClone.id, expiresTurn: unstableClone.state.expiresTurn });
      } else {
        run.metrics.worldStability = Math.max(0, run.metrics.worldStability - 2);
        events.push({ type: "debt_backflow_clone_drift", threshold, worldStabilityDelta: -2, noClone: true });
      }
    } else if (threshold === 50) {
      const player = entityById(run, run.playerEntityId);
      const position = [[1, 0], [0, 1], [-1, 0], [0, -1]]
        .map(([dx, dy]) => ({ x: player.position.x + dx, y: player.position.y + dy }))
        .find((point) => isWalkable(run.world, point) && !isBlockingOccupied(run, point));
      if (position) {
        const hostile = entity(deterministicUuid(`${run.id}:debt-backflow:${turnNo}`), "enemy",
          "enemy.dependency-backflow", "Dependency Backflow", position, true, false, false,
          { hp: 3, maxHp: 3, spawnedByDebtThreshold: threshold });
        run.entities.push(hostile);
        events.push({ type: "debt_backflow_hostile_spawned", threshold, entityId: hostile.id, position: clone(position) });
      } else {
        run.metrics.worldStability = Math.max(0, run.metrics.worldStability - 5);
        events.push({ type: "debt_backflow_blocked", threshold, worldStabilityDelta: -5 });
      }
    } else {
      const player = entityById(run, run.playerEntityId);
      const priorHp = Number.isInteger(player.state.hp) ? player.state.hp : 1;
      const damage = Math.min(2, Math.max(0, priorHp - 1));
      player.state.hp = priorHp - damage;
      run.exposed = true;
      events.push({ type: "debt_paradox_surge", threshold, entityId: player.id, damage, hp: player.state.hp, exposed: true });
    }
  }
}

function companionSupportBonus(run) {
  const player = entityById(run, run.playerEntityId);
  if (!player) return 0;
  return (run.npcPromises || []).some((promise) => promise.status !== "broken" &&
    run.entities.some((npc) => npc.id === promise.npcId && npc.active && manhattan(player.position, npc.position) <= 4)) ? 1 : 0;
}

function processNpcPromises(run, turnNo, events) {
  run.npcPromises ||= [];
  const player = entityById(run, run.playerEntityId);
  if (!player || areaAt(run.world, player.position).regionAxis !== ROOT_SYSTEM || !finaleGateEligible(run)) return;
  for (const promise of run.npcPromises) {
    if (promise.status !== "made") continue;
    promise.status = "fulfilled";
    promise.fulfilledTurn = turnNo;
    const relationship = run.npcRelationships.find((item) => item.npcId === promise.npcId);
    if (relationship) {
      relationship.affinity = Math.min(100, relationship.affinity + 5);
      relationship.trust = Math.min(100, relationship.trust + 10);
      relationship.lastChangedTurn = turnNo;
    }
    run.metrics.companionBond = Math.min(100, run.metrics.companionBond + 10);
    run.metrics.publicTrust = Math.min(100, run.metrics.publicTrust + 2);
    events.push({ type: "npc_promise_fulfilled", npcId: promise.npcId, turnNo,
      companionBondDelta: 10, publicTrustDelta: 2 });
  }
}

export function resolveTurn({ run: originalRun, request, d20Source = new DeterministicD20Source(), forcedD20 = null, now = new Date().toISOString(), directorOutput = null, sceneDecision = null, skillSelection = null, arcDecision = null }) {
  assert(originalRun.status === "active", 409, "RUN_NOT_ACTIVE", "The run does not accept turns.");
  let reconciledEncounter = null;
  if (originalRun.activeEncounter?.status === "active") {
    const reconciledRun = clone(originalRun);
    reconciledEncounter = reconcileOrphanedActiveEncounter(reconciledRun, now);
    if (reconciledEncounter) originalRun = reconciledRun;
  }
  assert(request.inputType === "USE_SKILL" && ACTION_SKILLS.includes(request.skillId), 400, "TURN_REQUEST_INVALID", "Only structured USE_SKILL commands consume campaign turns.");
  if (request.resolvesDebtEntryId) {
    assert(["RESTORE", "UNDO"].includes(request.skillId), 422, "TECHNICAL_DEBT_RESOLUTION_INVALID", "Only RESTORE or UNDO can explicitly resolve a technical debt entry.");
    assert(originalRun.technicalDebtEntries.some((item) => item.id === request.resolvesDebtEntryId && item.resolvedAt === null), 422, "TECHNICAL_DEBT_ENTRY_INVALID", "The selected technical debt entry is not unresolved.");
  }
  const stateHashBefore = stateFingerprint(originalRun);
  const openedEncounter = originalRun.activeEncounter?.status === "active" ? clone(originalRun.activeEncounter) : null;
  if (openedEncounter) {
    const emergencyRecovery = request.ability === "rest" && Number(originalRun.focus || 0) < 1;
    assert(request.ability !== "rest" || emergencyRecovery, 422, "ENCOUNTER_ACTION_REQUIRED",
      "An active encounter requires a nearby meaningful action; rest is available only when no focus remains.",
      { activeEncounter: openedEncounter });
  }
  const immutableLayout = fingerprint(publicWorld(originalRun.world));
  const preparation = prepare(originalRun, request, skillSelection);
  const tutorialAction = preparation.target?.state?.tutorialEncounter === true &&
    originalRun.currentTurn === 0 &&
    originalRun.activeEncounter?.reason === "opening_keyboard_tutorial" &&
    request.narrativeChoice?.choiceId === "opening.attack";
  // Targetless keyboard actions may be resolved by the authoritative selector.
  // From this point onward the committed request must describe what actually
  // happened, otherwise usage history and Unity presentation events incorrectly
  // report an empty target while mechanics mutate a concrete entity.
  if ((!request.targetEntityId && preparation.target) ||
      (!request.secondaryTargetEntityId && preparation.secondary) ||
      (!request.destination && preparation.destination)) {
    request = {
      ...request,
      targetEntityId: request.targetEntityId || preparation.target?.id || null,
      secondaryTargetEntityId: request.secondaryTargetEntityId || preparation.secondary?.id || null,
      destination: request.destination || (preparation.destination ? clone(preparation.destination) : null)
    };
  }
  if (skillSelection?.generatedEvent && !preparation.generatedEvent) preparation.generatedEvent = clone(skillSelection.generatedEvent);
  const supportBonus = request.ability === "move" ? 0 : companionSupportBonus(originalRun);
  if (supportBonus > 0) preparation.modifier += supportBonus;
  const actionContext = classifyActionContext(originalRun, request, preparation);
  assert(CAMPAIGN_ACTION_CONTEXTS.includes(actionContext), 500, "ACTION_CONTEXT_INVALID", "The server failed to classify a consuming action context.");
  const intentAnalysis = analyzeIntent({ run: originalRun, request, legalExecution: preparation.normalizedAttempt });
  const turnNo = originalRun.currentTurn + 1;
  const d20 = forcedD20 ?? d20Source.roll({ resolutionSeed: originalRun.resolutionSeed, runId: originalRun.id, turnNo });
  if (!Number.isInteger(d20) || d20 < 1 || d20 > 20) throw new AppError(500, "D20_SOURCE_INVALID", "The server D20 source returned an invalid value.");
  const score = d20 + preparation.modifier - preparation.difficulty;
  const outcome = outcomeFor(score, d20);
  const budget = consequenceBudget(d20);
  const run = clone(originalRun);
  run.majorChoices ||= [];
  run.regionOutcomes ||= [];
  run.abilityUsageHistory ||= [];
  run.adminAccessAcquisitionHistory ||= [];
  run.technicalDebtEntries ||= [];
  run.unresolvedHooks ||= clone(run.openLoops || []);
  run.choiceHistory ||= [];
  const events = [];
  if (reconciledEncounter) events.push({
    type: "encounter_state_reconciled",
    encounterId: reconciledEncounter.id,
    resolution: reconciledEncounter.resolution,
    sourceEntityId: reconciledEncounter.sourceEntityId || reconciledEncounter.entityId || null
  });
  skipPendingChoiceForDisengage(run, request, turnNo, events);
  const selectedChoiceRecord = narrativeChoiceRecord(run, request, turnNo, events);
  if (supportBonus > 0) events.push({ type: "companion_support_applied", modifier: supportBonus });

  if (preparation.focusCost > 0) {
    run.focus -= preparation.focusCost;
    events.push({ type: "resource_changed", resource: "focus", delta: -preparation.focusCost, reason: request.ability });
  }
  if (preparation.healthCost > 0) {
    const player = entityById(run, run.playerEntityId);
    player.state.hp -= preparation.healthCost;
    events.push({ type: "health_changed", entityId: player.id, delta: -preparation.healthCost, hp: player.state.hp, reason: "special_skill_cost" });
  }
  if (preparation.specialSkill) {
    const usedSkill = run.directorState.specialSkills.find((skill) => skill.id === preparation.specialSkill.id);
    assert(usedSkill && usedSkill.charges > 0, 409, "SPECIAL_SKILL_CONFLICT", "The selected special skill has no remaining charge.");
    usedSkill.charges -= 1;
    usedSkill.lastUsedTurn = turnNo;
    events.push({ type: "special_skill_used", entityId: run.playerEntityId, rewardId: usedSkill.id, skillId: request.skillId, charges: usedSkill.charges });
  }
  if (preparation.copyLocked) {
    events.push({ type: "copy_repeat_rejected", entityId: preparation.target?.id || null, lineageRootId: preparation.target?.state?.lineageRootId || preparation.target?.id || null, reason: "COPY_LINEAGE_LOCKED" });
  }
  if (SUCCESS_OUTCOMES.has(outcome)) {
    applyPrimaryEffect(run, request, preparation, turnNo, events, outcome);
    applyBoundActionItems(run, request, turnNo, events);
    grantSearchDiscoveryItem(run, request, turnNo, events, outcome);
    confirmEssentialClue(run, request, preparation, turnNo, events);
  }
  if (outcome === "partial_success") {
    applyPartialConsequence(run, request, Math.max(1, budget), events);
  } else if (outcome === "failure" || outcome === "critical_failure") {
    const pressureDelta = budget + (outcome === "critical_failure" ? 1 : 0);
    run.pressure += pressureDelta;
    events.push({ type: "pressure_changed", delta: pressureDelta });
    if (outcome === "critical_failure") {
      const player = entityById(run, run.playerEntityId);
      const priorHp = Number.isInteger(player.state.hp) ? player.state.hp : 1;
      if (priorHp > 1) {
        player.state.hp = priorHp - 1;
        run.reversibleLedger.push({
          turnNo,
          ability: "critical_consequence",
          reversible: true,
          consumed: false,
          inverseOps: [{ type: "restore_state", entityId: player.id, stateSnapshot: { hp: priorHp }, allowedFields: ["hp"] }]
        });
        events.push({ type: "health_changed", entityId: player.id, delta: -1, hp: player.state.hp, reversible: true });
      }
    }
  }
  applyNpcInvestigation(run, request, preparation, outcome, turnNo, events);

  const successfulRewind = events.some((event) => event.type === "undo_compensation_completed");
  run.currentTurn = turnNo;
  run.abilityUsageHistory.push({
    id: deterministicUuid(`${run.id}:ability-usage:${turnNo}:v${request.expectedRunVersion}`),
    turnNo,
    skillId: request.skillId,
    actionContext,
    targetIds: [request.targetEntityId, request.secondaryTargetEntityId].filter(Boolean),
    outcome,
    d20,
    forcedOverride: request.forcedOverride === true
  });
  resolveEncounterLifecycle(run, openedEncounter, request, outcome, turnNo, now, events);
  if (tutorialAction) {
    // Control onboarding must finish the real encounter without advancing debt
    // thresholds, promises, finale recipes, or alignment metrics.
  }
  else if (!successfulRewind) {
    updateCampaignMetrics(run, request, outcome, actionContext, turnNo, events, tutorialAction);
    processNpcPromises(run, turnNo, events);
    triggerTechnicalDebtConsequences(run, turnNo, events);
    evaluateFinalePuzzle(run, turnNo, events);
  }
  else {
    updateCampaignMetrics(run, request, outcome, actionContext, turnNo, events, tutorialAction);
    processNpcPromises(run, turnNo, events);
    triggerTechnicalDebtConsequences(run, turnNo, events);
  }
  const campaignRole = areaAt(run.world, entityById(run, run.playerEntityId).position).campaignRole;
  const currentArea = areaAt(run.world, entityById(run, run.playerEntityId).position);
  const targetEvidenceKeys = [preparation.target, preparation.secondary]
    .filter(Boolean)
    .flatMap((item) => [item.state?.evidenceKey, item.state?.finaleComponent ? "FINALE_PUZZLE_COMPONENT" : null])
    .filter(Boolean);
  if (run.finalePuzzle?.status === "resolved") targetEvidenceKeys.push("FINALE_PUZZLE_RESOLVED");
  if (currentArea.regionAxis === ROOT_SYSTEM) targetEvidenceKeys.push("ROOT_SYSTEM_ENTERED");
  if (!successfulRewind && !tutorialAction) advanceArcDirector(run, turnNo, events, {
      ability: request.ability, outcome, contextualActions: [actionContext.toLowerCase()], campaignRole,
      targetEvidenceKeys,
      eventTypes: events.map((item) => item.type)
    }, arcDecision);
  if (selectedChoiceRecord) {
    selectedChoiceRecord.outcome = outcome;
    selectedChoiceRecord.d20 = d20;
    const major = run.majorChoices.find((choice) => choice.id === selectedChoiceRecord.id);
    if (major) Object.assign(major, { outcome, d20 });
    const choiceLedger = (run.storyLedger || []).find((item) => item.turnNo === turnNo);
    if (choiceLedger) Object.assign(choiceLedger, {
      choiceRecordId: selectedChoiceRecord.id,
      choiceSetId: selectedChoiceRecord.choiceSetId,
      choiceId: selectedChoiceRecord.choiceId,
      choiceText: selectedChoiceRecord.text,
      choiceKind: selectedChoiceRecord.choiceKind,
      intentTag: selectedChoiceRecord.intentTag
    });
  }
  const directorPlan = successfulRewind || tutorialAction
    ? { appliedOps: [], rejectedOps: [], notices: [] }
    : applyDirectorOperations(run, directorOutput || { proposedOps: [] }, turnNo, budget, events);
  const sceneResolution = !successfulRewind && !tutorialAction && sceneDecision ? applyScenePlan(run, {
    candidates: sceneDecision.candidates,
    plan: sceneDecision.plan,
    decisionType: "ACTION",
    now
  }) : null;
  if (sceneResolution) events.push(...sceneResolution.events);
  const currentLedgerEntry = (run.storyLedger || []).find((item) => item.turnNo === turnNo);
  if (currentLedgerEntry) {
    currentLedgerEntry.eventTypes = [...new Set(events.map((item) => item.type).filter(Boolean))].slice(0, 24);
    currentLedgerEntry.narrativeFragments = events.flatMap((item) => [item.title, item.description, item.text, item.summary])
      .filter((item) => typeof item === "string" && item.trim().length > 0)
      .map((item) => item.trim().slice(0, 240)).slice(0, 8);
  }
  const ending = !successfulRewind && !tutorialAction ? storyDrivenEnding(run, request) : null;
  if (!successfulRewind && !tutorialAction) expireNarrativeState(run, turnNo, events, Boolean(ending));
  run.version += 1;
  run.updatedAt = now;
  events.push(successfulRewind
    ? { type: "undo_compensated", sourceTurns: 2, turnNo, runVersion: run.version }
    : { type: "turn_committed", turnNo, runVersion: run.version });
  if (!successfulRewind && ending) {
    const playerAtEnding = entityById(run, run.playerEntityId);
    const endingArea = areaAt(run.world, playerAtEnding.position);
    run.finalPlacement = {
      areaId: endingArea.id,
      regionAxis: endingArea.regionAxis,
      position: clone(playerAtEnding.position),
      selectedEndingId: ending.id,
      turnNo
    };
    run.status = "completed";
    run.endingCode = ending.id;
    run.currentAct = "finale_resolution";
    run.campaignPhase = "finale_resolution";
    run.finaleResolution = resolveFinale(run, ending, turnNo);
    run.currentStoryBeat = { ...run.currentStoryBeat, act: "finale_resolution" };
    events.push({ type: "finale_resolved", ...clone(run.finaleResolution) });
    events.push({ type: "run_completed", endingCode: ending.id, endingCategory: ending.category, title: ending.title, valence: ending.valence, completionMode: "story_driven_closure" });
  }
  assert(fingerprint(publicWorld(run.world)) === immutableLayout, 500, "WORLD_LAYOUT_MUTATED", "A turn attempted to mutate immutable world geometry.");

  const explanation = outcomeExplanation({ request, d20, score, outcome, preparation, intentAnalysis });
  let narrative = normalizeCommittedNarrative(directorOutput, directorPlan, explanation, sceneResolution);
  if (tutorialAction) {
    narrative = {
      ...narrative,
      summary: "관리자 키보드 첫 공격 성공",
      body: "R 키의 삭제 명령이 캐시 누수 슬라임에 적중했다. 막혀 있던 길이 열렸으니 이제 WASD로 직접 이동해 보자.",
      dialogue: [],
      storySequence: [
        { type: "NARRATION", speakerId: null, actionId: null,
          text: "삭제 명령이 캐시 누수 슬라임에 적중해 데이터 파편으로 흩어졌다." },
        { type: "MONOLOGUE", speakerId: null, actionId: null,
          text: "길이 열렸다. 이제 WASD를 계속 눌러 코드리아를 직접 탐색하자." }
      ],
      nextIntervention: null,
      continuesWithMovement: true,
      fallbackUsed: false,
      model: "deterministic-opening-tutorial"
    };
  } else if (request.ability === "move" && SUCCESS_OUTCOMES.has(outcome)) {
    narrative = {
      ...narrative,
      nextIntervention: null,
      continuesWithMovement: true
    };
  }
  persistCommittedNarrativeDigest(run, turnNo, narrative, directorOutput, {
    skillId: request.skillId,
    outcome,
    campaignRole,
    eventTypes: events.map((event) => event.type)
  });
  run.pendingChoiceSet = run.status === "active" ? clone(narrative.nextIntervention) : null;
  const turn = {
    id: deterministicUuid(`${run.id}:turn:${turnNo}:v${request.expectedRunVersion}`),
    runId: run.id,
    ownerId: run.ownerId,
    turnNo,
    idempotencyKey: request.idempotencyKey,
    requestFingerprint: turnFingerprint(request),
    expectedRunVersion: request.expectedRunVersion,
    committedRunVersion: run.version,
    request: clone(request),
    resolutionMode: request.resolutionMode || "D20",
    selectedChoice: selectedChoiceRecord ? clone(selectedChoiceRecord) : null,
    actionContext,
    normalizedAttempt: intentAnalysis.normalizedAttempt,
    intentAnalysis: clone(intentAnalysis),
    d20,
    dice: {
      raw: d20,
      modifier: preparation.modifier,
      modifiers: [{ source: "keyboard_affinity", value: preparation.modifier }],
      difficulty: preparation.difficulty,
      mechanicalScore: score,
      intentAlignment: intentAnalysis.score,
      realizationAlignment: realizationAlignment(intentAnalysis.score, score),
      intentStatus: intentAnalysis.status,
      intentIssues: clone(intentAnalysis.issues),
      outcomeExplanation: explanation,
      rngSource: forcedD20 === null ? "server_deterministic_sha256" : "preflight_forced_commit",
      rngAudit: { algorithm: "sha256_modulo_d20.v2", runId: run.id, turnNo, replayProtected: true, secretRedacted: true }
    },
    mechanicalScore: score,
    outcome,
    consequenceBudget: budget,
    rulesetVersion: "codria-rules.v4",
    stateHashBefore,
    stateHashAfter: stateFingerprint(run),
    events,
    stateDelta: {
      events: clone(events),
      facts: run.canonicalFacts.filter((fact) => fact.establishedTurn === turnNo),
      rumors: run.rumors.filter((rumor) => rumor.firstHeardTurn === turnNo),
      openLoops: run.openLoops.filter((loop) => loop.createdTurn === turnNo),
      npcMemories: run.npcMemories.filter((memory) => memory.createdTurn === turnNo),
      relationships: run.npcRelationships.filter((item) => item.lastChangedTurn === turnNo),
      quests: run.activeQuests.filter((quest) => quest.createdTurn === turnNo),
      majorChoices: run.majorChoices.filter((choice) => choice.turnNo === turnNo),
      regionOutcomes: run.regionOutcomes.filter((item) => item.lastChangedTurn === turnNo),
      abilityUsageHistory: run.abilityUsageHistory.filter((item) => item.turnNo === turnNo),
      adminAccessHistory: run.adminAccessAcquisitionHistory.filter((item) => item.turnNo === turnNo),
      technicalDebtEntries: run.technicalDebtEntries.filter((item) => item.turnNo === turnNo || item.resolvedAt === turnNo),
      narrativeChoices: selectedChoiceRecord ? [clone(selectedChoiceRecord)] : [],
      sceneDecision: sceneResolution ? clone(sceneResolution) : null,
      appliedOps: directorPlan.appliedOps,
      rejectedOps: directorPlan.rejectedOps
    },
    sceneDecision: sceneResolution ? clone(sceneResolution) : null,
    sceneSequence: sceneResolution ? clone(sceneResolution.sceneSequence) : [],
    runtimeEntityRefs: runtimeEntityRefs(run, request, events),
    spatialSnapshot: runtimeSpatialSnapshot(run, events),
    narrative,
    createdAt: now
  };
  return { run, turn };
}

export function resolveNarrativeChoice({ run: originalRun, request, now = new Date().toISOString(), directorOutput = null, arcDecision = null, sceneDecision = null }) {
  assert(originalRun.status === "active", 409, "RUN_NOT_ACTIVE", "The run does not accept turns.");
  let reconciledEncounter = null;
  if (originalRun.activeEncounter?.status === "active") {
    const reconciledRun = clone(originalRun);
    reconciledEncounter = reconcileOrphanedActiveEncounter(reconciledRun, now);
    if (reconciledEncounter) originalRun = reconciledRun;
  }
  assert(request?.inputType === "NARRATIVE_CHOICE" && request.resolutionMode === "NONE", 400, "CHOICE_REQUEST_INVALID", "Pure narrative resolution requires a sealed NONE choice.");
  assert(["DIALOGUE", "ATTITUDE"].includes(request.narrativeChoice?.choiceKind), 400, "CHOICE_REQUEST_INVALID", "Only dialogue and attitude choices use the non-D20 resolver.");
  const stateHashBefore = stateFingerprint(originalRun);
  const immutableLayout = fingerprint(publicWorld(originalRun.world));
  const run = clone(originalRun);
  const turnNo = run.currentTurn + 1;
  run.choiceHistory ||= [];
  run.majorChoices ||= [];
  run.npcMemories ||= [];
  run.unresolvedHooks ||= clone(run.openLoops || []);
  const events = [];
  if (reconciledEncounter) events.push({
    type: "encounter_state_reconciled",
    encounterId: reconciledEncounter.id,
    resolution: reconciledEncounter.resolution,
    sourceEntityId: reconciledEncounter.sourceEntityId || reconciledEncounter.entityId || null
  });
  const selectedChoiceRecord = narrativeChoiceRecord(run, request, turnNo, events);
  const activatedDormant = request.rejectedAction ? null : tryForceMonsterEncounter(run, request.intent, turnNo, request.idempotencyKey);
  if (activatedDormant) {
    selectedChoiceRecord.targetEntityId = activatedDormant.id;
    events.push({ type: "entity_activated", entityId: activatedDormant.id, assetId: activatedDormant.assetId, position: clone(activatedDormant.position), activationSlotId: activatedDormant.state.slotId, sceneDecisionNo: turnNo });
  }
  if (request.rejectedAction) {
    events.push({
      type: "player_action_rejected",
      actionKind: request.rejectedAction.kind,
      code: request.rejectedAction.code,
      reason: request.rejectedAction.reason,
      itemNames: clone(request.rejectedAction.itemNames || []),
      stateChanged: false
    });
  } else {
    const effectChoice = activatedDormant
      ? { ...request.narrativeChoice, targetEntityId: activatedDormant.id }
      : request.narrativeChoice;
    applyPureNarrativeChoiceEffects(run, effectChoice, selectedChoiceRecord, turnNo, events);
    applyNarrativeEncounterLifecycle(run, request, turnNo, events);
  }
  selectedChoiceRecord.outcome = "narrative";
  selectedChoiceRecord.d20 = null;
  const majorChoice = run.majorChoices.find((choice) => choice.id === selectedChoiceRecord.id);
  if (majorChoice) Object.assign(majorChoice, clone(selectedChoiceRecord));
  run.currentTurn = turnNo;

  const player = entityById(run, run.playerEntityId);
  const currentArea = areaAt(run.world, player.position);
  advanceArcDirector(run, turnNo, events, {
    ability: "narrative",
    outcome: "narrative",
    contextualActions: [request.narrativeChoice.choiceKind.toLowerCase()],
    campaignRole: currentArea.campaignRole,
    targetEvidenceKeys: [],
    eventTypes: events.map((event) => event.type)
  }, arcDecision);
  const choiceLedger = (run.storyLedger || []).find((item) => item.turnNo === turnNo);
  if (choiceLedger) Object.assign(choiceLedger, {
    choiceRecordId: selectedChoiceRecord.id,
    choiceSetId: selectedChoiceRecord.choiceSetId,
    choiceId: selectedChoiceRecord.choiceId,
    choiceText: selectedChoiceRecord.text,
    choiceKind: selectedChoiceRecord.choiceKind,
    intentTag: selectedChoiceRecord.intentTag,
    targetEntityId: selectedChoiceRecord.targetEntityId,
    eventTypes: [...new Set(events.map((event) => event.type))],
    narrativeFragments: [selectedChoiceRecord.text]
  });
  const directorPlan = applyDirectorOperations(run, directorOutput || { proposedOps: [] }, turnNo, 0, events);
  const sceneResolution = sceneDecision ? applyScenePlan(run, {
    candidates: sceneDecision.candidates,
    plan: sceneDecision.plan,
    decisionType: "ACTION",
    now
  }) : null;
  if (sceneResolution) events.push(...sceneResolution.events);
  if (choiceLedger) {
    choiceLedger.eventTypes = [...new Set(events.map((event) => event.type).filter(Boolean))].slice(0, 24);
    choiceLedger.narrativeFragments = events.flatMap((event) => [event.title, event.description, event.text, event.summary])
      .filter((text) => typeof text === "string" && text.trim().length > 0)
      .map((text) => text.trim().slice(0, 240)).slice(0, 8);
  }
  const ending = storyDrivenEnding(run, request);
  expireNarrativeState(run, turnNo, events, Boolean(ending));
  run.version += 1;
  run.updatedAt = now;
  events.push({ type: "narrative_choice_resolved", choiceRecordId: selectedChoiceRecord.id, outcome: "narrative", d20Used: false });
  events.push({ type: "turn_committed", turnNo, runVersion: run.version });
  if (ending) {
    const endingArea = areaAt(run.world, player.position);
    run.finalPlacement = {
      areaId: endingArea.id,
      regionAxis: endingArea.regionAxis,
      position: clone(player.position),
      selectedEndingId: ending.id,
      turnNo
    };
    run.status = "completed";
    run.endingCode = ending.id;
    run.currentAct = "finale_resolution";
    run.campaignPhase = "finale_resolution";
    run.finaleResolution = resolveFinale(run, ending, turnNo);
    run.currentStoryBeat = { ...run.currentStoryBeat, act: "finale_resolution" };
    events.push({ type: "finale_resolved", ...clone(run.finaleResolution) });
    events.push({ type: "run_completed", endingCode: ending.id, endingCategory: ending.category, title: ending.title, valence: ending.valence, completionMode: "story_driven_closure" });
  }
  assert(fingerprint(publicWorld(run.world)) === immutableLayout, 500, "WORLD_LAYOUT_MUTATED", "A narrative choice attempted to mutate immutable world geometry.");
  const explanation = request.rejectedAction
    ? `The attempted ${request.rejectedAction.kind} action was rejected without changing authoritative state: ${request.rejectedAction.reason}`
    : `The sealed ${request.narrativeChoice.choiceKind.toLowerCase()} choice was recorded without a D20 roll.`;
  const narrative = normalizeCommittedNarrative(directorOutput, directorPlan, explanation, null);
  persistCommittedNarrativeDigest(run, turnNo, narrative, directorOutput, {
    skillId: "NARRATIVE",
    outcome: "narrative",
    campaignRole: currentArea.campaignRole,
    eventTypes: events.map((event) => event.type)
  });
  run.pendingChoiceSet = run.status === "active" ? clone(narrative.nextIntervention) : null;
  const intentAnalysis = { score: 1, status: "aligned", issues: [], actions: [request.narrativeChoice.intentTag.toLowerCase()] };
  const turn = {
    id: deterministicUuid(`${run.id}:turn:${turnNo}:v${request.expectedRunVersion}`),
    runId: run.id,
    ownerId: run.ownerId,
    turnNo,
    idempotencyKey: request.idempotencyKey,
    requestFingerprint: turnFingerprint(request),
    expectedRunVersion: request.expectedRunVersion,
    committedRunVersion: run.version,
    request: clone(request),
    resolutionMode: "NONE",
    selectedChoice: clone(selectedChoiceRecord),
    actionContext: "NARRATIVE",
    normalizedAttempt: request.narrativeChoice.text,
    intentAnalysis,
    d20: null,
    dice: null,
    mechanicalScore: null,
    outcome: "narrative",
    consequenceBudget: 0,
    rulesetVersion: "codria-rules.v4",
    stateHashBefore,
    stateHashAfter: stateFingerprint(run),
    events,
    stateDelta: {
      events: clone(events),
      facts: run.canonicalFacts.filter((fact) => fact.establishedTurn === turnNo),
      rumors: run.rumors.filter((rumor) => rumor.firstHeardTurn === turnNo),
      openLoops: run.openLoops.filter((loop) => loop.createdTurn === turnNo),
      npcMemories: run.npcMemories.filter((memory) => memory.createdTurn === turnNo),
      relationships: run.npcRelationships.filter((item) => item.lastChangedTurn === turnNo),
      quests: run.activeQuests.filter((quest) => quest.createdTurn === turnNo),
      majorChoices: run.majorChoices.filter((choice) => choice.turnNo === turnNo),
      regionOutcomes: run.regionOutcomes.filter((item) => item.lastChangedTurn === turnNo),
      abilityUsageHistory: [],
      adminAccessHistory: [],
      technicalDebtEntries: [],
      narrativeChoices: [clone(selectedChoiceRecord)],
      sceneDecision: sceneResolution ? clone(sceneResolution) : null,
      appliedOps: directorPlan.appliedOps,
      rejectedOps: directorPlan.rejectedOps
    },
    sceneDecision: sceneResolution ? clone(sceneResolution) : null,
    sceneSequence: [
      ...clone(sceneResolution?.sceneSequence || []),
      ...(activatedDormant ? [{
        actionId: `${run.id}:force-spawn`,
        sequence: (sceneResolution?.sceneSequence?.length || 0),
        type: "SPAWN",
        actorId: activatedDormant.id,
        to: { x: activatedDormant.position.x, y: activatedDormant.position.y },
        text: `${activatedDormant.name}이 모습을 드러냈다.`
      }] : [])
    ],
    runtimeEntityRefs: runtimeEntityRefs(run, request, events),
    spatialSnapshot: runtimeSpatialSnapshot(run, events),
    narrative,
    createdAt: now
  };
  return { run, turn };
}

function applyPartialConsequence(run, request, severity, events) {
  const expose = (type, extra = {}) => {
    run.exposed = true;
    run.pressure += 1;
    events.push({ type, severity, status: "exposed", durationTurns: 1, ...extra });
  };
  switch (request.ability) {
    case "copy":
      run.metrics.technicalDebt = Math.min(100, run.metrics.technicalDebt + severity);
      events.push({ type: "partial_copy_drift", severity, unstableClone: true });
      break;
    case "delete":
      run.metrics.technicalDebt = Math.min(100, run.metrics.technicalDebt + severity);
      expose("partial_delete_backflow", { dependencyBackflow: true });
      break;
    case "connect":
      run.metrics.technicalDebt = Math.min(100, run.metrics.technicalDebt + severity);
      expose("partial_connect_hazard", { bidirectionalHazard: true });
      break;
    case "restore":
      run.metrics.technicalDebt = Math.min(100, run.metrics.technicalDebt + 1);
      events.push({ type: "partial_restore_defect", severity: 1, pastDefectRestored: true });
      break;
    case "undo":
      run.metrics.technicalDebt = Math.min(100, run.metrics.technicalDebt + severity);
      events.push({ type: "partial_undo_paradox", severity });
      break;
    case "select_all": {
      const overload = Math.min(1, run.focus);
      run.focus -= overload;
      events.push({ type: "partial_select_all_overload", severity, resource: "focus", delta: -overload });
      break;
    }
    case "search":
      expose("partial_search_noise");
      break;
    default:
      expose("partial_interaction_attention");
      break;
  }
}

function outcomeExplanation({ request, d20, score, outcome, preparation, intentAnalysis }) {
  const divergence = SUCCESS_OUTCOMES.has(outcome) ? "The legal attempt was resolved within server limits." : "The world state was preserved because the check did not meet the legal difficulty.";
  const intentNote = `Semantic intent fit ${intentAnalysis.score} (${intentAnalysis.status}${intentAnalysis.issues.length > 0 ? `: ${intentAnalysis.issues.join(", ")}` : ""}); free text cannot change legality, coordinates, difficulty, or effects.`;
  return `${request.ability} rolled ${d20} + ${preparation.modifier} against difficulty ${preparation.difficulty} (score ${score}): ${outcome}. ${intentNote} ${divergence}`;
}

export function normalizeCommittedNarrative(output, plan, explanation, sceneResolution = null) {
  const fallback = output || {};
  const dialogue = Array.isArray(fallback.dialogue) ? fallback.dialogue : fallback.dialogue ? [{ speakerId: null, line: fallback.dialogue }] : [];
  const authoredSequence = clone(fallback.storySequence || [{ type: "MONOLOGUE", speakerId: null, actionId: null, text: fallback.body || explanation }]);
  const normalizedConfirmedText = new Set((sceneResolution?.sceneSequence || [])
    .map((item) => String(item?.text || "").normalize("NFKC").replace(/\s+/gu, " ").trim())
    .filter(Boolean));
  // Unreferenced authoritative actions are inserted below. If the model copied the
  // exact same action as NARRATION, retaining both makes the player advance through
  // two pages for a single hit. Prefer the actionId-bearing authoritative beat.
  const distinctAuthoredSequence = authoredSequence.filter((item) => item?.type === "WORLD_ACTION" ||
    !normalizedConfirmedText.has(String(item?.text || "").normalize("NFKC").replace(/\s+/gu, " ").trim()));
  const referencedActionIds = new Set(authoredSequence.map((item) => item.actionId).filter(Boolean));
  const missingConfirmedActions = (sceneResolution?.sceneSequence || [])
    .filter((item) => item.type !== "DIALOGUE" && item.actionId && !referencedActionIds.has(item.actionId))
    .map((item) => ({ type: "WORLD_ACTION", speakerId: null, actionId: item.actionId, text: item.text || "주변 세계가 선택의 결과에 반응했다." }));
  return {
    summary: fallback.summary || "The command settles into the world.",
    body: fallback.body || explanation,
    dialogue,
    storySequence: [...missingConfirmedActions, ...distinctAuthoredSequence].slice(0, 8),
    nextIntervention: clone(fallback.nextIntervention || { reason: "장면이 멈췄다. 다음 개입이 필요하다.", suggestedSkillIds: [] }),
    elementalEffectId: fallback.elementalEffectId || null,
    proposedOps: clone(fallback.proposedOps || []),
    appliedOps: plan.appliedOps,
    rejectedOps: plan.rejectedOps,
    fallbackUsed: fallback.fallbackUsed !== false,
    model: fallback.model || "deterministic-fallback-v2"
  };
}

const LEDGER_NARRATIVE_DIGEST_MAX = 900;
const LEDGER_NARRATIVE_FRAGMENT_MAX = 320;
const LEDGER_NARRATIVE_FRAGMENT_COUNT = 8;

function boundedLedgerText(value, maximum) {
  if (typeof value !== "string") return "";
  return value.replace(/\s+/gu, " ").trim().slice(0, maximum);
}

function persistCommittedNarrativeDigest(run, turnNo, narrative, authoredOutput, evidence = {}) {
  const hasAuthoredNarrative = authoredOutput && typeof authoredOutput === "object"
    && ["summary", "body", "storySequence", "dialogue"].some((key) => authoredOutput[key] !== undefined);
  if (!hasAuthoredNarrative) return;
  run.storyLedger ||= [];
  let entry = run.storyLedger.find((item) => item.turnNo === turnNo);
  if (!entry) {
    entry = {
      id: deterministicUuid(`${run.id}:story-ledger:${turnNo}`),
      turnNo,
      skillId: String(evidence.skillId || "").toUpperCase(),
      outcome: evidence.outcome || "unknown",
      campaignRole: evidence.campaignRole || null,
      targetEvidenceKeys: [],
      eventTypes: []
    };
    run.storyLedger.push(entry);
  }
  const summary = boundedLedgerText(narrative?.summary, 160);
  const body = boundedLedgerText(narrative?.body, 700);
  entry.narrativeDigest = boundedLedgerText([summary, body].filter(Boolean).join(" — "), LEDGER_NARRATIVE_DIGEST_MAX);
  const authoredFragments = (Array.isArray(narrative?.storySequence) ? narrative.storySequence : [])
    .map((beat) => boundedLedgerText(beat?.text, LEDGER_NARRATIVE_FRAGMENT_MAX))
    .filter(Boolean);
  const priorFragments = (Array.isArray(entry.narrativeFragments) ? entry.narrativeFragments : [])
    .map((fragment) => boundedLedgerText(fragment, LEDGER_NARRATIVE_FRAGMENT_MAX))
    .filter(Boolean);
  entry.narrativeFragments = [...new Set([...authoredFragments, ...priorFragments])]
    .slice(0, LEDGER_NARRATIVE_FRAGMENT_COUNT);
  entry.eventTypes = [...new Set([...(entry.eventTypes || []), ...(evidence.eventTypes || [])].filter(Boolean))].slice(0, 24);
}

export function nearestArea(run) {
  const player = entityById(run, run.playerEntityId);
  return areaAt(run.world, player.position);
}

export function spatialContext(run) {
  const player = entityById(run, run.playerEntityId);
  const area = areaAt(run.world, player.position);
  const biome = (run.world.biomes || []).find((item) => item.id === area.biomeId) || null;
  const terrain = ["GRASS", "WALL", "HAZARD", "ROAD", "WATER", "RUIN"][tileAt(run.world, player.position)] || "UNKNOWN";
  const nearbyEntities = run.entities.filter((item) => item.active && item.id !== player.id && manhattan(item.position, player.position) <= 8)
    .sort((left, right) => manhattan(left.position, player.position) - manhattan(right.position, player.position) || left.id.localeCompare(right.id))
    .slice(0, 24)
    .map((item) => ({
      id: item.id, kind: item.kind, name: item.name, distance: manhattan(item.position, player.position), direction: directionBetween(player.position, item.position),
      visible: true, interactable: !item.state?.disabled && manhattan(item.position, player.position) <= (item.kind === "enemy" ? 1 : 2)
    }));
  const directionNames = new Map([[[1, 0].join(","), "동쪽"], [[-1, 0].join(","), "서쪽"], [[0, 1].join(","), "남쪽"], [[0, -1].join(","), "북쪽"]]);
  const destinations = DIRECTIONS.map(([dx, dy]) => ({ point: { x: player.position.x + dx, y: player.position.y + dy }, dx, dy }))
    .filter(({ point }) => isWalkable(run.world, point) && !isBlockingOccupied(run, point, player.id))
    .map(({ point, dx, dy }) => ({ ref: `step.${directionBetween(player.position, point).toLowerCase()}`, name: directionNames.get(`${dx},${dy}`), direction: directionBetween(player.position, point), distance: 1 }));
  return {
    authority: "SERVER", areaId: area.id, areaName: area.nameKo || area.name, biomeId: area.biomeId, biomeName: biome?.nameKo || biome?.name || area.biomeId,
    campaignRole: area.campaignRole || null, terrain, facing: player.state?.facing || "SOUTH", nearbyEntities, availableDestinations: destinations,
    activeEncounter: run.activeEncounter?.status === "active" ? {
      id: run.activeEncounter.id,
      mode: run.activeEncounter.mode,
      sourceEntityId: run.activeEncounter.sourceEntityId || run.activeEncounter.entityId || null
    } : null
  };
}

function runtimeSpatialSnapshot(run, events = []) {
  const player = entityById(run, run.playerEntityId);
  const area = areaAt(run.world, player.position);
  const moved = [...events].reverse().find((event) => event.type === "entity_moved" && event.entityId === player.id) || null;
  return {
    authoritative: true,
    areaId: area.id,
    biomeId: area.biomeId,
    player: { entityId: player.id, position: clone(player.position), facing: player.state?.facing || "SOUTH" },
    movement: moved ? { from: clone(moved.from), to: clone(moved.to), path: clone(moved.path || [moved.from, moved.to]), facing: player.state?.facing || "SOUTH", arrived: true } : null,
    visibility: run.entities.filter((item) => item.active && item.id !== player.id && manhattan(item.position, player.position) <= 12).slice(0, 32)
      .map((item) => ({ entityId: item.id, position: clone(item.position), distance: manhattan(item.position, player.position), direction: directionBetween(player.position, item.position), visible: true })),
    activeEncounter: run.activeEncounter?.status === "active" ? clone(run.activeEncounter) : null
  };
}

export function directorContext(run, turn) {
  const currentArea = nearestArea(run);
  // turnLimit is a soft pacing horizon now. Keep enough narrative horizon for
  // the model to open new hooks after it instead of treating zero as shutdown.
  const remainingTurns = Math.max(6, run.turnLimit - turn.turnNo);
  const semanticSpace = spatialContext(run);
  const player = entityById(run, run.playerEntityId);
  const activeEncounterEntityId = run.activeEncounter?.status === "active"
    ? run.activeEncounter.sourceEntityId || run.activeEncounter.entityId || null
    : null;
  const visibleEntities = run.entities
    .filter((item) => item.active && (manhattan(item.position, player.position) <= 8 || item.id === activeEncounterEntityId))
    .sort((left, right) => {
      const leftPriority = left.id === player.id ? 0 : left.id === activeEncounterEntityId ? 1 : 2;
      const rightPriority = right.id === player.id ? 0 : right.id === activeEncounterEntityId ? 1 : 2;
      return leftPriority - rightPriority || manhattan(left.position, player.position) - manhattan(right.position, player.position) || left.id.localeCompare(right.id);
    })
    .slice(0, 32)
    .map((item) => ({
      id: item.id, kind: item.kind, assetId: item.assetId, name: item.name, role: item.state?.npcRole || null,
      distance: manhattan(item.position, player.position), direction: directionBetween(player.position, item.position),
      capabilities: capabilitiesFor(item), activeEncounterTarget: item.id === activeEncounterEntityId
    }));
  return {
    schemaVersion: "2.0",
    requestType: "TURN_NARRATION",
    campaign: { title: GAME_TITLE, worldId: WORLD_CODRIA, worldName: WORLD_NAME_KO, protagonistId: PROTAGONIST_NUPJUKYI, protagonistName: PROTAGONIST_NAME_KO, artifactId: ARTIFACT_ADMIN_KEYBOARD, premise: run.premise, contentHash: run.campaignContentHash },
    progression: {
      level: run.progressLevel,
      tokens: clone(run.progressTokens),
      tokenDefinitions: clone(run.progressTokenDefinitions),
      rootSystemGate: rootSystemGate(run),
      inventory: clone((entityById(run, run.playerEntityId)?.state?.inventory || []).slice(0, 24))
    },
    macroPhase: clone(run.currentMacroPhase || macroPhaseForBeat(run.currentStoryBeat)),
    turnNo: turn.turnNo,
    remainingTurns,
    act: campaignAct(turn.turnNo, run.turnLimit),
    currentStoryBeat: clone(run.currentStoryBeat),
    currentArcQuestion: clone(run.currentArcQuestion || null),
    emergentStory: clone(run.emergentStory || null),
    resolvedArcOutcomes: clone((run.resolvedArcOutcomes || []).slice(-5)),
    episodeSummaries: clone((run.episodeSummaries || []).slice(-5)),
    storyLedger: clone([...(run.openingNarrative?.storySequence?.length ? [{
      id: deterministicUuid(`${run.id}:opening-dialogue`), turnNo: 0, skillId: "OPENING", outcome: "introduced",
      campaignRole: run.currentStoryBeat?.campaignRole || null, targetEvidenceKeys: [], eventTypes: ["npc_opening_dialogue"],
      narrativeDigest: run.openingNarrative.body,
      narrativeFragments: run.openingNarrative.storySequence.map((beat) => beat.text).filter(Boolean),
      speakerId: run.openingNarrative.storySequence.find((beat) => beat.type === "DIALOGUE")?.speakerId || null
    }] : []), ...(run.storyLedger || [])].slice(-12)),
    choiceHistory: clone((run.choiceHistory || []).slice(-12)),
    selectedChoice: clone(turn.selectedChoice || turn.request?.narrativeChoice || null),
    resolutionMode: turn.resolutionMode || turn.request?.resolutionMode || "D20",
    area: currentArea.name,
    areaSummary: currentArea.summary,
    spatialContext: semanticSpace,
    intent: turn.request.intent,
    playerNote: turn.request.playerNote,
    ability: turn.request.ability,
    skillId: turn.request.skillId,
    actionContext: turn.actionContext,
    normalizedAttempt: turn.normalizedAttempt,
    intentAnalysis: clone(turn.intentAnalysis),
    d20: turn.d20,
    outcome: turn.outcome,
    dice: clone(turn.dice),
    consequenceBudget: turn.consequenceBudget,
    rulesetVersion: turn.rulesetVersion,
    stateHashBefore: turn.stateHashBefore,
    stateHashAfter: turn.stateHashAfter,
    allowedEffects: [...DIRECTOR_OPS],
    allowedEntityIds: visibleEntities.map((item) => item.id),
    allowedQuestIds: [],
    allowedQuestTemplateIds: [],
    activeQuests: run.activeQuests.filter((quest) => quest.status === "active").slice(0, 6).map((quest) => ({ id: quest.id, title: quest.title, summary: quest.summary, currentStep: quest.currentStep, questKind: quest.questKind })),
    visibleEntities,
    placementSlots: [],
    readOnlyPlaces: run.world.pois.map((item) => ({ id: item.id, name: item.name, areaId: item.areaId, biomeId: item.biomeId, campaignRole: item.campaignRole })).slice(0, 24),
    readOnlySlots: run.world.placementSlots.filter((slot) => slot.areaId === currentArea.id).map((slot) => ({ id: slot.id, areaId: slot.areaId, kind: slot.kind, purpose: slot.purpose, reservedFor: slot.reservedFor, tags: slot.tags })).slice(0, 12),
    geometryPolicy: "read_only_ids_and_visual_intent_only",
    canonicalFacts: run.canonicalFacts.slice(-16),
    openLoops: run.openLoops.filter((loop) => loop.status === "open").slice(-8).map((loop) => ({ ...loop, summary: sanitizePlayerFacingHookSummary(loop.summary) })),
    rumors: run.rumors.filter((rumor) => rumor.status === "active").slice(-6),
    npcRelationships: run.npcRelationships.filter((item) => visibleEntities.some((entityItem) => entityItem.id === item.npcId)),
    recentMemories: run.npcMemories.filter((item) => !item.expired).slice(-8),
    majorChoices: (run.majorChoices || []).slice(-8),
    regionOutcomes: (run.regionOutcomes || []).slice(-6),
    abilityUsageHistory: (run.abilityUsageHistory || []).slice(-8),
    adminAccessHistory: clone(run.adminAccessAcquisitionHistory || []),
    technicalDebtEntries: (run.technicalDebtEntries || []).filter((item) => item.resolvedAt === null).slice(-8),
    unresolvedHooks: (run.unresolvedHooks || []).filter((item) => item.status === "open").slice(-8).map((item) => ({ ...item, summary: sanitizePlayerFacingHookSummary(item.summary) })),
    confirmedEffects: clone((turn.events || []).slice(0, 32)),
    sceneSequence: (turn.sceneSequence || []).slice(0, 16).map((item) => ({
      actionId: item.actionId, sequence: item.sequence, type: item.type, actorId: item.actorId || null, targetId: item.targetId || null,
      actionStyle: item.actionStyle || null, hit: item.hit ?? null, damage: item.damage ?? null,
      rewardId: item.rewardId || null, text: item.text || null
    })),
    endingFactors: run.status === "completed" ? clone(run.finaleResolution?.endingFactors || null) : null
  };
}

function restoreCandidatesForRun(run) {
  const candidates = [];
  const seen = new Set();
  for (const ledger of [...run.reversibleLedger].reverse()) {
    if (ledger.consumed || run.currentTurn - ledger.turnNo > 8) continue;
    for (const operation of ledger.inverseOps || []) {
      let entityId = null;
      let reason = null;
      let damage = null;
      if (operation.type === "restore_entity") {
        entityId = operation.entity?.id;
        const current = entityAnyById(run, entityId);
        if (!current || current.active) continue;
        reason = "removed";
      } else if (operation.type === "restore_state") {
        entityId = operation.entityId;
        const current = entityAnyById(run, entityId);
        if (!current?.active) continue;
        const snapshotHp = operation.stateSnapshot?.hp;
        const currentHp = current.state?.hp;
        const disabledChanged = "disabled" in (operation.stateSnapshot || {}) && Boolean(operation.stateSnapshot.disabled) !== Boolean(current.state?.disabled);
        if (!(Number.isInteger(snapshotHp) && Number.isInteger(currentHp) && snapshotHp > currentHp) && !disabledChanged) continue;
        reason = "recent_damage";
        damage = { currentHp: Number.isInteger(currentHp) ? currentHp : null, restorableHp: Number.isInteger(snapshotHp) ? snapshotHp : null, maxHp: Number.isInteger(current.state?.maxHp) ? current.state.maxHp : null, disabled: Boolean(current.state?.disabled) };
      }
      if (!entityId || seen.has(entityId)) continue;
      const current = entityAnyById(run, entityId);
      const snapshot = operation.entity;
      seen.add(entityId);
      candidates.push({ id: entityId, name: current?.name || snapshot?.name || "Restorable entity", kind: current?.kind || snapshot?.kind || "unknown", active: Boolean(current?.active), position: clone(current?.position || snapshot?.position || { x: 0, y: 0 }), sourceTurn: ledger.turnNo, reason, damage });
      if (candidates.length >= 12) return candidates;
    }
  }
  return candidates;
}

export function publicRun(run) {
  // Old persisted runs acquired access before anchors were marked disabled. The
  // acquisition history is authoritative, so reconcile the public projection too;
  // otherwise a consumed token can return after reconnect as a giant, targetable prop.
  const resolvedAdminCandidateIds = new Set((run.adminAccessAcquisitionHistory || [])
    .map((item) => item.candidateId).filter(Boolean));
  const acquiredAccessLevels = new Set((run.adminAccessAcquisitionHistory || [])
    .map((item) => item.accessLevelId).filter(Boolean));
  const nextPublicAccessLevel = ADMIN_ACCESS_LEVELS.find((level) =>
    !acquiredAccessLevels.has(level.id))?.id || null;
  const publicEntities = run.entities
    .filter((item) => item.active && item.state?.adminAccessResolved !== true
      && !(item.state?.candidateId && resolvedAdminCandidateIds.has(item.state.candidateId))
      && (!item.state?.adminAccessLevelId || item.state.adminAccessLevelId === nextPublicAccessLevel))
    .map((item) => ({ id: item.id, kind: item.kind, assetId: item.assetId, name: item.name, position: item.position, state: item.state, blocking: item.blocking, protected: item.protected, cloneable: item.cloneable, capabilities: capabilitiesFor(item) }));
  const player = publicEntities.find((item) => item.id === run.playerEntityId);
  const entityNames = new Map(publicEntities.map((item) => [item.id, item.name]));
  const pendingChoiceSet = run.status === "active" ? clone(run.pendingChoiceSet || null) : null;
  if (pendingChoiceSet?.choices) {
    pendingChoiceSet.choices = reconcileNarrativeSkillChoices(pendingChoiceSet.choices, {
      run,
      allowedEntityIds: authoritativeNarrativeEntityIds(run)
    });
    pendingChoiceSet.suggestedSkillIds = [...new Set(pendingChoiceSet.choices
      .filter((choice) => choice.choiceKind === "SKILL")
      .map((choice) => choice.skillId))];
  }
  return {
    id: run.id,
    campaignId: run.campaignId,
    campaignTitle: run.campaignTitle,
    gameTitle: run.gameTitle || GAME_TITLE,
    worldId: run.worldId || WORLD_CODRIA,
    worldName: run.worldName,
    protagonistId: run.protagonistId || PROTAGONIST_NUPJUKYI,
    protagonistName: run.protagonistName || PROTAGONIST_NAME_KO,
    artifactId: run.artifactId || ARTIFACT_ADMIN_KEYBOARD,
    archetype: run.archetype,
    premise: run.premise,
    templateId: run.templateId,
    status: run.status,
    version: run.version,
    currentTurn: run.currentTurn,
    turnLimit: run.turnLimit,
    remainingTurns: Math.max(0, run.turnLimit - run.currentTurn),
    currentAct: run.currentAct,
    campaignPhase: run.campaignPhase,
    currentMacroPhase: clone(run.currentMacroPhase || macroPhaseForBeat(run.currentStoryBeat)),
    campaignMacroPhases: clone(run.campaignMacroPhases || []),
    currentBeat: run.currentStoryBeat?.title || "",
    currentStoryBeat: run.currentStoryBeat,
    currentArcQuestion: clone(run.currentArcQuestion || null),
    emergentStory: clone(run.emergentStory || null),
    arcQuestions: clone(run.arcQuestions || []),
    resolvedArcOutcomes: clone(run.resolvedArcOutcomes || []),
    episodeSummaries: clone(run.episodeSummaries || []),
    storyLedger: clone((run.storyLedger || []).slice(-12)),
    choiceHistory: clone((run.choiceHistory || []).slice(-40)),
    pendingChoiceSet,
    openingNarrative: clone(run.openingNarrative || null),
    health: player?.state?.hp ?? 0,
    maxHealth: player?.state?.maxHp ?? 0,
    focus: run.focus,
    maxFocus: run.maxFocus || 10,
    experience: run.experience || 0,
    gold: run.gold || 0,
    inventory: clone(player?.state?.inventory || []),
    specialSkills: clone(run.directorState?.specialSkills || []),
    inventoryHistory: clone((run.inventoryHistory || []).slice(-40)),
    pressure: run.pressure,
    exposed: run.exposed,
    endingCode: run.endingCode,
    finaleResolution: run.finaleResolution,
    finalePuzzle: run.finalePuzzle,
    progressLevel: run.progressLevel,
    progressTokens: run.progressTokens,
    progressTokenDefinitions: run.progressTokenDefinitions,
    metrics: run.metrics,
    endingWindow: run.endingWindow,
    navigationSequence: run.navigationSequence,
    safeTravelCount: run.safeTravelCount,
    travelTime: run.travelTime || 0,
    travelTimeUnits: run.travelTimeUnits || 0,
    travelDistance: run.travelDistance || 0,
    nextStoryEventDistance: run.nextStoryEventDistance || 0,
    storyEventSequence: run.storyEventSequence || 0,
    visitedPoiIds: run.visitedPoiIds,
    discoveredAreaIds: run.discoveredAreaIds,
    activeEncounter: run.activeEncounter ? clone(run.activeEncounter) : null,
    encounterHistory: (run.encounterHistory || []).slice(-12).map((item) => clone(item)),
    rootSystemGate: rootSystemGate(run),
    finaleGate: { ...rootSystemGate(run), requiredProgressLevel: 3, missingProgressTokens: rootSystemGate(run).missingAdminAccessLevels },
    endingCandidates: run.endingCandidates.map((item) => item.title),
    endingCandidateDetails: run.endingCandidates,
    endingConditionReports: endingConditionReports(run),
    playerEntityId: run.playerEntityId,
    spatialContext: spatialContext(run),
    dormantEntityCount: run.entities.filter((item) => item.active === false && item.state?.activationState === "DORMANT").length,
    entities: publicEntities,
    connections: run.connections.filter((item) => item.active),
    world: publicWorld(run.world),
    activeQuests: run.activeQuests,
    canonicalFacts: run.canonicalFacts,
    openLoops: run.openLoops.filter((item) => item.status === "open").map((item) => ({ ...item, summary: sanitizePlayerFacingHookSummary(item.summary) })),
    rumors: run.rumors.filter((item) => item.status === "active"),
    npcRelationships: run.npcRelationships.map((item) => ({ ...item, npcName: entityNames.get(item.npcId) || "", score: item.affinity, label: item.stance, reason: "authoritative relationship state" })),
    npcPromises: clone(run.npcPromises || []),
    npcMemories: run.npcMemories.filter((item) => !item.expired).map((item) => ({ ...item, npcName: entityNames.get(item.npcId) || "", memory: item.summary, importance: Math.round(item.importance * 100), turnNo: item.createdTurn })),
    directorState: clone(run.directorState || {}),
    majorChoices: clone(run.majorChoices || []),
    regionOutcomes: clone(run.regionOutcomes || []),
    abilityUsageHistory: clone(run.abilityUsageHistory || []),
    adminAccessLevels: clone(run.adminAccessLevels || ADMIN_ACCESS_LEVELS),
    adminAccessCandidates: clone(run.adminAccessCandidates || []),
    adminAccessAcquisitionHistory: clone(run.adminAccessAcquisitionHistory || []),
    technicalDebtEntries: clone(run.technicalDebtEntries || []),
    unresolvedHooks: clone((run.unresolvedHooks || []).filter((item) => item.status === "open").map((item) => ({ ...item, summary: sanitizePlayerFacingHookSummary(item.summary) }))),
    restoreCandidates: restoreCandidatesForRun(run),
    generationPlan: run.generationPlan,
    campaignContentHash: run.campaignContentHash,
    abilities: CORE_ABILITIES,
    actionContexts: CONTEXT_ACTIONS,
    inputTypes: INPUT_TYPES,
    createdAt: run.createdAt,
    updatedAt: run.updatedAt
  };
}

const GAMEPLAY_ELEMENT_BY_EFFECT = Object.freeze({
  ELEMENTAL_EXPLOSION: "EXPLOSION",
  ELEMENTAL_FLAME: "FLAM",
  ELEMENTAL_ICE: "ICE",
  ELEMENTAL_ICE_B: "ICE",
  ELEMENTAL_ICE_FLAKE: "ICE",
  ELEMENTAL_PLANT: "PLANT",
  ELEMENTAL_PLANT_B: "PLANT",
  ELEMENTAL_ROCK: "ROCK",
  ELEMENTAL_ROCK_B: "ROCK",
  ELEMENTAL_ROCK_SPIKE: "ROCK_SPIKE",
  ELEMENTAL_THUNDER: "THUNDER",
  ELEMENTAL_WATER: "WATER",
  ELEMENTAL_WATER_PILLAR: "WATER_PILLAR"
});

function runtimeEntityType(entity) {
  return { player: "PLAYER", npc: "NPC", enemy: "ENEMY", prop: "PROP" }[entity?.kind] || null;
}

function runtimeEntityRefs(run, request, events = []) {
  const ids = new Set([request?.targetEntityId, request?.secondaryTargetEntityId].filter(Boolean));
  for (const event of events) {
    for (const field of ["entityId", "actorId", "sourceEntityId", "targetEntityId", "npcId", "fromId", "toId"]) {
      if (typeof event?.[field] === "string") ids.add(event[field]);
    }
  }
  const refs = [];
  for (const id of ids) {
    const entity = entityAnyById(run, id);
    const entityType = runtimeEntityType(entity);
    if (entityType) refs.push({ id, entityType });
  }
  const itemIds = new Set([...(request?.itemIds || [])]);
  for (const event of events) {
    for (const field of ["itemId", "resultItemId"]) if (typeof event?.[field] === "string") itemIds.add(event[field]);
    for (const item of event?.consumedItems || []) if (typeof item?.itemId === "string") itemIds.add(item.itemId);
  }
  for (const id of itemIds) refs.push({ id, entityType: "INVENTORY_ITEM" });
  return refs;
}

function resultFx(turn, resultTier, skillId) {
  const fallbackEffect = {
    ATTACK: "ELEMENTAL_FLAME",
    DELETE: "ELEMENTAL_ROCK_SPIKE",
    SELECT_ALL: "ELEMENTAL_EXPLOSION"
  }[skillId] || null;
  const effectId = turn.narrative?.elementalEffectId || fallbackEffect;
  return {
    scaleTier: {
      CRITICAL_FAILURE: "TILE",
      FAILURE: "SMALL",
      PARTIAL_SUCCESS: "MEDIUM",
      SUCCESS: "LARGE",
      STRONG_SUCCESS: "SCREEN"
    }[resultTier] || "TILE",
    element: GAMEPLAY_ELEMENT_BY_EFFECT[effectId] || null,
    effectId
  };
}

function entityRef(turn, id) {
  return (turn.runtimeEntityRefs || []).find((ref) => ref.id === id) || (id ? { id, entityType: "UNKNOWN" } : null);
}

function gameplayResult(turn, resultTier, unityType) {
  if (!unityType || turn.d20 === null || turn.d20 === undefined) return null;
  const events = turn.events || [];
  const skillId = String(turn.request?.skillId || "").toUpperCase();
  const targetIds = [turn.request?.targetEntityId, turn.request?.secondaryTargetEntityId].filter(Boolean);
  const targetRefs = targetIds.map((id) => entityRef(turn, id));
  const succeeded = SUCCESS_OUTCOMES.has(turn.outcome);
  const first = (type) => events.find((event) => event.type === type) || null;
  let result = { targetRefs };
  if (skillId === "ATTACK") {
    const health = first("health_changed");
    result = {
      target: entityRef(turn, health?.entityId || targetIds[0]),
      hit: Number(health?.delta || 0) < 0,
      damage: Math.abs(Math.min(0, Number(health?.delta || 0))),
      destroyed: health?.disabled === true,
      attackStyle: "DIRECT",
      range: 1,
      radius: 0,
      speed: { CRITICAL_FAILURE: 0.7, FAILURE: 0.85, PARTIAL_SUCCESS: 1, SUCCESS: 1.2, STRONG_SUCCESS: 1.4 }[resultTier]
    };
  } else if (skillId === "MOVE") {
    const moved = first("entity_moved");
    result = { actor: entityRef(turn, moved?.entityId), moved: Boolean(moved), from: clone(moved?.from || null), to: clone(moved?.to || null),
      path: clone(moved?.path || []), facing: turn.spatialSnapshot?.player?.facing || null, arrived: Boolean(moved) };
  } else if (skillId === "COPY") {
    const spawned = first("entity_spawned");
    const repeat = first("copy_repeat_rejected");
    result = {
      target: entityRef(turn, spawned?.sourceEntityId || repeat?.entityId || targetIds[0]),
      clone: entityRef(turn, spawned?.entityId),
      lineageRootId: repeat?.lineageRootId || spawned?.sourceEntityId || null,
      copyLocked: Boolean(spawned || repeat),
      rejectionReason: repeat?.reason || null
    };
  } else if (skillId === "DELETE") {
    const intervention = first("encounter_intervention");
    const health = first("health_changed");
    result = {
      target: entityRef(turn, health?.entityId || intervention?.entityId || targetIds[0]),
      hit: Number(health?.delta || 0) < 0,
      damage: Math.abs(Math.min(0, Number(health?.delta || 0))),
      destroyed: events.some((event) => event.type === "entity_removed" && event.entityId === (intervention?.entityId || targetIds[0])),
      resolution: intervention?.resolution || (succeeded ? "PRESSURED" : "RESISTED")
    };
  } else if (skillId === "CONNECT") {
    const connections = events.filter((event) => event.type === "connection_created");
    result = {
      targets: targetRefs,
      connections: connections.map((connection) => ({ id: connection.id, from: entityRef(turn, connection.fromId), to: entityRef(turn, connection.toId), relation: connection.relation, expiresTurn: connection.expiresTurn }))
    };
  } else if (skillId === "RESTORE") {
    const restored = first("entity_restored") || first("entity_state_restored");
    const spent = first("reversible_reward_spent");
    result = {
      target: entityRef(turn, restored?.entityId || targetIds[0]),
      restorationDegree: { CRITICAL_FAILURE: "FAIL", FAILURE: "FAIL", PARTIAL_SUCCESS: "BAD", SUCCESS: "GOOD", STRONG_SUCCESS: "BETTER" }[resultTier],
      restoredFields: clone(restored?.fields || []),
      sourceSnapshotTurn: spent?.sourceTurn ?? null
    };
  } else if (skillId === "UNDO") {
    const undo = first("undo_compensation_completed");
    result = { sourceTurns: clone(undo?.sourceTurns || []), compensatedTurns: undo?.turns || 0, runTurnRewound: false };
  } else if (skillId === "SEARCH") {
    const investigated = first("entity_investigated");
    const discovery = first("llm_discovery_event") || first("inventory_item_acquired") || first("ambient_fallback_applied");
    result = {
      target: entityRef(turn, investigated?.entityId || targetIds[0]),
      alreadyInvestigated: investigated?.repeat === true,
      revealedEvidenceIds: investigated?.evidenceKey ? [investigated.evidenceKey] : [],
      newInformation: Boolean((investigated && investigated.repeat !== true) || discovery),
      discoveryType: discovery?.discoveryType || discovery?.source || null,
      informationTitle: discovery?.title || discovery?.itemName || null
    };
  } else if (skillId === "SELECT_ALL") {
    const group = first("group_intervention_resolved");
    const affectedIds = [...new Set(events.filter((event) => event.type === "health_changed" && event.source === "SELECT_ALL").map((event) => event.entityId))];
    const defeatedIds = [...new Set(events.filter((event) => event.type === "entity_removed" && event.source === "SELECT_ALL").map((event) => event.entityId))];
    const radius = group?.radius || 4;
    result = {
      radius,
      damagePerTarget: group?.damagePerTarget || 3,
      affectedTargets: affectedIds.map((id) => entityRef(turn, id)),
      defeatedTargets: defeatedIds.map((id) => entityRef(turn, id)),
      affectedCount: group?.affectedCount || affectedIds.length,
      defeatedCount: group?.defeatedCount || defeatedIds.length
    };
  } else if (skillId === "USE_ITEM") {
    const used = first("inventory_item_used");
    result = { item: entityRef(turn, used?.itemId || turn.request?.itemIds?.[0]), consumed: used?.consumed === true, quantity: used?.quantity || 0 };
  } else if (skillId === "COMBINE") {
    const combined = first("inventory_items_combined");
    result = {
      sources: (combined?.consumedItems || []).map((item) => entityRef(turn, item.itemId)),
      createdItem: entityRef(turn, combined?.resultItemId),
      consumed: clone(combined?.consumedItems || [])
    };
  }
  return {
    schemaVersion: "1.0",
    actionType: unityType,
    context: turn.actionContext,
    outcome: resultTier,
    succeeded,
    rollId: `${turn.id}:roll`,
    fx: resultFx(turn, resultTier, skillId),
    result
  };
}

export function publicTurn(turn) {
  const dialogueDetails = Array.isArray(turn.narrative?.dialogue) ? turn.narrative.dialogue.map((item) => typeof item === "string" ? { speakerId: null, line: item } : item) : [];
  const publicOperation = (item) => ({ ...item, cost: item.budgetCost ?? item.cost ?? 0 });
  const publicDelta = {
    ...turn.stateDelta,
    npcMemories: (turn.stateDelta?.npcMemories || []).map((item) => ({ ...item, memory: item.summary, importance: Math.round(item.importance * 100), turnNo: item.createdTurn })),
    relationships: (turn.stateDelta?.relationships || []).map((item) => ({ ...item, score: item.affinity, label: item.stance })),
    appliedOps: (turn.stateDelta?.appliedOps || []).map(publicOperation),
    rejectedOps: (turn.stateDelta?.rejectedOps || []).map(publicOperation)
  };
  const resolutionRequired = (turn.resolutionMode || turn.request?.resolutionMode || "D20") !== "NONE" && turn.d20 !== null && turn.d20 !== undefined;
  const resultTier = {
    critical_failure: "CRITICAL_FAILURE",
    failure: "FAILURE",
    partial_success: "PARTIAL_SUCCESS",
    success: "SUCCESS",
    critical_success: "STRONG_SUCCESS"
  }[turn.outcome] || null;
  const skillId = String(turn.request?.skillId || "").toUpperCase();
  const unityType = {
    ATTACK: "ATTACK",
    MOVE: "MOVE",
    INTERACT: "INTERACT",
    NEGOTIATE: "NEGOTIATE",
    REST: "REST",
    USE_ITEM: "USE_ITEM",
    COMBINE: "COMBINE",
    SEARCH: "SEARCH",
    COPY: "COPY",
    DELETE: "DELETE",
    CONNECT: "CONNECT",
    RESTORE: "RESTORE",
    UNDO: "UNDO",
    SELECT_ALL: "SELECT_ALL"
  }[skillId] || null;
  const typedGameplayResult = resolutionRequired ? gameplayResult(turn, resultTier, unityType) : null;
  const unityEvents = resolutionRequired && unityType ? [{
    eventId: `${turn.id}:unity:0`,
    type: unityType,
    actorId: turn.request?.actorId || PROTAGONIST_NUPJUKYI,
    targetIds: [turn.request?.targetEntityId, turn.request?.secondaryTargetEntityId].filter(Boolean),
    resultTier,
    sequence: 0,
    payload: {
      skillId,
      intensity: resultTier === "STRONG_SUCCESS" ? "HIGH" : resultTier === "SUCCESS" ? "MEDIUM" : "LOW",
      effectId: typedGameplayResult?.fx?.effectId || null,
      element: typedGameplayResult?.fx?.element || null,
      fxScaleTier: typedGameplayResult?.fx?.scaleTier || "TILE",
      gameplayResult: clone(typedGameplayResult),
      confirmedEvents: clone(turn.events || [])
    }
  }] : [];
  const runtime = {
    turn: {
      turnNo: turn.turnNo,
      intent: resolutionRequired ? skillId : "DIALOGUE",
      model: turn.narrative?.model || "deterministic-fallback-v2",
      fallbackUsed: turn.narrative?.fallbackUsed === true
    },
    narrative: {
      storySequence: clone((turn.narrative?.storySequence || []).map((beat) => beat.type === "WORLD_ACTION" ? { ...beat, type: "NARRATION" } : beat)),
      nextIntervention: clone(turn.narrative?.nextIntervention || null)
    },
    resolution: {
      required: resolutionRequired,
      roll: resolutionRequired ? {
        rollId: `${turn.id}:roll`,
        d20: turn.d20,
        modifier: turn.dice?.modifier ?? 0,
        total: turn.d20 + (turn.dice?.modifier ?? 0),
        mechanicalScore: turn.mechanicalScore,
        resultTier
      } : null,
      healthChanges: clone((turn.events || []).filter((event) => event.type === "health_changed" || event.resource === "health" || event.resource === "hp")),
      inventoryChanges: clone((turn.events || []).filter((event) => /inventory|item_acquired|prop_looted/i.test(event.type || ""))),
      skillChanges: clone((turn.events || []).filter((event) => /special_reward|special_skill/i.test(event.type || ""))),
      statusChanges: clone((turn.events || []).filter((event) => /status|exposed|stun|burn|poison/i.test(event.type || ""))),
      movementChanges: clone((turn.events || []).filter((event) => /entity_moved|travel|destination/i.test(event.type || ""))),
      relationshipChanges: clone((turn.events || []).filter((event) => /relationship|negotiation|affinity|promise/i.test(event.type || ""))),
      worldChanges: clone((turn.events || []).filter((event) => /entity_spawned|entity_activated|entity_removed|entity_restored|connection_created|connection_removed/i.test(event.type || ""))),
      confirmedEffects: clone(turn.events || [])
    },
    gameplayResult: typedGameplayResult,
    spatial: clone(turn.spatialSnapshot || null),
    unity: {
      renderRequired: unityEvents.length > 0,
      events: unityEvents
    },
    runVersion: turn.committedRunVersion
  };
  return {
    id: turn.id,
    runId: turn.runId,
    turnNo: turn.turnNo,
    expectedRunVersion: turn.expectedRunVersion,
    committedRunVersion: turn.committedRunVersion,
    request: turn.request,
    resolutionMode: turn.resolutionMode || turn.request?.resolutionMode || "D20",
    selectedChoice: clone(turn.selectedChoice || null),
    actionContext: turn.actionContext,
    campaignTurnConsumed: true,
    normalizedAttempt: turn.normalizedAttempt,
    intentAnalysis: turn.intentAnalysis,
    d20: turn.d20,
    dice: turn.dice,
    mechanicalScore: turn.mechanicalScore,
    outcome: turn.outcome,
    consequenceBudget: turn.consequenceBudget,
    stateDelta: publicDelta,
    events: turn.events,
    sceneDecision: clone(turn.sceneDecision || null),
    sceneSequence: clone(turn.sceneSequence || []),
    narrative: {
      ...turn.narrative,
      dialogue: dialogueDetails.map((item) => item.line),
      dialogueDetails,
      proposedOps: (turn.narrative?.proposedOps || []).map(publicOperation),
      appliedOps: (turn.narrative?.appliedOps || []).map(publicOperation),
      rejectedOps: (turn.narrative?.rejectedOps || []).map(publicOperation)
    },
    runtime,
    createdAt: turn.createdAt
  };
}

export function tryForceMonsterEncounter(run, text, turnNo, idempotencyKey) {
  if (!isMonsterEncounterRequest(text)) return null;

  const player = run.entities.find((entity) => entity.id === run.playerEntityId && entity.active);
  const currentArea = player ? areaAt(run.world, player.position) : null;
  if (!currentArea) return null;

  const nearbyDormant = run.entities
    .filter((entity) => entity.kind === "enemy" && entity.active === false
      && entity.state?.activationState === "DORMANT" && entity.state?.dormant === true)
    .map((entity) => ({ entity, slot: run.world.placementSlots.find((item) => item.id === entity.state.activationSlotId) || null }))
    .filter(({ entity, slot: candidateSlot }) => candidateSlot?.areaId === currentArea.id
      && manhattan(player.position, candidateSlot) <= 3
      && !run.entities.some((other) => other.active
        && (other.state?.slotId === entity.state.activationSlotId || (other.position.x === candidateSlot.x && other.position.y === candidateSlot.y))))
    .sort((left, right) => manhattan(player.position, left.slot) - manhattan(player.position, right.slot)
      || left.entity.id.localeCompare(right.entity.id))[0] || null;

  let dormant = nearbyDormant?.entity || null;
  let slot = nearbyDormant?.slot || null;

  if (!dormant) {
    // 맵에 더이상 대기 중인 적 슬롯이 없거나 근처에 적이 없는 경우, 플레이어 근처 walkable 타일에 동적으로 새로운 몬스터를 스폰시킵니다.
    const spawnPos = findClosestWalkableTile(run, player.position, 3);

    if (!spawnPos) return null;

    const selectionHash = fingerprint(idempotencyKey || "spawn");
    const monsterIndex = Number.parseInt(selectionHash.slice(0, 8), 16) % MONSTER_CATALOG.length;
    const monster = MONSTER_CATALOG[monsterIndex];
    const newId = deterministicUuid(`${run.id}:forced-monster:${turnNo}:${idempotencyKey}`);

    dormant = entity(newId, "enemy", monster.assetId, `${monster.name} 변종`, spawnPos, true, false, false, {
      hp: monster.hp, maxHp: monster.hp, speed: monster.speed, slotId: null, activationSlotId: null, activationState: "ACTIVE", dormant: false,
      traits: [...monster.traits], factionId: "WILD_PROCESS", goal: "현재 지역의 시스템 규칙을 보존한다.", motivation: "침입으로 판단한 변화를 시험한다.",
      awareness: [run.playerEntityId], lastActionTurn: 0
    });
    run.entities.push(dormant);
  } else {
    dormant.active = true;
    dormant.blocking = true;
    dormant.position = { x: slot.x, y: slot.y };
    dormant.state = {
      ...dormant.state,
      dormant: false,
      activationState: "ACTIVE",
      activatedDecisionNo: turnNo,
      generatedDecisionNo: turnNo,
      slotId: slot.id,
      awareness: [run.playerEntityId],
      lastActionTurn: 0
    };
  }

  const choices = { kind: "COMBAT", suggestedSkillIds: ["DELETE", "SELECT_ALL"] };
  run.activeEncounter = {
    id: deterministicUuid(`${run.id}:encounter:${turnNo}:${idempotencyKey}`),
    status: "active",
    mode: "confrontation",
    escalation: "stable",
    reason: "hostile_proximity",
    description: `${dormant.name} 조우!`,
    sourceEntityId: dormant.id,
    entityId: dormant.id,
    slotId: slot ? slot.id : null,
    assetId: dormant.assetId,
    spawnKind: "enemy",
    displayName: dormant.name,
    traitIds: [...(dormant.state.traits || [])],
    ...choices
  };
  run.encounterHistory ||= [];
  run.encounterHistory.push(clone(run.activeEncounter));

  run.npcRelationships ||= [];
  let relationship = run.npcRelationships.find((item) => item.npcId === dormant.id);
  if (!relationship) {
    relationship = { npcId: dormant.id, affinity: -10, trust: 0, fear: 2, stance: "hostile", encounterStatus: "present", lastChangedTurn: turnNo };
    run.npcRelationships.push(relationship);
  } else {
    relationship.stance = "hostile";
    relationship.encounterStatus = "present";
    relationship.lastChangedTurn = turnNo;
  }

  return dormant;
}
