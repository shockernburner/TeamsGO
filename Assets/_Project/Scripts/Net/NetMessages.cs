using FishNet.Broadcast;

namespace ProjectFossil.Net
{
    // Host -> joiner: which island to build and how far into the match the host is. The island itself is never
    // sent; every machine generates the same one from the seed.
    public struct IslandMessage : IBroadcast
    {
        public int   Seed;
        public float Elapsed;
    }

    // Joiner -> host: this island is built and my player is standing on it. Send my body.
    public struct ReadyMessage : IBroadcast
    {
        public int Seed;
    }

    // Host -> team: something everyone should hear about (a pack sent in, the stalker loose).
    public struct AnnounceMessage : IBroadcast
    {
        public string Text;
    }

    // Joiner -> host: I've started boarding a helicopter. Send the final wave to my pad.
    public struct FinalStandMessage : IBroadcast
    {
        public byte Unused;
    }
}
