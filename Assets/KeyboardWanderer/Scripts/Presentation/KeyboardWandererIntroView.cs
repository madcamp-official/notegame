using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace KeyboardWanderer.Demo
{
    /// <summary>
    /// Intro Screen 오브젝트가 오프닝 컷신 재생을 직접 소유한다.
    /// 검은 화면 페이드 인 → 이미지별 클릭 대기(안내 문구는 2초 뒤 등장) → 크로스페이드 전환 →
    /// 마지막 이미지 이후 페이드 아웃 순서로 진행하며, 완료 콜백을 호출한 뒤에는 스스로 아무 상태도 남기지 않는다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class KeyboardWandererIntroView : MonoBehaviour
    {
        private const float FadeDuration = 0.6f;
        private const float CrossfadeDuration = 0.45f;
        private const float HintDelaySeconds = 2f;

        [SerializeField] private Image frameBottom;
        [SerializeField] private Image frameTop;
        [SerializeField] private Image fadeOverlay;
        [SerializeField] private TMP_Text hintText;
        [SerializeField] private Button clickCatcher;

        private Coroutine _routine;
        private bool _advanceRequested;
        private bool _clickWired;

        public bool IsReady => frameBottom != null && frameTop != null && fadeOverlay != null &&
                               hintText != null && clickCatcher != null;

        private void Awake()
        {
            WireClickCatcher();
        }

        private void OnDisable()
        {
            if (_routine != null)
            {
                StopCoroutine(_routine);
                _routine = null;
            }
        }

        /// <summary>주어진 순서대로 프레임을 재생하고, 마지막 프레임 이후 페이드 아웃이 끝나면 콜백을 부른다.</summary>
        public void Play(Sprite[] frames, Action onComplete)
        {
            if (!IsReady || frames == null || frames.Length == 0)
            {
                onComplete?.Invoke();
                return;
            }
            // Awake() order across sibling components isn't guaranteed, and this can be called
            // from another component's own Awake() — make sure the listener exists before we rely on it.
            WireClickCatcher();
            if (_routine != null)
                StopCoroutine(_routine);
            _routine = StartCoroutine(PlayRoutine(frames, onComplete));
        }

        private void WireClickCatcher()
        {
            if (_clickWired || clickCatcher == null)
                return;
            clickCatcher.onClick.AddListener(HandleClicked);
            _clickWired = true;
        }

        private void HandleClicked()
        {
            _advanceRequested = true;
        }

        private IEnumerator PlayRoutine(Sprite[] frames, Action onComplete)
        {
            _advanceRequested = false;
            frameBottom.sprite = frames[0];
            frameTop.sprite = frames[0];
            SetAlpha(frameTop, 0f);
            hintText.gameObject.SetActive(false);
            SetAlpha(fadeOverlay, 1f);

            yield return FadeAlpha(fadeOverlay, 1f, 0f, FadeDuration);
            _advanceRequested = false; // 페이드 인 중 들어온 클릭은 첫 이미지를 건너뛰지 않도록 무시한다.

            for (int i = 0; i < frames.Length; i++)
            {
                yield return WaitForAdvance();
                if (i < frames.Length - 1)
                    yield return Crossfade(frames[i + 1]);
            }

            hintText.gameObject.SetActive(false);
            yield return FadeAlpha(fadeOverlay, 0f, 1f, FadeDuration);
            _routine = null;
            onComplete?.Invoke();
        }

        private IEnumerator WaitForAdvance()
        {
            hintText.gameObject.SetActive(false);
            float t = 0f;
            while (t < HintDelaySeconds && !_advanceRequested)
            {
                t += Time.unscaledDeltaTime;
                yield return null;
            }
            hintText.gameObject.SetActive(true);
            while (!_advanceRequested)
                yield return null;
            _advanceRequested = false;
        }

        private IEnumerator Crossfade(Sprite next)
        {
            frameTop.sprite = next;
            SetAlpha(frameTop, 0f);
            float t = 0f;
            while (t < CrossfadeDuration)
            {
                t += Time.unscaledDeltaTime;
                SetAlpha(frameTop, Mathf.Clamp01(t / CrossfadeDuration));
                yield return null;
            }
            frameBottom.sprite = next;
            SetAlpha(frameTop, 0f);
        }

        private static IEnumerator FadeAlpha(Image image, float from, float to, float duration)
        {
            float t = 0f;
            SetAlpha(image, from);
            while (t < duration)
            {
                t += Time.unscaledDeltaTime;
                SetAlpha(image, Mathf.Lerp(from, to, Mathf.Clamp01(t / duration)));
                yield return null;
            }
            SetAlpha(image, to);
        }

        private static void SetAlpha(Image image, float alpha)
        {
            Color color = image.color;
            color.a = alpha;
            image.color = color;
        }

#if UNITY_EDITOR
        public void Configure(Image bottom, Image top, Image fade, TMP_Text hint, Button click)
        {
            frameBottom = bottom;
            frameTop = top;
            fadeOverlay = fade;
            hintText = hint;
            clickCatcher = click;
            UnityEditor.EditorUtility.SetDirty(this);
        }
#endif
    }
}
