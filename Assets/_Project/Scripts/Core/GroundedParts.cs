using UnityEngine;

namespace ProjectFossil.Core
{
    // For a prop made of several pieces (a ruin: arch, column, fallen wall, pots). Placed as one flat group on a
    // hillside, the downhill pieces hung in the air. On start each direct child drops (or rises) so its base sits
    // on the island's ground at the lowest point under it, a little sunk in, as rubble would.
    public class GroundedParts : MonoBehaviour
    {
        [Tooltip("Metres each piece sinks below the lowest ground under it")]
        public float sink = 0.1f;

        private void Start()
        {
            var world = IslandWorld.Current;
            if (world == null) return;
            foreach (Transform piece in transform)
            {
                if (!TryBounds(piece, out var b)) continue;
                float low = float.MaxValue;
                for (int i = 0; i < 5; i++)
                {
                    var p = i == 4 ? b.center : new Vector3(i < 2 ? b.min.x : b.max.x, 0f, i % 2 == 0 ? b.min.z : b.max.z);
                    low = Mathf.Min(low, world.GroundAt(p));
                }
                piece.position += Vector3.up * (low - sink - b.min.y);
            }
        }

        private static bool TryBounds(Transform piece, out Bounds b)
        {
            b = default;
            bool any = false;
            foreach (var r in piece.GetComponentsInChildren<Renderer>())
            {
                if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
            }
            return any;
        }
    }
}
