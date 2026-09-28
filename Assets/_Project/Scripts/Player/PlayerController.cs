using UnityEngine;
using UnityEngine.InputSystem;

namespace ProjectFossil.Player
{
    [RequireComponent(typeof(CharacterController))]
    public class PlayerController : MonoBehaviour
    {
        [Header("Movement")]
        public float walkSpeed   = 5f;
        public float sprintSpeed = 9f;
        public float crouchSpeed = 2.5f;
        public float gravity     = -20f;
        public float jumpHeight  = 1.5f;

        [Header("Stamina")]
        public float maxStamina         = 100f;
        public float staminaDrainRate   = 15f;  // per second while sprinting
        public float staminaRegenRate   = 8f;   // per second while not sprinting
        public float staminaRegenDelay  = 1.5f; // seconds after last sprint before regen starts

        [Header("Camera")]
        public Transform cameraTarget;
        public float mouseSensitivity = 0.15f;
        public float verticalClampMin = -80f;
        public float verticalClampMax = 75f;

        // ── State ─────────────────────────────────────────────────────────────
        public float Stamina { get; private set; }
        public bool  IsSprinting  { get; private set; }
        public bool  IsCrouching  { get; private set; }
        public bool  IsGrounded   { get; private set; }

        private CharacterController _cc;
        private Vector2 _moveInput;
        private Vector2 _lookInput;
        private bool    _jumpPressed;
        private bool    _sprintHeld;
        private bool    _crouchHeld;
        private float   _verticalVelocity;
        private float   _pitch;
        private float   _staminaRegenTimer;

        private void Awake()
        {
            _cc      = GetComponent<CharacterController>();
            Stamina  = maxStamina;
        }

        private void OnEnable()
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible   = false;
        }

        private void OnDisable()
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible   = true;
        }

        // ── Input callbacks (New Input System) ───────────────────────────────

        public void OnMove(InputValue v)   => _moveInput  = v.Get<Vector2>();
        public void OnLook(InputValue v)   => _lookInput  = v.Get<Vector2>();
        public void OnJump(InputValue v)   => _jumpPressed = v.isPressed;
        public void OnSprint(InputValue v) => _sprintHeld  = v.isPressed;
        public void OnCrouch(InputValue v) => _crouchHeld  = v.isPressed;

        // ── Update ────────────────────────────────────────────────────────────

        private void Update()
        {
            IsGrounded = _cc.isGrounded;

            HandleLook();
            HandleStamina();
            HandleMovement();
        }

        private void HandleLook()
        {
            transform.Rotate(Vector3.up, _lookInput.x * mouseSensitivity, Space.World);

            _pitch = Mathf.Clamp(_pitch - _lookInput.y * mouseSensitivity, verticalClampMin, verticalClampMax);
            if (cameraTarget != null)
                cameraTarget.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
        }

        private void HandleStamina()
        {
            if (IsSprinting)
            {
                Stamina            = Mathf.Max(0f, Stamina - staminaDrainRate * Time.deltaTime);
                _staminaRegenTimer = 0f;
            }
            else
            {
                _staminaRegenTimer += Time.deltaTime;
                if (_staminaRegenTimer >= staminaRegenDelay)
                    Stamina = Mathf.Min(maxStamina, Stamina + staminaRegenRate * Time.deltaTime);
            }
        }

        private void HandleMovement()
        {
            // Vertical velocity
            if (IsGrounded && _verticalVelocity < 0f)
                _verticalVelocity = -2f;

            if (IsGrounded && _jumpPressed && !IsCrouching)
                _verticalVelocity = Mathf.Sqrt(jumpHeight * -2f * gravity);

            _verticalVelocity += gravity * Time.deltaTime;
            _jumpPressed        = false;

            // Determine move state
            IsCrouching = _crouchHeld && IsGrounded;
            IsSprinting = _sprintHeld && !IsCrouching && Stamina > 0f && _moveInput.sqrMagnitude > 0.01f;

            float speed = IsCrouching ? crouchSpeed : (IsSprinting ? sprintSpeed : walkSpeed);

            // Horizontal movement
            Vector3 move = transform.right   * _moveInput.x
                         + transform.forward * _moveInput.y;
            move.y = 0f;

            _cc.Move((move.normalized * speed + Vector3.up * _verticalVelocity) * Time.deltaTime);
        }
    }
}
