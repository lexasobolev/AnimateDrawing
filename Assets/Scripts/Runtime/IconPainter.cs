using System;
using AnimatedDrawingsWorld.Logic;
using AnimatedDrawingsWorld.Logic.Imaging;
using UnityEngine;

namespace AnimatedDrawingsWorld.Runtime
{
    // Paints the round button icons in code, so the UI needs no image assets. Shapes are drawn
    // with hard edges at 4x size and averaged down, which gives smooth anti-aliased edges.
    public static class IconPainter
    {
        private const int Size = 256;
        private const int Super = 4;

        public static Texture2D AddCharacter() => Paint(new Color32(255, 159, 67, 255), c =>
        {
            var body = new Color32(120, 200, 80, 255);
            var dark = new Color32(40, 50, 40, 255);
            // horns
            c.Polygon(dark, (0.33f, 0.36f), (0.27f, 0.16f), (0.42f, 0.30f));
            c.Polygon(dark, (0.67f, 0.36f), (0.73f, 0.16f), (0.58f, 0.30f));
            // feet and body
            c.Ellipse(body, 0.38f, 0.80f, 0.08f, 0.05f);
            c.Ellipse(body, 0.62f, 0.80f, 0.08f, 0.05f);
            c.Ellipse(body, 0.5f, 0.56f, 0.27f, 0.25f);
            // arms up
            c.Line(body, (0.27f, 0.55f), (0.17f, 0.40f), 0.05f);
            c.Line(body, (0.73f, 0.55f), (0.83f, 0.40f), 0.05f);
            // eyes
            c.Ellipse(Color.white, 0.41f, 0.47f, 0.07f, 0.08f);
            c.Ellipse(Color.white, 0.59f, 0.47f, 0.07f, 0.08f);
            c.Ellipse(dark, 0.42f, 0.49f, 0.03f, 0.035f);
            c.Ellipse(dark, 0.58f, 0.49f, 0.03f, 0.035f);
            // grin with teeth
            c.Ellipse(dark, 0.5f, 0.64f, 0.13f, 0.06f);
            c.Polygon(Color.white, (0.40f, 0.60f), (0.44f, 0.60f), (0.42f, 0.655f));
            c.Polygon(Color.white, (0.56f, 0.60f), (0.60f, 0.60f), (0.58f, 0.655f));
        });

        public static Texture2D AddBackground() => Paint(new Color32(84, 160, 255, 255), c =>
        {
            // sun with rays, top right
            var sun = new Color32(255, 214, 60, 255);
            for (var i = 0; i < 8; i++)
            {
                var a = i * MathF.PI / 4f;
                c.Line(sun, (0.72f + MathF.Cos(a) * 0.10f, 0.28f + MathF.Sin(a) * 0.10f), (0.72f + MathF.Cos(a) * 0.15f, 0.28f + MathF.Sin(a) * 0.15f), 0.022f);
            }
            c.Ellipse(sun, 0.72f, 0.28f, 0.075f, 0.075f);
            // grass
            c.Ellipse(new Color32(90, 190, 80, 255), 0.5f, 1.02f, 0.62f, 0.26f);
            // house in the middle
            c.Polygon(new Color32(255, 236, 190, 255), (0.34f, 0.50f), (0.62f, 0.50f), (0.62f, 0.76f), (0.34f, 0.76f));
            c.Polygon(new Color32(230, 70, 60, 255), (0.29f, 0.52f), (0.48f, 0.33f), (0.67f, 0.52f));
            c.Polygon(new Color32(140, 85, 45, 255), (0.44f, 0.62f), (0.52f, 0.62f), (0.52f, 0.76f), (0.44f, 0.76f));
            c.Polygon(new Color32(120, 190, 250, 255), (0.55f, 0.56f), (0.60f, 0.56f), (0.60f, 0.61f), (0.55f, 0.61f));
        });

        public static Texture2D Clear() => Paint(new Color32(238, 82, 83, 255), c =>
        {
            c.Line(Color.white, (0.33f, 0.33f), (0.67f, 0.67f), 0.075f);
            c.Line(Color.white, (0.67f, 0.33f), (0.33f, 0.67f), 0.075f);
        });

        private static Texture2D Paint(Color32 fill, Action<Canvas> icon)
        {
            var canvas = new Canvas(Size * Super);
            // soft shadow, white rim, coloured disc
            canvas.Ellipse(new Color32(0, 0, 0, 70), 0.5f, 0.52f, 0.47f, 0.47f);
            canvas.Ellipse(Color.white, 0.5f, 0.5f, 0.46f, 0.46f);
            canvas.Ellipse(fill, 0.5f, 0.5f, 0.41f, 0.41f);
            canvas.ClipRadius = 0.41f; // the icon stays inside the coloured disc
            icon(canvas);
            var texture = TextureConversion.ToTexture(canvas.Image.Resize(Size, Size));
            texture.filterMode = FilterMode.Bilinear;
            return texture;
        }

        private sealed class Canvas
        {
            public readonly RgbaImage Image;
            private readonly int size;
            public float ClipRadius = float.MaxValue;

            public Canvas(int size)
            {
                this.size = size;
                Image = new RgbaImage(size, size);
            }

            private void Blend(int x, int y, Color32 c)
            {
                var i = Image.Index(x, y);
                var a = c.a / 255f;
                var p = Image.Pixels;
                var dstA = p[i + 3] / 255f;
                var outA = a + dstA * (1f - a);
                if (outA <= 0f) return;
                p[i] = (byte)((c.r * a + p[i] * dstA * (1f - a)) / outA);
                p[i + 1] = (byte)((c.g * a + p[i + 1] * dstA * (1f - a)) / outA);
                p[i + 2] = (byte)((c.b * a + p[i + 2] * dstA * (1f - a)) / outA);
                p[i + 3] = (byte)(outA * 255f);
            }

            private void Fill(Func<float, float, bool> inside, Color32 c, float x0, float y0, float x1, float y1)
            {
                var ax = Math.Max(0, (int)(x0 * size) - 1);
                var bx = Math.Min(size - 1, (int)(x1 * size) + 1);
                var ay = Math.Max(0, (int)(y0 * size) - 1);
                var by = Math.Min(size - 1, (int)(y1 * size) + 1);
                for (var y = ay; y <= by; y++)
                for (var x = ax; x <= bx; x++)
                {
                    var u = (x + 0.5f) / size;
                    var v = (y + 0.5f) / size;
                    if ((u - 0.5f) * (u - 0.5f) + (v - 0.5f) * (v - 0.5f) > ClipRadius * ClipRadius) continue;
                    if (inside(u, v)) Blend(x, y, c);
                }
            }

            public void Ellipse(Color32 c, float cx, float cy, float rx, float ry) =>
                Fill((x, y) => (x - cx) * (x - cx) / (rx * rx) + (y - cy) * (y - cy) / (ry * ry) <= 1f, c, cx - rx, cy - ry, cx + rx, cy + ry);

            public void Line(Color32 c, (float x, float y) a, (float x, float y) b, float width)
            {
                var pa = new V2(a.x, a.y);
                var pb = new V2(b.x, b.y);
                var r = width * 0.5f;
                Fill((x, y) =>
                {
                    var p = new V2(x, y);
                    var ab = pb - pa;
                    var t = MathUtil.Clamp01(V2.Dot(p - pa, ab) / Math.Max(1e-6f, ab.LengthSquared));
                    return (pa + ab * t - p).Length <= r;
                }, c, Math.Min(a.x, b.x) - r, Math.Min(a.y, b.y) - r, Math.Max(a.x, b.x) + r, Math.Max(a.y, b.y) + r);
            }

            public void Polygon(Color32 c, params (float x, float y)[] points)
            {
                float x0 = 1, y0 = 1, x1 = 0, y1 = 0;
                foreach (var p in points)
                {
                    x0 = Math.Min(x0, p.x); y0 = Math.Min(y0, p.y);
                    x1 = Math.Max(x1, p.x); y1 = Math.Max(y1, p.y);
                }
                Fill((x, y) =>
                {
                    // even-odd rule
                    var inside = false;
                    for (int i = 0, j = points.Length - 1; i < points.Length; j = i++)
                    {
                        var (xi, yi) = points[i];
                        var (xj, yj) = points[j];
                        if (yi > y != yj > y && x < (xj - xi) * (y - yi) / (yj - yi) + xi) inside = !inside;
                    }
                    return inside;
                }, c, x0, y0, x1, y1);
            }
        }
    }
}
