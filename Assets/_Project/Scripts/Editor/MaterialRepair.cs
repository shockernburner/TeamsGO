using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace ProjectFossil.Editor
{
    // The materials the game loads from Resources were written as text by hand, outside Unity. One of them reads
    // badly in Unity 6.5 ("Tried to get mapping information from scalar node" when a match starts). Having Unity
    // load and save them once rewrites each in its own format, which clears the error. Runs by itself once per
    // project copy (the marker lives in Library, so a fresh clone repairs again); also on the menu.
    [InitializeOnLoad]
    public static class MaterialRepair
    {
        private const string Folder = "Assets/_Project/Resources";
        private const string Marker = "Library/ProjectFossil/materials-resaved-v1";

        static MaterialRepair()
        {
            if (File.Exists(Marker)) return;
            EditorApplication.delayCall += () =>
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode || File.Exists(Marker)) return;
                Resave();
            };
        }

        [MenuItem("Project Fossil/Maintenance/Resave Game Materials")]
        public static void Resave()
        {
            var paths = AssetDatabase.FindAssets("t:Material", new[] { Folder })
                .Select(AssetDatabase.GUIDToAssetPath)
                .ToArray();
            if (paths.Length > 0) AssetDatabase.ForceReserializeAssets(paths);

            Directory.CreateDirectory(Path.GetDirectoryName(Marker));
            File.WriteAllText(Marker, string.Join("\n", paths));
            Debug.Log($"[Project Fossil] Resaved {paths.Length} game materials in Unity's own format.");
        }
    }
}
