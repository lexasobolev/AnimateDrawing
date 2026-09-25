using AnimatedDrawingsWorld.Logic.Imaging;
using UnityEngine;

namespace AnimatedDrawingsWorld.Runtime
{
    // Unity textures are stored bottom row first; the logic images are top row first.
    public static class TextureConversion
    {
        public static RgbaImage ToImage(Texture2D texture)
        {
            var pixels = texture.GetPixels32();
            var w = texture.width;
            var h = texture.height;
            var image = new RgbaImage(w, h);
            for (var y = 0; y < h; y++)
            {
                var src = (h - 1 - y) * w;
                for (var x = 0; x < w; x++)
                {
                    var c = pixels[src + x];
                    image.Set(x, y, c.r, c.g, c.b, c.a);
                }
            }
            return image;
        }

        public static Texture2D ToTexture(RgbaImage image, bool clamp = true)
        {
            var texture = new Texture2D(image.Width, image.Height, TextureFormat.RGBA32, false);
            var pixels = new Color32[image.Width * image.Height];
            for (var y = 0; y < image.Height; y++)
            {
                var dst = (image.Height - 1 - y) * image.Width;
                for (var x = 0; x < image.Width; x++)
                {
                    image.Get(x, y, out var r, out var g, out var b, out var a);
                    pixels[dst + x] = new Color32(r, g, b, a);
                }
            }
            texture.SetPixels32(pixels);
            texture.wrapMode = clamp ? TextureWrapMode.Clamp : TextureWrapMode.Repeat;
            texture.filterMode = FilterMode.Bilinear;
            texture.Apply(false, false);
            return texture;
        }

        public static GrayImage ToMask(Texture2D texture)
        {
            var image = ToImage(texture);
            var mask = new GrayImage(image.Width, image.Height);
            for (var i = 0; i < mask.Data.Length; i++)
            {
                // Meta's mask.png is single channel (0/255): any bright pixel is inside
                mask.Data[i] = image.Pixels[i * 4] > 127 ? (byte)255 : (byte)0;
            }
            return mask;
        }

        // decodes PNG/JPG bytes; the result stays CPU-readable
        public static Texture2D Decode(byte[] bytes, string name = "image")
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false) { name = name };
            if (!texture.LoadImage(bytes, false))
            {
                Object.Destroy(texture);
                return null;
            }
            texture.wrapMode = TextureWrapMode.Clamp;
            return texture;
        }

        // very large photos slow analysis and eat memory; scale them down first
        public static Texture2D LimitSize(Texture2D texture, int maxDimension)
        {
            if (Mathf.Max(texture.width, texture.height) <= maxDimension) return texture;
            var resized = ToImage(texture).ResizeToFit(maxDimension);
            var result = ToTexture(resized);
            result.name = texture.name;
            Object.Destroy(texture);
            return result;
        }
    }
}
