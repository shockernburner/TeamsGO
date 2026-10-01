using System.Collections.Generic;
using UnityEngine;

namespace ProjectFossil.Audio
{
    // Recorded sound effects, which live only on machines that have them (their licence doesn't allow putting the
    // raw files in a public repository, so the folder is git-ignored). Clips sit in any Resources/SoundLibrary
    // folder and are grouped by the part of their name before the first underscore: Roar_1 and Roar_2 make the
    // "Roar" set. Anything missing falls back to the synthesized sounds.
    public static class SoundLibrary
    {
        private static Dictionary<string, AudioClip[]> _sets;

        // The clips of a set, or null when there are none.
        public static AudioClip[] Get(string set)
        {
            Load();
            return _sets.TryGetValue(set, out var clips) ? clips : null;
        }

        public static AudioClip One(string set)
        {
            var clips = Get(set);
            return clips != null ? clips[0] : null;
        }

        private static void Load()
        {
            if (_sets != null) return;
            var groups = new Dictionary<string, List<AudioClip>>();
            foreach (var clip in Resources.LoadAll<AudioClip>("SoundLibrary"))
            {
                if (clip == null) continue;
                int cut = clip.name.IndexOf('_');
                string set = cut > 0 ? clip.name.Substring(0, cut) : clip.name;
                if (!groups.TryGetValue(set, out var list)) groups[set] = list = new List<AudioClip>();
                list.Add(clip);
            }
            _sets = new Dictionary<string, AudioClip[]>();
            foreach (var kv in groups)
            {
                kv.Value.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
                _sets[kv.Key] = kv.Value.ToArray();
            }
            if (_sets.Count > 0) Debug.Log($"[SoundLibrary] Recorded sounds: {string.Join(", ", _sets.Keys)}");
        }
    }
}
