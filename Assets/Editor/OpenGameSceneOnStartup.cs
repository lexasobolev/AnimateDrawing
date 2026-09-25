#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;

namespace AnimatedDrawingsWorld.EditorTools
{
    // When the project opens on an empty "Untitled" scene, open the game scene instead so
    // pressing Play shows the game straight away.
    [InitializeOnLoad]
    public static class OpenGameSceneOnStartup
    {
        private const string ScenePath = "Assets/Scenes/GameScene.unity";
        private const string SessionKey = "AnimatedDrawingsWorld.OpenedGameScene";

        static OpenGameSceneOnStartup() => EditorApplication.delayCall += OpenIfUntitled;

        private static void OpenIfUntitled()
        {
            if (SessionState.GetBool(SessionKey, false) || EditorApplication.isPlayingOrWillChangePlaymode) return;
            SessionState.SetBool(SessionKey, true);
            var active = EditorSceneManager.GetActiveScene();
            if (!string.IsNullOrEmpty(active.path) || active.isDirty || !File.Exists(ScenePath)) return;
            EditorSceneManager.OpenScene(ScenePath);
        }
    }
}
#endif
