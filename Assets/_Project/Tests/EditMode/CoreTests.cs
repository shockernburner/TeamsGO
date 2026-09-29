using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using ProjectFossil.Core;

namespace ProjectFossil.Tests.EditMode
{
    public class CoreTests
    {
        [Test]
        public void HealthPool_Damage_ClampsAtZero_AndFiresDiedOnce()
        {
            var pool = new HealthPool(50f);
            int died = 0;
            pool.Died += () => died++;

            Assert.AreEqual(30f, pool.ApplyDamage(30f));
            Assert.AreEqual(20f, pool.ApplyDamage(100f), "only remaining health is applied");
            Assert.AreEqual(0f, pool.ApplyDamage(10f), "no damage after death");
            Assert.IsTrue(pool.IsDead);
            Assert.AreEqual(1, died);
        }

        [Test]
        public void HealthPool_Heal_CapsAtMax_AndIgnoresDead()
        {
            var pool = new HealthPool(100f);
            pool.ApplyDamage(40f);
            Assert.AreEqual(40f, pool.Heal(1000f));
            Assert.AreEqual(100f, pool.Current);

            pool.ApplyDamage(100f);
            Assert.AreEqual(0f, pool.Heal(50f));
            Assert.IsTrue(pool.IsDead);
        }

        [Test]
        public void HealthPool_IgnoresNonPositiveAmounts()
        {
            var pool = new HealthPool(10f);
            Assert.AreEqual(0f, pool.ApplyDamage(-5f));
            Assert.AreEqual(0f, pool.Heal(-5f));
            Assert.AreEqual(10f, pool.Current);
        }
    

        // ── Screen marker ──────────────────────────────────────────────────────

        [Test]
        public void ScreenMarker_OnScreenPoint_StaysPut()
        {
            var at = ScreenMarker.Place(new Vector3(300f, 200f, 10f), 800f, 600f, 40f, out bool onScreen);
            Assert.IsTrue(onScreen);
            Assert.AreEqual(300f, at.x, 1e-3f);
            Assert.AreEqual(200f, at.y, 1e-3f);
        }

        [Test]
        public void ScreenMarker_OffToTheRight_PinsToRightEdge()
        {
            var at = ScreenMarker.Place(new Vector3(2000f, 300f, 10f), 800f, 600f, 40f, out bool onScreen);
            Assert.IsFalse(onScreen);
            Assert.AreEqual(760f, at.x, 1e-3f);
            Assert.AreEqual(300f, at.y, 1e-3f);
        }

        [Test]
        public void ScreenMarker_BehindOnTheLeft_PointsLeft()
        {
            // Behind the camera the projection is mirrored: a target behind-left comes out on the right.
            var at = ScreenMarker.Place(new Vector3(600f, 300f, -5f), 800f, 600f, 40f, out bool onScreen);
            Assert.IsFalse(onScreen);
            Assert.AreEqual(40f, at.x, 1e-3f);

            var straightBack = ScreenMarker.Place(new Vector3(400f, 300f, -5f), 800f, 600f, 40f, out _);
            Assert.AreEqual(40f, straightBack.y, 1e-3f, "dead behind points down: turn around");
        }

        // ── View blockers ──────────────────────────────────────────────────────

        [Test]
        public void ViewBlockers_FindsOnlyPlantsNearTheLineOfSight()
        {
            ViewBlockers.Clear();
            var onLine  = new Renderer[1];
            var farAway = new Renderer[1];
            ViewBlockers.Register(new Vector3(10f, 1f, 2f), 0.8f, onLine);
            ViewBlockers.Register(new Vector3(10f, 1f, 9f), 0.8f, farAway);

            var found = new List<Renderer[]>();
            ViewBlockers.Overlapping(new Vector3(6f, 1.5f, 1.5f), new Vector3(14f, 1.5f, 1.5f), 0.3f, found);
            Assert.AreEqual(1, found.Count);
            Assert.AreSame(onLine, found[0]);

            ViewBlockers.Clear();
            ViewBlockers.Overlapping(new Vector3(6f, 1.5f, 1.5f), new Vector3(14f, 1.5f, 1.5f), 0.3f, found);
            Assert.AreEqual(0, found.Count);
        }

        [Test]
        public void ViewBlockers_WaterHeightResetsWithTheIsland()
        {
            ViewBlockers.SetWaterHeight(3.5f);
            Assert.AreEqual(3.5f, ViewBlockers.WaterHeight);
            ViewBlockers.Clear();
            Assert.IsTrue(float.IsNegativeInfinity(ViewBlockers.WaterHeight));
        }
    

        [Test]
        public void ViewBlockers_LoneBushIsThinCover_ForestHides()
        {
            ViewBlockers.Clear();
            // A single bush in a field, and some grass.
            ViewBlockers.Register(new Vector3(0f, 0.5f, 0f), 1f, new Renderer[1], null, 1f, cover: true);
            ViewBlockers.Register(new Vector3(20f, 0.2f, 0f), 1f, new Renderer[1], null, 1f, cover: false);

            float bush = ViewBlockers.Concealment(new Vector3(0.3f, 0f, 0.2f));
            Assert.GreaterOrEqual(bush, ViewBlockers.CoverAt, "a bush is some cover");
            Assert.Less(bush, ViewBlockers.HiddenAt, "but one bush alone doesn't hide you");
            Assert.AreEqual(0f, ViewBlockers.Concealment(new Vector3(20f, 0f, 0f)), "grass isn't cover");

            // A stand of trees with undergrowth, 100 m away.
            var forest = new Vector3(100f, 0f, 100f);
            ViewBlockers.Register(forest + new Vector3(0.4f, 0.5f, 0f), 1f, new Renderer[1], null, 1f, cover: true);
            ViewBlockers.Register(forest + new Vector3(-2f, 0.5f, 1f), 1f, new Renderer[1], null, 1f, cover: true);
            for (int i = 0; i < 6; i++)
            {
                float a = i * Mathf.PI / 3f;
                ViewBlockers.RegisterTree(forest + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 3.5f, null, 0f);
            }
            Assert.GreaterOrEqual(ViewBlockers.Concealment(forest), ViewBlockers.HiddenAt, "thick forest hides you");
            Assert.Less(ViewBlockers.Concealment(forest + new Vector3(15f, 0f, 0f)), ViewBlockers.CoverAt,
                        "step out of the trees and you're in the open");
            ViewBlockers.Clear();
        }

        [Test]
        public void ViewBlockers_InWater_SeaRiversAndLakes()
        {
            ViewBlockers.Clear();
            ViewBlockers.SetWaterHeight(2f);
            ViewBlockers.RegisterWater(new Vector3(50f, 10f, 50f), 4f); // a river 8 m above the sea

            Assert.IsTrue(ViewBlockers.InWater(new Vector3(0f, 1.5f, 0f)), "wading in the sea");
            Assert.IsFalse(ViewBlockers.InWater(new Vector3(0f, 3f, 0f)), "on the beach");
            Assert.IsTrue(ViewBlockers.InWater(new Vector3(51f, 9.6f, 50f)), "standing in the river");
            Assert.IsFalse(ViewBlockers.InWater(new Vector3(51f, 11f, 50f)), "on a bank above it");
            Assert.IsFalse(ViewBlockers.InWater(new Vector3(60f, 9.6f, 50f)), "beside it");
            ViewBlockers.Clear();
        }

        // ── Scent trail ────────────────────────────────────────────────────────

        [Test]
        public void ScentTrail_DropsMarksOnInterval_AndTheyFade()
        {
            var trail = new ScentTrail(lifetime: 10f, dropInterval: 2f);
            for (int t = 0; t <= 6; t++) trail.Tick(new Vector3(t, 0f, 0f), t);
            Assert.AreEqual(4, trail.Count); // t = 0, 2, 4, 6

            trail.Tick(new Vector3(100f, 0f, 0f), 13f);
            Assert.AreEqual(3, trail.Count, "marks older than the lifetime are gone (0 and 2), a new one added");
        }

        [Test]
        public void ScentTrail_FollowingFreshestMarks_LeadsToThePlayer()
        {
            var trail = new ScentTrail(lifetime: 60f, dropInterval: 1f);
            for (int t = 0; t < 20; t++) trail.Tick(new Vector3(t * 3f, 0f, 0f), t); // walking +x, 3 m a second

            // A nose with 10 m range picks the freshest mark it can reach, then the next one from there.
            Vector3 at = new Vector3(-5f, 0f, 0f);
            float newer = float.NegativeInfinity;
            int steps = 0;
            while (trail.TryFindFreshest(at, 10f, newer, out var mark, out float time) && steps < 50)
            {
                Assert.Greater(time, newer);
                at = mark; newer = time; steps++;
            }
            Assert.AreEqual(57f, at.x, 1e-3f, "ends on the newest mark");
            Assert.Less(steps, 20, "skips ahead instead of stepping on every mark");
        }

        [Test]
        public void ScentTrail_WaterBreaksTheTrail()
        {
            var trail = new ScentTrail(lifetime: 60f, dropInterval: 1f);
            for (int t = 0; t < 5; t++)  trail.Tick(new Vector3(t * 3f, 0f, 0f), t);
            for (int t = 5; t < 10; t++) trail.Tick(new Vector3(t * 3f, 0f, 0f), t, leavesScent: false); // wading
            for (int t = 10; t < 15; t++) trail.Tick(new Vector3(t * 3f, 0f, 0f), t);

            Assert.IsTrue(trail.TryFindFreshest(new Vector3(12f, 0f, 0f), 6f, 4f, out _, out _) == false,
                "from the water's edge the next marks are out of reach");
        }
    }
}
