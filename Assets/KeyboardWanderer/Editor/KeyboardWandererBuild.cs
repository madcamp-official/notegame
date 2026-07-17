using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace KeyboardWanderer.Editor
{
    public static class KeyboardWandererBuild
    {
        private const string OutputPath = "Builds/KeyboardWanderer.app";

        [MenuItem("Keyboard Wanderer/Build macOS Player")]
        public static void BuildMacOS()
        {
            string[] scenes = EditorBuildSettings.scenes
                .Where(scene => scene.enabled)
                .Select(scene => scene.path)
                .ToArray();
            if (scenes.Length == 0)
                throw new InvalidOperationException("At least one enabled build scene is required.");

            BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = OutputPath,
                target = BuildTarget.StandaloneOSX,
                options = BuildOptions.CleanBuildCache
            });
            if (report.summary.result != BuildResult.Succeeded)
                throw new InvalidOperationException($"macOS build failed: {report.summary.result}");

            Debug.Log($"Keyboard Wanderer macOS build complete: {OutputPath} ({report.summary.totalSize} bytes)");
        }
    }
}
