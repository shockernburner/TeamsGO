namespace ProjectFossil.Core
{
    // How easy something is to hear and see right now (1 = normal). Dinosaurs scale their
    // hearing and sight ranges by these, so crouching and crawling actually matter.
    public interface IStealthProfile
    {
        float NoiseMultiplier      { get; }
        float VisibilityMultiplier { get; }
    }
}
