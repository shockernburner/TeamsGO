using System;

namespace ProjectFossil.Audio
{
    // The intro's music and the menu's loop, composed here from the same oscillators as the sound effects, so the
    // game owns every note (no licence to track). D minor, following the cue sheet in Docs/STORY.md: a low drone and
    // soft strings that change chord with each card, the music dropping away when the outposts go silent, a pulse
    // with drums building under the crews, silence and a breath before "They can hear you", then one big hit as
    // the logo slams in, ringing out into the menu.
    public static partial class SoundSynth
    {
        // What the music does under one story card.
        public enum Cue { Pad, Stars, Drop, Build, Hold, Silence }

        private static readonly float[][] Chords =
        {
            new[] { 146.83f, 174.61f, 220.00f }, // Dm
            new[] { 116.54f, 146.83f, 174.61f }, // Bb
            new[] { 174.61f, 220.00f, 261.63f }, // F
            new[] { 130.81f, 164.81f, 196.00f }, // C
            new[] {  98.00f, 116.54f, 146.83f }, // Gm
            new[] { 110.00f, 138.59f, 164.81f }, // A
        };
        private static readonly int[] Progression = { 0, 1, 2, 3, 4, 1, 4, 5, 0, 0 };

        // How loud the drone and strings are under each cue.
        private static float PadLevel(Cue c) => c switch
        {
            Cue.Pad => 1f, Cue.Stars => 0.5f, Cue.Drop => 0.12f, Cue.Build => 1.15f, Cue.Hold => 0.8f, _ => 0f,
        };

        // studio: seconds of studio logo before the first card. cards/cues: each card's length and music.
        // ring: how long the logo hit rings on afterwards. Returns the samples; the hit lands at studio + sum(cards).
        public static float[] IntroScore(float studio, float[] cards, Cue[] cues, float ring)
        {
            int n = cards.Length;
            var start = new float[n + 1];
            start[0] = studio;
            for (int c = 0; c < n; c++) start[c + 1] = start[c] + cards[c];
            float hitAt = start[n];

            var s = Buffer(hitAt + ring);
            var rng = new Random(6600);
            var wind = new OnePole(260f);
            var padLp = new OnePole(900f);
            var crash = new OnePole(1800f);
            var breathLp = new OnePole(1200f);
            var snareHp = new HighPass(900f);
            int card = -1;
            float level = 0f; // the drone and strings, eased between cues

            for (int i = 0; i < s.Length; i++)
            {
                float t = T(i);
                while (card < n && t >= start[card + 1]) card++;
                bool story = card >= 0 && card < n;
                Cue cue = story ? cues[card] : Cue.Pad;
                float sinceHit = t - hitAt;

                // Ease the bed towards this cue's level: slowly in, quickly out to silence.
                float target = card < 0 ? Lerp(0f, 0.6f, (t - studio * 0.5f) / 2f) : story ? PadLevel(cue) : 0f;
                float tau = target >= level ? 1.5f : cue == Cue.Silence ? 0.15f : 0.8f; // seconds
                level += (target - level) / (tau * SampleRate);

                // Wind under everything except the silence.
                float windLevel = cue == Cue.Silence && story ? 0.15f : 1f;
                float v = wind.Next(Noise(rng)) * 0.3f * windLevel;

                // Drone on D.
                v += (float)(Math.Sin(Tau * 73.42f * t) + 0.5 * Math.Sin(Tau * 36.71f * t) + 0.2 * Math.Sin(Tau * 110.0f * t))
                     * 0.11f * level * (1f + 0.15f * (float)Math.Sin(Tau * 0.11f * t));

                // Strings: each card's chord, crossfading into the next, brighter as the story builds.
                float pad = 0f;
                for (int c = Math.Max(0, card - 1); c <= Math.Min(n - 1, card + 1); c++)
                {
                    float w = Ramp(t, start[c] - 0.6f, start[c] + 1.6f) * (1f - Ramp(t, start[c + 1] - 0.4f, start[c + 1] + 1.4f));
                    if (w > 0f) pad += Chord(Chords[Progression[c % Progression.Length]], t, 0.2f + 0.6f * (t / hitAt)) * w;
                }
                v += padLp.Next(pad) * 0.09f * level;

                // Stars: a high, glassy shimmer.
                if (story && cue == Cue.Stars)
                {
                    float w = Ramp(t, start[card], start[card] + 1.5f) * (1f - Ramp(t, start[card + 1] - 1f, start[card + 1] + 0.5f));
                    float trem = 0.6f + 0.4f * (float)Math.Sin(Tau * 0.7f * t);
                    v += (float)(Math.Sin(Tau * 587.33f * t) + 0.6 * Math.Sin(Tau * 880f * t) + 0.3 * Math.Sin(Tau * 1174.66f * t)) * 0.02f * w * trem;
                }

                // Build: a pulse that speeds up (90 to 120 a minute) with a low drum on every beat and a snare on
                // the off-beat once it gets going, all growing louder. Hold keeps a softer pulse.
                if (story && (cue == Cue.Build || cue == Cue.Hold))
                {
                    float into = t - start[card], len = cards[card];
                    float k = cue == Cue.Build ? into / len : 0.4f;
                    float bpm = Lerp(90f, 120f, k);
                    float beat = into * bpm / 60f, inBeat = (beat - (float)Math.Floor(beat)) * 60f / bpm;
                    float loud = cue == Cue.Build ? Lerp(0.35f, 1f, k) : 0.35f;
                    v += (float)Math.Sin(Tau * (55f + 40f * (float)Math.Exp(-inBeat / 0.03f)) * inBeat) * Exp(inBeat, 0.003f, 0.12f) * 0.7f * loud;
                    float off = inBeat - 30f / bpm;
                    if (cue == Cue.Build && k > 0.35f && off > 0f)
                        v += snareHp.Next(Noise(rng)) * Exp(off, 0.002f, 0.06f) * 0.35f * loud;
                    // the pulse itself: a low octave throb
                    v += (float)Math.Sin(Tau * 73.42f * t) * Exp(inBeat, 0.02f, 0.25f) * 0.15f * loud;
                }

                // Silence, then a breath before the last line settles.
                if (story && cue == Cue.Silence)
                {
                    float tb = t - start[card] - 0.5f;
                    if (tb > 0f && tb < 1.4f) v += breathLp.Next(Noise(rng)) * Adsr(tb, 0.5f, 0.3f, 0.6f, 1.4f, 0.6f) * 0.45f;
                }

                // The hit: a low boom, a crash of noise, and D minor ringing out into the menu.
                if (sinceHit >= 0f)
                {
                    float f = 30f + 30f * (float)Math.Exp(-sinceHit / 0.25f);
                    v += (float)Math.Sin(Tau * f * sinceHit) * Exp(sinceHit, 0.004f, 1.1f) * 0.9f;
                    v += crash.Next(Noise(rng)) * Exp(sinceHit, 0.002f, 0.6f) * 0.5f;
                    v += Chord(new[] { 73.42f, 146.83f, 174.61f, 220f, 293.66f }, t, 0.9f) * Exp(sinceHit, 0.05f, 4.5f) * 0.1f;
                    v += wind.Next(Noise(rng)) * 0.2f;
                }
                s[i] = v;
            }
            return Finish(s, 0.6f);
        }

        // The menu: drone and a slow Dm / Bb sway, made to loop without a seam.
        public static float[] MenuLoop(float seconds = 32f)
        {
            float fade = 2f;
            var s = Buffer(seconds + fade);
            var rng = new Random(6700);
            var wind = new OnePole(220f);
            var padLp = new OnePole(700f);
            for (int i = 0; i < s.Length; i++)
            {
                float t = T(i);
                float w = 0.5f + 0.5f * (float)Math.Cos(Tau * t / seconds);
                float pad = Chord(Chords[0], t, 0.3f) * w + Chord(Chords[1], t, 0.3f) * (1f - w);
                float drone = (float)(Math.Sin(Tau * 73.42f * t) + 0.5 * Math.Sin(Tau * 36.71f * t));
                s[i] = padLp.Next(pad) * 0.08f + drone * 0.1f + wind.Next(Noise(rng)) * 0.25f;
            }
            // Fold the extra seconds over the start, so the end runs straight into the beginning.
            int len = (int)(seconds * SampleRate), over = s.Length - len;
            var loop = new float[len];
            for (int i = 0; i < len; i++)
            {
                loop[i] = s[i];
                if (i < over) { float k = i / (float)over; loop[i] = s[i] * k + s[len + i] * (1f - k); }
            }
            // Normalised like Finish, but without its fade-out: that would click at the loop point.
            float max = 1e-6f;
            for (int i = 0; i < len; i++) { loop[i] = (float)Math.Tanh(loop[i]); max = Math.Max(max, Math.Abs(loop[i])); }
            for (int i = 0; i < len; i++) loop[i] *= 0.35f / max;
            return loop;
        }

        // Three soft voices per note, slightly detuned; brightness 0..1 adds upper harmonics.
        private static float Chord(float[] notes, float t, float brightness)
        {
            float v = 0f;
            foreach (var f in notes)
                for (int d = -1; d <= 1; d++)
                {
                    double ph = Tau * f * (1f + d * 0.0035f) * t;
                    v += (float)(Math.Sin(ph) + brightness * (0.4 * Math.Sin(2 * ph) + 0.2 * Math.Sin(3 * ph)));
                }
            return v / (notes.Length * 3f);
        }

        private static float Ramp(float t, float a, float b) => t <= a ? 0f : t >= b ? 1f : (t - a) / (b - a);
    }
}
