using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

#pragma warning disable 0649 // JsonUtility populates serialized DTO fields through reflection.

namespace KeyboardWanderer.Networking
{
    /// <summary>
    /// REST client for the server-authoritative campaign/run/turn flow.
    /// It deliberately contains no model key and treats every response as untrusted data.
    /// </summary>
    public sealed class GameApiClient
    {
        public const string DefaultBaseUrl = "http://127.0.0.1:8787";

        [Serializable]
        public sealed class PointSnapshot
        {
            public string id;
            public string areaId;
            public string biomeId;
            public string campaignRole;
            public string kind;
            public string name;
            public string nameKo;
            public string summary;
            public string visualIntent;
            public int phase;
            public int orderHint;
            public int x;
            public int y;
        }

        [Serializable]
        public sealed class BoundsSnapshot
        {
            public int x;
            public int y;
            public int width;
            public int height;
        }

        [Serializable]
        public sealed class AreaSnapshot
        {
            public string id;
            public string name;
            public string nameKo;
            public string kind;
            public string biomeId;
            public string campaignRole;
            public string landmarkType;
            public string summary;
            public int travelCost;
            public int requiredAdminAccess;
            public string[] neighborAreaIds;
            public string[] tags;
            public BoundsSnapshot bounds;
            public PositionSnapshot anchor;
        }

        [Serializable]
        public sealed class BiomeSnapshot
        {
            public string id;
            public string name;
            public string nameKo;
            public string visualIntent;
            public string[] palette;
            public int offRoadCost;
        }

        [Serializable]
        public sealed class CampaignRegionRoleSnapshot
        {
            public string id;
            public int phase;
            public string[] candidateLandmarkNames;
        }

        [Serializable]
        public sealed class PlacementSlotSnapshot
        {
            public string id;
            public string areaId;
            public string biomeId;
            public string campaignRole;
            public string kind;
            public string purpose;
            public string reservedFor;
            public string[] tags;
            public string[] allowedAssetIds;
            public int x;
            public int y;
            public string occupiedBy;
            public bool gated;
            public int requiresAdminLevel;
            public string[] requiresAccessTokens;
            public string[] acquisitionModes;
            public int clearanceRadius;
            public string reachability;
            public bool reachable;
            public string[] visualIntents;
            public PositionSnapshot interactionAnchor;
        }

        [Serializable]
        public sealed class WorldRouteSnapshot
        {
            public string id;
            public string fromAreaId;
            public string toAreaId;
            public string kind;
            public string traversalKind;
            public int width;
            public bool isLoop;
            public bool gated;
            public int requiresAdminLevel;
            public string[] requiresAccessTokens;
            public bool campaignTurnConsumed;
            public PositionSnapshot[] path;
        }

        [Serializable]
        public sealed class ProgressionCandidatePathSnapshot
        {
            public string slotId;
            public string[] acquisitionModes;
        }

        [Serializable]
        public sealed class ProgressionNodeSnapshot
        {
            public string id;
            public string campaignRole;
            public string[] requires;
            public int rewardAdminLevel;
            public string rewardAccessToken;
            public string areaId;
            public string[] candidateSlotIds;
            public ProgressionCandidatePathSnapshot[] candidateAcquisitionPaths;
            public string[] acquisitionModes;
        }

        [Serializable]
        public sealed class ProgressionEdgeSnapshot
        {
            public string from;
            public string to;
        }

        [Serializable]
        public sealed class ProgressionRootGateSnapshot
        {
            public int requiresAdminLevel;
            public string[] requiresAccessTokens;
            public string[] requiresCompletedNodes;
        }

        [Serializable]
        public sealed class ProgressionGraphSnapshot
        {
            public string version;
            public ProgressionNodeSnapshot[] nodes;
            public ProgressionEdgeSnapshot[] edges;
            public ProgressionRootGateSnapshot rootGate;
            public ProgressionRootGateSnapshot rootSystemGate;
        }

        [Serializable]
        public sealed class GenerationRepairSnapshot
        {
            public string type;
            public string pointId;
            public string slotId;
            public string areaId;
            public string targetId;
            public string description;
            public PositionSnapshot position;
        }

        [Serializable]
        public sealed class GenerationRepairPolicySnapshot
        {
            public string mode;
            public int affectedRegionRegenerationLimit;
        }

        [Serializable]
        public sealed class GenerationCountsSnapshot
        {
            public int areas;
            public int routes;
            public int loopRoutes;
            public int points;
            public int pois;
            public int slots;
        }

        [Serializable]
        public sealed class GenerationBiomeCoverageSnapshot
        {
            public string biomeId;
            public int count;
            public string[] poiIds;
        }

        [Serializable]
        public sealed class GenerationEncounterMinimumSnapshot
        {
            public int width;
            public int height;
        }

        [Serializable]
        public sealed class GenerationChecksSnapshot
        {
            public bool progressionAcyclic;
            public bool rootReachableBeforeGate;
            public bool rootReachableAfterGate;
            public int nonRootTileCountBeforeGate;
            public int connectedTileCount;
            public GenerationBiomeCoverageSnapshot[] biomePoiCoverage;
            public GenerationEncounterMinimumSnapshot encounterMinimum;
        }

        [Serializable]
        public sealed class GenerationConfigurationSnapshot
        {
            public int areaCount;
            public int biomeAnchorCountPerBiome;
            public int boundaryNoiseScale;
            public int boundaryNoiseAmplitude;
            public int[] terrainNoiseScales;
            public int[] majorRoadWidths;
            public int minorRoadWidth;
            public int secretRoadWidth;
            public GenerationEncounterMinimumSnapshot poiClearing;
        }

        [Serializable]
        public sealed class GenerationTurnSimulationSnapshot
        {
            public bool performed;
            public string reason;
        }

        [Serializable]
        public sealed class GenerationReportSnapshot
        {
            public string status;
            public string pipeline;
            public string pipelineVersion;
            public string generatorVersion;
            public bool deterministic;
            public int attempts;
            public GenerationRepairPolicySnapshot repairPolicy;
            public GenerationRepairSnapshot[] repairs;
            public GenerationConfigurationSnapshot configuration;
            public GenerationCountsSnapshot counts;
            public GenerationChecksSnapshot checks;
            public GenerationTurnSimulationSnapshot turnSimulation;
        }

        [Serializable]
        public sealed class StageReachabilitySnapshot
        {
            public bool rootReachableBeforeGate;
            public int nonRootAreaCountBeforeGate;
            public int nonRootTileCountBeforeGate;
            public bool rootReachableAfterGate;
            public int areaCountAfterGate;
        }

        [Serializable]
        public sealed class ProgressionCandidateCountsSnapshot
        {
            public int admin;
            public int truth;
            public int root;
        }

        [Serializable]
        public sealed class WorldValidationSnapshot
        {
            public int connectedTileCount;
            public int pointCount;
            public int poiCount;
            public int areaCount;
            public int slotCount;
            public int routeCount;
            public int loopRouteCount;
            public ProgressionCandidateCountsSnapshot candidateCounts;
            public GenerationBiomeCoverageSnapshot[] biomePoiCoverage;
            public PositionSnapshot rootInteractionAnchor;
            public int rootMaxInteractionDistance;
            public bool progressionAcyclic;
            public string[] progressionOrder;
            public StageReachabilitySnapshot stageReachability;
            public GenerationEncounterMinimumSnapshot encounterMinimum;
            public int[] turnWindowContract;
            public bool turnSimulationPerformed;
            public bool safeNavigationConsumesCampaignTurn;
        }

        [Serializable]
        public sealed class WorldSnapshot
        {
            public string worldId;
            public string generatorVersion;
            public string worldName;
            public string worldNameKo;
            public long worldSeed;
            public int width;
            public int height;
            public string layoutHash;
            public string[] tileLegend;
            public string[] areaMapLegend;
            public string[] biomeMapLegend;
            public BiomeSnapshot[] biomes;
            public CampaignRegionRoleSnapshot[] campaignRegionRoles;
            public ProgressionGraphSnapshot progressionGraph;
            public AreaSnapshot[] areas;
            public WorldRouteSnapshot[] routes;
            public PlacementSlotSnapshot[] placementSlots;
            public PointSnapshot[] points;
            public PointSnapshot[] pois;
            public GenerationReportSnapshot generationReport;
            public WorldValidationSnapshot validation;
            public string geometryPolicy;
            public string llmGeometryAccess;

            [NonSerialized] public int[] tileCodes;
            [NonSerialized] public int[] areaCodes;
            [NonSerialized] public int[] biomeCodes;

            public bool HasCompleteLayout => width > 0 && height > 0 && tileCodes != null && tileCodes.Length == width * height;
            public bool HasCompleteAreaMap => width > 0 && height > 0 && areaCodes != null &&
                                              areaCodes.Length == width * height && areaMapLegend != null;
            public bool HasCompleteBiomeMap => width > 0 && height > 0 && biomeCodes != null &&
                                               biomeCodes.Length == width * height && biomeMapLegend != null;

            public string AreaIdAt(int x, int y)
            {
                if (!HasCompleteAreaMap || x < 0 || y < 0 || x >= width || y >= height)
                    return null;
                int code = areaCodes[y * width + x];
                return code >= 0 && code < areaMapLegend.Length ? areaMapLegend[code] : null;
            }

            public string BiomeIdAt(int x, int y)
            {
                if (!HasCompleteBiomeMap || x < 0 || y < 0 || x >= width || y >= height)
                    return null;
                int code = biomeCodes[y * width + x];
                return code >= 0 && code < biomeMapLegend.Length ? biomeMapLegend[code] : null;
            }
        }

        [Serializable]
        public sealed class CampaignSnapshot
        {
            public string id;
            public string title;
            public string worldId;
            public string worldName;
            public string protagonistId;
            public string protagonistName;
            public string artifactId;
            public string generatedTitle;
            public string generatedTitleKo;
            public long worldSeed;
            public int turnLimit;
            public string status;
            public string archetype;
            public string premise;
            public string premiseKo;
            public string templateId;
            public StoryBeatSnapshot[] requiredStoryBeats;
            public MacroPhaseSnapshot[] campaignMacroPhases;
            public string[] endingCandidates;
            public EndingCandidateSnapshot[] endingCandidateDetails;
            public EndingConditionReportSnapshot[] endingConditionReports;
            public WorldSnapshot world;
        }

        [Serializable]
        public sealed class PositionSnapshot
        {
            public int x;
            public int y;
        }

        [Serializable]
        public sealed class EntityStateSnapshot
        {
            public int hp;
            public int maxHp;
            public string role;
            public string npcRole;
            public string mood;
            public string slotId;
            public bool temporary;
            public bool disabled;
            public bool defeated;
            public bool fled;
            public bool boss;
            public int speed;
            public string factionId;
            public string goal;
            public string motivation;
            public string canonicalNpcId;
            public string[] traits;
            public string[] roleTags;
            public string rootComponent;
            public bool gated;
            public string campaignRole;
            public string reservedFor;
            public string evidenceKey;
            public bool designatedCampaignEvidence;
            public string fixtureType;
            public string requiredSkillId;
            public string finaleComponent;
            public string candidateId;
            public string adminAccessLevelId;
            public bool adminAccessResolved;
        }

        [Serializable]
        public sealed class EntitySnapshot
        {
            public string id;
            public string kind;
            public string assetId;
            public string name;
            public PositionSnapshot position;
            public EntityStateSnapshot state;
            public bool blocking;
            public bool @protected;
            public bool cloneable;
            public EntityCapabilitySnapshot capabilities;
        }

        [Serializable]
        public sealed class EntityCapabilitySnapshot
        {
            public bool canCopy;
            public bool canDelete;
            public bool canConnect;
            public bool canRestore;
            public bool canInteract;
            public int requiredAdminAccess;
            public bool grantsDefeatReward;
        }

        [Serializable]
        public sealed class InventoryItemSnapshot
        {
            public string id;
            public string kind;
            public string name;
            public string description;
            public int quantity;
            public string effect;
            public int effectValue;
            public bool @protected;
            public int acquiredTurn;
            public string source;
        }

        [Serializable]
        public sealed class QuestSnapshot
        {
            public string id;
            public string key;
            public string title;
            public string summary;
            public string status;
            public string questKind;
            public string currentStep;
            public bool acceptsNewSteps;
            public int createdTurn;
        }

        [Serializable]
        public sealed class FactSnapshot
        {
            public string id;
            public string subject;
            public string predicate;
            public string value;
            public string type;
            public int establishedTurn;
        }

        [Serializable]
        public sealed class OpenLoopSnapshot
        {
            public string id;
            public string summary;
            public string status;
            public int createdTurn;
            public int expiresTurn;
            public string source;
        }

        [Serializable]
        public sealed class RumorSnapshot
        {
            public string id;
            public string summary;
            public float reliability;
            public string status;
            public int firstHeardTurn;
            public int expiresTurn;
        }

        [Serializable]
        public sealed class EndingCandidateSnapshot
        {
            public string id;
            public string category;
            public string title;
            public string description;
            public string valence;
            public bool eligible;
        }

        [Serializable]
        public sealed class EndingConditionSnapshot
        {
            public string label;
            public bool satisfied;
        }

        [Serializable]
        public sealed class EndingConditionReportSnapshot
        {
            public string id;
            public string title;
            public bool eligible;
            public int satisfiedCount;
            public int totalCount;
            public EndingConditionSnapshot[] conditions;
        }

        [Serializable]
        public sealed class NpcRelationshipSnapshot
        {
            public string npcId;
            public string npcName;
            public int affinity;
            public int trust;
            public int fear;
            public string stance;
            public int lastChangedTurn;
            public int score;
            public string label;
            public string reason;
        }

        [Serializable]
        public sealed class NpcMemorySnapshot
        {
            public string id;
            public string npcId;
            public string npcName;
            public string summary;
            public string memory;
            public int importance;
            public int ttlTurns;
            public int createdTurn;
            public int turnNo;
        }

        [Serializable]
        public sealed class StoryBeatSnapshot
        {
            public string id;
            public string title;
            public string description;
            public string phaseId;
            public string macroPhaseId;
            public string requiredAbility;
            public string[] allowedAbilities;
            public string requiredCampaignRole;
            public string requiredEvidenceKey;
            public string requiredEvidencePolicy;
            public int targetTurn;
            public string status;
            public string act;
        }

        [Serializable]
        public sealed class RestoreCandidateSnapshot
        {
            public string entityId;
            public string targetEntityId;
            public string id;
            public string entityName;
            public string displayName;
            public string name;
            public string kind;
            public string reason;
            public PositionSnapshot position;
            public PositionSnapshot lastKnownPosition;
            public int sourceTurn;
            public int capturedTurn;
            public int recordedTurn;
            public int turnNo;
            public int expiresTurn;
        }

        [Serializable]
        public sealed class CampaignMetricsSnapshot
        {
            public int worldStability;
            public int worldAutonomy;
            public int publicTrust;
            public int technicalDebt;
            public int companionBond;
            public int turnPressure;
        }

        [Serializable]
        public sealed class TechnicalDebtEntrySnapshot
        {
            public string id;
            public int turnNo;
            public string skillId;
            public string operationType;
            public string targetId;
            public bool forcedOverride;
            public int debtDelta;
            public string deferredConsequenceType;
            public string resolvedAt;
            public int resolvedTurn;
        }

        [Serializable]
        public sealed class AdminAccessHistorySnapshot
        {
            public string accessLevelId;
            public int level;
            public string regionAxis;
            public string actionContext;
            public string skillId;
            public int turnNo;
        }

        [Serializable]
        public sealed class AccessTokenSnapshot
        {
            public string id;
            public string name;
            public string nameKo;
            public string description;
            public int level;
            public int adminLevel;
            public int progressLevel;
            public string sourceRole;
        }

        [Serializable]
        public sealed class RootGateSnapshot
        {
            public bool eligible;
            public int requiredAdminLevel;
            public string[] missingAccessTokens;
        }

        [Serializable]
        public sealed class RootResolutionSnapshot
        {
            public bool resolved;
            public int turnNo;
            public string endingId;
            public string endingCategory;
            public string rootSlotId;
            public string rootPoiId;
            public string areaId;
            public PositionSnapshot position;
            public int adminLevel;
            public string[] accessTokens;
            public bool geometryChanged;
            public string resolutionMode;
            public string[] puzzleEvidence;
        }

        [Serializable]
        public sealed class RootComponentIdsSnapshot
        {
            public string access;
            public string stabilizer;
            public string backup;
            public string autonomy_core;
            public string error_core;
            public string return_device;
            public string survivor_record;
        }

        [Serializable]
        public sealed class RootPuzzleSnapshot
        {
            public string status;
            public RootComponentIdsSnapshot componentEntityIds;
            public string[] evidence;
            public string matchedEndingId;
            public string resolutionMode;
            public string[] availableEndingIds;
        }

        [Serializable]
        public sealed class ActiveEncounterSnapshot
        {
            public string id;
            public string status;
            public string reason;
            public string kind;
            public string title;
            public string description;
            public string areaId;
            public string biomeId;
            public string campaignRole;
            public string sourceEntityId;
            public PositionSnapshot stagingPosition;
            public PositionSnapshot triggerPosition;
            public PositionSnapshot requestedDestination;
            public PositionSnapshot position;
            public PositionSnapshot destination;
            public string[] suggestedActions;
            public string[] suggestedActionContexts;
            public string[] suggestedSkillIds;
            public int openedNavigationSequence;
            public int campaignTurnOpened;
            public string openedAt;
            public int resolvedTurn;
            public string resolutionAction;
            public string resolutionOutcome;
            public bool campaignTurnConsumed;
        }

        [Serializable]
        public sealed class RunSnapshot
        {
            public string id;
            public string campaignId;
            public string campaignTitle;
            public string worldId;
            public string worldName;
            public string protagonistId;
            public string protagonistName;
            public string artifactId;
            public string archetype;
            public string premise;
            public string status;
            public long version;
            public int currentTurn;
            public int turnLimit;
            public int remainingTurns;
            public string currentAct;
            public string campaignPhase;
            public string currentBeat;
            public StoryBeatSnapshot currentStoryBeat;
            public ArcQuestionSnapshot currentArcQuestion;
            public ArcQuestionSnapshot[] arcQuestions;
            public MacroPhaseSnapshot currentMacroPhase;
            public MacroPhaseSnapshot[] campaignMacroPhases;
            public DirectorStateSnapshot directorState;
            public int health;
            public int maxHealth;
            public int focus;
            public int maxFocus;
            public int experience;
            public int gold;
            public InventoryItemSnapshot[] inventory;
            public int pressure;
            public bool exposed;
            public string endingCode;
            public RootResolutionSnapshot rootResolution;
            public int adminLevel;
            public int progressLevel;
            public string[] accessTokens;
            public string[] progressTokens;
            public AccessTokenSnapshot[] accessTokenDefinitions;
            public CampaignMetricsSnapshot metrics;
            public int navigationSequence;
            public int safeTravelCount;
            public int travelTime;
            public int travelTimeUnits;
            public int travelDistance;
            public string[] visitedPoiIds;
            public string[] discoveredAreaIds;
            public RootGateSnapshot rootGate;
            public RootGateSnapshot rootSystemGate;
            public RootGateSnapshot finaleGate;
            public ActiveEncounterSnapshot activeEncounter;
            public ActiveEncounterSnapshot[] encounterHistory;
            public RootPuzzleSnapshot rootPuzzle;
            public string[] endingCandidates;
            public EndingCandidateSnapshot[] endingCandidateDetails;
            public EndingConditionReportSnapshot[] endingConditionReports;
            public string playerEntityId;
            public EntitySnapshot[] entities;
            public WorldSnapshot world;
            public QuestSnapshot[] activeQuests;
            public FactSnapshot[] canonicalFacts;
            public OpenLoopSnapshot[] openLoops;
            public RumorSnapshot[] rumors;
            public NpcRelationshipSnapshot[] npcRelationships;
            public NpcMemorySnapshot[] npcMemories;
            public RestoreCandidateSnapshot[] restoreCandidates;
            public TechnicalDebtEntrySnapshot[] technicalDebtEntries;
            public AdminAccessHistorySnapshot[] adminAccessHistory;
            public string[] majorChoices;
            public string[] regionOutcomes;
            public string[] abilityUsageHistory;
            public string[] unresolvedHooks;
            // Recovery source for turn-zero, save/load, and a client reconnect between
            // the scene response and the player's next sealed narrative choice.
            public NextInterventionSnapshot pendingChoiceSet;
            public NarrativeSnapshot openingNarrative;
        }

        [Serializable]
        public sealed class PreparedD20Snapshot
        {
            public int d20;
            public long expectedRunVersion;
        }

        [Serializable]
        public sealed class DiceModifierSnapshot
        {
            public string source;
            public int value;
        }

        [Serializable]
        public sealed class DiceSnapshot
        {
            public int raw;
            public int modifier;
            public DiceModifierSnapshot[] modifiers;
            public int difficulty;
            public int mechanicalScore;
            public float intentAlignment;
            public string outcomeExplanation;
        }

        [Serializable]
        public sealed class EventSnapshot
        {
            public string type;
            public string text;
            public string entityId;
            public string actorId;
            public string sourceEntityId;
            public string targetEntityId;
            public string rewardId;
            public string encounterId;
            public string fact;
            public string endingCode;
            public string resource;
            public string npcId;
            public string npcName;
            public string line;
            public string concern;
            public string motivation;
            public string clueId;
            public string clueTitle;
            public string clueContent;
            public string clueMeaning;
            public string storyConnection;
            public string nextObjective;
            public string nextTargetId;
            public string nextTargetName;
            public string title;
            public string description;
            public string discoveryType;
            public string intensity;
            public string outcomeId;
            public string summary;
            public int delta;
            public int reward;
            public int trust;
            public int fear;
            public int trustDelta;
            public int fearDelta;
            public bool repeat;
            public int turnNo;
            public PositionSnapshot from;
            public PositionSnapshot to;
            public PositionSnapshot[] path;
            public string facing;
            public bool arrived;
            public PositionSnapshot position;
        }

        [Serializable]
        public sealed class ArcQuestionSnapshot
        {
            public string id;
            public int order;
            public int startTurn;
            public int deadlineTurn;
            public string question;
            public string status;
        }

        [Serializable]
        public sealed class MacroPhaseSnapshot
        {
            public string id;
            public int order;
            public string name;
            public string nameKo;
            public string purpose;
        }

        [Serializable]
        public sealed class DirectorStateSnapshot
        {
            public int decisionNo;
            public string[] recentSceneTypes;
            public string[] discoveredSecrets;
            public string[] runTraits;
            public SpecialSkillSnapshot[] specialSkills;
            public MonsterVariantSnapshot[] generatedMonsterVariants;
        }

        [Serializable]
        public sealed class SpecialSkillSnapshot
        {
            public string id;
            public string templateId;
            public string baseSkill;
            public string name;
            public string[] modifierIds;
            public int charges;
            public int maxCharges;
            public string sourceNpcId;
            public int acquiredTurn;
            public int acquiredDecisionNo;
        }

        [Serializable]
        public sealed class MonsterVariantSnapshot
        {
            public string entityId;
            public string assetId;
            public string name;
            public string[] traits;
            public bool boss;
        }

        [Serializable]
        public sealed class SceneActionSnapshot
        {
            public string actionId;
            public int sequence;
            public string type;
            public string actorId;
            public string targetId;
            public string speakerId;
            public string actionStyle;
            public string rewardId;
            public string line;
            public string text;
            public int roll;
            public bool hit;
            public int damage;
            public int initiative;
            public int initiativeRoll;
            public PositionSnapshot from;
            public PositionSnapshot to;
        }

        [Serializable]
        public sealed class RejectedSceneActionSnapshot
        {
            public string actionId;
            public string reason;
        }

        [Serializable]
        public sealed class SceneDecisionSnapshot
        {
            public int decisionNo;
            public string decisionType;
            public string sceneGoal;
            public SceneActionSnapshot[] sceneSequence;
            public EventSnapshot[] events;
            public string[] appliedActionIds;
            public RejectedSceneActionSnapshot[] rejectedActions;
            public bool fallbackUsed;
            public string model;
        }

        [Serializable]
        public sealed class ProposedOperationSnapshot
        {
            public string op;
            public string type;
            public string text;
            public string summary;
            public string reason;
            public string entityId;
            public string targetId;
            public string key;
            public string value;
            public string questTemplateId;
            public string slotId;
            public string assetId;
            public int delta;
            public int ttlTurns;
            public float importance;
            public int cost;
        }

        [Serializable]
        public sealed class NarrativeSnapshot
        {
            public string summary;
            public string body;
            public string[] dialogue;
            public DialogueSnapshot[] dialogueDetails;
            public StorySequenceSnapshot[] storySequence;
            public NextInterventionSnapshot nextIntervention;
            public string elementalEffectId;
            public ProposedOperationSnapshot[] proposedOps;
            public ProposedOperationSnapshot[] appliedOps;
            public ProposedOperationSnapshot[] rejectedOps;
            public bool fallbackUsed;
            public string model;
        }

        [Serializable]
        public sealed class StorySequenceSnapshot
        {
            public string type;
            public string speakerId;
            public string actionId;
            public string text;
        }

        [Serializable]
        public sealed class NarrativeChoiceSnapshot
        {
            public string choiceId;
            public string text;
            public string choiceKind;
            public string intentTag;
            public string skillId;
            public string targetEntityId;
            public string destinationRef;
            public string resolutionMode;
        }

        [Serializable]
        public sealed class NextInterventionSnapshot
        {
            public string choiceSetId;
            public string reason;
            public NarrativeChoiceSnapshot[] choices;
            public string[] suggestedSkillIds;
        }

        [Serializable]
        public sealed class DialogueSnapshot
        {
            public string speakerId;
            public string line;
        }

        [Serializable]
        public sealed class StateDeltaSnapshot
        {
            public EventSnapshot[] events;
            public FactSnapshot[] facts;
            public RumorSnapshot[] rumors;
            public OpenLoopSnapshot[] openLoops;
            public NpcMemorySnapshot[] npcMemories;
            public NpcRelationshipSnapshot[] relationships;
            public QuestSnapshot[] quests;
            public ProposedOperationSnapshot[] appliedOps;
            public ProposedOperationSnapshot[] rejectedOps;
            public SceneDecisionSnapshot sceneDecision;
        }

        [Serializable]
        public sealed class TurnRequestSnapshot
        {
            public string inputType;
            public string skillId;
            public string ability;
            public string targetEntityId;
            public string secondaryTargetEntityId;
            public PositionSnapshot destination;
        }

        [Serializable]
        public sealed class RuntimeEntityRefSnapshot
        {
            public string id;
            public string entityType;
        }

        [Serializable]
        public sealed class GameplayFxSnapshot
        {
            public string scaleTier;
            public string element;
            public string effectId;
        }

        [Serializable]
        public sealed class GameplayConnectionSnapshot
        {
            public string id;
            public RuntimeEntityRefSnapshot from;
            public RuntimeEntityRefSnapshot to;
            public string relation;
            public int expiresTurn;
        }

        [Serializable]
        public sealed class GameplayResultDetailSnapshot
        {
            public RuntimeEntityRefSnapshot target;
            public RuntimeEntityRefSnapshot actor;
            public RuntimeEntityRefSnapshot clone;
            public RuntimeEntityRefSnapshot item;
            public RuntimeEntityRefSnapshot createdItem;
            public RuntimeEntityRefSnapshot[] targetRefs;
            public RuntimeEntityRefSnapshot[] targets;
            public RuntimeEntityRefSnapshot[] sources;
            public RuntimeEntityRefSnapshot[] affectedTargets;
            public GameplayConnectionSnapshot[] connections;
            public string lineageRootId;
            public string rejectionReason;
            public string resolution;
            public string restorationDegree;
            public string discoveryType;
            public string informationTitle;
            public string[] restoredFields;
            public string[] revealedEvidenceIds;
            public int[] sourceTurns;
            public int sourceSnapshotTurn;
            public int compensatedTurns;
            public int damage;
            public int radius;
            public int affectedCount;
            public int quantity;
            public float range;
            public float speed;
            public bool hit;
            public bool destroyed;
            public bool moved;
            public bool copyLocked;
            public bool runTurnRewound;
            public bool alreadyInvestigated;
            public bool newInformation;
            public bool consumed;
            public PositionSnapshot from;
            public PositionSnapshot to;
            public PositionSnapshot[] path;
            public string facing;
            public bool arrived;
        }

        [Serializable]
        public sealed class GameplayResultSnapshot
        {
            public string schemaVersion;
            public string actionType;
            public string context;
            public string outcome;
            public bool succeeded;
            public string rollId;
            public GameplayFxSnapshot fx;
            public GameplayResultDetailSnapshot result;
        }

        [Serializable]
        public sealed class RuntimeRollSnapshot
        {
            public string rollId;
            public int d20;
            public int modifier;
            public int total;
            public int mechanicalScore;
            public string resultTier;
        }

        [Serializable]
        public sealed class RuntimeResolutionSnapshot
        {
            public bool required;
            public RuntimeRollSnapshot roll;
            public EventSnapshot[] healthChanges;
            public EventSnapshot[] inventoryChanges;
            public EventSnapshot[] skillChanges;
            public EventSnapshot[] statusChanges;
            public EventSnapshot[] movementChanges;
            public EventSnapshot[] relationshipChanges;
            public EventSnapshot[] worldChanges;
            public EventSnapshot[] confirmedEffects;
        }

        [Serializable]
        public sealed class RuntimeUnityEventPayloadSnapshot
        {
            public string skillId;
            public string intensity;
            public string effectId;
            public string element;
            public string fxScaleTier;
            public GameplayResultSnapshot gameplayResult;
            public EventSnapshot[] confirmedEvents;
        }

        [Serializable]
        public sealed class RuntimeUnityEventSnapshot
        {
            public string eventId;
            public string type;
            public string actorId;
            public string[] targetIds;
            public string resultTier;
            public int sequence;
            public RuntimeUnityEventPayloadSnapshot payload;
        }

        [Serializable]
        public sealed class RuntimeUnitySnapshot
        {
            public bool renderRequired;
            public RuntimeUnityEventSnapshot[] events;
        }

        [Serializable]
        public sealed class RuntimeTurnSnapshot
        {
            public int turnNo;
            public string intent;
            public string model;
            public bool fallbackUsed;
        }

        [Serializable]
        public sealed class RuntimeNarrativeSnapshot
        {
            public StorySequenceSnapshot[] storySequence;
            public NextInterventionSnapshot nextIntervention;
        }

        [Serializable]
        public sealed class RuntimeSpatialPlayerSnapshot
        {
            public string entityId;
            public PositionSnapshot position;
            public string facing;
        }

        [Serializable]
        public sealed class RuntimeSpatialMovementSnapshot
        {
            public PositionSnapshot from;
            public PositionSnapshot to;
            public PositionSnapshot[] path;
            public string facing;
            public bool arrived;
        }

        [Serializable]
        public sealed class RuntimeVisibilitySnapshot
        {
            public string entityId;
            public PositionSnapshot position;
            public int distance;
            public string direction;
            public bool visible;
        }

        [Serializable]
        public sealed class RuntimeSpatialSnapshot
        {
            public bool authoritative;
            public string areaId;
            public string biomeId;
            public RuntimeSpatialPlayerSnapshot player;
            public RuntimeSpatialMovementSnapshot movement;
            public RuntimeVisibilitySnapshot[] visibility;
            public ActiveEncounterSnapshot activeEncounter;
        }

        [Serializable]
        public sealed class RuntimeSnapshot
        {
            public RuntimeTurnSnapshot turn;
            public RuntimeNarrativeSnapshot narrative;
            public RuntimeResolutionSnapshot resolution;
            public GameplayResultSnapshot gameplayResult;
            public RuntimeSpatialSnapshot spatial;
            public RuntimeUnitySnapshot unity;
            public long runVersion;
        }

        [Serializable]
        public sealed class TurnSnapshot
        {
            public string id;
            public string runId;
            public int turnNo;
            public string normalizedAttempt;
            public string inputType;
            public string skillId;
            public string actionContext;
            public string[] targetIds;
            public TurnRequestSnapshot request;
            public DiceSnapshot dice;
            public int mechanicalScore;
            public string outcome;
            public bool consumesCampaignTurn;
            public bool campaignTurnConsumed;
            public int consequenceBudget;
            public StateDeltaSnapshot stateDelta;
            public EventSnapshot[] events;
            public NarrativeSnapshot narrative;
            public SceneDecisionSnapshot sceneDecision;
            public SceneActionSnapshot[] sceneSequence;
            public RuntimeSnapshot runtime;
            public string createdAt;
        }

        [Serializable]
        public sealed class NavigationSnapshot
        {
            public string id;
            public string runId;
            public int sequence;
            public long expectedRunVersion;
            public long committedRunVersion;
            public PositionSnapshot from;
            public PositionSnapshot to;
            public PositionSnapshot requestedDestination;
            public PositionSnapshot[] path;
            public int pathCost;
            public int travelTimeUnits;
            public int cumulativeTravelTimeUnits;
            public string enteredAreaId;
            public string enteredBiomeId;
            public string campaignRole;
            public string[] traversedAreaIds;
            public string[] reachedPoiIds;
            public bool campaignTurnConsumed;
            public int campaignTurnBefore;
            public int campaignTurnAfter;
            public string layoutHash;
            public string createdAt;
            public bool encounterOpened;
            public ActiveEncounterSnapshot encounter;
            public SceneDecisionSnapshot sceneDecision;
            public SceneActionSnapshot[] sceneSequence;
            public EventSnapshot[] events;
            public NarrativeSnapshot narrative;
            // Compatibility fields for older development-server payloads.
            public string encounterReason;
            public PositionSnapshot stagingPosition;
            public ActiveEncounterSnapshot activeEncounter;
        }

        [Serializable]
        private sealed class HealthEnvelope
        {
            public string status;
            public bool authoritativeTurns;
        }

        [Serializable]
        private sealed class CampaignEnvelope
        {
            public CampaignSnapshot campaign;
        }

        [Serializable]
        private sealed class RunEnvelope
        {
            public RunSnapshot run;
        }

        [Serializable]
        private sealed class AmbientWanderEnvelope
        {
            public RunSnapshot run;
            public string[] movedEntityIds;
        }

        [Serializable]
        private sealed class PreparedD20Envelope
        {
            public int d20;
            public long expectedRunVersion;
        }

        [Serializable]
        private sealed class TurnEnvelope
        {
            public TurnSnapshot turn;
            public RunSnapshot run;
            public bool fromIdempotencyCache;
        }

        [Serializable]
        private sealed class TurnOnlyEnvelope
        {
            public TurnSnapshot turn;
        }

        [Serializable]
        private sealed class NavigationEnvelope
        {
            public NavigationSnapshot navigation;
            public RunSnapshot run;
            public bool fromIdempotencyCache;
        }

        [Serializable]
        private sealed class ErrorDetail
        {
            public long currentVersion;
            public string reason;
            public bool campaignTurnConsumed;
            public string[] suggestedActions;
            public PositionSnapshot stopPosition;
            public PositionSnapshot encounterPosition;
        }

        [Serializable]
        private sealed class ErrorBody
        {
            public string code;
            public string message;
            public ErrorDetail details;
        }

        [Serializable]
        private sealed class ErrorEnvelope
        {
            public ErrorBody error;
        }

        public sealed class Result<T>
        {
            public bool IsSuccess { get; }
            public long StatusCode { get; }
            public T Value { get; }
            public string ErrorCode { get; }
            public string ErrorMessage { get; }
            public long CurrentVersion { get; }
            public string ErrorReason { get; }
            public bool CampaignTurnConsumed { get; }
            public string[] SuggestedActions { get; }
            public PositionSnapshot StopPosition { get; }

            private Result(bool success, long statusCode, T value, string errorCode, string errorMessage, long currentVersion,
                string errorReason, bool campaignTurnConsumed, string[] suggestedActions, PositionSnapshot stopPosition)
            {
                IsSuccess = success;
                StatusCode = statusCode;
                Value = value;
                ErrorCode = errorCode;
                ErrorMessage = errorMessage;
                CurrentVersion = currentVersion;
                ErrorReason = errorReason;
                CampaignTurnConsumed = campaignTurnConsumed;
                SuggestedActions = suggestedActions ?? Array.Empty<string>();
                StopPosition = stopPosition;
            }

            internal static Result<T> Success(long statusCode, T value)
                => new Result<T>(true, statusCode, value, null, null, 0, null, false, null, null);

            internal static Result<T> Failure(long statusCode, string code, string message, long currentVersion = 0,
                string errorReason = null, bool campaignTurnConsumed = false, string[] suggestedActions = null,
                PositionSnapshot stopPosition = null)
                => new Result<T>(false, statusCode, default(T), code, message, currentVersion, errorReason,
                    campaignTurnConsumed, suggestedActions, stopPosition);
        }

        public sealed class CommittedTurn
        {
            public TurnSnapshot Turn { get; }
            public RunSnapshot Run { get; }
            public bool FromIdempotencyCache { get; }

            internal CommittedTurn(TurnSnapshot turn, RunSnapshot run, bool fromIdempotencyCache)
            {
                Turn = turn;
                Run = run;
                FromIdempotencyCache = fromIdempotencyCache;
            }
        }

        public sealed class CommittedNavigation
        {
            public NavigationSnapshot Navigation { get; }
            public RunSnapshot Run { get; }
            public bool FromIdempotencyCache { get; }

            internal CommittedNavigation(NavigationSnapshot navigation, RunSnapshot run, bool fromIdempotencyCache)
            {
                Navigation = navigation;
                Run = run;
                FromIdempotencyCache = fromIdempotencyCache;
            }
        }

        private readonly string _baseUrl;
        private readonly string _userId;

        public GameApiClient(string baseUrl = null, string userId = null)
        {
            _baseUrl = KeyboardWandererEndpointResolver.ResolveBaseUrl(baseUrl, DefaultBaseUrl);
            _userId = string.IsNullOrWhiteSpace(userId) ? null : userId.Trim();
        }

        public IEnumerator CheckHealth(Action<Result<bool>> completed)
        {
            yield return Send("GET", "/health", null, raw =>
            {
                if (!raw.Success)
                {
                    completed?.Invoke(Result<bool>.Failure(raw.StatusCode, raw.ErrorCode, raw.ErrorMessage));
                    return;
                }
                HealthEnvelope envelope = SafeParse<HealthEnvelope>(raw.Json);
                bool authoritative = envelope != null && envelope.status == "ok" && envelope.authoritativeTurns;
                completed?.Invoke(authoritative
                    ? Result<bool>.Success(raw.StatusCode, true)
                    : Result<bool>.Failure(raw.StatusCode, "SERVER_NOT_AUTHORITATIVE", "서버가 권위 턴 모드가 아닙니다."));
            });
        }

        public IEnumerator CreateCampaign(long worldSeed, int turnLimit, Action<Result<CampaignSnapshot>> completed)
        {
            yield return CreateCampaign(worldSeed, turnLimit, null, completed);
        }

        public IEnumerator CreateCampaign(long worldSeed, int turnLimit, string idempotencyKey,
            Action<Result<CampaignSnapshot>> completed)
        {
            if (!IsValidOptionalIdempotencyKey(idempotencyKey))
            {
                completed?.Invoke(Result<CampaignSnapshot>.Failure(0, "INVALID_IDEMPOTENCY_KEY",
                    "Idempotency-Key must contain 8-128 characters."));
                yield break;
            }
            string body = "{\"worldSeed\":" + worldSeed.ToString(CultureInfo.InvariantCulture) +
                          ",\"turnLimit\":" + turnLimit.ToString(CultureInfo.InvariantCulture) + "}";
            yield return Send("POST", "/v1/campaigns", body, raw =>
            {
                CampaignEnvelope envelope = raw.Success ? SafeParse<CampaignEnvelope>(raw.Json) : null;
                if (envelope?.campaign == null)
                {
                    completed?.Invoke(Result<CampaignSnapshot>.Failure(raw.StatusCode, raw.ErrorCode,
                        raw.Success ? "캠페인 응답을 해석할 수 없습니다." : raw.ErrorMessage));
                    return;
                }
                PopulateTileCodes(envelope.campaign.world, raw.Json);
                completed?.Invoke(Result<CampaignSnapshot>.Success(raw.StatusCode, envelope.campaign));
            }, idempotencyKey);
        }

        public IEnumerator CreateRun(string campaignId, Action<Result<RunSnapshot>> completed)
        {
            yield return CreateRun(campaignId, null, completed);
        }

        public IEnumerator CreateRun(string campaignId, string idempotencyKey,
            Action<Result<RunSnapshot>> completed)
        {
            if (!IsValidOptionalIdempotencyKey(idempotencyKey))
            {
                completed?.Invoke(Result<RunSnapshot>.Failure(0, "INVALID_IDEMPOTENCY_KEY",
                    "Idempotency-Key must contain 8-128 characters."));
                yield break;
            }
            yield return Send("POST", "/v1/campaigns/" + EscapePath(campaignId) + "/runs", "{}", raw =>
            {
                RunEnvelope envelope = raw.Success ? SafeParse<RunEnvelope>(raw.Json) : null;
                if (envelope?.run == null)
                {
                    completed?.Invoke(Result<RunSnapshot>.Failure(raw.StatusCode, raw.ErrorCode,
                        raw.Success ? "런 응답을 해석할 수 없습니다." : raw.ErrorMessage));
                    return;
                }
                PopulateTileCodes(envelope.run.world, raw.Json);
                completed?.Invoke(Result<RunSnapshot>.Success(raw.StatusCode, envelope.run));
            }, idempotencyKey);
        }

        public IEnumerator GetRun(string runId, Action<Result<RunSnapshot>> completed)
        {
            yield return Send("GET", "/v1/runs/" + EscapePath(runId), null, raw =>
            {
                RunEnvelope envelope = raw.Success ? SafeParse<RunEnvelope>(raw.Json) : null;
                if (envelope?.run == null)
                {
                    completed?.Invoke(Result<RunSnapshot>.Failure(raw.StatusCode, raw.ErrorCode,
                        raw.Success ? "런 상태를 해석할 수 없습니다." : raw.ErrorMessage, raw.CurrentVersion));
                    return;
                }
                PopulateTileCodes(envelope.run.world, raw.Json);
                completed?.Invoke(Result<RunSnapshot>.Success(raw.StatusCode, envelope.run));
            });
        }

        public IEnumerator AdvanceAmbientWander(string runId, long expectedVersion,
            int minX, int minY, int maxX, int maxY, Action<Result<RunSnapshot>> completed)
        {
            string body = "{\"expectedRunVersion\":" + expectedVersion.ToString(CultureInfo.InvariantCulture) +
                          ",\"minX\":" + minX.ToString(CultureInfo.InvariantCulture) +
                          ",\"minY\":" + minY.ToString(CultureInfo.InvariantCulture) +
                          ",\"maxX\":" + maxX.ToString(CultureInfo.InvariantCulture) +
                          ",\"maxY\":" + maxY.ToString(CultureInfo.InvariantCulture) + "}";
            yield return Send("POST", "/v1/runs/" + EscapePath(runId) + "/ambient-wander", body, raw =>
            {
                AmbientWanderEnvelope envelope = raw.Success ? SafeParse<AmbientWanderEnvelope>(raw.Json) : null;
                if (envelope?.run == null)
                {
                    completed?.Invoke(Result<RunSnapshot>.Failure(raw.StatusCode, raw.ErrorCode,
                        raw.Success ? "NPC 배회 응답을 해석할 수 없습니다." : raw.ErrorMessage, raw.CurrentVersion));
                    return;
                }
                PopulateTileCodes(envelope.run.world, raw.Json);
                completed?.Invoke(Result<RunSnapshot>.Success(raw.StatusCode, envelope.run));
            });
        }

        public IEnumerator SubmitTurn(
            string runId,
            string idempotencyKey,
            long expectedRunVersion,
            string ability,
            string targetEntityId,
            string secondaryTargetEntityId,
            PositionSnapshot destination,
            string intent,
            Action<Result<CommittedTurn>> completed)
        {
            var targetIds = new List<string>();
            if (!string.IsNullOrWhiteSpace(targetEntityId)) targetIds.Add(targetEntityId);
            if (!string.IsNullOrWhiteSpace(secondaryTargetEntityId)) targetIds.Add(secondaryTargetEntityId);
            yield return SubmitAction(runId, idempotencyKey, expectedRunVersion, ability,
                targetIds.ToArray(), destination, 0, completed);
        }

        public IEnumerator PrepareD20(string runId, long expectedRunVersion,
            Action<Result<PreparedD20Snapshot>> completed)
        {
            string body = "{\"expectedRunVersion\":" +
                          expectedRunVersion.ToString(CultureInfo.InvariantCulture) + "}";
            yield return Send("POST", "/v1/runs/" + EscapePath(runId) + "/dice", body, raw =>
            {
                PreparedD20Envelope envelope = raw.Success ? SafeParse<PreparedD20Envelope>(raw.Json) : null;
                if (envelope == null || envelope.d20 < 1 || envelope.d20 > 20)
                {
                    completed?.Invoke(Result<PreparedD20Snapshot>.Failure(raw.StatusCode, raw.ErrorCode,
                        raw.Success ? "D20 준비 응답을 해석할 수 없습니다." : raw.ErrorMessage, raw.CurrentVersion));
                    return;
                }
                completed?.Invoke(Result<PreparedD20Snapshot>.Success(raw.StatusCode,
                    new PreparedD20Snapshot { d20 = envelope.d20, expectedRunVersion = envelope.expectedRunVersion }));
            });
        }

        public IEnumerator SubmitAction(
            string runId,
            string idempotencyKey,
            long expectedRunVersion,
            string skillId,
            string[] targetIds,
            PositionSnapshot destination,
            Action<Result<CommittedTurn>> completed)
        {
            yield return SubmitAction(runId, idempotencyKey, expectedRunVersion, skillId,
                targetIds, destination, 0, completed);
        }

        public IEnumerator SubmitAction(
            string runId,
            string idempotencyKey,
            long expectedRunVersion,
            string skillId,
            string[] targetIds,
            PositionSnapshot destination,
            int preparedD20,
            Action<Result<CommittedTurn>> completed)
        {
            if (!IsCanonicalSkillId(skillId) &&
                !string.Equals(skillId, "INTERACT", StringComparison.OrdinalIgnoreCase))
            {
                completed?.Invoke(Result<CommittedTurn>.Failure(0, "INVALID_SKILL",
                    "USE_SKILL accepts a keyboard skill or INTERACT."));
                yield break;
            }
            string body = BuildActionJson(idempotencyKey, expectedRunVersion, skillId, targetIds, destination, preparedD20);
            yield return Send("POST", "/v1/runs/" + EscapePath(runId) + "/actions", body, raw =>
            {
                TurnEnvelope envelope = raw.Success ? SafeParse<TurnEnvelope>(raw.Json) : null;
                if (envelope?.turn == null || envelope.run == null)
                {
                    completed?.Invoke(Result<CommittedTurn>.Failure(raw.StatusCode, raw.ErrorCode,
                        raw.Success ? "턴 응답을 해석할 수 없습니다." : raw.ErrorMessage, raw.CurrentVersion));
                    return;
                }
                PopulateTileCodes(envelope.run.world, raw.Json);
                completed?.Invoke(Result<CommittedTurn>.Success(raw.StatusCode,
                    new CommittedTurn(envelope.turn, envelope.run, envelope.fromIdempotencyCache)));
            });
        }

        /// <summary>
        /// Resolves one server-issued narrative choice. All new choice kinds (including
        /// SKILL and TRAVEL) use this boundary so the authoritative server can reject a
        /// stale choice and record the selected line before dispatching its mechanics.
        /// </summary>
        public IEnumerator SubmitNarrativeChoice(
            string runId,
            string choiceSetId,
            string choiceId,
            string idempotencyKey,
            long expectedRunVersion,
            Action<Result<CommittedTurn>> completed)
        {
            yield return SubmitNarrativeChoice(runId, choiceSetId, choiceId, idempotencyKey,
                expectedRunVersion, 0, completed);
        }

        public IEnumerator SubmitNarrativeChoice(
            string runId,
            string choiceSetId,
            string choiceId,
            string idempotencyKey,
            long expectedRunVersion,
            int preparedD20,
            Action<Result<CommittedTurn>> completed)
        {
            if (string.IsNullOrWhiteSpace(choiceSetId) || string.IsNullOrWhiteSpace(choiceId))
            {
                completed?.Invoke(Result<CommittedTurn>.Failure(0, "INVALID_CHOICE",
                    "A sealed choiceSetId and choiceId are required."));
                yield break;
            }
            string body = BuildChoiceJson(choiceSetId, choiceId, idempotencyKey, expectedRunVersion, preparedD20);
            yield return Send("POST", "/v1/runs/" + EscapePath(runId) + "/choices", body, raw =>
            {
                TurnEnvelope envelope = raw.Success ? SafeParse<TurnEnvelope>(raw.Json) : null;
                if (envelope?.turn == null || envelope.run == null)
                {
                    completed?.Invoke(Result<CommittedTurn>.Failure(raw.StatusCode, raw.ErrorCode,
                        raw.Success ? "선택 결과를 해석할 수 없습니다." : raw.ErrorMessage, raw.CurrentVersion));
                    return;
                }
                PopulateTileCodes(envelope.run.world, raw.Json);
                completed?.Invoke(Result<CommittedTurn>.Success(raw.StatusCode,
                    new CommittedTurn(envelope.turn, envelope.run, envelope.fromIdempotencyCache)));
            });
        }

        public IEnumerator SubmitPlayerMessage(string runId, string text, string idempotencyKey,
            long expectedRunVersion, Action<Result<CommittedTurn>> completed)
        {
            yield return SubmitPlayerMessage(runId, text, idempotencyKey, expectedRunVersion, 0, completed);
        }

        public IEnumerator SubmitPlayerMessage(string runId, string text, string idempotencyKey,
            long expectedRunVersion, int preparedD20, Action<Result<CommittedTurn>> completed)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                completed?.Invoke(Result<CommittedTurn>.Failure(0, "PLAYER_MESSAGE_INVALID", "대화 내용을 입력해 주세요."));
                yield break;
            }
            string body = "{\"text\":\"" + EscapeJson(text.Trim()) + "\"," +
                          "\"idempotencyKey\":\"" + EscapeJson(idempotencyKey) + "\"," +
                          "\"expectedRunVersion\":" + expectedRunVersion.ToString(CultureInfo.InvariantCulture) +
                          (preparedD20 >= 1 && preparedD20 <= 20 ? ",\"preparedD20\":" + preparedD20 : string.Empty) + "}";
            yield return Send("POST", "/v1/runs/" + EscapePath(runId) + "/messages", body, raw =>
            {
                TurnEnvelope envelope = raw.Success ? SafeParse<TurnEnvelope>(raw.Json) : null;
                if (envelope?.turn == null || envelope.run == null)
                {
                    completed?.Invoke(Result<CommittedTurn>.Failure(raw.StatusCode, raw.ErrorCode,
                        raw.Success ? "자연어 대화 결과를 해석할 수 없습니다." : raw.ErrorMessage, raw.CurrentVersion));
                    return;
                }
                PopulateTileCodes(envelope.run.world, raw.Json);
                completed?.Invoke(Result<CommittedTurn>.Success(raw.StatusCode,
                    new CommittedTurn(envelope.turn, envelope.run, envelope.fromIdempotencyCache)));
            });
        }

        public IEnumerator SubmitTravel(
            string runId,
            string idempotencyKey,
            long expectedRunVersion,
            PositionSnapshot destination,
            string intent,
            Action<Result<CommittedNavigation>> completed)
        {
            yield return SubmitTravel(runId, idempotencyKey, expectedRunVersion, destination, completed);
        }

        public IEnumerator SubmitTravel(
            string runId,
            string idempotencyKey,
            long expectedRunVersion,
            PositionSnapshot destination,
            Action<Result<CommittedNavigation>> completed)
        {
            string body = BuildTravelJson(idempotencyKey, expectedRunVersion, destination);
            yield return Send("POST", "/v1/runs/" + EscapePath(runId) + "/travel", body, raw =>
            {
                NavigationEnvelope envelope = raw.Success ? SafeParse<NavigationEnvelope>(raw.Json) : null;
                if (envelope?.navigation == null || envelope.run == null)
                {
                    completed?.Invoke(Result<CommittedNavigation>.Failure(raw.StatusCode, raw.ErrorCode,
                        raw.Success ? "이동 응답을 해석할 수 없습니다." : raw.ErrorMessage, raw.CurrentVersion,
                        raw.ErrorReason, raw.CampaignTurnConsumed, raw.SuggestedActions, raw.StopPosition));
                    return;
                }
                PopulateTileCodes(envelope.run.world, raw.Json);
                completed?.Invoke(Result<CommittedNavigation>.Success(raw.StatusCode,
                    new CommittedNavigation(envelope.navigation, envelope.run, envelope.fromIdempotencyCache)));
            });
        }

        public IEnumerator ResumeRun(string runId, long expectedVersion, Action<Result<RunSnapshot>> completed)
        {
            string body = "{\"expectedRunVersion\":" + expectedVersion.ToString(CultureInfo.InvariantCulture) + "}";
            yield return Send("POST", "/v1/runs/" + EscapePath(runId) + "/resume", body, raw =>
            {
                RunEnvelope envelope = raw.Success ? SafeParse<RunEnvelope>(raw.Json) : null;
                if (envelope?.run == null)
                {
                    completed?.Invoke(Result<RunSnapshot>.Failure(raw.StatusCode, raw.ErrorCode,
                        raw.Success ? "재개 응답을 해석할 수 없습니다." : raw.ErrorMessage, raw.CurrentVersion));
                    return;
                }
                PopulateTileCodes(envelope.run.world, raw.Json);
                completed?.Invoke(Result<RunSnapshot>.Success(raw.StatusCode, envelope.run));
            });
        }

        public IEnumerator GetTurn(string runId, int turnNo, Action<Result<TurnSnapshot>> completed)
        {
            yield return Send("GET", "/v1/runs/" + EscapePath(runId) + "/turns/" +
                turnNo.ToString(CultureInfo.InvariantCulture), null, raw =>
            {
                TurnOnlyEnvelope envelope = raw.Success ? SafeParse<TurnOnlyEnvelope>(raw.Json) : null;
                if (envelope?.turn == null)
                {
                    completed?.Invoke(Result<TurnSnapshot>.Failure(raw.StatusCode, raw.ErrorCode,
                        raw.Success ? "커밋된 턴을 해석할 수 없습니다." : raw.ErrorMessage));
                    return;
                }
                completed?.Invoke(Result<TurnSnapshot>.Success(raw.StatusCode, envelope.turn));
            });
        }

        private sealed class RawResult
        {
            public bool Success;
            public long StatusCode;
            public string Json;
            public string ErrorCode;
            public string ErrorMessage;
            public long CurrentVersion;
            public string ErrorReason;
            public bool CampaignTurnConsumed;
            public string[] SuggestedActions;
            public PositionSnapshot StopPosition;
        }

        private IEnumerator Send(string method, string path, string body, Action<RawResult> completed,
            string idempotencyKey = null)
        {
            using (var request = new UnityWebRequest(_baseUrl + path, method))
            {
                request.downloadHandler = new DownloadHandlerBuffer();
                if (body != null)
                {
                    request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
                    request.SetRequestHeader("Content-Type", "application/json");
                }
                request.SetRequestHeader("Accept", "application/json");
                ApplyIdempotencyKey(request, idempotencyKey);
                if (_userId != null)
                    request.SetRequestHeader("x-user-id", _userId);
                request.timeout = 65;
                yield return request.SendWebRequest();

                string json = request.downloadHandler?.text ?? string.Empty;
                bool ok = request.result == UnityWebRequest.Result.Success && request.responseCode >= 200 && request.responseCode < 300;
                if (ok)
                {
                    completed?.Invoke(new RawResult { Success = true, StatusCode = request.responseCode, Json = json });
                    yield break;
                }

                ErrorEnvelope envelope = SafeParse<ErrorEnvelope>(json);
                completed?.Invoke(new RawResult
                {
                    Success = false,
                    StatusCode = request.responseCode,
                    Json = json,
                    ErrorCode = envelope?.error?.code ?? "NETWORK_ERROR",
                    ErrorMessage = envelope?.error?.message ?? (string.IsNullOrWhiteSpace(request.error) ? "서버 요청에 실패했습니다." : request.error),
                    CurrentVersion = envelope?.error?.details == null ? 0 : envelope.error.details.currentVersion,
                    ErrorReason = envelope?.error?.details?.reason,
                    CampaignTurnConsumed = envelope?.error?.details?.campaignTurnConsumed ?? false,
                    SuggestedActions = envelope?.error?.details?.suggestedActions,
                    StopPosition = envelope?.error?.details?.stopPosition ?? envelope?.error?.details?.encounterPosition
                });
            }
        }

        private static bool IsValidOptionalIdempotencyKey(string value)
        {
            return string.IsNullOrWhiteSpace(value) || (value.Length >= 8 && value.Length <= 128);
        }

        private static void ApplyIdempotencyKey(UnityWebRequest request, string value)
        {
            if (request != null && !string.IsNullOrWhiteSpace(value))
                request.SetRequestHeader("Idempotency-Key", value);
        }

        private static T SafeParse<T>(string json) where T : class
        {
            if (string.IsNullOrWhiteSpace(json))
                return null;
            try
            {
                return JsonUtility.FromJson<T>(json);
            }
            catch (ArgumentException)
            {
                return null;
            }
        }

        private static string BuildActionJson(
            string idempotencyKey,
            long expectedVersion,
            string skillId,
            string[] targetIds,
            PositionSnapshot destination,
            int preparedD20)
        {
            var fields = new List<string>
            {
                "\"inputType\":\"USE_SKILL\"",
                "\"idempotencyKey\":\"" + EscapeJson(idempotencyKey) + "\"",
                "\"expectedRunVersion\":" + expectedVersion.ToString(CultureInfo.InvariantCulture),
                "\"skillId\":\"" + EscapeJson((skillId ?? string.Empty).ToUpperInvariant()) + "\""
            };
            var encodedTargets = new List<string>();
            if (targetIds != null)
            {
                for (int i = 0; i < targetIds.Length && i < 2; i++)
                    if (!string.IsNullOrWhiteSpace(targetIds[i]))
                        encodedTargets.Add("\"" + EscapeJson(targetIds[i]) + "\"");
            }
            fields.Add("\"targetIds\":[" + string.Join(",", encodedTargets) + "]");
            if (preparedD20 >= 1 && preparedD20 <= 20)
                fields.Add("\"preparedD20\":" + preparedD20.ToString(CultureInfo.InvariantCulture));
            if (destination != null)
            {
                fields.Add("\"destination\":{\"x\":" + destination.x.ToString(CultureInfo.InvariantCulture) +
                           ",\"y\":" + destination.y.ToString(CultureInfo.InvariantCulture) + "}");
            }
            return "{" + string.Join(",", fields) + "}";
        }

        private static string BuildChoiceJson(string choiceSetId, string choiceId,
            string idempotencyKey, long expectedVersion, int preparedD20)
        {
            return "{\"choiceSetId\":\"" + EscapeJson(choiceSetId) + "\"," +
                   "\"choiceId\":\"" + EscapeJson(choiceId) + "\"," +
                   "\"idempotencyKey\":\"" + EscapeJson(idempotencyKey) + "\"," +
                   "\"expectedRunVersion\":" + expectedVersion.ToString(CultureInfo.InvariantCulture) +
                   (preparedD20 >= 1 && preparedD20 <= 20 ? ",\"preparedD20\":" + preparedD20 : string.Empty) + "}";
        }

        private static bool IsCanonicalSkillId(string skillId)
        {
            switch ((skillId ?? string.Empty).Trim().ToUpperInvariant())
            {
                case "COPY":
                case "DELETE":
                case "CONNECT":
                case "RESTORE":
                case "UNDO":
                case "SEARCH":
                case "SELECT_ALL": return true;
                default: return false;
            }
        }

        private static string BuildTravelJson(
            string idempotencyKey,
            long expectedVersion,
            PositionSnapshot destination)
        {
            if (destination == null)
                return string.Empty;
            return "{\"inputType\":\"MOVE\",\"idempotencyKey\":\"" + EscapeJson(idempotencyKey) + "\"," +
                   "\"expectedRunVersion\":" + expectedVersion.ToString(CultureInfo.InvariantCulture) + "," +
                   "\"destination\":{\"x\":" + destination.x.ToString(CultureInfo.InvariantCulture) +
                   ",\"y\":" + destination.y.ToString(CultureInfo.InvariantCulture) + "}}";
        }

        private static string EscapeJson(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;
            var builder = new StringBuilder(value.Length + 8);
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                switch (c)
                {
                    case '\\': builder.Append("\\\\"); break;
                    case '"': builder.Append("\\\""); break;
                    case '\n': builder.Append("\\n"); break;
                    case '\r': builder.Append("\\r"); break;
                    case '\t': builder.Append("\\t"); break;
                    default:
                        if (c < 32) builder.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else builder.Append(c);
                        break;
                }
            }
            return builder.ToString();
        }

        private static string EscapePath(string value)
        {
            return UnityWebRequest.EscapeURL(value ?? string.Empty);
        }

        /// <summary>
        /// JsonUtility intentionally ignores compact nested primitive RLE arrays. Decode the three
        /// bounded world layers explicitly and accept them only when their expanded sizes and legend
        /// codes match the immutable world dimensions.
        /// </summary>
        private static void PopulateTileCodes(WorldSnapshot world, string json)
        {
            if (world == null || world.width <= 0 || world.height <= 0 || world.width > 256 || world.height > 256)
                return;
            int expected = world.width * world.height;
            world.tileCodes = DecodeRleField(json, "tilesRle", expected, world.tileLegend);
            world.areaCodes = DecodeRleField(json, "areaMapRle", expected, world.areaMapLegend);
            world.biomeCodes = DecodeRleField(json, "biomeMapRle", expected, world.biomeMapLegend);
        }

        private static int[] DecodeRleField(string json, string fieldName, int expected, string[] legend)
        {
            if (string.IsNullOrEmpty(json) || string.IsNullOrEmpty(fieldName) || expected <= 0 ||
                legend == null || legend.Length == 0)
                return null;
            int start = FindArrayPropertyStart(json, fieldName);
            if (start < 0)
                return null;

            var numbers = new List<int>();
            int depth = 0;
            for (int i = start; i < json.Length; i++)
            {
                char c = json[i];
                if (c == '[') depth++;
                else if (c == ']')
                {
                    depth--;
                    if (depth == 0) break;
                }
                else if (c >= '0' && c <= '9')
                {
                    if (i > start && json[i - 1] == '-')
                        return null;
                    int value = 0;
                    while (i < json.Length && json[i] >= '0' && json[i] <= '9')
                    {
                        int digit = json[i] - '0';
                        if (value > 1_000_000 || value > (int.MaxValue - digit) / 10)
                            return null;
                        value = value * 10 + digit;
                        i++;
                    }
                    i--;
                    numbers.Add(value);
                }
            }

            if (numbers.Count == 0 || numbers.Count % 2 != 0)
                return null;
            var tiles = new int[expected];
            int offset = 0;
            for (int i = 0; i < numbers.Count; i += 2)
            {
                int code = numbers[i];
                int count = numbers[i + 1];
                if (count <= 0 || offset + count > expected || code >= legend.Length)
                    return null;
                for (int j = 0; j < count; j++)
                    tiles[offset++] = code;
            }
            return offset == expected ? tiles : null;
        }

        private static int FindArrayPropertyStart(string json, string fieldName)
        {
            for (int index = 0; index < json.Length; index++)
            {
                if (json[index] != '"')
                    continue;

                int contentStart = index + 1;
                int cursor = contentStart;
                bool escaped = false;
                while (cursor < json.Length)
                {
                    char current = json[cursor];
                    if (escaped)
                    {
                        escaped = false;
                    }
                    else if (current == '\\')
                    {
                        escaped = true;
                    }
                    else if (current == '"')
                    {
                        break;
                    }
                    cursor++;
                }
                if (cursor >= json.Length)
                    return -1;

                bool exactUnescapedName = cursor - contentStart == fieldName.Length &&
                    string.CompareOrdinal(json, contentStart, fieldName, 0, fieldName.Length) == 0;
                int afterName = cursor + 1;
                while (afterName < json.Length && char.IsWhiteSpace(json[afterName]))
                    afterName++;
                if (exactUnescapedName && afterName < json.Length && json[afterName] == ':')
                {
                    afterName++;
                    while (afterName < json.Length && char.IsWhiteSpace(json[afterName]))
                        afterName++;
                    if (afterName < json.Length && json[afterName] == '[')
                        return afterName;
                }
                index = cursor;
            }
            return -1;
        }
    }
}
#pragma warning restore 0649
