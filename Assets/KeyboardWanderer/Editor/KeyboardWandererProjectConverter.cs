using KeyboardWanderer.Demo;
using KeyboardWanderer.Runtime;
using KeyboardWanderer.World;
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
            KeyboardWandererVisualAssetLibrary visualAssetLibrary =
                EnsureVisualAssetLibrary(world, manifest);
            KeyboardWandererMinimapRenderer minimapRenderer =
                world.GetComponent<KeyboardWandererMinimapRenderer>();
            if (minimapRenderer == null)
                minimapRenderer = world.gameObject.AddComponent<KeyboardWandererMinimapRenderer>();
            KeyboardWandererPathPlanner pathPlanner = world.GetComponent<KeyboardWandererPathPlanner>();
            if (pathPlanner == null)
                pathPlanner = world.gameObject.AddComponent<KeyboardWandererPathPlanner>();
            KeyboardWandererBiomeDecorationRenderer decorationRenderer = EnsureDecorationRenderer(world);
            KeyboardWandererEntityVisualFactory entityVisualFactory =
                EnsureEntityVisualFactory(world, entityPrefab);
            KeyboardWandererEntityAnimationDriver entityAnimationDriver =
                EnsureEntityAnimationDriver(world);
            Camera sceneCamera = EnsureSceneCamera(out KeyboardWandererCameraController cameraController);
            KeyboardWandererAudioController audioController =
                EnsureAuthoredAudio(controller.transform, out AudioSource music, out AudioSource sfx);
            KeyboardWandererInputRouter inputRouter = controller.GetComponent<KeyboardWandererInputRouter>();
            if (inputRouter == null)
                inputRouter = controller.gameObject.AddComponent<KeyboardWandererInputRouter>();
            KeyboardWandererSelectionController selectionController =
                controller.GetComponent<KeyboardWandererSelectionController>();
            if (selectionController == null)
                selectionController = controller.gameObject.AddComponent<KeyboardWandererSelectionController>();
            KeyboardWandererAbilityAvailability abilityAvailability =
                controller.GetComponent<KeyboardWandererAbilityAvailability>();
            if (abilityAvailability == null)
                abilityAvailability = controller.gameObject.AddComponent<KeyboardWandererAbilityAvailability>();
            KeyboardWandererTurnCoordinator turnCoordinator =
                controller.GetComponent<KeyboardWandererTurnCoordinator>();
            if (turnCoordinator == null)
                turnCoordinator = controller.gameObject.AddComponent<KeyboardWandererTurnCoordinator>();
            KeyboardWandererRunSessionController runSessionController =
                controller.GetComponent<KeyboardWandererRunSessionController>();
            if (runSessionController == null)
                runSessionController = controller.gameObject.AddComponent<KeyboardWandererRunSessionController>();
            KeyboardWandererSettingsController settingsController =
                controller.GetComponent<KeyboardWandererSettingsController>();
            if (settingsController == null)
                settingsController = controller.gameObject.AddComponent<KeyboardWandererSettingsController>();
            settingsController.Configure(audioController);
            if (controller.GetComponent<SceneSequencePlayer>() == null)
                controller.gameObject.AddComponent<SceneSequencePlayer>();
            controller.ConfigureAuthoredContent(
                settings, manifest, world, sceneCamera, cameraController, music, sfx, audioController,
                inputRouter, selectionController, abilityAvailability, turnCoordinator, runSessionController, settingsController,
                visualAssetLibrary, minimapRenderer, pathPlanner, decorationRenderer, entityVisualFactory,
                entityAnimationDriver);
            EditorUtility.SetDirty(controller);

            // Preserve scene/prefab UI edits. Rebuild only when no authored UI exists yet.
            KeyboardWandererSceneUIBuilder.EnsureExistingOrBuild();
            EditorSceneManager.MarkSceneDirty(controller.gameObject.scene);
            if (!EditorSceneManager.SaveScene(controller.gameObject.scene))
                throw new UnityException("Failed to save the authored scene.");

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            KeyboardWandererAuthoringValidator.ValidateOrThrow(controller);
            Selection.activeObject = settings;
            Debug.Log("Keyboard Wanderer conversion complete: Scene objects, UI prefab, world prefabs, and authoring settings are now persistent assets.");
        }

        private static KeyboardWandererEntityView BuildEntityPrefab()
        {
            var root = new GameObject("Entity Visual");
            var view = root.AddComponent<KeyboardWandererEntityView>();

            GameObject actorObject = Child(root.transform, "Actor");
            SpriteRenderer actor = actorObject.AddComponent<SpriteRenderer>();
            actorObject.AddComponent<Animator>();

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
            tilemapRenderer.sortingOrder = -1000;

            Transform staticRoot = EnsureChild(root.transform, "Static Objects");
            Transform runtimeRoot = EnsureChild(root.transform, "Runtime Objects");
            Transform entityRoot = EnsureChild(runtimeRoot, "Entities");
            Transform landmarkRoot = EnsureChild(runtimeRoot, "Landmarks");
            Transform effectsRoot = EnsureChild(runtimeRoot, "Effects");

            Transform selectionTransform = root.transform.Find("Selection Cursor");
            GameObject selectionObject = selectionTransform != null
                ? selectionTransform.gameObject
                : Child(root.transform, "Selection Cursor");
            SpriteRenderer selection = selectionObject.GetComponent<SpriteRenderer>();
            if (selection == null)
                selection = selectionObject.AddComponent<SpriteRenderer>();
            selection.sortingOrder = 900;
            selection.enabled = false;

            view.Configure(tilemap, staticRoot, entityRoot, landmarkRoot, effectsRoot, selection);
            root.SetActive(false);
            EditorUtility.SetDirty(view);
            return view;
        }

        private static KeyboardWandererBiomeDecorationRenderer EnsureDecorationRenderer(
            KeyboardWandererWorldView world)
        {
            KeyboardWandererBiomeDecorationRenderer renderer =
                world.GetComponent<KeyboardWandererBiomeDecorationRenderer>();
            if (renderer == null)
                renderer = world.gameObject.AddComponent<KeyboardWandererBiomeDecorationRenderer>();
            Transform pool = EnsureChild(world.transform, "Decoration Pool");
            pool.gameObject.SetActive(false);
            renderer.Configure(world.RuntimeLandmarks, pool);
            EditorUtility.SetDirty(renderer);
            return renderer;
        }

        private static KeyboardWandererVisualAssetLibrary EnsureVisualAssetLibrary(
            KeyboardWandererWorldView world, NinjaAdventureAssetManifest manifest)
        {
            KeyboardWandererVisualAssetLibrary library =
                world.GetComponent<KeyboardWandererVisualAssetLibrary>();
            if (library == null)
                library = world.gameObject.AddComponent<KeyboardWandererVisualAssetLibrary>();
            library.ConfigureManifest(manifest);
            EditorUtility.SetDirty(library);
            return library;
        }

        private static KeyboardWandererEntityVisualFactory EnsureEntityVisualFactory(
            KeyboardWandererWorldView world, KeyboardWandererEntityView prefab)
        {
            KeyboardWandererEntityVisualFactory factory =
                world.GetComponent<KeyboardWandererEntityVisualFactory>();
            if (factory == null)
                factory = world.gameObject.AddComponent<KeyboardWandererEntityVisualFactory>();
            Transform pool = EnsureChild(world.transform, "Entity Pool");
            pool.gameObject.SetActive(false);
            factory.Configure(prefab, world.RuntimeEntities, pool);
            EditorUtility.SetDirty(factory);
            return factory;
        }

        private static KeyboardWandererEntityAnimationDriver EnsureEntityAnimationDriver(
            KeyboardWandererWorldView world)
        {
            KeyboardWandererEntityAnimationDriver driver =
                world.GetComponent<KeyboardWandererEntityAnimationDriver>();
            if (driver == null)
                driver = world.gameObject.AddComponent<KeyboardWandererEntityAnimationDriver>();
            EditorUtility.SetDirty(driver);
            return driver;
        }

        private static Camera EnsureSceneCamera(out KeyboardWandererCameraController cameraController)
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
            cameraController = camera.GetComponent<KeyboardWandererCameraController>();
            if (cameraController == null)
                cameraController = camera.gameObject.AddComponent<KeyboardWandererCameraController>();
            cameraController.Configure(camera);
            EditorUtility.SetDirty(cameraController);
            EditorUtility.SetDirty(camera);
            return camera;
        }

        private static KeyboardWandererAudioController EnsureAuthoredAudio(
            Transform controller,
            out AudioSource music,
            out AudioSource sfx)
        {
            Transform audioRoot = controller.Find("Authored Audio");
            if (audioRoot == null)
                audioRoot = Child(controller, "Authored Audio").transform;
            music = EnsureAudioSource(audioRoot, "Music", true);
            sfx = EnsureAudioSource(audioRoot, "SFX", false);
            KeyboardWandererAudioController audio = audioRoot.GetComponent<KeyboardWandererAudioController>();
            if (audio == null)
                audio = audioRoot.gameObject.AddComponent<KeyboardWandererAudioController>();
            audio.Configure(music, sfx);
            EditorUtility.SetDirty(audio);
            return audio;
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

        private static Transform EnsureChild(Transform parent, string name)
        {
            Transform existing = parent.Find(name);
            return existing != null ? existing : Child(parent, name).transform;
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
