using System;
using System.Collections.Generic;
using AnimatedDrawingsWorld.Logic.Scene;

namespace AnimatedDrawingsWorld.Logic.Behavior
{
    // The simulation: characters living in an analyzed background. World units: the background
    // is `Height` units tall (default 10) and Height * aspect wide; (0,0) = bottom-left, y up.
    // Characters walk in the ground band just below the drawn ground line, use the objects the
    // analyzer found (climb trees, sleep on grass, smell flowers, swim, hide in houses...) and
    // meet each other (talk, dance, wave, scare, chase).
    public sealed class GameWorld
    {
        public readonly float Height;
        public SceneLayout Layout { get; private set; }
        public float Width => Height * Layout.Aspect;
        public float Time { get; private set; }
        public readonly List<Agent> Agents = new();
        public event Action<Agent, string> Log;

        private readonly SeededRandom rng;
        private int nextAgentId = 1;

        public GameWorld(SceneLayout layout, int seed = 1, float height = 10f)
        {
            Layout = layout ?? throw new ArgumentNullException(nameof(layout));
            Height = height;
            rng = new SeededRandom(seed);
        }

        // ------------------------------------------------------------------ geometry

        public float GroundTop(float x) => Layout.GroundAt(x / Width) * Height;

        // how far below the drawn ground line characters may stand (the "depth" of the scene)
        public float BandDepth(float x)
        {
            var top = GroundTop(x);
            return MathUtil.Clamp(top - 0.03f * Height, 0f, 0.28f * Height);
        }

        public float BandBottom(float x) => GroundTop(x) - BandDepth(x);

        public bool IsWater(float x) => Layout.SurfaceAt(x / Width) == SurfaceType.Water;

        // nearer to the viewer (lower on the page) = drawn bigger, like a child's perspective
        public float PerspectiveScale(Agent a)
        {
            if (a.OffGround) return 1f;
            var depth = BandDepth(a.Feet.X);
            if (depth < 1e-3f) return 1f;
            return 1f + 0.3f * MathUtil.Clamp01((GroundTop(a.Feet.X) - a.Feet.Y) / depth);
        }

        public V2 ClampToBand(V2 p, float margin = 0.4f)
        {
            var x = MathUtil.Clamp(p.X, margin, Width - margin);
            var y = MathUtil.Clamp(p.Y, BandBottom(x), GroundTop(x));
            return new V2(x, y);
        }

        public V2 RandomGroundPoint(float? nearX = null, float spread = 0f)
        {
            var x = nearX.HasValue ? nearX.Value + rng.Range(-spread, spread) : rng.Range(0.05f, 0.95f) * Width;
            x = MathUtil.Clamp(x, 0.4f, Width - 0.4f);
            return new V2(x, MathUtil.Lerp(BandBottom(x), GroundTop(x), rng.Value));
        }

        public V2 ToWorld(V2 normalized) => new(normalized.X * Width, normalized.Y * Height);
        public Rect2 ToWorld(Rect2 r) => r.Scaled(Width, Height);

        // ------------------------------------------------------------------ population

        public Agent AddCharacter(string name, CharacterKind kind, float bodyHeight = 0f, V2? feet = null)
        {
            var agent = new Agent
            {
                Id = nextAgentId++,
                Name = name,
                Kind = kind,
                BodyHeight = bodyHeight > 0f ? bodyHeight : Height * 0.2f,
                Energy = rng.Range(0.55f, 1f),
                Sociability = rng.Range(0.3f, 1f),
                Curiosity = rng.Range(0.3f, 1f),
                FacingRight = rng.Chance(0.5f),
            };
            agent.Feet = feet.HasValue ? ClampToBand(feet.Value) : RandomGroundPoint();
            Agents.Add(agent);
            Say(agent, "joined the drawing");
            return agent;
        }

        public void Remove(Agent agent)
        {
            Agents.Remove(agent);
            foreach (var other in Agents)
            {
                if (other.Partner == agent) Release(other);
                if (other.Current?.Other == agent) Interrupt(other);
            }
        }

        // The background was swapped: forget plans that refer to old objects and drop everyone
        // onto the new ground.
        public void SetLayout(SceneLayout layout)
        {
            var oldWidth = Width;
            Layout = layout ?? throw new ArgumentNullException(nameof(layout));
            foreach (var a in Agents)
            {
                Interrupt(a);
                a.ObjectCooldown.Clear();
                a.Opacity = 1f;
                a.OffGround = false;
                var x = a.Feet.X / Math.Max(0.001f, oldWidth) * Width;
                a.Feet = new V2(MathUtil.Clamp(x, 0.4f, Width - 0.4f), a.Feet.Y);
                if (a.Feet.Y > GroundTop(a.Feet.X) + 0.05f) StartFalling(a);
                else a.Feet = ClampToBand(a.Feet);
            }
        }

        // ------------------------------------------------------------------ player commands

        public void Command(Agent a, Activity activity)
        {
            Interrupt(a); // (queues climbing/swimming back down first if needed)
            switch (activity)
            {
                case Activity.Climb when Nearest(a, SceneObjectKind.Tree) is { } tree: PlanClimb(a, tree); break;
                case Activity.Swim when Nearest(a, SceneObjectKind.Water) is { } water: PlanSwim(a, water); break;
                case Activity.Smell when Nearest(a, SceneObjectKind.Flower) is { } flower: PlanFlower(a, flower); break;
                case Activity.Hide when Nearest(a, SceneObjectKind.House) is { } house: PlanHouse(a, house); break;
                case Activity.Sleep: PlanSleep(a, true); break;
                case Activity.Walk:
                case Activity.Run:
                    a.Plan.Enqueue(PlanStep.GoTo(RandomGroundPoint(), Speed(a, activity == Activity.Run), activity));
                    break;
                default:
                    a.Plan.Enqueue(PlanStep.Do(activity, activity == Activity.Surprised ? 1.2f : 4f));
                    break;
            }
            a.Thought = "doing what I'm told: " + activity.ToString().ToLowerInvariant();
            a.FollowingOrders = true;
        }

        public void BeginDrag(Agent a)
        {
            Interrupt(a);
            a.FollowingOrders = false;
            a.UserControlled = true;
            a.Falling = false;
            a.Activity = Activity.Fall;
            a.OffGround = true;
            a.Opacity = 1f;
            a.Thought = "wheee!";
        }

        public void DragTo(Agent a, V2 feet) => a.Feet = new V2(MathUtil.Clamp(feet.X, 0.2f, Width - 0.2f), MathUtil.Clamp(feet.Y, 0f, Height));

        // Dropped by the player: land on what's under the drop point.
        public void EndDrag(Agent a)
        {
            a.UserControlled = false;
            var n = new V2(a.Feet.X / Width, a.Feet.Y / Height);
            foreach (var o in Layout.Objects)
            {
                if (o.Kind == SceneObjectKind.Tree && o.Bounds.Contains(n) && a.Feet.Y > GroundTop(a.Feet.X) + a.BodyHeight * 0.3f)
                {
                    // caught in the branches: sit there, then climb down
                    var tx = (o.Trunk.XMin + o.Trunk.XMax) * 0.5f * Width;
                    var seat = new V2(tx, Math.Min(a.Feet.Y, o.CrownBottom * Height - a.BodyHeight * 0.35f));
                    a.Feet = seat;
                    a.OffGround = true;
                    a.Activity = Activity.Sit;
                    a.Plan.Enqueue(PlanStep.Do(Activity.Sit, rng.Range(2.5f, 4f), offGround: true));
                    a.Plan.Enqueue(PlanStep.MoveTo(new V2(tx, GroundTop(tx)), Speed(a) * 0.8f, Activity.Climb, true));
                    a.Thought = "stuck in a tree!";
                    return;
                }
            }

            if (a.Feet.Y > GroundTop(a.Feet.X) + 0.05f) StartFalling(a);
            else
            {
                a.OffGround = false;
                a.Feet = ClampToBand(a.Feet);
                a.Plan.Enqueue(PlanStep.Do(Activity.Surprised, 0.8f));
            }
        }

        // ------------------------------------------------------------------ simulation

        public void Step(float dt)
        {
            if (dt <= 0f) return;
            dt = Math.Min(dt, 0.1f);
            Time += dt;

            foreach (var a in Agents)
            {
                if (a.EmoteTimer > 0f) a.EmoteTimer -= dt;
                if (a.UserControlled) continue;
                UpdateNeeds(a, dt);

                if (a.Falling)
                {
                    TickFall(a, dt);
                    continue;
                }

                if (a.Current == null)
                {
                    if (a.Plan.Count == 0)
                    {
                        a.FollowingOrders = false;
                        if (a.Partner != null) Release(a);
                        ChooseGoal(a);
                    }
                    if (a.Plan.Count == 0) continue;
                    BeginStep(a, a.Plan.Dequeue());
                }
                TickStep(a, dt);
            }

            ReactToMonsters();
            KeepPersonalSpace(dt);
        }

        private void UpdateNeeds(Agent a, float dt)
        {
            if (a.Activity == Activity.Sleep) a.Energy = Math.Min(1f, a.Energy + dt / 10f);
            else a.Energy = Math.Max(0f, a.Energy - dt / (a.Activity is Activity.Run or Activity.Dance or Activity.Swim ? 45f : 90f));
            a.BodyRotation = a.Activity == Activity.Sleep ? (a.FacingRight ? -90f : 90f) : 0f;
        }

        private void BeginStep(Agent a, PlanStep step)
        {
            a.Current = step;
            a.StepTime = 0f;
            if (step.Thought != null) a.Thought = step.Thought;
            a.ClipOverride = step.Clip;
            a.AnimationSpeed = 1f;
            switch (step.Kind)
            {
                case StepKind.Do:
                    a.Activity = step.Activity;
                    a.OffGround = step.OffGround;
                    break;
                case StepKind.FaceTo:
                    a.FacingRight = step.Target.X >= a.Feet.X;
                    break;
                case StepKind.Fade:
                    a.Activity = step.Activity;
                    break;
            }
        }

        private void TickStep(Agent a, float dt)
        {
            var step = a.Current;
            a.StepTime += dt;
            var done = false;
            switch (step.Kind)
            {
                case StepKind.GoTo:
                {
                    a.OffGround = false;
                    var target = ClampToBand(step.Target);
                    done = MoveOnGround(a, target, step.Speed, step.Activity, dt) || a.StepTime > step.Duration;
                    break;
                }
                case StepKind.Follow:
                {
                    var other = step.Other;
                    if (other == null || !Agents.Contains(other) || !IsAvailable(other, a))
                    {
                        a.Plan.Clear();
                        done = true;
                        break;
                    }
                    var space = PersonalSpace(a, other);
                    var side = a.Feet.X < other.Feet.X ? -1f : 1f;
                    var target = ClampToBand(new V2(other.Feet.X + side * space, other.Feet.Y));
                    var arrived = MoveOnGround(a, target, step.Speed, Activity.Walk, dt);
                    if (arrived || Math.Abs(a.Feet.X - other.Feet.X) < space * 1.1f && Math.Abs(a.Feet.Y - other.Feet.Y) < space) done = true;
                    if (a.StepTime > 12f)
                    {
                        a.Plan.Clear(); // gave up
                        done = true;
                    }
                    break;
                }
                case StepKind.Do:
                    a.Activity = step.Activity;
                    if (step.Activity == Activity.Swim) a.AnimationSpeed = 1f;
                    done = a.StepTime >= step.Duration;
                    break;
                case StepKind.MoveTo:
                {
                    a.OffGround = step.OffGround;
                    a.Activity = step.Activity;
                    var delta = step.Target - a.Feet;
                    var move = step.Speed * dt;
                    if (Math.Abs(delta.X) > 0.02f) a.FacingRight = delta.X > 0f;
                    if (delta.Length <= move)
                    {
                        a.Feet = step.Target;
                        done = true;
                    }
                    else a.Feet += delta.Normalized * move;
                    if (a.StepTime > step.Duration) done = true;
                    break;
                }
                case StepKind.Fade:
                    a.Opacity = MathUtil.Lerp(a.Opacity, step.Value, MathUtil.Clamp01(a.StepTime / Math.Max(0.01f, step.Duration)));
                    done = a.StepTime >= step.Duration;
                    if (done) a.Opacity = step.Value;
                    break;
                case StepKind.Interact:
                    ResolveInteraction(a, step.Other);
                    done = true;
                    break;
                default:
                    done = true;
                    break;
            }

            if (!done) return;
            a.Current = null;
            step.OnDone?.Invoke(a);
            if (a.Plan.Count == 0 && a.Activity != Activity.Fall)
            {
                // settle back into standing unless we're still up somewhere
                if (!a.OffGround) a.Activity = Activity.Idle;
            }
        }

        // walks toward target inside the ground band; returns true on arrival
        private bool MoveOnGround(Agent a, V2 target, float speed, Activity activity, float dt)
        {
            var delta = target - a.Feet;
            var dist = delta.Length;
            var swimming = IsWater(a.Feet.X);
            a.Activity = swimming ? Activity.Swim : activity;
            if (swimming) speed *= 0.6f;
            if (Math.Abs(delta.X) > 0.02f) a.FacingRight = delta.X > 0f;
            var step = speed * dt;
            if (dist <= step || dist < 0.03f)
            {
                a.Feet = target;
                return true;
            }
            a.Feet = ClampToBand(a.Feet + delta / dist * step, 0.2f);
            // walking speed is expressed in body heights so tiny and huge drawings look alike
            a.AnimationSpeed = activity == Activity.Run ? 1f : MathUtil.Clamp(speed / Math.Max(0.1f, a.BodyHeight * 0.55f), 0.6f, 1.6f);
            return false;
        }

        private void StartFalling(Agent a)
        {
            Interrupt(a);
            a.Activity = Activity.Fall;
            a.OffGround = true;
            a.Falling = true;
            a.FallVelocity = 0f;
            a.Thought = "falling!";
        }

        private void TickFall(Agent a, float dt)
        {
            a.FallVelocity += 25f * dt * (Height / 10f);
            var y = a.Feet.Y - a.FallVelocity * dt;
            var ground = GroundTop(a.Feet.X);
            if (y <= ground)
            {
                a.Feet = ClampToBand(new V2(a.Feet.X, ground));
                a.OffGround = false;
                a.Falling = false;
                a.Activity = Activity.Idle;
                a.FallVelocity = 0f;
                a.Plan.Enqueue(PlanStep.Do(IsWater(a.Feet.X) ? Activity.Swim : Activity.Surprised, 1f));
                Emote(a, "!");
                return;
            }
            a.Feet = new V2(a.Feet.X, y);
        }

        public void Interrupt(Agent a)
        {
            a.Plan.Clear();
            a.Current = null;
            if (a.Partner != null) Release(a);
            if (a.Activity == Activity.Hide || a.Opacity < 1f) a.Opacity = 1f;
            if (a.OffGround && !a.UserControlled && !a.Falling)
            {
                // anyone interrupted while up a tree / on a rock / out in the water gets back to
                // the ground first (climbing or swimming)
                var target = ClampToBand(a.Feet);
                if (V2.Distance(a.Feet, target) > 0.05f)
                    a.Plan.Enqueue(PlanStep.MoveTo(target, Speed(a), a.Activity == Activity.Swim ? Activity.Swim : Activity.Climb, true));
                a.Plan.Enqueue(new PlanStep { Kind = StepKind.Custom, OnDone = x => x.OffGround = false });
            }
        }

        private void Release(Agent a)
        {
            var p = a.Partner;
            a.Partner = null;
            if (p != null && p.Partner == a) p.Partner = null;
        }

        // ------------------------------------------------------------------ choosing what to do

        private readonly List<(float weight, Action build)> options = new();

        private void ChooseGoal(Agent a)
        {
            options.Clear();
            a.TargetObject = null;

            if (a.Energy < 0.3f) options.Add((6f, () => PlanSleep(a, false)));

            foreach (var o in Layout.Objects)
            {
                if (a.ObjectCooldown.TryGetValue(o.Id, out var until) && Time < until) continue;
                var center = ToWorld(o.Bounds.Center);
                var distance = Math.Abs(center.X - a.Feet.X) / Math.Max(1f, Width);
                var near = 1f / (1f + distance * 2.5f);
                foreach (var (activity, weight) in Affordances.For(o.Kind))
                {
                    if (!CanDo(a, activity)) continue;
                    if (activity == Activity.Sleep && a.Energy > 0.6f) continue;
                    var w = weight * (0.5f + a.Curiosity) * near * o.Confidence;
                    var obj = o;
                    var act = activity;
                    options.Add((w, () => PlanObject(a, obj, act)));
                }
            }

            foreach (var other in Agents)
            {
                if (other == a || !IsAvailable(other, a)) continue;
                if (a.AgentCooldown.TryGetValue(other.Id, out var until) && Time < until) continue;
                var distance = Math.Abs(other.Feet.X - a.Feet.X) / Math.Max(1f, Width);
                var w = 1.3f * a.Sociability / (1f + distance * 2f);
                var o = other;
                options.Add((w, () => PlanApproach(a, o)));
            }

            options.Add((0.5f, () =>
            {
                a.Plan.Enqueue(new PlanStep { Kind = StepKind.GoTo, Target = RandomGroundPoint(), Speed = Speed(a), Activity = Activity.Walk, Duration = 20f, Thought = "going for a walk" });
                a.Plan.Enqueue(PlanStep.Do(Activity.Idle, rng.Range(0.8f, 2.5f)));
            }));
            options.Add((0.3f, () => a.Plan.Enqueue(new PlanStep { Kind = StepKind.Do, Activity = Activity.Idle, Duration = rng.Range(1.5f, 3.5f), Thought = "thinking..." })));
            options.Add((0.12f, () => a.Plan.Enqueue(new PlanStep { Kind = StepKind.Do, Activity = Activity.Dance, Duration = rng.Range(3f, 5f), Clip = PickDance(), Thought = "dancing for fun" })));

            var total = 0f;
            foreach (var (w, _) in options) total += w;
            var pick = rng.Value * total;
            foreach (var (w, build) in options)
            {
                pick -= w;
                if (pick > 0f) continue;
                build();
                return;
            }
        }

        private static bool CanDo(Agent a, Activity activity) => a.Kind switch
        {
            // animals don't wave at the sun or hide in houses
            CharacterKind.Animal => activity is not (Activity.Wave or Activity.Hide or Activity.Climb),
            _ => true,
        };

        private bool IsAvailable(Agent other, Agent asker) =>
            !other.UserControlled && !other.FollowingOrders && other.Partner == null && other.Opacity > 0.9f && !other.OffGround &&
            other.Activity != Activity.Fall && other.Activity != Activity.Hide &&
            (other.Current == null || other.Current.Kind != StepKind.Follow);

        private float Speed(Agent a, bool run = false) => a.BodyHeight * (run ? 1.3f : 0.55f) * (a.Kind == CharacterKind.Animal ? 1.2f : 1f);

        private float PersonalSpace(Agent a, Agent b) => 0.42f * (a.BodyHeight + b.BodyHeight) * 0.5f + 0.25f;

        private string PickDance() => MotionDance[rng.Range(0, MotionDance.Length)];
        private static readonly string[] MotionDance = { "jesse_dance", "dab", "jumping_jacks" };

        private SceneObject Nearest(Agent a, SceneObjectKind kind)
        {
            SceneObject best = null;
            var bestD = float.MaxValue;
            foreach (var o in Layout.OfKind(kind))
            {
                var d = Math.Abs(ToWorld(o.Bounds.Center).X - a.Feet.X);
                if (d >= bestD) continue;
                bestD = d;
                best = o;
            }
            return best;
        }

        private void Cooldown(Agent a, SceneObject o, float seconds = 25f) => a.ObjectCooldown[o.Id] = Time + seconds;

        private void PlanObject(Agent a, SceneObject o, Activity activity)
        {
            a.TargetObject = o;
            switch (o.Kind)
            {
                case SceneObjectKind.Tree when activity == Activity.Climb: PlanClimb(a, o); break;
                case SceneObjectKind.Grass when activity == Activity.Sleep: PlanSleep(a, false, o); break;
                case SceneObjectKind.Flower: PlanFlower(a, o); break;
                case SceneObjectKind.House when activity == Activity.Hide: PlanHouse(a, o); break;
                case SceneObjectKind.Bush when activity == Activity.Hide: PlanHouse(a, o); break;
                case SceneObjectKind.Water: PlanSwim(a, o); break;
                case SceneObjectKind.Rock when activity == Activity.Sit:
                case SceneObjectKind.Shape when activity == Activity.Sit:
                    PlanSitOn(a, o); break;
                default: PlanVisit(a, o, activity); break;
            }
            Cooldown(a, o);
        }

        private void PlanClimb(Agent a, SceneObject tree)
        {
            var tx = (tree.Trunk.XMin + tree.Trunk.XMax) * 0.5f * Width;
            if (tree.Trunk.Width <= 0f) tx = tree.Bounds.Center.X * Width;
            var baseY = GroundTop(tx);
            var top = Math.Max(baseY, tree.CrownBottom * Height - a.BodyHeight * 0.35f);
            if (top - baseY < a.BodyHeight * 0.3f)
            {
                PlanVisit(a, tree, Activity.Sit);
                return;
            }
            a.Plan.Enqueue(new PlanStep { Kind = StepKind.GoTo, Target = new V2(tx, baseY), Speed = Speed(a), Activity = Activity.Walk, Duration = 25f, Thought = $"wants to climb the {tree.Label ?? "tree"}" });
            a.Plan.Enqueue(PlanStep.MoveTo(new V2(tx, top), a.BodyHeight * 0.45f, Activity.Climb, true));
            a.Plan.Enqueue(new PlanStep { Kind = StepKind.Do, Activity = Activity.Sit, Duration = rng.Range(3f, 5f), OffGround = true, Thought = "sitting on a branch" });
            a.Plan.Enqueue(PlanStep.MoveTo(new V2(tx, baseY), a.BodyHeight * 0.6f, Activity.Climb, true));
            a.Plan.Enqueue(new PlanStep { Kind = StepKind.Custom, OnDone = x => x.OffGround = false });
        }

        private void PlanSleep(Agent a, bool here, SceneObject grass = null)
        {
            var spot = a.Feet;
            if (!here)
            {
                // a comfy spot on the grass if there is any
                var candidates = new List<float>();
                for (var i = 0; i < Layout.Surface.Length; i++)
                    if (Layout.Surface[i] == SurfaceType.Grass) candidates.Add((i + 0.5f) / Layout.Surface.Length * Width);
                if (candidates.Count > 0) spot = RandomGroundPoint(candidates[rng.Range(0, candidates.Count)], 0.3f);
            }
            // lying down takes a body length of room: keep it on screen
            var room = a.BodyHeight * 0.7f;
            spot = ClampToBand(new V2(MathUtil.Clamp(spot.X, room, Width - room), spot.Y));
            a.Plan.Enqueue(new PlanStep { Kind = StepKind.GoTo, Target = spot, Speed = Speed(a) * 0.8f, Activity = Activity.Walk, Duration = 25f, Thought = "getting sleepy..." });
            a.Plan.Enqueue(PlanStep.Do(Activity.Yawn, 2.4f));
            a.Plan.Enqueue(new PlanStep { Kind = StepKind.Do, Activity = Activity.Sleep, Duration = rng.Range(7f, 12f), Thought = grass != null ? "napping on the grass" : "sleeping" });
            a.Plan.Enqueue(PlanStep.Do(Activity.Yawn, 2.0f));
        }

        private void PlanFlower(Agent a, SceneObject flower)
        {
            var c = ToWorld(flower.Bounds.Center);
            var side = a.Feet.X < c.X ? -1f : 1f;
            var stand = new V2(c.X + side * a.BodyHeight * 0.25f, ToWorld(new V2(0, flower.Bounds.YMin)).Y);
            a.Plan.Enqueue(new PlanStep { Kind = StepKind.GoTo, Target = stand, Speed = Speed(a), Activity = Activity.Walk, Duration = 25f, Thought = $"going to smell the {flower.Label ?? "flower"}" });
            a.Plan.Enqueue(new PlanStep { Kind = StepKind.FaceTo, Target = c });
            a.Plan.Enqueue(new PlanStep { Kind = StepKind.Do, Activity = Activity.Smell, Duration = rng.Range(2.5f, 4f), Thought = "mmm, flowers" });
        }

        private void PlanHouse(Agent a, SceneObject house)
        {
            var door = new V2(house.Bounds.Center.X * Width, GroundTop(house.Bounds.Center.X * Width));
            var hidden = house.Kind == SceneObjectKind.House ? 0f : 0.35f;
            a.Plan.Enqueue(new PlanStep { Kind = StepKind.GoTo, Target = door, Speed = Speed(a), Activity = Activity.Walk, Duration = 25f, Thought = house.Kind == SceneObjectKind.House ? "going home" : "hiding in the bush" });
            a.Plan.Enqueue(new PlanStep { Kind = StepKind.Fade, Value = hidden, Duration = 0.6f, Activity = Activity.Hide });
            a.Plan.Enqueue(new PlanStep { Kind = StepKind.Do, Activity = Activity.Hide, Duration = rng.Range(3f, 6f), Thought = house.Kind == SceneObjectKind.House ? "inside the house" : "hiding!" });
            a.Plan.Enqueue(new PlanStep { Kind = StepKind.Fade, Value = 1f, Duration = 0.6f });
            a.Plan.Enqueue(PlanStep.Do(Activity.Wave, 1.6f));
        }

        private void PlanSwim(Agent a, SceneObject water)
        {
            var b = ToWorld(water.Bounds);
            // water that is part of the ground profile (sea, river at the bottom): walk in and swim
            var shoreline = -1f;
            for (var i = 0; i < Layout.Surface.Length; i++)
            {
                var x = (i + 0.5f) / Layout.Surface.Length * Width;
                if (Layout.Surface[i] == SurfaceType.Water && x >= b.XMin && x <= b.XMax) { shoreline = x; break; }
            }

            if (shoreline >= 0f)
            {
                var spot = RandomGroundPoint(MathUtil.Clamp(rng.Range(b.XMin, b.XMax), b.XMin + 0.3f, b.XMax - 0.3f), 0f);
                a.Plan.Enqueue(new PlanStep { Kind = StepKind.GoTo, Target = spot, Speed = Speed(a), Activity = Activity.Walk, Duration = 25f, Thought = "going for a swim" });
                a.Plan.Enqueue(PlanStep.Do(Activity.Swim, rng.Range(3f, 5f)));
                return;
            }

            // a pond inside the ground band, or a sea/lake drawn behind the ground line: go to the
            // shore, swim out to the middle of the water (off the walking band) and come back
            var c = b.Center;
            var inside = new V2(c.X, MathUtil.Clamp(c.Y, b.YMin + b.Height * 0.15f, b.YMax - Math.Min(b.Height * 0.3f, a.BodyHeight * 0.2f)));
            V2 shore;
            if (b.YMin >= GroundTop(c.X) - a.BodyHeight * 0.3f)
                shore = new V2(c.X + rng.Range(-0.3f, 0.3f) * b.Width, 0f);
            else
                shore = new V2(a.Feet.X < c.X ? b.XMin - a.BodyHeight * 0.2f : b.XMax + a.BodyHeight * 0.2f, inside.Y);
            shore = ClampToBand(new V2(shore.X, shore.Y <= 0f ? GroundTop(shore.X) : shore.Y));
            a.Plan.Enqueue(new PlanStep { Kind = StepKind.GoTo, Target = shore, Speed = Speed(a), Activity = Activity.Walk, Duration = 25f, Thought = $"jumping into the {water.Label ?? "water"}" });
            a.Plan.Enqueue(PlanStep.MoveTo(inside, Speed(a) * 0.5f, Activity.Swim, true));
            a.Plan.Enqueue(new PlanStep { Kind = StepKind.Do, Activity = Activity.Swim, Duration = rng.Range(3f, 5f), OffGround = true, Thought = "splash splash" });
            a.Plan.Enqueue(PlanStep.MoveTo(shore, Speed(a) * 0.5f, Activity.Swim, true));
            a.Plan.Enqueue(new PlanStep { Kind = StepKind.Custom, OnDone = x => x.OffGround = false });
            a.Plan.Enqueue(PlanStep.Do(Activity.Jump, 1.2f));
        }

        private void PlanSitOn(Agent a, SceneObject o)
        {
            var b = ToWorld(o.Bounds);
            var height = b.Height;
            if (height > a.BodyHeight * 0.75f)
            {
                PlanVisit(a, o, Activity.Sit);
                return;
            }
            var side = a.Feet.X < b.Center.X ? b.XMin - 0.2f : b.XMax + 0.2f;
            var foot = new V2(side, GroundTop(side));
            var seat = new V2(b.Center.X, b.YMax - height * 0.1f);
            a.Plan.Enqueue(new PlanStep { Kind = StepKind.GoTo, Target = foot, Speed = Speed(a), Activity = Activity.Walk, Duration = 25f, Thought = $"going to sit on the {o.Label ?? "rock"}" });
            a.Plan.Enqueue(PlanStep.MoveTo(seat, a.BodyHeight * 0.8f, Activity.Climb, true));
            a.Plan.Enqueue(new PlanStep { Kind = StepKind.Do, Activity = Activity.Sit, Duration = rng.Range(3f, 5f), OffGround = true, Thought = "resting" });
            a.Plan.Enqueue(PlanStep.MoveTo(foot, a.BodyHeight * 0.8f, Activity.Jump, true));
            a.Plan.Enqueue(new PlanStep { Kind = StepKind.Custom, OnDone = x => x.OffGround = false });
        }

        // walk near the object and do the activity there (wave at the sun, sit in the shade...)
        private void PlanVisit(Agent a, SceneObject o, Activity activity)
        {
            var b = ToWorld(o.Bounds);
            var x = MathUtil.Clamp(rng.Range(b.XMin, b.XMax), 0.5f, Width - 0.5f);
            if (o.Kind == SceneObjectKind.Tree) x = b.Center.X + (rng.Chance(0.5f) ? -1f : 1f) * Math.Min(b.Width * 0.5f, a.BodyHeight * 0.5f);
            var why = activity switch
            {
                Activity.Wave => $"waving at the {o.Label}",
                Activity.Dance => o.Kind == SceneObjectKind.Sun ? "sunshine dance!" : "dancing",
                Activity.Jump => $"trying to reach the {o.Label}",
                Activity.Sit => o.Kind == SceneObjectKind.Tree ? "resting in the shade" : $"sitting by the {o.Label}",
                _ => $"visiting the {o.Label}",
            };
            a.Plan.Enqueue(new PlanStep { Kind = StepKind.GoTo, Target = RandomGroundPoint(x, 0f), Speed = Speed(a), Activity = Activity.Walk, Duration = 25f, Thought = why });
            a.Plan.Enqueue(new PlanStep { Kind = StepKind.FaceTo, Target = b.Center });
            a.Plan.Enqueue(new PlanStep { Kind = StepKind.Do, Activity = activity, Duration = rng.Range(3f, 5f), Clip = activity == Activity.Dance ? PickDance() : null });
        }

        // ------------------------------------------------------------------ social

        private void PlanApproach(Agent a, Agent other)
        {
            a.Plan.Enqueue(new PlanStep { Kind = StepKind.Follow, Other = other, Speed = Speed(a), Thought = $"going to see {other.Name}" });
            a.Plan.Enqueue(new PlanStep { Kind = StepKind.Interact, Other = other });
            a.AgentCooldown[other.Id] = Time + 20f;
        }

        private void ResolveInteraction(Agent a, Agent b)
        {
            if (b == null || !Agents.Contains(b)) return;
            a.AgentCooldown[b.Id] = Time + 25f;
            b.AgentCooldown[a.Id] = Time + 25f;

            if (b.IsAsleep)
            {
                Queue(a, new PlanStep { Kind = StepKind.FaceTo, Target = b.Feet }, new PlanStep { Kind = StepKind.Do, Activity = Activity.Wave, Duration = 1.5f, Thought = $"waking up {b.Name}" });
                if (rng.Chance(0.5f))
                {
                    Interrupt(b);
                    b.Energy = Math.Max(b.Energy, 0.5f);
                    Queue(b, PlanStep.Do(Activity.Yawn, 2.4f), new PlanStep { Kind = StepKind.Do, Activity = Activity.Talk, Duration = 1.5f, Thought = "five more minutes..." });
                }
                Say(a, $"tried to wake {b.Name}");
                return;
            }

            if (!IsAvailable(b, a) || b.Current != null && b.Current.Kind == StepKind.Do && b.Current.Activity is Activity.Sleep or Activity.Climb) return;

            Interrupt(b);
            a.Partner = b;
            b.Partner = a;
            var faceB = new PlanStep { Kind = StepKind.FaceTo, Target = b.Feet };
            var faceA = new PlanStep { Kind = StepKind.FaceTo, Target = a.Feet };

            var aScary = a.Kind == CharacterKind.Monster;
            var bScary = b.Kind == CharacterKind.Monster;

            if (aScary && !bScary || bScary && !aScary && rng.Chance(0.5f))
            {
                // monster meets someone: roar! they flee, sometimes the monster chases them
                var monster = aScary ? a : b;
                var victim = aScary ? b : a;
                var away = RandomGroundPoint(victim.Feet.X + (victim.Feet.X < monster.Feet.X ? -1f : 1f) * Width * 0.35f, Width * 0.05f);
                Queue(monster, monster == a ? faceB : faceA, new PlanStep { Kind = StepKind.Do, Activity = Activity.Scare, Duration = 1.8f, Thought = "RAAAR!" });
                Queue(victim, victim == a ? faceB : faceA, new PlanStep { Kind = StepKind.Do, Activity = Activity.Surprised, Duration = 1.0f, Thought = $"eek, {monster.Name}!" },
                    PlanStep.GoTo(away, Speed(victim, true), Activity.Run));
                if (rng.Chance(0.5f)) monster.Plan.Enqueue(PlanStep.GoTo(away, Speed(monster, true) * 0.8f, Activity.Run));
                else monster.Plan.Enqueue(new PlanStep { Kind = StepKind.Do, Activity = Activity.Dance, Duration = 2.5f, Clip = "dab", Thought = "that was fun" });
                Emote(victim, "!");
                Say(monster, $"scared {victim.Name}");
                return;
            }

            if (aScary && bScary || a.Kind == CharacterKind.Monster && b.Kind == CharacterKind.Animal)
            {
                // two monsters throw a dance party
                var dance = PickDance();
                Queue(a, faceB, new PlanStep { Kind = StepKind.Do, Activity = Activity.Dance, Duration = 5f, Clip = dance, Thought = "monster party!" });
                Queue(b, faceA, new PlanStep { Kind = StepKind.Do, Activity = Activity.Dance, Duration = 5f, Clip = OtherDance(dance), Thought = "monster party!" });
                Emote(a, "♪");
                Emote(b, "♪");
                Say(a, $"is dancing with {b.Name}");
                return;
            }

            if (a.Kind == CharacterKind.Animal || b.Kind == CharacterKind.Animal)
            {
                var animal = a.Kind == CharacterKind.Animal ? a : b;
                var friend = animal == a ? b : a;
                Queue(friend, friend == a ? faceB : faceA, new PlanStep { Kind = StepKind.Do, Activity = Activity.Talk, Duration = 2.5f, Thought = $"petting {animal.Name}" });
                Queue(animal, animal == a ? faceB : faceA, new PlanStep { Kind = StepKind.Do, Activity = Activity.Jump, Duration = 2.5f, Thought = "happy!" });
                Emote(animal, "♥");
                Say(friend, $"is petting {animal.Name}");
                return;
            }

            // two friends: chat, then dance together, high-five (jump) or wave goodbye
            Queue(a, faceB, new PlanStep { Kind = StepKind.Do, Activity = Activity.Talk, Duration = 2.5f, Thought = $"chatting with {b.Name}" });
            Queue(b, faceA, new PlanStep { Kind = StepKind.Do, Activity = Activity.Talk, Duration = 2.5f, Thought = $"chatting with {a.Name}" });
            var roll = rng.Value;
            if (roll < 0.45f)
            {
                var dance = PickDance();
                Queue(a, new PlanStep { Kind = StepKind.Do, Activity = Activity.Dance, Duration = 5f, Clip = dance, Thought = $"dancing with {b.Name}" });
                Queue(b, new PlanStep { Kind = StepKind.Do, Activity = Activity.Dance, Duration = 5f, Clip = dance, Thought = $"dancing with {a.Name}" });
                Say(a, $"dances with {b.Name}");
            }
            else if (roll < 0.7f)
            {
                Queue(a, new PlanStep { Kind = StepKind.Do, Activity = Activity.Jump, Duration = 2f, Thought = "high five!" });
                Queue(b, new PlanStep { Kind = StepKind.Do, Activity = Activity.Jump, Duration = 2f, Thought = "high five!" });
                Say(a, $"high-fives {b.Name}");
            }
            else
            {
                Queue(a, PlanStep.Do(Activity.Wave, 1.6f));
                Queue(b, PlanStep.Do(Activity.Wave, 1.6f));
                Say(a, $"waves goodbye to {b.Name}");
            }
            Emote(a, "♥");
        }

        private static string OtherDance(string dance) => dance == "jesse_dance" ? "dab" : "jesse_dance";

        private static void Queue(Agent a, params PlanStep[] steps)
        {
            foreach (var s in steps) a.Plan.Enqueue(s);
        }

        private void Emote(Agent a, string emote)
        {
            a.Emote = emote;
            a.EmoteTimer = 1.6f;
        }

        private void Say(Agent a, string what) => Log?.Invoke(a, $"{a.Name} {what}");

        // humans passing close to a monster get a fright even without a planned meeting
        private void ReactToMonsters()
        {
            foreach (var h in Agents)
            {
                if (h.Kind == CharacterKind.Monster || h.UserControlled || h.FollowingOrders || h.OffGround || h.IsAsleep || h.Partner != null) continue;
                if (h.ScareCooldown > Time || h.Opacity < 0.9f) continue;
                if (h.Current != null && h.Current.Kind == StepKind.GoTo && h.Current.Activity == Activity.Run) continue;
                foreach (var m in Agents)
                {
                    if (m.Kind != CharacterKind.Monster || m.IsAsleep || m.OffGround || m.Opacity < 0.9f || m.UserControlled) continue;
                    var reach = (m.BodyHeight + h.BodyHeight) * 0.35f;
                    if (Math.Abs(m.Feet.X - h.Feet.X) > reach || Math.Abs(m.Feet.Y - h.Feet.Y) > reach) continue;
                    h.ScareCooldown = Time + rng.Range(20f, 35f);
                    if (rng.Chance(0.4f)) break; // didn't notice this time
                    Interrupt(h);
                    var away = RandomGroundPoint(h.Feet.X + (h.Feet.X < m.Feet.X ? -1f : 1f) * Width * 0.3f, Width * 0.05f);
                    Queue(h, new PlanStep { Kind = StepKind.FaceTo, Target = m.Feet }, new PlanStep { Kind = StepKind.Do, Activity = Activity.Surprised, Duration = 0.9f, Thought = $"a {m.Name}!" }, PlanStep.GoTo(away, Speed(h, true), Activity.Run));
                    Emote(h, "!");
                    Say(h, $"got scared by {m.Name}");
                    break;
                }
            }
        }

        // characters standing around shouldn't be drawn exactly on top of each other
        private void KeepPersonalSpace(float dt)
        {
            for (var i = 0; i < Agents.Count; i++)
            for (var j = i + 1; j < Agents.Count; j++)
            {
                var a = Agents[i];
                var b = Agents[j];
                if (a.OffGround || b.OffGround || a.UserControlled || b.UserControlled || a.Partner == b) continue;
                var minDx = PersonalSpace(a, b) * 0.6f;
                var dx = b.Feet.X - a.Feet.X;
                if (Math.Abs(dx) >= minDx || Math.Abs(b.Feet.Y - a.Feet.Y) > minDx * 0.5f) continue;
                var push = (minDx - Math.Abs(dx)) * Math.Min(1f, dt * 2f) * (dx >= 0f ? 1f : -1f);
                if (a.Current?.Kind != StepKind.GoTo) a.Feet = ClampToBand(new V2(a.Feet.X - push * 0.5f, a.Feet.Y), 0.2f);
                if (b.Current?.Kind != StepKind.GoTo) b.Feet = ClampToBand(new V2(b.Feet.X + push * 0.5f, b.Feet.Y), 0.2f);
            }
        }
    }
}
