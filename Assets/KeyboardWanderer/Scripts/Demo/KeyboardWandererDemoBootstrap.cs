using UnityEngine;

namespace KeyboardWanderer.Demo
{
    public static class KeyboardWandererDemoBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void CreateDemo()
        {
            // The normal setup is authored directly in the scene so it is visible
            // in the Hierarchy. Keep this as a fallback for test or recovery scenes.
            if (Object.FindAnyObjectByType<KeyboardWandererDemoController>() != null)
                return;

            var gameObject = new GameObject("Codria Game");
            gameObject.AddComponent<KeyboardWandererDemoController>();
        }
    }
}
