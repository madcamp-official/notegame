using UnityEngine;

namespace KeyboardWanderer.Demo
{
    [CreateAssetMenu(fileName = "KeyboardWandererAuthoringSettings", menuName = "Codria/Authoring Settings")]
    public sealed class KeyboardWandererAuthoringSettings : ScriptableObject
    {
        [Header("Content assets")]
        [SerializeField] private NinjaAdventureAssetManifest assetManifest;
        [SerializeField] private KeyboardWandererEntityView entityVisualPrefab;
        [SerializeField] private GameObject landmarkPrefab;

        [Header("World presentation")]
        [SerializeField, Min(0.1f)] private float playerWalkSpeed = 4.2f;
        [SerializeField, Min(0.1f)] private float cameraOrthographicSize = 8.25f;
        [SerializeField] private Color cameraBackground = new Color(0.025f, 0.033f, 0.02f, 1f);
        [SerializeField, Min(0.1f)] private float playerVisualSize = 1.34f;
        [SerializeField, Min(0.1f)] private float enemyVisualSize = 0.92f;
        [SerializeField, Min(0.1f)] private float neutralVisualSize = 0.98f;
        [SerializeField, Min(0.1f)] private float landmarkVisualSize = 0.42f;
        [SerializeField, Min(0.1f)] private float rootLandmarkVisualSize = 0.68f;

        public NinjaAdventureAssetManifest AssetManifest => assetManifest;
        public KeyboardWandererEntityView EntityVisualPrefab => entityVisualPrefab;
        public GameObject LandmarkPrefab => landmarkPrefab;
        public float PlayerWalkSpeed => playerWalkSpeed;
        public float CameraOrthographicSize => cameraOrthographicSize;
        public Color CameraBackground => cameraBackground;
        public float PlayerVisualSize => playerVisualSize;
        public float EnemyVisualSize => enemyVisualSize;
        public float NeutralVisualSize => neutralVisualSize;
        public float LandmarkVisualSize => landmarkVisualSize;
        public float RootLandmarkVisualSize => rootLandmarkVisualSize;

        public void Configure(
            NinjaAdventureAssetManifest manifest,
            KeyboardWandererEntityView entityPrefab,
            GameObject authoredLandmarkPrefab)
        {
            assetManifest = manifest;
            entityVisualPrefab = entityPrefab;
            landmarkPrefab = authoredLandmarkPrefab;
        }
    }
}
