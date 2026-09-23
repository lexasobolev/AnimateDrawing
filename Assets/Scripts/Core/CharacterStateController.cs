using System;
using System.Collections.Generic;
using UnityEngine;

namespace AnimatedDrawingsWorld.Core
{
    public class CharacterStateController : MonoBehaviour
    {
        [Header("Idle / Walk")]
        [SerializeField] private float minIdleTime = 1.5f;
        [SerializeField] private float maxIdleTime = 4f;
        [SerializeField] private float minWalkTime = 1.5f;
        [SerializeField] private float maxWalkTime = 3f;
        [SerializeField] private float walkSpeed = 1.2f;
        [SerializeField] private float walkRadius = 3f;
        [SerializeField, Range(0f, 1f)] private float danceChance = 0.15f;

        [Tooltip("Assumed number of speed oscillations in one loop of the walk clip. Analyzing the " +
                 "current zombie.bvh walk clip showed one smooth rise-then-fall arc per loop rather " +
                 "than distinct alternating footfalls, hence 1 — adjust if a different walk clip " +
                 "actually has a normal 2-per-loop (left, right) alternating gait.")]
        [SerializeField] private float gaitStepsPerLoop = 1f;
        [Tooltip("How pronounced the speed-up/slow-down per step is. 0 = constant speed (old behavior).")]
        [SerializeField, Range(0f, 0.9f)] private float gaitSpeedVariation = 0.5f;

        [Header("Dance movement")]
        [SerializeField] private float danceMoveSpeed = 0.4f;
        [SerializeField] private float danceMoveRadius = 1.5f;

        [Tooltip("How far to stop from another character when approaching them, so characters " +
                 "don't walk to the exact same point and overlap.")]
        [SerializeField] private float approachPersonalSpace = 1.2f;

        [Header("Sleepiness")]
        [SerializeField] private float sleepinessAfter = 20f;
        [SerializeField, Range(0f, 1f)] private float yawnChanceWhenSleepy = 0.5f;
        [SerializeField] private float yawnDuration = 1.2f;
        [SerializeField] private float minSleepDuration = 5f;
        [SerializeField] private float maxSleepDuration = 10f;

        public BehaviorState State { get; private set; } = BehaviorState.Idle;
        public event Action<BehaviorState> StateChanged;

        private Vector3 homePosition;
        private Vector3 walkTarget;
        private Transform walkTowardTransform;
        private float approachAngle;
        private Vector3 danceTarget;
        private float stateTimer;
        private float timeSinceRest;

        private MetaAnimatedDrawingPlayer animationPlayer;
        private bool waitingForAnimation;
        private readonly Queue<(BehaviorState state, float duration)> pendingInterrupts = new();

        private void Awake()
        {
            animationPlayer = GetComponent<MetaAnimatedDrawingPlayer>();
        }

        private void OnEnable()
        {
            if (animationPlayer != null) animationPlayer.AnimationLoopCompleted += OnAnimationLoopCompleted;
        }

        private void OnDisable()
        {
            if (animationPlayer != null) animationPlayer.AnimationLoopCompleted -= OnAnimationLoopCompleted;
        }

        private void Start()
        {
            homePosition = transform.position;
            EnterState(BehaviorState.Idle);
        }

        private void Update()
        {
            stateTimer -= Time.deltaTime;
            switch (State)
            {
                case BehaviorState.Idle:
                    timeSinceRest += Time.deltaTime;
                    if (stateTimer <= 0f) EnterState(NextAfterIdle());
                    break;

                case BehaviorState.Walk:
                    timeSinceRest += Time.deltaTime;
                    TickWalk();
                    break;

                case BehaviorState.Yawn:
                    if (stateTimer <= 0f && !waitingForAnimation) EnterState(BehaviorState.Sleep);
                    break;

                case BehaviorState.Sleep:
                    if (stateTimer <= 0f)
                    {
                        timeSinceRest = 0f;
                        EnterState(BehaviorState.Idle);
                    }
                    break;

                case BehaviorState.Dance:
                    TickDance();
                    if (stateTimer <= 0f && !waitingForAnimation) EnterState(BehaviorState.Idle);
                    break;

                default: // Wave, Surprised and other transient reaction states
                    if (stateTimer <= 0f && !waitingForAnimation) EnterState(BehaviorState.Idle);
                    break;
            }
        }

        // Called by InteractionListener to force a transient reaction (Wave, Surprised, ...)
        // or to wake a sleeping character. Idle/Walk/Sleep are continuous loops with no natural
        // "finish point", so interrupting them immediately looks seamless and keeps reactions
        // snappy; Wave/Surprised/Dance/Yawn are short performances that would look cut off mid-way,
        // so those still wait for the current clip's loop to complete before switching.
        public void Interrupt(BehaviorState state, float duration)
        {
            timeSinceRest = 0f;
            if (waitingForAnimation)
            {
                pendingInterrupts.Enqueue((state, duration));
                return;
            }
            EnterState(state, duration);
        }

        // Called by InteractionListener when this character has spotted another one worth
        // approaching. Walks toward the target's live position (re-aimed every frame, since the
        // other character may also be moving) instead of a random wander point.
        public void WalkToward(Transform target, float duration)
        {
            walkTowardTransform = target;
            // a fixed random angle around the target, chosen once so it doesn't jitter as they move
            approachAngle = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
            EnterState(BehaviorState.Walk, duration);
        }

        private void OnAnimationLoopCompleted()
        {
            waitingForAnimation = false;
            if (pendingInterrupts.Count > 0)
            {
                var (state, duration) = pendingInterrupts.Dequeue();
                EnterState(state, duration);
            }
        }

        private BehaviorState NextAfterIdle()
        {
            if (timeSinceRest > sleepinessAfter && UnityEngine.Random.value < yawnChanceWhenSleepy)
                return BehaviorState.Yawn;
            if (UnityEngine.Random.value < danceChance)
                return BehaviorState.Dance;
            return BehaviorState.Walk;
        }

        private void TickWalk()
        {
            if (walkTowardTransform != null)
            {
                var offset = new Vector3(Mathf.Cos(approachAngle), Mathf.Sin(approachAngle)) * approachPersonalSpace;
                walkTarget = walkTowardTransform.position + offset;
            }

            // real walking speed isn't constant — it dips at each footfall and peaks mid-stride.
            // Drive that from the walk clip's actual current playback phase (not an independent
            // timer) so the translation speed stays genuinely locked to the visible steps.
            var phase = animationPlayer != null ? animationPlayer.CurrentClipPhase01 : 0f;
            var gaitMultiplier = 1f - gaitSpeedVariation * Mathf.Cos(phase * gaitStepsPerLoop * 2f * Mathf.PI);

            transform.position = Vector3.MoveTowards(transform.position, walkTarget, walkSpeed * gaitMultiplier * Time.deltaTime);
            FaceDirection(walkTarget.x - transform.position.x);

            if (stateTimer <= 0f || Vector3.Distance(transform.position, walkTarget) < 0.05f)
                EnterState(BehaviorState.Idle);
        }

        private void TickDance()
        {
            if (Vector3.Distance(transform.position, danceTarget) < 0.05f)
                danceTarget = transform.position + (Vector3)(UnityEngine.Random.insideUnitCircle * danceMoveRadius);

            transform.position = Vector3.MoveTowards(transform.position, danceTarget, danceMoveSpeed * Time.deltaTime);
            FaceDirection(danceTarget.x - transform.position.x);
        }

        private void FaceDirection(float dx)
        {
            if (Mathf.Abs(dx) < 0.01f) return;
            var s = transform.localScale;
            s.x = Mathf.Abs(s.x) * (dx < 0f ? -1f : 1f);
            transform.localScale = s;
        }

        private void EnterState(BehaviorState next) => EnterState(next, DurationFor(next));

        private void EnterState(BehaviorState next, float duration)
        {
            State = next;
            stateTimer = duration;
            waitingForAnimation = animationPlayer != null && RequiresContinuity(next);

            if (next == BehaviorState.Walk)
            {
                if (walkTowardTransform == null)
                    walkTarget = homePosition + (Vector3)(UnityEngine.Random.insideUnitCircle * walkRadius);
            }
            else
            {
                walkTowardTransform = null;
            }

            if (next == BehaviorState.Dance)
                danceTarget = transform.position + (Vector3)(UnityEngine.Random.insideUnitCircle * danceMoveRadius);

            StateChanged?.Invoke(next);
        }

        // Idle/Walk/Sleep are continuous loops with no natural finish point, so cutting them off
        // to react immediately is seamless. Wave/Surprised/Dance/Yawn are short performances that
        // would look jarring if interrupted mid-way, so those play out their current loop first.
        private static bool RequiresContinuity(BehaviorState state) => state switch
        {
            BehaviorState.Idle or BehaviorState.Walk or BehaviorState.Sleep => false,
            _ => true
        };

        private float DurationFor(BehaviorState state) => state switch
        {
            BehaviorState.Idle => UnityEngine.Random.Range(minIdleTime, maxIdleTime),
            BehaviorState.Walk => UnityEngine.Random.Range(minWalkTime, maxWalkTime),
            BehaviorState.Yawn => yawnDuration,
            BehaviorState.Sleep => UnityEngine.Random.Range(minSleepDuration, maxSleepDuration),
            _ => 1f
        };
    }
}
