export function buildArcResolutionContext(runBefore, runAfter, turn) {
  const question = (runBefore.arcQuestions || []).find((item) => item.status === "active")
    || (runBefore.arcQuestions || []).find((item) => item.status === "pending");
  if (!question || turn.turnNo < question.deadlineTurn) return null;
  const ledger = (runAfter.storyLedger || []).filter((item) => item.turnNo >= question.startTurn && item.turnNo <= turn.turnNo).slice(-12);
  return {
    schemaVersion: "1.0",
    requestType: "ARC_RESOLUTION",
    question: {
      id: question.id,
      order: question.order,
      startTurn: question.startTurn,
      deadlineTurn: question.deadlineTurn,
      text: question.question,
      contextHint: question.contextHint,
      allowedOutcomes: question.allowedOutcomes
    },
    turnNo: turn.turnNo,
    latestAction: { skillId: turn.request?.skillId, outcome: turn.outcome, normalizedAttempt: turn.normalizedAttempt },
    storyLedger: ledger,
    episodeSummaries: (runBefore.episodeSummaries || []).slice(-4),
    resolvedArcOutcomes: (runBefore.resolvedArcOutcomes || []).slice(-4),
    majorChoices: (runAfter.majorChoices || []).slice(-8),
    regionOutcomes: (runAfter.regionOutcomes || []).slice(-6),
    npcMemories: (runAfter.npcMemories || []).filter((item) => !item.expired).slice(-8),
    npcRelationships: (runAfter.npcRelationships || []).slice(0, 16),
    openLoops: (runAfter.openLoops || []).filter((item) => item.status === "open").slice(-8),
    metrics: runAfter.metrics,
    adminAccessHistory: (runAfter.adminAccessAcquisitionHistory || []).slice(-3),
    technicalDebt: (runAfter.technicalDebtEntries || []).filter((item) => item.resolvedAt === null).slice(-8)
  };
}

export function validateArcDecision(raw, context) {
  if (!raw || raw.questionId !== context.question.id) throw new Error("Arc question mismatch.");
  if (!context.question.allowedOutcomes.includes(raw.outcomeId)) throw new Error("Arc outcome is not allowed.");
  const ledgerIds = new Set(context.storyLedger.map((item) => item.id));
  const evidenceIds = [...new Set(Array.isArray(raw.evidenceIds) ? raw.evidenceIds : [])];
  if (evidenceIds.length === 0 || evidenceIds.some((id) => !ledgerIds.has(id))) throw new Error("Arc evidence must reference the story ledger.");
  const summary = String(raw.summary || "").trim();
  if (summary.length < 1 || summary.length > 320) throw new Error("Arc summary is invalid.");
  return { questionId: raw.questionId, outcomeId: raw.outcomeId, evidenceIds, summary, model: raw.model || "validated-arc-resolver" };
}

export async function planArcResolution({ narrator, runBefore, runAfter, turn, logger = console }) {
  const context = buildArcResolutionContext(runBefore, runAfter, turn);
  if (!context || typeof narrator?.resolveArc !== "function") return null;
  try {
    const raw = await narrator.resolveArc(context);
    if (raw?.fallbackUsed === true) return null;
    return validateArcDecision(raw, context);
  } catch (error) {
    logger?.warn?.({ event: "arc_resolution_fallback", category: error?.code || "validation_or_transport" });
    return null;
  }
}
