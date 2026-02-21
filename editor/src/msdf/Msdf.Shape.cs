//
//  Faithful port of msdfgen by Viktor Chlumsky
//  https://github.com/Chlumsky/msdfgen
//

using System;
using System.Collections.Generic;
using static NoZ.Editor.Msdf.MsdfMath;

namespace NoZ.Editor.Msdf;

internal class Shape
{
    public List<Contour> contours = new();
    public bool inverseYAxis = false;

    public Contour AddContour()
    {
        var c = new Contour();
        contours.Add(c);
        return c;
    }

    public void AddContour(Contour contour) => contours.Add(contour);

    public bool Validate()
    {
        foreach (var contour in contours)
        {
            if (contour.edges.Count > 0)
            {
                var corner = contour.edges[^1].Point(1);
                foreach (var edge in contour.edges)
                {
                    if (edge.Point(0) != corner)
                        return false;
                    corner = edge.Point(1);
                }
            }
        }
        return true;
    }

    // Split single-edge contours into thirds so edge coloring has enough edges.
    public void Normalize()
    {
        foreach (var contour in contours)
        {
            if (contour.edges.Count == 1)
            {
                contour.edges[0].SplitInThirds(out var part0, out var part1, out var part2);
                contour.edges.Clear();
                contour.edges.Add(part0);
                contour.edges.Add(part1);
                contour.edges.Add(part2);
            }
        }
    }

    public void Bound(ref double xMin, ref double yMin, ref double xMax, ref double yMax)
    {
        foreach (var contour in contours)
            contour.Bound(ref xMin, ref yMin, ref xMax, ref yMax);
    }

    public int EdgeCount()
    {
        int total = 0;
        foreach (var contour in contours)
            total += contour.edges.Count;
        return total;
    }

    // Orient unoriented (even-odd) contours to conform to non-zero winding rule.
    public void OrientContours()
    {
        double ratio = 0.5 * (Math.Sqrt(5) - 1);
        var orientations = new int[contours.Count];
        Span<double> x = stackalloc double[3];
        Span<int> dy = stackalloc int[3];

        for (int i = 0; i < contours.Count; ++i)
        {
            if (orientations[i] != 0 || contours[i].edges.Count == 0)
                continue;

            double y0 = contours[i].edges[0].Point(0).y;
            double y1 = y0;
            foreach (var edge in contours[i].edges)
            {
                if (y0 != y1) break;
                y1 = edge.Point(1).y;
            }
            foreach (var edge in contours[i].edges)
            {
                if (y0 != y1) break;
                y1 = edge.Point(ratio).y;
            }
            double y = (1.0 - ratio) * y0 + ratio * y1;

            var intersections = new List<(double x, int direction, int contourIndex)>();

            for (int j = 0; j < contours.Count; ++j)
            {
                foreach (var edge in contours[j].edges)
                {
                    int n = edge.ScanlineIntersections(x, dy, y);
                    for (int k = 0; k < n; ++k)
                        intersections.Add((x[k], dy[k], j));
                }
            }

            if (intersections.Count > 0)
            {
                intersections.Sort((a, b) => a.x.CompareTo(b.x));

                for (int j = 1; j < intersections.Count; ++j)
                {
                    if (intersections[j].x == intersections[j - 1].x)
                    {
                        intersections[j] = (intersections[j].x, 0, intersections[j].contourIndex);
                        intersections[j - 1] = (intersections[j - 1].x, 0, intersections[j - 1].contourIndex);
                    }
                }

                for (int j = 0; j < intersections.Count; ++j)
                {
                    if (intersections[j].direction != 0)
                        orientations[intersections[j].contourIndex] += 2 * (((j & 1) ^ (intersections[j].direction > 0 ? 1 : 0)) != 0 ? 1 : 0) - 1;
                }
            }
        }

        for (int i = 0; i < contours.Count; ++i)
            if (orientations[i] < 0)
                contours[i].Reverse();
    }
}
