using TMPro;
using KeyboardWanderer.Presentation;
using UnityEngine;
using UnityEngine.UI;

namespace KeyboardWanderer.Demo
{
    /// <summary>
    /// 미니맵 패널 오브젝트가 소유하는 화면 컴포넌트입니다.
    /// 맵 텍스처 생성 시점은 <see cref="MinimapPresenter"/>가 결정하고,
    /// 이 컴포넌트는 생성된 Sprite와 상태 문구를 표시합니다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class KeyboardWandererMinimapView : MonoBehaviour
    {
        [SerializeField] private Image mapImage;
        [SerializeField] private TMP_Text placeholderText;
        [SerializeField] private TMP_Text statusText;

        public bool IsReady => mapImage != null && placeholderText != null && statusText != null;

        public void Present(Sprite sprite, string status)
        {
            if (mapImage != null)
            {
                if (mapImage.sprite != sprite)
                    mapImage.sprite = sprite;
                mapImage.enabled = sprite != null;
            }
            if (placeholderText != null)
                placeholderText.gameObject.SetActive(sprite == null);
            if (statusText != null && statusText.text != (status ?? string.Empty))
                statusText.text = status ?? string.Empty;
        }

#if UNITY_EDITOR
        public void Configure(Image map, TMP_Text placeholder, TMP_Text status)
        {
            mapImage = map;
            placeholderText = placeholder;
            statusText = status;
            UnityEditor.EditorUtility.SetDirty(this);
        }
#endif
    }
}
