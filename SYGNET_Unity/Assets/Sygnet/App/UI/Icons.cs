using System;
using System.Collections.Generic;
using UnityEngine;

namespace Sygnet.App.UI
{
    /// <summary>
    /// Ikony rysowane w kodzie (SDF → tekstura z antyaliasingiem), bo font nie ma ✓ ⚠ ✈.
    /// Białe, barwione przez Image.color. Współrzędne projektu: 0..100, oś Y w dół.
    /// </summary>
    public static class Icons
    {
        public const int RoundedBorder = 48;

        static readonly Dictionary<string, Sprite> Cache = new Dictionary<string, Sprite>();

        public static Sprite RoundedRect => Get("rounded", () => Make(128, RoundedSdf, RoundedBorder));
        public static Sprite Circle => Get("circle", () => Make(256, (x, y) => C(x, y, 50, 50, 48)));
        public static Sprite Ring => Get("ring", () => Make(256, (x, y) => Mathf.Abs(C(x, y, 50, 50, 46)) - 1.5f));

        public static Sprite Check => Get("check", () => Make(128, (x, y) =>
            Min(Seg(x, y, 22, 52, 42, 72, 10), Seg(x, y, 42, 72, 78, 30, 10))));

        public static Sprite Cross => Get("cross", () => Make(128, (x, y) =>
            Min(Seg(x, y, 26, 26, 74, 74, 10), Seg(x, y, 74, 26, 26, 74, 10))));

        public static Sprite Warning => Get("warning", () => Make(128, (x, y) => Min(
            Seg(x, y, 50, 12, 90, 84, 8), Seg(x, y, 90, 84, 10, 84, 8), Seg(x, y, 10, 84, 50, 12, 8),
            Seg(x, y, 50, 36, 50, 58, 9), C(x, y, 50, 71, 5))));

        public static Sprite Qr => Get("qr", () => Make(128, (x, y) => Min(
            Finder(x, y, 24, 24), Finder(x, y, 76, 24), Finder(x, y, 24, 76),
            Box(x, y, 62, 62, 5, 5, 1), Box(x, y, 80, 80, 5, 5, 1), Box(x, y, 62, 80, 5, 5, 1), Box(x, y, 80, 62, 5, 5, 1))));

        public static Sprite Speaker => Get("speaker", () => Make(128, (x, y) => Min(
            Poly(x, y, new Vector2(14, 38), new Vector2(32, 38), new Vector2(52, 20), new Vector2(52, 80), new Vector2(32, 62), new Vector2(14, 62)),
            Arc(x, y, 52, 50, 20, 7), Arc(x, y, 52, 50, 34, 7))));

        public static Sprite Back => Get("back", () => Make(128, (x, y) =>
            Min(Seg(x, y, 62, 20, 32, 50, 10), Seg(x, y, 32, 50, 62, 80, 10))));

        public static Sprite Inbox => Get("inbox", () => Make(128, (x, y) => Min(
            Mathf.Abs(Box(x, y, 50, 57, 36, 27, 8)) - 4,
            Seg(x, y, 14, 58, 36, 58, 8), Seg(x, y, 36, 58, 42, 68, 8), Seg(x, y, 42, 68, 58, 68, 8),
            Seg(x, y, 58, 68, 64, 58, 8), Seg(x, y, 64, 58, 86, 58, 8))));

        public static Sprite Plane => Get("plane", () => Make(128, (x, y) => Poly(x, y,
            new Vector2(50, 6), new Vector2(56, 16), new Vector2(56, 38), new Vector2(92, 58), new Vector2(92, 67),
            new Vector2(56, 56), new Vector2(56, 78), new Vector2(67, 86), new Vector2(67, 93), new Vector2(50, 88),
            new Vector2(33, 93), new Vector2(33, 86), new Vector2(44, 78), new Vector2(44, 56), new Vector2(8, 67),
            new Vector2(8, 58), new Vector2(44, 38), new Vector2(44, 16))));

        public static Sprite Mic => Get("mic", () => Make(128, (x, y) => Min(
            Mathf.Abs(Box(x, y, 50, 36, 13, 24, 13)) - 3.5f,
            Max(Mathf.Abs(C(x, y, 50, 50, 24)) - 3.5f, 50 - y),
            Seg(x, y, 50, 74, 50, 88, 7), Seg(x, y, 36, 88, 64, 88, 7))));

        /// <summary>Gruby pierścień do paska postępu (Image.Type.Filled, Radial360).</summary>
        public static Sprite RingThick => Get("ringThick", () => Make(512, (x, y) => Mathf.Abs(C(x, y, 50, 50, 46)) - 3.2f));

        public static Sprite Pin => Get("pin", () => Make(128, (x, y) => Max(
            Min(C(x, y, 50, 40, 26), Poly(x, y, new Vector2(29, 52), new Vector2(71, 52), new Vector2(50, 90))),
            -C(x, y, 50, 40, 10))));

        public static Sprite Key => Get("key", () => Make(128, (x, y) => Min(
            Mathf.Abs(C(x, y, 30, 50, 15)) - 4.5f,
            Seg(x, y, 45, 50, 90, 50, 9), Seg(x, y, 76, 50, 76, 66, 9), Seg(x, y, 88, 50, 88, 62, 9))));

        public static Sprite Drop => Get("drop", () => Make(128, (x, y) =>
            Min(C(x, y, 50, 62, 26), Poly(x, y, new Vector2(25.5f, 54), new Vector2(74.5f, 54), new Vector2(50, 8)))));

        public static Sprite Bolt => Get("bolt", () => Make(128, (x, y) => Poly(x, y,
            new Vector2(58, 6), new Vector2(22, 56), new Vector2(47, 56), new Vector2(40, 94), new Vector2(78, 40),
            new Vector2(53, 40))));

        public static Sprite Flask => Get("flask", () => Make(128, (x, y) => Min(
            Seg(x, y, 36, 10, 64, 10, 8), Seg(x, y, 42, 10, 42, 40, 8), Seg(x, y, 58, 10, 58, 40, 8),
            Seg(x, y, 42, 40, 18, 84, 8), Seg(x, y, 58, 40, 82, 84, 8), Seg(x, y, 18, 84, 82, 84, 8),
            Poly(x, y, new Vector2(30, 64), new Vector2(70, 64), new Vector2(82, 86), new Vector2(18, 86)))));

        public static Sprite Info => Get("info", () => Make(128, (x, y) => Min(
            Mathf.Abs(C(x, y, 50, 50, 40)) - 4.5f, C(x, y, 50, 29, 6), Seg(x, y, 50, 45, 50, 74, 10))));

        public static Sprite Exit => Get("exit", () => Make(128, (x, y) => Min(
            Seg(x, y, 56, 12, 16, 12, 8), Seg(x, y, 16, 12, 16, 88, 8), Seg(x, y, 16, 88, 56, 88, 8),
            Seg(x, y, 38, 50, 86, 50, 9), Seg(x, y, 70, 33, 87, 50, 9), Seg(x, y, 87, 50, 70, 67, 9))));

        public static Sprite CheckCircle => Get("checkCircle", () => Make(128, (x, y) => Min(
            Mathf.Abs(C(x, y, 50, 50, 40)) - 4.5f, Seg(x, y, 32, 52, 45, 65, 9), Seg(x, y, 45, 65, 69, 37, 9))));

        /// <summary>Ikona typu komunikatu (PROTOCOL.md §4).</summary>
        public static Sprite ForAlert(int type)
        {
            switch (type)
            {
                case Sygnet.Core.AlertTypes.AirRaid: return Plane;
                case Sygnet.Core.AlertTypes.AllClear: return CheckCircle;
                case Sygnet.Core.AlertTypes.Evacuation: return Exit;
                case Sygnet.Core.AlertTypes.WaterContamination: return Drop;
                case Sygnet.Core.AlertTypes.PowerOutage: return Bolt;
                case Sygnet.Core.AlertTypes.Chemical: return Flask;
                case Sygnet.Core.AlertTypes.DisinfoWarning: return Warning;
                case Sygnet.Core.AlertTypes.KeyRevoke: return Key;
                default: return Info;
            }
        }

        // ───────────── renderer ─────────────

        static Sprite Get(string key, Func<Sprite> make)
        {
            if (!Cache.TryGetValue(key, out var s) || s == null) Cache[key] = s = make();
            return s;
        }

        /// <summary>Rysuje SDF (w jednostkach projektu 0..100) do tekstury size×size.</summary>
        static Sprite Make(int size, Func<float, float, float> sdf, int border = 0)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                name = "SygnetIcon",
            };
            float unitsPerPixel = 100f / size;
            var px = new Color32[size * size];
            for (int row = 0; row < size; row++)
            {
                float y = 100f - (row + 0.5f) * unitsPerPixel;    // tekstura: wiersz 0 na dole
                for (int col = 0; col < size; col++)
                {
                    float x = (col + 0.5f) * unitsPerPixel;
                    float d = sdf(x, y) / unitsPerPixel;          // odległość w pikselach
                    byte a = (byte)(Mathf.Clamp01(0.5f - d) * 255f);
                    px[row * size + col] = new Color32(255, 255, 255, a);
                }
            }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            var b = new Vector4(border, border, border, border);
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, b);
        }

        static float RoundedSdf(float x, float y) => Box(x, y, 50, 50, 50, 50, 50f * RoundedBorder / 128f);

        // ───────────── prymitywy SDF (ujemne = wewnątrz) ─────────────

        static float C(float x, float y, float cx, float cy, float r) => Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy)) - r;

        static float Seg(float x, float y, float ax, float ay, float bx, float by, float width)
        {
            float px = x - ax, py = y - ay, dx = bx - ax, dy = by - ay;
            float h = Mathf.Clamp01((px * dx + py * dy) / (dx * dx + dy * dy));
            float ex = px - dx * h, ey = py - dy * h;
            return Mathf.Sqrt(ex * ex + ey * ey) - width / 2;
        }

        static float Box(float x, float y, float cx, float cy, float hx, float hy, float r)
        {
            float qx = Mathf.Abs(x - cx) - hx + r, qy = Mathf.Abs(y - cy) - hy + r;
            float ox = Mathf.Max(qx, 0), oy = Mathf.Max(qy, 0);
            return Mathf.Sqrt(ox * ox + oy * oy) + Mathf.Min(Mathf.Max(qx, qy), 0) - r;
        }

        static float Finder(float x, float y, float cx, float cy) =>
            Min(Mathf.Abs(Box(x, y, cx, cy, 15, 15, 3)) - 3.5f, Box(x, y, cx, cy, 6, 6, 1));

        /// <summary>Łuk pierścienia po prawej stronie środka (fale głośnika), kąt ±50°.</summary>
        static float Arc(float x, float y, float cx, float cy, float r, float width)
        {
            float ring = Mathf.Abs(C(x, y, cx, cy, r)) - width / 2;
            float ang = Mathf.Abs(Mathf.Atan2(y - cy, x - cx)) * Mathf.Rad2Deg;
            float mask = (ang - 50f) * r * Mathf.Deg2Rad;                   // odległość wzdłuż łuku poza zakres
            return Mathf.Max(ring, mask);
        }

        static float Poly(float x, float y, params Vector2[] v)
        {
            float d = float.MaxValue;
            bool inside = false;
            for (int i = 0, j = v.Length - 1; i < v.Length; j = i++)
            {
                d = Mathf.Min(d, Seg(x, y, v[j].x, v[j].y, v[i].x, v[i].y, 0));
                if ((v[i].y > y) != (v[j].y > y) && x < (v[j].x - v[i].x) * (y - v[i].y) / (v[j].y - v[i].y) + v[i].x)
                    inside = !inside;
            }
            return inside ? -d : d;
        }

        static float Min(params float[] d)
        {
            float m = d[0];
            for (int i = 1; i < d.Length; i++) m = Mathf.Min(m, d[i]);
            return m;
        }

        static float Max(float a, float b) => Mathf.Max(a, b);
    }
}
