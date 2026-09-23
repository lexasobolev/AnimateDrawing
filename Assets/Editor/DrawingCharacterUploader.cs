#if UNITY_EDITOR
using System;
using System.IO;
using System.Text;
using Process = System.Diagnostics.Process;
using ProcessStartInfo = System.Diagnostics.ProcessStartInfo;
using AnimatedDrawingsWorld.Core;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace AnimatedDrawingsWorld.EditorTools
{
    public static class DrawingCharacterUploader
    {
        private const string MetaOutputFolder = "Assets/StreamingAssets/AnimatedDrawings";
        private const string MetaRepository = "Tools/AnimatedDrawings";
        private const string PrefabPath = "Assets/Sprite.prefab";
        private const float CharacterScale = 2.5f;
        private static Process activeMetaProcess;
        private static Action<bool> activeMetaCompleted;
        private static readonly StringBuilder metaOutput = new();
        private static readonly StringBuilder metaError = new();

        [MenuItem("AnimatedDrawingsWorld/Upload and Animate with Meta...")]
        public static void UploadAndAnimateWithMeta()
        {
            var sourcePath = EditorUtility.OpenFilePanel("Choose a human-like drawing", "", "png,jpg,jpeg");
            if (string.IsNullOrEmpty(sourcePath)) return;

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null)
            {
                UnityEngine.Debug.LogError($"DrawingCharacterUploader: character prefab not found at {PrefabPath}.");
                return;
            }

            var fileName = MakeSafeAssetName(Path.GetFileNameWithoutExtension(sourcePath));
            var outputRelative = $"{MetaOutputFolder}/{fileName}";
            var outputAbsolute = Path.GetFullPath(outputRelative);
            var inputAbsolute = Path.Combine(outputAbsolute, "input" + Path.GetExtension(sourcePath).ToLowerInvariant());
            Directory.CreateDirectory(outputAbsolute);
            File.Copy(sourcePath, inputAbsolute, true);

            RunMetaPipeline(inputAbsolute, outputAbsolute, succeeded =>
            {
                if (!succeeded) return;

                AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
                var character = CreateCharacter(prefab, fileName, inputAbsolute);
                if (character == null) return;

                var metaPlayer = character.AddComponent<MetaAnimatedDrawingPlayer>();
                metaPlayer.Configure($"AnimatedDrawings/{fileName}");
                Undo.RegisterCreatedObjectUndo(character, "Upload and animate drawing with Meta");
                Selection.activeGameObject = character;
                EditorSceneManager.MarkSceneDirty(character.scene);
                UnityEngine.Debug.Log($"DrawingCharacterUploader: Meta Animated Drawings character created at {character.transform.position}.", character);
            });
        }

        private static void RunMetaPipeline(string inputAbsolute, string outputAbsolute, Action<bool> completed)
        {
            if (activeMetaProcess != null)
            {
                UnityEngine.Debug.LogWarning("DrawingCharacterUploader: a Meta animation is already running.");
                completed(false);
                return;
            }

            var projectRoot = Directory.GetParent(Application.dataPath).FullName;
            var repository = Path.Combine(projectRoot, MetaRepository);
            var bridge = Path.Combine(repository, "unity_animate.py");
            if (!File.Exists(bridge))
            {
                UnityEngine.Debug.LogError($"DrawingCharacterUploader: Meta bridge not found at {bridge}.");
                completed(false);
                return;
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = "python",
                Arguments = $"\"{bridge}\" \"{inputAbsolute}\" \"{outputAbsolute}\"",
                WorkingDirectory = repository,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            activeMetaProcess = new Process { StartInfo = startInfo };
            try
            {
                metaOutput.Clear();
                metaError.Clear();
                activeMetaProcess.OutputDataReceived += (_, args) =>
                {
                    if (args.Data != null) metaOutput.AppendLine(args.Data);
                };
                activeMetaProcess.ErrorDataReceived += (_, args) =>
                {
                    if (args.Data != null) metaError.AppendLine(args.Data);
                };
                activeMetaProcess.Start();
                activeMetaProcess.BeginOutputReadLine();
                activeMetaProcess.BeginErrorReadLine();
                activeMetaCompleted = completed;
                EditorApplication.update += PollMetaProcess;
            }
            catch (Exception exception)
            {
                UnityEngine.Debug.LogError($"Could not start Meta Animated Drawings. Install Python and its dependencies: {exception.Message}");
                activeMetaProcess.Dispose();
                activeMetaProcess = null;
                completed(false);
            }
        }

        private static void PollMetaProcess()
        {
            if (activeMetaProcess == null || !activeMetaProcess.HasExited) return;

            EditorApplication.update -= PollMetaProcess;
            var succeeded = activeMetaProcess.ExitCode == 0;
            if (!string.IsNullOrWhiteSpace(metaOutput.ToString())) UnityEngine.Debug.Log(metaOutput.ToString());
            if (!succeeded)
                UnityEngine.Debug.LogError($"Meta Animated Drawings failed ({activeMetaProcess.ExitCode}): {metaError}");

            var completed = activeMetaCompleted;
            activeMetaCompleted = null;
            activeMetaProcess.Dispose();
            activeMetaProcess = null;
            completed?.Invoke(succeeded);
        }

        private static GameObject CreateCharacter(GameObject prefab, string name, string imageAbsolute)
        {
            var world = FindWorldRoot();
            var character = (GameObject)PrefabUtility.InstantiatePrefab(prefab, world != null ? world.scene : default);
            character.name = $"Character_{name}";
            character.transform.position = PickSpawnPosition(world);
            character.transform.localScale = Vector3.one * CharacterScale;

            var renderer = character.GetComponent<SpriteRenderer>();
            if (renderer == null)
            {
                UnityEngine.Debug.LogError("DrawingCharacterUploader: Sprite.prefab has no SpriteRenderer.");
                UnityEngine.Object.DestroyImmediate(character);
                return null;
            }

            var assetPath = "Assets/StreamingAssets/AnimatedDrawings/" + name + "/input" + Path.GetExtension(imageAbsolute);
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
                ConfigureAsSprite(assetPath);
                AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
                renderer.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
            return character;
        }

        private static void ConfigureAsSprite(string assetPath)
        {
            var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer == null) return;

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.SaveAndReimport();
        }

        private static GameObject FindWorldRoot()
        {
            var world = GameObject.Find("World");
            return world != null ? world : new GameObject("World");
        }

        private static Vector3 PickSpawnPosition(GameObject world)
        {
            var camera = Camera.main;
            if (camera != null)
            {
                var point = camera.ViewportToWorldPoint(new Vector3(
                    UnityEngine.Random.Range(0.2f, 0.8f),
                    UnityEngine.Random.Range(0.3f, 0.65f),
                    -camera.transform.position.z));
                return new Vector3(point.x, point.y, 0f);
            }

            return world.transform.position;
        }

        private static string MakeSafeAssetName(string value)
        {
            foreach (var invalid in Path.GetInvalidFileNameChars())
                value = value.Replace(invalid, '_');
            return string.IsNullOrWhiteSpace(value) ? "Drawing" : value;
        }
    }
}
#endif
