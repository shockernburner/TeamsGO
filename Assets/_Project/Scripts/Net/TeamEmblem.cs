using UnityEngine;

namespace ProjectFossil.Net
{
    // A team's emblem: one of twelve badges, a coloured shape on a dark disc, drawn from shapes made in code (no art).
    public static class TeamEmblem
    {
        public const int Count = 12;
        private const int Shapes = 6, Size = 64;

        private static readonly Color[] Colours =
        {
            new Color(0.93f, 0.36f, 0.25f), new Color(0.98f, 0.76f, 0.24f), new Color(0.42f, 0.78f, 0.36f),
            new Color(0.26f, 0.68f, 0.86f), new Color(0.67f, 0.45f, 0.87f), new Color(0.92f, 0.92f, 0.88f),
            new Color(0.95f, 0.55f, 0.20f), new Color(0.30f, 0.82f, 0.70f), new Color(0.86f, 0.33f, 0.55f),
            new Color(0.55f, 0.62f, 0.95f), new Color(0.75f, 0.85f, 0.30f), new Color(0.80f, 0.60f, 0.45f),
        };
        public static readonly string[] Names = { "Claw", "Sun", "Fern", "Wave", "Amber", "Bone", "Flame", "Reef", "Bloom", "Storm", "Moss", "Clay" };

        private static Texture2D _disc;
        private static Texture2D[] _shapes;

        public static void Draw(Rect r, int emblem)
        {
            Ensure();
            emblem = ((emblem % Count) + Count) % Count;
            var keep = GUI.color;
            GUI.color = new Color(0.08f, 0.08f, 0.07f, 0.9f);
            GUI.DrawTexture(r, _disc);
            GUI.color = Colours[emblem];
            float pad = r.width * 0.2f;
            GUI.DrawTexture(new Rect(r.x + pad, r.y + pad, r.width - 2f * pad, r.height - 2f * pad), _shapes[emblem % Shapes]);
            GUI.color = keep;
        }

        // Whether a point (-1..1 each way, y up) is inside shape s: circle, diamond, triangle, star, cross, hexagon.
        public static bool Inside(int shape, float x, float y)
        {
            switch (((shape % Shapes) + Shapes) % Shapes)
            {
                case 0: return x * x + y * y <= 0.8f * 0.8f;
                case 1: return Mathf.Abs(x) + Mathf.Abs(y) <= 0.95f;
                case 2: return y >= -0.7f && Mathf.Abs(x) <= (0.85f - y) * 0.55f && y <= 0.85f;
                case 3:
                {
                    float a = Mathf.Atan2(y, x) + Mathf.PI / 2f, r = Mathf.Sqrt(x * x + y * y);
                    float spike = Mathf.Abs(Mathf.Cos(a * 2.5f)); // five points
                    return r <= Mathf.Lerp(0.42f, 0.95f, spike * spike);
                }
                case 4: return (Mathf.Abs(x) <= 0.28f && Mathf.Abs(y) <= 0.85f) || (Mathf.Abs(y) <= 0.28f && Mathf.Abs(x) <= 0.85f);
                default:
                {
                    float ax = Mathf.Abs(x), ay = Mathf.Abs(y);
                    return ay <= 0.78f && ax * 0.866f + ay * 0.5f <= 0.78f;
                }
            }
        }

        private static void Ensure()
        {
            if (_disc != null) return;
            _disc = Bake((x, y) => x * x + y * y <= 1f);
            _shapes = new Texture2D[Shapes];
            for (int s = 0; s < Shapes; s++) { int k = s; _shapes[s] = Bake((x, y) => Inside(k, x, y)); }
        }

        // 4x4 samples per pixel for soft edges.
        private static Texture2D Bake(System.Func<float, float, bool> inside)
        {
            var t = new Texture2D(Size, Size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[Size * Size];
            for (int j = 0; j < Size; j++)
                for (int i = 0; i < Size; i++)
                {
                    int hits = 0;
                    for (int sj = 0; sj < 4; sj++)
                        for (int si = 0; si < 4; si++)
                        {
                            float x = ((i + (si + 0.5f) / 4f) / Size) * 2f - 1f, y = ((j + (sj + 0.5f) / 4f) / Size) * 2f - 1f;
                            if (inside(x, y)) hits++;
                        }
                    px[j * Size + i] = new Color32(255, 255, 255, (byte)(hits * 255 / 16));
                }
            t.SetPixels32(px);
            t.Apply();
            return t;
        }
    }
}
