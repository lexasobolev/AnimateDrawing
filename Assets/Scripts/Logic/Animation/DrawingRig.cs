using System;
using System.Collections.Generic;
using AnimatedDrawingsWorld.Logic.Characters;

namespace AnimatedDrawingsWorld.Logic.Animation
{
    // C# port of Meta's AnimatedDrawingRig (animated_drawings/model/animated_drawing.py).
    // Rig space: texture pixels with y UP (Meta builds joints at [x, 1 - y]).
    // Orientations use Meta's convention: degrees counter-clockwise from +Y of the bone that
    // ENDS at the named joint (e.g. "left_elbow" = direction of the upper arm).
    public sealed class DrawingRig
    {
        public readonly int JointCount;
        public readonly string[] Names;
        public readonly int[] Parent;          // -1 for root
        public readonly V2[] Rest;             // rest positions, rig space
        public readonly float[] StartingTheta; // rest orientation of bone parent->joint
        public readonly int[] Order;           // parents before children
        public readonly float Width;
        public readonly float Height;
        private readonly Dictionary<string, int> indexOf = new();

        // Meta applies the rotation for joint J to J's parent, so we need children per joint
        private readonly List<int>[] children;

        public DrawingRig(CharacterAnnotation annotation)
        {
            JointCount = annotation.Skeleton.Count;
            Names = new string[JointCount];
            Parent = new int[JointCount];
            Rest = new V2[JointCount];
            StartingTheta = new float[JointCount];
            Width = annotation.Width;
            Height = annotation.Height;

            for (var i = 0; i < JointCount; i++)
            {
                var j = annotation.Skeleton[i];
                Names[i] = j.Name;
                indexOf[j.Name] = i;
                Rest[i] = new V2(j.Location.X, annotation.Height - j.Location.Y);
            }

            children = new List<int>[JointCount];
            for (var i = 0; i < JointCount; i++) children[i] = new List<int>();
            for (var i = 0; i < JointCount; i++)
            {
                var parentName = annotation.Skeleton[i].Parent;
                Parent[i] = parentName != null && indexOf.TryGetValue(parentName, out var p) ? p : -1;
                if (Parent[i] >= 0) children[Parent[i]].Add(i);
            }

            for (var i = 0; i < JointCount; i++)
                StartingTheta[i] = Parent[i] < 0 ? 0f : (Rest[i] - Rest[Parent[i]]).AngleFromUp();

            var order = new List<int>();
            var visited = new bool[JointCount];
            void Visit(int i)
            {
                if (visited[i]) return;
                if (Parent[i] >= 0) Visit(Parent[i]);
                visited[i] = true;
                order.Add(i);
            }
            for (var i = 0; i < JointCount; i++) Visit(i);
            Order = order.ToArray();
        }

        public int IndexOf(string name) => indexOf.TryGetValue(name, out var i) ? i : -1;

        public V2 RestOf(string name) => Rest[IndexOf(name)];

        public float BoneLength(int joint) => Parent[joint] < 0 ? 0f : V2.Distance(Rest[joint], Rest[Parent[joint]]);

        public RigPose CreatePose() => new(JointCount);

        public void SetRestPose(RigPose pose)
        {
            Array.Copy(Rest, pose.Positions, JointCount);
            Array.Clear(pose.GlobalRotation, 0, JointCount);
        }

        // Equivalent of AnimatedDrawingRig._set_global_orientations followed by a transform update.
        // `orientations` maps joint names to absolute bone angles; joints without an entry keep
        // their drawn angle relative to their parent. Mirrors Meta exactly: the bone ending at a
        // mapped joint J gets local rotation (theta_J - theta_parent) at J's parent, where theta is
        // "orientation minus drawn orientation" and an unmapped parent counts as 0 — so, as in
        // Meta's renderer, arms/legs also inherit the trunk's lean.
        public void Solve(IReadOnlyDictionary<string, float> orientations, RigPose pose)
        {
            for (var k = 0; k < Order.Length; k++)
            {
                var i = Order[k];
                var parent = Parent[i];
                var local = 0f;
                foreach (var c in children[i])
                {
                    if (!orientations.TryGetValue(Names[c], out var target)) continue;
                    var thetaChild = target - StartingTheta[c];
                    var thetaSelf = orientations.TryGetValue(Names[i], out var own) && parent >= 0 ? own - StartingTheta[i] : 0f;
                    local = thetaChild - thetaSelf;
                }
                pose.GlobalRotation[i] = (parent >= 0 ? pose.GlobalRotation[parent] : 0f) + local;
                pose.Positions[i] = parent < 0
                    ? Rest[i]
                    : pose.Positions[parent] + (Rest[i] - Rest[parent]).Rotate(pose.GlobalRotation[parent]);
            }
        }

        // where a rest-pose point attached to the bone that ends at `joint` goes in `pose`
        public V2 TransformByBone(int joint, V2 restPoint, RigPose pose)
        {
            var parent = Parent[joint];
            if (parent < 0) return pose.Positions[joint] + (restPoint - Rest[joint]);
            return pose.Positions[parent] + (restPoint - Rest[parent]).Rotate(pose.GlobalRotation[parent]);
        }

        // lowest point of the posed skeleton (feet), used to keep characters standing on the ground
        public float MinY(RigPose pose)
        {
            var min = float.MaxValue;
            foreach (var p in pose.Positions) if (p.Y < min) min = p.Y;
            return min;
        }

        // the rest orientation of every bone, i.e. "stand like you were drawn" (cached)
        public IReadOnlyDictionary<string, float> DrawnOrientations()
        {
            if (drawn != null) return drawn;
            drawn = new Dictionary<string, float>();
            for (var i = 0; i < JointCount; i++) if (Parent[i] >= 0) drawn[Names[i]] = StartingTheta[i];
            return drawn;
        }

        private Dictionary<string, float> drawn;
    }

    public sealed class RigPose
    {
        public readonly V2[] Positions;
        public readonly float[] GlobalRotation; // degrees, rotation applied to this joint's child bones

        public RigPose(int jointCount)
        {
            Positions = new V2[jointCount];
            GlobalRotation = new float[jointCount];
        }
    }
}
