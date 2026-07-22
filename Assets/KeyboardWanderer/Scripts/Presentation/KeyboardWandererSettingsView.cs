using System;
using TMPro;
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
        [SerializeField] private TMP_InputField geminiApiKeyInput;
        [SerializeField] private TMP_Text geminiApiKeyStatus;
        [SerializeField] private Button backButton;
        [SerializeField] private Button deleteSaveButton;

        private bool _bound;

        public bool IsReady => musicSlider != null && sfxSlider != null && gmToggle != null &&
                               geminiApiKeyInput != null && geminiApiKeyStatus != null &&
                               backButton != null && deleteSaveButton != null;

        public void Bind(Action<float> onMusicChanged, Action<float> onSfxChanged, Action<bool> onGmChanged,
            Action<string> onGeminiApiKeyChanged, Action onBack, Action onDeleteSave)
        {
            if (_bound || !IsReady)
                return;
            if (onMusicChanged != null) musicSlider.onValueChanged.AddListener(onMusicChanged.Invoke);
            if (onSfxChanged != null) sfxSlider.onValueChanged.AddListener(onSfxChanged.Invoke);
            if (onGmChanged != null) gmToggle.onValueChanged.AddListener(onGmChanged.Invoke);
            if (onGeminiApiKeyChanged != null)
                geminiApiKeyInput.onEndEdit.AddListener(onGeminiApiKeyChanged.Invoke);
            if (onBack != null) backButton.onClick.AddListener(onBack.Invoke);
            if (onDeleteSave != null) deleteSaveButton.onClick.AddListener(onDeleteSave.Invoke);
            _bound = true;
        }

        public void Present(float musicVolume, float sfxVolume, bool gmEnabled, string geminiApiKey)
        {
            if (!IsReady)
                return;
            musicSlider.SetValueWithoutNotify(musicVolume);
            sfxSlider.SetValueWithoutNotify(sfxVolume);
            gmToggle.SetIsOnWithoutNotify(gmEnabled);
            geminiApiKey ??= string.Empty;
            if (!geminiApiKeyInput.isFocused && !string.Equals(geminiApiKeyInput.text, geminiApiKey,
                    StringComparison.Ordinal))
                geminiApiKeyInput.SetTextWithoutNotify(geminiApiKey);
            geminiApiKeyStatus.text = string.IsNullOrWhiteSpace(geminiApiKey)
                ? "키 없음 · 서버 환경 키 사용"
                : "키 저장됨 · 이 기기에서만 사용";
        }

#if UNITY_EDITOR
        public void Configure(Slider music, Slider sfx, Toggle gm, TMP_InputField apiKeyInput,
            TMP_Text apiKeyStatus, Button back, Button deleteSave)
        {
            musicSlider = music;
            sfxSlider = sfx;
            gmToggle = gm;
            geminiApiKeyInput = apiKeyInput;
            geminiApiKeyStatus = apiKeyStatus;
            backButton = back;
            deleteSaveButton = deleteSave;
            UnityEditor.EditorUtility.SetDirty(this);
        }
#endif
    }
}
