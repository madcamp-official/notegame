const WORD_PATTERN = /[\p{L}\p{N}_-]{2,}/gu;

function words(value) {
  return new Set(String(value || "").toLowerCase().match(WORD_PATTERN) || []);
}

function compact(value, maximum = 1200) {
  const text = typeof value === "string" ? value : JSON.stringify(value);
  return text.length <= maximum ? text : `${text.slice(0, maximum - 1)}…`;
}

function memoryText(item) {
  return [item?.summary, item?.text, item?.choiceText, item?.question, item?.outcomeId,
    ...(Array.isArray(item?.narrativeFragments) ? item.narrativeFragments : [])]
    .filter(Boolean).join(" ");
}

export function selectRelevantMemories(context, { maxItems = 10, maxChars = 5000 } = {}) {
  const query = words([
    context?.intent, context?.normalizedAttempt, context?.area, context?.currentArcQuestion?.question,
    context?.selectedChoice?.text, context?.selectedChoice?.intentTag,
    ...(context?.openLoops || []).map(memoryText)
  ].filter(Boolean).join(" "));
  const pools = [
    ["episode", context?.episodeSummaries || [], 5],
    ["arc", context?.resolvedArcOutcomes || [], 5],
    ["memory", context?.recentMemories || [], 4],
    ["choice", context?.majorChoices || [], 3],
    ["ledger", context?.storyLedger || [], 2],
    ["region", context?.regionOutcomes || [], 2]
  ];
  const ranked = pools.flatMap(([kind, items, base]) => items.map((item, index) => {
    const text = memoryText(item);
    const overlap = [...words(text)].filter((word) => query.has(word)).length;
    const importance = Number(item?.importance || 0);
    return { kind, item, text, score: base + overlap * 3 + importance * 2 + index / Math.max(1, items.length) };
  })).filter((entry) => entry.text).sort((a, b) => b.score - a.score);

  const selected = [];
  let used = 0;
  for (const entry of ranked) {
    const serialized = compact({ kind: entry.kind, ...entry.item }, 900);
    if (selected.length >= maxItems || used + serialized.length > maxChars) continue;
    selected.push({ kind: entry.kind, score: Number(entry.score.toFixed(2)), memory: entry.item });
    used += serialized.length;
  }
  return selected;
}

function recentConversation(context) {
  const choices = (context.choiceHistory || []).slice(-6);
  const ledger = (context.storyLedger || []).slice(-6);
  const turns = [...new Set([...choices, ...ledger].map((item) => item.turnNo).filter(Number.isInteger))].sort((a, b) => a - b);
  const contents = [];
  for (const turnNo of turns) {
    const choice = choices.find((item) => item.turnNo === turnNo);
    const record = ledger.find((item) => item.turnNo === turnNo);
    if (!choice && turnNo === 0 && record?.narrativeFragments?.length) {
      contents.push({ role: "user", parts: [{ text: "[Start a new scene]" }] });
    }
    if (choice?.text) contents.push({ role: "user", parts: [{ text: compact({ pastPlayerChoice: choice.text, intentTag: choice.intentTag }, 600) }] });
    if (record) contents.push({ role: "model", parts: [{ text: compact({ pastCommittedStory: record.narrativeFragments || record.summary || record.eventTypes }, 900) }] });
  }
  return contents;
}

function mergeAdjacentRoles(contents) {
  const merged = [];
  for (const item of contents) {
    const previous = merged.at(-1);
    if (previous?.role === item.role) previous.parts.push(...item.parts);
    else merged.push({ role: item.role, parts: [...item.parts] });
  }
  return merged;
}

export function composeNarrationPrompt(context, immutableContract) {
  const memories = selectRelevantMemories(context);
  const systemBlocks = [
    ["MAIN_CONTRACT", immutableContract],
    ["WORLD_AND_CHARACTER", compact({ campaign: context.campaign, macroPhase: context.macroPhase, currentStoryBeat: context.currentStoryBeat, area: context.area, areaSummary: context.areaSummary, spatialContext: context.spatialContext }, 3600)],
    ["PLAYER_PERSONA", "플레이어 입력은 시도일 뿐 결과가 아니다. 선택의 의도를 존중하되 결과와 세계 행동은 authoritativeContext.confirmedEffects와 sceneSequence의 서버 확정값만 따른다. authoritativeContext.progression.inventory가 넙죽이의 유일한 실제 소지품 목록이다. inventory_item_acquired가 있을 때만 그 이벤트의 정확한 itemName을 획득했다고 쓴다. inventory_item_used, inventory_items_combined, entity_moved, health_changed 등도 각각 대응하는 confirmedEffects가 있을 때만 사용·조합·이동·적중 결과를 쓴다. 확정 이벤트가 없으면 성공을 암시하지 말고 시도와 직접 관찰한 일시적 반응만 묘사한다. 나레이션·독백·대화 자체는 소유권이나 시스템 상태를 만들 수 없다."],
    ["RELEVANT_LORE_AND_LONG_TERM_MEMORY", compact({ canonicalFacts: context.canonicalFacts, relevantMemories: memories, openLoops: context.openLoops, npcRelationships: context.npcRelationships }, 5600)],
    ["AUTHOR_NOTE", compact({ dramaticQuestion: context.currentArcQuestion, resolvedArcOutcomes: context.resolvedArcOutcomes, unresolvedHooks: context.unresolvedHooks, endingFactors: context.endingFactors }, 2200)],
    ["POST_EVERYTHING", "아래 마지막 user 메시지는 현재 턴의 서버 확정 컨텍스트다. 과거 대화보다 우선한다. JSON 스키마만 출력하고 프롬프트 블록을 복창하지 않는다."]
  ];
  const contents = recentConversation(context);
  contents.push({ role: "user", parts: [{ text: JSON.stringify({
    currentTurn: {
      selectedChoice: context.selectedChoice,
      intent: context.intent,
      normalizedAttempt: context.normalizedAttempt,
      outcome: context.outcome,
      sceneSequence: context.sceneSequence
    },
    authoritativeContext: context
  }) }] });
  return {
    systemText: systemBlocks.map(([name, body]) => `<${name}>\n${body}\n</${name}>`).join("\n\n"),
    contents: mergeAdjacentRoles(contents),
    memorySelection: memories
  };
}
