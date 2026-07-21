using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using KeyboardWanderer.Core;
using KeyboardWanderer.Demo;
using KeyboardWanderer.Gameplay;
using KeyboardWanderer.Networking;
using KeyboardWanderer.Presentation;
using KeyboardWanderer.Runtime;
using KeyboardWanderer.World;
using NUnit.Framework;
using UnityEngine;

namespace KeyboardWanderer.Tests.EditMode
{
    public sealed class RuntimeCompositionTests
    {
        [Test]
        public void GameFlowStateMachine_OnlyAcceptsCommandsAtChoiceBoundaries()
        {
            var flow = new GameFlowStateMachine();
            flow.Refresh(Signals(resolving: true));
            Assert.That(flow.Phase, Is.EqualTo(GameFlowPhase.ResolvingChoice));
            Assert.That(flow.CanIssueGameplayCommand, Is.False);

            flow.Refresh(Signals(intervention: true));
            Assert.That(flow.Phase, Is.EqualTo(GameFlowPhase.AwaitingChoice));
            Assert.That(flow.CanIssueAbility(AbilityKind.Move), Is.True);

            flow.Refresh(Signals());
            Assert.That(flow.Phase, Is.EqualTo(GameFlowPhase.WaitingForNarrative));
            Assert.That(flow.CanIssueAbility(AbilityKind.Search), Is.False);
        }

        [Test]
        public void GameFlowStateMachine_SealedNarrativeChoiceBlocksDirectSkills()
        {
            var flow = new GameFlowStateMachine();
            flow.Refresh(new GameFlowSignals(false, false, true, true, false, false,
                false, false, false, false, true, false, true));

            Assert.That(flow.Phase, Is.EqualTo(GameFlowPhase.AwaitingNarrativeChoice));
            Assert.That(flow.CanSelectNarrativeChoice, Is.True);
            Assert.That(flow.CanIssueGameplayCommand, Is.False);
            Assert.That(flow.CanIssueAbility(AbilityKind.Search), Is.False);
        }

        [Test]
        public void GameFlowStateMachine_EncounterAllowsSkillsButRejectsMove()
        {
            var flow = new GameFlowStateMachine();
            flow.Refresh(Signals(encounter: true, intervention: true));
            Assert.That(flow.Phase, Is.EqualTo(GameFlowPhase.AwaitingEncounterChoice));
            Assert.That(flow.CanIssueAbility(AbilityKind.Search), Is.True);
            Assert.That(flow.CanIssueAbility(AbilityKind.Move), Is.False);
            Assert.That(flow.BlockReason(AbilityKind.Move), Does.Contain("AwaitingEncounterChoice"));
        }

        [Test]
        public void GameFlowStateMachine_PresentationTakesPriorityOverEncounterChoice()
        {
            var flow = new GameFlowStateMachine();
            flow.Refresh(Signals(story: true, encounter: true));
            Assert.That(flow.Phase, Is.EqualTo(GameFlowPhase.PresentingStory));
            Assert.That(flow.CanIssueGameplayCommand, Is.False);
        }

        [Test]
        public void GameFlowStateMachine_TutorialPreservesInteractivePractice()
        {
            var flow = new GameFlowStateMachine();
            flow.Refresh(new GameFlowSignals(false, false, true, true, false, true,
                false, false, false, false, false, false));
            Assert.That(flow.Phase, Is.EqualTo(GameFlowPhase.Tutorial));
            Assert.That(flow.CanIssueAbility(AbilityKind.Search), Is.False);
        }

        private static GameFlowSignals Signals(bool resolving = false, bool story = false,
            bool encounter = false, bool intervention = false)
        {
            return new GameFlowSignals(false, false, true, true, false, false,
                resolving, false, false, story, intervention, encounter);
        }

        [Test]
        public void DestructiveActions_RequireSecondMatchingInputAndCanBeCancelled()
        {
            var confirmation = new DestructiveActionConfirmation();
            Guid target = Guid.NewGuid();
            Assert.That(confirmation.RequiresConfirmation(AbilityKind.Delete, target, 10f), Is.True);
            Assert.That(confirmation.IsArmed(11f), Is.True);
            Assert.That(confirmation.RequiresConfirmation(AbilityKind.Delete, target, 11f), Is.False);
            Assert.That(confirmation.IsArmed(11f), Is.False);
            Assert.That(confirmation.RequiresConfirmation(AbilityKind.Move, null, 12f), Is.False);

            confirmation.RequiresConfirmation(AbilityKind.Undo, null, 20f);
            confirmation.Cancel();
            Assert.That(confirmation.IsArmed(20f), Is.False);
        }

        [Test]
        public void GameplayTelemetry_TracksOnlyBoundedSessionCounters()
        {
            var telemetry = new GameplayTelemetry();
            telemetry.RecordSubmit();
            telemetry.RecordInvalidSelection();
            telemetry.RecordConfirmation();
            telemetry.RecordCancellation();
            Assert.That(telemetry.DiagnosticSummary, Is.EqualTo("실행 1 · 선택 오류 1 · 안전 확인 1 · 확인 취소 1"));
        }

        [Test]
        public void SelectionController_OwnsSkillTargetAndCopyLifecycle()
        {
            var root = new GameObject("Selection Controller Test");
            try
            {
                var selection = root.AddComponent<KeyboardWandererSelectionController>();
                Guid source = Guid.NewGuid();
                selection.ResetSelection(AbilityKind.Copy);
                selection.SelectCopySource(source, "원본 선택");
                selection.SelectCopyDestination(new GridCoord(4, 7), "배치 선택");

                Assert.That(selection.Ability, Is.EqualTo(AbilityKind.Copy));
                Assert.That(selection.SelectedTarget, Is.EqualTo(source));
                Assert.That(selection.SelectedCoord, Is.EqualTo(new GridCoord(4, 7)));
                Assert.That(selection.CopySourceCaptured, Is.True);

                Assert.That(selection.ChangeAbility(AbilityKind.Delete), Is.True);
                Assert.That(selection.Ability, Is.EqualTo(AbilityKind.Delete));
                Assert.That(selection.SelectedTarget, Is.Null);
                Assert.That(selection.SelectedCoord, Is.Null);
                Assert.That(selection.CopySourceCaptured, Is.False);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void SelectionController_TracksTwoConnectTargetsInOrder()
        {
            var root = new GameObject("Connect Selection Test");
            try
            {
                var selection = root.AddComponent<KeyboardWandererSelectionController>();
                Guid first = Guid.NewGuid();
                Guid second = Guid.NewGuid();
                selection.ResetSelection(AbilityKind.Connect);

                Assert.That(selection.SelectConnectTarget(first, new GridCoord(1, 2)), Is.False);
                Assert.That(selection.SelectConnectTarget(second, new GridCoord(3, 4)), Is.True);
                Assert.That(selection.SelectedTarget, Is.EqualTo(first));
                Assert.That(selection.SelectedSecondaryTarget, Is.EqualTo(second));
                Assert.That(selection.SelectedCoord, Is.EqualTo(new GridCoord(3, 4)));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void TurnCoordinator_ConnectCarriesTargetsButNeverDestination()
        {
            var root = new GameObject("Connect Request Test");
            try
            {
                var selection = root.AddComponent<KeyboardWandererSelectionController>();
                Guid first = Guid.NewGuid();
                Guid second = Guid.NewGuid();
                selection.ResetSelection(AbilityKind.Connect);
                selection.SelectConnectTarget(first, new GridCoord(1, 2));
                selection.SelectConnectTarget(second, new GridCoord(3, 4));

                long version = (long)int.MaxValue + 37L;
                TurnRequest request = KeyboardWandererTurnCoordinator.BuildRequest(selection, version, "connect");

                Assert.That(request.TargetEntityId, Is.EqualTo(first));
                Assert.That(request.SecondaryTargetEntityId, Is.EqualTo(second));
                Assert.That(request.Destination, Is.Null,
                    "Connect는 두 엔티티를 연결할 뿐 서버에 목적지 타일을 보내면 안 됩니다.");
                Assert.That(request.ExpectedRunVersion, Is.EqualTo(version),
                    "서버 버전은 Int32 범위를 넘어도 손실 없이 유지해야 합니다.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void TurnCoordinator_PreservesSearchTargetAndNormalizesPendingLifetime()
        {
            var root = new GameObject("Turn Coordinator Test");
            try
            {
                var selection = root.AddComponent<KeyboardWandererSelectionController>();
                var coordinator = root.AddComponent<KeyboardWandererTurnCoordinator>();
                Guid target = Guid.NewGuid();
                selection.ResetSelection(AbilityKind.Search);
                selection.SelectPrimary(target);
                TurnRequest request = KeyboardWandererTurnCoordinator.BuildRequest(selection, 17, "test");

                Assert.That(request.Ability, Is.EqualTo(AbilityKind.Search));
                Assert.That(request.TargetEntityId, Is.EqualTo(target),
                    "조사는 선택한 대상을 로컬·서버 공통 요청에 포함해야 합니다.");
                Assert.That(request.ExpectedRunVersion, Is.EqualTo(17));

                var pendingEvents = new List<bool>();
                coordinator.PendingChanged += pendingEvents.Add;
                coordinator.Configure(new DelegatingTurnGateway((_, done) => CompleteAfterFrame(done)));
                TurnGatewayResult result = null;
                Drain(coordinator.Submit(request, value => result = value));

                Assert.That(result, Is.Not.Null);
                Assert.That(result.ErrorCode, Is.EqualTo("TEST_RESULT"));
                Assert.That(pendingEvents, Is.EqualTo(new[] { true, false }));
                Assert.That(coordinator.IsPending, Is.False);
                Assert.That(coordinator.ActiveRequest, Is.Null);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void AbilityAvailability_UsesTheSameDeleteRuleForLocalAndServerPresentation()
        {
            var root = new GameObject("Ability Availability Test");
            try
            {
                var selection = root.AddComponent<KeyboardWandererSelectionController>();
                var availability = root.AddComponent<KeyboardWandererAbilityAvailability>();

                RunState state = LocalTurnService.CreateDemo(20260720L).CreateSnapshot();
                EntityState player = state.Spatial.Entities.Single(entity => entity.EntityId == state.PlayerEntityId);
                GridCoord enemyPosition = FindAvailableNeighbour(state, player.Position);
                var enemy = new EntityState(Guid.NewGuid(), EntityKind.Enemy, "test.enemy", "테스트 적",
                    true, false, false, true, 4, state.Region.RegionId, enemyPosition);
                Assert.That(state.Spatial.Register(enemy, out string registerError), Is.True, registerError);
                var localService = new LocalTurnService(state, new SequenceD20Source(20));
                RunPresentationModel local = new LocalRunPresentationAdapter().Capture(localService.CurrentView);
                selection.ResetSelection(AbilityKind.Delete);
                selection.SelectPrimary(enemy.EntityId);

                Assert.That(availability.CanUse(AbilityKind.Delete, local, selection, null, 0), Is.True,
                    "로컬 적 대상은 공통 Delete 규칙으로 사용 가능해야 합니다.");

                Guid serverPlayerId = Guid.NewGuid();
                Guid serverEnemyId = Guid.NewGuid();
                var serverRun = new GameApiClient.RunSnapshot
                {
                    version = 2,
                    currentTurn = 1,
                    status = "active",
                    focus = 3,
                    maxFocus = 10,
                    playerEntityId = serverPlayerId.ToString(),
                    entities = new[]
                    {
                        new GameApiClient.EntitySnapshot
                        {
                            id = serverPlayerId.ToString(), kind = "player",
                            position = new GameApiClient.PositionSnapshot { x = 5, y = 5 },
                            state = new GameApiClient.EntityStateSnapshot { hp = 10, maxHp = 10 }
                        },
                        new GameApiClient.EntitySnapshot
                        {
                            id = serverEnemyId.ToString(), kind = "enemy", name = "서버 적",
                            position = new GameApiClient.PositionSnapshot { x = 7, y = 5 },
                            state = new GameApiClient.EntityStateSnapshot { hp = 4, maxHp = 4 }
                        }
                    }
                };
                RunPresentationModel server =
                    new ServerRunPresentationAdapter(() => serverRun).Capture(localService.CurrentView);
                selection.SelectPrimary(serverEnemyId);

                Assert.That(availability.CanUse(AbilityKind.Delete, server, selection, null, 0), Is.True,
                    "서버 적 대상도 로컬과 같은 Delete 규칙을 사용해야 합니다.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void MinimapRenderer_OwnsAndReusesItsRuntimeTexture()
        {
            var root = new GameObject("Minimap Renderer Test");
            try
            {
                var renderer = root.AddComponent<KeyboardWandererMinimapRenderer>();
                LocalTurnService service = LocalTurnService.CreateDemo(20260722L);
                RunPresentationModel run = new LocalRunPresentationAdapter().Capture(service.CurrentView);
                var landmarks = new List<GridCoord> { new GridCoord(2, 2) };

                Sprite first = renderer.Render(12, 10, "layout", run.Core.Version, run.Core.Turn,
                    run.Core.PlayerPosition, null, null, null, null, string.Empty,
                    landmarks, run.Entities, _ => Color.black, out string status);
                Sprite second = renderer.Render(12, 10, "layout", run.Core.Version, run.Core.Turn,
                    run.Core.PlayerPosition, null, null, null, null, string.Empty,
                    landmarks, run.Entities, _ => Color.white, out _);

                Assert.That(first, Is.Not.Null);
                Assert.That(second, Is.SameAs(first), "상태가 같으면 미니맵 Sprite를 다시 만들지 않아야 합니다.");
                Assert.That(first.texture.width, Is.EqualTo(80));
                Assert.That(status, Does.Contain("턴 " + run.Core.Turn));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void PathPlanner_UsesLocalOccupancyAndRepairsBrokenServerNavigation()
        {
            var root = new GameObject("Path Planner Test");
            try
            {
                var planner = root.AddComponent<KeyboardWandererPathPlanner>();
                LocalTurnService service = LocalTurnService.CreateDemo(20260723L);
                RunView view = service.CurrentView;
                GridCoord localGoal = FindAvailableNeighbour(service.CreateSnapshot(), view.PlayerPosition);

                Assert.That(planner.CanSelectDestination(view, null, view.PlayerPosition, localGoal), Is.True);
                Assert.That(planner.FindLocalVisualPath(view, view.PlayerPosition, localGoal).Count,
                    Is.GreaterThan(1));
                KeyboardWandererPathPlanner.RoutePreview localPreview = planner.Preview(view, null,
                    view.PlayerPosition, localGoal);
                Assert.That(localPreview.HasPath, Is.True);
                Assert.That(localPreview.Distance, Is.GreaterThan(0));
                Assert.That(localPreview.PlayerText(), Does.Contain("경로 정보"));

                var serverRun = new GameApiClient.RunSnapshot
                {
                    playerEntityId = Guid.NewGuid().ToString(),
                    world = new GameApiClient.WorldSnapshot
                    {
                        width = 3,
                        height = 1,
                        tileLegend = new[] { "floor" },
                        tileCodes = new[] { 0, 0, 0 }
                    }
                };
                var brokenNavigation = new GameApiClient.NavigationSnapshot
                {
                    path = new[]
                    {
                        new GameApiClient.PositionSnapshot { x = 0, y = 0 },
                        new GameApiClient.PositionSnapshot { x = 2, y = 0 }
                    }
                };
                List<GridCoord> repaired = planner.FindServerVisualPath(brokenNavigation, serverRun,
                    new GridCoord(0, 0), new GridCoord(2, 0));

                Assert.That(repaired, Is.EqualTo(new[]
                {
                    new GridCoord(0, 0), new GridCoord(1, 0), new GridCoord(2, 0)
                }));
                KeyboardWandererPathPlanner.RoutePreview serverPreview = planner.Preview(view, serverRun,
                    new GridCoord(0, 0), new GridCoord(2, 0));
                Assert.That(serverPreview.Distance, Is.EqualTo(2));
                Assert.That(serverPreview.IsSafe, Is.True);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void DecorationRenderer_ReusesReleasedAuthoredObjects()
        {
            var root = new GameObject("Decoration Renderer Test");
            var texture = new Texture2D(1, 1);
            Sprite sprite = null;
            try
            {
                Transform active = new GameObject("Active Decorations").transform;
                active.SetParent(root.transform, false);
                Transform pool = new GameObject("Decoration Pool").transform;
                pool.SetParent(root.transform, false);
                var renderer = root.AddComponent<KeyboardWandererBiomeDecorationRenderer>();
                renderer.Configure(active, pool);
                sprite = Sprite.Create(texture, new Rect(0, 0, 1, 1), Vector2.one * 0.5f);

                renderer.Spawn(null, "첫 장식", sprite, Vector3.one, Color.white, 10, 1f);
                GameObject first = active.GetChild(0).gameObject;
                Assert.That(renderer.ActiveCount, Is.EqualTo(1));

                renderer.ReleaseAll();
                Assert.That(renderer.ActiveCount, Is.Zero);
                Assert.That(first.transform.parent, Is.EqualTo(pool));
                Assert.That(first.activeSelf, Is.False);

                renderer.Spawn(null, "재사용 장식", sprite, Vector3.zero, Color.green, 20, 0.8f);
                Assert.That(active.GetChild(0).gameObject, Is.SameAs(first));
                Assert.That(renderer.ActiveCount, Is.EqualTo(1));
            }
            finally
            {
                if (sprite != null) UnityEngine.Object.DestroyImmediate(sprite);
                UnityEngine.Object.DestroyImmediate(texture);
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void SettingsController_OwnsPersistenceAndAppliesAuthoredAudioVolumes()
        {
            const string musicKey = "keyboard-wanderer.music-volume";
            const string sfxKey = "keyboard-wanderer.sfx-volume";
            const string gmKey = "keyboard-wanderer.gm-enabled";
            bool hadMusic = PlayerPrefs.HasKey(musicKey);
            bool hadSfx = PlayerPrefs.HasKey(sfxKey);
            bool hadGm = PlayerPrefs.HasKey(gmKey);
            float oldMusic = PlayerPrefs.GetFloat(musicKey, 0.65f);
            float oldSfx = PlayerPrefs.GetFloat(sfxKey, 0.8f);
            int oldGm = PlayerPrefs.GetInt(gmKey, 1);
            var root = new GameObject("Settings Controller Test");
            try
            {
                var music = root.AddComponent<AudioSource>();
                var sfxObject = new GameObject("SFX", typeof(AudioSource));
                sfxObject.transform.SetParent(root.transform, false);
                var audio = root.AddComponent<KeyboardWandererAudioController>();
                audio.Configure(music, sfxObject.GetComponent<AudioSource>());
                var settings = root.AddComponent<KeyboardWandererSettingsController>();
                settings.Configure(audio);

                settings.SetMusicVolume(0.2f);
                settings.SetSfxVolume(0.3f);
                settings.SetGmEnabled(false);

                Assert.That(settings.MusicVolume, Is.EqualTo(0.2f).Within(0.001f));
                Assert.That(settings.SfxVolume, Is.EqualTo(0.3f).Within(0.001f));
                Assert.That(settings.GmEnabled, Is.False);
                Assert.That(music.volume, Is.EqualTo(0.09f).Within(0.001f));
                Assert.That(sfxObject.GetComponent<AudioSource>().volume, Is.EqualTo(0.3f).Within(0.001f));
            }
            finally
            {
                RestorePlayerPref(musicKey, hadMusic, oldMusic);
                RestorePlayerPref(sfxKey, hadSfx, oldSfx);
                if (hadGm) PlayerPrefs.SetInt(gmKey, oldGm); else PlayerPrefs.DeleteKey(gmKey);
                PlayerPrefs.Save();
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static IEnumerator CompleteAfterFrame(Action<TurnGatewayResult> completed)
        {
            yield return null;
            completed(TurnGatewayResult.Failure("TEST_RESULT", "정규화된 테스트 응답"));
        }

        private static GridCoord FindAvailableNeighbour(RunState state, GridCoord origin)
        {
            GridCoord[] candidates =
            {
                new GridCoord(origin.X + 1, origin.Y), new GridCoord(origin.X - 1, origin.Y),
                new GridCoord(origin.X, origin.Y + 1), new GridCoord(origin.X, origin.Y - 1)
            };
            for (int i = 0; i < candidates.Length; i++)
            {
                GridCoord candidate = candidates[i];
                if (state.Region.Contains(candidate) && state.Region.GetTile(candidate).IsWalkable &&
                    !state.Spatial.IsBlockingOccupied(state.Region.RegionId, candidate, 0))
                    return candidate;
            }
            Assert.Fail("스킬 판정 테스트용 인접 빈 타일을 찾지 못했습니다.");
            return origin;
        }

        private static void Drain(IEnumerator operation)
        {
            while (operation.MoveNext())
                if (operation.Current is IEnumerator nested)
                    Drain(nested);
        }

        private static void RestorePlayerPref(string key, bool existed, float value)
        {
            if (existed) PlayerPrefs.SetFloat(key, value); else PlayerPrefs.DeleteKey(key);
        }
    }
}
