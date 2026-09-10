using System.Collections.Generic;
using AnimatedDrawingsWorld.Core;
using UnityEngine;

namespace AnimatedDrawingsWorld.Interaction
{
    [CreateAssetMenu(fileName = "InteractionRuleSet", menuName = "AnimatedDrawingsWorld/Interaction Rule Set")]
    public class InteractionRuleSet : ScriptableObject
    {
        public List<InteractionRule> rules = new();

        // "self"/"other" refer to the two characters meeting; matching is direction-independent,
        // so results are swapped when the rule matches in reverse order.
        public bool TryFindRule(BehaviorState self, BehaviorState other, out ResolvedInteraction resolved)
        {
            foreach (var rule in rules)
            {
                if (rule.stateA == self && rule.stateB == other)
                {
                    resolved = new ResolvedInteraction(rule.resultForA, rule.resultForB, rule.probability, rule.resultDuration, rule.cooldown);
                    return true;
                }
                if (rule.stateA == other && rule.stateB == self)
                {
                    resolved = new ResolvedInteraction(rule.resultForB, rule.resultForA, rule.probability, rule.resultDuration, rule.cooldown);
                    return true;
                }
            }

            resolved = default;
            return false;
        }
    }
}
