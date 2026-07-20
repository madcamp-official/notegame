import { assert, AppError } from "../errors.js";

/**
 * SCENE_TRANSITION_PLAN contract (LLM communication reference §14–22).
 *
 * The flow is a three-stage compress/restore pipeline:
 *   A. the game submits its state plus an allowlist of candidate ids,
 *   B. the model receives a compacted payload and a dynamically narrowed JSON Schema and
 *      answers with index picks plus short prose (the "compact decision"),
 *   C. the server deterministically restores indices to real ids and emits the v1.0 plan.
 *
 * The model never authors game ids — it can only select indices whose enum ranges are sized to
 * this request's candidate arrays, so an out-of-allowlist id is structurally impossible.
 * Field names are camelCase to match the rest of the server API and the Unity DTOs.
 */

export const SCENE_REQUEST_TYPE = "SCENE_TRANSITION_PLAN";
export const SCENE_PROTOCOL_VERSION = "1.0";
export const SCENE_SCHEMA_VERSION = "1.0";
export const SCENE_FALLBACK_MODEL = "deterministic-scene-fallback-v1";

// Prose caps from the compact-decision contract (§18.2).
const MAX_GOAL = 80;
const MAX_CONFLICT = 80;
const MAX_SUMMARY = 120;
const MAX_CHOICE_LABEL = 40;
const MAX_CHOICE_INTENT = 30;

const MAX_DESTINATIONS = 8;
const MAX_ROUTES_PER_DESTINATION = 6;
const MAX_ENTRIES_PER_DESTINATION = 6;
const MAX_PATHS = 24;
const MAX_BEATS = 8;
const MAX_TEMPLATES = 8;
const MAX_TRANSITIONS = 6;
const MAX_BGM = 8;
const MAX_SFX = 12;
const MAX_REVEALS = 8;

export const SCENE_SYSTEM_PROMPT = "You are the scene director for the Korean-first TRPG 넙죽이와 붕괴한 코드 왕국. You are given the current state and numbered candidate lists. Choose the next scene by returning ONLY the requested compact JSON: index picks into the provided arrays plus short Korean prose. You cannot invent ids, coordinates, rules, or outcomes — the server owns all of them. Keep g within 80, f within 80, n within 120 characters; choice labels within 40 and intent tags within 30. Write prose in the player's language. Return only JSON.";

const ID_PATTERN = /^[A-Za-z0-9_.:-]{1,80}$/;

function boundedId(value, name) {
  assert(typeof value === "string" && ID_PATTERN.test(value), 400, "SCENE_REQUEST_INVALID", `${name} must be a bounded id string.`);
  return value;
}

function idArray(value, name, { minimum = 0, maximum }) {
  assert(Array.isArray(value) && value.length >= minimum && value.length <= maximum, 400, "SCENE_REQUEST_INVALID", `${name} must contain ${minimum}-${maximum} ids.`);
  const ids = value.map((item, index) => boundedId(item, `${name}[${index}]`));
  assert(new Set(ids).size === ids.length, 400, "SCENE_REQUEST_INVALID", `${name} must not contain duplicates.`);
  return ids;
}

function boundedText(value, name, maximum, { minimum = 1 } = {}) {
  assert(typeof value === "string", 400, "SCENE_REQUEST_INVALID", `${name} must be a string.`);
  const normalized = value.trim();
  assert(normalized.length >= minimum && normalized.length <= maximum, 400, "SCENE_REQUEST_INVALID", `${name} is outside its length limit.`);
  return normalized;
}

function exactKeys(object, allowed, name) {
  const unknown = Object.keys(object).filter((key) => !allowed.includes(key));
  assert(unknown.length === 0, 400, "SCENE_REQUEST_INVALID", `${name} has unknown fields: ${unknown.join(", ")}.`);
}

/** Validates and normalizes an A-stage request. Throws AppError(400) on any contract violation. */
export function validateSceneTransitionRequest(input) {
  assert(input && typeof input === "object" && !Array.isArray(input), 400, "SCENE_REQUEST_INVALID", "A scene transition request object is required.");
  exactKeys(input, ["protocolVersion", "schemaVersion", "requestType", "requestId", "run", "context", "candidates"], "request");
  assert(input.protocolVersion === SCENE_PROTOCOL_VERSION, 400, "SCENE_REQUEST_INVALID", "protocolVersion is unsupported.");
  assert(input.schemaVersion === SCENE_SCHEMA_VERSION, 400, "SCENE_REQUEST_INVALID", "schemaVersion is unsupported.");
  assert(input.requestType === SCENE_REQUEST_TYPE, 400, "SCENE_REQUEST_INVALID", "requestType must be SCENE_TRANSITION_PLAN.");
  const requestId = boundedId(input.requestId, "requestId");

  const run = input.run;
  assert(run && typeof run === "object" && !Array.isArray(run), 400, "SCENE_REQUEST_INVALID", "run is required.");
  exactKeys(run, ["runId", "turnId", "turnNo", "expectedRunVersion"], "run");
  const runId = boundedId(run.runId, "run.runId");
  const turnId = boundedId(run.turnId, "run.turnId");
  assert(Number.isInteger(run.turnNo) && run.turnNo >= 0 && run.turnNo <= 10000, 400, "SCENE_REQUEST_INVALID", "run.turnNo is invalid.");
  assert(Number.isSafeInteger(run.expectedRunVersion) && run.expectedRunVersion >= 0, 400, "SCENE_REQUEST_INVALID", "run.expectedRunVersion is invalid.");

  const context = input.context;
  assert(context && typeof context === "object" && !Array.isArray(context), 400, "SCENE_REQUEST_INVALID", "context is required.");
  exactKeys(context, ["worldLayoutHash", "contextHash", "currentAreaId", "playerIntent", "storySummary"], "context");
  const worldLayoutHash = boundedText(context.worldLayoutHash, "context.worldLayoutHash", 120);
  const contextHash = boundedText(context.contextHash, "context.contextHash", 120);
  const currentAreaId = boundedId(context.currentAreaId, "context.currentAreaId");
  const playerIntent = boundedText(context.playerIntent, "context.playerIntent", 200);
  const storySummary = boundedText(context.storySummary, "context.storySummary", 400);

  const candidates = input.candidates;
  assert(candidates && typeof candidates === "object" && !Array.isArray(candidates), 400, "SCENE_REQUEST_INVALID", "candidates is required.");
  exactKeys(candidates, ["destinations", "storyBeatIds", "sceneTemplateIds", "transitionStyleIds", "bgmCueIds", "sfxCueIds", "revealIds"], "candidates");
  assert(Array.isArray(candidates.destinations) && candidates.destinations.length >= 1 && candidates.destinations.length <= MAX_DESTINATIONS, 400, "SCENE_REQUEST_INVALID", `candidates.destinations must contain 1-${MAX_DESTINATIONS} entries.`);
  const destinations = candidates.destinations.map((destination, index) => {
    assert(destination && typeof destination === "object" && !Array.isArray(destination), 400, "SCENE_REQUEST_INVALID", `destinations[${index}] must be an object.`);
    exactKeys(destination, ["destinationAreaId", "routeIds", "entrySlotIds"], `destinations[${index}]`);
    return {
      destinationAreaId: boundedId(destination.destinationAreaId, `destinations[${index}].destinationAreaId`),
      routeIds: idArray(destination.routeIds, `destinations[${index}].routeIds`, { minimum: 1, maximum: MAX_ROUTES_PER_DESTINATION }),
      entrySlotIds: idArray(destination.entrySlotIds, `destinations[${index}].entrySlotIds`, { minimum: 1, maximum: MAX_ENTRIES_PER_DESTINATION })
    };
  });

  const normalized = {
    protocolVersion: SCENE_PROTOCOL_VERSION,
    schemaVersion: SCENE_SCHEMA_VERSION,
    requestType: SCENE_REQUEST_TYPE,
    requestId,
    run: { runId, turnId, turnNo: run.turnNo, expectedRunVersion: run.expectedRunVersion },
    context: { worldLayoutHash, contextHash, currentAreaId, playerIntent, storySummary },
    candidates: {
      destinations,
      storyBeatIds: idArray(candidates.storyBeatIds, "candidates.storyBeatIds", { minimum: 1, maximum: MAX_BEATS }),
      sceneTemplateIds: idArray(candidates.sceneTemplateIds, "candidates.sceneTemplateIds", { minimum: 1, maximum: MAX_TEMPLATES }),
      transitionStyleIds: idArray(candidates.transitionStyleIds, "candidates.transitionStyleIds", { minimum: 1, maximum: MAX_TRANSITIONS }),
      bgmCueIds: idArray(candidates.bgmCueIds, "candidates.bgmCueIds", { minimum: 1, maximum: MAX_BGM }),
      sfxCueIds: idArray(candidates.sfxCueIds, "candidates.sfxCueIds", { minimum: 0, maximum: MAX_SFX }),
      revealIds: idArray(candidates.revealIds, "candidates.revealIds", { minimum: 0, maximum: MAX_REVEALS })
    }
  };
  assert(flattenPaths(normalized).length <= MAX_PATHS, 400, "SCENE_REQUEST_INVALID", `The flattened path count exceeds ${MAX_PATHS}.`);
  return normalized;
}

/**
 * Flattens destination × route × entry into the paths[] array (§16.3): the model picks a single
 * path index, which resolves all three ids at once, so an incompatible combination cannot occur.
 */
export function flattenPaths(request) {
  const paths = [];
  for (const destination of request.candidates.destinations) {
    for (const routeId of destination.routeIds) {
      for (const entrySlotId of destination.entrySlotIds) {
        paths.push({ destinationAreaId: destination.destinationAreaId, routeId, entrySlotId });
      }
    }
  }
  return paths;
}

/** B-stage compact payload (§17.3). Array order must stay frozen until restoration. */
export function compactPayloadForModel(request, paths) {
  return {
    state: {
      area: request.context.currentAreaId,
      intent: request.context.playerIntent,
      summary: request.context.storySummary
    },
    paths: paths.map((path) => ({ destination: path.destinationAreaId, route: path.routeId, entry: path.entrySlotId })),
    beats: request.candidates.storyBeatIds,
    templates: request.candidates.sceneTemplateIds,
    transitions: request.candidates.transitionStyleIds,
    bgm: request.candidates.bgmCueIds,
    sfx: request.candidates.sfxCueIds,
    reveals: request.candidates.revealIds
  };
}

function indexEnum(length) {
  return Array.from({ length }, (_, index) => index);
}

/** Dynamic compact-decision schema (§18.2): every index enum is sized to this request. */
export function compactDecisionSchema(request, paths) {
  const sfxCount = request.candidates.sfxCueIds.length;
  const revealCount = request.candidates.revealIds.length;
  return {
    type: "object",
    additionalProperties: false,
    required: ["p", "b", "t", "x", "m", "s", "v", "g", "f", "n", "c"],
    properties: {
      p: { type: "integer", enum: indexEnum(paths.length) },
      b: { type: "integer", enum: indexEnum(request.candidates.storyBeatIds.length) },
      t: { type: "integer", enum: indexEnum(request.candidates.sceneTemplateIds.length) },
      x: { type: "integer", enum: indexEnum(request.candidates.transitionStyleIds.length) },
      m: { type: "integer", enum: indexEnum(request.candidates.bgmCueIds.length) },
      s: sfxCount === 0
        ? { type: "array", maxItems: 0, items: { type: "integer" } }
        : { type: "array", maxItems: 2, items: { type: "integer", enum: indexEnum(sfxCount) } },
      v: revealCount === 0
        ? { type: "array", maxItems: 0, items: { type: "integer" } }
        : { type: "array", maxItems: 1, items: { type: "integer", enum: indexEnum(revealCount) } },
      g: { type: "string", minLength: 1, maxLength: MAX_GOAL },
      f: { type: "string", minLength: 1, maxLength: MAX_CONFLICT },
      n: { type: "string", minLength: 1, maxLength: MAX_SUMMARY },
      c: {
        type: "array",
        minItems: 2,
        maxItems: 2,
        items: {
          type: "object",
          additionalProperties: false,
          required: ["l", "i"],
          properties: {
            l: { type: "string", minLength: 1, maxLength: MAX_CHOICE_LABEL },
            i: { type: "string", minLength: 1, maxLength: MAX_CHOICE_INTENT }
          }
        }
      }
    }
  };
}

function pickIndex(value, length, name) {
  if (!Number.isInteger(value) || value < 0 || value >= length) {
    throw new AppError(502, "SCENE_DECISION_INVALID", `${name} index is outside the candidate range.`);
  }
  return value;
}

function decisionText(value, maximum, name) {
  if (typeof value !== "string") throw new AppError(502, "SCENE_DECISION_INVALID", `${name} must be a string.`);
  const normalized = value.trim();
  if (normalized.length < 1 || normalized.length > maximum) {
    throw new AppError(502, "SCENE_DECISION_INVALID", `${name} is outside its length limit.`);
  }
  return normalized;
}

/**
 * C-stage restoration (§19): maps a compact decision back to real ids and assembles the v1.0 plan.
 * The structured-output schema already constrains the model, but every bound is re-checked here so
 * the restore step is safe even against a misbehaving backend; violations throw AppError(502),
 * which the caller's retry/fallback policy absorbs.
 */
export function restoreScenePlan(request, paths, decision, usage) {
  if (!decision || typeof decision !== "object" || Array.isArray(decision)) {
    throw new AppError(502, "SCENE_DECISION_INVALID", "The compact decision must be a JSON object.");
  }
  const path = paths[pickIndex(decision.p, paths.length, "p")];
  const storyBeatId = request.candidates.storyBeatIds[pickIndex(decision.b, request.candidates.storyBeatIds.length, "b")];
  const sceneTemplateId = request.candidates.sceneTemplateIds[pickIndex(decision.t, request.candidates.sceneTemplateIds.length, "t")];
  const transitionStyleId = request.candidates.transitionStyleIds[pickIndex(decision.x, request.candidates.transitionStyleIds.length, "x")];
  const bgmCueId = request.candidates.bgmCueIds[pickIndex(decision.m, request.candidates.bgmCueIds.length, "m")];

  if (!Array.isArray(decision.s) || decision.s.length > 2) throw new AppError(502, "SCENE_DECISION_INVALID", "s must contain at most 2 indices.");
  const sfxCueIds = [...new Set(decision.s.map((value) => pickIndex(value, request.candidates.sfxCueIds.length, "s")))]
    .map((index) => request.candidates.sfxCueIds[index]);
  if (!Array.isArray(decision.v) || decision.v.length > 1) throw new AppError(502, "SCENE_DECISION_INVALID", "v must contain at most 1 index.");
  const revealIds = decision.v.map((value) => request.candidates.revealIds[pickIndex(value, request.candidates.revealIds.length, "v")]);

  if (!Array.isArray(decision.c) || decision.c.length !== 2) throw new AppError(502, "SCENE_DECISION_INVALID", "c must contain exactly 2 choices.");
  const suggestedChoices = decision.c.map((choice, index) => {
    if (!choice || typeof choice !== "object" || Array.isArray(choice)) throw new AppError(502, "SCENE_DECISION_INVALID", `c[${index}] must be an object.`);
    return {
      choiceId: `choice-${index + 1}`,
      label: decisionText(choice.l, MAX_CHOICE_LABEL, `c[${index}].l`),
      intentTag: decisionText(choice.i, MAX_CHOICE_INTENT, `c[${index}].i`)
    };
  });

  const summary = decisionText(decision.n, MAX_SUMMARY, "n");
  return {
    protocolVersion: SCENE_PROTOCOL_VERSION,
    schemaVersion: SCENE_SCHEMA_VERSION,
    requestType: SCENE_REQUEST_TYPE,
    requestId: request.requestId,
    status: "OK",
    fallbackUsed: false,
    echo: {
      runId: request.run.runId,
      turnId: request.run.turnId,
      turnNo: request.run.turnNo,
      expectedRunVersion: request.run.expectedRunVersion,
      worldLayoutHash: request.context.worldLayoutHash,
      contextHash: request.context.contextHash
    },
    selection: {
      destinationAreaId: path.destinationAreaId,
      routeId: path.routeId,
      entrySlotId: path.entrySlotId,
      storyBeatId,
      sceneTemplateId
    },
    transition: {
      transitionStyleId,
      bgmCueId,
      sfxCueIds,
      cameraCue: "CAMERA_FOLLOW",
      summary,
      body: summary
    },
    scenePlan: {
      sceneGoal: decisionText(decision.g, MAX_GOAL, "g"),
      conflict: decisionText(decision.f, MAX_CONFLICT, "f"),
      revealIds,
      suggestedChoices
    },
    proposedOps: [],
    memoryCandidates: [],
    usage
  };
}

/** Deterministic fallback (§20.2): first candidate of every list, safe generic prose. */
export function createFallbackScenePlan(requestInput, reason = "fallback") {
  const request = requestInput?.requestType === SCENE_REQUEST_TYPE && requestInput?.candidates?.destinations
    ? requestInput
    : validateSceneTransitionRequest(requestInput);
  const paths = flattenPaths(request);
  const decision = {
    p: 0,
    b: 0,
    t: 0,
    x: 0,
    m: 0,
    s: [],
    v: [],
    g: request.context.playerIntent.slice(0, MAX_GOAL),
    f: "예상치 못한 기척이 느껴진다.",
    n: `${request.context.playerIntent}`.slice(0, MAX_SUMMARY),
    c: [
      { l: "계속 나아간다", i: "전진" },
      { l: "주변을 살핀다", i: "경계" }
    ]
  };
  const plan = restoreScenePlan(request, paths, decision, {
    modelProfile: "deterministic",
    modelId: SCENE_FALLBACK_MODEL,
    inputTokens: 0,
    outputTokens: 0,
    latencyMs: 0,
    finishReason: reason
  });
  return { ...plan, fallbackUsed: true };
}
