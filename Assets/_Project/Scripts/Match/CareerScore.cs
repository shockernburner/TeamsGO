using System;
using System.Collections.Generic;
using System.Globalization;

namespace ProjectFossil.Match
{
    // The online scoring rules: what a finished match adds to a player's and a team's running totals.
    // Pure, so the game, the tests and the server-side check (Cloud Code) can all apply the same numbers.
    //
    //   Survivors board: each player's own score adds up across matches (all-time and weekly).
    //   Teams board:     each match adds the crew's combined score times a bonus for how many got out.
    //   Wipe (nobody out): the team loses a quarter of its average match, and each player loses 50 points.
    public static class CareerScore
    {
        public const int   WipePenaltyPerPlayer   = 50;
        public const float WipePenaltyTeamFraction = 0.25f;

        // Bonus for how many of the crew reached the helicopter. 0 is a wipe and has no multiplier.
        public static float EscapeBonus(int escaped)
        {
            switch (escaped)
            {
                case 1:  return 1.0f;
                case 2:  return 1.3f;
                case 3:  return 1.6f;
                default: return escaped >= 4 ? 2.0f : 0f;
            }
        }

        // What one player's match adds to their own total: their score, or the wipe penalty if the whole crew died.
        public static int PlayerDelta(int matchScore, bool teamWiped) =>
            teamWiped ? -WipePenaltyPerPlayer : Math.Max(0, matchScore);

        // What a match adds to the team's total. memberScores are each player's own match score; averageMatch is
        // the team's average match result so far (0 for a new team, so its first wipe costs nothing).
        public static int TeamDelta(IReadOnlyList<int> memberScores, int escaped, int averageMatch)
        {
            if (escaped <= 0) return -(int)Math.Round(Math.Max(0, averageMatch) * WipePenaltyTeamFraction);
            long sum = 0;
            if (memberScores != null) foreach (int s in memberScores) sum += Math.Max(0, s);
            return (int)Math.Min(int.MaxValue, Math.Round(sum * EscapeBonus(escaped)));
        }

        // Running totals never drop below zero, so a new player can't be pushed into the negatives.
        public static long Apply(long total, int delta) => Math.Max(0L, total + delta);

        // The weekly board's key, e.g. "2026-W41". Weeks start on Monday (ISO 8601), the same everywhere.
        public static string WeekKey(DateTime utc)
        {
            // ISO week by hand (System.Globalization.ISOWeek isn't in Unity's .NET Standard 2.1): the week belongs
            // to the year that holds its Thursday.
            DateTime day = utc.Date;
            int dow = ((int)day.DayOfWeek + 6) % 7;     // Monday = 0
            DateTime thursday = day.AddDays(3 - dow);
            int year = thursday.Year;
            int week = (thursday.DayOfYear - 1) / 7 + 1;
            return year.ToString("D4", CultureInfo.InvariantCulture) + "-W" + week.ToString("D2", CultureInfo.InvariantCulture);
        }
    }

    // A player's country for the country boards: a two-letter ISO 3166 code such as "PK" or "GB".
    public static class Country
    {
        public const string Unknown = "ZZ";

        // Upper-cases a code and checks it's two letters; anything else becomes Unknown.
        public static string Normalize(string code)
        {
            if (string.IsNullOrWhiteSpace(code)) return Unknown;
            string c = code.Trim().ToUpperInvariant();
            if (c.Length != 2 || c[0] < 'A' || c[0] > 'Z' || c[1] < 'A' || c[1] > 'Z') return Unknown;
            return c;
        }

        // The computer's region, as the starting choice in the profile. The player can change it.
        public static string FromSystem()
        {
            try { return Normalize(RegionInfo.CurrentRegion.TwoLetterISORegionName); }
            catch (Exception) { return Unknown; }
        }

        // Board ids: one per country next to the global one, e.g. "survivors" and "survivors_PK".
        public static string BoardId(string globalBoard, string country)
        {
            string c = Normalize(country);
            return c == Unknown ? globalBoard : globalBoard + "_" + c;
        }
    }

    // Rejects match reports that can't be real. The game runs it before sending, and the same rules run on the
    // server, which is what stops a modified game. It catches easy cheats, not all of them.
    public static class ScoreCheck
    {
        public const float MinSeconds = 30f;           // shorter than this isn't a match
        public const float MaxSeconds = 45f * 60f;     // longest a match can run, with slack
        public const float MaxPointsPerSecond = 60f;   // generous ceiling for the best play seen; tune from real data
        public const float SecondsTolerance = 10f;     // crew reports of the same match must agree this closely

        public static bool IsPlausible(int score, float seconds, int kills, out string reason)
        {
            reason = null;
            if (score < 0)                         { reason = "negative score"; return false; }
            if (kills < 0)                         { reason = "negative kills"; return false; }
            if (float.IsNaN(seconds) || seconds < MinSeconds) { reason = "match too short"; return false; }
            if (seconds > MaxSeconds)              { reason = "match too long"; return false; }
            if (score > seconds * MaxPointsPerSecond) { reason = "score too high for the time played"; return false; }
            return true;
        }

        // A crew's reports of one match must share the island seed and roughly the same length.
        public static bool CrewAgrees(IReadOnlyList<(int seed, float seconds)> reports)
        {
            if (reports == null || reports.Count == 0) return false;
            int seed = reports[0].seed;
            float min = float.MaxValue, max = float.MinValue;
            foreach (var r in reports)
            {
                if (r.seed != seed) return false;
                min = Math.Min(min, r.seconds);
                max = Math.Max(max, r.seconds);
            }
            return max - min <= SecondsTolerance;
        }
    }
}
