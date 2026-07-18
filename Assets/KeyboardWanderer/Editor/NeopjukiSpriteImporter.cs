using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.U2D.Sprites;
using UnityEngine;

namespace KeyboardWanderer.Editor
{
    public static class NeopjukiSpriteImporter
    {
        private const string AtlasPath =
            "Assets/KeyboardWanderer/Art/Pets/Neopjuki/NeopjukiCodexAtlas.png";
        private const int CellWidth = 192;
        private const int CellHeight = 208;
        private const int AtlasHeight = 2288;

        private static readonly (string Name, int Row, int StartColumn, int Count)[] StandardRows =
        {
            ("idle", 0, 0, 6),
            ("neutral", 0, 6, 1),
            ("running-right", 1, 0, 8),
            ("running-left", 2, 0, 8),
            ("waving", 3, 0, 4),
            ("jumping", 4, 0, 5),
            ("failed", 5, 0, 8),
            ("waiting", 6, 0, 6),
            ("running", 7, 0, 6),
            ("review", 8, 0, 6)
        };

        private static readonly string[] LookDirections =
        {
            "000", "022.5", "045", "067.5", "090", "112.5", "135", "157.5",
            "180", "202.5", "225", "247.5", "270", "292.5", "315", "337.5"
        };

        [MenuItem("Keyboard Wanderer/Import Neopjuki Pet Atlas")]
        public static void Import()
        {
            var importer = AssetImporter.GetAtPath(AtlasPath) as TextureImporter;
            if (importer == null)
                throw new MissingReferenceException("Neopjuki atlas is missing at " + AtlasPath);

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Multiple;
            importer.spritePixelsPerUnit = CellWidth;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.filterMode = FilterMode.Point;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();

            var factories = new SpriteDataProviderFactories();
            factories.Init();
            ISpriteEditorDataProvider provider =
                factories.GetSpriteEditorDataProviderFromObject(importer);
            provider.InitSpriteEditorDataProvider();

            Dictionary<string, GUID> existingIds = provider.GetSpriteRects()
                .ToDictionary(rect => rect.name, rect => rect.spriteID);
            var rects = new List<SpriteRect>();

            foreach ((string state, int row, int startColumn, int count) in StandardRows)
            {
                for (int frame = 0; frame < count; frame++)
                {
                    int column = startColumn + frame;
                    string spriteName = state + "_" + frame.ToString("00");
                    rects.Add(CreateRect(spriteName, row, column, existingIds));
                }
            }

            for (int index = 0; index < LookDirections.Length; index++)
            {
                int row = 9 + index / 8;
                int column = index % 8;
                string spriteName = "look_" + LookDirections[index].Replace(".", "_");
                rects.Add(CreateRect(spriteName, row, column, existingIds));
            }

            provider.SetSpriteRects(rects.ToArray());
            ISpriteNameFileIdDataProvider nameProvider =
                provider.GetDataProvider<ISpriteNameFileIdDataProvider>();
            nameProvider.SetNameFileIdPairs(
                rects.Select(rect => new SpriteNameFileIdPair(rect.name, rect.spriteID))
                    .ToList());
            provider.Apply();
            importer.SaveAndReimport();

            Selection.activeObject = AssetDatabase.LoadAssetAtPath<Texture2D>(AtlasPath);
            Debug.Log("Imported " + rects.Count + " Neopjuki animation sprites.");
        }

        private static SpriteRect CreateRect(
            string spriteName,
            int row,
            int column,
            IReadOnlyDictionary<string, GUID> existingIds)
        {
            return new SpriteRect
            {
                name = spriteName,
                spriteID = existingIds.TryGetValue(spriteName, out GUID id)
                    ? id
                    : GUID.Generate(),
                rect = new Rect(
                    column * CellWidth,
                    AtlasHeight - (row + 1) * CellHeight,
                    CellWidth,
                    CellHeight),
                alignment = SpriteAlignment.Custom,
                pivot = new Vector2(0.5f, 0f)
            };
        }
    }
}
