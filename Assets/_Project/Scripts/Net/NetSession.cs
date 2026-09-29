using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using UnityEngine;
using FishNet.Connection;
using FishNet.Managing;
using FishNet.Managing.Object;
using FishNet.Object;
using FishNet.Transporting;
using FishNet.Transporting.Tugboat;
using ProjectFossil.Core;
using ProjectFossil.Dinosaurs;
using ProjectFossil.Match;

namespace ProjectFossil.Net
{
    // Online co-op, step 1: one player hosts, friends join by address (same Mac, same Wi-Fi, or a forwarded port).
    //
    // The host is the authority: it generates the island, runs the director, wildlife and every animal's brain,
    // and shares the animals with the team. A joiner is sent only the seed and the match clock and builds the
    // same island itself (the island is never sent). Each machine keeps its own local player, loot, coins and
    // score; teammates appear as NetAvatars that follow those players around.
    //
    // Added automatically next to the MatchBootstrap: it holds the match at a small start menu
    // (Solo / Host / Join) until a choice is made.
    public class NetSession : MonoBehaviour
    {
        public const ushort Port       = 7770;
        public const int    MaxTeam    = 4;
        private const string AddressKey = "ProjectFossil.JoinAddress";
        private const string PrefabsPath = "Net/NetworkPrefabs";

        public static NetSession Instance { get; private set; }

        private enum Mode { Menu, Solo, StartingHost, Hosting, Joining, Joined }

        public MatchManager Match => _boot != null ? _boot.Match : null;
        public bool IsOnline => _mode == Mode.Hosting || _mode == Mode.Joined || _mode == Mode.StartingHost || _mode == Mode.Joining;

        private MatchBootstrap _boot;
        private GameObject     _soloDinosaur;
        private NetworkManager _net;
        private PrefabObjects  _prefabs;
        private NetworkObject  _avatarPrefab, _dinosaurPrefab;
        private Mode   _mode = Mode.Menu;
        private string _address;
        private string _status;
        private string _lanAddress;

        private int  _seed;              // the island this machine is on (online)
        private int  _readySeed = -1;    // the island this machine last told the host it was standing on
        private bool _clientLoaded;
        private bool _hooked;            // subscribed to the MatchManager's team events
        private bool _warnedPrefab;

        private readonly Dictionary<int, NetAvatar> _avatars = new Dictionary<int, NetAvatar>(); // host: by client id

        private GUIStyle _title, _button, _label, _field, _hud;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoCreate()
        {
            var boot = FindFirstObjectByType<MatchBootstrap>();
            if (boot == null || boot.GetComponent<NetSession>() != null) return;
            boot.holdStart = true; // runs after Awake and before Start, so the match waits for the menu
            boot.gameObject.AddComponent<NetSession>();
        }

        private void Awake()
        {
            Instance = this;
            _boot = GetComponent<MatchBootstrap>();
            _soloDinosaur = _boot != null ? _boot.dinosaurPrefab : null;
            _address = PlayerPrefs.GetString(AddressKey, "127.0.0.1");
            NetRole.IsFollower = false;
        }

        private void OnEnable()
        {
            DinosaurAI.Created += OnDinosaurCreated;
            DinosaurAI.Damaged += OnDinosaurDamaged;
            DinosaurAI.Killed  += OnDinosaurKilled;
            if (_boot != null) _boot.Generated += OnGenerated;
        }

        private void OnDisable()
        {
            DinosaurAI.Created -= OnDinosaurCreated;
            DinosaurAI.Damaged -= OnDinosaurDamaged;
            DinosaurAI.Killed  -= OnDinosaurKilled;
            if (_boot != null) _boot.Generated -= OnGenerated;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            NetRole.IsFollower = false;
            if (_net != null) Destroy(_net.gameObject);
        }

        // ── Menu choices ───────────────────────────────────────────────────────

        private void PlaySolo()
        {
            StopNetwork();
            NetRole.IsFollower = false;
            _boot.dinosaurPrefab = _soloDinosaur;
            _boot.CanRestart = null;
            _boot.RestartNote = null;
            _boot.spawnOffset = Vector3.zero;
            _mode = Mode.Solo;
            _boot.StartSolo();
        }

        private void Host()
        {
            if (!EnsureNetwork()) return;
            NetRole.IsFollower = false;
            _boot.dinosaurPrefab = _dinosaurPrefab.gameObject;
            _boot.CanRestart = null;
            _boot.RestartNote = "A new island takes your whole team with you.";
            _boot.spawnOffset = Vector3.zero;
            _mode = Mode.StartingHost;
            _status = "Starting the host...";
            _lanAddress = LocalAddress();
            if (!_net.ServerManager.StartConnection(Port))
            {
                _status = $"Couldn't open port {Port}. Is another copy already hosting?";
                _mode = Mode.Menu;
            }
            // The island is generated once the server is up (OnServerState): an animal spawned before then
            // couldn't be shared.
        }

        private void Join()
        {
            if (!EnsureNetwork()) return;
            _address = string.IsNullOrWhiteSpace(_address) ? "127.0.0.1" : _address.Trim();
            PlayerPrefs.SetString(AddressKey, _address);
            NetRole.IsFollower = true; // animals that arrive from now on are the host's
            _boot.CanRestart = () => false;
            _boot.RestartNote = "Waiting for the host to start the next island...";
            _mode = Mode.Joining;
            _status = $"Connecting to {_address}...";
            _clientLoaded = false;
            _readySeed = -1;
            if (!_net.ClientManager.StartConnection(_address, Port))
            {
                _status = "Couldn't start connecting.";
                BackToMenu(_status);
            }
        }

        private void BackToMenu(string why)
        {
            NetRole.IsFollower = false;
            _boot.CanRestart = null;
            _boot.RestartNote = null;
            _mode = Mode.Menu;
            _status = why;
        }

        // ── Network setup ──────────────────────────────────────────────────────

        private bool EnsureNetwork()
        {
            if (_net != null) return true;

            _prefabs = Resources.Load<PrefabObjects>(PrefabsPath);
            if (_prefabs != null)
            {
                foreach (var nob in PrefabList(_prefabs))
                {
                    if (nob == null) continue;
                    if (nob.GetComponent<NetAvatar>() != null) _avatarPrefab = nob;
                    if (nob.GetComponent<NetDinosaur>() != null) _dinosaurPrefab = nob;
                }
            }
            if (_avatarPrefab == null || _dinosaurPrefab == null)
            {
                _status = "Co-op isn't set up yet. In Unity run Project Fossil > Co-op > Set Up Networking, then press Play again.";
                return false;
            }

            // Both copies keep simulating while the other window has focus (host in the Editor, friend in a build).
            Application.runInBackground = true;

            // Built inactive so the prefab list is in place before the NetworkManager wakes up.
            var go = new GameObject("Network");
            go.SetActive(false);
            DontDestroyOnLoad(go);
            var tugboat = go.AddComponent<Tugboat>();
            tugboat.SetPort(Port);
            tugboat.SetMaximumClients(MaxTeam);
            _net = go.AddComponent<NetworkManager>();
            _net.SpawnablePrefabs = _prefabs;
            go.SetActive(true);

            _net.ServerManager.OnServerConnectionState += OnServerState;
            _net.ClientManager.OnClientConnectionState += OnClientState;
            _net.SceneManager.OnClientLoadedStartScenes += OnLoadedStartScenes;
            _net.ServerManager.OnRemoteConnectionState  += OnRemoteState;

            _net.ServerManager.RegisterBroadcast<ReadyMessage>(OnReady);
            _net.ServerManager.RegisterBroadcast<FinalStandMessage>(OnFinalStandRequest);
            _net.ClientManager.RegisterBroadcast<IslandMessage>(OnIsland);
            _net.ClientManager.RegisterBroadcast<AnnounceMessage>(OnAnnounce);
            return true;
        }

        private static IEnumerable<NetworkObject> PrefabList(PrefabObjects prefabs)
        {
            if (prefabs is SinglePrefabObjects single) return single.Prefabs;
            var list = new List<NetworkObject>();
            for (int i = 0; i < prefabs.GetObjectCount(); i++) list.Add(prefabs.GetObject(true, i));
            return list;
        }

        private void StopNetwork()
        {
            if (_net == null) return;
            if (_net.ClientManager.Started) _net.ClientManager.StopConnection();
            if (_net.ServerManager.Started) _net.ServerManager.StopConnection(true);
        }

        // ── Connection events ──────────────────────────────────────────────────

        private void OnServerState(ServerConnectionStateArgs args)
        {
            if (args.ConnectionState == LocalConnectionState.Started && _mode == Mode.StartingHost)
            {
                _mode = Mode.Hosting;
                _status = null;
                _net.ClientManager.StartConnection("localhost", Port); // the host plays too
                _boot.StartSolo(); // random island; OnGenerated shares it
            }
            else if (args.ConnectionState == LocalConnectionState.Stopped && _mode == Mode.StartingHost)
            {
                BackToMenu($"Couldn't host on port {Port}.");
            }
        }

        private void OnClientState(ClientConnectionStateArgs args)
        {
            if (args.ConnectionState == LocalConnectionState.Started && _mode == Mode.Joining)
                _status = "Connected. Waiting for the island...";
            else if (args.ConnectionState == LocalConnectionState.Stopped && (_mode == Mode.Joining || _mode == Mode.Joined))
            {
                _clientLoaded = false;
                BackToMenu(_mode == Mode.Joined ? "Lost the host. Play solo, or join again." : $"Couldn't reach {_address}.");
            }
        }

        private void OnRemoteState(NetworkConnection conn, RemoteConnectionStateArgs args)
        {
            if (args.ConnectionState != RemoteConnectionState.Stopped) return;
            if (_avatars.TryGetValue(conn.ClientId, out var body))
            {
                _avatars.Remove(conn.ClientId);
                if (Match != null && body != null) Match.Teammates.Remove(body.transform);
            }
            if (Match != null && _mode == Mode.Hosting) Match.Announce("A teammate left the island.");
        }

        private void OnLoadedStartScenes(NetworkConnection conn, bool asServer)
        {
            if (!asServer) { _clientLoaded = true; return; }
            if (IsHostsOwn(conn) || Match == null || Match.State == null) return;
            _net.ServerManager.Broadcast(conn, new IslandMessage { Seed = _seed, Elapsed = Match.State.Elapsed });
        }

        // ── Island ─────────────────────────────────────────────────────────────

        // Host: every new island (the first one and each restart) goes to the whole team.
        private void OnGenerated(int seed)
        {
            _seed = seed;
            HookMatch();
            if (_mode != Mode.Hosting || _net == null || !_net.ServerManager.Started) return;

            _net.ServerManager.Broadcast(new IslandMessage { Seed = seed, Elapsed = 0f });
            foreach (var pair in _avatars)
                if (pair.Value != null && pair.Value.IsAlive && !IsHostsOwn(pair.Value.Owner))
                    Match.Teammates.Add(pair.Value.transform);
            Match.Announce($"Hosting. Friends join at {_lanAddress ?? "this computer's address"} (port {Port}).");
        }

        // Joiner: build the host's island and catch up with its clock.
        private void OnIsland(IslandMessage msg, Channel channel)
        {
            if (_net.IsServerStarted) return; // the host built it
            NetRole.IsFollower = true;
            _mode = Mode.Joined;
            _status = null;
            _seed = msg.Seed;
            int id = _net.ClientManager.Connection != null ? _net.ClientManager.Connection.ClientId : 1;
            float angle = id * 90f * Mathf.Deg2Rad;
            _boot.spawnOffset = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * 2.5f;
            _boot.GenerateAndSpawn(msg.Seed);
            if (Match != null && Match.State != null) Match.State.FastForward(msg.Elapsed);
            _readySeed = -1;
        }

        private void HookMatch()
        {
            if (_hooked || Match == null) return;
            _hooked = true;
            Match.TeamAnnounced       += OnTeamAnnounced;
            Match.FinalStandRequested += OnFinalStandRequested;
        }

        private void Update()
        {
            // Tell the host once this machine is standing on the current island (it then sends this player's body).
            if (_net == null || !_net.ClientManager.Started || !_clientLoaded) return;
            if (Match == null || Match.Player == null || _readySeed == _seed) return;
            if (_mode != Mode.Hosting && _mode != Mode.Joined) return;
            _readySeed = _seed;
            _net.ClientManager.Broadcast(new ReadyMessage { Seed = _seed });
        }

        // ── Host: bodies ───────────────────────────────────────────────────────

        private void OnReady(NetworkConnection conn, ReadyMessage msg, Channel channel)
        {
            if (msg.Seed != _seed || Match == null) return; // from an island we've already left

            if (_avatars.TryGetValue(conn.ClientId, out var body) && body != null)
            {
                if (body.IsAlive) return; // still out there: it keeps following its player
                if (body.IsSpawned) body.Despawn();
            }

            Vector3 pos = Match.Player != null ? Match.Player.transform.position : Vector3.zero;
            var go = Instantiate(_avatarPrefab.gameObject, pos, Quaternion.identity);
            go.name = $"Teammate_{conn.ClientId}";
            _net.ServerManager.Spawn(go, conn);
            body = go.GetComponent<NetAvatar>();
            _avatars[conn.ClientId] = body;
            if (!IsHostsOwn(conn))
            {
                Match.Teammates.Add(body.transform);
                Match.Announce($"{body.Label} dropped in with you.");
            }
        }

        // The host's own player (its local client), as opposed to a teammate who joined.
        private bool IsHostsOwn(NetworkConnection conn)
        {
            var local = _net != null && _net.ClientManager.Started ? _net.ClientManager.Connection : null;
            return conn != null && local != null && conn.ClientId == local.ClientId;
        }

        public NetAvatar AvatarOf(NetworkConnection conn)
        {
            if (conn == null) return null;
            return _avatars.TryGetValue(conn.ClientId, out var body) ? body : null;
        }

        public void OnAvatarLeft(NetAvatar body, bool extracted)
        {
            if (Match == null || body == null) return;
            Match.Teammates.Remove(body.transform);
            if (!IsHostsOwn(body.Owner))
                Match.Announce(extracted ? $"{body.Label} made it out!" : $"{body.Label} is down.");
        }

        private void OnFinalStandRequest(NetworkConnection conn, FinalStandMessage msg, Channel channel)
        {
            var body = AvatarOf(conn);
            if (Match != null && body != null) Match.BeginFinalStandFor(body.transform);
        }

        // ── Host: animals ──────────────────────────────────────────────────────

        private void OnDinosaurCreated(DinosaurAI ai)
        {
            if (_net == null || !_net.IsServerStarted) return;
            var shared = ai.GetComponent<NetDinosaur>();
            if (shared == null)
            {
                if (!_warnedPrefab) Debug.LogWarning($"[NetSession] {ai.name} has no NetDinosaur, so teammates won't see it.", ai);
                _warnedPrefab = true;
                return;
            }
            shared.PrepareForSpawn();
            _net.ServerManager.Spawn(ai.gameObject);
        }

        // A teammate's hit on one of the host's animals: their score and coins, on their machine.
        private void OnDinosaurDamaged(DinosaurAI dino, DamageInfo info)
        {
            var body = TeammateBody(info.Source);
            if (body != null) body.OwnerCreditDamage(body.Owner, info.Amount);
        }

        private void OnDinosaurKilled(DinosaurAI dino, GameObject killer)
        {
            var body = TeammateBody(killer);
            if (body != null) body.OwnerCreditKill(body.Owner, dino.species != null ? dino.species.name : "");
        }

        private NetAvatar TeammateBody(GameObject source)
        {
            if (_net == null || !_net.IsServerStarted || source == null) return null;
            var body = source.GetComponent<NetAvatar>();
            return body != null && body.IsSpawned && !IsHostsOwn(body.Owner) ? body : null;
        }

        // ── Team messages ──────────────────────────────────────────────────────

        private void OnTeamAnnounced(string text)
        {
            if (_mode == Mode.Hosting && _net != null && _net.ServerManager.Started && !string.IsNullOrEmpty(text))
                _net.ServerManager.Broadcast(new AnnounceMessage { Text = text });
        }

        private void OnAnnounce(AnnounceMessage msg, Channel channel)
        {
            if (_net.IsServerStarted) return; // the host announced it already
            if (Match != null) Match.Announce(msg.Text);
        }

        private void OnFinalStandRequested()
        {
            if (_mode == Mode.Joined && _net != null && _net.ClientManager.Started)
                _net.ClientManager.Broadcast(new FinalStandMessage());
        }

        // ── Menu and HUD ───────────────────────────────────────────────────────

        private void OnGUI()
        {
            EnsureStyles();
            if (_mode == Mode.Menu || _mode == Mode.Joining || _mode == Mode.StartingHost) DrawMenu();
            else if (_mode == Mode.Hosting || _mode == Mode.Joined) DrawTeamLine();
        }

        private void DrawMenu()
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible   = true;
            if (Match != null && Match.PlayerController != null) Match.PlayerController.InputBlocked = true;

            GUI.color = new Color(0f, 0f, 0f, 0.55f);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = Color.white;

            var area = new Rect(Screen.width * 0.5f - 200, Screen.height * 0.5f - 190, 400, 380);
            GUILayout.BeginArea(area, GUI.skin.box);
            GUILayout.Label("PROJECT FOSSIL", _title);
            GUILayout.Space(10);

            bool busy = _mode != Mode.Menu;
            GUI.enabled = !busy;
            if (GUILayout.Button("Play solo", _button, GUILayout.Height(40))) PlaySolo();
            GUILayout.Space(6);
            if (GUILayout.Button("Host a co-op game", _button, GUILayout.Height(40))) Host();
            GUILayout.Space(10);
            GUILayout.Label("Join a friend (their address):", _label);
            GUILayout.BeginHorizontal();
            _address = GUILayout.TextField(_address ?? "", 64, _field, GUILayout.Height(32));
            if (GUILayout.Button("Join", _button, GUILayout.Width(90), GUILayout.Height(32))) Join();
            GUILayout.EndHorizontal();
            GUI.enabled = true;

            GUILayout.Space(8);
            GUILayout.Label("Same computer: 127.0.0.1. Same Wi-Fi: the address the host sees at the top of their screen.", _label);
            if (!string.IsNullOrEmpty(_status))
            {
                GUILayout.Space(6);
                GUILayout.Label(_status, _label);
            }
            if (_mode == Mode.Joining && GUILayout.Button("Cancel", GUILayout.Height(26)))
            {
                StopNetwork();
                BackToMenu(null);
            }
            GUILayout.EndArea();
        }

        private void DrawTeamLine()
        {
            int team = _mode == Mode.Hosting ? _avatars.Count : NetAvatar.All.Count;
            string text = _mode == Mode.Hosting
                ? $"Hosting at {_lanAddress ?? "?"}  |  team of {Mathf.Max(1, team)}"
                : $"Co-op with {_address}  |  team of {Mathf.Max(1, team)}";
            GUI.Label(new Rect(Screen.width * 0.5f - 200, 4, 400, 22), text, _hud);
        }

        private void EnsureStyles()
        {
            if (_title != null) return;
            _title  = new GUIStyle(GUI.skin.label)  { fontSize = 26, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            _button = new GUIStyle(GUI.skin.button) { fontSize = 16 };
            _label  = new GUIStyle(GUI.skin.label)  { fontSize = 13, wordWrap = true };
            _field  = new GUIStyle(GUI.skin.textField) { fontSize = 16, alignment = TextAnchor.MiddleLeft };
            _hud    = new GUIStyle(GUI.skin.label)  { fontSize = 12, alignment = TextAnchor.MiddleCenter };
            _title.normal.textColor = new Color(1f, 0.85f, 0.45f);
        }

        // This computer's address on the local network, for a friend on the same Wi-Fi to type in.
        private static string LocalAddress()
        {
            try
            {
                using (var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, 0))
                {
                    socket.Connect("8.8.8.8", 65530); // picks the outgoing interface; nothing is sent
                    if (socket.LocalEndPoint is IPEndPoint ep) return ep.Address.ToString();
                }
            }
            catch { /* offline: fall through */ }
            try
            {
                foreach (var ip in Dns.GetHostEntry(Dns.GetHostName()).AddressList)
                    if (ip.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(ip)) return ip.ToString();
            }
            catch { }
            return "127.0.0.1";
        }
    }
}
