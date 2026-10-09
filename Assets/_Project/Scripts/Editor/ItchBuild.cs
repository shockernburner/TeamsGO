using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace ProjectFossil.EditorTools
{
    // The itch.io builds: Windows and Mac, each in its own folder ready for butler (Docs/ITCH.md). Unlike the
    // Steam test build there is no steam_appid.txt beside them, so on itch the game never claims to be Valve's
    // test app: Steam stays off, and co-op is LAN or direct IP. The current build target is left as it was.
    public static class ItchBuild
    {
        public const string WindowsFolder = "Builds/itch/windows";
        public const string MacFolder     = "Builds/itch/mac";
        private const string GameName     = "Hushclaw";

        [MenuItem("Project Fossil/Release/Build for itch (Windows + Mac)")]
        public static void BuildBoth()
        {
            bool win = BuildWindows(), mac = BuildMac();
            string summary = $"Windows: {(win ? "built" : "FAILED")}\nMac: {(mac ? "built" : "FAILED")}\n\nFolders: {Path.GetFullPath("Builds/itch")}";
            Debug.Log($"[ItchBuild] {summary.Replace('\n', ' ')}");
            if (!Application.isBatchMode)
            {
                EditorUtility.DisplayDialog("Build for itch", summary, "OK");
                EditorUtility.RevealInFinder(Path.GetFullPath("Builds/itch"));
            }
        }

        [MenuItem("Project Fossil/Release/Build for itch (Windows only)")]
        public static bool BuildWindows()
        {
            if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Standalone, BuildTarget.StandaloneWindows64))
            {
                Debug.LogError("[ItchBuild] This Unity can't build for Windows: add \"Windows Build Support (Mono)\" in Unity Hub.");
                return false;
            }
            return Build(BuildTarget.StandaloneWindows64, WindowsFolder, $"{WindowsFolder}/{GameName}.exe");
        }

        [MenuItem("Project Fossil/Release/Build for itch (Mac only)")]
        public static bool BuildMac()
        {
            // One app for Apple silicon and Intel Macs.
            UnityEditor.OSXStandalone.UserBuildSettings.architecture = UnityEditor.Build.OSArchitecture.x64ARM64;
            return Build(BuildTarget.StandaloneOSX, MacFolder, $"{MacFolder}/{GameName}.app");
        }

        private static bool Build(BuildTarget target, string folder, string location)
        {
            var scenes = new System.Collections.Generic.List<string>();
            foreach (var s in EditorBuildSettings.scenes)
                if (s.enabled) scenes.Add(s.path);
            if (Directory.Exists(folder)) FileUtil.DeleteFileOrDirectory(folder); // nothing stale left for butler to upload
            Directory.CreateDirectory(folder);
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = scenes.ToArray(),
                locationPathName = location,
                target = target,
                targetGroup = BuildTargetGroup.Standalone,
                options = BuildOptions.None,
            });
            if (report.summary.result != BuildResult.Succeeded)
            {
                Debug.LogError($"[ItchBuild] {target} build failed ({report.summary.result}). See the Console above for why.");
                return false;
            }
            // Never ship Steam's test App ID to itch, wherever it came from.
            foreach (var f in Directory.GetFiles(folder, "steam_appid.txt", SearchOption.AllDirectories)) File.Delete(f);
            // Unity's debug symbols are for us, not players.
            foreach (var d in Directory.GetDirectories(folder, "*_BurstDebugInformation_DoNotShip"))
                FileUtil.DeleteFileOrDirectory(d);
            Debug.Log($"[ItchBuild] {target}: {Path.GetFullPath(location)} ({report.summary.totalSize / (1024 * 1024)} MB)");
            return true;
        }
    }
}
