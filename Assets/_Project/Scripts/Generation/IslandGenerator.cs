using ProjectFossil.Core;

namespace ProjectFossil.Generation
{
    public class IslandGenerator
    {
        private readonly RNGService _rng;

        public IslandGenerator(int seed)
        {
            _rng = new RNGService(seed);
        }
    }
}
