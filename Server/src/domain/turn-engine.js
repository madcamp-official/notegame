import { createHash, randomUUID } from "node:crypto";
import { assert, AppError } from "../errors.js";
import { clone, deterministicUuid, fingerprint } from "./serialization.js";
import { advanceStoryDirector, campaignAct, chooseEnding, macroPhaseForBeat, resolveFinale } from "./campaign.js";
import { TILE, areaAt, isWalkable, movementCost, publicWorld, tileAt } from "./world.js";
import { analyzeIntent, detectIntentActions, realizationAlignment } from "./intent.js";
import { containsProtectedFactReference } from "./protected-mechanics.js";
import { applyScenePlan } from "./consequence-resolver.js";
import { CORE_NPC_CATALOG, monsterForAsset } from "./content-catalog.js";
import {
  ADMIN_ACCESS_LEVELS,
  ARTIFACT_ADMIN_KEYBOARD,
  CAMPAIGN_ACTION_CONTEXTS,
  GAME_TITLE,
  KEYBOARD_SKILLS,
  PROTAGONIST_NAME_KO,
  PROTAGONIST_NUPJUKYI,
  ROOT_SYSTEM,
  WORLD_CODRIA,
  WORLD_NAME_KO,
  rootSystemGate,
  technicalDebtDelta
} from "./codria-contract.js";

export const CORE_ABILITIES = Object.freeze(KEYBOARD_SKILLS.map((item) => item.toLowerCase()));
const ACTION_SKILLS = Object.freeze([...KEYBOARD_SKILLS, "INTERACT"]);
export const CONTEXT_ACTIONS = CAMPAIGN_ACTION_CONTEXTS;
export const ABILITIES = CORE_ABILITIES;
export const RUN_STATUSES = Object.freeze(["active", "abandoned", "completed"]);
export const OUTCOMES = Object.freeze(["critical_failure", "failure", "partial_success", "success", "critical_success"]);
export const DIRECTOR_OPS = Object.freeze(["SET_WORLD_FACT", "ADD_RUMOR", "ADD_NPC_MEMORY", "CHANGE_AFFINITY", "CREATE_HOOK", "START_QUEST", "ADVANCE_QUEST", "SET_VISUAL_INTENT", "BIND_SLOT_ENTITY"]);

const UUID_PATTERN = /^[0-9a-f]{8}-[0-9a-f]{4}-[1-8][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i;
const IDEMPOTENCY_PATTERN = /^[A-Za-z0-9][A-Za-z0-9_.:-]{7,127}$/;
const DIRECTIONS = Object.freeze([[1, 0], [-1, 0], [0, 1], [0, -1]]);
const SUCCESS_OUTCOMES = new Set(["partial_success", "success", "critical_success"]);

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
  exactKeys(input, ["inputType", "idempotencyKey", "expectedRunVersion", "skillId", "targetIds", "destination", "playerNote", "forcedOverride", "resolvesDebtEntryId"], "TURN_REQUEST_INVALID");
  assert(typeof input.idempotencyKey === "string" && IDEMPOTENCY_PATTERN.test(input.idempotencyKey), 400, "IDEMPOTENCY_KEY_INVALID", "idempotencyKey must be 8-128 safe characters.");
  assert(Number.isSafeInteger(input.expectedRunVersion) && input.expectedRunVersion >= 1, 400, "RUN_VERSION_INVALID", "expectedRunVersion must be a positive integer.");

  const normalizeEntityId = (value, name) => {
    if (value === undefined || value === null) return null;
    assert(typeof value === "string" && UUID_PATTERN.test(value), 400, "TARGET_INVALID", `${name} must be a UUID.`);
    return value.toLowerCase();
  };
  assert(String(input.inputType || "").toUpperCase() === "USE_SKILL", 400, "INPUT_TYPE_INVALID", "Campaign actions require inputType USE_SKILL.");
  assert(Array.isArray(input.targetIds) && input.targetIds.length <= 2, 400, "TARGET_INVALID", "targetIds must contain at most two entity UUIDs.");
  const targetIds = input.targetIds.map((value, index) => normalizeEntityId(value, `targetIds[${index}]`));
  const targetEntityId = targetIds[0] || null;
  const secondaryTargetEntityId = targetIds[1] || null;
  const destination = input.destination === undefined || input.destination === null ? null : point(input.destination, "destination");
  const skillId = String(input.skillId || "").toUpperCase();
  assert(ACTION_SKILLS.includes(skillId), 400, "SKILL_INVALID", `skillId must be one of: ${ACTION_SKILLS.join(", ")}.`);
  const expectedTargets = skillId === "CONNECT" ? 2 : ["UNDO", "SEARCH", "SELECT_ALL"].includes(skillId) ? 0 : 1;
  assert([targetEntityId, secondaryTargetEntityId].filter(Boolean).length === expectedTargets, 400, "TARGET_INVALID", `${skillId} requires exactly ${expectedTargets} selected target(s).`);
  const playerNote = input.playerNote ?? null;
  assert(playerNote === null || (typeof playerNote === "string" && playerNote.trim().length <= 400), 400, "PLAYER_NOTE_INVALID", "playerNote must contain at most 400 characters.");
  assert(input.forcedOverride === undefined || typeof input.forcedOverride === "boolean", 400, "FORCED_OVERRIDE_INVALID", "forcedOverride must be boolean.");
  const resolvesDebtEntryId = input.resolvesDebtEntryId === undefined || input.resolvesDebtEntryId === null ? null : normalizeEntityId(input.resolvesDebtEntryId, "resolvesDebtEntryId");
  const ability = skillId.toLowerCase();
  const targetSummary = [targetEntityId, secondaryTargetEntityId].filter(Boolean).join(" and ") || "the last reversible operation";
  return {
    inputType: "USE_SKILL",
    idempotencyKey: input.idempotencyKey,
    expectedRunVersion: input.expectedRunVersion,
    skillId,
    ability,
    targetEntityId,
    secondaryTargetEntityId,
    destination,
    intent: `Use ${skillId} on ${targetSummary}`,
    playerNote: playerNote?.trim() || null,
    abilitySource: "structured_selection",
    forcedOverride: input.forcedOverride === true,
    resolvesDebtEntryId
  };
}

export function turnFingerprint(request) {
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
    resolvesDebtEntryId: request.resolvesDebtEntryId || null
  });
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
    entities.push(entity(deterministicUuid(`${runId}:npc:${role.id}`), "npc", npcAssetId, coreNpc.name || role.name || role.displayName || seededName(campaign.worldSeed, index), slot, false, true, false, {
      hp: 8, maxHp: 8, npcRole: role.role, slotId: slot.id, campaignRole: role.campaignRole,
      evidenceKey: role.evidenceKey, designatedCampaignEvidence: true, canonicalNpcId: coreNpc.id,
      roleTags: [...coreNpc.roleTags], factionId: coreNpc.factionId, goal: coreNpc.goal,
      motivation: coreNpc.motivation, traits: coreNpc.roleTags.includes("COMBATANT") ? ["GUARDIAN"] : []
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
  const enemyNames = ["안개 슬라임", "가시 그림자", "황혼 포식자", "메아리 짐승", "잿빛 도깨비"];
  const firstEnemyIndex = Math.abs(Number(campaign.worldSeed || 0)) % enemySlot.allowedAssetIds.length;
  const firstEnemyAssetId = enemySlot.allowedAssetIds[firstEnemyIndex];
  const firstMonster = monsterForAsset(firstEnemyAssetId);
  entities.push(entity(deterministicUuid(`${runId}:enemy:first`), "enemy", firstEnemyAssetId, firstMonster?.name || enemyNames[Math.abs(Number(campaign.worldSeed || 0)) % enemyNames.length], enemySlot, true, false, false, {
    hp: firstMonster?.hp || 5, maxHp: firstMonster?.hp || 5, speed: firstMonster?.speed || 2,
    slotId: enemySlot.id, traits: [...(firstMonster?.traits || [])]
  }));
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
    const candidateAssetIndex = Math.abs(Number(campaign.worldSeed || 0) + candidate.id.length) % slot.allowedAssetIds.length;
    const candidateAssetId = slot.allowedAssetIds[candidateAssetIndex];
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
  const occupiedBlockingTiles = new Set();
  for (const item of entities.filter((candidate) => candidate.active && candidate.blocking)) {
    const positionKey = key(item.position);
    assert(!occupiedBlockingTiles.has(positionKey), 500, "INITIAL_ENTITY_OVERLAP", "Generated blocking entities must occupy unique tiles.");
    assert(isWalkable(world, item.position), 500, "INITIAL_ENTITY_UNREACHABLE", "Generated blocking entities must start on walkable tiles.");
    occupiedBlockingTiles.add(positionKey);
  }

  const npcRelationships = entities.filter((item) => item.kind === "npc").map((npc) => ({ npcId: npc.id, affinity: 0, trust: 0, fear: 0, stance: "neutral", lastChangedTurn: 0 }));
  const firstBeat = campaign.requiredStoryBeats[0];
  const canonicalFacts = campaign.canonicalFactTemplates.map((fact) => ({ ...clone(fact), establishedTurn: 0 }));
  canonicalFacts.push({ id: deterministicUuid(`${runId}:layout-fact`), subject: "world", predicate: "layout_hash", value: world.layoutHash, type: "canonical", establishedTurn: 0 });
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
    currentAct: firstBeat.phaseId,
    campaignPhase: firstBeat.phaseId,
    currentMacroPhase: clone(macroPhaseForBeat(firstBeat)),
    campaignMacroPhases: clone(campaign.campaignMacroPhases || []),
    currentStoryBeat: { ...clone(firstBeat), act: firstBeat.phaseId },
    requiredStoryBeats: clone(campaign.requiredStoryBeats),
    endingWindow: clone(campaign.endingWindow || { normalEligibleStart: Math.max(30, Math.min(38, campaign.turnLimit - 2)), preferredEnd: Math.min(42, campaign.turnLimit), hardLimit: campaign.turnLimit }),
    endingCandidates: clone(campaign.endingCandidates),
    forbiddenEvents: clone(campaign.forbiddenEvents || []),
    focus: 10,
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
    visitedPoiIds: ["entry"],
    discoveredAreaIds: [entry.areaId],
    activeEncounter: null,
    encounterHistory: [],
    world,
    playerEntityId: playerId,
    entities,
    connections: [],
    slotEnrichments: [],
    canonicalFacts,
    rumors: clone(campaign.initialRumors || []),
    openLoops: [{ id: deterministicUuid(`${runId}:opening-loop`), summary: firstBeat.title, status: "open", createdTurn: 0, expiresTurn: Math.min(campaign.turnLimit, 8), source: "campaign_director" }],
    unresolvedHooks: [{ id: deterministicUuid(`${runId}:opening-loop`), summary: firstBeat.title, status: "open", createdTurn: 0, expiresTurn: Math.min(campaign.turnLimit, 8), source: "campaign_director" }],
    majorChoices: [],
    regionOutcomes: [],
    abilityUsageHistory: [],
    technicalDebtEntries: [],
    finalPlacement: null,
    npcMemories: [],
    npcRelationships,
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
      generatedMonsterVariants: entities.filter((item) => item.kind === "enemy").map((item) => ({
        entityId: item.id, assetId: item.assetId, name: item.name, traits: [...(item.state?.traits || [])]
      }))
    },
    activeQuests: [
      { id: deterministicUuid(`${runId}:main-quest`), key: `MAIN.${fingerprint(campaign.templateId || campaign.generatedTitle).slice(0, 12)}`, title: campaign.generatedTitle || campaign.title, summary: campaign.premise, status: "active", questKind: "main", currentStep: firstBeat.id, acceptsNewSteps: true, createdTurn: 0 },
      ...(campaign.questSeeds || []).slice(0, 2).map((quest, index) => ({ id: deterministicUuid(`${runId}:seed-quest:${quest.id || index}`), key: `SEED.${quest.id || index}`, title: quest.title, summary: quest.summary || quest.description, status: "active", questKind: "seeded", currentStep: quest.initialStep || "discover", acceptsNewSteps: true, createdTurn: 0 }))
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

function samePoint(left, right) { return left.x === right.x && left.y === right.y; }
function manhattan(left, right) { return Math.abs(left.x - right.x) + Math.abs(left.y - right.y); }
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
  const runPlayer = entityById(run, run.playerEntityId);
  const from = clone(runPlayer.position);
  const pathDestination = path.path.at(-1);
  const actualDestination = { x: pathDestination.x, y: pathDestination.y };
  runPlayer.position = actualDestination;
  run.version += 1;
  run.navigationSequence = (run.navigationSequence || 0) + 1;
  run.safeTravelCount = (run.safeTravelCount || 0) + 1;
  run.travelTimeUnits = (run.travelTimeUnits || 0) + path.cost;
  run.travelTime = run.travelTimeUnits;
  run.travelDistance = (run.travelDistance || 0) + Math.max(0, path.path.length - 1);
  const traversedAreaIds = [...new Set(path.path.map((position) => areaAt(run.world, position).id))];
  for (const areaId of traversedAreaIds) if (!run.discoveredAreaIds.includes(areaId)) run.discoveredAreaIds.push(areaId);
  const reachedPois = run.world.points.filter((item) => path.path.some((position) => manhattan(item, position) <= 2)).map((item) => item.id);
  for (const poiId of reachedPois) if (!run.visitedPoiIds.includes(poiId)) run.visitedPoiIds.push(poiId);
  if (encounter) {
    run.activeEncounter = {
      id: deterministicUuid(`${run.id}:encounter:${run.navigationSequence}:${request.idempotencyKey}`),
      status: "active",
      ...clone(encounter),
      openedNavigationSequence: run.navigationSequence,
      openedAt: now,
      campaignTurnOpened: run.currentTurn,
      suggestedActionContexts: [...CAMPAIGN_ACTION_CONTEXTS],
      suggestedSkillIds: [...KEYBOARD_SKILLS]
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
    pathCost: path.cost,
    enteredAreaId: actualArea.id,
    enteredBiomeId: actualArea.biomeId,
    campaignRole: actualArea.campaignRole || null,
    traversedAreaIds,
    reachedPoiIds: reachedPois,
    travelTimeUnits: path.cost,
    cumulativeTravelTimeUnits: run.travelTimeUnits,
    encounterOpened: Boolean(encounter),
    encounter: run.activeEncounter ? clone(run.activeEncounter) : null,
    campaignTurnConsumed: false,
    campaignTurnBefore: originalRun.currentTurn,
    campaignTurnAfter: run.currentTurn,
    layoutHash: run.world.layoutHash,
    createdAt: now
  };
  return { run, navigation };
}

function prepare(run, request) {
  const player = entityById(run, run.playerEntityId);
  assert(player, 409, "PLAYER_MISSING", "The authoritative player entity is missing.");
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
  if (request.ability === "undo") {
    assert(!request.targetEntityId && !request.secondaryTargetEntityId && !request.destination, 422, "TARGET_FORBIDDEN", "Undo only compensates the immediately preceding reversible turn.");
    const specialCost = specialSkillPreparation(run, "UNDO", 3);
    assertSpecialSkillCost(run, specialCost, "UNDO");
    const reversible = [...run.reversibleLedger].reverse().find((item) => item.turnNo === run.currentTurn && item.reversible && !item.consumed);
    assert(reversible, 422, "UNDO_NOT_AVAILABLE", "The immediately preceding turn has no reversible mechanical result.");
    return { difficulty: 15, modifier: 2 + specialCost.modifierBonus, ...specialCost, normalizedAttempt: `Compensate the reversible mechanical effects of turn ${reversible.turnNo}`, reversible };
  }
  if (["search", "select_all"].includes(request.ability)) {
    assert(!request.targetEntityId && !request.secondaryTargetEntityId && !request.destination, 422, "TARGET_FORBIDDEN", `${request.skillId} does not accept a target or destination.`);
    const focusCost = request.ability === "select_all" ? 3 : 1;
    assert(run.focus >= focusCost, 422, "INSUFFICIENT_FOCUS", `${request.skillId} requires ${focusCost} focus.`);
    const radius = request.ability === "select_all" ? 4 : 6;
    return { difficulty: request.ability === "select_all" ? 14 : 9, modifier: request.ability === "select_all" ? 4 : 5, focusCost, radius, normalizedAttempt: `${request.skillId} scans the authoritative area within ${radius} tiles` };
  }
  const target = request.targetEntityId ? entityAnyById(run, request.targetEntityId) : null;
  assert(target, 422, "ENTITY_NOT_FOUND", "A valid target entity is required.");
  if (target.state?.adminAccessLevelId) {
    const candidate = (run.adminAccessCandidates || []).find((item) => item.id === target.state.candidateId);
    assert(candidate && target.active, 422, "ADMIN_ACCESS_CANDIDATE_INVALID", "The selected administrator access candidate is unavailable.");
    assert(candidate.skillId === request.skillId, 422, "ADMIN_ACCESS_SKILL_MISMATCH", `This candidate requires ${candidate.skillId}.`);
    assert(!(run.adminAccessAcquisitionHistory || []).some((item) => item.accessLevelId === candidate.accessLevelId), 422, "ADMIN_ACCESS_ALREADY_ACQUIRED", `${candidate.accessLevelId} has already been acquired through another path.`);
    const accessLevel = ADMIN_ACCESS_LEVELS.find((item) => item.id === candidate.accessLevelId);
    assert(accessLevel.level === (run.adminAccessAcquisitionHistory || []).length + 1, 422, "ADMIN_ACCESS_ORDER_INVALID", `Acquire ${ADMIN_ACCESS_LEVELS[(run.adminAccessAcquisitionHistory || []).length].id} first.`);
    assert(run.currentStoryBeat?.requiredEvidenceKey === candidate.accessLevelId, 422, "ADMIN_ACCESS_STAGE_INVALID", `${candidate.accessLevelId} can only be acquired during its active campaign beat.`);
    assert(manhattan(player.position, target.position) <= 3, 422, "OUT_OF_RANGE", "Administrator access candidates must be within 3 tiles.");
    let secondary = null;
    if (request.skillId === "CONNECT") {
      secondary = entityById(run, request.secondaryTargetEntityId);
      assert(secondary && secondary.id !== target.id && manhattan(player.position, secondary.position) <= 5, 422, "SECONDARY_TARGET_REQUIRED", "CONNECT requires a distinct nearby second target.");
    }
    const specialCost = specialSkillPreparation(run, request.skillId, { COPY: 1, DELETE: 1, CONNECT: 2, RESTORE: 3, UNDO: 3 }[request.skillId]);
    assertSpecialSkillCost(run, specialCost, request.skillId);
    return {
      difficulty: { COMBAT: 12, INVESTIGATION: 10, NEGOTIATION: 11, DEPLOYMENT: 13 }[candidate.actionContext],
      modifier: 3 + specialCost.modifierBonus,
      ...specialCost,
      target,
      secondary,
      actionContext: candidate.actionContext,
      adminAccessCandidate: candidate,
      normalizedAttempt: `${candidate.actionContext} with ${request.skillId} at ${candidate.regionAxis} for ${candidate.accessLevelId}`
    };
  }
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
  const specialCost = specialSkillPreparation(run, request.skillId, { copy: 1, delete: 1, connect: 2, restore: 3 }[request.ability]);
  assertSpecialSkillCost(run, specialCost, request.skillId);
  if (request.ability === "copy") {
    assert(!request.secondaryTargetEntityId, 422, "TARGET_INVALID", "Copy accepts one source entity.");
    assert(target.active && target.cloneable && !target.protected, 422, "ENTITY_NOT_CLONEABLE", "The selected entity cannot be copied.");
    assert(request.destination && isWalkable(run.world, request.destination), 422, "DESTINATION_INVALID", "Copy requires a walkable destination.");
    assert(!isBlockingOccupied(run, request.destination), 422, "DESTINATION_OCCUPIED", "Copy destination is occupied.");
    assert(!samePoint(target.position, request.destination), 422, "DESTINATION_INVALID", "Copy destination must differ from source.");
    assert(manhattan(player.position, target.position) <= 4 && manhattan(player.position, request.destination) <= 4, 422, "OUT_OF_RANGE", "Copy source and destination must be within 4 tiles.");
    return { difficulty: 11, modifier: 3 + specialCost.modifierBonus, ...specialCost, target, normalizedAttempt: `Copy ${target.name} into the legal tile (${request.destination.x},${request.destination.y})` };
  }
  if (request.ability === "delete") {
    assert(!request.secondaryTargetEntityId && !request.destination, 422, "TARGET_INVALID", "Delete accepts exactly one entity target.");
    assert(target.active && target.id !== run.playerEntityId && !target.protected, 422, "ENTITY_PROTECTED", "The selected entity cannot be deleted.");
    if (target.state?.finaleComponent) assert(finaleGateEligible(run) && areaAt(run.world, player.position).campaignRole === "FINAL_CONVERGENCE", 422, "FINALE_ACCESS_DENIED", "Finale component removal requires all three administrator access levels, the essential clue, and physical Root System entry.");
    assert(manhattan(player.position, target.position) <= 3, 422, "OUT_OF_RANGE", "Delete target must be within 3 tiles.");
    return { difficulty: 12, modifier: 3 + specialCost.modifierBonus, ...specialCost, target, normalizedAttempt: `Delete the removable entity ${target.name}` };
  }
  if (request.ability === "connect") {
    assert(!request.destination, 422, "DESTINATION_INVALID", "Connect joins entities and does not accept a destination tile.");
    const secondary = request.secondaryTargetEntityId ? entityById(run, request.secondaryTargetEntityId) : null;
    assert(target.active && secondary, 422, "SECONDARY_TARGET_REQUIRED", "Connect requires two active entities.");
    assert(target.id !== secondary.id, 422, "TARGETS_IDENTICAL", "Connect targets must differ.");
    assert(manhattan(player.position, target.position) <= 5 && manhattan(player.position, secondary.position) <= 5, 422, "OUT_OF_RANGE", "Both connection targets must be within 5 tiles.");
    assert(!run.connections.some((item) => item.active && ((item.fromId === target.id && item.toId === secondary.id) || (item.fromId === secondary.id && item.toId === target.id))), 422, "CONNECTION_EXISTS", "An active connection already joins those targets.");
    const finaleRelated = target.state?.finaleComponent || secondary.state?.finaleComponent;
    if (finaleRelated) {
      const bothPuzzleEntities = (target.state?.finaleComponent || target.id === run.playerEntityId) && (secondary.state?.finaleComponent || secondary.id === run.playerEntityId);
      assert(bothPuzzleEntities && finaleGateEligible(run) && areaAt(run.world, player.position).campaignRole === "FINAL_CONVERGENCE", 422, "FINALE_ACCESS_DENIED", "Finale links require puzzle entities, all three administrator access levels, the essential clue, and physical Root System entry.");
    }
    return { difficulty: 13, modifier: 2 + specialCost.modifierBonus, ...specialCost, target, secondary, normalizedAttempt: `Create a temporary allowed connection between ${target.name} and ${secondary.name}` };
  }
  assert(!request.secondaryTargetEntityId && !request.destination, 422, "TARGET_INVALID", "Restore accepts exactly one entity target.");
  const restoration = [...run.reversibleLedger].reverse().find((item) => !item.consumed && run.currentTurn - item.turnNo <= 8 && item.inverseOps.some((operation) =>
    (operation.type === "restore_entity" && operation.entity.id === target.id)
    || (operation.type === "restore_state" && operation.entityId === target.id)));
  assert(restoration && (!target.active || restoration.inverseOps.some((operation) => operation.type === "restore_state" && operation.entityId === target.id)), 422, "RESTORE_NOT_AVAILABLE", "The target has no recent reversible damage or removal snapshot.");
  const restoreEntityOperation = restoration.inverseOps.find((operation) => operation.type === "restore_entity" && operation.entity.id === target.id);
  if (restoreEntityOperation) assert(!isActiveOccupied(run, restoreEntityOperation.entity.position), 422, "RESTORE_DESTINATION_OCCUPIED", "Restore destination is occupied by an active entity.");
  return { difficulty: 14, modifier: 2 + specialCost.modifierBonus, ...specialCost, target, restoration, normalizedAttempt: `Restore permitted recent damage or removal on ${target.name} from the authoritative snapshot recorded on turn ${restoration.turnNo}` };
}

function classifyActionContext(run, request, preparation) {
  if (preparation.actionContext) return preparation.actionContext;
  const target = preparation.target || null;
  const relationship = target ? run.npcRelationships.find((item) => item.npcId === target.id) : null;
  if (run.activeEncounter?.status === "active" || target?.kind === "enemy" || relationship?.stance === "hostile") return "COMBAT";
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
      if (!["hp", "temporaryDamage", "disabled"].includes(field) || !(field in operation.stateSnapshot)) continue;
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

function applyPrimaryEffect(run, request, preparation, turnNo, events) {
  const inverseOps = [];
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
      targetIds: [request.targetEntityId, request.secondaryTargetEntityId].filter(Boolean)
    };
    run.adminAccessAcquisitionHistory.push(acquisition);
    if (!run.progressTokens.includes(candidate.accessLevelId)) run.progressTokens.push(candidate.accessLevelId);
    run.progressLevel = run.adminAccessAcquisitionHistory.length;
    preparation.target.state.adminAccessResolved = true;
    preparation.target.state.adminAccessResolvedTurn = turnNo;
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
  } else if (request.ability === "move") {
    const player = entityById(run, run.playerEntityId);
    const from = clone(player.position);
    player.position = clone(request.destination);
    events.push({ type: "entity_moved", entityId: player.id, from, to: clone(player.position), path: preparation.path });
    inverseOps.push({ type: "move_entity", entityId: player.id, to: from });
  } else if (request.ability === "copy") {
    const source = entityById(run, request.targetEntityId);
    const temporaryClone = (preparation.specialSkill?.modifierIds || []).includes("TEMPORARY_CLONE");
    const copyEntity = { ...clone(source), id: deterministicUuid(`${run.id}:${turnNo}:${request.idempotencyKey}:copy`), name: `${source.name} Copy`, position: clone(request.destination), protected: false, state: { ...clone(source.state), copiedOnTurn: turnNo, sourceEntityId: source.id, ...(temporaryClone ? { temporary: true, expiresTurn: Math.min(run.turnLimit, turnNo + 3) } : {}) } };
    run.entities.push(copyEntity);
    events.push({ type: "entity_spawned", entityId: copyEntity.id, assetId: copyEntity.assetId, position: copyEntity.position, sourceEntityId: source.id });
    inverseOps.push({ type: "remove_entity", entityId: copyEntity.id });
  } else if (request.ability === "delete") {
    const target = entityById(run, request.targetEntityId);
    inverseOps.push({ type: "restore_entity", entity: clone(target) });
    target.active = false;
    events.push({ type: "entity_removed", entityId: target.id });
  } else if (request.ability === "connect") {
    const connection = { id: deterministicUuid(`${run.id}:${turnNo}:${request.idempotencyKey}:connection`), fromId: preparation.target.id, toId: preparation.secondary.id, relation: "temporary_link", createdTurn: turnNo, expiresTurn: Math.min(run.turnLimit, turnNo + 5), active: true };
    run.connections.push(connection);
    events.push({ type: "connection_created", ...connection });
    inverseOps.push({ type: "remove_connection", connectionId: connection.id });
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
    const reversible = run.reversibleLedger.find((item) => item.turnNo === preparation.reversible.turnNo && !item.consumed);
    assert(reversible, 409, "UNDO_CONFLICT", "The previous result was already consumed.");
    let applied = 0;
    for (const operation of reversible.inverseOps) if (applyInverseOperation(run, operation, events)) applied += 1;
    assert(applied > 0, 409, "UNDO_CONFLICT", "The previous result can no longer be compensated safely.");
    reversible.consumed = true;
    events.push({ type: "reversible_reward_spent", ability: "undo", sourceTurn: reversible.turnNo, focusCost: preparation.focusCost });
  } else if (["search", "select_all"].includes(request.ability)) {
    const player = entityById(run, run.playerEntityId);
    const affected = run.entities.filter((item) => item.active && item.id !== player.id && manhattan(item.position, player.position) <= preparation.radius);
    for (const target of affected) {
      target.state = { ...(target.state || {}), revealed: true, revealedTurn: turnNo, selectedByArea: request.ability === "select_all" };
      events.push({ type: request.ability === "select_all" ? "area_target_selected" : "entity_revealed", entityId: target.id, radius: preparation.radius });
    }
    events.push({ type: request.ability === "select_all" ? "administrator_area_deployed" : "search_completed", radius: preparation.radius, affectedCount: affected.length });
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
    run.focus = Math.min(10, run.focus + preparation.focusRecovery);
    player.state.hp = Math.min(player.state.maxHp, player.state.hp + preparation.healthRecovery);
    if (run.focus !== priorFocus) events.push({ type: "resource_changed", resource: "focus", delta: run.focus - priorFocus, reason: "rest" });
    if (player.state.hp !== priorHp) events.push({ type: "health_changed", entityId: player.id, delta: player.state.hp - priorHp, hp: player.state.hp, reversible: false, reason: "rest" });
  }
  if (inverseOps.length > 0) run.reversibleLedger.push({ turnNo, ability: request.ability, reversible: true, consumed: false, inverseOps });
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
    } else if (operation.op === "BIND_SLOT_ENTITY") {
      const slot = run.world.placementSlots.find((item) => item.id === operation.slotId);
      const occupied = slot && (run.slotEnrichments.some((item) => item.slotId === slot.id)
        || run.entities.some((item) => item.active && samePoint(item.position, slot)));
      if (!slot || slot.purpose !== "ambient" || occupied) { reject("SLOT_BINDING_FORBIDDEN"); continue; }
      if (!slot.allowedAssetIds.includes(operation.assetId)) { reject("ASSET_NOT_ALLOWLISTED"); continue; }
      const minimumCost = slot.kind === "enemy" ? 2 : 1;
      if (cost < minimumCost) { reject("SLOT_BINDING_BUDGET_REQUIRED"); continue; }
      const entityId = deterministicUuid(`${run.id}:director-entity:${turnNo}:${slot.id}`);
      const kind = ["npc", "enemy", "prop"].includes(slot.kind) ? slot.kind : "prop";
      const created = entity(entityId, kind, operation.assetId, operation.summary.slice(0, 80), slot, kind === "enemy", false, false, {
        slotId: slot.id, spawnedBy: "validated_director", spawnedTurn: turnNo, geometryChanged: false
      });
      run.entities.push(created);
      if (kind === "npc") run.npcRelationships.push({ npcId: created.id, affinity: 0, trust: 0, fear: 0, stance: "neutral", lastChangedTurn: turnNo });
      run.slotEnrichments.push({ slotId: slot.id, turnNo, entityId: created.id, assetId: created.assetId, summary: created.name, geometryChanged: false });
      events.push({ type: "entity_bound_to_slot", entityId: created.id, slotId: slot.id, assetId: created.assetId, entityKind: kind, position: clone(created.position), geometryChanged: false });
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
  if (!forceConvergence && turnNo < run.turnLimit) return;
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

function updateCampaignMetrics(run, request, outcome, actionContext, turnNo, events) {
  const before = clone(run.metrics);
  const success = SUCCESS_OUTCOMES.has(outcome);
  const critical = outcome === "critical_success" || outcome === "critical_failure";
  const shift = success ? (critical ? 3 : 2) : (critical ? -4 : -2);
  run.metrics.worldStability += shift;
  run.metrics.publicTrust += success ? 1 : -2;
  // Failed checks do not silently create or erase debt. Only an applied editing
  // operation (or an explicit repair of a ledger entry) changes technical debt.
  if (request.ability === "copy") run.metrics.worldAutonomy += 2;
  if (request.ability === "delete") { run.metrics.worldStability += 2; run.metrics.worldAutonomy -= 1; }
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
      id: deterministicUuid(`${run.id}:technical-debt:${turnNo}:${request.skillId}`),
      runId: run.id,
      turnId: deterministicUuid(`${run.id}:turn:${turnNo}`),
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

export function resolveTurn({ run: originalRun, request, d20Source = new DeterministicD20Source(), forcedD20 = null, now = new Date().toISOString(), directorOutput = null, sceneDecision = null }) {
  assert(originalRun.status === "active", 409, "RUN_NOT_ACTIVE", "The run does not accept turns.");
  assert(request.inputType === "USE_SKILL" && ACTION_SKILLS.includes(request.skillId), 400, "TURN_REQUEST_INVALID", "Only structured USE_SKILL commands consume campaign turns.");
  if (request.resolvesDebtEntryId) {
    assert(["RESTORE", "UNDO"].includes(request.skillId), 422, "TECHNICAL_DEBT_RESOLUTION_INVALID", "Only RESTORE or UNDO can explicitly resolve a technical debt entry.");
    assert(originalRun.technicalDebtEntries.some((item) => item.id === request.resolvesDebtEntryId && item.resolvedAt === null), 422, "TECHNICAL_DEBT_ENTRY_INVALID", "The selected technical debt entry is not unresolved.");
  }
  const stateHashBefore = stateFingerprint(originalRun);
  const openedEncounter = originalRun.activeEncounter?.status === "active" ? clone(originalRun.activeEncounter) : null;
  if (openedEncounter) assert(request.ability !== "rest", 422, "ENCOUNTER_ACTION_REQUIRED", "An active encounter requires a nearby meaningful action; rest cannot bypass it.", { activeEncounter: openedEncounter });
  const immutableLayout = fingerprint(publicWorld(originalRun.world));
  const preparation = prepare(originalRun, request);
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
  const events = [];

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
  if (SUCCESS_OUTCOMES.has(outcome)) {
    applyPrimaryEffect(run, request, preparation, turnNo, events);
  }
  if (outcome === "partial_success") {
    run.exposed = true;
    run.pressure += 1;
    events.push({ type: "status_added", status: "exposed", durationTurns: 1 });
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

  run.currentTurn = turnNo;
  run.abilityUsageHistory.push({
    id: deterministicUuid(`${run.id}:ability-usage:${turnNo}`),
    turnNo,
    skillId: request.skillId,
    actionContext,
    targetIds: [request.targetEntityId, request.secondaryTargetEntityId].filter(Boolean),
    outcome,
    d20,
    forcedOverride: request.forcedOverride === true
  });
  if (openedEncounter) {
    const resolvedEncounter = { ...openedEncounter, status: "resolved", resolvedTurn: turnNo, resolutionAction: request.ability, resolutionOutcome: outcome, resolvedAt: now };
    run.encounterHistory = (run.encounterHistory || []).map((item) => item.id === resolvedEncounter.id ? clone(resolvedEncounter) : item);
    run.activeEncounter = null;
    events.push({ type: "encounter_resolved", encounterId: resolvedEncounter.id, action: request.ability, outcome, campaignTurnConsumed: true });
  }
  updateCampaignMetrics(run, request, outcome, actionContext, turnNo, events);
  evaluateFinalePuzzle(run, turnNo, events);
  const campaignRole = areaAt(run.world, entityById(run, run.playerEntityId).position).campaignRole;
  const currentArea = areaAt(run.world, entityById(run, run.playerEntityId).position);
  const targetEvidenceKeys = [preparation.target, preparation.secondary]
    .filter(Boolean)
    .flatMap((item) => [item.state?.evidenceKey, item.state?.finaleComponent ? "FINALE_PUZZLE_COMPONENT" : null])
    .filter(Boolean);
  if (run.finalePuzzle?.status === "resolved") targetEvidenceKeys.push("FINALE_PUZZLE_RESOLVED");
  if (currentArea.regionAxis === ROOT_SYSTEM) targetEvidenceKeys.push("ROOT_SYSTEM_ENTERED");
  advanceStoryDirector(run, turnNo, events, {
    ability: request.ability, outcome, contextualActions: [actionContext.toLowerCase()], campaignRole,
    targetEvidenceKeys,
    finalePuzzleResolved: run.finalePuzzle?.status === "resolved",
    finaleEndingId: run.finalePuzzle?.matchedEndingId || null
  });
  const directorPlan = applyDirectorOperations(run, directorOutput || { proposedOps: [] }, turnNo, budget, events);
  const sceneResolution = sceneDecision ? applyScenePlan(run, {
    candidates: sceneDecision.candidates,
    plan: sceneDecision.plan,
    decisionType: "ACTION",
    now
  }) : null;
  if (sceneResolution) events.push(...sceneResolution.events);
  const allRequiredBeatsCompleted = run.requiredStoryBeats.every((beat) => beat.status === "completed");
  const explicitFinaleReady = allRequiredBeatsCompleted
    && Boolean(run.selectedEndingId)
    && run.finalePuzzle?.status === "resolved"
    && turnNo >= (run.endingWindow?.normalEligibleStart || Math.max(30, Math.min(38, run.turnLimit - 2)));
  const forcedTurnLimitFallback = turnNo >= run.turnLimit;
  expireNarrativeState(run, turnNo, events, explicitFinaleReady || forcedTurnLimitFallback);
  run.version += 1;
  run.updatedAt = now;
  events.push({ type: "turn_committed", turnNo, runVersion: run.version });
  if (explicitFinaleReady || forcedTurnLimitFallback) {
    const ending = chooseEnding(run);
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
    events.push({ type: "run_completed", endingCode: ending.id, endingCategory: ending.category, title: ending.title, valence: ending.valence, completionMode: explicitFinaleReady ? "explicit_final_window" : "emergency_turn_limit_fallback" });
  }
  assert(fingerprint(publicWorld(run.world)) === immutableLayout, 500, "WORLD_LAYOUT_MUTATED", "A turn attempted to mutate immutable world geometry.");

  const explanation = outcomeExplanation({ request, d20, score, outcome, preparation, intentAnalysis });
  const narrative = normalizeCommittedNarrative(directorOutput, directorPlan, explanation);
  const turn = {
    id: deterministicUuid(`${run.id}:turn:${turnNo}`),
    runId: run.id,
    ownerId: run.ownerId,
    turnNo,
    idempotencyKey: request.idempotencyKey,
    requestFingerprint: turnFingerprint(request),
    expectedRunVersion: request.expectedRunVersion,
    committedRunVersion: run.version,
    request: clone(request),
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
      sceneDecision: sceneResolution ? clone(sceneResolution) : null,
      appliedOps: directorPlan.appliedOps,
      rejectedOps: directorPlan.rejectedOps
    },
    sceneDecision: sceneResolution ? clone(sceneResolution) : null,
    sceneSequence: sceneResolution ? clone(sceneResolution.sceneSequence) : [],
    narrative,
    createdAt: now
  };
  return { run, turn };
}

function outcomeExplanation({ request, d20, score, outcome, preparation, intentAnalysis }) {
  const divergence = SUCCESS_OUTCOMES.has(outcome) ? "The legal attempt was resolved within server limits." : "The world state was preserved because the check did not meet the legal difficulty.";
  const intentNote = `Semantic intent fit ${intentAnalysis.score} (${intentAnalysis.status}${intentAnalysis.issues.length > 0 ? `: ${intentAnalysis.issues.join(", ")}` : ""}); free text cannot change legality, coordinates, difficulty, or effects.`;
  return `${request.ability} rolled ${d20} + ${preparation.modifier} against difficulty ${preparation.difficulty} (score ${score}): ${outcome}. ${intentNote} ${divergence}`;
}

function normalizeCommittedNarrative(output, plan, explanation) {
  const fallback = output || {};
  const dialogue = Array.isArray(fallback.dialogue) ? fallback.dialogue : fallback.dialogue ? [{ speakerId: null, line: fallback.dialogue }] : [];
  return {
    summary: fallback.summary || "The command settles into the world.",
    body: fallback.body || explanation,
    dialogue,
    proposedOps: clone(fallback.proposedOps || []),
    appliedOps: plan.appliedOps,
    rejectedOps: plan.rejectedOps,
    fallbackUsed: fallback.fallbackUsed !== false,
    model: fallback.model || "deterministic-fallback-v2"
  };
}

export function nearestArea(run) {
  const player = entityById(run, run.playerEntityId);
  return areaAt(run.world, player.position);
}

export function directorContext(run, turn) {
  const currentArea = nearestArea(run);
  const remainingTurns = Math.max(0, run.turnLimit - turn.turnNo);
  const visibleEntities = run.entities.filter((item) => item.active && manhattan(item.position, entityById(run, run.playerEntityId).position) <= 8).map((item) => ({ id: item.id, kind: item.kind, assetId: item.assetId, name: item.name, position: item.position, role: item.state?.npcRole || null }));
  const occupiedSlotIds = new Set(run.slotEnrichments.map((item) => item.slotId));
  return {
    schemaVersion: "2.0",
    requestType: "TURN_NARRATION",
    campaign: { title: GAME_TITLE, worldId: WORLD_CODRIA, worldName: WORLD_NAME_KO, protagonistId: PROTAGONIST_NUPJUKYI, protagonistName: PROTAGONIST_NAME_KO, artifactId: ARTIFACT_ADMIN_KEYBOARD, premise: run.premise, contentHash: run.campaignContentHash },
    progression: { level: run.progressLevel, tokens: clone(run.progressTokens), tokenDefinitions: clone(run.progressTokenDefinitions), rootSystemGate: rootSystemGate(run) },
    macroPhase: clone(run.currentMacroPhase || macroPhaseForBeat(run.currentStoryBeat)),
    turnNo: turn.turnNo,
    remainingTurns,
    act: campaignAct(turn.turnNo, run.turnLimit),
    currentStoryBeat: clone(run.currentStoryBeat),
    area: currentArea.name,
    areaSummary: currentArea.summary,
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
    allowedQuestIds: run.activeQuests.filter((quest) => quest.status === "active").map((quest) => quest.id),
    allowedQuestTemplateIds: (run.questTemplates || []).filter((template) => !run.activeQuests.some((quest) => quest.templateId === template.id || quest.key === `TEMPLATE.${template.id}` || quest.key === `SEED.${template.id}`)).map((template) => template.id),
    activeQuests: run.activeQuests.filter((quest) => quest.status === "active").slice(0, 6).map((quest) => ({ id: quest.id, title: quest.title, summary: quest.summary, currentStep: quest.currentStep, questKind: quest.questKind })),
    visibleEntities,
    placementSlots: run.world.placementSlots.filter((slot) => slot.areaId === currentArea.id
      && !occupiedSlotIds.has(slot.id)
      && !run.entities.some((item) => item.active && item.position.x === slot.x && item.position.y === slot.y)).slice(0, 8),
    readOnlyPlaces: run.world.pois.map((item) => ({ id: item.id, name: item.name, areaId: item.areaId, biomeId: item.biomeId, campaignRole: item.campaignRole })).slice(0, 24),
    readOnlySlots: run.world.placementSlots.filter((slot) => slot.areaId === currentArea.id).map((slot) => ({ id: slot.id, areaId: slot.areaId, kind: slot.kind, purpose: slot.purpose, reservedFor: slot.reservedFor, tags: slot.tags })).slice(0, 12),
    geometryPolicy: "read_only_ids_and_visual_intent_only",
    canonicalFacts: run.canonicalFacts.slice(-16),
    openLoops: run.openLoops.filter((loop) => loop.status === "open").slice(-8),
    rumors: run.rumors.filter((rumor) => rumor.status === "active").slice(-6),
    npcRelationships: run.npcRelationships.filter((item) => visibleEntities.some((entityItem) => entityItem.id === item.npcId)),
    recentMemories: run.npcMemories.filter((item) => !item.expired).slice(-8),
    majorChoices: (run.majorChoices || []).slice(-8),
    regionOutcomes: (run.regionOutcomes || []).slice(-6),
    abilityUsageHistory: (run.abilityUsageHistory || []).slice(-8),
    adminAccessHistory: clone(run.adminAccessAcquisitionHistory || []),
    technicalDebtEntries: (run.technicalDebtEntries || []).filter((item) => item.resolvedAt === null).slice(-8),
    unresolvedHooks: (run.unresolvedHooks || []).filter((item) => item.status === "open").slice(-8),
    sceneSequence: (turn.sceneSequence || []).slice(0, 16).map((item) => ({
      sequence: item.sequence, type: item.type, actorId: item.actorId || null, targetId: item.targetId || null,
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
  const publicEntities = run.entities.filter((item) => item.active).map((item) => ({ id: item.id, kind: item.kind, assetId: item.assetId, name: item.name, position: item.position, state: item.state, blocking: item.blocking, protected: item.protected, cloneable: item.cloneable }));
  const player = publicEntities.find((item) => item.id === run.playerEntityId);
  const entityNames = new Map(publicEntities.map((item) => [item.id, item.name]));
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
    health: player?.state?.hp ?? 0,
    maxHealth: player?.state?.maxHp ?? 0,
    focus: run.focus,
    maxFocus: 10,
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
    visitedPoiIds: run.visitedPoiIds,
    discoveredAreaIds: run.discoveredAreaIds,
    activeEncounter: run.activeEncounter ? clone(run.activeEncounter) : null,
    encounterHistory: (run.encounterHistory || []).slice(-12).map((item) => clone(item)),
    rootSystemGate: rootSystemGate(run),
    finaleGate: { ...rootSystemGate(run), requiredProgressLevel: 3, missingProgressTokens: rootSystemGate(run).missingAdminAccessLevels },
    endingCandidates: run.endingCandidates.map((item) => item.title),
    endingCandidateDetails: run.endingCandidates,
    playerEntityId: run.playerEntityId,
    entities: publicEntities,
    connections: run.connections.filter((item) => item.active),
    world: publicWorld(run.world),
    activeQuests: run.activeQuests,
    canonicalFacts: run.canonicalFacts,
    openLoops: run.openLoops.filter((item) => item.status === "open"),
    rumors: run.rumors.filter((item) => item.status === "active"),
    npcRelationships: run.npcRelationships.map((item) => ({ ...item, npcName: entityNames.get(item.npcId) || "", score: item.affinity, label: item.stance, reason: "authoritative relationship state" })),
    npcMemories: run.npcMemories.filter((item) => !item.expired).map((item) => ({ ...item, npcName: entityNames.get(item.npcId) || "", memory: item.summary, importance: Math.round(item.importance * 100), turnNo: item.createdTurn })),
    directorState: clone(run.directorState || {}),
    majorChoices: clone(run.majorChoices || []),
    regionOutcomes: clone(run.regionOutcomes || []),
    abilityUsageHistory: clone(run.abilityUsageHistory || []),
    adminAccessLevels: clone(run.adminAccessLevels || ADMIN_ACCESS_LEVELS),
    adminAccessCandidates: clone(run.adminAccessCandidates || []),
    adminAccessAcquisitionHistory: clone(run.adminAccessAcquisitionHistory || []),
    technicalDebtEntries: clone(run.technicalDebtEntries || []),
    unresolvedHooks: clone((run.unresolvedHooks || []).filter((item) => item.status === "open")),
    restoreCandidates: restoreCandidatesForRun(run),
    generationPlan: run.generationPlan,
    campaignContentHash: run.campaignContentHash,
    abilities: CORE_ABILITIES,
    actionContexts: CONTEXT_ACTIONS,
    inputTypes: ["MOVE", "USE_SKILL"],
    createdAt: run.createdAt,
    updatedAt: run.updatedAt
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
  return {
    id: turn.id,
    runId: turn.runId,
    turnNo: turn.turnNo,
    expectedRunVersion: turn.expectedRunVersion,
    committedRunVersion: turn.committedRunVersion,
    request: turn.request,
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
    createdAt: turn.createdAt
  };
}
