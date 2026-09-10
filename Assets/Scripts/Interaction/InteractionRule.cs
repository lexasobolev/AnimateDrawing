using System;
using AnimatedDrawingsWorld.Core;
using UnityEngine;

namespace AnimatedDrawingsWorld.Interaction
{
    [Serializable]
    public class InteractionRule
    {
        public string ruleName = "New Rule";

        [Header("Trigger (order-independent: A meets B or B meets A)")]
        public BehaviorState stateA = BehaviorState.Idle;
        public BehaviorState stateB = BehaviorState.Idle;

        [Range(0f, 1f)] public float probability = 1f;

        [Header("Result")]
        public BehaviorState resultForA = BehaviorState.Wave;
        public BehaviorState resultForB = BehaviorState.Wave;
        public float resultDuration = 1.2f;
        public float cooldown = 3f;
    }

    public readonly struct ResolvedInteraction
    {
        public readonly BehaviorState SelfResult;
        public readonly BehaviorState OtherResult;
        public readonly float Probability;
        public readonly float Duration;
        public readonly float Cooldown;

        public ResolvedInteraction(BehaviorState selfResult, BehaviorState otherResult, float probability, float duration, float cooldown)
        {
            SelfResult = selfResult;
            OtherResult = otherResult;
            Probability = probability;
            Duration = duration;
            Cooldown = cooldown;
        }
    }
}
