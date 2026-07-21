using System.Collections.Generic;
using System.Linq;
using KeyboardWanderer.World;
using UnityEditor;
using UnityEngine;

namespace KeyboardWanderer.Editor
{
    /// <summary>
    /// NinjaAdventure FX 시트(이미 spriteMode: Multiple로 슬라이스됨)의 서브 스프라이트를
    /// 프레임 순서대로 모아 SkillEffectCatalog 애셋에 직렬화한다.
    /// 스킬↔이펙트 매핑에 쓰는 클립 원본을 한곳에서 관리한다.
    /// </summary>
    public static class SkillEffectCatalogBuilder
    {
        private const string CatalogPath = "Assets/KeyboardWanderer/Resources/SkillEffectCatalog.asset";

        private readonly struct Source
        {
            public readonly string Id;
            public readonly string Path;
            public readonly float Fps;

            public Source(string id, string path, float fps)
            {
                Id = id;
                Path = path;
                Fps = fps;
            }
        }

        // 클립 원본 시트. 각 시트는 단일 행 프레임 스트립이며 서브 스프라이트 이름은 "<Sheet>_<index>"다.
        private static readonly Source[] Sources =
        {
            new Source(SkillEffectClips.Explosion, "Assets/NinjaAdventure/FX/Elemental/Explosion/SpriteSheet.png", 18f),
            new Source(SkillEffectClips.SpiritDouble, "Assets/NinjaAdventure/FX/Magic/Spirit/SpriteSheetDouble.png", 12f),
            new Source(SkillEffectClips.Spark, "Assets/NinjaAdventure/FX/Magic/Spark/SpriteSheet.png", 20f),
            new Source(SkillEffectClips.Boost, "Assets/NinjaAdventure/FX/Magic/Boost/SpriteSheet.png", 16f),
            new Source(SkillEffectClips.CircleSpark, "Assets/NinjaAdventure/FX/Magic/Circle/SpriteSheetSpark.png", 14f),
            new Source(SkillEffectClips.Aura, "Assets/NinjaAdventure/FX/Magic/Aura/SpriteSheet.png", 12f),
            new Source(SkillEffectClips.SmokeCircular, "Assets/NinjaAdventure/FX/Smoke/SmokeCircular/SpriteSheet.png", 16f),
            // DELETE fx_type 속성별 이펙트
            new Source(SkillEffectClips.Flam, "Assets/NinjaAdventure/FX/Elemental/Flam/SpriteSheet.png", 16f),
            new Source(SkillEffectClips.Ice, "Assets/NinjaAdventure/FX/Elemental/Ice/SpriteSheet.png", 16f),
            new Source(SkillEffectClips.Thunder, "Assets/NinjaAdventure/FX/Elemental/Thunder/SpriteSheet.png", 16f),
            new Source(SkillEffectClips.Water, "Assets/NinjaAdventure/FX/Elemental/Water/SpriteSheet.png", 16f),
            new Source(SkillEffectClips.Plant, "Assets/NinjaAdventure/FX/Elemental/Plant/SpriteSheet.png", 14f),
        };

        [MenuItem("Keyboard Wanderer/Rebuild Skill Effect Catalog")]
        public static void Rebuild()
        {
            SkillEffectCatalog catalog = AssetDatabase.LoadAssetAtPath<SkillEffectCatalog>(CatalogPath);
            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<SkillEffectCatalog>();
                AssetDatabase.CreateAsset(catalog, CatalogPath);
            }

            var clips = new List<SkillEffectCatalog.Clip>();
            var missing = new List<string>();
            foreach (Source source in Sources)
            {
                Sprite[] frames = LoadFrames(source.Path);
                if (frames.Length == 0)
                    missing.Add(source.Id + " (" + source.Path + ")");
                clips.Add(new SkillEffectCatalog.Clip
                {
                    Id = source.Id,
                    Frames = frames,
                    Fps = source.Fps,
                    ScaleMultiplier = 1f
                });
            }

            catalog.SetClips(clips.ToArray());
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            if (missing.Count > 0)
                Debug.LogWarning("[SkillEffectCatalog] 프레임을 찾지 못한 클립: " + string.Join(", ", missing));
            else
                Debug.Log("[SkillEffectCatalog] 클립 " + clips.Count + "개 재생성 완료.");
        }

        private static Sprite[] LoadFrames(string path)
        {
            List<Sprite> sprites = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().ToList();
            sprites.Sort((a, b) => FrameIndex(a.name).CompareTo(FrameIndex(b.name)));
            return sprites.ToArray();
        }

        private static int FrameIndex(string spriteName)
        {
            int underscore = spriteName.LastIndexOf('_');
            if (underscore >= 0 && int.TryParse(spriteName.Substring(underscore + 1), out int index))
                return index;
            return 0;
        }
    }
}
