using System;
using KeyboardWanderer.Demo;

namespace KeyboardWanderer.Presentation
{
    /// <summary>게임 화면 전환과 전역 설정값을 HUD View에 전달합니다.</summary>
    public sealed class HudPresenter
    {
        private readonly KeyboardWandererSceneUI _view;

        public HudPresenter(KeyboardWandererSceneUI view) => _view = view;

        public void PresentScreen(bool title, bool settings, bool playing, bool paused, bool ended,
            float musicVolume, float sfxVolume, bool gmEnabled)
        {
            if (_view == null || !_view.IsReady) return;
            _view.Show(title, settings, playing, paused, ended);
            _view.PresentSettings(musicVolume, sfxVolume, gmEnabled);
        }
    }

    /// <summary>
    /// 대화 문장의 내용이나 UI 오브젝트를 소유하지 않고 페이지·닫힘 상태만 관리합니다.
    /// 같은 대화 서명이 다시 전달되면 현재 페이지를 유지합니다.
    /// </summary>
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

        public void Show() => IsDismissed = false;

        public void ShowLast(int pageCount)
        {
            Page = Math.Max(0, pageCount - 1);
            IsDismissed = false;
        }

        public void Dismiss() => IsDismissed = true;

        public void Reset()
        {
            Page = 0;
            IsDismissed = false;
            Signature = string.Empty;
        }
    }

    /// <summary>첫 실행 튜토리얼의 활성 상태와 현재 페이지만 관리합니다.</summary>
    public sealed class TutorialPresenter
    {
        public bool IsActive { get; private set; }
        public int Page { get; private set; }

        public void Start(bool active)
        {
            IsActive = active;
            Page = 0;
        }

        public bool Advance(int pageCount)
        {
            if (!IsActive)
                return false;
            if (Page < Math.Max(1, pageCount) - 1)
            {
                Page++;
                return false;
            }
            IsActive = false;
            Page = 0;
            return true;
        }
    }

    /// <summary>미니맵 입력 서명이 달라졌을 때만 다시 그리도록 변경 여부를 판정합니다.</summary>
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
