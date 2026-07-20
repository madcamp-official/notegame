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

        [SerializeField] private KeyboardWandererAudioController audioController;

        private bool _loaded;

        public float MusicVolume { get; private set; } = 0.65f;
        public float SfxVolume { get; private set; } = 0.8f;
        public bool GmEnabled { get; private set; } = true;

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
            Save();
        }

        private void EnsureLoaded()
        {
            if (_loaded)
                return;
            MusicVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(MusicVolumeKey, 0.65f));
            SfxVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(SfxVolumeKey, 0.8f));
            GmEnabled = PlayerPrefs.GetInt(GmEnabledKey, 1) != 0;
            _loaded = true;
        }

        private void SaveAndApply()
        {
            Save();
            ApplyAudioVolumes();
        }

        private void Save()
        {
            PlayerPrefs.SetFloat(MusicVolumeKey, MusicVolume);
            PlayerPrefs.SetFloat(SfxVolumeKey, SfxVolume);
            PlayerPrefs.SetInt(GmEnabledKey, GmEnabled ? 1 : 0);
            PlayerPrefs.Save();
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
}
