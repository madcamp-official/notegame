using System.Collections.Generic;
using KeyboardWanderer.Presentation;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace KeyboardWanderer.Demo
{
    /// <summary>
    /// Game HUD에 상시 노출되는 소지품 요약 패널이다. 최대 <see cref="SlotCount"/>개의
    /// 아이템 아이콘만 보여주며, 전체 목록은 여전히 <see cref="KeyboardWandererInventoryQuestView"/>
    /// 오버레이가 담당한다. 아이콘은 <see cref="KeyboardWandererInventoryQuestView.IconForKind"/>가
    /// 정한 bounded item kind 매핑을 그대로 재사용한다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class KeyboardWandererInventoryHudView : MonoBehaviour
    {
        private const int SlotCount = 4;

        [SerializeField] private NinjaAdventureAssetManifest assetManifest;
        [SerializeField] private Image[] slotIcons = new Image[SlotCount];
        [SerializeField] private TMP_Text[] slotQuantities = new TMP_Text[SlotCount];
        [SerializeField] private TMP_Text emptyText;

        public bool IsReady => assetManifest != null && emptyText != null &&
                               slotIcons != null && slotIcons.Length == SlotCount &&
                               slotQuantities != null && slotQuantities.Length == SlotCount &&
                               System.Array.TrueForAll(slotIcons, icon => icon != null) &&
                               System.Array.TrueForAll(slotQuantities, text => text != null);

        public void Present(IReadOnlyList<RunPresentationItem> items)
        {
            if (!IsReady)
                return;
            int count = items?.Count ?? 0;
            emptyText.gameObject.SetActive(count == 0);
            for (int i = 0; i < SlotCount; i++)
            {
                RunPresentationItem item = i < count ? items[i] : null;
                Sprite icon = item != null ? KeyboardWandererInventoryQuestView.IconForKind(assetManifest, item.Kind) : null;
                slotIcons[i].sprite = icon;
                slotIcons[i].enabled = icon != null;
                bool showQuantity = item != null && item.Quantity > 1;
                slotQuantities[i].gameObject.SetActive(showQuantity);
                if (showQuantity)
                    slotQuantities[i].text = "×" + item.Quantity;
            }
        }

#if UNITY_EDITOR
        public void Configure(NinjaAdventureAssetManifest manifest, Image[] icons, TMP_Text[] quantities, TMP_Text empty)
        {
            assetManifest = manifest;
            slotIcons = icons;
            slotQuantities = quantities;
            emptyText = empty;
            UnityEditor.EditorUtility.SetDirty(this);
        }
#endif
    }
}
