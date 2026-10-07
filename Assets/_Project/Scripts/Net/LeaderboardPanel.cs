using System.Collections.Generic;
using ProjectFossil.Match;
using UnityEngine;

namespace ProjectFossil.Net
{
    // The start menu's leaderboard: the best survivors and the best crews, worldwide (OnlineLeaderboard) or on
    // this computer (Leaderboard.Local).
    public static class LeaderboardPanel
    {
        private const int Shown = 10;
        private static int _tab;            // 0 survivors, 1 crews
        private static bool _online = true; // worldwide, or this computer
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
            if (GUILayout.Toggle(_tab == 1, "Crews", GUI.skin.button, GUILayout.Height(30))) _tab = 1;
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Toggle(_online, "Worldwide", GUI.skin.button, GUILayout.Height(24))) _online = true;
            if (GUILayout.Toggle(!_online, "This computer", GUI.skin.button, GUILayout.Height(24))) _online = false;
            GUILayout.EndHorizontal();
            GUILayout.Space(6);

            List<RunRecord> rows;
            if (_online)
            {
                OnlineLeaderboard.Refresh(); // at most every 20 seconds
                var all = _tab == 0 ? OnlineLeaderboard.Survivors : OnlineLeaderboard.Crews;
                rows = all.GetRange(0, Mathf.Min(Shown, all.Count));
            }
            else
            {
                var board = Leaderboard.Local;
                rows = _tab == 0 ? board.TopSurvivors(Shown) : board.TopCrews(Shown);
            }
            GUILayout.BeginHorizontal();
            GUILayout.Label("#", _head, GUILayout.Width(28));
            GUILayout.Label(_tab == 0 ? "Survivor" : "Crew", _head, GUILayout.Width(_tab == 0 ? 130 : 170));
            GUILayout.Label(_tab == 0 ? "Crew" : "Best run by", _head, GUILayout.Width(_tab == 0 ? 150 : 110));
            GUILayout.Label("Score", _head, GUILayout.Width(70));
            GUILayout.Label("Result", _head);
            GUILayout.EndHorizontal();

            if (rows.Count == 0)
            {
                GUILayout.Space(20);
                GUILayout.Label(_online && OnlineLeaderboard.Busy ? "Loading..." : "No runs yet. Finish a match to get on the board.", _small);
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

            GUILayout.FlexibleSpace();
            GUILayout.Label(_online ? OnlineLeaderboard.Status : "Runs played on this computer.", _small);
            if (GUILayout.Button("Back", GUILayout.Height(32))) back = true;
            GUILayout.EndArea();
            return back;
        }

        private static string Outcome(RunRecord r)
        {
            string how = r.Result == MatchResult.Extracted ? "Got out" : r.Result == MatchResult.Died ? "Fell" : "Stranded";
            int m = Mathf.FloorToInt(r.Seconds / 60f), s = Mathf.FloorToInt(r.Seconds % 60f);
            string crew = r.CrewSize > 1 ? $", crew of {r.CrewSize}" : "";
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
