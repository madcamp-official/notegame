using UnityEngine;

namespace KeyboardWanderer.Demo
{
    /// <summary>
    /// Keeps a full-screen UI root inside the platform safe area. It only writes the
    /// RectTransform when either the framebuffer or safe-area rectangle changes, so
    /// static menu/dialogue screens do not trigger a Canvas rebuild every frame.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RectTransform))]
    public sealed class KeyboardWandererSafeAreaFitter : MonoBehaviour
    {
        private RectTransform _rect;
        private Rect _lastSafeArea = new Rect(-1f, -1f, -1f, -1f);
        private Vector2Int _lastScreenSize = new Vector2Int(-1, -1);

        private void OnEnable()
        {
            ApplySafeArea(Screen.safeArea, new Vector2Int(Screen.width, Screen.height));
        }

        private void Update()
        {
            Rect safeArea = Screen.safeArea;
            var screenSize = new Vector2Int(Screen.width, Screen.height);
            if (safeArea != _lastSafeArea || screenSize != _lastScreenSize)
                ApplySafeArea(safeArea, screenSize);
        }

        /// <summary>Public deterministic entry point used by resolution regression tests.</summary>
        public bool ApplySafeArea(Rect safeArea, Vector2Int screenSize)
        {
            if (screenSize.x <= 0 || screenSize.y <= 0)
                return false;
            if (_rect == null)
                _rect = GetComponent<RectTransform>();

            safeArea.xMin = Mathf.Clamp(safeArea.xMin, 0f, screenSize.x);
            safeArea.xMax = Mathf.Clamp(safeArea.xMax, safeArea.xMin, screenSize.x);
            safeArea.yMin = Mathf.Clamp(safeArea.yMin, 0f, screenSize.y);
            safeArea.yMax = Mathf.Clamp(safeArea.yMax, safeArea.yMin, screenSize.y);
            Vector2 anchorMin = new Vector2(safeArea.xMin / screenSize.x, safeArea.yMin / screenSize.y);
            Vector2 anchorMax = new Vector2(safeArea.xMax / screenSize.x, safeArea.yMax / screenSize.y);
            bool changed = _lastSafeArea != safeArea || _lastScreenSize != screenSize ||
                           _rect.anchorMin != anchorMin || _rect.anchorMax != anchorMax ||
                           _rect.offsetMin != Vector2.zero || _rect.offsetMax != Vector2.zero;
            _lastSafeArea = safeArea;
            _lastScreenSize = screenSize;
            if (!changed)
                return false;

            _rect.anchorMin = anchorMin;
            _rect.anchorMax = anchorMax;
            _rect.offsetMin = Vector2.zero;
            _rect.offsetMax = Vector2.zero;
            return true;
        }
    }
}
