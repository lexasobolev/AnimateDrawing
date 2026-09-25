using System;
using System.Collections.Generic;

namespace AnimatedDrawingsWorld.Logic.Animation
{
    // A Meta BVH motion after Meta's Retargeter has projected it to 2D bone orientations
    // (baked by Tools/MotionBaker/bake_meta_motions.py into StreamingAssets/.../Motions/*.json).
    public sealed class MotionClip
    {
        public string Name;
        public float FrameTime;
        public int FrameCount;
        public string[] Joints;
        public float[] Orientations; // frame-major, FrameCount * Joints.Length
        public float[] RootY;        // vertical root offset per frame, in leg lengths
        public DepthGroup[] DepthGroups;

        public float Duration => FrameCount * FrameTime;

        public sealed class DepthGroup
        {
            public string Driver;
            public string[] CharJoints;
            public float[] Depths;
        }

        public static MotionClip FromJson(string json)
        {
            var root = MiniJson.Obj(MiniJson.Parse(json)) ?? throw new FormatException("Motion clip JSON must be an object");
            var clip = new MotionClip
            {
                Name = MiniJson.Str(MiniJson.Get(root, "name"), "motion"),
                FrameTime = MiniJson.Num(MiniJson.Get(root, "frameTime"), 1f / 30f),
                FrameCount = (int)MiniJson.Num(MiniJson.Get(root, "frameCount")),
            };
            clip.Joints = ToStrings(MiniJson.Arr(MiniJson.Get(root, "joints")));
            clip.Orientations = ToFloats(MiniJson.Arr(MiniJson.Get(root, "orientations")));
            clip.RootY = ToFloats(MiniJson.Arr(MiniJson.Get(root, "rootY")));
            if (clip.RootY.Length == 0) clip.RootY = new float[clip.FrameCount];

            var groups = new List<DepthGroup>();
            var rawGroups = MiniJson.Arr(MiniJson.Get(root, "depthGroups"));
            if (rawGroups != null)
            {
                foreach (var g in rawGroups)
                {
                    var o = MiniJson.Obj(g);
                    groups.Add(new DepthGroup
                    {
                        Driver = MiniJson.Str(MiniJson.Get(o, "driver")),
                        CharJoints = ToStrings(MiniJson.Arr(MiniJson.Get(o, "charJoints"))),
                        Depths = ToFloats(MiniJson.Arr(MiniJson.Get(o, "depths"))),
                    });
                }
            }
            clip.DepthGroups = groups.ToArray();

            if (clip.FrameCount <= 0 || clip.Orientations.Length != clip.FrameCount * clip.Joints.Length)
                throw new FormatException($"Motion clip '{clip.Name}' has inconsistent frame data");
            return clip;
        }

        // Samples with linear (shortest-arc) interpolation between frames.
        public void Sample(float time, bool loop, IDictionary<string, float> orientations, out float rootY, float[] groupDepths = null)
        {
            var f = time / FrameTime;
            if (loop)
            {
                f %= FrameCount;
                if (f < 0f) f += FrameCount;
            }
            else
            {
                f = MathUtil.Clamp(f, 0f, FrameCount - 1);
            }

            var f0 = (int)MathF.Floor(f);
            var f1 = loop ? (f0 + 1) % FrameCount : Math.Min(f0 + 1, FrameCount - 1);
            var t = f - f0;
            var n = Joints.Length;
            for (var j = 0; j < n; j++)
                orientations[Joints[j]] = MathUtil.LerpAngle(Orientations[f0 * n + j], Orientations[f1 * n + j], t);
            rootY = MathUtil.Lerp(RootY[f0], RootY[f1], t);

            if (groupDepths == null) return;
            for (var g = 0; g < DepthGroups.Length && g < groupDepths.Length; g++)
            {
                var d = DepthGroups[g].Depths;
                groupDepths[g] = d.Length == 0 ? 0f : MathUtil.Lerp(d[f0 % d.Length], d[f1 % d.Length], t);
            }
        }

        private static string[] ToStrings(List<object> list)
        {
            if (list == null) return Array.Empty<string>();
            var result = new string[list.Count];
            for (var i = 0; i < list.Count; i++) result[i] = list[i] as string;
            return result;
        }

        private static float[] ToFloats(List<object> list)
        {
            if (list == null) return Array.Empty<float>();
            var result = new float[list.Count];
            for (var i = 0; i < list.Count; i++) result[i] = MiniJson.Num(list[i]);
            return result;
        }
    }
}
