namespace ProjectFossil.Match
{
    public enum ChallengeLevel { Easy, Medium, Hard }

    // How hard a solo island is, picked on the start menu. Hard is the game as tuned (and what co-op always uses);
    // Easy and Medium scale it down on top of the Survivor Rank: a calmer director that starts later, weaker
    // bites, fewer animals and a later Ironjaw. Pure data, so it can be tested.
    public static class Challenge
    {
        // The level the next match starts at. Co-op sets it back to Hard.
        public static ChallengeLevel Current = ChallengeLevel.Hard;

        public static string Label(ChallengeLevel level) =>
            level == ChallengeLevel.Easy ? "Easy" : level == ChallengeLevel.Medium ? "Medium" : "Hard";

        public static string Describe(ChallengeLevel level) =>
            level == ChallengeLevel.Easy   ? "Fewer, weaker dinosaurs and a long quiet start. Learn the island."
          : level == ChallengeLevel.Medium ? "A fair fight. Threats come later and bite softer."
          :                                  "The island as it's meant to be. Nothing holds back.";

        // Multiplies the director's baseline intensity (how much it spends and how many it sends).
        public static float Director(ChallengeLevel level)     => Pick(level, 0.6f, 0.8f);
        // Seconds added to the quiet start before the director's first threat.
        public static float ExtraGrace(ChallengeLevel level)   => Pick(level, 90f, 40f, 0f);
        public static float DinosaurDamage(ChallengeLevel level) => Pick(level, 0.5f, 0.75f);
        public static float DinosaurHealth(ChallengeLevel level) => Pick(level, 0.75f, 0.9f);
        public static float Wildlife(ChallengeLevel level)     => Pick(level, 0.6f, 0.85f);
        // Multiplies when Ironjaw comes out.
        public static float StalkerDelay(ChallengeLevel level) => Pick(level, 1.5f, 1.2f);

        private static float Pick(ChallengeLevel level, float easy, float medium, float hard = 1f) =>
            level == ChallengeLevel.Easy ? easy : level == ChallengeLevel.Medium ? medium : hard;
    }
}
