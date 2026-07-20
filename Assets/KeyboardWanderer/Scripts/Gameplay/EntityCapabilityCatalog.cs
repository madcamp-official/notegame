using System;
using KeyboardWanderer.Core;

namespace KeyboardWanderer.Gameplay
{
    /// <summary>
    /// 엔티티 종류만으로 스킬 가능 여부를 추론하지 않도록, 게임 규칙에서 사용하는 능력을 한곳에 모은다.
    /// 저장 형식은 바꾸지 않고 기존 필드와 안정적인 asset ID로 capability를 계산한다.
    /// </summary>
    public static class EntityCapabilityCatalog
    {
        public const int RootSystemRequiredAdminAccess = 3;

        /// <summary>결말 퍼즐에서 Delete로 비활성화할 수 있는 시스템 노드인지 반환한다.</summary>
        public static bool IsRemovableRootComponent(string assetId)
        {
            return string.Equals(assetId, "finale.threat", StringComparison.Ordinal) ||
                   string.Equals(assetId, "finale.freedom", StringComparison.Ordinal);
        }

        /// <summary>현재 엔티티가 Delete의 합법적인 대상인지 반환한다.</summary>
        public static bool CanDelete(EntityKind kind, bool isHostile, string assetId)
        {
            return kind == EntityKind.Enemy && isHostile || IsRemovableRootComponent(assetId);
        }

        public static bool CanCopy(EntityKind kind, bool isProtected, bool isCloneable)
        {
            return isCloneable && !isProtected && kind != EntityKind.Player && kind != EntityKind.Npc;
        }

        public static bool CanConnect(bool isActive, bool isHostile)
        {
            return isActive && !isHostile;
        }

        public static bool CanRestore(EntityKind kind, bool isProtected, string assetId)
        {
            return !isProtected && kind != EntityKind.Player ||
                   IsAdministratorAccessCandidate(assetId);
        }

        public static bool CanInteract(EntityKind kind, bool isActive, bool isHostile)
        {
            return isActive && !isHostile && (kind == EntityKind.Npc || kind == EntityKind.Prop);
        }

        public static bool IsAdministratorAccessCandidate(string assetId)
        {
            return !string.IsNullOrEmpty(assetId) &&
                   assetId.StartsWith("story.admin-access", StringComparison.Ordinal);
        }

        public static EntityCapabilities Resolve(EntityKind kind, bool isActive, bool isHostile,
            bool isProtected, bool isCloneable, string assetId)
        {
            return new EntityCapabilities(
                CanCopy(kind, isProtected, isCloneable),
                CanDelete(kind, isHostile, assetId),
                CanConnect(isActive, isHostile),
                CanRestore(kind, isProtected, assetId),
                CanInteract(kind, isActive, isHostile),
                RequiredAdminAccessForDelete(assetId),
                GrantsDefeatReward(kind, isHostile));
        }

        /// <summary>대상을 조작하기 전에 필요한 관리자 권한 레벨을 반환한다.</summary>
        public static int RequiredAdminAccessForDelete(string assetId)
        {
            return IsRemovableRootComponent(assetId) ? RootSystemRequiredAdminAccess : 0;
        }

        /// <summary>처치 보상은 적에게만 주고 시스템 노드 비활성화에는 지급하지 않는다.</summary>
        public static bool GrantsDefeatReward(EntityKind kind, bool isHostile)
        {
            return kind == EntityKind.Enemy && isHostile;
        }
    }

    public readonly struct EntityCapabilities
    {
        public bool CanCopy { get; }
        public bool CanDelete { get; }
        public bool CanConnect { get; }
        public bool CanRestore { get; }
        public bool CanInteract { get; }
        public int RequiredAdminAccess { get; }
        public bool GrantsDefeatReward { get; }

        public EntityCapabilities(bool canCopy, bool canDelete, bool canConnect, bool canRestore,
            bool canInteract, int requiredAdminAccess, bool grantsDefeatReward)
        {
            CanCopy = canCopy;
            CanDelete = canDelete;
            CanConnect = canConnect;
            CanRestore = canRestore;
            CanInteract = canInteract;
            RequiredAdminAccess = requiredAdminAccess;
            GrantsDefeatReward = grantsDefeatReward;
        }
    }
}
