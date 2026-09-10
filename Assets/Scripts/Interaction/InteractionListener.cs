using System.Collections.Generic;
using AnimatedDrawingsWorld.Core;
using UnityEngine;

namespace AnimatedDrawingsWorld.Interaction
{
    // Requires a trigger CircleCollider2D sized to the character's "awareness radius"
    // on a dedicated layer that only collides with other characters (see setup notes).
    // A Rigidbody2D is mandatory: Unity only raises 2D trigger events when at least one
    // of the overlapping objects has one. It is forced to Kinematic so gravity can't move it.
    [RequireComponent(typeof(CharacterStateController))]
    [RequireComponent(typeof(Collider2D))]
    [RequireComponent(typeof(Rigidbody2D))]
    public class InteractionListener : MonoBehaviour
    {
        [SerializeField] private InteractionRuleSet ruleSet;

        private static int nextTieBreakId;
        private int tieBreakId;

        private CharacterStateController controller;
        private readonly Dictionary<InteractionListener, float> cooldownReadyAt = new();

        private void Awake()
        {
            controller = GetComponent<CharacterStateController>();
            tieBreakId = nextTieBreakId++;

            var body = GetComponent<Rigidbody2D>();
            body.bodyType = RigidbodyType2D.Kinematic;
            // Kinematic bodies only report contacts against Dynamic bodies by default; both
            // characters are Kinematic, so without this their triggers would never overlap.
            body.useFullKinematicContacts = true;

            if (ruleSet == null)
                Debug.LogWarning($"{name}: InteractionListener has no Rule Set assigned — this character will never interact.", this);
        }

        private void OnTriggerStay2D(Collider2D other)
        {
            if (ruleSet == null) return;

            var otherListener = other.GetComponent<InteractionListener>();
            if (otherListener == null || otherListener == this) return;

            if (IsBusy(controller.State) || IsBusy(otherListener.controller.State)) return;

            if (cooldownReadyAt.TryGetValue(otherListener, out var readyAt) && Time.time < readyAt) return;

            if (!ruleSet.TryFindRule(controller.State, otherListener.controller.State, out var resolved)) return;
            if (Random.value > resolved.Probability) return;

            // Both characters detect each other in the same frame; only the lower-tie-break-ID
            // side triggers the pair so the interaction doesn't fire (and get resolved) twice.
            if (tieBreakId > otherListener.tieBreakId) return;

            controller.Interrupt(resolved.SelfResult, resolved.Duration);
            otherListener.controller.Interrupt(resolved.OtherResult, resolved.Duration);

            var nextReadyAt = Time.time + resolved.Cooldown;
            cooldownReadyAt[otherListener] = nextReadyAt;
            otherListener.cooldownReadyAt[this] = nextReadyAt;
        }

        private static bool IsBusy(BehaviorState state) =>
            state is BehaviorState.Wave or BehaviorState.Surprised;
    }
}
