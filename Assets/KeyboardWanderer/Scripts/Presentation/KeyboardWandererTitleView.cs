using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace KeyboardWanderer.Demo
{
    /// <summary>Title Screen 오브젝트가 시작 화면 문구와 버튼을 직접 소유한다.</summary>
    [DisallowMultipleComponent]
    public sealed class KeyboardWandererTitleView : MonoBehaviour
    {
        [SerializeField] private TMP_Text heading;
        [SerializeField] private TMP_Text subtitle;
        [SerializeField] private TMP_Text seed;
        [SerializeField] private TMP_Text premise;
        [SerializeField] private TMP_Text status;
        [SerializeField] private Image character;
        [SerializeField] private Button newRunButton;
        [SerializeField] private Button continueButton;
        [SerializeField] private Button settingsButton;

        private bool _bound;
        private bool _initialSelectionEstablished;
        private bool _lastCanContinue;

        public bool IsReady => heading != null && subtitle != null && seed != null && premise != null &&
                               status != null && character != null && newRunButton != null &&
                               continueButton != null && settingsButton != null;

        private void OnEnable()
        {
            _initialSelectionEstablished = false;
        }

        public void Bind(Action onNewRun, Action onContinue, Action onSettings)
        {
            if (_bound || !IsReady)
                return;
            if (onNewRun != null) newRunButton.onClick.AddListener(onNewRun.Invoke);
            if (onContinue != null) continueButton.onClick.AddListener(onContinue.Invoke);
            if (onSettings != null) settingsButton.onClick.AddListener(onSettings.Invoke);
            _bound = true;
        }

        public void Present(string title, string description, string nextSeed, string introduction,
            string connectionStatus, Sprite characterSprite, bool canStart, bool canContinue)
        {
            if (!IsReady)
                return;
            SetText(heading, title);
            SetText(subtitle, description);
            SetText(seed, nextSeed);
            SetText(premise, introduction);
            SetText(status, connectionStatus);
            if (characterSprite != null && character.sprite != characterSprite)
                character.sprite = characterSprite;
            newRunButton.interactable = canStart;
            continueButton.interactable = canContinue;
            ConfigureKeyboardNavigation(canContinue);
            EnsureInitialSelection(canContinue);
        }

        private void ConfigureKeyboardNavigation(bool canContinue)
        {
            Selectable afterNew = canContinue ? continueButton : settingsButton;
            Selectable beforeSettings = canContinue ? continueButton : newRunButton;
            newRunButton.navigation = ExplicitNavigation(settingsButton, afterNew);
            continueButton.navigation = ExplicitNavigation(newRunButton, settingsButton);
            settingsButton.navigation = ExplicitNavigation(beforeSettings, newRunButton);
        }

        private void EnsureInitialSelection(bool canContinue)
        {
            EventSystem eventSystem = EventSystem.current;
            if (eventSystem == null || !gameObject.activeInHierarchy)
                return;

            GameObject selected = eventSystem.currentSelectedGameObject;
            bool selectionBelongsToTitle = selected != null &&
                                           (selected == gameObject || selected.transform.IsChildOf(transform));
            Selectable selectedControl = selectionBelongsToTitle ? selected.GetComponentInParent<Selectable>() : null;
            bool continueJustBecameAvailable = canContinue && !_lastCanContinue;
            _lastCanContinue = canContinue;
            if (_initialSelectionEstablished && !continueJustBecameAvailable &&
                selectedControl != null && selectedControl.IsInteractable())
                return;

            Button preferred = canContinue ? continueButton : newRunButton;
            if (!preferred.IsInteractable())
                preferred = settingsButton;
            // InputSystemUIInputModule can apply EventSystem.firstSelectedGameObject on
            // its first update after this view has already presented. Keep that late
            // initialization from restoring the authored New Run default over Continue.
            eventSystem.firstSelectedGameObject = preferred.gameObject;
            eventSystem.SetSelectedGameObject(preferred.gameObject);
            preferred.Select();
            _initialSelectionEstablished = true;
        }

        private static Navigation ExplicitNavigation(Selectable up, Selectable down)
        {
            return new Navigation
            {
                mode = Navigation.Mode.Explicit,
                selectOnUp = up,
                selectOnDown = down,
                selectOnLeft = up,
                selectOnRight = down
            };
        }

#if UNITY_EDITOR
        public void Configure(TMP_Text title, TMP_Text description, TMP_Text nextSeed, TMP_Text introduction,
            TMP_Text connectionStatus, Image characterImage, Button newRun, Button continueRun, Button settings)
        {
            heading = title;
            subtitle = description;
            seed = nextSeed;
            premise = introduction;
            status = connectionStatus;
            character = characterImage;
            newRunButton = newRun;
            continueButton = continueRun;
            settingsButton = settings;
            UnityEditor.EditorUtility.SetDirty(this);
        }
#endif

        private static void SetText(TMP_Text target, string value)
        {
            value ??= string.Empty;
            if (target.text != value)
                target.text = value;
        }
    }
}
