using UnityEngine;
using UnityEngine.AI;
using ProjectFossil.Core;

namespace ProjectFossil.Dinosaurs
{
    // Swaps the placeholder body for the species' imported model and drives its Animator from what the AI
    // is doing. Presentation only: DinosaurAI never reads this back except to skip the placeholder death tip.
    //
    // Animator parameters (built by the model setup tool): Speed (float, m/s), Attack (trigger), Dead (bool).
    public class DinosaurVisual : MonoBehaviour
    {
        private static readonly int SpeedId  = Animator.StringToHash("Speed");
        private static readonly int AttackId = Animator.StringToHash("Attack");
        private static readonly int DeadId   = Animator.StringToHash("Dead");

        public Transform ModelRoot { get; private set; }
        public bool      IsAnimated => _animator != null;

        private DinosaurAI   _ai;
        private NavMeshAgent _agent;
        private Animator     _animator;
        private bool         _built;
        private Vector3      _lastPos;

        private void Start() => Build();

        // Safe to call more than once; DinosaurFeedback calls it so it sees the model's renderers.
        public void Build()
        {
            if (_built) return;
            _ai    = GetComponent<DinosaurAI>();
            _agent = GetComponent<NavMeshAgent>();
            var def = _ai != null && _ai.species != null ? _ai.species.model : null;
            if (def == null || !def.HasModel) return; // species not set yet, or no model: keep the capsule
            _built = true;

            // Hide the placeholder, keep its colliders (gameplay uses them).
            foreach (var r in GetComponentsInChildren<Renderer>()) r.enabled = false;

            var model = ModelFit.Spawn(def, transform);
            ModelRoot = model.transform;
            FitBodyToModel(model);

            if (def.animator != null)
            {
                _animator = model.GetComponentInChildren<Animator>();
                if (_animator == null) _animator = model.AddComponent<Animator>();
                _animator.runtimeAnimatorController = def.animator;
                _animator.applyRootMotion = false;
                _animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
            }

            _ai.AttackWindupStarted += OnWindup;
            if (_ai.Health != null) _ai.Health.Died += OnDied;
        }

        // The placeholder capsule is a person-sized pill; a raptor is long and low. Swap it for a box around the
        // model so spears, fists and the player all collide with the body you can see, and size the agent to match.
        private void FitBodyToModel(GameObject model)
        {
            var b = ModelFit.MeasureLocal(model, transform);
            var old = GetComponent<CapsuleCollider>();
            if (old != null) old.enabled = false;
            var box = gameObject.AddComponent<BoxCollider>();
            box.center = b.center;
            box.size   = new Vector3(b.size.x * 0.8f, b.size.y * 0.9f, b.size.z * 0.85f); // trim tail tip and snout air

            if (_agent != null)
            {
                _agent.radius = Mathf.Max(0.3f, Mathf.Min(b.size.x, b.size.z) * 0.5f);
                _agent.height = b.size.y;
            }
        }

        private void OnDestroy()
        {
            if (_ai == null) return;
            _ai.AttackWindupStarted -= OnWindup;
            if (_ai.Health != null) _ai.Health.Died -= OnDied;
        }

        private void Update()
        {
            Vector3 moved = transform.position - _lastPos;
            _lastPos = transform.position;
            if (_animator == null || _agent == null) return;
            // Agent speeds are world m/s and so are the blend thresholds (the species' walk and run speeds).
            // An online copy has no agent running; it moves by what the host sends, so measure that.
            float speed;
            if (_agent.enabled) speed = _agent.velocity.magnitude;
            else if (_ai != null && _ai.IsRemote && _ai.CurrentState != DinosaurAI.State.Dead && Time.deltaTime > 0f)
            {
                moved.y = 0f;
                speed = moved.magnitude / Time.deltaTime;
                if (speed > 40f) speed = 0f; // a snap, not a sprint
            }
            else speed = 0f;
            _animator.SetFloat(SpeedId, speed, 0.1f, Time.deltaTime);
        }

        private void OnWindup(float seconds)
        {
            if (_animator != null) _animator.SetTrigger(AttackId);
        }

        private void OnDied(DamageInfo info)
        {
            if (_animator != null) _animator.SetBool(DeadId, true);
        }
    }
}
