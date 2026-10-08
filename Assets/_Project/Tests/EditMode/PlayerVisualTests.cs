using NUnit.Framework;
using ProjectFossil.Player;
using UnityEngine;

namespace ProjectFossil.Tests.EditMode
{
    // The survivor's body: afloat with the head out when swimming, and kept on the capsule when its facing is fixed.
    public class PlayerVisualTests
    {
        [Test]
        public void Swimmer_IsRaisedUntilTheChestIsJustUnderTheSurface()
        {
            // The swim clips hang the chest about a metre under the feet the controller floats 1.3 m down.
            float surface = 10f, chest = surface - 1.3f - 1.0f;
            float lift = PlayerVisual.SwimLiftFor(surface, chest);
            Assert.AreEqual(surface - PlayerVisual.ChestUnderWater, chest + lift, 1e-4f);
        }

        [Test]
        public void Swimmer_IsNeverPushedDown_AndTheLiftIsBounded()
        {
            Assert.AreEqual(0f, PlayerVisual.SwimLiftFor(10f, 11f));
            Assert.LessOrEqual(PlayerVisual.SwimLiftFor(10f, -50f), 2.5f);
        }

        [TestCase(180f, true, 180f)]
        [TestCase(-169f, true, -180f)]
        [TestCase(-83f, true, -90f)]
        [TestCase(95f, true, 90f)]
        [TestCase(49f, false, 0f)]   // a stroke or a twist read the shoulders 49 off: not a quarter turn
        [TestCase(135f, false, 0f)]
        [TestCase(20f, false, 0f)]
        public void FacingTurns_OnlyByQuarterOrHalfTurns(float off, bool turns, float expected)
        {
            Assert.AreEqual(turns, PlayerVisual.FacingTurn(off, out float turn));
            if (turns) Assert.AreEqual(Mathf.Abs(expected), Mathf.Abs(turn), 1e-4f);
        }

        [Test]
        public void FacingTurn_KeepsTheBodyOnTheCapsule()
        {
            // Turning about the hips moved the model off the capsule; about the axis, it stays the same distance.
            var fit = new Vector3(0.02f, 0f, -0.03f);
            foreach (float turn in new[] { 90f, -90f, 180f, -180f })
            {
                var after = PlayerVisual.TurnedOffset(fit, turn);
                Assert.AreEqual(fit.magnitude, after.magnitude, 1e-5f);
                Assert.AreEqual(fit.y, after.y, 1e-5f);
            }
        }
    }
}
