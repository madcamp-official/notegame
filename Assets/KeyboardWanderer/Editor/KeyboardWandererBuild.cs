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
        private const string OutputPath = "Builds/NinjaAdventure.app";
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
            ConfigureReleasePlayerSettings();

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
                options = BuildOptions.CleanBuildCache | BuildOptions.StrictMode
            });
            if (report.summary.result != BuildResult.Succeeded)
                throw new InvalidOperationException($"macOS build failed: {report.summary.result}");

            InstallThirdPartyNotices();
            SignAndVerifyLocalBuild();
            Debug.Log($"Keyboard Wanderer macOS build complete: {OutputPath} ({report.summary.totalSize} bytes)");
        }

        /// <summary>
        /// Keeps command-line, menu, and CI builds on the same release identity. macOS applies
        /// its own icon mask, so the source asset deliberately remains a full-bleed square.
        /// macOS uses its platform-specific x64ARM64 setting for a Universal player; the
        /// resulting binary is verified again with lipo after the build.
        /// </summary>
        private static void ConfigureReleasePlayerSettings()
        {
            Texture2D icon = AssetDatabase.LoadAssetAtPath<Texture2D>(AppIconPath);
            if (icon == null)
                throw new InvalidOperationException($"Release app icon is missing: {AppIconPath}");

            NamedBuildTarget target = NamedBuildTarget.Standalone;
            int[] iconSizes = PlayerSettings.GetIconSizes(target, IconKind.Application);
            if (iconSizes == null || iconSizes.Length == 0)
                throw new InvalidOperationException("Unity reported no macOS application icon slots.");

            PlayerSettings.SetIcons(target,
                Enumerable.Repeat(icon, iconSizes.Length).ToArray(), IconKind.Application);
            UnityEditor.OSXStandalone.UserBuildSettings.architecture =
                OSArchitecture.x64ARM64;
            PlayerSettings.productName = ProductName;
            PlayerSettings.bundleVersion = ReleaseVersion;
            PlayerSettings.SetApplicationIdentifier(target, BundleIdentifier);
            // Unity 6 exposes the macOS CFBundleVersion through this platform-specific
            // property. The legacy generic SetPropertyString path silently left release
            // players at build number 0 even though the editor setting appeared written.
            PlayerSettings.macOS.buildNumber = ReleaseBuildNumber;
            AssetDatabase.SaveAssets();
        }

        private static void InstallThirdPartyNotices()
        {
            string noticesDirectory = Path.Combine(OutputPath, "Contents", "Resources",
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

        /// <summary>
        /// Unity seals a macOS app as part of BuildPipeline.BuildPlayer. Adding the human-readable
        /// licenses afterward changes sealed resources, so the bundle must be signed again before
        /// it is executable or eligible for distribution signing. The shared release script signs
        /// nested Mach-O code from the inside out and performs strict recursive verification.
        /// </summary>
        private static void SignAndVerifyLocalBuild()
        {
            if (Application.platform != RuntimePlatform.OSXEditor)
                throw new PlatformNotSupportedException(
                    "The macOS release build must be finalized on macOS so codesign and lipo can verify it.");

            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string signingScript = Path.Combine(projectRoot, SigningScriptPath);
            string appPath = Path.Combine(projectRoot, OutputPath);
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
