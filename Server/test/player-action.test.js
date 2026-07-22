import test from "node:test";
import assert from "node:assert/strict";
import { randomUUID } from "node:crypto";
import { createCampaignBlueprint } from "../src/domain/campaign.js";
import { createRunState } from "../src/domain/turn-engine.js";
import { generateWorld } from "../src/domain/world.js";
import { fallbackPlayerActionProposal, playerActionContext, playerActionRejectionReason, requestedPlayerMovementDestination, resolvePlayerActionDestination, validatePlayerActionProposal } from "../src/llm/player-action.js";

function runFixture(seed = 91001) {
  const blueprint = createCampaignBlueprint({ worldSeed: seed, turnLimit: 40 });
  const run = createRunState({
    campaign: { id: randomUUID(), ownerId: randomUUID(), title: "Player action fixture", turnLimit: 40, ...blueprint, world: generateWorld(seed) },
    ownerId: randomUUID(),
    resolutionSeed: `player-action-${seed}`
  });
  const tutorialEnemy = run.entities.find((item) => item.state?.tutorialEncounter === true);
  if (tutorialEnemy) {
    tutorialEnemy.position = structuredClone(tutorialEnemy.state.originPosition);
    tutorialEnemy.state.slotId = tutorialEnemy.state.originSlotId;
    tutorialEnemy.state.revealed = false;
    tutorialEnemy.state.tutorialEncounter = false;
  }
  run.activeEncounter = null;
  run.encounterHistory = [];
  return run;
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
  assert.equal(fallback.rejectedAction.kind, "ATTACK");
  assert.match(fallback.rejectedAction.reason, /가까이 이동/u);
});

test("structured attack rejection preserves target, distance, range, and recovery guidance", () => {
  const run = runFixture(91006);
  const player = run.entities.find((entity) => entity.id === run.playerEntityId);
  const enemy = run.entities.find((entity) => entity.kind === "enemy" && entity.active);
  enemy.position = { x: player.position.x + 2, y: player.position.y };
  const context = playerActionContext(run, `${enemy.name}을 공격한다`);
  const proposal = { kind: "ATTACK", targetEntityIds: [enemy.id], itemIds: [] };
  const reason = playerActionRejectionReason(proposal, context, "PLAYER_ACTION_TARGET_INVALID");

  assert.match(reason, new RegExp(enemy.name, "u"));
  assert.match(reason, /2칸/u);
  assert.match(reason, /공격 범위 1칸/u);
  assert.match(reason, /가까이 이동/u);
});

test("deterministic natural-language fallback respects the requested movement direction", () => {
  const run = runFixture(91003);
  const context = playerActionContext(run, "오른쪽, 그러니까 동쪽으로 한 칸 이동할게");
  const fallback = fallbackPlayerActionProposal(context);
  assert.equal(fallback.kind, "MOVE");
  assert.equal(fallback.destinationRef, "step.east");

  const typoContext = playerActionContext(run, "동쪽으로 이돟한다");
  const typoFallback = fallbackPlayerActionProposal(typoContext);
  assert.equal(typoFallback.kind, "MOVE");
  assert.equal(typoFallback.destinationRef, "step.east");
});

test("natural-language retreat chooses a legal step away from the nearest hostile", () => {
  const context = {
    playerText: "몬스터에게서 도망간다",
    spatialContext: { facing: "EAST" },
    destinations: [
      { ref: "step.north", name: "북쪽으로 한 걸음", distance: 1, direction: "NORTH" },
      { ref: "step.west", name: "서쪽으로 한 걸음", distance: 1, direction: "WEST" }
    ],
    inventory: [],
    visibleEntities: [
      { id: "enemy", name: "버퍼 카파 변종", kind: "enemy", distance: 1, direction: "EAST", disabled: false, hostile: true }
    ]
  };

  assert.equal(requestedPlayerMovementDestination(context).ref, "step.west");
  const proposal = fallbackPlayerActionProposal(context);
  assert.equal(proposal.kind, "MOVE");
  assert.equal(proposal.destinationRef, "step.west");
});

test("a Root System request resolves to the authoritative finale area instead of an arbitrary step", () => {
  const run = runFixture(91007);
  const context = playerActionContext(run, "move to the Root System final convergence point");
  const destination = requestedPlayerMovementDestination(context);
  const finaleArea = run.world.areas.find((area) => area.campaignRole === "FINAL_CONVERGENCE");

  assert.ok(destination);
  assert.equal(destination.ref, `area:${finaleArea.id}`);
  assert.equal(destination.travelMode, "SAFE_TRAVEL");
  const point = resolvePlayerActionDestination(run, destination.ref);
  assert.ok(point);
  assert.equal(point.x >= finaleArea.bounds.x && point.x < finaleArea.bounds.x + finaleArea.bounds.width, true);
  assert.equal(point.y >= finaleArea.bounds.y && point.y < finaleArea.bounds.y + finaleArea.bounds.height, true);
});

test("an underspecified move never falls back to the first unrelated destination", () => {
  const run = runFixture(91008);
  const proposal = fallbackPlayerActionProposal(playerActionContext(run, "어딘가로 이동할게"));

  assert.equal(proposal.kind, "DIALOGUE");
  assert.equal(proposal.requiresRoll, false);
  assert.equal(proposal.rejectedAction.kind, "MOVE");
  assert.match(proposal.rejectedAction.reason, /방향/u);
});

test("the complete accepted long message reaches player-action classification", () => {
  const run = runFixture(91005);
  const message = `${"가".repeat(900)} 마지막으로 동쪽으로 이동한다`;
  const context = playerActionContext(run, message);

  assert.equal(context.playerText, message);
  assert.ok(context.playerText.length > 400);
  const fallback = fallbackPlayerActionProposal(context);
  assert.equal(fallback.kind, "MOVE");
  assert.equal(fallback.destinationRef, "step.east");
});

test("deterministic fallback covers common interaction, negotiation, rest, and keyboard phrasing", () => {
  const context = {
    playerText: "",
    spatialContext: { facing: "SOUTH" },
    destinations: [{ ref: "step.south", name: "남쪽으로 한 걸음", distance: 1, direction: "SOUTH" }],
    inventory: [],
    visibleEntities: [
      { id: "npc", name: "기록관", kind: "npc", distance: 1, direction: "EAST", disabled: false, hostile: false },
      { id: "prop", name: "봉인 상자", kind: "prop", distance: 1, direction: "WEST", disabled: false, hostile: false }
    ]
  };
  const classify = (text) => fallbackPlayerActionProposal({ ...context, playerText: text });
  assert.equal(classify("기록관을 설득해서 협삽해 본다").kind, "NEGOTIATE");
  assert.equal(classify("봉인 상자를 열어 확인해 본다").kind, "INTERACT");
  assert.equal(classify("잠깐 숨 고르고 쉬자").kind, "REST");
  assert.equal(classify("최근 선택을 되감아 줘").kind, "UNDO");
  assert.equal(classify("주변의 적을 전부 공격한다").kind, "SELECT_ALL");
});

test("impossible traversal produces a concrete reason and a legal alternative", () => {
  const run = runFixture(91004);
  const proposal = fallbackPlayerActionProposal(playerActionContext(run, "벽을 뚫고 하늘을 날아 맵 밖으로 간다"));
  assert.equal(proposal.kind, "DIALOGUE");
  assert.equal(proposal.requiresRoll, false);
  assert.equal(proposal.rejectedAction.kind, "IMPOSSIBLE_WORLD_ACTION");
  assert.match(proposal.rejectedAction.reason, /이동하거나.*SEARCH/u);
});
