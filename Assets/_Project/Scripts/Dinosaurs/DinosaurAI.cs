using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using ProjectFossil.Core;

namespace ProjectFossil.Dinosaurs
{
    [RequireComponent(typeof(NavMeshAgent))]
    public class DinosaurAI : MonoBehaviour
    {
        public DinosaurSpecies species;

        [Tooltip("Set by the spawner from the player's rank: multiplies the species' health and bite")]
        public float healthMultiplier = 1f;
        public float damageMultiplier = 1f;

        // At most this many dinosaurs bite one person at once; the rest circle and wait for an opening.
        public const int MaxAttackersPerTarget = 2;

        // Raised when any dinosaur dies. Killer may be null (hazards, unknown source).
        public static event Action<DinosaurAI, GameObject> Killed;
        // Raised when any dinosaur takes damage (amount actually applied, and who dealt it).
        public static event Action<DinosaurAI, DamageInfo> Damaged;
        // Raised when a dinosaur with a sense of smell starts following someone's scent trail.
        public static event Action<DinosaurAI> PickedUpScent;
        // Raised by spawners once a new animal's species and strength are set, before its first frame.
        // Online, the host hands it to the network here.
        public static event Action<DinosaurAI> Created;
        public static void AnnounceCreated(DinosaurAI ai) { if (ai != null) Created?.Invoke(ai); }

        public bool IsTrackingScent { get; private set; }
        // Nose down: following a trail, or standing where it went cold and casting about for it.
        public bool IsSniffing => IsTrackingScent || _castTimer > 0f;
        // The person this predator was sent to stalk (null for ordinary wildlife).
        public Transform Quarry => _quarry;

        // Presentation hooks (DinosaurFeedback listens). Windup passes its duration in seconds.
        public event Action<float> AttackWindupStarted;
        public event Action        AttackLanded;

        // ── State machine ──────────────────────────────────────────────────────
        public enum State { Wander, Alert, Chase, Flee, Dead }
        public State CurrentState { get; private set; } = State.Wander;

        public Health Health { get; private set; }

        // Online, on a machine that joined someone else's match: this is a copy of the host's animal. It thinks
        // nothing for itself; the networking layer moves it and feeds it state, health and bites to show.
        public bool IsRemote { get; private set; }
        private bool _remoteReady;

        private NavMeshAgent _agent;
        private Transform    _target;
        private float        _waitTimer;
        private float        _attackTimer;
        private float        _fleeTimer;
        private float        _windupTimer;  // > 0 while a telegraphed attack is charging
        private float        _staggerTimer; // > 0 right after being hit
        private bool         _hunting; // sent by the Threat Director: never gives up the chase
        private float        _trampleDamage; // > 0 while stampeding: runs through people and hurts them
        private float        _trampleTimer;
        private Transform    _slotTarget;    // the person this dinosaur holds a bite slot on
        private float        _circleSide;    // +1 / -1: which way it circles while waiting its turn
        private static int   _spawnCounter;  // alternates circling direction between animals
        private Transform    _quarry;        // set by Track: roams toward this person and follows their scent
        private float        _scentCheck;
        private float        _scentTime = float.NegativeInfinity; // time stamp of the last mark it followed
        private float        _castTimer;     // > 0 while it stands where the trail went cold, sniffing around
        private float        _stuckTimer;    // how long a chase has made no headway
        private float        _detourTimer;   // > 0 while it goes around something to get at its target

        private static readonly Dictionary<Transform, List<DinosaurAI>> Attackers =
            new Dictionary<Transform, List<DinosaurAI>>();

        private static readonly List<DinosaurAI> Living = new List<DinosaurAI>();
        private void OnEnable()  => Living.Add(this);
        private void OnDisable() => Living.Remove(this);

        // A loud noise (a helicopter, an explosion): wandering animals within `radius` react to it. Hunters come to
        // look, skittish ones bolt away. Animals already busy with someone ignore it.
        public static void NoiseAt(Vector3 position, float radius)
        {
            float r2 = radius * radius;
            for (int i = Living.Count - 1; i >= 0; i--)
            {
                var ai = Living[i];
                if (ai == null || (ai.transform.position - position).sqrMagnitude > r2) continue;
                ai.HearNoise(position);
            }
        }

        // How much danger someone at `position` is in, 0..1, from the predators around them: one charging nearby
        // is 1, one following a scent trail is felt from further out, a big one wandering close is a warning.
        // Presentation (heartbeat, screen edges) reads this; the simulation doesn't.
        public static float DangerAt(Vector3 position)
        {
            float danger = 0f;
            for (int i = Living.Count - 1; i >= 0; i--)
            {
                var ai = Living[i];
                if (ai == null || ai.species == null || ai.CurrentState == State.Dead) continue;
                if (ai.species.temperament != Temperament.Predator) continue;
                float d = Vector3.Distance(ai.transform.position, position);
                bool big = ai.species.maxHealth >= 200f;
                float level;
                // Small hunters are the everyday threat and stay a warning; the big one is the real fear.
                if (ai.CurrentState == State.Chase || ai.CurrentState == State.Alert)
                    level = (big ? 1f : 0.55f) * (1f - d / 45f);
                else if (ai.IsSniffing) level = 0.75f * (1f - d / 70f);
                else if (big)           level = 0.45f * (1f - d / 40f);
                else                    level = 0.15f * (1f - d / 20f);
                if (level > danger) danger = level;
            }
            return Mathf.Clamp01(danger);
        }

        // True while a predator is sniffing out `quarry`'s trail, or sniffing within `near` metres of them.
        public static bool IsBeingTracked(Transform quarry, float near = 35f)
        {
            if (quarry == null) return false;
            float n2 = near * near;
            for (int i = Living.Count - 1; i >= 0; i--)
            {
                var ai = Living[i];
                if (ai == null || !ai.IsSniffing) continue;
                if (ai._quarry == quarry || (ai.transform.position - quarry.position).sqrMagnitude < n2) return true;
            }
            return false;
        }

        public void HearNoise(Vector3 position)
        {
            if (species == null || IsRemote || !_agent.isOnNavMesh) return;
            if (CurrentState != State.Wander) return; // already busy with someone

            if (species.temperament == Temperament.Skittish) { EnterFlee(position); return; }

            // Come and see: to a spot near the noise, at a brisk walk, then wander from there.
            Vector3 near = position + UnityEngine.Random.insideUnitSphere * 10f;
            if (!NavMesh.SamplePosition(near, out var hit, 15f, NavMesh.AllAreas)) return;
            IsTrackingScent = false;
            _agent.speed = species.walkSpeed * 1.5f;
            _agent.SetDestination(hit.position);
            _waitTimer = UnityEngine.Random.Range(species.wanderWaitMin, species.wanderWaitMax) + 3f;
        }

        // ── Lifecycle ──────────────────────────────────────────────────────────

        private void Awake()
        {
            _agent = GetComponent<NavMeshAgent>();
            if (GetComponent<DinosaurVisual>() == null)   gameObject.AddComponent<DinosaurVisual>();
            if (GetComponent<DinosaurFeedback>() == null) gameObject.AddComponent<DinosaurFeedback>();

            Health = GetComponent<Health>();
            if (Health == null) Health = gameObject.AddComponent<Health>();

            IsRemote = NetRole.IsFollower;
            if (IsRemote) _agent.enabled = false; // the host's NavMesh decides where it goes
        }

        private void Start()
        {
            if (IsRemote) { SetUpRemote(); return; }
            if (species == null)
            {
                Debug.LogWarning($"[DinosaurAI] {name} has no DinosaurSpecies assigned.", this);
                enabled = false;
                return;
            }

            transform.localScale = Vector3.one * species.bodyScale;
            Health.Initialize(species.maxHealth * Mathf.Max(0.1f, healthMultiplier));
            _circleSide = (_spawnCounter++ & 1) == 0 ? 1f : -1f;
            Health.Damaged += OnDamaged;
            Health.Died    += OnDied;

            ApplySpeciesToAgent();
            if (_hunting && _target != null) EnterChase();
            else EnterWander();
        }

        private void OnDestroy()
        {
            ReleaseSlot();
            if (Health == null) return;
            Health.Damaged -= OnDamaged;
            Health.Died    -= OnDied;
        }

        private void Update()
        {
            if (IsRemote) { if (!_remoteReady) SetUpRemote(); return; }
            if (_trampleTimer > 0f) _trampleTimer -= Time.deltaTime;
            switch (CurrentState)
            {
                case State.Wander: UpdateWander(); break;
                case State.Alert:  UpdateAlert();  break;
                case State.Chase:  UpdateChase();  break;
                case State.Flee:   UpdateFlee();   break;
            }
            KeepOutOfPeople();
        }

        // ── Online copy ────────────────────────────────────────────────────────

        // The species arrives with the spawn message; if it came late, try again next frame.
        private void SetUpRemote()
        {
            if (_remoteReady || species == null) return;
            _remoteReady = true;
            transform.localScale = Vector3.one * species.bodyScale;
            if (Health.Pool == null || Health.Max <= 0f) Health.Initialize(species.maxHealth);
            Health.Damaged += OnDamaged;
            Health.Died    += OnDied;
            ApplySpeciesToAgent();
        }

        // What the host's animal is doing (drives the heartbeat, sniffing sounds and HUD). Death comes through
        // health, so it plays out here exactly once.
        public void SetRemoteState(State state, bool sniffing)
        {
            if (!IsRemote || CurrentState == State.Dead || state == State.Dead) return;
            CurrentState    = state;
            IsTrackingScent = sniffing;
        }

        public void PlayRemoteWindup(float seconds)
        {
            if (IsRemote && CurrentState != State.Dead) AttackWindupStarted?.Invoke(seconds);
        }

        public void PlayRemoteBite()
        {
            if (IsRemote && CurrentState != State.Dead) AttackLanded?.Invoke();
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

                // A stampeding animal hurts whoever it runs into (once a second at most).
                if (_trampleDamage > 0f && _trampleTimer <= 0f)
                {
                    var victim = cc.GetComponentInParent<IDamageable>();
                    if (victim != null && victim.IsAlive)
                    {
                        victim.TakeDamage(new DamageInfo(_trampleDamage * damageMultiplier, gameObject, probe));
                        _trampleTimer = 1f;
                    }
                }

                // Inside the body (gap 0): push straight away from the person.
                Vector3 dir = gap > 0.001f ? away / gap : Flat(transform.position - feet).normalized;
                if (dir.sqrMagnitude < 0.5f) dir = -transform.forward;
                _agent.Move(dir * (radius - gap));
            }
        }

        private Collider _body; // the visual swaps the placeholder capsule for a box around the model

        private Collider BodyCollider()
        {
            if (_body != null && _body.enabled) return _body;
            _body = null;
            foreach (var c in GetComponents<Collider>())
                if (c.enabled && !c.isTrigger) return _body = c;
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

        // Stalk a target (used by threats for predators with a nose): no homing, it roams toward where the target
        // is and follows the scent trail once it crosses it. Sight or sound then turns it into a normal chase.
        public void Track(Transform quarry)
        {
            if (quarry == null || CurrentState == State.Dead || species == null || !_agent.isOnNavMesh) return;
            _quarry = quarry;
            EnterWander();
        }

        // Stampede (used by threats): run flat out through a point and keep going, trampling anyone in the way.
        public void Stampede(Vector3 through, float trampleDamage)
        {
            if (CurrentState == State.Dead || species == null || !_agent.isOnNavMesh) return;

            Vector3 dir = Flat(through - transform.position);
            if (dir.sqrMagnitude < 0.01f) dir = transform.forward;
            dir.Normalize();

            float runOut = 50f;
            Vector3 dest = through + dir * runOut;
            if (NavMesh.SamplePosition(dest, out var hit, 30f, NavMesh.AllAreas)) dest = hit.position;

            ReleaseSlot();
            CurrentState   = State.Flee;
            _hunting       = false;
            _target        = null;
            _windupTimer   = 0f;
            _trampleDamage = trampleDamage;
            _agent.speed   = species.runSpeed * 1.15f;
            _agent.acceleration = 25f;
            _agent.SetDestination(dest);
            _fleeTimer = Vector3.Distance(transform.position, dest) / Mathf.Max(1f, _agent.speed) + 2f;
        }

        // ── Wander ─────────────────────────────────────────────────────────────

        private void EnterWander()
        {
            ReleaseSlot();
            CurrentState   = State.Wander;
            _hunting       = false;
            _trampleDamage = 0f;
            _agent.speed   = species.walkSpeed;
            _agent.acceleration = 10f;
            SetRandomWanderDestination();
        }

        private void UpdateWander()
        {
            var detected = TryDetectPlayer();
            if (detected != null)
            {
                switch (species.temperament)
                {
                    case Temperament.Skittish:
                        EnterFlee(detected.position);
                        return;

                    case Temperament.Territorial:
                        // Watch intruders; only charge the ones who come too close.
                        if (Vector3.Distance(transform.position, detected.position) > species.territoryRadius)
                        {
                            if (_agent.hasPath) _agent.ResetPath();
                            FaceTowards(detected.position);
                            return;
                        }
                        break;
                }

                _target = detected;
                if (ShouldFlee()) EnterFlee(detected.position);
                else EnterAlert();
                return;
            }

            if (FollowScent()) return;

            if (_castTimer > 0f)
            {
                // Where the trail went cold: stop, head low, swing around testing the air.
                _castTimer -= Time.deltaTime;
                transform.Rotate(0f, Mathf.Sin(_castTimer * 1.7f) * 50f * Time.deltaTime, 0f);
                if (_castTimer <= 0f) SetRandomWanderDestination();
                return;
            }

            if (!_agent.pathPending && _agent.remainingDistance <= _agent.stoppingDistance)
            {
                _waitTimer -= Time.deltaTime;
                if (_waitTimer <= 0f)
                    SetRandomWanderDestination();
            }
        }

        // Nose down along the trail: head for the freshest mark in smelling range, then the next one.
        // Returns true while it's on a trail (the trail decides where it goes, not wandering).
        private bool FollowScent()
        {
            var trail = ScentTrail.Active;
            if (species.smellRange <= 0f || trail == null) return false;

            _scentCheck -= Time.deltaTime;
            if (_scentCheck > 0f) return IsTrackingScent;
            _scentCheck = 1f;

            if (trail.TryFindFreshest(transform.position, species.smellRange, _scentTime, out var mark, out float time))
            {
                _scentTime = time;
                _castTimer = 0f;
                _agent.speed = species.walkSpeed * 1.3f; // purposeful, but a walk: it's sniffing, not charging
                _agent.SetDestination(mark);
                if (!IsTrackingScent)
                {
                    IsTrackingScent = true;
                    PickedUpScent?.Invoke(this);
                }
                return true;
            }

            if (IsTrackingScent && !_agent.pathPending && _agent.remainingDistance <= _agent.stoppingDistance + 0.5f)
            {
                // End of the trail (it faded, or went through water): cast about from here.
                IsTrackingScent = false;
                _scentTime = float.NegativeInfinity;
                _agent.speed = species.walkSpeed;
                _agent.ResetPath();
                _castTimer = 5f;
                _waitTimer = 1.5f;
            }
            return IsTrackingScent;
        }

        private void SetRandomWanderDestination()
        {
            _waitTimer = UnityEngine.Random.Range(species.wanderWaitMin, species.wanderWaitMax);

            if (_quarry != null && !IsValidTarget(_quarry)) _quarry = null;
            float radius = _quarry != null ? Mathf.Max(40f, species.wanderRadius) : species.wanderRadius;
            Vector3 randomDir = UnityEngine.Random.insideUnitSphere * radius;
            // A stalker drifts toward its quarry's area instead of wherever it happens to be.
            randomDir += _quarry != null ? _quarry.position : transform.position;
            if (NavMesh.SamplePosition(randomDir, out var hit, radius, NavMesh.AllAreas))
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
            IsTrackingScent = false;
            _castTimer = 0f;
            _scentTime = float.NegativeInfinity;
            _agent.speed = species.runSpeed;
            _stuckTimer  = 0f;
            _detourTimer = 0f;
        }

        private void UpdateChase()
        {
            if (!IsValidTarget(_target)) { _target = null; EnterWander(); return; }

            float dist = Vector3.Distance(transform.position, _target.position);
            // Reach is measured from the body you see: a big animal bites from its snout, not from its middle,
            // so it can't end up standing over someone.
            float gap   = GapTo(_target.position);
            float reach = BiteReach;

            // Give up if target is too far (hunters sent by the director never give up)
            if (!_hunting && dist > GiveUpRange)
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
                    if (gap <= reach * 1.25f + 0.5f)
                    {
                        var victim = _target.GetComponentInParent<IDamageable>();
                        victim?.TakeDamage(new DamageInfo(species.attackDamage * damageMultiplier, gameObject, _target.position));
                        AttackLanded?.Invoke();
                    }
                }
                return;
            }

            // Only a couple bite at once; the rest circle just out of reach and wait for a gap.
            if (gap <= reach + 4f && !HoldAttackSlot(_target))
            {
                CircleAround(_target.position);
                return;
            }

            if (gap > reach)
            {
                _agent.speed = species.runSpeed;
                if (_detourTimer > 0f)
                {
                    // Going around whatever blocked the way; back to the straight chase once there.
                    _detourTimer -= Time.deltaTime;
                    if (!_agent.pathPending && _agent.remainingDistance < 1.5f) _detourTimer = 0f;
                    return;
                }
                _agent.SetDestination(_target.position);

                // Stuck: hardly moving although out of reach, or the path ends short of the target (up on a rock,
                // behind a tree wall). Work round to another side rather than standing there.
                bool still   = !_agent.pathPending && _agent.velocity.sqrMagnitude < 0.36f;
                bool cutOff  = !_agent.pathPending && _agent.pathStatus != NavMeshPathStatus.PathComplete &&
                               _agent.remainingDistance < 2f;
                _stuckTimer = still || cutOff ? _stuckTimer + Time.deltaTime : 0f;
                if (_stuckTimer > 1.2f) { _stuckTimer = 0f; Detour(_target.position, dist - gap + reach); }
                return;
            }
            _stuckTimer = 0f;

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
            ReleaseSlot();
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
            Damaged?.Invoke(this, info);
            if (IsRemote) return;
            if (!Health.IsAlive || info.Source == null) return;
            if (_trampleDamage > 0f) return; // a stampede doesn't stop for a punch

            _staggerTimer = species.hitStagger;

            // Skittish animals bolt when hurt, unless they're already cornered and fighting.
            bool bolt = species.temperament == Temperament.Skittish && CurrentState != State.Chase && Health.Fraction > 0.5f;
            if (ShouldFlee() || bolt)
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
            ReleaseSlot();
            CurrentState = State.Dead;
            _target      = null;
            if (_agent.enabled && _agent.isOnNavMesh) _agent.ResetPath();
            _agent.enabled = false;

            foreach (var col in GetComponentsInChildren<Collider>())
                col.enabled = false;

            // Animated models play their own death; the placeholder just tips over. Then clean up.
            var visual = GetComponent<DinosaurVisual>();
            if (visual == null || !visual.IsAnimated)
                transform.rotation = Quaternion.LookRotation(transform.forward, transform.right);
            if (!IsRemote) Destroy(gameObject, 5f); // a copy goes when the host removes the original

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
                if (dist <= species.hearingRange * noise * WorldConditions.HearingMultiplier) return col.transform;

                float sight = species.sightRange * visibility * WorldConditions.SightMultiplier;
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

        // ── Bite slots ─────────────────────────────────────────────────────────

        private float GiveUpRange =>
            species.temperament == Temperament.Territorial ? Mathf.Min(species.chaseRange, species.territoryRadius * 3f)
                                                           : species.chaseRange;

        private bool HoldAttackSlot(Transform target)
        {
            if (!Attackers.TryGetValue(target, out var list))
                Attackers[target] = list = new List<DinosaurAI>();

            list.RemoveAll(a => a == null || a._slotTarget != target || a.CurrentState != State.Chase);
            if (list.Contains(this)) return true;
            if (list.Count >= MaxAttackersPerTarget) return false;

            list.Add(this);
            _slotTarget = target;
            return true;
        }

        private void ReleaseSlot()
        {
            if (_slotTarget != null && Attackers.TryGetValue(_slotTarget, out var list))
            {
                list.Remove(this);
                if (list.Count == 0) Attackers.Remove(_slotTarget);
            }
            _slotTarget = null;
        }

        // How far a bite reaches beyond the body's surface. Species ranges were tuned from the body's centre on
        // small placeholder bodies, about half of it ahead of the snout.
        private float BiteReach => species.attackRange * 0.5f;

        // Ground distance from the body's surface to a point (0 when it's under or inside the body).
        private float GapTo(Vector3 point)
        {
            var body = BodyCollider();
            if (body == null) return Flat(point - transform.position).magnitude;
            var b = body.bounds;
            Vector3 level = new Vector3(point.x, b.center.y, point.z);
            return Flat(level - body.ClosestPoint(level)).magnitude;
        }

        // Somewhere else around the target, partway round to one side, to come at it from there.
        private void Detour(Vector3 target, float radius)
        {
            _circleSide = -_circleSide;
            Vector3 from = Flat(transform.position - target);
            if (from.sqrMagnitude < 0.01f) from = -transform.forward;
            for (int i = 0; i < 4; i++)
            {
                float turn = UnityEngine.Random.Range(50f, 130f) * (i % 2 == 0 ? _circleSide : -_circleSide);
                Vector3 p = target + Quaternion.Euler(0f, turn, 0f) * from.normalized * (radius + UnityEngine.Random.Range(2f, 6f));
                if (NavMesh.SamplePosition(p, out var hit, 4f, NavMesh.AllAreas) && _agent.SetDestination(hit.position))
                {
                    _detourTimer = 3f;
                    return;
                }
            }
        }

        // Walk around the target just outside biting range, facing it.
        private void CircleAround(Vector3 center)
        {
            Vector3 from = Flat(transform.position - center);
            if (from.sqrMagnitude < 0.01f) from = -transform.forward;
            // Body length counts: a big animal circles farther out than a small one.
            float radius = from.magnitude - GapTo(center) + BiteReach + 3f;
            Vector3 next = Quaternion.Euler(0f, 35f * _circleSide, 0f) * from.normalized * radius;
            _agent.speed = species.walkSpeed;
            if (NavMesh.SamplePosition(center + next, out var hit, 3f, NavMesh.AllAreas))
                _agent.SetDestination(hit.position);
            if (Flat(transform.position - center).magnitude < radius + 1f) FaceTowards(center);
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
            _agent.stoppingDistance  = 0.5f; // the chase stops itself once the snout is in reach
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
