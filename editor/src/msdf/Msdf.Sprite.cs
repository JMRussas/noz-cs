//
//  Bridge between NoZ sprite shapes and MSDF generation.
//

using System;

namespace NoZ.Editor.Msdf;

internal static class MsdfSprite
{
    // Append a single sprite path as a contour to an existing shape.
    // Raw conversion only — no Clipper2 union, no normalize, no edge coloring.
    // Winding is normalized to positive for NonZero fill rule.
    private static void AppendContour(
        Shape shape,
        NoZ.Editor.Shape spriteShape,
        ushort pathIndex)
    {
        if (pathIndex >= spriteShape.PathCount) return;
        ref readonly var path = ref spriteShape.GetPath(pathIndex);
        if (path.AnchorCount < 3) return;

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

        // Normalize winding to positive for NonZero fill rule.
        // Must be done before any Clipper2 operation.
        if (contour.Winding() < 0)
            contour.Reverse();
    }

    // Rasterize MSDF for sprite paths. Paths are processed in draw order so that
    // subtract paths only carve from add paths that precede them.
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

        // Walk paths in draw order. Add paths accumulate raw contours.
        // Subtract paths trigger a Clipper2 Difference on the accumulated shape.
        // Clipper2 handles overlapping add contours internally via NonZero fill rule.
        var shape = new Shape();

        foreach (var pi in pathIndices)
        {
            if (pi >= spriteShape.PathCount) continue;
            ref readonly var path = ref spriteShape.GetPath(pi);
            if (path.AnchorCount < 3) continue;

            if (path.IsSubtract)
            {
                if (shape.contours.Count > 0)
                {
                    var subShape = new Shape();
                    AppendContour(subShape, spriteShape, pi);
                    shape = ShapeClipper.Difference(shape, subShape);
                }
            }
            else
            {
                AppendContour(shape, spriteShape, pi);
            }
        }

        if (shape.contours.Count == 0) return;

        // Final cleanup: union resolves any remaining overlaps, then prepare for generation
        shape = ShapeClipper.Union(shape);
        shape.Normalize();
        EdgeColoring.ColorSimple(shape, 3.0);

        var scale = new Vector2Double(dpi, dpi);
        var translate = new Vector2Double(
            (double)sourceOffset.X / dpi,
            (double)sourceOffset.Y / dpi);

        int w = targetRect.Width;
        int h = targetRect.Height;
        double rangeInShapeUnits = range / dpi * 2.0;

        // Use GenerateMSDF (OverlappingContourCombiner) instead of GenerateMSDFBasic.
        // This processes each contour independently and combines by winding,
        // correctly handling holes from Clipper2 Difference — same as fonts.
        var bitmap = new MsdfBitmap(w, h);
        MsdfGenerator.GenerateMSDF(bitmap, shape, rangeInShapeUnits, scale, translate);
        MsdfGenerator.DistanceSignCorrection(bitmap, shape, scale, translate);
        MsdfGenerator.ErrorCorrection(bitmap, shape, scale, translate, rangeInShapeUnits);

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
