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
        EndingText,
        CurrentObjective,
        SelectionHeading,
        SelectionDetail
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
            public GameObject SelectedIndicator;
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
        [SerializeField] private Image selectedSkillIcon;
        [SerializeField] private Image selectedTargetIcon;
        [SerializeField] private Image minimapMap;
        [SerializeField] private Text minimapPlaceholder;
        [SerializeField] private Text minimapStatus;

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
        private readonly Dictionary<KeyboardWandererUiButton, bool> _buttonAvailable =
            new Dictionary<KeyboardWandererUiButton, bool>();
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

        public void SetButtonState(KeyboardWandererUiButton id, bool interactable, bool selected = false,
            bool available = true)
        {
            if (!_buttons.TryGetValue(id, out Button button) || button == null)
                return;
            if (_buttonInteractable.TryGetValue(id, out bool previousInteractable) &&
                _buttonSelected.TryGetValue(id, out bool previousSelected) &&
                _buttonAvailable.TryGetValue(id, out bool previousAvailable) &&
                previousInteractable == interactable && previousSelected == selected &&
                previousAvailable == available)
                return;
            _buttonInteractable[id] = interactable;
            _buttonSelected[id] = selected;
            _buttonAvailable[id] = available;
            button.interactable = interactable;
            ColorBlock colors = _authoredButtonColors.TryGetValue(id, out ColorBlock authored)
                ? authored
                : button.colors;
            colors.fadeDuration = 0.055f;
            colors.highlightedColor = new Color(1f, 0.84f, 0.42f, 1f);
            colors.pressedColor = new Color(0.94f, 0.49f, 0.16f, 1f);
            colors.selectedColor = new Color(1f, 0.75f, 0.25f, 1f);
            colors.disabledColor = new Color(0.38f, 0.35f, 0.32f, 0.42f);
            if (!available && interactable)
                colors.normalColor = new Color(0.34f, 0.32f, 0.29f, 0.82f);
            if (selected)
            {
                colors.normalColor = available
                    ? new Color(1f, 0.72f, 0.2f, 1f)
                    : new Color(0.68f, 0.47f, 0.18f, 1f);
                colors.highlightedColor = new Color(1f, 0.9f, 0.55f, 1f);
                colors.pressedColor = new Color(0.86f, 0.38f, 0.12f, 1f);
            }
            button.colors = colors;
            for (int i = 0; i < buttonBindings.Length; i++)
                if (buttonBindings[i].Id == id)
                    SetActive(buttonBindings[i].SelectedIndicator, selected);
        }

        public void SetMinimap(Sprite sprite, string status)
        {
            if (minimapMap != null && minimapMap.sprite != sprite)
                minimapMap.sprite = sprite;
            if (minimapMap != null)
                minimapMap.enabled = sprite != null;
            if (minimapPlaceholder != null)
                minimapPlaceholder.gameObject.SetActive(sprite == null);
            if (minimapStatus != null && minimapStatus.text != status)
                minimapStatus.text = status ?? string.Empty;
        }

        public void SetSelectionVisual(AbilityKind ability, Sprite targetSprite, bool available)
        {
            if (selectedSkillIcon != null && assetManifest != null)
            {
                selectedSkillIcon.sprite = SkillIcon(ability);
                selectedSkillIcon.enabled = selectedSkillIcon.sprite != null;
                selectedSkillIcon.color = available ? Color.white : new Color(0.45f, 0.45f, 0.45f, 0.72f);
            }
            if (selectedTargetIcon != null)
            {
                selectedTargetIcon.sprite = targetSprite;
                selectedTargetIcon.enabled = targetSprite != null;
                selectedTargetIcon.color = targetSprite != null ? Color.white : Color.clear;
            }
        }

        private Sprite SkillIcon(AbilityKind ability)
        {
            switch (ability)
            {
                case AbilityKind.Copy: return assetManifest.CopyIcon;
                case AbilityKind.Delete: return assetManifest.DeleteIcon;
                case AbilityKind.Connect: return assetManifest.ConnectIcon;
                case AbilityKind.Restore: return assetManifest.RestoreIcon;
                case AbilityKind.Undo: return assetManifest.UndoIcon;
                case AbilityKind.Search: return assetManifest.SearchIcon;
                case AbilityKind.SelectAll: return assetManifest.SelectAllIcon;
                case AbilityKind.Move: return assetManifest.MoveIcon;
                default: return assetManifest.InteractIcon;
            }
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
            _buttonAvailable.Clear();
            for (int i = 0; i < textBindings.Length; i++)
            {
                if (textBindings[i].Target != null) _texts[textBindings[i].Id] = textBindings[i].Target;
            }
            for (int i = 0; i < buttonBindings.Length; i++)
            {
                if (buttonBindings[i].Target == null) continue;
                _buttons[buttonBindings[i].Id] = buttonBindings[i].Target;
                _authoredButtonColors[buttonBindings[i].Id] = buttonBindings[i].Target.colors;
                GameObject indicator = buttonBindings[i].SelectedIndicator;
                if (indicator != null && assetManifest != null && assetManifest.WoodPanelFocus != null)
                {
                    Image frame = indicator.GetComponent<Image>();
                    if (frame != null)
                    {
                        frame.sprite = assetManifest.WoodPanelFocus;
                        frame.color = Color.white;
                        frame.type = Image.Type.Sliced;
                        frame.preserveAspect = false;
                    }
                    Outline legacyOutline = indicator.GetComponent<Outline>();
                    if (legacyOutline != null) legacyOutline.enabled = false;
                }
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
            selectedSkillIcon = FindComponent<Image>("Selected Skill Icon");
            selectedTargetIcon = FindComponent<Image>("Selected Target Icon");
            minimapMap = FindComponent<Image>("Authored Minimap");
            minimapPlaceholder = FindComponent<Text>("Minimap Placeholder");
            minimapStatus = FindComponent<Text>("Minimap Status");
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
                Button button = FindComponent<Button>(ButtonObjectName(id));
                Transform indicator = button != null ? button.transform.Find("Selection Frame") : null;
                result[i] = new ButtonBinding
                {
                    Id = id,
                    Target = button,
                    SelectedIndicator = indicator != null ? indicator.gameObject : null
                };
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
                case KeyboardWandererUiText.CurrentObjective: return "Objective Text";
                case KeyboardWandererUiText.SelectionHeading: return "Selection Heading";
                case KeyboardWandererUiText.SelectionDetail: return "Selection Detail";
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
