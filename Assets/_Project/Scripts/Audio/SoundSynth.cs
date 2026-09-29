using System;

namespace ProjectFossil.Audio
{
    // Placeholder sound effects built from oscillators, noise and envelopes, so the game has audio before any
    // recorded sounds are licensed. Pure C# (no UnityEngine): every recipe returns mono samples in -1..1 and is
    // deterministic for a given variant, which keeps it testable. Swap a recipe for a real clip at any time.
    public static class SoundSynth
    {
        public const int SampleRate = 22050;

        // ── Recipes ────────────────────────────────────────────────────────────

        // Soft thud of a foot on earth. soft = crouching/crawling.
        public static float[] Footstep(int variant, bool soft)
        {
            var rng = new Random(1000 + variant);
            var s = Buffer(0.12f);
            var lp = new OnePole(soft ? 450f : 850f);
            for (int i = 0; i < s.Length; i++)
            {
                float t = T(i);
                float env = Exp(t, 0.002f, 0.035f);
                float thump = (float)Math.Sin(Tau * 85f * t) * Exp(t, 0.001f, 0.03f);
                s[i] = lp.Next(Noise(rng)) * env * 0.9f + thump * 0.5f;
            }
            return Finish(s, soft ? 0.35f : 0.6f);
        }

        // Raptor call: a rasping, falling screech with fast vibrato.
        public static float[] Screech(int variant)
        {
            var rng = new Random(2000 + variant);
            float start = 1000f + variant * 90f, end = 430f + variant * 40f;
            var s = Buffer(0.85f);
            var bp = new Bandpass(1400f, 1.2f);
            double phase = 0;
            for (int i = 0; i < s.Length; i++)
            {
                float t = T(i), k = t / 0.85f;
                float f = Lerp(start, end, k * k) + 45f * (float)Math.Sin(Tau * 23f * t);
                phase += f / SampleRate;
                float saw = (float)(2.0 * (phase - Math.Floor(phase + 0.5)));
                float rasp = bp.Next(Noise(rng)) * 0.8f;
                float env = Adsr(t, 0.04f, 0.1f, 0.7f, 0.85f, 0.35f);
                s[i] = (saw * 0.55f + rasp) * env;
            }
            return Finish(s, 0.8f);
        }

        // Big predator roar. Laptop and phone speakers play almost nothing below ~200 Hz, so the body of the sound
        // lives in throat formants (roughly 350 Hz to 2 kHz) and the sub rumble is only a bonus for headphones.
        public static float[] Roar(int variant)
        {
            var rng = new Random(3000 + variant);
            const float len = 2.6f;
            var s = Buffer(len);
            var chest  = new Bandpass(380f + variant * 20f, 2.5f);
            var throat = new Bandpass(900f, 3f);
            var rasp   = new Bandpass(1800f - variant * 60f, 4f);
            var sub    = new OnePole(140f);
            double phase = 0, phase2 = 0;
            for (int i = 0; i < s.Length; i++)
            {
                float t = T(i), k = t / len;
                // Swell up in pitch, then sag as the breath runs out.
                float f = t < 0.35f ? Lerp(100f, 165f - variant * 7f, t / 0.35f) : Lerp(165f - variant * 7f, 78f, (t - 0.35f) / (len - 0.35f));
                phase  += f / SampleRate;
                phase2 += f * 1.013f / SampleRate;
                float saw = (float)(2.0 * (phase - Math.Floor(phase + 0.5)) + (phase2 - Math.Floor(phase2 + 0.5)));
                float growl = 0.6f + 0.4f * (float)Math.Sin(Tau * (26f + variant * 2f) * t);
                float noise = Noise(rng);
                throat.SetFrequency(Lerp(1050f, 650f, k));
                float src = saw * growl + noise * 0.7f;
                float body = chest.Next(src) * 1.4f + throat.Next(src) * 1.1f + rasp.Next(noise * growl) * 0.9f;
                float env = Adsr(t, 0.15f, 0.3f, 0.8f, len, 1.0f);
                s[i] = (body * 2.2f + sub.Next(saw) * 0.5f) * env;
            }
            return Finish(s, 0.97f);
        }

        // Jaws snapping: a sharp click, a crunch and a low thump.
        public static float[] Bite(int variant)
        {
            var rng = new Random(4000 + variant);
            var s = Buffer(0.22f);
            var hp = new HighPass(1800f);
            for (int i = 0; i < s.Length; i++)
            {
                float t = T(i);
                float click = hp.Next(Noise(rng)) * Exp(t, 0.0005f, 0.012f);
                float crunch = Noise(rng) * Exp(t - 0.02f, 0.002f, 0.05f) * (t > 0.02f ? 0.5f : 0f);
                float thump = (float)Math.Sin(Tau * 110f * t) * Exp(t, 0.002f, 0.06f);
                s[i] = click * 1.2f + crunch + thump * 0.7f;
            }
            return Finish(s, 0.85f);
        }

        // Weapon swing: a quick whoosh with a rising band of noise.
        public static float[] Swing(int variant)
        {
            var rng = new Random(5000 + variant);
            const float len = 0.26f;
            var s = Buffer(len);
            var bp = new Bandpass(700f, 2f);
            for (int i = 0; i < s.Length; i++)
            {
                float t = T(i), k = t / len;
                bp.SetFrequency(Lerp(500f, 2600f + variant * 150f, k));
                float env = (float)Math.Sin(Math.PI * k);
                s[i] = bp.Next(Noise(rng)) * env * env;
            }
            return Finish(s, 0.45f);
        }

        // Blow landing on a body.
        public static float[] Hit(int variant)
        {
            var rng = new Random(6000 + variant);
            var s = Buffer(0.16f);
            var lp = new OnePole(1200f);
            for (int i = 0; i < s.Length; i++)
            {
                float t = T(i);
                float body = (float)Math.Sin(Tau * (75f + variant * 8f) * t) * Exp(t, 0.001f, 0.05f);
                s[i] = body + lp.Next(Noise(rng)) * Exp(t, 0.001f, 0.02f) * 0.8f;
            }
            return Finish(s, 0.75f);
        }

        // Player hurt: short low grunt.
        public static float[] Hurt(int variant)
        {
            var rng = new Random(7000 + variant);
            const float len = 0.3f;
            var s = Buffer(len);
            var lp = new OnePole(700f);
            double phase = 0;
            for (int i = 0; i < s.Length; i++)
            {
                float t = T(i);
                phase += Lerp(150f + variant * 10f, 95f, t / len) / SampleRate;
                float saw = (float)(2.0 * (phase - Math.Floor(phase + 0.5)));
                s[i] = lp.Next(saw + Noise(rng) * 0.3f) * Adsr(t, 0.01f, 0.05f, 0.7f, len, 0.15f);
            }
            return Finish(s, 0.6f);
        }

        // Coins: two bright blips.
        public static float[] Coin()
        {
            var s = Buffer(0.3f);
            for (int i = 0; i < s.Length; i++)
            {
                float t = T(i);
                float a = (float)Math.Sin(Tau * 1319f * t) * Exp(t, 0.002f, 0.06f);
                float b = t > 0.07f ? (float)Math.Sin(Tau * 1760f * t) * Exp(t - 0.07f, 0.002f, 0.09f) : 0f;
                s[i] = a * 0.6f + b * 0.7f;
            }
            return Finish(s, 0.45f);
        }

        // Threat warning: a low beating drone that swells, with a distant growl in it.
        public static float[] ThreatSting()
        {
            var rng = new Random(8000);
            const float len = 2.8f;
            var s = Buffer(len);
            var lp = new OnePole(300f);
            for (int i = 0; i < s.Length; i++)
            {
                float t = T(i);
                float drone = (float)(Math.Sin(Tau * 55f * t) + Math.Sin(Tau * 58.5f * t) + 0.5 * Math.Sin(Tau * 110f * t));
                float growl = lp.Next(Noise(rng)) * 2.5f;
                s[i] = (drone * 0.4f + growl) * Adsr(t, 1.2f, 0.3f, 0.8f, len, 1.0f);
            }
            return Finish(s, 0.7f);
        }

        // Extraction open: a rising three-note chime.
        public static float[] Chime()
        {
            var s = Buffer(1.4f);
            float[] notes = { 1047f, 1319f, 1568f };
            for (int i = 0; i < s.Length; i++)
            {
                float t = T(i), v = 0f;
                for (int n = 0; n < notes.Length; n++)
                {
                    float tn = t - n * 0.16f;
                    if (tn < 0f) continue;
                    v += (float)(Math.Sin(Tau * notes[n] * tn) + 0.3 * Math.Sin(Tau * notes[n] * 2f * tn)) * Exp(tn, 0.004f, 0.35f);
                }
                s[i] = v;
            }
            return Finish(s, 0.5f);
        }

        // A big animal sniffing: two or three quick wet inhales, then a snort out through the nose.
        public static float[] Sniff(int variant)
        {
            var rng = new Random(10000 + variant);
            int pulls = 2 + variant % 2;
            float len = pulls * 0.2f + 0.45f;
            var s = Buffer(len);
            var bp = new Bandpass(900f, 2.5f);
            var lp = new OnePole(500f);
            for (int i = 0; i < s.Length; i++)
            {
                float t = T(i), v = 0f;
                for (int p = 0; p < pulls; p++)
                {
                    float tp = t - p * 0.2f;
                    if (tp < 0f || tp > 0.16f) continue;
                    bp.SetFrequency(Lerp(700f, 1500f + variant * 80f, tp / 0.16f)); // rises as the breath is drawn
                    v += bp.Next(Noise(rng)) * (float)Math.Sin(Math.PI * tp / 0.16f) * 1.6f;
                }
                float ts = t - pulls * 0.2f - 0.05f; // the snort
                if (ts > 0f) v += lp.Next(Noise(rng)) * Exp(ts, 0.01f, 0.09f) * 3f
                                 + (float)Math.Sin(Tau * 70f * ts) * Exp(ts, 0.005f, 0.07f) * 0.6f;
                s[i] = v;
            }
            return Finish(s, 0.8f);
        }

        // One heartbeat: lub-dub, low and felt more than heard.
        public static float[] Heartbeat()
        {
            var s = Buffer(0.5f);
            for (int i = 0; i < s.Length; i++)
            {
                float t = T(i);
                float lub = (float)Math.Sin(Tau * 55f * t) * Exp(t, 0.008f, 0.05f);
                float dub = (float)Math.Sin(Tau * 68f * (t - 0.2f)) * Exp(t - 0.2f, 0.008f, 0.04f) * 0.7f;
                // A little upper harmonic so laptop speakers carry it.
                s[i] = lub + dub + (float)Math.Sin(Tau * 165f * t) * Exp(t, 0.005f, 0.03f) * 0.25f;
            }
            return Finish(s, 0.9f);
        }

        // Helicopter loop: blade slaps (whole number per loop so it repeats seamlessly) over a turbine whine.
        public static float[] Rotor(float seconds = 2f)
        {
            var rng = new Random(11000);
            var s = Buffer(seconds);
            const float slaps = 11f; // per second
            var bp = new Bandpass(420f, 1.5f);
            var lp = new OnePole(250f);
            for (int i = 0; i < s.Length; i++)
            {
                float t = T(i);
                float phase = t * slaps - (float)Math.Floor(t * slaps);
                float slap = bp.Next(Noise(rng)) * Exp(phase / slaps, 0.002f, 0.018f) * 2.2f;
                float thump = (float)Math.Sin(Tau * 60f * phase / slaps) * Exp(phase / slaps, 0.002f, 0.025f);
                float wash = lp.Next(Noise(rng)) * 0.9f;
                float whine = (float)Math.Sin(Tau * 1800f * t) * 0.04f + (float)Math.Sin(Tau * 3600f * t) * 0.015f;
                s[i] = slap + thump * 0.8f + wash + whine;
            }
            return Finish(s, 0.7f);
        }

        // Made it out: a bright rising fanfare that lands on a big major chord.
        public static float[] Victory()
        {
            const float len = 3.2f;
            var s = Buffer(len);
            float[] notes = { 523f, 659f, 784f, 1047f };     // C E G C, one after another
            float[] chord = { 523f, 659f, 784f, 1047f, 1319f }; // then all together
            for (int i = 0; i < s.Length; i++)
            {
                float t = T(i), v = 0f;
                for (int n = 0; n < notes.Length; n++)
                {
                    float tn = t - n * 0.13f;
                    if (tn < 0f || tn > 0.5f) continue;
                    v += Brass(notes[n], tn) * Exp(tn, 0.01f, 0.18f);
                }
                float tc = t - 0.6f;
                if (tc > 0f)
                    foreach (var f in chord)
                        v += Brass(f, tc) * Adsr(tc, 0.03f, 0.3f, 0.6f, len - 0.6f, 1.2f) * 0.55f;
                s[i] = v;
            }
            return Finish(s, 0.85f);
        }

        // A brassy tone: a few harmonics with a touch of vibrato.
        private static float Brass(float f, float t)
        {
            float vib = 1f + 0.004f * (float)Math.Sin(Tau * 5.5f * t);
            double w = Tau * f * vib * t;
            return (float)(Math.Sin(w) + 0.5 * Math.Sin(2 * w) + 0.3 * Math.Sin(3 * w) + 0.15 * Math.Sin(4 * w)) * 0.5f;
        }

        // Seamless background loop: gusting wind plus scattered bird chirps.
        public static float[] Ambience(float seconds = 12f, int seed = 9000)
        {
            var rng = new Random(seed);
            var s = Buffer(seconds);
            var lp = new OnePole(380f);
            var lp2 = new OnePole(900f);
            for (int i = 0; i < s.Length; i++)
            {
                float t = T(i);
                // Gusts: whole cycles per loop so the start and end match.
                float gust = 0.55f + 0.3f * (float)Math.Sin(Tau * t * 2f / seconds) + 0.15f * (float)Math.Sin(Tau * t * 5f / seconds + 1.3f);
                s[i] = (lp.Next(Noise(rng)) * 2.2f + lp2.Next(Noise(rng)) * 0.25f) * gust;
            }

            int birds = 5 + rng.Next(4);
            for (int b = 0; b < birds; b++)
            {
                float at = (float)rng.NextDouble() * (seconds - 1.2f) + 0.3f;
                float baseF = 2400f + (float)rng.NextDouble() * 1600f;
                int chirps = 2 + rng.Next(3);
                for (int c = 0; c < chirps; c++)
                {
                    float c0 = at + c * 0.13f;
                    int i0 = (int)(c0 * SampleRate), n = (int)(0.07f * SampleRate);
                    for (int j = 0; j < n && i0 + j < s.Length; j++)
                    {
                        float tj = j / (float)SampleRate, k = tj / 0.07f;
                        float f = baseF + 900f * k;
                        s[i0 + j] += (float)Math.Sin(Tau * f * tj) * (float)Math.Sin(Math.PI * k) * 0.18f;
                    }
                }
            }

            // Crossfade the tail into the head so the loop has no click.
            int fade = SampleRate / 2;
            for (int i = 0; i < fade; i++)
            {
                float k = i / (float)fade;
                s[i] = s[i] * k + s[s.Length - fade + i] * (1f - k);
            }
            Array.Resize(ref s, s.Length - fade);
            return Finish(s, 0.5f);
        }

        // ── Building blocks ────────────────────────────────────────────────────

        private const float Tau = (float)(Math.PI * 2.0);

        private static float[] Buffer(float seconds) => new float[Math.Max(1, (int)(seconds * SampleRate))];
        private static float T(int i) => i / (float)SampleRate;
        private static float Lerp(float a, float b, float t) => a + (b - a) * Math.Max(0f, Math.Min(1f, t));
        private static float Noise(Random rng) => (float)(rng.NextDouble() * 2.0 - 1.0);

        // Linear attack then exponential decay with the given time constant.
        private static float Exp(float t, float attack, float decay)
        {
            if (t < 0f) return 0f;
            if (t < attack) return t / attack;
            return (float)Math.Exp(-(t - attack) / decay);
        }

        private static float Adsr(float t, float a, float d, float sustain, float length, float release)
        {
            if (t < 0f || t > length) return 0f;
            float v = t < a ? t / a : t < a + d ? 1f - (1f - sustain) * (t - a) / d : sustain;
            float r = length - t;
            return r < release ? v * r / release : v;
        }

        // Soft-clip, normalise the peak to `peak`, and fade the last few ms to avoid clicks.
        private static float[] Finish(float[] s, float peak)
        {
            float max = 1e-6f;
            for (int i = 0; i < s.Length; i++)
            {
                s[i] = (float)Math.Tanh(s[i]);
                max = Math.Max(max, Math.Abs(s[i]));
            }
            float g = peak / max;
            int tail = Math.Min(s.Length, SampleRate / 200);
            for (int i = 0; i < s.Length; i++)
            {
                s[i] *= g;
                int fromEnd = s.Length - 1 - i;
                if (fromEnd < tail) s[i] *= fromEnd / (float)tail;
            }
            return s;
        }

        private sealed class OnePole
        {
            private readonly float _a;
            private float _y;
            public OnePole(float cutoff) { _a = 1f - (float)Math.Exp(-Tau * cutoff / SampleRate); }
            public float Next(float x) => _y += _a * (x - _y);
        }

        private sealed class HighPass
        {
            private readonly OnePole _lp;
            public HighPass(float cutoff) { _lp = new OnePole(cutoff); }
            public float Next(float x) => x - _lp.Next(x);
        }

        // Chamberlin state-variable filter, band-pass output.
        private sealed class Bandpass
        {
            private float _f, _low, _band;
            private readonly float _q;
            public Bandpass(float freq, float q) { _q = 1f / Math.Max(0.5f, q); SetFrequency(freq); }
            public void SetFrequency(float freq) => _f = 2f * (float)Math.Sin(Math.PI * Math.Min(freq, SampleRate / 6f) / SampleRate);
            public float Next(float x)
            {
                _low += _f * _band;
                float high = x - _low - _q * _band;
                _band += _f * high;
                return _band;
            }
        }
    }
}
