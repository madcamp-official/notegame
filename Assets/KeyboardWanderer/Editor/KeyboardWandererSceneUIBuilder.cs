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
        private const string DefaultFontPath =
            "Assets/KeyboardWanderer/Resources/Fonts/NeoDunggeunmoPro-Regular.ttf";
        private const string DefaultTmpFontPath =
            "Assets/KeyboardWanderer/Resources/Fonts/NeoDunggeunmoPro-Regular SDF.asset";
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

            GameObject prefabRoot = PrefabUtility.LoadPrefabContents(UiPrefabPath);
            try
            {
                Transform oldHud = prefabRoot.transform.Find("Game HUD");
                if (oldHud != null) Object.DestroyImmediate(oldHud.gameObject);
                BuildGameHud(prefabRoot.transform);

                KeyboardWandererSceneUI sceneUi = prefabRoot.GetComponent<KeyboardWandererSceneUI>();
                sceneUi.AutoWire();
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
            TMP_FontAsset fontAsset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(DefaultTmpFontPath);
            if (fontAsset != null)
                return fontAsset;

            Font sourceFont = AssetDatabase.LoadAssetAtPath<Font>(DefaultFontPath);
            if (sourceFont == null)
                throw new UnityException("Default authored UI font is missing: " + DefaultFontPath);
            fontAsset = TMP_FontAsset.CreateFontAsset(sourceFont);
            Texture2D atlas = fontAsset.atlasTexture;
            Material material = fontAsset.material;
            fontAsset.atlasPopulationMode = AtlasPopulationMode.Dynamic;
            fontAsset.isMultiAtlasTexturesEnabled = true;
            AssetDatabase.CreateAsset(fontAsset, DefaultTmpFontPath);
            AssetDatabase.AddObjectToAsset(atlas, fontAsset);
            AssetDatabase.AddObjectToAsset(material, fontAsset);
            AssetDatabase.SaveAssets();
            return fontAsset;
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

            RectTransform header = PanelRect(root, "Story Header", new Vector2(0.018f, 0.82f), new Vector2(0.265f, 0.975f),
                Vector2.zero, Vector2.zero, new Color(0.055f, 0.045f, 0.035f, 0.94f));
            ApplyPanelSprite(header, _assets != null ? _assets.FacesetBox : null, Image.Type.Sliced);
            RectTransform hudPortrait = PanelRect(header, "HUD Portrait Frame", new Vector2(0.035f, 0.15f), new Vector2(0.285f, 0.88f),
                Vector2.zero, Vector2.zero, new Color(0.10f, 0.08f, 0.065f, 1f));
            ImageRect(hudPortrait, "HUD Portrait", new Vector2(0.12f, 0.12f), new Vector2(0.88f, 0.88f),
                _assets != null ? _assets.PlayerIdle : null, Color.white);
            TextRect(header, "Scene Location", new Vector2(0.33f, 0.55f), new Vector2(0.94f, 0.84f), "현재 장소", 16, Gold, TextAnchor.MiddleLeft);
            TextRect(header, "Scene Title", new Vector2(0.33f, 0.20f), new Vector2(0.94f, 0.55f), "현재 장면", 20, Parchment, TextAnchor.MiddleLeft);

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

            RectTransform story = PanelRect(root, "Story Panel", new Vector2(0.245f, 0.025f), new Vector2(0.82f, 0.19f),
                Vector2.zero, Vector2.zero, new Color(0.80f, 0.63f, 0.39f, 0.98f));
            ApplyPanelSprite(story, _assets != null ? _assets.DialogueBoxFaceset : null, Image.Type.Simple);
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
            RectTransform storyText = TextRect(speech, "Story Text", new Vector2(0.055f, 0.34f), new Vector2(0.95f, 0.76f),
                "이곳에서 벌어진 이야기가 표시됩니다.", 14, new Color(0.35f, 0.20f, 0.13f, 1f), TextAnchor.UpperLeft);
            EnableBestFit(storyText, 10, 14);
            ButtonRect(speech, "Next Dialogue Button", "다음 ▶", new Vector2(0.86f, 0.10f), new Vector2(0.96f, 0.28f),
                new Color(0.58f, 0.37f, 0.23f, 1f), Parchment, "Next Dialogue Label");
            RectTransform actionHint = TextRect(speech, "Action Hint", new Vector2(0.055f, 0.10f), new Vector2(0.82f, 0.28f),
                "대화를 읽은 뒤 이동하거나 스킬을 사용할 수 있습니다.", 11, new Color(0.49f, 0.31f, 0.20f, 1f), TextAnchor.MiddleLeft);
            EnableBestFit(actionHint, 8, 11);
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
            colors.fadeDuration = 0.055f;
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
