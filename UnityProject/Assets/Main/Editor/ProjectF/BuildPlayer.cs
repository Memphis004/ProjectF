using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

// ReSharper disable CheckNamespace
namespace ProjectF.Editor
{
    /// <summary>
    /// Stage 16 build entry points for the multi-instance test (and CI later):
    ///   Unity -batchmode -quit -executeMethod ProjectF.Editor.BuildPlayer.BuildWindowsDev
    /// Output lands in UnityProject/build/win-dev (or win-release) so two
    /// builds never collide. Dev builds include the Development flag for
    /// readable Player.log output.
    /// </summary>
    public static class BuildPlayer
    {
        public static void BuildWindowsDev() => BuildWindows(true);

        public static void BuildWindowsRelease() => BuildWindows(false);

        public static void BuildWindows(bool development)
        {
            string projectRoot = Path.GetDirectoryName(Application.dataPath)
                                 ?? throw new InvalidOperationException("cannot resolve project root");
            string outputDir = Path.Combine(
                projectRoot, development ? "build/win-dev" : "build/win-release");
            string exePath = Path.Combine(outputDir, "UnityProject.exe");
            Directory.CreateDirectory(outputDir);

            string[] scenes = EditorBuildSettings.scenes
                .Where(s => s.enabled)
                .Select(s => s.path)
                .ToArray();
            if (scenes.Length == 0)
            {
                throw new InvalidOperationException(
                    "EditorBuildSettings has no enabled scenes — run the scene generator first.");
            }

            BuildOptions options = development
                ? BuildOptions.Development | BuildOptions.AllowDebugging
                : BuildOptions.None;

            BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = exePath,
                target = BuildTarget.StandaloneWindows64,
                options = options,
            });

            if (report.summary.result != BuildResult.Succeeded)
            {
                throw new InvalidOperationException(
                    $"build failed: {report.summary.result} ({report.summary.totalErrors} errors)");
            }

            Debug.Log(
                $"[build] succeeded: {exePath} " +
                $"({report.summary.totalSize / (1024 * 1024)} MB, dev={development})");
        }
    }
}
