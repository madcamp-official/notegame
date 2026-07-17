using UnityEngine;
using UnityEngine.SceneManagement;

namespace KeyboardWanderer.Demo
{
    public static class KeyboardWandererDemoBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void CreateDemo()
        {
            if (SceneManager.GetActiveScene().name != "SampleScene" || Object.FindAnyObjectByType<KeyboardWandererDemoController>() != null)
                return;

            var gameObject = new GameObject("Keyboard Wanderer Demo");
            gameObject.AddComponent<KeyboardWandererDemoController>();
        }
    }
}
