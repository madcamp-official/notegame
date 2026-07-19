import test from "node:test";
import assert from "node:assert/strict";
import { randomUUID } from "node:crypto";
import { AppError } from "../src/errors.js";
import { createCampaignBlueprint } from "../src/domain/campaign.js";
import { areaAt, generateWorld, isWalkable } from "../src/domain/world.js";
import { FixedD20Source, createRunState, normalizeTravelRequest, normalizeTurnRequest, publicRun, resolveSafeTravel, resolveTurn } from "../src/domain/turn-engine.js";

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

function normalizeSkillRequest({ ability, targetEntityId = null, secondaryTargetEntityId = null, intent, ...rest }) {
  return normalizeTurnRequest({
    ...rest,
    inputType: "USE_SKILL",
    skillId: String(ability || "").toUpperCase(),
    targetIds: [targetEntityId, secondaryTargetEntityId].filter(Boolean),
    ...(intent ? { playerNote: intent } : {})
  });
}

test("Ctrl F search and Ctrl A area deployment reveal bounded nearby entities without targets", () => {
  const run = runFixture(117);
  const player = run.entities.find((entity) => entity.id === run.playerEntityId);
  const nearby = run.entities.find((entity) => entity.id !== player.id && entity.active);
  nearby.position = { x: player.position.x + 1, y: player.position.y };

  const searched = resolveTurn({
    run,
    request: normalizeSkillRequest({ ability: "search", idempotencyKey: "ctrl-f-search-0001", expectedRunVersion: 1 }),
    d20Source: new FixedD20Source(20)
  });
  assert.equal(searched.run.entities.find((entity) => entity.id === nearby.id).state.revealed, true);
  assert.ok(searched.turn.events.some((event) => event.type === "search_completed"));

  const selected = resolveTurn({
    run: searched.run,
    request: normalizeSkillRequest({ ability: "select_all", idempotencyKey: "ctrl-a-area-0001", expectedRunVersion: 2 }),
    d20Source: new FixedD20Source(20)
  });
  assert.equal(selected.run.entities.find((entity) => entity.id === nearby.id).state.selectedByArea, true);
  assert.ok(selected.turn.events.some((event) => event.type === "administrator_area_deployed"));
});

test("books and crates block travel and support bounded nearby interaction", () => {
  const run = runFixture(91);
  const player = run.entities.find((entity) => entity.id === run.playerEntityId);
  const book = run.entities.find((entity) => entity.assetId === "item.rune-book.v1");
  const crate = run.entities.find((entity) => entity.assetId === "item.crate.v1");
  assert.equal(book.blocking, true);
  assert.equal(crate.blocking, true);

  assert.throws(() => resolveSafeTravel({
    run,
    request: normalizeTravelRequest({
      inputType: "MOVE", idempotencyKey: "travel-prop-0001", expectedRunVersion: 1,
      destination: book.position
    })
  }), (error) => error instanceof AppError && error.code === "TRAVEL_PATH_BLOCKED");

  player.position = { x: crate.position.x - 1, y: crate.position.y };
  run.focus = Math.max(0, run.maxFocus - 1);
  const interacted = resolveTurn({
    run,
    request: normalizeSkillRequest({
      idempotencyKey: "interact-crate-0001", expectedRunVersion: 1, ability: "interact",
      targetEntityId: crate.id, intent: "보급 상자를 확인한다"
    }),
    d20Source: new FixedD20Source(20)
  });
  const committedCrate = interacted.run.entities.find((entity) => entity.id === crate.id);
  assert.equal(committedCrate.state.opened, true);
  assert.equal(committedCrate.state.interactionCount, 1);
  assert.equal(interacted.run.focus, run.focus + 1);
  assert.ok(interacted.turn.events.some((event) => event.type === "entity_interacted"));
});

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
  const request = normalizeSkillRequest({
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
  const request = normalizeSkillRequest({
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

test("MOVE is rejected from the consuming action endpoint and blocked travel never consumes a turn", () => {
  const run = runFixture(46);
  const playerBefore = structuredClone(run.entities.find((entity) => entity.id === run.playerEntityId).position);
  assert.throws(() => normalizeSkillRequest({ idempotencyKey: "move-fail-0001", expectedRunVersion: 1, ability: "move", destination: adjacentWalkable(run) }), (error) => error.code === "SKILL_INVALID");
  const travel = normalizeTravelRequest({ inputType: "MOVE", idempotencyKey: "move-fail-0002", expectedRunVersion: 1, destination: { x: 0, y: 0 } });
  assert.throws(() => resolveSafeTravel({ run, request: travel }), (error) => error.code === "TRAVEL_PATH_BLOCKED");
  assert.deepEqual(run.entities.find((entity) => entity.id === run.playerEntityId).position, playerBefore);
  assert.equal(run.currentTurn, 0);
  assert.equal(run.version, 1);
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
    request: normalizeSkillRequest({
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
    request: normalizeSkillRequest({ idempotencyKey: "undo-0000001", expectedRunVersion: 2, ability: "undo", intent: "직전 연결만 보상 사건으로 되돌린다" }),
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
    request: normalizeSkillRequest({ idempotencyKey: "delete-restore-1", expectedRunVersion: 1, ability: "delete", targetEntityId: target.id, intent: "임시 물체를 지운다" }),
    d20Source: new FixedD20Source(20)
  });
  assert.equal(deleted.run.entities.find((item) => item.id === target.id).active, false);
  const restored = resolveTurn({
    run: deleted.run,
    request: normalizeSkillRequest({ idempotencyKey: "restore-0001", expectedRunVersion: 2, ability: "restore", targetEntityId: target.id, intent: "방금 제거한 물체를 복구한다" }),
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
    request: normalizeSkillRequest({
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
      request: normalizeSkillRequest({
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
    request: normalizeSkillRequest({
      idempotencyKey: "damage-connect-1", expectedRunVersion: 1, ability: "connect",
      targetEntityId: npc.id, secondaryTargetEntityId: prop.id, intent: "위험한 연결을 시도한다"
    }),
    d20Source: new FixedD20Source(1)
  });
  assert.equal(damaged.turn.outcome, "critical_failure");
  assert.equal(damaged.run.entities.find((item) => item.id === player.id).state.hp, 11);
  const repaired = resolveTurn({
    run: damaged.run,
    request: normalizeSkillRequest({ idempotencyKey: "restore-damage-1", expectedRunVersion: 2, ability: "restore", targetEntityId: player.id, intent: "최근의 허용된 손상만 복구한다" }),
    d20Source: new FixedD20Source(20)
  });
  assert.equal(repaired.run.entities.find((item) => item.id === player.id).state.hp, 12);
  assert.equal(repaired.run.currentTurn, 2);
  assert.equal(damaged.turn.d20, 1);
  assert.equal(repaired.run.world.layoutHash, run.world.layoutHash);
});

test("all five administrator keyboard skills expose explicit illegal paths", () => {
  const run = runFixture(50);
  const player = run.entities.find((item) => item.id === run.playerEntityId);
  const npc = run.entities.find((item) => item.kind === "npc");
  const prop = run.entities.find((item) => item.kind === "prop");
  player.position = { x: 2, y: 2 };
  npc.position = { x: 3, y: 2 };
  prop.position = { x: 4, y: 2 };
  const invalid = [
    { code: "ENTITY_NOT_CLONEABLE", body: { ability: "copy", targetEntityId: npc.id, destination: { x: 5, y: 2 } } },
    { code: "ENTITY_PROTECTED", body: { ability: "delete", targetEntityId: npc.id } },
    { code: "TARGETS_IDENTICAL", body: { ability: "connect", targetEntityId: prop.id, secondaryTargetEntityId: prop.id } },
    { code: "RESTORE_NOT_AVAILABLE", body: { ability: "restore", targetEntityId: player.id } },
    { code: "UNDO_NOT_AVAILABLE", body: { ability: "undo" } }
  ];
  for (const [index, item] of invalid.entries()) {
    const request = normalizeSkillRequest({ idempotencyKey: `illegal-${index + 1000}`, expectedRunVersion: 1, intent: "불가능한 요청의 이유를 확인한다", ...item.body });
    assert.throws(() => resolveTurn({ run, request, d20Source: new FixedD20Source(20) }), (error) => error instanceof AppError && error.code === item.code);
  }
  assert.equal(run.currentTurn, 0);
  assert.equal(run.version, 1);
});

test("MOVE respects the administrator access and clue gate at Root System", () => {
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
  const request = normalizeTravelRequest({
    inputType: "MOVE",
    idempotencyKey: "finale-gate-move-1",
    expectedRunVersion: run.version,
    destination: boundary.inside
  });
  assert.throws(
    () => resolveSafeTravel({ run, request }),
    (error) => error instanceof AppError && error.code === "ROOT_SYSTEM_ACCESS_DENIED"
  );

  run.adminAccessAcquisitionHistory = run.adminAccessLevels.map((access, index) => ({ accessLevelId: access.id, turnNo: index + 1 }));
  run.canonicalFacts.find((fact) => fact.subject === "collapse_origin").value = true;
  const allowed = resolveSafeTravel({ run, request });
  assert.equal(allowed.navigation.campaignTurnConsumed, false);
  assert.equal(allowed.run.currentTurn, 0);
});

test("public Root System eligibility uses exactly three access levels and the required clue", () => {
  const run = runFixture(74);
  const clue = run.canonicalFacts.find((fact) => fact.subject === "collapse_origin");
  clue.value = true;
  run.adminAccessAcquisitionHistory = run.adminAccessLevels.map((access, index) => ({ accessLevelId: access.id, turnNo: index + 1 }));
  assert.equal(publicRun(run).finaleGate.eligible, true);

  run.adminAccessAcquisitionHistory = run.adminAccessAcquisitionHistory.slice(0, 2);
  assert.equal(publicRun(run).finaleGate.eligible, false);

  run.adminAccessAcquisitionHistory = run.adminAccessLevels.map((access, index) => ({ accessLevelId: access.id, turnNo: index + 1 }));
  clue.value = false;
  assert.equal(publicRun(run).finaleGate.eligible, false);
});
