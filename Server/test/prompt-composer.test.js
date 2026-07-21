import test from "node:test";
import assert from "node:assert/strict";
import { composeNarrationPrompt, selectRelevantMemories } from "../src/llm/prompt-composer.js";

test("long-term memory retrieval prioritizes records relevant to the current choice", () => {
  const context = {
    intent: "봉인된 접근 기록에 관해 기억지기에게 묻는다",
    selectedChoice: { text: "기억지기에게 접근 기록을 보여 달라고 설득한다", intentTag: "EMPATHETIC" },
    episodeSummaries: [
      { id: "rain", summary: "비 내리는 폐허에서 등불을 주웠다." },
      { id: "access", summary: "기억지기가 봉인된 접근 기록을 숨기고 있다는 사실을 알았다." }
    ],
    resolvedArcOutcomes: [], recentMemories: [], majorChoices: [], storyLedger: [], regionOutcomes: [], openLoops: []
  };
  const selected = selectRelevantMemories(context, { maxItems: 1, maxChars: 2000 });
  assert.equal(selected[0].memory.id, "access");
});

test("prompt composer uses ordered RisuAI-style blocks and ends with authoritative current turn", () => {
  const context = {
    campaign: { title: "Ninja Adventure", premise: "코드리아의 붕괴를 추적한다." },
    macroPhase: { id: "opening" }, currentStoryBeat: { id: "beat-1" }, area: "시작 지역", areaSummary: "조용한 광장",
    canonicalFacts: [], episodeSummaries: [], resolvedArcOutcomes: [], recentMemories: [], majorChoices: [], storyLedger: [],
    regionOutcomes: [], openLoops: [], npcRelationships: [], unresolvedHooks: [], choiceHistory: [], sceneSequence: [],
    selectedChoice: { text: "대화를 계속한다", intentTag: "CURIOUS" }, intent: "대화를 계속한다", normalizedAttempt: "대화를 계속한다", outcome: "narrative"
  };
  const prompt = composeNarrationPrompt(context, "IMMUTABLE RULES");
  assert.ok(prompt.systemText.indexOf("<MAIN_CONTRACT>") < prompt.systemText.indexOf("<WORLD_AND_CHARACTER>"));
  assert.ok(prompt.systemText.indexOf("<RELEVANT_LORE_AND_LONG_TERM_MEMORY>") < prompt.systemText.indexOf("<AUTHOR_NOTE>"));
  assert.equal(prompt.contents.at(-1).role, "user");
  assert.match(prompt.contents.at(-1).parts[0].text, /authoritativeContext/);
});
