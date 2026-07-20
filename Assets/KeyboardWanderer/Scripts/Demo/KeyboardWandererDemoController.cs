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
        private const int TerrainSortingOrder = -1000;
        private const string TutorialCompletedKey = "keyboard-wanderer.tutorial-v1-complete";

        private enum ScreenMode
        {
            Title,
            Playing,
            Settings
        }

        private readonly Dictionary<Guid, KeyboardWandererEntityVisualState> _entityVisuals = new Dictionary<Guid, KeyboardWandererEntityVisualState>();
        private readonly List<Tile> _runtimeTiles = new List<Tile>();
        private readonly List<string> _sessionLog = new List<string>();

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
        private GameApiClient.CampaignSnapshot _serverCampaign;
        private GameApiClient.RunSnapshot _serverRun;
        private float _nextAmbientWanderAt;
        private ScreenMode _screenMode = ScreenMode.Title;
        private ScreenMode _settingsReturn = ScreenMode.Title;
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
        private bool _lastNarrativeFallbackUsed = true;
        private string _lastNarrativeModel = "deterministic";
        private bool _gmPending;
        private bool _serverOnline;
        private bool _serverPending;
        private bool _ambientWanderPending;
        private bool _encounterMoveRequired;
        private GridCoord? _encounterStagingCoord;
        private string _encounterReason;
        private string _serverStatus = "권위 서버 확인 전";
        private bool _showPause;
        private bool _playerWalking;
        private bool _reopenDialogueAfterWalk;
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
        private float _playerActionUntil;

        private float PlayerWalkSpeed => authoringSettings != null ? authoringSettings.PlayerWalkSpeed : 4.2f;
        private float MusicVolume => _settingsController != null ? _settingsController.MusicVolume : 0.65f;
        private float SfxVolume => _settingsController != null ? _settingsController.SfxVolume : 0.8f;
        private bool GmEnabled => _settingsController == null || _settingsController.GmEnabled;
        private bool TurnPending => _serverPending || _ambientWanderPending || (_turnCoordinator != null && _turnCoordinator.IsPending);
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
            if (_screenMode != ScreenMode.Playing || _service == null)
                return;

            if (!_playerWalking && !TurnPending && Time.unscaledTime >= _nextAmbientWanderAt)
            {
                _nextAmbientWanderAt = Time.unscaledTime + 0.5f;
                GetCameraActiveTileBounds(_service.CurrentView, 2, out GridCoord activeMin, out GridCoord activeMax);
                if (_serverOnline && _serverRun != null && _gameApi != null)
                    StartCoroutine(AdvanceServerAmbientWander(activeMin, activeMax));
                else if (_service.TryAdvanceAmbientWander(2, activeMin, activeMax))
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
                _dialoguePresenter.Signature, _showPause, TurnPending || _gmPending, _playerWalking);
            _runCoordinator.Publish(state, requested);
        }

        private void UpdateAuthoredUi()
        {
            if (_sceneUi == null || !_sceneUi.IsReady)
                return;

            PresentationChange changes = _pendingPresentationChanges;
            _pendingPresentationChanges = PresentationChange.None;

            bool ended = _screenMode == ScreenMode.Playing && _service != null && !RunIsPlaying(_service.CurrentView);
            _hudPresenter.PresentScreen(_screenMode == ScreenMode.Title, _screenMode == ScreenMode.Settings,
                _screenMode == ScreenMode.Playing, _showPause, ended, MusicVolume, SfxVolume, GmEnabled);
            long nextSeed = _runSessionController != null ? _runSessionController.NextSeed : 20260718L;
            _sceneUi.PresentTitle(CampaignCatalog.CampaignTitle,
                "관리자 키보드로 붕괴한 코드리아를 복구하는 탐험 RPG",
                "NEXT SEED  " + nextSeed,
                "넙죽이가 되어 여섯 지역을 탐험하세요.\n" +
                "대상을 조사하고 적과 싸워 관리자 권한 3개를 되찾으면 루트 시스템의 결말이 열립니다.",
                _serverStatus + " · Ninja Adventure CC0", _playerSprite, !_serverPending,
                !_serverPending && _runSessionController != null && _runSessionController.HasContinue);

            if (_service == null)
                return;

            RunView view = _service.CurrentView;
            if ((changes & PresentationChange.Minimap) != 0)
                UpdateMinimap(view);
            string narrative = _lastOutcome == "READY" || _lastOutcome == "RESTORED"
                ? CampaignPremise(view)
                : ShortNarrative(_lastNarrative);
            if (_tutorialPresenter.IsActive)
            {
                _sceneUi.PresentTutorial(_tutorialPresenter.Page, StoryBeatObjective(view));
            }
            else
            {
                string[] dialoguePages = BuildDialoguePages(narrative);
                string dialogueSignature = string.Join("\u001f", dialoguePages);
                if (_dialoguePresenter.Synchronize(dialogueSignature, _playerWalking))
                {
                    if (_playerWalking)
                        _reopenDialogueAfterWalk = true;
                }
                int dialoguePage = Mathf.Clamp(_dialoguePresenter.Page, 0, Mathf.Max(0, dialoguePages.Length - 1));
                bool showingResult = HasActionResultPage() && dialoguePage == 0;
                int narrationPage = HasActionResultPage() ? 1 : 0;
                bool showingNarration = dialoguePage == narrationPage;
                bool hasNextDialogue = dialoguePage < dialoguePages.Length - 1;
                _sceneUi.PresentDialogue(!_dialoguePresenter.IsDismissed,
                    showingResult ? "행동 결과" : showingNarration ? NarrativeSourceLabel() : "코드리아 주민",
                    dialoguePages[dialoguePage], hasNextDialogue ? "다음 ▶" : "대화 끝", true);
            }
            _sceneUi.PresentHud(CurrentAreaName(view) + " · " + CurrentBiomeLabel(view), StoryBeat(view),
                ObjectiveHudText(view),
                _playerWalking ? "선택한 경로를 따라 이동하고 있습니다." : ActionGuidanceText(view));
            bool selectionReady = CanSubmitCurrentSelection();
            string selectionHeading = AbilityPlayerLabel(_selectionController.Ability) + " · " +
                                      (selectionReady ? "사용 가능" : "준비 필요");
            string selectionDetail = SelectionStatusDetail(view);
            EntityView selectedEntity = SelectedEntity(view, _selectionController.SelectedTarget);
            Sprite selectedTargetSprite = selectedEntity == null ? null : SpriteForEntity(selectedEntity);
            if (selectedTargetSprite == null && TryGetServerEntity(_selectionController.SelectedTarget, out GameApiClient.EntitySnapshot serverSelected))
                selectedTargetSprite = SpriteForServerEntity(serverSelected,
                    string.Equals(serverSelected.id, _serverRun.playerEntityId, StringComparison.OrdinalIgnoreCase));
            _sceneUi.PresentSelection(_selectionController.Ability, selectedTargetSprite, selectionReady,
                selectionHeading, selectionDetail);

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
            string confirmLabel = _actionConfirmation.IsArmed(Time.unscaledTime)
                ? "다시 눌러 확인"
                : _selectionController.Ability == AbilityKind.Interact ? "상호작용" : "실행";
            _sceneUi.PresentConfirm(confirmLabel,
                RunIsPlaying(view) && !_showPause && !TurnPending && !_playerWalking && CanSubmitCurrentSelection());
            _sceneUi.PresentEnding("코드리아의 결말",
                EndingTitle(EndingCode(view)) + "\n\n" + EndingDescription(EndingCode(view)) +
                "\n\n당신의 선택이 코드리아에 남긴 결말입니다.");
        }

        public void UiStartNewRun() => StartNewRun();
        public void UiContinueRun() => ContinueRun();
        public void UiCyclePoi(int direction) => CyclePoi(direction);
        public void UiSetAbility(AbilityKind ability)
        {
            if (ability == AbilityKind.Copy && _selectionController.Ability == AbilityKind.Copy && _selectionController.CopySourceCaptured &&
                _selectionController.SelectedCoord.HasValue && CanSubmitCurrentSelection())
            {
                SubmitImmediately();
                return;
            }
            SetAbility(ability);
            if (CanSubmitCurrentSelection()) SubmitImmediately();
        }
        public void UiSubmit() => Submit();
        public void UiAdvanceDialogue()
        {
            if (_tutorialPresenter.IsActive)
            {
                if (_tutorialPresenter.Advance(Mathf.Max(1, _sceneUi == null ? 0 : _sceneUi.TutorialPageCount)))
                {
                    _dialoguePresenter.Reset();
                    PlayerPrefs.SetInt(TutorialCompletedKey, 1);
                    PlayerPrefs.Save();
                }
                PlaySfx(AssetClip("UiAcceptSound"));
                PublishPresentationState(PresentationChange.Dialogue);
                return;
            }
            string[] pages = BuildDialoguePages(_service == null ? _lastNarrative :
                (_lastOutcome == "READY" || _lastOutcome == "RESTORED" ? CampaignPremise(_service.CurrentView) : ShortNarrative(_lastNarrative)));
            if (!_dialoguePresenter.Advance(pages.Length))
            {
                _sceneUi?.SetStoryVisible(false);
            }
            PlaySfx(AssetClip("UiAcceptSound"));
            PublishPresentationState(PresentationChange.Dialogue);
        }
        public void UiResume()
        {
            _showPause = false;
            PublishPresentationState(PresentationChange.Screen | PresentationChange.Hud);
        }
        public void UiShowTitle() => ShowTitle();

        private void SetAbilityButton(AbilityKind ability)
        {
            _sceneUi.SetAbilityState(ability, CanHandleGameplayInput(), _selectionController.Ability == ability);
        }

        private string[] BuildDialoguePages(string narrative)
        {
            var pages = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            bool hasStoryDialogue = _lastDialogue != null && _lastDialogue.Length > 0 &&
                                    _selectionController.Ability == AbilityKind.Search;
            if (hasStoryDialogue)
            {
                for (int i = 0; i < _lastDialogue.Length; i++)
                    if (!string.IsNullOrWhiteSpace(_lastDialogue[i]) && seen.Add(_lastDialogue[i].Trim()))
                        pages.Add(_lastDialogue[i].Trim());
            }
            if (HasActionResultPage() && !hasStoryDialogue)
            {
                string result = (_selectionController.Ability == AbilityKind.Interact ? "INTERACT" : _selectionController.Ability.ToString().ToUpperInvariant()) +
                                " · " + _lastOutcome;
                if (_lastD20 > 0)
                    result += " · D20 " + _lastD20 + " " + Signed(_lastModifier) + " vs " + _lastDifficulty;
                if (!string.IsNullOrWhiteSpace(_lastAttempt)) result += "\n" + _lastAttempt.Trim();
                if (!string.IsNullOrWhiteSpace(_lastStateChanges)) result += "\n변화 · " + _lastStateChanges.Trim();
                if (!string.IsNullOrWhiteSpace(_lastExplanation)) result += "\n" + _lastExplanation.Trim();
                pages.Add(result);
                seen.Add(result);
            }
            if (!hasStoryDialogue && !string.IsNullOrWhiteSpace(narrative) && seen.Add(narrative.Trim()))
                pages.Add(narrative.Trim());
            if (pages.Count == 0) pages.Add("코드리아의 다음 이야기가 시작되기를 기다리고 있습니다.");
            return pages.ToArray();
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
                case AbilityKind.Connect: return "이어 주고 싶은 두 대상을 지도에서 차례로 고르세요.";
                case AbilityKind.Delete: return "삭제할 적 또는 시스템 노드를 클릭하면 즉시 공격합니다.";
                case AbilityKind.Restore: return "복구 가능한 대상이 생기면 대상이 자동 선택됩니다.";
                case AbilityKind.Interact: return "가까운 대상을 클릭하면 즉시 상호작용합니다.";
                case AbilityKind.Undo: return "Ctrl Z로 최근 의미 턴 2회의 상태를 시간 역행합니다.";
                case AbilityKind.Search: return "Ctrl F를 누른 뒤 조사할 대상을 클릭하면 즉시 조사합니다.";
                case AbilityKind.SelectAll: return "Ctrl A로 주변 4칸의 모든 적을 광범위 공격합니다.";
                case AbilityKind.Copy: return _selectionController.CopySourceCaptured
                    ? "Ctrl V 상태입니다. 복제본을 놓을 빈 타일을 고른 뒤 실행하세요."
                    : "Ctrl C 상태입니다. 복제할 대상을 먼저 고르세요.";
                default: return "이 선택을 적용할 대상을 지도에서 고르세요.";
            }
        }

        public void UiOpenSettings()
        {
            _settingsReturn = _screenMode;
            _screenMode = ScreenMode.Settings;
            PublishPresentationState(PresentationChange.Screen);
        }

        public void UiOpenSettingsFromPause()
        {
            _showPause = false;
            _settingsReturn = ScreenMode.Playing;
            _screenMode = ScreenMode.Settings;
            PublishPresentationState(PresentationChange.Screen);
        }

        public void UiCloseSettings()
        {
            _screenMode = _settingsReturn;
            if (_screenMode == ScreenMode.Playing)
                _cameraController?.SetEnabled(true);
            PublishPresentationState(PresentationChange.Screen);
        }

        public void UiDeleteSave()
        {
            _runSessionController?.DeleteSave();
        }

        public void UiSetMusicVolume(float value)
        {
            _settingsController?.SetMusicVolume(value);
        }

        public void UiSetSfxVolume(float value)
        {
            _settingsController?.SetSfxVolume(value);
        }

        public void UiSetGmEnabled(bool value)
        {
            _settingsController?.SetGmEnabled(value);
        }

        private bool CanSubmitCurrentSelection()
        {
            if (_selectionController.Ability == AbilityKind.Move) return _selectionController.SelectedCoord.HasValue;
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

        private string SelectionStatusDetail(RunView view)
        {
            RunPresentationModel run = PresentationModel(view);
            int cost = _abilityAvailability == null ? 0 :
                _abilityAvailability.FocusCost(_selectionController.Ability);
            if (run.Focus < cost)
                return "사용 불가 · 포커스 " + cost + " 필요 (현재 " + run.Focus + ")";
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
                case "RESTORE": return "Ctrl R 복구";
                case "SEARCH": return "Ctrl F 조사";
                case "CONNECT": return "Ctrl K 연결";
                case "COPY": return "Ctrl C 복제";
                case "DELETE": return "Delete 공격";
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

        private static EntityView SelectedEntity(RunView view, Guid? entityId)
        {
            if (view == null || !entityId.HasValue) return null;
            for (int i = 0; i < view.Entities.Count; i++)
                if (view.Entities[i].EntityId == entityId.Value) return view.Entities[i];
            return null;
        }

        private void ConfigureInput()
        {
            DisconnectInput();
            _inputRouter = authoredInputRouter;
            if (_inputRouter == null)
                return;
            _inputRouter.PauseRequested += HandlePauseRequested;
            _inputRouter.AbilityRequested += HandleAbilityRequested;
            _inputRouter.PasteRequested += HandlePasteRequested;
            _inputRouter.PoiCycleRequested += HandlePoiCycleRequested;
            _inputRouter.SubmitRequested += HandleSubmitRequested;
            _inputRouter.WorldClickRequested += HandleMapClick;
        }

        /// <summary>도메인 리로드나 오브젝트 파괴 뒤 입력 이벤트가 중복 호출되지 않도록 연결을 해제한다.</summary>
        private void DisconnectInput()
        {
            if (_inputRouter == null)
                return;
            _inputRouter.PauseRequested -= HandlePauseRequested;
            _inputRouter.AbilityRequested -= HandleAbilityRequested;
            _inputRouter.PasteRequested -= HandlePasteRequested;
            _inputRouter.PoiCycleRequested -= HandlePoiCycleRequested;
            _inputRouter.SubmitRequested -= HandleSubmitRequested;
            _inputRouter.WorldClickRequested -= HandleMapClick;
            _inputRouter = null;
        }

        private bool CanHandleGameplayInput()
        {
            return _screenMode == ScreenMode.Playing && _service != null && !_showPause &&
                   RunIsPlaying(_service.CurrentView) && !TurnPending && !_playerWalking;
        }

        private void HandlePauseRequested()
        {
            if (_screenMode != ScreenMode.Playing || _service == null)
                return;
            if (_actionConfirmation.IsArmed(Time.unscaledTime))
            {
                _actionConfirmation.Cancel();
                _gameplayTelemetry.RecordCancellation();
                _selectionController.Reject("고위험 행동 확인을 취소했습니다.");
                PlaySfx(AssetClip("UiCancelSound"));
                PublishPresentationState(PresentationChange.Hud);
                return;
            }
            _showPause = !_showPause;
            PlaySfx(_showPause ? AssetClip("UiCancelSound") : AssetClip("UiAcceptSound"));
        }

        private void HandleAbilityRequested(AbilityKind ability)
        {
            if (!CanHandleGameplayInput())
                return;
            SetAbility(ability);
            if (CanSubmitCurrentSelection()) SubmitImmediately();
        }

        private void HandlePasteRequested()
        {
            if (CanHandleGameplayInput() && _selectionController.Ability == AbilityKind.Copy && _selectionController.CopySourceCaptured &&
                _selectionController.SelectedCoord.HasValue)
                SubmitImmediately();
        }

        private void HandlePoiCycleRequested(int direction)
        {
            if (CanHandleGameplayInput()) CyclePoi(direction);
        }

        private void HandleSubmitRequested()
        {
            if (CanHandleGameplayInput()) Submit();
        }

        private void HandleMapClick(Vector2 mousePosition)
        {
            if (!CanHandleGameplayInput() || _cameraController == null || !_cameraController.IsReady)
                return;
            if (!_cameraController.ContainsScreenPoint(mousePosition))
                return;

            Vector3 world = _cameraController.ScreenToWorld(mousePosition);
            RunView view = _service.CurrentView;
            Vector2 origin = MapOrigin(view);
            var coord = new GridCoord(
                Mathf.FloorToInt((world.x - origin.x) / TileSize),
                Mathf.FloorToInt((world.y - origin.y) / TileSize));
            if (!ContainsWorldCoord(view, coord))
                return;

            _cameraInspectCoord = null;
            _cameraInspectUntil = 0f;

            Guid? clickedEntity = FindVisualTargetAt(world);
            if (!clickedEntity.HasValue)
                clickedEntity = _serverOnline ? FindServerTarget(coord, _selectionController.Ability == AbilityKind.Restore) : FindTarget(view, coord);
            if (clickedEntity.HasValue && TryGetEntityPosition(view, clickedEntity.Value, out GridCoord entityCoord))
                coord = entityCoord;
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
                    if (CanSubmitCurrentSelection()) SubmitImmediately();
                    return;
                }
                if (!CanSelectMovementDestination(view, coord))
                {
                    _selectionController.Reject("그곳까지 이어지는 통행 가능한 경로가 없습니다.");
                    PlaySfx(AssetClip("UiCancelSound"));
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
            if (_service == null || _showPause || TurnPending || _playerWalking ||
                (_sceneSequencePlayer != null && _sceneSequencePlayer.IsPlaying))
                return;
            RunView view = _service.CurrentView;
            if (!RunIsPlaying(view))
                return;
            if (bypassConfirmation) _actionConfirmation.Cancel();
            _gameplayTelemetry.RecordSubmit();
            if (!CanSubmitCurrentSelection())
            {
                _gameplayTelemetry.RecordInvalidSelection();
                PresentActionRejection("TARGET_INVALID", null);
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
            ApplyTurnPresentation(LocalTurnPresentationAdapter.Create(response));
            CaptureLocalRestorableTarget(response, view);

            RunView committedView = response.Run ?? _service.CurrentView;
            if (_selectionController.Ability == AbilityKind.Move)
            {
                List<GridCoord> path = _pathPlanner.FindLocalVisualPath(committedView, playerPositionBefore,
                    committedView.PlayerPosition);
                BeginPlayerPathAnimation(path, committedView);
            }
            SyncEntityVisuals(response.Run ?? _service.CurrentView);
            UpdateSelectionVisual(_service.CurrentView);
            LocalRunSaveService.Save(_service);

            if (response.ActionContext == ActionContext.Combat)
                PlaySfx(AssetClip("SlashSound"));
            else if (response.Outcome == RuleOutcome.CriticalSuccess)
                PlaySfx(AssetClip("SuccessJingle"));
            else
                PlaySfx(AssetClip("UiAcceptSound"));

            if (GmEnabled && _narrativeClient != null)
            {
                int generation = _runGeneration;
                int turn = response.TurnNo;
                _gmPending = true;
                StartCoroutine(_narrativeClient.RequestNarrative(_service.CurrentView, response, _selectionController.Ability,
                    response.NormalizedAttempt,
                    CurrentAreaName(_service.CurrentView), result =>
                    {
                        if (generation != _runGeneration || _service == null || _service.CurrentView.CurrentTurn != turn)
                            return;
                        _gmPending = false;
                        if (result.IsSuccess)
                        {
                            _lastNarrative = result.Narrative;
                            AddLog("GM · " + result.Narrative);
                            PublishPresentationState(PresentationChange.Dialogue | PresentationChange.Hud);
                        }
                    }));
            }

            _selectionController.ClearAfterAction(_encounterStagingCoord, _encounterMoveRequired);
            UpdateSelectionVisual(_service.CurrentView);

            if (_service.CurrentView.Status != RunStatus.Playing)
            {
                _gmPending = false;
                PlaySfx(AssetClip("SuccessJingle"));
            }
            PublishPresentationState(PresentationChange.Hud | PresentationChange.Dialogue |
                                     PresentationChange.Minimap | PresentationChange.Selection);
        }

        private IEnumerator SubmitServerAction(TurnRequest request)
        {
            EnsureRuntimeClients();
            _serverPending = true;
            _gmPending = true;
            _serverStatus = "권위 턴 커밋 중";

            GameApiClient.RunSnapshot runBeforeSubmit = _serverRun;
            int turnBeforeSubmit = _serverRun.currentTurn;
            TurnGatewayResult gatewayResult = null;
            yield return _turnCoordinator.Submit(request, value => gatewayResult = value);
            GameApiClient.Result<GameApiClient.CommittedTurn> result =
                gatewayResult?.TransportResponse as GameApiClient.Result<GameApiClient.CommittedTurn>;

            _serverPending = false;
            _gmPending = false;
            if (gatewayResult == null || !gatewayResult.IsSuccess)
            {
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
                            QueueServerSceneSequence(recoveredTurn.Value.sceneSequence);
                        }
                    }
                }
                yield break;
            }

            GameApiClient.CommittedTurn committed = gatewayResult.Payload as GameApiClient.CommittedTurn;
            if (committed == null)
            {
                PresentActionRejection("EMPTY_RESPONSE", "Server reported success but returned no committed turn.");
                yield break;
            }
            CaptureRestorableTarget(committed.Turn, runBeforeSubmit);
            _serverRun = committed.Run;
            SyncEncounterStateFromServer();
            SyncRestorableCandidateFromServer();
            ApplyServerTurnPresentation(committed.Turn);
            SyncServerEntityVisuals(_serverRun);
            QueueServerSceneSequence(committed.Turn.sceneSequence);
            UpdateSelectionVisual(_service.CurrentView);
            _runSessionController?.RememberServerRun(_serverRun.id);

            _selectionController.ClearAfterAction(_selectionController.SelectedCoord, _selectionController.Ability == AbilityKind.Move);
            UpdateSelectionVisual(_service.CurrentView);
            _serverStatus = committed.FromIdempotencyCache ? "멱등 응답 재생" : "권위 상태 커밋 완료";
            PlaySfx(_lastD20 == 20 ? AssetClip("SuccessJingle") : AssetClip("UiAcceptSound"));
        }

        private void PresentActionRejection(string errorCode, string technicalMessage)
        {
            string playerMessage = PlayerFacingRejection(errorCode, technicalMessage);
            _selectionController.Reject(playerMessage);
            _serverStatus = "행동 불가 · " + playerMessage;
            _dialoguePresenter.Dismiss();
            _sceneUi?.SetStoryVisible(false);
            AddLog("행동 거부 · " + (errorCode ?? "UNKNOWN") + " · " +
                   (technicalMessage ?? string.Empty));
            PlaySfx(AssetClip("UiCancelSound"));
            PublishPresentationState(PresentationChange.Hud | PresentationChange.Selection);
        }

        private static string PlayerFacingRejection(string errorCode, string technicalMessage)
        {
            string code = (errorCode ?? string.Empty).Trim().ToUpperInvariant();
            switch (code)
            {
                case "ADMIN_ACCESS_SKILL_MISMATCH":
                    string message = (technicalMessage ?? string.Empty).ToUpperInvariant();
                    if (message.Contains("RESTORE")) return "이 대상은 Ctrl R 복구로 해결해야 합니다.";
                    if (message.Contains("SEARCH")) return "이 대상은 Ctrl F 조사로 해결해야 합니다.";
                    if (message.Contains("CONNECT")) return "이 대상은 Ctrl K 연결로 해결해야 합니다.";
                    if (message.Contains("DELETE")) return "이 대상은 Delete 공격으로 해결해야 합니다.";
                    if (message.Contains("COPY")) return "이 대상은 Ctrl C 복제로 해결해야 합니다.";
                    return "이 대상에는 다른 관리자 키보드 스킬이 필요합니다.";
                case "OUT_OF_RANGE": return "대상이 너무 멉니다. 더 가까이 이동하세요.";
                case "INSUFFICIENT_FOCUS": return "집중력이 부족합니다. 보급이나 휴식이 필요합니다.";
                case "DEPENDENCY_NOT_REVEALED": return "먼저 Ctrl F로 대상의 약점을 조사하세요.";
                case "DESTINATION_OCCUPIED": return "선택한 타일에는 이미 다른 대상이 있습니다.";
                case "PATH_BLOCKED":
                case "TRAVEL_PATH_BLOCKED": return "그 위치까지 이동할 수 있는 길이 없습니다.";
                case "UNDO_NOT_AVAILABLE": return "되돌릴 수 있는 행동 기록이 부족합니다.";
                case "RESTORE_NOT_AVAILABLE": return "지금 복구할 수 있는 상태가 없습니다.";
                case "RUN_VERSION_CONFLICT": return "최신 게임 상태를 다시 불러왔습니다. 행동을 다시 선택하세요.";
                case "NETWORK_ERROR": return "서버에 연결하지 못했습니다. 잠시 후 다시 시도하세요.";
                case "TARGET_INVALID":
                case "ENTITY_NOT_FOUND": return "현재 선택한 대상에는 이 행동을 사용할 수 없습니다.";
                default: return "지금은 이 행동을 실행할 수 없습니다. 대상과 스킬 조건을 확인하세요.";
            }
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
                actorName));
            SyncRestorableCandidateFromServer();
            TryGetPlayerPosition(_service.CurrentView, out GridCoord playerPositionAfter);
            List<GridCoord> visualPath = _pathPlanner.FindServerVisualPath(
                navigation, _serverRun, playerPositionBefore, playerPositionAfter);
            BeginPlayerPathAnimation(visualPath, _service.CurrentView);
            SyncServerEntityVisuals(_serverRun);
            QueueServerSceneSequence(navigation?.sceneSequence);
            _selectionController.ClearToCoord(encounterOpened ? _encounterStagingCoord : null);
            UpdateSelectionVisual(_service.CurrentView);
            _runSessionController?.RememberServerRun(_serverRun.id);
            _serverStatus = encounterOpened
                ? "ACTIVE ENCOUNTER · 다음 행동은 의미 턴"
                : committed.FromIdempotencyCache ? "탐색 이동 멱등 응답" : "안전 탐색 이동 완료 · 턴 유지";
            PlaySfx(AssetClip("UiAcceptSound"));
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
                SyncServerEntityVisuals(_serverRun);
                _serverStatus = "최신 권위 상태 재동기화 완료";
            }
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
                _serverStatus = "런 시작 응답 없음";
                return;
            }

            _serverOnline = result.ServerOnline;
            _serverCampaign = result.ServerCampaign;
            _serverRun = result.ServerRun;
            _serverStatus = result.Status;
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
            _gmPending = false;
            _showPause = false;
            _playerWalking = false;
            _reopenDialogueAfterWalk = false;
            _service = service;
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
            _lastNarrativeFallbackUsed = true;
            _lastNarrativeModel = "deterministic";
            _tutorialPresenter.Start(PlayerPrefs.GetInt(TutorialCompletedKey, 0) == 0);
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
                _lastOutcome = "READY";
                _lastAttempt = "MOVE 목적지 또는 관리자 키보드 스킬과 대상을 선택하세요.";
                _lastExplanation = "자연어 없이 선택된 스킬·대상·장면 상태만으로 권위 판정을 실행합니다.";
                _lastNarrative = CampaignPremise(_service.CurrentView);
                AddLog("고정 월드 1회 생성 완료 · seed " + _worldSeed + " · " + ShortHash(LayoutHash(_service.CurrentView)));
            }

            _selectionController.ResetSelection(resumed ? AbilityKind.Move : FirstObjectiveAbility(_service.CurrentView));
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
            _cameraController?.SetEnabled(true);
            SnapCameraToPlayer();
            SetMusic(_assets != null ? _assets.VillageMusic ?? _assets.AdventureMusic : null);
            if (!_serverOnline)
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
            _screenMode = ScreenMode.Title;
            _showPause = false;
            _gmPending = false;
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
                if (entity.state != null && !entity.state.disabled && visual.Root != null && !visual.Root.activeSelf)
                    visual.Root.SetActive(true);
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

        private void UpdateServerHealthVisual(KeyboardWandererEntityVisualState visual, GameApiClient.EntitySnapshot entity)
        {
            if (!visual.IsHostile || visual.HealthBack == null || visual.HealthFill == null || entity.state == null)
                return;
            int max = Mathf.Max(1, entity.state.maxHp);
            float ratio = Mathf.Clamp01(entity.state.hp / (float)max);
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
                _playerAttackDownFrames);
        }

        private void BeginPlayerPathAnimation(IReadOnlyList<GridCoord> path, RunView view)
        {
            if (path == null || path.Count < 2 || !TryGetPlayerVisual(view, out KeyboardWandererEntityVisualState visual))
                return;
            visual.MovementPath.Clear();
            Vector2 origin = MapOrigin(view);
            for (int i = 1; i < path.Count; i++)
                visual.MovementPath.Enqueue(WorldPosition(origin, path[i]));
            if (visual.MovementPath.Count == 0)
                return;
            visual.TargetPosition = visual.MovementPath.Dequeue();
            _playerWalking = true;
            _reopenDialogueAfterWalk = !_dialoguePresenter.IsDismissed;
            _dialoguePresenter.Dismiss();
            _sceneUi?.SetStoryVisible(false);
            _cameraInspectCoord = null;
            _cameraInspectUntil = 0f;
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
            while (_playerWalking)
                yield return null;
            yield return _sceneSequencePlayer.Play(sequence, PlayServerSceneAction);
            _scenePlaybackCoroutine = null;
        }

        private IEnumerator PlayServerSceneAction(GameApiClient.SceneActionSnapshot action)
        {
            string type = (action.type ?? string.Empty).ToUpperInvariant();
            FocusServerSceneAction(action);
            if (type == "MOVE" || type == "FLEE")
            {
                yield return PlayServerActorMove(action);
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
                    AddLog(actorName + " · “" + action.line.Trim() + "”");
                    _dialoguePresenter.Show();
                    _sceneUi?.SetStoryVisible(true);
                }
                yield return new WaitForSecondsRealtime(0.7f);
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
                _playerActionUntil = Time.unscaledTime + 0.38f;
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
            if (!defeated) PlaySfx(AssetClip("HitSound"));
        }

        private void CompletePlayerPathAnimation()
        {
            _playerWalking = false;
            if (_reopenDialogueAfterWalk)
            {
                _reopenDialogueAfterWalk = false;
                _dialoguePresenter.Show();
                _sceneUi?.SetStoryVisible(true);
            }
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

        private IEnumerator AdvanceServerAmbientWander(GridCoord activeMin, GridCoord activeMax)
        {
            _ambientWanderPending = true;
            GameApiClient.Result<GameApiClient.RunSnapshot> result = null;
            yield return _gameApi.AdvanceAmbientWander(_serverRun.id, _serverRun.version,
                activeMin.X, activeMin.Y, activeMax.X, activeMax.Y, value => result = value);
            _ambientWanderPending = false;
            if (result?.IsSuccess == true)
            {
                _serverRun = result.Value;
                SyncServerEntityVisuals(_serverRun);
            }
            else if (result != null && result.ErrorCode == "RUN_VERSION_CONFLICT")
            {
                yield return ResyncServerRun();
            }
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

        private void PlaySfx(AudioClip clip)
        {
            _audioController?.PlaySfx(clip);
        }

        private AudioClip AssetClip(string fieldName)
        {
            return _visualAssetLibrary != null ? _visualAssetLibrary.GetAudioClip(fieldName) : null;
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
                if (!useServerWorld && OutsideLocalWorldDisc(coord, width, height))
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
            for (int y = 2; y < height - 2; y += 2)
            {
                for (int x = 2; x < width - 2; x += 2)
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
            if (_service == null || !TryGetPlayerVisual(_service.CurrentView, out KeyboardWandererEntityVisualState player) ||
                player.Renderer == null)
                return;
            _decorationRenderer?.UpdateOcclusion(player.Root.transform.position,
                player.Renderer.sortingOrder, Time.unscaledDeltaTime);
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
            if (renderer == null || controller == null)
                return null;
            Animator animator = renderer.GetComponent<Animator>();
            if (animator == null)
                throw new InvalidOperationException(
                    "The authored entity prefab Actor Renderer must include an Animator component.");
            animator.runtimeAnimatorController = controller;
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
            return KeyboardWandererHudTextComposer.ObjectiveHud(PresentationModel(view),
                _encounterMoveRequired);
        }

        private string ActionGuidanceText(RunView view)
        {
            if (CanSubmitCurrentSelection())
                return ExecutionPreview(view);
            return KeyboardWandererHudTextComposer.ObjectiveRouteHint(PresentationModel(view)) +
                   NarrativeSelectionHint() +
                   "\nMOVE는 턴을 쓰지 않고, 키보드 기술은 D20과 의미 턴 1회를 사용합니다.";
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
            ApplyTurnPresentation(ServerTurnPresentationAdapter.FromTurn(turn, _serverRun, GmEnabled));
        }

        /// <summary>응답 출처를 구분하지 않고 정규화된 결과를 현재 화면 상태에 반영한다.</summary>
        private void ApplyTurnPresentation(TurnPresentationResult presentation)
        {
            if (presentation == null)
                return;
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
            _lastNarrativeFallbackUsed = presentation.NarrativeFallbackUsed;
            _lastNarrativeModel = presentation.NarrativeModel;
            _playerActionUntil = Time.unscaledTime + presentation.ActionDuration;
            for (int i = 0; i < presentation.LogEntries.Length; i++)
                AddLog(presentation.LogEntries[i]);
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
