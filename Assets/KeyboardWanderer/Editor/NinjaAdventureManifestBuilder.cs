using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using KeyboardWanderer.Demo;
using UnityEditor;
using UnityEngine;

namespace KeyboardWanderer.Editor
{
    public static class NinjaAdventureManifestBuilder
    {
        private const string ManifestPath = "Assets/KeyboardWanderer/Resources/NinjaAdventureAssetManifest.asset";
        private const string BuilderScriptPath = "Assets/KeyboardWanderer/Editor/NinjaAdventureManifestBuilder.cs";
        private const string NeopjukiAtlasPath =
            "Assets/KeyboardWanderer/Art/Pets/Neopjuki/NeopjukiUnityAtlas.png";
        private const string CodriaTilesetFolder = "Assets/testSprite/tilesets/";

        private static readonly List<string> _missingAssetPaths = new();

        [InitializeOnLoadMethod]
        private static void ScheduleEnsureManifest()
        {
            EditorApplication.delayCall += EnsureManifest;
        }

        [MenuItem("Keyboard Wanderer/Rebuild Ninja Adventure Manifest")]
        public static void RebuildManifest()
        {
            _missingAssetPaths.Clear();
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

            manifest.GeneralFieldTiles = LoadCodriaTileset("General_Field");
            manifest.GeneralWaterTiles = LoadCodriaTileset("General_Water");
            manifest.GeneralHighGroundTiles = LoadCodriaTileset("General_HighGround");
            manifest.GeneralSlopeTiles = LoadCodriaTileset("General_Slope");
            manifest.BugForestGroundTiles = LoadCodriaTileset("BugForest_Ground");
            manifest.BugForestCampTiles = LoadCodriaTileset("BugForest_Camp");
            manifest.BugForestHoleTiles = LoadCodriaTileset("BugForest_Hole");
            manifest.BugForestLakeTiles = LoadCodriaTileset("BugForest_Lake");
            manifest.BufferVillageGroundTiles = LoadCodriaTileset("BufferVillage_Ground");
            manifest.DeadlockCityGroundTiles = LoadCodriaTileset("DeadlockCity_Ground");
            manifest.DeadlockCityVirusTiles = LoadCodriaTileset("DeadlockCity_Virus");
            manifest.DataArchiveRockTiles = LoadCodriaTileset("DataArchive_Rock");
            manifest.DataArchiveCrystalFloorTiles = LoadCodriaTileset("DataArchive_PureCrystalTile");
            manifest.DataArchiveWoodPlankTiles = LoadCodriaTileset("DataArchive_WoodPlank");
            manifest.LegacyCitadelSnowTiles = LoadCodriaTileset("LegacyCitadel_Snow");
            manifest.LegacyCitadelIceTiles = LoadCodriaTileset("LegacyCitadel_Ice");
            manifest.RootSystemGroundTiles = LoadCodriaTileset("RootSystem_Ground");
            manifest.CodriaTileSize = 16;

            manifest.BugForestDataTreeProps = LoadCodriaTileset("BugForest_DataTree");
            manifest.BugForestRockProps = LoadCodriaTileset("BugForest_Rocks");
            manifest.BugForestCampProps = LoadCodriaTileset("BugForest_Camp_Objects");
            manifest.BufferVillageBuildingProps = LoadCodriaTileset("BufferVillage_Buildings");
            manifest.BufferVillageFenceProps = LoadCodriaTileset("BufferVillage_Fence");
            manifest.DeadlockCityBuildingProps = LoadCodriaTileset("DeadlockCity_Buildings");
            manifest.DataArchiveBookshelfProps = LoadCodriaTileset("DataArchive_Bookshelf");
            manifest.DataArchiveCrystalProps = LoadCodriaTileset("DataArchive_Crystal");
            manifest.LegacyCitadelEdificeProps = LoadCodriaTileset("LegacyCitadel_Edifice");
            manifest.LegacyCitadelGooseProps = LoadCodriaTileset("LegacyCitadel_Goose");
            manifest.RootSystemServerProps = LoadCodriaTileset("RootSystem_Device_Server");
            manifest.RootSystemMonitorProps = LoadCodriaTileset("RootSystem_Device_Monitor");

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
            manifest.NeopjukiAtlas = LoadPixelTexture(NeopjukiAtlasPath);
            manifest.NeopjukiCellWidth = 192;
            manifest.NeopjukiCellHeight = 208;

            manifest.PlayerIdle = LoadSprite("Assets/NinjaAdventure/Actor/Character/NinjaGreen/SeparateAnim/Idle.png", "Idle_1");
            manifest.WardenIdle = LoadSprite("Assets/NinjaAdventure/Actor/Character/Samurai/SeparateAnim/Idle.png", "Idle_1");
            manifest.SlimeIdle = LoadSprite("Assets/NinjaAdventure/Actor/Monster/Slime/Slime.png", "Slime_1");
            manifest.VillagerIdle = LoadSprite(
                "Assets/NinjaAdventure/Actor/Character/Villager/SeparateAnim/Idle.png", "Idle_1");
            manifest.RuneBook = LoadFirstSprite("Assets/NinjaAdventure/Items/Object/Book.png");
            manifest.Crate = LoadFirstSprite("Assets/NinjaAdventure/Items/Object/CrateEmpty.png");
            manifest.TreasureChest = LoadFirstSprite("Assets/NinjaAdventure/Items/Treasure/BigTreasureChest.png");
            manifest.D20 = LoadFirstSprite("Assets/NinjaAdventure/Items/Object/Dice 20.png");
            manifest.D20Prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/3DObjects/icosa.prefab");

            manifest.PlayerAnimatorController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(
                "Assets/KeyboardWanderer/Animations/Player/Player.controller");
            manifest.SlimeAnimatorController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(
                "Assets/KeyboardWanderer/Animations/Slime/Slime.controller");
            manifest.VillagerAnimatorController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(
                "Assets/KeyboardWanderer/Animations/Villager/Villager.controller");

            manifest.NpcAnimations = new[]
            {
                Actor("npc.villager.green.v1", "Character/Villager/SeparateAnim/Walk.png", "Walk_0", "Villager/Villager.controller"),
                Actor("npc.villager2.v1", "Character/Villager2/SeparateAnim/Walk.png", "Walk_0", "NPC/Villager2/Villager2.controller"),
                Actor("npc.villager3.v1", "Character/Villager3/SeparateAnim/Walk.png", "Walk_0", "NPC/Villager3/Villager3.controller"),
                Actor("npc.villager4.v1", "Character/Villager4/SeparateAnim/Walk.png", "Walk_0", "NPC/Villager4/Villager4.controller"),
                Actor("npc.villager5.v1", "Character/Villager5/SeparateAnim/Walk.png", "Walk_0", "NPC/Villager5/Villager5.controller"),
                Actor("npc.villager6.v1", "Character/Villager6/SeparateAnim/Walk.png", "Walk_0", "NPC/Villager6/Villager6.controller"),
                Actor("npc.old-man.v1", "Character/OldMan/SeparateAnim/Walk.png", "Walk_0", "NPC/OldMan/OldMan.controller"),
                Actor("npc.noble.v1", "Character/Noble/SeparateAnim/Walk.png", "Walk_0", "NPC/Noble/Noble.controller"),
                Actor("npc.princess.v1", "Character/Princess/SeparateAnim/Walk.png", "Walk_0", "NPC/Princess/Princess.controller"),
                Actor("npc.samurai.v1", "Character/Samurai/SeparateAnim/Walk.png", "Walk_0", "NPC/Samurai/Samurai.controller")
            };
            manifest.MonsterAnimations = new[]
            {
                Actor("enemy.slime.blue.v1", "Monster/Slime/Slime.png", "Slime_0", "Slime/Slime.controller"),
                Actor("enemy.slime.green.v1", "Monster/Slime2/Slime2.png", "Slime2_0", "Monster/Slime2/Slime2.controller"),
                Actor("enemy.mushroom.v1", "Monster/Mushroom/mushroom.png", "mushroom_0", "Monster/Mushroom/Mushroom.controller"),
                Actor("enemy.blue-bat.v1", "Monster/BlueBat/SpriteSheet.png", "SpriteSheet_0", "Monster/BlueBat/BlueBat.controller"),
                Actor("enemy.bear.v1", "Monster/Bear/SpriteSheet.png", "SpriteSheet_0", "Monster/Bear/Bear.controller"),
                Actor("enemy.cyclope.v1", "Monster/Cyclope/SpriteSheet.png", "SpriteSheet_0", "Monster/Cyclope/Cyclope.controller"),
                Actor("enemy.dragon.v1", "Monster/Dragon/SpriteSheet.png", "SpriteSheet_0", "Monster/Dragon/Dragon.controller"),
                Actor("enemy.kappa-green.v1", "Monster/KappaGreen/SpriteSheet.png", "SpriteSheet_0", "Monster/KappaGreen/KappaGreen.controller"),
                Actor("enemy.snake.v1", "Monster/Snake/Snake.png", "Snake_0", "Monster/Snake/Snake.controller"),
                Actor("enemy.spider-red.v1", "Monster/SpiderRed/SpriteSheet.png", "SpriteSheet_0", "Monster/SpiderRed/SpiderRed.controller")
            };
            manifest.BossAnimations = new[]
            {
                Boss("boss.demon-cyclop.v1", "DemonCyclop", "Idle.png", "Idle_0"),
                Boss("boss.demon-cyclop-2.v1", "DemonCyclop2", "Idle.png", "Idle_0"),
                Boss("boss.giant-bamboo.v1", "GiantBamboo", "Idle.png", "Idle_0"),
                Boss("boss.giant-bamboo-2.v1", "GiantBamboo2", "Idle.png", "Idle_0"),
                Boss("boss.giant-blue-samurai.v1", "GiantBlueSamurai", "Idle.png", "Idle_0"),
                Boss("boss.giant-red-samurai.v1", "GiantRedSamurai", "Idle.png", "Idle_0"),
                Boss("boss.giant-flam.v1", "GiantFlam", "Idle.png", "Idle_0"),
                Boss("boss.giant-frog.v1", "GiantFrog", "Idle40x40.png", "Idle40x40_0"),
                Boss("boss.giant-frog-2.v1", "GiantFrog2", "Idle.png", "Idle_0"),
                Boss("boss.giant-racoon.v1", "GiantRacoon", "Idle.png", "Idle_0"),
                Boss("boss.giant-racoon-gold.v1", "GiantRacoonGold", "Idle.png", "Idle_0"),
                Boss("boss.giant-slime.v1", "GiantSlime", "Idle.png", "Idle_0"),
                Boss("boss.giant-slime-2.v1", "GiantSlime2", "Idle.png", "Idle_0"),
                Boss("boss.giant-spirit.v1", "GiantSpirit", "Idle.png", "Idle_0"),
                Boss("boss.squid-green.v1", "SquidGreen", "Idle.png", "Idle_0"),
                Boss("boss.squid-red.v1", "SquidRed", "Idle.png", "Idle_0"),
                Boss("boss.tengu-blue.v1", "TenguBlue", "Idle.png", "Idle_0"),
                Boss("boss.tengu-red.v1", "TenguRed", "Idle.png", "Idle_0")
            };
            manifest.ElementalEffects = new[]
            {
                Effect("ELEMENTAL_EXPLOSION", "Explosion", "SpriteSheet.png", 9),
                Effect("ELEMENTAL_FLAME", "Flam", "SpriteSheet.png", 10),
                Effect("ELEMENTAL_ICE", "Ice", "SpriteSheet.png", 10),
                Effect("ELEMENTAL_ICE_B", "Ice", "SpriteSheetB.png", 9),
                Effect("ELEMENTAL_ICE_FLAKE", "Ice", "SpriteSheetFlake.png", 9),
                Effect("ELEMENTAL_PLANT", "Plant", "SpriteSheet.png", 10),
                Effect("ELEMENTAL_PLANT_B", "Plant", "SpriteSheetB.png", 10),
                Effect("ELEMENTAL_ROCK", "Rock", "SpriteSheet.png", 14),
                Effect("ELEMENTAL_ROCK_B", "Rock", "SpriteSheetB.png", 14),
                Effect("ELEMENTAL_ROCK_SPIKE", "RockSpike", "SpriteSheet.png", 15),
                Effect("ELEMENTAL_THUNDER", "Thunder", "SpriteSheet.png", 8),
                Effect("ELEMENTAL_WATER", "Water", "SpriteSheet.png", 10),
                Effect("ELEMENTAL_WATER_PILLAR", "WaterPillar", "SpriteSheet.png", 10)
            };

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
            manifest.WoodPanelInterior = LoadFirstSprite(
                "Assets/NinjaAdventure/Ui/Theme/Theme Wood/nine_path_panel_interior.png");
            manifest.WoodPanelFocus = LoadFirstSprite(
                "Assets/NinjaAdventure/Ui/Theme/Theme Wood/nine_path_focus.png");
            manifest.WoodInventoryCell = LoadFirstSprite(
                "Assets/NinjaAdventure/Ui/Theme/Theme Wood/inventory_cell.png");
            manifest.WoodSliderProgress = LoadFirstSprite(
                "Assets/NinjaAdventure/Ui/Theme/Theme Wood/slider_progress.png");
            manifest.WoodSliderGrabber = LoadFirstSprite(
                "Assets/NinjaAdventure/Ui/Theme/Theme Wood/h_slidder_grabber.png");
            manifest.WoodChecked = LoadFirstSprite(
                "Assets/NinjaAdventure/Ui/Theme/Theme Wood/checked.png");
            manifest.WoodUnchecked = LoadFirstSprite(
                "Assets/NinjaAdventure/Ui/Theme/Theme Wood/unchecked.png");
            manifest.DialogueBox = LoadFirstSprite("Assets/NinjaAdventure/Ui/Dialog/DialogueBoxSimple.png");
            manifest.DialogueBoxFaceset = LoadFirstSprite("Assets/NinjaAdventure/Ui/Dialog/DialogBoxFaceset.png");
            manifest.ChoiceBox = LoadFirstSprite("Assets/NinjaAdventure/Ui/Dialog/ChoiceBox.png");
            manifest.DialogBox = LoadFirstSprite("Assets/NinjaAdventure/Ui/Dialog/DialogBox.png");
            manifest.DialogInfo = LoadFirstSprite("Assets/NinjaAdventure/Ui/Dialog/DialogInfo.png");
            manifest.FacesetBox = LoadFirstSprite("Assets/NinjaAdventure/Ui/Dialog/FacesetBox.png");
            manifest.YesButton = LoadFirstSprite("Assets/NinjaAdventure/Ui/Dialog/YesButton.png");
            manifest.NoButton = LoadFirstSprite("Assets/NinjaAdventure/Ui/Dialog/NoButton.png");
            manifest.Emotes = Enumerable.Range(1, 30)
                .Select(index => LoadFirstSprite("Assets/NinjaAdventure/Ui/Emote/emote" + index + ".png"))
                .ToArray();
            manifest.KeyCtrl = LoadFirstSprite("Assets/NinjaAdventure/Ui/Input/Keyboard/KeyCtrl.png");
            manifest.KeyC = LoadFirstSprite("Assets/NinjaAdventure/Ui/Input/Keyboard/KeyC.png");
            manifest.KeyV = LoadFirstSprite("Assets/NinjaAdventure/Ui/Input/Keyboard/KeyV.png");
            manifest.KeyZ = LoadFirstSprite("Assets/NinjaAdventure/Ui/Input/Keyboard/KeyZ.png");
            manifest.KeyF = LoadFirstSprite("Assets/NinjaAdventure/Ui/Input/Keyboard/KeyF.png");
            manifest.KeyA = LoadFirstSprite("Assets/NinjaAdventure/Ui/Input/Keyboard/KeyA.png");
            manifest.KeyK = LoadFirstSprite("Assets/NinjaAdventure/Ui/Input/Keyboard/KeyK.png");
            manifest.KeyR = LoadFirstSprite("Assets/NinjaAdventure/Ui/Input/Keyboard/KeyR.png");
            manifest.KeyW = LoadFirstSprite("Assets/NinjaAdventure/Ui/Input/Keyboard/KeyW.png");
            manifest.Key4 = LoadFirstSprite("Assets/NinjaAdventure/Ui/Input/Keyboard/Key4.png");
            manifest.Key5 = LoadFirstSprite("Assets/NinjaAdventure/Ui/Input/Keyboard/Key5.png");
            manifest.KeyDelete = LoadFirstSprite("Assets/NinjaAdventure/Ui/Input/Keyboard/KeyDelete.png");
            manifest.KeyEnter = LoadFirstSprite("Assets/NinjaAdventure/Ui/Input/Keyboard/KeyEnter.png");
            manifest.KeyEscape = LoadFirstSprite("Assets/NinjaAdventure/Ui/Input/Keyboard/KeyEscape.png");
            manifest.WoodBackgroundDark = LoadFirstSprite("Assets/NinjaAdventure/Ui/Theme/Theme Wood/nine_path_bg_2.png");
            manifest.WoodPanelAlt = LoadFirstSprite("Assets/NinjaAdventure/Ui/Theme/Theme Wood/nine_path_panel_2.png");
            manifest.WoodPanelLight = LoadFirstSprite("Assets/NinjaAdventure/Ui/Theme/Theme Wood/nine_path_panel_3.png");
            manifest.WoodPanelDisabled = LoadFirstSprite("Assets/NinjaAdventure/Ui/Theme/Theme Wood/nine_path_panel_disabled.png");
            manifest.WoodArrowLeft = LoadFirstSprite("Assets/NinjaAdventure/Ui/Theme/Theme Wood/arrow_left.png");
            manifest.WoodArrowLeftHover = LoadFirstSprite("Assets/NinjaAdventure/Ui/Theme/Theme Wood/arrow_left_hover.png");
            manifest.WoodArrowRight = LoadFirstSprite("Assets/NinjaAdventure/Ui/Theme/Theme Wood/arrow_right.png");
            manifest.WoodArrowRightHover = LoadFirstSprite("Assets/NinjaAdventure/Ui/Theme/Theme Wood/arrow_right_hover.png");
            manifest.WoodTab = LoadFirstSprite("Assets/NinjaAdventure/Ui/Theme/Theme Wood/tab.png");
            manifest.WoodTabHover = LoadFirstSprite("Assets/NinjaAdventure/Ui/Theme/Theme Wood/tab_hover.png");
            manifest.WoodTabSelected = LoadFirstSprite("Assets/NinjaAdventure/Ui/Theme/Theme Wood/tab_selected.png");
            manifest.WoodTabUnselected = LoadFirstSprite("Assets/NinjaAdventure/Ui/Theme/Theme Wood/tab_unselected.png");
            manifest.WoodButtonChecked = LoadFirstSprite("Assets/NinjaAdventure/Ui/Theme/Theme Wood/button_checked.png");
            manifest.WoodButtonUnchecked = LoadFirstSprite("Assets/NinjaAdventure/Ui/Theme/Theme Wood/button_unchecked.png");
            manifest.HeartIcon = LoadFirstSprite("Assets/NinjaAdventure/Ui/Receptacle/IconHeart.png");
            manifest.MoveIcon = LoadFirstSprite("Assets/NinjaAdventure/Ui/Skill Icon/Items & Weapon/Boot.png");
            manifest.MoveIconDisabled = LoadFirstSprite(
                "Assets/NinjaAdventure/Ui/Skill Icon/Items & Weapon/BootDisabled.png");
            manifest.CopyIcon = LoadFirstSprite("Assets/NinjaAdventure/Ui/Skill Icon/Items & Weapon/Scroll.png");
            manifest.CopyIconDisabled = LoadFirstSprite(
                "Assets/NinjaAdventure/Ui/Skill Icon/Items & Weapon/ScrollDisabled.png");
            manifest.DeleteIcon = LoadFirstSprite("Assets/NinjaAdventure/Ui/Skill Icon/Job & Action/Punch.png");
            manifest.DeleteIconDisabled = LoadFirstSprite(
                "Assets/NinjaAdventure/Ui/Skill Icon/Job & Action/PunchDisabled.png");
            manifest.InteractIcon = LoadFirstSprite(
                "Assets/NinjaAdventure/Ui/Skill Icon/Job & Action/Interact.png");
            manifest.ConnectIcon = LoadFirstSprite(
                "Assets/NinjaAdventure/Ui/Skill Icon/Spell/Permutation.png");
            manifest.RestoreIcon = LoadFirstSprite(
                "Assets/NinjaAdventure/Ui/Skill Icon/Job & Action/Repair.png");
            manifest.UndoIcon = LoadFirstSprite("Assets/NinjaAdventure/Items/Object/Hourglass.png");
            manifest.SearchIcon = LoadFirstSprite(
                "Assets/NinjaAdventure/Ui/Skill Icon/Spell/Vision.png");
            manifest.SelectAllIcon = LoadFirstSprite(
                "Assets/NinjaAdventure/Ui/Skill Icon/Spell/Explosion.png");
            manifest.GenericItemIcon = LoadFirstSprite(
                "Assets/NinjaAdventure/Ui/Skill Icon/Spell/BookLight.png");
            manifest.SalvageItemIcon = LoadFirstSprite(
                "Assets/NinjaAdventure/Ui/Skill Icon/Items & Weapon/Scroll.png");
            manifest.MaterialItemIcon = LoadFirstSprite(
                "Assets/NinjaAdventure/Ui/Skill Icon/Job & Action/Plant.png");
            manifest.ToolItemIcon = LoadFirstSprite(
                "Assets/NinjaAdventure/Ui/Skill Icon/Items & Weapon/Kunai.png");
            manifest.ConsumableItemIcon = LoadFirstSprite(
                "Assets/NinjaAdventure/Ui/Skill Icon/Job & Action/Potion.png");
            manifest.KeyItemIcon = LoadFirstSprite(
                "Assets/NinjaAdventure/Ui/Skill Icon/Items & Weapon/Amulet.png");
            manifest.PixelFont = AssetDatabase.LoadAssetAtPath<Font>(
                "Assets/KeyboardWanderer/Resources/Fonts/NeoDunggeunmoPro-Regular.ttf");

            manifest.AdventureMusic = LoadAudioClip("Assets/bgm/bgm_quest.ogg");
            manifest.VillageMusic = LoadAudioClip("Assets/bgm/bgm_main_wave2.ogg");
            manifest.BattleMusic = LoadAudioClip("Assets/bgm/bgm_boss_1.ogg");
            manifest.BugForestMusic = LoadAudioClip("Assets/bgm/bgm_main_wave1.ogg");
            manifest.BufferVillageMusic = LoadAudioClip("Assets/bgm/bgm_desert.ogg");
            manifest.DeadlockCityMusic = LoadAudioClip("Assets/bgm/bgm_deadlock.ogg");
            manifest.DataArchiveMusic = LoadAudioClip("Assets/bgm/bgm_archive.ogg");
            manifest.LegacyCitadelMusic = LoadAudioClip("Assets/bgm/bgm_legacy.ogg");
            manifest.RootSystemMusic = LoadAudioClip("Assets/bgm/bgm_root.ogg");
            manifest.BossMusic = LoadAudioClip("Assets/bgm/bgm_boss_1.ogg");
            manifest.FinalBossMusic = LoadAudioClip("Assets/bgm/bgm_main_wave4.ogg");
            manifest.GameOverMusic = LoadAudioClip("Assets/bgm/bgm_game_over.ogg");
            // 승리곡 파일은 아직 전달되지 않았다. 파일이 추가되면 빌더 소스 수정 없이
            // 이 선택 슬롯이 자동으로 채워지고, 그 전에는 런타임이 루트 테마를 유지한다.
            manifest.VictoryMusic = LoadOptionalAudioClip("Assets/bgm/bgm_victory.ogg");
            manifest.UiMoveSound = LoadAudioClip("Assets/NinjaAdventure/Audio/Sounds/Menu/Move1.wav");
            manifest.UiAcceptSound = LoadAudioClip("Assets/NinjaAdventure/Audio/Sounds/Menu/Accept.wav");
            manifest.UiCancelSound = LoadAudioClip("Assets/NinjaAdventure/Audio/Sounds/Menu/Cancel.wav");
            manifest.SlashSound = LoadAudioClip("Assets/NinjaAdventure/Audio/Sounds/Whoosh & Slash/Slash.wav");
            manifest.HitSound = LoadAudioClip("Assets/NinjaAdventure/Audio/Sounds/Hit & Impact/Hit1.wav");
            manifest.CoinSound = LoadAudioClip("Assets/NinjaAdventure/Audio/Sounds/Bonus/Coin.wav");
            manifest.SuccessJingle = LoadAudioClip("Assets/NinjaAdventure/Audio/Jingles/Success1.wav");

            manifest.CutsceneIntroFrames = Enumerable.Range(1, 8)
                .Select(index => LoadIllustrationSprite("Assets/cutscenes/cutscene-intro" + index + ".png"))
                .ToArray();
            // Only remember this build as successful if every path above actually resolved;
            // otherwise a transient/broken asset would be cached as "done" forever (it happened).
            if (_missingAssetPaths.Count == 0)
            {
                manifest.BuilderSourceHash = ComputeBuilderSourceHash();
            }
            else
            {
                manifest.BuilderSourceHash = string.Empty;
                Debug.LogWarning(
                    "Ninja Adventure manifest rebuilt with " + _missingAssetPaths.Count +
                    " unresolved asset path(s); it will retry on the next Editor load:\n- " +
                    string.Join("\n- ", _missingAssetPaths));
            }

            EditorUtility.SetDirty(manifest);
            AssetDatabase.SaveAssets();
        }

        private static void EnsureManifest()
        {
            NinjaAdventureAssetManifest manifest = AssetDatabase.LoadAssetAtPath<NinjaAdventureAssetManifest>(ManifestPath);
            if (manifest == null || manifest.BuilderSourceHash != ComputeBuilderSourceHash())
                RebuildManifest();
        }

        /// <summary>
        /// Hashes this file's own source so any edit here (a changed asset path, a new field)
        /// invalidates the cached manifest automatically, with no version constant to remember to bump.
        /// </summary>
        private static string ComputeBuilderSourceHash()
        {
            MonoScript script = AssetDatabase.LoadAssetAtPath<MonoScript>(BuilderScriptPath);
            if (script == null)
                throw new System.IO.FileNotFoundException("Manifest builder script not found: " + BuilderScriptPath);
            using SHA256 sha256 = SHA256.Create();
            byte[] hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(script.text));
            return System.Convert.ToBase64String(hash);
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
            Sprite sprite = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().FirstOrDefault();
            if (sprite == null) _missingAssetPaths.Add(path);
            return sprite;
        }

        /// <summary>
        /// Loads a sprite without forcing the pixel-art import settings that <see cref="ConfigurePixelTexture"/>
        /// applies (point filtering, uncompressed) — painted cutscene illustrations should keep their own settings.
        /// </summary>
        private static Sprite LoadIllustrationSprite(string path)
        {
            Sprite sprite = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().FirstOrDefault();
            if (sprite == null) _missingAssetPaths.Add(path);
            return sprite;
        }

        private static Texture2D LoadCodriaTileset(string sheetName)
        {
            return LoadPixelTexture(CodriaTilesetFolder + sheetName + ".png");
        }

        private static Texture2D LoadPixelTexture(string path)
        {
            ConfigurePixelTexture(path);
            Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (texture == null) _missingAssetPaths.Add(path);
            return texture;
        }

        private static AudioClip LoadAudioClip(string path)
        {
            AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            if (clip == null) _missingAssetPaths.Add(path);
            return clip;
        }

        private static AudioClip LoadOptionalAudioClip(string path)
        {
            return AssetDatabase.LoadAssetAtPath<AudioClip>(path);
        }

        private static ActorAnimationEntry Actor(string assetId, string sourcePath, string spriteName,
            string controllerPath)
        {
            string actorPath = "Assets/NinjaAdventure/Actor/" + sourcePath;
            string directory = actorPath.Substring(0, actorPath.LastIndexOf('/'))
                .Replace("/SeparateAnim", string.Empty).Replace("/Separate", string.Empty);
            return new ActorAnimationEntry
            {
                AssetId = assetId,
                DefaultSprite = LoadSprite(actorPath, spriteName),
                Portrait = LoadFirstSprite(directory + "/Faceset.png"),
                AnimatorController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(
                    "Assets/KeyboardWanderer/Animations/" + controllerPath)
            };
        }

        private static ElementalEffectEntry Effect(string effectId, string folder, string file, int frames)
        {
            return new ElementalEffectEntry
            {
                EffectId = effectId,
                SpriteSheet = LoadPixelTexture("Assets/NinjaAdventure/FX/Elemental/" + folder + "/" + file),
                FrameCount = frames,
                FramesPerSecond = 12f
            };
        }

        private static ActorAnimationEntry Boss(string assetId, string folder, string sourceFile,
            string spriteName)
        {
            return Actor(assetId, "Boss/" + folder + "/" + sourceFile, spriteName,
                "Boss/" + folder + "/" + folder + ".controller");
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

            Vector4 desiredBorder = path.EndsWith("/FacesetBox.png")
                ? new Vector4(8f, 8f, 8f, 8f)
                : path.EndsWith("/nine_path_focus.png")
                    ? new Vector4(4f, 4f, 4f, 4f)
                    : importer.spriteBorder;
            if (importer.spriteBorder != desiredBorder)
            {
                importer.spriteBorder = desiredBorder;
                changed = true;
            }

            if (changed)
                importer.SaveAndReimport();
        }
    }
}
