using System;
using System.Collections.Generic;
using AnimatedDrawingsWorld.Logic.Behavior;

namespace AnimatedDrawingsWorld.Logic.Audio
{
    public struct SoundCue
    {
        public SoundKind Kind;
        public CharacterKind Voice;
        public float Pitch;   // voice pitch multiplier of the character
        public float Pan;     // -1 left .. 1 right
        public float Volume;  // 0..1
        public int Variant;   // which of a few pre-generated variations to use
        public int AgentId;   // 0 for UI sounds

        public override string ToString() => $"{Kind} ({Voice}, agent {AgentId})";
    }

    // Decides which character sounds to play, and — just as important — which not to: sounds
    // fire on changes of activity (never continuously), each character has its own voice pitch,
    // and several limits keep a busy scene from turning into noise:
    //  * one sound per character per AgentGap seconds, the same sound per character only every SameKindGap,
    //  * at most MaxOverlapping sounds at once and a short global gap between them,
    //  * two characters chatting take turns, a dance party gets one jingle, only one sleeper snores.
    // Things the player caused (pick up, drop, commands) are always heard.
    public sealed class SoundDirector
    {
        public float AgentGap = 1.5f;
        public float SameKindGap = 7f;
        public float GlobalGap = 0.35f;
        public int MaxOverlapping = 2;
        public float DanceJingleGap = 10f;
        public float SnoreInterval = 3.2f;

        private sealed class Tracked
        {
            public Activity Activity = Activity.Idle;
            public bool Initialized;
            public bool UserControlled;
            public float LastSound = -100f;
            public readonly Dictionary<SoundKind, float> LastOfKind = new();
        }

        private readonly Dictionary<int, Tracked> tracked = new();
        private readonly List<(float time, SoundCue cue, bool priority)> scheduled = new();
        private readonly List<float> playingUntil = new();
        private float lastGlobal = -100f;
        private float lastJingle = -100f;
        private float nextSnore;
        private int variantCounter;

        public static float VoicePitch(Agent a)
        {
            // stable per character, and smaller drawings get higher voices
            var golden = (a.Id * 0.61803398875f) % 1f;
            var size = MathUtil.Clamp(MathF.Sqrt(1.9f / Math.Max(0.4f, a.BodyHeight)), 0.8f, 1.35f);
            return (0.88f + 0.3f * golden) * size;
        }

        public static float Duration(SoundKind kind) => kind switch
        {
            SoundKind.Babble => 0.8f,
            SoundKind.Yawn => 1.1f,
            SoundKind.Snore => 1.4f,
            SoundKind.Scare => 0.9f,
            SoundKind.Jingle => 0.7f,
            SoundKind.Whoosh or SoundKind.Sweep => 0.7f,
            SoundKind.Splash => 0.6f,
            SoundKind.Click => 0.04f,
            SoundKind.Pop => 0.1f,
            _ => 0.45f,
        };

        public void Update(GameWorld world, List<SoundCue> output)
        {
            var now = world.Time;
            playingUntil.RemoveAll(t => t <= now);

            // forget characters that left
            if (tracked.Count > world.Agents.Count)
            {
                var alive = new HashSet<int>();
                foreach (var a in world.Agents) alive.Add(a.Id);
                var gone = new List<int>();
                foreach (var id in tracked.Keys) if (!alive.Contains(id)) gone.Add(id);
                foreach (var id in gone) tracked.Remove(id);
            }

            foreach (var a in world.Agents)
            {
                if (!tracked.TryGetValue(a.Id, out var t)) tracked[a.Id] = t = new Tracked();
                if (!t.Initialized)
                {
                    t.Initialized = true;
                    t.Activity = a.Activity;
                    t.UserControlled = a.UserControlled;
                    continue;
                }

                var previous = t.Activity;
                var pickedUp = a.UserControlled && !t.UserControlled;
                t.Activity = a.Activity;
                t.UserControlled = a.UserControlled;

                if (pickedUp)
                {
                    Emit(world, a, t, SoundKind.Whee, true, output);
                    continue;
                }
                if (a.Activity == previous) continue;

                var priority = a.FollowingOrders || a.UserControlled;
                switch (a.Activity)
                {
                    case Activity.Talk:
                        // the one who starts the chat speaks now, the partner answers a moment later
                        if (a.Partner != null && tracked.TryGetValue(a.Partner.Id, out var pt) && pt.Activity == Activity.Talk && pt.LastSound > now - 0.5f)
                            Schedule(world, a, SoundKind.Babble, now + 1.0f);
                        else Emit(world, a, t, SoundKind.Babble, priority, output);
                        break;
                    case Activity.Wave: Emit(world, a, t, SoundKind.Hello, priority, output); break;
                    case Activity.Surprised:
                        // landing after being dropped is a thud first
                        if (previous == Activity.Fall) Emit(world, a, t, SoundKind.Thud, true, output);
                        else Emit(world, a, t, SoundKind.Surprise, true, output);
                        break;
                    case Activity.Scare: Emit(world, a, t, SoundKind.Scare, true, output); break;
                    case Activity.Yawn:
                        // yawn out loud when getting sleepy, quietly when waking up
                        if (previous != Activity.Sleep) Emit(world, a, t, SoundKind.Yawn, priority, output);
                        break;
                    case Activity.Jump: Emit(world, a, t, a.Kind == CharacterKind.Animal || a.Partner != null ? SoundKind.Happy : SoundKind.Boing, priority, output); break;
                    case Activity.Swim: Emit(world, a, t, SoundKind.Splash, priority, output); break;
                    case Activity.Smell: Emit(world, a, t, SoundKind.Sniff, priority, output); break;
                    case Activity.Dance:
                        if (now - lastJingle >= DanceJingleGap || priority)
                        {
                            if (Emit(world, a, t, SoundKind.Jingle, priority, output)) lastJingle = now;
                        }
                        break;
                    default:
                        if (previous == Activity.Fall && !a.UserControlled && !a.OffGround) Emit(world, a, t, SoundKind.Thud, true, output);
                        break;
                }
            }

            // replies that were waiting their turn
            for (var i = scheduled.Count - 1; i >= 0; i--)
            {
                if (scheduled[i].time > now) continue;
                var cue = scheduled[i].cue;
                scheduled.RemoveAt(i);
                var agent = world.Agents.Find(x => x.Id == cue.AgentId);
                if (agent == null || agent.Activity != Activity.Talk) continue;
                if (tracked.TryGetValue(agent.Id, out var t)) Emit(world, agent, t, cue.Kind, false, output);
            }

            // a single, quiet snorer
            if (now >= nextSnore)
            {
                Agent sleeper = null;
                foreach (var a in world.Agents)
                    if (a.IsAsleep && (sleeper == null || a.Id < sleeper.Id)) sleeper = a;
                if (sleeper != null && playingUntil.Count == 0)
                {
                    output.Add(MakeCue(world, sleeper, SoundKind.Snore, 0.35f));
                    Track(now, SoundKind.Snore);
                }
                nextSnore = now + SnoreInterval;
            }
        }

        // UI sounds (buttons, a character popping in, a new background) bypass the character limits
        public SoundCue UiCue(SoundKind kind) => new() { Kind = kind, Voice = CharacterKind.Human, Pitch = 1f, Volume = 0.6f, Variant = 0 };

        private bool Emit(GameWorld world, Agent a, Tracked t, SoundKind kind, bool priority, List<SoundCue> output)
        {
            var now = world.Time;
            if (!priority)
            {
                if (now - t.LastSound < AgentGap) return false;
                if (t.LastOfKind.TryGetValue(kind, out var last) && now - last < SameKindGap) return false;
                if (now - lastGlobal < GlobalGap || playingUntil.Count >= MaxOverlapping) return false;
                if (a.Opacity < 0.5f) return false; // inside the house
            }
            t.LastSound = now;
            t.LastOfKind[kind] = now;
            output.Add(MakeCue(world, a, kind, 0.8f));
            Track(now, kind);
            return true;
        }

        private void Track(float now, SoundKind kind)
        {
            lastGlobal = now;
            playingUntil.Add(now + Duration(kind));
        }

        private void Schedule(GameWorld world, Agent a, SoundKind kind, float time)
        {
            scheduled.Add((time, MakeCue(world, a, kind, 0.8f), false));
        }

        private SoundCue MakeCue(GameWorld world, Agent a, SoundKind kind, float volume) => new()
        {
            Kind = kind,
            Voice = a.Kind,
            Pitch = VoicePitch(a),
            Pan = MathUtil.Clamp((a.Feet.X / Math.Max(0.01f, world.Width)) * 2f - 1f, -1f, 1f) * 0.7f,
            Volume = volume,
            Variant = variantCounter++ % 3,
            AgentId = a.Id,
        };
    }
}
