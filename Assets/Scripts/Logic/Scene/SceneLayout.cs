using System;
using System.Collections.Generic;
using System.Text;
using AnimatedDrawingsWorld.Logic.Behavior;

namespace AnimatedDrawingsWorld.Logic.Scene
{
    public enum SceneObjectKind
    {
        Sky,
        Grass,
        Dirt,
        Sand,
        Floor,
        Water,
        Tree,
        Bush,
        Sun,
        Cloud,
        House,
        Flower,
        Rock,
        Mountain,
        Shape,
    }

    public enum SurfaceType
    {
        Grass,
        Dirt,
        Sand,
        Floor,
        Water,
    }

    [Serializable]
    public sealed class SceneObject
    {
        public int Id;
        public SceneObjectKind Kind;
        public Rect2 Bounds;        // normalized background coordinates, (0,0) bottom-left, y up
        public float Confidence;    // 0..1
        public string Label;        // human readable ("tree", "red house", ...)
        public string Color;        // dominant colour class name

        // Tree-specific geometry
        public Rect2 Trunk;         // trunk rectangle (climbing path)
        public float CrownBottom;   // where a climber stops and sits

        public override string ToString() => $"#{Id} {Kind} {Bounds} ({Confidence:0.00})";
    }

    // Everything the game knows about a background drawing: the objects in it and where
    // characters can stand.
    public sealed class SceneLayout
    {
        public int SourceWidth;
        public int SourceHeight;
        public float Aspect => SourceHeight > 0 ? (float)SourceWidth / SourceHeight : 1f;
        public readonly List<SceneObject> Objects = new();

        // walkable ground: top surface height (normalized, y up) sampled across the width
        public float[] GroundTop = Array.Empty<float>();
        public SurfaceType[] Surface = Array.Empty<SurfaceType>();
        public bool GroundDetected;
        public string Analyzer = "heuristic";

        public float GroundAt(float x01)
        {
            if (GroundTop.Length == 0) return 0.2f;
            var f = MathUtil.Clamp01(x01) * (GroundTop.Length - 1);
            var i = (int)f;
            var j = Math.Min(i + 1, GroundTop.Length - 1);
            return MathUtil.Lerp(GroundTop[i], GroundTop[j], f - i);
        }

        public SurfaceType SurfaceAt(float x01)
        {
            if (Surface.Length == 0) return SurfaceType.Grass;
            return Surface[MathUtil.Clamp((int)MathF.Round(MathUtil.Clamp01(x01) * (Surface.Length - 1)), 0, Surface.Length - 1)];
        }

        public IEnumerable<SceneObject> OfKind(SceneObjectKind kind)
        {
            foreach (var o in Objects) if (o.Kind == kind) yield return o;
        }

        public int Count(SceneObjectKind kind)
        {
            var n = 0;
            foreach (var o in Objects) if (o.Kind == kind) n++;
            return n;
        }

        public string Describe()
        {
            var sb = new StringBuilder();
            sb.Append($"{SourceWidth}x{SourceHeight} ground={(GroundDetected ? "detected" : "default")} [{Analyzer}]");
            foreach (var o in Objects) sb.Append("\n  ").Append(o.Label ?? o.Kind.ToString()).Append(' ').Append(o.Bounds);
            return sb.ToString();
        }
    }

    // What each kind of scene object lets a character do.
    public static class Affordances
    {
        public static IReadOnlyList<(Activity activity, float weight)> For(SceneObjectKind kind) => kind switch
        {
            SceneObjectKind.Tree => new[] { (Activity.Climb, 1f), (Activity.Sit, 0.35f) },
            SceneObjectKind.Bush => new[] { (Activity.Hide, 0.6f), (Activity.Smell, 0.3f) },
            SceneObjectKind.Grass => new[] { (Activity.Sleep, 0.6f), (Activity.Sit, 0.3f) },
            SceneObjectKind.Sun => new[] { (Activity.Wave, 0.5f), (Activity.Dance, 0.4f) },
            SceneObjectKind.Cloud => new[] { (Activity.Wave, 0.25f), (Activity.Jump, 0.25f) },
            SceneObjectKind.House => new[] { (Activity.Hide, 0.7f), (Activity.Wave, 0.2f) },
            SceneObjectKind.Flower => new[] { (Activity.Smell, 1f) },
            SceneObjectKind.Rock => new[] { (Activity.Sit, 0.8f), (Activity.Jump, 0.3f) },
            SceneObjectKind.Water => new[] { (Activity.Swim, 1f) },
            SceneObjectKind.Mountain => new[] { (Activity.Wave, 0.2f) },
            SceneObjectKind.Shape => new[] { (Activity.Sit, 0.4f), (Activity.Hide, 0.3f) },
            SceneObjectKind.Sand => new[] { (Activity.Sit, 0.4f), (Activity.Dance, 0.2f) },
            SceneObjectKind.Floor => new[] { (Activity.Sit, 0.3f), (Activity.Dance, 0.3f) },
            _ => Array.Empty<(Activity, float)>(),
        };
    }
}
