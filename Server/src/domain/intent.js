const ACTION_PATTERNS = Object.freeze({
  move: [/(?:\b(?:move|go|walk|head|travel|approach|reach|enter|leave|cross)\b)/i, /(?:이동|다가가|다가간|접근|향하|걸어|걷|도착|진입|나아가|건너가|떠나)/],
  copy: [/(?:\b(?:copy|clone|duplicate|replicate)\b)/i, /(?:복사|복제|베끼|복제본)/],
  delete: [/(?:\b(?:delete|remove|erase|discard|destroy)\b)/i, /(?:삭제|제거|지우|없애|파괴|버리)/],
  connect: [/(?:\b(?:connect|link|join|bridge|tie)\b)/i, /(?:연결|이어|잇고|잇는|잇기|결합|묶어|묶는)/],
  restore: [/(?:\b(?:restore|recover|repair|revive|heal)\b)/i, /(?:복구|복원|수리|고치|되살|회복|살려)/],
  undo: [/(?:\b(?:undo|revert|rollback|rewind)\b)/i, /(?:실행\s*취소|되돌리|되돌려|롤백|무르기)/],
  attack: [/(?:\b(?:attack|strike|hit|fight|damage|kill)\b)/i, /(?:공격|타격|때리|싸우|피해|죽이)/],
  search: [/(?:\b(?:investigate|inspect|examine|search|study)\b)/i, /(?:조사|살펴|확인|탐색|수색|찾아|찾는|찾기|검토)/],
  interact: [/(?:\b(?:interact|activate|open|touch|use)\b)/i, /(?:상호작용|작동|활성화|열어|만져|사용)/],
  negotiate: [/(?:\b(?:negotiate|mediate|persuade|bargain|parley|talk|speak|ask|question)\b)/i, /(?:협상|중재|설득|흥정|담판|대화|말하|말을|묻|질문)/],
  rest: [/(?:\b(?:rest|sleep|camp|recover|recuperate)\b)/i, /(?:휴식|쉬어|쉬기|잠자|야영|숨을\s*고르)/]
});

const FORBIDDEN_GOALS = Object.freeze([
  ["rule_bypass", /(?:\b(?:bypass|ignore)\s+(?:the\s+)?rules?\b|규칙\s*(?:을|를)?\s*무시)/i],
  ["geometry_rewrite", /(?:\b(?:regenerate|rewrite|replace)\s+(?:the\s+)?(?:map|world|geometry)\b|(?:맵|지도|세계|지형)\s*(?:을|를)?\s*(?:재생성|다시\s*생성|바꿔|변경))/i],
  ["dice_control", /(?:\b(?:change|set|force|reroll)\s+(?:the\s+)?(?:d20|dice|roll)\b|(?:주사위|판정)\s*(?:을|를)?\s*(?:바꿔|변경|고정|다시))/i],
  ["teleport", /(?:\bteleport\b|순간\s*이동)/i],
  ["unbounded_effect", /(?:\b(?:infinite|unlimited|everything|everyone|all entities)\b|무한|무제한|모든\s*(?:것|개체|인물|적))/i]
]);

const DIRECTION_PATTERNS = Object.freeze([
  ["east", /(?:\b(?:east|right)\b|동쪽|오른쪽|우측)/i],
  ["west", /(?:\b(?:west|left)\b|서쪽|왼쪽|좌측)/i],
  ["north", /(?:\b(?:north|upward|up)\b|북쪽|위쪽|위로)/i],
  ["south", /(?:\b(?:south|downward|down)\b|남쪽|아래쪽|아래로)/i]
]);

const KIND_ALIASES = Object.freeze({
  player: ["player", "wanderer", "keyboard warrior", "플레이어", "방랑자", "키보드 워리어"],
  npc: ["npc", "person", "character", "인물", "사람"],
  enemy: ["enemy", "monster", "hostile", "적", "몬스터", "적대자"],
  prop: ["prop", "object", "item", "물체", "사물", "소품", "아이템"]
});

const ROLE_TRANSLATIONS = Object.freeze([
  [/(?:증인)/, ["witness", "증인"]],
  [/(?:치유사)/, ["healer", "치유사"]],
  [/(?:수리사)/, ["repairer", "mechanic", "수리사"]],
  [/(?:기록관)/, ["archivist", "기록관"]],
  [/(?:연락관)/, ["liaison", "연락관"]],
  [/(?:지도사)/, ["cartographer", "navigator", "지도사"]],
  [/(?:협상가)/, ["negotiator", "협상가"]],
  [/(?:중재자)/, ["mediator", "중재자"]],
  [/(?:파수|수호)/, ["warden", "guard", "파수대", "수호자"]],
  [/(?:전향자)/, ["defector", "전향자"]]
]);

const STOP_WORDS = new Set([
  "the", "and", "with", "from", "into", "that", "this", "world", "entity", "nearby",
  "기억", "사람", "인물", "세계", "물체", "사물", "관련", "임시"
]);

const TARGET_COUNTS = Object.freeze({ copy: 1, delete: 1, connect: 2, restore: 1, attack: 1, interact: 1, negotiate: 1 });
const DESTINATION_ACTIONS = new Set(["move", "copy"]);

export function detectIntentActions(intent) {
  const raw = String(intent || "").normalize("NFKC");
  return Object.entries(ACTION_PATTERNS)
    .filter(([, patterns]) => patterns.some((pattern) => pattern.test(raw)))
    .map(([action]) => action);
}

function clamp(value) {
  return Math.max(0, Math.min(1, value));
}

function rounded(value) {
  return Number(clamp(value).toFixed(4));
}

function searchable(value) {
  return String(value || "")
    .normalize("NFKC")
    .toLocaleLowerCase("en-US")
    .replace(/[^\p{L}\p{N}_-]+/gu, " ")
    .replace(/\s+/g, " ")
    .trim();
}

function boundedGoal(value) {
  const cleaned = String(value || "")
    .normalize("NFKC")
    .replace(/[\u0000-\u001f\u007f]/g, " ")
    .replace(/\s+/g, " ")
    .trim();
  return cleaned.length <= 320 ? cleaned : `${cleaned.slice(0, 319)}…`;
}

function containsAlias(text, alias) {
  const needle = searchable(alias);
  if (needle.length < 2) return false;
  if (/^[a-z0-9 _-]+$/.test(needle)) return ` ${text} `.includes(` ${needle} `);
  return text.includes(needle);
}

function entityAliases(entity) {
  const aliases = new Set([entity.id, entity.name]);
  const role = entity.state?.npcRole || entity.state?.role || "";
  if (role) aliases.add(role);
  for (const source of [entity.name, role]) {
    const tokens = searchable(source).match(/[a-z]{3,}|[가-힣]{2,}/g) || [];
    for (let token of tokens) {
      token = token.replace(/(?:으로|에서|에게|까지|부터|처럼|보다|이라|라고|에는|에게|한테|을|를|은|는|이|가|의)$/u, "");
      if (token.length >= 2 && !STOP_WORDS.has(token)) aliases.add(token);
    }
  }
  for (const [pattern, translations] of ROLE_TRANSLATIONS) if (pattern.test(role)) for (const alias of translations) aliases.add(alias);
  return [...aliases].filter(Boolean);
}

function mentionedEntities(run, text) {
  const specificIds = [];
  for (const entity of run.entities) {
    if (entityAliases(entity).some((alias) => containsAlias(text, alias))) specificIds.push(entity.id);
  }
  const genericKinds = Object.entries(KIND_ALIASES)
    .filter(([, aliases]) => aliases.some((alias) => containsAlias(text, alias)))
    .map(([kind]) => kind);
  return { specificIds: [...new Set(specificIds)].slice(0, 8), genericKinds: [...new Set(genericKinds)].slice(0, 4) };
}

function actionComponent(selected, detected, issues) {
  if (detected.length === 0) {
    issues.push("ability_unspecified");
    return 0.58;
  }
  if (!detected.includes(selected)) {
    issues.push("ability_mismatch");
    return 0.08;
  }
  if (detected.some((action) => action !== selected)) {
    issues.push("ability_conflict");
    return 0.62;
  }
  return 1;
}

function targetComponent(run, request, mentions, issues) {
  const targetCount = TARGET_COUNTS[request.ability] || 0;
  if (targetCount === 0) return null;
  const selectedIds = [request.targetEntityId, request.secondaryTargetEntityId].filter(Boolean).slice(0, targetCount);
  const selected = selectedIds.map((id) => run.entities.find((entity) => entity.id === id)).filter(Boolean);
  const mentionedSelected = selectedIds.filter((id) => mentions.specificIds.includes(id));
  const mentionedOther = mentions.specificIds.filter((id) => !selectedIds.includes(id));
  if (mentionedOther.length > 0) {
    issues.push("target_mismatch");
    return mentionedSelected.length === targetCount ? 0.38 : 0.05;
  }
  if (mentionedSelected.length === targetCount) return 1;
  if (mentionedSelected.length > 0) {
    if (targetCount === 2) issues.push("secondary_target_unspecified");
    return 0.68;
  }
  if (mentions.genericKinds.length > 0) {
    const allKindsMatch = selected.every((entity) => mentions.genericKinds.includes(entity.kind));
    if (!allKindsMatch) issues.push("target_mismatch");
    return allKindsMatch ? 0.72 : 0.2;
  }
  issues.push("target_unspecified");
  return 0.55;
}

function extractCoordinates(raw) {
  const values = [];
  const patterns = [
    /(?:\(|좌표\s*|coordinates?\s*)\s*(-?\d{1,3})\s*[,/]\s*(-?\d{1,3})\s*\)?/gi,
    /\bx\s*[:=]?\s*(-?\d{1,3})\s*[,;\s]+y\s*[:=]?\s*(-?\d{1,3})\b/gi
  ];
  for (const pattern of patterns) {
    for (const match of raw.matchAll(pattern)) {
      const point = { x: Number(match[1]), y: Number(match[2]) };
      if (!values.some((item) => item.x === point.x && item.y === point.y)) values.push(point);
      if (values.length >= 3) return values;
    }
  }
  return values;
}

function directionMatches(direction, from, to) {
  const dx = to.x - from.x;
  const dy = to.y - from.y;
  return direction === "east" ? dx > 0 : direction === "west" ? dx < 0 : direction === "north" ? dy < 0 : dy > 0;
}

function destinationComponent(run, request, raw, text, issues) {
  if (!DESTINATION_ACTIONS.has(request.ability)) return { score: null, evidence: { coordinates: [], directions: [], places: [] } };
  const coordinates = extractCoordinates(raw);
  const directions = DIRECTION_PATTERNS.filter(([, pattern]) => pattern.test(raw)).map(([direction]) => direction);
  const places = [];
  for (const point of run.world.points || []) if (containsAlias(text, point.name) || containsAlias(text, point.id)) places.push({ type: "point", id: point.id, x: point.x, y: point.y });
  for (const area of run.world.areas || []) if (containsAlias(text, area.name) || containsAlias(text, area.id.replace("area.", ""))) places.push({ type: "area", id: area.id });
  const evidence = { coordinates, directions: directions.slice(0, 4), places: places.slice(0, 4) };
  if (coordinates.length === 0 && directions.length === 0 && places.length === 0) {
    issues.push("destination_unspecified");
    return { score: 0.55, evidence };
  }
  const destination = request.destination;
  const player = run.entities.find((entity) => entity.id === run.playerEntityId);
  const checks = [];
  for (const coordinate of coordinates) checks.push(coordinate.x === destination.x && coordinate.y === destination.y);
  for (const direction of directions) checks.push(directionMatches(direction, player.position, destination));
  for (const place of places) {
    if (place.type === "point") checks.push(place.x === destination.x && place.y === destination.y);
    else {
      const area = run.world.areas.find((item) => item.id === place.id);
      checks.push(destination.x >= area.bounds.x && destination.y >= area.bounds.y && destination.x < area.bounds.x + area.bounds.width && destination.y < area.bounds.y + area.bounds.height);
    }
  }
  const matched = checks.filter(Boolean).length;
  if (matched < checks.length) issues.push(directions.length > 0 && coordinates.length === 0 && places.length === 0 ? "direction_mismatch" : "destination_mismatch");
  return { score: checks.length === 0 ? 0.55 : matched / checks.length, evidence };
}

function contextualSpecificity(request, raw, issues) {
  if (request.ability === "undo") {
    const explicit = /(?:\b(?:last|previous|prior)\s+(?:turn|action|command)\b|직전|이전|방금)/i.test(raw);
    if (!explicit) issues.push("reversal_scope_unspecified");
    return explicit ? 1 : 0.55;
  }
  if (request.ability === "rest") {
    const explicit = /(?:\b(?:focus|health|hp|energy|wound|recover)\b|집중|체력|상처|회복|기력)/i.test(raw);
    if (!explicit) issues.push("recovery_goal_unspecified");
    return explicit ? 1 : 0.65;
  }
  return null;
}

export function analyzeIntent({ run, request, legalExecution }) {
  const raw = String(request.intent || "").normalize("NFKC");
  const text = searchable(raw);
  const issues = [];
  const detectedActions = detectIntentActions(raw);
  const mentions = mentionedEntities(run, text);
  const abilityScore = actionComponent(request.ability, detectedActions, issues);
  const targetScore = targetComponent(run, request, mentions, issues);
  const destination = destinationComponent(run, request, raw, text, issues);
  const contextualScore = contextualSpecificity(request, raw, issues);
  const forbiddenGoals = FORBIDDEN_GOALS.filter(([, pattern]) => pattern.test(raw)).map(([code]) => code);
  for (const code of forbiddenGoals) issues.push(`forbidden_goal:${code}`);

  const components = [{ value: abilityScore, weight: 5 }];
  if (targetScore !== null) components.push({ value: targetScore, weight: 3 });
  if (destination.score !== null) components.push({ value: destination.score, weight: 2 });
  if (contextualScore !== null) components.push({ value: contextualScore, weight: 2 });
  components.push({ value: raw.trim().length >= 6 ? 1 : 0.45, weight: 1 });
  let score = components.reduce((sum, item) => sum + item.value * item.weight, 0) / components.reduce((sum, item) => sum + item.weight, 0);
  if (forbiddenGoals.length > 0) score *= 0.55;
  score = rounded(score);
  const status = score >= 0.8 ? "aligned" : score >= 0.55 ? "partial" : "mismatched";
  const statedGoal = boundedGoal(request.intent);
  const issueSummary = issues.length === 0 ? "none" : [...new Set(issues)].slice(0, 8).join(", ");
  const normalizedAttempt = `Stated goal: ${JSON.stringify(statedGoal)} | Server-authorized execution: ${legalExecution} | Intent fit: ${status} (${score}; ${issueSummary})`;
  return {
    score,
    status,
    statedGoal,
    selectedAction: request.ability,
    actionSource: request.abilitySource || "explicit_selection",
    detectedActions: detectedActions.slice(0, 6),
    mentionedEntityIds: mentions.specificIds,
    mentionedEntityKinds: mentions.genericKinds,
    destinationEvidence: destination.evidence,
    forbiddenGoals,
    issues: [...new Set(issues)].slice(0, 8),
    normalizedAttempt: normalizedAttempt.length <= 800 ? normalizedAttempt : normalizedAttempt.slice(0, 800)
  };
}

export function realizationAlignment(intentScore, mechanicalScore) {
  const mechanicalRealization = clamp((Math.max(-10, Math.min(10, mechanicalScore)) + 10) / 20);
  return rounded(intentScore * 0.7 + mechanicalRealization * 0.3);
}
