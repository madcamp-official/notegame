using System;
using KeyboardWanderer.Core;
using UnityEngine;

namespace KeyboardWanderer.Demo
{
    [CreateAssetMenu(fileName = "KeyboardWandererWorldVisualProfile", menuName = "Codria/World Visual Profile")]
    public sealed class KeyboardWandererWorldVisualProfile : ScriptableObject
    {
        [Serializable]
        public sealed class BiomeVisual
        {
            [SerializeField] private string biomeId = "temperate_forest_field";
            [SerializeField] private Color palette = Color.white;
            [SerializeField] private Color decorationTint = Color.white;
            [SerializeField, Range(0, 100)] private int decorationDensity = 44;
            [SerializeField] private Sprite groundSprite;
            [SerializeField] private Sprite[] decorationSprites = Array.Empty<Sprite>();
            [SerializeField] private Sprite landmarkSprite;
            [SerializeField] private GameObject decorationPrefab;

            public string BiomeId => biomeId;
            public Color Palette => palette;
            public Color DecorationTint => decorationTint;
            public int DecorationDensity => decorationDensity;
            public Sprite GroundSprite => groundSprite;
            public Sprite LandmarkSprite => landmarkSprite;
            public GameObject DecorationPrefab => decorationPrefab;

            public Sprite DecorationSprite(int stableHash)
            {
                if (decorationSprites == null || decorationSprites.Length == 0) return null;
                return decorationSprites[Math.Abs(stableHash) % decorationSprites.Length];
            }

            public void Configure(string id, Color biomePalette, Color tint, int density)
            {
                biomeId = id;
                palette = biomePalette;
                decorationTint = tint;
                decorationDensity = Mathf.Clamp(density, 0, 100);
            }
        }

        [SerializeField] private BiomeVisual[] biomes = Array.Empty<BiomeVisual>();
        public BiomeVisual[] Biomes => biomes;

        public bool TryGet(string biomeId, out BiomeVisual visual)
        {
            for (int i = 0; i < biomes.Length; i++)
            {
                visual = biomes[i];
                if (visual != null && string.Equals(visual.BiomeId, biomeId, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            visual = null;
            return false;
        }

        public Color PreviewColor(string biomeId, TileKind kind)
        {
            Color baseColor = kind == TileKind.Water ? new Color(0.12f, 0.34f, 0.58f) :
                kind == TileKind.Wall ? new Color(0.24f, 0.23f, 0.25f) :
                kind == TileKind.Hazard ? new Color(0.72f, 0.23f, 0.18f) : Color.white;
            if (!TryGet(biomeId, out BiomeVisual visual)) return baseColor;
            return Color.Lerp(baseColor, visual.Palette, 0.62f);
        }

        public void ConfigureDefaults()
        {
            biomes = new[]
            {
                Entry("temperate_forest_field", "6ca85d", "ffffff", 64),
                Entry("river_wetland", "5fa9a8", "b6e1cf", 48),
                Entry("arid_desert", "d6a253", "f0c879", 44),
                Entry("frost_highland", "a9c8df", "e8f6ff", 50),
                Entry("subterranean_cavern", "745a91", "cf9fea", 56),
                Entry("ancient_ruins", "a68a61", "c6ae82", 48),
                Entry("root_system", "2a1830", "b667d8", 52)
            };
        }

        private static BiomeVisual Entry(string id, string palette, string tint, int density)
        {
            var entry = new BiomeVisual();
            entry.Configure(id, Parse(palette), Parse(tint), density);
            return entry;
        }

        private static Color Parse(string hex)
        {
            ColorUtility.TryParseHtmlString("#" + hex, out Color color);
            return color;
        }
    }
}
