using TMPro;
using KeyboardWanderer.Presentation;
using UnityEngine;
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

        private bool _bound;

        public bool IsReady => speakerText != null && storyText != null && nextLabel != null && nextButton != null;
        public TMP_Text SpeakerText => speakerText;
        public TMP_Text StoryText => storyText;
        public TMP_Text NextLabel => nextLabel;
        public Button NextButton => nextButton;

        public void Bind(UnityAction advance)
        {
            if (_bound || advance == null || nextButton == null)
                return;
            nextButton.onClick.AddListener(advance);
            _bound = true;
        }

        public void Present(bool visible, string speaker, string story, string actionLabel, bool interactable)
        {
            // 값이 실제로 달라질 때만 UI를 갱신해 불필요한 Canvas rebuild를 피합니다.
            if (gameObject.activeSelf != visible)
                gameObject.SetActive(visible);
            SetText(speakerText, speaker);
            SetText(storyText, story);
            SetText(nextLabel, actionLabel);
            if (nextButton != null && nextButton.interactable != interactable)
                nextButton.interactable = interactable;
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
#endif

        private static void SetText(TMP_Text target, string value)
        {
            value ??= string.Empty;
            if (target != null && target.text != value)
                target.text = value;
        }
    }
}
