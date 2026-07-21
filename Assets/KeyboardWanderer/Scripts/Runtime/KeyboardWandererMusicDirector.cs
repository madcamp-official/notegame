using KeyboardWanderer.World;
using UnityEngine;

namespace KeyboardWanderer.Demo
{
    /// <summary>
    /// 월드 상태를 하나의 BGM으로 결정한다. 종료와 보스 상태는 현재 바이옴보다 우선한다.
    /// </summary>
    public static class KeyboardWandererMusicDirector
    {
        public readonly struct Context
        {
            public readonly string BiomeId;
            public readonly bool BossBattle;
            public readonly bool FinalBossBattle;
            public readonly bool GameOver;
            public readonly bool Cleared;

            public Context(string biomeId, bool bossBattle = false, bool finalBossBattle = false,
                bool gameOver = false, bool cleared = false)
            {
                BiomeId = biomeId ?? string.Empty;
                BossBattle = bossBattle;
                FinalBossBattle = finalBossBattle;
                GameOver = gameOver;
                Cleared = cleared;
            }
        }

        public static AudioClip Resolve(NinjaAdventureAssetManifest manifest, Context context)
        {
            if (manifest == null) return null;
            if (context.GameOver)
                return manifest.GameOverMusic ?? manifest.AdventureMusic;
            if (context.Cleared)
                return manifest.VictoryMusic ?? manifest.RootSystemMusic ?? manifest.AdventureMusic;
            if (context.FinalBossBattle)
                return manifest.FinalBossMusic ?? manifest.BossMusic ?? manifest.BattleMusic;
            if (context.BossBattle)
                return manifest.BossMusic ?? manifest.BattleMusic;

            switch ((context.BiomeId ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "temperate_forest_field": return manifest.BugForestMusic ?? manifest.AdventureMusic;
                case "arid_desert": return manifest.BufferVillageMusic ?? manifest.AdventureMusic;
                case "ancient_ruins": return manifest.DeadlockCityMusic ?? manifest.AdventureMusic;
                case "subterranean_cavern": return manifest.DataArchiveMusic ?? manifest.AdventureMusic;
                case "frost_highland": return manifest.LegacyCitadelMusic ?? manifest.AdventureMusic;
                case "root_system": return manifest.RootSystemMusic ?? manifest.VillageMusic ?? manifest.AdventureMusic;
                default: return manifest.AdventureMusic ?? manifest.VillageMusic;
            }
        }
    }
}
