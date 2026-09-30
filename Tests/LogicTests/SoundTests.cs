using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AnimatedDrawingsWorld.Logic;
using AnimatedDrawingsWorld.Logic.Audio;
using AnimatedDrawingsWorld.Logic.Behavior;
using AnimatedDrawingsWorld.Logic.Scene;
using Xunit;
using Xunit.Abstractions;

namespace AnimatedDrawingsWorld.Tests
{
    public class SoundTests
    {
        private readonly ITestOutputHelper output;
        public SoundTests(ITestOutputHelper output) => this.output = output;

        [Fact]
        public void EverySoundIsWellFormed()
        {
            var dir = Path.Combine(TestSupport.OutputDir, "sounds");
            Directory.CreateDirectory(dir);
            foreach (SoundKind kind in Enum.GetValues(typeof(SoundKind)))
            foreach (CharacterKind voice in Enum.GetValues(typeof(CharacterKind)))
            {
                var s = SoundSynth.Generate(kind, voice, 1f, 7);
                var seconds = s.Length / (float)SoundSynth.SampleRate;
                Assert.InRange(seconds, 0.03f, 2f);
                Assert.All(s, v => Assert.True(float.IsFinite(v) && Math.Abs(v) <= 1f));
                Assert.True(s.Max(Math.Abs) > 0.3f, $"{kind}/{voice} is silent");
                WriteWav(Path.Combine(dir, $"{kind}_{voice}.wav"), s);
            }
        }

        [Fact]
        public void MonstersSoundLowerThanChildren()
        {
            float Pitch(CharacterKind voice) => DominantFrequency(SoundSynth.Generate(SoundKind.Babble, voice, 1f, 3));
            var human = Pitch(CharacterKind.Human);
            var monster = Pitch(CharacterKind.Monster);
            output.WriteLine($"dominant low frequency: human {human:0} Hz, monster {monster:0} Hz");
            Assert.True(monster < human * 0.7f);
            // each character has its own pitch, and small drawings sound higher
            var small = new Agent { Id = 1, BodyHeight = 1.0f };
            var big = new Agent { Id = 1, BodyHeight = 2.6f };
            Assert.True(SoundDirector.VoicePitch(small) > SoundDirector.VoicePitch(big));
        }

        [Fact]
        public void BusySceneStaysCalm()
        {
            var layout = BackgroundAnalyzer.Analyze(TestSupport.Load(Path.Combine(TestSupport.Backgrounds, "meadow_trees_sun.png")));
            var world = new GameWorld(layout, 11);
            world.AddCharacter("Boy", CharacterKind.Human, 1.9f);
            world.AddCharacter("Girl", CharacterKind.Human, 1.8f);
            world.AddCharacter("Candy", CharacterKind.Monster, 1.7f);
            world.AddCharacter("Garlic", CharacterKind.Monster, 1.6f);
            world.AddCharacter("Piggy", CharacterKind.Animal, 1.1f);

            var director = new SoundDirector();
            var cues = new List<(float time, SoundCue cue)>();
            var frame = new List<SoundCue>();
            const float seconds = 240f;
            for (var i = 0; i < seconds * 30; i++)
            {
                world.Step(1f / 30f);
                frame.Clear();
                director.Update(world, frame);
                foreach (var c in frame) cues.Add((world.Time, c));
            }

            var perMinute = cues.Count / (seconds / 60f);
            output.WriteLine($"{cues.Count} sounds in {seconds}s = {perMinute:0.0}/min");
            foreach (var g in cues.GroupBy(c => (c.cue.Kind, c.cue.Voice)).OrderByDescending(g => g.Count()))
                output.WriteLine($"  {g.Key.Kind} ({g.Key.Voice}): {g.Count()}");

            Assert.InRange(perMinute, 3f, 45f);
            // never a pile-up: at most 3 sounds start within any half second
            for (var i = 0; i < cues.Count; i++)
                Assert.True(cues.Count(c => c.time >= cues[i].time && c.time < cues[i].time + 0.5f) <= 3, $"pile-up at {cues[i].time:0.0}s");
            // the right voices make the right noises
            Assert.Contains(cues, c => c.cue.Kind == SoundKind.Scare && c.cue.Voice == CharacterKind.Monster);
            Assert.Contains(cues, c => c.cue.Kind == SoundKind.Babble && c.cue.Voice == CharacterKind.Human);
            Assert.True(cues.Select(c => c.cue.Kind).Distinct().Count() >= 5);
        }

        [Fact]
        public void PickingUpAndDroppingIsAlwaysHeard()
        {
            var layout = BackgroundAnalyzer.Analyze(TestSupport.Load(Path.Combine(TestSupport.Backgrounds, "meadow_trees_sun.png")));
            var world = new GameWorld(layout, 2);
            var boy = world.AddCharacter("Boy", CharacterKind.Human, 1.9f);
            var director = new SoundDirector();
            var heard = new List<SoundCue>();
            void Tick(int frames)
            {
                for (var i = 0; i < frames; i++)
                {
                    world.Step(1f / 30f);
                    director.Update(world, heard);
                }
            }
            Tick(5);
            world.BeginDrag(boy);
            Tick(2);
            world.DragTo(boy, new V2(world.Width * 0.5f, world.Height * 0.9f));
            world.EndDrag(boy);
            Tick(60);
            Assert.Contains(heard, c => c.Kind == SoundKind.Whee);
            Assert.Contains(heard, c => c.Kind == SoundKind.Thud);
        }

        // strongest frequency below 600 Hz (a crude pitch estimate) via a DFT scan
        private static float DominantFrequency(float[] s)
        {
            var best = 0f;
            var bestPower = 0.0;
            for (var f = 60f; f < 600f; f += 5f)
            {
                double re = 0, im = 0;
                for (var i = 0; i < s.Length; i++)
                {
                    var a = 2 * Math.PI * f * i / SoundSynth.SampleRate;
                    re += s[i] * Math.Cos(a);
                    im += s[i] * Math.Sin(a);
                }
                var power = re * re + im * im;
                if (power <= bestPower) continue;
                bestPower = power;
                best = f;
            }
            return best;
        }

        private static void WriteWav(string path, float[] samples)
        {
            using var w = new BinaryWriter(File.Create(path));
            w.Write("RIFF".ToCharArray()); w.Write(36 + samples.Length * 2); w.Write("WAVE".ToCharArray());
            w.Write("fmt ".ToCharArray()); w.Write(16); w.Write((short)1); w.Write((short)1);
            w.Write(SoundSynth.SampleRate); w.Write(SoundSynth.SampleRate * 2); w.Write((short)2); w.Write((short)16);
            w.Write("data".ToCharArray()); w.Write(samples.Length * 2);
            foreach (var v in samples) w.Write((short)(Math.Clamp(v, -1f, 1f) * 32000));
        }
    }
}
