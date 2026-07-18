using UnityEngine;
using UnityEngine.Tilemaps;

namespace KeyboardWanderer.Demo
{
    public sealed class KeyboardWandererWorldView : MonoBehaviour
    {
        [SerializeField] private Tilemap terrainTilemap;
        [SerializeField] private Transform dynamicObjects;
        [SerializeField] private SpriteRenderer selectionCursor;

        public Tilemap TerrainTilemap => terrainTilemap;
        public Transform DynamicObjects => dynamicObjects != null ? dynamicObjects : transform;
        public SpriteRenderer SelectionCursor => selectionCursor;

        public void Configure(Tilemap terrain, Transform dynamicRoot, SpriteRenderer selection)
        {
            terrainTilemap = terrain;
            dynamicObjects = dynamicRoot;
            selectionCursor = selection;
        }

        public void ResetRuntimeContent()
        {
            gameObject.SetActive(true);
            if (terrainTilemap != null)
                terrainTilemap.ClearAllTiles();
            if (selectionCursor != null)
                selectionCursor.enabled = false;

            Transform root = DynamicObjects;
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
