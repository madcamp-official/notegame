using System;

namespace KeyboardWanderer.Demo
{
    public sealed class HudPresenter
    {
        private readonly KeyboardWandererSceneUI _view;

        public HudPresenter(KeyboardWandererSceneUI view) => _view = view;

        public void PresentScreen(bool title, bool settings, bool playing, bool paused, bool ended,
            float musicVolume, float sfxVolume, bool gmEnabled)
        {
            if (_view == null || !_view.IsReady) return;
            _view.Show(title, settings, playing, paused, ended);
            _view.SetMusicVolume(musicVolume);
            _view.SetSfxVolume(sfxVolume);
            _view.SetGmEnabled(gmEnabled);
        }
    }

    public sealed class DialoguePresenter
    {
        public int Page { get; private set; }
        public bool IsDismissed { get; private set; }
        public string Signature { get; private set; } = string.Empty;

        public bool Synchronize(string signature, bool deferReopen)
        {
            signature ??= string.Empty;
            if (string.Equals(Signature, signature, StringComparison.Ordinal)) return false;
            Signature = signature;
            Page = 0;
            if (!deferReopen) IsDismissed = false;
            return true;
        }

        public bool Advance(int pageCount)
        {
            if (Page < Math.Max(1, pageCount) - 1)
            {
                Page++;
                return true;
            }
            IsDismissed = true;
            return false;
        }

        public void Restore(int page, bool dismissed)
        {
            Page = Math.Max(0, page);
            IsDismissed = dismissed;
        }
    }

    public sealed class MinimapPresenter
    {
        public string Signature { get; private set; } = string.Empty;

        public bool ShouldRedraw(string signature)
        {
            signature ??= string.Empty;
            if (string.Equals(Signature, signature, StringComparison.Ordinal)) return false;
            Signature = signature;
            return true;
        }

        public void Invalidate() => Signature = string.Empty;
    }
}
