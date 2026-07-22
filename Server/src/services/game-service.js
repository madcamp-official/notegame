import { randomInt, randomUUID } from "node:crypto";
import { assert, AppError } from "../errors.js";
import { createCampaignBlueprint, endingConditionReports } from "../domain/campaign.js";
import { generateWorld, isWalkable, publicWorld } from "../domain/world.js";
import { DeterministicD20Source, createRunState, directorContext, isMonsterEncounterRequest, normalizeTravelRequest, normalizeTurnRequest, publicRun, publicTurn, resolveNarrativeChoice, resolveSafeTravel, resolveTurn, travelFingerprint, turnFingerprint, tryForceMonsterEncounter } from "../domain/turn-engine.js";
import { planDecisionScene, planDeterministicDecisionScene, resolveTravelDecision } from "../domain/decision-orchestrator.js";
import { planSkillTarget } from "../domain/skill-target-orchestrator.js";
import { applyCampaignPlanEnrichment, createCampaignPlanContext, createCampaignPlanFallback, createDeterministicCampaignPreview, validateCampaignPlanOutput } from "../llm/campaign-planning.js";
import { createFallbackNarration, validateNarrationContext, validateNarrationOutput } from "../llm/narration.js";
import { createFallbackScenePlan, validateSceneTransitionRequest } from "../llm/scene-transition.js";
import { PRODUCT_CONTRACT } from "../domain/codria-contract.js";
import { choiceSelectionFingerprint, choicesFromLegacySkills, narrationNoteFromPlayerText, narrativeChoiceRequest, normalizeChoiceSelectionRequest, normalizePlayerMessageRequest, playerMessageFingerprint, playerMessageRequest, sealNarrativeIntervention, selectedChoiceForRun } from "../domain/narrative-choices.js";
import { selectRelevantMemories } from "../llm/prompt-composer.js";
import { normalizeInventoryRequest, resolveInventoryAction } from "../domain/inventory.js";
import { deterministicUuid, fingerprint } from "../domain/serialization.js";
import { fallbackPlayerActionProposal, playerActionContext, playerActionRejectionReason, playerTextRequestsMovement, requestedPlayerMovementDestination, resolvePlayerActionDestination, validatePlayerActionProposal } from "../llm/player-action.js";
import { withLlmTurnBudget } from "../llm/request-budget.js";

const UUID_PATTERN = /^[0-9a-f]{8}-[0-9a-f]{4}-[1-8][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i;
const IDEMPOTENCY_PATTERN = /^[A-Za-z0-9][A-Za-z0-9_.:-]{7,127}$/;
const DEFAULT_IDEMPOTENCY_LEASE_MS = 30_000;
const DEFAULT_IDEMPOTENCY_WAIT_TIMEOUT_MS = 25_000;
const DEFAULT_LLM_TURN_DEADLINE_MS = 20_000;
const DEFAULT_LLM_TURN_MAX_CALLS = 6;

function exactKeys(object, allowed, code) {
  const unknown = Object.keys(object).filter((key) => !allowed.includes(key));
  assert(unknown.length === 0, 400, code, `Unknown fields: ${unknown.join(", ")}.`);
}

function cloneChoice(choice) {
  return structuredClone(choice);
}

function alignFreeformDialogue(output) {
  if (!Array.isArray(output?.dialogue) || output.dialogue.length === 0) return output;
  const nonDialogue = (output.storySequence || []).filter((beat) => beat?.type !== "DIALOGUE");
  const dialogueBeats = output.dialogue.map((item) => ({
    type: "DIALOGUE",
    speakerId: item.speakerId,
    actionId: null,
    text: item.line
  }));
  return { ...output, storySequence: [...nonDialogue, ...dialogueBeats].slice(0, 8) };
}

function sealDirectorOutput(output, context, resolvedRun) {
  const source = output?.nextIntervention || {};
  const proposal = {
    reason: source.reason || "장면이 멈췄다. 넙죽이의 다음 반응을 선택한다.",
    choices: source.choices || choicesFromLegacySkills(source.suggestedSkillIds, context.skillId)
  };
  const nextIntervention = sealNarrativeIntervention(proposal, {
    runId: resolvedRun.id,
    turnNo: context.turnNo,
    runVersion: resolvedRun.version,
    allowedEntityIds: context.allowedEntityIds,
    allowedDestinationRefs: context.readOnlyPlaces.map((place) => place.id),
    allowTravel: false,
    authoritativeRun: resolvedRun
  });
  return { ...output, nextIntervention };
}

function sealDirectorOutputOrFallback(output, context, resolvedRun, logger, event) {
  try {
    return sealDirectorOutput(output, context, resolvedRun);
  } catch (error) {
    logger?.warn?.({ event, category: error?.code || "unexpected" });
    return sealDirectorOutput(createFallbackNarration(context), context, resolvedRun);
  }
}

export class GameService {
  constructor({
    store,
    narrator,
    d20Source = new DeterministicD20Source(),
    worldGenerator = generateWorld,
    clock = () => new Date().toISOString(),
    logger = console,
    idempotencyLeaseMs = DEFAULT_IDEMPOTENCY_LEASE_MS,
    idempotencyWaitTimeoutMs = DEFAULT_IDEMPOTENCY_WAIT_TIMEOUT_MS,
    llmTurnDeadlineMs = DEFAULT_LLM_TURN_DEADLINE_MS,
    llmTurnMaxCalls = DEFAULT_LLM_TURN_MAX_CALLS
  }) {
    this.store = store;
    this.narrator = narrator;
    this.d20Source = d20Source;
    this.worldGenerator = worldGenerator;
    this.clock = clock;
    this.logger = logger;
    this.idempotencyLeaseMs = idempotencyLeaseMs;
    this.idempotencyWaitTimeoutMs = idempotencyWaitTimeoutMs;
    this.llmTurnDeadlineMs = llmTurnDeadlineMs;
    this.llmTurnMaxCalls = llmTurnMaxCalls;
  }

  _runLlmTurn(work) {
    return withLlmTurnBudget({
      timeoutMs: this.llmTurnDeadlineMs,
      maxCalls: this.llmTurnMaxCalls
    }, work);
  }

  async _runIdempotent({ ownerId, operation, idempotencyKey, requestFingerprint, persistResponse = false, markReplay = false }, work) {
    if (!idempotencyKey || typeof this.store.claimIdempotency !== "function") return work();
    const startedAt = Date.now();
    while (true) {
      if (Date.now() - startedAt >= this.idempotencyWaitTimeoutMs) {
        throw new AppError(503, "IDEMPOTENCY_IN_PROGRESS", "An identical request is still being processed. Retry with the same key.", {
          retryAfterMs: Math.min(1000, this.idempotencyLeaseMs)
        });
      }
      const leaseToken = randomUUID();
      const claim = await this.store.claimIdempotency({
        ownerId,
        operation,
        idempotencyKey,
        requestFingerprint,
        leaseToken,
        leaseMs: this.idempotencyLeaseMs
      });
      if (claim.state === "completed") {
        if (claim.response !== null && claim.response !== undefined) {
          const replay = structuredClone(claim.response);
          if (markReplay && replay && typeof replay === "object") replay.fromIdempotencyCache = true;
          return replay;
        }
        // Turn and navigation ledgers already contain their authoritative result.
        // Re-entering work takes their existing cache branch without another LLM call.
        return work();
      }
      if (claim.state !== "acquired") {
        await delay(Math.max(1, Math.min(250, claim.retryAfterMs || 50)));
        continue;
      }

      const renewEveryMs = Math.max(1000, Math.floor(this.idempotencyLeaseMs / 3));
      const heartbeat = setInterval(() => {
        this.store.renewIdempotencyLease({
          ownerId,
          operation,
          idempotencyKey,
          leaseToken,
          leaseMs: this.idempotencyLeaseMs
        }).catch((error) => this.logger?.warn?.({
          event: "idempotency_lease_renewal_failed",
          operation,
          category: error?.code || error?.name || "unexpected"
        }));
      }, renewEveryMs);
      heartbeat.unref?.();
      try {
        const result = await work();
        const completed = await this.store.completeIdempotency({
          ownerId,
          operation,
          idempotencyKey,
          leaseToken,
          response: persistResponse ? result : null
        });
        if (completed) return result;
      } catch (error) {
        await this.store.releaseIdempotency({ ownerId, operation, idempotencyKey, leaseToken }).catch((releaseError) => {
          this.logger?.warn?.({ event: "idempotency_lease_release_failed", operation, category: releaseError?.code || releaseError?.name || "unexpected" });
        });
        throw error;
      } finally {
        clearInterval(heartbeat);
      }
      // A lease can be lost only after its heartbeat failed or expired. Read the
      // winner's durable result rather than returning an unrecorded side effect.
    }
  }

  _prepareD20(snapshot) {
    const d20 = this.d20Source.roll({
      resolutionSeed: snapshot.resolutionSeed,
      runId: snapshot.id,
      turnNo: snapshot.currentTurn + 1
    });
    if (!Number.isInteger(d20) || d20 < 1 || d20 > 20) throw new AppError(500, "D20_SOURCE_INVALID", "The server D20 source returned an invalid value.");
    return d20;
  }

  _authoritativeD20(snapshot, suppliedD20 = null) {
    const expected = this._prepareD20(snapshot);
    if (suppliedD20 !== null && suppliedD20 !== undefined && suppliedD20 !== expected) {
      throw new AppError(409, "D20_MISMATCH", "The submitted D20 does not match the server-prepared roll for this run version.", {
        expectedRunVersion: snapshot.version
      });
    }
    return expected;
  }

  async health() {
    const storage = await this.store.health();
    return {
      status: "ok",
      service: "codria-v4-game-server",
      storage: storage.storage,
      schemaVersion: storage.schemaVersion,
      authoritativeTurns: true,
      campaignDirector: true,
      butterflySceneDirector: true,
      sceneCandidateAuthority: "server_allowlist",
      generativeCampaignPlanning: true,
      campaignPreviewMode: "deterministic_no_llm",
      runCampaignPlanMode: "one_request_with_at_most_one_retry",
      immutableWorlds: true,
      safeTravelSeparateFromCampaignTurns: true,
      productContract: PRODUCT_CONTRACT,
      canonicalInputTypes: PRODUCT_CONTRACT.inputTypes,
      consumingActionContexts: ["COMBAT", "INVESTIGATION", "NEGOTIATION", "DEPLOYMENT"],
      narrationProfile: this.narrator?.modelProfiles?.fast?.model || "configured-or-fallback",
      campaignPlanningProfile: this.narrator?.modelProfiles?.fast?.model || "configured-or-fallback"
    };
  }

  async generateCampaignContent({ worldSeed, turnLimit, archetype = undefined, themeHint = null, enrichWithLlm = true }) {
    // The complete world is generated before the optional LLM enrichment. The
    // planner receives a read-only summary and can never alter the sealed layout.
    const world = this.worldGenerator(worldSeed);
    const deterministicBlueprint = createCampaignBlueprint({ worldSeed, requestedArchetype: archetype, turnLimit });
    if (!enrichWithLlm) return { blueprint: createDeterministicCampaignPreview(deterministicBlueprint), world };
    const planningContext = createCampaignPlanContext({ blueprint: deterministicBlueprint, world, themeHint });
    let blueprint;
    if (typeof this.narrator?.planCampaign === "function") {
      try {
        const candidate = await this.narrator.planCampaign(planningContext);
        if (!candidate || candidate.fallbackUsed === true || candidate.proposal === null) {
          blueprint = createCampaignPlanFallback(deterministicBlueprint, {
            reason: candidate?.fallbackReason || "planner_declined",
            model: candidate?.model || "deterministic-campaign-genome"
          });
        } else {
          const proposalInput = candidate.proposal || campaignProposalOnly(candidate);
          const proposal = validateCampaignPlanOutput(proposalInput, planningContext);
          blueprint = applyCampaignPlanEnrichment(deterministicBlueprint, proposal, {
            model: candidate.model,
            modelProfile: candidate.modelProfile
          });
        }
      } catch (error) {
        this.logger?.warn?.({ event: "campaign_plan_fallback", category: error?.code || "unexpected" });
        blueprint = createCampaignPlanFallback(deterministicBlueprint, { reason: error?.code || "planner_error" });
      }
    } else {
      blueprint = createCampaignPlanFallback(deterministicBlueprint, { reason: "planner_unavailable" });
    }
    return { blueprint, world };
  }

  async createCampaign(ownerId, input = {}) {
    assert(input && typeof input === "object" && !Array.isArray(input), 400, "CAMPAIGN_REQUEST_INVALID", "Campaign body must be an object.");
    exactKeys(input, ["title", "worldSeed", "turnLimit", "archetype", "idempotencyKey"], "CAMPAIGN_REQUEST_INVALID");
    const idempotencyKey = normalizeCreationIdempotencyKey(input.idempotencyKey);
    const suppliedTitle = input.title === undefined ? null : input.title;
    assert(suppliedTitle === null || (typeof suppliedTitle === "string" && suppliedTitle.trim().length >= 1 && suppliedTitle.trim().length <= 100), 400, "CAMPAIGN_TITLE_INVALID", "title must contain 1-100 characters.");
    const requestHash = fingerprint({
      title: suppliedTitle?.trim() || null,
      worldSeed: input.worldSeed ?? null,
      turnLimit: input.turnLimit ?? null,
      archetype: input.archetype ?? null
    });
    const worldSeed = input.worldSeed === undefined
      ? (idempotencyKey ? deterministicWorldSeed(`${ownerId}:campaign.create:${idempotencyKey}:${requestHash}`) : randomInt(1, 2_147_483_647))
      : input.worldSeed;
    assert(Number.isSafeInteger(worldSeed), 400, "WORLD_SEED_INVALID", "worldSeed must be a safe integer.");
    const turnLimit = input.turnLimit === undefined ? 40 : input.turnLimit;
    assert(Number.isInteger(turnLimit) && turnLimit >= 30 && turnLimit <= 50, 400, "TURN_LIMIT_INVALID", "turnLimit must be between 30 and 50.");
    const campaignId = idempotencyKey
      ? deterministicUuid(`${ownerId}:campaign.create:${idempotencyKey}:${requestHash}`)
      : randomUUID();
    const create = () => this._runLlmTurn(async () => {
      if (idempotencyKey) {
        const persisted = await findExisting(() => this.store.getCampaign(ownerId, campaignId));
        if (persisted) return { ...publicCampaign(persisted), fromIdempotencyCache: true };
      }
      const { blueprint, world } = await this.generateCampaignContent({ worldSeed, turnLimit, archetype: input.archetype, enrichWithLlm: false });
      const title = suppliedTitle === null ? blueprint.generatedTitle : suppliedTitle;
      const now = this.clock();
      const campaign = {
        id: campaignId, ownerId, title: title.trim(), worldSeed, turnLimit, status: "active",
        ...blueprint,
        world,
        createdAt: now, updatedAt: now
      };
      const created = publicCampaign(await this.store.createCampaign(campaign));
      return idempotencyKey ? { ...created, fromIdempotencyCache: false } : created;
    });
    if (!idempotencyKey) return create();
    return this._runIdempotent({
      ownerId,
      operation: "campaign.create",
      idempotencyKey,
      requestFingerprint: requestHash,
      persistResponse: true,
      markReplay: true
    }, create);
  }

  async listCampaigns(ownerId) {
    return Promise.all((await this.store.listCampaigns(ownerId)).map(publicCampaign));
  }

  async getCampaign(ownerId, campaignId) {
    validateResourceId(campaignId, "campaignId");
    return publicCampaign(await this.store.getCampaign(ownerId, campaignId));
  }

  async createRun(ownerId, campaignId, input = {}) {
    validateResourceId(campaignId, "campaignId");
    assert(input && typeof input === "object" && !Array.isArray(input), 400, "RUN_REQUEST_INVALID", "Run body must be an object.");
    exactKeys(input, ["worldSeed", "turnLimit", "themeHint", "idempotencyKey"], "RUN_REQUEST_INVALID");
    const idempotencyKey = normalizeCreationIdempotencyKey(input.idempotencyKey);
    const themeHint = normalizeThemeHint(input.themeHint);
    assert(input.worldSeed === undefined || Number.isSafeInteger(input.worldSeed), 400, "WORLD_SEED_INVALID", "worldSeed must be a safe integer.");
    assert(input.turnLimit === undefined || (Number.isInteger(input.turnLimit) && input.turnLimit >= 30 && input.turnLimit <= 50), 400, "TURN_LIMIT_INVALID", "turnLimit must be between 30 and 50.");
    const requestHash = fingerprint({
      campaignId,
      worldSeed: input.worldSeed ?? null,
      turnLimit: input.turnLimit ?? null,
      themeHint
    });
    const runId = idempotencyKey
      ? deterministicUuid(`${ownerId}:run.create:${campaignId}:${idempotencyKey}:${requestHash}`)
      : randomUUID();
    const create = () => this._runLlmTurn(async () => {
      if (idempotencyKey) {
        const persisted = await findExisting(() => this.store.getRun(ownerId, runId));
        if (persisted) return { ...publicRun(persisted), fromIdempotencyCache: true };
      }
      const template = await this.store.getCampaign(ownerId, campaignId);
      assert(template.status === "active", 409, "CAMPAIGN_NOT_ACTIVE", "Campaign does not accept new runs.");
      const worldSeed = input.worldSeed === undefined ? template.worldSeed : input.worldSeed;
      const turnLimit = input.turnLimit === undefined ? template.turnLimit : input.turnLimit;
      const { blueprint, world } = await this.generateCampaignContent({ worldSeed, turnLimit, archetype: template.archetype, themeHint });
      const campaign = {
        ...template,
        ...blueprint,
        id: template.id,
        ownerId,
        title: template.title,
        worldSeed,
        turnLimit,
        world
      };
      // World geometry, areas, paths, exits and placement slots are sealed here.
      // Turns clone and mutate only run/entity/narrative state inside this layout.
      const run = createRunState({ campaign, ownerId, runId, now: this.clock() });
      const created = publicRun(await this.store.createRun(run));
      return idempotencyKey ? { ...created, fromIdempotencyCache: false } : created;
    });
    if (!idempotencyKey) return create();
    return this._runIdempotent({
      ownerId,
      operation: `run.create:${campaignId}`,
      idempotencyKey,
      requestFingerprint: requestHash,
      persistResponse: true,
      markReplay: true
    }, create);
  }

  async getRun(ownerId, runId) {
    validateResourceId(runId, "runId");
    return publicRun(await this.store.getRun(ownerId, runId));
  }

  async getRunDebug(ownerId, runId) {
    validateResourceId(runId, "runId");
    const run = await this.store.getRun(ownerId, runId);
    const turns = await this.store.listTurns(ownerId, runId);
    const latestTurn = turns.at(-1) || null;
    const context = latestTurn ? directorContext(run, latestTurn) : null;
    return {
      run: publicRun(run),
      turns: turns.map(publicTurn),
      promptContext: context,
      relevantMemories: context ? selectRelevantMemories(context) : [],
      diagnostics: {
        currentTurn: run.currentTurn,
        version: run.version,
        pendingChoiceSetId: run.pendingChoiceSet?.choiceSetId || null,
        choiceHistoryCount: run.choiceHistory?.length || 0,
        storyLedgerCount: run.storyLedger?.length || 0,
        npcMemoryCount: run.npcMemories?.length || 0,
        episodeSummaryCount: run.episodeSummaries?.length || 0,
        unresolvedHookCount: run.unresolvedHooks?.length || 0,
        endingPolicy: "story_driven_soft_horizon",
        hardTurnLimit: false,
        softConvergenceTurn: run.endingWindow?.normalEligibleStart || 30,
        allArcsResolved: (run.arcQuestions || []).length > 0 && run.arcQuestions.every((arc) => arc.status === "resolved"),
        enoughStoryForEnding: (run.storyLedger || []).length >= 8
      },
      endingCandidates: endingConditionReports(run)
    };
  }

  async mutateInventory(ownerId, runId, input) {
    validateResourceId(runId, "runId");
    const request = normalizeInventoryRequest(input);
    const committed = await this.store.commitRunMutation({
      ownerId, runId, expectedRunVersion: request.expectedRunVersion,
      resolve: (run) => resolveInventoryAction(run, request, this.clock())
    });
    return { inventoryAction: committed.inventoryAction, run: publicRun(committed.run) };
  }

  async ambientWander(ownerId, runId, input) {
    validateResourceId(runId, "runId");
    assert(input && typeof input === "object" && !Array.isArray(input), 400, "AMBIENT_WANDER_INVALID", "A JSON body is required.");
    exactKeys(input, ["expectedRunVersion", "minX", "minY", "maxX", "maxY"], "AMBIENT_WANDER_INVALID");
    for (const key of ["expectedRunVersion", "minX", "minY", "maxX", "maxY"])
      assert(Number.isSafeInteger(input[key]), 400, "AMBIENT_WANDER_INVALID", `${key} must be an integer.`);
    assert(input.minX <= input.maxX && input.minY <= input.maxY, 400, "AMBIENT_WANDER_INVALID", "Active bounds are invalid.");
    const now = this.clock();
    const result = await this.store.commitAmbientWander({
      ownerId, runId, expectedRunVersion: input.expectedRunVersion,
      resolve: (run) => resolveAmbientWander(run, input, now)
    });
    return { run: publicRun(result.run), movedEntityIds: result.movedEntityIds };
  }

  async submitTurn(ownerId, runId, input) {
    validateResourceId(runId, "runId");
    const request = normalizeTurnRequest(input);
    const requestHash = turnFingerprint(request);
    return this._runIdempotent({
      ownerId,
      operation: `turn:${runId}`,
      idempotencyKey: request.idempotencyKey,
      requestFingerprint: requestHash
    }, () => this._runLlmTurn(() => this._submitSkillTurn(ownerId, runId, request, null, request.preparedD20)));
  }

  async prepareD20(ownerId, runId, input) {
    validateResourceId(runId, "runId");
    assert(input && typeof input === "object" && !Array.isArray(input), 400, "D20_PREPARE_INVALID", "A JSON body is required.");
    exactKeys(input, ["expectedRunVersion"], "D20_PREPARE_INVALID");
    assert(Number.isSafeInteger(input.expectedRunVersion) && input.expectedRunVersion >= 1, 400, "RUN_VERSION_INVALID", "expectedRunVersion must be a positive integer.");
    const snapshot = await this.store.getRun(ownerId, runId);
    if (snapshot.version !== input.expectedRunVersion) throw new AppError(409, "RUN_VERSION_CONFLICT", "The run version is stale.", { currentVersion: snapshot.version });
    return { d20: this._prepareD20(snapshot), expectedRunVersion: snapshot.version };
  }

  async submitChoice(ownerId, runId, input) {
    validateResourceId(runId, "runId");
    const selection = normalizeChoiceSelectionRequest(input);
    const requestHash = choiceSelectionFingerprint(selection);
    return this._runIdempotent({
      ownerId,
      operation: `turn:${runId}`,
      idempotencyKey: selection.idempotencyKey,
      requestFingerprint: requestHash
    }, () => this._runLlmTurn(() => this._submitChoiceOnce(ownerId, runId, selection, requestHash)));
  }

  async _submitChoiceOnce(ownerId, runId, selection, requestHash) {
    const existing = await this.store.findTurnByIdempotency?.(ownerId, runId, selection.idempotencyKey);
    if (existing) {
      if (existing.requestFingerprint !== requestHash) throw new AppError(409, "IDEMPOTENCY_CONFLICT", "The idempotency key was already used with a different payload.");
      return { turn: publicTurn(existing), run: publicRun(await this.store.getRun(ownerId, runId)), fromIdempotencyCache: true };
    }
    const snapshot = await this.store.getRun(ownerId, runId);
    if (snapshot.version !== selection.expectedRunVersion) throw new AppError(409, "RUN_VERSION_CONFLICT", "The run version is stale.", { currentVersion: snapshot.version });
    const choice = selectedChoiceForRun(snapshot, selection);
    if (choice.choiceKind === "TRAVEL") throw new AppError(422, "CHOICE_TRAVEL_NOT_ENABLED", "Narrative travel choices are not enabled until a server-issued destination transition can be committed.");
    if (choice.choiceKind === "SKILL") {
      const targetIds = choice.skillId === "CONNECT"
        ? (choice.targetEntityId ? [choice.targetEntityId, snapshot.playerEntityId] : [])
        : (choice.targetEntityId ? [choice.targetEntityId] : []);
      const request = normalizeTurnRequest({
        inputType: "USE_SKILL",
        idempotencyKey: selection.idempotencyKey,
        expectedRunVersion: selection.expectedRunVersion,
        skillId: choice.skillId,
        targetIds,
        destination: null
      });
      request.intent = choice.text;
      request.abilitySource = "server_sealed_choice";
      request.resolutionMode = "D20";
      request.narrativeChoice = { choiceSetId: selection.choiceSetId, ...cloneChoice(choice) };
      request.choiceRequestFingerprint = requestHash;
      return this._submitSkillTurn(ownerId, runId, request, snapshot, selection.preparedD20);
    }
    const request = narrativeChoiceRequest({ selection, choice, requestFingerprint: requestHash });
    return this._submitPureNarrativeChoice(ownerId, runId, request, snapshot);
  }

  async submitPlayerMessage(ownerId, runId, input) {
    validateResourceId(runId, "runId");
    const message = normalizePlayerMessageRequest(input);
    const requestHash = playerMessageFingerprint(message);
    return this._runIdempotent({
      ownerId,
      operation: `turn:${runId}`,
      idempotencyKey: message.idempotencyKey,
      requestFingerprint: requestHash,
      // A natural-language message can now resolve to either a campaign turn or
      // an authoritative navigation ledger. Persist the discriminated response
      // so both shapes replay safely under the message fingerprint.
      persistResponse: true,
      markReplay: true
    }, () => this._runLlmTurn(() => this._submitPlayerMessageOnce(ownerId, runId, message, requestHash)));
  }

  async _submitPlayerMessageOnce(ownerId, runId, message, requestHash) {
    const existing = await this.store.findTurnByIdempotency?.(ownerId, runId, message.idempotencyKey);
    if (existing) {
      if (existing.requestFingerprint !== requestHash) throw new AppError(409, "IDEMPOTENCY_CONFLICT", "The idempotency key was already used with a different payload.");
      return { turn: publicTurn(existing), run: publicRun(await this.store.getRun(ownerId, runId)), fromIdempotencyCache: true };
    }
    const snapshot = await this.store.getRun(ownerId, runId);
    if (snapshot.version !== message.expectedRunVersion) throw new AppError(409, "RUN_VERSION_CONFLICT", "The run version is stale.", { currentVersion: snapshot.version });
    if (snapshot.activeEncounter?.reason === "opening_keyboard_tutorial" &&
        snapshot.pendingChoiceSet?.choices?.some((choice) => choice.choiceId === "opening.attack")) {
      throw new AppError(409, "OPENING_TUTORIAL_ACTION_REQUIRED",
        "첫 전투에서는 R 키로 관리자 키보드의 공격을 먼저 실행해야 합니다.", {
          choiceSetId: snapshot.pendingChoiceSet.choiceSetId,
          requiredChoiceId: "opening.attack",
          requiredSkillId: "DELETE"
        });
    }
    if (isMonsterEncounterRequest(message.text)) {
      const request = playerMessageRequest({ run: snapshot, message, requestFingerprint: requestHash });
      request.narrativeChoice = {
        choiceSetId: snapshot.pendingChoiceSet?.choiceSetId || deterministicUuid(`${snapshot.id}:freeform:${snapshot.currentTurn}`),
        choiceId: "player.freeform",
        text: message.text,
        choiceKind: "DIALOGUE",
        intentTag: "ASSERTIVE",
        resolutionMode: "NONE",
        skillId: null,
        targetEntityId: null,
        destinationRef: null
      };
      request.targetEntityId = null;
      return this._submitPureNarrativeChoice(ownerId, runId, request, snapshot);
    }
    const proposalContext = playerActionContext(snapshot, message.text);
    const requestedDestination = requestedPlayerMovementDestination(proposalContext);
    if (requestedDestination?.travelMode === "SAFE_TRAVEL") {
      const destinationPoint = resolvePlayerActionDestination(snapshot, requestedDestination.ref);
      assert(destinationPoint, 422, "TRAVEL_PATH_BLOCKED", "The named destination has no unoccupied walkable arrival tile.");
      const travelRequest = normalizeTravelRequest({
        inputType: "MOVE",
        idempotencyKey: message.idempotencyKey,
        expectedRunVersion: message.expectedRunVersion,
        destination: {
          areaId: requestedDestination.ref.slice("area:".length),
          x: destinationPoint.x,
          y: destinationPoint.y
        },
        playerNote: message.text.slice(0, 240)
      });
      // A named world destination is navigation, not a D20 combat move. Commit it
      // through the same authoritative path/gate transaction used by map clicks so
      // prose can never move farther than the stored coordinates.
      return this._travelOnce(ownerId, runId, travelRequest, travelFingerprint(travelRequest));
    }
    let actionProposal;
    try {
      const proposed = requestedDestination
        ? {
          kind: "MOVE", targetEntityIds: [], itemIds: [], destinationRef: requestedDestination.ref,
          resultItem: null, reason: "플레이어가 월드의 명시된 목적지를 지목했다."
        }
        : playerTextRequestsMovement(proposalContext.playerText)
          ? fallbackPlayerActionProposal(proposalContext)
          : typeof this.narrator?.planPlayerAction === "function"
          ? await this.narrator.planPlayerAction(proposalContext)
          : fallbackPlayerActionProposal(proposalContext);
      actionProposal = proposed?.requiresRoll === undefined
        ? validatePlayerActionProposal(proposed, proposalContext)
        : proposed;
    } catch (error) {
      this.logger?.warn?.({ event: "player_action_proposal_fallback", category: error?.code || "unexpected" });
      actionProposal = fallbackPlayerActionProposal(proposalContext);
    }
    if (actionProposal.requiresRoll) {
      const skillId = actionProposal.kind === "ACQUIRE" ? "SEARCH" : actionProposal.kind;
      const destinationPoint = actionProposal.destinationRef === null
        ? null
        : resolvePlayerActionDestination(snapshot, actionProposal.destinationRef);
      const request = normalizeTurnRequest({
        inputType: "USE_SKILL",
        idempotencyKey: message.idempotencyKey,
        expectedRunVersion: message.expectedRunVersion,
        skillId,
        targetIds: actionProposal.targetEntityIds,
        destination: destinationPoint ? { x: destinationPoint.x, y: destinationPoint.y } : null,
        playerNote: narrationNoteFromPlayerText(message.text),
        itemIds: actionProposal.itemIds,
        actionProposal
      });
      request.intent = message.text;
      request.abilitySource = "player_freeform";
      request.resolutionMode = "D20";
      request.narrativeChoice = {
        choiceSetId: snapshot.pendingChoiceSet?.choiceSetId || deterministicUuid(`${snapshot.id}:freeform:${snapshot.currentTurn}`),
        choiceId: "player.freeform",
        text: message.text,
        choiceKind: "SKILL",
        intentTag: actionProposal.kind === "ATTACK" ? "ASSERTIVE" : actionProposal.kind === "NEGOTIATE" ? "EMPATHETIC" : "INVESTIGATE",
        resolutionMode: "D20",
        skillId,
        targetEntityId: actionProposal.targetEntityIds[0] || null,
        destinationRef: actionProposal.destinationRef
      };
      request.choiceRequestFingerprint = requestHash;
      try {
        return await this._submitSkillTurn(ownerId, runId, request, snapshot, message.preparedD20);
      } catch (error) {
        if (!(error instanceof AppError) || error.status !== 422) throw error;
        // A legal-boundary rejection is still a meaningful player attempt. Keep
        // authoritative state unchanged, but commit the failed attempt so the
        // narrator can explain it naturally instead of surfacing an HTTP error.
        this.logger?.info?.({ event: "player_action_rejected_as_narrative", code: error.code, actionKind: actionProposal.kind });
        const rejectedRequest = playerMessageRequest({ run: snapshot, message, requestFingerprint: requestHash });
        rejectedRequest.narrativeChoice.choiceKind = "ATTITUDE";
        rejectedRequest.narrativeChoice.intentTag = "INVESTIGATE";
        rejectedRequest.actionProposal = actionProposal;
        rejectedRequest.rejectedAction = {
          kind: actionProposal.kind,
          code: error.code,
          reason: playerActionRejectionReason(actionProposal, proposalContext, error.code),
          itemNames: actionProposal.itemIds.map((itemId) => proposalContext.inventory.find((item) => item.id === itemId)?.name).filter(Boolean)
        };
        return this._submitPureNarrativeChoice(ownerId, runId, rejectedRequest, snapshot);
      }
    }
    const request = playerMessageRequest({ run: snapshot, message, requestFingerprint: requestHash });
    request.actionProposal = actionProposal;
    if (actionProposal.rejectedAction) request.rejectedAction = structuredClone(actionProposal.rejectedAction);
    return this._submitPureNarrativeChoice(ownerId, runId, request, snapshot);
  }

  async _submitSkillTurn(ownerId, runId, request, knownSnapshot = null, preparedD20 = null) {
    const requestHash = turnFingerprint(request);
    const existing = await this.store.findTurnByIdempotency?.(ownerId, runId, request.idempotencyKey);
    if (existing) {
      if (existing.requestFingerprint !== requestHash) throw new AppError(409, "IDEMPOTENCY_CONFLICT", "The idempotency key was already used with a different payload.");
      return { turn: publicTurn(existing), run: publicRun(await this.store.getRun(ownerId, runId)), fromIdempotencyCache: true };
    }

    const snapshot = knownSnapshot || await this.store.getRun(ownerId, runId);
    if (snapshot.version !== request.expectedRunVersion) throw new AppError(409, "RUN_VERSION_CONFLICT", "The run version is stale.", { currentVersion: snapshot.version });
    if (snapshot.pendingChoiceSet && !request.narrativeChoice) {
      throw new AppError(409, "CHOICE_REQUIRED", "The current story boundary requires one server-sealed narrative choice.", {
        choiceSetId: snapshot.pendingChoiceSet.choiceSetId,
        currentVersion: snapshot.version
      });
    }
    const now = this.clock();
    const authoritativeD20 = this._authoritativeD20(snapshot, preparedD20);
    // Preflight mechanics are deterministic and read-only. They define exactly what
    // the proposal engine may see; the same roll is forced into the commit plan.
    const preview = resolveTurn({ run: snapshot, request, d20Source: this.d20Source, forcedD20: authoritativeD20, now });
    const isPlayerFreeform = request.abilitySource === "player_freeform";
    const hasExplicitSelection = Boolean(request.targetEntityId || request.secondaryTargetEntityId || request.destination);
    const targetDecision = !hasExplicitSelection && request.actionProposal?.kind !== "ACQUIRE" && ["COPY", "DELETE", "CONNECT", "RESTORE", "UNDO", "SEARCH", "SELECT_ALL"].includes(request.skillId)
      ? await planSkillTarget({ narrator: this.narrator, run: snapshot, request, d20: preview.turn.d20, logger: this.logger })
      : null;
    const skillSelection = targetDecision?.selection || null;
    const targetedPreview = resolveTurn({ run: snapshot, request, forcedD20: preview.turn.d20, now, skillSelection });
    const arcPreview = targetedPreview;
    const sceneDecision = isPlayerFreeform
      ? null
      : await planDecisionScene({ narrator: this.narrator, run: arcPreview.run, decisionType: "ACTION", turn: arcPreview.turn, logger: this.logger });
    // Resolve the selected legal scene on a disposable clone before narration.
    // The model therefore describes confirmed results and can no longer propose
    // mechanics after seeing the outcome.
    const resolvedPreview = resolveTurn({
      run: snapshot, request, forcedD20: preview.turn.d20, now,
      directorOutput: { proposedOps: [] }, sceneDecision, skillSelection
    });
    const context = directorContext(resolvedPreview.run, resolvedPreview.turn);
    context.allowedEffects = [];
    context.consequenceBudget = 0;
    let directorOutput;
    if (request.skillId === "INTERACT") {
      const target = snapshot.entities.find((item) => item.id === request.targetEntityId);
      const isCrate = String(target?.assetId || "").startsWith("item.crate");
      const isBook = String(target?.assetId || "").includes("book");
      const alreadyInteracted = target?.state?.interacted === true;
      directorOutput = {
        summary: `${target?.name || "대상"} 상호작용`,
        body: isCrate
          ? (alreadyInteracted ? "이미 확인한 상자다. 안에는 더 이상 가져갈 보급품이 없다." : "상자를 열어 안쪽을 확인했다. 남아 있던 보급품이 집중력을 조금 회복시킨다.")
          : isBook
            ? "책장을 넘겨 기록을 확인했다. 이 물건은 이제 조사한 대상으로 남는다."
            : `${target?.name || "대상"}에게 가까이 다가가 상태와 반응을 확인했다.`,
        dialogue: [], proposedOps: [], elementalEffectId: null,
        nextIntervention: { reason: "상호작용이 끝났다. 다음 반응을 선택한다.", choices: choicesFromLegacySkills([], "SEARCH") },
        fallbackUsed: false, model: "deterministic-interaction-v1"
      };
    } else try {
      const candidate = await this.narrator.narrate(context);
      const validated = validateNarrationOutput({ summary: candidate.summary, body: candidate.body, dialogue: candidate.dialogue, storySequence: candidate.storySequence, nextIntervention: candidate.nextIntervention, proposedOps: candidate.proposedOps, elementalEffectId: candidate.elementalEffectId }, context);
      directorOutput = { ...validated, fallbackUsed: candidate.fallbackUsed === true, model: candidate.model || "validated-custom-narrator" };
    } catch (error) {
      this.logger?.warn?.({ event: "director_fallback", category: error?.code || "unexpected" });
      directorOutput = createFallbackNarration(context);
    }
    directorOutput = sealDirectorOutputOrFallback(
      directorOutput,
      context,
      resolvedPreview.run,
      this.logger,
      "director_seal_fallback"
    );
    const committed = await this.store.commitTurn({
      ownerId,
      runId,
      idempotencyKey: request.idempotencyKey,
      requestFingerprint: requestHash,
      expectedRunVersion: request.expectedRunVersion,
      resolve: (run) => resolveTurn({ run, request, forcedD20: preview.turn.d20, now, directorOutput, sceneDecision, skillSelection })
    });
    return { turn: publicTurn(committed.turn), run: publicRun(committed.run), fromIdempotencyCache: committed.fromIdempotencyCache };
  }

  async _submitPureNarrativeChoice(ownerId, runId, request, knownSnapshot = null) {
    const requestHash = turnFingerprint(request);
    const existing = await this.store.findTurnByIdempotency?.(ownerId, runId, request.idempotencyKey);
    if (existing) {
      if (existing.requestFingerprint !== requestHash) throw new AppError(409, "IDEMPOTENCY_CONFLICT", "The idempotency key was already used with a different payload.");
      return { turn: publicTurn(existing), run: publicRun(await this.store.getRun(ownerId, runId)), fromIdempotencyCache: true };
    }
    const snapshot = knownSnapshot || await this.store.getRun(ownerId, runId);
    if (snapshot.version !== request.expectedRunVersion) throw new AppError(409, "RUN_VERSION_CONFLICT", "The run version is stale.", { currentVersion: snapshot.version });
    const now = this.clock();
    const preview = resolveNarrativeChoice({ run: snapshot, request, now, directorOutput: { proposedOps: [] } });
    const arcPreview = preview;
    const isPlayerFreeform = request.abilitySource === "player_freeform";
    // Free-form chat is already the scene-driving input. Running it through the
    // action candidate planner first can replace a direct NPC reply with an
    // unrelated world action. Keep that planner for suggested choices only.
    const sceneDecision = isPlayerFreeform ? null : await planDecisionScene({ narrator: this.narrator, run: arcPreview.run, decisionType: "ACTION", turn: arcPreview.turn, logger: this.logger });
    const resolvedPreview = resolveNarrativeChoice({
      run: snapshot, request, now, directorOutput: { proposedOps: [] }, sceneDecision
    });
    const context = directorContext(resolvedPreview.run, resolvedPreview.turn);
    context.allowedEffects = [];
    context.consequenceBudget = 0;
    let directorOutput;
    try {
      const candidate = await this.narrator.narrate(context);
      const validated = validateNarrationOutput({
        summary: candidate.summary,
        body: candidate.body,
        dialogue: candidate.dialogue,
        storySequence: candidate.storySequence,
        nextIntervention: candidate.nextIntervention,
        // Risu-style conversation may imagine freely, but prose cannot mutate
        // authoritative game state. Discard mechanical proposals at this boundary.
        proposedOps: isPlayerFreeform ? [] : candidate.proposedOps,
        elementalEffectId: isPlayerFreeform ? null : candidate.elementalEffectId
      }, context);
      const presentation = isPlayerFreeform ? alignFreeformDialogue(validated) : validated;
      directorOutput = { ...presentation, fallbackUsed: candidate.fallbackUsed === true, model: candidate.model || "validated-custom-narrator" };
    } catch (error) {
      this.logger?.warn?.({ event: "narrative_choice_director_fallback", category: error?.code || "unexpected" });
      directorOutput = createFallbackNarration(context);
    }
    directorOutput = sealDirectorOutputOrFallback(
      directorOutput,
      context,
      resolvedPreview.run,
      this.logger,
      "narrative_choice_director_seal_fallback"
    );
    const committed = await this.store.commitTurn({
      ownerId,
      runId,
      idempotencyKey: request.idempotencyKey,
      requestFingerprint: requestHash,
      expectedRunVersion: request.expectedRunVersion,
      resolve: (run) => resolveNarrativeChoice({ run, request, now, directorOutput, sceneDecision })
    });
    return { turn: publicTurn(committed.turn), run: publicRun(committed.run), fromIdempotencyCache: committed.fromIdempotencyCache };
  }

  async travel(ownerId, runId, input) {
    validateResourceId(runId, "runId");
    const request = normalizeTravelRequest(input);
    const requestHash = travelFingerprint(request);
    return this._runIdempotent({
      ownerId,
      operation: `travel:${runId}`,
      idempotencyKey: request.idempotencyKey,
      requestFingerprint: requestHash
    }, () => this._travelOnce(ownerId, runId, request, requestHash));
  }

  async _travelOnce(ownerId, runId, request, requestHash) {
    const existing = await this.store.findNavigationByIdempotency?.(ownerId, runId, request.idempotencyKey);
    if (existing) {
      if (existing.requestFingerprint !== requestHash) throw new AppError(409, "IDEMPOTENCY_CONFLICT", "The travel idempotency key was already used with a different payload.");
      return { navigation: existing, run: publicRun(await this.store.getRun(ownerId, runId)), fromIdempotencyCache: true };
    }
    const snapshot = await this.store.getRun(ownerId, runId);
    if (snapshot.version !== request.expectedRunVersion) throw new AppError(409, "RUN_VERSION_CONFLICT", "The run version is stale.", { currentVersion: snapshot.version });
    // Narrative choices are optional branches. Travel atomically dismisses an
    // unanswered set so one finished turn never locks exploration.
    const now = this.clock();
    const preview = resolveSafeTravel({ run: snapshot, request, d20Source: this.d20Source, now });
    // Safe travel is a non-consuming navigation transaction, so it must never
    // wait for a remote scene planner. The same server-owned candidate system
    // supplies a bounded deterministic scene and preserves the narrative/event
    // contract without putting authoritative coordinates behind an LLM timeout.
    const sceneDecision = preview.run.storyEventDue || preview.navigation.encounterOpened
      ? planDeterministicDecisionScene({ run: preview.run, decisionType: "TRAVEL", navigation: preview.navigation, logger: this.logger })
      : null;
    const committed = await this.store.commitNavigation({
      ownerId, runId, idempotencyKey: request.idempotencyKey, requestFingerprint: requestHash, expectedRunVersion: request.expectedRunVersion,
      resolve: (run) => resolveTravelDecision({ run, request, d20Source: this.d20Source, sceneDecision, now })
    });
    return { navigation: committed.navigation, run: publicRun(committed.run), fromIdempotencyCache: committed.fromIdempotencyCache };
  }

  async getTurn(ownerId, runId, turnNo) {
    validateResourceId(runId, "runId");
    return publicTurn(await this.store.getTurn(ownerId, runId, turnNo));
  }

  async listTurns(ownerId, runId) {
    validateResourceId(runId, "runId");
    return (await this.store.listTurns(ownerId, runId)).map(publicTurn);
  }

  async abandonRun(ownerId, runId, input) {
    validateResourceId(runId, "runId");
    const expected = lifecycleVersion(input);
    return publicRun(await this.store.abandonRun(ownerId, runId, expected, this.clock()));
  }

  async resumeRun(ownerId, runId, input) {
    validateResourceId(runId, "runId");
    const expected = lifecycleVersion(input);
    return publicRun(await this.store.resumeRun(ownerId, runId, expected, this.clock()));
  }

  async narrate(input) {
    return this.narrator.narrate(validateNarrationContext(input));
  }

  /**
   * SCENE_TRANSITION_PLAN: providers without a scene director (e.g. Gemini today) degrade to the
   * deterministic first-candidate plan, so the endpoint always answers with a valid v1.0 plan.
   */
  async planSceneTransition(input) {
    const request = validateSceneTransitionRequest(input);
    if (typeof this.narrator?.planSceneTransition !== "function") {
      return createFallbackScenePlan(request, "provider_unsupported");
    }
    return this.narrator.planSceneTransition(request);
  }
}

function resolveAmbientWander(run, bounds, now) {
  assert(run.status === "active", 409, "RUN_NOT_ACTIVE", "The run does not accept ambient movement.");
  const nowMs = Date.parse(now);
  const movedEntityIds = [];
  const directions = [[1, 0], [0, 1], [-1, 0], [0, -1]];
  const occupied = (point, exceptId) => run.entities.some((item) => item.active && item.blocking && item.id !== exceptId && item.position.x === point.x && item.position.y === point.y);
  const npcs = run.entities.filter((item) => item.active && item.kind === "npc" &&
    !item.state?.adminAccessLevelId).sort((a, b) => a.id.localeCompare(b.id));
  for (const npc of npcs) {
    if (npc.position.x < bounds.minX || npc.position.x > bounds.maxX || npc.position.y < bounds.minY || npc.position.y > bounds.maxY) continue;
    npc.state ||= {};
    npc.state.wanderOrigin ||= { ...npc.position };
    const dueAt = Date.parse(npc.state.nextWanderAt || "");
    if (Number.isFinite(dueAt) && nowMs < dueAt) continue;
    const seed = [...npc.id].reduce((sum, character) => sum + character.charCodeAt(0), 0);
    const offset = (seed + Math.floor(nowMs / 1000)) & 3;
    for (let index = 0; index < directions.length; index += 1) {
      const [dx, dy] = directions[(offset + index) & 3];
      const destination = { x: npc.position.x + dx, y: npc.position.y + dy };
      const distance = Math.abs(destination.x - npc.state.wanderOrigin.x) + Math.abs(destination.y - npc.state.wanderOrigin.y);
      if (distance > 2 || !isWalkable(run.world, destination) || occupied(destination, npc.id)) continue;
      npc.position = destination;
      npc.state.nextWanderAt = new Date(nowMs + 1400 + ((seed + nowMs) % 1100)).toISOString();
      movedEntityIds.push(npc.id);
      break;
    }
  }
  if (movedEntityIds.length > 0) {
    run.version += 1;
    run.updatedAt = now;
  }
  return { run, movedEntityIds };
}

function lifecycleVersion(input) {
  assert(input && typeof input === "object" && !Array.isArray(input), 400, "RUN_REQUEST_INVALID", "A JSON body is required.");
  exactKeys(input, ["expectedRunVersion"], "RUN_REQUEST_INVALID");
  assert(Number.isSafeInteger(input.expectedRunVersion) && input.expectedRunVersion >= 1, 400, "RUN_VERSION_INVALID", "expectedRunVersion must be a positive integer.");
  return input.expectedRunVersion;
}

function normalizeCreationIdempotencyKey(value) {
  if (value === undefined) return null;
  assert(typeof value === "string" && IDEMPOTENCY_PATTERN.test(value), 400, "IDEMPOTENCY_KEY_INVALID", "idempotencyKey must be 8-128 safe characters.");
  return value;
}

function deterministicWorldSeed(value) {
  return (Number.parseInt(fingerprint(value).slice(0, 8), 16) % 2_147_483_646) + 1;
}

async function findExisting(loader) {
  try {
    return await loader();
  } catch (error) {
    if (error instanceof AppError && error.code === "NOT_FOUND") return null;
    throw error;
  }
}

function delay(milliseconds) {
  return new Promise((resolve) => setTimeout(resolve, milliseconds));
}

function normalizeThemeHint(value) {
  if (value === undefined || value === null) return null;
  assert(typeof value === "string", 400, "THEME_HINT_INVALID", "themeHint must be a string.");
  const normalized = value.trim();
  assert(normalized.length >= 1 && normalized.length <= 120, 400, "THEME_HINT_INVALID", "themeHint must contain 1-120 characters.");
  assert(!/[\u0000-\u001f\u007f]/u.test(normalized), 400, "THEME_HINT_INVALID", "themeHint must be a single plain-text line.");
  return normalized;
}

function publicCampaign(campaign) {
  return {
    id: campaign.id,
    title: campaign.title,
    gameTitle: campaign.gameTitle,
    worldId: campaign.worldId,
    generatedTitle: campaign.generatedTitle,
    generatedTitleKo: campaign.generatedTitleKo,
    worldSeed: campaign.worldSeed,
    turnLimit: campaign.turnLimit,
    status: campaign.status,
    templateId: campaign.templateId,
    templateVersion: campaign.templateVersion,
    archetype: campaign.archetype,
    baseArchetype: campaign.baseArchetype,
    variant: campaign.variant,
    premise: campaign.premise,
    premiseKo: campaign.premiseKo,
    tone: campaign.tone,
    worldName: campaign.worldName || campaign.genome?.worldName || campaign.scenarioPlan?.genome?.worldName,
    protagonistId: campaign.protagonistId,
    protagonistName: campaign.protagonistName,
    artifactId: campaign.artifactId,
    artifactName: campaign.artifactName,
    adminAccessLevels: campaign.adminAccessLevels,
    regionAxes: campaign.regionAxes,
    generationMetadata: campaign.generationMetadata || campaign.progressionMetadata || campaign.scenarioPlan?.generationMetadata,
    genome: campaign.genome || campaign.scenarioPlan?.genome,
    questSeeds: campaign.questSeeds || campaign.scenarioPlan?.questSeeds || [],
    initialQuests: campaign.initialQuests || campaign.questSeeds || campaign.scenarioPlan?.questSeeds || [],
    contentHash: campaign.contentHash || campaign.scenarioPlan?.contentHash,
    areaFlavors: campaign.areaFlavors || campaign.scenarioPlan?.areaFlavors || [],
    npcRoles: campaign.npcRoles,
    requiredStoryBeats: campaign.requiredStoryBeats,
    arcQuestions: campaign.arcQuestions || [],
    campaignMacroPhases: campaign.campaignMacroPhases || [],
    endingCandidates: campaign.endingCandidates.map((item) => item.title),
    endingCandidateDetails: campaign.endingCandidates,
    world: publicWorld(campaign.world),
    createdAt: campaign.createdAt,
    updatedAt: campaign.updatedAt
  };
}

function campaignProposalOnly(candidate) {
  return {
    campaign: candidate.campaign,
    beats: candidate.beats,
    npcs: candidate.npcs,
    quests: candidate.quests,
    endings: candidate.endings,
    areaFlavors: candidate.areaFlavors
  };
}

function validateResourceId(value, name) {
  assert(typeof value === "string" && UUID_PATTERN.test(value), 400, "RESOURCE_ID_INVALID", `${name} must be a UUID.`);
}
