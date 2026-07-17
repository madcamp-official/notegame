#!/usr/bin/env node

import { mkdir, writeFile } from "node:fs/promises";
import { dirname, extname, resolve } from "node:path";
import { generateWorld, publicWorld, TILE_NAMES } from "../src/domain/world.js";

const options = parseArguments(process.argv.slice(2));
const summaries = [];

for (let offset = 0; offset < options.count; offset += 1) {
  const seed = options.seed + offset;
  const world = generateWorld(seed, { width: options.width, height: options.height });
  const summary = summarize(world);
  summaries.push(summary);

  if (!options.summaryOnly && options.output) {
    const basePath = outputBase(options.output, seed, options.count);
    await mkdir(dirname(basePath), { recursive: true });
    await writeFile(`${basePath}.json`, `${JSON.stringify(publicWorld(world), null, 2)}\n`, "utf8");
    await writeFile(`${basePath}.svg`, renderSvg(world), "utf8");
    summary.files = [`${basePath}.json`, `${basePath}.svg`];
  }
}

process.stdout.write(`${JSON.stringify(options.count === 1 ? summaries[0] : {
  seedRange: [options.seed, options.seed + options.count - 1],
  count: options.count,
  allValid: summaries.every((item) => item.valid),
  uniqueLayouts: new Set(summaries.map((item) => item.layoutHash)).size,
  worlds: summaries
}, null, 2)}\n`);

function parseArguments(args) {
  const values = {
    seed: 20260717,
    count: 1,
    width: 160,
    height: 160,
    output: null,
    summaryOnly: false
  };
  for (let index = 0; index < args.length; index += 1) {
    const argument = args[index];
    const next = () => {
      index += 1;
      if (index >= args.length) fail(`Missing value after ${argument}.`);
      return args[index];
    };
    if (argument === "--seed") values.seed = integer(next(), "seed");
    else if (argument === "--count") values.count = integer(next(), "count");
    else if (argument === "--width") values.width = integer(next(), "width");
    else if (argument === "--height") values.height = integer(next(), "height");
    else if (argument === "--output" || argument === "-o") values.output = resolve(next());
    else if (argument === "--summary-only") values.summaryOnly = true;
    else if (argument === "--help" || argument === "-h") usage();
    else fail(`Unknown argument: ${argument}`);
  }
  if (!Number.isSafeInteger(values.seed)) fail("seed must be a safe integer.");
  if (values.count < 1 || values.count > 1000) fail("count must be between 1 and 1000.");
  const finalSeed = BigInt(values.seed) + BigInt(values.count - 1);
  if (finalSeed < BigInt(Number.MIN_SAFE_INTEGER) || finalSeed > BigInt(Number.MAX_SAFE_INTEGER)) {
    fail("the entire seed range must contain only safe integers.");
  }
  return values;
}

function integer(value, name) {
  if (!/^-?\d+$/.test(value)) fail(`${name} must be an integer.`);
  const parsed = Number(value);
  if (!Number.isSafeInteger(parsed)) fail(`${name} must be a safe integer.`);
  return parsed;
}

function outputBase(output, seed, count) {
  const resolved = resolve(output);
  const extension = extname(resolved).toLowerCase();
  const withoutExtension = extension === ".json" || extension === ".svg"
    ? resolved.slice(0, -extension.length)
    : resolved;
  return count === 1 ? withoutExtension : resolve(withoutExtension, `world-${seed}`);
}

function summarize(world) {
  return {
    seed: world.worldSeed,
    generatorVersion: world.generatorVersion,
    dimensions: `${world.width}x${world.height}`,
    layoutHash: world.layoutHash,
    valid: world.generationReport?.status === "valid" || Boolean(world.validation),
    biomes: new Set(world.areas.map((area) => area.biomeId)).size,
    campaignRoles: new Set(world.areas.map((area) => area.campaignRole).filter(Boolean)).size,
    areas: world.areas.length,
    routes: world.routes?.length || 0,
    loopRoutes: world.routes?.filter((route) => route.isLoop || route.loop).length || 0,
    points: world.points.length,
    slots: world.placementSlots.length,
    repairs: world.generationReport?.repairs?.length || 0
  };
}

function renderSvg(world) {
  const colors = {
    grass: "#4e8a4b",
    wall: "#2c2926",
    hazard: "#a64b42",
    road: "#b5965d",
    water: "#36758b",
    ruin: "#776958",
    floor: "#776f62",
    bridge: "#a87842"
  };
  const tileSize = 4;
  const runs = [];
  for (let y = 0; y < world.height; y += 1) {
    let start = 0;
    let current = world.tiles[y * world.width];
    for (let x = 1; x <= world.width; x += 1) {
      const value = x < world.width ? world.tiles[y * world.width + x] : null;
      if (value === current) continue;
      const name = TILE_NAMES[current] || "floor";
      runs.push(`<rect x="${start * tileSize}" y="${y * tileSize}" width="${(x - start) * tileSize}" height="${tileSize}" fill="${colors[name] || colors.floor}"/>`);
      start = x;
      current = value;
    }
  }
  const points = world.points.map((point) => {
    const finale = point.campaignRole === "FINAL_CONVERGENCE";
    const radius = point.kind === "hub" ? 2 : 3;
    return `<circle cx="${point.x * tileSize + tileSize / 2}" cy="${point.y * tileSize + tileSize / 2}" r="${radius}" fill="${finale ? "#e85d75" : "#f6d365"}" stroke="#17120e" stroke-width="1"><title>${escapeXml(point.nameKo || point.name || point.id)}</title></circle>`;
  });
  const width = world.width * tileSize;
  const height = world.height * tileSize;
  return `<?xml version="1.0" encoding="UTF-8"?>\n<svg xmlns="http://www.w3.org/2000/svg" width="${width}" height="${height}" viewBox="0 0 ${width} ${height}" shape-rendering="crispEdges">\n<rect width="100%" height="100%" fill="#17120e"/>\n${runs.join("\n")}\n${points.join("\n")}\n</svg>\n`;
}

function escapeXml(value) {
  return String(value).replace(/[&<>"']/g, (character) => ({
    "&": "&amp;", "<": "&lt;", ">": "&gt;", "\"": "&quot;", "'": "&apos;"
  })[character]);
}

function usage() {
  process.stdout.write(`Generate and validate sealed Keyboard Wanderer worlds.\n\nUsage:\n  npm run world:generate -- [options]\n\nOptions:\n  --seed <integer>       First world seed (default: 20260717)\n  --count <integer>      Consecutive seeds to validate (default: 1)\n  --width <integer>      Debug/test width; production default is 160\n  --height <integer>     Debug/test height; production default is 160\n  --output, -o <path>    Write public JSON and SVG preview\n  --summary-only         Validate without writing artifacts\n`);
  process.exit(0);
}

function fail(message) {
  process.stderr.write(`${message}\nUse --help for usage.\n`);
  process.exit(1);
}
