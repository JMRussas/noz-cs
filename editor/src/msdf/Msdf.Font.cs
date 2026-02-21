//
//  Bridge between TTF font glyphs and MSDF generation.
//

using System;

namespace NoZ.Editor.Msdf;

internal static class MsdfFont
{
    /// <summary>
    /// Convert a TTF glyph into an msdf Shape with edge coloring applied,
    /// ready for MSDF generation.
    /// When flipY is true, glyph Y coordinates are negated to convert from
    /// TTF Y-up to screen Y-down space. This ensures correct winding for
    /// the MSDF generator.
    /// </summary>
    public static Shape? FromGlyph(TrueTypeFont.Glyph glyph, bool flipY)
    {
        if (glyph?.contours == null || glyph.contours.Length == 0)
            return null;

        var shape = new Shape();

        for (int ci = 0; ci < glyph.contours.Length; ci++)
        {
            ref var glyphContour = ref glyph.contours[ci];
            int contourStart = glyphContour.start;
            int contourLength = glyphContour.length;

            if (contourLength == 0)
                continue;

            var contour = shape.AddContour();

            Vector2Double GetXY(int index)
            {
                var p = glyph.points[contourStart + (index % contourLength)].xy;
                return flipY ? new Vector2Double(p.x, -p.y) : p;
            }

            TrueTypeFont.CurveType GetCurve(int index) =>
                glyph.points[contourStart + (index % contourLength)].curve;

            static Vector2Double Midpoint(Vector2Double a, Vector2Double b) =>
                new((a.x + b.x) / 2, (a.y + b.y) / 2);

            // Find the first on-curve point, or compute implicit start if all are conic
            int firstOnCurve = -1;
            for (int p = 0; p < contourLength; p++)
            {
                if (GetCurve(p) != TrueTypeFont.CurveType.Conic)
                {
                    firstOnCurve = p;
                    break;
                }
            }

            Vector2Double start;
            int startIndex;

            if (firstOnCurve >= 0)
            {
                start = GetXY(firstOnCurve);
                startIndex = firstOnCurve;
            }
            else
            {
                start = Midpoint(GetXY(0), GetXY(1));
                startIndex = 0;
            }

            Vector2Double last = start;
            int processed = 0;
            int pIdx = startIndex;

            while (processed < contourLength)
            {
                pIdx = (pIdx + 1) % contourLength;
                processed++;

                var curve = GetCurve(pIdx);
                var xy = GetXY(pIdx);

                if (curve == TrueTypeFont.CurveType.Conic)
                {
                    var control = xy;

                    while (processed < contourLength)
                    {
                        int nextIdx = (pIdx + 1) % contourLength;
                        var nextCurve = GetCurve(nextIdx);
                        var nextXY = GetXY(nextIdx);

                        if (nextCurve != TrueTypeFont.CurveType.Conic)
                        {
                            contour.AddEdge(new QuadraticSegment(last, control, nextXY));
                            last = nextXY;
                            pIdx = nextIdx;
                            processed++;
                            break;
                        }
                        else
                        {
                            var mid = Midpoint(control, nextXY);
                            contour.AddEdge(new QuadraticSegment(last, control, mid));
                            last = mid;
                            control = nextXY;
                            pIdx = nextIdx;
                            processed++;
                        }
                    }

                    if (processed >= contourLength && last != start)
                        contour.AddEdge(new QuadraticSegment(last, control, start));
                }
                else
                {
                    contour.AddEdge(new LinearSegment(last, xy));
                    last = xy;
                }
            }

            // Close the contour if needed
            if (contour.edges.Count > 0 && last != start)
            {
                var lastEdgeEnd = contour.edges[^1].Point(1.0);
                if (!MathEx.Approximately(lastEdgeEnd.x, start.x) || !MathEx.Approximately(lastEdgeEnd.y, start.y))
                    contour.AddEdge(new LinearSegment(last, start));
            }
        }

        shape.Normalize();
        shape.OrientContours();
        EdgeColoring.ColorSimple(shape, 3.0);

        return shape;
    }

    /// <summary>
    /// Render a glyph as MSDF into a 3-channel float bitmap.
    /// </summary>
    public static void RenderGlyph(
        TrueTypeFont.Glyph glyph,
        MsdfBitmap output,
        Vector2Int outputPosition,
        Vector2Int outputSize,
        double range,
        in Vector2Double scale,
        in Vector2Double translate)
    {
        var shape = FromGlyph(glyph, true);
        if (shape == null)
            return;

        // Create a sub-bitmap view for the glyph region
        var glyphBitmap = new MsdfBitmap(outputSize.X, outputSize.Y);
        MsdfGenerator.GenerateMSDFSimple(glyphBitmap, shape, range * 2.0, scale, translate);

        // Scanline sign correction: fixes distance sign for overlapping contours
        // where the simple combiner gets the sign wrong. OrientContours ensures
        // correct winding for non-overlapping regions; this pass fixes the rest.
        MsdfGenerator.DistanceSignCorrection(glyphBitmap, shape, scale, translate);

        // Error correction: fix interpolation artifacts at contour boundaries by converting
        // clashing texels to single-channel (median). This matches msdfgen's behavior where
        // msdfErrorCorrection is always called after MSDF generation.
        var ecThreshold = new Vector2Double(
            1.001 / outputSize.X,
            1.001 / outputSize.Y);
        MsdfGenerator.ErrorCorrection(glyphBitmap, ecThreshold);

        // Copy to output at the correct position
        for (int y = 0; y < outputSize.Y; y++)
        {
            for (int x = 0; x < outputSize.X; x++)
            {
                var src = glyphBitmap[x, y];
                var dst = output[x + outputPosition.X, y + outputPosition.Y];
                dst[0] = src[0];
                dst[1] = src[1];
                dst[2] = src[2];
            }
        }
    }
}
