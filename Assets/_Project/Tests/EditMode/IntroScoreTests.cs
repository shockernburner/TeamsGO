using System;
using NUnit.Framework;
using ProjectFossil.Audio;
using Cue = ProjectFossil.Audio.SoundSynth.Cue;

namespace ProjectFossil.Tests.EditMode
{
    public class IntroScoreTests
    {
        private static readonly float[] Cards = { 6f, 6f, 5.5f, 7f, 7f, 7.5f, 4.5f, 8f, 4.5f, 3.5f };
        private static readonly Cue[] Cues =
            { Cue.Stars, Cue.Pad, Cue.Pad, Cue.Pad, Cue.Pad, Cue.Pad, Cue.Drop, Cue.Build, Cue.Hold, Cue.Silence };
        private const float Studio = 3f, Ring = 9f;

        private static float[] _score;
        private static float[] Score => _score ??= SoundSynth.IntroScore(Studio, Cards, Cues, Ring);

        private static float Start(int card)
        {
            float t = Studio;
            for (int i = 0; i < card; i++) t += Cards[i];
            return t;
        }

        private static float Rms(float from, float to)
        {
            int a = (int)(from * SoundSynth.SampleRate), b = Math.Min(Score.Length, (int)(to * SoundSynth.SampleRate));
            double sum = 0;
            for (int i = a; i < b; i++) sum += Score[i] * Score[i];
            return (float)Math.Sqrt(sum / Math.Max(1, b - a));
        }

        [Test]
        public void LastsUntilTheHitHasRungOut()
        {
            float hit = Start(Cards.Length);
            Assert.AreEqual(hit + Ring, Score.Length / (float)SoundSynth.SampleRate, 0.01f);
        }

        // The logo is the high point, but a swell rather than a jolt: louder than the story, not twice as loud.
        [Test]
        public void TheLogoIsTheHighPoint_WithoutAJolt()
        {
            float hit = Start(Cards.Length);
            float atHit = Rms(hit, hit + 1f), story = Rms(Start(1) + 2f, Start(1) + 4f);
            Assert.Greater(atHit, story);
            Assert.Less(atHit, story * 3f);
            Assert.Greater(atHit, Rms(Start(7) + 5f, Start(8)));
            Assert.Greater(Rms(hit - 0.5f, hit), Rms(Start(9) + 0.3f, Start(9) + 0.5f), "the music swells back before the logo");
        }

        [Test]
        public void SilenceBeforeTheLastLine_AndTheMusicDropsAtTheOutposts()
        {
            float pad = Rms(Start(3) + 2f, Start(4));
            Assert.Less(Rms(Start(9) + 0.3f, Start(9) + 0.5f), pad * 0.4f, "silence on 'They can hear you'");
            Assert.Less(Rms(Start(6) + 2f, Start(7)), pad, "the music drops away when the outposts go silent");
        }

        [Test]
        public void TheBuildGrows()
        {
            Assert.Greater(Rms(Start(7) + 6f, Start(8)), Rms(Start(7), Start(7) + 2f));
        }

        [Test]
        public void SameNotesEveryTime()
        {
            var again = SoundSynth.IntroScore(Studio, Cards, Cues, Ring);
            for (int i = 0; i < again.Length; i += 997) Assert.AreEqual(Score[i], again[i]);
        }

        [Test]
        public void MenuLoop_HasNoSeam()
        {
            var loop = SoundSynth.MenuLoop(16f);
            Assert.AreEqual(16f * SoundSynth.SampleRate, loop.Length, 1f);
            Assert.Less(Math.Abs(loop[loop.Length - 1] - loop[0]), 0.05f);
        }
    }
}
