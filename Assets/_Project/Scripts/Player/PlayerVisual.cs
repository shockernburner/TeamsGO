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

        // Weapon in the right hand. The grip is worked out from the finger bones once, in the rest pose,
        // so it works whatever way the rig's hand bone happens to point.
        private Transform  _hand;
        private Vector3    _gripPos;   // hand-local
        private Quaternion _gripRot;   // hand-local; +Y runs along the handle, out of the thumb side of the fist
        private GameObject _held;
        private HeldLook   _heldLook = HeldLook.None;
        private static Material _wood, _stone, _bone, _cord;

        public float turnSpeed = 720f; // degrees per second

        // First person: the body still casts its shadow (and is there for teammates), but this player's own
        // camera, inside the head, doesn't draw it.
        private bool _shadowOnly;

        public void SetShadowOnly(bool on)
        {
            _shadowOnly = on;
            ApplyShadowMode();
        }

        private void ApplyShadowMode()
        {
            if (_pivot == null) return;
            var mode = _shadowOnly ? UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly
                                   : UnityEngine.Rendering.ShadowCastingMode.On;
            foreach (var r in _pivot.GetComponentsInChildren<Renderer>(true)) r.shadowCastingMode = mode;
        }

        // Set on a teammate's body online, where there is no controller or health here to read.
        private bool _remoteCrouch, _remoteDead, _remote;

        public void SetRemotePose(bool crouched, bool dead)
        {
            _remote = true;
            _remoteCrouch = crouched;
            if (dead == _remoteDead) return;
            _remoteDead = dead;
            if (_animator != null) _animator.SetBool(DeadId, dead);
        }

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
            Placeholder.RemoveRenderers(transform);

            _pivot = new GameObject("Visual").transform;
            _pivot.SetParent(transform, false);
            var model = ModelFit.Spawn(def, _pivot);
            ApplyShadowMode();

            if (def.animator != null)
            {
                _animator = model.GetComponentInChildren<Animator>();
                if (_animator == null) _animator = model.AddComponent<Animator>();
                _animator.runtimeAnimatorController = def.animator;
                _animator.applyRootMotion = false;
            }

            FindGrip(model.transform);

            if (_combat != null) _combat.Attacked += OnAttacked;
            if (_health != null) { _health.Damaged += OnDamaged; _health.Died += OnDied; }
            _lastPos = transform.position;
        }

        private void FindGrip(Transform model)
        {
            // Any humanoid rig names its bones through the avatar; otherwise look them up by the usual names.
            bool human = _animator != null && _animator.isHuman;
            _hand = human ? _animator.GetBoneTransform(HumanBodyBones.RightHand) : Find(model, "hand_r");
            if (_hand == null) return;
            var index = human ? _animator.GetBoneTransform(HumanBodyBones.RightIndexProximal)  : Find(_hand, "index_01_r");
            var pinky = human ? _animator.GetBoneTransform(HumanBodyBones.RightLittleProximal) : Find(_hand, "pinky_01_r");
            var mid   = human ? _animator.GetBoneTransform(HumanBodyBones.RightMiddleProximal) : Find(_hand, "middle_01_r");

            Vector3 axis = index != null && pinky != null ? index.position - pinky.position : _hand.up;
            Vector3 palm = mid != null ? Vector3.Lerp(_hand.position, mid.position, 0.6f) : _hand.position;
            _gripPos = _hand.InverseTransformPoint(palm);
            _gripRot = Quaternion.FromToRotation(Vector3.up, _hand.InverseTransformDirection(axis.normalized));
        }

        private static Transform Find(Transform root, string name)
        {
            if (root.name == name) return root;
            foreach (Transform c in root)
            {
                var t = Find(c, name);
                if (t != null) return t;
            }
            return null;
        }

        private void UpdateHeldWeapon()
        {
            if (_hand == null || _combat == null) return;
            var look = _combat.CurrentWeapon.Look;
            if (look == _heldLook && (_held != null || look == HeldLook.None)) return;

            _heldLook = look;
            if (_held != null) Destroy(_held);
            _held = null;
            if (look == HeldLook.None) return;

            _held = new GameObject("Held_" + look);
            var t = _held.transform;
            t.SetParent(_hand, false);
            t.localPosition = _gripPos;
            t.localRotation = _gripRot;
            Vector3 ls = _hand.lossyScale; // build in metres whatever the rig's import scale
            t.localScale = new Vector3(1f / Mathf.Max(1e-4f, ls.x), 1f / Mathf.Max(1e-4f, ls.y), 1f / Mathf.Max(1e-4f, ls.z));
            BuildWeapon(t, look);
            ApplyShadowMode();
        }

        // Simple shapes in metres, grip at the origin, handle along +Y.
        internal static void BuildWeapon(Transform root, HeldLook look)
        {
            EnsureMaterials();
            switch (look)
            {
                case HeldLook.Spear:
                    Shape(root, PrimitiveType.Cylinder, _wood, new Vector3(0f, 0.2f, 0f), new Vector3(0.045f, 0.95f, 0.045f));
                    Shape(root, PrimitiveType.Cylinder, _cord, new Vector3(0f, 1.1f, 0f), new Vector3(0.055f, 0.04f, 0.055f));
                    var tip = new GameObject("Tip").transform;
                    tip.SetParent(root, false);
                    tip.localPosition = new Vector3(0f, 1.28f, 0f);
                    tip.localScale    = new Vector3(1f, 2.6f, 1f); // stretches the diamond below into a leaf blade
                    var blade = Shape(tip, PrimitiveType.Cube, _stone, Vector3.zero, new Vector3(0.075f, 0.075f, 0.014f));
                    blade.transform.localRotation = Quaternion.Euler(0f, 0f, 45f);
                    break;

                case HeldLook.Club:
                    Shape(root, PrimitiveType.Cylinder, _bone, new Vector3(0f, 0.15f, 0f), new Vector3(0.055f, 0.36f, 0.055f));
                    Shape(root, PrimitiveType.Capsule,  _bone, new Vector3(0f, 0.58f, 0f), new Vector3(0.14f, 0.16f, 0.14f));
                    Shape(root, PrimitiveType.Sphere,   _bone, new Vector3(0f, -0.2f, 0f), new Vector3(0.08f, 0.08f, 0.08f));
                    break;

                case HeldLook.Axe:
                    Shape(root, PrimitiveType.Cylinder, _wood, new Vector3(0f, 0.17f, 0f), new Vector3(0.045f, 0.38f, 0.045f));
                    Shape(root, PrimitiveType.Cube,     _stone, new Vector3(0.09f, 0.48f, 0f), new Vector3(0.18f, 0.13f, 0.035f));
                    Shape(root, PrimitiveType.Cylinder, _cord, new Vector3(0f, 0.48f, 0f), new Vector3(0.06f, 0.07f, 0.06f));
                    break;
            }
        }

        private static GameObject Shape(Transform parent, PrimitiveType type, Material mat, Vector3 pos, Vector3 scale)
        {
            var go = Placeholder.Primitive(type);
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale    = scale;
            go.GetComponent<Renderer>().sharedMaterial = mat;
            return go;
        }

        private static void EnsureMaterials()
        {
            if (_wood != null) return;
            _wood  = Lit(new Color(0.42f, 0.28f, 0.16f));
            _stone = Lit(new Color(0.5f, 0.5f, 0.53f));
            _bone  = Lit(new Color(0.86f, 0.82f, 0.7f));
            _cord  = Lit(new Color(0.25f, 0.18f, 0.12f));
        }

        private static Material Lit(Color c)
        {
            var m = Placeholder.Lit(c);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.15f);
            return m;
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
            UpdateHeldWeapon();
            if (_animator == null) return;
            _animator.SetFloat(SpeedId, speed, 0.1f, Time.deltaTime);
            if (_controller != null) _animator.SetBool(CrouchId, _controller.Stance != Stance.Standing);
            else if (_remote) _animator.SetBool(CrouchId, _remoteCrouch);
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
