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

        // Online, a copy of something another machine owns: hits go to the owner instead of landing here.
        // Return true when the hit was sent on.
        public Func<DamageInfo, bool> Redirect;

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
            if (Redirect != null && Redirect(info)) return;
            Apply(info);
        }

        // Online copy: take the owner's numbers. A drop plays as a hit (source unknown) so the flash, the
        // stagger animation and death all happen here too.
        public void Mirror(float current, float max)
        {
            if (Pool == null) Pool = new HealthPool(max);
            if (max > 0f && !Mathf.Approximately(Pool.Max, max))
            {
                // First look (or a new maximum): take the numbers quietly, it isn't a hit.
                maxHealth = max;
                Pool.Reset(max);
                if (current > 0f && current < max) Pool.ApplyDamage(max - current);
                if (current > 0f) return;
            }
            float drop = Pool.Current - current;
            if (drop > 0.001f) Apply(new DamageInfo(drop, null, transform.position));
            else if (drop < -0.001f) Pool.Heal(-drop);
        }

        private void Apply(DamageInfo info)
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
