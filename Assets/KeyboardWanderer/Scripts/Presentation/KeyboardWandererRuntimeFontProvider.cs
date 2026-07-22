using TMPro;
using UnityEngine;

namespace KeyboardWanderer.Demo
{
    /// <summary>
    /// Supplies a non-persistent copy of the prewarmed project TMP font while the game
    /// is running. Arbitrary player and LLM text may grow this font to multiple atlases
    /// without changing a Resources asset or involving Unity's NativeFormatImporter.
    /// </summary>
    public static class KeyboardWandererRuntimeFontProvider
    {
        private const string FontResourcePath = "Fonts/NeoDunggeunmoPro-Regular SDF";
        private static TMP_FontAsset _source;
        private static TMP_FontAsset _runtime;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetRuntimeState()
        {
            TMP_FontAsset runtime = _runtime;
            TMP_FontAsset source = _source;
            _runtime = null;
            _source = null;
            if (runtime == null)
                return;

            // TMP_FontAsset destruction does not own or release the instantiated
            // material and atlas textures referenced by the clone. Explicitly release
            // them so repeated Play Mode sessions or a runtime source-font swap cannot
            // retain native textures until the Editor/process exits.
            Material runtimeMaterial = runtime.material;
            Texture2D[] runtimeAtlases = runtime.atlasTextures;
            DestroyRuntimeObject(runtime);
            if (runtimeMaterial != null && (source == null || runtimeMaterial != source.material))
                DestroyRuntimeObject(runtimeMaterial);
            if (runtimeAtlases == null)
                return;
            for (int i = 0; i < runtimeAtlases.Length; i++)
            {
                Texture2D atlas = runtimeAtlases[i];
                if (atlas == null || SourceOwnsAtlas(source, atlas) || AlreadySeen(runtimeAtlases, atlas, i))
                    continue;
                DestroyRuntimeObject(atlas);
            }
        }

        private static bool SourceOwnsAtlas(TMP_FontAsset source, Texture2D candidate)
        {
            Texture2D[] sourceAtlases = source != null ? source.atlasTextures : null;
            if (sourceAtlases == null)
                return false;
            for (int i = 0; i < sourceAtlases.Length; i++)
                if (sourceAtlases[i] == candidate)
                    return true;
            return false;
        }

        private static bool AlreadySeen(Texture2D[] values, Texture2D candidate, int beforeIndex)
        {
            for (int i = 0; i < beforeIndex; i++)
                if (values[i] == candidate)
                    return true;
            return false;
        }

        private static void DestroyRuntimeObject(Object value)
        {
            if (value == null)
                return;
            if (Application.isPlaying)
                Object.Destroy(value);
            else
                Object.DestroyImmediate(value);
        }

        public static TMP_FontAsset Get(TMP_FontAsset preferred = null)
        {
            TMP_FontAsset source = preferred != null ? preferred : Resources.Load<TMP_FontAsset>(FontResourcePath);
            if (source == null || !Application.isPlaying)
                return source;
            if (source == _runtime)
                return _runtime;
            if (_runtime != null && _source == source)
                return _runtime;

            ResetRuntimeState();
            _source = source;
            _runtime = CreateDeepRuntimeClone(source);
            return _runtime != null ? _runtime : source;
        }

        public static void ApplyTo(Transform root, TMP_FontAsset preferred = null)
        {
            if (root == null)
                return;
            TMP_FontAsset source = preferred;
            TMP_Text[] texts = root.GetComponentsInChildren<TMP_Text>(true);
            if (source == null)
            {
                for (int i = 0; i < texts.Length && source == null; i++)
                    source = texts[i]?.font;
            }
            TMP_FontAsset runtime = Get(source);
            if (runtime == null || runtime == source)
                return;
            for (int i = 0; i < texts.Length; i++)
                if (texts[i] != null && texts[i].font == source)
                    texts[i].font = runtime;
        }

        private static TMP_FontAsset CreateDeepRuntimeClone(TMP_FontAsset source)
        {
            if (source.atlasTextures == null || source.atlasTextures.Length == 0 || source.material == null)
                return null;
            TMP_FontAsset runtime = Object.Instantiate(source);
            runtime.name = source.name + " (Runtime)";
            runtime.hideFlags = HideFlags.HideAndDontSave;

            Texture2D[] sourceAtlases = source.atlasTextures;
            var runtimeAtlases = new Texture2D[sourceAtlases.Length];
            for (int i = 0; i < sourceAtlases.Length; i++)
            {
                if (sourceAtlases[i] == null) continue;
                runtimeAtlases[i] = Object.Instantiate(sourceAtlases[i]);
                runtimeAtlases[i].name = sourceAtlases[i].name + " (Runtime)";
                runtimeAtlases[i].hideFlags = HideFlags.HideAndDontSave;
            }
            runtime.atlasTextures = runtimeAtlases;

            Material runtimeMaterial = Object.Instantiate(source.material);
            runtimeMaterial.name = source.material.name + " (Runtime)";
            runtimeMaterial.hideFlags = HideFlags.HideAndDontSave;
            if (runtimeAtlases[0] != null)
                runtimeMaterial.mainTexture = runtimeAtlases[0];
            runtime.material = runtimeMaterial;
            // The serialized Resources asset stays Static and cannot be mutated accidentally.
            // Only this non-persistent deep clone may grow for arbitrary player / LLM text.
            runtime.atlasPopulationMode = AtlasPopulationMode.Dynamic;
            runtime.isMultiAtlasTexturesEnabled = true;
            runtime.ReadFontAssetDefinition();
            return runtime;
        }
    }
}
