using System.Linq;
using KeyboardWanderer.Demo;
using UnityEditor;
using UnityEngine;

namespace KeyboardWanderer.Editor
{
    public static class NinjaAdventureManifestBuilder
    {
        private const string ManifestPath = "Assets/KeyboardWanderer/Resources/NinjaAdventureAssetManifest.asset";

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

            manifest.InteriorFloorAtlas = AssetDatabase.LoadAssetAtPath<Texture2D>(
                "Assets/NinjaAdventure/Backgrounds/Tilesets/Interior/TilesetInteriorFloor.png");
            manifest.FloorRect = new Rect(192, 32, 16, 16);
            manifest.WallRect = new Rect(176, 96, 16, 16);
            manifest.HazardRect = new Rect(0, 32, 16, 16);
            manifest.PlayerIdle = LoadSprite("Assets/NinjaAdventure/Actor/Character/NinjaGreen/SeparateAnim/Idle.png", "Idle_1");
            manifest.WardenIdle = LoadSprite("Assets/NinjaAdventure/Actor/Character/Samurai/SeparateAnim/Idle.png", "Idle_1");
            manifest.RuneBook = LoadFirstSprite("Assets/NinjaAdventure/Items/Object/Book.png");
            manifest.Crate = LoadFirstSprite("Assets/NinjaAdventure/Items/Object/CrateEmpty.png");
            manifest.D20 = LoadFirstSprite("Assets/NinjaAdventure/Items/Object/Dice 20.png");
            manifest.MoveIcon = LoadFirstSprite("Assets/NinjaAdventure/Ui/Skill Icon/Items & Weapon/Boot.png");
            manifest.CopyIcon = LoadFirstSprite("Assets/NinjaAdventure/Ui/Skill Icon/Items & Weapon/Scroll.png");
            manifest.DeleteIcon = LoadFirstSprite("Assets/NinjaAdventure/Ui/Skill Icon/Spell/Fireball.png");
            manifest.PixelFont = AssetDatabase.LoadAssetAtPath<Font>("Assets/NinjaAdventure/Ui/Font/NormalFont.ttf");

            EditorUtility.SetDirty(manifest);
            AssetDatabase.SaveAssets();
        }

        private static void EnsureManifest()
        {
            if (AssetDatabase.LoadAssetAtPath<NinjaAdventureAssetManifest>(ManifestPath) == null)
                RebuildManifest();
        }

        private static Sprite LoadSprite(string path, string spriteName)
        {
            return AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().FirstOrDefault(sprite => sprite.name == spriteName)
                ?? LoadFirstSprite(path);
        }

        private static Sprite LoadFirstSprite(string path)
        {
            return AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().FirstOrDefault();
        }
    }
}
