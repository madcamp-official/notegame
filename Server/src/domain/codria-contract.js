import { assert } from "../errors.js";
import { clone } from "./serialization.js";

export const GAME_TITLE = "NUPJUK : The Last Commit";
export const WORLD_CODRIA = "WORLD_CODRIA";
export const WORLD_NAME_KO = "코드리아";
export const PROTAGONIST_NUPJUKYI = "PROTAGONIST_NUPJUKYI";
export const PROTAGONIST_NAME_KO = "넙죽이";
export const ARTIFACT_ADMIN_KEYBOARD = "ARTIFACT_ADMIN_KEYBOARD";
export const ADMIN_KEYBOARD = ARTIFACT_ADMIN_KEYBOARD;
export const ADMIN_KEYBOARD_NAME_KO = "관리자 키보드";
export const ROOT_SYSTEM = "REGION_ROOT_SYSTEM";

export const CAMPAIGN_REGION_AXES = Object.freeze([
  "REGION_BUG_FOREST",
  "REGION_BUFFER_VILLAGE",
  "REGION_DEADLOCK_CITY",
  "REGION_DATA_GRAND_LIBRARY",
  "REGION_LEGACY_CITADEL",
  ROOT_SYSTEM
]);

export const ADMIN_ACCESS_LEVELS = Object.freeze([
  Object.freeze({ id: "ADMIN_ACCESS_LEVEL_1", level: 1, nameKo: "관리자 권한 I" }),
  Object.freeze({ id: "ADMIN_ACCESS_LEVEL_2", level: 2, nameKo: "관리자 권한 II" }),
  Object.freeze({ id: "ADMIN_ACCESS_LEVEL_3", level: 3, nameKo: "관리자 권한 III" })
]);

export const INPUT_TYPES = Object.freeze(["MOVE", "USE_SKILL", "NARRATIVE_CHOICE"]);
export const KEYBOARD_SKILLS = Object.freeze(["COPY", "DELETE", "CONNECT", "RESTORE", "UNDO", "SEARCH", "SELECT_ALL"]);
export const CAMPAIGN_ACTION_CONTEXTS = Object.freeze([
  "COMBAT",
  "INVESTIGATION",
  "NEGOTIATION",
  "DEPLOYMENT"
]);

export const PRODUCT_CONTRACT = Object.freeze({
  contractVersion: "codria-product.v4",
  gameTitle: GAME_TITLE,
  world: Object.freeze({ id: WORLD_CODRIA, nameKo: WORLD_NAME_KO }),
  protagonist: Object.freeze({ id: PROTAGONIST_NUPJUKYI, nameKo: PROTAGONIST_NAME_KO }),
  artifact: Object.freeze({ id: ARTIFACT_ADMIN_KEYBOARD, nameKo: ADMIN_KEYBOARD_NAME_KO }),
  adminAccessLevels: ADMIN_ACCESS_LEVELS,
  regionAxes: CAMPAIGN_REGION_AXES,
  rootSystemId: ROOT_SYSTEM,
  inputTypes: INPUT_TYPES,
  skills: KEYBOARD_SKILLS,
  consumingActionContexts: CAMPAIGN_ACTION_CONTEXTS
});

export function adminAccessLevel(levelOrId) {
  return ADMIN_ACCESS_LEVELS.find((item) => item.level === levelOrId || item.id === levelOrId) || null;
}

/**
 * Return the administrator-access candidate that this entity may resolve now.
 *
 * The old campaign arc scheduler used `currentArcQuestion.order >= 2` as an
 * indirect unlock. Emergent campaigns deliberately retire those questions as
 * `legacy_disabled`, however, and therefore expose `currentArcQuestion = null`
 * for the rest of the run. Basing a mechanical permission on that presentation
 * object made every keyboard-targeted access anchor disappear forever.
 *
 * Access legality is fully described by authoritative state instead: the
 * entity must still be active, bind the requested keyboard skill, agree with
 * its sealed candidate record, and represent the next missing access level.
 */
export function eligibleAdminAccessCandidate(run, entity, skillId) {
  if (!run || !entity || entity.active === false || !entity.state?.adminAccessLevelId) return null;
  const normalizedSkillId = String(skillId || "").trim().toUpperCase();
  const candidate = (run.adminAccessCandidates || []).find((item) => item.id === entity.state.candidateId);
  if (!candidate || candidate.skillId !== normalizedSkillId
    || candidate.accessLevelId !== entity.state.adminAccessLevelId) return null;

  const acquired = new Set((run.adminAccessAcquisitionHistory || []).map((item) => item.accessLevelId));
  if (acquired.has(candidate.accessLevelId)) return null;
  const nextLevel = ADMIN_ACCESS_LEVELS.find((item) => !acquired.has(item.id));
  return nextLevel?.id === candidate.accessLevelId ? candidate : null;
}

export function rootSystemGate(run) {
  const acquired = new Set((run.adminAccessAcquisitionHistory || []).map((item) => item.accessLevelId));
  const missingAdminAccessLevels = ADMIN_ACCESS_LEVELS.map((item) => item.id).filter((id) => !acquired.has(id));
  const requiredClueEstablished = (run.canonicalFacts || []).some((fact) =>
    fact.subject === "collapse_origin"
    && fact.predicate === "inside_admin_control_system"
    && fact.value === true);
  return {
    eligible: missingAdminAccessLevels.length === 0 && requiredClueEstablished,
    missingAdminAccessLevels,
    requiredClueEstablished
  };
}

export function validateAdminAccessCandidates(candidates) {
  assert(Array.isArray(candidates), 500, "ADMIN_ACCESS_CANDIDATES_INVALID", "Admin access candidates must be an array.");
  for (const access of ADMIN_ACCESS_LEVELS) {
    const choices = candidates.filter((candidate) => candidate.accessLevelId === access.id);
    assert(choices.length >= 2, 500, "ADMIN_ACCESS_CANDIDATES_INCOMPLETE", `${access.id} requires at least two acquisition candidates.`);
    assert(new Set(choices.map((candidate) => candidate.areaId)).size >= 2, 500, "ADMIN_ACCESS_CANDIDATE_REGIONS_INCOMPLETE", `${access.id} candidates must span multiple areas.`);
    assert(new Set(choices.map((candidate) => candidate.actionContext)).size >= 2, 500, "ADMIN_ACCESS_CANDIDATE_CONTEXTS_INCOMPLETE", `${access.id} candidates must span multiple action contexts.`);
    for (const candidate of choices) {
      assert(CAMPAIGN_REGION_AXES.includes(candidate.regionAxis) && candidate.regionAxis !== ROOT_SYSTEM, 500, "ADMIN_ACCESS_CANDIDATE_AXIS_INVALID", "Admin access candidates must bind a pre-root region axis.");
      assert(CAMPAIGN_ACTION_CONTEXTS.includes(candidate.actionContext), 500, "ADMIN_ACCESS_CANDIDATE_CONTEXT_INVALID", "Admin access candidates require a consuming action context.");
      assert(KEYBOARD_SKILLS.includes(candidate.skillId), 500, "ADMIN_ACCESS_CANDIDATE_SKILL_INVALID", "Admin access candidates require an administrator keyboard skill.");
    }
  }
  return clone(candidates);
}

export function technicalDebtDelta({ skillId, successful, forcedOverride = false, resolvesDebtEntryId = null }) {
  if (!successful) return 0;
  const base = {
    COPY: 1,
    DELETE: 2,
    CONNECT: 1,
    RESTORE: resolvesDebtEntryId ? -2 : 0,
    UNDO: resolvesDebtEntryId ? -1 : 1,
    SEARCH: 0,
    SELECT_ALL: 1
  }[skillId] ?? 0;
  return base + (forcedOverride ? 2 : 0);
}

export function endingFactors(run, finalPlacement = null) {
  return {
    worldStability: run.metrics?.worldStability ?? 0,
    worldAutonomy: run.metrics?.worldAutonomy ?? 0,
    publicTrust: run.metrics?.publicTrust ?? 0,
    technicalDebt: run.metrics?.technicalDebt ?? 0,
    companionBond: run.metrics?.companionBond ?? 0,
    adminAccessAcquisitionHistory: clone(run.adminAccessAcquisitionHistory || []),
    regionOutcomes: clone(run.regionOutcomes || []),
    majorChoices: clone(run.majorChoices || []),
    abilityUsageHistory: clone(run.abilityUsageHistory || []),
    technicalDebtEntries: clone(run.technicalDebtEntries || []),
    finalPlacement: clone(finalPlacement || run.finalPlacement || null)
  };
}
