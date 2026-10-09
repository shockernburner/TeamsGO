using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ProjectFossil.Core;
using ProjectFossil.Match;
using Unity.Services.CloudCode;
using Unity.Services.Leaderboards;
using UnityEngine;

namespace ProjectFossil.Net
{
    // The worldwide boards, on Unity Gaming Services (itch and Steam alike), for signed-in accounts playing online.
    // Scores add up (Match/CareerScore): every player's runs on the survivors boards (all time, this week, their
    // country), every crew's matches on the teams boards with the escape bonus or the wipe penalty.
    //
    // The game never writes a score itself: it sends each finished run to a Cloud Code script
    // (Data/Services/CloudCode/RecordRun.js, and RecordCrew.js from the host for the crew), which checks it with
    // the same rules as Match/ScoreCheck, refuses an island already counted or results faster than matches can be
    // played, and only then adds it. The boards are Data/Services/*.lb; scripts and boards are published from
    // Services > Deployment. Launched with -testboards, everything goes to the test_ boards instead.
    public static class OnlineLeaderboard
    {
        public enum Board { Survivors, SurvivorsWeek, SurvivorsCountry, Crews, CrewsWeek }

        public class Entry
        {
            public int    Rank;
            public string Name, Crew, Country;
            public long   Score;
        }

        private const int Fetch = 50;
        private const string CountryKey = "ProjectFossil.Country";

        public static bool TestBoards { get; } = Array.IndexOf(Environment.GetCommandLineArgs(), "-testboards") >= 0;
        private static string Pre => TestBoards ? "test_" : "";

        public static string Status { get; private set; } = "Not connected yet.";
        public static bool Busy { get; private set; }
        public static string LastResult { get; private set; } // what the last finished run did to the totals
        private static readonly Dictionary<Board, List<Entry>> _boards = new Dictionary<Board, List<Entry>>();
        private static DateTime _lastAttempt = DateTime.MinValue;

        public static List<Entry> Get(Board b) => _boards.TryGetValue(b, out var l) ? l : new List<Entry>();

        // The player's country for the country boards: their choice, else the computer's region.
        public static string PlayerCountry
        {
            get => Country.Normalize(PlayerPrefs.GetString(CountryKey, Country.FromSystem()));
            set { PlayerPrefs.SetString(CountryKey, Country.Normalize(value)); PlayerPrefs.Save(); _lastAttempt = DateTime.MinValue; }
        }

        // Country boards exist for these (Data/Services/survivors_total_XX.lb); anyone else is on the global ones.
        public static readonly string[] Countries =
        {
            "US","GB","CA","AU","NZ","IE","DE","FR","ES","IT","NL","BE","SE","NO","DK","FI","PL","PT","BR","MX",
            "AR","CL","RU","UA","TR","IN","PK","BD","LK","SG","MY","ID","PH","TH","VN","JP","KR","TW","HK","CN",
            "SA","AE","EG","NG","ZA",
        };
        public static bool HasCountryBoard(string code) => Array.IndexOf(Countries, Country.Normalize(code)) >= 0;

        public static string CountryName(string code)
        {
            code = Country.Normalize(code);
            if (code == Country.Unknown) return "Not set";
            try { return new System.Globalization.RegionInfo(code).EnglishName; } catch { return code; }
        }

        private static string BoardId(Board b) => b switch
        {
            Board.Survivors        => Pre + "survivors_total",
            Board.SurvivorsWeek    => Pre + "survivors_week",
            Board.SurvivorsCountry => "survivors_total_" + PlayerCountry,
            Board.Crews            => Pre + "teams_total",
            _                      => Pre + "teams_week",
        };

        private static bool SignedIn()
        {
            if (!Account.SignedIn) Status = "Sign in with Play Online to see and join the worldwide boards.";
            return Account.SignedIn;
        }

        // ── Sending ───────────────────────────────────────────────────────────

        // Results waiting to go up: a finished online match whose result couldn't be sent (the connection dropped)
        // is kept on this computer and sent next time. Offline practice never comes here.
        [Serializable] private class Pending
        {
            public bool Team;        // false: one player's run (submit_run); true: the crew's match (submit_team)
            public RunRecord Run;
            public List<int> Scores; // team: each member's score
            public int Escaped;
        }
        [Serializable] private class PendingList { public List<Pending> Items = new List<Pending>(); }
        private const string PendingKey = "ProjectFossil.PendingRuns";

        // Read again around every send (Core/SendQueue): a match end queues the player's run and, right after it,
        // the crew's match. The crew's arrived while the run was being sent, and saving the list read before it
        // dropped the crew's result, so the crew boards stayed empty.
        private static readonly SendQueue<Pending> _queue = new SendQueue<Pending>(
            () =>
            {
                try { return (JsonUtility.FromJson<PendingList>(PlayerPrefs.GetString(PendingKey, "")) ?? new PendingList()).Items; }
                catch { return new List<Pending>(); }
            },
            items =>
            {
                PlayerPrefs.SetString(PendingKey, JsonUtility.ToJson(new PendingList { Items = items }));
                PlayerPrefs.Save();
            });

        // This player's finished online run: the score they earned is added, whether they got out or died.
        public static void Submit(RunRecord run)
        {
            if (run == null) return;
            if (!ScoreCheck.IsPlausible(run.Score, run.Seconds, run.Kills, out string why))
            {
                Debug.Log($"[OnlineLeaderboard] Not sending this run: {why}");
                return;
            }
            Queue(new Pending { Run = run });
        }

        // The crew's match, from the host (or a solo player, a crew of one): each member's score and how many got out.
        public static void SubmitTeam(RunRecord run, List<int> scores, int escaped)
        {
            if (run == null || scores == null || scores.Count == 0 || string.IsNullOrWhiteSpace(run.Crew)) return;
            Queue(new Pending { Team = true, Run = run, Scores = scores, Escaped = escaped });
        }

        private static void Queue(Pending p)
        {
            _queue.Add(p);
            Flush();
        }

        [Serializable] private class Reply { public bool ok; public string reason; public int delta; public long total; }

        // Sends every waiting result, oldest first; stops at the first connection failure and tries again later.
        // A result the server refuses is dropped (sending it again wouldn't change the answer).
        public static async void Flush()
        {
            if (_queue.Sending || !Account.SignedIn || _queue.Count == 0) return;
            try
            {
                await _queue.Drain(async p =>
                {
                    var reply = p.Team ? await SendTeam(p) : await SendRun(p);
                    if (reply != null && !reply.ok) Debug.Log($"[OnlineLeaderboard] The server refused a result: {reply.reason}");
                    else if (reply != null && !p.Team) LastResult = $"+{reply.delta:N0} points. Your total: {reply.total:N0}";
                    else if (reply != null) Debug.Log($"[OnlineLeaderboard] Crew result in: {reply.delta} points, crew total {reply.total:N0}.");
                });
                Status = "Your results are on the worldwide boards.";
                _lastAttempt = DateTime.MinValue; // show them on the next look
            }
            catch (Exception e)
            {
                Status = $"Offline: {_queue.Count} result(s) will be sent when you're back online.";
                Debug.Log($"[OnlineLeaderboard] Sending failed: {e.Message}");
            }
        }

        private static async Task<Reply> SendRun(Pending p)
        {
            var args = new Dictionary<string, object>
            {
                { "score", p.Run.Score }, { "seconds", p.Run.Seconds }, { "kills", p.Run.Kills }, { "seed", p.Run.Seed },
                { "test", TestBoards }, { "player", p.Run.Player ?? "" }, { "crew", p.Run.Crew ?? "" },
                { "country", PlayerCountry },
            };
            string json = await CloudCodeService.Instance.CallEndpointAsync("RecordRun", args);
            return JsonUtility.FromJson<Reply>(json);
        }

        private static async Task<Reply> SendTeam(Pending p)
        {
            var args = new Dictionary<string, object>
            {
                { "crew", p.Run.Crew }, { "scores", p.Scores }, { "escaped", p.Escaped }, { "seconds", p.Run.Seconds },
                { "seed", p.Run.Seed }, { "test", TestBoards },
            };
            string json = await CloudCodeService.Instance.CallEndpointAsync("RecordCrew", args);
            return JsonUtility.FromJson<Reply>(json);
        }

        // ── Reading ───────────────────────────────────────────────────────────

        // Fetches the boards (the panel calls this when it opens, at most every 20 seconds).
        public static async void Refresh()
        {
            if (Busy || (DateTime.UtcNow - _lastAttempt).TotalSeconds < 20) return;
            _lastAttempt = DateTime.UtcNow;
            if (!SignedIn()) return;
            Busy = true;
            Flush(); // anything waiting goes up first
            Status = "Loading the worldwide boards...";
            try
            {
                foreach (Board b in Enum.GetValues(typeof(Board)))
                {
                    if (b == Board.SurvivorsCountry && !HasCountryBoard(PlayerCountry)) { _boards[b] = new List<Entry>(); continue; }
                    var page = await LeaderboardsService.Instance.GetScoresAsync(BoardId(b), new GetScoresOptions { Limit = Fetch, IncludeMetadata = true });
                    _boards[b] = ToEntries(page.Results, b == Board.Crews || b == Board.CrewsWeek);
                }
                Status = TestBoards ? "Test boards." : "Worldwide.";
            }
            catch (Exception e)
            {
                Status = "Couldn't load the worldwide boards.";
                Debug.Log($"[OnlineLeaderboard] Refresh failed: {e.Message}");
            }
            finally { Busy = false; }
        }

        [Serializable] private class Meta { public string player, crew, country; }

        private static List<Entry> ToEntries(List<Unity.Services.Leaderboards.Models.LeaderboardEntry> results, bool crews)
        {
            var list = new List<Entry>();
            if (results == null) return list;
            foreach (var e in results)
            {
                Meta meta = null;
                try { if (e.Metadata != null) meta = JsonUtility.FromJson<Meta>(e.Metadata.ToString()); }
                catch { /* an entry without readable details still shows its score */ }
                string crew = string.IsNullOrWhiteSpace(meta?.crew) ? "" : NameFilter.Clean(meta.crew, "Unnamed crew");
                list.Add(new Entry
                {
                    Rank = e.Rank + 1,
                    Name = crews ? crew : NameFilter.Clean(meta?.player ?? StripTag(e.PlayerName), "Survivor"),
                    Crew = crew,
                    Country = Country.Normalize(meta?.country),
                    Score = (long)e.Score,
                });
            }
            return list;
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
