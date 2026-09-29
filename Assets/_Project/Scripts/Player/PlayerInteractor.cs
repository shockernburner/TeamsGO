using UnityEngine;
using UnityEngine.InputSystem;
using ProjectFossil.Core;

namespace ProjectFossil.Player
{
    // Finds the best IInteractable in front of the player and uses it on the Interact action.
    [RequireComponent(typeof(PlayerController))]
    public class PlayerInteractor : MonoBehaviour
    {
        public float interactRange = 3f;
        [Range(-1f, 1f)]
        public float minFacingDot  = 0.3f; // how directly the player must face the object

        public IInteractable Current { get; private set; }
        public string CurrentPrompt => Current?.Prompt;

        private PlayerController _controller;
        private readonly Collider[] _buffer = new Collider[32];

        private void Awake()
        {
            _controller = GetComponent<PlayerController>();
        }

        private void Update()
        {
            Current = _controller.enabled && !_controller.InputBlocked ? FindBest() : null;
        }

        public void OnInteract(InputValue v)
        {
            if (!v.isPressed || Current == null) return;
            if (Current.CanInteract(gameObject))
                Current.Interact(gameObject);
        }

        private IInteractable FindBest()
        {
            int count = Physics.OverlapSphereNonAlloc(transform.position, interactRange, _buffer,
                                                      Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide);
            IInteractable best = null;
            float bestScore = float.MinValue;
            for (int i = 0; i < count; i++)
            {
                var interactable = _buffer[i].GetComponentInParent<IInteractable>();
                if (interactable == null || !interactable.CanInteract(gameObject)) continue;

                Vector3 to = _buffer[i].bounds.center - transform.position;
                to.y = 0f;
                float dot = to.sqrMagnitude > 0.0001f ? Vector3.Dot(transform.forward, to.normalized) : 1f;
                if (dot < minFacingDot) continue;

                float score = dot - to.magnitude / interactRange;
                if (score > bestScore)
                {
                    bestScore = score;
                    best      = interactable;
                }
            }
            return best;
        }
    }
}
