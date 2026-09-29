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

        // Presentation hooks (DinosaurFeedback listens). Windup passes its duration in seconds.
        public event Action<float> AttackWindupStarted;
        public event Action        AttackLanded;

        // ── State machine ──────────────────────────────────────────────────────
        public enum State { Wander, Alert, Chase, Flee, Dead }
        public State CurrentState { get; private set; } = State.Wander;

        public Health Health { get; private set; }

        private NavMeshAgent _agent;
        private Transform    _target;
        private float        _waitTimer;
        private float        _attackTimer;
        private float        _fleeTimer;
        private float        _windupTimer;  // > 0 while a telegraphed attack is charging
        private float        _staggerTimer; // > 0 right after being hit
        private bool         _hunting; // sent by the Threat Director: never gives up the chase

        // ── Lifecycle ──────────────────────────────────────────────────────────

        private void Awake()
        {
            _agent = GetComponent<NavMeshAgent>();
            if (GetComponent<DinosaurVisual>() == null)   gameObject.AddComponent<DinosaurVisual>();
            if (GetComponent<DinosaurFeedback>() == null) gameObject.AddComponent<DinosaurFeedback>();

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
            KeepOutOfPeople();
        }

        // NavMeshAgents ignore CharacterControllers, so without this a dinosaur walks straight through the player.
        // Push the dinosaur back out along the ground whenever its body overlaps someone's capsule.
        private readonly Collider[] _near = new Collider[8];

        private void KeepOutOfPeople()
        {
            if (CurrentState == State.Dead || !_agent.enabled || !_agent.isOnNavMesh) return;
            var body = BodyCollider();
            if (body == null) return;

            var bounds = body.bounds;
            int n = Physics.OverlapBoxNonAlloc(bounds.center, bounds.extents + Vector3.one * 0.5f, _near,
                                               Quaternion.identity, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                if (!(_near[i] is CharacterController cc) || !cc.enabled) continue;
                Vector3 feet = cc.transform.position;
                Vector3 probe = feet + Vector3.up * Mathf.Min(cc.height * 0.5f, bounds.extents.y);
                Vector3 closest = body.ClosestPoint(probe);
                Vector3 away = closest - probe;
                away.y = 0f;
                float gap = away.magnitude;
                float radius = cc.radius * Mathf.Max(cc.transform.lossyScale.x, cc.transform.lossyScale.z) + 0.05f;
                if (gap >= radius) continue;

                // Inside the body (gap 0): push straight away from the person.
                Vector3 dir = gap > 0.001f ? away / gap : Flat(transform.position - feet).normalized;
                if (dir.sqrMagnitude < 0.5f) dir = -transform.forward;
                _agent.Move(dir * (radius - gap));
            }
        }

        private Collider BodyCollider()
        {
            foreach (var c in GetComponents<Collider>())
                if (c.enabled && !c.isTrigger) return c;
            return null;
        }

        private static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

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

            _attackTimer -= Time.deltaTime;

            if (_staggerTimer > 0f)
            {
                _staggerTimer -= Time.deltaTime;
                _windupTimer   = 0f; // a hit interrupts the bite
                if (_agent.hasPath) _agent.ResetPath();
                return;
            }

            // A charging bite finishes even if the target stepped back; it only lands if they're still in reach.
            if (_windupTimer > 0f)
            {
                FaceTowards(_target.position);
                _windupTimer -= Time.deltaTime;
                if (_windupTimer <= 0f)
                {
                    _attackTimer = species.attackCooldown;
                    if (dist <= species.attackRange * 1.25f)
                    {
                        var victim = _target.GetComponentInParent<IDamageable>();
                        victim?.TakeDamage(new DamageInfo(species.attackDamage, gameObject, _target.position));
                        AttackLanded?.Invoke();
                    }
                }
                return;
            }

            if (dist > species.attackRange)
            {
                _agent.SetDestination(_target.position);
                return;
            }

            // In range: stop, face the target and telegraph the bite.
            _agent.ResetPath();
            FaceTowards(_target.position);
            if (_attackTimer <= 0f)
            {
                _windupTimer = Mathf.Max(0.01f, species.attackWindup);
                AttackWindupStarted?.Invoke(species.attackWindup);
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
            _windupTimer = 0f;
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

            _staggerTimer = species.hitStagger;

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

            // Animated models play their own death; the placeholder just tips over. Then clean up.
            var visual = GetComponent<DinosaurVisual>();
            if (visual == null || !visual.IsAnimated)
                transform.rotation = Quaternion.LookRotation(transform.forward, transform.right);
            Destroy(gameObject, 5f);

            Killed?.Invoke(this, info.Source);
        }

        // ── Detection ──────────────────────────────────────────────────────────

        // Hearing is omnidirectional; sight needs the FOV cone and a clear line. Both ranges shrink
        // for a player who is crouching or crawling (IStealthProfile) and hearing grows when they run.
        private Transform TryDetectPlayer()
        {
            float maxRange = Mathf.Max(species.hearingRange, species.sightRange) * 1.5f;
            var cols = Physics.OverlapSphere(transform.position, maxRange);
            foreach (var col in cols)
            {
                if (!col.CompareTag("Player")) continue;
                if (!IsValidTarget(col.transform)) continue;

                float noise = 1f, visibility = 1f;
                var stealth = col.GetComponentInParent<IStealthProfile>();
                if (stealth != null)
                {
                    noise      = stealth.NoiseMultiplier;
                    visibility = stealth.VisibilityMultiplier;
                }

                float dist = Vector3.Distance(transform.position, col.transform.position);
                if (dist <= species.hearingRange * noise) return col.transform;

                float sight = species.sightRange * visibility;
                if (dist <= sight && HasLineOfSight(col.transform, sight)) return col.transform;
            }
            return null;
        }

        private bool HasLineOfSight(Transform target, float range)
        {
            Vector3 toTarget = target.position - transform.position;
            float angle = Vector3.Angle(transform.forward, toTarget);
            if (angle > species.sightAngle) return false;

            // Raycast; ignore trigger colliders
            if (Physics.Raycast(transform.position + Vector3.up,
                                toTarget.normalized,
                                out var hit,
                                range,
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
