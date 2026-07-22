using System;
using System.Collections;
using System.Collections.Generic;
using KeyboardWanderer.Core;
using KeyboardWanderer.Gameplay;
using KeyboardWanderer.Networking;
using KeyboardWanderer.Presentation;
using KeyboardWanderer.Runtime;
using KeyboardWanderer.World;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.Tilemaps;

namespace KeyboardWanderer.Demo
{
    public sealed class KeyboardWandererDemoController : MonoBehaviour
    {
        private const float TileSize = 1f;
        private const int CinematicTravelPathThreshold = 9;
        private const int TerrainSortingOrder = -1000;
        private const string TutorialCompletedKey = "keyboard-wanderer.tutorial-v1-complete";
        private const int TargetFrameRate = 30;
        private const float DecorationOcclusionInterval = 0.1f;
        private const float AmbientWanderInterval = 2f;
        private const int MaxBiomeDecorations = 600;
        private const string IntroCompletedKey = "keyboard-wanderer.intro-v1-complete";

        private enum ScreenMode
        {
            Title,
            Playing,
            Settings
        }

        private enum DialoguePageSource
        {
            StorySequence,
            SearchDialogue,
            Narrative
        }

        private readonly Dictionary<Guid, KeyboardWandererEntityVisualState> _entityVisuals = new Dictionary<Guid, KeyboardWandererEntityVisualState>();
        private readonly List<Tile> _runtimeTiles = new List<Tile>();
        private readonly List<string> _sessionLog = new List<string>();
        private readonly GameFlowStateMachine _flowStateMachine = new GameFlowStateMachine();

        [Header("Authored content")]
        [SerializeField] private KeyboardWandererAuthoringSettings authoringSettings;
        [SerializeField] private NinjaAdventureAssetManifest authoredAssetManifest;
        [SerializeField] private KeyboardWandererWorldView authoredWorld;
        [SerializeField] private Camera authoredCamera;
        [SerializeField] private KeyboardWandererCameraController authoredCameraController;
        [SerializeField] private AudioSource authoredMusicSource;
        [SerializeField] private AudioSource authoredSfxSource;
        [SerializeField] private KeyboardWandererAudioController authoredAudioController;
        [FormerlySerializedAs("authoredInputController")]
        [SerializeField] private KeyboardWandererInputRouter authoredInputRouter;
        [SerializeField] private KeyboardWandererSelectionController authoredSelectionController;
        [SerializeField] private KeyboardWandererAbilityAvailability authoredAbilityAvailability;
        [SerializeField] private KeyboardWandererTurnCoordinator authoredTurnCoordinator;
        [SerializeField] private KeyboardWandererRunSessionController authoredRunSessionController;
        [SerializeField] private KeyboardWandererSettingsController authoredSettingsController;
        [SerializeField] private KeyboardWandererVisualAssetLibrary authoredVisualAssetLibrary;
        [SerializeField] private KeyboardWandererMinimapRenderer authoredMinimapRenderer;
        [SerializeField] private KeyboardWandererPathPlanner authoredPathPlanner;
        [SerializeField] private KeyboardWandererBiomeDecorationRenderer authoredDecorationRenderer;
        [SerializeField] private KeyboardWandererEntityVisualFactory authoredEntityVisualFactory;
        [SerializeField] private KeyboardWandererEntityAnimationDriver authoredEntityAnimationDriver;

        private LocalTurnService _service;
        private ITurnGateway _turnGateway;
        private GmNarrativeClient _narrativeClient;
        private GameApiClient _gameApi;
        private SceneSequencePlayer _sceneSequencePlayer;
        private Coroutine _scenePlaybackCoroutine;
        private Coroutine _storyWorldActionCoroutine;
        private Coroutine _turnImpactPresentationCoroutine;
        private Coroutine _focusDialogueInputCoroutine;
        private bool _storyWorldActionPlaying;
        private bool _turnImpactPresentationPlaying;
        private GameApiClient.CampaignSnapshot _serverCampaign;
        private GameApiClient.RunSnapshot _serverRun;
        private float _nextAmbientWanderAt;
        private float _nextDecorationOcclusionAt;
        private ScreenMode _screenMode = ScreenMode.Title;
        private ScreenMode _settingsReturn = ScreenMode.Title;
        private bool _settingsReturnToPause;
        private Guid? _lastRestorableTarget;
        private GridCoord? _lastRestorableCoord;
        private string _lastRestorableName;
        private string _lastNarrative = "코드리아의 봉인된 세계가 넙죽이의 첫 선택을 기다립니다.";
        private string _lastAttempt = "목적지 또는 스킬과 대상을 선택하세요.";
        private string _lastStateChanges = "아직 확정된 상태 변화가 없습니다.";
        private int _lastD20;
        private int _lastModifier;
        private string _lastModifierBreakdown = "--";
        private int _lastDifficulty;
        private int _lastMechanicalScore;
        private string _lastActionContext = "--";
        private string _lastOutcome = "READY";
        private string _lastExplanation = "서버는 입력을 가장 가까운 합법적 시도로 정규화합니다.";
        private string[] _lastDialogue = Array.Empty<string>();
        private bool _hasDialoguePageCache;
        private DialoguePageSource _cachedDialoguePageSource;
        private string[] _cachedDialogueStoryTexts = Array.Empty<string>();
        private string[] _cachedDialogueLines = Array.Empty<string>();
        private string _cachedDialogueNarrative = string.Empty;
        private string _cachedDialogueInterventionReason = string.Empty;
        private bool _cachedDialogueEncounterMoveRequired;
        private bool _cachedDialogueContinuesWithMovement;
        private string[] _cachedDialoguePages = Array.Empty<string>();
        private string _cachedDialogueSignature = string.Empty;
        private int _dialoguePageCacheRevision;
        private int _flowPhaseRefreshRevision;
        // 대화 줄(trim) → 화자 이름·스프라이트. 서버 DIALOGUE 씬 액션에서 채워지고 런 리셋 때 비운다.
        private readonly Dictionary<string, string> _dialogueSpeakerNames = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<string, Sprite> _dialogueSpeakerSprites = new Dictionary<string, Sprite>(StringComparer.Ordinal);
        private StorySequencePage[] _lastStorySequence = Array.Empty<StorySequencePage>();
        private string _lastNextInterventionReason = string.Empty;
        private string _lastChoiceSetId = string.Empty;
        private NarrativeChoiceOption[] _lastNarrativeChoices = Array.Empty<NarrativeChoiceOption>();
        private string[] _lastSuggestedSkillIds = Array.Empty<string>();
        private GameApiClient.SceneActionSnapshot[] _lastServerSceneSequence = Array.Empty<GameApiClient.SceneActionSnapshot>();
        private GameApiClient.SceneActionSnapshot[] _pendingRuntimeRenderSequence = Array.Empty<GameApiClient.SceneActionSnapshot>();
        private readonly HashSet<string> _playedStoryActionIds = new HashSet<string>(StringComparer.Ordinal);
        private bool _lastNarrativeFallbackUsed = true;
        private string _lastNarrativeModel = "deterministic";
        private bool _gmPending;
        private bool _serverOnline;
        private bool _serverPending;
        private bool _choiceSubmissionPending;
        private bool _naturalLanguageComposeMode;
        private string _choiceStatusMessage = string.Empty;
        private string _pendingNarrativeChoiceLabel = string.Empty;
        private string _pendingPlayerMessageText = string.Empty;
        private string _pendingPlayerMessageIdempotencyKey = string.Empty;
        private long _pendingPlayerMessageExpectedVersion;
        private int _optionalNarrativeRequestToken;
        private bool _sessionReplacedMissingContinue;
        private bool _encounterMoveRequired;
        private GridCoord? _encounterStagingCoord;
        private string _encounterReason;
        private string _serverStatus = "권위 서버 확인 전";
        private bool _showPause;
        private float _pauseStartedAt;
        private bool _playerWalking;
        private bool _lastPresentationContinuesWithMovement;
        private bool _directionalInputHeld;
        private bool _hasBufferedDirectionalMove;
        private Vector2Int _bufferedMoveDirection;
        private bool _reopenDialogueAfterWalk;
        private bool _runEndCutscenePlayed;
        private bool _suppressDialogueReopenAfterWalk;
        private int _runGeneration;
        private long _worldSeed;
        private GridCoord? _cameraInspectCoord;
        private float _cameraInspectUntil;

        private KeyboardWandererCameraController _cameraController;
        private KeyboardWandererSceneUI _sceneUi;
        private GameObject _worldRoot;
        private SpriteRenderer _selectionRenderer;
        private KeyboardWandererAudioController _audioController;
        private KeyboardWandererInputRouter _inputRouter;
        private KeyboardWandererSelectionController _selectionController;
        private KeyboardWandererAbilityAvailability _abilityAvailability;
        private KeyboardWandererTurnCoordinator _turnCoordinator;
        private KeyboardWandererRunSessionController _runSessionController;
        private KeyboardWandererSettingsController _settingsController;
        private KeyboardWandererVisualAssetLibrary _visualAssetLibrary;
        private KeyboardWandererMinimapRenderer _minimapRenderer;
        private KeyboardWandererPathPlanner _pathPlanner;
        private KeyboardWandererBiomeDecorationRenderer _decorationRenderer;
        private KeyboardWandererEntityVisualFactory _entityVisualFactory;
        private KeyboardWandererEntityAnimationDriver _entityAnimationDriver;
        private KeyboardWandererDiceOverlay _diceOverlay;
        private KeyboardWandererCombatEffectOverlay _combatEffectOverlay;
        private int _preparedD20;
        private float _playerActionUntil;
        // 현재 스킬 시전 모션 프레임. null이면 방향별 기본 공격 모션을 쓴다(DELETE 등).
        private Sprite[] _playerActionFrames;
        private bool _hasPendingImpactTargetPosition;
        private Vector3 _pendingImpactTargetPosition;

        private float PlayerWalkSpeed => (authoringSettings != null ? authoringSettings.PlayerWalkSpeed : 4.2f) * 1.5f;
        private float MusicVolume => _settingsController != null ? _settingsController.MusicVolume : 0.65f;
        private float SfxVolume => _settingsController != null ? _settingsController.SfxVolume : 0.8f;
        private bool GmEnabled => _settingsController == null || _settingsController.GmEnabled;
        internal bool OptionalNarrativePending => _gmPending;
        private bool TurnPending => _serverPending || _choiceSubmissionPending ||
                                    (_turnCoordinator != null && _turnCoordinator.IsPending);
        private Transform WorldLandmarkRoot => authoredWorld != null ? authoredWorld.RuntimeLandmarks : null;
        private Transform WorldEffectsRoot => authoredWorld != null ? authoredWorld.RuntimeEffects : null;

        // 기존 메서드의 읽기 위치를 유지하면서 실제 에셋 소유권은 VisualAssetLibrary에 둔다.
        private NinjaAdventureAssetManifest _assets => _visualAssetLibrary != null ? _visualAssetLibrary.Manifest : null;
        private Sprite _grassSprite => _visualAssetLibrary.GrassSprite;
        private Sprite _dirtSprite => _visualAssetLibrary.DirtSprite;
        private Sprite _darkGrassSprite => _visualAssetLibrary.DarkGrassSprite;
        private Sprite _wallSprite => _visualAssetLibrary.WallSprite;
        private Sprite _waterSprite => _visualAssetLibrary.WaterSprite;
        private Sprite _snowSprite => _visualAssetLibrary.SnowSprite;
        private Sprite _cavernFloorSprite => _visualAssetLibrary.CavernFloorSprite;
        private Sprite _ruinFloorSprite => _visualAssetLibrary.RuinFloorSprite;
        private Sprite _forestTreeSprite => _visualAssetLibrary.ForestTreeSprite;
        private Sprite _forestHouseSprite => _visualAssetLibrary.ForestHouseSprite;
        private Sprite _wetlandPlantSprite => _visualAssetLibrary.WetlandPlantSprite;
        private Sprite _wetlandLandmarkSprite => _visualAssetLibrary.WetlandLandmarkSprite;
        private Sprite _desertPalmSprite => _visualAssetLibrary.DesertPalmSprite;
        private Sprite _desertLandmarkSprite => _visualAssetLibrary.DesertLandmarkSprite;
        private Sprite _frostTreeSprite => _visualAssetLibrary.FrostTreeSprite;
        private Sprite _frostLandmarkSprite => _visualAssetLibrary.FrostLandmarkSprite;
        private Sprite _cavernCrystalSprite => _visualAssetLibrary.CavernCrystalSprite;
        private Sprite _ruinTreeSprite => _visualAssetLibrary.RuinTreeSprite;
        private Sprite _ruinLandmarkSprite => _visualAssetLibrary.RuinLandmarkSprite;
        private Sprite[] _forestDecorationSprites => _visualAssetLibrary.ForestDecorationSprites;
        private Sprite[] _wetlandDecorationSprites => _visualAssetLibrary.WetlandDecorationSprites;
        private Sprite[] _desertDecorationSprites => _visualAssetLibrary.DesertDecorationSprites;
        private Sprite[] _frostDecorationSprites => _visualAssetLibrary.FrostDecorationSprites;
        private Sprite[] _cavernDecorationSprites => _visualAssetLibrary.CavernDecorationSprites;
        private Sprite[] _ruinDecorationSprites => _visualAssetLibrary.RuinDecorationSprites;
        private Sprite _playerSprite => _visualAssetLibrary.PlayerSprite;
        private Sprite _wardenSprite => _visualAssetLibrary.WardenSprite;
        private Sprite _villagerSprite => _visualAssetLibrary.VillagerSprite;
        private Sprite _slimeSprite => _visualAssetLibrary.SlimeSprite;
        private Sprite _bookSprite => _visualAssetLibrary.BookSprite;
        private Sprite _crateSprite => _visualAssetLibrary.CrateSprite;
        private Sprite _chestSprite => _visualAssetLibrary.ChestSprite;
        private Sprite _d20Sprite => _visualAssetLibrary.D20Sprite;
        private Sprite _whiteSprite => _visualAssetLibrary.WhiteSprite;
        private Sprite[] _playerIdleFrames => _visualAssetLibrary.PlayerIdleFrames;
        private Sprite[] _playerWalkFrames => _visualAssetLibrary.PlayerWalkFrames;
        private Sprite[] _playerWalkLeftFrames => _visualAssetLibrary.PlayerWalkLeftFrames;
        private Sprite[] _playerWalkUpFrames => _visualAssetLibrary.PlayerWalkUpFrames;
        private Sprite[] _playerWalkDownFrames => _visualAssetLibrary.PlayerWalkDownFrames;
        private Sprite[] _playerAttackFrames => _visualAssetLibrary.PlayerAttackFrames;
        private Sprite[] _playerAttackLeftFrames => _visualAssetLibrary.PlayerAttackLeftFrames;
        private Sprite[] _playerAttackUpFrames => _visualAssetLibrary.PlayerAttackUpFrames;
        private Sprite[] _playerAttackDownFrames => _visualAssetLibrary.PlayerAttackDownFrames;
        private Sprite[] _slimeFrames => _visualAssetLibrary.SlimeFrames;
        private Sprite[] _villagerFrames => _visualAssetLibrary.VillagerFrames;
        private readonly RunCoordinator _runCoordinator = new RunCoordinator();
        private IRunPresentationAdapter _runPresentationAdapter = new LocalRunPresentationAdapter();
        private RunPresentationModel _runPresentationModel = RunPresentationModel.Empty;
        private readonly DialoguePresenter _dialoguePresenter = new DialoguePresenter();
        private readonly TutorialPresenter _tutorialPresenter = new TutorialPresenter();
        private readonly DestructiveActionConfirmation _actionConfirmation = new DestructiveActionConfirmation();
        private readonly GameplayTelemetry _gameplayTelemetry = new GameplayTelemetry();
        public string SessionTelemetry => _gameplayTelemetry.DiagnosticSummary;
        private string _renderedLayoutHash = string.Empty;
        private PresentationChange _pendingPresentationChanges = PresentationChange.All;
        private HudPresenter _hudPresenter;

        private void Awake()
        {
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = TargetFrameRate;
            _selectionController = authoredSelectionController;
            _abilityAvailability = authoredAbilityAvailability;
            _turnCoordinator = authoredTurnCoordinator;
            _runSessionController = authoredRunSessionController;
            _settingsController = authoredSettingsController;
            _visualAssetLibrary = authoredVisualAssetLibrary;
            _minimapRenderer = authoredMinimapRenderer;
            _pathPlanner = authoredPathPlanner;
            _decorationRenderer = authoredDecorationRenderer;
            _entityVisualFactory = authoredEntityVisualFactory;
            _entityAnimationDriver = authoredEntityAnimationDriver;
            if (_entityAnimationDriver != null)
                _entityAnimationDriver.PlayerPathCompleted += CompletePlayerPathAnimation;
            if (_runSessionController != null)
                _runSessionController.StatusChanged += HandleRunSessionStatusChanged;
            _runCoordinator.PresentationChanged += HandlePresentationChanged;
            _sceneUi = GetComponentInChildren<KeyboardWandererSceneUI>(true);
            _hudPresenter = new HudPresenter(_sceneUi);
            _sceneSequencePlayer = GetComponent<SceneSequencePlayer>();
            if (_visualAssetLibrary == null)
                throw new InvalidOperationException("Authored World에는 Visual Asset Library 참조가 필요합니다.");
            _visualAssetLibrary.Initialize(authoredAssetManifest, authoringSettings);
            _diceOverlay = GetComponent<KeyboardWandererDiceOverlay>();
            if (_diceOverlay == null)
                _diceOverlay = gameObject.AddComponent<KeyboardWandererDiceOverlay>();
            _diceOverlay.Configure(authoredCamera != null ? authoredCamera : Camera.main, _assets?.D20Prefab);
            _combatEffectOverlay = GetComponent<KeyboardWandererCombatEffectOverlay>();
            if (_combatEffectOverlay == null)
                _combatEffectOverlay = gameObject.AddComponent<KeyboardWandererCombatEffectOverlay>();
            _combatEffectOverlay.Configure(authoredCamera != null ? authoredCamera : Camera.main);
            _sceneUi?.InitializeInventoryQuestOverlay(_assets, UiSelectInventoryItem,
                open => _inputRouter?.SetUiOverlayMode(open));
            ConfigureCamera();
            ConfigureAudio();
            ConfigureInput();
            EnsureRuntimeClients();
            ShowTitle();
            if (_sceneUi != null)
            {
                _sceneUi.Bind(this);
                UpdateAuthoredUi();
            }
            PlayIntroIfNeeded();
        }

        /// <summary>
        /// 첫 실행에서만 오프닝 컷신을 화면 위 오버레이로 재생한다. 화면 상태(_screenMode)와
        /// 완전히 독립적이므로 어떤 화면이 떠 있든 그 위에 그려지고, 끝나면 스스로 사라진다.
        /// </summary>
        private void PlayIntroIfNeeded()
        {
            if (KeyboardWandererPreferences.GetInt(IntroCompletedKey, 0) != 0)
                return;
            Sprite[] frames = _visualAssetLibrary != null && _visualAssetLibrary.Manifest != null
                ? _visualAssetLibrary.Manifest.CutsceneIntroFrames
                : null;
            if (frames == null || frames.Length == 0)
                return;
            KeyboardWandererCutsceneOverlayView.Play(transform, frames, () =>
            {
                KeyboardWandererPreferences.SetInt(IntroCompletedKey, 1);
                KeyboardWandererPreferences.Save();
            });

        }

        /// <summary>런이 끝난 첫 프레임에 한 번만 결말/게임 오버 컷신을 재생한다.</summary>
        private void PlayRunEndCutsceneIfNeeded(bool gameOver)
        {
            if (_runEndCutscenePlayed || _assets == null)
                return;
            _runEndCutscenePlayed = true;
            Sprite frame = gameOver ? _assets.CutsceneGameOverImage : _assets.CutsceneEndingImage;
            if (frame == null)
                return;
            KeyboardWandererCutsceneOverlayView.Play(transform, new[] { frame }, null);
        }

        public void ConfigureAuthoredContent(
            KeyboardWandererAuthoringSettings settings,
            NinjaAdventureAssetManifest manifest,
            KeyboardWandererWorldView world,
            Camera sceneCamera,
            KeyboardWandererCameraController cameraController,
            AudioSource music,
            AudioSource sfx,
            KeyboardWandererAudioController audioController,
            KeyboardWandererInputRouter inputRouter,
            KeyboardWandererSelectionController selectionController,
            KeyboardWandererAbilityAvailability abilityAvailability,
            KeyboardWandererTurnCoordinator turnCoordinator,
            KeyboardWandererRunSessionController runSessionController,
            KeyboardWandererSettingsController settingsController,
            KeyboardWandererVisualAssetLibrary visualAssetLibrary,
            KeyboardWandererMinimapRenderer minimapRenderer,
            KeyboardWandererPathPlanner pathPlanner,
            KeyboardWandererBiomeDecorationRenderer decorationRenderer,
            KeyboardWandererEntityVisualFactory entityVisualFactory,
            KeyboardWandererEntityAnimationDriver entityAnimationDriver)
        {
            authoringSettings = settings;
            authoredAssetManifest = manifest;
            authoredWorld = world;
            authoredCamera = sceneCamera;
            authoredCameraController = cameraController;
            authoredMusicSource = music;
            authoredSfxSource = sfx;
            authoredAudioController = audioController;
            authoredInputRouter = inputRouter;
            authoredSelectionController = selectionController;
            authoredAbilityAvailability = abilityAvailability;
            authoredTurnCoordinator = turnCoordinator;
            authoredRunSessionController = runSessionController;
            authoredSettingsController = settingsController;
            authoredVisualAssetLibrary = visualAssetLibrary;
            authoredMinimapRenderer = minimapRenderer;
            authoredPathPlanner = pathPlanner;
            authoredDecorationRenderer = decorationRenderer;
            authoredEntityVisualFactory = entityVisualFactory;
            authoredEntityAnimationDriver = entityAnimationDriver;
        }

        private void OnEnable()
        {
            // These clients are intentionally not serialized. Recreate them after an
            // Editor domain reload as well as during a normal player startup.
            EnsureRuntimeClients();
        }

        private void EnsureRuntimeClients()
        {
            if (_narrativeClient == null)
                _narrativeClient = new GmNarrativeClient();
            if (_gameApi == null)
                _gameApi = _runSessionController != null ? _runSessionController.Api : new GameApiClient();
        }

        /// <summary>세션 오브젝트의 연결 진행 문구를 제목 화면에 반영한다.</summary>
        private void HandleRunSessionStatusChanged(string status)
        {
            _serverStatus = status;
            PublishPresentationState(PresentationChange.Screen | PresentationChange.Hud);
        }

        private void OnDestroy()
        {
            DisconnectInput();
            if (_entityAnimationDriver != null)
                _entityAnimationDriver.PlayerPathCompleted -= CompletePlayerPathAnimation;
            if (_runSessionController != null)
                _runSessionController.StatusChanged -= HandleRunSessionStatusChanged;
            _runCoordinator.PresentationChanged -= HandlePresentationChanged;
            _sceneSequencePlayer?.Cancel();
            StopAllCoroutines();
            for (int i = 0; i < _runtimeTiles.Count; i++)
                if (_runtimeTiles[i] != null)
                    Destroy(_runtimeTiles[i]);
        }

        private void Update()
        {
            UpdateCameraViewport();
            PublishPresentationState();
            // 코루틴 종료처럼 RunPresentationState 자체에는 포함되지 않는 시각 상태도
            // 요청된 다음 프레임에 반드시 UI에 반영한다.
            if (_pendingPresentationChanges != PresentationChange.None)
                UpdateAuthoredUi();
            if (_screenMode != ScreenMode.Playing || _service == null)
                return;
            // Pause is a real simulation boundary. UI remains responsive because the
            // presentation pass above still runs, while world animation, dialogue
            // shortcuts and autonomous/network mutations stop until resume.
            if (_showPause)
                return;

            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            if (keyboard != null && !_dialoguePresenter.IsDismissed)
            {
                bool isInputFocused = _sceneUi != null && _sceneUi.IsDialogueInputFocused;
                if (!isInputFocused)
                {
                    string narrative = _lastOutcome == "RESTORED" || (_lastOutcome == "READY" && string.IsNullOrWhiteSpace(_lastNarrative))
                        ? CampaignPremise(_service.CurrentView)
                        : ShortNarrative(_lastNarrative);
                    string[] pages = BuildDialoguePages(narrative);
                    bool awaitingIntervention = IsInterventionPage(_dialoguePresenter.Page, pages.Length);
                    // The input router is the sole keyboard/gamepad owner at an
                    // intervention. Keeping this legacy page-advance path out of that
                    // state prevents Return/Space from both advancing and choosing.
                    if (!awaitingIntervention &&
                        keyboard.spaceKey.wasPressedThisFrame)
                    {
                        UiAdvanceDialogue();
                    }
                    else if (!awaitingIntervention && keyboard.enterKey.wasPressedThisFrame)
                    {
                        UiAdvanceDialogue();
                    }
                }
            }

            // Online ambient movement used to POST every two seconds and shared the
            // gameplay pending flag. A stalled cosmetic request could therefore lock
            // every command for the transport timeout and immediately retry forever.
            // The release client leaves online NPC positions entirely server-driven by
            // committed gameplay snapshots; only the deterministic local fallback wanders.
            if (!_serverOnline && !_playerWalking && !TurnPending && !_tutorialPresenter.IsActive &&
                !WorldPresentationPlaying &&
                Time.unscaledTime >= _nextAmbientWanderAt)
            {
                _nextAmbientWanderAt = Time.unscaledTime + AmbientWanderInterval;
                GetCameraActiveTileBounds(_service.CurrentView, 2, out GridCoord activeMin, out GridCoord activeMax);
                if (_service.TryAdvanceAmbientWander(2, activeMin, activeMax))
                    SyncEntityVisuals(_service.CurrentView);
            }

            UpdateAnimatedVisuals();
            UpdateCameraFollow();
            UpdateDecorationOcclusion();
        }

        private void HandlePresentationChanged(RunPresentationState state, PresentationChange changes)
        {
            _pendingPresentationChanges |= changes;
            UpdateAuthoredUi();
        }

        private void PublishPresentationState(PresentationChange requested = PresentationChange.None)
        {
            RunView view = _service?.CurrentView;
            _runPresentationModel = PresentationModel(view);
            RunPresentationCore core = _runPresentationModel.Core;
            var state = new RunPresentationState(core,
                _selectionController.SelectedCoord, _selectionController.SelectedTarget, _selectionController.Ability, (int)_screenMode, _dialoguePresenter.Page,
                _dialoguePresenter.Signature, _showPause, TurnPending, _playerWalking);
            _runCoordinator.Publish(state, requested);
        }

        private void UpdateAuthoredUi()
        {
            if (_sceneUi == null || !_sceneUi.IsReady)
                return;

            PresentationChange changes = _pendingPresentationChanges;
            _pendingPresentationChanges = PresentationChange.None;

            bool ended = _screenMode == ScreenMode.Playing && _service != null && !RunIsPlaying(_service.CurrentView);
            _inputRouter?.SetUiOverlayMode(_screenMode != ScreenMode.Playing || _showPause || ended ||
                                            (_sceneUi?.IsInventoryQuestOverlayOpen ?? false));
            _hudPresenter.PresentScreen(_screenMode == ScreenMode.Title, _screenMode == ScreenMode.Settings,
                _screenMode == ScreenMode.Playing, _showPause, ended, MusicVolume, SfxVolume, GmEnabled);
            long nextSeed = _runSessionController != null ? _runSessionController.NextSeed : 20260718L;
            _sceneUi.PresentTitle(CampaignCatalog.CampaignTitle,
                "관리자 키보드로 붕괴한 코드리아를 복구하는 탐험 RPG",
                "NEXT SEED  " + nextSeed,
                "넙죽이가 되어 여섯 지역을 탐험하세요.\n" +
                "대상을 조사하고 적과 싸워 관리자 권한 3개를 되찾으면 루트 시스템의 결말이 열립니다.",
                _serverStatus + " · NUPJUK : The Last Commit", _playerSprite, !_serverPending,
                !_serverPending && _runSessionController != null && _runSessionController.HasContinue);

            if (_service == null)
                return;

            _inputRouter?.SetNarrativeChoiceMode(false);
            _inputRouter?.SetNarrativeOverlayMode(false);
            RunView view = _service.CurrentView;
            if (ended && !_runEndCutscenePlayed)
                PlayRunEndCutsceneIfNeeded(IsGameOver(view, PresentationModel(view)));
            RefreshDynamicMusic(view, _runPresentationModel);
            if ((changes & PresentationChange.Minimap) != 0)
                UpdateMinimap(view);
            string narrative = _lastOutcome == "RESTORED" ||
                               (_lastOutcome == "READY" && string.IsNullOrWhiteSpace(_lastNarrative))
                ? CampaignPremise(view)
                : ShortNarrative(_lastNarrative);
            string[] dialoguePages = BuildDialoguePages(narrative);
            if (_tutorialPresenter.IsActive)
            {
                // A previous online run may have left a large choice modal above the
                // story panel. Disable its root (and therefore every raycast target)
                // before the tutorial is presented so HUD controls remain clickable.
                _sceneUi.PresentDialogueChoices(false, Array.Empty<NarrativeChoiceOption>(), false);
                _sceneUi.PresentTutorial(_tutorialPresenter.Page, StoryBeatObjective(view));
            }
            else
            {
                if (_dialoguePresenter.Synchronize(_cachedDialogueSignature, _playerWalking))
                {
                    if (_playerWalking && !_suppressDialogueReopenAfterWalk)
                        _reopenDialogueAfterWalk = true;
                }
                int dialoguePage = Mathf.Clamp(_dialoguePresenter.Page, 0, Mathf.Max(0, dialoguePages.Length - 1));
                PlayCurrentStoryWorldAction(dialoguePage);
                bool hasNextDialogue = dialoguePage < dialoguePages.Length - 1;
                bool awaitingIntervention = IsInterventionPage(dialoguePage, dialoguePages.Length);
                string storySpeaker = StoryPageSpeaker(dialoguePage, awaitingIntervention);
                bool storyActionPlaying = WorldPresentationPlaying;
                Sprite storyPortrait = StoryPagePortrait(dialoguePage, awaitingIntervention);
                bool showLargeSubject = StoryPageUsesLargeSubject(dialoguePage, awaitingIntervention, storyPortrait);
                string presentedStory = dialoguePages[dialoguePage];
                if (awaitingIntervention && !string.IsNullOrWhiteSpace(_choiceStatusMessage))
                    presentedStory += "\n\n주의: " + _choiceStatusMessage;
                // A confirmed WORLD_ACTION gets an unobstructed field interstitial.
                // The same story page returns automatically when its short animation ends.
                // During a request the D20/action status owns the field. Keeping the
                // previous speaker cut-in or choice sheet visible here can completely
                // cover a target impact even though those controls are disabled.
                _sceneUi.PresentDialogue(!_dialoguePresenter.IsDismissed && !storyActionPlaying && !TurnPending,
                    storySpeaker,
                    presentedStory, storyActionPlaying ? "연출 중…" : hasNextDialogue ? "다음 ▶" : awaitingIntervention ? "직접 입력 ▶" : "대화 끝",
                    !storyActionPlaying,
                    storyPortrait, showLargeSubject);
                bool showNarrativeInput = !TurnPending && !_dialoguePresenter.IsDismissed &&
                                          (awaitingIntervention || _naturalLanguageComposeMode);
                _sceneUi.PresentDialogueChoices(showNarrativeInput,
                    awaitingIntervention ? CurrentNarrativeChoices() : Array.Empty<NarrativeChoiceOption>(),
                    showNarrativeInput && !TurnPending && !WorldPresentationPlaying);
                _inputRouter?.SetNarrativeOverlayMode(showNarrativeInput);
                _inputRouter?.SetNarrativeChoiceMode(awaitingIntervention && !_dialoguePresenter.IsDismissed && !TurnPending &&
                    !WorldPresentationPlaying && !_showPause && _screenMode == ScreenMode.Playing &&
                    !(_sceneUi?.IsDialogueInputFocused ?? false));
            }
            // All controls in this presentation pass describe the same flow snapshot.
            // Refresh it once after dialogue synchronization, then reuse it for HUD,
            // ability and confirm state instead of rebuilding dialogue pages per control.
            RefreshFlowPhaseForDialoguePageCount(dialoguePages.Length);
            bool selectionReady = CanSubmitCurrentSelectionForCurrentFlow();
            _sceneUi.PresentHud(CurrentAreaName(view) + " · " + CurrentBiomeLabel(view), StoryBeat(view),
                ObjectiveHudText(view),
                _playerWalking ? "선택한 경로를 따라 이동하고 있습니다." :
                TurnPending ? PendingActionGuidance() : ActionGuidanceTextForCurrentFlow(view, selectionReady));
            _sceneUi.PresentQuestStatus(QuestActionHintText(view));
            _sceneUi.PresentInventoryAndQuests(_runPresentationModel);

            SetAbilityButton(AbilityKind.Move);
            SetAbilityButton(AbilityKind.Copy);
            SetAbilityButton(AbilityKind.Delete);
            SetAbilityButton(AbilityKind.Connect);
            SetAbilityButton(AbilityKind.Restore);
            SetAbilityButton(AbilityKind.Undo);
            SetAbilityButton(AbilityKind.Search);
            SetAbilityButton(AbilityKind.SelectAll);
            _sceneUi.SetCopyPasteMode(_selectionController.CopySourceCaptured);
            _sceneUi.SetOutcomeEmote(_lastOutcome);
            string confirmLabel = TurnPending
                ? "처리 중…"
                : _actionConfirmation.IsArmed(Time.unscaledTime)
                ? "다시 눌러 확인"
                : _selectionController.Ability == AbilityKind.Interact ? "상호작용" : "실행";
            _sceneUi.PresentConfirm(confirmLabel,
                RunIsPlaying(view) && !_showPause && !TurnPending && !_playerWalking && selectionReady);
            _sceneUi.PresentEnding("코드리아의 결말",
                EndingTitle(EndingCode(view)) + "\n\n" + EndingDescription(EndingCode(view)) +
                "\n\n당신의 선택이 코드리아에 남긴 결말입니다.");
        }

        public void UiStartNewRun() { AuditUi("StartNewRun"); StartNewRun(); }
        public void UiContinueRun() { AuditUi("ContinueRun"); ContinueRun(); }
        public void UiCyclePoi(int direction) { AuditUi("CyclePoi", direction.ToString()); CyclePoi(direction); }
        public void UiSetAbility(AbilityKind ability)
        {
            AuditUi("SetAbility", ability.ToString());
            string blocked = AbilityInputBlockReason(ability);
            if (!string.IsNullOrEmpty(blocked))
            {
                Debug.LogWarning("[KW.Input] event=UiSetAbility ability=" + ability +
                                 " result=blocked reason=" + blocked);
                RejectBlockedGameplayInput(null);
                return;
            }
            if (ability == AbilityKind.Copy && _selectionController.Ability == AbilityKind.Copy && _selectionController.CopySourceCaptured &&
                _selectionController.SelectedCoord.HasValue && CanSubmitCurrentSelection())
            {
                SubmitImmediately();
                return;
            }
            SetAbility(ability);
            if (CanSubmitCurrentSelection()) SubmitImmediately();
        }

        /// <summary>Mouse/UI entry point. The choice id must match the currently displayed sealed set.</summary>
        public void UiSelectNarrativeChoice(string choiceId)
        {
            AuditUi("SelectNarrativeChoice", choiceId);
            NarrativeChoiceOption[] choices = CurrentNarrativeChoices();
            NarrativeChoiceOption selected = null;
            for (int i = 0; i < choices.Length; i++)
                if (string.Equals(choices[i]?.ChoiceId, choiceId, StringComparison.Ordinal))
                {
                    selected = choices[i];
                    break;
                }
            if (selected == null)
            {
                Debug.LogWarning("[KW.Choice] event=Select result=blocked reason=unknown-choice choiceId=" + choiceId);
                return;
            }
            SelectNarrativeChoice(selected);
        }

        /// <summary>Keyboard accessibility entry point for number keys 1-4.</summary>
        public void UiSelectNarrativeChoiceIndex(int index)
        {
            AuditUi("SelectNarrativeChoiceIndex", index.ToString());
            NarrativeChoiceOption[] choices = CurrentNarrativeChoices();
            if (index < 0 || index >= choices.Length || index >= 4) return;
            SelectNarrativeChoice(choices[index]);
        }

        public void UiMoveNarrativeChoice(int direction) { AuditUi("MoveNarrativeChoice", direction.ToString()); _sceneUi?.MoveDialogueChoiceSelection(direction); }
        public void UiConfirmNarrativeChoice() { AuditUi("ConfirmNarrativeChoice"); _sceneUi?.ConfirmDialogueChoiceSelection(); }

        public void UiToggleInventory()
        {
            AuditUi("ToggleInventory");
            if (_screenMode != ScreenMode.Playing || _showPause) return;
            bool open = _sceneUi != null && _sceneUi.ToggleInventory();
            _inputRouter?.SetUiOverlayMode(open);
        }

        public void UiToggleQuests()
        {
            AuditUi("ToggleQuests");
            if (_screenMode != ScreenMode.Playing || _showPause) return;
            bool open = _sceneUi != null && _sceneUi.ToggleQuests();
            _inputRouter?.SetUiOverlayMode(open);
        }

        public void UiSelectInventoryItem(string itemName)
        {
            AuditUi("SelectInventoryItem", itemName);
            if (_sceneUi == null || !_sceneUi.InsertInventoryItemIntoDialogue(itemName)) return;
            _sceneUi.CloseInventoryQuestOverlay();
            _inputRouter?.SetUiOverlayMode(false);
        }

        public void UiSubmitPlayerMessage(string text)
        {
            KeyboardWandererInputAudit.RecordTextSubmission("FreeformInput", text, InputAuditState());
            if (string.IsNullOrWhiteSpace(text) || _choiceSubmissionPending || TurnPending) return;
            if (!ContainsMeaningfulPlayerText(text))
            {
                PresentChoiceRejection("PLAYER_MESSAGE_INVALID", "문자나 숫자가 포함된 행동을 입력해 주세요.");
                return;
            }
            if (!_serverOnline || _serverRun == null || _gameApi == null)
            {
                PresentChoiceRejection("NETWORK_ERROR", "자연어 대화에는 실행 중인 권위 서버가 필요합니다.");
                return;
            }
            string normalizedText = text.Trim();
            ReservePlayerMessageSubmission(normalizedText, _serverRun.version);
            _naturalLanguageComposeMode = false;
            _choiceStatusMessage = string.Empty;
            StartCoroutine(SubmitServerPlayerMessage(normalizedText,
                _pendingPlayerMessageIdempotencyKey, _pendingPlayerMessageExpectedVersion));
        }

        private void SelectNarrativeChoice(NarrativeChoiceOption choice)
        {
            RefreshFlowPhase();
            bool awaiting = _flowStateMachine.CanSelectNarrativeChoice ||
                            _flowStateMachine.Phase == GameFlowPhase.AwaitingChoice ||
                            _flowStateMachine.Phase == GameFlowPhase.AwaitingEncounterChoice;
            if (!awaiting || choice == null || _choiceSubmissionPending || TurnPending)
            {
                Debug.LogWarning("[KW.Choice] event=Select result=blocked reason=" +
                                 (_choiceSubmissionPending || TurnPending ? "request-pending" : "not-at-choice-boundary") +
                                 " choiceId=" + (choice?.ChoiceId ?? "null"));
                return;
            }

            if (!string.IsNullOrWhiteSpace(_lastChoiceSetId))
            {
                if (!_serverOnline || _serverRun == null || _gameApi == null)
                {
                    PresentChoiceRejection("NETWORK_ERROR", "서버 선택 세트를 처리할 권위 서버가 없습니다.");
                    return;
                }
                SynchronizeSelectionWithNarrativeChoice(choice);
                _choiceStatusMessage = string.Empty;
                StartCoroutine(SubmitServerNarrativeChoice(choice));
                return;
            }

            // Compatibility path for old suggestedSkillIds responses. New server-sealed
            // SKILL/TRAVEL choices never enter this branch and always use /choices.
            if (string.Equals(choice.ChoiceKind, "TRAVEL", StringComparison.OrdinalIgnoreCase))
            {
                UiSetAbility(AbilityKind.Move);
                return;
            }
            if (choice.IsSkill && TryAbilityForSkillId(choice.SkillId, out AbilityKind ability))
            {
                UiSetAbility(ability);
                return;
            }
            PresentChoiceRejection("INVALID_CHOICE", "이전 버전 선택지를 현재 행동으로 변환할 수 없습니다.");
        }

        private void SynchronizeSelectionWithNarrativeChoice(NarrativeChoiceOption choice)
        {
            if (choice == null || !choice.IsSkill || _selectionController == null ||
                !TryAbilityForSkillId(choice.SkillId, out AbilityKind ability))
                return;
            // The sealed choice endpoint owns targets and mechanics, but the HUD still
            // reads the local SelectionController while the request is pending. Mirror
            // only the submitted ability so "조사" can never be presented as the stale
            // prior "이동" command during its D20/network wait.
            _selectionController.ResetSelection(ability);
            _pendingPresentationChanges |= PresentationChange.Selection | PresentationChange.Hud;
        }
        public void UiSubmit() { AuditUi("Submit"); Submit(); }
        public void UiAdvanceDialogue()
        {
            AuditUi("AdvanceDialogue");
            if (WorldPresentationPlaying) return;
            if (_tutorialPresenter.IsActive)
            {
                if (_tutorialPresenter.Advance(Mathf.Max(1, _sceneUi == null ? 0 : _sceneUi.TutorialPageCount)))
                {
                    _dialoguePresenter.Reset();
                    KeyboardWandererPreferences.SetInt(TutorialCompletedKey, 1);
                    KeyboardWandererPreferences.Save();
                }
                PlaySfx(AssetClip("UiAcceptSound"));
                PublishPresentationState(PresentationChange.Dialogue);
                return;
            }
            string[] pages = BuildDialoguePages(_service == null ? _lastNarrative :
                (_lastOutcome == "READY" || _lastOutcome == "RESTORED" ? CampaignPremise(_service.CurrentView) : ShortNarrative(_lastNarrative)));
            if (IsInterventionPage(_dialoguePresenter.Page, pages.Length))
            {
                // The visible "직접 입력" button must enter the same compose state as
                // the T shortcut. Previously it only queued focus; the focus coroutine
                // then rejected its own request because compose mode was still false.
                _naturalLanguageComposeMode = true;
                _choiceStatusMessage = string.Empty;
                PublishPresentationState(PresentationChange.Dialogue);
                QueueDialogueInputFocus();
                return;
            }
            if (!_dialoguePresenter.Advance(pages.Length))
            {
                _naturalLanguageComposeMode = false;
                _sceneUi?.SetStoryVisible(false);
            }
            PlaySfx(AssetClip("UiAcceptSound"));
            PublishPresentationState(PresentationChange.Dialogue);
        }
        public void UiResume()
        {
            AuditUi("Resume");
            SetPauseState(false);
            PublishPresentationState(PresentationChange.Screen | PresentationChange.Hud);
        }
        public void UiShowTitle() { AuditUi("ShowTitle"); ShowTitle(); }

        private void AuditUi(string control, string value = null)
        {
            KeyboardWandererInputAudit.Record("UI", control, "Activated", "UiAction", InputAuditState(), value);
        }

        private string InputAuditState()
        {
            return "screen=" + _screenMode + ";pause=" + _showPause + ";flow=" +
                   (_flowStateMachine == null ? "null" : _flowStateMachine.Phase.ToString()) +
                   ";serverOnline=" + _serverOnline + ";turnPending=" + TurnPending;
        }

        private void SetAbilityButton(AbilityKind ability)
        {
            _sceneUi.SetAbilityState(ability, _flowStateMachine.CanIssueAbility(ability) && IsOfferedAbility(ability),
                _selectionController.Ability == ability);
        }

        private string[] BuildDialoguePages(string narrative)
        {
            DialoguePageSource source = CurrentDialoguePageSource();
            if (DialoguePageCacheMatches(source, narrative))
                return _cachedDialoguePages;

            var pages = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            if (_lastStorySequence != null && _lastStorySequence.Length > 0)
            {
                for (int i = 0; i < _lastStorySequence.Length; i++)
                {
                    string text = _lastStorySequence[i]?.Text;
                    if (!string.IsNullOrWhiteSpace(text)) pages.Add(text.Trim());
                }
            }
            bool hasStoryDialogue = pages.Count == 0 && _lastDialogue != null && _lastDialogue.Length > 0 &&
                                    _selectionController.Ability == AbilityKind.Search;
            if (hasStoryDialogue)
            {
                for (int i = 0; i < _lastDialogue.Length; i++)
                    if (!string.IsNullOrWhiteSpace(_lastDialogue[i]) && seen.Add(_lastDialogue[i].Trim()))
                        pages.Add(_lastDialogue[i].Trim());
            }
            if (pages.Count == 0 && !hasStoryDialogue && !string.IsNullOrWhiteSpace(narrative) && seen.Add(narrative.Trim()))
                pages.Add(narrative.Trim());
            if (pages.Count == 0) pages.Add("……아직 눈에 띄는 변화는 없어. 조금 더 지켜보자.");
            if (!string.IsNullOrWhiteSpace(_lastNextInterventionReason))
                pages.Add(InterventionPageText());
            _cachedDialoguePages = pages.ToArray();
            _cachedDialogueSignature = string.Join("\u001f", _cachedDialoguePages);
            _cachedDialoguePageSource = source;
            _cachedDialogueNarrative = source == DialoguePageSource.Narrative
                ? narrative ?? string.Empty
                : string.Empty;
            _cachedDialogueStoryTexts = source == DialoguePageSource.StorySequence
                ? SnapshotStoryTexts(_lastStorySequence)
                : Array.Empty<string>();
            _cachedDialogueLines = source == DialoguePageSource.SearchDialogue
                ? SnapshotStrings(_lastDialogue)
                : Array.Empty<string>();
            _cachedDialogueInterventionReason = _lastNextInterventionReason ?? string.Empty;
            _cachedDialogueEncounterMoveRequired = _encounterMoveRequired;
            _cachedDialogueContinuesWithMovement = _lastPresentationContinuesWithMovement;
            _hasDialoguePageCache = true;
            _dialoguePageCacheRevision++;
            return _cachedDialoguePages;
        }

        private DialoguePageSource CurrentDialoguePageSource()
        {
            if (_lastStorySequence != null)
                for (int i = 0; i < _lastStorySequence.Length; i++)
                    if (!string.IsNullOrWhiteSpace(_lastStorySequence[i]?.Text))
                        return DialoguePageSource.StorySequence;
            if (_lastDialogue != null && _lastDialogue.Length > 0 &&
                _selectionController.Ability == AbilityKind.Search)
                return DialoguePageSource.SearchDialogue;
            return DialoguePageSource.Narrative;
        }

        private bool DialoguePageCacheMatches(DialoguePageSource source, string narrative)
        {
            if (!_hasDialoguePageCache || source != _cachedDialoguePageSource ||
                !string.Equals(_cachedDialogueInterventionReason, _lastNextInterventionReason ?? string.Empty,
                    StringComparison.Ordinal) ||
                _cachedDialogueEncounterMoveRequired != _encounterMoveRequired ||
                _cachedDialogueContinuesWithMovement != _lastPresentationContinuesWithMovement)
                return false;

            switch (source)
            {
                case DialoguePageSource.StorySequence:
                    return StoryTextsMatchSnapshot(_lastStorySequence, _cachedDialogueStoryTexts);
                case DialoguePageSource.SearchDialogue:
                    return StringsMatchSnapshot(_lastDialogue, _cachedDialogueLines);
                default:
                    return string.Equals(_cachedDialogueNarrative, narrative ?? string.Empty,
                        StringComparison.Ordinal);
            }
        }

        private static string[] SnapshotStoryTexts(StorySequencePage[] pages)
        {
            if (pages == null || pages.Length == 0) return Array.Empty<string>();
            var snapshot = new string[pages.Length];
            for (int i = 0; i < pages.Length; i++)
                snapshot[i] = pages[i]?.Text;
            return snapshot;
        }

        private static string[] SnapshotStrings(string[] values)
        {
            if (values == null || values.Length == 0) return Array.Empty<string>();
            var snapshot = new string[values.Length];
            Array.Copy(values, snapshot, values.Length);
            return snapshot;
        }

        private static bool StoryTextsMatchSnapshot(StorySequencePage[] pages, string[] snapshot)
        {
            int count = pages?.Length ?? 0;
            if (snapshot == null || count != snapshot.Length) return false;
            for (int i = 0; i < count; i++)
                if (!string.Equals(pages[i]?.Text, snapshot[i], StringComparison.Ordinal))
                    return false;
            return true;
        }

        private static bool StringsMatchSnapshot(string[] values, string[] snapshot)
        {
            int count = values?.Length ?? 0;
            if (snapshot == null || count != snapshot.Length) return false;
            for (int i = 0; i < count; i++)
                if (!string.Equals(values[i], snapshot[i], StringComparison.Ordinal))
                    return false;
            return true;
        }

        private string InterventionPageText()
        {
            string reason = _lastNextInterventionReason.Trim();
            if (_lastPresentationContinuesWithMovement)
                return reason + "\n\nWASD로 다음 흔적을 향해 계속 이동하세요.";
            if (_encounterMoveRequired)
                return reason + "\n\n어떤 방식으로 개입할까? 위의 선택지를 골라 주세요.";
            return reason + "\n\n어디로 이동하거나, 어떤 방식으로 개입할까? 위의 선택지를 골라 주세요.";
        }

        private bool IsInterventionPage(int page, int pageCount)
        {
            return !_lastPresentationContinuesWithMovement &&
                   !string.IsNullOrWhiteSpace(_lastNextInterventionReason) &&
                   pageCount > 0 && page == pageCount - 1;
        }

        private NarrativeChoiceOption[] CurrentNarrativeChoices()
        {
            if (_lastPresentationContinuesWithMovement)
                return Array.Empty<NarrativeChoiceOption>();
            if (_lastNarrativeChoices != null && _lastNarrativeChoices.Length > 0)
                return _lastNarrativeChoices;
            return ServerTurnPresentationAdapter.BuildNarrativeChoices(null,
                CurrentSuggestedSkillIds(), !_encounterMoveRequired);
        }

        private string StoryPageSpeaker(int page, bool interventionPage)
        {
            if (interventionPage) return "선택";
            if (_lastStorySequence != null && page >= 0 && page < _lastStorySequence.Length)
            {
                StorySequencePage beat = _lastStorySequence[page];
                if (!string.IsNullOrWhiteSpace(beat?.Speaker)) return beat.Speaker;
                if (string.Equals(beat?.Type, "WORLD_ACTION", StringComparison.OrdinalIgnoreCase)) return "코드리아";
                if (string.Equals(beat?.Type, "NARRATION", StringComparison.OrdinalIgnoreCase)) return "이야기";
            }
            return CampaignCatalog.ProtagonistName;
        }

        private Sprite StoryPagePortrait(int page, bool interventionPage)
        {
            if (interventionPage) return null;
            if (_lastStorySequence == null || page < 0 || page >= _lastStorySequence.Length)
                return _visualAssetLibrary?.PlayerSprite;
            StorySequencePage beat = _lastStorySequence[page];
            if (beat == null || string.Equals(beat.Type, "MONOLOGUE", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(beat.Type, "NARRATION", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(beat.Type, "WORLD_ACTION", StringComparison.OrdinalIgnoreCase))
                return _visualAssetLibrary?.PlayerSprite;
            if (Guid.TryParse(beat.SpeakerId, out Guid speakerId) && TryGetServerEntity(speakerId, out GameApiClient.EntitySnapshot entity))
            {
                ActorAnimationEntry actor = ActorAnimationForAsset(entity.assetId, entity.kind);
                return actor?.Portrait ?? actor?.DefaultSprite ?? SpriteForServerEntity(entity, false);
            }
            // A malformed or unavailable speaker portrait must not collapse the
            // dialogue back into the compact HUD layout. Keep the protagonist as
            // the visual point of view until the next resolvable actor speaks.
            return _visualAssetLibrary?.PlayerSprite;
        }

        private bool StoryPageUsesLargeSubject(int page, bool interventionPage, Sprite portrait)
        {
            if (interventionPage || portrait == null || _lastStorySequence == null ||
                page < 0 || page >= _lastStorySequence.Length)
                return false;
            return _lastStorySequence[page] != null;
        }

        private static string AbilityResultTitle(AbilityKind ability)
        {
            switch (ability)
            {
                case AbilityKind.Move: return "이동";
                case AbilityKind.Copy: return "복제";
                case AbilityKind.Delete: return "경계 설정";
                case AbilityKind.Connect: return "두 대상 연결";
                case AbilityKind.Restore: return "대상 복구";
                case AbilityKind.Undo: return "최근 행동 되돌리기";
                case AbilityKind.Search: return "대상 조사";
                case AbilityKind.SelectAll: return "주변 전체에 개입";
                case AbilityKind.Interact: return "상호작용";
                default: return "행동";
            }
        }

        private string NarrativeSourceLabel()
        {
            if (!GmEnabled) return "규칙 결과";
            return _lastNarrativeFallbackUsed ? "기본 이야기" : "AI 이야기";
        }

        private bool HasActionResultPage()
        {
            return !string.IsNullOrWhiteSpace(_lastOutcome) &&
                   !string.Equals(_lastOutcome, "READY", StringComparison.OrdinalIgnoreCase) &&
                   !string.Equals(_lastOutcome, "RESTORED", StringComparison.OrdinalIgnoreCase);
        }

        private string NarrativeSelectionHint()
        {
            if (!string.IsNullOrWhiteSpace(_selectionController.Feedback))
                return _selectionController.Feedback;
            switch (_selectionController.Ability)
            {
                case AbilityKind.Move: return "이동할 타일을 클릭하면 캐릭터가 즉시 길을 따라 걷습니다.";
                case AbilityKind.Connect: return _serverOnline ? "C로 개입하면 연결할 대상과 사건이 결정됩니다." : "이어 주고 싶은 두 대상을 지도에서 차례로 고르세요.";
                case AbilityKind.Delete: return _serverOnline ? "R로 개입하면 공격할 대상과 사건이 결정됩니다." : "공격할 적 또는 시스템 노드를 클릭하면 즉시 공격합니다.";
                case AbilityKind.Restore: return _serverOnline ? "X로 개입하면 복구할 흔적과 사건이 결정됩니다." : "복구 가능한 대상이 생기면 대상이 자동 선택됩니다.";
                case AbilityKind.Interact: return "가까운 대상을 클릭하면 즉시 상호작용합니다.";
                case AbilityKind.Undo: return "Z로 최근 의미 턴 2회의 상태를 시간 역행합니다.";
                case AbilityKind.Search: return _serverOnline ? "F로 개입하면 발견할 대상이나 사건이 결정됩니다." : "F를 누른 뒤 조사할 대상을 클릭하면 즉시 조사합니다.";
                case AbilityKind.SelectAll: return "Ctrl A로 주변 4칸의 모든 적을 광범위 공격합니다.";
                case AbilityKind.Copy: return _serverOnline ? "E로 개입하면 복제할 대상이나 주변 데이터 사건이 결정됩니다." : _selectionController.CopySourceCaptured
                    ? "Ctrl V 상태입니다. 복제본을 놓을 빈 타일을 고른 뒤 실행하세요."
                    : "E 상태입니다. 복제할 대상을 먼저 고르세요.";
                default: return "이 선택을 적용할 대상을 지도에서 고르세요.";
            }
        }

        public void UiOpenSettings()
        {
            AuditUi("OpenSettings");
            _settingsReturn = _screenMode;
            _settingsReturnToPause = false;
            _screenMode = ScreenMode.Settings;
            _inputRouter?.SetUiOverlayMode(true);
            PublishPresentationState(PresentationChange.Screen);
        }

        public void UiOpenSettingsFromPause()
        {
            AuditUi("OpenSettingsFromPause");
            _settingsReturnToPause = true;
            _settingsReturn = ScreenMode.Playing;
            _screenMode = ScreenMode.Settings;
            _inputRouter?.SetUiOverlayMode(true);
            PublishPresentationState(PresentationChange.Screen);
        }

        public void UiCloseSettings()
        {
            AuditUi("CloseSettings");
            _screenMode = _settingsReturn;
            bool restorePause = _screenMode == ScreenMode.Playing && _settingsReturnToPause;
            _settingsReturnToPause = false;
            SetPauseState(restorePause);
            _inputRouter?.SetUiOverlayMode(_screenMode != ScreenMode.Playing || _showPause);
            if (_screenMode == ScreenMode.Playing)
                _cameraController?.SetEnabled(true);
            PublishPresentationState(PresentationChange.Screen);
        }

        public void UiDeleteSave()
        {
            AuditUi("DeleteSave");
            _runSessionController?.DeleteSave();
        }

        public void UiSetMusicVolume(float value)
        {
            AuditUi("SetMusicVolume", value.ToString("0.000"));
            _settingsController?.SetMusicVolume(value);
        }

        public void UiSetSfxVolume(float value)
        {
            AuditUi("SetSfxVolume", value.ToString("0.000"));
            _settingsController?.SetSfxVolume(value);
        }

        public void UiSetGmEnabled(bool value)
        {
            AuditUi("SetGmEnabled", value.ToString());
            _settingsController?.SetGmEnabled(value);
        }

        private bool CanSubmitCurrentSelection()
        {
            RefreshFlowPhase();
            return CanSubmitCurrentSelectionForCurrentFlow();
        }

        private bool CanSubmitCurrentSelectionForCurrentFlow()
        {
            if (!string.IsNullOrEmpty(_flowStateMachine.BlockReason())) return false;
            if (!_flowStateMachine.CanIssueAbility(_selectionController.Ability)) return false;
            if (_selectionController.Ability == AbilityKind.Move) return _selectionController.SelectedCoord.HasValue;
            if (_serverOnline && IsServerDirectedSkill(_selectionController.Ability))
            {
                RunPresentationModel run = PresentationModel(_service?.CurrentView);
                int cost = _abilityAvailability == null ? 0 : _abilityAvailability.FocusCost(_selectionController.Ability);
                return run != null && run.Focus >= cost;
            }
            if (_selectionController.Ability == AbilityKind.Undo || _selectionController.Ability == AbilityKind.SelectAll)
                return IsSkillEnabledForCurrentTarget(_selectionController.Ability);
            if (_selectionController.Ability == AbilityKind.Copy)
                return _selectionController.SelectedTarget.HasValue && _selectionController.SelectedCoord.HasValue &&
                       IsSkillEnabledForCurrentTarget(_selectionController.Ability);
            if (_selectionController.Ability == AbilityKind.Connect)
                return _selectionController.SelectedTarget.HasValue && _selectionController.SelectedSecondaryTarget.HasValue &&
                       IsSkillEnabledForCurrentTarget(_selectionController.Ability);
            return _selectionController.SelectedTarget.HasValue && IsSkillEnabledForCurrentTarget(_selectionController.Ability);
        }

        private static bool IsServerDirectedSkill(AbilityKind ability)
        {
            return ability == AbilityKind.Copy || ability == AbilityKind.Delete ||
                   ability == AbilityKind.Connect || ability == AbilityKind.Restore ||
                   ability == AbilityKind.Search;
        }

        private bool IsSkillEnabledForCurrentTarget(AbilityKind skill)
        {
            if (_service == null || _abilityAvailability == null)
                return false;
            _runPresentationModel = PresentationModel(_service.CurrentView);
            return _abilityAvailability.CanUse(skill, _runPresentationModel, _selectionController,
                _lastRestorableTarget, AvailableLocalUndoTurns());
        }

        /// <summary>로컬 Undo 판정에 필요한 미소비 기록 수만 계산해 공통 판정 컴포넌트에 전달한다.</summary>
        private int AvailableLocalUndoTurns()
        {
            if (_serverOnline || _service == null)
                return 0;
            RunState state = _service.CreateSnapshot();
            int available = 0;
            for (int i = state.ReversibleHistory.Count - 1; i >= 0 && available < 2; i--)
                if (!state.ReversibleHistory[i].IsConsumed)
                    available++;
            return available;
        }

        /// <summary>
        /// 화면에 상시 노출되던 Selection Panel은 제거되었지만, 서버 지시 스킬과
        /// 로컬 대상 선택의 안내 문구가 갈라지는 지점은 여전히 회귀 테스트가 검증하므로 남겨 둔다.
        /// </summary>
        private string SelectionStatusDetail(RunView view)
        {
            RunPresentationModel run = PresentationModel(view);
            int cost = _abilityAvailability == null ? 0 :
                _abilityAvailability.FocusCost(_selectionController.Ability);
            if (run.Focus < cost)
                return "사용 불가 · 포커스 " + cost + " 필요 (현재 " + run.Focus + ")";
            if (!string.IsNullOrWhiteSpace(_selectionController.Feedback))
                return _selectionController.Feedback;
            if (_serverOnline && IsServerDirectedSkill(_selectionController.Ability))
                return "사용 가능 · 누적된 이야기와 현재 장면을 바탕으로 AI가 개입 대상과 사건을 정합니다.\n" +
                       SelectionExecutionSummary();
            if (_selectionController.Ability == AbilityKind.Undo)
                return IsSkillEnabledForCurrentTarget(_selectionController.Ability)
                    ? "직전 의미 턴 2개를 역순으로 복구합니다.\n" + SelectionExecutionSummary()
                    : "사용 불가 · 되돌릴 수 있는 의미 턴이 2개 필요합니다.";
            if (_selectionController.Ability == AbilityKind.SelectAll)
                return IsSkillEnabledForCurrentTarget(_selectionController.Ability)
                    ? "사용 가능 · 반경 4칸의 모든 적에게 피해 3\n" + SelectionExecutionSummary()
                    : "사용 불가 · 반경 4칸 안에 적이 없습니다.";
            if (_selectionController.Ability == AbilityKind.Move)
                return _selectionController.SelectedCoord.HasValue
                    ? "목적지 " + _selectionController.SelectedCoord.Value + "\n" + SelectionExecutionSummary()
                    : "지도에서 이동할 빈 타일을 선택하세요.";

            RunPresentationEntity target = run.FindEntity(_selectionController.SelectedTarget);
            if (target == null)
                return _selectionController.Ability == AbilityKind.Connect ? "첫 번째와 두 번째 대상을 차례로 선택하세요."
                    : _selectionController.Ability == AbilityKind.Copy ? "복제할 오브젝트를 선택하세요. 조사 효과는 없습니다."
                    : _selectionController.Ability == AbilityKind.Search ? "조사할 대상 하나를 선택하세요."
                    : _selectionController.Ability == AbilityKind.Delete ? "삭제할 적 또는 시스템 노드 하나를 선택하세요."
                    : "대상을 선택하세요.";

            int distance = run.DistanceFromPlayer(target);
            string targetLine = target.Name + " · " + target.KindLabel +
                                (target.MaxHealth > 0 ? " · HP " + target.Health + "/" + target.MaxHealth : string.Empty) +
                                (distance >= 0 ? " · 거리 " + distance : string.Empty);
            if (target.Kind == RunPresentationEntityKind.Enemy)
                targetLine += "\n" + EnemyArchetypeCatalog.PlayerIntent(
                    EnemyArchetypeCatalog.Resolve(target.AssetId, view.WorldSeed, target.Id));
            if (_selectionController.Ability == AbilityKind.Connect && _selectionController.SelectedSecondaryTarget.HasValue)
            {
                RunPresentationEntity secondary = run.FindEntity(_selectionController.SelectedSecondaryTarget);
                targetLine += "\n연결 대상 · " + (secondary?.Name ?? "알 수 없는 대상");
            }
            if (!string.IsNullOrWhiteSpace(target.RequiredSkillId) &&
                !string.Equals(target.RequiredSkillId, _selectionController.Ability.ToString(), StringComparison.OrdinalIgnoreCase))
                return "사용 불가 · 이 대상은 " + RequiredSkillPlayerLabel(target.RequiredSkillId) +
                       " 스킬이 필요합니다.\n" + targetLine;
            if (!IsSkillEnabledForCurrentTarget(_selectionController.Ability)) return "사용 불가 · " + targetLine;
            if (_selectionController.Ability == AbilityKind.Copy && !_selectionController.SelectedCoord.HasValue)
                return targetLine + "\n원본은 유지됩니다. 복제본을 놓을 빈 타일을 선택하세요.";
            if (_selectionController.Ability == AbilityKind.Connect && !_selectionController.SelectedSecondaryTarget.HasValue)
                return targetLine + "\n두 번째 연결 대상을 선택하세요.";
            return "선택됨 · " + targetLine + ObjectiveSelectionContext(run, target) +
                   "\n" + SelectionExecutionSummary();
        }

        private string ObjectiveSelectionContext(RunPresentationModel run, RunPresentationEntity selected)
        {
            if (_selectionController.Ability != AbilityKind.Search || selected == null ||
                !run.ObjectiveTargetId.HasValue)
                return string.Empty;
            if (selected.Id == run.ObjectiveTargetId.Value)
                return "\n현재 이야기 목표";
            return "\n조사 가능 · 현재 이야기 목표는 " +
                   FirstNonEmpty(run.ObjectiveTargetName, "표시된 대상") + "입니다.";
        }

        private static string RequiredSkillPlayerLabel(string skillId)
        {
            switch ((skillId ?? string.Empty).Trim().ToUpperInvariant())
            {
                case "RESTORE": return "X 복구";
                case "SEARCH": return "F 조사";
                case "CONNECT": return "C 연결";
                case "COPY": return "E 복제";
                case "DELETE": return "R 공격";
                default: return "다른 관리자 키보드";
            }
        }

        private string SelectionExecutionSummary()
        {
            if (_selectionController.Ability == AbilityKind.Move)
                return "실행 · 의미 턴 소비 없음 · 이동 중 사건 발생 가능";
            string risk = _selectionController.Ability == AbilityKind.Delete || _selectionController.Ability == AbilityKind.Undo
                ? "높은 위험"
                : _selectionController.Ability == AbilityKind.Connect ? "관계 변화 가능" : "판정 결과 적용";
            return "실행 · 의미 턴 1 + D20 · " + risk;
        }

        private void ConfigureInput()
        {
            DisconnectInput();
            _inputRouter = authoredInputRouter;
            if (_inputRouter == null)
                return;
            _inputRouter.PauseRequested += HandlePauseRequested;
            _inputRouter.NarrativeChoiceRequested += UiSelectNarrativeChoiceIndex;
            _inputRouter.NarrativeChoiceMoveRequested += UiMoveNarrativeChoice;
            _inputRouter.NarrativeChoiceConfirmRequested += UiConfirmNarrativeChoice;
            _inputRouter.AbilityRequested += HandleAbilityRequested;
            _inputRouter.PasteRequested += HandlePasteRequested;
            _inputRouter.PoiCycleRequested += HandlePoiCycleRequested;
            _inputRouter.SubmitRequested += HandleSubmitRequested;
            _inputRouter.InventoryRequested += UiToggleInventory;
            _inputRouter.QuestRequested += UiToggleQuests;
            _inputRouter.WorldClickRequested += HandleMapClick;
            _inputRouter.DirectionalMoveRequested += HandleDirectionalMoveRequested;
            _inputRouter.DirectionalMoveReleased += HandleDirectionalMoveReleased;
            _inputRouter.NaturalLanguageRequested += HandleNaturalLanguageRequested;
        }

        /// <summary>도메인 리로드나 오브젝트 파괴 뒤 입력 이벤트가 중복 호출되지 않도록 연결을 해제한다.</summary>
        private void DisconnectInput()
        {
            if (_inputRouter == null)
                return;
            _inputRouter.PauseRequested -= HandlePauseRequested;
            _inputRouter.NarrativeChoiceRequested -= UiSelectNarrativeChoiceIndex;
            _inputRouter.NarrativeChoiceMoveRequested -= UiMoveNarrativeChoice;
            _inputRouter.NarrativeChoiceConfirmRequested -= UiConfirmNarrativeChoice;
            _inputRouter.AbilityRequested -= HandleAbilityRequested;
            _inputRouter.PasteRequested -= HandlePasteRequested;
            _inputRouter.PoiCycleRequested -= HandlePoiCycleRequested;
            _inputRouter.SubmitRequested -= HandleSubmitRequested;
            _inputRouter.InventoryRequested -= UiToggleInventory;
            _inputRouter.QuestRequested -= UiToggleQuests;
            _inputRouter.WorldClickRequested -= HandleMapClick;
            _inputRouter.DirectionalMoveRequested -= HandleDirectionalMoveRequested;
            _inputRouter.DirectionalMoveReleased -= HandleDirectionalMoveReleased;
            _inputRouter.NaturalLanguageRequested -= HandleNaturalLanguageRequested;
            _inputRouter = null;
        }

        private bool CanHandleGameplayInput()
        {
            return string.IsNullOrEmpty(GameplayInputBlockReason());
        }

        private string GameplayInputBlockReason()
        {
            RefreshFlowPhase();
            return _flowStateMachine.BlockReason();
        }

        private string AbilityInputBlockReason(AbilityKind ability)
        {
            RefreshFlowPhase();
            return _flowStateMachine.BlockReason(ability);
        }

        private void RejectBlockedGameplayInput(string playerFacingReason)
        {
            if (_selectionController != null)
                _selectionController.Reject(string.IsNullOrWhiteSpace(playerFacingReason)
                    ? PlayerFacingFlowBlockReason()
                    : playerFacingReason);
            PlaySfx(AssetClip("UiCancelSound"));
            PublishPresentationState(PresentationChange.Hud | PresentationChange.Selection);
        }

        private string PlayerFacingFlowBlockReason()
        {
            switch (_flowStateMachine.Phase)
            {
                case GameFlowPhase.Paused: return "일시정지 메뉴를 닫은 뒤 다시 입력해 주세요.";
                case GameFlowPhase.Settings: return "설정을 닫고 게임으로 돌아오면 행동할 수 있습니다.";
                case GameFlowPhase.Tutorial: return "튜토리얼 안내를 먼저 확인해 주세요.";
                case GameFlowPhase.ResolvingChoice: return "이전 입력을 처리하고 있습니다. 결과가 표시될 때까지 기다려 주세요.";
                case GameFlowPhase.Traveling: return "현재 이동이 끝난 뒤 다음 방향을 입력해 주세요.";
                case GameFlowPhase.PresentingWorldAction: return "현재 연출이 끝난 뒤 다음 행동을 선택할 수 있습니다.";
                case GameFlowPhase.PresentingStory: return "대화를 확인한 뒤 마지막 선택 페이지에서 행동해 주세요.";
                case GameFlowPhase.Ended: return "이 런은 종료되었습니다. 새 게임이나 타이틀로 이동해 주세요.";
                case GameFlowPhase.Title: return "먼저 새 게임 또는 이어하기를 선택해 주세요.";
                case GameFlowPhase.WaitingForNarrative: return "다음 선택 내용을 준비하고 있습니다. 잠시 후 다시 시도해 주세요.";
                default: return "현재 장면에서는 이 행동을 사용할 수 없습니다.";
            }
        }

        private void RefreshFlowPhase()
        {
            string[] dialoguePages = BuildDialoguePages(_service == null ? _lastNarrative :
                (_lastOutcome == "READY" || _lastOutcome == "RESTORED"
                    ? CampaignPremise(_service.CurrentView)
                    : ShortNarrative(_lastNarrative)));
            RefreshFlowPhaseForDialoguePageCount(dialoguePages.Length);
        }

        private void RefreshFlowPhaseForDialoguePageCount(int dialoguePageCount)
        {
            _flowPhaseRefreshRevision++;
            bool playingScreen = _screenMode == ScreenMode.Playing;
            bool runActive = _service != null && RunIsPlaying(_service.CurrentView);
            bool atIntervention = IsInterventionPage(_dialoguePresenter.Page, dialoguePageCount);
            bool awaitingIntervention = atIntervention && !_dialoguePresenter.IsDismissed;
            // A dismissed dialogue surface hands control back to world exploration.
            // The server can replace a one-tile travel narrative while the walk is
            // playing, which changes the page count without reopening the surface.
            // Using only the old page index then left the visible world actionable
            // while the flow machine stayed in WaitingForNarrative forever.
            bool actionableBoundary = awaitingIntervention || _dialoguePresenter.IsDismissed;
            bool storyVisible = !_tutorialPresenter.IsActive && _lastStorySequence != null &&
                                _lastStorySequence.Length > 0 && !_dialoguePresenter.IsDismissed &&
                                !awaitingIntervention;
            bool worldAction = WorldPresentationPlaying;
            _flowStateMachine.Refresh(new GameFlowSignals(
                _screenMode == ScreenMode.Title,
                _screenMode == ScreenMode.Settings,
                playingScreen,
                runActive,
                _showPause,
                _tutorialPresenter.IsActive,
                TurnPending,
                _playerWalking,
                worldAction,
                storyVisible,
                actionableBoundary,
                _encounterMoveRequired,
                awaitingIntervention && HasSealedNarrativeChoiceSet()));
        }

        private bool HasSealedNarrativeChoiceSet()
        {
            return !string.IsNullOrWhiteSpace(_lastChoiceSetId) &&
                   _lastNarrativeChoices != null &&
                   (_lastNarrativeChoices.Length >= 2 || IsMandatoryOpeningAttackChoice());
        }

        private void HandlePauseRequested()
        {
            // Escape from settings follows the same authored Back path as the button.
            // When settings came from Pause, UiCloseSettings restores the paused screen
            // and keeps the simulation frozen.
            if (_screenMode == ScreenMode.Settings)
            {
                UiCloseSettings();
                PlaySfx(AssetClip("UiCancelSound"));
                return;
            }
            if (_screenMode != ScreenMode.Playing || _service == null)
                return;
            if (_showPause)
            {
                SetPauseState(false);
                PlaySfx(AssetClip("UiAcceptSound"));
                PublishPresentationState(PresentationChange.Screen | PresentationChange.Hud);
                return;
            }
            if (_sceneUi != null && _sceneUi.CloseInventoryQuestOverlay())
            {
                _inputRouter?.SetUiOverlayMode(false);
                PlaySfx(AssetClip("UiCancelSound"));
                return;
            }
            if (_actionConfirmation.IsArmed(Time.unscaledTime))
            {
                _actionConfirmation.Cancel();
                _gameplayTelemetry.RecordCancellation();
                _selectionController.Reject("고위험 행동 확인을 취소했습니다.");
                PlaySfx(AssetClip("UiCancelSound"));
                PublishPresentationState(PresentationChange.Hud);
                return;
            }
            // An authoritative action that has already left the client must finish as
            // one atomic commit. Explain this short lock instead of opening a pause
            // screen whose background state can change beneath the player.
            if (_serverPending || _choiceSubmissionPending ||
                (_turnCoordinator != null && _turnCoordinator.IsPending) ||
                WorldPresentationPlaying)
            {
                RejectBlockedGameplayInput("현재 행동 결과를 확정하는 중입니다. 연출이 끝나면 일시정지할 수 있습니다.");
                return;
            }
            SetPauseState(true);
            PlaySfx(AssetClip("UiCancelSound"));
            PublishPresentationState(PresentationChange.Screen | PresentationChange.Hud);
        }

        private void SetPauseState(bool paused)
        {
            if (_showPause == paused)
                return;
            if (paused)
            {
                _showPause = true;
                _pauseStartedAt = Time.unscaledTime;
                _inputRouter?.SetUiOverlayMode(true);
                return;
            }

            float pausedDuration = Mathf.Max(0f, Time.unscaledTime - _pauseStartedAt);
            _showPause = false;
            _pauseStartedAt = 0f;
            // All controller-owned unscaled deadlines are shifted so a pause cannot
            // silently expire a movement/action/camera cue or trigger ambient mutation
            // immediately on the first resumed frame.
            _nextAmbientWanderAt += pausedDuration;
            _nextDecorationOcclusionAt += pausedDuration;
            if (_cameraInspectUntil > 0f) _cameraInspectUntil += pausedDuration;
            if (_playerActionUntil > 0f) _playerActionUntil += pausedDuration;
            _inputRouter?.SetUiOverlayMode(_screenMode != ScreenMode.Playing ||
                                            (_sceneUi?.IsInventoryQuestOverlayOpen ?? false));
        }

        private void HandleAbilityRequested(AbilityKind ability)
        {
            if (TrySubmitSealedSkillChoice(ability))
                return;
            string blocked = AbilityInputBlockReason(ability);
            if (!string.IsNullOrEmpty(blocked))
            {
                Debug.LogWarning("[KW.Input] event=AbilityRequested ability=" + ability + " result=blocked reason=" + blocked +
                                 " inputId=" + KeyboardWandererInputAudit.CurrentInputId);
                RejectBlockedGameplayInput(null);
                return;
            }
            Debug.Log("[KW.Input] event=AbilityRequested ability=" + ability + " result=accepted inputId=" +
                      KeyboardWandererInputAudit.CurrentInputId);
            SetAbility(ability);
            if (CanSubmitCurrentSelection()) SubmitImmediately();
        }

        private bool TrySubmitSealedSkillChoice(AbilityKind ability)
        {
            if (string.IsNullOrWhiteSpace(_lastChoiceSetId) || _lastNarrativeChoices == null ||
                _lastNarrativeChoices.Length == 0 || _dialoguePresenter == null)
                return false;

            // POI cycling deliberately lets the player leave a sealed choice surface
            // and continue moving; safe travel atomically records that choice as
            // skipped on the server. A keyboard skill is different: the action API
            // preserves the story boundary and rejects it with CHOICE_REQUIRED. Do
            // not send a request that the authoritative server must reject, and do
            // not let a hidden choice look like a silent, usable world shortcut.
            if (_dialoguePresenter.IsDismissed)
            {
                if (!HasPendingServerNarrativeChoice())
                    return false;
                ReopenPendingNarrativeChoice(
                    "이야기 선택이 남아 있습니다. 표시된 선택지를 고르거나 T로 직접 입력한 뒤 스킬을 사용해 주세요.");
                return true;
            }

            // A shortcut may only resolve the final, visibly presented intervention.
            // Earlier story pages must remain readable and own their input boundary.
            if (!IsVisibleInterventionPage())
                return false;

            NarrativeChoiceOption match = null;
            for (int i = 0; i < _lastNarrativeChoices.Length; i++)
            {
                NarrativeChoiceOption choice = _lastNarrativeChoices[i];
                if (choice != null && choice.IsSkill && TryAbilityForSkillId(choice.SkillId, out AbilityKind offered) &&
                    offered == ability)
                {
                    match = choice;
                    break;
                }
            }

            if (match == null)
            {
                string[] pages = BuildDialoguePages(_lastNarrative);
                _dialoguePresenter.ShowLast(pages.Length);
                _naturalLanguageComposeMode = false;
                RejectBlockedGameplayInput(AbilityPlayerLabel(ability) +
                    "은 현재 서버 선택지에 없습니다. 표시된 선택지 중 하나를 고르거나 T로 직접 입력해 주세요.");
                PublishPresentationState(PresentationChange.Dialogue | PresentationChange.Hud |
                                         PresentationChange.Selection);
                return true;
            }

            if (_choiceSubmissionPending || TurnPending)
            {
                RejectBlockedGameplayInput("이전 선택을 처리하고 있습니다. 결과가 표시될 때까지 기다려 주세요.");
                return true;
            }

            Debug.Log("[KW.Input] event=AbilityRequested ability=" + ability +
                      " result=sealed-choice choiceId=" + match.ChoiceId + " inputId=" +
                      KeyboardWandererInputAudit.CurrentInputId);
            SynchronizeSelectionWithNarrativeChoice(match);
            _choiceStatusMessage = string.Empty;
            StartCoroutine(SubmitServerNarrativeChoice(match));
            return true;
        }

        private bool HasPendingServerNarrativeChoice()
        {
            string pendingChoiceSetId = _serverRun?.pendingChoiceSet?.choiceSetId;
            return _serverOnline && !string.IsNullOrWhiteSpace(pendingChoiceSetId) &&
                   string.Equals(pendingChoiceSetId, _lastChoiceSetId, StringComparison.Ordinal) &&
                   _lastNarrativeChoices != null && _lastNarrativeChoices.Length > 0;
        }

        private bool IsMandatoryOpeningAttackChoice()
        {
            if (_lastNarrativeChoices == null || _lastNarrativeChoices.Length != 1)
                return false;
            NarrativeChoiceOption choice = _lastNarrativeChoices[0];
            return choice != null && choice.IsSkill &&
                   string.Equals(choice.ChoiceId, "opening.attack", StringComparison.Ordinal) &&
                   string.Equals(choice.SkillId, "DELETE", StringComparison.OrdinalIgnoreCase);
        }

        private void RejectOpeningTutorialBypass()
        {
            const string guidance = "첫 전투를 먼저 끝내야 합니다. R 키로 눈앞의 몬스터를 공격하세요.";
            if (_dialoguePresenter != null && _dialoguePresenter.IsDismissed)
                ReopenPendingNarrativeChoice(guidance);
            else
            {
                _selectionController?.Reject(guidance);
                _choiceStatusMessage = guidance;
                PlaySfx(AssetClip("UiCancelSound"));
                PublishPresentationState(PresentationChange.Dialogue | PresentationChange.Hud |
                                         PresentationChange.Selection);
            }
        }

        private void ReopenPendingNarrativeChoice(string playerMessage)
        {
            string[] pages = BuildDialoguePages(_lastNarrative);
            _naturalLanguageComposeMode = false;
            _choiceStatusMessage = playerMessage ?? string.Empty;
            _dialoguePresenter.ShowLast(pages.Length);
            _sceneUi?.SetStoryVisible(true);
            _selectionController?.Reject(playerMessage);
            PlaySfx(AssetClip("UiCancelSound"));
            PublishPresentationState(PresentationChange.Dialogue | PresentationChange.Hud |
                                     PresentationChange.Selection);
        }

        private void HandlePasteRequested()
        {
            if (!CanHandleGameplayInput())
            {
                RejectBlockedGameplayInput(null);
                return;
            }
            if (_selectionController.Ability == AbilityKind.Copy && _selectionController.CopySourceCaptured &&
                _selectionController.SelectedCoord.HasValue) SubmitImmediately();
            else RejectBlockedGameplayInput("먼저 복제할 대상과 배치할 빈 타일을 차례로 선택해 주세요.");
        }

        private void HandlePoiCycleRequested(int direction)
        {
            if (!CanHandleGameplayInput())
            {
                RejectBlockedGameplayInput(null);
                return;
            }
            if (IsMandatoryOpeningAttackChoice())
            {
                RejectOpeningTutorialBypass();
                return;
            }
            ClaimWorldNavigationInput();
            CyclePoi(direction);
        }

        private void ClaimWorldNavigationInput()
        {
            RefreshFlowPhase();
            // A visible intervention may be left through either POI navigation or a
            // direct map click. Close the modal before selecting a destination so the
            // next Enter confirms travel instead of accidentally choosing option 1.
            if (_flowStateMachine.Phase == GameFlowPhase.AwaitingNarrativeChoice ||
                IsVisibleInterventionPage())
                DismissNarrativeChoicesForGameplay();
        }

        private bool IsVisibleInterventionPage()
        {
            if (_dialoguePresenter == null || _dialoguePresenter.IsDismissed)
                return false;
            string narrative = _service == null ? _lastNarrative :
                (_lastOutcome == "READY" || _lastOutcome == "RESTORED"
                    ? CampaignPremise(_service.CurrentView)
                    : ShortNarrative(_lastNarrative));
            string[] pages = BuildDialoguePages(narrative);
            return IsInterventionPage(_dialoguePresenter.Page, pages.Length);
        }

        private void DismissNarrativeChoicesForGameplay()
        {
            _naturalLanguageComposeMode = false;
            _dialoguePresenter.Dismiss();
            _sceneUi?.PresentDialogueChoices(false, Array.Empty<NarrativeChoiceOption>(), false);
            _inputRouter?.SetNarrativeChoiceMode(false);
            _inputRouter?.SetNarrativeOverlayMode(false);
            PublishPresentationState(PresentationChange.Dialogue | PresentationChange.Selection);
        }

        private void HandleSubmitRequested()
        {
            if (CanHandleGameplayInput()) Submit();
            else RejectBlockedGameplayInput(null);
        }

        private void HandleDirectionalMoveRequested(Vector2Int direction)
        {
            _directionalInputHeld = true;
            if (Mathf.Abs(direction.x) + Mathf.Abs(direction.y) != 1)
            {
                ClearBufferedDirectionalMove();
                RejectBlockedGameplayInput("한 번에 상하좌우 한 방향으로만 이동할 수 있습니다.");
                return;
            }
            if (IsMandatoryOpeningAttackChoice())
            {
                ClearBufferedDirectionalMove();
                RejectOpeningTutorialBypass();
                return;
            }
            // Optional narrative choices must not steal the primary movement keys.
            // A fresh WASD press explicitly hands control back to exploration before
            // the flow-state ability guard is evaluated.
            if (_flowStateMachine.Phase == GameFlowPhase.AwaitingNarrativeChoice ||
                IsVisibleInterventionPage())
                DismissNarrativeChoicesForGameplay();
            if (_lastPresentationContinuesWithMovement && !_dialoguePresenter.IsDismissed)
                DismissNarrativeChoicesForGameplay();
            string blocked = AbilityInputBlockReason(AbilityKind.Move);
            if (!string.IsNullOrEmpty(blocked) || _service?.CurrentView == null)
            {
                if (_service?.CurrentView != null &&
                    (_playerWalking || TurnPending || _flowStateMachine.Phase == GameFlowPhase.Traveling ||
                     _flowStateMachine.Phase == GameFlowPhase.ResolvingChoice ||
                     (_lastPresentationContinuesWithMovement &&
                      (_flowStateMachine.Phase == GameFlowPhase.PresentingStory || WorldPresentationPlaying))))
                {
                    _bufferedMoveDirection = direction;
                    _hasBufferedDirectionalMove = true;
                    _selectionController?.Reject("연속 이동 예약 · 현재 칸이 끝나면 다음 방향으로 이어갑니다.");
                    PublishPresentationState(PresentationChange.Hud | PresentationChange.Selection);
                    Debug.Log("[KW.Input] event=DirectionalMove result=buffered direction=" + direction +
                              " inputId=" + KeyboardWandererInputAudit.CurrentInputId);
                    return;
                }
                ClearBufferedDirectionalMove();
                Debug.LogWarning("[KW.Input] event=DirectionalMove result=blocked reason=" +
                                 (string.IsNullOrEmpty(blocked) ? "run-not-ready" : blocked) +
                                 " inputId=" + KeyboardWandererInputAudit.CurrentInputId);
                RejectBlockedGameplayInput(null);
                return;
            }
            _hasBufferedDirectionalMove = false;
            RunView view = _service.CurrentView;
            if (!TryGetPlayerPosition(view, out GridCoord player))
            {
                RejectBlockedGameplayInput("현재 플레이어 위치를 동기화하지 못했습니다. 잠시 후 다시 시도해 주세요.");
                return;
            }
            var destination = new GridCoord(player.X + direction.x, player.Y + direction.y);
            if (!CanSelectMovementDestination(view, destination))
            {
                _selectionController.Reject("그 방향으로는 이동할 수 없습니다.");
                PlaySfx(AssetClip("UiCancelSound"));
                PublishPresentationState(PresentationChange.Hud);
                return;
            }

            // Directional movement now uses exactly the same authoritative command
            // boundary as a clicked destination. The local gateway commits and saves
            // immediately; the online gateway waits for the server snapshot before the
            // world, camera and minimap advance. There is no client-only coordinate.
            _actionConfirmation.Cancel();
            _selectionController.SelectMovement(destination);
            _selectionController.Reject("이동 요청 접수 · " + destination + " 경로를 확인하고 있습니다.");
            UpdateSelectionVisual(view);
            PlaySfx(AssetClip("UiMoveSound"));
            PublishPresentationState(PresentationChange.Hud | PresentationChange.Selection);
            Debug.Log("[KW.Input] event=DirectionalMove result=submitted destination=" + destination +
                      " inputId=" + KeyboardWandererInputAudit.CurrentInputId);
            SubmitImmediately();
        }

        private void HandleDirectionalMoveReleased()
        {
            _directionalInputHeld = false;
            ClearBufferedDirectionalMove();
        }

        private void ClearBufferedDirectionalMove()
        {
            _hasBufferedDirectionalMove = false;
            _bufferedMoveDirection = Vector2Int.zero;
        }

        private void SubmitBufferedDirectionalMoveIfReady()
        {
            if (!_directionalInputHeld || !_hasBufferedDirectionalMove || TurnPending ||
                _scenePlaybackCoroutine != null || _sceneSequencePlayer?.IsPlaying == true)
                return;
            Vector2Int direction = _bufferedMoveDirection;
            _hasBufferedDirectionalMove = false;
            HandleDirectionalMoveRequested(direction);
        }

        private void HandleNaturalLanguageRequested()
        {
            if (_screenMode != ScreenMode.Playing || TurnPending || _service == null) return;
            if (IsMandatoryOpeningAttackChoice())
            {
                RejectOpeningTutorialBypass();
                return;
            }
            string[] pages = BuildDialoguePages(_lastNarrative);
            _naturalLanguageComposeMode = true;
            _dialoguePresenter.ShowLast(pages.Length);
            PublishPresentationState(PresentationChange.Dialogue);
            QueueDialogueInputFocus();
        }

        /// <summary>
        /// TMP_InputField must not be activated in the same frame as the shortcut that
        /// opened it. The Input System router runs before the EventSystem text input
        /// module, so immediate activation lets the opening T (or the Return used to
        /// reach an intervention) leak into the draft. This is especially visible with
        /// a Korean IME, where T becomes a leading 'ㅅ'. Waiting for both the current
        /// frame and every activation key to be released gives the text field a clean
        /// ownership boundary without dropping the player's first intentional key.
        /// </summary>
        private void QueueDialogueInputFocus()
        {
            if (_focusDialogueInputCoroutine != null)
                StopCoroutine(_focusDialogueInputCoroutine);
            _focusDialogueInputCoroutine = StartCoroutine(FocusDialogueInputAfterActivation());
        }

        private IEnumerator FocusDialogueInputAfterActivation()
        {
            yield return null;
            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            while (keyboard != null &&
                   (keyboard.tKey.isPressed || keyboard.enterKey.isPressed ||
                    keyboard.numpadEnterKey.isPressed || keyboard.spaceKey.isPressed ||
                    keyboard.zKey.isPressed))
            {
                yield return null;
                keyboard = UnityEngine.InputSystem.Keyboard.current;
            }

            if (_screenMode == ScreenMode.Playing && !_showPause && !TurnPending &&
                _naturalLanguageComposeMode && _sceneUi != null)
                _sceneUi.FocusDialogueInput();
            _focusDialogueInputCoroutine = null;
        }

        private void HandleMapClick(Vector2 mousePosition)
        {
            if (IsMandatoryOpeningAttackChoice())
            {
                RejectOpeningTutorialBypass();
                return;
            }
            ClaimWorldNavigationInput();
            string blocked = AbilityInputBlockReason(_selectionController.Ability);
            if (!string.IsNullOrEmpty(blocked))
            {
                Debug.LogWarning("[KW.Input] event=WorldClick ability=" + _selectionController?.Ability +
                                 " result=blocked reason=" + blocked + " inputId=" +
                                 KeyboardWandererInputAudit.CurrentInputId);
                RejectBlockedGameplayInput(null);
                return;
            }
            if (_cameraController == null || !_cameraController.IsReady)
            {
                Debug.LogWarning("[KW.Input] event=WorldClick result=blocked reason=camera-not-ready inputId=" +
                                 KeyboardWandererInputAudit.CurrentInputId);
                RejectBlockedGameplayInput("화면과 월드 좌표를 아직 동기화하고 있습니다. 잠시 후 다시 선택해 주세요.");
                return;
            }
            if (!_cameraController.ContainsScreenPoint(mousePosition))
            {
                Debug.Log("[KW.Input] event=WorldClick result=ignored reason=outside-game-viewport inputId=" +
                          KeyboardWandererInputAudit.CurrentInputId);
                return;
            }

            Vector3 world = _cameraController.ScreenToWorld(mousePosition);
            RunView view = _service.CurrentView;
            Vector2 origin = MapOrigin(view);
            var coord = new GridCoord(
                Mathf.FloorToInt((world.x - origin.x) / TileSize),
                Mathf.FloorToInt((world.y - origin.y) / TileSize));
            if (!ContainsWorldCoord(view, coord))
            {
                Debug.Log("[KW.Input] event=WorldClick result=ignored reason=outside-world coord=" + coord);
                _selectionController.Reject("월드 경계 밖은 이동할 수 없습니다. 지도 안쪽의 타일을 선택해 주세요.");
                PlaySfx(AssetClip("UiCancelSound"));
                PublishPresentationState(PresentationChange.Hud | PresentationChange.Selection);
                return;
            }

            _cameraInspectCoord = null;
            _cameraInspectUntil = 0f;

            Guid? clickedEntity = FindVisualTargetAt(world);
            if (!clickedEntity.HasValue)
                clickedEntity = _serverOnline ? FindServerTarget(coord, _selectionController.Ability == AbilityKind.Restore) : FindTarget(view, coord);
            if (clickedEntity.HasValue && TryGetEntityPosition(view, clickedEntity.Value, out GridCoord entityCoord))
                coord = entityCoord;
            Debug.Log("[KW.Input] event=WorldClick ability=" + _selectionController.Ability +
                      " coord=" + coord + " entity=" + (clickedEntity.HasValue ? clickedEntity.Value.ToString() : "none") +
                      " inputId=" + KeyboardWandererInputAudit.CurrentInputId);
            string abilityName = _selectionController.Ability.ToString();
            if (abilityName == "Move")
            {
                if (clickedEntity.HasValue && IsInteractableTarget(view, clickedEntity.Value))
                {
                    _selectionController.SelectInteraction(clickedEntity.Value, coord,
                        IsSkillEnabledForCurrentTarget(AbilityKind.Interact)
                            ? DisplayEntityName(view, clickedEntity.Value) + " 상호작용 준비 완료"
                            : "상호작용하려면 대상의 2칸 이내로 이동하세요.");
                    UpdateSelectionVisual(view);
                    PlaySfx(AssetClip("UiMoveSound"));
                    PublishPresentationState(PresentationChange.Hud | PresentationChange.Selection);
                    if (CanSubmitCurrentSelection()) SubmitImmediately();
                    return;
                }
                if (!CanSelectMovementDestination(view, coord))
                {
                    _selectionController.Reject("그곳까지 이어지는 통행 가능한 경로가 없습니다.");
                    PlaySfx(AssetClip("UiCancelSound"));
                    PublishPresentationState(PresentationChange.Hud | PresentationChange.Selection);
                    return;
                }
                _selectionController.SelectMovement(coord);
                if (_pathPlanner != null && TryGetPlayerPosition(view, out GridCoord routeStart))
                    _selectionController.Reject(_pathPlanner.Preview(view,
                        _serverOnline ? _serverRun : null, routeStart, coord).PlayerText());
            }
            else if (abilityName == "Copy")
            {
                if (clickedEntity.HasValue)
                {
                    _selectionController.SelectCopySource(clickedEntity.Value,
                        "복사 원본 선택 · " + DisplayEntityName(view, clickedEntity.Value) +
                        " · 이제 빈 타일을 클릭하세요.");
                }
                else
                {
                    _selectionController.SelectCopyDestination(coord, _selectionController.CopySourceCaptured
                        ? "붙여넣을 타일 선택 완료 · 즉시 복제합니다."
                        : "먼저 복사할 적 또는 오브젝트를 클릭하세요.");
                }
            }
            else if (abilityName == "Connect")
            {
                if (clickedEntity.HasValue)
                {
                    bool completed = _selectionController.SelectConnectTarget(clickedEntity.Value, coord);
                    _selectionController.Feedback = completed
                        ? "연결 대상 2개 선택 완료 · 즉시 연결합니다."
                        : "첫 연결 대상 · " + DisplayEntityName(view, clickedEntity.Value) + " · 두 번째 대상을 고르세요.";
                }
                else
                {
                    _selectionController.SelectDestination(coord);
                }
            }
            else if (abilityName != "Undo")
            {
                _selectionController.SelectSingleTarget(clickedEntity, coord, clickedEntity.HasValue
                    ? DisplayEntityName(view, clickedEntity.Value) + " 선택 완료 · 즉시 실행합니다."
                    : "선택 가능한 적 또는 오브젝트의 모습을 직접 클릭하세요.");
            }

            UpdateSelectionVisual(view);
            PlaySfx(AssetClip("UiMoveSound"));
            PublishPresentationState(PresentationChange.Hud | PresentationChange.Selection);
            if (CanSubmitCurrentSelection()) SubmitImmediately();
        }

        private void CyclePoi(int direction)
        {
            if (_service == null) return;
            var coordinates = new List<GridCoord>();
            var labels = new List<string>();
            RunPresentationModel presentation = PresentationModel(_service.CurrentView);
            if (presentation.ObjectiveTargetPosition.HasValue)
            {
                GridCoord objectiveCoord = presentation.ObjectiveTargetPosition.Value;
                coordinates.Add(objectiveCoord);
                labels.Add("현재 목표 · " + presentation.ObjectiveTargetName);
            }
            GameApiClient.PointSnapshot[] serverPoints = _serverOnline
                ? (_serverRun?.world?.points ?? _serverRun?.world?.pois)
                : null;
            if (serverPoints != null)
            {
                for (int i = 0; i < serverPoints.Length; i++)
                {
                    GameApiClient.PointSnapshot point = serverPoints[i];
                    if (point == null || string.Equals(point.kind, "hub", StringComparison.OrdinalIgnoreCase))
                        continue;
                    var coord = new GridCoord(point.x, point.y);
                    if (!ContainsWorldCoord(_service.CurrentView, coord)) continue;
                    if (coordinates.Contains(coord)) continue;
                    coordinates.Add(coord);
                    labels.Add(FirstNonEmpty(point.nameKo, point.name, point.id, "POI"));
                }
            }
            else
            {
                IReadOnlyList<WorldArea> areas = _service.CurrentView.Region.Areas;
                for (int i = 0; i < areas.Count; i++)
                {
                    WorldArea area = areas[i];
                    if (area == null || string.IsNullOrWhiteSpace(area.CampaignRole)) continue;
                    coordinates.Add(area.Center);
                    labels.Add(FirstNonEmpty(area.DisplayName, area.Id, "POI"));
                }
            }

            if (coordinates.Count == 0)
            {
                _selectionController.PoiLabel = "표시할 POI 없음";
                return;
            }
            int cursor = (_selectionController.PoiCursor + (direction < 0 ? -1 : 1)) % coordinates.Count;
            if (cursor < 0) cursor += coordinates.Count;
            string label = (cursor + 1) + "/" + coordinates.Count + " " + labels[cursor];
            _selectionController.SelectPoi(cursor, coordinates[cursor], label);
            _cameraInspectCoord = _selectionController.SelectedCoord;
            _cameraInspectUntil = Time.unscaledTime + 6f;
            UpdateSelectionVisual(_service.CurrentView);
            PlaySfx(AssetClip("UiMoveSound"));
        }

        private void Submit() => SubmitInternal(false);

        private void SubmitImmediately() => SubmitInternal(true);

        private void SubmitInternal(bool bypassConfirmation)
        {
            string blocked = AbilityInputBlockReason(_selectionController.Ability);
            if (!string.IsNullOrEmpty(blocked))
            {
                Debug.LogWarning("[KW.Input] event=Submit ability=" + _selectionController?.Ability +
                                 " result=blocked reason=" + blocked);
                RejectBlockedGameplayInput(null);
                return;
            }
            if (_sceneSequencePlayer != null && _sceneSequencePlayer.IsPlaying)
            {
                Debug.LogWarning("[KW.Input] event=Submit ability=" + _selectionController?.Ability +
                                 " result=blocked reason=scene-sequence-playing");
                return;
            }
            RunView view = _service.CurrentView;
            if (!RunIsPlaying(view))
                return;
            if (bypassConfirmation) _actionConfirmation.Cancel();
            _gameplayTelemetry.RecordSubmit();
            if (!CanSubmitCurrentSelection())
            {
                Debug.LogWarning("[KW.Input] event=Submit ability=" + _selectionController.Ability +
                                 " result=blocked reason=selection-invalid target=" +
                                 (_selectionController.SelectedTarget.HasValue
                                     ? _selectionController.SelectedTarget.Value.ToString()
                                     : "none") + " feedback=" + _selectionController.Feedback);
                _gameplayTelemetry.RecordInvalidSelection();
                // Preserve the presentation rule that made the confirm button unavailable.
                // In particular, an unrevealed Root Process must explain that Search is
                // required instead of degrading to a generic target error on keyboard submit.
                PresentActionRejection("TARGET_INVALID", SelectionStatusDetail(view));
                return;
            }

            if (!bypassConfirmation && _actionConfirmation.RequiresConfirmation(_selectionController.Ability,
                    _selectionController.SelectedTarget, Time.unscaledTime))
            {
                _gameplayTelemetry.RecordConfirmation();
                _selectionController.Reject("고위험 행동입니다. 5초 안에 실행을 다시 누르면 적용됩니다. Esc로 취소할 수 있습니다.");
                PlaySfx(AssetClip("UiCancelSound"));
                PublishPresentationState(PresentationChange.Hud);
                return;
            }

            if (_turnCoordinator == null || !_turnCoordinator.IsReady)
            {
                PresentActionRejection("NETWORK_ERROR", null);
                return;
            }

            long expectedVersion = _serverOnline && _serverRun != null
                ? _serverRun.version
                : view.Version;
            string requestPrefix = _serverOnline
                ? (_selectionController.Ability == AbilityKind.Move ? "unity-travel" : "unity")
                : "local";
            TurnRequest request;
            try
            {
                request = KeyboardWandererTurnCoordinator.BuildRequest(
                    _selectionController, expectedVersion, requestPrefix);
            }
            catch (InvalidOperationException exception)
            {
                PresentActionRejection("TARGET_INVALID", exception.Message);
                return;
            }

            if (_serverOnline && _serverRun != null && _gameApi != null)
            {
                StartCoroutine(request.Ability == AbilityKind.Move
                    ? SubmitServerTravel(request)
                    : SubmitServerAction(request));
                return;
            }

            StartCoroutine(SubmitLocalTurn(request));
        }

        private IEnumerator SubmitLocalTurn(TurnRequest request)
        {
            RunView view = _service.CurrentView;
            TryGetPlayerPosition(view, out GridCoord playerPositionBefore);
            TurnGatewayResult gatewayResult = null;
            yield return _turnCoordinator.Submit(request, value => gatewayResult = value);
            TurnResponse response = gatewayResult?.LocalResponse;
            if (response == null)
            {
                PresentActionRejection(gatewayResult?.ErrorCode, gatewayResult?.ErrorMessage);
                yield break;
            }
            if (!response.IsSuccess)
            {
                PresentActionRejection(response.ErrorCode.ToString(), response.ErrorMessage);
                yield break;
            }

            SyncLocalEncounterState(response.Run);
            RunView committedView = response.Run ?? _service.CurrentView;
            bool oneTileMovement = request.Ability == AbilityKind.Move &&
                                   playerPositionBefore.ManhattanDistance(committedView.PlayerPosition) == 1;
            ApplyTurnPresentation(LocalTurnPresentationAdapter.Create(response), !oneTileMovement);
            ApplySkillMotionForLocalTurn(request.Ability, response.Run ?? _service.CurrentView);
            CaptureLocalRestorableTarget(response, view);

            if (request.Ability == AbilityKind.Move)
            {
                List<GridCoord> path = _pathPlanner.FindLocalVisualPath(committedView, playerPositionBefore,
                    committedView.PlayerPosition);
                BeginPlayerPathAnimation(path, committedView, !oneTileMovement);
            }
            SyncEntityVisuals(response.Run ?? _service.CurrentView);
            UpdateSelectionVisual(_service.CurrentView);
            LocalRunSaveService.Save(_service);

            if (response.ActionContext == ActionContext.Combat)
                PlaySfx(AssetClip("SlashSound"));
            else if (response.Outcome == RuleOutcome.CriticalSuccess)
                PlaySfx(AssetClip("SuccessJingle"), cutOffPrevious: false);
            else
                PlaySfx(AssetClip("UiAcceptSound"));

            if (request.Ability != AbilityKind.Move && GmEnabled && _narrativeClient != null)
            {
                int generation = _runGeneration;
                int turn = response.TurnNo;
                int requestToken = ++_optionalNarrativeRequestToken;
                _gmPending = true;
                StartCoroutine(_narrativeClient.RequestNarrative(_service.CurrentView, response, _selectionController.Ability,
                    response.NormalizedAttempt,
                    CurrentAreaName(_service.CurrentView), result =>
                    {
                        // A later committed turn or run boundary owns narration. Older
                        // callbacks must not clear the newer request or reopen stale UI.
                        if (requestToken != _optionalNarrativeRequestToken)
                            return;
                        _gmPending = false;
                        if (generation != _runGeneration || _service == null ||
                            _service.CurrentView.CurrentTurn != turn || _screenMode != ScreenMode.Playing)
                            return;
                        if (result.IsSuccess)
                        {
                            Debug.Log("[KW.Narrative] event=LocalGmResponse result=success turn=" + turn +
                                      " fallback=" + result.FallbackUsed + " model=" + result.Model +
                                      " chars=" + (result.Narrative?.Length ?? 0));
                            _lastNarrative = result.Narrative;
                            _lastNarrativeFallbackUsed = result.FallbackUsed;
                            _lastNarrativeModel = string.IsNullOrWhiteSpace(result.Model)
                                ? "deterministic"
                                : result.Model;
                            ReopenDialogueForNewTurnContent();
                            AddLog("GM · " + result.Narrative);
                            PublishPresentationState(PresentationChange.Dialogue | PresentationChange.Hud);
                        }
                        else
                        {
                            Debug.LogWarning("[KW.Narrative] event=LocalGmResponse result=failure turn=" + turn +
                                             " error=" + result.Error);
                        }
                    }));
            }

            _selectionController.ClearAfterAction(_encounterStagingCoord, _encounterMoveRequired);
            UpdateSelectionVisual(_service.CurrentView);

            if (_service.CurrentView.Status != RunStatus.Playing)
            {
                _gmPending = false;
                CancelOptionalNarrativeRequest();
                PlaySfx(AssetClip("SuccessJingle"), cutOffPrevious: false);
            }
            PublishPresentationState(PresentationChange.Hud | PresentationChange.Dialogue |
                                     PresentationChange.Minimap | PresentationChange.Selection);
        }

        private IEnumerator SubmitServerNarrativeChoice(NarrativeChoiceOption choice)
        {
            EnsureRuntimeClients();
            if (choice == null || string.IsNullOrWhiteSpace(_lastChoiceSetId) || _serverRun == null)
            {
                PresentChoiceRejection("INVALID_CHOICE", "현재 선택 세트가 만료되었습니다.");
                yield break;
            }

            string submittedChoiceSetId = _lastChoiceSetId;
            string submittedChoiceId = choice.ChoiceId;
            NarrativeChoiceOption[] submittedChoices = _lastNarrativeChoices;
            string idempotencyKey = "unity-choice-" + Guid.NewGuid().ToString("N");
            long expectedVersion = _serverRun.version;
            GameApiClient.RunSnapshot runBeforeSubmit = _serverRun;
            int turnBeforeSubmit = _serverRun.currentTurn;
            bool requiresD20 = choice.RequiresD20;
            if (string.Equals(choice.SkillId, "DELETE", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(choice.SkillId, "SELECT_ALL", StringComparison.OrdinalIgnoreCase))
                CapturePendingImpactTarget(choice.TargetEntityId);
            else
                _hasPendingImpactTargetPosition = false;

            _pendingNarrativeChoiceLabel = KeyboardWandererHudTextComposer.NarrativeChoicePlayerLabel(choice);
            _choiceSubmissionPending = true;
            _serverPending = true;
            _gmPending = true;
            _serverStatus = requiresD20 ? "서사 선택 판정 준비 중" : "서사 선택 처리 중";
            // DIALOGUE/ATTITUDE choices use NONE resolution and must never flash a
            // meaningless die or reserve a server roll. Only the server-sealed D20
            // contract owns the dice presentation.
            if (requiresD20)
                yield return PreparePendingDiceRoll(expectedVersion);
            else
            {
                _preparedD20 = 0;
                CancelPendingDiceRoll();
            }
            _serverStatus = "서사 선택 처리 중";
            // Invalidate the consumed set immediately so a second mouse/key event cannot
            // submit it. Keep the visible options in place (disabled by TurnPending) so
            // the choice labels do not flicker into legacy fallback options while waiting.
            _lastChoiceSetId = string.Empty;
            Debug.Log("[KW.Choice] event=Selected runId=" + _serverRun.id + " turn=" +
                      _serverRun.currentTurn + " choiceSetId=" + submittedChoiceSetId +
                      " choiceId=" + submittedChoiceId + " kind=" + choice.ChoiceKind);

            GameApiClient.Result<GameApiClient.CommittedTurn> result = null;
            yield return _gameApi.SubmitNarrativeChoice(_serverRun.id, submittedChoiceSetId,
                submittedChoiceId, idempotencyKey, expectedVersion, _preparedD20, value => result = value);

            if (result == null || !result.IsSuccess || result.Value == null)
            {
                CancelPendingDiceRoll();
                _serverPending = false;
                _gmPending = false;
                _choiceSubmissionPending = false;
                _pendingNarrativeChoiceLabel = string.Empty;
                if (RecoverMissingServerRun(result?.ErrorCode, result?.ErrorMessage))
                    yield break;
                // Restore the exact sealed set so a transient network failure is retryable.
                _lastChoiceSetId = submittedChoiceSetId;
                _lastNarrativeChoices = submittedChoices ?? Array.Empty<NarrativeChoiceOption>();
                RecoverPendingChoiceSet(runBeforeSubmit?.pendingChoiceSet, true);
                PresentChoiceRejection(result?.ErrorCode, result?.ErrorMessage);
                if (result == null || result.ErrorCode == "NETWORK_ERROR" || result.ErrorCode == "RUN_VERSION_CONFLICT")
                    yield return ResyncServerRun();
                yield break;
            }

            GameApiClient.CommittedTurn committed = result.Value;
            if (requiresD20)
                yield return ResolvePendingDiceRoll(committed.Turn?.dice?.raw ?? 0);
            else
                CancelPendingDiceRoll();
            _serverPending = false;
            _gmPending = false;
            _choiceSubmissionPending = false;
            _pendingNarrativeChoiceLabel = string.Empty;
            CaptureRestorableTarget(committed.Turn, runBeforeSubmit);
            _serverRun = committed.Run;
            SyncEncounterStateFromServer();
            SyncRestorableCandidateFromServer();
            ApplyServerTurnPresentation(committed.Turn);
            RecoverPendingChoiceSet(_serverRun.pendingChoiceSet, false);
            SyncServerEntityVisuals(_serverRun);
            PrepareStoryWorldActions();
            _selectionController.ResetSelection(AbilityKind.Move);
            UpdateSelectionVisual(_service.CurrentView);
            _runSessionController?.RememberServerRun(_serverRun.id);
            _serverStatus = committed.FromIdempotencyCache ? "서사 선택 멱등 응답 재생" : "서사 선택 반영 완료";
            AddLog("서사 선택 · " + choice.Text);
            Debug.Log("[KW.Choice] event=Resolved runId=" + _serverRun.id + " turnBefore=" +
                      turnBeforeSubmit + " turnAfter=" + _serverRun.currentTurn + " choiceSetId=" +
                      submittedChoiceSetId + " choiceId=" + submittedChoiceId +
                      " fallback=" + (committed.Turn?.narrative?.fallbackUsed ?? true));
            PlaySfx(AssetClip("UiAcceptSound"));
            PublishPresentationState(PresentationChange.Hud | PresentationChange.Dialogue |
                                     PresentationChange.Minimap | PresentationChange.Selection);
        }

        private IEnumerator SubmitServerPlayerMessage(string text, string idempotencyKey, long expectedVersion)
        {
            EnsureRuntimeClients();
            GameApiClient.RunSnapshot runBeforeSubmit = _serverRun;
            int turnBeforeSubmit = _serverRun.currentTurn;
            TryGetPlayerPosition(_service.CurrentView, out GridCoord playerPositionBefore);
            string layoutHashBefore = _serverRun.world?.layoutHash;

            _choiceSubmissionPending = true;
            _pendingNarrativeChoiceLabel = string.Empty;
            _serverPending = true;
            _gmPending = true;
            _serverStatus = "자유 입력 판정 준비 중";
            yield return PreparePendingDiceRoll(expectedVersion);
            _serverStatus = "자연어 대화 생성 중";
            _lastChoiceSetId = string.Empty;
            Debug.Log("[KW.Message] event=Submitted runId=" + _serverRun.id + " turn=" +
                      turnBeforeSubmit + " chars=" + text.Length);

            GameApiClient.Result<GameApiClient.CommittedPlayerMessage> result = null;
            yield return _gameApi.SubmitPlayerMessage(_serverRun.id, text, idempotencyKey,
                expectedVersion, _preparedD20, value => result = value);

            if (result == null || !result.IsSuccess || result.Value == null)
            {
                CancelPendingDiceRoll();
                _serverPending = false;
                _gmPending = false;
                _choiceSubmissionPending = false;
                if (RecoverMissingServerRun(result?.ErrorCode, result?.ErrorMessage))
                    yield break;
                RecoverPendingChoiceSet(runBeforeSubmit?.pendingChoiceSet, true);
                PresentChoiceRejection(result?.ErrorCode, result?.ErrorMessage);
                bool ambiguousTransportFailure = result == null || result.ErrorCode == "NETWORK_ERROR";
                if (!ambiguousTransportFailure)
                    ClearPlayerMessageSubmission();
                if (ambiguousTransportFailure || result.ErrorCode == "RUN_VERSION_CONFLICT")
                    yield return ResyncServerRun();
                yield break;
            }

            GameApiClient.CommittedPlayerMessage committed = result.Value;
            if (committed.Navigation != null)
            {
                CancelPendingDiceRoll();
                _serverPending = false;
                _gmPending = false;
                _choiceSubmissionPending = false;
                ClearPlayerMessageSubmission();
                _sceneUi?.CompleteDialogueFreeformSubmission();
                ApplyNaturalLanguageNavigation(committed, text, playerPositionBefore,
                    turnBeforeSubmit, layoutHashBefore);
                yield break;
            }
            yield return ResolvePendingDiceRoll(committed.Turn?.dice?.raw ?? 0);
            _serverPending = false;
            _gmPending = false;
            _choiceSubmissionPending = false;
            ClearPlayerMessageSubmission();
            _sceneUi?.CompleteDialogueFreeformSubmission();
            _serverRun = committed.Run;
            SyncEncounterStateFromServer();
            SyncRestorableCandidateFromServer();
            ApplyServerTurnPresentation(committed.Turn);
            RecoverPendingChoiceSet(_serverRun.pendingChoiceSet, false);
            SyncServerEntityVisuals(_serverRun);
            PrepareStoryWorldActions();
            _selectionController.ResetSelection(AbilityKind.Move);
            UpdateSelectionVisual(_service.CurrentView);
            _runSessionController?.RememberServerRun(_serverRun.id);
            _serverStatus = "자연어 대화 반영 완료";
            AddLog("플레이어 · " + text);
            Debug.Log("[KW.Message] event=Resolved runId=" + _serverRun.id + " turnBefore=" +
                      turnBeforeSubmit + " turnAfter=" + _serverRun.currentTurn + " fallback=" +
                      (committed.Turn?.narrative?.fallbackUsed ?? true));
            PlaySfx(AssetClip("UiAcceptSound"));
            PublishPresentationState(PresentationChange.Hud | PresentationChange.Dialogue |
                                     PresentationChange.Minimap | PresentationChange.Selection);
        }

        private void ApplyNaturalLanguageNavigation(GameApiClient.CommittedPlayerMessage committed,
            string text, GridCoord playerPositionBefore, int campaignTurnBefore, string layoutHashBefore)
        {
            GameApiClient.NavigationSnapshot navigation = committed.Navigation;
            _serverRun = committed.Run;
            SyncEncounterStateFromServer();
            SyncRestorableCandidateFromServer();
            bool encounterOpened = navigation.encounterOpened || IsOpenServerEncounter(_serverRun.activeEncounter);
            GameApiClient.PositionSnapshot encounterStaging = navigation.encounter?.stagingPosition ??
                                                                navigation.stagingPosition ??
                                                                navigation.activeEncounter?.stagingPosition ??
                                                                _serverRun.activeEncounter?.stagingPosition;
            _encounterMoveRequired = encounterOpened;
            _encounterReason = FirstNonEmpty(navigation.encounter?.reason, navigation.encounterReason,
                navigation.activeEncounter?.reason, _serverRun.activeEncounter?.reason);
            _encounterStagingCoord = encounterStaging == null
                ? (GridCoord?)null
                : new GridCoord(encounterStaging.x, encounterStaging.y);
            GameApiClient.PositionSnapshot requested = navigation.requestedDestination ?? navigation.to;
            var requestedCoord = new GridCoord(requested.x, requested.y);
            bool invariantHeld = !navigation.campaignTurnConsumed &&
                                 navigation.campaignTurnBefore == campaignTurnBefore &&
                                 navigation.campaignTurnAfter == campaignTurnBefore &&
                                 _serverRun.currentTurn == campaignTurnBefore &&
                                 string.Equals(layoutHashBefore, _serverRun.world?.layoutHash,
                                     StringComparison.Ordinal);
            ApplyTurnPresentation(ServerTurnPresentationAdapter.FromNavigation(
                navigation, _serverRun, campaignTurnBefore, requestedCoord.X, requestedCoord.Y,
                encounterOpened, _encounterReason, invariantHeld, GmEnabled,
                PlayerDisplayName(_service.CurrentView)), true);
            RecoverPendingChoiceSet(_serverRun.pendingChoiceSet, false);
            TryGetPlayerPosition(_service.CurrentView, out GridCoord playerPositionAfter);
            List<GridCoord> visualPath = _pathPlanner.FindServerVisualPath(
                navigation, _serverRun, playerPositionBefore, playerPositionAfter);
            BeginPlayerPathAnimation(visualPath, _service.CurrentView, true);
            SyncServerEntityVisuals(_serverRun);
            QueueServerSceneSequence(navigation.sceneSequence);
            _selectionController.ClearToCoord(encounterOpened ? _encounterStagingCoord : null);
            UpdateSelectionVisual(_service.CurrentView);
            _runSessionController?.RememberServerRun(_serverRun.id);
            _serverStatus = encounterOpened
                ? "자연어 이동 중 조우 발생 · 다음 행동을 선택하세요"
                : committed.FromIdempotencyCache ? "자연어 이동 멱등 응답" : "자연어 목적지 이동 완료";
            AddLog("플레이어 이동 · " + text);
            Debug.Log("[KW.Message] event=NavigationResolved runId=" + _serverRun.id +
                      " turn=" + campaignTurnBefore + " from=(" + playerPositionBefore.X + "," +
                      playerPositionBefore.Y + ") to=(" + playerPositionAfter.X + "," +
                      playerPositionAfter.Y + ") requestedArea=" + navigation.enteredAreaId);
            PlaySfx(AssetClip("UiAcceptSound"));
            PublishPresentationState(PresentationChange.Hud | PresentationChange.Dialogue |
                                     PresentationChange.Minimap | PresentationChange.Selection);
        }

        private void PresentChoiceRejection(string errorCode, string technicalMessage)
        {
            string playerMessage = PlayerFacingRejection(errorCode, technicalMessage);
            _choiceStatusMessage = playerMessage + " 입력 내용을 보존했습니다. 다시 시도할 수 있습니다.";
            _selectionController?.Reject(playerMessage);
            _serverStatus = "선택 처리 불가 · " + playerMessage;
            _sceneUi?.ReleaseDialogueChoiceInputLock();
            if (_service != null)
                _dialoguePresenter.ShowLast(BuildDialoguePages(_lastNarrative).Length);
            AddLog("서사 선택 거부 · " + (errorCode ?? "UNKNOWN") + " · " +
                   (technicalMessage ?? string.Empty));
            Debug.LogWarning("[KW.Choice] event=Rejected code=" + (errorCode ?? "UNKNOWN") +
                             " message=" + (technicalMessage ?? string.Empty));
            PlaySfx(AssetClip("UiCancelSound"));
            PublishPresentationState(PresentationChange.Dialogue | PresentationChange.Hud);
        }

        private void ReservePlayerMessageSubmission(string text, long expectedVersion)
        {
            string normalized = (text ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(_pendingPlayerMessageIdempotencyKey) &&
                string.Equals(_pendingPlayerMessageText, normalized, StringComparison.Ordinal))
                return;

            _pendingPlayerMessageText = normalized;
            _pendingPlayerMessageIdempotencyKey = "unity-message-" + Guid.NewGuid().ToString("N");
            _pendingPlayerMessageExpectedVersion = expectedVersion;
        }

        private void ClearPlayerMessageSubmission()
        {
            _pendingPlayerMessageText = string.Empty;
            _pendingPlayerMessageIdempotencyKey = string.Empty;
            _pendingPlayerMessageExpectedVersion = 0;
        }

        private void CancelOptionalNarrativeRequest()
        {
            // The HTTP request itself may already be in flight. Advancing this ownership
            // token makes its eventual callback a no-op without blocking the next turn.
            _optionalNarrativeRequestToken++;
            _gmPending = false;
        }

        private bool RecoverMissingServerRun(string errorCode, string technicalMessage)
        {
            if (!IsMissingServerRun(errorCode, technicalMessage) || _service == null)
                return false;

            string missingRunId = _serverRun?.id ?? "unknown";
            LocalTurnService checkpoint = LocalRunSaveService.Load();
            if (checkpoint == null || checkpoint.CurrentView.WorldSeed != _service.CurrentView.WorldSeed)
                checkpoint = _service;

            _runGeneration++;
            _serverPending = false;
            _gmPending = false;
            _choiceSubmissionPending = false;
            _pendingNarrativeChoiceLabel = string.Empty;
            CancelOptionalNarrativeRequest();
            ClearPlayerMessageSubmission();
            _playerWalking = false;
            _lastPresentationContinuesWithMovement = false;
            _directionalInputHeld = false;
            ClearBufferedDirectionalMove();
            _reopenDialogueAfterWalk = false;
            _suppressDialogueReopenAfterWalk = false;
            _preparedD20 = 0;
            _storyWorldActionPlaying = false;
            _turnImpactPresentationPlaying = false;
            if (_turnImpactPresentationCoroutine != null)
            {
                StopCoroutine(_turnImpactPresentationCoroutine);
                _turnImpactPresentationCoroutine = null;
            }
            if (_storyWorldActionCoroutine != null)
            {
                StopCoroutine(_storyWorldActionCoroutine);
                _storyWorldActionCoroutine = null;
            }
            if (_scenePlaybackCoroutine != null)
            {
                StopCoroutine(_scenePlaybackCoroutine);
                _scenePlaybackCoroutine = null;
            }
            _sceneSequencePlayer?.Cancel();
            _lastServerSceneSequence = Array.Empty<GameApiClient.SceneActionSnapshot>();
            _pendingRuntimeRenderSequence = Array.Empty<GameApiClient.SceneActionSnapshot>();
            _playedStoryActionIds.Clear();
            _actionConfirmation.Cancel();
            ClearRestorableTargetCache();
            _cameraInspectCoord = null;
            _cameraInspectUntil = 0f;
            _playerActionUntil = 0f;
            CancelPendingDiceRoll();

            _serverOnline = false;
            _serverCampaign = null;
            _serverRun = null;
            _runSessionController?.ForgetServerRun();
            _service = checkpoint;
            _turnGateway = new LocalTurnGateway(_service);
            _turnCoordinator.Configure(_turnGateway);
            _runPresentationAdapter = new LocalRunPresentationAdapter();
            _runPresentationModel = PresentationModel(_service.CurrentView);

            _lastChoiceSetId = string.Empty;
            _lastNarrativeChoices = Array.Empty<NarrativeChoiceOption>();
            _choiceStatusMessage = string.Empty;
            _lastStorySequence = Array.Empty<StorySequencePage>();
            _lastDialogue = Array.Empty<string>();
            _dialogueSpeakerNames.Clear();
            _dialogueSpeakerSprites.Clear();
            _naturalLanguageComposeMode = false;
            _lastOutcome = "RESTORED";
            _lastD20 = 0;
            _lastModifier = 0;
            _lastModifierBreakdown = "--";
            _lastDifficulty = 0;
            _lastMechanicalScore = 0;
            _lastActionContext = "--";
            _lastStateChanges = "서버 전용 상태를 폐기하고 로컬 체크포인트를 복원했습니다.";
            _lastAttempt = "서버 런을 찾지 못해 마지막 로컬 체크포인트로 전환했습니다.";
            _lastExplanation = "만료된 선택과 서버 포인터를 폐기했습니다. 현재 화면의 위치와 목표부터 안전하게 계속할 수 있습니다.";
            _lastNarrative = "서버에서 진행 중이던 여정을 더 이상 찾을 수 없습니다. 마지막으로 검증된 로컬 체크포인트를 불러왔습니다.";
            _lastNextInterventionReason = "로컬 체크포인트 복구 완료. 현재 목표를 확인한 뒤 이동하거나 주변 대상을 조사하세요.";
            _lastSuggestedSkillIds = new[] { "SEARCH", "CONNECT" };
            _lastNarrativeFallbackUsed = true;
            _lastNarrativeModel = "deterministic-recovery";
            _serverStatus = "서버 런 소실 · 로컬 체크포인트 복구 완료";
            _dialoguePresenter.Reset();
            _tutorialPresenter.Start(false);
            _sceneUi?.ResetDialogueChoiceSession();
            _inputRouter?.SetNarrativeChoiceMode(false);
            _inputRouter?.SetNarrativeOverlayMode(false);
            _selectionController.ResetSelection(AbilityKind.Move);
            _selectionController.Reject("서버 런이 없어 로컬 체크포인트로 복구했습니다. 현재 목표부터 계속하세요.");

            SyncLocalEncounterState(_service.CurrentView);
            BuildWorld(_service.CurrentView);
            SyncEntityVisuals(_service.CurrentView);
            UpdateSelectionVisual(_service.CurrentView);
            SnapCameraToPlayer();
            RefreshDynamicMusic(_service.CurrentView, _runPresentationModel);
            LocalRunSaveService.Save(_service);
            AddLog("서버 런 " + missingRunId + " 소실 · 로컬 체크포인트 복구");
            Debug.LogWarning("[KW.Session] event=MissingServerRunRecovered runId=" + missingRunId +
                             " code=" + (errorCode ?? "UNKNOWN"));
            PublishPresentationState(PresentationChange.All);
            return true;
        }

        private static bool IsMissingServerRun(string errorCode, string technicalMessage)
        {
            string code = (errorCode ?? string.Empty).Trim().ToUpperInvariant();
            if (code == "NOT_FOUND" || code == "RUN_NOT_FOUND")
                return true;
            if (!string.IsNullOrWhiteSpace(code))
                return false;
            string message = technicalMessage ?? string.Empty;
            return message.IndexOf("Run was not found", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   message.IndexOf("run not found", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private IEnumerator SubmitServerAction(TurnRequest request)
        {
            EnsureRuntimeClients();
            if (request.Ability == AbilityKind.Delete || request.Ability == AbilityKind.SelectAll)
                CapturePendingImpactTarget(request.TargetEntityId);
            else
                _hasPendingImpactTargetPosition = false;
            _serverPending = true;
            _gmPending = true;
            yield return PreparePendingDiceRoll(request.ExpectedRunVersion);
            if (_preparedD20 >= 1 && _preparedD20 <= 20)
                request = request.WithPreparedD20(_preparedD20);
            _serverStatus = "권위 턴 커밋 중";

            GameApiClient.RunSnapshot runBeforeSubmit = _serverRun;
            int turnBeforeSubmit = _serverRun.currentTurn;
            TurnGatewayResult gatewayResult = null;
            yield return _turnCoordinator.Submit(request, value => gatewayResult = value);
            GameApiClient.Result<GameApiClient.CommittedTurn> result =
                gatewayResult?.TransportResponse as GameApiClient.Result<GameApiClient.CommittedTurn>;

            if (gatewayResult == null || !gatewayResult.IsSuccess)
            {
                CancelPendingDiceRoll();
                _serverPending = false;
                _gmPending = false;
                if (RecoverMissingServerRun(gatewayResult?.ErrorCode, gatewayResult?.ErrorMessage))
                    yield break;
                bool encounterRequired = string.Equals(gatewayResult?.ErrorCode, "TRAVEL_ENCOUNTER_REQUIRED", StringComparison.Ordinal);
                if (encounterRequired)
                {
                    _encounterMoveRequired = true;
                    _encounterReason = FirstNonEmpty(result?.ErrorReason, "unsafe_route");
                    GameApiClient.PositionSnapshot staging = result?.StopPosition;
                    _encounterStagingCoord = staging == null ? (GridCoord?)null : new GridCoord(staging.x, staging.y);
                    _selectionController.SelectDestination(_encounterStagingCoord);
                    _lastD20 = 0;
                    _lastModifier = 0;
                    _lastDifficulty = 0;
                    _lastMechanicalScore = 0;
                    _lastOutcome = "사건 진입 필요";
                    _lastAttempt = "안전 이동이 " + EncounterReasonLabel(_encounterReason) + " 앞에서 멈췄습니다.";
                    _lastExplanation = "아직 D20과 의미 있는 턴은 소비하지 않았습니다. 현재 위치에서 5칸 이내의 사건 배치 좌표를 고르거나 상황 행동을 선택하세요.";
                    _serverStatus = "ACTIVE ENCOUNTER · " + EncounterReasonLabel(_encounterReason);
                    UpdateSelectionVisual(_service.CurrentView);
                    AddLog("탐색 중 사건 발견 · 의미 턴 유지 · 다음 사건 행동부터 D20");
                    PlaySfx(AssetClip("UiCancelSound"));
                    yield break;
                }
                PresentActionRejection(gatewayResult?.ErrorCode, gatewayResult?.ErrorMessage);
                if (gatewayResult == null || gatewayResult.ErrorCode == "NETWORK_ERROR" || gatewayResult.ErrorCode == "RUN_VERSION_CONFLICT")
                {
                    yield return ResyncServerRun();
                    if (_serverRun != null && _serverRun.currentTurn > turnBeforeSubmit)
                    {
                        GameApiClient.Result<GameApiClient.TurnSnapshot> recoveredTurn = null;
                        yield return _gameApi.GetTurn(_serverRun.id, _serverRun.currentTurn, value => recoveredTurn = value);
                        if (recoveredTurn != null && recoveredTurn.IsSuccess)
                        {
                            CaptureRestorableTarget(recoveredTurn.Value, runBeforeSubmit);
                            ApplyServerTurnPresentation(recoveredTurn.Value);
                            _serverStatus = "타임아웃 후 커밋된 턴 복구 완료";
                            SyncServerEntityVisuals(_serverRun);
                            PrepareStoryWorldActions();
                        }
                    }
                }
                yield break;
            }

            GameApiClient.CommittedTurn committed = gatewayResult.Payload as GameApiClient.CommittedTurn;
            if (committed == null)
            {
                CancelPendingDiceRoll();
                _serverPending = false;
                _gmPending = false;
                PresentActionRejection("EMPTY_RESPONSE", "Server reported success but returned no committed turn.");
                yield break;
            }
            yield return ResolvePendingDiceRoll(committed.Turn?.dice?.raw ?? 0);
            _serverPending = false;
            _gmPending = false;
            CaptureRestorableTarget(committed.Turn, runBeforeSubmit);
            _serverRun = committed.Run;
            SyncEncounterStateFromServer();
            SyncRestorableCandidateFromServer();
            ApplyServerTurnPresentation(committed.Turn);
            SyncServerEntityVisuals(_serverRun);
            PrepareStoryWorldActions();
            ApplySkillMotionForServerTurn(committed.Turn);
            UpdateSelectionVisual(_service.CurrentView);
            _runSessionController?.RememberServerRun(_serverRun.id);

            _selectionController.ClearAfterAction(_selectionController.SelectedCoord, _selectionController.Ability == AbilityKind.Move);
            UpdateSelectionVisual(_service.CurrentView);
            _serverStatus = committed.FromIdempotencyCache ? "멱등 응답 재생" : "권위 상태 커밋 완료";
            PlaySfx(_lastD20 == 20 ? AssetClip("SuccessJingle") : AssetClip("UiAcceptSound"), cutOffPrevious: _lastD20 != 20);
        }

        private void PresentActionRejection(string errorCode, string technicalMessage)
        {
            string playerMessage = PlayerFacingRejection(errorCode, technicalMessage);
            _selectionController.Reject(playerMessage);
            _serverStatus = "행동 불가 · " + playerMessage;
            bool choiceRequired = string.Equals(errorCode, "CHOICE_REQUIRED", StringComparison.OrdinalIgnoreCase) &&
                                  HasPendingServerNarrativeChoice();
            if (choiceRequired)
            {
                _choiceStatusMessage = playerMessage;
                string[] pages = BuildDialoguePages(_lastNarrative);
                _naturalLanguageComposeMode = false;
                _dialoguePresenter.ShowLast(pages.Length);
                _sceneUi?.SetStoryVisible(true);
            }
            else
            {
                _dialoguePresenter.Dismiss();
                _sceneUi?.SetStoryVisible(false);
            }
            AddLog("행동 거부 · " + (errorCode ?? "UNKNOWN") + " · " +
                   (technicalMessage ?? string.Empty));
            PlaySfx(AssetClip("UiCancelSound"));
            PublishPresentationState(PresentationChange.Hud | PresentationChange.Selection |
                                     (choiceRequired ? PresentationChange.Dialogue : PresentationChange.None));
        }

        private void BeginPendingDiceRoll()
        {
            _diceOverlay?.BeginRoll();
        }

        private IEnumerator PreparePendingDiceRoll(long expectedRunVersion)
        {
            _preparedD20 = 0;
            GameApiClient.Result<GameApiClient.PreparedD20Snapshot> prepared = null;
            yield return _gameApi.PrepareD20(_serverRun.id, expectedRunVersion, value => prepared = value);
            BeginPendingDiceRoll();
            if (prepared != null && prepared.IsSuccess && prepared.Value != null &&
                prepared.Value.d20 >= 1 && prepared.Value.d20 <= 20)
            {
                _preparedD20 = prepared.Value.d20;
                StartCoroutine(_diceOverlay.ResolveAndWait(_preparedD20));
            }
        }

        private IEnumerator ResolvePendingDiceRoll(int authoritativeD20)
        {
            if (_diceOverlay == null)
                yield break;
            int displayedD20 = KeyboardWandererDiceOverlay.IsD20Result(authoritativeD20)
                ? authoritativeD20
                : _preparedD20;
            if (!KeyboardWandererDiceOverlay.IsD20Result(displayedD20))
            {
                _diceOverlay.CancelAndHide();
                yield break;
            }
            if (_preparedD20 == 0)
                yield return _diceOverlay.ResolveAndWait(displayedD20);
            else
                while (!_diceOverlay.HasSettledResult)
                    yield return null;
            yield return _diceOverlay.HideAfterResponse();
            _preparedD20 = 0;
        }

        private void CancelPendingDiceRoll()
        {
            _diceOverlay?.CancelAndHide();
        }

        private static string PlayerFacingRejection(string errorCode, string technicalMessage)
        {
            string code = (errorCode ?? string.Empty).Trim().ToUpperInvariant();
            switch (code)
            {
                case "PLAYER_MESSAGE_INVALID": return "문자나 숫자가 포함된 행동을 입력해 주세요.";
                case "ADMIN_ACCESS_SKILL_MISMATCH":
                    string message = (technicalMessage ?? string.Empty).ToUpperInvariant();
                    if (message.Contains("RESTORE")) return "이 대상은 X 복구로 해결해야 합니다.";
                    if (message.Contains("SEARCH")) return "이 대상은 F 조사로 해결해야 합니다.";
                    if (message.Contains("CONNECT")) return "이 대상은 C 연결로 해결해야 합니다.";
                    if (message.Contains("DELETE")) return "이 대상은 R 공격으로 해결해야 합니다.";
                    if (message.Contains("COPY")) return "이 대상은 E 복제로 해결해야 합니다.";
                    return "이 대상에는 다른 관리자 키보드 스킬이 필요합니다.";
                case "OUT_OF_RANGE":
                case "OUTOFRANGE": return "대상이 너무 멉니다. 더 가까이 이동하세요.";
                case "INSUFFICIENT_FOCUS":
                case "INSUFFICIENTRESOURCE": return "집중력이 부족합니다. 보급이나 휴식이 필요합니다.";
                case "DEPENDENCY_NOT_REVEALED": return "먼저 F로 대상의 약점을 조사하세요.";
                case "QUESTCONDITIONMISSING":
                    string conditionMessage = (technicalMessage ?? string.Empty).ToUpperInvariant();
                    if (conditionMessage.Contains("SEARCH") || conditionMessage.Contains("DEPENDENCY"))
                        return "먼저 F로 대상의 약점을 조사하세요.";
                    if (conditionMessage.Contains("ADMIN"))
                        return "ROOT_SYSTEM 개입에는 관리자 권한 3단계가 필요합니다.";
                    return "아직 필요한 퀘스트 조건을 충족하지 못했습니다.";
                case "DESTINATION_OCCUPIED": return "선택한 타일에는 이미 다른 대상이 있습니다.";
                case "PATH_BLOCKED":
                case "TRAVEL_PATH_BLOCKED": return "그 위치까지 이동할 수 있는 길이 없습니다.";
                case "ROOT_SYSTEM_ACCESS_DENIED":
                    return "루트 시스템은 아직 봉인되어 있습니다. 관리자 권한 3단계와 내부 붕괴 단서를 모두 확보해야 합니다.";
                case "ENCOUNTER_ACTION_REQUIRED":
                    return "현재 조우를 먼저 해결해야 다른 지역으로 이동할 수 있습니다.";
                case "DESTINATION_AREA_MISMATCH":
                    return "목적지와 지역 정보가 일치하지 않아 이동을 중단했습니다. 지도를 다시 동기화해 주세요.";
                case "UNDO_NOT_AVAILABLE": return "되돌릴 수 있는 행동 기록이 부족합니다.";
                case "RESTORE_NOT_AVAILABLE": return "지금 복구할 수 있는 상태가 없습니다.";
                case "CHOICE_REQUIRED": return "이야기 선택이 남아 있습니다. 표시된 선택지를 고르거나 T로 직접 입력해 주세요.";
                case "RUN_VERSION_CONFLICT":
                case "RUNVERSIONCONFLICT": return "최신 게임 상태를 다시 불러왔습니다. 행동을 다시 선택하세요.";
                case "NOT_FOUND":
                case "RUN_NOT_FOUND": return "서버 런을 찾지 못해 마지막 로컬 체크포인트로 복구합니다.";
                case "NETWORK_ERROR": return "서버에 연결하지 못했습니다. 잠시 후 다시 시도하세요.";
                case "TARGET_INVALID":
                    string targetMessage = technicalMessage ?? string.Empty;
                    if (targetMessage.IndexOf("SEARCH", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        targetMessage.IndexOf("F 조사", StringComparison.Ordinal) >= 0)
                        return "먼저 F로 대상의 약점을 조사하세요.";
                    return "현재 선택한 대상에는 이 행동을 사용할 수 없습니다.";
                case "INVALIDTARGET":
                case "ENTITY_NOT_FOUND":
                case "ENTITYNOTFOUND": return "현재 선택한 대상에는 이 행동을 사용할 수 없습니다.";
                default: return "지금은 이 행동을 실행할 수 없습니다. 대상과 스킬 조건을 확인하세요.";
            }
        }

        private static bool ContainsMeaningfulPlayerText(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            for (int i = 0; i < text.Length; i++)
                if (char.IsLetterOrDigit(text[i])) return true;
            return false;
        }

        private IEnumerator SubmitServerTravel(TurnRequest request)
        {
            EnsureRuntimeClients();
            if (!_selectionController.SelectedCoord.HasValue)
            {
                _lastOutcome = "DESTINATION REQUIRED";
                _lastAttempt = "안전 탐색 목적지를 먼저 지도에서 선택하세요.";
                _lastExplanation = "목적지가 없는 이동 요청은 서버에 보내지 않았고 의미 있는 턴도 소비하지 않았습니다.";
                PlaySfx(AssetClip("UiCancelSound"));
                yield break;
            }

            _serverPending = true;
            _gmPending = false;
            _serverStatus = "안전 탐색 경로 검증 중";
            GridCoord destinationCoord = _selectionController.SelectedCoord.Value;
            TryGetPlayerPosition(_service.CurrentView, out GridCoord playerPositionBefore);
            int campaignTurnBefore = _serverRun.currentTurn;
            string layoutHashBefore = _serverRun.world?.layoutHash;
            TurnGatewayResult gatewayResult = null;
            yield return _turnCoordinator.Submit(request, value => gatewayResult = value);

            _serverPending = false;
            if (gatewayResult == null || !gatewayResult.IsSuccess)
            {
                if (RecoverMissingServerRun(gatewayResult?.ErrorCode, gatewayResult?.ErrorMessage))
                    yield break;
                PresentActionRejection(gatewayResult?.ErrorCode, gatewayResult?.ErrorMessage);
                if (gatewayResult == null || gatewayResult.ErrorCode == "NETWORK_ERROR" || gatewayResult.ErrorCode == "RUN_VERSION_CONFLICT")
                    yield return ResyncServerRun();
                yield break;
            }

            GameApiClient.CommittedNavigation committed = gatewayResult.Payload as GameApiClient.CommittedNavigation;
            if (committed == null)
            {
                PresentActionRejection("EMPTY_RESPONSE", "Server reported success but returned no committed navigation.");
                yield break;
            }
            GameApiClient.NavigationSnapshot navigation = committed.Navigation;
            _serverRun = committed.Run;
            bool encounterOpened = (navigation != null && navigation.encounterOpened) ||
                                   IsOpenServerEncounter(_serverRun.activeEncounter);
            GameApiClient.PositionSnapshot encounterStaging = navigation?.encounter?.stagingPosition ??
                                                                navigation?.stagingPosition ??
                                                                navigation?.activeEncounter?.stagingPosition ??
                                                                _serverRun.activeEncounter?.stagingPosition;
            _encounterMoveRequired = encounterOpened;
            _encounterReason = FirstNonEmpty(navigation?.encounter?.reason, navigation?.encounterReason,
                navigation?.activeEncounter?.reason,
                _serverRun.activeEncounter?.reason);
            _encounterStagingCoord = encounterStaging == null
                ? (GridCoord?)null
                : new GridCoord(encounterStaging.x, encounterStaging.y);
            bool invariantHeld = navigation != null && !navigation.campaignTurnConsumed &&
                                 navigation.campaignTurnBefore == campaignTurnBefore &&
                                 navigation.campaignTurnAfter == campaignTurnBefore &&
                                 _serverRun.currentTurn == campaignTurnBefore &&
                                 string.Equals(layoutHashBefore, _serverRun.world?.layoutHash, StringComparison.Ordinal);
            string actorName = PlayerDisplayName(_service.CurrentView);
            bool oneTileMovement = playerPositionBefore.ManhattanDistance(destinationCoord) == 1;
            bool presentTravelEvent = encounterOpened || navigation?.storyEventTriggered == true;
            ApplyTurnPresentation(ServerTurnPresentationAdapter.FromNavigation(
                navigation,
                _serverRun,
                campaignTurnBefore,
                destinationCoord.X,
                destinationCoord.Y,
                encounterOpened,
                _encounterReason,
                invariantHeld,
                GmEnabled,
                actorName), !oneTileMovement || presentTravelEvent);
            RecoverPendingChoiceSet(_serverRun.pendingChoiceSet, false);
            SyncRestorableCandidateFromServer();
            TryGetPlayerPosition(_service.CurrentView, out GridCoord playerPositionAfter);
            List<GridCoord> visualPath = _pathPlanner.FindServerVisualPath(
                navigation, _serverRun, playerPositionBefore, playerPositionAfter);
            BeginPlayerPathAnimation(visualPath, _service.CurrentView,
                !oneTileMovement || presentTravelEvent);
            SyncServerEntityVisuals(_serverRun);
            if (!oneTileMovement || presentTravelEvent)
                QueueServerSceneSequence(navigation?.sceneSequence);
            _selectionController.ClearToCoord(encounterOpened ? _encounterStagingCoord : null);
            UpdateSelectionVisual(_service.CurrentView);
            _runSessionController?.RememberServerRun(_serverRun.id);
            _serverStatus = encounterOpened
                ? "ACTIVE ENCOUNTER · 다음 행동은 의미 턴"
                : committed.FromIdempotencyCache ? "탐색 이동 멱등 응답"
                : navigation != null && navigation.storyEventTriggered
                    ? "탐색 사건 발생 · 서버 판정 완료"
                    : "안전 탐색 이동 완료 · 다음 사건까지 " + StepsUntilStoryEvent(_serverRun) + "칸";
            PlaySfx(AssetClip("UiAcceptSound"));
        }

        private static int StepsUntilStoryEvent(GameApiClient.RunSnapshot run)
        {
            if (run == null) return 0;
            return Mathf.Max(0, run.nextStoryEventDistance - run.travelDistance);
        }

        private IEnumerator ResyncServerRun()
        {
            EnsureRuntimeClients();
            if (_serverRun == null)
                yield break;
            GameApiClient.Result<GameApiClient.RunSnapshot> result = null;
            yield return _gameApi.GetRun(_serverRun.id, value => result = value);
            if (result != null && result.IsSuccess)
            {
                _serverRun = result.Value;
                SyncEncounterStateFromServer();
                SyncRestorableCandidateFromServer();
                RecoverPendingChoiceSet(_serverRun.pendingChoiceSet, true);
                SyncServerEntityVisuals(_serverRun);
                _serverStatus = "최신 권위 상태 재동기화 완료";
            }
            else
            {
                RecoverMissingServerRun(result?.ErrorCode, result?.ErrorMessage);
            }
        }

        private static StorySequencePage[] FixedOpeningMonologue()
        {
            return new[]
            {
                new StorySequencePage("MONOLOGUE", CampaignCatalog.ProtagonistName,
                    "……여기가 코드리아인가. 조용해 보이지만, 곳곳의 모습이 어딘가 조금씩 어긋나 있어."),
                new StorySequencePage("MONOLOGUE", CampaignCatalog.ProtagonistName,
                    "관리자 키보드는 내 손에 있지만 정답까지 알려 주지는 않겠지. 지우기 전에 먼저 살펴보고, 길을 막는 존재가 있다면 왜 그러는지부터 들어 보자."),
                new StorySequencePage("MONOLOGUE", CampaignCatalog.ProtagonistName,
                    "이 세계를 정복할 필요는 없어. 흘러가는 이야기를 지켜보다가, 정말 필요한 순간에만 내가 할 수 있는 선택을 하면 돼.")
            };
        }

        private void StartNewRun()
        {
            if (_serverPending)
                return;
            if (_runSessionController == null)
                throw new InvalidOperationException("Game Root에는 Run Session Controller 참조가 필요합니다.");
            StartCoroutine(StartNewSession());
        }

        private void ContinueRun()
        {
            if (_serverPending)
                return;
            if (_runSessionController == null)
                throw new InvalidOperationException("Game Root에는 Run Session Controller 참조가 필요합니다.");
            StartCoroutine(ContinueSession());
        }

        /// <summary>새 런의 서버 연결·로컬 폴백 결과를 세션 오브젝트에서 받아 게임 화면을 연다.</summary>
        private IEnumerator StartNewSession()
        {
            _serverPending = true;
            KeyboardWandererRunSessionResult result = null;
            yield return _runSessionController.StartNew(value => result = value);
            _serverPending = false;
            ApplyRunSessionResult(result);
        }

        /// <summary>이어하기 결과를 세션 오브젝트에서 받아 기존 런 상태로 복귀한다.</summary>
        private IEnumerator ContinueSession()
        {
            _serverPending = true;
            KeyboardWandererRunSessionResult result = null;
            yield return _runSessionController.Continue(value => result = value);
            _serverPending = false;
            ApplyRunSessionResult(result);
        }

        /// <summary>세션 결과를 현재 실행 경로에 연결하고 실제 게임 오브젝트 구성을 시작한다.</summary>
        private void ApplyRunSessionResult(KeyboardWandererRunSessionResult result)
        {
            if (result == null)
            {
                _serverStatus = _runSessionController != null && !string.IsNullOrWhiteSpace(_runSessionController.Status)
                    ? _runSessionController.Status
                    : "런 시작 응답 없음";
                PublishPresentationState(PresentationChange.Screen);
                return;
            }

            _serverOnline = result.ServerOnline;
            _serverCampaign = result.ServerCampaign;
            _serverRun = result.ServerRun;
            _serverStatus = result.Status;
            _sessionReplacedMissingContinue = result.ReplacedMissingContinue;
            if (_serverOnline && _serverRun != null)
                SyncEncounterStateFromServer();
            StartRun(result.Service, result.Resumed);
        }

        private void StartRun(LocalTurnService service, bool resumed)
        {
            _runGeneration++;
            if (_scenePlaybackCoroutine != null)
            {
                StopCoroutine(_scenePlaybackCoroutine);
                _scenePlaybackCoroutine = null;
            }
            CancelOptionalNarrativeRequest();
            _choiceSubmissionPending = false;
            _pendingNarrativeChoiceLabel = string.Empty;
            _showPause = false;
            _pauseStartedAt = 0f;
            _settingsReturnToPause = false;
            _naturalLanguageComposeMode = false;
            _nextAmbientWanderAt = Time.unscaledTime + AmbientWanderInterval;
            ClearPlayerMessageSubmission();
            _playerWalking = false;
            _lastPresentationContinuesWithMovement = false;
            _directionalInputHeld = false;
            ClearBufferedDirectionalMove();
            _reopenDialogueAfterWalk = false;
            _runEndCutscenePlayed = false;
            _suppressDialogueReopenAfterWalk = false;
            _service = service;
            // Starting/rebinding a run is a hard UI ownership boundary. Clear the
            // previous run's input lock, options and whole modal root immediately;
            // leaving it merely non-interactable still lets its transparent children
            // intercept clicks intended for the tutorial, HUD and Escape button.
            _inputRouter?.SetNarrativeChoiceMode(false);
            _inputRouter?.SetNarrativeOverlayMode(false);
            _sceneUi?.ResetDialogueChoiceSession();
            Debug.Log("[KW.Session] event=StartRun service=" + (_service == null ? "null" : "ready") +
                      " serverOnline=" + _serverOnline + " serverRun=" +
                      (_serverRun == null ? "null" : _serverRun.id) + " resumed=" + resumed);
            _turnGateway = _serverOnline && _serverRun != null
                ? new ServerTurnGateway(_gameApi, () => _serverRun?.id)
                : new LocalTurnGateway(service);
            _runPresentationAdapter = _serverOnline && _serverRun != null
                ? new ServerRunPresentationAdapter(() => _serverRun, () => _serverCampaign)
                : new LocalRunPresentationAdapter();
            _runPresentationModel = PresentationModel(_service.CurrentView);
            if (_turnCoordinator == null)
                throw new InvalidOperationException("Game Root에는 Authored Turn Coordinator 참조가 필요합니다.");
            _turnCoordinator.Configure(_turnGateway);
            _worldSeed = _service.CurrentView.WorldSeed;
            _sessionLog.Clear();
            _lastRestorableTarget = null;
            _lastRestorableCoord = null;
            _lastRestorableName = null;
            _encounterMoveRequired = false;
            _encounterStagingCoord = null;
            _encounterReason = null;
            _lastModifier = 0;
            _lastModifierBreakdown = "--";
            _lastDifficulty = 0;
            _lastMechanicalScore = 0;
            _lastActionContext = "--";
            _lastStateChanges = "아직 확정된 상태 변화가 없습니다.";
            _lastDialogue = Array.Empty<string>();
            _dialogueSpeakerNames.Clear();
            _dialogueSpeakerSprites.Clear();
            _lastStorySequence = Array.Empty<StorySequencePage>();
            _lastNextInterventionReason = string.Empty;
            _lastChoiceSetId = string.Empty;
            _lastNarrativeChoices = Array.Empty<NarrativeChoiceOption>();
            _choiceStatusMessage = string.Empty;
            _lastSuggestedSkillIds = Array.Empty<string>();
            _pendingRuntimeRenderSequence = Array.Empty<GameApiClient.SceneActionSnapshot>();
            _lastNarrativeFallbackUsed = true;
            _lastNarrativeModel = "deterministic";
            // Online runs already open with an authored NPC approach and a server-provided
            // intervention. The legacy tutorial replaces the entire dialogue view, so showing
            // it here would hide the NPC's first line. Keep it only for the local fallback.
            bool replacedMissingContinue = !resumed && !_serverOnline && _sessionReplacedMissingContinue;
            _sessionReplacedMissingContinue = false;
            _tutorialPresenter.Start(!replacedMissingContinue && !resumed && !_serverOnline &&
                                     KeyboardWandererPreferences.GetInt(TutorialCompletedKey, 0) == 0);
            _dialoguePresenter.Reset();

            if (resumed)
            {
                _lastD20 = 0;
                _lastOutcome = "RESTORED";
                _lastAttempt = _serverOnline
                    ? "서버의 최신 권위 상태를 다시 불러왔습니다."
                    : "검증된 로컬 스냅샷에서 폴백 상태를 복원했습니다.";
                _lastExplanation = _serverOnline
                    ? "서버 run version과 layout hash를 기준으로 이어갑니다."
                    : "서버가 없어 로컬 규칙 엔진 상태로만 이어갑니다.";
                _lastNarrative = StoryBeat(_service.CurrentView);
                AddLog("턴 " + CurrentTurn(_service.CurrentView) + " 런 복원 완료");
            }
            else
            {
                _lastD20 = 0;
                _lastOutcome = replacedMissingContinue ? "RESTORED" : "READY";
                _lastAttempt = replacedMissingContinue
                    ? "저장된 런을 찾지 못해 새 로컬 여정으로 안전하게 전환했습니다."
                    : "MOVE 목적지 또는 관리자 키보드 스킬과 대상을 선택하세요.";
                _lastExplanation = replacedMissingContinue
                    ? "이전 서버 런의 선택지와 입력은 폐기했습니다. 현재 화면의 새 목표부터 진행하세요."
                    : "자연어 없이 선택된 스킬·대상·장면 상태만으로 권위 판정을 실행합니다.";
                GameApiClient.NarrativeSnapshot opening = _serverOnline ? _serverRun?.openingNarrative : null;
                _lastNarrative = replacedMissingContinue
                    ? "이전 권위 런이 더 이상 존재하지 않아 새 로컬 체크포인트를 만들었습니다. 현재 위치와 목표는 새 여정의 상태입니다."
                    : !string.IsNullOrWhiteSpace(opening?.body)
                    ? opening.body
                    : CampaignPremise(_service.CurrentView);
                _lastStorySequence = replacedMissingContinue
                    ? Array.Empty<StorySequencePage>()
                    : opening != null
                    ? ServerTurnPresentationAdapter.BuildStorySequence(opening, _serverRun, _lastNarrative)
                    : FixedOpeningMonologue();
                _lastDialogue = opening?.dialogue ?? Array.Empty<string>();
                _lastNextInterventionReason = replacedMissingContinue
                    ? "새 로컬 여정을 시작했습니다. 가까운 목표를 조사하거나 이동할 방향을 선택하세요."
                    : opening?.nextIntervention?.reason ??
                    "우선 가까운 곳부터 천천히 살펴보자. 길을 따라 움직이거나, 눈에 걸리는 존재의 사정을 조사해도 좋겠다.";
                _lastSuggestedSkillIds = opening?.nextIntervention?.suggestedSkillIds ?? new[] { "SEARCH", "CONNECT" };
                _lastNarrativeFallbackUsed = opening?.fallbackUsed ?? true;
                _lastNarrativeModel = opening?.model ?? "deterministic";
                AddLog("고정 월드 1회 생성 완료 · seed " + _worldSeed + " · " + ShortHash(LayoutHash(_service.CurrentView)));
            }

            if (_serverOnline && _serverRun != null)
                RecoverPendingChoiceSet(_serverRun.pendingChoiceSet, true);

            if (resumed)
            {
                if (string.IsNullOrWhiteSpace(_lastNextInterventionReason))
                    _lastNextInterventionReason =
                        "복원된 위치와 목표를 확인했습니다. 어디로 이동하거나 어떤 방식으로 다시 개입할까요?";
                if (_lastSuggestedSkillIds == null || _lastSuggestedSkillIds.Length == 0)
                    _lastSuggestedSkillIds = new[] { "SEARCH", "CONNECT" };
                string[] restoredPages = BuildDialoguePages(_lastNarrative);
                _dialoguePresenter.Restore(Mathf.Max(0, restoredPages.Length - 1), false);
            }

            // A run begins in neutral movement mode. Story beats may recommend a skill,
            // but recommendations must never look like a player selection.
            _selectionController.ResetSelection(AbilityKind.Move);
            _cameraInspectCoord = null;
            _cameraInspectUntil = 0f;
            SyncRestorableCandidateFromServer();
            if (!_serverOnline)
                SyncLocalEncounterState(_service.CurrentView);
            BuildWorld(_service.CurrentView);
            if (_serverOnline && _serverRun != null)
                SyncServerEntityVisuals(_serverRun);
            else
                SyncEntityVisuals(_service.CurrentView);
            _screenMode = ScreenMode.Playing;
            _inputRouter?.SetUiOverlayMode(false);
            _cameraController?.SetEnabled(true);
            SnapCameraToPlayer();
            RefreshDynamicMusic(_service.CurrentView, PresentationModel(_service.CurrentView));
            // Keep a seed-aligned deterministic checkpoint even while the server is
            // authoritative. If that run is later deleted/restarted, NOT_FOUND can
            // recover into this known-good local state instead of a stale prior seed.
            LocalRunSaveService.Save(_service);
        }

        private static AbilityKind FirstObjectiveAbility(RunView view)
        {
            if (view != null)
            {
                for (int i = 0; i < view.RequiredBeats.Count; i++)
                {
                    CampaignBeatState beat = view.RequiredBeats[i];
                    if (!beat.IsCompleted && !beat.IsSkipped)
                        return beat.TriggerAbility;
                }
            }
            return AbilityKind.Move;
        }

        private void ShowTitle()
        {
            _sceneUi?.CloseInventoryQuestOverlay();
            _inputRouter?.SetUiOverlayMode(true);
            _screenMode = ScreenMode.Title;
            _showPause = false;
            _pauseStartedAt = 0f;
            _settingsReturnToPause = false;
            CancelOptionalNarrativeRequest();
            _cameraController?.SetEnabled(true);
            SetMusic(_assets != null ? _assets.AdventureMusic ?? _assets.VillageMusic : null);
        }

        private void BuildWorld(RunView view)
        {
            if (authoredWorld == null || authoredWorld.TerrainTilemap == null ||
                authoredWorld.SelectionCursor == null || authoredWorld.RuntimeEntities == null ||
                authoredWorld.RuntimeLandmarks == null || authoredWorld.RuntimeEffects == null)
                throw new InvalidOperationException(
                    "Keyboard Wanderer requires a fully configured authored world. Run the project converter or repair the scene references.");
            GameApiClient.WorldSnapshot serverWorld = _serverOnline ? _serverRun?.world : null;
            bool useServerWorld = serverWorld != null && serverWorld.HasCompleteLayout;
            string layoutHash = useServerWorld ? serverWorld.layoutHash : view.Region.LayoutHash;
            bool rebuildStaticWorld = !string.Equals(_renderedLayoutHash, layoutHash, StringComparison.Ordinal);
            foreach (KeyValuePair<Guid, KeyboardWandererEntityVisualState> pair in _entityVisuals)
                ReleaseEntityVisual(pair.Value);
            _entityVisuals.Clear();
            if (rebuildStaticWorld)
            {
                _decorationRenderer.ReleaseAll();
                for (int i = 0; i < _runtimeTiles.Count; i++)
                    if (_runtimeTiles[i] != null)
                        Destroy(_runtimeTiles[i]);
                _runtimeTiles.Clear();
            }
            Vector2 origin = MapOrigin(view);
            int worldWidth = useServerWorld ? serverWorld.width : view.Region.Width;
            int worldHeight = useServerWorld ? serverWorld.height : view.Region.Height;

            // One Tilemap renders the immutable 160x160 terrain. Online, the server snapshot is the
            // sole geometry authority; the local map remains only an offline continuity fallback.
            _worldRoot = authoredWorld.gameObject;
            _worldRoot.name = "Authored World · " + ShortHash(layoutHash);
            authoredWorld.ResetRuntimeContent(rebuildStaticWorld);
            Tilemap tilemap = authoredWorld.TerrainTilemap;
            tilemap.transform.position = new Vector3(origin.x, origin.y, 0f);
            _selectionRenderer = authoredWorld.SelectionCursor;
            TilemapRenderer activeTilemapRenderer = tilemap.GetComponent<TilemapRenderer>();
            if (activeTilemapRenderer != null)
                activeTilemapRenderer.sortingOrder = TerrainSortingOrder;
            var tilePalette = new Dictionary<string, Tile>(StringComparer.Ordinal);

            if (rebuildStaticWorld)
            {
                // 1패스: 칸마다 지형 종류와 타일 시트를 미리 정해 둔다. 오토타일 마스크는 이웃이
                // 같은 시트를 쓰는지로 결정되므로, 전체 배열이 있어야 2패스에서 이웃을 볼 수 있다.
                int tileCount = worldWidth * worldHeight;
                var tileKinds = new TileKind[tileCount];
                var biomeIds = new string[tileCount];
                var tileSheets = new Texture2D[tileCount];
                for (int y = 0; y < worldHeight; y++)
                {
                    for (int x = 0; x < worldWidth; x++)
                    {
                        int index = y * worldWidth + x;
                        var coord = new GridCoord(x, y);
                        tileKinds[index] = useServerWorld
                            ? TileKindForServer(serverWorld, serverWorld.tileCodes[y * serverWorld.width + x])
                            : view.Region.GetTile(coord).Kind;
                        biomeIds[index] = BiomeIdAt(view, coord);
                        tileSheets[index] = CodriaTerrainSheet(biomeIds[index], tileKinds[index]);
                    }
                }

                // 2패스: 이웃 네 방향이 같은 시트면 비트를 세워 타일 인덱스를 만든다. 월드 밖은
                // 같은 지형으로 취급해 가장자리가 잘려 보이지 않게 한다.
                for (int y = 0; y < worldHeight; y++)
                {
                    for (int x = 0; x < worldWidth; x++)
                    {
                        int index = y * worldWidth + x;
                        TileKind tileKind = tileKinds[index];
                        string biomeId = biomeIds[index];
                        Texture2D sheet = tileSheets[index];

                        Sprite sprite;
                        Color tint;
                        string paletteKey;
                        if (sheet != null)
                        {
                            int mask = 0;
                            if (y + 1 >= worldHeight || tileSheets[index + worldWidth] == sheet) mask |= 1; // N
                            if (x + 1 >= worldWidth || tileSheets[index + 1] == sheet) mask |= 2;           // E
                            if (y - 1 < 0 || tileSheets[index - worldWidth] == sheet) mask |= 4;            // S
                            if (x - 1 < 0 || tileSheets[index - 1] == sheet) mask |= 8;                     // W
                            sprite = _visualAssetLibrary.CodriaAutotileSprite(sheet, mask);
                            tint = Color.white; // 시트에 지역 색이 이미 입혀져 있어 덧칠하지 않는다.
                            paletteKey = sheet.name + ":" + mask;
                        }
                        else
                        {
                            // 매니페스트에 시트가 없으면 기존 Ninja Adventure 경로로 되돌아간다.
                            TileAppearance(tileKind, biomeId, new GridCoord(x, y), out sprite, out tint);
                            paletteKey = biomeId + ":" + tileKind;
                        }

                        if (!tilePalette.TryGetValue(paletteKey, out Tile visualTile))
                        {
                            visualTile = ScriptableObject.CreateInstance<Tile>();
                            visualTile.name = "Runtime " + paletteKey + " Tile";
                            visualTile.sprite = sprite;
                            visualTile.color = Color.white;
                            visualTile.flags = TileFlags.None;
                            tilePalette.Add(paletteKey, visualTile);
                            _runtimeTiles.Add(visualTile);
                        }
                        var cell = new Vector3Int(x, y, 0);
                        tilemap.SetTile(cell, visualTile);
                        tilemap.SetTileFlags(cell, TileFlags.None);
                        tilemap.SetColor(cell, tint);
                    }
                }
                CreateBiomeDecorations(view, origin, useServerWorld, worldWidth, worldHeight);
                CreateCampaignLandmarkMarkers(view, origin, useServerWorld);
                _renderedLayoutHash = layoutHash;
            }

            _selectionRenderer.sprite = _grassSprite;
            _selectionRenderer.color = new Color(1f, 0.85f, 0.35f, 0.42f);
            _selectionRenderer.sortingOrder = 900;
            _selectionRenderer.enabled = false;
            ScaleSprite(_selectionRenderer.transform, _grassSprite, 1.02f);
        }

        private void SyncEntityVisuals(RunView view)
        {
            var active = new HashSet<Guid>();
            Vector2 origin = MapOrigin(view);
            for (int i = 0; i < view.Entities.Count; i++)
            {
                EntityView entity = view.Entities[i];
                active.Add(entity.EntityId);
                if (!_entityVisuals.TryGetValue(entity.EntityId, out KeyboardWandererEntityVisualState visual))
                {
                    visual = CreateEntityVisual(entity, origin);
                    _entityVisuals.Add(entity.EntityId, visual);
                }

                Vector3 authoritativePosition = WorldPosition(origin, entity.Position);
                if (!visual.IsPlayer || !_playerWalking)
                {
                    visual.TargetPosition = authoritativePosition;
                    if (Vector3.Distance(visual.Root.transform.position, visual.TargetPosition) > 8f)
                        visual.Root.transform.position = visual.TargetPosition;
                }
                UpdateEntityHealthVisual(visual, entity);
            }

            var removed = new List<Guid>();
            foreach (KeyValuePair<Guid, KeyboardWandererEntityVisualState> pair in _entityVisuals)
            {
                if (active.Contains(pair.Key))
                    continue;
                DestroyEntityVisual(pair.Value);
                removed.Add(pair.Key);
            }
            for (int i = 0; i < removed.Count; i++)
                _entityVisuals.Remove(removed[i]);
        }

        private void SyncServerEntityVisuals(GameApiClient.RunSnapshot run)
        {
            if (run?.entities == null || _worldRoot == null || _service == null)
                return;
            var active = new HashSet<Guid>();
            Vector2 origin = MapOrigin(_service.CurrentView);
            for (int i = 0; i < run.entities.Length; i++)
            {
                GameApiClient.EntitySnapshot entity = run.entities[i];
                if (entity == null || entity.position == null || !Guid.TryParse(entity.id, out Guid id))
                    continue;
                active.Add(id);
                bool isPlayer = string.Equals(entity.id, run.playerEntityId, StringComparison.OrdinalIgnoreCase);
                bool hostile = string.Equals(entity.kind, "enemy", StringComparison.OrdinalIgnoreCase);
                string rootComponent = entity.state?.rootComponent;
                if (!_entityVisuals.TryGetValue(id, out KeyboardWandererEntityVisualState visual))
                {
                    string componentSuffix = string.IsNullOrWhiteSpace(rootComponent) ? string.Empty : " · " + rootComponent;
                    GameObject root = CreateEntityViewObject(
                        (entity.name ?? "Entity") + " · " + entity.assetId + componentSuffix,
                        hostile,
                        out KeyboardWandererEntityView authoredView,
                        out SpriteRenderer renderer);
                    var coord = new GridCoord(entity.position.x, entity.position.y);
                    root.transform.position = WorldPosition(origin, coord);
                    Sprite sprite = SpriteForServerEntity(entity, isPlayer);
                    renderer.sprite = sprite;
                    renderer.sortingOrder = 500 - entity.position.y * 4;
                    float desired = VisualSize(isPlayer, hostile);
                    ScaleSprite(authoredView != null ? renderer.transform : root.transform, sprite, desired);
                    visual = new KeyboardWandererEntityVisualState
                    {
                        AuthoredView = authoredView,
                        Root = root,
                        Renderer = renderer,
                        IdleFrames = FramesForAsset(entity.assetId, entity.kind, isPlayer),
                        WalkFrames = isPlayer ? _playerWalkFrames : Array.Empty<Sprite>(),
                        AttackFrames = isPlayer ? _playerAttackFrames : Array.Empty<Sprite>(),
                        TargetPosition = root.transform.position,
                        BaseColor = ServerEntityTint(entity),
                        DesiredSize = desired,
                        IsPlayer = isPlayer,
                        IsHostile = hostile
                    };
                    visual.Animator = ConfigureEntityAnimator(renderer,
                        AnimatorForAsset(entity.assetId, entity.kind, isPlayer));
                    KeyboardWandererEntityAnimationDriver.InitializeAmbientWander(visual, id);
                    if (!string.IsNullOrWhiteSpace(rootComponent))
                        visual.RootComponentLabel = CreateRootComponentLabel(rootComponent, entity.name, visual.BaseColor, authoredView);
                    renderer.color = visual.BaseColor;
                    if (hostile && entity.state != null && entity.state.maxHp > 0)
                    {
                        ConfigureHealthBars(visual, entity.name ?? "Enemy");
                    }
                    _entityVisuals.Add(id, visual);
                }

                Vector3 authoritativePosition = WorldPosition(origin, new GridCoord(entity.position.x, entity.position.y));
                if (!visual.IsPlayer || !_playerWalking)
                {
                    visual.TargetPosition = authoritativePosition;
                    if (Vector3.Distance(visual.Root.transform.position, visual.TargetPosition) > 8f)
                        visual.Root.transform.position = visual.TargetPosition;
                }
                if (visual.RootComponentLabel != null)
                    visual.RootComponentLabel.transform.position = visual.TargetPosition + new Vector3(0f, -0.72f, -0.2f);
                if (visual.Root != null)
                {
                    // A just-committed defeat/flee remains visible only long enough for
                    // its authoritative exit action. Resumed/resynced inactive snapshots
                    // never reappear as targetable-looking world actors.
                    bool visible = IsServerEntityVisuallyActive(entity) || HasPendingServerExitAction(entity.id);
                    if (visual.Root.activeSelf != visible)
                        visual.Root.SetActive(visible);
                }
                UpdateServerHealthVisual(visual, entity);
            }

            var removed = new List<Guid>();
            foreach (KeyValuePair<Guid, KeyboardWandererEntityVisualState> pair in _entityVisuals)
            {
                if (active.Contains(pair.Key))
                    continue;
                DestroyEntityVisual(pair.Value);
                removed.Add(pair.Key);
            }
            for (int i = 0; i < removed.Count; i++)
                _entityVisuals.Remove(removed[i]);
        }

        private static bool IsServerEntityVisuallyActive(GameApiClient.EntitySnapshot entity)
        {
            if (entity?.state == null)
                return entity != null;
            return !entity.state.disabled && !entity.state.defeated && !entity.state.fled &&
                   !entity.state.adminAccessResolved &&
                   (entity.state.maxHp <= 0 || entity.state.hp > 0);
        }

        private bool HasPendingServerExitAction(string entityId)
        {
            return SequenceContainsServerExit(_lastServerSceneSequence, entityId) ||
                   SequenceContainsServerExit(_pendingRuntimeRenderSequence, entityId);
        }

        private static bool SequenceContainsServerExit(
            GameApiClient.SceneActionSnapshot[] sequence,
            string entityId)
        {
            if (sequence == null || string.IsNullOrWhiteSpace(entityId))
                return false;
            for (int i = 0; i < sequence.Length; i++)
            {
                GameApiClient.SceneActionSnapshot action = sequence[i];
                if (action == null || !string.Equals(action.actorId, entityId, StringComparison.OrdinalIgnoreCase))
                    continue;
                string type = (action.type ?? string.Empty).Trim();
                if (string.Equals(type, "DEFEATED", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(type, "FLEE", StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private void HideServerActorIfInactive(string entityId)
        {
            if (_serverRun?.entities == null || string.IsNullOrWhiteSpace(entityId))
                return;
            for (int i = 0; i < _serverRun.entities.Length; i++)
            {
                GameApiClient.EntitySnapshot entity = _serverRun.entities[i];
                if (entity == null || !string.Equals(entity.id, entityId, StringComparison.OrdinalIgnoreCase) ||
                    IsServerEntityVisuallyActive(entity))
                    continue;
                if (Guid.TryParse(entityId, out Guid id) &&
                    _entityVisuals.TryGetValue(id, out KeyboardWandererEntityVisualState visual) &&
                    visual.Root != null)
                    visual.Root.SetActive(false);
                return;
            }
        }

        private void UpdateServerHealthVisual(KeyboardWandererEntityVisualState visual, GameApiClient.EntitySnapshot entity)
        {
            if (!visual.IsHostile || visual.HealthBack == null || visual.HealthFill == null || entity.state == null)
                return;
            int max = Mathf.Max(1, entity.state.maxHp);
            float ratio = Mathf.Clamp01(entity.state.hp / (float)max);
            bool visible = IsServerEntityVisuallyActive(entity) && entity.state.maxHp > 0;
            visual.HealthBack.SetActive(visible);
            visual.HealthFill.SetActive(visible);
            Vector3 position = visual.TargetPosition + new Vector3(0f, 0.66f, -0.1f);
            visual.HealthBack.transform.position = position;
            visual.HealthBack.transform.localScale = new Vector3(0.78f, 0.09f, 1f);
            visual.HealthFill.transform.position = position + new Vector3(-0.39f * (1f - ratio), 0f, -0.01f);
            visual.HealthFill.transform.localScale = new Vector3(0.74f * ratio, 0.055f, 1f);
        }

        private KeyboardWandererEntityVisualState CreateEntityVisual(EntityView entity, Vector2 origin)
        {
            bool isPlayer = entity.EntityId == _service.CurrentView.PlayerEntityId;
            bool hostile = entity.Kind == EntityKind.Enemy;
            GameObject root = CreateEntityViewObject(
                entity.DisplayName + " · " + entity.AssetId,
                hostile,
                out KeyboardWandererEntityView authoredView,
                out SpriteRenderer renderer);
            root.transform.position = WorldPosition(origin, entity.Position);
            Sprite sprite = SpriteForEntity(entity);
            renderer.sprite = sprite;
            renderer.sortingOrder = 500 - entity.Position.Y * 4;
            float desired = VisualSize(isPlayer, hostile);
            ScaleSprite(authoredView != null ? renderer.transform : root.transform, sprite, desired);

            var visual = new KeyboardWandererEntityVisualState
            {
                AuthoredView = authoredView,
                Root = root,
                Renderer = renderer,
                IdleFrames = FramesForEntity(entity),
                WalkFrames = isPlayer ? _playerWalkFrames : Array.Empty<Sprite>(),
                AttackFrames = isPlayer ? _playerAttackFrames : Array.Empty<Sprite>(),
                TargetPosition = root.transform.position,
                BaseColor = EntityTint(entity),
                DesiredSize = desired,
                IsPlayer = isPlayer,
                IsHostile = entity.IsHostile
            };
            visual.Animator = ConfigureEntityAnimator(renderer,
                AnimatorForAsset(entity.AssetId, entity.Kind.ToString(), isPlayer));
            KeyboardWandererEntityAnimationDriver.InitializeAmbientWander(visual, entity.EntityId);
            renderer.color = visual.BaseColor;

            if (visual.IsHostile)
                ConfigureHealthBars(visual, entity.DisplayName);
            return visual;
        }

        private GameObject CreateEntityViewObject(
            string objectName,
            bool hostile,
            out KeyboardWandererEntityView authoredView,
            out SpriteRenderer renderer)
        {
            authoredView = _entityVisualFactory != null
                ? _entityVisualFactory.Acquire(objectName, hostile, _whiteSprite)
                : null;
            if (authoredView == null)
                throw new InvalidOperationException("Entity Visual Factory must reference its prefab and roots.");
            renderer = authoredView.ActorRenderer;
            if (renderer == null)
            {
                Destroy(authoredView.gameObject);
                throw new InvalidOperationException("Entity Visual prefab must reference its Actor SpriteRenderer.");
            }
            return authoredView.gameObject;
        }

        private float VisualSize(bool isPlayer, bool hostile)
        {
            if (authoringSettings == null)
                return isPlayer ? 1.34f : hostile ? 0.92f : 0.98f;
            return isPlayer
                ? authoringSettings.PlayerVisualSize
                : hostile ? authoringSettings.EnemyVisualSize : authoringSettings.NeutralVisualSize;
        }

        private void ConfigureHealthBars(KeyboardWandererEntityVisualState visual, string displayName)
        {
            if (visual.AuthoredView == null || visual.AuthoredView.HealthBack == null || visual.AuthoredView.HealthFill == null)
                throw new InvalidOperationException("Entity Visual prefab must contain authored Health Back and Health Fill renderers.");

            visual.HealthBack = visual.AuthoredView.HealthBack;
            visual.HealthFill = visual.AuthoredView.HealthFill;
            visual.HealthBack.name = displayName + " HP bg";
            visual.HealthFill.name = displayName + " HP";
            visual.HealthBack.SetActive(true);
            visual.HealthFill.SetActive(true);
        }

        private void DestroyEntityVisual(KeyboardWandererEntityVisualState visual)
        {
            ReleaseEntityVisual(visual);
        }

        private void ReleaseEntityVisual(KeyboardWandererEntityVisualState visual)
        {
            if (visual == null)
                return;
            DestroyDetachedVisual(visual.HealthBack, visual.Root);
            DestroyDetachedVisual(visual.HealthFill, visual.Root);
            DestroyDetachedVisual(visual.RootComponentLabel, visual.Root);
            if (visual.Root == null) return;
            KeyboardWandererEntityView view = visual.AuthoredView != null
                ? visual.AuthoredView
                : visual.Root.GetComponent<KeyboardWandererEntityView>();
            if (view == null)
            {
                Destroy(visual.Root);
                return;
            }
            _entityVisualFactory.Release(view);
        }

        private static void DestroyDetachedVisual(GameObject item, GameObject root)
        {
            if (item == null)
                return;
            if (root == null || !item.transform.IsChildOf(root.transform))
                Destroy(item);
        }

        private void UpdateEntityHealthVisual(KeyboardWandererEntityVisualState visual, EntityView entity)
        {
            if (!visual.IsHostile || visual.HealthBack == null || visual.HealthFill == null)
                return;
            int health = entity.Health;
            int max = Mathf.Max(1, entity.MaxHealth);
            float ratio = Mathf.Clamp01(health / (float)max);
            bool visible = health > 0;
            visual.HealthBack.SetActive(visible);
            visual.HealthFill.SetActive(visible);
            Vector3 position = visual.TargetPosition + new Vector3(0f, 0.66f, -0.1f);
            visual.HealthBack.transform.position = position;
            visual.HealthBack.transform.localScale = new Vector3(0.78f, 0.09f, 1f);
            visual.HealthFill.transform.position = position + new Vector3(-0.39f * (1f - ratio), 0f, -0.01f);
            visual.HealthFill.transform.localScale = new Vector3(0.74f * ratio, 0.055f, 1f);
        }

        private void UpdateAnimatedVisuals()
        {
            if (_entityAnimationDriver == null || _service == null)
                return;
            _entityAnimationDriver.Tick(
                _entityVisuals,
                _selectionController,
                _playerWalking,
                _playerActionUntil,
                PlayerWalkSpeed,
                MapOrigin(_service.CurrentView),
                TileSize,
                _assets?.NeopjukiAtlas != null,
                _playerWalkFrames,
                _playerWalkLeftFrames,
                _playerWalkUpFrames,
                _playerWalkDownFrames,
                _playerAttackFrames,
                _playerAttackLeftFrames,
                _playerAttackUpFrames,
                _playerAttackDownFrames,
                _playerActionFrames);
        }

        private void BeginPlayerPathAnimation(IReadOnlyList<GridCoord> path, RunView view,
            bool reopenDialogueAfterWalk = true)
        {
            if (path == null || path.Count < 2 || !TryGetPlayerVisual(view, out KeyboardWandererEntityVisualState visual))
                return;
            if (path.Count - 1 >= CinematicTravelPathThreshold && _cameraController?.TargetCamera != null)
            {
                BeginCinematicTravel(path[0], path[path.Count - 1], visual, view, reopenDialogueAfterWalk);
                return;
            }
            visual.MovementPath.Clear();
            Vector2 origin = MapOrigin(view);
            for (int i = 1; i < path.Count; i++)
                visual.MovementPath.Enqueue(WorldPosition(origin, path[i]));
            if (visual.MovementPath.Count == 0)
                return;
            visual.TargetPosition = visual.MovementPath.Dequeue();
            BeginWalkingPresentation(reopenDialogueAfterWalk);
            _cameraInspectCoord = null;
            _cameraInspectUntil = 0f;
        }

        private void BeginCinematicTravel(GridCoord start, GridCoord destination,
            KeyboardWandererEntityVisualState visual, RunView view, bool reopenDialogueAfterWalk)
        {
            Vector2 mapOrigin = MapOrigin(view);
            Vector3 startWorld = WorldPosition(mapOrigin, start);
            Vector3 destinationWorld = WorldPosition(mapOrigin, destination);
            Vector3 delta = destinationWorld - startWorld;
            Vector3 travelDirection = Mathf.Abs(delta.x) >= Mathf.Abs(delta.y)
                ? new Vector3(Mathf.Sign(delta.x), 0f, 0f)
                : new Vector3(0f, Mathf.Sign(delta.y), 0f);
            if (travelDirection.sqrMagnitude < 0.5f) travelDirection = Vector3.right;

            Camera camera = _cameraController.TargetCamera;
            float halfHeight = camera.orthographicSize;
            float halfWidth = halfHeight * Mathf.Max(0.1f, camera.aspect);
            float exitDistance = Mathf.Abs(travelDirection.x) > 0f ? halfWidth + 1.25f : halfHeight + 1.25f;
            Vector3 cameraCenter = camera.transform.position;
            Vector3 exit = new Vector3(
                Mathf.Abs(travelDirection.x) > 0f ? cameraCenter.x + travelDirection.x * exitDistance : startWorld.x,
                Mathf.Abs(travelDirection.y) > 0f ? cameraCenter.y + travelDirection.y * exitDistance : startWorld.y,
                startWorld.z);

            visual.MovementPath.Clear();
            visual.MovementPath.Enqueue(destinationWorld);
            visual.TargetPosition = exit;
            BeginWalkingPresentation(reopenDialogueAfterWalk);
            _cameraInspectCoord = start;
            _cameraInspectUntil = Time.unscaledTime + 10f;
            StartCoroutine(CompleteCinematicTravelExit(visual, destination, destinationWorld, exit,
                travelDirection, exitDistance));
        }

        private IEnumerator CompleteCinematicTravelExit(KeyboardWandererEntityVisualState visual,
            GridCoord destination, Vector3 destinationWorld, Vector3 exitWorld,
            Vector3 travelDirection, float entryDistance)
        {
            while (_playerWalking && visual?.Root != null &&
                   Vector3.SqrMagnitude(visual.Root.transform.position - exitWorld) > 0.01f)
                yield return null;
            if (!_playerWalking || visual?.Root == null) yield break;

            _cameraController.Snap(destinationWorld);
            _cameraInspectCoord = destination;
            _cameraInspectUntil = Time.unscaledTime + 10f;
            Vector3 entry = destinationWorld - travelDirection * entryDistance;
            entry.z = destinationWorld.z;
            visual.Root.transform.position = entry;
            visual.MovementPath.Clear();
            visual.TargetPosition = destinationWorld;
            Debug.Log("[KW.Travel] event=CinematicTransition destination=(" + destination.X + "," +
                      destination.Y + ") result=camera-snapped-and-player-entering");
        }

        private void BeginWalkingPresentation(bool reopenDialogueAfterWalk = true)
        {
            _playerWalking = true;
            _suppressDialogueReopenAfterWalk = !reopenDialogueAfterWalk;
            _reopenDialogueAfterWalk = reopenDialogueAfterWalk && !_dialoguePresenter.IsDismissed;
            _dialoguePresenter.Dismiss();
            _sceneUi?.SetStoryVisible(false);
        }

        private void QueueServerSceneSequence(GameApiClient.SceneActionSnapshot[] sequence)
        {
            if (_sceneSequencePlayer == null || sequence == null || sequence.Length == 0)
                return;
            if (_scenePlaybackCoroutine != null)
            {
                _sceneSequencePlayer.Cancel();
                StopCoroutine(_scenePlaybackCoroutine);
            }
            _scenePlaybackCoroutine = StartCoroutine(PlayServerSceneSequence(sequence));
        }

        private IEnumerator PlayServerSceneSequence(GameApiClient.SceneActionSnapshot[] sequence)
        {
            try
            {
                while (_playerWalking)
                    yield return null;
                yield return _sceneSequencePlayer.Play(sequence, PlayServerSceneAction);
            }
            finally
            {
                _scenePlaybackCoroutine = null;
                _pendingPresentationChanges |= PresentationChange.Dialogue;
                SubmitBufferedDirectionalMoveIfReady();
            }
        }

        private IEnumerator PlayServerSceneAction(GameApiClient.SceneActionSnapshot action)
        {
            string type = (action.type ?? string.Empty).ToUpperInvariant();
            FocusServerSceneAction(action);
            if (type == "MOVE" || type == "FLEE")
            {
                yield return PlayServerActorMove(action);
                if (type == "FLEE")
                    HideServerActorIfInactive(action.actorId);
                yield break;
            }
            if (type == "ATTACK")
            {
                yield return PlayServerActorAttack(action);
                yield break;
            }
            if (type == "DAMAGE" || type == "DEFEATED")
            {
                yield return PlayServerActorReaction(action, type == "DEFEATED");
                yield break;
            }
            if (type == "DEFEND" || type == "ASSIST")
            {
                yield return PlayServerActorGesture(action, type == "DEFEND");
                yield break;
            }
            if (type == "SPAWN")
            {
                if (Guid.TryParse(action.actorId, out Guid spawnedId) &&
                    _entityVisuals.TryGetValue(spawnedId, out KeyboardWandererEntityVisualState spawnedVisual) &&
                    spawnedVisual.Root != null)
                    spawnedVisual.Root.SetActive(true);
                PlaySfx(AssetClip("UiCancelSound"));
                yield return new WaitForSecondsRealtime(0.45f);
                yield break;
            }

            string actorName = ServerEntityName(action.actorId, type == "DIALOGUE" ? "NPC" : "세계");
            string message = !string.IsNullOrWhiteSpace(action.text) ? action.text : action.line;
            if (type == "DIALOGUE")
            {
                if (!string.IsNullOrWhiteSpace(action.line))
                {
                    var dialogue = new List<string>(_lastDialogue ?? Array.Empty<string>());
                    if (dialogue.Count < 8 && !dialogue.Contains(action.line))
                        dialogue.Add(action.line);
                    _lastDialogue = dialogue.ToArray();
                    string dialogueKey = action.line.Trim();
                    _dialogueSpeakerNames[dialogueKey] = actorName;
                    _dialogueSpeakerSprites[dialogueKey] = ServerEntitySprite(action.actorId);
                    AddLog(actorName + " · “" + action.line.Trim() + "”");
                }
                // Player-facing dialogue is owned by narrative.storySequence and advances
                // only through UiAdvanceDialogue. The authoritative scene sequence keeps
                // this line as a log entry so its timed playback cannot replace UI pages.
                yield break;
            }

            if (!string.IsNullOrWhiteSpace(message))
                AddLog("후속 장면 · " + message);
            if (type == "LOOT")
                PlaySfx(AssetClip("UiAcceptSound"));
            else if (type == "ENCOUNTER")
                PlaySfx(AssetClip("UiCancelSound"));
            yield return new WaitForSecondsRealtime(type == "ENCOUNTER" ? 0.6f : 0.35f);
        }

        private void PrepareStoryWorldActions()
        {
            for (int i = 0; i < _lastServerSceneSequence.Length; i++)
            {
                GameApiClient.SceneActionSnapshot action = _lastServerSceneSequence[i];
                if (!string.Equals(action?.type, "SPAWN", StringComparison.OrdinalIgnoreCase) ||
                    !Guid.TryParse(action.actorId, out Guid entityId) ||
                    !_entityVisuals.TryGetValue(entityId, out KeyboardWandererEntityVisualState visual) || visual.Root == null) continue;
                visual.Root.SetActive(false);
            }

            for (int i = 0; i < _pendingRuntimeRenderSequence.Length; i++)
            {
                GameApiClient.SceneActionSnapshot action = _pendingRuntimeRenderSequence[i];
                if (!string.Equals(action?.type, "SPAWN", StringComparison.OrdinalIgnoreCase) ||
                    !Guid.TryParse(action.actorId, out Guid entityId) ||
                    !_entityVisuals.TryGetValue(entityId, out KeyboardWandererEntityVisualState visual) || visual.Root == null)
                    continue;
                visual.Root.SetActive(false);
            }
            if (_pendingRuntimeRenderSequence.Length > 0)
            {
                QueueServerSceneSequence(_pendingRuntimeRenderSequence);
                _pendingRuntimeRenderSequence = Array.Empty<GameApiClient.SceneActionSnapshot>();
            }
        }

        private void FocusServerSceneAction(GameApiClient.SceneActionSnapshot action)
        {
            if (_serverRun?.entities == null || action == null)
                return;
            if (!ShouldFocusServerSceneAction(action.type))
                return;
            string focusId = !string.IsNullOrWhiteSpace(action.actorId) ? action.actorId : action.targetId;
            for (int i = 0; i < _serverRun.entities.Length; i++)
            {
                GameApiClient.EntitySnapshot entity = _serverRun.entities[i];
                if (entity?.position == null || !string.Equals(entity.id, focusId, StringComparison.OrdinalIgnoreCase))
                    continue;
                _cameraInspectCoord = new GridCoord(entity.position.x, entity.position.y);
                _cameraInspectUntil = Time.unscaledTime + 1.2f;
                return;
            }
        }

        private static bool ShouldFocusServerSceneAction(string actionType)
        {
            switch ((actionType ?? string.Empty).Trim().ToUpperInvariant())
            {
                case "ATTACK":
                case "DAMAGE":
                case "DEFEATED":
                case "FLEE":
                case "SPAWN":
                case "ENCOUNTER":
                case "DIALOGUE":
                case "DEFEND":
                case "ASSIST":
                    return true;
                default:
                    return false;
            }
        }

        private IEnumerator PlayServerActorMove(GameApiClient.SceneActionSnapshot action)
        {
            if (!Guid.TryParse(action.actorId, out Guid actorId) ||
                !_entityVisuals.TryGetValue(actorId, out KeyboardWandererEntityVisualState visual) || visual.Root == null ||
                action.from == null || action.to == null || _service == null)
            {
                if (!string.IsNullOrWhiteSpace(action.text)) AddLog("후속 장면 · " + action.text);
                yield return new WaitForSecondsRealtime(0.2f);
                yield break;
            }

            Vector2 origin = MapOrigin(_service.CurrentView);
            Vector3 from = WorldPosition(origin, new GridCoord(action.from.x, action.from.y));
            Vector3 to = WorldPosition(origin, new GridCoord(action.to.x, action.to.y));
            visual.Root.transform.position = from;
            visual.TargetPosition = to;
            visual.MovementPath.Clear();
            visual.IsWandering = false;
            Vector3 direction = to - from;
            if (Mathf.Abs(direction.x) > Mathf.Abs(direction.y))
                visual.Facing = new Vector2(Mathf.Sign(direction.x), 0f);
            else if (Mathf.Abs(direction.y) > 0.0001f)
                visual.Facing = new Vector2(0f, Mathf.Sign(direction.y));
            SetAnimatorFloat(visual.Animator, "MoveX", visual.Facing.x);
            SetAnimatorFloat(visual.Animator, "MoveY", visual.Facing.y);
            SetAnimatorFloat(visual.Animator, "MoveSpeed", 1f);

            float deadline = Time.unscaledTime + 0.55f;
            while (visual.Root != null && Vector3.SqrMagnitude(visual.Root.transform.position - to) > 0.0004f &&
                   Time.unscaledTime < deadline)
            {
                visual.Root.transform.position = Vector3.MoveTowards(visual.Root.transform.position, to,
                    PlayerWalkSpeed * Time.unscaledDeltaTime);
                yield return null;
            }
            if (visual.Root != null)
                visual.Root.transform.position = to;
            SetAnimatorFloat(visual.Animator, "MoveSpeed", 0f);
            if (!string.IsNullOrWhiteSpace(action.text)) AddLog("후속 장면 · " + action.text);
        }

        private IEnumerator PlayServerActorAttack(GameApiClient.SceneActionSnapshot action)
        {
            if (!Guid.TryParse(action.actorId, out Guid actorId) ||
                !_entityVisuals.TryGetValue(actorId, out KeyboardWandererEntityVisualState visual) || visual.Root == null)
            {
                if (!string.IsNullOrWhiteSpace(action.text)) AddLog("후속 장면 · " + action.text);
                yield return new WaitForSecondsRealtime(0.25f);
                yield break;
            }

            Vector3 origin = visual.TargetPosition;
            Vector3 lunge = Vector3.zero;
            if (Guid.TryParse(action.targetId, out Guid targetId) &&
                _entityVisuals.TryGetValue(targetId, out KeyboardWandererEntityVisualState target) && target.Root != null)
            {
                Vector3 direction = (target.Root.transform.position - origin).normalized;
                lunge = direction * 0.22f;
                if (Mathf.Abs(direction.x) > Mathf.Abs(direction.y))
                    visual.Facing = new Vector2(Mathf.Sign(direction.x), 0f);
                else if (Mathf.Abs(direction.y) > 0.0001f)
                    visual.Facing = new Vector2(0f, Mathf.Sign(direction.y));
            }
            if (visual.IsPlayer)
            {
                _playerActionFrames = null; // 후속 장면 공격은 방향별 기본 공격 모션 사용
                _playerActionUntil = Time.unscaledTime + 0.38f;
            }
            PlaySfx(AssetClip(action.hit ? "SlashSound" : "UiCancelSound"));
            float started = Time.unscaledTime;
            const float duration = 0.34f;
            while (visual.Root != null && Time.unscaledTime - started < duration)
            {
                float progress = (Time.unscaledTime - started) / duration;
                visual.Root.transform.position = origin + lunge * Mathf.Sin(progress * Mathf.PI);
                yield return null;
            }
            if (visual.Root != null)
                visual.Root.transform.position = origin;
            if (!string.IsNullOrWhiteSpace(action.text)) AddLog("후속 장면 · " + action.text);
        }

        private IEnumerator PlayServerActorReaction(GameApiClient.SceneActionSnapshot action, bool defeated)
        {
            if (!Guid.TryParse(action.actorId, out Guid actorId) ||
                !_entityVisuals.TryGetValue(actorId, out KeyboardWandererEntityVisualState visual) || visual.Root == null)
            {
                if (!string.IsNullOrWhiteSpace(action.text)) AddLog("후속 장면 · " + action.text);
                yield return new WaitForSecondsRealtime(0.2f);
                yield break;
            }

            Vector3 baseScale = visual.Root.transform.localScale;
            float started = Time.unscaledTime;
            const float duration = 0.3f;
            while (visual.Root != null && Time.unscaledTime - started < duration)
            {
                float progress = (Time.unscaledTime - started) / duration;
                float pulse = 1f - Mathf.Sin(progress * Mathf.PI) * (defeated ? 0.45f : 0.16f);
                visual.Root.transform.localScale = baseScale * pulse;
                yield return null;
            }
            if (visual.Root != null)
            {
                visual.Root.transform.localScale = baseScale;
                if (defeated) visual.Root.SetActive(false);
            }
            if (!string.IsNullOrWhiteSpace(action.text)) AddLog("후속 장면 · " + action.text);
            if (!defeated) PlaySfx(AssetClip("HitSound"), cutOffPrevious: false);
        }

        private IEnumerator PlayServerActorGesture(GameApiClient.SceneActionSnapshot action, bool defending)
        {
            if (!Guid.TryParse(action.actorId, out Guid actorId) ||
                !_entityVisuals.TryGetValue(actorId, out KeyboardWandererEntityVisualState visual) || visual.Root == null)
            {
                if (!string.IsNullOrWhiteSpace(action.text)) AddLog("후속 장면 · " + action.text);
                yield return new WaitForSecondsRealtime(0.2f);
                yield break;
            }

            Vector3 baseScale = visual.Root.transform.localScale;
            float started = Time.unscaledTime;
            const float duration = 0.42f;
            PlaySfx(AssetClip(defending ? "UiCancelSound" : "UiAcceptSound"));
            while (visual.Root != null && Time.unscaledTime - started < duration)
            {
                float progress = (Time.unscaledTime - started) / duration;
                float pulse = Mathf.Sin(progress * Mathf.PI);
                visual.Root.transform.localScale = defending
                    ? new Vector3(baseScale.x * (1f + pulse * 0.16f), baseScale.y * (1f - pulse * 0.08f), baseScale.z)
                    : baseScale * (1f + pulse * 0.12f);
                yield return null;
            }
            if (visual.Root != null) visual.Root.transform.localScale = baseScale;
            if (!string.IsNullOrWhiteSpace(action.text)) AddLog("후속 장면 · " + action.text);
        }

        private void CompletePlayerPathAnimation()
        {
            _playerWalking = false;
            _cameraInspectCoord = null;
            _cameraInspectUntil = 0f;
            if (_reopenDialogueAfterWalk)
            {
                _reopenDialogueAfterWalk = false;
                _dialoguePresenter.Show();
                _sceneUi?.SetStoryVisible(true);
                Debug.Log("[KW.Narrative] event=DialogueDisplay result=reopened-after-walk sceneUi=" +
                          (_sceneUi == null ? "null" : "ready"));
            }
            _suppressDialogueReopenAfterWalk = false;
            SubmitBufferedDirectionalMoveIfReady();
        }

        private bool TryGetPlayerVisual(RunView view, out KeyboardWandererEntityVisualState visual)
        {
            Guid playerId = view.PlayerEntityId;
            if (_serverOnline && _serverRun != null && Guid.TryParse(_serverRun.playerEntityId, out Guid serverId))
                playerId = serverId;
            return _entityVisuals.TryGetValue(playerId, out visual) && visual?.Root != null;
        }

        private bool CanSelectMovementDestination(RunView view, GridCoord goal)
        {
            if (_pathPlanner == null || !TryGetPlayerPosition(view, out GridCoord start))
                return false;
            return _pathPlanner.CanSelectDestination(view,
                _serverOnline ? _serverRun : null, start, goal);
        }

        private void UpdateSelectionVisual(RunView view)
        {
            if (_selectionRenderer == null)
                return;
            _selectionRenderer.enabled = _selectionController.SelectedCoord.HasValue;
            if (_selectionController.SelectedCoord.HasValue)
            {
                _selectionRenderer.transform.position = WorldPosition(MapOrigin(view), _selectionController.SelectedCoord.Value);
                // Keep the authored tile cursor behind actors. The cursor is deliberately a little
                // larger than one cell, so only its perimeter remains visible around a selected entity.
                _selectionRenderer.sortingOrder = 499 - _selectionController.SelectedCoord.Value.Y * 4;
                _selectionRenderer.color = _selectionController.Ability == AbilityKind.Delete
                    ? new Color(1f, 0.12f, 0.08f, 0.58f)
                    : _selectionController.Ability == AbilityKind.Restore
                        ? new Color(0.2f, 0.95f, 1f, 0.54f)
                        : new Color(1f, 0.85f, 0.25f, 0.48f);
                ScaleSprite(_selectionRenderer.transform, _selectionRenderer.sprite, 1.28f);
            }
        }

        private void ConfigureCamera()
        {
            _cameraController = authoredCameraController;
            UpdateCameraViewport(true);
        }

        private void UpdateCameraViewport(bool force = false)
        {
            _cameraController?.UpdateViewport(force);
        }

        private void UpdateCameraFollow()
        {
            if (_cameraController == null || !_cameraController.IsReady || _service == null)
                return;
            RunView view = _service.CurrentView;
            if (!TryGetPlayerPosition(view, out GridCoord playerPosition))
                return;
            GridCoord cameraTarget = _cameraInspectCoord.HasValue && Time.unscaledTime < _cameraInspectUntil
                ? _cameraInspectCoord.Value
                : playerPosition;
            if (Time.unscaledTime >= _cameraInspectUntil) _cameraInspectCoord = null;
            Vector3 desired = _playerWalking && !_cameraInspectCoord.HasValue && TryGetPlayerVisual(view, out KeyboardWandererEntityVisualState playerVisual)
                ? playerVisual.Root.transform.position
                : WorldPosition(MapOrigin(view), cameraTarget);
            Vector2 origin = MapOrigin(view);
            int worldWidth = ActiveWorldWidth(view);
            int worldHeight = ActiveWorldHeight(view);
            _cameraController.Follow(desired, origin, worldWidth, worldHeight, Time.unscaledDeltaTime);
        }

        private void GetCameraActiveTileBounds(RunView view, int paddingTiles,
            out GridCoord minimum, out GridCoord maximum)
        {
            Camera camera = _cameraController?.TargetCamera;
            if (camera == null)
            {
                minimum = new GridCoord(0, 0);
                maximum = new GridCoord(ActiveWorldWidth(view) - 1, ActiveWorldHeight(view) - 1);
                return;
            }

            Vector2 origin = MapOrigin(view);
            float halfHeight = camera.orthographicSize;
            float halfWidth = halfHeight * camera.aspect;
            Vector3 center = camera.transform.position;
            minimum = new GridCoord(
                Mathf.Max(0, Mathf.FloorToInt((center.x - halfWidth - origin.x) / TileSize) - paddingTiles),
                Mathf.Max(0, Mathf.FloorToInt((center.y - halfHeight - origin.y) / TileSize) - paddingTiles));
            maximum = new GridCoord(
                Mathf.Min(ActiveWorldWidth(view) - 1,
                    Mathf.FloorToInt((center.x + halfWidth - origin.x) / TileSize) + paddingTiles),
                Mathf.Min(ActiveWorldHeight(view) - 1,
                    Mathf.FloorToInt((center.y + halfHeight - origin.y) / TileSize) + paddingTiles));
        }

        private void SnapCameraToPlayer()
        {
            if (_service == null || _cameraController == null || !_cameraController.IsReady)
                return;
            if (!TryGetPlayerPosition(_service.CurrentView, out GridCoord playerPosition))
                return;
            Vector3 position = WorldPosition(MapOrigin(_service.CurrentView), playerPosition);
            _cameraController.Snap(position);
        }

        private void ConfigureAudio()
        {
            _audioController = authoredAudioController;
            if (_settingsController == null)
                throw new InvalidOperationException("Game Root에는 Settings Controller 참조가 필요합니다.");
            _settingsController.Configure(_audioController);
        }

        private void SetMusic(AudioClip clip)
        {
            _audioController?.SetMusic(clip);
        }

        private void RefreshDynamicMusic(RunView view, RunPresentationModel presentation)
        {
            if (_assets == null || view == null || presentation == null) return;
            bool gameOver = IsGameOver(view, presentation);
            bool cleared = !gameOver && IsRunCleared(view, presentation);
            bool bossBattle = TryGetActiveBossBattle(presentation.CurrentBiomeId, out bool finalBossBattle);
            AudioClip clip = KeyboardWandererMusicDirector.Resolve(_assets,
                new KeyboardWandererMusicDirector.Context(presentation.CurrentBiomeId, bossBattle,
                    finalBossBattle, gameOver, cleared));
            SetMusic(clip);
        }

        private bool IsGameOver(RunView view, RunPresentationModel presentation)
        {
            if (presentation.Health <= 0) return true;
            if (_serverOnline && _serverRun != null)
                return StatusMatches(_serverRun.status, "dead", "failed", "game_over", "gameover");
            return view.Status == RunStatus.Dead;
        }

        private bool IsRunCleared(RunView view, RunPresentationModel presentation)
        {
            if (presentation.IsPlaying) return false;
            if (_serverOnline && _serverRun != null)
                return StatusMatches(_serverRun.status, "completed", "cleared", "victory", "won");
            return view.Status == RunStatus.Completed;
        }

        private bool TryGetActiveBossBattle(string biomeId, out bool finalBossBattle)
        {
            finalBossBattle = false;
            if (_serverOnline && _serverRun != null)
            {
                GameApiClient.ActiveEncounterSnapshot encounter = _serverRun.activeEncounter;
                if (!IsOpenServerEncounter(encounter) ||
                    !string.Equals(encounter.kind, "COMBAT", StringComparison.OrdinalIgnoreCase))
                    return false;
                GameApiClient.EntitySnapshot source = FindServerEntity(encounter.sourceEntityId);
                bool boss = source != null && (source.state?.boss == true || IsBossAsset(source.assetId));
                if (!boss) return false;
                finalBossBattle = IsFinalBoss(source, biomeId);
                return true;
            }

            if (_service?.CurrentView == null || !_service.CurrentView.HasActiveEncounter) return false;
            RunPresentationModel run = _runPresentationModel;
            if (run?.Entities == null) return false;
            for (int i = 0; i < run.Entities.Count; i++)
            {
                RunPresentationEntity entity = run.Entities[i];
                if (entity == null || !entity.IsActive || !entity.IsHostile || !IsBossAsset(entity.AssetId)) continue;
                finalBossBattle = IsFinalBossAsset(entity.AssetId) ||
                                  string.Equals(biomeId, "root_system", StringComparison.OrdinalIgnoreCase);
                return true;
            }
            return false;
        }

        private GameApiClient.EntitySnapshot FindServerEntity(string entityId)
        {
            if (string.IsNullOrWhiteSpace(entityId) || _serverRun?.entities == null) return null;
            for (int i = 0; i < _serverRun.entities.Length; i++)
                if (string.Equals(_serverRun.entities[i]?.id, entityId, StringComparison.OrdinalIgnoreCase))
                    return _serverRun.entities[i];
            return null;
        }

        private static bool IsFinalBoss(GameApiClient.EntitySnapshot source, string biomeId)
        {
            if (source == null) return false;
            if (IsFinalBossAsset(source.assetId) ||
                string.Equals(biomeId, "root_system", StringComparison.OrdinalIgnoreCase))
                return true;
            if (string.Equals(source.state?.campaignRole, CampaignCatalog.FinalConvergenceRole,
                    StringComparison.OrdinalIgnoreCase))
                return true;
            string[] roles = source.state?.roleTags;
            if (roles == null) return false;
            for (int i = 0; i < roles.Length; i++)
                if (string.Equals(roles[i], CampaignCatalog.FinalConvergenceRole, StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }

        private static bool IsBossAsset(string assetId)
            => !string.IsNullOrWhiteSpace(assetId) && assetId.StartsWith("boss.", StringComparison.OrdinalIgnoreCase);

        private static bool IsFinalBossAsset(string assetId)
            => string.Equals(assetId, "boss.giant-flam.v1", StringComparison.OrdinalIgnoreCase);

        private static bool StatusMatches(string status, params string[] values)
        {
            for (int i = 0; i < values.Length; i++)
                if (string.Equals(status, values[i], StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private void PlaySfx(AudioClip clip, bool cutOffPrevious = true)
        {
            _audioController?.PlaySfx(clip, cutOffPrevious);
        }

        private AudioClip AssetClip(string fieldName)
        {
            return _visualAssetLibrary != null ? _visualAssetLibrary.GetAudioClip(fieldName) : null;
        }

        // 서버 인카운터 발동 반경(5칸, SubmitServerAction의 "5칸 이내" 안내)과 동일한 범위를
        // 보스 교전 판정에 재사용해 별도 상수를 새로 정의하지 않는다.
        private const int BossEngagementRange = 5;

        private void UpdateDynamicMusic(RunView view)
        {
            if (_assets == null || _screenMode != ScreenMode.Playing)
                return;
            RunPresentationModel model = PresentationModel(view);
            AudioClip clip;
            if (model.Status == RunStatus.Dead)
                clip = _assets.GameOverMusic;
            else if (HasNearbyActiveHostileBoss(model))
                clip = string.Equals(model.CurrentRegionAxis, CampaignCatalog.RootSystemAxis, StringComparison.Ordinal)
                    ? _assets.FinalBossMusic
                    : _assets.BattleMusic;
            else
                clip = RegionMusic(model.CurrentRegionAxis);
            if (clip != null)
                SetMusic(clip);
        }

        private static bool HasNearbyActiveHostileBoss(RunPresentationModel model)
        {
            for (int i = 0; i < model.Entities.Count; i++)
            {
                RunPresentationEntity entity = model.Entities[i];
                if (entity == null || entity.Kind != RunPresentationEntityKind.Enemy ||
                    !entity.IsHostile || !entity.IsActive ||
                    !entity.AssetId.StartsWith("boss.", StringComparison.Ordinal))
                    continue;
                if (model.DistanceFromPlayer(entity) <= BossEngagementRange)
                    return true;
            }
            return false;
        }

        private AudioClip RegionMusic(string regionAxis)
        {
            if (_assets == null)
                return null;
            switch (regionAxis)
            {
                case CampaignCatalog.BugForestAxis: return _assets.BugForestMusic;
                case CampaignCatalog.BufferVillageAxis: return _assets.BufferVillageMusic;
                case CampaignCatalog.DeadlockCityAxis: return _assets.DeadlockCityMusic;
                case CampaignCatalog.DataGrandLibraryAxis: return _assets.DataArchiveMusic;
                case CampaignCatalog.LegacyCitadelAxis: return _assets.LegacyCitadelMusic;
                case CampaignCatalog.RootSystemAxis: return _assets.RootSystemMusic;
                default: return null;
            }
        }

       private void TileAppearance(TileKind kind, string biomeId, GridCoord coord, out Sprite sprite, out Color tint)
        {
            sprite = GroundSpriteForBiome(biomeId);
            switch (kind)
            {
                case TileKind.Dirt:
                    sprite = _dirtSprite;
                    tint = new Color(0.95f, 0.9f, 0.78f, 1f);
                    break;
                case TileKind.Water:
                    sprite = _waterSprite;
                    tint = new Color(0.27f, 0.66f, 0.78f, 1f);
                    break;
                case TileKind.Bridge:
                    sprite = _dirtSprite;
                    tint = new Color(0.72f, 0.52f, 0.3f, 1f);
                    break;
                case TileKind.DarkGrass:
                    sprite = _darkGrassSprite;
                    tint = new Color(0.72f, 0.9f, 0.72f, 1f);
                    break;
                case TileKind.Ruin:
                    sprite = string.Equals(biomeId, "ancient_ruins", StringComparison.OrdinalIgnoreCase)
                        ? _ruinFloorSprite : _darkGrassSprite;
                    tint = new Color(0.7f, 0.72f, 0.64f, 1f);
                    break;
                case TileKind.Wall:
                    sprite = _wallSprite != null ? _wallSprite : _darkGrassSprite;
                    tint = new Color(0.52f, 0.64f, 0.5f, 1f);
                    break;
                case TileKind.Hazard:
                    sprite = _dirtSprite;
                    tint = new Color(0.9f, 0.42f, 0.32f, 1f);
                    break;
                default:
                    tint = Color.white;
                    break;
            }
            tint = ApplyBiomePalette(tint, biomeId);
            float variation = (((coord.X * 31 + coord.Y * 17) & 3) - 1.5f) * 0.025f;
            tint = new Color(Mathf.Clamp01(tint.r + variation), Mathf.Clamp01(tint.g + variation), Mathf.Clamp01(tint.b + variation), tint.a);
        }

        // 지형 한 칸이 어느 타일 시트에서 그려지는지 고른다. 같은 시트를 쓰는 이웃끼리만
        // 오토타일로 이어지므로, 이 반환값이 곧 "지형 그룹" 식별자 역할도 한다.
        private Texture2D CodriaTerrainSheet(string biomeId, TileKind kind)
        {
            if (_assets == null) return null;
            string biome = (biomeId ?? string.Empty).ToLowerInvariant();

            switch (kind)
            {
                case TileKind.Water:
                    return biome == "temperate_forest_field"
                        ? _assets.BugForestLakeTiles : _assets.GeneralWaterTiles;
                case TileKind.Hazard:
                    switch (biome)
                    {
                        case "temperate_forest_field": return _assets.BugForestHoleTiles;
                        case "frost_highland": return _assets.LegacyCitadelIceTiles;
                        default: return _assets.DeadlockCityVirusTiles;
                    }
                case TileKind.Dirt:
                case TileKind.Bridge:
                    switch (biome)
                    {
                        case "temperate_forest_field": return _assets.BugForestCampTiles;
                        case "subterranean_cavern": return _assets.DataArchiveWoodPlankTiles;
                        case "frost_highland": return _assets.LegacyCitadelIceTiles;
                        default: return CodriaGroundSheet(biome);
                    }
                default:
                    return CodriaGroundSheet(biome);
            }
        }

        private Texture2D CodriaGroundSheet(string biome)
        {
            switch (biome)
            {
                case "temperate_forest_field": return _assets.BugForestGroundTiles;
                case "arid_desert": return _assets.BufferVillageGroundTiles;
                case "ancient_ruins": return _assets.DeadlockCityGroundTiles;
                case "subterranean_cavern": return _assets.DataArchiveRockTiles;
                case "frost_highland": return _assets.LegacyCitadelSnowTiles;
                case "root_system": return _assets.RootSystemGroundTiles;
                default: return _assets.GeneralFieldTiles;
            }
        }

        private Sprite GroundSpriteForBiome(string biomeId)
        {
            KeyboardWandererWorldVisualProfile profile = authoringSettings != null ? authoringSettings.WorldVisualProfile : null;
            if (profile != null && profile.TryGet(biomeId, out KeyboardWandererWorldVisualProfile.BiomeVisual authored) &&
                authored.GroundSprite != null)
                return authored.GroundSprite;
            switch ((biomeId ?? string.Empty).ToLowerInvariant())
            {
                case "root_system": return _cavernFloorSprite;
                case "river_wetland": return _darkGrassSprite;
                case "arid_desert": return _dirtSprite;
                case "frost_highland": return _snowSprite;
                case "subterranean_cavern": return _cavernFloorSprite;
                case "ancient_ruins": return _ruinFloorSprite;
                default: return _grassSprite;
            }
        }

        private void UpdateMinimap(RunView view)
        {
            if (_sceneUi == null || view == null || _minimapRenderer == null)
                return;
            RunPresentationModel presentation = PresentationModel(view);
            GridCoord player = presentation.Core.PlayerPosition;
            GameApiClient.WorldSnapshot serverWorld = _serverOnline ? _serverRun?.world : null;
            bool useServerWorld = serverWorld != null && serverWorld.HasCompleteLayout;
            int width = useServerWorld ? serverWorld.width : view.Region.Width;
            int height = useServerWorld ? serverWorld.height : view.Region.Height;
            string layoutHash = useServerWorld ? serverWorld.layoutHash : view.Region.LayoutHash;
            var landmarks = new List<GridCoord>();
            if (_serverOnline && _serverRun?.world?.points != null)
            {
                for (int i = 0; i < _serverRun.world.points.Length; i++)
                {
                    GameApiClient.PointSnapshot point = _serverRun.world.points[i];
                    if (point != null)
                        landmarks.Add(new GridCoord(point.x, point.y));
                }
            }
            else
            {
                for (int i = 0; i < view.Region.Areas.Count; i++)
                    landmarks.Add(view.Region.Areas[i].Center);
            }

            Func<GridCoord, Color> tileColorAt = coord =>
            {
                // 서버 월드도 실제로는 정사각 그리드지만, 미니맵은 항상 원형 실루엣으로 보여준다.
                if (OutsideLocalWorldDisc(coord, width, height))
                    return Color.clear;
                TileKind kind = WorldTileKind(view, coord, useServerWorld);
                return MinimapTileColor(BiomeIdAt(view, coord), kind);
            };
            Sprite sprite = _minimapRenderer.Render(width, height, layoutHash,
                presentation.Core.Version, presentation.Core.Turn, player,
                _selectionController.SelectedCoord, _selectionController.SelectedTarget,
                presentation.ObjectiveTargetPosition, presentation.ObjectiveTargetId,
                presentation.ObjectiveTargetName, landmarks, presentation.Entities,
                tileColorAt, out string status);
            _sceneUi.SetMinimap(sprite, status);
        }

        private static bool OutsideLocalWorldDisc(GridCoord coord, int width, int height)
        {
            double cx = (width - 1) / 2.0;
            double cy = (height - 1) / 2.0;
            double dx = coord.X - cx;
            double dy = coord.Y - cy;
            double radius = Math.Min(width, height) / 2.0 - 1.0;
            return dx * dx + dy * dy > radius * radius;
        }

        private static Color MinimapTileColor(string biomeId, TileKind kind)
        {
            Color biome;
            switch ((biomeId ?? string.Empty).ToLowerInvariant())
            {
                case "root_system": biome = Hex("49234f"); break;
                case "river_wetland": biome = Hex("397d83"); break;
                case "arid_desert": biome = Hex("c28a42"); break;
                case "frost_highland": biome = Hex("a9d1df"); break;
                case "subterranean_cavern": biome = Hex("57406f"); break;
                case "ancient_ruins": biome = Hex("877053"); break;
                default: biome = Hex("477b43"); break;
            }
            switch (kind)
            {
                case TileKind.Wall: return Color.Lerp(biome, Color.black, 0.72f);
                case TileKind.Water: return Hex("2f7595");
                case TileKind.Bridge: return Hex("b77a3c");
                case TileKind.Dirt: return Color.Lerp(biome, Hex("c28a4b"), 0.6f);
                case TileKind.Hazard: return Hex("bd493f");
                default: return biome;
            }
        }

        private string BiomeIdAt(RunView view, GridCoord coord)
        {
            if (_serverOnline && _serverRun?.world != null)
            {
                GameApiClient.WorldSnapshot world = _serverRun.world;
                string mappedBiome = world.BiomeIdAt(coord.X, coord.Y);
                if (!string.IsNullOrWhiteSpace(mappedBiome))
                    return mappedBiome;

                GameApiClient.AreaSnapshot[] areas = world.areas;
                if (areas != null)
                {
                    for (int i = 0; i < areas.Length; i++)
                    {
                        GameApiClient.BoundsSnapshot bounds = areas[i]?.bounds;
                        if (bounds != null && coord.X >= bounds.x && coord.X < bounds.x + bounds.width &&
                            coord.Y >= bounds.y && coord.Y < bounds.y + bounds.height)
                            return areas[i].biomeId;
                    }
                }
            }
            return SectorBiomeId(coord, view.Region.Width, view.Region.Height);
        }

        private static readonly string[] SectorBiomeOrder =
        {
            "temperate_forest_field", "river_wetland", "arid_desert",
            "frost_highland", "subterranean_cavern", "ancient_ruins"
        };

        private static string SectorBiomeId(GridCoord coord, int width, int height)
        {
            double cx = (width - 1) / 2.0;
            double cy = (height - 1) / 2.0;
            double dx = coord.X - cx;
            double dy = coord.Y - cy;
            double centralRadius = Math.Min(width, height) * 0.12;
            if (dx * dx + dy * dy <= centralRadius * centralRadius)
                return "root_system";

            double angle = Math.Atan2(dy, dx) + Math.PI;
            int sector = (int)(angle / (2.0 * Math.PI) * SectorBiomeOrder.Length);
            return SectorBiomeOrder[Mathf.Clamp(sector, 0, SectorBiomeOrder.Length - 1)];
        }

        private Color ApplyBiomePalette(Color tileTint, string biomeId)
        {
            KeyboardWandererWorldVisualProfile profile = authoringSettings != null ? authoringSettings.WorldVisualProfile : null;
            if (profile != null && profile.TryGet(biomeId, out KeyboardWandererWorldVisualProfile.BiomeVisual authored))
            {
                Color palette = authored.Palette;
                return new Color(Mathf.Clamp01(tileTint.r * 0.34f + palette.r * 0.72f),
                    Mathf.Clamp01(tileTint.g * 0.34f + palette.g * 0.72f),
                    Mathf.Clamp01(tileTint.b * 0.34f + palette.b * 0.72f), tileTint.a);
            }
            if (string.Equals(biomeId, "root_system", StringComparison.OrdinalIgnoreCase))
            {
                Color core = Hex("2a1830");
                return new Color(
                    Mathf.Clamp01(tileTint.r * 0.22f + core.r + 0.06f),
                    Mathf.Clamp01(tileTint.g * 0.18f + core.g),
                    Mathf.Clamp01(tileTint.b * 0.26f + core.b + 0.04f),
                    tileTint.a);
            }
            Color biome;
            switch ((biomeId ?? string.Empty).ToLowerInvariant())
            {
                case "river_wetland": biome = Hex("5fa9a8"); break;
                case "arid_desert": biome = Hex("d6a253"); break;
                case "frost_highland": biome = Hex("a9c8df"); break;
                case "subterranean_cavern": biome = Hex("745a91"); break;
                case "ancient_ruins": biome = Hex("a68a61"); break;
                default: biome = Hex("6ca85d"); break;
            }
            return new Color(
                Mathf.Clamp01(tileTint.r * 0.34f + biome.r * 0.72f),
                Mathf.Clamp01(tileTint.g * 0.34f + biome.g * 0.72f),
                Mathf.Clamp01(tileTint.b * 0.34f + biome.b * 0.72f),
                tileTint.a);
        }

        private void CreateBiomeDecorations(RunView view, Vector2 origin, bool useServerWorld, int width, int height)
        {
            string layoutHash = useServerWorld ? _serverRun?.world?.layoutHash : view.Region.LayoutHash;
            HashSet<GridCoord> routeTiles = BuildRouteTileSet(view, useServerWorld);
            int createdDecorations = 0;
            for (int y = 2; y < height - 2 && createdDecorations < MaxBiomeDecorations; y += 2)
            {
                for (int x = 2; x < width - 2 && createdDecorations < MaxBiomeDecorations; x += 2)
                {
                    var coord = new GridCoord(x, y);
                    string biomeId = BiomeIdAt(view, coord);
                    TileKind tileKind = WorldTileKind(view, coord, useServerWorld);
                    if (routeTiles.Contains(coord) || !SupportsDecorationTerrain(biomeId, tileKind) ||
                        IsNearWorldPoint(view, coord, useServerWorld, 4) ||
                        IsNearEntity(view, coord, useServerWorld, 2) ||
                        StableVisualHash(layoutHash, x, y, 17) % 100 >= DecorationDensity(biomeId))
                        continue;
                    float scale = 0.62f + (StableVisualHash(layoutHash, x, y, 31) % 44) / 100f;
                    CreateDecoration(biomeId, "Scenery", DecorationSpriteForBiome(
                            biomeId, StableVisualHash(layoutHash, x, y, 47)), coord, origin,
                        DecorationTint(biomeId), scale);
                    createdDecorations++;
                }
            }

            if (useServerWorld && _serverRun?.world?.areas != null)
            {
                GameApiClient.AreaSnapshot[] areas = _serverRun.world.areas;
                for (int i = 0; i < areas.Length; i++)
                {
                    GameApiClient.AreaSnapshot area = areas[i];
                    if (area?.bounds == null) continue;
                    var center = area.anchor != null
                        ? new GridCoord(area.anchor.x, area.anchor.y)
                        : new GridCoord(area.bounds.x + area.bounds.width / 2,
                            area.bounds.y + area.bounds.height / 2);
                    TryCreateBiomeLandmark(view, origin, true, area.biomeId, center, width, height, layoutHash, i);
                }
            }
            else
            {
                for (int i = 0; i < view.Region.Areas.Count; i++)
                {
                    WorldArea area = view.Region.Areas[i];
                    TryCreateBiomeLandmark(view, origin, false, area.Biome, area.Center, width, height, layoutHash, i);
                }
            }
        }

        private HashSet<GridCoord> BuildRouteTileSet(RunView view, bool useServerWorld)
        {
            var result = new HashSet<GridCoord>();
            if (useServerWorld && _serverRun?.world?.routes != null)
            {
                GameApiClient.WorldRouteSnapshot[] routes = _serverRun.world.routes;
                for (int routeIndex = 0; routeIndex < routes.Length; routeIndex++)
                {
                    GameApiClient.PositionSnapshot[] path = routes[routeIndex]?.path;
                    if (path == null) continue;
                    for (int pointIndex = 0; pointIndex < path.Length; pointIndex++)
                        if (path[pointIndex] != null)
                            result.Add(new GridCoord(path[pointIndex].x, path[pointIndex].y));
                }
                return result;
            }

            IReadOnlyList<WorldRoute> localRoutes = view.Region.Routes;
            for (int routeIndex = 0; routeIndex < localRoutes.Count; routeIndex++)
                for (int pointIndex = 0; pointIndex < localRoutes[routeIndex].Path.Count; pointIndex++)
                    result.Add(localRoutes[routeIndex].Path[pointIndex]);
            return result;
        }

        private void TryCreateBiomeLandmark(RunView view, Vector2 origin, bool useServerWorld, string biomeId,
            GridCoord center, int width, int height, string layoutHash, int index)
        {
            int direction = StableVisualHash(layoutHash, center.X, center.Y, index + 101) % 4;
            GridCoord[] offsets =
            {
                new GridCoord(6, 5), new GridCoord(-6, 5), new GridCoord(6, -5), new GridCoord(-6, -5)
            };
            GridCoord offset = offsets[direction];
            var candidate = new GridCoord(center.X + offset.X, center.Y + offset.Y);
            if (!TryFindDecorationTile(view, useServerWorld, biomeId, candidate, width, height, out GridCoord coord))
                return;
            CreateDecoration(biomeId, "Biome landmark", LandmarkSpriteForBiome(biomeId), coord, origin, Color.white,
                string.Equals(biomeId, "subterranean_cavern", StringComparison.OrdinalIgnoreCase) ? 1.4f : 0.92f);
        }

        private bool TryFindDecorationTile(RunView view, bool useServerWorld, string biomeId, GridCoord origin,
            int width, int height, out GridCoord result)
        {
            for (int radius = 0; radius <= 7; radius++)
            {
                for (int y = -radius; y <= radius; y++)
                {
                    for (int x = -radius; x <= radius; x++)
                    {
                        if (Math.Abs(x) != radius && Math.Abs(y) != radius) continue;
                        var coord = new GridCoord(origin.X + x, origin.Y + y);
                        if (coord.X < 2 || coord.Y < 2 || coord.X >= width - 2 || coord.Y >= height - 2 ||
                            !string.Equals(BiomeIdAt(view, coord), biomeId, StringComparison.OrdinalIgnoreCase) ||
                            !SupportsDecorationTerrain(biomeId, WorldTileKind(view, coord, useServerWorld)) ||
                            IsNearWorldPoint(view, coord, useServerWorld, 4) ||
                            IsNearEntity(view, coord, useServerWorld, 2))
                            continue;
                        result = coord;
                        return true;
                    }
                }
            }
            result = origin;
            return false;
        }

        private TileKind WorldTileKind(RunView view, GridCoord coord, bool useServerWorld)
        {
            if (useServerWorld && _serverRun?.world != null)
            {
                GameApiClient.WorldSnapshot world = _serverRun.world;
                return TileKindForServer(world, world.tileCodes[coord.Y * world.width + coord.X]);
            }
            return view.Region.GetTile(coord).Kind;
        }

        private bool IsNearWorldPoint(RunView view, GridCoord coord, bool useServerWorld, int clearance)
        {
            if (useServerWorld && _serverRun?.world?.points != null)
            {
                GameApiClient.PointSnapshot[] points = _serverRun.world.points;
                for (int i = 0; i < points.Length; i++)
                    if (points[i] != null && Math.Abs(coord.X - points[i].x) +
                        Math.Abs(coord.Y - points[i].y) <= clearance)
                        return true;
                return false;
            }
            for (int i = 0; i < view.Region.Areas.Count; i++)
                if (coord.ManhattanDistance(view.Region.Areas[i].Center) <= clearance)
                    return true;
            return false;
        }

        private bool IsNearEntity(RunView view, GridCoord coord, bool useServerWorld, int clearance)
        {
            if (useServerWorld && _serverRun?.entities != null)
            {
                for (int i = 0; i < _serverRun.entities.Length; i++)
                {
                    GameApiClient.PositionSnapshot position = _serverRun.entities[i]?.position;
                    if (position != null && Math.Abs(coord.X - position.x) + Math.Abs(coord.Y - position.y) <= clearance)
                        return true;
                }
                return false;
            }
            for (int i = 0; i < view.Entities.Count; i++)
                if (coord.ManhattanDistance(view.Entities[i].Position) <= clearance)
                    return true;
            return false;
        }

        private void CreateDecoration(string biomeId, string prefix, Sprite sprite, GridCoord coord, Vector2 origin, Color tint,
            float scale)
        {
            if (sprite == null || _decorationRenderer == null || !_decorationRenderer.IsReady) return;
            KeyboardWandererWorldVisualProfile profile = authoringSettings != null ? authoringSettings.WorldVisualProfile : null;
            GameObject authoredPrefab = profile != null && profile.TryGet(biomeId,
                out KeyboardWandererWorldVisualProfile.BiomeVisual authored) ? authored.DecorationPrefab : null;
            _decorationRenderer.Spawn(authoredPrefab, prefix + " · " + sprite.name, sprite,
                WorldPosition(origin, coord) + new Vector3(0f, 0.18f, -0.03f), tint,
                499 - coord.Y * 4, scale);
        }

        private void UpdateDecorationOcclusion()
        {
            if (Time.unscaledTime < _nextDecorationOcclusionAt)
                return;
            _nextDecorationOcclusionAt = Time.unscaledTime + DecorationOcclusionInterval;
            if (_service == null || !TryGetPlayerVisual(_service.CurrentView, out KeyboardWandererEntityVisualState player) ||
                player.Renderer == null)
                return;
            _decorationRenderer?.UpdateOcclusion(player.Root.transform.position,
                player.Renderer.sortingOrder, DecorationOcclusionInterval);
        }

        private Sprite DecorationSpriteForBiome(string biomeId, int variantHash)
        {
            KeyboardWandererWorldVisualProfile profile = authoringSettings != null ? authoringSettings.WorldVisualProfile : null;
            if (profile != null && profile.TryGet(biomeId, out KeyboardWandererWorldVisualProfile.BiomeVisual authored))
            {
                Sprite authoredSprite = authored.DecorationSprite(variantHash);
                if (authoredSprite != null) return authoredSprite;
            }
            Sprite[] variants;
            switch ((biomeId ?? string.Empty).ToLowerInvariant())
            {
                case "root_system": variants = _cavernDecorationSprites; break;
                case "river_wetland": variants = _wetlandDecorationSprites; break;
                case "arid_desert": variants = _desertDecorationSprites; break;
                case "frost_highland": variants = _frostDecorationSprites; break;
                case "subterranean_cavern": variants = _cavernDecorationSprites; break;
                case "ancient_ruins": variants = _ruinDecorationSprites; break;
                default: variants = _forestDecorationSprites; break;
            }
            if (variants == null || variants.Length == 0) return _forestTreeSprite;
            return variants[variantHash % variants.Length];
        }

        private Sprite LandmarkSpriteForBiome(string biomeId)
        {
            KeyboardWandererWorldVisualProfile profile = authoringSettings != null ? authoringSettings.WorldVisualProfile : null;
            if (profile != null && profile.TryGet(biomeId, out KeyboardWandererWorldVisualProfile.BiomeVisual authored) &&
                authored.LandmarkSprite != null)
                return authored.LandmarkSprite;
            switch ((biomeId ?? string.Empty).ToLowerInvariant())
            {
                case "river_wetland": return _wetlandLandmarkSprite;
                case "arid_desert": return _desertLandmarkSprite;
                case "frost_highland": return _frostLandmarkSprite;
                case "subterranean_cavern": return _cavernCrystalSprite;
                case "ancient_ruins": return _ruinLandmarkSprite;
                default: return _forestHouseSprite;
            }
        }

        private static bool SupportsDecorationTerrain(string biomeId, TileKind tileKind)
        {
            switch ((biomeId ?? string.Empty).ToLowerInvariant())
            {
                case "root_system": return tileKind == TileKind.Ruin || tileKind == TileKind.Wall || tileKind == TileKind.DarkGrass;
                case "river_wetland": return tileKind == TileKind.Water || tileKind == TileKind.DarkGrass;
                case "arid_desert": return tileKind == TileKind.Dirt || tileKind == TileKind.Hazard ||
                                            tileKind == TileKind.Wall || tileKind == TileKind.Ruin;
                case "frost_highland": return tileKind == TileKind.Floor || tileKind == TileKind.Wall ||
                                             tileKind == TileKind.Hazard;
                case "subterranean_cavern": return tileKind == TileKind.DarkGrass || tileKind == TileKind.Wall ||
                                                   tileKind == TileKind.Ruin;
                case "ancient_ruins": return tileKind == TileKind.Ruin || tileKind == TileKind.Wall;
                default: return tileKind == TileKind.Grass || tileKind == TileKind.Wall ||
                                tileKind == TileKind.DarkGrass;
            }
        }

        private int DecorationDensity(string biomeId)
        {
            KeyboardWandererWorldVisualProfile profile = authoringSettings != null ? authoringSettings.WorldVisualProfile : null;
            if (profile != null && profile.TryGet(biomeId, out KeyboardWandererWorldVisualProfile.BiomeVisual authored))
                return authored.DecorationDensity;
            switch ((biomeId ?? string.Empty).ToLowerInvariant())
            {
                case "root_system": return 52;
                case "temperate_forest_field": return 64;
                case "river_wetland": return 48;
                case "arid_desert": return 44;
                case "frost_highland": return 50;
                case "subterranean_cavern": return 56;
                case "ancient_ruins": return 48;
                default: return 44;
            }
        }

        private Color DecorationTint(string biomeId)
        {
            KeyboardWandererWorldVisualProfile profile = authoringSettings != null ? authoringSettings.WorldVisualProfile : null;
            if (profile != null && profile.TryGet(biomeId, out KeyboardWandererWorldVisualProfile.BiomeVisual authored))
                return authored.DecorationTint;
            switch ((biomeId ?? string.Empty).ToLowerInvariant())
            {
                case "root_system": return Hex("b667d8");
                case "river_wetland": return Hex("b6e1cf");
                case "arid_desert": return Hex("f0c879");
                case "frost_highland": return Hex("e8f6ff");
                case "subterranean_cavern": return Hex("cf9fea");
                case "ancient_ruins": return Hex("c6ae82");
                default: return Color.white;
            }
        }

        private static int StableVisualHash(string layoutHash, int x, int y, int salt)
        {
            unchecked
            {
                int value = 17;
                string text = layoutHash ?? string.Empty;
                for (int i = 0; i < text.Length; i++) value = value * 31 + text[i];
                value = value * 31 + x;
                value = value * 31 + y;
                value = value * 31 + salt;
                return value == int.MinValue ? int.MaxValue : Math.Abs(value);
            }
        }

        private void CreateCampaignLandmarkMarkers(RunView view, Vector2 origin, bool useServerWorld)
        {
            if (useServerWorld && _serverRun?.world?.points != null)
            {
                GameApiClient.PointSnapshot[] points = _serverRun.world.points;
                for (int i = 0; i < points.Length; i++)
                {
                    GameApiClient.PointSnapshot point = points[i];
                    if (point == null || string.Equals(point.kind, "hub", StringComparison.OrdinalIgnoreCase))
                        continue;
                    CreateLandmarkMarker(FirstNonEmpty(point.nameKo, point.name, point.id),
                        point.campaignRole, new GridCoord(point.x, point.y), origin,
                        string.Equals(point.kind, "root", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(point.kind, "finale", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(point.campaignRole, CampaignCatalog.FinalConvergenceRole,
                            StringComparison.Ordinal));
                }
                return;
            }

            for (int i = 0; i < view.Region.Areas.Count; i++)
            {
                WorldArea area = view.Region.Areas[i];
                if (area == null || string.IsNullOrWhiteSpace(area.CampaignRole))
                    continue;
                CreateLandmarkMarker(area.DisplayName, area.CampaignRole, area.Center, origin,
                    string.Equals(area.CampaignRole, CampaignCatalog.FinalConvergenceRole,
                        StringComparison.Ordinal));
            }
        }

        private void CreateLandmarkMarker(string label, string campaignRole, GridCoord coord, Vector2 origin, bool root)
        {
            GameObject prefab = authoringSettings != null ? authoringSettings.LandmarkPrefab : null;
            if (prefab == null)
                throw new InvalidOperationException("Authoring Settings must reference a Landmark prefab.");
            GameObject marker = Instantiate(prefab, WorldLandmarkRoot);
            marker.name = "Landmark · " + (label ?? campaignRole ?? "POI");
            marker.transform.position = WorldPosition(origin, coord) + new Vector3(0f, 0.1f, -0.05f);
            SpriteRenderer renderer = marker.GetComponent<SpriteRenderer>();
            if (renderer == null)
                throw new InvalidOperationException("Landmark prefab must contain a SpriteRenderer on its root.");
            renderer.sprite = root ? _d20Sprite : (_bookSprite != null ? _bookSprite : _whiteSprite);
            renderer.color = CampaignRoleColor(campaignRole);
            renderer.sortingOrder = 80;
            float size = authoringSettings == null
                ? root ? 0.68f : 0.42f
                : root ? authoringSettings.RootLandmarkVisualSize : authoringSettings.LandmarkVisualSize;
            ScaleSprite(marker.transform, renderer.sprite, size);
        }

        private static Color CampaignRoleColor(string campaignRole)
        {
            switch ((campaignRole ?? string.Empty).ToUpperInvariant())
            {
                case CampaignCatalog.LocalStakesRole: return Hex("efc65d");
                case CampaignCatalog.RelationshipConflictRole: return Hex("e88c4d");
                case CampaignCatalog.HiddenTruthRole: return Hex("70c7d8");
                case CampaignCatalog.ConsequenceReturnRole: return Hex("d377b2");
                case CampaignCatalog.FinalConvergenceRole: return Hex("f4e6a0");
                default: return Hex("b6dc74");
            }
        }

        private Sprite SpriteForEntity(EntityView entity)
        {
            return SpriteForAsset(entity.AssetId, entity.Kind.ToString(), entity.EntityId == _service.CurrentView.PlayerEntityId);
        }

        private Sprite[] FramesForEntity(EntityView entity)
        {
            return FramesForAsset(entity.AssetId, entity.Kind.ToString(), entity.EntityId == _service.CurrentView.PlayerEntityId);
        }

        private static Color EntityTint(EntityView entity)
        {
            return TintForAsset(entity.AssetId, entity.Kind.ToString());
        }

        private Sprite SpriteForAsset(string assetId, string kind, bool isPlayer)
        {
            string id = (assetId ?? string.Empty).ToLowerInvariant();
            string entityKind = (kind ?? string.Empty).ToLowerInvariant();
            if (isPlayer || id.Contains("player")) return _playerSprite;
            ActorAnimationEntry actorAnimation = ActorAnimationForAsset(assetId, kind);
            if (actorAnimation?.DefaultSprite != null &&
                (entityKind.Contains("npc") || entityKind.Contains("enemy") || id.StartsWith("boss.")))
                return actorAnimation.DefaultSprite;
            if (id == "artifact.keyboard" || id.Contains("rune-book") || id.Contains("hidden-truth")) return _bookSprite;
            if (id.Contains("admin-access") || id.Contains("altar") || id.Contains("sign")) return _d20Sprite;
            if (id.StartsWith("finale."))
                return id.Contains("memory") || id.Contains("witness") ? _bookSprite : _d20Sprite;
            if (id.Contains("root-core") || id.Contains("stabilizer") || id.Contains("autonomy-core") ||
                id.Contains("error-core") || id.Contains("return-gate")) return _d20Sprite;
            if (id.Contains("world-backup") || id.Contains("survivor") || id.Contains("focus-shard")) return _chestSprite;
            if (id.Contains("slime") || entityKind.Contains("enemy")) return _slimeSprite;
            if (id.Contains("samurai") || id.Contains("warden")) return _wardenSprite;
            if (entityKind.Contains("npc") || id.Contains("villager") || id.Contains("merchant") || id.Contains("healer")) return _villagerSprite;
            if (id.Contains("chest") || id.Contains("treasure")) return _chestSprite;
            if (id.Contains("book") || id.Contains("clue") || id.Contains("scroll")) return _bookSprite;
            if (entityKind.Contains("quest") || id.Contains("anchor") || id.Contains("relay") || id.Contains("shrine")) return _d20Sprite;
            return _crateSprite;
        }

        private Sprite SpriteForServerEntity(GameApiClient.EntitySnapshot entity, bool isPlayer)
        {
            string component = entity?.state?.rootComponent;
            switch ((component ?? string.Empty).ToLowerInvariant())
            {
                case "backup": return _chestSprite;
                case "survivor_record": return _bookSprite;
                case "error_core": return _slimeSprite;
                case "autonomy_core": return _bookSprite;
                case "access":
                case "stabilizer":
                case "return_device": return _d20Sprite;
                default: return SpriteForAsset(entity?.assetId, entity?.kind, isPlayer);
            }
        }

        private static Color ServerEntityTint(GameApiClient.EntitySnapshot entity)
        {
            string component = entity?.state?.rootComponent;
            switch ((component ?? string.Empty).ToLowerInvariant())
            {
                case "access": return Hex("f2d46b");
                case "stabilizer": return Hex("67c8dc");
                case "backup": return Hex("6e9fd6");
                case "autonomy_core": return Hex("8fd363");
                case "error_core": return Hex("df5b55");
                case "return_device": return Hex("d58fdc");
                case "survivor_record": return Hex("ead6a1");
                default: return TintForAsset(entity?.assetId, entity?.kind);
            }
        }

        private GameObject CreateRootComponentLabel(
            string component,
            string displayName,
            Color tint,
            KeyboardWandererEntityView authoredView = null)
        {
            if (authoredView == null || authoredView.FinaleLabelText == null)
                throw new InvalidOperationException("Entity Visual prefab must contain an authored Finale Label TextMesh.");
            GameObject label = authoredView.FinaleLabel;
            TextMesh text = authoredView.FinaleLabelText;
            label.SetActive(true);
            label.name = "Finale label · " + component;
            text.text = RootComponentLabel(component, displayName);
            text.color = Color.Lerp(tint, Color.white, 0.28f);
            return label;
        }

        private static string RootComponentLabel(string component, string fallback)
        {
            switch ((component ?? string.Empty).ToLowerInvariant())
            {
                case "access": return "ANCHOR";
                case "stabilizer": return "SAFEGUARD";
                case "backup": return "MEMORY";
                case "autonomy_core": return "FREEDOM";
                case "error_core": return "THREAT";
                case "return_device": return "PASSAGE";
                case "survivor_record": return "WITNESS";
                default: return FirstNonEmpty(fallback, component, "FINALE");
            }
        }


        private static Color TintForAsset(string assetId, string kind)
        {
            string id = (assetId ?? string.Empty).ToLowerInvariant();
            if (id.Contains("finale.anchor")) return Hex("f2d46b");
            if (id.Contains("finale.safeguard")) return Hex("79c7d7");
            if (id.Contains("finale.memory")) return Hex("77a7d8");
            if (id.Contains("finale.freedom")) return Hex("9cd66f");
            if (id.Contains("finale.threat")) return Hex("db625c");
            if (id.Contains("finale.passage")) return Hex("d69adb");
            if (id.Contains("finale.witness")) return Hex("ead6a1");
            if (id.Contains("stabilizer")) return Hex("79c7d7");
            if (id.Contains("world-backup")) return Hex("77a7d8");
            if (id.Contains("autonomy-core")) return Hex("9cd66f");
            if (id.Contains("error-core")) return Hex("db625c");
            if (id.Contains("return-gate")) return Hex("d69adb");
            if (id.Contains("survivor")) return Hex("ead6a1");
            if (id.Contains("admin-access")) return Hex("efc65d");
            if (id.Contains("hidden-truth") || id.Contains("rune-book")) return Hex("70c7d8");
            return TintForKind(kind);
        }

        private Sprite[] FramesForAsset(string assetId, string kind, bool isPlayer)
        {
            string id = (assetId ?? string.Empty).ToLowerInvariant();
            string entityKind = (kind ?? string.Empty).ToLowerInvariant();
            if (isPlayer || id.Contains("player")) return _playerIdleFrames;
            if (ActorAnimationForAsset(assetId, kind)?.AnimatorController != null) return Array.Empty<Sprite>();
            if (id.Contains("slime") || entityKind.Contains("enemy")) return _slimeFrames;
            if (entityKind.Contains("npc") || id.Contains("villager") || id.Contains("merchant") || id.Contains("healer")) return _villagerFrames;
            return Array.Empty<Sprite>();
        }

        private RuntimeAnimatorController AnimatorForAsset(string assetId, string kind, bool isPlayer)
        {
            if (_assets == null)
                return null;
            string id = (assetId ?? string.Empty).ToLowerInvariant();
            string entityKind = (kind ?? string.Empty).ToLowerInvariant();
            if (isPlayer || id.Contains("player"))
                return _assets.NeopjukiAtlas != null ? null : _assets.PlayerAnimatorController;
            ActorAnimationEntry actorAnimation = ActorAnimationForAsset(assetId, kind);
            if (actorAnimation?.AnimatorController != null)
                return actorAnimation.AnimatorController;
            if (id.Contains("slime") || entityKind.Contains("enemy"))
                return _assets.SlimeAnimatorController;
            if (entityKind.Contains("npc") || id.Contains("villager") || id.Contains("merchant") || id.Contains("healer"))
                return _assets.VillagerAnimatorController;
            return null;
        }

        private ActorAnimationEntry ActorAnimationForAsset(string assetId, string kind)
        {
            if (_assets == null)
                return null;

            string id = (assetId ?? string.Empty).ToLowerInvariant();
            ActorAnimationEntry exact = FindActorAnimation(_assets.NpcAnimations, id) ??
                                        FindActorAnimation(_assets.MonsterAnimations, id) ??
                                        FindActorAnimation(_assets.BossAnimations, id);
            if (exact != null)
                return exact;

            if (id.Contains("warden") || id.Contains("samurai"))
                return FindActorAnimation(_assets.NpcAnimations, "npc.samurai.v1");
            if (id.Contains("traveler") || id.Contains("old-man"))
                return FindActorAnimation(_assets.NpcAnimations, "npc.old-man.v1");

            string entityKind = (kind ?? string.Empty).ToLowerInvariant();
            ActorAnimationEntry[] catalog = id.StartsWith("boss.")
                ? _assets.BossAnimations
                : entityKind.Contains("enemy") ? _assets.MonsterAnimations
                : entityKind.Contains("npc") ? _assets.NpcAnimations
                : null;
            if (catalog == null || catalog.Length == 0)
                return null;
            return catalog[StableCatalogIndex(id, catalog.Length)];
        }

        private static ActorAnimationEntry FindActorAnimation(ActorAnimationEntry[] catalog, string assetId)
        {
            if (catalog == null)
                return null;
            for (int i = 0; i < catalog.Length; i++)
            {
                ActorAnimationEntry entry = catalog[i];
                if (entry != null && string.Equals(entry.AssetId, assetId, StringComparison.OrdinalIgnoreCase))
                    return entry;
            }
            return null;
        }

        private static int StableCatalogIndex(string value, int count)
        {
            uint hash = 2166136261;
            for (int i = 0; i < value.Length; i++)
            {
                hash ^= value[i];
                hash *= 16777619;
            }
            return (int)(hash % (uint)count);
        }

        private static bool HasAnimatorParameter(Animator animator, string parameterName)
        {
            if (animator == null)
                return false;
            AnimatorControllerParameter[] parameters = animator.parameters;
            for (int i = 0; i < parameters.Length; i++)
                if (parameters[i].type == AnimatorControllerParameterType.Float &&
                    string.Equals(parameters[i].name, parameterName, StringComparison.Ordinal))
                    return true;
            return false;
        }

        private static void SetAnimatorFloat(Animator animator, string parameterName, float value)
        {
            if (HasAnimatorParameter(animator, parameterName))
                animator.SetFloat(parameterName, value);
        }

        private static Animator ConfigureEntityAnimator(
            SpriteRenderer renderer,
            RuntimeAnimatorController controller)
        {
            if (renderer == null)
                return null;
            Animator animator = renderer.GetComponent<Animator>();
            if (animator == null)
                throw new InvalidOperationException(
                    "The authored entity prefab Actor Renderer must include an Animator component.");
            animator.enabled = false;
            animator.runtimeAnimatorController = null;
            if (controller == null)
                return null;
            animator.runtimeAnimatorController = controller;
            animator.enabled = true;
            animator.Rebind();
            animator.Update(0f);
            return animator;
        }

        private static Color TintForKind(string kind)
        {
            return Color.white;
        }

        private void SetAbility(AbilityKind ability)
        {
            if (!string.IsNullOrEmpty(AbilityInputBlockReason(ability)))
                return;
            if (!IsOfferedAbility(ability))
            {
                Debug.LogWarning("[KW.Input] event=SetAbility ability=" + ability +
                                 " result=blocked reason=not-offered-by-current-choice");
                PlaySfx(AssetClip("UiCancelSound"));
                return;
            }
            if (!_selectionController.ChangeAbility(ability, _lastRestorableTarget, _lastRestorableCoord))
                return;
            if (_service != null)
                UpdateSelectionVisual(_service.CurrentView);
            PlaySfx(AssetClip("UiMoveSound"));
        }

        private string SecondaryObjectiveText(RunView view)
        {
            return KeyboardWandererHudTextComposer.SecondaryObjectives(PresentationModel(view));
        }


        private AbilityKind[] RecommendedActions(RunView view)
        {
            return KeyboardWandererHudTextComposer.RecommendedActions(PresentationModel(view),
                _encounterMoveRequired);
        }

        private string ObjectiveHudText(RunView view)
        {
            return KeyboardWandererHudTextComposer.ObjectiveHud(PresentationModel(view));
        }

        private string QuestActionHintText(RunView view)
        {
            return KeyboardWandererHudTextComposer.QuestActionHint(PresentationModel(view),
                _encounterMoveRequired);
        }

        private string ActionGuidanceText(RunView view)
        {
            return ActionGuidanceTextForCurrentFlow(view, CanSubmitCurrentSelection());
        }

        private string ActionGuidanceTextForCurrentFlow(RunView view, bool selectionReady)
        {
            if (_encounterMoveRequired && IsOpenServerEncounter(_serverRun?.activeEncounter))
            {
                GameApiClient.ActiveEncounterSnapshot encounter = _serverRun.activeEncounter;
                return FirstNonEmpty(encounter.title, "앞을 막은 사건") + " · 어떤 스킬을 사용할까?\n" +
                       FirstNonEmpty(encounter.description, _encounterReason) + "\n선택지: " + SuggestedSkillList();
            }
            if (!string.IsNullOrWhiteSpace(_lastNextInterventionReason))
                return _lastNextInterventionReason + "\n어디로 이동할까, 또는 어떤 스킬을 사용할까?\n" +
                       MovementChoiceHint(view) + " · 스킬: " + SuggestedSkillList() +
                       " (D20 · 의미 턴 1회)";
            if (selectionReady)
                return ExecutionPreview(view);
            return KeyboardWandererHudTextComposer.ObjectiveRouteHint(PresentationModel(view)) +
                   NarrativeSelectionHint() +
                   "\nMOVE는 턴을 쓰지 않고, 키보드 기술은 D20과 의미 턴 1회를 사용합니다.";
        }

        private string PendingActionGuidance()
        {
            string status = FirstNonEmpty(_serverStatus, "요청 처리 중");
            string action = !string.IsNullOrWhiteSpace(_pendingPlayerMessageText)
                ? "자유 입력"
                : !string.IsNullOrWhiteSpace(_pendingNarrativeChoiceLabel)
                    ? _pendingNarrativeChoiceLabel
                    : AbilityPlayerLabel(_selectionController.Ability);
            return action + " · 처리 중\n" + status +
                   "\n입력이 접수되었습니다. 결과가 확정될 때까지 같은 행동을 다시 누르지 않아도 됩니다.";
        }

        private string[] CurrentSuggestedSkillIds()
        {
            GameApiClient.ActiveEncounterSnapshot encounter = _serverRun?.activeEncounter;
            if (IsOpenServerEncounter(encounter) && encounter.suggestedSkillIds != null &&
                encounter.suggestedSkillIds.Length > 0)
                return encounter.suggestedSkillIds;
            return _lastSuggestedSkillIds ?? Array.Empty<string>();
        }

        private bool IsOfferedAbility(AbilityKind ability)
        {
            if (!_serverOnline) return true;
            if (ability == AbilityKind.Move) return !_encounterMoveRequired;
            if (ability == AbilityKind.Interact) return true;
            // Suggested skills are guidance, not a world-action allowlist. Restrict
            // buttons only while an encounter or a visible server-sealed choice owns
            // input. Once the player dismisses that surface for exploration, every
            // mechanically valid ability must be available again.
            bool encounterOwnsInput = IsOpenServerEncounter(_serverRun?.activeEncounter);
            bool sealedChoiceOwnsInput = _dialoguePresenter != null && !_dialoguePresenter.IsDismissed &&
                                         HasSealedNarrativeChoiceSet();
            if (!encounterOwnsInput && !sealedChoiceOwnsInput) return true;
            string[] offered = CurrentSuggestedSkillIds();
            if (offered.Length == 0) return true;
            string skillId = ability == AbilityKind.SelectAll ? "SELECT_ALL" : ability.ToString().ToUpperInvariant();
            for (int i = 0; i < offered.Length; i++)
                if (string.Equals(offered[i], skillId, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private string SuggestedSkillList()
        {
            string[] offered = CurrentSuggestedSkillIds();
            if (offered.Length == 0) return "현재 사용 가능한 스킬";
            var labels = new List<string>();
            for (int i = 0; i < offered.Length; i++)
            {
                if (TryAbilityForSkillId(offered[i], out AbilityKind ability))
                    labels.Add(AbilityPlayerLabel(ability));
            }
            return labels.Count == 0 ? "현재 사용 가능한 스킬" : string.Join(" · ", labels);
        }

        private string MovementChoiceHint(RunView view)
        {
            GameApiClient.PointSnapshot[] points = _serverRun?.world?.points ?? _serverRun?.world?.pois;
            if (points == null || points.Length == 0 || !TryGetPlayerPosition(view, out GridCoord player))
                return "이동: 지도 클릭 또는 좌우 방향키로 목적지 선택";
            var candidates = new List<GameApiClient.PointSnapshot>();
            for (int i = 0; i < points.Length; i++)
                if (points[i] != null && !string.Equals(points[i].kind, "hub", StringComparison.OrdinalIgnoreCase))
                    candidates.Add(points[i]);
            candidates.Sort((left, right) =>
                (Math.Abs(left.x - player.X) + Math.Abs(left.y - player.Y)).CompareTo(
                    Math.Abs(right.x - player.X) + Math.Abs(right.y - player.Y)));
            var labels = new List<string>();
            for (int i = 0; i < Math.Min(3, candidates.Count); i++)
                labels.Add(FirstNonEmpty(candidates[i].nameKo, candidates[i].name, candidates[i].id, "목적지"));
            return labels.Count == 0
                ? "이동: 지도 클릭 또는 좌우 방향키로 목적지 선택"
                : "이동: " + string.Join(" / ", labels) + " (좌우 방향키로 선택)";
        }

        private static bool TryAbilityForSkillId(string skillId, out AbilityKind ability)
        {
            switch ((skillId ?? string.Empty).ToUpperInvariant())
            {
                case "COPY": ability = AbilityKind.Copy; return true;
                case "DELETE": ability = AbilityKind.Delete; return true;
                case "CONNECT": ability = AbilityKind.Connect; return true;
                case "RESTORE": ability = AbilityKind.Restore; return true;
                case "UNDO": ability = AbilityKind.Undo; return true;
                case "SEARCH": ability = AbilityKind.Search; return true;
                case "SELECT_ALL": ability = AbilityKind.SelectAll; return true;
                default: ability = AbilityKind.Move; return false;
            }
        }

        private static string AbilityPlayerLabel(AbilityKind ability)
        {
            return KeyboardWandererHudTextComposer.AbilityPlayerLabel(ability);
        }

        private string ExecutionPreview(RunView view)
        {
            string target = _selectionController.Ability == AbilityKind.Move
                ? (_selectionController.SelectedCoord.HasValue ? _selectionController.SelectedCoord.Value.ToString() : "목적지 미선택")
                : _selectionController.Ability == AbilityKind.Undo ? "직전 가역 행동"
                : _selectionController.SelectedTarget.HasValue ? DisplayEntityName(view, _selectionController.SelectedTarget.Value) : "대상 미선택";
            if (_selectionController.Ability == AbilityKind.Connect && _selectionController.SelectedSecondaryTarget.HasValue)
                target += " + " + DisplayEntityName(view, _selectionController.SelectedSecondaryTarget.Value);
            bool consumes = _selectionController.Ability != AbilityKind.Move;
            string risk = _selectionController.Ability == AbilityKind.Move ? "경로상 사건 활성화 가능"
                : _selectionController.Ability == AbilityKind.Interact ? "낮음 · 대상 확인"
                : _selectionController.Ability == AbilityKind.Delete || _selectionController.Ability == AbilityKind.Undo ? "높음 · 기술 부채 발생 가능"
                : _selectionController.Ability == AbilityKind.Connect ? "중간 · 관계/배치 결과"
                : "중간 · D20 결과 적용";
            return "대상  " + target + "\n스킬  " + (_selectionController.Ability == AbilityKind.Move ? "MOVE" : _selectionController.Ability.ToString().ToUpperInvariant()) +
                   "\n문맥  " + ContextPreview(_selectionController.Ability, view) + "\n턴 소비  " +
                   (consumes ? "예 · D20 사용" : "아니오 · D20 없음") + "\n예상 위험  " + risk;
        }

        private string ContextPreview(AbilityKind ability, RunView view)
        {
            if (ability == AbilityKind.Move) return "안전 이동";
            if (ability == AbilityKind.Copy) return "배치";
            if (ability == AbilityKind.Search) return "조사";
            if (ability == AbilityKind.Interact)
            {
                if (_selectionController.SelectedTarget.HasValue)
                {
                    if (TryGetServerEntity(_selectionController.SelectedTarget, out GameApiClient.EntitySnapshot serverEntity) &&
                        string.Equals(serverEntity.kind, "npc", StringComparison.OrdinalIgnoreCase))
                        return "협상";
                    for (int i = 0; i < view.Entities.Count; i++)
                        if (view.Entities[i].EntityId == _selectionController.SelectedTarget.Value && view.Entities[i].Kind == EntityKind.Npc)
                            return "협상";
                }
                return "조사";
            }
            if (ability == AbilityKind.Delete || ability == AbilityKind.SelectAll) return "전투";
            if (ability == AbilityKind.Connect)
            {
                if (_selectionController.SelectedTarget.HasValue)
                {
                    if (TryGetServerEntity(_selectionController.SelectedTarget, out GameApiClient.EntitySnapshot serverEntity) &&
                        string.Equals(serverEntity.kind, "npc", StringComparison.OrdinalIgnoreCase))
                        return "협상";
                    for (int i = 0; i < view.Entities.Count; i++)
                        if (view.Entities[i].EntityId == _selectionController.SelectedTarget.Value && view.Entities[i].Kind == EntityKind.Npc)
                            return "협상";
                }
                return "배치";
            }
            return "배치";
        }

        private static string ShortNarrative(string text)
        {
            return KeyboardWandererHudTextComposer.ShortNarrative(text);
        }

        private void AddLog(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return;
            _sessionLog.Add(message.Trim());
            if (_sessionLog.Count > 18)
                _sessionLog.RemoveAt(0);
        }

        private static void ScaleSprite(Transform target, Sprite sprite, float desiredSize)
        {
            if (target == null || sprite == null)
                return;
            float largest = Mathf.Max(sprite.bounds.size.x, sprite.bounds.size.y);
            float scale = largest > 0.001f ? desiredSize / largest : desiredSize;
            target.localScale = new Vector3(scale, scale, 1f);
        }

        private Vector2 MapOrigin(RunView view)
        {
            return new Vector2(-ActiveWorldWidth(view) * TileSize * 0.5f,
                -ActiveWorldHeight(view) * TileSize * 0.5f);
        }

        private int ActiveWorldWidth(RunView view)
            => _serverOnline && _serverRun?.world != null && _serverRun.world.width > 0
                ? _serverRun.world.width
                : view.Region.Width;

        private int ActiveWorldHeight(RunView view)
            => _serverOnline && _serverRun?.world != null && _serverRun.world.height > 0
                ? _serverRun.world.height
                : view.Region.Height;

        private static Vector3 WorldPosition(Vector2 origin, GridCoord coord)
        {
            return new Vector3(origin.x + (coord.X + 0.5f) * TileSize, origin.y + (coord.Y + 0.5f) * TileSize, 0f);
        }

        private static Guid? FindTarget(RunView view, GridCoord coord)
        {
            Guid? fallback = null;
            for (int i = 0; i < view.Entities.Count; i++)
            {
                EntityView entity = view.Entities[i];
                if (entity.Position != coord || entity.EntityId == view.PlayerEntityId)
                    continue;
                if (entity.Kind == EntityKind.Enemy)
                    return entity.EntityId;
                fallback = entity.EntityId;
            }
            return fallback;
        }

        private Guid? FindVisualTargetAt(Vector3 worldPosition)
        {
            Guid? fallback = null;
            foreach (KeyValuePair<Guid, KeyboardWandererEntityVisualState> pair in _entityVisuals)
            {
                KeyboardWandererEntityVisualState visual = pair.Value;
                if (visual == null || visual.IsPlayer || visual.Renderer == null || !visual.Renderer.enabled)
                    continue;
                Bounds bounds = visual.Renderer.bounds;
                float padding = Mathf.Max(0.22f, Mathf.Min(bounds.size.x, bounds.size.y) * 0.12f);
                bool contains = worldPosition.x >= bounds.min.x - padding && worldPosition.x <= bounds.max.x + padding &&
                                worldPosition.y >= bounds.min.y - padding && worldPosition.y <= bounds.max.y + padding;
                if (!contains)
                    continue;
                if (visual.IsHostile)
                    return pair.Key;
                fallback = pair.Key;
            }
            return fallback;
        }

        private bool TryGetEntityPosition(RunView view, Guid id, out GridCoord position)
        {
            if (_serverOnline && _serverRun?.entities != null)
            {
                string key = id.ToString();
                for (int i = 0; i < _serverRun.entities.Length; i++)
                {
                    GameApiClient.EntitySnapshot entity = _serverRun.entities[i];
                    if (entity?.position != null && string.Equals(entity.id, key, StringComparison.OrdinalIgnoreCase))
                    {
                        position = new GridCoord(entity.position.x, entity.position.y);
                        return true;
                    }
                }
            }
            for (int i = 0; i < view.Entities.Count; i++)
            {
                EntityView entity = view.Entities[i];
                if (entity.EntityId != id) continue;
                position = entity.Position;
                return true;
            }
            position = default;
            return false;
        }

        private bool IsInteractableTarget(RunView view, Guid id)
        {
            if (_serverOnline && _serverRun?.entities != null)
            {
                string key = id.ToString();
                for (int i = 0; i < _serverRun.entities.Length; i++)
                {
                    GameApiClient.EntitySnapshot entity = _serverRun.entities[i];
                    if (entity == null || !string.Equals(entity.id, key, StringComparison.OrdinalIgnoreCase))
                        continue;
                    return string.Equals(entity.kind, "prop", StringComparison.OrdinalIgnoreCase) ||
                           string.Equals(entity.kind, "npc", StringComparison.OrdinalIgnoreCase);
                }
                return false;
            }
            for (int i = 0; i < view.Entities.Count; i++)
            {
                EntityView entity = view.Entities[i];
                if (entity.EntityId == id)
                    return (entity.Kind == EntityKind.Prop || entity.Kind == EntityKind.Npc) && !entity.IsHostile;
            }
            return false;
        }

        private static string EntityName(RunView view, Guid id)
        {
            for (int i = 0; i < view.Entities.Count; i++)
                if (view.Entities[i].EntityId == id)
                    return view.Entities[i].DisplayName;
            return "알 수 없는 대상";
        }

        private string DisplayEntityName(RunView view, Guid id)
        {
            if (_serverOnline && _serverRun?.entities != null)
            {
                string key = id.ToString();
                for (int i = 0; i < _serverRun.entities.Length; i++)
                {
                    GameApiClient.EntitySnapshot entity = _serverRun.entities[i];
                    if (entity != null && string.Equals(entity.id, key, StringComparison.OrdinalIgnoreCase))
                        return string.IsNullOrWhiteSpace(entity.name) ? "이름 없는 대상" : entity.name;
                }
            }
            return EntityName(view, id);
        }

        private bool TryGetServerEntity(Guid? id, out GameApiClient.EntitySnapshot result)
        {
            result = null;
            if (!_serverOnline || !id.HasValue || _serverRun?.entities == null)
                return false;
            string key = id.Value.ToString();
            for (int i = 0; i < _serverRun.entities.Length; i++)
            {
                GameApiClient.EntitySnapshot entity = _serverRun.entities[i];
                if (entity == null || !string.Equals(entity.id, key, StringComparison.OrdinalIgnoreCase)) continue;
                result = entity;
                return true;
            }
            return false;
        }

        private Guid? FindServerTarget(GridCoord coord, bool allowPlayer)
        {
            if (_serverRun?.entities == null)
                return null;
            Guid? fallback = null;
            for (int i = 0; i < _serverRun.entities.Length; i++)
            {
                GameApiClient.EntitySnapshot entity = _serverRun.entities[i];
                bool isPlayer = entity != null && string.Equals(entity.id, _serverRun.playerEntityId, StringComparison.OrdinalIgnoreCase);
                if (entity?.position == null || entity.position.x != coord.X || entity.position.y != coord.Y ||
                    (isPlayer && !allowPlayer) || !Guid.TryParse(entity.id, out Guid id))
                    continue;
                if (string.Equals(entity.kind, "enemy", StringComparison.OrdinalIgnoreCase))
                    return id;
                fallback = id;
            }
            return fallback;
        }

        private bool TryGetPlayerPosition(RunView view, out GridCoord position)
        {
            if (_serverOnline && _serverRun?.entities != null)
            {
                for (int i = 0; i < _serverRun.entities.Length; i++)
                {
                    GameApiClient.EntitySnapshot entity = _serverRun.entities[i];
                    if (entity?.position != null && string.Equals(entity.id, _serverRun.playerEntityId, StringComparison.OrdinalIgnoreCase))
                    {
                        position = new GridCoord(entity.position.x, entity.position.y);
                        return true;
                    }
                }
            }
            if (view == null)
            {
                position = default;
                return false;
            }
            position = view.PlayerPosition;
            return true;
        }

        private string CurrentAreaName(RunView view)
        {
            return PresentationModel(view).CurrentAreaName;
        }

        private bool ContainsWorldCoord(RunView view, GridCoord coord)
        {
            if (_serverOnline && _serverRun?.world != null)
                return coord.X >= 0 && coord.X < _serverRun.world.width && coord.Y >= 0 && coord.Y < _serverRun.world.height;
            return view.Region.Contains(coord);
        }

        private static TileKind TileKindForServer(GameApiClient.WorldSnapshot world, int code)
        {
            string name = world?.tileLegend != null && code >= 0 && code < world.tileLegend.Length
                ? (world.tileLegend[code] ?? string.Empty).ToLowerInvariant()
                : string.Empty;
            if (name.Contains("wall")) return TileKind.Wall;
            if (name.Contains("hazard")) return TileKind.Hazard;
            if (name.Contains("water")) return TileKind.Water;
            if (name.Contains("road") || name.Contains("dirt")) return TileKind.Dirt;
            if (name.Contains("bridge")) return TileKind.Bridge;
            if (name.Contains("ruin")) return TileKind.Ruin;
            if (name.Contains("dark")) return TileKind.DarkGrass;
            return TileKind.Grass;
        }

        private string CampaignPremise(RunView view)
        {
            return PresentationModel(view).CampaignPremise;
        }

        private string StoryBeat(RunView view)
        {
            return PresentationModel(view).StoryBeat;
        }

        private string StoryBeatObjective(RunView view)
        {
            return PresentationModel(view).StoryObjective;
        }

        private int CurrentTurn(RunView view) => PresentationModel(view).Core.Turn;
        private int TurnLimit(RunView view) => PresentationModel(view).TurnLimit;
        private int AdminAccessLevel(RunView view) => PresentationModel(view).AdminAccess;
        private string CurrentBiomeLabel(RunView view)
        {
            string id = PresentationModel(view).CurrentBiomeId;
            switch (id)
            {
                case "temperate_forest_field": return "온대 숲·들판";
                case "river_wetland": return "강·습지";
                case "arid_desert": return "건조 사막";
                case "frost_highland": return "설원 고지";
                case "subterranean_cavern": return "지하 동굴";
                case "ancient_ruins": return "고대 유적";
                case "root_system": return "루트 시스템 · 코어";
                default: return string.IsNullOrWhiteSpace(id) ? "바이옴 미확인" : id;
            }
        }

        private string EndingCode(RunView view) => PresentationModel(view).EndingCode;

        private string PlayerDisplayName(RunView view)
        {
            return PresentationModel(view).PlayerName;
        }

        private bool RunIsPlaying(RunView view)
        {
            return PresentationModel(view).IsPlaying;
        }

        /// <summary>현재 실행 경로를 로컬·서버 공통 화면 모델로 정규화하고 최신 값을 보관한다.</summary>
        private RunPresentationModel PresentationModel(RunView view)
        {
            bool useServer = _serverOnline && _serverRun != null;
            if (useServer && !(_runPresentationAdapter is ServerRunPresentationAdapter))
                _runPresentationAdapter = new ServerRunPresentationAdapter(() => _serverRun, () => _serverCampaign);
            else if (!useServer && !(_runPresentationAdapter is LocalRunPresentationAdapter))
                _runPresentationAdapter = new LocalRunPresentationAdapter();
            _runPresentationModel = _runPresentationAdapter?.Capture(view) ?? RunPresentationModel.Empty;
            return _runPresentationModel;
        }

        /// <summary>화자 이름으로 서버·로컬 엔티티를 차례로 찾아 버스트 스프라이트를 결정한다.</summary>
        private Sprite SpriteForSpeakerName(string speakerName)
        {
            if (string.IsNullOrWhiteSpace(speakerName))
                return null;
            string name = speakerName.Trim();
            if (_serverRun?.entities != null)
            {
                for (int i = 0; i < _serverRun.entities.Length; i++)
                {
                    GameApiClient.EntitySnapshot entity = _serverRun.entities[i];
                    if (entity == null || !string.Equals(entity.name?.Trim(), name, StringComparison.Ordinal))
                        continue;
                    return SpriteForServerEntity(entity,
                        string.Equals(entity.id, _serverRun.playerEntityId, StringComparison.OrdinalIgnoreCase));
                }
            }
            if (_service != null)
            {
                IReadOnlyList<EntityView> entities = _service.CurrentView.Entities;
                for (int i = 0; i < entities.Count; i++)
                    if (string.Equals(entities[i].DisplayName?.Trim(), name, StringComparison.Ordinal))
                        return SpriteForEntity(entities[i]);
            }
            return null;
        }

        /// <summary>서버 엔티티 id로 대화창 버스트에 쓸 스프라이트를 찾는다. 미확인 화자는 null.</summary>
        private Sprite ServerEntitySprite(string entityId)
        {
            if (_serverRun?.entities == null || string.IsNullOrWhiteSpace(entityId))
                return null;
            for (int i = 0; i < _serverRun.entities.Length; i++)
            {
                GameApiClient.EntitySnapshot entity = _serverRun.entities[i];
                if (entity == null || !string.Equals(entity.id, entityId, StringComparison.OrdinalIgnoreCase))
                    continue;
                return SpriteForServerEntity(entity,
                    string.Equals(entity.id, _serverRun.playerEntityId, StringComparison.OrdinalIgnoreCase));
            }
            return null;
        }

        private string ServerEntityName(string entityId, string fallback)
        {
            if (_serverRun?.entities != null && !string.IsNullOrWhiteSpace(entityId))
            {
                for (int i = 0; i < _serverRun.entities.Length; i++)
                {
                    GameApiClient.EntitySnapshot entity = _serverRun.entities[i];
                    if (entity != null && string.Equals(entity.id, entityId, StringComparison.OrdinalIgnoreCase) &&
                        !string.IsNullOrWhiteSpace(entity.name))
                        return entity.name;
                }
            }
            return string.IsNullOrWhiteSpace(fallback) ? "NPC" : fallback;
        }



        private void ApplyServerTurnPresentation(GameApiClient.TurnSnapshot turn)
        {
            _lastServerSceneSequence = turn?.sceneSequence ?? Array.Empty<GameApiClient.SceneActionSnapshot>();
            _pendingRuntimeRenderSequence = ServerTurnPresentationAdapter.BuildRuntimeRenderSequence(turn, _serverRun);
            ApplyTurnPresentation(ServerTurnPresentationAdapter.FromTurn(turn, _serverRun, GmEnabled));
            RecoverPendingChoiceSet(_serverRun?.pendingChoiceSet, false);
        }

        private void PlayCurrentStoryWorldAction(int page)
        {
            if (_lastStorySequence == null || page < 0 || page >= _lastStorySequence.Length) return;
            StorySequencePage beat = _lastStorySequence[page];
            if (!string.Equals(beat?.Type, "WORLD_ACTION", StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrWhiteSpace(beat.ActionId) || _playedStoryActionIds.Contains(beat.ActionId)) return;
            GameApiClient.SceneActionSnapshot action = null;
            for (int i = 0; i < _lastServerSceneSequence.Length; i++)
                if (string.Equals(_lastServerSceneSequence[i]?.actionId, beat.ActionId, StringComparison.Ordinal))
                {
                    action = _lastServerSceneSequence[i];
                    break;
                }
            if (action == null)
            {
                Debug.LogWarning("[KW.Story] WORLD_ACTION ignored: confirmed action not found · " + beat.ActionId);
                return;
            }
            _playedStoryActionIds.Add(beat.ActionId);
            if (_storyWorldActionCoroutine != null)
                StopCoroutine(_storyWorldActionCoroutine);
            _storyWorldActionPlaying = true;
            Coroutine started = StartCoroutine(PlayStoryWorldAction(action));
            // StartCoroutine은 첫 yield까지 즉시 실행할 수 있다. 아주 짧은 액션이 이미
            // 끝났다면 완료된 Coroutine 참조를 다시 저장해 UI를 잠그지 않는다.
            _storyWorldActionCoroutine = _storyWorldActionPlaying ? started : null;
        }

        private IEnumerator PlayStoryWorldAction(GameApiClient.SceneActionSnapshot action)
        {
            const float timeoutSeconds = 4f;
            float deadline = Time.unscaledTime + timeoutSeconds;
            try
            {
                while (_playerWalking && Time.unscaledTime < deadline)
                    yield return null;
                if (_playerWalking)
                {
                    Debug.LogWarning("[KW.Story] WORLD_ACTION timed out waiting for player movement · " +
                                     action?.actionId);
                    yield break;
                }
                yield return PlayServerSceneAction(action);
            }
            finally
            {
                _storyWorldActionPlaying = false;
                _storyWorldActionCoroutine = null;
                _pendingPresentationChanges |= PresentationChange.Dialogue;
            }
        }

        // ── 스킬 시전 모션 ───────────────────────────────────────────────────────────
        // 실제 스킬 이펙트(VFX)는 서버 gameplayResult.fx.effectId를 그대로 재생하는
        // PlayElementalAttackEffect(ApplyTurnPresentation 참조)가 담당한다. 여기서는
        // 넙죽이가 스킬에 어울리는 시전 모션을 잠깐 재생하는 것만 다룬다.

        /// <summary>스킬에 어울리는 넙죽이 모션을 골라 시전 창을 연다. 공격 스킬은 대상 쪽을 바라본다.</summary>
        private void SetPlayerSkillMotion(AbilityKind skill, List<Vector3> targets)
        {
            if (skill == AbilityKind.Move || skill == AbilityKind.Interact)
                return;
            _playerActionFrames = SkillMotionFrames(skill);
            if ((skill == AbilityKind.Delete || skill == AbilityKind.SelectAll) && targets != null && targets.Count > 0)
                FacePlayerToward(targets[0]);
            bool hasMotion = _playerActionFrames != null && _playerActionFrames.Length > 0;
            float duration = hasMotion ? 0.9f : 0.55f;
            _playerActionUntil = Mathf.Max(_playerActionUntil, Time.unscaledTime + duration);
        }

        /// <summary>스킬 → 넙죽이 시전 모션. null이면 방향별 기본 공격 모션(DELETE)을 사용한다.</summary>
        private Sprite[] SkillMotionFrames(AbilityKind skill)
        {
            if (_visualAssetLibrary == null)
                return null;
            switch (skill)
            {
                // 물리 공격은 방향이 있는 keyboard-attack 모션을 그대로 쓴다.
                case AbilityKind.Delete: return null;
                // 복제·연결·복원·영역전개는 마법 시전 모션.
                case AbilityKind.Copy:
                case AbilityKind.Connect:
                case AbilityKind.Restore:
                case AbilityKind.SelectAll: return _visualAssetLibrary.PlayerMagicFrames;
                // 시간 역행은 디버그(되감기) 모션.
                case AbilityKind.Undo: return _visualAssetLibrary.PlayerDebugFrames;
                // 조사는 리뷰(살펴보기) 모션.
                case AbilityKind.Search: return _visualAssetLibrary.PlayerReviewFrames;
                default: return null;
            }
        }

        private void ApplySkillMotionForLocalTurn(AbilityKind skill, RunView view)
        {
            SetPlayerSkillMotion(skill, ResolveTargetsLocal(skill, view));
        }

        private void ApplySkillMotionForServerTurn(GameApiClient.TurnSnapshot turn)
        {
            if (turn == null)
                return;
            AbilityKind skill = AbilityFromSkillId(turn.skillId);
            SetPlayerSkillMotion(skill, ResolveTargetsServer(skill, turn));
        }

        private List<Vector3> ResolveTargetsLocal(AbilityKind skill, RunView view)
        {
            var targets = new List<Vector3>();
            if (skill == AbilityKind.Undo)
            {
                targets.Add(PlayerWorldPosition(view));
                return targets;
            }
            if (skill == AbilityKind.SelectAll)
            {
                CollectHostilesNearPlayer(targets, 4f);
                if (targets.Count == 0)
                    targets.Add(PlayerWorldPosition(view));
                return targets;
            }
            if (_selectionController.SelectedTarget.HasValue)
                targets.Add(EntityWorldPosition(_selectionController.SelectedTarget.Value, view));
            if (skill == AbilityKind.Connect && _selectionController.SelectedSecondaryTarget.HasValue)
                targets.Add(EntityWorldPosition(_selectionController.SelectedSecondaryTarget.Value, view));
            if (targets.Count == 0)
                targets.Add(PlayerWorldPosition(view));
            return targets;
        }

        private List<Vector3> ResolveTargetsServer(AbilityKind skill, GameApiClient.TurnSnapshot turn)
        {
            var targets = new List<Vector3>();
            if (skill == AbilityKind.Undo)
            {
                if (TryGetPlayerWorldPosition(out Vector3 selfPosition))
                    targets.Add(selfPosition);
                return targets;
            }
            if (skill == AbilityKind.SelectAll)
            {
                CollectHostilesNearPlayer(targets, 4f);
                if (targets.Count == 0 && TryGetPlayerWorldPosition(out Vector3 center))
                    targets.Add(center);
                return targets;
            }
            if (turn.targetIds != null)
                for (int i = 0; i < turn.targetIds.Length; i++)
                    if (Guid.TryParse(turn.targetIds[i], out Guid id) &&
                        _entityVisuals.TryGetValue(id, out KeyboardWandererEntityVisualState visual) &&
                        visual?.Root != null)
                        targets.Add(visual.Root.transform.position);
            if (targets.Count == 0 && TryGetPlayerWorldPosition(out Vector3 fallback))
                targets.Add(fallback);
            return targets;
        }

        private void CollectHostilesNearPlayer(List<Vector3> into, float radiusTiles)
        {
            if (!TryGetPlayerWorldPosition(out Vector3 playerPosition))
                return;
            float limit = radiusTiles * TileSize + 0.1f;
            foreach (KeyValuePair<Guid, KeyboardWandererEntityVisualState> pair in _entityVisuals)
            {
                KeyboardWandererEntityVisualState visual = pair.Value;
                if (visual == null || visual.Root == null || visual.IsPlayer || !visual.IsHostile)
                    continue;
                if (Vector3.Distance(visual.Root.transform.position, playerPosition) <= limit)
                    into.Add(visual.Root.transform.position);
            }
        }

        private void FacePlayerToward(Vector3 worldTarget)
        {
            foreach (KeyValuePair<Guid, KeyboardWandererEntityVisualState> pair in _entityVisuals)
            {
                KeyboardWandererEntityVisualState visual = pair.Value;
                if (visual == null || visual.Root == null || !visual.IsPlayer)
                    continue;
                Vector3 delta = worldTarget - visual.Root.transform.position;
                visual.Facing = Mathf.Abs(delta.x) >= Mathf.Abs(delta.y)
                    ? new Vector2(delta.x >= 0f ? 1f : -1f, 0f)
                    : new Vector2(0f, delta.y >= 0f ? 1f : -1f);
                return;
            }
        }

        private bool TryGetPlayerWorldPosition(out Vector3 position)
        {
            foreach (KeyValuePair<Guid, KeyboardWandererEntityVisualState> pair in _entityVisuals)
            {
                KeyboardWandererEntityVisualState visual = pair.Value;
                if (visual != null && visual.Root != null && visual.IsPlayer)
                {
                    position = visual.Root.transform.position;
                    return true;
                }
            }
            position = Vector3.zero;
            return false;
        }

        private Vector3 PlayerWorldPosition(RunView view)
        {
            if (TryGetPlayerWorldPosition(out Vector3 live))
                return live;
            return view != null ? WorldPosition(MapOrigin(view), view.PlayerPosition) : Vector3.zero;
        }

        private Vector3 EntityWorldPosition(Guid entityId, RunView view)
        {
            if (_entityVisuals.TryGetValue(entityId, out KeyboardWandererEntityVisualState visual) &&
                visual?.Root != null)
                return visual.Root.transform.position;
            if (view != null)
            {
                Vector2 origin = MapOrigin(view);
                for (int i = 0; i < view.Entities.Count; i++)
                    if (view.Entities[i].EntityId == entityId)
                        return WorldPosition(origin, view.Entities[i].Position);
            }
            return PlayerWorldPosition(view);
        }

        private static AbilityKind AbilityFromSkillId(string skillId)
        {
            switch ((skillId ?? string.Empty).Trim().ToUpperInvariant())
            {
                case "COPY": return AbilityKind.Copy;
                case "DELETE": return AbilityKind.Delete;
                case "CONNECT": return AbilityKind.Connect;
                case "RESTORE": return AbilityKind.Restore;
                case "UNDO": return AbilityKind.Undo;
                case "SEARCH": return AbilityKind.Search;
                case "SELECT_ALL": return AbilityKind.SelectAll;
                case "INTERACT": return AbilityKind.Interact;
                default: return AbilityKind.Move;
            }
        }

        /// <summary>응답 출처를 구분하지 않고 정규화된 결과를 현재 화면 상태에 반영한다.</summary>
        private void ApplyTurnPresentation(TurnPresentationResult presentation, bool reopenDialogue = true)
        {
            if (presentation == null)
            {
                Debug.LogWarning("[KW.Narrative] event=ApplyTurnPresentation result=ignored reason=null-presentation");
                return;
            }
            Debug.Log("[KW.Narrative] event=ApplyTurnPresentation result=accepted outcome=" +
                      presentation.Outcome + " fallback=" + presentation.NarrativeFallbackUsed +
                      " model=" + presentation.NarrativeModel + " chars=" +
                      (presentation.Narrative?.Length ?? 0) + " dialoguePages=" +
                      (presentation.Dialogue?.Length ?? 0));
            _lastD20 = presentation.D20;
            _lastModifier = presentation.Modifier;
            _lastDifficulty = presentation.Difficulty;
            _lastMechanicalScore = presentation.MechanicalScore;
            _lastActionContext = presentation.ActionContext;
            _lastModifierBreakdown = presentation.ModifierBreakdown;
            _lastOutcome = presentation.Outcome;
            _lastAttempt = presentation.Attempt;
            _lastExplanation = presentation.Explanation;
            _lastNarrative = presentation.Narrative;
            _lastStateChanges = presentation.StateChanges;
            _lastDialogue = presentation.Dialogue;
            if (!string.IsNullOrWhiteSpace(presentation.DialogueSpeaker))
            {
                Sprite speakerSprite = SpriteForSpeakerName(presentation.DialogueSpeaker);
                for (int i = 0; i < _lastDialogue.Length; i++)
                {
                    if (string.IsNullOrWhiteSpace(_lastDialogue[i]))
                        continue;
                    string key = _lastDialogue[i].Trim();
                    _dialogueSpeakerNames[key] = presentation.DialogueSpeaker;
                    _dialogueSpeakerSprites[key] = speakerSprite;
                }
            }
            _lastStorySequence = presentation.StorySequence;
            _lastNextInterventionReason = presentation.NextInterventionReason;
            _lastChoiceSetId = presentation.ChoiceSetId;
            _lastNarrativeChoices = presentation.NarrativeChoices ?? Array.Empty<NarrativeChoiceOption>();
            _lastPresentationContinuesWithMovement = presentation.ContinuesWithMovement;
            if (!string.IsNullOrWhiteSpace(_lastChoiceSetId) && _lastNarrativeChoices.Length >= 2 &&
                string.IsNullOrWhiteSpace(_lastNextInterventionReason))
                _lastNextInterventionReason = "이야기가 잠시 멈췄다. 어떤 말이나 행동으로 다음 장면을 이어 갈까?";
            _lastSuggestedSkillIds = presentation.SuggestedSkillIds ?? Array.Empty<string>();
            _playedStoryActionIds.Clear();
            _lastNarrativeFallbackUsed = presentation.NarrativeFallbackUsed;
            _lastNarrativeModel = presentation.NarrativeModel;
            _playerActionUntil = Time.unscaledTime + presentation.ActionDuration;
            if (!string.IsNullOrWhiteSpace(presentation.ElementalEffectId))
                BeginTurnImpactPresentation(presentation.ElementalEffectId);
            if (reopenDialogue)
            {
                ReopenDialogueForNewTurnContent();
            }
            else
            {
                _naturalLanguageComposeMode = false;
                _reopenDialogueAfterWalk = false;
                _suppressDialogueReopenAfterWalk = true;
                if (string.IsNullOrWhiteSpace(_lastNextInterventionReason))
                    _lastNextInterventionReason = "이동을 계속하거나 주변 대상을 조사하세요.";
                string[] movementPages = BuildDialoguePages(_lastNarrative);
                _dialoguePresenter.ShowLast(movementPages.Length);
                _dialoguePresenter.Dismiss();
                _sceneUi?.SetStoryVisible(false);
            }
            for (int i = 0; i < presentation.LogEntries.Length; i++)
                AddLog(presentation.LogEntries[i]);
        }

        private void RecoverPendingChoiceSet(GameApiClient.NextInterventionSnapshot pending, bool overwrite)
        {
            if (!ServerTurnPresentationAdapter.IsValidSealedChoiceSet(pending)) return;
            if (!overwrite && HasSealedNarrativeChoiceSet()) return;
            NarrativeChoiceOption[] choices = ServerTurnPresentationAdapter.BuildNarrativeChoices(
                pending, pending.suggestedSkillIds, !_encounterMoveRequired);
            bool mandatoryOpeningAttack = choices.Length == 1 && choices[0] != null &&
                                          choices[0].IsSkill &&
                                          string.Equals(choices[0].ChoiceId, "opening.attack", StringComparison.Ordinal) &&
                                          string.Equals(choices[0].SkillId, "DELETE", StringComparison.OrdinalIgnoreCase);
            if (choices.Length < 2 && !mandatoryOpeningAttack) return;
            _lastChoiceSetId = pending.choiceSetId.Trim();
            _lastNarrativeChoices = choices;
            if (!string.IsNullOrWhiteSpace(pending.reason))
                _lastNextInterventionReason = pending.reason.Trim();
            else if (string.IsNullOrWhiteSpace(_lastNextInterventionReason))
                _lastNextInterventionReason = "이야기가 잠시 멈췄다. 어떤 말이나 행동으로 다음 장면을 이어 갈까?";
            _lastSuggestedSkillIds = pending.suggestedSkillIds ?? Array.Empty<string>();
            Debug.Log("[KW.Choice] event=Recovered choiceSetId=" + _lastChoiceSetId +
                      " choices=" + _lastNarrativeChoices.Length + " turn=" + (_serverRun?.currentTurn ?? 0));
        }

        private IEnumerator PlayElementalAttackEffect(string effectId)
        {
            Sprite[] frames = _visualAssetLibrary?.GetElementalFrames(effectId);
            if (frames == null || frames.Length == 0)
            {
                _hasPendingImpactTargetPosition = false;
                yield break;
            }
            RunView view = _service?.CurrentView;
            if (view == null || !TryGetPlayerVisual(view, out KeyboardWandererEntityVisualState player))
            {
                _hasPendingImpactTargetPosition = false;
                yield break;
            }

            Vector3 position = player.Root.transform.position + new Vector3(player.Facing.x, player.Facing.y, 0f) * 0.75f;
            string positionSource = "player-facing-fallback";
            if (_hasPendingImpactTargetPosition)
            {
                position = _pendingImpactTargetPosition;
                positionSource = "captured-target";
            }
            else if (_selectionController?.SelectedTarget is Guid targetId &&
                _entityVisuals.TryGetValue(targetId, out KeyboardWandererEntityVisualState target) && target?.Root != null)
            {
                position = target.Root.transform.position;
                positionSource = "live-target";
            }
            if (_combatEffectOverlay == null)
            {
                _combatEffectOverlay = GetComponent<KeyboardWandererCombatEffectOverlay>();
                if (_combatEffectOverlay == null)
                    _combatEffectOverlay = gameObject.AddComponent<KeyboardWandererCombatEffectOverlay>();
                _combatEffectOverlay.Configure(authoredCamera != null ? authoredCamera : Camera.main);
            }
            yield return _combatEffectOverlay.PlayAndWait(frames, position + Vector3.up * 0.12f,
                _visualAssetLibrary.GetElementalFrameRate(effectId));
            Debug.Log("[KW.Combat] event=EffectTarget source=" + positionSource + " position=" + position);
            _hasPendingImpactTargetPosition = false;
        }

        private void CapturePendingImpactTarget(Guid? targetId)
        {
            if (!targetId.HasValue)
            {
                _hasPendingImpactTargetPosition = false;
                return;
            }
            CapturePendingImpactTarget(targetId.Value.ToString());
        }

        private void CapturePendingImpactTarget(string targetId)
        {
            KeyboardWandererEntityVisualState target = null;
            _hasPendingImpactTargetPosition = Guid.TryParse(targetId, out Guid parsed) &&
                                              _entityVisuals.TryGetValue(parsed, out target) &&
                                              target?.Root != null;
            if (_hasPendingImpactTargetPosition)
                _pendingImpactTargetPosition = target.Root.transform.position;
        }

        /// <summary>
        /// Keeps the authoritative impact, target reaction, and elemental VFX on an
        /// unobstructed world field. Narrative pages stay logically selected and return
        /// immediately after the short presentation, so no result or choice is lost.
        /// </summary>
        private void BeginTurnImpactPresentation(string effectId)
        {
            if (_turnImpactPresentationCoroutine != null)
                StopCoroutine(_turnImpactPresentationCoroutine);
            _turnImpactPresentationPlaying = true;
            // Do this synchronously. Waiting for the next presentation refresh leaves
            // one rendered frame where the old portrait/choice modal covers the hit.
            _sceneUi?.SetStoryVisible(false);
            _sceneUi?.PresentDialogueChoices(false, Array.Empty<NarrativeChoiceOption>(), false);
            _pendingPresentationChanges |= PresentationChange.Dialogue;
            _turnImpactPresentationCoroutine = StartCoroutine(PlayTurnImpactPresentation(effectId));
            Debug.Log("[KW.Combat] event=ImpactPresentation result=started effect=" + effectId);
        }

        private IEnumerator PlayTurnImpactPresentation(string effectId)
        {
            const float sceneTimeoutSeconds = 3f;
            try
            {
                yield return PlayElementalAttackEffect(effectId);
                // PrepareStoryWorldActions queues the authoritative ATTACK/DAMAGE/SPAWN
                // sequence later in the same submit frame. Give it one frame to claim
                // playback before deciding that the presentation is complete.
                yield return null;
                float deadline = Time.unscaledTime + sceneTimeoutSeconds;
                while (_sceneSequencePlayer != null && _sceneSequencePlayer.IsPlaying &&
                       Time.unscaledTime < deadline)
                    yield return null;
            }
            finally
            {
                _turnImpactPresentationPlaying = false;
                _turnImpactPresentationCoroutine = null;
                _pendingPresentationChanges |= PresentationChange.Dialogue;
                Debug.Log("[KW.Combat] event=ImpactPresentation result=completed effect=" + effectId);
            }
        }

        private bool WorldPresentationPlaying =>
            _storyWorldActionPlaying || _turnImpactPresentationPlaying ||
            (_sceneSequencePlayer != null && _sceneSequencePlayer.IsPlaying);

        /// <summary>
        /// A committed result or a later AI narration must never remain hidden just because the
        /// previous dialogue was dismissed. Walking temporarily owns the screen, so defer the
        /// reopen until the path animation completes in that case.
        /// </summary>
        private void ReopenDialogueForNewTurnContent()
        {
            if (_playerWalking)
            {
                _reopenDialogueAfterWalk = true;
                Debug.Log("[KW.Narrative] event=DialogueDisplay result=deferred reason=player-walking");
                return;
            }

            _dialoguePresenter.Show();
            _sceneUi?.SetStoryVisible(true);
            _pendingPresentationChanges |= PresentationChange.Dialogue;
            Debug.Log("[KW.Narrative] event=DialogueDisplay result=requested sceneUi=" +
                      (_sceneUi == null ? "null" : "ready") + " tutorialActive=" +
                      _tutorialPresenter.IsActive + " screen=" + _screenMode);
        }

        private void SyncEncounterStateFromServer()
        {
            GameApiClient.ActiveEncounterSnapshot encounter = _serverRun?.activeEncounter;
            bool open = IsOpenServerEncounter(encounter);
            _encounterMoveRequired = open;
            if (!open)
            {
                _encounterStagingCoord = null;
                _encounterReason = null;
                return;
            }
            _encounterReason = encounter.reason;
            GameApiClient.PositionSnapshot staging = encounter.stagingPosition ?? encounter.position;
            _encounterStagingCoord = staging == null ? (GridCoord?)null : new GridCoord(staging.x, staging.y);
            if (_selectionController != null && _selectionController.Ability == AbilityKind.Move &&
                encounter.suggestedSkillIds != null)
            {
                for (int i = 0; i < encounter.suggestedSkillIds.Length; i++)
                {
                    if (!TryAbilityForSkillId(encounter.suggestedSkillIds[i], out AbilityKind suggested)) continue;
                    _selectionController.ChangeAbility(suggested, _lastRestorableTarget, _lastRestorableCoord);
                    break;
                }
            }
            RefreshFlowPhase();
        }

        private static bool IsOpenServerEncounter(GameApiClient.ActiveEncounterSnapshot encounter)
        {
            return encounter != null &&
                   string.Equals(encounter.status, "active", StringComparison.OrdinalIgnoreCase) &&
                   !string.IsNullOrWhiteSpace(encounter.id);
        }

        private void SyncLocalEncounterState(RunView view)
        {
            if (_serverOnline || view == null)
                return;
            _encounterMoveRequired = view.HasActiveEncounter;
            _encounterReason = view.ActiveEncounterReason;
            _encounterStagingCoord = view.HasActiveEncounter ? view.EncounterStagingPosition : (GridCoord?)null;
        }

        private void SyncRestorableCandidateFromServer()
        {
            if (!_serverOnline || _serverRun == null || _serverRun.restoreCandidates == null)
                return;
            if (_serverRun.restoreCandidates.Length == 0)
            {
                ClearRestorableTargetCache();
                return;
            }

            GameApiClient.RestoreCandidateSnapshot latest = null;
            Guid latestId = Guid.Empty;
            int latestTurn = int.MinValue;
            for (int i = 0; i < _serverRun.restoreCandidates.Length; i++)
            {
                GameApiClient.RestoreCandidateSnapshot candidate = _serverRun.restoreCandidates[i];
                if (candidate == null || (candidate.expiresTurn > 0 && _serverRun.currentTurn > candidate.expiresTurn))
                    continue;
                string idText = FirstNonEmpty(candidate.entityId, candidate.targetEntityId, candidate.id);
                if (!Guid.TryParse(idText, out Guid id))
                    continue;
                int sourceTurn = Math.Max(Math.Max(candidate.sourceTurn, candidate.capturedTurn),
                    Math.Max(candidate.recordedTurn, candidate.turnNo));
                if (latest != null && sourceTurn < latestTurn)
                    continue;
                latest = candidate;
                latestId = id;
                latestTurn = sourceTurn;
            }

            if (latest == null)
            {
                ClearRestorableTargetCache();
                return;
            }

            GameApiClient.EntitySnapshot activeEntity = FindServerEntity(_serverRun, latestId.ToString());
            GameApiClient.PositionSnapshot position = latest.position ?? latest.lastKnownPosition ?? activeEntity?.position;
            _lastRestorableTarget = latestId;
            _lastRestorableName = FirstNonEmpty(latest.entityName, latest.displayName, latest.name, activeEntity?.name);
            if (string.IsNullOrWhiteSpace(_lastRestorableName))
                _lastRestorableName = "최근 변경된 대상";
            _lastRestorableCoord = position == null ? (GridCoord?)null : new GridCoord(position.x, position.y);

            if (_selectionController.Ability == AbilityKind.Restore)
            {
                _selectionController.SelectPrimary(_lastRestorableTarget, _lastRestorableCoord);
                if (_service != null)
                    UpdateSelectionVisual(_service.CurrentView);
            }
        }

        private void ClearRestorableTargetCache()
        {
            _lastRestorableTarget = null;
            _lastRestorableCoord = null;
            _lastRestorableName = null;
            if (_selectionController.Ability != AbilityKind.Restore)
                return;
            _selectionController.ClearRestoreSelection();
            if (_service != null)
                UpdateSelectionVisual(_service.CurrentView);
        }

        private static string FirstNonEmpty(params string[] values)
        {
            if (values == null)
                return null;
            for (int i = 0; i < values.Length; i++)
                if (!string.IsNullOrWhiteSpace(values[i]))
                    return values[i].Trim();
            return null;
        }

        private void CaptureRestorableTarget(GameApiClient.TurnSnapshot turn, GameApiClient.RunSnapshot beforeRun)
        {
            GameApiClient.EventSnapshot[] events = turn?.events ?? turn?.stateDelta?.events;
            if (events == null)
                return;
            for (int i = 0; i < events.Length; i++)
            {
                GameApiClient.EventSnapshot eventItem = events[i];
                if (eventItem == null || string.IsNullOrWhiteSpace(eventItem.entityId))
                    continue;
                string type = (eventItem.type ?? string.Empty).ToLowerInvariant();
                if ((type == "entity_restored" || type == "entity_state_restored") &&
                    _lastRestorableTarget.HasValue && string.Equals(_lastRestorableTarget.Value.ToString(), eventItem.entityId, StringComparison.OrdinalIgnoreCase))
                {
                    _lastRestorableTarget = null;
                    _lastRestorableCoord = null;
                    _lastRestorableName = null;
                    continue;
                }
                if (type != "entity_removed" && type != "health_changed")
                    continue;
                if (!Guid.TryParse(eventItem.entityId, out Guid id))
                    continue;
                GameApiClient.EntitySnapshot entity = FindServerEntity(beforeRun, eventItem.entityId);
                _lastRestorableTarget = id;
                _lastRestorableName = entity?.name ?? "최근 변경된 대상";
                _lastRestorableCoord = entity?.position == null
                    ? (GridCoord?)null
                    : new GridCoord(entity.position.x, entity.position.y);
            }
        }

        private void CaptureLocalRestorableTarget(TurnResponse response, RunView beforeView)
        {
            for (int i = 0; i < response.Events.Count; i++)
            {
                string eventCode = response.Events[i] ?? string.Empty;
                if (!eventCode.StartsWith("ENTITY_REMOVED:", StringComparison.Ordinal) &&
                    !eventCode.StartsWith("ENTITY_DAMAGED:", StringComparison.Ordinal) &&
                    !eventCode.StartsWith("PLAYER_DAMAGED:", StringComparison.Ordinal))
                {
                    if (eventCode.StartsWith("ENTITY_RESTORED:", StringComparison.Ordinal))
                    {
                        _lastRestorableTarget = null;
                        _lastRestorableCoord = null;
                        _lastRestorableName = null;
                    }
                    continue;
                }

                string[] parts = eventCode.Split(':');
                Guid id;
                if (eventCode.StartsWith("PLAYER_DAMAGED:", StringComparison.Ordinal))
                    id = beforeView.PlayerEntityId;
                else if (parts.Length < 2 || !Guid.TryParse(parts[1], out id))
                    continue;
                for (int entityIndex = 0; entityIndex < beforeView.Entities.Count; entityIndex++)
                {
                    EntityView entity = beforeView.Entities[entityIndex];
                    if (entity.EntityId != id) continue;
                    _lastRestorableTarget = id;
                    _lastRestorableName = entity.DisplayName;
                    _lastRestorableCoord = entity.Position;
                    break;
                }
            }
        }

        private static GameApiClient.EntitySnapshot FindServerEntity(GameApiClient.RunSnapshot run, string entityId)
        {
            if (run?.entities == null || string.IsNullOrWhiteSpace(entityId))
                return null;
            for (int i = 0; i < run.entities.Length; i++)
                if (run.entities[i] != null && string.Equals(run.entities[i].id, entityId, StringComparison.OrdinalIgnoreCase))
                    return run.entities[i];
            return null;
        }

        private static string EncounterReasonLabel(string reason)
        {
            switch ((reason ?? string.Empty).ToLowerInvariant())
            {
                case "hostile_proximity": return "적대 개체";
                case "hazardous_tile": return "위험 지형";
                case "unknown_off_route": return "미개척 경로";
                case "unsafe_or_blocked_route": return "차단된 경로";
                default: return "예상치 못한 사건";
            }
        }

        private static string IntentAlignmentLabel(int value)
        {
            if (value <= 0) return "불일치";
            if (value == 1) return "일반";
            if (value == 2) return "구체적";
            return "매우 구체적";
        }

        private static string IntentAlignmentLabel(float value)
        {
            value = Mathf.Clamp01(value);
            string label = value < 0.2f ? "매우 낮음" : value < 0.4f ? "낮음" : value < 0.6f ? "중간" : value < 0.8f ? "높음" : "매우 높음";
            return label + " " + Mathf.RoundToInt(value * 100f) + "%";
        }

        private string LayoutHash(RunView view)
            => _serverOnline && !string.IsNullOrWhiteSpace(_serverRun?.world?.layoutHash) ? _serverRun.world.layoutHash : view.Region.LayoutHash;

        private static string ShortHash(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "no-hash";
            return value.Substring(0, Math.Min(10, value.Length));
        }

        private static string Signed(int value) => value >= 0 ? "+" + value : value.ToString();

        private static string EndingTitle(string code)
        {
            if (string.IsNullOrWhiteSpace(code)) return "ROOT_SYSTEM 진입 전";
            switch (code.ToUpperInvariant())
            {
                case "ENDING_REWEAVE_TOGETHER": return "함께 다시 잇기";
                case "ENDING_OPEN_FRONTIER": return "열린 변경";
                case "ENDING_KEEP_THE_PROMISE": return "약속을 지키는 이";
                case "ENDING_CUT_THE_CYCLE": return "되풀이 끊기";
                case "ENDING_PRESERVE_THE_SCARS": return "상처를 기억하기";
                case "ENDING_WALK_BETWEEN_WORLDS": return "세계 사이를 걷기";
                case "ENDING_EMERGENCY_WITHDRAWAL": return "긴급 이탈";
                default: return code.Replace('_', ' ').Replace('-', ' ');
            }
        }

        private static string EndingDescription(string code)
        {
            if (string.IsNullOrWhiteSpace(code))
                return "관리자 권한 3단계와 내부 오류 단서를 모아 ROOT_SYSTEM에 진입하세요.";
            switch (code.ToUpperInvariant())
            {
                case "ENDING_REWEAVE_TOGETHER": return "관계와 세계의 상처를 함께 엮어 새 약속을 만듭니다.";
                case "ENDING_OPEN_FRONTIER": return "코드리아가 위험과 선택권을 함께 품도록 경계를 엽니다.";
                case "ENDING_KEEP_THE_PROMISE": return "주민과 맺은 약속의 책임을 받아들이고 수호자로 남습니다.";
                case "ENDING_CUT_THE_CYCLE": return "오래된 통제 순환을 끊고 다음 가능성을 엽니다.";
                case "ENDING_PRESERVE_THE_SCARS": return "완전한 복구 대신 코드리아의 상처와 증언을 보존합니다.";
                case "ENDING_WALK_BETWEEN_WORLDS": return "두 세계를 잇는 통로와 책임을 함께 선택합니다.";
                case "ENDING_EMERGENCY_WITHDRAWAL": return "생존자를 우선해 위험한 수렴점에서 이탈합니다.";
                default: return "서버가 확정한 공간 배치와 지표가 이 결말을 선택했습니다.";
            }
        }


        private static Color Hex(string rgb)
        {
            if (!ColorUtility.TryParseHtmlString("#" + rgb, out Color color))
                return Color.white;
            // This project renders in Linear color space; convert authored fallback sprite colors.
            return color.linear;
        }
    }
}
