using System.Collections.Generic;
using UnityEngine;
using ProjectFossil.Core;

namespace ProjectFossil.Environment
{
    // Flocks of flying reptiles wheeling high over the island, crying now and then. Scenery only: they never
    // land or attack. Built from simple shapes until a real animated model replaces them; at this height a
    // dark crested silhouette with flapping wings reads well enough.
    public class Pterosaurs : MonoBehaviour
    {
        private const float KeepWithin = 480f; // a flock this far from the camera moves somewhere closer

        private class Bird
        {
            public Transform Root, LeftWing, RightWing;
            public float Phase, Radius, Height, FlapRate;
        }

        private class Flock
        {
            public Vector3 Centre;
            public float   Radius, Speed, Angle, Altitude, NextCall;
            public Vector3 Drift;
            public readonly List<Bird> Birds = new List<Bird>();
        }

        private readonly List<Flock> _flocks = new List<Flock>();
        private Material _skin, _wing;
        private System.Random _rng = new System.Random(311);
        private Transform _cam;

        public void Apply(Conditions c)
        {
            foreach (var f in _flocks)
                foreach (var b in f.Birds)
                    if (b.Root != null) Destroy(b.Root.gameObject);
            _flocks.Clear();

            int flocks = c.Time == DayTime.Night || c.Weather == Weather.Storm ? 0
                       : c.Weather == Weather.Rain || c.Weather == Weather.Fog ? 1
                       : c.Time == DayTime.Day ? 4 : 3;
            _rng = new System.Random(311 + (int)c.Time * 17 + (int)c.Weather);
            for (int i = 0; i < flocks; i++) _flocks.Add(MakeFlock());
        }

        public void Follow(Transform cam) => _cam = cam;

        private void Update()
        {
            if (_cam == null || _flocks.Count == 0) return;
            float t = Time.time;
            foreach (var f in _flocks)
            {
                f.Angle  += f.Speed / f.Radius * Time.deltaTime;
                f.Centre += f.Drift * Time.deltaTime;

                Vector3 toCam = _cam.position - f.Centre;
                toCam.y = 0f;
                if (toCam.magnitude > KeepWithin) Relocate(f);

                for (int i = 0; i < f.Birds.Count; i++)
                {
                    var b = f.Birds[i];
                    if (b.Root == null) continue;
                    float a = f.Angle + b.Phase;
                    float r = f.Radius + b.Radius;
                    Vector3 pos = f.Centre + new Vector3(Mathf.Cos(a) * r, f.Altitude + b.Height + Mathf.Sin(t * 0.4f + b.Phase) * 2f, Mathf.Sin(a) * r);
                    Vector3 heading = new Vector3(-Mathf.Sin(a), 0f, Mathf.Cos(a)); // counter-clockwise tangent
                    b.Root.position = pos;
                    b.Root.rotation = Quaternion.LookRotation(heading) * Quaternion.Euler(0f, 0f, 18f); // bank into the turn

                    // Flap in bursts, glide in between (placeholder birds only).
                    float gliding = Mathf.PerlinNoise(t * 0.25f, b.Phase * 3f);
                    float flap = gliding > 0.5f ? Mathf.Sin(t * b.FlapRate + b.Phase) * 32f : 6f;
                    if (b.LeftWing == null) continue; // a bought, animated model flaps by itself
                    b.LeftWing.localRotation  = Quaternion.Euler(0f, 0f,  flap);
                    b.RightWing.localRotation = Quaternion.Euler(0f, 0f, -flap);
                }

                if (t >= f.NextCall && f.Birds.Count > 0 && f.Birds[0].Root != null)
                {
                    f.NextCall = t + Range(9f, 24f);
                    if ((f.Birds[0].Root.position - _cam.position).magnitude < 260f)
                        AmbientEvents.RaiseSkyCall(f.Birds[0].Root.position);
                }
            }
        }

        private Flock MakeFlock()
        {
            var f = new Flock
            {
                Radius = Range(25f, 65f),
                Speed  = Range(9f, 14f),
                Angle  = Range(0f, Mathf.PI * 2f),
                Drift  = new Vector3(Range(-1.5f, 1.5f), 0f, Range(-1.5f, 1.5f)),
                NextCall = Time.time + Range(3f, 15f),
            };
            Relocate(f);
            int n = _rng.Next(3, 8);
            for (int i = 0; i < n; i++) f.Birds.Add(MakeBird(i, n));
            return f;
        }

        private void Relocate(Flock f)
        {
            Vector3 around = _cam != null ? _cam.position : Vector3.zero;
            float a = Range(0f, Mathf.PI * 2f), d = Range(80f, 300f);
            f.Centre = new Vector3(around.x + Mathf.Cos(a) * d, 0f, around.z + Mathf.Sin(a) * d);
            float ground = Terrain.activeTerrain != null ? Terrain.activeTerrain.SampleHeight(f.Centre) + Terrain.activeTerrain.transform.position.y : around.y;
            f.Altitude = Mathf.Max(ground, 0f) + Range(45f, 85f);
        }

        // A crested head, a slim body and two long wings that pivot at the shoulder. Wingspan about 5 m.
        private Bird MakeBird(int index, int count)
        {
            EnsureMaterials();
            var root = new GameObject("Pterosaur").transform;
            root.SetParent(transform, false);

            var bought = BoughtArt.Current != null ? BoughtArt.Current.flyer : null;
            if (bought != null && bought.HasModel)
            {
                var model = ModelFit.Spawn(bought, root);
                foreach (var r in model.GetComponentsInChildren<Renderer>())
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                if (bought.animator != null)
                {
                    var anim = model.GetComponentInChildren<Animator>();
                    if (anim == null) anim = model.AddComponent<Animator>();
                    anim.runtimeAnimatorController = bought.animator;
                    anim.applyRootMotion = false;
                    anim.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
                }
                root.localScale = Vector3.one * Range(0.85f, 1.2f);
                return new Bird
                {
                    Root = root,
                    Phase    = index * Mathf.PI * 2f / count + Range(-0.3f, 0.3f),
                    Radius   = Range(-6f, 6f),
                    Height   = Range(-5f, 5f),
                    FlapRate = Range(4f, 5.5f),
                };
            }

            Part(root, PrimitiveType.Capsule, _skin, new Vector3(0f, 0f, 0f),     new Vector3(0.35f, 0.65f, 0.3f), new Vector3(90f, 0f, 0f));
            Part(root, PrimitiveType.Sphere,  _skin, new Vector3(0f, 0.08f, 0.75f), new Vector3(0.26f, 0.24f, 0.32f), Vector3.zero);
            Part(root, PrimitiveType.Cube,    _skin, new Vector3(0f, 0.04f, 1.15f), new Vector3(0.07f, 0.07f, 0.7f), new Vector3(4f, 0f, 0f));   // beak
            Part(root, PrimitiveType.Cube,    _skin, new Vector3(0f, 0.2f, 0.45f), new Vector3(0.05f, 0.18f, 0.6f), new Vector3(-28f, 0f, 0f)); // crest
            Part(root, PrimitiveType.Cube,    _skin, new Vector3(0f, 0f, -0.75f), new Vector3(0.05f, 0.05f, 0.5f), Vector3.zero);                // tail

            var left  = Wing(root, -1f);
            var right = Wing(root,  1f);
            float s = Range(0.85f, 1.2f);
            root.localScale = Vector3.one * s;

            return new Bird
            {
                Root = root, LeftWing = left, RightWing = right,
                Phase    = index * Mathf.PI * 2f / count + Range(-0.3f, 0.3f),
                Radius   = Range(-6f, 6f),
                Height   = Range(-5f, 5f),
                FlapRate = Range(4f, 5.5f),
            };
        }

        private Transform Wing(Transform body, float side)
        {
            var pivot = new GameObject(side < 0 ? "LeftWing" : "RightWing").transform;
            pivot.SetParent(body, false);
            pivot.localPosition = new Vector3(0.15f * side, 0.05f, 0.25f);
            // Membrane: broad at the shoulder, narrowing to the tip, swept back.
            Part(pivot, PrimitiveType.Cube, _wing, new Vector3(1.05f * side, 0f, -0.1f), new Vector3(2.1f, 0.03f, 0.75f), new Vector3(0f, -8f * side, 0f));
            Part(pivot, PrimitiveType.Cube, _wing, new Vector3(2.35f * side, 0f, -0.3f), new Vector3(0.7f, 0.025f, 0.4f), new Vector3(0f, -20f * side, 0f));
            return pivot;
        }

        private static void Part(Transform parent, PrimitiveType type, Material mat, Vector3 pos, Vector3 scale, Vector3 euler)
        {
            var go = Placeholder.Primitive(type);
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = Quaternion.Euler(euler);
            go.transform.localScale    = scale;
            var r = go.GetComponent<Renderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
        }

        private void EnsureMaterials()
        {
            if (_skin != null) return;
            _skin = Placeholder.Lit(new Color(0.26f, 0.2f, 0.16f));
            _wing = Placeholder.Lit(new Color(0.36f, 0.26f, 0.2f));
        }

        private float Range(float a, float b) => a + (float)_rng.NextDouble() * (b - a);
    }
}
