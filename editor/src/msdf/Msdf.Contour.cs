//
//  Faithful port of msdfgen by Viktor Chlumsky
//  https://github.com/Chlumsky/msdfgen
//

using System;
using System.Collections.Generic;
using static NoZ.Editor.Msdf.MsdfMath;

namespace NoZ.Editor.Msdf;

internal class Contour
{
    public List<EdgeSegment> edges = new();

    public void AddEdge(EdgeSegment edge) => edges.Add(edge);

    public void Bound(ref double xMin, ref double yMin, ref double xMax, ref double yMax)
    {
        foreach (var edge in edges)
            edge.Bound(ref xMin, ref yMin, ref xMax, ref yMax);
    }

    public int Winding()
    {
        if (edges.Count == 0)
            return 0;

        double total = 0;
        if (edges.Count == 1)
        {
            var a = edges[0].Point(0);
            var b = edges[0].Point(1.0 / 3.0);
            var c = edges[0].Point(2.0 / 3.0);
            total += Shoelace(a, b);
            total += Shoelace(b, c);
            total += Shoelace(c, a);
        }
        else if (edges.Count == 2)
        {
            var a = edges[0].Point(0);
            var b = edges[0].Point(0.5);
            var c = edges[1].Point(0);
            var d = edges[1].Point(0.5);
            total += Shoelace(a, b);
            total += Shoelace(b, c);
            total += Shoelace(c, d);
            total += Shoelace(d, a);
        }
        else
        {
            var prev = edges[^1].Point(0);
            foreach (var edge in edges)
            {
                var cur = edge.Point(0);
                total += Shoelace(prev, cur);
                prev = cur;
            }
        }
        return Sign(total);
    }

    public void Reverse()
    {
        for (int i = edges.Count / 2; i > 0; --i)
        {
            var tmp = edges[i - 1];
            edges[i - 1] = edges[edges.Count - i];
            edges[edges.Count - i] = tmp;
        }
        foreach (var edge in edges)
            edge.Reverse();
    }

    private static double Shoelace(Vector2Double a, Vector2Double b) => (b.x - a.x) * (a.y + b.y);
}
