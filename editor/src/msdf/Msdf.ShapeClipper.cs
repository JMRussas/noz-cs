//
//  Clipper2 boolean operations on MSDF shapes.
//

using Clipper2Lib;

namespace NoZ.Editor.Msdf;

internal static class ShapeClipper
{
    const int DefaultStepsPerCurve = 8;
    const int ClipperPrecision = 6;

    // Boolean-union all contours, producing non-overlapping linear contours.
    public static Shape Union(Shape shape, int stepsPerCurve = DefaultStepsPerCurve)
    {
        if (shape.contours.Count == 0)
            return shape;

        var paths = ShapeToPaths(shape, stepsPerCurve);
        if (paths.Count == 0)
            return shape;

        var tree = new PolyTreeD();
        Clipper.BooleanOp(ClipType.Union, paths, null, tree, FillRule.NonZero, ClipperPrecision);

        return TreeToShape(tree, shape) ?? shape;
    }

    // Boolean-difference: subject minus clip.
    public static Shape Difference(Shape subject, Shape clip, int stepsPerCurve = DefaultStepsPerCurve)
    {
        if (subject.contours.Count == 0)
            return subject;
        if (clip.contours.Count == 0)
            return subject;

        var subjectPaths = ShapeToPaths(subject, stepsPerCurve);
        var clipPaths = ShapeToPaths(clip, stepsPerCurve);

        if (subjectPaths.Count == 0)
            return subject;
        if (clipPaths.Count == 0)
            return subject;

        var tree = new PolyTreeD();
        Clipper.BooleanOp(ClipType.Difference, subjectPaths, clipPaths, tree, FillRule.NonZero, ClipperPrecision);

        return TreeToShape(tree, subject) ?? subject;
    }

    private static PathsD ShapeToPaths(Shape shape, int stepsPerCurve)
    {
        var paths = new PathsD();
        foreach (var contour in shape.contours)
        {
            var path = ContourToPath(contour, stepsPerCurve);
            if (path.Count >= 3)
                paths.Add(path);
        }
        return paths;
    }

    // Reverse all contours: Clipper2 winding is opposite to our MSDF generator's.
    private static Shape? TreeToShape(PolyTreeD tree, Shape reference)
    {
        var result = new Shape();
        result.inverseYAxis = reference.inverseYAxis;
        CollectContours(tree, result);

        if (result.contours.Count == 0)
            return null;

        foreach (var contour in result.contours)
            contour.Reverse();

        return result;
    }

    private static PathD ContourToPath(Contour contour, int stepsPerCurve)
    {
        var path = new PathD();
        foreach (var edge in contour.edges)
        {
            switch (edge)
            {
                case LinearSegment lin:
                    path.Add(new PointD(lin.p[0].x, lin.p[0].y));
                    break;

                case QuadraticSegment quad:
                    for (int i = 0; i < stepsPerCurve; i++)
                    {
                        double t = (double)i / stepsPerCurve;
                        var p = quad.Point(t);
                        path.Add(new PointD(p.x, p.y));
                    }
                    break;

                case CubicSegment cub:
                    for (int i = 0; i < stepsPerCurve; i++)
                    {
                        double t = (double)i / stepsPerCurve;
                        var p = cub.Point(t);
                        path.Add(new PointD(p.x, p.y));
                    }
                    break;
            }
        }
        return path;
    }

    private static void CollectContours(PolyPathD node, Shape shape)
    {
        if (node.Polygon != null && node.Polygon.Count >= 3)
        {
            var contour = shape.AddContour();
            var poly = node.Polygon;
            for (int i = 0; i < poly.Count; i++)
            {
                int next = (i + 1) % poly.Count;
                contour.AddEdge(new LinearSegment(
                    new Vector2Double(poly[i].x, poly[i].y),
                    new Vector2Double(poly[next].x, poly[next].y)));
            }
        }

        for (int i = 0; i < node.Count; i++)
            CollectContours(node[i], shape);
    }

    // Ensure all contours wind in the same direction (positive).
    // Sprites have no holes, so all paths should be outer contours
    // with consistent winding for NonZero fill rule to union correctly.
    private static void NormalizeWindings(Shape shape)
    {
        foreach (var contour in shape.contours)
        {
            if (contour.Winding() < 0)
                contour.Reverse();
        }
    }
}
