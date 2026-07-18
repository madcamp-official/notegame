using KeyboardWanderer.Demo;
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
        private static readonly Color Ink = Hex("160f0a");
        private static readonly Color Panel = Hex("281a11");
        private static readonly Color Raised = Hex("352419");
        private static readonly Color Gold = Hex("d3a64b");
        private static readonly Color Parchment = Hex("f0dfb6");
        private static readonly Color Muted = Hex("ad9878");
        private static Font _font;
        private static NinjaAdventureAssetManifest _assets;

        [MenuItem("Keyboard Wanderer/Rebuild Authored Scene UI")]
        public static void Build()
        {
            KeyboardWandererDemoController controller = Object.FindAnyObjectByType<KeyboardWandererDemoController>();
            if (controller == null)
                throw new UnityException("Open SampleScene and add Codria Game before building its UI.");

            _font = AssetDatabase.LoadAssetAtPath<Font>("Assets/NinjaAdventure/Ui/Font/NormalFont.ttf");
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

            GameObject eventSystem = NewObject("EventSystem", null, typeof(EventSystem), typeof(InputSystemUIInputModule));
            Undo.RegisterCreatedObjectUndo(eventSystem, "Create Codria EventSystem");
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            EditorSceneManager.SaveOpenScenes();
            Selection.activeGameObject = canvasObject;
        }

        private static void BuildTitle(Transform canvas)
        {
            RectTransform root = PanelRect(canvas, "Title Screen", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero,
                new Color(0.035f, 0.027f, 0.02f, 1f));
            RectTransform card = PanelRect(root, "Title Card", new Vector2(0.267f, 0.06f), new Vector2(0.733f, 0.94f),
                Vector2.zero, Vector2.zero, new Color(0.31f, 0.23f, 0.16f, 0.98f));
            AddOutline(card.gameObject, Gold, 2f);
            TextRect(card, "Title Heading", new Vector2(0.06f, 0.86f), new Vector2(0.94f, 0.96f), "넙죽이와 붕괴한 코드 왕국", 30, Gold, TextAnchor.MiddleCenter);
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
            RectTransform viewport = PanelRect(root, "World Viewport", new Vector2(0.181f, 0.244f), new Vector2(0.729f, 0.933f),
                Vector2.zero, Vector2.zero, new Color(1f, 1f, 1f, 0.01f));
            viewport.GetComponent<Image>().raycastTarget = false;

            RectTransform top = PanelRect(root, "Top Bar", new Vector2(0f, 0.933f), Vector2.one, Vector2.zero, Vector2.zero, Panel);
            TextRect(top, "Campaign Text", new Vector2(0.012f, 0.08f), new Vector2(0.28f, 0.92f), "캠페인", 15, Parchment, TextAnchor.MiddleLeft);
            TextRect(top, "Area Text", new Vector2(0.28f, 0.08f), new Vector2(0.68f, 0.92f), "현재 지역", 16, Parchment, TextAnchor.MiddleCenter);
            TextRect(top, "Metrics Text", new Vector2(0.68f, 0.08f), new Vector2(0.988f, 0.92f), "턴 · 권한 · 지표", 14, Muted, TextAnchor.MiddleRight);

            RectTransform left = PanelRect(root, "Left Rail", new Vector2(0f, 0.244f), new Vector2(0.181f, 0.933f), Vector2.zero, Vector2.zero, Panel);
            TextRect(left, "Context Text", new Vector2(0.07f, 0.04f), new Vector2(0.93f, 0.97f), "런 컨텍스트\n\n메인 목표\n\n보조 목표", 15, Parchment, TextAnchor.UpperLeft);
            ButtonRect(left, "Previous POI Button", "◀ POI", new Vector2(0.07f, 0.005f), new Vector2(0.47f, 0.045f), Raised, Parchment, "Previous POI Label");
            ButtonRect(left, "Next POI Button", "POI ▶", new Vector2(0.53f, 0.005f), new Vector2(0.93f, 0.045f), Raised, Parchment, "Next POI Label");

            RectTransform right = PanelRect(root, "Right Rail", new Vector2(0.729f, 0.244f), new Vector2(1f, 0.933f), Vector2.zero, Vector2.zero, Panel);
            TextRect(right, "Resolution Text", new Vector2(0.05f, 0.03f), new Vector2(0.95f, 0.97f), "1 · 판정\n\n2 · 상태 변화\n\n3 · 짧은 서사\n\n기억", 15, Parchment, TextAnchor.UpperLeft);

            RectTransform bottom = PanelRect(root, "Command Deck", Vector2.zero, new Vector2(1f, 0.244f), Vector2.zero, Vector2.zero, Panel);
            string[] abilities = { "Move", "Copy", "Delete", "Connect", "Restore", "Undo" };
            string[] labels = { "이동", "복제", "삭제", "연결", "복원", "되돌림" };
            for (int i = 0; i < abilities.Length; i++)
            {
                float x0 = 0.0125f + i * 0.067f;
                ButtonRect(bottom, "Ability " + abilities[i], labels[i], new Vector2(x0, 0.36f), new Vector2(x0 + 0.061f, 0.82f), Raised, Parchment, abilities[i] + " Label");
            }
            TextRect(bottom, "Execution Text", new Vector2(0.43f, 0.12f), new Vector2(0.84f, 0.88f), "실행 전 확인", 15, Parchment, TextAnchor.UpperLeft);
            ButtonRect(bottom, "Submit Button", "대상 선택 필요", new Vector2(0.852f, 0.16f), new Vector2(0.988f, 0.86f), Gold, Ink, "Submit Label");
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
            Text text = rect.gameObject.AddComponent<Text>();
            text.text = value;
            text.font = _font;
            text.fontSize = size;
            text.color = color;
            text.alignment = alignment;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.raycastTarget = false;
            return rect;
        }

        private static RectTransform ButtonRect(Transform parent, string name, string label, Vector2 min, Vector2 max, Color background, Color foreground, string labelName)
        {
            RectTransform rect = PanelRect(parent, name, min, max, Vector2.zero, Vector2.zero, background);
            Button button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = rect.GetComponent<Image>();
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

        private static Color Hex(string value)
        {
            ColorUtility.TryParseHtmlString("#" + value, out Color color);
            return color;
        }
    }
}
