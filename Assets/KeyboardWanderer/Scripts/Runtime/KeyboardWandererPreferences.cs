using UnityEngine;

namespace KeyboardWanderer.Runtime
{
    /// <summary>
    /// 게임이 소유한 PlayerPrefs 접근점이다. 일반 실행에서는 전달받은 키를 그대로 사용한다.
    /// Editor 테스트는 별도 namespace를 활성화해 실제 플레이어 설정과 런 포인터를 건드리지 않는다.
    /// </summary>
    public static class KeyboardWandererPreferences
    {
#if UNITY_EDITOR
        private static string _editorNamespacePrefix = string.Empty;

        internal static void SetEditorNamespace(string prefix)
        {
            _editorNamespacePrefix = prefix ?? string.Empty;
        }

        internal static void ClearEditorNamespace()
        {
            _editorNamespacePrefix = string.Empty;
        }

        /// <summary>격리 회귀 테스트가 실제 값을 읽지 않고 해석 경계를 검증할 때만 사용한다.</summary>
        public static string ResolveKeyForEditorTest(string key) => ResolveKey(key);
#endif

        public static bool HasKey(string key) => PlayerPrefs.HasKey(ResolveKey(key));

        public static int GetInt(string key, int defaultValue = 0) =>
            PlayerPrefs.GetInt(ResolveKey(key), defaultValue);

        public static float GetFloat(string key, float defaultValue = 0f) =>
            PlayerPrefs.GetFloat(ResolveKey(key), defaultValue);

        public static string GetString(string key, string defaultValue = "") =>
            PlayerPrefs.GetString(ResolveKey(key), defaultValue);

        public static void SetInt(string key, int value) => PlayerPrefs.SetInt(ResolveKey(key), value);
        public static void SetFloat(string key, float value) => PlayerPrefs.SetFloat(ResolveKey(key), value);
        public static void SetString(string key, string value) => PlayerPrefs.SetString(ResolveKey(key), value);
        public static void DeleteKey(string key) => PlayerPrefs.DeleteKey(ResolveKey(key));
        public static void Save() => PlayerPrefs.Save();

        private static string ResolveKey(string key)
        {
#if UNITY_EDITOR
            return string.IsNullOrEmpty(_editorNamespacePrefix)
                ? key
                : _editorNamespacePrefix + key;
#else
            return key;
#endif
        }
    }
}
