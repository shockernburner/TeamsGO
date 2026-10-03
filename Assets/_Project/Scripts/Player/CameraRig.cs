using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using ProjectFossil.Core;

namespace ProjectFossil.Player
{
    // Keeps the over-the-shoulder camera usable: it slides in front of trunks, rocks and hills instead of going
    // through them, hides plants that come between it and the player, and never dips under the sea.
    // Lives on the camera (a child of the player's CameraTarget); PlayerController adds it at start.
    [DisallowMultipleComponent]
    public class CameraRig : MonoBehaviour
    {
        public float probeRadius   = 0.25f;
        public float minDistance   = 0.9f;   // never closer to the head than this
        public float returnSpeed   = 5f;     // metres per second back out once the way is clear
        public float plantPadding  = 0.35f;  // plants this close to the line of sight hide
        public float plantClearance = 1.2f;  // and plants this close to the camera itself, which fill the frame edges
        public float aboveWater    = 0.35f;
        public float aboveGround   = 0.6f;   // pulled in low on a slope, the lens skimmed the ground and filled the view
        public float eyeClearance  = 0.45f;  // first person: leaves and grass this close to the eyes hide

        // First person: the camera sits in the head (FirstPersonView places it), so there is nothing to pull in.
        public bool FirstPerson { get; set; }

        private Transform _pivot;
        private Transform _owner;
        private Vector3   _restLocal;
        private float     _distance = -1f;

        private readonly RaycastHit[]     _hits    = new RaycastHit[16];
        private readonly List<Renderer[]> _near    = new List<Renderer[]>();
        private readonly List<Renderer[]> _around  = new List<Renderer[]>();
        private readonly HashSet<Renderer> _hidden = new HashSet<Renderer>();
        private readonly HashSet<Renderer> _still  = new HashSet<Renderer>();

        public void Init(Transform pivot, Transform owner)
        {
            _pivot     = pivot;
            _owner     = owner;
            _restLocal = pivot.InverseTransformPoint(transform.position);
        }

        private void LateUpdate()
        {
            if (_pivot == null) return;
            if (FirstPerson)
            {
                _distance = -1f;
                HidePlants(transform.position, transform.position, eyeClearance);
                return;
            }

            Vector3 origin  = _pivot.position;
            Vector3 desired = _pivot.TransformPoint(_restLocal);
            Vector3 offset  = desired - origin;
            float   full    = offset.magnitude;
            if (full < 1e-3f) return;
            Vector3 dir = offset / full;

            float allowed = full;
            int n = Physics.SphereCastNonAlloc(origin, probeRadius, dir, _hits, full, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var col = _hits[i].collider;
                if (col == null || !BlocksView(col, origin)) continue;
                if (_hits[i].distance <= 0f) continue; // started inside it; the other hits decide
                allowed = Mathf.Min(allowed, _hits[i].distance);
            }
            allowed = Mathf.Max(Mathf.Min(minDistance, full), allowed);

            // Snap in at once so nothing pokes through; ease back out so the view doesn't jump.
            _distance = _distance < 0f || allowed < _distance ? allowed
                      : Mathf.MoveTowards(_distance, allowed, returnSpeed * Time.deltaTime);

            Vector3 pos = origin + dir * _distance;
            float floor = IslandWorld.SurfaceOver(pos) + aboveWater; // the sea, or a lake or stream under the camera
            if (pos.y < floor) pos.y = floor;
            var world = IslandWorld.Current;
            if (world != null)
            {
                float ground = world.GroundAt(pos) + aboveGround;
                if (pos.y < ground) pos.y = ground;
            }
            transform.position = pos;

            HidePlants(pos, origin, plantClearance);
        }

        // Terrain, trunks and rocks block; the player and anything that moves don't, so the view doesn't jump each
        // time an animal walks behind. Except an animal right on top of the player: a dinosaur biting from behind
        // put the camera inside its body, and the screen went dark with its hide at the moment that mattered.
        private bool BlocksView(Collider col, Vector3 pivot)
        {
            if (_owner != null && col.transform.IsChildOf(_owner)) return false;
            if (col is CharacterController || col.attachedRigidbody != null) return false;
            if (col.GetComponentInParent<NavMeshAgent>() != null)
                return (col.ClosestPoint(pivot) - pivot).sqrMagnitude < CloseAnimal * CloseAnimal;
            return true;
        }

        // Metres from the player inside which an animal blocks the camera like a wall.
        private const float CloseAnimal = 4f;

        private void HidePlants(Vector3 cameraPos, Vector3 target, float clearance)
        {
            // Stop a little short of the head so plants right at the player's feet stay visible.
            Vector3 end = Vector3.Lerp(cameraPos, target, 0.85f);
            if (target != cameraPos) ViewBlockers.Overlapping(cameraPos, end, plantPadding, _near);
            else _near.Clear();
            ViewBlockers.Overlapping(cameraPos, cameraPos, clearance, _around);

            _still.Clear();
            Collect(_near);
            Collect(_around);

            foreach (var r in _hidden)
                if (r != null && !_still.Contains(r)) r.forceRenderingOff = false;
            foreach (var r in _still)
                r.forceRenderingOff = true;

            _hidden.Clear();
            _hidden.UnionWith(_still);
        }

        private void Collect(List<Renderer[]> groups)
        {
            foreach (var group in groups)
                foreach (var r in group)
                    if (r != null) _still.Add(r);
        }

        private void OnDisable()
        {
            foreach (var r in _hidden)
                if (r != null) r.forceRenderingOff = false;
            _hidden.Clear();
        }
    }
}
