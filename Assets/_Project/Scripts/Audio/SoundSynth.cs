using System;

namespace ProjectFossil.Audio
{
    // Placeholder sound effects built from oscillators, noise and envelopes, so the game has audio before any
    // recorded sounds are licensed. Pure C# (no UnityEngine): every recipe returns mono samples in -1..1 and is
    // deterministic for a given variant, which keeps it testable. Swap a recipe for a real clip at any time.
    public static partial class SoundSynth
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

        // Death: a last few heartbeats that slow and stop, under a low minor-chord swell that dies away.
        public static float[] Lament()
        {
            var s = Buffer(6f);
            var rng = new Random(9700);
            var lp = new OnePole(900f);
            float[] chord = { 110f, 130.81f, 164.81f, 220f }; // A minor, low
            float[] beats = { 0f, 0.95f, 2.1f, 3.6f };         // slowing, then nothing
            for (int i = 0; i < s.Length; i++)
            {
                float t = T(i), pad = 0f;
                foreach (var f in chord)
                    for (int d = -1; d <= 1; d++) // three slightly detuned voices per note: a soft string section
                    {
                        double ph = Tau * f * (1f + d * 0.004f) * t;
                        pad += (float)(Math.Sin(ph) + 0.35 * Math.Sin(2 * ph) + 0.15 * Math.Sin(3 * ph));
                    }
                float swell = Adsr(t - 0.6f, 1.6f, 1.5f, 0.6f, 5.4f, 2.6f);
                float v = lp.Next(pad * 0.06f * swell * (1f + 0.08f * (float)Math.Sin(Tau * 4.5f * t)));
                for (int b = 0; b < beats.Length; b++)
                {
                    float tb = t - beats[b], k = 1f - b * 0.22f;
                    if (tb < 0f || tb > 0.5f) continue;
                    v += k * (float)Math.Sin(Tau * 48f * tb) * Exp(tb, 0.004f, 0.07f);
                    float t2 = tb - 0.24f;
                    if (t2 > 0f) v += 0.7f * k * (float)Math.Sin(Tau * 42f * t2) * Exp(t2, 0.004f, 0.08f);
                }
                s[i] = v + Noise(rng) * 0.002f;
            }
            return Finish(s, 0.7f);
        }

        // A radio opening: a click, a short burst of band-limited static, then a quieter hiss that holds.
        public static float[] RadioSquelch()
        {
            var s = Buffer(0.9f);
            var rng = new Random(9800);
            var bp = new Bandpass(1800f, 0.8f);
            for (int i = 0; i < s.Length; i++)
            {
                float t = T(i);
                float level = t < 0.012f ? 1f : t < 0.22f ? 0.55f : 0.12f * (1f - (t - 0.22f) / 0.68f);
                float click = t < 0.004f ? 1f - t / 0.004f : 0f;
                s[i] = bp.Next(Noise(rng)) * level + click * 0.6f;
            }
            return Finish(s, 0.45f);
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

        // Helicopter loop. A real one is mostly felt: a deep "whop" each time a blade passes (about nine a
        // second, a whole number per loop so it repeats seamlessly), a rush of rotor wash, and only a faint
        // turbine whine. The old loop slapped high and fast and sounded like a toy.
        public static float[] Rotor(float seconds = 2f)
        {
            var rng = new Random(11000);
            var s = Buffer(seconds);
            const float slaps = 9f; // per second
            var body = new OnePole(140f);    // the chest-thump of each pass
            var crack = new Bandpass(260f, 1.2f);
            var wash = new OnePole(380f);
            var rumble = new OnePole(60f);
            for (int i = 0; i < s.Length; i++)
            {
                float t = T(i);
                float phase = t * slaps - (float)Math.Floor(t * slaps);
                float tp = phase / slaps;
                float env = Exp(tp, 0.004f, 0.035f);
                float whop = (float)Math.Sin(Tau * 48f * tp) * env * 1.4f + body.Next(Noise(rng)) * env * 2.2f;
                float edge = crack.Next(Noise(rng)) * Exp(tp, 0.002f, 0.012f) * 0.6f;
                // The wash swells with each pass, so it breathes with the blades instead of hissing flat.
                float breathe = 0.65f + 0.35f * (float)Math.Cos(Tau * phase);
                float air = wash.Next(Noise(rng)) * 0.55f * breathe + rumble.Next(Noise(rng)) * 1.2f;
                float whine = (float)Math.Sin(Tau * 2350f * t) * 0.008f;
                s[i] = whop + edge + air + whine;
            }
            return Finish(s, 0.8f);
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

        // Steady rain on leaves: hiss plus scattered drops, looping seamlessly.
        public static float[] Rain(float seconds = 6f, int seed = 9100)
        {
            var rng = new Random(seed);
            var s = Buffer(seconds);
            var hp = new HighPass(700f);
            var lp = new OnePole(5200f);
            for (int i = 0; i < s.Length; i++)
                s[i] = lp.Next(hp.Next(Noise(rng))) * 0.9f;

            // Drops: short ticks on leaves, a few hundred a second.
            int drops = (int)(seconds * 260f);
            for (int d = 0; d < drops; d++)
            {
                int i0 = rng.Next(s.Length);
                float f = 1800f + (float)rng.NextDouble() * 3000f, amp = 0.1f + (float)rng.NextDouble() * 0.25f;
                int n = (int)(0.012f * SampleRate);
                for (int j = 0; j < n && i0 + j < s.Length; j++)
                {
                    float tj = j / (float)SampleRate;
                    s[i0 + j] += (float)Math.Sin(Tau * f * tj) * Exp(tj, 0.0005f, 0.003f) * amp;
                }
            }
            return Loop(s, 0.45f);
        }

        // A small waterfall into a pool: a low rush with bubbling, looping seamlessly.
        public static float[] Waterfall(float seconds = 5f, int seed = 9600)
        {
            var rng = new Random(seed);
            var s = Buffer(seconds);
            var lp = new OnePole(1400f);
            var hp = new HighPass(120f);
            var air = new HighPass(2500f);
            for (int i = 0; i < s.Length; i++)
            {
                float n = Noise(rng);
                s[i] = hp.Next(lp.Next(n)) * 1.6f + air.Next(n) * 0.12f;
            }
            // Bubbles: short rising blips in the pool.
            int bubbles = (int)(seconds * 70f);
            for (int b = 0; b < bubbles; b++)
            {
                int i0 = rng.Next(s.Length);
                float f0 = 350f + (float)rng.NextDouble() * 700f, amp = 0.05f + (float)rng.NextDouble() * 0.12f;
                int n = (int)(0.03f * SampleRate);
                for (int j = 0; j < n && i0 + j < s.Length; j++)
                {
                    float tj = j / (float)SampleRate;
                    s[i0 + j] += (float)Math.Sin(Tau * f0 * (1f + tj * 20f) * tj) * Exp(tj, 0.002f, 0.012f) * amp;
                }
            }
            return Loop(s, 0.5f);
        }

        // Thunder: a sharp crack (close strikes) rolling into a long low rumble.
        public static float[] Thunder(int variant)
        {
            var rng = new Random(9200 + variant);
            const float len = 5.5f;
            var s = Buffer(len);
            var low = new OnePole(160f + variant * 25f);
            var mid = new Bandpass(420f, 1.5f);
            float crack = variant % 2 == 0 ? 0.7f : 0.25f;
            for (int i = 0; i < s.Length; i++)
            {
                float t = T(i);
                // Rolling: a few swells as the sound comes back off hills and cloud.
                float roll = 0.6f + 0.4f * (float)Math.Sin(Tau * (0.7f + variant * 0.15f) * t + variant);
                float env = Exp(t, 0.02f, 1.6f) * roll;
                float n = Noise(rng);
                s[i] = (low.Next(n) * 3.2f + mid.Next(n) * 0.5f) * env + Noise(rng) * crack * Exp(t, 0.001f, 0.06f);
            }
            return Finish(s, 0.9f);
        }

        // Pterosaur cry from high up: a thin, wavering two-part squawk.
        public static float[] SkyCall(int variant)
        {
            var rng = new Random(9300 + variant);
            const float len = 0.9f;
            var s = Buffer(len);
            var bp = new Bandpass(2200f + variant * 150f, 2f);
            double phase = 0;
            for (int i = 0; i < s.Length; i++)
            {
                float t = T(i);
                bool second = t > 0.42f;
                float tt = second ? t - 0.42f : t;
                float k = tt / 0.4f;
                float f = (second ? 1500f : 1900f) + variant * 80f - 600f * k + 60f * (float)Math.Sin(Tau * 31f * t);
                phase += f / SampleRate;
                float tone = (float)Math.Sin(Tau * phase) + 0.35f * (float)Math.Sin(Tau * 2.0 * phase);
                float env = Adsr(tt, 0.03f, 0.08f, 0.6f, 0.4f, 0.15f);
                s[i] = (tone * 0.6f + bp.Next(Noise(rng)) * 0.5f) * env;
            }
            return Finish(s, 0.7f);
        }

        // Night loop: insects trilling in pulses over a low breeze.
        public static float[] NightAmbience(float seconds = 10f, int seed = 9400)
        {
            var rng = new Random(seed);
            var s = Buffer(seconds);
            var lp = new OnePole(260f);
            for (int i = 0; i < s.Length; i++)
                s[i] = lp.Next(Noise(rng)) * 1.4f;

            int insects = 4;
            for (int b = 0; b < insects; b++)
            {
                float f = 3800f + b * 450f + (float)rng.NextDouble() * 200f;
                float rate = 2.5f + (float)rng.NextDouble() * 2.5f;   // chirps per second
                float amp = 0.05f + 0.03f * b;
                float offset = (float)rng.NextDouble();
                for (int i = 0; i < s.Length; i++)
                {
                    float t = T(i);
                    float pulse = (t * rate + offset) % 1f;
                    if (pulse > 0.35f) continue;
                    float trill = 0.5f + 0.5f * (float)Math.Sin(Tau * 45f * t);
                    s[i] += (float)Math.Sin(Tau * f * t) * trill * (float)Math.Sin(Math.PI * pulse / 0.35f) * amp;
                }
            }
            return Loop(s, 0.45f);
        }

        // Crossfade the tail into the head so a loop has no click, then finish.
        private static float[] Loop(float[] s, float peak)
        {
            int fade = Math.Min(SampleRate / 2, s.Length / 4);
            for (int i = 0; i < fade; i++)
            {
                float k = i / (float)fade;
                s[i] = s[i] * k + s[s.Length - fade + i] * (1f - k);
            }
            Array.Resize(ref s, s.Length - fade);
            return FinishLoop(s, peak);
        }

        // Like Finish, without the fade at the end (a loop's end runs straight into its start).
        private static float[] FinishLoop(float[] s, float peak)
        {
            float max = 1e-6f;
            for (int i = 0; i < s.Length; i++)
            {
                s[i] = (float)Math.Tanh(s[i]);
                max = Math.Max(max, Math.Abs(s[i]));
            }
            float g = peak / max;
            for (int i = 0; i < s.Length; i++) s[i] *= g;
            return s;
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
