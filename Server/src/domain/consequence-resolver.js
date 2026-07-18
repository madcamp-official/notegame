import { createHash } from "node:crypto";
import { clone, deterministicUuid } from "./serialization.js";
import { isWalkable } from "./world.js";
import { SPECIAL_SKILL_MODIFIERS, bossForAsset, monsterForAsset, npcForAsset, specialSkillTemplate } from "./content-catalog.js";

const DIRECTIONS = Object.freeze([[1, 0], [0, 1], [-1, 0], [0, -1]]);
const COOLDOWNS = Object.freeze({ START_DIALOGUE: 2, LOOT_PROP: 4, OPEN_PROP: 4, START_ENCOUNTER: 1, CHANGE_RELATIONSHIP: 2, GRANT_SPECIAL_REWARD: 6, SPAWN_FROM_SLOT: 8 });

function manhattan(left, right) {
  return Math.abs(left.x - right.x) + Math.abs(left.y - right.y);
}

function entity(run, id) {
  return run.entities.find((item) => item.id === id && item.active) || null;
}

function occupied(run, point, exceptId = null) {
  return run.entities.some((item) => item.active && !item.state?.disabled && item.id !== exceptId
    && (item.blocking || item.kind === "prop") && item.position.x === point.x && item.position.y === point.y);
}

function deterministicRoll(run, decisionNo, label, sides) {
  return (createHash("sha256")
    .update(`${run.resolutionSeed}|${run.id}|scene:${decisionNo}|${label}`)
    .digest()
    .readUInt32BE(0) % sides) + 1;
}

function stepToward(run, actor, target) {
  return DIRECTIONS
    .map(([dx, dy]) => ({ x: actor.position.x + dx, y: actor.position.y + dy }))
    .filter((point) => isWalkable(run.world, point) && !occupied(run, point, actor.id))
    .sort((left, right) => manhattan(left, target.position) - manhattan(right, target.position) || left.y - right.y || left.x - right.x)[0] || null;
}

function stepAway(run, actor, target) {
  return DIRECTIONS
    .map(([dx, dy]) => ({ x: actor.position.x + dx, y: actor.position.y + dy }))
    .filter((point) => isWalkable(run.world, point) && !occupied(run, point, actor.id))
    .sort((left, right) => manhattan(right, target.position) - manhattan(left, target.position) || left.y - right.y || left.x - right.x)[0] || null;
}

function isBoss(actor) {
  return Boolean(actor && (actor.state?.boss === true || String(actor.assetId || "").startsWith("boss.")));
}

function initiative(run, candidate, decisionNo) {
  const actor = candidate.actorId ? entity(run, candidate.actorId) : null;
  const base = Number(actor?.state?.speed || (isBoss(actor) ? 3 : 2));
  const roll = deterministicRoll(run, decisionNo, `initiative:${candidate.candidateId}`, 20);
  const narrativeOnly = ["START_DIALOGUE", "ADD_NPC_MEMORY", "CREATE_HOOK", "CHANGE_RELATIONSHIP", "NO_EVENT"].includes(candidate.type);
  return { score: (narrativeOnly ? -20 : 0) + base + roll, roll };
}

function ensureDirectorState(run) {
  run.directorState ||= {};
  const state = run.directorState;
  state.decisionNo = Number(state.decisionNo || 0);
  state.recentSceneTypes ||= [];
  state.eventCooldowns ||= {};
  state.pendingConsequences ||= [];
  state.factionStandings ||= { AUDITORS: 0, OLD_GUARD: 0, NEUTRAL_BROKERS: 0 };
  state.discoveredSecrets ||= [];
  state.runTraits ||= [];
  state.specialSkills ||= [];
  state.generatedCharacters ||= [];
  state.generatedMonsterVariants ||= [];
  for (const key of Object.keys(state.eventCooldowns)) {
    state.eventCooldowns[key] = Math.max(0, Number(state.eventCooldowns[key] || 0) - 1);
    if (state.eventCooldowns[key] === 0) delete state.eventCooldowns[key];
  }
  return state;
}

function queuePendingConsequence(run, candidate, decisionNo) {
  if (candidate.pendingId || ["NO_EVENT", "START_DIALOGUE", "ADD_NPC_MEMORY", "CREATE_HOOK"].includes(candidate.type)) return;
  const state = ensureDirectorState(run);
  const id = deterministicUuid(`${run.id}:pending-consequence:${decisionNo}:${candidate.candidateId}`);
  if (state.pendingConsequences.some((item) => item.id === id)) return;
  state.pendingConsequences.push({
    id, status: "pending", sourceDecisionNo: decisionNo, dueDecisionNo: decisionNo + 2 + deterministicRoll(run, decisionNo, `pending:${candidate.candidateId}`, 2),
    sourceActionType: candidate.type, actorId: candidate.actorId || null, targetId: candidate.targetId || null,
    summary: `${candidate.type} 선택의 결과가 아직 끝나지 않았다.`
  });
  state.pendingConsequences = state.pendingConsequences.filter((item) => item.status === "pending" || Number(item.resolvedDecisionNo || 0) >= decisionNo - 4).slice(-12);
}

function attack(run, candidate, decisionNo, sequence, events) {
  const actor = entity(run, candidate.actorId);
  const target = entity(run, candidate.targetId);
  if (!actor || !target || actor.state?.disabled || target.state?.disabled || manhattan(actor.position, target.position) !== 1) return "ATTACK_PRECONDITION_FAILED";
  const roll = deterministicRoll(run, decisionNo, `attack:${actor.id}:${target.id}`, 20);
  const boss = isBoss(actor);
  const defense = Number(target.state?.defendedUntilDecision || 0) >= decisionNo ? 3 : 0;
  const hit = roll + (boss ? 4 : 3) >= 10 + defense;
  const berserk = (actor.state?.traits || []).includes("BERSERK") && Number(actor.state?.hp || 0) <= Math.max(1, Math.floor(Number(actor.state?.maxHp || 1) / 2));
  const rawDamage = (boss ? 2 : 1) + (berserk ? 1 : 0);
  const guardedDamage = defense > 0 ? Math.max(0, rawDamage - 1) : rawDamage;
  const damage = hit ? Math.min(Number(target.state?.hp || 0), guardedDamage) : 0;
  let defeated = false;
  if (hit && damage > 0) {
    const minimumHp = target.id === run.playerEntityId ? 1 : 0;
    target.state.hp = Math.max(minimumHp, Number(target.state.hp || 0) - damage);
    if (target.state.hp === 0) {
      defeated = true;
      target.state.disabled = true;
      target.state.defeated = true;
      target.blocking = false;
      events.push({ type: "entity_defeated", entityId: target.id, sourceEntityId: actor.id, decisionNo });
    }
    events.push({ type: "health_changed", entityId: target.id, sourceEntityId: actor.id, delta: -damage, hp: target.state.hp, sceneDecisionNo: decisionNo });
    if ((actor.state?.traits || []).includes("MEMORY_DRAIN")) {
      const memory = [...(run.npcMemories || [])].reverse().find((item) => !item.expired && (target.kind !== "npc" || item.npcId === target.id));
      if (memory) {
        memory.expired = true;
        memory.expiredDecisionNo = decisionNo;
        events.push({ type: "npc_memory_expired", memoryId: memory.id, sourceEntityId: actor.id, sceneDecisionNo: decisionNo });
      }
    }
  } else {
    events.push({ type: "scene_attack_missed", entityId: actor.id, targetEntityId: target.id, roll, sceneDecisionNo: decisionNo });
  }
  actor.state.lastActionTurn = run.currentTurn;
  sequence.push({ sequence: sequence.length + 1, type: "ATTACK", actorId: actor.id, targetId: target.id, actionStyle: candidate.actionStyle || "MELEE", roll, hit, damage, text: hit ? `${actor.name}의 공격이 ${target.name}에게 적중했다.` : `${actor.name}의 공격이 빗나갔다.` });
  if (hit && damage > 0) sequence.push({ sequence: sequence.length + 1, type: "DAMAGE", actorId: target.id, targetId: actor.id, damage, text: `${target.name}이 ${damage}의 피해를 입었다.` });
  if (defeated) sequence.push({ sequence: sequence.length + 1, type: "DEFEATED", actorId: target.id, targetId: actor.id, text: `${target.name}이 전투 불능 상태가 됐다.` });
  return null;
}

function moveActor(run, candidate, decisionNo, sequence, events) {
  const actor = entity(run, candidate.actorId);
  const target = entity(run, candidate.targetId);
  if (!actor || !target || actor.state?.disabled) return "MOVE_PRECONDITION_FAILED";
  const destination = stepToward(run, actor, target);
  if (!destination || manhattan(destination, target.position) >= manhattan(actor.position, target.position)) return "MOVE_PATH_UNAVAILABLE";
  const from = clone(actor.position);
  actor.position = clone(destination);
  actor.state.lastActionTurn = run.currentTurn;
  events.push({ type: "entity_moved", entityId: actor.id, from, to: clone(destination), sceneDecisionNo: decisionNo });
  sequence.push({ sequence: sequence.length + 1, type: "MOVE", actorId: actor.id, targetId: target.id, from, to: clone(destination), actionStyle: candidate.actionStyle || "APPROACH", text: `${actor.name}이 ${target.name} 쪽으로 움직였다.` });
  return null;
}

function lootProp(run, candidate, decisionNo, sequence, events) {
  const actor = entity(run, candidate.actorId);
  const prop = entity(run, candidate.targetId);
  if (!actor || !prop || prop.kind !== "prop" || actor.state?.disabled || prop.state?.opened === true || prop.state?.interacted === true || manhattan(actor.position, prop.position) !== 1) return "LOOT_PRECONDITION_FAILED";
  const rewardId = deterministicUuid(`${run.id}:scene-reward:${decisionNo}:${actor.id}:${prop.id}`);
  const reward = { id: rewardId, kind: "salvage", name: "복구 가능한 데이터 조각", sourceEntityId: prop.id, acquiredDecisionNo: decisionNo };
  actor.state.inventory ||= [];
  actor.state.inventory.push(reward);
  actor.state.lastActionTurn = run.currentTurn;
  prop.state = { ...(prop.state || {}), opened: true, interacted: true, lootedBy: actor.id, interactedTurn: run.currentTurn };
  events.push({ type: "prop_looted", entityId: prop.id, actorId: actor.id, rewardId, sceneDecisionNo: decisionNo });
  sequence.push({ sequence: sequence.length + 1, type: "LOOT", actorId: actor.id, targetId: prop.id, rewardId, text: `${actor.name}이 ${prop.name}에서 데이터 조각을 가져갔다.` });
  return null;
}

function defendEntity(run, candidate, decisionNo, sequence, events) {
  const actor = entity(run, candidate.actorId);
  const target = entity(run, candidate.targetId);
  if (!actor || !target || actor.state?.disabled || target.state?.disabled || manhattan(actor.position, target.position) > 2) return "DEFEND_PRECONDITION_FAILED";
  target.state.defendedUntilDecision = decisionNo;
  target.state.defendedBy = actor.id;
  actor.state.lastActionTurn = run.currentTurn;
  events.push({ type: "entity_defended", entityId: target.id, actorId: actor.id, sceneDecisionNo: decisionNo });
  sequence.push({ sequence: sequence.length + 1, type: "DEFEND", actorId: actor.id, targetId: target.id, actionStyle: candidate.actionStyle || "GUARD", text: `${actor.name}이 ${target.name}을 방어한다.` });
  return null;
}

function assistEntity(run, candidate, decisionNo, sequence, events) {
  const actor = entity(run, candidate.actorId);
  const target = entity(run, candidate.targetId);
  if (!actor || !target || actor.state?.disabled || target.state?.disabled || manhattan(actor.position, target.position) > 2) return "ASSIST_PRECONDITION_FAILED";
  const before = Number(target.state?.hp || 0);
  const maximum = Math.max(before, Number(target.state?.maxHp || before));
  if (before >= maximum) return "ASSIST_TARGET_FULL";
  target.state.hp = Math.min(maximum, before + 1);
  actor.state.lastActionTurn = run.currentTurn;
  events.push({ type: "health_changed", entityId: target.id, sourceEntityId: actor.id, delta: target.state.hp - before, hp: target.state.hp, sceneDecisionNo: decisionNo });
  sequence.push({ sequence: sequence.length + 1, type: "ASSIST", actorId: actor.id, targetId: target.id, actionStyle: candidate.actionStyle || "SUPPORT", text: `${actor.name}이 ${target.name}의 손상을 복구했다.` });
  return null;
}

function fleeEntity(run, candidate, decisionNo, sequence, events) {
  const actor = entity(run, candidate.actorId);
  const target = entity(run, candidate.targetId);
  if (!actor || !target || actor.state?.disabled || actor.state?.fled) return "FLEE_PRECONDITION_FAILED";
  const destination = stepAway(run, actor, target);
  if (!destination || manhattan(destination, target.position) <= manhattan(actor.position, target.position)) return "FLEE_PATH_UNAVAILABLE";
  const from = clone(actor.position);
  actor.position = clone(destination);
  actor.state.lastActionTurn = run.currentTurn;
  if (manhattan(actor.position, target.position) >= 4) {
    actor.state.fled = true;
    actor.blocking = false;
    events.push({ type: "entity_fled", entityId: actor.id, sourceEntityId: target.id, sceneDecisionNo: decisionNo });
  }
  events.push({ type: "entity_moved", entityId: actor.id, from, to: clone(destination), sceneDecisionNo: decisionNo });
  sequence.push({ sequence: sequence.length + 1, type: "FLEE", actorId: actor.id, targetId: target.id, from, to: clone(destination), actionStyle: "FLEE", text: `${actor.name}이 전투에서 물러났다.` });
  return null;
}

function revealClue(run, candidate, decisionNo, sequence, events) {
  const target = entity(run, candidate.targetId);
  const actor = candidate.actorId ? entity(run, candidate.actorId) : null;
  if (!target || !target.state?.evidenceKey || target.state.revealed === true) return "REVEAL_PRECONDITION_FAILED";
  const player = entity(run, run.playerEntityId);
  if (!player || manhattan(player.position, target.position) > 3) return "REVEAL_OUT_OF_RANGE";
  target.state.revealed = true;
  target.state.revealedDecisionNo = decisionNo;
  const secretId = String(target.state.evidenceKey);
  const state = ensureDirectorState(run);
  if (!state.discoveredSecrets.includes(secretId)) state.discoveredSecrets.push(secretId);
  events.push({ type: "clue_revealed", entityId: target.id, actorId: actor?.id || null, fact: secretId, sceneDecisionNo: decisionNo });
  sequence.push({ sequence: sequence.length + 1, type: "REVEAL", actorId: actor?.id || null, targetId: target.id, text: `${target.name}에서 ${secretId} 단서가 드러났다.` });
  return null;
}

function spawnFromSlot(run, candidate, decisionNo, sequence, events) {
  const kind = candidate.spawnKind || "enemy";
  const slot = run.world.placementSlots.find((item) => item.id === candidate.slotId && item.kind === kind);
  const boss = bossForAsset(candidate.assetId);
  const monster = monsterForAsset(candidate.assetId);
  const npc = npcForAsset(candidate.assetId);
  if (!slot || (kind === "npc" ? !npc : !boss && !monster) || (boss && boss.minMacroOrder > Number(run.currentMacroPhase?.order || 1))) return "SPAWN_CATALOG_FORBIDDEN";
  if (run.entities.some((item) => item.active && (item.state?.slotId === slot.id || (item.position.x === slot.x && item.position.y === slot.y)))) return "SPAWN_SLOT_OCCUPIED";
  const catalog = boss || monster || npc;
  const traits = [...new Set(candidate.traitIds || catalog.traits || catalog.roleTags || [])].slice(0, 4);
  const id = deterministicUuid(`${run.id}:scene-spawn:${decisionNo}:${slot.id}:${candidate.assetId}`);
  const created = {
    id, kind, assetId: candidate.assetId, name: candidate.displayName || boss?.name || monster?.name || "런 전용 인물",
    position: { x: slot.x, y: slot.y }, blocking: kind === "enemy", protected: false, cloneable: false, active: true,
    state: {
      hp: boss?.hp || monster?.hp || 8, maxHp: boss?.hp || monster?.hp || 8, speed: boss?.speed || monster?.speed || 2,
      boss: Boolean(boss), bossPatterns: [...(boss?.patterns || [])],
      factionId: kind === "npc" ? "NEUTRAL_BROKERS" : traits.includes("OLD_GUARD_ALIGNED") ? "OLD_GUARD" : traits.includes("AUDITOR_ALIGNED") || boss ? "AUDITORS" : "WILD_PROCESS",
      goal: kind === "npc" ? "현재 지역에서 플레이어의 선택이 남긴 파장을 추적한다." : "현재 캠페인 단계의 진행을 시험한다.",
      motivation: kind === "npc" ? "자신만의 생존 이유를 다음 선택과 연결하려 한다." : "자신이 대표하는 시스템 규칙을 끝까지 보존한다.",
      traits, roleTags: kind === "npc" ? [...traits] : [], awareness: [run.playerEntityId], inventory: [], lastActionTurn: 0,
      slotId: slot.id, generatedDecisionNo: decisionNo
    }
  };
  run.entities.push(created);
  const state = ensureDirectorState(run);
  if (kind === "npc") {
    state.generatedCharacters.push({ entityId: created.id, assetId: created.assetId, name: created.name, roleTags: [...created.state.roleTags] });
    run.npcRelationships.push({ npcId: created.id, affinity: 0, trust: 0, fear: 0, stance: "neutral", lastChangedTurn: run.currentTurn });
  } else {
    state.generatedMonsterVariants.push({ entityId: created.id, assetId: created.assetId, name: created.name, traits: [...created.state.traits], boss: Boolean(boss) });
  }
  events.push({ type: "entity_spawned", entityId: created.id, assetId: created.assetId, position: clone(created.position), sceneDecisionNo: decisionNo });
  sequence.push({ sequence: sequence.length + 1, type: "SPAWN", actorId: created.id, targetId: run.playerEntityId, actionStyle: candidate.actionStyle || "ENCOUNTER", text: `${created.name}이 지역의 오류 슬롯에서 모습을 드러냈다.` });
  return null;
}

function changeRelationship(run, candidate, decisionNo, sequence, events) {
  const npc = entity(run, candidate.actorId);
  const relationship = run.npcRelationships.find((item) => item.npcId === npc?.id);
  if (!npc || npc.kind !== "npc" || !relationship) return "RELATIONSHIP_PRECONDITION_FAILED";
  const delta = Math.max(-5, Math.min(5, Number(candidate.delta || 1)));
  relationship.affinity = Math.max(-100, Math.min(100, Number(relationship.affinity || 0) + delta));
  relationship.trust = Math.max(0, Math.min(100, Number(relationship.trust || 0) + Math.max(0, delta)));
  relationship.stance = relationship.affinity >= 30 ? "allied" : relationship.affinity <= -30 ? "hostile" : "neutral";
  relationship.lastChangedTurn = run.currentTurn;
  const faction = npc.state?.factionId;
  const state = ensureDirectorState(run);
  if (faction && Object.hasOwn(state.factionStandings, faction)) state.factionStandings[faction] = Math.max(-100, Math.min(100, state.factionStandings[faction] + delta));
  events.push({ type: "relationship_changed", npcId: npc.id, delta, affinity: relationship.affinity, sceneDecisionNo: decisionNo });
  sequence.push({ sequence: sequence.length + 1, type: "RELATIONSHIP", actorId: npc.id, targetId: run.playerEntityId, text: `${npc.name}의 관계가 ${delta > 0 ? "가까워졌다" : "멀어졌다"}.` });
  return null;
}

function addNpcMemory(run, candidate, decisionNo, sequence, events, summary = null) {
  const npc = entity(run, candidate.actorId);
  if (!npc || npc.kind !== "npc") return "MEMORY_PRECONDITION_FAILED";
  const text = summary || `${npc.name}은 플레이어의 ${decisionNo}번째 선택이 주변 인물에게 남긴 결과를 기억한다.`;
  const memory = { id: deterministicUuid(`${run.id}:scene-memory:${decisionNo}:${npc.id}:${run.npcMemories.length}`), npcId: npc.id, summary: text, importance: 0.6, ttlTurns: 12, createdTurn: run.currentTurn, createdDecisionNo: decisionNo };
  run.npcMemories.push(memory);
  events.push({ type: "npc_memory_added", memoryId: memory.id, npcId: npc.id, summary: memory.summary, sceneDecisionNo: decisionNo });
  sequence.push({ sequence: sequence.length + 1, type: "MEMORY", actorId: npc.id, targetId: run.playerEntityId, text: `${npc.name}이 이번 선택을 기억했다.` });
  return null;
}

function createHook(run, candidate, decisionNo, sequence, events) {
  const npc = entity(run, candidate.actorId);
  if (!npc || npc.kind !== "npc") return "HOOK_PRECONDITION_FAILED";
  const state = ensureDirectorState(run);
  const pending = candidate.pendingId ? state.pendingConsequences.find((item) => item.id === candidate.pendingId && item.status === "pending") : null;
  if (candidate.pendingId && !pending) return "PENDING_CONSEQUENCE_MISSING";
  const hook = {
    id: deterministicUuid(`${run.id}:scene-hook:${decisionNo}:${npc.id}`),
    summary: pending ? `${npc.name}이 전한 과거 선택의 역류: ${pending.sourceActionType}` : `${npc.name}이 남긴 미해결 신호`, status: "open", createdTurn: run.currentTurn,
    createdDecisionNo: decisionNo, expiresTurn: Math.min(run.turnLimit, run.currentTurn + 8), source: "scene_director"
  };
  run.openLoops.push(hook);
  run.unresolvedHooks.push(clone(hook));
  if (pending) Object.assign(pending, { status: "resolved", resolvedDecisionNo: decisionNo, resolvedHookId: hook.id });
  events.push({ type: "open_loop_created", loopId: hook.id, summary: hook.summary, sceneDecisionNo: decisionNo });
  sequence.push({ sequence: sequence.length + 1, type: "HOOK", actorId: npc.id, targetId: run.playerEntityId, text: `${npc.name}이 새로운 복선을 남겼다.` });
  return null;
}

function advanceQuest(run, candidate, decisionNo, sequence, events) {
  const quest = run.activeQuests.find((item) => item.id === candidate.questId && item.status === "active" && item.acceptsNewSteps !== false);
  if (!quest) return "QUEST_PRECONDITION_FAILED";
  quest.currentStep = `scene-decision-${decisionNo}`;
  quest.lastAdvancedTurn = run.currentTurn;
  events.push({ type: "quest_updated", questId: quest.id, currentStep: quest.currentStep, sceneDecisionNo: decisionNo });
  sequence.push({ sequence: sequence.length + 1, type: "QUEST", actorId: candidate.actorId, targetId: run.playerEntityId, text: `${quest.title}의 다음 단서가 열렸다.` });
  return null;
}

function startQuest(run, candidate, decisionNo, sequence, events) {
  if (!candidate.questId || run.activeQuests.some((item) => item.key === candidate.questId || item.id === candidate.questId)) return "QUEST_PRECONDITION_FAILED";
  const quest = { id: deterministicUuid(`${run.id}:scene-quest:${decisionNo}:${candidate.questId}`), key: candidate.questId, title: "갈라진 실행 경로", summary: "현재 거시 골조 안에서 선택의 파장을 추적한다.", status: "active", questKind: "scene", currentStep: "discover", acceptsNewSteps: true, createdTurn: run.currentTurn, sourceNpcId: candidate.actorId };
  run.activeQuests.push(quest);
  events.push({ type: "quest_started", questId: quest.id, key: quest.key, sceneDecisionNo: decisionNo });
  sequence.push({ sequence: sequence.length + 1, type: "QUEST", actorId: candidate.actorId, targetId: run.playerEntityId, text: `${quest.title} 퀘스트가 시작됐다.` });
  return null;
}

function grantSpecialReward(run, candidate, decisionNo, sequence, events) {
  const source = entity(run, candidate.actorId);
  const template = specialSkillTemplate(candidate.rewardId);
  const state = ensureDirectorState(run);
  if (!source || source.kind !== "npc" || !template || state.specialSkills.some((skill) => skill.templateId === template.id)) return "REWARD_PRECONDITION_FAILED";
  if (!template.modifierIds.every((id) => SPECIAL_SKILL_MODIFIERS[id])) return "REWARD_MODIFIER_FORBIDDEN";
  const skill = {
    id: deterministicUuid(`${run.id}:special-skill:${template.id}`), templateId: template.id,
    baseSkill: template.baseSkill, name: template.name, modifierIds: [...template.modifierIds],
    charges: template.charges, maxCharges: template.charges, sourceNpcId: source.id,
    acquiredTurn: run.currentTurn, acquiredDecisionNo: decisionNo
  };
  state.specialSkills.push(skill);
  events.push({ type: "special_reward_granted", entityId: run.playerEntityId, actorId: source.id, rewardId: skill.id, skillId: skill.baseSkill, sceneDecisionNo: decisionNo });
  sequence.push({ sequence: sequence.length + 1, type: "REWARD", actorId: source.id, targetId: run.playerEntityId, rewardId: skill.id, text: `${source.name}에게서 특수 스킬 ‘${skill.name}’을 얻었다.` });
  return null;
}

function startEncounter(run, candidate, decisionNo, sequence, events) {
  if (!run.activeEncounter || run.activeEncounter.status !== "active") return "ENCOUNTER_PRECONDITION_FAILED";
  events.push({ type: "scene_encounter_presented", encounterId: run.activeEncounter.id, sceneDecisionNo: decisionNo });
  sequence.push({ sequence: sequence.length + 1, type: "ENCOUNTER", actorId: candidate.actorId, targetId: run.playerEntityId, text: "위험 요소가 길을 막아 전투 라운드가 열렸다." });
  return null;
}

function noEvent(sequence) {
  sequence.push({ sequence: sequence.length + 1, type: "AMBIENT", actorId: null, targetId: null, text: "주변은 조용하지만, 방금 선택의 흔적은 세계에 남았다." });
  return null;
}

export function applyScenePlan(run, { candidates, plan, decisionType, now = new Date().toISOString() }) {
  const state = ensureDirectorState(run);
  const decisionNo = state.decisionNo + 1;
  const candidatesById = new Map(candidates.map((candidate) => [candidate.candidateId, candidate]));
  const sequence = [];
  const events = [];
  const appliedActionIds = [];
  const rejectedActions = [];
  const actorActions = new Map();
  let spent = 0;
  const orderedSelections = plan.selectedActionIds
    .map((actionId) => {
      const candidate = candidatesById.get(actionId);
      return { actionId, candidate, initiative: candidate ? initiative(run, candidate, decisionNo) : { score: -100, roll: 0 } };
    })
    .sort((left, right) => right.initiative.score - left.initiative.score || left.actionId.localeCompare(right.actionId));
  for (const selection of orderedSelections) {
    const { actionId, initiative: actionInitiative } = selection;
    const candidate = candidatesById.get(actionId);
    if (!candidate) { rejectedActions.push({ actionId, reason: "CANDIDATE_MISSING" }); continue; }
    if (spent + candidate.cost > 4) { rejectedActions.push({ actionId, reason: "SCENE_BUDGET_EXCEEDED" }); continue; }
    const actor = candidate.actorId ? entity(run, candidate.actorId) : null;
    const actorBudget = isBoss(actor) ? 2 : 1;
    const isMechanical = !["START_DIALOGUE", "NO_EVENT", "START_ENCOUNTER"].includes(candidate.type);
    if (isMechanical && candidate.actorId && Number(actorActions.get(candidate.actorId) || 0) >= actorBudget) {
      rejectedActions.push({ actionId, reason: "ACTOR_ACTION_BUDGET_EXCEEDED" });
      continue;
    }
    const sequenceBefore = sequence.length;
    let reason = null;
    if (candidate.type === "ATTACK_ENTITY") reason = attack(run, candidate, decisionNo, sequence, events);
    else if (candidate.type === "MOVE_ACTOR") reason = moveActor(run, candidate, decisionNo, sequence, events);
    else if (candidate.type === "DEFEND_ENTITY") reason = defendEntity(run, candidate, decisionNo, sequence, events);
    else if (candidate.type === "ASSIST_ENTITY") reason = assistEntity(run, candidate, decisionNo, sequence, events);
    else if (candidate.type === "FLEE_ENTITY") reason = fleeEntity(run, candidate, decisionNo, sequence, events);
    else if (["LOOT_PROP", "OPEN_PROP"].includes(candidate.type)) reason = lootProp(run, candidate, decisionNo, sequence, events);
    else if (candidate.type === "REVEAL_CLUE") reason = revealClue(run, candidate, decisionNo, sequence, events);
    else if (candidate.type === "SPAWN_FROM_SLOT") reason = spawnFromSlot(run, candidate, decisionNo, sequence, events);
    else if (candidate.type === "CHANGE_RELATIONSHIP") reason = changeRelationship(run, candidate, decisionNo, sequence, events);
    else if (candidate.type === "ADD_NPC_MEMORY") reason = addNpcMemory(run, candidate, decisionNo, sequence, events);
    else if (candidate.type === "CREATE_HOOK") reason = createHook(run, candidate, decisionNo, sequence, events);
    else if (candidate.type === "START_QUEST") reason = startQuest(run, candidate, decisionNo, sequence, events);
    else if (candidate.type === "ADVANCE_QUEST") reason = advanceQuest(run, candidate, decisionNo, sequence, events);
    else if (candidate.type === "GRANT_SPECIAL_REWARD") reason = grantSpecialReward(run, candidate, decisionNo, sequence, events);
    else if (candidate.type === "START_ENCOUNTER") reason = startEncounter(run, candidate, decisionNo, sequence, events);
    else if (candidate.type === "START_DIALOGUE") reason = null;
    else if (candidate.type === "NO_EVENT") reason = noEvent(sequence);
    else reason = "ACTION_NOT_IMPLEMENTED";
    if (reason) { rejectedActions.push({ actionId, reason }); continue; }
    if (isMechanical && candidate.actorId) actorActions.set(candidate.actorId, Number(actorActions.get(candidate.actorId) || 0) + 1);
    for (let index = sequenceBefore; index < sequence.length; index += 1) {
      sequence[index].initiative = actionInitiative.score;
      sequence[index].initiativeRoll = actionInitiative.roll;
    }
    appliedActionIds.push(actionId);
    queuePendingConsequence(run, candidate, decisionNo);
    spent += candidate.cost;
    if (COOLDOWNS[candidate.type]) state.eventCooldowns[candidate.type] = COOLDOWNS[candidate.type];
  }

  for (const dialogue of plan.dialogue || []) {
    const speaker = entity(run, dialogue.speakerId);
    if (!speaker || speaker.kind !== "npc" || speaker.state?.disabled) {
      rejectedActions.push({ actionId: null, reason: "DIALOGUE_SPEAKER_UNAVAILABLE" });
      continue;
    }
    const memory = { id: deterministicUuid(`${run.id}:scene-memory:${decisionNo}:${speaker.id}:${sequence.length}`), npcId: speaker.id, summary: dialogue.line, importance: 0.6, ttlTurns: 12, createdTurn: run.currentTurn, createdDecisionNo: decisionNo };
    run.npcMemories.push(memory);
    events.push({ type: "npc_memory_added", memoryId: memory.id, npcId: speaker.id, summary: memory.summary, sceneDecisionNo: decisionNo });
    sequence.push({ sequence: sequence.length + 1, type: "DIALOGUE", actorId: speaker.id, targetId: run.playerEntityId, speakerId: speaker.id, line: dialogue.line, text: dialogue.line });
  }

  const player = entity(run, run.playerEntityId);
  const nearbyHostiles = run.entities.filter((item) => item.active && item.kind === "enemy" && !item.state?.disabled && !item.state?.fled && player && manhattan(item.position, player.position) <= 6);
  if (nearbyHostiles.length === 0 && sequence.some((item) => ["ATTACK", "FLEE"].includes(item.type))) {
    events.push({ type: "scene_combat_resolved", decisionNo, reason: "no_active_hostiles_in_scene" });
    sequence.push({ sequence: sequence.length + 1, type: "ENCOUNTER_END", actorId: null, targetId: run.playerEntityId, text: "주변의 적대 개체가 사라져 전투 라운드가 끝났다." });
  }

  state.decisionNo = decisionNo;
  const sceneTypes = sequence.map((item) => item.type);
  state.recentSceneTypes = [...state.recentSceneTypes, ...sceneTypes].slice(-8);
  state.lastScene = { decisionNo, decisionType, sceneGoal: plan.sceneGoal, sceneTypes, appliedActionIds: [...appliedActionIds], rejectedActions: clone(rejectedActions), createdAt: now };
  run.updatedAt = now;
  return {
    decisionNo,
    decisionType,
    sceneGoal: plan.sceneGoal,
    sceneSequence: sequence,
    events,
    appliedActionIds,
    rejectedActions,
    fallbackUsed: plan.fallbackUsed === true,
    model: plan.model || "validated-scene-director"
  };
}
