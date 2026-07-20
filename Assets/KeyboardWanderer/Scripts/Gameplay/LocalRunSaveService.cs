using System;
using System.Collections.Generic;
using System.IO;
using KeyboardWanderer.Core;
using UnityEngine;

namespace KeyboardWanderer.Gameplay
{
    /// <summary>
    /// Atomic local snapshot for the standalone demo. It persists narrative-domain state as well as
    /// mechanical snapshots, while the immutable map is regenerated and verified by layout hash.
    /// </summary>
    public static class LocalRunSaveService
    {
        private const string FileName = "codria-save-v4.json";
        private const int CurrentSchemaVersion = 6;

        public static string SavePath => Path.Combine(Application.persistentDataPath, FileName);
        public static bool HasSave => File.Exists(SavePath);

        public static void Save(LocalTurnService service)
        {
            if (service == null) throw new ArgumentNullException(nameof(service));
            string json = Serialize(service);
            string directory = Path.GetDirectoryName(SavePath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            string temporary = SavePath + ".tmp";
            File.WriteAllText(temporary, json);
            if (File.Exists(SavePath)) File.Delete(SavePath);
            File.Move(temporary, SavePath);
        }

        public static LocalTurnService Load()
        {
            if (!HasSave) return null;
            try
            {
                SaveData data = JsonUtility.FromJson<SaveData>(File.ReadAllText(SavePath));
                return data == null ? null : data.ToService();
            }
            catch (Exception exception)
            {
                Debug.LogWarning("Codria save could not be loaded: " + exception.Message);
                return null;
            }
        }

        public static void Delete()
        {
            if (File.Exists(SavePath)) File.Delete(SavePath);
            string temporary = SavePath + ".tmp";
            if (File.Exists(temporary)) File.Delete(temporary);
        }

        public static string Serialize(LocalTurnService service)
        {
            if (service == null) throw new ArgumentNullException(nameof(service));
            return JsonUtility.ToJson(SaveData.FromState(service.CreateSnapshot()), false);
        }

        public static LocalTurnService Deserialize(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                throw new ArgumentException("Save JSON is required.", nameof(json));
            SaveData data = JsonUtility.FromJson<SaveData>(json);
            if (data == null)
                throw new InvalidDataException("Save JSON could not be parsed.");
            return data.ToService();
        }

        [Serializable]
        private sealed class SaveData
        {
            public int schemaVersion;
            public string runId;
            public long worldSeed;
            public long version;
            public int currentTurn;
            public int turnLimit;
            public int focus;
            public int maxFocus;
            public int experience;
            public int gold;
            public bool isExposed;
            public int status;
            public string endingCode;
            public string regionKey;
            public int worldWidth;
            public int worldHeight;
            public string layoutHash;
            public string playerEntityId;
            public int enemiesDefeated;
            public int adminAccess;
            public int worldStability;
            public int worldAutonomy;
            public int publicTrust;
            public int technicalDebt;
            public int companionBond;
            public int turnPressure;
            public int travelTime;
            public List<string> visitedAreaIds = new List<string>();
            public bool hasActiveEncounter;
            public string activeEncounterReason;
            public int encounterStagingX;
            public int encounterStagingY;

            public string campaignId;
            public string campaignTitle;
            public string campaignPremise;
            public int currentBeatIndex;
            public List<BeatData> requiredBeats = new List<BeatData>();
            public List<EndingData> endingCandidates = new List<EndingData>();
            public List<string> canonicalFacts = new List<string>();
            public List<string> openLoops = new List<string>();
            public List<string> rumors = new List<string>();
            public List<string> forbiddenEvents = new List<string>();
            public List<NpcData> npcStories = new List<NpcData>();
            public List<DebtData> technicalDebtEntries = new List<DebtData>();
            public List<AccessData> adminAccessHistory = new List<AccessData>();
            public List<string> majorChoices = new List<string>();
            public List<string> regionOutcomes = new List<string>();

            public string lastIntentText;
            public string lastNormalizedAttempt;
            public int lastRollRaw;
            public int lastRollModifier;
            public int lastRollDifficulty;
            public int lastMechanicalScore;
            public int lastIntentAlignment;
            public int lastOutcome;
            public string lastOutcomeExplanation;
            public List<string> intentHistory = new List<string>();
            public List<RestorationData> restorationLedger = new List<RestorationData>();
            public UndoData lastReversibleTurn;
            public List<UndoData> reversibleHistory = new List<UndoData>();

            public List<string> inventory = new List<string>();
            public List<string> connections = new List<string>();
            public List<string> gmLog = new List<string>();
            public List<EntityData> entities = new List<EntityData>();

            public static SaveData FromState(RunState state)
            {
                var data = new SaveData
                {
                    schemaVersion = CurrentSchemaVersion,
                    runId = state.RunId.ToString("N"),
                    worldSeed = state.WorldSeed,
                    version = state.Version,
                    currentTurn = state.CurrentTurn,
                    turnLimit = state.TurnLimit,
                    focus = state.Focus,
                    maxFocus = state.MaxFocus,
                    experience = state.Experience,
                    gold = state.Gold,
                    isExposed = state.IsExposed,
                    status = (int)state.Status,
                    endingCode = state.EndingCode,
                    regionKey = state.Region.RegionKey,
                    worldWidth = state.Region.Width,
                    worldHeight = state.Region.Height,
                    layoutHash = state.Region.LayoutHash,
                    playerEntityId = state.PlayerEntityId.ToString("N"),
                    enemiesDefeated = state.EnemiesDefeated,
                    adminAccess = state.AdminAccess,
                    worldStability = state.WorldStability,
                    worldAutonomy = state.WorldAutonomy,
                    publicTrust = state.PublicTrust,
                    technicalDebt = state.TechnicalDebt,
                    companionBond = state.CompanionBond,
                    turnPressure = state.TurnPressure,
                    travelTime = state.TravelTime,
                    visitedAreaIds = new List<string>(state.VisitedAreaIds),
                    hasActiveEncounter = state.HasActiveEncounter,
                    activeEncounterReason = state.ActiveEncounterReason,
                    encounterStagingX = state.EncounterStagingPosition.X,
                    encounterStagingY = state.EncounterStagingPosition.Y,
                    campaignId = state.CampaignId,
                    campaignTitle = state.CampaignTitle,
                    campaignPremise = state.CampaignPremise,
                    currentBeatIndex = state.CurrentBeatIndex,
                    canonicalFacts = new List<string>(state.CanonicalFacts),
                    openLoops = new List<string>(state.OpenLoops),
                    rumors = new List<string>(state.Rumors),
                    forbiddenEvents = new List<string>(state.ForbiddenEvents),
                    majorChoices = new List<string>(state.MajorChoices),
                    regionOutcomes = new List<string>(state.RegionOutcomes),
                    lastIntentText = state.LastIntentText,
                    lastNormalizedAttempt = state.LastNormalizedAttempt,
                    lastRollRaw = state.LastRollRaw,
                    lastRollModifier = state.LastRollModifier,
                    lastRollDifficulty = state.LastRollDifficulty,
                    lastMechanicalScore = state.LastMechanicalScore,
                    lastIntentAlignment = state.LastIntentAlignment,
                    lastOutcome = (int)state.LastOutcome,
                    lastOutcomeExplanation = state.LastOutcomeExplanation,
                    intentHistory = new List<string>(state.IntentHistory),
                    inventory = new List<string>(state.Inventory),
                    connections = new List<string>(state.Connections),
                    gmLog = new List<string>(state.GmLog)
                };
                for (int i = 0; i < state.RequiredBeats.Count; i++)
                    data.requiredBeats.Add(BeatData.FromState(state.RequiredBeats[i]));
                for (int i = 0; i < state.EndingCandidates.Count; i++)
                    data.endingCandidates.Add(EndingData.FromState(state.EndingCandidates[i]));
                for (int i = 0; i < state.NpcStories.Count; i++)
                    data.npcStories.Add(NpcData.FromState(state.NpcStories[i]));
                for (int i = 0; i < state.RestorationLedger.Count; i++)
                    data.restorationLedger.Add(RestorationData.FromState(state.RestorationLedger[i]));
                for (int i = 0; i < state.TechnicalDebtEntries.Count; i++)
                    data.technicalDebtEntries.Add(DebtData.FromState(state.TechnicalDebtEntries[i]));
                for (int i = 0; i < state.AdminAccessAcquisitionHistory.Count; i++)
                    data.adminAccessHistory.Add(AccessData.FromState(state.AdminAccessAcquisitionHistory[i]));
                data.lastReversibleTurn = state.LastReversibleTurn == null
                    ? null
                    : UndoData.FromState(state.LastReversibleTurn);
                for (int i = 0; i < state.ReversibleHistory.Count; i++)
                    data.reversibleHistory.Add(UndoData.FromState(state.ReversibleHistory[i]));
                foreach (EntityState entity in state.Spatial.Entities)
                    data.entities.Add(EntityData.FromEntity(entity));
                return data;
            }

            public LocalTurnService ToService()
            {
                if (schemaVersion != CurrentSchemaVersion)
                    throw new InvalidDataException("Save schema is not supported by this campaign rules version.");
                RegionMap region = DeterministicRegionGenerator.Generate(worldSeed, regionKey, worldWidth, worldHeight);
                if (!string.Equals(region.LayoutHash, layoutHash, StringComparison.Ordinal))
                    throw new InvalidDataException("Saved layout does not match the deterministic generator.");
                CampaignBlueprint expected = CampaignCatalog.Create(worldSeed);
                if (!string.Equals(expected.Id, campaignId, StringComparison.Ordinal))
                    throw new InvalidDataException("Saved campaign does not match its deterministic seed.");

                var spatial = new SpatialIndex();
                for (int i = 0; i < entities.Count; i++) entities[i].RegisterInto(spatial, region.RegionId);
                var state = new RunState(Guid.Parse(runId), worldSeed, version, currentTurn, turnLimit, focus,
                    (RunStatus)status, endingCode, region, spatial, Guid.Parse(playerEntityId))
                {
                    MaxFocus = maxFocus,
                    Experience = experience,
                    Gold = gold,
                    IsExposed = isExposed,
                    EnemiesDefeated = enemiesDefeated,
                    CampaignId = campaignId,
                    CampaignTitle = campaignTitle,
                    CampaignPremise = campaignPremise,
                    CurrentBeatIndex = currentBeatIndex,
                    LastIntentText = lastIntentText ?? string.Empty,
                    LastNormalizedAttempt = lastNormalizedAttempt ?? string.Empty,
                    LastRollRaw = lastRollRaw,
                    LastRollModifier = lastRollModifier,
                    LastRollDifficulty = lastRollDifficulty,
                    LastMechanicalScore = lastMechanicalScore,
                    LastIntentAlignment = lastIntentAlignment,
                    LastOutcome = (RuleOutcome)lastOutcome,
                    LastOutcomeExplanation = lastOutcomeExplanation ?? string.Empty,
                    AdminAccess = adminAccess,
                    WorldStability = worldStability,
                    WorldAutonomy = worldAutonomy,
                    PublicTrust = publicTrust,
                    TechnicalDebt = technicalDebt,
                    CompanionBond = companionBond,
                    TurnPressure = turnPressure,
                    TravelTime = travelTime,
                    HasActiveEncounter = hasActiveEncounter,
                    ActiveEncounterReason = activeEncounterReason ?? string.Empty,
                    EncounterStagingPosition = new GridCoord(encounterStagingX, encounterStagingY)
                };

                for (int i = 0; i < requiredBeats.Count; i++) state.RequiredBeats.Add(requiredBeats[i].ToState());
                for (int i = 0; i < endingCandidates.Count; i++) state.EndingCandidates.Add(endingCandidates[i].ToState());
                state.CanonicalFacts.AddRange(canonicalFacts ?? new List<string>());
                state.OpenLoops.AddRange(openLoops ?? new List<string>());
                state.Rumors.AddRange(rumors ?? new List<string>());
                state.ForbiddenEvents.AddRange(forbiddenEvents ?? new List<string>());
                for (int i = 0; i < npcStories.Count; i++) state.NpcStories.Add(npcStories[i].ToState());
                state.IntentHistory.AddRange(intentHistory ?? new List<string>());
                for (int i = 0; i < restorationLedger.Count; i++)
                    state.RestorationLedger.Add(restorationLedger[i].ToState(region.RegionId));
                for (int i = 0; i < technicalDebtEntries.Count; i++)
                    state.TechnicalDebtEntries.Add(technicalDebtEntries[i].ToState());
                for (int i = 0; i < adminAccessHistory.Count; i++)
                    state.AdminAccessAcquisitionHistory.Add(adminAccessHistory[i].ToState());
                state.MajorChoices.AddRange(majorChoices ?? new List<string>());
                state.RegionOutcomes.AddRange(regionOutcomes ?? new List<string>());
                state.LastReversibleTurn = lastReversibleTurn == null
                    ? null
                    : lastReversibleTurn.ToState(region.RegionId);
                if (reversibleHistory != null)
                    for (int i = 0; i < reversibleHistory.Count; i++)
                        state.ReversibleHistory.Add(reversibleHistory[i].ToState(region.RegionId));
                state.Inventory.AddRange(inventory ?? new List<string>());
                state.Connections.AddRange(connections ?? new List<string>());
                state.GmLog.AddRange(gmLog ?? new List<string>());
                state.VisitedAreaIds.Clear();
                state.VisitedAreaIds.AddRange(visitedAreaIds ?? new List<string>());
                return new LocalTurnService(state,
                    new SeededD20Source(unchecked((int)worldSeed), Math.Max(0, currentTurn)));
            }
        }

        [Serializable]
        private sealed class BeatData
        {
            public string id;
            public string title;
            public string objective;
            public int triggerAbility;
            public int requiredContext;
            public string roleId;
            public string adminAccessRewardId;
            public bool required;
            public bool completed;
            public bool skipped;
            public int resolvedTurn;
            public string resolution;

            public static BeatData FromState(CampaignBeatState state)
            {
                return new BeatData { id = state.Id, title = state.Title, objective = state.Objective,
                    triggerAbility = (int)state.TriggerAbility, requiredContext = (int)state.RequiredContext,
                    required = state.IsRequired,
                    roleId = state.RoleId, adminAccessRewardId = state.AdminAccessRewardId,
                    completed = state.IsCompleted, skipped = state.IsSkipped,
                    resolvedTurn = state.ResolvedTurn, resolution = state.Resolution };
            }

            public CampaignBeatState ToState()
            {
                return new CampaignBeatState(id, title, objective, (AbilityKind)triggerAbility, required,
                    roleId, adminAccessRewardId, (ActionContext)requiredContext)
                { IsCompleted = completed, IsSkipped = skipped, ResolvedTurn = resolvedTurn,
                    Resolution = resolution ?? string.Empty };
            }
        }

        [Serializable]
        private sealed class DebtData
        {
            public string id;
            public int turnNo;
            public int skill;
            public string operationType;
            public string targetId;
            public bool forcedOverride;
            public int debtDelta;
            public string deferredConsequenceType;
            public int resolvedTurn;

            public static DebtData FromState(TechnicalDebtEntry state)
            {
                return new DebtData { id = state.Id, turnNo = state.TurnNo, skill = (int)state.Skill,
                    operationType = state.OperationType, targetId = state.TargetId,
                    forcedOverride = state.ForcedOverride, debtDelta = state.DebtDelta,
                    deferredConsequenceType = state.DeferredConsequenceType,
                    resolvedTurn = state.ResolvedTurn };
            }

            public TechnicalDebtEntry ToState()
            {
                return new TechnicalDebtEntry(id, turnNo, (AbilityKind)skill, operationType, targetId,
                    forcedOverride, debtDelta, deferredConsequenceType) { ResolvedTurn = resolvedTurn };
            }
        }

        [Serializable]
        private sealed class AccessData
        {
            public int level;
            public string accessId;
            public string regionAxis;
            public int context;
            public int skill;
            public int turnNo;

            public static AccessData FromState(AdminAccessAcquisitionRecord state)
            {
                return new AccessData { level = state.Level, accessId = state.AccessId,
                    regionAxis = state.RegionAxis, context = (int)state.Context,
                    skill = (int)state.Skill, turnNo = state.TurnNo };
            }

            public AdminAccessAcquisitionRecord ToState()
            {
                return new AdminAccessAcquisitionRecord(level, accessId, regionAxis,
                    (ActionContext)context, (AbilityKind)skill, turnNo);
            }
        }

        [Serializable]
        private sealed class EndingData
        {
            public string code;
            public string title;
            public string description;
            public int minimumCompletedBeats;
            public bool eligible;

            public static EndingData FromState(EndingCandidateState state)
            {
                return new EndingData { code = state.Code, title = state.Title, description = state.Description,
                    minimumCompletedBeats = state.MinimumCompletedBeats, eligible = state.IsEligible };
            }

            public EndingCandidateState ToState()
            {
                return new EndingCandidateState(code, title, description, minimumCompletedBeats)
                { IsEligible = eligible };
            }
        }

        [Serializable]
        private sealed class NpcData
        {
            public string entityId;
            public string npcName;
            public string role;
            public int affinity;
            public List<string> memories = new List<string>();

            public static NpcData FromState(NpcStoryState state)
            {
                return new NpcData { entityId = state.EntityId.ToString("N"), npcName = state.NpcName,
                    role = state.Role, affinity = state.Affinity, memories = new List<string>(state.Memories) };
            }

            public NpcStoryState ToState()
            {
                var state = new NpcStoryState(Guid.Parse(entityId), npcName, role, affinity);
                state.Memories.AddRange(memories ?? new List<string>());
                return state;
            }
        }

        [Serializable]
        private sealed class SnapshotData
        {
            public string entityId;
            public int x;
            public int y;
            public int layer;
            public int health;
            public bool opened;
            public bool active;

            public static SnapshotData FromState(EntityRuntimeSnapshot state)
            {
                return new SnapshotData { entityId = state.EntityId.ToString("N"), x = state.Position.X,
                    y = state.Position.Y, layer = state.Layer, health = state.Health,
                    opened = state.IsOpened, active = state.IsActive };
            }

            public EntityRuntimeSnapshot ToState(Guid regionId)
            {
                return new EntityRuntimeSnapshot(Guid.Parse(entityId), regionId, new GridCoord(x, y), layer,
                    health, opened, active);
            }
        }

        [Serializable]
        private sealed class RestorationData
        {
            public SnapshotData snapshot;
            public int capturedTurn;
            public string reason;
            public bool consumed;

            public static RestorationData FromState(RestorationRecord state)
            {
                return new RestorationData { snapshot = SnapshotData.FromState(state.Snapshot),
                    capturedTurn = state.CapturedTurn, reason = state.Reason, consumed = state.IsConsumed };
            }

            public RestorationRecord ToState(Guid regionId)
            {
                return new RestorationRecord(snapshot.ToState(regionId), capturedTurn, reason)
                    { IsConsumed = consumed };
            }
        }

        [Serializable]
        private sealed class UndoData
        {
            public int sourceTurn;
            public int sourceAbility;
            public int focusBefore;
            public int goldBefore;
            public int experienceBefore;
            public bool exposedBefore;
            public List<string> inventoryBefore = new List<string>();
            public List<string> connectionsBefore = new List<string>();
            public List<SnapshotData> snapshots = new List<SnapshotData>();
            public List<string> irreversibleEntityIds = new List<string>();
            public bool restoreInventory;
            public bool restoreEconomy;
            public bool restoreConnections;
            public bool consumed;

            public static UndoData FromState(ReversibleTurnRecord state)
            {
                var data = new UndoData { sourceTurn = state.SourceTurn,
                    sourceAbility = (int)state.SourceAbility, focusBefore = state.FocusBefore,
                    goldBefore = state.GoldBefore, experienceBefore = state.ExperienceBefore,
                    exposedBefore = state.WasExposedBefore,
                    inventoryBefore = new List<string>(state.InventoryBefore),
                    connectionsBefore = new List<string>(state.ConnectionsBefore),
                    restoreInventory = state.RestoreInventory, restoreEconomy = state.RestoreEconomy,
                    restoreConnections = state.RestoreConnections, consumed = state.IsConsumed };
                for (int i = 0; i < state.EntitySnapshots.Count; i++)
                    data.snapshots.Add(SnapshotData.FromState(state.EntitySnapshots[i]));
                for (int i = 0; i < state.IrreversibleEntityIds.Count; i++)
                    data.irreversibleEntityIds.Add(state.IrreversibleEntityIds[i].ToString("N"));
                return data;
            }

            public ReversibleTurnRecord ToState(Guid regionId)
            {
                var entitySnapshots = new List<EntityRuntimeSnapshot>();
                for (int i = 0; i < snapshots.Count; i++) entitySnapshots.Add(snapshots[i].ToState(regionId));
                var state = new ReversibleTurnRecord(sourceTurn, (AbilityKind)sourceAbility, focusBefore,
                    goldBefore, experienceBefore, exposedBefore, inventoryBefore, connectionsBefore,
                    entitySnapshots) { RestoreInventory = restoreInventory, RestoreEconomy = restoreEconomy,
                    RestoreConnections = restoreConnections, IsConsumed = consumed };
                for (int i = 0; i < irreversibleEntityIds.Count; i++)
                    state.IrreversibleEntityIds.Add(Guid.Parse(irreversibleEntityIds[i]));
                return state;
            }
        }

        [Serializable]
        private sealed class EntityData
        {
            public string id;
            public int kind;
            public string assetId;
            public string displayName;
            public bool blocking;
            public bool protectedEntity;
            public bool cloneable;
            public bool hostile;
            public bool active;
            public bool opened;
            public int health;
            public int maxHealth;
            public int x;
            public int y;
            public int layer;

            public static EntityData FromEntity(EntityState entity)
            {
                return new EntityData { id = entity.EntityId.ToString("N"), kind = (int)entity.Kind,
                    assetId = entity.AssetId, displayName = entity.DisplayName, blocking = entity.IsBlocking,
                    protectedEntity = entity.IsProtected, cloneable = entity.IsCloneable,
                    hostile = entity.IsHostile, active = entity.IsActive, opened = entity.IsOpened,
                    health = entity.Health, maxHealth = entity.MaxHealth, x = entity.Position.X,
                    y = entity.Position.Y, layer = entity.Layer };
            }

            public void RegisterInto(SpatialIndex spatial, Guid regionId)
            {
                var entity = new EntityState(Guid.Parse(id), (EntityKind)kind, assetId, displayName, blocking,
                    protectedEntity, cloneable, hostile, maxHealth, regionId, new GridCoord(x, y), layer);
                entity.RestoreRuntimeState(health, opened, true);
                if (!spatial.Register(entity, out string error))
                    throw new InvalidDataException("Saved entity could not be restored: " + error);
                if (!active && !spatial.TryRemove(entity.EntityId, out error))
                    throw new InvalidDataException("Saved inactive entity could not be restored: " + error);
            }
        }
    }
}
