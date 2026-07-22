using UnityEngine;

namespace KeyboardWanderer.Demo
{
    [DisallowMultipleComponent]
    public sealed class KeyboardWandererCameraController : MonoBehaviour
    {
        [SerializeField] private Camera targetCamera;
        [SerializeField, Min(0.01f)] private float followSmoothTime = 0.16f;

        private Vector3 _velocity;
        private int _lastScreenWidth;
        private int _lastScreenHeight;

        public Camera TargetCamera => targetCamera;
        public bool IsReady => targetCamera != null;

        public void Configure(Camera camera)
        {
            targetCamera = camera;
        }

        public void SetEnabled(bool value)
        {
            if (targetCamera != null) targetCamera.enabled = value;
        }

        public void UpdateViewport(bool force = false)
        {
            if (targetCamera == null ||
                (!force && _lastScreenWidth == Screen.width && _lastScreenHeight == Screen.height))
                return;
            _lastScreenWidth = Screen.width;
            _lastScreenHeight = Screen.height;
            targetCamera.rect = new Rect(0f, 0f, 1f, 1f);
        }

        public bool ContainsScreenPoint(Vector2 screenPoint)
        {
            return targetCamera != null && targetCamera.pixelRect.Contains(screenPoint);
        }

        public Vector3 ScreenToWorld(Vector2 screenPoint)
        {
            return targetCamera != null
                ? targetCamera.ScreenToWorldPoint(new Vector3(screenPoint.x, screenPoint.y, 0f))
                : Vector3.zero;
        }

        public void Follow(
            Vector3 desired,
            Vector2 worldOrigin,
            int worldWidth,
            int worldHeight,
            float unscaledDeltaTime)
        {
            if (targetCamera == null)
                return;
            float halfHeight = targetCamera.orthographicSize;
            float aspect = targetCamera.pixelHeight > 0
                ? targetCamera.pixelWidth / (float)targetCamera.pixelHeight
                : 1.3f;
            float halfWidth = halfHeight * aspect;
            float minX = worldOrigin.x + Mathf.Min(halfWidth, worldWidth * 0.5f);
            float maxX = worldOrigin.x + worldWidth - Mathf.Min(halfWidth, worldWidth * 0.5f);
            float minY = worldOrigin.y + Mathf.Min(halfHeight, worldHeight * 0.5f);
            float maxY = worldOrigin.y + worldHeight - Mathf.Min(halfHeight, worldHeight * 0.5f);
            desired.x = Mathf.Clamp(desired.x, minX, maxX);
            desired.y = Mathf.Clamp(desired.y, minY, maxY);
            desired.z = targetCamera.transform.position.z;
            Vector3 current = targetCamera.transform.position;
            if (Vector3.SqrMagnitude(current - desired) <= 0.000001f && _velocity.sqrMagnitude <= 0.000001f)
            {
                if (current != desired)
                    targetCamera.transform.position = desired;
                _velocity = Vector3.zero;
                return;
            }
            targetCamera.transform.position = Vector3.SmoothDamp(
                current,
                desired,
                ref _velocity,
                followSmoothTime,
                Mathf.Infinity,
                unscaledDeltaTime);
        }

        public void Snap(Vector3 worldPosition)
        {
            if (targetCamera == null)
                return;
            targetCamera.transform.position = new Vector3(
                worldPosition.x,
                worldPosition.y,
                targetCamera.transform.position.z);
            _velocity = Vector3.zero;
        }
    }
}
