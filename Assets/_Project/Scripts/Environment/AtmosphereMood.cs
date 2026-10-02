using System.Collections.Generic;
using UnityEngine;
using ProjectFossil.Core;

namespace ProjectFossil.Environment
{
    // The air between the trees: shafts of sunlight slanting through the canopy, and mist lying on the ground at
    // dawn, in fog and rain, and over water. Both live near the camera and are re-placed as it moves; neither
    // exists on open ground in a clear midday, where the real thing wouldn't either. Presentation only.
    public class AtmosphereMood : MonoBehaviour
    {
        private const int   ShaftCount    = 14;
        private const float ShaftNear     = 8f, ShaftFar = 48f; // metres from the camera
        private const float ShaftLength   = 22f, ShaftWidth = 2.4f;
        private const float MistRadius    = 45f;
        private const float MistPerSecond = 6f;                 // at full mist
        private const float MinShaftRise  = 0.55f;              // sine of the flattest shaft (about 33 degrees)

        private Material       _shaftMat;
        private Mesh           _shaftMesh;
        private readonly List<Transform> _shafts = new List<Transform>();
        private readonly List<Vector3>   _trees  = new List<Vector3>();
        private Vector3        _toSun = Vector3.up;
        private float          _shaftStrength;
        private ParticleSystem _mist;
        private float          _mistAmount, _mistDebt;
        private Color          _mistColor;

        // ── Setup for a match ──────────────────────────────────────────────────

        public void Apply(Conditions c, Light sun, Color sunColor, Color fog)
        {
            _toSun = sun != null ? -sun.transform.forward : Vector3.up;
            // A dawn sun barely above the horizon would lay its shafts along the ground; keep them standing.
            var flat = new Vector3(_toSun.x, 0f, _toSun.z);
            if (_toSun.y < MinShaftRise && flat.sqrMagnitude > 1e-4f)
                _toSun = flat.normalized * Mathf.Sqrt(1f - MinShaftRise * MinShaftRise) + Vector3.up * MinShaftRise;

            // Low sun makes the longest, brightest shafts; cloud scatters them; rain, storm, fog and night have none.
            _shaftStrength = c.Time == DayTime.Night ? 0f
                           : c.Weather == Weather.Clear  ? (c.Time == DayTime.Day ? 0.11f : 0.2f)
                           : c.Weather == Weather.Cloudy ? (c.Time == DayTime.Day ? 0.04f : 0.07f)
                           : 0f;
            _mistAmount = c.Weather == Weather.Fog ? 1.2f
                        : c.Weather == Weather.Rain || c.Weather == Weather.Storm ? 0.7f
                        : c.Time == DayTime.Dawn ? 1f
                        : c.Time == DayTime.Dusk || c.Time == DayTime.Night ? 0.5f
                        : 0.12f; // a clear day: only a breath over the water
            _mistColor = Color.Lerp(fog, Color.white, c.Time == DayTime.Night ? 0.05f : 0.35f);

            _trees.Clear();
            ViewBlockers.ForEachTree(_trees.Add);

            SetUpShafts(sunColor);
            SetUpMist(c);
        }

        // ── Sun shafts ─────────────────────────────────────────────────────────

        private void SetUpShafts(Color sunColor)
        {
            if (_shaftStrength <= 0f || _trees.Count == 0)
            {
                foreach (var s in _shafts) s.gameObject.SetActive(false);
                return;
            }
            if (_shaftMat == null)
            {
                _shaftMat = NewMaterial("Sun shafts", ShaftTexture(), additive: true);
                if (_shaftMat == null) return;
                _shaftMesh = StarMesh();
            }
            var tint = sunColor * _shaftStrength;
            tint.a = 1f;
            _shaftMat.SetColor("_BaseColor", tint);

            while (_shafts.Count < ShaftCount)
            {
                var go = new GameObject("SunShaft");
                go.transform.SetParent(transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = _shaftMesh;
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = _shaftMat;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
                _shafts.Add(go.transform);
            }
            // Spread the first placement out; Follow moves each one on as the camera leaves it behind.
            foreach (var s in _shafts) s.gameObject.SetActive(false);
        }

        // Each shaft stands in a gap beside a tree within reach of the camera, leaning towards the sun.
        private bool PlaceShaft(Transform shaft, Vector3 cam)
        {
            var terrain = Terrain.activeTerrain;
            if (terrain == null) return false;
            for (int attempt = 0; attempt < 24; attempt++)
            {
                var tree = _trees[Random.Range(0, _trees.Count)];
                Vector2 off = Random.insideUnitCircle.normalized * Random.Range(2f, 5f);
                var p = new Vector3(tree.x + off.x, 0f, tree.z + off.y);
                float d = Vector2.Distance(new Vector2(p.x, p.z), new Vector2(cam.x, cam.z));
                if (d < ShaftNear || d > ShaftFar) continue;
                p.y = terrain.SampleHeight(p) + terrain.transform.position.y - 1f;
                if (p.y < ViewBlockers.SurfaceAt(p)) continue; // not out of a river or lake
                shaft.position = p;
                shaft.rotation = Quaternion.FromToRotation(Vector3.up, _toSun) * Quaternion.Euler(0f, Random.Range(0f, 60f), 0f);
                float w = Random.Range(0.6f, 1.4f);
                shaft.localScale = new Vector3(w, Random.Range(0.8f, 1.15f), w);
                return true;
            }
            return false;
        }

        // Three cards crossed at 60 degrees round the shaft's axis; the shader fades whichever is seen edge-on.
        private static Mesh StarMesh()
        {
            var verts = new List<Vector3>(); var uvs = new List<Vector2>(); var normals = new List<Vector3>();
            var tris  = new List<int>();
            for (int i = 0; i < 3; i++)
            {
                var q = Quaternion.Euler(0f, i * 60f, 0f);
                Vector3 side = q * Vector3.right * (ShaftWidth * 0.5f), n = q * Vector3.forward;
                int b = verts.Count;
                verts.Add(-side); verts.Add(side); verts.Add(-side + Vector3.up * ShaftLength); verts.Add(side + Vector3.up * ShaftLength);
                uvs.Add(new Vector2(0f, 0f)); uvs.Add(new Vector2(1f, 0f)); uvs.Add(new Vector2(0f, 1f)); uvs.Add(new Vector2(1f, 1f));
                for (int k = 0; k < 4; k++) normals.Add(n);
                tris.AddRange(new[] { b, b + 2, b + 1, b + 1, b + 2, b + 3 });
            }
            var mesh = new Mesh { name = "SunShaft" };
            mesh.SetVertices(verts); mesh.SetUVs(0, uvs); mesh.SetNormals(normals); mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        // Soft across, with a few brighter rays; fades in under the canopy and out before it reaches the ground.
        private static Texture2D ShaftTexture()
        {
            const int w = 64, h = 128;
            var px  = new Color32[w * h];
            var rng = new System.Random(31);
            var rays = new float[w];
            for (int x = 0; x < w; x++) rays[x] = 0.65f + 0.35f * Mathf.PerlinNoise(x * 0.18f, 3.7f) + 0.1f * (float)rng.NextDouble();
            for (int y = 0; y < h; y++)
            {
                float v = (y + 0.5f) / h;
                float along = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.02f, 0.5f, v)) * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.7f, 1f, v)));
                for (int x = 0; x < w; x++)
                {
                    float u = (x + 0.5f) / w;
                    float across = Mathf.Pow(Mathf.Sin(u * Mathf.PI), 2f) * rays[x];
                    byte a = (byte)(Mathf.Clamp01(along * across) * 255f);
                    px[y * w + x] = new Color32(255, 255, 255, a);
                }
            }
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, true) { name = "SunShaft", wrapMode = TextureWrapMode.Clamp };
            tex.SetPixels32(px);
            tex.Apply(true);
            return tex;
        }

        // ── Ground mist ────────────────────────────────────────────────────────

        private void SetUpMist(Conditions c)
        {
            if (_mist == null)
            {
                var mat = NewMaterial("Mist", MistTexture(), additive: false);
                if (mat == null) return;
                var go = new GameObject("Mist");
                go.transform.SetParent(transform, false);
                _mist = go.AddComponent<ParticleSystem>();
                _mist.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                var r = go.GetComponent<ParticleSystemRenderer>();
                r.sharedMaterial = mat;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
                r.sortMode = ParticleSystemSortMode.Distance;
                r.maxParticleSize = 3f;

                var main = _mist.main;
                main.loop            = true;
                main.playOnAwake     = false;
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                main.startLifetime   = new ParticleSystem.MinMaxCurve(14f, 22f);
                main.startSpeed      = 0f;
                main.startSize       = new ParticleSystem.MinMaxCurve(7f, 13f);
                main.startRotation   = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
                main.maxParticles    = 400;
                var emission = _mist.emission;
                emission.rateOverTime = 0f; // placed by hand on the ground, in Follow
                var shape = _mist.shape;
                shape.enabled = false;

                var fade = _mist.colorOverLifetime;
                fade.enabled = true;
                var g = new Gradient();
                g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                          new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.3f), new GradientAlphaKey(1f, 0.7f), new GradientAlphaKey(0f, 1f) });
                fade.color = g;
            }
            var drift = _mist.velocityOverLifetime;
            drift.enabled = true;
            drift.space = ParticleSystemSimulationSpace.World;
            Vector3 wind = Quaternion.Euler(0f, c.WindDegrees, 0f) * Vector3.forward * 0.35f;
            drift.x = wind.x; drift.y = 0f; drift.z = wind.z;

            _mist.Clear();
            _mist.Play();
            _mistDebt = 0f;
        }

        // Mist gathers low: over water, in hollows, and (in fog, rain or at dawn) everywhere.
        private void EmitMist(Vector3 cam, float dt)
        {
            if (_mist == null || _mistAmount <= 0f) return;
            var terrain = Terrain.activeTerrain;
            if (terrain == null) return;
            _mistDebt += MistPerSecond * _mistAmount * dt;
            int tries = 0;
            while (_mistDebt >= 1f && tries++ < 8)
            {
                Vector2 o = Random.insideUnitCircle * MistRadius;
                var p = new Vector3(cam.x + o.x, 0f, cam.z + o.y);
                float ground = terrain.SampleHeight(p) + terrain.transform.position.y;
                float water  = ViewBlockers.SurfaceAt(p);
                bool overWater = water > ground - 0.1f;
                // Hollows: lower than the ground a little way round.
                float around = 0f;
                for (int i = 0; i < 4; i++)
                {
                    var q = p + Quaternion.Euler(0f, i * 90f, 0f) * Vector3.forward * 12f;
                    around += terrain.SampleHeight(q) + terrain.transform.position.y;
                }
                bool hollow = ground < around / 4f - 0.8f;
                float chance = overWater ? 1f : hollow ? 0.8f : Mathf.Clamp01(_mistAmount - 0.4f);
                if (Random.value > chance) continue;
                _mistDebt -= 1f;

                var e = new ParticleSystem.EmitParams
                {
                    position = new Vector3(p.x, Mathf.Max(ground, water) + Random.Range(0.4f, 1.6f), p.z),
                    startColor = new Color(_mistColor.r, _mistColor.g, _mistColor.b, Mathf.Clamp01(0.16f * Mathf.Max(0.6f, _mistAmount))),
                    applyShapeToPosition = false,
                };
                _mist.Emit(e, 1);
            }
            if (tries >= 8) _mistDebt = Mathf.Min(_mistDebt, 1f);
        }

        private static Texture2D MistTexture()
        {
            const int s = 64;
            var px = new Color32[s * s];
            for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                {
                    float dx = (x + 0.5f) / s * 2f - 1f, dy = (y + 0.5f) / s * 2f - 1f;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float n = 0.75f + 0.25f * Mathf.PerlinNoise(x * 0.12f, y * 0.12f);
                    float a = Mathf.Pow(Mathf.Clamp01(1f - r), 2f) * n;
                    px[y * s + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            var tex = new Texture2D(s, s, TextureFormat.RGBA32, true) { name = "Mist", wrapMode = TextureWrapMode.Clamp };
            tex.SetPixels32(px);
            tex.Apply(true);
            return tex;
        }

        // ── Every frame ────────────────────────────────────────────────────────

        public void Follow(Transform cam, float dt)
        {
            Vector3 p = cam.position;
            if (_shaftStrength > 0f && _trees.Count > 0)
            {
                int moved = 0;
                foreach (var s in _shafts)
                {
                    float d = Vector2.Distance(new Vector2(s.position.x, s.position.z), new Vector2(p.x, p.z));
                    bool stale = !s.gameObject.activeSelf || d > ShaftFar + 8f;
                    if (!stale || moved >= 2) continue; // a couple a frame keeps it cheap
                    moved++;
                    s.gameObject.SetActive(PlaceShaft(s, p));
                }
            }
            EmitMist(p, dt);
        }

        private static Material NewMaterial(string name, Texture2D tex, bool additive)
        {
            // The shader by name first: loading the Resources material that keeps it in builds logged YAML
            // "IsMapping" errors on Firdous's Mac. The material is only the fallback.
            var shader = Shader.Find("ProjectFossil/SoftVolume");
            Material m;
            if (shader != null) m = new Material(shader);
            else
            {
                var baseMat = Resources.Load<Material>("Shaders/SoftVolume");
                if (baseMat == null) return null;
                m = new Material(baseMat);
            }
            m.name = name;
            m.SetTexture("_BaseMap", tex);
            m.SetColor("_BaseColor", Color.white);
            m.SetFloat("_Additive", additive ? 1f : 0f);
            m.SetFloat("_EdgeFade", additive ? 1f : 0f);
            m.SetFloat("_NearFade", additive ? 4f : 2.5f);
            m.SetFloat("_SoftRange", additive ? 1.5f : 2.5f);
            m.SetFloat("_SrcBlend", additive ? (float)UnityEngine.Rendering.BlendMode.One : (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", additive ? (float)UnityEngine.Rendering.BlendMode.One : (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            return m;
        }
    }
}
