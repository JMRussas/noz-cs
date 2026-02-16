//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System;
using System.Threading.Tasks;

namespace NoZ.Editor
{
    partial class MSDF
    {
        public static void RenderGlyph(
            TrueTypeFont.Glyph glyph,
            PixelData<byte> output,
            Vector2Int outputPosition,
            Vector2Int outputSize,
            double range,
            in Vector2Double scale,
            in Vector2Double translate
            )
        {
            GenerateSDF(
                output,
                outputPosition,
                outputSize,
                Shape.FromGlyph(glyph, true),
                range,
                scale,
                translate
            );
        }

        // Compute winding number contribution from a contour using analytical ray casting.
        // Quadratic edges are decomposed into y-monotone segments so that the same
        // half-open y-interval rule used for linear edges applies consistently.
        private static int ComputeWindingNumber(Contour contour, Vector2Double p)
        {
            int winding = 0;

            foreach (var edge in contour.edges)
            {
                if (edge is LinearEdge line)
                {
                    double y0 = line.p0.y, y1 = line.p1.y;
                    if (y0 <= p.y)
                    {
                        if (y1 > p.y)
                        {
                            double t = (p.y - y0) / (y1 - y0);
                            if (line.p0.x + t * (line.p1.x - line.p0.x) > p.x)
                                winding++;
                        }
                    }
                    else if (y1 <= p.y)
                    {
                        double t = (p.y - y0) / (y1 - y0);
                        if (line.p0.x + t * (line.p1.x - line.p0.x) > p.x)
                            winding--;
                    }
                }
                else if (edge is QuadraticEdge q)
                {
                    // Decompose into y-monotone segments by splitting at the y-extremum.
                    // This ensures consistent half-open interval handling with linear edges.
                    double ay = q.p0.y - 2 * q.p1.y + q.p2.y;

                    if (Math.Abs(ay) > 1e-12)
                    {
                        double tExt = (q.p0.y - q.p1.y) / ay;
                        if (tExt > 0 && tExt < 1)
                        {
                            // Non-monotone: split at y-extremum and process two pieces
                            double yExt = QuadY(q, tExt);
                            QuadMonoCrossing(q, p, 0, tExt, q.p0.y, yExt, ref winding);
                            QuadMonoCrossing(q, p, tExt, 1, yExt, q.p2.y, ref winding);
                            continue;
                        }
                    }

                    // Already y-monotone
                    QuadMonoCrossing(q, p, 0, 1, q.p0.y, q.p2.y, ref winding);
                }
            }

            return winding;
        }

        private static double QuadY(QuadraticEdge q, double t)
        {
            double omt = 1 - t;
            return omt * omt * q.p0.y + 2 * omt * t * q.p1.y + t * t * q.p2.y;
        }

        // Check for a ray crossing within a y-monotone quadratic segment [tA, tB].
        // Uses the same half-open interval rule as linear edges (include lower y, exclude upper y).
        private static void QuadMonoCrossing(
            QuadraticEdge q, Vector2Double p,
            double tA, double tB, double yA, double yB,
            ref int winding)
        {
            int dir;
            if (yA <= p.y && yB > p.y)
                dir = 1;  // upward
            else if (yA > p.y && yB <= p.y)
                dir = -1; // downward
            else
                return;

            // Solve (y0 - 2*y1 + y2)*t^2 + 2*(y1-y0)*t + (y0-py) = 0
            double a = q.p0.y - 2 * q.p1.y + q.p2.y;
            double b = 2 * (q.p1.y - q.p0.y);
            double c = q.p0.y - p.y;

            double t;
            if (Math.Abs(a) < 1e-12)
            {
                t = (Math.Abs(b) > 1e-12) ? -c / b : (tA + tB) * 0.5;
            }
            else
            {
                double disc = b * b - 4 * a * c;
                if (disc < 0) disc = 0;
                double sqrtDisc = Math.Sqrt(disc);
                double inv2a = 0.5 / a;
                double t1 = (-b + sqrtDisc) * inv2a;
                double t2 = (-b - sqrtDisc) * inv2a;

                // Pick the root that falls within [tA, tB]
                t = (t1 >= tA - 1e-6 && t1 <= tB + 1e-6) ? t1 : t2;
                t = Math.Clamp(t, tA, tB);
            }

            double omt = 1 - t;
            double xAt = omt * omt * q.p0.x + 2 * omt * t * q.p1.x + t * t * q.p2.x;
            if (xAt > p.x)
                winding += dir;
        }

        // Compute the total winding number across all contours at point p.
        private static int ComputeTotalWinding(Shape shape, Vector2Double p)
        {
            int total = 0;
            foreach (var contour in shape.contours)
                total += ComputeWindingNumber(contour, p);
            return total;
        }

        private static void GenerateSDF(
            PixelData<byte> output,
            Vector2Int outputPosition,
            Vector2Int outputSize,
            Shape? shape,
            double range,
            Vector2Double scale,
            Vector2Double translate)
        {
            if (shape == null)
                return;

            int w = outputSize.X;
            int h = outputSize.Y;

            // Collect all edges from all contours into a flat list for distance queries.
            int totalEdges = 0;
            foreach (var contour in shape.contours)
                totalEdges += contour.edges.Length;
            var allEdges = new Edge[totalEdges];
            int ei = 0;
            foreach (var contour in shape.contours)
                foreach (var edge in contour.edges)
                    allEdges[ei++] = edge;

            Parallel.For(0, h, y =>
            {
                int row = shape.InverseYAxis ? h - y - 1 : y;

                for (int x = 0; x < w; ++x)
                {
                    double dummy = 0;
                    Vector2Double p = new Vector2Double(x + .5, y + .5) / scale - translate;

                    // Use winding number for inside/outside (handles all overlaps correctly)
                    bool inside = ComputeTotalWinding(shape, p) != 0;

                    // Find the minimum absolute distance to any edge
                    double minAbsDist = double.PositiveInfinity;
                    foreach (var edge in allEdges)
                    {
                        double absDist = Math.Abs(edge.GetSignedDistance(p, out dummy).distance);
                        if (absDist < minAbsDist)
                            minAbsDist = absDist;
                    }

                    // For inside pixels, check if the nearest edge is an internal overlap edge
                    // by testing the winding at a point nudged toward the nearest edge. If moving
                    // toward the nearest edge doesn't eventually cross outside, the edge is internal
                    // and we should use the distance to the next-closest boundary edge instead.
                    if (inside && minAbsDist < range)
                    {
                        // Sample a point just past the nearest edge (1 pixel beyond)
                        // If it's still inside, the nearest edge is internal — find the real boundary
                        double probeDistance = minAbsDist + 1.0;
                        double bestBoundaryDist = double.PositiveInfinity;

                        foreach (var edge in allEdges)
                        {
                            var sd = edge.GetSignedDistance(p, out double param);
                            double absDist = Math.Abs(sd.distance);

                            // Get the direction to this edge
                            double clampedParam = Math.Clamp(param, 0, 1);
                            Vector2Double edgePoint = edge.GetPoint(clampedParam);
                            Vector2Double toEdge = edgePoint - p;
                            double toEdgeMag = toEdge.Magnitude;

                            if (toEdgeMag < 1e-10)
                            {
                                // We're on the edge — this is a real boundary
                                bestBoundaryDist = 0;
                                break;
                            }

                            // Probe a point slightly past this edge
                            Vector2Double probeDir = toEdge * (1.0 / toEdgeMag);
                            Vector2Double probe = p + probeDir * (toEdgeMag + 0.5);
                            bool probeInside = ComputeTotalWinding(shape, probe) != 0;

                            // If the probe is outside, this edge is a real boundary
                            if (!probeInside && absDist < bestBoundaryDist)
                                bestBoundaryDist = absDist;
                        }

                        if (bestBoundaryDist < double.PositiveInfinity)
                            minAbsDist = bestBoundaryDist;
                    }

                    double finalSD = inside ? minAbsDist : -minAbsDist;

                    // Set the SDF value in the output image (R8 format)
                    finalSD /= (range * 2.0f);
                    finalSD = Math.Clamp(finalSD, -0.5, 0.5) + 0.5;

                    output.Set(
                        x + outputPosition.X,
                        row + outputPosition.Y,
                        (byte)(finalSD * 255.0f));
                }
            });
        }
    }
}
