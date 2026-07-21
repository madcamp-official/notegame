import { createHash } from "node:crypto";
import { macroPhaseForBeat } from "./campaign.js";
import { SPECIAL_SKILL_TEMPLATES } from "./content-catalog.js";
import { clone, deterministicUuid } from "./serialization.js";
import { areaAt } from "./world.js";

const FIXED_CANON = Object.freeze([
  "세계는 WORLD_CODRIA이며 주인공은 PROTAGONIST_NUPJUKYI이다.",
  "관리자 키보드와 관리자 권한 3단계는 대체하거나 소급 변경할 수 없다.",
  "월드 geometry와 좌표는 서버 전용이며 LLM이 생성하거나 수정할 수 없다.",
  "붕괴 원인은 관리자 통제 시스템 내부에 있으며 구체적 진실만 런마다 달라질 수 있다.",
  "최종 결말 ID는 루트 시스템의 서버 레시피가 확정한다."
]);

function manhattan(left, right) {
  return Math.abs(left.x - right.x) + Math.abs(left.y - right.y);
}

function activeEntity(run, id) {
  return run.entities.find((entity) => entity.id === id && entity.active) || null;
}

function probabilityGate(run, decisionNo, label, chance) {
  const value = createHash("sha256")
    .update(`${run.resolutionSeed}|${run.id}|scene:${decisionNo}|${label}`)
    .digest()
    .readUInt32BE(0) % 100;
  return value < chance;
}

function candidate(run, decisionNo, type, actorId, targetId, priority, cost, reason, actionStyle = null, data = {}) {
  return {
    candidateId: deterministicUuid(`${run.id}:scene:${decisionNo}:${type}:${actorId || "world"}:${targetId || "none"}:${actionStyle || "default"}:${JSON.stringify(data)}`),
    type,
    actorId: actorId || null,
    targetId: targetId || null,
    priority,
    cost,
    reason,
    actionStyle,
    ...data
  };
}

function onCooldown(run, type) {
  return Number(run.directorState?.eventCooldowns?.[type] || 0) > 0;
}

export function buildConsequenceCandidates(run, { decisionType, navigation = null, turn = null } = {}) {
  const directorState = run.directorState || {};
  const decisionNo = Number(directorState.decisionNo || 0) + 1;
  const player = activeEntity(run, run.playerEntityId);
  const candidates = [];
  const actors = run.entities
    .filter((entity) => entity.active && !entity.state?.disabled && !entity.state?.fled && ["npc", "enemy"].includes(entity.kind) && manhattan(entity.position, player.position) <= 5)
    .sort((left, right) => manhattan(left.position, player.position) - manhattan(right.position, player.position)
      || Number(right.kind === "enemy") - Number(left.kind === "enemy") || left.id.localeCompare(right.id))
    .slice(0, 5);
  const props = run.entities.filter((entity) => entity.active && entity.kind === "prop" && entity.state?.opened !== true && entity.state?.interacted !== true && manhattan(entity.position, player.position) <= 5);
  const npcs = actors.filter((entity) => entity.kind === "npc");
  const enemies = actors.filter((entity) => entity.kind === "enemy");
  const narrativeIntent = String(turn?.selectedChoice?.intentTag || "").toUpperCase();

  if (run.activeEncounter?.status === "active") {
    candidates.push(candidate(run, decisionNo, "START_ENCOUNTER", run.activeEncounter.sourceEntityId, run.playerEntityId, 100, 0, "안전 이동이 위험 요소 앞에서 멈추어 조우가 활성화되었다."));
  }

  for (const enemy of enemies) {
    const playerDistance = manhattan(enemy.position, player.position);
    const traits = new Set(enemy.state?.traits || []);
    const relationship = run.npcRelationships.find((item) => item.npcId === enemy.id);
    const confrontationEscalated = ["ASSERTIVE", "PROTECT"].includes(narrativeIntent);
    if (playerDistance <= 1 && (relationship?.stance === "hostile" || confrontationEscalated)) {
      candidates.push(candidate(run, decisionNo, "ATTACK_ENTITY", enemy.id, player.id,
        relationship?.stance === "hostile" ? 96 : 88, 2,
        confrontationEscalated
          ? "단호한 대화가 가까운 적대 개체와의 충돌로 번질 수 있다. 선택될 때만 서버가 공격을 확정한다."
          : "이미 적대적인 개체가 인접해 있어 협상이 무너지면 공격으로 이어질 수 있다.", "DIALOGUE_BREAKDOWN"));
    }
    if (playerDistance <= 2 && !onCooldown(run, "START_DIALOGUE")) {
      candidates.push(candidate(run, decisionNo, "START_DIALOGUE", enemy.id, player.id,
        relationship?.stance === "hostile" ? 92 : 84, 0,
        "가까운 오류 개체가 플레이어의 개입에 말과 태도로 반응할 수 있다.", "ENCOUNTER_DIALOGUE"));
    } else if (playerDistance >= 2 && playerDistance <= 3 && !onCooldown(run, "MOVE_ACTOR") && probabilityGate(run, decisionNo, `approach:${enemy.id}`, 70)) {
      candidates.push(candidate(run, decisionNo, "MOVE_ACTOR", enemy.id, player.id, 72, 1, "경계 중인 오류 개체가 대화를 피하지 않고 거리를 좁힌다.", "APPROACH"));
    }
    if (traits.has("GUARDIAN") && enemies.some((ally) => ally.id !== enemy.id && manhattan(ally.position, enemy.position) <= 1)) {
      const ally = enemies.find((item) => item.id !== enemy.id && manhattan(item.position, enemy.position) <= 1);
      candidates.push(candidate(run, decisionNo, "DEFEND_ENTITY", enemy.id, ally.id, 74, 1, "수호 성향 오류 개체가 인접한 동료를 방어할 수 있다.", "GUARD"));
    }
    if (traits.has("SUPPORT")) {
      const guarded = enemies.find((ally) => ally.id !== enemy.id && manhattan(ally.position, enemy.position) <= 2);
      if (guarded) candidates.push(candidate(run, decisionNo, "DEFEND_ENTITY", enemy.id, guarded.id, 76, 1, "지원형 오류 개체가 동료 앞을 지키며 협상 태도를 드러낸다.", "SUPPORT"));
    }
  }

  for (const npc of npcs) {
    const relationship = run.npcRelationships.find((item) => item.npcId === npc.id);
    if (manhattan(npc.position, player.position) <= 2 && relationship?.stance !== "hostile" && !onCooldown(run, "START_DIALOGUE") && probabilityGate(run, decisionNo, `dialogue:${npc.id}`, 65)) {
      candidates.push(candidate(run, decisionNo, "START_DIALOGUE", npc.id, player.id, 58 + Math.max(0, Math.round((relationship?.affinity || 0) / 10)), 0, "가까운 NPC가 플레이어의 방금 선택에 반응할 수 있다."));
    }
    for (const enemy of enemies) {
      if (manhattan(npc.position, enemy.position) === 1 && relationship?.stance !== "hostile" && probabilityGate(run, decisionNo, `npc-defend:${npc.id}:${enemy.id}`, 55)) {
        candidates.push(candidate(run, decisionNo, "DEFEND_ENTITY", npc.id, enemy.id, 70, 1, "NPC가 가까운 오류 개체와 플레이어 사이에서 중재 자세를 취한다.", "MEDIATION"));
      }
    }
    const threatened = enemies.find((enemy) => manhattan(enemy.position, player.position) <= 2);
    if (threatened && (npc.state?.traits || []).includes("GUARDIAN") && manhattan(npc.position, player.position) <= 2) {
      candidates.push(candidate(run, decisionNo, "DEFEND_ENTITY", npc.id, player.id, 82, 1, "전투형 NPC가 플레이어를 보호할 수 있다.", "GUARD"));
    }
  }

  for (const actor of actors) {
    for (const prop of props) {
      if (manhattan(actor.position, prop.position) !== 1 || onCooldown(run, "LOOT_PROP")) continue;
      const chance = (actor.state?.traits || []).includes("LOOTER") ? 90 : actor.kind === "enemy" ? 55 : 40;
      if (probabilityGate(run, decisionNo, `loot:${actor.id}:${prop.id}`, chance)) {
        candidates.push(candidate(run, decisionNo, "LOOT_PROP", actor.id, prop.id, (actor.state?.traits || []).includes("LOOTER") ? 88 : actor.kind === "enemy" ? 70 : 52, 1, "행동 가능한 개체가 아직 조사되지 않은 물체에 인접해 있다."));
      }
    }
  }

  const nearbyEvidence = props.find((prop) => prop.state?.evidenceKey && !prop.state?.revealed && manhattan(prop.position, player.position) <= 3);
  const nearbyNpc = npcs[0] || null;
  const dueConsequence = (directorState.pendingConsequences || []).find((item) => item.status === "pending" && Number(item.dueDecisionNo || 0) <= decisionNo);
  if (dueConsequence) {
    const callbackActor = activeEntity(run, dueConsequence.actorId) || nearbyNpc;
    const callbackNpc = callbackActor?.kind === "npc" ? callbackActor : nearbyNpc;
    if (callbackNpc) candidates.push(candidate(run, decisionNo, "CREATE_HOOK", callbackNpc.id, player.id, 89, 1,
      `이전 ${dueConsequence.sourceActionType} 선택에서 예약된 결과가 돌아올 시점이다.`, "CALLBACK", { pendingId: dueConsequence.id }));
  }
  if (nearbyEvidence) {
    candidates.push(candidate(run, decisionNo, "REVEAL_CLUE", nearbyNpc?.id || null, nearbyEvidence.id, 64, 1, "서버가 배치한 증거 물체가 조사 가능한 거리에 있다.", "REVEAL"));
  }
  if (nearbyNpc && !onCooldown(run, "CHANGE_RELATIONSHIP") && probabilityGate(run, decisionNo, `relationship:${nearbyNpc.id}`, 28)) {
    candidates.push(candidate(run, decisionNo, "CHANGE_RELATIONSHIP", nearbyNpc.id, player.id, 46, 1, "가까운 NPC가 플레이어의 선택을 관계 변화로 기억할 수 있다.", null, { delta: 1 }));
  }
  if (nearbyNpc && probabilityGate(run, decisionNo, `memory:${nearbyNpc.id}`, 25)) {
    candidates.push(candidate(run, decisionNo, "ADD_NPC_MEMORY", nearbyNpc.id, player.id, 44, 0, "가까운 NPC가 이번 선택의 의미를 장기 기억으로 남길 수 있다."));
  }
  if (nearbyNpc && (run.openLoops || []).filter((loop) => loop.status === "open").length < 8 && probabilityGate(run, decisionNo, `hook:${nearbyNpc.id}`, 16)) {
    candidates.push(candidate(run, decisionNo, "CREATE_HOOK", nearbyNpc.id, player.id, 40, 1, "현재 거시 골조를 벗어나지 않는 작은 복선을 만들 여지가 있다."));
  }
  if (nearbyNpc && (directorState.specialSkills || []).length < 3 && probabilityGate(run, decisionNo, `reward:${nearbyNpc.id}`, 10)) {
    const reward = SPECIAL_SKILL_TEMPLATES[decisionNo % SPECIAL_SKILL_TEMPLATES.length];
    if (!(directorState.specialSkills || []).some((skill) => skill.templateId === reward.id)) {
      candidates.push(candidate(run, decisionNo, "GRANT_SPECIAL_REWARD", nearbyNpc.id, player.id, 62, 2, "관계와 서사 단계가 런 전용 키보드 변형을 보상할 수 있다.", "REWARD", { rewardId: reward.id }));
    }
  }

  const macroPhase = run.currentMacroPhase || macroPhaseForBeat(run.currentStoryBeat);
  const currentArea = areaAt(run.world, player.position);
  const dormantCandidate = (kind, predicate = () => true) => run.entities.find((entity) => entity.kind === kind && entity.active === false
    && entity.state?.activationState === "DORMANT" && entity.state?.dormant === true && predicate(entity)
    && run.world.placementSlots.some((slot) => slot.id === entity.state.activationSlotId && slot.areaId === currentArea.id)
    && !run.entities.some((other) => other.active && (other.state?.slotId === entity.state.activationSlotId || (other.position.x === entity.position.x && other.position.y === entity.position.y))));
  if (!onCooldown(run, "SPAWN_FROM_SLOT") && (directorState.generatedCharacters || []).length < 3 && probabilityGate(run, decisionNo, `rare-npc:${currentArea.id}`, 7)) {
    const dormant = dormantCandidate("npc");
    if (dormant) {
      candidates.push(candidate(run, decisionNo, "SPAWN_FROM_SLOT", null, player.id, 56, 2, "현재 지역의 휴면 NPC 후보가 활성화될 수 있다.", "ARRIVAL", {
        entityId: dormant.id, slotId: dormant.state.activationSlotId, assetId: dormant.assetId, spawnKind: "npc", displayName: dormant.name, traitIds: [...(dormant.state.roleTags || [])]
      }));
    }
  }
  if (!onCooldown(run, "SPAWN_FROM_SLOT") && enemies.length === 0 && probabilityGate(run, decisionNo, `monster-variant:${currentArea.id}`, 14)) {
    const dormant = dormantCandidate("enemy", (entity) => entity.state?.boss !== true);
    if (dormant) {
      candidates.push(candidate(run, decisionNo, "SPAWN_FROM_SLOT", null, player.id, 68, 2, "현재 지역의 휴면 몬스터 후보가 활성화될 수 있다.", "ENCOUNTER", {
        entityId: dormant.id, slotId: dormant.state.activationSlotId, assetId: dormant.assetId, spawnKind: "enemy", displayName: dormant.name, traitIds: [...(dormant.state.traits || [])]
      }));
    }
  }
  const bossAlreadyGenerated = run.entities.some((entity) => entity.active && entity.state?.boss === true);
  if (!onCooldown(run, "SPAWN_FROM_SLOT") && !bossAlreadyGenerated && Number(macroPhase.order || 1) >= 2 && probabilityGate(run, decisionNo, `rare-boss:${currentArea.id}`, 8)) {
    const dormant = dormantCandidate("enemy", (entity) => entity.state?.boss === true && Number(entity.state?.minMacroOrder || 99) <= Number(macroPhase.order || 1)
      && (!Array.isArray(entity.state?.roles) || entity.state.roles.includes(currentArea.campaignRole)));
    if (dormant) {
      candidates.push(candidate(run, decisionNo, "SPAWN_FROM_SLOT", null, player.id, 84, 3, "현재 지역과 캠페인 단계가 허용하는 휴면 보스 후보가 활성화될 수 있다.", "ENCOUNTER", {
        entityId: dormant.id, slotId: dormant.state.activationSlotId, assetId: dormant.assetId, spawnKind: "enemy", displayName: dormant.name, traitIds: [...(dormant.state.traits || [])]
      }));
    }
  }

  candidates.push(candidate(run, decisionNo, "NO_EVENT", null, null, 1, 0, "즉시 발생할 의미 있는 후속 사건 없이 세계가 다음 선택을 기다린다."));
  return {
    context: {
      schemaVersion: "1.0",
      requestType: "SCENE_PLAN",
      decisionType: decisionType === "ACTION" ? "ACTION" : "TRAVEL",
      decisionNo,
      campaign: { title: run.campaignTitle, premise: run.premise, contentHash: run.campaignContentHash },
      macroPhase: clone(macroPhase),
      storyBeat: clone(run.currentStoryBeat),
      area: currentArea.name,
      playerId: player.id,
      actors: [player, ...actors].slice(0, 16).map((actor) => {
        const relationship = run.npcRelationships.find((item) => item.npcId === actor.id);
        return { id: actor.id, kind: actor.kind, name: actor.name, distance: manhattan(actor.position, player.position), stance: relationship?.stance || null, disabled: Boolean(actor.state?.disabled) };
      }),
      candidates: candidates.slice(0, 32),
      recentSceneTypes: (directorState.recentSceneTypes || []).slice(-8),
      openLoops: (run.openLoops || []).filter((item) => item.status === "open").slice(-8).map((item) => ({ id: item.id, summary: item.summary })),
      npcRelationships: (run.npcRelationships || []).filter((item) => actors.some((actor) => actor.id === item.npcId)).slice(0, 16),
      fixedCanon: [...FIXED_CANON],
      maxSelectedActions: 4
    },
    candidates,
    source: { decisionType, navigation: navigation ? { outcome: navigation.outcome, d20: navigation.d20, encounterOpened: Boolean(navigation.encounterOpened), campaignRole: navigation.campaignRole } : null, turn: turn ? { outcome: turn.outcome, actionContext: turn.actionContext } : null }
  };
}

export function materializeProposedSceneActions(run, proposedActions = []) {
  const decisionNo = Number(run.directorState?.decisionNo || 0) + 1;
  const costs = { MOVE_ACTOR: 1, ATTACK_ENTITY: 2, DEFEND_ENTITY: 1, ASSIST_ENTITY: 1, FLEE_ENTITY: 1, START_DIALOGUE: 0, NARRATIVE_EVENT: 0 };
  return proposedActions.flatMap((proposal, index) => {
    return [candidate(run, decisionNo, proposal.type, proposal.actorId, proposal.targetId, 60, costs[proposal.type] ?? 0,
      proposal.text, proposal.actionStyle || "STORY_PROPOSAL", { proposalIndex: index, ...(proposal.type === "NARRATIVE_EVENT" ? { text: proposal.text } : {}) })];
  });
}
