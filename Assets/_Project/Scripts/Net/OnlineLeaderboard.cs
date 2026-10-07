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

        // A finished run: on the survivors board always, on the crews board when this player names the crew.
        public static async void Submit(RunRecord run, bool forCrew)
        {
            if (run == null || run.Score <= 0) return;
            if (!await SignIn()) return;
            var meta = new Meta
            {
                player = run.Player, crew = run.Crew, size = run.CrewSize, kills = run.Kills, seconds = run.Seconds,
                result = run.Result.ToString(),
            };
            var options = new AddPlayerScoreOptions { Metadata = meta };
            try
            {
                await LeaderboardsService.Instance.AddPlayerScoreAsync(SurvivorsId, run.Score, options);
                if (forCrew && !string.IsNullOrWhiteSpace(run.Crew))
                    await LeaderboardsService.Instance.AddPlayerScoreAsync(CrewsId, run.Score, options);
                Status = "Your run is on the online boards.";
            }
            catch (Exception e)
            {
                Status = "Couldn't send the run to the online boards.";
                Debug.Log($"[OnlineLeaderboard] Submit failed: {e.Message}");
            }
        }

        // Fetches both boards (the panel calls this when it opens, at most every 20 seconds).
        public static async void Refresh()
        {
            if (Busy || (DateTime.UtcNow - _lastAttempt).TotalSeconds < 20) return;
            _lastAttempt = DateTime.UtcNow;
            Busy = true;
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
