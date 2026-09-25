using System;
using System.Collections.Generic;
using AnimatedDrawingsWorld.Logic.Behavior;

namespace AnimatedDrawingsWorld.Logic.Animation
{
    // Poses Meta's sample BVH files don't contain, expressed in the same form Meta's retargeter
    // produces (absolute bone angles, degrees CCW from +Y; 180 = pointing down). Since the
    // drawing is front-facing, "right_*" limbs are on the image left: 90 = pointing image-left,
    // 270 = pointing image-right.
    public static class ProceduralMotions
    {
        public static bool Supports(Activity activity) => activity switch
        {
            Activity.Idle or Activity.Climb or Activity.Sit or Activity.Sleep or Activity.Swim or
            Activity.Smell or Activity.Yawn or Activity.Fall or Activity.Hide or Activity.Talk => true,
            _ => false,
        };

        public static void Evaluate(Activity activity, DrawingRig rig, float t, IDictionary<string, float> o, out float rootY)
        {
            rootY = 0f;
            const float twoPi = MathF.PI * 2f;
            switch (activity)
            {
                case Activity.Climb:
                {
                    // hand-over-hand: one arm reaches up while the other pulls, legs push alternately
                    var s = MathF.Sin(t * twoPi * 1.1f);
                    o["torso"] = 0f;
                    o["neck"] = 0f;
                    o["right_elbow"] = 25f + 25f * s;
                    o["right_hand"] = 5f + 30f * s;
                    o["left_elbow"] = 335f + 25f * s;
                    o["left_hand"] = 355f + 30f * s;
                    o["right_knee"] = 150f - 25f * s;
                    o["right_foot"] = 185f + 15f * s;
                    o["left_knee"] = 210f - 25f * s;
                    o["left_foot"] = 175f + 15f * s;
                    rootY = 0.04f * s;
                    break;
                }
                case Activity.Sit:
                {
                    var breathe = MathF.Sin(t * twoPi * 0.3f);
                    o["torso"] = 0f;
                    o["neck"] = 2f * breathe;
                    o["right_elbow"] = 160f;
                    o["right_hand"] = 200f;
                    o["left_elbow"] = 200f;
                    o["left_hand"] = 160f;
                    // knees splay outward, shins hang: a front view of someone sitting on a ledge
                    o["right_knee"] = 115f;
                    o["right_foot"] = 175f + 8f * MathF.Sin(t * twoPi * 0.7f);
                    o["left_knee"] = 245f;
                    o["left_foot"] = 185f + 8f * MathF.Sin(t * twoPi * 0.7f + 1.3f);
                    break;
                }
                case Activity.Sleep:
                {
                    // the body is laid down by the renderer; here: limp arms, straight legs, breathing
                    var breathe = MathF.Sin(t * twoPi * 0.25f);
                    o["torso"] = 0f;
                    o["neck"] = 4f + 2f * breathe;
                    o["right_elbow"] = 165f + 3f * breathe;
                    o["right_hand"] = 150f;
                    o["left_elbow"] = 195f - 3f * breathe;
                    o["left_hand"] = 210f;
                    o["right_knee"] = 178f;
                    o["right_foot"] = 175f;
                    o["left_knee"] = 182f;
                    o["left_foot"] = 185f;
                    break;
                }
                case Activity.Swim:
                {
                    // crawl stroke: arms windmill in opposite phase, legs flutter
                    var a = t * twoPi * 0.8f;
                    o["torso"] = 5f * MathF.Sin(a);
                    o["neck"] = 0f;
                    o["right_elbow"] = Deg(a) ;
                    o["right_hand"] = Deg(a) + 20f;
                    o["left_elbow"] = 360f - Deg(a + MathF.PI);
                    o["left_hand"] = 340f - Deg(a + MathF.PI);
                    o["right_knee"] = 180f + 15f * MathF.Sin(a * 3f);
                    o["right_foot"] = 180f + 20f * MathF.Sin(a * 3f + 0.5f);
                    o["left_knee"] = 180f - 15f * MathF.Sin(a * 3f);
                    o["left_foot"] = 180f - 20f * MathF.Sin(a * 3f + 0.5f);
                    rootY = -0.12f + 0.03f * MathF.Sin(a * 2f);
                    break;
                }
                case Activity.Smell:
                {
                    // lean over (a flower) and sway a little with pleasure
                    var sway = MathF.Sin(t * twoPi * 0.5f);
                    o["torso"] = 25f + 4f * sway;
                    o["neck"] = 40f + 6f * sway;
                    o["right_elbow"] = 150f;
                    o["right_hand"] = 120f;
                    o["left_elbow"] = 200f;
                    o["left_hand"] = 160f;
                    o["right_knee"] = 175f;
                    o["right_foot"] = 180f;
                    o["left_knee"] = 185f;
                    o["left_foot"] = 180f;
                    break;
                }
                case Activity.Yawn:
                {
                    // a big stretch: arms rise over ~1s, hold, lower
                    var k = MathUtil.SmoothStep(MathUtil.Clamp01(MathF.Min(t / 0.8f, (2.4f - t) / 0.8f)));
                    Blend(o, rig.DrawnOrientations(), YawnStretch, k);
                    rootY = 0.02f * k;
                    break;
                }
                case Activity.Fall:
                {
                    var flail = MathF.Sin(t * twoPi * 3f);
                    o["right_elbow"] = 50f + 20f * flail;
                    o["right_hand"] = 30f - 20f * flail;
                    o["left_elbow"] = 310f - 20f * flail;
                    o["left_hand"] = 330f + 20f * flail;
                    o["right_knee"] = 160f + 15f * flail;
                    o["right_foot"] = 190f;
                    o["left_knee"] = 200f - 15f * flail;
                    o["left_foot"] = 170f;
                    break;
                }
                case Activity.Hide:
                {
                    // crouch with hands over the eyes
                    o["torso"] = 0f;
                    o["neck"] = 0f;
                    o["right_elbow"] = 200f;
                    o["right_hand"] = 330f;
                    o["left_elbow"] = 160f;
                    o["left_hand"] = 30f;
                    o["right_knee"] = 135f;
                    o["right_foot"] = 215f;
                    o["left_knee"] = 225f;
                    o["left_foot"] = 145f;
                    rootY = -0.15f;
                    break;
                }
                case Activity.Talk:
                {
                    // gesturing with one hand while bobbing slightly
                    var g = MathF.Sin(t * twoPi * 1.3f);
                    CopyDrawn(rig, o);
                    o["left_elbow"] = 230f + 15f * g;
                    o["left_hand"] = 300f + 25f * g;
                    o["neck"] = 355f + 5f * MathF.Sin(t * twoPi * 0.9f);
                    rootY = 0.01f * MathF.Abs(g);
                    break;
                }
                default: // Idle: stand as drawn, breathing and swaying gently
                {
                    CopyDrawn(rig, o);
                    var breathe = MathF.Sin(t * twoPi * 0.35f);
                    Add(o, "neck", 3f * MathF.Sin(t * twoPi * 0.21f));
                    Add(o, "right_elbow", -4f * breathe);
                    Add(o, "right_hand", -6f * breathe);
                    Add(o, "left_elbow", 4f * breathe);
                    Add(o, "left_hand", 6f * breathe);
                    rootY = 0.008f * breathe;
                    break;
                }
            }
        }

        private static readonly Dictionary<string, float> YawnStretch = new()
        {
            ["right_elbow"] = 30f, ["right_hand"] = 15f,
            ["left_elbow"] = 330f, ["left_hand"] = 345f,
            ["neck"] = 350f, ["torso"] = 0f,
        };

        private static float Deg(float radians)
        {
            var d = radians * 180f / MathF.PI % 360f;
            return d < 0f ? d + 360f : d;
        }

        private static void CopyDrawn(DrawingRig rig, IDictionary<string, float> o)
        {
            foreach (var pair in rig.DrawnOrientations()) o[pair.Key] = pair.Value;
        }

        private static void Add(IDictionary<string, float> o, string joint, float delta)
        {
            if (o.TryGetValue(joint, out var v)) o[joint] = v + delta;
        }

        private static void Blend(IDictionary<string, float> o, IReadOnlyDictionary<string, float> from, IReadOnlyDictionary<string, float> to, float t)
        {
            foreach (var pair in from) o[pair.Key] = pair.Value;
            foreach (var pair in to)
                o[pair.Key] = from.TryGetValue(pair.Key, out var a) ? MathUtil.LerpAngle(a, pair.Value, t) : pair.Value;
        }
    }
}
