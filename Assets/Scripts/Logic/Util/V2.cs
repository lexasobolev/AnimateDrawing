using System;

namespace AnimatedDrawingsWorld.Logic
{
    // Engine-independent 2D vector. The Logic folder deliberately has no UnityEngine dependency
    // so the whole simulation (scene analysis, rigging, behavior) compiles and is unit-tested
    // outside Unity (see Tests/LogicTests); the Unity layer converts at the boundary.
    [Serializable]
    public struct V2 : IEquatable<V2>
    {
        public float X;
        public float Y;

        public V2(float x, float y)
        {
            X = x;
            Y = y;
        }

        public static readonly V2 Zero = new(0f, 0f);

        public float Length => MathF.Sqrt(X * X + Y * Y);
        public float LengthSquared => X * X + Y * Y;
        public V2 Normalized => Length > 1e-6f ? this / Length : Zero;

        public static V2 operator +(V2 a, V2 b) => new(a.X + b.X, a.Y + b.Y);
        public static V2 operator -(V2 a, V2 b) => new(a.X - b.X, a.Y - b.Y);
        public static V2 operator -(V2 a) => new(-a.X, -a.Y);
        public static V2 operator *(V2 a, float s) => new(a.X * s, a.Y * s);
        public static V2 operator *(float s, V2 a) => new(a.X * s, a.Y * s);
        public static V2 operator /(V2 a, float s) => new(a.X / s, a.Y / s);

        public static float Dot(V2 a, V2 b) => a.X * b.X + a.Y * b.Y;
        public static float Cross(V2 a, V2 b) => a.X * b.Y - a.Y * b.X;
        public static float Distance(V2 a, V2 b) => (a - b).Length;
        public static V2 Lerp(V2 a, V2 b, float t) => a + (b - a) * t;

        // rotates counter-clockwise by the given angle in degrees
        public V2 Rotate(float degrees)
        {
            var r = degrees * MathF.PI / 180f;
            var c = MathF.Cos(r);
            var s = MathF.Sin(r);
            return new V2(X * c - Y * s, X * s + Y * c);
        }

        // angle in degrees counter-clockwise from +Y, in [0, 360) — the convention Meta's
        // retargeter uses for bone orientations (0 = pointing up, 180 = pointing down)
        public float AngleFromUp()
        {
            var deg = (MathF.Atan2(Y, X) - MathF.PI / 2f) * 180f / MathF.PI;
            deg %= 360f;
            return deg < 0f ? deg + 360f : deg;
        }

        public static V2 FromAngleFromUp(float degrees) => new V2(0f, 1f).Rotate(degrees);

        public bool Equals(V2 other) => X.Equals(other.X) && Y.Equals(other.Y);
        public override bool Equals(object obj) => obj is V2 other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(X, Y);
        public override string ToString() => $"({X:0.###}, {Y:0.###})";
    }

    [Serializable]
    public struct Rect2
    {
        public float XMin;
        public float YMin;
        public float XMax;
        public float YMax;

        public Rect2(float xMin, float yMin, float xMax, float yMax)
        {
            XMin = xMin;
            YMin = yMin;
            XMax = xMax;
            YMax = yMax;
        }

        public float Width => XMax - XMin;
        public float Height => YMax - YMin;
        public V2 Center => new((XMin + XMax) * 0.5f, (YMin + YMax) * 0.5f);
        public float Area => Math.Max(0f, Width) * Math.Max(0f, Height);

        public bool Contains(V2 p) => p.X >= XMin && p.X <= XMax && p.Y >= YMin && p.Y <= YMax;

        public float HorizontalOverlap(Rect2 other) =>
            Math.Max(0f, Math.Min(XMax, other.XMax) - Math.Max(XMin, other.XMin));

        public static Rect2 Union(Rect2 a, Rect2 b) =>
            new(Math.Min(a.XMin, b.XMin), Math.Min(a.YMin, b.YMin), Math.Max(a.XMax, b.XMax), Math.Max(a.YMax, b.YMax));

        public Rect2 Scaled(float sx, float sy) => new(XMin * sx, YMin * sy, XMax * sx, YMax * sy);

        public override string ToString() => $"[{XMin:0.###},{YMin:0.###} .. {XMax:0.###},{YMax:0.###}]";
    }

    public static class MathUtil
    {
        public static float Clamp(float v, float min, float max) => v < min ? min : v > max ? max : v;
        public static int Clamp(int v, int min, int max) => v < min ? min : v > max ? max : v;
        public static float Clamp01(float v) => Clamp(v, 0f, 1f);
        public static float Lerp(float a, float b, float t) => a + (b - a) * t;
        public static float InverseLerp(float a, float b, float v) => Math.Abs(b - a) < 1e-9f ? 0f : Clamp01((v - a) / (b - a));
        public static float SmoothStep(float t) => t * t * (3f - 2f * t);

        // wraps a degree angle into (-180, 180]
        public static float WrapDegrees(float d)
        {
            d %= 360f;
            if (d > 180f) d -= 360f;
            if (d <= -180f) d += 360f;
            return d;
        }

        public static float LerpAngle(float a, float b, float t) => a + WrapDegrees(b - a) * t;
    }
}
