//
//  Bridge between NoZ sprite shapes and msdf2 MSDF generation.
//

using System;

namespace NoZ.Editor.Msdf2;

internal static class MsdfSprite
{
    /// <summary>
    /// Convert NoZ sprite paths into an msdf2 Shape with edge coloring applied,
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

        shape.Normalize();
        shape.OrientContours();
        EdgeColoring.ColorSimple(shape, 3.0);

        return shape;
    }

    /// <summary>
    /// Generate an MSDF for a set of sprite paths and write the result into the target pixel data.
    /// All paths are combined into a single shape. The generator uses the OverlappingContourCombiner
    /// algorithm to correctly handle multiple disjoint contours. Subtract paths are generated
    /// separately and carved out via intersection (min of inverted).
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

        // Build msdf2 shape from all additive paths (one contour per path)
        var addShape = addPaths.Count > 0
            ? FromSpritePaths(spriteShape, addPaths.ToArray())
            : null;

        // Build msdf2 shape from all subtract paths
        var subShape = subtractPaths.Count > 0
            ? FromSpritePaths(spriteShape, subtractPaths.ToArray())
            : null;

        var scale = new Vector2Double(dpi, dpi);
        var translate = new Vector2Double(
            (double)sourceOffset.X / dpi,
            (double)sourceOffset.Y / dpi);

        int w = targetRect.Width;
        int h = targetRect.Height;
        double rangeInShapeUnits = range / dpi * 2.0;

        // Generate MSDF for additive shape (generator handles multi-contour via
        // OverlappingContourCombiner algorithm)
        MsdfBitmap? addBitmap = null;
        if (addShape != null)
        {
            addBitmap = new MsdfBitmap(w, h);
            MsdfGenerator.GenerateMSDF(addBitmap, addShape, rangeInShapeUnits, scale, translate);
        }

        // Generate MSDF for subtract shape
        MsdfBitmap? subBitmap = null;
        if (subShape != null)
        {
            subBitmap = new MsdfBitmap(w, h);
            MsdfGenerator.GenerateMSDF(subBitmap, subShape, rangeInShapeUnits, scale, translate);
        }

        // Composite into target pixel data
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                float r, g, b;

                if (addBitmap != null)
                {
                    var px = addBitmap[x, y];
                    r = px[0];
                    g = px[1];
                    b = px[2];
                }
                else
                {
                    r = 0f;
                    g = 0f;
                    b = 0f;
                }

                // Subtract: the subtract shape's distance inverts and mins
                if (subBitmap != null)
                {
                    var spx = subBitmap[x, y];
                    r = Math.Min(r, 1f - spx[0]);
                    g = Math.Min(g, 1f - spx[1]);
                    b = Math.Min(b, 1f - spx[2]);
                }

                // Clamp to [0, 1]
                r = Math.Clamp(r, 0f, 1f);
                g = Math.Clamp(g, 0f, 1f);
                b = Math.Clamp(b, 0f, 1f);

                int tx = targetRect.X + x;
                int ty = targetRect.Y + y;
                target[tx, ty] = new Color32(
                    (byte)(r * 255f),
                    (byte)(g * 255f),
                    (byte)(b * 255f),
                    0);
            }
        }
    }
}
