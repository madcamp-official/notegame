using System.Linq;
using KeyboardWanderer.Demo;
using UnityEditor;
using UnityEngine;

namespace KeyboardWanderer.Editor
{
    public static class NinjaAdventureManifestBuilder
    {
        private const string ManifestPath = "Assets/KeyboardWanderer/Resources/NinjaAdventureAssetManifest.asset";
        private const int CurrentManifestVersion = 3;

        [InitializeOnLoadMethod]
        private static void ScheduleEnsureManifest()
        {
            EditorApplication.delayCall += EnsureManifest;
        }

        [MenuItem("Keyboard Wanderer/Rebuild Ninja Adventure Manifest")]
        public static void RebuildManifest()
        {
            NinjaAdventureAssetManifest manifest = AssetDatabase.LoadAssetAtPath<NinjaAdventureAssetManifest>(ManifestPath);
            if (manifest == null)
            {
                manifest = ScriptableObject.CreateInstance<NinjaAdventureAssetManifest>();
                AssetDatabase.CreateAsset(manifest, ManifestPath);
            }

            manifest.InteriorFloorAtlas = LoadPixelTexture(
                "Assets/NinjaAdventure/Backgrounds/Tilesets/Interior/TilesetInteriorFloor.png");
            manifest.FloorRect = new Rect(192, 32, 16, 16);
            manifest.WallRect = new Rect(176, 96, 16, 16);
            manifest.HazardRect = new Rect(0, 32, 16, 16);

            manifest.OutdoorFieldAtlas = LoadPixelTexture(
                "Assets/NinjaAdventure/Backgrounds/Tilesets/TilesetField.png");
            manifest.WaterAtlas = LoadPixelTexture(
                "Assets/NinjaAdventure/Backgrounds/Tilesets/TilesetWater.png");
            manifest.NatureAtlas = LoadPixelTexture(
                "Assets/NinjaAdventure/Backgrounds/Tilesets/TilesetNature.png");
            manifest.HouseAtlas = LoadPixelTexture(
                "Assets/NinjaAdventure/Backgrounds/Tilesets/TilesetHouse.png");
            manifest.DesertAtlas = LoadPixelTexture(
                "Assets/NinjaAdventure/Backgrounds/Tilesets/TilesetDesert.png");
            manifest.DungeonAtlas = LoadPixelTexture(
                "Assets/NinjaAdventure/Backgrounds/Tilesets/TilesetDungeon.png");
            manifest.AbandonedVillageAtlas = LoadPixelTexture(
                "Assets/NinjaAdventure/Backgrounds/Tilesets/TilesetVillageAbandoned.png");
            manifest.WatermillAtlas = LoadPixelTexture(
                "Assets/NinjaAdventure/Backgrounds/Animated/WaterMill/Watermill_A_34x36.png");
            manifest.OutdoorDirtRect = new Rect(16, 208, 16, 16);
            manifest.OutdoorGrassRect = new Rect(16, 160, 16, 16);
            manifest.OutdoorDarkGrassRect = new Rect(16, 112, 16, 16);

            manifest.PlayerIdleSheet = LoadPixelTexture(
                "Assets/NinjaAdventure/Actor/CharacterAnimated/NinjaGreen/Separate/Idle.png");
            manifest.PlayerWalkSheet = LoadPixelTexture(
                "Assets/NinjaAdventure/Actor/CharacterAnimated/NinjaGreen/Separate/Walk.png");
            manifest.PlayerAttackSheet = LoadPixelTexture(
                "Assets/NinjaAdventure/Actor/CharacterAnimated/NinjaGreen/Separate/Attack.png");
            manifest.SlimeSheet = LoadPixelTexture(
                "Assets/NinjaAdventure/Actor/Monster/Slime/Slime.png");
            manifest.VillagerWalkSheet = LoadPixelTexture(
                "Assets/NinjaAdventure/Actor/Character/Villager/SeparateAnim/Walk.png");
            manifest.PlayerFrameSize = 32;
            manifest.CreatureFrameSize = 16;

            manifest.PlayerIdle = LoadSprite("Assets/NinjaAdventure/Actor/Character/NinjaGreen/SeparateAnim/Idle.png", "Idle_1");
            manifest.WardenIdle = LoadSprite("Assets/NinjaAdventure/Actor/Character/Samurai/SeparateAnim/Idle.png", "Idle_1");
            manifest.SlimeIdle = LoadSprite("Assets/NinjaAdventure/Actor/Monster/Slime/Slime.png", "Slime_1");
            manifest.VillagerIdle = LoadSprite(
                "Assets/NinjaAdventure/Actor/Character/Villager/SeparateAnim/Idle.png", "Idle_1");
            manifest.RuneBook = LoadFirstSprite("Assets/NinjaAdventure/Items/Object/Book.png");
            manifest.Crate = LoadFirstSprite("Assets/NinjaAdventure/Items/Object/CrateEmpty.png");
            manifest.TreasureChest = LoadFirstSprite("Assets/NinjaAdventure/Items/Treasure/BigTreasureChest.png");
            manifest.D20 = LoadFirstSprite("Assets/NinjaAdventure/Items/Object/Dice 20.png");

            manifest.WoodPanel = LoadFirstSprite(
                "Assets/NinjaAdventure/Ui/Theme/Theme Wood/nine_path_panel.png");
            manifest.WoodBackground = LoadFirstSprite(
                "Assets/NinjaAdventure/Ui/Theme/Theme Wood/nine_path_bg.png");
            manifest.WoodButtonNormal = LoadFirstSprite(
                "Assets/NinjaAdventure/Ui/Theme/Theme Wood/button_normal.png");
            manifest.WoodButtonHover = LoadFirstSprite(
                "Assets/NinjaAdventure/Ui/Theme/Theme Wood/button_hover.png");
            manifest.WoodButtonPressed = LoadFirstSprite(
                "Assets/NinjaAdventure/Ui/Theme/Theme Wood/button_pressed.png");
            manifest.WoodButtonDisabled = LoadFirstSprite(
                "Assets/NinjaAdventure/Ui/Theme/Theme Wood/button_disabled.png");
            manifest.HeartIcon = LoadFirstSprite("Assets/NinjaAdventure/Ui/Receptacle/IconHeart.png");
            manifest.MoveIcon = LoadFirstSprite("Assets/NinjaAdventure/Ui/Skill Icon/Items & Weapon/Boot.png");
            manifest.MoveIconDisabled = LoadFirstSprite(
                "Assets/NinjaAdventure/Ui/Skill Icon/Items & Weapon/BootDisabled.png");
            manifest.CopyIcon = LoadFirstSprite("Assets/NinjaAdventure/Ui/Skill Icon/Items & Weapon/Scroll.png");
            manifest.CopyIconDisabled = LoadFirstSprite(
                "Assets/NinjaAdventure/Ui/Skill Icon/Items & Weapon/ScrollDisabled.png");
            manifest.DeleteIcon = LoadFirstSprite("Assets/NinjaAdventure/Ui/Skill Icon/Spell/Fireball.png");
            manifest.DeleteIconDisabled = LoadFirstSprite(
                "Assets/NinjaAdventure/Ui/Skill Icon/Spell/FireballDisabled.png");
            manifest.InteractIcon = LoadFirstSprite(
                "Assets/NinjaAdventure/Ui/Skill Icon/Job & Action/Interact.png");
            manifest.PixelFont = AssetDatabase.LoadAssetAtPath<Font>("Assets/NinjaAdventure/Ui/Font/NormalFont.ttf");

            manifest.AdventureMusic = LoadAudioClip("Assets/NinjaAdventure/Audio/Musics/1 - Adventure Begin.ogg");
            manifest.VillageMusic = LoadAudioClip("Assets/NinjaAdventure/Audio/Musics/33 - Calm Village.ogg");
            manifest.BattleMusic = LoadAudioClip("Assets/NinjaAdventure/Audio/Musics/17 - Fight.ogg");
            manifest.UiMoveSound = LoadAudioClip("Assets/NinjaAdventure/Audio/Sounds/Menu/Move1.wav");
            manifest.UiAcceptSound = LoadAudioClip("Assets/NinjaAdventure/Audio/Sounds/Menu/Accept.wav");
            manifest.UiCancelSound = LoadAudioClip("Assets/NinjaAdventure/Audio/Sounds/Menu/Cancel.wav");
            manifest.SlashSound = LoadAudioClip("Assets/NinjaAdventure/Audio/Sounds/Whoosh & Slash/Slash.wav");
            manifest.HitSound = LoadAudioClip("Assets/NinjaAdventure/Audio/Sounds/Hit & Impact/Hit1.wav");
            manifest.CoinSound = LoadAudioClip("Assets/NinjaAdventure/Audio/Sounds/Bonus/Coin.wav");
            manifest.SuccessJingle = LoadAudioClip("Assets/NinjaAdventure/Audio/Jingles/Success1.wav");
            manifest.BuilderVersion = CurrentManifestVersion;

            EditorUtility.SetDirty(manifest);
            AssetDatabase.SaveAssets();
        }

        private static void EnsureManifest()
        {
            NinjaAdventureAssetManifest manifest = AssetDatabase.LoadAssetAtPath<NinjaAdventureAssetManifest>(ManifestPath);
            if (manifest == null || manifest.BuilderVersion < CurrentManifestVersion)
                RebuildManifest();
        }

        private static Sprite LoadSprite(string path, string spriteName)
        {
            ConfigurePixelTexture(path);
            return AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().FirstOrDefault(sprite => sprite.name == spriteName)
                ?? LoadFirstSprite(path);
        }

        private static Sprite LoadFirstSprite(string path)
        {
            ConfigurePixelTexture(path);
            return AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().FirstOrDefault();
        }

        private static Texture2D LoadPixelTexture(string path)
        {
            ConfigurePixelTexture(path);
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        private static AudioClip LoadAudioClip(string path)
        {
            return AssetDatabase.LoadAssetAtPath<AudioClip>(path);
        }

        private static void ConfigurePixelTexture(string path)
        {
            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null)
                return;

            bool changed = false;
            if (importer.filterMode != FilterMode.Point)
            {
                importer.filterMode = FilterMode.Point;
                changed = true;
            }

            if (importer.mipmapEnabled)
            {
                importer.mipmapEnabled = false;
                changed = true;
            }

            if (importer.textureCompression != TextureImporterCompression.Uncompressed)
            {
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                changed = true;
            }

            if (importer.wrapMode != TextureWrapMode.Clamp)
            {
                importer.wrapMode = TextureWrapMode.Clamp;
                changed = true;
            }

            if (changed)
                importer.SaveAndReimport();
        }
    }
}
