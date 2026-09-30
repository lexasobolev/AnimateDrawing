using System;
using System.Collections;
using UnityEngine;
#if UNITY_ANDROID
using UnityEngine.Android;
#endif

namespace AnimatedDrawingsWorld.Runtime
{
    // Full-screen in-app camera for photographing a drawing: live viewfinder, a big shutter
    // button, close, and front/back switching. Uses Unity's WebCamTexture, so it works on
    // Android, iOS and desktop webcams alike. The photo comes back upright as a readable texture.
    public sealed class CameraCapture : MonoBehaviour
    {
        public bool Visible { get; private set; }

        private WebCamTexture webcam;
        private int deviceIndex;
        private Action<Texture2D> onPhoto;
        private Texture2D shutterIcon, closeIcon, switchIcon, black;
        private string message;

        public static bool HasCamera => WebCamTexture.devices.Length > 0 || Application.isMobilePlatform;

        private void Awake()
        {
            shutterIcon = IconPainter.Shutter();
            closeIcon = IconPainter.Close();
            switchIcon = IconPainter.SwitchCamera();
            black = new Texture2D(1, 1);
            black.SetPixel(0, 0, Color.black);
            black.Apply();
        }

        public void Open(Action<Texture2D> photoTaken)
        {
            onPhoto = photoTaken;
            Visible = true;
            message = null;
            StartCoroutine(StartCamera());
        }

        public void Close()
        {
            Visible = false;
            StopCamera();
        }

        private IEnumerator StartCamera()
        {
#if UNITY_ANDROID
            if (!Permission.HasUserAuthorizedPermission(Permission.Camera))
            {
                Permission.RequestUserPermission(Permission.Camera);
                // the system dialog pauses the app; wait until the user answered
                var until = Time.realtimeSinceStartup + 30f;
                while (!Permission.HasUserAuthorizedPermission(Permission.Camera) && Time.realtimeSinceStartup < until && Visible)
                    yield return new WaitForSecondsRealtime(0.25f);
            }
#elif UNITY_IOS
            if (!Application.HasUserAuthorization(UserAuthorization.WebCam))
                yield return Application.RequestUserAuthorization(UserAuthorization.WebCam);
#endif
            yield return null;
            if (!Visible) yield break;

            var devices = WebCamTexture.devices;
            if (devices.Length == 0)
            {
                message = "No camera found (or no permission to use it).";
                yield break;
            }

            // drawings are photographed with the back camera
            deviceIndex = 0;
            for (var i = 0; i < devices.Length; i++)
                if (!devices[i].isFrontFacing) { deviceIndex = i; break; }
            Play();
        }

        private void Play()
        {
            StopCamera();
            var device = WebCamTexture.devices[deviceIndex];
            webcam = new WebCamTexture(device.name, 1920, 1080, 30);
            webcam.Play();
        }

        private void StopCamera()
        {
            if (webcam == null) return;
            webcam.Stop();
            Destroy(webcam);
            webcam = null;
        }

        private void Update()
        {
            // Android back button / Escape closes the camera
            if (Visible && Input.GetKeyDown(KeyCode.Escape)) Close();
        }

        private void OnGUI()
        {
            if (!Visible) return;
            GUI.depth = -100;
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), black);
            var safe = SafeAreaGui();
            var button = Mathf.Clamp(Mathf.Min(Screen.width, Screen.height) * 0.18f, 80f, 200f);

            if (webcam != null && webcam.width > 16) DrawPreview(safe, button);
            else GUI.Label(new Rect(safe.x, safe.center.y - 20, safe.width, 40), message ?? "Starting the camera...", CenteredLabel());

            var close = new Rect(safe.x + button * 0.15f, safe.y + button * 0.15f, button * 0.6f, button * 0.6f);
            if (Pressed(close, closeIcon)) { Close(); return; }

            if (WebCamTexture.devices.Length > 1)
            {
                var sw = new Rect(safe.xMax - button * 0.75f, safe.y + button * 0.15f, button * 0.6f, button * 0.6f);
                if (Pressed(sw, switchIcon))
                {
                    deviceIndex = (deviceIndex + 1) % WebCamTexture.devices.Length;
                    Play();
                }
            }

            // shutter on the side in landscape (where the thumb is), at the bottom in portrait
            var landscape = Screen.width > Screen.height;
            var shutter = landscape
                ? new Rect(safe.xMax - button * 1.15f, safe.center.y - button * 0.5f, button, button)
                : new Rect(safe.center.x - button * 0.5f, safe.yMax - button * 1.15f, button, button);
            if (webcam != null && webcam.width > 16 && Pressed(shutter, shutterIcon)) TakePhoto();
        }

        private void DrawPreview(Rect safe, float button)
        {
            var angle = webcam.videoRotationAngle;
            var sideways = angle % 180 != 0;
            var w = sideways ? webcam.height : webcam.width;
            var h = sideways ? webcam.width : webcam.height;
            var scale = Mathf.Min(safe.width / w, safe.height / h);
            var size = new Vector2(w * scale, h * scale);
            var center = safe.center;
            // draw unrotated, then rotate the GUI around the center (clockwise by the camera angle)
            var drawSize = sideways ? new Vector2(size.y, size.x) : size;
            var rect = new Rect(center - drawSize * 0.5f, drawSize);
            var matrix = GUI.matrix;
            GUIUtility.RotateAroundPivot(angle, center);
            var uv = webcam.videoVerticallyMirrored ? new Rect(0, 1, 1, -1) : new Rect(0, 0, 1, 1);
            GUI.DrawTextureWithTexCoords(rect, webcam, uv);
            GUI.matrix = matrix;

            // a frame hint: "put the drawing inside"
            var frame = new Rect(center.x - size.x * 0.4f, center.y - size.y * 0.4f, size.x * 0.8f, size.y * 0.8f);
            var c = new Color(1f, 1f, 1f, 0.6f);
            DrawCorner(frame.x, frame.y, 1, 1, c, button);
            DrawCorner(frame.xMax, frame.y, -1, 1, c, button);
            DrawCorner(frame.x, frame.yMax, 1, -1, c, button);
            DrawCorner(frame.xMax, frame.yMax, -1, -1, c, button);
        }

        private static void DrawCorner(float x, float y, int dx, int dy, Color c, float button)
        {
            var len = button * 0.35f;
            var t = Mathf.Max(3f, button * 0.03f);
            GUI.color = c;
            GUI.DrawTexture(new Rect(dx > 0 ? x : x - len, dy > 0 ? y : y - t, len, t), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(dx > 0 ? x : x - t, dy > 0 ? y : y - len, t, len), Texture2D.whiteTexture);
            GUI.color = Color.white;
        }

        private static bool Pressed(Rect r, Texture2D icon)
        {
            GUI.DrawTexture(r, icon, ScaleMode.ScaleToFit, true);
            var e = Event.current;
            if (e.type != EventType.MouseDown || (e.mousePosition - r.center).magnitude > r.width * 0.5f) return false;
            e.Use();
            return true;
        }

        private void TakePhoto()
        {
            var w = webcam.width;
            var h = webcam.height;
            var pixels = webcam.GetPixels32();
            if (webcam.videoVerticallyMirrored) FlipRows(pixels, w, h);
            // rotate clockwise in 90° steps until upright
            for (var turns = ((webcam.videoRotationAngle % 360) + 360) % 360 / 90; turns > 0; turns--)
            {
                pixels = RotateClockwise(pixels, w, h);
                (w, h) = (h, w);
            }

            var photo = new Texture2D(w, h, TextureFormat.RGBA32, false) { name = "Photo" };
            photo.SetPixels32(pixels);
            photo.Apply(false, false);
            var callback = onPhoto;
            Close();
            callback?.Invoke(photo);
        }

        // Unity pixel arrays are bottom row first (y up)
        private static Color32[] RotateClockwise(Color32[] src, int w, int h)
        {
            var dst = new Color32[src.Length];
            // new width = h, new height = w; (x, y) -> (y, w - 1 - x)
            for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
                dst[(w - 1 - x) * h + y] = src[y * w + x];
            return dst;
        }

        private static void FlipRows(Color32[] pixels, int w, int h)
        {
            var row = new Color32[w];
            for (var y = 0; y < h / 2; y++)
            {
                Array.Copy(pixels, y * w, row, 0, w);
                Array.Copy(pixels, (h - 1 - y) * w, pixels, y * w, w);
                Array.Copy(row, 0, pixels, (h - 1 - y) * w, w);
            }
        }

        public static Rect SafeAreaGui()
        {
            var s = Screen.safeArea;
            return new Rect(s.x, Screen.height - s.yMax, s.width, s.height);
        }

        private static GUIStyle CenteredLabel()
        {
            var style = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = Mathf.RoundToInt(Mathf.Clamp(Screen.height / 30f, 16f, 36f)) };
            style.normal.textColor = Color.white;
            return style;
        }

        private void OnDestroy()
        {
            StopCamera();
            Destroy(shutterIcon);
            Destroy(closeIcon);
            Destroy(switchIcon);
            Destroy(black);
        }
    }
}
