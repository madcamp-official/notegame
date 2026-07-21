using KeyboardWanderer.Demo;
using KeyboardWanderer.World;
using NUnit.Framework;
using UnityEngine;

namespace KeyboardWanderer.Tests.EditMode
{
    public sealed class DynamicMusicTests
    {
        private NinjaAdventureAssetManifest _manifest;

        [SetUp]
        public void SetUp()
        {
            _manifest = ScriptableObject.CreateInstance<NinjaAdventureAssetManifest>();
            _manifest.AdventureMusic = Clip("fallback");
            _manifest.BugForestMusic = Clip("bug");
            _manifest.BufferVillageMusic = Clip("buffer");
            _manifest.DeadlockCityMusic = Clip("deadlock");
            _manifest.DataArchiveMusic = Clip("archive");
            _manifest.LegacyCitadelMusic = Clip("legacy");
            _manifest.RootSystemMusic = Clip("root");
            _manifest.BossMusic = Clip("boss");
            _manifest.FinalBossMusic = Clip("final-boss");
            _manifest.GameOverMusic = Clip("game-over");
            _manifest.VictoryMusic = Clip("victory");
        }

        [TearDown]
        public void TearDown()
        {
            AudioClip[] clips = Resources.FindObjectsOfTypeAll<AudioClip>();
            for (int i = 0; i < clips.Length; i++)
                if (clips[i] != null && clips[i].name.StartsWith("kw-test-"))
                    Object.DestroyImmediate(clips[i]);
            Object.DestroyImmediate(_manifest);
        }

        [TestCase("temperate_forest_field", "kw-test-bug")]
        [TestCase("arid_desert", "kw-test-buffer")]
        [TestCase("ancient_ruins", "kw-test-deadlock")]
        [TestCase("subterranean_cavern", "kw-test-archive")]
        [TestCase("frost_highland", "kw-test-legacy")]
        [TestCase("root_system", "kw-test-root")]
        public void Resolve_MapsBiomeToItsTheme(string biomeId, string expectedName)
        {
            AudioClip result = KeyboardWandererMusicDirector.Resolve(_manifest,
                new KeyboardWandererMusicDirector.Context(biomeId));
            Assert.That(result.name, Is.EqualTo(expectedName));
        }

        [Test]
        public void Resolve_AppliesStatePriorityBeforeBiome()
        {
            Assert.That(Resolve(boss: true), Is.SameAs(_manifest.BossMusic));
            Assert.That(Resolve(boss: true, finalBoss: true), Is.SameAs(_manifest.FinalBossMusic));
            Assert.That(Resolve(boss: true, finalBoss: true, cleared: true), Is.SameAs(_manifest.VictoryMusic));
            Assert.That(Resolve(boss: true, finalBoss: true, cleared: true, gameOver: true),
                Is.SameAs(_manifest.GameOverMusic));
        }

        [Test]
        public void Resolve_UsesRootThemeUntilVictoryAssetIsAdded()
        {
            _manifest.VictoryMusic = null;
            Assert.That(Resolve(cleared: true), Is.SameAs(_manifest.RootSystemMusic));
        }

        private AudioClip Resolve(bool boss = false, bool finalBoss = false, bool gameOver = false,
            bool cleared = false)
        {
            return KeyboardWandererMusicDirector.Resolve(_manifest,
                new KeyboardWandererMusicDirector.Context("temperate_forest_field", boss, finalBoss,
                    gameOver, cleared));
        }

        private static AudioClip Clip(string name)
            => AudioClip.Create("kw-test-" + name, 8, 1, 8000, false);
    }
}
