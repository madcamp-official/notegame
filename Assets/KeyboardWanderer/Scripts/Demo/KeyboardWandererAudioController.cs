using UnityEngine;

namespace KeyboardWanderer.Demo
{
    [DisallowMultipleComponent]
    public sealed class KeyboardWandererAudioController : MonoBehaviour
    {
        [SerializeField] private AudioSource musicSource;
        [SerializeField] private AudioSource sfxSource;

        private AudioClip _currentMusic;
        private float _sfxVolume = 1f;

        public AudioSource MusicSource => musicSource;
        public AudioSource SfxSource => sfxSource;
        public bool IsReady => musicSource != null && sfxSource != null;

        public void Configure(AudioSource music, AudioSource sfx)
        {
            musicSource = music;
            sfxSource = sfx;
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

        public void PlaySfx(AudioClip clip)
        {
            if (sfxSource != null && clip != null && _sfxVolume > 0.001f)
                sfxSource.PlayOneShot(clip, _sfxVolume);
        }
    }
}
