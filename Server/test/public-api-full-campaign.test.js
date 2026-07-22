import test from "node:test";
import assert from "node:assert/strict";
import { GameService } from "../src/services/game-service.js";
import { MemoryStore } from "../src/store/memory-store.js";

const OWNER_ID = "11111111-1111-4111-8111-111111111111";
const silentLogger = { debug() {}, info() {}, warn() {}, error() {} };
const distance = (left, right) => Math.abs(left.x - right.x) + Math.abs(left.y - right.y);

class PublicCampaignNarrator {
  async planPlayerAction() {
    return {
      kind: "DIALOGUE",
      targetEntityIds: [],
      itemIds: [],
      destinationRef: null,
      resultItem: null,
      reason: "현재 장면에 말로 반응한다."
    };
  }

  async narrate(context) {
    const enemy = context.visibleEntities
      .filter((entity) => entity.kind === "enemy")
      .sort((left, right) => left.distance - right.distance || left.id.localeCompare(right.id))[0] || null;
    const encounterActive = context.spatialContext?.activeEncounter !== null;
    const choices = encounterActive && enemy && enemy.distance > 3
      ? [
          {
            choiceId: "combat.connect",
            text: "주변 신호를 연결해 적대적 조우를 안전하게 풀어낸다.",
            choiceKind: "SKILL",
            intentTag: "EMPATHETIC",
            resolutionMode: "D20",
            skillId: "CONNECT",
            targetEntityId: null,
            destinationRef: null
          },
          {
            choiceId: "combat.observe",
            text: "적의 움직임을 지켜보며 다음 기회를 기다린다.",
            choiceKind: "ATTITUDE",
            intentTag: "CAUTIOUS",
            resolutionMode: "NONE",
            skillId: null,
            targetEntityId: enemy.id,
            destinationRef: null
          }
        ]
      : encounterActive && enemy && context.skillId !== "SEARCH"
        ? [
            {
              choiceId: "combat.search",
              text: "삭제하기 전에 눈앞의 적이 숨긴 의존성을 먼저 조사한다.",
              choiceKind: "SKILL",
              intentTag: "INVESTIGATE",
              resolutionMode: "D20",
              skillId: "SEARCH",
              targetEntityId: enemy.id,
              destinationRef: null
            },
            {
              choiceId: "combat.observe",
              text: "적의 움직임을 지켜보며 다음 기회를 기다린다.",
              choiceKind: "ATTITUDE",
              intentTag: "CAUTIOUS",
              resolutionMode: "NONE",
              skillId: null,
              targetEntityId: enemy.id,
              destinationRef: null
            }
          ]
        : enemy && enemy.distance <= 3
        ? [
            {
              choiceId: "combat.delete",
              text: "관리자 키보드의 삭제 명령으로 눈앞의 적을 제압한다.",
              choiceKind: "SKILL",
              intentTag: "ASSERTIVE",
              resolutionMode: "D20",
              skillId: "DELETE",
              targetEntityId: enemy.id,
              destinationRef: null
            },
            {
              choiceId: "combat.observe",
              text: "적의 움직임을 지켜보며 다음 기회를 기다린다.",
              choiceKind: "ATTITUDE",
              intentTag: "CAUTIOUS",
              resolutionMode: "NONE",
              skillId: null,
              targetEntityId: enemy.id,
              destinationRef: null
            }
          ]
        : [
            {
              choiceId: "story.continue",
              text: "확인한 결과를 바탕으로 침착하게 다음 행동을 준비한다.",
              choiceKind: "ATTITUDE",
              intentTag: "CAUTIOUS",
              resolutionMode: "NONE",
              skillId: null,
              targetEntityId: null,
              destinationRef: null
            },
            {
              choiceId: "story.reflect",
              text: "동료와 지금까지의 단서를 차분히 되짚어 본다.",
              choiceKind: "DIALOGUE",
              intentTag: "CURIOUS",
              resolutionMode: "NONE",
              skillId: null,
              targetEntityId: null,
              destinationRef: null
            }
          ];

    return {
      summary: "확정된 장면의 여파",
      body: "관리자 키보드가 확정된 결과를 남겼다. 넙죽이는 주변의 반응을 살피며 다음 결정을 준비한다.",
      dialogue: [],
      storySequence: [{
        type: "NARRATION",
        speakerId: null,
        actionId: null,
        text: "확정된 행동 뒤로 코드리아의 풍경이 조용히 반응했다."
      }],
      nextIntervention: { reason: "장면의 결과를 확인하고 다음 반응을 선택한다.", choices },
      proposedOps: [],
      elementalEffectId: null,
      fallbackUsed: false,
      model: "public-api-campaign-test"
    };
  }
}

function player(run) {
  const entity = run.entities.find((candidate) => candidate.id === run.playerEntityId);
  assert(entity, "The public run must expose its active player entity.");
  return entity;
}

function areaForPoint(run, point) {
  const area = run.world.areas.find((candidate) => point.x >= candidate.bounds.x
    && point.y >= candidate.bounds.y
    && point.x < candidate.bounds.x + candidate.bounds.width
    && point.y < candidate.bounds.y + candidate.bounds.height);
  assert(area, `No public area contains (${point.x},${point.y}).`);
  return area;
}

function decodeTiles(world) {
  const tiles = [];
  for (const [code, count] of world.tilesRle) {
    for (let index = 0; index < count; index += 1) tiles.push(code);
  }
  assert.equal(tiles.length, world.width * world.height);
  return tiles;
}

function safeDestination(run, point, tiles = decodeTiles(run.world)) {
  if (point.x < 0 || point.y < 0 || point.x >= run.world.width || point.y >= run.world.height) return false;
  const tileName = run.world.tileLegend[tiles[point.y * run.world.width + point.x]];
  if (!["grass", "road"].includes(tileName)) return false;
  if (!run.rootSystemGate.eligible && areaForPoint(run, point).campaignRole === "FINAL_CONVERGENCE") return false;
  return !run.entities.some((entity) => entity.id !== run.playerEntityId
    && entity.position.x === point.x
    && entity.position.y === point.y
    && (entity.blocking || entity.kind === "prop"));
}

function destinationWithin(run, targets, maximumDistances, { excludeCurrent = false } = {}) {
  const current = player(run).position;
  const tiles = decodeTiles(run.world);
  const candidates = [];
  for (let y = 0; y < run.world.height; y += 1) {
    for (let x = 0; x < run.world.width; x += 1) {
      const point = { x, y };
      if (excludeCurrent && distance(point, current) === 0) continue;
      if (!safeDestination(run, point, tiles)) continue;
      if (!targets.every((target, index) => distance(point, target.position) <= maximumDistances[index])) continue;
      candidates.push(point);
    }
  }
  candidates.sort((left, right) => distance(left, current) - distance(right, current)
    || targets.reduce((sum, target) => sum + distance(left, target.position), 0)
      - targets.reduce((sum, target) => sum + distance(right, target.position), 0)
    || left.y - right.y || left.x - right.x);
  assert(candidates.length > 0, `No public safe tile reaches ${targets.map((target) => target.name).join(" and ")}.`);
  return candidates[0];
}

test("a fresh campaign reaches a non-emergency ending through only GameService public play APIs", async () => {
  let clockTick = 0;
  const service = new GameService({
    store: new MemoryStore(),
    narrator: new PublicCampaignNarrator(),
    d20Source: { roll: () => 20 },
    clock: () => new Date(Date.UTC(2026, 0, 1) + clockTick++ * 1_000).toISOString(),
    logger: silentLogger
  });
  const campaign = await service.createCampaign(OWNER_ID, { worldSeed: 27, turnLimit: 40 });
  let run = await service.createRun(OWNER_ID, campaign.id, {});
  let operationSequence = 0;
  let travelReplayVerified = false;

  const commitTravel = async (destination, label) => {
    const turnBefore = run.currentTurn;
    const payload = {
      inputType: "MOVE",
      idempotencyKey: `campaign-travel-${String(operationSequence++).padStart(3, "0")}-${label}`,
      expectedRunVersion: run.version,
      destination
    };
    const result = await service.travel(OWNER_ID, run.id, payload);
    assert.equal(result.run.currentTurn, turnBefore, "Safe travel must not consume a campaign turn.");
    if (!travelReplayVerified) {
      const replay = await service.travel(OWNER_ID, run.id, payload);
      assert.equal(replay.fromIdempotencyCache, true);
      assert.equal(replay.run.version, result.run.version);
      assert.equal(replay.run.currentTurn, result.run.currentTurn);
      assert.deepEqual(replay.navigation.path, result.navigation.path);
      travelReplayVerified = true;
    }
    run = result.run;
    return result.navigation;
  };

  const travelExactly = async (destination, label) => {
    for (let attempt = 0; attempt < 32; attempt += 1) {
      if (distance(player(run).position, destination) === 0) return;
      const navigation = await commitTravel(destination, `${label}-${attempt}`);
      assert.equal(run.activeEncounter, null,
        `Unexpected encounter while following the public safe route to ${label}: ${navigation.encounter?.reason || "unknown"}.`);
    }
    assert.fail(`The public travel API did not reach ${label} within 32 committed navigation segments.`);
  };

  const discoverArea = async (areaId, label) => {
    if (run.discoveredAreaIds.includes(areaId)) return;
    const hub = run.world.points.find((point) => point.areaId === areaId && point.id.startsWith("hub."))
      || run.world.points.find((point) => point.areaId === areaId);
    assert(hub, `Area ${areaId} has no public routed point.`);
    await travelExactly({ x: hub.x, y: hub.y }, `${label}-hub`);
    assert(run.discoveredAreaIds.includes(areaId), `Travel did not disclose ${label}.`);
  };

  const moveWithin = async (targets, ranges, label, { dismissPending = true } = {}) => {
    const alreadyWithin = targets.every((target, index) => distance(player(run).position, target.position) <= ranges[index]);
    if (alreadyWithin && (!dismissPending || run.pendingChoiceSet === null)) return;
    const destination = destinationWithin(run, targets, ranges, { excludeCurrent: alreadyWithin });
    await travelExactly(destination, label);
    assert(targets.every((target, index) => distance(player(run).position, target.position) <= ranges[index]));
    if (dismissPending) assert.equal(run.pendingChoiceSet, null, "Travel must dismiss the prior optional choice set.");
  };

  const useSkill = async (skillId, targetIds, label) => {
    assert.equal(run.pendingChoiceSet, null, `${label} must start after the prior choice set is dismissed.`);
    const payload = {
      inputType: "USE_SKILL",
      idempotencyKey: `campaign-turn-${String(operationSequence++).padStart(3, "0")}-${label}`,
      expectedRunVersion: run.version,
      skillId,
      targetIds
    };
    const result = await service.submitTurn(OWNER_ID, run.id, payload);
    assert(["success", "critical_success"].includes(result.turn.outcome),
      `${label} must resolve as a successful public action.`);
    assert.equal(result.turn.d20, 20);
    run = result.run;
    return { ...result, payload };
  };

  const rest = async (label) => {
    if (run.pendingChoiceSet) {
      const currentTarget = { name: "current area", position: player(run).position };
      await moveWithin([currentTarget], [2], `${label}-dismiss`, { dismissPending: true });
    }
    const focusBefore = run.focus;
    const result = await useSkill("REST", [], label);
    assert(run.focus > focusBefore, "A public REST turn must restore focus.");
    return result;
  };

  assert.equal(run.status, "active");
  assert.equal(run.currentTurn, 0);
  assert(run.pendingChoiceSet);
  const openingFocus = run.focus;
  const openingMetrics = structuredClone(run.metrics);
  const openingEnemyId = run.activeEncounter?.sourceEntityId;
  const opening = run.pendingChoiceSet.choices.find((choice) => choice.choiceId === "opening.attack");
  assert(opening);
  const openingResult = await service.submitChoice(OWNER_ID, run.id, {
    choiceSetId: run.pendingChoiceSet.choiceSetId,
    choiceId: opening.choiceId,
    idempotencyKey: "campaign-opening-choice",
    expectedRunVersion: run.version
  });
  run = openingResult.run;
  assert(openingResult.turn.events.some((event) => event.type === "narrative_choice_selected"));
  assert(openingResult.turn.events.some((event) => event.type === "health_changed" &&
    event.entityId === openingEnemyId && event.delta === -5));
  assert.equal(run.activeEncounter, null);
  assert.equal(run.pendingChoiceSet, null,
    "The tutorial result must release the player to move instead of forcing another choice.");
  assert.equal(openingResult.turn.narrative.continuesWithMovement, true);
  assert.match(openingResult.turn.narrative.storySequence.at(-1).text, /WASD/u);
  assert.equal(run.focus, openingFocus, "The mandatory tutorial attack must not spend campaign focus.");
  assert.deepEqual(run.metrics, openingMetrics,
    "The mandatory tutorial attack must not bias any later ending metric.");
  assert.equal(run.progressLevel, 0,
    "The keyboard tutorial must not grant administrator access or advance the campaign arc.");
  assert.equal(run.adminAccessAcquisitionHistory.length, 0);

  const encounterMessage = {
    text: "주변을 수색해 숨어 있는 몬스터와 조우한다.",
    idempotencyKey: "campaign-freeform-monster",
    expectedRunVersion: run.version
  };
  const messageResult = await service.submitPlayerMessage(OWNER_ID, run.id, encounterMessage);
  run = messageResult.run;
  const activation = messageResult.turn.events.find((event) => event.type === "entity_activated");
  assert(activation, "The public free-form request must activate a concrete combatant.");
  const activatedEnemyId = activation.entityId;
  assert(run.entities.some((entity) => entity.id === activatedEnemyId && entity.kind === "enemy"));
  assert.equal(run.activeEncounter?.kind, "COMBAT");
  const messageReplay = await service.submitPlayerMessage(OWNER_ID, run.id, encounterMessage);
  assert.equal(messageReplay.fromIdempotencyCache, true);
  assert.equal(messageReplay.run.version, run.version);
  assert.equal(messageReplay.run.currentTurn, run.currentTurn);
  assert.equal(messageReplay.run.entities.filter((entity) => entity.id === activatedEnemyId).length, 1);

  const encounterChoice = run.pendingChoiceSet.choices.find((choice) => choice.choiceId === "combat.connect")
    || run.pendingChoiceSet.choices.find((choice) => choice.choiceId === "combat.search")
    || run.pendingChoiceSet.choices.find((choice) => choice.choiceId === "combat.delete");
  assert(encounterChoice, "An activated enemy must offer a public combat or encounter-resolution choice.");
  const encounterChoiceRequest = {
    choiceSetId: run.pendingChoiceSet.choiceSetId,
    choiceId: encounterChoice.choiceId,
    idempotencyKey: "campaign-first-combat-choice",
    expectedRunVersion: run.version
  };
  const encounterResult = await service.submitChoice(OWNER_ID, run.id, encounterChoiceRequest);
  run = encounterResult.run;
  if (encounterChoice.choiceId === "combat.search") {
    assert(encounterResult.turn.events.some((event) => event.type === "entity_investigated"
      && event.entityId === activatedEnemyId));
    assert.equal(run.activeEncounter?.status, "active");
  } else {
    assert(encounterResult.turn.events.some((event) => event.type === "encounter_resolved"));
    assert.equal(run.activeEncounter, null);
  }
  const encounterReplay = await service.submitChoice(OWNER_ID, run.id, encounterChoiceRequest);
  assert.equal(encounterReplay.fromIdempotencyCache, true);
  assert.equal(encounterReplay.run.version, run.version);
  assert.equal(encounterReplay.run.currentTurn, run.currentTurn);

  const combatTurns = [encounterResult.turn];
  for (let attack = 0; run.entities.some((entity) => entity.id === activatedEnemyId) && attack < 3; attack += 1) {
    const activatedEnemy = run.entities.find((entity) => entity.id === activatedEnemyId);
    const sealedDelete = run.pendingChoiceSet?.choices.find((choice) => choice.choiceId === "combat.delete"
      && choice.targetEntityId === activatedEnemyId);
    if (sealedDelete) {
      const result = await service.submitChoice(OWNER_ID, run.id, {
        choiceSetId: run.pendingChoiceSet.choiceSetId,
        choiceId: sealedDelete.choiceId,
        idempotencyKey: `campaign-combat-delete-${attack}`,
        expectedRunVersion: run.version
      });
      run = result.run;
      combatTurns.push(result.turn);
    } else {
      await moveWithin([activatedEnemy], [3], `activated-enemy-${attack}`);
      if (activatedEnemy.state?.revealed !== true) {
        const searchResult = await useSkill("SEARCH", [activatedEnemyId], `reveal-activated-enemy-${attack}`);
        combatTurns.push(searchResult.turn);
        continue;
      }
      const result = await useSkill("DELETE", [activatedEnemyId], `defeat-activated-enemy-${attack}`);
      combatTurns.push(result.turn);
    }
  }
  const combatEvents = combatTurns.flatMap((turn) => turn.events);
  assert(combatEvents.some((event) => event.type === "health_changed"));
  assert(combatEvents.some((event) => event.type === "enemy_defeated"));
  assert(combatEvents.some((event) => event.type === "defeat_reward_granted"));
  assert(!run.entities.some((entity) => entity.id === activatedEnemyId), "The defeated enemy must leave the public active-entity view.");

  const openingCompanion = run.entities.find((entity) => entity.kind === "npc" && entity.name === "코멘트");
  assert(openingCompanion?.capabilities.canConnect);
  const openingConnectionExists = run.connections.some((connection) => connection.active
    && ((connection.fromId === run.playerEntityId && connection.toId === openingCompanion.id)
      || (connection.toId === run.playerEntityId && connection.fromId === openingCompanion.id)));
  if (!openingConnectionExists) {
    await moveWithin([openingCompanion], [5], "opening-companion");
    await useSkill("CONNECT", [run.playerEntityId, openingCompanion.id], "connect-opening-companion");
  }

  const accessTargets = [1, 2, 3].map((level) => {
    const candidate = run.adminAccessCandidates.find((item) => item.accessLevelId === `ADMIN_ACCESS_LEVEL_${level}`
      && item.skillId === "SEARCH");
    assert(candidate, `Seed 27 must expose a public SEARCH path for administrator access ${level}.`);
    return { level, candidate };
  });

  const currentAccessEntity = (accessTarget) => {
    const entity = run.entities.find((item) => item.state?.candidateId === accessTarget.candidate.id);
    assert(entity, `Administrator access ${accessTarget.level} is not visible when it becomes the next level.`);
    return entity;
  };

  await discoverArea(accessTargets[0].candidate.areaId, "access-level-1");
  const firstAccessEntity = currentAccessEntity(accessTargets[0]);
  await moveWithin([firstAccessEntity], [6], "access-level-1-target");
  const accessOne = await useSkill("SEARCH", [firstAccessEntity.id], "acquire-access-1");
  assert(accessOne.turn.events.some((event) => event.type === "admin_access_acquired"
    && event.accessLevelId === "ADMIN_ACCESS_LEVEL_1"));
  assert.equal(run.progressLevel, 1);
  await rest("rest-after-first-access");
  const semicolon = run.entities.find((entity) => entity.kind === "npc" && entity.name === "세미콜론");
  assert(semicolon?.capabilities.canConnect);
  await moveWithin([semicolon], [5], "semicolon-companion");
  await useSkill("CONNECT", [run.playerEntityId, semicolon.id], "connect-semicolon-companion");

  for (const accessTarget of accessTargets.slice(1)) {
    await discoverArea(accessTarget.candidate.areaId, `access-level-${accessTarget.level}`);
    const accessEntity = currentAccessEntity(accessTarget);
    await moveWithin([accessEntity], [6], `access-level-${accessTarget.level}-target`);
    const accessResult = await useSkill("SEARCH", [accessEntity.id], `acquire-access-${accessTarget.level}`);
    assert(accessResult.turn.events.some((event) => event.type === "admin_access_acquired"
      && event.accessLevelId === `ADMIN_ACCESS_LEVEL_${accessTarget.level}`));
    assert.equal(run.progressLevel, accessTarget.level);
  }
  assert.deepEqual(run.adminAccessAcquisitionHistory.map((entry) => entry.accessLevelId), [
    "ADMIN_ACCESS_LEVEL_1", "ADMIN_ACCESS_LEVEL_2", "ADMIN_ACCESS_LEVEL_3"
  ]);

  const clue = run.entities.find((entity) => entity.kind === "npc"
    && entity.state?.evidenceKey === "STORY_REVELATION"
    && entity.capabilities.canConnect);
  assert(clue, "Seed 27 must expose a connectable public essential-clue witness.");
  const clueArea = areaForPoint(run, clue.position);
  await discoverArea(clueArea.id, "essential-clue");
  await moveWithin([clue], [6], "essential-clue-target");
  const clueResult = await useSkill("SEARCH", [clue.id], "confirm-essential-clue");
  assert(clueResult.turn.events.some((event) => event.type === "essential_clue_acquired"));
  assert.equal(run.rootSystemGate.requiredClueEstablished, true);
  assert.equal(run.rootSystemGate.eligible, true);
  await rest("rest-before-root");
  await moveWithin([clue], [5], "essential-clue-companion");
  await useSkill("CONNECT", [run.playerEntityId, clue.id], "connect-essential-clue-witness");

  const rootArea = run.world.areas.find((area) => area.campaignRole === "FINAL_CONVERGENCE");
  assert(rootArea);
  await discoverArea(rootArea.id, "root-system");
  const finaleByRole = Object.fromEntries(run.entities
    .filter((entity) => entity.state?.finaleComponent)
    .map((entity) => [entity.state.finaleComponent, entity]));
  assert(finaleByRole.anchor && finaleByRole.freedom && finaleByRole.threat);
  await moveWithin([finaleByRole.anchor, finaleByRole.freedom], [5, 5], "open-frontier-link");
  const finaleConnect = await useSkill("CONNECT", [finaleByRole.anchor.id, finaleByRole.freedom.id], "link-anchor-freedom");
  assert(finaleConnect.turn.events.some((event) => event.type === "connection_created"));
  await moveWithin([finaleByRole.threat], [3], "open-frontier-threat");
  const finaleDelete = await useSkill("DELETE", [finaleByRole.threat.id], "remove-finale-threat");

  assert(finaleDelete.turn.events.some((event) => event.type === "finale_puzzle_matched"
    && event.endingId === "ENDING_OPEN_FRONTIER"));
  assert(finaleDelete.turn.events.some((event) => event.type === "run_completed"
    && event.endingCode === "ENDING_OPEN_FRONTIER"));
  assert.equal(run.status, "completed");
  assert.equal(run.endingCode, "ENDING_OPEN_FRONTIER");
  assert.notEqual(run.endingCode, "ENDING_EMERGENCY_WITHDRAWAL");
  assert.equal(run.finalePuzzle.status, "resolved");
  assert.equal(run.finalePuzzle.matchedEndingId, "ENDING_OPEN_FRONTIER");
  assert(run.metrics.worldAutonomy >= 50);
  assert(run.emergentStory.meaningfulTurns >= 8);
  assert(run.emergentStory.majorChoiceCount >= 3);
  assert.equal(run.emergentStory.endingEligible, true);
  assert.equal(travelReplayVerified, true);

  const finaleReplay = await service.submitTurn(OWNER_ID, run.id, finaleDelete.payload);
  assert.equal(finaleReplay.fromIdempotencyCache, true);
  assert.equal(finaleReplay.run.version, run.version);
  assert.equal(finaleReplay.run.currentTurn, run.currentTurn);
  const fetched = await service.getRun(OWNER_ID, run.id);
  assert.equal(fetched.status, "completed");
  assert.equal(fetched.version, run.version);
  assert.equal(fetched.endingCode, "ENDING_OPEN_FRONTIER");
  assert.deepEqual(fetched.finaleResolution, run.finaleResolution);

  const terminalBeforeAmbient = structuredClone(await service.store.getRun(OWNER_ID, run.id));
  await assert.rejects(
    service.ambientWander(OWNER_ID, run.id, {
      expectedRunVersion: run.version,
      minX: 0, minY: 0, maxX: run.world.width - 1, maxY: run.world.height - 1
    }),
    (error) => error?.status === 409 && error?.code === "RUN_NOT_ACTIVE"
  );
  assert.deepEqual(await service.store.getRun(OWNER_ID, run.id), terminalBeforeAmbient);

  await assert.rejects(
    service.submitTurn(OWNER_ID, run.id, {
      inputType: "USE_SKILL",
      idempotencyKey: "campaign-terminal-rejection",
      expectedRunVersion: run.version,
      skillId: "SEARCH",
      targetIds: []
    }),
    (error) => error?.status === 409 && error?.code === "RUN_NOT_ACTIVE"
  );
});
