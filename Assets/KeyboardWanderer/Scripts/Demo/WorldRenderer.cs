using System;
using System.Collections.Generic;
using KeyboardWanderer.Core;
using KeyboardWanderer.Gameplay;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace KeyboardWanderer.Demo
{
    /// <summary>Layout-hash-aware immutable terrain renderer shared by runtime tooling and Edit Mode preview.</summary>
    public sealed class WorldRenderer : IDisposable
    {
        private readonly List<Tile> _tiles = new List<Tile>();
        public string RenderedLayoutHash { get; private set; } = string.Empty;

        public bool Render(Tilemap tilemap, RunView view, KeyboardWandererWorldVisualProfile profile, Sprite baseSprite)
        {
            if (tilemap == null) throw new ArgumentNullException(nameof(tilemap));
            if (view == null) throw new ArgumentNullException(nameof(view));
            if (string.Equals(RenderedLayoutHash, view.Region.LayoutHash, StringComparison.Ordinal)) return false;

            Clear(tilemap);
            var palette = new Dictionary<string, Tile>(StringComparer.Ordinal);
            for (int y = 0; y < view.Region.Height; y++)
            {
                for (int x = 0; x < view.Region.Width; x++)
                {
                    var coord = new GridCoord(x, y);
                    BaseTile source = view.Region.GetTile(coord);
                    string biome = BiomeAt(view.Region, coord);
                    string key = biome + ":" + source.Kind;
                    if (!palette.TryGetValue(key, out Tile tile))
                    {
                        tile = ScriptableObject.CreateInstance<Tile>();
                        tile.name = "Preview " + key;
                        tile.sprite = baseSprite;
                        tile.color = profile != null ? profile.PreviewColor(biome, source.Kind) : FallbackColor(source.Kind);
                        tile.hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild;
                        palette.Add(key, tile);
                        _tiles.Add(tile);
                    }
                    tilemap.SetTile(new Vector3Int(x, y, 0), tile);
                }
            }
            RenderedLayoutHash = view.Region.LayoutHash;
            return true;
        }

        public void Clear(Tilemap tilemap)
        {
            if (tilemap != null) tilemap.ClearAllTiles();
            for (int i = 0; i < _tiles.Count; i++)
                if (_tiles[i] != null)
                    UnityEngine.Object.DestroyImmediate(_tiles[i]);
            _tiles.Clear();
            RenderedLayoutHash = string.Empty;
        }

        public void Dispose() => Clear(null);

        private static string BiomeAt(RegionMap region, GridCoord coord)
        {
            for (int i = 0; i < region.Areas.Count; i++)
                if (region.Areas[i].Contains(coord))
                    return region.Areas[i].Biome;
            return "temperate_forest_field";
        }

        private static Color FallbackColor(TileKind kind)
        {
            switch (kind)
            {
                case TileKind.Water: return new Color(0.12f, 0.34f, 0.58f);
                case TileKind.Wall: return new Color(0.22f, 0.21f, 0.24f);
                case TileKind.Hazard: return new Color(0.72f, 0.23f, 0.18f);
                case TileKind.Dirt: return new Color(0.58f, 0.42f, 0.24f);
                default: return new Color(0.36f, 0.58f, 0.3f);
            }
        }
    }
}
