import { assert, AppError } from "../errors.js";

export const SCENE_ACTION_TYPES = Object.freeze([
  "MOVE_ACTOR",
  "ATTACK_ENTITY",
  "DEFEND_ENTITY",
  "ASSIST_ENTITY",
  "FLEE_ENTITY",
  "LOOT_PROP",
  "OPEN_PROP",
  "START_DIALOGUE",
  "REVEAL_CLUE",
  "START_ENCOUNTER",
  "SPAWN_FROM_SLOT",
  "CHANGE_RELATIONSHIP",
  "ADD_NPC_MEMORY",
  "CREATE_HOOK",
  "START_QUEST",
  "ADVANCE_QUEST",
  "GRANT_SPECIAL_REWARD",
  "NARRATIVE_EVENT",
  "NO_EVENT"
]);

export const FREEFORM_SCENE_ACTION_TYPES = Object.freeze([
  "MOVE_ACTOR", "ATTACK_ENTITY", "DEFEND_ENTITY", "ASSIST_ENTITY", "FLEE_ENTITY",
  "START_DIALOGUE", "NARRATIVE_EVENT"
]);

const MAX_SELECTED_ACTIONS = 4;
const ID_PATTERN = /^[0-9a-f-]{36}$/i;
const PROTECTED_PROPOSAL_PATTERN = /(?:관리자\s*권한.{0,24}(?:획득|해금|부여)|결말.{0,20}(?:확정|변경|도달)|피날레.{0,20}(?:완료|해결)|(?:최종\s*)?보스.{0,20}(?:즉사|한\s*방|소멸)|죽은.{0,16}부활|좌표\s*[(:=]|(?:피해|damage|hp)\s*[:=]?\s*\d+|보상.{0,16}(?:획득|지급))/i;

function boundedString(value, name, minimum, maximum, status = 400) {
  if (typeof value !== "string") throw new AppError(status, status === 400 ? "SCENE_CONTEXT_INVALID" : "SCENE_PLAN_INVALID", `${name} must be a string.`);
  const normalized = value.trim();
  if (normalized.length < minimum || normalized.length > maximum) throw new AppError(status, status === 400 ? "SCENE_CONTEXT_INVALID" : "SCENE_PLAN_INVALID", `${name} is outside its length limit.`);
  return normalized;
}

function exactKeys(object, allowed, code, status) {
  const unknown = Object.keys(object).filter((key) => !allowed.includes(key));
  if (unknown.length > 0) throw new AppError(status, code, `Unknown fields: ${unknown.join(", ")}.`);
}

export function validateSceneDirectorContext(input) {
  assert(input && typeof input === "object" && !Array.isArray(input), 400, "SCENE_CONTEXT_INVALID", "A scene director context is required.");
  exactKeys(input, ["schemaVersion", "requestType", "decisionType", "decisionNo", "campaign", "macroPhase", "storyBeat", "area", "playerId", "actors", "candidates", "recentSceneTypes", "openLoops", "npcRelationships", "fixedCanon", "maxSelectedActions"], "SCENE_CONTEXT_INVALID", 400);
  assert(input.schemaVersion === "1.0" && input.requestType === "SCENE_PLAN", 400, "SCENE_CONTEXT_INVALID", "Unsupported scene plan context version.");
  assert(["TRAVEL", "ACTION"].includes(input.decisionType), 400, "SCENE_CONTEXT_INVALID", "decisionType must be TRAVEL or ACTION.");
  assert(Number.isInteger(input.decisionNo) && input.decisionNo >= 1, 400, "SCENE_CONTEXT_INVALID", "decisionNo must be positive.");
  assert(input.campaign && typeof input.campaign === "object", 400, "SCENE_CONTEXT_INVALID", "campaign is required.");
  assert(input.macroPhase && typeof input.macroPhase.id === "string", 400, "SCENE_CONTEXT_INVALID", "macroPhase is required.");
  assert(input.storyBeat && typeof input.storyBeat.id === "string", 400, "SCENE_CONTEXT_INVALID", "storyBeat is required.");
  boundedString(input.area, "area", 1, 120);
  assert(typeof input.playerId === "string" && ID_PATTERN.test(input.playerId), 400, "SCENE_CONTEXT_INVALID", "playerId must be a UUID.");
  assert(Array.isArray(input.actors) && input.actors.length >= 1 && input.actors.length <= 16, 400, "SCENE_CONTEXT_INVALID", "actors must contain the nearby story actors.");
  for (const actor of input.actors) {
    exactKeys(actor, ["id", "kind", "name", "distance", "stance", "disabled"], "SCENE_CONTEXT_INVALID", 400);
    assert(typeof actor.id === "string" && ID_PATTERN.test(actor.id), 400, "SCENE_CONTEXT_INVALID", "actor.id must be a UUID.");
    assert(["player", "npc", "enemy"].includes(actor.kind), 400, "SCENE_CONTEXT_INVALID", "actor.kind is unsupported.");
  }
  assert(Array.isArray(input.candidates) && input.candidates.length >= 1 && input.candidates.length <= 32, 400, "SCENE_CONTEXT_INVALID", "candidates must contain 1-32 items.");
  const candidateIds = new Set();
  for (const candidate of input.candidates) {
    assert(candidate && typeof candidate === "object" && !Array.isArray(candidate), 400, "SCENE_CONTEXT_INVALID", "Each candidate must be an object.");
    exactKeys(candidate, ["candidateId", "type", "actorId", "targetId", "priority", "cost", "reason", "actionStyle", "slotId", "assetId", "spawnKind", "displayName", "traitIds", "rewardId", "questId", "pendingId", "delta", "text", "proposalIndex"], "SCENE_CONTEXT_INVALID", 400);
    assert(typeof candidate.candidateId === "string" && ID_PATTERN.test(candidate.candidateId) && !candidateIds.has(candidate.candidateId), 400, "SCENE_CONTEXT_INVALID", "candidateId must be a unique UUID.");
    candidateIds.add(candidate.candidateId);
    assert(SCENE_ACTION_TYPES.includes(candidate.type), 400, "SCENE_CONTEXT_INVALID", "Candidate type is unsupported.");
    assert(candidate.actorId === null || (typeof candidate.actorId === "string" && ID_PATTERN.test(candidate.actorId)), 400, "SCENE_CONTEXT_INVALID", "actorId must be null or a UUID.");
    assert(candidate.targetId === null || (typeof candidate.targetId === "string" && ID_PATTERN.test(candidate.targetId)), 400, "SCENE_CONTEXT_INVALID", "targetId must be null or a UUID.");
    assert(Number.isInteger(candidate.priority) && candidate.priority >= 0 && candidate.priority <= 100, 400, "SCENE_CONTEXT_INVALID", "priority is invalid.");
    assert(Number.isInteger(candidate.cost) && candidate.cost >= 0 && candidate.cost <= 4, 400, "SCENE_CONTEXT_INVALID", "cost is invalid.");
    boundedString(candidate.reason, "candidate.reason", 1, 180);
    assert(candidate.actionStyle === null || candidate.actionStyle === undefined || (typeof candidate.actionStyle === "string" && candidate.actionStyle.length <= 40), 400, "SCENE_CONTEXT_INVALID", "actionStyle is invalid.");
    for (const field of ["slotId", "assetId", "spawnKind", "displayName", "rewardId", "questId", "pendingId"]) {
      assert(candidate[field] === undefined || candidate[field] === null || (typeof candidate[field] === "string" && candidate[field].length >= 1 && candidate[field].length <= 120), 400, "SCENE_CONTEXT_INVALID", `${field} is invalid.`);
    }
    assert(candidate.traitIds === undefined || (Array.isArray(candidate.traitIds) && candidate.traitIds.length <= 4 && candidate.traitIds.every((trait) => typeof trait === "string" && trait.length >= 1 && trait.length <= 40)), 400, "SCENE_CONTEXT_INVALID", "traitIds is invalid.");
    assert(candidate.spawnKind === undefined || ["npc", "enemy"].includes(candidate.spawnKind), 400, "SCENE_CONTEXT_INVALID", "spawnKind is invalid.");
    assert(candidate.delta === undefined || (Number.isInteger(candidate.delta) && candidate.delta >= -5 && candidate.delta <= 5), 400, "SCENE_CONTEXT_INVALID", "delta is invalid.");
    assert(candidate.text === undefined || (typeof candidate.text === "string" && candidate.text.length >= 1 && candidate.text.length <= 240), 400, "SCENE_CONTEXT_INVALID", "text is invalid.");
    assert(candidate.proposalIndex === undefined || (Number.isInteger(candidate.proposalIndex) && candidate.proposalIndex >= 0 && candidate.proposalIndex < 4), 400, "SCENE_CONTEXT_INVALID", "proposalIndex is invalid.");
  }
  assert(Array.isArray(input.recentSceneTypes) && input.recentSceneTypes.length <= 8, 400, "SCENE_CONTEXT_INVALID", "recentSceneTypes is invalid.");
  assert(Array.isArray(input.openLoops) && input.openLoops.length <= 8, 400, "SCENE_CONTEXT_INVALID", "openLoops is invalid.");
  assert(Array.isArray(input.npcRelationships) && input.npcRelationships.length <= 16, 400, "SCENE_CONTEXT_INVALID", "npcRelationships is invalid.");
  assert(Array.isArray(input.fixedCanon) && input.fixedCanon.length >= 1 && input.fixedCanon.length <= 16, 400, "SCENE_CONTEXT_INVALID", "fixedCanon is invalid.");
  assert(input.maxSelectedActions === MAX_SELECTED_ACTIONS, 400, "SCENE_CONTEXT_INVALID", "maxSelectedActions is invalid.");
  return { ...input, area: input.area.trim() };
}

export function validateScenePlan(input, contextInput) {
  const context = contextInput?.schemaVersion ? validateSceneDirectorContext(contextInput) : contextInput;
  assert(input && typeof input === "object" && !Array.isArray(input), 502, "SCENE_PLAN_INVALID", "Scene plan output must be an object.");
  exactKeys(input, ["sceneGoal", "selectedActionIds", "proposedActions", "dialogue"], "SCENE_PLAN_INVALID", 502);
  const sceneGoal = boundedString(input.sceneGoal, "sceneGoal", 1, 180, 502);
  assert(/[가-힣]/u.test(sceneGoal), 502, "SCENE_PLAN_LANGUAGE_INVALID", "sceneGoal must be written in Korean.");
  assert(Array.isArray(input.selectedActionIds) && input.selectedActionIds.length <= MAX_SELECTED_ACTIONS, 502, "SCENE_PLAN_INVALID", "selectedActionIds must contain at most four IDs.");
  const allowedIds = new Set(context.candidates.map((candidate) => candidate.candidateId));
  const selectedActionIds = input.selectedActionIds.map((id) => {
    assert(typeof id === "string" && allowedIds.has(id), 502, "SCENE_PLAN_CANDIDATE_FORBIDDEN", "The scene plan referenced an unavailable candidate.");
    return id;
  });
  assert(new Set(selectedActionIds).size === selectedActionIds.length, 502, "SCENE_PLAN_INVALID", "selectedActionIds must be unique.");
  const actorIds = new Set(context.actors.map((actor) => actor.id));
  const proposedActions = (input.proposedActions || []).map((action, index) => {
    assert(action && typeof action === "object" && !Array.isArray(action), 502, "SCENE_PLAN_INVALID", `proposedActions[${index}] must be an object.`);
    exactKeys(action, ["type", "actorId", "targetId", "actionStyle", "text", "displayName"], "SCENE_PLAN_INVALID", 502);
    const type = boundedString(action.type, `proposedActions[${index}].type`, 1, 32, 502).toUpperCase();
    assert(FREEFORM_SCENE_ACTION_TYPES.includes(type), 502, "SCENE_PLAN_ACTION_FORBIDDEN", "The proposed action type is not safely translatable.");
    const actorId = action.actorId ?? null;
    const targetId = action.targetId ?? null;
    assert(actorId === null || actorIds.has(actorId), 502, "SCENE_PLAN_ENTITY_FORBIDDEN", "A proposed actor is outside the nearby scene.");
    assert(targetId === null || actorIds.has(targetId), 502, "SCENE_PLAN_ENTITY_FORBIDDEN", "A proposed target is outside the nearby scene.");
    if (type !== "NARRATIVE_EVENT") assert(actorId !== null, 502, "SCENE_PLAN_ENTITY_REQUIRED", "This proposed action requires a nearby actor.");
    if (["MOVE_ACTOR", "ATTACK_ENTITY", "DEFEND_ENTITY", "ASSIST_ENTITY", "FLEE_ENTITY"].includes(type)) assert(targetId !== null && targetId !== actorId, 502, "SCENE_PLAN_TARGET_REQUIRED", "This proposed action requires another nearby target.");
    const text = boundedString(action.text, `proposedActions[${index}].text`, 1, 240, 502);
    assert(/[가-힣]/u.test(text), 502, "SCENE_PLAN_LANGUAGE_INVALID", "Proposed action text must be written in Korean.");
    assert(!PROTECTED_PROPOSAL_PATTERN.test(text), 502, "SCENE_PLAN_PROTECTED_RESULT", "A free proposal cannot assert protected progression, ending, resurrection, coordinates, numeric damage, or rewards.");
    const displayName = action.displayName == null ? null : boundedString(action.displayName, `proposedActions[${index}].displayName`, 1, 40, 502);
    return { type, actorId, targetId, actionStyle: action.actionStyle?.slice(0, 40) || null, text, displayName };
  });
  assert(selectedActionIds.length + proposedActions.length >= 1 && selectedActionIds.length + proposedActions.length <= MAX_SELECTED_ACTIONS, 502, "SCENE_PLAN_INVALID", "A scene must contain 1-4 recommended or freely proposed actions.");
  assert(Array.isArray(input.dialogue) && input.dialogue.length <= 3, 502, "SCENE_PLAN_INVALID", "dialogue must contain at most three lines.");
  const allowedSpeakers = new Set(context.actors.filter((actor) => ["npc", "enemy"].includes(actor.kind) && !actor.disabled).map((actor) => actor.id));
  const dialogue = input.dialogue.map((line) => {
    assert(line && typeof line === "object" && !Array.isArray(line), 502, "SCENE_PLAN_INVALID", "Dialogue lines must be objects.");
    exactKeys(line, ["speakerId", "line"], "SCENE_PLAN_INVALID", 502);
    assert(typeof line.speakerId === "string" && allowedSpeakers.has(line.speakerId), 502, "SCENE_PLAN_ENTITY_FORBIDDEN", "Dialogue speaker is outside the scene.");
    const dialogueLine = boundedString(line.line, "dialogue.line", 1, 220, 502);
    assert(/[가-힣]/u.test(dialogueLine), 502, "SCENE_PLAN_LANGUAGE_INVALID", "dialogue.line must be written in Korean.");
    return { speakerId: line.speakerId, line: dialogueLine };
  });
  return { sceneGoal, selectedActionIds, proposedActions, dialogue };
}

export function createFallbackScenePlan(contextInput) {
  const context = contextInput?.schemaVersion ? validateSceneDirectorContext(contextInput) : contextInput;
  const meaningful = context.candidates
    .filter((candidate) => candidate.type !== "NO_EVENT")
    .sort((left, right) => right.priority - left.priority || left.candidateId.localeCompare(right.candidateId));
  const selected = [];
  let spent = 0;
  const acted = new Set();
  for (const candidate of meaningful) {
    if (selected.length >= 3 || spent + candidate.cost > 4) continue;
    if (candidate.actorId && acted.has(candidate.actorId) && candidate.type !== "START_DIALOGUE") continue;
    selected.push(candidate.candidateId);
    spent += candidate.cost;
    if (candidate.actorId) acted.add(candidate.actorId);
  }
  if (selected.length === 0) selected.push(context.candidates.find((candidate) => candidate.type === "NO_EVENT")?.candidateId || context.candidates[0].candidateId);
  const dialogueCandidate = context.candidates.find((candidate) => selected.includes(candidate.candidateId) && candidate.type === "START_DIALOGUE" && candidate.actorId);
  return {
    sceneGoal: selected.length === 1 && context.candidates.find((candidate) => candidate.candidateId === selected[0])?.type === "NO_EVENT"
      ? "세계가 다음 선택을 기다리며 조용히 반응한다."
      : "주변 인물과 오류 개체가 플레이어의 선택에 즉시 반응한다.",
    selectedActionIds: selected,
    proposedActions: [],
    dialogue: dialogueCandidate ? [{ speakerId: dialogueCandidate.actorId, line: "방금 선택은 여기서 끝나지 않을 거예요. 다음 흔적을 함께 확인해요." }] : [],
    fallbackUsed: true,
    model: "deterministic-scene-director-v1"
  };
}

const DIALOGUE_SCHEMA = {
  type: "object",
  additionalProperties: false,
  required: ["speakerId", "line"],
  properties: { speakerId: { type: "string" }, line: { type: "string" } }
};

const PROPOSED_ACTION_SCHEMA = {
  type: "object",
  additionalProperties: false,
  required: ["type", "actorId", "targetId", "actionStyle", "text", "displayName"],
  properties: {
    type: { type: "string", enum: FREEFORM_SCENE_ACTION_TYPES },
    actorId: { anyOf: [{ type: "string" }, { type: "null" }] },
    targetId: { anyOf: [{ type: "string" }, { type: "null" }] },
    actionStyle: { anyOf: [{ type: "string" }, { type: "null" }] },
    text: { type: "string" },
    displayName: { anyOf: [{ type: "string" }, { type: "null" }] }
  }
};

export const SCENE_PLAN_RESPONSE_JSON_SCHEMA = Object.freeze({
  type: "object",
  additionalProperties: false,
  required: ["sceneGoal", "selectedActionIds", "proposedActions", "dialogue"],
  properties: {
    sceneGoal: { type: "string" },
    selectedActionIds: { type: "array", maxItems: MAX_SELECTED_ACTIONS, items: { type: "string" } },
    proposedActions: { type: "array", maxItems: MAX_SELECTED_ACTIONS, items: PROPOSED_ACTION_SCHEMA },
    dialogue: { type: "array", maxItems: 3, items: DIALOGUE_SCHEMA }
  }
});
