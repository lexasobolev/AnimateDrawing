using System;
using System.Collections.Generic;
using System.Linq;
using AnimatedDrawingsWorld.Logic;
using AnimatedDrawingsWorld.Logic.Animation;
using AnimatedDrawingsWorld.Logic.Behavior;
using AnimatedDrawingsWorld.Logic.Imaging;

namespace AnimatedDrawingsWorld.Tests
{
    // Headless stand-in for the Unity scene: draws the background and every character's
    // skinned mesh (textured triangles, painter's order) so simulations can be inspected as
    // images/GIFs without the Unity Editor.
    public sealed class SoftwareRenderer
    {
        private readonly RgbaImage background;
        private readonly int width;
        private readonly int height;

        public SoftwareRenderer(RgbaImage backgroundImage, int outputWidth)
        {
            width = outputWidth;
            height = (int)MathF.Round(outputWidth * backgroundImage.Height / (float)backgroundImage.Width);
            background = backgroundImage.Resize(width, height);
        }

        public RgbaImage Render(GameWorld world, IReadOnlyDictionary<Agent, (CharacterAnimator animator, RgbaImage texture)> characters)
        {
            var frame = background.Clone();
            foreach (var agent in world.Agents.OrderByDescending(a => Placement.SortDepth(a, world.Height)))
            {
                if (!characters.TryGetValue(agent, out var c)) continue;
                var toWorld = Placement.RigToWorld(agent, c.animator, world.PerspectiveScale(agent));
                V2 ToPixel(V2 rigPoint)
                {
                    var w = toWorld.Apply(rigPoint);
                    return new V2(w.X / world.Width * width, (1f - w.Y / world.Height) * height);
                }

                var verts = c.animator.DeformedVertices.Select(ToPixel).ToArray();
                var order = c.animator.DrawOrder;
                var uvs = c.animator.Mesh.UVs;
                for (var t = 0; t + 2 < order.Length; t += 3)
                    Triangle(frame, c.texture, verts[order[t]], verts[order[t + 1]], verts[order[t + 2]],
                        uvs[order[t]], uvs[order[t + 1]], uvs[order[t + 2]], agent.Opacity);
            }
            return frame;
        }

        private static void Triangle(RgbaImage dst, RgbaImage tex, V2 p0, V2 p1, V2 p2, V2 t0, V2 t1, V2 t2, float opacity)
        {
            var minX = Math.Max(0, (int)MathF.Floor(Math.Min(p0.X, Math.Min(p1.X, p2.X))));
            var maxX = Math.Min(dst.Width - 1, (int)MathF.Ceiling(Math.Max(p0.X, Math.Max(p1.X, p2.X))));
            var minY = Math.Max(0, (int)MathF.Floor(Math.Min(p0.Y, Math.Min(p1.Y, p2.Y))));
            var maxY = Math.Min(dst.Height - 1, (int)MathF.Ceiling(Math.Max(p0.Y, Math.Max(p1.Y, p2.Y))));
            var area = V2.Cross(p1 - p0, p2 - p0);
            if (Math.Abs(area) < 1e-6f) return;
            for (var y = minY; y <= maxY; y++)
            for (var x = minX; x <= maxX; x++)
            {
                var p = new V2(x + 0.5f, y + 0.5f);
                var w0 = V2.Cross(p1 - p, p2 - p) / area;
                var w1 = V2.Cross(p2 - p, p0 - p) / area;
                var w2 = 1f - w0 - w1;
                if (w0 < -1e-4f || w1 < -1e-4f || w2 < -1e-4f) continue;
                var uv = t0 * w0 + t1 * w1 + t2 * w2;
                var tx = MathUtil.Clamp((int)(uv.X * tex.Width), 0, tex.Width - 1);
                var ty = MathUtil.Clamp((int)((1f - uv.Y) * tex.Height), 0, tex.Height - 1);
                tex.Get(tx, ty, out var r, out var g, out var b, out var a);
                var alpha = a / 255f * opacity;
                if (alpha <= 0.01f) continue;
                var i = dst.Index(x, y);
                dst.Pixels[i] = (byte)(dst.Pixels[i] * (1 - alpha) + r * alpha);
                dst.Pixels[i + 1] = (byte)(dst.Pixels[i + 1] * (1 - alpha) + g * alpha);
                dst.Pixels[i + 2] = (byte)(dst.Pixels[i + 2] * (1 - alpha) + b * alpha);
            }
        }
    }
}
