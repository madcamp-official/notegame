using System.Collections;
using KeyboardWanderer.Demo;
using UnityEngine;

namespace KeyboardWanderer.Runtime
{
    /// <summary>
    /// 사용자 설정값의 로드·저장과 오디오 볼륨 반영을 전담한다.
    /// UI 버튼은 이 컴포넌트의 공개 메서드만 호출하며 PlayerPrefs 키를 직접 알지 않는다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class KeyboardWandererSettingsController : MonoBehaviour
    {
        private const string MusicVolumeKey = "keyboard-wanderer.music-volume";
        private const string SfxVolumeKey = "keyboard-wanderer.sfx-volume";
        private const string GmEnabledKey = "keyboard-wanderer.gm-enabled";
        private const string GeminiApiKey = "keyboard-wanderer.gemini-api-key";
        private const float SaveDebounceSeconds = 0.25f;

        [SerializeField] private KeyboardWandererAudioController audioController;

        private bool _loaded;
        private bool _saveDirty;
        private Coroutine _saveCoroutine;
        private float _saveDueAt;

        public float MusicVolume { get; private set; } = 0.65f;
        public float SfxVolume { get; private set; } = 0.8f;
        public bool GmEnabled { get; private set; } = true;
        public string GeminiKey { get; private set; } = string.Empty;

        private void Awake()
        {
            EnsureLoaded();
            ApplyAudioVolumes();
        }

        /// <summary>씬에서 작성된 오디오 오브젝트를 연결하고 현재 설정을 즉시 적용한다.</summary>
        public void Configure(KeyboardWandererAudioController audio)
        {
            audioController = audio;
            EnsureLoaded();
            ApplyAudioVolumes();
        }

        public void SetMusicVolume(float value)
        {
            EnsureLoaded();
            MusicVolume = Mathf.Clamp01(value);
            SaveAndApply();
        }

        public void SetSfxVolume(float value)
        {
            EnsureLoaded();
            SfxVolume = Mathf.Clamp01(value);
            SaveAndApply();
        }

        public void SetGmEnabled(bool value)
        {
            EnsureLoaded();
            GmEnabled = value;
            StageAndScheduleSave();
        }

        /// <summary>
        /// 플레이어가 설정 화면에 입력한 Gemini 키를 이 기기의 PlayerPrefs에 보존한다.
        /// 빈 값은 클라이언트 키를 제거하고 서버의 GEMINI_API_KEY 폴백을 다시 사용한다.
        /// </summary>
        public void SetGeminiApiKey(string value)
        {
            EnsureLoaded();
            GeminiKey = NormalizeGeminiKey(value);
            if (string.IsNullOrEmpty(GeminiKey))
                KeyboardWandererPreferences.DeleteKey(GeminiApiKey);
            else
                KeyboardWandererPreferences.SetString(GeminiApiKey, GeminiKey);
            _saveDirty = true;
            ScheduleSave();
        }

        private void EnsureLoaded()
        {
            if (_loaded)
                return;
            MusicVolume = Mathf.Clamp01(KeyboardWandererPreferences.GetFloat(MusicVolumeKey, 0.65f));
            SfxVolume = Mathf.Clamp01(KeyboardWandererPreferences.GetFloat(SfxVolumeKey, 0.8f));
            GmEnabled = KeyboardWandererPreferences.GetInt(GmEnabledKey, 1) != 0;
            GeminiKey = NormalizeGeminiKey(KeyboardWandererPreferences.GetString(GeminiApiKey, string.Empty));
            _loaded = true;
        }

        private void SaveAndApply()
        {
            StageAndScheduleSave();
            ApplyAudioVolumes();
        }

        private void StageAndScheduleSave()
        {
            KeyboardWandererPreferences.SetFloat(MusicVolumeKey, MusicVolume);
            KeyboardWandererPreferences.SetFloat(SfxVolumeKey, SfxVolume);
            KeyboardWandererPreferences.SetInt(GmEnabledKey, GmEnabled ? 1 : 0);
            _saveDirty = true;

            ScheduleSave();
        }

        private void ScheduleSave()
        {
            // Edit Mode utilities are not allowed to start coroutines and historically
            // expect persistence to be synchronous. Runtime slider drags, however, can
            // fire dozens of changes per second; debounce only the native disk flush.
            if (!Application.isPlaying || !isActiveAndEnabled)
            {
                FlushPreferences();
                return;
            }
            _saveDueAt = Time.realtimeSinceStartup + SaveDebounceSeconds;
            if (_saveCoroutine == null)
                _saveCoroutine = StartCoroutine(FlushAfterQuietPeriod());
        }

        private static string NormalizeGeminiKey(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;
            string normalized = value.Trim();
            for (int i = 0; i < normalized.Length; i++)
                if (char.IsControl(normalized[i]) || char.IsWhiteSpace(normalized[i]))
                    return string.Empty;
            return normalized.Length <= 256 ? normalized : normalized.Substring(0, 256);
        }

        private IEnumerator FlushAfterQuietPeriod()
        {
            // Keep one coroutine alive while the slider moves instead of allocating and
            // cancelling a new delayed-yield object for every onValueChanged callback.
            while (Time.realtimeSinceStartup < _saveDueAt)
                yield return null;
            _saveCoroutine = null;
            FlushPreferences();
        }

        private void FlushPreferences()
        {
            if (_saveCoroutine != null)
            {
                StopCoroutine(_saveCoroutine);
                _saveCoroutine = null;
            }
            if (!_saveDirty)
                return;
            KeyboardWandererPreferences.Save();
            _saveDirty = false;
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused)
                FlushPreferences();
        }

        private void OnApplicationQuit()
        {
            FlushPreferences();
        }

        private void OnDisable()
        {
            FlushPreferences();
        }

        private void ApplyAudioVolumes()
        {
            audioController?.SetVolumes(MusicVolume, SfxVolume);
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            MusicVolume = Mathf.Clamp01(MusicVolume);
            SfxVolume = Mathf.Clamp01(SfxVolume);
        }
#endif
    }

    /// <summary>네트워크 클라이언트가 현재 사용자 키를 동적으로 읽는 단일 경계다.</summary>
    public static class KeyboardWandererGeminiKeyStore
    {
        private const string GeminiApiKey = "keyboard-wanderer.gemini-api-key";

        public static string Current
        {
            get
            {
                string value = KeyboardWandererPreferences.GetString(GeminiApiKey, string.Empty)?.Trim() ?? string.Empty;
                if (value.Length > 256) return string.Empty;
                for (int i = 0; i < value.Length; i++)
                    if (char.IsControl(value[i]) || char.IsWhiteSpace(value[i]))
                        return string.Empty;
                return value;
            }
        }
    }
}
