using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace KeyboardWanderer.Editor.Validation
{
    public static class UnityAssetIntegrityValidator
    {
        [MenuItem("Keyboard Wanderer/Validate Project Assets")]
        public static void ValidateProjectAssets()
        {
            IReadOnlyList<string> problems = CollectProblems(Application.dataPath);
            if (problems.Count == 0)
            {
                Debug.Log("[Keyboard Wanderer] Asset integrity validation passed.");
                return;
            }

            foreach (string problem in problems)
                Debug.LogError("[Keyboard Wanderer] " + problem);
            throw new InvalidOperationException($"Asset integrity validation failed with {problems.Count} problem(s).");
        }

        public static IReadOnlyList<string> CollectProblems(string assetsPath)
        {
            var problems = new List<string>();
            var guidOwners = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (string entry in Directory.EnumerateFileSystemEntries(assetsPath, "*", SearchOption.AllDirectories))
            {
                if (IsHidden(entry, assetsPath))
                    continue;

                if (!entry.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                {
                    if (!File.Exists(entry + ".meta"))
                        problems.Add("Missing meta: " + Relative(entry, assetsPath));
                    continue;
                }

                string assetPath = entry.Substring(0, entry.Length - ".meta".Length);
                if (!File.Exists(assetPath) && !Directory.Exists(assetPath))
                    problems.Add("Orphan meta: " + Relative(entry, assetsPath));

                string guid = File.ReadLines(entry)
                    .FirstOrDefault(line => line.StartsWith("guid: ", StringComparison.Ordinal))
                    ?.Substring("guid: ".Length).Trim();
                if (string.IsNullOrWhiteSpace(guid))
                {
                    problems.Add("Meta has no GUID: " + Relative(entry, assetsPath));
                    continue;
                }

                string relative = Relative(entry, assetsPath);
                if (guidOwners.TryGetValue(guid, out string owner))
                    problems.Add($"Duplicate GUID {guid}: {owner} and {relative}");
                else
                    guidOwners.Add(guid, relative);
            }

            problems.Sort(StringComparer.Ordinal);
            return problems;
        }

        private static bool IsHidden(string path, string assetsPath)
        {
            string relative = Path.GetRelativePath(assetsPath, path);
            return relative.Split(Path.DirectorySeparatorChar)
                .Any(segment => segment.StartsWith(".", StringComparison.Ordinal));
        }

        private static string Relative(string path, string assetsPath)
        {
            return "Assets/" + Path.GetRelativePath(assetsPath, path).Replace('\\', '/');
        }
    }
}
