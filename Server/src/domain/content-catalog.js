export const CORE_NPC_CATALOG = Object.freeze([
  Object.freeze({ id: "NPC_COMMENT", name: "코멘트", assetId: "npc.villager2.v1", roleTags: ["GUIDE", "WITNESS"], factionId: "NEUTRAL_BROKERS", goal: "넙죽이가 코드리아의 규칙을 이해하도록 돕는다.", motivation: "잘못 지워진 설명도 세계를 구할 수 있다고 믿는다." }),
  Object.freeze({ id: "NPC_SEMICOLON", name: "세미콜론", assetId: "npc.noble.v1", roleTags: ["BROKER", "MEDIATOR"], factionId: "NEUTRAL_BROKERS", goal: "충돌하는 프로세스 사이에 실행 가능한 타협을 만든다.", motivation: "끝과 계속 사이의 경계를 지키려 한다." }),
  Object.freeze({ id: "NPC_CLEANUP", name: "감사관장 클린업", assetId: "npc.samurai.v1", roleTags: ["AUDITOR", "COMBATANT"], factionId: "AUDITORS", goal: "관리자 통제에서 벗어난 코드를 정리한다.", motivation: "통제되지 않은 자유가 다시 붕괴를 부른다고 믿는다." }),
  Object.freeze({ id: "NPC_LEGACY", name: "성주 레거시", assetId: "npc.old-man.v1", roleTags: ["OLD_GUARD", "ARCHIVIST"], factionId: "OLD_GUARD", goal: "삭제되기 직전의 오래된 약속을 보존한다.", motivation: "기술 부채에도 누군가의 삶이 남아 있다고 믿는다." }),
  Object.freeze({ id: "NPC_INDEX", name: "인덱스", assetId: "npc.princess.v1", roleTags: ["ARCHIVIST", "ROOT_WITNESS"], factionId: "NEUTRAL_BROKERS", goal: "흩어진 진실을 루트 시스템까지 연결한다.", motivation: "찾을 수 없는 기억은 존재하지 않는 것과 같다고 두려워한다." }),
  Object.freeze({ id: "NPC_CACHE", name: "캐시", assetId: "npc.villager5.v1", roleTags: ["SURVIVOR", "SCOUT"], factionId: "OLD_GUARD", goal: "붕괴 지역의 생존자를 안전한 길로 이끈다.", motivation: "한 번 구한 사람을 다시 잃지 않으려 한다." })
]);

export const NPC_CATALOG = Object.freeze([
  Object.freeze({ assetId: "npc.villager.green.v1", roleTags: ["RESIDENT", "MERCHANT", "WITNESS"] }),
  Object.freeze({ assetId: "npc.villager2.v1", roleTags: ["GUIDE", "REFUGEE", "WITNESS"] }),
  Object.freeze({ assetId: "npc.villager3.v1", roleTags: ["RESIDENT", "TECHNICIAN"] }),
  Object.freeze({ assetId: "npc.villager4.v1", roleTags: ["MERCHANT", "SCOUT"] }),
  Object.freeze({ assetId: "npc.villager5.v1", roleTags: ["SURVIVOR", "SCOUT"] }),
  Object.freeze({ assetId: "npc.villager6.v1", roleTags: ["REFUGEE", "WITNESS"] }),
  Object.freeze({ assetId: "npc.old-man.v1", roleTags: ["TECHNICIAN", "ARCHIVIST", "HERMIT"] }),
  Object.freeze({ assetId: "npc.noble.v1", roleTags: ["BROKER", "JUDGE", "ADMINISTRATOR"] }),
  Object.freeze({ assetId: "npc.princess.v1", roleTags: ["ARCHIVIST", "HIGH_PROCESS"] }),
  Object.freeze({ assetId: "npc.samurai.v1", roleTags: ["AUDITOR", "GUARD", "COMBATANT"] })
]);

export const MONSTER_CATALOG = Object.freeze([
  Object.freeze({ assetId: "enemy.slime.blue.v1", name: "캐시 누수 슬라임", hp: 5, speed: 2, traits: ["LOOTER"] }),
  Object.freeze({ assetId: "enemy.slime.green.v1", name: "재생 누수 슬라임", hp: 5, speed: 2, traits: ["SUPPORT"] }),
  Object.freeze({ assetId: "enemy.mushroom.v1", name: "포자 예외", hp: 6, speed: 1, traits: ["GUARDIAN"] }),
  Object.freeze({ assetId: "enemy.blue-bat.v1", name: "기억 탈취 박쥐", hp: 4, speed: 4, traits: ["AMBUSHER", "MEMORY_DRAIN"] }),
  Object.freeze({ assetId: "enemy.bear.v1", name: "과부하 베어", hp: 8, speed: 2, traits: ["BERSERK"] }),
  Object.freeze({ assetId: "enemy.cyclope.v1", name: "감시 사이클롭스", hp: 7, speed: 2, traits: ["GUARDIAN", "AUDITOR_ALIGNED"] }),
  Object.freeze({ assetId: "enemy.dragon.v1", name: "재귀 드래곤", hp: 9, speed: 3, traits: ["BERSERK", "SUMMONER"] }),
  Object.freeze({ assetId: "enemy.kappa-green.v1", name: "버퍼 카파", hp: 6, speed: 2, traits: ["MEMORY_DRAIN", "SUPPORT"] }),
  Object.freeze({ assetId: "enemy.snake.v1", name: "분기 스네이크", hp: 5, speed: 4, traits: ["AMBUSHER", "FLEE_AT_LOW_HP"] }),
  Object.freeze({ assetId: "enemy.spider-red.v1", name: "데드락 스파이더", hp: 7, speed: 3, traits: ["GUARDIAN", "OLD_GUARD_ALIGNED"] })
]);

export const BOSS_CATALOG = Object.freeze([
  Object.freeze({ assetId: "boss.giant-slime.v1", name: "거대 캐시 누수", roles: ["LOCAL_STAKES"], minMacroOrder: 2, hp: 16, speed: 2, traits: ["LOOTER", "SUMMONER"], patterns: ["CHARGE", "SUMMON"] }),
  Object.freeze({ assetId: "boss.giant-slime-2.v1", name: "거대 재생 누수", roles: ["LOCAL_STAKES"], minMacroOrder: 2, hp: 17, speed: 2, traits: ["SUPPORT", "SUMMONER"], patterns: ["CHARGE", "RESTORE"] }),
  Object.freeze({ assetId: "boss.giant-frog.v1", name: "버그 포레스트 포식자", roles: ["LOCAL_STAKES"], minMacroOrder: 2, hp: 18, speed: 3, traits: ["AMBUSHER"], patterns: ["JUMP", "TONGUE"] }),
  Object.freeze({ assetId: "boss.giant-frog-2.v1", name: "오염된 포레스트 포식자", roles: ["LOCAL_STAKES"], minMacroOrder: 2, hp: 19, speed: 3, traits: ["AMBUSHER", "BERSERK"], patterns: ["JUMP", "CHARGE"] }),
  Object.freeze({ assetId: "boss.giant-racoon.v1", name: "버퍼 약탈왕", roles: ["RELATIONSHIP_CONFLICT"], minMacroOrder: 3, hp: 18, speed: 3, traits: ["LOOTER"], patterns: ["CHARGE", "STEAL"] }),
  Object.freeze({ assetId: "boss.giant-racoon-gold.v1", name: "황금 버퍼 약탈왕", roles: ["RELATIONSHIP_CONFLICT"], minMacroOrder: 3, hp: 20, speed: 3, traits: ["LOOTER", "GUARDIAN"], patterns: ["CHARGE", "STEAL"] }),
  Object.freeze({ assetId: "boss.giant-blue-samurai.v1", name: "감사관장 클린업", roles: ["RELATIONSHIP_CONFLICT", "CONSEQUENCE_RETURN"], minMacroOrder: 3, hp: 22, speed: 3, traits: ["AUDITOR_ALIGNED", "GUARDIAN"], patterns: ["CHARGE", "DOUBLE_SLASH"] }),
  Object.freeze({ assetId: "boss.giant-red-samurai.v1", name: "격리 집행자", roles: ["RELATIONSHIP_CONFLICT", "CONSEQUENCE_RETURN"], minMacroOrder: 3, hp: 22, speed: 3, traits: ["AUDITOR_ALIGNED", "BERSERK"], patterns: ["CHARGE", "DOUBLE_SLASH"] }),
  Object.freeze({ assetId: "boss.squid-green.v1", name: "대도서관 인덱서", roles: ["HIDDEN_TRUTH"], minMacroOrder: 4, hp: 19, speed: 2, traits: ["MEMORY_DRAIN", "SUMMONER"], patterns: ["SHOOT", "BIND"] }),
  Object.freeze({ assetId: "boss.squid-red.v1", name: "대도서관 말소자", roles: ["HIDDEN_TRUTH"], minMacroOrder: 4, hp: 20, speed: 2, traits: ["MEMORY_DRAIN", "BERSERK"], patterns: ["SHOOT", "BIND"] }),
  Object.freeze({ assetId: "boss.giant-spirit.v1", name: "루트 기억 파편", roles: ["HIDDEN_TRUTH", "FINAL_CONVERGENCE"], minMacroOrder: 4, hp: 21, speed: 3, traits: ["MEMORY_DRAIN", "SUPPORT"], patterns: ["TRANSFORM", "SHOOT"] }),
  Object.freeze({ assetId: "boss.demon-cyclop.v1", name: "레거시 감시 악마", roles: ["CONSEQUENCE_RETURN"], minMacroOrder: 5, hp: 23, speed: 2, traits: ["OLD_GUARD_ALIGNED", "GUARDIAN"], patterns: ["CHARGE", "LASER"] }),
  Object.freeze({ assetId: "boss.demon-cyclop-2.v1", name: "손상된 레거시 감시 악마", roles: ["CONSEQUENCE_RETURN"], minMacroOrder: 5, hp: 24, speed: 2, traits: ["OLD_GUARD_ALIGNED", "BERSERK"], patterns: ["CHARGE", "LASER"] }),
  Object.freeze({ assetId: "boss.giant-bamboo.v1", name: "레거시 성채 죽림", roles: ["CONSEQUENCE_RETURN"], minMacroOrder: 5, hp: 24, speed: 1, traits: ["OLD_GUARD_ALIGNED", "GUARDIAN"], patterns: ["CHARGE", "ROOT"] }),
  Object.freeze({ assetId: "boss.giant-bamboo-2.v1", name: "기술 부채 죽림", roles: ["CONSEQUENCE_RETURN"], minMacroOrder: 5, hp: 25, speed: 1, traits: ["OLD_GUARD_ALIGNED", "SUMMONER"], patterns: ["CHARGE", "ROOT"] }),
  Object.freeze({ assetId: "boss.tengu-blue.v1", name: "감사관단 천구", roles: ["CONSEQUENCE_RETURN", "FINAL_CONVERGENCE"], minMacroOrder: 5, hp: 23, speed: 4, traits: ["AUDITOR_ALIGNED", "AMBUSHER"], patterns: ["JUMP", "SHOOT"] }),
  Object.freeze({ assetId: "boss.tengu-red.v1", name: "폭주 감사관 천구", roles: ["CONSEQUENCE_RETURN", "FINAL_CONVERGENCE"], minMacroOrder: 5, hp: 24, speed: 4, traits: ["AUDITOR_ALIGNED", "BERSERK"], patterns: ["JUMP", "SHOOT"] }),
  Object.freeze({ assetId: "boss.giant-flam.v1", name: "루트 화염 예외", roles: ["FINAL_CONVERGENCE"], minMacroOrder: 7, hp: 26, speed: 3, traits: ["BERSERK", "SUMMONER"], patterns: ["TRANSFORM", "CHARGE"] })
]);

export const SPECIAL_SKILL_MODIFIERS = Object.freeze({
  EXTRA_TARGET: Object.freeze({ id: "EXTRA_TARGET", maxStack: 1 }),
  REDUCED_FOCUS_COST: Object.freeze({ id: "REDUCED_FOCUS_COST", focusDelta: -1, maxStack: 1 }),
  DELAYED_EFFECT: Object.freeze({ id: "DELAYED_EFFECT", delayDecisions: 1, maxStack: 1 }),
  TEMPORARY_CLONE: Object.freeze({ id: "TEMPORARY_CLONE", durationTurns: 3, maxStack: 1 }),
  MEMORY_RESTORE: Object.freeze({ id: "MEMORY_RESTORE", memories: 1, maxStack: 1 }),
  FACTION_BONUS: Object.freeze({ id: "FACTION_BONUS", modifier: 2, maxStack: 1 }),
  MONSTER_TYPE_BONUS: Object.freeze({ id: "MONSTER_TYPE_BONUS", damage: 1, maxStack: 1 }),
  HEALTH_INSTEAD_OF_FOCUS: Object.freeze({ id: "HEALTH_INSTEAD_OF_FOCUS", healthCost: 1, maxStack: 1 }),
  LIMITED_CHARGES: Object.freeze({ id: "LIMITED_CHARGES", charges: 2, maxStack: 1 }),
  ONE_RUN_ONLY: Object.freeze({ id: "ONE_RUN_ONLY", charges: 1, maxStack: 1 })
});

export const SPECIAL_SKILL_TEMPLATES = Object.freeze([
  Object.freeze({ id: "RUN_SKILL_ECHO_CHAIN", baseSkill: "CONNECT", name: "메아리 사슬", modifierIds: ["EXTRA_TARGET", "LIMITED_CHARGES"], charges: 2 }),
  Object.freeze({ id: "RUN_SKILL_LIGHTWEIGHT_COPY", baseSkill: "COPY", name: "경량 복제", modifierIds: ["REDUCED_FOCUS_COST", "TEMPORARY_CLONE", "LIMITED_CHARGES"], charges: 2 }),
  Object.freeze({ id: "RUN_SKILL_MEMORY_RESTORE", baseSkill: "RESTORE", name: "기억 되감기", modifierIds: ["MEMORY_RESTORE", "REDUCED_FOCUS_COST", "ONE_RUN_ONLY"], charges: 1 }),
  Object.freeze({ id: "RUN_SKILL_AUDIT_DELETE", baseSkill: "DELETE", name: "감사관의 예외 처리", modifierIds: ["MONSTER_TYPE_BONUS", "LIMITED_CHARGES"], charges: 2 }),
  Object.freeze({ id: "RUN_SKILL_DEBT_UNDO", baseSkill: "UNDO", name: "부채의 역류", modifierIds: ["HEALTH_INSTEAD_OF_FOCUS", "ONE_RUN_ONLY"], charges: 1 })
]);

export function monsterForAsset(assetId) {
  return MONSTER_CATALOG.find((entry) => entry.assetId === assetId) || null;
}

export function npcForAsset(assetId) {
  return NPC_CATALOG.find((entry) => entry.assetId === assetId) || null;
}

export function bossForAsset(assetId) {
  return BOSS_CATALOG.find((entry) => entry.assetId === assetId) || null;
}

export function bossCandidatesFor({ macroOrder, campaignRole }) {
  return BOSS_CATALOG.filter((entry) => entry.minMacroOrder <= macroOrder &&
    (!campaignRole || entry.roles.includes(campaignRole) || entry.roles.includes("FINAL_CONVERGENCE")));
}

export function specialSkillTemplate(id) {
  return SPECIAL_SKILL_TEMPLATES.find((entry) => entry.id === id) || null;
}
