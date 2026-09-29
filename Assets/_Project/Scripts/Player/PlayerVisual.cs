using UnityEngine;
using ProjectFossil.Core;

namespace ProjectFossil.Player
{
    // Shows an imported character model in place of the placeholder capsule and drives its Animator from the
    // controller, combat and health. Presentation only; nothing in gameplay reads it.
    //
    // Animator parameters (built by the model setup tool): Speed (float, m/s), Crouch (bool), Grounded (bool),
    // Attack (trigger), Armed (bool: weapon swing vs punch), Hit (trigger), Dead (bool).
    public class PlayerVisual : MonoBehaviour
    {
        private static readonly int SpeedId    = Animator.StringToHash("Speed");
        private static readonly int CrouchId   = Animator.StringToHash("Crouch");
        private static readonly int GroundedId = Animator.StringToHash("Grounded");
        private static readonly int AttackId   = Animator.StringToHash("Attack");
        private static readonly int ArmedId    = Animator.StringToHash("Armed");
        private static readonly int HitId      = Animator.StringToHash("Hit");
        private static readonly int DeadId     = Animator.StringToHash("Dead");

        public const string UnarmedName = "Bare hands";

        private PlayerController _controller;
        private PlayerCombat     _combat;
        private Health           _health;
        private Animator         _animator;
        private Vector3          _lastPos;

        // Adds the visual to a spawned player. No-op when the definition has no model.
        public static PlayerVisual Attach(GameObject player, ModelDefinition def)
        {
            if (player == null || def == null || !def.HasModel) return null;
            var v = player.GetComponent<PlayerVisual>();
            if (v == null) v = player.AddComponent<PlayerVisual>();
            v.Build(def);
            return v;
        }

        private void Build(ModelDefinition def)
        {
            _controller = GetComponent<PlayerController>();
            _combat     = GetComponent<PlayerCombat>();
            _health     = GetComponent<Health>();

            // Hide the capsule body. The camera and colliders stay as they are.
            foreach (var r in GetComponentsInChildren<Renderer>()) r.enabled = false;

            var model = ModelFit.Spawn(def, transform);
            foreach (var r in model.GetComponentsInChildren<Renderer>())
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;

            if (def.animator != null)
            {
                _animator = model.GetComponentInChildren<Animator>();
                if (_animator == null) _animator = model.AddComponent<Animator>();
                _animator.runtimeAnimatorController = def.animator;
                _animator.applyRootMotion = false;
            }

            if (_combat != null) _combat.Attacked += OnAttacked;
            if (_health != null) { _health.Damaged += OnDamaged; _health.Died += OnDied; }
            _lastPos = transform.position;
        }

        private void OnDestroy()
        {
            if (_combat != null) _combat.Attacked -= OnAttacked;
            if (_health != null) { _health.Damaged -= OnDamaged; _health.Died -= OnDied; }
        }

        private void Update()
        {
            if (_animator == null) return;
            Vector3 d = transform.position - _lastPos;
            _lastPos = transform.position;
            d.y = 0f;
            float speed = Time.deltaTime > 0f ? d.magnitude / Time.deltaTime : 0f;
            if (speed > 30f) speed = 0f; // teleported (respawn/rescue), not running

            _animator.SetFloat(SpeedId, speed, 0.1f, Time.deltaTime);
            if (_controller != null)
            {
                _animator.SetBool(CrouchId, _controller.Stance != Stance.Standing);
                _animator.SetBool(GroundedId, _controller.IsGrounded);
            }
        }

        private void OnAttacked(WeaponStats weapon, Health target)
        {
            if (_animator == null) return;
            _animator.SetBool(ArmedId, weapon.Name != UnarmedName);
            _animator.SetTrigger(AttackId);
        }

        private void OnDamaged(DamageInfo info)
        {
            if (_animator != null && _health != null && _health.IsAlive) _animator.SetTrigger(HitId);
        }

        private void OnDied(DamageInfo info)
        {
            if (_animator != null) _animator.SetBool(DeadId, true);
        }
    }
}
