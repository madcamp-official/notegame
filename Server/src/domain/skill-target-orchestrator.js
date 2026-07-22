import { capabilitiesFor } from "./entity-capabilities.js";
import { enemyArchetype } from "./enemy-archetypes.js";
import { prioritizedFinaleConnectionPairs, prioritizedFinaleRemovalTargets } from "./campaign.js";
import { eligibleAdminAccessCandidate, rootSystemGate } from "./codria-contract.js";

const LLM_TARGET_SKILLS = new Set(["SEARCH", "COPY", "DELETE", "CONNECT", "RESTORE", "UNDO", "SELECT_ALL"]);
const AMBIENT_PERSISTENCE_ASSERTION = /(?:(?:깨끗이|완전히|영구적으로).{0,16}(?:사라|삭제|소거|제거|정화|복구|회복|치유|해결)|(?:정화|복구|회복|치유|해결)(?:됐|되었|되니|했다|완료))/i;
const DISCOVERIES = Object.freeze({
  SEARCH: ["hidden_log", "data_cache", "resource_trace"],
  COPY: ["ambient_packet"],
  DELETE: ["system_residue"],
  CONNECT: ["dormant_signal"],
  RESTORE: ["configuration_fragment"],
  UNDO: ["reverted_trace"],
  SELECT_ALL: ["area_resonance"]
});

const distance = (a, b) => Math.abs(a.x - b.x) + Math.abs(a.y - b.y);

function activeCandidate(run, entity, player, radius) {
  return entity.id !== player.id && entity.active && !entity.state?.disabled && !entity.state?.defeated
    && !entity.state?.fled && distance(player.position, entity.position) <= radius;
}

function adminCandidate(run, entity, skillId) {
  return Boolean(eligibleAdminAccessCandidate(run, entity, skillId));
}

function requiredStoryRevelationCandidate(run, entity) {
  if (rootSystemGate(run).requiredClueEstablished) return false;
  const state = entity?.state;
  return state?.designatedCampaignEvidence === true
    && state?.campaignRole === "HIDDEN_TRUTH"
    && state?.evidenceKey === "STORY_REVELATION";
}

function campaignCandidatePriority(run, request, entity) {
  if (request.skillId === "SEARCH" && requiredStoryRevelationCandidate(run, entity)) return 4;
  if (adminCandidate(run, entity, request.skillId)) return 3;
  if (request.skillId === "DELETE"
    && prioritizedFinaleRemovalTargets(run, 3).some((candidate) => candidate.target.id === entity.id)) return 2;
  return 0;
}

function entityOption(index, entity, player, extra = {}) {
  return {
    candidateId: `entity_${index}`,
    kind: "entity",
    entityIds: [entity.id],
    label: entity.name,
    entityKind: entity.kind,
    distance: distance(player.position, entity.position),
    ...extra
  };
}

export function buildSkillTargetContext(run, request, d20) {
  if (!LLM_TARGET_SKILLS.has(request.skillId)) return null;
  const player = run.entities.find((item) => item.id === run.playerEntityId);
  if (!player) return null;
  let entities = [];
  if (request.skillId === "SEARCH") {
    entities = run.entities.filter((entity) => activeCandidate(run, entity, player, 6) && (
      adminCandidate(run, entity, "SEARCH")
      || requiredStoryRevelationCandidate(run, entity)
      || (entity.kind === "enemy" && !entity.state?.adminAccessLevelId
        && enemyArchetype(entity.assetId, run.worldSeed ?? run.world?.worldSeed, entity.id) === "root_process" && !entity.state?.revealed)
      || (!entity.state?.adminAccessLevelId && ["prop", "npc"].includes(entity.kind) && !entity.state?.revealed)
    ));
  } else if (request.skillId === "COPY") {
    entities = run.entities.filter((entity) => activeCandidate(run, entity, player, 4)
      && (adminCandidate(run, entity, "COPY") || (!entity.state?.adminAccessLevelId && capabilitiesFor(entity).canCopy)));
  } else if (request.skillId === "DELETE") {
    entities = run.entities.filter((entity) => activeCandidate(run, entity, player, 3)
      && (adminCandidate(run, entity, "DELETE") || (!entity.state?.adminAccessLevelId && capabilitiesFor(entity).canDelete
        && (entity.kind !== "enemy" || enemyArchetype(entity.assetId, run.worldSeed ?? run.world?.worldSeed, entity.id) !== "root_process" || entity.state?.revealed === true))));
  } else if (request.skillId === "RESTORE") {
    entities = run.entities.filter((entity) => activeCandidate(run, entity, player, 5) && (adminCandidate(run, entity, "RESTORE")
      || [...run.reversibleLedger].reverse().some((item) => !item.consumed && run.currentTurn - item.turnNo <= 8 && item.inverseOps.some((op) =>
        (op.type === "restore_entity" && op.entity.id === entity.id) || (op.type === "restore_state" && op.entityId === entity.id)))));
  }
  entities.sort((a, b) => campaignCandidatePriority(run, request, b) - campaignCandidatePriority(run, request, a)
    || distance(player.position, a.position) - distance(player.position, b.position)
    || a.id.localeCompare(b.id));
  let candidates = entities.slice(0, 8).map((entity, index) => entityOption(index, entity, player));
  if (request.skillId === "CONNECT") {
    const endpoints = run.entities.filter((entity) => activeCandidate(run, entity, player, 5) && capabilitiesFor(entity).canConnect)
      .sort((a, b) => distance(player.position, a.position) - distance(player.position, b.position) || a.id.localeCompare(b.id));
    // A CONNECT administrator-access candidate is deliberately a relationship
    // boundary. It must remain completable even when the generated slot has no
    // second NPC nearby: the protagonist is the natural second endpoint. The
    // generic endpoint list excludes the player to avoid self-targeting ordinary
    // connections, so materialize this one campaign-critical pair explicitly.
    const adminPairs = endpoints
      .filter((entity) => adminCandidate(run, entity, "CONNECT"))
      .map((entity, index) => ({
        candidateId: `admin_pair_${index}`,
        kind: "entity_pair",
        entityIds: [entity.id, player.id],
        label: `${entity.name} ↔ ${player.name}`,
        distance: distance(player.position, entity.position),
        adminAccessCandidateId: entity.state?.candidateId || null
      }));
    const requiredPairs = prioritizedFinaleConnectionPairs(run, 5);
    candidates = [...adminPairs, ...requiredPairs.slice(0, Math.max(0, 8 - adminPairs.length)).map((pair, index) => ({
      candidateId: `finale_pair_${index}`,
      kind: "entity_pair",
      entityIds: [pair.left.id, pair.right.id],
      label: `${pair.left.name} ↔ ${pair.right.name}`,
      distance: Math.max(distance(player.position, pair.left.position), distance(player.position, pair.right.position)),
      requiredComponents: pair.components,
      requiredByEndingIds: pair.endingIds
    }))];
    const seenPairs = new Set(candidates.map((candidate) => [...candidate.entityIds].sort().join(":")));
    for (let left = 0; left < endpoints.length && candidates.length < 8; left += 1) {
      for (let right = left + 1; right < endpoints.length && candidates.length < 8; right += 1) {
        const a = endpoints[left];
        const b = endpoints[right];
        const exists = (run.connections || []).some((item) => item.active && ((item.fromId === a.id && item.toId === b.id) || (item.fromId === b.id && item.toId === a.id)));
        const pairKey = [a.id, b.id].sort().join(":");
        if (!exists && !seenPairs.has(pairKey)) {
          seenPairs.add(pairKey);
          candidates.push({ candidateId: `pair_${candidates.length}`, kind: "entity_pair", entityIds: [a.id, b.id], label: `${a.name} ↔ ${b.name}`, distance: Math.max(distance(player.position, a.position), distance(player.position, b.position)) });
        }
      }
    }
  }
  for (const discoveryType of DISCOVERIES[request.skillId] || []) {
    candidates.push({ candidateId: `ambient_${discoveryType}`, kind: "ambient", entityIds: [], discoveryType, label: discoveryType });
  }
  return {
    skillId: request.skillId,
    d20,
    rollBand: d20 === 20 ? "exceptional" : d20 >= 15 ? "strong" : d20 >= 10 ? "standard" : d20 >= 2 ? "weak" : "critical_failure",
    playerNote: request.playerNote,
    candidates,
    currentArcQuestion: run.currentArcQuestion || null,
    resolvedArcOutcomes: (run.resolvedArcOutcomes || []).slice(-4),
    episodeSummaries: (run.episodeSummaries || []).slice(-4),
    recentStoryLedger: (run.storyLedger || []).slice(-8),
    majorChoices: (run.majorChoices || []).slice(-6),
    regionOutcomes: (run.regionOutcomes || []).slice(-6),
    npcMemories: (run.npcMemories || []).filter((item) => !item.expired).slice(-6),
    openLoops: (run.openLoops || []).filter((item) => item.status === "open").slice(-6)
  };
}

export function createFallbackSkillTarget(context) {
  const candidate = context?.candidates?.[0];
  return { candidateId: candidate?.candidateId || null, rationale: "서버 우선순위 후보를 선택했다.", fallbackUsed: true, model: "deterministic-skill-target" };
}

function criticalCampaignSelection(run, request, context) {
  // HIDDEN_TRUTH is a mechanical Root gate, not optional ambience. A
  // targetless SEARCH at the range boundary must bind the designated evidence
  // even when the narrator proposes a generic discovery. The committed turn
  // will then promote this selection into its authoritative request target.
  const requiredStoryRevelation = request.skillId === "SEARCH"
    ? context?.candidates?.find((candidate) => {
      if (candidate.kind !== "entity" || candidate.entityIds?.length !== 1) return false;
      const entity = run.entities.find((item) => item.id === candidate.entityIds[0]);
      return requiredStoryRevelationCandidate(run, entity);
    })
    : null;
  if (requiredStoryRevelation) {
    return validateSkillTarget({
      candidateId: requiredStoryRevelation.candidateId,
      rationale: "Root System 진입에 필요한 감춰진 진실의 증거를 우선 조사했다.",
      model: "deterministic-campaign-target"
    }, context);
  }

  const adminAccess = context?.candidates?.find((candidate) => {
    if (candidate.kind !== "entity" || candidate.entityIds?.length !== 1) return false;
    const entity = run.entities.find((item) => item.id === candidate.entityIds[0]);
    return adminCandidate(run, entity, request.skillId);
  });
  if (adminAccess) {
    return validateSkillTarget({
      candidateId: adminAccess.candidateId,
      rationale: "현재 위치에서 획득 가능한 다음 관리자 권한을 우선 선택했다.",
      model: "deterministic-campaign-target"
    }, context);
  }

  const adminAccessPair = request.skillId === "CONNECT"
    ? context?.candidates?.find((candidate) => {
      if (candidate.kind !== "entity_pair" || candidate.entityIds?.length !== 2) return false;
      const target = run.entities.find((item) => item.id === candidate.entityIds[0]);
      return adminCandidate(run, target, "CONNECT");
    })
    : null;
  if (adminAccessPair) {
    return validateSkillTarget({
      candidateId: adminAccessPair.candidateId,
      rationale: "현재 위치에서 획득 가능한 연결형 관리자 권한을 플레이어와 우선 연결했다.",
      model: "deterministic-campaign-target"
    }, context);
  }

  const finaleRemoval = request.skillId === "DELETE"
    ? context?.candidates?.find((candidate) => candidate.kind === "entity"
      && prioritizedFinaleRemovalTargets(run, 3)
        .some((removal) => removal.target.id === candidate.entityIds?.[0]))
    : null;
  if (finaleRemoval) {
    return validateSkillTarget({
      candidateId: finaleRemoval.candidateId,
      rationale: "현재 엔딩 조건에서 제거해야 하는 루트 구성요소를 우선 선택했다.",
      model: "deterministic-campaign-target"
    }, context);
  }

  // The targetless keyboard flow is the only way to express a two-entity
  // finale recipe without a pointer. Once the authoritative gate exposes a
  // missing recipe pair, ambience or a generic pair must never supersede it.
  const finalePair = request.skillId === "CONNECT"
    ? context?.candidates?.find((candidate) => candidate.candidateId.startsWith("finale_pair_"))
    : null;
  return finalePair ? validateSkillTarget({
    candidateId: finalePair.candidateId,
    rationale: "현재 엔딩 조건에 필요한 연결을 우선 선택했다.",
    model: "deterministic-campaign-target"
  }, context) : null;
}

export function validateSkillTarget(raw, context) {
  const candidateId = typeof raw?.candidateId === "string" ? raw.candidateId : "";
  const candidate = context.candidates.find((item) => item.candidateId === candidateId);
  if (!candidate) throw new Error("Unknown skill target candidate.");
  const rationale = typeof raw?.rationale === "string" ? raw.rationale.trim().slice(0, 160) : "";
  const generatedEvent = raw?.generatedEvent && typeof raw.generatedEvent === "object" ? {
    title: String(raw.generatedEvent.title || "").trim().slice(0, 80),
    description: String(raw.generatedEvent.description || "").trim().slice(0, 360),
    discoveryType: String(raw.generatedEvent.discoveryType || candidate.discoveryType || "ambient_discovery").trim().slice(0, 64)
  } : null;
  if (candidate.kind === "ambient" && generatedEvent && AMBIENT_PERSISTENCE_ASSERTION.test(`${generatedEvent.title} ${generatedEvent.description}`)) throw new Error("Ambient skill events cannot claim a persistent world result.");
  return { ...candidate, generatedEvent, rationale, fallbackUsed: raw?.fallbackUsed === true, model: raw?.model || "validated-skill-target" };
}

export async function planSkillTarget({ narrator, run, request, d20, logger = console }) {
  const context = buildSkillTargetContext(run, request, d20);
  if (!context) return null;
  const criticalSelection = criticalCampaignSelection(run, request, context);
  if (criticalSelection) return { context, selection: criticalSelection };
  if (typeof narrator?.planSkillTarget !== "function") return { context, selection: null };
  let raw;
  try {
    raw = await narrator.planSkillTarget(context);
    if (raw?.fallbackUsed === true) return { context, selection: null };
    return { context, selection: validateSkillTarget(raw, context) };
  } catch (error) {
    logger?.warn?.({ event: "skill_target_fallback", category: error?.code || "validation_or_transport" });
    return { context, selection: null };
  }
}
