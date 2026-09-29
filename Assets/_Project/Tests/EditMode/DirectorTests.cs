using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using ProjectFossil.Core;
using ProjectFossil.Director;
using ProjectFossil.Economy;

namespace ProjectFossil.Tests.EditMode
{
    public class DirectorTests
    {
        private static ThreatDefinition MakeThreat(string id, int cost, float cooldown = 0f, float unlock = 0f, float weight = 1f)
        {
            var t = ScriptableObject.CreateInstance<ThreatDefinition>();
            t.threatId   = id;
            t.cost       = cost;
            t.cooldown   = cooldown;
            t.unlockTime = unlock;
            t.aiWeight   = weight;
            return t;
        }

        private static ThreatBuyer Buyer(int coins, string team = "director") =>
            new ThreatBuyer("buyer-" + team, team, true, new CurrencySystem(coins));

        private static readonly ThreatTarget Players = new ThreatTarget("players", Vector3.zero);

        [Test]
        public void Purchase_Success_SpendsAndFiresEvent()
        {
            var director = new ThreatDirector(new[] { MakeThreat("pack", 40) });
            var buyer = Buyer(100);
            ThreatEvent? fired = null;
            director.OnThreatTriggered += e => fired = e;

            Assert.IsTrue(director.CanAfford("pack", buyer));
            Assert.AreEqual(PurchaseResult.Success, director.Purchase("pack", Players, buyer));
            Assert.AreEqual(60, buyer.Wallet.Balance);
            Assert.IsTrue(fired.HasValue);
            Assert.AreEqual("pack", fired.Value.Threat.threatId);
            Assert.AreEqual(buyer, fired.Value.Buyer);
        }

        [Test]
        public void Purchase_Rejections_DoNotSpend()
        {
            var director = new ThreatDirector(new[]
            {
                MakeThreat("cheap", 10, cooldown: 30f),
                MakeThreat("late", 10, unlock: 100f),
                MakeThreat("pricey", 500),
            });
            var buyer = Buyer(100);

            Assert.AreEqual(PurchaseResult.UnknownThreat, director.Purchase("nope", Players, buyer));
            Assert.AreEqual(PurchaseResult.Locked, director.Purchase("late", Players, buyer));
            Assert.AreEqual(PurchaseResult.CannotAfford, director.Purchase("pricey", Players, buyer));
            Assert.AreEqual(PurchaseResult.InvalidTarget,
                            director.Purchase("cheap", new ThreatTarget("director", Vector3.zero), buyer));
            Assert.AreEqual(100, buyer.Wallet.Balance);

            Assert.AreEqual(PurchaseResult.Success, director.Purchase("cheap", Players, buyer));
            Assert.AreEqual(PurchaseResult.OnCooldown, director.Purchase("cheap", Players, buyer));
            Assert.AreEqual(90, buyer.Wallet.Balance);

            director.SetTime(30f);
            Assert.AreEqual(PurchaseResult.Success, director.Purchase("cheap", Players, buyer));
        }

        [Test]
        public void Cooldowns_ArePerBuyer()
        {
            var director = new ThreatDirector(new[] { MakeThreat("pack", 10, cooldown: 60f) });
            var ai    = Buyer(50, "director");
            var rival = Buyer(50, "team-b");

            Assert.AreEqual(PurchaseResult.Success, director.Purchase("pack", Players, ai));
            Assert.AreEqual(PurchaseResult.Success, director.Purchase("pack", Players, rival));
        }

        [Test]
        public void DuplicateThreatIds_Throw()
        {
            Assert.Throws<System.ArgumentException>(() =>
                new ThreatDirector(new[] { MakeThreat("a", 1), MakeThreat("a", 2) }));
        }

        // ── AI brain ─────────────────────────────────────────────────────────

        private static DirectorSettings MakeSettings()
        {
            var s = ScriptableObject.CreateInstance<DirectorSettings>();
            s.startingBudget       = 0;
            s.startIncomePerSecond = 1f;
            s.endIncomePerSecond   = 4f;
            s.escalationExponent   = 1f;
            s.gracePeriod          = 30f;
            s.minSecondsBetween    = 20f;
            s.decisionInterval     = 5f;
            s.saveUpChance         = 0f;
            return s;
        }

        private static List<float> Simulate(int seed, DirectorSettings settings, IEnumerable<ThreatDefinition> threats, float duration)
        {
            var director = new ThreatDirector(threats);
            var brain = new AIDirectorBrain(director, settings, Buyer(0), new RNGService(seed), duration);
            var times = new List<float>();
            const float dt = 0.5f;
            for (float t = 0f; t < duration; t += dt)
                if (brain.Tick(dt, t, Players) != null) times.Add(t);
            return times;
        }

        [Test]
        public void Brain_RespectsGracePeriodAndSpacing()
        {
            var settings = MakeSettings();
            var times = Simulate(1, settings, new[] { MakeThreat("pack", 10) }, 600f);

            Assert.IsNotEmpty(times);
            Assert.GreaterOrEqual(times[0], settings.gracePeriod);
            for (int i = 1; i < times.Count; i++)
                Assert.GreaterOrEqual(times[i] - times[i - 1], settings.minSecondsBetween - 0.001f);
        }

        [Test]
        public void Brain_IsDeterministicForSeed()
        {
            var settings = MakeSettings();
            var threats = new[] { MakeThreat("small", 10, weight: 3f), MakeThreat("big", 60, weight: 1f) };
            CollectionAssert.AreEqual(Simulate(7, settings, threats, 900f), Simulate(7, settings, threats, 900f));
        }

        [Test]
        public void Brain_NoTarget_NoPurchases()
        {
            var settings = MakeSettings();
            var director = new ThreatDirector(new[] { MakeThreat("pack", 1) });
            var brain = new AIDirectorBrain(director, settings, Buyer(0), new RNGService(0), 300f);
            for (float t = 0f; t < 300f; t += 1f)
                Assert.IsNull(brain.Tick(1f, t, null));
            Assert.Greater(brain.Buyer.Wallet.Balance, 0, "budget still accrues while waiting");
        }

        [Test]
        public void Settings_IncomeEscalates()
        {
            var s = MakeSettings();
            Assert.AreEqual(1f, s.IncomeAt(0f), 1e-4);
            Assert.AreEqual(4f, s.IncomeAt(1f), 1e-4);
            Assert.Greater(s.IncomeAt(0.75f), s.IncomeAt(0.25f));
        }
    }
}
