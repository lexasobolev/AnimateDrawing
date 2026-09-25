using System;
using System.Collections;
using System.Collections.Generic;
using AnimatedDrawingsWorld.Logic;
using AnimatedDrawingsWorld.Logic.Characters;
using AnimatedDrawingsWorld.Logic.Imaging;
using UnityEngine;
using UnityEngine.Networking;

namespace AnimatedDrawingsWorld.Runtime
{
    // Talks to Meta's TorchServe container (Tools/AnimatedDrawings/torchserve, see
    // META_ANIMATED_DRAWINGS_INTEGRATION.md) directly from the game — the same two requests
    // Meta's image_to_annotations.py makes: humanoid detection, then pose estimation on the crop.
    // Segmentation uses the C# port of Meta's segment().
    public sealed class MetaTorchServeClient
    {
        public readonly string BaseUrl;
        public bool? Available { get; private set; }

        public MetaTorchServeClient(string baseUrl = "http://localhost:8080") => BaseUrl = baseUrl.TrimEnd('/');

        public IEnumerator Ping(float timeoutSeconds = 1.5f)
        {
            using var request = UnityWebRequest.Get(BaseUrl + "/ping");
            request.timeout = Mathf.CeilToInt(timeoutSeconds);
            yield return request.SendWebRequest();
            Available = request.result == UnityWebRequest.Result.Success && request.downloadHandler.text.Contains("Healthy");
        }

        public IEnumerator Annotate(RgbaImage page, Action<CharacterAnnotation> done, Action<string> failed)
        {
            // Meta resizes to at most 1000px before detection
            var image = page.ResizeToFit(1000);
            List<object> detections = null;
            yield return Post("/predictions/drawn_humanoid_detector", image, json => detections = MiniJson.Arr(json), failed);
            if (detections == null) yield break;
            if (detections.Count == 0)
            {
                failed("Meta's detector found no human-like figure in this drawing");
                yield break;
            }

            // highest score wins (as in image_to_annotations.py)
            Dictionary<string, object> best = null;
            var bestScore = float.MinValue;
            foreach (var d in detections)
            {
                var o = MiniJson.Obj(d);
                var score = MiniJson.Num(MiniJson.Get(o, "score"));
                if (score <= bestScore) continue;
                bestScore = score;
                best = o;
            }

            var bbox = MiniJson.Arr(MiniJson.Get(best, "bbox"));
            var l = Mathf.Clamp(Mathf.RoundToInt(MiniJson.Num(bbox[0])), 0, image.Width - 2);
            var t = Mathf.Clamp(Mathf.RoundToInt(MiniJson.Num(bbox[1])), 0, image.Height - 2);
            var r = Mathf.Clamp(Mathf.RoundToInt(MiniJson.Num(bbox[2])), l + 1, image.Width);
            var b = Mathf.Clamp(Mathf.RoundToInt(MiniJson.Num(bbox[3])), t + 1, image.Height);
            var cropped = image.Crop(l, t, r - l, b - t);
            var mask = CharacterSegmenter.Segment(cropped);

            List<object> poses = null;
            yield return Post("/predictions/drawn_humanoid_pose_estimator", cropped, json => poses = MiniJson.Arr(json), failed);
            if (poses == null) yield break;
            if (poses.Count != 1)
            {
                failed($"Meta's pose estimator found {poses.Count} skeletons, expected 1");
                yield break;
            }

            var keypoints = new List<V2>();
            foreach (var k in MiniJson.Arr(MiniJson.Get(MiniJson.Obj(poses[0]), "keypoints")))
            {
                var p = MiniJson.Arr(k);
                keypoints.Add(new V2(Mathf.Clamp(MiniJson.Num(p[0]), 0, cropped.Width - 1), Mathf.Clamp(MiniJson.Num(p[1]), 0, cropped.Height - 1)));
            }

            var annotation = CocoSkeletonMapping.FromKeypoints(cropped.Width, cropped.Height, keypoints);
            annotation.Mask = mask;
            annotation.Texture = CharacterSegmenter.ApplyMask(cropped, mask);
            done(annotation);
        }

        private IEnumerator Post(string path, RgbaImage image, Action<object> done, Action<string> failed)
        {
            var texture = TextureConversion.ToTexture(image);
            var png = texture.EncodeToPNG();
            UnityEngine.Object.Destroy(texture);

            // requests.post(files={'data': bytes}) == multipart form with a file field "data"
            var form = new List<IMultipartFormSection> { new MultipartFormFileSection("data", png, "data.png", "image/png") };
            using var request = UnityWebRequest.Post(BaseUrl + path, form);
            request.timeout = 60;
            yield return request.SendWebRequest();
            if (request.result != UnityWebRequest.Result.Success)
            {
                Available = false;
                failed($"TorchServe {path} failed: {request.error}");
                yield break;
            }

            object json;
            try
            {
                json = MiniJson.Parse(request.downloadHandler.text);
            }
            catch (Exception e)
            {
                failed($"TorchServe returned invalid JSON: {e.Message}");
                yield break;
            }

            if (json is Dictionary<string, object> error && error.ContainsKey("code"))
            {
                failed($"TorchServe error: {MiniJson.Str(MiniJson.Get(error, "message"), "unknown")}");
                yield break;
            }
            done(json);
        }
    }
}
