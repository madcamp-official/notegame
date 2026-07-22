using System;
using System.Collections.Generic;
using KeyboardWanderer.Core;

namespace KeyboardWanderer.Gameplay
{
    public enum AbilityKind
    {
        Move = 0,
        Copy = 1,
        Delete = 2,
        Connect = 3,
        // Numeric values 4..6 and 9 were legacy public actions. They intentionally remain
        // unassigned so old payloads deserialize to an unknown value and are rejected.
        Restore = 7,
        Undo = 8,
        Interact = 10,
        Search = 11,
        SelectAll = 12
    }

    public enum PlayerInputType
    {
        MOVE,
        USE_SKILL
    }

    public enum ActionContext
    {
        None,
        SafeTravel,
        Combat,
        Investigation,
        Negotiation,
        Deployment
    }

    public enum RuleOutcome
    {
        CriticalFailure,
        Failure,
        PartialSuccess,
        Success,
        CriticalSuccess
    }

    public enum RunStatus
    {
        Playing,
        Completed,
        Dead,
        Abandoned,
        RecoveryRequired
    }

    public enum CampaignAct
    {
        Introduction,
        Exploration,
        Pressure,
        Convergence,
        Ending
    }

    public enum CampaignPhase
    {
        ArrivalAndKeyboardAwakening,
        FirstRegionProblem,
        AdminAccessOne,
        AdminAccessTwo,
        InternalFailureTruth,
        TechnicalDebtBackflow,
        AdminAccessThree,
        RootSystemEntry,
        FinalDeployment
    }

    public enum TurnErrorCode
    {
        None,
        InvalidRequest,
        IdempotencyConflict,
        RunVersionConflict,
        RunNotPlaying,
        EntityNotFound,
        InvalidTarget,
        ProtectedEntity,
        NotCloneable,
        TargetTooHealthy,
        AlreadyOpened,
        DestinationInvalid,
        PathBlocked,
        OutOfRange,
        InsufficientResource,
        QuestConditionMissing,
        NoRestorableSnapshot,
        SnapshotExpired,
        UndoUnavailable
    }

    public sealed class TurnRequest
    {
        public string IdempotencyKey { get; }
        public long ExpectedRunVersion { get; }
        public AbilityKind Ability { get; }
        public Guid? TargetEntityId { get; }
        public Guid? SecondaryTargetEntityId { get; }
        public GridCoord? Destination { get; }
        public PlayerInputType InputType { get; }
        public string SkillId => Ability == AbilityKind.Move ? string.Empty : Ability.ToString().ToUpperInvariant();
        /// <summary>Optional flavour note. It is never required or used as rules authority.</summary>
        public string IntentText { get; }
        public int PreparedD20 { get; }

        public TurnRequest(string idempotencyKey, long expectedRunVersion, AbilityKind ability,
            Guid? targetEntityId, GridCoord? destination, string intentText)
            : this(idempotencyKey, expectedRunVersion, ability, targetEntityId, null, destination, intentText)
        {
        }

        public TurnRequest(string idempotencyKey, long expectedRunVersion, AbilityKind ability,
            Guid? targetEntityId, Guid? secondaryTargetEntityId, GridCoord? destination, string intentText)
            : this(idempotencyKey, expectedRunVersion, ability, targetEntityId, secondaryTargetEntityId,
                destination, intentText, 0)
        {
        }

        private TurnRequest(string idempotencyKey, long expectedRunVersion, AbilityKind ability,
            Guid? targetEntityId, Guid? secondaryTargetEntityId, GridCoord? destination, string intentText,
            int preparedD20)
        {
            IdempotencyKey = idempotencyKey;
            ExpectedRunVersion = expectedRunVersion;
            Ability = ability;
            TargetEntityId = targetEntityId;
            SecondaryTargetEntityId = secondaryTargetEntityId;
            Destination = destination;
            InputType = ability == AbilityKind.Move ? PlayerInputType.MOVE : PlayerInputType.USE_SKILL;
            IntentText = intentText ?? string.Empty;
            PreparedD20 = preparedD20;
        }

        public TurnRequest WithPreparedD20(int value)
        {
            if (value < 1 || value > 20) throw new ArgumentOutOfRangeException(nameof(value));
            return new TurnRequest(IdempotencyKey, ExpectedRunVersion, Ability, TargetEntityId,
                SecondaryTargetEntityId, Destination, IntentText, value);
        }

        public static TurnRequest Move(string idempotencyKey, long expectedRunVersion, GridCoord destination)
        {
            return new TurnRequest(idempotencyKey, expectedRunVersion, AbilityKind.Move, null, null,
                destination, string.Empty);
        }

        public static TurnRequest UseSkill(string idempotencyKey, long expectedRunVersion, AbilityKind skill,
            Guid? targetEntityId = null, Guid? secondaryTargetEntityId = null, GridCoord? destination = null)
        {
            if (!IsPublicKeyboardSkill(skill) && skill != AbilityKind.Interact)
                throw new ArgumentException(
                    "USE_SKILL accepts a keyboard skill or INTERACT.", nameof(skill));
            return new TurnRequest(idempotencyKey, expectedRunVersion, skill, targetEntityId,
                secondaryTargetEntityId, destination, string.Empty);
        }

        public static bool IsPublicKeyboardSkill(AbilityKind skill)
        {
            return skill == AbilityKind.Copy || skill == AbilityKind.Delete ||
                   skill == AbilityKind.Connect || skill == AbilityKind.Restore ||
                   skill == AbilityKind.Undo || skill == AbilityKind.Search ||
                   skill == AbilityKind.SelectAll;
        }

        public string Fingerprint()
        {
            return ExpectedRunVersion + "|" + InputType + "|" + Ability + "|" + TargetEntityId + "|" + SecondaryTargetEntityId +
                   "|" + Destination + "|" + IntentText;
        }
    }

    public sealed class TechnicalDebtEntry
    {
        public string Id { get; }
        public int TurnNo { get; }
        public AbilityKind Skill { get; }
        public string OperationType { get; }
        public string TargetId { get; }
        public bool ForcedOverride { get; }
        public int DebtDelta { get; }
        public string DeferredConsequenceType { get; }
        public int ResolvedTurn { get; set; }
        public bool IsResolved => ResolvedTurn > 0;

        public TechnicalDebtEntry(string id, int turnNo, AbilityKind skill, string operationType,
            string targetId, bool forcedOverride, int debtDelta, string deferredConsequenceType)
        {
            Id = id ?? string.Empty;
            TurnNo = turnNo;
            Skill = skill;
            OperationType = operationType ?? string.Empty;
            TargetId = targetId ?? string.Empty;
            ForcedOverride = forcedOverride;
            DebtDelta = debtDelta;
            DeferredConsequenceType = deferredConsequenceType ?? string.Empty;
        }

        public TechnicalDebtEntry Clone()
        {
            return new TechnicalDebtEntry(Id, TurnNo, Skill, OperationType, TargetId, ForcedOverride,
                DebtDelta, DeferredConsequenceType) { ResolvedTurn = ResolvedTurn };
        }
    }

    public sealed class AdminAccessAcquisitionRecord
    {
        public int Level { get; }
        public string AccessId { get; }
        public string RegionAxis { get; }
        public ActionContext Context { get; }
        public AbilityKind Skill { get; }
        public int TurnNo { get; }

        public AdminAccessAcquisitionRecord(int level, string accessId, string regionAxis,
            ActionContext context, AbilityKind skill, int turnNo)
        {
            Level = Math.Max(1, Math.Min(3, level));
            AccessId = accessId ?? string.Empty;
            RegionAxis = regionAxis ?? string.Empty;
            Context = context;
            Skill = skill;
            TurnNo = turnNo;
        }

        public AdminAccessAcquisitionRecord Clone()
        {
            return new AdminAccessAcquisitionRecord(Level, AccessId, RegionAxis, Context, Skill, TurnNo);
        }
    }

    public sealed class CampaignBeatState
    {
        public string Id { get; }
        public string Title { get; }
        public string Objective { get; }
        public AbilityKind TriggerAbility { get; }
        public ActionContext RequiredContext { get; }
        public string RoleId { get; }
        public string AdminAccessRewardId { get; }
        public bool IsRequired { get; }
        public bool IsCompleted { get; set; }
        public bool IsSkipped { get; set; }
        public int ResolvedTurn { get; set; }
        public string Resolution { get; set; }

        public CampaignBeatState(string id, string title, string objective, AbilityKind triggerAbility,
            bool isRequired = true, string roleId = "", string adminAccessRewardId = "",
            ActionContext requiredContext = ActionContext.None)
        {
            Id = id ?? string.Empty;
            Title = title ?? string.Empty;
            Objective = objective ?? string.Empty;
            TriggerAbility = triggerAbility;
            RequiredContext = requiredContext;
            RoleId = roleId ?? string.Empty;
            AdminAccessRewardId = adminAccessRewardId ?? string.Empty;
            IsRequired = isRequired;
            Resolution = string.Empty;
        }

        public CampaignBeatState Clone()
        {
            return new CampaignBeatState(Id, Title, Objective, TriggerAbility, IsRequired, RoleId,
                AdminAccessRewardId, RequiredContext)
            {
                IsCompleted = IsCompleted,
                IsSkipped = IsSkipped,
                ResolvedTurn = ResolvedTurn,
                Resolution = Resolution
            };
        }
    }

    public sealed class EndingCandidateState
    {
        public string Code { get; }
        public string Title { get; }
        public string Description { get; }
        public int MinimumCompletedBeats { get; }
        public bool IsEligible { get; set; }

        public EndingCandidateState(string code, string title, string description, int minimumCompletedBeats)
        {
            Code = code ?? string.Empty;
            Title = title ?? string.Empty;
            Description = description ?? string.Empty;
            MinimumCompletedBeats = Math.Max(0, minimumCompletedBeats);
        }

        public EndingCandidateState Clone()
        {
            return new EndingCandidateState(Code, Title, Description, MinimumCompletedBeats)
            {
                IsEligible = IsEligible
            };
        }
    }

    public sealed class EndingCondition
    {
        public string Label { get; }
        public bool IsSatisfied { get; }
        public EndingCondition(string label, bool isSatisfied) { Label = label ?? string.Empty; IsSatisfied = isSatisfied; }
    }

    public sealed class EndingConditionReport
    {
        public string Code { get; }
        public string Title { get; }
        public IReadOnlyList<EndingCondition> Conditions { get; }
        public int SatisfiedCount { get; }
        public bool IsEligible => SatisfiedCount == Conditions.Count;
        public EndingConditionReport(string code, string title, IReadOnlyList<EndingCondition> conditions)
        {
            Code = code ?? string.Empty; Title = title ?? string.Empty;
            Conditions = conditions ?? Array.Empty<EndingCondition>();
            for (int i = 0; i < Conditions.Count; i++) if (Conditions[i].IsSatisfied) SatisfiedCount++;
        }
    }

    public sealed class NpcStoryState
    {
        public Guid EntityId { get; }
        public string NpcName { get; }
        public string Role { get; }
        public int Affinity { get; set; }
        public int Trust { get; set; }
        public int Fear { get; set; }
        public int Obligation { get; set; }
        public string Motivation { get; set; }
        public string Secret { get; set; }
        public string CurrentConcern { get; set; }
        public int LastConversationTurn { get; set; }
        public List<string> RevealedClues { get; }
        public List<string> Memories { get; }

        public NpcStoryState(Guid entityId, string npcName, string role, int affinity = 0)
        {
            EntityId = entityId;
            NpcName = npcName ?? string.Empty;
            Role = role ?? string.Empty;
            Affinity = affinity;
            Motivation = string.Empty;
            Secret = string.Empty;
            CurrentConcern = string.Empty;
            RevealedClues = new List<string>();
            Memories = new List<string>();
        }

        public void Remember(string memory)
        {
            if (string.IsNullOrWhiteSpace(memory))
                return;
            Memories.Add(memory.Trim());
            while (Memories.Count > 6)
                Memories.RemoveAt(0);
        }

        public NpcStoryState Clone()
        {
            var clone = new NpcStoryState(EntityId, NpcName, Role, Affinity);
            clone.Trust = Trust;
            clone.Fear = Fear;
            clone.Obligation = Obligation;
            clone.Motivation = Motivation;
            clone.Secret = Secret;
            clone.CurrentConcern = CurrentConcern;
            clone.LastConversationTurn = LastConversationTurn;
            clone.RevealedClues.AddRange(RevealedClues);
            clone.Memories.AddRange(Memories);
            return clone;
        }
    }

    public sealed class RestorationRecord
    {
        public EntityRuntimeSnapshot Snapshot { get; }
        public int CapturedTurn { get; }
        public string Reason { get; }
        public bool IsConsumed { get; set; }

        public RestorationRecord(EntityRuntimeSnapshot snapshot, int capturedTurn, string reason)
        {
            Snapshot = snapshot == null ? throw new ArgumentNullException(nameof(snapshot)) : snapshot;
            CapturedTurn = capturedTurn;
            Reason = reason ?? string.Empty;
        }

        public RestorationRecord Clone()
        {
            return new RestorationRecord(Snapshot.Clone(), CapturedTurn, Reason) { IsConsumed = IsConsumed };
        }
    }

    /// <summary>
    /// State eligible for a compensating Undo event. It deliberately excludes turn/version/RNG,
    /// map data, campaign facts, beat progression, NPC memories, and run endings.
    /// </summary>
    public sealed class ReversibleTurnRecord
    {
        public int SourceTurn { get; }
        public AbilityKind SourceAbility { get; }
        public int FocusBefore { get; }
        public int GoldBefore { get; }
        public int ExperienceBefore { get; }
        public bool WasExposedBefore { get; }
        public List<string> InventoryBefore { get; }
        public List<string> ConnectionsBefore { get; }
        public List<EntityRuntimeSnapshot> EntitySnapshots { get; }
        public List<Guid> IrreversibleEntityIds { get; }
        public bool RestoreInventory { get; set; }
        public bool RestoreEconomy { get; set; }
        public bool RestoreConnections { get; set; }
        public bool IsConsumed { get; set; }

        public ReversibleTurnRecord(int sourceTurn, AbilityKind sourceAbility, int focusBefore, int goldBefore,
            int experienceBefore, bool wasExposedBefore, IEnumerable<string> inventoryBefore,
            IEnumerable<string> connectionsBefore, IEnumerable<EntityRuntimeSnapshot> entitySnapshots)
        {
            SourceTurn = sourceTurn;
            SourceAbility = sourceAbility;
            FocusBefore = focusBefore;
            GoldBefore = goldBefore;
            ExperienceBefore = experienceBefore;
            WasExposedBefore = wasExposedBefore;
            InventoryBefore = new List<string>(inventoryBefore ?? Array.Empty<string>());
            ConnectionsBefore = new List<string>(connectionsBefore ?? Array.Empty<string>());
            EntitySnapshots = new List<EntityRuntimeSnapshot>();
            if (entitySnapshots != null)
            {
                foreach (EntityRuntimeSnapshot snapshot in entitySnapshots)
                    EntitySnapshots.Add(snapshot.Clone());
            }
            IrreversibleEntityIds = new List<Guid>();
            RestoreInventory = true;
            RestoreEconomy = true;
            RestoreConnections = true;
        }

        public ReversibleTurnRecord Clone()
        {
            var clone = new ReversibleTurnRecord(SourceTurn, SourceAbility, FocusBefore, GoldBefore,
                ExperienceBefore, WasExposedBefore, InventoryBefore, ConnectionsBefore, EntitySnapshots)
            {
                RestoreInventory = RestoreInventory,
                RestoreEconomy = RestoreEconomy,
                RestoreConnections = RestoreConnections,
                IsConsumed = IsConsumed
            };
            clone.IrreversibleEntityIds.AddRange(IrreversibleEntityIds);
            return clone;
        }
    }

    public sealed class EntityView
    {
        public Guid EntityId { get; }
        public EntityKind Kind { get; }
        public string AssetId { get; }
        public string DisplayName { get; }
        public GridCoord Position { get; }
        public int Health { get; }
        public int MaxHealth { get; }
        public bool IsHostile { get; }
        public bool IsOpened { get; }
        public bool IsBlocking { get; }
        public bool IsProtected { get; }
        public bool IsCloneable { get; }

        public EntityView(EntityState state)
        {
            EntityId = state.EntityId;
            Kind = state.Kind;
            AssetId = state.AssetId;
            DisplayName = state.DisplayName;
            Position = state.Position;
            Health = state.Health;
            MaxHealth = state.MaxHealth;
            IsHostile = state.IsHostile;
            IsOpened = state.IsOpened;
            IsBlocking = state.IsBlocking;
            IsProtected = state.IsProtected;
            IsCloneable = state.IsCloneable;
        }
    }

    public sealed class NpcMemoryView
    {
        public Guid EntityId { get; }
        public string NpcName { get; }
        public string Role { get; }
        public int Affinity { get; }
        public int Trust { get; }
        public int Fear { get; }
        public int Obligation { get; }
        public string Motivation { get; }
        public string CurrentConcern { get; }
        public IReadOnlyList<string> RevealedClues { get; }
        public string LatestMemory { get; }
        public IReadOnlyList<string> Memories { get; }

        public NpcMemoryView(NpcStoryState state)
        {
            EntityId = state.EntityId;
            NpcName = state.NpcName;
            Role = state.Role;
            Affinity = state.Affinity;
            Trust = state.Trust;
            Fear = state.Fear;
            Obligation = state.Obligation;
            Motivation = state.Motivation;
            CurrentConcern = state.CurrentConcern;
            RevealedClues = new List<string>(state.RevealedClues);
            Memories = new List<string>(state.Memories);
            LatestMemory = state.Memories.Count == 0 ? string.Empty : state.Memories[state.Memories.Count - 1];
        }
    }

    public sealed class EndingCandidateView
    {
        public string Code { get; }
        public string Title { get; }
        public string Description { get; }
        public bool IsEligible { get; }

        public EndingCandidateView(EndingCandidateState state)
        {
            Code = state.Code;
            Title = state.Title;
            Description = state.Description;
            IsEligible = state.IsEligible;
        }
    }

    public sealed class RunView
    {
        public Guid RunId { get; }
        public long WorldSeed { get; }
        public long Version { get; }
        public int CurrentTurn { get; }
        public int TurnLimit { get; }
        public int RemainingTurns => Math.Max(0, TurnLimit - CurrentTurn);
        public int Health { get; }
        public int MaxHealth { get; }
        public int Focus { get; }
        public int MaxFocus { get; }
        public int Experience { get; }
        public int Gold { get; }
        public RunStatus Status { get; }
        public CampaignAct Act { get; }
        public CampaignPhase Phase { get; }
        public string EndingCode { get; }
        public RegionMap Region { get; }
        public Guid PlayerEntityId { get; }
        public GridCoord PlayerPosition { get; }
        public string CurrentAreaId { get; }
        public string CurrentAreaName { get; }
        public string CurrentAreaDescription { get; }
        public string CurrentRegionAxis { get; }
        public int AdminAccess { get; }
        public int WorldStability { get; }
        public int WorldAutonomy { get; }
        public int PublicTrust { get; }
        public int TechnicalDebt { get; }
        public IReadOnlyList<TechnicalDebtEntry> TechnicalDebtEntries { get; }
        public IReadOnlyList<AdminAccessAcquisitionRecord> AdminAccessAcquisitionHistory { get; }
        public IReadOnlyList<string> MajorChoices { get; }
        public IReadOnlyList<string> RegionOutcomes { get; }
        public int CompanionBond { get; }
        public int TurnPressure { get; }
        public int TravelTime { get; }
        public IReadOnlyList<string> VisitedAreaIds { get; }
        public bool HasActiveEncounter { get; }
        public string ActiveEncounterReason { get; }
        public GridCoord EncounterStagingPosition { get; }

        public string CampaignId { get; }
        public string CampaignTitle { get; }
        public string CampaignPremise { get; }
        public string CurrentStoryBeat { get; }
        public string CurrentStoryBeatObjective { get; }
        public string QuestTitle { get; }
        public string QuestObjective { get; }
        public string QuestProgress { get; }
        public int QuestStage { get; }
        public IReadOnlyList<CampaignBeatState> RequiredBeats { get; }
        public IReadOnlyList<string> CanonicalFacts { get; }
        public IReadOnlyList<string> OpenLoops { get; }
        public IReadOnlyList<string> Rumors { get; }
        public IReadOnlyList<string> ForbiddenEvents { get; }
        public IReadOnlyList<NpcMemoryView> NpcMemories { get; }
        public IReadOnlyList<EndingCandidateView> EndingCandidates { get; }
        public IReadOnlyList<EndingConditionReport> EndingConditionReports { get; }

        public string LastIntentText { get; }
        public string LastNormalizedAttempt { get; }
        public int LastRollRaw { get; }
        public int LastRollModifier { get; }
        public int LastRollDifficulty { get; }
        public int LastMechanicalScore { get; }
        public int LastIntentAlignment { get; }
        public RuleOutcome LastOutcome { get; }
        public string LastOutcomeExplanation { get; }
        public IReadOnlyList<string> IntentHistory { get; }

        public int EnemiesDefeated { get; }
        public int RollCount { get; }
        public IReadOnlyList<string> Inventory { get; }
        public IReadOnlyList<string> Connections { get; }
        public IReadOnlyList<string> GmLog { get; }
        public IReadOnlyList<EntityView> Entities { get; }

        public RunView(RunState state)
        {
            RunId = state.RunId;
            WorldSeed = state.WorldSeed;
            Version = state.Version;
            CurrentTurn = state.CurrentTurn;
            TurnLimit = state.TurnLimit;
            Focus = state.Focus;
            MaxFocus = state.MaxFocus;
            Experience = state.Experience;
            Gold = state.Gold;
            Status = state.Status;
            CampaignConstraints constraints = CampaignDirector.Evaluate(state.CurrentTurn, state.TurnLimit);
            Act = constraints.Act;
            Phase = constraints.Phase;
            EndingCode = state.EndingCode;
            Region = state.Region;
            PlayerEntityId = state.PlayerEntityId;

            if (state.Spatial.TryGetEntity(state.PlayerEntityId, out EntityState player))
            {
                Health = player.Health;
                MaxHealth = player.MaxHealth;
                PlayerPosition = player.Position;
            }
            else
            {
                Health = 0;
                MaxHealth = 10;
                PlayerPosition = state.Region.Start;
            }

            WorldArea area = state.Region.AreaAt(PlayerPosition);
            CurrentAreaId = area == null ? "unknown" : area.Id;
            CurrentAreaName = area == null ? "미지의 지역" : area.DisplayName;
            CurrentAreaDescription = area == null ? string.Empty : area.Description;
            CurrentRegionAxis = area == null ? string.Empty : area.CampaignRole;
            AdminAccess = state.AdminAccess;
            WorldStability = state.WorldStability;
            WorldAutonomy = state.WorldAutonomy;
            PublicTrust = state.PublicTrust;
            TechnicalDebt = state.TechnicalDebt;
            var debtEntries = new List<TechnicalDebtEntry>();
            for (int i = 0; i < state.TechnicalDebtEntries.Count; i++)
                debtEntries.Add(state.TechnicalDebtEntries[i].Clone());
            TechnicalDebtEntries = debtEntries;
            var accessHistory = new List<AdminAccessAcquisitionRecord>();
            for (int i = 0; i < state.AdminAccessAcquisitionHistory.Count; i++)
                accessHistory.Add(state.AdminAccessAcquisitionHistory[i].Clone());
            AdminAccessAcquisitionHistory = accessHistory;
            MajorChoices = new List<string>(state.MajorChoices);
            RegionOutcomes = new List<string>(state.RegionOutcomes);
            CompanionBond = state.CompanionBond;
            TurnPressure = state.TurnPressure;
            TravelTime = state.TravelTime;
            VisitedAreaIds = new List<string>(state.VisitedAreaIds);
            HasActiveEncounter = state.HasActiveEncounter;
            ActiveEncounterReason = state.ActiveEncounterReason;
            EncounterStagingPosition = state.EncounterStagingPosition;

            CampaignId = state.CampaignId;
            CampaignTitle = state.CampaignTitle;
            CampaignPremise = state.CampaignPremise;
            CampaignBeatState beat = state.CurrentBeat;
            CurrentStoryBeat = beat == null ? "에필로그" : beat.Title;
            CurrentStoryBeatObjective = beat == null ? "확정된 결말을 확인하세요." : beat.Objective;
            QuestTitle = CampaignTitle;
            QuestObjective = CurrentStoryBeatObjective;
            QuestProgress = state.CompletedBeatCount + "/" + state.RequiredBeats.Count + " 핵심 비트 해결";
            QuestStage = state.CurrentBeatIndex;
            var requiredBeats = new List<CampaignBeatState>();
            for (int i = 0; i < state.RequiredBeats.Count; i++)
                requiredBeats.Add(state.RequiredBeats[i].Clone());
            RequiredBeats = requiredBeats;
            CanonicalFacts = new List<string>(state.CanonicalFacts);
            OpenLoops = new List<string>(state.OpenLoops);
            Rumors = new List<string>(state.Rumors);
            ForbiddenEvents = new List<string>(state.ForbiddenEvents);

            var memories = new List<NpcMemoryView>();
            for (int i = 0; i < state.NpcStories.Count; i++)
                memories.Add(new NpcMemoryView(state.NpcStories[i]));
            NpcMemories = memories;
            var endingCandidates = new List<EndingCandidateView>();
            for (int i = 0; i < state.EndingCandidates.Count; i++)
                endingCandidates.Add(new EndingCandidateView(state.EndingCandidates[i]));
            EndingCandidates = endingCandidates;
            EndingConditionReports = CampaignDirector.EvaluateEndingBoard(state);

            LastIntentText = state.LastIntentText;
            LastNormalizedAttempt = state.LastNormalizedAttempt;
            LastRollRaw = state.LastRollRaw;
            LastRollModifier = state.LastRollModifier;
            LastRollDifficulty = state.LastRollDifficulty;
            LastMechanicalScore = state.LastMechanicalScore;
            LastIntentAlignment = state.LastIntentAlignment;
            LastOutcome = state.LastOutcome;
            LastOutcomeExplanation = state.LastOutcomeExplanation;
            IntentHistory = new List<string>(state.IntentHistory);

            EnemiesDefeated = state.EnemiesDefeated;
            RollCount = state.RollCount;
            Inventory = new List<string>(state.Inventory);
            Connections = new List<string>(state.Connections);
            GmLog = new List<string>(state.GmLog);

            var entities = new List<EntityView>();
            foreach (EntityState entity in state.Spatial.Entities)
            {
                if (entity.IsActive)
                    entities.Add(new EntityView(entity));
            }
            Entities = entities;
        }
    }

    public sealed class TurnResponse
    {
        public bool IsSuccess { get; }
        public bool FromIdempotencyCache { get; }
        public TurnErrorCode ErrorCode { get; }
        public string ErrorMessage { get; }
        public int TurnNo { get; }
        public int D20 { get; }
        public int Modifier { get; }
        public int Difficulty { get; }
        public int MechanicalScore { get; }
        public int IntentAlignment { get; }
        public RuleOutcome Outcome { get; }
        public string OutcomeExplanation { get; }
        public string NormalizedAttempt { get; }
        public string Narrative { get; }
        public int ConsequenceBudget { get; }
        public IReadOnlyList<string> Events { get; }
        public RunView Run { get; }
        public bool ConsumesCampaignTurn { get; }
        public ActionContext ActionContext { get; }

        private TurnResponse(bool isSuccess, bool fromCache, TurnErrorCode errorCode, string errorMessage,
            int turnNo, int d20, int modifier, int difficulty, int mechanicalScore, int intentAlignment,
            RuleOutcome outcome, string outcomeExplanation, string normalizedAttempt, string narrative,
            int consequenceBudget, IReadOnlyList<string> events, RunView run, bool consumesCampaignTurn,
            ActionContext actionContext)
        {
            IsSuccess = isSuccess;
            FromIdempotencyCache = fromCache;
            ErrorCode = errorCode;
            ErrorMessage = errorMessage;
            TurnNo = turnNo;
            D20 = d20;
            Modifier = modifier;
            Difficulty = difficulty;
            MechanicalScore = mechanicalScore;
            IntentAlignment = intentAlignment;
            Outcome = outcome;
            OutcomeExplanation = outcomeExplanation ?? string.Empty;
            NormalizedAttempt = normalizedAttempt ?? string.Empty;
            Narrative = narrative ?? string.Empty;
            ConsequenceBudget = consequenceBudget;
            Events = events ?? Array.Empty<string>();
            Run = run;
            ConsumesCampaignTurn = consumesCampaignTurn;
            ActionContext = actionContext;
        }

        public static TurnResponse Failure(TurnErrorCode code, string message, RunView run)
        {
            return new TurnResponse(false, false, code, message, 0, 0, 0, 0, 0, 0,
                RuleOutcome.Failure, string.Empty, string.Empty, string.Empty, 0, Array.Empty<string>(), run, false,
                ActionContext.None);
        }

        public static TurnResponse Success(int turnNo, int d20, int modifier, int difficulty, int mechanicalScore,
            int intentAlignment, RuleOutcome outcome, string outcomeExplanation, string normalizedAttempt,
            string narrative, int consequenceBudget, IReadOnlyList<string> events, RunView run,
            bool consumesCampaignTurn = true, ActionContext actionContext = ActionContext.None)
        {
            return new TurnResponse(true, false, TurnErrorCode.None, null, turnNo, d20, modifier, difficulty,
                mechanicalScore, intentAlignment, outcome, outcomeExplanation, normalizedAttempt, narrative,
                consequenceBudget, events, run, consumesCampaignTurn, actionContext);
        }

        public TurnResponse AsCached()
        {
            return new TurnResponse(IsSuccess, true, ErrorCode, ErrorMessage, TurnNo, D20, Modifier, Difficulty,
                MechanicalScore, IntentAlignment, Outcome, OutcomeExplanation, NormalizedAttempt, Narrative,
                ConsequenceBudget, Events, Run, ConsumesCampaignTurn, ActionContext);
        }
    }

    public sealed class RunState
    {
        public Guid RunId { get; }
        public long WorldSeed { get; }
        public long Version { get; set; }
        public int CurrentTurn { get; set; }
        public int TurnLimit { get; }
        public int Focus { get; set; }
        public int MaxFocus { get; set; }
        public int Experience { get; set; }
        public int Gold { get; set; }
        public bool IsExposed { get; set; }
        public RunStatus Status { get; set; }
        public string EndingCode { get; set; }
        public RegionMap Region { get; }
        public SpatialIndex Spatial { get; }
        public Guid PlayerEntityId { get; }

        public int AdminAccess { get; set; }
        public int WorldStability { get; set; }
        public int WorldAutonomy { get; set; }
        public int PublicTrust { get; set; }
        public int TechnicalDebt { get; set; }
        public List<TechnicalDebtEntry> TechnicalDebtEntries { get; }
        public List<AdminAccessAcquisitionRecord> AdminAccessAcquisitionHistory { get; }
        public List<string> MajorChoices { get; }
        public List<string> RegionOutcomes { get; }
        public int CompanionBond { get; set; }
        public int TurnPressure { get; set; }
        public int TravelTime { get; set; }
        public List<string> VisitedAreaIds { get; }
        public bool HasActiveEncounter { get; set; }
        public string ActiveEncounterReason { get; set; }
        public GridCoord EncounterStagingPosition { get; set; }

        public string CampaignId { get; set; }
        public string CampaignTitle { get; set; }
        public string CampaignPremise { get; set; }
        public int CurrentBeatIndex { get; set; }
        public List<CampaignBeatState> RequiredBeats { get; }
        public List<EndingCandidateState> EndingCandidates { get; }
        public List<string> CanonicalFacts { get; }
        public List<string> OpenLoops { get; }
        public List<string> Rumors { get; }
        public List<string> ForbiddenEvents { get; }
        public List<NpcStoryState> NpcStories { get; }

        public string LastIntentText { get; set; }
        public string LastNormalizedAttempt { get; set; }
        public int LastRollRaw { get; set; }
        public int LastRollModifier { get; set; }
        public int LastRollDifficulty { get; set; }
        public int LastMechanicalScore { get; set; }
        public int LastIntentAlignment { get; set; }
        public RuleOutcome LastOutcome { get; set; }
        public string LastOutcomeExplanation { get; set; }
        public List<string> IntentHistory { get; }
        public List<RestorationRecord> RestorationLedger { get; }
        public ReversibleTurnRecord LastReversibleTurn { get; set; }
        public List<ReversibleTurnRecord> ReversibleHistory { get; }

        public int EnemiesDefeated { get; set; }
        public int RollCount { get; set; }
        public HashSet<Guid> RewardedEnemyIds { get; }
        public int AmbientWanderTick { get; set; }
        public Dictionary<Guid, GridCoord> AmbientWanderOrigins { get; }
        public Dictionary<Guid, int> AmbientWanderNextTicks { get; }
        public int QuestStage { get; set; }
        public bool QuestAccepted { get; set; }
        public List<string> Inventory { get; }
        public List<string> Connections { get; }
        public List<string> GmLog { get; }

        public CampaignBeatState CurrentBeat
        {
            get
            {
                for (int i = Math.Max(0, CurrentBeatIndex); i < RequiredBeats.Count; i++)
                {
                    if (!RequiredBeats[i].IsCompleted && !RequiredBeats[i].IsSkipped)
                        return RequiredBeats[i];
                }
                return null;
            }
        }

        public int CompletedBeatCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < RequiredBeats.Count; i++)
                    if (RequiredBeats[i].IsCompleted) count++;
                return count;
            }
        }

        public RunState(Guid runId, long worldSeed, long version, int currentTurn, int turnLimit, int focus,
            RunStatus status, string endingCode, RegionMap region, SpatialIndex spatial, Guid playerEntityId)
        {
            RunId = runId;
            WorldSeed = worldSeed;
            Version = version;
            CurrentTurn = currentTurn;
            TurnLimit = turnLimit;
            Focus = focus;
            MaxFocus = Math.Max(8, focus);
            Status = status;
            EndingCode = endingCode;
            Region = region ?? throw new ArgumentNullException(nameof(region));
            Spatial = spatial ?? throw new ArgumentNullException(nameof(spatial));
            PlayerEntityId = playerEntityId;
            CampaignId = "uninitialized";
            CampaignTitle = "생성 중인 캠페인";
            CampaignPremise = string.Empty;
            RequiredBeats = new List<CampaignBeatState>();
            EndingCandidates = new List<EndingCandidateState>();
            CanonicalFacts = new List<string>();
            OpenLoops = new List<string>();
            Rumors = new List<string>();
            ForbiddenEvents = new List<string>();
            NpcStories = new List<NpcStoryState>();
            LastIntentText = string.Empty;
            LastNormalizedAttempt = string.Empty;
            LastOutcomeExplanation = string.Empty;
            IntentHistory = new List<string>();
            RestorationLedger = new List<RestorationRecord>();
            RewardedEnemyIds = new HashSet<Guid>();
            AmbientWanderOrigins = new Dictionary<Guid, GridCoord>();
            AmbientWanderNextTicks = new Dictionary<Guid, int>();
            ReversibleHistory = new List<ReversibleTurnRecord>();
            TechnicalDebtEntries = new List<TechnicalDebtEntry>();
            AdminAccessAcquisitionHistory = new List<AdminAccessAcquisitionRecord>();
            MajorChoices = new List<string>();
            RegionOutcomes = new List<string>();
            Inventory = new List<string>();
            Connections = new List<string>();
            GmLog = new List<string>();
            WorldStability = 55;
            WorldAutonomy = 45;
            PublicTrust = 40;
            TechnicalDebt = 10;
            CompanionBond = 20;
            TurnPressure = 0;
            TravelTime = 0;
            VisitedAreaIds = new List<string>();
            ActiveEncounterReason = string.Empty;
            EncounterStagingPosition = Region.Start;
            WorldArea startingArea = Region.AreaAt(Region.Start);
            if (startingArea != null) VisitedAreaIds.Add(startingArea.Id);
        }

        public bool HasItem(string item)
        {
            return Inventory.Exists(value => string.Equals(value, item, StringComparison.Ordinal));
        }

        public void AddLog(string message)
        {
            AddBoundedUniqueOrRepeated(GmLog, message, 18, false);
        }

        public void AddCanonicalFact(string fact)
        {
            AddBoundedUniqueOrRepeated(CanonicalFacts, fact, 24, true);
        }

        public void AddOpenLoop(string loop)
        {
            AddBoundedUniqueOrRepeated(OpenLoops, loop, 12, true);
        }

        public void ResolveOpenLoop(string loop)
        {
            if (string.IsNullOrWhiteSpace(loop))
                return;
            OpenLoops.RemoveAll(value => string.Equals(value, loop, StringComparison.Ordinal));
        }

        public void AddRumor(string rumor)
        {
            AddBoundedUniqueOrRepeated(Rumors, rumor, 10, true);
        }

        public void RecordIntent(int turnNo, AbilityKind ability, string intent)
        {
            LastIntentText = intent ?? string.Empty;
            IntentHistory.Add(turnNo + "|" + ability + "|" + LastIntentText);
            while (IntentHistory.Count > 40)
                IntentHistory.RemoveAt(0);
        }

        public NpcStoryState FindNpcStory(Guid entityId)
        {
            return NpcStories.Find(value => value.EntityId == entityId);
        }

        public void RecordNpcMemory(Guid entityId, string memory, int affinityDelta)
        {
            NpcStoryState npc = FindNpcStory(entityId);
            if (npc == null)
                return;
            npc.Remember(memory);
            npc.Affinity = Math.Max(-5, Math.Min(5, npc.Affinity + affinityDelta));
        }

        public void RecordRestorable(EntityState entity, int turnNo, string reason)
        {
            if (entity == null || (entity.EntityId != PlayerEntityId && entity.IsProtected))
                return;
            RestorationLedger.Add(new RestorationRecord(EntityRuntimeSnapshot.Capture(entity), turnNo, reason));
            while (RestorationLedger.Count > 10)
                RestorationLedger.RemoveAt(0);
        }

        public RestorationRecord FindRestoration(Guid? targetEntityId, int maxAge = 6)
        {
            for (int i = RestorationLedger.Count - 1; i >= 0; i--)
            {
                RestorationRecord record = RestorationLedger[i];
                if (record.IsConsumed || CurrentTurn - record.CapturedTurn > maxAge)
                    continue;
                if (!targetEntityId.HasValue || record.Snapshot.EntityId == targetEntityId.Value)
                    return record;
            }
            return null;
        }

        public ReversibleTurnRecord CaptureReversibleTurn(int sourceTurn, AbilityKind ability)
        {
            return new ReversibleTurnRecord(sourceTurn, ability, Focus, Gold, Experience, IsExposed,
                Inventory, Connections, Spatial.CaptureRuntimeState());
        }

        public RunState Clone()
        {
            var clone = new RunState(RunId, WorldSeed, Version, CurrentTurn, TurnLimit, Focus, Status, EndingCode,
                Region, Spatial.Clone(), PlayerEntityId)
            {
                MaxFocus = MaxFocus,
                Experience = Experience,
                Gold = Gold,
                IsExposed = IsExposed,
                CampaignId = CampaignId,
                CampaignTitle = CampaignTitle,
                CampaignPremise = CampaignPremise,
                CurrentBeatIndex = CurrentBeatIndex,
                LastIntentText = LastIntentText,
                LastNormalizedAttempt = LastNormalizedAttempt,
                LastRollRaw = LastRollRaw,
                LastRollModifier = LastRollModifier,
                LastRollDifficulty = LastRollDifficulty,
                LastMechanicalScore = LastMechanicalScore,
                LastIntentAlignment = LastIntentAlignment,
                LastOutcome = LastOutcome,
                LastOutcomeExplanation = LastOutcomeExplanation,
                EnemiesDefeated = EnemiesDefeated,
                RollCount = RollCount,
                AmbientWanderTick = AmbientWanderTick,
                QuestStage = QuestStage,
                QuestAccepted = QuestAccepted,
                AdminAccess = AdminAccess,
                WorldStability = WorldStability,
                WorldAutonomy = WorldAutonomy,
                PublicTrust = PublicTrust,
                TechnicalDebt = TechnicalDebt,
                CompanionBond = CompanionBond,
                TurnPressure = TurnPressure,
                TravelTime = TravelTime,
                HasActiveEncounter = HasActiveEncounter,
                ActiveEncounterReason = ActiveEncounterReason,
                EncounterStagingPosition = EncounterStagingPosition
            };
            for (int i = 0; i < RequiredBeats.Count; i++) clone.RequiredBeats.Add(RequiredBeats[i].Clone());
            for (int i = 0; i < EndingCandidates.Count; i++) clone.EndingCandidates.Add(EndingCandidates[i].Clone());
            clone.CanonicalFacts.AddRange(CanonicalFacts);
            clone.OpenLoops.AddRange(OpenLoops);
            clone.Rumors.AddRange(Rumors);
            clone.ForbiddenEvents.AddRange(ForbiddenEvents);
            for (int i = 0; i < NpcStories.Count; i++) clone.NpcStories.Add(NpcStories[i].Clone());
            clone.IntentHistory.AddRange(IntentHistory);
            for (int i = 0; i < RestorationLedger.Count; i++) clone.RestorationLedger.Add(RestorationLedger[i].Clone());
            for (int i = 0; i < TechnicalDebtEntries.Count; i++)
                clone.TechnicalDebtEntries.Add(TechnicalDebtEntries[i].Clone());
            for (int i = 0; i < AdminAccessAcquisitionHistory.Count; i++)
                clone.AdminAccessAcquisitionHistory.Add(AdminAccessAcquisitionHistory[i].Clone());
            clone.MajorChoices.AddRange(MajorChoices);
            clone.RegionOutcomes.AddRange(RegionOutcomes);
            clone.LastReversibleTurn = LastReversibleTurn == null ? null : LastReversibleTurn.Clone();
            for (int i = 0; i < ReversibleHistory.Count; i++)
                clone.ReversibleHistory.Add(ReversibleHistory[i].Clone());
            clone.Inventory.AddRange(Inventory);
            clone.Connections.AddRange(Connections);
            clone.GmLog.AddRange(GmLog);
            clone.RewardedEnemyIds.UnionWith(RewardedEnemyIds);
            foreach (KeyValuePair<Guid, GridCoord> pair in AmbientWanderOrigins)
                clone.AmbientWanderOrigins[pair.Key] = pair.Value;
            foreach (KeyValuePair<Guid, int> pair in AmbientWanderNextTicks)
                clone.AmbientWanderNextTicks[pair.Key] = pair.Value;
            clone.VisitedAreaIds.Clear();
            clone.VisitedAreaIds.AddRange(VisitedAreaIds);
            return clone;
        }

        private static void AddBoundedUniqueOrRepeated(List<string> list, string value, int limit, bool unique)
        {
            if (string.IsNullOrWhiteSpace(value))
                return;
            string trimmed = value.Trim();
            if (unique && list.Exists(item => string.Equals(item, trimmed, StringComparison.Ordinal)))
                return;
            list.Add(trimmed);
            while (list.Count > limit)
                list.RemoveAt(0);
        }

        public static int ClampMetric(int value) { return Math.Max(0, Math.Min(100, value)); }
    }
}
