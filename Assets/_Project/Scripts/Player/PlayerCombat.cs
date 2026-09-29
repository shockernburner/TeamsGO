using UnityEngine;
using UnityEngine.InputSystem;
using ProjectFossil.Core;

namespace ProjectFossil.Player
{
    // Placeholder melee: a short sphere-cast from the camera. Uses the equipped weapon if an
    // IWeaponProvider (the inventory) is present, otherwise bare hands.
    [RequireComponent(typeof(PlayerController))]
    public class PlayerCombat : MonoBehaviour
    {
        [Header("Bare hands")]
        public float unarmedDamage   = 8f;
        public float unarmedRange    = 2f;
        public float unarmedCooldown = 0.6f;

        public float hitRadius = 0.4f;

        public float CooldownRemaining { get; private set; }
        public WeaponStats CurrentWeapon => GetWeapon();

        private PlayerController _controller;
        private IWeaponProvider  _weapons;
        private Camera           _camera;

        private void Awake()
        {
            _controller = GetComponent<PlayerController>();
            _camera     = GetComponentInChildren<Camera>();
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

            Transform origin = _camera != null ? _camera.transform : transform;
            var hits = Physics.SphereCastAll(origin.position, hitRadius, origin.forward,
                                             weapon.Range + 1.5f, // camera sits behind/above the body
                                             Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);

            IDamageable best = null;
            float bestDist = float.MaxValue;
            Vector3 bestPoint = Vector3.zero;
            foreach (var hit in hits)
            {
                if (hit.collider.transform.IsChildOf(transform)) continue;

                // Measure reach from the body, not the camera
                float bodyDist = Vector3.Distance(transform.position, hit.collider.ClosestPoint(transform.position));
                if (bodyDist > weapon.Range) continue;

                var target = hit.collider.GetComponentInParent<IDamageable>();
                if (target == null || !target.IsAlive) continue;
                if (hit.distance < bestDist)
                {
                    best      = target;
                    bestDist  = hit.distance;
                    bestPoint = hit.point;
                }
            }

            if (best == null) return false;
            best.TakeDamage(new DamageInfo(weapon.Damage, gameObject, bestPoint));
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
