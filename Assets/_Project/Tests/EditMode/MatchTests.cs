using NUnit.Framework;
using ProjectFossil.Match;

namespace ProjectFossil.Tests.EditMode
{
    public class MatchTests
    {
        private static MatchState Make() =>
            new MatchState(duration: 100f, extractionOpensAt: 30f, extractionHoldTime: 5f,
                           payoutInterval: 10f, payoutAmount: 3);

        [Test]
        public void Extraction_ClosedEarly_DoesNotProgress()
        {
            var s = Make();
            for (int i = 0; i < 20; i++) s.Tick(1f, true);
            Assert.AreEqual(MatchPhase.Active, s.Phase);
            Assert.AreEqual(0f, s.ExtractionProgress);
        }

        [Test]
        public void Extraction_HoldInsideZone_Extracts()
        {
            var s = Make();
            MatchResult? ended = null;
            s.Ended += r => ended = r;

            for (int i = 0; i < 30; i++) s.Tick(1f, false);
            Assert.IsTrue(s.IsExtractionOpen);
            for (int i = 0; i < 5; i++) s.Tick(1f, true);

            Assert.AreEqual(MatchPhase.Ended, s.Phase);
            Assert.AreEqual(MatchResult.Extracted, s.Result);
            Assert.AreEqual(MatchResult.Extracted, ended);
        }

        [Test]
        public void Extraction_LeavingZone_ResetsProgress()
        {
            var s = Make();
            for (int i = 0; i < 30; i++) s.Tick(1f, false);
            s.Tick(3f, true);
            s.Tick(1f, false);
            Assert.AreEqual(0f, s.ExtractionProgress);
            Assert.AreEqual(MatchPhase.Active, s.Phase);
        }

        [Test]
        public void Timeout_Strands_AndDeathEnds()
        {
            var a = Make();
            for (int i = 0; i < 100; i++) a.Tick(1f, false);
            Assert.AreEqual(MatchResult.Stranded, a.Result);

            var b = Make();
            b.Tick(1f, false);
            b.ReportPlayerDied();
            b.ReportPlayerDied();
            Assert.AreEqual(MatchResult.Died, b.Result);
            b.Tick(50f, true);
            Assert.AreEqual(1f, b.Elapsed, "ended matches stop ticking");
        }

        [Test]
        public void SurvivalPayout_FiresOnInterval_AndExtractionOpensOnce()
        {
            var s = Make();
            int paid = 0, opened = 0;
            s.SurvivalPayout   += amount => paid += amount;
            s.ExtractionOpened += () => opened++;

            for (int i = 0; i < 45; i++) s.Tick(1f, false);
            Assert.AreEqual(12, paid); // 4 intervals x 3
            Assert.AreEqual(1, opened);
        }
    }
}
