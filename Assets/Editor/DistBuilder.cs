using System;
using System.IO;
using KeyboardWanderer.Editor;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

// External release entry points. build-and-package.sh invokes BuildMac and
// BuildWindows in separate Unity processes with matching -buildTarget values.
public static class DistBuilder
{
    static string OutBase()
    {
        string configured = Environment.GetEnvironmentVariable("DIST_OUT");
        return string.IsNullOrEmpty(configured)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "notegame-dist")
            : configured;
    }

    public static void BuildMac()
    {
        Build(BuildTarget.StandaloneOSX, "mac",
            KeyboardWandererBuild.PortableProductFileName + ".app");
    }

    public static void BuildWindows()
    {
        Build(BuildTarget.StandaloneWindows64, "windows",
            KeyboardWandererBuild.PortableProductFileName + ".exe");
    }

    public static void BuildAll()
    {
        throw new InvalidOperationException(
            "Building macOS and Windows in one Unity process can retain stale platform " +
            "compiler symbols. Run BuildSupport/distribute/build-and-package.sh instead.");
    }

    static void Build(BuildTarget target, string subdirectory, string fileName)
    {
        string outputPath = Path.Combine(OutBase(), subdirectory, fileName);
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? OutBase());
        BuildReport report = KeyboardWandererBuild.BuildDesktopPlayer(target, outputPath);
        BuildSummary summary = report.summary;
        Debug.Log($"[DistBuilder] {target} result={summary.result} errors={summary.totalErrors} " +
                  $"sizeBytes={summary.totalSize} out={outputPath}");
    }
}
