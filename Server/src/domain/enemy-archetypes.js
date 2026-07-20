export function enemyArchetype(assetId, worldSeed = null, entityId = null) {
  const value = String(assetId || "").toLowerCase();
  if (value.includes("mushroom")) return "cache_replicator";
  if (value.includes("dragon") || value.includes("cyclope")) return "root_process";
  if (worldSeed !== null && entityId) {
    const bucket = stableHash(`${worldSeed}:${String(entityId).replaceAll("-", "").toLowerCase()}`) % 6;
    if (bucket === 0) return "cache_replicator";
    if (bucket === 1) return "root_process";
  }
  return "standard";
}

function stableHash(value) {
  let hash = 2166136261;
  for (let index = 0; index < value.length; index += 1) {
    hash ^= value.charCodeAt(index);
    hash = Math.imul(hash, 16777619) >>> 0;
  }
  return hash >>> 0;
}
