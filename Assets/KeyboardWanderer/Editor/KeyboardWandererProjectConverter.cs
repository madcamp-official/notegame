using KeyboardWanderer.Demo;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace KeyboardWanderer.Editor
{
    public static class KeyboardWandererProjectConverter
    {
        private const string RootFolder = "Assets/KeyboardWanderer";
        private const string PrefabFolder = RootFolder + "/Prefabs";
        private const string WorldPrefabFolder = PrefabFolder + "/World";
        private const string DataFolder = RootFolder + "/ScriptableObjects";
        private const string EntityPrefabPath = WorldPrefabFolder + "/EntityVisual.prefab";
        private const string LandmarkPrefabPath = WorldPrefabFolder + "/Landmark.prefab";
        private const string SettingsPath = DataFolder + "/KeyboardWandererAuthoringSettings.asset";
        private const string ManifestPath = RootFolder + "/Resources/NinjaAdventureAssetManifest.asset";

        [MenuItem("Keyboard Wanderer/Convert Runtime Composition to Authored Assets")]
        public static void Convert()
        {
            if (Application.isPlaying)
                throw new UnityException("Run the authored asset conversion outside Play Mode.");

            EnsureFolder(PrefabFolder);
            EnsureFolder(WorldPrefabFolder);
            EnsureFolder(DataFolder);
            EnsureFolder(PrefabFolder + "/UI");

            NinjaAdventureManifestBuilder.RebuildManifest();
            KeyboardWandererEntityView entityPrefab = BuildEntityPrefab();
            GameObject landmarkPrefab = BuildLandmarkPrefab();
            NinjaAdventureAssetManifest manifest = AssetDatabase.LoadAssetAtPath<NinjaAdventureAssetManifest>(ManifestPath);

            KeyboardWandererAuthoringSettings settings =
                AssetDatabase.LoadAssetAtPath<KeyboardWandererAuthoringSettings>(SettingsPath);
            if (settings == null)
            {
                settings = ScriptableObject.CreateInstance<KeyboardWandererAuthoringSettings>();
                AssetDatabase.CreateAsset(settings, SettingsPath);
            }
            settings.Configure(manifest, entityPrefab, landmarkPrefab);
            EditorUtility.SetDirty(settings);

            KeyboardWandererDemoController controller = Object.FindAnyObjectByType<KeyboardWandererDemoController>();
            if (controller == null)
                throw new UnityException("Open SampleScene before converting the project.");

            KeyboardWandererWorldView world = EnsureAuthoredWorld(controller.transform);
            Camera sceneCamera = EnsureSceneCamera();
            EnsureAuthoredAudio(controller.transform, out AudioSource music, out AudioSource sfx);
            controller.ConfigureAuthoredContent(settings, manifest, world, sceneCamera, music, sfx);
            EditorUtility.SetDirty(controller);

            KeyboardWandererSceneUIBuilder.Build();
            EditorSceneManager.MarkSceneDirty(controller.gameObject.scene);
            if (!EditorSceneManager.SaveScene(controller.gameObject.scene))
                throw new UnityException("Failed to save the authored scene.");

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Selection.activeObject = settings;
            Debug.Log("Keyboard Wanderer conversion complete: Scene objects, UI prefab, world prefabs, and authoring settings are now persistent assets.");
        }

        private static KeyboardWandererEntityView BuildEntityPrefab()
        {
            var root = new GameObject("Entity Visual");
            var view = root.AddComponent<KeyboardWandererEntityView>();

            GameObject actorObject = Child(root.transform, "Actor");
            SpriteRenderer actor = actorObject.AddComponent<SpriteRenderer>();

            GameObject healthBackObject = Child(root.transform, "Health Back");
            SpriteRenderer healthBack = healthBackObject.AddComponent<SpriteRenderer>();
            healthBack.color = new Color(0.08f, 0.035f, 0.025f, 0.95f);
            healthBack.sortingOrder = 510;
            healthBackObject.SetActive(false);

            GameObject healthFillObject = Child(root.transform, "Health Fill");
            SpriteRenderer healthFill = healthFillObject.AddComponent<SpriteRenderer>();
            healthFill.color = Color.red;
            healthFill.sortingOrder = 511;
            healthFillObject.SetActive(false);

            GameObject labelObject = Child(root.transform, "Finale Label");
            TextMesh label = labelObject.AddComponent<TextMesh>();
            label.anchor = TextAnchor.MiddleCenter;
            label.alignment = TextAlignment.Center;
            label.fontSize = 34;
            label.characterSize = 0.075f;
            label.fontStyle = FontStyle.Bold;
            MeshRenderer labelRenderer = labelObject.GetComponent<MeshRenderer>();
            if (labelRenderer != null)
                labelRenderer.sortingOrder = 525;
            labelObject.SetActive(false);

            view.Configure(actor, healthBack, healthFill, label);
            PrefabUtility.SaveAsPrefabAsset(root, EntityPrefabPath, out bool success);
            Object.DestroyImmediate(root);
            if (!success)
                throw new UnityException("Failed to create the entity visual prefab.");
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(EntityPrefabPath);
            return prefab != null ? prefab.GetComponent<KeyboardWandererEntityView>() : null;
        }

        private static GameObject BuildLandmarkPrefab()
        {
            var root = new GameObject("Landmark");
            SpriteRenderer renderer = root.AddComponent<SpriteRenderer>();
            renderer.sortingOrder = 80;
            PrefabUtility.SaveAsPrefabAsset(root, LandmarkPrefabPath, out bool success);
            Object.DestroyImmediate(root);
            if (!success)
                throw new UnityException("Failed to create the landmark prefab.");
            return AssetDatabase.LoadAssetAtPath<GameObject>(LandmarkPrefabPath);
        }

        private static KeyboardWandererWorldView EnsureAuthoredWorld(Transform controller)
        {
            Transform existing = controller.Find("Authored World");
            GameObject root = existing != null ? existing.gameObject : Child(controller, "Authored World");
            GameObjectUtility.RemoveMonoBehavioursWithMissingScript(root);
            if (root.GetComponent<Grid>() == null)
                root.AddComponent<Grid>();
            KeyboardWandererWorldView view = root.GetComponent<KeyboardWandererWorldView>();
            if (view == null)
                view = root.AddComponent<KeyboardWandererWorldView>();

            Transform terrainTransform = root.transform.Find("Immutable Terrain Tilemap");
            GameObject terrainObject = terrainTransform != null
                ? terrainTransform.gameObject
                : Child(root.transform, "Immutable Terrain Tilemap");
            Tilemap tilemap = terrainObject.GetComponent<Tilemap>();
            if (tilemap == null)
                tilemap = terrainObject.AddComponent<Tilemap>();
            TilemapRenderer tilemapRenderer = terrainObject.GetComponent<TilemapRenderer>();
            if (tilemapRenderer == null)
                tilemapRenderer = terrainObject.AddComponent<TilemapRenderer>();
            tilemapRenderer.sortingOrder = 0;

            Transform dynamicRoot = root.transform.Find("Dynamic Objects");
            if (dynamicRoot == null)
                dynamicRoot = Child(root.transform, "Dynamic Objects").transform;

            Transform selectionTransform = root.transform.Find("Selection Cursor");
            GameObject selectionObject = selectionTransform != null
                ? selectionTransform.gameObject
                : Child(root.transform, "Selection Cursor");
            SpriteRenderer selection = selectionObject.GetComponent<SpriteRenderer>();
            if (selection == null)
                selection = selectionObject.AddComponent<SpriteRenderer>();
            selection.sortingOrder = 900;
            selection.enabled = false;

            view.Configure(tilemap, dynamicRoot, selection);
            root.SetActive(false);
            EditorUtility.SetDirty(view);
            return view;
        }

        private static Camera EnsureSceneCamera()
        {
            Camera camera = Camera.main ?? Object.FindAnyObjectByType<Camera>();
            if (camera == null)
            {
                var cameraObject = new GameObject("Main Camera");
                cameraObject.tag = "MainCamera";
                camera = cameraObject.AddComponent<Camera>();
            }
            camera.tag = "MainCamera";
            camera.orthographic = true;
            if (camera.GetComponent<AudioListener>() == null)
                camera.gameObject.AddComponent<AudioListener>();
            EditorUtility.SetDirty(camera);
            return camera;
        }

        private static void EnsureAuthoredAudio(Transform controller, out AudioSource music, out AudioSource sfx)
        {
            Transform audioRoot = controller.Find("Authored Audio");
            if (audioRoot == null)
                audioRoot = Child(controller, "Authored Audio").transform;
            music = EnsureAudioSource(audioRoot, "Music", true);
            sfx = EnsureAudioSource(audioRoot, "SFX", false);
        }

        private static AudioSource EnsureAudioSource(Transform parent, string name, bool loop)
        {
            Transform existing = parent.Find(name);
            GameObject item = existing != null ? existing.gameObject : Child(parent, name);
            AudioSource source = item.GetComponent<AudioSource>();
            if (source == null)
                source = item.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = loop;
            return source;
        }

        private static GameObject Child(Transform parent, string name)
        {
            var child = new GameObject(name);
            child.transform.SetParent(parent, false);
            return child;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
                return;
            int slash = path.LastIndexOf('/');
            string parent = path.Substring(0, slash);
            string name = path.Substring(slash + 1);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, name);
        }
    }
}
