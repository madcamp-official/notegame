import { assert } from "../errors.js";
import { clone } from "../domain/serialization.js";
import { computeCampaignContentHash } from "../domain/campaign.js";

export const CAMPAIGN_PLAN_CONTEXT_VERSION = "campaign-plan.v1";

const TOP_LEVEL_KEYS = Object.freeze(["campaign", "beats", "npcs", "quests", "endings", "areaFlavors"]);
const ALLOWED_ENRICHMENT = Object.freeze({
  campaign: Object.freeze(["title", "description", "tone"]),
  beats: Object.freeze(["title", "description"]),
  npcs: Object.freeze(["title", "description"]),
  quests: Object.freeze(["title", "description"]),
  endings: Object.freeze(["title", "description"]),
  areas: Object.freeze(["flavor"])
});
const FORBIDDEN_NARRATIVE_PATTERN = /(?:\b(?:x|y)\s*[:=]\s*-?\d+|\(\s*-?\d+\s*,\s*-?\d+\s*\)|-?\d+\s*,\s*-?\d+|\b(?:coordinate|coordinates|position|positions|route|routes|exit|exits|slot(?:id)?|asset(?:id)?|d20|hp|damage|reward|metric|mechanic|mechanics|recipe|move|copy|delete|connect|restore|undo)\b|\.png\b|\.jpg\b|\/assets?\/|requiredLinks|requiredRemoved|requiredActive|forbiddenLinks|worldStability|worldAutonomy|publicTrust|technicalDebt|companionBond|turnPressure|좌표|슬롯|에셋|레시피|주사위|피해\s*[+-]?\d|보상\s*[+-]?\d|수치\s*[+-]?\d|지도\s*(?:변경|재생성)|경로\s*(?:변경|생성))/iu;

export const CAMPAIGN_PLAN_RESPONSE_JSON_SCHEMA = Object.freeze({
  type: "object",
  additionalProperties: false,
  required: [...TOP_LEVEL_KEYS],
  properties: {
    campaign: {
      type: "object",
      additionalProperties: false,
      required: ["title", "description", "tone"],
      properties: {
        title: { type: "string", minLength: 1, maxLength: 80 },
        description: { type: "string", minLength: 1, maxLength: 480 },
        tone: { type: "array", minItems: 1, maxItems: 5, items: { type: "string", minLength: 1, maxLength: 32 } }
      }
    },
    beats: {
      type: "array",
      maxItems: 6,
      items: {
        type: "object",
        additionalProperties: false,
        required: ["id", "title", "description"],
        properties: {
          id: { type: "string", minLength: 1, maxLength: 80 },
          title: { type: "string", minLength: 1, maxLength: 80 },
          description: { type: "string", minLength: 1, maxLength: 320 }
        }
      }
    },
    npcs: {
      type: "array",
      maxItems: 6,
      items: {
        type: "object",
        additionalProperties: false,
        required: ["id", "title", "description"],
        properties: {
          id: { type: "string", minLength: 1, maxLength: 80 },
          title: { type: "string", minLength: 1, maxLength: 60 },
          description: { type: "string", minLength: 1, maxLength: 320 }
        }
      }
    },
    quests: {
      type: "array",
      maxItems: 8,
      items: {
        type: "object",
        additionalProperties: false,
        required: ["id", "title", "description"],
        properties: {
          id: { type: "string", minLength: 1, maxLength: 100 },
          title: { type: "string", minLength: 1, maxLength: 80 },
          description: { type: "string", minLength: 1, maxLength: 320 }
        }
      }
    },
    endings: {
      type: "array",
      maxItems: 7,
      items: {
        type: "object",
        additionalProperties: false,
        required: ["id", "title", "description"],
        properties: {
          id: { type: "string", minLength: 1, maxLength: 100 },
          title: { type: "string", minLength: 1, maxLength: 80 },
          description: { type: "string", minLength: 1, maxLength: 320 }
        }
      }
    },
    areaFlavors: {
      type: "array",
      maxItems: 12,
      items: {
        type: "object",
        additionalProperties: false,
        required: ["areaId", "flavor"],
        properties: {
          areaId: { type: "string", minLength: 1, maxLength: 100 },
          flavor: { type: "string", minLength: 1, maxLength: 240 }
        }
      }
    }
  }
});

function exactKeys(value, expected, code, label) {
  assert(value && typeof value === "object" && !Array.isArray(value), 502, code, `${label} must be an object.`);
  const unexpected = Object.keys(value).filter((key) => !expected.includes(key));
  assert(unexpected.length === 0, 502, code, `${label} contains forbidden fields: ${unexpected.join(", ")}.`);
}

function text(value, { code, label, maximum }) {
  assert(typeof value === "string", 502, code, `${label} must be a string.`);
  const normalized = value.trim();
  assert(normalized.length >= 1 && normalized.length <= maximum, 502, code, `${label} must contain 1-${maximum} characters.`);
  assert(!FORBIDDEN_NARRATIVE_PATTERN.test(normalized), 502, "CAMPAIGN_PLAN_MECHANICS_FORBIDDEN", `${label} may not contain coordinates, assets, mechanics, rewards, or recipe instructions.`);
  return normalized;
}

function stringIds(values) {
  return values.map((value) => value.id);
}

function slotCounts(world) {
  const counts = {};
  for (const slot of world.placementSlots || []) {
    const key = slot.kind || "unspecified";
    counts[key] = (counts[key] || 0) + 1;
  }
  return counts;
}

export function createCampaignPlanContext({ blueprint, world, themeHint = null }) {
  assert(blueprint && typeof blueprint === "object", 500, "CAMPAIGN_PLAN_CONTEXT_INVALID", "Campaign blueprint is required.");
  assert(world && typeof world === "object", 500, "CAMPAIGN_PLAN_CONTEXT_INVALID", "Generated world is required.");
  assert(themeHint === null || (typeof themeHint === "string" && themeHint.length >= 1 && themeHint.length <= 120 && !/[\u0000-\u001f\u007f]/u.test(themeHint)), 500, "CAMPAIGN_PLAN_CONTEXT_INVALID", "themeHint must be null or a bounded plain-text hint.");
  const context = {
    requestType: "CAMPAIGN_PLAN",
    schemaVersion: CAMPAIGN_PLAN_CONTEXT_VERSION,
    worldSeed: blueprint.worldSeed,
    turnLimit: blueprint.endingWindow?.hardLimit,
    themeHint,
    immutableIds: {
      beatIds: stringIds(blueprint.requiredStoryBeats || []),
      npcIds: stringIds(blueprint.npcRoles || []),
      questIds: stringIds(blueprint.questSeeds || []),
      endingIds: stringIds(blueprint.endingCandidates || []),
      areaIds: stringIds(world.areas || [])
    },
    genome: {
      worldName: blueprint.genome?.worldName,
      motif: blueprint.genome?.motif,
      motifImage: blueprint.genome?.motifImage,
      crisis: blueprint.genome?.crisis,
      hiddenCause: blueprint.genome?.hiddenCause,
      community: blueprint.genome?.community,
      dilemma: blueprint.genome?.dilemma,
      palette: blueprint.genome?.palette,
      companionTemperament: blueprint.genome?.companionTemperament
    },
    worldSummary: {
      generatorVersion: world.generatorVersion,
      layoutHash: world.layoutHash,
      width: world.width,
      height: world.height,
      geometryPolicy: world.geometryPolicy,
      biomeCount: (world.biomes || []).length,
      biomes: (world.biomes || []).map((biome) => ({ id: biome.id, nameKo: biome.nameKo })),
      areas: (world.areas || []).map((area) => ({ id: area.id, biomeId: area.biomeId })),
      placementCapacityByKind: slotCounts(world)
    },
    allowedEnrichment: clone(ALLOWED_ENRICHMENT),
    forbiddenContent: [
      "coordinates or positions",
      "placement slot selection or changes",
      "asset IDs or asset paths",
      "D20, HP, damage, rewards, metrics, or resource values",
      "ability legality or quest conditions",
      "ending recipes or ending IDs",
      "world geometry, routes, exits, or regeneration"
    ]
  };
  return validateCampaignPlanContext(context);
}

export function validateCampaignPlanContext(input) {
  exactKeys(input, ["requestType", "schemaVersion", "worldSeed", "turnLimit", "themeHint", "immutableIds", "genome", "worldSummary", "allowedEnrichment", "forbiddenContent"], "CAMPAIGN_PLAN_CONTEXT_INVALID", "Campaign plan context");
  assert(input.requestType === "CAMPAIGN_PLAN" && input.schemaVersion === CAMPAIGN_PLAN_CONTEXT_VERSION, 500, "CAMPAIGN_PLAN_CONTEXT_INVALID", "Campaign plan context version is invalid.");
  assert(Number.isSafeInteger(input.worldSeed), 500, "CAMPAIGN_PLAN_CONTEXT_INVALID", "Campaign plan worldSeed is invalid.");
  assert(Number.isInteger(input.turnLimit) && input.turnLimit >= 30 && input.turnLimit <= 50, 500, "CAMPAIGN_PLAN_CONTEXT_INVALID", "Campaign plan turnLimit is invalid.");
  assert(input.themeHint === null || (typeof input.themeHint === "string" && input.themeHint.length >= 1 && input.themeHint.length <= 120 && !/[\u0000-\u001f\u007f]/u.test(input.themeHint)), 500, "CAMPAIGN_PLAN_CONTEXT_INVALID", "Campaign plan themeHint is invalid.");
  exactKeys(input.immutableIds, ["beatIds", "npcIds", "questIds", "endingIds", "areaIds"], "CAMPAIGN_PLAN_CONTEXT_INVALID", "immutableIds");
  for (const [key, values] of Object.entries(input.immutableIds)) {
    assert(Array.isArray(values) && values.every((value) => typeof value === "string" && value.length > 0) && new Set(values).size === values.length, 500, "CAMPAIGN_PLAN_CONTEXT_INVALID", `${key} must contain unique string IDs.`);
  }
  exactKeys(input.genome, ["worldName", "motif", "motifImage", "crisis", "hiddenCause", "community", "dilemma", "palette", "companionTemperament"], "CAMPAIGN_PLAN_CONTEXT_INVALID", "genome");
  assert(Object.values(input.genome).every((value) => typeof value === "string" && value.length >= 1 && value.length <= 600), 500, "CAMPAIGN_PLAN_CONTEXT_INVALID", "Campaign genome must contain bounded strings.");
  exactKeys(input.worldSummary, ["generatorVersion", "layoutHash", "width", "height", "geometryPolicy", "biomeCount", "biomes", "areas", "placementCapacityByKind"], "CAMPAIGN_PLAN_CONTEXT_INVALID", "worldSummary");
  assert(typeof input.worldSummary.generatorVersion === "string" && typeof input.worldSummary.layoutHash === "string" && input.worldSummary.layoutHash.length === 64, 500, "CAMPAIGN_PLAN_CONTEXT_INVALID", "World summary identity is invalid.");
  assert(Number.isInteger(input.worldSummary.width) && Number.isInteger(input.worldSummary.height) && input.worldSummary.width >= 120 && input.worldSummary.width <= 256 && input.worldSummary.height >= 120 && input.worldSummary.height <= 256, 500, "CAMPAIGN_PLAN_CONTEXT_INVALID", "World summary dimensions are invalid.");
  assert(input.worldSummary.biomeCount === 6 && Array.isArray(input.worldSummary.biomes) && input.worldSummary.biomes.length === 6, 500, "CAMPAIGN_PLAN_CONTEXT_INVALID", "World summary must contain six biomes.");
  assert(Array.isArray(input.worldSummary.areas) && input.worldSummary.areas.length === input.immutableIds.areaIds.length, 500, "CAMPAIGN_PLAN_CONTEXT_INVALID", "World summary areas are invalid.");
  for (const biome of input.worldSummary.biomes) {
    exactKeys(biome, ["id", "nameKo"], "CAMPAIGN_PLAN_CONTEXT_INVALID", "worldSummary biome");
    assert(typeof biome.id === "string" && typeof biome.nameKo === "string", 500, "CAMPAIGN_PLAN_CONTEXT_INVALID", "World summary biome fields are invalid.");
  }
  for (const area of input.worldSummary.areas) {
    exactKeys(area, ["id", "biomeId"], "CAMPAIGN_PLAN_CONTEXT_INVALID", "worldSummary area");
    assert(input.immutableIds.areaIds.includes(area.id) && input.worldSummary.biomes.some((biome) => biome.id === area.biomeId), 500, "CAMPAIGN_PLAN_CONTEXT_INVALID", "World summary area references are invalid.");
  }
  assert(input.worldSummary.placementCapacityByKind && typeof input.worldSummary.placementCapacityByKind === "object" && !Array.isArray(input.worldSummary.placementCapacityByKind) && Object.values(input.worldSummary.placementCapacityByKind).every((value) => Number.isInteger(value) && value >= 0), 500, "CAMPAIGN_PLAN_CONTEXT_INVALID", "World placement capacity summary is invalid.");
  exactKeys(input.allowedEnrichment, ["campaign", "beats", "npcs", "quests", "endings", "areas"], "CAMPAIGN_PLAN_CONTEXT_INVALID", "allowedEnrichment");
  for (const [key, expected] of Object.entries(ALLOWED_ENRICHMENT)) {
    const actual = input.allowedEnrichment[key];
    assert(Array.isArray(actual) && actual.length === expected.length && actual.every((value, index) => value === expected[index]), 500, "CAMPAIGN_PLAN_CONTEXT_INVALID", "allowedEnrichment may not be widened or changed.");
  }
  assert(Array.isArray(input.forbiddenContent) && input.forbiddenContent.length > 0, 500, "CAMPAIGN_PLAN_CONTEXT_INVALID", "forbiddenContent is required.");
  return clone(input);
}

function validateKeyedEntries(values, { allowedIds, entryKeys, textFields, code, label }) {
  assert(Array.isArray(values), 502, code, `${label} must be an array.`);
  const seen = new Set();
  return values.map((entry) => {
    exactKeys(entry, entryKeys, code, `${label} entry`);
    assert(typeof entry.id === "string" && allowedIds.has(entry.id), 502, "CAMPAIGN_PLAN_ID_FORBIDDEN", `${label} references an unknown immutable ID.`);
    assert(!seen.has(entry.id), 502, code, `${label} IDs must be unique.`);
    seen.add(entry.id);
    const normalized = { id: entry.id };
    for (const [field, maximum] of Object.entries(textFields)) normalized[field] = text(entry[field], { code, label: `${label}.${field}`, maximum });
    return normalized;
  });
}

export function validateCampaignPlanOutput(input, contextInput) {
  const context = validateCampaignPlanContext(contextInput);
  exactKeys(input, TOP_LEVEL_KEYS, "CAMPAIGN_PLAN_OUTPUT_INVALID", "Campaign plan output");
  exactKeys(input.campaign, ["title", "description", "tone"], "CAMPAIGN_PLAN_OUTPUT_INVALID", "campaign");
  assert(Array.isArray(input.campaign.tone) && input.campaign.tone.length >= 1 && input.campaign.tone.length <= 5, 502, "CAMPAIGN_PLAN_OUTPUT_INVALID", "campaign.tone must contain 1-5 items.");
  const campaign = {
    title: text(input.campaign.title, { code: "CAMPAIGN_PLAN_OUTPUT_INVALID", label: "campaign.title", maximum: 80 }),
    description: text(input.campaign.description, { code: "CAMPAIGN_PLAN_OUTPUT_INVALID", label: "campaign.description", maximum: 480 }),
    tone: input.campaign.tone.map((value) => text(value, { code: "CAMPAIGN_PLAN_OUTPUT_INVALID", label: "campaign.tone", maximum: 32 }))
  };
  const ids = context.immutableIds;
  const beats = validateKeyedEntries(input.beats, { allowedIds: new Set(ids.beatIds), entryKeys: ["id", "title", "description"], textFields: { title: 80, description: 320 }, code: "CAMPAIGN_PLAN_OUTPUT_INVALID", label: "beats" });
  const npcs = validateKeyedEntries(input.npcs, { allowedIds: new Set(ids.npcIds), entryKeys: ["id", "title", "description"], textFields: { title: 60, description: 320 }, code: "CAMPAIGN_PLAN_OUTPUT_INVALID", label: "npcs" });
  const quests = validateKeyedEntries(input.quests, { allowedIds: new Set(ids.questIds), entryKeys: ["id", "title", "description"], textFields: { title: 80, description: 320 }, code: "CAMPAIGN_PLAN_OUTPUT_INVALID", label: "quests" });
  const endings = validateKeyedEntries(input.endings, { allowedIds: new Set(ids.endingIds), entryKeys: ["id", "title", "description"], textFields: { title: 80, description: 320 }, code: "CAMPAIGN_PLAN_OUTPUT_INVALID", label: "endings" });
  assert(Array.isArray(input.areaFlavors), 502, "CAMPAIGN_PLAN_OUTPUT_INVALID", "areaFlavors must be an array.");
  const areaIds = new Set(ids.areaIds);
  const seenAreas = new Set();
  const areaFlavors = input.areaFlavors.map((entry) => {
    exactKeys(entry, ["areaId", "flavor"], "CAMPAIGN_PLAN_OUTPUT_INVALID", "areaFlavors entry");
    assert(typeof entry.areaId === "string" && areaIds.has(entry.areaId), 502, "CAMPAIGN_PLAN_ID_FORBIDDEN", "areaFlavors references an unknown immutable area ID.");
    assert(!seenAreas.has(entry.areaId), 502, "CAMPAIGN_PLAN_OUTPUT_INVALID", "areaFlavors area IDs must be unique.");
    seenAreas.add(entry.areaId);
    return { areaId: entry.areaId, flavor: text(entry.flavor, { code: "CAMPAIGN_PLAN_OUTPUT_INVALID", label: "areaFlavors.flavor", maximum: 240 }) };
  });
  assert(beats.length <= ids.beatIds.length && npcs.length <= ids.npcIds.length && quests.length <= ids.questIds.length && endings.length <= ids.endingIds.length && areaFlavors.length <= ids.areaIds.length, 502, "CAMPAIGN_PLAN_OUTPUT_INVALID", "Campaign plan contains too many entries.");
  return { campaign, beats, npcs, quests, endings, areaFlavors };
}

function replaceFields(items, proposals, fields) {
  const byId = new Map(proposals.map((proposal) => [proposal.id, proposal]));
  return items.map((item) => {
    const proposal = byId.get(item.id);
    if (!proposal) return item;
    const output = { ...item };
    for (const field of fields) output[field] = proposal[field];
    return output;
  });
}

function refreshPersistenceMirrors(campaign) {
  campaign.progressionMetadata = clone(campaign.generationMetadata);
  campaign.scenarioPlan = {
    genome: clone(campaign.genome),
    questSeeds: clone(campaign.questSeeds),
    contentHash: campaign.contentHash,
    generationMetadata: clone(campaign.generationMetadata),
    areaFlavors: clone(campaign.areaFlavors || [])
  };
  campaign.generationPlan = clone(campaign.scenarioPlan);
}

export function applyCampaignPlanEnrichment(blueprint, proposal, { model = "configured-campaign-planner", modelProfile = "campaign-lite" } = {}) {
  const campaign = clone(blueprint);
  campaign.generatedTitle = proposal.campaign.title;
  campaign.generatedTitleKo = proposal.campaign.title;
  campaign.premise = proposal.campaign.description;
  campaign.premiseKo = proposal.campaign.description;
  campaign.tone = clone(proposal.campaign.tone);
  campaign.requiredStoryBeats = replaceFields(campaign.requiredStoryBeats, proposal.beats, ["title", "description"]);
  const npcProposals = new Map(proposal.npcs.map((npc) => [npc.id, npc]));
  campaign.npcRoles = campaign.npcRoles.map((npc) => {
    const planned = npcProposals.get(npc.id);
    return planned ? { ...npc, displayName: planned.title, content: planned.description } : npc;
  });
  campaign.questSeeds = replaceFields(campaign.questSeeds, proposal.quests, ["title", "description"]);
  campaign.questSeeds = campaign.questSeeds.map((quest) => ({ ...quest, summary: quest.description }));
  campaign.initialQuests = clone(campaign.questSeeds);
  campaign.endingCandidates = replaceFields(campaign.endingCandidates, proposal.endings, ["title", "description"]);
  campaign.areaFlavors = clone(proposal.areaFlavors);
  campaign.contentHash = computeCampaignContentHash(campaign);
  campaign.generationMetadata = {
    ...campaign.generationMetadata,
    planner: "gemini-structured-campaign-plan",
    model,
    modelProfile,
    enrichment: "validated",
    fallbackUsed: false,
    contentHash: campaign.contentHash
  };
  refreshPersistenceMirrors(campaign);
  return campaign;
}

export function createCampaignPlanFallback(blueprint, { reason = "planner_unavailable", model = "deterministic-campaign-genome" } = {}) {
  const campaign = clone(blueprint);
  campaign.contentHash = computeCampaignContentHash(campaign);
  campaign.generationMetadata = {
    ...campaign.generationMetadata,
    planner: model,
    enrichment: "deterministic_fallback",
    fallbackUsed: true,
    fallbackReason: reason,
    contentHash: campaign.contentHash
  };
  refreshPersistenceMirrors(campaign);
  return campaign;
}

export function createDeterministicCampaignPreview(blueprint) {
  const campaign = clone(blueprint);
  campaign.contentHash = computeCampaignContentHash(campaign);
  campaign.generationMetadata = {
    ...campaign.generationMetadata,
    planner: "deferred-to-run-creation",
    enrichment: "deterministic_preview",
    fallbackUsed: false,
    planningDeferredToRun: true,
    contentHash: campaign.contentHash
  };
  refreshPersistenceMirrors(campaign);
  return campaign;
}
