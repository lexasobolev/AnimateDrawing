using System;
using System.Collections.Generic;
using AnimatedDrawingsWorld.Logic.Imaging;

namespace AnimatedDrawingsWorld.Logic.Scene
{
    // Finds the things a child typically draws in a scene — sky, grass/ground, water, trees,
    // sun, clouds, houses, flowers, rocks, mountains — from colour regions and their shapes,
    // and works out where the walkable ground is. Runs in a few milliseconds on a thread,
    // no ML model required; an optional vision model can refine the result (VisionSceneParser).
    public static class BackgroundAnalyzer
    {
        public const int AnalysisWidth = 240;
        public const int GroundSamples = 96;

        private sealed class Region
        {
            public ColorClass Color;
            public Component C;
            public float X0, Y0, X1, Y1; // normalized, y DOWN
            public float Cx, Cy;
            public float Area;           // fraction of image
            public float W => X1 - X0;
            public float H => Y1 - Y0;
            public float Fill => C.FillRatio;
            public bool Top => C.YMin <= 1;
            public bool Bottom;
            public bool Left => C.XMin <= 1;
            public bool Right;
            public bool Used;
            public int[] Labels;
        }

        public static SceneLayout Analyze(RgbaImage image)
        {
            var layout = new SceneLayout { SourceWidth = image.Width, SourceHeight = image.Height };
            var small = image.Width > AnalysisWidth
                ? image.Resize(AnalysisWidth, Math.Max(8, (int)MathF.Round(image.Height * (AnalysisWidth / (float)image.Width))))
                : image;
            if (ReferenceEquals(small, image)) small = image.Clone();
            // pencil/marker strokes: local-contrast threshold (as in Meta's segment()), taken before
            // colour flattening so thin grey lines survive the downscale
            var ink = ImageOps.AdaptiveThresholdInv(ImageOps.MinChannel(small), 15, 12f);
            FlattenIllumination(small);
            var w = small.Width;
            var h = small.Height;

            var classes = ClassifySmoothed(small);
            var regions = FindRegions(classes, w, h);

            var nextId = 1;
            SceneObject Add(SceneObjectKind kind, float x0, float y0, float x1, float y1, float confidence, string label, ColorClass color)
            {
                // convert y-down image fractions to y-up scene coordinates
                var o = new SceneObject
                {
                    Id = nextId++,
                    Kind = kind,
                    Bounds = new Rect2(x0, 1f - y1, x1, 1f - y0),
                    Confidence = confidence,
                    Label = label,
                    Color = color.ToString().ToLowerInvariant(),
                };
                layout.Objects.Add(o);
                return o;
            }

            // --- sky: blue region along the top
            foreach (var r in regions)
            {
                if (!IsBlue(r.Color)) continue;
                if ((r.Top && r.W > 0.3f) || (r.Cy < 0.3f && r.W > 0.5f))
                {
                    Add(SceneObjectKind.Sky, r.X0, r.Y0, r.X1, r.Y1, 0.9f, "sky", r.Color);
                    r.Used = true;
                }
            }

            // --- ground surfaces along the bottom
            foreach (var r in regions)
            {
                if (r.Used) continue;
                var groundLike = r.Bottom || (r.W > 0.45f && r.Y1 > 0.8f);
                if (!groundLike || r.Cy < 0.45f) continue;
                SceneObjectKind? kind = r.Color switch
                {
                    ColorClass.Green => SceneObjectKind.Grass,
                    ColorClass.Brown => SceneObjectKind.Dirt,
                    ColorClass.Orange => SceneObjectKind.Sand,
                    ColorClass.Yellow => SceneObjectKind.Sand,
                    ColorClass.Gray => SceneObjectKind.Floor,
                    ColorClass.Blue => SceneObjectKind.Water,
                    ColorClass.LightBlue => SceneObjectKind.Water,
                    _ => null,
                };
                if (kind == null) continue;
                // thin strips of blue at the bottom that span the page are water; narrow ones too
                if (r.W < 0.12f && kind != SceneObjectKind.Water) continue;
                var label = kind == SceneObjectKind.Grass ? "grass" : kind.Value.ToString().ToLowerInvariant();
                Add(kind.Value, r.X0, r.Y0, r.X1, r.Y1, 0.85f, label, r.Color);
                r.Used = true;
            }

            // --- ground profile (needed by the rules below: things "standing on the ground")
            ComputeGround(layout, regions, ink, w, h);
            float GroundYDown(float x) => 1f - layout.GroundAt(x);

            // --- lakes / ponds that don't touch the bottom edge
            foreach (var r in regions)
            {
                if (r.Used || !IsBlue(r.Color)) continue;
                if (r.Cy > 0.5f && r.Area > 0.004f && r.W > r.H * 1.2f)
                {
                    Add(SceneObjectKind.Water, r.X0, r.Y0, r.X1, r.Y1, 0.7f, r.W > 0.3f ? "lake" : "pond", r.Color);
                    r.Used = true;
                }
            }

            // --- trees: brown trunk + green crown above it
            var trunks = regions.FindAll(r => !r.Used && (r.Color == ColorClass.Brown || r.Color == ColorClass.Orange) &&
                                             r.H > 0.05f && r.H > r.W * 1.3f && r.Cy > 0.2f);
            var crowns = regions.FindAll(r => !r.Used && r.Color == ColorClass.Green && r.Area > 0.002f && !r.Bottom);
            foreach (var trunk in trunks)
            {
                Region bestCrown = null;
                var bestScore = 0f;
                foreach (var crown in crowns)
                {
                    if (crown.Used) continue;
                    var overlap = Math.Max(0f, Math.Min(trunk.X1, crown.X1) - Math.Max(trunk.X0, crown.X0));
                    var trunkInside = trunk.Cx > crown.X0 && trunk.Cx < crown.X1;
                    if (overlap <= 0f && !trunkInside) continue;
                    // crown bottom should be around the trunk's top
                    var gap = trunk.Y0 - crown.Y1;
                    if (gap > 0.08f || crown.Y1 > trunk.Y0 + trunk.H * 0.7f) continue;
                    var score = crown.Area / (1f + Math.Abs(gap) * 10f);
                    if (score <= bestScore) continue;
                    bestScore = score;
                    bestCrown = crown;
                }

                if (bestCrown == null && trunk.H < 0.12f) continue; // a lone short brown stick isn't a tree
                trunk.Used = true;
                var x0 = trunk.X0; var y0 = trunk.Y0; var x1 = trunk.X1; var y1 = trunk.Y1;
                if (bestCrown != null)
                {
                    bestCrown.Used = true;
                    x0 = Math.Min(x0, bestCrown.X0); y0 = Math.Min(y0, bestCrown.Y0);
                    x1 = Math.Max(x1, bestCrown.X1);
                }
                // extend the trunk down to the ground if the child left a gap
                var ground = GroundYDown(trunk.Cx);
                y1 = Math.Max(y1, Math.Min(ground, y1 + 0.05f));
                var tree = Add(SceneObjectKind.Tree, x0, y0, x1, y1, bestCrown != null ? 0.9f : 0.55f,
                    bestCrown != null ? "tree" : "bare tree", ColorClass.Brown);
                tree.Trunk = new Rect2(trunk.X0, 1f - y1, trunk.X1, 1f - trunk.Y0);
                tree.CrownBottom = bestCrown != null ? Math.Max(1f - bestCrown.Y1, 1f - trunk.Y0 - trunk.H * 0.2f) : 1f - trunk.Y0;
            }

            // green blobs off the ground: lollipop trees with outlined trunks, or bushes
            foreach (var crown in crowns)
            {
                if (crown.Used) continue;
                var ground = GroundYDown(crown.Cx);
                var aspect = crown.W / Math.Max(0.001f, crown.H);
                if (aspect < 0.35f || aspect > 3.5f) continue;
                var groundGap = ground - crown.Y1;
                if (groundGap < 0.06f && crown.Area > 0.003f)
                {
                    Add(SceneObjectKind.Bush, crown.X0, crown.Y0, crown.X1, crown.Y1, 0.6f, "bush", crown.Color);
                    crown.Used = true;
                }
                else if (groundGap >= 0.06f && crown.Area > 0.008f && crown.Cy < 0.8f)
                {
                    var tree = Add(SceneObjectKind.Tree, crown.X0, crown.Y0, crown.X1, ground, 0.6f, "tree", crown.Color);
                    var trunkHalf = Math.Max(0.012f, crown.W * 0.1f);
                    tree.Trunk = new Rect2(crown.Cx - trunkHalf, 1f - ground, crown.Cx + trunkHalf, 1f - crown.Y1);
                    tree.CrownBottom = 1f - crown.Y1;
                    crown.Used = true;
                }
            }

            // --- sun: roundish yellow/orange/red blob in the upper part
            Region sun = null;
            var sunScore = 0f;
            foreach (var r in regions)
            {
                if (r.Used || !(r.Color == ColorClass.Yellow || r.Color == ColorClass.Orange || r.Color == ColorClass.Red)) continue;
                var aspect = r.W / Math.Max(0.001f, r.H);
                if (r.Cy > 0.45f || aspect < 0.55f || aspect > 1.8f || r.Area < 0.0015f || r.Area > 0.12f || r.Fill < 0.4f) continue;
                var cornerBonus = (r.Cx < 0.3f || r.Cx > 0.7f ? 1.3f : 1f) * (r.Color == ColorClass.Yellow ? 1.5f : 1f);
                var score = r.Area * r.Fill * cornerBonus * (1.2f - r.Cy);
                if (score <= sunScore) continue;
                sunScore = score;
                sun = r;
            }
            if (sun != null)
            {
                Add(SceneObjectKind.Sun, sun.X0, sun.Y0, sun.X1, sun.Y1, 0.85f, "sun", sun.Color);
                sun.Used = true;
            }

            // --- houses: rectangular colored walls standing on the ground, merged with a roof above
            var houseCandidates = regions.FindAll(r => !r.Used && r.Area > 0.008f && r.Fill > 0.6f &&
                                                      r.Color != ColorClass.Green && r.Color != ColorClass.Paper &&
                                                      r.Color != ColorClass.Dark && !r.Top && r.Cy > 0.25f &&
                                                      r.W < 0.7f && r.H < 0.75f && r.W / Math.Max(0.001f, r.H) is > 0.4f and < 3f);
            foreach (var wall in houseCandidates)
            {
                if (wall.Used) continue;
                var ground = GroundYDown(wall.Cx);
                if (Math.Abs(wall.Y1 - ground) > 0.12f) continue; // houses stand on the ground
                var x0 = wall.X0; var y0 = wall.Y0; var x1 = wall.X1; var y1 = wall.Y1;
                var hasRoof = false;
                foreach (var roof in regions)
                {
                    if (roof.Used || roof == wall || roof.Color == ColorClass.Green || IsBlue(roof.Color) && roof.Top) continue;
                    var overlap = Math.Max(0f, Math.Min(roof.X1, wall.X1) - Math.Max(roof.X0, wall.X0));
                    if (overlap < wall.W * 0.5f) continue;
                    var gap = wall.Y0 - roof.Y1;
                    if (gap < -wall.H * 0.25f || gap > 0.04f || roof.Area < wall.Area * 0.15f) continue;
                    roof.Used = true;
                    hasRoof = true;
                    x0 = Math.Min(x0, roof.X0); y0 = Math.Min(y0, roof.Y0); x1 = Math.Max(x1, roof.X1);
                }
                // swallow windows/doors drawn inside the wall
                foreach (var inner in regions)
                {
                    if (inner.Used || inner == wall) continue;
                    if (inner.X0 >= x0 && inner.X1 <= x1 && inner.Y0 >= y0 && inner.Y1 <= y1) inner.Used = true;
                }
                wall.Used = true;
                Add(SceneObjectKind.House, x0, y0, x1, y1, hasRoof ? 0.85f : 0.55f, (hasRoof ? "" : "box-like ") + wall.Color.ToString().ToLowerInvariant() + " house", wall.Color);
            }

            // --- mountains: big triangular grey/brown/purple/blue shapes rising from the horizon
            foreach (var r in regions)
            {
                if (r.Used || r.Area < 0.03f) continue;
                if (!(r.Color == ColorClass.Gray || r.Color == ColorClass.Brown || r.Color == ColorClass.Purple || r.Color == ColorClass.Blue || r.Color == ColorClass.Dark)) continue;
                var ground = GroundYDown(r.Cx);
                if (r.Y1 < ground - 0.15f || r.Fill > 0.75f || r.Fill < 0.3f) continue;
                // triangular: the top rows are much narrower than the bottom
                if (!IsPointyTop(classes, w, h, r)) continue;
                Add(SceneObjectKind.Mountain, r.X0, r.Y0, r.X1, r.Y1, 0.6f, "mountain", r.Color);
                r.Used = true;
            }

            // --- rocks: grey blobs sitting on the ground
            foreach (var r in regions)
            {
                if (r.Used || r.Color != ColorClass.Gray || r.Area < 0.002f || r.Area > 0.08f) continue;
                var ground = GroundYDown(r.Cx);
                if (Math.Abs(r.Y1 - ground) > 0.12f || r.W < r.H * 0.6f) continue;
                if (InsideAny(layout, SceneObjectKind.Mountain, r)) continue; // snow and shading on a mountain
                Add(SceneObjectKind.Rock, r.X0, r.Y0, r.X1, r.Y1, 0.6f, "rock", r.Color);
                r.Used = true;
            }

            // --- flowers: small bright blobs down on the ground
            var flowers = 0;
            foreach (var r in regions)
            {
                if (r.Used || flowers >= 12) continue;
                if (!(r.Color == ColorClass.Red || r.Color == ColorClass.Pink || r.Color == ColorClass.Purple ||
                      r.Color == ColorClass.Yellow || r.Color == ColorClass.Orange || r.Color == ColorClass.Blue)) continue;
                if (r.Area < 0.0004f || r.Area > 0.012f || r.W > 0.1f || r.H > 0.12f || r.Fill < 0.25f) continue;
                var ground = GroundYDown(r.Cx);
                if (r.Cy < ground - 0.2f) continue;
                if (InsideAny(layout, SceneObjectKind.Mountain, r) || InsideAny(layout, SceneObjectKind.House, r)) continue;
                Add(SceneObjectKind.Flower, r.X0, r.Y0, r.X1, r.Y1, 0.55f, r.Color.ToString().ToLowerInvariant() + " flower", r.Color);
                r.Used = true;
                flowers++;
            }

            // --- clouds: light blue / grey blobs up high, or white shapes inside a blue sky
            var skyBottom = 0f;
            foreach (var o in layout.OfKind(SceneObjectKind.Sky)) skyBottom = Math.Max(skyBottom, 1f - o.Bounds.YMin);
            foreach (var r in regions)
            {
                if (r.Used || r.Cy > 0.45f || r.Area < 0.002f) continue;
                var cloudColor = r.Color == ColorClass.LightBlue || r.Color == ColorClass.Gray || r.Color == ColorClass.Blue ||
                                 (r.Color == ColorClass.Paper && skyBottom > 0f && !r.Top && !r.Left && !r.Right && r.Cy < skyBottom);
                if (!cloudColor || r.W < r.H * 1.1f) continue;
                if (r.Top && r.W > 0.3f) continue;
                Add(SceneObjectKind.Cloud, r.X0, r.Y0, r.X1, r.Y1, 0.6f, "cloud", r.Color);
                r.Used = true;
            }

            // --- outline-only drawings (pencil): closed shapes become generic props / sun / clouds
            DetectOutlinedShapes(layout, ink, classes, w, h, Add, GroundYDown);

            return layout;
        }

        // Photos of drawings have uneven lighting (shadows, vignetting) that would otherwise read
        // as big grey regions. Estimate the paper's brightness with a max filter (removes strokes)
        // plus a wide blur, and divide it out — a standard flat-field correction. Uses HSV value,
        // so bright crayon colours count as "lit" like paper and aren't washed out.
        internal static void FlattenIllumination(RgbaImage img)
        {
            var w = img.Width;
            var h = img.Height;
            var value = new GrayImage(w, h);
            var p = img.Pixels;
            for (var i = 0; i < w * h; i++) value.Data[i] = Math.Max(p[i * 4], Math.Max(p[i * 4 + 1], p[i * 4 + 2]));
            var lit = ImageOps.Dilate(value, 1, Math.Max(2, w / 40));
            var background = ImageOps.GaussianBlur(lit.Data, w, h, Math.Max(3f, w / 12f));
            for (var i = 0; i < w * h; i++)
            {
                var gain = MathUtil.Clamp(240f / Math.Max(1f, background[i]), 1f, 2.2f);
                for (var k = 0; k < 3; k++) p[i * 4 + k] = (byte)Math.Min(255f, p[i * 4 + k] * gain);
            }
        }

        private static bool InsideAny(SceneLayout layout, SceneObjectKind kind, Region r)
        {
            var center = new V2(r.Cx, 1f - r.Cy);
            foreach (var o in layout.OfKind(kind)) if (o.Bounds.Contains(center)) return true;
            return false;
        }

        private static bool IsBlue(ColorClass c) => c == ColorClass.Blue || c == ColorClass.LightBlue;

        // Pixel classes after smoothing crayon texture: each pixel takes the dominant colour of
        // its neighbourhood if that colour covers enough of it (paper shows through crayon).
        internal static byte[] ClassifySmoothed(RgbaImage img)
        {
            var w = img.Width;
            var h = img.Height;
            var raw = new byte[w * h];

            // paper brightness: 90th percentile of low-saturation value, to cope with dim photos
            var histogram = new int[256];
            var p = img.Pixels;
            for (var i = 0; i < w * h; i++)
            {
                ColorClassifier.RgbToHsv(p[i * 4], p[i * 4 + 1], p[i * 4 + 2], out _, out var s, out var v);
                if (s < 0.25f) histogram[(int)(v * 255)]++;
            }
            var total = 0;
            foreach (var c in histogram) total += c;
            var acc = 0;
            var paperV = 0.85f;
            for (var i = 0; i < 256 && total > 0; i++)
            {
                acc += histogram[i];
                if (acc < total * 0.9f) continue;
                paperV = i / 255f;
                break;
            }
            paperV = MathUtil.Clamp(paperV, 0.45f, 0.95f);

            for (var i = 0; i < w * h; i++)
            {
                raw[i] = p[i * 4 + 3] < 64
                    ? (byte)ColorClass.Paper
                    : (byte)ColorClassifier.Classify(p[i * 4], p[i * 4 + 1], p[i * 4 + 2], paperV);
            }

            var radius = Math.Max(2, w / 60);
            var sats = new float[ColorClassifier.ClassCount][];
            for (var c = 0; c < ColorClassifier.ClassCount; c++)
            {
                var ind = new float[w * h];
                var any = false;
                for (var i = 0; i < ind.Length; i++)
                {
                    if (raw[i] != c) continue;
                    ind[i] = 1f;
                    any = true;
                }
                sats[c] = any ? ImageOps.Integral(ind, w, h) : null;
            }

            var result = new byte[w * h];
            for (var y = 0; y < h; y++)
            {
                var y0 = Math.Max(0, y - radius);
                var y1 = Math.Min(h - 1, y + radius);
                for (var x = 0; x < w; x++)
                {
                    var x0 = Math.Max(0, x - radius);
                    var x1 = Math.Min(w - 1, x + radius);
                    var area = (x1 - x0 + 1) * (y1 - y0 + 1);
                    var best = -1;
                    var bestD = 0f;
                    for (var c = 0; c < ColorClassifier.ClassCount; c++)
                    {
                        if (sats[c] == null || !ColorClassifier.IsChromatic((ColorClass)c) && c != (int)ColorClass.Gray) continue;
                        var d = ImageOps.BoxSum(sats[c], w, x0, y0, x1, y1) / area;
                        if (d > bestD)
                        {
                            bestD = d;
                            best = c;
                        }
                    }

                    var own = raw[y * w + x];
                    if (best >= 0 && bestD >= 0.3f && (own != (byte)ColorClass.Dark || bestD >= 0.55f))
                        result[y * w + x] = (byte)best;
                    else
                        result[y * w + x] = own == (byte)ColorClass.Dark ? (byte)ColorClass.Dark : own == (byte)ColorClass.Gray && bestD < 0.3f ? (byte)ColorClass.Paper : own;
                }
            }
            return result;
        }

        private static List<Region> FindRegions(byte[] classes, int w, int h)
        {
            // children mix yellow and orange crayon in one area (sand, suns); label them together
            var merged = (byte[])classes.Clone();
            for (var i = 0; i < merged.Length; i++)
                if (merged[i] == (byte)ColorClass.Orange) merged[i] = (byte)ColorClass.Yellow;
            var image = new GrayImage(w, h, merged);
            var regions = new List<Region>();
            var minArea = Math.Max(4, (int)(w * h * 0.0004f));
            for (var c = 0; c < ColorClassifier.ClassCount; c++)
            {
                var cls = (byte)c;
                if (cls == (byte)ColorClass.Dark) continue;
                var labels = ImageOps.Label(image, v => v == cls, false, out var comps);
                foreach (var comp in comps)
                {
                    if (comp.Area < minArea) continue;
                    regions.Add(new Region
                    {
                        Color = (ColorClass)c,
                        C = comp,
                        X0 = comp.XMin / (float)w,
                        Y0 = comp.YMin / (float)h,
                        X1 = (comp.XMax + 1) / (float)w,
                        Y1 = (comp.YMax + 1) / (float)h,
                        Cx = (comp.CentroidX + 0.5f) / w,
                        Cy = (comp.CentroidY + 0.5f) / h,
                        Area = comp.Area / (float)(w * h),
                        Bottom = comp.YMax >= h - 2,
                        Right = comp.XMax >= w - 2,
                        Labels = labels,
                    });
                }
            }
            // big things first so rules that "swallow" parts see the container first
            regions.Sort((a, b) => b.Area.CompareTo(a.Area));
            return regions;
        }

        private static void ComputeGround(SceneLayout layout, List<Region> regions, GrayImage ink, int w, int h)
        {
            var groundRegions = new List<(Region r, SurfaceType type)>();
            foreach (var o in layout.Objects)
            {
                SurfaceType? type = o.Kind switch
                {
                    SceneObjectKind.Grass => SurfaceType.Grass,
                    SceneObjectKind.Dirt => SurfaceType.Dirt,
                    SceneObjectKind.Sand => SurfaceType.Sand,
                    SceneObjectKind.Floor => SurfaceType.Floor,
                    SceneObjectKind.Water => SurfaceType.Water,
                    _ => null,
                };
                if (type == null) continue;
                // find the region this object came from (same bounds)
                foreach (var r in regions)
                {
                    if (!r.Used || Math.Abs(r.X0 - o.Bounds.XMin) > 1e-4f || Math.Abs(1f - r.Y1 - o.Bounds.YMin) > 1e-4f) continue;
                    groundRegions.Add((r, type.Value));
                    break;
                }
            }

            var top = new float[GroundSamples];
            var surface = new SurfaceType[GroundSamples];
            var known = new bool[GroundSamples];
            for (var s = 0; s < GroundSamples; s++)
            {
                var x = Math.Min(w - 1, (int)((s + 0.5f) / GroundSamples * w));
                var bestY = h;
                var bestType = SurfaceType.Grass;
                // solid ground wins over water (a beach: walk on the sand, swim in the sea)
                for (var pass = 0; pass < 2 && bestY >= h; pass++)
                foreach (var (r, type) in groundRegions)
                {
                    if ((type == SurfaceType.Water) != (pass == 1)) continue;
                    if (x < r.C.XMin || x > r.C.XMax) continue;
                    // topmost pixel of this region in the column, ignoring stray specks: require a
                    // short solid run below it
                    for (var y = r.C.YMin; y <= r.C.YMax; y++)
                    {
                        if (r.Labels[y * w + x] != r.C.Label) continue;
                        var solid = 0;
                        for (var k = 0; k < 4 && y + k < h; k++) if (r.Labels[(y + k) * w + x] == r.C.Label) solid++;
                        if (solid < 3) continue;
                        if (y < bestY)
                        {
                            bestY = y;
                            bestType = type;
                        }
                        break;
                    }
                }
                if (bestY >= h) continue;
                top[s] = 1f - bestY / (float)h;
                surface[s] = bestType;
                known[s] = true;
            }

            var knownCount = 0;
            foreach (var k in known) if (k) knownCount++;
            layout.GroundDetected = knownCount >= GroundSamples / 4;

            if (knownCount == 0)
            {
                // no coloured ground: look for a long horizontal pencil line in the lower part
                var lineY = FindHorizonLine(ink, w, h);
                var y01 = lineY >= 0 ? 1f - lineY / (float)h : 0.2f;
                layout.GroundDetected = lineY >= 0;
                for (var s = 0; s < GroundSamples; s++)
                {
                    top[s] = y01;
                    surface[s] = SurfaceType.Floor;
                }
            }
            else
            {
                // fill gaps by interpolating between known columns
                for (var s = 0; s < GroundSamples; s++)
                {
                    if (known[s]) continue;
                    int l = s - 1, r = s + 1;
                    while (l >= 0 && !known[l]) l--;
                    while (r < GroundSamples && !known[r]) r++;
                    if (l < 0 && r >= GroundSamples) continue;
                    if (l < 0) { top[s] = top[r]; surface[s] = surface[r]; }
                    else if (r >= GroundSamples) { top[s] = top[l]; surface[s] = surface[l]; }
                    else
                    {
                        top[s] = MathUtil.Lerp(top[l], top[r], (s - l) / (float)(r - l));
                        surface[s] = s - l < r - s ? surface[l] : surface[r];
                    }
                }
            }

            // median filter to remove single-column spikes (a flower stem, a crayon stroke)
            var smoothed = new float[GroundSamples];
            var window = new List<float>();
            for (var s = 0; s < GroundSamples; s++)
            {
                window.Clear();
                for (var k = -3; k <= 3; k++) window.Add(top[MathUtil.Clamp(s + k, 0, GroundSamples - 1)]);
                window.Sort();
                smoothed[s] = MathUtil.Clamp(window[window.Count / 2], 0.04f, 0.85f);
            }

            layout.GroundTop = smoothed;
            layout.Surface = surface;
        }

        private static int FindHorizonLine(GrayImage ink, int w, int h)
        {
            var bestRow = -1;
            var bestCount = 0;
            for (var y = (int)(h * 0.35f); y < h - 2; y++)
            {
                var count = 0;
                for (var x = 0; x < w; x++)
                {
                    // allow a slightly wobbly hand-drawn line: check this row and its neighbours
                    var dark = false;
                    for (var dy = -2; dy <= 2 && !dark; dy++)
                    {
                        var yy = y + dy;
                        if (yy >= 0 && yy < h && ink.Data[yy * w + x] != 0) dark = true;
                    }
                    if (dark) count++;
                }
                if (count <= bestCount) continue;
                bestCount = count;
                bestRow = y;
            }
            return bestCount > w * 0.55f ? bestRow : -1;
        }

        private static bool IsPointyTop(byte[] classes, int w, int h, Region r)
        {
            int RowWidth(int y)
            {
                var n = 0;
                for (var x = r.C.XMin; x <= r.C.XMax; x++) if (r.Labels[y * w + x] == r.C.Label) n++;
                return n;
            }
            var topRow = r.C.YMin + (int)(r.C.BoxHeight * 0.15f);
            var bottomRow = r.C.YMax - (int)(r.C.BoxHeight * 0.1f);
            return RowWidth(topRow) < RowWidth(bottomRow) * 0.45f;
        }

        // Closed pencil outlines (no fill) show up as paper regions not connected to the page
        // border. Classify by shape: round & high = sun, wide & high = cloud, on the ground = prop.
        private static void DetectOutlinedShapes(SceneLayout layout, GrayImage strokes, byte[] classes, int w, int h,
            Func<SceneObjectKind, float, float, float, float, float, string, ColorClass, SceneObject> add,
            Func<float, float> groundYDown)
        {
            // thicken strokes a little so hand-drawn outlines with small gaps still close
            var ink = ImageOps.Dilate(strokes, 1);
            var holeLabels = ImageOps.Label(ink, v => v == 0, false, out var holes);

            // share of a hole's surroundings that is coloured crayon rather than a pencil line:
            // paper left between coloured areas is not a drawn outline
            float ColouredBorder(Component hole)
            {
                int coloured = 0, total = 0;
                for (var y = Math.Max(0, hole.YMin - 1); y <= Math.Min(h - 1, hole.YMax + 1); y++)
                for (var x = Math.Max(0, hole.XMin - 1); x <= Math.Min(w - 1, hole.XMax + 1); x++)
                {
                    if (holeLabels[y * w + x] != hole.Label) continue;
                    var edge = false;
                    for (var k = 0; k < 4 && !edge; k++)
                    {
                        var nx = x + (k == 0 ? 1 : k == 1 ? -1 : 0);
                        var ny = y + (k == 2 ? 1 : k == 3 ? -1 : 0);
                        if (nx >= 0 && ny >= 0 && nx < w && ny < h && holeLabels[ny * w + nx] != hole.Label) edge = true;
                    }
                    if (!edge) continue;
                    total++;
                    // look a few pixels further out, past the dilated stroke
                    var cx = hole.CentroidX;
                    var cy = hole.CentroidY;
                    var dx = x - cx;
                    var dy = y - cy;
                    var len = MathF.Sqrt(dx * dx + dy * dy) + 1e-3f;
                    var ox = MathUtil.Clamp((int)MathF.Round(x + dx / len * 4f), 0, w - 1);
                    var oy = MathUtil.Clamp((int)MathF.Round(y + dy / len * 4f), 0, h - 1);
                    if (ColorClassifier.IsChromatic((ColorClass)classes[oy * w + ox])) coloured++;
                }
                return total == 0 ? 1f : (float)coloured / total;
            }

            var hasSun = layout.Count(SceneObjectKind.Sun) > 0;
            foreach (var hole in holes)
            {
                if (hole.TouchesBorder) continue;
                var area = hole.Area / (float)(w * h);
                if (area < 0.003f || area > 0.25f) continue;
                var x0 = hole.XMin / (float)w;
                var y0 = hole.YMin / (float)h;
                var x1 = (hole.XMax + 1) / (float)w;
                var y1 = (hole.YMax + 1) / (float)h;
                var cx = (x0 + x1) * 0.5f;
                var cy = (y0 + y1) * 0.5f;

                // already explained by a coloured object (e.g. window inside a house)?
                var covered = false;
                foreach (var o in layout.Objects)
                {
                    if (o.Kind == SceneObjectKind.Sky || o.Kind == SceneObjectKind.Grass || o.Kind == SceneObjectKind.Water ||
                        o.Kind == SceneObjectKind.Dirt || o.Kind == SceneObjectKind.Sand || o.Kind == SceneObjectKind.Floor) continue;
                    if (o.Bounds.Contains(new V2(cx, 1f - cy))) covered = true;
                }
                if (covered || ColouredBorder(hole) > 0.35f) continue;

                var aspect = hole.BoxWidth / (float)hole.BoxHeight;
                var ground = groundYDown(cx);
                if (!hasSun && cy < 0.35f && aspect > 0.7f && aspect < 1.4f && hole.FillRatio > 0.6f)
                {
                    add(SceneObjectKind.Sun, x0, y0, x1, y1, 0.45f, "sun (outline)", ColorClass.Dark);
                    hasSun = true;
                }
                else if (cy < 0.4f && aspect > 1.4f)
                {
                    add(SceneObjectKind.Cloud, x0, y0, x1, y1, 0.4f, "cloud (outline)", ColorClass.Dark);
                }
                else if (Math.Abs(y1 - ground) < 0.1f && hole.FillRatio > 0.6f && x1 - x0 < 0.4f)
                {
                    // box-like and not huge: paper gaps between coloured things are irregular
                    var tall = (y1 - y0) > (x1 - x0) * 1.2f;
                    add(SceneObjectKind.Shape, x0, y0, x1, y1, 0.35f, tall ? "tall outlined shape" : "outlined box", ColorClass.Dark);
                }
            }
        }
    }
}
