using NUnit.Framework;
using ProjectFossil.Player;

namespace ProjectFossil.Tests.EditMode
{
    public class StaminaTests
    {
        private static StaminaModel Make() => new StaminaModel(100f)
        {
            DrainPerSecond      = 20f,
            RegenPerSecond      = 10f,
            IdleRegenMultiplier = 2f,
            RegenDelay          = 1f,
            RecoverFraction     = 0.3f,
        };

        [Test]
        public void Running_Drains_WalkingDoesNot()
        {
            var s = Make();
            s.Tick(1f, running: true, moving: true);
            Assert.AreEqual(80f, s.Current, 1e-4);
            s.Tick(0.5f, running: false, moving: true); // still inside the regen delay
            Assert.AreEqual(80f, s.Current, 1e-4);
        }

        [Test]
        public void HittingZero_Exhausts_AndBlocksRunningUntilRecovered()
        {
            var s = Make();
            for (int i = 0; i < 5; i++) s.Tick(1f, running: true, moving: true);
            Assert.AreEqual(0f, s.Current);
            Assert.IsTrue(s.IsExhausted);
            Assert.IsFalse(s.CanRun);

            // Holding "run" while exhausted must not drain or flicker back into running.
            s.Tick(1f, running: true, moving: true);
            s.Tick(1f, running: true, moving: true);
            Assert.IsTrue(s.IsExhausted);
            Assert.Greater(s.Current, 0f, "regen continues while exhausted");

            for (int i = 0; i < 3; i++) s.Tick(1f, running: false, moving: true);
            Assert.IsFalse(s.IsExhausted, "recovers once back above 30%");
            Assert.IsTrue(s.CanRun);
        }

        [Test]
        public void StandingStill_RecoversFaster()
        {
            var walking = Make();
            var still = Make();
            walking.TrySpend(50f);
            still.TrySpend(50f);
            walking.Tick(3f, running: false, moving: true);
            still.Tick(3f, running: false, moving: false);
            Assert.Greater(still.Current, walking.Current);
        }

        [Test]
        public void TrySpend_FailsWhenShortOrExhausted()
        {
            var s = Make();
            Assert.IsTrue(s.TrySpend(95f));
            Assert.IsFalse(s.TrySpend(10f));
            Assert.AreEqual(5f, s.Current, 1e-4);
            Assert.IsTrue(s.TrySpend(5f));
            Assert.IsTrue(s.IsExhausted);
            Assert.IsFalse(s.TrySpend(0.5f));
        }
    }
}
