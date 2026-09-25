using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

namespace AnimatedDrawingsWorld.Runtime
{
    // Reads StreamingAssets on every platform (a plain folder on desktop, inside the APK on
    // Android, a URL on WebGL) plus arbitrary files and web URLs the player picks.
    public static class StreamingAssetsIO
    {
        public static string PathFor(string relative) => Path.Combine(Application.streamingAssetsPath, relative).Replace('\\', '/');

        public static IEnumerator ReadBytes(string relativeOrAbsoluteOrUrl, Action<byte[]> done, Action<string> failed = null)
        {
            var location = relativeOrAbsoluteOrUrl;
            var isUrl = location.Contains("://");
            if (!isUrl && !Path.IsPathRooted(location)) location = PathFor(location);

            // local files: direct read is fastest and works in the Editor/standalone players
            if (!location.Contains("://") && File.Exists(location))
            {
                byte[] bytes = null;
                string error = null;
                try { bytes = File.ReadAllBytes(location); }
                catch (Exception e) { error = e.Message; }
                if (bytes != null) done(bytes);
                else failed?.Invoke(error);
                yield break;
            }

            if (!location.Contains("://")) location = "file://" + location;
            using var request = UnityWebRequest.Get(location);
            yield return request.SendWebRequest();
            if (request.result == UnityWebRequest.Result.Success) done(request.downloadHandler.data);
            else failed?.Invoke($"{request.error} ({relativeOrAbsoluteOrUrl})");
        }

        public static IEnumerator ReadText(string relative, Action<string> done, Action<string> failed = null) =>
            ReadBytes(relative, bytes => done(System.Text.Encoding.UTF8.GetString(bytes)), failed);
    }
}
