using System;
using System.Collections.Generic;
using KeyboardWanderer.Demo;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace KeyboardWanderer.Editor
{
    public static class AuthoredUiMigration
    {
        private const string UiPrefabPath = "Assets/KeyboardWanderer/Prefabs/UI/AuthoredUI.prefab";
        private const string ScreenFolder = "Assets/KeyboardWanderer/Prefabs/UI/Screens";

        [MenuItem("Keyboard Wanderer/Migrate Authored UI to Components")]
        public static void Upgrade()
        {
            TMP_FontAsset font = EnsureFontAsset();
            EnsureFolder(ScreenFolder);

            GameObject root = PrefabUtility.LoadPrefabContents(UiPrefabPath);
            try
            {
                ConvertLegacyTexts(root, font);
                EnsureMinimap(root);
                ConfigureButtons(root);

                Transform gameHud = Find(root.transform, "Game HUD");
                if (gameHud != null)
                {
                    ExtractNested(gameHud, "Story Panel", ScreenFolder + "/DialoguePanel.prefab");
                    ExtractNested(gameHud, "Minimap Panel", ScreenFolder + "/MinimapPanel.prefab");
                }

                ExtractNested(root.transform, "Title Screen", ScreenFolder + "/TitleScreen.prefab");
                ExtractNested(root.transform, "Game HUD", ScreenFolder + "/GameHUD.prefab");
                ExtractNested(root.transform, "Settings Screen", ScreenFolder + "/SettingsScreen.prefab");
                ExtractNested(root.transform, "Pause Screen", ScreenFolder + "/PauseScreen.prefab");
                ExtractNested(root.transform, "Ending Screen", ScreenFolder + "/EndingScreen.prefab");

                KeyboardWandererSceneUI sceneUi = root.GetComponent<KeyboardWandererSceneUI>();
                sceneUi.AutoWire();
                EditorUtility.SetDirty(sceneUi);
                PrefabUtility.SaveAsPrefabAsset(root, UiPrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity", OpenSceneMode.Single);
            EditorSceneManager.SaveOpenScenes();
            Debug.Log("Migrated AuthoredUI to TMP, serialized minimap/button views, and nested screen prefabs.");
        }

        public static void EnsureTmpSettingsAsset()
        {
            const string settingsFolder = "Assets/TextMesh Pro/Resources";
            const string settingsPath = settingsFolder + "/TMP Settings.asset";
            if (AssetDatabase.LoadAssetAtPath<TMP_Settings>(settingsPath) != null)
                return;
            EnsureFolder(settingsFolder);
            TMP_Settings settings = ScriptableObject.CreateInstance<TMP_Settings>();
            AssetDatabase.CreateAsset(settings, settingsPath);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            AssetDatabase.SaveAssets();
            TMP_Settings.LoadDefaultSettings();
        }

        private static TMP_FontAsset EnsureFontAsset()
        {
            EnsureTmpSettingsAsset();
            return KeyboardWandererFontAssetAuthoring.EnsureProjectFontAsset();
        }

        private static void ConvertLegacyTexts(GameObject root, TMP_FontAsset font)
        {
            Text[] legacyTexts = root.GetComponentsInChildren<Text>(true);
            foreach (Text legacy in legacyTexts)
            {
                string value = legacy.text;
                float size = legacy.fontSize;
                Color color = legacy.color;
                TextAnchor alignment = legacy.alignment;
                bool bestFit = legacy.resizeTextForBestFit;
                float minSize = legacy.resizeTextMinSize;
                float maxSize = legacy.resizeTextMaxSize;
                bool raycastTarget = legacy.raycastTarget;

                GameObject textObject = legacy.gameObject;
                UnityEngine.Object.DestroyImmediate(legacy, true);
                TextMeshProUGUI replacement = textObject.AddComponent<TextMeshProUGUI>();
                replacement.text = value;
                replacement.font = font;
                replacement.fontSize = size;
                replacement.color = color;
                replacement.alignment = ConvertAlignment(alignment);
                replacement.textWrappingMode = TextWrappingModes.Normal;
                replacement.overflowMode = TextOverflowModes.Truncate;
                replacement.enableAutoSizing = bestFit;
                replacement.fontSizeMin = minSize;
                replacement.fontSizeMax = maxSize;
                replacement.raycastTarget = raycastTarget;
            }
        }

        private static void EnsureMinimap(GameObject root)
        {
            Transform preview = Find(root.transform, "Minimap Preview");
            if (preview == null || Find(preview, "Minimap Map") != null)
                return;

            var mapObject = new GameObject("Minimap Map", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            RectTransform rect = mapObject.GetComponent<RectTransform>();
            rect.SetParent(preview, false);
            rect.anchorMin = new Vector2(0.04f, 0.06f);
            rect.anchorMax = new Vector2(0.96f, 0.94f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            Image image = mapObject.GetComponent<Image>();
            image.preserveAspect = true;
            image.raycastTarget = false;
            image.enabled = false;
            rect.SetAsFirstSibling();
        }

        private static void ConfigureButtons(GameObject root)
        {
            foreach (Button button in root.GetComponentsInChildren<Button>(true))
            {
                ColorBlock colors = button.colors;
                colors.fadeDuration = 0.18f;
                colors.highlightedColor = new Color(1f, 0.84f, 0.42f, 1f);
                colors.pressedColor = new Color(0.94f, 0.49f, 0.16f, 1f);
                colors.selectedColor = new Color(1f, 0.75f, 0.25f, 1f);
                colors.disabledColor = new Color(0.38f, 0.35f, 0.32f, 0.42f);
                button.colors = colors;

                Graphic target = button.targetGraphic;
                if (target == null)
                    continue;
                Outline outline = target.GetComponent<Outline>();
                if (outline == null)
                    outline = target.gameObject.AddComponent<Outline>();
                outline.effectColor = new Color(1f, 0.78f, 0.22f, 0.95f);
                outline.effectDistance = new Vector2(2f, -2f);
                outline.useGraphicAlpha = true;

                KeyboardWandererButtonStateView stateView =
                    button.GetComponent<KeyboardWandererButtonStateView>();
                if (stateView == null)
                    stateView = button.gameObject.AddComponent<KeyboardWandererButtonStateView>();
                stateView.Configure(outline);
                EditorUtility.SetDirty(button);
                EditorUtility.SetDirty(stateView);
            }
        }

        private static void ExtractNested(Transform parent, string childName, string prefabPath)
        {
            Transform child = Find(parent, childName);
            if (child == null || child.parent != parent)
                return;
            if (PrefabUtility.IsPartOfPrefabInstance(child.gameObject))
                return;

            int sibling = child.GetSiblingIndex();
            bool active = child.gameObject.activeSelf;
            GameObject asset = PrefabUtility.SaveAsPrefabAsset(child.gameObject, prefabPath);
            UnityEngine.Object.DestroyImmediate(child.gameObject);
            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(asset, parent);
            instance.name = childName;
            instance.SetActive(active);
            instance.transform.SetSiblingIndex(sibling);
        }

        private static Transform Find(Transform root, string name)
        {
            var queue = new Queue<Transform>();
            queue.Enqueue(root);
            while (queue.Count > 0)
            {
                Transform current = queue.Dequeue();
                if (string.Equals(current.name, name, StringComparison.Ordinal))
                    return current;
                for (int i = 0; i < current.childCount; i++)
                    queue.Enqueue(current.GetChild(i));
            }
            return null;
        }

        private static TextAlignmentOptions ConvertAlignment(TextAnchor alignment)
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
