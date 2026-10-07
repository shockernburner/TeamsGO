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

        // Only a background: under the logo it carries on at about the story's level, no hit and no jump.
        [Test]
        public void NoHitAtTheLogo_TheBackgroundCarriesOn()
        {
            float end = Start(Cards.Length), story = Rms(Start(3) + 2f, Start(4));
            float under = Rms(end, end + 1f);
            Assert.Less(under, story * 1.2f, "no hit when the logo appears");
            Assert.Greater(Rms(end + 3f, end + 5f), story * 0.6f, "the background comes back under the logo");
        }

        [Test]
        public void TheBackgroundStepsBackForTheLastLine()
        {
            float pad = Rms(Start(3) + 2f, Start(4));
            Assert.Less(Rms(Start(9) + 2.5f, Start(10)), pad * 0.6f);
        }

        [Test]
        public void SteadyThroughTheStory_NoDropsOrBuilds()
        {
            float a = Rms(Start(3) + 2f, Start(4)), drop = Rms(Start(6) + 2f, Start(7)), build = Rms(Start(7) + 5f, Start(8));
            Assert.Greater(drop, a * 0.6f);
            Assert.Less(build, a * 1.5f);
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
