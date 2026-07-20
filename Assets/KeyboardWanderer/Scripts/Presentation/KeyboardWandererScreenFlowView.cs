using UnityEngine;

namespace KeyboardWanderer.Demo
{
    /// <summary>
    /// Authored UI 루트가 다섯 화면의 활성 상태만 관리한다.
    /// 개별 화면의 텍스트와 버튼은 각 화면 View가 소유한다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class KeyboardWandererScreenFlowView : MonoBehaviour
    {
        [SerializeField] private GameObject titleScreen;
        [SerializeField] private GameObject gameHud;
        [SerializeField] private GameObject settingsScreen;
        [SerializeField] private GameObject pauseScreen;
        [SerializeField] private GameObject endingScreen;

        public bool IsReady => titleScreen != null && gameHud != null && settingsScreen != null &&
                               pauseScreen != null && endingScreen != null;

        public void Present(bool title, bool settings, bool playing, bool paused, bool ended)
        {
            SetActive(titleScreen, title);
            SetActive(gameHud, playing);
            SetActive(settingsScreen, settings);
            SetActive(pauseScreen, playing && paused);
            SetActive(endingScreen, playing && ended);
        }

#if UNITY_EDITOR
        public void Configure(GameObject title, GameObject hud, GameObject settings,
            GameObject pause, GameObject ending)
        {
            titleScreen = title;
            gameHud = hud;
            settingsScreen = settings;
            pauseScreen = pause;
            endingScreen = ending;
            UnityEditor.EditorUtility.SetDirty(this);
        }
#endif

        private static void SetActive(GameObject target, bool active)
        {
            if (target != null && target.activeSelf != active)
                target.SetActive(active);
        }
    }
}
