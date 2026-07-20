using System;
using UnityEngine;
using UnityEngine.UI;

namespace KeyboardWanderer.Demo
{
    /// <summary>
    /// Settings Screen의 음악/효과음/GM 설정 컨트롤을 소유한다.
    /// 값 표시와 사용자 입력 연결을 같은 오브젝트에서 처리해 루트 UI의 책임을 줄인다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class KeyboardWandererSettingsView : MonoBehaviour
    {
        [SerializeField] private Slider musicSlider;
        [SerializeField] private Slider sfxSlider;
        [SerializeField] private Toggle gmToggle;
        [SerializeField] private Button backButton;
        [SerializeField] private Button deleteSaveButton;

        private bool _bound;

        public bool IsReady => musicSlider != null && sfxSlider != null && gmToggle != null &&
                               backButton != null && deleteSaveButton != null;

        public void Bind(Action<float> onMusicChanged, Action<float> onSfxChanged, Action<bool> onGmChanged,
            Action onBack, Action onDeleteSave)
        {
            if (_bound || !IsReady)
                return;
            if (onMusicChanged != null) musicSlider.onValueChanged.AddListener(onMusicChanged.Invoke);
            if (onSfxChanged != null) sfxSlider.onValueChanged.AddListener(onSfxChanged.Invoke);
            if (onGmChanged != null) gmToggle.onValueChanged.AddListener(onGmChanged.Invoke);
            if (onBack != null) backButton.onClick.AddListener(onBack.Invoke);
            if (onDeleteSave != null) deleteSaveButton.onClick.AddListener(onDeleteSave.Invoke);
            _bound = true;
        }

        public void Present(float musicVolume, float sfxVolume, bool gmEnabled)
        {
            if (!IsReady)
                return;
            musicSlider.SetValueWithoutNotify(musicVolume);
            sfxSlider.SetValueWithoutNotify(sfxVolume);
            gmToggle.SetIsOnWithoutNotify(gmEnabled);
        }

#if UNITY_EDITOR
        public void Configure(Slider music, Slider sfx, Toggle gm, Button back, Button deleteSave)
        {
            musicSlider = music;
            sfxSlider = sfx;
            gmToggle = gm;
            backButton = back;
            deleteSaveButton = deleteSave;
            UnityEditor.EditorUtility.SetDirty(this);
        }
#endif
    }
}
