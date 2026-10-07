using FishNet.Broadcast;

namespace ProjectFossil.Net
{
    // Host -> joiner: which island to build and how far into the match the host is. The island itself is never
    // sent; every machine generates the same one from the seed.
    public struct IslandMessage : IBroadcast
    {
        public int   Seed;
        public float Elapsed;
        public byte  Time;     // DayTime the host's island got (never Random)
        public byte  Weather;  // Weather likewise
        public float Wind;
        public string Crew;    // the host's crew name: the whole team plays under it
    }

    // Joiner -> host: this island is built and my player is standing on it. Send my body.
    public struct ReadyMessage : IBroadcast
    {
        public int    Seed;
        public string Name; // what this player wants to be called (the host makes it unique)
    }

    // Host -> team: something everyone should hear about (a pack sent in, the stalker loose, a teammate down).
    // From is the client it's about or came from; that client already heard it and skips it. -1: everyone.
    public struct AnnounceMessage : IBroadcast
    {
        public string Text;
        public int    From;
    }

    // Anyone -> host: tell the rest of the team this (the host passes it on as an AnnounceMessage).
    public struct TeamMessage : IBroadcast
    {
        public string Text;
    }

    // Anyone -> host -> team: my rescue flare dropped a helicopter pad here. The host also replays these to
    // late joiners.
    public struct FlareMessage : IBroadcast
    {
        public UnityEngine.Vector3 Pad;
        public int From;
    }

    // Anyone -> host -> team: my helicopter at Pad just took off; whoever is standing under it comes too.
    public struct LiftOffMessage : IBroadcast
    {
        public UnityEngine.Vector3 Pad;
        public int From;
    }

    // Joiner -> host: I've started boarding a helicopter. Send the final wave to my pad.
    public struct FinalStandMessage : IBroadcast
    {
        public byte Unused;
    }
}
