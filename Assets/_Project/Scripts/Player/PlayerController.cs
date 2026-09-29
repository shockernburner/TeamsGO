using UnityEngine;
using UnityEngine.InputSystem;
using ProjectFossil.Core;

namespace ProjectFossil.Player
{
    public enum Stance { Standing, Crouching, Prone }

    [RequireComponent(typeof(CharacterController))]
    public class PlayerController : MonoBehaviour, IStealthProfile
    {
        [Header("Movement")]
        public float walkSpeed      = 5f;
        public float runSpeed       = 9f;
        public float crouchSpeed    = 2.5f;
        public float crawlSpeed     = 1.2f;
        public float exhaustedSpeed = 3f;   // walking while out of breath
        public float gravity        = -20f;
        public float jumpHeight     = 1.5f;

        [Header("Stamina")]
        public float maxStamina        = 100f;
        public float staminaDrainRate  = 10f;   // per second while running
        public float staminaRegenRate  = 10f;   // per second while walking (faster when still)
        public float staminaRegenDelay = 1.2f;  // seconds after running/jumping before regen starts
        public float jumpStaminaCost   = 10f;
        [Range(0f, 1f)]
        public float exhaustionRecoverFraction = 0.3f;

        [Header("Stance heights (CharacterController)")]
        public float standHeight  = 1.8f;
        public float crouchHeight = 1.2f;
        public float proneHeight  = 0.6f;

        [Header("Stealth (1 = normal)")]
        public float runNoise    = 1.5f;
        public float crouchNoise = 0.5f;
        public float proneNoise  = 0.25f;
        public float crouchVisibility = 0.75f;
        public float proneVisibility  = 0.5f;

        [Header("Camera")]
        public Transform cameraTarget;
        public float mouseSensitivity = 0.15f;
        public float verticalClampMin = -80f;
        public float verticalClampMax = 75f;

        [Header("Water")]
        [Tooltip("How deep into the sea the player can wade before the seabed stops them")]
        public float maxWadeDepth = 1.1f;

        // ── State ─────────────────────────────────────────────────────────────
        public StaminaModel StaminaModel { get; private set; }
        public float  Stamina     => StaminaModel.Current;
        public bool   IsExhausted => StaminaModel.IsExhausted;
        public bool   RunToggled  { get; private set; }
        public bool   IsSprinting { get; private set; }
        public bool   IsMoving    { get; private set; }
        public Stance Stance      { get; private set; } = Stance.Standing;
        public bool   IsCrouching => Stance == Stance.Crouching;
        public bool   IsProne     => Stance == Stance.Prone;
        public bool   IsGrounded  { get; private set; }

        public float NoiseMultiplier =>
            Stance == Stance.Prone ? proneNoise :
            Stance == Stance.Crouching ? crouchNoise :
            IsSprinting ? runNoise : 1f;

        public float VisibilityMultiplier =>
            Stance == Stance.Prone ? proneVisibility :
            Stance == Stance.Crouching ? crouchVisibility : 1f;

        // Set by UI (shop, results screen) to freeze movement/look and free the cursor.
        public bool InputBlocked
        {
            get => _inputBlocked;
            set
            {
                _inputBlocked    = value;
                Cursor.lockState = value ? CursorLockMode.None : CursorLockMode.Locked;
                Cursor.visible   = value;
            }
        }

        public Health Health { get; private set; }

        private CharacterController _cc;
        private Transform _body;
        private Vector2 _moveInput;
        private Vector2 _lookInput;
        private bool    _jumpPressed;
        private float   _verticalVelocity;
        private float   _pitch;
        private bool    _inputBlocked;
        private float   _cameraStandY;
        private Vector3 _bodyStandPos;
        private Vector3 _bodyStandScale;

        private void Awake()
        {
            _cc  = GetComponent<CharacterController>();
            _body = transform.Find("Body");
            if (_body != null)
            {
                _bodyStandPos   = _body.localPosition;
                _bodyStandScale = _body.localScale;
            }
            if (cameraTarget != null)
            {
                _cameraStandY = cameraTarget.localPosition.y;
                var cam = cameraTarget.GetComponentInChildren<Camera>();
                if (cam != null)
                {
                    var rig = cam.GetComponent<CameraRig>();
                    if (rig == null) rig = cam.gameObject.AddComponent<CameraRig>();
                    rig.Init(cameraTarget, transform);
                }
            }

            StaminaModel = new StaminaModel(maxStamina)
            {
                DrainPerSecond  = staminaDrainRate,
                RegenPerSecond  = staminaRegenRate,
                RegenDelay      = staminaRegenDelay,
                RecoverFraction = exhaustionRecoverFraction,
            };

            Health = GetComponent<Health>();
            if (Health == null) Health = gameObject.AddComponent<Health>();
            Health.Died += OnDied;
        }

        private void OnDestroy()
        {
            if (Health != null) Health.Died -= OnDied;
        }

        private void OnDied(DamageInfo info)
        {
            // Stop all control; the match flow decides what happens next.
            _moveInput = Vector2.zero;
            _lookInput = Vector2.zero;
            enabled    = false;
        }

        private void OnEnable()
        {
            InputBlocked = _inputBlocked;
        }

        private void OnDisable()
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible   = true;
        }

        // ── Input callbacks (PlayerInput "Send Messages") ─────────────────────
        // Button actions only report presses in this mode, so run/crouch/crawl are toggles.

        public void OnMove(InputValue v) => _moveInput = v.Get<Vector2>();
        public void OnLook(InputValue v) => _lookInput = v.Get<Vector2>();

        public void OnJump(InputValue v)
        {
            if (!v.isPressed || _inputBlocked) return;
            if (Stance != Stance.Standing) SetStance(Stance.Standing); // jump key also stands you up
            else _jumpPressed = true;
        }

        public void OnSprint(InputValue v)
        {
            if (!v.isPressed || _inputBlocked) return;
            RunToggled = !RunToggled;
            if (RunToggled && Stance != Stance.Standing) SetStance(Stance.Standing);
        }

        public void OnCrouch(InputValue v)
        {
            if (!v.isPressed || _inputBlocked) return;
            SetStance(Stance == Stance.Crouching ? Stance.Standing : Stance.Crouching);
        }

        public void OnProne(InputValue v)
        {
            if (!v.isPressed || _inputBlocked) return;
            SetStance(Stance == Stance.Prone ? Stance.Standing : Stance.Prone);
        }

        // ── Update ────────────────────────────────────────────────────────────

        private void Update()
        {
            IsGrounded = _cc.isGrounded;

            // The Editor (or Alt-Tab) can drop the cursor lock; a click in the game takes it back.
            if (!_inputBlocked && Cursor.lockState != CursorLockMode.Locked && Application.isFocused &&
                Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible   = false;
            }

            if (_inputBlocked)
            {
                _moveInput   = Vector2.zero;
                _lookInput   = Vector2.zero;
                _jumpPressed = false;
            }

            HandleLook();
            HandleMovement();
            UpdateCameraHeight();
        }

        private void HandleLook()
        {
            transform.Rotate(Vector3.up, _lookInput.x * mouseSensitivity, Space.World);

            _pitch = Mathf.Clamp(_pitch - _lookInput.y * mouseSensitivity, verticalClampMin, verticalClampMax);
            if (cameraTarget != null)
                cameraTarget.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
        }

        private void HandleMovement()
        {
            IsMoving = _moveInput.sqrMagnitude > 0.01f;

            // Running needs the toggle, standing, movement and breath. Exhaustion cancels the toggle.
            if (StaminaModel.IsExhausted) RunToggled = false;
            IsSprinting = RunToggled && Stance == Stance.Standing && IsMoving && StaminaModel.CanRun;
            StaminaModel.Tick(Time.deltaTime, IsSprinting, IsMoving);

            // Vertical velocity
            if (IsGrounded && _verticalVelocity < 0f)
                _verticalVelocity = -2f;

            if (IsGrounded && _jumpPressed && Stance == Stance.Standing && StaminaModel.TrySpend(jumpStaminaCost))
                _verticalVelocity = Mathf.Sqrt(jumpHeight * -2f * gravity);

            _verticalVelocity += gravity * Time.deltaTime;
            _jumpPressed        = false;

            Vector3 before = transform.position;
            _cc.Move((HorizontalMove() + Vector3.up * _verticalVelocity) * Time.deltaTime);
            StayInShallowWater(before);
        }

        // The sea floor keeps going down; stop at chest depth instead of walking along the bottom.
        private void StayInShallowWater(Vector3 before)
        {
            Vector3 now = transform.position;
            float limit = ViewBlockers.WaterHeight - maxWadeDepth;
            if (now.y >= limit || now.y >= before.y) return;
            _cc.Move(new Vector3(before.x - now.x, 0f, before.z - now.z));
        }

        private Vector3 HorizontalMove()
        {
            float speed;
            switch (Stance)
            {
                case Stance.Prone:     speed = crawlSpeed;  break;
                case Stance.Crouching: speed = crouchSpeed; break;
                default:
                    speed = IsSprinting ? runSpeed : (StaminaModel.IsExhausted ? exhaustedSpeed : walkSpeed);
                    break;
            }

            Vector3 move = transform.right * _moveInput.x + transform.forward * _moveInput.y;
            move.y = 0f;
            return move.normalized * speed;
        }

        // ── Stance ────────────────────────────────────────────────────────────

        public void SetStance(Stance stance)
        {
            if (stance == Stance) return;
            Stance = stance;
            if (stance != Stance.Standing) RunToggled = false;

            float height = stance == Stance.Prone ? proneHeight : stance == Stance.Crouching ? crouchHeight : standHeight;
            _cc.height = height;
            _cc.center = new Vector3(0f, height * 0.5f, 0f);

            // Placeholder body: squash the capsule for crouching, lay it flat for crawling.
            if (_body != null)
            {
                if (stance == Stance.Prone)
                {
                    _body.localRotation = Quaternion.Euler(90f, 0f, 0f); // lying flat, head forward
                    _body.localScale    = new Vector3(0.6f, 0.9f, 0.6f);
                    _body.localPosition = new Vector3(0f, 0.3f, 0f);
                }
                else
                {
                    float k = height / standHeight;
                    _body.localRotation = Quaternion.identity;
                    _body.localScale    = new Vector3(_bodyStandScale.x, _bodyStandScale.y * k, _bodyStandScale.z);
                    _body.localPosition = new Vector3(_bodyStandPos.x, _bodyStandPos.y * k, _bodyStandPos.z);
                }
            }
        }

        private void UpdateCameraHeight()
        {
            if (cameraTarget == null) return;

            float target = Stance == Stance.Prone ? _cameraStandY * 0.4f
                         : Stance == Stance.Crouching ? _cameraStandY * 0.7f
                         : _cameraStandY;
            var p = cameraTarget.localPosition;
            p.y = Mathf.MoveTowards(p.y, target, 4f * Time.deltaTime);
            cameraTarget.localPosition = p;
        }
    }
}
