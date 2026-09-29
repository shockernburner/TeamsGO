using NUnit.Framework;
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
    }
}
