using UnityEngine;

namespace KeyboardWanderer.Demo
{
    [DisallowMultipleComponent]
    public sealed class KeyboardWandererAudioController : MonoBehaviour
    {
        [SerializeField] private AudioSource musicSource;
        [SerializeField] private AudioSource sfxSource;
        [SerializeField] private AudioSource importantSfxSource;

        private AudioClip _currentMusic;
        private float _sfxVolume = 1f;

        public AudioSource MusicSource => musicSource;
        public AudioSource SfxSource => sfxSource;
        public bool IsReady => musicSource != null && sfxSource != null;

        public void Configure(AudioSource music, AudioSource sfx, AudioSource importantSfx = null)
        {
            musicSource = music;
            sfxSource = sfx;
            importantSfxSource = importantSfx;
        }

        public void SetVolumes(float musicVolume, float sfxVolume)
        {
            _sfxVolume = Mathf.Clamp01(sfxVolume);
            if (musicSource != null) musicSource.volume = Mathf.Clamp01(musicVolume) * 0.45f;
            if (sfxSource != null) sfxSource.volume = _sfxVolume;
        }

        public void SetMusic(AudioClip clip)
        {
            if (musicSource == null || clip == null || _currentMusic == clip)
                return;
            _currentMusic = clip;
            musicSource.Stop();
            musicSource.clip = clip;
            musicSource.Play();
        }

        /// <summary>
        /// 반복 호출되는 SFX가 같은 채널에서 겹쳐 시끄러워지지 않도록 기본적으로
        /// 이전 재생을 끊고 새 클립을 튼다. 플레이어가 반드시 인지해야 하는 신호음은
        /// 호출부에서 cutOffPrevious를 false로 넘겨 다른 SFX에 끊기지 않는 전용 채널로 재생한다.
        /// </summary>
        public void PlaySfx(AudioClip clip, bool cutOffPrevious = true)
        {
            if (clip == null || sfxSource == null || _sfxVolume <= 0.001f)
                return;
            if (cutOffPrevious)
            {
                sfxSource.Stop();
                sfxSource.clip = clip;
                sfxSource.Play();
            }
            else
            {
                ResolveImportantSource().PlayOneShot(clip, _sfxVolume);
            }
        }

        private AudioSource ResolveImportantSource()
        {
            if (importantSfxSource == null)
            {
                importantSfxSource = sfxSource.gameObject.AddComponent<AudioSource>();
                importantSfxSource.playOnAwake = false;
                importantSfxSource.loop = false;
                importantSfxSource.spatialBlend = sfxSource.spatialBlend;
            }
            return importantSfxSource;
        }
    }
}
