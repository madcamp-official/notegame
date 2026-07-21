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
        private Camera _camera;
        private GameObject _prefab;
        private GameObject _instance;
        private IcosahedronDice _dice;

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

            _instance = Instantiate(_prefab, _camera.transform);
            _instance.name = "Pending D20 Overlay";
            _instance.transform.localScale = Vector3.one * 0.36f;
            _dice = _instance.GetComponent<IcosahedronDice>();
            if (_dice == null) _dice = _instance.GetComponentInChildren<IcosahedronDice>(true);
            if (_dice == null)
            {
                Destroy(_instance);
                _instance = null;
                return false;
            }
            _instance.SetActive(false);
            return true;
        }

        private void PositionInCamera()
        {
            _instance.transform.SetParent(_camera.transform, false);
            _instance.transform.localPosition = new Vector3(0f, 0.35f, 8f);
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
