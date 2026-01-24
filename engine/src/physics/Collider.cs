//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ;

public class Collider
{
    private readonly Vector2[] _points;
    private readonly Rect _bounds;

    public ReadOnlySpan<Vector2> Points => _points;
    public Rect Bounds => _bounds;
    public int PointCount => _points.Length;

    private Collider(Vector2[] points, Rect bounds)
    {
        _points = points;
        _bounds = bounds;
    }

    public static Collider FromBounds(Rect bounds)
    {
        var points = new Vector2[4];
        points[0] = new Vector2(bounds.MinX, bounds.MinY);
        points[1] = new Vector2(bounds.MaxX, bounds.MinY);
        points[2] = new Vector2(bounds.MaxX, bounds.MaxY);
        points[3] = new Vector2(bounds.MinX, bounds.MaxY);
        return new Collider(points, bounds);
    }

    public static Collider FromPoints(ReadOnlySpan<Vector2> points)
    {
        var pointsCopy = points.ToArray();
        var bounds = ComputeBounds(points);
        return new Collider(pointsCopy, bounds);
    }

    private static Rect ComputeBounds(ReadOnlySpan<Vector2> points)
    {
        if (points.Length == 0)
            return Rect.Zero;

        var min = points[0];
        var max = points[0];

        for (var i = 1; i < points.Length; i++)
        {
            min = Vector2.Min(min, points[i]);
            max = Vector2.Max(max, points[i]);
        }

        return Rect.FromMinMax(min, max);
    }
}
