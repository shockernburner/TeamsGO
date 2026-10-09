using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace ProjectFossil.EditorTools
{
    // Builds a Windows copy of the game on this Mac, so a friend's Windows laptop can join a co-op match. Needs
    // Unity Hub > Installs > this Unity version > Add modules > "Windows Build Support (Mono)". The current build
    // target is left as it was.
    public static class WindowsBuild
    {
        private const string Folder = "Builds/Windows/Hushclaw";
        private const string Zip    = "Builds/Hushclaw-Windows.zip";

        [MenuItem("Project Fossil/Co-op/Build for Windows")]
        public static void Build()
        {
            if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Standalone, BuildTarget.StandaloneWindows64))
            {
                EditorUtility.DisplayDialog("Build for Windows",
                    "This Unity can't build for Windows yet.\n\nIn Unity Hub: Installs > Unity 6.5 > gear icon > Add modules > " +
                    "tick \"Windows Build Support (Mono)\", install, then restart Unity and run this menu again.", "OK");
                return;
            }

            var scenes = new System.Collections.Generic.List<string>();
            foreach (var s in EditorBuildSettings.scenes)
                if (s.enabled) scenes.Add(s.path);
            Directory.CreateDirectory(Folder);
            var options = new BuildPlayerOptions
            {
                scenes = scenes.ToArray(),
                locationPathName = $"{Folder}/Hushclaw.exe",
                target = BuildTarget.StandaloneWindows64,
                targetGroup = BuildTargetGroup.Standalone,
                options = BuildOptions.None,
            };
            var report = BuildPipeline.BuildPlayer(options);
            if (report.summary.result != BuildResult.Succeeded)
            {
                Debug.LogError($"[WindowsBuild] Build failed ({report.summary.result}). See the Console above for why.");
                return;
            }
            // Started outside the Steam library, a test build finds its App ID here (480 until the store app exists).
            if (File.Exists("steam_appid.txt")) File.Copy("steam_appid.txt", $"{Folder}/steam_appid.txt", true);
            if (File.Exists(Zip)) File.Delete(Zip);
            System.IO.Compression.ZipFile.CreateFromDirectory(Folder, Zip, System.IO.Compression.CompressionLevel.Optimal, true);
            string full = Path.GetFullPath(Zip);
            Debug.Log($"[WindowsBuild] Done: {full} ({new FileInfo(Zip).Length / (1024 * 1024)} MB). Send the zip, unzip it on the Windows PC, start Steam there, then run Hushclaw.exe.");
            if (!Application.isBatchMode) EditorUtility.RevealInFinder(full);
        }
    }
}
