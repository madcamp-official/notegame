using System;
using System.Collections.Generic;
using KeyboardWanderer.Gameplay;
using UnityEngine;
using UnityEngine.UI;

namespace KeyboardWanderer.Demo
{
    public enum KeyboardWandererUiText
    {
        TitleHeading,
        TitleSubtitle,
        TitleSeed,
        TitlePremise,
        TitleStatus,
        SceneLocation,
        SceneTitle,
        DialogueSpeaker,
        Story,
        NextDialogueLabel,
        ActionHint,
        CopySkillLabel,
        DeleteSkillLabel,
        UndoSkillLabel,
        ConfirmActionLabel,
        EndingHeading,
        EndingText
    }

    public enum KeyboardWandererUiButton
    {
        NewRun,
        Continue,
        Settings,
        SettingsBack,
        DeleteSave,
        Move,
        Copy,
        Delete,
        Connect,
        Restore,
        Undo,
        Search,
        SelectAll,
        ConfirmAction,
        NextDialogue,
        Resume,
        PauseSettings,
        Title,
        EndingNewRun,
        EndingTitle
    }

    [DisallowMultipleComponent]
    public sealed class KeyboardWandererSceneUI : MonoBehaviour
    {
        [Serializable]
        private struct TextBinding
        {
            public KeyboardWandererUiText Id;
            public Text Target;
        }

        [Serializable]
        private struct ButtonBinding
        {
            public KeyboardWandererUiButton Id;
            public Button Target;
        }

        [Header("Screens")]
        [SerializeField] private GameObject titleScreen;
        [SerializeField] private GameObject gameHud;
        [SerializeField] private GameObject settingsScreen;
        [SerializeField] private GameObject pauseScreen;
        [SerializeField] private GameObject endingScreen;

        [Header("Typed controls")]
        [SerializeField] private TextBinding[] textBindings = Array.Empty<TextBinding>();
        [SerializeField] private ButtonBinding[] buttonBindings = Array.Empty<ButtonBinding>();
        [SerializeField] private Slider musicSlider;
        [SerializeField] private Slider sfxSlider;
        [SerializeField] private Toggle gmToggle;
        [SerializeField] private GameObject storyPanel;

        [Header("Dynamic presentation")]
        [SerializeField] private NinjaAdventureAssetManifest assetManifest;
        [SerializeField] private Image titleCharacter;
        [SerializeField] private Image copyActionKeycap;
        [SerializeField] private Image outcomeEmote;

        private readonly Dictionary<KeyboardWandererUiText, Text> _texts =
            new Dictionary<KeyboardWandererUiText, Text>();
        private readonly Dictionary<KeyboardWandererUiButton, Button> _buttons =
            new Dictionary<KeyboardWandererUiButton, Button>();
        private readonly Dictionary<KeyboardWandererUiButton, ColorBlock> _authoredButtonColors =
            new Dictionary<KeyboardWandererUiButton, ColorBlock>();
        private readonly Dictionary<KeyboardWandererUiButton, bool> _buttonInteractable =
            new Dictionary<KeyboardWandererUiButton, bool>();
        private readonly Dictionary<KeyboardWandererUiButton, bool> _buttonSelected =
            new Dictionary<KeyboardWandererUiButton, bool>();
        private Image _minimapMap;
        private Text _minimapPlaceholder;
        private Text _minimapStatus;
        private bool _bound;
        private bool _copyPasteMode;

        public bool IsReady => titleScreen != null && gameHud != null && settingsScreen != null &&
                               textBindings.Length > 0 && buttonBindings.Length > 0;

        private void Awake()
        {
            BuildLookup();
        }

        private void OnValidate()
        {
#if UNITY_EDITOR
            AutoWire();
#endif
            BuildLookup();
        }

        public void Bind(KeyboardWandererDemoController controller)
        {
            if (_bound || controller == null)
                return;
            BuildLookup();
            BindButton(KeyboardWandererUiButton.NewRun, controller.UiStartNewRun);
            BindButton(KeyboardWandererUiButton.Continue, controller.UiContinueRun);
            BindButton(KeyboardWandererUiButton.Settings, controller.UiOpenSettings);
            BindButton(KeyboardWandererUiButton.SettingsBack, controller.UiCloseSettings);
            BindButton(KeyboardWandererUiButton.DeleteSave, controller.UiDeleteSave);
            BindButton(KeyboardWandererUiButton.Move, () => controller.UiSetAbility(AbilityKind.Move));
            BindButton(KeyboardWandererUiButton.Copy, () => controller.UiSetAbility(AbilityKind.Copy));
            BindButton(KeyboardWandererUiButton.Delete, () => controller.UiSetAbility(AbilityKind.Delete));
            BindButton(KeyboardWandererUiButton.Connect, () => controller.UiSetAbility(AbilityKind.Connect));
            BindButton(KeyboardWandererUiButton.Restore, () => controller.UiSetAbility(AbilityKind.Restore));
            BindButton(KeyboardWandererUiButton.Undo, () => controller.UiSetAbility(AbilityKind.Undo));
            BindButton(KeyboardWandererUiButton.Search, () => controller.UiSetAbility(AbilityKind.Search));
            BindButton(KeyboardWandererUiButton.SelectAll, () => controller.UiSetAbility(AbilityKind.SelectAll));
            BindButton(KeyboardWandererUiButton.ConfirmAction, controller.UiSubmit);
            BindButton(KeyboardWandererUiButton.NextDialogue, controller.UiAdvanceDialogue);
            BindButton(KeyboardWandererUiButton.Resume, controller.UiResume);
            BindButton(KeyboardWandererUiButton.PauseSettings, controller.UiOpenSettingsFromPause);
            BindButton(KeyboardWandererUiButton.Title, controller.UiShowTitle);
            BindButton(KeyboardWandererUiButton.EndingNewRun, controller.UiStartNewRun);
            BindButton(KeyboardWandererUiButton.EndingTitle, controller.UiShowTitle);

            if (musicSlider != null) musicSlider.onValueChanged.AddListener(controller.UiSetMusicVolume);
            if (sfxSlider != null) sfxSlider.onValueChanged.AddListener(controller.UiSetSfxVolume);
            if (gmToggle != null) gmToggle.onValueChanged.AddListener(controller.UiSetGmEnabled);
            _bound = true;
        }

        public void Show(bool title, bool settings, bool playing, bool paused, bool ended)
        {
            SetActive(titleScreen, title);
            SetActive(gameHud, playing);
            SetActive(settingsScreen, settings);
            SetActive(pauseScreen, playing && paused);
            SetActive(endingScreen, playing && ended);
        }

        public void SetText(KeyboardWandererUiText id, string value)
        {
            if (_texts.TryGetValue(id, out Text text) && text != null && text.text != value)
                text.text = value ?? string.Empty;
        }

        public void SetButtonState(KeyboardWandererUiButton id, bool interactable, bool selected = false)
        {
            if (!_buttons.TryGetValue(id, out Button button) || button == null)
                return;
            if (_buttonInteractable.TryGetValue(id, out bool previousInteractable) &&
                _buttonSelected.TryGetValue(id, out bool previousSelected) &&
                previousInteractable == interactable && previousSelected == selected)
                return;
            _buttonInteractable[id] = interactable;
            _buttonSelected[id] = selected;
            button.interactable = interactable;
            ColorBlock colors = _authoredButtonColors.TryGetValue(id, out ColorBlock authored)
                ? authored
                : button.colors;
            colors.fadeDuration = 0.055f;
            colors.highlightedColor = new Color(1f, 0.84f, 0.42f, 1f);
            colors.pressedColor = new Color(0.94f, 0.49f, 0.16f, 1f);
            colors.selectedColor = new Color(1f, 0.75f, 0.25f, 1f);
            colors.disabledColor = new Color(0.38f, 0.35f, 0.32f, 0.42f);
            if (selected)
            {
                colors.normalColor = new Color(1f, 0.72f, 0.2f, 1f);
                colors.highlightedColor = new Color(1f, 0.9f, 0.55f, 1f);
                colors.pressedColor = new Color(0.86f, 0.38f, 0.12f, 1f);
            }
            button.colors = colors;
            button.transform.localScale = selected ? new Vector3(1.065f, 1.065f, 1f) : Vector3.one;

            Graphic target = button.targetGraphic;
            if (target != null)
            {
                Outline outline = target.GetComponent<Outline>();
                if (outline == null)
                    outline = target.gameObject.AddComponent<Outline>();
                outline.effectColor = new Color(1f, 0.78f, 0.22f, 0.95f);
                outline.effectDistance = new Vector2(2f, -2f);
                outline.useGraphicAlpha = true;
                outline.enabled = selected;
            }
        }

        public void SetMinimap(Sprite sprite, string status)
        {
            EnsureMinimapControls();
            if (_minimapMap != null && _minimapMap.sprite != sprite)
                _minimapMap.sprite = sprite;
            if (_minimapMap != null)
                _minimapMap.enabled = sprite != null;
            if (_minimapPlaceholder != null)
                _minimapPlaceholder.gameObject.SetActive(sprite == null);
            if (_minimapStatus != null && _minimapStatus.text != status)
                _minimapStatus.text = status ?? string.Empty;
        }

        private void EnsureMinimapControls()
        {
            if (_minimapStatus == null)
                _minimapStatus = FindNamedComponent<Text>("Minimap Status");
            if (_minimapPlaceholder == null)
                _minimapPlaceholder = FindNamedComponent<Text>("Minimap Placeholder");
            if (_minimapMap != null)
                return;
            GameObject preview = FindNamedObject("Minimap Preview");
            if (preview == null)
                return;
            Transform existing = preview.transform.Find("Runtime Minimap");
            if (existing != null)
                _minimapMap = existing.GetComponent<Image>();
            if (_minimapMap != null)
                return;
            var mapObject = new GameObject("Runtime Minimap", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            RectTransform rect = mapObject.GetComponent<RectTransform>();
            rect.SetParent(preview.transform, false);
            rect.anchorMin = new Vector2(0.04f, 0.06f);
            rect.anchorMax = new Vector2(0.96f, 0.94f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            _minimapMap = mapObject.GetComponent<Image>();
            _minimapMap.preserveAspect = true;
            _minimapMap.raycastTarget = false;
        }

        private GameObject FindNamedObject(string objectName)
        {
            Transform[] items = GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < items.Length; i++)
                if (string.Equals(items[i].name, objectName, StringComparison.Ordinal))
                    return items[i].gameObject;
            return null;
        }

        private T FindNamedComponent<T>(string objectName) where T : Component
        {
            GameObject target = FindNamedObject(objectName);
            return target != null ? target.GetComponent<T>() : null;
        }

        public void SetStoryVisible(bool visible)
        {
            SetActive(storyPanel, visible);
        }

        public void SetTitleCharacter(Sprite sprite)
        {
            if (titleCharacter == null)
            {
                Image[] images = GetComponentsInChildren<Image>(true);
                for (int i = 0; i < images.Length; i++)
                {
                    if (!string.Equals(images[i].name, "Title Character", StringComparison.Ordinal)) continue;
                    titleCharacter = images[i];
                    break;
                }
            }
            if (titleCharacter != null && sprite != null && titleCharacter.sprite != sprite)
                titleCharacter.sprite = sprite;
        }

        public void SetMusicVolume(float value)
        {
            if (musicSlider != null) musicSlider.SetValueWithoutNotify(value);
        }

        public void SetSfxVolume(float value)
        {
            if (sfxSlider != null) sfxSlider.SetValueWithoutNotify(value);
        }

        public void SetGmEnabled(bool value)
        {
            if (gmToggle != null) gmToggle.SetIsOnWithoutNotify(value);
        }

        public void SetCopyPasteMode(bool pasteMode)
        {
            if (assetManifest == null || copyActionKeycap == null || _copyPasteMode == pasteMode)
                return;
            _copyPasteMode = pasteMode;
            copyActionKeycap.sprite = pasteMode ? assetManifest.KeyV : assetManifest.KeyC;
        }

        public void SetOutcomeEmote(string outcome)
        {
            if (assetManifest == null || outcomeEmote == null ||
                assetManifest.Emotes == null || assetManifest.Emotes.Length < 30)
                return;
            int number;
            switch ((outcome ?? string.Empty).ToUpperInvariant())
            {
                case "CRITICALSUCCESS":
                case "CRITICAL_SUCCESS": number = 27; break;
                case "SUCCESS": number = 11; break;
                case "PARTIALSUCCESS":
                case "PARTIAL_SUCCESS": number = 7; break;
                case "FAILURE": number = 13; break;
                case "CRITICALFAILURE":
                case "CRITICAL_FAILURE": number = 22; break;
                default: number = 23; break;
            }
            outcomeEmote.sprite = assetManifest.Emotes[number - 1];
        }

        private void BuildLookup()
        {
            _texts.Clear();
            _buttons.Clear();
            _authoredButtonColors.Clear();
            _buttonInteractable.Clear();
            _buttonSelected.Clear();
            for (int i = 0; i < textBindings.Length; i++)
            {
                if (textBindings[i].Target != null) _texts[textBindings[i].Id] = textBindings[i].Target;
            }
            for (int i = 0; i < buttonBindings.Length; i++)
            {
                if (buttonBindings[i].Target == null) continue;
                _buttons[buttonBindings[i].Id] = buttonBindings[i].Target;
                _authoredButtonColors[buttonBindings[i].Id] = buttonBindings[i].Target.colors;
            }
        }

        private void BindButton(KeyboardWandererUiButton id, UnityEngine.Events.UnityAction action)
        {
            if (_buttons.TryGetValue(id, out Button button) && button != null)
                button.onClick.AddListener(action);
        }

#if UNITY_EDITOR
        public void AutoWire()
        {
            titleScreen = FindObject("Title Screen");
            gameHud = FindObject("Game HUD");
            settingsScreen = FindObject("Settings Screen");
            pauseScreen = FindObject("Pause Screen");
            endingScreen = FindObject("Ending Screen");
            storyPanel = FindObject("Story Panel");
            musicSlider = FindComponent<Slider>("Music Slider");
            sfxSlider = FindComponent<Slider>("Sfx Slider");
            gmToggle = FindComponent<Toggle>("GM Toggle");
            titleCharacter = FindComponent<Image>("Title Character");
            outcomeEmote = FindComponent<Image>("Speaker Emote");
            assetManifest = UnityEditor.AssetDatabase.LoadAssetAtPath<NinjaAdventureAssetManifest>(
                "Assets/KeyboardWanderer/Resources/NinjaAdventureAssetManifest.asset");

            textBindings = BuildTextBindings();
            buttonBindings = BuildButtonBindings();
            Button copy = FindComponent<Button>("Copy Skill Button");
            Transform copyKey = copy != null ? copy.transform.Find("Ninja Keycaps/Key 1") : null;
            copyActionKeycap = copyKey != null ? copyKey.GetComponent<Image>() : null;
        }

        private TextBinding[] BuildTextBindings()
        {
            Array ids = Enum.GetValues(typeof(KeyboardWandererUiText));
            var result = new TextBinding[ids.Length];
            for (int i = 0; i < ids.Length; i++)
            {
                var id = (KeyboardWandererUiText)ids.GetValue(i);
                result[i] = new TextBinding { Id = id, Target = FindComponent<Text>(TextObjectName(id)) };
            }
            return result;
        }

        private ButtonBinding[] BuildButtonBindings()
        {
            Array ids = Enum.GetValues(typeof(KeyboardWandererUiButton));
            var result = new ButtonBinding[ids.Length];
            for (int i = 0; i < ids.Length; i++)
            {
                var id = (KeyboardWandererUiButton)ids.GetValue(i);
                result[i] = new ButtonBinding { Id = id, Target = FindComponent<Button>(ButtonObjectName(id)) };
            }
            return result;
        }

        private GameObject FindObject(string objectName)
        {
            Transform[] items = GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < items.Length; i++)
            {
                if (string.Equals(items[i].name, objectName, StringComparison.Ordinal)) return items[i].gameObject;
            }
            return null;
        }

        private T FindComponent<T>(string objectName) where T : Component
        {
            GameObject item = FindObject(objectName);
            return item != null ? item.GetComponent<T>() : null;
        }

        private static string TextObjectName(KeyboardWandererUiText id)
        {
            switch (id)
            {
                case KeyboardWandererUiText.TitleHeading: return "Title Heading";
                case KeyboardWandererUiText.TitleSubtitle: return "Title Subtitle";
                case KeyboardWandererUiText.TitleSeed: return "Title Seed";
                case KeyboardWandererUiText.TitlePremise: return "Title Premise";
                case KeyboardWandererUiText.TitleStatus: return "Title Status";
                case KeyboardWandererUiText.SceneLocation: return "Scene Location";
                case KeyboardWandererUiText.SceneTitle: return "Scene Title";
                case KeyboardWandererUiText.DialogueSpeaker: return "Dialogue Speaker";
                case KeyboardWandererUiText.Story: return "Story Text";
                case KeyboardWandererUiText.NextDialogueLabel: return "Next Dialogue Label";
                case KeyboardWandererUiText.ActionHint: return "Action Hint";
                case KeyboardWandererUiText.CopySkillLabel: return "Copy Skill Label";
                case KeyboardWandererUiText.DeleteSkillLabel: return "Delete Skill Label";
                case KeyboardWandererUiText.UndoSkillLabel: return "Undo Skill Label";
                case KeyboardWandererUiText.ConfirmActionLabel: return "Confirm Action Label";
                case KeyboardWandererUiText.EndingHeading: return "Ending Heading";
                case KeyboardWandererUiText.EndingText: return "Ending Text";
                default: throw new ArgumentOutOfRangeException(nameof(id), id, null);
            }
        }

        private static string ButtonObjectName(KeyboardWandererUiButton id)
        {
            switch (id)
            {
                case KeyboardWandererUiButton.NewRun: return "New Run Button";
                case KeyboardWandererUiButton.Continue: return "Continue Button";
                case KeyboardWandererUiButton.Settings: return "Settings Button";
                case KeyboardWandererUiButton.SettingsBack: return "Settings Back Button";
                case KeyboardWandererUiButton.DeleteSave: return "Delete Save Button";
                case KeyboardWandererUiButton.Move: return "Move Button";
                case KeyboardWandererUiButton.Copy: return "Copy Skill Button";
                case KeyboardWandererUiButton.Delete: return "Delete Skill Button";
                case KeyboardWandererUiButton.Connect: return "Connect Skill Button";
                case KeyboardWandererUiButton.Restore: return "Restore Skill Button";
                case KeyboardWandererUiButton.Undo: return "Undo Skill Button";
                case KeyboardWandererUiButton.Search: return "Search Skill Button";
                case KeyboardWandererUiButton.SelectAll: return "Select All Skill Button";
                case KeyboardWandererUiButton.ConfirmAction: return "Confirm Action Button";
                case KeyboardWandererUiButton.NextDialogue: return "Next Dialogue Button";
                case KeyboardWandererUiButton.Resume: return "Resume Button";
                case KeyboardWandererUiButton.PauseSettings: return "Pause Settings Button";
                case KeyboardWandererUiButton.Title: return "Title Button";
                case KeyboardWandererUiButton.EndingNewRun: return "Ending New Run Button";
                case KeyboardWandererUiButton.EndingTitle: return "Ending Title Button";
                default: throw new ArgumentOutOfRangeException(nameof(id), id, null);
            }
        }
#endif

        private static void SetActive(GameObject target, bool active)
        {
            if (target != null && target.activeSelf != active) target.SetActive(active);
        }
    }
}
