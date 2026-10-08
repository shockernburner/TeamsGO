using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using UnityEngine;
using FishNet.Connection;
using FishNet.Managing;
using FishNet.Managing.Object;
using FishNet.Object;
using FishNet.Transporting;
using FishNet.Transporting.Tugboat;
using FishNet.Transporting.Multipass;
using FishNet.Transporting.UTP;
using Steamworks;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using ProjectFossil.Core;
using ProjectFossil.Dinosaurs;
using ProjectFossil.Match;

namespace ProjectFossil.Net
{
    // Co-op. Online (signed in, scores count): the host gets a short join code from Unity Relay and friends type
    // it in; Unity's servers connect them, no ports to open, on itch and Steam alike, with Vivox voice. Steam
    // builds also take Steam invites. Offline (practice, scores stay on this computer): friends on the same Wi-Fi
    // join by the host's address.
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
        private const string NameKey    = "ProjectFossil.PlayerName";
        private const string CrewKey    = "ProjectFossil.CrewName";
        private const string TimeKey    = "ProjectFossil.DayTime";
        private const string WeatherKey = "ProjectFossil.Weather";
        private const int    MaxName    = 16;
        private const int    MaxCrew    = 24;
        private static readonly string[] Names =
        {
            "Kestrel", "Juniper", "Rook", "Vega", "Ember", "Wren", "Flint", "Cedar", "Lark", "Briar",
            "Sable", "Moss", "Quill", "Onyx", "Fern", "Talon", "Ash", "Reed", "Marlow", "Sage",
        };
        private const string PrefabsPath = "Net/NetworkPrefabs";

        public static NetSession Instance { get; private set; }

        private enum Mode { Menu, Solo, StartingHost, Hosting, Joining, Joined }

        public MatchManager Match => _boot != null ? _boot.Match : null;
        // Proximity voice and dinosaurs that hear it (lives beside this session, works in solo too).
        public VoiceChat Voice => _voice != null ? _voice : (_voice = GetComponent<VoiceChat>() ?? gameObject.AddComponent<VoiceChat>());
        private VoiceChat _voice;
        // What teammates see over this player's head and in team messages.
        public string PlayerName { get; private set; }
        // The crew this player's runs go on the leaderboard under. Joiners play under the host's.
        public string CrewName { get; private set; }
        private string _hostCrew; // joined: the host's crew name
        public bool IsOnline => _mode == Mode.Hosting || _mode == Mode.Joined || _mode == Mode.StartingHost || _mode == Mode.Joining;

        private MatchBootstrap _boot;
        private GameObject     _soloDinosaur;
        private NetworkManager _net;
        // One network per kind of match, rebuilt when the kind changes. Offline: LAN only (Tugboat). Online: Unity
        // Relay (FishyUnityTransport), plus Steam beside it through a Multipass in Steam builds (Relay first, so the
        // host's own player joins through it). Running LAN and Relay side by side left the host's own player
        // half-connected: its body never hid and a friend's never arrived.
        private bool _netOnline;
        private Multipass _multipass;                      // online with Steam only
        private Tugboat _tugboat;
        private UnityTransport _relayNet;
        private FishySteamworks.FishySteamworks _steamNet;
        private bool _online;                              // this match is online: signed in, scores count
        private string _joinCode;                          // online host: the code friends type; everyone: the voice channel
        private string _codeInput = "";
        private CSteamID _steamHost = CSteamID.Nil;        // the friend we joined through Steam
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
        private bool _pickingChallenge;  // "Play solo" pressed: asking Easy, Medium or Hard

        private readonly Dictionary<int, NetAvatar> _avatars = new Dictionary<int, NetAvatar>(); // host: by client id

        private GUIStyle _title, _tagline, _button, _label, _field, _hud;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoCreate()
        {
            var boot = FindAnyObjectByType<MatchBootstrap>();
            if (boot == null || boot.GetComponent<NetSession>() != null) return;
            boot.holdStart = true; // runs after Awake and before Start, so the match waits for the menu
            boot.gameObject.AddComponent<NetSession>();
        }

        private void Awake()
        {
            Instance = this;
            _ = Voice;
            _boot = GetComponent<MatchBootstrap>();
            _soloDinosaur = _boot != null ? _boot.dinosaurPrefab : null;
            _address = PlayerPrefs.GetString(AddressKey, "127.0.0.1");
            Account.Restore();          // sign back in if this computer remembers an account
            OnlineLeaderboard.Flush();  // an online result that couldn't be sent last time goes up now
            PlayerName = CleanName(PlayerPrefs.GetString(NameKey, ""));
            if (string.IsNullOrEmpty(PlayerName) && SteamService.Ready) PlayerName = CleanName(SteamService.PersonaName);
            if (string.IsNullOrEmpty(PlayerName)) PlayerName = Names[new System.Random().Next(Names.Length)];
            CrewName = CleanName(PlayerPrefs.GetString(CrewKey, ""), MaxCrew);
            if (string.IsNullOrEmpty(CrewName)) CrewName = CrewNames.Random(new System.Random());
            WorldConditions.ChosenTime    = (DayTime)Mathf.Clamp(PlayerPrefs.GetInt(TimeKey, 0), 0, (int)DayTime.Night);
            WorldConditions.ChosenWeather = (Weather)Mathf.Clamp(PlayerPrefs.GetInt(WeatherKey, 0), 0, (int)Weather.Fog);
            NetRole.IsFollower = false;
            if (_boot != null)
            {
                _boot.CanPause       = () => _mode == Mode.Solo;
                _boot.LeaveRequested = LeaveMatch;
                _titleSequence = gameObject.AddComponent<TitleSequence>();
            }
        }
        private TitleSequence _titleSequence;

        // From the in-match menu or the results: drop the island and the connection, back to the start menu.
        private void LeaveMatch()
        {
            SteamService.LeaveLobby();
            VivoxVoice.Leave();
            _joinCode = null;
            bool online = IsOnline;
            _leaving = true;
            BackToMenu(null); // first, so the dropped connection doesn't read as "lost the host"
            if (online) StopNetwork();
            _leaving = false;
            _avatars.Clear();
            _flares.Clear();
            _boot.Leave();
            if (_titleSequence != null) _titleSequence.ReturnToMenu();
        }
        private bool _leaving;

        // Test launches, for checking a built game without clicking through it:
        //   -autosolo           an offline solo match (Easy) as soon as the menu is up
        //   -autojoin <address> join an offline (same Wi-Fi) host at that address
        //   -shot <file.png>    save a screenshot 6 s after the menu is up, then quit
        private bool _autoDone;
        private void AutoLaunch()
        {
            if (_autoDone || !TitleSequence.MenuReady || _mode != Mode.Menu) return;
            _autoDone = true;
            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++)
                if (args[i] == "-shot") StartCoroutine(ShotAndQuit(args[i + 1]));
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "-autosolo") { Debug.Log("[NetSession] -autosolo"); PlaySolo(ChallengeLevel.Easy, online: false); return; }
                if (args[i] == "-autojoin" && i + 1 < args.Length) { Debug.Log($"[NetSession] -autojoin {args[i + 1]}"); _address = args[i + 1]; Join(); return; }
            }
        }

        private System.Collections.IEnumerator ShotAndQuit(string path)
        {
            yield return new WaitForSecondsRealtime(6f);
            ScreenCapture.CaptureScreenshot(path);
            yield return new WaitForSecondsRealtime(2f);
            Application.Quit();
        }

        private void OnEnable()
        {
            DinosaurAI.Created += OnDinosaurCreated;
            DinosaurAI.Damaged += OnDinosaurDamaged;
            DinosaurAI.Killed  += OnDinosaurKilled;
            if (_boot != null) _boot.Generated += OnGenerated;
            SteamService.LobbyCreated += OnSteamLobbyCreated;
            SteamService.JoinHost     += JoinSteam;
        }

        private void OnDisable()
        {
            DinosaurAI.Created -= OnDinosaurCreated;
            DinosaurAI.Damaged -= OnDinosaurDamaged;
            DinosaurAI.Killed  -= OnDinosaurKilled;
            if (_boot != null) _boot.Generated -= OnGenerated;
            SteamService.LobbyCreated -= OnSteamLobbyCreated;
            SteamService.JoinHost     -= JoinSteam;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            NetRole.IsFollower = false;
            if (_net != null) Destroy(_net.gameObject);
        }

        // ── Menu choices ───────────────────────────────────────────────────────

        // On the start menu, not in a match or connecting.
        public bool InMenu => _mode == Mode.Menu;
        // The start menu is on screen (also while hosting starts or a join connects).
        public bool ShowingMenu => _mode == Mode.Menu || _mode == Mode.Joining || _mode == Mode.StartingHost;

        // The start menu's Play solo. Tools call it too (the island audit starts a solo match this way).
        public void PlaySolo(ChallengeLevel level) => PlaySolo(level, online: false);

        public void PlaySolo(ChallengeLevel level, bool online)
        {
            _online = online && Account.SignedIn;
            UseNames();
            _pickingChallenge = false;
            Challenge.Current = level;
            SaveName();
            WorldConditions.ClearOverride();
            StopNetwork();
            NetRole.IsFollower = false;
            _boot.dinosaurPrefab = _soloDinosaur;
            _boot.CanRestart = null;
            _boot.RestartNote = null;
            _boot.spawnOffset = Vector3.zero;
            _boot.playerSlot  = 0;
            _mode = Mode.Solo;
            if (_titleSequence != null) _titleSequence.EndIntro();
            _boot.StartSolo();
        }

        private async void Host(bool online)
        {
            _online = online && Account.SignedIn;
            UseNames();
            SaveName();
            WorldConditions.ClearOverride();
            if (!EnsureNetwork(_online)) return;
            _joinCode = null;
            _mode = Mode.StartingHost;
            if (_online)
            {
                // A Relay allocation for the team, and the code that finds it.
                _status = "Getting a join code...";
                try
                {
                    var allocation = await RelayService.Instance.CreateAllocationAsync(MaxTeam - 1);
                    _joinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);
                    _relayNet.SetRelayServerData(allocation.ToRelayServerData("dtls"));
                }
                catch (System.Exception e)
                {
                    Debug.Log($"[NetSession] Relay: {e.Message}");
                    BackToMenu("Couldn't reach the online servers. Check the internet connection, or play offline.");
                    return;
                }
                if (_mode != Mode.StartingHost) return; // cancelled meanwhile
            }
            Challenge.Current = ChallengeLevel.Hard; // co-op plays the island as tuned
            NetRole.IsFollower = false;
            _boot.dinosaurPrefab = _dinosaurPrefab.gameObject;
            _boot.CanRestart = null;
            _boot.RestartNote = "A new island takes your whole team with you.";
            _boot.spawnOffset = Vector3.zero;
            _boot.playerSlot  = 0;
            _status = "Starting the host...";
            _lanAddress = LocalAddress();
            // Online: Relay, and Steam beside it for invites; offline: LAN.
            bool started = _multipass != null ? _multipass.StartConnection(true, 0) : _net.ServerManager.StartConnection();
            if (_multipass != null && _steamNet != null) _multipass.StartConnection(true, 1);
            if (!started)
            {
                StopNetwork();
                BackToMenu(_online ? "Couldn't start the online host." : $"Couldn't open port {Port}. Is another copy already hosting?");
            }
            // The island is generated once the server is up (OnServerState): an animal spawned before then
            // couldn't be shared.
        }

        // A friend's Steam lobby was entered (from an invite or "Join game"): connect to its host over Steam.
        private void JoinSteam(CSteamID host)
        {
            if (_mode == Mode.Solo) LeaveMatch();           // invited mid-solo-match: drop it and go
            else if (_mode != Mode.Menu) return;              // already in a team
            if (_titleSequence != null) _titleSequence.EndIntro();
            _online = Account.SignedIn;
            UseNames();
            SaveName();
            if (!EnsureNetwork(true) || _steamNet == null || _multipass == null)
            {
                _status = "Couldn't join over Steam.";
                return;
            }
            Challenge.Current = ChallengeLevel.Hard;
            NetRole.IsFollower = true;
            _boot.CanRestart = () => false;
            _boot.RestartNote = "Waiting for the host to start the next island...";
            _steamHost = host;
            _address = SteamFriends.GetFriendPersonaName(host);
            _mode = Mode.Joining;
            _status = $"Joining {_address} over Steam...";
            _clientLoaded = false;
            _readySeed = -1;
            _multipass.SetClientTransport(_steamNet);
            _steamNet.SetClientAddress(host.m_SteamID.ToString());
            if (!_net.ClientManager.StartConnection())
            {
                _status = "Couldn't start connecting over Steam.";
                BackToMenu(_status);
            }
        }

        private void OnSteamLobbyCreated()
        {
            if (_mode == Mode.Hosting) Match?.Announce("Steam lobby ready: press Esc and Invite friends, or Shift+Tab.");
        }

        // Offline: a host on the same Wi-Fi, by address.
        private void Join()
        {
            _online = false;
            UseNames();
            SaveName();
            if (!EnsureNetwork(false)) return;
            Challenge.Current = ChallengeLevel.Hard;
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

        // Online: a friend's join code, through Unity Relay.
        private async void JoinCode()
        {
            string code = (_codeInput ?? "").Trim().ToUpperInvariant();
            if (code.Length < 6) { _status = "Type the 6-character code from the host's screen."; return; }
            _online = Account.SignedIn;
            UseNames();
            if (!EnsureNetwork(true)) return;
            Challenge.Current = ChallengeLevel.Hard;
            NetRole.IsFollower = true;
            _boot.CanRestart = () => false;
            _boot.RestartNote = "Waiting for the host to start the next island...";
            _mode = Mode.Joining;
            _address = code;
            _status = $"Joining {code}...";
            _clientLoaded = false;
            _readySeed = -1;
            try
            {
                var allocation = await RelayService.Instance.JoinAllocationAsync(code);
                _relayNet.SetRelayServerData(allocation.ToRelayServerData("dtls"));
            }
            catch (System.Exception e)
            {
                Debug.Log($"[NetSession] Relay join: {e.Message}");
                BackToMenu("No game with that code. Check it with the host (codes last while the host is playing).");
                return;
            }
            if (_mode != Mode.Joining) return; // cancelled meanwhile
            _joinCode = code;
            if (_multipass != null) _multipass.SetClientTransport(_relayNet);
            if (!_net.ClientManager.StartConnection())
                BackToMenu("Couldn't start connecting.");
        }

        // Online, the name is the account's (it can't be borrowed); offline, the one typed on the menu.
        private void UseNames()
        {
            if (_online && Account.SignedIn) PlayerName = Account.Username;
            else PlayerName = CleanName(PlayerPrefs.GetString(NameKey, PlayerName));
            if (string.IsNullOrEmpty(PlayerName)) PlayerName = Names[new System.Random().Next(Names.Length)];
        }

        private void BackToMenu(string why)
        {
            // Dropped out of a match (the host went away): the island behind the menu replaces it.
            bool wasPlaying = _mode == Mode.Solo || _mode == Mode.Hosting || _mode == Mode.Joined;
            if (wasPlaying && !_leaving && _titleSequence != null)
            {
                _mode = Mode.Menu;
                _titleSequence.ReturnToMenu();
            }
            NetRole.IsFollower = false;
            _boot.CanRestart = null;
            _boot.RestartNote = null;
            _mode = Mode.Menu;
            _status = why;
        }

        // ── Network setup ──────────────────────────────────────────────────────

        private bool EnsureNetwork(bool online)
        {
            if (_net != null && _netOnline == online) return true;
            if (_net != null)
            {
                StopNetwork();
                DestroyImmediate(_net.gameObject);
                _net = null; _multipass = null; _tugboat = null; _relayNet = null; _steamNet = null;
            }
            _netOnline = online;

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
            Transport transport;
            if (!online)
            {
                _tugboat = go.AddComponent<Tugboat>();
                _tugboat.SetPort(Port);
                _tugboat.SetMaximumClients(MaxTeam);
                transport = _tugboat;
            }
            else
            {
                _relayNet = go.AddComponent<UnityTransport>();
                transport = _relayNet;
                if (SteamService.Ready)
                {
                    // Its settings live in private fields until it starts (its setters need a running server).
                    const System.Reflection.BindingFlags priv = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
                    _steamNet = go.AddComponent<FishySteamworks.FishySteamworks>();
                    typeof(FishySteamworks.FishySteamworks).GetField("_peerToPeer", priv)?.SetValue(_steamNet, true);
                    typeof(FishySteamworks.FishySteamworks).GetField("_maximumClients", priv)?.SetValue(_steamNet, (ushort)MaxTeam);
                    _multipass = go.AddComponent<Multipass>();
                    var list = typeof(Multipass)
                        .GetField("_transports", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                        ?.GetValue(_multipass) as List<Transport>;
                    list.Add(_relayNet); // first: the host's own player joins through it
                    list.Add(_steamNet);
                    transport = _multipass;
                }
            }
            // In the Editor, FishNet checks the prefab list the instant the component is added (before the next line
            // can give it one) and logs an error; that check never runs in a build, so it is muted for this one call.
            bool logs = Debug.unityLogger.logEnabled;
            Debug.unityLogger.logEnabled = false;
            _net = go.AddComponent<NetworkManager>();
            Debug.unityLogger.logEnabled = logs;
            // The NetworkManager comes first: a TransportManager added before it drags in a second, empty one.
            var transports = go.GetComponent<FishNet.Managing.Transporting.TransportManager>();
            if (transports == null) transports = go.AddComponent<FishNet.Managing.Transporting.TransportManager>();
            transports.Transport = transport;
            if (_multipass != null) _multipass.SetClientTransport(_relayNet);
            _net.SpawnablePrefabs = _prefabs;
            go.SetActive(true);

            _net.ServerManager.OnServerConnectionState += OnServerState;
            _net.ClientManager.OnClientConnectionState += OnClientState;
            _net.SceneManager.OnClientLoadedStartScenes += OnLoadedStartScenes;
            _net.ServerManager.OnRemoteConnectionState  += OnRemoteState;

            _net.ServerManager.RegisterBroadcast<ReadyMessage>(OnReady);
            _net.ServerManager.RegisterBroadcast<FinalStandMessage>(OnFinalStandRequest);
            _net.ServerManager.RegisterBroadcast<TeamMessage>(OnTeamMessage);
            _net.ServerManager.RegisterBroadcast<LiftOffMessage>(OnLiftOffRequest);
            _net.ServerManager.RegisterBroadcast<FlareMessage>(OnFlareRequest);
            _net.ClientManager.RegisterBroadcast<IslandMessage>(OnIsland);
            _net.ClientManager.RegisterBroadcast<AnnounceMessage>(OnAnnounce);
            _net.ClientManager.RegisterBroadcast<LiftOffMessage>(OnLiftOff);
            _net.ClientManager.RegisterBroadcast<FlareMessage>(OnFlare);
            Voice.Attach(_net);
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
            // With Steam beside Relay, Relay (the first transport) decides.
            if (_multipass != null && args.TransportIndex != 0) return;
            if (args.ConnectionState == LocalConnectionState.Started && _mode == Mode.StartingHost)
            {
                _mode = Mode.Hosting;
                _status = null;
                if (_online && SteamService.Ready) SteamService.HostLobby(MaxTeam); // so Steam friends can be invited
                if (_joinCode != null) VivoxVoice.Join(_joinCode);
                // The island first: building it holds the game for a few seconds, and a connection started before
                // that gave up waiting ("ConnectionFailed"), which left the host's own player out of its match.
                _boot.StartSolo(); // random island; OnGenerated shares it
                // Then the host plays too: through its own Relay transport online, over LAN offline ("localhost" can
                // resolve to IPv6, which LAN hosting doesn't listen on).
                if (_netOnline) { if (_multipass != null) _multipass.SetClientTransport(_relayNet); _net.ClientManager.StartConnection(); }
                else _net.ClientManager.StartConnection("127.0.0.1", Port);
            }
            else if (args.ConnectionState == LocalConnectionState.Stopped && _mode == Mode.StartingHost)
            {
                BackToMenu(_online ? "Couldn't start the online host." : $"Couldn't host on port {Port}.");
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
            string who = "A teammate";
            if (_avatars.TryGetValue(conn.ClientId, out var body))
            {
                _avatars.Remove(conn.ClientId);
                if (body != null)
                {
                    if (!string.IsNullOrEmpty(body.Label)) who = body.Label;
                    if (Match != null) Match.Teammates.Remove(body.transform);
                    if (!body.IsAlive) return; // already flew out or didn't make it; nothing new to say
                }
            }
            if (_mode == Mode.Hosting) TellTeam($"{who} left the game.", conn.ClientId);
        }

        private void OnLoadedStartScenes(NetworkConnection conn, bool asServer)
        {
            if (!asServer) { _clientLoaded = true; return; }
            if (IsHostsOwn(conn) || Match == null || Match.State == null) return;
            _net.ServerManager.Broadcast(conn, IslandFor(_seed, Match.State.Elapsed));
            foreach (var pad in _flares) // pads teammates' flares already brought in
                _net.ServerManager.Broadcast(conn, new FlareMessage { Pad = pad, From = -1 });
        }

        // ── Island ─────────────────────────────────────────────────────────────

        // Host: every new island (the first one and each restart) goes to the whole team.
        private void OnGenerated(int seed)
        {
            _seed = seed;
            _flares.Clear();
            HookMatch();
            if (Match != null) Match.PlayerName = PlayerName;
            if (_mode != Mode.Hosting || _net == null || !_net.ServerManager.Started) return;

            _net.ServerManager.Broadcast(IslandFor(seed, 0f));
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
            _hostCrew = CleanName(msg.Crew, MaxCrew);
            if (!string.IsNullOrEmpty(msg.Voice)) { _joinCode = msg.Voice; VivoxVoice.Join(msg.Voice); } // the team's voice channel
            int id = _net.ClientManager.Connection != null ? _net.ClientManager.Connection.ClientId : 1;
            float angle = id * 90f * Mathf.Deg2Rad;
            _boot.spawnOffset = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * 2.5f;
            _boot.playerSlot  = id;
            _flares.Clear();
            WorldConditions.Override((DayTime)msg.Time, (Weather)msg.Weather, msg.Wind); // the host's sky
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
            Match.LiftedOff           += OnLiftedOff;
            Match.FlareDropped        += OnFlareDropped;
            Match.MatchEnded          += OnMatchEnded;
            // A teammate still on their feet can get this player up, so going down isn't the end.
            Match.CanBeRevived  = () => IsOnline && Teammates().Any(a => a.IsStanding);
            Match.TeammatesNear = (point, radius) =>
            {
                if (!IsOnline) return 0;
                int n = 0;
                foreach (var a in Teammates())
                {
                    if (!a.IsStanding) continue;
                    Vector3 d = a.transform.position - point;
                    d.y = 0f;
                    if (d.sqrMagnitude <= radius * radius) n++;
                }
                return n;
            };
        }

        // Everyone else's bodies on this machine.
        private static IEnumerable<NetAvatar> Teammates()
        {
            foreach (var a in NetAvatar.All)
                if (a != null && a.IsSpawned && !a.IsOwner) yield return a;
        }

        private void Update()
        {
            AutoLaunch();
            // Tell the host once this machine is standing on the current island (it then sends this player's body).
            if (_net == null || !_net.ClientManager.Started || !_clientLoaded) return;
            if (Match == null || Match.Player == null || _readySeed == _seed) return;
            if (_mode != Mode.Hosting && _mode != Mode.Joined) return;
            _readySeed = _seed;
            _net.ClientManager.Broadcast(new ReadyMessage { Seed = _seed, Name = PlayerName });
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
            body = go.GetComponent<NetAvatar>();
            body.SetLabel(UniqueName(msg.Name, conn.ClientId));
            _net.ServerManager.Spawn(go, conn);
            _avatars[conn.ClientId] = body;
            if (!IsHostsOwn(conn)) Match.Teammates.Add(body.transform);
            TellTeam($"{body.Label} dropped in.", conn.ClientId);
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
            TellTeam(extracted ? $"{body.Label} made it out!" : $"{body.Label} didn't make it.", body.Owner.ClientId);
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

        // This machine has news for the team (it already showed it here): send it via the host.
        private void OnTeamAnnounced(string text)
        {
            if (!string.IsNullOrEmpty(text) && _net != null && _net.ClientManager.Started && IsOnline)
                _net.ClientManager.Broadcast(new TeamMessage { Text = text });
        }

        private void OnTeamMessage(NetworkConnection conn, TeamMessage msg, Channel channel)
        {
            if (string.IsNullOrEmpty(msg.Text)) return;
            string text = msg.Text.Length > 200 ? msg.Text.Substring(0, 200) : msg.Text;
            TellTeam(text, conn.ClientId);
        }

        // Host: to every machine but the one it's from (or about).
        private void TellTeam(string text, int from)
        {
            if (_net != null && _net.ServerManager.Started)
                _net.ServerManager.Broadcast(new AnnounceMessage { Text = text, From = from });
        }

        private void OnAnnounce(AnnounceMessage msg, Channel channel)
        {
            if (msg.From >= 0 && msg.From == MyClientId) return;
            if (Match != null) Match.Announce(msg.Text);
        }

        // ── Leaving together ───────────────────────────────────────────────────

        private void OnLiftedOff(Vector3 pad)
        {
            if (_net != null && _net.ClientManager.Started && IsOnline)
                _net.ClientManager.Broadcast(new LiftOffMessage { Pad = pad });
        }

        private void OnLiftOffRequest(NetworkConnection conn, LiftOffMessage msg, Channel channel)
        {
            _net.ServerManager.Broadcast(new LiftOffMessage { Pad = msg.Pad, From = conn.ClientId });
        }

        private void OnLiftOff(LiftOffMessage msg, Channel channel)
        {
            if (msg.From == MyClientId || Match == null) return;
            Match.TeamLiftOff(msg.Pad);
        }

        // ── Rescue flares ──────────────────────────────────────────────────────

        private readonly List<Vector3> _flares = new List<Vector3>(); // host: this island's flare pads, for late joiners

        // Every finished run goes on this computer's leaderboard: under the player's name, and the crew's
        // (the host's crew name for the whole team online).
        private void OnMatchEnded(MatchStats stats)
        {
            if (stats == null) return;
            string crew = _mode == Mode.Joined && !string.IsNullOrEmpty(_hostCrew) ? _hostCrew : CrewName;
            int size = _mode == Mode.Hosting ? _avatars.Count : _mode == Mode.Joined ? NetAvatar.All.Count : 1;
            stats.Crew = crew;
            var run = new RunRecord
            {
                Player = PlayerName, Crew = crew, CrewSize = Mathf.Max(1, size), Score = stats.Score, Result = stats.Result,
                Seconds = stats.TimeSurvived, Kills = stats.DinosKilled, Challenge = stats.Challenge, Seed = stats.Seed,
            };
            stats.BoardPlace = Leaderboard.Local.Record(run);
            // Worldwide too; the crew's entry comes from whoever named the crew (the host, or a solo player).
            // Online matches only: offline is practice, and its scores stay on this computer.
            if (_online && Account.SignedIn) OnlineLeaderboard.Submit(run, forCrew: _mode != Mode.Joined);
        }

        private void OnFlareDropped(Vector3 pad)
        {
            if (_net != null && _net.ClientManager.Started && IsOnline)
                _net.ClientManager.Broadcast(new FlareMessage { Pad = pad });
        }

        private void OnFlareRequest(NetworkConnection conn, FlareMessage msg, Channel channel)
        {
            if (_flares.Count >= 8) return; // one per player is plenty; ignore floods
            _flares.Add(msg.Pad);
            _net.ServerManager.Broadcast(new FlareMessage { Pad = msg.Pad, From = conn.ClientId });
        }

        private void OnFlare(FlareMessage msg, Channel channel)
        {
            if (msg.From == MyClientId || Match == null) return;
            Match.AddSharedPad(msg.Pad);
        }

        private static IslandMessage IslandFor(int seed, float elapsed)
        {
            var c = WorldConditions.Current;
            return new IslandMessage { Seed = seed, Elapsed = elapsed, Time = (byte)c.Time, Weather = (byte)c.Weather, Wind = c.WindDegrees,
                                       Crew = Instance != null ? Instance.CrewName : null,
                                       Voice = Instance != null ? Instance._joinCode : null };
        }

        private int MyClientId => _net != null && _net.ClientManager.Started && _net.ClientManager.Connection != null
            ? _net.ClientManager.Connection.ClientId : -2;

        // ── Names ──────────────────────────────────────────────────────────────

        private static string CleanName(string name) => CleanName(name, MaxName);

        private static string CleanName(string name, int max)
        {
            if (string.IsNullOrEmpty(name)) return "";
            var sb = new System.Text.StringBuilder();
            foreach (char c in name)
                if (char.IsLetterOrDigit(c) || c == ' ' || c == '-' || c == '_' || c == '.') sb.Append(c);
            string s = sb.ToString().Trim();
            if (s.Length > max) s = s.Substring(0, max).Trim();
            return NameFilter.IsOffensive(s) ? "" : s; // callers fall back to a generated name
        }

        private void SaveName()
        {
            string clean = CleanName(PlayerName);
            if (string.IsNullOrEmpty(clean)) clean = Names[new System.Random().Next(Names.Length)];
            PlayerName = clean;
            PlayerPrefs.SetString(NameKey, clean);
            CrewName = CleanName(CrewName, MaxCrew);
            if (string.IsNullOrEmpty(CrewName)) CrewName = CrewNames.Random(new System.Random());
            PlayerPrefs.SetString(CrewKey, CrewName);
            _hostCrew = null;
            PlayerPrefs.Save();
            if (Match != null) Match.PlayerName = clean;
        }

        // Host: two Kestrels become Kestrel and Kestrel 2.
        private string UniqueName(string wanted, int clientId)
        {
            string name = CleanName(wanted);
            if (string.IsNullOrEmpty(name)) name = $"Survivor {clientId + 1}";
            string candidate = name;
            for (int i = 2; Taken(candidate, clientId); i++) candidate = $"{name} {i}";
            return candidate;
        }

        private bool Taken(string name, int clientId)
        {
            foreach (var pair in _avatars)
                if (pair.Key != clientId && pair.Value != null && pair.Value.IsAlive &&
                    string.Equals(pair.Value.Label, name, System.StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private void OnFinalStandRequested()
        {
            if (_mode == Mode.Joined && _net != null && _net.ClientManager.Started)
                _net.ClientManager.Broadcast(new FinalStandMessage());
        }

        // ── Menu and HUD ───────────────────────────────────────────────────────

        private void OnGUI()
        {
            Ui.Begin();
            EnsureStyles();
            if (ShowingMenu) { if (TitleSequence.MenuReady) DrawMenu(); }
            else if (_mode == Mode.Hosting || _mode == Mode.Joined) DrawTeamLine();
        }

        private void DrawMenu()
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible   = true;
            if (Match != null && Match.PlayerController != null) Match.PlayerController.InputBlocked = true;

            GUI.color = new Color(0f, 0f, 0f, 0.3f); // a light veil: the island behind stays in view
            GUI.DrawTexture(new Rect(0, 0, Ui.W, Ui.H), Texture2D.whiteTexture);
            GUI.color = Color.white;

            // The title itself is drawn above this box by the TitleSequence.
            var area = new Rect(Ui.W * 0.5f - 200, Ui.H * 0.5f - MenuTopOffset, 400, 480);
            if (_showSettings)
            {
                if (SettingsPanel.Draw(area, onStartMenu: true)) _showSettings = false;
                return;
            }
            if (_showBoard)
            {
                if (LeaderboardPanel.Draw(new Rect(Ui.W * 0.5f - 260, area.y, 520, area.height), PlayerName, CrewName)) _showBoard = false;
                return;
            }
            GUILayout.BeginArea(area, GUI.skin.box);
            GUILayout.Label("Survive together. Quietly.", _tagline);
            GUILayout.Space(6);
            if (_mode != Mode.Menu) DrawConnecting();
            else if (_page == Page.SignIn) DrawSignIn();
            else if (_page == Page.Online) DrawOnline();
            else if (_page == Page.Offline) DrawOffline();
            else DrawMain();
            GUILayout.EndArea();
        }

        // ── Menu pages ─────────────────────────────────────────────────────────

        private enum Page { Main, SignIn, Online, Offline }
        private Page _page = Page.Main;
        private string _username, _password = "";
        private bool _signingUp;

        private void DrawMain()
        {
            if (GUILayout.Button("Play Online", _button, GUILayout.Height(46)))
            {
                _status = null;
                _page = Account.SignedIn ? Page.Online : Page.SignIn;
            }
            GUILayout.Label("Solo or co-op over the internet, voice chat, worldwide leaderboards. Needs an account.", _label);
            GUILayout.Space(8);
            if (GUILayout.Button("Play Offline", _button, GUILayout.Height(40))) { _status = null; _page = Page.Offline; }
            GUILayout.Label("Practice: solo, or co-op on the same Wi-Fi. Scores are not submitted.", _label);
            GUILayout.Space(10);
            if (Account.SignedIn)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label($"Signed in as {Account.Username}", _label);
                if (GUILayout.Button("Sign out", GUILayout.Width(80), GUILayout.Height(24))) Account.SignOut();
                GUILayout.EndHorizontal();
            }
            DrawStatus();
            GUILayout.FlexibleSpace();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Leaderboard", GUILayout.Height(28))) _showBoard = true;
            if (GUILayout.Button("Settings", GUILayout.Height(28))) _showSettings = true;
            if (!Application.isEditor && GUILayout.Button("Quit to desktop", GUILayout.Height(28))) Application.Quit();
            GUILayout.EndHorizontal();
        }

        private void DrawSignIn()
        {
            if (_username == null) _username = Account.LastUsername;
            GUILayout.Label(_signingUp ? "CREATE AN ACCOUNT" : "SIGN IN", _title);
            GUILayout.Space(4);
            GUI.enabled = !Account.Busy;
            GUILayout.BeginHorizontal();
            GUILayout.Label("Username:", _label, GUILayout.Width(90), GUILayout.Height(30));
            _username = GUILayout.TextField(_username ?? "", 20, _field, GUILayout.Height(30));
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Label("Password:", _label, GUILayout.Width(90), GUILayout.Height(30));
            _password = GUILayout.PasswordField(_password ?? "", '*', 30, _field, GUILayout.Height(30));
            GUILayout.EndHorizontal();
            if (_signingUp)
                GUILayout.Label($"Username: {Account.UsernameRules}. It's your name on the leaderboards.\nPassword: {Account.PasswordRules}.", _label);
            GUILayout.Space(6);
            System.Action signedIn = () => { _password = ""; _page = Page.Online; };
            if (GUILayout.Button(_signingUp ? "Create account" : "Sign in", _button, GUILayout.Height(40)))
            {
                if (_signingUp) Account.SignUp(_username, _password, signedIn);
                else Account.SignIn(_username, _password, signedIn);
            }
            if (GUILayout.Button(_signingUp ? "I have an account: sign in" : "New here? Create an account", GUILayout.Height(28)))
                _signingUp = !_signingUp;
            GUI.enabled = true;
            if (!string.IsNullOrEmpty(Account.Status)) GUILayout.Label(Account.Status, _label);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Back", GUILayout.Height(28))) { _password = ""; _page = Page.Main; }
        }

        private void DrawOnline()
        {
            if (!Account.SignedIn) { _page = Page.SignIn; return; }
            GUILayout.Label($"ONLINE  -  {Account.Username}", _title);
            DrawCrewName();
            DrawConditionsChoice();
            GUILayout.Space(6);
            if (_pickingChallenge) { DrawChallengeChoice(online: true); return; }
            if (GUILayout.Button("Solo", _button, GUILayout.Height(40))) _pickingChallenge = true;
            GUILayout.Space(4);
            if (GUILayout.Button("Host a co-op game", _button, GUILayout.Height(40))) Host(online: true);
            GUILayout.Label("You get a join code to give your friends.", _label);
            GUILayout.Space(6);
            GUILayout.Label("Join a friend (their join code):", _label);
            GUILayout.BeginHorizontal();
            _codeInput = GUILayout.TextField(_codeInput ?? "", 8, _field, GUILayout.Height(32)).ToUpperInvariant();
            if (GUILayout.Button("Join", _button, GUILayout.Width(90), GUILayout.Height(32))) JoinCode();
            GUILayout.EndHorizontal();
            if (SteamService.Ready) GUILayout.Label("Steam friends can also join from your invite.", _label);
            DrawStatus();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Back", GUILayout.Height(28))) { _pickingChallenge = false; _page = Page.Main; }
        }

        private void DrawOffline()
        {
            GUILayout.Label("OFFLINE PRACTICE", _title);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Your name:", _label, GUILayout.Width(90), GUILayout.Height(30));
            PlayerName = GUILayout.TextField(PlayerName ?? "", MaxName, _field, GUILayout.Height(30));
            GUILayout.EndHorizontal();
            DrawCrewName();
            DrawConditionsChoice();
            GUILayout.Space(6);
            if (_pickingChallenge) { DrawChallengeChoice(online: false); return; }
            if (GUILayout.Button("Solo", _button, GUILayout.Height(40))) _pickingChallenge = true;
            GUILayout.Space(4);
            if (GUILayout.Button("Host on this Wi-Fi", _button, GUILayout.Height(40))) Host(online: false);
            GUILayout.Space(6);
            GUILayout.Label("Join a friend on this Wi-Fi (their address):", _label);
            GUILayout.BeginHorizontal();
            _address = GUILayout.TextField(_address ?? "", 64, _field, GUILayout.Height(32));
            if (GUILayout.Button("Join", _button, GUILayout.Width(90), GUILayout.Height(32))) Join();
            GUILayout.EndHorizontal();
            // 127.0.0.1 (two copies on one computer) is for testing, so only development builds mention it.
            string hint = "The address is shown at the top of the host's screen. Practice: scores stay on this computer.";
            if (Debug.isDebugBuild) hint += " Same computer: 127.0.0.1.";
            GUILayout.Label(hint, _label);
            DrawStatus();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Back", GUILayout.Height(28))) { _pickingChallenge = false; _page = Page.Main; }
        }

        private void DrawCrewName()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("Crew name:", _label, GUILayout.Width(90), GUILayout.Height(30));
            CrewName = GUILayout.TextField(CrewName ?? "", MaxCrew, _field, GUILayout.Height(30));
            if (GUILayout.Button("New", GUILayout.Width(44), GUILayout.Height(30))) CrewName = CrewNames.Random(new System.Random());
            GUILayout.EndHorizontal();
        }

        // Hosting is starting or a join is connecting: what's happening, and a way out.
        private void DrawConnecting()
        {
            GUILayout.Label(_mode == Mode.StartingHost ? "STARTING" : "JOINING", _title);
            DrawStatus();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Cancel", GUILayout.Height(30)))
            {
                StopNetwork();
                BackToMenu(null);
            }
        }

        private void DrawStatus()
        {
            if (string.IsNullOrEmpty(_status)) return;
            GUILayout.Space(6);
            GUILayout.Label(_status, _label);
        }

        public const string GameTitle = "TETHER: PRIMAL";
        public const float MenuTopOffset = 190f; // the menu box starts this far above the screen's middle
        private bool _showSettings;
        public bool ShowingSettings => _showSettings || _showBoard;
        private bool _showBoard;

        // Solo only: how hard the island fights. Co-op always plays it as tuned (Hard).
        private void DrawChallengeChoice(bool online)
        {
            GUILayout.Label("How hard?", _label);
            foreach (ChallengeLevel level in new[] { ChallengeLevel.Easy, ChallengeLevel.Medium, ChallengeLevel.Hard })
            {
                if (GUILayout.Button(Challenge.Label(level), _button, GUILayout.Height(38))) PlaySolo(level, online);
                GUILayout.Label(Challenge.Describe(level), _label);
                GUILayout.Space(2);
            }
            GUILayout.Space(4);
            if (GUILayout.Button("Back", GUILayout.Height(26))) _pickingChallenge = false;
        }

        // Time of day and weather for the islands this player starts (solo or hosting). Joiners get the host's.
        private void DrawConditionsChoice()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("Time:", _label, GUILayout.Width(60), GUILayout.Height(26));
            if (GUILayout.Button(WorldConditions.Label(WorldConditions.ChosenTime), GUILayout.Height(26)))
            {
                WorldConditions.ChosenTime = (DayTime)(((int)WorldConditions.ChosenTime + 1) % ((int)DayTime.Night + 1));
                PlayerPrefs.SetInt(TimeKey, (int)WorldConditions.ChosenTime);
            }
            GUILayout.Label("Weather:", _label, GUILayout.Width(66), GUILayout.Height(26));
            if (GUILayout.Button(WorldConditions.Label(WorldConditions.ChosenWeather), GUILayout.Height(26)))
            {
                WorldConditions.ChosenWeather = (Weather)(((int)WorldConditions.ChosenWeather + 1) % ((int)Weather.Fog + 1));
                PlayerPrefs.SetInt(WeatherKey, (int)WorldConditions.ChosenWeather);
            }
            GUILayout.EndHorizontal();
        }

        private void DrawTeamLine()
        {
            int team = _mode == Mode.Hosting ? _avatars.Count : NetAvatar.All.Count;
            string where = _mode != Mode.Hosting ? $"co-op with {_address}"
                         : _joinCode != null ? $"join code {_joinCode}"
                         : $"hosting on this Wi-Fi at {_lanAddress ?? "?"}";
            string text = $"{PlayerName}  |  {where}  |  team of {Mathf.Max(1, team)}{(_online ? "" : "  |  practice")}";
            GUI.Label(new Rect(Ui.W * 0.5f - 250, 4, 500, 22), text, _hud);
            // The code, ready to paste into a chat, whenever the mouse is free.
            if (_mode == Mode.Hosting && _joinCode != null && Cursor.visible &&
                GUI.Button(new Rect(Ui.W * 0.5f + 90, 28, 130, 26), "Copy join code"))
                GUIUtility.systemCopyBuffer = _joinCode;
            // The Steam invite, whenever the mouse is free (the Esc menu, the shop, the results).
            if (_mode == Mode.Hosting && SteamService.InLobby && Cursor.visible &&
                GUI.Button(new Rect(Ui.W * 0.5f - 80, 28, 160, 26), "Invite Steam friends"))
                SteamService.InviteFriends();
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
            _tagline = new GUIStyle(GUI.skin.label) { fontSize = 13, fontStyle = FontStyle.Italic, alignment = TextAnchor.MiddleCenter };
            _tagline.normal.textColor = new Color(1f, 1f, 1f, 0.7f);
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
