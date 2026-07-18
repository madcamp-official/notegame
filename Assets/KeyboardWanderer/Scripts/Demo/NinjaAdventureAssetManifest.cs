using UnityEngine;

namespace KeyboardWanderer.Demo
{
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
        public Rect OutdoorDirtRect = new Rect(16, 208, 16, 16);
        public Rect OutdoorGrassRect = new Rect(16, 160, 16, 16);
        public Rect OutdoorDarkGrassRect = new Rect(16, 112, 16, 16);

        [Header("Animated actor sheets")]
        public Texture2D PlayerIdleSheet;
        public Texture2D PlayerWalkSheet;
        public Texture2D PlayerAttackSheet;
        public Texture2D SlimeSheet;
        public Texture2D VillagerWalkSheet;
        public int PlayerFrameSize = 32;
        public int CreatureFrameSize = 16;

        [Header("Actors and props")]
        public Sprite PlayerIdle;
        public Sprite WardenIdle;
        public Sprite SlimeIdle;
        public Sprite VillagerIdle;
        public Sprite RuneBook;
        public Sprite Crate;
        public Sprite TreasureChest;
        public Sprite D20;

        [Header("Generated animator controllers")]
        public RuntimeAnimatorController PlayerAnimatorController;
        public RuntimeAnimatorController SlimeAnimatorController;
        public RuntimeAnimatorController VillagerAnimatorController;

        [Header("Interface")]
        public Sprite WoodPanel;
        public Sprite WoodBackground;
        public Sprite WoodButtonNormal;
        public Sprite WoodButtonHover;
        public Sprite WoodButtonPressed;
        public Sprite WoodButtonDisabled;
        public Sprite HeartIcon;
        public Sprite MoveIcon;
        public Sprite MoveIconDisabled;
        public Sprite CopyIcon;
        public Sprite CopyIconDisabled;
        public Sprite DeleteIcon;
        public Sprite DeleteIconDisabled;
        public Sprite InteractIcon;
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
