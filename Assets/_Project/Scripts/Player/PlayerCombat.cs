using UnityEngine;
using UnityEngine.InputSystem;
using ProjectFossil.Core;

namespace ProjectFossil.Player
{
    // Placeholder melee: hits the nearest damageable thing in front of the body. Uses the equipped weapon if an
    // IWeaponProvider (the inventory) is present, otherwise bare hands.
    [RequireComponent(typeof(PlayerController))]
    public class PlayerCombat : MonoBehaviour
    {
        [Header("Bare hands")]
        public float unarmedDamage   = 12f;
        public float unarmedRange    = 2f;
        public float unarmedCooldown = 0.6f;

        public float hitRadius = 0.4f;

        public float CooldownRemaining { get; private set; }
        public WeaponStats CurrentWeapon => GetWeapon();

        private PlayerController _controller;
        private IWeaponProvider  _weapons;
        private readonly Collider[] _buffer = new Collider[32];

        // (weapon used, health of what was hit; null target = miss or something without Health)
        public event System.Action<WeaponStats, Health> Attacked;

        private void Awake()
        {
            _controller = GetComponent<PlayerController>();
        }

        private void Update()
        {
            if (CooldownRemaining > 0f) CooldownRemaining -= Time.deltaTime;
        }

        public void OnAttack(InputValue v)
        {
            if (!v.isPressed) return;
            TryAttack();
        }

        public bool TryAttack()
        {
            if (!_controller.enabled || _controller.InputBlocked) return false;
            if (!_controller.Health.IsAlive || CooldownRemaining > 0f) return false;

            var weapon = GetWeapon();
            CooldownRemaining = weapon.Cooldown;

            // Swing from the body (the third-person camera sits ~4 m behind it) in the direction the player faces.
            Vector3 origin = transform.position + Vector3.up * 1f;
            Vector3 center = origin + transform.forward * (weapon.Range * 0.5f);
            int count = Physics.OverlapSphereNonAlloc(center, weapon.Range * 0.5f + hitRadius, _buffer,
                                                      Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);

            IDamageable best = null;
            Collider bestCollider = null;
            float bestDist = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                var col = _buffer[i];
                if (col.transform.IsChildOf(transform)) continue;

                var target = col.GetComponentInParent<IDamageable>();
                if (target == null || !target.IsAlive) continue;

                Vector3 closest = col.ClosestPoint(origin);
                Vector3 to = closest - origin;
                to.y = 0f;
                if (to.sqrMagnitude > 0.01f && Vector3.Dot(transform.forward, to.normalized) < 0.2f) continue; // behind us

                float dist = to.magnitude;
                if (dist < bestDist)
                {
                    best         = target;
                    bestCollider = col;
                    bestDist     = dist;
                }
            }

            if (best == null)
            {
                Attacked?.Invoke(weapon, null);
                return false;
            }

            best.TakeDamage(new DamageInfo(weapon.Damage, gameObject, bestCollider.ClosestPoint(origin)));
            Attacked?.Invoke(weapon, best as Health);
            return true;
        }

        private WeaponStats GetWeapon()
        {
            if (_weapons == null) _weapons = GetComponent<IWeaponProvider>(); // may be added after Awake
            if (_weapons != null && _weapons.TryGetEquippedWeapon(out var w)) return w;
            return new WeaponStats("Bare hands", unarmedDamage, unarmedRange, unarmedCooldown);
        }
    }
}
