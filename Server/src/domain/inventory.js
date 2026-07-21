import { assert, AppError } from "../errors.js";
import { clone } from "./serialization.js";

const ACTIONS = new Set(["USE", "DROP", "TRANSFER_IN", "TRANSFER_OUT"]);

export function normalizeInventoryRequest(input) {
  assert(input && typeof input === "object" && !Array.isArray(input), 400, "INVENTORY_REQUEST_INVALID", "A JSON inventory request is required.");
  const allowed = new Set(["action", "itemId", "otherEntityId", "quantity", "expectedRunVersion"]);
  const unknown = Object.keys(input).filter((key) => !allowed.has(key));
  assert(unknown.length === 0, 400, "INVENTORY_REQUEST_INVALID", `Unknown fields: ${unknown.join(", ")}.`);
  const action = String(input.action || "").toUpperCase();
  assert(ACTIONS.has(action), 400, "INVENTORY_ACTION_INVALID", "Unknown inventory action.");
  assert(typeof input.itemId === "string" && input.itemId.length >= 3 && input.itemId.length <= 100, 400, "INVENTORY_ITEM_INVALID", "itemId is invalid.");
  assert(Number.isSafeInteger(input.expectedRunVersion) && input.expectedRunVersion >= 1, 400, "RUN_VERSION_INVALID", "expectedRunVersion is invalid.");
  const quantity = input.quantity === undefined ? 1 : input.quantity;
  assert(Number.isInteger(quantity) && quantity >= 1 && quantity <= 99, 400, "INVENTORY_QUANTITY_INVALID", "quantity must be between 1 and 99.");
  const otherEntityId = input.otherEntityId == null ? null : String(input.otherEntityId);
  if (action.startsWith("TRANSFER")) assert(otherEntityId && otherEntityId.length >= 3, 400, "INVENTORY_ENTITY_INVALID", "Transfers require otherEntityId.");
  return { action, itemId: input.itemId, otherEntityId, quantity, expectedRunVersion: input.expectedRunVersion };
}

function distance(a, b) { return Math.abs(a.x - b.x) + Math.abs(a.y - b.y); }

export function resolveInventoryAction(originalRun, request, now = new Date().toISOString()) {
  const run = clone(originalRun);
  assert(run.status === "active", 409, "RUN_NOT_ACTIVE", "The run does not accept inventory actions.");
  const player = run.entities.find((entity) => entity.id === run.playerEntityId && entity.active);
  assert(player, 500, "PLAYER_MISSING", "The player entity is missing.");
  player.state.inventory ||= [];
  let source = player;
  let destination = null;
  if (request.action === "TRANSFER_IN") {
    source = run.entities.find((entity) => entity.id === request.otherEntityId && entity.active);
    destination = player;
  } else if (request.action === "TRANSFER_OUT") {
    destination = run.entities.find((entity) => entity.id === request.otherEntityId && entity.active);
  }
  if (request.action.startsWith("TRANSFER")) {
    assert(source && destination && source.id !== destination.id, 422, "INVENTORY_TRANSFER_INVALID", "Transfer entities are invalid.");
    assert(distance(source.position, destination.position) <= 1, 422, "INVENTORY_TRANSFER_TOO_FAR", "Inventory transfers require adjacent entities.");
    source.state.inventory ||= [];
    destination.state.inventory ||= [];
  }
  const sourceInventory = source.state.inventory;
  const index = sourceInventory.findIndex((item) => item.id === request.itemId);
  const item = index >= 0 ? sourceInventory[index] : null;
  assert(item, 422, "INVENTORY_ITEM_NOT_OWNED", "The source does not own that item.");
  assert(Number(item.quantity || 1) >= request.quantity, 422, "INVENTORY_QUANTITY_UNAVAILABLE", "The source does not own that quantity.");
  const events = [];
  if (request.action === "USE") {
    assert(item.kind === "consumable" && !item.protected, 422, "INVENTORY_ITEM_NOT_USABLE", "That item cannot be consumed.");
    if (item.effect === "restore_focus") run.focus = Math.min(run.maxFocus || 10, run.focus + Number(item.effectValue || 2));
    else throw new AppError(422, "INVENTORY_EFFECT_UNKNOWN", "The item has no coded use effect.");
    events.push({ type: "inventory_item_used", itemId: item.id, itemName: item.name, quantity: request.quantity });
  } else if (request.action === "DROP") {
    assert(!item.protected, 422, "INVENTORY_ITEM_PROTECTED", "That item cannot be dropped.");
    events.push({ type: "inventory_item_dropped", itemId: item.id, itemName: item.name, quantity: request.quantity });
  } else {
    assert(!item.protected, 422, "INVENTORY_ITEM_PROTECTED", "That item cannot be transferred.");
    const moved = { ...clone(item), quantity: request.quantity };
    const existing = destination.state.inventory.find((candidate) => candidate.id === moved.id);
    if (existing) existing.quantity = Number(existing.quantity || 1) + request.quantity;
    else destination.state.inventory.push(moved);
    events.push({ type: "inventory_item_transferred", itemId: item.id, itemName: item.name, quantity: request.quantity, fromEntityId: source.id, toEntityId: destination.id });
  }
  if (request.action !== "TRANSFER_IN" || source.id !== player.id) {
    if (Number(item.quantity || 1) === request.quantity) sourceInventory.splice(index, 1);
    else item.quantity -= request.quantity;
  }
  run.inventoryHistory ||= [];
  run.inventoryHistory.push(...events.map((event) => ({ ...event, turnNo: run.currentTurn, createdAt: now })));
  run.version += 1;
  run.updatedAt = now;
  return { run, inventoryAction: { action: request.action, itemId: request.itemId, quantity: request.quantity, campaignTurnConsumed: false, events, createdAt: now } };
}
