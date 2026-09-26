#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AnimatedDrawingsWorld.Runtime;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace AnimatedDrawingsWorld.EditorTools
{
    public static class GameSceneTools
    {
        private const string ScenePath = "Assets/Scenes/GameScene.unity";
        private const string SamplesRoot = "Assets/StreamingAssets/Samples";

        // (Re)creates the playable scene: a camera plus the GameController, which builds the
        // background, characters and UI at runtime.
        [MenuItem("AnimatedDrawingsWorld/Build Game Scene")]
        public static void BuildGameScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var cameraObject = new GameObject("Main Camera") { tag = "MainCamera" };
            var camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 5.6f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.93f, 0.91f, 0.86f);
            cameraObject.transform.position = new Vector3(7f, 5f, -10f);
            cameraObject.AddComponent<AudioListener>();

            var game = new GameObject("Game");
            game.AddComponent<GameController>();
            game.AddComponent<GameUI>();
            game.AddComponent<EditModePreview>();

            Directory.CreateDirectory("Assets/Scenes");
            EditorSceneManager.SaveScene(scene, ScenePath);

            // the game scene first in the build, keep the older sample scenes after it
            var scenes = EditorBuildSettings.scenes.Where(s => s.path != ScenePath).ToList();
            scenes.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
            Debug.Log($"GameSceneTools: created {ScenePath}. Press Play.");
        }

        // Rewrites StreamingAssets/Samples/samples.json from the folders on disk: every image in
        // Samples/Backgrounds and every folder with texture.png + mask.png + char_cfg.yaml in
        // Samples/Characters (existing names/kinds are kept).
        [MenuItem("AnimatedDrawingsWorld/Refresh Samples Manifest")]
        public static void RefreshSamplesManifest()
        {
            var manifestPath = Path.Combine(SamplesRoot, "samples.json");
            var old = File.Exists(manifestPath) ? SamplesManifest.Parse(File.ReadAllText(manifestPath)) : new SamplesManifest();

            var backgrounds = new List<object>();
            foreach (var file in Directory.GetFiles(Path.Combine(SamplesRoot, "Backgrounds")).Where(IsImage).OrderBy(f => f))
            {
                var path = "Samples/Backgrounds/" + Path.GetFileName(file);
                var known = old.Backgrounds.FirstOrDefault(b => b.Path == path);
                backgrounds.Add(new Dictionary<string, object> { ["name"] = known?.Name ?? Nice(Path.GetFileNameWithoutExtension(file)), ["path"] = path });
            }

            var characters = new List<object>();
            var folders = Directory.GetDirectories(Path.Combine(SamplesRoot, "Characters")).Select(d => "Samples/Characters/" + Path.GetFileName(d)).ToList();
            // keep entries that point elsewhere (e.g. characters made by the old MP4 pipeline)
            folders.AddRange(old.Characters.Select(c => c.Folder).Where(f => !f.StartsWith("Samples/Characters/")));
            foreach (var folder in folders.Distinct())
            {
                var dir = Path.Combine("Assets/StreamingAssets", folder);
                if (!File.Exists(Path.Combine(dir, "texture.png")) || !File.Exists(Path.Combine(dir, "mask.png")) || !File.Exists(Path.Combine(dir, "char_cfg.yaml"))) continue;
                var known = old.Characters.FirstOrDefault(c => c.Folder == folder);
                characters.Add(new Dictionary<string, object>
                {
                    ["name"] = known?.Name ?? Nice(Path.GetFileName(folder)),
                    ["folder"] = folder,
                    ["kind"] = (known?.Kind ?? Logic.Behavior.CharacterKind.Human).ToString(),
                    ["start"] = known?.StartInScene ?? false,
                });
            }

            var json = Logic.MiniJson.Serialize(new Dictionary<string, object> { ["backgrounds"] = backgrounds, ["characters"] = characters });
            File.WriteAllText(manifestPath, json);
            AssetDatabase.Refresh();
            Debug.Log($"GameSceneTools: {backgrounds.Count} backgrounds and {characters.Count} characters in {manifestPath}");
        }

        private static bool IsImage(string f) => f.EndsWith(".png") || f.EndsWith(".jpg") || f.EndsWith(".jpeg");

        private static string Nice(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1).Replace('_', ' ');
    }
}
#endif
