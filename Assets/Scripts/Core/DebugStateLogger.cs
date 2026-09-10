using UnityEngine;

namespace AnimatedDrawingsWorld.Core
{
    // Temporary helper to verify the state machine in the Console before any art/Animator is set up.
    [RequireComponent(typeof(CharacterStateController))]
    public class DebugStateLogger : MonoBehaviour
    {
        private CharacterStateController controller;

        private void Awake() => controller = GetComponent<CharacterStateController>();
        private void OnEnable() => controller.StateChanged += Log;
        private void OnDisable() => controller.StateChanged -= Log;

        private void Log(BehaviorState state) => Debug.Log($"{name}: {state}", this);
    }
}
