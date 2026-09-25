using System;
using AnimatedDrawingsWorld.Logic.Behavior;

namespace AnimatedDrawingsWorld.Logic.Animation
{
    // 2x2 matrix + translation
    public struct Affine2
    {
        public float M00, M01, M10, M11;
        public V2 T;

        public V2 Apply(V2 p) => new(M00 * p.X + M01 * p.Y + T.X, M10 * p.X + M11 * p.Y + T.Y);

        public float RotationDegrees => MathF.Atan2(M10, M00) * 180f / MathF.PI;
    }

    // Where and how a character's rig is drawn in the world for its current activity:
    // standing on its feet, sitting on its bottom, lying down to sleep, swimming...
    // Shared by the Unity renderer and the headless test renderer so both agree.
    public static class Placement
    {
        public static Affine2 RigToWorld(Agent agent, CharacterAnimator animator, float perspectiveScale)
        {
            var rig = animator.Rig;
            var scale = agent.BodyHeight * perspectiveScale / Math.Max(1f, BodyExtent(rig));
            var flip = agent.FacingRight ? 1f : -1f;

            var root = rig.Rest[Math.Max(0, rig.IndexOf("root"))];
            var footY = Math.Min(Y(rig, "left_foot", root.Y), Y(rig, "right_foot", root.Y));
            var anchor = new V2(root.X, footY);
            var rotation = agent.BodyRotation;
            var lift = 0f;

            switch (agent.Activity)
            {
                case Activity.Sit:
                    // sit on the bottom: hips rest on the seat
                    anchor = new V2(root.X, root.Y - (root.Y - footY) * 0.08f);
                    break;
                case Activity.Sleep:
                    // lie on the side: pivot around the hips, raise by half the body's thickness
                    anchor = root;
                    lift = BodyThickness(rig) * 0.5f * scale;
                    break;
                case Activity.Swim:
                    // chest-deep, leaning into the stroke
                    anchor = root;
                    rotation = agent.FacingRight ? -55f : 55f;
                    lift = -0.05f * agent.BodyHeight;
                    break;
            }

            var r = rotation * MathF.PI / 180f;
            var c = MathF.Cos(r);
            var s = MathF.Sin(r);
            // world = feet + R * (flip*s, s) * (v - anchor) + bob
            var sx = scale * flip;
            var sy = scale;
            var affine = new Affine2
            {
                M00 = c * sx,
                M01 = -s * sy,
                M10 = s * sx,
                M11 = c * sy,
            };
            var bob = new V2(0f, animator.RootOffsetY * scale + lift);
            var anchored = new V2(affine.M00 * anchor.X + affine.M01 * anchor.Y, affine.M10 * anchor.X + affine.M11 * anchor.Y);
            affine.T = agent.Feet + bob - anchored;
            return affine;
        }

        // characters drawn later appear in front: lower on the page = nearer; climbers are in
        // front of the background objects they climb, so they sort as "near"
        public static float SortDepth(Agent agent, float worldHeight) =>
            agent.OffGround && agent.Activity != Activity.Swim ? -1f : agent.Feet.Y / Math.Max(1f, worldHeight);

        private static float Y(DrawingRig rig, string name, float fallback)
        {
            var i = rig.IndexOf(name);
            return i >= 0 ? rig.Rest[i].Y : fallback;
        }

        // distance from the lowest foot to the top of the drawing, in rig pixels
        public static float BodyExtent(DrawingRig rig)
        {
            var footY = float.MaxValue;
            foreach (var name in new[] { "left_foot", "right_foot" })
            {
                var i = rig.IndexOf(name);
                if (i >= 0) footY = Math.Min(footY, rig.Rest[i].Y);
            }
            if (footY == float.MaxValue) footY = 0f;
            return Math.Max(1f, rig.Height - footY);
        }

        private static float BodyThickness(DrawingRig rig)
        {
            var l = rig.IndexOf("left_shoulder");
            var r = rig.IndexOf("right_shoulder");
            if (l < 0 || r < 0) return rig.Width * 0.3f;
            return Math.Max(rig.Width * 0.2f, Math.Abs(rig.Rest[l].X - rig.Rest[r].X) * 2f);
        }
    }
}
