using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace KeyboardWanderer.Demo
{
    public sealed class KeyboardWandererWorldView : MonoBehaviour
    {
        [SerializeField] private Tilemap terrainTilemap;
        [SerializeField, Tooltip("Scene-authored decorations and landmarks. Runtime reset never removes this hierarchy.")]
        private Transform staticObjects;
        [SerializeField] private Transform runtimeEntities;
        [SerializeField] private Transform runtimeLandmarks;
        [SerializeField] private Transform runtimeEffects;
        [SerializeField] private SpriteRenderer selectionCursor;

        public Tilemap TerrainTilemap => terrainTilemap;
        public Transform StaticObjects => staticObjects;
        public Transform RuntimeEntities => runtimeEntities;
        public Transform RuntimeLandmarks => runtimeLandmarks;
        public Transform RuntimeEffects => runtimeEffects;
        public SpriteRenderer SelectionCursor => selectionCursor;

        public void Configure(
            Tilemap terrain,
            Transform authoredStaticRoot,
            Transform entityRoot,
            Transform landmarkRoot,
            Transform effectsRoot,
            SpriteRenderer selection)
        {
            terrainTilemap = terrain;
            staticObjects = authoredStaticRoot;
            runtimeEntities = entityRoot;
            runtimeLandmarks = landmarkRoot;
            runtimeEffects = effectsRoot;
            selectionCursor = selection;
        }

        public void ResetRuntimeContent()
        {
            gameObject.SetActive(true);
            if (terrainTilemap != null)
                terrainTilemap.ClearAllTiles();
            if (selectionCursor != null)
                selectionCursor.enabled = false;

            var cleared = new HashSet<Transform>();
            ClearRuntimeRoot(RuntimeEntities, cleared);
            ClearRuntimeRoot(RuntimeLandmarks, cleared);
            ClearRuntimeRoot(RuntimeEffects, cleared);
        }

        private void ClearRuntimeRoot(Transform root, HashSet<Transform> cleared)
        {
            if (root == null || root == transform || !cleared.Add(root))
                return;
            for (int i = root.childCount - 1; i >= 0; i--)
            {
                GameObject child = root.GetChild(i).gameObject;
                if (Application.isPlaying)
                    Destroy(child);
                else
                    DestroyImmediate(child);
            }
        }
    }
}
