using System;

namespace AnimatedDrawingsWorld.Logic.Imaging
{
    public enum ColorClass : byte
    {
        Paper,
        Dark,
        Gray,
        Red,
        Orange,
        Yellow,
        Green,
        LightBlue,
        Blue,
        Purple,
        Pink,
        Brown,
    }

    public static class ColorClassifier
    {
        public const int ClassCount = 12;

        public static void RgbToHsv(byte r, byte g, byte b, out float h, out float s, out float v)
        {
            float rf = r / 255f, gf = g / 255f, bf = b / 255f;
            var max = Math.Max(rf, Math.Max(gf, bf));
            var min = Math.Min(rf, Math.Min(gf, bf));
            var d = max - min;
            v = max;
            s = max <= 0f ? 0f : d / max;
            if (d <= 1e-5f)
            {
                h = 0f;
                return;
            }

            if (max == rf) h = 60f * (((gf - bf) / d) % 6f);
            else if (max == gf) h = 60f * ((bf - rf) / d + 2f);
            else h = 60f * ((rf - gf) / d + 4f);
            if (h < 0f) h += 360f;
        }

        // Crayon/marker oriented classification. `paperBrightness` lets photos of paper taken in
        // dim light (grey-ish "white") still count as paper.
        public static ColorClass Classify(byte r, byte g, byte b, float paperBrightness = 0.78f)
        {
            RgbToHsv(r, g, b, out var h, out var s, out var v);

            if (v < 0.22f) return ColorClass.Dark;
            if (s < 0.16f || (v > 0.9f && s < 0.22f))
            {
                if (v >= paperBrightness - 0.1f) return ColorClass.Paper;
                return v < 0.42f ? ColorClass.Dark : ColorClass.Gray;
            }

            // low-saturation warm colours in dim light look like paper photographed under a lamp
            if (s < 0.24f && v >= paperBrightness && (h < 60f || h > 330f)) return ColorClass.Paper;

            if ((h < 45f || h >= 340f) && v < 0.62f && s > 0.25f) return ColorClass.Brown;
            if (h >= 45f && h < 60f && v < 0.5f) return ColorClass.Brown;

            if (h < 15f || h >= 345f) return ColorClass.Red;
            if (h < 42f) return ColorClass.Orange;
            if (h < 68f) return ColorClass.Yellow;
            if (h < 165f) return ColorClass.Green;
            if (h < 205f) return ColorClass.LightBlue;
            if (h < 255f) return s < 0.45f && v > 0.7f ? ColorClass.LightBlue : ColorClass.Blue;
            if (h < 295f) return ColorClass.Purple;
            return ColorClass.Pink;
        }

        public static bool IsChromatic(ColorClass c) => c != ColorClass.Paper && c != ColorClass.Dark && c != ColorClass.Gray;
    }
}
