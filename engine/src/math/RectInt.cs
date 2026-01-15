//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ;

/// <summary>
/// Integer rectangle with position and size.
/// </summary>
public struct RectInt(int x, int y, int width, int height)
    : IEquatable<RectInt>
{
    public static readonly RectInt Zero = new(0,0,0,0);  
    
    public int X = x;
    public int Y = y;
    public int Width = width;
    public int Height = height;

    // Edges
    public int Left => X;
    public int Right => X + Width;
    public int Top => Y;
    public int Bottom => Y + Height;

    // Computed properties
    public Vector2Int Position => new(X, Y);
    public Vector2Int Size => new(Width, Height);

    /// <summary>
    /// Check if a point is inside the rectangle.
    /// </summary>
    public bool Contains(int px, int py)
    {
        return px >= X && px < X + Width && py >= Y && py < Y + Height;
    }

    /// <summary>
    /// Check if a point is inside the rectangle.
    /// </summary>
    public bool Contains(Vector2Int point) => Contains(point.X, point.Y);

    /// <summary>
    /// Scale the rectangle by the given factor.
    /// </summary>
    public RectInt Scale(int factor)
    {
        return new RectInt(X * factor, Y * factor, Width * factor, Height * factor);
    }

    /// <summary>
    /// Expand the rectangle by the given amount on all sides.
    /// </summary>
    public RectInt Expand(int amount)
    {
        return new RectInt(X - amount, Y - amount, Width + amount * 2, Height + amount * 2);
    }

    /// <summary>
    /// Get the union of two rectangles.
    /// </summary>
    public static RectInt Union(RectInt a, RectInt b)
    {
        int x1 = Math.Min(a.X, b.X);
        int y1 = Math.Min(a.Y, b.Y);
        int x2 = Math.Max(a.X + a.Width, b.X + b.Width);
        int y2 = Math.Max(a.Y + a.Height, b.Y + b.Height);
        return new RectInt(x1, y1, x2 - x1, y2 - y1);
    }

    /// <summary>
    /// Convert to floating-point rectangle.
    /// </summary>
    public Rect ToRect() => new(X, Y, Width, Height);

    public bool Equals(RectInt other)
    {
        return X == other.X && Y == other.Y && Width == other.Width && Height == other.Height;
    }

    public override bool Equals(object? obj) => obj is RectInt other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(X, Y, Width, Height);

    public static bool operator ==(RectInt left, RectInt right) => left.Equals(right);
    public static bool operator !=(RectInt left, RectInt right) => !left.Equals(right);

    public override string ToString() => $"RectInt({X}, {Y}, {Width}, {Height})";
}
