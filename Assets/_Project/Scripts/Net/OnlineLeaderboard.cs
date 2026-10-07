using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ProjectFossil.Match;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Leaderboards;
using UnityEngine;

namespace ProjectFossil.Net
{
    // The worldwide boards, on Unity Gaming Services (works on itch and Steam alike). Each player signs in
    // anonymously, without an account; the service keeps one best score per player on each board:
    //   survivors: every player's best run.
    //   crews:     the best run of each crew, sent by whoever names it (the host online, the player solo), so a
    //              player's entry is their best crew run; the panel shows each crew name once.
    // The boards themselves are Data/Services/*.lb, published from Window > Unity Services > Deployment.
    // Without a connection (or before the boards are published) everything here quietly stays offline.
    public static class OnlineLeaderboard
    {
        public const string SurvivorsId = "survivors", CrewsId = "crews";
        private const int Fetch = 50;

        public static string Status { get; private set; } = "Not connected yet.";
        public static bool Busy { get; private set; }
        public static List<RunRecord> Survivors { get; } = new List<RunRecord>();
        public static List<RunRecord> Crews { get; } = new List<RunRecord>();
        private static DateTime _lastAttempt = DateTime.MinValue;

        [Serializable]
        private class Meta
        {
            public string player, crew, result;
            public int size, kills;
            public float seconds;
        }

        private static Task<bool> _signIn;

        private static Task<bool> SignIn() => _signIn != null && !_signIn.IsFaulted && (!_signIn.IsCompleted || _signIn.Result)
            ? _signIn : _signIn = SignInNow();

        private static async Task<bool> SignInNow()
        {
            try
            {
                if (UnityServices.State != ServicesInitializationState.Initialized) await UnityServices.InitializeAsync();
                if (!AuthenticationService.Instance.IsSignedIn) await AuthenticationService.Instance.SignInAnonymouslyAsync();
                return true;
            }
            catch (Exception e)
            {
                Status = "Online boards are offline (no connection?).";
                Debug.Log($"[OnlineLeaderboard] Sign-in failed: {e.Message}");
                return false;
            }
        }

        // Runs waiting to go up: a match finished offline (solo, or co-op on the same Wi-Fi with no internet) is
        // kept on this computer and sent the next time the boards are reachable.
        [Serializable] private class Pending { public RunRecord Run; public bool ForCrew; }
        [Serializable] private class PendingList { public List<Pending> Items = new List<Pending>(); }
        private const string PendingKey = "ProjectFossil.PendingRuns";
        private const int MaxPending = 50;
        private static bool _flushing;

        private static PendingList LoadPending()
        {
            try { return JsonUtility.FromJson<PendingList>(PlayerPrefs.GetString(PendingKey, "")) ?? new PendingList(); }
            catch { return new PendingList(); }
        }

        private static void SavePending(PendingList list)
        {
            if (list.Items.Count > MaxPending) list.Items.RemoveRange(0, list.Items.Count - MaxPending);
            PlayerPrefs.SetString(PendingKey, JsonUtility.ToJson(list));
            PlayerPrefs.Save();
        }

        public static int PendingCount => LoadPending().Items.Count;

        // A finished run: on the survivors board always, on the crews board when this player names the crew.
        public static void Submit(RunRecord run, bool forCrew)
        {
            if (run == null || run.Score <= 0) return;
            var list = LoadPending();
            list.Items.Add(new Pending { Run = run, ForCrew = forCrew });
            SavePending(list);
            Flush();
        }

        // Sends every waiting run, oldest first; stops at the first failure and tries again later.
        public static async void Flush()
        {
            if (_flushing) return;
            var list = LoadPending();
            if (list.Items.Count == 0) return;
            _flushing = true;
            try
            {
                if (!await SignIn()) return;
                while (list.Items.Count > 0)
                {
                    var p = list.Items[0];
                    var meta = new Meta
                    {
                        player = p.Run.Player, crew = p.Run.Crew, size = p.Run.CrewSize, kills = p.Run.Kills,
                        seconds = p.Run.Seconds, result = p.Run.Result.ToString(),
                    };
                    var options = new AddPlayerScoreOptions { Metadata = meta };
                    await LeaderboardsService.Instance.AddPlayerScoreAsync(SurvivorsId, p.Run.Score, options);
                    if (p.ForCrew && !string.IsNullOrWhiteSpace(p.Run.Crew))
                        await LeaderboardsService.Instance.AddPlayerScoreAsync(CrewsId, p.Run.Score, options);
                    list.Items.RemoveAt(0);
                    SavePending(list);
                }
                Status = "Your runs are on the online boards.";
                _lastAttempt = DateTime.MinValue; // show them on the next look
            }
            catch (Exception e)
            {
                Status = $"Offline: {list.Items.Count} run(s) will be sent when you're back online.";
                Debug.Log($"[OnlineLeaderboard] Submit failed: {e.Message}");
            }
            finally { _flushing = false; }
        }

        // Fetches both boards (the panel calls this when it opens, at most every 20 seconds).
        public static async void Refresh()
        {
            if (Busy || (DateTime.UtcNow - _lastAttempt).TotalSeconds < 20) return;
            _lastAttempt = DateTime.UtcNow;
            Busy = true;
            Flush(); // anything played offline goes up first
            Status = "Loading the online boards...";
            try
            {
                if (!await SignIn()) return;
                var options = new GetScoresOptions { Limit = Fetch, IncludeMetadata = true };
                var survivors = await LeaderboardsService.Instance.GetScoresAsync(SurvivorsId, options);
                var crews = await LeaderboardsService.Instance.GetScoresAsync(CrewsId, options);
                Fill(Survivors, survivors.Results, oncePerCrew: false);
                Fill(Crews, crews.Results, oncePerCrew: true);
                Status = "Worldwide.";
            }
            catch (Exception e)
            {
                Status = "Couldn't load the online boards.";
                Debug.Log($"[OnlineLeaderboard] Refresh failed: {e.Message}");
            }
            finally { Busy = false; }
        }

        private static void Fill(List<RunRecord> into, List<Unity.Services.Leaderboards.Models.LeaderboardEntry> entries, bool oncePerCrew)
        {
            into.Clear();
            if (entries == null) return;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in entries)
            {
                Meta meta = null;
                try { if (e.Metadata != null) meta = JsonUtility.FromJson<Meta>(e.Metadata.ToString()); }
                catch { /* an entry without readable details still shows its score */ }
                var run = new RunRecord
                {
                    Player = NameFilter.Clean(meta?.player ?? StripTag(e.PlayerName), "Survivor"),
                    Crew = string.IsNullOrWhiteSpace(meta?.crew) ? "" : NameFilter.Clean(meta.crew, "Unnamed crew"), CrewSize = Math.Max(1, meta?.size ?? 1),
                    Score = (int)e.Score, Kills = meta?.kills ?? 0, Seconds = meta?.seconds ?? 0f,
                    Result = Enum.TryParse(meta?.result, out MatchResult r) ? r : MatchResult.Extracted,
                };
                if (oncePerCrew && (string.IsNullOrWhiteSpace(run.Crew) || !seen.Add(run.Crew.Trim()))) continue;
                into.Add(run);
            }
        }

        // The service's generated names end in "#1234".
        private static string StripTag(string name)
        {
            if (string.IsNullOrEmpty(name)) return "Survivor";
            int hash = name.LastIndexOf('#');
            return hash > 0 ? name.Substring(0, hash) : name;
        }
    }
}
