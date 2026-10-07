using System.IO;
using NUnit.Framework;
using ProjectFossil.Match;

namespace ProjectFossil.Tests.EditMode
{
    public class LeaderboardTests
    {
        private static RunRecord Run(string player, string crew, int score, long when = 0) =>
            new RunRecord { Player = player, Crew = crew, Score = score, CrewSize = 1, WhenUtcTicks = when, Result = MatchResult.Extracted };

        [Test]
        public void Record_ReturnsPlaceAmongAllRuns()
        {
            var board = new Leaderboard();
            Assert.AreEqual(1, board.Record(Run("Kestrel", "Quiet Talons", 500)));
            Assert.AreEqual(1, board.Record(Run("Rook", "Quiet Talons", 900)));
            Assert.AreEqual(3, board.Record(Run("Vega", "Ash Line", 100)));
            Assert.AreEqual(2, board.Record(Run("Vega", "Ash Line", 700)));
        }

        [Test]
        public void Survivors_KeepEachPlayersBest_IgnoringCase()
        {
            var board = new Leaderboard();
            board.Record(Run("Kestrel", "A", 300));
            board.Record(Run("kestrel", "A", 800));
            board.Record(Run("Rook", "B", 500));
            var top = board.TopSurvivors(10);
            Assert.AreEqual(2, top.Count);
            Assert.AreEqual(800, top[0].Score);
            Assert.AreEqual("Rook", top[1].Player);
        }

        [Test]
        public void Crews_KeepEachCrewsBest()
        {
            var board = new Leaderboard();
            board.Record(Run("Kestrel", "Quiet Talons", 300));
            board.Record(Run("Rook", "Quiet Talons", 600));
            board.Record(Run("Vega", "Ash Line", 450));
            board.Record(Run("Moss", "", 999)); // no crew name: not on the crew board
            var top = board.TopCrews(10);
            Assert.AreEqual(2, top.Count);
            Assert.AreEqual("Quiet Talons", top[0].Crew);
            Assert.AreEqual(600, top[0].Score);
        }

        [Test]
        public void Ties_KeepTheEarlierRunAhead()
        {
            var board = new Leaderboard();
            board.Record(Run("First", "A", 400, when: 10));
            board.Record(Run("Second", "B", 400, when: 20));
            Assert.AreEqual("First", board.Runs[0].Player);
        }

        [Test]
        public void KeepsOnlyTheBestRuns()
        {
            var board = new Leaderboard();
            for (int i = 0; i < Leaderboard.Keep + 20; i++) board.Record(Run("P" + i, "C", i));
            Assert.AreEqual(Leaderboard.Keep, board.Runs.Count);
            Assert.AreEqual(Leaderboard.Keep + 19, board.Runs[0].Score);
            Assert.AreEqual(0, board.Record(Run("Low", "C", -1))); // didn't make the board
        }

        [Test]
        public void SavesAndLoads()
        {
            string path = Path.Combine(Path.GetTempPath(), "fossil_board_test.json");
            if (File.Exists(path)) File.Delete(path);
            try
            {
                var board = new Leaderboard(path);
                board.Record(Run("Kestrel", "Quiet Talons", 640));
                var again = new Leaderboard(path);
                Assert.AreEqual(1, again.Runs.Count);
                Assert.AreEqual("Quiet Talons", again.Runs[0].Crew);
                Assert.AreEqual(640, again.Runs[0].Score);
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        }
    }
}
