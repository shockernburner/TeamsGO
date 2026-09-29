using System;

namespace ProjectFossil.Match
{
    // Turns a match into one number, so 10 minutes with 100 kills clearly beats 5 quiet minutes.
    // Pure: the match feeds it events, the results screen reads the breakdown.
    public class ScoreModel
    {
        public const int PointsPerSecond      = 2;
        public const int PointsPerThreat      = 50;
        public const float ExtractedMultiplier = 1.5f;
        public const float DiedMultiplier      = 0.75f;

        public int   KillPoints   { get; private set; }
        public float DamageDealt  { get; private set; }
        public int   Threats      { get; private set; }

        public void AddKill(int scoreValue) => KillPoints += Math.Max(0, scoreValue);
        public void AddDamage(float amount) { if (amount > 0f) DamageDealt += amount; }
        public void AddThreatFaced() => Threats++;

        public int SurvivalPoints(float secondsSurvived) => (int)(Math.Max(0f, secondsSurvived) * PointsPerSecond);
        public int DamagePoints => (int)DamageDealt;
        public int ThreatPoints => Threats * PointsPerThreat;

        public int Subtotal(float secondsSurvived) =>
            SurvivalPoints(secondsSurvived) + KillPoints + DamagePoints + ThreatPoints;

        public static float Multiplier(MatchResult result)
        {
            switch (result)
            {
                case MatchResult.Extracted: return ExtractedMultiplier;
                case MatchResult.Died:      return DiedMultiplier;
                default:                    return 1f;
            }
        }

        // Live score while playing counts as if you'd survive (no multiplier yet).
        public int Total(float secondsSurvived, MatchResult result = MatchResult.None) =>
            (int)Math.Round(Subtotal(secondsSurvived) * Multiplier(result));
    }
}
