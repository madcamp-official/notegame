using System;
using System.Collections;
using System.Collections.Generic;
using KeyboardWanderer.Core;
using KeyboardWanderer.Gameplay;
using KeyboardWanderer.Networking;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace KeyboardWanderer.Demo
{
    public sealed class KeyboardWandererDemoController : MonoBehaviour
    {
        private const float TileSize = 1f;
        private const int TerrainSortingOrder = -1000;
        private const string MusicVolumeKey = "keyboard-wanderer.music-volume";
        private const string SfxVolumeKey = "keyboard-wanderer.sfx-volume";
        private const string GmEnabledKey = "keyboard-wanderer.gm-enabled";
        private const string ServerRunIdKey = "keyboard-wanderer.server-run-id";
        private const string TutorialCompletedKey = "keyboard-wanderer.tutorial-v1-complete";

        private enum ScreenMode
        {
            Title,
            Playing,
            Settings
        }

        private sealed class EntityVisual
        {
            public KeyboardWandererEntityView AuthoredView;
            public GameObject Root;
            public SpriteRenderer Renderer;
            public Animator Animator;
            public GameObject HealthBack;
            public GameObject HealthFill;
            public GameObject RootComponentLabel;
            public Sprite[] IdleFrames;
            public Sprite[] WalkFrames;
            public Sprite[] WalkLeftFrames;
            public Sprite[] WalkUpFrames;
            public Sprite[] WalkDownFrames;
            public Sprite[] AttackFrames;
            public Vector3 TargetPosition;
            public readonly Queue<Vector3> MovementPath = new Queue<Vector3>();
            public Vector2 Facing = Vector2.down;
            public Vector3 WanderTargetOffset;
            public float WanderPhase;
            public float NextWanderDecisionAt;
            public int WanderStep;
            public bool IsWandering;
            public Color BaseColor;
            public float DesiredSize;
            public bool FacingLeft;
            public int FacingVertical;
            public bool IsPlayer;
            public bool IsHostile;
        }

        private static readonly Color Gold = Hex("d3a64b");
        private static readonly Color GoldDim = Hex("7c5d2b");
        private static readonly Color Parchment = Hex("f0dfb6");
        private static readonly Color Muted = Hex("ad9878");
        private static readonly Color Leaf = Hex("65a850");
        private static readonly Color Ruby = Hex("c84c43");
        private static readonly Color FocusBlue = Hex("57a9bd");
        private static readonly Rect NeopjukiTestPanelRect = new Rect(270f, 70f, 900f, 200f);
        private static readonly (string Id, string Label)[] NeopjukiTestAnimations =
        {
            ("auto", "자동"),
            ("idle", "기본"),
            ("right", "오른쪽 이동"),
            ("left", "왼쪽 이동"),
            ("wave", "손 흔들기"),
            ("jump", "점프"),
            ("failed", "실패"),
            ("waiting", "승인 대기"),
            ("review", "리뷰"),
            ("keyboard-attack", "오른쪽 공격"),
            ("keyboard-magic", "키보드 마법"),
            ("keyboard-debug", "버그 수정"),
            ("keyboard-attack-left", "왼쪽 공격"),
            ("keyboard-attack-up", "위쪽 공격"),
            ("keyboard-attack-down", "아래쪽 공격"),
            ("walking-up", "위로 걷기"),
            ("walking-down", "아래로 걷기")
        };

        private readonly Dictionary<Guid, EntityVisual> _entityVisuals = new Dictionary<Guid, EntityVisual>();
        private readonly List<Sprite> _runtimeSprites = new List<Sprite>();
        private readonly List<Texture2D> _runtimeTextures = new List<Texture2D>();
        private readonly List<Tile> _runtimeTiles = new List<Tile>();
        private readonly List<string> _sessionLog = new List<string>();
        private readonly Dictionary<SpriteRenderer, Color> _decorationBaseColors =
            new Dictionary<SpriteRenderer, Color>();
        private readonly Stack<KeyboardWandererEntityView> _entityViewPool = new Stack<KeyboardWandererEntityView>();
        private readonly Stack<SpriteRenderer> _decorationPool = new Stack<SpriteRenderer>();
        private readonly List<SpriteRenderer> _activeDecorations = new List<SpriteRenderer>();

        [Header("Authored content")]
        [SerializeField] private KeyboardWandererAuthoringSettings authoringSettings;
        [SerializeField] private NinjaAdventureAssetManifest authoredAssetManifest;
        [SerializeField] private KeyboardWandererWorldView authoredWorld;
        [SerializeField] private Camera authoredCamera;
        [SerializeField] private KeyboardWandererCameraController authoredCameraController;
        [SerializeField] private AudioSource authoredMusicSource;
        [SerializeField] private AudioSource authoredSfxSource;
        [SerializeField] private KeyboardWandererAudioController authoredAudioController;
        [SerializeField] private KeyboardWandererInputController authoredInputController;
        [SerializeField] private IcosahedronDice authoredDice;

        private LocalTurnService _service;
        private ITurnGateway _turnGateway;
        private NinjaAdventureAssetManifest _assets;
        private GmNarrativeClient _narrativeClient;
        private GameApiClient _gameApi;
        private SceneSequencePlayer _sceneSequencePlayer;
        private Coroutine _scenePlaybackCoroutine;
        private GameApiClient.CampaignSnapshot _serverCampaign;
        private GameApiClient.RunSnapshot _serverRun;
        private ScreenMode _screenMode = ScreenMode.Title;
        private ScreenMode _settingsReturn = ScreenMode.Title;
        private AbilityKind _ability = AbilityKind.Move;
        private GridCoord? _selectedCoord;
        private Guid? _selectedTarget;
        private Guid? _selectedSecondaryTarget;
        private bool _copySourceCaptured;
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
        private string _authoredDialogueSignature = string.Empty;
        private int _authoredDialoguePage;
        private bool _authoredDialogueDismissed;
        private bool _tutorialActive;
        private int _tutorialPage;
        private bool _gmPending;
        private bool _gmEnabled = true;
        private bool _serverOnline;
        private bool _serverPending;
        private bool _encounterMoveRequired;
        private GridCoord? _encounterStagingCoord;
        private string _encounterReason;
        private string _serverStatus = "권위 서버 확인 전";
        private bool _showPause;
        private bool _playerWalking;
        private bool _reopenDialogueAfterWalk;
        private int _runGeneration;
        private long _worldSeed;
        private int _poiCursor = -1;
        private GridCoord? _cameraInspectCoord;
        private float _cameraInspectUntil;
        // TEMP DEBUG — 구역 순회 (커밋 전 제거). 넙죽이 테스트 패널과 동일한 임시 도구.
        private bool _debugTourActive;
        private int _debugTourIndex;
        private float _debugTourZoom = 32f;
        private float _debugTourBaseOrthoSize = 8.25f;
        private string _poiLabel = "POI 탐색";
        private string _movementSelectionFeedback = string.Empty;

        private KeyboardWandererCameraController _cameraController;
        private KeyboardWandererSceneUI _sceneUi;
        private GameObject _worldRoot;
        private SpriteRenderer _selectionRenderer;
        private KeyboardWandererAudioController _audioController;
        private KeyboardWandererInputController _inputController;
        private float _musicVolume = 0.65f;
        private float _sfxVolume = 0.8f;
        private float _nextAnimationAt;
        private int _animationFrame;
        private float _playerActionUntil;
        private Font _koreanFont;

        private float PlayerWalkSpeed => authoringSettings != null ? authoringSettings.PlayerWalkSpeed : 4.2f;
        private Transform WorldContentRoot => authoredWorld != null ? authoredWorld.RuntimeEntities : null;
        private Transform WorldLandmarkRoot => authoredWorld != null ? authoredWorld.RuntimeLandmarks : null;
        private Transform WorldEffectsRoot => authoredWorld != null ? authoredWorld.RuntimeEffects : null;

        private Sprite _grassSprite;
        private Sprite _dirtSprite;
        private Sprite _darkGrassSprite;
        private Sprite _wallSprite;
        private Sprite _waterSprite;
        private Sprite _snowSprite;
        private Sprite _cavernFloorSprite;
        private Sprite _ruinFloorSprite;
        private Sprite _forestTreeSprite;
        private Sprite _forestHouseSprite;
        private Sprite _wetlandPlantSprite;
        private Sprite _wetlandLandmarkSprite;
        private Sprite _desertShrubSprite;
        private Sprite _desertLandmarkSprite;
        private Sprite _frostTreeSprite;
        private Sprite _frostLandmarkSprite;
        private Sprite _cavernCrystalSprite;
        private Sprite _ruinTreeSprite;
        private Sprite _ruinLandmarkSprite;
        private Sprite[] _forestDecorationSprites = Array.Empty<Sprite>();
        private Sprite[] _wetlandDecorationSprites = Array.Empty<Sprite>();
        private Sprite[] _desertDecorationSprites = Array.Empty<Sprite>();
        private Sprite[] _desertBuildingSprites = Array.Empty<Sprite>();
        private Sprite[] _frostDecorationSprites = Array.Empty<Sprite>();
        private Sprite[] _cavernDecorationSprites = Array.Empty<Sprite>();
        private Sprite[] _ruinDecorationSprites = Array.Empty<Sprite>();
        private Sprite _playerSprite;
        private Sprite _wardenSprite;
        private Sprite _villagerSprite;
        private Sprite _slimeSprite;
        private Sprite _bookSprite;
        private Sprite _crateSprite;
        private Sprite _chestSprite;
        private Sprite _d20Sprite;
        private Sprite _whiteSprite;
        private Sprite[] _playerIdleFrames = Array.Empty<Sprite>();
        private Sprite[] _playerWalkFrames = Array.Empty<Sprite>();
        private Sprite[] _playerWalkLeftFrames = Array.Empty<Sprite>();
        private Sprite[] _playerAttackFrames = Array.Empty<Sprite>();
        private Sprite[] _neopjukiWaveFrames = Array.Empty<Sprite>();
        private Sprite[] _neopjukiJumpFrames = Array.Empty<Sprite>();
        private Sprite[] _neopjukiFailedFrames = Array.Empty<Sprite>();
        private Sprite[] _neopjukiWaitingFrames = Array.Empty<Sprite>();
        private Sprite[] _neopjukiReviewFrames = Array.Empty<Sprite>();
        private Sprite[] _neopjukiKeyboardAttackFrames = Array.Empty<Sprite>();
        private Sprite[] _neopjukiKeyboardMagicFrames = Array.Empty<Sprite>();
        private Sprite[] _neopjukiKeyboardDebugFrames = Array.Empty<Sprite>();
        private Sprite[] _neopjukiKeyboardAttackLeftFrames = Array.Empty<Sprite>();
        private Sprite[] _neopjukiKeyboardAttackUpFrames = Array.Empty<Sprite>();
        private Sprite[] _neopjukiKeyboardAttackDownFrames = Array.Empty<Sprite>();
        private Sprite[] _neopjukiWalkUpFrames = Array.Empty<Sprite>();
        private Sprite[] _neopjukiWalkDownFrames = Array.Empty<Sprite>();
        private Sprite[] _slimeFrames = Array.Empty<Sprite>();
        private Sprite[] _villagerFrames = Array.Empty<Sprite>();
        private string _neopjukiTestAnimation = "auto";

        private Texture2D _inkTexture;
        private Texture2D _panelTexture;
        private Texture2D _raisedTexture;
        private Texture2D _buttonTexture;
        private Texture2D _buttonHoverTexture;
        private Texture2D _buttonPressedTexture;
        private Texture2D _selectedTexture;
        private Texture2D _disabledTexture;
        private Texture2D _fieldTexture;
        private Texture2D _whiteTexture;

        private bool _stylesReady;
        private GUIStyle _titleStyle;
        private GUIStyle _subtitleStyle;
        private GUIStyle _headerStyle;
        private GUIStyle _labelStyle;
        private GUIStyle _smallStyle;
        private GUIStyle _mutedStyle;
        private GUIStyle _centerStyle;
        private GUIStyle _previewPremiseStyle;
        private GUIStyle _buttonStyle;
        private GUIStyle _selectedButtonStyle;
        private GUIStyle _dangerButtonStyle;
        private GUIStyle _textAreaStyle;
        private GUIStyle _numberStyle;

        private void Awake()
        {
            _runCoordinator.PresentationChanged += HandlePresentationChanged;
            _sceneUi = GetComponentInChildren<KeyboardWandererSceneUI>(true);
            _hudPresenter = new HudPresenter(_sceneUi);
            _sceneSequencePlayer = GetComponent<SceneSequencePlayer>();
            if (authoredDice == null)
                authoredDice = FindFirstObjectByType<IcosahedronDice>(FindObjectsInactive.Include);
            LoadSettings();
            LoadNinjaAdventureAssets();
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
            KeyboardWandererInputController inputController)
        {
            authoringSettings = settings;
            authoredAssetManifest = manifest;
            authoredWorld = world;
            authoredCamera = sceneCamera;
            authoredCameraController = cameraController;
            authoredMusicSource = music;
            authoredSfxSource = sfx;
            authoredAudioController = audioController;
            authoredInputController = inputController;
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
                _gameApi = new GameApiClient();
        }

        private void OnDestroy()
        {
            _runCoordinator.PresentationChanged -= HandlePresentationChanged;
            _sceneSequencePlayer?.Cancel();
            StopAllCoroutines();
            for (int i = 0; i < _runtimeSprites.Count; i++)
                if (_runtimeSprites[i] != null)
                    Destroy(_runtimeSprites[i]);
            for (int i = 0; i < _runtimeTextures.Count; i++)
                if (_runtimeTextures[i] != null)
                    Destroy(_runtimeTextures[i]);
            for (int i = 0; i < _runtimeTiles.Count; i++)
                if (_runtimeTiles[i] != null)
                    Destroy(_runtimeTiles[i]);
            if (_koreanFont != null && (_assets == null || _koreanFont != _assets.PixelFont))
                Destroy(_koreanFont);
        }

        private void Update()
        {
            UpdateCameraViewport();
            PublishPresentationState();
            if (_screenMode != ScreenMode.Playing || _service == null)
                return;

            UpdateAnimatedVisuals();
            UpdateCameraFollow();
            HandleKeyboard();
            HandleDebugRegionTour(); // TEMP DEBUG — 커밋 전 제거
            HandleMapClick();
        }

        private void HandlePresentationChanged(RunPresentationState state, PresentationChange changes)
        {
            DrawNeopjukiAnimationTestPanel();
            DrawDebugRegionTourPanel(); // TEMP DEBUG — 커밋 전 제거
            if (_sceneUi != null && _sceneUi.IsReady)
                return;

        private void PublishPresentationState(PresentationChange requested = PresentationChange.None)
        {
            RunView view = _service?.CurrentView;
            GridCoord player = view != null ? view.PlayerPosition : default;
            string layoutHash = view != null ? LayoutHash(view) : string.Empty;
            var state = new RunPresentationState(view?.Version ?? 0L, view?.CurrentTurn ?? 0, layoutHash,
                player, _selectedCoord, _selectedTarget, _ability, (int)_screenMode, _authoredDialoguePage,
                _authoredDialogueSignature, _showPause, _serverPending || _gmPending, _playerWalking);
            _runCoordinator.Publish(state, requested);
        }

        private void DrawNeopjukiAnimationTestPanel()
        {
            if (_screenMode != ScreenMode.Playing || _service == null || _neopjukiWaveFrames.Length == 0)
                return;

            Matrix4x4 previousMatrix = GUI.matrix;
            Color previousColor = GUI.color;
            Font previousFont = GUI.skin.font;
            GUI.matrix = Matrix4x4.Scale(new Vector3(Screen.width / LogicalWidth, Screen.height / LogicalHeight, 1f));
            if (_koreanFont != null)
                GUI.skin.font = _koreanFont;
            else if (_assets != null && _assets.PixelFont != null)
                GUI.skin.font = _assets.PixelFont;
            EnsureStyles();

            GUI.Box(NeopjukiTestPanelRect, "넙죽이 애니메이션 테스트");
            const float buttonWidth = 166f;
            const float buttonHeight = 32f;
            for (int i = 0; i < NeopjukiTestAnimations.Length; i++)
            {
                int column = i % 5;
                int row = i / 5;
                var rect = new Rect(
                    NeopjukiTestPanelRect.x + 18f + column * 174f,
                    NeopjukiTestPanelRect.y + 34f + row * 38f,
                    buttonWidth,
                    buttonHeight);
                (string id, string label) = NeopjukiTestAnimations[i];
                GUIStyle style = string.Equals(_neopjukiTestAnimation, id, StringComparison.Ordinal)
                    ? _selectedButtonStyle
                    : _buttonStyle;
                if (GUI.Button(rect, label, style))
                    SetNeopjukiTestAnimation(id);
            }

            GUI.skin.font = previousFont;
            GUI.color = previousColor;
            GUI.matrix = previousMatrix;
        }

        private void SetNeopjukiTestAnimation(string animationId)
        {
            _neopjukiTestAnimation = string.IsNullOrWhiteSpace(animationId) ? "auto" : animationId;
            _animationFrame = 0;
            _nextAnimationAt = 0f;
            PlaySfx(AssetClip("UiMoveSound"));
        }

        private Sprite[] CurrentNeopjukiTestFrames()
        {
            switch (_neopjukiTestAnimation)
            {
                case "idle": return _playerIdleFrames;
                case "right": return _playerWalkFrames;
                case "left": return _playerWalkLeftFrames;
                case "wave": return _neopjukiWaveFrames;
                case "jump": return _neopjukiJumpFrames;
                case "failed": return _neopjukiFailedFrames;
                case "waiting": return _neopjukiWaitingFrames;
                case "review": return _neopjukiReviewFrames;
                case "keyboard-attack": return _neopjukiKeyboardAttackFrames;
                case "keyboard-magic": return _neopjukiKeyboardMagicFrames;
                case "keyboard-debug": return _neopjukiKeyboardDebugFrames;
                case "keyboard-attack-left": return _neopjukiKeyboardAttackLeftFrames;
                case "keyboard-attack-up": return _neopjukiKeyboardAttackUpFrames;
                case "keyboard-attack-down": return _neopjukiKeyboardAttackDownFrames;
                case "walking-up": return _neopjukiWalkUpFrames;
                case "walking-down": return _neopjukiWalkDownFrames;
                default: return Array.Empty<Sprite>();
            }
        }

        private Sprite[] CurrentNeopjukiAttackFrames(EntityVisual visual)
        {
            if (visual == null || !visual.IsPlayer)
                return visual != null ? visual.AttackFrames : Array.Empty<Sprite>();
            if (visual.FacingVertical > 0 && _neopjukiKeyboardAttackUpFrames.Length > 0)
                return _neopjukiKeyboardAttackUpFrames;
            if (visual.FacingVertical < 0 && _neopjukiKeyboardAttackDownFrames.Length > 0)
                return _neopjukiKeyboardAttackDownFrames;
            if (visual.FacingLeft && _neopjukiKeyboardAttackLeftFrames.Length > 0)
                return _neopjukiKeyboardAttackLeftFrames;
            return visual.AttackFrames;
        }

        private void UpdateAuthoredUi()
        {
            if (_sceneUi == null || !_sceneUi.IsReady)
                return;

            PresentationChange changes = _pendingPresentationChanges;
            _pendingPresentationChanges = PresentationChange.None;

            bool ended = _screenMode == ScreenMode.Playing && _service != null && !RunIsPlaying(_service.CurrentView);
            _hudPresenter.PresentScreen(_screenMode == ScreenMode.Title, _screenMode == ScreenMode.Settings,
                _screenMode == ScreenMode.Playing, _showPause, ended, _musicVolume, _sfxVolume, _gmEnabled);
            _sceneUi.SetTitleCharacter(_playerSprite);

            int nextCounter = PlayerPrefs.GetInt("keyboard-wanderer.run-counter", 0) + 1;
            long nextSeed = 20260717L + nextCounter;
            CampaignBlueprint preview = CampaignCatalog.Create(nextSeed);
            _sceneUi.SetText("Title Heading", CampaignCatalog.CampaignTitle);
            _sceneUi.SetText("Title Subtitle", "코드리아 × 관리자 키보드 × 선택 회수");
            _sceneUi.SetText("Title Seed", "NEXT SEED  " + nextSeed);
            _sceneUi.SetText("Title Premise", preview.Title + "\n\n" + preview.Premise);
            _sceneUi.SetText("Title Status", _serverStatus + " · Ninja Adventure CC0");
            _sceneUi.SetImageSprite("Title Character", _playerSprite);
            _sceneUi.SetButtonState("New Run Button", !_serverPending);
            _sceneUi.SetButtonState("Continue Button", !_serverPending &&
                (LocalRunSaveService.HasSave || !string.IsNullOrWhiteSpace(PlayerPrefs.GetString(ServerRunIdKey, string.Empty))));

            if (_service == null)
                return;

            RunView view = _service.CurrentView;
            if ((changes & PresentationChange.Minimap) != 0)
                UpdateMinimap(view);
            string narrative = _lastOutcome == "READY" || _lastOutcome == "RESTORED"
                ? CampaignPremise(view)
                : ShortNarrative(_lastNarrative);
            if (_tutorialActive)
            {
                string[] tutorialPages = TutorialPages(view);
                _tutorialPage = Mathf.Clamp(_tutorialPage, 0, tutorialPages.Length - 1);
                _sceneUi.SetStoryVisible(true);
                _sceneUi.SetText(KeyboardWandererUiText.DialogueSpeaker,
                    "게임 방법 " + (_tutorialPage + 1) + "/" + tutorialPages.Length);
                _sceneUi.SetText(KeyboardWandererUiText.Story, tutorialPages[_tutorialPage]);
                _sceneUi.SetText(KeyboardWandererUiText.NextDialogueLabel,
                    _tutorialPage < tutorialPages.Length - 1 ? "다음 ▶" : "게임 시작");
                _sceneUi.SetButtonState(KeyboardWandererUiButton.NextDialogue, true);
            }
            else
            {
                string[] dialoguePages = BuildDialoguePages(narrative);
                string dialogueSignature = string.Join("\u001f", dialoguePages);
                if (!string.Equals(_authoredDialogueSignature, dialogueSignature, StringComparison.Ordinal))
                {
                    _authoredDialogueSignature = dialogueSignature;
                    _authoredDialoguePage = 0;
                    if (_playerWalking)
                        _reopenDialogueAfterWalk = true;
                    else
                        _authoredDialogueDismissed = false;
                }
                _sceneUi.SetStoryVisible(!_authoredDialogueDismissed);
                _authoredDialoguePage = Mathf.Clamp(_authoredDialoguePage, 0, Mathf.Max(0, dialoguePages.Length - 1));
                bool showingResult = HasActionResultPage() && _authoredDialoguePage == 0;
                int narrationPage = HasActionResultPage() ? 1 : 0;
                bool showingNarration = _authoredDialoguePage == narrationPage;
                _sceneUi.SetText(KeyboardWandererUiText.DialogueSpeaker,
                    showingResult ? "행동 결과" : showingNarration ? NarrativeSourceLabel() : "코드리아 주민");
                _sceneUi.SetText(KeyboardWandererUiText.Story, dialoguePages[_authoredDialoguePage]);
                bool hasNextDialogue = _authoredDialoguePage < dialoguePages.Length - 1;
                _sceneUi.SetText(KeyboardWandererUiText.NextDialogueLabel, hasNextDialogue ? "다음 ▶" : "대화 끝");
                _sceneUi.SetButtonState(KeyboardWandererUiButton.NextDialogue, true);
            }
            _sceneUi.SetText(KeyboardWandererUiText.SceneLocation, CurrentAreaName(view) + " · " + CurrentBiomeLabel(view));
            _sceneUi.SetText(KeyboardWandererUiText.SceneTitle, StoryBeat(view));
            _sceneUi.SetText(KeyboardWandererUiText.CurrentObjective, ObjectiveHudText(view));
            _sceneUi.SetText(KeyboardWandererUiText.ActionHint,
                _playerWalking ? "선택한 경로를 따라 이동하고 있습니다." : ActionGuidanceText(view));
            bool selectionReady = CanSubmitCurrentSelection();
            _sceneUi.SetText(KeyboardWandererUiText.SelectionHeading,
                AbilityPlayerLabel(_ability) + " · " + (selectionReady ? "사용 가능" : "준비 필요"));
            _sceneUi.SetText(KeyboardWandererUiText.SelectionDetail, SelectionStatusDetail(view));
            EntityView selectedEntity = SelectedEntity(view, _selectedTarget);
            Sprite selectedTargetSprite = selectedEntity == null ? null : SpriteForEntity(selectedEntity);
            if (selectedTargetSprite == null && TryGetServerEntity(_selectedTarget, out GameApiClient.EntitySnapshot serverSelected))
                selectedTargetSprite = SpriteForServerEntity(serverSelected,
                    string.Equals(serverSelected.id, _serverRun.playerEntityId, StringComparison.OrdinalIgnoreCase));
            _sceneUi.SetSelectionVisual(_ability,
                selectedTargetSprite, selectionReady);

            SetAbilityButton(KeyboardWandererUiButton.Move, AbilityKind.Move);
            SetAbilityButton(KeyboardWandererUiButton.Copy, AbilityKind.Copy);
            SetAbilityButton(KeyboardWandererUiButton.Delete, AbilityKind.Delete);
            SetAbilityButton(KeyboardWandererUiButton.Connect, AbilityKind.Connect);
            SetAbilityButton(KeyboardWandererUiButton.Restore, AbilityKind.Restore);
            SetAbilityButton(KeyboardWandererUiButton.Undo, AbilityKind.Undo);
            SetAbilityButton(KeyboardWandererUiButton.Search, AbilityKind.Search);
            SetAbilityButton(KeyboardWandererUiButton.SelectAll, AbilityKind.SelectAll);
            _sceneUi.SetText(KeyboardWandererUiText.CopySkillLabel, _copySourceCaptured ? "Ctrl V" : "Ctrl C");
            _sceneUi.SetText(KeyboardWandererUiText.DeleteSkillLabel, "Delete");
            _sceneUi.SetText(KeyboardWandererUiText.UndoSkillLabel, "Ctrl Z");
            _sceneUi.SetCopyPasteMode(_copySourceCaptured);
            _sceneUi.SetOutcomeEmote(_lastOutcome);
            _sceneUi.SetText(KeyboardWandererUiText.ConfirmActionLabel,
                _ability == AbilityKind.Interact ? "상호작용" : "실행");
            _sceneUi.SetButtonState(KeyboardWandererUiButton.ConfirmAction,
                RunIsPlaying(view) && !_showPause && !_serverPending && !_playerWalking && CanSubmitCurrentSelection());
            _sceneUi.SetText(KeyboardWandererUiText.EndingHeading, "코드리아의 결말");
            _sceneUi.SetText(KeyboardWandererUiText.EndingText, EndingTitle(EndingCode(view)) + "\n\n" + EndingDescription(EndingCode(view)) +
                "\n\n당신의 선택이 코드리아에 남긴 결말입니다.");
        }

        public void UiStartNewRun() => StartNewRun();
        public void UiContinueRun() => ContinueRun();
        public void UiCyclePoi(int direction) => CyclePoi(direction);
        public void UiSetAbility(AbilityKind ability)
        {
            if (ability == AbilityKind.Copy && _ability == AbilityKind.Copy && _copySourceCaptured &&
                _selectedCoord.HasValue && CanSubmitCurrentSelection())
            {
                Submit();
                return;
            }
            SetAbility(ability);
            if ((ability == AbilityKind.SelectAll || ability == AbilityKind.Undo) &&
                IsSkillEnabledForCurrentTarget(ability))
                Submit();
        }
        public void UiSubmit() => Submit();
        public void UiAdvanceDialogue()
        {
            if (_tutorialActive)
            {
                string[] tutorialPages = TutorialPages(_service?.CurrentView);
                if (_tutorialPage < tutorialPages.Length - 1)
                {
                    _tutorialPage++;
                }
                else
                {
                    _tutorialActive = false;
                    _tutorialPage = 0;
                    _authoredDialogueSignature = string.Empty;
                    _authoredDialogueDismissed = false;
                    PlayerPrefs.SetInt(TutorialCompletedKey, 1);
                    PlayerPrefs.Save();
                }
                PlaySfx(AssetClip("UiAcceptSound"));
                return;
            }
            string[] pages = BuildDialoguePages(_service == null ? _lastNarrative :
                (_lastOutcome == "READY" || _lastOutcome == "RESTORED" ? CampaignPremise(_service.CurrentView) : ShortNarrative(_lastNarrative)));
            if (_authoredDialoguePage < pages.Length - 1)
            {
                _authoredDialoguePage++;
            }
            else
            {
                _authoredDialogueDismissed = true;
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

        private void SetAbilityButton(KeyboardWandererUiButton button, AbilityKind ability)
        {
            bool available = IsSkillEnabledForCurrentTarget(ability);
            _sceneUi.SetButtonState(button, CanHandleGameplayInput(), _ability == ability, available);
        }

        private string[] BuildDialoguePages(string narrative)
        {
            var pages = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            if (HasActionResultPage())
            {
                string result = (_ability == AbilityKind.Interact ? "INTERACT" : _ability.ToString().ToUpperInvariant()) +
                                " · " + _lastOutcome;
                if (_lastD20 > 0)
                    result += " · D20 " + _lastD20 + " " + Signed(_lastModifier) + " vs " + _lastDifficulty;
                if (!string.IsNullOrWhiteSpace(_lastAttempt)) result += "\n" + _lastAttempt.Trim();
                if (!string.IsNullOrWhiteSpace(_lastStateChanges)) result += "\n변화 · " + _lastStateChanges.Trim();
                if (!string.IsNullOrWhiteSpace(_lastExplanation)) result += "\n" + _lastExplanation.Trim();
                pages.Add(result);
                seen.Add(result);
            }
            if (!string.IsNullOrWhiteSpace(narrative) && seen.Add(narrative.Trim()))
                pages.Add(narrative.Trim());
            for (int i = 0; i < _lastDialogue.Length; i++)
                if (!string.IsNullOrWhiteSpace(_lastDialogue[i]) && seen.Add(_lastDialogue[i].Trim()))
                    pages.Add(_lastDialogue[i].Trim());
            if (pages.Count == 0) pages.Add("코드리아의 다음 이야기가 시작되기를 기다리고 있습니다.");
            return pages.ToArray();
        }

        private string[] TutorialPages(RunView view)
        {
            string objective = view == null ? "좌측 상단의 현재 목표" : StoryBeatObjective(view);
            return new[]
            {
                "당신은 넙죽이입니다. 붕괴한 코드리아를 탐험해 관리자 권한 3개를 되찾고, 루트 시스템까지 도달하는 것이 최종 목표입니다.\n지금 할 일은 좌측 상단의 ‘현재 목표’에서 확인할 수 있습니다.",
                "이동: W 버튼을 선택하고 지도에서 빈 타일을 누른 뒤, 우측 아래 실행 버튼을 누르세요.\n이동은 의미 턴과 D20을 사용하지 않습니다. 분홍 미니맵 표식이 현재 목표입니다.",
                "조사: Ctrl F를 선택하고 목표 인물이나 물건을 누른 뒤 실행하세요.\n조사·공격·연결 같은 키보드 기술은 의미 턴 1회와 D20 판정을 사용하며, 성공과 실패가 세계에 남습니다.",
                "주요 기술: Delete는 적 1명 공격, Ctrl A는 주변 적 전체 공격, Ctrl K는 두 대상 연결·대화, Ctrl Z는 최근 의미 턴 2개 되돌리기입니다.\n사용할 수 없을 때는 좌측 선택 패널에 필요한 대상·거리·조건이 표시됩니다.",
                "첫 목표: " + objective + "\n행동 결과는 서버 규칙이 확정하고, 그 뒤의 짧은 장면과 대사는 AI가 표현합니다. AI가 응답하지 않아도 기본 이야기로 게임은 계속됩니다."
            };
        }

        private string NarrativeSourceLabel()
        {
            if (!_gmEnabled) return "규칙 결과";
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
            if (!string.IsNullOrWhiteSpace(_movementSelectionFeedback))
                return _movementSelectionFeedback;
            switch (_ability)
            {
                case AbilityKind.Move: return "이동할 타일을 고른 뒤 실행을 누르세요. 캐릭터는 길을 따라 걷습니다.";
                case AbilityKind.Connect: return "이어 주고 싶은 두 대상을 지도에서 차례로 고르세요.";
                case AbilityKind.Delete: return "단일 공격할 적을 클릭한 뒤 실행을 누르세요.";
                case AbilityKind.Restore: return "복구 가능한 대상이 생기면 대상이 자동 선택됩니다.";
                case AbilityKind.Interact: return "가까운 대상과 상호작용하려면 실행을 누르세요.";
                case AbilityKind.Undo: return "Ctrl Z로 최근 의미 턴 2회의 상태를 시간 역행합니다.";
                case AbilityKind.Search: return "조사할 대상 하나를 클릭한 뒤 Ctrl F를 실행하세요.";
                case AbilityKind.SelectAll: return "Ctrl A로 주변 4칸의 모든 적을 광범위 공격합니다.";
                case AbilityKind.Copy: return _copySourceCaptured
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
            LocalRunSaveService.Delete();
            PlayerPrefs.DeleteKey(ServerRunIdKey);
            PlayerPrefs.Save();
        }

        public void UiSetMusicVolume(float value)
        {
            _musicVolume = value;
            ApplyAudioVolumes();
            SaveSettings();
        }

        public void UiSetSfxVolume(float value)
        {
            _sfxVolume = value;
            ApplyAudioVolumes();
            SaveSettings();
        }

        public void UiSetGmEnabled(bool value)
        {
            _gmEnabled = value;
            SaveSettings();
        }

        private bool CanSubmitCurrentSelection()
        {
            if (_ability == AbilityKind.Move) return _selectedCoord.HasValue;
            if (_ability == AbilityKind.Undo || _ability == AbilityKind.SelectAll)
                return IsSkillEnabledForCurrentTarget(_ability);
            if (_ability == AbilityKind.Copy)
                return _selectedTarget.HasValue && _selectedCoord.HasValue &&
                       IsSkillEnabledForCurrentTarget(_ability);
            if (_ability == AbilityKind.Connect)
                return _selectedTarget.HasValue && _selectedSecondaryTarget.HasValue &&
                       IsSkillEnabledForCurrentTarget(_ability);
            return _selectedTarget.HasValue && IsSkillEnabledForCurrentTarget(_ability);
        }

        private bool IsSkillEnabledForCurrentTarget(AbilityKind skill)
        {
            if (_service == null) return false;
            if (skill == AbilityKind.Move) return true;
            RunView view = _service.CurrentView;
            int focus = _serverOnline && _serverRun != null ? _serverRun.focus : view.Focus;
            int focusCost = skill == AbilityKind.Copy || skill == AbilityKind.Delete || skill == AbilityKind.Search ? 1
                : skill == AbilityKind.Connect || skill == AbilityKind.Restore ? 2
                : skill == AbilityKind.Undo || skill == AbilityKind.SelectAll ? 3
                : skill == AbilityKind.Interact ? 0 : int.MaxValue;
            if (focus < focusCost) return false;
            if (skill == AbilityKind.Undo)
            {
                if (_serverOnline) return _serverRun != null && _serverRun.currentTurn >= 2;
                RunState state = _service.CreateSnapshot();
                int available = 0;
                for (int i = state.ReversibleHistory.Count - 1; i >= 0 && available < 2; i--)
                    if (!state.ReversibleHistory[i].IsConsumed) available++;
                return available >= 2;
            }
            if (skill == AbilityKind.SelectAll)
            {
                if (!TryGetPlayerPosition(view, out GridCoord playerPosition)) return false;
                if (_serverOnline && _serverRun?.entities != null)
                {
                    for (int i = 0; i < _serverRun.entities.Length; i++)
                    {
                        GameApiClient.EntitySnapshot entity = _serverRun.entities[i];
                        if (entity?.position == null || !string.Equals(entity.kind, "enemy", StringComparison.OrdinalIgnoreCase) ||
                            entity.state == null || entity.state.hp <= 0 || entity.state.disabled ||
                            entity.state.defeated || entity.state.fled)
                            continue;
                        var position = new GridCoord(entity.position.x, entity.position.y);
                        if (playerPosition.ManhattanDistance(position) <= 4) return true;
                    }
                    return false;
                }
                for (int i = 0; i < view.Entities.Count; i++)
                    if (view.Entities[i].IsHostile && view.Entities[i].Kind == EntityKind.Enemy &&
                        view.Entities[i].Health > 0 && playerPosition.ManhattanDistance(view.Entities[i].Position) <= 4)
                        return true;
                return false;
            }
            if (!_selectedTarget.HasValue)
                return true;
            if (TryGetServerEntity(_selectedTarget, out GameApiClient.EntitySnapshot serverTarget))
            {
                bool isPlayer = string.Equals(serverTarget.id, _serverRun.playerEntityId, StringComparison.OrdinalIgnoreCase);
                bool isEnemy = string.Equals(serverTarget.kind, "enemy", StringComparison.OrdinalIgnoreCase);
                bool active = serverTarget.state == null ||
                              (!serverTarget.state.disabled && !serverTarget.state.defeated && !serverTarget.state.fled);
                if (skill == AbilityKind.Copy)
                    return !isPlayer && !string.Equals(serverTarget.kind, "npc", StringComparison.OrdinalIgnoreCase) &&
                           serverTarget.cloneable && !serverTarget.@protected && serverTarget.position != null &&
                           TryGetPlayerPosition(view, out GridCoord copyPosition) &&
                           copyPosition.ManhattanDistance(new GridCoord(serverTarget.position.x, serverTarget.position.y)) <= 4 &&
                           (!_selectedCoord.HasValue || copyPosition.ManhattanDistance(_selectedCoord.Value) <= 4);
                if (skill == AbilityKind.Delete)
                    return isEnemy && active && (serverTarget.state == null || serverTarget.state.hp > 0) &&
                           serverTarget.position != null && TryGetPlayerPosition(view, out GridCoord deletePosition) &&
                           deletePosition.ManhattanDistance(new GridCoord(serverTarget.position.x, serverTarget.position.y)) <= 3;
                if (skill == AbilityKind.Search)
                    return serverTarget.position != null && TryGetPlayerPosition(view, out GridCoord searchPosition) &&
                           searchPosition.ManhattanDistance(new GridCoord(serverTarget.position.x, serverTarget.position.y)) <= 6;
                if (skill == AbilityKind.Connect)
                {
                    if (isEnemy || serverTarget.position == null) return false;
                    if (!_selectedSecondaryTarget.HasValue) return true;
                    if (!TryGetServerEntity(_selectedSecondaryTarget, out GameApiClient.EntitySnapshot serverSecondary) ||
                        serverSecondary.position == null || string.Equals(serverSecondary.kind, "enemy", StringComparison.OrdinalIgnoreCase))
                        return false;
                    var firstPosition = new GridCoord(serverTarget.position.x, serverTarget.position.y);
                    var secondPosition = new GridCoord(serverSecondary.position.x, serverSecondary.position.y);
                    return TryGetPlayerPosition(view, out GridCoord connectPosition) &&
                           (connectPosition.ManhattanDistance(firstPosition) <= 4 ||
                            connectPosition.ManhattanDistance(secondPosition) <= 4) &&
                           firstPosition.ManhattanDistance(secondPosition) <= 12;
                }
                if (skill == AbilityKind.Restore)
                    return serverTarget.position != null && TryGetPlayerPosition(view, out GridCoord restorePosition) &&
                           restorePosition.ManhattanDistance(new GridCoord(serverTarget.position.x, serverTarget.position.y)) <= 4 &&
                           (_lastRestorableTarget == _selectedTarget ||
                            (serverTarget.assetId ?? string.Empty).StartsWith("story.admin-access", StringComparison.Ordinal));
                if (skill == AbilityKind.Interact)
                    return !isEnemy && !isPlayer && serverTarget.position != null &&
                           (string.Equals(serverTarget.kind, "prop", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(serverTarget.kind, "npc", StringComparison.OrdinalIgnoreCase)) &&
                           TryGetPlayerPosition(view, out GridCoord interactPosition) &&
                           interactPosition.ManhattanDistance(new GridCoord(serverTarget.position.x, serverTarget.position.y)) <= 2;
            }
            for (int i = 0; i < view.Entities.Count; i++)
            {
                EntityView target = view.Entities[i];
                if (target.EntityId != _selectedTarget.Value) continue;
                if (skill == AbilityKind.Copy)
                    return target.Kind != EntityKind.Player && target.Kind != EntityKind.Npc &&
                           target.IsCloneable && !target.IsProtected &&
                           TryGetPlayerPosition(view, out GridCoord copyPosition) &&
                           copyPosition.ManhattanDistance(target.Position) <= 4 &&
                           (!_selectedCoord.HasValue || copyPosition.ManhattanDistance(_selectedCoord.Value) <= 4);
                if (skill == AbilityKind.Delete)
                    return target.Kind == EntityKind.Enemy && target.IsHostile && target.Health > 0 &&
                           TryGetPlayerPosition(view, out GridCoord deletePosition) &&
                           deletePosition.ManhattanDistance(target.Position) <= 3;
                if (skill == AbilityKind.Search)
                    return TryGetPlayerPosition(view, out GridCoord searchPosition) &&
                           searchPosition.ManhattanDistance(target.Position) <= 6;
                if (skill == AbilityKind.Connect)
                {
                    if (target.IsHostile) return false;
                    EntityView secondary = SelectedEntity(view, _selectedSecondaryTarget);
                    if (secondary == null) return true;
                    return !secondary.IsHostile && TryGetPlayerPosition(view, out GridCoord connectPosition) &&
                           (connectPosition.ManhattanDistance(target.Position) <= 4 ||
                            connectPosition.ManhattanDistance(secondary.Position) <= 4) &&
                           target.Position.ManhattanDistance(secondary.Position) <= 12;
                }
                if (skill == AbilityKind.Restore)
                    return TryGetPlayerPosition(view, out GridCoord restorePosition) &&
                           restorePosition.ManhattanDistance(target.Position) <= 4 &&
                           (_lastRestorableTarget == target.EntityId ||
                            target.AssetId.StartsWith("story.admin-access", StringComparison.Ordinal));
                if (skill == AbilityKind.Interact)
                {
                    if ((target.Kind != EntityKind.Prop && target.Kind != EntityKind.Npc) || target.IsHostile)
                        return false;
                    return TryGetPlayerPosition(view, out GridCoord playerPosition) &&
                           playerPosition.ManhattanDistance(target.Position) <= 2;
                }
            }
            return true;
        }

        private string SelectionStatusDetail(RunView view)
        {
            int focus = _serverOnline && _serverRun != null ? _serverRun.focus : view.Focus;
            int cost = _ability == AbilityKind.Copy || _ability == AbilityKind.Delete || _ability == AbilityKind.Search ? 1
                : _ability == AbilityKind.Connect || _ability == AbilityKind.Restore ? 2
                : _ability == AbilityKind.Undo || _ability == AbilityKind.SelectAll ? 3 : 0;
            if (focus < cost) return "사용 불가 · 포커스 " + cost + " 필요 (현재 " + focus + ")";
            if (_ability == AbilityKind.Undo)
                return IsSkillEnabledForCurrentTarget(_ability)
                    ? "직전 의미 턴 2개를 역순으로 복구합니다.\n" + SelectionExecutionSummary()
                    : "사용 불가 · 되돌릴 수 있는 의미 턴이 2개 필요합니다.";
            if (_ability == AbilityKind.SelectAll)
                return IsSkillEnabledForCurrentTarget(_ability)
                    ? "사용 가능 · 반경 4칸의 모든 적에게 피해 3\n" + SelectionExecutionSummary()
                    : "사용 불가 · 반경 4칸 안에 적이 없습니다.";
            if (_ability == AbilityKind.Move)
                return _selectedCoord.HasValue
                    ? "목적지 " + _selectedCoord.Value + "\n" + SelectionExecutionSummary()
                    : "지도에서 이동할 빈 타일을 선택하세요.";

            EntityView target = SelectedEntity(view, _selectedTarget);
            if (target == null && TryGetServerEntity(_selectedTarget, out GameApiClient.EntitySnapshot serverTarget))
                return ServerSelectionStatusDetail(view, serverTarget);
            if (target == null)
                return _ability == AbilityKind.Connect ? "첫 번째와 두 번째 대상을 차례로 선택하세요."
                    : _ability == AbilityKind.Copy ? "복제할 오브젝트를 선택하세요. 조사 효과는 없습니다."
                    : _ability == AbilityKind.Search ? "조사할 대상 하나를 선택하세요."
                    : _ability == AbilityKind.Delete ? "공격할 적 하나를 선택하세요."
                    : "대상을 선택하세요.";

            int distance = TryGetPlayerPosition(view, out GridCoord playerPosition)
                ? playerPosition.ManhattanDistance(target.Position) : -1;
            string targetLine = target.DisplayName + " · " + target.Kind +
                                (target.MaxHealth > 0 ? " · HP " + target.Health + "/" + target.MaxHealth : string.Empty) +
                                (distance >= 0 ? " · 거리 " + distance : string.Empty);
            if (_ability == AbilityKind.Connect && _selectedSecondaryTarget.HasValue)
                targetLine += "\n연결 대상 · " + DisplayEntityName(view, _selectedSecondaryTarget.Value);
            if (!IsSkillEnabledForCurrentTarget(_ability)) return "사용 불가 · " + targetLine;
            if (_ability == AbilityKind.Copy && !_selectedCoord.HasValue)
                return targetLine + "\n원본은 유지됩니다. 복제본을 놓을 빈 타일을 선택하세요.";
            if (_ability == AbilityKind.Connect && !_selectedSecondaryTarget.HasValue)
                return targetLine + "\n두 번째 연결 대상을 선택하세요.";
            return "선택됨 · " + targetLine + "\n" + SelectionExecutionSummary();
        }

        private string ObjectiveSelectionContext(RunView view, GameApiClient.EntitySnapshot selected)
        {
            if (_ability != AbilityKind.Search || selected == null ||
                !TryGetServerObjectiveTarget(view, out GameApiClient.EntitySnapshot objective, out _))
                return string.Empty;
            if (string.Equals(selected.id, objective.id, StringComparison.OrdinalIgnoreCase))
                return "\n현재 이야기 목표";
            return "\n조사 가능 · 현재 이야기 목표는 " +
                   FirstNonEmpty(objective.name, objective.assetId, "표시된 대상") + "입니다.";
        }

        private string ServerSelectionStatusDetail(RunView view, GameApiClient.EntitySnapshot target)
        {
            int distance = target.position != null && TryGetPlayerPosition(view, out GridCoord playerPosition)
                ? playerPosition.ManhattanDistance(new GridCoord(target.position.x, target.position.y)) : -1;
            int health = target.state?.hp ?? 0;
            int maxHealth = target.state?.maxHp ?? 0;
            string kind = string.Equals(target.kind, "npc", StringComparison.OrdinalIgnoreCase) ? "NPC"
                : string.Equals(target.kind, "enemy", StringComparison.OrdinalIgnoreCase) ? "적"
                : string.Equals(target.kind, "prop", StringComparison.OrdinalIgnoreCase) ? "오브젝트"
                : string.IsNullOrWhiteSpace(target.kind) ? "대상" : target.kind;
            string targetLine = FirstNonEmpty(target.name, target.assetId, "이름 없는 대상") + " · " + kind +
                                (maxHealth > 0 ? " · HP " + health + "/" + maxHealth : string.Empty) +
                                (distance >= 0 ? " · 거리 " + distance : string.Empty);
            if (_ability == AbilityKind.Connect && _selectedSecondaryTarget.HasValue)
                targetLine += "\n연결 대상 · " + DisplayEntityName(view, _selectedSecondaryTarget.Value);
            if (!IsSkillEnabledForCurrentTarget(_ability)) return "사용 불가 · " + targetLine;
            if (_ability == AbilityKind.Copy && !_selectedCoord.HasValue)
                return targetLine + "\n원본은 유지됩니다. 복제본을 놓을 빈 타일을 선택하세요.";
            if (_ability == AbilityKind.Connect && !_selectedSecondaryTarget.HasValue)
                return targetLine + "\n두 번째 연결 대상을 선택하세요.";
            string objectiveContext = ObjectiveSelectionContext(view, target);
            return "선택됨 · " + targetLine + objectiveContext + "\n" + SelectionExecutionSummary();
        }

        private string SelectionExecutionSummary()
        {
            Mouse mouse = Mouse.current;
            if (_debugTourActive || _showPause || _playerWalking || mouse == null || !mouse.leftButton.wasPressedThisFrame || _camera == null)
                return;

            Vector2 mousePosition = mouse.position.ReadValue();
            Vector2 logicalGuiPosition = new Vector2(
                mousePosition.x * LogicalWidth / Mathf.Max(1f, Screen.width),
                (Screen.height - mousePosition.y) * LogicalHeight / Mathf.Max(1f, Screen.height));
            if (_neopjukiWaveFrames.Length > 0 && NeopjukiTestPanelRect.Contains(logicalGuiPosition))
                return;
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
                return;
            if (!_camera.pixelRect.Contains(mousePosition))
                return;

            Vector3 world = _camera.ScreenToWorldPoint(new Vector3(mousePosition.x, mousePosition.y, 0f));
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
                clickedEntity = _serverOnline ? FindServerTarget(coord, _ability == AbilityKind.Restore) : FindTarget(view, coord);
            if (clickedEntity.HasValue && TryGetEntityPosition(view, clickedEntity.Value, out GridCoord entityCoord))
                coord = entityCoord;
            string abilityName = _ability.ToString();
            if (abilityName == "Move")
            {
                if (clickedEntity.HasValue && IsInteractableTarget(view, clickedEntity.Value))
                {
                    _ability = AbilityKind.Interact;
                    _selectedTarget = clickedEntity;
                    _selectedSecondaryTarget = null;
                    _selectedCoord = coord;
                    _movementSelectionFeedback = IsSkillEnabledForCurrentTarget(AbilityKind.Interact)
                        ? DisplayEntityName(view, clickedEntity.Value) + " 상호작용 준비 완료"
                        : "상호작용하려면 대상의 2칸 이내로 이동하세요.";
                    UpdateSelectionVisual(view);
                    PlaySfx(AssetClip("UiMoveSound"));
                    return;
                }
                if (!CanSelectMovementDestination(view, coord))
                {
                    _movementSelectionFeedback = "그곳까지 이어지는 통행 가능한 경로가 없습니다.";
                    PlaySfx(AssetClip("UiCancelSound"));
                    return;
                }
                _movementSelectionFeedback = string.Empty;
                _selectedCoord = coord;
                _selectedTarget = null;
                _selectedSecondaryTarget = null;
            }
            else if (abilityName == "Copy")
            {
                if (clickedEntity.HasValue)
                {
                    _selectedTarget = clickedEntity;
                    _selectedCoord = null;
                    _copySourceCaptured = true;
                    _movementSelectionFeedback = "복사 원본 선택 · " + DisplayEntityName(view, clickedEntity.Value) +
                                                 " · 이제 빈 타일을 클릭하세요.";
                }
                else
                {
                    _selectedCoord = coord;
                    _movementSelectionFeedback = _copySourceCaptured
                        ? "붙여넣을 타일 선택 완료 · 우측 아래 실행 버튼을 누르세요."
                        : "먼저 복사할 적 또는 오브젝트를 클릭하세요.";
                }
            }
            else if (abilityName == "Connect")
            {
                if (clickedEntity.HasValue)
                {
                    if (!_selectedTarget.HasValue || _selectedTarget.Value == clickedEntity.Value)
                    {
                        _selectedTarget = clickedEntity;
                        _selectedSecondaryTarget = null;
                    }
                    else
                    {
                        _selectedSecondaryTarget = clickedEntity;
                    }
                    _movementSelectionFeedback = _selectedSecondaryTarget.HasValue
                        ? "연결 대상 2개 선택 완료 · 실행 버튼을 누르세요."
                        : "첫 연결 대상 · " + DisplayEntityName(view, clickedEntity.Value) + " · 두 번째 대상을 고르세요.";
                }
                _selectedCoord = coord;
            }
            else if (abilityName != "Undo")
            {
                _selectedCoord = coord;
                _selectedTarget = clickedEntity;
                _selectedSecondaryTarget = null;
                _movementSelectionFeedback = clickedEntity.HasValue
                    ? DisplayEntityName(view, clickedEntity.Value) + " 선택 완료 · 우측 아래 실행 버튼을 누르세요."
                    : "선택 가능한 적 또는 오브젝트의 모습을 직접 클릭하세요.";
            }

            UpdateSelectionVisual(view);
            PlaySfx(AssetClip("UiMoveSound"));
        }

        private void CyclePoi(int direction)
        {
            if (_service == null) return;
            var coordinates = new List<GridCoord>();
            var labels = new List<string>();
            if (TryGetServerObjectiveTarget(_service.CurrentView,
                    out GameApiClient.EntitySnapshot objectiveTarget, out GridCoord objectiveCoord))
            {
                coordinates.Add(objectiveCoord);
                labels.Add("현재 목표 · " + objectiveTarget.name);
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
                _poiLabel = "표시할 POI 없음";
                return;
            }
            _poiCursor = (_poiCursor + (direction < 0 ? -1 : 1)) % coordinates.Count;
            if (_poiCursor < 0) _poiCursor += coordinates.Count;
            _selectedCoord = coordinates[_poiCursor];
            _selectedTarget = null;
            _selectedSecondaryTarget = null;
            _cameraInspectCoord = _selectedCoord;
            _cameraInspectUntil = Time.unscaledTime + 6f;
            _poiLabel = (_poiCursor + 1) + "/" + coordinates.Count + " " + labels[_poiCursor];
            UpdateSelectionVisual(_service.CurrentView);
            PlaySfx(AssetClip("UiMoveSound"));
        }

        private void Submit()
        {
            if (_service == null || _showPause || _serverPending || _playerWalking ||
                (_sceneSequencePlayer != null && _sceneSequencePlayer.IsPlaying))
                return;
            RunView view = _service.CurrentView;
            if (!RunIsPlaying(view))
                return;
            if (!CanSubmitCurrentSelection())
            {
                _lastOutcome = "SELECTION REQUIRED";
                _lastAttempt = NarrativeSelectionHint();
                _lastExplanation = "목적지 또는 유효한 대상을 선택하기 전에는 턴을 소비하지 않습니다.";
                _lastNarrative = "먼저 지도에서 이동할 타일이나 스킬을 적용할 대상을 선택하세요.";
                AddLog("실행 대기 · " + _lastAttempt);
                PlaySfx(AssetClip("UiCancelSound"));
                PublishPresentationState(PresentationChange.Hud | PresentationChange.Dialogue);
                return;
            }

            if (_serverOnline && _serverRun != null && _gameApi != null)
            {
                if (_ability == AbilityKind.Move)
                    StartCoroutine(SubmitServerTravel());
                else
                    StartCoroutine(SubmitServerAction());
                return;
            }

            StartCoroutine(SubmitLocalTurn());
        }

        private IEnumerator SubmitLocalTurn()
        {
            RunView view = _service.CurrentView;
            TryGetPlayerPosition(view, out GridCoord playerPositionBefore);

            string abilityName = _ability.ToString();
            GridCoord? destination = abilityName == "Move" || abilityName == "Copy" || abilityName == "Connect"
                ? _selectedCoord
                : null;
            Guid? target = abilityName == "Move" || abilityName == "Undo" || abilityName == "SelectAll" ? null : _selectedTarget;
            Guid? secondary = abilityName == "Connect" ? _selectedSecondaryTarget : null;
            TurnRequest request = _ability == AbilityKind.Move && destination.HasValue
                ? TurnRequest.Move(Guid.NewGuid().ToString("N"), view.Version, destination.Value)
                : TurnRequest.UseSkill(Guid.NewGuid().ToString("N"), view.Version, _ability, target,
                    secondary, destination);
            TurnGatewayResult gatewayResult = null;
            yield return _turnGateway.Submit(request, value => gatewayResult = value);
            TurnResponse response = gatewayResult?.LocalResponse;
            if (response == null)
            {
                _lastOutcome = gatewayResult?.ErrorCode ?? "GATEWAY_ERROR";
                _lastAttempt = gatewayResult?.ErrorMessage ?? "턴 게이트웨이에서 응답을 받지 못했습니다.";
                _lastExplanation = "요청이 커밋되지 않아 턴과 저장 상태는 변하지 않았습니다.";
                PublishPresentationState(PresentationChange.Hud | PresentationChange.Dialogue);
                yield break;
            }
            if (!response.IsSuccess)
            {
                _lastOutcome = response.ErrorCode.ToString();
                _lastAttempt = response.ErrorMessage ?? "행동이 거부되었습니다.";
                _lastExplanation = "서버 규칙과 같은 로컬 폴백 검증에서 거부되어 턴은 소비되지 않았습니다.";
                _lastNarrative = "대상, 목적지, 자원 조건을 다시 확인한 뒤 스킬을 다시 선택하세요.";
                AddLog("행동 거부 · " + _lastAttempt);
                PlaySfx(AssetClip("UiCancelSound"));
                PublishPresentationState(PresentationChange.Hud | PresentationChange.Dialogue);
                yield break;
            }

            _lastD20 = response.D20;
            PresentDiceRoll(response.D20);
            SyncLocalEncounterState(response.Run);
            _lastModifier = response.Modifier;
            _lastDifficulty = response.Difficulty;
            _lastMechanicalScore = response.MechanicalScore;
            _lastActionContext = CampaignCatalog.ContextLabel(response.ActionContext);
            _lastModifierBreakdown = "로컬 능력·상태 합계 " + Signed(response.Modifier);
            _lastOutcome = KoreanOutcome(response.Outcome);
            _lastAttempt = response.NormalizedAttempt;
            _lastExplanation = response.OutcomeExplanation + " · " + _lastModifierBreakdown;
            _lastNarrative = response.Narrative;
            _lastStateChanges = StateChangeSummary(response.Events);
            _lastDialogue = Array.Empty<string>();
            _playerActionUntil = Time.unscaledTime +
                (response.ActionContext == ActionContext.Combat ? 0.5f : 0.22f);
            AddLog("D20 " + response.D20 + " · " + _lastOutcome + " · " + response.NormalizedAttempt);
            for (int i = 0; i < response.Events.Count; i++)
                AddLog(HumanizeEvent(response.Events[i]));
            CaptureLocalRestorableTarget(response, view);

            RunView committedView = response.Run ?? _service.CurrentView;
            if (_ability == AbilityKind.Move)
            {
                List<GridCoord> path = FindLocalVisualPath(committedView, playerPositionBefore,
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

            if (_gmEnabled && _narrativeClient != null)
            {
                int generation = _runGeneration;
                int turn = response.TurnNo;
                _gmPending = true;
                StartCoroutine(_narrativeClient.RequestNarrative(_service.CurrentView, response, _ability,
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

            _selectedTarget = null;
            _selectedSecondaryTarget = null;
            _selectedCoord = _encounterMoveRequired ? _encounterStagingCoord : null;
            UpdateSelectionVisual(_service.CurrentView);

            if (_service.CurrentView.Status != RunStatus.Playing)
            {
                _gmPending = false;
                PlaySfx(AssetClip("SuccessJingle"));
            }
            PublishPresentationState(PresentationChange.Hud | PresentationChange.Dialogue |
                                     PresentationChange.Minimap | PresentationChange.Selection);
        }

        private IEnumerator SubmitServerAction()
        {
            EnsureRuntimeClients();
            _serverPending = true;
            _gmPending = true;
            _serverStatus = "권위 턴 커밋 중";

            GridCoord? destinationCoord = _ability == AbilityKind.Copy
                ? _selectedCoord
                : null;
            string requestId = "unity-" + Guid.NewGuid().ToString("N");
            GameApiClient.RunSnapshot runBeforeSubmit = _serverRun;
            int turnBeforeSubmit = _serverRun.currentTurn;
            Guid? target = _ability == AbilityKind.Undo || _ability == AbilityKind.Search ||
                           _ability == AbilityKind.SelectAll ? null : _selectedTarget;
            Guid? secondary = _ability == AbilityKind.Connect ? _selectedSecondaryTarget : null;
            var request = TurnRequest.UseSkill(requestId, _serverRun.version, _ability, target, secondary, destinationCoord);
            TurnGatewayResult gatewayResult = null;
            yield return _turnGateway.Submit(request, value => gatewayResult = value);
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
                    _selectedCoord = _encounterStagingCoord;
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
                _lastOutcome = gatewayResult?.ErrorCode ?? "NETWORK_ERROR";
                _lastAttempt = gatewayResult?.ErrorMessage ?? "권위 서버에서 응답을 받지 못했습니다.";
                _lastExplanation = gatewayResult?.ErrorCode == "RUN_VERSION_CONFLICT"
                    ? "다른 상태가 먼저 커밋되어 최신 런을 다시 동기화합니다. 선택한 행동은 자동 재실행하지 않습니다."
                    : "서버가 거부한 요청은 턴을 소비하지 않습니다. 로컬에서 임의 판정을 대신하지 않았습니다.";
                _serverStatus = "턴 거부 · " + _lastOutcome;
                PlaySfx(AssetClip("UiCancelSound"));
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
            CaptureRestorableTarget(committed.Turn, runBeforeSubmit);
            _serverRun = committed.Run;
            SyncEncounterStateFromServer();
            SyncRestorableCandidateFromServer();
            ApplyServerTurnPresentation(committed.Turn);
            SyncServerEntityVisuals(_serverRun);
            QueueServerSceneSequence(committed.Turn.sceneSequence);
            UpdateSelectionVisual(_service.CurrentView);
            PlayerPrefs.SetString(ServerRunIdKey, _serverRun.id);
            PlayerPrefs.Save();

            _selectedTarget = null;
            _selectedSecondaryTarget = null;
            if (_ability != AbilityKind.Move)
                _selectedCoord = null;
            UpdateSelectionVisual(_service.CurrentView);
            _serverStatus = committed.FromIdempotencyCache ? "멱등 응답 재생" : "권위 상태 커밋 완료";
            PlaySfx(_lastD20 == 20 ? AssetClip("SuccessJingle") : AssetClip("UiAcceptSound"));
        }

        private IEnumerator SubmitServerTravel()
        {
            EnsureRuntimeClients();
            if (!_selectedCoord.HasValue)
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
            GridCoord destinationCoord = _selectedCoord.Value;
            TryGetPlayerPosition(_service.CurrentView, out GridCoord playerPositionBefore);
            string requestId = "unity-travel-" + Guid.NewGuid().ToString("N");
            int campaignTurnBefore = _serverRun.currentTurn;
            string layoutHashBefore = _serverRun.world?.layoutHash;
            TurnGatewayResult gatewayResult = null;
            yield return _turnGateway.Submit(TurnRequest.Move(requestId, _serverRun.version, destinationCoord),
                value => gatewayResult = value);

            _serverPending = false;
            if (gatewayResult == null || !gatewayResult.IsSuccess)
            {
                _lastOutcome = gatewayResult?.ErrorCode ?? "NETWORK_ERROR";
                _lastAttempt = gatewayResult?.ErrorMessage ?? "안전 탐색 응답을 받지 못했습니다.";
                _lastExplanation = "이동이 거부되어 위치·D20·의미 있는 캠페인 턴은 변하지 않았습니다.";
                _serverStatus = "탐색 이동 거부 · " + _lastOutcome;
                PlaySfx(AssetClip("UiCancelSound"));
                if (gatewayResult == null || gatewayResult.ErrorCode == "NETWORK_ERROR" || gatewayResult.ErrorCode == "RUN_VERSION_CONFLICT")
                    yield return ResyncServerRun();
                yield break;
            }

            GameApiClient.CommittedNavigation committed = gatewayResult.Payload as GameApiClient.CommittedNavigation;
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
            _lastD20 = 0;
            _lastModifier = 0;
            _lastModifierBreakdown = "탐색 이동에는 판정 수정치 없음";
            _lastDifficulty = 0;
            _lastMechanicalScore = 0;
            _lastActionContext = "안전 이동";
            _lastOutcome = encounterOpened ? "사건 발견" : invariantHeld ? "안전 이동" : "이동 상태 확인";
            _lastAttempt = "고정 월드의 (" + destinationCoord.X + ", " + destinationCoord.Y + ")까지 안전 경로로 이동";
            _lastExplanation = encounterOpened
                ? "서버가 안전 구간 이동 뒤 사건을 열었습니다. 아직 D20과 의미 있는 턴은 쓰지 않았으며 다음 사건 행동이 이를 소비합니다."
                : invariantHeld
                ? "서버 /travel이 위치와 탐색 시간만 갱신했습니다. D20과 의미 있는 캠페인 턴은 소비하지 않았습니다."
                : "서버 이동 결과를 다시 동기화했습니다. 캠페인 턴 또는 레이아웃 불변식 표시를 확인하세요.";
            string actorName = PlayerDisplayName(_service.CurrentView);
            _lastNarrative = _gmEnabled && !string.IsNullOrWhiteSpace(navigation?.narrative?.body)
                ? navigation.narrative.body
                : encounterOpened
                    ? actorName + "는(은) 안전 구간 끝에서 " + EncounterReasonLabel(_encounterReason) + " 사건과 마주쳤다. 이제 배치·전투·조사·협상 중 하나로 해결해야 한다."
                    : actorName + "는(은) 이미 생성된 월드 안에서 안전 경로를 따라 이동했다. 사건이 시작되기 전까지 세계 지형은 바뀌지 않는다.";
            _lastNarrativeFallbackUsed = navigation?.narrative == null || navigation.narrative.fallbackUsed;
            _lastNarrativeModel = navigation?.narrative?.model ?? "deterministic";
            _lastStateChanges = navigation?.events != null && navigation.events.Length > 0
                ? StateChangeSummary(navigation.events)
                : encounterOpened
                    ? "위치: 안전 지점까지 이동 · 사건 활성화 · 캠페인 턴 유지"
                    : "위치와 이동 시간만 변경 · 캠페인 턴 유지 · D20 없음";
            _lastDialogue = _gmEnabled ? navigation?.narrative?.dialogue ?? Array.Empty<string>() : Array.Empty<string>();
            SyncRestorableCandidateFromServer();
            TryGetPlayerPosition(_service.CurrentView, out GridCoord playerPositionAfter);
            List<GridCoord> visualPath = NavigationVisualPath(navigation, playerPositionBefore, playerPositionAfter);
            BeginPlayerPathAnimation(visualPath, _service.CurrentView);
            SyncServerEntityVisuals(_serverRun);
            QueueServerSceneSequence(navigation?.sceneSequence);
            _selectedCoord = encounterOpened ? _encounterStagingCoord : null;
            _selectedTarget = null;
            _selectedSecondaryTarget = null;
            UpdateSelectionVisual(_service.CurrentView);
            PlayerPrefs.SetString(ServerRunIdKey, _serverRun.id);
            PlayerPrefs.Save();
            int pathCost = navigation?.pathCost ?? 0;
            AddLog("안전 탐색 · 비용 " + pathCost + " · 의미 턴 " + campaignTurnBefore + " 유지 · D20 없음");
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
            int counter = PlayerPrefs.GetInt("keyboard-wanderer.run-counter", 0) + 1;
            PlayerPrefs.SetInt("keyboard-wanderer.run-counter", counter);
            PlayerPrefs.Save();
            long seed = 20260717L + counter;
            StartCoroutine(StartServerOrLocalRun(seed));
        }

        private void ContinueRun()
        {
            if (_serverPending)
                return;
            StartCoroutine(ContinueServerOrLocalRun());
        }

        private IEnumerator StartServerOrLocalRun(long seed)
        {
            EnsureRuntimeClients();
            _serverPending = true;
            _serverStatus = "권위 서버 확인 중";
            GameApiClient.Result<bool> health = null;
            yield return _gameApi.CheckHealth(value => health = value);
            if (health == null || !health.IsSuccess)
            {
                _serverPending = false;
                _serverOnline = false;
                _serverCampaign = null;
                _serverRun = null;
                _serverStatus = "서버 미실행 · 로컬 연속성 폴백";
                ClearServerRunPointer();
                StartRun(LocalTurnService.CreateDemo(seed), false);
                yield break;
            }

            _serverStatus = "Seed 기반 캠페인 생성 중";
            GameApiClient.Result<GameApiClient.CampaignSnapshot> campaign = null;
            yield return _gameApi.CreateCampaign(seed, LocalTurnService.CampaignTurnLimit, value => campaign = value);
            if (campaign == null || !campaign.IsSuccess)
            {
                _serverPending = false;
                _serverOnline = false;
                _serverStatus = "캠페인 생성 실패 · 로컬 연속성 폴백";
                ClearServerRunPointer();
                StartRun(LocalTurnService.CreateDemo(seed), false);
                yield break;
            }

            _serverStatus = "권위 런 초기화 중";
            GameApiClient.Result<GameApiClient.RunSnapshot> run = null;
            yield return _gameApi.CreateRun(campaign.Value.id, value => run = value);
            _serverPending = false;
            if (run == null || !run.IsSuccess)
            {
                _serverOnline = false;
                _serverCampaign = null;
                _serverRun = null;
                _serverStatus = "런 생성 실패 · 로컬 연속성 폴백";
                ClearServerRunPointer();
                StartRun(LocalTurnService.CreateDemo(seed), false);
                yield break;
            }

            _serverOnline = true;
            _serverCampaign = campaign.Value;
            _serverRun = run.Value;
            SyncEncounterStateFromServer();
            _serverStatus = "권위 서버 연결 · layout " + ShortHash(_serverRun.world?.layoutHash);
            PlayerPrefs.SetString(ServerRunIdKey, _serverRun.id);
            PlayerPrefs.Save();
            StartRun(LocalTurnService.CreateDemo(seed), false);
        }

        private IEnumerator ContinueServerOrLocalRun()
        {
            EnsureRuntimeClients();
            _serverPending = true;
            _serverStatus = "저장된 권위 런 동기화 중";
            LocalTurnService restored = LocalRunSaveService.Load();
            string serverRunId = PlayerPrefs.GetString(ServerRunIdKey, string.Empty);
            if (!string.IsNullOrWhiteSpace(serverRunId))
            {
                GameApiClient.Result<GameApiClient.RunSnapshot> server = null;
                yield return _gameApi.GetRun(serverRunId, value => server = value);
                if (server != null && server.IsSuccess)
                {
                    if (string.Equals(server.Value.status, "abandoned", StringComparison.OrdinalIgnoreCase))
                    {
                        GameApiClient.Result<GameApiClient.RunSnapshot> resumedServer = null;
                        yield return _gameApi.ResumeRun(serverRunId, server.Value.version, value => resumedServer = value);
                        if (resumedServer != null && resumedServer.IsSuccess)
                            server = resumedServer;
                    }
                    _serverOnline = true;
                    _serverRun = server.Value;
                    SyncEncounterStateFromServer();
                    _serverCampaign = null;
                    _serverPending = false;
                    _serverStatus = "권위 런 재동기화 완료";
                    long seed = _serverRun.world != null ? _serverRun.world.worldSeed : 20260717L;
                    StartRun(restored ?? LocalTurnService.CreateDemo(seed), true);
                    yield break;
                }
            }

            _serverPending = false;
            _serverOnline = false;
            _serverRun = null;
            _serverCampaign = null;
            if (restored == null)
            {
                _serverStatus = "복원할 런 없음 · 새 로컬 폴백 시작";
                int counter = PlayerPrefs.GetInt("keyboard-wanderer.run-counter", 0) + 1;
                PlayerPrefs.SetInt("keyboard-wanderer.run-counter", counter);
                StartRun(LocalTurnService.CreateDemo(20260717L + counter), false);
            }
            else
            {
                _serverStatus = "서버 상태 없음 · 로컬 스냅샷 폴백";
                StartRun(restored, true);
            }
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
            _tutorialActive = PlayerPrefs.GetInt(TutorialCompletedKey, 0) == 0;
            _tutorialPage = 0;

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

            _ability = resumed ? AbilityKind.Move : FirstObjectiveAbility(_service.CurrentView);
            _selectedCoord = null;
            _selectedTarget = null;
            _selectedSecondaryTarget = null;
            _poiCursor = -1;
            _poiLabel = "POI 탐색";
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

        private static void ClearServerRunPointer()
        {
            PlayerPrefs.DeleteKey(ServerRunIdKey);
            PlayerPrefs.Save();
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
            foreach (KeyValuePair<Guid, EntityVisual> pair in _entityVisuals)
                ReleaseEntityVisual(pair.Value);
            _entityVisuals.Clear();
            if (rebuildStaticWorld)
            {
                ReleaseActiveDecorations();
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
            Tilemap tilemap;
            if (authoredWorld != null && authoredWorld.TerrainTilemap != null)
            {
                _worldRoot = authoredWorld.gameObject;
                _worldRoot.name = "Authored World · " + ShortHash(layoutHash);
                authoredWorld.ResetRuntimeContent();
                tilemap = authoredWorld.TerrainTilemap;
                tilemap.transform.position = new Vector3(origin.x, origin.y, 0f);
                _selectionRenderer = authoredWorld.SelectionCursor;
            }
            else
            {
                _worldRoot = new GameObject("Persistent World · " + ShortHash(layoutHash));
                _worldRoot.transform.SetParent(transform, false);
                _worldRoot.AddComponent<Grid>();
                var terrainObject = new GameObject("Immutable Terrain Tilemap");
                terrainObject.transform.SetParent(_worldRoot.transform, false);
                terrainObject.transform.position = new Vector3(origin.x, origin.y, 0f);
                tilemap = terrainObject.AddComponent<Tilemap>();
                var tilemapRenderer = terrainObject.AddComponent<TilemapRenderer>();
                tilemapRenderer.sortingOrder = 0;
            }
            var tilePalette = new Dictionary<string, Tile>(StringComparer.Ordinal);

            if (rebuildStaticWorld)
            {
                for (int y = 0; y < worldHeight; y++)
                {
                    var coord = new GridCoord(x, y);
                    TileKind tileKind = useServerWorld
                        ? TileKindForServer(serverWorld, serverWorld.tileCodes[y * serverWorld.width + x])
                        : view.Region.GetTile(coord).Kind;
                    string biomeId = BiomeIdAt(view, coord);
                    TileAppearance(tileKind, biomeId, coord, out Sprite sprite, out Color tint);
                    string paletteKey = biomeId + ":" + tileKind;
                    if (!tilePalette.TryGetValue(paletteKey, out Tile visualTile))
                    {
                        visualTile = ScriptableObject.CreateInstance<Tile>();
                        visualTile.name = "Runtime " + biomeId + " " + tileKind + " Tile";
                        visualTile.sprite = sprite;
                        visualTile.color = Color.white;
                        visualTile.flags = TileFlags.None;
                        tilePalette.Add(paletteKey, visualTile);
                        _runtimeTiles.Add(visualTile);
                    }
                }
                CreateBiomeDecorations(view, origin, useServerWorld, worldWidth, worldHeight);
                CreateCampaignLandmarkMarkers(view, origin, useServerWorld);
                _renderedLayoutHash = layoutHash;
            }

            CreateBiomeDecorations(view, origin, useServerWorld, worldWidth, worldHeight);
            CreateCampaignLandmarkMarkers(view, origin, useServerWorld);

            if (_selectionRenderer == null)
            {
                var selection = new GameObject("Selection Cursor");
                selection.transform.SetParent(WorldContentRoot, false);
                _selectionRenderer = selection.AddComponent<SpriteRenderer>();
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
                if (!_entityVisuals.TryGetValue(entity.EntityId, out EntityVisual visual))
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
            foreach (KeyValuePair<Guid, EntityVisual> pair in _entityVisuals)
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
                if (!_entityVisuals.TryGetValue(id, out EntityVisual visual))
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
                    visual = new EntityVisual
                    {
                        AuthoredView = authoredView,
                        Root = root,
                        Renderer = renderer,
                        IdleFrames = FramesForAsset(entity.assetId, entity.kind, isPlayer),
                        WalkFrames = isPlayer ? _playerWalkFrames : Array.Empty<Sprite>(),
                        WalkLeftFrames = isPlayer ? _playerWalkLeftFrames : Array.Empty<Sprite>(),
                        WalkUpFrames = isPlayer ? _neopjukiWalkUpFrames : Array.Empty<Sprite>(),
                        WalkDownFrames = isPlayer ? _neopjukiWalkDownFrames : Array.Empty<Sprite>(),
                        AttackFrames = isPlayer ? _playerAttackFrames : Array.Empty<Sprite>(),
                        TargetPosition = root.transform.position,
                        BaseColor = ServerEntityTint(entity),
                        DesiredSize = desired,
                        IsPlayer = isPlayer,
                        IsHostile = hostile
                    };
                    visual.Animator = ConfigureEntityAnimator(renderer,
                        AnimatorForAsset(entity.assetId, entity.kind, isPlayer));
                    InitializeAmbientWander(visual, id);
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
            foreach (KeyValuePair<Guid, EntityVisual> pair in _entityVisuals)
            {
                if (active.Contains(pair.Key))
                    continue;
                DestroyEntityVisual(pair.Value);
                removed.Add(pair.Key);
            }
            for (int i = 0; i < removed.Count; i++)
                _entityVisuals.Remove(removed[i]);
        }

        private void UpdateServerHealthVisual(EntityVisual visual, GameApiClient.EntitySnapshot entity)
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

        private EntityVisual CreateEntityVisual(EntityView entity, Vector2 origin)
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

            var visual = new EntityVisual
            {
                AuthoredView = authoredView,
                Root = root,
                Renderer = renderer,
                IdleFrames = FramesForEntity(entity),
                WalkFrames = isPlayer ? _playerWalkFrames : Array.Empty<Sprite>(),
                WalkLeftFrames = isPlayer ? _playerWalkLeftFrames : Array.Empty<Sprite>(),
                WalkUpFrames = isPlayer ? _neopjukiWalkUpFrames : Array.Empty<Sprite>(),
                WalkDownFrames = isPlayer ? _neopjukiWalkDownFrames : Array.Empty<Sprite>(),
                AttackFrames = isPlayer ? _playerAttackFrames : Array.Empty<Sprite>(),
                TargetPosition = root.transform.position,
                BaseColor = EntityTint(entity),
                DesiredSize = desired,
                IsPlayer = isPlayer,
                IsHostile = entity.IsHostile
            };
            visual.Animator = ConfigureEntityAnimator(renderer,
                AnimatorForAsset(entity.AssetId, entity.Kind.ToString(), isPlayer));
            InitializeAmbientWander(visual, entity.EntityId);
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
            KeyboardWandererEntityView prefab = authoringSettings != null ? authoringSettings.EntityVisualPrefab : null;
            if (prefab == null)
                throw new InvalidOperationException("Authoring Settings must reference an Entity Visual prefab.");

            while (_entityViewPool.Count > 0 && _entityViewPool.Peek() == null)
                _entityViewPool.Pop();
            authoredView = _entityViewPool.Count > 0 ? _entityViewPool.Pop() : Instantiate(prefab);
            authoredView.transform.SetParent(WorldContentRoot, false);
            authoredView.gameObject.SetActive(true);
            authoredView.name = objectName;
            authoredView.Prepare(_whiteSprite, hostile);
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

        private void ConfigureHealthBars(EntityVisual visual, string displayName)
        {
            if (visual.AuthoredView == null || visual.AuthoredView.HealthBack == null || visual.AuthoredView.HealthFill == null)
                throw new InvalidOperationException("Entity Visual prefab must contain authored Health Back and Health Fill renderers.");

            visual.HealthBack = visual.AuthoredView.HealthBack;
            visual.HealthFill = visual.AuthoredView.HealthFill;
            visual.HealthBack.name = displayName + " HP bg";
            visual.HealthFill.name = displayName + " HP";
        }

        private void DestroyEntityVisual(EntityVisual visual)
        {
            ReleaseEntityVisual(visual);
        }

        private void ReleaseEntityVisual(EntityVisual visual)
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
            EnsureVisualPoolRoot();
            view.gameObject.SetActive(false);
            view.transform.SetParent(_visualPoolRoot, false);
            _entityViewPool.Push(view);
        }

        private void EnsureVisualPoolRoot()
        {
            if (_visualPoolRoot != null) return;
            var root = new GameObject("Runtime Visual Pool");
            root.transform.SetParent(transform, false);
            root.SetActive(false);
            _visualPoolRoot = root.transform;
        }

        private static void DestroyDetachedVisual(GameObject item, GameObject root)
        {
            if (item == null)
                return;
            if (root == null || !item.transform.IsChildOf(root.transform))
                Destroy(item);
        }

        private void UpdateEntityHealthVisual(EntityVisual visual, EntityView entity)
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
            if (Time.unscaledTime >= _nextAnimationAt)
            {
                _animationFrame++;
                _nextAnimationAt = Time.unscaledTime + 0.16f;
            }

            float smoothing = 1f - Mathf.Exp(-12f * Time.unscaledDeltaTime);
            foreach (KeyValuePair<Guid, EntityVisual> pair in _entityVisuals)
            {
                EntityVisual visual = pair.Value;
                if (visual.Root == null)
                    continue;
                bool walkingThisFrame = visual.IsPlayer && _playerWalking;
                bool usesAnimator = visual.Animator != null && visual.Animator.runtimeAnimatorController != null;
                if (walkingThisFrame)
                {
                    Vector3 before = visual.Root.transform.position;
                    Vector3 remaining = visual.TargetPosition - before;
                    if (Mathf.Abs(remaining.x) > Mathf.Abs(remaining.y))
                        visual.Facing = new Vector2(Mathf.Sign(remaining.x), 0f);
                    else if (Mathf.Abs(remaining.y) > 0.0001f)
                        visual.Facing = new Vector2(0f, Mathf.Sign(remaining.y));
                    visual.Root.transform.position = Vector3.MoveTowards(before, visual.TargetPosition,
                        PlayerWalkSpeed * Time.unscaledDeltaTime);
                    float horizontal = visual.Root.transform.position.x - before.x;
                    float vertical = visual.Root.transform.position.y - before.y;
                    if (Mathf.Abs(vertical) > 0.0001f)
                    {
                        visual.FacingVertical = vertical > 0f ? 1 : -1;
                        visual.FacingLeft = false;
                        visual.Renderer.flipX = false;
                    }
                    else if (Mathf.Abs(horizontal) > 0.0001f)
                    {
                        visual.FacingVertical = 0;
                        visual.FacingLeft = horizontal < 0f;
                        visual.Renderer.flipX = visual.WalkLeftFrames.Length == 0 && visual.FacingLeft;
                    }
                    if (Vector3.SqrMagnitude(visual.Root.transform.position - visual.TargetPosition) < 0.0004f)
                    {
                        visual.Root.transform.position = visual.TargetPosition;
                        if (visual.MovementPath.Count > 0)
                            visual.TargetPosition = visual.MovementPath.Dequeue();
                        else
                            CompletePlayerPathAnimation();
                    }
                }
                else if (usesAnimator && !visual.IsPlayer)
                {
                    UpdateAmbientWander(visual);
                }
                else
                {
                    visual.Root.transform.position = Vector3.Lerp(visual.Root.transform.position, visual.TargetPosition, smoothing);
                }
                Sprite[] testFrames = visual.IsPlayer ? CurrentNeopjukiTestFrames() : Array.Empty<Sprite>();
                Sprite[] walkFrames = visual.FacingVertical > 0 && visual.WalkUpFrames.Length > 0
                    ? visual.WalkUpFrames
                    : visual.FacingVertical < 0 && visual.WalkDownFrames.Length > 0
                        ? visual.WalkDownFrames
                        : visual.FacingLeft && visual.WalkLeftFrames.Length > 0
                            ? visual.WalkLeftFrames
                            : visual.WalkFrames;
                Sprite[] attackFrames = CurrentNeopjukiAttackFrames(visual);
                Sprite[] frames = testFrames.Length > 0
                    ? testFrames
                    : walkingThisFrame && walkFrames.Length > 0
                        ? walkFrames
                        : visual.IsPlayer && Time.unscaledTime < _playerActionUntil && attackFrames.Length > 0
                            ? attackFrames
                            : visual.IdleFrames;
                if (testFrames.Length > 0 && visual.WalkLeftFrames.Length > 0)
                    visual.Renderer.flipX = false;
                if (frames.Length > 0)
                {
                    Sprite frame = frames[_animationFrame % frames.Length];
                    if (visual.Renderer.sprite != frame)
                    {
                        visual.Renderer.sprite = frame;
                        ScaleSprite(visual.AuthoredView != null ? visual.Renderer.transform : visual.Root.transform,
                            frame, visual.DesiredSize);
                    }
                }

                bool selected = (_selectedTarget.HasValue && pair.Key == _selectedTarget.Value) ||
                                (_selectedSecondaryTarget.HasValue && pair.Key == _selectedSecondaryTarget.Value);
                float pulse = 0.78f + Mathf.Sin(Time.unscaledTime * 7f) * 0.18f;
                Color selectionColor = _ability == AbilityKind.Delete
                    ? new Color(1f, 0.16f, 0.12f, 1f)
                    : _ability == AbilityKind.Restore
                        ? new Color(0.2f, 0.95f, 1f, 1f)
                        : new Color(1f, 0.82f, 0.25f, 1f);
                visual.Renderer.color = selected
                    ? Color.Lerp(visual.BaseColor, selectionColor, pulse)
                    : visual.BaseColor;
                if (visual.HealthBack != null)
                {
                    Vector3 position = visual.Root.transform.position + new Vector3(0f, 0.66f, -0.1f);
                    visual.HealthBack.transform.position = position;
                    float ratio = visual.HealthFill.transform.localScale.x / 0.74f;
                    visual.HealthFill.transform.position = position + new Vector3(-0.39f * (1f - ratio), 0f, -0.01f);
                }
                if (visual.RootComponentLabel != null)
                    visual.RootComponentLabel.transform.position = visual.Root.transform.position + new Vector3(0f, -0.72f, -0.2f);
            }
        }

        private Sprite[] DirectionalPlayerFrames(Vector2 facing, bool attacking, Sprite[] fallback)
        {
            Sprite[] selected;
            if (facing.y > 0.5f)
                selected = attacking ? _playerAttackUpFrames : _playerWalkUpFrames;
            else if (facing.y < -0.5f)
                selected = attacking ? _playerAttackDownFrames : _playerWalkDownFrames;
            else if (facing.x < -0.5f)
                selected = attacking ? _playerAttackLeftFrames : _playerWalkLeftFrames;
            else
                selected = attacking ? _playerAttackFrames : _playerWalkFrames;
            return selected != null && selected.Length > 0 ? selected : fallback ?? Array.Empty<Sprite>();
        }

        private void BeginPlayerPathAnimation(IReadOnlyList<GridCoord> path, RunView view)
        {
            if (path == null || path.Count < 2 || !TryGetPlayerVisual(view, out EntityVisual visual))
                return;
            visual.MovementPath.Clear();
            Vector2 origin = MapOrigin(view);
            for (int i = 1; i < path.Count; i++)
                visual.MovementPath.Enqueue(WorldPosition(origin, path[i]));
            if (visual.MovementPath.Count == 0)
                return;
            visual.TargetPosition = visual.MovementPath.Dequeue();
            _playerWalking = true;
            _reopenDialogueAfterWalk = !_authoredDialogueDismissed;
            _authoredDialogueDismissed = true;
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
                    _authoredDialogueDismissed = false;
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

        private IEnumerator PlayServerActorMove(GameApiClient.SceneActionSnapshot action)
        {
            if (!Guid.TryParse(action.actorId, out Guid actorId) ||
                !_entityVisuals.TryGetValue(actorId, out EntityVisual visual) || visual.Root == null ||
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
                !_entityVisuals.TryGetValue(actorId, out EntityVisual visual) || visual.Root == null)
            {
                if (!string.IsNullOrWhiteSpace(action.text)) AddLog("후속 장면 · " + action.text);
                yield return new WaitForSecondsRealtime(0.25f);
                yield break;
            }

            Vector3 origin = visual.TargetPosition;
            Vector3 lunge = Vector3.zero;
            if (Guid.TryParse(action.targetId, out Guid targetId) &&
                _entityVisuals.TryGetValue(targetId, out EntityVisual target) && target.Root != null)
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
                !_entityVisuals.TryGetValue(actorId, out EntityVisual visual) || visual.Root == null)
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
                _authoredDialogueDismissed = false;
                _sceneUi?.SetStoryVisible(true);
            }
        }

        private static void InitializeAmbientWander(EntityVisual visual, Guid entityId)
        {
            if (visual == null || visual.IsPlayer || visual.Animator == null ||
                !HasAnimatorParameter(visual.Animator, "MoveSpeed"))
                return;

            byte[] bytes = entityId.ToByteArray();
            uint seed = (uint)(bytes[0] | bytes[1] << 8 | bytes[2] << 16 | bytes[3] << 24);
            visual.WanderPhase = (seed & 0xffff) / 65535f;
            visual.NextWanderDecisionAt = Time.unscaledTime + 0.6f + visual.WanderPhase * 1.4f;
            visual.Facing = Vector2.down;
            visual.Animator.SetFloat("MoveX", 0f);
            visual.Animator.SetFloat("MoveY", -1f);
            visual.Animator.SetFloat("MoveSpeed", 0f);
        }

        private static void UpdateAmbientWander(EntityVisual visual)
        {
            float now = Time.unscaledTime;
            if (now >= visual.NextWanderDecisionAt)
            {
                visual.WanderStep++;
                float sequence = Mathf.Repeat(visual.WanderPhase + visual.WanderStep * 0.618034f, 1f);
                if (visual.WanderStep % 3 == 0)
                {
                    visual.WanderTargetOffset = visual.Root.transform.position - visual.TargetPosition;
                    visual.WanderTargetOffset.z = 0f;
                    visual.IsWandering = false;
                    visual.NextWanderDecisionAt = now + 0.8f + sequence * 1.2f;
                }
                else
                {
                    float angle = sequence * Mathf.PI * 2f;
                    float radius = 0.14f + Mathf.Repeat(sequence * 2.37f, 1f) * 0.16f;
                    visual.WanderTargetOffset = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * radius;
                    Vector3 direction = visual.TargetPosition + visual.WanderTargetOffset - visual.Root.transform.position;
                    if (Mathf.Abs(direction.x) > Mathf.Abs(direction.y))
                        visual.Facing = new Vector2(Mathf.Sign(direction.x), 0f);
                    else if (Mathf.Abs(direction.y) > 0.0001f)
                        visual.Facing = new Vector2(0f, Mathf.Sign(direction.y));
                    visual.IsWandering = true;
                    visual.NextWanderDecisionAt = now + 1.1f + sequence * 0.8f;
                }
            }

            if (!visual.IsWandering)
                return;

            Vector3 destination = visual.TargetPosition + visual.WanderTargetOffset;
            visual.Root.transform.position = Vector3.MoveTowards(
                visual.Root.transform.position,
                destination,
                0.42f * Time.unscaledDeltaTime);
            if (Vector3.SqrMagnitude(visual.Root.transform.position - destination) < 0.0004f)
            {
                visual.Root.transform.position = destination;
                visual.IsWandering = false;
                visual.NextWanderDecisionAt = Mathf.Min(visual.NextWanderDecisionAt, now + 0.75f);
            }
        }

        private bool TryGetPlayerVisual(RunView view, out EntityVisual visual)
        {
            Guid playerId = view.PlayerEntityId;
            if (_serverOnline && _serverRun != null && Guid.TryParse(_serverRun.playerEntityId, out Guid serverId))
                playerId = serverId;
            return _entityVisuals.TryGetValue(playerId, out visual) && visual?.Root != null;
        }

        private bool CanSelectMovementDestination(RunView view, GridCoord goal)
        {
            if (!TryGetPlayerPosition(view, out GridCoord start) || start == goal)
                return false;
            if (_serverOnline && _serverRun?.world != null)
                return !IsServerOccupied(goal) && FindServerVisualPath(start, goal).Count > 1;
            if (!view.Region.IsWalkable(goal))
                return false;
            for (int i = 0; i < view.Entities.Count; i++)
                if (view.Entities[i].EntityId != view.PlayerEntityId && view.Entities[i].Position == goal)
                    return false;
            List<GridCoord> path = GridPathfinder.FindPath(view.Region, start, goal, coord =>
            {
                for (int i = 0; i < view.Entities.Count; i++)
                    if (view.Entities[i].EntityId != view.PlayerEntityId && view.Entities[i].Position == coord)
                        return true;
                return false;
            });
            return path.Count > 1;
        }

        private static List<GridCoord> FindLocalVisualPath(RunView view, GridCoord start, GridCoord goal)
        {
            if (start == goal)
                return new List<GridCoord> { start };
            return GridPathfinder.FindPath(view.Region, start, goal, coord =>
            {
                if (coord == goal)
                    return false;
                for (int i = 0; i < view.Entities.Count; i++)
                {
                    EntityView entity = view.Entities[i];
                    if (entity.EntityId != view.PlayerEntityId && entity.Position == coord)
                        return true;
                }
                return false;
            });
        }

        private List<GridCoord> NavigationVisualPath(GameApiClient.NavigationSnapshot navigation,
            GridCoord start, GridCoord goal)
        {
            var path = new List<GridCoord>();
            if (navigation?.path != null)
            {
                for (int i = 0; i < navigation.path.Length; i++)
                {
                    GameApiClient.PositionSnapshot step = navigation.path[i];
                    if (step == null)
                        continue;
                    var coord = new GridCoord(step.x, step.y);
                    if (path.Count == 0 || path[path.Count - 1] != coord)
                        path.Add(coord);
                }
            }
            if (path.Count == 0 || path[0] != start)
                path.Insert(0, start);
            if (path[path.Count - 1] != goal)
                path.Add(goal);
            return PathIsContiguous(path) ? path : FindServerVisualPath(start, goal);
        }

        private List<GridCoord> FindServerVisualPath(GridCoord start, GridCoord goal)
        {
            var empty = new List<GridCoord>();
            if (!IsServerWalkable(start) || !IsServerWalkable(goal))
                return empty;
            var queue = new Queue<GridCoord>();
            var visited = new HashSet<GridCoord> { start };
            var cameFrom = new Dictionary<GridCoord, GridCoord>();
            queue.Enqueue(start);
            var directions = new[]
            {
                new GridCoord(1, 0), new GridCoord(-1, 0),
                new GridCoord(0, 1), new GridCoord(0, -1)
            };
            while (queue.Count > 0)
            {
                GridCoord current = queue.Dequeue();
                if (current == goal)
                {
                    var path = new List<GridCoord> { current };
                    while (cameFrom.TryGetValue(current, out GridCoord previous))
                    {
                        current = previous;
                        path.Add(current);
                    }
                    path.Reverse();
                    return path;
                }
                for (int i = 0; i < directions.Length; i++)
                {
                    var next = new GridCoord(current.X + directions[i].X, current.Y + directions[i].Y);
                    if (visited.Contains(next) || !IsServerWalkable(next) || IsServerVisualBlocker(next, goal))
                        continue;
                    visited.Add(next);
                    cameFrom[next] = current;
                    queue.Enqueue(next);
                }
            }
            return empty;
        }

        private bool IsServerWalkable(GridCoord coord)
        {
            GameApiClient.WorldSnapshot world = _serverRun?.world;
            if (world == null || !world.HasCompleteLayout || coord.X < 0 || coord.Y < 0 ||
                coord.X >= world.width || coord.Y >= world.height)
                return false;
            TileKind kind = TileKindForServer(world, world.tileCodes[coord.Y * world.width + coord.X]);
            return kind != TileKind.Wall && kind != TileKind.Water;
        }

        private bool IsServerVisualBlocker(GridCoord coord, GridCoord goal)
        {
            return coord != goal && IsServerOccupied(coord);
        }

        private bool IsServerOccupied(GridCoord coord)
        {
            if (_serverRun?.entities == null)
                return false;
            for (int i = 0; i < _serverRun.entities.Length; i++)
            {
                GameApiClient.EntitySnapshot entity = _serverRun.entities[i];
                if (entity?.position != null && !string.Equals(entity.id, _serverRun.playerEntityId, StringComparison.OrdinalIgnoreCase) &&
                    entity.position.x == coord.X && entity.position.y == coord.Y)
                    return true;
            }
            return false;
        }

        private static bool PathIsContiguous(IReadOnlyList<GridCoord> path)
        {
            if (path == null || path.Count == 0)
                return false;
            for (int i = 1; i < path.Count; i++)
                if (path[i - 1].ManhattanDistance(path[i]) != 1)
                    return false;
            return true;
        }

        private void UpdateSelectionVisual(RunView view)
        {
            if (_selectionRenderer == null)
                return;
            _selectionRenderer.enabled = _selectedCoord.HasValue;
            if (_selectedCoord.HasValue)
            {
                _selectionRenderer.transform.position = WorldPosition(MapOrigin(view), _selectedCoord.Value);
                // Keep the authored tile cursor behind actors. The cursor is deliberately a little
                // larger than one cell, so only its perimeter remains visible around a selected entity.
                _selectionRenderer.sortingOrder = 499 - _selectedCoord.Value.Y * 4;
                _selectionRenderer.color = _ability == AbilityKind.Delete
                    ? new Color(1f, 0.12f, 0.08f, 0.58f)
                    : _ability == AbilityKind.Restore
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
            if (_debugTourActive) // TEMP DEBUG — 커밋 전 제거
            {
                UpdateDebugTourCamera();
                return;
            }
            RunView view = _service.CurrentView;
            if (!TryGetPlayerPosition(view, out GridCoord playerPosition))
                return;
            GridCoord cameraTarget = _cameraInspectCoord.HasValue && Time.unscaledTime < _cameraInspectUntil
                ? _cameraInspectCoord.Value
                : playerPosition;
            if (Time.unscaledTime >= _cameraInspectUntil) _cameraInspectCoord = null;
            Vector3 desired = _playerWalking && !_cameraInspectCoord.HasValue && TryGetPlayerVisual(view, out EntityVisual playerVisual)
                ? playerVisual.Root.transform.position
                : WorldPosition(MapOrigin(view), cameraTarget);
            Vector2 origin = MapOrigin(view);
            int worldWidth = ActiveWorldWidth(view);
            int worldHeight = ActiveWorldHeight(view);
            _cameraController.Follow(desired, origin, worldWidth, worldHeight, Time.unscaledDeltaTime);
        }

        // ─────────────────────────────────────────────────────────────
        // TEMP DEBUG — 구역 순회 기능 (커밋 전 제거).
        // F1 토글 · F2/F3 이전·다음 · +/- 줌 · F4 전체보기.
        // 넙죽이 테스트 패널과 마찬가지로 임시 개발 도구다.
        // ─────────────────────────────────────────────────────────────
        private void HandleDebugRegionTour()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
                return;

            if (keyboard.backquoteKey.wasPressedThisFrame)
            {
                _debugTourActive = !_debugTourActive;
                if (_debugTourActive)
                {
                    _debugTourBaseOrthoSize = _camera != null ? _camera.orthographicSize : 8.25f;
                    FocusDebugTourRegion(_debugTourIndex);
                }
                else
                {
                    _cameraInspectCoord = null;
                    _cameraInspectUntil = 0f;
                    if (_camera != null) _camera.orthographicSize = _debugTourBaseOrthoSize;
                }
                PlaySfx(AssetClip("UiAcceptSound"));
            }

            if (!_debugTourActive)
                return;

            int count = DebugTourRegionCount();
            if (count > 0)
            {
                if (keyboard.periodKey.wasPressedThisFrame || keyboard.pageDownKey.wasPressedThisFrame)
                    FocusDebugTourRegion(_debugTourIndex + 1);
                if (keyboard.commaKey.wasPressedThisFrame || keyboard.pageUpKey.wasPressedThisFrame)
                    FocusDebugTourRegion(_debugTourIndex - 1);
            }
            if (keyboard.equalsKey.wasPressedThisFrame || keyboard.numpadPlusKey.wasPressedThisFrame)
                _debugTourZoom = Mathf.Max(6f, _debugTourZoom - 4f);
            if (keyboard.minusKey.wasPressedThisFrame || keyboard.numpadMinusKey.wasPressedThisFrame)
                _debugTourZoom = Mathf.Min(90f, _debugTourZoom + 4f);
            if (keyboard.digit0Key.wasPressedThisFrame)
                _debugTourZoom = 84f;
        }

        private void FocusDebugTourRegion(int index)
        {
            int count = DebugTourRegionCount();
            if (count <= 0)
                return;
            _debugTourIndex = ((index % count) + count) % count;
            if (TryGetDebugTourRegion(_debugTourIndex, out GridCoord center, out _))
            {
                _cameraInspectCoord = center;
                _cameraInspectUntil = float.MaxValue;
            }
        }

        private void UpdateDebugTourCamera()
        {
            RunView view = _service != null ? _service.CurrentView : null;
            if (view == null || _camera == null)
                return;
            _camera.orthographicSize = Mathf.Lerp(_camera.orthographicSize, _debugTourZoom,
                1f - Mathf.Exp(-8f * Time.unscaledDeltaTime));
            if (!TryGetDebugTourRegion(_debugTourIndex, out GridCoord center, out _))
                return;
            Vector3 desired = WorldPosition(MapOrigin(view), center);
            desired.z = -10f;
            _camera.transform.position = Vector3.SmoothDamp(_camera.transform.position, desired,
                ref _cameraVelocity, 0.16f, Mathf.Infinity, Time.unscaledDeltaTime);
        }

        private int DebugTourRegionCount()
        {
            RunView view = _service != null ? _service.CurrentView : null;
            if (view == null)
                return 0;
            if (_serverOnline && _serverRun?.world?.areas != null)
                return _serverRun.world.areas.Length;
            return view.Region?.Areas?.Count ?? 0;
        }

        private bool TryGetDebugTourRegion(int index, out GridCoord center, out string label)
        {
            center = default;
            label = string.Empty;
            RunView view = _service != null ? _service.CurrentView : null;
            if (view == null)
                return false;
            if (_serverOnline && _serverRun?.world?.areas != null)
            {
                GameApiClient.AreaSnapshot[] areas = _serverRun.world.areas;
                if (index < 0 || index >= areas.Length)
                    return false;
                GameApiClient.AreaSnapshot area = areas[index];
                center = area.anchor != null
                    ? new GridCoord(area.anchor.x, area.anchor.y)
                    : (area.bounds != null
                        ? new GridCoord(area.bounds.x + area.bounds.width / 2, area.bounds.y + area.bounds.height / 2)
                        : new GridCoord(view.Region.Width / 2, view.Region.Height / 2));
                string name = !string.IsNullOrWhiteSpace(area.nameKo) ? area.nameKo
                    : (!string.IsNullOrWhiteSpace(area.name) ? area.name : area.id);
                label = name + " · " + BiomeLabelForId(area.biomeId);
                return true;
            }
            if (view.Region?.Areas != null && index >= 0 && index < view.Region.Areas.Count)
            {
                WorldArea area = view.Region.Areas[index];
                center = area.Center;
                label = area.DisplayName + " · " + BiomeLabelForId(area.Biome);
                return true;
            }
            return false;
        }

        private static string BiomeLabelForId(string id)
        {
            switch (id)
            {
                case "root_system": return "루트 시스템 · 코어";
                case "temperate_forest_field": return "온대 숲·들판";
                case "river_wetland": return "강·습지";
                case "arid_desert": return "건조 사막";
                case "frost_highland": return "설원 고지";
                case "subterranean_cavern": return "지하 동굴";
                case "ancient_ruins": return "고대 유적";
                default: return string.IsNullOrWhiteSpace(id) ? "바이옴 미확인" : id;
            }
        }

        private void DrawDebugRegionTourPanel()
        {
            if (!_debugTourActive || _screenMode != ScreenMode.Playing || _service == null)
                return;

            Matrix4x4 previousMatrix = GUI.matrix;
            Color previousColor = GUI.color;
            Font previousFont = GUI.skin.font;
            GUI.matrix = Matrix4x4.Scale(new Vector3(Screen.width / LogicalWidth, Screen.height / LogicalHeight, 1f));
            if (_koreanFont != null)
                GUI.skin.font = _koreanFont;
            else if (_assets != null && _assets.PixelFont != null)
                GUI.skin.font = _assets.PixelFont;
            EnsureStyles();

            int count = DebugTourRegionCount();
            int rows = (count + 1) / 2;
            var panel = new Rect(24f, 250f, 396f, 118f + rows * 34f);
            GUI.Box(panel, "구역 순회 (임시 디버그)");

            string current = TryGetDebugTourRegion(_debugTourIndex, out _, out string label) ? label : "구역 없음";
            GUI.Label(new Rect(panel.x + 16f, panel.y + 32f, panel.width - 32f, 22f),
                "현재 [" + (count == 0 ? 0 : _debugTourIndex + 1) + "/" + count + "]  " + current, _headerStyle);
            GUI.Label(new Rect(panel.x + 16f, panel.y + 56f, panel.width - 32f, 40f),
                "` 토글 · , / . 이전·다음 · - / = 줌 · 0 전체", _mutedStyle);

            for (int i = 0; i < count; i++)
            {
                int column = i % 2;
                int row = i / 2;
                var rect = new Rect(panel.x + 16f + column * 184f, panel.y + 104f + row * 34f, 176f, 30f);
                TryGetDebugTourRegion(i, out _, out string itemLabel);
                GUIStyle style = i == _debugTourIndex ? _selectedButtonStyle : _buttonStyle;
                if (GUI.Button(rect, itemLabel, style))
                    FocusDebugTourRegion(i);
            }

            GUI.skin.font = previousFont;
            GUI.color = previousColor;
            GUI.matrix = previousMatrix;
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
            ApplyAudioVolumes();
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
            if (_assets == null)
                return null;
            switch (fieldName)
            {
                case "AdventureMusic": return _assets.AdventureMusic;
                case "VillageMusic": return _assets.VillageMusic;
                case "BattleMusic": return _assets.BattleMusic;
                case "UiMoveSound": return _assets.UiMoveSound;
                case "UiAcceptSound": return _assets.UiAcceptSound;
                case "UiCancelSound": return _assets.UiCancelSound;
                case "SlashSound": return _assets.SlashSound;
                case "HitSound": return _assets.HitSound;
                case "CoinSound": return _assets.CoinSound;
                case "SuccessJingle": return _assets.SuccessJingle;
                default: return null;
            }
        }

        private void LoadNinjaAdventureAssets()
        {
            _assets = authoredAssetManifest != null
                ? authoredAssetManifest
                : authoringSettings != null && authoringSettings.AssetManifest != null
                    ? authoringSettings.AssetManifest
                    : Resources.Load<NinjaAdventureAssetManifest>("NinjaAdventureAssetManifest");
            try
            {
                _koreanFont = Font.CreateDynamicFontFromOSFont(
                    new[] { "Apple SD Gothic Neo", "Noto Sans CJK KR", "Arial Unicode MS", "Arial" }, 20);
            }
            catch (Exception)
            {
                _koreanFont = _assets != null ? _assets.PixelFont : null;
            }

            _grassSprite = CreateAtlasSprite(_assets != null ? _assets.OutdoorFieldAtlas : null,
                _assets != null ? _assets.OutdoorGrassRect : new Rect(16f, 160f, 16f, 16f), "Field Grass", Hex("5d993f"));
            _dirtSprite = CreateAtlasSprite(_assets != null ? _assets.OutdoorFieldAtlas : null,
                _assets != null ? _assets.OutdoorDirtRect : new Rect(16f, 208f, 16f, 16f), "Field Dirt", Hex("b97842"));
            _darkGrassSprite = CreateAtlasSprite(_assets != null ? _assets.OutdoorFieldAtlas : null,
                _assets != null ? _assets.OutdoorDarkGrassRect : new Rect(16f, 112f, 16f, 16f), "Field Dark Grass", Hex("315f38"));
            _wallSprite = CreateAtlasSprite(_assets != null ? _assets.InteriorFloorAtlas : null,
                _assets != null ? _assets.WallRect : new Rect(176f, 96f, 16f, 16f), "Ruin Wall", Hex("35453a"));
            _waterSprite = CreateAtlasSprite(_assets != null ? _assets.WaterAtlas : null,
                new Rect(176f, 240f, 16f, 16f), "Wetland Water", Hex("36758b"));
            _snowSprite = CreateAtlasSprite(_assets != null ? _assets.OutdoorFieldAtlas : null,
                new Rect(16f, 16f, 16f, 16f), "Frost Snow", Hex("d9edf3"));
            _ruinFloorSprite = CreateAtlasSprite(_assets != null ? _assets.OutdoorFieldAtlas : null,
                new Rect(16f, 64f, 16f, 16f), "Ruins Ground", Hex("a68a82"));
            _cavernFloorSprite = CreateAtlasSprite(_assets != null ? _assets.DungeonAtlas : null,
                new Rect(80f, 16f, 16f, 16f), "Cavern Floor", Hex("4c425d"));

            _forestTreeSprite = CreateAtlasSprite(_assets != null ? _assets.NatureAtlas : null,
                new Rect(0f, 304f, 32f, 32f), "Forest Tree", Hex("568b42"), new Vector2(0.5f, 0.08f));
            _forestHouseSprite = CreateAtlasSprite(_assets != null ? _assets.HouseAtlas : null,
                new Rect(0f, 304f, 64f, 64f), "Forest House", Hex("a7653f"), new Vector2(0.5f, 0.05f));
            _wetlandPlantSprite = CreateAtlasSprite(_assets != null ? _assets.NatureAtlas : null,
                new Rect(112f, 160f, 16f, 16f), "Wetland Reeds", Hex("4f8f68"), new Vector2(0.5f, 0.08f));
            _wetlandLandmarkSprite = CreateAtlasSprite(_assets != null ? _assets.WatermillAtlas : null,
                new Rect(0f, 0f, 34f, 36f), "Wetland Watermill", Hex("9a6b43"), new Vector2(0.5f, 0.08f));
            // The desert atlas is a modular construction sheet rather than a collection of standalone
            // scenery sprites. Use isolated nature-atlas cells here so a crop cannot include building pieces.
            _desertShrubSprite = CreateAtlasSprite(_assets != null ? _assets.NatureAtlas : null,
                new Rect(176f, 160f, 16f, 16f), "Desert Shrub", Hex("729347"), new Vector2(0.5f, 0.08f));
            _desertLandmarkSprite = CreateAtlasSprite(_assets != null ? _assets.DesertAtlas : null,
                new Rect(48f, 112f, 48f, 80f), "Desert Tower", Hex("d4a36a"), new Vector2(0.5f, 0.03f));
            _frostTreeSprite = CreateAtlasSprite(_assets != null ? _assets.NatureAtlas : null,
                new Rect(128f, 304f, 32f, 32f), "Frost Tree", Hex("dcebf0"), new Vector2(0.5f, 0.08f));
            _frostLandmarkSprite = CreateAtlasSprite(_assets != null ? _assets.HouseAtlas : null,
                new Rect(0f, 144f, 48f, 48f), "Frost Shelter", Hex("e5f1f4"), new Vector2(0.5f, 0.04f));
            _cavernCrystalSprite = CreateAtlasSprite(_assets != null ? _assets.NatureAtlas : null,
                new Rect(0f, 112f, 32f, 32f), "Cavern Crystal", Hex("a978c4"), new Vector2(0.5f, 0.08f));
            _ruinTreeSprite = CreateAtlasSprite(_assets != null ? _assets.NatureAtlas : null,
                new Rect(64f, 304f, 32f, 32f), "Ruins Dead Tree", Hex("75624f"), new Vector2(0.5f, 0.08f));
            _ruinLandmarkSprite = CreateAtlasSprite(_assets != null ? _assets.AbandonedVillageAtlas : null,
                new Rect(192f, 16f, 64f, 80f), "Ancient Ruin", Hex("82705a"), new Vector2(0.5f, 0.04f));
            _forestDecorationSprites = new[]
            {
                _forestTreeSprite,
                CreateAtlasSprite(_assets != null ? _assets.NatureAtlas : null,
                    new Rect(32f, 304f, 32f, 32f), "Forest Pine", Hex("477a3d"), new Vector2(0.5f, 0.08f)),
                CreateAtlasSprite(_assets != null ? _assets.NatureAtlas : null,
                    new Rect(96f, 304f, 32f, 32f), "Forest Shrub", Hex("6a9a46"), new Vector2(0.5f, 0.08f)),
                CreateAtlasSprite(_assets != null ? _assets.NatureAtlas : null,
                    new Rect(64f, 160f, 16f, 16f), "Forest Plants", Hex("6f9e4c"), new Vector2(0.5f, 0.08f))
            };
            _wetlandDecorationSprites = new[]
            {
                _wetlandPlantSprite,
                CreateAtlasSprite(_assets != null ? _assets.NatureAtlas : null,
                    new Rect(128f, 160f, 16f, 16f), "Wetland Plants", Hex("4f8f68"), new Vector2(0.5f, 0.08f)),
                CreateAtlasSprite(_assets != null ? _assets.NatureAtlas : null,
                    new Rect(0f, 144f, 16f, 16f), "Wetland Flowers", Hex("6fa87b"), new Vector2(0.5f, 0.08f))
            };
            _desertDecorationSprites = new[]
            {
                _desertShrubSprite,
                CreateAtlasSprite(_assets != null ? _assets.NatureAtlas : null,
                    new Rect(144f, 176f, 16f, 16f), "Desert Rock", Hex("7f9a4d"), new Vector2(0.5f, 0.08f)),
                CreateAtlasSprite(_assets != null ? _assets.NatureAtlas : null,
                    new Rect(160f, 176f, 16f, 16f), "Desert Bush", Hex("8ca454"), new Vector2(0.5f, 0.08f))
            };
            _desertBuildingSprites = new[]
            {
                _desertLandmarkSprite,
                CreateAtlasSpriteWithoutBottomLeft(_assets != null ? _assets.HouseAtlas : null,
                    new Rect(128f, 304f, 64f, 64f), new Vector2(32f, 16f),
                    "Desert House", Hex("c98342"), new Vector2(0.5f, 0.04f)),
                CreateAtlasSprite(_assets != null ? _assets.HouseAtlas : null,
                    new Rect(192f, 304f, 64f, 64f), "Desert Red House", Hex("c56f45"), new Vector2(0.5f, 0.04f))
            };
            _frostDecorationSprites = new[]
            {
                _frostTreeSprite,
                CreateAtlasSprite(_assets != null ? _assets.NatureAtlas : null,
                    new Rect(160f, 304f, 32f, 32f), "Frost Snow Tree", Hex("dcebf0"), new Vector2(0.5f, 0.08f)),
                CreateAtlasSprite(_assets != null ? _assets.NatureAtlas : null,
                    new Rect(192f, 304f, 32f, 32f), "Frost Bush", Hex("d4e7ed"), new Vector2(0.5f, 0.08f))
            };
            _cavernDecorationSprites = new[]
            {
                _cavernCrystalSprite,
                CreateAtlasSprite(_assets != null ? _assets.NatureAtlas : null,
                    new Rect(32f, 112f, 32f, 32f), "Cavern Crystal Cluster", Hex("9670b8"), new Vector2(0.5f, 0.08f)),
                CreateAtlasSprite(_assets != null ? _assets.NatureAtlas : null,
                    new Rect(64f, 112f, 32f, 32f), "Cavern Ore", Hex("8062a0"), new Vector2(0.5f, 0.08f))
            };
            _ruinDecorationSprites = new[]
            {
                _ruinTreeSprite,
                CreateAtlasSprite(_assets != null ? _assets.AbandonedVillageAtlas : null,
                    new Rect(0f, 112f, 32f, 32f), "Ruin Rubble", Hex("8b7757"), new Vector2(0.5f, 0.08f)),
                CreateAtlasSprite(_assets != null ? _assets.AbandonedVillageAtlas : null,
                    new Rect(32f, 112f, 32f, 32f), "Ruin Overgrowth", Hex("788152"), new Vector2(0.5f, 0.08f))
            };

            if (_assets != null)
            {
                _playerIdleFrames = CreateSheetFrames(_assets.PlayerIdleSheet, Mathf.Max(1, _assets.PlayerFrameSize), 3, "Player Idle");
                _playerWalkFrames = CreateSheetFrames(_assets.PlayerWalkSheet, Mathf.Max(1, _assets.PlayerFrameSize), 3, "Player Walk");
                _playerAttackFrames = CreateSheetFrames(_assets.PlayerAttackSheet, Mathf.Max(1, _assets.PlayerFrameSize), 3, "Player Attack");
                _slimeFrames = CreateSheetFrames(_assets.SlimeSheet, Mathf.Max(1, _assets.CreatureFrameSize), 3, "Slime");
                _villagerFrames = CreateSheetFrames(_assets.VillagerWalkSheet, Mathf.Max(1, _assets.CreatureFrameSize), 3, "Villager");

                if (_assets.NeopjukiAtlas != null)
                {
                    int cellWidth = Mathf.Max(1, _assets.NeopjukiCellWidth);
                    int cellHeight = Mathf.Max(1, _assets.NeopjukiCellHeight);
                    _playerIdleFrames = CreateNeopjukiFrames(_assets.NeopjukiAtlas, cellWidth, cellHeight, 0, 6, "Neopjuki Idle");
                    _playerWalkFrames = CreateNeopjukiFrames(_assets.NeopjukiAtlas, cellWidth, cellHeight, 1, 8, "Neopjuki Right");
                    _playerWalkLeftFrames = CreateNeopjukiFrames(_assets.NeopjukiAtlas, cellWidth, cellHeight, 2, 8, "Neopjuki Left");
                    _neopjukiWaveFrames = CreateNeopjukiFrames(_assets.NeopjukiAtlas, cellWidth, cellHeight, 3, 4, "Neopjuki Wave");
                    _neopjukiJumpFrames = CreateNeopjukiFrames(_assets.NeopjukiAtlas, cellWidth, cellHeight, 4, 5, "Neopjuki Jump");
                    _neopjukiFailedFrames = CreateNeopjukiFrames(_assets.NeopjukiAtlas, cellWidth, cellHeight, 5, 8, "Neopjuki Failed");
                    _neopjukiWaitingFrames = CreateNeopjukiFrames(_assets.NeopjukiAtlas, cellWidth, cellHeight, 6, 6, "Neopjuki Waiting");
                    _neopjukiReviewFrames = CreateNeopjukiFrames(_assets.NeopjukiAtlas, cellWidth, cellHeight, 7, 6, "Neopjuki Review");
                    _neopjukiKeyboardAttackFrames = CreateNeopjukiFrames(_assets.NeopjukiAtlas, cellWidth, cellHeight, 8, 8, "Neopjuki Keyboard Attack");
                    _neopjukiKeyboardMagicFrames = CreateNeopjukiFrames(_assets.NeopjukiAtlas, cellWidth, cellHeight, 9, 8, "Neopjuki Keyboard Magic");
                    _neopjukiKeyboardDebugFrames = CreateNeopjukiFrames(_assets.NeopjukiAtlas, cellWidth, cellHeight, 10, 8, "Neopjuki Keyboard Debug");
                    _neopjukiKeyboardAttackLeftFrames = CreateNeopjukiFrames(_assets.NeopjukiAtlas, cellWidth, cellHeight, 11, 8, "Neopjuki Keyboard Attack Left");
                    _neopjukiKeyboardAttackUpFrames = CreateNeopjukiFrames(_assets.NeopjukiAtlas, cellWidth, cellHeight, 12, 8, "Neopjuki Keyboard Attack Up");
                    _neopjukiKeyboardAttackDownFrames = CreateNeopjukiFrames(_assets.NeopjukiAtlas, cellWidth, cellHeight, 13, 8, "Neopjuki Keyboard Attack Down");
                    _neopjukiWalkUpFrames = CreateNeopjukiFrames(_assets.NeopjukiAtlas, cellWidth, cellHeight, 14, 8, "Neopjuki Walk Up");
                    _neopjukiWalkDownFrames = CreateNeopjukiFrames(_assets.NeopjukiAtlas, cellWidth, cellHeight, 15, 8, "Neopjuki Walk Down");
                    _playerAttackFrames = _neopjukiKeyboardAttackFrames;
                }
            }

            _playerSprite = FirstOrSource(_playerIdleFrames, _assets != null ? _assets.PlayerIdle : null, "Player", Hex("6a9d45"));
            _wardenSprite = SourceOrFallback(_assets != null ? _assets.WardenIdle : null, "Warden", Hex("8c7654"));
            _villagerSprite = FirstOrSource(_villagerFrames, _assets != null ? _assets.VillagerIdle : null, "Villager", Hex("c58f58"));
            _slimeSprite = FirstOrSource(_slimeFrames, _assets != null ? _assets.SlimeIdle : null, "Slime", Hex("61a65d"));
            _bookSprite = SourceOrFallback(_assets != null ? _assets.RuneBook : null, "Rune Book", Hex("8d65a8"));
            _crateSprite = SourceOrFallback(_assets != null ? _assets.Crate : null, "Crate", Hex("95623d"));
            _chestSprite = SourceOrFallback(_assets != null ? _assets.TreasureChest : null, "Chest", Hex("d7a743"));
            _d20Sprite = SourceOrFallback(_assets != null ? _assets.D20 : null, "D20", Gold);
            _whiteSprite = CreateSolidSprite(Color.white, "White Pixel");
        }

        private Sprite CreateAtlasSprite(Texture2D texture, Rect requestedRect, string spriteName, Color fallbackColor,
            Vector2? requestedPivot = null)
        {
            if (texture == null || texture.width <= 0 || texture.height <= 0)
                return CreateSolidSprite(fallbackColor, spriteName + " Fallback");
            Rect rect = SafeRect(texture, requestedRect);
            Sprite sprite = Sprite.Create(texture, rect, requestedPivot ?? new Vector2(0.5f, 0.5f), 16f, 0,
                SpriteMeshType.FullRect);
            sprite.name = spriteName;
            _runtimeSprites.Add(sprite);
            return sprite;
        }

        private Sprite CreateAtlasSpriteWithoutBottomLeft(Texture2D texture, Rect requestedRect, Vector2 cutoutSize,
            string spriteName, Color fallbackColor, Vector2? requestedPivot = null)
        {
            Sprite sprite = CreateAtlasSprite(texture, requestedRect, spriteName, fallbackColor, requestedPivot);
            if (texture == null)
                return sprite;

            float width = sprite.rect.width;
            float height = sprite.rect.height;
            float cutoutWidth = Mathf.Clamp(cutoutSize.x, 0f, width);
            float cutoutHeight = Mathf.Clamp(cutoutSize.y, 0f, height);
            if (cutoutWidth <= 0f || cutoutHeight <= 0f ||
                cutoutWidth >= width || cutoutHeight >= height)
                return sprite;

            // Keep the atlas rect intact for consistent placement, but omit the two unrelated
            // 16 px cells at the lower-left of the orange house from the rendered sprite mesh.
            sprite.OverrideGeometry(
                new[]
                {
                    new Vector2(0f, cutoutHeight),
                    new Vector2(0f, height),
                    new Vector2(width, height),
                    new Vector2(width, cutoutHeight),
                    new Vector2(cutoutWidth, 0f),
                    new Vector2(cutoutWidth, cutoutHeight),
                    new Vector2(width, cutoutHeight),
                    new Vector2(width, 0f)
                },
                new ushort[]
                {
                    0, 1, 2, 0, 2, 3,
                    4, 5, 6, 4, 6, 7
                });
            return sprite;
        }

        private Sprite[] CreateSheetFrames(Texture2D texture, int frameSize, int rowFromBottom, string prefix)
        {
            if (texture == null || frameSize <= 0 || texture.width < frameSize || texture.height < frameSize)
                return Array.Empty<Sprite>();
            int count = Mathf.Min(4, texture.width / frameSize);
            int y = Mathf.Clamp(rowFromBottom * frameSize, 0, texture.height - frameSize);
            var frames = new Sprite[count];
            for (int i = 0; i < count; i++)
            {
                frames[i] = Sprite.Create(texture, new Rect(i * frameSize, y, frameSize, frameSize),
                    new Vector2(0.5f, 0.42f), frameSize, 0, SpriteMeshType.FullRect);
                frames[i].name = prefix + " " + i;
                _runtimeSprites.Add(frames[i]);
            }
            return frames;
        }

        private Sprite[] CreateNeopjukiFrames(
            Texture2D texture,
            int cellWidth,
            int cellHeight,
            int rowFromTop,
            int frameCount,
            string prefix)
        {
            if (texture == null || cellWidth <= 0 || cellHeight <= 0 ||
                texture.width < cellWidth || texture.height < cellHeight)
                return Array.Empty<Sprite>();

            int columns = texture.width / cellWidth;
            int rows = texture.height / cellHeight;
            if (rowFromTop < 0 || rowFromTop >= rows)
                return Array.Empty<Sprite>();

            int count = Mathf.Min(frameCount, columns);
            int y = texture.height - (rowFromTop + 1) * cellHeight;
            var frames = new Sprite[count];
            for (int i = 0; i < count; i++)
            {
                frames[i] = Sprite.Create(
                    texture,
                    new Rect(i * cellWidth, y, cellWidth, cellHeight),
                    new Vector2(0.5f, 0f),
                    cellWidth,
                    0,
                    SpriteMeshType.FullRect);
                frames[i].name = prefix + " " + i;
                _runtimeSprites.Add(frames[i]);
            }
            return frames;
        }

        private Sprite SourceOrFallback(Sprite source, string name, Color color)
        {
            return source != null ? source : CreateSolidSprite(color, name + " Fallback");
        }

        private Sprite FirstOrSource(Sprite[] frames, Sprite source, string name, Color color)
        {
            return frames != null && frames.Length > 0 ? frames[0] : SourceOrFallback(source, name, color);
        }

        private Sprite CreateSolidSprite(Color color, string name)
        {
            Texture2D texture = MakeTexture(color, name + " Texture");
            Sprite sprite = Sprite.Create(texture, new Rect(0f, 0f, 1f, 1f), new Vector2(0.5f, 0.5f), 1f);
            sprite.name = name;
            _runtimeSprites.Add(sprite);
            return sprite;
        }

        private static Rect SafeRect(Texture2D texture, Rect rect)
        {
            float width = Mathf.Clamp(rect.width, 1f, texture.width);
            float height = Mathf.Clamp(rect.height, 1f, texture.height);
            return new Rect(Mathf.Clamp(rect.x, 0f, texture.width - width), Mathf.Clamp(rect.y, 0f, texture.height - height), width, height);
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

        private Sprite GroundSpriteForBiome(string biomeId)
        {
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
            // 로컬 원반 월드는 각도 섹터로 바이옴을 정한다(생성기 SectorBiomeIndex와 동일 공식).
            return SectorBiomeId(coord, view.Region.Width, view.Region.Height);
        }

        // 생성기 CreateBiomes()와 동일한 순서 — 파이 슬라이스 바이옴 매핑.
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
            double centralRadius = Math.Min(width, height) * 0.12; // 중심 최종 스테이지 존(생성기와 동일)
            if (dx * dx + dy * dy <= centralRadius * centralRadius)
                return "root_system"; // 다크 보스 코어
            double angle = Math.Atan2(dy, dx) + Math.PI; // 0..2π
            int sector = (int)(angle / (2.0 * Math.PI) * SectorBiomeOrder.Length);
            if (sector < 0) sector = 0;
            if (sector >= SectorBiomeOrder.Length) sector = SectorBiomeOrder.Length - 1;
            return SectorBiomeOrder[sector];
        }

        private static readonly string[] SectorBiomeOrder =
        {
            // 다크 보스 코어: 기존 타일을 크게 어둡게 낮추고 심홍/보라 기운을 더한다.
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
            HashSet<GridCoord> desertBuildingTiles = CreateDesertBuildings(
                view, origin, useServerWorld, width, height, layoutHash, routeTiles);
            // step 2로 촘촘히 훑고, 타일마다 크기를 흔들어 우거진 느낌을 낸다.
            // 실제 route path만 비워 두므로 사막의 Dirt처럼 길과 같은 TileKind를 쓰는 넓은 지형도 채울 수 있다.
            for (int y = 2; y < height - 2; y += 2)
            {
                for (int x = 2; x < width - 2; x += 2)
                {
                    var coord = new GridCoord(x, y);
                    string biomeId = BiomeIdAt(view, coord);
                    TileKind tileKind = WorldTileKind(view, coord, useServerWorld);
                    if (routeTiles.Contains(coord) || desertBuildingTiles.Contains(coord) ||
                        !SupportsDecorationTerrain(biomeId, tileKind) ||
                        IsNearWorldPoint(view, coord, useServerWorld, 4) ||
                        StableVisualHash(layoutHash, x, y, 17) % 100 >= DecorationDensity(biomeId))
                        continue;
                    float scale = 0.62f + (StableVisualHash(layoutHash, x, y, 31) % 44) / 100f;
                    CreateDecoration("Scenery", DecorationSpriteForBiome(
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
                        : new GridCoord(area.bounds.x + area.bounds.width / 2, area.bounds.y + area.bounds.height / 2);
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

        private HashSet<GridCoord> CreateDesertBuildings(RunView view, Vector2 origin, bool useServerWorld,
            int width, int height, string layoutHash, HashSet<GridCoord> routeTiles)
        {
            var occupied = new HashSet<GridCoord>();
            if (_desertBuildingSprites == null || _desertBuildingSprites.Length == 0)
                return occupied;

            // Buildings use a separate, sparse pass. Each anchor represents the bottom-center of a
            // four-by-five-tile footprint, with one extra tile of padding reserved around it.
            for (int y = 3; y < height - 6; y += 7)
            {
                for (int x = 4; x < width - 4; x += 7)
                {
                    var anchor = new GridCoord(x, y);
                    if (!string.Equals(BiomeIdAt(view, anchor), "arid_desert",
                            StringComparison.OrdinalIgnoreCase) ||
                        WorldTileKind(view, anchor, useServerWorld) != TileKind.Dirt ||
                        StableVisualHash(layoutHash, x, y, 211) % 100 >= 36 ||
                        IsNearWorldPoint(view, anchor, useServerWorld, 5) ||
                        !TryReserveDesertBuildingFootprint(
                            view, useServerWorld, width, height, anchor, routeTiles, occupied))
                        continue;

                    int variant = StableVisualHash(layoutHash, x, y, 223) % _desertBuildingSprites.Length;
                    float scale = 0.86f + (StableVisualHash(layoutHash, x, y, 227) % 13) / 100f;
                    CreateDecoration("Desert building", _desertBuildingSprites[variant], anchor, origin,
                        Color.white, scale, 30);
                }
            }
            return occupied;
        }

        private bool TryReserveDesertBuildingFootprint(RunView view, bool useServerWorld, int width, int height,
            GridCoord anchor, HashSet<GridCoord> routeTiles, HashSet<GridCoord> occupied)
        {
            for (int y = 0; y < 5; y++)
            {
                for (int x = -2; x < 2; x++)
                {
                    var coord = new GridCoord(anchor.X + x, anchor.Y + y);
                    if (coord.X < 0 || coord.Y < 0 || coord.X >= width || coord.Y >= height ||
                        routeTiles.Contains(coord) || occupied.Contains(coord) ||
                        !string.Equals(BiomeIdAt(view, coord), "arid_desert",
                            StringComparison.OrdinalIgnoreCase) ||
                        !SupportsDecorationTerrain(
                            "arid_desert", WorldTileKind(view, coord, useServerWorld)))
                        return false;
                }
            }

            for (int y = -1; y <= 5; y++)
            {
                for (int x = -3; x <= 2; x++)
                {
                    var coord = new GridCoord(anchor.X + x, anchor.Y + y);
                    if (coord.X >= 0 && coord.Y >= 0 && coord.X < width && coord.Y < height)
                        occupied.Add(coord);
                }
            }
            return true;
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
            CreateDecoration("Biome landmark", LandmarkSpriteForBiome(biomeId), coord, origin,
                Color.white, string.Equals(biomeId, "subterranean_cavern", StringComparison.OrdinalIgnoreCase) ? 1.4f : 0.92f);
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
                            IsNearWorldPoint(view, coord, useServerWorld, 4))
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
                    if (points[i] != null && Math.Abs(coord.X - points[i].x) + Math.Abs(coord.Y - points[i].y) <= clearance)
                        return true;
                return false;
            }
            for (int i = 0; i < view.Region.Areas.Count; i++)
                if (coord.ManhattanDistance(view.Region.Areas[i].Center) <= clearance)
                    return true;
            return false;
        }

        private void CreateDecoration(string prefix, Sprite sprite, GridCoord coord, Vector2 origin, Color tint,
            float scale, int sortingOrder = 20)
        {
            if (sprite == null) return;
            var decoration = new GameObject(prefix + " · " + sprite.name);
            decoration.transform.SetParent(WorldContentRoot, false);
            decoration.transform.position = WorldPosition(origin, coord) + new Vector3(0f, 0.18f, -0.03f);
            var renderer = decoration.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.color = tint;
            renderer.sortingOrder = sortingOrder;
            decoration.transform.localScale = Vector3.one * scale;
        }

        private Sprite DecorationSpriteForBiome(string biomeId, int variantHash)
        {
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

        private static int DecorationDensity(string biomeId)
        {
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

        private static Color DecorationTint(string biomeId)
        {
            switch ((biomeId ?? string.Empty).ToLowerInvariant())
            {
                case "root_system": return Hex("b667d8"); // 어둠 속 크리스탈 발광
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

        private static string RootEvidenceLabel(string evidence)
        {
            if (string.IsNullOrWhiteSpace(evidence)) return "공간 조건 대기";
            string[] parts = evidence.Split(':');
            if (parts.Length == 3 && string.Equals(parts[0], "link", StringComparison.OrdinalIgnoreCase))
                return RootComponentLabel(parts[1], parts[1]) + " ↔ " + RootComponentLabel(parts[2], parts[2]);
            if (parts.Length == 2 && string.Equals(parts[0], "removed", StringComparison.OrdinalIgnoreCase))
                return RootComponentLabel(parts[1], parts[1]) + " 제거됨";
            if (evidence.StartsWith("metrics:", StringComparison.OrdinalIgnoreCase))
                return "결말 지표 · " + evidence.Substring("metrics:".Length).Replace(",", " · ");
            return evidence;
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
            if (_ability.Equals(ability))
                return;
            _ability = ability;
            _copySourceCaptured = false;
            _selectedTarget = null;
            _selectedSecondaryTarget = null;
            _selectedCoord = null;
            _movementSelectionFeedback = string.Empty;
            if (ability == AbilityKind.Restore && _lastRestorableTarget.HasValue)
            {
                _selectedTarget = _lastRestorableTarget;
                _selectedCoord = _lastRestorableCoord;
            }
            if (_service != null)
                UpdateSelectionVisual(_service.CurrentView);
            PlaySfx(AssetClip("UiMoveSound"));
        }

        private Sprite AbilityIcon(string name)
        {
            if (_assets == null)
                return _d20Sprite;
            switch (name)
            {
                case "Move": return _assets.MoveIcon;
                case "Copy": return _assets.CopyIcon;
                case "Delete": return _assets.DeleteIcon;
                case "Restore": return _assets.HeartIcon;
                case "Undo": return _assets.CopyIcon;
                case "Connect": return _assets.CopyIcon;
                case "Interact": return _assets.InteractIcon;
                case "Search": return _assets.D20;
                case "SelectAll": return _assets.D20;
                default: return _assets.D20;
            }
        }

        private string SecondaryObjectiveText(RunView view)
        {
            var objectives = new List<string>();
            if (_serverOnline && _serverRun?.openLoops != null)
            {
                for (int i = 0; i < _serverRun.openLoops.Length && objectives.Count < 2; i++)
                {
                    string summary = _serverRun.openLoops[i]?.summary;
                    if (!string.IsNullOrWhiteSpace(summary)) objectives.Add("• " + summary.Trim());
                }
            }
            else
            {
                for (int i = 0; i < view.OpenLoops.Count && objectives.Count < 2; i++)
                    if (!string.IsNullOrWhiteSpace(view.OpenLoops[i]) &&
                        !string.Equals(view.OpenLoops[i], view.CurrentStoryBeatObjective, StringComparison.Ordinal))
                        objectives.Add("• " + view.OpenLoops[i]);
            }
            if (objectives.Count == 0) objectives.Add("• 현재 보조 목표 없음");
            return string.Join("\n", objectives);
        }

        private string ProgressLedgerText(RunView view)
        {
            int debt = _serverOnline ? _serverRun?.metrics?.technicalDebt ?? view.TechnicalDebt : view.TechnicalDebt;
            int openDebt = _serverOnline ? _serverRun?.technicalDebtEntries?.Length ?? 0 :
                CountUnresolvedDebt(view.TechnicalDebtEntries);
            int choices = _serverOnline ? _serverRun?.majorChoices?.Length ?? 0 : view.MajorChoices.Count;
            return "관리자 권한 " + AdminAccessLevel(view) + "/3\n기술 부채 " + debt +
                   " · 미해결 " + openDebt + "\n선택 회수 대기 " + choices;
        }

        private static int CountUnresolvedDebt(IReadOnlyList<TechnicalDebtEntry> entries)
        {
            int count = 0;
            for (int i = 0; i < entries.Count; i++) if (!entries[i].IsResolved) count++;
            return count;
        }

        private AbilityKind[] RecommendedActions(RunView view)
        {
            var values = new List<AbilityKind>();
            AbilityKind objectiveSkill;
            if (!TryGetServerObjectiveAbility(out objectiveSkill))
            {
                objectiveSkill = AbilityKind.Copy;
                for (int i = 0; i < view.RequiredBeats.Count; i++)
                {
                    CampaignBeatState beat = view.RequiredBeats[i];
                    if (!beat.IsCompleted && !beat.IsSkipped)
                    {
                        objectiveSkill = beat.TriggerAbility;
                        break;
                    }
                }
            }
            bool objectiveOutOfRange = TryGetServerObjectiveTarget(view, out _, out GridCoord objectiveCoord) &&
                                       TryGetPlayerPosition(view, out GridCoord playerCoord) &&
                                       playerCoord.ManhattanDistance(objectiveCoord) > ObjectiveAbilityRange(objectiveSkill);
            if (!_encounterMoveRequired && objectiveOutOfRange && !values.Contains(AbilityKind.Move))
                values.Add(AbilityKind.Move);
            if (!values.Contains(objectiveSkill)) values.Add(objectiveSkill);
            if (!_encounterMoveRequired && !values.Contains(AbilityKind.Move)) values.Add(AbilityKind.Move);
            AbilityKind contextual = _encounterMoveRequired ? AbilityKind.Delete : AbilityKind.Connect;
            if (!values.Contains(contextual)) values.Add(contextual);
            if (values.Count < 2) values.Add(AbilityKind.Restore);
            return values.ToArray();
        }

        private bool TryGetServerObjectiveAbility(out AbilityKind ability)
        {
            ability = AbilityKind.Copy;
            if (!_serverOnline || string.IsNullOrWhiteSpace(_serverRun?.currentStoryBeat?.requiredAbility))
                return false;
            if (!Enum.TryParse(_serverRun.currentStoryBeat.requiredAbility, true, out ability))
                return false;
            return ability == AbilityKind.Move || TurnRequest.IsPublicKeyboardSkill(ability);
        }

        private string ObjectiveHudText(RunView view)
        {
            AbilityKind[] recommendations = RecommendedActions(view);
            string[] labels = new string[recommendations.Length];
            for (int i = 0; i < recommendations.Length; i++)
                labels[i] = AbilityPlayerLabel(recommendations[i]);
            string objective = StoryBeatObjective(view);
            if (TryGetServerObjectiveTarget(view, out GameApiClient.EntitySnapshot target, out GridCoord targetCoord) &&
                TryGetPlayerPosition(view, out GridCoord player))
            {
                int distance = player.ManhattanDistance(targetCoord);
                AbilityKind ability = AbilityFromServerName(_serverRun.currentStoryBeat.requiredAbility);
                objective = ObjectiveActionText(target.name, ability) + "\n위치  " +
                            DirectionLabel(player, targetCoord) + " " + distance + "칸 · 미니맵 분홍 표식";
            }
            return objective + "\n추천  " + string.Join(" → ", labels) +
                   "\n진행  권한 " + AdminAccessLevel(view) + "/3 · 의미 턴 " +
                   CurrentTurn(view) + "/" + TurnLimit(view);
        }

        private string ActionGuidanceText(RunView view)
        {
            if (CanSubmitCurrentSelection())
                return ExecutionPreview(view);
            string route = string.Empty;
            if (TryGetServerObjectiveTarget(view, out GameApiClient.EntitySnapshot target, out GridCoord targetCoord) &&
                TryGetPlayerPosition(view, out GridCoord player))
            {
                AbilityKind ability = AbilityFromServerName(_serverRun.currentStoryBeat.requiredAbility);
                int distance = player.ManhattanDistance(targetCoord);
                if (distance > ObjectiveAbilityRange(ability))
                    route = "현재 목표 " + target.name + " · " + DirectionLabel(player, targetCoord) + " " + distance + "칸\n";
            }
            return route + NarrativeSelectionHint() +
                   "\nMOVE는 턴을 쓰지 않고, 키보드 기술은 D20과 의미 턴 1회를 사용합니다.";
        }

        private bool TryGetServerObjectiveTarget(RunView view, out GameApiClient.EntitySnapshot target,
            out GridCoord targetCoord)
        {
            target = null;
            targetCoord = default;
            GameApiClient.StoryBeatSnapshot beat = _serverOnline ? _serverRun?.currentStoryBeat : null;
            if (beat == null || _serverRun?.entities == null)
                return false;
            TryGetPlayerPosition(view, out GridCoord player);
            int bestDistance = int.MaxValue;
            for (int i = 0; i < _serverRun.entities.Length; i++)
            {
                GameApiClient.EntitySnapshot entity = _serverRun.entities[i];
                if (entity?.position == null || entity.state == null)
                    continue;
                bool evidenceMatches = !string.IsNullOrWhiteSpace(beat.requiredEvidenceKey) &&
                                       string.Equals(entity.state.evidenceKey, beat.requiredEvidenceKey,
                                           StringComparison.OrdinalIgnoreCase);
                bool roleMatches = string.IsNullOrWhiteSpace(beat.requiredEvidenceKey) &&
                                   !string.IsNullOrWhiteSpace(beat.requiredCampaignRole) &&
                                   string.Equals(entity.state.campaignRole, beat.requiredCampaignRole,
                                       StringComparison.OrdinalIgnoreCase);
                if (!evidenceMatches && !roleMatches)
                    continue;
                var coord = new GridCoord(entity.position.x, entity.position.y);
                int distance = player.ManhattanDistance(coord);
                if (target != null && distance >= bestDistance)
                    continue;
                target = entity;
                targetCoord = coord;
                bestDistance = distance;
            }
            return target != null;
        }

        private static int ObjectiveAbilityRange(AbilityKind ability)
        {
            switch (ability)
            {
                case AbilityKind.Search: return 6;
                case AbilityKind.Delete: return 3;
                case AbilityKind.Copy:
                case AbilityKind.Connect:
                case AbilityKind.Restore: return 4;
                default: return 0;
            }
        }

        private static string ObjectiveActionText(string targetName, AbilityKind ability)
        {
            switch (ability)
            {
                case AbilityKind.Search: return WithObjectParticle(targetName) + " 찾아 Ctrl F로 조사하세요.";
                case AbilityKind.Delete: return WithObjectParticle(targetName) + " 찾아 Delete로 공격하세요.";
                case AbilityKind.Copy: return WithObjectParticle(targetName) + " 찾아 Ctrl C로 복제하세요.";
                case AbilityKind.Connect: return targetName + "와 연결할 대상을 찾아 Ctrl K로 연결하세요.";
                case AbilityKind.Restore: return WithObjectParticle(targetName) + " Ctrl R로 복구하세요.";
                default: return targetName + "에게 이동하세요.";
            }
        }

        private static string DirectionLabel(GridCoord from, GridCoord to)
        {
            int dx = to.X - from.X;
            int dy = to.Y - from.Y;
            string vertical = dy > 0 ? "북" : dy < 0 ? "남" : string.Empty;
            string horizontal = dx > 0 ? "동" : dx < 0 ? "서" : string.Empty;
            string direction = vertical + horizontal;
            return string.IsNullOrEmpty(direction) ? "현재 위치" : direction + "쪽";
        }

        private static string AbilityPlayerLabel(AbilityKind ability)
        {
            switch (ability)
            {
                case AbilityKind.Move: return "W 이동";
                case AbilityKind.Copy: return "Ctrl C 복제";
                case AbilityKind.Delete: return "Delete 단일 공격";
                case AbilityKind.Connect: return "Ctrl K 연결";
                case AbilityKind.Restore: return "Ctrl R 복구";
                case AbilityKind.Undo: return "Ctrl Z 2턴 역행";
                case AbilityKind.Search: return "Ctrl F 조사";
                case AbilityKind.SelectAll: return "Ctrl A 범위 공격";
                default: return ability.ToString();
            }
        }

        private string ExecutionPreview(RunView view)
        {
            string target = _ability == AbilityKind.Move
                ? (_selectedCoord.HasValue ? _selectedCoord.Value.ToString() : "목적지 미선택")
                : _ability == AbilityKind.Undo ? "직전 가역 행동"
                : _selectedTarget.HasValue ? DisplayEntityName(view, _selectedTarget.Value) : "대상 미선택";
            if (_ability == AbilityKind.Connect && _selectedSecondaryTarget.HasValue)
                target += " + " + DisplayEntityName(view, _selectedSecondaryTarget.Value);
            bool consumes = _ability != AbilityKind.Move;
            string risk = _ability == AbilityKind.Move ? "경로상 사건 활성화 가능"
                : _ability == AbilityKind.Interact ? "낮음 · 대상 확인"
                : _ability == AbilityKind.Delete || _ability == AbilityKind.Undo ? "높음 · 기술 부채 발생 가능"
                : _ability == AbilityKind.Connect ? "중간 · 관계/배치 결과"
                : "중간 · D20 결과 적용";
            return "대상  " + target + "\n스킬  " + (_ability == AbilityKind.Move ? "MOVE" : _ability.ToString().ToUpperInvariant()) +
                   "\n문맥  " + ContextPreview(_ability, view) + "\n턴 소비  " +
                   (consumes ? "예 · D20 사용" : "아니오 · D20 없음") + "\n예상 위험  " + risk;
        }

        private string ContextPreview(AbilityKind ability, RunView view)
        {
            if (ability == AbilityKind.Move) return "안전 이동";
            if (ability == AbilityKind.Copy) return "배치";
            if (ability == AbilityKind.Search) return "조사";
            if (ability == AbilityKind.Interact)
            {
                if (_selectedTarget.HasValue)
                {
                    if (TryGetServerEntity(_selectedTarget, out GameApiClient.EntitySnapshot serverEntity) &&
                        string.Equals(serverEntity.kind, "npc", StringComparison.OrdinalIgnoreCase))
                        return "협상";
                    for (int i = 0; i < view.Entities.Count; i++)
                        if (view.Entities[i].EntityId == _selectedTarget.Value && view.Entities[i].Kind == EntityKind.Npc)
                            return "협상";
                }
                return "조사";
            }
            if (ability == AbilityKind.Delete || ability == AbilityKind.SelectAll) return "전투";
            if (ability == AbilityKind.Connect)
            {
                if (_selectedTarget.HasValue)
                {
                    if (TryGetServerEntity(_selectedTarget, out GameApiClient.EntitySnapshot serverEntity) &&
                        string.Equals(serverEntity.kind, "npc", StringComparison.OrdinalIgnoreCase))
                        return "협상";
                    for (int i = 0; i < view.Entities.Count; i++)
                        if (view.Entities[i].EntityId == _selectedTarget.Value && view.Entities[i].Kind == EntityKind.Npc)
                            return "협상";
                }
                return "배치";
            }
            return "배치";
        }

        private static string ActionContextLabel(string context)
        {
            switch ((context ?? string.Empty).ToUpperInvariant())
            {
                case "COMBAT": return "전투";
                case "INVESTIGATION": return "조사";
                case "NEGOTIATION": return "협상";
                case "DEPLOYMENT": return "배치";
                case "SAFE_TRAVEL":
                case "MOVE": return "안전 이동";
                default: return string.IsNullOrWhiteSpace(context) ? "--" : context;
            }
        }

        private static string ShortNarrative(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return "확정된 짧은 서사가 없습니다.";
            string value = text.Trim();
            int sentences = 0;
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (c != '.' && c != '!' && c != '?') continue;
                sentences++;
                if (sentences == 4) return value.Substring(0, i + 1);
            }
            return value;
        }

        private static string StateChangeSummary(IReadOnlyList<string> events)
        {
            if (events == null || events.Count == 0) return "상태 변화 없음";
            var values = new List<string>();
            for (int i = 0; i < events.Count && values.Count < 3; i++)
            {
                string label = HumanizeEvent(events[i]);
                if (!string.IsNullOrWhiteSpace(label)) values.Add("• " + label);
            }
            return values.Count == 0 ? "상태 변화 없음" : string.Join("\n", values);
        }

        private static string StateChangeSummary(GameApiClient.EventSnapshot[] events)
        {
            if (events == null || events.Length == 0) return "상태 변화 없음";
            var values = new List<string>();
            for (int i = 0; i < events.Length && values.Count < 3; i++)
            {
                string label = HumanizeServerEvent(events[i]);
                if (!string.IsNullOrWhiteSpace(label)) values.Add("• " + label);
            }
            return values.Count == 0 ? "상태 변화 없음" : string.Join("\n", values);
        }

        private string SelectionHint(RunView view)
        {
            string ability = _ability.ToString();
            if (ability == "Undo") return "직전 의미 턴 2개의 기계 상태와 턴 카운터를 되돌립니다.";
            if (ability == "Move")
            {
                if (_encounterMoveRequired)
                    return _selectedCoord.HasValue
                        ? "사건 배치 위치 선택 · 현재 위치에서 5칸 이내만 의미 턴 Move로 제출됩니다."
                        : "ACTIVE ENCOUNTER · COPY/DELETE/CONNECT/RESTORE/UNDO 중 상황에 맞는 스킬을 선택하세요.";
                return _selectedCoord.HasValue ? "안전 탐색 목적지 선택 완료 · 우선 /travel로 검증" : "맵에서 탐색 목적지를 선택하세요.";
            }
            if (ability == "Copy")
            {
                if (!_selectedTarget.HasValue) return "먼저 복제할 개체를 선택하세요.";
                if (!_selectedCoord.HasValue) return "원본 유지 · 이제 빈 타일을 선택하세요.";
                return DisplayEntityName(view, _selectedTarget.Value) + " → " + _selectedCoord.Value;
            }
            if (ability == "Connect")
            {
                if (!_selectedTarget.HasValue) return "첫 번째 연결 개체를 선택하세요.";
                if (!_selectedSecondaryTarget.HasValue) return "두 번째 연결 개체를 선택하세요.";
                return "연결 대상 2개 선택 완료";
            }
            if (ability == "Interact")
            {
                if (!_selectedTarget.HasValue) return "책, 상자 또는 NPC를 선택하세요.";
                return IsSkillEnabledForCurrentTarget(AbilityKind.Interact)
                    ? DisplayEntityName(view, _selectedTarget.Value) + " · 상호작용 가능"
                    : "대상이 너무 멉니다. 2칸 이내로 이동하세요.";
            }
            if (ability == "Restore")
            {
                if (_selectedTarget.HasValue)
                {
                    string name = _lastRestorableTarget == _selectedTarget && !string.IsNullOrWhiteSpace(_lastRestorableName)
                        ? _lastRestorableName
                        : DisplayEntityName(view, _selectedTarget.Value);
                    return name + " · 권위 스냅샷 복원 대상";
                }
                return "최근 손상·삭제된 대상이나 플레이어를 선택하세요.";
            }
            return _selectedTarget.HasValue ? DisplayEntityName(view, _selectedTarget.Value) + " 선택됨" : "대상을 선택하세요.";
        }

        private void LoadSettings()
        {
            _musicVolume = PlayerPrefs.GetFloat(MusicVolumeKey, 0.65f);
            _sfxVolume = PlayerPrefs.GetFloat(SfxVolumeKey, 0.8f);
            _gmEnabled = PlayerPrefs.GetInt(GmEnabledKey, 1) != 0;
        }

        private void SaveSettings()
        {
            PlayerPrefs.SetFloat(MusicVolumeKey, _musicVolume);
            PlayerPrefs.SetFloat(SfxVolumeKey, _sfxVolume);
            PlayerPrefs.SetInt(GmEnabledKey, _gmEnabled ? 1 : 0);
            PlayerPrefs.Save();
        }

        private void ApplyAudioVolumes()
        {
            _audioController?.SetVolumes(_musicVolume, _sfxVolume);
        }

        private void AddLog(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return;
            _sessionLog.Add(message.Trim());
            if (_sessionLog.Count > 18)
                _sessionLog.RemoveAt(0);
        }

        private Texture2D MakeTexture(Color color, string textureName)
        {
            var texture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            texture.name = "Runtime " + textureName;
            texture.SetPixel(0, 0, color);
            texture.Apply();
            texture.wrapMode = TextureWrapMode.Repeat;
            texture.filterMode = FilterMode.Point;
            _runtimeTextures.Add(texture);
            return texture;
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

        private static EntityView FindPlayer(RunView view)
        {
            for (int i = 0; i < view.Entities.Count; i++)
                if (view.Entities[i].EntityId == view.PlayerEntityId)
                    return view.Entities[i];
            return null;
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
            foreach (KeyValuePair<Guid, EntityVisual> pair in _entityVisuals)
            {
                EntityVisual visual = pair.Value;
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
            if (_serverOnline && _serverRun?.world != null && TryGetPlayerPosition(view, out GridCoord position))
            {
                GameApiClient.WorldSnapshot world = _serverRun.world;
                GameApiClient.AreaSnapshot[] areas = world.areas;
                if (areas != null)
                {
                    string mappedAreaId = world.AreaIdAt(position.X, position.Y);
                    if (!string.IsNullOrWhiteSpace(mappedAreaId))
                    {
                        for (int i = 0; i < areas.Length; i++)
                        {
                            GameApiClient.AreaSnapshot mappedArea = areas[i];
                            if (mappedArea != null && string.Equals(mappedArea.id, mappedAreaId,
                                    StringComparison.Ordinal))
                                return FirstNonEmpty(mappedArea.nameKo, mappedArea.name, mappedArea.id);
                        }
                    }

                    for (int i = 0; i < areas.Length; i++)
                    {
                        GameApiClient.AreaSnapshot area = areas[i];
                        GameApiClient.BoundsSnapshot bounds = area?.bounds;
                        if (bounds != null && position.X >= bounds.x && position.X < bounds.x + bounds.width &&
                            position.Y >= bounds.y && position.Y < bounds.y + bounds.height)
                            return FirstNonEmpty(area.nameKo, area.name, area.id);
                    }
                }
                GameApiClient.PointSnapshot[] points = world.points;
                GameApiClient.PointSnapshot nearest = null;
                int best = int.MaxValue;
                if (points != null)
                {
                    for (int i = 0; i < points.Length; i++)
                    {
                        GameApiClient.PointSnapshot point = points[i];
                        if (point == null) continue;
                        int distance = Math.Abs(position.X - point.x) + Math.Abs(position.Y - point.y);
                        if (distance < best) { best = distance; nearest = point; }
                    }
                }
                if (nearest != null && !string.IsNullOrWhiteSpace(nearest.name))
                    return nearest.name;
            }
            return string.IsNullOrWhiteSpace(view.CurrentAreaName) ? "알 수 없는 변경지대" : view.CurrentAreaName;
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

        private string CampaignTitle(RunView view)
        {
            return CampaignCatalog.CampaignTitle;
        }

        private string CampaignPremise(RunView view)
        {
            if (_serverOnline && !string.IsNullOrWhiteSpace(_serverRun?.premise)) return _serverRun.premise;
            if (_serverCampaign != null && !string.IsNullOrWhiteSpace(_serverCampaign.premiseKo)) return _serverCampaign.premiseKo;
            if (_serverCampaign != null && !string.IsNullOrWhiteSpace(_serverCampaign.premise)) return _serverCampaign.premise;
            return string.IsNullOrWhiteSpace(view.CampaignPremise)
                ? "넙죽이는 코드리아에서 관리자 키보드와 권한 3단계를 찾아 ROOT_SYSTEM으로 향합니다."
                : view.CampaignPremise;
        }

        private string StoryBeat(RunView view)
        {
            if (_serverOnline && !string.IsNullOrWhiteSpace(_serverRun?.currentBeat)) return _serverRun.currentBeat;
            if (_serverOnline && !string.IsNullOrWhiteSpace(_serverRun?.currentStoryBeat?.title)) return _serverRun.currentStoryBeat.title;
            return string.IsNullOrWhiteSpace(view.CurrentStoryBeat) ? "첫 장면을 확정하세요" : view.CurrentStoryBeat;
        }

        private string StoryBeatObjective(RunView view)
        {
            if (_serverOnline && !string.IsNullOrWhiteSpace(_serverRun?.currentStoryBeat?.description))
                return _serverRun.currentStoryBeat.description;
            if (_serverOnline && _serverRun?.activeQuests != null && _serverRun.activeQuests.Length > 0)
            {
                GameApiClient.QuestSnapshot quest = _serverRun.activeQuests[0];
                if (!string.IsNullOrWhiteSpace(quest?.summary)) return quest.summary;
                if (!string.IsNullOrWhiteSpace(quest?.currentStep)) return quest.currentStep;
            }
            return string.IsNullOrWhiteSpace(view.CurrentStoryBeatObjective)
                ? "목적지 또는 관리자 키보드 스킬과 대상을 선택하세요."
                : view.CurrentStoryBeatObjective;
        }

        private int CurrentTurn(RunView view) => _serverOnline && _serverRun != null ? _serverRun.currentTurn : view.CurrentTurn;
        private int TurnLimit(RunView view) => _serverOnline && _serverRun != null && _serverRun.turnLimit > 0 ? _serverRun.turnLimit : view.TurnLimit;
        private int RemainingTurns(RunView view) => _serverOnline && _serverRun != null ? Math.Max(0, _serverRun.remainingTurns) : view.RemainingTurns;
        private int Focus(RunView view) => _serverOnline && _serverRun != null ? _serverRun.focus : view.Focus;
        private int Pressure(RunView view) => _serverOnline && _serverRun != null ? _serverRun.pressure : 0;
        private int AdminAccessLevel(RunView view) => _serverOnline && _serverRun != null
            ? Mathf.Clamp(_serverRun.adminLevel, 0, 3)
            : view.AdminAccess;
        private int TravelCount(RunView view) => _serverOnline && _serverRun != null ? _serverRun.safeTravelCount : view.TravelTime;

        private string MetricsLine(RunView view)
        {
            GameApiClient.CampaignMetricsSnapshot metrics = _serverOnline ? _serverRun?.metrics : null;
            int stability = metrics?.worldStability ?? view.WorldStability;
            int autonomy = metrics?.worldAutonomy ?? view.WorldAutonomy;
            int trust = metrics?.publicTrust ?? view.PublicTrust;
            int debt = metrics?.technicalDebt ?? view.TechnicalDebt;
            int bond = metrics?.companionBond ?? view.CompanionBond;
            int turnPressure = metrics?.turnPressure ?? view.TurnPressure;
            return "안정 " + stability + " · 자율 " + autonomy + " · 신뢰 " + trust + " · 부채 " + debt +
                   " · 동료 " + bond + " · 압박 " + turnPressure;
        }

        private string CampaignPhaseLabel(RunView view)
        {
            string phase = _serverOnline && _serverRun != null
                ? FirstNonEmpty(_serverRun.campaignPhase, _serverRun.currentAct, _serverRun.currentStoryBeat?.act)
                : view.Phase.ToString();
            switch ((phase ?? string.Empty).ToLowerInvariant())
            {
                case "awakening":
                case "introduction": return "1단계 · 코드리아 추락";
                case "permission_one":
                case "adaptation": return "2단계 · 관리자 권한 I";
                case "permission_two":
                case "expansion": return "3단계 · 관리자 권한 II";
                case "truth_index":
                case "truth": return "4단계 · 통제 내부 오류";
                case "legacy_judgment":
                case "backflow": return "5단계 · 기술 부채 역류";
                case "root_resolution":
                case "finale": return "6단계 · ROOT_SYSTEM";
                default: return string.IsNullOrWhiteSpace(phase) ? "1단계 · 코드리아 추락" : phase;
            }
        }

        private string CurrentBiomeLabel(RunView view)
        {
            if (!TryGetPlayerPosition(view, out GridCoord position)) return "바이옴 확인 중";
            string id = BiomeIdAt(view, position);
            switch (id)
            {
                case "root_system": return "루트 시스템 · 코어";
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

        private string FinaleGateLabel(RunView view)
        {
            if (_serverOnline && _serverRun?.rootPuzzle != null)
            {
                string matched = string.IsNullOrWhiteSpace(_serverRun.rootPuzzle.matchedEndingId)
                    ? "레시피 미완성"
                    : EndingTitle(_serverRun.rootPuzzle.matchedEndingId);
                return "ROOT_SYSTEM " + FirstNonEmpty(_serverRun.rootPuzzle.status, "locked") + " · " + matched;
            }
            if (_serverOnline && _serverRun?.rootGate != null)
                return _serverRun.rootGate.eligible
                    ? "ROOT_SYSTEM 개방 · 최종 배치 대기"
                    : "ROOT_SYSTEM 잠김 · 관리자 권한 " + AdminAccessLevel(view) + "/3";
            return AdminAccessLevel(view) >= 3
                ? "ROOT_SYSTEM 개방 · 최종 배치를 선택하세요"
                : "ROOT_SYSTEM 잠김 · 관리자 권한 " + AdminAccessLevel(view) + "/3";
        }

        private string SubmissionButtonLabel(RunView view)
        {
            if (_ability == AbilityKind.Move)
                return "MOVE 실행\n\nD20 없음\n턴 유지";
            if (_ability == AbilityKind.Interact)
                return "상호작용\n\n서버 D20\n+ 캠페인 턴";
            return "USE_SKILL 실행\n\n서버 D20\n+ 캠페인 턴";
        }
        private int MaxFocus(RunView view) => _serverOnline && _serverRun != null
            ? (_serverRun.maxFocus > 0 ? _serverRun.maxFocus : Math.Max(10, _serverRun.focus))
            : view.MaxFocus;
        private int Health(RunView view)
            => TryGetServerPlayerState(out GameApiClient.EntityStateSnapshot state) && state.maxHp > 0 ? state.hp : view.Health;
        private int MaxHealth(RunView view)
            => TryGetServerPlayerState(out GameApiClient.EntityStateSnapshot state) && state.maxHp > 0 ? state.maxHp : view.MaxHealth;
        private string EndingCode(RunView view) => _serverOnline && _serverRun != null ? _serverRun.endingCode : view.EndingCode;

        private string PlayerDisplayName(RunView view)
        {
            return CampaignCatalog.ProtagonistName;
        }

        private bool TryGetServerPlayerState(out GameApiClient.EntityStateSnapshot state)
        {
            state = null;
            if (!_serverOnline || _serverRun?.entities == null)
                return false;
            for (int i = 0; i < _serverRun.entities.Length; i++)
            {
                GameApiClient.EntitySnapshot entity = _serverRun.entities[i];
                if (entity != null && string.Equals(entity.id, _serverRun.playerEntityId, StringComparison.OrdinalIgnoreCase))
                {
                    state = entity.state;
                    return state != null;
                }
            }
            return false;
        }

        private bool RunIsPlaying(RunView view)
        {
            if (_serverOnline && _serverRun != null)
                return string.Equals(_serverRun.status, "active", StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(_serverRun.status, "playing", StringComparison.OrdinalIgnoreCase);
            return view.Status == RunStatus.Playing;
        }

        private int CanonicalFactCount(RunView view)
            => _serverOnline && _serverRun?.canonicalFacts != null ? _serverRun.canonicalFacts.Length : view.CanonicalFacts.Count;

        private int OpenLoopCount(RunView view)
            => _serverOnline && _serverRun?.openLoops != null ? _serverRun.openLoops.Length : view.OpenLoops.Count;

        private List<string> MemoryLines(RunView view)
        {
            var lines = new List<string>();
            if (_serverOnline && _serverRun != null)
            {
                lines.Add("⌘ 관리자 권한 · " + Mathf.Clamp(_serverRun.adminLevel, 0, 3) + "/3");
                int serverDebt = _serverRun.metrics?.technicalDebt ?? 0;
                lines.Add("⚠ 기술 부채 · " + serverDebt + " · 원장 " +
                          (_serverRun.technicalDebtEntries?.Length ?? 0));
                if (_serverRun.majorChoices != null && _serverRun.majorChoices.Length > 0)
                    lines.Add("↩ 선택 회수 · " + _serverRun.majorChoices[_serverRun.majorChoices.Length - 1]);
                if (_serverRun.rootPuzzle != null)
                {
                    string matched = string.IsNullOrWhiteSpace(_serverRun.rootPuzzle.matchedEndingId)
                        ? "공간 레시피 선택 대기"
                        : EndingTitle(_serverRun.rootPuzzle.matchedEndingId);
                    lines.Add("⌘ ROOT_SYSTEM · " + FirstNonEmpty(_serverRun.rootPuzzle.status, "locked") + " · " + matched);
                    if (_serverRun.rootPuzzle.evidence != null)
                    {
                        for (int i = 0; i < _serverRun.rootPuzzle.evidence.Length && i < 4; i++)
                        {
                            string evidence = _serverRun.rootPuzzle.evidence[i];
                            if (string.IsNullOrWhiteSpace(evidence)) continue;
                            lines.Add("◆ " + RootEvidenceLabel(evidence));
                        }
                    }
                }
                if (_serverRun.canonicalFacts != null)
                {
                    for (int i = 0; i < _serverRun.canonicalFacts.Length && i < 4; i++)
                    {
                        GameApiClient.FactSnapshot fact = _serverRun.canonicalFacts[i];
                        if (fact == null) continue;
                        string value = fact.predicate == "layout_hash" ? ShortHash(fact.value) : fact.value;
                        if (!string.IsNullOrWhiteSpace(value)) lines.Add("◆ 사실 · " + value);
                    }
                }
                if (_serverRun.openLoops != null)
                {
                    for (int i = 0; i < _serverRun.openLoops.Length && i < 4; i++)
                    {
                        GameApiClient.OpenLoopSnapshot loop = _serverRun.openLoops[i];
                        if (loop != null && !string.IsNullOrWhiteSpace(loop.summary))
                            lines.Add("◇ 열린 훅 · " + loop.summary + " (T" + loop.expiresTurn + ")");
                    }
                }
                if (_serverRun.rumors != null)
                {
                    for (int i = 0; i < _serverRun.rumors.Length && i < 2; i++)
                    {
                        GameApiClient.RumorSnapshot rumor = _serverRun.rumors[i];
                        if (rumor != null && !string.IsNullOrWhiteSpace(rumor.summary))
                            lines.Add("? 소문 · " + rumor.summary);
                    }
                }
                if (_serverRun.endingCandidateDetails != null && _serverRun.endingCandidateDetails.Length > 0)
                {
                    for (int i = 0; i < _serverRun.endingCandidateDetails.Length && i < 5; i++)
                    {
                        GameApiClient.EndingCandidateSnapshot ending = _serverRun.endingCandidateDetails[i];
                        if (ending != null && !string.IsNullOrWhiteSpace(ending.title))
                            lines.Add((ending.eligible ? "◆" : "⌛") + " 결말 후보 · " + ending.title);
                    }
                }
                else
                {
                    AppendPrefixed(lines, "⌛ 결말 후보 · ", _serverRun.endingCandidates, 3);
                }
                if (_serverRun.npcMemories != null)
                {
                    for (int i = 0; i < _serverRun.npcMemories.Length && i < 3; i++)
                    {
                        GameApiClient.NpcMemorySnapshot memory = _serverRun.npcMemories[i];
                        if (memory == null) continue;
                        string text = string.IsNullOrWhiteSpace(memory.memory) ? memory.summary : memory.memory;
                        if (!string.IsNullOrWhiteSpace(text)) lines.Add("◎ " + ServerEntityName(memory.npcId, memory.npcName) + " 기억 · " + text);
                    }
                }
                if (_serverRun.npcRelationships != null)
                {
                    for (int i = 0; i < _serverRun.npcRelationships.Length && i < 3; i++)
                    {
                        GameApiClient.NpcRelationshipSnapshot relation = _serverRun.npcRelationships[i];
                        if (relation == null) continue;
                        string stance = string.IsNullOrWhiteSpace(relation.stance)
                            ? string.IsNullOrWhiteSpace(relation.label) ? "관계" : relation.label
                            : relation.stance;
                        int affinity = relation.affinity != 0 ? relation.affinity : relation.score;
                        lines.Add("↔ " + ServerEntityName(relation.npcId, relation.npcName) + " · " + stance + " " + Signed(affinity) +
                                  " (신뢰 " + Signed(relation.trust) + ", 공포 " + Signed(relation.fear) + ")");
                    }
                }
                return lines;
            }

            lines.Add("⌘ 관리자 권한 · " + view.AdminAccess + "/3");
            lines.Add("⚠ 기술 부채 · " + view.TechnicalDebt + " · 미해결 " +
                      CountUnresolvedDebt(view.TechnicalDebtEntries));
            if (view.MajorChoices.Count > 0)
                lines.Add("↩ 선택 회수 · " + view.MajorChoices[view.MajorChoices.Count - 1]);
            AppendPrefixed(lines, "◆ 사실 · ", view.CanonicalFacts, 4);
            AppendPrefixed(lines, "◇ 열린 훅 · ", view.OpenLoops, 4);
            AppendPrefixed(lines, "? 소문 · ", view.Rumors, 2);
            for (int i = 0; i < view.EndingCandidates.Count && i < 5; i++)
            {
                EndingCandidateView ending = view.EndingCandidates[i];
                lines.Add((ending.IsEligible ? "◆" : "⌛") + " 결말 후보 · " + ending.Title);
            }
            for (int i = 0; i < view.NpcMemories.Count && i < 4; i++)
            {
                NpcMemoryView memory = view.NpcMemories[i];
                string latest = string.IsNullOrWhiteSpace(memory.LatestMemory) ? "아직 기억 없음" : memory.LatestMemory;
                lines.Add("◎ " + memory.NpcName + " [" + memory.Role + ", 관계 " + Signed(memory.Affinity) + "] · " + latest);
            }
            return lines;
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

        private static void AppendPrefixed(List<string> destination, string prefix, IReadOnlyList<string> source, int maximum)
        {
            if (source == null) return;
            for (int i = 0; i < source.Count && i < maximum; i++)
                if (!string.IsNullOrWhiteSpace(source[i])) destination.Add(prefix + source[i]);
        }

        private string NarrativeWithDialogue()
        {
            string text = _lastNarrative ?? string.Empty;
            for (int i = 0; i < _lastDialogue.Length; i++)
                if (!string.IsNullOrWhiteSpace(_lastDialogue[i])) text += "\n“" + _lastDialogue[i].Trim() + "”";
            return text;
        }

        /// <summary>
        /// Spins the authored icosahedron die so it lands on the D20 value that already
        /// decided the action's outcome, instead of drawing its own random face.
        /// </summary>
        private void PresentDiceRoll(int result)
        {
            if (authoredDice == null || result < 1 || result > 20) return;
            authoredDice.RollTo(result);
        }

        private void ApplyServerTurnPresentation(GameApiClient.TurnSnapshot turn)
        {
            GameApiClient.DiceSnapshot dice = turn.dice;
            _lastD20 = dice?.raw ?? 0;
            PresentDiceRoll(_lastD20);
            _lastModifier = 0;
            var modifierParts = new List<string>();
            if (dice?.modifiers != null)
            {
                for (int i = 0; i < dice.modifiers.Length; i++)
                {
                    GameApiClient.DiceModifierSnapshot modifier = dice.modifiers[i];
                    if (modifier == null) continue;
                    _lastModifier += modifier.value;
                    if (modifierParts.Count < 3)
                        modifierParts.Add(ModifierSourceLabel(modifier.source) + " " + Signed(modifier.value));
                }
            }
            if (modifierParts.Count == 0 && dice != null && dice.modifier != 0)
            {
                _lastModifier = dice.modifier;
                modifierParts.Add("기본 수정치 " + Signed(dice.modifier));
            }
            _lastModifierBreakdown = modifierParts.Count == 0 ? "수정치 없음" : string.Join(", ", modifierParts);
            _lastDifficulty = dice?.difficulty ?? 0;
            _lastMechanicalScore = dice?.mechanicalScore ?? turn.mechanicalScore;
            _lastActionContext = ActionContextLabel(turn.actionContext);
            _lastOutcome = KoreanOutcome(turn.outcome);
            _lastAttempt = PlayerFacingAttempt(turn);
            _lastExplanation = ExplainResultDifference(turn);
            string serverNarrative = !string.IsNullOrWhiteSpace(turn.narrative?.body)
                ? turn.narrative.body
                : turn.narrative?.summary;
            _lastNarrative = !_gmEnabled
                ? "생성형 장면·대사 표시가 꺼져 있습니다. 권위 규칙 이벤트와 세계 기억은 그대로 적용되었습니다."
                : IsTechnicalNarrative(serverNarrative)
                    ? PlayerFacingFallbackNarrative(turn)
                    : serverNarrative.Trim();
            _lastDialogue = _gmEnabled ? turn.narrative?.dialogue ?? Array.Empty<string>() : Array.Empty<string>();
            _lastNarrativeFallbackUsed = turn.narrative == null || turn.narrative.fallbackUsed;
            _lastNarrativeModel = turn.narrative?.model ?? "deterministic";
            _playerActionUntil = Time.unscaledTime + 0.28f;
            AddLog("D20 " + _lastD20 + Signed(_lastModifier) + " vs " + _lastDifficulty + " · " + _lastOutcome);
            AddLog("실제 시도 · " + _lastAttempt);
            GameApiClient.EventSnapshot[] appliedEvents = turn.events ?? turn.stateDelta?.events;
            _lastStateChanges = StateChangeSummary(appliedEvents);
            if (appliedEvents != null)
                for (int i = 0; i < appliedEvents.Length; i++) AddLog(HumanizeServerEvent(appliedEvents[i]));
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

            if (_ability == AbilityKind.Restore)
            {
                _selectedTarget = _lastRestorableTarget;
                _selectedCoord = _lastRestorableCoord;
                if (_service != null)
                    UpdateSelectionVisual(_service.CurrentView);
            }
        }

        private void ClearRestorableTargetCache()
        {
            _lastRestorableTarget = null;
            _lastRestorableCoord = null;
            _lastRestorableName = null;
            if (_ability != AbilityKind.Restore)
                return;
            _selectedTarget = null;
            _selectedCoord = null;
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

        private string ExplainResultDifference(GameApiClient.TurnSnapshot turn)
        {
            string roll = _lastD20 > 0
                ? "D20 " + _lastD20 + " " + Signed(_lastModifier) + " / 난이도 " + _lastDifficulty
                : "주사위 판정 없음";
            string cost = turn.consequenceBudget > 0
                ? " · 결과에 따른 합병증 " + turn.consequenceBudget
                : string.Empty;
            return "판정 · " + _lastActionContext + " · " + roll + " · " + _lastModifierBreakdown + cost;
        }

        private static string HumanizeServerEvent(GameApiClient.EventSnapshot value)
        {
            if (value == null) return string.Empty;
            if (!string.IsNullOrWhiteSpace(value.text)) return value.text;
            switch ((value.type ?? string.Empty).ToLowerInvariant())
            {
                case "entity_investigated": return "대상의 단서를 조사함";
                case "search_completed": return "조사를 완료함";
                case "resource_changed": return value.delta == 0 ? "자원을 사용함" : "자원 " + Signed(value.delta);
                case "health_changed": return value.delta < 0 ? "대상에게 " + (-value.delta) + " 피해" : "체력 " + Signed(value.delta);
                case "area_attack_resolved": return "주변 적 광범위 공격 완료";
                case "turn_rewound": return "이전 턴의 변화를 되돌림";
                case "time_rewind_completed": return "최근 2턴 시간 역행 완료";
                case "turn_counter_rewound": return "턴 카운터를 2턴 되돌림";
                case "entity_restored":
                case "entity_state_restored": return "대상 상태 복구 완료";
                case "entity_removed": return "대상 제거 완료";
                case "turn_committed": return string.Empty;
                default: return HumanizeEvent(value.type);
            }
        }

        private string PlayerFacingAttempt(GameApiClient.TurnSnapshot turn)
        {
            string skill = AbilityPlayerLabel(AbilityFromServerName(TurnSkillId(turn)));
            string firstId = TurnTargetId(turn, false);
            string secondId = TurnTargetId(turn, true);
            string first = string.IsNullOrWhiteSpace(firstId) ? string.Empty : ServerEntityDisplayName(firstId);
            string second = string.IsNullOrWhiteSpace(secondId) ? string.Empty : ServerEntityDisplayName(secondId);
            if (!string.IsNullOrWhiteSpace(first) && !string.IsNullOrWhiteSpace(second))
                return first + "와(과) " + second + "에 " + skill + " 사용";
            if (!string.IsNullOrWhiteSpace(first))
                return first + "에 " + skill + " 사용";
            return skill + " 사용";
        }

        private string PlayerFacingFallbackNarrative(GameApiClient.TurnSnapshot turn)
        {
            AbilityKind ability = AbilityFromServerName(TurnSkillId(turn));
            string targetId = TurnTargetId(turn, false);
            string target = !string.IsNullOrWhiteSpace(targetId)
                ? ServerEntityDisplayName(targetId)
                : "주변 상황";
            string outcome = KoreanOutcome(turn?.outcome);
            switch (ability)
            {
                case AbilityKind.Search:
                    return "넙죽이는 " + WithObjectParticle(target) + " 자세히 조사했다. " + outcome +
                           "으로 숨겨진 단서가 세계 기록에 남았다.";
                case AbilityKind.Delete:
                    return "넙죽이는 " + target + "에게 단일 공격을 가했다. 전투 판정은 " + outcome + "으로 끝났다.";
                case AbilityKind.SelectAll:
                    return "넙죽이는 주변의 적들을 한꺼번에 공격했다. 광범위 공격은 " + outcome + "으로 끝났다.";
                case AbilityKind.Undo:
                    return "넙죽이가 시간을 되감자 최근 두 턴의 변화가 차례로 사라졌다.";
                case AbilityKind.Copy:
                    return "넙죽이는 " + target + "의 복제본을 선택한 위치에 배치했다.";
                case AbilityKind.Connect:
                    return "넙죽이는 선택한 두 대상 사이에 새로운 연결을 만들었다.";
                case AbilityKind.Restore:
                    return "넙죽이는 " + target + "의 이전 상태를 복구했다.";
                default:
                    return "선택한 행동의 결과가 코드리아의 현재 상태에 반영되었다.";
            }
        }

        private string ServerEntityDisplayName(string id)
        {
            GameApiClient.EntitySnapshot entity = FindServerEntity(_serverRun, id);
            return FirstNonEmpty(entity?.name, entity?.assetId, "선택한 대상");
        }

        private static string TurnSkillId(GameApiClient.TurnSnapshot turn)
        {
            return FirstNonEmpty(turn?.skillId, turn?.request?.skillId, turn?.request?.ability);
        }

        private static string TurnTargetId(GameApiClient.TurnSnapshot turn, bool secondary)
        {
            if (secondary)
                return FirstNonEmpty(turn?.targetIds != null && turn.targetIds.Length > 1 ? turn.targetIds[1] : null,
                    turn?.request?.secondaryTargetEntityId);
            return FirstNonEmpty(turn?.targetIds != null && turn.targetIds.Length > 0 ? turn.targetIds[0] : null,
                turn?.request?.targetEntityId);
        }

        private static bool IsTechnicalNarrative(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return true;
            string text = value.Trim();
            return text.StartsWith("Stated goal:", StringComparison.OrdinalIgnoreCase) ||
                   text.IndexOf("Server-authorized execution:", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   text.IndexOf("Intent fit:", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string ModifierSourceLabel(string source)
        {
            switch ((source ?? string.Empty).ToLowerInvariant())
            {
                case "keyboard_affinity": return "키보드 친화도";
                case "focus": return "집중력";
                case "admin_access": return "관리자 권한";
                case "special_skill": return "특수 스킬";
                case "encounter": return "사건 효과";
                default: return string.IsNullOrWhiteSpace(source) ? "수정치" : source.Replace('_', ' ');
            }
        }

        private static string WithObjectParticle(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "대상을";
            string text = value.Trim();
            char last = text[text.Length - 1];
            if (last >= '\uAC00' && last <= '\uD7A3')
                return text + ((last - '\uAC00') % 28 == 0 ? "를" : "을");
            return text + "을(를)";
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

        private static string ServerAbilityName(AbilityKind ability)
        {
            switch (ability)
            {
                case AbilityKind.Copy: return "copy";
                case AbilityKind.Delete: return "delete";
                case AbilityKind.Connect: return "connect";
                case AbilityKind.Restore: return "restore";
                case AbilityKind.Undo: return "undo";
                case AbilityKind.Interact: return "interact";
                case AbilityKind.Search: return "search";
                case AbilityKind.SelectAll: return "select_all";
                default: return "move";
            }
        }

        private static AbilityKind AbilityFromServerName(string value)
        {
            switch ((value ?? string.Empty).Trim().ToUpperInvariant())
            {
                case "COPY": return AbilityKind.Copy;
                case "DELETE": return AbilityKind.Delete;
                case "CONNECT": return AbilityKind.Connect;
                case "RESTORE": return AbilityKind.Restore;
                case "UNDO": return AbilityKind.Undo;
                case "INTERACT": return AbilityKind.Interact;
                case "SEARCH": return AbilityKind.Search;
                case "SELECT_ALL": return AbilityKind.SelectAll;
                default: return AbilityKind.Move;
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

        private static string KoreanOutcome(string outcome)
        {
            string value = (outcome ?? string.Empty).ToLowerInvariant();
            if (value.Contains("critical_success")) return "대성공";
            if (value == "success") return "성공";
            if (value.Contains("partial")) return "부분 성공";
            if (value.Contains("critical_failure")) return "대실패";
            if (value == "failure") return "실패";
            return string.IsNullOrWhiteSpace(outcome) ? "--" : outcome;
        }

        private string LayoutHash(RunView view)
            => _serverOnline && !string.IsNullOrWhiteSpace(_serverRun?.world?.layoutHash) ? _serverRun.world.layoutHash : view.Region.LayoutHash;

        private static string ShortHash(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "no-hash";
            return value.Substring(0, Math.Min(10, value.Length));
        }

        private static string Signed(int value) => value >= 0 ? "+" + value : value.ToString();
        private static string DisplayNumber(int value) => value == 0 ? "--" : value.ToString();

        private static string KoreanOutcome(RuleOutcome outcome)
        {
            switch (outcome)
            {
                case RuleOutcome.CriticalSuccess: return "대성공";
                case RuleOutcome.Success: return "성공";
                case RuleOutcome.PartialSuccess: return "부분 성공";
                case RuleOutcome.CriticalFailure: return "대실패";
                default: return "실패";
            }
        }

        private static string HumanizeEvent(string eventCode)
        {
            if (string.IsNullOrWhiteSpace(eventCode))
                return string.Empty;
            return eventCode.Replace('_', ' ').Replace(":", " · ");
        }

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

        private static AbilityKind AbilityByName(string name)
        {
            switch (name)
            {
                case "Copy": return AbilityKind.Copy;
                case "Delete": return AbilityKind.Delete;
                case "Connect": return AbilityKind.Connect;
                case "Restore": return AbilityKind.Restore;
                case "Undo": return AbilityKind.Undo;
                case "Interact": return AbilityKind.Interact;
                case "Search": return AbilityKind.Search;
                case "SelectAll": return AbilityKind.SelectAll;
                default: return AbilityKind.Move;
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
