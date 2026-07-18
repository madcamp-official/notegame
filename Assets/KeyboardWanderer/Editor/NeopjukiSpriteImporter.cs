using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.U2D.Sprites;
using UnityEngine;

namespace KeyboardWanderer.Editor
{
    public static class NeopjukiSpriteImporter
    {
        private const string ImporterVersion = "NeopjukiSpriteImporter:v4";
        private const string AtlasPath =
            "Assets/KeyboardWanderer/Art/Pets/Neopjuki/NeopjukiUnityAtlas.png";
        private const int CellWidth = 192;
        private const int CellHeight = 208;
        private const int AtlasHeight = 3328;

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
            ("review", 7, 0, 6),
            ("keyboard-attack", 8, 0, 8),
            ("keyboard-magic", 9, 0, 8),
            ("keyboard-debug", 10, 0, 8),
            ("keyboard-attack-left", 11, 0, 8),
            ("keyboard-attack-up", 12, 0, 8),
            ("keyboard-attack-down", 13, 0, 8),
            ("walking-up", 14, 0, 8),
            ("walking-down", 15, 0, 8)
        };

        [InitializeOnLoadMethod]
        private static void QueueAutomaticImport()
        {
            EditorApplication.delayCall += ImportIfNeeded;
        }

        private static void ImportIfNeeded()
        {
            var importer = AssetImporter.GetAtPath(AtlasPath) as TextureImporter;
            if (importer != null && importer.userData != ImporterVersion)
                Import();
        }

        [MenuItem("Keyboard Wanderer/Import Neopjuki Pet Atlas")]
        public static void Import()
        {
            var importer = AssetImporter.GetAtPath(AtlasPath) as TextureImporter;
            if (importer == null)
                throw new MissingReferenceException("Neopjuki atlas is missing at " + AtlasPath);

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Multiple;
            importer.spritePixelsPerUnit = CellWidth;
            importer.maxTextureSize = 4096;
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

            provider.SetSpriteRects(rects.ToArray());
            ISpriteNameFileIdDataProvider nameProvider =
                provider.GetDataProvider<ISpriteNameFileIdDataProvider>();
            nameProvider.SetNameFileIdPairs(
                rects.Select(rect => new SpriteNameFileIdPair(rect.name, rect.spriteID))
                    .ToList());
            provider.Apply();
            importer.userData = ImporterVersion;
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
