using System;
using UnityEngine;
using ProjectFossil.Core;
using ProjectFossil.Player;

namespace ProjectFossil.Match
{
    // An extraction point: a landing pad with a beacon pillar (red while closed, green when open) and a rescue
    // helicopter. When extraction opens the chopper flies in with its searchlight on, then hovers low over the pad
    // with a rope ladder down, kicking up dust. Its rotor noise carries: every few seconds it draws dinosaurs toward
    // the pad, so the wait to get aboard is a fight. When someone makes it, it lifts off with them on the ladder.
    // Uses the bought helicopter model where this machine has one (BoughtArt), primitives otherwise.
    public class ExtractionZone : MonoBehaviour
    {
        public float radius = 8f;
        public float approachSeconds = 10f;  // flight in from the sky
        public float noiseInterval   = 6f;
        public float noiseRadius     = 70f;

        // (position, radius): the helicopter is making noise. MatchManager passes it on to the dinosaurs.
        public static event Action<Vector3, float> Noise;

        // Down over the pad with the ladder out: people inside the zone can board.
        public bool HelicopterLanded => _landed;
        // Flying (arriving, hovering or leaving); presentation hangs the rotor sound on it.
        public bool HelicopterActive => _open == true && _heli != null && _heli.gameObject.activeSelf;
        public Transform Helicopter  => _heli;
        public bool IsLeaving        => _leaving;

        private static readonly Color ClosedColor = new Color(0.8f, 0.2f, 0.15f);
        private static readonly Color OpenColor   = new Color(0.2f, 0.9f, 0.3f);

        private Renderer[] _beaconRenderers;
        private Renderer   _pad;
        private bool?     _open;
        private Transform _heli;
        private Transform _rotor;
        private Transform _tailRotor;
        private Transform _ladder;
        private Transform _ladderGrip;   // where a rescued player hangs on
        private Renderer  _navRed, _navGreen, _strobe;
        private Light     _searchlight;
        private ParticleSystem _dust;
        private float     _flight;   // 0 = high above, 1 = hovering over the pad
        private bool      _landed;
        private bool      _leaving;
        private float     _leaveTime;
        private float     _noiseTimer;
        private float     _time;
        private Transform _passenger;

        private const float SkyHeight   = 70f;
        private const float HoverHeight = 5.5f;  // skids this high: clear of heads and cameras, ladder to the ground
        private const float ApproachX   = -60f;

        public static ExtractionZone Create(Vector3 position, float radius, Transform parent)
        {
            var root = new GameObject("ExtractionZone");
            root.transform.SetParent(parent, false);
            root.transform.position = position;

            // The pad is marked by a ring of stakes with coloured tips, each set into the ground where it stands. A
            // flat disc didn't follow the slope: on a hillside it hung in the air on one side like a red plate.
            var tips = new System.Collections.Generic.List<Renderer>();
            const int Stakes = 10;
            for (int i = 0; i < Stakes; i++)
            {
                float a = i * Mathf.PI * 2f / Stakes;
                Vector3 at = Ground(position + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * radius);
                var stake = Placeholder.Primitive(PrimitiveType.Cylinder);
                stake.name = "Stake";
                Destroy(stake.GetComponent<Collider>());
                stake.transform.SetParent(root.transform, true);
                stake.transform.position   = at + Vector3.up * 0.35f;
                stake.transform.localScale = new Vector3(0.07f, 0.45f, 0.07f);
                stake.GetComponent<Renderer>().sharedMaterial = Placeholder.Lit(new Color(0.3f, 0.22f, 0.14f));
                var tip = Placeholder.Primitive(PrimitiveType.Cube);
                tip.name = "Flag";
                Destroy(tip.GetComponent<Collider>());
                tip.transform.SetParent(root.transform, true);
                tip.transform.position   = at + new Vector3(0f, 0.72f, 0f) + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 0.08f;
                tip.transform.rotation   = Quaternion.Euler(0f, -a * Mathf.Rad2Deg, 0f);
                tip.transform.localScale = new Vector3(0.18f, 0.12f, 0.02f);
                tips.Add(tip.GetComponent<Renderer>());
            }

            // The beacon: a slim column beside the pad, standing on the ground there, seen from across the island.
            Vector3 beaconFoot = Ground(position + new Vector3(radius + 1f, 0f, 0f));
            var pillar = Placeholder.Primitive(PrimitiveType.Cylinder);
            pillar.name = "Beacon";
            Destroy(pillar.GetComponent<Collider>());
            pillar.transform.SetParent(root.transform, true);
            pillar.transform.position   = beaconFoot + Vector3.up * 20f;
            pillar.transform.localScale = new Vector3(0.3f, 20f, 0.3f);
            tips.Add(pillar.GetComponent<Renderer>());

            var zone = root.AddComponent<ExtractionZone>();
            zone.radius = radius;
            zone._beaconRenderers = tips.ToArray();
            zone.BuildHelicopter();
            zone.SetOpen(false);
            return zone;
        }

        // The terrain under a point (or the point itself if nothing is below or above it).
        private static Vector3 Ground(Vector3 p)
        {
            if (Physics.Raycast(p + Vector3.up * 60f, Vector3.down, out var hit, 200f, Physics.DefaultRaycastLayers,
                                QueryTriggerInteraction.Ignore) && hit.collider is TerrainCollider)
                return hit.point;
            var t = Terrain.activeTerrain;
            if (t != null) p.y = t.SampleHeight(p) + t.transform.position.y;
            return p;
        }

        public bool Contains(Vector3 position)
        {
            Vector3 d = position - transform.position;
            d.y = 0f;
            return d.sqrMagnitude <= radius * radius;
        }

        public void SetOpen(bool open)
        {
            if (_open == open) return;
            _open = open;
            if (_beaconRenderers != null)
                foreach (var r in _beaconRenderers)
                    r.material.color = open ? OpenColor : ClosedColor;
            // The pad fills the view while boarding: a muted tint of the beacon colour, not a glowing floor.
            if (_pad != null) _pad.material.color = Color.Lerp(open ? OpenColor : ClosedColor, new Color(0.3f, 0.3f, 0.28f), 0.6f);
            if (_heli != null) _heli.gameObject.SetActive(open);
        }

        // The rescue: the player grabs the ladder and the helicopter climbs away with them.
        public void LiftOff(Transform passenger)
        {
            if (_leaving || _heli == null) return;
            _leaving   = true;
            _leaveTime = 0f;
            _passenger = passenger;
            if (passenger == null) return;

            var cc = passenger.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false; // it would fight the parenting
            var controller = passenger.GetComponent<PlayerController>();
            if (controller != null) controller.enabled = false;
            passenger.SetParent(_ladderGrip, true);
            passenger.localPosition = Vector3.zero;
        }

        private void Update()
        {
            if (_open != true || _heli == null) return;
            _time += Time.deltaTime;

            if (_leaving) UpdateLeaving();
            else if (_flight < 1f) UpdateArrival();
            else UpdateHover();

            float spin = _landed || _leaving ? 1100f : 1300f;
            if (_rotor != null)     _rotor.Rotate(_rotorAxis, spin * Time.deltaTime, Space.Self);
            if (_tailRotor != null) _tailRotor.Rotate(_tailAxis, spin * 1.6f * Time.deltaTime, Space.Self);
            // Blades only show as a ghost flicker inside the blur.
            bool ghost = Mathf.Repeat(_time * 7f, 1f) < 0.25f;
            foreach (var b in _blades) if (b != null) b.enabled = ghost;

            // Navigation lights blink; the white strobe flashes twice a second.
            SetGlow(_navRed,   new Color(1f, 0.1f, 0.05f), Mathf.Repeat(_time, 1.2f) < 0.6f ? 6f : 0.3f);
            SetGlow(_navGreen, new Color(0.1f, 1f, 0.2f),  Mathf.Repeat(_time + 0.6f, 1.2f) < 0.6f ? 6f : 0.3f);
            SetGlow(_strobe,   Color.white,                Mathf.Repeat(_time, 0.5f) < 0.06f ? 12f : 0f);

            if (!_landed || _leaving) return;
            _noiseTimer -= Time.deltaTime;
            if (_noiseTimer <= 0f)
            {
                _noiseTimer = noiseInterval;
                Noise?.Invoke(transform.position, noiseRadius);
            }
        }

        // Fly in along a descending curve, nose down, and flare over the pad.
        private void UpdateArrival()
        {
            _flight = Mathf.Min(1f, _flight + Time.deltaTime / Mathf.Max(1f, approachSeconds));
            float t = 1f - (1f - _flight) * (1f - _flight); // ease out
            _heli.localPosition = new Vector3((1f - t) * ApproachX, Mathf.Lerp(SkyHeight, HoverHeight, t), 0f);
            float pitch = _flight < 0.8f ? 10f : Mathf.Lerp(10f, -8f, (_flight - 0.8f) / 0.2f); // flare at the end
            _heli.localRotation = Quaternion.Euler(0f, 90f, 0f) * Quaternion.Euler(pitch, 0f, 0f);
            if (_ladder != null) _ladder.gameObject.SetActive(false);
            SetDust(_heli.localPosition.y < 25f);

            if (_flight >= 1f)
            {
                _landed = true;
                _noiseTimer = 0f;
                if (_ladder != null) _ladder.gameObject.SetActive(true);
            }
        }

        // Holding over the pad: a gentle bob and sway, searchlight sweeping the treeline.
        private void UpdateHover()
        {
            float bob  = Mathf.Sin(_time * 1.3f) * 0.25f;
            float roll = Mathf.Sin(_time * 0.9f) * 2.5f;
            float yaw  = Mathf.Sin(_time * 0.35f) * 6f;
            _heli.localPosition = new Vector3(0f, HoverHeight + bob, 0f);
            _heli.localRotation = Quaternion.Euler(0f, 90f + yaw, 0f) * Quaternion.Euler(-2f, 0f, roll);
            if (_searchlight != null)
                _searchlight.transform.localRotation = Quaternion.Euler(55f + Mathf.Sin(_time * 0.5f) * 15f,
                                                                        Mathf.Sin(_time * 0.3f) * 60f, 0f);
            SetDust(true);
        }

        // Climb out, nose down and accelerating, with whoever made it hanging off the ladder.
        private void UpdateLeaving()
        {
            _leaveTime += Time.deltaTime;
            float t = _leaveTime;
            Vector3 start = new Vector3(0f, HoverHeight, 0f);
            _heli.localPosition = start + new Vector3(t * t * 1.6f, t * 4f + t * t * 0.5f, 0f);
            float pitch = Mathf.Min(14f, t * 6f);
            _heli.localRotation = Quaternion.Euler(0f, 90f, 0f) * Quaternion.Euler(pitch, 0f, Mathf.Sin(t * 1.5f) * 3f);
            SetDust(_heli.localPosition.y < 18f);
        }

        private void SetDust(bool on)
        {
            if (_dust == null) return;
            // The dust stays on the ground under the helicopter, not up in the air with it.
            _dust.transform.position = new Vector3(_heli.position.x, transform.position.y + 0.2f, _heli.position.z);
            var emission = _dust.emission;
            float height = Mathf.Max(0f, _heli.position.y - transform.position.y);
            emission.rateOverTime = on ? Mathf.Lerp(90f, 10f, height / 25f) : 0f;
        }

        private static void SetGlow(Renderer r, Color c, float intensity)
        {
            if (r == null) return;
            var m = r.material;
            m.color = intensity > 1f ? c : c * 0.3f;
            m.SetColor("_EmissionColor", c * intensity);
        }

        // ── Model ──────────────────────────────────────────────────────────────

        // Where the shared fittings go on whichever body the helicopter has (helicopter-local, nose along +Z).
        private struct Fittings
        {
            public Vector3 lamp;      // searchlight under the nose
            public Vector3 door;      // top of the rope ladder, on the right side
            public Vector3 boxCenter, boxSize;
        }

        // The bought model when this machine has one (see BoughtArt), otherwise one built from primitives. Either way
        // it gets a searchlight, a rope ladder on the right side, a collider and rotor dust.
        private void BuildHelicopter()
        {
            _heli = new GameObject("Helicopter").transform;
            _heli.SetParent(transform, false);
            _heli.localPosition = new Vector3(ApproachX, SkyHeight, 0f);
            _heli.localRotation = Quaternion.Euler(0f, 90f, 0f);

            var art = BoughtArt.Current;
            var fit = art != null && art.helicopter != null ? BuildBoughtBody(art) : BuildPrimitiveBody();

            // Searchlight under the nose.
            var lamp = new GameObject("Searchlight").transform;
            lamp.SetParent(_heli, false);
            lamp.localPosition = fit.lamp;
            lamp.localRotation = Quaternion.Euler(55f, 0f, 0f);
            _searchlight = lamp.gameObject.AddComponent<Light>();
            _searchlight.type      = LightType.Spot;
            _searchlight.color     = new Color(1f, 0.95f, 0.85f);
            _searchlight.intensity = 60f;
            _searchlight.range     = 60f;
            _searchlight.spotAngle = 34f;
            _searchlight.shadows   = LightShadows.None;

            // Rope ladder from the door to the ground.
            _ladder = new GameObject("Ladder").transform;
            _ladder.SetParent(_heli, false);
            _ladder.localPosition = fit.door;
            var rope = new Color(0.45f, 0.35f, 0.2f);
            float length = fit.door.y + HoverHeight - 0.1f;
            Part(PrimitiveType.Cylinder, _ladder, new Vector3(0f, -length * 0.5f, -0.3f), Vector3.zero, new Vector3(0.05f, length * 0.5f, 0.05f), rope);
            Part(PrimitiveType.Cylinder, _ladder, new Vector3(0f, -length * 0.5f, 0.3f),  Vector3.zero, new Vector3(0.05f, length * 0.5f, 0.05f), rope);
            for (float y = -0.4f; y > -length; y -= 0.45f)
                Part(PrimitiveType.Cube, _ladder, new Vector3(0f, y, 0f), Vector3.zero, new Vector3(0.06f, 0.05f, 0.62f), rope);
            _ladderGrip = new GameObject("Grip").transform;
            _ladderGrip.SetParent(_ladder, false);
            _ladderGrip.localPosition = new Vector3(0.25f, -length + 1.4f, 0f);
            _ladderGrip.localRotation = Quaternion.Euler(0f, -90f, 0f); // facing the ladder
            _ladder.gameObject.SetActive(false);

            // The body is solid for the camera (it won't clip inside), and high enough that nobody walks into it.
            var box = _heli.gameObject.AddComponent<BoxCollider>();
            box.center = fit.boxCenter;
            box.size   = fit.boxSize;

            _dust = MakeDust(transform);
            _heli.gameObject.SetActive(false);
        }

        // ── Bought model ──

        // Spin axes of the bought model's rotors, in each rotor's own space.
        private Vector3 _rotorAxis = Vector3.up, _tailAxis = Vector3.right;

        private Fittings BuildBoughtBody(BoughtArt art)
        {
            var model = Instantiate(art.helicopter, _heli, false);
            model.name = art.helicopter.name;
            var t = model.transform;
            t.localRotation = Quaternion.Euler(0f, art.helicopterYaw, 0f);
            t.localScale    = Vector3.one;
            var b = ModelFit.MeasureLocal(model, _heli);
            float s = b.size.z > 0.01f ? art.helicopterLength / b.size.z : 1f;
            t.localScale = Vector3.one * s;
            b = ModelFit.MeasureLocal(model, _heli);
            t.localPosition = new Vector3(-b.center.x, -b.min.y, -b.center.z); // skids on the pivot, centred
            b = ModelFit.MeasureLocal(model, _heli);

            FindRotors(t);
            if (_rotor != null)
            {
                // The blades spin too fast for a camera to see: a faint disc where they sweep.
                Vector3 hub = _heli.InverseTransformPoint(_rotor.position);
                var rb = ModelFit.MeasureLocal(_rotor.gameObject, _heli);
                float diameter = Mathf.Max(rb.size.x, rb.size.z);
                if (diameter < b.size.z * 0.5f) diameter = b.size.z * 0.9f; // blades drawn by bones: guess from the body
                _discs.Add(Disc(_heli, new Vector3(hub.x, hub.y + 0.05f, hub.z), Vector3.zero, diameter, 0.16f));
            }

            return new Fittings
            {
                lamp      = new Vector3(0f, b.min.y + 0.5f, b.max.z * 0.7f),
                door      = new Vector3(b.max.x * 0.8f + 0.1f, b.min.y + b.size.y * 0.4f, b.center.z + b.size.z * 0.1f),
                boxCenter = new Vector3(0f, b.min.y + b.size.y * 0.4f, b.center.z),
                boxSize   = new Vector3(Mathf.Min(b.size.x, 3f), b.size.y * 0.6f, b.size.z * 0.6f),
            };
        }

        // Rotors are found by name (main rotor highest up, tail rotor farthest back). A part inside another match
        // (blades under a hub) turns with it, so only the outermost matches spin.
        private void FindRotors(Transform model)
        {
            var found = new System.Collections.Generic.List<Transform>();
            foreach (var c in model.GetComponentsInChildren<Transform>(true))
            {
                if (c == model) continue;
                string n = c.name.ToLowerInvariant();
                if (!(n.Contains("rotor") || n.Contains("blade") || n.Contains("prop") || n.Contains("fan"))) continue;
                bool inside = false;
                for (var p = c.parent; p != null && p != model; p = p.parent)
                    if (found.Contains(p)) { inside = true; break; }
                if (!inside) found.Add(c);
            }
            if (found.Count == 0) { Debug.Log("[ExtractionZone] Bought helicopter: no rotor parts found by name."); return; }

            Transform main = null, tail = null;
            foreach (var c in found)
            {
                Vector3 p = _heli.InverseTransformPoint(c.position);
                if (main == null || p.y > _heli.InverseTransformPoint(main.position).y) main = c;
            }
            foreach (var c in found)
            {
                if (c == main || c.IsChildOf(main) || main.IsChildOf(c)) continue;
                Vector3 p = _heli.InverseTransformPoint(c.position);
                if (tail == null || p.z < _heli.InverseTransformPoint(tail.position).z) tail = c;
            }
            _rotor = main;
            _tailRotor = tail;
            _rotorAxis = main.InverseTransformDirection(_heli.up).normalized;
            if (tail != null) _tailAxis = tail.InverseTransformDirection(_heli.right).normalized;
        }

        // ── Primitive model ──

        // A rescue chopper from primitives: rounded cabin with an open side door, glass nose, engine hump, tapered
        // tail with fin and stabiliser, skids, four-blade main rotor, tail rotor and blinking lights.
        private Fittings BuildPrimitiveBody()
        {

            // Matte military olive with a darker band: bright rescue orange read as a toy against the real forest.
            var body   = new Color(0.22f, 0.25f, 0.19f);
            var stripe = new Color(0.15f, 0.17f, 0.13f);
            var dark   = new Color(0.07f, 0.07f, 0.08f);
            var metal  = new Color(0.26f, 0.27f, 0.28f);
            var glass  = new Color(0.06f, 0.08f, 0.1f);

            // Cabin and nose.
            Part(PrimitiveType.Capsule,  _heli, new Vector3(0f, 1.9f, 0.2f),  new Vector3(90f, 0f, 0f), new Vector3(2.5f, 2.3f, 2.3f), body);
            Part(PrimitiveType.Sphere,   _heli, new Vector3(0f, 1.95f, 2.1f), Vector3.zero,               new Vector3(2.0f, 1.7f, 1.5f), glass);
            Part(PrimitiveType.Cube,     _heli, new Vector3(0f, 3.05f, -0.3f), Vector3.zero,              new Vector3(1.3f, 0.6f, 2.2f), metal);
            // White band down both sides, and the dark open door on the right (the ladder side).
            Part(PrimitiveType.Cube,     _heli, new Vector3(1.2f, 1.6f, 0f),  Vector3.zero,               new Vector3(0.06f, 0.25f, 3.6f), stripe);
            Part(PrimitiveType.Cube,     _heli, new Vector3(-1.2f, 1.6f, 0f), Vector3.zero,               new Vector3(0.06f, 0.25f, 3.6f), stripe);
            Part(PrimitiveType.Cube,     _heli, new Vector3(1.22f, 2.0f, -0.2f), Vector3.zero,            new Vector3(0.06f, 1.2f, 1.4f), dark);

            // Tail: a boom narrowing toward the fin, a horizontal stabiliser, and the fin.
            Part(PrimitiveType.Cylinder, _heli, new Vector3(0f, 2.3f, -3.2f), new Vector3(90f, 0f, 0f),  new Vector3(0.6f, 1.6f, 0.6f), body);
            Part(PrimitiveType.Cylinder, _heli, new Vector3(0f, 2.45f, -5.6f), new Vector3(90f, 0f, 0f), new Vector3(0.35f, 1.1f, 0.35f), body);
            Part(PrimitiveType.Cube,     _heli, new Vector3(0f, 2.45f, -5.9f), Vector3.zero,              new Vector3(2.0f, 0.08f, 0.5f), body);
            Part(PrimitiveType.Cube,     _heli, new Vector3(0f, 3.1f, -6.6f), new Vector3(-20f, 0f, 0f), new Vector3(0.12f, 1.4f, 0.7f), body);
            Part(PrimitiveType.Cube,     _heli, new Vector3(0f, 3.4f, -6.75f), new Vector3(-20f, 0f, 0f), new Vector3(0.14f, 0.25f, 0.72f), stripe);

            // Skids and struts.
            Part(PrimitiveType.Cylinder, _heli, new Vector3(-1.05f, 0.12f, 0.2f), new Vector3(90f, 0f, 0f), new Vector3(0.12f, 2.1f, 0.12f), dark);
            Part(PrimitiveType.Cylinder, _heli, new Vector3(1.05f, 0.12f, 0.2f),  new Vector3(90f, 0f, 0f), new Vector3(0.12f, 2.1f, 0.12f), dark);
            foreach (float x in new[] { -0.85f, 0.85f })
            foreach (float z in new[] { -0.8f, 1.1f })
                Part(PrimitiveType.Cube, _heli, new Vector3(x, 0.55f, z), new Vector3(0f, 0f, x > 0f ? 25f : -25f),
                     new Vector3(0.08f, 0.9f, 0.08f), dark);

            // Main rotor: mast, hub, four blades.
            Part(PrimitiveType.Cylinder, _heli, new Vector3(0f, 3.55f, -0.2f), Vector3.zero, new Vector3(0.18f, 0.2f, 0.18f), dark);
            _rotor = new GameObject("Rotor").transform;
            _rotor.SetParent(_heli, false);
            _rotor.localPosition = new Vector3(0f, 3.8f, -0.2f);
            Part(PrimitiveType.Cylinder, _rotor, Vector3.zero, Vector3.zero, new Vector3(0.45f, 0.1f, 0.45f), metal);
            _blades.Add(Part(PrimitiveType.Cube, _rotor, Vector3.zero, new Vector3(0f, 0f, 0f),  new Vector3(11f, 0.04f, 0.32f), dark));
            _blades.Add(Part(PrimitiveType.Cube, _rotor, Vector3.zero, new Vector3(0f, 90f, 0f), new Vector3(11f, 0.04f, 0.32f), dark));
            // At full speed a camera sees a rotor as a faint blurred disc, not four sticks stepping round.
            _discs.Add(Disc(_heli, new Vector3(0f, 3.8f, -0.2f), Vector3.zero, 11f, 0.22f));

            _tailRotor = new GameObject("TailRotor").transform;
            _tailRotor.SetParent(_heli, false);
            _tailRotor.localPosition = new Vector3(0.22f, 2.9f, -6.6f);
            _blades.Add(Part(PrimitiveType.Cube, _tailRotor, Vector3.zero, Vector3.zero,              new Vector3(0.04f, 1.9f, 0.18f), dark));
            _blades.Add(Part(PrimitiveType.Cube, _tailRotor, Vector3.zero, new Vector3(90f, 0f, 0f), new Vector3(0.04f, 1.9f, 0.18f), dark));
            _discs.Add(Disc(_heli, new Vector3(0.22f, 2.9f, -6.6f), new Vector3(0f, 0f, 90f), 1.9f, 0.18f));

            // Lights: red port, green starboard, a white strobe on the tail.
            _navRed   = Glow(_heli, new Vector3(-1.1f, 1.3f, 1.6f),  0.18f);
            _navGreen = Glow(_heli, new Vector3(1.1f, 1.3f, 1.6f),   0.18f);
            _strobe   = Glow(_heli, new Vector3(0f, 3.85f, -6.85f),  0.16f);

            return new Fittings
            {
                lamp      = new Vector3(0f, 0.6f, 1.9f),
                door      = new Vector3(1.35f, 1.3f, -0.2f),
                boxCenter = new Vector3(0f, 2.0f, 0f),
                boxSize   = new Vector3(2.5f, 2.4f, 5f),
            };
        }

        private readonly System.Collections.Generic.List<Renderer> _blades = new System.Collections.Generic.List<Renderer>();
        private readonly System.Collections.Generic.List<Renderer> _discs  = new System.Collections.Generic.List<Renderer>();

        // A thin see-through disc where a spinning rotor sweeps.
        private static Renderer Disc(Transform parent, Vector3 pos, Vector3 euler, float diameter, float alpha)
        {
            var r = Part(PrimitiveType.Cylinder, parent, pos, euler, new Vector3(diameter, 0.004f, diameter), Color.black);
            var clear = Resources.Load<Material>("Shaders/PlainLitClear");
            if (clear != null)
            {
                var m = new Material(clear);
                var c = new Color(0.08f, 0.08f, 0.08f, alpha);
                m.color = c;
                if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
                r.sharedMaterial = m;
            }
            else r.enabled = false; // no see-through material: plain blades only
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return r;
        }

        private static Renderer Glow(Transform parent, Vector3 pos, float size)
        {
            var r = Part(PrimitiveType.Sphere, parent, pos, Vector3.zero, Vector3.one * size, Color.black);
            r.material.EnableKeyword("_EMISSION");
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return r;
        }

        private static Renderer Part(PrimitiveType type, Transform parent, Vector3 pos, Vector3 euler, Vector3 scale, Color color)
        {
            var go = Placeholder.Primitive(type);
            Destroy(go.GetComponent<Collider>()); // one box on the body stands in for all the parts
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = Quaternion.Euler(euler);
            go.transform.localScale    = scale;
            var r = go.GetComponent<Renderer>();
            r.material.color = color;
            return r;
        }

        // Rotor wash: sandy puffs blown outward along the ground.
        private static ParticleSystem MakeDust(Transform parent)
        {
            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null) return null;

            var go = new GameObject("RotorDust");
            go.transform.SetParent(parent, false);
            go.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f); // emit in the ground plane
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.loop            = true;
            main.startLifetime   = new ParticleSystem.MinMaxCurve(1.2f, 2.2f);
            main.startSpeed      = new ParticleSystem.MinMaxCurve(6f, 11f);
            main.startSize       = new ParticleSystem.MinMaxCurve(1.5f, 3.5f);
            main.startColor      = new Color(0.72f, 0.64f, 0.5f, 0.35f);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles    = 400;
            main.gravityModifier = -0.05f;

            var emission = ps.emission;
            emission.rateOverTime = 0f;

            var shape = ps.shape;
            shape.shapeType       = ParticleSystemShapeType.Circle;
            shape.radius          = 1.5f;
            shape.radiusThickness = 0f; // from the rim, so it blows outward

            var col = ps.colorOverLifetime;
            col.enabled = true;
            var fade = new Gradient();
            fade.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                         new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(0f, 1f) });
            col.color = fade;

            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 1.6f));

            var mat = new Material(shader);
            mat.SetTexture("_BaseMap", SoftDot());
            mat.SetFloat("_Surface", 1f);   // transparent
            mat.SetFloat("_Blend", 0f);     // alpha
            mat.SetFloat("_ZWrite", 0f);
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.material = mat;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            ps.Play();
            return ps;
        }

        private static Texture2D _softDot;

        private static Texture2D SoftDot()
        {
            if (_softDot != null) return _softDot;
            const int n = 32;
            _softDot = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float dx = (x + 0.5f) / n * 2f - 1f, dy = (y + 0.5f) / n * 2f - 1f;
                float a = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy));
                _softDot.SetPixel(x, y, new Color(1f, 1f, 1f, a * a));
            }
            _softDot.Apply();
            return _softDot;
        }
    }
}
