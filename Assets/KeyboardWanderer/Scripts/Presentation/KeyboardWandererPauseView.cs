using System;
using UnityEngine;
using UnityEngine.UI;

namespace KeyboardWanderer.Demo
{
    /// <summary>Pause Screen 오브젝트가 일시정지 메뉴 버튼을 직접 소유한다.</summary>
    [DisallowMultipleComponent]
    public sealed class KeyboardWandererPauseView : MonoBehaviour
    {
        [SerializeField] private Button resumeButton;
        [SerializeField] private Button settingsButton;
        [SerializeField] private Button titleButton;

        private bool _bound;

        public bool IsReady => resumeButton != null && settingsButton != null && titleButton != null;

        public void Bind(Action onResume, Action onSettings, Action onTitle)
        {
            if (_bound || !IsReady)
                return;
            if (onResume != null) resumeButton.onClick.AddListener(onResume.Invoke);
            if (onSettings != null) settingsButton.onClick.AddListener(onSettings.Invoke);
            if (onTitle != null) titleButton.onClick.AddListener(onTitle.Invoke);
            _bound = true;
        }

#if UNITY_EDITOR
        public void Configure(Button resume, Button settings, Button title)
        {
            resumeButton = resume;
            settingsButton = settings;
            titleButton = title;
            UnityEditor.EditorUtility.SetDirty(this);
        }
#endif
    }
}
