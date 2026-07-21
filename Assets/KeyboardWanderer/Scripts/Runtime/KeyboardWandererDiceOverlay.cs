using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace KeyboardWanderer.Runtime
{
    /// <summary>
    /// Presents the existing 3D D20 inside a fixed-size screen-space UI layer. The die is
    /// rendered by an isolated camera into a transparent RenderTexture, so neither its
    /// size nor its rotation can affect the gameplay camera or world presentation.
    /// </summary>
    public sealed class KeyboardWandererDiceOverlay : MonoBehaviour
    {
        private const float ResultHoldSeconds = 0.4f;
        private const int OverlaySortingOrder = 4500;
        private const int TextureSize = 512;
        private static readonly Vector3 CaptureOrigin = new Vector3(10000f, 10000f, 0f);

        private GameObject _prefab;
        private GameObject _uiRoot;
        private GameObject _instance;
        private Camera _captureCamera;
        private RenderTexture _renderTexture;
        private IcosahedronDice _dice;

        public bool IsVisible => _uiRoot != null && _uiRoot.activeSelf;
        public bool IsRolling => _dice != null && _dice.IsRolling;

        public void Configure(Camera targetCamera, GameObject dicePrefab)
        {
            // Kept in the signature for the existing composition API. The gameplay
            // camera is deliberately not used by this screen-space presentation.
            _prefab = dicePrefab;
        }

        public void BeginRoll()
        {
            if (!EnsureInstance()) return;
            _uiRoot.SetActive(true);
            _instance.SetActive(true);
            FitCaptureCamera();
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
            if (_uiRoot != null && _instance != null && _dice != null) return true;
            if (_prefab == null) return false;

            BuildUiLayer();

            _instance = Instantiate(_prefab, _uiRoot.transform);
            _instance.name = "Pending D20 Capture";
            _instance.transform.position = CaptureOrigin;
            _instance.transform.localScale = Vector3.one;
            _dice = _instance.GetComponent<IcosahedronDice>();
            if (_dice == null) _dice = _instance.GetComponentInChildren<IcosahedronDice>(true);
            if (_dice == null)
            {
                Destroy(_uiRoot);
                _uiRoot = null;
                _instance = null;
                return false;
            }

            _dice.SetTargetCamera(_captureCamera);
            _uiRoot.SetActive(false);
            return true;
        }

        private void BuildUiLayer()
        {
            _uiRoot = new GameObject("D20 UI Overlay");
            _uiRoot.transform.SetParent(transform, false);

            _renderTexture = new RenderTexture(TextureSize, TextureSize, 24, RenderTextureFormat.ARGB32)
            {
                name = "D20 UI Render Texture",
                antiAliasing = 4,
                useMipMap = false,
                autoGenerateMips = false
            };
            _renderTexture.Create();

            var cameraObject = new GameObject("D20 UI Camera", typeof(Camera));
            cameraObject.transform.SetParent(_uiRoot.transform, false);
            _captureCamera = cameraObject.GetComponent<Camera>();
            _captureCamera.orthographic = true;
            _captureCamera.clearFlags = CameraClearFlags.SolidColor;
            _captureCamera.backgroundColor = Color.clear;
            _captureCamera.nearClipPlane = 0.1f;
            _captureCamera.farClipPlane = 50f;
            _captureCamera.allowHDR = false;
            _captureCamera.allowMSAA = true;
            _captureCamera.targetTexture = _renderTexture;

            var canvasObject = new GameObject("D20 Overlay Canvas",
                typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(_uiRoot.transform, false);
            Canvas canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = OverlaySortingOrder;
            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1440f, 900f);
            scaler.matchWidthOrHeight = 0.5f;

            var imageObject = new GameObject("Rolling D20", typeof(RectTransform), typeof(RawImage));
            imageObject.transform.SetParent(canvasObject.transform, false);
            RectTransform rect = imageObject.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = new Vector2(0f, 45f);
            rect.sizeDelta = new Vector2(190f, 190f);
            RawImage image = imageObject.GetComponent<RawImage>();
            image.texture = _renderTexture;
            image.color = Color.white;
            image.raycastTarget = false;
        }

        private void FitCaptureCamera()
        {
            if (_captureCamera == null || _instance == null) return;
            Renderer[] renderers = _instance.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) return;

            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
                bounds.Encapsulate(renderers[i].bounds);

            float halfSize = Mathf.Max(bounds.extents.x, bounds.extents.y, 0.001f);
            _captureCamera.orthographicSize = halfSize * 1.25f;
            _captureCamera.transform.position = new Vector3(bounds.center.x, bounds.center.y, bounds.min.z - 10f);
            _captureCamera.transform.rotation = Quaternion.identity;
        }

        private void LateUpdate()
        {
            if (IsVisible)
                FitCaptureCamera();
        }

        private void Hide()
        {
            if (_uiRoot != null) _uiRoot.SetActive(false);
        }

        private void OnDisable()
        {
            CancelAndHide();
        }

        private void OnDestroy()
        {
            if (_renderTexture == null) return;
            _renderTexture.Release();
            Destroy(_renderTexture);
            _renderTexture = null;
        }
    }
}
