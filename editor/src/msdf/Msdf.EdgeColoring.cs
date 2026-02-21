//
//  Faithful port of msdfgen by Viktor Chlumsky
//  https://github.com/Chlumsky/msdfgen
//

using System;
using System.Collections.Generic;
using static NoZ.Editor.Msdf.MsdfMath;

namespace NoZ.Editor.Msdf;

internal static class EdgeColoring
{
    private const int EDGE_LENGTH_PRECISION = 4;

    private static bool IsCorner(Vector2Double aDir, Vector2Double bDir, double crossThreshold)
    {
        return Dot(aDir, bDir) <= 0 || Math.Abs(Cross(aDir, bDir)) > crossThreshold;
    }

    private static int SeedExtract2(ref ulong seed)
    {
        int v = (int)(seed) & 1;
        seed >>= 1;
        return v;
    }

    private static int SeedExtract3(ref ulong seed)
    {
        int v = (int)(seed % 3);
        seed /= 3;
        return v;
    }

    private static EdgeColor InitColor(ref ulong seed)
    {
        EdgeColor[] colors = [EdgeColor.CYAN, EdgeColor.MAGENTA, EdgeColor.YELLOW];
        return colors[SeedExtract3(ref seed)];
    }

    private static void SwitchColor(ref EdgeColor color, ref ulong seed)
    {
        int shifted = (int)color << (1 + SeedExtract2(ref seed));
        color = (EdgeColor)((shifted | shifted >> 3) & (int)EdgeColor.WHITE);
    }

    private static void SwitchColor(ref EdgeColor color, ref ulong seed, EdgeColor banned)
    {
        var combined = (EdgeColor)((int)color & (int)banned);
        if (combined == EdgeColor.RED || combined == EdgeColor.GREEN || combined == EdgeColor.BLUE)
            color = (EdgeColor)((int)combined ^ (int)EdgeColor.WHITE);
        else
            SwitchColor(ref color, ref seed);
    }

    private static int SymmetricalTrichotomy(int position, int n)
    {
        return (int)(3 + 2.875 * position / (n - 1) - 1.4375 + 0.5) - 3;
    }

    private static double EstimateEdgeLength(EdgeSegment edge)
    {
        double len = 0;
        var prev = edge.Point(0);
        for (int i = 1; i <= EDGE_LENGTH_PRECISION; ++i)
        {
            var cur = edge.Point(1.0 / EDGE_LENGTH_PRECISION * i);
            len += Length(cur - prev);
            prev = cur;
        }
        return len;
    }

    /// <summary>
    /// Assigns edge colors with ink trap corner detection. Short edge segments between
    /// adjacent corners are treated as "minor" and given derived colors, preventing
    /// artifacts at sharp concave corners (like the V-junction of letter M).
    /// Port of msdfgen's edgeColoringInkTrap.
    /// </summary>
    public static void ColorInkTrap(Shape shape, double angleThreshold, ulong seed = 0)
    {
        double crossThreshold = Math.Sin(angleThreshold);
        var color = InitColor(ref seed);
        var corners = new List<InkTrapCorner>();

        foreach (var contour in shape.contours)
        {
            if (contour.edges.Count == 0)
                continue;

            corners.Clear();
            double splineLength = 0;

            // Identify corners with edge length tracking
            var prevDirection = contour.edges[^1].Direction(1);
            for (int index = 0; index < contour.edges.Count; ++index)
            {
                if (IsCorner(Normalize(prevDirection), Normalize(contour.edges[index].Direction(0)), crossThreshold))
                {
                    corners.Add(new InkTrapCorner { index = index, prevEdgeLengthEstimate = splineLength });
                    splineLength = 0;
                }
                splineLength += EstimateEdgeLength(contour.edges[index]);
                prevDirection = contour.edges[index].Direction(1);
            }

            // Smooth contour
            if (corners.Count == 0)
            {
                SwitchColor(ref color, ref seed);
                foreach (var edge in contour.edges)
                    edge.color = color;
            }
            // "Teardrop" case
            else if (corners.Count == 1)
            {
                EdgeColor[] colors = new EdgeColor[3];
                SwitchColor(ref color, ref seed);
                colors[0] = color;
                colors[1] = EdgeColor.WHITE;
                SwitchColor(ref color, ref seed);
                colors[2] = color;
                int corner = corners[0].index;
                if (contour.edges.Count >= 3)
                {
                    int m = contour.edges.Count;
                    for (int i = 0; i < m; ++i)
                        contour.edges[(corner + i) % m].color = colors[1 + SymmetricalTrichotomy(i, m)];
                }
                else if (contour.edges.Count >= 1)
                {
                    var parts = new EdgeSegment[7];
                    contour.edges[0].SplitInThirds(out parts[0 + 3 * corner], out parts[1 + 3 * corner], out parts[2 + 3 * corner]);
                    if (contour.edges.Count >= 2)
                    {
                        contour.edges[1].SplitInThirds(out parts[3 - 3 * corner], out parts[4 - 3 * corner], out parts[5 - 3 * corner]);
                        parts[0].color = parts[1].color = colors[0];
                        parts[2].color = parts[3].color = colors[1];
                        parts[4].color = parts[5].color = colors[2];
                    }
                    else
                    {
                        parts[0].color = colors[0];
                        parts[1].color = colors[1];
                        parts[2].color = colors[2];
                    }
                    contour.edges.Clear();
                    for (int i = 0; i < parts.Length && parts[i] != null; ++i)
                        contour.edges.Add(parts[i]);
                }
            }
            // Multiple corners — ink trap logic
            else
            {
                int cornerCount = corners.Count;
                int majorCornerCount = cornerCount;

                // Mark minor corners (short segments between adjacent corners)
                if (cornerCount > 3)
                {
                    corners[0] = corners[0] with { prevEdgeLengthEstimate = corners[0].prevEdgeLengthEstimate + splineLength };
                    for (int i = 0; i < cornerCount; ++i)
                    {
                        if (corners[i].prevEdgeLengthEstimate > corners[(i + 1) % cornerCount].prevEdgeLengthEstimate &&
                            corners[(i + 1) % cornerCount].prevEdgeLengthEstimate < corners[(i + 2) % cornerCount].prevEdgeLengthEstimate)
                        {
                            corners[(i + 1) % cornerCount] = corners[(i + 1) % cornerCount] with { minor = true };
                            --majorCornerCount;
                        }
                    }
                }

                // Assign colors to major corners
                var initialColor = EdgeColor.BLACK;
                for (int i = 0; i < cornerCount; ++i)
                {
                    if (!corners[i].minor)
                    {
                        --majorCornerCount;
                        SwitchColor(ref color, ref seed, (EdgeColor)(majorCornerCount == 0 ? (int)initialColor : 0));
                        corners[i] = corners[i] with { color = color };
                        if (initialColor == EdgeColor.BLACK)
                            initialColor = color;
                    }
                }

                // Derive colors for minor corners
                for (int i = 0; i < cornerCount; ++i)
                {
                    if (corners[i].minor)
                    {
                        var nextColor = corners[(i + 1) % cornerCount].color;
                        corners[i] = corners[i] with { color = (EdgeColor)((int)(color & nextColor) ^ (int)EdgeColor.WHITE) };
                    }
                    else
                    {
                        color = corners[i].color;
                    }
                }

                // Apply colors to edges
                int spline = 0;
                int start = corners[0].index;
                color = corners[0].color;
                int m = contour.edges.Count;
                for (int i = 0; i < m; ++i)
                {
                    int index = (start + i) % m;
                    if (spline + 1 < cornerCount && corners[spline + 1].index == index)
                        color = corners[++spline].color;
                    contour.edges[index].color = color;
                }
            }
        }
    }

    private struct InkTrapCorner
    {
        public int index;
        public double prevEdgeLengthEstimate;
        public bool minor;
        public EdgeColor color;
    }

    /// <summary>
    /// Assigns colors to edges of the shape in accordance to the multi-channel distance field technique.
    /// May split some edges if necessary.
    /// angleThreshold specifies the maximum angle (in radians) to be considered a corner, e.g. 3 (~172 degrees).
    /// </summary>
    public static void ColorSimple(Shape shape, double angleThreshold, ulong seed = 0)
    {
        double crossThreshold = Math.Sin(angleThreshold);
        var color = InitColor(ref seed);
        var corners = new List<int>();

        foreach (var contour in shape.contours)
        {
            if (contour.edges.Count == 0)
                continue;

            // Identify corners
            corners.Clear();
            var prevDirection = contour.edges[^1].Direction(1);
            for (int index = 0; index < contour.edges.Count; ++index)
            {
                if (IsCorner(Normalize(prevDirection), Normalize(contour.edges[index].Direction(0)), crossThreshold))
                    corners.Add(index);
                prevDirection = contour.edges[index].Direction(1);
            }

            // Smooth contour
            if (corners.Count == 0)
            {
                SwitchColor(ref color, ref seed);
                foreach (var edge in contour.edges)
                    edge.color = color;
            }
            // "Teardrop" case
            else if (corners.Count == 1)
            {
                EdgeColor[] colors = new EdgeColor[3];
                SwitchColor(ref color, ref seed);
                colors[0] = color;
                colors[1] = EdgeColor.WHITE;
                SwitchColor(ref color, ref seed);
                colors[2] = color;
                int corner = corners[0];
                if (contour.edges.Count >= 3)
                {
                    int m = contour.edges.Count;
                    for (int i = 0; i < m; ++i)
                        contour.edges[(corner + i) % m].color = colors[1 + SymmetricalTrichotomy(i, m)];
                }
                else if (contour.edges.Count >= 1)
                {
                    // Less than three edge segments for three colors => edges must be split
                    var parts = new EdgeSegment[7];
                    contour.edges[0].SplitInThirds(out parts[0 + 3 * corner], out parts[1 + 3 * corner], out parts[2 + 3 * corner]);
                    if (contour.edges.Count >= 2)
                    {
                        contour.edges[1].SplitInThirds(out parts[3 - 3 * corner], out parts[4 - 3 * corner], out parts[5 - 3 * corner]);
                        parts[0].color = parts[1].color = colors[0];
                        parts[2].color = parts[3].color = colors[1];
                        parts[4].color = parts[5].color = colors[2];
                    }
                    else
                    {
                        parts[0].color = colors[0];
                        parts[1].color = colors[1];
                        parts[2].color = colors[2];
                    }
                    contour.edges.Clear();
                    for (int i = 0; i < parts.Length && parts[i] != null; ++i)
                        contour.edges.Add(parts[i]);
                }
            }
            // Multiple corners
            else
            {
                int cornerCount = corners.Count;
                int spline = 0;
                int start = corners[0];
                int m = contour.edges.Count;
                SwitchColor(ref color, ref seed);
                var initialColor = color;
                for (int i = 0; i < m; ++i)
                {
                    int index = (start + i) % m;
                    if (spline + 1 < cornerCount && corners[spline + 1] == index)
                    {
                        ++spline;
                        SwitchColor(ref color, ref seed, (EdgeColor)((spline == cornerCount - 1 ? 1 : 0) * (int)initialColor));
                    }
                    contour.edges[index].color = color;
                }
            }
        }
    }
}
