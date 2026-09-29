using System;

namespace ProjectFossil.Match
{
    // Long-term skill, carried between matches. A rating from 0 to 100 moves after every match depending on how
    // the score compares with what is expected at the current rank: beat it and the rank climbs, fall short and
    // it slips. The rank sets how hard new islands start (director baseline, dinosaur toughness, wildlife count).
    // Pure; saving and loading the rating is the caller's job.
    public class SurvivorRank
    {
        public const int   MaxLevel      = 10;
        public const float MaxRating     = 100f;
        public const float MaxGainPerMatch = 10f;
        public const float MaxLossPerMatch = 6f;
        public const float ExtractionBonus = 2f;

        public float Rating { get; private set; }
        public int   Level  => Math.Min(MaxLevel, (int)(Rating / (MaxRating / MaxLevel)));

        public SurvivorRank(float rating = 0f) => Rating = Clamp(rating, 0f, MaxRating);

        // Score that holds the rank steady at a level: about 7 minutes of steady fighting at level 0.
        public static int ExpectedScore(int level) => (int)(1500f * (1f + 0.35f * Math.Max(0, level)));

        // Difficulty knobs for a level. Level 0 = as tuned in the data.
        public static float DirectorBaseline(int level) => 1f + 0.1f * Math.Max(0, level);
        public static float DinosaurHealth(int level)   => 1f + 0.06f * Math.Max(0, level);
        public static float DinosaurDamage(int level)   => 1f + 0.04f * Math.Max(0, level);
        public static float WildlifeCount(int level)    => 1f + 0.08f * Math.Max(0, level);

        // Rating change a match would cause, without applying it.
        public float DeltaFor(int score, MatchResult result)
        {
            float expected = ExpectedScore(Level);
            float performance = expected > 0f ? score / expected : 1f;
            float delta = (performance - 1f) * 8f;
            if (result == MatchResult.Extracted) delta += ExtractionBonus;
            return Clamp(delta, -MaxLossPerMatch, MaxGainPerMatch);
        }

        // Applies a finished match. Returns the rating change.
        public float Apply(int score, MatchResult result)
        {
            float before = Rating;
            Rating = Clamp(Rating + DeltaFor(score, result), 0f, MaxRating);
            return Rating - before;
        }

        private static float Clamp(float v, float lo, float hi) => v < lo ? lo : v > hi ? hi : v;
    }
}
