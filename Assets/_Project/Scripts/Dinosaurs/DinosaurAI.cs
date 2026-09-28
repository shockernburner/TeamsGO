using UnityEngine;
using UnityEngine.AI;

namespace ProjectFossil.Dinosaurs
{
    [RequireComponent(typeof(NavMeshAgent))]
    public class DinosaurAI : MonoBehaviour
    {
        public DinosaurSpecies species;

        // ── State machine ──────────────────────────────────────────────────────
        public enum State { Wander, Alert, Chase }
        public State CurrentState { get; private set; } = State.Wander;

        private NavMeshAgent _agent;
        private Transform    _target;
        private float        _waitTimer;
        private float        _attackTimer;

        // ── Lifecycle ──────────────────────────────────────────────────────────

        private void Awake()
        {
            _agent = GetComponent<NavMeshAgent>();
        }

        private void Start()
        {
            if (species == null)
            {
                Debug.LogWarning($"[DinosaurAI] {name} has no DinosaurSpecies assigned.", this);
                enabled = false;
                return;
            }

            ApplySpeciesToAgent();
            EnterWander();
        }

        private void Update()
        {
            switch (CurrentState)
            {
                case State.Wander: UpdateWander(); break;
                case State.Alert:  UpdateAlert();  break;
                case State.Chase:  UpdateChase();  break;
            }
        }

        // ── Wander ─────────────────────────────────────────────────────────────

        private void EnterWander()
        {
            CurrentState = State.Wander;
            _agent.speed = species.walkSpeed;
            SetRandomWanderDestination();
        }

        private void UpdateWander()
        {
            // Try to detect players every frame
            var detected = TryDetectPlayer();
            if (detected != null)
            {
                _target = detected;
                EnterAlert();
                return;
            }

            if (!_agent.pathPending && _agent.remainingDistance <= _agent.stoppingDistance)
            {
                _waitTimer -= Time.deltaTime;
                if (_waitTimer <= 0f)
                    SetRandomWanderDestination();
            }
        }

        private void SetRandomWanderDestination()
        {
            _waitTimer = Random.Range(species.wanderWaitMin, species.wanderWaitMax);

            Vector3 randomDir = Random.insideUnitSphere * species.wanderRadius;
            randomDir += transform.position;
            if (NavMesh.SamplePosition(randomDir, out var hit, species.wanderRadius, NavMesh.AllAreas))
                _agent.SetDestination(hit.position);
        }

        // ── Alert ──────────────────────────────────────────────────────────────

        private void EnterAlert()
        {
            CurrentState = State.Alert;
            _agent.ResetPath();
            _waitTimer = 0.4f; // brief pause before chase
        }

        private void UpdateAlert()
        {
            if (_target == null) { EnterWander(); return; }

            // Face the target while paused
            Vector3 dir = (_target.position - transform.position).normalized;
            dir.y = 0f;
            if (dir != Vector3.zero)
                transform.rotation = Quaternion.RotateTowards(
                    transform.rotation,
                    Quaternion.LookRotation(dir),
                    species.turnSpeed * Time.deltaTime);

            _waitTimer -= Time.deltaTime;
            if (_waitTimer <= 0f)
                EnterChase();
        }

        // ── Chase ──────────────────────────────────────────────────────────────

        private void EnterChase()
        {
            CurrentState = State.Chase;
            _agent.speed = species.runSpeed;
        }

        private void UpdateChase()
        {
            if (_target == null) { EnterWander(); return; }

            float dist = Vector3.Distance(transform.position, _target.position);

            // Give up if target is too far
            if (dist > species.chaseRange)
            {
                _target = null;
                EnterWander();
                return;
            }

            _agent.SetDestination(_target.position);

            // Attack when in range
            if (dist <= species.attackRange)
            {
                _agent.ResetPath();
                _attackTimer -= Time.deltaTime;
                if (_attackTimer <= 0f)
                {
                    _attackTimer = species.attackCooldown;
                    // TODO: deal damage via health component
                    Debug.Log($"[DinosaurAI] {name} attacks {_target.name} for {species.attackDamage} dmg");
                }
            }
        }

        // ── Detection ──────────────────────────────────────────────────────────

        private Transform TryDetectPlayer()
        {
            // Hearing: omnidirectional sphere check
            var heard = OverlapCheckForPlayer(species.hearingRange);
            if (heard != null) return heard;

            // Sight: FOV cone + line-of-sight raycast
            var inSightRange = OverlapCheckForPlayer(species.sightRange);
            if (inSightRange != null && HasLineOfSight(inSightRange))
                return inSightRange;

            return null;
        }

        private Transform OverlapCheckForPlayer(float radius)
        {
            var cols = Physics.OverlapSphere(transform.position, radius);
            foreach (var col in cols)
            {
                if (!col.CompareTag("Player")) continue;
                return col.transform;
            }
            return null;
        }

        private bool HasLineOfSight(Transform target)
        {
            Vector3 toTarget = target.position - transform.position;
            float angle = Vector3.Angle(transform.forward, toTarget);
            if (angle > species.sightAngle) return false;

            // Raycast; ignore trigger colliders
            if (Physics.Raycast(transform.position + Vector3.up,
                                toTarget.normalized,
                                out var hit,
                                species.sightRange,
                                Physics.DefaultRaycastLayers,
                                QueryTriggerInteraction.Ignore))
            {
                return hit.collider.CompareTag("Player");
            }
            return false;
        }

        // ── Helpers ────────────────────────────────────────────────────────────

        private void ApplySpeciesToAgent()
        {
            _agent.speed             = species.walkSpeed;
            _agent.angularSpeed      = species.turnSpeed;
            _agent.stoppingDistance  = species.attackRange * 0.9f;
            _agent.acceleration      = 10f;
        }

        private void OnDrawGizmosSelected()
        {
            if (species == null) return;

            // Hearing range (yellow)
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(transform.position, species.hearingRange);

            // Sight cone (cyan)
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(transform.position, species.sightRange);
            Vector3 left  = Quaternion.Euler(0, -species.sightAngle, 0) * transform.forward * species.sightRange;
            Vector3 right = Quaternion.Euler(0,  species.sightAngle, 0) * transform.forward * species.sightRange;
            Gizmos.DrawLine(transform.position, transform.position + left);
            Gizmos.DrawLine(transform.position, transform.position + right);

            // Chase give-up range (red)
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(transform.position, species.chaseRange);
        }
    }
}
