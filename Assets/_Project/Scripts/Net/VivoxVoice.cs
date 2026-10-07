using System;
using System.Threading.Tasks;
using ProjectFossil.Core;
using Unity.Services.Vivox;
using UnityEngine;

namespace ProjectFossil.Net
{
    // Teammates' voices online, through Vivox (Unity Gaming Services): one positional channel per online match,
    // named after its join code, so voices come from where each player stands and fade out by HearRange metres.
    // Push to talk mutes the microphone between presses; Off never joins. Needs the signed-in account.
    public static class VivoxVoice
    {
        private const int HearRange = 35, ClearWithin = 3;

        public static string Channel { get; private set; }
        public static string Status { get; private set; }
        private static bool _loggedIn, _joining;

        public static bool InChannel => Channel != null && !_joining;

        public static async void Join(string code)
        {
            if (string.IsNullOrEmpty(code) || !Account.SignedIn || GameSettings.Voice == VoiceMode.Off) return;
            string channel = "tether_" + code.ToLowerInvariant();
            if (Channel == channel || _joining) return;
            _joining = true;
            try
            {
                if (!_loggedIn)
                {
                    await VivoxService.Instance.InitializeAsync();
                    await VivoxService.Instance.LoginAsync(new LoginOptions { DisplayName = Account.Username });
                    _loggedIn = true;
                    GameSettings.VoiceChanged += ApplySettings;
                }
                if (Channel != null) await VivoxService.Instance.LeaveChannelAsync(Channel);
                var props = new Channel3DProperties(HearRange, ClearWithin, 1f, AudioFadeModel.InverseByDistance);
                await VivoxService.Instance.JoinPositionalChannelAsync(channel, ChatCapability.AudioOnly, props);
                Channel = channel;
                Status = null;
                ApplySettings();
            }
            catch (Exception e)
            {
                Channel = null;
                Status = "Voice chat couldn't connect.";
                Debug.Log($"[VivoxVoice] {e.Message}");
            }
            finally { _joining = false; }
        }

        public static async void Leave()
        {
            string channel = Channel;
            Channel = null;
            if (channel == null) return;
            try { await VivoxService.Instance.LeaveChannelAsync(channel); }
            catch (Exception e) { Debug.Log($"[VivoxVoice] Leave: {e.Message}"); }
        }

        // Every frame in an online match: where this player stands and listens, and whether the mic is open.
        public static void Tick(Transform player, bool talking)
        {
            if (!InChannel || player == null) return;
            try
            {
                VivoxService.Instance.Set3DPosition(player.gameObject, Channel);
                bool muted = VivoxService.Instance.IsInputDeviceMuted;
                if (talking && muted) VivoxService.Instance.UnmuteInputDevice();
                else if (!talking && !muted) VivoxService.Instance.MuteInputDevice();
            }
            catch (Exception e) { Debug.Log($"[VivoxVoice] {e.Message}"); }
        }

        // Voice volume from Settings: 0..1 maps to Vivox's -50..0 (0 = as recorded).
        public static void ApplySettings()
        {
            if (!_loggedIn) return;
            VivoxService.Instance.SetOutputDeviceVolume(Mathf.RoundToInt(Mathf.Lerp(-50f, 0f, GameSettings.VoiceVolume)));
        }
    }
}
