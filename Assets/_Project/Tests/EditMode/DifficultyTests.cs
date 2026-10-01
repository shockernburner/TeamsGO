using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using ProjectFossil.Core;
using ProjectFossil.Director;
using ProjectFossil.Dinosaurs;
using ProjectFossil.Economy;
using ProjectFossil.Match;

namespace ProjectFossil.Tests.EditMode
{
    // Adaptive director, score, Survivor Rank, per-hit coins and the wildlife table.
    public class DifficultyTests
    {
        private static DirectorSettings Settings()
        {
            var s = ScriptableObject.CreateInstance<DirectorSettings>();
            s.intensityChangePerSecond = 100f; // jump straight to the target in tests
            return s;
        }

        // ── Adaptive difficulty ────────────────────────────────────────────────

        [Test]
        public void Intensity_RisesWithRecentKills()
        {
            var s = Settings();
            var a = new AdaptiveDifficulty(s);
            float calm = a.Tick(1f, 10f, 0.6f);
            for (int i = 0; i < s.killsForFullBoost; i++) a.ReportKill(10f);
            float busy = a.Tick(1f, 11f, 0.6f);
            Assert.Greater(busy, calm);
            Assert.AreEqual(1f + s.killBoost, busy, 1e-4f);
        }

        [Test]
        public void Intensity_EasesOffWhenBadlyHurt()
        {
            var s = Settings();
            var a = new AdaptiveDifficulty(s);
            Assert.Less(a.Tick(1f, 10f, 0.2f), 1f);
        }

        [Test]
        public void Intensity_OldKillsExpire()
        {
            var s = Settings();
            var a = new AdaptiveDifficulty(s);
            for (int i = 0; i < 6; i++) a.ReportKill(0f);
            a.Tick(1f, s.killWindow + 1f, 0.6f);
            Assert.AreEqual(0, a.RecentKills);
        }

        [Test]
        public void Intensity_StaysWithinLimits()
        {
            var s = Settings();
            var a = new AdaptiveDifficulty(s, baseline: 5f);
            for (int i = 0; i < 50; i++) a.ReportKill(1f);
            Assert.LessOrEqual(a.Tick(1f, 200f, 1f), s.maxIntensity);
            var b = new AdaptiveDifficulty(s, baseline: 0.1f);
            Assert.GreaterOrEqual(b.Tick(1f, 10f, 0.1f), s.minIntensity);
        }

        [Test]
        public void Intensity_MovesGradually()
        {
            var s = ScriptableObject.CreateInstance<DirectorSettings>(); // default, slow change
            var a = new AdaptiveDifficulty(s);
            for (int i = 0; i < 6; i++) a.ReportKill(1f);
            float after = a.Tick(1f, 2f, 0.6f);
            Assert.AreEqual(1f + s.intensityChangePerSecond, after, 1e-4f);
        }

        [Test]
        public void ScaledSpawnCount_NeverBelowBaseAndGrowsAtMax()
        {
            var s = Settings();
            Assert.AreEqual(3, s.ScaledSpawnCount(3, 0.6f));
            Assert.AreEqual(3, s.ScaledSpawnCount(3, 1f));
            Assert.Greater(s.ScaledSpawnCount(3, s.maxIntensity), 3);
            Assert.AreEqual(1, s.ScaledSpawnCount(0, 2f));
        }

        [Test]
        public void Brain_HigherIntensity_EarnsFaster()
        {
            int Earned(float intensity)
            {
                var s = Settings();
                s.startIncomePerSecond = 1f;
                s.endIncomePerSecond = 1f;
                var director = new ThreatDirector(new ThreatDefinition[0]);
                var buyer = new ThreatBuyer("ai", "director", true, new CurrencySystem());
                var brain = new AIDirectorBrain(director, s, buyer, new RNGService(1), 600f) { Intensity = intensity };
                for (int i = 0; i < 100; i++) brain.Tick(0.1f, i * 0.1f, null);
                return buyer.Wallet.Balance;
            }
            Assert.Greater(Earned(2f), Earned(1f));
        }

        // ── Score ──────────────────────────────────────────────────────────────

        [Test]
        public void Score_TenMinutesWithManyKills_BeatsFiveQuietMinutes()
        {
            var quiet = new ScoreModel();
            var busy  = new ScoreModel();
            for (int i = 0; i < 100; i++) { busy.AddKill(100); busy.AddDamage(60f); }
            Assert.Greater(busy.Total(600f, MatchResult.Died) , quiet.Total(300f, MatchResult.Died) * 10);
        }

        [Test]
        public void Score_ExtractionMultiplies_DeathReduces()
        {
            var s = new ScoreModel();
            s.AddKill(100);
            int stranded = s.Total(300f, MatchResult.Stranded);
            Assert.Greater(s.Total(300f, MatchResult.Extracted), stranded);
            Assert.Less(s.Total(300f, MatchResult.Died), stranded);
        }

        [Test]
        public void Score_LeavingWithTeammates_AddsAQuarterEach()
        {
            var s = new ScoreModel();
            s.AddKill(400);
            int alone = s.Total(300f, MatchResult.Extracted);
            Assert.AreEqual((int)System.Math.Round(alone * 1.5f), s.Total(300f, MatchResult.Extracted, 2), 1);
            Assert.AreEqual(s.Total(300f, MatchResult.Died), s.Total(300f, MatchResult.Died, 3), "no bonus unless you leave");
        }

        [Test]
        public void Score_Breakdown_AddsUp()
        {
            var s = new ScoreModel();
            s.AddKill(80); s.AddDamage(45.7f); s.AddThreatFaced();
            Assert.AreEqual(s.SurvivalPoints(100f) + 80 + 45 + ScoreModel.PointsPerThreat, s.Subtotal(100f));
        }

        // ── Survivor Rank ──────────────────────────────────────────────────────

        [Test]
        public void Rank_GoodMatchesClimb_BadMatchesSlip()
        {
            var r = new SurvivorRank();
            for (int i = 0; i < 5; i++) r.Apply(SurvivorRank.ExpectedScore(r.Level) * 3, MatchResult.Extracted);
            Assert.Greater(r.Level, 0);
            int top = r.Level;
            for (int i = 0; i < 10; i++) r.Apply(0, MatchResult.Died);
            Assert.Less(r.Level, top);
        }

        [Test]
        public void Rank_ChangePerMatchIsCapped()
        {
            var r = new SurvivorRank(50f);
            Assert.LessOrEqual(r.Apply(1000000, MatchResult.Extracted), SurvivorRank.MaxGainPerMatch + 1e-4f);
            Assert.GreaterOrEqual(r.Apply(0, MatchResult.Died), -SurvivorRank.MaxLossPerMatch - 1e-4f);
        }

        [Test]
        public void Rank_ExpectedScoreRisesWithLevel_AndKnobsHarden()
        {
            Assert.Greater(SurvivorRank.ExpectedScore(5), SurvivorRank.ExpectedScore(0));
            Assert.AreEqual(1f, SurvivorRank.DirectorBaseline(0), 1e-4f);
            Assert.Greater(SurvivorRank.DinosaurHealth(10), SurvivorRank.DinosaurHealth(0));
            Assert.AreEqual(SurvivorRank.MaxLevel, new SurvivorRank(1000f).Level);
        }

        // ── Coins per hit ──────────────────────────────────────────────────────

        [Test]
        public void CoinTrickle_KeepsFractionsBetweenHits()
        {
            var c = new CoinTrickle(0.15f);
            int total = 0;
            for (int i = 0; i < 10; i++) total += c.AddDamage(12f); // 18 coins' worth
            Assert.AreEqual(18, total);
            Assert.AreEqual(0, new CoinTrickle(0f).AddDamage(50f));
        }

        // ── Wildlife ───────────────────────────────────────────────────────────

        private static WildlifeTable Table(params (float weight, float from)[] entries)
        {
            var t = ScriptableObject.CreateInstance<WildlifeTable>();
            t.entries = new List<WildlifeTable.Entry>();
            foreach (var (weight, from) in entries)
                t.entries.Add(new WildlifeTable.Entry
                {
                    species = ScriptableObject.CreateInstance<DinosaurSpecies>(), weight = weight, fromTime = from,
                    groupMin = 2, groupMax = 4,
                });
            return t;
        }

        [Test]
        public void Wildlife_Pick_RespectsWeightsAndTime()
        {
            var t = Table((1f, 0f), (0f, 0f), (5f, 300f));
            var rng = new RNGService(3);
            for (int i = 0; i < 50; i++) Assert.AreSame(t.entries[0], t.Pick(rng, 10f)); // later entry not unlocked yet

            int late = 0;
            for (int i = 0; i < 200; i++) if (t.Pick(rng, 400f) == t.entries[2]) late++;
            Assert.Greater(late, 120);
        }

        [Test]
        public void Wildlife_GroupSizeInRange_AndTargetGrowsToCap()
        {
            var t = Table((1f, 0f));
            var rng = new RNGService(9);
            for (int i = 0; i < 50; i++)
            {
                int n = WildlifeTable.GroupSize(t.entries[0], rng);
                Assert.That(n, Is.InRange(2, 4));
            }
            t.maxAlive = 30;
            Assert.Greater(t.TargetAlive(20, 300f, 1f), 20);
            Assert.AreEqual(30, t.TargetAlive(20, 100000f, 1f));
        }
    }
}
