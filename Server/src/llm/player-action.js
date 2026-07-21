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
    playerText: cleanText(text, 400),
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

export function fallbackPlayerActionProposal(context) {
  const text = context.playerText;
  let kind = "DIALOGUE";
  if (/(?:꺼내|집어|줍|챙기|획득|손에\s*넣)/u.test(text)
    || (/(?:아이템|물건|도구|재료|파편|보물|전리품)/u.test(text) && /(?:찾아|탐색|수색|발견)/u.test(text))) kind = "ACQUIRE";
  else if (/(?:공격|때리|타격|싸우)/u.test(text)) kind = "ATTACK";
  else if (/(?:조합|합치|섞어|결합)/u.test(text)) kind = "COMBINE";
  else if (/(?:사용|먹어|마셔)/u.test(text) && context.inventory.some((item) => text.includes(item.name))) kind = "USE_ITEM";
  else if (/(?:탐색|조사|수색|살펴|찾아|둘러보)/u.test(text) && !/(?:묻|질문|말해)/u.test(text)) kind = "SEARCH";
  else if (/(?:이동|향하|간다|다가가)/u.test(text) && context.destinations.length > 0) kind = "MOVE";
  const target = kind === "ATTACK"
    ? context.visibleEntities.find((entity) => entity.hostile && !entity.disabled && entity.distance <= 1)
    : kind === "INTERACT"
      ? context.visibleEntities.find((entity) => !entity.hostile && ["prop", "npc"].includes(entity.kind) && entity.distance <= 2)
      : kind === "NEGOTIATE"
        ? context.visibleEntities.find((entity) => !entity.hostile && entity.kind === "npc" && entity.distance <= 2)
        : null;
  const mentionedItems = context.inventory.filter((item) => text.includes(item.name)).slice(0, kind === "COMBINE" ? 2 : 1);
  if ((kind === "ATTACK" && !target) || (kind === "COMBINE" && mentionedItems.length !== 2) || (kind === "USE_ITEM" && mentionedItems.length !== 1)) kind = "DIALOGUE";
  return {
    kind,
    requiresRoll: ROLL_ACTIONS.has(kind),
    targetEntityIds: target ? [target.id] : [],
    itemIds: ["USE_ITEM", "COMBINE"].includes(kind) ? mentionedItems.map((item) => item.id) : [],
    destinationRef: kind === "MOVE" ? context.destinations[0]?.ref || null : null,
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
