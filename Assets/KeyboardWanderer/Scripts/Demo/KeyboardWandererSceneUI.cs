using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace KeyboardWanderer.Demo
{
    [DisallowMultipleComponent]
    public sealed class KeyboardWandererSceneUI : MonoBehaviour
    {
        [SerializeField] private GameObject titleScreen;
        [SerializeField] private GameObject gameHud;
        [SerializeField] private GameObject settingsScreen;
        [SerializeField] private GameObject pauseScreen;
        [SerializeField] private GameObject endingScreen;

        private readonly Dictionary<string, Text> _texts = new Dictionary<string, Text>(StringComparer.Ordinal);
        private readonly Dictionary<string, Button> _buttons = new Dictionary<string, Button>(StringComparer.Ordinal);
        private readonly Dictionary<string, Slider> _sliders = new Dictionary<string, Slider>(StringComparer.Ordinal);
        private readonly Dictionary<string, Toggle> _toggles = new Dictionary<string, Toggle>(StringComparer.Ordinal);
        private readonly Dictionary<string, GameObject> _objects = new Dictionary<string, GameObject>(StringComparer.Ordinal);
        private bool _bound;

        public bool IsReady => titleScreen != null && gameHud != null && settingsScreen != null;

        private void Awake()
        {
            CacheControls();
        }

        private void OnValidate()
        {
            ResolveScreens();
            CacheControls();
        }

        public void Bind(KeyboardWandererDemoController controller)
        {
            if (_bound || controller == null)
                return;
            CacheControls();
            BindButton("New Run Button", controller.UiStartNewRun);
            BindButton("Continue Button", controller.UiContinueRun);
            BindButton("Settings Button", controller.UiOpenSettings);
            BindButton("Settings Back Button", controller.UiCloseSettings);
            BindButton("Delete Save Button", controller.UiDeleteSave);
            BindButton("Move Button", () => controller.UiSetAbility("Move"));
            BindButton("Copy Skill Button", () => controller.UiSetAbility("Copy"));
            BindButton("Delete Skill Button", () => controller.UiSetAbility("Delete"));
            BindButton("Connect Skill Button", () => controller.UiSetAbility("Connect"));
            BindButton("Restore Skill Button", () => controller.UiSetAbility("Restore"));
            BindButton("Undo Skill Button", () => controller.UiSetAbility("Undo"));
            BindButton("Confirm Action Button", controller.UiSubmit);
            BindButton("Next Dialogue Button", controller.UiAdvanceDialogue);
            BindButton("Resume Button", controller.UiResume);
            BindButton("Pause Settings Button", controller.UiOpenSettingsFromPause);
            BindButton("Title Button", controller.UiShowTitle);
            BindButton("Ending New Run Button", controller.UiStartNewRun);
            BindButton("Ending Title Button", controller.UiShowTitle);

            if (_sliders.TryGetValue("Music Slider", out Slider music))
                music.onValueChanged.AddListener(controller.UiSetMusicVolume);
            if (_sliders.TryGetValue("Sfx Slider", out Slider sfx))
                sfx.onValueChanged.AddListener(controller.UiSetSfxVolume);
            if (_toggles.TryGetValue("GM Toggle", out Toggle gm))
                gm.onValueChanged.AddListener(controller.UiSetGmEnabled);
            _bound = true;
        }

        public void Show(string screen, bool paused, bool ended)
        {
            bool title = string.Equals(screen, "Title", StringComparison.Ordinal);
            bool settings = string.Equals(screen, "Settings", StringComparison.Ordinal);
            bool playing = string.Equals(screen, "Playing", StringComparison.Ordinal);
            SetActive(titleScreen, title);
            SetActive(gameHud, playing);
            SetActive(settingsScreen, settings);
            SetActive(pauseScreen, playing && paused);
            SetActive(endingScreen, playing && ended);
        }

        public void SetText(string controlName, string value)
        {
            if (_texts.TryGetValue(controlName, out Text text) && text.text != value)
                text.text = value ?? string.Empty;
        }

        public void SetButtonState(string controlName, bool interactable, bool selected = false)
        {
            if (!_buttons.TryGetValue(controlName, out Button button))
                return;
            button.interactable = interactable;
            ColorBlock colors = button.colors;
            colors.normalColor = selected ? new Color(0.72f, 0.55f, 0.20f, 1f) : new Color(0.28f, 0.20f, 0.13f, 1f);
            button.colors = colors;
        }

        public void SetObjectActive(string controlName, bool active)
        {
            if (_objects.TryGetValue(controlName, out GameObject target) && target.activeSelf != active)
                target.SetActive(active);
        }

        public void SetSlider(string controlName, float value)
        {
            if (_sliders.TryGetValue(controlName, out Slider slider))
                slider.SetValueWithoutNotify(value);
        }

        public void SetToggle(string controlName, bool value)
        {
            if (_toggles.TryGetValue(controlName, out Toggle toggle))
                toggle.SetIsOnWithoutNotify(value);
        }

        public void ApplyFont(Font font)
        {
            if (font == null)
                return;
            CacheControls();
            foreach (Text text in _texts.Values)
                text.font = font;
        }

        private void ResolveScreens()
        {
            if (titleScreen == null) titleScreen = FindDirectChild("Title Screen");
            if (gameHud == null) gameHud = FindDirectChild("Game HUD");
            if (settingsScreen == null) settingsScreen = FindDirectChild("Settings Screen");
            if (pauseScreen == null) pauseScreen = FindDirectChild("Pause Screen");
            if (endingScreen == null) endingScreen = FindDirectChild("Ending Screen");
        }

        private GameObject FindDirectChild(string childName)
        {
            Transform child = transform.Find(childName);
            return child != null ? child.gameObject : null;
        }

        private void CacheControls()
        {
            ResolveScreens();
            _texts.Clear();
            _buttons.Clear();
            _sliders.Clear();
            _toggles.Clear();
            _objects.Clear();
            foreach (Transform item in GetComponentsInChildren<Transform>(true)) _objects[item.gameObject.name] = item.gameObject;
            foreach (Text text in GetComponentsInChildren<Text>(true)) _texts[text.gameObject.name] = text;
            foreach (Button button in GetComponentsInChildren<Button>(true)) _buttons[button.gameObject.name] = button;
            foreach (Slider slider in GetComponentsInChildren<Slider>(true)) _sliders[slider.gameObject.name] = slider;
            foreach (Toggle toggle in GetComponentsInChildren<Toggle>(true)) _toggles[toggle.gameObject.name] = toggle;
        }

        private void BindButton(string controlName, UnityEngine.Events.UnityAction action)
        {
            if (_buttons.TryGetValue(controlName, out Button button))
                button.onClick.AddListener(action);
        }

        private static void SetActive(GameObject target, bool active)
        {
            if (target != null && target.activeSelf != active)
                target.SetActive(active);
        }
    }
}
