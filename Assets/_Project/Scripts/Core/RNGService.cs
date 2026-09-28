using System;

namespace ProjectFossil.Core
{
    // All procedural generation must use this — never UnityEngine.Random.
    public class RNGService
    {
        public int Seed { get; }

        private readonly Random _rng;

        public RNGService(int seed)
        {
            Seed = seed;
            _rng = new Random(seed);
        }

        public int Next() => _rng.Next();
        public int Next(int maxExclusive) => _rng.Next(maxExclusive);
        public int Next(int minInclusive, int maxExclusive) => _rng.Next(minInclusive, maxExclusive);
        public float NextFloat() => (float)_rng.NextDouble();
        public double NextDouble() => _rng.NextDouble();
    }
}
