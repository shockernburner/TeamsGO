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
        private static readonly int SwimId     = Animator.StringToHash("Swim");

        public const string UnarmedName = "Bare hands";

        private PlayerController _controller;
        private PlayerCombat     _combat;
        private Health           _health;
        private Animator         _animator;
        private Vector3          _lastPos;
        private Transform        _pivot;      // turns the model toward where it is moving
        private float            _yaw;        // current turn of the pivot, degrees
        private float            _faceForwardTimer;
        private Transform        _model;
        private int              _facingChecks;  // frames until the animated body's facing is checked
        private bool             _hasSwim, _dead;
        private int              _baseState;   // base layer state last frame
        private int              _facingAgree;   // settled frames in a row that read the same wrong facing
        private int              _facingTries;   // settled frames read since the last state change
        private float            _facingOff;     // the wrong facing those frames agreed on, degrees
        private float            _swimLift;      // metres the body is raised while swimming

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
            _dead = dead;
            if (_animator != null) _animator.SetBool(DeadId, dead);
        }

        // Adds the visual to a spawned player. No-op when the definition has no model.
        public static PlayerVisual Attach(GameObject player, ModelDefinition def)
        {
            if (player == null || def == null || !def.HasModel) return null;
            var v = player.GetComponent<PlayerVisual>();
            if (v == null) v = player.AddComponent<PlayerVisual>();
            v.Build(def);
            PlantPusher.Ensure(player, 0.6f);
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
            _model = model.transform;
            ApplyShadowMode();

            if (def.animator != null)
            {
                _animator = model.GetComponentInChildren<Animator>();
                if (_animator == null) _animator = model.AddComponent<Animator>();
                _animator.runtimeAnimatorController = def.animator;
                _animator.applyRootMotion = false;
                foreach (var p in _animator.parameters) if (p.nameHash == SwimId) _hasSwim = true;
                _facingChecks = 3;
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

                case HeldLook.Club: // the hatchet
                    var hatchet = BoughtArt.Current != null ? BoughtArt.Current.hatchet : null;
                    if (hatchet != null && BoughtWeapon(root, hatchet, 0.45f)) break;
                    Shape(root, PrimitiveType.Cylinder, _wood, new Vector3(0f, 0.13f, 0f), new Vector3(0.04f, 0.28f, 0.04f));
                    Shape(root, PrimitiveType.Cube,     _stone, new Vector3(0.06f, 0.36f, 0f), new Vector3(0.12f, 0.09f, 0.03f));
                    Shape(root, PrimitiveType.Cylinder, _cord, new Vector3(0f, 0.36f, 0f), new Vector3(0.05f, 0.05f, 0.05f));
                    break;

                case HeldLook.Axe:
                    var axe = BoughtArt.Current != null ? BoughtArt.Current.axe : null;
                    if (axe != null && BoughtWeapon(root, axe, 0.62f)) break;
                    Shape(root, PrimitiveType.Cylinder, _wood, new Vector3(0f, 0.17f, 0f), new Vector3(0.045f, 0.38f, 0.045f));
                    Shape(root, PrimitiveType.Cube,     _stone, new Vector3(0.09f, 0.48f, 0f), new Vector3(0.18f, 0.13f, 0.035f));
                    Shape(root, PrimitiveType.Cylinder, _cord, new Vector3(0f, 0.48f, 0f), new Vector3(0.06f, 0.07f, 0.06f));
                    break;
            }
        }

        // A bought weapon model in the hand: its longest side along the handle (+Y), scaled to `length` metres,
        // with the grip a little above the bottom end. Models are expected head up; the pack is logged by setup.
        private static bool BoughtWeapon(Transform root, GameObject prefab, float length)
        {
            var go = Instantiate(prefab, root, false);
            go.name = prefab.name;
            foreach (var c in go.GetComponentsInChildren<Collider>(true)) Destroy(c);
            var renderers = go.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) { Destroy(go); return false; }

            Bounds Local()
            {
                var b = new Bounds(root.InverseTransformPoint(renderers[0].bounds.center), Vector3.zero);
                foreach (var r in renderers)
                {
                    var wb = r.bounds;
                    for (int i = 0; i < 8; i++)
                        b.Encapsulate(root.InverseTransformPoint(wb.center + Vector3.Scale(wb.extents,
                            new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1))));
                }
                return b;
            }

            var lb = Local();
            Vector3 s = lb.size;
            if (s.x > s.y && s.x >= s.z) go.transform.localRotation = Quaternion.Euler(0f, 0f, 90f) * go.transform.localRotation;
            else if (s.z > s.y && s.z > s.x) go.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f) * go.transform.localRotation;
            lb = Local();
            go.transform.localScale *= length / Mathf.Max(0.01f, lb.size.y);
            lb = Local();
            go.transform.localPosition -= new Vector3(lb.center.x, lb.min.y + length * 0.12f, lb.center.z);
            return true;
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
            if (_hasSwim && _controller != null) _animator.SetBool(SwimId, _controller.IsSwimming);

            // Crouching and swimming use clips from another library than walking, and those may face the body the
            // other way. Check the facing again whenever the base state changes, once its blend has settled.
            int state = _animator.GetCurrentAnimatorStateInfo(0).shortNameHash;
            if (state != _baseState)
            {
                if (_baseState != 0 && !_dead) { _facingChecks = 2; _facingAgree = 0; _facingTries = 0; }
                _baseState = state;
            }
            UpdateActionLayer();
        }

        // The upper-body layer (punch, swing, flinch) only counts while one of those plays. Left at full weight
        // between them, its empty state held the arms in the last punch's pose, so the survivor walked and ran
        // with fists up. It fades out once the action is over.
        private const float ActionFadeOut = 6f; // weight per second
        private void UpdateActionLayer()
        {
            if (_animator.layerCount < 2) return;
            bool acting = _animator.IsInTransition(1) || !_animator.GetCurrentAnimatorStateInfo(1).IsName("Empty");
            float w = _animator.GetLayerWeight(1);
            w = acting ? 1f : Mathf.MoveTowards(w, 0f, ActionFadeOut * Time.deltaTime);
            _animator.SetLayerWeight(1, w);
        }

        // A humanoid animation turns the body to face its Animator's forward, which isn't always the way the
        // character pack's prefab faces (the Survivalist ran backwards). After the first animated frames, measure
        // where the chest points from the shoulders and turn the model so it faces the pivot's forward.
        private const int FacingFrames = 6, FacingMaxTries = 60;

        private void LateUpdate()
        {
            UpdateSwimLift();
            CheckFacing();
        }

        // The swim clips keep the body where their library animated it: hanging about 1.3 m below the feet, for a
        // root at the water line. The controller floats the feet swimDepth under the surface, so the whole body,
        // head included, was under water. Raise the body until the chest is just under the surface (treading water
        // that leaves head and shoulders out; in the crawl, the back at the water line), measured from the animated
        // chest so it holds whatever the clips do, and eased so the stroke's bob isn't followed.
        public const float ChestUnderWater = 0.12f;

        // How far to raise a swimmer whose chest (with no lift) is at unliftedChest.
        public static float SwimLiftFor(float surface, float unliftedChest) =>
            Mathf.Clamp(surface - ChestUnderWater - unliftedChest, 0f, 2.5f);

        // The turn that corrects a body reading `off` degrees from the pivot's forward: a quarter or half turn,
        // or none for a reading near forward or between quarters (that is the pose, not the facing).
        public static bool FacingTurn(float off, out float turn)
        {
            turn = Mathf.Round(off / 90f) * 90f;
            return Mathf.Abs(off) >= 30f && Mathf.Abs(Mathf.DeltaAngle(off, turn)) <= 20f;
        }

        // A facing correction turns the model about the capsule's axis, never away from it.
        public static Vector3 TurnedOffset(Vector3 local, float turn) => Quaternion.Euler(0f, -turn, 0f) * local;
        private const float SwimLiftSpeed = 2.5f; // metres per second
        private void UpdateSwimLift()
        {
            if (_pivot == null) return;
            float target = 0f;
            var chest = _animator != null && _animator.isHuman ? _animator.GetBoneTransform(HumanBodyBones.Chest) : null;
            if (_controller != null && _controller.IsSwimming && chest != null && !_dead)
            {
                target = SwimLiftFor(IslandWorld.SurfaceOver(transform.position), chest.position.y - _swimLift);
            }
            _swimLift = Mathf.MoveTowards(_swimLift, target, SwimLiftSpeed * Time.deltaTime);
            _pivot.localPosition = new Vector3(0f, _swimLift, 0f);
        }

        private void CheckFacing()
        {
            if (_facingChecks <= 0 || _animator == null || !_animator.isHuman || _model == null) return;
            if (--_facingChecks > 0) return;
            if (_animator.IsInTransition(0)) { _facingChecks = 1; return; } // wait until the new pose has settled
            var l = _animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
            var r = _animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
            if (l == null || r == null) return;
            Vector3 right = _pivot.InverseTransformDirection(r.position - l.position);
            right.y = 0f;
            if (right.sqrMagnitude < 1e-6f) return;
            Vector3 fwd = Vector3.Cross(right, Vector3.up);
            float off = Mathf.Atan2(fwd.x, fwd.z) * Mathf.Rad2Deg;
            // A clip from another library is off by a quarter or a half turn. A reading in between (49 degrees, from
            // a stroke or a twist) is the pose, not the facing: turning 90 for it swam the body sideways.
            if (!FacingTurn(off, out _)) { _facingAgree = 0; return; }
            // One frame of a stride or a twist can read the shoulders far off (a 167 degree reading spun the
            // runner backwards). Turn only when several settled frames in a row agree.
            if (_facingAgree > 0 && Mathf.Abs(Mathf.DeltaAngle(off, _facingOff)) > 20f) _facingAgree = 0;
            _facingOff = off;
            if (++_facingAgree < FacingFrames)
            {
                if (++_facingTries < FacingMaxTries) _facingChecks = 1;
                else _facingAgree = 0; // never settled: leave the model as it is
                return;
            }
            _facingAgree = 0;
            FacingTurn(off, out float turn);
            // Turn about the capsule's own axis. Turning about the hips moved the model off the capsule whenever the
            // hips were away from it (a metre out in the swim stroke): the body then sank into hillsides, hung past
            // ledges, and swung round the player at every A, S or D as the pivot turned toward the step.
            _model.localPosition = TurnedOffset(_model.localPosition, turn);
            _model.localRotation = Quaternion.Euler(0f, -turn, 0f) * _model.localRotation;
            Debug.Log($"[PlayerVisual] {_model.name} faced {off:0} degrees off; turned it {-turn:0}.");
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
            _dead = true;
            if (_animator != null) _animator.SetBool(DeadId, true);
        }
    }
}
