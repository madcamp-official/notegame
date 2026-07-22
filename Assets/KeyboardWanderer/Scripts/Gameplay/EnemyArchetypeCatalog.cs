using System;
using System.Collections.Generic;

namespace KeyboardWanderer.Gameplay
{
    public enum EnemyDependencyArchetype
    {
        Standard,
        CacheReplicator,
        RootProcess
    }

    public static class EnemyArchetypeCatalog
    {
        public static EnemyDependencyArchetype Resolve(string assetId)
        {
            string value = assetId ?? string.Empty;
            if (value.IndexOf("mushroom", StringComparison.OrdinalIgnoreCase) >= 0)
                return EnemyDependencyArchetype.CacheReplicator;
            if (value.IndexOf("dragon", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.IndexOf("cyclope", StringComparison.OrdinalIgnoreCase) >= 0)
                return EnemyDependencyArchetype.RootProcess;
            return EnemyDependencyArchetype.Standard;
        }

        /// <summary>
        /// 명시적인 몬스터 정체성은 유지하고, 일반 적만 런 시드와 엔티티 ID로 의존 규칙을 변형한다.
        /// 동일 시드·동일 ID는 저장/재개와 플랫폼에 관계없이 같은 결과를 낸다.
        /// </summary>
        public static EnemyDependencyArchetype Resolve(string assetId, long worldSeed, Guid entityId)
        {
            EnemyDependencyArchetype explicitArchetype = Resolve(assetId);
            if (explicitArchetype != EnemyDependencyArchetype.Standard)
                return explicitArchetype;
            uint hash = StableHash(worldSeed + ":" + entityId.ToString("N"));
            switch (hash % 6u)
            {
                case 0: return EnemyDependencyArchetype.CacheReplicator;
                case 1: return EnemyDependencyArchetype.RootProcess;
                default: return EnemyDependencyArchetype.Standard;
            }
        }

        public static string PlayerIntent(EnemyDependencyArchetype archetype)
        {
            switch (archetype)
            {
                case EnemyDependencyArchetype.CacheReplicator:
                    return "적 의도 · 조사 없이 삭제하면 캐시 복제본 생성";
                case EnemyDependencyArchetype.RootProcess:
                    return "적 의도 · Search로 의존성을 밝힌 뒤 Delete 가능";
                default:
                    return "적 의도 · 표준 프로세스";
            }
        }

        private static uint StableHash(string value)
        {
            uint hash = 2166136261u;
            for (int i = 0; i < value.Length; i++)
            {
                hash ^= value[i];
                hash = unchecked(hash * 16777619u);
            }
            return hash;
        }

        public static string RevealedFact(Guid entityId) => "DEPENDENCY_REVEALED:" + entityId.ToString("N");
        public static string ReplicatedFact(Guid entityId) => "CACHE_REPLICATED:" + entityId.ToString("N");

        /// <summary>
        /// Root Process의 Delete 선행 조건을 규칙 엔진과 presentation availability가 함께 사용한다.
        /// 화면과 권위 커밋이 서로 다른 조건으로 같은 대상을 판정하지 않게 한다.
        /// </summary>
        public static bool RequiresSearchBeforeDelete(string assetId, long worldSeed, Guid entityId,
            IReadOnlyList<string> canonicalFacts)
        {
            if (Resolve(assetId, worldSeed, entityId) != EnemyDependencyArchetype.RootProcess)
                return false;
            string revealedFact = RevealedFact(entityId);
            if (canonicalFacts != null)
                for (int i = 0; i < canonicalFacts.Count; i++)
                    if (string.Equals(canonicalFacts[i], revealedFact, StringComparison.Ordinal))
                        return false;
            return true;
        }
    }
}
