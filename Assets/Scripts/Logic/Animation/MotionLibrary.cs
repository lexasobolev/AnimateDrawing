using System;
using System.Collections.Generic;
using AnimatedDrawingsWorld.Logic.Behavior;

namespace AnimatedDrawingsWorld.Logic.Animation
{
    // Which motion drives each activity: a Meta BVH clip (retargeted by Meta's code) when
    // one fits, otherwise a procedural pose on the same rig.
    public sealed class MotionLibrary
    {
        public readonly Dictionary<string, MotionClip> Clips = new();

        // activity -> (clip name, playback speed); activities absent here are procedural
        private static readonly Dictionary<Activity, (string clip, float speed, bool loop)> ClipForActivity = new()
        {
            [Activity.Walk] = ("zombie", 1f, true),
            [Activity.Run] = ("zombie", 1.9f, true),
            [Activity.Wave] = ("wave_hello", 1f, true),
            [Activity.Surprised] = ("jumping", 1.2f, false),
            [Activity.Dance] = ("jesse_dance", 1f, true),
            [Activity.Jump] = ("jumping_jacks", 1f, true),
            [Activity.Scare] = ("dab", 1.3f, true),
        };

        // alternative dances so two characters dancing together don't mirror each other exactly
        public static readonly string[] DanceClips = { "jesse_dance", "dab", "jumping_jacks" };

        public void Add(MotionClip clip) => Clips[clip.Name] = clip;

        public bool TryGetClip(Activity activity, out MotionClip clip, out float speed, out bool loop, string overrideClip = null)
        {
            clip = null;
            speed = 1f;
            loop = true;
            if (overrideClip != null && Clips.TryGetValue(overrideClip, out clip)) return true;
            if (!ClipForActivity.TryGetValue(activity, out var entry)) return false;
            speed = entry.speed;
            loop = entry.loop;
            return Clips.TryGetValue(entry.clip, out clip);
        }

        public static IEnumerable<string> RequiredClipNames()
        {
            var names = new HashSet<string>(DanceClips);
            foreach (var entry in ClipForActivity.Values) names.Add(entry.clip);
            return names;
        }
    }

    // Per-character animation state: samples the right source for the current activity and
    // produces the posed rig, draw order and vertical bob.
    public sealed class CharacterAnimator
    {
        public readonly DrawingRig Rig;
        public readonly SkinnedDrawingMesh Mesh;
        public readonly RigPose Pose;
        public readonly V2[] DeformedVertices;
        public readonly int[] DrawOrder;
        public float RootOffsetY; // rig units (pixels), positive = up
        public Activity CurrentActivity { get; private set; } = Activity.Idle;
        public float ActivityTime { get; private set; }

        private readonly MotionLibrary library;
        private readonly Dictionary<string, float> orientations = new();
        private readonly Dictionary<string, float> previous = new();
        private readonly List<string> keys = new();
        private readonly float[] groupDepths = new float[5];
        private string clipOverride;
        private float blend = 1f;
        private readonly float legLength;

        public CharacterAnimator(DrawingRig rig, SkinnedDrawingMesh mesh, MotionLibrary library)
        {
            Rig = rig;
            Mesh = mesh;
            this.library = library;
            Pose = rig.CreatePose();
            rig.SetRestPose(Pose);
            DeformedVertices = new V2[mesh.VertexCount];
            DrawOrder = new int[mesh.Triangles.Length];
            legLength = LegLength(rig);
            Update(Activity.Idle, 0f);
        }

        public void Play(Activity activity, string clip = null)
        {
            if (activity == CurrentActivity && clip == clipOverride) return;
            // remember the current pose so the new motion blends in instead of snapping
            previous.Clear();
            foreach (var pair in orientations) previous[pair.Key] = pair.Value;
            CurrentActivity = activity;
            clipOverride = clip;
            ActivityTime = 0f;
            blend = 0f;
        }

        public void Update(Activity activity, float deltaTime, float speedMultiplier = 1f, string clip = null)
        {
            Play(activity, clip);
            ActivityTime += deltaTime * speedMultiplier;
            blend = Math.Min(1f, blend + deltaTime / 0.25f);

            orientations.Clear();
            float rootY;
            float[] depths = null;
            if (!ProceduralMotions.Supports(CurrentActivity) &&
                library.TryGetClip(CurrentActivity, out var motion, out var speed, out var loop, clipOverride))
            {
                motion.Sample(ActivityTime * speed, loop, orientations, out rootY, groupDepths);
                depths = motion.DepthGroups.Length == groupDepths.Length ? groupDepths : null;
            }
            else
            {
                ProceduralMotions.Evaluate(CurrentActivity, Rig, ActivityTime, orientations, out rootY);
            }

            if (blend < 1f && previous.Count > 0)
            {
                var t = MathUtil.SmoothStep(blend);
                keys.Clear();
                keys.AddRange(orientations.Keys);
                foreach (var key in keys)
                    if (previous.TryGetValue(key, out var from)) orientations[key] = MathUtil.LerpAngle(from, orientations[key], t);
            }

            Rig.Solve(orientations, Pose);
            Mesh.Deform(Rig, Pose, DeformedVertices);
            Mesh.BuildDrawOrder(Rig, null, depths, DrawOrder);
            RootOffsetY = rootY * legLength;
        }

        private static float LegLength(DrawingRig rig)
        {
            float Chain(params string[] names)
            {
                var total = 0f;
                for (var i = 1; i < names.Length; i++)
                {
                    var a = rig.IndexOf(names[i - 1]);
                    var b = rig.IndexOf(names[i]);
                    if (a >= 0 && b >= 0) total += V2.Distance(rig.Rest[a], rig.Rest[b]);
                }
                return total;
            }
            var legs = 0.5f * (Chain("left_hip", "left_knee", "left_foot") + Chain("right_hip", "right_knee", "right_foot"));
            return legs > 1f ? legs : rig.Height * 0.4f;
        }
    }
}
