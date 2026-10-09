using UnityEngine;

namespace ProjectFossil.Core
{
    // How high anything flying must be over the island. The island's ground (IslandWorld, the same heights the
    // terrain draws) is known at every x, z before anything moves; a flyer reads it under itself and along the way
    // it is heading, and climbs before a slope reaches it, so it never passes through a hill.
    public static class FlightHeight
    {
        public const float Clearance    = 25f;  // metres over the highest ground near and ahead
        public const float HardMinimum  = 10f;  // never closer than this to the ground right below, whatever else
        public const float LookAhead    = 60f;  // metres ahead along the heading that count
        public const float ClimbRate    = 20f;  // metres per second, up
        public const float SinkRate     = 4f;   // metres per second, down (unhurried once past a ridge)
        private const float Spread      = 8f;   // wingspan and wobble: ground this far either side counts too

        // The highest ground (or water surface) under pos, beside it, and ahead of it along heading.
        public static float Floor(IslandWorld world, Vector3 pos, Vector3 heading)
        {
            if (world == null) return float.NegativeInfinity;
            heading.y = 0f;
            Vector3 ahead = heading.sqrMagnitude > 1e-6f ? heading.normalized : Vector3.zero;
            Vector3 side  = new Vector3(-ahead.z, 0f, ahead.x);
            float top = float.NegativeInfinity;
            for (int i = 0; i <= 4; i++)
            {
                Vector3 p = pos + ahead * (LookAhead * i / 4f);
                top = Mathf.Max(top, Surface(world, p), Surface(world, p + side * Spread), Surface(world, p - side * Spread));
            }
            return top;
        }

        // The flyer's height this frame: where it wants to be, but at least Clearance over the floor (climbing at
        // ClimbRate), and never under HardMinimum over the ground right below it, even mid-climb.
        public static float Step(IslandWorld world, Vector3 pos, Vector3 heading, float current, float wanted, float dt)
        {
            if (world == null) return wanted;
            float target = Mathf.Max(wanted, Floor(world, pos, heading) + Clearance);
            float y = float.IsNaN(current) ? target
                    : current < target ? Mathf.MoveTowards(current, target, ClimbRate * dt)
                    : Mathf.MoveTowards(current, target, SinkRate * dt);
            return Mathf.Max(y, Surface(world, pos) + HardMinimum);
        }

        private static float Surface(IslandWorld world, Vector3 p) => Mathf.Max(world.GroundAt(p), world.WaterSurfaceAt(p));
    }
}
