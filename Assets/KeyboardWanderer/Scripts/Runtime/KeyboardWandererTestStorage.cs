#if UNITY_EDITOR
using System;
using System.IO;
using KeyboardWanderer.Gameplay;
using UnityEngine;

namespace KeyboardWanderer.Runtime
{
    /// <summary>
    /// Unity Test Framework가 실제 PlayerPrefs와 persistentDataPath 저장 파일을 읽거나
    /// 변경하지 않도록 두 저장소를 한 번에 격리한다. Player 빌드에는 포함되지 않는다.
    /// </summary>
    public static class KeyboardWandererTestStorage
    {
        private const string SessionDirectoryKey = "keyboard-wanderer.test-storage.directory";
        private const string SessionPreferencePrefixKey = "keyboard-wanderer.test-storage.preference-prefix";
        private static string _activeDirectory = string.Empty;

        public static bool IsActive => !string.IsNullOrEmpty(_activeDirectory);
        public static string ActiveDirectory => _activeDirectory;

        public static void Begin(string suiteName)
        {
            if (string.IsNullOrWhiteSpace(suiteName))
                throw new ArgumentException("Test storage suite name is required.", nameof(suiteName));

            // A previous aborted test run may not have reached OneTimeTearDown. Replacing
            // that abandoned test-only scope is safe; neither scope can resolve real keys.
            string token = Sanitize(suiteName) + "-" + Guid.NewGuid().ToString("N");
            _activeDirectory = Path.Combine(
                Application.temporaryCachePath, "KeyboardWandererTests", token);
            Directory.CreateDirectory(_activeDirectory);
            string preferencePrefix = "keyboard-wanderer.test." + token + ".";
            UnityEditor.SessionState.SetString(SessionDirectoryKey, _activeDirectory);
            UnityEditor.SessionState.SetString(SessionPreferencePrefixKey, preferencePrefix);
            Activate(_activeDirectory, preferencePrefix);
        }

        public static void End()
        {
            // Stop resolving test calls first only after all test-owned behaviours have
            // flushed. Fixtures perform that flush before invoking End().
            LocalRunSaveService.ClearEditorSaveDirectory();
            KeyboardWandererPreferences.ClearEditorNamespace();
            UnityEditor.SessionState.EraseString(SessionDirectoryKey);
            UnityEditor.SessionState.EraseString(SessionPreferencePrefixKey);
            _activeDirectory = string.Empty;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void RestoreAfterDomainReload()
        {
            string directory = UnityEditor.SessionState.GetString(SessionDirectoryKey, string.Empty);
            string preferencePrefix = UnityEditor.SessionState.GetString(SessionPreferencePrefixKey, string.Empty);
            if (!string.IsNullOrWhiteSpace(directory) && !string.IsNullOrWhiteSpace(preferencePrefix))
                Activate(directory, preferencePrefix);
        }

        private static void Activate(string directory, string preferencePrefix)
        {
            _activeDirectory = Path.GetFullPath(directory);
            Directory.CreateDirectory(_activeDirectory);
            KeyboardWandererPreferences.SetEditorNamespace(preferencePrefix);
            LocalRunSaveService.SetEditorSaveDirectory(_activeDirectory);
        }

        private static string Sanitize(string value)
        {
            char[] characters = value.Trim().ToCharArray();
            for (int i = 0; i < characters.Length; i++)
                if (!char.IsLetterOrDigit(characters[i]) && characters[i] != '-' && characters[i] != '_')
                    characters[i] = '-';
            return new string(characters);
        }
    }
}
#endif
