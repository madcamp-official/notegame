import { createHash } from "node:crypto";
import { assert } from "../errors.js";
import {
  ADMIN_ACCESS_LEVELS,
  ADMIN_KEYBOARD,
  ADMIN_KEYBOARD_NAME_KO,
  CAMPAIGN_REGION_AXES,
  GAME_TITLE,
  PROTAGONIST_NAME_KO,
  PROTAGONIST_NUPJUKYI,
  WORLD_CODRIA,
  WORLD_NAME_KO,
  endingFactors
} from "./codria-contract.js";
import { clone, deterministicUuid, fingerprint } from "./serialization.js";

export const CAMPAIGN_TEMPLATE_VERSION = "codria-campaign.v4";
export const CAMPAIGN_ARCHETYPE = "codria-admin-keyboard-roguelike";
export const CAMPAIGN_TITLE = GAME_TITLE;
export const WORLD_NAME = WORLD_NAME_KO;

export const GENERATIVE_ROLE_IDS = Object.freeze([
  "ARRIVAL_CATALYST",
  "LOCAL_STAKES",
  "RELATIONSHIP_CONFLICT",
  "HIDDEN_TRUTH",
  "CONSEQUENCE_RETURN",
  "FINAL_CONVERGENCE"
]);

export const PROGRESS_TOKEN_DEFINITIONS = Object.freeze([
  Object.freeze({ ...ADMIN_ACCESS_LEVELS[0], name: ADMIN_ACCESS_LEVELS[0].nameKo, meaning: "코드리아의 지역 문제를 해결해 획득한 첫 관리자 권한", progressLevel: 1, sourceRole: "LOCAL_STAKES" }),
  Object.freeze({ ...ADMIN_ACCESS_LEVELS[1], name: ADMIN_ACCESS_LEVELS[1].nameKo, meaning: "서로 충돌하는 객체 관계를 조정해 획득한 둘째 관리자 권한", progressLevel: 2, sourceRole: "RELATIONSHIP_CONFLICT" }),
  Object.freeze({ ...ADMIN_ACCESS_LEVELS[2], name: ADMIN_ACCESS_LEVELS[2].nameKo, meaning: "기술 부채의 결과를 책임지고 획득한 셋째 관리자 권한", progressLevel: 3, sourceRole: "CONSEQUENCE_RETURN" })
]);

export const METRIC_KEYS = Object.freeze(["worldStability", "worldAutonomy", "publicTrust", "technicalDebt", "companionBond", "turnPressure"]);

export const CAMPAIGN_PHASES = Object.freeze([
  Object.freeze({ id: "codria_crash", name: "Codria crash", nameKo: "코드리아 추락과 관리자 키보드 각성" }),
  Object.freeze({ id: "first_region_problem", name: "First region problem", nameKo: "붕괴 현상과 첫 지역 문제" }),
  Object.freeze({ id: "admin_access_1", name: "Admin access I", nameKo: "관리자 권한 I 획득" }),
  Object.freeze({ id: "admin_access_2", name: "Admin access II", nameKo: "관리자 권한 II 획득" }),
  Object.freeze({ id: "internal_cause", name: "Internal cause", nameKo: "관리자 통제 시스템 내부 원인 확인" }),
  Object.freeze({ id: "technical_debt_return", name: "Technical debt return", nameKo: "기술 부채와 과거 선택의 역류" }),
  Object.freeze({ id: "admin_access_3", name: "Admin access III", nameKo: "관리자 권한 III 획득" }),
  Object.freeze({ id: "root_system_entry", name: "Root System entry", nameKo: "루트 시스템 진입" }),
  Object.freeze({ id: "final_deployment", name: "Final deployment", nameKo: "최종 배치와 결말" })
]);

// The nine internal beats remain useful as deterministic progression checkpoints, while
// these seven macro phases are the immutable story spine exposed to the scene director.
export const CAMPAIGN_MACRO_PHASES = Object.freeze([
  Object.freeze({ id: "MACRO_ARRIVAL_AWAKENING", order: 1, nameKo: "코드리아 추락과 관리자 키보드 각성" }),
  Object.freeze({ id: "MACRO_ADMIN_ACCESS_1", order: 2, nameKo: "관리자 권한 I" }),
  Object.freeze({ id: "MACRO_ADMIN_ACCESS_2", order: 3, nameKo: "관리자 권한 II" }),
  Object.freeze({ id: "MACRO_COLLAPSE_TRUTH", order: 4, nameKo: "붕괴 원인의 진실" }),
  Object.freeze({ id: "MACRO_TECHNICAL_DEBT_RETURN", order: 5, nameKo: "기술 부채와 과거 선택의 역류" }),
  Object.freeze({ id: "MACRO_ADMIN_ACCESS_3", order: 6, nameKo: "관리자 권한 III" }),
  Object.freeze({ id: "MACRO_ROOT_DECISION", order: 7, nameKo: "루트 시스템과 최종 결정" })
]);

const MACRO_PHASE_BY_BEAT = Object.freeze({
  "beat.codria_crash": "MACRO_ARRIVAL_AWAKENING",
  "beat.first_region_problem": "MACRO_ARRIVAL_AWAKENING",
  "beat.admin_access_1": "MACRO_ADMIN_ACCESS_1",
  "beat.admin_access_2": "MACRO_ADMIN_ACCESS_2",
  "beat.internal_cause": "MACRO_COLLAPSE_TRUTH",
  "beat.technical_debt_return": "MACRO_TECHNICAL_DEBT_RETURN",
  "beat.admin_access_3": "MACRO_ADMIN_ACCESS_3",
  "beat.root_system_entry": "MACRO_ROOT_DECISION",
  "beat.final_deployment": "MACRO_ROOT_DECISION"
});

export function macroPhaseForBeat(beatOrId) {
  const id = typeof beatOrId === "string" ? beatOrId : beatOrId?.id;
  return CAMPAIGN_MACRO_PHASES.find((phase) => phase.id === MACRO_PHASE_BY_BEAT[id]) || CAMPAIGN_MACRO_PHASES[0];
}

const GENERATIVE_ALLOWED_ABILITIES = Object.freeze({
  ARRIVAL_CATALYST: Object.freeze(["search"]),
  LOCAL_STAKES: Object.freeze(["search", "restore"]),
  RELATIONSHIP_CONFLICT: Object.freeze(["connect", "restore"]),
  HIDDEN_TRUTH: Object.freeze(["search", "connect"]),
  CONSEQUENCE_RETURN: Object.freeze(["restore", "undo"]),
  FINAL_CONVERGENCE: Object.freeze(["connect", "delete"])
});

export const CAMPAIGN_ALLOWED_ABILITIES_BY_ROLE = GENERATIVE_ALLOWED_ABILITIES;

const MOTIFS = Object.freeze([
  { id: "borrowed_names", noun: "빌린 이름", image: "이름을 잃을수록 길이 선명해지는 표식" },
  { id: "sleeping_bells", noun: "잠든 종", image: "누군가의 약속에만 울리는 종소리" },
  { id: "glass_rain", noun: "유리비", image: "기억을 비추지만 손대면 갈라지는 비" },
  { id: "wandering_stars", noun: "떠도는 별", image: "선택한 사람의 뒤를 따라 이동하는 별자리" },
  { id: "ink_tides", noun: "잉크 조수", image: "말하지 않은 문장을 해안에 남기는 조수" },
  { id: "moss_letters", noun: "이끼 문자", image: "오래된 후회를 먹고 자라는 문자" },
  { id: "thread_moon", noun: "실타래 달", image: "관계가 얽힐수록 조각나는 달빛" },
  { id: "echo_keys", noun: "메아리 열쇠", image: "한 번의 행동을 다른 장소에서 되풀이하는 열쇠" }
]);
const CRISES = Object.freeze([
  { id: "vanishing_paths", label: "밤마다 생활로가 하나씩 사라지는 현상", source: "서로 다른 안전을 바란 두 약속의 충돌" },
  { id: "frozen_seasons", label: "계절이 한 구역에 붙잡혀 주민의 시간이 어긋나는 현상", source: "떠나지 않으려는 기억과 앞으로 가려는 삶의 마찰" },
  { id: "borrowed_voices", label: "사람들의 목소리가 낯선 이에게 옮겨가는 현상", source: "감춰 둔 진실을 대신 말하게 하려던 오래된 의식" },
  { id: "hollow_harvest", label: "수확물이 안쪽부터 이야기로 변해 사라지는 현상", source: "결과 없는 풍요를 요구했던 공동체의 계약" },
  { id: "repeating_dawn", label: "같은 새벽이 반복되어 약속이 닳아가는 현상", source: "실패를 지우려던 수호자의 미완성 선택" },
  { id: "mirror_migration", label: "거울 속 거주지가 현실의 마을과 자리를 바꾸는 현상", source: "누가 진짜 고향을 가질지 미뤄 둔 합의" }
]);
const COMMUNITIES = Object.freeze(["등불 운반자", "떠돌이 제본사", "비늘 농부", "구름 우편배달부", "기억 양봉가", "종탑 잠수부", "그림자 도예가", "새벽 지도사"]);
const DILEMMAS = Object.freeze([
  "한 사람의 완전한 귀환과 공동체의 불완전한 미래 중 무엇을 지킬 것인가",
  "안전을 위해 세계의 자유를 묶을 것인가, 위험을 감수하고 선택권을 돌려줄 것인가",
  "상처 난 기억을 보존할 것인가, 치유를 위해 일부를 놓아줄 것인가",
  "지금의 주민과 아직 태어나지 않은 가능성 중 누구의 약속을 우선할 것인가",
  "떠날 수 있는 문을 닫고 세계를 지킬 것인가, 문을 열어 변화의 대가를 감수할 것인가"
]);
const NPC_GIVEN_NAMES = Object.freeze(["라온", "모라", "해윰", "누리", "이든", "세온", "아라", "비오", "루미", "여울", "다온", "미르", "소담", "하람", "시안", "온결", "로하", "윤슬"]);
const NPC_EPITHETS = Object.freeze(["금 간 나침반", "느린 종", "푸른 실", "마지막 등불", "거꾸로 쓴 편지", "작은 폭풍", "침묵의 지도", "따뜻한 열쇠"]);
const ROLE_BLUEPRINTS = Object.freeze([
  { id: "ARRIVAL_CATALYST", npcRole: "낯선 이를 가장 먼저 알아본 길잡이", evidenceKey: "ARRIVAL_GUIDE", beatId: "beat.arrival_catalyst" },
  { id: "LOCAL_STAKES", npcRole: "생활의 피해를 기록하는 공동체 대표", evidenceKey: "ADMIN_ACCESS_LEVEL_1", beatId: "beat.local_stakes" },
  { id: "RELATIONSHIP_CONFLICT", npcRole: "서로 다른 약속 사이에 선 중재자", evidenceKey: "ADMIN_ACCESS_LEVEL_2", beatId: "beat.relationship_conflict" },
  { id: "HIDDEN_TRUTH", npcRole: "감춰진 원인을 간직한 증언자", evidenceKey: "STORY_REVELATION", beatId: "beat.hidden_truth" },
  { id: "CONSEQUENCE_RETURN", npcRole: "이전 선택의 결과를 들고 돌아온 생존자", evidenceKey: "ADMIN_ACCESS_LEVEL_3", beatId: "beat.consequence_return" },
  { id: "FINAL_CONVERGENCE", npcRole: "마지막 선택들의 의미를 묻는 관찰자", evidenceKey: "FINALE_PUZZLE_RESOLVED", beatId: "beat.final_convergence" }
]);

const ENDING_RECIPES = Object.freeze([
  Object.freeze({
    id: "ENDING_REWEAVE_TOGETHER", category: "reconciliation", valence: "success",
    requiredLinks: [["anchor", "safeguard"], ["player", "memory"]], requiredRemoved: ["threat"], requiredActive: ["safeguard", "memory"], forbiddenLinks: [["player", "freedom"]],
    metricConditions: { publicTrust: { min: 45 }, technicalDebt: { max: 65 } }
  }),
  Object.freeze({
    id: "ENDING_OPEN_FRONTIER", category: "freedom", valence: "success",
    requiredLinks: [["anchor", "freedom"]], requiredRemoved: ["threat"], requiredActive: ["freedom", "passage"], forbiddenLinks: [["anchor", "safeguard"]],
    metricConditions: { worldAutonomy: { min: 50 } }
  }),
  Object.freeze({
    id: "ENDING_KEEP_THE_PROMISE", category: "guardianship", valence: "neutral",
    requiredLinks: [["player", "safeguard"], ["memory", "anchor"]], requiredRemoved: ["threat"], requiredActive: ["safeguard", "memory"], forbiddenLinks: [["player", "passage"]],
    metricConditions: { companionBond: { min: 45 } }
  }),
  Object.freeze({
    id: "ENDING_CUT_THE_CYCLE", category: "release", valence: "neutral",
    requiredLinks: [["anchor", "passage"]], requiredRemoved: ["threat", "freedom"], requiredActive: ["passage", "witness"], forbiddenLinks: [["player", "safeguard"]],
    metricConditions: { technicalDebt: { max: 55 } }
  }),
  Object.freeze({
    id: "ENDING_PRESERVE_THE_SCARS", category: "memory", valence: "neutral",
    requiredLinks: [["memory", "safeguard"], ["player", "witness"]], requiredRemoved: ["freedom"], requiredActive: ["memory", "threat", "witness"], forbiddenLinks: [["anchor", "threat"]],
    metricConditions: { worldStability: { min: 35 }, publicTrust: { min: 40 } }
  }),
  Object.freeze({
    id: "ENDING_WALK_BETWEEN_WORLDS", category: "return", valence: "success",
    requiredLinks: [["player", "passage"], ["anchor", "safeguard"]], requiredRemoved: ["threat"], requiredActive: ["passage", "safeguard"], forbiddenLinks: [["player", "freedom"]],
    metricConditions: { worldStability: { min: 45 }, companionBond: { min: 35 } }
  }),
  Object.freeze({
    id: "ENDING_EMERGENCY_WITHDRAWAL", category: "emergency", valence: "failure", emergency: true,
    requiredLinks: [], requiredRemoved: [], requiredActive: [], forbiddenLinks: [], metricConditions: {}
  })
]);

export function campaignArchetypeIds() {
  return [CAMPAIGN_ARCHETYPE];
}

function beatTurns(turnLimit) {
  return [
    1,
    Math.max(3, Math.round(turnLimit * 0.10)),
    Math.max(5, Math.round(turnLimit * 0.18)),
    Math.max(8, Math.round(turnLimit * 0.30)),
    Math.max(12, Math.round(turnLimit * 0.45)),
    Math.max(17, Math.round(turnLimit * 0.60)),
    Math.max(21, Math.round(turnLimit * 0.72)),
    Math.max(25, Math.round(turnLimit * 0.84)),
    Math.max(28, Math.round(turnLimit * 0.94))
  ];
}

function stableInteger(seed, label, modulo) {
  const digest = createHash("sha256").update(`${seed}:${label}`).digest();
  return digest.readUInt32BE(0) % modulo;
}

function pick(seed, label, values) {
  return values[stableInteger(seed, label, values.length)];
}

function seededOrder(seed, label, values) {
  return [...values].sort((left, right) => {
    const leftHash = createHash("sha256").update(`${seed}:${label}:${left.id || left}`).digest("hex");
    const rightHash = createHash("sha256").update(`${seed}:${label}:${right.id || right}`).digest("hex");
    return leftHash.localeCompare(rightHash);
  });
}

function createGenome(worldSeed) {
  const motif = pick(worldSeed, "motif", MOTIFS);
  const crisis = pick(worldSeed, "crisis", CRISES);
  const community = pick(worldSeed, "community", COMMUNITIES);
  const dilemma = pick(worldSeed, "dilemma", DILEMMAS);
  return {
    version: "codria-campaign-genome.v4",
    seed: worldSeed,
    worldId: WORLD_CODRIA,
    worldName: WORLD_NAME_KO,
    protagonistId: PROTAGONIST_NUPJUKYI,
    protagonistName: PROTAGONIST_NAME_KO,
    artifactId: ADMIN_KEYBOARD,
    artifactName: ADMIN_KEYBOARD_NAME_KO,
    title: GAME_TITLE,
    motifId: motif.id,
    motif: motif.noun,
    motifImage: motif.image,
    crisisId: crisis.id,
    crisis: crisis.label,
    hiddenCause: crisis.source,
    community,
    dilemma,
    palette: pick(worldSeed, "palette", ["moonlit_glass", "moss_and_copper", "ink_and_amber", "frosted_lilac", "storm_teal", "sunset_rust"]),
    companionTemperament: pick(worldSeed, "companion", ["겁이 많지만 질문을 멈추지 않는", "농담으로 불안을 숨기는", "모든 약속을 글자로 기록하는", "낯선 이를 먼저 믿어 보는", "위험 앞에서 지나치게 솔직해지는"]),
    endingQuestion: dilemma,
    regionAxes: [...CAMPAIGN_REGION_AXES]
  };
}

function progressTokenDefinitions(worldSeed, genome) {
  const acquisitionMethods = [
    `${genome.community}의 지역 문제를 관리자 키보드로 해결`,
    `${genome.motif} 아래 충돌하던 객체 관계를 조정`,
    `${genome.hiddenCause}에서 비롯된 기술 부채의 결과를 책임지고 복구`
  ];
  return PROGRESS_TOKEN_DEFINITIONS.map((definition, index) => ({
    ...clone(definition),
    name: definition.name,
    nameKo: definition.nameKo,
    meaning: definition.meaning,
    acquisitionMethod: acquisitionMethods[index],
    acquisitionVariant: stableInteger(worldSeed, `access-method:${index}`, 1_000_000)
  }));
}

function npcName(worldSeed, index) {
  const given = pick(worldSeed, `npc-given:${index}`, NPC_GIVEN_NAMES);
  const epithet = pick(worldSeed, `npc-epithet:${index}`, NPC_EPITHETS);
  return `${given}, ${epithet}`;
}

function createNpcRoles(worldSeed, genome) {
  return ROLE_BLUEPRINTS.map((definition, index) => {
    const displayName = npcName(worldSeed, index);
    const role = index === 0 ? `${genome.companionTemperament} 길잡이` : definition.npcRole;
    const contents = [
      `${displayName}은 플레이어의 도착이 ${genome.motif}의 움직임과 겹쳤다고 믿는다.`,
      `${displayName}은 ${genome.crisis} 때문에 무너진 ${genome.community}의 일상을 보여 준다.`,
      `${displayName}은 같은 위기를 두고 갈라진 두 약속 중 어느 쪽도 쉽게 버리지 못한다.`,
      `${displayName}은 위기의 숨은 원인이 '${genome.hiddenCause}'임을 알고 있지만 공개의 대가를 두려워한다.`,
      `${displayName}은 초반 선택이 남긴 이익과 상처를 함께 들고 돌아온다.`,
      `${displayName}은 마지막에 '${genome.dilemma}'라고 묻되 답을 대신 고르지 않는다.`
    ];
    return {
      id: `role.${definition.id.toLowerCase()}`,
      displayName,
      role,
      content: contents[index],
      campaignRole: definition.id,
      evidenceKey: definition.evidenceKey,
      questSeeds: [`quest.${definition.id.toLowerCase()}.a`, `quest.${definition.id.toLowerCase()}.b`]
    };
  });
}

function createQuestSeeds(worldSeed, genome) {
  const localObject = pick(worldSeed, "quest-object", ["금 간 등불", "이름 없는 편지", "계절 씨앗", "기억 벌집", "침묵의 종", "길 잃은 그림자"]);
  const witness = pick(worldSeed, "quest-witness", ["아이들의 놀이", "시장 상인의 장부", "떠나는 사람의 노래", "수호 동물의 흔적", "밤샘 작업자의 지도"]);
  return [
    {
      id: "quest.seed.local_life",
      title: `${localObject}을 일상으로 돌려놓기`,
      description: `${genome.crisis} 속에서도 ${genome.community}가 포기하지 않은 생활의 물건을 찾아 안전하게 되돌린다.`,
      campaignRole: "LOCAL_STAKES",
      suggestedAbilities: ["copy", "restore"],
      horizon: "early"
    },
    {
      id: "quest.seed.shared_witness",
      title: `${witness}에 남은 두 약속`,
      description: `갈등 당사자들이 같은 사건을 다르게 기억하는 이유를 수집하고 둘 모두가 인정할 연결점을 만든다.`,
      campaignRole: "RELATIONSHIP_CONFLICT",
      suggestedAbilities: ["connect", "restore"],
      horizon: "middle"
    },
    {
      id: "quest.seed.returning_cost",
      title: `${genome.motif}가 되돌려 준 것`,
      description: `초반에 미뤄 둔 선택이 어떤 주민에게 돌아갔는지 확인하고 복구하거나 책임질 방법을 찾는다.`,
      campaignRole: "CONSEQUENCE_RETURN",
      suggestedAbilities: ["restore", "undo"],
      horizon: "late"
    }
  ];
}

function createBeats(worldSeed, turnLimit, genome, tokens) {
  const targets = beatTurns(turnLimit);
  const definitions = [
    { id: "beat.codria_crash", role: "ARRIVAL_CATALYST", evidence: "ARRIVAL_GUIDE", abilities: ["search"], title: "코드리아 추락과 각성", description: `넙죽이가 코드리아에 추락해 ${genome.motif}에 반응하는 관리자 키보드를 조사한다.` },
    { id: "beat.first_region_problem", role: "LOCAL_STAKES", evidence: "LOCAL_DEBUG_RECORD", abilities: ["search"], title: "첫 지역의 붕괴", description: `${genome.crisis}으로 무너진 ${genome.community}의 생활과 첫 지역 문제를 조사한다.` },
    { id: "beat.admin_access_1", role: "LOCAL_STAKES", evidence: "ADMIN_ACCESS_LEVEL_1", abilities: ["search", "delete", "connect", "restore"], reward: tokens[0], title: "관리자 권한 I", description: "서로 다른 지역과 방식 중 하나를 선택해 첫 관리자 권한을 획득한다." },
    { id: "beat.admin_access_2", role: "RELATIONSHIP_CONFLICT", evidence: "ADMIN_ACCESS_LEVEL_2", abilities: ["search", "delete", "connect", "restore"], reward: tokens[1], title: "관리자 권한 II", description: "관계의 교착과 지역 선택을 통과해 둘째 관리자 권한을 획득한다." },
    { id: "beat.internal_cause", role: "HIDDEN_TRUTH", evidence: "STORY_REVELATION", abilities: ["search", "connect"], title: "통제 시스템 내부의 원인", description: `붕괴 원인이 관리자 통제 시스템 내부의 '${genome.hiddenCause}'와 연결됐음을 조사한다.` },
    { id: "beat.technical_debt_return", role: "CONSEQUENCE_RETURN", evidence: "LEGACY_RECOVERY_RECORD", abilities: ["connect"], title: "기술 부채의 역류", description: "관리자 키보드 편집이 남긴 기술 부채와 과거 선택의 결과를 주민 협력으로 회수한다." },
    { id: "beat.admin_access_3", role: "CONSEQUENCE_RETURN", evidence: "ADMIN_ACCESS_LEVEL_3", abilities: ["search", "delete", "connect", "restore"], reward: tokens[2], title: "관리자 권한 III", description: "대가를 감수한 해결 방식으로 마지막 관리자 권한을 획득한다." },
    { id: "beat.root_system_entry", role: "FINAL_CONVERGENCE", evidence: "ROOT_SYSTEM_ENTERED", abilities: ["connect", "delete"], title: "루트 시스템 진입", description: "세 관리자 권한과 내부 원인 단서를 사용해 루트 시스템에 진입한다." },
    { id: "beat.final_deployment", role: "FINAL_CONVERGENCE", evidence: "FINALE_PUZZLE_RESOLVED", abilities: ["connect", "delete"], finale: true, title: "최종 배치와 결말", description: `루트 시스템에서 '${genome.dilemma}'라는 질문에 최종 배치로 답한다.` }
  ];
  return definitions.map((definition, index) => {
    const requiredAbility = pick(worldSeed, `beat-ability:${index}`, definition.abilities);
    const reward = definition.reward || null;
    return {
      id: definition.id,
      title: definition.title,
      description: definition.description,
      phaseId: CAMPAIGN_PHASES[index].id,
      macroPhaseId: macroPhaseForBeat(definition.id).id,
      requiredAbility,
      allowedAbilities: [...definition.abilities],
      requiredCampaignRole: definition.role,
      requiredEvidenceKey: definition.evidence,
      requiredEvidencePolicy: definition.finale ? "finale_resolved" : "designated_target",
      recoveryPaths: definition.abilities.map((ability) => ({ ability, requiresDesignatedEvidence: true })),
      rewardProgressToken: reward?.id || null,
      rewardProgressLevel: reward?.progressLevel ?? null,
      targetTurn: targets[index],
      status: index === 0 ? "active" : "pending"
    };
  });
}

function createEndingCandidates(worldSeed, genome) {
  const selected = seededOrder(worldSeed, "ending-subset", ENDING_RECIPES.filter((item) => !item.emergency)).slice(0, 4);
  const emergency = ENDING_RECIPES.find((item) => item.emergency);
  const copy = [...selected, emergency].map((definition) => {
    const text = endingText(definition.id, genome);
    return { ...clone(definition), title: text.title, description: text.description };
  });
  return copy;
}

function endingText(id, genome) {
  const texts = {
    ENDING_REWEAVE_TOGETHER: { title: "갈라진 약속을 다시 잇다", description: `${genome.community}와 기억의 흔적을 함께 지키며 위기의 근원을 걷어 내고 ${genome.worldName}의 관계를 다시 엮는다.` },
    ENDING_OPEN_FRONTIER: { title: "열린 경계의 세계", description: `통제의 닻을 놓고 ${genome.worldName}가 스스로 변화할 자유를 선택한다.` },
    ENDING_KEEP_THE_PROMISE: { title: "남아서 지키는 약속", description: `귀환보다 주민과 맺은 약속을 우선해 기억과 생활을 잇는 장기적인 수호자가 된다.` },
    ENDING_CUT_THE_CYCLE: { title: "되풀이를 끊는 문", description: `위기와 통제의 원인을 함께 제거하고 귀환로를 열어 같은 비극이 반복될 구조를 끝낸다.` },
    ENDING_PRESERVE_THE_SCARS: { title: "상처를 지우지 않는 복구", description: `위기의 흔적을 증언으로 남긴 채 공동체가 그 기억과 함께 살아갈 수 있도록 안정시킨다.` },
    ENDING_WALK_BETWEEN_WORLDS: { title: "두 세계 사이의 방랑자", description: `${genome.worldName}의 생활을 안정시키고 귀환로를 연결해 필요할 때 다시 건널 수 있는 약속을 만든다.` },
    ENDING_EMERGENCY_WITHDRAWAL: { title: "비상 이탈", description: `결말 조합을 완성하지 못한 채 생존자를 우선해 위험한 수렴점에서 빠져나온다.` }
  };
  return texts[id];
}

export function computeCampaignContentHash(campaign) {
  return fingerprint({
    templateVersion: campaign.templateVersion,
    archetype: campaign.archetype,
    worldSeed: campaign.worldSeed,
    generatedTitle: campaign.generatedTitle,
    worldName: campaign.worldName,
    premise: campaign.premise,
    tone: campaign.tone,
    genome: campaign.genome,
    npcRoles: campaign.npcRoles,
    questSeeds: campaign.questSeeds,
    requiredStoryBeats: campaign.requiredStoryBeats,
    endingCandidates: campaign.endingCandidates,
    areaFlavors: campaign.areaFlavors || []
  });
}

export function createCampaignBlueprint({ worldSeed, requestedArchetype = null, turnLimit = 40 }) {
  assert(Number.isSafeInteger(worldSeed), 400, "WORLD_SEED_INVALID", "worldSeed must be a safe integer.");
  assert(requestedArchetype === null || requestedArchetype === CAMPAIGN_ARCHETYPE, 400, "ARCHETYPE_INVALID", `archetype must be ${CAMPAIGN_ARCHETYPE}.`);
  assert(Number.isInteger(turnLimit) && turnLimit >= 30 && turnLimit <= 50, 400, "TURN_LIMIT_INVALID", "turnLimit must be between 30 and 50.");

  const genome = createGenome(worldSeed);
  const tokens = progressTokenDefinitions(worldSeed, genome);
  const npcRoles = createNpcRoles(worldSeed, genome);
  const questSeeds = createQuestSeeds(worldSeed, genome);
  for (const quest of questSeeds) quest.summary = quest.description;
  const requiredStoryBeats = createBeats(worldSeed, turnLimit, genome, tokens);
  const endingCandidates = createEndingCandidates(worldSeed, genome);
  const premise = `${genome.crisis}에 빠진 코드리아를 구하기 위해 넙죽이가 관리자 키보드의 권한을 각성한다.`;
  const tone = ["keyboard_fantasy", genome.palette, pick(worldSeed, "tone", ["tender_mystery", "melancholy_adventure", "hopeful_tension", "strange_folklore"] )];

  const blueprint = {
    templateId: `${CAMPAIGN_ARCHETYPE}.${CAMPAIGN_TEMPLATE_VERSION}`,
    templateVersion: CAMPAIGN_TEMPLATE_VERSION,
    archetype: CAMPAIGN_ARCHETYPE,
    baseArchetype: CAMPAIGN_ARCHETYPE,
    variant: stableInteger(worldSeed, "variant", 1_000_000),
    worldSeed,
    generatedTitle: genome.title,
    generatedTitleKo: genome.title,
    gameTitle: GAME_TITLE,
    worldId: WORLD_CODRIA,
    worldName: genome.worldName,
    protagonistId: PROTAGONIST_NUPJUKYI,
    protagonistName: PROTAGONIST_NAME_KO,
    artifactId: ADMIN_KEYBOARD,
    artifactName: ADMIN_KEYBOARD_NAME_KO,
    adminAccessLevels: clone(ADMIN_ACCESS_LEVELS),
    regionAxes: [...CAMPAIGN_REGION_AXES],
    premise,
    premiseKo: premise,
    tone,
    genome,
    forbiddenEvents: ["코드리아가 아닌 세계 생성", "넙죽이가 아닌 주인공 생성", "관리자 권한 체계 대체", "지도 재생성", "LLM 좌표·슬롯·에셋 변경", "주사위·피해·보상의 소급 변경", "서버 결말 레시피 변경", "NPC 기억의 강제 삭제"],
    npcRoles,
    questSeeds,
    initialQuests: clone(questSeeds),
    progressTokenDefinitions: clone(tokens),
    metricDefinitions: [...METRIC_KEYS],
    campaignPhases: clone(CAMPAIGN_PHASES),
    campaignMacroPhases: clone(CAMPAIGN_MACRO_PHASES),
    canonicalFactTemplates: [
      { id: deterministicUuid(`${worldSeed}:fact:world`), subject: "world", predicate: "identity", value: WORLD_CODRIA, label: WORLD_NAME_KO, type: "canonical" },
      { id: deterministicUuid(`${worldSeed}:fact:player`), subject: "protagonist", predicate: "identity", value: PROTAGONIST_NUPJUKYI, label: PROTAGONIST_NAME_KO, type: "canonical" },
      { id: deterministicUuid(`${worldSeed}:fact:artifact`), subject: "artifact", predicate: "identity", value: ADMIN_KEYBOARD, label: ADMIN_KEYBOARD_NAME_KO, type: "canonical" },
      { id: deterministicUuid(`${worldSeed}:fact:collapse-origin`), subject: "collapse_origin", predicate: "inside_admin_control_system", value: false, type: "canonical" },
      { id: deterministicUuid(`${worldSeed}:fact:crisis`), subject: "campaign_crisis", predicate: "hidden_cause", value: `${genome.crisis}의 숨은 원인은 ${genome.hiddenCause}이다.`, type: "canonical" },
      { id: deterministicUuid(`${worldSeed}:fact:geometry`), subject: "world", predicate: "geometry_policy", value: "전체 월드는 런 시작 전에 한 번 생성되고 캠페인 턴 중 재생성되지 않는다.", type: "canonical" }
    ],
    initialRumors: [
      { id: deterministicUuid(`${worldSeed}:rumor:motif`), summary: `${genome.motif}는 거짓말보다 말하지 않은 약속에 더 강하게 반응하며, ${genome.crisis}도 주민들이 지키려던 무언가에서 시작됐다고 한다.`, reliability: 0.5, status: "active", firstHeardTurn: 0, expiresTurn: Math.min(turnLimit, 14) }
    ],
    requiredStoryBeats,
    endingWindow: { normalEligibleStart: Math.max(30, Math.min(38, turnLimit - 2)), preferredEnd: Math.min(42, turnLimit), hardLimit: turnLimit },
    endingCandidates,
    areaFlavors: []
  };
  blueprint.contentHash = computeCampaignContentHash(blueprint);
  blueprint.generationMetadata = {
    generator: "deterministic-campaign-genome",
    genomeVersion: genome.version,
    planVersion: "campaign-plan.v1",
    enrichment: "not_attempted",
    fallbackUsed: false,
    contentHash: blueprint.contentHash
  };
  blueprint.progressionMetadata = clone(blueprint.generationMetadata);
  blueprint.scenarioPlan = {
    genome: clone(genome),
    questSeeds: clone(questSeeds),
    contentHash: blueprint.contentHash,
    generationMetadata: clone(blueprint.generationMetadata),
    areaFlavors: []
  };
  blueprint.generationPlan = clone(blueprint.scenarioPlan);
  return blueprint;
}

export function campaignAct(turnNo, turnLimit) {
  const index = Math.min(CAMPAIGN_PHASES.length - 1, Math.floor((Math.max(1, turnNo) - 1) * CAMPAIGN_PHASES.length / turnLimit));
  return CAMPAIGN_PHASES[index].id;
}

function grantBeatRewards(run, beat, turnNo, events) {
  const tokenId = beat.rewardProgressToken;
  const progressTokens = Array.isArray(run.progressTokens) ? run.progressTokens : [];
  run.progressTokens = progressTokens;
  if (tokenId && !progressTokens.includes(tokenId)) {
    progressTokens.push(tokenId);
    events.push({ type: "progress_token_granted", tokenId, turnNo });
  }
  const level = beat.rewardProgressLevel;
  const current = Number.isInteger(run.progressLevel) ? run.progressLevel : 0;
  if (Number.isInteger(level) && level > current) {
    run.progressLevel = Math.min(3, level);
    events.push({ type: "progress_level_changed", from: current, to: run.progressLevel, turnNo });
  }
}

export function advanceStoryDirector(run, turnNo, events, evidence = {}) {
  let active = run.requiredStoryBeats.find((beat) => beat.status === "active") || run.requiredStoryBeats.find((beat) => beat.status === "pending");
  const successful = ["partial_success", "success", "critical_success"].includes(evidence.outcome);
  const contextualActions = new Set(evidence.contextualActions || []);
  const adminAccessEvidence = active?.requiredEvidenceKey?.startsWith("ADMIN_ACCESS_LEVEL_")
    && (evidence.targetEvidenceKeys || []).includes(active.requiredEvidenceKey);
  const abilityMatches = active && (adminAccessEvidence || (active.allowedAbilities || [active.requiredAbility]).includes(evidence.ability) || contextualActions.has(active.requiredAbility));
  const placeMatches = active && (adminAccessEvidence || !active.requiredCampaignRole || active.requiredCampaignRole === evidence.campaignRole);
  const availableTokens = new Set(run.progressTokens || []);
  const progressLevel = Number.isInteger(run.progressLevel) ? run.progressLevel : 0;
  const convergencePermissionMatches = active?.requiredCampaignRole !== "FINAL_CONVERGENCE" || (progressLevel >= 3 && (run.progressTokenDefinitions || PROGRESS_TOKEN_DEFINITIONS).every((token) => availableTokens.has(token.id)));
  const finaleResolved = evidence.finaleResolved === true || evidence.finalePuzzleResolved === true;
  const finaleEndingId = evidence.finaleEndingId || null;
  const selectedEndingId = run.selectedEndingId || null;
  const evidenceMatches = active && (active.requiredEvidencePolicy === "finale_resolved"
    ? finaleResolved && (!selectedEndingId || finaleEndingId === selectedEndingId)
    : (evidence.targetEvidenceKeys || []).includes(active.requiredEvidenceKey));
  if (active && successful && abilityMatches && placeMatches && convergencePermissionMatches && evidenceMatches) {
    active.status = "completed";
    active.completedTurn = turnNo;
    grantBeatRewards(run, active, turnNo, events);
    events.push({ type: "story_beat_changed", beatId: active.id, status: "completed", phaseId: active.phaseId, evidence: { ability: evidence.ability, campaignRole: evidence.campaignRole, outcome: evidence.outcome, targetEvidenceKeys: [...new Set(evidence.targetEvidenceKeys || [])], finaleEndingId } });
    if (active.requiredCampaignRole === "HIDDEN_TRUTH") {
      const clue = run.canonicalFacts.find((fact) => fact.subject === "collapse_origin" && fact.predicate === "inside_admin_control_system");
      if (clue) {
        clue.value = true;
        clue.establishedTurn = turnNo;
        events.push({ type: "canonical_fact_confirmed", factId: clue.id, subject: clue.subject, predicate: clue.predicate });
      }
    }
    const mainQuest = run.activeQuests?.find((quest) => quest.questKind === "main");
    if (mainQuest) {
      mainQuest.currentStep = active.id;
      events.push({ type: "quest_updated", questId: mainQuest.id, currentStep: active.id, beatCompleted: active.id });
    }
    active = run.requiredStoryBeats.find((beat) => beat.status === "pending");
    if (active) {
      active.status = "active";
      events.push({ type: "story_beat_changed", beatId: active.id, status: "active", phaseId: active.phaseId });
    }
  }

  const forcedConvergence = turnNo >= run.turnLimit;
  if (forcedConvergence) {
    for (const beat of run.requiredStoryBeats) {
      if (!["active", "pending"].includes(beat.status)) continue;
      beat.status = "skipped";
      events.push({ type: "story_beat_changed", beatId: beat.id, status: "skipped", phaseId: beat.phaseId, reason: "turn_limit_convergence" });
      const loop = { id: deterministicUuid(`${run.id}:skipped:${beat.id}`), summary: `미해결 대가: ${beat.description}`, status: "open", createdTurn: turnNo, expiresTurn: run.turnLimit, source: "campaign_convergence" };
      if (!run.openLoops.some((item) => item.id === loop.id)) {
        run.openLoops.push(loop);
        run.unresolvedHooks ||= [];
        run.unresolvedHooks.push(clone(loop));
        events.push({ type: "open_loop_created", loopId: loop.id, summary: loop.summary, consequence: true });
      }
    }
  }

  active = run.requiredStoryBeats.find((beat) => beat.status === "active")
    || run.requiredStoryBeats.find((beat) => beat.status === "pending")
    || [...run.requiredStoryBeats].reverse().find((beat) => beat.status === "completed")
    || run.requiredStoryBeats.at(-1);
  if (!active) return;
  if (active.status === "pending") active.status = "active";
  run.currentAct = forcedConvergence ? "final_convergence" : active.phaseId;
  run.campaignPhase = run.currentAct;
  run.currentMacroPhase = clone(macroPhaseForBeat(active));
  run.currentStoryBeat = { ...clone(active), act: run.currentAct };
  if (turnNo === active.targetTurn && !["completed", "skipped"].includes(active.status)) {
    const loopId = deterministicUuid(`${run.id}:beat-loop:${active.id}`);
    if (!run.openLoops.some((loop) => loop.id === loopId)) {
      const loop = { id: loopId, summary: active.description, status: "open", createdTurn: turnNo, expiresTurn: Math.min(run.turnLimit, turnNo + Math.max(3, Math.ceil(run.turnLimit * 0.15))), source: "campaign_phase" };
      run.openLoops.push(loop);
      run.unresolvedHooks ||= [];
      run.unresolvedHooks.push(clone(loop));
      events.push({ type: "open_loop_created", loopId: loop.id, summary: loop.summary });
    }
  }
  if (run.turnLimit - turnNo <= 5) run.activeQuests = (run.activeQuests || []).map((quest) => quest.questKind === "main" ? quest : { ...quest, acceptsNewSteps: false });
}

const COMPONENT_ALIASES = Object.freeze({
  player: ["player"],
  anchor: ["anchor", "FINAL_ANCHOR"],
  safeguard: ["safeguard", "FINAL_SAFEGUARD"],
  memory: ["memory", "FINAL_MEMORY"],
  freedom: ["freedom", "FINAL_FREEDOM"],
  threat: ["threat", "FINAL_THREAT"],
  passage: ["passage", "FINAL_PASSAGE"],
  witness: ["witness", "FINAL_WITNESS"]
});

function componentEntities(run, componentId) {
  if (componentId === "player") return run.entities.filter((entity) => entity.kind === "player");
  const aliases = COMPONENT_ALIASES[componentId] || [componentId];
  return run.entities.filter((entity) => aliases.includes(entity.state?.finaleComponent));
}

function componentActive(run, componentId) {
  const entities = componentEntities(run, componentId);
  return entities.some((entity) => entity.active !== false && !entity.state?.deleted);
}

function componentRemoved(run, componentId) {
  const entities = componentEntities(run, componentId);
  return entities.length > 0 && entities.every((entity) => entity.active === false || entity.state?.deleted === true);
}

function componentLinked(run, pair) {
  const [leftId, rightId] = pair;
  const leftEntities = new Set(componentEntities(run, leftId).map((entity) => entity.id));
  const rightEntities = new Set(componentEntities(run, rightId).map((entity) => entity.id));
  if (leftEntities.size === 0 || rightEntities.size === 0) return false;
  return (run.connections || []).some((connection) => connection.active !== false && ((leftEntities.has(connection.fromId) && rightEntities.has(connection.toId)) || (leftEntities.has(connection.toId) && rightEntities.has(connection.fromId))));
}

function metricMatches(run, metric, condition) {
  const actual = Number(run.metrics?.[metric]);
  if (!Number.isFinite(actual)) return false;
  if (Number.isFinite(condition)) return actual >= condition;
  if (!condition || typeof condition !== "object") return false;
  return (!Number.isFinite(condition.min) || actual >= condition.min)
    && (!Number.isFinite(condition.max) || actual <= condition.max);
}

function endingRecipeMatches(run, ending) {
  return ending.requiredLinks.every((pair) => componentLinked(run, pair))
    && ending.requiredRemoved.every((componentId) => componentRemoved(run, componentId))
    && ending.requiredActive.every((componentId) => componentActive(run, componentId))
    && ending.forbiddenLinks.every((pair) => !componentLinked(run, pair))
    && Object.entries(ending.metricConditions || {}).every(([metric, condition]) => metricMatches(run, metric, condition));
}

export function chooseEnding(run) {
  const selected = run.selectedEndingId;
  if (selected) {
    const explicit = run.endingCandidates.find((item) => item.id === selected);
    if (explicit) return explicit;
  }
  const recipeMatch = run.endingCandidates.find((item) => !item.emergency && endingRecipeMatches(run, item));
  if (recipeMatch) return recipeMatch;
  return run.endingCandidates.find((item) => item.emergency) || run.endingCandidates.at(-1);
}

export function endingConditionReports(run) {
  return (run.endingCandidates || []).filter((ending) => !ending.emergency).map((ending) => {
    const conditions = [];
    for (const pair of ending.requiredLinks || []) conditions.push({ label: `${pair[0]}—${pair[1]} 연결`, satisfied: componentLinked(run, pair) });
    for (const componentId of ending.requiredRemoved || []) conditions.push({ label: `${componentId} 비활성`, satisfied: componentRemoved(run, componentId) });
    for (const componentId of ending.requiredActive || []) conditions.push({ label: `${componentId} 활성`, satisfied: componentActive(run, componentId) });
    for (const pair of ending.forbiddenLinks || []) conditions.push({ label: `${pair[0]}—${pair[1]} 연결 없음`, satisfied: !componentLinked(run, pair) });
    for (const [metric, condition] of Object.entries(ending.metricConditions || {})) conditions.push({ label: `${metric} 조건`, satisfied: metricMatches(run, metric, condition) });
    conditions.unshift({ label: "관리자 권한 3단계", satisfied: run.progressLevel === 3 });
    const satisfiedCount = conditions.filter((item) => item.satisfied).length;
    return { id: ending.id, title: ending.title, eligible: satisfiedCount === conditions.length, satisfiedCount, totalCount: conditions.length, conditions };
  }).sort((left, right) => right.satisfiedCount - left.satisfiedCount || left.id.localeCompare(right.id));
}

export function resolveFinalConvergence(run, ending, turnNo) {
  const poi = run.world.pois.find((item) => item.campaignRole === "FINAL_CONVERGENCE") || run.world.pois.at(-1) || null;
  const focus = componentEntities(run, "anchor")[0] || run.entities.find((item) => item.kind === "player") || null;
  const slot = focus?.state?.slotId ? run.world.placementSlots.find((item) => item.id === focus.state.slotId) : null;
  const progressLevel = Number.isInteger(run.progressLevel) ? run.progressLevel : 0;
  const progressTokens = [...new Set(run.progressTokens || [])];
  const evidence = {
    matchedRecipe: clone({
      requiredLinks: ending.requiredLinks || [],
      requiredRemoved: ending.requiredRemoved || [],
      requiredActive: ending.requiredActive || [],
      forbiddenLinks: ending.forbiddenLinks || [],
      metricConditions: ending.metricConditions || {}
    }),
    spatialEvidence: clone(run.finalePuzzle?.evidence || [])
  };
  return {
    resolved: true,
    turnNo,
    endingId: ending.id,
    endingCategory: ending.category,
    finalePoiId: poi?.id || null,
    areaId: slot?.areaId || poi?.areaId || null,
    position: clonePoint(focus?.position || poi?.position || { x: 0, y: 0 }),
    progressLevel,
    progressTokens,
    endingFactors: endingFactors(run, focus?.position || poi?.position || null),
    geometryChanged: false,
    resolutionMode: run.selectedEndingId ? "explicit_server_recipe" : "turn_limit_recipe_fallback",
    evidence
  };
}

export function resolveFinale(run, ending, turnNo) {
  return resolveFinalConvergence(run, ending, turnNo);
}

function clonePoint(point) {
  return { x: point.x, y: point.y };
}
