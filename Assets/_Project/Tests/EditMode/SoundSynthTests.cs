using System;
using System.Collections.Generic;
using NUnit.Framework;
using ProjectFossil.Audio;

namespace ProjectFossil.Tests.EditMode
{
    public class SoundSynthTests
    {
        private static IEnumerable<TestCaseData> AllSounds()
        {
            yield return new TestCaseData(SoundSynth.Footstep(0, false), 0.05f, 0.3f).SetName("Footstep");
            yield return new TestCaseData(SoundSynth.Footstep(1, true), 0.05f, 0.3f).SetName("SoftFootstep");
            yield return new TestCaseData(SoundSynth.Screech(2), 0.5f, 1.5f).SetName("Screech");
            yield return new TestCaseData(SoundSynth.Roar(1), 1.5f, 3f).SetName("Roar");
            yield return new TestCaseData(SoundSynth.Bite(0), 0.1f, 0.5f).SetName("Bite");
            yield return new TestCaseData(SoundSynth.Swing(3), 0.1f, 0.5f).SetName("Swing");
            yield return new TestCaseData(SoundSynth.Hit(0), 0.05f, 0.5f).SetName("Hit");
            yield return new TestCaseData(SoundSynth.Hurt(0), 0.1f, 0.6f).SetName("Hurt");
            yield return new TestCaseData(SoundSynth.Coin(), 0.1f, 0.6f).SetName("Coin");
            yield return new TestCaseData(SoundSynth.ThreatSting(), 1.5f, 4f).SetName("ThreatSting");
            yield return new TestCaseData(SoundSynth.Chime(), 0.8f, 2f).SetName("Chime");
            yield return new TestCaseData(SoundSynth.Ambience(), 8f, 14f).SetName("Ambience");
            yield return new TestCaseData(SoundSynth.Sniff(0), 0.5f, 1.5f).SetName("Sniff");
            yield return new TestCaseData(SoundSynth.Sniff(1), 0.5f, 1.5f).SetName("SniffLong");
            yield return new TestCaseData(SoundSynth.Heartbeat(), 0.3f, 0.8f).SetName("Heartbeat");
            yield return new TestCaseData(SoundSynth.Rotor(), 1.5f, 2.5f).SetName("Rotor");
            yield return new TestCaseData(SoundSynth.Victory(), 2f, 4f).SetName("Victory");
        }

        [TestCaseSource(nameof(AllSounds))]
        public void Sound_IsAudibleFiniteAndInRange(float[] samples, float minSeconds, float maxSeconds)
        {
            float seconds = samples.Length / (float)SoundSynth.SampleRate;
            Assert.That(seconds, Is.InRange(minSeconds, maxSeconds));

            float peak = 0f;
            foreach (var s in samples)
            {
                Assert.That(float.IsNaN(s) || float.IsInfinity(s), Is.False);
                peak = Math.Max(peak, Math.Abs(s));
            }
            Assert.That(peak, Is.GreaterThan(0.2f), "too quiet to hear");
            Assert.That(peak, Is.LessThanOrEqualTo(1f), "would clip");
        }

        [Test]
        public void OneShots_EndInSilence()
        {
            // A non-zero last sample makes an audible click when the clip stops.
            foreach (var clip in new[] { SoundSynth.Bite(0), SoundSynth.Screech(0), SoundSynth.Roar(0), SoundSynth.Coin(),
                                         SoundSynth.Sniff(0), SoundSynth.Heartbeat(), SoundSynth.Victory() })
                Assert.That(Math.Abs(clip[clip.Length - 1]), Is.LessThan(0.01f));
        }

        [Test]
        public void Roar_IsAudibleOnLaptopSpeakers()
        {
            // Small speakers drop most of what sits below ~250 Hz; the roar must not live there.
            for (int v = 0; v < 4; v++)
            {
                var s = SoundSynth.Roar(v);
                float a = 1f - (float)Math.Exp(-2.0 * Math.PI * 250.0 / SoundSynth.SampleRate);
                float low = 0f;
                double total = 0, high = 0;
                foreach (var x in s)
                {
                    low += a * (x - low);
                    total += x * x;
                    high  += (x - low) * (x - low);
                }
                Assert.That(high / total, Is.GreaterThan(0.5), $"variant {v}");
            }
        }

        [Test]
        public void SameVariant_SameSamples_DifferentVariant_Differs()
        {
            var a = SoundSynth.Screech(1);
            var b = SoundSynth.Screech(1);
            var c = SoundSynth.Screech(2);
            Assert.That(b, Is.EqualTo(a));
            Assert.That(c, Is.Not.EqualTo(a));
        }

        [Test]
        public void Ambience_LoopSeamIsSmooth()
        {
            var s = SoundSynth.Ambience();
            float seam = Math.Abs(s[s.Length - 1] - s[0]);
            Assert.That(seam, Is.LessThan(0.1f), "loop would click where it wraps");
        }
    }
}
