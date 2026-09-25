using System;

namespace AnimatedDrawingsWorld.Logic
{
    // Deterministic RNG so simulations are reproducible in tests; the game seeds it from time.
    public sealed class SeededRandom
    {
        private readonly Random random;

        public SeededRandom(int seed) => random = new Random(seed);

        public float Value => (float)random.NextDouble();
        public float Range(float min, float max) => min + (max - min) * Value;
        public int Range(int minInclusive, int maxExclusive) => random.Next(minInclusive, maxExclusive);
        public bool Chance(float probability) => Value < probability;

        public T Pick<T>(System.Collections.Generic.IReadOnlyList<T> items) =>
            items.Count == 0 ? default : items[random.Next(items.Count)];
    }
}
