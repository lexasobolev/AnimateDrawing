using System;
using System.IO;
using AnimatedDrawingsWorld.Logic.Imaging;
using StbImageSharp;
using StbImageWriteSharp;

namespace AnimatedDrawingsWorld.Tests
{
    public static class TestSupport
    {
        public static string RepoRoot
        {
            get
            {
                var dir = AppContext.BaseDirectory;
                while (dir != null && !Directory.Exists(Path.Combine(dir, "Assets")))
                    dir = Path.GetDirectoryName(dir);
                return dir ?? throw new DirectoryNotFoundException("Could not locate the repository root");
            }
        }

        public static string TestImages => Path.Combine(RepoRoot, "Tests", "TestImages");
        public static string Characters => Path.Combine(TestImages, "characters");
        public static string Backgrounds => Path.Combine(TestImages, "backgrounds");
        public static string MotionsDir => Path.Combine(RepoRoot, "Assets", "StreamingAssets", "AnimatedDrawings", "Motions");

        public static string OutputDir
        {
            get
            {
                var dir = Path.Combine(RepoRoot, "Tests", "Output");
                Directory.CreateDirectory(dir);
                return dir;
            }
        }

        public static RgbaImage Load(string path)
        {
            using var stream = File.OpenRead(path);
            var result = ImageResult.FromStream(stream, StbImageSharp.ColorComponents.RedGreenBlueAlpha);
            return new RgbaImage(result.Width, result.Height, result.Data);
        }

        public static GrayImage LoadMask(string path)
        {
            var img = Load(path);
            var mask = new GrayImage(img.Width, img.Height);
            for (var i = 0; i < mask.Data.Length; i++) mask.Data[i] = img.Pixels[i * 4] > 127 ? (byte)255 : (byte)0;
            return mask;
        }

        public static void SavePng(RgbaImage image, string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            using var stream = File.Create(path);
            new ImageWriter().WritePng(image.Pixels, image.Width, image.Height, StbImageWriteSharp.ColorComponents.RedGreenBlueAlpha, stream);
        }

        public static float IoU(GrayImage a, GrayImage b)
        {
            if (a.Width != b.Width || a.Height != b.Height) throw new ArgumentException("mask sizes differ");
            int inter = 0, union = 0;
            for (var i = 0; i < a.Data.Length; i++)
            {
                var x = a.Data[i] != 0;
                var y = b.Data[i] != 0;
                if (x && y) inter++;
                if (x || y) union++;
            }
            return union == 0 ? 1f : (float)inter / union;
        }
    }
}
