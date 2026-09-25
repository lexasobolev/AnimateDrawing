namespace AnimatedDrawingsWorld.Logic.Behavior
{
    // Everything a character can be doing. Meta's BVH motions cover the "performances"
    // (walk, wave, dance, jump); poses Meta has no capture for (climbing, sitting, lying down,
    // swimming...) are generated procedurally on the same rig (see ProceduralMotions).
    public enum Activity
    {
        Idle,
        Walk,
        Run,
        Climb,
        Sit,
        Sleep,
        Yawn,
        Wave,
        Surprised,
        Dance,
        Jump,
        Swim,
        Smell,
        Scare,
        Fall,
        Hide,
        Talk,
    }

    public enum CharacterKind
    {
        Human,
        Monster,
        Animal,
    }
}
