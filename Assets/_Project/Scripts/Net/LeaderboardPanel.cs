using System.Collections.Generic;
using ProjectFossil.Match;
using UnityEngine;

namespace ProjectFossil.Net
{
    // The start menu's leaderboard. Survivors (each player's points, added up over every online match) or Teams
    // (each team's points with the escape bonus), shown All time, This week or for the player's country, worldwide
    // (OnlineLeaderboard); or This computer, the best single runs played here, offline included (Leaderboard.Local).
    public static class LeaderboardPanel
    {
        private const int Shown = 10;
        private enum Scope { AllTime, Week, Country, Local }
        private static int _tab;                    // 0 survivors, 1 teams
        private static Scope _scope = Scope.AllTime;
        private static GUIStyle _title, _head, _row, _mine, _small;

        // Returns true when the player presses Back. Rows for this player and crew are highlighted.
        public static bool Draw(Rect area, string player, string crew)
        {
            EnsureStyles();
            bool back = false;
            GUILayout.BeginArea(area, GUI.skin.box);
            GUILayout.Label("LEADERBOARD", _title);
            GUILayout.BeginHorizontal();
            if (GUILayout.Toggle(_tab == 0, "Survivors", GUI.skin.button, GUILayout.Height(30))) _tab = 0;
            if (GUILayout.Toggle(_tab == 1, "Teams", GUI.skin.button, GUILayout.Height(30))) _tab = 1;
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            ScopeButton(Scope.AllTime, "All time");
            ScopeButton(Scope.Week, "This week");
            ScopeButton(Scope.Country, OnlineLeaderboard.PlayerCountry == Country.Unknown ? "My country"
                                       : OnlineLeaderboard.CountryName(OnlineLeaderboard.PlayerCountry));
            ScopeButton(Scope.Local, "This computer");
            GUILayout.EndHorizontal();
            GUILayout.Space(6);

            if (_scope == Scope.Local) DrawLocal(player, crew);
            else DrawOnline(player, crew);

            GUILayout.FlexibleSpace();
            GUILayout.Label(_scope == Scope.Local ? "Best single runs played on this computer, offline included."
                            : _scope == Scope.Country && !OnlineLeaderboard.HasCountryBoard(OnlineLeaderboard.PlayerCountry)
                                ? "No board for your country yet: set it on the Play Online page, or see All time."
                                : OnlineLeaderboard.Status, _small);
            if (GUILayout.Button("Back", GUILayout.Height(32))) back = true;
            GUILayout.EndArea();
            return back;
        }

        private static void ScopeButton(Scope s, string label)
        {
            if (GUILayout.Toggle(_scope == s, label, GUI.skin.button, GUILayout.Height(24))) _scope = s;
        }

        private static void DrawOnline(string player, string crew)
        {
            OnlineLeaderboard.Refresh(); // at most every 20 seconds
            var board = _tab == 1
                ? (_scope == Scope.Week ? OnlineLeaderboard.Board.TeamsWeek
                 : _scope == Scope.Country ? OnlineLeaderboard.Board.TeamsCountry : OnlineLeaderboard.Board.Teams)
                : _scope == Scope.Week ? OnlineLeaderboard.Board.SurvivorsWeek
                : _scope == Scope.Country ? OnlineLeaderboard.Board.SurvivorsCountry
                : OnlineLeaderboard.Board.Survivors;
            var rows = OnlineLeaderboard.Get(board);

            GUILayout.BeginHorizontal();
            GUILayout.Label("#", _head, GUILayout.Width(34));
            GUILayout.Label(_tab == 0 ? "Survivor" : "Team", _head, GUILayout.Width(170));
            GUILayout.Label(_tab == 0 ? "Team" : "Members", _head, GUILayout.Width(150));
            GUILayout.Label("Points", _head);
            GUILayout.EndHorizontal();
            if (rows.Count == 0)
            {
                GUILayout.Space(20);
                GUILayout.Label(OnlineLeaderboard.Busy ? "Loading..." : "No one here yet. Finish an online match to get on the board.", _small);
            }
            for (int i = 0; i < rows.Count && i < Shown; i++)
            {
                var r = rows[i];
                var style = (_tab == 0 ? Same(r.Name, player) : Same(r.Crew, crew)) ? _mine : _row;
                GUILayout.BeginHorizontal();
                GUILayout.Label(r.Rank.ToString(), style, GUILayout.Width(34));
                if (_tab == 1)
                {
                    var badge = GUILayoutUtility.GetRect(20, 20, GUILayout.Width(20), GUILayout.Height(20));
                    TeamEmblem.Draw(badge, r.Avatar);
                }
                GUILayout.Label(r.Name, style, GUILayout.Width(_tab == 1 ? 150 : 170));
                GUILayout.Label(_tab == 0 ? r.Crew : (r.Members > 0 ? r.Members.ToString() : ""), style, GUILayout.Width(150));
                GUILayout.Label(r.Score.ToString("N0"), style);
                GUILayout.EndHorizontal();
            }
            if (!string.IsNullOrEmpty(OnlineLeaderboard.LastResult)) GUILayout.Label("Last match: " + OnlineLeaderboard.LastResult, _small);
        }

        private static void DrawLocal(string player, string crew)
        {
            var board = Leaderboard.Local;
            var rows = _tab == 0 ? board.TopSurvivors(Shown) : board.TopCrews(Shown);
            GUILayout.BeginHorizontal();
            GUILayout.Label("#", _head, GUILayout.Width(28));
            GUILayout.Label(_tab == 0 ? "Survivor" : "Team", _head, GUILayout.Width(_tab == 0 ? 130 : 170));
            GUILayout.Label(_tab == 0 ? "Team" : "Best run by", _head, GUILayout.Width(_tab == 0 ? 150 : 110));
            GUILayout.Label("Score", _head, GUILayout.Width(70));
            GUILayout.Label("Result", _head);
            GUILayout.EndHorizontal();
            if (rows.Count == 0)
            {
                GUILayout.Space(20);
                GUILayout.Label("No runs yet. Finish a match to get on the board.", _small);
            }
            for (int i = 0; i < rows.Count; i++)
            {
                var r = rows[i];
                bool me = _tab == 0 ? Same(r.Player, player) : Same(r.Crew, crew);
                var style = me ? _mine : _row;
                GUILayout.BeginHorizontal();
                GUILayout.Label((i + 1).ToString(), style, GUILayout.Width(28));
                GUILayout.Label(_tab == 0 ? r.Player : r.Crew, style, GUILayout.Width(_tab == 0 ? 130 : 170));
                GUILayout.Label(_tab == 0 ? r.Crew : r.Player, style, GUILayout.Width(_tab == 0 ? 150 : 110));
                GUILayout.Label(r.Score.ToString(), style, GUILayout.Width(70));
                GUILayout.Label(Outcome(r), style);
                GUILayout.EndHorizontal();
            }
        }

        private static string Outcome(RunRecord r)
        {
            string how = r.Result == MatchResult.Extracted ? "Got out" : r.Result == MatchResult.Died ? "Fell" : "Stranded";
            int m = Mathf.FloorToInt(r.Seconds / 60f), s = Mathf.FloorToInt(r.Seconds % 60f);
            string crew = r.CrewSize > 1 ? $", team of {r.CrewSize}" : "";
            return $"{how}  {m}:{s:00}{crew}";
        }

        private static bool Same(string a, string b) =>
            !string.IsNullOrEmpty(a) && string.Equals(a.Trim(), b?.Trim(), System.StringComparison.OrdinalIgnoreCase);

        private static void EnsureStyles()
        {
            if (_title != null) return;
            _title = new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            _title.normal.textColor = new Color(1f, 0.85f, 0.45f);
            _head = new GUIStyle(GUI.skin.label) { fontSize = 12, fontStyle = FontStyle.Bold };
            _head.normal.textColor = new Color(1f, 1f, 1f, 0.6f);
            _row = new GUIStyle(GUI.skin.label) { fontSize = 14, clipping = TextClipping.Clip };
            _mine = new GUIStyle(_row) { fontStyle = FontStyle.Bold };
            _mine.normal.textColor = new Color(1f, 0.85f, 0.45f);
            _small = new GUIStyle(GUI.skin.label) { fontSize = 12, wordWrap = true, alignment = TextAnchor.MiddleCenter };
        }
    }
}
