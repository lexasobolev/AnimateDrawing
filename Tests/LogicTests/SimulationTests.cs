using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AnimatedDrawingsWorld.Logic;
using AnimatedDrawingsWorld.Logic.Animation;
using AnimatedDrawingsWorld.Logic.Behavior;
using AnimatedDrawingsWorld.Logic.Characters;
using AnimatedDrawingsWorld.Logic.Imaging;
using AnimatedDrawingsWorld.Logic.Scene;
using Xunit;
using Xunit.Abstractions;

namespace AnimatedDrawingsWorld.Tests
{
    // End-to-end: background image -> analysis -> real kids' characters living in it.
    // Set RENDER_SIM=1 to also write the frames (Tests/Output/sim/<scene>/) for a GIF.
    public class SimulationTests
    {
        private readonly ITestOutputHelper output;
        public SimulationTests(ITestOutputHelper output) => this.output = output;

        private static readonly MotionLibrary Motions = AnimationTests.LoadMotions();

        private sealed class Cast
        {
            public GameWorld World;
            public RgbaImage Background;
            public readonly Dictionary<Agent, (CharacterAnimator animator, RgbaImage texture)> Characters = new();
        }

        private static Cast Setup(string scene, int seed)
        {
            var background = TestSupport.Load(Path.Combine(TestSupport.Backgrounds, scene + ".png"));
            var cast = new Cast { Background = background, World = new GameWorld(BackgroundAnalyzer.Analyze(background), seed) };

            void Add(string name, BuiltCharacter c, CharacterKind kind, float height)
            {
                var agent = cast.World.AddCharacter(name, kind, height);
                cast.Characters[agent] = (new CharacterAnimator(c.Rig, c.Mesh, Motions), c.Annotation.Texture);
            }

            Add("Crown Boy", AnimationTests.LoadMetaCharacter("boy_crown"), CharacterKind.Human, 1.9f);
            Add("Ballerina", AnimationTests.LoadMetaCharacter("girl_ballerina"), CharacterKind.Human, 1.8f);
            Add("Candy Corn", AnimationTests.LoadMetaCharacter("candy_corn_monster"), CharacterKind.Monster, 1.7f);
            // straight from a photo of the drawing, no Meta annotations at all
            var garlic = CharacterBuilder.Build(CharacterBuilder.AnnotateWithoutModels(TestSupport.Load(Path.Combine(TestSupport.Characters, "garlic_monster", "page.png"))));
            Add("Garlic", garlic, CharacterKind.Monster, 1.6f);
            Add("Piggy", AnimationTests.LoadMetaCharacter("pig_animal"), CharacterKind.Animal, 1.1f);
            return cast;
        }

        [Theory]
        [InlineData("meadow_trees_sun", new[] { Activity.Climb, Activity.Smell })]
        [InlineData("house_tree_garden", new[] { Activity.Climb, Activity.Hide })]
        [InlineData("beach_sea_rock", new[] { Activity.Swim })]
        [InlineData("mountains_pond", new[] { Activity.Swim, Activity.Climb })]
        [InlineData("pencil_sketch", new Activity[0])]
        [InlineData("real_kid_room_photo", new Activity[0])]
        public void CharactersLiveInTheDrawing(string scene, Activity[] expectedObjectActivities)
        {
            var cast = Setup(scene, seed: scene.Length);
            var world = cast.World;
            var seen = new HashSet<Activity>();
            var log = new List<string>();
            world.Log += (_, line) => log.Add($"{world.Time,6:0.0}s {line}");

            var render = Environment.GetEnvironmentVariable("RENDER_SIM") == "1";
            var renderer = render ? new SoftwareRenderer(cast.Background, 480) : null;
            var frameDir = Path.Combine(TestSupport.OutputDir, "sim", scene);
            if (render) Directory.CreateDirectory(frameDir);

            const float dt = 1f / 30f;
            var frames = 0;
            for (var step = 0; step < 30 * 240; step++)
            {
                world.Step(dt);
                foreach (var (agent, c) in cast.Characters)
                {
                    c.animator.Update(agent.Activity, dt, agent.AnimationSpeed, agent.ClipOverride);
                    seen.Add(agent.Activity);

                    Assert.InRange(agent.Feet.X, 0f, world.Width);
                    Assert.InRange(agent.Feet.Y, 0f, world.Height);
                    if (!agent.OffGround && agent.Activity != Activity.Fall)
                    {
                        var x = agent.Feet.X;
                        Assert.True(agent.Feet.Y <= world.GroundTop(x) + 0.02f && agent.Feet.Y >= world.BandBottom(x) - 0.02f,
                            $"{agent} is floating/sunk (ground {world.GroundTop(x):0.00}, band bottom {world.BandBottom(x):0.00})");
                    }
                }

                if (render && step % 4 == 0 && step < 30 * 60)
                    TestSupport.SavePng(renderer.Render(world, cast.Characters), Path.Combine(frameDir, $"frame_{frames++:0000}.png"));
            }

            output.WriteLine(cast.World.Layout.Describe());
            output.WriteLine("activities: " + string.Join(", ", seen.OrderBy(a => a)));
            foreach (var line in log.Take(60)) output.WriteLine(line);

            foreach (var activity in expectedObjectActivities)
                Assert.Contains(activity, seen);
            // social life happens everywhere
            Assert.True(seen.Contains(Activity.Talk) || seen.Contains(Activity.Scare) || seen.Contains(Activity.Dance), "nobody interacted");
            Assert.Contains(Activity.Walk, seen);
        }

        [Fact]
        public void DroppingCharactersOnTheSceneLandsThemSensibly()
        {
            var cast = Setup("meadow_trees_sun", 7);
            var world = cast.World;
            var boy = world.Agents[0];
            var tree = world.Layout.OfKind(SceneObjectKind.Tree).First();

            // dropped into the tree crown: sits on a branch, then climbs down
            world.BeginDrag(boy);
            world.DragTo(boy, world.ToWorld(new V2(tree.Bounds.Center.X, tree.Bounds.YMax - tree.Bounds.Height * 0.2f)));
            world.EndDrag(boy);
            world.Step(0.05f);
            Assert.True(boy.OffGround);
            Assert.Equal(Activity.Sit, boy.Activity);
            for (var i = 0; i < 30 * 12; i++) world.Step(1f / 30f);
            Assert.False(boy.OffGround);

            // dropped from the sky: falls onto the ground
            var girl = world.Agents[1];
            world.BeginDrag(girl);
            world.DragTo(girl, new V2(world.Width * 0.5f, world.Height * 0.95f));
            world.EndDrag(girl);
            Assert.Equal(Activity.Fall, girl.Activity);
            for (var i = 0; i < 30 * 2; i++) world.Step(1f / 30f);
            Assert.False(girl.OffGround);
            Assert.True(girl.Feet.Y <= world.GroundTop(girl.Feet.X) + 0.02f);
        }

        [Fact]
        public void PlayerCommandsUseTheScene()
        {
            var cast = Setup("house_tree_garden", 3);
            var world = cast.World;
            var girl = world.Agents[1];
            world.Command(girl, Activity.Climb);
            var maxY = 0f;
            for (var i = 0; i < 30 * 25; i++)
            {
                world.Step(1f / 30f);
                maxY = Math.Max(maxY, girl.Feet.Y - world.GroundTop(girl.Feet.X));
            }
            Assert.True(maxY > girl.BodyHeight * 0.3f, $"climbed only {maxY:0.00}");

            world.Command(girl, Activity.Hide);
            var hidden = false;
            for (var i = 0; i < 30 * 25; i++)
            {
                world.Step(1f / 30f);
                hidden |= girl.Opacity < 0.05f;
            }
            Assert.True(hidden, "never went into the house");
        }

        [Fact]
        public void ChangingTheBackgroundReplacesEveryone()
        {
            var cast = Setup("meadow_trees_sun", 5);
            var world = cast.World;
            for (var i = 0; i < 30 * 20; i++) world.Step(1f / 30f);

            var beach = BackgroundAnalyzer.Analyze(TestSupport.Load(Path.Combine(TestSupport.Backgrounds, "beach_sea_rock.png")));
            world.SetLayout(beach);
            for (var i = 0; i < 30 * 3; i++) world.Step(1f / 30f);
            foreach (var a in world.Agents)
            {
                if (a.OffGround) continue;
                Assert.True(a.Feet.Y <= world.GroundTop(a.Feet.X) + 0.02f, $"{a} not on the new ground");
            }
        }
    }
}
