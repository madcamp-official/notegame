using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

// 외부 배포용 플레이어 빌드 (batchmode -executeMethod 로 호출).
// 씬은 EditorBuildSettings 의 enabled 목록을 그대로 사용.
public static class DistBuilder
{
    static string[] Scenes() =>
        EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();

    static string OutBase()
    {
        var o = Environment.GetEnvironmentVariable("DIST_OUT");
        return string.IsNullOrEmpty(o)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "notegame-dist")
            : o;
    }

    public static void BuildMac()   => Build(BuildTarget.StandaloneOSX,       "mac",     "Ninja Adventure.app");
    public static void BuildWindows()=> Build(BuildTarget.StandaloneWindows64, "windows", "Ninja Adventure.exe");

    public static void BuildAll()
    {
        BuildMac();
        BuildWindows();
    }

    static void Build(BuildTarget target, string subdir, string fileName)
    {
        // 맥에서 윈도우 크로스빌드가 되도록 스탠드얼론 백엔드를 Mono 로 고정.
        PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, ScriptingImplementation.Mono2x);

        var outPath = Path.Combine(OutBase(), subdir, fileName);
        Directory.CreateDirectory(Path.GetDirectoryName(outPath));

        var opts = new BuildPlayerOptions
        {
            scenes = Scenes(),
            locationPathName = outPath,
            target = target,
            targetGroup = BuildTargetGroup.Standalone,
            options = BuildOptions.None
        };

        var report = BuildPipeline.BuildPlayer(opts);
        var s = report.summary;
        Debug.Log($"[DistBuilder] {target} result={s.result} errors={s.totalErrors} sizeBytes={s.totalSize} out={outPath}");
        if (s.result != BuildResult.Succeeded)
        {
            Debug.LogError($"[DistBuilder] BUILD FAILED for {target}");
            EditorApplication.Exit(1);
        }
    }
}
