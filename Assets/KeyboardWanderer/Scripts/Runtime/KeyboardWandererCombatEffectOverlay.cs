using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace KeyboardWanderer.Runtime
{
    /// <summary>
    /// Renders short combat impacts in screen space at the authoritative world target.
    /// A world SpriteRenderer is always behind a ScreenSpaceOverlay HUD, so important
    /// hit feedback could disappear under the quest/status panels near screen edges.
    /// This non-interactive layer keeps the same spatial target while drawing above the
    /// normal HUD and below the D20/result overlay.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class KeyboardWandererCombatEffectOverlay : MonoBehaviour
    {
        public const int OverlaySortingOrder = 4400;
        private const float MaximumEffectSize = 190f;
        private const float SafeEdgePadding = 10f;

        private Camera _worldCamera;
        private GameObject _root;
        private RectTransform _canvasRect;
        private Image _image;

        public bool IsVisible => _root != null && _root.activeSelf && _image != null && _image.enabled;

        public void Configure(Camera worldCamera)
        {
            _worldCamera = worldCamera;
        }

        public IEnumerator PlayAndWait(Sprite[] frames, Vector3 worldPosition, float framesPerSecond)
        {
            if (frames == null || frames.Length == 0)
                yield break;
            if (!EnsureLayer())
                yield break;

            _root.SetActive(true);
            _image.enabled = true;
            float frameDuration = 1f / Mathf.Clamp(framesPerSecond, 1f, 30f);
            for (int i = 0; i < frames.Length; i++)
            {
                Sprite frame = frames[i];
                if (frame == null)
                    continue;
                _image.sprite = frame;
                FitImage(frame);
                float frameEndsAt = Time.unscaledTime + frameDuration;
                do
                {
                    PositionAtWorldTarget(worldPosition);
                    yield return null;
                } while (Time.unscaledTime < frameEndsAt);
            }

            _image.enabled = false;
            _root.SetActive(false);
        }

        public void Hide()
        {
            if (_image != null)
                _image.enabled = false;
            if (_root != null)
                _root.SetActive(false);
        }

        private bool EnsureLayer()
        {
            if (_root != null && _canvasRect != null && _image != null)
                return true;
            if (_worldCamera == null)
                _worldCamera = Camera.main;
            if (_worldCamera == null)
                return false;

            _root = new GameObject("Combat Effect UI Overlay", typeof(RectTransform), typeof(Canvas),
                typeof(CanvasScaler));
            _root.transform.SetParent(transform, false);
            Canvas canvas = _root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = OverlaySortingOrder;
            CanvasScaler scaler = _root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1440f, 900f);
            scaler.matchWidthOrHeight = 0.5f;
            _canvasRect = _root.GetComponent<RectTransform>();

            var imageObject = new GameObject("Combat Impact", typeof(RectTransform), typeof(Image));
            imageObject.transform.SetParent(_root.transform, false);
            RectTransform imageRect = imageObject.GetComponent<RectTransform>();
            imageRect.anchorMin = imageRect.anchorMax = new Vector2(0.5f, 0.5f);
            imageRect.pivot = new Vector2(0.5f, 0.5f);
            _image = imageObject.GetComponent<Image>();
            _image.preserveAspect = true;
            _image.raycastTarget = false;
            _root.SetActive(false);
            return true;
        }

        private void FitImage(Sprite sprite)
        {
            Rect spriteRect = sprite.rect;
            float longest = Mathf.Max(1f, spriteRect.width, spriteRect.height);
            RectTransform rect = (RectTransform)_image.transform;
            rect.sizeDelta = new Vector2(spriteRect.width / longest * MaximumEffectSize,
                spriteRect.height / longest * MaximumEffectSize);
        }

        private void PositionAtWorldTarget(Vector3 worldPosition)
        {
            if (_worldCamera == null || _canvasRect == null || _image == null)
                return;
            Vector3 screen = _worldCamera.WorldToScreenPoint(worldPosition);
            RectTransform imageRect = (RectTransform)_image.transform;
            Rect safe = Screen.safeArea;
            float halfWidth = imageRect.rect.width * 0.5f;
            float halfHeight = imageRect.rect.height * 0.5f;
            screen.x = Mathf.Clamp(screen.x, safe.xMin + SafeEdgePadding + halfWidth,
                safe.xMax - SafeEdgePadding - halfWidth);
            screen.y = Mathf.Clamp(screen.y, safe.yMin + SafeEdgePadding + halfHeight,
                safe.yMax - SafeEdgePadding - halfHeight);
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(_canvasRect, screen, null,
                    out Vector2 localPoint))
                imageRect.anchoredPosition = localPoint;
        }

        private void OnDisable()
        {
            Hide();
        }
    }
}
