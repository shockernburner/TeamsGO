using UnityEngine;
using ProjectFossil.Core;

namespace ProjectFossil.Environment
{
    // Clouds, stars and rain as particle systems around the camera. Textures are drawn in code, so there is
    // nothing to import. Clouds and stars use a shader that ignores the island's haze.
    public class SkyEffects : MonoBehaviour
    {
        private const float CloudHeight = 170f;
        private const float StarRadius  = 900f;
        private const float RainHeight  = 16f;

        private ParticleSystem _clouds, _stars, _rain;
        private Transform _starRoot;
        private Vector3 _wind = Vector3.right;

        // ── Setup for a match ──────────────────────────────────────────────────

        public void Apply(Conditions c, Color cloudTint, Color fog)
        {
            _wind = Quaternion.Euler(0f, c.WindDegrees, 0f) * Vector3.forward;
            SetUpClouds(c, cloudTint);
            SetUpStars(c);
            SetUpRain(c);
        }

        public void Follow(Transform cam)
        {
            Vector3 p = cam.position;
            if (_clouds != null) _clouds.transform.position = new Vector3(p.x, CloudHeight, p.z);
            if (_starRoot != null) _starRoot.position = p;
            if (_rain != null) _rain.transform.position = p + Vector3.up * RainHeight + _wind * 3f;
        }

        // ── Clouds ─────────────────────────────────────────────────────────────

        private void SetUpClouds(Conditions c, Color tint)
        {
            int count = c.Weather switch
            {
                Weather.Clear => 22, Weather.Cloudy => 90, Weather.Rain => 120, Weather.Storm => 150, _ => 45,
            };
            float alpha = c.Weather == Weather.Clear ? 0.75f : 0.92f;

            if (_clouds == null)
            {
                _clouds = MakeSystem("Clouds", SkyMaterial(CloudTexture()));
                var r = _clouds.GetComponent<ParticleSystemRenderer>();
                r.renderMode = ParticleSystemRenderMode.Billboard;
                r.sortMode   = ParticleSystemSortMode.Distance;
                r.maxParticleSize = 3f; // big on screen when overhead
            }

            _clouds.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = _clouds.main;
            main.loop            = true;
            main.prewarm         = true;
            main.startLifetime   = 240f;
            main.startSpeed      = 0f;
            main.startSize       = new ParticleSystem.MinMaxCurve(70f, 160f);
            main.startRotation   = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor      = new Color(tint.r, tint.g, tint.b, alpha);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles    = count + 20;

            var emission = _clouds.emission;
            emission.rateOverTime = count / 240f;

            var shape = _clouds.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale     = new Vector3(1500f, 50f, 1500f);

            // Drift with the wind; storms race.
            float speed = c.Weather == Weather.Storm ? 9f : c.Weather == Weather.Rain ? 6f : 3f;
            var vel = _clouds.velocityOverLifetime;
            vel.enabled = true;
            vel.space   = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(_wind.x * speed);
            vel.y = new ParticleSystem.MinMaxCurve(0f);
            vel.z = new ParticleSystem.MinMaxCurve(_wind.z * speed);

            var col = _clouds.colorOverLifetime;
            col.enabled = true;
            var fade = new Gradient();
            fade.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                         new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.1f), new GradientAlphaKey(1f, 0.9f), new GradientAlphaKey(0f, 1f) });
            col.color = fade;

            var cam = Camera.main;
            if (cam != null) _clouds.transform.position = new Vector3(cam.transform.position.x, CloudHeight, cam.transform.position.z);
            _clouds.Play();
        }

        // ── Stars ──────────────────────────────────────────────────────────────

        private void SetUpStars(Conditions c)
        {
            bool show = c.Time == DayTime.Night && (c.Weather == Weather.Clear || c.Weather == Weather.Cloudy);
            if (!show)
            {
                if (_stars != null) _stars.Clear();
                return;
            }
            if (_stars == null)
            {
                _stars = MakeSystem("Stars", SkyMaterial(DotTexture()));
                _starRoot = _stars.transform;
                var main = _stars.main;
                main.loop = false;
                main.playOnAwake = false;
                main.startLifetime = 100000f;
                main.startSpeed = 0f;
                main.simulationSpace = ParticleSystemSimulationSpace.Local; // they move with the camera: infinitely far
                main.maxParticles = 1500;
                var emission = _stars.emission;
                emission.enabled = false;
                var shape = _stars.shape;
                shape.enabled = false;
            }

            _stars.Clear();
            var rng = new System.Random(4242);
            int n = c.Weather == Weather.Cloudy ? 450 : 1100;
            var e = new ParticleSystem.EmitParams();
            for (int i = 0; i < n; i++)
            {
                // Upper sky only, thinning toward the horizon.
                float u = (float)rng.NextDouble(), v = (float)rng.NextDouble();
                float y = Mathf.Lerp(0.05f, 1f, Mathf.Sqrt(v));
                float r = Mathf.Sqrt(1f - y * y), a = u * Mathf.PI * 2f;
                e.position   = new Vector3(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r) * StarRadius;
                e.startSize  = Mathf.Lerp(1.6f, 4.5f, (float)(rng.NextDouble() * rng.NextDouble()));
                float bright = Mathf.Lerp(0.45f, 1f, (float)rng.NextDouble());
                e.startColor = new Color(Mathf.Lerp(0.8f, 1f, (float)rng.NextDouble()), 0.92f, 1f, bright);
                _stars.Emit(e, 1);
            }
        }

        // ── Rain ───────────────────────────────────────────────────────────────

        private void SetUpRain(Conditions c)
        {
            bool raining = c.Weather == Weather.Rain || c.Weather == Weather.Storm;
            if (!raining)
            {
                if (_rain != null) _rain.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                return;
            }
            if (_rain == null)
            {
                var dust = Resources.Load<Material>("Shaders/RotorDust"); // URP particle material, kept in builds
                if (dust == null) return;
                var mat = new Material(dust) { name = "Rain" };
                mat.SetTexture("_BaseMap", StreakTexture());
                _rain = MakeSystem("Rain", mat);
                var r = _rain.GetComponent<ParticleSystemRenderer>();
                r.renderMode    = ParticleSystemRenderMode.Stretch;
                r.velocityScale = 0.045f;
                r.lengthScale   = 1f;
            }

            bool storm = c.Weather == Weather.Storm;
            _rain.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = _rain.main;
            main.loop            = true;
            main.startLifetime   = 1.1f;
            main.startSpeed      = 0f;
            main.startSize       = new ParticleSystem.MinMaxCurve(0.022f, 0.035f);
            main.startColor      = storm ? new Color(0.7f, 0.74f, 0.82f, 0.4f) : new Color(0.8f, 0.85f, 0.95f, 0.38f);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles    = storm ? 7000 : 4000;

            var emission = _rain.emission;
            emission.rateOverTime = storm ? 5200f : 2600f;

            var shape = _rain.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale     = new Vector3(38f, 0.5f, 38f);

            float slant = storm ? 5f : 2f;
            var vel = _rain.velocityOverLifetime;
            vel.enabled = true;
            vel.space   = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(_wind.x * slant);
            vel.y = new ParticleSystem.MinMaxCurve(-24f);
            vel.z = new ParticleSystem.MinMaxCurve(_wind.z * slant);
            _rain.Play();
        }

        // ── Helpers ────────────────────────────────────────────────────────────

        private ParticleSystem MakeSystem(string name, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            return ps;
        }

        private static Material SkyMaterial(Texture2D tex)
        {
            var baseMat = Resources.Load<Material>("Shaders/SkyParticle");
            Material m;
            if (baseMat != null) m = new Material(baseMat);
            else
            {
                var shader = Shader.Find("ProjectFossil/SkyParticle");
                if (shader == null) return null;
                m = new Material(shader);
            }
            m.SetTexture("_BaseMap", tex);
            return m;
        }

        // A puffy cloud: overlapping soft blobs, ragged at the edge, a little darker underneath.
        private static Texture2D CloudTexture()
        {
            const int size = 128;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp };
            var rng = new System.Random(77);
            var blobs = new Vector3[9];
            for (int i = 0; i < blobs.Length; i++)
            {
                float a = (float)rng.NextDouble() * Mathf.PI * 2f, d = (float)rng.NextDouble() * 0.22f;
                blobs[i] = new Vector3(0.5f + Mathf.Cos(a) * d, 0.48f + Mathf.Sin(a) * d * 0.6f, 0.13f + (float)rng.NextDouble() * 0.12f);
            }
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = x / (size - 1f), v = y / (size - 1f);
                float density = 0f;
                foreach (var b in blobs)
                {
                    float dx = u - b.x, dy = v - b.y;
                    density += Mathf.Exp(-(dx * dx + dy * dy) / (b.z * b.z));
                }
                float detail = Mathf.PerlinNoise(u * 7f + 3.1f, v * 7f + 1.7f) * 0.5f + Mathf.PerlinNoise(u * 15f, v * 15f) * 0.25f;
                float a = Mathf.Clamp01((density * 0.7f + detail * 0.5f - 0.45f) * 1.6f);
                // Fade to nothing at the square's edge so no corner shows.
                float edge = Mathf.Clamp01((0.5f - Mathf.Max(Mathf.Abs(u - 0.5f), Mathf.Abs(v - 0.5f))) * 6f);
                float shade = Mathf.Lerp(0.72f, 1f, Mathf.Clamp01(v * 1.3f));
                tex.SetPixel(x, y, new Color(shade, shade, shade * 1.02f, a * edge));
            }
            tex.Apply();
            return tex;
        }

        // A round soft dot (stars).
        private static Texture2D DotTexture()
        {
            const int size = 32;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = (x - 15.5f) / 15.5f, dy = (y - 15.5f) / 15.5f;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                float a = Mathf.Clamp01(1f - d);
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a * a));
            }
            tex.Apply();
            return tex;
        }

        // A thin vertical streak (rain drops).
        private static Texture2D StreakTexture()
        {
            const int w = 8, h = 32;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float across = 1f - Mathf.Abs((x - 3.5f) / 3.5f);
                float along  = Mathf.Sin(Mathf.PI * y / (h - 1f));
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, across * across * along));
            }
            tex.Apply();
            return tex;
        }
    }
}
