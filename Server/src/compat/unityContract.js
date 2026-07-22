/**
 * Adds aliases expected by older Unity builds without removing the canonical server fields.
 * The walk is cycle-safe and mutates only plain response objects/arrays passed to it.
 */
export function addUnityContractAliases(value, seen = new WeakSet()) {
  if (value === null || typeof value !== "object") return value;
  if (seen.has(value)) return value;
  seen.add(value);
  if (Array.isArray(value)) {
    for (const item of value) addUnityContractAliases(item, seen);
    return value;
  }

  if (Object.hasOwn(value, "progressLevel") && !Object.hasOwn(value, "adminLevel")) {
    value.adminLevel = value.progressLevel;
  }
  if (Object.hasOwn(value, "progressTokens") && !Object.hasOwn(value, "accessTokens")) {
    value.accessTokens = value.progressTokens;
  }
  if (Object.hasOwn(value, "rootSystemGate") && !Object.hasOwn(value, "rootGate")) {
    value.rootGate = value.rootSystemGate;
  }
  for (const [key, item] of Object.entries(value)) {
    if (key.startsWith("finale") && key.length > "finale".length) {
      const alias = `root${key.slice("finale".length)}`;
      if (!Object.hasOwn(value, alias)) value[alias] = item;
    }
    addUnityContractAliases(item, seen);
  }
  return value;
}
