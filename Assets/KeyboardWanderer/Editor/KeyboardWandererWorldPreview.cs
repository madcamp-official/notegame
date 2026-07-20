using KeyboardWanderer.Demo;
using KeyboardWanderer.Gameplay;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;

namespace KeyboardWanderer.Editor
{
    internal sealed class KeyboardWandererWorldPreviewResources : MonoBehaviour
    {
        public Texture2D Texture;
        public Sprite Sprite;
        public WorldRenderer Renderer;

        private void OnDestroy()
        {
            Renderer?.Dispose();
            if (Sprite != null) DestroyImmediate(Sprite);
            if (Texture != null) DestroyImmediate(Texture);
        }
    }

    public sealed class KeyboardWandererWorldPreview : EditorWindow
    {
        private const string PreviewRootName = "[Keyboard Wanderer World Preview]";
        private const string ProfilePath = "Assets/KeyboardWanderer/ScriptableObjects/KeyboardWandererWorldVisualProfile.asset";
        private const string SettingsPath = "Assets/KeyboardWanderer/ScriptableObjects/KeyboardWandererAuthoringSettings.asset";
        private long _seed = 20260719L;
        private string _lastLayoutHash = string.Empty;

        [MenuItem("Keyboard Wanderer/World Preview")]
        private static void Open() => GetWindow<KeyboardWandererWorldPreview>("World Preview");

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Deterministic Region Preview", EditorStyles.boldLabel);
            _seed = EditorGUILayout.LongField("Seed", _seed);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Generate")) _lastLayoutHash = GeneratePreview(_seed);
                if (GUILayout.Button("Clear")) ClearPreview();
            }
            EditorGUILayout.SelectableLabel(string.IsNullOrEmpty(_lastLayoutHash) ? "No preview" : _lastLayoutHash,
                EditorStyles.helpBox, GUILayout.Height(EditorGUIUtility.singleLineHeight * 2f));
            EditorGUILayout.HelpBox("Preview objects use DontSaveInEditor/DontSaveInBuild and are removed before Play Mode.", MessageType.Info);
        }

        public static string GeneratePreview(long seed)
        {
            Scene scene = SceneManager.GetActiveScene();
            ClearPreviewInternal(false);

            LocalTurnService service = LocalTurnService.CreateDemo(seed);
            var root = new GameObject(PreviewRootName, typeof(Grid), typeof(KeyboardWandererWorldPreviewResources));
            SetPreviewFlags(root);
            var tileObject = new GameObject("Terrain Preview", typeof(Tilemap), typeof(TilemapRenderer));
            tileObject.transform.SetParent(root.transform, false);
            SetPreviewFlags(tileObject);

            Texture2D texture = new Texture2D(1, 1, TextureFormat.RGBA32, false)
            {
                name = "World Preview Pixel",
                filterMode = FilterMode.Point,
                hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild
            };
            texture.SetPixel(0, 0, Color.white);
            texture.Apply();
            Sprite sprite = Sprite.Create(texture, new Rect(0, 0, 1, 1), Vector2.zero, 1f);
            sprite.name = "World Preview Pixel";
            sprite.hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild;

            KeyboardWandererWorldVisualProfile profile = AssetDatabase.LoadAssetAtPath<KeyboardWandererWorldVisualProfile>(ProfilePath);
            var renderer = new WorldRenderer();
            renderer.Render(tileObject.GetComponent<Tilemap>(), service.CurrentView, profile, sprite);
            KeyboardWandererWorldPreviewResources resources = root.GetComponent<KeyboardWandererWorldPreviewResources>();
            resources.Texture = texture;
            resources.Sprite = sprite;
            resources.Renderer = renderer;
            root.transform.position = new Vector3(-service.CurrentView.Region.Width * 0.5f,
                -service.CurrentView.Region.Height * 0.5f, 0f);
            Selection.activeGameObject = root;
            SceneView.lastActiveSceneView?.FrameSelected();
            return service.CurrentView.Region.LayoutHash;
        }

        public static void ClearPreview()
        {
            ClearPreviewInternal(true);
        }

        private static void ClearPreviewInternal(bool clearSelection)
        {
            GameObject[] objects = Resources.FindObjectsOfTypeAll<GameObject>();
            for (int i = objects.Length - 1; i >= 0; i--)
            {
                GameObject item = objects[i];
                if (item != null && item.scene.IsValid() && item.name == PreviewRootName)
                    DestroyImmediate(item);
            }
            if (clearSelection && Selection.activeGameObject != null && Selection.activeGameObject.name == PreviewRootName)
                Selection.activeObject = null;
        }

        private static void SetPreviewFlags(GameObject item) =>
            item.hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild;

        [InitializeOnLoadMethod]
        private static void InstallPlayModeCleanup()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.ExitingEditMode) ClearPreview();
        }

        [MenuItem("Keyboard Wanderer/Rebuild Default World Visual Profile")]
        public static void RebuildDefaultProfile()
        {
            KeyboardWandererWorldVisualProfile profile = AssetDatabase.LoadAssetAtPath<KeyboardWandererWorldVisualProfile>(ProfilePath);
            if (profile == null)
            {
                profile = CreateInstance<KeyboardWandererWorldVisualProfile>();
                AssetDatabase.CreateAsset(profile, ProfilePath);
            }
            profile.ConfigureDefaults();
            EditorUtility.SetDirty(profile);
            KeyboardWandererAuthoringSettings settings = AssetDatabase.LoadAssetAtPath<KeyboardWandererAuthoringSettings>(SettingsPath);
            if (settings != null)
            {
                settings.SetWorldVisualProfile(profile);
                EditorUtility.SetDirty(settings);
            }
            AssetDatabase.SaveAssets();
            Debug.Log("Keyboard Wanderer default world visual profile rebuilt.");
        }
    }
}
