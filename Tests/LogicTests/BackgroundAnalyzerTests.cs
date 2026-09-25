using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AnimatedDrawingsWorld.Logic;
using AnimatedDrawingsWorld.Logic.Scene;
using Xunit;
using Xunit.Abstractions;

namespace AnimatedDrawingsWorld.Tests
{
    public class BackgroundAnalyzerTests
    {
        private readonly ITestOutputHelper output;
        public BackgroundAnalyzerTests(ITestOutputHelper output) => this.output = output;

        public static IEnumerable<object[]> Scenes() =>
            Directory.GetFiles(TestSupport.Backgrounds, "*.truth.json")
                .Select(f => new object[] { Path.GetFileName(f).Replace(".truth.json", "") });

        private static readonly HashSet<string> GroundKinds = new() { "Grass", "Sand", "Dirt", "Floor" };

        [Theory]
        [MemberData(nameof(Scenes))]
        public void FindsTheObjectsAChildDrew(string scene)
        {
            var image = TestSupport.Load(Path.Combine(TestSupport.Backgrounds, scene + ".png"));
            var layout = BackgroundAnalyzer.Analyze(image);
            TestSupport.SavePng(Debug.DrawLayout(image, layout), Path.Combine(TestSupport.OutputDir, "analysis", scene + ".png"));
            output.WriteLine(layout.Describe());

            var truth = MiniJson.Arr(MiniJson.Get(MiniJson.Obj(MiniJson.Parse(File.ReadAllText(Path.Combine(TestSupport.Backgrounds, scene + ".truth.json")))), "objects"));
            int found = 0, expected = 0;
            foreach (var t in truth)
            {
                var o = MiniJson.Obj(t);
                var kind = MiniJson.Str(MiniJson.Get(o, "kind"));
                var b = MiniJson.Arr(MiniJson.Get(o, "bounds")).Select(v => MiniJson.Num(v)).ToArray();
                // truth is y-down; convert to the layout's y-up
                var truthRect = new Rect2(b[0], 1 - b[3], b[2], 1 - b[1]);
                expected++;

                bool hit;
                if (GroundKinds.Contains(kind))
                {
                    // ground is judged by the walkable profile, not a box
                    var err = Enumerable.Range(1, 9).Select(i => i / 10f).Average(x => Math.Abs(layout.GroundAt(x) - truthRect.YMax));
                    hit = err < 0.08f;
                    output.WriteLine($"  truth {kind}: ground profile mean error {err:0.000} -> {(hit ? "OK" : "MISS")}");
                }
                else
                {
                    var match = layout.Objects.Where(x => x.Kind.ToString() == kind)
                        .Select(x => (x, iou: IoU(x.Bounds, truthRect))).OrderByDescending(p => p.iou).FirstOrDefault();
                    hit = match.x != null && (match.iou > 0.3f || match.x.Bounds.Contains(truthRect.Center));
                    output.WriteLine($"  truth {kind} {truthRect}: {(hit ? $"OK (IoU {match.iou:0.00})" : "MISS")}");
                }
                if (hit) found++;
            }

            var recall = (float)found / expected;
            output.WriteLine($"{scene}: recall {found}/{expected} = {recall:P0}");
            Assert.True(recall >= 0.75f, $"{scene}: only {found}/{expected} objects found");

            // precision: detected objects that match something the child actually drew
            var truthRects = truth.Select(t => MiniJson.Obj(t)).Select(o => (kind: MiniJson.Str(MiniJson.Get(o, "kind")),
                rect: MiniJson.Arr(MiniJson.Get(o, "bounds")).Select(v => MiniJson.Num(v)).ToArray())).ToList();
            var detected = layout.Objects.Where(o => o.Kind != SceneObjectKind.Sky && !GroundKinds.Contains(o.Kind.ToString())).ToList();
            var correct = detected.Count(o => truthRects.Any(t => t.kind == o.Kind.ToString() &&
                IoU(o.Bounds, new Rect2(t.rect[0], 1 - t.rect[3], t.rect[2], 1 - t.rect[1])) > 0.1f));
            var precision = detected.Count == 0 ? 1f : (float)correct / detected.Count;
            output.WriteLine($"{scene}: precision {correct}/{detected.Count} = {precision:P0}");
            Assert.True(precision >= 0.6f, $"{scene}: {detected.Count - correct} false detections");
        }

        [Fact]
        public void RealPhotographedKidDrawingGetsAFloorLine()
        {
            var image = TestSupport.Load(Path.Combine(TestSupport.Backgrounds, "real_kid_room_photo.png"));
            var layout = BackgroundAnalyzer.Analyze(image);
            TestSupport.SavePng(Debug.DrawLayout(image, layout), Path.Combine(TestSupport.OutputDir, "analysis", "real_kid_room_photo.png"));
            output.WriteLine(layout.Describe());
            // the child drew furniture standing on the bottom part of the page
            var ground = layout.GroundAt(0.5f);
            Assert.InRange(ground, 0.05f, 0.45f);
        }

        [Fact]
        public void EmptyPageStillHasSomewhereToStand()
        {
            var blank = new Logic.Imaging.RgbaImage(320, 200);
            for (var i = 0; i < blank.Pixels.Length; i++) blank.Pixels[i] = 250;
            var layout = BackgroundAnalyzer.Analyze(blank);
            Assert.Empty(layout.Objects);
            Assert.False(layout.GroundDetected);
            Assert.Equal(0.2f, layout.GroundAt(0.3f), 3);
        }

        private static float IoU(Rect2 a, Rect2 b)
        {
            var ix = Math.Max(0, Math.Min(a.XMax, b.XMax) - Math.Max(a.XMin, b.XMin));
            var iy = Math.Max(0, Math.Min(a.YMax, b.YMax) - Math.Max(a.YMin, b.YMin));
            var inter = ix * iy;
            return inter / (a.Area + b.Area - inter);
        }
    }
}
