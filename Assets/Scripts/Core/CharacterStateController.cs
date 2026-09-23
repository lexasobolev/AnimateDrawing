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
                    if (stateTimer <= 0f && !waitingForAnimation) EnterState(NextAfterIdle());
                    break;

                case BehaviorState.Walk:
                    timeSinceRest += Time.deltaTime;
                    TickWalk();
                    break;

                case BehaviorState.Yawn:
                    if (stateTimer <= 0f && !waitingForAnimation) EnterState(BehaviorState.Sleep);
                    break;

                case BehaviorState.Sleep:
                    if (stateTimer <= 0f && !waitingForAnimation)
                    {
                        timeSinceRest = 0f;
                        EnterState(BehaviorState.Idle);
                    }
                    break;

                default: // Wave, Surprised and other transient reaction states
                    if (stateTimer <= 0f && !waitingForAnimation) EnterState(BehaviorState.Idle);
                    break;
            }
        }

        // Called by InteractionListener to force a transient reaction (Wave, Surprised, ...)
        // or to wake a sleeping character. If the current state's animation clip hasn't finished
        // a loop yet, the request is queued and applied once it does — never cuts a clip short.
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
            transform.position = Vector3.MoveTowards(transform.position, walkTarget, walkSpeed * Time.deltaTime);
            FaceDirection(walkTarget.x - transform.position.x);

            if (!waitingForAnimation && (stateTimer <= 0f || Vector3.Distance(transform.position, walkTarget) < 0.05f))
                EnterState(BehaviorState.Idle);
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
            waitingForAnimation = animationPlayer != null;
            if (next == BehaviorState.Walk)
                walkTarget = homePosition + (Vector3)(UnityEngine.Random.insideUnitCircle * walkRadius);

            StateChanged?.Invoke(next);
        }

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
