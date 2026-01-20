//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ.Editor;

partial class MSDF
{
    private class Shape
    {
        public Contour[] contours = null!;

        public bool InverseYAxis {
            get; set;
        } = false;

        private bool Validate()
        {
            foreach (var contour in contours)
            {
                if (contour.edges.Length == 0)
                    continue;

                Vector2Double corner = contour.edges.Last().GetPoint(1.0);
                foreach (var edge in contour.edges)
                {
                    var compare = edge.GetPoint(0.0);
                    if (compare != corner)
                        return false;

                    corner = edge.GetPoint(1.0);
                }
            }

            return true;
        }

        private void Normalize()
        {
            foreach (var contour in contours)
            {
                if (contour.edges.Length != 1)
                    continue;

                contour.edges[0].SplitInThirds(out var part1, out var part2, out var part3);
                contour.edges = new Edge[3];
                contour.edges[0] = part1;
                contour.edges[1] = part2;
                contour.edges[2] = part3;
            }
        }

        public void Bounds(ref double l, ref double b, ref double r, ref double t)
        {
            foreach (var contour in contours)
            {
                contour.Bounds(ref l, ref b, ref r, ref t);
            }
        }

        public static Shape? FromGlyph(TrueTypeFont.Glyph glyph, bool invertYAxis)
        {
            if (null == glyph)
                return null;

            var shape = new Shape() { contours = new Contour[glyph.contours.Length] };

            for (int i = 0; i < glyph.contours.Length; i++)
            {
                ref var glyphContour = ref glyph.contours[i];

                List<Edge> edges = new List<Edge>();

                Vector2Double last = glyph.points[glyphContour.start].xy;
                Vector2Double start = last;

                for (int p = 1; p < glyphContour.length;)
                {
                    ref var glyphPoint = ref glyph.points[glyphContour.start + p++];

                    // Quadratic edge?
                    if (glyphPoint.curve == TrueTypeFont.CurveType.Conic)
                    {
                        var control = glyphPoint.xy;

                        for (; p < glyphContour.length;)
                        {
                            glyphPoint = ref glyph.points[glyphContour.start + p++];

                            if (glyphPoint.curve != TrueTypeFont.CurveType.Conic)
                            {
                                edges.Add(new QuadraticEdge(
                                    new Vector2Double(last.x, last.y),
                                    new Vector2Double(control.x, control.y),
                                    new Vector2Double(glyphPoint.xy.x, glyphPoint.xy.y)
                                    ));
                                last = glyphPoint.xy;
                                break;
                            }

                            var middle = new Vector2Double((control.x + glyphPoint.xy.x) / 2, (control.y + glyphPoint.xy.y) / 2);

                            edges.Add(new QuadraticEdge(
                                new Vector2Double(last.x, last.y),
                                new Vector2Double(control.x, control.y),
                                new Vector2Double(middle.x, middle.y)
                                ));

                            last = middle;
                            control = glyphPoint.xy;
                        }

                        if (p == glyphContour.length)
                        {
                            if (glyph.points[glyphContour.start + glyphContour.length - 1].curve == TrueTypeFont.CurveType.Conic)
                            {
                                edges.Add(new QuadraticEdge(
                                    new Vector2Double(last.x, last.y),
                                    new Vector2Double(control.x, control.y),
                                    new Vector2Double(start.x, start.y)
                                    ));
                            }
                            else
                            {
                                edges.Add(new LinearEdge(
                                    new Vector2Double(last.x, last.y),
                                    new Vector2Double(start.x, start.y)
                                    ));
                            }
                        }


                        // Linear edge..
                    }
                    else
                    {
                        edges.Add(new LinearEdge(
                            new Vector2Double(last.x, last.y),
                            new Vector2Double(glyphPoint.xy.x, glyphPoint.xy.y)
                            ));


                        last = glyphPoint.xy;

                        // If we ended on a linear then finish on a linear
                        if (p == glyphContour.length)
                            edges.Add(new LinearEdge(
                                new Vector2Double(last.x, last.y),
                                new Vector2Double(start.x, start.y)
                            ));
                    }
                }

                shape.contours[i] = new Contour { edges = edges.ToArray() };
            }

            if (!shape.Validate())
                throw new ImportException("Invalid shape data in glyph");

            shape.Normalize();
            shape.InverseYAxis = invertYAxis;

            return shape;
        }
    }
}
