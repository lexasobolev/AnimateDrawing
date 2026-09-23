using System;

namespace AnimatedDrawingsWorld.Core
{
    [Serializable]
    public class AiAnimationPlan
    {
        public AiAnimationStep[] steps;
    }

    [Serializable]
    public class AiAnimationStep
    {
        public string state;
        public float duration;
    }
}
