using System;
using UnityEngine;

namespace AnimatedDrawingsWorld.Runtime
{
    // Picking an existing picture: the phone's photo library on Android/iOS (via the
    // NativeGallery package, see Packages/manifest.json), a file dialog elsewhere.
    public static class PhotoSource
    {
        public static bool HasPhoneGallery =>
            Application.platform == RuntimePlatform.Android || Application.platform == RuntimePlatform.IPhonePlayer;

        // done(null) when the user cancelled
        public static void PickFromGallery(string title, Action<Texture2D> done)
        {
#if UNITY_ANDROID || UNITY_IOS
            NativeGallery.GetImageFromGallery(path =>
            {
                if (string.IsNullOrEmpty(path))
                {
                    done(null);
                    return;
                }
                // LoadImageAtPath also applies the photo's EXIF rotation; keep it readable for analysis
                var texture = NativeGallery.LoadImageAtPath(path, 2048, false, false);
                if (texture == null) Debug.LogError("Could not open that picture.");
                done(texture);
            }, title, "image/*");
#else
            done(null);
#endif
        }
    }
}
