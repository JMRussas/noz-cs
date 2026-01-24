//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ;

public static class Physics
{
    public static bool OverlapPoint(
        in Vector2 v0,
        in Vector2 v1,
        in Vector2 v2,
        in Vector2 point,
        out Vector2 barycentric)
    {
        barycentric = default;

        var area = (v1.X - v0.X) * (v2.Y - v0.Y) - (v2.X - v0.X) * (v1.Y - v0.Y);

        if (MathF.Abs(area) < 1e-6f)
            return false;

        var invArea = 1.0f / area;
        var s = ((v2.Y - v0.Y) * (point.X - v0.X) + (v0.X - v2.X) * (point.Y - v0.Y)) * invArea;
        var t = ((v0.Y - v1.Y) * (point.X - v0.X) + (v1.X - v0.X) * (point.Y - v0.Y)) * invArea;

        if (s >= 0 && t >= 0 && (s + t) <= 1)
        {
            barycentric = new Vector2(s, t);
            return true;
        }

        return false;
    }

    public static bool OverlapLine(
        in Vector2 l0Start,
        in Vector2 l0End,
        in Vector2 l1Start,
        in Vector2 l1End,
        out Vector2 intersection)
    {
        intersection = default;

        var d0 = l0End - l0Start;
        var d1 = l1End - l1Start;
        var cross = d0.X * d1.Y - d0.Y * d1.X;
        if (MathF.Abs(cross) < MathEx.Epsilon)
            return false;

        var delta = l1Start - l0Start;
        var t = (delta.X * d1.Y - delta.Y * d1.X) / cross;
        var u = (delta.X * d0.Y - delta.Y * d0.X) / cross;
        if (t >= 0.0f && t <= 1.0f && u >= 0.0f && u <= 1.0f)
        {
            intersection = l0Start + d0 * t;
            return true;
        }

        return false;
    }

    public static bool ClosestPointOnLine(
        in Vector2 lineStart,
        in Vector2 lineEnd,
        in Vector2 point,
        float maxDistance,
        out Vector2 closestPoint)
    {
        var line = lineEnd - lineStart;
        var lengthSq = line.LengthSquared();

        if (lengthSq < MathEx.Epsilon)
        {
            closestPoint = lineStart;
            return Vector2.Distance(point, lineStart) <= maxDistance;
        }

        var t = Math.Clamp(Vector2.Dot(point - lineStart, line) / lengthSq, 0f, 1f);
        closestPoint = lineStart + line * t;

        return Vector2.Distance(point, closestPoint) <= maxDistance;
    }

    public static bool OverlapPoint(Collider collider, Matrix3x2 transform, Vector2 point)
    {
        var points = collider.Points;
        if (points.Length == 0)
            return false;

        var v1 = Vector2.Transform(points[points.Length - 1], transform);

        for (var i = 0; i < points.Length; i++)
        {
            var v2 = Vector2.Transform(points[i], transform);
            var edge = v2 - v1;
            var toPoint = point - v1;
            var cross = edge.X * toPoint.Y - edge.Y * toPoint.X;

            if (cross < 0)
                return false;

            v1 = v2;
        }

        return true;
    }

    public static bool OverlapBounds(Collider collider, Matrix3x2 transform, Rect bounds)
    {
        Span<Vector2> corners = stackalloc Vector2[4];
        corners[0] = new Vector2(bounds.MinX, bounds.MinY);
        corners[1] = new Vector2(bounds.MaxX, bounds.MinY);
        corners[2] = new Vector2(bounds.MaxX, bounds.MaxY);
        corners[3] = new Vector2(bounds.MinX, bounds.MaxY);

        for (var i = 0; i < 4; i++)
        {
            if (OverlapPoint(collider, transform, corners[i]))
                return true;
        }

        var points = collider.Points;
        if (points.Length == 0)
            return false;

        var v1 = Vector2.Transform(points[points.Length - 1], transform);
        for (var i = 0; i < points.Length; i++)
        {
            var v2 = Vector2.Transform(points[i], transform);
            if (IntersectsLine(bounds, v1, v2))
                return true;

            v1 = v2;
        }

        return false;
    }

    public static bool Raycast(Collider collider, Matrix3x2 transform, Vector2 start, Vector2 end, out RaycastResult result)
    {
        var direction = end - start;
        var distance = direction.Length();
        if (distance < MathEx.Epsilon)
        {
            result = default;
            return false;
        }

        return Raycast(collider, transform, start, direction / distance, distance, out result);
    }

    public static bool Raycast(Collider collider, Matrix3x2 transform, Vector2 origin, Vector2 direction, float distance, out RaycastResult result)
    {
        result = default;
        result.Fraction = 1.0f;

        var rayEnd = origin + direction * distance;
        var points = collider.Points;

        if (points.Length == 0)
            return false;

        var v1 = Vector2.Transform(points[points.Length - 1], transform);
        for (var i = 0; i < points.Length; i++)
        {
            var v2 = Vector2.Transform(points[i], transform);

            if (OverlapLine(origin, rayEnd, v1, v2, out var intersection))
            {
                var overlapDistance = Vector2.Distance(intersection, origin);
                var fraction = overlapDistance / distance;

                if (fraction < result.Fraction)
                {
                    result.Point = intersection;
                    result.Fraction = fraction;
                    result.Distance = overlapDistance;

                    var edge = v2 - v1;
                    result.Normal = Vector2.Normalize(new Vector2(-edge.Y, edge.X));
                }
            }

            v1 = v2;
        }

        return result.Fraction < 1.0f;
    }

    public static bool CircleCast(Collider collider, Matrix3x2 transform, Vector2 start, Vector2 end, float radius, out RaycastResult result)
    {
        var direction = end - start;
        var distance = direction.Length();
        if (distance < MathEx.Epsilon)
        {
            result = default;
            return false;
        }

        return CircleCast(collider, transform, start, direction / distance, distance, radius, out result);
    }

    public static bool CircleCast(Collider collider, Matrix3x2 transform, Vector2 origin, Vector2 direction, float distance, float radius, out RaycastResult result)
    {
        result = default;
        result.Fraction = 1.0f;

        var points = collider.Points;
        if (points.Length == 0)
            return false;

        var v1 = Vector2.Transform(points[points.Length - 1], transform);
        for (var i = 0; i < points.Length; i++)
        {
            var v2 = Vector2.Transform(points[i], transform);

            var edge = v2 - v1;
            var edgeNormal = Vector2.Normalize(new Vector2(edge.Y, -edge.X));

            var v1Offset = v1 + edgeNormal * radius;
            var v2Offset = v2 + edgeNormal * radius;

            var rayEnd = origin + direction * distance;
            if (OverlapLine(origin, rayEnd, v1Offset, v2Offset, out var intersection))
            {
                var overlapDistance = Vector2.Distance(intersection, origin);
                var fraction = overlapDistance / distance;

                if (fraction < result.Fraction)
                {
                    result.Point = intersection - edgeNormal * radius;
                    result.Fraction = fraction;
                    result.Distance = overlapDistance;
                    result.Normal = edgeNormal;
                }
            }

            // Test ray against circle at vertex v1 (handles rounded corners)
            var toVertex = origin - v1;
            var a = Vector2.Dot(direction, direction);
            var b = 2.0f * Vector2.Dot(toVertex, direction);
            var c = Vector2.Dot(toVertex, toVertex) - radius * radius;
            var discriminant = b * b - 4.0f * a * c;

            if (discriminant >= 0.0f)
            {
                var sqrtDisc = MathF.Sqrt(discriminant);
                var t = (-b - sqrtDisc) / (2.0f * a);

                if (t >= 0.0f && t <= distance)
                {
                    var fraction = t / distance;
                    if (fraction < result.Fraction)
                    {
                        var hitPoint = origin + direction * t;
                        result.Point = v1;
                        result.Fraction = fraction;
                        result.Distance = t;
                        result.Normal = Vector2.Normalize(hitPoint - v1);
                    }
                }
            }

            v1 = v2;
        }

        return result.Fraction < 1.0f;
    }

    private static bool IntersectsLine(Rect bounds, Vector2 lineStart, Vector2 lineEnd)
    {
        if (bounds.Contains(lineStart) || bounds.Contains(lineEnd))
            return true;

        Span<Vector2> corners = stackalloc Vector2[4];
        corners[0] = bounds.TopLeft;
        corners[1] = bounds.TopRight;
        corners[2] = bounds.BottomRight;
        corners[3] = bounds.BottomLeft;

        for (var i = 0; i < 4; i++)
        {
            var next = (i + 1) % 4;
            if (OverlapLine(lineStart, lineEnd, corners[i], corners[next], out _))
                return true;
        }

        return false;
    }
}
