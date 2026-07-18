using System;
using System.Collections.Generic;
using KeyboardWanderer.Demo;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace KeyboardWanderer.Editor
{
    public static class KeyboardWandererAuthoringValidator
    {
        private static readonly string[] RequiredUiObjects =
        {
            "Title Screen", "Game HUD", "Settings Screen", "Pause Screen", "Ending Screen",
            "Search Skill Button", "Select All Skill Button", "Speaker Emote"
        };

        [MenuItem("Keyboard Wanderer/Validate Authored Composition")]
        public static void ValidateOpenScene()
        {
            KeyboardWandererDemoController controller =
                UnityEngine.Object.FindAnyObjectByType<KeyboardWandererDemoController>(FindObjectsInactive.Include);
            if (controller == null)
                throw new UnityException("The open scene does not contain a KeyboardWandererDemoController.");

            List<string> errors = CollectErrors(controller);
            if (errors.Count > 0)
                throw new UnityException("Authored composition is incomplete:\n- " + string.Join("\n- ", errors));

            Debug.Log("Keyboard Wanderer authored composition is valid.", controller);
        }

        public static void ValidateSampleScene()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity", OpenSceneMode.Single);
            ValidateOpenScene();
        }

        public static void ValidateOrThrow(KeyboardWandererDemoController controller)
        {
            List<string> errors = CollectErrors(controller);
            if (errors.Count > 0)
                throw new UnityException("Authored composition is incomplete:\n- " + string.Join("\n- ", errors));
        }

        private static List<string> CollectErrors(KeyboardWandererDemoController controller)
        {
            var errors = new List<string>();
            if (controller == null)
            {
                errors.Add("Controller is missing.");
                return errors;
            }

            var serializedController = new SerializedObject(controller);
            KeyboardWandererAuthoringSettings settings = ObjectReference<KeyboardWandererAuthoringSettings>(
                serializedController, "authoringSettings");
            KeyboardWandererWorldView world = ObjectReference<KeyboardWandererWorldView>(
                serializedController, "authoredWorld");
            Camera camera = ObjectReference<Camera>(serializedController, "authoredCamera");
            KeyboardWandererCameraController cameraController = ObjectReference<KeyboardWandererCameraController>(
                serializedController, "authoredCameraController");
            AudioSource music = ObjectReference<AudioSource>(serializedController, "authoredMusicSource");
            AudioSource sfx = ObjectReference<AudioSource>(serializedController, "authoredSfxSource");
            KeyboardWandererAudioController audio = ObjectReference<KeyboardWandererAudioController>(
                serializedController, "authoredAudioController");
            KeyboardWandererInputController input = ObjectReference<KeyboardWandererInputController>(
                serializedController, "authoredInputController");

            if (settings == null) errors.Add("Controller Authoring Settings reference is missing.");
            if (world == null) errors.Add("Controller Authored World reference is missing.");
            if (camera == null) errors.Add("Controller Authored Camera reference is missing.");
            if (cameraController == null || !cameraController.IsReady)
                errors.Add("Controller Authored Camera Controller reference is missing or incomplete.");
            if (music == null) errors.Add("Controller Music AudioSource reference is missing.");
            if (sfx == null) errors.Add("Controller SFX AudioSource reference is missing.");
            if (audio == null || !audio.IsReady)
                errors.Add("Controller Authored Audio Controller reference is missing or incomplete.");
            if (input == null)
                errors.Add("Controller Authored Input Controller reference is missing.");
            if (controller.GetComponent<SceneSequencePlayer>() == null)
                errors.Add("Controller object must contain SceneSequencePlayer.");

            ValidateSettings(settings, errors);
            ValidateWorld(world, errors);
            ValidateUi(controller, errors);
            return errors;
        }

        private static void ValidateSettings(KeyboardWandererAuthoringSettings settings, List<string> errors)
        {
            if (settings == null) return;
            KeyboardWandererEntityView entity = settings.EntityVisualPrefab;
            if (entity == null)
            {
                errors.Add("Authoring Settings Entity Visual Prefab is missing.");
            }
            else
            {
                if (entity.ActorRenderer == null)
                    errors.Add("Entity Visual Prefab Actor Renderer is missing.");
                else if (entity.ActorRenderer.GetComponent<Animator>() == null)
                    errors.Add("Entity Visual Prefab Actor Renderer Animator is missing.");
                if (entity.HealthBack == null) errors.Add("Entity Visual Prefab Health Back is missing.");
                if (entity.HealthFill == null) errors.Add("Entity Visual Prefab Health Fill is missing.");
                if (entity.FinaleLabelText == null) errors.Add("Entity Visual Prefab Finale Label is missing.");
            }

            GameObject landmark = settings.LandmarkPrefab;
            if (landmark == null)
                errors.Add("Authoring Settings Landmark Prefab is missing.");
            else if (landmark.GetComponent<SpriteRenderer>() == null)
                errors.Add("Landmark Prefab root SpriteRenderer is missing.");
        }

        private static void ValidateWorld(KeyboardWandererWorldView world, List<string> errors)
        {
            if (world == null) return;
            var serializedWorld = new SerializedObject(world);
            if (world.TerrainTilemap == null) errors.Add("World Terrain Tilemap is missing.");
            if (world.SelectionCursor == null) errors.Add("World Selection Cursor is missing.");
            RequireTransform(serializedWorld, "staticObjects", "World Static Objects root", errors);
            RequireTransform(serializedWorld, "runtimeEntities", "World Runtime Entities root", errors);
            RequireTransform(serializedWorld, "runtimeLandmarks", "World Runtime Landmarks root", errors);
            RequireTransform(serializedWorld, "runtimeEffects", "World Runtime Effects root", errors);
        }

        private static void ValidateUi(KeyboardWandererDemoController controller, List<string> errors)
        {
            KeyboardWandererSceneUI ui = controller.GetComponentInChildren<KeyboardWandererSceneUI>(true);
            if (ui == null)
            {
                errors.Add("Authored UI component is missing.");
                return;
            }
            if (!ui.IsReady)
                errors.Add("Authored UI typed bindings are incomplete; run Rebuild Authored Scene UI.");

            Transform[] descendants = ui.GetComponentsInChildren<Transform>(true);
            var names = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < descendants.Length; i++) names.Add(descendants[i].name);
            for (int i = 0; i < RequiredUiObjects.Length; i++)
            {
                if (!names.Contains(RequiredUiObjects[i]))
                    errors.Add("Authored UI object is missing: " + RequiredUiObjects[i]);
            }

            Button[] buttons = ui.GetComponentsInChildren<Button>(true);
            if (buttons.Length == 0) errors.Add("Authored UI contains no Button components.");
        }

        private static T ObjectReference<T>(SerializedObject owner, string propertyName) where T : UnityEngine.Object
        {
            SerializedProperty property = owner.FindProperty(propertyName);
            return property != null ? property.objectReferenceValue as T : null;
        }

        private static void RequireTransform(
            SerializedObject owner,
            string propertyName,
            string label,
            List<string> errors)
        {
            if (ObjectReference<Transform>(owner, propertyName) == null)
                errors.Add(label + " is missing.");
        }
    }
}
