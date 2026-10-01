using UnityEngine;
using UnityEngine.InputSystem;
using ProjectFossil.Core;

namespace ProjectFossil.Player
{
    // Puts the camera in the player's eyes. Your own body still casts its shadow but isn't drawn for you; what you
    // see of yourself is a pair of arms and the weapon in your hand, which sway with the view, bob as you walk and
    // swing when you attack. V (or d-pad down) switches to the over-the-shoulder view and back. When you die the
    // camera pulls out so you can see what got you.
    //
    // Presentation only. PlayerController adds it next to the CameraRig.
    [DisallowMultipleComponent]
    public class FirstPersonView : MonoBehaviour
    {
        private const string PrefKey = "ProjectFossil.FirstPerson";

        public Vector3 eyeOffset = new Vector3(0f, 0.06f, 0.14f); // from the CameraTarget (the head pivot)
        public float   nearClip  = 0.05f;
        public float   fieldOfView = 70f;

        public bool IsFirstPerson { get; private set; }

        private Camera           _cam;
        private Transform        _head;
        private CameraRig        _rig;
        private PlayerController _controller;
        private PlayerCombat     _combat;
        private PlayerVisual     _visual;
        private Vector3          _thirdLocal;
        private Quaternion       _thirdRot;
        private float            _thirdNear, _thirdFov;
        private bool             _wanted = true;

        // Viewmodel
        private Transform _arms, _right, _left, _held;
        private HeldLook  _heldLook = HeldLook.None;
        private bool      _builtHeld;
        private Quaternion _lastView;
        private Vector2   _sway;
        private float     _bob;
        private float     _swing = -1f;   // 0..1 through an attack, < 0 when idle
        private bool      _armedSwing;
        private float     _flinch;
        private static Material _skin, _sleeve;

        public void Init(Camera cam, Transform head, CameraRig rig)
        {
            _cam  = cam;
            _head = head;
            _rig  = rig;
            _thirdLocal = cam.transform.localPosition;
            _thirdRot   = cam.transform.localRotation;
            _thirdNear  = cam.nearClipPlane;
            _thirdFov   = cam.fieldOfView;
            _wanted = PlayerPrefs.GetInt(PrefKey, 1) == 1;
        }

        private void Awake()
        {
            _controller = GetComponent<PlayerController>();
            _combat     = GetComponent<PlayerCombat>();
        }

        private void OnEnable()
        {
            if (_combat == null) _combat = GetComponent<PlayerCombat>();
            if (_combat != null) _combat.Attacked += OnAttacked;
        }

        private void OnDisable()
        {
            if (_combat != null) _combat.Attacked -= OnAttacked;
            Apply(false);
        }

        public void OnView(InputValue v)
        {
            if (!v.isPressed || (_controller != null && _controller.InputBlocked)) return;
            _wanted = !_wanted;
            PlayerPrefs.SetInt(PrefKey, _wanted ? 1 : 0);
        }

        private void LateUpdate()
        {
            if (_cam == null) return;
            if (_visual == null) _visual = GetComponent<PlayerVisual>();

            bool alive = _controller == null || _controller.Health == null || _controller.Health.IsAlive;
            bool fp = _wanted && alive && _controller != null && _controller.enabled;
            if (fp != IsFirstPerson || (_visual != null && !_visualApplied)) Apply(fp);
            if (!IsFirstPerson) return;

            _cam.transform.localPosition = eyeOffset;
            _cam.transform.localRotation = Quaternion.identity;
            AnimateArms();
        }

        private bool _visualApplied;

        private void Apply(bool fp)
        {
            IsFirstPerson = fp;
            if (_visual != null) { _visual.SetShadowOnly(fp); _visualApplied = true; }
            if (_rig != null) _rig.FirstPerson = fp;
            if (_combat != null) _combat.FirstPerson = fp;
            if (_cam != null)
            {
                _cam.nearClipPlane = fp ? nearClip : _thirdNear;
                _cam.fieldOfView   = fp ? fieldOfView : _thirdFov;
                if (!fp)
                {
                    _cam.transform.localPosition = _thirdLocal;
                    _cam.transform.localRotation = _thirdRot;
                }
            }
            if (fp && _arms == null) BuildArms();
            if (_arms != null) _arms.gameObject.SetActive(fp);
            if (_cam != null) _lastView = _cam.transform.rotation;
        }

        // ── Arms ───────────────────────────────────────────────────────────────

        private void BuildArms()
        {
            EnsureMaterials();
            _arms = new GameObject("FirstPersonArms").transform;
            _arms.SetParent(_cam.transform, false);
            _right = Arm("Right", new Vector3( 0.2f, -0.2f, 0.32f), 1f);
            _left  = Arm("Left",  new Vector3(-0.22f, -0.24f, 0.28f), -1f);
        }

        // A sleeved forearm reaching forward from below the view, ending in a fist. The shoulder end is the pivot.
        private Transform Arm(string side, Vector3 pos, float mirror)
        {
            var root = new GameObject(side).transform;
            root.SetParent(_arms, false);
            root.localPosition = pos;
            root.localRotation = Quaternion.Euler(-8f, -10f * mirror, 0f);

            Part(root, PrimitiveType.Capsule, _sleeve, new Vector3(0f, 0f, -0.12f), new Vector3(0.085f, 0.13f, 0.085f), new Vector3(90f, 0f, 0f));
            Part(root, PrimitiveType.Capsule, _skin,   new Vector3(0f, 0f,  0.06f), new Vector3(0.065f, 0.1f, 0.065f), new Vector3(90f, 0f, 0f));
            Part(root, PrimitiveType.Sphere,  _skin,   new Vector3(0f, 0.005f, 0.16f), new Vector3(0.085f, 0.075f, 0.095f), Vector3.zero);
            Part(root, PrimitiveType.Capsule, _skin,   new Vector3(0.035f * mirror, 0.03f, 0.15f), new Vector3(0.03f, 0.035f, 0.03f), new Vector3(0f, 0f, 60f * mirror));
            return root;
        }

        private static GameObject Part(Transform parent, PrimitiveType type, Material mat, Vector3 pos, Vector3 scale, Vector3 euler)
        {
            var go = GameObject.CreatePrimitive(type);
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = Quaternion.Euler(euler);
            go.transform.localScale    = scale;
            var r = go.GetComponent<Renderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; // the body already casts one
            return go;
        }

        private void UpdateHeld()
        {
            var look = _combat != null ? _combat.CurrentWeapon.Look : HeldLook.None;
            if (_builtHeld && look == _heldLook) return;
            _builtHeld = true;
            _heldLook = look;
            if (_held != null) Destroy(_held.gameObject);
            _held = null;
            if (look == HeldLook.None) return;

            // In the right fist, handle running up through it and leaning forward, the head out ahead.
            _held = new GameObject("Held_" + look).transform;
            _held.SetParent(_right, false);
            _held.localPosition = new Vector3(0f, 0f, 0.16f);
            _held.localRotation = Quaternion.Euler(look == HeldLook.Spear ? 80f : 35f, 0f, 0f);
            if (look == HeldLook.Spear) _held.localPosition += new Vector3(0f, 0f, -0.25f); // grip it further back
            PlayerVisual.BuildWeapon(_held, look);
            foreach (var r in _held.GetComponentsInChildren<Renderer>())
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        private void AnimateArms()
        {
            if (_arms == null) return;
            UpdateHeld();
            float dt = Time.deltaTime;

            // Sway: the arms lag a little behind where you look.
            Quaternion view = _cam.transform.rotation;
            Quaternion delta = Quaternion.Inverse(_lastView) * view;
            _lastView = view;
            Vector3 e = delta.eulerAngles;
            float yaw   = Mathf.DeltaAngle(0f, e.y);
            float pitch = Mathf.DeltaAngle(0f, e.x);
            _sway = Vector2.Lerp(_sway, new Vector2(Mathf.Clamp(-yaw, -6f, 6f), Mathf.Clamp(-pitch, -6f, 6f)), 10f * dt);

            // Bob with the stride; bigger running, small crouched.
            bool moving = _controller != null && _controller.IsMoving && _controller.IsGrounded;
            float rate = _controller != null && _controller.IsSprinting ? 13f : 8.5f;
            if (_controller != null && _controller.Stance != Stance.Standing) rate *= 0.6f;
            _bob = moving ? _bob + dt * rate : Mathf.MoveTowards(_bob, Mathf.Round(_bob / Mathf.PI) * Mathf.PI, dt * 4f);
            float amp = _controller != null && _controller.IsSprinting ? 0.022f : 0.011f;
            Vector3 bob = new Vector3(Mathf.Cos(_bob) * amp, -Mathf.Abs(Mathf.Sin(_bob)) * amp, 0f);

            // Lowered while running flat out, and pressed to the ground while down.
            bool down = _controller != null && _controller.IsDown;
            float lower = down ? 0.12f : _controller != null && _controller.IsSprinting ? 0.05f : 0f;

            _flinch = Mathf.MoveTowards(_flinch, 0f, dt * 3f);
            _arms.localPosition = bob + new Vector3(0f, -lower - _flinch * 0.04f, -_flinch * 0.05f);
            _arms.localRotation = Quaternion.Euler(_sway.y + (_controller != null && _controller.IsSprinting ? 12f : 0f),
                                                   _sway.x, _sway.x * 0.5f);

            // Attack: armed, a downward diagonal chop from the right; bare hands, a straight jab.
            Quaternion swingRot = Quaternion.identity;
            Vector3    swingPos = Vector3.zero;
            if (_swing >= 0f)
            {
                _swing += dt / 0.38f;
                float t = Mathf.Clamp01(_swing);
                // Quick wind-up (0-0.25), fast strike (0.25-0.5), slower recovery.
                float k = t < 0.25f ? -Mathf.SmoothStep(0f, 1f, t / 0.25f)
                        : t < 0.5f ? Mathf.Lerp(-1f, 1f, (t - 0.25f) / 0.25f)
                        : Mathf.SmoothStep(1f, 0f, (t - 0.5f) / 0.5f);
                if (_armedSwing)
                {
                    swingRot = Quaternion.Euler(k * 55f, -k * 25f, -k * 30f);
                    swingPos = new Vector3(-k * 0.08f, k * 0.03f, Mathf.Max(0f, k) * 0.12f);
                }
                else
                {
                    swingPos = new Vector3(-Mathf.Max(0f, k) * 0.06f, Mathf.Max(0f, k) * 0.05f, k * 0.22f);
                }
                if (_swing >= 1f) _swing = -1f;
            }
            _right.localPosition = new Vector3(0.2f, -0.2f, 0.32f) + swingPos;
            _right.localRotation = Quaternion.Euler(-8f, -10f, 0f) * swingRot;
        }

        private void OnAttacked(WeaponStats weapon, Health target)
        {
            _swing = 0f;
            _armedSwing = weapon.Look != HeldLook.None;
        }

        // Called by the player's health (wired below) so a bite jolts the view model.
        private Health _health;
        private void Start()
        {
            _health = GetComponent<Health>();
            if (_health != null) _health.Damaged += OnDamaged;
        }

        private void OnDestroy()
        {
            if (_health != null) _health.Damaged -= OnDamaged;
        }

        private void OnDamaged(DamageInfo info) => _flinch = 1f;

        private static void EnsureMaterials()
        {
            if (_skin != null) return;
            _skin   = Lit(new Color(0.78f, 0.58f, 0.45f));
            _sleeve = Lit(new Color(0.33f, 0.36f, 0.24f));
        }

        private static Material Lit(Color c)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            var m = new Material(shader != null ? shader : Shader.Find("Standard"));
            m.color = c;
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.2f);
            return m;
        }

        // A small dot in the middle of the screen, so you know where a swing goes.
        private void OnGUI()
        {
            if (!IsFirstPerson || (_controller != null && _controller.InputBlocked)) return;
            float cx = Screen.width * 0.5f, cy = Screen.height * 0.5f;
            GUI.color = new Color(0f, 0f, 0f, 0.5f);
            GUI.DrawTexture(new Rect(cx - 3f, cy - 3f, 6f, 6f), Texture2D.whiteTexture);
            GUI.color = new Color(1f, 1f, 1f, 0.85f);
            GUI.DrawTexture(new Rect(cx - 2f, cy - 2f, 4f, 4f), Texture2D.whiteTexture);
            GUI.color = Color.white;
        }
    }
}
