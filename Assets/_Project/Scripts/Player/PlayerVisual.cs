using UnityEngine;
using ProjectFossil.Core;

namespace ProjectFossil.Player
{
    // Shows an imported character model in place of the placeholder capsule and drives its Animator from the
    // controller, combat and health. Presentation only; nothing in gameplay reads it.
    //
    // Animator parameters (built by the model setup tool): Speed (float, m/s), Crouch (bool),
    // Attack (trigger), Armed (bool: weapon swing vs punch), Hit (trigger), Dead (bool).
    public class PlayerVisual : MonoBehaviour
    {
        private static readonly int SpeedId    = Animator.StringToHash("Speed");
        private static readonly int CrouchId   = Animator.StringToHash("Crouch");
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
        private Transform        _pivot;      // turns the model toward where it is moving
        private float            _yaw;        // current turn of the pivot, degrees
        private float            _faceForwardTimer;

        public float turnSpeed = 720f; // degrees per second

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

            _pivot = new GameObject("Visual").transform;
            _pivot.SetParent(transform, false);
            var model = ModelFit.Spawn(def, _pivot);
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
            if (_pivot == null) return;
            Vector3 d = transform.position - _lastPos;
            _lastPos = transform.position;
            d.y = 0f;
            float speed = Time.deltaTime > 0f ? d.magnitude / Time.deltaTime : 0f;
            if (speed > 30f) speed = 0f; // teleported (respawn/rescue), not running

            TurnTowards(d, speed);
            if (_animator == null) return;
            _animator.SetFloat(SpeedId, speed, 0.1f, Time.deltaTime);
            if (_controller != null) _animator.SetBool(CrouchId, _controller.Stance != Stance.Standing);
        }

        // The body faces the camera direction; the model faces where it is going, so backing up or strafing
        // shows the character running that way instead of moonwalking. Attacks snap it back to the aim.
        private void TurnTowards(Vector3 worldMove, float speed)
        {
            float target = 0f;
            if (_faceForwardTimer > 0f) _faceForwardTimer -= Time.deltaTime;
            else if (speed > 0.5f)
            {
                Vector3 local = transform.InverseTransformDirection(worldMove);
                target = Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg;
            }
            _yaw = Mathf.MoveTowardsAngle(_yaw, target, turnSpeed * Time.deltaTime);
            _pivot.localRotation = Quaternion.Euler(0f, _yaw, 0f);
        }

        private void OnAttacked(WeaponStats weapon, Health target)
        {
            _faceForwardTimer = 0.5f;
            _yaw = 0f; // swing where the player is aiming
            if (_pivot != null) _pivot.localRotation = Quaternion.identity;
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
