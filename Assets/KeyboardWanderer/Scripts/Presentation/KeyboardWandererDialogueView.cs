using System;
using Game.Client.UI;
using TMPro;
using KeyboardWanderer.Presentation;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Events;
using UnityEngine.UI;

namespace KeyboardWanderer.Demo
{
    /// <summary>
    /// 대화 패널 오브젝트가 소유하는 화면 컴포넌트입니다.
    /// 대화의 진행 규칙은 <see cref="DialoguePresenter"/>가 관리하고,
    /// 이 컴포넌트는 Inspector에 연결된 텍스트와 버튼의 표시만 담당합니다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class KeyboardWandererDialogueView : MonoBehaviour
    {
        [SerializeField] private TMP_Text speakerText;
        [SerializeField] private TMP_Text storyText;
        [SerializeField] private TMP_Text nextLabel;
        [SerializeField] private Button nextButton;
        [SerializeField] private Image speakerPortrait;
        [SerializeField] private GameObject speakerPortraitFrame;
        [SerializeField] private GameObject encounterSubjectStage;
        [SerializeField] private Image encounterSubject;

        private GameObject _choiceStrip;
        private readonly Button[] _choiceButtons = new Button[5];
        private readonly TMP_Text[] _choiceLabels = new TMP_Text[5];
        private readonly NarrativeChoiceOption[] _choiceOptions = new NarrativeChoiceOption[5];
        private Action<string> _chooseNarrativeChoice;
        private Action<string> _submitPlayerMessage;
        private TMP_InputField _freeformInput;
        private TMP_Text _freeformCharacterCount;
        private TMP_Text _freeformSubmitLabel;
        private Button _freeformSubmit;
        private GameObject _freeformRow;
        private ScrollRect _choiceScroll;
        private RectTransform _choiceViewport;
        private RectTransform _choiceContent;
        private string _choiceSignature = string.Empty;
        private string _choicePresentationSignature = string.Empty;
        private bool _choiceInputLocked;
        private bool _freeformHasSelection;
        private int _keyboardChoiceIndex;
        private bool _hasChoicePresentationState;
        private bool _presentedChoicesVisible;
        private bool _presentedChoicesInteractable;
        private bool _presentedChoiceInputLocked;
        private bool _presentedFreeformFocused;
        private float _presentedChoiceViewportWidth = -1f;
        private const int FreeformCharacterLimit = 1000;
        private static readonly Color SelectedChoiceColor = new Color(0.88f, 0.68f, 0.31f, 0.96f);
        private static readonly Color DefaultChoiceColor = new Color(0.22f, 0.16f, 0.10f, 0.96f);
        private static readonly Color DisabledChoiceColor = new Color(0.10f, 0.085f, 0.07f, 0.78f);

        [Header("화자 컷인(선택)")]
        [SerializeField] private Image speakerCutinBackdrop;

        private bool _bound;

        public bool IsReady => speakerText != null && storyText != null && nextLabel != null && nextButton != null;
        public TMP_Text SpeakerText => speakerText;
        public TMP_Text StoryText => storyText;
        public TMP_Text NextLabel => nextLabel;
        public Button NextButton => nextButton;
        internal int ChoiceLayoutRevision { get; private set; }
        public bool IsFreeformFocused
        {
            get
            {
                if (_freeformInput == null) return false;
                if (_freeformInput.gameObject.activeInHierarchy &&
                    InputFocusTracker.OwnsTextInputThisFrame)
                    return true;
                // 입력 필드가 실제로 포커스되어 있거나, 선택(select) 상태이거나,
                // EventSystem의 현재 선택 대상이 이 입력 필드일 때만 true를 반환합니다.
                // _freeformRow 활성 여부만으로 판정하면 다른 버튼 클릭까지 차단됩니다.
                if (_freeformHasSelection || _freeformInput.isFocused) return true;

                GameObject selected = EventSystem.current?.currentSelectedGameObject;
                TMP_InputField selectedInput = selected != null
                    ? selected.GetComponentInParent<TMP_InputField>()
                    : null;
                return selectedInput == _freeformInput;
            }
        }

        public void Bind(UnityAction advance, Action<string> chooseNarrativeChoice = null,
            Action<string> submitPlayerMessage = null)
        {
            if (_bound || advance == null || nextButton == null)
                return;
            nextButton.onClick.AddListener(advance);
            _chooseNarrativeChoice = chooseNarrativeChoice;
            _submitPlayerMessage = submitPlayerMessage;
            ResolveChoiceReferences();
            ResolveFreeformReferences();
            for (int i = 0; i < _choiceButtons.Length; i++)
            {
                int index = i;
                if (_choiceButtons[i] != null)
                    _choiceButtons[i].onClick.AddListener(() => Choose(index));
            }
            _bound = true;
        }

        public void PresentChoices(bool visible, NarrativeChoiceOption[] choices, bool interactable)
        {
            ResolveChoiceReferences();
            ResolveFreeformReferences();
            if (_choiceStrip != null && _choiceStrip.activeSelf != visible)
                _choiceStrip.SetActive(visible);
            choices ??= Array.Empty<NarrativeChoiceOption>();
            string signature = ChoiceSignature(choices);
            if (!string.Equals(_choiceSignature, signature, StringComparison.Ordinal))
            {
                _choiceSignature = signature;
                _choiceInputLocked = false;
                _keyboardChoiceIndex = 0;
            }
            if (!visible)
                _choiceInputLocked = false;
            if (_freeformRow != null && _freeformRow.activeSelf != visible)
                _freeformRow.SetActive(visible);
            if (_freeformInput != null)
            {
                bool inputInteractable = visible && interactable && !_choiceInputLocked;
                if (_freeformInput.interactable != inputInteractable)
                    _freeformInput.interactable = inputInteractable;
            }
            if (nextButton != null && !nextButton.gameObject.activeSelf)
                nextButton.gameObject.SetActive(true);
            if (_freeformSubmit != null)
            {
                if (_freeformSubmit.gameObject.activeSelf != visible)
                    _freeformSubmit.gameObject.SetActive(visible);
                bool submitInteractable = visible && interactable && !_choiceInputLocked;
                if (_freeformSubmit.interactable != submitInteractable)
                    _freeformSubmit.interactable = submitInteractable;
            }
            for (int i = 0; i < _choiceButtons.Length; i++)
            {
                // The authored prefab still contains a fifth legacy slot. Narrative
                // contracts are deliberately bounded to 2-4 choices.
                bool active = visible && i < 4 && i < choices.Length && choices[i] != null;
                _choiceOptions[i] = active ? choices[i] : null;
                if (_choiceButtons[i] != null && _choiceButtons[i].gameObject.activeSelf != active)
                    _choiceButtons[i].gameObject.SetActive(active);
                bool choiceInteractable = active && interactable && !_choiceInputLocked;
                if (_choiceButtons[i] != null && _choiceButtons[i].interactable != choiceInteractable)
                    _choiceButtons[i].interactable = choiceInteractable;
            }

            string presentationSignature = ChoicePresentationSignature(choices);
            bool freeformFocused = IsFreeformFocused;
            float viewportWidth = visible ? ChoiceViewportWidth() : -1f;
            bool presentationUnchanged = _hasChoicePresentationState &&
                                         _presentedChoicesVisible == visible &&
                                         _presentedChoicesInteractable == interactable &&
                                         _presentedChoiceInputLocked == _choiceInputLocked &&
                                         _presentedFreeformFocused == freeformFocused &&
                                         string.Equals(_choicePresentationSignature, presentationSignature,
                                             StringComparison.Ordinal) &&
                                         Mathf.Approximately(_presentedChoiceViewportWidth, viewportWidth);

            // A hidden choice panel has no active layout or navigation target. Keeping
            // its content disabled is sufficient; rebuilding a hidden VerticalLayoutGroup
            // on every unrelated HUD update only dirties the parent Canvas.
            if (!visible)
            {
                RememberChoicePresentation(visible, interactable, freeformFocused,
                    presentationSignature, viewportWidth);
                return;
            }
            if (presentationUnchanged)
                return;

            RefreshChoiceVisuals();
            if (visible && interactable && !_choiceInputLocked && !IsFreeformFocused)
                SelectKeyboardButton();
            RememberChoicePresentation(visible, interactable, IsFreeformFocused,
                presentationSignature, ChoiceViewportWidth());
        }

        public void MoveChoiceSelection(int direction)
        {
            if (_choiceInputLocked || IsFreeformFocused || direction == 0) return;
            int count = ActiveChoiceCount();
            if (count == 0) return;
            _keyboardChoiceIndex = (_keyboardChoiceIndex + (direction < 0 ? -1 : 1) + count) % count;
            RefreshChoiceVisuals();
            SelectKeyboardButton();
        }

        public void ConfirmChoiceSelection()
        {
            if (_choiceInputLocked || IsFreeformFocused) return;
            Choose(_keyboardChoiceIndex);
        }

        public int KeyboardChoiceIndex => _keyboardChoiceIndex;

        private void Choose(int index)
        {
            // EventSystem submit and the authored keyboard router can both observe Return in
            // the same frame. A focused text field owns that key absolutely; a stale selected
            // Button must never turn typed freeform text into a sealed-choice submission.
            if (_choiceInputLocked || IsFreeformFocused || index < 0 || index >= 4 || index >= _choiceOptions.Length ||
                _choiceOptions[index] == null || string.IsNullOrWhiteSpace(_choiceOptions[index].ChoiceId))
                return;
            _choiceInputLocked = true;
            for (int i = 0; i < _choiceButtons.Length; i++)
                if (_choiceButtons[i] != null) _choiceButtons[i].interactable = false;
            RefreshChoiceVisuals();
            _chooseNarrativeChoice?.Invoke(_choiceOptions[index].ChoiceId);
        }

        public void ReleaseChoiceInputLock()
        {
            if (!_choiceInputLocked)
                return;
            ResolveChoiceReferences();
            ResolveFreeformReferences();
            _choiceInputLocked = false;
            SetText(_freeformSubmitLabel, "전송");
            if (_freeformInput != null && _freeformInput.gameObject.activeInHierarchy)
                _freeformInput.interactable = true;
            if (_freeformSubmit != null && _freeformSubmit.gameObject.activeInHierarchy)
                _freeformSubmit.interactable = true;
            for (int i = 0; i < _choiceButtons.Length; i++)
            {
                if (_choiceButtons[i] == null) continue;
                _choiceButtons[i].interactable = i < _choiceOptions.Length &&
                                                  _choiceOptions[i] != null &&
                                                  !string.IsNullOrWhiteSpace(_choiceOptions[i].ChoiceId);
            }
            // SubmitFreeform/Choose update the live Button state without going through
            // PresentChoices, so the remembered presentation still describes the
            // pre-submit (interactive) frame. Force the next authoritative presentation
            // to repaint the selected/default colors as well as restoring interactability.
            _hasChoicePresentationState = false;
        }

        /// <summary>
        /// Clears a free-form draft only after the authoritative response has been
        /// accepted. Transport failures call <see cref="ReleaseChoiceInputLock"/> instead,
        /// leaving the exact player-authored text available for a same-request retry.
        /// </summary>
        public void CompleteFreeformSubmission()
        {
            ResolveFreeformReferences();
            _choiceInputLocked = false;
            _freeformHasSelection = false;
            SetText(_freeformSubmitLabel, "전송");
            if (_freeformInput == null)
                return;

            GameObject selected = EventSystem.current?.currentSelectedGameObject;
            if (selected != null && (selected == _freeformInput.gameObject ||
                                     selected.transform.IsChildOf(_freeformInput.transform)))
                EventSystem.current.SetSelectedGameObject(null);
            _freeformInput.DeactivateInputField();
            _freeformInput.text = string.Empty;
        }

        /// <summary>
        /// Clears every piece of transient input state at a run/session boundary.
        /// Hiding the strip alone is insufficient because TMP text, EventSystem focus and
        /// sealed option references otherwise survive and reappear on the next prompt.
        /// </summary>
        public void ResetChoiceSession()
        {
            ResolveChoiceReferences();
            ResolveFreeformReferences();
            GameObject selected = EventSystem.current?.currentSelectedGameObject;
            if (selected != null && _choiceStrip != null &&
                (selected == _choiceStrip || selected.transform.IsChildOf(_choiceStrip.transform)))
                EventSystem.current.SetSelectedGameObject(null);
            _freeformHasSelection = false;
            SetText(_freeformSubmitLabel, "전송");
            if (_freeformInput != null)
            {
                _freeformInput.DeactivateInputField();
                _freeformInput.text = string.Empty;
                _freeformInput.interactable = false;
            }
            if (_freeformSubmit != null)
                _freeformSubmit.interactable = false;
            for (int i = 0; i < _choiceOptions.Length; i++)
                _choiceOptions[i] = null;
            _choiceSignature = string.Empty;
            _choiceInputLocked = false;
            _keyboardChoiceIndex = 0;
            PresentChoices(false, Array.Empty<NarrativeChoiceOption>(), false);
        }

        public void FocusFreeformInput()
        {
            ResolveChoiceReferences();
            ResolveFreeformReferences();
            if (_freeformInput == null || !_freeformInput.gameObject.activeInHierarchy) return;
            EventSystem.current?.SetSelectedGameObject(_freeformInput.gameObject);
            _freeformInput.Select();
            _freeformInput.ActivateInputField();
        }

        private void SubmitFreeform()
        {
            if (_choiceInputLocked || _freeformInput == null) return;
            string text = _freeformInput.text?.Trim();
            if (string.IsNullOrWhiteSpace(text)) return;
            _choiceInputLocked = true;
            _freeformInput.interactable = false;
            if (_freeformSubmit != null) _freeformSubmit.interactable = false;
            SetText(_freeformSubmitLabel, "처리 중…");
            for (int i = 0; i < _choiceButtons.Length; i++)
                if (_choiceButtons[i] != null) _choiceButtons[i].interactable = false;
            RefreshChoiceVisuals();
            _submitPlayerMessage?.Invoke(text);
        }

        private void ResolveFreeformReferences()
        {
            if (_choiceStrip == null || _freeformInput != null) return;
            // 긴 장면 문장이 본문 Rect를 벗어나 입력창 위에 그려지지 않도록 한다.
            // 전체 내용은 장면 페이지 단위로 넘기고, 한 페이지의 물리 경계는 UI가 지킨다.
            if (storyText != null)
            {
                storyText.overflowMode = TextOverflowModes.Ellipsis;
                storyText.enableAutoSizing = true;
                storyText.fontSizeMin = 10f;
                storyText.textWrappingMode = TextWrappingModes.Normal;
            }
            _freeformRow = new GameObject("Freeform Input", typeof(RectTransform), typeof(Image));
            // 선택지와 자유 입력은 같은 확장 패널의 고정 영역을 사용한다. 본문 패널 안에
            // 입력창을 겹쳐 놓으면 작은 해상도에서 스토리의 마지막 두 줄과 클릭 영역이
            // 충돌하므로, 선택지 네 줄 아래에 독립된 44px+ 입력 행을 예약한다.
            _freeformRow.transform.SetParent(_choiceStrip.transform, false);
            RectTransform rowRect = (RectTransform)_freeformRow.transform;
            rowRect.anchorMin = new Vector2(0.025f, 0.02f);
            rowRect.anchorMax = new Vector2(0.975f, 0.23f);
            rowRect.offsetMin = rowRect.offsetMax = Vector2.zero;
            var rowImage = _freeformRow.GetComponent<Image>();
            rowImage.color = new Color(0.12f, 0.13f, 0.16f, 0.98f);
            rowImage.raycastTarget = false;
            _freeformRow.transform.SetAsLastSibling();

            var inputObject = new GameObject("Input", typeof(RectTransform), typeof(Image), typeof(TMP_InputField));
            inputObject.transform.SetParent(_freeformRow.transform, false);
            RectTransform inputRect = (RectTransform)inputObject.transform;
            inputRect.anchorMin = new Vector2(0f, 0f);
            inputRect.anchorMax = new Vector2(0.82f, 1f);
            inputRect.offsetMin = new Vector2(6f, 4f);
            inputRect.offsetMax = new Vector2(-4f, -4f);
            Image inputBackground = inputObject.GetComponent<Image>();
            inputBackground.color = new Color(0f, 0f, 0f, 0.25f);

            var viewportObject = new GameObject("Text Viewport", typeof(RectTransform), typeof(RectMask2D));
            viewportObject.transform.SetParent(inputObject.transform, false);
            RectTransform textViewport = (RectTransform)viewportObject.transform;
            textViewport.anchorMin = new Vector2(0f, 0.23f);
            textViewport.anchorMax = new Vector2(0.95f, 1f);
            textViewport.offsetMin = new Vector2(5f, 2f);
            textViewport.offsetMax = new Vector2(-2f, -3f);

            TMP_Text textComponent = CreateFreeformText(textViewport, "Text", string.Empty,
                new Color(0.96f, 0.96f, 0.92f, 1f));
            TMP_Text placeholder = CreateFreeformText(textViewport, "Placeholder",
                "하고 싶은 말이나 행동을 직접 입력하세요…",
                new Color(0.72f, 0.74f, 0.76f, 0.75f));
            Scrollbar scrollbar = CreateFreeformScrollbar(inputObject.transform);
            _freeformCharacterCount = CreateInputText(inputObject.transform, "Character Count",
                "0/1000자", new Color(0.68f, 0.72f, 0.76f, 0.92f));
            RectTransform countRect = (RectTransform)_freeformCharacterCount.transform;
            countRect.anchorMin = new Vector2(0.02f, 0.01f);
            countRect.anchorMax = new Vector2(0.94f, 0.23f);
            countRect.offsetMin = countRect.offsetMax = Vector2.zero;
            _freeformCharacterCount.alignment = TextAlignmentOptions.MidlineRight;
            _freeformCharacterCount.fontSize = 11f;
            _freeformCharacterCount.enableAutoSizing = true;
            _freeformCharacterCount.fontSizeMin = 9f;
            _freeformCharacterCount.fontSizeMax = 11f;
            _freeformCharacterCount.raycastTarget = false;
            _freeformInput = inputObject.GetComponent<TMP_InputField>();
            inputObject.AddComponent<InputFocusTracker>();
            _freeformInput.navigation = new Navigation { mode = Navigation.Mode.None };
            _freeformInput.targetGraphic = inputBackground;
            _freeformInput.transition = Selectable.Transition.ColorTint;
            ColorBlock inputColors = _freeformInput.colors;
            inputColors.normalColor = Color.white;
            inputColors.highlightedColor = new Color(1f, 0.94f, 0.72f, 1f);
            inputColors.selectedColor = new Color(1f, 0.86f, 0.48f, 1f);
            inputColors.pressedColor = new Color(1f, 0.80f, 0.34f, 1f);
            inputColors.disabledColor = new Color(0.55f, 0.55f, 0.55f, 0.62f);
            inputColors.colorMultiplier = 1f;
            _freeformInput.colors = inputColors;
            _freeformInput.textViewport = textViewport;
            _freeformInput.textComponent = textComponent;
            _freeformInput.placeholder = placeholder;
            _freeformInput.verticalScrollbar = scrollbar;
            _freeformInput.scrollSensitivity = 1f;
            // MultiLineSubmit keeps Return as the explicit submit gesture while long
            // Korean drafts wrap across several visible lines. The viewport and
            // scrollbar still let the player review the complete 1000-character draft.
            _freeformInput.lineType = TMP_InputField.LineType.MultiLineSubmit;
            _freeformInput.characterLimit = FreeformCharacterLimit;
            _freeformInput.onValueChanged.AddListener(UpdateFreeformCharacterCount);
            _freeformInput.onSelect.AddListener(_ => _freeformHasSelection = true);
            _freeformInput.onDeselect.AddListener(_ => _freeformHasSelection = false);
            _freeformInput.onSubmit.AddListener(_ => SubmitFreeform());
            UpdateFreeformCharacterCount(string.Empty);

            var buttonObject = new GameObject("Send", typeof(RectTransform), typeof(Image), typeof(Button));
            buttonObject.transform.SetParent(_freeformRow.transform, false);
            RectTransform buttonRect = (RectTransform)buttonObject.transform;
            buttonRect.anchorMin = new Vector2(0.83f, 0f);
            buttonRect.anchorMax = new Vector2(1f, 1f);
            buttonRect.offsetMin = new Vector2(2f, 4f);
            buttonRect.offsetMax = new Vector2(-6f, -4f);
            buttonObject.GetComponent<Image>().color = SelectedChoiceColor;
            _freeformSubmit = buttonObject.GetComponent<Button>();
            _freeformSubmit.navigation = new Navigation { mode = Navigation.Mode.None };
            _freeformSubmit.onClick.AddListener(SubmitFreeform);
            _freeformSubmitLabel = CreateInputText(buttonObject.transform, "Label", "전송",
                new Color(0.12f, 0.09f, 0.06f, 1f));
            _freeformSubmitLabel.alignment = TextAlignmentOptions.Center;
        }

        public bool InsertItemReference(string itemName)
        {
            ResolveChoiceReferences();
            ResolveFreeformReferences();
            if (_freeformInput == null || !_freeformInput.gameObject.activeInHierarchy ||
                !_freeformInput.interactable || string.IsNullOrWhiteSpace(itemName))
                return false;
            string prefix = string.IsNullOrWhiteSpace(_freeformInput.text)
                ? string.Empty
                : _freeformInput.text.TrimEnd() + " ";
            _freeformInput.text = prefix + itemName.Trim();
            _freeformInput.caretPosition = _freeformInput.text.Length;
            EventSystem.current?.SetSelectedGameObject(_freeformInput.gameObject);
            _freeformInput.Select();
            _freeformInput.ActivateInputField();
            return true;
        }

        private TMP_Text CreateInputText(Transform parent, string name, string value, Color color)
        {
            var item = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            item.transform.SetParent(parent, false);
            RectTransform rect = (RectTransform)item.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(12f, 2f);
            rect.offsetMax = new Vector2(-8f, -2f);
            TMP_Text label = item.GetComponent<TMP_Text>();
            ApplyDialogueFont(label);
            label.text = value;
            label.color = color;
            label.fontSize = 15f;
            label.alignment = TextAlignmentOptions.MidlineLeft;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            return label;
        }

        private TMP_Text CreateFreeformText(RectTransform parent, string name, string value, Color color)
        {
            TMP_Text label = CreateInputText(parent, name, value, color);
            RectTransform rect = (RectTransform)label.transform;
            rect.offsetMin = new Vector2(7f, 2f);
            rect.offsetMax = new Vector2(-5f, -2f);
            label.alignment = TextAlignmentOptions.TopLeft;
            label.textWrappingMode = TextWrappingModes.Normal;
            label.overflowMode = TextOverflowModes.Overflow;
            label.raycastTarget = false;
            return label;
        }

        private static Scrollbar CreateFreeformScrollbar(Transform parent)
        {
            var scrollbarObject = new GameObject("Vertical Scrollbar", typeof(RectTransform),
                typeof(Image), typeof(Scrollbar));
            scrollbarObject.transform.SetParent(parent, false);
            RectTransform scrollbarRect = (RectTransform)scrollbarObject.transform;
            scrollbarRect.anchorMin = new Vector2(0.955f, 0.27f);
            scrollbarRect.anchorMax = new Vector2(0.995f, 0.96f);
            scrollbarRect.offsetMin = scrollbarRect.offsetMax = Vector2.zero;
            Image track = scrollbarObject.GetComponent<Image>();
            track.color = new Color(0.05f, 0.055f, 0.065f, 0.92f);

            var slidingObject = new GameObject("Sliding Area", typeof(RectTransform));
            slidingObject.transform.SetParent(scrollbarObject.transform, false);
            RectTransform slidingRect = (RectTransform)slidingObject.transform;
            slidingRect.anchorMin = Vector2.zero;
            slidingRect.anchorMax = Vector2.one;
            slidingRect.offsetMin = new Vector2(2f, 2f);
            slidingRect.offsetMax = new Vector2(-2f, -2f);

            var handleObject = new GameObject("Handle", typeof(RectTransform), typeof(Image));
            handleObject.transform.SetParent(slidingObject.transform, false);
            RectTransform handleRect = (RectTransform)handleObject.transform;
            handleRect.anchorMin = Vector2.zero;
            handleRect.anchorMax = Vector2.one;
            handleRect.offsetMin = handleRect.offsetMax = Vector2.zero;
            Image handle = handleObject.GetComponent<Image>();
            handle.color = new Color(0.72f, 0.58f, 0.32f, 0.95f);

            Scrollbar scrollbar = scrollbarObject.GetComponent<Scrollbar>();
            scrollbar.handleRect = handleRect;
            scrollbar.targetGraphic = handle;
            scrollbar.direction = Scrollbar.Direction.BottomToTop;
            scrollbar.navigation = new Navigation { mode = Navigation.Mode.None };
            scrollbar.value = 0f;
            scrollbar.size = 1f;
            return scrollbar;
        }

        private void UpdateFreeformCharacterCount(string value)
        {
            if (_freeformCharacterCount == null)
                return;
            int length = value?.Length ?? 0;
            _freeformCharacterCount.text = Mathf.Clamp(length, 0, FreeformCharacterLimit) +
                                           "/" + FreeformCharacterLimit + "자";
            _freeformCharacterCount.color = length >= FreeformCharacterLimit
                ? new Color(1f, 0.58f, 0.36f, 1f)
                : new Color(0.68f, 0.72f, 0.76f, 0.92f);
        }

        private int ActiveChoiceCount()
        {
            int count = 0;
            for (int i = 0; i < 4 && i < _choiceOptions.Length; i++)
                if (_choiceOptions[i] != null) count++;
            return count;
        }

        private string ChoiceLabel(int index)
            => (_keyboardChoiceIndex == index ? "▶ " : "  ") + (index + 1) + ".  " + (_choiceOptions[index]?.Text ?? string.Empty);

        private void RefreshChoiceVisuals()
        {
            for (int i = 0; i < 4 && i < _choiceOptions.Length; i++)
            {
                if (_choiceOptions[i] == null) continue;
                SetText(_choiceLabels[i], ChoiceLabel(i));
                Image background = _choiceButtons[i] != null ? _choiceButtons[i].targetGraphic as Image : null;
                if (background == null && _choiceButtons[i] != null)
                    background = _choiceButtons[i].GetComponent<Image>();
                if (background != null)
                    background.color = _choiceButtons[i] != null && !_choiceButtons[i].interactable
                        ? DisabledChoiceColor
                        : i == _keyboardChoiceIndex ? SelectedChoiceColor : DefaultChoiceColor;
                if (_choiceLabels[i] != null)
                    _choiceLabels[i].color = _choiceButtons[i] != null && !_choiceButtons[i].interactable
                        ? new Color(0.62f, 0.60f, 0.56f, 0.82f)
                        : Color.white;
            }
            RefreshChoiceLayout();
        }

        private void SelectKeyboardButton()
        {
            int count = ActiveChoiceCount();
            if (count == 0) return;
            _keyboardChoiceIndex = Mathf.Clamp(_keyboardChoiceIndex, 0, count - 1);
            Button button = _choiceButtons[_keyboardChoiceIndex];
            if (button != null && button.interactable)
            {
                button.Select();
                ScrollChoiceIntoView(_keyboardChoiceIndex);
            }
        }

        private static string ChoiceSignature(NarrativeChoiceOption[] choices)
        {
            if (choices == null || choices.Length == 0) return string.Empty;
            var values = new string[Math.Min(4, choices.Length)];
            for (int i = 0; i < values.Length; i++)
                values[i] = choices[i]?.ChoiceId ?? string.Empty;
            return string.Join("\u001f", values);
        }

        private static string ChoicePresentationSignature(NarrativeChoiceOption[] choices)
        {
            if (choices == null || choices.Length == 0)
                return string.Empty;
            var values = new string[Math.Min(4, choices.Length)];
            for (int i = 0; i < values.Length; i++)
            {
                NarrativeChoiceOption choice = choices[i];
                values[i] = choice == null
                    ? string.Empty
                    : (choice.ChoiceId ?? string.Empty) + "\u001e" + (choice.Text ?? string.Empty);
            }
            return string.Join("\u001f", values);
        }

        private void RememberChoicePresentation(bool visible, bool interactable, bool freeformFocused,
            string presentationSignature, float viewportWidth)
        {
            _hasChoicePresentationState = true;
            _presentedChoicesVisible = visible;
            _presentedChoicesInteractable = interactable;
            _presentedChoiceInputLocked = _choiceInputLocked;
            _presentedFreeformFocused = freeformFocused;
            _choicePresentationSignature = presentationSignature ?? string.Empty;
            _presentedChoiceViewportWidth = viewportWidth;
        }

        private void ResolveChoiceReferences()
        {
            if (_choiceStrip == null)
                _choiceStrip = transform.Find("Choice Strip")?.gameObject;
            if (_choiceStrip == null)
                _choiceStrip = CreateChoiceStrip();
            if (_choiceStrip == null) return;
            if (_choiceStrip.transform is RectTransform stripRect)
            {
                stripRect.anchorMin = new Vector2(0.02f, 1.08f);
                // Four short server-sealed choices are all primary actions and must
                // be visible without guessing that a scrollbar exists. Long localized
                // choices still use the ScrollRect below, but the common 2-4 option
                // combat case now exposes every action at first glance.
                stripRect.anchorMax = new Vector2(0.98f, 3.85f);
                stripRect.offsetMin = Vector2.zero;
                stripRect.offsetMax = Vector2.zero;
            }
            for (int i = 0; i < _choiceButtons.Length; i++)
            {
                Transform choice = FindDescendant(_choiceStrip.transform, "Choice " + (i + 1));
                if (_choiceButtons[i] == null) _choiceButtons[i] = choice?.GetComponent<Button>();
                if (_choiceLabels[i] == null) _choiceLabels[i] = choice?.GetComponentInChildren<TMP_Text>(true);
                // Keyboard selection owns the choice-bar color. Unity's default
                // ColorTint transition otherwise restores the previously selected
                // button's authored tint after Button.Select(), making a downward
                // move look as if the highlight moved upward.
                if (_choiceButtons[i] != null && _choiceButtons[i].transition != Selectable.Transition.None)
                    _choiceButtons[i].transition = Selectable.Transition.None;
                if (_choiceButtons[i] != null)
                    _choiceButtons[i].navigation = new Navigation { mode = Navigation.Mode.None };
                if (_choiceLabels[i] != null && i < 4)
                {
                    ApplyDialogueFont(_choiceLabels[i]);
                    _choiceLabels[i].enableAutoSizing = false;
                    _choiceLabels[i].fontSize = 15f;
                    _choiceLabels[i].alignment = TextAlignmentOptions.MidlineLeft;
                    _choiceLabels[i].margin = new Vector4(14f, 3f, 10f, 3f);
                    _choiceLabels[i].textWrappingMode = TextWrappingModes.Normal;
                    _choiceLabels[i].overflowMode = TextOverflowModes.Overflow;
                }
            }
            EnsureChoiceScrollLayout();
        }

        private void EnsureChoiceScrollLayout()
        {
            if (_choiceStrip == null)
                return;
            if (_choiceScroll == null)
            {
                Transform existing = _choiceStrip.transform.Find("Choice Scroll");
                GameObject scrollObject = existing != null
                    ? existing.gameObject
                    : new GameObject("Choice Scroll", typeof(RectTransform), typeof(ScrollRect));
                if (existing == null)
                    scrollObject.transform.SetParent(_choiceStrip.transform, false);
                RectTransform scrollRect = (RectTransform)scrollObject.transform;
                scrollRect.anchorMin = new Vector2(0.025f, 0.26f);
                scrollRect.anchorMax = new Vector2(0.975f, 0.98f);
                scrollRect.offsetMin = scrollRect.offsetMax = Vector2.zero;
                _choiceScroll = scrollObject.GetComponent<ScrollRect>();

                Transform viewportTransform = scrollObject.transform.Find("Viewport");
                GameObject viewportObject = viewportTransform != null
                    ? viewportTransform.gameObject
                    : new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D));
                if (viewportTransform == null)
                    viewportObject.transform.SetParent(scrollObject.transform, false);
                _choiceViewport = (RectTransform)viewportObject.transform;
                _choiceViewport.anchorMin = Vector2.zero;
                _choiceViewport.anchorMax = Vector2.one;
                _choiceViewport.offsetMin = _choiceViewport.offsetMax = Vector2.zero;

                Transform contentTransform = viewportObject.transform.Find("Content");
                GameObject contentObject = contentTransform != null
                    ? contentTransform.gameObject
                    : new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup),
                        typeof(ContentSizeFitter));
                if (contentTransform == null)
                    contentObject.transform.SetParent(viewportObject.transform, false);
                _choiceContent = (RectTransform)contentObject.transform;
                _choiceContent.anchorMin = new Vector2(0f, 1f);
                _choiceContent.anchorMax = new Vector2(1f, 1f);
                _choiceContent.pivot = new Vector2(0.5f, 1f);
                _choiceContent.anchoredPosition = Vector2.zero;
                _choiceContent.sizeDelta = Vector2.zero;

                VerticalLayoutGroup layout = contentObject.GetComponent<VerticalLayoutGroup>();
                layout.padding = new RectOffset(2, 2, 2, 2);
                layout.spacing = 7f;
                layout.childAlignment = TextAnchor.UpperCenter;
                layout.childControlWidth = true;
                layout.childControlHeight = true;
                layout.childForceExpandWidth = true;
                layout.childForceExpandHeight = false;
                ContentSizeFitter fitter = contentObject.GetComponent<ContentSizeFitter>();
                fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
                fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

                _choiceScroll.viewport = _choiceViewport;
                _choiceScroll.content = _choiceContent;
                _choiceScroll.horizontal = false;
                _choiceScroll.vertical = true;
                _choiceScroll.movementType = ScrollRect.MovementType.Clamped;
                _choiceScroll.inertia = true;
                _choiceScroll.scrollSensitivity = 36f;
            }

            for (int i = 0; i < 4; i++)
            {
                Button button = _choiceButtons[i];
                if (button == null)
                    continue;
                RectTransform rect = button.transform as RectTransform;
                if (rect.parent != _choiceContent)
                    rect.SetParent(_choiceContent, false);
                rect.anchorMin = new Vector2(0f, 1f);
                rect.anchorMax = new Vector2(1f, 1f);
                rect.pivot = new Vector2(0.5f, 1f);
                rect.localScale = Vector3.one;
                LayoutElement element = button.GetComponent<LayoutElement>();
                if (element == null)
                    element = button.gameObject.AddComponent<LayoutElement>();
                element.minHeight = 52f;
                element.flexibleHeight = 0f;
            }
        }

        private void RefreshChoiceLayout()
        {
            if (_choiceContent == null)
                return;
            ChoiceLayoutRevision++;
            float viewportWidth = ChoiceViewportWidth();
            float textWidth = Mathf.Max(120f, viewportWidth - 34f);
            for (int i = 0; i < 4; i++)
            {
                if (_choiceButtons[i] == null || _choiceLabels[i] == null)
                    continue;
                LayoutElement element = _choiceButtons[i].GetComponent<LayoutElement>();
                if (element == null)
                    continue;
                float textHeight = _choiceLabels[i].GetPreferredValues(_choiceLabels[i].text, textWidth, 0f).y;
                float preferred = Mathf.Max(52f, textHeight + 16f);
                if (!Mathf.Approximately(element.preferredHeight, preferred))
                    element.preferredHeight = preferred;
            }
            LayoutRebuilder.MarkLayoutForRebuild(_choiceContent);
        }

        private float ChoiceViewportWidth()
        {
            float viewportWidth = _choiceViewport != null ? _choiceViewport.rect.width : 0f;
            if (viewportWidth <= 1f && _choiceStrip != null &&
                _choiceStrip.transform is RectTransform stripRect)
                viewportWidth = stripRect.rect.width * 0.95f;
            return viewportWidth > 1f ? viewportWidth : 560f;
        }

        private void ScrollChoiceIntoView(int index)
        {
            if (_choiceScroll == null || _choiceViewport == null || _choiceContent == null ||
                index < 0 || index >= 4 || _choiceButtons[index] == null)
                return;
            Canvas.ForceUpdateCanvases();
            Bounds viewportBounds = new Bounds(_choiceViewport.rect.center, _choiceViewport.rect.size);
            Bounds itemBounds = RectTransformUtility.CalculateRelativeRectTransformBounds(
                _choiceViewport, _choiceButtons[index].transform);
            Vector2 position = _choiceContent.anchoredPosition;
            if (itemBounds.min.y < viewportBounds.min.y)
                position.y += viewportBounds.min.y - itemBounds.min.y;
            else if (itemBounds.max.y > viewportBounds.max.y)
                position.y -= itemBounds.max.y - viewportBounds.max.y;
            float maxY = Mathf.Max(0f, _choiceContent.rect.height - _choiceViewport.rect.height);
            position.y = Mathf.Clamp(position.y, 0f, maxY);
            _choiceContent.anchoredPosition = position;
        }

        private static Transform FindDescendant(Transform root, string name)
        {
            if (root == null)
                return null;
            Transform direct = root.Find(name);
            if (direct != null)
                return direct;
            Transform[] values = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < values.Length; i++)
                if (string.Equals(values[i].name, name, StringComparison.Ordinal))
                    return values[i];
            return null;
        }

        private GameObject CreateChoiceStrip()
        {
            var strip = new GameObject("Choice Strip", typeof(RectTransform), typeof(Image));
            strip.transform.SetParent(transform, false);
            RectTransform stripRect = (RectTransform)strip.transform;
            stripRect.anchorMin = new Vector2(0.02f, 1.08f);
            stripRect.anchorMax = new Vector2(0.98f, 2.98f);
            stripRect.offsetMin = Vector2.zero;
            stripRect.offsetMax = Vector2.zero;
            var stripImage = strip.GetComponent<Image>();
            stripImage.color = new Color(0.055f, 0.036f, 0.022f, 0.96f);
            stripImage.raycastTarget = false;

            for (int i = 0; i < 4; i++)
            {
                var buttonObject = new GameObject("Choice " + (i + 1), typeof(RectTransform), typeof(Image), typeof(Button));
                buttonObject.transform.SetParent(strip.transform, false);
                RectTransform buttonRect = (RectTransform)buttonObject.transform;
                float top = 0.98f - i * 0.185f;
                buttonRect.anchorMin = new Vector2(0.025f, top - 0.175f);
                buttonRect.anchorMax = new Vector2(0.975f, top);
                buttonRect.offsetMin = Vector2.zero;
                buttonRect.offsetMax = Vector2.zero;
                buttonObject.GetComponent<Image>().color = i == 0 ? SelectedChoiceColor : DefaultChoiceColor;
                TMP_Text label = CreateInputText(buttonObject.transform, "Choice Label " + (i + 1),
                    "선택 " + (i + 1), Color.white);
                label.enableAutoSizing = true;
                label.fontSizeMin = 10f;
                label.fontSizeMax = 16f;
            }
            strip.SetActive(false);
            return strip;
        }

        private void ApplyDialogueFont(TMP_Text label)
        {
            if (label == null) return;
            TMP_FontAsset koreanFont = storyText != null && storyText.font != null
                ? storyText.font
                : speakerText != null ? speakerText.font : null;
            if (koreanFont != null && label.font != koreanFont)
                label.font = koreanFont;
        }

        public void Present(bool visible, string speaker, string story, string actionLabel, bool interactable,
            Sprite portrait = null, bool showLargeSubject = false)
        {
            ResolvePortraitReferences();
            bool largeVisible = visible && showLargeSubject && portrait != null;
            bool compactPortraitVisible = visible && portrait != null && !largeVisible;
            if (speakerPortraitFrame != null && speakerPortraitFrame.activeSelf != compactPortraitVisible)
                speakerPortraitFrame.SetActive(compactPortraitVisible);
            if (speakerPortrait != null && speakerPortrait.sprite != portrait)
                speakerPortrait.sprite = portrait;
            if (encounterSubjectStage != null && encounterSubjectStage.activeSelf != largeVisible)
                encounterSubjectStage.SetActive(largeVisible);
            if (encounterSubject != null && encounterSubject.sprite != portrait)
                encounterSubject.sprite = portrait;
            // 값이 실제로 달라질 때만 UI를 갱신해 불필요한 Canvas rebuild를 피합니다.
            if (gameObject.activeSelf != visible)
                gameObject.SetActive(visible);
            SetText(speakerText, speaker);
            SetText(storyText, story);
            SetText(nextLabel, actionLabel);
            if (nextButton != null && nextButton.interactable != interactable)
                nextButton.interactable = interactable;
            if (speakerPortrait != null)
            {
                // 화자를 아는 대사만 컷인을 보여준다. 내레이션·행동 결과는 이름표만 남긴다.
                if (speakerPortrait.sprite != portrait)
                    speakerPortrait.sprite = portrait;
                GameObject frame = speakerPortrait.transform.parent != null
                    ? speakerPortrait.transform.parent.gameObject
                    : speakerPortrait.gameObject;
                if (frame.activeSelf != compactPortraitVisible)
                    frame.SetActive(compactPortraitVisible);
                if (speakerCutinBackdrop != null && speakerCutinBackdrop.gameObject.activeSelf != compactPortraitVisible)
                    speakerCutinBackdrop.gameObject.SetActive(compactPortraitVisible);
            }
        }

        private void ResolvePortraitReferences()
        {
            if (speakerPortraitFrame == null)
                speakerPortraitFrame = transform.Find("Speaker Portrait Frame")?.gameObject;
            if (speakerPortrait == null && speakerPortraitFrame != null)
                speakerPortrait = speakerPortraitFrame.transform.Find("Speaker Portrait")?.GetComponent<Image>();
            if (encounterSubjectStage == null)
                encounterSubjectStage = transform.Find("Encounter Subject Stage")?.gameObject;
            if (encounterSubject == null && encounterSubjectStage != null)
                encounterSubject = encounterSubjectStage.transform.Find("Encounter Subject")?.GetComponent<Image>();
            SetChildActive("Speaker Emote", false);
        }

        private void SetChildActive(string childName, bool active)
        {
            Transform child = transform.Find(childName);
            if (child != null && child.gameObject.activeSelf != active)
                child.gameObject.SetActive(active);
        }

        public void SetVisible(bool visible)
        {
            if (gameObject.activeSelf != visible)
                gameObject.SetActive(visible);
        }

#if UNITY_EDITOR
        public void Configure(TMP_Text speaker, TMP_Text story, TMP_Text label, Button button)
        {
            speakerText = speaker;
            storyText = story;
            nextLabel = label;
            nextButton = button;
            UnityEditor.EditorUtility.SetDirty(this);
        }

        public void ConfigureSpeakerVisuals(Image portrait, Image cutinBackdrop)
        {
            speakerPortrait = portrait;
            speakerCutinBackdrop = cutinBackdrop;
            UnityEditor.EditorUtility.SetDirty(this);
        }
#endif

        private static void SetText(TMP_Text target, string value)
        {
            value ??= string.Empty;
            if (target != null && target.text != value)
                target.text = value;
        }
    }
}
