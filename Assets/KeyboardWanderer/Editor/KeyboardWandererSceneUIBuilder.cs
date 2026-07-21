using KeyboardWanderer.Demo;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace KeyboardWanderer.Editor
{
    public static class KeyboardWandererSceneUIBuilder
    {
        private const string UiPrefabPath = "Assets/KeyboardWanderer/Prefabs/UI/AuthoredUI.prefab";
        private const string GameHudPrefabPath = "Assets/KeyboardWanderer/Prefabs/UI/Screens/GameHUD.prefab";
        private const string DialoguePanelPrefabPath = "Assets/KeyboardWanderer/Prefabs/UI/Screens/DialoguePanel.prefab";
        private static readonly Color Ink = Hex("160f0a");
        private static readonly Color Panel = Hex("281a11");
        private static readonly Color Raised = Hex("352419");
        private static readonly Color Gold = Hex("d3a64b");
        private static readonly Color Parchment = Hex("f0dfb6");
        private static readonly Color Muted = Hex("ad9878");
        private static TMP_FontAsset _font;
        private static NinjaAdventureAssetManifest _assets;

        [MenuItem("Keyboard Wanderer/Rebuild Authored Scene UI")]
        public static void Build()
        {
            KeyboardWandererDemoController controller = Object.FindAnyObjectByType<KeyboardWandererDemoController>();
            if (controller == null)
                throw new UnityException("Open SampleScene and add Codria Game before building its UI.");

            _font = LoadDefaultFont();
            _assets = AssetDatabase.LoadAssetAtPath<NinjaAdventureAssetManifest>(
                "Assets/KeyboardWanderer/Resources/NinjaAdventureAssetManifest.asset");
            Transform oldUi = controller.transform.Find("Authored UI");
            if (oldUi != null) Undo.DestroyObjectImmediate(oldUi.gameObject);
            GameObject oldEvents = GameObject.Find("EventSystem");
            if (oldEvents != null) Undo.DestroyObjectImmediate(oldEvents);

            GameObject canvasObject = NewObject("Authored UI", controller.transform, typeof(RectTransform), typeof(Canvas),
                typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(KeyboardWandererSceneUI));
            Canvas canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 20;
            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1440f, 900f);
            scaler.matchWidthOrHeight = 0.5f;

            BuildTitle(canvasObject.transform);
            BuildGameHud(canvasObject.transform);
            BuildSettings(canvasObject.transform);
            BuildPause(canvasObject.transform);
            BuildEnding(canvasObject.transform);
            canvasObject.GetComponent<KeyboardWandererSceneUI>().AutoWire();

            EnsureFolder("Assets/KeyboardWanderer/Prefabs");
            EnsureFolder("Assets/KeyboardWanderer/Prefabs/UI");
            PrefabUtility.SaveAsPrefabAssetAndConnect(
                canvasObject,
                UiPrefabPath,
                InteractionMode.AutomatedAction,
                out bool prefabSaved);
            if (!prefabSaved)
                throw new UnityException("Failed to save the authored UI prefab.");

            EnsureEventSystem();
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            EditorSceneManager.SaveOpenScenes();
            Selection.activeGameObject = canvasObject;
        }

        public static void EnsureExistingOrBuild()
        {
            KeyboardWandererDemoController controller =
                Object.FindAnyObjectByType<KeyboardWandererDemoController>(FindObjectsInactive.Include);
            if (controller == null)
                throw new UnityException("Open SampleScene before synchronizing its authored UI.");

            KeyboardWandererSceneUI sceneUi =
                controller.GetComponentInChildren<KeyboardWandererSceneUI>(true);
            if (sceneUi == null)
            {
                Build();
                return;
            }

            sceneUi.AutoWire();
            EditorUtility.SetDirty(sceneUi);
            PrefabUtility.RecordPrefabInstancePropertyModifications(sceneUi);
            EnsureEventSystem();
            EditorSceneManager.MarkSceneDirty(controller.gameObject.scene);
            EditorSceneManager.SaveOpenScenes();
        }

        [MenuItem("Keyboard Wanderer/Apply Scene UI Overrides to AuthoredUI Prefab")]
        public static void ApplySceneUiOverridesToPrefab()
        {
            KeyboardWandererDemoController controller =
                Object.FindAnyObjectByType<KeyboardWandererDemoController>(FindObjectsInactive.Include);
            KeyboardWandererSceneUI sceneUi = controller != null
                ? controller.GetComponentInChildren<KeyboardWandererSceneUI>(true)
                : null;
            if (sceneUi == null)
                throw new UnityException("The open scene does not contain Authored UI.");
            if (!PrefabUtility.IsPartOfPrefabInstance(sceneUi.gameObject))
                throw new UnityException("Authored UI must be a prefab instance before its overrides can be applied.");

            sceneUi.AutoWire();
            EditorUtility.SetDirty(sceneUi);
            PrefabUtility.RecordPrefabInstancePropertyModifications(sceneUi);
            GameObject instanceRoot = PrefabUtility.GetOutermostPrefabInstanceRoot(sceneUi.gameObject);
            PrefabUtility.ApplyPrefabInstance(instanceRoot, InteractionMode.AutomatedAction);
            EditorSceneManager.MarkSceneDirty(controller.gameObject.scene);
            EditorSceneManager.SaveOpenScenes();
            AssetDatabase.SaveAssets();
            Debug.Log("Applied the current scene Authored UI overrides to AuthoredUI.prefab.", sceneUi);
        }

        public static void ApplySampleSceneUiOverridesToPrefab()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity", OpenSceneMode.Single);
            ApplySceneUiOverridesToPrefab();
        }

        [MenuItem("Keyboard Wanderer/Apply Default Font to Authored UI")]
        public static void ApplyDefaultFontToAuthoredUi()
        {
            TMP_FontAsset font = LoadDefaultFont();
            GameObject prefabRoot = PrefabUtility.LoadPrefabContents(UiPrefabPath);
            try
            {
                TMP_Text[] texts = prefabRoot.GetComponentsInChildren<TMP_Text>(true);
                for (int i = 0; i < texts.Length; i++)
                {
                    texts[i].font = font;
                    EditorUtility.SetDirty(texts[i]);
                }
                PrefabUtility.SaveAsPrefabAsset(prefabRoot, UiPrefabPath);
                Debug.Log("Applied " + font.name + " to " + texts.Length + " authored UI text components.");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(prefabRoot);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        [MenuItem("Keyboard Wanderer/Regenerate Default Game HUD Layout")]
        public static void ApplyEditableGameHudLayout()
        {
            _font = LoadDefaultFont();
            _assets = AssetDatabase.LoadAssetAtPath<NinjaAdventureAssetManifest>(
                "Assets/KeyboardWanderer/Resources/NinjaAdventureAssetManifest.asset");

            UpgradeDialoguePanelPrefab();

            GameObject prefabRoot = PrefabUtility.LoadPrefabContents(UiPrefabPath);
            try
            {
                Transform oldHud = prefabRoot.transform.Find("Game HUD");
                if (oldHud != null) Object.DestroyImmediate(oldHud.gameObject);
                GameObject hudAsset = AssetDatabase.LoadAssetAtPath<GameObject>(
                    "Assets/KeyboardWanderer/Prefabs/UI/Screens/GameHUD.prefab");
                if (hudAsset == null)
                    throw new UnityException("Componentized GameHUD prefab is missing.");
                GameObject hud = (GameObject)PrefabUtility.InstantiatePrefab(hudAsset, prefabRoot.transform);
                hud.name = "Game HUD";
                hud.transform.SetSiblingIndex(1);
                hud.SetActive(false);

                KeyboardWandererSceneUI sceneUi = prefabRoot.GetComponent<KeyboardWandererSceneUI>();
                sceneUi.AutoWire();
                if (!sceneUi.IsReady)
                    throw new UnityException("Regenerated AuthoredUI has incomplete screen references.");
                EditorUtility.SetDirty(sceneUi);
                PrefabUtility.SaveAsPrefabAsset(prefabRoot, UiPrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(prefabRoot);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            EditorSceneManager.MarkAllScenesDirty();
            EditorSceneManager.SaveOpenScenes();
            Debug.Log("Applied editable edge-HUD layout without rebuilding the title screen.");
        }

        /// <summary>프레임 스프라이트를 유지한 채 슬라이스 배율만 올려 테두리를 얇게 만든다.</summary>
        private const float ThinBorderMultiplier = 2.1f;

        /// <summary>
        /// 기존 프리팹을 다시 만들지 않고 퀘스트·인벤토리 패널과 대화 화자 버스트를 덧입힌다.
        /// 여러 번 실행해도 같은 결과가 나오도록 이전 실행이 만든 오브젝트는 먼저 지운다.
        /// </summary>
        [MenuItem("Keyboard Wanderer/Restyle HUD (Quest + Inventory + Dialogue Bust)")]
        public static void RestyleQuestStatusDialogue()
        {
            _font = LoadDefaultFont();
            _assets = AssetDatabase.LoadAssetAtPath<NinjaAdventureAssetManifest>(
                "Assets/KeyboardWanderer/Resources/NinjaAdventureAssetManifest.asset");
            RestyleGameHudPrefab();
            RestyleDialoguePanelPrefab();
            RestyleMinimapPanelPrefab();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("Restyled HUD prefabs: thin frames, larger text, quest list, inventory panel, left dialogue bust.");
        }

        private static void ThinBorder(Transform panel)
        {
            Image image = panel != null ? panel.GetComponent<Image>() : null;
            if (image != null && image.sprite != null && image.type == Image.Type.Sliced)
                image.pixelsPerUnitMultiplier = ThinBorderMultiplier;
        }

        private static void BumpText(Transform root, string childName, int size, int bestFitMin = 0)
        {
            Transform child = root != null ? root.Find(childName) : null;
            TMP_Text text = child != null ? child.GetComponent<TMP_Text>() : null;
            if (text == null)
                return;
            text.fontSize = size;
            if (bestFitMin > 0)
            {
                text.enableAutoSizing = true;
                text.fontSizeMin = bestFitMin;
                text.fontSizeMax = size;
            }
        }

        private static void RestyleGameHudPrefab()
        {
            GameObject root = PrefabUtility.LoadPrefabContents(GameHudPrefabPath);
            try
            {
                Transform hud = root.transform;
                RectTransform objectivePanel = hud.Find("Objective Panel") as RectTransform;
                if (objectivePanel == null)
                    throw new UnityException("GameHUD.prefab is missing its Objective Panel.");

                // 퀘스트 프레임은 디자인을 유지하고 두께만 얇게 한다.
                ThinBorder(objectivePanel);
                // 좌측 최상단 캐릭터 상태창(초상 + 현재 장소/장면)은 사용하지 않는다.
                DestroyChild(hud, "Story Header");

                // 퀘스트 패널: 더 크게, 배너 + 번호 목록 본문 + 추천 한 줄.
                objectivePanel.anchorMin = new Vector2(0.018f, 0.52f);
                objectivePanel.anchorMax = new Vector2(0.265f, 0.795f);
                DestroyChild(objectivePanel, "Quest Banner");
                DestroyChild(objectivePanel, "Quest Hint");
                RectTransform banner = PanelRect(objectivePanel, "Quest Banner", new Vector2(0.03f, 0.86f),
                    new Vector2(0.97f, 0.985f), Vector2.zero, Vector2.zero, new Color(Gold.r, Gold.g, Gold.b, 0.92f));
                banner.SetAsFirstSibling();
                RectTransform heading = objectivePanel.Find("Objective Heading") as RectTransform;
                if (heading != null)
                {
                    heading.anchorMin = new Vector2(0.09f, 0.86f);
                    heading.anchorMax = new Vector2(0.97f, 0.985f);
                    TMP_Text headingText = heading.GetComponent<TMP_Text>();
                    headingText.text = "!  QUEST";
                    headingText.fontSize = 16;
                    headingText.color = Ink;
                    headingText.fontStyle = FontStyles.Bold;
                    heading.SetAsLastSibling();
                }
                RectTransform objectiveText = objectivePanel.Find("Objective Text") as RectTransform;
                if (objectiveText != null)
                {
                    objectiveText.anchorMin = new Vector2(0.07f, 0.16f);
                    objectiveText.anchorMax = new Vector2(0.93f, 0.82f);
                    TMP_Text bodyText = objectiveText.GetComponent<TMP_Text>();
                    bodyText.alignment = TextAlignmentOptions.TopLeft;
                    bodyText.lineSpacing = 8f;
                    EnableBestFit(objectiveText, 12, 17);
                }
                RectTransform questHint = TextRect(objectivePanel, "Quest Hint", new Vector2(0.07f, 0.035f),
                    new Vector2(0.93f, 0.14f), "추천  --", 13, Muted, TextAnchor.MiddleLeft);
                EnableBestFit(questHint, 10, 14);

                // 상태 패널은 소지품 요약 패널로 대체한다. 이동 안내를 담당하던 중앙 상단
                // Selection Panel도 함께 정리한다(더 이상 화면에 노출하지 않는다).
                DestroyChild(hud, "Status Panel");
                DestroyChild(hud, "Selection Panel");
                RectTransform inventory = BuildInventoryPanel(hud);
                inventory.SetSiblingIndex(objectivePanel.GetSiblingIndex() + 1);

                KeyboardWandererGameHudView hudView = root.GetComponent<KeyboardWandererGameHudView>();
                if (hudView == null)
                    throw new UnityException("GameHUD.prefab is missing KeyboardWandererGameHudView.");
                hudView.ConfigureQuestHint(questHint.GetComponent<TMP_Text>());
                PrefabUtility.SaveAsPrefabAsset(root, GameHudPrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        /// <summary>
        /// Status Panel이 있던 자리에 상시 노출되는 소지품 요약 패널을 만든다.
        /// 다른 HUD 패널과 같은 FacesetBox 테두리 + 금색 배너 스타일을 쓰고,
        /// 아이콘 4칸은 <see cref="KeyboardWandererInventoryHudView"/>가 매 프레임 갱신한다.
        /// </summary>
        private static RectTransform BuildInventoryPanel(Transform hud)
        {
            RectTransform inventory = PanelRect(hud, "Inventory Panel", new Vector2(0.018f, 0.315f),
                new Vector2(0.265f, 0.50f), Vector2.zero, Vector2.zero, new Color(0.055f, 0.045f, 0.035f, 0.92f));
            ApplyPanelSprite(inventory, _assets != null ? _assets.FacesetBox : null, Image.Type.Sliced);
            ThinBorder(inventory);
            RectTransform banner = PanelRect(inventory, "Inventory Banner", new Vector2(0.03f, 0.845f),
                new Vector2(0.97f, 0.985f), Vector2.zero, Vector2.zero, new Color(Gold.r, Gold.g, Gold.b, 0.92f));
            banner.SetAsFirstSibling();
            RectTransform heading = TextRect(inventory, "Inventory Heading", new Vector2(0.09f, 0.845f),
                new Vector2(0.97f, 0.985f), "◆  INVENTORY", 16, Ink, TextAnchor.MiddleLeft);
            heading.GetComponent<TMP_Text>().fontStyle = FontStyles.Bold;
            RectTransform empty = TextRect(inventory, "Inventory Empty", new Vector2(0.08f, 0.10f),
                new Vector2(0.92f, 0.76f), "아직 소지품이 없습니다.", 13, Muted, TextAnchor.MiddleCenter);
            EnableBestFit(empty, 10, 13);

            const int slotCount = 4;
            Image[] icons = new Image[slotCount];
            TMP_Text[] quantities = new TMP_Text[slotCount];
            for (int i = 0; i < slotCount; i++)
            {
                float left = 0.03f + i * 0.24f;
                RectTransform slot = PanelRect(inventory, "Item Slot " + i, new Vector2(left, 0.10f),
                    new Vector2(left + 0.22f, 0.78f), Vector2.zero, Vector2.zero,
                    new Color(0.12f, 0.10f, 0.08f, 0.85f));
                Image icon = ImageRect(slot, "Icon", new Vector2(0.12f, 0.12f), new Vector2(0.88f, 0.88f),
                    null, Color.white).GetComponent<Image>();
                icon.preserveAspect = true;
                RectTransform quantity = TextRect(slot, "Quantity", new Vector2(0.42f, 0.02f), new Vector2(0.98f, 0.32f),
                    string.Empty, 11, Parchment, TextAnchor.LowerRight);
                icons[i] = icon;
                quantities[i] = quantity.GetComponent<TMP_Text>();
            }

            KeyboardWandererInventoryHudView view = inventory.gameObject.AddComponent<KeyboardWandererInventoryHudView>();
            view.Configure(_assets, icons, quantities, empty.GetComponent<TMP_Text>());
            return inventory;
        }

        private static void RestyleDialoguePanelPrefab()
        {
            GameObject root = PrefabUtility.LoadPrefabContents(DialoguePanelPrefabPath);
            try
            {
                Transform panel = root.transform;
                DestroyChild(panel, "Speaker Bust");
                DestroyChild(panel, "Speaker Name Plate");
                DestroyChild(panel, "Speaker Cutin Backdrop");

                // 초상 슬롯이 그려진 페이스셋 상자 대신 슬롯 없는 일반 대화 상자를 쓴다.
                if (_assets != null && _assets.DialogBox != null)
                {
                    Image panelImage = panel.GetComponent<Image>();
                    panelImage.sprite = _assets.DialogBox;
                    panelImage.type = Image.Type.Simple;
                    panelImage.color = Color.white;
                }

                // 화자 컷인: 이벤트 연출처럼 화면 한가운데에 큼직하게 띄운다.
                // 패널(0.245~0.82 × 0.025~0.19) 기준 앵커 (0.443, 2.6)이 화면 중앙 부근이다.
                // 카메라가 항상 플레이어를 화면 중앙에 두므로, 인 게임 캐릭터와 겹쳐 보이지 않도록
                // 컷인은 확실히 더 크게(380px) 만들고 그 뒤에 스포트라이트 배경을 깔아 도드라지게 한다.
                Vector2 cutinAnchor = new Vector2(0.443f, 2.6f);
                RectTransform backdrop = PanelRect(panel, "Speaker Cutin Backdrop", cutinAnchor, cutinAnchor,
                    new Vector2(-230f, -230f), new Vector2(230f, 230f), new Color(0.11f, 0.19f, 0.21f, 0.55f));
                AddOutline(backdrop.gameObject, new Color(Gold.r, Gold.g, Gold.b, 0.55f), 2f);
                backdrop.gameObject.SetActive(false);

                RectTransform portraitFrame = panel.Find("Speaker Portrait Frame") as RectTransform;
                if (portraitFrame != null)
                {
                    portraitFrame.anchorMin = cutinAnchor;
                    portraitFrame.anchorMax = cutinAnchor;
                    portraitFrame.offsetMin = new Vector2(-190f, -190f);
                    portraitFrame.offsetMax = new Vector2(190f, 190f);
                    RectTransform portrait = portraitFrame.Find("Speaker Portrait") as RectTransform;
                    if (portrait != null)
                    {
                        portrait.anchorMin = Vector2.zero;
                        portrait.anchorMax = Vector2.one;
                        portrait.offsetMin = Vector2.zero;
                        portrait.offsetMax = Vector2.zero;
                        // 기본 초상(배경이 박힌 페이스셋)은 쓰지 않는다. 화자 스프라이트가 매 프레임 지정된다.
                        portrait.GetComponent<Image>().sprite = null;
                    }
                    // 배경이 컷인 바로 뒤에서 렌더링되도록 형제 순서를 붙여 넣는다.
                    backdrop.SetSiblingIndex(portraitFrame.GetSiblingIndex());
                    portraitFrame.gameObject.SetActive(false);
                }

                // 이름표: 대화 상자 스프라이트의 왼쪽 위 탭 위에 금색 명판으로 얹는다.
                RectTransform namePlate = PanelRect(panel, "Speaker Name Plate", new Vector2(0f, 1f), new Vector2(0f, 1f),
                    new Vector2(28f, -34f), new Vector2(170f, -4f), new Color(Gold.r, Gold.g, Gold.b, 0.96f));
                AddOutline(namePlate.gameObject, new Color(0.25f, 0.15f, 0.08f, 0.9f), 1f);
                RectTransform speakerName = panel.Find("Dialogue Speaker") as RectTransform;
                if (speakerName != null)
                {
                    speakerName.anchorMin = new Vector2(0f, 1f);
                    speakerName.anchorMax = new Vector2(0f, 1f);
                    speakerName.offsetMin = new Vector2(28f, -34f);
                    speakerName.offsetMax = new Vector2(170f, -4f);
                    TMP_Text nameText = speakerName.GetComponent<TMP_Text>();
                    nameText.alignment = TextAlignmentOptions.Center;
                    nameText.color = Ink;
                    nameText.fontStyle = FontStyles.Bold;
                    nameText.enableAutoSizing = true;
                    nameText.fontSizeMin = 11;
                    nameText.fontSizeMax = 15;
                    namePlate.SetSiblingIndex(speakerName.GetSiblingIndex());
                }

                // 물음표 등 감정 말풍선은 대화창에서 쓰지 않는다. 참조는 유지한 채 꺼 둔다.
                RectTransform emote = panel.Find("Speaker Emote") as RectTransform;
                if (emote != null)
                    emote.gameObject.SetActive(false);

                // 컷인이 중앙으로 옮겨졌으니 대사는 상자 전체 폭을 쓴다. 하단 안내문은 크게.
                RectTransform speech = panel.Find("Speech Bubble") as RectTransform;
                if (speech != null)
                {
                    speech.anchorMin = new Vector2(0.045f, 0.10f);
                    speech.anchorMax = new Vector2(0.975f, 0.90f);
                    RectTransform storyText = speech.Find("Story Text") as RectTransform;
                    if (storyText != null)
                        EnableBestFit(storyText, 12, 16);
                    RectTransform actionHint = speech.Find("Action Hint") as RectTransform;
                    if (actionHint != null)
                    {
                        actionHint.anchorMin = new Vector2(0.055f, 0.04f);
                        actionHint.anchorMax = new Vector2(0.84f, 0.36f);
                        EnableBestFit(actionHint, 14, 18);
                    }
                }

                Transform bigPortrait = panel.Find("Speaker Portrait Frame/Speaker Portrait");
                KeyboardWandererDialogueView dialogueView = root.GetComponent<KeyboardWandererDialogueView>();
                if (dialogueView == null)
                    throw new UnityException("DialoguePanel.prefab is missing KeyboardWandererDialogueView.");
                // 중앙 대형 컷인 하나가 화자 표시를 전담한다. 화자 스프라이트가 있을 때만 배경과 함께 켜진다.
                dialogueView.ConfigureSpeakerVisuals(
                    bigPortrait != null ? bigPortrait.GetComponent<Image>() : null,
                    backdrop.GetComponent<Image>());
                PrefabUtility.SaveAsPrefabAsset(root, DialoguePanelPrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static void RestyleMinimapPanelPrefab()
        {
            GameObject root = PrefabUtility.LoadPrefabContents(
                "Assets/KeyboardWanderer/Prefabs/UI/Screens/MinimapPanel.prefab");
            try
            {
                ThinBorder(root.transform);
                BumpText(root.transform, "Minimap Heading", 14);
                BumpText(root.transform, "Minimap Status", 13, 11);
                PrefabUtility.SaveAsPrefabAsset(root, "Assets/KeyboardWanderer/Prefabs/UI/Screens/MinimapPanel.prefab");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static void DestroyChild(Transform parent, string childName)
        {
            Transform child = parent.Find(childName);
            if (child != null)
                Object.DestroyImmediate(child.gameObject);
        }

        private static void UpgradeDialoguePanelPrefab()
        {
            const string path = "Assets/KeyboardWanderer/Prefabs/UI/Screens/DialoguePanel.prefab";
            GameObject dialogue = PrefabUtility.LoadPrefabContents(path);
            try
            {
                RectTransform root = dialogue.GetComponent<RectTransform>();
                root.anchorMin = new Vector2(0.18f, 0.025f);
                root.anchorMax = new Vector2(0.86f, 0.235f);
                root.offsetMin = Vector2.zero;
                root.offsetMax = Vector2.zero;

                Transform storyObject = FindDescendant(dialogue.transform, "Story Text");
                if (storyObject != null)
                {
                    RectTransform storyRect = storyObject.GetComponent<RectTransform>();
                    storyRect.anchorMin = new Vector2(0.055f, 0.30f);
                    storyRect.anchorMax = new Vector2(0.95f, 0.80f);
                    TMP_Text storyText = storyObject.GetComponent<TMP_Text>();
                    storyText.enableAutoSizing = true;
                    storyText.fontSizeMin = 13;
                    storyText.fontSizeMax = 18;
                }

                Transform oldChoices = FindDescendant(dialogue.transform, "Choice Strip");
                if (oldChoices != null) Object.DestroyImmediate(oldChoices.gameObject);
                Transform oldStage = FindDescendant(dialogue.transform, "Encounter Subject Stage");
                if (oldStage != null) Object.DestroyImmediate(oldStage.gameObject);
                RectTransform stage = PanelRect(root, "Encounter Subject Stage", new Vector2(-0.27f, 1.02f),
                    new Vector2(1.27f, 4.62f), Vector2.zero, Vector2.zero,
                    new Color(0.015f, 0.018f, 0.025f, 0.72f));
                ImageRect(stage, "Encounter Subject", new Vector2(0.16f, 0.02f), new Vector2(0.84f, 0.98f),
                    null, Color.white);
                stage.SetAsFirstSibling();
                stage.gameObject.SetActive(false);
                RectTransform choices = PanelRect(root, "Choice Strip", new Vector2(0.02f, 1.08f),
                    new Vector2(0.98f, 1.72f), Vector2.zero, Vector2.zero,
                    new Color(0.055f, 0.036f, 0.022f, 0.96f));
                AddOutline(choices.gameObject, new Color(Gold.r, Gold.g, Gold.b, 0.62f), 2f);
                for (int i = 0; i < 4; i++)
                {
                    float top = 0.94f - i * 0.235f;
                    RectTransform choice = ButtonRect(choices, "Choice " + (i + 1), "선택 " + (i + 1),
                        new Vector2(0.025f, top - 0.19f), new Vector2(0.975f, top),
                        i == 0 ? Gold : Raised, i == 0 ? Ink : Parchment, "Choice Label " + (i + 1));
                    TMP_Text label = choice.GetComponentInChildren<TMP_Text>(true);
                    label.enableAutoSizing = true;
                    label.fontSizeMin = 10;
                    label.fontSizeMax = 16;
                    label.alignment = TextAlignmentOptions.MidlineLeft;
                    label.margin = new Vector4(14f, 3f, 10f, 3f);
                }
                choices.gameObject.SetActive(false);
                PrefabUtility.SaveAsPrefabAsset(dialogue, path);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(dialogue);
            }
        }

        private static Transform FindDescendant(Transform root, string objectName)
        {
            Transform[] items = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < items.Length; i++)
                if (items[i].name == objectName) return items[i];
            return null;
        }

        private static void EnsureEventSystem()
        {
            EventSystem eventSystem = Object.FindAnyObjectByType<EventSystem>(FindObjectsInactive.Include);
            if (eventSystem != null)
                return;
            GameObject eventObject = NewObject(
                "EventSystem", null, typeof(EventSystem), typeof(InputSystemUIInputModule));
            Undo.RegisterCreatedObjectUndo(eventObject, "Create Codria EventSystem");
        }

        private static TMP_FontAsset LoadDefaultFont()
        {
            return KeyboardWandererFontAssetAuthoring.EnsureProjectFontAsset();
        }

        private static void BuildTitle(Transform canvas)
        {
            RectTransform root = PanelRect(canvas, "Title Screen", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero,
                new Color(0.035f, 0.027f, 0.02f, 1f));
            RectTransform card = PanelRect(root, "Title Card", new Vector2(0.267f, 0.06f), new Vector2(0.733f, 0.94f),
                Vector2.zero, Vector2.zero, new Color(0.31f, 0.23f, 0.16f, 0.98f));
            AddOutline(card.gameObject, Gold, 2f);
            TextRect(card, "Title Heading", new Vector2(0.06f, 0.86f), new Vector2(0.94f, 0.96f), "NINJA ADVENTURE", 30, Gold, TextAnchor.MiddleCenter);
            TextRect(card, "Title Subtitle", new Vector2(0.08f, 0.80f), new Vector2(0.92f, 0.86f), "코드리아 × 관리자 키보드 × 선택 회수", 16, Muted, TextAnchor.MiddleCenter);
            ImageRect(card, "Title Character", new Vector2(0.37f, 0.59f), new Vector2(0.63f, 0.79f), _assets != null ? _assets.PlayerIdle : null, Color.white);
            ImageRect(card, "Title D20", new Vector2(0.60f, 0.57f), new Vector2(0.69f, 0.66f), _assets != null ? _assets.D20 : null, new Color(1f, 0.88f, 0.58f, 1f));
            TextRect(card, "Title Seed", new Vector2(0.08f, 0.53f), new Vector2(0.92f, 0.58f), "NEXT SEED", 14, Muted, TextAnchor.MiddleCenter);
            TextRect(card, "Title Premise", new Vector2(0.10f, 0.35f), new Vector2(0.90f, 0.52f), "캠페인 미리보기", 17, Parchment, TextAnchor.UpperCenter);
            ButtonRect(card, "New Run Button", "새 Seed 런", new Vector2(0.22f, 0.24f), new Vector2(0.78f, 0.31f), Gold, Ink, "New Run Label");
            ButtonRect(card, "Continue Button", "권위 상태에서 이어하기", new Vector2(0.22f, 0.16f), new Vector2(0.78f, 0.22f), Raised, Parchment, "Continue Label");
            ButtonRect(card, "Settings Button", "설정", new Vector2(0.22f, 0.09f), new Vector2(0.78f, 0.14f), Raised, Parchment, "Settings Label");
            TextRect(card, "Title Status", new Vector2(0.08f, 0.025f), new Vector2(0.92f, 0.075f), "권위 서버 확인 전", 13, Muted, TextAnchor.MiddleCenter);
        }

        private static void BuildGameHud(Transform canvas)
        {
            RectTransform root = RectObject(canvas, "Game HUD", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            RectTransform viewport = PanelRect(root, "World Viewport", Vector2.zero, Vector2.one,
                Vector2.zero, Vector2.zero, new Color(1f, 1f, 1f, 0.005f));
            viewport.GetComponent<Image>().raycastTarget = false;

            RectTransform objective = PanelRect(root, "Objective Panel", new Vector2(0.018f, 0.635f), new Vector2(0.265f, 0.795f),
                Vector2.zero, Vector2.zero, new Color(0.055f, 0.045f, 0.035f, 0.92f));
            ApplyPanelSprite(objective, _assets != null ? _assets.FacesetBox : null, Image.Type.Sliced);
            TextRect(objective, "Objective Heading", new Vector2(0.10f, 0.62f), new Vector2(0.90f, 0.82f), "◆ 현재 목표", 13, Gold, TextAnchor.MiddleLeft);
            RectTransform objectiveText = TextRect(objective, "Objective Text", new Vector2(0.10f, 0.20f), new Vector2(0.90f, 0.60f),
                "관리자 키보드로 길을 탐색하세요.", 14, Parchment, TextAnchor.MiddleLeft);
            EnableBestFit(objectiveText, 10, 14);

            RectTransform actions = PanelRect(root, "Action Bar", new Vector2(0.865f, 0.16f), new Vector2(0.982f, 0.88f),
                Vector2.zero, Vector2.zero, new Color(0.055f, 0.036f, 0.022f, 0.94f));
            AddOutline(actions.gameObject, new Color(Gold.r, Gold.g, Gold.b, 0.42f), 1f);
            ButtonRect(actions, "Move Button", "MOVE", new Vector2(0.08f, 0.875f), new Vector2(0.92f, 0.975f), Gold, Ink, "Move Label");
            ButtonRect(actions, "Copy Skill Button", "COPY", new Vector2(0.08f, 0.76f), new Vector2(0.92f, 0.86f), Raised, Parchment, "Copy Skill Label");
            ButtonRect(actions, "Delete Skill Button", "DELETE", new Vector2(0.08f, 0.645f), new Vector2(0.92f, 0.745f), Raised, Parchment, "Delete Skill Label");
            ButtonRect(actions, "Connect Skill Button", "CONNECT", new Vector2(0.08f, 0.53f), new Vector2(0.92f, 0.63f), Raised, Parchment, "Connect Skill Label");
            ButtonRect(actions, "Restore Skill Button", "RESTORE", new Vector2(0.08f, 0.415f), new Vector2(0.92f, 0.515f), Raised, Parchment, "Restore Skill Label");
            ButtonRect(actions, "Undo Skill Button", "UNDO", new Vector2(0.08f, 0.30f), new Vector2(0.92f, 0.40f), Raised, Parchment, "Undo Skill Label");
            ButtonRect(actions, "Search Skill Button", "SEARCH", new Vector2(0.08f, 0.185f), new Vector2(0.92f, 0.285f), Raised, Parchment, "Search Skill Label");
            ButtonRect(actions, "Select All Skill Button", "SELECT ALL", new Vector2(0.08f, 0.07f), new Vector2(0.92f, 0.17f), Raised, Parchment, "Select All Skill Label");
            if (_assets != null)
            {
                AddSkillIcon(actions, "Move Button", _assets.MoveIcon);
                AddSkillIcon(actions, "Copy Skill Button", _assets.CopyIcon);
                AddSkillIcon(actions, "Delete Skill Button", _assets.DeleteIcon);
                AddSkillIcon(actions, "Connect Skill Button", _assets.ConnectIcon);
                AddSkillIcon(actions, "Restore Skill Button", _assets.RestoreIcon);
                AddSkillIcon(actions, "Undo Skill Button", _assets.UndoIcon);
                AddSkillIcon(actions, "Search Skill Button", _assets.SearchIcon);
                AddSkillIcon(actions, "Select All Skill Button", _assets.SelectAllIcon);
                AddShortcutKeycaps(actions, "Move Button", _assets.KeyW);
                AddShortcutKeycaps(actions, "Copy Skill Button", _assets.KeyCtrl, _assets.KeyC);
                AddShortcutKeycaps(actions, "Delete Skill Button", _assets.KeyDelete);
                AddShortcutKeycaps(actions, "Connect Skill Button", _assets.KeyCtrl, _assets.KeyK);
                AddShortcutKeycaps(actions, "Restore Skill Button", _assets.KeyCtrl, _assets.KeyR);
                AddShortcutKeycaps(actions, "Undo Skill Button", _assets.KeyCtrl, _assets.KeyZ);
                AddShortcutKeycaps(actions, "Search Skill Button", _assets.KeyCtrl, _assets.KeyF);
                AddShortcutKeycaps(actions, "Select All Skill Button", _assets.KeyCtrl, _assets.KeyA);
            }

            RectTransform menuHint = PanelRect(root, "Menu Hint", new Vector2(0.89f, 0.91f), new Vector2(0.982f, 0.97f),
                Vector2.zero, Vector2.zero, new Color(0.055f, 0.045f, 0.035f, 0.92f));
            AddOutline(menuHint.gameObject, new Color(Gold.r, Gold.g, Gold.b, 0.52f), 1f);
            TextRect(menuHint, "Menu Hint Text", new Vector2(0.08f, 0.08f), new Vector2(0.92f, 0.92f), "ESC  메뉴", 14, Parchment, TextAnchor.MiddleCenter);
            if (_assets != null) AddShortcutKeycaps(root, "Menu Hint", _assets.KeyEscape);

            ButtonRect(root, "Confirm Action Button", "ENTER  실행", new Vector2(0.875f, 0.085f), new Vector2(0.982f, 0.145f),
                Gold, Ink, "Confirm Action Label");
            if (_assets != null)
            {
                AddShortcutKeycaps(root, "Confirm Action Button", _assets.KeyEnter);
                root.Find("Confirm Action Button").GetComponent<Image>().color = Color.clear;
            }

            RectTransform minimap = PanelRect(root, "Minimap Panel", new Vector2(0.018f, 0.025f), new Vector2(0.22f, 0.285f),
                Vector2.zero, Vector2.zero, new Color(0.055f, 0.045f, 0.035f, 0.92f));
            ApplyPanelSprite(minimap, _assets != null ? _assets.FacesetBox : null, Image.Type.Sliced);
            TextRect(minimap, "Minimap Heading", new Vector2(0.12f, 0.72f), new Vector2(0.88f, 0.84f), "WORLD MAP", 12, Parchment, TextAnchor.MiddleLeft);
            RectTransform minimapPreview = PanelRect(minimap, "Minimap Preview", new Vector2(0.12f, 0.22f), new Vector2(0.88f, 0.70f),
                Vector2.zero, Vector2.zero, new Color(0.12f, 0.16f, 0.12f, 1f));
            Image minimapMap = ImageRect(minimapPreview, "Minimap Map", new Vector2(0.04f, 0.06f),
                new Vector2(0.96f, 0.94f), null, Color.white).GetComponent<Image>();
            minimapMap.enabled = false;
            TextRect(minimapPreview, "Minimap Placeholder", new Vector2(0.08f, 0.08f), new Vector2(0.92f, 0.92f),
                "MAP\nPLACEHOLDER", 15, Muted, TextAnchor.MiddleCenter);
            TextRect(minimap, "Minimap Status", new Vector2(0.12f, 0.08f), new Vector2(0.88f, 0.19f), "탐사율 0%", 11, Muted, TextAnchor.MiddleLeft);

            RectTransform story = PanelRect(root, "Story Panel", new Vector2(0.18f, 0.025f), new Vector2(0.86f, 0.235f),
                Vector2.zero, Vector2.zero, new Color(0.80f, 0.63f, 0.39f, 0.98f));
            ApplyPanelSprite(story, _assets != null ? _assets.DialogueBoxFaceset : null, Image.Type.Simple);
            RectTransform subjectStage = PanelRect(story, "Encounter Subject Stage", new Vector2(-0.27f, 1.02f),
                new Vector2(1.27f, 4.62f), Vector2.zero, Vector2.zero,
                new Color(0.015f, 0.018f, 0.025f, 0.72f));
            ImageRect(subjectStage, "Encounter Subject", new Vector2(0.16f, 0.02f), new Vector2(0.84f, 0.98f),
                null, Color.white);
            subjectStage.SetAsFirstSibling();
            subjectStage.gameObject.SetActive(false);
            RectTransform portraitFrame = PanelRect(story, "Speaker Portrait Frame", new Vector2(0.025f, 0.18f), new Vector2(0.18f, 0.88f),
                Vector2.zero, Vector2.zero, Color.clear);
            ImageRect(portraitFrame, "Speaker Portrait", new Vector2(0.20f, 0.20f), new Vector2(0.80f, 0.80f),
                _assets != null ? _assets.VillagerIdle : null, Color.white);
            ImageRect(story, "Speaker Emote", new Vector2(0.115f, 0.70f), new Vector2(0.175f, 1.02f),
                _assets != null && _assets.Emotes != null && _assets.Emotes.Length >= 23 ? _assets.Emotes[22] : null,
                Color.white);
            RectTransform speakerText = TextRect(story, "Dialogue Speaker", new Vector2(0.025f, 0.08f), new Vector2(0.18f, 0.20f),
                "코드리아 주민", 11, new Color(0.38f, 0.20f, 0.13f, 1f), TextAnchor.MiddleCenter);
            EnableBestFit(speakerText, 8, 11);

            RectTransform speech = PanelRect(story, "Speech Bubble", new Vector2(0.19f, 0.10f), new Vector2(0.975f, 0.90f),
                Vector2.zero, Vector2.zero, Color.clear);
            RectTransform storyText = TextRect(speech, "Story Text", new Vector2(0.055f, 0.30f), new Vector2(0.95f, 0.80f),
                "이곳에서 벌어진 이야기가 표시됩니다.", 18, new Color(0.35f, 0.20f, 0.13f, 1f), TextAnchor.UpperLeft);
            EnableBestFit(storyText, 13, 18);
            ButtonRect(speech, "Next Dialogue Button", "다음 ▶", new Vector2(0.86f, 0.10f), new Vector2(0.96f, 0.28f),
                new Color(0.58f, 0.37f, 0.23f, 1f), Parchment, "Next Dialogue Label");
            RectTransform actionHint = TextRect(speech, "Action Hint", new Vector2(0.055f, 0.10f), new Vector2(0.82f, 0.28f),
                "대화를 읽은 뒤 이동하거나 스킬을 사용할 수 있습니다.", 11, new Color(0.49f, 0.31f, 0.20f, 1f), TextAnchor.MiddleLeft);
            EnableBestFit(actionHint, 8, 11);
            RectTransform choices = PanelRect(story, "Choice Strip", new Vector2(0.02f, 1.08f), new Vector2(0.98f, 1.72f),
                Vector2.zero, Vector2.zero, new Color(0.055f, 0.036f, 0.022f, 0.96f));
            AddOutline(choices.gameObject, new Color(Gold.r, Gold.g, Gold.b, 0.62f), 2f);
            for (int i = 0; i < 4; i++)
            {
                float top = 0.94f - i * 0.235f;
                RectTransform choice = ButtonRect(choices, "Choice " + (i + 1), "선택 " + (i + 1),
                    new Vector2(0.025f, top - 0.19f), new Vector2(0.975f, top),
                    i == 0 ? Gold : Raised, i == 0 ? Ink : Parchment, "Choice Label " + (i + 1));
                TMP_Text label = choice.GetComponentInChildren<TMP_Text>(true);
                if (label != null)
                {
                    label.fontSize = 16;
                    label.enableAutoSizing = true;
                    label.fontSizeMin = 10;
                    label.fontSizeMax = 16;
                    label.alignment = TextAlignmentOptions.MidlineLeft;
                    label.margin = new Vector4(14f, 3f, 10f, 3f);
                }
            }
            choices.gameObject.SetActive(false);
            root.gameObject.SetActive(false);
        }

        private static void BuildSettings(Transform canvas)
        {
            RectTransform root = PanelRect(canvas, "Settings Screen", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero,
                new Color(0.035f, 0.027f, 0.02f, 1f));
            RectTransform card = PanelRect(root, "Settings Card", new Vector2(0.305f, 0.17f), new Vector2(0.695f, 0.83f), Vector2.zero, Vector2.zero, Panel);
            AddOutline(card.gameObject, Gold, 2f);
            TextRect(card, "Settings Heading", new Vector2(0.08f, 0.82f), new Vector2(0.92f, 0.94f), "설정", 30, Gold, TextAnchor.MiddleCenter);
            TextRect(card, "Music Label", new Vector2(0.10f, 0.68f), new Vector2(0.90f, 0.76f), "음악 볼륨", 17, Parchment, TextAnchor.MiddleLeft);
            SliderRect(card, "Music Slider", new Vector2(0.10f, 0.61f), new Vector2(0.90f, 0.66f));
            TextRect(card, "Sfx Label", new Vector2(0.10f, 0.49f), new Vector2(0.90f, 0.57f), "효과음 볼륨", 17, Parchment, TextAnchor.MiddleLeft);
            SliderRect(card, "Sfx Slider", new Vector2(0.10f, 0.42f), new Vector2(0.90f, 0.47f));
            ToggleRect(card, "GM Toggle", "생성형 장면·대사 패널 표시", new Vector2(0.10f, 0.29f), new Vector2(0.90f, 0.37f));
            ButtonRect(card, "Settings Back Button", "돌아가기", new Vector2(0.10f, 0.10f), new Vector2(0.47f, 0.18f), Gold, Ink, "Settings Back Label");
            ButtonRect(card, "Delete Save Button", "이어하기 기록 삭제", new Vector2(0.53f, 0.10f), new Vector2(0.90f, 0.18f), Raised, Parchment, "Delete Save Label");
            root.gameObject.SetActive(false);
        }

        private static void BuildPause(Transform canvas)
        {
            RectTransform root = PanelRect(canvas, "Pause Screen", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, new Color(0f, 0f, 0f, 0.70f));
            RectTransform card = PanelRect(root, "Pause Card", new Vector2(0.35f, 0.28f), new Vector2(0.65f, 0.72f), Vector2.zero, Vector2.zero, Panel);
            TextRect(card, "Pause Heading", new Vector2(0.1f, 0.72f), new Vector2(0.9f, 0.9f), "일시 정지", 28, Gold, TextAnchor.MiddleCenter);
            ButtonRect(card, "Resume Button", "계속하기", new Vector2(0.18f, 0.50f), new Vector2(0.82f, 0.62f), Gold, Ink, "Resume Label");
            ButtonRect(card, "Pause Settings Button", "설정", new Vector2(0.18f, 0.34f), new Vector2(0.82f, 0.46f), Raised, Parchment, "Pause Settings Label");
            ButtonRect(card, "Title Button", "타이틀로", new Vector2(0.18f, 0.18f), new Vector2(0.82f, 0.30f), Raised, Parchment, "Title Button Label");
            root.gameObject.SetActive(false);
        }

        private static void BuildEnding(Transform canvas)
        {
            RectTransform root = PanelRect(canvas, "Ending Screen", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, new Color(0f, 0f, 0f, 0.74f));
            RectTransform card = PanelRect(root, "Ending Card", new Vector2(0.30f, 0.17f), new Vector2(0.70f, 0.83f), Vector2.zero, Vector2.zero, Panel);
            TextRect(card, "Ending Heading", new Vector2(0.08f, 0.80f), new Vector2(0.92f, 0.92f), "코드리아의 결말", 28, Gold, TextAnchor.MiddleCenter);
            TextRect(card, "Ending Text", new Vector2(0.10f, 0.30f), new Vector2(0.90f, 0.76f), "결말 요약", 17, Parchment, TextAnchor.MiddleCenter);
            ButtonRect(card, "Ending New Run Button", "새 여정", new Vector2(0.10f, 0.12f), new Vector2(0.47f, 0.22f), Gold, Ink, "Ending New Run Label");
            ButtonRect(card, "Ending Title Button", "타이틀", new Vector2(0.53f, 0.12f), new Vector2(0.90f, 0.22f), Raised, Parchment, "Ending Title Label");
            root.gameObject.SetActive(false);
        }

        private static RectTransform PanelRect(Transform parent, string name, Vector2 min, Vector2 max, Vector2 offsetMin, Vector2 offsetMax, Color color)
        {
            RectTransform rect = RectObject(parent, name, min, max, offsetMin, offsetMax);
            Image image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            return rect;
        }

        private static RectTransform TextRect(Transform parent, string name, Vector2 min, Vector2 max, string value, int size, Color color, TextAnchor alignment)
        {
            RectTransform rect = RectObject(parent, name, min, max, Vector2.zero, Vector2.zero);
            TextMeshProUGUI text = rect.gameObject.AddComponent<TextMeshProUGUI>();
            text.text = value;
            text.font = _font;
            text.fontSize = size;
            text.color = color;
            text.alignment = TmpAlignment(alignment);
            text.textWrappingMode = TextWrappingModes.Normal;
            text.overflowMode = TextOverflowModes.Truncate;
            text.raycastTarget = false;
            return rect;
        }

        private static RectTransform ButtonRect(Transform parent, string name, string label, Vector2 min, Vector2 max, Color background, Color foreground, string labelName)
        {
            RectTransform rect = PanelRect(parent, name, min, max, Vector2.zero, Vector2.zero, background);
            Button button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = rect.GetComponent<Image>();
            ColorBlock colors = button.colors;
            colors.fadeDuration = 0.18f;
            colors.highlightedColor = new Color(1f, 0.84f, 0.42f, 1f);
            colors.pressedColor = new Color(0.94f, 0.49f, 0.16f, 1f);
            colors.selectedColor = new Color(1f, 0.75f, 0.25f, 1f);
            colors.disabledColor = new Color(0.38f, 0.35f, 0.32f, 0.42f);
            button.colors = colors;
            Outline selectedOutline = rect.gameObject.AddComponent<Outline>();
            selectedOutline.effectColor = new Color(1f, 0.78f, 0.22f, 0.95f);
            selectedOutline.effectDistance = new Vector2(2f, -2f);
            selectedOutline.useGraphicAlpha = true;
            KeyboardWandererButtonStateView stateView = rect.gameObject.AddComponent<KeyboardWandererButtonStateView>();
            stateView.Configure(selectedOutline);
            TextRect(rect, labelName, Vector2.zero, Vector2.one, label, 16, foreground, TextAnchor.MiddleCenter);
            return rect;
        }

        private static RectTransform ImageRect(Transform parent, string name, Vector2 min, Vector2 max, Sprite sprite, Color color)
        {
            RectTransform rect = RectObject(parent, name, min, max, Vector2.zero, Vector2.zero);
            Image image = rect.gameObject.AddComponent<Image>();
            image.sprite = sprite;
            image.color = color;
            image.preserveAspect = true;
            image.raycastTarget = false;
            return rect;
        }

        private static void AddShortcutKeycaps(Transform parent, string buttonName, params Sprite[] sprites)
        {
            Transform button = parent.Find(buttonName);
            if (button == null || sprites == null) return;

            int validCount = 0;
            float totalWidth = 0f;
            for (int i = 0; i < sprites.Length; i++)
            {
                if (sprites[i] == null) continue;
                validCount++;
                totalWidth += Mathf.Max(1f, sprites[i].rect.width);
            }
            if (validCount == 0) return;

            TMP_Text label = button.GetComponentInChildren<TMP_Text>(true);
            if (label != null) label.color = Color.clear;

            RectTransform keycapRoot = RectObject(button, "Ninja Keycaps", new Vector2(0.08f, 0.14f),
                new Vector2(0.92f, 0.86f), Vector2.zero, Vector2.zero);
            int cursor = 0;
            float cursorWidth = 0f;
            for (int i = 0; i < sprites.Length; i++)
            {
                if (sprites[i] == null) continue;
                float spriteWidth = Mathf.Max(1f, sprites[i].rect.width);
                RectTransform key = ImageRect(keycapRoot, "Key " + cursor,
                    new Vector2(cursorWidth / totalWidth, 0f),
                    new Vector2((cursorWidth + spriteWidth) / totalWidth, 1f), sprites[i], Color.white);
                key.offsetMin = new Vector2(1f, 0f);
                key.offsetMax = new Vector2(-1f, 0f);
                cursorWidth += spriteWidth;
                cursor++;
            }
        }

        private static void AddSkillIcon(Transform parent, string buttonName, Sprite sprite)
        {
            Transform button = parent.Find(buttonName);
            if (button == null || sprite == null) return;
            Image image = ImageRect(button, "Ninja UI Icon", new Vector2(0.68f, 0.54f),
                new Vector2(0.96f, 0.92f), sprite, Color.white).GetComponent<Image>();
            image.preserveAspect = true;
            image.raycastTarget = false;
        }

        private static void ApplyPanelSprite(RectTransform panel, Sprite sprite, Image.Type type)
        {
            if (panel == null || sprite == null) return;
            Image image = panel.GetComponent<Image>();
            image.sprite = sprite;
            image.type = type;
            image.color = Color.white;
        }

        private static void EnableBestFit(RectTransform rect, int minSize, int maxSize)
        {
            if (rect == null) return;
            TMP_Text text = rect.GetComponent<TMP_Text>();
            if (text == null) return;
            text.enableAutoSizing = true;
            text.fontSizeMin = minSize;
            text.fontSizeMax = maxSize;
        }

        private static void SliderRect(Transform parent, string name, Vector2 min, Vector2 max)
        {
            RectTransform root = PanelRect(parent, name, min, max, Vector2.zero, Vector2.zero, Ink);
            Slider slider = root.gameObject.AddComponent<Slider>();
            RectTransform fill = PanelRect(root, "Fill", new Vector2(0f, 0.15f), new Vector2(1f, 0.85f), new Vector2(4f, 0f), new Vector2(-4f, 0f), Gold);
            RectTransform handle = PanelRect(root, "Handle", new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(-7f, 0f), new Vector2(7f, 0f), Parchment);
            slider.fillRect = fill;
            slider.handleRect = handle;
            slider.targetGraphic = handle.GetComponent<Image>();
            slider.value = 0.65f;
        }

        private static void ToggleRect(Transform parent, string name, string label, Vector2 min, Vector2 max)
        {
            RectTransform root = RectObject(parent, name, min, max, Vector2.zero, Vector2.zero);
            Toggle toggle = root.gameObject.AddComponent<Toggle>();
            RectTransform box = PanelRect(root, "Background", new Vector2(0f, 0.2f), new Vector2(0.08f, 0.8f), Vector2.zero, Vector2.zero, Ink);
            RectTransform check = PanelRect(box, "Checkmark", new Vector2(0.2f, 0.2f), new Vector2(0.8f, 0.8f), Vector2.zero, Vector2.zero, Gold);
            TextRect(root, name + " Label", new Vector2(0.11f, 0f), Vector2.one, label, 16, Parchment, TextAnchor.MiddleLeft);
            toggle.targetGraphic = box.GetComponent<Image>();
            toggle.graphic = check.GetComponent<Image>();
            toggle.isOn = true;
        }

        private static RectTransform RectObject(Transform parent, string name, Vector2 min, Vector2 max, Vector2 offsetMin, Vector2 offsetMax)
        {
            GameObject gameObject = NewObject(name, parent, typeof(RectTransform));
            RectTransform rect = gameObject.GetComponent<RectTransform>();
            rect.anchorMin = min;
            rect.anchorMax = max;
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
            rect.localScale = Vector3.one;
            return rect;
        }

        private static GameObject NewObject(string name, Transform parent, params System.Type[] components)
        {
            GameObject gameObject = new GameObject(name, components);
            Undo.RegisterCreatedObjectUndo(gameObject, "Create " + name);
            if (parent != null) gameObject.transform.SetParent(parent, false);
            return gameObject;
        }

        private static void AddOutline(GameObject target, Color color, float distance)
        {
            Outline outline = target.AddComponent<Outline>();
            outline.effectColor = color;
            outline.effectDistance = new Vector2(distance, -distance);
        }

        private static TextAlignmentOptions TmpAlignment(TextAnchor alignment)
        {
            switch (alignment)
            {
                case TextAnchor.UpperLeft: return TextAlignmentOptions.TopLeft;
                case TextAnchor.UpperCenter: return TextAlignmentOptions.Top;
                case TextAnchor.UpperRight: return TextAlignmentOptions.TopRight;
                case TextAnchor.MiddleLeft: return TextAlignmentOptions.Left;
                case TextAnchor.MiddleRight: return TextAlignmentOptions.Right;
                case TextAnchor.LowerLeft: return TextAlignmentOptions.BottomLeft;
                case TextAnchor.LowerCenter: return TextAlignmentOptions.Bottom;
                case TextAnchor.LowerRight: return TextAlignmentOptions.BottomRight;
                default: return TextAlignmentOptions.Center;
            }
        }

        private static Color Hex(string value)
        {
            ColorUtility.TryParseHtmlString("#" + value, out Color color);
            return color;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
                return;
            int slash = path.LastIndexOf('/');
            string parent = path.Substring(0, slash);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, path.Substring(slash + 1));
        }
    }
}
