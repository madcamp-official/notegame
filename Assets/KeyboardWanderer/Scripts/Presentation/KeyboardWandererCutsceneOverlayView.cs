using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace KeyboardWanderer.Demo
{
    /// <summary>
    /// Authored UI 화면 목록에 속하지 않는, 완전히 코드로만 만들어지는 컷신 오버레이다.
    /// 오프닝·엔딩·게임 오버 컷신이 모두 이 View를 공유한다.
    /// 부착된 오브젝트 위에 자기 전용 Canvas를 새로 만들어 무엇이 떠 있든 그 위에 그려지고,
    /// 재생이 끝나면 그 Canvas를 포함한 자기 자신을 통째로 파괴한다.
    /// Editor의 "Rebuild Authored Scene UI" 도구나 씬에 미리 구워둔 오브젝트에 전혀 의존하지 않으므로
    /// 씬을 다시 굽지 않아도 항상 재생되고, 끝난 뒤에는 게임 UI에 어떤 흔적도 남기지 않는다.
    /// </summary>
    public sealed class KeyboardWandererCutsceneOverlayView : MonoBehaviour
    {
        private const float FadeDuration = 0.6f;
        private const float CrossfadeDuration = 0.45f;
        private const float HintDelaySeconds = 2f;
        private const int OverlaySortingOrder = 5000;
        private const string FontResourcePath = "Fonts/NeoDunggeunmoPro-Regular SDF";
        private const string HintLabel = "화면을 클릭해서 진행";

        private Image _frameBottom;
        private Image _frameTop;
        private Image _fadeOverlay;
        private TMP_Text _hintText;
        private bool _advanceRequested;

        /// <summary>
        /// <paramref name="parent"/> 아래에 오버레이를 만들어 프레임을 순서대로 재생한다.
        /// 마지막 프레임 이후 페이드 아웃이 끝나면 오버레이를 파괴하고 콜백을 부른다.
        /// </summary>
        public static void Play(Transform parent, Sprite[] frames, Action onComplete)
        {
            if (frames == null || frames.Length == 0)
            {
                onComplete?.Invoke();
                return;
            }

            var host = new GameObject("Cutscene Overlay");
            host.transform.SetParent(parent, false);
            KeyboardWandererCutsceneOverlayView view = host.AddComponent<KeyboardWandererCutsceneOverlayView>();
            view.BuildUi();
            view.StartCoroutine(view.PlayRoutine(frames, onComplete, host));
        }

        private void BuildUi()
        {
            var canvasObject = new GameObject("Overlay Canvas",
                typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform, false);

            Canvas canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = OverlaySortingOrder;
            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1440f, 900f);
            scaler.matchWidthOrHeight = 0.5f;

            Transform root = canvasObject.transform;
            TMP_FontAsset font = Resources.Load<TMP_FontAsset>(FontResourcePath);

            _frameBottom = FullRectImage(root, "Frame Bottom", Color.white);
            _frameBottom.preserveAspect = false;

            _frameTop = FullRectImage(root, "Frame Top", new Color(1f, 1f, 1f, 0f));
            _frameTop.preserveAspect = false;

            var catcherObject = new GameObject("Click Catcher", typeof(RectTransform), typeof(Image), typeof(Button));
            catcherObject.transform.SetParent(root, false);
            StretchFull(catcherObject.GetComponent<RectTransform>());
            Image catcherImage = catcherObject.GetComponent<Image>();
            catcherImage.color = Color.clear;
            Button clickCatcher = catcherObject.GetComponent<Button>();
            clickCatcher.transition = Selectable.Transition.None;
            clickCatcher.targetGraphic = catcherImage;
            clickCatcher.onClick.AddListener(() => _advanceRequested = true);

            var hintObject = new GameObject("Continue Hint", typeof(RectTransform));
            hintObject.transform.SetParent(root, false);
            RectTransform hintRect = hintObject.GetComponent<RectTransform>();
            hintRect.anchorMin = new Vector2(0.30f, 0.035f);
            hintRect.anchorMax = new Vector2(0.70f, 0.085f);
            hintRect.offsetMin = Vector2.zero;
            hintRect.offsetMax = Vector2.zero;
            _hintText = hintObject.AddComponent<TextMeshProUGUI>();
            _hintText.text = HintLabel;
            _hintText.font = font;
            _hintText.fontSize = 14;
            _hintText.color = new Color(0.94f, 0.87f, 0.71f, 1f);
            _hintText.alignment = TextAlignmentOptions.Center;
            _hintText.raycastTarget = false;
            hintObject.SetActive(false);

            _fadeOverlay = FullRectImage(root, "Fade Overlay", Color.black);
            _fadeOverlay.raycastTarget = false;
        }

        private static Image FullRectImage(Transform parent, string name, Color color)
        {
            var obj = new GameObject(name, typeof(RectTransform), typeof(Image));
            obj.transform.SetParent(parent, false);
            StretchFull(obj.GetComponent<RectTransform>());
            Image image = obj.GetComponent<Image>();
            image.color = color;
            return image;
        }

        private static void StretchFull(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private IEnumerator PlayRoutine(Sprite[] frames, Action onComplete, GameObject host)
        {
            _advanceRequested = false;
            _frameBottom.sprite = frames[0];
            _frameTop.sprite = frames[0];
            SetAlpha(_frameTop, 0f);
            _hintText.gameObject.SetActive(false);
            SetAlpha(_fadeOverlay, 1f);

            yield return FadeAlpha(_fadeOverlay, 1f, 0f, FadeDuration);
            _advanceRequested = false; // 페이드 인 중 들어온 클릭은 첫 이미지를 건너뛰지 않도록 무시한다.

            for (int i = 0; i < frames.Length; i++)
            {
                yield return WaitForAdvance();
                if (i < frames.Length - 1)
                    yield return Crossfade(frames[i + 1]);
            }

            _hintText.gameObject.SetActive(false);
            yield return FadeAlpha(_fadeOverlay, 0f, 1f, FadeDuration);

            Destroy(host);
            onComplete?.Invoke();
        }

        private IEnumerator WaitForAdvance()
        {
            _hintText.gameObject.SetActive(false);
            float t = 0f;
            while (t < HintDelaySeconds && !_advanceRequested)
            {
                t += Time.unscaledDeltaTime;
                yield return null;
            }
            _hintText.gameObject.SetActive(true);
            while (!_advanceRequested)
                yield return null;
            _advanceRequested = false;
        }

        private IEnumerator Crossfade(Sprite next)
        {
            _frameTop.sprite = next;
            SetAlpha(_frameTop, 0f);
            float t = 0f;
            while (t < CrossfadeDuration)
            {
                t += Time.unscaledDeltaTime;
                SetAlpha(_frameTop, Mathf.Clamp01(t / CrossfadeDuration));
                yield return null;
            }
            _frameBottom.sprite = next;
            SetAlpha(_frameTop, 0f);
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
    }
}
