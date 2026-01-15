//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System;
using System.Linq;

namespace NoZ.Editor
{
    partial class MSDF
    {
        private class Contour
        {
            public Edge[] edges;

            public void Bounds(ref double l, ref double b, ref double r, ref double t)
            {
                foreach (Edge edge in edges)
                    edge.Bounds(ref l, ref b, ref r, ref t);
            }

            public int Winding()
            {
                if (edges.Length == 0)
                    return 0;

                double total = 0;
                if (edges.Length == 1)
                {
                    var a = edges[0].GetPoint(0);
                    var b = edges[0].GetPoint(1 / 3.0);
                    var c = edges[0].GetPoint(2 / 3.0);
                    total += ShoeLace(a, b);
                    total += ShoeLace(b, c);
                    total += ShoeLace(c, a);
                }
                else if (edges.Length == 2)
                {
                    var a = edges[0].GetPoint(0);
                    var b = edges[0].GetPoint(0.5);
                    var c = edges[1].GetPoint(0);
                    var d = edges[1].GetPoint(.5);
                    total += ShoeLace(a, b);
                    total += ShoeLace(b, c);
                    total += ShoeLace(c, d);
                    total += ShoeLace(d, a);
                }
                else
                {
                    var prev = edges.Last().GetPoint(0);
                    foreach (var edge in edges)
                    {
                        var cur = edge.GetPoint(0);
                        total += ShoeLace(prev, cur);
                        prev = cur;
                    }
                }
                return Sign(total);
            }
        }
    }
}
