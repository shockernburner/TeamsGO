using UnityEngine;

namespace ProjectFossil.Core
{
    // Where to draw an on-screen marker for something in the world, such as the nearest extraction beacon.
    public static class ScreenMarker
    {
        // `screen` is what Camera.WorldToScreenPoint returned (pixels from the bottom-left, z = depth).
        // A point on screen stays where it is. A point off screen or behind the camera is pulled in to the screen
        // edge (inside `margin`) along the line from the centre, so the marker points the way to turn.
        public static Vector2 Place(Vector3 screen, float width, float height, float margin, out bool onScreen)
        {
            var centre = new Vector2(width * 0.5f, height * 0.5f);
            var p = new Vector2(screen.x, screen.y);
            bool behind = screen.z < 0f;
            if (behind) p = centre - (p - centre); // the projection of a point behind the camera comes out mirrored

            onScreen = !behind && p.x >= margin && p.x <= width - margin && p.y >= margin && p.y <= height - margin;
            if (onScreen) return p;

            Vector2 d = p - centre;
            if (d.sqrMagnitude < 1e-4f) d = new Vector2(0f, -1f); // straight behind: point down, "turn around"
            float sx = (centre.x - margin) / Mathf.Max(1e-4f, Mathf.Abs(d.x));
            float sy = (centre.y - margin) / Mathf.Max(1e-4f, Mathf.Abs(d.y));
            return centre + d * Mathf.Min(sx, sy);
        }
    }
}
