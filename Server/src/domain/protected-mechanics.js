const PROTECTED_ASCII_TOKENS = new Set([
  "progress", "milestone", "token", "metric", "metrics",
  "stability", "autonomy", "trust", "finale", "ending", "geometry", "layout", "map",
  "path", "coordinate", "coordinates", "position", "dice", "d20", "roll", "damage",
  "health", "hp", "focus", "reward", "turn", "version"
]);

const PROTECTED_MULTIWORD = /\b(?:technical[\s._-]*debt|world[\s._-]*stability|world[\s._-]*autonomy|public[\s._-]*trust|companion[\s._-]*bond|turn[\s._-]*pressure|entity[\s._-]*(?:position|state)|milestone[\s._-]*token|progress[\s._-]*level)\b/i;
const PROTECTED_KOREAN = /(?:진행\s*(?:레벨|단계)|이정표\s*(?:조각|토큰|증표)|지표|세계\s*안정성|세계\s*자율성|공공\s*신뢰|기술\s*부채|동료\s*유대|턴\s*압박|피날레|결말|지형|지도|경로|좌표|위치|주사위|판정|피해|체력|집중|보상)/i;

export function containsProtectedFactReference(value) {
  const text = String(value ?? "");
  if (PROTECTED_MULTIWORD.test(text) || PROTECTED_KOREAN.test(text)) return true;
  const tokens = text.toLowerCase().match(/[a-z0-9]+/g) || [];
  return tokens.some((token) => PROTECTED_ASCII_TOKENS.has(token));
}
