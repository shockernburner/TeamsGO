using System;
using UnityEngine;

namespace ProjectFossil.Match
{
    // An extraction point: a landing pad with a beacon pillar (red while closed, green when open) and a rescue
    // helicopter that flies in when extraction opens, lands, and keeps its rotors turning. The rotor noise carries:
    // every few seconds it draws wandering dinosaurs toward the pad, so the last run to the chopper is never quiet.
    // Placeholder art from primitives, like the rest of the prototype.
    public class ExtractionZone : MonoBehaviour
    {
        public float radius = 8f;
        public float approachSeconds = 10f;  // flight in from the sky
        public float noiseInterval   = 6f;
        public float noiseRadius     = 70f;

        // (position, radius): the landed helicopter is making noise. MatchManager passes it on to the dinosaurs.
        public static event Action<Vector3, float> Noise;

        public bool HelicopterLanded => _landed;

        private static readonly Color ClosedColor = new Color(0.8f, 0.2f, 0.15f);
        private static readonly Color OpenColor   = new Color(0.2f, 0.9f, 0.3f);

        private Renderer[] _beaconRenderers;
        private bool?     _open;
        private Transform _heli;
        private Transform _rotor;
        private Transform _tailRotor;
        private float     _flight;   // 0 = high above, 1 = landed
        private bool      _landed;
        private float     _noiseTimer;

        private const float SkyHeight = 70f;

        public static ExtractionZone Create(Vector3 position, float radius, Transform parent)
        {
            var root = new GameObject("ExtractionZone");
            root.transform.SetParent(parent, false);
            root.transform.position = position;

            var disc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            disc.name = "Disc";
            Destroy(disc.GetComponent<Collider>());
            disc.transform.SetParent(root.transform, false);
            disc.transform.localScale = new Vector3(radius * 2f, 0.05f, radius * 2f);

            var pillar = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            pillar.name = "Beacon";
            Destroy(pillar.GetComponent<Collider>());
            pillar.transform.SetParent(root.transform, false);
            pillar.transform.localPosition = new Vector3(radius + 1f, 20f, 0f); // beside the pad, clear of the rotor
            pillar.transform.localScale    = new Vector3(0.6f, 20f, 0.6f);

            var zone = root.AddComponent<ExtractionZone>();
            zone.radius = radius;
            zone._beaconRenderers = new[] { disc.GetComponent<Renderer>(), pillar.GetComponent<Renderer>() };
            zone.BuildHelicopter();
            zone.SetOpen(false);
            return zone;
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
            if (_heli != null) _heli.gameObject.SetActive(open);
        }

        private void Update()
        {
            if (_open != true || _heli == null) return;

            // Fly in along a slow curve, flaring over the pad.
            if (_flight < 1f)
            {
                _flight = Mathf.Min(1f, _flight + Time.deltaTime / Mathf.Max(1f, approachSeconds));
                float t = 1f - (1f - _flight) * (1f - _flight); // ease out
                _heli.localPosition = new Vector3((1f - t) * -40f, Mathf.Lerp(SkyHeight, 0f, t), 0f);
                _heli.localRotation = Quaternion.Euler(Mathf.Lerp(12f, 0f, t), 90f, 0f);
                if (_flight >= 1f) { _landed = true; _noiseTimer = 0f; }
            }

            float spin = _landed ? 900f : 1300f;
            if (_rotor != null)     _rotor.Rotate(Vector3.up, spin * Time.deltaTime, Space.Self);
            if (_tailRotor != null) _tailRotor.Rotate(Vector3.right, spin * 1.6f * Time.deltaTime, Space.Self);

            if (!_landed) return;
            _noiseTimer -= Time.deltaTime;
            if (_noiseTimer <= 0f)
            {
                _noiseTimer = noiseInterval;
                Noise?.Invoke(transform.position, noiseRadius);
            }
        }

        // A small rescue chopper from primitives: cabin, windscreen, tail boom, skids, main and tail rotors.
        private void BuildHelicopter()
        {
            _heli = new GameObject("Helicopter").transform;
            _heli.SetParent(transform, false);
            _heli.localPosition = new Vector3(-40f, SkyHeight, 0f);
            _heli.localRotation = Quaternion.Euler(0f, 90f, 0f);

            var body  = new Color(0.85f, 0.55f, 0.12f); // rescue orange
            var dark  = new Color(0.12f, 0.12f, 0.13f);
            var glass = new Color(0.25f, 0.4f, 0.5f);

            Part(PrimitiveType.Capsule,  _heli, new Vector3(0f, 1.9f, 0f),    new Vector3(90f, 0f, 0f), new Vector3(2.4f, 2.4f, 2.4f), body);
            Part(PrimitiveType.Sphere,   _heli, new Vector3(0f, 2.1f, 1.7f),  Vector3.zero,               new Vector3(1.9f, 1.5f, 1.4f), glass);
            Part(PrimitiveType.Cylinder, _heli, new Vector3(0f, 2.3f, -3.8f), new Vector3(90f, 0f, 0f), new Vector3(0.45f, 2.6f, 0.45f), body);
            Part(PrimitiveType.Cube,     _heli, new Vector3(0f, 2.9f, -6.2f), Vector3.zero,               new Vector3(0.15f, 1.3f, 0.8f), body);
            Part(PrimitiveType.Cube,     _heli, new Vector3(-1f, 0.15f, 0f),  Vector3.zero,               new Vector3(0.15f, 0.15f, 3.6f), dark);
            Part(PrimitiveType.Cube,     _heli, new Vector3(1f, 0.15f, 0f),   Vector3.zero,               new Vector3(0.15f, 0.15f, 3.6f), dark);
            Part(PrimitiveType.Cube,     _heli, new Vector3(-0.8f, 0.6f, 0.9f), Vector3.zero,             new Vector3(0.1f, 0.9f, 0.1f), dark);
            Part(PrimitiveType.Cube,     _heli, new Vector3(0.8f, 0.6f, 0.9f),  Vector3.zero,             new Vector3(0.1f, 0.9f, 0.1f), dark);
            Part(PrimitiveType.Cube,     _heli, new Vector3(-0.8f, 0.6f, -0.9f), Vector3.zero,            new Vector3(0.1f, 0.9f, 0.1f), dark);
            Part(PrimitiveType.Cube,     _heli, new Vector3(0.8f, 0.6f, -0.9f),  Vector3.zero,            new Vector3(0.1f, 0.9f, 0.1f), dark);

            _rotor = new GameObject("Rotor").transform;
            _rotor.SetParent(_heli, false);
            _rotor.localPosition = new Vector3(0f, 3.35f, 0f);
            Part(PrimitiveType.Cylinder, _rotor, Vector3.zero, Vector3.zero, new Vector3(0.3f, 0.15f, 0.3f), dark);
            Part(PrimitiveType.Cube, _rotor, Vector3.zero, Vector3.zero,               new Vector3(10f, 0.05f, 0.35f), dark);
            Part(PrimitiveType.Cube, _rotor, Vector3.zero, new Vector3(0f, 90f, 0f), new Vector3(10f, 0.05f, 0.35f), dark);

            _tailRotor = new GameObject("TailRotor").transform;
            _tailRotor.SetParent(_heli, false);
            _tailRotor.localPosition = new Vector3(0.2f, 2.9f, -6.3f);
            Part(PrimitiveType.Cube, _tailRotor, Vector3.zero, Vector3.zero, new Vector3(0.04f, 1.8f, 0.2f), dark);

            _heli.gameObject.SetActive(false);
        }

        private static void Part(PrimitiveType type, Transform parent, Vector3 pos, Vector3 euler, Vector3 scale, Color color)
        {
            var go = GameObject.CreatePrimitive(type);
            Destroy(go.GetComponent<Collider>()); // it lands on the pad; people walk into it to board
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = Quaternion.Euler(euler);
            go.transform.localScale    = scale;
            go.GetComponent<Renderer>().material.color = color;
        }
    }
}
