using System;
using System.Collections.Generic;

namespace AnimatedDrawingsWorld.Logic.Imaging
{
    public readonly struct Component
    {
        public readonly int Label;
        public readonly int Area;
        public readonly int XMin, YMin, XMax, YMax; // inclusive pixel bounds, y down
        public readonly float CentroidX, CentroidY;
        public readonly bool TouchesBorder;

        public Component(int label, int area, int xMin, int yMin, int xMax, int yMax, float cx, float cy, bool touchesBorder)
        {
            Label = label;
            Area = area;
            XMin = xMin;
            YMin = yMin;
            XMax = xMax;
            YMax = yMax;
            CentroidX = cx;
            CentroidY = cy;
            TouchesBorder = touchesBorder;
        }

        public int BoxWidth => XMax - XMin + 1;
        public int BoxHeight => YMax - YMin + 1;
        public float FillRatio => (float)Area / (BoxWidth * BoxHeight);
    }

    // The handful of OpenCV/scipy operations Meta's image_to_annotations.segment() relies on,
    // reimplemented in plain C# so segmentation also runs inside a Unity build.
    public static class ImageOps
    {
        // np.min(img, axis=2)
        public static GrayImage MinChannel(RgbaImage image)
        {
            var result = new GrayImage(image.Width, image.Height);
            var p = image.Pixels;
            for (int i = 0, j = 0; j < result.Data.Length; i += 4, j++)
            {
                var alpha = p[i + 3];
                var m = Math.Min(p[i], Math.Min(p[i + 1], p[i + 2]));
                // treat transparent pixels as white paper
                result.Data[j] = alpha < 128 ? (byte)255 : m;
            }
            return result;
        }

        // cv2.adaptiveThreshold(img, 255, ADAPTIVE_THRESH_GAUSSIAN_C, THRESH_BINARY_INV, block, c):
        // foreground (255) where pixel <= local gaussian mean - c. The gaussian is approximated by
        // three successive box blurs of matching variance (standard, and within a grey level here).
        public static GrayImage AdaptiveThresholdInv(GrayImage src, int blockSize, float c)
        {
            var sigma = 0.3f * ((blockSize - 1) * 0.5f - 1f) + 0.8f;
            var mean = GaussianBlur(src.Data, src.Width, src.Height, sigma);
            var result = new GrayImage(src.Width, src.Height);
            for (var i = 0; i < src.Data.Length; i++)
                result.Data[i] = src.Data[i] <= mean[i] - c ? (byte)255 : (byte)0;
            return result;
        }

        public static float[] GaussianBlur(byte[] data, int width, int height, float sigma)
        {
            var buffer = new float[data.Length];
            for (var i = 0; i < data.Length; i++) buffer[i] = data[i];
            return GaussianBlur(buffer, width, height, sigma);
        }

        public static float[] GaussianBlur(float[] data, int width, int height, float sigma)
        {
            // box sizes for 3-pass box blur approximating a gaussian (Kovesi)
            const int passes = 3;
            var wIdeal = MathF.Sqrt(12f * sigma * sigma / passes + 1f);
            var wl = (int)MathF.Floor(wIdeal);
            if (wl % 2 == 0) wl--;
            var m = (int)MathF.Round((12f * sigma * sigma - passes * wl * wl - 4f * passes * wl - 3f * passes) / (-4f * wl - 4f));
            var a = (float[])data.Clone();
            var b = new float[data.Length];
            for (var pass = 0; pass < passes; pass++)
            {
                var radius = ((pass < m ? wl : wl + 2) - 1) / 2;
                BoxBlurH(a, b, width, height, radius);
                BoxBlurV(b, a, width, height, radius);
            }
            return a;
        }

        // separable box blur with edge replication
        private static void BoxBlurH(float[] src, float[] dst, int w, int h, int r)
        {
            if (r <= 0) { Array.Copy(src, dst, src.Length); return; }
            var norm = 1f / (2 * r + 1);
            for (var y = 0; y < h; y++)
            {
                var row = y * w;
                float acc = 0;
                for (var k = -r; k <= r; k++) acc += src[row + Math.Clamp(k, 0, w - 1)];
                for (var x = 0; x < w; x++)
                {
                    dst[row + x] = acc * norm;
                    acc += src[row + Math.Min(x + r + 1, w - 1)] - src[row + Math.Max(x - r, 0)];
                }
            }
        }

        private static void BoxBlurV(float[] src, float[] dst, int w, int h, int r)
        {
            if (r <= 0) { Array.Copy(src, dst, src.Length); return; }
            var norm = 1f / (2 * r + 1);
            for (var x = 0; x < w; x++)
            {
                float acc = 0;
                for (var k = -r; k <= r; k++) acc += src[Math.Clamp(k, 0, h - 1) * w + x];
                for (var y = 0; y < h; y++)
                {
                    dst[y * w + x] = acc * norm;
                    acc += src[Math.Min(y + r + 1, h - 1) * w + x] - src[Math.Max(y - r, 0) * w + x];
                }
            }
        }

        // 3x3 rectangular-kernel morphology (cv2.MORPH_RECT, (3,3))
        public static GrayImage Dilate(GrayImage src, int iterations = 1, int radius = 1) => Morph(src, iterations, radius, true);
        public static GrayImage Erode(GrayImage src, int iterations = 1, int radius = 1) => Morph(src, iterations, radius, false);

        public static GrayImage Close(GrayImage src, int iterations = 1)
        {
            // cv2.morphologyEx(MORPH_CLOSE, iterations=n) = n dilations followed by n erosions
            return Erode(Dilate(src, iterations), iterations);
        }

        private static GrayImage Morph(GrayImage src, int iterations, int radius, bool dilate)
        {
            var current = src;
            for (var it = 0; it < iterations; it++)
            {
                var w = current.Width;
                var h = current.Height;
                // separable: rows then columns (valid for rectangular kernels)
                var tmp = new GrayImage(w, h);
                for (var y = 0; y < h; y++)
                for (var x = 0; x < w; x++)
                {
                    byte v = dilate ? (byte)0 : (byte)255;
                    for (var k = -radius; k <= radius; k++)
                    {
                        var xx = x + k;
                        if (xx < 0 || xx >= w) continue;
                        var s = current.Data[y * w + xx];
                        v = dilate ? Math.Max(v, s) : Math.Min(v, s);
                    }
                    tmp.Data[y * w + x] = v;
                }
                var next = new GrayImage(w, h);
                for (var y = 0; y < h; y++)
                for (var x = 0; x < w; x++)
                {
                    byte v = dilate ? (byte)0 : (byte)255;
                    for (var k = -radius; k <= radius; k++)
                    {
                        var yy = y + k;
                        if (yy < 0 || yy >= h) continue;
                        var s = tmp.Data[yy * w + x];
                        v = dilate ? Math.Max(v, s) : Math.Min(v, s);
                    }
                    next.Data[y * w + x] = v;
                }
                current = next;
            }
            return current;
        }

        // Labels 4- or 8-connected regions where predicate(value) holds. Returns label image
        // (0 = none) and component stats indexed by label - 1.
        public static int[] Label(GrayImage image, Func<byte, bool> predicate, bool eightConnected, out List<Component> components)
        {
            var w = image.Width;
            var h = image.Height;
            var labels = new int[w * h];
            components = new List<Component>();
            var stack = new Stack<int>();
            var next = 1;
            for (var start = 0; start < labels.Length; start++)
            {
                if (labels[start] != 0 || !predicate(image.Data[start])) continue;
                int area = 0, xMin = int.MaxValue, yMin = int.MaxValue, xMax = -1, yMax = -1;
                double sx = 0, sy = 0;
                var border = false;
                labels[start] = next;
                stack.Push(start);
                while (stack.Count > 0)
                {
                    var idx = stack.Pop();
                    var x = idx % w;
                    var y = idx / w;
                    area++;
                    sx += x;
                    sy += y;
                    if (x < xMin) xMin = x;
                    if (x > xMax) xMax = x;
                    if (y < yMin) yMin = y;
                    if (y > yMax) yMax = y;
                    if (x == 0 || y == 0 || x == w - 1 || y == h - 1) border = true;

                    for (var dy = -1; dy <= 1; dy++)
                    for (var dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dy == 0) continue;
                        if (!eightConnected && dx != 0 && dy != 0) continue;
                        var nx = x + dx;
                        var ny = y + dy;
                        if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                        var n = ny * w + nx;
                        if (labels[n] != 0 || !predicate(image.Data[n])) continue;
                        labels[n] = next;
                        stack.Push(n);
                    }
                }
                components.Add(new Component(next, area, xMin, yMin, xMax, yMax, (float)(sx / area), (float)(sy / area), border));
                next++;
            }
            return labels;
        }

        // scipy.ndimage.binary_fill_holes: anything not reachable from the border through
        // background becomes foreground
        public static GrayImage FillHoles(GrayImage mask)
        {
            var w = mask.Width;
            var h = mask.Height;
            var outside = new bool[w * h];
            var stack = new Stack<int>();
            void Seed(int x, int y)
            {
                var i = y * w + x;
                if (mask.Data[i] != 0 || outside[i]) return;
                outside[i] = true;
                stack.Push(i);
            }
            for (var x = 0; x < w; x++) { Seed(x, 0); Seed(x, h - 1); }
            for (var y = 0; y < h; y++) { Seed(0, y); Seed(w - 1, y); }
            while (stack.Count > 0)
            {
                var i = stack.Pop();
                var x = i % w;
                var y = i / w;
                if (x > 0) Seed(x - 1, y);
                if (x < w - 1) Seed(x + 1, y);
                if (y > 0) Seed(x, y - 1);
                if (y < h - 1) Seed(x, y + 1);
            }
            var result = new GrayImage(w, h);
            for (var i = 0; i < result.Data.Length; i++) result.Data[i] = outside[i] ? (byte)0 : (byte)255;
            return result;
        }

        public static GrayImage KeepLabel(int[] labels, int width, int height, int label)
        {
            var result = new GrayImage(width, height);
            for (var i = 0; i < labels.Length; i++) if (labels[i] == label) result.Data[i] = 255;
            return result;
        }

        // Summed-area table for fast box sums of a boolean/float map
        public static float[] Integral(float[] data, int w, int h)
        {
            var sat = new float[(w + 1) * (h + 1)];
            for (var y = 0; y < h; y++)
            {
                float row = 0;
                for (var x = 0; x < w; x++)
                {
                    row += data[y * w + x];
                    sat[(y + 1) * (w + 1) + x + 1] = sat[y * (w + 1) + x + 1] + row;
                }
            }
            return sat;
        }

        public static float BoxSum(float[] sat, int w, int x0, int y0, int x1, int y1)
        {
            // inclusive x0..x1, y0..y1 (already clamped)
            var W = w + 1;
            return sat[(y1 + 1) * W + x1 + 1] - sat[y0 * W + x1 + 1] - sat[(y1 + 1) * W + x0] + sat[y0 * W + x0];
        }
    }
}
