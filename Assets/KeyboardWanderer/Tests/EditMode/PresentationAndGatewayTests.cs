using System;
using System.Collections;
using System.Reflection;
using KeyboardWanderer.Core;
using KeyboardWanderer.Demo;
using KeyboardWanderer.Gameplay;
using KeyboardWanderer.Networking;
using KeyboardWanderer.Presentation;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace KeyboardWanderer.Tests.EditMode
{
    public sealed class PresentationAndGatewayTests
    {
        [Test]
        public void Coordinator_OnlyPublishesChangedPresentationSections()
        {
            var coordinator = new RunCoordinator();
            PresentationChange observed = PresentationChange.None;
            int notifications = 0;
            coordinator.PresentationChanged += (_, changes) =>
            {
                observed = changes;
                notifications++;
            };
            var initial = State(new GridCoord(2, 3), null, 0);

            coordinator.Publish(initial);
            Assert.That(observed, Is.EqualTo(PresentationChange.All));

            coordinator.Publish(initial);
            Assert.That(notifications, Is.EqualTo(1));

            coordinator.Publish(State(new GridCoord(3, 3), null, 0));
            Assert.That((observed & PresentationChange.Minimap) != 0, Is.True);
            Assert.That((observed & PresentationChange.Dialogue) == 0, Is.True);

            coordinator.Publish(State(new GridCoord(3, 3), new GridCoord(7, 8), 0));
            Assert.That((observed & PresentationChange.Minimap) != 0, Is.True);
            Assert.That((observed & PresentationChange.Selection) != 0, Is.True);
        }

        [Test]
        public void LocalGateway_ProducesSameCommittedRunAsLocalService()
        {
            LocalTurnService service = LocalTurnService.CreateDemo(20260719L);
            var gateway = new LocalTurnGateway(service);
            GridCoord destination = FindWalkableNeighbour(service.CurrentView);
            TurnGatewayResult result = null;

            Drain(gateway.Submit(TurnRequest.Move("gateway-test", service.CurrentView.Version, destination),
                value => result = value));

            Assert.That(result, Is.Not.Null);
            Assert.That(result.IsSuccess, Is.True, result?.ErrorMessage);
            Assert.That(result.LocalResponse.Run.Version, Is.EqualTo(service.CurrentView.Version));
            Assert.That(result.LocalResponse.Run.PlayerPosition, Is.EqualTo(service.CurrentView.PlayerPosition));
            Assert.That(gateway.IsPending, Is.False);
        }

        [Test]
        public void DelegatingGateway_NormalizesDelayedAndMissingResponses()
        {
            var delayed = new DelegatingTurnGateway((_, done) => CompleteAfterFrame(done));
            TurnGatewayResult delayedResult = null;
            Drain(delayed.Submit(null, value => delayedResult = value));
            Assert.That(delayedResult.ErrorCode, Is.EqualTo("TEST_ERROR"));
            Assert.That(delayed.IsPending, Is.False);

            var missing = new DelegatingTurnGateway((_, __) => Empty());
            TurnGatewayResult missingResult = null;
            Drain(missing.Submit(null, value => missingResult = value));
            Assert.That(missingResult.ErrorCode, Is.EqualTo("NO_COMPLETION"));
        }

        [Test]
        public void ServerPresentationAdapter_UsesAuthoritativeVersionLayoutAndPlayerPosition()
        {
            LocalTurnService fallback = LocalTurnService.CreateDemo(20260720L);
            var run = new GameApiClient.RunSnapshot
            {
                version = 41,
                currentTurn = 7,
                playerEntityId = "player-1",
                world = new GameApiClient.WorldSnapshot { layoutHash = "server-layout" },
                inventory = new[]
                {
                    new GameApiClient.InventoryItemSnapshot
                    {
                        id = "item-1", name = "빛나는 데이터 파편", kind = "material",
                        description = "차갑게 진동한다.", quantity = 2
                    }
                },
                activeQuests = new[]
                {
                    new GameApiClient.QuestSnapshot
                    {
                        id = "quest-1", title = "흔적의 주인", summary = "파편의 근원을 찾는다.",
                        currentStep = "코멘트에게 묻기", status = "active", createdTurn = 2
                    },
                    new GameApiClient.QuestSnapshot
                    {
                        id = "legacy-seed", title = "시작부터 열린 옛 훅", status = "active",
                        questKind = "story_hook", createdTurn = 0
                    }
                },
                entities = new[]
                {
                    new GameApiClient.EntitySnapshot
                    {
                        id = "player-1",
                        position = new GameApiClient.PositionSnapshot { x = 18, y = 29 }
                    }
                }
            };
            var adapter = new ServerRunPresentationAdapter(() => run);

            RunPresentationModel presentation = adapter.Capture(fallback.CurrentView);
            RunPresentationCore core = presentation.Core;

            Assert.That(presentation.IsServerAuthoritative, Is.True);
            Assert.That(core.Version, Is.EqualTo(41));
            Assert.That(core.Turn, Is.EqualTo(7));
            Assert.That(core.LayoutHash, Is.EqualTo("server-layout"));
            Assert.That(core.PlayerPosition, Is.EqualTo(new GridCoord(18, 29)));
            Assert.That(presentation.Inventory, Has.Count.EqualTo(1));
            Assert.That(presentation.Inventory[0].Name, Is.EqualTo("빛나는 데이터 파편"));
            Assert.That(presentation.Inventory[0].Quantity, Is.EqualTo(2));
            Assert.That(presentation.Quests, Has.Count.EqualTo(1));
            Assert.That(presentation.Quests[0].CurrentStep, Is.EqualTo("코멘트에게 묻기"));
        }

        [Test]
        public void HudTextComposer_UsesNormalizedLocalStateAndPlayerFacingSkillNames()
        {
            LocalTurnService service = LocalTurnService.CreateDemo(20260721L);
            RunPresentationModel run = new LocalRunPresentationAdapter().Capture(service.CurrentView);

            AbilityKind[] recommendations = KeyboardWandererHudTextComposer.RecommendedActions(run, false);
            string objective = KeyboardWandererHudTextComposer.ObjectiveHud(run);
            string questHint = KeyboardWandererHudTextComposer.QuestActionHint(run, false);
            string statusLabels = KeyboardWandererHudTextComposer.StatusLabels();
            string statusValues = KeyboardWandererHudTextComposer.StatusValues(run);
            string secondary = KeyboardWandererHudTextComposer.SecondaryObjectives(run);

            Assert.That(recommendations.Length, Is.InRange(2, 3));
            Assert.That(recommendations[0], Is.EqualTo(run.ObjectiveAbility));
            Assert.That(objective, Does.Contain(run.StoryObjective));
            Assert.That(objective, Does.Not.Contain("추천"), "진행·자원 수치는 상태 패널로 분리되어야 한다.");
            Assert.That(questHint, Does.Contain("추천"));
            Assert.That(statusLabels.Split('\n').Length, Is.EqualTo(statusValues.Split('\n').Length),
                "상태 패널 라벨과 값의 줄 수는 항상 일치해야 한다.");
            Assert.That(statusLabels.Split('\n')[0], Is.EqualTo("HP"));
            Assert.That(statusValues, Does.StartWith(run.Health + " / " + run.MaxHealth),
                "피격과 회복을 즉시 확인할 수 있도록 플레이어 체력을 상태 패널에 표시해야 한다.");
            Assert.That(statusValues, Does.Contain(run.AdminAccess + " / 3"));
            Assert.That(KeyboardWandererHudTextComposer.AbilityPlayerLabel(AbilityKind.Search),
                Is.EqualTo("F 조사"));
            Assert.That(KeyboardWandererHudTextComposer.NarrativeChoicePlayerLabel(
                    new NarrativeChoiceOption("ask", "상황을 묻는다.", "DIALOGUE")),
                Is.EqualTo("대화 선택"));
            Assert.That(KeyboardWandererHudTextComposer.NarrativeChoicePlayerLabel(
                    new NarrativeChoiceOption("attack", "공격한다.", "SKILL", skillId: "DELETE")),
                Is.EqualTo("R 단일 공격"));
            Assert.That(KeyboardWandererHudTextComposer.NarrativeChoicePlayerLabel(
                    new NarrativeChoiceOption("rest", "숨을 고른다.", "SKILL", skillId: "REST")),
                Is.EqualTo("휴식 회복"));
            Assert.That(secondary, Is.Not.Empty);
        }

        [Test]
        public void LocalTurnPresentationAdapter_NormalizesMechanicalAndNarrativeFields()
        {
            TurnResponse response = TurnResponse.Success(
                3, 17, 2, 14, 19, 2, RuleOutcome.Success,
                "판정에 성공했습니다.", "대상 조사", "숨겨진 기록을 발견했다.", 0,
                new[] { "ENTITY_INVESTIGATED:test" }, null, true, ActionContext.Investigation);

            TurnPresentationResult result = LocalTurnPresentationAdapter.Create(response);

            Assert.That(result.D20, Is.EqualTo(17));
            Assert.That(result.Modifier, Is.EqualTo(2));
            Assert.That(result.ActionContext, Is.EqualTo("조사"));
            Assert.That(result.Outcome, Is.EqualTo("성공"));
            Assert.That(result.Narrative, Is.EqualTo("숨겨진 기록을 발견했다."));
            Assert.That(result.StateChanges, Does.Contain("대상을 조사해 새로운 정보를 확인함"));
            Assert.That(result.LogEntries, Is.Not.Empty);
        }

        [Test]
        public void ServerTurnPresentationAdapter_HidesDtoNamesAndUsesSelectedEntityName()
        {
            var run = new GameApiClient.RunSnapshot
            {
                entities = new[]
                {
                    new GameApiClient.EntitySnapshot { id = "target-1", name = "고대 단말" }
                }
            };
            var turn = new GameApiClient.TurnSnapshot
            {
                skillId = "SEARCH",
                targetIds = new[] { "target-1" },
                actionContext = "INVESTIGATION",
                outcome = "success",
                dice = new GameApiClient.DiceSnapshot
                {
                    raw = 16,
                    modifier = 3,
                    difficulty = 12,
                    mechanicalScore = 19
                },
                events = new[]
                {
                    new GameApiClient.EventSnapshot { type = "search_completed" }
                }
            };

            TurnPresentationResult result = ServerTurnPresentationAdapter.FromTurn(turn, run, true);

            Assert.That(result.Attempt, Does.Contain("고대 단말"));
            Assert.That(result.Attempt, Does.Contain("F 조사"));
            Assert.That(result.ActionContext, Is.EqualTo("조사"));
            Assert.That(result.Outcome, Is.EqualTo("성공"));
            Assert.That(result.Narrative, Does.Contain("흔적을 발견했어"));
            Assert.That(result.StateChanges, Does.Contain("조사를 완료함"));
        }

        [Test]
        public void ServerTurnPresentationAdapter_ShowsClueMeaningAndNextInvestigation()
        {
            var turn = new GameApiClient.TurnSnapshot
            {
                outcome = "success",
                events = new[]
                {
                    new GameApiClient.EventSnapshot
                    {
                        type = "npc_clue_revealed", npcName = "인덱스",
                        line = "인덱스가 삭제된 기록을 건넸다.",
                        clueTitle = "삭제 기록에 남은 내부 통제 서명",
                        clueContent = "삭제된 기록 조각에 내부 통제 시스템의 서명이 남아 있었다",
                        clueMeaning = "외부 공격이 아니라 내부 통제 시스템이 실행한 명령의 흔적이다.",
                        storyConnection = "붕괴가 관리자 통제 계층 내부에서 시작됐을 가능성이 커졌다.",
                        nextObjective = "클린업에게 이 기록의 서명을 확인받는다.", trust = 3
                    }
                },
                narrative = new GameApiClient.NarrativeSnapshot { body = "일반적인 조사 결과다." }
            };

            TurnPresentationResult result = ServerTurnPresentationAdapter.FromTurn(
                turn, new GameApiClient.RunSnapshot(), true);

            Assert.That(result.Dialogue, Has.Some.Contains("삭제된 기록 조각"));
            Assert.That(result.Dialogue, Has.Some.Contains("내부 통제 시스템이 실행"));
            Assert.That(result.Dialogue, Has.Some.Contains("클린업에게"));
            Assert.That(result.StateChanges, Does.Contain("삭제 기록에 남은 내부 통제 서명"));
            Assert.That(result.StateChanges, Does.Not.Contain("대상의 단서를 조사함"));
        }

        [Test]
        public void NavigationPresentation_AlwaysReturnsToANarrativeChoiceBoundary()
        {
            var navigation = new GameApiClient.NavigationSnapshot
            {
                pathCost = 3,
                campaignTurnConsumed = false,
                narrative = null
            };
            var run = new GameApiClient.RunSnapshot { protagonistName = "넙죽이" };

            TurnPresentationResult result = ServerTurnPresentationAdapter.FromNavigation(
                navigation, run, 0, 3, 4, false, null, true, true, "넙죽이");

            Assert.That(result.StorySequence, Has.Length.GreaterThanOrEqualTo(1));
            Assert.That(result.NextInterventionReason, Is.Not.Empty);
            Assert.That(result.SuggestedSkillIds, Does.Contain("SEARCH"));
        }

        [Test]
        public void ServerPresentationAdapter_PreservesSealedNarrativeChoiceTextAndMetadata()
        {
            var intervention = new GameApiClient.NextInterventionSnapshot
            {
                choiceSetId = "choice-set-7",
                reason = "경비병이 대답을 기다린다.",
                choices = new[]
                {
                    new GameApiClient.NarrativeChoiceSnapshot
                    {
                        choiceId = "ask-first", text = "무슨 일이 있었는지 먼저 들려줘.",
                        choiceKind = "DIALOGUE", intentTag = "CURIOUS", resolutionMode = "NONE"
                    },
                    new GameApiClient.NarrativeChoiceSnapshot
                    {
                        choiceId = "connect-trace", text = "남은 흔적을 연결해 볼게.",
                        choiceKind = "SKILL", intentTag = "INVESTIGATE", skillId = "CONNECT",
                        destinationRef = "signal-1", resolutionMode = "D20"
                    }
                }
            };
            var turn = new GameApiClient.TurnSnapshot
            {
                outcome = "success",
                narrative = new GameApiClient.NarrativeSnapshot
                {
                    body = "경비병이 고개를 들었다.", nextIntervention = intervention
                }
            };

            TurnPresentationResult result = ServerTurnPresentationAdapter.FromTurn(
                turn, new GameApiClient.RunSnapshot(), true);

            Assert.That(result.ChoiceSetId, Is.EqualTo("choice-set-7"));
            Assert.That(result.NarrativeChoices, Has.Length.EqualTo(2));
            Assert.That(result.NarrativeChoices[0].Text, Is.EqualTo("무슨 일이 있었는지 먼저 들려줘."));
            Assert.That(result.NarrativeChoices[0].RequiresD20, Is.False);
            Assert.That(result.NarrativeChoices[1].IsSkill, Is.True);
            Assert.That(result.NarrativeChoices[1].DestinationRef, Is.EqualTo("signal-1"));
            Assert.That(result.NarrativeChoices[1].ResolutionMode, Is.EqualTo("D20"));
            Assert.That(result.NarrativeChoices[1].RequiresD20, Is.True);
        }

        [Test]
        public void ServerPresentationAdapter_ConvertsLegacySuggestedSkillsIntoReadableChoices()
        {
            NarrativeChoiceOption[] choices = ServerTurnPresentationAdapter.BuildNarrativeChoices(
                null, new[] { "SEARCH", "CONNECT" }, true);

            Assert.That(choices.Length, Is.InRange(2, 4));
            Assert.That(choices[0].ChoiceKind, Is.EqualTo("TRAVEL"));
            Assert.That(choices, Has.Some.Matches<NarrativeChoiceOption>(value =>
                value.SkillId == "SEARCH" && value.Text.Contains("조사")));
            Assert.That(choices, Has.Some.Matches<NarrativeChoiceOption>(value =>
                value.SkillId == "CONNECT" && value.Text.Contains("연결")));
        }

        [Test]
        public void DialogueView_RendersFullChoiceTextAndLocksDuplicateClicks()
        {
            var root = new GameObject("Narrative Choice View", typeof(RectTransform));
            var view = root.AddComponent<KeyboardWandererDialogueView>();
            TMP_FontAsset koreanFont = Resources.Load<TMP_FontAsset>(
                "Fonts/NeoDunggeunmoPro-Regular SDF");
            Assert.That(koreanFont, Is.Not.Null);
            var speaker = CreateText(root.transform, "Speaker");
            var speechBubble = new GameObject("Speech Bubble", typeof(RectTransform));
            speechBubble.transform.SetParent(root.transform, false);
            var story = CreateText(speechBubble.transform, "Story");
            var nextLabel = CreateText(root.transform, "Next Label");
            speaker.font = koreanFont;
            story.font = koreanFont;
            nextLabel.font = koreanFont;
            var nextObject = new GameObject("Next", typeof(RectTransform), typeof(Image), typeof(Button));
            nextObject.transform.SetParent(root.transform, false);
            view.Configure(speaker, story, nextLabel, nextObject.GetComponent<Button>());
            var strip = new GameObject("Choice Strip", typeof(RectTransform), typeof(Image));
            strip.transform.SetParent(root.transform, false);
            for (int i = 0; i < 5; i++)
            {
                var buttonObject = new GameObject("Choice " + (i + 1), typeof(RectTransform),
                    typeof(Image), typeof(Button));
                buttonObject.transform.SetParent(strip.transform, false);
                CreateText(buttonObject.transform, "Choice Label " + (i + 1)).font = koreanFont;
            }

            try
            {
                int selections = 0;
                string selectedId = null;
                int messageSubmissions = 0;
                string submittedMessage = null;
                view.Bind(() => { }, id =>
                {
                    selections++;
                    selectedId = id;
                }, text =>
                {
                    messageSubmissions++;
                    submittedMessage = text;
                });
                var choices = new[]
                {
                    new NarrativeChoiceOption("listen", "어떤 일이 있었는지 먼저 들려줘.", "DIALOGUE"),
                    new NarrativeChoiceOption("guard", "아직 믿을 수 없어. 거기서 말해.", "ATTITUDE")
                };

                view.PresentChoices(true, choices, true);
                int firstLayoutRevision = PrivateIntProperty(view, "ChoiceLayoutRevision");
                view.PresentChoices(true, choices, true);
                Assert.That(PrivateIntProperty(view, "ChoiceLayoutRevision"),
                    Is.EqualTo(firstLayoutRevision),
                    "같은 선택 상태를 다시 표시할 때 Choice Layout/Canvas를 재빌드하면 안 됩니다.");
                view.PresentChoices(false, Array.Empty<NarrativeChoiceOption>(), false);
                int hiddenLayoutRevision = PrivateIntProperty(view, "ChoiceLayoutRevision");
                view.PresentChoices(false, Array.Empty<NarrativeChoiceOption>(), false);
                Assert.That(hiddenLayoutRevision, Is.EqualTo(firstLayoutRevision));
                Assert.That(PrivateIntProperty(view, "ChoiceLayoutRevision"),
                    Is.EqualTo(hiddenLayoutRevision),
                    "숨은 선택 패널은 반복 HUD 갱신으로 레이아웃을 더럽히면 안 됩니다.");
                view.PresentChoices(true, choices, true);
                Assert.That(PrivateIntProperty(view, "ChoiceLayoutRevision"),
                    Is.GreaterThan(hiddenLayoutRevision),
                    "다시 표시할 때는 최초 가시 레이아웃을 정상 계산해야 합니다.");
                Button first = Array.Find(strip.GetComponentsInChildren<Button>(true),
                    button => button.name == "Choice 1");
                Button second = Array.Find(strip.GetComponentsInChildren<Button>(true),
                    button => button.name == "Choice 2");
                Assert.That(first, Is.Not.Null);
                Assert.That(second, Is.Not.Null);
                TMP_InputField freeform = strip.transform.Find("Freeform Input/Input").GetComponent<TMP_InputField>();
                Assert.That(freeform.transform.parent.parent, Is.EqualTo(strip.transform),
                    "자연어 입력은 선택지와 같은 확장 패널의 예약된 입력 행에 있어야 합니다.");
                Assert.That(freeform.characterLimit, Is.EqualTo(1000),
                    "Client input capacity must match the server's 1000-character contract.");
                Assert.That(freeform.lineType, Is.EqualTo(TMP_InputField.LineType.MultiLineSubmit),
                    "긴 자유입력은 줄바꿈되지만 Return 제출 의미는 유지해야 합니다.");
                Assert.That(freeform.textViewport, Is.Not.Null);
                Assert.That(freeform.textViewport.GetComponent<RectMask2D>(), Is.Not.Null,
                    "여러 줄 초안이 입력 영역 밖의 선택지나 버튼 위에 그려지면 안 됩니다.");
                Assert.That(freeform.textComponent.textWrappingMode, Is.EqualTo(TextWrappingModes.Normal));
                Assert.That(freeform.textComponent.transform.IsChildOf(freeform.textViewport), Is.True);
                Assert.That(freeform.verticalScrollbar, Is.Not.Null,
                    "1000자 초안의 앞부분을 다시 검토할 수 있는 세로 스크롤이 필요합니다.");
                TMP_Text characterCount = Array.Find(strip.GetComponentsInChildren<TMP_Text>(true),
                    text => text.name == "Character Count");
                Assert.That(characterCount, Is.Not.Null);
                Assert.That(characterCount.text, Is.EqualTo("0/1000자"));
                freeform.text = "문 옆의 흔적을 자세히 조사한다";
                Assert.That(characterCount.text, Is.EqualTo(freeform.text.Length + "/1000자"));
                freeform.onSubmit.Invoke(freeform.text);
                Assert.That(messageSubmissions, Is.EqualTo(1));
                Assert.That(submittedMessage, Is.EqualTo("문 옆의 흔적을 자세히 조사한다"));
                Assert.That(freeform.text, Is.EqualTo(submittedMessage),
                    "전송 직후에는 권위 응답이 아직 없으므로 작성한 문장을 보존해야 합니다.");
                Assert.That(freeform.interactable, Is.False);
                view.ReleaseChoiceInputLock();
                view.PresentChoices(true, choices, true);
                Assert.That(freeform.interactable, Is.True);
                Assert.That(freeform.text, Is.EqualTo(submittedMessage),
                    "네트워크 실패 후 재시도 UI는 초안을 그대로 복원해야 합니다.");
                view.CompleteFreeformSubmission();
                Assert.That(freeform.text, Is.Empty,
                    "권위 서버가 성공한 뒤에만 자유입력 초안을 비웁니다.");
                Assert.That(characterCount.text, Is.EqualTo("0/1000자"));
                Color selectedColor = first.GetComponent<Image>().color;
                Color defaultColor = second.GetComponent<Image>().color;
                Assert.That(story.overflowMode, Is.EqualTo(TextOverflowModes.Ellipsis),
                    "긴 대사가 자연어 입력창 위로 넘쳐 그려지면 안 됩니다.");
                freeform.text = new string('가', 1000);
                Assert.That(characterCount.text, Is.EqualTo("1000/1000자"));
                freeform.onSelect.Invoke(string.Empty);
                Assert.That(view.IsFreeformFocused, Is.True,
                    "입력창 내부 텍스트가 선택되어도 자연어 입력 포커스로 인식해야 합니다.");
                view.MoveChoiceSelection(1);
                view.ConfirmChoiceSelection();
                first.onClick.Invoke();
                Assert.That(view.KeyboardChoiceIndex, Is.EqualTo(0),
                    "자연어 입력 중 W/S는 선택지를 이동하면 안 됩니다.");
                Assert.That(selections, Is.Zero,
                    "자연어 입력 중 Return이나 stale Button submit은 선택지를 확정하면 안 됩니다.");

                freeform.onDeselect.Invoke(string.Empty);
                view.MoveChoiceSelection(1);
                Assert.That(view.KeyboardChoiceIndex, Is.EqualTo(1));
                Assert.That(first.GetComponent<Image>().color, Is.EqualTo(defaultColor),
                    "키보드 선택이 이동하면 이전 버튼은 기본 배경색으로 돌아가야 합니다.");
                Assert.That(second.GetComponent<Image>().color, Is.EqualTo(selectedColor),
                    "키보드로 선택된 버튼의 배경색도 선택 강조색으로 바뀌어야 합니다.");
                Assert.That(first.GetComponent<Button>().transition, Is.EqualTo(Selectable.Transition.None));
                Assert.That(second.GetComponent<Button>().transition, Is.EqualTo(Selectable.Transition.None),
                    "Unity Button ColorTint가 수동 선택 강조색을 다음 프레임에 덮어쓰면 안 됩니다.");
                Assert.That(second.GetComponentInChildren<TMP_Text>().text,
                    Does.StartWith("▶"));
                view.ConfirmChoiceSelection();
                view.ConfirmChoiceSelection();

                Assert.That(first.GetComponentInChildren<TMP_Text>().text,
                    Does.Contain("어떤 일이 있었는지 먼저 들려줘."));
                Assert.That(first.GetComponentInChildren<TMP_Text>().font, Is.SameAs(story.font),
                    "런타임 선택지는 대화창과 같은 한글 TMP 폰트를 사용해야 합니다.");
                Assert.That(selectedId, Is.EqualTo("guard"));
                Assert.That(selections, Is.EqualTo(1));
                Assert.That(second.interactable, Is.False);
                Assert.That(second.GetComponent<Image>().color, Is.Not.EqualTo(selectedColor),
                    "A submitted/disabled choice must not retain the gold active-selection appearance.");
                Assert.That(strip.transform.Find("Choice 5").gameObject.activeSelf, Is.False);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void NetworkClients_UseEnvironmentOverridesUnlessConstructorIsExplicit()
        {
            const string gameVariable = "KW_GAME_SERVER_URL";
            const string gmVariable = "KW_GM_ENDPOINT";
            string oldGame = Environment.GetEnvironmentVariable(gameVariable);
            string oldGm = Environment.GetEnvironmentVariable(gmVariable);
            try
            {
                Environment.SetEnvironmentVariable(gameVariable, "http://10.0.0.25:9123/root/");
                Environment.SetEnvironmentVariable(gmVariable, null);
                Assert.That(PrivateString(new GameApiClient(), "_baseUrl"),
                    Is.EqualTo("http://10.0.0.25:9123/root"));
                Assert.That(PrivateString(new SceneTransitionClient(), "_baseUrl"),
                    Is.EqualTo("http://10.0.0.25:9123/root"));
                Assert.That(PrivateString(new GmNarrativeClient(), "_endpoint"),
                    Is.EqualTo("http://10.0.0.25:9123/root/v1/gm/narrate"));

                Environment.SetEnvironmentVariable(gmVariable, "http://10.0.0.40:9444/custom-gm/");
                Assert.That(PrivateString(new GmNarrativeClient(), "_endpoint"),
                    Is.EqualTo("http://10.0.0.40:9444/custom-gm"));

                Assert.That(PrivateString(new GameApiClient("http://127.0.0.9:7001/"), "_baseUrl"),
                    Is.EqualTo("http://127.0.0.9:7001"));
                Assert.That(PrivateString(new SceneTransitionClient("http://127.0.0.9:7002/"), "_baseUrl"),
                    Is.EqualTo("http://127.0.0.9:7002"));
                Assert.That(PrivateString(new GmNarrativeClient("http://127.0.0.9:7003/narrate"), "_endpoint"),
                    Is.EqualTo("http://127.0.0.9:7003/narrate"));
            }
            finally
            {
                Environment.SetEnvironmentVariable(gameVariable, oldGame);
                Environment.SetEnvironmentVariable(gmVariable, oldGm);
            }
        }

        [Test]
        public void RunDto_ParsesPendingChoiceSetForTurnZeroRecovery()
        {
            const string json = "{\"pendingChoiceSet\":{\"choiceSetId\":\"opening-1\"," +
                                "\"reason\":\"어떻게 답할까?\",\"choices\":[" +
                                "{\"choiceId\":\"a\",\"text\":\"먼저 들어 본다.\",\"choiceKind\":\"DIALOGUE\"}," +
                                "{\"choiceId\":\"b\",\"text\":\"흔적을 살핀다.\",\"choiceKind\":\"SKILL\",\"skillId\":\"SEARCH\"}]}}";

            GameApiClient.RunSnapshot run = JsonUtility.FromJson<GameApiClient.RunSnapshot>(json);

            Assert.That(run.pendingChoiceSet.choiceSetId, Is.EqualTo("opening-1"));
            Assert.That(run.pendingChoiceSet.choices, Has.Length.EqualTo(2));
            Assert.That(run.pendingChoiceSet.choices[1].skillId, Is.EqualTo("SEARCH"));
        }

        [Test]
        public void RunDto_UsesCanonicalProgressFieldsWithoutLegacyAliases()
        {
            const string json = "{\"version\":4,\"progressLevel\":2," +
                                "\"progressTokens\":[\"ACCESS_LOCAL\",\"ACCESS_RELATIONSHIP\"]," +
                                "\"rootSystemGate\":{\"eligible\":false,\"requiredAdminLevel\":3}}";
            GameApiClient.RunSnapshot run = JsonUtility.FromJson<GameApiClient.RunSnapshot>(json);
            var adapter = new ServerRunPresentationAdapter(() => run);

            RunPresentationModel presentation = adapter.Capture(null);

            Assert.That(run.progressTokens, Has.Length.EqualTo(2));
            Assert.That(run.rootSystemGate.requiredAdminLevel, Is.EqualTo(3));
            Assert.That(presentation.AdminAccess, Is.EqualTo(2));
        }

        [Test]
        public void RuntimeUnityEvents_MapToAuthoritativeAnimationCommands()
        {
            var run = new GameApiClient.RunSnapshot { playerEntityId = Guid.NewGuid().ToString() };
            var targetId = Guid.NewGuid().ToString();
            var turn = new GameApiClient.TurnSnapshot
            {
                runtime = new GameApiClient.RuntimeSnapshot
                {
                    unity = new GameApiClient.RuntimeUnitySnapshot
                    {
                        renderRequired = true,
                        events = new[]
                        {
                            new GameApiClient.RuntimeUnityEventSnapshot
                            {
                                eventId = "turn:unity:0",
                                type = "ATTACK",
                                actorId = "PROTAGONIST_NUPJUKYI",
                                targetIds = new[] { targetId },
                                payload = new GameApiClient.RuntimeUnityEventPayloadSnapshot
                                {
                                    gameplayResult = new GameApiClient.GameplayResultSnapshot
                                    {
                                        succeeded = true,
                                        result = new GameApiClient.GameplayResultDetailSnapshot
                                        {
                                            hit = true,
                                            damage = 4
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            };

            GameApiClient.SceneActionSnapshot[] sequence =
                ServerTurnPresentationAdapter.BuildRuntimeRenderSequence(turn, run);

            Assert.That(sequence, Has.Length.EqualTo(2));
            Assert.That(sequence[0].type, Is.EqualTo("ATTACK"));
            Assert.That(sequence[0].actorId, Is.EqualTo(run.playerEntityId));
            Assert.That(sequence[0].targetId, Is.EqualTo(targetId));
            Assert.That(sequence[1].type, Is.EqualTo("DAMAGE"));
            Assert.That(sequence[1].actorId, Is.EqualTo(targetId));
        }

        private static TMP_Text CreateText(Transform parent, string name)
        {
            var value = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            value.transform.SetParent(parent, false);
            return value.GetComponent<TMP_Text>();
        }

        private static string PrivateString(object target, string fieldName)
        {
            FieldInfo field = target.GetType().GetField(fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            return (string)field.GetValue(target);
        }

        private static int PrivateIntProperty(object target, string propertyName)
        {
            PropertyInfo property = target.GetType().GetProperty(propertyName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(property, Is.Not.Null);
            return (int)property.GetValue(target);
        }

        private static RunPresentationState State(GridCoord player, GridCoord? selected, int page)
        {
            return new RunPresentationState(1, 1, "layout", player, selected, null, AbilityKind.Move,
                1, page, "dialogue", false, false, false);
        }

        private static GridCoord FindWalkableNeighbour(RunView view)
        {
            GridCoord origin = view.PlayerPosition;
            GridCoord[] candidates =
            {
                new GridCoord(origin.X + 1, origin.Y), new GridCoord(origin.X - 1, origin.Y),
                new GridCoord(origin.X, origin.Y + 1), new GridCoord(origin.X, origin.Y - 1)
            };
            foreach (GridCoord candidate in candidates)
                if (view.Region.Contains(candidate) && view.Region.GetTile(candidate).IsWalkable)
                    return candidate;
            Assert.Fail("The generated player spawn has no walkable neighbour.");
            return origin;
        }

        private static IEnumerator CompleteAfterFrame(System.Action<TurnGatewayResult> done)
        {
            yield return null;
            done(TurnGatewayResult.Failure("TEST_ERROR", "simulated"));
        }

        private static IEnumerator Empty() { yield break; }

        private static void Drain(IEnumerator operation)
        {
            while (operation.MoveNext())
            {
                if (operation.Current is IEnumerator nested)
                    Drain(nested);
            }
        }
    }
}
