using System;
using UnityEngine;

namespace ProjectFossil.Core
{
    // Scene-side wrapper around HealthPool. Anything that can be hurt (players, dinosaurs) gets one.
    public class Health : MonoBehaviour, IDamageable
    {
        public float maxHealth = 100f;

        public HealthPool Pool { get; private set; }
        public bool  IsAlive  => !Pool.IsDead;
        public float Current  => Pool.Current;
        public float Max      => Pool.Max;
        public float Fraction => Pool.Fraction;

        public DamageInfo LastDamage { get; private set; }

        public event Action<DamageInfo> Damaged;
        public event Action<DamageInfo> Died;

        private void Awake()
        {
            if (Pool == null) Pool = new HealthPool(maxHealth);
        }

        // Call before the first frame to override the inspector value (e.g. from species data).
        public void Initialize(float max)
        {
            maxHealth = max;
            if (Pool == null) Pool = new HealthPool(max);
            else Pool.Reset(max);
        }

        public void TakeDamage(DamageInfo info)
        {
            if (!IsAlive) return;

            float applied = Pool.ApplyDamage(info.Amount);
            if (applied <= 0f) return;

            info.Amount = applied;
            LastDamage  = info;
            Damaged?.Invoke(info);
            if (!IsAlive) Died?.Invoke(info);
        }

        public float Heal(float amount) => Pool.Heal(amount);
    }
}
