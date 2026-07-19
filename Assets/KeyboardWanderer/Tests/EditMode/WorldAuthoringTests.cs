using KeyboardWanderer.Demo;
using KeyboardWanderer.Editor;
using KeyboardWanderer.Gameplay;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;

namespace KeyboardWanderer.Tests.EditMode
{
    public sealed class WorldAuthoringTests
    {
        [TearDown]
        public void TearDown() => KeyboardWandererWorldPreview.ClearPreview();

        [Test]
        public void SameSeed_ProducesSameLayoutAndPreviewDoesNotDirtyScene()
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Assert.That(scene.isDirty, Is.False);

            string first = KeyboardWandererWorldPreview.GeneratePreview(20260719L);
            Assert.That(scene.isDirty, Is.False);
            KeyboardWandererWorldPreview.ClearPreview();
            string second = KeyboardWandererWorldPreview.GeneratePreview(20260719L);

            Assert.That(second, Is.EqualTo(first));
            Assert.That(scene.isDirty, Is.False);
        }

        [Test]
        public void WorldRenderer_SkipsAnUnchangedLayoutHash()
        {
            var root = new GameObject("Renderer Test", typeof(Grid));
            var tileObject = new GameObject("Tilemap", typeof(Tilemap), typeof(TilemapRenderer));
            tileObject.transform.SetParent(root.transform);
            var texture = new Texture2D(1, 1);
            texture.SetPixel(0, 0, Color.white);
            texture.Apply();
            Sprite sprite = Sprite.Create(texture, new Rect(0, 0, 1, 1), Vector2.zero, 1f);
            var renderer = new WorldRenderer();
            RunView view = LocalTurnService.CreateDemo(8801L).CurrentView;

            Assert.That(renderer.Render(tileObject.GetComponent<Tilemap>(), view, null, sprite), Is.True);
            Assert.That(renderer.Render(tileObject.GetComponent<Tilemap>(), view, null, sprite), Is.False);
            Assert.That(renderer.RenderedLayoutHash, Is.EqualTo(view.Region.LayoutHash));

            renderer.Dispose();
            Object.DestroyImmediate(root);
            Object.DestroyImmediate(sprite);
            Object.DestroyImmediate(texture);
        }

        [Test]
        public void VisualProfile_ProvidesEditableBiomeDefaults()
        {
            var profile = ScriptableObject.CreateInstance<KeyboardWandererWorldVisualProfile>();
            profile.ConfigureDefaults();
            Assert.That(profile.Biomes.Length, Is.EqualTo(7));
            Assert.That(profile.TryGet("river_wetland", out KeyboardWandererWorldVisualProfile.BiomeVisual wetland), Is.True);
            Assert.That(wetland.DecorationDensity, Is.EqualTo(48));
            Object.DestroyImmediate(profile);
        }
    }
}
