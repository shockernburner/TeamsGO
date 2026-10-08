using System;
using NUnit.Framework;
using ProjectFossil.Match;

namespace ProjectFossil.Tests
{
    public class CareerScoreTests
    {
        [Test]
        public void EscapeBonusFollowsTheAgreedTable()
        {
            Assert.AreEqual(0f,   CareerScore.EscapeBonus(0));
            Assert.AreEqual(1.0f, CareerScore.EscapeBonus(1));
            Assert.AreEqual(1.3f, CareerScore.EscapeBonus(2));
            Assert.AreEqual(1.6f, CareerScore.EscapeBonus(3));
            Assert.AreEqual(2.0f, CareerScore.EscapeBonus(4));
        }

        [Test]
        public void TeamScoreIsCombinedScoreTimesTheBonus()
        {
            Assert.AreEqual(4000, CareerScore.TeamDelta(new[] { 1000, 500, 300, 200 }, 4, 0));
            Assert.AreEqual(1950, CareerScore.TeamDelta(new[] { 1000, 500 }, 2, 0));
            Assert.AreEqual(800,  CareerScore.TeamDelta(new[] { 800 }, 1, 0));
        }

        [Test]
        public void WipeCostsTheTeamAQuarterOfItsAverageMatch()
        {
            Assert.AreEqual(-500, CareerScore.TeamDelta(new[] { 900, 700 }, 0, 2000));
            Assert.AreEqual(0, CareerScore.TeamDelta(new[] { 900 }, 0, 0), "a new team's first wipe costs nothing");
        }

        [Test]
        public void PlayersBankWhatTheyEarned_EvenWhenTheyDied()
        {
            // A death already costs a quarter of the match score (ScoreModel); the total adds what's left. A first
            // online match that ended in death used to cost 50 from zero and never reached the board.
            Assert.AreEqual(1200, CareerScore.PlayerDelta(1200));
            Assert.AreEqual(0,    CareerScore.PlayerDelta(-5));
        }

        [Test]
        public void TotalsNeverGoNegative()
        {
            Assert.AreEqual(0, CareerScore.Apply(30, -50));
            Assert.AreEqual(150, CareerScore.Apply(100, 50));
        }

        [Test]
        public void WeekKeyUsesIsoWeeks()
        {
            Assert.AreEqual("2026-W41", CareerScore.WeekKey(new DateTime(2026, 10, 8)));
            Assert.AreEqual("2026-W01", CareerScore.WeekKey(new DateTime(2025, 12, 29)), "a week belongs to the year of its Thursday");
            Assert.AreEqual("2020-W53", CareerScore.WeekKey(new DateTime(2021, 1, 3)));
        }

        [Test]
        public void CountryCodesAreCheckedAndBoardsNamed()
        {
            Assert.AreEqual("PK", Country.Normalize(" pk "));
            Assert.AreEqual(Country.Unknown, Country.Normalize("Pakistan"));
            Assert.AreEqual(Country.Unknown, Country.Normalize(null));
            Assert.AreEqual("survivors_GB", Country.BoardId("survivors", "gb"));
            Assert.AreEqual("survivors", Country.BoardId("survivors", "??"));
        }

        [Test]
        public void ImpossibleScoresAreRejected()
        {
            Assert.IsTrue(ScoreCheck.IsPlausible(3000, 600f, 12, out _));
            Assert.IsFalse(ScoreCheck.IsPlausible(500, 10f, 0, out var r1)); StringAssert.Contains("short", r1);
            Assert.IsFalse(ScoreCheck.IsPlausible(999999, 300f, 1, out var r2)); StringAssert.Contains("too high", r2);
            Assert.IsFalse(ScoreCheck.IsPlausible(-5, 300f, 0, out _));
        }

        [Test]
        public void CrewReportsMustDescribeTheSameMatch()
        {
            Assert.IsTrue(ScoreCheck.CrewAgrees(new[] { (42, 600f), (42, 604f) }));
            Assert.IsFalse(ScoreCheck.CrewAgrees(new[] { (42, 600f), (43, 600f) }), "different islands");
            Assert.IsFalse(ScoreCheck.CrewAgrees(new[] { (42, 600f), (42, 900f) }), "different lengths");
        }
    }
}
