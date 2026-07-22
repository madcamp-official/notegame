import { buildConsequenceCandidates, materializeProposedSceneActions } from "./consequence-candidates.js";
import { applyScenePlan } from "./consequence-resolver.js";
import { resolveSafeTravel } from "./turn-engine.js";
import { createFallbackScenePlan, validateScenePlan } from "../llm/scene-director.js";

function finalizeSceneDecision({ run, bundle, rawPlan, logger }) {
  let plan;
  try {
    const validated = validateScenePlan({ sceneGoal: rawPlan.sceneGoal, selectedActionIds: rawPlan.selectedActionIds || [], proposedActions: rawPlan.proposedActions || [], dialogue: rawPlan.dialogue || [] }, bundle.context);
    const proposedCandidates = materializeProposedSceneActions(run, validated.proposedActions);
    bundle.candidates.push(...proposedCandidates);
    plan = { ...validated, selectedActionIds: [...validated.selectedActionIds, ...proposedCandidates.map((item) => item.candidateId)].slice(0, 4), fallbackUsed: rawPlan.fallbackUsed === true, model: rawPlan.model || "validated-scene-director" };
  } catch (error) {
    logger?.warn?.({ event: "scene_plan_validation_fallback", category: error?.code || "unexpected" });
    plan = createFallbackScenePlan(bundle.context);
  }
  return { context: bundle.context, candidates: bundle.candidates, plan };
}

export function planDeterministicDecisionScene({ run, decisionType, navigation = null, turn = null, logger = console }) {
  const bundle = buildConsequenceCandidates(run, { decisionType, navigation, turn });
  return finalizeSceneDecision({ run, bundle, rawPlan: createFallbackScenePlan(bundle.context), logger });
}

export async function planDecisionScene({ narrator, run, decisionType, navigation = null, turn = null, logger = console }) {
  const bundle = buildConsequenceCandidates(run, { decisionType, navigation, turn });
  let rawPlan;
  try {
    rawPlan = typeof narrator?.planScene === "function"
      ? await narrator.planScene(bundle.context)
      : createFallbackScenePlan(bundle.context);
  } catch (error) {
    logger?.warn?.({ event: "scene_director_fallback", category: error?.code || "unexpected" });
    rawPlan = createFallbackScenePlan(bundle.context);
  }
  return finalizeSceneDecision({ run, bundle, rawPlan, logger });
}

export function resolveTravelDecision({ run, request, d20Source, sceneDecision, now = new Date().toISOString() }) {
  const committed = resolveSafeTravel({ run, request, d20Source, now });
  const scene = applyScenePlan(committed.run, {
    candidates: sceneDecision.candidates,
    plan: sceneDecision.plan,
    decisionType: "TRAVEL",
    now
  });
  if (committed.run.storyEventDue) {
    committed.run.storyEventSequence = Number(committed.run.storyEventSequence || 0) + 1;
    committed.run.nextStoryEventDistance = Number(committed.run.travelDistance || 0) + 15 +
      (Number.parseInt(committed.run.id.replace(/-/g, "").slice(0, 8), 16) + committed.run.storyEventSequence) % 6;
    committed.run.storyEventDue = false;
  }
  committed.navigation.sceneDecision = scene;
  committed.navigation.sceneSequence = scene.sceneSequence;
  committed.navigation.events = [...(committed.navigation.events || []), ...(committed.events || []), ...scene.events];
  committed.navigation.narrative = {
    summary: scene.sceneGoal,
    body: scene.sceneSequence.map((item) => item.text).filter(Boolean).join(" ") || scene.sceneGoal,
    dialogue: scene.sceneSequence.filter((item) => item.type === "DIALOGUE").map((item) => item.line),
    dialogueDetails: scene.sceneSequence.filter((item) => item.type === "DIALOGUE").map((item) => ({ speakerId: item.speakerId, line: item.line })),
    fallbackUsed: scene.fallbackUsed,
    model: scene.model
  };
  return committed;
}
