using UnityEngine;
using ProjectFossil.Core;

namespace ProjectFossil.Generation
{
    // Wind for the vegetation near the camera: plants bend and trees lean with slow gusts. Only what's close
    // enough to notice moves (a few hundred objects), so it stays cheap on a 10,000-plant island.
    // Presentation only; the decorator adds it to the island root.
    public class IslandWind : MonoBehaviour
    {
        public float radius       = 45f;
        public float plantBend    = 7f;    // degrees at full gust
        public float frequency    = 1.3f;  // sway cycles per second (roughly)
        public float gustSpeed    = 0.12f; // how fast gusts come and go

        private Vector3 _windDir = Vector3.right;
        private Vector3 _axis    = Vector3.forward;
        private float   _time;
        private float   _gust;
        private ViewBlockers.SwayVisitor _visit;

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
            e.Root.rotation = Quaternion.AngleAxis(angle, _axis) * rest;
        }
    }
}
