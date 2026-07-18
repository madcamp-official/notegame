using System;
using System.Collections;
using System.Collections.Generic;
using KeyboardWanderer.Core;
using KeyboardWanderer.Gameplay;
using KeyboardWanderer.Networking;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.Tilemaps;

namespace KeyboardWanderer.Demo
{
    public sealed class KeyboardWandererDemoController : MonoBehaviour
    {
        private const float LogicalWidth = 1440f;
        private const float LogicalHeight = 900f;
        private const float TopHeight = 60f;
        private const float BottomHeight = 220f;
        private const float LeftWidth = 260f;
        private const float RightWidth = 390f;
        private const float TileSize = 1f;
        private const int TerrainSortingOrder = -1000;
        private const string MusicVolumeKey = "keyboard-wanderer.music-volume";
        private const string SfxVolumeKey = "keyboard-wanderer.sfx-volume";
        private const string GmEnabledKey = "keyboard-wanderer.gm-enabled";
        private const string ServerRunIdKey = "keyboard-wanderer.server-run-id";

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
            public Sprite[] AttackFrames;
            public Vector3 TargetPosition;
            public readonly Queue<Vector3> MovementPath = new Queue<Vector3>();
            public Color BaseColor;
            public float DesiredSize;
            public bool IsPlayer;
            public bool IsHostile;
        }

        private static readonly Color Ink = Hex("160f0a");
        private static readonly Color Panel = Hex("281a11");
        private static readonly Color PanelRaised = Hex("352419");
        private static readonly Color Gold = Hex("d3a64b");
        private static readonly Color GoldDim = Hex("7c5d2b");
        private static readonly Color Parchment = Hex("f0dfb6");
        private static readonly Color Muted = Hex("ad9878");
        private static readonly Color Leaf = Hex("65a850");
        private static readonly Color Ruby = Hex("c84c43");
        private static readonly Color FocusBlue = Hex("57a9bd");

        private readonly Dictionary<Guid, EntityVisual> _entityVisuals = new Dictionary<Guid, EntityVisual>();
        private readonly List<Sprite> _runtimeSprites = new List<Sprite>();
        private readonly List<Texture2D> _runtimeTextures = new List<Texture2D>();
        private readonly List<Tile> _runtimeTiles = new List<Tile>();
        private readonly List<string> _sessionLog = new List<string>();

        [Header("Authored content")]
        [SerializeField] private KeyboardWandererAuthoringSettings authoringSettings;
        [SerializeField] private NinjaAdventureAssetManifest authoredAssetManifest;
        [SerializeField] private KeyboardWandererWorldView authoredWorld;
        [SerializeField] private Camera authoredCamera;
        [SerializeField] private AudioSource authoredMusicSource;
        [SerializeField] private AudioSource authoredSfxSource;

        private LocalTurnService _service;
        private NinjaAdventureAssetManifest _assets;
        private GmNarrativeClient _narrativeClient;
        private GameApiClient _gameApi;
        private GameApiClient.CampaignSnapshot _serverCampaign;
        private GameApiClient.RunSnapshot _serverRun;
        private ScreenMode _screenMode = ScreenMode.Title;
        private ScreenMode _settingsReturn = ScreenMode.Title;
        private AbilityKind _ability = AbilityKind.Move;
        private GridCoord? _selectedCoord;
        private Guid? _selectedTarget;
        private Guid? _selectedSecondaryTarget;
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
        private string _authoredDialogueSignature = string.Empty;
        private int _authoredDialoguePage;
        private bool _authoredDialogueDismissed;
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
        private Vector2 _logScroll;
        private Vector2 _narrativeScroll;
        private int _poiCursor = -1;
        private GridCoord? _cameraInspectCoord;
        private float _cameraInspectUntil;
        private string _poiLabel = "POI 탐색";
        private string _movementSelectionFeedback = string.Empty;

        private Camera _camera;
        private KeyboardWandererSceneUI _sceneUi;
        private Vector3 _cameraVelocity;
        private GameObject _worldRoot;
        private SpriteRenderer _selectionRenderer;
        private AudioSource _musicSource;
        private AudioSource _sfxSource;
        private AudioClip _currentMusic;
        private float _musicVolume = 0.65f;
        private float _sfxVolume = 0.8f;
        private float _nextAnimationAt;
        private int _animationFrame;
        private float _playerActionUntil;
        private int _lastScreenWidth;
        private int _lastScreenHeight;
        private Font _koreanFont;

        private float PlayerWalkSpeed => authoringSettings != null ? authoringSettings.PlayerWalkSpeed : 4.2f;
        private Transform WorldContentRoot => authoredWorld != null && _worldRoot == authoredWorld.gameObject
            ? authoredWorld.DynamicObjects
            : _worldRoot != null ? _worldRoot.transform : transform;

        private Sprite _grassSprite;
        private Sprite _dirtSprite;
        private Sprite _darkGrassSprite;
        private Sprite _wallSprite;
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
        private Sprite[] _playerAttackFrames = Array.Empty<Sprite>();
        private Sprite[] _slimeFrames = Array.Empty<Sprite>();
        private Sprite[] _villagerFrames = Array.Empty<Sprite>();

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
            _sceneUi = GetComponentInChildren<KeyboardWandererSceneUI>(true);
            LoadSettings();
            CreateUiTextures();
            LoadNinjaAdventureAssets();
            ConfigureCamera();
            ConfigureAudio();
            EnsureRuntimeClients();
            ShowTitle();
            if (_sceneUi != null)
            {
                _sceneUi.Bind(this);
                _sceneUi.ApplyFont(_koreanFont != null ? _koreanFont : (_assets != null ? _assets.PixelFont : null));
                UpdateAuthoredUi();
            }
        }

        public void ConfigureAuthoredContent(
            KeyboardWandererAuthoringSettings settings,
            NinjaAdventureAssetManifest manifest,
            KeyboardWandererWorldView world,
            Camera sceneCamera,
            AudioSource music,
            AudioSource sfx)
        {
            authoringSettings = settings;
            authoredAssetManifest = manifest;
            authoredWorld = world;
            authoredCamera = sceneCamera;
            authoredMusicSource = music;
            authoredSfxSource = sfx;
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
            StopAllCoroutines();
            if (_worldRoot != null && (authoredWorld == null || _worldRoot != authoredWorld.gameObject))
                Destroy(_worldRoot);
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
            UpdateAuthoredUi();
            if (_screenMode != ScreenMode.Playing || _service == null)
                return;

            UpdateAnimatedVisuals();
            UpdateCameraFollow();
            HandleKeyboard();
            HandleMapClick();
        }

        private void OnGUI()
        {
            if (_sceneUi != null && _sceneUi.IsReady)
                return;

            Matrix4x4 previousMatrix = GUI.matrix;
            Color previousColor = GUI.color;
            bool previousEnabled = GUI.enabled;
            GUI.matrix = Matrix4x4.Scale(new Vector3(Screen.width / LogicalWidth, Screen.height / LogicalHeight, 1f));
            if (_koreanFont != null)
                GUI.skin.font = _koreanFont;
            else if (_assets != null && _assets.PixelFont != null)
                GUI.skin.font = _assets.PixelFont;
            EnsureStyles();

            switch (_screenMode)
            {
                case ScreenMode.Title:
                    DrawTitleScreen();
                    break;
                case ScreenMode.Settings:
                    DrawSettingsScreen();
                    break;
                default:
                    DrawGameHud();
                    break;
            }

            GUI.enabled = previousEnabled;
            GUI.color = previousColor;
            GUI.matrix = previousMatrix;
        }

        private void UpdateAuthoredUi()
        {
            if (_sceneUi == null || !_sceneUi.IsReady)
                return;

            bool ended = _screenMode == ScreenMode.Playing && _service != null && !RunIsPlaying(_service.CurrentView);
            _sceneUi.Show(_screenMode.ToString(), _showPause, ended);
            _sceneUi.SetSlider("Music Slider", _musicVolume);
            _sceneUi.SetSlider("Sfx Slider", _sfxVolume);
            _sceneUi.SetToggle("GM Toggle", _gmEnabled);

            int nextCounter = PlayerPrefs.GetInt("keyboard-wanderer.run-counter", 0) + 1;
            long nextSeed = 20260717L + nextCounter;
            CampaignBlueprint preview = CampaignCatalog.Create(nextSeed);
            _sceneUi.SetText("Title Heading", CampaignCatalog.CampaignTitle);
            _sceneUi.SetText("Title Subtitle", "코드리아 × 관리자 키보드 × 선택 회수");
            _sceneUi.SetText("Title Seed", "NEXT SEED  " + nextSeed);
            _sceneUi.SetText("Title Premise", preview.Title + "\n\n" + preview.Premise);
            _sceneUi.SetText("Title Status", _serverStatus + " · Ninja Adventure CC0");
            _sceneUi.SetButtonState("New Run Button", !_serverPending);
            _sceneUi.SetButtonState("Continue Button", !_serverPending &&
                (LocalRunSaveService.HasSave || !string.IsNullOrWhiteSpace(PlayerPrefs.GetString(ServerRunIdKey, string.Empty))));

            if (_service == null)
                return;

            RunView view = _service.CurrentView;
            string narrative = _lastOutcome == "READY" || _lastOutcome == "RESTORED"
                ? CampaignPremise(view)
                : ShortNarrative(_lastNarrative);
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
            _sceneUi.SetObjectActive("Story Panel", !_authoredDialogueDismissed);
            _authoredDialoguePage = Mathf.Clamp(_authoredDialoguePage, 0, Mathf.Max(0, dialoguePages.Length - 1));
            bool showingNarration = _authoredDialoguePage == 0;
            _sceneUi.SetText("Scene Location", CurrentAreaName(view) + " · " + CurrentBiomeLabel(view));
            _sceneUi.SetText("Scene Title", StoryBeat(view));
            _sceneUi.SetText("Dialogue Speaker", showingNarration ? "이야기" : "코드리아 주민");
            _sceneUi.SetText("Story Text", dialoguePages[_authoredDialoguePage]);
            bool hasNextDialogue = _authoredDialoguePage < dialoguePages.Length - 1;
            _sceneUi.SetText("Next Dialogue Label", hasNextDialogue ? "다음 ▶" : "대화 끝");
            _sceneUi.SetButtonState("Next Dialogue Button", true);
            _sceneUi.SetText("Action Hint", _playerWalking ? "선택한 경로를 따라 이동하고 있습니다." : NarrativeSelectionHint());

            SetAbilityButton("Move Button", AbilityKind.Move);
            SetAbilityButton("Copy Skill Button", AbilityKind.Copy);
            SetAbilityButton("Delete Skill Button", AbilityKind.Delete);
            SetAbilityButton("Connect Skill Button", AbilityKind.Connect);
            SetAbilityButton("Restore Skill Button", AbilityKind.Restore);
            SetAbilityButton("Undo Skill Button", AbilityKind.Undo);
            _sceneUi.SetText("Confirm Action Label", "실행");
            _sceneUi.SetButtonState("Confirm Action Button",
                RunIsPlaying(view) && !_showPause && !_serverPending && !_playerWalking && CanSubmitCurrentSelection());
            _sceneUi.SetText("Ending Heading", "코드리아의 결말");
            _sceneUi.SetText("Ending Text", EndingTitle(EndingCode(view)) + "\n\n" + EndingDescription(EndingCode(view)) +
                "\n\n당신의 선택이 코드리아에 남긴 결말입니다.");
        }

        public void UiStartNewRun() => StartNewRun();
        public void UiContinueRun() => ContinueRun();
        public void UiCyclePoi(int direction) => CyclePoi(direction);
        public void UiSetAbility(string abilityName) => SetAbility(AbilityByName(abilityName));
        public void UiSubmit() => Submit();
        public void UiAdvanceDialogue()
        {
            string[] pages = BuildDialoguePages(_service == null ? _lastNarrative :
                (_lastOutcome == "READY" || _lastOutcome == "RESTORED" ? CampaignPremise(_service.CurrentView) : ShortNarrative(_lastNarrative)));
            if (_authoredDialoguePage < pages.Length - 1)
            {
                _authoredDialoguePage++;
            }
            else
            {
                _authoredDialogueDismissed = true;
                _sceneUi?.SetObjectActive("Story Panel", false);
            }
            PlaySfx(AssetClip("UiAcceptSound"));
        }
        public void UiResume() => _showPause = false;
        public void UiShowTitle() => ShowTitle();

        private void SetAbilityButton(string buttonName, AbilityKind ability)
        {
            _sceneUi.SetButtonState(buttonName, !_playerWalking && IsSkillEnabledForCurrentTarget(ability), _ability == ability);
        }

        private string[] BuildDialoguePages(string narrative)
        {
            var pages = new List<string>();
            if (!string.IsNullOrWhiteSpace(narrative)) pages.Add(narrative.Trim());
            for (int i = 0; i < _lastDialogue.Length; i++)
                if (!string.IsNullOrWhiteSpace(_lastDialogue[i])) pages.Add(_lastDialogue[i].Trim());
            if (pages.Count == 0) pages.Add("코드리아의 다음 이야기가 시작되기를 기다리고 있습니다.");
            return pages.ToArray();
        }

        private string NarrativeSelectionHint()
        {
            switch (_ability)
            {
                case AbilityKind.Move: return string.IsNullOrWhiteSpace(_movementSelectionFeedback)
                    ? "이동할 타일을 고른 뒤 실행을 누르세요. 캐릭터는 길을 따라 걷습니다."
                    : _movementSelectionFeedback;
                case AbilityKind.Connect: return "이어 주고 싶은 두 대상을 지도에서 차례로 고르세요.";
                case AbilityKind.Undo: return "직전 선택을 되돌립니다.";
                default: return "이 선택을 적용할 대상을 지도에서 고르세요.";
            }
        }

        public void UiOpenSettings()
        {
            _settingsReturn = _screenMode;
            _screenMode = ScreenMode.Settings;
        }

        public void UiOpenSettingsFromPause()
        {
            _showPause = false;
            _settingsReturn = ScreenMode.Playing;
            _screenMode = ScreenMode.Settings;
        }

        public void UiCloseSettings()
        {
            _screenMode = _settingsReturn;
            if (_screenMode == ScreenMode.Playing && _camera != null)
                _camera.enabled = true;
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

        private void DrawTitleScreen()
        {
            DrawRect(new Rect(0f, 0f, LogicalWidth, LogicalHeight), new Color(0.035f, 0.027f, 0.02f, 1f));
            for (int i = 0; i < 8; i++)
            {
                float inset = 24f + i * 9f;
                DrawBorder(new Rect(inset, inset, LogicalWidth - inset * 2f, LogicalHeight - inset * 2f),
                    i % 2 == 0 ? new Color(GoldDim.r, GoldDim.g, GoldDim.b, 0.24f) : new Color(1f, 1f, 1f, 0.025f), 1f);
            }

            int nextCounter = PlayerPrefs.GetInt("keyboard-wanderer.run-counter", 0) + 1;
            long nextSeed = 20260717L + nextCounter;
            CampaignBlueprint preview = CampaignCatalog.Create(nextSeed);
            string previewTitle = preview.Title;
            string previewPremise = preview.Premise;

            var panel = new Rect(385f, 54f, 670f, 790f);
            DrawWoodPanel(panel, true);
            GUI.Label(new Rect(420f, 82f, 600f, 64f), CampaignCatalog.CampaignTitle, _titleStyle);
            GUI.Label(new Rect(420f, 140f, 600f, 30f), "코드리아 × 관리자 키보드 × 선택 회수", _subtitleStyle);
            DrawOrnament(new Rect(465f, 184f, 510f, 2f));

            DrawSprite(_playerSprite, new Rect(604f, 198f, 196f, 196f), Color.white);
            DrawSprite(_d20Sprite, new Rect(758f, 326f, 58f, 58f), new Color(1f, 0.86f, 0.45f, 0.95f));
            GUI.Label(new Rect(438f, 395f, 564f, 24f), "NEXT SEED  " + nextSeed, _mutedStyle);
            GUI.Label(new Rect(430f, 425f, 580f, 36f), previewTitle, _headerStyle);
            GUI.Label(new Rect(430f, 463f, 580f, 64f), previewPremise, _previewPremiseStyle);
            GUI.Label(new Rect(450f, 529f, 540f, 42f),
                "160×160 맵은 런 시작 시 한 번만 확정됩니다.\n안전 탐색은 턴을 쓰지 않고, 사건 행동만 D20과 의미 있는 턴을 씁니다.", _mutedStyle);

            GUI.enabled = !_serverPending;
            if (GUI.Button(new Rect(528f, 591f, 384f, 56f), _serverPending ? "월드 생성 중…" : "새 Seed 런", _selectedButtonStyle))
                StartNewRun();
            GUI.enabled = true;

            GUI.enabled = !_serverPending && (LocalRunSaveService.HasSave || !string.IsNullOrWhiteSpace(PlayerPrefs.GetString(ServerRunIdKey, string.Empty)));
            if (GUI.Button(new Rect(528f, 660f, 384f, 50f), "권위 상태에서 이어하기", _buttonStyle))
                ContinueRun();
            GUI.enabled = true;

            if (GUI.Button(new Rect(528f, 723f, 384f, 44f), "설정", _buttonStyle))
            {
                _settingsReturn = ScreenMode.Title;
                _screenMode = ScreenMode.Settings;
            }

            GUI.Label(new Rect(430f, 780f, 580f, 22f), _serverStatus + " · Ninja Adventure CC0", _mutedStyle);
            GUI.Label(new Rect(430f, 810f, 580f, 20f), "1 Move · 2 Copy · 3 Delete · 4 Connect · 5 Restore · 6 Undo", _smallStyle);
        }

        private void DrawSettingsScreen()
        {
            DrawRect(new Rect(0f, 0f, LogicalWidth, LogicalHeight), new Color(0.035f, 0.027f, 0.02f, 1f));
            var panel = new Rect(440f, 150f, 560f, 600f);
            DrawWoodPanel(panel, true);
            GUI.Label(new Rect(480f, 188f, 480f, 50f), "설정", _titleStyle);
            DrawOrnament(new Rect(510f, 248f, 420f, 2f));

            GUI.Label(new Rect(510f, 292f, 250f, 30f), "음악 볼륨", _headerStyle);
            float music = GUI.HorizontalSlider(new Rect(510f, 330f, 420f, 22f), _musicVolume, 0f, 1f);
            GUI.Label(new Rect(860f, 288f, 70f, 30f), Mathf.RoundToInt(music * 100f) + "%", _smallStyle);

            GUI.Label(new Rect(510f, 382f, 250f, 30f), "효과음 볼륨", _headerStyle);
            float sfx = GUI.HorizontalSlider(new Rect(510f, 420f, 420f, 22f), _sfxVolume, 0f, 1f);
            GUI.Label(new Rect(860f, 378f, 70f, 30f), Mathf.RoundToInt(sfx * 100f) + "%", _smallStyle);

            bool gmEnabled = GUI.Toggle(new Rect(510f, 478f, 420f, 36f), _gmEnabled,
                "  생성형 장면·대사 패널 표시", _buttonStyle);
            GUI.Label(new Rect(520f, 520f, 400f, 58f),
                "Gemini는 서술·대사·기억만 제안합니다.\nD20과 상태 변경은 항상 서버 규칙 엔진이 확정합니다.", _mutedStyle);

            if (!Mathf.Approximately(music, _musicVolume) || !Mathf.Approximately(sfx, _sfxVolume) || gmEnabled != _gmEnabled)
            {
                _musicVolume = music;
                _sfxVolume = sfx;
                _gmEnabled = gmEnabled;
                ApplyAudioVolumes();
                SaveSettings();
            }

            if (GUI.Button(new Rect(510f, 625f, 200f, 50f), "돌아가기", _selectedButtonStyle))
            {
                _screenMode = _settingsReturn;
                if (_screenMode == ScreenMode.Playing && _camera != null)
                    _camera.enabled = true;
            }

            if ((LocalRunSaveService.HasSave || !string.IsNullOrWhiteSpace(PlayerPrefs.GetString(ServerRunIdKey, string.Empty))) &&
                GUI.Button(new Rect(730f, 625f, 200f, 50f), "이어하기 기록 삭제", _dangerButtonStyle))
            {
                LocalRunSaveService.Delete();
                PlayerPrefs.DeleteKey(ServerRunIdKey);
                PlayerPrefs.Save();
            }
        }

        private void DrawGameHud()
        {
            if (_service == null)
                return;

            RunView view = _service.CurrentView;
            DrawTopBar(view);
            DrawLeftPanel(view);
            DrawRightPanel(view);
            DrawBottomBar(view);

            if (_showPause)
                DrawPauseOverlay();
            else if (!RunIsPlaying(view))
                DrawEndSummary(view);
        }

        private void DrawTopBar(RunView view)
        {
            var rect = new Rect(0f, 0f, LogicalWidth, TopHeight);
            DrawWoodPanel(rect, false);
            GUI.Label(new Rect(18f, 8f, 382f, 25f), CampaignTitle(view), _headerStyle);
            GUI.Label(new Rect(18f, 34f, 382f, 18f), _serverOnline ? "SERVER 160×160 AUTHORITATIVE" : "OFFLINE 160×160 RULE FALLBACK", _mutedStyle);
            string area = CurrentAreaName(view);
            GUI.Label(new Rect(400f, 7f, 570f, 25f), "◆  " + area + "  ◆", _centerStyle);
            GUI.Label(new Rect(400f, 33f, 570f, 20f), CurrentBiomeLabel(view) + " · " + CampaignPhaseLabel(view), _mutedStyle);
            GUI.Label(new Rect(978f, 7f, 444f, 22f),
                "캠페인 턴 " + CurrentTurn(view) + "/" + TurnLimit(view) + "  ·  관리자 권한 " + AdminAccessLevel(view) + "/3", _smallStyle);
            GUI.Label(new Rect(978f, 32f, 444f, 22f), MetricsLine(view), _mutedStyle);
        }

        private void DrawLeftPanel(RunView view)
        {
            var rect = new Rect(0f, TopHeight, LeftWidth, LogicalHeight - TopHeight - BottomHeight);
            DrawWoodPanel(rect, false);
            GUI.Label(new Rect(18f, 74f, 224f, 28f), "런 컨텍스트", _headerStyle);
            DrawBorder(new Rect(18f, 108f, 96f, 96f), GoldDim, 2f);
            DrawRect(new Rect(22f, 112f, 88f, 88f), new Color(0.08f, 0.12f, 0.08f, 0.9f));
            DrawSprite(_playerSprite, new Rect(24f, 114f, 84f, 84f), Color.white);
            GUI.Label(new Rect(122f, 113f, 122f, 28f), PlayerDisplayName(view), _smallStyle);
            GUI.Label(new Rect(122f, 143f, 122f, 22f), CampaignPhaseLabel(view), _mutedStyle);
            GUI.Label(new Rect(122f, 170f, 122f, 22f), "Seed " + view.WorldSeed, _mutedStyle);

            int health = Health(view);
            int maxHealth = MaxHealth(view);
            int focus = Focus(view);
            int maxFocus = MaxFocus(view);
            DrawMeter(new Rect(18f, 217f, 224f, 23f), health, maxHealth, Ruby, "HP");
            DrawMeter(new Rect(18f, 247f, 224f, 23f), focus, maxFocus, FocusBlue, "FOCUS");
            GUI.Label(new Rect(18f, 272f, 224f, 18f), "권한 " + AdminAccessLevel(view) + "/3 · 안전 이동 " + TravelCount(view), _mutedStyle);

            GUI.Label(new Rect(18f, 294f, 224f, 24f), "메인 목표 · 1", _headerStyle);
            DrawRect(new Rect(16f, 323f, 228f, 86f), new Color(0.04f, 0.03f, 0.02f, 0.74f));
            DrawBorder(new Rect(16f, 323f, 228f, 86f), GoldDim, 1f);
            GUI.Label(new Rect(26f, 331f, 208f, 70f), StoryBeat(view) + "\n" + StoryBeatObjective(view), _smallStyle);

            GUI.Label(new Rect(18f, 421f, 224f, 24f), "보조 목표 · 최대 2", _headerStyle);
            GUI.Label(new Rect(20f, 450f, 220f, 64f), SecondaryObjectiveText(view), _mutedStyle);
            GUI.Label(new Rect(18f, 520f, 224f, 24f), "권한 / 부채 / 선택 회수", _headerStyle);
            GUI.Label(new Rect(20f, 549f, 220f, 68f), ProgressLedgerText(view), _smallStyle);
            GUI.Label(new Rect(18f, 618f, 224f, 32f), FinaleGateLabel(view), _mutedStyle);
            if (GUI.Button(new Rect(18f, 653f, 104f, 24f), "◀ POI", _buttonStyle)) CyclePoi(-1);
            if (GUI.Button(new Rect(138f, 653f, 104f, 24f), "POI ▶", _buttonStyle)) CyclePoi(1);
        }

        private void DrawRightPanel(RunView view)
        {
            float x = LogicalWidth - RightWidth;
            var rect = new Rect(x, TopHeight, RightWidth, LogicalHeight - TopHeight - BottomHeight);
            DrawWoodPanel(rect, false);

            GUI.Label(new Rect(x + 18f, 74f, RightWidth - 36f, 28f), "1 · 판정", _headerStyle);
            var diceRect = new Rect(x + 16f, 104f, RightWidth - 32f, 116f);
            DrawRect(diceRect, new Color(0.11f, 0.07f, 0.035f, 0.94f));
            DrawBorder(diceRect, Gold, 2f);
            DrawSprite(_d20Sprite, new Rect(diceRect.x + 12f, diceRect.y + 16f, 76f, 76f), Color.white);
            GUI.Label(new Rect(diceRect.x + 96f, diceRect.y + 10f, 132f, 32f), _lastD20 > 0 ? "D20  " + _lastD20 : "D20  --", _numberStyle);
            GUI.Label(new Rect(diceRect.x + 232f, diceRect.y + 13f, 120f, 25f), _lastOutcome, _smallStyle);
            GUI.Label(new Rect(diceRect.x + 96f, diceRect.y + 46f, 258f, 22f),
                "수정 " + Signed(_lastModifier) + "  ·  난이도 " + DisplayNumber(_lastDifficulty), _smallStyle);
            GUI.Label(new Rect(diceRect.x + 96f, diceRect.y + 70f, 258f, 22f),
                "총점 " + DisplayNumber(_lastMechanicalScore) + "  ·  문맥 " + _lastActionContext, _smallStyle);
            GUI.Label(new Rect(diceRect.x + 10f, diceRect.y + 94f, diceRect.width - 20f, 17f),
                _serverPending || _gmPending ? "권위 판정과 짧은 서사를 검증하는 중…" : _serverStatus, _mutedStyle);

            GUI.Label(new Rect(x + 18f, 230f, RightWidth - 36f, 26f), "2 · 상태 변화", _headerStyle);
            var attemptRect = new Rect(x + 16f, 258f, RightWidth - 32f, 112f);
            DrawRect(attemptRect, new Color(0.04f, 0.03f, 0.02f, 0.76f));
            DrawBorder(attemptRect, GoldDim, 1f);
            GUI.Label(new Rect(attemptRect.x + 10f, attemptRect.y + 7f, attemptRect.width - 20f, 42f), _lastStateChanges, _smallStyle);
            GUI.Label(new Rect(attemptRect.x + 10f, attemptRect.y + 50f, attemptRect.width - 20f, 55f), _lastExplanation, _mutedStyle);

            GUI.Label(new Rect(x + 18f, 380f, RightWidth - 36f, 26f), "3 · 짧은 서사 · 2–4문장", _headerStyle);
            var narrationRect = new Rect(x + 16f, 408f, RightWidth - 32f, 116f);
            DrawRect(narrationRect, new Color(0.04f, 0.03f, 0.02f, 0.76f));
            DrawBorder(narrationRect, GoldDim, 1f);
            DrawScrollableText(ShortNarrative(NarrativeWithDialogue()), new Rect(narrationRect.x + 8f, narrationRect.y + 8f,
                narrationRect.width - 16f, narrationRect.height - 16f), ref _narrativeScroll);

            GUI.Label(new Rect(x + 18f, 534f, RightWidth - 36f, 26f), "기억 · 권한 / 부채 / 선택 회수", _headerStyle);
            var memoryRect = new Rect(x + 16f, 562f, RightWidth - 32f, 101f);
            DrawRect(memoryRect, new Color(0.04f, 0.03f, 0.02f, 0.76f));
            DrawBorder(memoryRect, GoldDim, 1f);
            DrawMemory(view, new Rect(memoryRect.x + 8f, memoryRect.y + 7f, memoryRect.width - 16f, memoryRect.height - 14f));
        }

        private void DrawBottomBar(RunView view)
        {
            var rect = new Rect(0f, LogicalHeight - BottomHeight, LogicalWidth, BottomHeight);
            DrawWoodPanel(rect, false);
            GUI.Label(new Rect(18f, 690f, 570f, 25f), "MOVE + 관리자 키보드 5 스킬", _headerStyle);

            string[] names = { "Move", "Copy", "Delete", "Connect", "Restore", "Undo" };
            string[] korean = { "이동", "복제", "삭제", "연결", "복원", "되돌림" };
            string[] keys = { "1", "2", "3", "4", "5", "6" };
            for (int i = 0; i < names.Length; i++)
            {
                var buttonRect = new Rect(18f + i * 96f, 720f, 88f, 90f);
                DrawAbilityButton(buttonRect, names[i], korean[i], keys[i]);
            }

            GUI.Label(new Rect(18f, 823f, 570f, 20f), "추천 행동 · 2–3", _mutedStyle);
            AbilityKind[] recommendations = RecommendedActions(view);
            for (int i = 0; i < recommendations.Length && i < 3; i++)
            {
                AbilityKind recommended = recommendations[i];
                if (GUI.Button(new Rect(18f + i * 182f, 846f, 172f, 34f),
                        ContextPreview(recommended, view) + " · " + recommended,
                        _ability == recommended ? _selectedButtonStyle : _buttonStyle))
                    SetAbility(recommended);
            }

            GUI.Label(new Rect(620f, 690f, 590f, 25f), "실행 전 확인", _headerStyle);
            var previewRect = new Rect(620f, 720f, 590f, 145f);
            DrawRect(previewRect, new Color(0.04f, 0.03f, 0.02f, 0.76f));
            DrawBorder(previewRect, GoldDim, 1f);
            GUI.Label(new Rect(636f, 734f, 558f, 120f), ExecutionPreview(view), _smallStyle);

            GUI.enabled = RunIsPlaying(view) && !_showPause && !_serverPending && CanSubmitCurrentSelection();
            if (GUI.Button(new Rect(1226f, 720f, 196f, 145f), SubmissionButtonLabel(view), _selectedButtonStyle))
                Submit();
            GUI.enabled = true;
            GUI.Label(new Rect(620f, 869f, 802f, 18f), SelectionHint(view), _mutedStyle);
        }

        private bool CanSubmitCurrentSelection()
        {
            if (_ability == AbilityKind.Move) return _selectedCoord.HasValue;
            if (_ability == AbilityKind.Undo) return true;
            if (_ability == AbilityKind.Connect)
                return _selectedTarget.HasValue && _selectedSecondaryTarget.HasValue;
            return _selectedTarget.HasValue && IsSkillEnabledForCurrentTarget(_ability);
        }

        private void DrawPauseOverlay()
        {
            DrawRect(new Rect(0f, 0f, LogicalWidth, LogicalHeight), new Color(0f, 0f, 0f, 0.68f));
            var panel = new Rect(510f, 250f, 420f, 400f);
            DrawWoodPanel(panel, true);
            GUI.Label(new Rect(550f, 290f, 340f, 50f), "일시 정지", _titleStyle);
            if (GUI.Button(new Rect(580f, 380f, 280f, 52f), "계속하기", _selectedButtonStyle))
                _showPause = false;
            if (GUI.Button(new Rect(580f, 450f, 280f, 48f), "설정", _buttonStyle))
            {
                _showPause = false;
                _settingsReturn = ScreenMode.Playing;
                _screenMode = ScreenMode.Settings;
            }
            if (GUI.Button(new Rect(580f, 516f, 280f, 48f), "타이틀로", _buttonStyle))
                ShowTitle();
            GUI.Label(new Rect(550f, 592f, 340f, 24f), "진행 상황은 매 턴 자동 저장됩니다.", _mutedStyle);
        }

        private void DrawEndSummary(RunView view)
        {
            DrawRect(new Rect(0f, 0f, LogicalWidth, LogicalHeight), new Color(0f, 0f, 0f, 0.72f));
            var panel = new Rect(435f, 145f, 570f, 610f);
            DrawWoodPanel(panel, true);
            GUI.Label(new Rect(475f, 185f, 490f, 52f), "코드리아의 결말", _titleStyle);
            DrawSprite(_d20Sprite, new Rect(640f, 255f, 160f, 160f), Color.white);
            string endingCode = EndingCode(view);
            GUI.Label(new Rect(485f, 425f, 470f, 42f), EndingTitle(endingCode), _headerStyle);
            GUI.Label(new Rect(505f, 475f, 430f, 62f), EndingDescription(endingCode), _centerStyle);
            GUI.Label(new Rect(505f, 555f, 430f, 28f),
                "턴 " + CurrentTurn(view) + " · 확정 사실 " + CanonicalFactCount(view) + " · 열린 훅 " + OpenLoopCount(view), _smallStyle);
            if (GUI.Button(new Rect(505f, 625f, 200f, 54f), "새 여정", _selectedButtonStyle))
                StartNewRun();
            if (GUI.Button(new Rect(735f, 625f, 200f, 54f), "타이틀", _buttonStyle))
                ShowTitle();
        }

        private void DrawLog(RunView view, Rect viewport)
        {
            var lines = new List<string>(view.GmLog);
            for (int i = 0; i < _sessionLog.Count; i++)
                if (!lines.Contains(_sessionLog[i]))
                    lines.Add(_sessionLog[i]);
            if (lines.Count == 0)
                lines.Add(_lastNarrative);

            float width = viewport.width - 18f;
            float height = 8f;
            for (int i = 0; i < lines.Count; i++)
                height += _smallStyle.CalcHeight(new GUIContent("• " + lines[i]), width) + 10f;
            var content = new Rect(0f, 0f, width, Mathf.Max(height, viewport.height));
            _logScroll = GUI.BeginScrollView(viewport, _logScroll, content, false, true);
            float y = 4f;
            for (int i = 0; i < lines.Count; i++)
            {
                string line = "• " + lines[i];
                float lineHeight = _smallStyle.CalcHeight(new GUIContent(line), width);
                GUI.Label(new Rect(2f, y, width, lineHeight), line, _smallStyle);
                y += lineHeight + 10f;
            }
            GUI.EndScrollView();
        }

        private void DrawScrollableText(string text, Rect viewport, ref Vector2 scroll)
        {
            string safe = string.IsNullOrWhiteSpace(text) ? "아직 확정된 장면 서술이 없습니다." : text.Trim();
            float width = viewport.width - 18f;
            float height = Mathf.Max(viewport.height, _smallStyle.CalcHeight(new GUIContent(safe), width) + 8f);
            scroll = GUI.BeginScrollView(viewport, scroll, new Rect(0f, 0f, width, height), false, true);
            GUI.Label(new Rect(2f, 2f, width, height - 4f), safe, _smallStyle);
            GUI.EndScrollView();
        }

        private void DrawMemory(RunView view, Rect viewport)
        {
            List<string> lines = MemoryLines(view);
            if (lines.Count == 0)
                lines.Add("아직 확정된 세계 기억이 없습니다.");
            float width = viewport.width - 18f;
            float height = 5f;
            for (int i = 0; i < lines.Count; i++)
                height += _smallStyle.CalcHeight(new GUIContent(lines[i]), width) + 7f;
            _logScroll = GUI.BeginScrollView(viewport, _logScroll,
                new Rect(0f, 0f, width, Mathf.Max(height, viewport.height)), false, true);
            float y = 3f;
            for (int i = 0; i < lines.Count; i++)
            {
                float lineHeight = _smallStyle.CalcHeight(new GUIContent(lines[i]), width);
                GUI.Label(new Rect(2f, y, width, lineHeight), lines[i], _smallStyle);
                y += lineHeight + 7f;
            }
            GUI.EndScrollView();
        }

        private void DrawAbilityButton(Rect rect, string abilityName, string label, string key)
        {
            AbilityKind value = AbilityByName(abilityName);
            bool selected = _ability.ToString() == abilityName;
            bool enabled = IsSkillEnabledForCurrentTarget(value);
            GUI.enabled = enabled;
            if (GUI.Button(rect, GUIContent.none, selected ? _selectedButtonStyle : _buttonStyle))
                SetAbility(value);
            GUI.enabled = true;
            DrawSprite(AbilityIcon(abilityName), new Rect(rect.x + 28f, rect.y + 8f, 34f, 34f), selected ? Color.white : new Color(0.9f, 0.84f, 0.67f, 1f));
            GUI.Label(new Rect(rect.x + 4f, rect.y + 47f, rect.width - 8f, 19f), label, _centerStyle);
            GUI.Label(new Rect(rect.x + 4f, rect.y + 67f, rect.width - 8f, 15f), enabled ? key : "사용 불가", _mutedStyle);
        }

        private bool IsSkillEnabledForCurrentTarget(AbilityKind skill)
        {
            if (_service == null) return false;
            if (skill == AbilityKind.Move) return true;
            RunView view = _service.CurrentView;
            int focus = _serverOnline && _serverRun != null ? _serverRun.focus : view.Focus;
            int focusCost = skill == AbilityKind.Copy || skill == AbilityKind.Delete ? 1
                : skill == AbilityKind.Connect || skill == AbilityKind.Restore ? 2
                : skill == AbilityKind.Undo ? 3 : int.MaxValue;
            if (focus < focusCost) return false;
            if (skill == AbilityKind.Undo)
            {
                if (_serverOnline) return _serverRun != null && _serverRun.currentTurn > 0;
                RunState state = _service.CreateSnapshot();
                return state.LastReversibleTurn != null && !state.LastReversibleTurn.IsConsumed &&
                       state.LastReversibleTurn.SourceTurn == state.CurrentTurn;
            }
            if (!_selectedTarget.HasValue)
                return true;
            for (int i = 0; i < view.Entities.Count; i++)
            {
                EntityView target = view.Entities[i];
                if (target.EntityId != _selectedTarget.Value) continue;
                if (skill == AbilityKind.Copy)
                    return target.Kind != EntityKind.Player && target.Kind != EntityKind.Npc;
                if (skill == AbilityKind.Delete)
                    return !target.IsProtected && target.Kind != EntityKind.Player && target.Kind != EntityKind.Npc;
                if (skill == AbilityKind.Connect) return !target.IsHostile;
                if (skill == AbilityKind.Restore)
                    return _lastRestorableTarget == target.EntityId ||
                           target.AssetId.StartsWith("story.admin-access", StringComparison.Ordinal);
            }
            return true;
        }

        private void DrawMeter(Rect rect, int value, int max, Color color, string label)
        {
            max = Mathf.Max(1, max);
            float normalized = Mathf.Clamp01(value / (float)max);
            DrawRect(rect, new Color(0.035f, 0.025f, 0.018f, 1f));
            DrawRect(new Rect(rect.x + 2f, rect.y + 2f, (rect.width - 4f) * normalized, rect.height - 4f), color);
            DrawBorder(rect, GoldDim, 1f);
            GUI.Label(rect, label + "  " + Mathf.Max(0, value) + " / " + max, _centerStyle);
        }

        private void DrawHearts(Rect rect, int health, int maxHealth)
        {
            int count = 5;
            float segment = maxHealth / (float)count;
            for (int i = 0; i < count; i++)
            {
                bool filled = health > segment * i;
                var heartRect = new Rect(rect.x + i * 36f, rect.y, 28f, 28f);
                DrawSprite(_assets != null ? _assets.HeartIcon : null, heartRect,
                    filled ? Color.white : new Color(0.25f, 0.22f, 0.2f, 0.85f));
            }
        }

        private void HandleKeyboard()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
                return;

            if (keyboard.escapeKey.wasPressedThisFrame)
            {
                _showPause = !_showPause;
                PlaySfx(_showPause ? AssetClip("UiCancelSound") : AssetClip("UiAcceptSound"));
                return;
            }
            if (_showPause || !RunIsPlaying(_service.CurrentView) || _serverPending || _playerWalking)
                return;

            if (keyboard.digit1Key.wasPressedThisFrame || keyboard.wKey.wasPressedThisFrame) SetAbility(AbilityKind.Move);
            if (keyboard.digit2Key.wasPressedThisFrame || keyboard.eKey.wasPressedThisFrame) SetAbility(AbilityKind.Copy);
            if (keyboard.digit3Key.wasPressedThisFrame || keyboard.rKey.wasPressedThisFrame) SetAbility(AbilityKind.Delete);
            if (keyboard.digit4Key.wasPressedThisFrame || keyboard.cKey.wasPressedThisFrame) SetAbility(AbilityKind.Connect);
            if (keyboard.digit5Key.wasPressedThisFrame || keyboard.qKey.wasPressedThisFrame) SetAbility(AbilityKind.Restore);
            if (keyboard.digit6Key.wasPressedThisFrame || keyboard.zKey.wasPressedThisFrame) SetAbility(AbilityKind.Undo);
            if (keyboard.leftBracketKey.wasPressedThisFrame) CyclePoi(-1);
            if (keyboard.rightBracketKey.wasPressedThisFrame) CyclePoi(1);
            if (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame) Submit();
        }

        private void HandleMapClick()
        {
            Mouse mouse = Mouse.current;
            if (_showPause || _playerWalking || mouse == null || !mouse.leftButton.wasPressedThisFrame || _camera == null)
                return;

            Vector2 mousePosition = mouse.position.ReadValue();
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

            Guid? clickedEntity = _serverOnline ? FindServerTarget(coord, _ability == AbilityKind.Restore) : FindTarget(view, coord);
            string abilityName = _ability.ToString();
            if (abilityName == "Move")
            {
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
                    _selectedTarget = clickedEntity;
                else
                    _selectedCoord = coord;
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
                }
                _selectedCoord = coord;
            }
            else if (abilityName != "Undo")
            {
                _selectedCoord = coord;
                _selectedTarget = clickedEntity;
                _selectedSecondaryTarget = null;
            }

            UpdateSelectionVisual(view);
            PlaySfx(AssetClip("UiMoveSound"));
        }

        private void CyclePoi(int direction)
        {
            if (_service == null) return;
            var coordinates = new List<GridCoord>();
            var labels = new List<string>();
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
            if (_service == null || _showPause || _serverPending || _playerWalking)
                return;
            RunView view = _service.CurrentView;
            if (!RunIsPlaying(view))
                return;

            if (_serverOnline && _serverRun != null && _gameApi != null)
            {
                if (_ability == AbilityKind.Move)
                    StartCoroutine(SubmitServerTravel());
                else
                    StartCoroutine(SubmitServerAction());
                return;
            }

            SubmitLocalTurn();
        }

        private void SubmitLocalTurn()
        {
            RunView view = _service.CurrentView;
            TryGetPlayerPosition(view, out GridCoord playerPositionBefore);

            string abilityName = _ability.ToString();
            GridCoord? destination = abilityName == "Move" || abilityName == "Copy" || abilityName == "Connect"
                ? _selectedCoord
                : null;
            Guid? target = abilityName == "Move" || abilityName == "Undo" ? null : _selectedTarget;
            Guid? secondary = abilityName == "Connect" ? _selectedSecondaryTarget : null;
            TurnRequest request = _ability == AbilityKind.Move && destination.HasValue
                ? TurnRequest.Move(Guid.NewGuid().ToString("N"), view.Version, destination.Value)
                : TurnRequest.UseSkill(Guid.NewGuid().ToString("N"), view.Version, _ability, target,
                    secondary, destination);
            TurnResponse response = _service.Submit(request);
            if (!response.IsSuccess)
            {
                _lastOutcome = response.ErrorCode.ToString();
                _lastAttempt = response.ErrorMessage ?? "행동이 거부되었습니다.";
                _lastExplanation = "서버 규칙과 같은 로컬 폴백 검증에서 거부되어 턴은 소비되지 않았습니다.";
                _lastNarrative = "대상, 목적지, 자원 조건을 다시 확인한 뒤 스킬을 다시 선택하세요.";
                AddLog("행동 거부 · " + _lastAttempt);
                PlaySfx(AssetClip("UiCancelSound"));
                return;
            }

            _lastD20 = response.D20;
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
        }

        private IEnumerator SubmitServerAction()
        {
            EnsureRuntimeClients();
            _serverPending = true;
            _gmPending = true;
            _serverStatus = "권위 턴 커밋 중";

            string skillId = ServerAbilityName(_ability).ToUpperInvariant();
            GridCoord? destinationCoord = _ability == AbilityKind.Copy
                ? _selectedCoord
                : null;
            var destination = destinationCoord.HasValue
                ? new GameApiClient.PositionSnapshot { x = destinationCoord.Value.X, y = destinationCoord.Value.Y }
                : null;
            var targetIds = new List<string>();
            if (_ability != AbilityKind.Undo && _selectedTarget.HasValue)
                targetIds.Add(_selectedTarget.Value.ToString());
            if (_ability == AbilityKind.Connect && _selectedSecondaryTarget.HasValue)
                targetIds.Add(_selectedSecondaryTarget.Value.ToString());
            string requestId = "unity-" + Guid.NewGuid().ToString("N");
            GameApiClient.RunSnapshot runBeforeSubmit = _serverRun;
            int turnBeforeSubmit = _serverRun.currentTurn;
            GameApiClient.Result<GameApiClient.CommittedTurn> result = null;

            yield return _gameApi.SubmitAction(_serverRun.id, requestId, _serverRun.version, skillId,
                targetIds.ToArray(), destination, value => result = value);

            _serverPending = false;
            _gmPending = false;
            if (result == null || !result.IsSuccess)
            {
                bool encounterRequired = string.Equals(result?.ErrorCode, "TRAVEL_ENCOUNTER_REQUIRED", StringComparison.Ordinal);
                if (encounterRequired)
                {
                    _encounterMoveRequired = true;
                    _encounterReason = FirstNonEmpty(result.ErrorReason, "unsafe_route");
                    GameApiClient.PositionSnapshot staging = result.StopPosition;
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
                _lastOutcome = result?.ErrorCode ?? "NETWORK_ERROR";
                _lastAttempt = result?.ErrorMessage ?? "권위 서버에서 응답을 받지 못했습니다.";
                _lastExplanation = result?.ErrorCode == "RUN_VERSION_CONFLICT"
                    ? "다른 상태가 먼저 커밋되어 최신 런을 다시 동기화합니다. 선택한 행동은 자동 재실행하지 않습니다."
                    : "서버가 거부한 요청은 턴을 소비하지 않습니다. 로컬에서 임의 판정을 대신하지 않았습니다.";
                _serverStatus = "턴 거부 · " + _lastOutcome;
                PlaySfx(AssetClip("UiCancelSound"));
                if (result == null || result.ErrorCode == "NETWORK_ERROR" || result.ErrorCode == "RUN_VERSION_CONFLICT")
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
                        }
                    }
                }
                yield break;
            }

            GameApiClient.CommittedTurn committed = result.Value;
            CaptureRestorableTarget(committed.Turn, runBeforeSubmit);
            _serverRun = committed.Run;
            SyncEncounterStateFromServer();
            SyncRestorableCandidateFromServer();
            ApplyServerTurnPresentation(committed.Turn);
            SyncServerEntityVisuals(_serverRun);
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
            var destination = new GameApiClient.PositionSnapshot { x = destinationCoord.X, y = destinationCoord.Y };
            string requestId = "unity-travel-" + Guid.NewGuid().ToString("N");
            int campaignTurnBefore = _serverRun.currentTurn;
            string layoutHashBefore = _serverRun.world?.layoutHash;
            GameApiClient.Result<GameApiClient.CommittedNavigation> result = null;

            yield return _gameApi.SubmitTravel(_serverRun.id, requestId, _serverRun.version, destination,
                value => result = value);

            _serverPending = false;
            if (result == null || !result.IsSuccess)
            {
                _lastOutcome = result?.ErrorCode ?? "NETWORK_ERROR";
                _lastAttempt = result?.ErrorMessage ?? "안전 탐색 응답을 받지 못했습니다.";
                _lastExplanation = "이동이 거부되어 위치·D20·의미 있는 캠페인 턴은 변하지 않았습니다.";
                _serverStatus = "탐색 이동 거부 · " + _lastOutcome;
                PlaySfx(AssetClip("UiCancelSound"));
                if (result == null || result.ErrorCode == "NETWORK_ERROR" || result.ErrorCode == "RUN_VERSION_CONFLICT")
                    yield return ResyncServerRun();
                yield break;
            }

            GameApiClient.CommittedNavigation committed = result.Value;
            GameApiClient.NavigationSnapshot navigation = committed.Navigation;
            _serverRun = committed.Run;
            bool encounterOpened = (navigation != null && navigation.encounterOpened) ||
                                   (_serverRun.activeEncounter != null &&
                                    !string.Equals(_serverRun.activeEncounter.status, "resolved", StringComparison.OrdinalIgnoreCase));
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
            _lastNarrative = encounterOpened
                ? actorName + "는(은) 안전 구간 끝에서 " + EncounterReasonLabel(_encounterReason) + " 사건과 마주쳤다. 이제 배치·전투·조사·협상 중 하나로 해결해야 한다."
                : actorName + "는(은) 이미 생성된 월드 안에서 안전 경로를 따라 이동했다. 사건이 시작되기 전까지 세계 지형은 바뀌지 않는다.";
            _lastStateChanges = encounterOpened
                ? "위치: 안전 지점까지 이동 · 사건 활성화 · 캠페인 턴 유지"
                : "위치와 이동 시간만 변경 · 캠페인 턴 유지 · D20 없음";
            _lastDialogue = Array.Empty<string>();
            SyncRestorableCandidateFromServer();
            TryGetPlayerPosition(_service.CurrentView, out GridCoord playerPositionAfter);
            List<GridCoord> visualPath = NavigationVisualPath(navigation, playerPositionBefore, playerPositionAfter);
            BeginPlayerPathAnimation(visualPath, _service.CurrentView);
            SyncServerEntityVisuals(_serverRun);
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
            StopAllCoroutines();
            _gmPending = false;
            _showPause = false;
            _service = service;
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

            _ability = AbilityKind.Move;
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
            if (_camera != null)
                _camera.enabled = true;
            SnapCameraToPlayer();
            SetMusic(_assets != null ? _assets.VillageMusic ?? _assets.AdventureMusic : null);
            if (!_serverOnline)
                LocalRunSaveService.Save(_service);
        }

        private void ShowTitle()
        {
            _screenMode = ScreenMode.Title;
            _showPause = false;
            _gmPending = false;
            if (_camera != null)
                _camera.enabled = true;
            SetMusic(_assets != null ? _assets.AdventureMusic ?? _assets.VillageMusic : null);
        }

        private static void ClearServerRunPointer()
        {
            PlayerPrefs.DeleteKey(ServerRunIdKey);
            PlayerPrefs.Save();
        }

        private void BuildWorld(RunView view)
        {
            if (_worldRoot != null && (authoredWorld == null || _worldRoot != authoredWorld.gameObject))
                Destroy(_worldRoot);
            for (int i = 0; i < _runtimeTiles.Count; i++)
                if (_runtimeTiles[i] != null)
                    Destroy(_runtimeTiles[i]);
            _runtimeTiles.Clear();
            _entityVisuals.Clear();
            GameApiClient.WorldSnapshot serverWorld = _serverOnline ? _serverRun?.world : null;
            bool useServerWorld = serverWorld != null && serverWorld.HasCompleteLayout;
            string layoutHash = useServerWorld ? serverWorld.layoutHash : view.Region.LayoutHash;
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
                tilemapRenderer.sortingOrder = TerrainSortingOrder;
            }
            TilemapRenderer activeTilemapRenderer = tilemap.GetComponent<TilemapRenderer>();
            if (activeTilemapRenderer != null)
                activeTilemapRenderer.sortingOrder = TerrainSortingOrder;
            var tilePalette = new Dictionary<TileKind, Tile>();

            for (int y = 0; y < worldHeight; y++)
            {
                for (int x = 0; x < worldWidth; x++)
                {
                    var coord = new GridCoord(x, y);
                    TileKind tileKind = useServerWorld
                        ? TileKindForServer(serverWorld, serverWorld.tileCodes[y * serverWorld.width + x])
                        : view.Region.GetTile(coord).Kind;
                    TileAppearance(tileKind, coord, out Sprite sprite, out Color tint);
                    tint = ApplyBiomePalette(tint, BiomeIdAt(view, coord));
                    if (!tilePalette.TryGetValue(tileKind, out Tile visualTile))
                    {
                        visualTile = ScriptableObject.CreateInstance<Tile>();
                        visualTile.name = "Runtime " + tileKind + " Tile";
                        visualTile.sprite = sprite;
                        visualTile.color = Color.white;
                        visualTile.flags = TileFlags.None;
                        tilePalette.Add(tileKind, visualTile);
                        _runtimeTiles.Add(visualTile);
                    }
                    var cell = new Vector3Int(x, y, 0);
                    tilemap.SetTile(cell, visualTile);
                    tilemap.SetTileFlags(cell, TileFlags.None);
                    tilemap.SetColor(cell, tint);
                }
            }

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
                        AttackFrames = isPlayer ? _playerAttackFrames : Array.Empty<Sprite>(),
                        TargetPosition = root.transform.position,
                        BaseColor = ServerEntityTint(entity),
                        DesiredSize = desired,
                        IsPlayer = isPlayer,
                        IsHostile = hostile
                    };
                    visual.Animator = ConfigureEntityAnimator(renderer,
                        AnimatorForAsset(entity.assetId, entity.kind, isPlayer));
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
                AttackFrames = isPlayer ? _playerAttackFrames : Array.Empty<Sprite>(),
                TargetPosition = root.transform.position,
                BaseColor = EntityTint(entity),
                DesiredSize = desired,
                IsPlayer = isPlayer,
                IsHostile = entity.IsHostile
            };
            visual.Animator = ConfigureEntityAnimator(renderer,
                AnimatorForAsset(entity.AssetId, entity.Kind.ToString(), isPlayer));
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
            if (prefab != null)
            {
                authoredView = Instantiate(prefab, WorldContentRoot);
                authoredView.name = objectName;
                authoredView.Prepare(_whiteSprite, hostile);
                renderer = authoredView.ActorRenderer;
                if (renderer != null)
                    return authoredView.gameObject;
                Destroy(authoredView.gameObject);
            }

            authoredView = null;
            var root = new GameObject(objectName);
            root.transform.SetParent(WorldContentRoot, false);
            renderer = root.AddComponent<SpriteRenderer>();
            return root;
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
            if (visual.AuthoredView != null && visual.AuthoredView.HealthBack != null && visual.AuthoredView.HealthFill != null)
            {
                visual.HealthBack = visual.AuthoredView.HealthBack;
                visual.HealthFill = visual.AuthoredView.HealthFill;
                visual.HealthBack.name = displayName + " HP bg";
                visual.HealthFill.name = displayName + " HP";
                SpriteRenderer back = visual.HealthBack.GetComponent<SpriteRenderer>();
                SpriteRenderer fill = visual.HealthFill.GetComponent<SpriteRenderer>();
                back.color = new Color(0.08f, 0.035f, 0.025f, 0.95f);
                back.sortingOrder = 510;
                fill.color = Ruby;
                fill.sortingOrder = 511;
                return;
            }

            visual.HealthBack = CreateHealthBarObject(displayName + " HP bg",
                new Color(0.08f, 0.035f, 0.025f, 0.95f), 510);
            visual.HealthFill = CreateHealthBarObject(displayName + " HP", Ruby, 511);
        }

        private GameObject CreateHealthBarObject(string name, Color color, int order)
        {
            var bar = new GameObject(name);
            bar.transform.SetParent(WorldContentRoot, false);
            var renderer = bar.AddComponent<SpriteRenderer>();
            renderer.sprite = _whiteSprite;
            renderer.color = color;
            renderer.sortingOrder = order;
            return bar;
        }

        private static void DestroyEntityVisual(EntityVisual visual)
        {
            if (visual == null)
                return;
            DestroyDetachedVisual(visual.HealthBack, visual.Root);
            DestroyDetachedVisual(visual.HealthFill, visual.Root);
            DestroyDetachedVisual(visual.RootComponentLabel, visual.Root);
            if (visual.Root != null)
                Destroy(visual.Root);
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
                if (walkingThisFrame)
                {
                    Vector3 before = visual.Root.transform.position;
                    visual.Root.transform.position = Vector3.MoveTowards(before, visual.TargetPosition,
                        PlayerWalkSpeed * Time.unscaledDeltaTime);
                    float horizontal = visual.Root.transform.position.x - before.x;
                    if (Mathf.Abs(horizontal) > 0.0001f)
                        visual.Renderer.flipX = horizontal < 0f;
                    if (Vector3.SqrMagnitude(visual.Root.transform.position - visual.TargetPosition) < 0.0004f)
                    {
                        visual.Root.transform.position = visual.TargetPosition;
                        if (visual.MovementPath.Count > 0)
                            visual.TargetPosition = visual.MovementPath.Dequeue();
                        else
                            CompletePlayerPathAnimation();
                    }
                }
                else
                {
                    visual.Root.transform.position = Vector3.Lerp(visual.Root.transform.position, visual.TargetPosition, smoothing);
                }
                Sprite[] frames = walkingThisFrame && visual.WalkFrames.Length > 0
                    ? visual.WalkFrames
                    : visual.IsPlayer && Time.unscaledTime < _playerActionUntil && visual.AttackFrames.Length > 0
                        ? visual.AttackFrames
                        : visual.IdleFrames;
                bool usesAnimator = visual.Animator != null && visual.Animator.runtimeAnimatorController != null;
                if (usesAnimator && visual.IsPlayer)
                {
                    visual.Animator.SetBool("IsMoving", walkingThisFrame);
                    visual.Animator.SetBool("IsAttacking",
                        !walkingThisFrame && Time.unscaledTime < _playerActionUntil);
                }
                else if (!usesAnimator && frames.Length > 0)
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
                visual.Renderer.color = selected ? Color.Lerp(visual.BaseColor, new Color(1f, 0.82f, 0.25f), pulse) : visual.BaseColor;
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
            _reopenDialogueAfterWalk = true;
            _authoredDialogueDismissed = true;
            _sceneUi?.SetObjectActive("Story Panel", false);
            _cameraInspectCoord = null;
            _cameraInspectUntil = 0f;
        }

        private void CompletePlayerPathAnimation()
        {
            _playerWalking = false;
            if (_reopenDialogueAfterWalk)
            {
                _reopenDialogueAfterWalk = false;
                _authoredDialogueDismissed = false;
                _sceneUi?.SetObjectActive("Story Panel", true);
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
                _selectionRenderer.color = _ability == AbilityKind.Restore
                    ? new Color(0.35f, 0.92f, 1f, 0.5f)
                    : new Color(1f, 0.85f, 0.35f, 0.42f);
            }
        }

        private void ConfigureCamera()
        {
            _camera = authoredCamera != null ? authoredCamera : Camera.main;
            if (_camera == null)
            {
                var cameraObject = new GameObject("Main Camera");
                cameraObject.tag = "MainCamera";
                _camera = cameraObject.AddComponent<Camera>();
            }
            _camera.orthographic = true;
            _camera.orthographicSize = authoringSettings != null ? authoringSettings.CameraOrthographicSize : 8.25f;
            _camera.backgroundColor = authoringSettings != null
                ? authoringSettings.CameraBackground
                : new Color(0.025f, 0.033f, 0.02f, 1f);
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.transform.position = new Vector3(0f, 0f, -10f);
            if (_camera.GetComponent<AudioListener>() == null && FindAnyObjectByType<AudioListener>() == null)
                _camera.gameObject.AddComponent<AudioListener>();
            UpdateCameraViewport(true);
        }

        private void UpdateCameraViewport(bool force = false)
        {
            if (_camera == null || (!force && _lastScreenWidth == Screen.width && _lastScreenHeight == Screen.height))
                return;
            _lastScreenWidth = Screen.width;
            _lastScreenHeight = Screen.height;
            _camera.rect = new Rect(0f, 0f, 1f, 1f);
        }

        private void UpdateCameraFollow()
        {
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
            float halfHeight = _camera.orthographicSize;
            float aspect = _camera.pixelHeight > 0 ? _camera.pixelWidth / (float)_camera.pixelHeight : 1.3f;
            float halfWidth = halfHeight * aspect;
            Vector2 origin = MapOrigin(view);
            int worldWidth = ActiveWorldWidth(view);
            int worldHeight = ActiveWorldHeight(view);
            float minX = origin.x + Mathf.Min(halfWidth, worldWidth * 0.5f);
            float maxX = origin.x + worldWidth - Mathf.Min(halfWidth, worldWidth * 0.5f);
            float minY = origin.y + Mathf.Min(halfHeight, worldHeight * 0.5f);
            float maxY = origin.y + worldHeight - Mathf.Min(halfHeight, worldHeight * 0.5f);
            desired.x = Mathf.Clamp(desired.x, minX, maxX);
            desired.y = Mathf.Clamp(desired.y, minY, maxY);
            desired.z = -10f;
            _camera.transform.position = Vector3.SmoothDamp(_camera.transform.position, desired, ref _cameraVelocity, 0.16f,
                Mathf.Infinity, Time.unscaledDeltaTime);
        }

        private void SnapCameraToPlayer()
        {
            if (_service == null || _camera == null)
                return;
            if (!TryGetPlayerPosition(_service.CurrentView, out GridCoord playerPosition))
                return;
            Vector3 position = WorldPosition(MapOrigin(_service.CurrentView), playerPosition);
            _camera.transform.position = new Vector3(position.x, position.y, -10f);
            _cameraVelocity = Vector3.zero;
        }

        private void ConfigureAudio()
        {
            _musicSource = authoredMusicSource != null ? authoredMusicSource : gameObject.AddComponent<AudioSource>();
            _musicSource.loop = true;
            _musicSource.playOnAwake = false;
            _sfxSource = authoredSfxSource != null ? authoredSfxSource : gameObject.AddComponent<AudioSource>();
            _sfxSource.loop = false;
            _sfxSource.playOnAwake = false;
            ApplyAudioVolumes();
        }

        private void SetMusic(AudioClip clip)
        {
            if (_musicSource == null || clip == null || _currentMusic == clip)
                return;
            _currentMusic = clip;
            _musicSource.Stop();
            _musicSource.clip = clip;
            _musicSource.Play();
        }

        private void PlaySfx(AudioClip clip)
        {
            if (_sfxSource != null && clip != null && _sfxVolume > 0.001f)
                _sfxSource.PlayOneShot(clip, _sfxVolume);
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

            if (_assets != null)
            {
                if (_assets.PlayerAnimatorController == null)
                {
                    _playerIdleFrames = CreateSheetFrames(_assets.PlayerIdleSheet, Mathf.Max(1, _assets.PlayerFrameSize), 3, "Player Idle");
                    _playerWalkFrames = CreateSheetFrames(_assets.PlayerWalkSheet, Mathf.Max(1, _assets.PlayerFrameSize), 3, "Player Walk");
                    _playerAttackFrames = CreateSheetFrames(_assets.PlayerAttackSheet, Mathf.Max(1, _assets.PlayerFrameSize), 3, "Player Attack");
                }
                if (_assets.SlimeAnimatorController == null)
                    _slimeFrames = CreateSheetFrames(_assets.SlimeSheet, Mathf.Max(1, _assets.CreatureFrameSize), 3, "Slime");
                if (_assets.VillagerAnimatorController == null)
                    _villagerFrames = CreateSheetFrames(_assets.VillagerWalkSheet, Mathf.Max(1, _assets.CreatureFrameSize), 3, "Villager");
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

        private Sprite CreateAtlasSprite(Texture2D texture, Rect requestedRect, string spriteName, Color fallbackColor)
        {
            if (texture == null || texture.width <= 0 || texture.height <= 0)
                return CreateSolidSprite(fallbackColor, spriteName + " Fallback");
            Rect rect = SafeRect(texture, requestedRect);
            Sprite sprite = Sprite.Create(texture, rect, new Vector2(0.5f, 0.5f), 16f, 0, SpriteMeshType.FullRect);
            sprite.name = spriteName;
            _runtimeSprites.Add(sprite);
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

        private void TileAppearance(TileKind kind, GridCoord coord, out Sprite sprite, out Color tint)
        {
            switch (kind)
            {
                case TileKind.Dirt:
                    sprite = _dirtSprite;
                    tint = new Color(0.95f, 0.9f, 0.78f, 1f);
                    break;
                case TileKind.Water:
                    sprite = _grassSprite;
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
                    sprite = _darkGrassSprite;
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
                    sprite = _grassSprite;
                    tint = Color.white;
                    break;
            }
            float variation = (((coord.X * 31 + coord.Y * 17) & 3) - 1.5f) * 0.025f;
            tint = new Color(Mathf.Clamp01(tint.r + variation), Mathf.Clamp01(tint.g + variation), Mathf.Clamp01(tint.b + variation), tint.a);
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
            return view.Region.AreaAt(coord)?.Biome ?? string.Empty;
        }

        private static Color ApplyBiomePalette(Color tileTint, string biomeId)
        {
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
                Mathf.Clamp01(tileTint.r * 0.48f + biome.r * 0.62f),
                Mathf.Clamp01(tileTint.g * 0.48f + biome.g * 0.62f),
                Mathf.Clamp01(tileTint.b * 0.48f + biome.b * 0.62f),
                tileTint.a);
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
            GameObject marker = prefab != null
                ? Instantiate(prefab, WorldContentRoot)
                : new GameObject("Landmark");
            marker.name = "Landmark · " + (label ?? campaignRole ?? "POI");
            if (prefab == null)
                marker.transform.SetParent(WorldContentRoot, false);
            marker.transform.position = WorldPosition(origin, coord) + new Vector3(0f, 0.1f, -0.05f);
            SpriteRenderer renderer = marker.GetComponent<SpriteRenderer>();
            if (renderer == null)
                renderer = marker.AddComponent<SpriteRenderer>();
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
            GameObject label;
            TextMesh text;
            if (authoredView != null && authoredView.FinaleLabelText != null)
            {
                label = authoredView.FinaleLabel;
                text = authoredView.FinaleLabelText;
                label.SetActive(true);
            }
            else
            {
                label = new GameObject("Finale label");
                label.transform.SetParent(WorldContentRoot, false);
                text = label.AddComponent<TextMesh>();
            }
            label.name = "Finale label · " + component;
            text.text = RootComponentLabel(component, displayName);
            text.anchor = TextAnchor.MiddleCenter;
            text.alignment = TextAlignment.Center;
            text.fontSize = 34;
            text.characterSize = 0.075f;
            text.fontStyle = FontStyle.Bold;
            text.color = Color.Lerp(tint, Color.white, 0.28f);
            MeshRenderer renderer = label.GetComponent<MeshRenderer>();
            if (renderer != null) renderer.sortingOrder = 525;
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
                return _assets.PlayerAnimatorController;
            if (id.Contains("slime") || entityKind.Contains("enemy"))
                return _assets.SlimeAnimatorController;
            if (entityKind.Contains("npc") || id.Contains("villager") || id.Contains("merchant") || id.Contains("healer"))
                return _assets.VillagerAnimatorController;
            return null;
        }

        private static Animator ConfigureEntityAnimator(
            SpriteRenderer renderer,
            RuntimeAnimatorController controller)
        {
            if (renderer == null || controller == null)
                return null;
            Animator animator = renderer.GetComponent<Animator>();
            if (animator == null)
                animator = renderer.gameObject.AddComponent<Animator>();
            animator.runtimeAnimatorController = controller;
            animator.Rebind();
            animator.Play("Idle", 0, 0f);
            animator.Update(0f);
            return animator;
        }

        private static Color TintForKind(string kind)
        {
            string value = (kind ?? string.Empty).ToLowerInvariant();
            if (value.Contains("enemy")) return new Color(1f, 0.78f, 0.72f, 1f);
            if (value.Contains("npc")) return new Color(1f, 0.95f, 0.8f, 1f);
            return Color.white;
        }

        private void SetAbility(AbilityKind ability)
        {
            if (_ability.Equals(ability))
                return;
            _ability = ability;
            _selectedTarget = null;
            _selectedSecondaryTarget = null;
            _selectedCoord = null;
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
            if (!_encounterMoveRequired) values.Add(AbilityKind.Move);
            AbilityKind objectiveSkill = AbilityKind.Copy;
            for (int i = 0; i < view.RequiredBeats.Count; i++)
            {
                CampaignBeatState beat = view.RequiredBeats[i];
                if (!beat.IsCompleted && !beat.IsSkipped)
                {
                    objectiveSkill = beat.TriggerAbility;
                    break;
                }
            }
            if (objectiveSkill != AbilityKind.Move && !values.Contains(objectiveSkill)) values.Add(objectiveSkill);
            AbilityKind contextual = _encounterMoveRequired ? AbilityKind.Delete : AbilityKind.Connect;
            if (!values.Contains(contextual)) values.Add(contextual);
            if (values.Count < 2) values.Add(AbilityKind.Restore);
            return values.ToArray();
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
            if (ability == AbilityKind.Copy) return "조사";
            if (ability == AbilityKind.Delete) return "전투/배치";
            if (ability == AbilityKind.Connect)
            {
                if (_selectedTarget.HasValue)
                {
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
            if (ability == "Undo") return "직전 가역 턴을 보상 이벤트로 되돌립니다. 턴 번호와 맵은 되감지 않습니다.";
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
            if (_musicSource != null)
                _musicSource.volume = _musicVolume * 0.45f;
            if (_sfxSource != null)
                _sfxSource.volume = _sfxVolume;
        }

        private void AddLog(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return;
            _sessionLog.Add(message.Trim());
            if (_sessionLog.Count > 18)
                _sessionLog.RemoveAt(0);
            _logScroll.y = float.MaxValue;
        }

        private void CreateUiTextures()
        {
            _inkTexture = MakeTexture(Ink, "Ink");
            _panelTexture = MakeTexture(Panel, "Wood Panel");
            _raisedTexture = MakeTexture(PanelRaised, "Raised Panel");
            _buttonTexture = MakeTexture(Hex("3b291b"), "Button");
            _buttonHoverTexture = MakeTexture(Hex("503821"), "Button Hover");
            _buttonPressedTexture = MakeTexture(Hex("21160e"), "Button Pressed");
            _selectedTexture = MakeTexture(Hex("69471f"), "Selected Button");
            _disabledTexture = MakeTexture(Hex("211b16"), "Disabled Button");
            _fieldTexture = MakeTexture(Hex("110c09"), "Intent Field");
            _whiteTexture = MakeTexture(Color.white, "White");
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

        private void EnsureStyles()
        {
            if (_stylesReady)
                return;
            Font font = _koreanFont != null ? _koreanFont : (_assets != null ? _assets.PixelFont : GUI.skin.font);
            _labelStyle = new GUIStyle(GUI.skin.label) { font = font, fontSize = 18, wordWrap = true, normal = { textColor = Parchment } };
            _titleStyle = new GUIStyle(_labelStyle) { fontSize = 34, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { textColor = Gold } };
            _subtitleStyle = new GUIStyle(_labelStyle) { fontSize = 18, alignment = TextAnchor.MiddleCenter, normal = { textColor = Muted } };
            _headerStyle = new GUIStyle(_labelStyle) { fontSize = 20, fontStyle = FontStyle.Bold, normal = { textColor = Gold } };
            _smallStyle = new GUIStyle(_labelStyle) { fontSize = 15, normal = { textColor = Parchment } };
            _mutedStyle = new GUIStyle(_smallStyle) { alignment = TextAnchor.MiddleCenter, normal = { textColor = Muted } };
            _centerStyle = new GUIStyle(_labelStyle) { fontSize = 16, alignment = TextAnchor.MiddleCenter, normal = { textColor = Parchment } };
            _previewPremiseStyle = new GUIStyle(_centerStyle) { fontSize = 13 };
            _numberStyle = new GUIStyle(_headerStyle) { fontSize = 27, alignment = TextAnchor.MiddleLeft, normal = { textColor = Parchment } };

            _buttonStyle = new GUIStyle(GUI.skin.button)
            {
                font = font,
                fontSize = 17,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                padding = new RectOffset(12, 12, 8, 8),
                normal = { background = _buttonTexture, textColor = Parchment },
                hover = { background = _buttonHoverTexture, textColor = Color.white },
                active = { background = _buttonPressedTexture, textColor = Gold },
                focused = { background = _buttonHoverTexture, textColor = Color.white },
                onNormal = { background = _selectedTexture, textColor = Color.white },
                onHover = { background = _selectedTexture, textColor = Color.white }
            };
            _selectedButtonStyle = new GUIStyle(_buttonStyle)
            {
                normal = { background = _selectedTexture, textColor = Color.white },
                hover = { background = _buttonHoverTexture, textColor = Color.white }
            };
            _dangerButtonStyle = new GUIStyle(_buttonStyle)
            {
                normal = { background = _buttonPressedTexture, textColor = new Color(1f, 0.65f, 0.56f) }
            };
            _textAreaStyle = new GUIStyle(GUI.skin.textArea)
            {
                font = font,
                fontSize = 16,
                wordWrap = true,
                padding = new RectOffset(13, 13, 10, 10),
                normal = { background = _fieldTexture, textColor = Parchment },
                focused = { background = _fieldTexture, textColor = Color.white }
            };
            _stylesReady = true;
        }

        private void DrawWoodPanel(Rect rect, bool raised)
        {
            if (_assets != null && _assets.WoodBackground != null)
                DrawSprite(_assets.WoodBackground, rect, new Color(0.48f, 0.34f, 0.2f, 0.22f));
            DrawRect(rect, new Color(raised ? PanelRaised.r : Panel.r, raised ? PanelRaised.g : Panel.g,
                raised ? PanelRaised.b : Panel.b, 0.92f));
            DrawBorder(rect, GoldDim, 2f);
            DrawBorder(new Rect(rect.x + 5f, rect.y + 5f, rect.width - 10f, rect.height - 10f),
                new Color(GoldDim.r, GoldDim.g, GoldDim.b, 0.45f), 1f);
        }

        private void DrawOrnament(Rect rect)
        {
            DrawRect(rect, GoldDim);
            DrawRect(new Rect(rect.x + rect.width * 0.44f, rect.y - 3f, rect.width * 0.12f, 8f), Gold);
        }

        private void DrawRect(Rect rect, Color color)
        {
            Color old = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, _whiteTexture != null ? _whiteTexture : Texture2D.whiteTexture, ScaleMode.StretchToFill, true);
            GUI.color = old;
        }

        private void DrawBorder(Rect rect, Color color, float thickness)
        {
            DrawRect(new Rect(rect.x, rect.y, rect.width, thickness), color);
            DrawRect(new Rect(rect.x, rect.yMax - thickness, rect.width, thickness), color);
            DrawRect(new Rect(rect.x, rect.y, thickness, rect.height), color);
            DrawRect(new Rect(rect.xMax - thickness, rect.y, thickness, rect.height), color);
        }

        private static void DrawSprite(Sprite sprite, Rect rect, Color tint)
        {
            if (sprite == null || sprite.texture == null)
                return;
            Rect source = sprite.textureRect;
            var uv = new Rect(source.x / sprite.texture.width, source.y / sprite.texture.height,
                source.width / sprite.texture.width, source.height / sprite.texture.height);
            Color old = GUI.color;
            GUI.color = tint;
            GUI.DrawTextureWithTexCoords(rect, sprite.texture, uv, true);
            GUI.color = old;
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
                case "temperate_forest_field": return "온대 숲·들판";
                case "river_wetland": return "강·습지";
                case "arid_desert": return "건조 사막";
                case "frost_highland": return "설원 고지";
                case "subterranean_cavern": return "지하 동굴";
                case "ancient_ruins": return "고대 유적";
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

        private void ApplyServerTurnPresentation(GameApiClient.TurnSnapshot turn)
        {
            GameApiClient.DiceSnapshot dice = turn.dice;
            _lastD20 = dice?.raw ?? 0;
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
                        modifierParts.Add((string.IsNullOrWhiteSpace(modifier.source) ? "modifier" : modifier.source) + " " + Signed(modifier.value));
                }
            }
            if (modifierParts.Count == 0 && dice != null && dice.modifier != 0)
            {
                _lastModifier = dice.modifier;
                modifierParts.Add("server modifier " + Signed(dice.modifier));
            }
            _lastModifierBreakdown = modifierParts.Count == 0 ? "수정치 없음" : string.Join(", ", modifierParts);
            _lastDifficulty = dice?.difficulty ?? 0;
            _lastMechanicalScore = dice?.mechanicalScore ?? turn.mechanicalScore;
            _lastActionContext = ActionContextLabel(turn.actionContext);
            _lastOutcome = KoreanOutcome(turn.outcome);
            _lastAttempt = string.IsNullOrWhiteSpace(turn.normalizedAttempt) ? "서버가 정규화한 시도 없음" : turn.normalizedAttempt;
            _lastExplanation = ExplainResultDifference(turn);
            _lastNarrative = !_gmEnabled
                ? "생성형 장면·대사 표시가 꺼져 있습니다. 권위 규칙 이벤트와 세계 기억은 그대로 적용되었습니다."
                : !string.IsNullOrWhiteSpace(turn.narrative?.body)
                    ? turn.narrative.body
                    : !string.IsNullOrWhiteSpace(turn.narrative?.summary) ? turn.narrative.summary : "규칙 결과가 상태에 반영되었습니다.";
            _lastDialogue = _gmEnabled ? turn.narrative?.dialogue ?? Array.Empty<string>() : Array.Empty<string>();
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
            bool open = encounter != null && !string.Equals(encounter.status, "resolved", StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(encounter.status, "closed", StringComparison.OrdinalIgnoreCase);
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
            string cost = turn.consequenceBudget > 0 ? "합병증 예산 " + turn.consequenceBudget + "이 적용되었습니다." : "추가 합병증 예산은 없습니다.";
            string normalized = string.IsNullOrWhiteSpace(turn.normalizedAttempt)
                ? "서버가 별도의 실제 시도 문장을 반환하지 않았습니다."
                : "서버가 선택된 스킬·대상·현재 위치와 장면 상태를 검증해 실제 시도를 확정했습니다.";
            string serverExplanation = string.IsNullOrWhiteSpace(turn.dice?.outcomeExplanation)
                ? string.Empty
                : " 서버 결과 설명: " + turn.dice.outcomeExplanation.Trim();
            return normalized + " 자연어 메모는 규칙 판정에 사용되지 않습니다. " + cost +
                   " 문맥: " + _lastActionContext + " · 수정: " + _lastModifierBreakdown + "." + serverExplanation;
        }

        private static string HumanizeServerEvent(GameApiClient.EventSnapshot value)
        {
            if (value == null) return string.Empty;
            if (!string.IsNullOrWhiteSpace(value.text)) return value.text;
            return HumanizeEvent(value.type);
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
                default: return "move";
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
                default: return AbilityKind.Move;
            }
        }

        private static Color Hex(string rgb)
        {
            if (!ColorUtility.TryParseHtmlString("#" + rgb, out Color color))
                return Color.white;
            // This project renders in Linear color space; convert authored hex colors before IMGUI tinting.
            return color.linear;
        }
    }
}
