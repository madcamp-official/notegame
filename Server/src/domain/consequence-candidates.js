import { createHash } from "node:crypto";
import { macroPhaseForBeat } from "./campaign.js";
import { MONSTER_CATALOG, NPC_CATALOG, SPECIAL_SKILL_TEMPLATES, bossCandidatesFor } from "./content-catalog.js";
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

  if (run.activeEncounter?.status === "active") {
    candidates.push(candidate(run, decisionNo, "START_ENCOUNTER", run.activeEncounter.sourceEntityId, run.playerEntityId, 100, 0, "안전 이동이 위험 요소 앞에서 멈추어 조우가 활성화되었다."));
  }

  for (const enemy of enemies) {
    const playerDistance = manhattan(enemy.position, player.position);
    const traits = new Set(enemy.state?.traits || []);
    const lowHealth = Number(enemy.state?.hp || 0) <= Math.max(1, Math.floor(Number(enemy.state?.maxHp || 1) * 0.3));
    if (lowHealth && traits.has("FLEE_AT_LOW_HP")) {
      candidates.push(candidate(run, decisionNo, "FLEE_ENTITY", enemy.id, player.id, 96, 1, "체력이 낮은 생존형 오류 개체가 플레이어에게서 도주할 수 있다.", "FLEE"));
    }
    if (playerDistance === 1 && !onCooldown(run, "ATTACK_ENTITY") && probabilityGate(run, decisionNo, `attack-player:${enemy.id}`, 85)) {
      candidates.push(candidate(run, decisionNo, "ATTACK_ENTITY", enemy.id, player.id, traits.has("AMBUSHER") ? 96 : 92, 2, "적대 오류 개체가 플레이어와 인접해 공격할 수 있다.", traits.has("AMBUSHER") ? "AMBUSH" : "MELEE"));
    } else if (playerDistance >= 2 && playerDistance <= 3 && !onCooldown(run, "MOVE_ACTOR") && probabilityGate(run, decisionNo, `approach:${enemy.id}`, 70)) {
      candidates.push(candidate(run, decisionNo, "MOVE_ACTOR", enemy.id, player.id, 72, 1, "적대 오류 개체가 플레이어에게 접근할 수 있다.", "APPROACH"));
    }
    for (const npc of npcs) {
      if (manhattan(enemy.position, npc.position) === 1 && probabilityGate(run, decisionNo, `attack-npc:${enemy.id}:${npc.id}`, 65)) {
        candidates.push(candidate(run, decisionNo, "ATTACK_ENTITY", enemy.id, npc.id, 86, 2, "적대 오류 개체와 비적대 NPC가 인접해 있다.", "MELEE"));
      }
    }
    if (traits.has("GUARDIAN") && enemies.some((ally) => ally.id !== enemy.id && manhattan(ally.position, enemy.position) <= 1)) {
      const ally = enemies.find((item) => item.id !== enemy.id && manhattan(item.position, enemy.position) <= 1);
      candidates.push(candidate(run, decisionNo, "DEFEND_ENTITY", enemy.id, ally.id, 74, 1, "수호 성향 오류 개체가 인접한 동료를 방어할 수 있다.", "GUARD"));
    }
    if (traits.has("SUPPORT")) {
      const damaged = enemies.find((ally) => ally.id !== enemy.id && manhattan(ally.position, enemy.position) <= 2 && Number(ally.state?.hp || 0) < Number(ally.state?.maxHp || 0));
      if (damaged) candidates.push(candidate(run, decisionNo, "ASSIST_ENTITY", enemy.id, damaged.id, 76, 1, "지원형 오류 개체가 손상된 동료를 회복시킬 수 있다.", "SUPPORT"));
    }
  }

  for (const npc of npcs) {
    const relationship = run.npcRelationships.find((item) => item.npcId === npc.id);
    if (manhattan(npc.position, player.position) <= 2 && relationship?.stance !== "hostile" && !onCooldown(run, "START_DIALOGUE") && probabilityGate(run, decisionNo, `dialogue:${npc.id}`, 65)) {
      candidates.push(candidate(run, decisionNo, "START_DIALOGUE", npc.id, player.id, 58 + Math.max(0, Math.round((relationship?.affinity || 0) / 10)), 0, "가까운 NPC가 플레이어의 방금 선택에 반응할 수 있다."));
    }
    for (const enemy of enemies) {
      if (manhattan(npc.position, enemy.position) === 1 && relationship?.stance !== "hostile" && probabilityGate(run, decisionNo, `npc-defend:${npc.id}:${enemy.id}`, 55)) {
        candidates.push(candidate(run, decisionNo, "ATTACK_ENTITY", npc.id, enemy.id, 78, 1, "NPC가 가까운 적대 오류 개체에 대응할 수 있다.", "MELEE"));
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
  const npcQuest = (run.activeQuests || []).find((quest) => quest.status === "active" && quest.questKind !== "main");
  if (nearbyNpc && npcQuest && probabilityGate(run, decisionNo, `quest:${npcQuest.id}`, 22)) {
    candidates.push(candidate(run, decisionNo, "ADVANCE_QUEST", nearbyNpc.id, player.id, 48, 1, "활성 보조 퀘스트를 한 단계 진행할 수 있다.", null, { questId: npcQuest.id }));
  }
  if (nearbyNpc && (directorState.specialSkills || []).length < 3 && probabilityGate(run, decisionNo, `reward:${nearbyNpc.id}`, 10)) {
    const reward = SPECIAL_SKILL_TEMPLATES[decisionNo % SPECIAL_SKILL_TEMPLATES.length];
    if (!(directorState.specialSkills || []).some((skill) => skill.templateId === reward.id)) {
      candidates.push(candidate(run, decisionNo, "GRANT_SPECIAL_REWARD", nearbyNpc.id, player.id, 62, 2, "관계와 서사 단계가 런 전용 키보드 변형을 보상할 수 있다.", "REWARD", { rewardId: reward.id }));
    }
  }

  const macroPhase = run.currentMacroPhase || macroPhaseForBeat(run.currentStoryBeat);
  const currentArea = areaAt(run.world, player.position);
  const freeSlot = (kind) => run.world.placementSlots.find((item) => item.kind === kind && item.areaId === currentArea.id &&
    !run.entities.some((entity) => entity.active && (entity.state?.slotId === item.id || (entity.position.x === item.x && entity.position.y === item.y))));
  if (!onCooldown(run, "SPAWN_FROM_SLOT") && (directorState.generatedCharacters || []).length < 3 && probabilityGate(run, decisionNo, `rare-npc:${currentArea.id}`, 7)) {
    const slot = freeSlot("npc");
    if (slot) {
      const npcAsset = NPC_CATALOG[decisionNo % NPC_CATALOG.length];
      const names = ["리턴", "포인터", "패치", "스택", "모듈", "브랜치"];
      candidates.push(candidate(run, decisionNo, "SPAWN_FROM_SLOT", null, player.id, 56, 2, "현재 지역의 빈 NPC 슬롯에 런 전용 인물이 등장할 수 있다.", "ARRIVAL", {
        slotId: slot.id, assetId: npcAsset.assetId, spawnKind: "npc", displayName: names[decisionNo % names.length], traitIds: [...npcAsset.roleTags].slice(0, 3)
      }));
    }
  }
  if (!onCooldown(run, "SPAWN_FROM_SLOT") && enemies.length === 0 && probabilityGate(run, decisionNo, `monster-variant:${currentArea.id}`, 14)) {
    const slot = freeSlot("enemy");
    if (slot) {
      const monster = MONSTER_CATALOG[decisionNo % MONSTER_CATALOG.length];
      const extraTraits = ["LOOTER", "AMBUSHER", "GUARDIAN", "FLEE_AT_LOW_HP", "MEMORY_DRAIN"];
      const traits = [...new Set([...monster.traits, extraTraits[decisionNo % extraTraits.length]])].slice(0, 3);
      candidates.push(candidate(run, decisionNo, "SPAWN_FROM_SLOT", null, player.id, 68, 2, "현재 지역의 빈 오류 슬롯에 서버 검증 몬스터 변종이 등장할 수 있다.", "ENCOUNTER", {
        slotId: slot.id, assetId: monster.assetId, spawnKind: "enemy", displayName: `${monster.name} 변종`, traitIds: traits
      }));
    }
  }
  const bossAlreadyGenerated = run.entities.some((entity) => String(entity.assetId || "").startsWith("boss."));
  if (!onCooldown(run, "SPAWN_FROM_SLOT") && !bossAlreadyGenerated && Number(macroPhase.order || 1) >= 2 && probabilityGate(run, decisionNo, `rare-boss:${currentArea.id}`, 8)) {
    const slot = freeSlot("enemy");
    const bosses = bossCandidatesFor({ macroOrder: Number(macroPhase.order || 1), campaignRole: currentArea.campaignRole });
    if (slot && bosses.length > 0) {
      const boss = bosses[decisionNo % bosses.length];
      candidates.push(candidate(run, decisionNo, "SPAWN_FROM_SLOT", null, player.id, 84, 3, "현재 지역과 캠페인 단계가 허용하는 희귀 보스 슬롯이 열렸다.", "ENCOUNTER", {
        slotId: slot.id, assetId: boss.assetId, spawnKind: "enemy", displayName: boss.name, traitIds: [...boss.traits]
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
      candidates: candidates.slice(0, 32),
      recentSceneTypes: (directorState.recentSceneTypes || []).slice(-8),
      openLoops: (run.openLoops || []).filter((item) => item.status === "open").slice(-8).map((item) => ({ id: item.id, summary: item.summary })),
      npcRelationships: (run.npcRelationships || []).filter((item) => npcs.some((npc) => npc.id === item.npcId)).slice(0, 16),
      fixedCanon: [...FIXED_CANON],
      maxSelectedActions: 4
    },
    candidates,
    source: { decisionType, navigation: navigation ? { encounterOpened: Boolean(navigation.encounterOpened), campaignRole: navigation.campaignRole } : null, turn: turn ? { outcome: turn.outcome, actionContext: turn.actionContext } : null }
  };
}
