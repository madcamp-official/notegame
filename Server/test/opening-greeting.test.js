import test from "node:test";
import assert from "node:assert/strict";
import { createCampaignBlueprint } from "../src/domain/campaign.js";
import { createRunState, publicRun } from "../src/domain/turn-engine.js";
import { generateWorld } from "../src/domain/world.js";
import { composeNarrationPrompt } from "../src/llm/prompt-composer.js";

test("a new run starts with an adjacent one-hit monster and a mandatory keyboard attack", () => {
  const seed = 8221;
  const run = createRunState({
    campaign: {
      ...createCampaignBlueprint({ worldSeed: seed, turnLimit: 40 }),
      id: "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa",
      world: generateWorld(seed)
    },
    ownerId: "bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb",
    runId: "cccccccc-cccc-4ccc-8ccc-cccccccccccc"
  });
  const dto = publicRun(run);
  const beat = dto.openingNarrative.storySequence.find((item) => item.type === "DIALOGUE");
  const speaker = dto.entities.find((entity) => entity.id === beat.speakerId);
  const player = dto.entities.find((entity) => entity.kind === "player");
  const monster = dto.entities.find((entity) => entity.id === dto.activeEncounter?.sourceEntityId);
  assert.equal(beat.type, "DIALOGUE");
  assert.equal(speaker.kind, "npc");
  assert.ok(Math.abs(player.position.x - speaker.position.x) + Math.abs(player.position.y - speaker.position.y) <= 8);
  assert.equal(speaker.state.approachedPlayerAtOpening, true);
  assert.equal(monster.kind, "enemy");
  assert.equal(monster.state.hp, 5);
  assert.equal(monster.state.maxHp, 5);
  assert.equal(monster.state.revealed, true);
  assert.equal(Math.abs(player.position.x - monster.position.x) + Math.abs(player.position.y - monster.position.y), 1);
  assert.equal(dto.activeEncounter.kind, "COMBAT");
  assert.equal(dto.pendingChoiceSet.choices.length, 1);
  assert.equal(dto.pendingChoiceSet.choices[0].choiceId, "opening.attack");
  assert.equal(dto.pendingChoiceSet.choices[0].skillId, "DELETE");
  assert.equal(dto.pendingChoiceSet.choices[0].targetEntityId, monster.id);
  assert.ok(dto.entities.some((entity) => entity.state?.adminAccessLevelId === "ADMIN_ACCESS_LEVEL_1"));
  assert.ok(!dto.entities.some((entity) =>
    ["ADMIN_ACCESS_LEVEL_2", "ADMIN_ACCESS_LEVEL_3"].includes(entity.state?.adminAccessLevelId)),
  "future administrator-access actors must stay hidden until their preceding level is acquired");
  assert.match(dto.pendingChoiceSet.reason, /R 키/u);
  assert.equal(dto.storyLedger.length, 0, "the tutorial setup must not consume or inflate the campaign turn ledger");
  assert.match(dto.openingNarrative.body, /[가-힣]/u);
  assert.match(dto.openingNarrative.body, /관리자 키보드를 든 넙죽이/);
  assert.match(dto.openingNarrative.body, /코드리아/);
  assert.match(dto.openingNarrative.body, /R 키|R을/u);
  assert.equal(dto.openingNarrative.storySequence[0].type, "NARRATION");
});

test("the opening greeting is passed to the first generation as prior model dialogue", () => {
  const prompt = composeNarrationPrompt({
    campaign: {}, macroPhase: {}, currentStoryBeat: {}, canonicalFacts: [], episodeSummaries: [], resolvedArcOutcomes: [],
    recentMemories: [], majorChoices: [], regionOutcomes: [], openLoops: [], npcRelationships: [], unresolvedHooks: [],
    choiceHistory: [], sceneSequence: [], selectedChoice: { text: "무슨 일인지 묻는다" }, intent: "대답한다",
    normalizedAttempt: "무슨 일인지 묻는다", outcome: "narrative",
    storyLedger: [{ turnNo: 0, narrativeFragments: ["잠깐, 낯선 관리자. 네 생각을 듣고 싶어."] }]
  }, "contract");
  assert.equal(prompt.contents[0].role, "user");
  assert.match(prompt.contents[0].parts[0].text, /Start a new scene/);
  assert.equal(prompt.contents[1].role, "model");
  assert.match(prompt.contents[1].parts[0].text, /낯선 관리자/);
});
