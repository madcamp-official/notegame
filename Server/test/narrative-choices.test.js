import test from "node:test";
import assert from "node:assert/strict";
import {
  choiceSelectionFingerprint,
  choicesFromLegacySkills,
  createInitialChoiceSet,
  normalizeChoiceSelectionRequest,
  sealNarrativeIntervention,
  selectedChoiceForRun,
  validateNarrativeChoices
} from "../src/domain/narrative-choices.js";

const RUN_ID = "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa";

test("legacy skill suggestions migrate to a diverse server-sealed narrative choice set", () => {
  const choices = choicesFromLegacySkills(["CONNECT", "SEARCH"]);
  assert.equal(choices.length, 3);
  assert.equal(choices[0].resolutionMode, "NONE");
  assert.ok(choices.some((choice) => choice.choiceKind === "SKILL" && choice.skillId === "CONNECT"));
  const sealed = sealNarrativeIntervention({ reason: "누군가 대답을 기다리고 있다.", choices }, {
    runId: RUN_ID,
    turnNo: 4,
    runVersion: 5
  });
  assert.match(sealed.choiceSetId, /^[0-9a-f-]{36}$/i);
  assert.deepEqual(sealed.suggestedSkillIds, ["CONNECT", "SEARCH"]);
  assert.equal(sealed.issuedTurn, 4);
  assert.equal(sealed.issuedRunVersion, 5);
});

test("choice validation rejects all-skill, duplicate, impossible-reference, and generated travel sets", () => {
  const skill = choicesFromLegacySkills(["SEARCH"])[1];
  assert.throws(() => validateNarrativeChoices([
    skill,
    { ...skill, choiceId: "skill.connect", text: "연결 명령을 사용한다.", skillId: "CONNECT" }
  ]), /non-skill/);
  const valid = choicesFromLegacySkills(["SEARCH"]);
  assert.throws(() => validateNarrativeChoices([
    valid[0],
    { ...valid[1], choiceId: valid[0].choiceId }
  ]), /choiceId values must be unique/);
  assert.throws(() => validateNarrativeChoices([
    { ...valid[0], targetEntityId: "missing-entity" },
    valid[1]
  ], { allowedEntityIds: [] }), /outside the visible scene/);
  assert.throws(() => validateNarrativeChoices([
    valid[0],
    {
      choiceId: "travel.next",
      text: "다음 장소로 장면을 옮긴다.",
      choiceKind: "TRAVEL",
      intentTag: "TRAVEL",
      resolutionMode: "NONE",
      skillId: null,
      targetEntityId: null,
      destinationRef: "area.next"
    }
  ], { allowedDestinationRefs: ["area.next"], allowTravel: false }), /not enabled/);
});

test("selection accepts only a current server-offered choice and fingerprints the sealed IDs", () => {
  const pendingChoiceSet = createInitialChoiceSet({ runId: RUN_ID, runVersion: 1, turnNo: 0 });
  const request = normalizeChoiceSelectionRequest({
    choiceSetId: pendingChoiceSet.choiceSetId,
    choiceId: "opening.listen",
    idempotencyKey: "choice-test-0001",
    expectedRunVersion: 1
  });
  const run = { id: RUN_ID, version: 1, currentTurn: 0, pendingChoiceSet };
  assert.equal(selectedChoiceForRun(run, request).text, pendingChoiceSet.choices[0].text);
  assert.match(choiceSelectionFingerprint(request), /^[0-9a-f]{64}$/);
  assert.throws(() => selectedChoiceForRun(run, { ...request, choiceId: "opening.fabricated" }), (error) => error.code === "CHOICE_NOT_OFFERED");
  assert.throws(() => selectedChoiceForRun(run, { ...request, choiceSetId: "bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb" }), (error) => error.code === "CHOICE_SET_STALE");
});
