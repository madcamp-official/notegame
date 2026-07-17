import test from "node:test";
import assert from "node:assert/strict";
import { randomUUID } from "node:crypto";
import { AppError } from "../src/errors.js";
import { createCampaignBlueprint } from "../src/domain/campaign.js";
import { areaAt, generateWorld, isWalkable } from "../src/domain/world.js";
import { FixedD20Source, createRunState, normalizeTurnRequest, publicRun, resolveTurn } from "../src/domain/turn-engine.js";

const OWNER_ID = "33333333-3333-4333-8333-333333333333";

function runFixture(seed = 44) {
  const now = "2026-07-17T00:00:00.000Z";
  const campaign = {
    id: randomUUID(),
    ownerId: OWNER_ID,
    turnLimit: 30,
    ...createCampaignBlueprint({ worldSeed: seed, turnLimit: 30 }),
    world: generateWorld(seed)
  };
  return createRunState({ campaign, ownerId: OWNER_ID, now, resolutionSeed: "private-test-seed" });
}

function adjacentWalkable(run) {
  const player = run.entities.find((entity) => entity.id === run.playerEntityId);
  const occupied = new Set(run.entities.filter((entity) => entity.active && entity.blocking && entity.id !== player.id)
    .map((entity) => `${entity.position.x},${entity.position.y}`));
  for (const [dx, dy] of [[1, 0], [0, 1], [-1, 0], [0, -1]]) {
    const point = { x: player.position.x + dx, y: player.position.y + dy };
    const tile = run.world.tiles[point.y * run.world.width + point.x];
    if (point.x > 0 && point.y > 0 && point.x < run.world.width - 1 && point.y < run.world.height - 1
      && tile !== 1 && tile !== 4 && !occupied.has(`${point.x},${point.y}`)) return point;
  }
  assert.fail("Generated entry must expose an adjacent walkable tile.");
}

test("copy validates and mutates only the cloned committed state", () => {
  const original = runFixture();
  const initialEntityCount = original.entities.length;
  const player = original.entities.find((entity) => entity.id === original.playerEntityId);
  const book = original.entities.find((entity) => entity.assetId === "item.rune-book.v1");
  player.position = { x: book.position.x - 1, y: book.position.y };
  const destination = { x: book.position.x, y: book.position.y + 1 };
  // The guaranteed road may approach horizontally; explicitly use an adjacent
  // walkable destination from the immutable generated layout.
  const candidates = [
    { x: book.position.x + 1, y: book.position.y },
    { x: book.position.x - 1, y: book.position.y },
    destination
  ];
  const available = candidates.find((candidate) => {
    const tile = original.world.tiles[candidate.y * original.world.width + candidate.x];
    return tile !== 1 && tile !== 4 && !(candidate.x === book.position.x && candidate.y === book.position.y);
  });
  player.position = { x: available.x - 1, y: available.y };
  const request = normalizeTurnRequest({
    idempotencyKey: "copy-0001",
    expectedRunVersion: 1,
    ability: "copy",
    targetEntityId: book.id,
    destination: available,
    intent: "Copy the unwritten ledger"
  });

  const result = resolveTurn({ run: original, request, d20Source: new FixedD20Source(20) });
  assert.equal(original.entities.length, initialEntityCount);
  assert.equal(result.run.entities.length, initialEntityCount + 1);
  assert.equal(result.run.focus, original.focus - 1);
  assert.equal(result.run.world.layoutHash, original.world.layoutHash);
  assert.ok(result.turn.events.some((event) => event.type === "entity_spawned"));
});

test("protected entity rejection does not consume or mutate a turn", () => {
  const run = runFixture(45);
  const player = run.entities.find((entity) => entity.id === run.playerEntityId);
  const warden = run.entities.find((entity) => entity.kind === "npc");
  player.position = { x: warden.position.x - 1, y: warden.position.y };
  const request = normalizeTurnRequest({
    idempotencyKey: "delete-0001",
    expectedRunVersion: 1,
    ability: "delete",
    targetEntityId: warden.id,
    intent: "Delete the warden"
  });

  assert.throws(
    () => resolveTurn({ run, request, d20Source: new FixedD20Source(20) }),
    (error) => error instanceof AppError && error.code === "ENTITY_PROTECTED"
  );
  assert.equal(run.currentTurn, 0);
  assert.equal(run.version, 1);
  assert.equal(warden.active, true);
});

test("failed move consumes a valid turn but never moves the player", () => {
  const run = runFixture(46);
  const playerBefore = structuredClone(run.entities.find((entity) => entity.id === run.playerEntityId).position);
  const request = normalizeTurnRequest({
    idempotencyKey: "move-fail-0001",
    expectedRunVersion: 1,
    ability: "move",
    destination: adjacentWalkable(run),
    intent: "Move carefully"
  });
  const result = resolveTurn({ run, request, d20Source: new FixedD20Source(1) });
  assert.equal(result.turn.outcome, "failure");
  assert.deepEqual(result.run.entities.find((entity) => entity.id === run.playerEntityId).position, playerBefore);
  assert.equal(result.run.currentTurn, 1);
  assert.equal(result.run.version, 2);
  assert.ok(result.run.pressure > 0);
});

test("connect creates a temporary relation and Undo compensates without rewinding history or layout", () => {
  const run = runFixture(47);
  const player = run.entities.find((item) => item.id === run.playerEntityId);
  const targets = run.entities.filter((item) => item.kind === "prop").slice(0, 2);
  player.position = { x: 2, y: 2 };
  targets[0].position = { x: 3, y: 2 };
  targets[1].position = { x: 4, y: 2 };
  const layoutHash = run.world.layoutHash;
  const connected = resolveTurn({
    run,
    request: normalizeTurnRequest({
      idempotencyKey: "connect-0001",
      expectedRunVersion: 1,
      ability: "connect",
      targetEntityId: targets[0].id,
      secondaryTargetEntityId: targets[1].id,
      intent: "두 증거가 같은 약속을 가리키도록 연결한다"
    }),
    d20Source: new FixedD20Source(20)
  });
  assert.equal(connected.run.connections.filter((item) => item.active).length, 1);

  const undone = resolveTurn({
    run: connected.run,
    request: normalizeTurnRequest({ idempotencyKey: "undo-0000001", expectedRunVersion: 2, ability: "undo", intent: "직전 연결만 보상 사건으로 되돌린다" }),
    d20Source: new FixedD20Source(20)
  });
  assert.equal(undone.run.connections.filter((item) => item.active).length, 0);
  assert.equal(undone.run.currentTurn, 2);
  assert.equal(undone.run.world.layoutHash, layoutHash);
  assert.ok(undone.turn.events.some((event) => event.type === "reversible_reward_spent"));
});

test("Restore revives a recently deleted entity from its authoritative snapshot", () => {
  const run = runFixture(48);
  const player = run.entities.find((item) => item.id === run.playerEntityId);
  const target = run.entities.find((item) => item.kind === "prop" && !item.protected);
  player.position = { x: 2, y: 2 };
  target.position = { x: 3, y: 2 };
  const deleted = resolveTurn({
    run,
    request: normalizeTurnRequest({ idempotencyKey: "delete-restore-1", expectedRunVersion: 1, ability: "delete", targetEntityId: target.id, intent: "임시 물체를 지운다" }),
    d20Source: new FixedD20Source(20)
  });
  assert.equal(deleted.run.entities.find((item) => item.id === target.id).active, false);
  const restored = resolveTurn({
    run: deleted.run,
    request: normalizeTurnRequest({ idempotencyKey: "restore-0001", expectedRunVersion: 2, ability: "restore", targetEntityId: target.id, intent: "방금 제거한 물체를 복구한다" }),
    d20Source: new FixedD20Source(20)
  });
  assert.equal(restored.run.entities.find((item) => item.id === target.id).active, true);
  assert.equal(restored.run.world.layoutHash, run.world.layoutHash);
  assert.ok(restored.turn.events.some((event) => event.type === "entity_restored"));
});

test("Restore rejects a non-blocking ambient entity when its slot has been rebound", () => {
  const run = runFixture(1);
  const player = run.entities.find((item) => item.id === run.playerEntityId);
  const target = run.entities.find((item) => item.kind === "prop" && !item.protected && !item.blocking
    && run.world.placementSlots.some((slot) => slot.id === item.state?.slotId && slot.purpose === "ambient"));
  assert.ok(target, "the fixture must contain a removable non-blocking ambient prop");
  const slot = run.world.placementSlots.find((item) => item.id === target.state.slotId);
  player.position = { x: target.position.x - 1, y: target.position.y };

  const deletedAndRebound = resolveTurn({
    run,
    request: normalizeTurnRequest({
      idempotencyKey: "delete-rebind-1", expectedRunVersion: 1, ability: "delete",
      targetEntityId: target.id, intent: "기존 주변 물체를 지우고 같은 슬롯에 새 물체를 배치한다"
    }),
    d20Source: new FixedD20Source(10),
    directorOutput: {
      summary: "빈 슬롯에 새 주변 물체가 놓였다.",
      body: "미리 생성된 슬롯과 허용된 에셋 범위 안에서 새 물체가 활성화됐다.",
      dialogue: [],
      proposedOps: [{
        op: "BIND_SLOT_ENTITY", summary: "같은 ambient 슬롯을 새 물체로 채운다.",
        slotId: slot.id, assetId: slot.allowedAssetIds[0], budgetCost: 1
      }],
      fallbackUsed: false,
      model: "memory-regression"
    }
  });
  const replacement = deletedAndRebound.run.entities.find((item) => item.active && item.id !== target.id && item.state?.slotId === slot.id);
  assert.ok(replacement, "the deleted ambient entity's slot must be rebound");
  assert.deepEqual(replacement.position, target.position);

  assert.throws(
    () => resolveTurn({
      run: deletedAndRebound.run,
      request: normalizeTurnRequest({
        idempotencyKey: "restore-rebound-2", expectedRunVersion: 2, ability: "restore",
        targetEntityId: target.id, intent: "이미 다시 점유된 슬롯으로 기존 물체를 복원한다"
      }),
      d20Source: new FixedD20Source(20)
    }),
    (error) => error instanceof AppError && error.code === "RESTORE_DESTINATION_OCCUPIED"
  );
});

test("Restore repairs recent active-target damage but never rewinds the roll, turn, or map", () => {
  const run = runFixture(49);
  const player = run.entities.find((item) => item.id === run.playerEntityId);
  const npc = run.entities.find((item) => item.kind === "npc");
  const prop = run.entities.find((item) => item.kind === "prop");
  player.position = { x: 2, y: 2 };
  npc.position = { x: 3, y: 2 };
  prop.position = { x: 4, y: 2 };
  const damaged = resolveTurn({
    run,
    request: normalizeTurnRequest({
      idempotencyKey: "damage-connect-1", expectedRunVersion: 1, ability: "connect",
      targetEntityId: npc.id, secondaryTargetEntityId: prop.id, intent: "위험한 연결을 시도한다"
    }),
    d20Source: new FixedD20Source(1)
  });
  assert.equal(damaged.turn.outcome, "critical_failure");
  assert.equal(damaged.run.entities.find((item) => item.id === player.id).state.hp, 11);
  const repaired = resolveTurn({
    run: damaged.run,
    request: normalizeTurnRequest({ idempotencyKey: "restore-damage-1", expectedRunVersion: 2, ability: "restore", targetEntityId: player.id, intent: "최근의 허용된 손상만 복구한다" }),
    d20Source: new FixedD20Source(20)
  });
  assert.equal(repaired.run.entities.find((item) => item.id === player.id).state.hp, 12);
  assert.equal(repaired.run.currentTurn, 2);
  assert.equal(damaged.turn.d20, 1);
  assert.equal(repaired.run.world.layoutHash, run.world.layoutHash);
});

test("all six keyboard abilities expose explicit illegal paths", () => {
  const run = runFixture(50);
  const player = run.entities.find((item) => item.id === run.playerEntityId);
  const npc = run.entities.find((item) => item.kind === "npc");
  const prop = run.entities.find((item) => item.kind === "prop");
  player.position = { x: 2, y: 2 };
  npc.position = { x: 3, y: 2 };
  prop.position = { x: 4, y: 2 };
  const invalid = [
    { code: "PATH_BLOCKED", body: { ability: "move", destination: { x: 0, y: 0 } } },
    { code: "ENTITY_NOT_CLONEABLE", body: { ability: "copy", targetEntityId: npc.id, destination: { x: 5, y: 2 } } },
    { code: "ENTITY_PROTECTED", body: { ability: "delete", targetEntityId: npc.id } },
    { code: "TARGETS_IDENTICAL", body: { ability: "connect", targetEntityId: prop.id, secondaryTargetEntityId: prop.id } },
    { code: "RESTORE_NOT_AVAILABLE", body: { ability: "restore", targetEntityId: player.id } },
    { code: "UNDO_NOT_AVAILABLE", body: { ability: "undo" } }
  ];
  for (const [index, item] of invalid.entries()) {
    const request = normalizeTurnRequest({ idempotencyKey: `illegal-${index + 1000}`, expectedRunVersion: 1, intent: "불가능한 요청의 이유를 확인한다", ...item.body });
    assert.throws(() => resolveTurn({ run, request, d20Source: new FixedD20Source(20) }), (error) => error instanceof AppError && error.code === item.code);
  }
  assert.equal(run.currentTurn, 0);
  assert.equal(run.version, 1);
});

test("local encounter Move respects the same finale milestone gate as safe travel", () => {
  const run = runFixture(73);
  const player = run.entities.find((item) => item.id === run.playerEntityId);
  const occupied = new Set(run.entities.filter((item) => item.active && item.blocking && item.id !== player.id)
    .map((item) => `${item.position.x},${item.position.y}`));
  let boundary = null;
  for (let y = 1; y < run.world.height - 1 && !boundary; y += 1) {
    for (let x = 1; x < run.world.width - 1 && !boundary; x += 1) {
      const outside = { x, y };
      if (areaAt(run.world, outside).campaignRole === "FINAL_CONVERGENCE" || !isWalkable(run.world, outside)) continue;
      for (const [dx, dy] of [[1, 0], [0, 1], [-1, 0], [0, -1]]) {
        const inside = { x: x + dx, y: y + dy };
        if (areaAt(run.world, inside).campaignRole === "FINAL_CONVERGENCE"
          && isWalkable(run.world, inside) && !occupied.has(`${inside.x},${inside.y}`)) {
          boundary = { outside, inside };
          break;
        }
      }
    }
  }
  assert.ok(boundary, "generated finale region must have a walkable boundary for gate verification");
  player.position = boundary.outside;
  const request = normalizeTurnRequest({
    idempotencyKey: "finale-gate-move-1",
    expectedRunVersion: run.version,
    ability: "move",
    destination: boundary.inside,
    intent: "세 개의 이야기 전환점 없이 최종 권역의 경계를 넘으려 한다"
  });
  assert.throws(
    () => resolveTurn({ run, request, d20Source: new FixedD20Source(20) }),
    (error) => error instanceof AppError && error.code === "PATH_BLOCKED"
  );

  run.progressLevel = 3;
  run.progressTokens = run.progressTokenDefinitions.map((token) => token.id);
  const allowed = resolveTurn({ run, request, d20Source: new FixedD20Source(20) });
  assert.deepEqual(allowed.run.entities.find((item) => item.id === run.playerEntityId).position, boundary.inside);
});

test("public finale eligibility uses the authoritative exact three-milestone gate", () => {
  const run = runFixture(74);
  const canonicalDefinitions = run.progressTokenDefinitions.map((token) => ({ ...token }));
  run.progressLevel = 3;
  run.progressTokens = canonicalDefinitions.map((token) => token.id);
  assert.equal(publicRun(run).finaleGate.eligible, true);

  run.progressTokenDefinitions = canonicalDefinitions.slice(0, 2);
  assert.equal(publicRun(run).finaleGate.eligible, false);

  run.progressTokenDefinitions = canonicalDefinitions;
  run.progressTokens = canonicalDefinitions.slice(0, 2).map((token) => token.id);
  assert.equal(publicRun(run).finaleGate.eligible, false);

  run.progressTokenDefinitions = [];
  run.progressTokens = [];
  assert.equal(publicRun(run).finaleGate.eligible, false);
});
