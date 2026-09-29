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
    }
}
