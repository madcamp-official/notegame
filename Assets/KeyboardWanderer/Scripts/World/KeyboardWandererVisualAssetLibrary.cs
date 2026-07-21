using System;
using System.Collections.Generic;
using KeyboardWanderer.Demo;
using UnityEngine;

namespace KeyboardWanderer.World
{
    /// <summary>
    /// Ninja Adventure 원본 텍스처를 게임에서 쓰는 Sprite와 방향별 프레임으로 변환한다.
    /// 런타임에 만든 Sprite·Texture의 수명도 이 컴포넌트가 소유하므로 게임 컨트롤러는 에셋 생성 코드를 알지 않는다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class KeyboardWandererVisualAssetLibrary : MonoBehaviour
    {
        [SerializeField] private NinjaAdventureAssetManifest manifest;

        private const int CodriaAutotileCount = 16;

        private readonly List<Sprite> _runtimeSprites = new List<Sprite>();
        private readonly List<Texture2D> _runtimeTextures = new List<Texture2D>();
        private readonly Dictionary<Texture2D, Sprite[]> _codriaTileCache = new Dictionary<Texture2D, Sprite[]>();
        private bool _initialized;

        public NinjaAdventureAssetManifest Manifest => manifest;
        public bool IsReady => _initialized && WhiteSprite != null;

        public Sprite GrassSprite { get; private set; }
        public Sprite DirtSprite { get; private set; }
        public Sprite DarkGrassSprite { get; private set; }
        public Sprite WallSprite { get; private set; }
        public Sprite WaterSprite { get; private set; }
        public Sprite SnowSprite { get; private set; }
        public Sprite CavernFloorSprite { get; private set; }
        public Sprite RuinFloorSprite { get; private set; }
        public Sprite ForestTreeSprite { get; private set; }
        public Sprite ForestHouseSprite { get; private set; }
        public Sprite WetlandPlantSprite { get; private set; }
        public Sprite WetlandLandmarkSprite { get; private set; }
        public Sprite DesertPalmSprite { get; private set; }
        public Sprite DesertLandmarkSprite { get; private set; }
        public Sprite FrostTreeSprite { get; private set; }
        public Sprite FrostLandmarkSprite { get; private set; }
        public Sprite CavernCrystalSprite { get; private set; }
        public Sprite RuinTreeSprite { get; private set; }
        public Sprite RuinLandmarkSprite { get; private set; }

        public Sprite[] ForestDecorationSprites { get; private set; } = Array.Empty<Sprite>();
        public Sprite[] WetlandDecorationSprites { get; private set; } = Array.Empty<Sprite>();
        public Sprite[] DesertDecorationSprites { get; private set; } = Array.Empty<Sprite>();
        public Sprite[] FrostDecorationSprites { get; private set; } = Array.Empty<Sprite>();
        public Sprite[] CavernDecorationSprites { get; private set; } = Array.Empty<Sprite>();
        public Sprite[] RuinDecorationSprites { get; private set; } = Array.Empty<Sprite>();

        public Sprite PlayerSprite { get; private set; }
        public Sprite WardenSprite { get; private set; }
        public Sprite VillagerSprite { get; private set; }
        public Sprite SlimeSprite { get; private set; }
        public Sprite BookSprite { get; private set; }
        public Sprite CrateSprite { get; private set; }
        public Sprite ChestSprite { get; private set; }
        public Sprite D20Sprite { get; private set; }
        public Sprite WhiteSprite { get; private set; }

        public Sprite[] PlayerIdleFrames { get; private set; } = Array.Empty<Sprite>();
        public Sprite[] PlayerWalkFrames { get; private set; } = Array.Empty<Sprite>();
        public Sprite[] PlayerWalkLeftFrames { get; private set; } = Array.Empty<Sprite>();
        public Sprite[] PlayerWalkUpFrames { get; private set; } = Array.Empty<Sprite>();
        public Sprite[] PlayerWalkDownFrames { get; private set; } = Array.Empty<Sprite>();
        public Sprite[] PlayerAttackFrames { get; private set; } = Array.Empty<Sprite>();
        public Sprite[] PlayerAttackLeftFrames { get; private set; } = Array.Empty<Sprite>();
        public Sprite[] PlayerAttackUpFrames { get; private set; } = Array.Empty<Sprite>();
        public Sprite[] PlayerAttackDownFrames { get; private set; } = Array.Empty<Sprite>();
        public Sprite[] PlayerMagicFrames { get; private set; } = Array.Empty<Sprite>();
        public Sprite[] PlayerDebugFrames { get; private set; } = Array.Empty<Sprite>();
        public Sprite[] PlayerReviewFrames { get; private set; } = Array.Empty<Sprite>();
        public Sprite[] SlimeFrames { get; private set; } = Array.Empty<Sprite>();
        public Sprite[] VillagerFrames { get; private set; } = Array.Empty<Sprite>();

        /// <summary>Editor 변환 도구가 manifest 참조만 직렬화할 때 사용한다.</summary>
        public void ConfigureManifest(NinjaAdventureAssetManifest authoredManifest)
        {
            manifest = authoredManifest;
#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(this);
#endif
        }

        /// <summary>Inspector manifest를 우선 사용하고 없으면 AuthoringSettings와 Resources 순으로 찾는다.</summary>
        public void Initialize(NinjaAdventureAssetManifest authoredManifest,
            KeyboardWandererAuthoringSettings settings)
        {
            NinjaAdventureAssetManifest resolved = authoredManifest != null
                ? authoredManifest
                : settings != null && settings.AssetManifest != null
                    ? settings.AssetManifest
                    : Resources.Load<NinjaAdventureAssetManifest>("NinjaAdventureAssetManifest");
            if (_initialized && manifest == resolved)
                return;

            ReleaseRuntimeAssets();
            manifest = resolved;
            BuildTerrainSprites();
            BuildDecorationSprites();
            BuildActorFrames();
            BuildEntitySprites();
            _initialized = true;
        }

        /// <summary>이름 기반 호출을 한곳에 모아 컨트롤러가 manifest 필드 구조를 직접 참조하지 않게 한다.</summary>
        public AudioClip GetAudioClip(string fieldName)
        {
            if (manifest == null)
                return null;
            switch (fieldName)
            {
                case "AdventureMusic": return manifest.AdventureMusic;
                case "VillageMusic": return manifest.VillageMusic;
                case "BattleMusic": return manifest.BattleMusic;
                case "UiMoveSound": return manifest.UiMoveSound;
                case "UiAcceptSound": return manifest.UiAcceptSound;
                case "UiCancelSound": return manifest.UiCancelSound;
                case "SlashSound": return manifest.SlashSound;
                case "HitSound": return manifest.HitSound;
                case "CoinSound": return manifest.CoinSound;
                case "SuccessJingle": return manifest.SuccessJingle;
                default: return null;
            }
        }

        private void OnDestroy()
        {
            ReleaseRuntimeAssets();
        }

        private void BuildTerrainSprites()
        {
            GrassSprite = CreateAtlasSprite(manifest != null ? manifest.OutdoorFieldAtlas : null,
                manifest != null ? manifest.OutdoorGrassRect : new Rect(16f, 160f, 16f, 16f),
                "Field Grass", Hex("5d993f"));
            DirtSprite = CreateAtlasSprite(manifest != null ? manifest.OutdoorFieldAtlas : null,
                manifest != null ? manifest.OutdoorDirtRect : new Rect(16f, 208f, 16f, 16f),
                "Field Dirt", Hex("b97842"));
            DarkGrassSprite = CreateAtlasSprite(manifest != null ? manifest.OutdoorFieldAtlas : null,
                manifest != null ? manifest.OutdoorDarkGrassRect : new Rect(16f, 112f, 16f, 16f),
                "Field Dark Grass", Hex("315f38"));
            WallSprite = CreateAtlasSprite(manifest != null ? manifest.InteriorFloorAtlas : null,
                manifest != null ? manifest.WallRect : new Rect(176f, 96f, 16f, 16f),
                "Ruin Wall", Hex("35453a"));
            WaterSprite = CreateAtlasSprite(manifest != null ? manifest.WaterAtlas : null,
                new Rect(176f, 240f, 16f, 16f), "Wetland Water", Hex("36758b"));
            SnowSprite = CreateAtlasSprite(manifest != null ? manifest.OutdoorFieldAtlas : null,
                new Rect(16f, 16f, 16f, 16f), "Frost Snow", Hex("d9edf3"));
            RuinFloorSprite = CreateAtlasSprite(manifest != null ? manifest.OutdoorFieldAtlas : null,
                new Rect(16f, 64f, 16f, 16f), "Ruins Ground", Hex("a68a82"));
            CavernFloorSprite = CreateAtlasSprite(manifest != null ? manifest.DungeonAtlas : null,
                new Rect(80f, 16f, 16f, 16f), "Cavern Floor", Hex("4c425d"));
        }

        private void BuildDecorationSprites()
        {
            ForestTreeSprite = CreateAtlasSprite(manifest != null ? manifest.NatureAtlas : null,
                new Rect(0f, 304f, 32f, 32f), "Forest Tree", Hex("568b42"), new Vector2(0.5f, 0.08f));
            // 아틀라스에서 그림 한 채를 온전히 감싸도록 픽셀 경계를 실측해 맞춘 Rect들이다.
            // 격자 좌표로 어림잡으면 이웃 에셋이 섞여 들어와 건물이 잘린 채 그려진다.
            ForestHouseSprite = CreateAtlasSprite(manifest != null ? manifest.HouseAtlas : null,
                new Rect(0f, 320f, 64f, 48f), "Forest House", Hex("a7653f"), new Vector2(0.5f, 0.05f));
            WetlandPlantSprite = CreateAtlasSprite(manifest != null ? manifest.NatureAtlas : null,
                new Rect(112f, 144f, 16f, 32f), "Wetland Reeds", Hex("4f8f68"), new Vector2(0.5f, 0.08f));
            WetlandLandmarkSprite = CreateAtlasSprite(manifest != null ? manifest.WatermillAtlas : null,
                new Rect(0f, 0f, 34f, 36f), "Wetland Watermill", Hex("9a6b43"), new Vector2(0.5f, 0.08f));
            DesertPalmSprite = CreateAtlasSprite(manifest != null ? manifest.DesertAtlas : null,
                new Rect(160f, 32f, 64f, 40f), "Desert Palm", Hex("729347"), new Vector2(0.5f, 0.08f));
            DesertLandmarkSprite = CreateAtlasSprite(manifest != null ? manifest.DesertAtlas : null,
                new Rect(223f, 48f, 43f, 80f), "Desert Tower", Hex("d4a36a"), new Vector2(0.5f, 0.03f));
            FrostTreeSprite = CreateAtlasSprite(manifest != null ? manifest.NatureAtlas : null,
                new Rect(128f, 304f, 32f, 32f), "Frost Tree", Hex("dcebf0"), new Vector2(0.5f, 0.08f));
            FrostLandmarkSprite = CreateAtlasSprite(manifest != null ? manifest.HouseAtlas : null,
                new Rect(0f, 146f, 48f, 46f), "Frost Shelter", Hex("e5f1f4"), new Vector2(0.5f, 0.04f));
            CavernCrystalSprite = CreateAtlasSprite(manifest != null ? manifest.NatureAtlas : null,
                new Rect(0f, 110f, 42f, 34f), "Cavern Crystal", Hex("a978c4"), new Vector2(0.5f, 0.08f));
            RuinTreeSprite = CreateAtlasSprite(manifest != null ? manifest.NatureAtlas : null,
                new Rect(64f, 304f, 32f, 32f), "Ruins Dead Tree", Hex("75624f"), new Vector2(0.5f, 0.08f));
            RuinLandmarkSprite = CreateAtlasSprite(manifest != null ? manifest.AbandonedVillageAtlas : null,
                new Rect(192f, 16f, 64f, 80f), "Ancient Ruin", Hex("82705a"), new Vector2(0.5f, 0.04f));

            ForestDecorationSprites = new[]
            {
                ForestTreeSprite,
                CreateAtlasSprite(manifest != null ? manifest.NatureAtlas : null,
                    new Rect(32f, 304f, 32f, 32f), "Forest Pine", Hex("477a3d"), new Vector2(0.5f, 0.08f)),
                CreateAtlasSprite(manifest != null ? manifest.NatureAtlas : null,
                    new Rect(96f, 304f, 32f, 32f), "Forest Shrub", Hex("6a9a46"), new Vector2(0.5f, 0.08f)),
                CreateAtlasSprite(manifest != null ? manifest.NatureAtlas : null,
                    new Rect(96f, 208f, 32f, 32f), "Forest Plants", Hex("6f9e4c"), new Vector2(0.5f, 0.08f))
            };
            WetlandDecorationSprites = new[]
            {
                WetlandPlantSprite,
                CreateAtlasSprite(manifest != null ? manifest.NatureAtlas : null,
                    new Rect(128f, 208f, 32f, 32f), "Wetland Plants", Hex("4f8f68"), new Vector2(0.5f, 0.08f)),
                CreateAtlasSprite(manifest != null ? manifest.NatureAtlas : null,
                    new Rect(160f, 208f, 32f, 32f), "Wetland Flowers", Hex("6fa87b"), new Vector2(0.5f, 0.08f))
            };
            DesertDecorationSprites = new[]
            {
                DesertPalmSprite,
                CreateAtlasSprite(manifest != null ? manifest.DesertAtlas : null,
                    new Rect(160f, 72f, 48f, 48f), "Desert Palm Cluster", Hex("7f9a4d"), new Vector2(0.5f, 0.08f)),
                CreateAtlasSprite(manifest != null ? manifest.DesertAtlas : null,
                    new Rect(208f, 72f, 48f, 48f), "Desert Oasis Plant", Hex("8ca454"), new Vector2(0.5f, 0.08f))
            };
            FrostDecorationSprites = new[]
            {
                FrostTreeSprite,
                CreateAtlasSprite(manifest != null ? manifest.NatureAtlas : null,
                    new Rect(160f, 304f, 32f, 32f), "Frost Snow Tree", Hex("dcebf0"), new Vector2(0.5f, 0.08f)),
                CreateAtlasSprite(manifest != null ? manifest.NatureAtlas : null,
                    new Rect(192f, 304f, 32f, 32f), "Frost Bush", Hex("d4e7ed"), new Vector2(0.5f, 0.08f))
            };
            CavernDecorationSprites = new[]
            {
                CavernCrystalSprite,
                CreateAtlasSprite(manifest != null ? manifest.NatureAtlas : null,
                    new Rect(32f, 112f, 32f, 32f), "Cavern Crystal Cluster", Hex("9670b8"), new Vector2(0.5f, 0.08f)),
                CreateAtlasSprite(manifest != null ? manifest.NatureAtlas : null,
                    new Rect(64f, 112f, 32f, 32f), "Cavern Ore", Hex("8062a0"), new Vector2(0.5f, 0.08f))
            };
            RuinDecorationSprites = new[]
            {
                RuinTreeSprite,
                CreateAtlasSprite(manifest != null ? manifest.AbandonedVillageAtlas : null,
                    new Rect(0f, 112f, 32f, 32f), "Ruin Rubble", Hex("8b7757"), new Vector2(0.5f, 0.08f)),
                CreateAtlasSprite(manifest != null ? manifest.AbandonedVillageAtlas : null,
                    new Rect(32f, 112f, 32f, 32f), "Ruin Overgrowth", Hex("788152"), new Vector2(0.5f, 0.08f))
            };
        }

        private void BuildActorFrames()
        {
            if (manifest == null)
                return;
            if (manifest.PlayerAnimatorController == null)
            {
                PlayerIdleFrames = CreateSheetFrames(manifest.PlayerIdleSheet,
                    Mathf.Max(1, manifest.PlayerFrameSize), 3, "Player Idle");
                PlayerWalkFrames = CreateSheetFrames(manifest.PlayerWalkSheet,
                    Mathf.Max(1, manifest.PlayerFrameSize), 3, "Player Walk");
                PlayerAttackFrames = CreateSheetFrames(manifest.PlayerAttackSheet,
                    Mathf.Max(1, manifest.PlayerFrameSize), 3, "Player Attack");
            }
            if (manifest.SlimeAnimatorController == null)
                SlimeFrames = CreateSheetFrames(manifest.SlimeSheet,
                    Mathf.Max(1, manifest.CreatureFrameSize), 3, "Slime");
            if (manifest.VillagerAnimatorController == null)
                VillagerFrames = CreateSheetFrames(manifest.VillagerWalkSheet,
                    Mathf.Max(1, manifest.CreatureFrameSize), 3, "Villager");

            if (manifest.NeopjukiAtlas == null)
                return;
            int cellWidth = Mathf.Max(1, manifest.NeopjukiCellWidth);
            int cellHeight = Mathf.Max(1, manifest.NeopjukiCellHeight);
            PlayerIdleFrames = CreateNeopjukiFrames(manifest.NeopjukiAtlas, cellWidth, cellHeight, 0, 6, "Neopjuki Idle");
            PlayerWalkFrames = CreateNeopjukiFrames(manifest.NeopjukiAtlas, cellWidth, cellHeight, 1, 8, "Neopjuki Right");
            PlayerWalkLeftFrames = CreateNeopjukiFrames(manifest.NeopjukiAtlas, cellWidth, cellHeight, 2, 8, "Neopjuki Left");
            PlayerAttackFrames = CreateNeopjukiFrames(manifest.NeopjukiAtlas, cellWidth, cellHeight, 8, 8, "Neopjuki Attack Right");
            PlayerAttackLeftFrames = CreateNeopjukiFrames(manifest.NeopjukiAtlas, cellWidth, cellHeight, 11, 8, "Neopjuki Attack Left");
            PlayerAttackUpFrames = CreateNeopjukiFrames(manifest.NeopjukiAtlas, cellWidth, cellHeight, 12, 8, "Neopjuki Attack Up");
            PlayerAttackDownFrames = CreateNeopjukiFrames(manifest.NeopjukiAtlas, cellWidth, cellHeight, 13, 8, "Neopjuki Attack Down");
            PlayerWalkUpFrames = CreateNeopjukiFrames(manifest.NeopjukiAtlas, cellWidth, cellHeight, 14, 8, "Neopjuki Walk Up");
            PlayerWalkDownFrames = CreateNeopjukiFrames(manifest.NeopjukiAtlas, cellWidth, cellHeight, 15, 8, "Neopjuki Walk Down");
            // 스킬 시전용 모션: 키보드 마법(행 9), 키보드 디버그(행 10), 리뷰/조사(행 7).
            PlayerMagicFrames = CreateNeopjukiFrames(manifest.NeopjukiAtlas, cellWidth, cellHeight, 9, 8, "Neopjuki Magic");
            PlayerDebugFrames = CreateNeopjukiFrames(manifest.NeopjukiAtlas, cellWidth, cellHeight, 10, 8, "Neopjuki Debug");
            PlayerReviewFrames = CreateNeopjukiFrames(manifest.NeopjukiAtlas, cellWidth, cellHeight, 7, 6, "Neopjuki Review");
        }

        private void BuildEntitySprites()
        {
            PlayerSprite = FirstOrSource(PlayerIdleFrames, manifest != null ? manifest.PlayerIdle : null,
                "Player", Hex("6a9d45"));
            WardenSprite = SourceOrFallback(manifest != null ? manifest.WardenIdle : null,
                "Warden", Hex("8c7654"));
            VillagerSprite = FirstOrSource(VillagerFrames, manifest != null ? manifest.VillagerIdle : null,
                "Villager", Hex("c58f58"));
            SlimeSprite = FirstOrSource(SlimeFrames, manifest != null ? manifest.SlimeIdle : null,
                "Slime", Hex("61a65d"));
            BookSprite = SourceOrFallback(manifest != null ? manifest.RuneBook : null,
                "Rune Book", Hex("8d65a8"));
            CrateSprite = SourceOrFallback(manifest != null ? manifest.Crate : null,
                "Crate", Hex("95623d"));
            ChestSprite = SourceOrFallback(manifest != null ? manifest.TreasureChest : null,
                "Chest", Hex("d7a743"));
            D20Sprite = SourceOrFallback(manifest != null ? manifest.D20 : null,
                "D20", Hex("d3a64b"));
            WhiteSprite = CreateSolidSprite(Color.white, "White Pixel");
        }

        /// <summary>
        /// 코드리아 타일셋은 4×4 오토타일 블롭이고, 타일 인덱스가 곧 이웃 비트마스크다
        /// (N=1, E=2, S=4, W=8). 시트 좌상단이 인덱스 0이므로 Unity Rect 기준으로 y를 뒤집는다.
        /// </summary>
        public Sprite CodriaAutotileSprite(Texture2D sheet, int mask)
        {
            if (sheet == null)
                return null;
            if (!_codriaTileCache.TryGetValue(sheet, out Sprite[] tiles))
            {
                tiles = new Sprite[CodriaAutotileCount];
                _codriaTileCache.Add(sheet, tiles);
            }

            mask &= CodriaAutotileCount - 1;
            if (tiles[mask] != null)
                return tiles[mask];

            int size = manifest != null && manifest.CodriaTileSize > 0 ? manifest.CodriaTileSize : 16;
            var rect = new Rect((mask % 4) * size, sheet.height - ((mask / 4) + 1) * size, size, size);
            Sprite sprite = CreateAtlasSprite(sheet, rect, sheet.name + " " + mask, Color.magenta);
            tiles[mask] = sprite;
            return sprite;
        }

        private Sprite CreateAtlasSprite(Texture2D texture, Rect requestedRect, string spriteName,
            Color fallbackColor, Vector2? requestedPivot = null)
        {
            if (texture == null || texture.width <= 0 || texture.height <= 0)
                return CreateSolidSprite(fallbackColor, spriteName + " Fallback");
            Rect rect = SafeRect(texture, requestedRect);
            Sprite sprite = Sprite.Create(texture, rect, requestedPivot ?? new Vector2(0.5f, 0.5f),
                16f, 0, SpriteMeshType.FullRect);
            sprite.name = spriteName;
            _runtimeSprites.Add(sprite);
            return sprite;
        }

        private Sprite[] CreateSheetFrames(Texture2D texture, int frameSize, int rowFromBottom, string prefix)
        {
            if (texture == null || frameSize <= 0 || texture.width < frameSize || texture.height < frameSize)
                return Array.Empty<Sprite>();
            int count = Mathf.Min(4, texture.width / frameSize);
            int y = Mathf.Clamp(rowFromBottom * frameSize, 0, texture.height - frameSize);
            var frames = new Sprite[count];
            for (int i = 0; i < count; i++)
            {
                frames[i] = Sprite.Create(texture, new Rect(i * frameSize, y, frameSize, frameSize),
                    new Vector2(0.5f, 0.42f), frameSize, 0, SpriteMeshType.FullRect);
                frames[i].name = prefix + " " + i;
                _runtimeSprites.Add(frames[i]);
            }
            return frames;
        }

        private Sprite[] CreateNeopjukiFrames(Texture2D texture, int cellWidth, int cellHeight,
            int rowFromTop, int frameCount, string prefix)
        {
            if (texture == null || cellWidth <= 0 || cellHeight <= 0 ||
                texture.width < cellWidth || texture.height < cellHeight)
                return Array.Empty<Sprite>();
            int columns = texture.width / cellWidth;
            int rows = texture.height / cellHeight;
            if (rowFromTop < 0 || rowFromTop >= rows)
                return Array.Empty<Sprite>();
            int count = Mathf.Min(frameCount, columns);
            int y = texture.height - (rowFromTop + 1) * cellHeight;
            var frames = new Sprite[count];
            for (int i = 0; i < count; i++)
            {
                frames[i] = Sprite.Create(texture, new Rect(i * cellWidth, y, cellWidth, cellHeight),
                    new Vector2(0.5f, 0f), cellWidth, 0, SpriteMeshType.FullRect);
                frames[i].name = prefix + " " + i;
                _runtimeSprites.Add(frames[i]);
            }
            return frames;
        }

        private Sprite SourceOrFallback(Sprite source, string name, Color color)
        {
            return source != null ? source : CreateSolidSprite(color, name + " Fallback");
        }

        private Sprite FirstOrSource(Sprite[] frames, Sprite source, string name, Color color)
        {
            return frames != null && frames.Length > 0 ? frames[0] : SourceOrFallback(source, name, color);
        }

        private Sprite CreateSolidSprite(Color color, string name)
        {
            var texture = new Texture2D(1, 1) { name = name + " Texture" };
            texture.SetPixel(0, 0, color);
            texture.Apply();
            texture.wrapMode = TextureWrapMode.Repeat;
            texture.filterMode = FilterMode.Point;
            _runtimeTextures.Add(texture);
            Sprite sprite = Sprite.Create(texture, new Rect(0f, 0f, 1f, 1f),
                new Vector2(0.5f, 0.5f), 1f);
            sprite.name = name;
            _runtimeSprites.Add(sprite);
            return sprite;
        }

        private void ReleaseRuntimeAssets()
        {
            for (int i = 0; i < _runtimeSprites.Count; i++)
                if (_runtimeSprites[i] != null)
                    Destroy(_runtimeSprites[i]);
            for (int i = 0; i < _runtimeTextures.Count; i++)
                if (_runtimeTextures[i] != null)
                    Destroy(_runtimeTextures[i]);
            _runtimeSprites.Clear();
            _runtimeTextures.Clear();
            _codriaTileCache.Clear();
            _initialized = false;
        }

        private static Rect SafeRect(Texture2D texture, Rect rect)
        {
            float width = Mathf.Clamp(rect.width, 1f, texture.width);
            float height = Mathf.Clamp(rect.height, 1f, texture.height);
            return new Rect(Mathf.Clamp(rect.x, 0f, texture.width - width),
                Mathf.Clamp(rect.y, 0f, texture.height - height), width, height);
        }

        private static Color Hex(string rgb)
        {
            ColorUtility.TryParseHtmlString("#" + rgb, out Color color);
            return color;
        }
    }
}
