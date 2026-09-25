using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AnimatedDrawingsWorld.Logic;
using AnimatedDrawingsWorld.Logic.Animation;
using AnimatedDrawingsWorld.Logic.Behavior;
using AnimatedDrawingsWorld.Logic.Characters;
using AnimatedDrawingsWorld.Logic.Imaging;
using AnimatedDrawingsWorld.Logic.Scene;
using Xunit;
using Xunit.Abstractions;

namespace AnimatedDrawingsWorld.Tests
{
    // The game's bundled samples (Assets/StreamingAssets/Samples/samples.json) must all load.
    public class SamplesTests
    {
        private readonly ITestOutputHelper output;
        public SamplesTests(ITestOutputHelper output) => this.output = output;

        private static string StreamingAssets => Path.Combine(TestSupport.RepoRoot, "Assets", "StreamingAssets");

        private static Dictionary<string, object> Manifest() =>
            MiniJson.Obj(MiniJson.Parse(File.ReadAllText(Path.Combine(StreamingAssets, "Samples", "samples.json"))));

        public static IEnumerable<object[]> Characters() =>
            MiniJson.Arr(Manifest()["characters"]).Select(c => new object[] { MiniJson.Str(MiniJson.Get(MiniJson.Obj(c), "folder")) });

        public static IEnumerable<object[]> Backgrounds() =>
            MiniJson.Arr(Manifest()["backgrounds"]).Select(c => new object[] { MiniJson.Str(MiniJson.Get(MiniJson.Obj(c), "path")) });

        [Theory]
        [MemberData(nameof(Characters))]
        public void SampleCharacterLoadsAndAnimates(string folder)
        {
            var dir = Path.Combine(StreamingAssets, folder);
            var annotation = CharacterBuilder.FromMetaFiles(TestSupport.Load(Path.Combine(dir, "texture.png")),
                TestSupport.LoadMask(Path.Combine(dir, "mask.png")), File.ReadAllText(Path.Combine(dir, "char_cfg.yaml")));
            var built = CharacterBuilder.Build(annotation);
            var animator = new CharacterAnimator(built.Rig, built.Mesh, AnimationTests.LoadMotions());
            foreach (var activity in new[] { Activity.Walk, Activity.Dance, Activity.Climb, Activity.Sleep })
                for (var i = 0; i < 20; i++) animator.Update(activity, 0.05f);
            output.WriteLine($"{folder}: {built.Rig.JointCount} joints, {built.Mesh.TriangleCount} triangles");
            Assert.All(animator.DeformedVertices, v => Assert.True(float.IsFinite(v.X) && float.IsFinite(v.Y)));
        }

        [Theory]
        [MemberData(nameof(Backgrounds))]
        public void SampleBackgroundAnalyzes(string path)
        {
            var layout = BackgroundAnalyzer.Analyze(TestSupport.Load(Path.Combine(StreamingAssets, path)));
            output.WriteLine(layout.Describe());
            Assert.True(layout.GroundTop.Length > 0);
        }

        // EXPORT_SAMPLES=1 regenerates the garlic sample from its photo with the built-in
        // (no Meta models) pipeline, written in Meta's annotation format.
        [Fact]
        public void ExportDetectorFreeSample()
        {
            if (Environment.GetEnvironmentVariable("EXPORT_SAMPLES") != "1") return;
            var page = TestSupport.Load(Path.Combine(TestSupport.Characters, "garlic_monster", "page.png"));
            var annotation = CharacterBuilder.AnnotateWithoutModels(page);
            var dir = Path.Combine(StreamingAssets, "Samples", "Characters", "garlic_monster");
            Directory.CreateDirectory(dir);
            TestSupport.SavePng(annotation.Texture, Path.Combine(dir, "texture.png"));
            var mask = new RgbaImage(annotation.Mask.Width, annotation.Mask.Height);
            for (var i = 0; i < annotation.Mask.Data.Length; i++)
            {
                var v = annotation.Mask.Data[i];
                mask.Pixels[i * 4] = mask.Pixels[i * 4 + 1] = mask.Pixels[i * 4 + 2] = v;
                mask.Pixels[i * 4 + 3] = 255;
            }
            TestSupport.SavePng(mask, Path.Combine(dir, "mask.png"));
            File.WriteAllText(Path.Combine(dir, "char_cfg.yaml"), annotation.ToCharCfgYaml());
            TestSupport.SavePng(Debug.DrawSkeleton(annotation.Texture, annotation), Path.Combine(dir, "joint_overlay.png"));
        }
    }
}
