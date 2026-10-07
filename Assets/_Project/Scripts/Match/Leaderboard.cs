using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace ProjectFossil.Match
{
    // One finished match, as the leaderboard keeps it.
    [Serializable]
    public class RunRecord
    {
        public string Player;
        public string Crew;
        public int    CrewSize;
        public int    Score;
        public MatchResult Result;
        public float  Seconds;
        public int    Kills;
        public ChallengeLevel Challenge;
        public int    Seed;
        public long   WhenUtcTicks;
    }

    // The best runs on this computer, ranked two ways: survivors (each player's best) and crews (each crew's best).
    // Plain data with its own file, so an online board (Steam or another service) can later take the same records.
    public class Leaderboard
    {
        public const int Keep = 200; // runs kept; the lowest scores drop off

        [Serializable] private class Saved { public List<RunRecord> Runs = new List<RunRecord>(); }

        private readonly List<RunRecord> _runs = new List<RunRecord>();
        private readonly string _path; // null: in memory only (tests)

        public IReadOnlyList<RunRecord> Runs => _runs;

        public Leaderboard(string path = null)
        {
            _path = path;
            Load();
        }

        // The game's own board, in the player's save folder.
        private static Leaderboard _local;
        public static Leaderboard Local => _local ??= new Leaderboard(Path.Combine(Application.persistentDataPath, "leaderboard.json"));

        // Adds a run and returns its place among all runs on this board (1 = best).
        public int Record(RunRecord run)
        {
            if (run == null) return 0;
            if (run.WhenUtcTicks == 0) run.WhenUtcTicks = DateTime.UtcNow.Ticks;
            _runs.Add(run);
            _runs.Sort(ByScore);
            int place = _runs.IndexOf(run) + 1;
            if (_runs.Count > Keep) _runs.RemoveRange(Keep, _runs.Count - Keep);
            Save();
            return place <= Keep ? place : 0;
        }

        // Each player's best run, best first. Names match regardless of case.
        public List<RunRecord> TopSurvivors(int count) => BestPer(r => r.Player, count);

        // Each crew's best run, best first. A solo run counts for the player's crew too.
        public List<RunRecord> TopCrews(int count) => BestPer(r => r.Crew, count);

        private List<RunRecord> BestPer(Func<RunRecord, string> key, int count)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var best = new List<RunRecord>();
            foreach (var r in _runs) // already best first
            {
                string k = key(r);
                if (string.IsNullOrWhiteSpace(k) || !seen.Add(k.Trim())) continue;
                best.Add(r);
                if (best.Count >= count) break;
            }
            return best;
        }

        // Higher score first; on a tie the earlier run keeps its place.
        private static int ByScore(RunRecord a, RunRecord b)
        {
            int s = b.Score.CompareTo(a.Score);
            return s != 0 ? s : a.WhenUtcTicks.CompareTo(b.WhenUtcTicks);
        }

        private void Load()
        {
            _runs.Clear();
            if (_path == null || !File.Exists(_path)) return;
            try
            {
                var saved = JsonUtility.FromJson<Saved>(File.ReadAllText(_path));
                if (saved?.Runs != null) _runs.AddRange(saved.Runs.Where(r => r != null));
                _runs.Sort(ByScore);
            }
            catch (Exception e) { Debug.LogWarning($"[Leaderboard] Couldn't read {_path}: {e.Message}"); }
        }

        private void Save()
        {
            if (_path == null) return;
            try
            {
                var tmp = _path + ".tmp"; // write then swap, so a crash never leaves half a file
                File.WriteAllText(tmp, JsonUtility.ToJson(new Saved { Runs = _runs }));
                if (File.Exists(_path)) File.Delete(_path);
                File.Move(tmp, _path);
            }
            catch (Exception e) { Debug.LogWarning($"[Leaderboard] Couldn't save {_path}: {e.Message}"); }
        }
    }
}
