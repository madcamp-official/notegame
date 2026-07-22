import test from "node:test";
import assert from "node:assert/strict";
import {
  choiceSelectionFingerprint,
  choicesFromLegacySkills,
  createInitialChoiceSet,
  normalizeChoiceSelectionRequest,
  normalizePlayerMessageRequest,
  reconcileNarrativeSkillChoices,
  sealNarrativeIntervention,
  selectedChoiceForRun,
  validateNarrativeChoices
} from "../src/domain/narrative-choices.js";

const RUN_ID = "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa";

test("freeform input rejects punctuation-only text before any LLM can invent an unrelated action", () => {
  const base = {
    idempotencyKey: "message-meaningful-0001",
    expectedRunVersion: 2,
    preparedD20: null
  };
  assert.throws(() => normalizePlayerMessageRequest({ ...base, text: ".!?  …" }),
    /at least one letter or number/u);
  assert.equal(normalizePlayerMessageRequest({ ...base, text: "루트로 이동" }).text, "루트로 이동");
  assert.equal(normalizePlayerMessageRequest({ ...base, text: "move to root" }).text, "move to root");
});

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

test("server sealing binds encounter skills to a capable authoritative actor", () => {
  const player = { id: "player", kind: "player", active: true, protected: true, position: { x: 5, y: 5 }, state: {} };
  const comment = { id: "comment", kind: "npc", active: true, protected: true, position: { x: 4, y: 5 }, state: {} };
  const enemy = {
    id: "enemy", kind: "enemy", assetId: "enemy.slime.v1", name: "캐시 누수 슬라임", active: true,
    protected: false, position: { x: 5, y: 6 }, state: { hp: 5, maxHp: 5, revealed: true }
  };
  const run = {
    id: RUN_ID,
    playerEntityId: player.id,
    entities: [player, comment, enemy],
    activeEncounter: { status: "active", kind: "COMBAT", entityId: enemy.id },
    connections: [],
    reversibleLedger: [],
    adminAccessCandidates: [],
    adminAccessAcquisitionHistory: [],
    currentTurn: 1,
    focus: 10,
    worldSeed: 20260718,
    world: { worldSeed: 20260718 }
  };
  const choices = [
    {
      choiceId: "choice.attack.delete",
      text: "캐시 누수 슬라임을 삭제 명령으로 제압한다.",
      choiceKind: "SKILL",
      intentTag: "ASSERTIVE",
      resolutionMode: "D20",
      skillId: "DELETE",
      targetEntityId: comment.id,
      destinationRef: null
    },
    {
      choiceId: "choice.dialogue.ask",
      text: "코멘트에게 방금 본 현상을 자세히 묻는다.",
      choiceKind: "DIALOGUE",
      intentTag: "CURIOUS",
      resolutionMode: "NONE",
      skillId: null,
      targetEntityId: comment.id,
      destinationRef: null
    }
  ];

  const reconciled = reconcileNarrativeSkillChoices(choices, {
    run,
    allowedEntityIds: [player.id, comment.id, enemy.id]
  });
  assert.equal(reconciled[0].targetEntityId, enemy.id);
  assert.match(reconciled[0].text, /캐시 누수 슬라임/u);
  assert.equal(reconciled[0].skillId, "DELETE");
  assert.equal(reconciled[1].targetEntityId, comment.id);

  const sealed = sealNarrativeIntervention({ reason: "활성 조우의 다음 대응을 선택한다.", choices }, {
    runId: RUN_ID,
    turnNo: 1,
    runVersion: 2,
    allowedEntityIds: [player.id, comment.id, enemy.id],
    authoritativeRun: run
  });
  assert.equal(sealed.choices[0].targetEntityId, enemy.id);

  const legacyRun = { ...run, version: 2, pendingChoiceSet: { ...sealed, choices } };
  const recovered = selectedChoiceForRun(legacyRun, {
    choiceSetId: sealed.choiceSetId,
    choiceId: "choice.attack.delete"
  });
  assert.equal(recovered.targetEntityId, enemy.id);

  const noEnemy = { ...run, entities: [player, comment], activeEncounter: null };
  const fallback = reconcileNarrativeSkillChoices(choices, {
    run: noEnemy,
    allowedEntityIds: [player.id, comment.id]
  });
  assert.equal(fallback[0].choiceKind, "ATTITUDE");
  assert.equal(fallback[0].skillId, null);
  assert.equal(fallback[0].text, "지금은 바로 삭제할 수 없다. 오염의 범위와 다른 해결책을 다시 살핀다.");
  assert.doesNotMatch(fallback[0].text, /첫|두 대응/u);
  assert.equal(fallback[1].choiceKind, "DIALOGUE");
});

test("server sealing keeps one direct combat response when narration pivots only to conversation", () => {
  const player = { id: "player", kind: "player", active: true, protected: true, position: { x: 5, y: 5 }, state: {} };
  const enemy = {
    id: "enemy", kind: "enemy", assetId: "enemy.slime.v1", name: "캐시 누수 슬라임", active: true,
    protected: false, position: { x: 5, y: 6 }, state: { hp: 5, maxHp: 5, revealed: true }
  };
  const run = {
    id: RUN_ID, playerEntityId: player.id, entities: [player, enemy], currentTurn: 2, focus: 10,
    activeEncounter: { status: "active", kind: "COMBAT", sourceEntityId: enemy.id,
      suggestedSkillIds: ["DELETE", "SELECT_ALL", "SEARCH", "RESTORE"] },
    connections: [], reversibleLedger: [], adminAccessCandidates: [], adminAccessAcquisitionHistory: [],
    worldSeed: 20260718, world: { worldSeed: 20260718 }
  };
  const choices = [
    {
      choiceId: "choice.search_truth", text: "스네이크 주변의 기록에서 진실을 조사한다.", choiceKind: "SKILL",
      intentTag: "INVESTIGATE", resolutionMode: "D20", skillId: "SEARCH", targetEntityId: enemy.id,
      destinationRef: null
    },
    {
      choiceId: "choice.ask", text: "스네이크에게 그 말의 의미를 묻는다.", choiceKind: "DIALOGUE",
      intentTag: "CURIOUS", resolutionMode: "NONE", skillId: null, targetEntityId: enemy.id,
      destinationRef: null
    }
  ];

  const sealed = sealNarrativeIntervention({ reason: "실패 뒤에도 대치는 계속된다.", choices }, {
    runId: RUN_ID, turnNo: 2, runVersion: 3, allowedEntityIds: [player.id, enemy.id], authoritativeRun: run
  });

  assert.ok(sealed.choices.some((choice) => choice.choiceKind === "DIALOGUE"));
  assert.ok(sealed.choices.some((choice) => choice.choiceKind === "SKILL" && choice.skillId === "DELETE" &&
    choice.targetEntityId === enemy.id), "an active combat encounter must retain a keyboard-addressable response");
});

test("an exhausted combat choice set offers an authoritative rest instead of an unaffordable attack", () => {
  const player = {
    id: "player", kind: "player", active: true, protected: true, position: { x: 5, y: 5 },
    state: { hp: 7, maxHp: 7 }
  };
  const enemy = {
    id: "enemy", kind: "enemy", assetId: "enemy.slime.v1", name: "캐시 누수 슬라임", active: true,
    protected: false, position: { x: 5, y: 6 }, state: { hp: 5, maxHp: 5, revealed: true }
  };
  const run = {
    id: RUN_ID, playerEntityId: player.id, entities: [player, enemy], currentTurn: 3, focus: 0,
    activeEncounter: { status: "active", kind: "COMBAT", sourceEntityId: enemy.id },
    connections: [], reversibleLedger: [], adminAccessCandidates: [], adminAccessAcquisitionHistory: [],
    worldSeed: 20260718, world: { worldSeed: 20260718 }
  };
  const choices = [
    {
      choiceId: "choice.delete", text: "캐시 누수 슬라임에게 삭제 명령으로 맞선다.", choiceKind: "SKILL",
      intentTag: "ASSERTIVE", resolutionMode: "D20", skillId: "DELETE", targetEntityId: enemy.id,
      destinationRef: null
    },
    {
      choiceId: "choice.ask", text: "캐시 누수 슬라임에게 목적을 묻는다.", choiceKind: "DIALOGUE",
      intentTag: "CURIOUS", resolutionMode: "NONE", skillId: null, targetEntityId: enemy.id,
      destinationRef: null
    }
  ];

  const sealed = sealNarrativeIntervention({ reason: "집중력이 바닥난 채 대치가 계속된다.", choices }, {
    runId: RUN_ID, turnNo: 3, runVersion: 4, allowedEntityIds: [player.id, enemy.id], authoritativeRun: run
  });

  assert.ok(sealed.choices.some((choice) => choice.choiceKind === "SKILL" && choice.skillId === "REST" &&
    choice.targetEntityId === null));
  assert.equal(sealed.choices.some((choice) => choice.choiceKind === "SKILL" && choice.skillId === "DELETE"), false);
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
