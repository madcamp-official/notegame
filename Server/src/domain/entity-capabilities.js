const REMOVABLE_FINALE_COMPONENTS = new Set(["freedom", "threat"]);

export function capabilitiesFor(entity) {
  if (!entity) return Object.freeze({ canCopy: false, canDelete: false, canConnect: false, canRestore: false, canInteract: false, requiredAdminAccess: 0, grantsDefeatReward: false });
  const active = entity.active !== false && !entity.state?.disabled && !entity.state?.defeated && !entity.state?.fled;
  const hostile = entity.kind === "enemy";
  const finaleComponent = typeof entity.state?.finaleComponent === "string" && entity.state.finaleComponent.length > 0;
  const removableFinale = REMOVABLE_FINALE_COMPONENTS.has(entity.state?.finaleComponent);
  return {
    canCopy: active && Boolean(entity.cloneable) && !entity.protected && !["player", "npc"].includes(entity.kind),
    canDelete: active && ((hostile && !entity.state?.disabled) || removableFinale),
    // Ordinary props are not relationship endpoints. Finale components are the
    // intentional exception because every non-emergency ending recipe links
    // component props (or a component and the player); prepare() still enforces
    // the Root gate, physical finale location, and puzzle-only endpoints.
    canConnect: active && (entity.kind !== "prop" || finaleComponent),
    canRestore: !entity.protected && entity.kind !== "player",
    canInteract: active && !hostile && ["npc", "prop"].includes(entity.kind),
    requiredAdminAccess: removableFinale ? 3 : 0,
    grantsDefeatReward: hostile
  };
}
