using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using AnimatedDrawingsWorld.Logic;
using AnimatedDrawingsWorld.Logic.Animation;
using AnimatedDrawingsWorld.Logic.Behavior;
using AnimatedDrawingsWorld.Logic.Characters;
using AnimatedDrawingsWorld.Logic.Imaging;
using AnimatedDrawingsWorld.Logic.Scene;
using UnityEngine;

namespace AnimatedDrawingsWorld.Runtime
{
    // The game: a background drawing that has been analyzed for objects, with drawn characters
    // living in it. Backgrounds and characters can be swapped/added at runtime (samples, image
    // files, URLs). Put this on an empty GameObject in an otherwise empty scene (see
    // Assets/Scenes/GameScene.unity or the "AnimatedDrawingsWorld/Build Game Scene" menu).
    public sealed class GameController : MonoBehaviour
    {
        [Header("World")]
        [SerializeField] private float worldHeight = 10f;
        [SerializeField] private float characterHeight = 2f;
        [SerializeField] private int randomSeed;

        [Header("Samples (StreamingAssets)")]
        [SerializeField] private string samplesManifest = "Samples/samples.json";
        [SerializeField] private string motionsFolder = "AnimatedDrawings/Motions";

        [Header("Meta Animated Drawings models")]
        [Tooltip("Use Meta's TorchServe detector + pose estimator when it's running (see META_ANIMATED_DRAWINGS_INTEGRATION.md). Falls back to the built-in segmenter/skeleton estimator otherwise.")]
        [SerializeField] private bool useMetaTorchServe = true;
        [SerializeField] private string torchServeUrl = "http://localhost:8080";

        public GameWorld World { get; private set; }
        public SceneLayout Layout => World?.Layout;
        public readonly List<CharacterView> Views = new();
        public CharacterView Selected { get; private set; }
        public SamplesManifest Samples { get; private set; } = new();
        public string CurrentBackgroundName { get; private set; } = "";
        public string Status { get; private set; } = "Starting...";
        public bool Busy => busyCount > 0;
        public readonly List<string> Log = new();
        public float Speed = 1f;
        public bool Paused;
        public bool ShowAnalysis;
        public CharacterKind? NewCharacterKind; // null = guess

        public Camera Camera { get; private set; }

        private readonly MotionLibrary motions = new();
        private BackgroundView background;
        private MetaTorchServeClient torchServe;
        private int busyCount;

        // drag & drop state
        private CharacterView pressed;
        private Vector2 pressScreen;
        private Vector2 dragOffset;
        private bool dragging;

        public Func<Vector2, bool> IsPointerOverUi = _ => false;

        public static GameController Instance { get; private set; }

        // Pressing Play in any scene (an empty one, or a scene saved before this game existed)
        // still starts the game: if no scene provides a GameController, create one.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void EnsureGameExists()
        {
            if (Instance != null) return;
            var game = new GameObject("Game");
            game.AddComponent<GameController>();
            game.AddComponent<GameUI>();
            Debug.Log("AnimatedDrawingsWorld: no GameController in the scene, created one.");
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            Application.logMessageReceived += OnLogMessage;
        }

        private void OnDestroy()
        {
            if (Instance != this) return;
            Instance = null;
            Application.logMessageReceived -= OnLogMessage;
        }

        // surface errors in the game itself, so a problem never looks like a silent blank screen
        private void OnLogMessage(string message, string stackTrace, LogType type)
        {
            if (type != LogType.Error && type != LogType.Exception) return;
            var firstLine = message.Split('\n')[0];
            AddLog("<color=#ff8080>Error: " + firstLine + "</color>");
            SetStatus("Error: " + firstLine);
        }

        private IEnumerator Start()
        {
            Camera = Camera.main;
            if (Camera == null)
            {
                var cameraObject = new GameObject("Main Camera") { tag = "MainCamera" };
                Camera = cameraObject.AddComponent<Camera>();
                cameraObject.AddComponent<AudioListener>();
            }
            Camera.orthographic = true;
            Camera.clearFlags = CameraClearFlags.SolidColor;
            Camera.backgroundColor = new Color(0.93f, 0.91f, 0.86f);

            background = new GameObject("Background").AddComponent<BackgroundView>();
            background.transform.SetParent(transform, false);
            torchServe = new MetaTorchServeClient(torchServeUrl);
            if (GetComponent<GameUI>() == null) gameObject.AddComponent<GameUI>();

            SetStatus("Loading Meta motions...");
            foreach (var clip in MotionLibrary.RequiredClipNames())
                yield return StreamingAssetsIO.ReadText($"{motionsFolder}/{clip}.json",
                    json => motions.Add(MotionClip.FromJson(json)),
                    error => AddLog($"Missing motion {clip}: {error}"));

            yield return StreamingAssetsIO.ReadText(samplesManifest, json => Samples = SamplesManifest.Parse(json), error => AddLog("No samples manifest: " + error));

            if (useMetaTorchServe) StartCoroutine(torchServe.Ping());

            if (Samples.Backgrounds.Count > 0) yield return LoadBackgroundRoutine(Samples.Backgrounds[0].Path, Samples.Backgrounds[0].Name);
            else yield return ApplyBackground(MakeBlankPaper(), "blank paper");

            foreach (var c in Samples.Characters)
                if (c.StartInScene) yield return LoadSampleCharacterRoutine(c);

            SetStatus("Drag characters around, click one for commands. Load your own drawings from the toolbar.");
        }

        private void Update()
        {
            if (World == null) return;
            var dt = Paused ? 0f : Time.deltaTime * Speed;
            HandlePointer();
            if (dt > 0f) World.Step(dt);
            foreach (var view in Views) view.Tick(dt);
        }

        // ------------------------------------------------------------------ backgrounds

        public void LoadSampleBackground(SamplesManifest.Entry entry) => StartCoroutine(LoadBackgroundRoutine(entry.Path, entry.Name));

        public void LoadBackgroundFromPathOrUrl(string pathOrUrl) =>
            StartCoroutine(LoadBackgroundRoutine(pathOrUrl, Path.GetFileNameWithoutExtension(pathOrUrl.Split('?')[0])));

        private IEnumerator LoadBackgroundRoutine(string location, string name)
        {
            busyCount++;
            SetStatus($"Loading background '{name}'...");
            byte[] bytes = null;
            yield return StreamingAssetsIO.ReadBytes(location, b => bytes = b, e => SetStatus("Could not load background: " + e));
            var texture = bytes != null ? TextureConversion.Decode(bytes, name) : null;
            if (texture == null)
            {
                if (bytes != null) SetStatus($"'{name}' is not a PNG/JPG image");
                busyCount--;
                yield break;
            }
            yield return ApplyBackground(TextureConversion.LimitSize(texture, 2048), name);
            busyCount--;
        }

        private IEnumerator ApplyBackground(Texture2D texture, string name)
        {
            SetStatus($"Looking for trees, grass, water... in '{name}'");
            var image = TextureConversion.ToImage(texture);
            SceneLayout layout = null;
            yield return RunOffThread(() => layout = BackgroundAnalyzer.Analyze(image));
            if (layout == null) yield break;

            if (World == null)
            {
                World = new GameWorld(layout, randomSeed != 0 ? randomSeed : Environment.TickCount, worldHeight);
                World.Log += (_, line) => AddLog(line);
            }
            else World.SetLayout(layout);

            CurrentBackgroundName = name;
            background.Show(texture, World.Width, World.Height);
            FitCamera();
            AddLog($"Background '{name}': found {Summarize(layout)}");
            SetStatus($"'{name}' is ready");
        }

        private static string Summarize(SceneLayout layout)
        {
            var counts = new SortedDictionary<string, int>();
            foreach (var o in layout.Objects)
            {
                var key = o.Kind.ToString().ToLowerInvariant();
                counts[key] = counts.TryGetValue(key, out var n) ? n + 1 : 1;
            }
            if (counts.Count == 0) return "no objects (characters will walk on the paper)";
            var parts = new List<string>();
            foreach (var pair in counts) parts.Add(pair.Value > 1 ? $"{pair.Value} {pair.Key}s" : pair.Key);
            return string.Join(", ", parts);
        }

        private static Texture2D MakeBlankPaper()
        {
            var image = new RgbaImage(640, 400);
            for (var i = 0; i < image.Pixels.Length; i++) image.Pixels[i] = 246;
            return TextureConversion.ToTexture(image);
        }

        public void FitCamera()
        {
            if (World == null || Camera == null) return;
            var margin = 1.12f; // leave room for the toolbar
            var sizeForHeight = World.Height * 0.5f * margin;
            var sizeForWidth = World.Width * 0.5f / Mathf.Max(0.1f, Camera.aspect) * 1.02f;
            Camera.orthographicSize = Mathf.Max(sizeForHeight, sizeForWidth);
            Camera.transform.position = new Vector3(World.Width * 0.5f, World.Height * 0.5f + World.Height * (margin - 1f) * 0.35f, -10f);
        }

        private int lastScreenWidth, lastScreenHeight;

        private void LateUpdate()
        {
            if (Screen.width == lastScreenWidth && Screen.height == lastScreenHeight) return;
            lastScreenWidth = Screen.width;
            lastScreenHeight = Screen.height;
            FitCamera();
        }

        // ------------------------------------------------------------------ characters

        public void LoadSampleCharacter(SamplesManifest.CharacterEntry entry) => StartCoroutine(LoadSampleCharacterRoutine(entry));

        private IEnumerator LoadSampleCharacterRoutine(SamplesManifest.CharacterEntry entry)
        {
            busyCount++;
            SetStatus($"Bringing {entry.Name} to life...");
            byte[] texture = null, mask = null;
            string cfg = null;
            yield return StreamingAssetsIO.ReadBytes($"{entry.Folder}/texture.png", b => texture = b, e => SetStatus(e));
            yield return StreamingAssetsIO.ReadBytes($"{entry.Folder}/mask.png", b => mask = b, e => SetStatus(e));
            yield return StreamingAssetsIO.ReadText($"{entry.Folder}/char_cfg.yaml", t => cfg = t, e => SetStatus(e));
            if (texture != null && mask != null && cfg != null)
            {
                var textureTex = TextureConversion.Decode(texture);
                var maskTex = TextureConversion.Decode(mask);
                var annotation = CharacterBuilder.FromMetaFiles(TextureConversion.ToImage(textureTex), TextureConversion.ToMask(maskTex), cfg);
                Destroy(textureTex);
                Destroy(maskTex);
                yield return BuildAndSpawn(annotation, entry.Name, NewCharacterKind ?? entry.Kind);
            }
            busyCount--;
        }

        public void LoadCharacterFromPathOrUrl(string pathOrUrl) => StartCoroutine(LoadCharacterRoutine(pathOrUrl));

        private IEnumerator LoadCharacterRoutine(string location)
        {
            busyCount++;
            var name = Path.GetFileNameWithoutExtension(location.Split('?')[0]);
            SetStatus($"Loading drawing '{name}'...");
            byte[] bytes = null;
            yield return StreamingAssetsIO.ReadBytes(location, b => bytes = b, e => SetStatus("Could not load drawing: " + e));
            var texture = bytes != null ? TextureConversion.Decode(bytes, name) : null;
            if (texture == null)
            {
                busyCount--;
                yield break;
            }
            yield return AddCharacterFromTexture(TextureConversion.LimitSize(texture, 1600), name);
            Destroy(texture);
            busyCount--;
        }

        public IEnumerator AddCharacterFromTexture(Texture2D texture, string name)
        {
            var page = TextureConversion.ToImage(texture);
            CharacterAnnotation annotation = null;

            if (useMetaTorchServe && torchServe.Available == true && !CharacterSegmenter.HasMeaningfulAlpha(page))
            {
                SetStatus($"Asking Meta's models to find {name}'s skeleton...");
                yield return torchServe.Annotate(page, a => annotation = a, error => AddLog($"Meta models: {error} — using the built-in skeleton finder"));
            }

            if (annotation == null)
            {
                SetStatus($"Cutting {name} out of the paper and finding the skeleton...");
                yield return RunOffThread(() => annotation = CharacterBuilder.AnnotateWithoutModels(page));
            }
            if (annotation == null) yield break;
            AddLog($"{name}: skeleton from {(annotation.Source == "meta-torchserve" ? "Meta's pose model" : "the built-in estimator")}");
            yield return BuildAndSpawn(annotation, name, NewCharacterKind);
        }

        private IEnumerator BuildAndSpawn(CharacterAnnotation annotation, string name, CharacterKind? kind)
        {
            BuiltCharacter built = null;
            yield return RunOffThread(() => built = CharacterBuilder.Build(annotation));
            if (built == null || World == null) yield break;

            var finalKind = kind ?? built.GuessedKind;
            var height = characterHeight * UnityEngine.Random.Range(0.85f, 1.1f) * (finalKind == CharacterKind.Animal ? 0.65f : 1f);
            var agent = World.AddCharacter(UniqueName(name), finalKind, height);
            var view = new GameObject("Character " + agent.Name).AddComponent<CharacterView>();
            view.transform.SetParent(transform, false);
            view.Initialize(World, agent, built, motions);
            Views.Add(view);
            SetStatus($"{agent.Name} the {finalKind.ToString().ToLowerInvariant()} joined!");
        }

        private string UniqueName(string name)
        {
            name = string.IsNullOrWhiteSpace(name) ? "Drawing" : name.Replace('_', ' ').Trim();
            name = char.ToUpperInvariant(name[0]) + name.Substring(1);
            var candidate = name;
            var n = 1;
            while (Views.Exists(v => v.Agent.Name == candidate)) candidate = $"{name} {++n}";
            return candidate;
        }

        public void Remove(CharacterView view)
        {
            if (view == null) return;
            if (Selected == view) Select(null);
            World.Remove(view.Agent);
            Views.Remove(view);
            Destroy(view.gameObject);
        }

        public void Select(CharacterView view)
        {
            if (Selected != null) Selected.SetHighlighted(false);
            Selected = view;
            if (Selected != null) Selected.SetHighlighted(true);
        }

        public void SetKind(CharacterView view, CharacterKind kind)
        {
            view.Agent.Kind = kind;
            AddLog($"{view.Agent.Name} is now a {kind.ToString().ToLowerInvariant()}");
        }

        // ------------------------------------------------------------------ pointer

        public Vector2 ScreenToWorld(Vector2 screen)
        {
            var w = Camera.ScreenToWorldPoint(new Vector3(screen.x, screen.y, -Camera.transform.position.z));
            return new Vector2(w.x, w.y);
        }

        private void HandlePointer()
        {
            if (Camera == null) return;
            var mouse = (Vector2)Input.mousePosition;
            var world = ScreenToWorld(mouse);

            if (Input.GetMouseButtonDown(0) && !IsPointerOverUi(mouse))
            {
                pressed = Pick(world);
                pressScreen = mouse;
                dragging = false;
                Select(pressed);
                if (pressed != null) dragOffset = new Vector2(pressed.Agent.Feet.X, pressed.Agent.Feet.Y) - world;
            }

            if (pressed != null && Input.GetMouseButton(0))
            {
                if (!dragging && (mouse - pressScreen).magnitude > 6f)
                {
                    dragging = true;
                    World.BeginDrag(pressed.Agent);
                }
                if (dragging) World.DragTo(pressed.Agent, new V2(world.x + dragOffset.x, world.y + dragOffset.y));
            }

            if (pressed != null && Input.GetMouseButtonUp(0))
            {
                if (dragging) World.EndDrag(pressed.Agent);
                pressed = null;
                dragging = false;
            }

            // right click: the selected character walks there
            if (Input.GetMouseButtonDown(1) && Selected != null && !IsPointerOverUi(mouse))
                World.CommandWalkTo(Selected.Agent, new V2(world.x, world.y));
        }

        private CharacterView Pick(Vector2 world)
        {
            CharacterView best = null;
            var bestOrder = int.MinValue;
            foreach (var v in Views)
            {
                if (v.Agent.Opacity < 0.3f || !v.Contains(world)) continue;
                var order = v.GetComponent<MeshRenderer>().sortingOrder;
                if (order <= bestOrder) continue;
                bestOrder = order;
                best = v;
            }
            return best;
        }

        // ------------------------------------------------------------------ helpers

        public void AddLog(string line)
        {
            Log.Add(line);
            if (Log.Count > 40) Log.RemoveAt(0);
        }

        public void SetStatus(string status) => Status = status;

        // heavy image work runs on a worker thread (except on WebGL, which has none)
        private static IEnumerator RunOffThread(Action work)
        {
            if (Application.platform == RuntimePlatform.WebGLPlayer)
            {
                work();
                yield break;
            }
            var task = Task.Run(work);
            while (!task.IsCompleted) yield return null;
            if (task.Exception != null) Debug.LogException(task.Exception.InnerException ?? task.Exception);
        }
    }

    // StreamingAssets/Samples/samples.json: which sample backgrounds and characters exist
    // (StreamingAssets can't be listed on Android/WebGL, so it's an explicit manifest).
    public sealed class SamplesManifest
    {
        public sealed class Entry
        {
            public string Name;
            public string Path;
        }

        public sealed class CharacterEntry
        {
            public string Name;
            public string Folder;
            public CharacterKind Kind;
            public bool StartInScene;
        }

        public readonly List<Entry> Backgrounds = new();
        public readonly List<CharacterEntry> Characters = new();

        public static SamplesManifest Parse(string json)
        {
            var root = MiniJson.Obj(MiniJson.Parse(json));
            var manifest = new SamplesManifest();
            foreach (var b in MiniJson.Arr(MiniJson.Get(root, "backgrounds")) ?? new List<object>())
            {
                var o = MiniJson.Obj(b);
                manifest.Backgrounds.Add(new Entry { Name = MiniJson.Str(MiniJson.Get(o, "name")), Path = MiniJson.Str(MiniJson.Get(o, "path")) });
            }
            foreach (var c in MiniJson.Arr(MiniJson.Get(root, "characters")) ?? new List<object>())
            {
                var o = MiniJson.Obj(c);
                Enum.TryParse(MiniJson.Str(MiniJson.Get(o, "kind"), "Human"), true, out CharacterKind kind);
                manifest.Characters.Add(new CharacterEntry
                {
                    Name = MiniJson.Str(MiniJson.Get(o, "name")),
                    Folder = MiniJson.Str(MiniJson.Get(o, "folder")),
                    Kind = kind,
                    StartInScene = MiniJson.Get(o, "start") is bool start && start,
                });
            }
            return manifest;
        }
    }
}
