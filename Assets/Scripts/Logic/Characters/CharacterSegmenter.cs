using System;
using System.Collections.Generic;
using AnimatedDrawingsWorld.Logic.Imaging;

namespace AnimatedDrawingsWorld.Logic.Characters
{
    // C# port of Meta's examples/image_to_annotations.py segment() plus a detector-free crop,
    // so a drawing can be cut out of its paper at runtime even without the TorchServe models.
    public static class CharacterSegmenter
    {
        // Meta's detector is replaced by: segment the whole (downscaled) page, keep the biggest
        // plausible drawing, and crop to it with a small margin.
        public static bool TryFindDrawingBounds(RgbaImage page, out int x, out int y, out int width, out int height)
        {
            var small = page.ResizeToFit(400).Clone();
            var scale = (float)page.Width / small.Width;
            var sw = small.Width;
            var sh = small.Height;

            // strokes = local dark contrast after removing uneven lighting
            Scene.BackgroundAnalyzer.FlattenIllumination(small);
            var ink = ImageOps.AdaptiveThresholdInv(ImageOps.MinChannel(small), 31, 10f);

            // whatever touches the photo's border is table, fingers or the paper's edge
            var inkLabels = ImageOps.Label(ink, v => v != 0, true, out var inkComponents);
            var border = new bool[inkComponents.Count + 1];
            foreach (var c in inkComponents) border[c.Label] = c.TouchesBorder && (c.BoxWidth > sw * 0.3f || c.BoxHeight > sh * 0.3f || c.Area > 40);

            // ...and long straight lines are the edges of the sheet of paper, not a drawing
            var nearEdge = new int[inkComponents.Count + 1];
            for (var i = 0; i < inkLabels.Length; i++)
            {
                var label = inkLabels[i];
                if (label == 0) continue;
                var c = inkComponents[label - 1];
                var px = i % sw;
                var py = i / sw;
                if (px - c.XMin <= 2 || c.XMax - px <= 2 || py - c.YMin <= 2 || c.YMax - py <= 2) nearEdge[label]++;
            }
            foreach (var c in inkComponents)
            {
                var long_ = c.BoxWidth > sw * 0.4f || c.BoxHeight > sh * 0.4f;
                if (long_ && nearEdge[c.Label] > c.Area * 0.85f) border[c.Label] = true;
            }
            for (var i = 0; i < inkLabels.Length; i++) if (inkLabels[i] != 0 && border[inkLabels[i]]) ink.Data[i] = 0;

            // join the separate strokes of one drawing (eyes, limbs drawn with gaps)
            var link = Math.Max(2, Math.Max(sw, sh) / 45);
            var linked = ImageOps.Dilate(ink, 1, link);
            var labels = ImageOps.Label(linked, v => v != 0, true, out var components);
            var inkCount = new int[components.Count + 1];
            for (var i = 0; i < labels.Length; i++) if (ink.Data[i] != 0) inkCount[labels[i]]++;

            Component best = default;
            var bestScore = 0f;
            var cx = sw * 0.5f;
            var cy = sh * 0.5f;
            var diag = MathF.Sqrt(sw * sw + sh * sh);
            foreach (var c in components)
            {
                if (inkCount[c.Label] < sw * sh * 0.001f) continue;
                var centerDist = MathF.Sqrt((c.CentroidX - cx) * (c.CentroidX - cx) + (c.CentroidY - cy) * (c.CentroidY - cy)) / diag;
                // bigger drawings with more ink near the middle of the page win
                var extent = MathF.Sqrt(c.BoxWidth * c.BoxHeight);
                var score = extent * MathF.Sqrt(inkCount[c.Label]) * (1.3f - centerDist);
                if (score <= bestScore) continue;
                bestScore = score;
                best = c;
            }

            if (bestScore <= 0f)
            {
                x = y = width = height = 0;
                return false;
            }

            // the dilation grew the box by `link`; give some of it back, keep a margin
            var pad = link / 2 + 2;
            x = Math.Max(0, (int)((best.XMin + link - pad) * scale));
            y = Math.Max(0, (int)((best.YMin + link - pad) * scale));
            width = Math.Min(page.Width, (int)MathF.Ceiling((best.XMax - link + pad + 1) * scale)) - x;
            height = Math.Min(page.Height, (int)MathF.Ceiling((best.YMax - link + pad + 1) * scale)) - y;
            return width > 4 && height > 4;
        }

        // Meta segment(): adaptive threshold -> close x2 -> dilate x2 -> flood-fill background
        // from the borders -> keep largest region -> fill holes.
        public static GrayImage Segment(RgbaImage cropped)
        {
            var img = RawForeground(cropped, 115);
            img = ImageOps.Close(img, 2);
            img = ImageOps.Dilate(img, 2);

            // anything reachable from the border through non-stroke pixels is background
            var filled = ImageOps.FillHoles(img);

            // make sure edges aren't character (as Meta does before contour finding)
            var w = filled.Width;
            var h = filled.Height;
            for (var x = 0; x < w; x++) { filled[x, 0] = 0; filled[x, h - 1] = 0; }
            for (var y = 0; y < h; y++) { filled[0, y] = 0; filled[w - 1, y] = 0; }

            var labels = ImageOps.Label(filled, v => v != 0, true, out var components);
            if (components.Count == 0) return filled;
            var biggest = components[0];
            foreach (var c in components) if (c.Area > biggest.Area) biggest = c;
            return ImageOps.FillHoles(ImageOps.KeepLabel(labels, w, h, biggest.Label));
        }

        private static GrayImage RawForeground(RgbaImage image, int blockSize)
        {
            var gray = ImageOps.MinChannel(image);
            return ImageOps.AdaptiveThresholdInv(gray, blockSize, 8f);
        }

        // Full detector-free pipeline: crop + segment + trim + transparent texture. Returns the
        // final crop rectangle in the (<=1000px) working page's pixels, plus that page's scale.
        public static (RgbaImage texture, GrayImage mask, int cropX, int cropY) CutOut(RgbaImage page) => CutOut(page, out _);

        public static (RgbaImage texture, GrayImage mask, int cropX, int cropY) CutOut(RgbaImage page, out float workingScale)
        {
            // Meta resizes pages so the longest side is at most 1000px before annotating
            var working = page.ResizeToFit(1000);
            workingScale = (float)working.Width / page.Width;
            if (!TryFindDrawingBounds(working, out var x, out var y, out var w, out var h))
            {
                x = 0; y = 0; w = working.Width; h = working.Height;
            }

            var cropped = working.Crop(x, y, w, h);
            var mask = Segment(cropped);
            var (trimmedTexture, trimmedMask, offsetX, offsetY) = TrimToMask(cropped, mask, 4);
            return (ApplyMask(trimmedTexture, trimmedMask), trimmedMask, x + offsetX, y + offsetY);
        }

        public static RgbaImage ApplyMask(RgbaImage image, GrayImage mask)
        {
            var result = image.Clone();
            for (var i = 0; i < mask.Data.Length; i++)
                result.Pixels[i * 4 + 3] = mask.Data[i] != 0 ? image.Pixels[i * 4 + 3] : (byte)0;
            return result;
        }

        // PNGs that already carry transparency (e.g. a pre-cut sticker) don't need segmentation
        public static bool HasMeaningfulAlpha(RgbaImage image)
        {
            var transparent = 0;
            for (var i = 3; i < image.Pixels.Length; i += 4) if (image.Pixels[i] < 16) transparent++;
            var ratio = (float)transparent / (image.Width * image.Height);
            return ratio > 0.05f && ratio < 0.97f;
        }

        public static GrayImage MaskFromAlpha(RgbaImage image)
        {
            var mask = new GrayImage(image.Width, image.Height);
            for (var i = 0; i < mask.Data.Length; i++) mask.Data[i] = image.Pixels[i * 4 + 3] >= 128 ? (byte)255 : (byte)0;
            return mask;
        }

        public static (RgbaImage texture, GrayImage mask, int offsetX, int offsetY) TrimToMask(RgbaImage texture, GrayImage mask, int padding = 4)
        {
            int x0 = mask.Width, y0 = mask.Height, x1 = -1, y1 = -1;
            for (var y = 0; y < mask.Height; y++)
            for (var x = 0; x < mask.Width; x++)
            {
                if (mask[x, y] == 0) continue;
                if (x < x0) x0 = x;
                if (x > x1) x1 = x;
                if (y < y0) y0 = y;
                if (y > y1) y1 = y;
            }
            if (x1 < 0) return (texture, mask, 0, 0);
            x0 = Math.Max(0, x0 - padding); y0 = Math.Max(0, y0 - padding);
            x1 = Math.Min(mask.Width - 1, x1 + padding); y1 = Math.Min(mask.Height - 1, y1 + padding);
            var w = x1 - x0 + 1;
            var h = y1 - y0 + 1;
            var croppedMask = new GrayImage(w, h);
            for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
                croppedMask[x, y] = mask[x0 + x, y0 + y];
            return (texture.Crop(x0, y0, w, h), croppedMask, x0, y0);
        }

        internal static List<(int start, int end)> RowRuns(GrayImage mask, int y)
        {
            var runs = new List<(int, int)>();
            var start = -1;
            for (var x = 0; x < mask.Width; x++)
            {
                var inside = mask[x, y] != 0;
                if (inside && start < 0) start = x;
                if (!inside && start >= 0)
                {
                    runs.Add((start, x - 1));
                    start = -1;
                }
            }
            if (start >= 0) runs.Add((start, mask.Width - 1));
            return runs;
        }
    }
}
