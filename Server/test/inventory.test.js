import test from "node:test";
import assert from "node:assert/strict";
import { createCampaignBlueprint } from "../src/domain/campaign.js";
import { generateWorld } from "../src/domain/world.js";
import { createRunState, publicRun } from "../src/domain/turn-engine.js";
import { resolveInventoryAction } from "../src/domain/inventory.js";

const OWNER_ID = "bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb";

function runFixture() {
  const seed = 91021;
  return createRunState({
    campaign: { ...createCampaignBlueprint({ worldSeed: seed, turnLimit: 40 }), id: "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa", world: generateWorld(seed) },
    ownerId: OWNER_ID,
    runId: "cccccccc-cccc-4ccc-8ccc-cccccccccccc"
  });
}

test("a run starts with the protected administrator keyboard in server-owned inventory", () => {
  const dto = publicRun(runFixture());
  assert.equal(dto.inventory.length, 1);
  assert.equal(dto.inventory[0].name, "관리자 키보드");
  assert.equal(dto.inventory[0].protected, true);
});

test("a run starts with one world quest and keeps story hooks as future templates", () => {
  const run = runFixture();
  const dto = publicRun(run);
  assert.equal(dto.activeQuests.length, 1);
  assert.equal(dto.activeQuests[0].questKind, "world_thread");
  assert.equal(dto.activeQuests[0].currentStep, "opening");
  assert.equal(dto.activeQuests[0].acceptsNewSteps, true);
  assert.ok(run.questTemplates.length >= 1);
});

test("inventory actions reject nonexistent and protected items", () => {
  const run = runFixture();
  assert.throws(() => resolveInventoryAction(run, { action: "DROP", itemId: "missing-item", quantity: 1 }), (error) => error.code === "INVENTORY_ITEM_NOT_OWNED");
  const keyboard = run.entities.find((entity) => entity.id === run.playerEntityId).state.inventory[0];
  assert.throws(() => resolveInventoryAction(run, { action: "DROP", itemId: keyboard.id, quantity: 1 }), (error) => error.code === "INVENTORY_ITEM_PROTECTED");
});

test("coded consumable use changes authoritative state without consuming a story turn", () => {
  const run = runFixture();
  const player = run.entities.find((entity) => entity.id === run.playerEntityId);
  player.state.inventory.push({ id: "item-focus-shard", kind: "consumable", name: "집중 조각", quantity: 1, effect: "restore_focus", effectValue: 3, protected: false });
  run.focus = 4;
  const result = resolveInventoryAction(run, { action: "USE", itemId: "item-focus-shard", quantity: 1 });
  assert.equal(result.run.focus, 7);
  assert.equal(result.run.currentTurn, run.currentTurn);
  assert.equal(result.run.version, run.version + 1);
  assert.ok(!result.run.entities.find((entity) => entity.id === run.playerEntityId).state.inventory.some((item) => item.id === "item-focus-shard"));
});

test("transfer-in succeeds only for an item actually owned by an adjacent entity", () => {
  const run = runFixture();
  const player = run.entities.find((entity) => entity.id === run.playerEntityId);
  const npc = run.entities.find((entity) => entity.kind === "npc");
  npc.position = { x: player.position.x + 1, y: player.position.y };
  npc.state.inventory.push({ id: "npc-real-item", kind: "clue", name: "실재하는 편지", quantity: 1, protected: false });
  const result = resolveInventoryAction(run, { action: "TRANSFER_IN", itemId: "npc-real-item", otherEntityId: npc.id, quantity: 1 });
  const nextPlayer = result.run.entities.find((entity) => entity.id === run.playerEntityId);
  const nextNpc = result.run.entities.find((entity) => entity.id === npc.id);
  assert.ok(nextPlayer.state.inventory.some((item) => item.id === "npc-real-item"));
  assert.ok(!nextNpc.state.inventory.some((item) => item.id === "npc-real-item"));
});
