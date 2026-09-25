using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AnimatedDrawingsWorld.Logic;
using AnimatedDrawingsWorld.Logic.Characters;
using Xunit;
using Xunit.Abstractions;

namespace AnimatedDrawingsWorld.Tests
{
    // Real children's drawings from Meta's AnimatedDrawings repository, with Meta's own
    // annotations (mask + skeleton from their detector/pose models) as ground truth.
    public class CharacterPipelineTests
    {
        private readonly ITestOutputHelper output;
        public CharacterPipelineTests(ITestOutputHelper output) => this.output = output;

        public static IEnumerable<object[]> AnnotatedCharacters() =>
            Directory.GetDirectories(TestSupport.Characters)
                .Where(d => File.Exists(Path.Combine(d, "meta_mask.png")))
                .Select(d => new object[] { Path.GetFileName(d) });

        [Theory]
        [MemberData(nameof(AnnotatedCharacters))]
        public void SegmentationMatchesMetaMask(string name)
        {
            var dir = Path.Combine(TestSupport.Characters, name);
            var crop = TestSupport.Load(Path.Combine(dir, "crop.png"));
            var expected = TestSupport.LoadMask(Path.Combine(dir, "meta_mask.png"));

            var mask = CharacterSegmenter.Segment(crop);
            var iou = TestSupport.IoU(mask, expected);
            output.WriteLine($"{name}: IoU with Meta's segmentation = {iou:0.000}");
            Assert.True(iou > 0.8f, $"{name}: IoU {iou:0.000}");
        }

        [Theory]
        [MemberData(nameof(AnnotatedCharacters))]
        public void CharCfgRoundTrips(string name)
        {
            var yaml = File.ReadAllText(Path.Combine(TestSupport.Characters, name, "meta_char_cfg.yaml"));
            var cfg = CharacterAnnotation.ParseCharCfgYaml(yaml);
            // most are Meta's 16-joint humanoid; the bug has a custom six-armed skeleton
            Assert.True(cfg.Skeleton.Count >= 16);
            Assert.Single(cfg.Skeleton, j => j.Parent == null);
            Assert.True(cfg.Width > 0 && cfg.Height > 0);
            var again = CharacterAnnotation.ParseCharCfgYaml(cfg.ToCharCfgYaml());
            for (var i = 0; i < cfg.Skeleton.Count; i++)
            {
                Assert.Equal(cfg.Skeleton[i].Name, again.Skeleton[i].Name);
                Assert.Equal(cfg.Skeleton[i].Parent, again.Skeleton[i].Parent);
                Assert.Equal(cfg.Skeleton[i].Location.X, again.Skeleton[i].Location.X, 0);
            }
        }

        // The heuristic estimator replaces Meta's pose model when TorchServe isn't running.
        // Compare against Meta's joints: mean error normalized by character height.
        [Theory]
        [InlineData("boy_crown", 0.12f)]
        [InlineData("girl_ballerina", 0.12f)]
        [InlineData("stick_figure", 0.12f)]
        [InlineData("candy_corn_monster", 0.15f)]
        public void HeuristicSkeletonIsCloseToMetaPoseModel(string name, float maxMeanError)
        {
            var dir = Path.Combine(TestSupport.Characters, name);
            var mask = TestSupport.LoadMask(Path.Combine(dir, "meta_mask.png"));
            var meta = CharacterAnnotation.ParseCharCfgYaml(File.ReadAllText(Path.Combine(dir, "meta_char_cfg.yaml")));
            var estimate = SkeletonEstimator.Estimate(mask);

            SkeletonEstimator.GetBounds(mask, out _, out var top, out _, out var bottom);
            var height = bottom - top;
            var errors = new List<string>();
            var total = 0f;
            foreach (var joint in meta.Skeleton)
            {
                var e = V2.Distance(joint.Location, estimate.Joint(joint.Name)) / height;
                total += e;
                errors.Add($"{joint.Name}={e:0.00}");
            }
            var mean = total / meta.Skeleton.Count;
            output.WriteLine($"{name}: mean joint error {mean:0.000} of height; {string.Join(", ", errors)}");
            Assert.True(mean < maxMeanError, $"{name}: mean error {mean:0.000}");
        }

        [Theory]
        [InlineData("bug_monster")]
        [InlineData("pig_animal")]
        public void CutsDrawingOutOfPhotographedPage(string name)
        {
            var dir = Path.Combine(TestSupport.Characters, name);
            var page = TestSupport.Load(Path.Combine(dir, "page.png"));
            // Meta's detector box: the bug page was annotated at its stored 1000px size; the pig's
            // box refers to a 1000px-wide version of the 600px image in Meta's repo
            var box = File.ReadAllLines(Path.Combine(dir, "meta_bounding_box.yaml"))
                .Select(l => l.Split(':')).ToDictionary(p => p[0].Trim(), p => float.Parse(p[1]));
            var s = name == "pig_animal" ? 0.6f : 1f;

            var (texture, _, x, y) = CharacterSegmenter.CutOut(page);
            var w = texture.Width;
            var h = texture.Height;
            float ix = Math.Max(0, Math.Min(x + w, box["right"] * s) - Math.Max(x, box["left"] * s));
            float iy = Math.Max(0, Math.Min(y + h, box["bottom"] * s) - Math.Max(y, box["top"] * s));
            var inter = ix * iy;
            var union = w * h + (box["right"] - box["left"]) * (box["bottom"] - box["top"]) * s * s - inter;
            var iou = inter / union;
            output.WriteLine($"{name}: found [{x},{y},{w},{h}] vs Meta detector box, IoU {iou:0.00}");
            Assert.True(iou > 0.5f, $"IoU {iou:0.00}");
        }

        [Theory]
        [InlineData("bug_monster")]
        [InlineData("pig_animal")]
        [InlineData("stick_figure")]
        [InlineData("garlic_monster")]
        public void CutsOutAndRigsFullPages(string name)
        {
            var page = TestSupport.Load(Path.Combine(TestSupport.Characters, name, "page.png"));
            var (texture, mask, _, _) = CharacterSegmenter.CutOut(page);
            var coverage = (float)mask.CountNonZero() / (mask.Width * mask.Height);
            output.WriteLine($"{name}: cut-out {texture.Width}x{texture.Height}, mask covers {coverage:P0}");
            Assert.InRange(coverage, 0.08f, 0.95f);

            var annotation = SkeletonEstimator.Estimate(mask);
            annotation.Texture = texture;
            Assert.Equal(16, annotation.Skeleton.Count);
            // feet below hips below neck
            Assert.True(annotation.Joint("left_foot").Y > annotation.Joint("hip").Y);
            Assert.True(annotation.Joint("hip").Y > annotation.Joint("neck").Y);
            TestSupport.SavePng(Debug.DrawSkeleton(texture, annotation), Path.Combine(TestSupport.OutputDir, "cutouts", name + ".png"));
        }
    }
}
