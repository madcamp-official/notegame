using System;
using UnityEngine;

namespace KeyboardWanderer.Demo
{
    [Serializable]
    public sealed class ActorAnimationEntry
    {
        public string AssetId;
        public Sprite DefaultSprite;
        public RuntimeAnimatorController AnimatorController;
    }

    [CreateAssetMenu(fileName = "NinjaAdventureAssetManifest", menuName = "Codria/Ninja Adventure Asset Manifest")]
    public sealed class NinjaAdventureAssetManifest : ScriptableObject
    {
        [HideInInspector]
        public int BuilderVersion;

        [Header("Tile atlas")]
        public Texture2D InteriorFloorAtlas;
        public Rect FloorRect = new Rect(192, 32, 16, 16);
        public Rect WallRect = new Rect(176, 96, 16, 16);
        public Rect HazardRect = new Rect(0, 32, 16, 16);

        [Header("Outdoor tile atlases")]
        public Texture2D OutdoorFieldAtlas;
        public Texture2D WaterAtlas;
        public Texture2D NatureAtlas;
        public Texture2D HouseAtlas;
        public Texture2D DesertAtlas;
        public Texture2D DungeonAtlas;
        public Texture2D AbandonedVillageAtlas;
        public Texture2D WatermillAtlas;
        public Rect OutdoorDirtRect = new Rect(16, 208, 16, 16);
        public Rect OutdoorGrassRect = new Rect(16, 160, 16, 16);
        public Rect OutdoorDarkGrassRect = new Rect(16, 112, 16, 16);

        // 코드리아 지역 타일셋. 바닥 시트는 4×4 오토타일 블롭이며 타일 인덱스가 곧
        // 이웃 비트마스크(N=1, E=2, S=4, W=8)다. 4×5, 4×6 시트는 오토타일 16칸 뒤에
        // 꽉 찬 장식 변형 행이 이어진다.
        [Header("Codria terrain tilesets (16px autotile blobs)")]
        public Texture2D GeneralFieldTiles;
        public Texture2D GeneralWaterTiles;
        public Texture2D GeneralHighGroundTiles;
        public Texture2D GeneralSlopeTiles;
        public Texture2D BugForestGroundTiles;
        public Texture2D BugForestCampTiles;
        public Texture2D BugForestHoleTiles;
        public Texture2D BugForestLakeTiles;
        public Texture2D BufferVillageGroundTiles;
        public Texture2D DeadlockCityGroundTiles;
        public Texture2D DeadlockCityVirusTiles;
        public Texture2D DataArchiveRockTiles;
        public Texture2D DataArchiveCrystalFloorTiles;
        public Texture2D DataArchiveWoodPlankTiles;
        public Texture2D LegacyCitadelSnowTiles;
        public Texture2D LegacyCitadelIceTiles;
        public Texture2D RootSystemGroundTiles;
        public int CodriaTileSize = 16;

        [Header("Codria region props (free-form sprites)")]
        public Texture2D BugForestDataTreeProps;
        public Texture2D BugForestRockProps;
        public Texture2D BugForestCampProps;
        public Texture2D BufferVillageBuildingProps;
        public Texture2D BufferVillageFenceProps;
        public Texture2D DeadlockCityBuildingProps;
        public Texture2D DataArchiveBookshelfProps;
        public Texture2D DataArchiveCrystalProps;
        public Texture2D LegacyCitadelEdificeProps;
        public Texture2D LegacyCitadelGooseProps;
        public Texture2D RootSystemServerProps;
        public Texture2D RootSystemMonitorProps;

        [Header("Animated actor sheets")]
        public Texture2D PlayerIdleSheet;
        public Texture2D PlayerWalkSheet;
        public Texture2D PlayerAttackSheet;
        public Texture2D SlimeSheet;
        public Texture2D VillagerWalkSheet;
        public int PlayerFrameSize = 32;
        public int CreatureFrameSize = 16;

        [Header("Neopjuki player")]
        public Texture2D NeopjukiAtlas;
        public int NeopjukiCellWidth = 192;
        public int NeopjukiCellHeight = 208;

        [Header("Actors and props")]
        public Sprite PlayerIdle;
        public Sprite WardenIdle;
        public Sprite SlimeIdle;
        public Sprite VillagerIdle;
        public Sprite RuneBook;
        public Sprite Crate;
        public Sprite TreasureChest;
        public Sprite D20;

        [Header("Animator controllers")]
        public RuntimeAnimatorController PlayerAnimatorController;
        public RuntimeAnimatorController SlimeAnimatorController;
        public RuntimeAnimatorController VillagerAnimatorController;

        [Header("Actor animation catalog")]
        public ActorAnimationEntry[] NpcAnimations = Array.Empty<ActorAnimationEntry>();
        public ActorAnimationEntry[] MonsterAnimations = Array.Empty<ActorAnimationEntry>();
        public ActorAnimationEntry[] BossAnimations = Array.Empty<ActorAnimationEntry>();

        [Header("Interface")]
        public Sprite WoodPanel;
        public Sprite WoodBackground;
        public Sprite WoodButtonNormal;
        public Sprite WoodButtonHover;
        public Sprite WoodButtonPressed;
        public Sprite WoodButtonDisabled;
        public Sprite WoodPanelInterior;
        public Sprite WoodPanelFocus;
        public Sprite WoodInventoryCell;
        public Sprite WoodSliderProgress;
        public Sprite WoodSliderGrabber;
        public Sprite WoodChecked;
        public Sprite WoodUnchecked;
        public Sprite DialogueBox;
        public Sprite DialogueBoxFaceset;
        public Sprite ChoiceBox;
        public Sprite DialogBox;
        public Sprite DialogInfo;
        public Sprite FacesetBox;
        public Sprite YesButton;
        public Sprite NoButton;
        public Sprite[] Emotes = Array.Empty<Sprite>();
        public Sprite KeyCtrl;
        public Sprite KeyC;
        public Sprite KeyV;
        public Sprite KeyZ;
        public Sprite KeyF;
        public Sprite KeyA;
        public Sprite KeyK;
        public Sprite KeyR;
        public Sprite KeyW;
        public Sprite Key4;
        public Sprite Key5;
        public Sprite KeyDelete;
        public Sprite KeyEnter;
        public Sprite KeyEscape;
        public Sprite WoodBackgroundDark;
        public Sprite WoodPanelAlt;
        public Sprite WoodPanelLight;
        public Sprite WoodPanelDisabled;
        public Sprite WoodArrowLeft;
        public Sprite WoodArrowLeftHover;
        public Sprite WoodArrowRight;
        public Sprite WoodArrowRightHover;
        public Sprite WoodTab;
        public Sprite WoodTabHover;
        public Sprite WoodTabSelected;
        public Sprite WoodTabUnselected;
        public Sprite WoodButtonChecked;
        public Sprite WoodButtonUnchecked;
        public Sprite HeartIcon;
        public Sprite MoveIcon;
        public Sprite MoveIconDisabled;
        public Sprite CopyIcon;
        public Sprite CopyIconDisabled;
        public Sprite DeleteIcon;
        public Sprite DeleteIconDisabled;
        public Sprite InteractIcon;
        public Sprite ConnectIcon;
        public Sprite RestoreIcon;
        public Sprite UndoIcon;
        public Sprite SearchIcon;
        public Sprite SelectAllIcon;
        public Font PixelFont;

        [Header("Music")]
        public AudioClip AdventureMusic;
        public AudioClip VillageMusic;
        public AudioClip BattleMusic;

        [Header("Sound effects")]
        public AudioClip UiMoveSound;
        public AudioClip UiAcceptSound;
        public AudioClip UiCancelSound;
        public AudioClip SlashSound;
        public AudioClip HitSound;
        public AudioClip CoinSound;
        public AudioClip SuccessJingle;
    }
}
