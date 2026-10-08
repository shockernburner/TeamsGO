using UnityEngine;

namespace ProjectFossil.Core
{
    // One size for all on-screen text and boxes, whatever the screen: everything is laid out for a 720-pixel-tall
    // screen and scaled to the real one. A built game on a Retina Mac runs at twice the pixels of its window, so
    // without this the menus and HUD came out half size. Every OnGUI calls Begin() first and lays out with W and H
    // instead of Screen.width and Screen.height; points from the camera go through FromScreen.
    public static class Ui
    {
        private const float ReferenceHeight = 720f;

        public static float Scale => Mathf.Clamp(Screen.height / ReferenceHeight, 1f, 4f);
        public static float W => Screen.width / Scale;
        public static float H => Screen.height / Scale;

        public static void Begin() => GUI.matrix = Matrix4x4.Scale(new Vector3(Scale, Scale, 1f));

        // A point from Camera.WorldToScreenPoint (pixels, origin bottom-left) in the same units as W and H.
        public static Vector3 FromScreen(Vector3 p) { float s = Scale; return new Vector3(p.x / s, p.y / s, p.z); }
    }
}
