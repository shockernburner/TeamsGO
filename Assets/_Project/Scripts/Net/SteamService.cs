using System;
using UnityEngine;
using Steamworks;

namespace ProjectFossil.Net
{
    // Steam for the whole game: started once before the first scene, its callbacks pumped every frame, shut down on
    // quit. Without the Steam client (or before the store app exists) the game still runs, with LAN co-op only.
    // Lobbies are friends-only and carry the host's Steam id; a friend joins from an invite in the Steam overlay,
    // or from the friends list's "Join game", even when the game was not running yet.
    public class SteamService : MonoBehaviour
    {
        // Valve's public test app (Spacewar) until TETHER: Primal has its own App ID from Steamworks.
        public const uint AppId = 480;
        private const string HostKey = "host";

        public static bool Ready { get; private set; }
        public static string PersonaName => Ready ? SteamFriends.GetPersonaName() : null;
        public static CSteamID Lobby { get; private set; } = CSteamID.Nil;
        public static bool InLobby => Lobby != CSteamID.Nil;

        // A lobby this machine created is ready for invites.
        public static event Action LobbyCreated;
        // This machine entered a friend's lobby: connect to that host (their Steam id as the transport address).
        public static event Action<CSteamID> JoinHost;
        public static string LastError { get; private set; }

        private Callback<LobbyCreated_t> _created;
        private Callback<LobbyEnter_t> _entered;
        private Callback<GameLobbyJoinRequested_t> _joinRequested;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Boot()
        {
            if (FindAnyObjectByType<SteamService>() != null) return;
            var go = new GameObject("Steam");
            DontDestroyOnLoad(go);
            go.AddComponent<SteamService>();
        }

        private void Awake()
        {
            Ready = false;
            Lobby = CSteamID.Nil;
            if (!Packsize.Test() || !DllCheck.Test())
            {
                Fail("Steamworks.NET does not match this platform's Steam library");
                return;
            }
            try
            {
                // Started outside Steam, a real store build relaunches through Steam (not the test app, nor the Editor).
                if (!Application.isEditor && AppId != 480 && SteamAPI.RestartAppIfNecessary(new AppId_t(AppId)))
                {
                    Application.Quit();
                    return;
                }
                Ready = SteamAPI.Init();
            }
            catch (DllNotFoundException e)
            {
                Fail($"Steam library not found ({e.Message})");
                return;
            }
            if (!Ready)
            {
                Fail("Steam isn't running or isn't logged in");
                return;
            }

            _created       = Callback<LobbyCreated_t>.Create(OnLobbyCreated);
            _entered       = Callback<LobbyEnter_t>.Create(OnLobbyEntered);
            _joinRequested = Callback<GameLobbyJoinRequested_t>.Create(OnJoinRequested);
            Debug.Log($"[Steam] Ready as {PersonaName} (app {AppId}).");

            // Invited while the game was closed: Steam starts it with +connect_lobby <id>.
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++)
                if (args[i] == "+connect_lobby" && ulong.TryParse(args[i + 1], out ulong id))
                    SteamMatchmaking.JoinLobby(new CSteamID(id));
        }

        private static void Fail(string why)
        {
            LastError = why;
            Debug.Log($"[Steam] Not available: {why}. Co-op works over the local network only.");
        }

        private void Update()
        {
            if (Ready) SteamAPI.RunCallbacks();
        }

        private void OnApplicationQuit() => Shutdown();
        private void OnDestroy() => Shutdown();

        private static void Shutdown()
        {
            if (!Ready) return;
            LeaveLobby();
            SteamAPI.Shutdown();
            Ready = false;
        }

        // ── Lobbies ───────────────────────────────────────────────────────────

        public static void HostLobby(int maxPlayers)
        {
            if (!Ready) return;
            LeaveLobby();
            SteamMatchmaking.CreateLobby(ELobbyType.k_ELobbyTypeFriendsOnly, maxPlayers);
        }

        public static void InviteFriends()
        {
            if (Ready && InLobby) SteamFriends.ActivateGameOverlayInviteDialog(Lobby);
        }

        public static void LeaveLobby()
        {
            if (Ready && InLobby) SteamMatchmaking.LeaveLobby(Lobby);
            Lobby = CSteamID.Nil;
        }

        private void OnLobbyCreated(LobbyCreated_t cb)
        {
            if (cb.m_eResult != EResult.k_EResultOK)
            {
                LastError = $"Steam couldn't create a lobby ({cb.m_eResult})";
                Debug.LogWarning($"[Steam] {LastError}");
                return;
            }
            Lobby = new CSteamID(cb.m_ulSteamIDLobby);
            SteamMatchmaking.SetLobbyData(Lobby, HostKey, SteamUser.GetSteamID().m_SteamID.ToString());
            SteamMatchmaking.SetLobbyData(Lobby, "name", $"{PersonaName}'s island");
            LobbyCreated?.Invoke();
        }

        private void OnJoinRequested(GameLobbyJoinRequested_t cb) => SteamMatchmaking.JoinLobby(cb.m_steamIDLobby);

        private void OnLobbyEntered(LobbyEnter_t cb)
        {
            Lobby = new CSteamID(cb.m_ulSteamIDLobby);
            var owner = SteamMatchmaking.GetLobbyOwner(Lobby);
            if (owner == SteamUser.GetSteamID()) return; // our own lobby: we are the host
            string host = SteamMatchmaking.GetLobbyData(Lobby, HostKey);
            var hostId = ulong.TryParse(host, out ulong h) ? new CSteamID(h) : owner;
            JoinHost?.Invoke(hostId);
        }
    }
}
