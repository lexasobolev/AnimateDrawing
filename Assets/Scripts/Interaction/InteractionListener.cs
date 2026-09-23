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

        [Header("Seeking out interactions")]
        [Tooltip("How far a character can notice another one worth approaching. Keep this comfortably " +
                 "bigger than double the trigger collider's radius (scaled by character size), or it " +
                 "won't extend interaction range beyond characters incidentally bumping into each other.")]
        [SerializeField] private float awarenessRadius = 10f;
        [SerializeField] private float seekCheckInterval = 0.75f;
        [Tooltip("Safety timeout for an approach walk, in case the target wanders off before being reached.")]
        [SerializeField] private float walkToInteractDuration = 10f;

        private static int nextTieBreakId;
        private int tieBreakId;

        // every active listener, so an idle character can notice one outside trigger range and
        // walk over — OnTriggerStay2D alone only fires once they've already bumped into each other
        private static readonly List<InteractionListener> All = new();

        private CharacterStateController controller;
        private readonly Dictionary<InteractionListener, float> cooldownReadyAt = new();
        private float nextSeekCheckAt;

        // who this character is currently walking toward, if anyone — reserved so no other idle
        // character also picks the same target and they all converge/stack on top of it
        private InteractionListener approachTarget;

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

        private void OnEnable() => All.Add(this);
        private void OnDisable() => All.Remove(this);

        private void Update()
        {
            // release the reservation once we're no longer actually walking toward them, whether
            // because we arrived and interacted, got interrupted into something else, or timed out
            if (approachTarget != null && controller.State != BehaviorState.Walk)
                approachTarget = null;

            // only look for someone to approach while genuinely idle, so this never fights an
            // already-purposeful Walk (random wander or an approach already in progress)
            if (ruleSet == null || controller.State != BehaviorState.Idle) return;
            if (Time.time < nextSeekCheckAt) return;
            nextSeekCheckAt = Time.time + seekCheckInterval;

            InteractionListener best = null;
            var bestDistance = awarenessRadius;
            foreach (var other in All)
            {
                if (other == this || IsBusy(other.controller.State)) continue;
                if (cooldownReadyAt.TryGetValue(other, out var readyAt) && Time.time < readyAt) continue;
                if (!ruleSet.TryFindRule(controller.State, other.controller.State, out _)) continue;
                if (IsAlreadyBeingApproached(other)) continue;

                var distance = Vector3.Distance(transform.position, other.transform.position);
                if (distance >= bestDistance) continue;
                best = other;
                bestDistance = distance;
            }

            if (best == null) return;
            approachTarget = best;
            controller.WalkToward(best.transform, walkToInteractDuration);
        }

        private static bool IsAlreadyBeingApproached(InteractionListener candidate)
        {
            foreach (var listener in All)
                if (listener.approachTarget == candidate) return true;
            return false;
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
