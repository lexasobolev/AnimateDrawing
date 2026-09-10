using UnityEngine;

namespace AnimatedDrawingsWorld.Core
{
    // Debug/preview aid: tints the SpriteRenderer per state so behavior and interactions
    // are visible before real animation clips exist. Remove once art is in place.
    [RequireComponent(typeof(CharacterStateController))]
    public class StateColorVisualizer : MonoBehaviour
    {
        [SerializeField] private SpriteRenderer spriteRenderer;

        private CharacterStateController controller;

        private void Awake()
        {
            controller = GetComponent<CharacterStateController>();
            if (spriteRenderer == null) spriteRenderer = GetComponentInChildren<SpriteRenderer>();
        }

        private void OnEnable() => controller.StateChanged += Apply;
        private void OnDisable() => controller.StateChanged -= Apply;

        private void Apply(BehaviorState state)
        {
            if (spriteRenderer == null) return;
            spriteRenderer.color = ColorFor(state);
        }

        private static Color ColorFor(BehaviorState state) => state switch
        {
            BehaviorState.Idle => Color.white,
            BehaviorState.Walk => new Color(0.4f, 0.8f, 1f),   // light blue
            BehaviorState.Yawn => new Color(1f, 0.85f, 0.3f),  // yellow
            BehaviorState.Sleep => new Color(0.5f, 0.4f, 0.9f),// purple
            BehaviorState.Wave => new Color(0.4f, 1f, 0.5f),   // green
            BehaviorState.Surprised => new Color(1f, 0.4f, 0.4f), // red
            _ => Color.white
        };
    }
}
