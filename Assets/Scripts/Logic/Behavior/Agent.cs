using System.Collections.Generic;
using AnimatedDrawingsWorld.Logic.Scene;

namespace AnimatedDrawingsWorld.Logic.Behavior
{
    // One character living in the drawing. Pure simulation state — the Unity layer (and the
    // headless test renderer) read it every frame to pose and place the drawing.
    public sealed class Agent
    {
        public int Id;
        public string Name;
        public CharacterKind Kind;
        public float BodyHeight = 1.8f;  // standing height in world units (before perspective)

        // feet position in world units (y up). Standing on the ground: Y is inside the ground band.
        public V2 Feet;
        public bool FacingRight = true;
        public Activity Activity = Activity.Idle;
        public string ClipOverride;       // e.g. a specific dance
        public float AnimationSpeed = 1f;
        public float BodyRotation;        // degrees CCW; 90 = lying down with the head to the left
        public float Opacity = 1f;
        public bool OffGround;            // climbing, sitting on something, falling
        public string Thought = "";       // what they are up to, shown in a bubble
        public float EmoteTimer;          // >0 while a reaction ("!", "Zzz", "♪") should show
        public string Emote = "";

        // needs, 0..1
        public float Energy = 1f;
        public float Sociability = 0.5f;
        public float Curiosity = 0.5f;

        // --- planning (managed by GameWorld)
        internal readonly Queue<PlanStep> Plan = new();
        internal PlanStep Current;
        internal float StepTime;
        internal Agent Partner;
        internal SceneObject TargetObject;
        internal readonly Dictionary<int, float> ObjectCooldown = new();
        internal readonly Dictionary<int, float> AgentCooldown = new();
        internal float FallVelocity;
        internal bool Falling;
        public bool FollowingOrders;      // doing what the player asked; not to be interrupted by others
        internal float ScareCooldown;
        public bool UserControlled;       // true while the player is dragging this character

        public bool IsBusyWithPartner => Partner != null;
        public bool IsAsleep => Activity == Activity.Sleep;

        public override string ToString() => $"{Name} ({Kind}) {Activity} at {Feet}";
    }

    internal enum StepKind
    {
        GoTo,       // walk/run to a point in the ground band
        Follow,     // walk toward another agent
        Do,         // perform an activity for a duration
        MoveTo,     // move (climb / step / swim) in a straight line while doing an activity
        FaceTo,
        Fade,       // fade opacity (entering/leaving a house)
        Interact,   // resolve a social interaction on arrival
        Custom,
    }

    internal sealed class PlanStep
    {
        public StepKind Kind;
        public V2 Target;
        public Activity Activity = Activity.Idle;
        public float Duration;
        public float Speed;
        public string Clip;
        public string Thought;
        public bool OffGround;
        public float Value;
        public Agent Other;
        public System.Action<Agent> OnDone;

        public static PlanStep GoTo(V2 target, float speed, Activity activity = Activity.Walk) =>
            new() { Kind = StepKind.GoTo, Target = target, Speed = speed, Activity = activity, Duration = 25f };

        public static PlanStep Do(Activity activity, float duration, string clip = null, bool offGround = false) =>
            new() { Kind = StepKind.Do, Activity = activity, Duration = duration, Clip = clip, OffGround = offGround };

        public static PlanStep MoveTo(V2 target, float speed, Activity activity, bool offGround) =>
            new() { Kind = StepKind.MoveTo, Target = target, Speed = speed, Activity = activity, OffGround = offGround, Duration = 30f };
    }
}
