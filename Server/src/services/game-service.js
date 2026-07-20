import { randomInt, randomUUID } from "node:crypto";
import { assert, AppError } from "../errors.js";
import { createCampaignBlueprint } from "../domain/campaign.js";
import { generateWorld, publicWorld } from "../domain/world.js";
import { DeterministicD20Source, createRunState, directorContext, normalizeTravelRequest, normalizeTurnRequest, publicRun, publicTurn, resolveSafeTravel, resolveTurn, travelFingerprint, turnFingerprint } from "../domain/turn-engine.js";
import { planDecisionScene, resolveTravelDecision } from "../domain/decision-orchestrator.js";
import { applyCampaignPlanEnrichment, createCampaignPlanContext, createCampaignPlanFallback, createDeterministicCampaignPreview, validateCampaignPlanOutput } from "../llm/campaign-planning.js";
import { createFallbackNarration, validateNarrationContext, validateNarrationOutput } from "../llm/narration.js";
import { createFallbackScenePlan, validateSceneTransitionRequest } from "../llm/scene-transition.js";
import { PRODUCT_CONTRACT } from "../domain/codria-contract.js";

const UUID_PATTERN = /^[0-9a-f]{8}-[0-9a-f]{4}-[1-8][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i;

function exactKeys(object, allowed, code) {
  const unknown = Object.keys(object).filter((key) => !allowed.includes(key));
  assert(unknown.length === 0, 400, code, `Unknown fields: ${unknown.join(", ")}.`);
}

export class GameService {
  constructor({ store, narrator, d20Source = new DeterministicD20Source(), worldGenerator = generateWorld, clock = () => new Date().toISOString(), logger = console }) {
    this.store = store;
    this.narrator = narrator;
    this.d20Source = d20Source;
    this.worldGenerator = worldGenerator;
    this.clock = clock;
    this.logger = logger;
  }

  async health() {
    const storage = await this.store.health();
    return {
      status: "ok",
      service: "codria-v4-game-server",
      storage: storage.storage,
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
      canonicalInputTypes: ["MOVE", "USE_SKILL"],
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
    exactKeys(input, ["title", "worldSeed", "turnLimit", "archetype"], "CAMPAIGN_REQUEST_INVALID");
    const worldSeed = input.worldSeed === undefined ? randomInt(1, 2_147_483_647) : input.worldSeed;
    assert(Number.isSafeInteger(worldSeed), 400, "WORLD_SEED_INVALID", "worldSeed must be a safe integer.");
    const turnLimit = input.turnLimit === undefined ? 40 : input.turnLimit;
    assert(Number.isInteger(turnLimit) && turnLimit >= 30 && turnLimit <= 50, 400, "TURN_LIMIT_INVALID", "turnLimit must be between 30 and 50.");
    const { blueprint, world } = await this.generateCampaignContent({ worldSeed, turnLimit, archetype: input.archetype, enrichWithLlm: false });
    const title = input.title === undefined ? blueprint.generatedTitle : input.title;
    assert(typeof title === "string" && title.trim().length >= 1 && title.trim().length <= 100, 400, "CAMPAIGN_TITLE_INVALID", "title must contain 1-100 characters.");
    const now = this.clock();
    const campaign = {
      id: randomUUID(), ownerId, title: title.trim(), worldSeed, turnLimit, status: "active",
      ...blueprint,
      world,
      createdAt: now, updatedAt: now
    };
    return publicCampaign(await this.store.createCampaign(campaign));
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
    exactKeys(input, ["worldSeed", "turnLimit", "themeHint"], "RUN_REQUEST_INVALID");
    const template = await this.store.getCampaign(ownerId, campaignId);
    assert(template.status === "active", 409, "CAMPAIGN_NOT_ACTIVE", "Campaign does not accept new runs.");
    const worldSeed = input.worldSeed === undefined ? randomInt(1, 2_147_483_647) : input.worldSeed;
    assert(Number.isSafeInteger(worldSeed), 400, "WORLD_SEED_INVALID", "worldSeed must be a safe integer.");
    const turnLimit = input.turnLimit === undefined ? template.turnLimit : input.turnLimit;
    assert(Number.isInteger(turnLimit) && turnLimit >= 30 && turnLimit <= 50, 400, "TURN_LIMIT_INVALID", "turnLimit must be between 30 and 50.");
    const themeHint = normalizeThemeHint(input.themeHint);
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
    const run = createRunState({ campaign, ownerId, now: this.clock() });
    return publicRun(await this.store.createRun(run));
  }

  async getRun(ownerId, runId) {
    validateResourceId(runId, "runId");
    return publicRun(await this.store.getRun(ownerId, runId));
  }

  async submitTurn(ownerId, runId, input) {
    validateResourceId(runId, "runId");
    const request = normalizeTurnRequest(input);
    const requestHash = turnFingerprint(request);
    const existing = await this.store.findTurnByIdempotency?.(ownerId, runId, request.idempotencyKey);
    if (existing) {
      if (existing.requestFingerprint !== requestHash) throw new AppError(409, "IDEMPOTENCY_CONFLICT", "The idempotency key was already used with a different payload.");
      return { turn: publicTurn(existing), run: publicRun(await this.store.getRun(ownerId, runId)), fromIdempotencyCache: true };
    }

    const snapshot = await this.store.getRun(ownerId, runId);
    if (snapshot.version !== request.expectedRunVersion) throw new AppError(409, "RUN_VERSION_CONFLICT", "The run version is stale.", { currentVersion: snapshot.version });
    const now = this.clock();
    // Preflight mechanics are deterministic and read-only. They define exactly what
    // the proposal engine may see; the same roll is forced into the commit plan.
    const preview = resolveTurn({ run: snapshot, request, d20Source: this.d20Source, now });
    const sceneDecision = await planDecisionScene({ narrator: this.narrator, run: preview.run, decisionType: "ACTION", turn: preview.turn, logger: this.logger });
    // Resolve the selected legal scene on a disposable clone before narration.
    // The model therefore describes confirmed results and can no longer propose
    // mechanics after seeing the outcome.
    const resolvedPreview = resolveTurn({
      run: snapshot, request, forcedD20: preview.turn.d20, now,
      directorOutput: { proposedOps: [] }, sceneDecision
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
        dialogue: [], proposedOps: [], fallbackUsed: false, model: "deterministic-interaction-v1"
      };
    } else try {
      const candidate = await this.narrator.narrate(context);
      const validated = validateNarrationOutput({ summary: candidate.summary, body: candidate.body, dialogue: candidate.dialogue, proposedOps: candidate.proposedOps }, context);
      directorOutput = { ...validated, fallbackUsed: candidate.fallbackUsed === true, model: candidate.model || "validated-custom-narrator" };
    } catch (error) {
      this.logger?.warn?.({ event: "director_fallback", category: error?.code || "unexpected" });
      directorOutput = createFallbackNarration(context);
    }
    const committed = await this.store.commitTurn({
      ownerId,
      runId,
      idempotencyKey: request.idempotencyKey,
      requestFingerprint: requestHash,
      expectedRunVersion: request.expectedRunVersion,
      resolve: (run) => resolveTurn({ run, request, forcedD20: preview.turn.d20, now, directorOutput, sceneDecision })
    });
    return { turn: publicTurn(committed.turn), run: publicRun(committed.run), fromIdempotencyCache: committed.fromIdempotencyCache };
  }

  async travel(ownerId, runId, input) {
    validateResourceId(runId, "runId");
    const request = normalizeTravelRequest(input);
    const requestHash = travelFingerprint(request);
    const existing = await this.store.findNavigationByIdempotency?.(ownerId, runId, request.idempotencyKey);
    if (existing) {
      if (existing.requestFingerprint !== requestHash) throw new AppError(409, "IDEMPOTENCY_CONFLICT", "The travel idempotency key was already used with a different payload.");
      return { navigation: existing, run: publicRun(await this.store.getRun(ownerId, runId)), fromIdempotencyCache: true };
    }
    const snapshot = await this.store.getRun(ownerId, runId);
    if (snapshot.version !== request.expectedRunVersion) throw new AppError(409, "RUN_VERSION_CONFLICT", "The run version is stale.", { currentVersion: snapshot.version });
    const now = this.clock();
    const preview = resolveSafeTravel({ run: snapshot, request, now });
    const sceneDecision = await planDecisionScene({ narrator: this.narrator, run: preview.run, decisionType: "TRAVEL", navigation: preview.navigation, logger: this.logger });
    const committed = await this.store.commitNavigation({
      ownerId, runId, idempotencyKey: request.idempotencyKey, requestFingerprint: requestHash, expectedRunVersion: request.expectedRunVersion,
      resolve: (run) => resolveTravelDecision({ run, request, sceneDecision, now })
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

function lifecycleVersion(input) {
  assert(input && typeof input === "object" && !Array.isArray(input), 400, "RUN_REQUEST_INVALID", "A JSON body is required.");
  exactKeys(input, ["expectedRunVersion"], "RUN_REQUEST_INVALID");
  assert(Number.isSafeInteger(input.expectedRunVersion) && input.expectedRunVersion >= 1, 400, "RUN_VERSION_INVALID", "expectedRunVersion must be a positive integer.");
  return input.expectedRunVersion;
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
