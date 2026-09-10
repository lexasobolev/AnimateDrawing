#if UNITY_EDITOR
using System.IO;
using AnimatedDrawingsWorld.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace AnimatedDrawingsWorld.EditorTools
{
    // Builds a ready-to-play scene (camera + standalone spawner) so the world doesn't
    // have to be assembled by hand. Run via the "AnimatedDrawingsWorld" menu.
    public static class WorldBootstrap
    {
        private const string PrefabPath = "Assets/Sprite.prefab";
        private const string ScenePath = "Assets/Scenes/MainScene.unity";

        [MenuItem("AnimatedDrawingsWorld/Build Sample Scene")]
        public static void BuildSampleScene()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null)
            {
                Debug.LogError($"WorldBootstrap: character prefab not found at {PrefabPath}. Aborting.");
                return;
            }

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // 2D orthographic camera.
            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            camGo.transform.position = new Vector3(0f, 0f, -10f);
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = 5f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.16f, 0.18f, 0.22f);
            camGo.AddComponent<AudioListener>();

            // Standalone world manager with the spawner (NOT part of the character).
            var worldGo = new GameObject("World");
            var spawner = worldGo.AddComponent<WorldSpawner>();

            // characterPrefabs / count are private [SerializeField]; assign via SerializedObject.
            var so = new SerializedObject(spawner);
            var prefabsProp = so.FindProperty("characterPrefabs");
            prefabsProp.arraySize = 1;
            prefabsProp.GetArrayElementAtIndex(0).objectReferenceValue = prefab;
            so.FindProperty("count").intValue = 3;
            so.ApplyModifiedPropertiesWithoutUndo();

            Directory.CreateDirectory("Assets/Scenes");
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };

            Debug.Log($"WorldBootstrap: created {ScenePath} with a camera and a spawner for {PrefabPath}. Press Play.");
        }
    }
}
#endif
