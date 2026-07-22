using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
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
        private const int CurrentSchemaVersion = 9;
#if UNITY_EDITOR
        private static string _editorSaveDirectory = string.Empty;
#endif

        public static string SavePath => Path.Combine(SaveDirectory, FileName);
        public static string BackupPath => SavePath + ".bak";
        public static bool HasSave => File.Exists(SavePath) || File.Exists(BackupPath);

        private static string SaveDirectory
        {
            get
            {
#if UNITY_EDITOR
                if (!string.IsNullOrEmpty(_editorSaveDirectory))
                    return _editorSaveDirectory;
#endif
                return Application.persistentDataPath;
            }
        }

#if UNITY_EDITOR
        internal static void SetEditorSaveDirectory(string directory)
        {
            if (string.IsNullOrWhiteSpace(directory))
                throw new ArgumentException("An isolated save directory is required.", nameof(directory));
            _editorSaveDirectory = Path.GetFullPath(directory);
        }

        internal static void ClearEditorSaveDirectory()
        {
            _editorSaveDirectory = string.Empty;
        }
#endif

        public static void Save(LocalTurnService service)
        {
            if (service == null) throw new ArgumentNullException(nameof(service));
            string json = Serialize(service);
            string directory = Path.GetDirectoryName(SavePath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            string temporary = SavePath + ".tmp";
            WriteDurable(temporary, json);
            if (File.Exists(SavePath))
                File.Replace(temporary, SavePath, BackupPath);
            else
                File.Move(temporary, SavePath);
        }

        public static LocalTurnService Load()
        {
            if (!HasSave) return null;
            if (TryLoadPath(SavePath, out LocalTurnService primary, out Exception primaryError)) return primary;
            if (TryLoadPath(BackupPath, out LocalTurnService backup, out Exception backupError))
            {
                Debug.LogWarning("Codria primary save was invalid; recovered the backup: " + primaryError?.Message);
                string temporary = SavePath + ".tmp";
                WriteDurable(temporary, File.ReadAllText(BackupPath));
                if (File.Exists(SavePath)) File.Replace(temporary, SavePath, null);
                else File.Move(temporary, SavePath);
                return backup;
            }
            Debug.LogWarning("Codria save could not be loaded: " +
                (primaryError?.Message ?? backupError?.Message ?? "unknown error"));
            return null;
        }

        public static void Delete()
        {
            if (File.Exists(SavePath)) File.Delete(SavePath);
            string temporary = SavePath + ".tmp";
            if (File.Exists(temporary)) File.Delete(temporary);
            if (File.Exists(BackupPath)) File.Delete(BackupPath);
        }

        public static string Serialize(LocalTurnService service)
        {
            if (service == null) throw new ArgumentNullException(nameof(service));
            SaveData data = SaveData.FromState(service.CreateSnapshot());
            data.checksum = string.Empty;
            data.checksum = Sha256(JsonUtility.ToJson(data, false));
            return JsonUtility.ToJson(data, false);
        }

        public static LocalTurnService Deserialize(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                throw new ArgumentException("Save JSON is required.", nameof(json));
            SaveData data = JsonUtility.FromJson<SaveData>(json);
            if (data == null)
                throw new InvalidDataException("Save JSON could not be parsed.");
            if (data.schemaVersion >= 9)
            {
                string savedChecksum = data.checksum;
                data.checksum = string.Empty;
                string actualChecksum = Sha256(JsonUtility.ToJson(data, false));
                if (string.IsNullOrWhiteSpace(savedChecksum) ||
                    !string.Equals(savedChecksum, actualChecksum, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Save checksum does not match its contents.");
                data.checksum = savedChecksum;
            }
            return data.ToService();
        }

        private static bool TryLoadPath(string path, out LocalTurnService service, out Exception error)
        {
            service = null;
            error = null;
            if (!File.Exists(path)) return false;
            try { service = Deserialize(File.ReadAllText(path)); return service != null; }
            catch (Exception exception) { error = exception; return false; }
        }

        private static void WriteDurable(string path, string contents)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(contents);
            using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(true);
            }
        }

        private static string Sha256(string value)
        {
            using (SHA256 algorithm = SHA256.Create())
            {
                byte[] hash = algorithm.ComputeHash(Encoding.UTF8.GetBytes(value ?? string.Empty));
                var result = new StringBuilder(hash.Length * 2);
                for (int i = 0; i < hash.Length; i++) result.Append(hash[i].ToString("x2"));
                return result.ToString();
            }
        }

        [Serializable]
        private sealed class SaveData
        {
            public int schemaVersion;
            public string checksum;
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
            public int rollCount;
            public List<string> rewardedEnemyIds = new List<string>();
            public int ambientWanderTick;
            public List<WanderData> ambientWanderStates = new List<WanderData>();
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
                    rollCount = state.RollCount,
                    ambientWanderTick = state.AmbientWanderTick,
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
                foreach (Guid rewardedEnemyId in state.RewardedEnemyIds)
                    data.rewardedEnemyIds.Add(rewardedEnemyId.ToString("N"));
                foreach (KeyValuePair<Guid, GridCoord> pair in state.AmbientWanderOrigins)
                {
                    state.AmbientWanderNextTicks.TryGetValue(pair.Key, out int nextTick);
                    data.ambientWanderStates.Add(new WanderData
                    {
                        entityId = pair.Key.ToString("N"), originX = pair.Value.X,
                        originY = pair.Value.Y, nextTick = nextTick
                    });
                }
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
                if (schemaVersion < 6 || schemaVersion > CurrentSchemaVersion)
                    throw new InvalidDataException("Save schema is not supported by this campaign rules version.");
                ValidateEnvelope();
                if (schemaVersion == 6)
                    rollCount = Math.Max(currentTurn, intentHistory == null ? 0 : intentHistory.Count);
                RegionMap region = DeterministicRegionGenerator.Generate(worldSeed, regionKey, worldWidth, worldHeight);
                if (!string.Equals(region.LayoutHash, layoutHash, StringComparison.Ordinal))
                    throw new InvalidDataException("Saved layout does not match the deterministic generator.");
                CampaignBlueprint expected = CampaignCatalog.Create(worldSeed);
                if (!string.Equals(expected.Id, campaignId, StringComparison.Ordinal))
                    throw new InvalidDataException("Saved campaign does not match its deterministic seed.");

                var spatial = new SpatialIndex();
                for (int i = 0; i < entities.Count; i++) entities[i].RegisterInto(spatial, region.RegionId);
                if (!Guid.TryParse(playerEntityId, out Guid parsedPlayerId) ||
                    !spatial.TryGetEntity(parsedPlayerId, out EntityState savedPlayer) ||
                    !savedPlayer.IsActive || savedPlayer.Kind != EntityKind.Player)
                    throw new InvalidDataException("Save does not contain one active player entity.");
                List<string> spatialErrors = spatial.Validate((regionId, coord) =>
                    regionId == region.RegionId && region.IsWalkable(coord));
                if (spatialErrors.Count > 0)
                    throw new InvalidDataException("Saved spatial state is invalid: " + string.Join(",", spatialErrors));
                var state = new RunState(Guid.Parse(runId), worldSeed, version, currentTurn, turnLimit, focus,
                    (RunStatus)status, endingCode, region, spatial, parsedPlayerId)
                {
                    MaxFocus = maxFocus,
                    Experience = experience,
                    Gold = gold,
                    IsExposed = isExposed,
                    EnemiesDefeated = enemiesDefeated,
                    RollCount = rollCount,
                    AmbientWanderTick = ambientWanderTick,
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
                if (rewardedEnemyIds != null)
                    for (int i = 0; i < rewardedEnemyIds.Count; i++)
                        if (Guid.TryParse(rewardedEnemyIds[i], out Guid rewardedEnemyId))
                            state.RewardedEnemyIds.Add(rewardedEnemyId);
                if (schemaVersion == 6)
                    for (int i = 0; i < entities.Count; i++)
                        if (!entities[i].active && entities[i].hostile &&
                            (EntityKind)entities[i].kind == EntityKind.Enemy &&
                            Guid.TryParse(entities[i].id, out Guid defeatedEnemyId))
                            state.RewardedEnemyIds.Add(defeatedEnemyId);
                if (ambientWanderStates != null)
                    for (int i = 0; i < ambientWanderStates.Count; i++)
                    {
                        WanderData wander = ambientWanderStates[i];
                        if (!Guid.TryParse(wander.entityId, out Guid entityId)) continue;
                        state.AmbientWanderOrigins[entityId] = new GridCoord(wander.originX, wander.originY);
                        state.AmbientWanderNextTicks[entityId] = Math.Max(ambientWanderTick, wander.nextTick);
                    }
                state.VisitedAreaIds.Clear();
                state.VisitedAreaIds.AddRange(visitedAreaIds ?? new List<string>());
                return new LocalTurnService(state,
                    new SeededD20Source(unchecked((int)worldSeed), Math.Max(0, rollCount)));
            }

            private void ValidateEnvelope()
            {
                if (!Guid.TryParse(runId, out _) || !Guid.TryParse(playerEntityId, out _))
                    throw new InvalidDataException("Save contains an invalid run or player ID.");
                if (version < 1 || currentTurn < 0 || turnLimit < 30 ||
                    turnLimit > LocalTurnService.MaximumCampaignTurnLimit || currentTurn > turnLimit)
                    throw new InvalidDataException("Save contains invalid turn or version values.");
                if (worldWidth < 16 || worldHeight < 16 || worldWidth > 256 || worldHeight > 256)
                    throw new InvalidDataException("Save world dimensions are outside supported bounds.");
                if (focus < 0 || maxFocus < 1 || focus > maxFocus || maxFocus > 100 ||
                    experience < 0 || gold < 0 || enemiesDefeated < 0 || rollCount < 0)
                    throw new InvalidDataException("Save contains invalid resource values.");
                if (!Enum.IsDefined(typeof(RunStatus), status))
                    throw new InvalidDataException("Save contains an invalid run status.");
                ValidateText(regionKey, 128, "region key");
                ValidateText(layoutHash, 256, "layout hash");
                ValidateText(campaignId, 256, "campaign ID");
                ValidateText(campaignTitle, 256, "campaign title");
                ValidateList(entities, 4096, "entities");
                ValidateList(requiredBeats, 64, "required beats");
                ValidateList(endingCandidates, 64, "ending candidates");
                ValidateList(restorationLedger, 64, "restoration ledger");
                ValidateList(reversibleHistory, 64, "reversible history");
                ValidateList(technicalDebtEntries, 256, "technical debt entries");
                ValidateList(inventory, 256, "inventory");
                ValidateList(connections, 1024, "connections");
                ValidateList(gmLog, 128, "GM log");
            }

            private static void ValidateText(string value, int maximumLength, string label)
            {
                if (string.IsNullOrWhiteSpace(value) || value.Length > maximumLength)
                    throw new InvalidDataException("Save " + label + " is missing or too long.");
            }

            private static void ValidateList<T>(List<T> values, int maximumCount, string label)
            {
                if (values == null || values.Count > maximumCount)
                    throw new InvalidDataException("Save " + label + " exceeds supported bounds.");
            }
        }

        [Serializable]
        private sealed class WanderData
        {
            public string entityId;
            public int originX;
            public int originY;
            public int nextTick;
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
            public int trust;
            public int fear;
            public int obligation;
            public string motivation;
            public string secret;
            public string currentConcern;
            public int lastConversationTurn;
            public List<string> revealedClues = new List<string>();
            public List<string> memories = new List<string>();

            public static NpcData FromState(NpcStoryState state)
            {
                return new NpcData { entityId = state.EntityId.ToString("N"), npcName = state.NpcName,
                    role = state.Role, affinity = state.Affinity, trust = state.Trust, fear = state.Fear,
                    obligation = state.Obligation, motivation = state.Motivation, secret = state.Secret,
                    currentConcern = state.CurrentConcern, lastConversationTurn = state.LastConversationTurn,
                    revealedClues = new List<string>(state.RevealedClues), memories = new List<string>(state.Memories) };
            }

            public NpcStoryState ToState()
            {
                var state = new NpcStoryState(Guid.Parse(entityId), npcName, role, affinity);
                state.Trust = trust;
                state.Fear = fear;
                state.Obligation = obligation;
                state.Motivation = motivation ?? string.Empty;
                state.Secret = secret ?? string.Empty;
                state.CurrentConcern = currentConcern ?? string.Empty;
                state.LastConversationTurn = lastConversationTurn;
                state.RevealedClues.AddRange(revealedClues ?? new List<string>());
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
