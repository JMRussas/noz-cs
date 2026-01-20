//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace NoZ;

[StructLayout(LayoutKind.Sequential, Pack = 0)]
public struct Rect(float x, float y, float width, float height)
    : IEquatable<Rect>
{
    public float X = x;
    public float Y = y;
    public float Width = width;
    public float Height = height;

    public Rect(Vector2 position, Vector2 size) : this(position.X, position.Y, size.X, size.Y)
    {
    }

    public static Rect Zero => new(0, 0, 0, 0);

    // Edges
    public float Left => X;
    public float Right => X + Width;
    public float Top => Y;
    public float Bottom => Y + Height;

    // Computed properties
    public Vector2 Position => new(X, Y);
    public Vector2 Size => new(Width, Height);
    public Vector2 Center => new(X + Width * 0.5f, Y + Height * 0.5f);
    public Vector2 Min => new(X, Y);
    public Vector2 Max => new(X + Width, Y + Height);
    public float MinX => X;
    public float MinY => Y;
    public float MaxX => X + Width;
    public float MaxY => Y + Height;
    public Vector2 TopLeft => new(X, Y);
    public Vector2 TopRight => new(X + Width, Y);
    public Vector2 BottomLeft => new(X, Y + Height);
    public Vector2 BottomRight => new(X + Width, Y + Height);

    public unsafe float this[int index]
    {
        get
        {
            fixed (float* ptr = &X)
                return ptr[index];
        }
        set
        {
            fixed (float* ptr = &X)
                ptr[index] = value;
        }
    }

    public unsafe float GetSize(int axis)
    {
        fixed (float* ptr = &Width)
            return ptr[axis];
    }

    public bool Contains(float px, float py)
    {
        return px >= X && px <= X + Width && py >= Y && py <= Y + Height;
    }

    public bool Contains(Vector2 point) => Contains(point.X, point.Y);

    public bool Intersects(Rect other)
    {
        return !(other.X > X + Width || other.X + other.Width < X ||
                 other.Y > Y + Height || other.Y + other.Height < Y);
    }

    public Rect Intersection(Rect other)
    {
        var x1 = MathF.Max(X, other.X);
        var y1 = MathF.Max(Y, other.Y);
        var x2 = MathF.Min(X + Width, other.X + other.Width);
        var y2 = MathF.Min(Y + Height, other.Y + other.Height);

        if (x2 < x1 || y2 < y1)
            return Zero;

        return new Rect(x1, y1, x2 - x1, y2 - y1);
    }

    public readonly Rect Expand(float amount) =>new(X - amount, Y - amount, Width + amount * 2, Height + amount * 2);
    public readonly Rect Translate(in Vector2 offset) => new Rect(X + offset.X, Y + offset.Y, Width, Height);
    public readonly Rect Scale(float scale) =>
        new(X * scale, Y * scale, Width * scale, Height * scale);

    public static Rect FromMinMax(Vector2 min, Vector2 max) => new(min.X, min.Y, max.X - min.X, max.Y - min.Y);
    public static Rect FromMinMax(float minX, float minY, float maxX, float maxY) =>
        new(minX, minY, maxX - minX, maxY - minY);
    public static Rect FromCenterSize(Vector2 center, Vector2 size) => new(center.X - size.X * 0.5f, center.Y - size.Y * 0.5f, size.X, size.Y);

    public static Rect Lerp(Rect a, Rect b, float t)
    {
        return new Rect(
            a.X + (b.X - a.X) * t,
            a.Y + (b.Y - a.Y) * t,
            a.Width + (b.Width - a.Width) * t,
            a.Height + (b.Height - a.Height) * t
        );
    }

    public static Rect Union(Rect a, Rect b)
    {
        var minX = MathF.Min(a.X, b.X);
        var minY = MathF.Min(a.Y, b.Y);
        var maxX = MathF.Max(a.Right, b.Right);
        var maxY = MathF.Max(a.Bottom, b.Bottom);
        return new Rect(minX, minY, maxX - minX, maxY - minY);
    }

    public bool Equals(Rect other)
    {
        return X == other.X && Y == other.Y && Width == other.Width && Height == other.Height;
    }

    public override bool Equals(object? obj) => obj is Rect other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(X, Y, Width, Height);

    public static bool operator ==(Rect left, Rect right) => left.Equals(right);
    public static bool operator !=(Rect left, Rect right) => !left.Equals(right);

    public override string ToString() => $"<{X}, {Y}, {Width}, {Height}>";
}
