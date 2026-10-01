using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;

namespace ProjectFossil.Editor
{
    // The game loads recorded sounds from a Resources/SoundLibrary folder, which is easy to lose when unzipping or
    // dragging files in. This finds our clips (Roar_1, RainLoop, ...) anywhere under Assets/SoundLibrary and moves
    // them into Assets/SoundLibrary/Resources/SoundLibrary, keeping their import settings.
    internal static class SoundSetup
    {
        private const string Root = "Assets/SoundLibrary";
        private const string Target = Root + "/Resources/SoundLibrary";

        private static readonly string[] Sets =
            { "Roar", "Growl", "Screech", "Breath", "Thunder", "Rotor", "RainLoop", "StormLoop", "WindLoop" };

        internal static List<string> Misplaced()
        {
            var list = new List<string>();
            if (!AssetDatabase.IsValidFolder(Root)) return list;
            foreach (var guid in AssetDatabase.FindAssets("t:AudioClip", new[] { Root }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.Replace('\\', '/').Contains("/Resources/SoundLibrary/")) continue;
                if (IsOurs(Path.GetFileNameWithoutExtension(path))) list.Add(path);
            }
            return list;
        }

        private static bool IsOurs(string name)
        {
            int cut = name.IndexOf('_');
            string set = cut > 0 ? name.Substring(0, cut) : name;
            return System.Array.IndexOf(Sets, set) >= 0;
        }

        // Returns how many clips were moved.
        internal static int Organise(StringBuilder log)
        {
            if (!AssetDatabase.IsValidFolder(Root))
            {
                log.AppendLine("Recorded sounds: no Assets/SoundLibrary folder, so the game uses synthesized ones.");
                return 0;
            }
            var misplaced = Misplaced();
            if (misplaced.Count > 0)
            {
                if (!AssetDatabase.IsValidFolder(Root + "/Resources")) AssetDatabase.CreateFolder(Root, "Resources");
                if (!AssetDatabase.IsValidFolder(Target)) AssetDatabase.CreateFolder(Root + "/Resources", "SoundLibrary");
            }
            int moved = 0;
            foreach (var path in misplaced)
            {
                string to = Target + "/" + Path.GetFileName(path);
                if (AssetDatabase.LoadMainAssetAtPath(to) != null) { log.AppendLine($"  {path}: already in place, left as is"); continue; }
                string error = AssetDatabase.MoveAsset(path, to);
                if (string.IsNullOrEmpty(error)) moved++;
                else log.AppendLine($"  {path}: couldn't move ({error})");
            }
            int ready = AssetDatabase.IsValidFolder(Target) ? AssetDatabase.FindAssets("t:AudioClip", new[] { Target }).Length : 0;
            log.AppendLine($"Recorded sounds: {ready} ready in {Target}" + (moved > 0 ? $" ({moved} moved there now)." : "."));
            if (ready == 0)
            {
                var other = AssetDatabase.FindAssets("t:AudioClip", new[] { Root });
                log.AppendLine($"  Assets/SoundLibrary has {other.Length} other sound files; none is named like Roar_1 or RainLoop.");
            }
            return moved;
        }
    }
}
