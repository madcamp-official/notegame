import test from "node:test";
import assert from "node:assert/strict";
import { randomUUID } from "node:crypto";
import { createCampaignBlueprint } from "../src/domain/campaign.js";
import { createRunState } from "../src/domain/turn-engine.js";
import { generateWorld } from "../src/domain/world.js";
import { fallbackPlayerActionProposal, playerActionContext, resolvePlayerActionDestination, validatePlayerActionProposal } from "../src/llm/player-action.js";

function runFixture(seed = 91001) {
  const blueprint = createCampaignBlueprint({ worldSeed: seed, turnLimit: 40 });
  return createRunState({
    campaign: { id: randomUUID(), ownerId: randomUUID(), title: "Player action fixture", turnLimit: 40, ...blueprint, world: generateWorld(seed) },
    ownerId: randomUUID(),
    resolutionSeed: `player-action-${seed}`
  });
}

test("player action context exposes a real area and legal one-step movement without leaking coordinates", () => {
  const run = runFixture();
  const context = playerActionContext(run, "동쪽으로 이동한다");
  assert.equal(typeof context.currentArea, "string");
  assert.ok(context.currentArea.length > 0);
  const step = context.destinations.find((destination) => destination.ref.startsWith("step."));
  assert.ok(step);
  assert.deepEqual(Object.keys(step).sort(), ["direction", "distance", "name", "ref"]);
  assert.equal(context.spatialContext.authority, "SERVER");
  assert.ok(context.spatialContext.areaId);
  assert.ok(context.destinations.every((item) => !Object.hasOwn(item, "position") && !Object.hasOwn(item, "x") && !Object.hasOwn(item, "y")));
  const destination = resolvePlayerActionDestination(run, step.ref);
  const player = run.entities.find((entity) => entity.id === run.playerEntityId);
  assert.equal(Math.abs(destination.x - player.position.x) + Math.abs(destination.y - player.position.y), 1);
});

test("structured attack proposals reject visible but non-adjacent targets before turn resolution", () => {
  const run = runFixture(91002);
  const player = run.entities.find((entity) => entity.id === run.playerEntityId);
  const enemy = run.entities.find((entity) => entity.kind === "enemy" && entity.active);
  enemy.position = { x: player.position.x + 3, y: player.position.y };
  const context = playerActionContext(run, `${enemy.name}을 공격한다`);
  assert.throws(() => validatePlayerActionProposal({
    kind: "ATTACK", targetEntityIds: [enemy.id], itemIds: [], destinationRef: null, resultItem: null, reason: "공격 시도"
  }, context), (error) => error?.code === "PLAYER_ACTION_TARGET_INVALID");
  const fallback = fallbackPlayerActionProposal(context);
  assert.equal(fallback.kind, "DIALOGUE");
  assert.equal(fallback.requiresRoll, false);
});
