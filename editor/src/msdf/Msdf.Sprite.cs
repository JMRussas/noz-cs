//
//  Bridge between NoZ sprite shapes and msdf MSDF generation.
//

using System;

namespace NoZ.Editor.Msdf;

internal static class MsdfSprite
{
    /// <summary>
    /// Convert NoZ sprite paths into an msdf Shape with edge coloring applied,
    /// ready for MSDF generation.
    /// </summary>
    public static Shape FromSpritePaths(
        NoZ.Editor.Shape spriteShape,
        ReadOnlySpan<ushort> pathIndices)
    {
        var shape = new Shape();

        foreach (var pathIndex in pathIndices)
        {
            if (pathIndex >= spriteShape.PathCount) continue;
            ref readonly var path = ref spriteShape.GetPath(pathIndex);
            if (path.AnchorCount < 3) continue;

            var contour = shape.AddContour();

            for (ushort a = 0; a < path.AnchorCount; a++)
            {
                var a0Idx = (ushort)(path.AnchorStart + a);
                var a1Idx = (ushort)(path.AnchorStart + ((a + 1) % path.AnchorCount));

                ref readonly var anchor0 = ref spriteShape.GetAnchor(a0Idx);
                ref readonly var anchor1 = ref spriteShape.GetAnchor(a1Idx);

                var p0 = new Vector2Double(anchor0.Position.X, anchor0.Position.Y);
                var p1 = new Vector2Double(anchor1.Position.X, anchor1.Position.Y);

                if (Math.Abs(anchor0.Curve) < 0.0001)
                {
                    contour.AddEdge(new LinearSegment(p0, p1));
                }
                else
                {
                    // Compute quadratic control point from curve value:
                    // cp = midpoint(p0,p1) + perpendicular * curve
                    var mid = new Vector2Double(
                        0.5 * (p0.x + p1.x),
                        0.5 * (p0.y + p1.y));
                    var dir = p1 - p0;
                    double perpMag = MsdfMath.Length(dir);
                    Vector2Double perp;
                    if (perpMag > 1e-10)
                        perp = new Vector2Double(-dir.y / perpMag, dir.x / perpMag);
                    else
                        perp = new Vector2Double(0, 1);
                    var cp = mid + perp * anchor0.Curve;
                    contour.AddEdge(new QuadraticSegment(p0, cp, p1));
                }
            }
        }

        // Boolean-union all contours to eliminate overlaps.
        shape = ShapeClipper.Union(shape);

        shape.Normalize();
        EdgeColoring.ColorSimple(shape, 3.0);

        return shape;
    }

    /// <summary>
    /// Generate an MSDF for a set of sprite paths and write the result into the target pixel data.
    /// Add paths are unioned, subtract paths are unioned, then subtract is carved out of add
    /// via Clipper2 boolean difference. The result is a single clean shape for MSDF generation.
    /// </summary>
    public static void RasterizeMSDF(
        NoZ.Editor.Shape spriteShape,
        PixelData<Color32> target,
        RectInt targetRect,
        Vector2Int sourceOffset,
        ReadOnlySpan<ushort> pathIndices,
        float range = 1.5f)
    {
        if (pathIndices.Length == 0) return;

        var dpi = EditorApplication.Config.PixelsPerUnit;

        // Separate add and subtract paths
        var addPaths = new System.Collections.Generic.List<ushort>();
        var subtractPaths = new System.Collections.Generic.List<ushort>();
        foreach (var pi in pathIndices)
        {
            if (pi >= spriteShape.PathCount) continue;
            ref readonly var path = ref spriteShape.GetPath(pi);
            if (path.AnchorCount < 3) continue;
            if (path.IsSubtract)
                subtractPaths.Add(pi);
            else
                addPaths.Add(pi);
        }

        if (addPaths.Count == 0) return;

        // Build msdf shapes (each already Clipper-unioned internally)
        var shape = FromSpritePaths(spriteShape, addPaths.ToArray());

        // Carve out subtract paths via Clipper2 boolean difference
        if (subtractPaths.Count > 0)
        {
            var subShape = FromSpritePaths(spriteShape, subtractPaths.ToArray());
            shape = ShapeClipper.Difference(shape, subShape);
            shape.Normalize();
            EdgeColoring.ColorSimple(shape, 3.0);
        }

        var scale = new Vector2Double(dpi, dpi);
        var translate = new Vector2Double(
            (double)sourceOffset.X / dpi,
            (double)sourceOffset.Y / dpi);

        int w = targetRect.Width;
        int h = targetRect.Height;
        double rangeInShapeUnits = range / dpi * 2.0;

        // Generate single MSDF from the combined shape
        var bitmap = new MsdfBitmap(w, h);
        MsdfGenerator.GenerateMSDF(bitmap, shape, rangeInShapeUnits, scale, translate);

        // Write to target pixel data
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                var px = bitmap[x, y];
                int tx = targetRect.X + x;
                int ty = targetRect.Y + y;
                target[tx, ty] = new Color32(
                    (byte)(Math.Clamp(px[0], 0f, 1f) * 255f),
                    (byte)(Math.Clamp(px[1], 0f, 1f) * 255f),
                    (byte)(Math.Clamp(px[2], 0f, 1f) * 255f),
                    255);
            }
        }
    }
}
