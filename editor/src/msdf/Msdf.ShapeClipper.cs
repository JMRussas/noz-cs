//
//  Boolean operations on MSDF shapes using Clipper2 before MSDF generation.
//

using Clipper2Lib;

namespace NoZ.Editor.Msdf;

internal static class ShapeClipper
{
    const int DefaultStepsPerCurve = 8;
    const int ClipperPrecision = 6;

    /// <summary>
    /// Boolean-union all contours of a shape, producing a new shape
    /// with non-overlapping contours (all linear edges).
    /// Curves are flattened to polylines before the union.
    /// </summary>
    public static Shape Union(Shape shape, int stepsPerCurve = DefaultStepsPerCurve)
    {
        if (shape.contours.Count == 0)
            return shape;

        var paths = ShapeToPaths(shape, stepsPerCurve);
        if (paths.Count == 0)
            return shape;

        // Perform boolean union with non-zero fill rule
        var tree = new PolyTreeD();
        Clipper.BooleanOp(ClipType.Union, paths, null, tree, FillRule.NonZero, ClipperPrecision);

        return TreeToShape(tree, shape) ?? shape;
    }

    /// <summary>
    /// Boolean-difference: subject minus clip. Produces a new shape with
    /// the clip regions carved out of the subject (all linear edges).
    /// </summary>
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

    /// <summary>
    /// Flatten all contours of a shape into Clipper2 PathsD.
    /// </summary>
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

    /// <summary>
    /// Convert a PolyTree result back to a Shape with reversed winding.
    /// Returns null if the tree produced no contours.
    /// </summary>
    private static Shape? TreeToShape(PolyTreeD tree, Shape reference)
    {
        var result = new Shape();
        result.inverseYAxis = reference.inverseYAxis;
        CollectContours(tree, result);

        if (result.contours.Count == 0)
            return null;

        // Clipper2's winding convention is opposite to our MSDF generator's
        // convention (Clipper2 positive area = our negative winding).
        // Reverse all contours to match what the generator expects.
        foreach (var contour in result.contours)
            contour.Reverse();

        return result;
    }

    /// <summary>
    /// Flatten a contour's edges into a Clipper2 PathD (polyline).
    /// </summary>
    private static PathD ContourToPath(Contour contour, int stepsPerCurve)
    {
        var path = new PathD();
        foreach (var edge in contour.edges)
        {
            switch (edge)
            {
                case LinearSegment lin:
                    // Add start point only; end is next edge's start
                    path.Add(new PointD(lin.p[0].x, lin.p[0].y));
                    break;

                case QuadraticSegment quad:
                    // Flatten: sample at uniform intervals, skip t=1 (next edge's start)
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
        // Clipper2 paths are implicitly closed (last->first)
        return path;
    }

    /// <summary>
    /// Recursively collect all polygons from a PolyTree into Shape contours.
    /// All contours are emitted as-is — Clipper2 already outputs outers and holes
    /// with opposite windings for non-zero fill rule.
    /// </summary>
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

        // Recurse into children
        for (int i = 0; i < node.Count; i++)
            CollectContours(node[i], shape);
    }
}
