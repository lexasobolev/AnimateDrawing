using System;
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
                    if (stateTimer <= 0f) EnterState(BehaviorState.Sleep);
                    break;

                case BehaviorState.Sleep:
                    if (stateTimer <= 0f)
                    {
                        timeSinceRest = 0f;
                        EnterState(BehaviorState.Idle);
                    }
                    break;

                default: // Wave, Surprised and other transient reaction states
                    if (stateTimer <= 0f) EnterState(BehaviorState.Idle);
                    break;
            }
        }

        // Called by InteractionListener to force a transient reaction (Wave, Surprised, ...)
        // or to wake a sleeping character. Autonomous ticking above resumes once the timer expires.
        public void Interrupt(BehaviorState state, float duration)
        {
            timeSinceRest = 0f;
            EnterState(state, duration);
        }

        private BehaviorState NextAfterIdle()
        {
            if (timeSinceRest > sleepinessAfter && UnityEngine.Random.value < yawnChanceWhenSleepy)
                return BehaviorState.Yawn;
            return BehaviorState.Walk;
        }

        private void TickWalk()
        {
            transform.position = Vector3.MoveTowards(transform.position, walkTarget, walkSpeed * Time.deltaTime);
            FaceDirection(walkTarget.x - transform.position.x);

            if (stateTimer <= 0f || Vector3.Distance(transform.position, walkTarget) < 0.05f)
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
