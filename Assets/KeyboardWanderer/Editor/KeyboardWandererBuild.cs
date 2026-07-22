using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using Process = System.Diagnostics.Process;
using ProcessStartInfo = System.Diagnostics.ProcessStartInfo;

namespace KeyboardWanderer.Editor
{
    public static class KeyboardWandererBuild
    {
        public const string MacOutputPath = "Builds/NinjaAdventure.app";
        public const string WindowsOutputPath =
            "Builds/NinjaAdventure-Windows/NUPJUK-The-Last-Commit.exe";
        public const string PortableProductFileName = "NUPJUK-The-Last-Commit";

        private const string AppIconPath =
            "Assets/KeyboardWanderer/Art/AppIcon/NinjaAdventureAppIcon.png";
        private const string FontLicensePath =
            "Assets/KeyboardWanderer/Resources/Fonts/NeoDunggeunmoPro-LICENSE.txt";
        private const string TmpFontLicensePath =
            "Assets/TextMesh Pro/Fonts/LiberationSans - OFL.txt";
        private const string ArtLicensePath = "Assets/NinjaAdventure/LICENSE.txt";
        private const string ThirdPartyNoticesPath =
            "BuildSupport/THIRD-PARTY-NOTICES.md";
        private const string SigningScriptPath =
            "BuildSupport/Sign-and-Verify-macOS.command";
        private const string ProductName = "NUPJUK : The Last Commit";
        private const string BundleIdentifier = "com.madcamp.ninjaadventure";
        private const string ReleaseVersion = "1.0.0";
        private const string ReleaseBuildNumber = "1";

        [MenuItem("Keyboard Wanderer/Build macOS Player")]
        public static void BuildMacOS()
        {
            BuildDesktopPlayer(BuildTarget.StandaloneOSX, MacOutputPath);
        }

        [MenuItem("Keyboard Wanderer/Build Windows x64 Player")]
        public static void BuildWindows()
        {
            BuildDesktopPlayer(BuildTarget.StandaloneWindows64, WindowsOutputPath);
        }

        /// <summary>
        /// Shared release entry point used by menu and distribution builds. The caller
        /// must start Unity with the matching -buildTarget. Building two platforms in
        /// one process can retain the first platform's UNITY_STANDALONE_* symbols.
        /// </summary>
        public static BuildReport BuildDesktopPlayer(BuildTarget buildTarget, string outputPath)
        {
            if (string.IsNullOrWhiteSpace(outputPath))
                throw new ArgumentException("A desktop player output path is required.", nameof(outputPath));

            EnsureTargetReady(buildTarget);
            ConfigureReleasePlayerSettings(buildTarget);

            string[] scenes = EditorBuildSettings.scenes
                .Where(scene => scene.enabled)
                .Select(scene => scene.path)
                .ToArray();
            if (scenes.Length == 0)
                throw new InvalidOperationException("At least one enabled build scene is required.");

            string outputDirectory = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrWhiteSpace(outputDirectory))
                Directory.CreateDirectory(outputDirectory);

            BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = outputPath,
                target = buildTarget,
                targetGroup = BuildTargetGroup.Standalone,
                options = BuildOptions.CleanBuildCache | BuildOptions.StrictMode
            });
            if (report.summary.result != BuildResult.Succeeded)
                throw new BuildFailedException($"{buildTarget} build failed: {report.summary.result}");

            InstallThirdPartyNotices(outputPath, buildTarget);
            if (buildTarget == BuildTarget.StandaloneOSX)
                SignAndVerifyLocalBuild(outputPath);
            Debug.Log($"Keyboard Wanderer {buildTarget} build complete: {outputPath} " +
                      $"({report.summary.totalSize} bytes)");
            return report;
        }

        private static void EnsureTargetReady(BuildTarget buildTarget)
        {
            BuildTargetGroup group = BuildPipeline.GetBuildTargetGroup(buildTarget);
            if (!BuildPipeline.IsBuildTargetSupported(group, buildTarget))
            {
                string module = buildTarget == BuildTarget.StandaloneWindows64
                    ? "Windows Build Support (Mono), module id windows-mono"
                    : "Mac Build Support";
                throw new BuildFailedException(
                    $"{buildTarget} is not installed. Add the Unity module: {module}.");
            }

            if (EditorUserBuildSettings.activeBuildTarget != buildTarget)
            {
                throw new BuildFailedException(
                    $"Active target is {EditorUserBuildSettings.activeBuildTarget}, not {buildTarget}. " +
                    $"Start a separate Unity process with -buildTarget {buildTarget} so platform " +
                    "compiler symbols are reloaded before the build.");
            }
        }

        /// <summary>
        /// Keeps command-line, menu, macOS, and Windows builds on one release identity.
        /// macOS remains Universal while Windows is an x64 Mono player so it can be
        /// cross-built from the team's macOS build host.
        /// </summary>
        private static void ConfigureReleasePlayerSettings(BuildTarget buildTarget)
        {
            Texture2D icon = AssetDatabase.LoadAssetAtPath<Texture2D>(AppIconPath);
            if (icon == null)
                throw new InvalidOperationException($"Release app icon is missing: {AppIconPath}");

            NamedBuildTarget namedTarget = NamedBuildTarget.Standalone;
            int[] iconSizes = PlayerSettings.GetIconSizes(namedTarget, IconKind.Application);
            if (iconSizes == null || iconSizes.Length == 0)
                throw new InvalidOperationException("Unity reported no standalone application icon slots.");

            PlayerSettings.SetIcons(namedTarget,
                Enumerable.Repeat(icon, iconSizes.Length).ToArray(), IconKind.Application);
#if UNITY_EDITOR_OSX
            if (buildTarget == BuildTarget.StandaloneOSX)
                UnityEditor.OSXStandalone.UserBuildSettings.architecture = OSArchitecture.x64ARM64;
#endif
            PlayerSettings.SetScriptingBackend(namedTarget, ScriptingImplementation.Mono2x);
            PlayerSettings.productName = ProductName;
            PlayerSettings.bundleVersion = ReleaseVersion;
            PlayerSettings.SetApplicationIdentifier(namedTarget, BundleIdentifier);
#if UNITY_EDITOR_OSX
            if (buildTarget == BuildTarget.StandaloneOSX)
                PlayerSettings.macOS.buildNumber = ReleaseBuildNumber;
#endif
            AssetDatabase.SaveAssets();
        }

        private static void InstallThirdPartyNotices(string outputPath, BuildTarget buildTarget)
        {
            string noticesDirectory = buildTarget == BuildTarget.StandaloneOSX
                ? Path.Combine(outputPath, "Contents", "Resources", "ThirdPartyLicenses")
                : Path.Combine(Path.GetDirectoryName(outputPath) ?? string.Empty,
                    "ThirdPartyLicenses");
            Directory.CreateDirectory(noticesDirectory);
            CopyNotice(FontLicensePath, noticesDirectory);
            CopyNotice(TmpFontLicensePath, noticesDirectory);
            CopyNotice(ArtLicensePath, noticesDirectory);
            CopyNotice(ThirdPartyNoticesPath, noticesDirectory);
        }

        private static void CopyNotice(string sourcePath, string destinationDirectory)
        {
            if (!File.Exists(sourcePath))
                throw new FileNotFoundException("Required third-party notice is missing.", sourcePath);
            File.Copy(sourcePath, Path.Combine(destinationDirectory, Path.GetFileName(sourcePath)), true);
        }

        private static void SignAndVerifyLocalBuild(string outputPath)
        {
            if (Application.platform != RuntimePlatform.OSXEditor)
                throw new PlatformNotSupportedException(
                    "The macOS release build must be finalized on macOS so codesign and lipo can verify it.");

            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string signingScript = Path.Combine(projectRoot, SigningScriptPath);
            string appPath = Path.GetFullPath(Path.Combine(projectRoot, outputPath));
            if (!File.Exists(signingScript))
                throw new FileNotFoundException("macOS signing script is missing.", signingScript);

            var processStartInfo = new ProcessStartInfo
            {
                FileName = "/bin/zsh",
                Arguments = QuoteProcessArgument(signingScript),
                WorkingDirectory = projectRoot,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            processStartInfo.EnvironmentVariables["NINJA_ADVENTURE_APP"] = appPath;
            processStartInfo.EnvironmentVariables["NINJA_CODESIGN_IDENTITY"] = "-";

            var standardOutput = new StringBuilder();
            var standardError = new StringBuilder();
            using var process = new Process { StartInfo = processStartInfo };
            process.OutputDataReceived += (_, args) =>
            {
                if (args.Data != null)
                    standardOutput.AppendLine(args.Data);
            };
            process.ErrorDataReceived += (_, args) =>
            {
                if (args.Data != null)
                    standardError.AppendLine(args.Data);
            };
            if (!process.Start())
                throw new InvalidOperationException("Failed to start the macOS signing verifier.");
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            process.WaitForExit();

            string outputText = standardOutput.ToString().Trim();
            string errorText = standardError.ToString().Trim();
            if (!string.IsNullOrWhiteSpace(outputText))
                Debug.Log(outputText);
            if (process.ExitCode != 0)
                throw new InvalidOperationException(
                    $"macOS signing verification failed ({process.ExitCode}).\n{errorText}");
            if (!string.IsNullOrWhiteSpace(errorText))
                Debug.Log(errorText);
        }

        private static string QuoteProcessArgument(string value)
        {
            return "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        }
    }
}
