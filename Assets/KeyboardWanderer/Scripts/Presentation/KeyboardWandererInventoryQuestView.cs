using System;
using System.Collections.Generic;
using System.Text;
using KeyboardWanderer.Presentation;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace KeyboardWanderer.Demo
{
    /// <summary>
    /// 서버가 확정한 소지품과 퀘스트를 표시하는 런타임 오버레이다.
    /// 이름은 LLM이 만들 수 있지만 아이콘과 장비 분류는 bounded item kind만 사용한다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class KeyboardWandererInventoryQuestView : MonoBehaviour
    {
        private enum Page { None, Inventory, Quests }

        private GameObject _overlay;
        private RectTransform _content;
        private TMP_Text _heading;
        private TMP_Text _empty;
        private TMP_FontAsset _font;
        private NinjaAdventureAssetManifest _manifest;
        private Action<string> _itemSelected;
        private Action<bool> _visibilityChanged;
        private IReadOnlyList<RunPresentationItem> _items = Array.Empty<RunPresentationItem>();
        private IReadOnlyList<RunPresentationQuest> _quests = Array.Empty<RunPresentationQuest>();
        private string _signature = string.Empty;
        private Page _page;

        public bool IsOpen => _page != Page.None && _overlay != null && _overlay.activeSelf;

        public void Initialize(Transform gameHud, TMP_FontAsset font,
            NinjaAdventureAssetManifest manifest, Action<string> itemSelected, Action<bool> visibilityChanged)
        {
            if (_overlay != null || gameHud == null) return;
            _font = font;
            _manifest = manifest;
            _itemSelected = itemSelected;
            _visibilityChanged = visibilityChanged;
            Build(gameHud);
        }

        public void Present(IReadOnlyList<RunPresentationItem> items,
            IReadOnlyList<RunPresentationQuest> quests)
        {
            _items = items ?? Array.Empty<RunPresentationItem>();
            _quests = quests ?? Array.Empty<RunPresentationQuest>();
            string signature = Signature(_items, _quests);
            if (_signature == signature) return;
            _signature = signature;
            if (IsOpen) Rebuild();
        }

        public bool ToggleInventory()
        {
            SetPage(_page == Page.Inventory ? Page.None : Page.Inventory);
            return IsOpen;
        }

        public bool ToggleQuests()
        {
            SetPage(_page == Page.Quests ? Page.None : Page.Quests);
            return IsOpen;
        }

        public bool Close()
        {
            if (!IsOpen) return false;
            SetPage(Page.None);
            return true;
        }

        private void SetPage(Page page)
        {
            _page = page;
            if (_overlay == null) return;
            _overlay.SetActive(page != Page.None);
            _visibilityChanged?.Invoke(page != Page.None);
            if (page != Page.None)
            {
                _overlay.transform.SetAsLastSibling();
                Rebuild();
            }
        }

        private void Build(Transform parent)
        {
            _overlay = new GameObject("Inventory Quest Overlay", typeof(RectTransform), typeof(Image));
            _overlay.transform.SetParent(parent, false);
            RectTransform overlayRect = (RectTransform)_overlay.transform;
            overlayRect.anchorMin = Vector2.zero;
            overlayRect.anchorMax = Vector2.one;
            overlayRect.offsetMin = overlayRect.offsetMax = Vector2.zero;
            _overlay.GetComponent<Image>().color = new Color(0.015f, 0.02f, 0.025f, 0.78f);

            GameObject window = CreateImage(_overlay.transform, "Window",
                new Vector2(0.16f, 0.12f), new Vector2(0.84f, 0.88f),
                _manifest != null ? _manifest.WoodPanel : null,
                new Color(0.18f, 0.12f, 0.07f, 0.99f));
            _heading = CreateText(window.transform, "Heading", "소지품",
                new Vector2(0.05f, 0.89f), new Vector2(0.78f, 0.98f), 24f,
                TextAlignmentOptions.MidlineLeft, new Color(1f, 0.87f, 0.54f));
            TMP_Text shortcut = CreateText(window.transform, "Shortcut", "I 소지품   Q 퀘스트",
                new Vector2(0.55f, 0.90f), new Vector2(0.88f, 0.97f), 14f,
                TextAlignmentOptions.MidlineRight, new Color(0.86f, 0.82f, 0.72f));
            shortcut.enableAutoSizing = true;
            shortcut.fontSizeMin = 10f;
            shortcut.fontSizeMax = 14f;

            GameObject closeObject = CreateImage(window.transform, "Close",
                new Vector2(0.90f, 0.90f), new Vector2(0.97f, 0.97f),
                _manifest != null ? _manifest.WoodButtonNormal : null,
                new Color(0.38f, 0.18f, 0.12f, 1f));
            Button close = closeObject.AddComponent<Button>();
            close.onClick.AddListener(() => SetPage(Page.None));
            CreateText(closeObject.transform, "Label", "×", Vector2.zero, Vector2.one, 22f,
                TextAlignmentOptions.Center, Color.white);

            GameObject content = CreateImage(window.transform, "Content",
                new Vector2(0.045f, 0.065f), new Vector2(0.955f, 0.875f),
                _manifest != null ? _manifest.WoodPanelInterior : null,
                new Color(0.055f, 0.052f, 0.045f, 0.98f));
            _content = (RectTransform)content.transform;
            _empty = CreateText(content.transform, "Empty", string.Empty,
                new Vector2(0.08f, 0.35f), new Vector2(0.92f, 0.65f), 18f,
                TextAlignmentOptions.Center, new Color(0.78f, 0.76f, 0.68f));
            _overlay.SetActive(false);
        }

        private void Rebuild()
        {
            if (_content == null) return;
            for (int i = _content.childCount - 1; i >= 0; i--)
            {
                Transform child = _content.GetChild(i);
                if (child != _empty.transform) Destroy(child.gameObject);
            }
            if (_page == Page.Inventory) BuildInventory();
            else if (_page == Page.Quests) BuildQuests();
        }

        private void BuildInventory()
        {
            _heading.text = "INVENTORY · 장비와 소지품";
            _empty.gameObject.SetActive(_items.Count == 0);
            _empty.text = "아직 획득한 아이템이 없습니다.";
            if (_items.Count == 0) return;

            CreateSectionLabel("장비 · 중요품", 0.92f, 0.99f);
            int equipmentSlot = 0;
            for (int i = 0; i < _items.Count && equipmentSlot < 4; i++)
            {
                RunPresentationItem item = _items[i];
                if (item == null || !item.IsEquipment) continue;
                float left = 0.02f + equipmentSlot * 0.245f;
                CreateItemTile(item, new Vector2(left, 0.68f), new Vector2(left + 0.225f, 0.91f), true);
                equipmentSlot++;
            }
            for (; equipmentSlot < 4; equipmentSlot++)
            {
                float left = 0.02f + equipmentSlot * 0.245f;
                GameObject slot = CreateImage(_content, "Empty Equipment Slot",
                    new Vector2(left, 0.68f), new Vector2(left + 0.225f, 0.91f),
                    _manifest != null ? _manifest.WoodInventoryCell : null,
                    new Color(0.22f, 0.19f, 0.14f, 0.62f));
                CreateText(slot.transform, "Empty", "비어 있음", new Vector2(0.08f, 0.15f),
                    new Vector2(0.92f, 0.85f), 12f, TextAlignmentOptions.Center,
                    new Color(0.55f, 0.52f, 0.46f));
            }

            CreateSectionLabel("가방 · 선택하면 대화 입력에 이름이 들어갑니다", 0.59f, 0.66f);
            int bagIndex = 0;
            for (int i = 0; i < _items.Count && bagIndex < 18; i++)
            {
                RunPresentationItem item = _items[i];
                if (item == null || item.IsEquipment) continue;
                int row = bagIndex / 6;
                int column = bagIndex % 6;
                float left = 0.02f + column * 0.163f;
                float top = 0.57f - row * 0.18f;
                CreateItemTile(item, new Vector2(left, top - 0.16f),
                    new Vector2(left + 0.145f, top), false);
                bagIndex++;
            }
        }

        private void BuildQuests()
        {
            _heading.text = "QUEST · 진행 중인 이야기";
            _empty.gameObject.SetActive(_quests.Count == 0);
            _empty.text = "현재 등록된 퀘스트가 없습니다.";
            for (int i = 0; i < _quests.Count && i < 6; i++)
            {
                RunPresentationQuest quest = _quests[i];
                if (quest == null) continue;
                float top = 0.97f - i * 0.155f;
                GameObject row = CreateImage(_content, "Quest " + i,
                    new Vector2(0.025f, top - 0.135f), new Vector2(0.975f, top),
                    _manifest != null ? _manifest.WoodInventoryCell : null,
                    new Color(0.16f, 0.14f, 0.11f, 0.98f));
                string step = string.IsNullOrWhiteSpace(quest.CurrentStep) ? string.Empty :
                    "\n현재 · " + StepLabel(quest.CurrentStep);
                TMP_Text title = CreateText(row.transform, "Title",
                    "[진행] " + quest.Title + step,
                    new Vector2(0.025f, 0.48f), new Vector2(0.38f, 0.95f), 15f,
                    TextAlignmentOptions.MidlineLeft, new Color(1f, 0.83f, 0.48f));
                title.enableAutoSizing = true;
                title.fontSizeMin = 10f;
                title.fontSizeMax = 15f;
                TMP_Text summary = CreateText(row.transform, "Summary", quest.Summary,
                    new Vector2(0.40f, 0.10f), new Vector2(0.975f, 0.90f), 14f,
                    TextAlignmentOptions.MidlineLeft, new Color(0.91f, 0.89f, 0.82f));
                summary.enableAutoSizing = true;
                summary.fontSizeMin = 10f;
                summary.fontSizeMax = 14f;
            }
        }

        private void CreateSectionLabel(string value, float bottom, float top)
        {
            CreateText(_content, "Section", value, new Vector2(0.02f, bottom),
                new Vector2(0.98f, top), 14f, TextAlignmentOptions.MidlineLeft,
                new Color(0.94f, 0.78f, 0.42f));
        }

        private void CreateItemTile(RunPresentationItem item, Vector2 min, Vector2 max, bool equipment)
        {
            GameObject tile = CreateImage(_content, "Item " + item.Id, min, max,
                _manifest != null ? _manifest.WoodInventoryCell : null,
                equipment ? new Color(0.31f, 0.23f, 0.12f, 1f) : new Color(0.18f, 0.16f, 0.12f, 1f));
            Button button = tile.AddComponent<Button>();
            RunPresentationItem captured = item;
            button.onClick.AddListener(() => _itemSelected?.Invoke(captured.Name));

            GameObject iconObject = CreateImage(tile.transform, "Icon", new Vector2(0.05f, 0.25f),
                new Vector2(0.34f, 0.83f), IconForKind(_manifest, item.Kind), Color.white);
            iconObject.GetComponent<Image>().preserveAspect = true;
            string quantity = item.Quantity > 1 ? " ×" + item.Quantity : string.Empty;
            TMP_Text label = CreateText(tile.transform, "Name", item.Name + quantity,
                new Vector2(0.36f, 0.40f), new Vector2(0.97f, 0.90f), 13f,
                TextAlignmentOptions.MidlineLeft, Color.white);
            label.enableAutoSizing = true;
            label.fontSizeMin = 8f;
            label.fontSizeMax = 13f;
            TMP_Text kind = CreateText(tile.transform, "Kind", KindLabel(item.Kind),
                new Vector2(0.36f, 0.08f), new Vector2(0.97f, 0.40f), 10f,
                TextAlignmentOptions.MidlineLeft, new Color(0.72f, 0.68f, 0.58f));
            kind.enableAutoSizing = true;
            kind.fontSizeMin = 8f;
            kind.fontSizeMax = 10f;
        }

        public static Sprite IconForKind(NinjaAdventureAssetManifest manifest, string kind)
        {
            if (manifest == null) return null;
            switch ((kind ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "key_item": return manifest.KeyItemIcon != null ? manifest.KeyItemIcon : manifest.RuneBook;
                case "tool": return manifest.ToolItemIcon != null ? manifest.ToolItemIcon : manifest.InteractIcon;
                case "consumable": return manifest.ConsumableItemIcon != null ? manifest.ConsumableItemIcon : manifest.HeartIcon;
                case "material": return manifest.MaterialItemIcon != null ? manifest.MaterialItemIcon : manifest.Crate;
                case "salvage": return manifest.SalvageItemIcon != null ? manifest.SalvageItemIcon : manifest.CopyIcon;
                default: return manifest.GenericItemIcon != null ? manifest.GenericItemIcon : manifest.RuneBook;
            }
        }

        private static string KindLabel(string kind)
        {
            switch ((kind ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "key_item": return "중요품";
                case "tool": return "도구";
                case "consumable": return "소모품";
                case "material": return "재료";
                case "salvage": return "파편";
                default: return "아이템";
            }
        }

        private static string StepLabel(string step)
        {
            switch ((step ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "open":
                case "opening": return "서막";
                case "discover": return "단서 발견";
                case "advanced": return "다음 단계";
                case "completed": return "완료";
                default: return step;
            }
        }

        private GameObject CreateImage(Transform parent, string name, Vector2 min, Vector2 max,
            Sprite sprite, Color color)
        {
            var item = new GameObject(name, typeof(RectTransform), typeof(Image));
            item.transform.SetParent(parent, false);
            RectTransform rect = (RectTransform)item.transform;
            rect.anchorMin = min;
            rect.anchorMax = max;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            Image image = item.GetComponent<Image>();
            image.sprite = sprite;
            image.type = sprite != null && sprite.border.sqrMagnitude > 0f ? Image.Type.Sliced : Image.Type.Simple;
            image.color = color;
            return item;
        }

        private TMP_Text CreateText(Transform parent, string name, string value, Vector2 min, Vector2 max,
            float size, TextAlignmentOptions alignment, Color color)
        {
            var item = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            item.transform.SetParent(parent, false);
            RectTransform rect = (RectTransform)item.transform;
            rect.anchorMin = min;
            rect.anchorMax = max;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            TMP_Text text = item.GetComponent<TMP_Text>();
            if (_font != null) text.font = _font;
            text.text = value ?? string.Empty;
            text.fontSize = size;
            text.alignment = alignment;
            text.color = color;
            text.enableWordWrapping = true;
            text.raycastTarget = false;
            return text;
        }

        private static string Signature(IReadOnlyList<RunPresentationItem> items,
            IReadOnlyList<RunPresentationQuest> quests)
        {
            var value = new StringBuilder();
            for (int i = 0; i < items.Count; i++)
                value.Append(items[i]?.Id).Append(':').Append(items[i]?.Quantity).Append('|');
            value.Append('#');
            for (int i = 0; i < quests.Count; i++)
                value.Append(quests[i]?.Id).Append(':').Append(quests[i]?.CurrentStep).Append('|');
            return value.ToString();
        }
    }
}
