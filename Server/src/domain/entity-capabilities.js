const REMOVABLE_FINALE_COMPONENTS = new Set(["freedom", "threat"]);

export function capabilitiesFor(entity) {
  if (!entity) return Object.freeze({ canCopy: false, canDelete: false, canConnect: false, canRestore: false, canInteract: false, requiredAdminAccess: 0, grantsDefeatReward: false });
  const active = entity.active !== false && !entity.state?.disabled && !entity.state?.defeated && !entity.state?.fled;
  const hostile = entity.kind === "enemy";
  const removableFinale = REMOVABLE_FINALE_COMPONENTS.has(entity.state?.finaleComponent);
  return {
    canCopy: active && Boolean(entity.cloneable) && !entity.protected && !["player", "npc"].includes(entity.kind),
    canDelete: active && ((hostile && !entity.state?.disabled) || removableFinale),
    // Encounters are conversations, not a combat-only gate. A hostile actor may
    // still be contacted; the authoritative relationship result decides whether
    // it listens, withdraws, or becomes more guarded.
    canConnect: active && entity.kind !== "prop",
    canRestore: !entity.protected && entity.kind !== "player",
    canInteract: active && !hostile && ["npc", "prop"].includes(entity.kind),
    requiredAdminAccess: removableFinale ? 3 : 0,
    grantsDefeatReward: hostile
  };
}
