namespace ProjectFossil.Net
{
    // A crew name to start from (the player can type their own): two words in the salvage crews' style.
    public static class CrewNames
    {
        private static readonly string[] First =
        {
            "Quiet", "Low", "Silent", "Red", "Iron", "Ash", "Night", "Ember", "Hollow", "Grey", "Last", "Long",
            "Salt", "Storm", "Cinder", "Black", "Amber", "Drift", "Bone", "Copper", "Wild", "Lost", "Still", "Far",
        };
        private static readonly string[] Second =
        {
            "Talons", "Line", "Tide", "Lantern", "Rope", "Signal", "Ridge", "Hollow", "Fern", "Flare", "Anchor", "Wick",
            "Harbor", "Compass", "Rotor", "Tether", "Cordon", "Embers", "Rangers", "Echo", "Wake", "Ladder", "Canopy", "Ward",
        };

        public static string Random(System.Random rng)
        {
            string a = First[rng.Next(First.Length)], b = Second[rng.Next(Second.Length)];
            return a == b ? a + " Team" : $"{a} {b}";
        }
    }
}
