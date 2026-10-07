using System;

namespace ProjectFossil.Audio
{
    // The intro's music and the menu's loop, composed here from the same oscillators as the sound effects, so the
    // game owns every note (no licence to track). Only a background: a low drone and soft strings in D minor that
    // change chord with each story card, under the narrator, with nothing else (Firdous: no drums, swells, bells or
    // hits). It eases down under the last line, then carries straight on under the logo into the menu's loop.
    public static partial class SoundSynth
    {
        // The music column of Story/Intro.txt. Only Silence (the last line) changes anything now: the background
        // steps back so "They can hear you" stands alone. The other names are kept so the file stays readable.
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
        private const float SilenceLevel = 0.35f;

        // studio: seconds of studio logo before the first card. cards/cues: each card's length and music.
        // tail: how long it carries on after the last card (under the logo, into the menu loop). The logo appears at
        // studio + sum(cards).
        public static float[] IntroScore(float studio, float[] cards, Cue[] cues, float tail)
        {
            int n = cards.Length;
            var start = new float[n + 1];
            start[0] = studio;
            for (int c = 0; c < n; c++) start[c + 1] = start[c] + cards[c];
            float end = start[n];

            var s = Buffer(end + tail);
            var padLp = new OnePole(2400f);
            int card = -1;
            float level = 0f;

            for (int i = 0; i < s.Length; i++)
            {
                float t = T(i);
                while (card < n && t >= start[card + 1]) card++;
                bool story = card >= 0 && card < n;

                // Fades in under the studio logo, steady through the story, steps back under the last line, and comes
                // back gently under the TETHER logo.
                float target = card < 0 ? Ramp(t, studio * 0.3f, studio) : story && cues[card] == Cue.Silence ? SilenceLevel : 1f;
                float tau = card >= n ? 2.5f : 1.2f; // seconds
                level += (target - level) / (tau * SampleRate);

                float v = Drone(t) * level;
                float pad = 0f;
                int chordCard = Math.Min(card, n - 1);
                for (int c = Math.Max(0, chordCard - 1); c <= Math.Min(n - 1, chordCard + 1); c++)
                {
                    float w = Ramp(t, start[c] - 0.6f, start[c] + 1.6f) * (c == n - 1 ? 1f : 1f - Ramp(t, start[c + 1] - 0.4f, start[c + 1] + 1.4f));
                    if (w > 0f) pad += Strings(Chords[Progression[c % Progression.Length]], t, 0.4f) * w;
                }
                v += padLp.Next(pad) * 0.22f * level;
                s[i] = v;
            }
            return Finish(s, 0.85f);
        }

        // The menu: the same background, drone and a slow Dm / Bb sway, made to loop without a seam. It starts on Dm,
        // the chord the intro ends on.
        public static float[] MenuLoop(float seconds = 32f)
        {
            float fade = 2f;
            var s = Buffer(seconds + fade);
            var padLp = new OnePole(2000f);
            for (int i = 0; i < s.Length; i++)
            {
                float t = T(i);
                float w = 0.5f + 0.5f * (float)Math.Cos(Tau * t / seconds);
                float pad = Strings(Chords[0], t, 0.4f) * w + Strings(Chords[1], t, 0.4f) * (1f - w);
                s[i] = padLp.Next(pad) * 0.2f + Drone(t) * 0.8f;
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
            for (int i = 0; i < len; i++) loop[i] *= 0.6f / max;
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

        // The drone on D, with octaves above the low notes so small speakers carry it.
        private static float Drone(float t)
        {
            float swell = 1f + 0.15f * (float)Math.Sin(Tau * 0.11f * t);
            return (float)(0.5 * Math.Sin(Tau * 36.71f * t) + Math.Sin(Tau * 73.42f * t)
                         + 0.6 * Math.Sin(Tau * 146.83f * t) + 0.35 * Math.Sin(Tau * 220f * t)) * 0.07f * swell;
        }

        // Strings: the chord where it's written and an octave up.
        private static float Strings(float[] notes, float t, float brightness)
        {
            var up = new float[notes.Length];
            for (int i = 0; i < notes.Length; i++) up[i] = notes[i] * 2f;
            return Chord(notes, t, brightness) * 0.6f + Chord(up, t, brightness) * 0.7f;
        }

        private static float Ramp(float t, float a, float b) => t <= a ? 0f : t >= b ? 1f : (t - a) / (b - a);
    }
}
