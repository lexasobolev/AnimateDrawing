using System;
using System.Collections.Generic;
using AnimatedDrawingsWorld.Logic.Characters;
using AnimatedDrawingsWorld.Logic.Imaging;

namespace AnimatedDrawingsWorld.Logic.Animation
{
    // Triangle mesh over the character mask, skinned to the Meta rig.
    //
    // Meta's renderer assigns each triangle to the bone that is closest *through the mask*
    // (a BFS that never leaves the character) and deforms with ARAP. Here each vertex gets soft
    // weights from per-bone geodesic distance fields (same "closest through the drawing" idea,
    // so an arm drawn next to the body doesn't drag the body), and is deformed with linear blend
    // skinning — cheap enough to run for many characters every frame on the CPU. Triangles keep
    // Meta's per-bone ownership for body-part draw ordering.
    public sealed class SkinnedDrawingMesh
    {
        public const int MaxInfluences = 3;

        public V2[] RestVertices;   // rig space (texture pixels, y up)
        public V2[] UVs;            // 0..1, v up (matches Unity textures)
        public int[] Triangles;     // 3 indices per triangle
        public int[] TriangleOwner; // joint index owning each triangle (bone ending at that joint)
        public int[] BoneIndex;     // MaxInfluences per vertex
        public float[] BoneWeight;  // MaxInfluences per vertex
        private float[] triangleOwnerDistance;

        public int VertexCount => RestVertices.Length;
        public int TriangleCount => Triangles.Length / 3;

        // Meta's char_bodypart_groups (fair1_ppf.yaml): trunk, left arm, right arm, left leg, right leg
        public static readonly string[][] DefaultBodyPartGroups =
        {
            new[] { "right_shoulder", "left_shoulder", "right_hip", "left_hip", "hip", "torso", "neck" },
            new[] { "left_elbow", "left_hand" },
            new[] { "right_elbow", "right_hand" },
            new[] { "left_knee", "left_foot" },
            new[] { "right_knee", "right_foot" },
        };

        // draw order used when the current motion has no depth information (procedural poses):
        // legs behind the trunk, arms in front — how children usually draw a figure
        private static readonly float[] DefaultGroupDepths = { 0f, 0.5f, 0.5f, -0.5f, -0.5f };

        public static SkinnedDrawingMesh Build(CharacterAnnotation annotation, DrawingRig rig, int gridDivisions = 36)
        {
            var mask = annotation.Mask ?? throw new ArgumentException("Annotation has no mask");
            var w = mask.Width;
            var h = mask.Height;

            // analysis resolution for the distance fields
            var scale = Math.Min(1f, 160f / Math.Max(w, h));
            var aw = Math.Max(2, (int)MathF.Round(w * scale));
            var ah = Math.Max(2, (int)MathF.Round(h * scale));
            var smallMask = new bool[aw * ah];
            for (var y = 0; y < ah; y++)
            for (var x = 0; x < aw; x++)
            {
                var sx = Math.Min(w - 1, (int)((x + 0.5f) / scale));
                var sy = Math.Min(h - 1, (int)((y + 0.5f) / scale));
                smallMask[y * aw + x] = mask[sx, sy] != 0;
            }

            // bones = every joint with a parent (Meta seeds 20 points along each)
            var bones = new List<int>();
            for (var j = 0; j < rig.JointCount; j++) if (rig.Parent[j] >= 0) bones.Add(j);
            var fields = new float[bones.Count][];
            for (var b = 0; b < bones.Count; b++)
            {
                var joint = bones[b];
                var p0 = ToAnalysis(rig.Rest[joint], h, scale);
                var p1 = ToAnalysis(rig.Rest[rig.Parent[joint]], h, scale);
                fields[b] = GeodesicDistance(smallMask, aw, ah, p0, p1);
            }

            // grid vertices over every cell touching the mask
            var step = Math.Max(w, h) / (float)gridDivisions;
            var cols = (int)MathF.Ceiling(w / step);
            var rows = (int)MathF.Ceiling(h / step);
            var cellUsed = new bool[cols * rows];
            for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                if (mask[x, y] == 0) continue;
                cellUsed[Math.Min(rows - 1, (int)(y / step)) * cols + Math.Min(cols - 1, (int)(x / step))] = true;
            }

            var vertexOf = new Dictionary<int, int>();
            var vertices = new List<V2>();
            var triangles = new List<int>();
            int Vertex(int gx, int gy)
            {
                var key = gy * (cols + 1) + gx;
                if (vertexOf.TryGetValue(key, out var index)) return index;
                index = vertices.Count;
                // image space (y down) -> rig space (y up)
                var px = Math.Min(w, gx * step);
                var py = Math.Min(h, gy * step);
                vertices.Add(new V2(px, h - py));
                vertexOf[key] = index;
                return index;
            }

            for (var cy = 0; cy < rows; cy++)
            for (var cx = 0; cx < cols; cx++)
            {
                if (!cellUsed[cy * cols + cx]) continue;
                var a = Vertex(cx, cy);
                var b = Vertex(cx + 1, cy);
                var c = Vertex(cx + 1, cy + 1);
                var d = Vertex(cx, cy + 1);
                // counter-clockwise in rig space (y up)
                triangles.AddRange(new[] { a, d, c, a, c, b });
            }

            var mesh = new SkinnedDrawingMesh
            {
                RestVertices = vertices.ToArray(),
                Triangles = triangles.ToArray(),
            };
            mesh.UVs = new V2[mesh.RestVertices.Length];
            for (var i = 0; i < mesh.RestVertices.Length; i++)
                mesh.UVs[i] = new V2(mesh.RestVertices[i].X / w, mesh.RestVertices[i].Y / h);

            mesh.ComputeWeights(rig, bones, fields, aw, ah, h, scale);
            mesh.ComputeTriangleOwners(bones, fields, aw, ah, h, scale);
            return mesh;
        }

        private static V2 ToAnalysis(V2 rigPoint, int height, float scale) =>
            new(rigPoint.X * scale, (height - rigPoint.Y) * scale);

        private static float SampleField(float[] field, int aw, int ah, V2 rigPoint, int height, float scale)
        {
            var p = ToAnalysis(rigPoint, height, scale);
            var x = MathUtil.Clamp((int)p.X, 0, aw - 1);
            var y = MathUtil.Clamp((int)p.Y, 0, ah - 1);
            return field[y * aw + x];
        }

        private void ComputeWeights(DrawingRig rig, List<int> bones, float[][] fields, int aw, int ah, int h, float scale)
        {
            var n = RestVertices.Length;
            BoneIndex = new int[n * MaxInfluences];
            BoneWeight = new float[n * MaxInfluences];
            // blend radius: ~5% of the character's size, in analysis pixels
            var sigma = Math.Max(1.5f, 0.05f * Math.Max(aw, ah));
            var distances = new float[bones.Count];
            var order = new int[bones.Count];

            for (var v = 0; v < n; v++)
            {
                var min = float.MaxValue;
                for (var b = 0; b < bones.Count; b++)
                {
                    distances[b] = SampleField(fields[b], aw, ah, RestVertices[v], h, scale);
                    order[b] = b;
                    if (distances[b] < min) min = distances[b];
                }
                Array.Sort(order, (x, y) => distances[x].CompareTo(distances[y]));

                var total = 0f;
                for (var k = 0; k < MaxInfluences; k++)
                {
                    var b = order[Math.Min(k, bones.Count - 1)];
                    var excess = distances[b] - min;
                    var weight = k == 0 ? 1f : excess > 2.5f * sigma ? 0f : MathF.Exp(-(excess * excess) / (2f * sigma * sigma));
                    BoneIndex[v * MaxInfluences + k] = bones[b];
                    BoneWeight[v * MaxInfluences + k] = weight;
                    total += weight;
                }
                for (var k = 0; k < MaxInfluences; k++) BoneWeight[v * MaxInfluences + k] /= total;
            }
        }

        private void ComputeTriangleOwners(List<int> bones, float[][] fields, int aw, int ah, int h, float scale)
        {
            var count = TriangleCount;
            TriangleOwner = new int[count];
            triangleOwnerDistance = new float[count];
            for (var t = 0; t < count; t++)
            {
                var centroid = (RestVertices[Triangles[t * 3]] + RestVertices[Triangles[t * 3 + 1]] + RestVertices[Triangles[t * 3 + 2]]) / 3f;
                var best = 0;
                var bestD = float.MaxValue;
                for (var b = 0; b < bones.Count; b++)
                {
                    var d = SampleField(fields[b], aw, ah, centroid, h, scale);
                    if (d >= bestD) continue;
                    bestD = d;
                    best = b;
                }
                TriangleOwner[t] = bones[best];
                triangleOwnerDistance[t] = bestD;
            }
        }

        // Dial's algorithm (integer bucket queue: 10 per straight step, 14 per diagonal) from a
        // bone segment. Travel outside the drawing is allowed but 6x as expensive, so distances
        // stay "through the drawing" while pixels just outside the outline still get a value.
        private static float[] GeodesicDistance(bool[] mask, int w, int h, V2 a, V2 b)
        {
            const int outsideCost = 6;
            var dist = new int[w * h];
            for (var i = 0; i < dist.Length; i++) dist[i] = int.MaxValue;
            var buckets = new List<List<int>>();
            void Push(int index, int d)
            {
                if (d >= dist[index]) return;
                dist[index] = d;
                while (buckets.Count <= d) buckets.Add(null);
                (buckets[d] ??= new List<int>()).Add(index);
            }

            const int seeds = 20;
            for (var s = 0; s <= seeds; s++)
            {
                var p = V2.Lerp(a, b, s / (float)seeds);
                var x = MathUtil.Clamp((int)p.X, 0, w - 1);
                var y = MathUtil.Clamp((int)p.Y, 0, h - 1);
                Push(y * w + x, 0);
            }

            for (var d = 0; d < buckets.Count; d++)
            {
                var bucket = buckets[d];
                if (bucket == null) continue;
                for (var k = 0; k < bucket.Count; k++)
                {
                    var index = bucket[k];
                    if (dist[index] != d) continue;
                    var x = index % w;
                    var y = index / w;
                    for (var dy = -1; dy <= 1; dy++)
                    for (var dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dy == 0) continue;
                        var nx = x + dx;
                        var ny = y + dy;
                        if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                        var n = ny * w + nx;
                        var cost = dx != 0 && dy != 0 ? 14 : 10;
                        if (!mask[n]) cost *= outsideCost;
                        Push(n, d + cost);
                    }
                }
                buckets[d] = null;
            }

            var result = new float[w * h];
            for (var i = 0; i < result.Length; i++) result[i] = dist[i] == int.MaxValue ? 1e6f : dist[i] / 10f;
            return result;
        }

        // Linear blend skinning of the rest mesh into `pose`.
        public void Deform(DrawingRig rig, RigPose pose, V2[] output)
        {
            for (var v = 0; v < RestVertices.Length; v++)
            {
                var rest = RestVertices[v];
                var result = V2.Zero;
                for (var k = 0; k < MaxInfluences; k++)
                {
                    var weight = BoneWeight[v * MaxInfluences + k];
                    if (weight <= 0f) continue;
                    result += rig.TransformByBone(BoneIndex[v * MaxInfluences + k], rest, pose) * weight;
                }
                output[v] = result;
            }
        }

        // Triangle indices in painter's order, like Meta's _set_draw_indices: body-part groups by
        // increasing depth, and inside a group, joints in listed order (reversed when behind).
        // Runs every frame, so per-joint triangle lists are cached and nothing is allocated.
        public int[] BuildDrawOrder(DrawingRig rig, string[][] groups, float[] groupDepths, int[] output = null)
        {
            groups ??= DefaultBodyPartGroups;
            groupDepths ??= DefaultGroupDepths;
            output ??= new int[Triangles.Length];
            EnsureDrawCache(rig, groups);

            var groupCount = groups.Length;
            if (groupOrder == null || groupOrder.Length != groupCount) groupOrder = new int[groupCount];
            for (var g = 0; g < groupCount; g++) groupOrder[g] = g;
            // insertion sort by depth (5 groups; stable, allocation-free)
            for (var i = 1; i < groupCount; i++)
            {
                var g = groupOrder[i];
                var d = g < groupDepths.Length ? groupDepths[g] : 0f;
                var k = i - 1;
                while (k >= 0 && (groupOrder[k] < groupDepths.Length ? groupDepths[groupOrder[k]] : 0f) > d)
                {
                    groupOrder[k + 1] = groupOrder[k];
                    k--;
                }
                groupOrder[k + 1] = g;
            }

            var cursor = 0;
            foreach (var joint in unlistedJoints) cursor = Emit(joint, output, cursor);
            foreach (var g in groupOrder)
            {
                var joints = groupJoints[g];
                var depth = g < groupDepths.Length ? groupDepths[g] : 0f;
                if (depth > 0f)
                    for (var k = 0; k < joints.Length; k++) cursor = Emit(joints[k], output, cursor);
                else
                    for (var k = joints.Length - 1; k >= 0; k--) cursor = Emit(joints[k], output, cursor);
            }
            return output;
        }

        private int[][] trianglesByJoint;   // per joint: triangle ids, farthest from the bone first
        private int[][] groupJoints;        // per group: joint ids
        private int[] unlistedJoints;
        private string[][] cachedGroups;
        private int[] groupOrder;

        private int Emit(int joint, int[] output, int cursor)
        {
            foreach (var t in trianglesByJoint[joint])
            {
                output[cursor++] = Triangles[t * 3];
                output[cursor++] = Triangles[t * 3 + 1];
                output[cursor++] = Triangles[t * 3 + 2];
            }
            return cursor;
        }

        private void EnsureDrawCache(DrawingRig rig, string[][] groups)
        {
            if (trianglesByJoint == null)
            {
                var lists = new List<int>[rig.JointCount];
                for (var j = 0; j < lists.Length; j++) lists[j] = new List<int>();
                for (var t = 0; t < TriangleCount; t++) lists[TriangleOwner[t]].Add(t);
                trianglesByJoint = new int[rig.JointCount][];
                for (var j = 0; j < lists.Length; j++)
                {
                    lists[j].Sort((a, b) => triangleOwnerDistance[b].CompareTo(triangleOwnerDistance[a]));
                    trianglesByJoint[j] = lists[j].ToArray();
                }
            }

            if (ReferenceEquals(groups, cachedGroups)) return;
            cachedGroups = groups;
            var listed = new bool[rig.JointCount];
            groupJoints = new int[groups.Length][];
            for (var g = 0; g < groups.Length; g++)
            {
                var ids = new List<int>();
                foreach (var name in groups[g])
                {
                    var j = rig.IndexOf(name);
                    if (j < 0 || listed[j]) continue;
                    listed[j] = true;
                    ids.Add(j);
                }
                groupJoints[g] = ids.ToArray();
            }
            // joints no group lists (root, and any extra joints of custom skeletons like the
            // six-armed bug) are drawn first, underneath everything
            var unlisted = new List<int>();
            for (var j = 0; j < rig.JointCount; j++) if (!listed[j]) unlisted.Add(j);
            unlistedJoints = unlisted.ToArray();
        }
    }
}
