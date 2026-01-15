//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System;

namespace NoZ.Editor
{
    partial class MSDF
    {
        enum EdgeColor
        {
            White
        }

        abstract class Edge
        {
            public EdgeColor color;

            public Edge(EdgeColor color)
            {
                this.color = color;
            }

            public abstract Vector2Double GetPoint(double mix);
            public abstract void SplitInThirds(out Edge part1, out Edge part2, out Edge part3);
            public abstract void Bounds(ref double l, ref double b, ref double r, ref double t);
            public abstract SignedDistance GetSignedDistance(Vector2Double origin, out double param);

            protected static void Bounds(Vector2Double p, ref double l, ref double b, ref double r, ref double t)
            {
                if (p.x < l)
                    l = p.x;
                if (p.y < b)
                    b = p.y;
                if (p.x > r)
                    r = p.x;
                if (p.y > t)
                    t = p.y;
            }
        }

        class LinearEdge : Edge
        {
            public Vector2Double p0;
            public Vector2Double p1;

            public LinearEdge(Vector2Double p0, Vector2Double p1) : this(p0, p1, EdgeColor.White)
            {
            }

            public LinearEdge(Vector2Double p0, Vector2Double p1, EdgeColor color) : base(color)
            {
                this.p0 = p0;
                this.p1 = p1;
            }

            public override Vector2Double GetPoint(double mix)
            {
                return Vector2Double.Mix(p0, p1, mix);
            }

            public override void SplitInThirds(out Edge part1, out Edge part2, out Edge part3)
            {
                part1 = new LinearEdge(p0, GetPoint(1 / 3.0), color);
                part2 = new LinearEdge(GetPoint(1 / 3.0), GetPoint(2 / 3.0), color);
                part3 = new LinearEdge(GetPoint(2 / 3.0), p1, color);
            }

            public override void Bounds(ref double l, ref double b, ref double r, ref double t)
            {
                Bounds(p0, ref l, ref b, ref r, ref t);
                Bounds(p1, ref l, ref b, ref r, ref t);
            }

            public override SignedDistance GetSignedDistance(Vector2Double origin, out double param)
            {
                Vector2Double aq = origin - p0;
                Vector2Double ab = p1 - p0;
                param = Vector2Double.Dot(aq, ab) / Vector2Double.Dot(ab, ab);
                Vector2Double eq = (param > 0.5 ? p1 : p0) - origin;
                double endpointDistance = eq.Magnitude;
                if (param > 0 && param < 1)
                {
                    double orthoDistance = Vector2Double.Dot(ab.OrthoNormalize(false), aq);
                    if (Math.Abs(orthoDistance) < endpointDistance)
                        return new SignedDistance(orthoDistance, 0);
                }
                return new SignedDistance(
                    NonZeroSign(Vector2Double.Cross(aq, ab)) * endpointDistance,
                    Math.Abs(Vector2Double.Dot(ab.Normalized, eq.Normalized))
                );
            }
        }

        class QuadraticEdge : Edge
        {
            public Vector2Double p0;
            public Vector2Double p1;
            public Vector2Double p2;

            public QuadraticEdge(Vector2Double p0, Vector2Double p1, Vector2Double p2) : this(p0, p1, p2, EdgeColor.White)
            {
            }

            public QuadraticEdge(Vector2Double p0, Vector2Double p1, Vector2Double p2, EdgeColor color) : base(color)
            {
                if (p1 == p0 || p1 == p2)
                    p1 = 0.5 * (p0 + p2);

                this.p0 = p0;
                this.p1 = p1;
                this.p2 = p2;
            }

            public override Vector2Double GetPoint(double mix)
            {
                return Vector2Double.Mix(
                    Vector2Double.Mix(p0, p1, mix),
                    Vector2Double.Mix(p1, p2, mix),
                    mix);
            }

            public override void SplitInThirds(out Edge part1, out Edge part2, out Edge part3)
            {
                part1 = new QuadraticEdge(p0, Vector2Double.Mix(p0, p1, 1 / 3.0), GetPoint(1 / 3.0), color);
                part2 = new QuadraticEdge(GetPoint(1 / 3.0), Vector2Double.Mix(Vector2Double.Mix(p0, p1, 5 / 9.0), Vector2Double.Mix(p1, p2, 4 / 9.0), .5), GetPoint(2 / 3.0), color);
                part3 = new QuadraticEdge(GetPoint(2 / 3.0), Vector2Double.Mix(p1, p2, 2 / 3.0), p2, color);
            }

            public override void Bounds(ref double l, ref double b, ref double r, ref double t)
            {
                Bounds(p0, ref l, ref b, ref r, ref t);
                Bounds(p2, ref l, ref b, ref r, ref t);

                Vector2Double bot = (p1 - p0) - (p2 - p1);
                if (bot.x != 0.0)
                {
                    double param = (p1.x - p0.x) / bot.x;
                    if (param > 0 && param < 1)
                        Bounds(GetPoint(param), ref l, ref b, ref r, ref t);
                }
                if (bot.y != 0.0)
                {
                    double param = (p1.y - p0.y) / bot.y;
                    if (param > 0 && param < 1)
                        Bounds(GetPoint(param), ref l, ref b, ref r, ref t);
                }
            }


            public override SignedDistance GetSignedDistance(Vector2Double origin, out double param)
            {
                var qa = p0 - origin;
                var ab = p1 - p0;
                var br = p0 + p2 - p1 - p1;
                double a = Vector2Double.Dot(br, br);
                double b = 3.0 * Vector2Double.Dot(ab, br);
                double c = 2.0 * Vector2Double.Dot(ab, ab) + Vector2Double.Dot(qa, br);
                double d = Vector2Double.Dot(qa, ab);
                double t0 = 0;
                double t1 = 0;
                double t2 = 0;
                int solutions = SolveCubic(ref t0, ref t1, ref t2, a, b, c, d);

                double minDistance = NonZeroSign(Vector2Double.Cross(ab, qa)) * qa.Magnitude; // distance from A
                param = -Vector2Double.Dot(qa, ab) / Vector2Double.Dot(ab, ab);
                {
                    double distance = NonZeroSign(Vector2Double.Cross(p2 - p1, p2 - origin)) * (p2 - origin).Magnitude; // distance from B
                    if (Math.Abs(distance) < Math.Abs(minDistance))
                    {
                        minDistance = distance;
                        param = Vector2Double.Dot(origin - p1, p2 - p1) / Vector2Double.Dot(p2 - p1, p2 - p1);
                    }
                }

                double ApplySolution(double t, double oldSolution)
                {
                    if (t > 0 && t < 1)
                    {
                        Vector2Double endpoint = p0 + 2 * t * ab + t * t * br;
                        double distance = NonZeroSign(Vector2Double.Cross(p2 - p0, endpoint - origin)) * (endpoint - origin).Magnitude;
                        if (Math.Abs(distance) <= Math.Abs(minDistance))
                        {
                            minDistance = distance;
                            return t;
                        }
                    }

                    return oldSolution;
                }

                if (solutions > 0) param = ApplySolution(t0, param);
                if (solutions > 1) param = ApplySolution(t1, param);
                if (solutions > 2) param = ApplySolution(t2, param);

                if (param >= 0 && param <= 1)
                    return new SignedDistance(minDistance, 0);
                if (param < .5)
                    return new SignedDistance(minDistance, Math.Abs(Vector2Double.Dot(ab.Normalized, qa.Normalized)));

                return new SignedDistance(minDistance, Math.Abs(Vector2Double.Dot((p2 - p1).Normalized, (p2 - origin).Normalized)));
            }
        }
    }
}
