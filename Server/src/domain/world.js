import { createHash } from "node:crypto";
import { assert } from "../errors.js";
import { CAMPAIGN_ALLOWED_ABILITIES_BY_ROLE } from "./campaign.js";
import {
  ADMIN_ACCESS_LEVELS,
  CAMPAIGN_ACTION_CONTEXTS,
  CAMPAIGN_REGION_AXES,
  KEYBOARD_SKILLS,
  ROOT_SYSTEM,
  WORLD_CODRIA,
  WORLD_NAME_KO,
  validateAdminAccessCandidates
} from "./codria-contract.js";

export const WORLD_GENERATOR_VERSION = "codria-world.v7";
export const DEFAULT_WORLD_SIZE = Object.freeze({ width: 160, height: 160 });
export const TILE = Object.freeze({ GRASS: 0, WALL: 1, HAZARD: 2, ROAD: 3, WATER: 4, RUIN: 5 });
export const TILE_NAMES = Object.freeze(["grass", "wall", "hazard", "road", "water", "ruin"]);

export const BIOME_DESCRIPTORS = Object.freeze([
  { id: "temperate_forest_field", name: "Temperate Forest Field", nameKo: "온대 숲과 들판", visualIntent: "layered green canopy, broad fields, soft firefly motes", palette: ["#173B2A", "#4E9B52", "#D4FF70"], tileWeights: { wall: 80, water: 25, hazard: 35, ruin: 30 }, offRoadCost: 1 },
  { id: "river_wetland", name: "River Wetland", nameKo: "강과 습지", visualIntent: "branching rivers, reed buffers, reflective cyan shallows", palette: ["#173447", "#2C7A7B", "#A8E6CF"], tileWeights: { wall: 35, water: 180, hazard: 70, ruin: 20 }, offRoadCost: 2 },
  { id: "arid_desert", name: "Arid Desert", nameKo: "건조 사막", visualIntent: "ochre dunes, heat shimmer, exposed stone strata", palette: ["#5D3A1A", "#C9853D", "#F4D58D"], tileWeights: { wall: 45, water: 8, hazard: 150, ruin: 65 }, offRoadCost: 2 },
  { id: "frost_highland", name: "Frost Highland", nameKo: "설원 고지", visualIntent: "frost ridges, pale sky, blue-white signal crystals", palette: ["#25364A", "#6C8EAD", "#E6F4FF"], tileWeights: { wall: 115, water: 55, hazard: 100, ruin: 25 }, offRoadCost: 2 },
  { id: "subterranean_cavern", name: "Subterranean Cavern", nameKo: "지하 동굴", visualIntent: "dark basalt chambers, luminous crystal veins, deep violet pools", palette: ["#17141F", "#513C6B", "#B794F4"], tileWeights: { wall: 205, water: 45, hazard: 80, ruin: 85 }, offRoadCost: 2 },
  { id: "ancient_ruins", name: "Ancient Ruins", nameKo: "고대 유적", visualIntent: "weathered stone glyphs, rust-gold reliefs, collapsed arches", palette: ["#3B332C", "#8D6E4D", "#D9B56D"], tileWeights: { wall: 120, water: 18, hazard: 50, ruin: 210 }, offRoadCost: 2 }
]);

export const CAMPAIGN_REGION_ROLES = Object.freeze([
  { id: "ARRIVAL_CATALYST", regionAxis: "REGION_BUG_FOREST", phase: 1, landmarkNames: ["스택 이끼길", "예외 포자 군락", "디버그 초원"] },
  { id: "LOCAL_STAKES", regionAxis: "REGION_BUFFER_VILLAGE", phase: 2, landmarkNames: ["캐시 광장", "대기열 수로", "버퍼 피난처"] },
  { id: "RELATIONSHIP_CONFLICT", regionAxis: "REGION_DEADLOCK_CITY", phase: 3, landmarkNames: ["교착 성문", "뮤텍스 다리", "스레드 시장"] },
  { id: "HIDDEN_TRUTH", regionAxis: "REGION_DATA_GRAND_LIBRARY", phase: 4, landmarkNames: ["아카이브 코어", "인덱스 서고", "불변 기록실"] },
  { id: "CONSEQUENCE_RETURN", regionAxis: "REGION_LEGACY_CITADEL", phase: 5, landmarkNames: ["레거시 성벽", "리팩터 탑", "부채 법정"] },
  { id: "FINAL_CONVERGENCE", regionAxis: ROOT_SYSTEM, phase: 6, landmarkNames: ["루트 콘솔", "권한 커널", "최종 배치실"] }
]);

const AREA_COLUMNS = 4;
const AREA_ROWS = 3;
const AREA_COUNT = AREA_COLUMNS * AREA_ROWS;
const FINALE_ROLE = "FINAL_CONVERGENCE";
const ARRIVAL_ROLE = "ARRIVAL_CATALYST";
const ADMIN_ACCESS_TOKENS = Object.freeze(ADMIN_ACCESS_LEVELS.map((item) => item.id));
const SUPPORTED_ACQUISITION_MODES = Object.freeze(new Set([
  "copy", "delete", "connect", "restore", "undo"
]));
const DIRECTIONS = Object.freeze([[1, 0], [-1, 0], [0, 1], [0, -1]]);
const FINALE_COMPONENTS = Object.freeze([
  "FINAL_ANCHOR", "FINAL_SAFEGUARD", "FINAL_MEMORY", "FINAL_FREEDOM",
  "FINAL_THREAT", "FINAL_PASSAGE", "FINAL_WITNESS"
]);
const FINALE_CLUSTER_OFFSETS = Object.freeze([
  Object.freeze({ x: 0, y: -2 }), Object.freeze({ x: 2, y: -1 }), Object.freeze({ x: 2, y: 1 }),
  Object.freeze({ x: 0, y: 2 }), Object.freeze({ x: -1, y: 1 }), Object.freeze({ x: -2, y: -1 }),
  Object.freeze({ x: 0, y: 0 })
]);

class StableRandom {
  constructor(seed) { this.state = seed === 0 ? 0x6d2b79f5 : seed >>> 0; }
  next(maximum) {
    assert(Number.isInteger(maximum) && maximum > 0, 500, "RNG_RANGE_INVALID", "Stable RNG requires a positive integer range.");
    let value = this.state;
    value ^= value << 13;
    value ^= value >>> 17;
    value ^= value << 5;
    this.state = value >>> 0;
    return this.state % maximum;
  }
}

class DisjointSet {
  constructor(values) {
    this.parent = new Map(values.map((value) => [value, value]));
  }
  find(value) {
    const parent = this.parent.get(value);
    if (parent === value) return value;
    const root = this.find(parent);
    this.parent.set(value, root);
    return root;
  }
  union(left, right) {
    const leftRoot = this.find(left);
    const rightRoot = this.find(right);
    if (leftRoot === rightRoot) return false;
    this.parent.set(rightRoot, leftRoot);
    return true;
  }
}

function seed32(worldSeed, label = "") {
  return createHash("sha256")
    .update(String(worldSeed) + "|" + WORLD_GENERATOR_VERSION + "|" + label)
    .digest()
    .readUInt32LE(0);
}

function stableId(seed, label) {
  return createHash("sha256")
    .update(String(seed) + "|" + WORLD_GENERATOR_VERSION + "|" + label)
    .digest("hex")
    .slice(0, 20);
}

function mix32(value) {
  let mixed = value >>> 0;
  mixed ^= mixed >>> 16;
  mixed = Math.imul(mixed, 0x7feb352d);
  mixed ^= mixed >>> 15;
  mixed = Math.imul(mixed, 0x846ca68b);
  mixed ^= mixed >>> 16;
  return mixed >>> 0;
}

function latticeValue(seed, x, y, salt) {
  const value = seed ^ Math.imul(x, 0x1f123bb5) ^ Math.imul(y, 0x5f356495) ^ Math.imul(salt, 0x6c8e9cf5);
  return (mix32(value) / 0xffffffff) * 2 - 1;
}

function smoothstep(value) { return value * value * (3 - 2 * value); }
function lerp(left, right, amount) { return left + (right - left) * amount; }

function valueNoise(seed, x, y, scale, salt) {
  const gridX = Math.floor(x / scale);
  const gridY = Math.floor(y / scale);
  const localX = smoothstep((x - gridX * scale) / scale);
  const localY = smoothstep((y - gridY * scale) / scale);
  const top = lerp(latticeValue(seed, gridX, gridY, salt), latticeValue(seed, gridX + 1, gridY, salt), localX);
  const bottom = lerp(latticeValue(seed, gridX, gridY + 1, salt), latticeValue(seed, gridX + 1, gridY + 1, salt), localX);
  return lerp(top, bottom, localY);
}

function indexOf(width, x, y) { return y * width + x; }
function positionKey(point) { return String(point.x) + "," + String(point.y); }
function sameUniqueStringSet(left, right) {
  if (!Array.isArray(left) || !Array.isArray(right)
    || left.some((value) => typeof value !== "string")
    || right.some((value) => typeof value !== "string")) return false;
  const leftSet = new Set(left);
  const rightSet = new Set(right);
  return leftSet.size === left.length && rightSet.size === right.length
    && leftSet.size === rightSet.size
    && [...leftSet].every((value) => rightSet.has(value));
}
function manhattan(left, right) { return Math.abs(left.x - right.x) + Math.abs(left.y - right.y); }
function distanceSquared(left, right) {
  const dx = left.x - right.x;
  const dy = left.y - right.y;
  return dx * dx + dy * dy;
}
function clamp(value, minimum, maximum) { return Math.max(minimum, Math.min(maximum, value)); }

function shuffle(values, random) {
  const result = [...values];
  for (let index = result.length - 1; index > 0; index -= 1) {
    const other = random.next(index + 1);
    [result[index], result[other]] = [result[other], result[index]];
  }
  return result;
}

function encodeRle(values) {
  const encoded = [];
  for (const value of values) {
    const last = encoded[encoded.length - 1];
    if (last && last[0] === value) last[1] += 1;
    else encoded.push([value, 1]);
  }
  return encoded;
}

function createProgressionGraph() {
  return {
    version: "codria-progression.v4",
    nodes: [
      { id: "arrival", campaignRole: ARRIVAL_ROLE, requires: [] },
      { id: "stakes", campaignRole: "LOCAL_STAKES", requires: ["arrival"], rewardProgressLevel: 1, rewardProgressToken: "ADMIN_ACCESS_LEVEL_1" },
      { id: "bonds", campaignRole: "RELATIONSHIP_CONFLICT", requires: ["stakes"], rewardProgressLevel: 2, rewardProgressToken: "ADMIN_ACCESS_LEVEL_2" },
      { id: "truth", campaignRole: "HIDDEN_TRUTH", requires: ["bonds"] },
      { id: "consequence", campaignRole: "CONSEQUENCE_RETURN", requires: ["truth"], rewardProgressLevel: 3, rewardProgressToken: "ADMIN_ACCESS_LEVEL_3" },
      { id: "finale", campaignRole: FINALE_ROLE, requires: ["consequence"] }
    ],
    edges: [
      { from: "arrival", to: "stakes" },
      { from: "stakes", to: "bonds" },
      { from: "bonds", to: "truth" },
      { from: "truth", to: "consequence" },
      { from: "consequence", to: "finale" }
    ],
    finalGate: {
      requiresProgressLevel: 3,
      requiresProgressTokens: [...ADMIN_ACCESS_TOKENS],
      requiresCanonicalFact: {
        subject: "collapse_origin",
        predicate: "inside_admin_control_system",
        value: true
      },
      requiresCompletedNodes: ["truth", "consequence"]
    }
  };
}

function topologicalOrder(graph) {
  const indegree = new Map(graph.nodes.map((node) => [node.id, 0]));
  const outgoing = new Map(graph.nodes.map((node) => [node.id, []]));
  for (const edge of graph.edges) {
    if (!indegree.has(edge.from) || !indegree.has(edge.to)) return null;
    indegree.set(edge.to, indegree.get(edge.to) + 1);
    outgoing.get(edge.from).push(edge.to);
  }
  const queue = graph.nodes.map((node) => node.id).filter((id) => indegree.get(id) === 0).sort();
  const result = [];
  while (queue.length > 0) {
    const current = queue.shift();
    result.push(current);
    for (const next of outgoing.get(current)) {
      indegree.set(next, indegree.get(next) - 1);
      if (indegree.get(next) === 0) {
        queue.push(next);
        queue.sort();
      }
    }
  }
  return result.length === graph.nodes.length ? result : null;
}

function createAreaAnchors(worldSeed, width, height, random) {
  const biomeAssignments = shuffle([...BIOME_DESCRIPTORS, ...BIOME_DESCRIPTORS], random);
  const anchors = [];
  for (let row = 0; row < AREA_ROWS; row += 1) {
    for (let column = 0; column < AREA_COLUMNS; column += 1) {
      const index = row * AREA_COLUMNS + column;
      const x0 = Math.floor(column * width / AREA_COLUMNS);
      const x1 = Math.floor((column + 1) * width / AREA_COLUMNS) - 1;
      const y0 = Math.floor(row * height / AREA_ROWS);
      const y1 = Math.floor((row + 1) * height / AREA_ROWS) - 1;
      const jitterX = Math.max(2, Math.floor((x1 - x0 + 1) * 0.18));
      const jitterY = Math.max(2, Math.floor((y1 - y0 + 1) * 0.18));
      const anchor = {
        x: clamp(Math.floor((x0 + x1) / 2) + random.next(jitterX * 2 + 1) - jitterX, x0 + 7, x1 - 7),
        y: clamp(Math.floor((y0 + y1) / 2) + random.next(jitterY * 2 + 1) - jitterY, y0 + 7, y1 - 7)
      };
      anchors.push({ index, anchor, biome: biomeAssignments[index] });
    }
  }

  const cornerIndices = [0, AREA_COLUMNS - 1, AREA_COUNT - AREA_COLUMNS, AREA_COUNT - 1];
  const arrivalIndex = cornerIndices[random.next(cornerIndices.length)];
  const finaleIndex = anchors
    .filter((item) => item.index !== arrivalIndex)
    .sort((left, right) => distanceSquared(anchors[arrivalIndex].anchor, right.anchor) - distanceSquared(anchors[arrivalIndex].anchor, left.anchor) || left.index - right.index)[0]
    .index;
  const remaining = shuffle(anchors.map((item) => item.index).filter((index) => index !== arrivalIndex && index !== finaleIndex), random);
  const roleAreaIndices = [arrivalIndex, remaining[0], remaining[1], remaining[2], remaining[3], finaleIndex];
  const roleByArea = new Map(CAMPAIGN_REGION_ROLES.map((role, index) => [roleAreaIndices[index], role]));

  return anchors.map((item) => {
    const role = roleByArea.get(item.index) || null;
    const roleText = role ? role.id : "support";
    return {
      index: item.index,
      id: "area." + stableId(worldSeed, "area:" + String(item.index) + ":" + item.biome.id),
      name: item.biome.name + " " + String(item.index + 1),
      nameKo: item.biome.nameKo + " 권역 " + String(item.index + 1),
      kind: "campaign_region",
      biomeId: item.biome.id,
      campaignRole: role ? role.id : "",
      regionAxis: role ? role.regionAxis : "",
      anchor: { ...item.anchor },
      bounds: { x: item.anchor.x, y: item.anchor.y, width: 1, height: 1 },
      tileCount: 0,
      neighborAreaIds: [],
      summary: item.biome.nameKo + " 지형에 " + (role?.regionAxis || roleText) + " 지역 축과 " + roleText + " 캠페인 역할이 독립적으로 배정된 생성 권역."
    };
  });
}

function createAreaAndBiomeMaps(areas, width, height, noiseSeed) {
  const areaMap = new Array(width * height);
  const biomeIndexById = new Map(BIOME_DESCRIPTORS.map((biome, index) => [biome.id, index]));
  const biomeMap = new Array(width * height);
  const cellScale = Math.min(width / AREA_COLUMNS, height / AREA_ROWS);
  const amplitude = cellScale * cellScale * 0.20;
  const noiseScale = Math.max(16, Math.round(cellScale * 0.58));

  for (let y = 0; y < height; y += 1) {
    for (let x = 0; x < width; x += 1) {
      let bestArea = 0;
      let bestScore = Number.POSITIVE_INFINITY;
      for (const area of areas) {
        const boundaryNoise = valueNoise(noiseSeed, x, y, noiseScale, 101 + area.index * 37)
          + valueNoise(noiseSeed, x, y, noiseScale * 2, 211 + area.index * 53) * 0.35;
        const score = distanceSquared({ x, y }, area.anchor) + boundaryNoise * amplitude;
        if (score < bestScore || (score === bestScore && area.index < bestArea)) {
          bestScore = score;
          bestArea = area.index;
        }
      }
      const tileIndex = indexOf(width, x, y);
      areaMap[tileIndex] = bestArea;
      biomeMap[tileIndex] = biomeIndexById.get(areas[bestArea].biomeId);
    }
  }

  for (const area of areas) {
    const tileIndex = indexOf(width, area.anchor.x, area.anchor.y);
    areaMap[tileIndex] = area.index;
    biomeMap[tileIndex] = biomeIndexById.get(area.biomeId);
  }
  return { areaMap, biomeMap, noiseScale, amplitude };
}

function updateAreaGeometry(areas, areaMap, width, height) {
  const stats = areas.map(() => ({ minX: width, minY: height, maxX: -1, maxY: -1, count: 0, neighbors: new Set() }));
  for (let y = 0; y < height; y += 1) {
    for (let x = 0; x < width; x += 1) {
      const areaIndex = areaMap[indexOf(width, x, y)];
      const stat = stats[areaIndex];
      stat.minX = Math.min(stat.minX, x);
      stat.minY = Math.min(stat.minY, y);
      stat.maxX = Math.max(stat.maxX, x);
      stat.maxY = Math.max(stat.maxY, y);
      stat.count += 1;
      if (x + 1 < width) {
        const right = areaMap[indexOf(width, x + 1, y)];
        if (right !== areaIndex) {
          stat.neighbors.add(right);
          stats[right].neighbors.add(areaIndex);
        }
      }
      if (y + 1 < height) {
        const down = areaMap[indexOf(width, x, y + 1)];
        if (down !== areaIndex) {
          stat.neighbors.add(down);
          stats[down].neighbors.add(areaIndex);
        }
      }
    }
  }
  for (const area of areas) {
    const stat = stats[area.index];
    area.bounds = {
      x: stat.minX,
      y: stat.minY,
      width: stat.maxX - stat.minX + 1,
      height: stat.maxY - stat.minY + 1
    };
    area.tileCount = stat.count;
    area.neighborAreaIds = [...stat.neighbors].sort((left, right) => left - right).map((index) => areas[index].id);
  }
}

function candidateEdges(areas) {
  const edges = [];
  for (let left = 0; left < areas.length; left += 1) {
    for (let right = left + 1; right < areas.length; right += 1) {
      edges.push({
        left,
        right,
        weight: distanceSquared(areas[left].anchor, areas[right].anchor),
        tie: areas[left].id + "|" + areas[right].id
      });
    }
  }
  edges.sort((a, b) => a.weight - b.weight || a.tie.localeCompare(b.tie));
  return edges;
}

function fourConnectedLine(from, to, horizontalFirst = true) {
  const path = [{ x: from.x, y: from.y }];
  let x = from.x;
  let y = from.y;
  const totalX = Math.abs(to.x - from.x);
  const totalY = Math.abs(to.y - from.y);
  let movedX = 0;
  let movedY = 0;
  while (x !== to.x || y !== to.y) {
    const canMoveX = x !== to.x;
    const canMoveY = y !== to.y;
    const xRatio = totalX === 0 ? Number.POSITIVE_INFINITY : (movedX + 1) / totalX;
    const yRatio = totalY === 0 ? Number.POSITIVE_INFINITY : (movedY + 1) / totalY;
    const moveX = canMoveX && (!canMoveY || xRatio < yRatio || (xRatio === yRatio && horizontalFirst));
    if (moveX) {
      x += Math.sign(to.x - x);
      movedX += 1;
    } else {
      y += Math.sign(to.y - y);
      movedY += 1;
    }
    path.push({ x, y });
  }
  return path;
}

function joinPathSegments(points, horizontalFirst) {
  const path = [];
  for (let index = 0; index + 1 < points.length; index += 1) {
    const segment = fourConnectedLine(points[index], points[index + 1], index % 2 === 0 ? horizontalFirst : !horizontalFirst);
    if (path.length > 0) segment.shift();
    path.push(...segment);
  }
  return path;
}

function routePathCandidates(from, to, width, height, routeSeed) {
  const midpoint = {
    x: clamp(Math.round((from.x + to.x) / 2), 2, width - 3),
    y: clamp(Math.round((from.y + to.y) / 2), 2, height - 3)
  };
  const dx = to.x - from.x;
  const dy = to.y - from.y;
  const offsetMagnitude = 3 + routeSeed % 7;
  const offsetX = dy === 0 ? 0 : Math.sign(dy) * offsetMagnitude;
  const offsetY = dx === 0 ? 0 : -Math.sign(dx) * offsetMagnitude;
  const bent = {
    x: clamp(midpoint.x + offsetX, 2, width - 3),
    y: clamp(midpoint.y + offsetY, 2, height - 3)
  };
  return [
    fourConnectedLine(from, to, true),
    fourConnectedLine(from, to, false),
    joinPathSegments([from, midpoint, to], true),
    joinPathSegments([from, bent, to], false)
  ];
}

function pathAvoidsArea(path, width, height, areaMap, blockedAreaIndex, clearanceRadius = 0) {
  return path.every((point) => {
    for (let y = point.y - clearanceRadius; y <= point.y + clearanceRadius; y += 1) {
      for (let x = point.x - clearanceRadius; x <= point.x + clearanceRadius; x += 1) {
        if (x <= 0 || y <= 0 || x >= width - 1 || y >= height - 1) return false;
        if (areaMap[indexOf(width, x, y)] === blockedAreaIndex) return false;
      }
    }
    return true;
  });
}

function shortestPathAvoidingArea(from, to, width, height, areaMap, blockedAreaIndex, clearanceRadius, routeSeed) {
  const total = width * height;
  const parent = new Int32Array(total);
  parent.fill(-1);
  const queue = new Int32Array(total);
  const startIndex = indexOf(width, from.x, from.y);
  const goalIndex = indexOf(width, to.x, to.y);
  const directions = [...DIRECTIONS];
  const rotation = routeSeed % directions.length;
  directions.push(...directions.splice(0, rotation));
  const allowed = (x, y) => pathAvoidsArea([{ x, y }], width, height, areaMap, blockedAreaIndex, clearanceRadius);
  assert(allowed(from.x, from.y) && allowed(to.x, to.y), 500, "WORLD_ROUTE_ENDPOINT_CLEARANCE_INVALID", "A pre-finale route endpoint overlaps the finale clearance boundary.");

  let head = 0;
  let tail = 0;
  queue[tail++] = startIndex;
  parent[startIndex] = startIndex;
  while (head < tail && parent[goalIndex] === -1) {
    const currentIndex = queue[head++];
    const currentX = currentIndex % width;
    const currentY = Math.floor(currentIndex / width);
    for (const [dx, dy] of directions) {
      const x = currentX + dx;
      const y = currentY + dy;
      if (!allowed(x, y)) continue;
      const nextIndex = indexOf(width, x, y);
      if (parent[nextIndex] !== -1) continue;
      parent[nextIndex] = currentIndex;
      queue[tail++] = nextIndex;
    }
  }
  assert(parent[goalIndex] !== -1, 500, "WORLD_ROUTE_AVOIDANCE_FAILED", "A pre-finale route could not avoid the gated finale area.");
  const reversed = [];
  for (let current = goalIndex; ; current = parent[current]) {
    reversed.push({ x: current % width, y: Math.floor(current / width) });
    if (current === startIndex) break;
  }
  reversed.reverse();
  return reversed;
}

function chooseRoutePath(from, to, width, height, areaMap, finaleAreaIndex, incidentToFinale, routeSeed, roadWidth) {
  const candidates = routePathCandidates(from, to, width, height, routeSeed);
  const clearanceRadius = Math.floor(roadWidth / 2);
  const boundedCandidates = candidates.filter((path) => pathAvoidsArea(path, width, height, areaMap, -1, clearanceRadius));
  candidates.length = 0;
  candidates.push(...boundedCandidates);
  if (!incidentToFinale) {
    const avoidingCandidates = candidates.filter((path) => pathAvoidsArea(path, width, height, areaMap, finaleAreaIndex, clearanceRadius));
    if (avoidingCandidates.length === 0) {
      return shortestPathAvoidingArea(from, to, width, height, areaMap, finaleAreaIndex, clearanceRadius, routeSeed);
    }
    candidates.length = 0;
    candidates.push(...avoidingCandidates);
  }
  assert(candidates.length > 0, 500, "WORLD_ROUTE_CLEARANCE_FAILED", "A generated route could not preserve its declared raster width inside the world boundary.");
  candidates.sort((left, right) => {
    const score = (path) => {
      let turns = 0;
      for (let index = 2; index < path.length; index += 1) {
        const priorDx = path[index - 1].x - path[index - 2].x;
        const priorDy = path[index - 1].y - path[index - 2].y;
        const nextDx = path[index].x - path[index - 1].x;
        const nextDy = path[index].y - path[index - 1].y;
        if (priorDx !== nextDx || priorDy !== nextDy) turns += 1;
      }
      return path.length * 10 + turns;
    };
    return score(left) - score(right);
  });
  return candidates[0];
}

function createRoutes(worldSeed, areas, areaMap, width, height) {
  const finaleArea = areas.find((area) => area.campaignRole === FINALE_ROLE);
  const preFinaleAreas = areas.filter((area) => area.id !== finaleArea.id);
  const allCandidates = candidateEdges(areas);
  const preFinaleIds = new Set(preFinaleAreas.map((area) => area.index));
  const preFinaleCandidates = allCandidates.filter((edge) => preFinaleIds.has(edge.left) && preFinaleIds.has(edge.right));
  const disjoint = new DisjointSet(preFinaleAreas.map((area) => area.index));
  const selected = [];
  const selectedKeys = new Set();

  const addEdge = (edge, isLoop, kind) => {
    const key = String(Math.min(edge.left, edge.right)) + ":" + String(Math.max(edge.left, edge.right));
    if (selectedKeys.has(key)) return false;
    selectedKeys.add(key);
    selected.push({ ...edge, isLoop, kind });
    return true;
  };

  for (const edge of preFinaleCandidates) {
    if (disjoint.union(edge.left, edge.right)) addEdge(edge, false, "major");
    if (selected.length === preFinaleAreas.length - 1) break;
  }

  const finaleCandidates = allCandidates.filter((edge) => edge.left === finaleArea.index || edge.right === finaleArea.index);
  addEdge(finaleCandidates[0], false, "major");
  addEdge(finaleCandidates[1], true, "minor");

  const loopCandidates = preFinaleCandidates.filter((edge) => {
    const key = String(Math.min(edge.left, edge.right)) + ":" + String(Math.max(edge.left, edge.right));
    return !selectedKeys.has(key);
  });
  addEdge(loopCandidates[0], true, "minor");
  addEdge(loopCandidates[1], true, "secret");

  return selected.map((edge, index) => {
    const fromArea = areas[edge.left];
    const toArea = areas[edge.right];
    const incidentToFinale = fromArea.id === finaleArea.id || toArea.id === finaleArea.id;
    const routeSeed = seed32(worldSeed, "route:" + fromArea.id + ":" + toArea.id);
    const widthValue = edge.kind === "secret" ? 1 : edge.kind === "minor" ? 3 : routeSeed % 2 === 0 ? 3 : 5;
    const path = chooseRoutePath(fromArea.anchor, toArea.anchor, width, height, areaMap, finaleArea.index, incidentToFinale, routeSeed, widthValue);
    return {
      id: "route." + stableId(worldSeed, fromArea.id + ":" + toArea.id),
      fromAreaId: fromArea.id,
      toAreaId: toArea.id,
      from: { ...fromArea.anchor },
      to: { ...toArea.anchor },
      kind: edge.kind,
      traversalKind: edge.kind === "secret" ? "secret_route" : "safe_route",
      width: widthValue,
      isLoop: edge.isLoop,
      gated: incidentToFinale,
      requiresProgressLevel: incidentToFinale ? 3 : 0,
      requiresProgressTokens: incidentToFinale ? [...ADMIN_ACCESS_TOKENS] : [],
      requiresCanonicalFact: incidentToFinale
        ? { subject: "collapse_origin", predicate: "inside_admin_control_system", value: true }
        : null,
      campaignTurnConsumed: false,
      path
    };
  });
}

function routeReachableAreaIds(routes, startAreaId, state) {
  const adjacency = new Map();
  const add = (from, to) => {
    if (!adjacency.has(from)) adjacency.set(from, []);
    adjacency.get(from).push(to);
  };
  for (const route of routes) {
    const allowed = !route.gated || (state.progressLevel >= route.requiresProgressLevel
      && route.requiresProgressTokens.every((token) => state.progressTokens.includes(token)));
    if (!allowed) continue;
    add(route.fromAreaId, route.toAreaId);
    add(route.toAreaId, route.fromAreaId);
  }
  const visited = new Set([startAreaId]);
  const queue = [startAreaId];
  for (let cursor = 0; cursor < queue.length; cursor += 1) {
    for (const next of adjacency.get(queue[cursor]) || []) {
      if (visited.has(next)) continue;
      visited.add(next);
      queue.push(next);
    }
  }
  return visited;
}

function findUniqueAreaPosition(area, preferred, areaMap, width, height, reserved, minimumDistance = 0, borderClearance = 1) {
  const maximumRadius = Math.max(width, height);
  for (let radius = 0; radius < maximumRadius; radius += 1) {
    for (let y = Math.max(borderClearance, preferred.y - radius); y <= Math.min(height - 1 - borderClearance, preferred.y + radius); y += 1) {
      for (let x = Math.max(borderClearance, preferred.x - radius); x <= Math.min(width - 1 - borderClearance, preferred.x + radius); x += 1) {
        if (Math.abs(x - preferred.x) + Math.abs(y - preferred.y) !== radius) continue;
        if (areaMap[indexOf(width, x, y)] !== area.index) continue;
        const candidate = { x, y };
        if (reserved.has(positionKey(candidate))) continue;
        if (minimumDistance > 0 && [...reserved].some((key) => {
          const [reservedX, reservedY] = key.split(",").map(Number);
          return Math.abs(reservedX - x) + Math.abs(reservedY - y) < minimumDistance;
        })) continue;
        return candidate;
      }
    }
  }
  throw new Error("No unique coordinate exists in area " + area.id + ".");
}

function createPoints(worldSeed, areas, areaMap, width, height) {
  const reserved = new Set();
  const arrivalArea = areas.find((area) => area.campaignRole === ARRIVAL_ROLE);
  const entry = { id: "entry", kind: "entry", name: "Codria Crash Site", nameKo: "코드리아 추락지", ...arrivalArea.anchor, areaId: arrivalArea.id, biomeId: arrivalArea.biomeId, campaignRole: ARRIVAL_ROLE, regionAxis: arrivalArea.regionAxis, clearingRadius: 4, encounterSpace: { width: 9, height: 9 } };
  reserved.add(positionKey(entry));

  const hubs = areas.map((area, index) => {
    const preferred = { x: area.anchor.x + (index % 2 === 0 ? -3 : 3), y: area.anchor.y };
    const position = findUniqueAreaPosition(area, preferred, areaMap, width, height, reserved, 1, 5);
    reserved.add(positionKey(position));
    return {
      id: "hub." + stableId(worldSeed, "hub:" + area.id),
      kind: "hub",
      name: area.name + " Hub",
      nameKo: area.nameKo + " 허브",
      ...position,
      areaId: area.id,
      biomeId: area.biomeId,
      campaignRole: area.campaignRole,
      regionAxis: area.regionAxis,
      orderHint: index,
      clearingRadius: 3
    };
  });

  const rolePois = CAMPAIGN_REGION_ROLES.map((role, roleIndex) => {
    const area = areas.find((candidate) => candidate.campaignRole === role.id);
    const preferred = {
      x: area.anchor.x + (roleIndex % 2 === 0 ? 5 : -5),
      y: area.anchor.y + (roleIndex % 3 === 0 ? 5 : -5)
    };
    const position = findUniqueAreaPosition(area, preferred, areaMap, width, height, reserved, 2, 5);
    reserved.add(positionKey(position));
    const biome = BIOME_DESCRIPTORS.find((item) => item.id === area.biomeId);
    const names = role.landmarkNames;
    const landmarkName = names[seed32(worldSeed, "landmark:" + role.id) % names.length];
    return {
      id: "poi." + role.id.toLowerCase(),
      kind: role.id === FINALE_ROLE ? "finale" : "campaign",
      name: landmarkName,
      nameKo: landmarkName,
      ...position,
      areaId: area.id,
      biomeId: area.biomeId,
      campaignRole: role.id,
      regionAxis: role.regionAxis,
      phase: role.phase,
      visualIntent: biome.visualIntent + "; landmark role " + role.id,
      clearingRadius: 4,
      encounterSpace: { width: 9, height: 9 }
    };
  });

  const biomePois = BIOME_DESCRIPTORS.map((biome, biomeIndex) => {
    const matching = areas.filter((area) => area.biomeId === biome.id);
    const area = matching.find((candidate) => !candidate.campaignRole) || matching[biomeIndex % matching.length];
    const preferred = {
      x: area.anchor.x + (biomeIndex % 2 === 0 ? -6 : 6),
      y: area.anchor.y + (biomeIndex % 3 === 0 ? -6 : 6)
    };
    const position = findUniqueAreaPosition(area, preferred, areaMap, width, height, reserved, 2, 5);
    reserved.add(positionKey(position));
    return {
      id: "poi.biome." + biome.id,
      kind: "biome_landmark",
      name: biome.name + " Landmark",
      nameKo: biome.nameKo + " 표식지",
      ...position,
      areaId: area.id,
      biomeId: biome.id,
      campaignRole: area.campaignRole,
      regionAxis: area.regionAxis,
      visualIntent: biome.visualIntent + "; navigable biome landmark",
      clearingRadius: 4,
      encounterSpace: { width: 9, height: 9 }
    };
  });

  return [entry, ...hubs, ...rolePois, ...biomePois];
}

function terrainForBiome(noiseSeed, x, y, biomeId) {
  const form = valueNoise(noiseSeed, x, y, 11, 701) * 0.65 + valueNoise(noiseSeed, x, y, 23, 709) * 0.35;
  const wet = valueNoise(noiseSeed, x, y, 13, 811) * 0.70 + valueNoise(noiseSeed, x, y, 29, 821) * 0.30;
  const danger = valueNoise(noiseSeed, x, y, 9, 907) * 0.60 + valueNoise(noiseSeed, x, y, 21, 911) * 0.40;
  const relic = valueNoise(noiseSeed, x, y, 15, 1009) * 0.65 + valueNoise(noiseSeed, x, y, 31, 1013) * 0.35;

  switch (biomeId) {
    case "temperate_forest_field":
      if (form > 0.53) return TILE.WALL;
      if (wet > 0.66) return TILE.WATER;
      if (relic > 0.73) return TILE.RUIN;
      if (danger > 0.76) return TILE.HAZARD;
      return TILE.GRASS;
    case "river_wetland":
      if (wet > 0.30) return TILE.WATER;
      if (form > 0.68) return TILE.WALL;
      if (danger > 0.56) return TILE.HAZARD;
      if (relic > 0.78) return TILE.RUIN;
      return TILE.GRASS;
    case "arid_desert":
      if (danger > 0.38) return TILE.HAZARD;
      if (form > 0.66) return TILE.WALL;
      if (relic > 0.51) return TILE.RUIN;
      if (wet > 0.83) return TILE.WATER;
      return TILE.GRASS;
    case "frost_highland":
      if (form > 0.40) return TILE.WALL;
      if (danger > 0.51) return TILE.HAZARD;
      if (wet > 0.67) return TILE.WATER;
      if (relic > 0.76) return TILE.RUIN;
      return TILE.GRASS;
    case "subterranean_cavern":
      if (form > 0.28) return TILE.WALL;
      if (wet > 0.58) return TILE.WATER;
      if (relic > 0.47) return TILE.RUIN;
      if (danger > 0.55) return TILE.HAZARD;
      return TILE.GRASS;
    case "ancient_ruins":
      if (relic > 0.20) return TILE.RUIN;
      if (form > 0.51) return TILE.WALL;
      if (danger > 0.62) return TILE.HAZARD;
      if (wet > 0.79) return TILE.WATER;
      return TILE.GRASS;
    default:
      return TILE.GRASS;
  }
}

function createClusteredTerrain(width, height, biomeMap, noiseSeed) {
  const tiles = new Array(width * height);
  for (let y = 0; y < height; y += 1) {
    for (let x = 0; x < width; x += 1) {
      const tileIndex = indexOf(width, x, y);
      const border = x === 0 || y === 0 || x === width - 1 || y === height - 1;
      tiles[tileIndex] = border
        ? TILE.WALL
        : terrainForBiome(noiseSeed, x, y, BIOME_DESCRIPTORS[biomeMap[tileIndex]].id);
    }
  }
  return tiles;
}

function carvePath(tiles, width, height, path, roadWidth) {
  const radius = Math.floor(roadWidth / 2);
  for (const point of path) {
    for (let y = point.y - radius; y <= point.y + radius; y += 1) {
      for (let x = point.x - radius; x <= point.x + radius; x += 1) {
        if (x <= 0 || y <= 0 || x >= width - 1 || y >= height - 1) continue;
        tiles[indexOf(width, x, y)] = TILE.ROAD;
      }
    }
  }
}

function carveClearing(tiles, width, height, center, radius) {
  for (let y = center.y - radius; y <= center.y + radius; y += 1) {
    for (let x = center.x - radius; x <= center.x + radius; x += 1) {
      if (x <= 0 || y <= 0 || x >= width - 1 || y >= height - 1) continue;
      const tileIndex = indexOf(width, x, y);
      if (tiles[tileIndex] !== TILE.ROAD) tiles[tileIndex] = TILE.GRASS;
    }
  }
}

function isWalkableTile(tile) { return tile !== TILE.WALL && tile !== TILE.WATER; }

function floodFill(tiles, width, height, start, canVisit = () => true) {
  const visited = new Set();
  if (!start || !canVisit(start) || !isWalkableTile(tiles[indexOf(width, start.x, start.y)])) return visited;
  visited.add(positionKey(start));
  const queue = [start];
  for (let cursor = 0; cursor < queue.length; cursor += 1) {
    const current = queue[cursor];
    for (const [dx, dy] of DIRECTIONS) {
      const x = current.x + dx;
      const y = current.y + dy;
      if (x < 0 || y < 0 || x >= width || y >= height) continue;
      const key = String(x) + "," + String(y);
      if (visited.has(key) || !canVisit({ x, y }) || !isWalkableTile(tiles[indexOf(width, x, y)])) continue;
      visited.add(key);
      queue.push({ x, y });
    }
  }
  return visited;
}

function hasWalkableClearance(tiles, width, height, position, radius) {
  for (let y = position.y - radius; y <= position.y + radius; y += 1) {
    for (let x = position.x - radius; x <= position.x + radius; x += 1) {
      if (x <= 0 || y <= 0 || x >= width - 1 || y >= height - 1) return false;
      if (!isWalkableTile(tiles[indexOf(width, x, y)])) return false;
    }
  }
  return true;
}

function nearestSemanticSlot({ area, preferred, areaMap, tiles, width, height, connected, reserved, points, clearanceRadius }) {
  let best = null;
  for (let y = area.bounds.y; y < area.bounds.y + area.bounds.height; y += 1) {
    for (let x = area.bounds.x; x < area.bounds.x + area.bounds.width; x += 1) {
      if (x <= 0 || y <= 0 || x >= width - 1 || y >= height - 1) continue;
      const tileIndex = indexOf(width, x, y);
      if (areaMap[tileIndex] !== area.index || !connected.has(String(x) + "," + String(y))) continue;
      const candidate = { x, y };
      if (reserved.has(positionKey(candidate))) continue;
      if (points.some((point) => manhattan(point, candidate) < Math.max(2, clearanceRadius + 1))) continue;
      if (!hasWalkableClearance(tiles, width, height, candidate, clearanceRadius)) continue;
      const score = manhattan(preferred, candidate);
      if (!best || score < best.score || (score === best.score && (y < best.y || (y === best.y && x < best.x)))) best = { x, y, score };
    }
  }
  return best ? { x: best.x, y: best.y } : null;
}

function slotVisualIntents(area, kind) {
  const biome = BIOME_DESCRIPTORS.find((item) => item.id === area.biomeId);
  const roleIntent = area.campaignRole ? "supports campaign role " + area.campaignRole : "supports optional exploration";
  return [biome.visualIntent, kind + " placement with readable approach", roleIntent];
}

function findFinaleClusterCenter({ area, areaMap, tiles, width, height, connected, reserved, points }) {
  let best = null;
  for (let y = area.bounds.y + 3; y < area.bounds.y + area.bounds.height - 3; y += 1) {
    for (let x = area.bounds.x + 3; x < area.bounds.x + area.bounds.width - 3; x += 1) {
      const center = { x, y };
      const componentPositions = FINALE_CLUSTER_OFFSETS.map((offset) => ({ x: x + offset.x, y: y + offset.y }));
      const staging = { x: x + 1, y };
      const available = [...componentPositions, staging].every((position) => {
        const key = positionKey(position);
        return areaMap[indexOf(width, position.x, position.y)] === area.index
          && connected.has(key)
          && !reserved.has(key)
          && points.every((point) => manhattan(point, position) >= 2)
          && isWalkableTile(tiles[indexOf(width, position.x, position.y)])
          && (position === staging || hasWalkableClearance(tiles, width, height, position, 1));
      });
      if (!available || !hasWalkableClearance(tiles, width, height, center, 2)) continue;
      const score = manhattan(center, area.anchor);
      if (!best || score < best.score || (score === best.score && (y < best.center.y || (y === best.center.y && x < best.center.x)))) {
        best = { center, staging, componentPositions, score };
      }
    }
  }
  return best;
}

function createPlacementSlots({ worldSeed, areas, points, areaMap, tiles, width, height, entry, repairs }) {
  const assets = {
    npc: [
      "npc.villager.green.v1", "npc.villager2.v1", "npc.villager3.v1", "npc.villager4.v1",
      "npc.villager5.v1", "npc.villager6.v1", "npc.old-man.v1", "npc.noble.v1",
      "npc.princess.v1", "npc.samurai.v1"
    ],
    prop: ["item.crate.v1", "item.rune-book.v1", "prop.lantern.v1"],
    quest: ["prop.sign.v1", "prop.altar.v1"],
    enemy: [
      "enemy.slime.blue.v1", "enemy.slime.green.v1", "enemy.mushroom.v1", "enemy.blue-bat.v1",
      "enemy.bear.v1", "enemy.cyclope.v1", "enemy.dragon.v1", "enemy.kappa-green.v1",
      "enemy.snake.v1", "enemy.spider-red.v1"
    ],
    loot: ["item.focus-shard.v1", "item.field-ration.v1"]
  };
  const finaleArea = areas.find((item) => item.campaignRole === FINALE_ROLE);
  let connected = floodFill(tiles, width, height, entry);
  let connectedBeforeFinale = floodFill(tiles, width, height, entry,
    (point) => areaMap[indexOf(width, point.x, point.y)] !== finaleArea.index);
  const reserved = new Set(points.map(positionKey));
  const slots = [];

  const addSlot = ({ area, kind, label, tags, allowedAssetIds, reservedFor = null, offset = 0, clearanceRadius = 1, acquisitionModes = [], actionContext = null }) => {
    const preferred = {
      x: clamp(area.anchor.x + ((offset * 7) % 17) - 8, 2, width - 3),
      y: clamp(area.anchor.y + ((offset * 11) % 17) - 8, 2, height - 3)
    };
    let position = nearestSemanticSlot({
      area,
      preferred,
      areaMap,
      tiles,
      width,
      height,
      connected: area.id === finaleArea.id ? connected : connectedBeforeFinale,
      reserved,
      points,
      clearanceRadius
    });
    if (!position) {
      position = findUniqueAreaPosition(area, preferred, areaMap, width, height, reserved, 1);
      const connector = area.id === finaleArea.id
        ? fourConnectedLine(area.anchor, position, true)
        : shortestPathAvoidingArea(area.anchor, position, width, height, areaMap, finaleArea.index, 1,
          seed32(worldSeed, "slot-repair:" + area.id + ":" + label));
      carvePath(tiles, width, height, connector, 3);
      carveClearing(tiles, width, height, position, Math.max(2, clearanceRadius));
      connected = floodFill(tiles, width, height, entry);
      connectedBeforeFinale = floodFill(tiles, width, height, entry,
        (point) => areaMap[indexOf(width, point.x, point.y)] !== finaleArea.index);
      repairs.push({ type: "slot_clearance", areaId: area.id, label, position: { ...position } });
    }
    reserved.add(positionKey(position));
    const roleTag = area.campaignRole ? area.campaignRole.toLowerCase() : "support_area";
    slots.push({
      id: "slot." + stableId(worldSeed, area.id + ":" + label),
      areaId: area.id,
      biomeId: area.biomeId,
      campaignRole: area.campaignRole,
      regionAxis: area.regionAxis,
      kind,
      purpose: reservedFor ? "campaign_candidate" : "ambient",
      reservedFor,
      tags: [...tags, area.biomeId, roleTag],
      x: position.x,
      y: position.y,
      allowedAssetIds: [...allowedAssetIds],
      clearanceRadius,
      reachability: "entry_component",
      reachable: true,
      visualIntents: slotVisualIntents(area, kind),
      acquisitionModes: [...acquisitionModes],
      actionContext
    });
  };

  const regularKinds = [
    { kind: "npc", clearanceRadius: 1 },
    { kind: "prop", clearanceRadius: 1 },
    { kind: "enemy", clearanceRadius: 2 },
    { kind: "quest", clearanceRadius: 1 },
    { kind: "loot", clearanceRadius: 1 }
  ];
  for (const [areaIndex, area] of areas.entries()) {
    for (const [kindIndex, descriptor] of regularKinds.entries()) {
      addSlot({
        area,
        kind: descriptor.kind,
        label: descriptor.kind + ":" + String(kindIndex),
        tags: [descriptor.kind, "connected", "immutable", descriptor.kind === "enemy" ? "encounter_space" : "semantic_slot"],
        allowedAssetIds: assets[descriptor.kind],
        offset: areaIndex * regularKinds.length + kindIndex,
        clearanceRadius: descriptor.clearanceRadius
      });
    }
  }

  const promoteSupportSlot = (campaignRole, kind, reservedFor, acquisitionModes, actionContext = null) => {
    const area = areas.find((item) => item.campaignRole === campaignRole);
    const slot = slots.find((item) => item.areaId === area.id && item.kind === kind && item.purpose === "ambient");
    assert(slot, 500, "WORLD_RECOVERY_SLOT_MISSING", "A generated progression recovery slot is missing.");
    slot.purpose = "campaign_candidate";
    slot.reservedFor = reservedFor;
    slot.tags.push("progression_candidate", "recovery_candidate");
    if (reservedFor.startsWith("ADMIN_ACCESS_LEVEL_")) slot.tags.push("admin_access_candidate");
    slot.acquisitionModes = [...acquisitionModes];
    slot.actionContext = actionContext;
  };
  promoteSupportSlot(ARRIVAL_ROLE, "npc", "ARRIVAL_GUIDE", ["connect"]);
  const preRootRoles = CAMPAIGN_REGION_ROLES.filter((role) => role.regionAxis !== ROOT_SYSTEM).map((role) => role.id);
  const alternateCandidate = (level, primaryRole, kind, paths) => {
    const roles = preRootRoles.filter((role) => role !== primaryRole);
    const index = seed32(worldSeed, `admin-access:${level}`) % roles.length;
    const path = paths[index % paths.length];
    promoteSupportSlot(roles[index], kind, `ADMIN_ACCESS_LEVEL_${level}`, [path.skillId.toLowerCase()], path.actionContext);
  };
  alternateCandidate(1, "LOCAL_STAKES", "loot", [
    { skillId: "RESTORE", actionContext: "DEPLOYMENT" },
    { skillId: "DELETE", actionContext: "COMBAT" },
    { skillId: "CONNECT", actionContext: "NEGOTIATION" },
    { skillId: "DELETE", actionContext: "COMBAT" }
  ]);
  alternateCandidate(2, "RELATIONSHIP_CONFLICT", "quest", [
    { skillId: "DELETE", actionContext: "COMBAT" },
    { skillId: "COPY", actionContext: "INVESTIGATION" },
    { skillId: "RESTORE", actionContext: "DEPLOYMENT" },
    { skillId: "COPY", actionContext: "INVESTIGATION" }
  ]);
  promoteSupportSlot("HIDDEN_TRUTH", "npc", "STORY_REVELATION", ["connect"]);
  alternateCandidate(3, "CONSEQUENCE_RETURN", "enemy", [
    { skillId: "CONNECT", actionContext: "INVESTIGATION" },
    { skillId: "DELETE", actionContext: "COMBAT" },
    { skillId: "COPY", actionContext: "NEGOTIATION" },
    { skillId: "DELETE", actionContext: "COMBAT" }
  ]);

  const primaryAccessDefinitions = [
    { reservedFor: "ADMIN_ACCESS_LEVEL_1", campaignRole: "LOCAL_STAKES", modes: ["copy"], actionContext: "INVESTIGATION" },
    { reservedFor: "ADMIN_ACCESS_LEVEL_2", campaignRole: "RELATIONSHIP_CONFLICT", modes: ["connect"], actionContext: "NEGOTIATION" },
    { reservedFor: "ADMIN_ACCESS_LEVEL_3", campaignRole: "CONSEQUENCE_RETURN", modes: ["restore"], actionContext: "DEPLOYMENT" }
  ];
  for (const [index, definition] of primaryAccessDefinitions.entries()) {
    const area = areas.find((item) => item.campaignRole === definition.campaignRole);
    addSlot({
      area,
      kind: "quest",
      label: "admin-access:" + definition.reservedFor,
      tags: ["primary_admin_access_candidate", "admin_access_candidate", "read_only_candidate", "required_slot"],
      allowedAssetIds: ["prop.sign.v1", "prop.altar.v1"],
      reservedFor: definition.reservedFor,
      offset: 80 + index,
      clearanceRadius: 2,
      acquisitionModes: definition.modes,
      actionContext: definition.actionContext
    });
  }

  const truthArea = areas.find((item) => item.campaignRole === "HIDDEN_TRUTH");
  for (let index = 0; index < 2; index += 1) {
    addSlot({
      area: truthArea,
      kind: "quest",
      label: "revelation:" + String(index),
      tags: ["revelation_candidate", "read_only_candidate", "required_slot"],
      allowedAssetIds: ["item.rune-book.v1", "prop.altar.v1"],
      reservedFor: "STORY_REVELATION",
      offset: 90 + index,
      clearanceRadius: 2,
      acquisitionModes: ["copy"]
    });
  }

  let finaleCluster = findFinaleClusterCenter({ area: finaleArea, areaMap, tiles, width, height, connected, reserved, points });
  if (!finaleCluster) {
    carveClearing(tiles, width, height, finaleArea.anchor, 6);
    connected = floodFill(tiles, width, height, entry);
    repairs.push({ type: "finale_cluster_clearance", areaId: finaleArea.id, position: { ...finaleArea.anchor } });
    finaleCluster = findFinaleClusterCenter({ area: finaleArea, areaMap, tiles, width, height, connected, reserved, points });
  }
  assert(finaleCluster, 500, "FINALE_CLUSTER_PLACEMENT_FAILED", "No semantic finale cluster fits the generated finale area.");
  for (const [index, reservedFor] of FINALE_COMPONENTS.entries()) {
    const position = finaleCluster.componentPositions[index];
    reserved.add(positionKey(position));
    slots.push({
      id: "slot." + stableId(worldSeed, finaleArea.id + ":finale:" + reservedFor),
      areaId: finaleArea.id,
      biomeId: finaleArea.biomeId,
      campaignRole: finaleArea.campaignRole,
      kind: "quest",
      purpose: "campaign_candidate",
      reservedFor,
      tags: ["finale_candidate", "read_only_candidate", "ending_safe", "gated", "component_" + reservedFor.toLowerCase(), finaleArea.biomeId, finaleArea.campaignRole.toLowerCase()],
      x: position.x,
      y: position.y,
      allowedAssetIds: ["prop.altar.v1", "prop.sign.v1", "item.rune-book.v1"],
      gated: true,
      requiresProgressLevel: 3,
      requiresProgressTokens: [...ADMIN_ACCESS_TOKENS],
      requiresCanonicalFact: { subject: "collapse_origin", predicate: "inside_admin_control_system", value: true },
      interactionAnchor: { ...finaleCluster.staging },
      clearanceRadius: 1,
      reachability: "entry_component_after_finale_gate",
      reachable: true,
      visualIntents: slotVisualIntents(finaleArea, "finale_component"),
      acquisitionModes: ["connect", ...( ["FINAL_FREEDOM", "FINAL_THREAT"].includes(reservedFor) ? ["delete"] : [])]
    });
  }
  return { placementSlots: slots, connected };
}

function bindProgressionCandidates(progressionGraph, placementSlots) {
  const selectors = {
    arrival: (slot) => slot.reservedFor === "ARRIVAL_GUIDE",
    stakes: (slot) => slot.reservedFor === "ADMIN_ACCESS_LEVEL_1",
    bonds: (slot) => slot.reservedFor === "ADMIN_ACCESS_LEVEL_2",
    truth: (slot) => slot.reservedFor === "STORY_REVELATION",
    consequence: (slot) => slot.reservedFor === "ADMIN_ACCESS_LEVEL_3",
    finale: (slot) => slot.tags.includes("finale_candidate")
  };
  for (const node of progressionGraph.nodes) {
    const candidates = placementSlots.filter((slot) => selectors[node.id](slot, node)).sort((left, right) => left.id.localeCompare(right.id));
    const seededAcquisitionModes = [...new Set(candidates.flatMap((slot) => slot.acquisitionModes || []))];
    node.candidateSlotIds = candidates.map((slot) => slot.id);
    node.candidateAcquisitionPaths = candidates.map((slot) => ({
      slotId: slot.id,
      areaId: slot.areaId,
      regionAxis: slot.regionAxis,
      acquisitionModes: [...slot.acquisitionModes],
      actionContext: slot.actionContext || null
    }));
    node.acquisitionModes = seededAcquisitionModes;
  }
}

function createAdminAccessCandidates(worldSeed, areas, placementSlots) {
  const candidates = placementSlots
    .filter((slot) => slot.tags.includes("admin_access_candidate"))
    .map((slot) => {
      const area = areas.find((item) => item.id === slot.areaId);
      const skillId = String(slot.acquisitionModes[0] || "").toUpperCase();
      assert(KEYBOARD_SKILLS.includes(skillId), 500, "ADMIN_ACCESS_CANDIDATE_SKILL_INVALID", "Admin access candidates require a keyboard skill.");
      assert(CAMPAIGN_ACTION_CONTEXTS.includes(slot.actionContext), 500, "ADMIN_ACCESS_CANDIDATE_CONTEXT_INVALID", "Admin access candidates require an authoritative action context.");
      assert(CAMPAIGN_REGION_AXES.includes(area.regionAxis) && area.regionAxis !== ROOT_SYSTEM, 500, "ADMIN_ACCESS_CANDIDATE_AXIS_INVALID", "Admin access candidates must remain outside Root System.");
      return {
        id: `admin-candidate.${stableId(worldSeed, slot.id)}`,
        accessLevelId: slot.reservedFor,
        slotId: slot.id,
        areaId: slot.areaId,
        regionAxis: area.regionAxis,
        terrainBiomeId: slot.biomeId,
        skillId,
        actionContext: slot.actionContext
      };
    })
    .sort((left, right) => left.accessLevelId.localeCompare(right.accessLevelId) || left.id.localeCompare(right.id));
  return validateAdminAccessCandidates(candidates);
}

function pointClearingIsValid(tiles, width, height, point) {
  return Number.isInteger(point.clearingRadius) && point.clearingRadius >= 4
    && hasWalkableClearance(tiles, width, height, point, point.clearingRadius);
}

function validateRoutePaths(world) {
  const finaleArea = world.areas.find((area) => area.campaignRole === FINALE_ROLE);
  for (const route of world.routes) {
    assert(route.path.length > 0, 500, "WORLD_ROUTE_EMPTY", "Every generated route requires a concrete path.");
    for (let index = 1; index < route.path.length; index += 1) {
      assert(manhattan(route.path[index - 1], route.path[index]) === 1, 500, "WORLD_ROUTE_DISCONNECTED", "Route paths must be four-way contiguous.");
    }
    const fromArea = world.areas.find((area) => area.id === route.fromAreaId);
    const toArea = world.areas.find((area) => area.id === route.toAreaId);
    assert(positionKey(route.path[0]) === positionKey(fromArea.anchor) && positionKey(route.path[route.path.length - 1]) === positionKey(toArea.anchor), 500, "WORLD_ROUTE_ENDPOINT_INVALID", "Route paths must join their declared area anchors.");
    assert(positionKey(route.from) === positionKey(fromArea.anchor) && positionKey(route.to) === positionKey(toArea.anchor), 500, "WORLD_ROUTE_ENDPOINT_CONTRACT_INVALID", "Route endpoint fields must expose their immutable area anchors.");
    const validWidth = route.kind === "major"
      ? route.width === 3 || route.width === 5
      : route.kind === "minor"
        ? route.width === 3
        : route.kind === "secret" && route.width === 1;
    assert(validWidth, 500, "WORLD_ROUTE_WIDTH_INVALID", "Route width must match its route class.");
    const radius = Math.floor(route.width / 2);
    for (const point of route.path) {
      for (let y = point.y - radius; y <= point.y + radius; y += 1) {
        for (let x = point.x - radius; x <= point.x + radius; x += 1) {
          assert(x > 0 && y > 0 && x < world.width - 1 && y < world.height - 1
            && world.tiles[indexOf(world.width, x, y)] === TILE.ROAD,
          500, "WORLD_ROUTE_WIDTH_RASTER_INVALID", "A declared route width is not present in the immutable tile layer.");
          if (!route.gated) {
            assert(world.areaMap[indexOf(world.width, x, y)] !== finaleArea.index,
              500, "WORLD_ROUTE_CROSSES_FINALE", "A pre-finale route may not carve through the gated finale area.");
          }
        }
      }
    }
  }
}

export function validateGeneratedWorld(world, { turnLimit = 40 } = {}) {
  assert(Number.isInteger(world.width) && Number.isInteger(world.height)
    && world.width >= 120 && world.width <= 256 && world.height >= 120 && world.height <= 256,
  500, "WORLD_SIZE_INVALID", "World dimensions must be integer values between 120 and 256.");
  assert(world.tiles.length === world.width * world.height, 500, "WORLD_TILE_COUNT_INVALID", "Tile data must fill the generated world.");
  assert(world.areaMap.length === world.tiles.length && world.biomeMap.length === world.tiles.length, 500, "WORLD_LAYER_COUNT_INVALID", "Area and biome layers must cover every tile.");
  assert(world.areas.length === AREA_COUNT, 500, "WORLD_AREA_COUNT_INVALID", "The generated world requires exactly twelve areas.");
  assert(world.worldId === WORLD_CODRIA && world.worldNameKo === WORLD_NAME_KO, 500, "WORLD_IDENTITY_INVALID", "Every generated run must remain in Codria.");
  assert(Array.isArray(world.regionAxes) && world.regionAxes.length === CAMPAIGN_REGION_AXES.length
    && CAMPAIGN_REGION_AXES.every((axis) => world.regionAxes.includes(axis)), 500, "WORLD_REGION_AXES_INVALID", "The six Codria region axes are required.");

  const biomeAreaCounts = new Map(BIOME_DESCRIPTORS.map((biome) => [biome.id, 0]));
  for (const area of world.areas) biomeAreaCounts.set(area.biomeId, (biomeAreaCounts.get(area.biomeId) || 0) + 1);
  assert([...biomeAreaCounts.values()].every((count) => count === 2), 500, "WORLD_BIOMES_INCOMPLETE", "Each of the six terrain biomes must own exactly two anchors.");
  assert(new Set(world.biomeMap).size === BIOME_DESCRIPTORS.length, 500, "WORLD_BIOME_LAYER_INCOMPLETE", "Every terrain biome must appear in the tile biome layer.");

  const assignedRoles = world.areas.map((area) => area.campaignRole).filter(Boolean);
  assert(assignedRoles.length === CAMPAIGN_REGION_ROLES.length && new Set(assignedRoles).size === CAMPAIGN_REGION_ROLES.length, 500, "WORLD_CAMPAIGN_ROLES_INVALID", "Each campaign role must be assigned exactly once to a distinct area.");
  assert(CAMPAIGN_REGION_ROLES.every((role) => assignedRoles.includes(role.id)), 500, "WORLD_CAMPAIGN_ROLES_INCOMPLETE", "All six campaign roles must be represented.");
  const assignedAxes = world.areas.map((area) => area.regionAxis).filter(Boolean);
  assert(assignedAxes.length === CAMPAIGN_REGION_AXES.length && new Set(assignedAxes).size === CAMPAIGN_REGION_AXES.length
    && CAMPAIGN_REGION_AXES.every((axis) => assignedAxes.includes(axis)), 500, "WORLD_REGION_AXIS_BINDINGS_INVALID", "Each Codria region axis must bind to one generated area.");
  validateAdminAccessCandidates(world.adminAccessCandidates);

  const topological = topologicalOrder(world.progressionGraph);
  assert(topological && topological[0] === "arrival" && topological[topological.length - 1] === "finale", 500, "WORLD_PROGRESSION_INVALID", "The run progression scaffold must be acyclic from arrival to finale.");
  const slotById = new Map(world.placementSlots.map((slot) => [slot.id, slot]));
  const slotIds = new Set(slotById.keys());
  assert(world.progressionGraph.nodes.every((node) => Array.isArray(node.candidateSlotIds)
    && node.candidateSlotIds.length > 0
    && new Set(node.candidateSlotIds).size === node.candidateSlotIds.length
    && node.candidateSlotIds.every((slotId) => slotIds.has(slotId))
    && Array.isArray(node.acquisitionModes)
    && new Set(node.acquisitionModes).size >= 1), 500, "WORLD_PROGRESSION_CANDIDATES_INVALID", "Every progression node must bind generated candidate slots and at least one executable acquisition mode.");
  assert(world.progressionGraph.nodes.every((node) => {
    if (!Array.isArray(node.candidateAcquisitionPaths)
      || node.candidateAcquisitionPaths.length !== node.candidateSlotIds.length) return false;
    const pathSlotIds = node.candidateAcquisitionPaths.map((path) => path?.slotId);
    if (new Set(pathSlotIds).size !== node.candidateSlotIds.length
      || !node.candidateSlotIds.every((slotId) => pathSlotIds.includes(slotId))) return false;
    if (!node.candidateAcquisitionPaths.every((path) => {
      const slot = slotById.get(path.slotId);
      return slot
        && Array.isArray(path.acquisitionModes)
        && path.acquisitionModes.length > 0
        && path.acquisitionModes.every((mode) => SUPPORTED_ACQUISITION_MODES.has(mode))
        && sameUniqueStringSet(path.acquisitionModes, slot.acquisitionModes);
    })) return false;
    const pathModes = [...new Set(node.candidateAcquisitionPaths.flatMap((path) => path.acquisitionModes))];
    return node.acquisitionModes.every((mode) => SUPPORTED_ACQUISITION_MODES.has(mode))
      && sameUniqueStringSet(node.acquisitionModes, pathModes);
  }),
  500, "WORLD_ACQUISITION_CONTRACT_INVALID", "Progression candidates must expose executable turn actions only.");
  assert(world.progressionGraph.nodes.every((node) => {
    if (node.rewardProgressToken?.startsWith("ADMIN_ACCESS_LEVEL_")) {
      return node.acquisitionModes.every((mode) => KEYBOARD_SKILLS.includes(mode.toUpperCase()))
        && new Set(node.candidateAcquisitionPaths.map((path) => path.areaId)).size >= 2;
    }
    return sameUniqueStringSet(node.acquisitionModes, CAMPAIGN_ALLOWED_ABILITIES_BY_ROLE[node.campaignRole]);
  }), 500, "WORLD_CAMPAIGN_ABILITY_ALIGNMENT_INVALID", "Generated progression paths must match the Codria action contract.");
  assert(world.progressionGraph.finalGate.requiresProgressLevel === 3
    && ADMIN_ACCESS_TOKENS.every((token) => world.progressionGraph.finalGate.requiresProgressTokens.includes(token))
    && world.progressionGraph.finalGate.requiresCanonicalFact?.subject === "collapse_origin"
    && world.progressionGraph.finalGate.requiresCanonicalFact?.predicate === "inside_admin_control_system"
    && world.progressionGraph.finalGate.requiresCanonicalFact?.value === true,
  500, "WORLD_FINALE_GATE_INVALID", "Root System progression requires all three administrator access levels and the internal-collapse clue.");

  validateRoutePaths(world);
  assert(world.routes.filter((route) => route.isLoop).length >= 2, 500, "WORLD_ROUTE_LOOPS_INCOMPLETE", "The area graph requires at least two deterministic loop routes.");
  const finaleArea = world.areas.find((area) => area.campaignRole === FINALE_ROLE);
  const arrivalArea = world.areas.find((area) => area.campaignRole === ARRIVAL_ROLE);
  const finaleRoutes = world.routes.filter((route) => route.fromAreaId === finaleArea.id || route.toAreaId === finaleArea.id);
  assert(finaleRoutes.length >= 1 && finaleRoutes.every((route) => route.gated && route.requiresProgressLevel === 3
    && ADMIN_ACCESS_TOKENS.every((token) => route.requiresProgressTokens.includes(token))
    && route.requiresCanonicalFact?.subject === "collapse_origin"
    && route.requiresCanonicalFact?.predicate === "inside_admin_control_system"
    && route.requiresCanonicalFact?.value === true), 500, "WORLD_FINALE_ROUTES_UNGATED", "Every route incident to Root System must carry the full logical gate.");
  const beforeGate = routeReachableAreaIds(world.routes, arrivalArea.id, { progressLevel: 0, progressTokens: [] });
  const afterGate = routeReachableAreaIds(world.routes, arrivalArea.id, { progressLevel: 3, progressTokens: [...ADMIN_ACCESS_TOKENS] });
  assert(!beforeGate.has(finaleArea.id), 500, "WORLD_FINALE_REACHABLE_EARLY", "Root System must be unreachable before all administrator access levels and the essential clue are available.");
  assert(world.areas.filter((area) => area.id !== finaleArea.id).every((area) => beforeGate.has(area.id)), 500, "WORLD_PRE_FINALE_GRAPH_DISCONNECTED", "Every pre-finale area must remain route-reachable before the finale gate.");
  assert(afterGate.size === world.areas.length, 500, "WORLD_POST_FINALE_GRAPH_DISCONNECTED", "All areas, including Root System, must be route-reachable after the gate contract is satisfied.");

  for (const area of world.areas) {
    assert(world.areaMap[indexOf(world.width, area.anchor.x, area.anchor.y)] === area.index, 500, "WORLD_AREA_ANCHOR_INVALID", "Each area anchor must remain inside its own distance-field region.");
  }

  const entry = world.points.find((point) => point.id === "entry");
  const connected = floodFill(world.tiles, world.width, world.height, entry);
  const connectedBeforeFinale = floodFill(world.tiles, world.width, world.height, entry,
    (point) => world.areaMap[indexOf(world.width, point.x, point.y)] !== finaleArea.index);
  const preFinalePointsAndSlots = [...world.points, ...world.placementSlots]
    .filter((item) => item.areaId !== finaleArea.id);
  const unreachableBeforeFinale = [
    ...world.areas.filter((area) => area.id !== finaleArea.id && !connectedBeforeFinale.has(positionKey(area.anchor))).map((area) => area.id),
    ...preFinalePointsAndSlots.filter((point) => !connectedBeforeFinale.has(positionKey(point))).map((point) => point.id)
  ];
  assert(unreachableBeforeFinale.length === 0, 500, "WORLD_PRE_FINALE_TILE_CONNECTIVITY_INVALID",
    "Every pre-finale area, POI, and slot must be tile-reachable without crossing the gated finale area: " + unreachableBeforeFinale.join(", "));
  const unreachablePoints = world.points.filter((point) => !connected.has(positionKey(point)));
  const unreachableSlots = world.placementSlots.filter((slot) => !connected.has(positionKey(slot)));
  assert(unreachablePoints.length === 0 && unreachableSlots.length === 0, 500, "WORLD_CONNECTIVITY_INVALID", "All POIs and slots must share the entry component.");
  assert(world.points.every((point) => world.areas[world.areaMap[indexOf(world.width, point.x, point.y)]].id === point.areaId), 500, "WORLD_POI_AREA_MISMATCH", "Every POI must remain inside its declared distance-field area.");
  assert(world.placementSlots.every((slot) => world.areas[world.areaMap[indexOf(world.width, slot.x, slot.y)]].id === slot.areaId), 500, "WORLD_SLOT_AREA_MISMATCH", "Every semantic slot must remain inside its declared distance-field area.");
  const occupied = [...world.points, ...world.placementSlots].map(positionKey);
  assert(new Set(occupied).size === occupied.length, 500, "WORLD_POSITION_DUPLICATE", "POIs and slots must reserve unique coordinates.");

  for (const slot of world.placementSlots) {
    assert(slot.reachable === true && slot.reachability, 500, "WORLD_SLOT_REACHABILITY_UNDECLARED", "Every semantic slot must declare its reachability contract.");
    assert(Number.isInteger(slot.clearanceRadius) && slot.clearanceRadius >= 1, 500, "WORLD_SLOT_CLEARANCE_INVALID", "Every semantic slot must reserve clearance.");
    assert(Array.isArray(slot.visualIntents) && slot.visualIntents.length > 0, 500, "WORLD_SLOT_VISUAL_INTENT_MISSING", "Every semantic slot must expose bounded visual intents.");
    assert(Array.isArray(slot.allowedAssetIds), 500, "WORLD_SLOT_ASSET_CONTRACT_MISSING", "Every semantic slot must retain an asset allowlist.");
    assert(hasWalkableClearance(world.tiles, world.width, world.height, slot, slot.clearanceRadius), 500, "WORLD_SLOT_CLEARANCE_BLOCKED", "Semantic slot clearance is blocked: " + slot.id + ".");
  }

  const requiredSlots = world.placementSlots.filter((slot) => slot.tags.includes("primary_admin_access_candidate") || slot.tags.includes("revelation_candidate"));
  assert(requiredSlots.every((slot) => Array.isArray(slot.acquisitionModes)
    && slot.acquisitionModes.length >= 1
    && slot.acquisitionModes.every((mode) => SUPPORTED_ACQUISITION_MODES.has(mode))),
  500, "WORLD_ACQUISITION_PATHS_INCOMPLETE", "Every administrator-access and revelation slot must expose an executable acquisition mode.");
  const primaryAccessSlots = world.placementSlots.filter((slot) => slot.tags.includes("primary_admin_access_candidate"));
  const revelations = world.placementSlots.filter((slot) => slot.tags.includes("revelation_candidate"));
  const finale = world.placementSlots.filter((slot) => slot.tags.includes("finale_candidate"));
  assert(primaryAccessSlots.length === 3 && revelations.length >= 2 && finale.length === 7, 500, "WORLD_CANDIDATES_INCOMPLETE", "Exactly three primary administrator-access anchors, redundant revelation anchors, and seven finale components are required.");
  const minimumAccessDistance = Math.floor(Math.min(world.width, world.height) / 10);
  for (let left = 0; left < primaryAccessSlots.length; left += 1) {
    for (let right = left + 1; right < primaryAccessSlots.length; right += 1) {
      assert(manhattan(primaryAccessSlots[left], primaryAccessSlots[right]) >= minimumAccessDistance, 500, "WORLD_ADMIN_ACCESS_DISTANCE_INVALID", "Primary administrator-access anchors must be spatially separated.");
    }
  }

  assert(finale.every((slot) => manhattan(entry, slot) >= Math.floor(world.width / 3)), 500, "FINALE_DISTANCE_INVALID", "Finale components must be spatially separated from entry.");
  const finaleAnchors = new Set(finale.map((slot) => positionKey(slot.interactionAnchor || {})));
  assert(finaleAnchors.size === 1, 500, "FINALE_CLUSTER_INVALID", "All finale components must declare one common interaction anchor.");
  const finaleInteractionAnchor = finale[0].interactionAnchor;
  assert(connected.has(positionKey(finaleInteractionAnchor)) && finale.every((slot) => manhattan(finaleInteractionAnchor, slot) <= 5), 500, "FINALE_CLUSTER_UNREACHABLE", "Every finale recipe target must be reachable from its common interaction tile.");
  assert(finale.filter((slot) => ["FINAL_FREEDOM", "FINAL_THREAT"].includes(slot.reservedFor)).every((slot) => manhattan(finaleInteractionAnchor, slot) <= 3), 500, "FINALE_REMOVAL_RANGE_INVALID", "Every removable finale component must be in Delete range from the common interaction tile.");

  const encounterPois = world.pois.filter((point) => ["campaign", "finale", "biome_landmark"].includes(point.kind));
  assert(encounterPois.every((point) => pointClearingIsValid(world.tiles, world.width, world.height, point)), 500, "WORLD_ENCOUNTER_CLEARANCE_INVALID", "Campaign and biome POIs require at least a nine-by-nine walkable encounter clearing.");
  const biomePoiCoverage = BIOME_DESCRIPTORS.map((biome) => ({
    biomeId: biome.id,
    poiIds: world.pois.filter((point) => point.biomeId === biome.id && connected.has(positionKey(point))).map((point) => point.id)
  }));
  assert(biomePoiCoverage.every((item) => item.poiIds.length > 0), 500, "WORLD_BIOME_POI_INCOMPLETE", "Every biome requires a reachable POI.");
  assert(Number.isInteger(turnLimit) && turnLimit >= 30 && turnLimit <= 50, 500, "TURN_LIMIT_INVALID", "The world contract supports campaign limits from 30 to 50 turns.");

  if (typeof world.layoutHash === "string" && world.layoutHash.length > 0) {
    assert(world.layoutHash === computeWorldLayoutHash(world), 500, "WORLD_LAYOUT_HASH_MISMATCH", "Immutable generated world data no longer matches its layout hash.");
  }

  return {
    connectedTileCount: connected.size,
    pointCount: world.points.length,
    poiCount: world.pois.length,
    areaCount: world.areas.length,
    slotCount: world.placementSlots.length,
    routeCount: world.routes.length,
    loopRouteCount: world.routes.filter((route) => route.isLoop).length,
    candidateCounts: { primaryAdminAccess: primaryAccessSlots.length, adminAccess: world.adminAccessCandidates.length, revelations: revelations.length, finale: finale.length },
    biomePoiCoverage,
    finaleInteractionAnchor: { ...finaleInteractionAnchor },
    finaleMaxInteractionDistance: Math.max(...finale.map((slot) => manhattan(finaleInteractionAnchor, slot))),
    progressionAcyclic: true,
    progressionOrder: topological,
    stageReachability: {
      finaleReachableBeforeGate: beforeGate.has(finaleArea.id),
      preFinaleAreaCountBeforeGate: beforeGate.size,
      preFinaleTileCountBeforeGate: connectedBeforeFinale.size,
      finaleReachableAfterGate: afterGate.has(finaleArea.id),
      areaCountAfterGate: afterGate.size
    },
    encounterMinimum: { width: 9, height: 9 },
    turnWindowContract: [30, 50],
    turnSimulationPerformed: false,
    safeNavigationConsumesCampaignTurn: false
  };
}

export function computeWorldLayoutHash(world) {
  const logical = {
    generatorVersion: world.generatorVersion,
    worldId: world.worldId,
    worldName: world.worldName,
    worldNameKo: world.worldNameKo,
    regionAxes: world.regionAxes,
    worldSeed: world.worldSeed,
    width: world.width,
    height: world.height,
    biomes: world.biomes,
    campaignRegionRoles: world.campaignRegionRoles,
    areas: world.areas,
    routes: world.routes,
    progressionGraph: world.progressionGraph,
    adminAccessCandidates: world.adminAccessCandidates,
    points: world.points,
    pois: world.pois,
    placementSlots: world.placementSlots,
    generationReport: world.generationReport,
    geometryPolicy: world.geometryPolicy,
    llmGeometryAccess: world.llmGeometryAccess,
    validation: world.validation
  };
  return createHash("sha256")
    .update(WORLD_GENERATOR_VERSION + "|" + String(world.worldSeed) + "|" + String(world.width) + "x" + String(world.height) + "|")
    .update(Buffer.from(world.tiles))
    .update(Buffer.from(world.areaMap))
    .update(Buffer.from(world.biomeMap))
    .update(JSON.stringify(logical))
    .digest("hex");
}

export function generateWorld(worldSeed, { width = DEFAULT_WORLD_SIZE.width, height = DEFAULT_WORLD_SIZE.height } = {}) {
  assert(Number.isSafeInteger(worldSeed), 400, "WORLD_SEED_INVALID", "worldSeed must be a safe integer.");
  assert(Number.isInteger(width) && Number.isInteger(height)
    && width >= 120 && width <= 256 && height >= 120 && height <= 256,
  400, "WORLD_SIZE_INVALID", "World dimensions must be integer values between 120 and 256.");
  const random = new StableRandom(seed32(worldSeed, "pipeline"));
  const repairs = [];
  const progressionGraph = createProgressionGraph();
  const areas = createAreaAnchors(worldSeed, width, height, random);
  const layers = createAreaAndBiomeMaps(areas, width, height, seed32(worldSeed, "area-map"));
  updateAreaGeometry(areas, layers.areaMap, width, height);
  for (const node of progressionGraph.nodes) {
    const area = areas.find((candidate) => candidate.campaignRole === node.campaignRole);
    node.areaId = area.id;
  }
  const routes = createRoutes(worldSeed, areas, layers.areaMap, width, height);
  const points = createPoints(worldSeed, areas, layers.areaMap, width, height);
  const tiles = createClusteredTerrain(width, height, layers.biomeMap, seed32(worldSeed, "terrain"));
  const finaleArea = areas.find((candidate) => candidate.campaignRole === FINALE_ROLE);

  for (const route of routes) carvePath(tiles, width, height, route.path, route.width);
  for (const point of points) {
    const area = areas.find((candidate) => candidate.id === point.areaId);
    const connector = area.id === finaleArea.id
      ? fourConnectedLine(area.anchor, point, true)
      : shortestPathAvoidingArea(area.anchor, point, width, height, layers.areaMap, finaleArea.index, 1,
        seed32(worldSeed, "poi:" + point.id));
    carvePath(tiles, width, height, connector, point.kind === "hub" ? 3 : 3);
    carveClearing(tiles, width, height, point, point.clearingRadius || 3);
  }

  const entry = points.find((point) => point.id === "entry");
  let connected = floodFill(tiles, width, height, entry);
  for (const point of points) {
    if (connected.has(positionKey(point))) continue;
    const area = areas.find((candidate) => candidate.id === point.areaId);
    const connector = area.id === finaleArea.id
      ? fourConnectedLine(area.anchor, point, false)
      : shortestPathAvoidingArea(area.anchor, point, width, height, layers.areaMap, finaleArea.index, 1,
        seed32(worldSeed, "poi-repair:" + point.id));
    carvePath(tiles, width, height, connector, 3);
    carveClearing(tiles, width, height, point, point.clearingRadius || 3);
    repairs.push({ type: "poi_connector", pointId: point.id, areaId: point.areaId });
    connected = floodFill(tiles, width, height, entry);
  }

  const slotResult = createPlacementSlots({
    worldSeed,
    areas,
    points,
    areaMap: layers.areaMap,
    tiles,
    width,
    height,
    entry,
    repairs
  });
  bindProgressionCandidates(progressionGraph, slotResult.placementSlots);
  const adminAccessCandidates = createAdminAccessCandidates(worldSeed, areas, slotResult.placementSlots);
  const world = {
    generatorVersion: WORLD_GENERATOR_VERSION,
    worldId: WORLD_CODRIA,
    worldName: "Codria",
    worldNameKo: WORLD_NAME_KO,
    regionAxes: [...CAMPAIGN_REGION_AXES],
    worldSeed,
    width,
    height,
    layoutHash: "",
    tiles,
    areaMap: layers.areaMap,
    biomeMap: layers.biomeMap,
    biomes: BIOME_DESCRIPTORS.map((item) => ({ ...item })),
    campaignRegionRoles: CAMPAIGN_REGION_ROLES.map(({ landmarkNames, ...item }) => ({ ...item, candidateLandmarkNames: [...landmarkNames] })),
    progressionGraph,
    adminAccessCandidates,
    areas,
    routes,
    points,
    pois: points.filter((point) => point.kind !== "hub" && point.kind !== "entry"),
    placementSlots: slotResult.placementSlots,
    geometryPolicy: "generated_once_server_authoritative_immutable",
    llmGeometryAccess: "forbidden",
    generationReport: null,
    validation: null
  };
  const validation = validateGeneratedWorld(world);
  world.generationReport = {
    pipeline: "logical_first_constraint_pipeline",
    pipelineVersion: "1",
    generatorVersion: WORLD_GENERATOR_VERSION,
    status: "valid",
    deterministic: true,
    attempts: 1,
    repairPolicy: {
      mode: "deterministic_local_repair_then_fail",
      affectedRegionRegenerationLimit: 0
    },
    repairs,
    configuration: {
      areaCount: AREA_COUNT,
      biomeAnchorCountPerBiome: 2,
      boundaryNoiseScale: layers.noiseScale,
      boundaryNoiseAmplitude: layers.amplitude,
      terrainNoiseScales: [9, 11, 13, 15, 21, 23, 29, 31],
      majorRoadWidths: [3, 5],
      minorRoadWidth: 3,
      secretRoadWidth: 1,
      poiClearing: { width: 9, height: 9 }
    },
    counts: {
      areas: areas.length,
      routes: routes.length,
      loopRoutes: routes.filter((route) => route.isLoop).length,
      points: points.length,
      pois: world.pois.length,
      slots: world.placementSlots.length
    },
    checks: {
      progressionAcyclic: validation.progressionAcyclic,
      finaleReachableBeforeGate: validation.stageReachability.finaleReachableBeforeGate,
      preFinaleTileCountBeforeGate: validation.stageReachability.preFinaleTileCountBeforeGate,
      finaleReachableAfterGate: validation.stageReachability.finaleReachableAfterGate,
      connectedTileCount: validation.connectedTileCount,
      biomePoiCoverage: validation.biomePoiCoverage.map((item) => ({ biomeId: item.biomeId, count: item.poiIds.length })),
      encounterMinimum: validation.encounterMinimum
    },
    turnSimulation: {
      performed: false,
      reason: "World generation validates immutable geometry and progression gates only."
    }
  };
  world.validation = validation;
  world.layoutHash = computeWorldLayoutHash(world);
  return world;
}

export function isInside(world, point) {
  return Number.isInteger(point?.x) && Number.isInteger(point?.y)
    && point.x >= 0 && point.x < world.width && point.y >= 0 && point.y < world.height;
}

export function tileAt(world, point) {
  return isInside(world, point) ? world.tiles[indexOf(world.width, point.x, point.y)] : TILE.WALL;
}

export function isWalkable(world, point) {
  return isInside(world, point) && isWalkableTile(tileAt(world, point));
}

export function areaAt(world, point) {
  if (isInside(world, point) && Array.isArray(world.areaMap)) {
    const areaIndex = world.areaMap[indexOf(world.width, point.x, point.y)];
    if (Number.isInteger(areaIndex) && world.areas[areaIndex]) return world.areas[areaIndex];
  }
  return world.areas.find((area) => point.x >= area.bounds.x && point.y >= area.bounds.y
    && point.x < area.bounds.x + area.bounds.width && point.y < area.bounds.y + area.bounds.height) || world.areas[0];
}

export function movementCost(world, point) {
  const tile = tileAt(world, point);
  if (tile === TILE.ROAD) return 1;
  if (tile === TILE.HAZARD) return 3;
  if (tile === TILE.RUIN) return 2;
  const biome = BIOME_DESCRIPTORS.find((item) => item.id === areaAt(world, point).biomeId);
  return biome?.offRoadCost || 1;
}

export function publicWorld(world) {
  return {
    generatorVersion: world.generatorVersion,
    worldId: world.worldId,
    worldName: world.worldName,
    worldNameKo: world.worldNameKo,
    regionAxes: world.regionAxes,
    worldSeed: world.worldSeed,
    width: world.width,
    height: world.height,
    layoutHash: world.layoutHash,
    tileLegend: TILE_NAMES,
    tilesRle: encodeRle(world.tiles),
    areaMapLegend: world.areas.map((area) => area.id),
    areaMapRle: encodeRle(world.areaMap),
    biomeMapLegend: world.biomes.map((biome) => biome.id),
    biomeMapRle: encodeRle(world.biomeMap),
    biomes: world.biomes,
    campaignRegionRoles: world.campaignRegionRoles,
    progressionGraph: world.progressionGraph,
    adminAccessCandidates: world.adminAccessCandidates,
    areas: world.areas,
    routes: world.routes,
    points: world.points,
    pois: world.pois,
    placementSlots: world.placementSlots,
    generationReport: world.generationReport,
    geometryPolicy: world.geometryPolicy,
    llmGeometryAccess: world.llmGeometryAccess,
    validation: world.validation
  };
}
