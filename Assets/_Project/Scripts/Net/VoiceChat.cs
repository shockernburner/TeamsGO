using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using FishNet.Broadcast;
using FishNet.Connection;
using FishNet.Managing;
using FishNet.Transporting;
using ProjectFossil.Core;
using ProjectFossil.Dinosaurs;

namespace ProjectFossil.Net
{
    // Joiner -> host: how loud this player is speaking (never the sound itself), so the host's dinosaurs hear it.
    public struct VoiceLevelMessage : IBroadcast
    {
        public float Loudness; // 0..1
    }

    // Voices, and dinosaurs that hear them. The microphone's loudness (MicLevel, through Unity, so it works in every
    // build and in solo) becomes a noise event where the speaker stands: a whisper stays secret a metre from a
    // raptor and a shout brings the island. Dinosaurs live on the host, so a joiner sends only its loudness there.
    // Teammates hear each other online through Vivox (VivoxVoice), from where each one stands.
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

        public static VoiceChat Instance { get; private set; }

        public bool  Speaking   { get; private set; }
        public int   Level      { get; private set; } = -1;   // the local player's current level, -1 = silent
        public float LastHeard  { get; private set; }         // metres, what the last noise event reached

        private NetworkManager _net;
        private readonly MicLevel _mic = new MicLevel();
        private float _loudPeak, _noiseTimer, _speakingUntil;

        private void Awake() => Instance = this;
        private void OnDestroy() { if (Instance == this) Instance = null; _mic.Stop(); }

        // Called by NetSession whenever it builds its network.
        public void Attach(NetworkManager net)
        {
            _net = net;
            _net.ServerManager.RegisterBroadcast<VoiceLevelMessage>(OnServerLevel);
        }

        private static Transform LocalPlayer =>
            NetSession.Instance != null && NetSession.Instance.Match != null && NetSession.Instance.Match.Player != null
                ? NetSession.Instance.Match.Player.transform : null;

        private bool InMatch => NetSession.Instance != null && NetSession.Instance.Match != null && NetSession.Instance.Match.IsRunning;

        // ── Capture ───────────────────────────────────────────────────────────

        private void Update()
        {
            var me = LocalPlayer;
            bool inMatch = InMatch && me != null && GameSettings.Voice != VoiceMode.Off;
            bool talking = inMatch && (GameSettings.Voice == VoiceMode.OpenMic ||
                                       (Keyboard.current != null && Keyboard.current.tKey.isPressed));
            if (inMatch) _mic.Start(); else _mic.Stop();
            VivoxVoice.Tick(me, talking);

            if (talking && _mic.Running)
            {
                float loud = ToLoudness(_mic.Db());
                if (loud >= LevelStarts[0]) // louder than room noise: a voice
                {
                    _loudPeak = Mathf.Max(_loudPeak, loud);
                    Speaking = true;
                    Level = LevelOf(loud);
                    _speakingUntil = Time.time + 0.35f;
                }
            }
            if (Time.time > _speakingUntil) { Speaking = false; Level = -1; }

            _noiseTimer -= Time.deltaTime;
            if (_noiseTimer <= 0f && _loudPeak > 0f)
            {
                _noiseTimer = NoiseEvery;
                // Dinosaurs live on the host: the host and a solo player make the noise here, a joiner tells the host.
                if (!NetRole.IsFollower) LastHeard = MakeNoise(me != null ? me.position : Vector3.zero, _loudPeak);
                else
                {
                    LastHeard = Radius(_loudPeak);
                    if (_net != null && _net.ClientManager.Started)
                        _net.ClientManager.Broadcast(new VoiceLevelMessage { Loudness = _loudPeak }, Channel.Unreliable);
                }
                _loudPeak = 0f;
            }
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

        // Host: a joiner is speaking; the dinosaurs hear it where that player stands.
        private void OnServerLevel(NetworkConnection conn, VoiceLevelMessage msg, Channel channel)
        {
            if (conn.IsLocalClient) return; // the host's own voice made its noise in Update
            var body = AvatarOf(conn.ClientId);
            if (body != null) MakeNoise(body.position, Mathf.Clamp01(msg.Loudness));
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
            if (!InMatch) return;
            if (_style == null) _style = new GUIStyle(GUI.skin.label) { fontSize = 14, fontStyle = FontStyle.Bold };
            if (!string.IsNullOrEmpty(VivoxVoice.Status))
            {
                _style.normal.textColor = new Color(1f, 0.75f, 0.3f);
                GUI.Label(new Rect(14, Screen.height - 88, 520, 24), VivoxVoice.Status, _style);
            }
            if (!Speaking || Level < 0) return;
            float r = Radius(LevelStarts[Level] + 0.01f);
            _style.normal.textColor = Level >= 3 ? new Color(1f, 0.35f, 0.3f) : Level == 2 ? new Color(1f, 0.75f, 0.3f) : new Color(0.75f, 1f, 0.8f);
            GUI.Label(new Rect(14, Screen.height - 64, 520, 24), $"Voice: {LevelNames[Level]}  -  dinosaurs within {r:0} m can hear you", _style);
        }
    }
}
