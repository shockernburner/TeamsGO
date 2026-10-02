using System.Collections.Generic;
using UnityEngine;

namespace ProjectFossil.Core
{
    // The one answer to "where is the ground, where is the water, and who is where" for the current island.
    // Generation builds it once from the seed, and the terrain, the water, the plants, the NavMesh, the player, the
    // dinosaurs and the camera all read it. Before this, each asked its own way (the coarse map, the drawn terrain,
    // circles round each lake), so the water was drawn over one ground, walked on over another and swum in over a
    // third. Pure data, no MonoBehaviour, so it can be tested and later sent to clients as just a seed.
    public sealed class IslandWorld
    {
        public static IslandWorld Current { get; private set; }
        public static void SetCurrent(IslandWorld world) => Current = world;

        public int     Size     { get; }  // vertices along each side
        public float   Cell     { get; }  // metres between vertices
        public float   Extent   => Cell * (Size - 1);
        public Vector3 Origin                     // world position of vertex (0, 0) at height 0
        {
            get => _origin;
            set
            {
                _origin = value;
                _rapids.Clear();
                foreach (var r in _rapidsLocal) _rapids.Add(r + value);
            }
        }
        private Vector3 _origin;
        public float   SeaLevel { get; }  // height of the sea surface above Origin

        // Heights above Origin, row-major [z * Size + x]. Water is NaN where a vertex is dry.
        private readonly float[] _ground;
        private readonly float[] _water;

        // Where streams run fast down a slope (local positions on the surface), for their sound.
        private readonly List<Vector3> _rapidsLocal;
        private readonly List<Vector3> _rapids = new List<Vector3>();
        public IReadOnlyList<Vector3> Rapids => _rapids;

        public IslandWorld(int size, float cell, float seaLevel, float[] ground, float[] water,
                           List<Vector3> rapids = null)
        {
            Size = size; Cell = cell; SeaLevel = seaLevel;
            _ground = ground; _water = water;
            _rapidsLocal = rapids ?? new List<Vector3>();
            Origin = Vector3.zero;
        }

        // ── Grid access (local metres) ─────────────────────────────────────────

        public float GroundAtVertex(int x, int z) => _ground[Clamp(z) * Size + Clamp(x)];
        public float WaterAtVertex(int x, int z)  => _water[Clamp(z) * Size + Clamp(x)];
        public bool  WetVertex(int x, int z)      => !float.IsNaN(WaterAtVertex(x, z));

        private int Clamp(int i) => i < 0 ? 0 : i >= Size ? Size - 1 : i;

        // ── World queries ──────────────────────────────────────────────────────

        public float SeaSurface => Origin.y + SeaLevel;

        // The ground under a world position, as the terrain draws it.
        public float GroundAt(Vector3 p)
        {
            Locate(p, out int x, out int z, out float tx, out float tz);
            float a = GroundAtVertex(x, z), b = GroundAtVertex(x + 1, z);
            float c = GroundAtVertex(x, z + 1), d = GroundAtVertex(x + 1, z + 1);
            return Origin.y + Lerp(Lerp(a, b, tx), Lerp(c, d, tx), tz);
        }

        // The highest water surface over a spot: a lake or stream it lies in, else the sea.
        public float WaterSurfaceAt(Vector3 p)
        {
            Locate(p, out int x, out int z, out float tx, out float tz);
            float sum = 0f, weight = 0f;
            Add(x, z, (1f - tx) * (1f - tz)); Add(x + 1, z, tx * (1f - tz));
            Add(x, z + 1, (1f - tx) * tz);   Add(x + 1, z + 1, tx * tz);
            float inland = weight > 0f ? Origin.y + sum / weight : float.NegativeInfinity;
            return Mathf.Max(inland, SeaSurface);

            void Add(int vx, int vz, float w)
            {
                float s = WaterAtVertex(vx, vz);
                if (float.IsNaN(s)) return;
                w += 1e-4f; // a corner exactly on the far side still counts
                sum += s * w; weight += w;
            }
        }

        // How deep the water is over the ground here (0 on dry land).
        public float WaterDepthAt(Vector3 p) => Mathf.Max(0f, WaterSurfaceAt(p) - GroundAt(p));

        // Feet in the sea, a lake or a stream.
        public bool InWater(Vector3 feet) => feet.y < WaterSurfaceAt(feet) - 0.05f;

        private void Locate(Vector3 p, out int x, out int z, out float tx, out float tz)
        {
            float gx = Mathf.Clamp((p.x - Origin.x) / Cell, 0f, Size - 1.001f);
            float gz = Mathf.Clamp((p.z - Origin.z) / Cell, 0f, Size - 1.001f);
            x = (int)gx; z = (int)gz;
            tx = gx - x; tz = gz - z;
        }

        private static float Lerp(float a, float b, float t) => a + (b - a) * t;

        // ── Shortcuts for the current island (safe before one exists) ──────────

        // The water surface over a spot, or -Infinity with no island.
        public static float SurfaceOver(Vector3 p) => Current != null ? Current.WaterSurfaceAt(p) : float.NegativeInfinity;

        // Feet in any water.
        public static bool Wet(Vector3 feet) => Current != null && Current.InWater(feet);

        // The sea's surface, or -Infinity with no island.
        public static float Sea => Current != null ? Current.SeaSurface : float.NegativeInfinity;

        // ── Who is where ───────────────────────────────────────────────────────

        public enum ActorKind { Survivor, Dinosaur }

        // Players and dinosaurs register while they're alive in the match, so the map, the director, the sound and
        // the AI all see the same people in the same places.
        private static readonly List<(Transform t, ActorKind kind)> Actors = new List<(Transform, ActorKind)>();

        public static void Register(Transform t, ActorKind kind)
        {
            if (t == null) return;
            for (int i = 0; i < Actors.Count; i++) if (Actors[i].t == t) return;
            Actors.Add((t, kind));
        }

        public static void Unregister(Transform t)
        {
            for (int i = Actors.Count - 1; i >= 0; i--) if (Actors[i].t == t) Actors.RemoveAt(i);
        }

        // Every live actor of a kind (destroyed ones are dropped on the way).
        public static void ForEach(ActorKind kind, System.Action<Transform> visit)
        {
            for (int i = Actors.Count - 1; i >= 0; i--)
            {
                var (t, k) = Actors[i];
                if (t == null) { Actors.RemoveAt(i); continue; }
                if (k == kind) visit(t);
            }
        }
    }
}
