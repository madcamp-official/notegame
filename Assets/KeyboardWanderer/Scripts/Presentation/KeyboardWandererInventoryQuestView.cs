using System;
using System.Collections.Generic;
using System.Text;
using KeyboardWanderer.Presentation;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
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
        private ScrollRect _listScroll;
        private RectTransform _listContent;
        private TMP_Text _heading;
        private TMP_Text _empty;
        private TMP_Text _detailTitle;
        private TMP_Text _detailBody;
        private ScrollRect _detailScroll;
        private Button _detailAction;
        private TMP_Text _detailActionLabel;
        private TMP_FontAsset _font;
        private NinjaAdventureAssetManifest _manifest;
        private Action<string> _itemSelected;
        private Action<bool> _visibilityChanged;
        private IReadOnlyList<RunPresentationItem> _items = Array.Empty<RunPresentationItem>();
        private IReadOnlyList<RunPresentationQuest> _quests = Array.Empty<RunPresentationQuest>();
        private string _signature = string.Empty;
        private Page _page;
        private RunPresentationItem _selectedItem;
        private readonly Dictionary<RunPresentationItem, Image> _itemRows =
            new Dictionary<RunPresentationItem, Image>();

        public bool IsOpen => _page != Page.None && _overlay != null && _overlay.activeSelf;

        public void Initialize(Transform gameHud, TMP_FontAsset font,
            NinjaAdventureAssetManifest manifest, Action<string> itemSelected, Action<bool> visibilityChanged)
        {
            if (_overlay != null || gameHud == null) return;
            // The controller initializes this overlay before SceneUI.Bind swaps authored
            // labels. Keep the dynamic-row factory on the shared non-persistent font too;
            // otherwise arbitrary server item / quest names would hit the Static Resources
            // template and render missing glyphs.
            _font = KeyboardWandererRuntimeFontProvider.Get(font);
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
                new Vector2(0.08f, 0.35f), new Vector2(0.60f, 0.65f), 18f,
                TextAlignmentOptions.Center, new Color(0.78f, 0.76f, 0.68f));
            BuildListAndDetailPanels(content.transform);
            _empty.transform.SetAsLastSibling();
            _overlay.SetActive(false);
        }

        private void BuildListAndDetailPanels(Transform parent)
        {
            GameObject scrollObject = CreateImage(parent, "Inventory Quest Scroll",
                new Vector2(0.025f, 0.035f), new Vector2(0.63f, 0.965f), null,
                new Color(0.025f, 0.027f, 0.026f, 0.88f));
            _listScroll = scrollObject.AddComponent<ScrollRect>();
            GameObject viewportObject = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D));
            viewportObject.transform.SetParent(scrollObject.transform, false);
            RectTransform viewport = (RectTransform)viewportObject.transform;
            viewport.anchorMin = Vector2.zero;
            viewport.anchorMax = Vector2.one;
            viewport.offsetMin = new Vector2(5f, 5f);
            viewport.offsetMax = new Vector2(-5f, -5f);

            GameObject listObject = new GameObject("List Content", typeof(RectTransform),
                typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            listObject.transform.SetParent(viewportObject.transform, false);
            _listContent = (RectTransform)listObject.transform;
            _listContent.anchorMin = new Vector2(0f, 1f);
            _listContent.anchorMax = new Vector2(1f, 1f);
            _listContent.pivot = new Vector2(0.5f, 1f);
            _listContent.anchoredPosition = Vector2.zero;
            _listContent.sizeDelta = Vector2.zero;
            VerticalLayoutGroup layout = listObject.GetComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(6, 6, 6, 6);
            layout.spacing = 7f;
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            ContentSizeFitter fitter = listObject.GetComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            _listScroll.viewport = viewport;
            _listScroll.content = _listContent;
            _listScroll.horizontal = false;
            _listScroll.vertical = true;
            _listScroll.movementType = ScrollRect.MovementType.Clamped;
            _listScroll.scrollSensitivity = 38f;

            GameObject detail = CreateImage(parent, "Detail Panel",
                new Vector2(0.65f, 0.035f), new Vector2(0.975f, 0.965f),
                _manifest != null ? _manifest.WoodInventoryCell : null,
                new Color(0.13f, 0.115f, 0.085f, 0.98f));
            _detailTitle = CreateText(detail.transform, "Detail Title", "항목을 선택하세요",
                new Vector2(0.06f, 0.77f), new Vector2(0.94f, 0.95f), 18f,
                TextAlignmentOptions.MidlineLeft, new Color(1f, 0.84f, 0.48f));
            _detailTitle.enableAutoSizing = true;
            _detailTitle.fontSizeMin = 11f;
            _detailTitle.fontSizeMax = 18f;
            _detailTitle.overflowMode = TextOverflowModes.Overflow;

            GameObject detailScrollObject = CreateImage(detail.transform, "Detail Scroll",
                new Vector2(0.055f, 0.23f), new Vector2(0.945f, 0.74f), null,
                new Color(0.035f, 0.035f, 0.03f, 0.72f));
            _detailScroll = detailScrollObject.AddComponent<ScrollRect>();
            GameObject detailViewportObject = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D));
            detailViewportObject.transform.SetParent(detailScrollObject.transform, false);
            RectTransform detailViewport = (RectTransform)detailViewportObject.transform;
            detailViewport.anchorMin = Vector2.zero;
            detailViewport.anchorMax = Vector2.one;
            detailViewport.offsetMin = new Vector2(8f, 7f);
            detailViewport.offsetMax = new Vector2(-8f, -7f);
            GameObject detailBodyObject = new GameObject("Detail Body", typeof(RectTransform),
                typeof(TextMeshProUGUI), typeof(ContentSizeFitter));
            detailBodyObject.transform.SetParent(detailViewportObject.transform, false);
            RectTransform detailBodyRect = (RectTransform)detailBodyObject.transform;
            detailBodyRect.anchorMin = new Vector2(0f, 1f);
            detailBodyRect.anchorMax = new Vector2(1f, 1f);
            detailBodyRect.pivot = new Vector2(0.5f, 1f);
            detailBodyRect.anchoredPosition = Vector2.zero;
            detailBodyRect.sizeDelta = Vector2.zero;
            _detailBody = detailBodyObject.GetComponent<TMP_Text>();
            if (_font != null) _detailBody.font = _font;
            _detailBody.fontSize = 14f;
            _detailBody.color = new Color(0.92f, 0.89f, 0.80f);
            _detailBody.alignment = TextAlignmentOptions.TopLeft;
            _detailBody.textWrappingMode = TextWrappingModes.Normal;
            _detailBody.overflowMode = TextOverflowModes.Overflow;
            _detailBody.raycastTarget = false;
            ContentSizeFitter detailFitter = detailBodyObject.GetComponent<ContentSizeFitter>();
            detailFitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            detailFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            _detailScroll.viewport = detailViewport;
            _detailScroll.content = detailBodyRect;
            _detailScroll.horizontal = false;
            _detailScroll.vertical = true;
            _detailScroll.movementType = ScrollRect.MovementType.Clamped;
            _detailScroll.scrollSensitivity = 30f;

            GameObject actionObject = CreateImage(detail.transform, "Insert Into Dialogue",
                new Vector2(0.08f, 0.065f), new Vector2(0.92f, 0.18f),
                _manifest != null ? _manifest.WoodButtonNormal : null,
                new Color(0.72f, 0.48f, 0.18f, 1f));
            _detailAction = actionObject.AddComponent<Button>();
            _detailAction.onClick.AddListener(InsertSelectedItem);
            _detailActionLabel = CreateText(actionObject.transform, "Label", "대화에 넣기",
                Vector2.zero, Vector2.one, 15f, TextAlignmentOptions.Center,
                new Color(0.10f, 0.07f, 0.03f));
            ResetDetail();
        }

        private void Rebuild()
        {
            if (_listContent == null) return;
            for (int i = _listContent.childCount - 1; i >= 0; i--)
            {
                Transform child = _listContent.GetChild(i);
                child.gameObject.SetActive(false);
                Destroy(child.gameObject);
            }
            _itemRows.Clear();
            _selectedItem = null;
            ResetDetail();
            if (_page == Page.Inventory) BuildInventory();
            else if (_page == Page.Quests) BuildQuests();
            if (_listScroll != null)
                _listScroll.verticalNormalizedPosition = 1f;
        }

        private void BuildInventory()
        {
            _heading.text = "INVENTORY · 장비와 소지품";
            _empty.gameObject.SetActive(_items.Count == 0);
            _empty.text = "아직 획득한 아이템이 없습니다.";
            if (_items.Count == 0) return;

            CreateSectionRow("장비 · 중요품");
            RunPresentationItem firstItem = null;
            int equipmentCount = 0;
            for (int i = 0; i < _items.Count; i++)
            {
                RunPresentationItem item = _items[i];
                if (item == null || !item.IsEquipment) continue;
                CreateItemRow(item, true);
                firstItem ??= item;
                equipmentCount++;
            }
            if (equipmentCount == 0)
                CreateInfoRow("장비 또는 중요품이 없습니다.");

            CreateSectionRow("가방 · 선택 후 상세에서 ‘대화에 넣기’");
            int bagCount = 0;
            for (int i = 0; i < _items.Count; i++)
            {
                RunPresentationItem item = _items[i];
                if (item == null || item.IsEquipment) continue;
                CreateItemRow(item, false);
                firstItem ??= item;
                bagCount++;
            }
            if (bagCount == 0)
                CreateInfoRow("가방에 든 일반 아이템이 없습니다.");
            if (firstItem != null)
                SelectItem(firstItem);
        }

        private void BuildQuests()
        {
            _heading.text = "QUEST · 전체 이야기 기록";
            _empty.gameObject.SetActive(_quests.Count == 0);
            _empty.text = "현재 등록된 퀘스트가 없습니다.";
            RunPresentationQuest firstQuest = null;
            for (int i = 0; i < _quests.Count; i++)
            {
                RunPresentationQuest quest = _quests[i];
                if (quest == null) continue;
                CreateQuestRow(quest, i);
                firstQuest ??= quest;
            }
            if (firstQuest != null)
                SelectQuest(firstQuest);
        }

        private void CreateSectionRow(string value)
        {
            GameObject row = CreateLayoutRow("Section", 32f, new Color(0.08f, 0.065f, 0.04f, 0.96f));
            CreateText(row.transform, "Label", value, new Vector2(0.025f, 0f),
                new Vector2(0.975f, 1f), 14f, TextAlignmentOptions.MidlineLeft,
                new Color(0.94f, 0.78f, 0.42f));
        }

        private void CreateInfoRow(string value)
        {
            GameObject row = CreateLayoutRow("Info", 48f, new Color(0.10f, 0.095f, 0.08f, 0.8f));
            CreateText(row.transform, "Label", value, new Vector2(0.04f, 0f), new Vector2(0.96f, 1f),
                13f, TextAlignmentOptions.MidlineLeft, new Color(0.68f, 0.65f, 0.58f));
        }

        private void CreateItemRow(RunPresentationItem item, bool equipment)
        {
            float height = Mathf.Clamp(68f + Mathf.Floor(Mathf.Max(0, (item.Name ?? string.Empty).Length - 28) / 24f) * 18f,
                68f, 116f);
            GameObject row = CreateLayoutRow("Item " + item.Id, height,
                _manifest != null ? _manifest.WoodInventoryCell : null,
                equipment ? new Color(0.31f, 0.23f, 0.12f, 1f) : new Color(0.18f, 0.16f, 0.12f, 1f));
            Image rowImage = row.GetComponent<Image>();
            _itemRows[item] = rowImage;
            Button button = row.AddComponent<Button>();
            RunPresentationItem captured = item;
            button.onClick.AddListener(() => SelectItem(captured));
            AddFocusEvents(row, () =>
            {
                SelectItem(captured);
                ScrollListRowIntoView(row.transform as RectTransform);
            });

            GameObject iconObject = CreateImage(row.transform, "Icon", new Vector2(0.025f, 0.16f),
                new Vector2(0.16f, 0.84f), IconForKind(_manifest, item.Kind), Color.white);
            iconObject.GetComponent<Image>().preserveAspect = true;
            string quantity = item.Quantity > 1 ? " ×" + item.Quantity : string.Empty;
            TMP_Text label = CreateText(row.transform, "Name", item.Name + quantity,
                new Vector2(0.19f, 0.38f), new Vector2(0.97f, 0.92f), 14f,
                TextAlignmentOptions.MidlineLeft, Color.white);
            label.overflowMode = TextOverflowModes.Overflow;
            TMP_Text kind = CreateText(row.transform, "Kind", KindLabel(item.Kind) +
                (equipment ? " · 장비/중요품" : "") + " · 수량 " + Mathf.Max(0, item.Quantity),
                new Vector2(0.19f, 0.06f), new Vector2(0.97f, 0.38f), 11f,
                TextAlignmentOptions.MidlineLeft, new Color(0.72f, 0.68f, 0.58f));
            kind.overflowMode = TextOverflowModes.Overflow;
        }

        private void CreateQuestRow(RunPresentationQuest quest, int index)
        {
            float height = Mathf.Clamp(74f + Mathf.Floor(Mathf.Max(0, (quest.Title ?? string.Empty).Length - 30) / 26f) * 18f,
                74f, 124f);
            Color statusColor = QuestStatusColor(quest.Status);
            GameObject row = CreateLayoutRow("Quest " + index + " · " + quest.Id, height,
                _manifest != null ? _manifest.WoodInventoryCell : null,
                new Color(statusColor.r * 0.28f, statusColor.g * 0.25f, statusColor.b * 0.20f, 0.98f));
            Button button = row.AddComponent<Button>();
            RunPresentationQuest captured = quest;
            button.onClick.AddListener(() => SelectQuest(captured));
            AddFocusEvents(row, () =>
            {
                SelectQuest(captured);
                ScrollListRowIntoView(row.transform as RectTransform);
            });
            CreateText(row.transform, "Status", "[" + QuestStatusLabel(quest.Status) + "]",
                new Vector2(0.025f, 0.18f), new Vector2(0.20f, 0.82f), 13f,
                TextAlignmentOptions.MidlineLeft, statusColor);
            string step = string.IsNullOrWhiteSpace(quest.CurrentStep)
                ? "현재 단계 정보 없음"
                : "현재 · " + StepLabel(quest.CurrentStep);
            TMP_Text title = CreateText(row.transform, "Title", quest.Title + "\n" + step,
                new Vector2(0.21f, 0.09f), new Vector2(0.97f, 0.91f), 14f,
                TextAlignmentOptions.MidlineLeft, new Color(0.94f, 0.91f, 0.83f));
            title.overflowMode = TextOverflowModes.Overflow;
        }

        private GameObject CreateLayoutRow(string name, float height, Color color)
        {
            return CreateLayoutRow(name, height, null, color);
        }

        private GameObject CreateLayoutRow(string name, float height, Sprite sprite, Color color)
        {
            var row = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            row.transform.SetParent(_listContent, false);
            Image image = row.GetComponent<Image>();
            image.sprite = sprite;
            image.type = sprite != null && sprite.border.sqrMagnitude > 0f ? Image.Type.Sliced : Image.Type.Simple;
            image.color = color;
            LayoutElement element = row.GetComponent<LayoutElement>();
            element.minHeight = Mathf.Max(44f, height);
            element.preferredHeight = Mathf.Max(44f, height);
            element.flexibleHeight = 0f;
            return row;
        }

        private void SelectItem(RunPresentationItem item)
        {
            if (item == null) return;
            _selectedItem = item;
            foreach (KeyValuePair<RunPresentationItem, Image> pair in _itemRows)
            {
                bool selected = ReferenceEquals(pair.Key, item);
                Color color = pair.Key.IsEquipment
                    ? new Color(0.31f, 0.23f, 0.12f, 1f)
                    : new Color(0.18f, 0.16f, 0.12f, 1f);
                pair.Value.color = selected
                    ? new Color(0.52f, 0.36f, 0.14f, 1f)
                    : color;
            }
            _detailTitle.text = item.Name + (item.Quantity > 1 ? " ×" + item.Quantity : string.Empty);
            _detailBody.text = "종류 · " + KindLabel(item.Kind) +
                               "\n수량 · " + Mathf.Max(0, item.Quantity) +
                               "\n분류 · " + (item.IsEquipment ? "장비/중요품" : "가방 아이템") +
                               (item.IsProtected ? "\n보호 · 삭제할 수 없는 중요 항목" : string.Empty) +
                               "\n\n" + (string.IsNullOrWhiteSpace(item.Description)
                                   ? "설명이 제공되지 않았습니다."
                                   : item.Description.Trim());
            _detailAction.gameObject.SetActive(true);
            _detailAction.interactable = !string.IsNullOrWhiteSpace(item.Name);
            _detailActionLabel.text = "대화에 넣기";
            Canvas.ForceUpdateCanvases();
            _detailScroll.verticalNormalizedPosition = 1f;
        }

        private void SelectQuest(RunPresentationQuest quest)
        {
            if (quest == null) return;
            _selectedItem = null;
            string status = QuestStatusLabel(quest.Status);
            _detailTitle.text = "[" + status + "] " + quest.Title;
            _detailTitle.color = QuestStatusColor(quest.Status);
            _detailBody.text = "상태 · " + status +
                               "\n종류 · " + (string.IsNullOrWhiteSpace(quest.Kind) ? "이야기" : quest.Kind) +
                               "\n현재 단계 · " + (string.IsNullOrWhiteSpace(quest.CurrentStep)
                                   ? "정보 없음"
                                   : StepLabel(quest.CurrentStep)) +
                               "\n\n" + (string.IsNullOrWhiteSpace(quest.Summary)
                                   ? "요약이 제공되지 않았습니다."
                                   : quest.Summary.Trim());
            _detailAction.gameObject.SetActive(false);
            Canvas.ForceUpdateCanvases();
            _detailScroll.verticalNormalizedPosition = 1f;
        }

        private void InsertSelectedItem()
        {
            if (_selectedItem != null && !string.IsNullOrWhiteSpace(_selectedItem.Name))
                _itemSelected?.Invoke(_selectedItem.Name);
        }

        private void ScrollListRowIntoView(RectTransform row)
        {
            if (row == null || _listScroll?.viewport == null || _listContent == null)
                return;
            Canvas.ForceUpdateCanvases();
            Bounds viewportBounds = new Bounds(_listScroll.viewport.rect.center, _listScroll.viewport.rect.size);
            Bounds rowBounds = RectTransformUtility.CalculateRelativeRectTransformBounds(_listScroll.viewport, row);
            Vector2 position = _listContent.anchoredPosition;
            if (rowBounds.min.y < viewportBounds.min.y)
                position.y += viewportBounds.min.y - rowBounds.min.y;
            else if (rowBounds.max.y > viewportBounds.max.y)
                position.y -= rowBounds.max.y - viewportBounds.max.y;
            position.y = Mathf.Clamp(position.y, 0f,
                Mathf.Max(0f, _listContent.rect.height - _listScroll.viewport.rect.height));
            _listContent.anchoredPosition = position;
        }

        private void ResetDetail()
        {
            if (_detailTitle != null)
            {
                _detailTitle.text = "항목을 선택하세요";
                _detailTitle.color = new Color(1f, 0.84f, 0.48f);
            }
            if (_detailBody != null)
                _detailBody.text = "왼쪽 목록에서 아이템이나 퀘스트를 선택하면 전체 정보를 확인할 수 있습니다.";
            if (_detailAction != null)
            {
                _detailAction.gameObject.SetActive(_page == Page.Inventory);
                _detailAction.interactable = false;
            }
        }

        private static void AddFocusEvents(GameObject target, Action focused)
        {
            EventTrigger trigger = target.AddComponent<EventTrigger>();
            trigger.triggers = new List<EventTrigger.Entry>();
            AddTrigger(trigger, EventTriggerType.Select, focused);
            AddTrigger(trigger, EventTriggerType.PointerEnter, focused);
        }

        private static void AddTrigger(EventTrigger trigger, EventTriggerType type, Action action)
        {
            var entry = new EventTrigger.Entry { eventID = type };
            entry.callback.AddListener(_ => action?.Invoke());
            trigger.triggers.Add(entry);
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

        private static string QuestStatusLabel(string status)
        {
            switch ((status ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "active":
                case "open":
                case "in_progress": return "진행";
                case "completed":
                case "complete": return "완료";
                case "abandoned":
                case "abandon": return "포기";
                case "failed":
                case "failure": return "실패";
                case "locked": return "잠김";
                default: return string.IsNullOrWhiteSpace(status) ? "상태 미상" : status.Trim();
            }
        }

        private static Color QuestStatusColor(string status)
        {
            switch ((status ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "completed":
                case "complete": return new Color(0.47f, 0.86f, 0.57f, 1f);
                case "abandoned":
                case "abandon": return new Color(0.62f, 0.62f, 0.60f, 1f);
                case "failed":
                case "failure": return new Color(0.92f, 0.42f, 0.36f, 1f);
                case "locked": return new Color(0.58f, 0.55f, 0.52f, 1f);
                default: return new Color(1f, 0.80f, 0.36f, 1f);
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
            text.textWrappingMode = TextWrappingModes.Normal;
            text.raycastTarget = false;
            return text;
        }

        private static string Signature(IReadOnlyList<RunPresentationItem> items,
            IReadOnlyList<RunPresentationQuest> quests)
        {
            var value = new StringBuilder();
            for (int i = 0; i < items.Count; i++)
                value.Append(items[i]?.Id).Append(':').Append(items[i]?.Kind).Append(':')
                    .Append(items[i]?.Name).Append(':').Append(items[i]?.Description).Append(':')
                    .Append(items[i]?.Quantity).Append(':').Append(items[i]?.IsProtected).Append('|');
            value.Append('#');
            for (int i = 0; i < quests.Count; i++)
                value.Append(quests[i]?.Id).Append(':').Append(quests[i]?.Title).Append(':')
                    .Append(quests[i]?.Summary).Append(':').Append(quests[i]?.CurrentStep).Append(':')
                    .Append(quests[i]?.Kind).Append(':').Append(quests[i]?.Status).Append('|');
            return value.ToString();
        }
    }
}
