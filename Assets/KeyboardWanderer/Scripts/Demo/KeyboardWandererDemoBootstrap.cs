using UnityEngine;

namespace KeyboardWanderer.Demo
{
    public static class KeyboardWandererDemoBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void ValidateAuthoredScene()
        {
            if (Object.FindAnyObjectByType<KeyboardWandererDemoController>() != null)
                return;
            Debug.LogError(
                "Keyboard Wanderer requires an authored scene containing KeyboardWandererDemoController. " +
                "Run 'Keyboard Wanderer/Convert Runtime Composition to Authored Assets' in the intended scene.");
        }
    }
}
