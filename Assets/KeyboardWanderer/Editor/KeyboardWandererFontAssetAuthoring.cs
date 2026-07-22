using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

namespace KeyboardWanderer.Editor
{
    /// <summary>
    /// 프로젝트에서 사용하는 한글 TMP 폰트 에셋을 만들고 검증한다.
    /// 런타임에 처음 등장한 한글이 여러 Atlas로 분리되는 순간 발생하던
    /// TMP fallback material 오류를 막기 위해 실제 게임 문자를 미리 채운다.
    /// </summary>
    public static class KeyboardWandererFontAssetAuthoring
    {
        public const string SourceFontPath =
            "Assets/KeyboardWanderer/Resources/Fonts/NeoDunggeunmoPro-Regular.ttf";
        public const string FontAssetPath =
            "Assets/KeyboardWanderer/Resources/Fonts/NeoDunggeunmoPro-Regular SDF.asset";
        public const int AtlasSize = 1024;

        private const int SamplingPointSize = 36;
        private const int AtlasPadding = 4;
        private const string RuntimeSourceRoot = "Assets/KeyboardWanderer/Scripts";
        private const string BasicCharacters =
            " !\"#$%&'()*+,-./0123456789:;<=>?@ABCDEFGHIJKLMNOPQRSTUVWXYZ[\\]^_`abcdefghijklmnopqrstuvwxyz{|}~" +
            "…·–—“”‘’";

        [MenuItem("Keyboard Wanderer/UI/한글 TMP 폰트 검증 및 다시 만들기")]
        public static void RebuildFromMenu()
        {
            TMP_FontAsset font = EnsureProjectFontAsset(true);
            Debug.Log(string.Format(
                "한글 TMP 폰트를 준비했습니다. 문자 {0}개, Atlas {1}개 ({2}x{2})",
                font.characterTable.Count,
                font.atlasTextureCount,
                AtlasSize),
                font);
        }

        /// <summary>
        /// 기본 TMP 폰트 에셋이 존재하는지 확인하고 현재 게임 문자열을 미리 넣는다.
        /// </summary>
        /// <param name="forceRebuild">
        /// true이면 기존 글리프를 지우고 같은 에셋/GUID 안에서 Atlas를 다시 만든다.
        /// </param>
        public static TMP_FontAsset EnsureProjectFontAsset(bool forceRebuild = false)
        {
            Font sourceFont = AssetDatabase.LoadAssetAtPath<Font>(SourceFontPath);
            if (sourceFont == null)
                throw new InvalidOperationException("기본 UI 폰트가 없습니다: " + SourceFontPath);

            TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontAssetPath);
            bool created = font == null;
            if (font == null)
                font = CreateFontAsset(sourceFont);

            string requiredCharacters = CollectRuntimeCharacters();
            // Never grow the persistent project font incrementally. TMP may attach a
            // second atlas sub-asset during TryAddCharacters, changing NativeFormatImporter
            // topology while the Editor still holds the previous artifact. Build the
            // complete sorted corpus in a transient font and copy it atomically instead.
            bool requiresRebuild = forceRebuild || RequiresRebuild(font) ||
                                   !HasAllCharacters(font, requiredCharacters);
            if (requiresRebuild)
                RebuildFontData(font, sourceFont, requiredCharacters);

            bool changed = created || requiresRebuild || ConfigureSerializedSettings(font);
            if (font.name != "NeoDunggeunmoPro-Regular SDF")
            {
                font.name = "NeoDunggeunmoPro-Regular SDF";
                changed = true;
            }

            // Validation during ordinary editor/build startup must be a true no-op. Repeated
            // dirty/save/import cycles create unnecessary NativeFormatImporter revisions and
            // can make a previous live TMP topology look inconsistent even when bytes match.
            if (changed)
            {
                EditorUtility.SetDirty(font);
                AssetDatabase.SaveAssets();
            }

            font.ReadFontAssetDefinition();
            return font;
        }

        /// <summary>
        /// TMP의 문자열 단위 검사 결과에 의존하지 않고 필요한 각 글리프를 직접 확인한다.
        /// </summary>
        public static bool HasAllRuntimeCharacters(TMP_FontAsset font)
        {
            return font != null && HasAllCharacters(font, CollectRuntimeCharacters());
        }

        /// <summary>
        /// UI와 게임 로직의 문자열 리터럴에서 실제 표시 가능한 문자만 수집한다.
        /// 한글 주석은 폰트에 불필요하므로 문자열 리터럴 밖의 문자는 포함하지 않는다.
        /// </summary>
        public static string CollectRuntimeCharacters()
        {
            var characters = new SortedSet<char>(BasicCharacters);
            string[] scriptGuids = AssetDatabase.FindAssets("t:MonoScript", new[] { RuntimeSourceRoot });
            var literalPattern = new Regex("\"(?:\\\\.|[^\"\\\\])*\"", RegexOptions.Compiled);

            for (int i = 0; i < scriptGuids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(scriptGuids[i]);
                MonoScript script = AssetDatabase.LoadAssetAtPath<MonoScript>(path);
                if (script == null || string.IsNullOrEmpty(script.text))
                    continue;

                MatchCollection literals = literalPattern.Matches(script.text);
                for (int literalIndex = 0; literalIndex < literals.Count; literalIndex++)
                    AddDisplayCharacters(literals[literalIndex].Value, characters);
            }

            var builder = new StringBuilder(characters.Count);
            foreach (char character in characters)
                builder.Append(character);
            return builder.ToString();
        }

        private static TMP_FontAsset CreateFontAsset(Font sourceFont)
        {
            TMP_FontAsset font = TMP_FontAsset.CreateFontAsset(
                sourceFont,
                SamplingPointSize,
                AtlasPadding,
                GlyphRenderMode.SDFAA,
                AtlasSize,
                AtlasSize,
                AtlasPopulationMode.Dynamic,
                true);
            Texture2D atlas = font.atlasTexture;
            Material material = font.material;

            font.name = "NeoDunggeunmoPro-Regular SDF";
            atlas.name = "NeoDunggeunmoPro-Regular Atlas";
            material.name = "NeoDunggeunmoPro-Regular Material";
            AssetDatabase.CreateAsset(font, FontAssetPath);
            AssetDatabase.AddObjectToAsset(atlas, font);
            AssetDatabase.AddObjectToAsset(material, font);
            return font;
        }

        private static bool RequiresRebuild(TMP_FontAsset font)
        {
            if (font.atlasWidth != AtlasSize || font.atlasHeight != AtlasSize ||
                font.atlasPadding != AtlasPadding || font.atlasTextureCount != 1)
                return true;

            return !Mathf.Approximately(font.faceInfo.pointSize, SamplingPointSize);
        }

        private static void RebuildFontData(TMP_FontAsset font, Font sourceFont, string requiredCharacters)
        {
            Texture2D persistentAtlas = font.atlasTextures != null && font.atlasTextures.Length > 0
                ? font.atlasTextures[0]
                : null;
            Material persistentMaterial = font.material;
            if (persistentAtlas == null || persistentMaterial == null)
                throw new InvalidOperationException("기존 TMP 폰트의 Atlas 또는 Material sub-asset이 없습니다.");

            TMP_FontAsset generated = TMP_FontAsset.CreateFontAsset(
                sourceFont,
                SamplingPointSize,
                AtlasPadding,
                GlyphRenderMode.SDFAA,
                AtlasSize,
                AtlasSize,
                AtlasPopulationMode.Dynamic,
                true);
            try
            {
                bool added = generated.TryAddCharacters(requiredCharacters, out string missingCharacters, false);
                if (!added)
                {
                    string detail = string.IsNullOrEmpty(missingCharacters)
                        ? "알 수 없는 문자 또는 Atlas 용량 부족"
                        : missingCharacters;
                    throw new InvalidOperationException(
                        "기본 폰트가 모든 런타임 문자를 렌더링할 수 없습니다: " + detail);
                }
                if (generated.atlasTextureCount != 1)
                    throw new InvalidOperationException("기본 게임 문자가 하나의 TMP Atlas에 들어가지 않습니다.");

                Texture2D generatedAtlas = generated.atlasTextures[0];
                Material generatedMaterial = generated.material;

                // 추가 Atlas는 제거하되 첫 Atlas와 Material 오브젝트는 남겨 프리팹 fileID를 보존한다.
                font.ClearFontAssetData(true);
                EditorUtility.CopySerialized(generated, font);
                EditorUtility.CopySerialized(generatedAtlas, persistentAtlas);
                EditorUtility.CopySerialized(generatedMaterial, persistentMaterial);

                var serializedFont = new SerializedObject(font);
                serializedFont.FindProperty("m_Material").objectReferenceValue = persistentMaterial;
                SerializedProperty atlases = serializedFont.FindProperty("m_AtlasTextures");
                atlases.arraySize = 1;
                atlases.GetArrayElementAtIndex(0).objectReferenceValue = persistentAtlas;
                serializedFont.ApplyModifiedPropertiesWithoutUndo();

                persistentAtlas.name = "NeoDunggeunmoPro-Regular Atlas";
                persistentMaterial.name = "NeoDunggeunmoPro-Regular Material";
                persistentMaterial.mainTexture = persistentAtlas;
                EditorUtility.SetDirty(persistentAtlas);
                EditorUtility.SetDirty(persistentMaterial);
            }
            finally
            {
                Material generatedMaterial = generated != null ? generated.material : null;
                Texture2D[] generatedAtlases = generated != null ? generated.atlasTextures : null;
                if (generatedMaterial != null)
                    UnityEngine.Object.DestroyImmediate(generatedMaterial);
                if (generatedAtlases != null)
                {
                    for (int i = 0; i < generatedAtlases.Length; i++)
                    {
                        if (generatedAtlases[i] != null)
                            UnityEngine.Object.DestroyImmediate(generatedAtlases[i]);
                    }
                }
                if (generated != null)
                    UnityEngine.Object.DestroyImmediate(generated);
            }
        }

        private static bool ConfigureSerializedSettings(TMP_FontAsset font)
        {
            var serializedFont = new SerializedObject(font);
            bool changed = SetInt(serializedFont, "m_AtlasWidth", AtlasSize);
            changed |= SetInt(serializedFont, "m_AtlasHeight", AtlasSize);
            changed |= SetInt(serializedFont, "m_AtlasPadding", AtlasPadding);
            // The project asset is a prewarmed, immutable template. Arbitrary player / LLM
            // text is added only to KeyboardWandererRuntimeFontProvider's deep clone. Keeping
            // the persistent asset Static prevents any missed binding from changing sub-asset
            // topology and confusing NativeFormatImporter.
            changed |= SetInt(serializedFont, "m_AtlasPopulationMode", (int)AtlasPopulationMode.Static);
            changed |= SetBool(serializedFont, "m_IsMultiAtlasTexturesEnabled", true);
            // 미리 채운 한글을 빌드에서 지우면 같은 첫 프레임 오류가 다시 발생한다.
            changed |= SetBool(serializedFont, "m_ClearDynamicDataOnBuild", false);
            if (changed)
                serializedFont.ApplyModifiedPropertiesWithoutUndo();
            return changed;
        }

        private static bool SetInt(SerializedObject target, string propertyName, int value)
        {
            SerializedProperty property = target.FindProperty(propertyName);
            if (property == null || property.intValue == value)
                return false;
            property.intValue = value;
            return true;
        }

        private static bool SetBool(SerializedObject target, string propertyName, bool value)
        {
            SerializedProperty property = target.FindProperty(propertyName);
            if (property == null || property.boolValue == value)
                return false;
            property.boolValue = value;
            return true;
        }

        private static void AddDisplayCharacters(string literal, ISet<char> destination)
        {
            for (int i = 0; i < literal.Length; i++)
            {
                char character = literal[i];
                UnicodeCategory category = char.GetUnicodeCategory(character);
                bool korean = character >= '\u1100' && character <= '\u11ff' ||
                              character >= '\u3130' && character <= '\u318f' ||
                              character >= '\uac00' && character <= '\ud7a3';
                bool displaySymbol = category == UnicodeCategory.OtherPunctuation ||
                                     category == UnicodeCategory.DashPunctuation ||
                                     category == UnicodeCategory.MathSymbol ||
                                     category == UnicodeCategory.OtherSymbol;
                if (korean || displaySymbol)
                    destination.Add(character);
            }
        }

        private static bool HasAllCharacters(TMP_FontAsset font, string characters)
        {
            // CopySerialized로 글리프 테이블을 갱신한 직후에는 TMP의 런타임 lookup table이
            // 아직 이전 데이터를 가리킬 수 있다. 직렬화된 테이블로 명시적으로 재구축한다.
            font.ReadFontAssetDefinition();
            for (int i = 0; i < characters.Length; i++)
            {
                if (!font.HasCharacter(characters[i]))
                    return false;
            }
            return true;
        }
    }
}
