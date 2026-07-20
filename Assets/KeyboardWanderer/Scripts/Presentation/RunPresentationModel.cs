using System;
using System.Collections.Generic;
using KeyboardWanderer.Core;
using KeyboardWanderer.Gameplay;

namespace KeyboardWanderer.Presentation
{
    /// <summary>로컬 EntityView와 서버 EntitySnapshot을 화면·선택 계층에서 함께 읽기 위한 종류다.</summary>
    public enum RunPresentationEntityKind
    {
        Unknown,
        Player,
        Npc,
        Enemy,
        Prop
    }

    /// <summary>서버 DTO와 로컬 저장 모델의 차이를 제거한 엔티티 표시 상태다.</summary>
    public sealed class RunPresentationEntity
    {
        public Guid Id { get; internal set; }
        public RunPresentationEntityKind Kind { get; internal set; }
        public string AssetId { get; internal set; } = string.Empty;
        public string Name { get; internal set; } = string.Empty;
        public GridCoord Position { get; internal set; }
        public int Health { get; internal set; }
        public int MaxHealth { get; internal set; }
        public bool IsPlayer { get; internal set; }
        public bool IsHostile { get; internal set; }
        public bool IsActive { get; internal set; } = true;
        public bool IsProtected { get; internal set; }
        public bool IsCloneable { get; internal set; }
        public bool CanCopy { get; internal set; }
        public bool CanDelete { get; internal set; }
        public bool CanConnect { get; internal set; }
        public bool CanRestore { get; internal set; }
        public bool CanInteract { get; internal set; }
        public int DeleteRequiredAdminAccess { get; internal set; }
        public string RequiredSkillId { get; internal set; } = string.Empty;

        public string KindLabel
        {
            get
            {
                switch (Kind)
                {
                    case RunPresentationEntityKind.Player: return "플레이어";
                    case RunPresentationEntityKind.Npc: return "NPC";
                    case RunPresentationEntityKind.Enemy: return "적";
                    case RunPresentationEntityKind.Prop: return "오브젝트";
                    default: return "대상";
                }
            }
        }
    }

    /// <summary>화면에 표시할 캠페인 비트 한 개의 공통 상태다.</summary>
    public sealed class RunPresentationBeat
    {
        public string Title { get; internal set; } = string.Empty;
        public string Objective { get; internal set; } = string.Empty;
        public AbilityKind Ability { get; internal set; }
        public bool IsCompleted { get; internal set; }
        public bool IsSkipped { get; internal set; }
    }

    public sealed class RunPresentationEnding
    {
        public string Code { get; internal set; } = string.Empty;
        public string Title { get; internal set; } = string.Empty;
        public bool IsEligible { get; internal set; }
        public int SatisfiedCount { get; internal set; }
        public int TotalCount { get; internal set; }
        public IReadOnlyList<string> MissingConditions { get; internal set; } = Array.Empty<string>();
    }

    /// <summary>
    /// HUD와 선택 규칙이 소비하는 로컬·서버 공통 읽기 모델이다.
    /// Networking DTO와 저장 가능한 RunState는 이 경계 밖으로 노출하지 않는다.
    /// </summary>
    public sealed class RunPresentationModel
    {
        public static readonly RunPresentationModel Empty = new RunPresentationModel();

        public RunPresentationCore Core { get; internal set; }
        public bool IsServerAuthoritative { get; internal set; }
        public bool IsPlaying { get; internal set; }
        public int TurnLimit { get; internal set; }
        public int RemainingTurns { get; internal set; }
        public int Focus { get; internal set; }
        public int MaxFocus { get; internal set; }
        public int Experience { get; internal set; }
        public int Gold { get; internal set; }
        public int Pressure { get; internal set; }
        public int AdminAccess { get; internal set; }
        public int Health { get; internal set; }
        public int MaxHealth { get; internal set; }
        public string EndingCode { get; internal set; } = string.Empty;
        public string PlayerName { get; internal set; } = string.Empty;
        public string CampaignTitle { get; internal set; } = string.Empty;
        public string CampaignPremise { get; internal set; } = string.Empty;
        public string StoryBeat { get; internal set; } = string.Empty;
        public string StoryObjective { get; internal set; } = string.Empty;
        public string CurrentAreaName { get; internal set; } = string.Empty;
        public string CurrentBiomeId { get; internal set; } = string.Empty;
        public AbilityKind ObjectiveAbility { get; internal set; } = AbilityKind.Copy;
        public Guid? ObjectiveTargetId { get; internal set; }
        public string ObjectiveTargetName { get; internal set; } = string.Empty;
        public GridCoord? ObjectiveTargetPosition { get; internal set; }
        public IReadOnlyList<string> OpenLoops { get; internal set; } = Array.Empty<string>();
        public IReadOnlyList<RunPresentationBeat> RequiredBeats { get; internal set; } =
            Array.Empty<RunPresentationBeat>();
        public IReadOnlyList<RunPresentationEntity> Entities { get; internal set; } =
            Array.Empty<RunPresentationEntity>();
        public IReadOnlyList<RunPresentationEnding> EndingBoard { get; internal set; } =
            Array.Empty<RunPresentationEnding>();

        public RunPresentationEntity FindEntity(Guid? id)
        {
            if (!id.HasValue)
                return null;
            for (int i = 0; i < Entities.Count; i++)
                if (Entities[i] != null && Entities[i].Id == id.Value)
                    return Entities[i];
            return null;
        }

        public int DistanceFromPlayer(RunPresentationEntity entity)
        {
            return entity == null ? -1 : Core.PlayerPosition.ManhattanDistance(entity.Position);
        }

        public int ActiveEnemiesWithin(int range)
        {
            int count = 0;
            for (int i = 0; i < Entities.Count; i++)
            {
                RunPresentationEntity entity = Entities[i];
                if (entity != null && entity.Kind == RunPresentationEntityKind.Enemy && entity.IsHostile &&
                    entity.IsActive && Core.PlayerPosition.ManhattanDistance(entity.Position) <= range)
                    count++;
            }
            return count;
        }
    }
}
