import { createHash } from "node:crypto";
import { assert } from "../errors.js";
import { clone, deterministicUuid, fingerprint } from "./serialization.js";

export const CAMPAIGN_TEMPLATE_VERSION = "generative-campaign.v1";
export const CAMPAIGN_ARCHETYPE = "generative-keyboard-fantasy";
export const CAMPAIGN_TITLE = "키보드 방랑자의 미지 세계";
export const WORLD_NAME = "미지의 격자권";

export const GENERATIVE_ROLE_IDS = Object.freeze([
  "ARRIVAL_CATALYST",
  "LOCAL_STAKES",
  "RELATIONSHIP_CONFLICT",
  "HIDDEN_TRUTH",
  "CONSEQUENCE_RETURN",
  "FINAL_CONVERGENCE"
]);

export const PROGRESS_TOKEN_DEFINITIONS = Object.freeze([
  Object.freeze({ id: "MILESTONE_TOKEN_1", name: "첫 이정표", meaning: "지역의 문제와 연결되었다는 증표", progressLevel: 1, sourceRole: "LOCAL_STAKES" }),
  Object.freeze({ id: "MILESTONE_TOKEN_2", name: "둘째 이정표", meaning: "관계의 교착을 통과했다는 증표", progressLevel: 2, sourceRole: "RELATIONSHIP_CONFLICT" }),
  Object.freeze({ id: "MILESTONE_TOKEN_3", name: "셋째 이정표", meaning: "선택의 결과를 받아들였다는 증표", progressLevel: 3, sourceRole: "CONSEQUENCE_RETURN" })
]);

export const METRIC_KEYS = Object.freeze(["worldStability", "worldAutonomy", "publicTrust", "technicalDebt", "companionBond", "turnPressure"]);

export const CAMPAIGN_PHASES = Object.freeze([
  Object.freeze({ id: "arrival", name: "Arrival", nameKo: "낯선 세계의 징후" }),
  Object.freeze({ id: "local_stakes", name: "Local stakes", nameKo: "눈앞의 삶과 대가" }),
  Object.freeze({ id: "relationship_conflict", name: "Relationship conflict", nameKo: "얽힌 약속의 충돌" }),
  Object.freeze({ id: "hidden_truth", name: "Hidden truth", nameKo: "감춰진 원인의 발견" }),
  Object.freeze({ id: "consequence_return", name: "Consequence return", nameKo: "되돌아온 선택" }),
  Object.freeze({ id: "final_convergence", name: "Final convergence", nameKo: "모든 갈래의 수렴" })
]);

const GENERATIVE_ALLOWED_ABILITIES = Object.freeze({
  ARRIVAL_CATALYST: Object.freeze(["interact", "negotiate"]),
  LOCAL_STAKES: Object.freeze(["copy", "interact"]),
  RELATIONSHIP_CONFLICT: Object.freeze(["connect", "negotiate"]),
  HIDDEN_TRUTH: Object.freeze(["interact", "negotiate"]),
  CONSEQUENCE_RETURN: Object.freeze(["restore", "interact"]),
  FINAL_CONVERGENCE: Object.freeze(["connect", "delete"])
});

export const CAMPAIGN_ALLOWED_ABILITIES_BY_ROLE = GENERATIVE_ALLOWED_ABILITIES;

const WORLD_PREFIXES = Object.freeze(["유리", "달그늘", "잉크", "종소리", "별실", "서리", "이끼", "황혼", "풍경", "메아리", "청동", "비늘"]);
const WORLD_FORMS = Object.freeze(["군도", "수해", "회랑", "평원", "도서관", "고리", "분지", "정원", "천공로", "미궁"]);
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
const TOKEN_NAMES = Object.freeze([
  ["첫 약속의 조각", "머물렀던 자리의 표식", "생활의 매듭"],
  ["함께 건넌 다리", "두 목소리의 인장", "갈등의 매듭"],
  ["되돌아온 선택", "마지막 증언의 별", "결과의 매듭"]
]);

const ROLE_BLUEPRINTS = Object.freeze([
  { id: "ARRIVAL_CATALYST", npcRole: "낯선 이를 가장 먼저 알아본 길잡이", evidenceKey: "ARRIVAL_GUIDE", beatId: "beat.arrival_catalyst" },
  { id: "LOCAL_STAKES", npcRole: "생활의 피해를 기록하는 공동체 대표", evidenceKey: "MILESTONE_TOKEN_1", beatId: "beat.local_stakes" },
  { id: "RELATIONSHIP_CONFLICT", npcRole: "서로 다른 약속 사이에 선 중재자", evidenceKey: "MILESTONE_TOKEN_2", beatId: "beat.relationship_conflict" },
  { id: "HIDDEN_TRUTH", npcRole: "감춰진 원인을 간직한 증언자", evidenceKey: "STORY_REVELATION", beatId: "beat.hidden_truth" },
  { id: "CONSEQUENCE_RETURN", npcRole: "이전 선택의 결과를 들고 돌아온 생존자", evidenceKey: "MILESTONE_TOKEN_3", beatId: "beat.consequence_return" },
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
    Math.max(5, Math.round(turnLimit * 0.16)),
    Math.max(10, Math.round(turnLimit * 0.34)),
    Math.max(17, Math.round(turnLimit * 0.56)),
    Math.max(23, Math.round(turnLimit * 0.76)),
    Math.max(28, Math.round(turnLimit * 0.92))
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
  const prefix = pick(worldSeed, "world-prefix", WORLD_PREFIXES);
  const form = pick(worldSeed, "world-form", WORLD_FORMS);
  const motif = pick(worldSeed, "motif", MOTIFS);
  const crisis = pick(worldSeed, "crisis", CRISES);
  const community = pick(worldSeed, "community", COMMUNITIES);
  const dilemma = pick(worldSeed, "dilemma", DILEMMAS);
  const titlePattern = stableInteger(worldSeed, "title-pattern", 4);
  const worldName = `${prefix}${form}`;
  const titles = [
    `${worldName}와 ${motif.noun}`,
    `${motif.noun}의 키보드 방랑자`,
    `${worldName}: 사라지는 길의 노래`,
    `${community}와 ${motif.noun}`
  ];
  return {
    version: "campaign-genome.v1",
    seed: worldSeed,
    worldName,
    title: titles[titlePattern],
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
    endingQuestion: dilemma
  };
}

function progressTokenDefinitions(worldSeed, genome) {
  const meanings = [
    `${genome.community}의 일상에 직접 손을 보탠 기억`,
    `${genome.motif} 아래 충돌하던 약속을 한 장면 안에 함께 남긴 증거`,
    `${genome.hiddenCause}을 알고도 결과를 외면하지 않은 선택`
  ];
  const nameFlavors = [genome.community, genome.motif, genome.worldName];
  return PROGRESS_TOKEN_DEFINITIONS.map((definition, index) => ({
    ...clone(definition),
    name: `${pick(worldSeed, `token-name:${index}`, TOKEN_NAMES[index])} · ${nameFlavors[index]}`,
    meaning: meanings[index]
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
      suggestedAbilities: ["copy", "interact"],
      horizon: "early"
    },
    {
      id: "quest.seed.shared_witness",
      title: `${witness}에 남은 두 약속`,
      description: `갈등 당사자들이 같은 사건을 다르게 기억하는 이유를 수집하고 둘 모두가 인정할 연결점을 만든다.`,
      campaignRole: "RELATIONSHIP_CONFLICT",
      suggestedAbilities: ["connect", "negotiate"],
      horizon: "middle"
    },
    {
      id: "quest.seed.returning_cost",
      title: `${genome.motif}가 되돌려 준 것`,
      description: `초반에 미뤄 둔 선택이 어떤 주민에게 돌아갔는지 확인하고 복구하거나 책임질 방법을 찾는다.`,
      campaignRole: "CONSEQUENCE_RETURN",
      suggestedAbilities: ["restore", "interact"],
      horizon: "late"
    }
  ];
}

function createBeats(worldSeed, turnLimit, genome, tokens) {
  const targets = beatTurns(turnLimit);
  const descriptions = [
    `${genome.worldName}에 도착해 ${genome.motifImage}와 반응하는 지역 존재를 만나 첫 징후를 확인한다.`,
    `${genome.crisis}으로 무너진 ${genome.community}의 생활을 직접 돕고 이 세계에 남을 이유를 얻는다.`,
    `위기의 해법을 두고 충돌하는 약속들을 연결하거나 중재해 어느 관계를 감수할지 드러낸다.`,
    `증언과 흔적을 모아 위기의 감춰진 원인이 '${genome.hiddenCause}'임을 확인한다.`,
    `앞선 선택이 낳은 예상 밖의 결과가 돌아오면 복구와 책임 사이에서 실제 대가를 치른다.`,
    `모든 인물과 증거가 모인 자리에서 '${genome.dilemma}'라는 질문에 공간 편집으로 답한다.`
  ];
  const titles = [
    `${genome.motif}의 반응`,
    `${genome.community}의 오늘`,
    "두 약속 사이의 길",
    `${genome.hiddenCause}의 흔적`,
    "되돌아온 선택의 무게",
    `${genome.worldName}의 마지막 물음`
  ];
  const rewardByIndex = new Map([[1, tokens[0]], [2, tokens[1]], [4, tokens[2]]]);
  return ROLE_BLUEPRINTS.map((definition, index) => {
    const allowedAbilities = [...GENERATIVE_ALLOWED_ABILITIES[definition.id]];
    const requiredAbility = pick(worldSeed, `beat-ability:${index}`, allowedAbilities);
    const reward = rewardByIndex.get(index) || null;
    return {
      id: definition.beatId,
      title: titles[index],
      description: descriptions[index],
      phaseId: CAMPAIGN_PHASES[index].id,
      requiredAbility,
      allowedAbilities,
      requiredCampaignRole: definition.id,
      requiredEvidenceKey: definition.evidenceKey,
      requiredEvidencePolicy: index === ROLE_BLUEPRINTS.length - 1 ? "finale_resolved" : "designated_target",
      recoveryPaths: allowedAbilities.map((ability) => ({ ability, requiresDesignatedEvidence: true })),
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
  const premise = `키보드를 현실 편집 도구로 쓰는 방랑자가 ${genome.worldName}에 떨어진다. ${genome.crisis}을 멈추려면 ${genome.community}의 삶과 갈라진 관계를 통과해 '${genome.hiddenCause}'라는 진실을 마주하고, 끝내 ${genome.dilemma}.`;
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
    worldName: genome.worldName,
    premise,
    premiseKo: premise,
    tone,
    genome,
    forbiddenEvents: ["지도 재생성", "LLM 좌표·슬롯·에셋 변경", "주사위·피해·보상의 소급 변경", "서버 결말 레시피 변경", "NPC 기억의 강제 삭제"],
    npcRoles,
    questSeeds,
    initialQuests: clone(questSeeds),
    progressTokenDefinitions: clone(tokens),
    metricDefinitions: [...METRIC_KEYS],
    campaignPhases: clone(CAMPAIGN_PHASES),
    canonicalFactTemplates: [
      { id: deterministicUuid(`${worldSeed}:fact:world`), subject: "generated_world", predicate: "world_identity", value: `${genome.worldName}는 ${genome.motifImage}가 현실의 규칙에 영향을 주는 세계다.`, type: "canonical" },
      { id: deterministicUuid(`${worldSeed}:fact:player`), subject: "player", predicate: "identity", value: "플레이어는 키보드의 Move, Copy, Delete, Connect, Restore, Undo 능력으로 현실을 편집하는 방랑자다.", type: "canonical" },
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
  const abilityMatches = active && ((active.allowedAbilities || [active.requiredAbility]).includes(evidence.ability) || contextualActions.has(active.requiredAbility));
  const placeMatches = active && (!active.requiredCampaignRole || active.requiredCampaignRole === evidence.campaignRole);
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
  run.currentStoryBeat = { ...clone(active), act: run.currentAct };
  if (turnNo === active.targetTurn && !["completed", "skipped"].includes(active.status)) {
    const loopId = deterministicUuid(`${run.id}:beat-loop:${active.id}`);
    if (!run.openLoops.some((loop) => loop.id === loopId)) {
      const loop = { id: loopId, summary: active.description, status: "open", createdTurn: turnNo, expiresTurn: Math.min(run.turnLimit, turnNo + Math.max(3, Math.ceil(run.turnLimit * 0.15))), source: "campaign_phase" };
      run.openLoops.push(loop);
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
