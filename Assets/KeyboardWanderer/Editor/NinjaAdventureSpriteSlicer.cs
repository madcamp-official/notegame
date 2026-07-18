using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using KeyboardWanderer.Demo;
using UnityEditor;
using UnityEditor.U2D.Sprites;
using UnityEngine;

namespace KeyboardWanderer.Editor
{
    public static class NinjaAdventureSpriteSlicer
    {
        private const string AssetRoot = "Assets/NinjaAdventure";
        private const int CellSize = 16;

        [MenuItem("Keyboard Wanderer/Repair All Ninja Adventure Sprite Slices")]
        public static void RepairAllSpriteSlices()
        {
            if (Application.isPlaying)
                throw new UnityException("Exit Play Mode before repairing sprite imports.");

            string[] paths = AssetDatabase.FindAssets("t:Texture2D", new[] { AssetRoot })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(path => string.Equals(Path.GetExtension(path), ".png", StringComparison.OrdinalIgnoreCase))
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();

            var factories = new SpriteDataProviderFactories();
            factories.Init();
            int repaired = 0;
            int skipped = 0;
            int frameCount = 0;

            try
            {
                for (int i = 0; i < paths.Length; i++)
                {
                    string path = paths[i];
                    EditorUtility.DisplayProgressBar(
                        "Repairing all Ninja Adventure sprites",
                        $"{i + 1}/{paths.Length}  {path}",
                        paths.Length > 0 ? (i + 1f) / paths.Length : 1f);

                    Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                    TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
                    if (texture == null || importer == null || texture.width % CellSize != 0 || texture.height % CellSize != 0)
                    {
                        skipped++;
                        continue;
                    }

                    ISpriteEditorDataProvider provider = factories.GetSpriteEditorDataProviderFromObject(importer);
                    if (provider == null)
                    {
                        skipped++;
                        continue;
                    }
                    provider.InitSpriteEditorDataProvider();
                    Dictionary<string, GUID> existingIds = provider.GetSpriteRects()
                        .Where(rect => !string.IsNullOrEmpty(rect.name))
                        .GroupBy(rect => rect.name, StringComparer.Ordinal)
                        .ToDictionary(group => group.Key, group => group.First().spriteID, StringComparer.Ordinal);

                    string baseName = Path.GetFileNameWithoutExtension(path);
                    var rects = new List<SpriteRect>();
                    int index = 0;
                    for (int y = texture.height - CellSize; y >= 0; y -= CellSize)
                    {
                        for (int x = 0; x < texture.width; x += CellSize)
                        {
                            string spriteName = baseName + "_" + index++;
                            rects.Add(new SpriteRect
                            {
                                name = spriteName,
                                rect = new Rect(x, y, CellSize, CellSize),
                                alignment = SpriteAlignment.Center,
                                pivot = new Vector2(0.5f, 0.5f),
                                border = Vector4.zero,
                                spriteID = existingIds.TryGetValue(spriteName, out GUID id) ? id : GUID.Generate()
                            });
                        }
                    }

                    ConfigureImporter(importer);
                    provider.SetSpriteRects(rects.ToArray());
                    provider.Apply();
                    importer.SaveAndReimport();

                    if (GetSpriteRectCount(path, factories) != rects.Count)
                    {
                        importer = AssetImporter.GetAtPath(path) as TextureImporter;
                        importer.spriteImportMode = SpriteImportMode.Single;
                        importer.SaveAndReimport();

                        importer = AssetImporter.GetAtPath(path) as TextureImporter;
                        provider = factories.GetSpriteEditorDataProviderFromObject(importer);
                        provider.InitSpriteEditorDataProvider();
                        ConfigureImporter(importer);
                        provider.SetSpriteRects(rects.ToArray());
                        provider.Apply();
                        importer.SaveAndReimport();

                        int actualCount = GetSpriteRectCount(path, factories);
                        if (actualCount != rects.Count)
                            throw new UnityException($"Failed to slice {path}: expected {rects.Count} frames, found {actualCount}.");
                    }

                    repaired++;
                    frameCount += rects.Count;
                }

                NinjaAdventureManifestBuilder.RebuildManifest();
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                Debug.Log($"Ninja Adventure sprite repair complete: {repaired} textures, {frameCount} frames, {skipped} skipped.");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        private static void ConfigureImporter(TextureImporter importer)
        {
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Multiple;
            importer.spritePixelsPerUnit = CellSize;
            importer.filterMode = FilterMode.Point;
            importer.mipmapEnabled = false;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.wrapMode = TextureWrapMode.Clamp;
        }

        private static int GetSpriteRectCount(string path, SpriteDataProviderFactories factories)
        {
            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            ISpriteEditorDataProvider provider = factories.GetSpriteEditorDataProviderFromObject(importer);
            provider.InitSpriteEditorDataProvider();
            return provider.GetSpriteRects().Length;
        }
    }
}
