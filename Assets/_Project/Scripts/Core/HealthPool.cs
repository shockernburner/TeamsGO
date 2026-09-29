using System;

namespace ProjectFossil.Core
{
    // Pure health model. No Unity dependencies so it can be tested and later synced over the network.
    public class HealthPool
    {
        public float Max     { get; private set; }
        public float Current { get; private set; }
        public bool  IsDead  => Current <= 0f;
        public float Fraction => Max > 0f ? Current / Max : 0f;

        // (current, max)
        public event Action<float, float> Changed;
        public event Action Died;

        public HealthPool(float max)
        {
            Reset(max);
        }

        public void Reset(float max)
        {
            if (max <= 0f) throw new ArgumentOutOfRangeException(nameof(max), "Max health must be positive.");
            Max     = max;
            Current = max;
            Changed?.Invoke(Current, Max);
        }

        // Returns the damage actually applied.
        public float ApplyDamage(float amount)
        {
            if (amount <= 0f || IsDead) return 0f;

            float applied = Math.Min(amount, Current);
            Current -= applied;
            Changed?.Invoke(Current, Max);
            if (IsDead) Died?.Invoke();
            return applied;
        }

        // Returns the amount actually healed. The dead cannot be healed.
        public float Heal(float amount)
        {
            if (amount <= 0f || IsDead) return 0f;

            float healed = Math.Min(amount, Max - Current);
            Current += healed;
            if (healed > 0f) Changed?.Invoke(Current, Max);
            return healed;
        }
    }
}
