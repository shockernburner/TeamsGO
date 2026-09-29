namespace ProjectFossil.Core
{
    // Which side of an online match this machine is on. Game code reads it; only the networking layer sets it.
    // Solo and host are the authority: they run the director, wildlife and every dinosaur's brain.
    // A follower (someone who joined a friend's island) only mirrors the animals the host sends.
    public static class NetRole
    {
        public static bool IsFollower { get; set; }
    }
}
