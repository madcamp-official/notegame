using System;
using System.Collections.Generic;
using KeyboardWanderer.Core;
using KeyboardWanderer.Gameplay;
using KeyboardWanderer.Presentation;

namespace KeyboardWanderer.Networking
{
    /// <summary>
    /// 서버 RunSnapshot을 HUD·선택 계층이 읽는 공통 RunPresentationModel로 변환한다.
    /// GameApiClient DTO 해석, 증거 대상 탐색, 지역·바이옴 이름 결정은 이 경계 안에서 끝낸다.
    /// </summary>
    public sealed class ServerRunPresentationAdapter : IRunPresentationAdapter
    {
        private readonly Func<GameApiClient.RunSnapshot> _current;
        private readonly Func<GameApiClient.CampaignSnapshot> _campaign;
        private long _cachedVersion = long.MinValue;
        private long _cachedFallbackVersion = long.MinValue;
        private GameApiClient.CampaignSnapshot _cachedCampaign;
        private RunPresentationModel _cached = RunPresentationModel.Empty;

        public ServerRunPresentationAdapter(Func<GameApiClient.RunSnapshot> current,
            Func<GameApiClient.CampaignSnapshot> campaign = null)
        {
            _current = current ?? throw new ArgumentNullException(nameof(current));
            _campaign = campaign;
        }

        public RunPresentationModel Capture(RunView fallback)
        {
            GameApiClient.RunSnapshot run = _current();
            if (run == null)
                return new LocalRunPresentationAdapter().Capture(fallback);
            long fallbackVersion = fallback?.Version ?? long.MinValue;
            GameApiClient.CampaignSnapshot campaign = _campaign?.Invoke();
            if (_cachedVersion == run.version && _cachedFallbackVersion == fallbackVersion &&
                ReferenceEquals(_cachedCampaign, campaign))
                return _cached;

            GridCoord fallbackPlayerPosition = fallback?.PlayerPosition ?? default;
            var entities = BuildEntities(run, fallbackPlayerPosition, out GridCoord playerPosition,
                out int playerHealth, out int playerMaxHealth);
            AbilityKind objectiveAbility = AbilityFrom(run.currentStoryBeat?.requiredAbility);
            FindObjectiveTarget(run, entities, playerPosition, out Guid? objectiveTargetId,
                out string objectiveTargetName, out GridCoord? objectiveTargetPosition);
            string layoutHash = !string.IsNullOrWhiteSpace(run.world?.layoutHash)
                ? run.world.layoutHash
                : fallback?.Region?.LayoutHash;
            string premise = FirstNonEmpty(run.premise, campaign?.premiseKo, campaign?.premise,
                fallback?.CampaignPremise,
                "넙죽이는 코드리아에서 관리자 키보드와 권한 3단계를 찾아 ROOT_SYSTEM으로 향합니다.");
            string storyBeat = FirstNonEmpty(run.currentBeat, run.currentStoryBeat?.title,
                fallback?.CurrentStoryBeat, "첫 장면을 확정하세요");
            string storyObjective = StoryObjective(run, fallback);

            _cached = new RunPresentationModel
            {
                Core = new RunPresentationCore(run.version, run.currentTurn, layoutHash, playerPosition),
                IsServerAuthoritative = true,
                IsPlaying = string.Equals(run.status, "active", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(run.status, "playing", StringComparison.OrdinalIgnoreCase),
                Status = fallback?.Status ?? RunStatus.Playing,
                TurnLimit = run.turnLimit > 0 ? run.turnLimit : fallback?.TurnLimit ?? 0,
                RemainingTurns = Math.Max(0, run.remainingTurns),
                Focus = run.focus,
                MaxFocus = run.maxFocus > 0 ? run.maxFocus : Math.Max(10, run.focus),
                Experience = run.experience,
                Gold = run.gold,
                Pressure = run.pressure,
                AdminAccess = Math.Max(0, Math.Min(3, run.adminLevel)),
                Health = playerMaxHealth > 0 ? playerHealth : fallback?.Health ?? run.health,
                MaxHealth = playerMaxHealth > 0 ? playerMaxHealth : fallback?.MaxHealth ?? run.maxHealth,
                EndingCode = run.endingCode ?? string.Empty,
                PlayerName = CampaignCatalog.ProtagonistName,
                CampaignTitle = CampaignCatalog.CampaignTitle,
                CampaignPremise = premise,
                StoryBeat = storyBeat,
                StoryObjective = storyObjective,
                CurrentAreaName = CurrentAreaName(run.world, playerPosition, fallback?.CurrentAreaName),
                CurrentBiomeId = CurrentBiomeId(run.world, playerPosition, fallback),
                CurrentRegionAxis = fallback?.CurrentRegionAxis ?? string.Empty,
                ObjectiveAbility = objectiveAbility,
                ObjectiveTargetId = objectiveTargetId,
                ObjectiveTargetName = objectiveTargetName,
                ObjectiveTargetPosition = objectiveTargetPosition,
                OpenLoops = OpenLoopSummaries(run, fallback),
                RequiredBeats = BuildBeats(campaign, run, fallback),
                Entities = entities,
                EndingBoard = BuildEndingBoard(run, fallback)
            };
            _cachedVersion = run.version;
            _cachedFallbackVersion = fallbackVersion;
            _cachedCampaign = campaign;
            return _cached;
        }

        private static IReadOnlyList<RunPresentationEnding> BuildEndingBoard(GameApiClient.RunSnapshot run,
            RunView fallback)
        {
            if (run.endingConditionReports != null && run.endingConditionReports.Length > 0)
            {
                var reports = new List<RunPresentationEnding>();
                for (int i = 0; i < run.endingConditionReports.Length; i++)
                {
                    GameApiClient.EndingConditionReportSnapshot source = run.endingConditionReports[i];
                    if (source == null) continue;
                    var missing = new List<string>();
                    if (source.conditions != null)
                        for (int j = 0; j < source.conditions.Length; j++)
                            if (source.conditions[j] != null && !source.conditions[j].satisfied)
                                missing.Add(source.conditions[j].label ?? string.Empty);
                    reports.Add(new RunPresentationEnding
                    {
                        Code = source.id ?? string.Empty, Title = FirstNonEmpty(source.title, source.id),
                        IsEligible = source.eligible, SatisfiedCount = source.satisfiedCount,
                        TotalCount = source.totalCount, MissingConditions = missing
                    });
                }
                return reports;
            }
            if (run.endingCandidateDetails == null || run.endingCandidateDetails.Length == 0)
                return new LocalRunPresentationAdapter().Capture(fallback).EndingBoard;
            var values = new List<RunPresentationEnding>();
            for (int i = 0; i < run.endingCandidateDetails.Length; i++)
            {
                GameApiClient.EndingCandidateSnapshot source = run.endingCandidateDetails[i];
                if (source == null || string.Equals(source.id, CampaignCatalog.FallbackEndingCode,
                        StringComparison.Ordinal)) continue;
                values.Add(new RunPresentationEnding
                {
                    Code = source.id ?? string.Empty,
                    Title = FirstNonEmpty(source.title, source.id, "알 수 없는 결말"),
                    IsEligible = source.eligible,
                    SatisfiedCount = source.eligible ? 1 : 0,
                    TotalCount = 1,
                    MissingConditions = source.eligible ? Array.Empty<string>() :
                        new[] { "서버 Root 퍼즐의 연결·노드·메트릭 조건" }
                });
            }
            values.Sort((left, right) => left.IsEligible != right.IsEligible
                ? (left.IsEligible ? -1 : 1)
                : string.CompareOrdinal(left.Code, right.Code));
            return values;
        }

        private static IReadOnlyList<RunPresentationEntity> BuildEntities(GameApiClient.RunSnapshot run,
            GridCoord fallbackPlayerPosition,
            out GridCoord playerPosition, out int playerHealth, out int playerMaxHealth)
        {
            playerPosition = fallbackPlayerPosition;
            playerHealth = 0;
            playerMaxHealth = 0;
            var values = new List<RunPresentationEntity>();
            if (run.entities == null)
                return values;
            for (int i = 0; i < run.entities.Length; i++)
            {
                GameApiClient.EntitySnapshot source = run.entities[i];
                if (source?.position == null)
                    continue;
                bool isPlayer = string.Equals(source.id, run.playerEntityId, StringComparison.OrdinalIgnoreCase);
                int health = source.state?.hp ?? 0;
                int maxHealth = source.state?.maxHp ?? 0;
                var position = new GridCoord(source.position.x, source.position.y);
                if (isPlayer)
                {
                    playerPosition = position;
                    playerHealth = health;
                    playerMaxHealth = maxHealth;
                }
                // 선택 대상 식별자는 기존 선택 계약상 Guid여야 한다. 위치·체력은 문자열 ID여도 보존한다.
                if (!Guid.TryParse(source.id, out Guid id))
                    continue;
                RunPresentationEntityKind kind = KindFor(source.kind, isPlayer);
                EntityKind gameplayKind = GameplayKindFor(kind);
                bool active = source.state == null ||
                              (!source.state.disabled && !source.state.defeated && !source.state.fled &&
                               (maxHealth <= 0 || health > 0));
                EntityCapabilities capabilities = EntityCapabilityCatalog.Resolve(gameplayKind, active,
                    kind == RunPresentationEntityKind.Enemy, source.@protected, source.cloneable, source.assetId);
                GameApiClient.EntityCapabilitySnapshot serverCapabilities = source.capabilities;
                values.Add(new RunPresentationEntity
                {
                    Id = id,
                    Kind = kind,
                    AssetId = source.assetId ?? string.Empty,
                    Name = FirstNonEmpty(source.name, source.assetId, "이름 없는 대상"),
                    Position = position,
                    Health = health,
                    MaxHealth = maxHealth,
                    IsPlayer = isPlayer,
                    IsHostile = kind == RunPresentationEntityKind.Enemy,
                    IsActive = active,
                    IsProtected = source.@protected,
                    IsCloneable = source.cloneable,
                    CanCopy = serverCapabilities?.canCopy ?? capabilities.CanCopy,
                    CanDelete = serverCapabilities?.canDelete ?? capabilities.CanDelete,
                    CanConnect = serverCapabilities?.canConnect ?? capabilities.CanConnect,
                    CanRestore = serverCapabilities?.canRestore ?? capabilities.CanRestore,
                    CanInteract = serverCapabilities?.canInteract ?? capabilities.CanInteract,
                    RequiredSkillId = source.state?.requiredSkillId ?? string.Empty,
                    DeleteRequiredAdminAccess = serverCapabilities?.requiredAdminAccess ??
                                                capabilities.RequiredAdminAccess
                });
            }
            return values;
        }

        private static EntityKind GameplayKindFor(RunPresentationEntityKind kind)
        {
            switch (kind)
            {
                case RunPresentationEntityKind.Player: return EntityKind.Player;
                case RunPresentationEntityKind.Npc: return EntityKind.Npc;
                case RunPresentationEntityKind.Enemy: return EntityKind.Enemy;
                case RunPresentationEntityKind.Prop: return EntityKind.Prop;
                default: return EntityKind.Effect;
            }
        }

        private static void FindObjectiveTarget(GameApiClient.RunSnapshot run,
            IReadOnlyList<RunPresentationEntity> entities, GridCoord playerPosition,
            out Guid? targetId, out string targetName, out GridCoord? targetPosition)
        {
            targetId = null;
            targetName = string.Empty;
            targetPosition = null;
            GameApiClient.StoryBeatSnapshot beat = run.currentStoryBeat;
            if (beat == null || run.entities == null)
                return;
            int bestDistance = int.MaxValue;
            for (int i = 0; i < run.entities.Length; i++)
            {
                GameApiClient.EntitySnapshot source = run.entities[i];
                if (source?.position == null || source.state == null || !Guid.TryParse(source.id, out Guid id))
                    continue;
                bool evidenceMatches = !string.IsNullOrWhiteSpace(beat.requiredEvidenceKey) &&
                                       string.Equals(source.state.evidenceKey, beat.requiredEvidenceKey,
                                           StringComparison.OrdinalIgnoreCase);
                bool roleMatches = string.IsNullOrWhiteSpace(beat.requiredEvidenceKey) &&
                                   !string.IsNullOrWhiteSpace(beat.requiredCampaignRole) &&
                                   string.Equals(source.state.campaignRole, beat.requiredCampaignRole,
                                       StringComparison.OrdinalIgnoreCase);
                if (!evidenceMatches && !roleMatches)
                    continue;
                var position = new GridCoord(source.position.x, source.position.y);
                int distance = playerPosition.ManhattanDistance(position);
                if (targetId.HasValue && distance >= bestDistance)
                    continue;
                targetId = id;
                targetName = FirstNonEmpty(source.name, source.assetId, "표시된 대상");
                targetPosition = position;
                bestDistance = distance;
            }
        }

        private static IReadOnlyList<RunPresentationBeat> BuildBeats(GameApiClient.CampaignSnapshot campaign,
            GameApiClient.RunSnapshot run, RunView fallback)
        {
            GameApiClient.StoryBeatSnapshot[] source = campaign?.requiredStoryBeats;
            if (source != null && source.Length > 0)
            {
                var values = new RunPresentationBeat[source.Length];
                for (int i = 0; i < source.Length; i++)
                {
                    GameApiClient.StoryBeatSnapshot beat = source[i];
                    values[i] = new RunPresentationBeat
                    {
                        Title = beat?.title ?? string.Empty,
                        Objective = beat?.description ?? string.Empty,
                        Ability = AbilityFrom(beat?.requiredAbility),
                        IsCompleted = string.Equals(beat?.status, "completed", StringComparison.OrdinalIgnoreCase),
                        IsSkipped = string.Equals(beat?.status, "skipped", StringComparison.OrdinalIgnoreCase)
                    };
                }
                return values;
            }
            if (run.currentStoryBeat != null)
            {
                return new[]
                {
                    new RunPresentationBeat
                    {
                        Title = run.currentStoryBeat.title ?? string.Empty,
                        Objective = run.currentStoryBeat.description ?? string.Empty,
                        Ability = AbilityFrom(run.currentStoryBeat.requiredAbility),
                        IsCompleted = false,
                        IsSkipped = false
                    }
                };
            }
            return new LocalRunPresentationAdapter().Capture(fallback).RequiredBeats;
        }

        private static IReadOnlyList<string> OpenLoopSummaries(GameApiClient.RunSnapshot run, RunView fallback)
        {
            var values = new List<string>();
            if (run.openLoops != null)
            {
                for (int i = 0; i < run.openLoops.Length; i++)
                {
                    string summary = run.openLoops[i]?.summary;
                    if (!string.IsNullOrWhiteSpace(summary))
                        values.Add(summary.Trim());
                }
            }
            if (values.Count == 0 && fallback?.OpenLoops != null)
                values.AddRange(fallback.OpenLoops);
            return values;
        }

        private static string StoryObjective(GameApiClient.RunSnapshot run, RunView fallback)
        {
            if (!string.IsNullOrWhiteSpace(run.currentStoryBeat?.description))
                return run.currentStoryBeat.description;
            if (run.activeQuests != null && run.activeQuests.Length > 0)
            {
                GameApiClient.QuestSnapshot quest = run.activeQuests[0];
                string questText = FirstNonEmpty(quest?.summary, quest?.currentStep);
                if (!string.IsNullOrWhiteSpace(questText))
                    return questText;
            }
            return FirstNonEmpty(fallback?.CurrentStoryBeatObjective,
                "목적지 또는 관리자 키보드 스킬과 대상을 선택하세요.");
        }

        private static string CurrentAreaName(GameApiClient.WorldSnapshot world, GridCoord position,
            string fallback)
        {
            if (world != null)
            {
                GameApiClient.AreaSnapshot[] areas = world.areas;
                if (areas != null)
                {
                    string mappedId = world.AreaIdAt(position.X, position.Y);
                    for (int i = 0; i < areas.Length; i++)
                    {
                        GameApiClient.AreaSnapshot area = areas[i];
                        if (area == null)
                            continue;
                        if (!string.IsNullOrWhiteSpace(mappedId) &&
                            string.Equals(area.id, mappedId, StringComparison.Ordinal))
                            return FirstNonEmpty(area.nameKo, area.name, area.id);
                        GameApiClient.BoundsSnapshot bounds = area.bounds;
                        if (bounds != null && position.X >= bounds.x && position.X < bounds.x + bounds.width &&
                            position.Y >= bounds.y && position.Y < bounds.y + bounds.height)
                            return FirstNonEmpty(area.nameKo, area.name, area.id);
                    }
                }
                GameApiClient.PointSnapshot nearest = null;
                int best = int.MaxValue;
                if (world.points != null)
                {
                    for (int i = 0; i < world.points.Length; i++)
                    {
                        GameApiClient.PointSnapshot point = world.points[i];
                        if (point == null)
                            continue;
                        int distance = Math.Abs(position.X - point.x) + Math.Abs(position.Y - point.y);
                        if (distance < best)
                        {
                            best = distance;
                            nearest = point;
                        }
                    }
                }
                if (!string.IsNullOrWhiteSpace(nearest?.name))
                    return nearest.name;
            }
            return FirstNonEmpty(fallback, "알 수 없는 변경지대");
        }

        private static string CurrentBiomeId(GameApiClient.WorldSnapshot world, GridCoord position,
            RunView fallback)
        {
            string mapped = world?.BiomeIdAt(position.X, position.Y);
            if (!string.IsNullOrWhiteSpace(mapped))
                return mapped;
            if (world?.areas != null)
            {
                for (int i = 0; i < world.areas.Length; i++)
                {
                    GameApiClient.AreaSnapshot area = world.areas[i];
                    GameApiClient.BoundsSnapshot bounds = area?.bounds;
                    if (bounds != null && position.X >= bounds.x && position.X < bounds.x + bounds.width &&
                        position.Y >= bounds.y && position.Y < bounds.y + bounds.height)
                        return area.biomeId ?? string.Empty;
                }
            }
            return new LocalRunPresentationAdapter().Capture(fallback).CurrentBiomeId;
        }

        private static RunPresentationEntityKind KindFor(string kind, bool isPlayer)
        {
            if (isPlayer) return RunPresentationEntityKind.Player;
            if (string.Equals(kind, "npc", StringComparison.OrdinalIgnoreCase)) return RunPresentationEntityKind.Npc;
            if (string.Equals(kind, "enemy", StringComparison.OrdinalIgnoreCase)) return RunPresentationEntityKind.Enemy;
            if (string.Equals(kind, "prop", StringComparison.OrdinalIgnoreCase)) return RunPresentationEntityKind.Prop;
            return RunPresentationEntityKind.Unknown;
        }

        private static AbilityKind AbilityFrom(string value)
        {
            if (!Enum.TryParse(value, true, out AbilityKind ability))
                return AbilityKind.Copy;
            return ability == AbilityKind.Move || TurnRequest.IsPublicKeyboardSkill(ability)
                ? ability
                : AbilityKind.Copy;
        }

        private static string FirstNonEmpty(params string[] values)
        {
            if (values == null)
                return string.Empty;
            for (int i = 0; i < values.Length; i++)
                if (!string.IsNullOrWhiteSpace(values[i]))
                    return values[i].Trim();
            return string.Empty;
        }
    }
}
