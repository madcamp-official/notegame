using System;
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
        private Button _freeformSubmit;
        private string _choiceSignature = string.Empty;
        private bool _choiceInputLocked;
        private bool _freeformHasSelection;
        private int _keyboardChoiceIndex;

        [Header("화자 컷인(선택)")]
        [SerializeField] private Image speakerCutinBackdrop;

        private bool _bound;

        public bool IsReady => speakerText != null && storyText != null && nextLabel != null && nextButton != null;
        public TMP_Text SpeakerText => speakerText;
        public TMP_Text StoryText => storyText;
        public TMP_Text NextLabel => nextLabel;
        public Button NextButton => nextButton;
        public bool IsFreeformFocused
        {
            get
            {
                if (_freeformInput == null) return false;
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
            if (_freeformInput != null)
            {
                _freeformInput.gameObject.SetActive(visible);
                _freeformInput.interactable = visible && interactable && !_choiceInputLocked;
            }
            if (nextButton != null && nextButton.gameObject.activeSelf == visible)
                nextButton.gameObject.SetActive(!visible);
            if (_freeformSubmit != null)
            {
                _freeformSubmit.gameObject.SetActive(visible);
                _freeformSubmit.interactable = visible && interactable && !_choiceInputLocked;
            }
            for (int i = 0; i < _choiceButtons.Length; i++)
            {
                // The authored prefab still contains a fifth legacy slot. Narrative
                // contracts are deliberately bounded to 2-4 choices.
                bool active = visible && i < 4 && i < choices.Length && choices[i] != null;
                _choiceOptions[i] = active ? choices[i] : null;
                if (_choiceButtons[i] != null && _choiceButtons[i].gameObject.activeSelf != active)
                    _choiceButtons[i].gameObject.SetActive(active);
                if (_choiceButtons[i] != null)
                    _choiceButtons[i].interactable = active && interactable && !_choiceInputLocked;
                if (active) SetText(_choiceLabels[i], ChoiceLabel(i));
            }
            if (visible && interactable && !_choiceInputLocked && !IsFreeformFocused)
                SelectKeyboardButton();
        }

        public void MoveChoiceSelection(int direction)
        {
            if (_choiceInputLocked || IsFreeformFocused || direction == 0) return;
            int count = ActiveChoiceCount();
            if (count == 0) return;
            _keyboardChoiceIndex = (_keyboardChoiceIndex + (direction < 0 ? -1 : 1) + count) % count;
            RefreshChoiceLabels();
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
            if (_choiceInputLocked || index < 0 || index >= 4 || index >= _choiceOptions.Length ||
                _choiceOptions[index] == null || string.IsNullOrWhiteSpace(_choiceOptions[index].ChoiceId))
                return;
            _choiceInputLocked = true;
            for (int i = 0; i < _choiceButtons.Length; i++)
                if (_choiceButtons[i] != null) _choiceButtons[i].interactable = false;
            _chooseNarrativeChoice?.Invoke(_choiceOptions[index].ChoiceId);
        }

        public void ReleaseChoiceInputLock()
        {
            _choiceInputLocked = false;
        }

        private void SubmitFreeform()
        {
            if (_choiceInputLocked || _freeformInput == null) return;
            string text = _freeformInput.text?.Trim();
            if (string.IsNullOrWhiteSpace(text)) return;
            _choiceInputLocked = true;
            _freeformInput.interactable = false;
            if (_freeformSubmit != null) _freeformSubmit.interactable = false;
            _submitPlayerMessage?.Invoke(text);
            _freeformInput.text = string.Empty;
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
                storyText.fontSizeMin = Mathf.Min(storyText.fontSizeMin, 10f);
            }
            var row = new GameObject("Freeform Input", typeof(RectTransform), typeof(Image));
            // 자연어 입력은 선택지 목록 위의 별도 HUD가 아니라 실제 대화 상자 안에 둔다.
            row.transform.SetParent(transform, false);
            RectTransform rowRect = (RectTransform)row.transform;
            rowRect.anchorMin = new Vector2(0.055f, 0.045f);
            rowRect.anchorMax = new Vector2(0.945f, 0.215f);
            rowRect.offsetMin = rowRect.offsetMax = Vector2.zero;
            row.GetComponent<Image>().color = new Color(0.06f, 0.07f, 0.09f, 0.96f);
            row.transform.SetAsLastSibling();

            var inputObject = new GameObject("Input", typeof(RectTransform), typeof(Image), typeof(TMP_InputField));
            inputObject.transform.SetParent(row.transform, false);
            RectTransform inputRect = (RectTransform)inputObject.transform;
            inputRect.anchorMin = new Vector2(0f, 0f);
            inputRect.anchorMax = new Vector2(0.82f, 1f);
            inputRect.offsetMin = new Vector2(6f, 5f);
            inputRect.offsetMax = new Vector2(-4f, -5f);
            inputObject.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.08f);

            TMP_Text textComponent = CreateInputText(inputObject.transform, "Text", "", new Color(0.96f, 0.96f, 0.92f, 1f));
            TMP_Text placeholder = CreateInputText(inputObject.transform, "Placeholder", "하고 싶은 말이나 행동을 직접 입력하세요…", new Color(0.72f, 0.74f, 0.76f, 0.75f));
            _freeformInput = inputObject.GetComponent<TMP_InputField>();
            _freeformInput.textComponent = textComponent;
            _freeformInput.placeholder = placeholder;
            _freeformInput.lineType = TMP_InputField.LineType.SingleLine;
            _freeformInput.characterLimit = 1000;
            _freeformInput.onSelect.AddListener(_ => _freeformHasSelection = true);
            _freeformInput.onDeselect.AddListener(_ => _freeformHasSelection = false);
            _freeformInput.onSubmit.AddListener(_ => SubmitFreeform());

            var buttonObject = new GameObject("Send", typeof(RectTransform), typeof(Image), typeof(Button));
            buttonObject.transform.SetParent(row.transform, false);
            RectTransform buttonRect = (RectTransform)buttonObject.transform;
            buttonRect.anchorMin = new Vector2(0.83f, 0f);
            buttonRect.anchorMax = new Vector2(1f, 1f);
            buttonRect.offsetMin = new Vector2(2f, 5f);
            buttonRect.offsetMax = new Vector2(-6f, -5f);
            buttonObject.GetComponent<Image>().color = new Color(0.25f, 0.55f, 0.92f, 0.95f);
            _freeformSubmit = buttonObject.GetComponent<Button>();
            _freeformSubmit.onClick.AddListener(SubmitFreeform);
            TMP_Text sendLabel = CreateInputText(buttonObject.transform, "Label", "전송", Color.white);
            sendLabel.alignment = TextAlignmentOptions.Center;
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
            label.enableWordWrapping = false;
            return label;
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

        private void RefreshChoiceLabels()
        {
            for (int i = 0; i < 4 && i < _choiceOptions.Length; i++)
                if (_choiceOptions[i] != null) SetText(_choiceLabels[i], ChoiceLabel(i));
        }

        private void SelectKeyboardButton()
        {
            int count = ActiveChoiceCount();
            if (count == 0) return;
            _keyboardChoiceIndex = Mathf.Clamp(_keyboardChoiceIndex, 0, count - 1);
            Button button = _choiceButtons[_keyboardChoiceIndex];
            if (button != null && button.interactable) button.Select();
        }

        private static string ChoiceSignature(NarrativeChoiceOption[] choices)
        {
            if (choices == null || choices.Length == 0) return string.Empty;
            var values = new string[Math.Min(4, choices.Length)];
            for (int i = 0; i < values.Length; i++)
                values[i] = choices[i]?.ChoiceId ?? string.Empty;
            return string.Join("\u001f", values);
        }

        private void ResolveChoiceReferences()
        {
            if (_choiceStrip == null)
                _choiceStrip = transform.Find("Choice Strip")?.gameObject;
            if (_choiceStrip == null)
                _choiceStrip = CreateChoiceStrip();
            if (_choiceStrip == null) return;
            for (int i = 0; i < _choiceButtons.Length; i++)
            {
                Transform choice = _choiceStrip.transform.Find("Choice " + (i + 1));
                if (_choiceButtons[i] == null) _choiceButtons[i] = choice?.GetComponent<Button>();
                if (_choiceLabels[i] == null) _choiceLabels[i] = choice?.GetComponentInChildren<TMP_Text>(true);
                // Older authored prefabs arranged five shortcut-sized tiles in one row.
                // Reflow the retained first four slots into visual-novel response bars so
                // the complete LLM-authored sentence remains readable.
                if (choice is RectTransform rect && i < 4)
                {
                    float top = 0.98f - i * 0.19f;
                    rect.anchorMin = new Vector2(0.025f, top - 0.145f);
                    rect.anchorMax = new Vector2(0.975f, top);
                    rect.offsetMin = Vector2.zero;
                    rect.offsetMax = Vector2.zero;
                }
                if (_choiceLabels[i] != null && i < 4)
                {
                    ApplyDialogueFont(_choiceLabels[i]);
                    _choiceLabels[i].enableAutoSizing = true;
                    _choiceLabels[i].fontSizeMin = 10f;
                    _choiceLabels[i].fontSizeMax = 16f;
                    _choiceLabels[i].alignment = TextAlignmentOptions.MidlineLeft;
                    _choiceLabels[i].margin = new Vector4(14f, 3f, 10f, 3f);
                }
            }
        }

        private GameObject CreateChoiceStrip()
        {
            var strip = new GameObject("Choice Strip", typeof(RectTransform), typeof(Image));
            strip.transform.SetParent(transform, false);
            RectTransform stripRect = (RectTransform)strip.transform;
            stripRect.anchorMin = new Vector2(0.02f, 1.08f);
            stripRect.anchorMax = new Vector2(0.98f, 2.08f);
            stripRect.offsetMin = Vector2.zero;
            stripRect.offsetMax = Vector2.zero;
            strip.GetComponent<Image>().color = new Color(0.055f, 0.036f, 0.022f, 0.96f);

            for (int i = 0; i < 4; i++)
            {
                var buttonObject = new GameObject("Choice " + (i + 1), typeof(RectTransform), typeof(Image), typeof(Button));
                buttonObject.transform.SetParent(strip.transform, false);
                RectTransform buttonRect = (RectTransform)buttonObject.transform;
                float top = 0.98f - i * 0.19f;
                buttonRect.anchorMin = new Vector2(0.025f, top - 0.145f);
                buttonRect.anchorMax = new Vector2(0.975f, top);
                buttonRect.offsetMin = Vector2.zero;
                buttonRect.offsetMax = Vector2.zero;
                buttonObject.GetComponent<Image>().color = i == 0
                    ? new Color(0.88f, 0.68f, 0.31f, 0.96f)
                    : new Color(0.22f, 0.16f, 0.10f, 0.96f);
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
            if (speakerPortraitFrame != null && speakerPortraitFrame.activeSelf != (portrait != null))
                speakerPortraitFrame.SetActive(portrait != null);
            if (speakerPortrait != null && speakerPortrait.sprite != portrait)
                speakerPortrait.sprite = portrait;
            bool largeVisible = visible && showLargeSubject && portrait != null;
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
                bool portraitVisible = portrait != null;
                if (frame.activeSelf != portraitVisible)
                    frame.SetActive(portraitVisible);
                if (speakerCutinBackdrop != null && speakerCutinBackdrop.gameObject.activeSelf != portraitVisible)
                    speakerCutinBackdrop.gameObject.SetActive(portraitVisible);
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
