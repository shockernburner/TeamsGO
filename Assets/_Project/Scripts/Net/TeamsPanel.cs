using ProjectFossil.Match;
using UnityEngine;

namespace ProjectFossil.Net
{
    // The start menu's teams page (online): your team and its members, finding a team to join (your country's, the
    // top teams, or by name), and founding one with a name and an emblem. Leaving or joining another is one click.
    public static class TeamsPanel
    {
        private enum Tab { Mine, Find, Create }
        private static Tab _tab = Tab.Mine;
        private static Teams.Scope _scope = Teams.Scope.MyCountry;
        private static string _query = "", _newName;
        private static int _newEmblem;
        private static bool _searched;
        private static Vector2 _scroll;
        private static GUIStyle _title, _head, _row, _small, _big;

        // Returns true when the player presses Back.
        public static bool Draw(Rect area)
        {
            EnsureStyles();
            Teams.EnsureKnown();
            bool back = false;
            GUILayout.BeginArea(area, GUI.skin.box);
            GUILayout.Label("TEAMS", _title);
            GUILayout.BeginHorizontal();
            TabButton(Tab.Mine, Teams.Mine != null ? "My team" : "No team yet");
            TabButton(Tab.Find, "Find a team");
            TabButton(Tab.Create, "Create a team");
            GUILayout.EndHorizontal();
            GUILayout.Space(6);

            GUI.enabled = !Teams.Busy;
            if (_tab == Tab.Mine) DrawMine();
            else if (_tab == Tab.Find) DrawFind();
            else DrawCreate();
            GUI.enabled = true;

            GUILayout.FlexibleSpace();
            if (!string.IsNullOrEmpty(Teams.Status)) GUILayout.Label(Teams.Status, _small);
            if (GUILayout.Button("Back", GUILayout.Height(32))) back = true;
            GUILayout.EndArea();
            return back;
        }

        private static void TabButton(Tab t, string label)
        {
            if (GUILayout.Toggle(_tab == t, label, GUI.skin.button, GUILayout.Height(30)) && _tab != t) { _tab = t; _searched = false; }
        }

        private static void DrawMine()
        {
            var team = Teams.Mine;
            if (!Teams.Known) { GUILayout.Label(Teams.Busy ? "Loading..." : "Not loaded yet.", _small); return; }
            if (team == null)
            {
                GUILayout.Label("You're not in a team. Online matches count for the host's team, and your team's points " +
                                "go on the Teams board. Find one to join, or create your own.", _small);
                GUILayout.Space(8);
                if (GUILayout.Button("Find a team", GUILayout.Height(34))) _tab = Tab.Find;
                if (GUILayout.Button("Create a team", GUILayout.Height(34))) _tab = Tab.Create;
                return;
            }
            GUILayout.BeginHorizontal();
            var badge = GUILayoutUtility.GetRect(64, 64, GUILayout.Width(64), GUILayout.Height(64));
            TeamEmblem.Draw(badge, team.avatar);
            GUILayout.BeginVertical();
            GUILayout.Label(team.name, _big);
            GUILayout.Label($"{OnlineLeaderboard.CountryName(team.country)}  -  {team.members.Count} of {Teams.MaxMembers} members", _row);
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();
            GUILayout.Space(6);
            GUILayout.Label("MEMBERS", _head);
            foreach (var m in team.members)
                GUILayout.Label((m.id == Account.PlayerId ? "> " : "   ") + NameFilter.Clean(m.name, "Survivor"), _row);
            GUILayout.Space(8);
            if (GUILayout.Button("Leave this team", GUILayout.Height(30))) Teams.Leave();
        }

        private static void DrawFind()
        {
            GUILayout.BeginHorizontal();
            string country = OnlineLeaderboard.PlayerCountry;
            ScopeButton(Teams.Scope.MyCountry, country == Country.Unknown ? "My country" : OnlineLeaderboard.CountryName(country));
            ScopeButton(Teams.Scope.Top, "Top teams");
            ScopeButton(Teams.Scope.Name, "By name");
            GUILayout.EndHorizontal();
            if (_scope == Teams.Scope.Name)
            {
                GUILayout.BeginHorizontal();
                _query = GUILayout.TextField(_query ?? "", 24, GUILayout.Height(28));
                if (GUILayout.Button("Search", GUILayout.Width(80), GUILayout.Height(28))) { Teams.Find(_scope, _query); _searched = true; }
                GUILayout.EndHorizontal();
            }
            else if (!_searched) { Teams.Find(_scope, null); _searched = true; }

            if (Teams.Finding) { GUILayout.Label("Looking...", _small); return; }
            _scroll = GUILayout.BeginScrollView(_scroll, GUILayout.Height(250));
            foreach (var t in Teams.Found)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(t.Rank.ToString(), _row, GUILayout.Width(28), GUILayout.Height(30));
                var badge = GUILayoutUtility.GetRect(28, 28, GUILayout.Width(28), GUILayout.Height(28));
                TeamEmblem.Draw(badge, t.Avatar);
                GUILayout.Label(t.Name, _row, GUILayout.Width(160), GUILayout.Height(30));
                GUILayout.Label(t.Country == Country.Unknown ? "" : t.Country, _row, GUILayout.Width(30), GUILayout.Height(30));
                GUILayout.Label($"{t.Members}/{Teams.MaxMembers}", _row, GUILayout.Width(40), GUILayout.Height(30));
                GUILayout.Label(t.Points.ToString("N0"), _row, GUILayout.Width(70), GUILayout.Height(30));
                bool mine = Teams.Mine != null && Teams.Mine.id == t.Id;
                GUI.enabled = !Teams.Busy && !mine && t.Members < Teams.MaxMembers;
                if (GUILayout.Button(mine ? "Yours" : t.Members >= Teams.MaxMembers ? "Full" : "Join", GUILayout.Width(60), GUILayout.Height(28)))
                    Teams.Join(t.Id, () => _tab = Tab.Mine);
                GUI.enabled = !Teams.Busy;
                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();
            if (Teams.Mine != null) GUILayout.Label($"Joining another team leaves {Teams.Mine.name}.", _small);
        }

        private static void ScopeButton(Teams.Scope s, string label)
        {
            if (GUILayout.Toggle(_scope == s, label, GUI.skin.button, GUILayout.Height(26)) && _scope != s)
            {
                _scope = s;
                _searched = false;
                Teams.Find(s, s == Teams.Scope.Name ? _query : null);
                _searched = s != Teams.Scope.Name || !string.IsNullOrEmpty(_query);
            }
        }

        private static void DrawCreate()
        {
            if (_newName == null) _newName = CrewNames.Random(new System.Random());
            GUILayout.BeginHorizontal();
            GUILayout.Label("Name:", _row, GUILayout.Width(60), GUILayout.Height(30));
            _newName = GUILayout.TextField(_newName, 24, GUILayout.Height(30));
            if (GUILayout.Button("Random", GUILayout.Width(70), GUILayout.Height(30))) _newName = CrewNames.Random(new System.Random());
            GUILayout.EndHorizontal();
            GUILayout.Label("EMBLEM", _head);
            for (int row = 0; row < 2; row++)
            {
                GUILayout.BeginHorizontal();
                for (int i = row * 6; i < row * 6 + 6; i++)
                {
                    var r = GUILayoutUtility.GetRect(52, 52, GUILayout.Width(52), GUILayout.Height(52));
                    if (i == _newEmblem)
                    {
                        GUI.color = new Color(1f, 0.85f, 0.45f, 0.9f);
                        GUI.DrawTexture(new Rect(r.x - 2, r.y - 2, r.width + 4, r.height + 4), Texture2D.whiteTexture);
                        GUI.color = Color.white;
                    }
                    TeamEmblem.Draw(r, i);
                    if (GUI.Button(r, GUIContent.none, GUIStyle.none)) _newEmblem = i;
                }
                GUILayout.EndHorizontal();
            }
            string country = OnlineLeaderboard.PlayerCountry;
            GUILayout.Label($"Country: {OnlineLeaderboard.CountryName(country)} (yours, from the Play Online page). " +
                            (Teams.Mine != null ? $"Creating a team leaves {Teams.Mine.name}." : ""), _small);
            GUILayout.Space(6);
            string bad = NameFilter.IsOffensive(_newName) ? "Please choose a different name." : null;
            if (bad != null) GUILayout.Label(bad, _small);
            GUI.enabled = !Teams.Busy && bad == null && (_newName ?? "").Trim().Length >= 3;
            if (GUILayout.Button("Create team", GUILayout.Height(36))) Teams.Create(_newName, _newEmblem, () => _tab = Tab.Mine);
            GUI.enabled = !Teams.Busy;
        }

        private static void EnsureStyles()
        {
            if (_title != null) return;
            _title = new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            _title.normal.textColor = new Color(1f, 0.85f, 0.45f);
            _big = new GUIStyle(GUI.skin.label) { fontSize = 20, fontStyle = FontStyle.Bold };
            _head = new GUIStyle(GUI.skin.label) { fontSize = 12, fontStyle = FontStyle.Bold };
            _head.normal.textColor = new Color(1f, 1f, 1f, 0.6f);
            _row = new GUIStyle(GUI.skin.label) { fontSize = 14, clipping = TextClipping.Clip, alignment = TextAnchor.MiddleLeft };
            _small = new GUIStyle(GUI.skin.label) { fontSize = 12, wordWrap = true };
        }
    }
}
