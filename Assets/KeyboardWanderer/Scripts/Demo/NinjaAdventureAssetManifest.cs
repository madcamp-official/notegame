using UnityEngine;

namespace KeyboardWanderer.Demo
{
    [CreateAssetMenu(fileName = "NinjaAdventureAssetManifest", menuName = "Keyboard Wanderer/Ninja Adventure Asset Manifest")]
    public sealed class NinjaAdventureAssetManifest : ScriptableObject
    {
        [Header("Tile atlas")]
        public Texture2D InteriorFloorAtlas;
        public Rect FloorRect = new Rect(192, 32, 16, 16);
        public Rect WallRect = new Rect(176, 96, 16, 16);
        public Rect HazardRect = new Rect(0, 32, 16, 16);

        [Header("Actors and props")]
        public Sprite PlayerIdle;
        public Sprite WardenIdle;
        public Sprite RuneBook;
        public Sprite Crate;
        public Sprite D20;

        [Header("Interface")]
        public Sprite MoveIcon;
        public Sprite CopyIcon;
        public Sprite DeleteIcon;
        public Font PixelFont;
    }
}
