using UnityEngine;

namespace AnimatedDrawingsWorld.Core
{
    [RequireComponent(typeof(CharacterStateController))]
    public class CharacterAnimatorDriver : MonoBehaviour
    {
        [SerializeField] private Animator animator;
        [SerializeField] private float crossFadeDuration = 0.1f;

        private CharacterStateController controller;

        private void Awake()
        {
            controller = GetComponent<CharacterStateController>();
            if (animator == null) animator = GetComponentInChildren<Animator>();
            if (animator == null)
                Debug.LogWarning($"{name}: CharacterAnimatorDriver found no Animator — state changes won't be visualized.", this);
        }

        private void OnEnable() => controller.StateChanged += OnStateChanged;
        private void OnDisable() => controller.StateChanged -= OnStateChanged;

        // Animator Controller must have a state named exactly like each BehaviorState value
        // (Idle, Walk, Yawn, Sleep, Wave, Surprised) — see setup notes.
        private void OnStateChanged(BehaviorState state)
        {
            if (animator == null || animator.runtimeAnimatorController == null) return;
            animator.CrossFade(state.ToString(), crossFadeDuration);
        }
    }
}
