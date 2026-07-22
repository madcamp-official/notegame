using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace KeyboardWanderer.Demo
{
    /// <summary>Ending Screen 오브젝트가 결말 문구와 이동 버튼을 직접 소유한다.</summary>
    [DisallowMultipleComponent]
    public sealed class KeyboardWandererEndingView : MonoBehaviour
    {
        [SerializeField] private TMP_Text heading;
        [SerializeField] private TMP_Text body;
        [SerializeField] private Button newRunButton;
        [SerializeField] private Button titleButton;

        private bool _bound;

        public bool IsReady => heading != null && body != null && newRunButton != null && titleButton != null;

        private void OnEnable()
        {
            ConfigureKeyboardNavigation();
            if (!IsReady || EventSystem.current == null)
                return;
            GameObject selected = EventSystem.current.currentSelectedGameObject;
            if (selected == null || !selected.transform.IsChildOf(transform))
                EventSystem.current.SetSelectedGameObject(newRunButton.gameObject);
        }

        public void Bind(Action onNewRun, Action onTitle)
        {
            if (_bound || !IsReady)
                return;
            if (onNewRun != null) newRunButton.onClick.AddListener(onNewRun.Invoke);
            if (onTitle != null) titleButton.onClick.AddListener(onTitle.Invoke);
            _bound = true;
        }

        public void Present(string title, string ending)
        {
            if (!IsReady)
                return;
            ConfigureKeyboardNavigation();
            SetText(heading, title);
            SetText(body, ending);
        }

        private void ConfigureKeyboardNavigation()
        {
            if (!IsReady)
                return;
            newRunButton.navigation = PairedNavigation(titleButton);
            titleButton.navigation = PairedNavigation(newRunButton);
        }

        private static Navigation PairedNavigation(Selectable other)
        {
            return new Navigation
            {
                mode = Navigation.Mode.Explicit,
                selectOnUp = other,
                selectOnDown = other,
                selectOnLeft = other,
                selectOnRight = other
            };
        }

#if UNITY_EDITOR
        public void Configure(TMP_Text title, TMP_Text ending, Button newRun, Button goToTitle)
        {
            heading = title;
            body = ending;
            newRunButton = newRun;
            titleButton = goToTitle;
            ConfigureKeyboardNavigation();
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
