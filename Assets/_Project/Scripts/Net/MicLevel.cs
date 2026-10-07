using UnityEngine;

namespace ProjectFossil.Net
{
    // How loud the player is speaking right now, from the default microphone through Unity itself, so it works in
    // every build (itch, Steam, offline) and in solo. Only the loudness is used, never the sound: teammates hear the
    // voice through Vivox, which records the same microphone on its own.
    public class MicLevel
    {
        private const int Rate = 16000;
        private const int Window = Rate / 20; // 50 ms

        private AudioClip _clip;
        private string _device;
        private readonly float[] _buffer = new float[Window];

        public bool Running => _clip != null;

        public void Start()
        {
            if (_clip != null || Microphone.devices.Length == 0) return;
            _device = null; // the system default
            _clip = Microphone.Start(_device, true, 1, Rate);
        }

        public void Stop()
        {
            if (_clip == null) return;
            Microphone.End(_device);
            Object.Destroy(_clip);
            _clip = null;
        }

        // RMS of the last 50 ms, in dB (about -80 for silence, 0 for full scale).
        public float Db()
        {
            if (_clip == null) return -80f;
            int pos = Microphone.GetPosition(_device) - Window;
            if (pos < 0) pos += _clip.samples;
            if (!_clip.GetData(_buffer, pos)) return -80f;
            double sum = 0;
            for (int i = 0; i < _buffer.Length; i++) sum += _buffer[i] * _buffer[i];
            return 20f * Mathf.Log10(Mathf.Sqrt((float)(sum / _buffer.Length)) + 1e-6f);
        }
    }
}
