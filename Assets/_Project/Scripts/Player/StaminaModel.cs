using System;

namespace ProjectFossil.Player
{
    // Pure stamina rules. Running drains it; hitting zero leaves you exhausted (no running or jumping,
    // slower walk) until it recovers past a threshold. Kept free of Unity types so it can be unit-tested.
    public class StaminaModel
    {
        public float Max     { get; }
        public float Current { get; private set; }
        public bool  IsExhausted { get; private set; }
        public float Fraction => Max > 0f ? Current / Max : 0f;

        public bool CanRun => !IsExhausted && Current > 0f;

        public float DrainPerSecond     { get; set; } = 15f;
        public float RegenPerSecond     { get; set; } = 8f;   // while moving without running
        public float IdleRegenMultiplier { get; set; } = 1.75f; // standing still recovers faster
        public float RegenDelay         { get; set; } = 1.2f; // pause after running/jumping before regen
        public float RecoverFraction    { get; set; } = 0.3f; // exhaustion ends at this fraction

        private float _regenTimer;

        public StaminaModel(float max)
        {
            if (max <= 0f) throw new ArgumentOutOfRangeException(nameof(max));
            Max     = max;
            Current = max;
        }

        public void Tick(float deltaTime, bool running, bool moving)
        {
            if (deltaTime <= 0f) return;

            if (running && CanRun)
            {
                Current     = Math.Max(0f, Current - DrainPerSecond * deltaTime);
                _regenTimer = 0f;
                if (Current <= 0f) IsExhausted = true;
                return;
            }

            _regenTimer += deltaTime;
            if (_regenTimer < RegenDelay) return;

            float rate = RegenPerSecond * (moving ? 1f : IdleRegenMultiplier);
            Current = Math.Min(Max, Current + rate * deltaTime);
            if (IsExhausted && Current >= Max * RecoverFraction) IsExhausted = false;
        }

        // For one-off costs such as jumping. Fails (and costs nothing) when exhausted or short.
        public bool TrySpend(float amount)
        {
            if (amount <= 0f) return true;
            if (IsExhausted || Current < amount) return false;

            Current    -= amount;
            _regenTimer = 0f;
            if (Current <= 0f) IsExhausted = true;
            return true;
        }
    }
}
