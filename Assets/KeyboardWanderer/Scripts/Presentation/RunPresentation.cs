using System;
using System.Collections.Generic;
using KeyboardWanderer.Core;
using KeyboardWanderer.Gameplay;

namespace KeyboardWanderer.Presentation
{
    /// <summary>
    /// 로컬 저장 모델과 서버 DTO를 화면 계층에 노출하지 않기 위한 최소 런 상태다.
    /// 각 실행 경로의 어댑터가 이 값으로 변환한 뒤 Presenter에 전달한다.
    /// </summary>
    public readonly struct RunPresentationCore
    {
        public readonly long Version;
        public readonly int Turn;
        public readonly string LayoutHash;
        public readonly GridCoord PlayerPosition;

        public RunPresentationCore(long version, int turn, string layoutHash, GridCoord playerPosition)
        {
            Version = version;
            Turn = turn;
            LayoutHash = layoutHash ?? string.Empty;
            PlayerPosition = playerPosition;
        }
    }

    /// <summary>실행 출처와 관계없이 화면이 읽을 수 있는 공통 런 상태를 만든다.</summary>
    public interface IRunPresentationAdapter
    {
        RunPresentationModel Capture(RunView fallback);
    }

    /// <summary>로컬 규칙 엔진의 읽기 전용 RunView를 정규화한다.</summary>
    public sealed class LocalRunPresentationAdapter : IRunPresentationAdapter
    {
        private long _cachedVersion = long.MinValue;
        private RunPresentationModel _cached = RunPresentationModel.Empty;

        public RunPresentationModel Capture(RunView fallback)
        {
            if (fallback == null)
                return RunPresentationModel.Empty;
            if (_cachedVersion == fallback.Version)
                return _cached;

            var entities = new RunPresentationEntity[fallback.Entities.Count];
            for (int i = 0; i < fallback.Entities.Count; i++)
            {
                EntityView source = fallback.Entities[i];
                EntityCapabilities capabilities = EntityCapabilityCatalog.Resolve(source.Kind,
                    source.MaxHealth <= 0 || source.Health > 0, source.IsHostile, source.IsProtected,
                    source.IsCloneable, source.AssetId);
                entities[i] = new RunPresentationEntity
                {
                    Id = source.EntityId,
                    Kind = EntityKindFor(source.Kind),
                    AssetId = source.AssetId ?? string.Empty,
                    Name = source.DisplayName ?? string.Empty,
                    Position = source.Position,
                    Health = source.Health,
                    MaxHealth = source.MaxHealth,
                    IsPlayer = source.EntityId == fallback.PlayerEntityId,
                    IsHostile = source.IsHostile,
                    IsActive = source.MaxHealth <= 0 || source.Health > 0,
                    IsProtected = source.IsProtected,
                    IsCloneable = source.IsCloneable,
                    CanCopy = capabilities.CanCopy,
                    CanDelete = capabilities.CanDelete,
                    CanConnect = capabilities.CanConnect,
                    CanRestore = capabilities.CanRestore,
                    CanInteract = capabilities.CanInteract,
                    DeleteRequiredAdminAccess = capabilities.RequiredAdminAccess
                };
            }

            var beats = new RunPresentationBeat[fallback.RequiredBeats.Count];
            AbilityKind objectiveAbility = AbilityKind.Copy;
            bool foundObjective = false;
            for (int i = 0; i < fallback.RequiredBeats.Count; i++)
            {
                CampaignBeatState source = fallback.RequiredBeats[i];
                beats[i] = new RunPresentationBeat
                {
                    Title = source.Title ?? string.Empty,
                    Objective = source.Objective ?? string.Empty,
                    Ability = source.TriggerAbility,
                    IsCompleted = source.IsCompleted,
                    IsSkipped = source.IsSkipped
                };
                if (!foundObjective && !source.IsCompleted && !source.IsSkipped)
                {
                    objectiveAbility = source.TriggerAbility;
                    foundObjective = true;
                }
            }

            string premise = string.IsNullOrWhiteSpace(fallback.CampaignPremise)
                ? "넙죽이는 코드리아에서 관리자 키보드와 권한 3단계를 찾아 ROOT_SYSTEM으로 향합니다."
                : fallback.CampaignPremise;
            string beat = string.IsNullOrWhiteSpace(fallback.CurrentStoryBeat)
                ? "첫 장면을 확정하세요"
                : fallback.CurrentStoryBeat;
            string objective = string.IsNullOrWhiteSpace(fallback.CurrentStoryBeatObjective)
                ? "목적지 또는 관리자 키보드 스킬과 대상을 선택하세요."
                : fallback.CurrentStoryBeatObjective;

            IReadOnlyList<EndingConditionReport> endingReports = fallback.EndingConditionReports;
            var endingBoard = new RunPresentationEnding[endingReports.Count];
            for (int i = 0; i < endingReports.Count; i++)
            {
                EndingConditionReport report = endingReports[i];
                var missing = new System.Collections.Generic.List<string>();
                for (int j = 0; j < report.Conditions.Count; j++)
                    if (!report.Conditions[j].IsSatisfied) missing.Add(report.Conditions[j].Label);
                endingBoard[i] = new RunPresentationEnding
                {
                    Code = report.Code, Title = report.Title, IsEligible = report.IsEligible,
                    SatisfiedCount = report.SatisfiedCount, TotalCount = report.Conditions.Count,
                    MissingConditions = missing
                };
            }
            var inventory = new RunPresentationItem[fallback.Inventory.Count];
            for (int i = 0; i < fallback.Inventory.Count; i++)
            {
                string name = fallback.Inventory[i] ?? string.Empty;
                inventory[i] = new RunPresentationItem
                {
                    Id = name,
                    Kind = name.IndexOf("키보드", StringComparison.OrdinalIgnoreCase) >= 0 ? "key_item" : "salvage",
                    Name = name,
                    Description = "로컬 런에서 시스템이 관리하는 소지품입니다.",
                    Quantity = 1,
                    IsProtected = name.IndexOf("키보드", StringComparison.OrdinalIgnoreCase) >= 0
                };
            }
            var quests = new[]
            {
                new RunPresentationQuest
                {
                    Id = "local-story",
                    Title = fallback.QuestTitle,
                    Summary = fallback.QuestObjective,
                    CurrentStep = fallback.QuestProgress,
                    Kind = "main",
                    Status = fallback.Status == RunStatus.Playing ? "active" : "completed"
                }
            };
            _cached = new RunPresentationModel
            {
                Core = new RunPresentationCore(fallback.Version, fallback.CurrentTurn,
                    fallback.Region?.LayoutHash, fallback.PlayerPosition),
                IsServerAuthoritative = false,
                IsPlaying = fallback.Status == RunStatus.Playing,
                Status = fallback.Status,
                TurnLimit = fallback.TurnLimit,
                RemainingTurns = fallback.RemainingTurns,
                Focus = fallback.Focus,
                MaxFocus = fallback.MaxFocus,
                Experience = fallback.Experience,
                Gold = fallback.Gold,
                Pressure = 0,
                AdminAccess = fallback.AdminAccess,
                Health = fallback.Health,
                MaxHealth = fallback.MaxHealth,
                EndingCode = fallback.EndingCode ?? string.Empty,
                PlayerName = CampaignCatalog.ProtagonistName,
                CampaignTitle = CampaignCatalog.CampaignTitle,
                CampaignPremise = premise,
                StoryBeat = beat,
                StoryObjective = objective,
                CurrentAreaName = string.IsNullOrWhiteSpace(fallback.CurrentAreaName)
                    ? "알 수 없는 변경지대"
                    : fallback.CurrentAreaName,
                CurrentBiomeId = SectorBiomeId(fallback.PlayerPosition,
                    fallback.Region.Width, fallback.Region.Height),
                CurrentRegionAxis = fallback.CurrentRegionAxis ?? string.Empty,
                ObjectiveAbility = objectiveAbility,
                OpenLoops = new System.Collections.Generic.List<string>(fallback.OpenLoops),
                RequiredBeats = beats,
                Entities = entities,
                EndingBoard = endingBoard,
                Inventory = inventory,
                Quests = quests
            };
            _cachedVersion = fallback.Version;
            return _cached;
        }

        private static RunPresentationEntityKind EntityKindFor(EntityKind kind)
        {
            switch (kind)
            {
                case EntityKind.Player: return RunPresentationEntityKind.Player;
                case EntityKind.Npc: return RunPresentationEntityKind.Npc;
                case EntityKind.Enemy: return RunPresentationEntityKind.Enemy;
                case EntityKind.Prop: return RunPresentationEntityKind.Prop;
                default: return RunPresentationEntityKind.Unknown;
            }
        }

        private static string SectorBiomeId(GridCoord coord, int width, int height)
        {
            string[] order =
            {
                "temperate_forest_field", "river_wetland", "arid_desert",
                "frost_highland", "subterranean_cavern", "ancient_ruins"
            };
            double cx = (width - 1) / 2.0;
            double cy = (height - 1) / 2.0;
            double dx = coord.X - cx;
            double dy = coord.Y - cy;
            double centralRadius = Math.Min(width, height) * 0.12;
            if (dx * dx + dy * dy <= centralRadius * centralRadius)
                return "root_system";
            double angle = Math.Atan2(dy, dx) + Math.PI;
            int sector = (int)(angle / (2.0 * Math.PI) * order.Length);
            return order[Math.Max(0, Math.Min(order.Length - 1, sector))];
        }
    }

    [Flags]
    public enum PresentationChange
    {
        None = 0,
        Screen = 1 << 0,
        Hud = 1 << 1,
        Dialogue = 1 << 2,
        Minimap = 1 << 3,
        Selection = 1 << 4,
        All = Screen | Hud | Dialogue | Minimap | Selection
    }

    /// <summary>
    /// The normalized, read-only state consumed by authored scene presenters.
    /// Network DTOs and save models deliberately do not cross this boundary.
    /// </summary>
    public readonly struct RunPresentationState : IEquatable<RunPresentationState>
    {
        public readonly long Version;
        public readonly int Turn;
        public readonly string LayoutHash;
        public readonly GridCoord PlayerPosition;
        public readonly GridCoord? SelectedCoord;
        public readonly Guid? SelectedTarget;
        public readonly AbilityKind Ability;
        public readonly int Screen;
        public readonly int DialoguePage;
        public readonly string DialogueSignature;
        public readonly bool Paused;
        public readonly bool Pending;
        public readonly bool Walking;

        public RunPresentationState(long version, int turn, string layoutHash, GridCoord playerPosition,
            GridCoord? selectedCoord, Guid? selectedTarget, AbilityKind ability, int screen,
            int dialoguePage, string dialogueSignature, bool paused, bool pending, bool walking)
        {
            Version = version;
            Turn = turn;
            LayoutHash = layoutHash ?? string.Empty;
            PlayerPosition = playerPosition;
            SelectedCoord = selectedCoord;
            SelectedTarget = selectedTarget;
            Ability = ability;
            Screen = screen;
            DialoguePage = dialoguePage;
            DialogueSignature = dialogueSignature ?? string.Empty;
            Paused = paused;
            Pending = pending;
            Walking = walking;
        }

        public RunPresentationState(RunPresentationCore core,
            GridCoord? selectedCoord, Guid? selectedTarget, AbilityKind ability, int screen,
            int dialoguePage, string dialogueSignature, bool paused, bool pending, bool walking)
            : this(core.Version, core.Turn, core.LayoutHash, core.PlayerPosition,
                selectedCoord, selectedTarget, ability, screen, dialoguePage,
                dialogueSignature, paused, pending, walking)
        {
        }

        public bool Equals(RunPresentationState other)
        {
            return Version == other.Version && Turn == other.Turn && LayoutHash == other.LayoutHash &&
                   PlayerPosition.Equals(other.PlayerPosition) && Nullable.Equals(SelectedCoord, other.SelectedCoord) &&
                   Nullable.Equals(SelectedTarget, other.SelectedTarget) && Ability == other.Ability &&
                   Screen == other.Screen && DialoguePage == other.DialoguePage &&
                   DialogueSignature == other.DialogueSignature && Paused == other.Paused &&
                   Pending == other.Pending && Walking == other.Walking;
        }

        public override bool Equals(object obj) => obj is RunPresentationState other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(Version, Turn, LayoutHash, PlayerPosition,
            SelectedCoord, SelectedTarget, Ability, HashCode.Combine(Screen, DialoguePage, DialogueSignature,
                Paused, Pending, Walking));
    }

    /// <summary>Owns presentation invalidation independently from MonoBehaviour lifecycle.</summary>
    public sealed class RunCoordinator
    {
        private bool _hasState;
        private RunPresentationState _state;

        public event Action<RunPresentationState, PresentationChange> PresentationChanged;
        public RunPresentationState State => _state;

        public void Publish(RunPresentationState next, PresentationChange requested = PresentationChange.None)
        {
            PresentationChange changes = requested | Compare(_hasState ? _state : default, next, !_hasState);
            _state = next;
            _hasState = true;
            if (changes != PresentationChange.None)
                PresentationChanged?.Invoke(next, changes);
        }

        public void Invalidate(PresentationChange changes)
        {
            if (_hasState && changes != PresentationChange.None)
                PresentationChanged?.Invoke(_state, changes);
        }

        private static PresentationChange Compare(RunPresentationState previous, RunPresentationState next, bool first)
        {
            if (first) return PresentationChange.All;
            PresentationChange changes = PresentationChange.None;
            if (previous.Screen != next.Screen || previous.Paused != next.Paused)
                changes |= PresentationChange.Screen;
            if (previous.Version != next.Version || previous.Turn != next.Turn || previous.Ability != next.Ability ||
                previous.Pending != next.Pending || previous.Walking != next.Walking)
                changes |= PresentationChange.Hud;
            if (previous.DialoguePage != next.DialoguePage || previous.DialogueSignature != next.DialogueSignature)
                changes |= PresentationChange.Dialogue;
            if (previous.LayoutHash != next.LayoutHash || !previous.PlayerPosition.Equals(next.PlayerPosition) ||
                !Nullable.Equals(previous.SelectedCoord, next.SelectedCoord) ||
                !Nullable.Equals(previous.SelectedTarget, next.SelectedTarget))
                changes |= PresentationChange.Minimap;
            if (!Nullable.Equals(previous.SelectedCoord, next.SelectedCoord) ||
                !Nullable.Equals(previous.SelectedTarget, next.SelectedTarget) || previous.Ability != next.Ability)
                changes |= PresentationChange.Selection;
            return changes;
        }
    }
}
