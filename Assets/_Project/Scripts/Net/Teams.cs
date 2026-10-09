using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ProjectFossil.Match;
using Unity.Services.CloudCode;
using Unity.Services.Leaderboards;
using UnityEngine;

namespace ProjectFossil.Net
{
    // The player's team, online: create one, join one, leave to join or found another. A team has a name, a country,
    // an emblem and up to eight members, kept on the server (Data/Services/CloudCode/Teams.js), and its points on the
    // teams boards. Online matches count for the host's team. Finding teams reads those boards: a country's, the top
    // ones, or a name among them.
    public static class Teams
    {
        public const int MaxMembers = 8;

        [Serializable] public class Member { public string id, name; }

        [Serializable] public class Team
        {
            public string id, name, country;
            public int avatar;
            public List<Member> members = new List<Member>();
        }

        // A team on a board, as the finder lists it.
        public class Listed
        {
            public string Id, Name, Country;
            public int Rank, Avatar, Members;
            public long Points;
        }

        public enum Scope { MyCountry, Top, Name }

        public static Team Mine { get; private set; }
        public static bool Known { get; private set; }      // asked the server since signing in
        public static bool Busy { get; private set; }
        public static string Status { get; private set; }
        public static List<Listed> Found { get; private set; } = new List<Listed>();
        public static bool Finding { get; private set; }

        private static string _knownFor;                    // the account Mine belongs to

        [Serializable] private class Reply { public bool ok; public string reason; public Team team; }

        // The player's team, asked once per sign-in (and again after any change).
        public static void EnsureKnown()
        {
            if (!Account.SignedIn) { Mine = null; Known = false; _knownFor = null; return; }
            if (Known && _knownFor == Account.PlayerId) return;
            if (!Busy) Refresh();
        }

        public static void Refresh() => Call("mine", null);
        public static void Create(string name, int avatar, Action done = null) =>
            Call("create", new Dictionary<string, object> { { "name", name ?? "" }, { "avatar", avatar }, { "country", OnlineLeaderboard.PlayerCountry } }, done);
        public static void Join(string id, Action done = null) => Call("join", new Dictionary<string, object> { { "id", id ?? "" } }, done);
        public static void Leave(Action done = null) => Call("leave", null, done);

        private static async void Call(string action, Dictionary<string, object> args, Action done = null)
        {
            if (Busy || !Account.SignedIn) return;
            Busy = true;
            Status = action == "mine" ? null : "Working...";
            args = args ?? new Dictionary<string, object>();
            args["action"] = action;
            args["player"] = Account.Username ?? "";
            args["test"] = OnlineLeaderboard.TestBoards;
            try
            {
                string json = await CloudCodeService.Instance.CallEndpointAsync("Teams", args);
                var reply = JsonUtility.FromJson<Reply>(json);
                if (reply == null || !reply.ok) { Status = reply?.reason ?? "The team service didn't answer."; return; }
                if (action == "leave") Mine = null;
                else Mine = reply.team != null && !string.IsNullOrEmpty(reply.team.id) ? reply.team : null;
                Known = true;
                _knownFor = Account.PlayerId;
                Status = action == "create" ? $"You founded {Mine?.name}." : action == "join" ? $"You joined {Mine?.name}."
                       : action == "leave" ? "You left your team." : null;
                OnlineLeaderboard.ForgetBoards();
                done?.Invoke();
            }
            catch (Exception e)
            {
                Status = "Couldn't reach the team service. Check the internet connection.";
                Debug.Log($"[Teams] {action} failed: {e.Message}");
            }
            finally { Busy = false; }
        }

        // Teams to join: a country's board, the top of the world board, or names matching among the top 200.
        public static async void Find(Scope scope, string query)
        {
            if (Finding || !Account.SignedIn) return;
            Finding = true;
            Status = null;
            try
            {
                string country = OnlineLeaderboard.PlayerCountry;
                string pre = OnlineLeaderboard.TestBoards ? "test_" : "";
                string board = scope == Scope.MyCountry && OnlineLeaderboard.HasCountryBoard(country) && pre == ""
                    ? "teams_total_" + country : pre + "teams_total";
                int limit = scope == Scope.Name ? 200 : 50;
                var page = await LeaderboardsService.Instance.GetScoresAsync(board, new GetScoresOptions { Limit = limit, IncludeMetadata = true });
                var list = new List<Listed>();
                string q = (query ?? "").Trim();
                foreach (var e in page.Results)
                {
                    var l = Parse(e);
                    if (scope == Scope.MyCountry && l.Country != Country.Normalize(country)) continue;
                    if (scope == Scope.Name && (q.Length == 0 || l.Name.IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0)) continue;
                    list.Add(l);
                }
                Found = list;
                if (list.Count == 0)
                    Status = scope == Scope.Name ? "No team by that name yet. Create it!" : "No teams here yet. Create the first!";
            }
            catch (Exception e)
            {
                Found = new List<Listed>();
                Status = "Couldn't load teams.";
                Debug.Log($"[Teams] find failed: {e.Message}");
            }
            finally { Finding = false; }
        }

        [Serializable] private class Meta { public string crew, country; public int avatar, members; }

        internal static Listed Parse(Unity.Services.Leaderboards.Models.LeaderboardEntry e)
        {
            Meta m = null;
            try { if (e.Metadata != null) m = JsonUtility.FromJson<Meta>(e.Metadata.ToString()); } catch { }
            return new Listed
            {
                Id = e.PlayerId,
                Rank = e.Rank + 1,
                Name = NameFilter.Clean(m?.crew, "Unnamed team"),
                Country = Country.Normalize(m?.country),
                Avatar = m != null ? m.avatar : 0,
                Members = m != null ? m.members : 0,
                Points = (long)e.Score,
            };
        }
    }
}
