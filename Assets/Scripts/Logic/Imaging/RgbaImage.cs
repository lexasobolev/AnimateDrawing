using System;

namespace AnimatedDrawingsWorld.Logic.Imaging
{
    // Plain RGBA8 image, row 0 = TOP (image convention, same as the PNG files and Meta's
    // annotation pixel coordinates). Unity's Texture2D rows are bottom-up; the Unity layer
    // flips when converting (see Runtime/TextureConversion.cs).
    public sealed class RgbaImage
    {
        public readonly int Width;
        public readonly int Height;
        public readonly byte[] Pixels; // RGBA, Width * Height * 4

        public RgbaImage(int width, int height)
        {
            if (width <= 0 || height <= 0) throw new ArgumentException($"Invalid image size {width}x{height}");
            Width = width;
            Height = height;
            Pixels = new byte[width * height * 4];
        }

        public RgbaImage(int width, int height, byte[] rgba)
        {
            if (rgba.Length != width * height * 4) throw new ArgumentException("Pixel buffer size mismatch");
            Width = width;
            Height = height;
            Pixels = rgba;
        }

        public int Index(int x, int y) => (y * Width + x) * 4;

        public bool InBounds(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height;

        public void Get(int x, int y, out byte r, out byte g, out byte b, out byte a)
        {
            var i = Index(x, y);
            r = Pixels[i];
            g = Pixels[i + 1];
            b = Pixels[i + 2];
            a = Pixels[i + 3];
        }

        public void Set(int x, int y, byte r, byte g, byte b, byte a = 255)
        {
            var i = Index(x, y);
            Pixels[i] = r;
            Pixels[i + 1] = g;
            Pixels[i + 2] = b;
            Pixels[i + 3] = a;
        }

        public byte Alpha(int x, int y) => Pixels[Index(x, y) + 3];

        public RgbaImage Clone() => new(Width, Height, (byte[])Pixels.Clone());

        public RgbaImage Crop(int x0, int y0, int width, int height)
        {
            x0 = Math.Max(0, x0);
            y0 = Math.Max(0, y0);
            width = Math.Min(width, Width - x0);
            height = Math.Min(height, Height - y0);
            var result = new RgbaImage(width, height);
            for (var y = 0; y < height; y++)
                Buffer.BlockCopy(Pixels, Index(x0, y0 + y), result.Pixels, y * width * 4, width * 4);
            return result;
        }

        // area-averaging downscale (or nearest upscale); good enough for analysis passes
        public RgbaImage Resize(int newWidth, int newHeight)
        {
            var result = new RgbaImage(newWidth, newHeight);
            var sx = (float)Width / newWidth;
            var sy = (float)Height / newHeight;
            for (var y = 0; y < newHeight; y++)
            {
                var y0 = (int)(y * sy);
                var y1 = Math.Max(y0 + 1, Math.Min(Height, (int)((y + 1) * sy)));
                for (var x = 0; x < newWidth; x++)
                {
                    var x0 = (int)(x * sx);
                    var x1 = Math.Max(x0 + 1, Math.Min(Width, (int)((x + 1) * sx)));
                    int r = 0, g = 0, b = 0, a = 0, n = 0;
                    for (var yy = y0; yy < y1; yy++)
                    for (var xx = x0; xx < x1; xx++)
                    {
                        var i = Index(xx, yy);
                        r += Pixels[i];
                        g += Pixels[i + 1];
                        b += Pixels[i + 2];
                        a += Pixels[i + 3];
                        n++;
                    }
                    result.Set(x, y, (byte)(r / n), (byte)(g / n), (byte)(b / n), (byte)(a / n));
                }
            }
            return result;
        }

        public RgbaImage ResizeToFit(int maxDimension)
        {
            var largest = Math.Max(Width, Height);
            if (largest <= maxDimension) return this;
            var scale = (float)maxDimension / largest;
            return Resize(Math.Max(1, (int)MathF.Round(Width * scale)), Math.Max(1, (int)MathF.Round(Height * scale)));
        }
    }

    // single-channel byte image (masks, grayscale)
    public sealed class GrayImage
    {
        public readonly int Width;
        public readonly int Height;
        public readonly byte[] Data;

        public GrayImage(int width, int height)
        {
            Width = width;
            Height = height;
            Data = new byte[width * height];
        }

        public GrayImage(int width, int height, byte[] data)
        {
            Width = width;
            Height = height;
            Data = data;
        }

        public byte this[int x, int y]
        {
            get => Data[y * Width + x];
            set => Data[y * Width + x] = value;
        }

        public bool InBounds(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height;

        public GrayImage Clone() => new(Width, Height, (byte[])Data.Clone());

        public int CountNonZero()
        {
            var n = 0;
            foreach (var v in Data) if (v != 0) n++;
            return n;
        }
    }
}
