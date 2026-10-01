using UnityEngine;
using ProjectFossil.Core;

namespace ProjectFossil.Generation
{
    // Wind for the vegetation near the camera: plants bend and trees lean with slow gusts, and bushes and ferns
    // are brushed aside by whoever walks through them (players and dinosaurs), springing back once they've passed.
    // Only what's close enough to notice moves (a few hundred objects), so it stays cheap on a 10,000-plant island.
    // Presentation only; the decorator adds it to the island root.
    public class IslandWind : MonoBehaviour
    {
        public float radius       = 45f;
        public float plantBend    = 7f;    // degrees at full gust
        public float frequency    = 1.3f;  // sway cycles per second (roughly)
        public float gustSpeed    = 0.12f; // how fast gusts come and go
        public float pushBend     = 38f;   // degrees a plant leans away from someone standing in it

        private Vector3 _windDir = Vector3.right;
        private Vector3 _axis    = Vector3.forward;
        private float   _time;
        private float   _gust;
        private ViewBlockers.SwayVisitor _visit;
        // Pushers near the camera this frame: flat position and reach.
        private readonly System.Collections.Generic.List<Vector4> _near = new System.Collections.Generic.List<Vector4>();

        public void SetDirection(float degrees)
        {
            _windDir = Quaternion.Euler(0f, degrees, 0f) * Vector3.forward;
            _axis    = Vector3.Cross(Vector3.up, _windDir).normalized; // bending "downwind"
        }

        private void Awake() => _visit = Sway;

        private void LateUpdate()
        {
            var cam = Camera.main;
            if (cam == null) return;

            _time += Time.deltaTime;
            // Gusts: a slow wave between calm and strong, never fully still.
            _gust = 0.35f + 0.65f * Mathf.PerlinNoise(_time * gustSpeed, 0.37f);
            _near.Clear();
            Vector3 c = cam.transform.position;
            foreach (var p in PlantPusher.All)
            {
                Vector3 q = p.transform.position;
                if ((q.x - c.x) * (q.x - c.x) + (q.z - c.z) * (q.z - c.z) < (radius + 5f) * (radius + 5f))
                    _near.Add(new Vector4(q.x, q.y, q.z, p.reach));
            }
            ViewBlockers.ForEachSwaying(cam.transform.position, radius, _visit);
        }

        private void Sway(in ViewBlockers.Entry e)
        {
            float phase = e.Center.x * 0.37f + e.Center.z * 0.61f;
            // A steady lean downwind plus a flutter on top: slow, heavy rocking for trees, quick for plants.
            float flutter = Mathf.Sin(_time * frequency * (e.Tree ? 0.3f : 1f) * Mathf.PI * 2f + phase);
            float angle = plantBend * e.Sway * _gust * (0.45f + 0.55f * flutter);

            var parent = e.Root.parent;
            Quaternion rest = (parent != null ? parent.rotation : Quaternion.identity) * e.RestRotation;
            Quaternion bend = Quaternion.AngleAxis(angle, _axis);

            // Brushed aside: lean away from anyone inside the plant, most at its centre. Trees don't give way.
            if (!e.Tree)
            {
                foreach (var p in _near)
                {
                    float dx = e.Center.x - p.x, dz = e.Center.z - p.z;
                    float reach = e.Radius + p.w;
                    float d2 = dx * dx + dz * dz;
                    if (d2 >= reach * reach || Mathf.Abs(e.Center.y - p.y) > 3f) continue;
                    float d = Mathf.Sqrt(d2);
                    float k = 1f - d / reach;
                    Vector3 away = d > 0.05f ? new Vector3(dx / d, 0f, dz / d) : _windDir;
                    bend = Quaternion.AngleAxis(pushBend * k * (2f - k), Vector3.Cross(Vector3.up, away)) * bend;
                }
            }
            e.Root.rotation = bend * rest;
        }
    }
}
