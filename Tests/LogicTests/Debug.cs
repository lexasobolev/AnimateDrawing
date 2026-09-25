using System;
using AnimatedDrawingsWorld.Logic;
using AnimatedDrawingsWorld.Logic.Characters;
using AnimatedDrawingsWorld.Logic.Imaging;
using AnimatedDrawingsWorld.Logic.Scene;

namespace AnimatedDrawingsWorld.Tests
{
    // Tiny raster helpers for writing inspection images from tests.
    public static class Debug
    {
        public static void Line(RgbaImage img, V2 a, V2 b, (byte r, byte g, byte b) c, int thickness = 2)
        {
            var steps = (int)Math.Max(1, V2.Distance(a, b));
            for (var i = 0; i <= steps; i++)
            {
                var p = V2.Lerp(a, b, i / (float)steps);
                Dot(img, p, thickness, c);
            }
        }

        public static void Dot(RgbaImage img, V2 p, int radius, (byte r, byte g, byte b) c)
        {
            for (var dy = -radius; dy <= radius; dy++)
            for (var dx = -radius; dx <= radius; dx++)
            {
                if (dx * dx + dy * dy > radius * radius) continue;
                var x = (int)p.X + dx;
                var y = (int)p.Y + dy;
                if (img.InBounds(x, y)) img.Set(x, y, c.r, c.g, c.b, 255);
            }
        }

        public static void Rect(RgbaImage img, float x0, float y0, float x1, float y1, (byte, byte, byte) c, int t = 2)
        {
            Line(img, new V2(x0, y0), new V2(x1, y0), c, t);
            Line(img, new V2(x1, y0), new V2(x1, y1), c, t);
            Line(img, new V2(x1, y1), new V2(x0, y1), c, t);
            Line(img, new V2(x0, y1), new V2(x0, y0), c, t);
        }

        public static RgbaImage DrawSkeleton(RgbaImage texture, CharacterAnnotation a)
        {
            var img = OnWhite(texture);
            foreach (var j in a.Skeleton)
            {
                if (j.Parent == null) continue;
                Line(img, a.Joint(j.Parent), j.Location, (220, 30, 30), 2);
            }
            foreach (var j in a.Skeleton) Dot(img, j.Location, 4, j.Name.StartsWith("left") ? ((byte)30, (byte)90, (byte)230) : ((byte)20, (byte)160, (byte)40));
            return img;
        }

        public static RgbaImage OnWhite(RgbaImage texture)
        {
            var img = new RgbaImage(texture.Width, texture.Height);
            for (var i = 0; i < texture.Width * texture.Height; i++)
            {
                var a = texture.Pixels[i * 4 + 3] / 255f;
                for (var k = 0; k < 3; k++) img.Pixels[i * 4 + k] = (byte)(texture.Pixels[i * 4 + k] * a + 255 * (1 - a));
                img.Pixels[i * 4 + 3] = 255;
            }
            return img;
        }

        public static (byte, byte, byte) ColorFor(SceneObjectKind kind) => kind switch
        {
            SceneObjectKind.Sky => (0, 120, 255),
            SceneObjectKind.Grass => (0, 200, 0),
            SceneObjectKind.Water => (0, 0, 180),
            SceneObjectKind.Tree => (120, 60, 0),
            SceneObjectKind.Sun => (255, 160, 0),
            SceneObjectKind.Cloud => (120, 120, 255),
            SceneObjectKind.House => (220, 0, 0),
            SceneObjectKind.Flower => (255, 0, 200),
            SceneObjectKind.Rock => (90, 90, 90),
            SceneObjectKind.Mountain => (120, 0, 160),
            _ => (0, 0, 0),
        };

        public static RgbaImage DrawLayout(RgbaImage background, SceneLayout layout)
        {
            var img = background.Clone();
            float W = img.Width, H = img.Height;
            foreach (var o in layout.Objects)
            {
                var b = o.Bounds;
                Rect(img, b.XMin * W, (1 - b.YMax) * H, b.XMax * W, (1 - b.YMin) * H, ColorFor(o.Kind), 3);
                if (o.Kind == SceneObjectKind.Tree)
                    Rect(img, o.Trunk.XMin * W, (1 - o.Trunk.YMax) * H, o.Trunk.XMax * W, (1 - o.Trunk.YMin) * H, (255, 255, 0), 2);
            }
            for (var i = 1; i < layout.GroundTop.Length; i++)
            {
                var x0 = (i - 1) / (float)(layout.GroundTop.Length - 1) * W;
                var x1 = i / (float)(layout.GroundTop.Length - 1) * W;
                Line(img, new V2(x0, (1 - layout.GroundTop[i - 1]) * H), new V2(x1, (1 - layout.GroundTop[i]) * H), (255, 0, 255), 3);
            }
            return img;
        }
    }
}
