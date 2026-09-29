using System;
using UnityEngine;
using UnityEngine.AI;
using ProjectFossil.Core;

namespace ProjectFossil.Dinosaurs
{
    [RequireComponent(typeof(NavMeshAgent))]
    public class DinosaurAI : MonoBehaviour
    {
        public DinosaurSpecies species;

        // Raised when any dinosaur dies. Killer may be null (hazards, unknown source).
        public static event Action<DinosaurAI, GameObject> Killed;

        // ── State machine ──────────────────────────────────────────────────────
        public enum State { Wander, Alert, Chase, Flee, Dead }
        public State CurrentState { get; private set; } = State.Wander;

        public Health Health { get; private set; }

        private NavMeshAgent _agent;
        private Transform    _target;
        private float        _waitTimer;
        private float        _attackTimer;
        private float        _fleeTimer;
        private bool         _hunting; // sent by the Threat Director: never gives up the chase

        // ── Lifecycle ──────────────────────────────────────────────────────────

        private void Awake()
        {
            _agent = GetComponent<NavMeshAgent>();

            Health = GetComponent<Health>();
            if (Health == null) Health = gameObject.AddComponent<Health>();
        }

        private void Start()
        {
            if (species == null)
            {
                Debug.LogWarning($"[DinosaurAI] {name} has no DinosaurSpecies assigned.", this);
                enabled = false;
                return;
            }

            transform.localScale = Vector3.one * species.bodyScale;
            Health.Initialize(species.maxHealth);
            Health.Damaged += OnDamaged;
            Health.Died    += OnDied;

            ApplySpeciesToAgent();
            if (_hunting && _target != null) EnterChase();
            else EnterWander();
        }

        private void OnDestroy()
        {
            if (Health == null) return;
            Health.Damaged -= OnDamaged;
            Health.Died    -= OnDied;
        }

        private void Update()
        {
            switch (CurrentState)
            {
                case State.Wander: UpdateWander(); break;
                case State.Alert:  UpdateAlert();  break;
                case State.Chase:  UpdateChase();  break;
                case State.Flee:   UpdateFlee();   break;
            }
        }

        // Send this dinosaur straight after a target (used by threats). It ignores chaseRange.
        public void Hunt(Transform target)
        {
            if (target == null || CurrentState == State.Dead) return;

            _target  = target;
            _hunting = true;
            if (species != null && _agent.isOnNavMesh) EnterChase();
        }

        // ── Wander ─────────────────────────────────────────────────────────────

        private void EnterWander()
        {
            CurrentState = State.Wander;
            _hunting     = false;
            _agent.speed = species.walkSpeed;
            SetRandomWanderDestination();
        }

        private void UpdateWander()
        {
            var detected = TryDetectPlayer();
            if (detected != null)
            {
                _target = detected;
                if (ShouldFlee()) EnterFlee(detected.position);
                else EnterAlert();
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
            _waitTimer = UnityEngine.Random.Range(species.wanderWaitMin, species.wanderWaitMax);

            Vector3 randomDir = UnityEngine.Random.insideUnitSphere * species.wanderRadius;
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
            if (!IsValidTarget(_target)) { EnterWander(); return; }

            FaceTowards(_target.position);

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
            if (!IsValidTarget(_target)) { _target = null; EnterWander(); return; }

            float dist = Vector3.Distance(transform.position, _target.position);

            // Give up if target is too far (hunters sent by the director never give up)
            if (!_hunting && dist > species.chaseRange)
            {
                _target = null;
                EnterWander();
                return;
            }

            if (dist > species.attackRange)
            {
                _agent.SetDestination(_target.position);
                return;
            }

            // Attack when in range
            _agent.ResetPath();
            FaceTowards(_target.position);
            _attackTimer -= Time.deltaTime;
            if (_attackTimer <= 0f)
            {
                _attackTimer = species.attackCooldown;
                var victim = _target.GetComponentInParent<IDamageable>();
                victim?.TakeDamage(new DamageInfo(species.attackDamage, gameObject, _target.position));
            }
        }

        // ── Flee ───────────────────────────────────────────────────────────────

        private bool ShouldFlee() =>
            species.fleeHealthFraction > 0f && Health.Fraction <= species.fleeHealthFraction;

        private void EnterFlee(Vector3 threatPosition)
        {
            CurrentState = State.Flee;
            _hunting     = false;
            _target      = null;
            _fleeTimer   = species.fleeDuration;
            _agent.speed = species.runSpeed;

            Vector3 away = transform.position - threatPosition;
            away.y = 0f;
            if (away.sqrMagnitude < 0.01f) away = transform.forward;

            Vector3 dest = transform.position + away.normalized * species.fleeDistance;
            if (NavMesh.SamplePosition(dest, out var hit, species.fleeDistance, NavMesh.AllAreas))
                _agent.SetDestination(hit.position);
        }

        private void UpdateFlee()
        {
            _fleeTimer -= Time.deltaTime;
            if (_fleeTimer <= 0f) EnterWander();
        }

        // ── Damage / death ─────────────────────────────────────────────────────

        private void OnDamaged(DamageInfo info)
        {
            if (!Health.IsAlive || info.Source == null) return;

            if (ShouldFlee())
            {
                EnterFlee(info.Source.transform.position);
                return;
            }

            // Retaliate against whoever hurt us
            if (CurrentState == State.Wander || CurrentState == State.Alert)
            {
                _target = info.Source.transform;
                EnterChase();
            }
        }

        private void OnDied(DamageInfo info)
        {
            CurrentState = State.Dead;
            _target      = null;
            if (_agent.isOnNavMesh) _agent.ResetPath();
            _agent.enabled = false;

            foreach (var col in GetComponentsInChildren<Collider>())
                col.enabled = false;

            // Placeholder death: tip over, then clean up
            transform.rotation = Quaternion.LookRotation(transform.forward, transform.right);
            Destroy(gameObject, 5f);

            Killed?.Invoke(this, info.Source);
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
                if (!IsValidTarget(col.transform)) continue;
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

        // Dead or destroyed targets are not worth chasing.
        private static bool IsValidTarget(Transform target)
        {
            if (target == null) return false;
            var damageable = target.GetComponentInParent<IDamageable>();
            return damageable == null || damageable.IsAlive;
        }

        // ── Helpers ────────────────────────────────────────────────────────────

        private void FaceTowards(Vector3 position)
        {
            Vector3 dir = position - transform.position;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.0001f) return;

            transform.rotation = Quaternion.RotateTowards(
                transform.rotation,
                Quaternion.LookRotation(dir.normalized),
                species.turnSpeed * Time.deltaTime);
        }

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
