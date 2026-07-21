using System.Collections;
using KeyboardWanderer.Core;
using KeyboardWanderer.Demo;
using KeyboardWanderer.Gameplay;
using KeyboardWanderer.Networking;
using KeyboardWanderer.Presentation;
using NUnit.Framework;

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
            Assert.That(statusValues, Does.Contain(run.AdminAccess + " / 3"));
            Assert.That(KeyboardWandererHudTextComposer.AbilityPlayerLabel(AbilityKind.Search),
                Is.EqualTo("Ctrl F 조사"));
            Assert.That(secondary.Split('\n').Length, Is.LessThanOrEqualTo(3));
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
            Assert.That(result.StateChanges, Does.Contain("ENTITY INVESTIGATED"));
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
            Assert.That(result.Attempt, Does.Contain("Ctrl F 조사"));
            Assert.That(result.ActionContext, Is.EqualTo("조사"));
            Assert.That(result.Outcome, Is.EqualTo("성공"));
            Assert.That(result.Narrative, Does.Contain("숨겨진 단서"));
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
