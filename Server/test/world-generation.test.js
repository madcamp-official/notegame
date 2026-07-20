import test from "node:test";
import assert from "node:assert/strict";
import { createCampaignBlueprint } from "../src/domain/campaign.js";
import {
  BIOME_DESCRIPTORS,
  CAMPAIGN_REGION_ROLES,
  DEFAULT_WORLD_SIZE,
  TILE,
  WORLD_GENERATOR_VERSION,
  areaAt,
  computeWorldLayoutHash,
  generateWorld,
  isWalkable,
  publicWorld,
  validateGeneratedWorld
} from "../src/domain/world.js";

const ADMIN_ACCESS_LEVELS = ["ADMIN_ACCESS_LEVEL_1", "ADMIN_ACCESS_LEVEL_2", "ADMIN_ACCESS_LEVEL_3"];
const BATCH_SEEDS = [0, 1, 2, 7, 13, 31, 67, 101, 257, 991, 4093, 20260717, -17, 2147483647];
const SUPPORTED_ACQUISITION_MODES = new Set(["copy", "delete", "connect", "restore", "undo", "search"]);

function decodeRle(rle) {
  const values = [];
  for (const [value, count] of rle) {
    for (let index = 0; index < count; index += 1) values.push(value);
  }
  return values;
}

function countBy(values) {
  const counts = new Map();
  for (const value of values) counts.set(value, (counts.get(value) || 0) + 1);
  return counts;
}

function reachableBeforeFinale(world) {
  const finaleArea = world.areas.find((area) => area.campaignRole === "FINAL_CONVERGENCE");
  const entry = world.points.find((point) => point.id === "entry");
  const key = (point) => `${point.x},${point.y}`;
  const visited = new Set([key(entry)]);
  const queue = [entry];
  for (let cursor = 0; cursor < queue.length; cursor += 1) {
    const current = queue[cursor];
    for (const [dx, dy] of [[1, 0], [-1, 0], [0, 1], [0, -1]]) {
      const next = { x: current.x + dx, y: current.y + dy };
      const nextKey = key(next);
      if (visited.has(nextKey) || !isWalkable(world, next) || areaAt(world, next).id === finaleArea.id) continue;
      visited.add(nextKey);
      queue.push(next);
    }
  }
  return { finaleArea, visited };
}

test("same seed and version reproduce every immutable logical layer while different seeds vary", () => {
  const first = generateWorld(73021);
  const second = generateWorld(73021);
  assert.equal(WORLD_GENERATOR_VERSION, "codria-world.v7");
  assert.deepEqual(DEFAULT_WORLD_SIZE, { width: 160, height: 160 });
  assert.equal(first.layoutHash, second.layoutHash);
  assert.deepEqual(first.tiles, second.tiles);
  assert.deepEqual(first.areaMap, second.areaMap);
  assert.deepEqual(first.biomeMap, second.biomeMap);
  assert.deepEqual(first.areas, second.areas);
  assert.deepEqual(first.routes, second.routes);
  assert.deepEqual(first.progressionGraph, second.progressionGraph);
  assert.deepEqual(first.points, second.points);
  assert.deepEqual(first.placementSlots, second.placementSlots);
  assert.deepEqual(first.generationReport, second.generationReport);

  const hashes = new Set([73021, 73022, 73023, 73024, 73025].map((seed) => generateWorld(seed).layoutHash));
  assert.equal(hashes.size, 5);
});

test("logical-first generator satisfies biome, role, route, gate, POI, and slot invariants across seeds", () => {
  for (const seed of BATCH_SEEDS) {
    const world = generateWorld(seed);
    assert.equal(world.width, 160);
    assert.equal(world.height, 160);
    assert.equal(world.areas.length, 12);
    assert.equal(world.areaMap.length, world.width * world.height);
    assert.equal(world.biomeMap.length, world.width * world.height);

    const biomeCounts = countBy(world.areas.map((area) => area.biomeId));
    assert.deepEqual([...biomeCounts.keys()].sort(), BIOME_DESCRIPTORS.map((biome) => biome.id).sort());
    assert.ok([...biomeCounts.values()].every((count) => count === 2));

    const roleCounts = countBy(world.areas.map((area) => area.campaignRole).filter(Boolean));
    assert.deepEqual([...roleCounts.keys()].sort(), CAMPAIGN_REGION_ROLES.map((role) => role.id).sort());
    assert.ok([...roleCounts.values()].every((count) => count === 1));
    assert.equal(world.areas.filter((area) => !area.campaignRole).length, 6);

    assert.deepEqual(world.progressionGraph.edges, [
      { from: "arrival", to: "stakes" },
      { from: "stakes", to: "bonds" },
      { from: "bonds", to: "truth" },
      { from: "truth", to: "consequence" },
      { from: "consequence", to: "finale" }
    ]);
    assert.equal(world.progressionGraph.finalGate.requiresProgressLevel, 3);
    assert.deepEqual(world.progressionGraph.finalGate.requiresProgressTokens, ADMIN_ACCESS_LEVELS);
    assert.deepEqual(world.progressionGraph.finalGate.requiresCanonicalFact, {
      subject: "collapse_origin", predicate: "inside_admin_control_system", value: true
    });
    assert.equal(world.validation.progressionAcyclic, true);
    const slotById = new Map(world.placementSlots.map((slot) => [slot.id, slot]));
    const blueprint = createCampaignBlueprint({ worldSeed: seed });
    assert.equal(blueprint.requiredStoryBeats.length, 9);
    assert.ok(world.progressionGraph.nodes.every((node) => node.candidateSlotIds.length > 0
      && node.candidateSlotIds.every((slotId) => slotById.has(slotId))
      && new Set(node.acquisitionModes).size >= 1));
    for (const node of world.progressionGraph.nodes) {
      const pathBySlotId = new Map(node.candidateAcquisitionPaths.map((path) => [path.slotId, path]));
      assert.equal(pathBySlotId.size, node.candidateSlotIds.length);
      for (const slotId of node.candidateSlotIds) {
        assert.deepEqual(
          [...pathBySlotId.get(slotId).acquisitionModes].sort(),
          [...slotById.get(slotId).acquisitionModes].sort()
        );
      }
      const pathModeUnion = [...new Set(node.candidateAcquisitionPaths.flatMap((path) => path.acquisitionModes))].sort();
      assert.deepEqual(pathModeUnion, [...node.acquisitionModes].sort());
    }

    assert.ok(world.routes.length >= world.areas.length + 1);
    assert.ok(world.routes.filter((route) => route.isLoop).length >= 2);
    const finaleArea = world.areas.find((area) => area.campaignRole === "FINAL_CONVERGENCE");
    const finaleRoutes = world.routes.filter((route) => route.fromAreaId === finaleArea.id || route.toAreaId === finaleArea.id);
    assert.ok(finaleRoutes.length >= 1);
    assert.ok(finaleRoutes.every((route) => route.gated
      && route.requiresProgressLevel === 3
      && ADMIN_ACCESS_LEVELS.every((token) => route.requiresProgressTokens.includes(token))
      && route.requiresCanonicalFact?.subject === "collapse_origin"
      && route.requiresCanonicalFact?.predicate === "inside_admin_control_system"
      && route.requiresCanonicalFact?.value === true));
    assert.ok(world.routes.every((route) => {
      if (route.kind === "major") return route.width === 3 || route.width === 5;
      if (route.kind === "minor") return route.width === 3;
      return route.kind === "secret" && route.width === 1;
    }));
    assert.ok(world.routes.every((route) => {
      const fromArea = world.areas.find((area) => area.id === route.fromAreaId);
      const toArea = world.areas.find((area) => area.id === route.toAreaId);
      return route.from.x === fromArea.anchor.x
        && route.from.y === fromArea.anchor.y
        && route.to.x === toArea.anchor.x
        && route.to.y === toArea.anchor.y;
    }));
    assert.equal(world.validation.stageReachability.finaleReachableBeforeGate, false);
    assert.equal(world.validation.stageReachability.preFinaleAreaCountBeforeGate, 11);
    assert.equal(world.validation.stageReachability.finaleReachableAfterGate, true);
    assert.equal(world.validation.stageReachability.areaCountAfterGate, 12);

    for (const biome of BIOME_DESCRIPTORS) {
      assert.ok(world.pois.some((poi) => poi.biomeId === biome.id));
    }
    assert.ok(world.pois.every((poi) => poi.encounterSpace.width >= 8 && poi.encounterSpace.height >= 8));

    const requiredSlots = world.placementSlots.filter((slot) => slot.tags.includes("primary_admin_access_candidate") || slot.tags.includes("revelation_candidate"));
    assert.equal(world.placementSlots.filter((slot) => slot.tags.includes("primary_admin_access_candidate")).length, 3);
    assert.equal(world.adminAccessCandidates.length, 6);
    for (const accessLevelId of ADMIN_ACCESS_LEVELS) {
      const candidates = world.adminAccessCandidates.filter((candidate) => candidate.accessLevelId === accessLevelId);
      assert.equal(candidates.length, 2);
      assert.equal(new Set(candidates.map((candidate) => candidate.areaId)).size, 2);
      assert.equal(new Set(candidates.map((candidate) => candidate.actionContext)).size, 2);
    }
    assert.ok(world.placementSlots.filter((slot) => slot.tags.includes("revelation_candidate")).length >= 2);
    assert.equal(world.placementSlots.filter((slot) => slot.tags.includes("finale_candidate")).length, 7);
    assert.ok(world.placementSlots.every((slot) => Number.isInteger(slot.clearanceRadius)
      && slot.clearanceRadius >= 1
      && slot.reachable === true
      && slot.reachability
      && Array.isArray(slot.visualIntents)
      && slot.visualIntents.length > 0
      && Array.isArray(slot.allowedAssetIds)));
    assert.ok(requiredSlots.every((slot) => slot.acquisitionModes.length >= 1
      && slot.acquisitionModes.every((mode) => SUPPORTED_ACQUISITION_MODES.has(mode))));

    assert.equal(world.generationReport.pipeline, "logical_first_constraint_pipeline");
    assert.equal(world.generationReport.turnSimulation.performed, false);
    assert.equal(world.generationReport.checks.finaleReachableBeforeGate, false);
    assert.equal(world.generationReport.checks.finaleReachableAfterGate, true);
    assert.doesNotThrow(() => validateGeneratedWorld(world));
  }
});

test("world dimensions reject strings and fractional values before generation", () => {
  const invalidDimensions = [
    { width: "160", height: 160 },
    { width: 160, height: "160" },
    { width: 160.5, height: 160 },
    { width: 160, height: 160.5 }
  ];
  for (const dimensions of invalidDimensions) {
    assert.throws(
      () => generateWorld(99, dimensions),
      (error) => error?.code === "WORLD_SIZE_INVALID"
    );
  }
});

test("progression validation rejects duplicated paths and campaign-incompatible supported abilities", () => {
  const duplicatedPathWorld = generateWorld(917);
  const duplicatedNode = duplicatedPathWorld.progressionGraph.nodes.find((node) => node.id === "stakes");
  assert.equal(duplicatedNode.candidateAcquisitionPaths.length, 2);
  duplicatedNode.candidateAcquisitionPaths = [
    duplicatedNode.candidateAcquisitionPaths[0],
    duplicatedNode.candidateAcquisitionPaths[0]
  ];
  assert.throws(
    () => validateGeneratedWorld(duplicatedPathWorld),
    (error) => error?.code === "WORLD_ACQUISITION_CONTRACT_INVALID"
  );

  const incompatibleAbilityWorld = generateWorld(918);
  const incompatibleNode = incompatibleAbilityWorld.progressionGraph.nodes.find((node) => node.id === "arrival");
  const incompatiblePath = incompatibleNode.candidateAcquisitionPaths[0];
  const incompatibleSlot = incompatibleAbilityWorld.placementSlots.find((slot) => slot.id === incompatiblePath.slotId);
  incompatiblePath.acquisitionModes = ["undo"];
  incompatibleSlot.acquisitionModes = ["undo"];
  incompatibleNode.acquisitionModes = [...new Set(
    incompatibleNode.candidateAcquisitionPaths.flatMap((path) => path.acquisitionModes)
  )];
  assert.throws(
    () => validateGeneratedWorld(incompatibleAbilityWorld),
    (error) => error?.code === "WORLD_CAMPAIGN_ABILITY_ALIGNMENT_INVALID"
  );
});

test("layout hash covers immutable semantic data and validation rejects post-generation mutation", () => {
  const world = generateWorld(919);
  assert.equal(computeWorldLayoutHash(world), world.layoutHash);
  world.pois[0].nameKo += " (변조)";
  assert.notEqual(computeWorldLayoutHash(world), world.layoutHash);
  assert.throws(
    () => validateGeneratedWorld(world),
    (error) => error?.code === "WORLD_LAYOUT_HASH_MISMATCH"
  );
});

test("route validation rejects a declared multi-tile road whose raster shoulder is narrowed", () => {
  const world = generateWorld(11);
  const route = world.routes.find((item) => item.width > 1);
  assert.ok(route);
  const centerline = new Set(route.path.map((point) => `${point.x},${point.y}`));
  const radius = Math.floor(route.width / 2);
  let shoulder = null;
  for (const point of route.path) {
    for (let y = point.y - radius; y <= point.y + radius && !shoulder; y += 1) {
      for (let x = point.x - radius; x <= point.x + radius; x += 1) {
        if (!centerline.has(`${x},${y}`) && world.tiles[y * world.width + x] === TILE.ROAD) {
          shoulder = { x, y };
          break;
        }
      }
    }
    if (shoulder) break;
  }
  assert.ok(shoulder, "generated multi-tile route must expose a raster shoulder");
  world.tiles[shoulder.y * world.width + shoulder.x] = TILE.GRASS;
  assert.throws(
    () => validateGeneratedWorld(world),
    (error) => error?.code === "WORLD_ROUTE_WIDTH_RASTER_INVALID"
  );
});

test("all pre-Root campaign targets remain tile-reachable before administrator authorization", () => {
  const cases = [
    { seed: -32, width: 120, height: 256 },
    { seed: 73, width: 160, height: 160 }
  ];
  for (const options of cases) {
    const world = generateWorld(options.seed, { width: options.width, height: options.height });
    const { finaleArea, visited } = reachableBeforeFinale(world);
    const targets = [
      ...world.areas.filter((area) => area.id !== finaleArea.id).map((area) => ({ id: area.id, x: area.anchor.x, y: area.anchor.y })),
      ...world.points.filter((point) => point.areaId !== finaleArea.id),
      ...world.placementSlots.filter((slot) => slot.areaId !== finaleArea.id)
    ];
    const unreachable = targets.filter((target) => !visited.has(`${target.x},${target.y}`)).map((target) => target.id);
    assert.deepEqual(unreachable, [], `pre-finale reachability failed for seed ${options.seed} at ${options.width}x${options.height}`);
    assert.ok(world.routes.filter((route) => !route.gated)
      .every((route) => route.path.every((point) => areaAt(world, point).id !== finaleArea.id)));
  }
});

test("areaAt resolves the distance-field area map and public DTO exposes lossless RLE layers", () => {
  const world = generateWorld(481516);
  for (let y = 0; y < world.height; y += 1) {
    for (let x = 0; x < world.width; x += 1) {
      const tileIndex = y * world.width + x;
      assert.equal(areaAt(world, { x, y }).index, world.areaMap[tileIndex]);
    }
  }

  const dto = publicWorld(world);
  assert.deepEqual(decodeRle(dto.tilesRle), world.tiles);
  assert.deepEqual(decodeRle(dto.areaMapRle), world.areaMap);
  assert.deepEqual(decodeRle(dto.biomeMapRle), world.biomeMap);
  assert.deepEqual(dto.areaMapLegend, world.areas.map((area) => area.id));
  assert.deepEqual(dto.biomeMapLegend, BIOME_DESCRIPTORS.map((biome) => biome.id));
  assert.deepEqual(dto.routes, world.routes);
  assert.deepEqual(dto.progressionGraph, world.progressionGraph);
  assert.deepEqual(dto.generationReport, world.generationReport);
});

test("terrain obstacles form low-frequency clusters instead of isolated salt-and-pepper noise", () => {
  for (const seed of [3, 19, 83, 509]) {
    const world = generateWorld(seed);
    let specialTiles = 0;
    let clusteredTiles = 0;
    for (let y = 1; y < world.height - 1; y += 1) {
      for (let x = 1; x < world.width - 1; x += 1) {
        const tile = world.tiles[y * world.width + x];
        if (![TILE.WALL, TILE.WATER, TILE.HAZARD, TILE.RUIN].includes(tile)) continue;
        specialTiles += 1;
        const sameNeighbor = [[1, 0], [-1, 0], [0, 1], [0, -1]].some(([dx, dy]) => {
          return world.tiles[(y + dy) * world.width + x + dx] === tile;
        });
        if (sameNeighbor) clusteredTiles += 1;
      }
    }
    assert.ok(specialTiles > 0);
    assert.ok(clusteredTiles / specialTiles > 0.95);
  }
});
