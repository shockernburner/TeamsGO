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
        public float   fieldOfView = 80f;

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
        private static Material _glove, _cuff, _sleeve;
        private Material _handMat, _sleeveMat;

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

        // Where each wrist rests in front of the eyes, low in the corners of the view.
        private static readonly Vector3    RightRest    = new Vector3( 0.19f, -0.24f, 0.36f);
        private static readonly Vector3    LeftRest     = new Vector3(-0.21f, -0.27f, 0.32f);
        private static readonly Quaternion RightRestRot = Quaternion.Euler(-22f, -14f, -8f);
        private static readonly Quaternion LeftRestRot  = Quaternion.Euler(-22f,  16f,  8f);

        private void BuildArms()
        {
            EnsureMaterials();
            // Bare hands and the shirt sleeve of whoever the survivor model is, when its materials say which is which.
            var (skin, shirt) = _visual != null ? _visual.OutfitMaterials() : (null, null);
            _handMat   = skin  != null ? skin  : _glove;
            _sleeveMat = shirt != null ? shirt : _sleeve;
            _arms = new GameObject("FirstPersonArms").transform;
            _arms.SetParent(_cam.transform, false);
            _right = Arm("Right", RightRest, RightRestRot, 1f);
            _left  = Arm("Left",  LeftRest,  LeftRestRot, -1f);
        }

        // A gloved fist at the wrist (the pivot), with the sleeved forearm running back and down out of view.
        private Transform Arm(string side, Vector3 pos, Quaternion rot, float mirror)
        {
            var root = new GameObject(side).transform;
            root.SetParent(_arms, false);
            root.localPosition = pos;
            root.localRotation = rot;

            // Forearm in the outfit's sleeve, a leather cuff at the wrist.
            Part(root, PrimitiveType.Capsule,  _sleeveMat, new Vector3(0f, -0.005f, -0.2f), new Vector3(0.078f, 0.17f, 0.07f), new Vector3(90f, 0f, 0f));
            Part(root, PrimitiveType.Cylinder, _cuff,   new Vector3(0f, 0f, -0.035f),    new Vector3(0.07f, 0.03f, 0.064f), new Vector3(90f, 0f, 0f));
            // Gloved fist: back of the hand, curled fingers, knuckles and thumb.
            Part(root, PrimitiveType.Cube,     _handMat, new Vector3(0f, 0.004f, 0.03f),  new Vector3(0.072f, 0.04f, 0.075f), Vector3.zero);
            Part(root, PrimitiveType.Cube,     _handMat, new Vector3(0f, -0.018f, 0.068f), new Vector3(0.07f, 0.045f, 0.03f), new Vector3(18f, 0f, 0f));
            Part(root, PrimitiveType.Capsule,  _handMat, new Vector3(0f, 0.008f, 0.07f),  new Vector3(0.026f, 0.036f, 0.026f), new Vector3(0f, 0f, 90f));
            Part(root, PrimitiveType.Capsule,  _handMat, new Vector3(-0.036f * mirror, -0.006f, 0.045f), new Vector3(0.022f, 0.028f, 0.022f), new Vector3(70f, 25f * mirror, 0f));
            return root;
        }

        private static GameObject Part(Transform parent, PrimitiveType type, Material mat, Vector3 pos, Vector3 scale, Vector3 euler)
        {
            var go = Placeholder.Primitive(type);
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
            _held.localPosition = new Vector3(0f, -0.01f, 0.05f);
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
            _right.localPosition = RightRest + swingPos;
            _right.localRotation = RightRestRot * swingRot;
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

        // Where the last bite came from, so you can turn and fight what you can't see.
        private Vector3 _hitFrom;
        private float   _hitTimer;
        private const float HitShowSeconds = 1.4f;

        private void OnDamaged(DamageInfo info)
        {
            _flinch = 1f;
            Vector3 from = info.Source != null ? info.Source.transform.position : info.Point;
            Vector3 d = from - transform.position;
            d.y = 0f;
            if (d.sqrMagnitude < 0.01f) return; // no direction (bleeding out, a fall)
            _hitFrom  = from;
            _hitTimer = HitShowSeconds;
        }

        private static void EnsureMaterials()
        {
            if (_glove != null) return;
            _glove  = Lit(new Color(0.24f, 0.17f, 0.11f));
            _cuff   = Lit(new Color(0.14f, 0.1f, 0.07f));
            _sleeve = Lit(new Color(0.25f, 0.28f, 0.18f));
        }

        private static Material Lit(Color c)
        {
            var m = Placeholder.Lit(c);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.2f);
            return m;
        }

        private static Texture2D _wedge;
        private float _fps, _fpsTimer;
        private int   _fpsFrames;

        private void Update()
        {
            if (_hitTimer > 0f) _hitTimer -= Time.unscaledDeltaTime;
            _fpsFrames++;
            _fpsTimer += Time.unscaledDeltaTime;
            if (_fpsTimer >= 0.5f) { _fps = _fpsFrames / _fpsTimer; _fpsFrames = 0; _fpsTimer = 0f; }
        }

        private void OnGUI()
        {
            DrawFrameRate();
            if (_controller != null && _controller.InputBlocked) return;
            DrawHitDirection();
            if (!IsFirstPerson) return;
            DrawDot();
        }

        // Development builds and the Editor: frames per second, top right, to spot slowdowns in playtests.
        private void DrawFrameRate()
        {
            if (!Application.isEditor && !Debug.isDebugBuild) return;
            GUI.color = _fps >= 45f ? new Color(0.6f, 1f, 0.6f, 0.8f) : _fps >= 25f ? new Color(1f, 0.9f, 0.4f, 0.9f) : new Color(1f, 0.4f, 0.35f, 0.95f);
            GUI.Label(new Rect(Screen.width - 70f, Screen.height - 24f, 66f, 20f), $"{Mathf.RoundToInt(_fps)} fps");
            GUI.color = Color.white;
        }

        // A red wedge around the middle of the screen, pointing toward whatever just bit you.
        private void DrawHitDirection()
        {
            if (_hitTimer <= 0f || _cam == null) return;
            Vector3 local = _cam.transform.InverseTransformDirection(_hitFrom - _cam.transform.position);
            float angle = Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg; // 0 = ahead, 90 = right, 180 = behind
            if (_wedge == null) _wedge = MakeWedge();

            var centre = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            float radius = Mathf.Min(Screen.width, Screen.height) * 0.22f;
            var rect = new Rect(centre.x - 60f, centre.y - radius - 22f, 120f, 30f);
            var saved = GUI.matrix;
            GUIUtility.RotateAroundPivot(angle, centre);
            GUI.color = new Color(1f, 0.15f, 0.1f, Mathf.Clamp01(_hitTimer / HitShowSeconds) * 0.9f);
            GUI.DrawTexture(rect, _wedge);
            GUI.color = Color.white;
            GUI.matrix = saved;
        }

        // A curved band, thickest in the middle, fading at the ends.
        private static Texture2D MakeWedge()
        {
            const int w = 64, h = 16;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (int x = 0; x < w; x++)
            for (int y = 0; y < h; y++)
            {
                float u = x / (w - 1f) * 2f - 1f;                // -1..1 across
                float arc = (1f - u * u) * 0.5f;                  // band bows outward in the middle
                float v = y / (h - 1f);                           // 0 bottom .. 1 top
                float thick = 0.45f * (1f - Mathf.Abs(u));        // thinner toward the ends
                float centreLine = 0.3f + arc;
                float a = Mathf.Clamp01(1f - Mathf.Abs(v - centreLine) / Mathf.Max(0.05f, thick));
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a * (1f - Mathf.Abs(u) * 0.6f)));
            }
            tex.Apply();
            return tex;
        }

        // A small dot in the middle of the screen, so you know where a swing goes.
        private void DrawDot()
        {
            float cx = Screen.width * 0.5f, cy = Screen.height * 0.5f;
            GUI.color = new Color(0f, 0f, 0f, 0.5f);
            GUI.DrawTexture(new Rect(cx - 3f, cy - 3f, 6f, 6f), Texture2D.whiteTexture);
            GUI.color = new Color(1f, 1f, 1f, 0.85f);
            GUI.DrawTexture(new Rect(cx - 2f, cy - 2f, 4f, 4f), Texture2D.whiteTexture);
            GUI.color = Color.white;
        }
    }
}
