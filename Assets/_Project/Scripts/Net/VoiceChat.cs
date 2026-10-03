using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using FishNet.Broadcast;
using FishNet.Connection;
using FishNet.Managing;
using FishNet.Transporting;
using Steamworks;
using ProjectFossil.Core;
using ProjectFossil.Dinosaurs;

namespace ProjectFossil.Net
{
    // Anyone -> host -> team: a slice of a player's voice (Steam-compressed) and how loud it was.
    public struct VoiceMessage : IBroadcast
    {
        public int    Sender;   // the speaker's client id (set by the host when it passes the message on)
        public byte[] Data;
        public float  Loudness; // 0..1, the speaker's own measure of their voice
    }

    // Proximity voice, and dinosaurs that hear it. Steam records the microphone and detects speech; only how LOUD
    // each slice is (never the words) becomes a noise event where the speaker stands, so a whisper stays secret a
    // metre from a raptor and a shout brings the island. Dinosaurs live on the host, so the host applies everyone's
    // noise; in solo the player's own voice counts too. Teammates' voices play from their bodies, fading by distance.
    public class VoiceChat : MonoBehaviour
    {
        // How far each level of voice carries to a dinosaur's ear, in metres; rain and storms halve it.
        public static readonly float[] HeardWithin = { 3f, 12f, 25f, 45f };
        public static readonly string[] LevelNames = { "whisper", "talking", "raised voice", "SHOUTING" };
        // Where each level starts, in dB (RMS of the speech): a whisper up to -30, talking to -20, a raised voice to
        // -12, a shout above. Measured on a laptop mic: a quiet room sits near -32. Under -40 is silence.
        private static readonly float[] LevelStartsDb = { -40f, -30f, -20f, -12f };
        private const float MinDb = -50f, MaxDb = -6f; // loudness 0..1 spans this
        private static float ToLoudness(float db) => Mathf.InverseLerp(MinDb, MaxDb, db);
        private static readonly float[] LevelStarts =
            { ToLoudness(LevelStartsDb[0]), ToLoudness(LevelStartsDb[1]), ToLoudness(LevelStartsDb[2]), ToLoudness(LevelStartsDb[3]) };
        private const float NoiseEvery = 0.4f;   // seconds between noise events while someone keeps talking
        private const float HearRange  = 35f;    // metres a teammate's voice carries to another player

        public static VoiceChat Instance { get; private set; }

        public bool  Speaking   { get; private set; }
        public int   Level      { get; private set; } = -1;   // the local player's current level, -1 = silent
        public float LastHeard  { get; private set; }         // metres, what the last noise event reached

        private NetworkManager _net;
        private bool _recording;
        private readonly byte[] _compressed = new byte[8 * 1024];
        private byte[] _pcm = new byte[VoiceBuffer.Bytes];
        private float _loudPeak, _noiseTimer, _speakingUntil;
        private readonly Dictionary<int, VoicePlayer> _players = new Dictionary<int, VoicePlayer>();

        private void Awake() => Instance = this;
        private void OnDestroy() { if (Instance == this) Instance = null; StopRecording(); }

        // Called by NetSession whenever it builds its network.
        public void Attach(NetworkManager net)
        {
            _net = net;
            _net.ServerManager.RegisterBroadcast<VoiceMessage>(OnServerVoice);
            _net.ClientManager.RegisterBroadcast<VoiceMessage>(OnClientVoice);
        }

        private static Transform LocalPlayer =>
            NetSession.Instance != null && NetSession.Instance.Match != null && NetSession.Instance.Match.Player != null
                ? NetSession.Instance.Match.Player.transform : null;

        private bool InMatch => NetSession.Instance != null && NetSession.Instance.Match != null && NetSession.Instance.Match.IsRunning;

        // ── Capture ───────────────────────────────────────────────────────────

        private void Update()
        {
            bool want = SteamService.Ready && InMatch && LocalPlayer != null && GameSettings.Voice != VoiceMode.Off &&
                        (GameSettings.Voice == VoiceMode.OpenMic ||
                         (Keyboard.current != null && Keyboard.current.vKey.isPressed));
            if (want && !_recording) { SteamUser.StartVoiceRecording(); _recording = true; }
            else if (!want && _recording) StopRecording();

            if (_recording) Capture();
            if (Time.time > _speakingUntil) { Speaking = false; Level = -1; }

            _noiseTimer -= Time.deltaTime;
            if (_noiseTimer <= 0f && _loudPeak > 0f)
            {
                _noiseTimer = NoiseEvery;
                var me = LocalPlayer;
                // Dinosaurs live on the host: the host and a solo player make the noise here, a joiner's goes with
                // its voice to the host.
                if (me != null && !NetRole.IsFollower) LastHeard = MakeNoise(me.position, _loudPeak);
                else LastHeard = Radius(_loudPeak);
                _loudPeak = 0f;
            }
        }

        private void StopRecording()
        {
            if (!_recording) return;
            if (SteamService.Ready) SteamUser.StopVoiceRecording();
            _recording = false;
        }

        private void Capture()
        {
            if (SteamUser.GetAvailableVoice(out uint available) != EVoiceResult.k_EVoiceResultOK || available == 0) return;
            if (SteamUser.GetVoice(true, _compressed, (uint)_compressed.Length, out uint written) != EVoiceResult.k_EVoiceResultOK || written == 0) return;

            float loud = Loudness(_compressed, written);
            if (loud < LevelStarts[0]) return; // room noise, not a voice
            _loudPeak = Mathf.Max(_loudPeak, loud);
            Speaking = true;
            Level = LevelOf(loud);
            _speakingUntil = Time.time + 0.35f;

            if (_net != null && _net.ClientManager.Started)
            {
                var data = new byte[written];
                System.Array.Copy(_compressed, data, written);
                _net.ClientManager.Broadcast(new VoiceMessage { Data = data, Loudness = loud }, Channel.Unreliable);
            }
        }

        // The slice's level: RMS of the decompressed speech, in dB, mapped to 0..1 (MinDb to MaxDb).
        private float Loudness(byte[] data, uint size)
        {
            uint rate = SteamUser.GetVoiceOptimalSampleRate();
            if (!VoiceBuffer.Decompress(data, size, ref _pcm, rate, out uint bytes) || bytes < 2) return 0f;
            double sum = 0; int n = (int)bytes / 2;
            for (int i = 0; i < n; i++)
            {
                float v = (short)(_pcm[2 * i] | (_pcm[2 * i + 1] << 8)) / 32768f;
                sum += v * v;
            }
            float db = 20f * Mathf.Log10(Mathf.Sqrt((float)(sum / Mathf.Max(1, n))) + 1e-6f);
            return ToLoudness(db);
        }

        public static int LevelOf(float loudness)
        {
            int level = 0;
            for (int i = 0; i < LevelStarts.Length; i++) if (loudness >= LevelStarts[i]) level = i;
            return level;
        }

        public static float Radius(float loudness)
        {
            float r = HeardWithin[LevelOf(loudness)];
            var w = WorldConditions.Current.Weather;
            return w == Weather.Rain || w == Weather.Storm ? r * 0.5f : r;
        }

        // A voice at `at` reaches the dinosaurs within its radius, exactly like a footstep or a gunshot.
        public static float MakeNoise(Vector3 at, float loudness)
        {
            float r = Radius(loudness);
            DinosaurAI.NoiseAt(at, r);
            return r;
        }

        // ── Network ───────────────────────────────────────────────────────────

        // Host: pass the voice to everyone else, and let the dinosaurs hear it where that player stands.
        private readonly Dictionary<int, float> _serverNoiseTimers = new Dictionary<int, float>();
        private void OnServerVoice(NetworkConnection conn, VoiceMessage msg, Channel channel)
        {
            msg.Sender = conn.ClientId;
            foreach (var other in _net.ServerManager.Clients.Values)
                if (other != conn) _net.ServerManager.Broadcast(other, msg, true, Channel.Unreliable);

            if (conn.IsLocalClient) return; // the host's own voice made its noise in Update
            _serverNoiseTimers.TryGetValue(conn.ClientId, out float next);
            if (Time.time < next) return;
            _serverNoiseTimers[conn.ClientId] = Time.time + NoiseEvery;
            var body = AvatarOf(conn.ClientId);
            if (body != null) MakeNoise(body.position, msg.Loudness);
        }

        // Everyone: play a teammate's voice from their body.
        private void OnClientVoice(VoiceMessage msg, Channel channel)
        {
            if (!SteamService.Ready || msg.Data == null) return;
            var body = AvatarOf(msg.Sender);
            if (body == null) return;
            if (!_players.TryGetValue(msg.Sender, out var player) || player == null)
            {
                player = body.gameObject.AddComponent<VoicePlayer>();
                _players[msg.Sender] = player;
            }
            player.Feed(msg.Data, HearRange);
        }

        private static Transform AvatarOf(int clientId)
        {
            foreach (var a in NetAvatar.All)
                if (a != null && a.Owner != null && a.Owner.ClientId == clientId) return a.transform;
            return null;
        }

        // ── Screen ────────────────────────────────────────────────────────────

        // While you speak: how far your voice carries, so the rule is learnt in the first match.
        private GUIStyle _style;
        private void OnGUI()
        {
            if (!Speaking || !InMatch || Level < 0) return;
            if (_style == null) _style = new GUIStyle(GUI.skin.label) { fontSize = 14, fontStyle = FontStyle.Bold };
            float r = Radius(LevelStarts[Level] + 0.01f);
            _style.normal.textColor = Level >= 3 ? new Color(1f, 0.35f, 0.3f) : Level == 2 ? new Color(1f, 0.75f, 0.3f) : new Color(0.75f, 1f, 0.8f);
            GUI.Label(new Rect(14, Screen.height - 64, 520, 24), $"Voice: {LevelNames[Level]}  -  dinosaurs within {r:0} m can hear you", _style);
        }
    }

    // Plays one teammate's voice from their body: Steam-decompressed speech streamed into a 3D audio source.
    public class VoicePlayer : MonoBehaviour
    {
        private AudioSource _source;
        private uint _rate;
        private byte[] _pcm = new byte[VoiceBuffer.Bytes];
        private readonly Queue<float> _samples = new Queue<float>();
        private readonly object _lock = new object();

        private void Awake()
        {
            _rate = SteamUser.GetVoiceOptimalSampleRate();
            _source = gameObject.AddComponent<AudioSource>();
            _source.spatialBlend = 1f;
            _source.rolloffMode = AudioRolloffMode.Linear;
            _source.minDistance = 2f;
            _source.loop = true;
            _source.clip = AudioClip.Create("Voice", (int)_rate * 2, 1, (int)_rate, true, OnRead);
            _source.Play();
        }

        public void Feed(byte[] data, float range)
        {
            _source.maxDistance = range;
            _source.volume = GameSettings.VoiceVolume;
            if (!VoiceBuffer.Decompress(data, (uint)data.Length, ref _pcm, _rate, out uint bytes)) return;
            lock (_lock)
            {
                for (int i = 0; i + 1 < bytes; i += 2) _samples.Enqueue((short)(_pcm[i] | (_pcm[i + 1] << 8)) / 32768f);
                while (_samples.Count > _rate) _samples.Dequeue(); // never more than a second behind
            }
        }

        // Audio thread: next samples, silence when the speaker pauses.
        private void OnRead(float[] data)
        {
            lock (_lock)
                for (int i = 0; i < data.Length; i++) data[i] = _samples.Count > 0 ? _samples.Dequeue() : 0f;
        }
    }
}

namespace ProjectFossil.Net
{
    // Steam voice decompression into a reusable buffer. Steam can answer "buffer too small" with a needed size of
    // 0; growing to that and retrying forever froze the game's main thread, and Steam then closed the game ("stalled
    // cross-thread pipe"). Now: a 2-second buffer, one retry at most, never recursion.
    internal static class VoiceBuffer
    {
        public const int Bytes = 48000 * 2 * 2;      // 2 s of 16-bit mono at 48 kHz
        private const int MaxBytes = 1024 * 1024;

        public static bool Decompress(byte[] data, uint size, ref byte[] pcm, uint rate, out uint bytes)
        {
            bytes = 0;
            if (data == null || size == 0) return false;
            var result = SteamUser.DecompressVoice(data, size, pcm, (uint)pcm.Length, out bytes, rate);
            if (result == EVoiceResult.k_EVoiceResultBufferTooSmall && bytes > pcm.Length && bytes <= MaxBytes)
            {
                pcm = new byte[bytes];
                result = SteamUser.DecompressVoice(data, size, pcm, (uint)pcm.Length, out bytes, rate);
            }
            return result == EVoiceResult.k_EVoiceResultOK;
        }
    }
}
