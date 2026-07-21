using System.Collections;
using UnityEngine;

namespace KeyboardWanderer.Runtime
{
    /// <summary>
    /// Camera-space presentation for the existing 3D D20. The client never chooses the
    /// mechanical result: it spins while the request is pending, then lands on the value
    /// returned by the authoritative turn response.
    /// </summary>
    public sealed class KeyboardWandererDiceOverlay : MonoBehaviour
    {
        private const float ResultHoldSeconds = 0.4f;
        private const float TargetViewportDiameter = 0.16f;
        private const float CameraDepth = 8f;
        private static readonly Vector2 ViewportPosition = new Vector2(0.5f, 0.58f);
        private Camera _camera;
        private GameObject _prefab;
        private GameObject _instance;
        private IcosahedronDice _dice;
        private float _unscaledDiameter;

        public bool IsVisible => _instance != null && _instance.activeSelf;
        public bool IsRolling => _dice != null && _dice.IsRolling;

        public void Configure(Camera targetCamera, GameObject dicePrefab)
        {
            _camera = targetCamera;
            _prefab = dicePrefab;
        }

        public void BeginRoll()
        {
            if (!EnsureInstance()) return;
            PositionInCamera();
            _instance.SetActive(true);
            _dice.BeginPendingRoll();
        }

        public IEnumerator ResolveAndHide(int authoritativeD20)
        {
            if (_dice == null || !IsVisible)
                yield break;
            if (!IsD20Result(authoritativeD20))
            {
                CancelAndHide();
                yield break;
            }

            _dice.ResolveTo(authoritativeD20);
            while (_dice != null && _dice.IsRolling)
                yield return null;
            yield return new WaitForSecondsRealtime(ResultHoldSeconds);
            Hide();
        }

        public void CancelAndHide()
        {
            if (_dice != null) _dice.CancelRoll();
            Hide();
        }

        public static bool IsD20Result(int value) => value >= 1 && value <= 20;

        private bool EnsureInstance()
        {
            if (_instance != null && _dice != null) return true;
            if (_camera == null || _prefab == null) return false;

            // Keep the die out of the camera hierarchy. The overlay follows the
            // camera position, but rotating the die must never have a transform path
            // that can be mistaken for (or coupled to) camera rotation.
            _instance = Instantiate(_prefab);
            _instance.name = "Pending D20 Overlay";
            _instance.transform.localScale = Vector3.one;
            _dice = _instance.GetComponent<IcosahedronDice>();
            if (_dice == null) _dice = _instance.GetComponentInChildren<IcosahedronDice>(true);
            if (_dice == null)
            {
                Destroy(_instance);
                _instance = null;
                return false;
            }
            _unscaledDiameter = MeasureRendererDiameter(_instance);
            _instance.SetActive(false);
            return true;
        }

        private void PositionInCamera()
        {
            if (_instance == null || _camera == null) return;

            float visibleHeight = _camera.orthographic
                ? _camera.orthographicSize * 2f
                : 2f * CameraDepth * Mathf.Tan(_camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
            float visibleWidth = visibleHeight * _camera.aspect;
            float scale = CalculateViewportScale(visibleWidth, visibleHeight, _unscaledDiameter);
            _instance.transform.localScale = Vector3.one * scale;
            _instance.transform.position = _camera.ViewportToWorldPoint(
                new Vector3(ViewportPosition.x, ViewportPosition.y, CameraDepth));
        }

        private static float CalculateViewportScale(float visibleWidth, float visibleHeight, float unscaledDiameter)
        {
            if (visibleWidth <= 0f || visibleHeight <= 0f || unscaledDiameter <= 0f)
                return 1f;
            return Mathf.Min(visibleWidth, visibleHeight) * TargetViewportDiameter / unscaledDiameter;
        }

        private static float MeasureRendererDiameter(GameObject instance)
        {
            Renderer[] renderers = instance.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) return 1f;

            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
                bounds.Encapsulate(renderers[i].bounds);
            // Use the enclosing-box diagonal so the die remains within the target
            // viewport diameter even when rotation enlarges its screen-space AABB.
            return Mathf.Max(bounds.size.magnitude, 0.001f);
        }

        private void LateUpdate()
        {
            if (IsVisible && _camera != null)
                PositionInCamera();
        }

        private void Hide()
        {
            if (_instance != null) _instance.SetActive(false);
        }

        private void OnDisable()
        {
            CancelAndHide();
        }
    }
}
