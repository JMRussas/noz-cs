//
//  Faithful port of msdfgen by Viktor Chlumsky
//  https://github.com/Chlumsky/msdfgen
//

using System;
using static NoZ.Editor.Msdf.MsdfMath;

namespace NoZ.Editor.Msdf;

internal abstract class EdgeSegment
{
    public const int CUBIC_SEARCH_STARTS = 4;
    public const int CUBIC_SEARCH_STEPS = 4;

    public EdgeColor color;

    protected EdgeSegment(EdgeColor edgeColor = EdgeColor.WHITE) { color = edgeColor; }

    public abstract EdgeSegment Clone();
    public abstract Vector2Double Point(double param);
    public abstract Vector2Double Direction(double param);
    public abstract SignedDistance GetSignedDistance(Vector2Double origin, out double param);
    public abstract int ScanlineIntersections(Span<double> x, Span<int> dy, double y);
    public abstract void Bound(ref double xMin, ref double yMin, ref double xMax, ref double yMax);
    public abstract void Reverse();
    public abstract void SplitInThirds(out EdgeSegment part0, out EdgeSegment part1, out EdgeSegment part2);

    public void DistanceToPerpendicularDistance(ref SignedDistance distance, Vector2Double origin, double param)
    {
        if (param < 0)
        {
            var dir = Normalize(Direction(0));
            var aq = origin - Point(0);
            double ts = Dot(aq, dir);
            if (ts < 0)
            {
                double perpendicularDistance = Cross(aq, dir);
                if (Math.Abs(perpendicularDistance) <= Math.Abs(distance.distance))
                {
                    distance.distance = perpendicularDistance;
                    distance.dot = 0;
                }
            }
        }
        else if (param > 1)
        {
            var dir = Normalize(Direction(1));
            var bq = origin - Point(1);
            double ts = Dot(bq, dir);
            if (ts > 0)
            {
                double perpendicularDistance = Cross(bq, dir);
                if (Math.Abs(perpendicularDistance) <= Math.Abs(distance.distance))
                {
                    distance.distance = perpendicularDistance;
                    distance.dot = 0;
                }
            }
        }
    }

    protected static void PointBounds(Vector2Double p, ref double xMin, ref double yMin, ref double xMax, ref double yMax)
    {
        if (p.x < xMin) xMin = p.x;
        if (p.y < yMin) yMin = p.y;
        if (p.x > xMax) xMax = p.x;
        if (p.y > yMax) yMax = p.y;
    }
}

internal class LinearSegment : EdgeSegment
{
    public Vector2Double[] p = new Vector2Double[2];

    public LinearSegment(Vector2Double p0, Vector2Double p1, EdgeColor edgeColor = EdgeColor.WHITE) : base(edgeColor)
    {
        p[0] = p0;
        p[1] = p1;
    }

    public override EdgeSegment Clone() => new LinearSegment(p[0], p[1], color);

    public override Vector2Double Point(double param) => VecMix(p[0], p[1], param);

    public override Vector2Double Direction(double param) => p[1] - p[0];

    public override SignedDistance GetSignedDistance(Vector2Double origin, out double param)
    {
        var aq = origin - p[0];
        var ab = p[1] - p[0];
        param = Dot(aq, ab) / Dot(ab, ab);
        var eq = (param > 0.5 ? p[1] : p[0]) - origin;
        double endpointDistance = Length(eq);
        if (param > 0 && param < 1)
        {
            double orthoDistance = Dot(GetOrthonormal(ab, false), aq);
            if (Math.Abs(orthoDistance) < endpointDistance)
                return new SignedDistance(orthoDistance, 0);
        }
        return new SignedDistance(
            NonZeroSign(Cross(aq, ab)) * endpointDistance,
            Math.Abs(Dot(Normalize(ab), Normalize(eq))));
    }

    public override int ScanlineIntersections(Span<double> x, Span<int> dy, double y)
    {
        if ((y >= p[0].y && y < p[1].y) || (y >= p[1].y && y < p[0].y))
        {
            double param = (y - p[0].y) / (p[1].y - p[0].y);
            x[0] = (1.0 - param) * p[0].x + param * p[1].x;
            dy[0] = Sign(p[1].y - p[0].y);
            return 1;
        }
        return 0;
    }

    public override void Bound(ref double xMin, ref double yMin, ref double xMax, ref double yMax)
    {
        PointBounds(p[0], ref xMin, ref yMin, ref xMax, ref yMax);
        PointBounds(p[1], ref xMin, ref yMin, ref xMax, ref yMax);
    }

    public override void Reverse()
    {
        (p[0], p[1]) = (p[1], p[0]);
    }

    public override void SplitInThirds(out EdgeSegment part0, out EdgeSegment part1, out EdgeSegment part2)
    {
        part0 = new LinearSegment(p[0], Point(1.0 / 3.0), color);
        part1 = new LinearSegment(Point(1.0 / 3.0), Point(2.0 / 3.0), color);
        part2 = new LinearSegment(Point(2.0 / 3.0), p[1], color);
    }
}

internal class QuadraticSegment : EdgeSegment
{
    public Vector2Double[] p = new Vector2Double[3];

    public QuadraticSegment(Vector2Double p0, Vector2Double p1, Vector2Double p2, EdgeColor edgeColor = EdgeColor.WHITE) : base(edgeColor)
    {
        // Degenerate control point → push to midpoint to prevent zero-length tangent
        if (Cross(p1 - p0, p2 - p1) == 0)
            p1 = 0.5 * (p0 + p2);
        p[0] = p0;
        p[1] = p1;
        p[2] = p2;
    }

    public override EdgeSegment Clone() => new QuadraticSegment(p[0], p[1], p[2], color);

    public override Vector2Double Point(double param)
    {
        return VecMix(VecMix(p[0], p[1], param), VecMix(p[1], p[2], param), param);
    }

    public override Vector2Double Direction(double param)
    {
        var tangent = VecMix(p[1] - p[0], p[2] - p[1], param);
        if (tangent.x == 0 && tangent.y == 0)
            return p[2] - p[0];
        return tangent;
    }

    public override SignedDistance GetSignedDistance(Vector2Double origin, out double param)
    {
        var qa = p[0] - origin;
        var ab = p[1] - p[0];
        var br = p[2] - p[1] - ab;
        double a = Dot(br, br);
        double b = 3 * Dot(ab, br);
        double c = 2 * Dot(ab, ab) + Dot(qa, br);
        double d = Dot(qa, ab);
        Span<double> t = stackalloc double[3];
        int solutions = SolveCubic(t, a, b, c, d);

        var epDir = Direction(0);
        double minDistance = NonZeroSign(Cross(epDir, qa)) * Length(qa);
        param = -Dot(qa, epDir) / Dot(epDir, epDir);
        {
            double distance = Length(p[2] - origin);
            if (distance < Math.Abs(minDistance))
            {
                epDir = Direction(1);
                minDistance = NonZeroSign(Cross(epDir, p[2] - origin)) * distance;
                param = Dot(origin - p[1], epDir) / Dot(epDir, epDir);
            }
        }
        for (int i = 0; i < solutions; ++i)
        {
            if (t[i] > 0 && t[i] < 1)
            {
                var qe = qa + 2 * t[i] * ab + t[i] * t[i] * br;
                double distance = Length(qe);
                if (distance <= Math.Abs(minDistance))
                {
                    minDistance = NonZeroSign(Cross(ab + t[i] * br, qe)) * distance;
                    param = t[i];
                }
            }
        }

        if (param >= 0 && param <= 1)
            return new SignedDistance(minDistance, 0);
        if (param < 0.5)
            return new SignedDistance(minDistance, Math.Abs(Dot(Normalize(Direction(0)), Normalize(qa))));
        else
            return new SignedDistance(minDistance, Math.Abs(Dot(Normalize(Direction(1)), Normalize(p[2] - origin))));
    }

    public override int ScanlineIntersections(Span<double> x, Span<int> dy, double y)
    {
        int total = 0;
        int nextDY = y > p[0].y ? 1 : -1;
        x[total] = p[0].x;
        if (p[0].y == y)
        {
            if (p[0].y < p[1].y || (p[0].y == p[1].y && p[0].y < p[2].y))
                dy[total++] = 1;
            else
                nextDY = 1;
        }
        {
            var ab2 = p[1] - p[0];
            var br2 = p[2] - p[1] - ab2;
            Span<double> t = stackalloc double[2];
            int solutions = SolveQuadratic(t, br2.y, 2 * ab2.y, p[0].y - y);
            if (solutions >= 2 && t[0] > t[1])
                (t[0], t[1]) = (t[1], t[0]);
            for (int i = 0; i < solutions && total < 2; ++i)
            {
                if (t[i] >= 0 && t[i] <= 1)
                {
                    x[total] = p[0].x + 2 * t[i] * ab2.x + t[i] * t[i] * br2.x;
                    if (nextDY * (ab2.y + t[i] * br2.y) >= 0)
                    {
                        dy[total++] = nextDY;
                        nextDY = -nextDY;
                    }
                }
            }
        }
        if (p[2].y == y)
        {
            if (nextDY > 0 && total > 0)
            {
                --total;
                nextDY = -1;
            }
            if ((p[2].y < p[1].y || (p[2].y == p[1].y && p[2].y < p[0].y)) && total < 2)
            {
                x[total] = p[2].x;
                if (nextDY < 0)
                {
                    dy[total++] = -1;
                    nextDY = 1;
                }
            }
        }
        if (nextDY != (y >= p[2].y ? 1 : -1))
        {
            if (total > 0)
                --total;
            else
            {
                if (Math.Abs(p[2].y - y) < Math.Abs(p[0].y - y))
                    x[total] = p[2].x;
                dy[total++] = nextDY;
            }
        }
        return total;
    }

    public override void Bound(ref double xMin, ref double yMin, ref double xMax, ref double yMax)
    {
        PointBounds(p[0], ref xMin, ref yMin, ref xMax, ref yMax);
        PointBounds(p[2], ref xMin, ref yMin, ref xMax, ref yMax);
        var bot = (p[1] - p[0]) - (p[2] - p[1]);
        if (bot.x != 0)
        {
            double param = (p[1].x - p[0].x) / bot.x;
            if (param > 0 && param < 1)
                PointBounds(Point(param), ref xMin, ref yMin, ref xMax, ref yMax);
        }
        if (bot.y != 0)
        {
            double param = (p[1].y - p[0].y) / bot.y;
            if (param > 0 && param < 1)
                PointBounds(Point(param), ref xMin, ref yMin, ref xMax, ref yMax);
        }
    }

    public override void Reverse()
    {
        (p[0], p[2]) = (p[2], p[0]);
    }

    public override void SplitInThirds(out EdgeSegment part0, out EdgeSegment part1, out EdgeSegment part2)
    {
        part0 = new QuadraticSegment(p[0], VecMix(p[0], p[1], 1.0 / 3.0), Point(1.0 / 3.0), color);
        part1 = new QuadraticSegment(Point(1.0 / 3.0), VecMix(VecMix(p[0], p[1], 5.0 / 9.0), VecMix(p[1], p[2], 4.0 / 9.0), 0.5), Point(2.0 / 3.0), color);
        part2 = new QuadraticSegment(Point(2.0 / 3.0), VecMix(p[1], p[2], 2.0 / 3.0), p[2], color);
    }

    public EdgeSegment ConvertToCubic()
    {
        return new CubicSegment(p[0], VecMix(p[0], p[1], 2.0 / 3.0), VecMix(p[1], p[2], 1.0 / 3.0), p[2], color);
    }
}

internal class CubicSegment : EdgeSegment
{
    public Vector2Double[] p = new Vector2Double[4];

    public CubicSegment(Vector2Double p0, Vector2Double p1, Vector2Double p2, Vector2Double p3, EdgeColor edgeColor = EdgeColor.WHITE) : base(edgeColor)
    {
        p[0] = p0;
        p[1] = p1;
        p[2] = p2;
        p[3] = p3;
    }

    public override EdgeSegment Clone() => new CubicSegment(p[0], p[1], p[2], p[3], color);

    public override Vector2Double Point(double param)
    {
        var p12 = VecMix(p[1], p[2], param);
        return VecMix(VecMix(VecMix(p[0], p[1], param), p12, param), VecMix(p12, VecMix(p[2], p[3], param), param), param);
    }

    public override Vector2Double Direction(double param)
    {
        var tangent = VecMix(VecMix(p[1] - p[0], p[2] - p[1], param), VecMix(p[2] - p[1], p[3] - p[2], param), param);
        if (tangent.x == 0 && tangent.y == 0)
        {
            if (param == 0) return p[2] - p[0];
            if (param == 1) return p[3] - p[1];
        }
        return tangent;
    }

    public override SignedDistance GetSignedDistance(Vector2Double origin, out double param)
    {
        var qa = p[0] - origin;
        var ab = p[1] - p[0];
        var br = p[2] - p[1] - ab;
        var @as = (p[3] - p[2]) - (p[2] - p[1]) - br;

        var epDir = Direction(0);
        double minDistance = NonZeroSign(Cross(epDir, qa)) * Length(qa);
        param = -Dot(qa, epDir) / Dot(epDir, epDir);
        {
            double distance = Length(p[3] - origin);
            if (distance < Math.Abs(minDistance))
            {
                epDir = Direction(1);
                minDistance = NonZeroSign(Cross(epDir, p[3] - origin)) * distance;
                param = Dot(epDir - (p[3] - origin), epDir) / Dot(epDir, epDir);
            }
        }
        for (int i = 0; i <= CUBIC_SEARCH_STARTS; ++i)
        {
            double t = 1.0 / CUBIC_SEARCH_STARTS * i;
            var qe = qa + 3 * t * ab + 3 * t * t * br + t * t * t * @as;
            var d1 = 3 * ab + 6 * t * br + 3 * t * t * @as;
            var d2 = 6 * br + 6 * t * @as;
            double improvedT = t - Dot(qe, d1) / (Dot(d1, d1) + Dot(qe, d2));
            if (improvedT > 0 && improvedT < 1)
            {
                int remainingSteps = CUBIC_SEARCH_STEPS;
                do
                {
                    t = improvedT;
                    qe = qa + 3 * t * ab + 3 * t * t * br + t * t * t * @as;
                    d1 = 3 * ab + 6 * t * br + 3 * t * t * @as;
                    if (--remainingSteps == 0)
                        break;
                    d2 = 6 * br + 6 * t * @as;
                    improvedT = t - Dot(qe, d1) / (Dot(d1, d1) + Dot(qe, d2));
                } while (improvedT > 0 && improvedT < 1);
                double distance = Length(qe);
                if (distance < Math.Abs(minDistance))
                {
                    minDistance = NonZeroSign(Cross(d1, qe)) * distance;
                    param = t;
                }
            }
        }

        if (param >= 0 && param <= 1)
            return new SignedDistance(minDistance, 0);
        if (param < 0.5)
            return new SignedDistance(minDistance, Math.Abs(Dot(Normalize(Direction(0)), Normalize(qa))));
        else
            return new SignedDistance(minDistance, Math.Abs(Dot(Normalize(Direction(1)), Normalize(p[3] - origin))));
    }

    public override int ScanlineIntersections(Span<double> x, Span<int> dy, double y)
    {
        int total = 0;
        int nextDY = y > p[0].y ? 1 : -1;
        x[total] = p[0].x;
        if (p[0].y == y)
        {
            if (p[0].y < p[1].y || (p[0].y == p[1].y && (p[0].y < p[2].y || (p[0].y == p[2].y && p[0].y < p[3].y))))
                dy[total++] = 1;
            else
                nextDY = 1;
        }
        {
            var ab2 = p[1] - p[0];
            var br2 = p[2] - p[1] - ab2;
            var as2 = (p[3] - p[2]) - (p[2] - p[1]) - br2;
            Span<double> t = stackalloc double[3];
            int solutions = SolveCubic(t, as2.y, 3 * br2.y, 3 * ab2.y, p[0].y - y);
            if (solutions >= 2)
            {
                if (t[0] > t[1]) (t[0], t[1]) = (t[1], t[0]);
                if (solutions >= 3 && t[1] > t[2])
                {
                    (t[1], t[2]) = (t[2], t[1]);
                    if (t[0] > t[1]) (t[0], t[1]) = (t[1], t[0]);
                }
            }
            for (int i = 0; i < solutions && total < 3; ++i)
            {
                if (t[i] >= 0 && t[i] <= 1)
                {
                    x[total] = p[0].x + 3 * t[i] * ab2.x + 3 * t[i] * t[i] * br2.x + t[i] * t[i] * t[i] * as2.x;
                    if (nextDY * (ab2.y + 2 * t[i] * br2.y + t[i] * t[i] * as2.y) >= 0)
                    {
                        dy[total++] = nextDY;
                        nextDY = -nextDY;
                    }
                }
            }
        }
        if (p[3].y == y)
        {
            if (nextDY > 0 && total > 0)
            {
                --total;
                nextDY = -1;
            }
            if ((p[3].y < p[2].y || (p[3].y == p[2].y && (p[3].y < p[1].y || (p[3].y == p[1].y && p[3].y < p[0].y)))) && total < 3)
            {
                x[total] = p[3].x;
                if (nextDY < 0)
                {
                    dy[total++] = -1;
                    nextDY = 1;
                }
            }
        }
        if (nextDY != (y >= p[3].y ? 1 : -1))
        {
            if (total > 0)
                --total;
            else
            {
                if (Math.Abs(p[3].y - y) < Math.Abs(p[0].y - y))
                    x[total] = p[3].x;
                dy[total++] = nextDY;
            }
        }
        return total;
    }

    public override void Bound(ref double xMin, ref double yMin, ref double xMax, ref double yMax)
    {
        PointBounds(p[0], ref xMin, ref yMin, ref xMax, ref yMax);
        PointBounds(p[3], ref xMin, ref yMin, ref xMax, ref yMax);
        var a0 = p[1] - p[0];
        var a1 = 2 * (p[2] - p[1] - a0);
        var a2 = p[3] - 3 * p[2] + 3 * p[1] - p[0];
        Span<double> parms = stackalloc double[2];
        int solutions;
        solutions = SolveQuadratic(parms, a2.x, a1.x, a0.x);
        for (int i = 0; i < solutions; ++i)
            if (parms[i] > 0 && parms[i] < 1)
                PointBounds(Point(parms[i]), ref xMin, ref yMin, ref xMax, ref yMax);
        solutions = SolveQuadratic(parms, a2.y, a1.y, a0.y);
        for (int i = 0; i < solutions; ++i)
            if (parms[i] > 0 && parms[i] < 1)
                PointBounds(Point(parms[i]), ref xMin, ref yMin, ref xMax, ref yMax);
    }

    public override void Reverse()
    {
        (p[0], p[3]) = (p[3], p[0]);
        (p[1], p[2]) = (p[2], p[1]);
    }

    public override void SplitInThirds(out EdgeSegment part0, out EdgeSegment part1, out EdgeSegment part2)
    {
        part0 = new CubicSegment(p[0],
            p[0] == p[1] ? p[0] : VecMix(p[0], p[1], 1.0 / 3.0),
            VecMix(VecMix(p[0], p[1], 1.0 / 3.0), VecMix(p[1], p[2], 1.0 / 3.0), 1.0 / 3.0),
            Point(1.0 / 3.0), color);
        part1 = new CubicSegment(Point(1.0 / 3.0),
            VecMix(VecMix(VecMix(p[0], p[1], 1.0 / 3.0), VecMix(p[1], p[2], 1.0 / 3.0), 1.0 / 3.0), VecMix(VecMix(p[1], p[2], 1.0 / 3.0), VecMix(p[2], p[3], 1.0 / 3.0), 1.0 / 3.0), 2.0 / 3.0),
            VecMix(VecMix(VecMix(p[0], p[1], 2.0 / 3.0), VecMix(p[1], p[2], 2.0 / 3.0), 2.0 / 3.0), VecMix(VecMix(p[1], p[2], 2.0 / 3.0), VecMix(p[2], p[3], 2.0 / 3.0), 2.0 / 3.0), 1.0 / 3.0),
            Point(2.0 / 3.0), color);
        part2 = new CubicSegment(Point(2.0 / 3.0),
            VecMix(VecMix(p[1], p[2], 2.0 / 3.0), VecMix(p[2], p[3], 2.0 / 3.0), 2.0 / 3.0),
            p[2] == p[3] ? p[3] : VecMix(p[2], p[3], 2.0 / 3.0),
            p[3], color);
    }
}
