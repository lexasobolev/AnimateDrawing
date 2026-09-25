using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AnimatedDrawingsWorld.Logic;
using AnimatedDrawingsWorld.Logic.Animation;
using AnimatedDrawingsWorld.Logic.Behavior;
using AnimatedDrawingsWorld.Logic.Characters;
using Xunit;
using Xunit.Abstractions;

namespace AnimatedDrawingsWorld.Tests
{
    public class AnimationTests
    {
        private readonly ITestOutputHelper output;
        public AnimationTests(ITestOutputHelper output) => this.output = output;

        public static MotionLibrary LoadMotions()
        {
            var library = new MotionLibrary();
            foreach (var file in Directory.GetFiles(TestSupport.MotionsDir, "*.json"))
                library.Add(MotionClip.FromJson(File.ReadAllText(file)));
            return library;
        }

        public static BuiltCharacter LoadMetaCharacter(string name)
        {
            var dir = Path.Combine(TestSupport.Characters, name);
            var annotation = CharacterBuilder.FromMetaFiles(TestSupport.Load(Path.Combine(dir, "crop.png")),
                TestSupport.LoadMask(Path.Combine(dir, "meta_mask.png")), File.ReadAllText(Path.Combine(dir, "meta_char_cfg.yaml")));
            return CharacterBuilder.Build(annotation);
        }

        [Fact]
        public void AllBakedMetaMotionsLoad()
        {
            var library = LoadMotions();
            foreach (var name in MotionLibrary.RequiredClipNames()) Assert.True(library.Clips.ContainsKey(name), $"missing clip {name}");
            foreach (var clip in library.Clips.Values)
            {
                output.WriteLine($"{clip.Name}: {clip.FrameCount} frames, {clip.Duration:0.0}s, joints {string.Join(",", clip.Joints)}");
                Assert.Equal(5, clip.DepthGroups.Length);
                var o = new Dictionary<string, float>();
                clip.Sample(clip.Duration * 0.37f, true, o, out var rootY);
                Assert.Equal(clip.Joints.Length, o.Count);
                Assert.All(o.Values, v => Assert.InRange(v, -360f, 720f));
            }
        }

        [Fact]
        public void RigSolveKeepsBoneLengthsAndRestPose()
        {
            var c = LoadMetaCharacter("boy_crown");
            var pose = c.Rig.CreatePose();

            // the drawn orientations reproduce the drawing exactly
            c.Rig.Solve(c.Rig.DrawnOrientations(), pose);
            for (var j = 0; j < c.Rig.JointCount; j++)
                Assert.True(V2.Distance(pose.Positions[j], c.Rig.Rest[j]) < 0.01f, c.Rig.Names[j]);

            var verts = new V2[c.Mesh.VertexCount];
            c.Mesh.Deform(c.Rig, pose, verts);
            for (var v = 0; v < verts.Length; v++) Assert.True(V2.Distance(verts[v], c.Mesh.RestVertices[v]) < 0.01f);

            // any motion frame keeps limb lengths (rigid bones)
            var zombie = LoadMotions().Clips["zombie"];
            var o = new Dictionary<string, float>();
            zombie.Sample(1.3f, true, o, out _);
            c.Rig.Solve(o, pose);
            for (var j = 0; j < c.Rig.JointCount; j++)
            {
                var p = c.Rig.Parent[j];
                if (p < 0) continue;
                Assert.Equal(c.Rig.BoneLength(j), V2.Distance(pose.Positions[j], pose.Positions[p]), 2);
            }

            // and the mapped bones point where Meta's retargeter says (up to the trunk lean
            // that Meta also adds to the limbs)
            var elbow = c.Rig.IndexOf("left_elbow");
            var actual = (pose.Positions[elbow] - pose.Positions[c.Rig.Parent[elbow]]).AngleFromUp();
            var neckLean = o["neck"] - c.Rig.StartingTheta[c.Rig.IndexOf("neck")];
            Assert.True(Math.Abs(MathUtil.WrapDegrees(actual - o["left_elbow"] - neckLean)) < 0.5f);
        }

        [Theory]
        [InlineData("boy_crown")]
        [InlineData("girl_ballerina")]
        [InlineData("candy_corn_monster")]
        [InlineData("stick_figure")]
        [InlineData("bug_monster")] // Meta's six-armed custom skeleton
        [InlineData("pig_animal")]
        public void EveryActivityAnimatesEveryCharacter(string name)
        {
            var c = LoadMetaCharacter(name);
            var animator = new CharacterAnimator(c.Rig, c.Mesh, LoadMotions());
            output.WriteLine($"{name}: {c.Rig.JointCount} joints, {c.Mesh.VertexCount} vertices, {c.Mesh.TriangleCount} triangles, guessed {c.GuessedKind}");
            foreach (Activity activity in Enum.GetValues(typeof(Activity)))
            {
                for (var t = 0; t < 40; t++)
                {
                    animator.Update(activity, 1f / 20f);
                    foreach (var v in animator.DeformedVertices) Assert.True(float.IsFinite(v.X) && float.IsFinite(v.Y), $"{activity} produced NaN");
                }
                // every triangle is still drawn exactly once
                Assert.Equal(c.Mesh.Triangles.Length, animator.DrawOrder.Length);
                Assert.Equal(c.Mesh.Triangles.OrderBy(x => x), animator.DrawOrder.OrderBy(x => x));
            }
        }

        [Fact]
        public void MeshCoversTheDrawing()
        {
            var c = LoadMetaCharacter("girl_ballerina");
            var mask = c.Annotation.Mask;
            var covered = 0;
            var inside = 0;
            var tris = c.Mesh.Triangles;
            var verts = c.Mesh.RestVertices;
            for (var y = 0; y < mask.Height; y += 3)
            for (var x = 0; x < mask.Width; x += 3)
            {
                if (mask[x, y] == 0) continue;
                inside++;
                var p = new V2(x + 0.5f, mask.Height - y - 0.5f);
                for (var t = 0; t < tris.Length; t += 3)
                {
                    var a = verts[tris[t]]; var b = verts[tris[t + 1]]; var d = verts[tris[t + 2]];
                    var area = V2.Cross(b - a, d - a);
                    var w0 = V2.Cross(b - p, d - p) / area;
                    var w1 = V2.Cross(d - p, a - p) / area;
                    if (w0 >= -1e-3f && w1 >= -1e-3f && w0 + w1 <= 1.001f) { covered++; break; }
                }
            }
            Assert.Equal(inside, covered);
        }

        [Fact]
        public void KindGuessing()
        {
            var results = new[] { "boy_crown", "girl_ballerina", "stick_figure", "candy_corn_monster", "pig_animal" }
                .ToDictionary(n => n, n => LoadMetaCharacter(n).GuessedKind);
            foreach (var r in results) output.WriteLine($"{r.Key}: {r.Value}");
            // only the unambiguous cases are asserted; the player can override the guess
            Assert.Equal(CharacterKind.Human, results["girl_ballerina"]);
            Assert.Equal(CharacterKind.Human, results["stick_figure"]);
            Assert.Equal(CharacterKind.Animal, results["pig_animal"]);
        }
    }
}
