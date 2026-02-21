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
    /// Glyph coordinates are kept in TTF Y-up space. The shape is marked with
    /// inverseYAxis=true so the generator flips the output rows for screen
    /// Y-down rendering. This preserves natural contour windings for the
    /// OverlappingContourCombiner, matching msdfgen's font pipeline.
    /// </summary>
    public static Shape? FromGlyph(TrueTypeFont.Glyph glyph)
    {
        if (glyph?.contours == null || glyph.contours.Length == 0)
            return null;

        var shape = new Shape();
        shape.inverseYAxis = true; // TTF is Y-up, output is Y-down

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
                return new Vector2Double(p.x, p.y);
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

        // Match msdfgen's default font pipeline (NO_PREPROCESS):
        // No OrientContours — the OverlappingContourCombiner uses natural windings.
        shape.Normalize();
        EdgeColoring.ColorSimple(shape, 3.0);

        return shape;
    }

    /// <summary>
    /// Render a glyph as MSDF into a 3-channel float bitmap.
    /// Uses OverlappingContourCombiner (GenerateMSDF) matching msdfgen's default pipeline.
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
        var shape = FromGlyph(glyph);
        if (shape == null)
            return;

        // Debug: dump edge coloring for 'M' glyph
        if (glyph.codepoint == 'M')
        {
            Console.WriteLine($"=== MSDF DEBUG: Glyph 'M' ===");
            Console.WriteLine($"  Contours: {shape.contours.Count}, inverseYAxis: {shape.inverseYAxis}");
            Console.WriteLine($"  Scale: ({scale.x}, {scale.y}), Translate: ({translate.x}, {translate.y})");
            Console.WriteLine($"  Range: {range}, OutputSize: ({outputSize.X}, {outputSize.Y})");
            for (int ci = 0; ci < shape.contours.Count; ci++)
            {
                var contour = shape.contours[ci];
                Console.WriteLine($"  Contour {ci}: {contour.edges.Count} edges, winding={contour.Winding()}");
                for (int ei = 0; ei < contour.edges.Count; ei++)
                {
                    var edge = contour.edges[ei];
                    var p0 = edge.Point(0);
                    var p1 = edge.Point(1);
                    double len = Math.Sqrt((p1.x-p0.x)*(p1.x-p0.x) + (p1.y-p0.y)*(p1.y-p0.y));
                    Console.WriteLine($"    Edge {ei}: {edge.GetType().Name} color={edge.color} len={len:F3} p0=({p0.x:F3},{p0.y:F3}) p1=({p1.x:F3},{p1.y:F3})");
                }
            }
        }

        // Create a sub-bitmap view for the glyph region
        var glyphBitmap = new MsdfBitmap(outputSize.X, outputSize.Y);

        // Use OverlappingContourCombiner — handles overlapping contours natively
        // using per-contour winding classification. No OrientContours or scanline
        // sign correction needed.
        MsdfGenerator.GenerateMSDF(glyphBitmap, shape, range * 2.0, scale, translate);

        // Debug: dump bitmap block around V-junction BEFORE error correction
        if (glyph.codepoint == 'M')
        {
            // V-bottom in shape space is at approximately (19.4, 19.0)
            // bitmap x = (shapeX + translate.x) * scale.x = (19.4 + 2.656) * 1 ≈ 22
            // bitmap y (flipped) = h - 1 - (shapeY + translate.y) * scale.y = 42 - 1 - (19 + 4.384) = ~18
            Console.WriteLine($"  === PRE-ERROR-CORRECTION: bitmap rows 15-28 (V-junction area) ===");
            for (int py = 15; py <= 28 && py < outputSize.Y; py++)
            {
                var sb = new System.Text.StringBuilder();
                sb.Append($"  y={py:D2}: ");
                for (int px = 17; px <= 27 && px < outputSize.X; px++)
                {
                    var p = glyphBitmap[px, py];
                    float med = MathF.Max(MathF.Min(p[0], p[1]), MathF.Min(MathF.Max(p[0], p[1]), p[2]));
                    // Show med as a compact char: . for outside, # for inside, ? for edge
                    char c = med > 0.55f ? '#' : med < 0.45f ? '.' : '~';
                    sb.Append($"{c}({p[0]:F2},{p[1]:F2},{p[2]:F2}) ");
                }
                Console.WriteLine(sb.ToString());
            }
        }

        // Error correction: modern pipeline with corner/edge protection.
        MsdfGenerator.ErrorCorrection(glyphBitmap, shape, scale, translate, range * 2.0);

        // Debug: dump AFTER error correction
        if (glyph.codepoint == 'M')
        {
            Console.WriteLine($"  === POST-ERROR-CORRECTION: bitmap rows 15-28 ===");
            for (int py = 15; py <= 28 && py < outputSize.Y; py++)
            {
                var sb = new System.Text.StringBuilder();
                sb.Append($"  y={py:D2}: ");
                for (int px = 17; px <= 27 && px < outputSize.X; px++)
                {
                    var p = glyphBitmap[px, py];
                    float med = MathF.Max(MathF.Min(p[0], p[1]), MathF.Min(MathF.Max(p[0], p[1]), p[2]));
                    char c = med > 0.55f ? '#' : med < 0.45f ? '.' : '~';
                    sb.Append($"{c}({p[0]:F2},{p[1]:F2},{p[2]:F2}) ");
                }
                Console.WriteLine(sb.ToString());
            }
        }

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
