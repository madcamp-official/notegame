using UnityEngine;
using UnityEngine.SceneManagement;

namespace KeyboardWanderer.Demo
{
    public static class KeyboardWandererDemoBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void ValidateAuthoredScene()
        {
            // Test Runner and utility scenes intentionally do not contain the game composition.
            // Only the shipped entry scene is required to satisfy this authoring contract.
            if (SceneManager.GetActiveScene().path != "Assets/Scenes/SampleScene.unity")
                return;
            if (Object.FindAnyObjectByType<KeyboardWandererDemoController>() != null)
                return;
            Debug.LogError(
                "Keyboard Wanderer requires an authored scene containing KeyboardWandererDemoController. " +
                "Run 'Keyboard Wanderer/Convert Runtime Composition to Authored Assets' in the intended scene.");
        }
    }
}
