import { AppError } from "../errors.js";
import { areaAt, isWalkable } from "../domain/world.js";

export const PLAYER_ACTION_KINDS = Object.freeze([
  "DIALOGUE", "ATTITUDE", "SEARCH", "ACQUIRE", "ATTACK", "MOVE",
  "INTERACT", "NEGOTIATE", "REST", "USE_ITEM", "COMBINE",
  "COPY", "DELETE", "CONNECT", "RESTORE", "UNDO", "SELECT_ALL"
]);

const ROLL_ACTIONS = new Set(PLAYER_ACTION_KINDS.filter((kind) => !["DIALOGUE", "ATTITUDE"].includes(kind)));
const STEP_DIRECTIONS = Object.freeze([
  { ref: "step.north", name: "북쪽으로 한 걸음", dx: 0, dy: -1 },
  { ref: "step.east", name: "동쪽으로 한 걸음", dx: 1, dy: 0 },
  { ref: "step.south", name: "남쪽으로 한 걸음", dx: 0, dy: 1 },
  { ref: "step.west", name: "서쪽으로 한 걸음", dx: -1, dy: 0 }
]);

function distance(left, right) {
  return Math.abs(left.x - right.x) + Math.abs(left.y - right.y);
}

function direction(left, right) {
  const dx = right.x - left.x;
  const dy = right.y - left.y;
  if (dx === 0 && dy === 0) return "HERE";
  if (Math.abs(dx) >= Math.abs(dy)) return dx > 0 ? "EAST" : "WEST";
  return dy > 0 ? "SOUTH" : "NORTH";
}

function cleanText(value, maximum) {
  return String(value || "").normalize("NFKC").replace(/[\u0000-\u001f\u007f]/g, " ").replace(/\s+/g, " ").trim().slice(0, maximum);
}

export function playerActionContext(run, text) {
  const player = run.entities.find((entity) => entity.id === run.playerEntityId && entity.active);
  const visibleEntities = run.entities
    .filter((entity) => entity.active && entity.id !== run.playerEntityId && distance(entity.position, player.position) <= 8)
    .map((entity) => ({
      id: entity.id,
      name: entity.name,
      kind: entity.kind,
      distance: distance(entity.position, player.position),
      direction: direction(player.position, entity.position),
      disabled: entity.state?.disabled === true,
      hostile: entity.kind === "enemy" || run.npcRelationships?.some((relationship) => relationship.npcId === entity.id && relationship.stance === "hostile") === true
    }));
  const inventory = (player.state?.inventory || []).map((item) => ({
    id: item.id,
    name: item.name,
    kind: item.kind,
    description: item.description,
    quantity: Number(item.quantity || 1),
    protected: item.protected === true
  }));
  const occupied = new Set(run.entities
    .filter((entity) => entity.active && entity.blocking && entity.id !== player.id)
    .map((entity) => `${entity.position.x},${entity.position.y}`));
  const stepDestinations = STEP_DIRECTIONS
    .map((direction) => ({ ...direction, position: { x: player.position.x + direction.dx, y: player.position.y + direction.dy } }))
    .filter((destination) => isWalkable(run.world, destination.position) && !occupied.has(`${destination.position.x},${destination.position.y}`))
    .map(({ ref, name, position }) => ({ ref, name, distance: 1, direction: direction(player.position, position) }));
  const namedDestinations = (run.world?.points || [])
    .filter((point) => distance(point, player.position) > 0 && distance(point, player.position) <= 5)
    .map((point) => ({ ref: point.id, name: point.name || point.nameKo || point.id, distance: distance(point, player.position), direction: direction(player.position, point) }))
    .slice(0, 12);
  const destinations = [...stepDestinations, ...namedDestinations].slice(0, 16);
  const area = areaAt(run.world, player.position);
  const biome = (run.world?.biomes || []).find((item) => item.id === area?.biomeId);
  return {
    requestType: "PLAYER_ACTION_PROPOSAL",
    // The public /messages contract accepts up to 1,000 characters. Preserve that
    // complete accepted input here as well: silently clipping the classifier context
    // at 400 characters can discard a player's actual intent when it appears after
    // scene-setting prose near the end of a long Korean sentence.
    playerText: cleanText(text, 1000),
    currentArea: area?.name || null,
    spatialContext: {
      authority: "SERVER", areaId: area?.id || null, areaName: area?.nameKo || area?.name || null,
      biomeId: area?.biomeId || null, biomeName: biome?.nameKo || biome?.name || null,
      campaignRole: area?.campaignRole || null, facing: player.state?.facing || "SOUTH"
    },
    visibleEntities,
    inventory,
    destinations,
    allowedKinds: PLAYER_ACTION_KINDS
  };
}

export function validatePlayerActionProposal(input, context) {
  if (!input || typeof input !== "object" || Array.isArray(input)) throw new AppError(502, "PLAYER_ACTION_INVALID", "Player action proposal must be an object.");
  const allowedKeys = ["kind", "targetEntityIds", "itemIds", "destinationRef", "resultItem", "reason"];
  const unknown = Object.keys(input).filter((key) => !allowedKeys.includes(key));
  if (unknown.length > 0) throw new AppError(502, "PLAYER_ACTION_INVALID", `Unknown player action fields: ${unknown.join(", ")}.`);
  const kind = String(input.kind || "").toUpperCase();
  if (!PLAYER_ACTION_KINDS.includes(kind)) throw new AppError(502, "PLAYER_ACTION_INVALID", "Unknown player action kind.");
  const entityIds = Array.isArray(input.targetEntityIds) ? [...new Set(input.targetEntityIds.map(String))] : [];
  const itemIds = Array.isArray(input.itemIds) ? [...new Set(input.itemIds.map(String))] : [];
  if (entityIds.length > 2 || !entityIds.every((id) => context.visibleEntities.some((entity) => entity.id === id))) throw new AppError(502, "PLAYER_ACTION_TARGET_INVALID", "Player action referenced an unavailable entity.");
  if (itemIds.length > 2 || !itemIds.every((id) => context.inventory.some((item) => item.id === id))) throw new AppError(502, "PLAYER_ACTION_ITEM_INVALID", "Player action referenced an unowned item.");
  const destinationRef = input.destinationRef == null ? null : String(input.destinationRef);
  if (destinationRef !== null && !context.destinations.some((item) => item.ref === destinationRef)) throw new AppError(502, "PLAYER_ACTION_DESTINATION_INVALID", "Player action referenced an unavailable destination.");
  const requiredEntityCount = { ATTACK: 1, INTERACT: 1, NEGOTIATE: 1 }[kind] ?? null;
  if (requiredEntityCount !== null && entityIds.length !== requiredEntityCount) throw new AppError(502, "PLAYER_ACTION_TARGET_INVALID", `${kind} requires ${requiredEntityCount} entity target.`);
  const primaryTarget = entityIds.length > 0 ? context.visibleEntities.find((entity) => entity.id === entityIds[0]) : null;
  if (kind === "ATTACK" && (!primaryTarget?.hostile || primaryTarget.disabled || primaryTarget.distance > 1)) throw new AppError(502, "PLAYER_ACTION_TARGET_INVALID", "ATTACK requires one adjacent active hostile target.");
  if (kind === "INTERACT" && (!primaryTarget || primaryTarget.hostile || !["npc", "prop"].includes(primaryTarget.kind) || primaryTarget.distance > 2)) throw new AppError(502, "PLAYER_ACTION_TARGET_INVALID", "INTERACT requires one nearby non-hostile NPC or prop.");
  if (kind === "NEGOTIATE" && (!primaryTarget || primaryTarget.hostile || primaryTarget.kind !== "npc" || primaryTarget.distance > 2)) throw new AppError(502, "PLAYER_ACTION_TARGET_INVALID", "NEGOTIATE requires one nearby non-hostile NPC.");
  if (kind === "MOVE" && !destinationRef) throw new AppError(502, "PLAYER_ACTION_DESTINATION_INVALID", "MOVE requires one supplied destination.");
  if (kind === "USE_ITEM" && itemIds.length !== 1) throw new AppError(502, "PLAYER_ACTION_ITEM_INVALID", "USE_ITEM requires one owned item.");
  if (kind === "COMBINE" && itemIds.length !== 2) throw new AppError(502, "PLAYER_ACTION_ITEM_INVALID", "COMBINE requires two distinct owned items.");
  if (!ROLL_ACTIONS.has(kind) && itemIds.length > 0) throw new AppError(502, "PLAYER_ACTION_ITEM_INVALID", "Only resolved actions may bind inventory IDs.");
  let resultItem = null;
  if (input.resultItem != null) {
    if (!input.resultItem || typeof input.resultItem !== "object" || Array.isArray(input.resultItem)) throw new AppError(502, "PLAYER_ACTION_RESULT_ITEM_INVALID", "resultItem must be an object or null.");
    const resultUnknown = Object.keys(input.resultItem).filter((key) => !["name", "kind", "description"].includes(key));
    if (resultUnknown.length > 0) throw new AppError(502, "PLAYER_ACTION_RESULT_ITEM_INVALID", "resultItem contains unknown fields.");
    const name = cleanText(input.resultItem.name, 60);
    const itemKind = String(input.resultItem.kind || "salvage").toLowerCase();
    const description = cleanText(input.resultItem.description, 180);
    if (!name || !/[가-힣A-Za-z0-9]/u.test(name) || !["salvage", "material", "tool", "consumable", "key_item"].includes(itemKind)) throw new AppError(502, "PLAYER_ACTION_RESULT_ITEM_INVALID", "resultItem is outside the bounded item contract.");
    resultItem = { name, kind: itemKind, description: description || `${name}에 관한 장면 확정 아이템.` };
  }
  if (kind === "ACQUIRE" && resultItem === null) throw new AppError(502, "PLAYER_ACTION_RESULT_ITEM_INVALID", "ACQUIRE requires a bounded proposed result item.");
  if (!["ACQUIRE", "COMBINE"].includes(kind) && resultItem !== null) throw new AppError(502, "PLAYER_ACTION_RESULT_ITEM_INVALID", "Only acquisition or combination may propose a result item.");
  return {
    kind,
    requiresRoll: ROLL_ACTIONS.has(kind),
    targetEntityIds: entityIds,
    itemIds,
    destinationRef,
    resultItem,
    reason: cleanText(input.reason, 160) || "자연어 전체 맥락에서 행동을 분류했다.",
    source: "llm_structured_proposal"
  };
}

export function playerActionRejectionReason(proposal, context, code = "PLAYER_ACTION_INVALID") {
  const kind = String(proposal?.kind || "").toUpperCase();
  const targetId = Array.isArray(proposal?.targetEntityIds) ? proposal.targetEntityIds[0] : null;
  const target = targetId ? context?.visibleEntities?.find((entity) => entity.id === targetId) : null;
  const itemIds = Array.isArray(proposal?.itemIds) ? proposal.itemIds : [];

  if (kind === "ATTACK") {
    if (!target) return "공격할 대상을 현재 시야에서 확정하지 못했다. 가까운 적을 선택하거나 먼저 주변을 조사해야 한다.";
    if (target.disabled) return `${target.name}은 이미 행동 불능 상태라 다시 공격할 수 없다. 다른 활성 적을 선택해야 한다.`;
    if (!target.hostile) return `${target.name}은 현재 적대 대상으로 확정되지 않아 공격할 수 없다. 대화하거나 상황을 먼저 확인해야 한다.`;
    if (Number(target.distance) > 1) {
      return `${target.name}까지 ${target.distance}칸 떨어져 있어 공격 범위 1칸에 닿지 않는다. 먼저 대상 쪽으로 한 칸 가까이 이동해야 한다.`;
    }
  }
  if (["INTERACT", "NEGOTIATE"].includes(kind) && target && Number(target.distance) > 2) {
    const action = kind === "NEGOTIATE" ? "대화" : "상호작용";
    return `${target.name}까지 ${target.distance}칸 떨어져 있어 ${action} 범위 2칸에 닿지 않는다. 먼저 대상 쪽으로 가까이 이동해야 한다.`;
  }
  if (["USE_ITEM", "COMBINE"].includes(kind) && itemIds.length < (kind === "COMBINE" ? 2 : 1)) {
    return kind === "COMBINE"
      ? "조합에 필요한 소지품 두 개를 실제 인벤토리에서 모두 찾을 수 없다. 인벤토리에서 재료 두 개를 선택해야 한다."
      : "사용하려는 물건을 실제 인벤토리에서 찾을 수 없다. 보유한 아이템을 선택해야 한다.";
  }

  return {
    INVENTORY_ITEM_PROTECTED: "관리자 키보드는 보호된 핵심 도구라서 조합 재료처럼 소모할 수 없다.",
    INVENTORY_ITEM_NOT_OWNED: "필요한 소지품을 실제로 보유하고 있지 않다.",
    OUT_OF_RANGE: "대상이 지금 위치에서 행동할 수 있는 범위 밖에 있다. 대상 쪽으로 가까이 이동해야 한다.",
    PATH_BLOCKED: "현재 위치에서는 그곳까지 이어지는 안전한 경로가 없다. 다른 방향으로 이동해야 한다.",
    TARGET_NOT_HOSTILE: "그 대상은 지금 공격 가능한 적으로 확정되지 않았다. 대화하거나 상황을 먼저 확인해야 한다.",
    TARGET_NOT_INTERACTABLE: "그 대상과는 현재 방식으로 상호작용할 수 없다. 다른 대상이나 행동을 선택해야 한다.",
    INSUFFICIENT_FOCUS: "지금은 이 행동을 실행할 집중력이 부족하다. 휴식하거나 비용이 낮은 행동을 선택해야 한다."
  }[code] || "현재 장면과 시스템 상태에서는 이 시도를 실행할 수 없다. 다른 대상이나 행동을 선택해야 한다.";
}

function semanticText(value) {
  return cleanText(value, 1000).toLowerCase()
    .replace(/공걱|공겍/gu, "공격")
    .replace(/이돟|이돈/gu, "이동")
    .replace(/협삽|협샹/gu, "협상")
    .replace(/복언|복웡/gu, "복원")
    .replace(/삭재/gu, "삭제");
}

function closestEntity(context, predicate, text = "") {
  const candidates = context.visibleEntities.filter(predicate)
    .sort((left, right) => left.distance - right.distance || String(left.id).localeCompare(String(right.id)));
  return candidates.find((entity) => entity.name && text.includes(semanticText(entity.name))) || candidates[0] || null;
}

function destinationForText(context, text) {
  const named = context.destinations.find((candidate) =>
    candidate.name && text.includes(semanticText(candidate.name)));
  if (named) return named;

  let directionRef = null;
  if (/(?:북쪽|북으로|위쪽|위로|north)/u.test(text)) directionRef = "step.north";
  else if (/(?:동쪽|동으로|오른쪽|우측|east)/u.test(text)) directionRef = "step.east";
  else if (/(?:남쪽|남으로|아래쪽|아래로|south)/u.test(text)) directionRef = "step.south";
  else if (/(?:서쪽|서으로|왼쪽|좌측|west)/u.test(text)) directionRef = "step.west";
  else if (/(?:앞으로|전진)/u.test(text)) {
    const facing = String(context.spatialContext?.facing || "").toLowerCase();
    directionRef = ["north", "east", "south", "west"].includes(facing) ? `step.${facing}` : null;
  }
  return context.destinations.find((candidate) => candidate.ref === directionRef)
    || context.destinations[0]
    || null;
}

function rejectedFallback(kind, reason, alternative) {
  return {
    kind: "DIALOGUE",
    requiresRoll: false,
    targetEntityIds: [],
    itemIds: [],
    destinationRef: null,
    resultItem: null,
    reason,
    source: "deterministic_semantic_fallback",
    rejectedAction: {
      kind,
      code: "PLAYER_ACTION_NOT_CURRENTLY_POSSIBLE",
      reason: alternative ? `${reason} ${alternative}` : reason,
      itemNames: []
    }
  };
}

export function fallbackPlayerActionProposal(context) {
  const text = semanticText(context.playerText);
  let kind = "DIALOGUE";
  if (/(?:순간\s*이동|텔레포트|하늘을?\s*날|벽을?\s*뚫|맵\s*밖|무적|세계.*(?:삭제|파괴))/u.test(text)) {
    return rejectedFallback("IMPOSSIBLE_WORLD_ACTION",
      "현재 위치와 관리자 키보드의 권한으로는 그 행동을 실행할 수 없습니다.",
      "가까운 타일로 이동하거나 보이는 대상에 SEARCH·CONNECT를 사용해 보세요.");
  }
  if (/(?:꺼내|집어|주워|줍|챙기|획득|손에\s*넣|얻어|take|pick\s*up)/u.test(text)
    || (/(?:아이템|물건|도구|재료|파편|보물|전리품)/u.test(text) && /(?:찾아|탐색|수색|발견)/u.test(text))) kind = "ACQUIRE";
  else if (/(?:모두\s*선택|전부\s*(?:공격|삭제)|광역|select\s*all)/u.test(text)) kind = "SELECT_ALL";
  else if (/(?:공격|때리|타격|싸우|베어|내려쳐|attack|hit)/u.test(text)) kind = "ATTACK";
  else if (/(?:조합|합치|섞어|결합|combine|craft)/u.test(text)) kind = "COMBINE";
  else if (/(?:사용|먹어|마셔|use)/u.test(text) && context.inventory.some((item) => text.includes(semanticText(item.name)))) kind = "USE_ITEM";
  else if (/(?:복원|복구|되살|restore|repair)/u.test(text)) kind = "RESTORE";
  else if (/(?:되돌|되감|시간.*(?:돌|역행)|undo)/u.test(text)) kind = "UNDO";
  else if (/(?:삭제|지워|제거|delete)/u.test(text)) kind = "DELETE";
  else if (/(?:복사|복제|copy)/u.test(text)) kind = "COPY";
  else if (/(?:연결|이어\s*주|잇고|connect|link)/u.test(text)) kind = "CONNECT";
  else if (/(?:협상|설득|타협|거래|흥정|negotiate|persuade)/u.test(text)) kind = "NEGOTIATE";
  else if (/(?:상호\s*작용|말\s*걸|대화해|열어|만져|확인해|interact|talk\s*to)/u.test(text)) kind = "INTERACT";
  else if (/(?:휴식|쉬자|쉬어|쉰다|숨\s*고르|회복하|rest)/u.test(text)) kind = "REST";
  else if (/(?:탐색|조사|수색|살펴|찾아|둘러보|search|inspect)/u.test(text) && !/(?:묻|질문|말해)/u.test(text)) kind = "SEARCH";
  else if (/(?:이동|향하|간다|가자|다가가|걸어|북쪽|남쪽|동쪽|서쪽|왼쪽|오른쪽|위로|아래로|move|go\s)/u.test(text)) kind = "MOVE";

  const target = kind === "ATTACK"
    ? closestEntity(context, (entity) => entity.hostile && !entity.disabled && entity.distance <= 1, text)
    : kind === "INTERACT"
      ? closestEntity(context, (entity) => !entity.hostile && ["prop", "npc"].includes(entity.kind) && entity.distance <= 2, text)
      : kind === "NEGOTIATE"
        ? closestEntity(context, (entity) => !entity.hostile && entity.kind === "npc" && entity.distance <= 2, text)
        : ["COPY", "DELETE", "RESTORE", "SEARCH"].includes(kind)
          ? closestEntity(context, (entity) => !entity.disabled, text)
          : null;
  const mentionedItems = context.inventory
    .filter((item) => text.includes(semanticText(item.name)))
    .slice(0, kind === "COMBINE" ? 2 : 1);
  if (kind === "ATTACK" && !target) {
    return rejectedFallback("ATTACK", "바로 공격할 수 있는 인접한 적이 없습니다.",
      "먼저 적 가까이 이동하거나 주변을 조사해 주세요.");
  }
  if (kind === "INTERACT" && !target) {
    return rejectedFallback("INTERACT", "두 칸 안에 상호작용할 수 있는 NPC나 물체가 없습니다.",
      "가까이 이동한 뒤 다시 시도해 주세요.");
  }
  if (kind === "NEGOTIATE" && !target) {
    return rejectedFallback("NEGOTIATE", "두 칸 안에 협상 가능한 비적대 NPC가 없습니다.",
      "대화 상대에게 가까이 이동하거나 SEARCH로 주변을 확인해 주세요.");
  }
  if (kind === "COMBINE" && mentionedItems.length !== 2) {
    return rejectedFallback("COMBINE", "조합하려면 소지품 이름 두 개를 함께 입력해야 합니다.",
      "인벤토리에서 두 재료를 확인한 뒤 ‘A와 B를 조합’처럼 입력해 주세요.");
  }
  if (kind === "USE_ITEM" && mentionedItems.length !== 1) {
    return rejectedFallback("USE_ITEM", "사용할 소지품을 정확히 찾지 못했습니다.",
      "인벤토리의 아이템 이름을 문장에 포함해 주세요.");
  }
  const destination = kind === "MOVE" ? destinationForText(context, text) : null;
  if (kind === "MOVE" && !destination) {
    return rejectedFallback("MOVE", "현재 위치에서 바로 이동할 수 있는 방향이 없습니다.",
      "다른 방향을 고르거나 주변 대상과 상호작용해 주세요.");
  }
  return {
    kind,
    requiresRoll: ROLL_ACTIONS.has(kind),
    targetEntityIds: target ? [target.id] : [],
    itemIds: ["USE_ITEM", "COMBINE"].includes(kind) ? mentionedItems.map((item) => item.id) : [],
    destinationRef: destination?.ref || null,
    resultItem: kind === "ACQUIRE" ? { name: "균열에서 건져낸 금속성 파편", kind: "material", description: "불안정한 공간에서 확보한 차가운 금속성 파편." } : null,
    reason: "모델 행동 분류를 사용할 수 없어 제한된 의미 폴백을 적용했다.",
    source: "deterministic_semantic_fallback"
  };
}

export function resolvePlayerActionDestination(run, destinationRef) {
  const player = run.entities.find((entity) => entity.id === run.playerEntityId && entity.active);
  const direction = STEP_DIRECTIONS.find((candidate) => candidate.ref === destinationRef);
  if (direction) return { x: player.position.x + direction.dx, y: player.position.y + direction.dy };
  const point = (run.world?.points || []).find((candidate) => candidate.id === destinationRef);
  return point ? { x: point.x, y: point.y } : null;
}
