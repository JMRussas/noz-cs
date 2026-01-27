//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ;

/// <summary>
/// Integer 2D vector for screen coordinates and pixel positions.
/// </summary>
public struct Vector2Int : IEquatable<Vector2Int>
{
    public int X;
    public int Y;

    public Vector2Int(int x, int y)
    {
        X = x;
        Y = y;
    }

    public static Vector2Int Zero => new(0, 0);
    public static Vector2Int One => new(1, 1);

    public static implicit operator Vector2(Vector2Int v) => new(v.X, v.Y);

    public static Vector2Int operator +(Vector2Int a, Vector2Int b) => new(a.X + b.X, a.Y + b.Y);
    public static Vector2Int operator -(Vector2Int a, Vector2Int b) => new(a.X - b.X, a.Y - b.Y);
    public static Vector2Int operator *(Vector2Int a, int scalar) => new(a.X * scalar, a.Y * scalar);
    public static Vector2Int operator /(Vector2Int a, int scalar) => new(a.X / scalar, a.Y / scalar);
    public static Vector2Int operator -(Vector2Int v) => new(-v.X, -v.Y);

    public readonly bool Equals(Vector2Int other) => X == other.X && Y == other.Y;
    public readonly override bool Equals(object? obj) => obj is Vector2Int other && Equals(other);
    public readonly override int GetHashCode() => HashCode.Combine(X, Y);

    public static bool operator ==(Vector2Int left, Vector2Int right) => left.Equals(right);
    public static bool operator !=(Vector2Int left, Vector2Int right) => !left.Equals(right);

    public readonly override string ToString() => $"({X}, {Y})";

    public static Vector2Int Min(Vector2Int a, Vector2Int b) => new(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y));
    public static Vector2Int Max(Vector2Int a, Vector2Int b) => new(Math.Max(a.X, b.X), Math.Max(a.Y, b.Y));
}

/// <summary>
/// Extension methods for Vector2.
/// </summary>
public static class Vector2Extensions
{
    public static Vector2Int ToVector2Int(this Vector2 v) => new((int)v.X, (int)v.Y);
    public static Vector2 ToVector2(this Vector2Int v) => new(v.X, v.Y);
}
