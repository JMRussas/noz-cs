//
//  Faithful port of msdfgen by Viktor Chlumsky
//  https://github.com/Chlumsky/msdfgen
//

using System;
using System.Threading.Tasks;
using static NoZ.Editor.Msdf2.MsdfMath;

namespace NoZ.Editor.Msdf2;

/// <summary>
/// MSDF bitmap: width * height * 3 floats (RGB channels).
/// </summary>
internal class MsdfBitmap
{
    public readonly int width;
    public readonly int height;
    public readonly float[] pixels; // row-major, 3 floats per pixel (R, G, B)

    public MsdfBitmap(int width, int height)
    {
        this.width = width;
        this.height = height;
        pixels = new float[width * height * 3];
    }

    public Span<float> this[int x, int y] => pixels.AsSpan((y * width + x) * 3, 3);
}

internal static class MsdfGenerator
{
    /// <summary>
    /// Per-contour multi-channel distance result.
    /// </summary>
    private struct ContourMSD
    {
        public double r, g, b;

        public double Median() => MsdfMath.Median(r, g, b);
    }

    /// <summary>
    /// Generate a multi-channel signed distance field with overlap support.
    /// This ports msdfgen's OverlappingContourCombiner algorithm, which computes
    /// per-contour distances and uses winding direction to correctly resolve
    /// which contour "owns" each pixel. This is required for shapes with multiple
    /// disjoint contours (e.g. the dots on "i" or separate sprite paths).
    ///
    /// scale: pixels per shape unit
    /// translate: shape-space offset to apply before scaling
    /// range: the distance range in shape units
    /// </summary>
    public static void GenerateMSDF(
        MsdfBitmap output,
        Shape shape,
        double rangeValue,
        Vector2Double scale,
        Vector2Double translate)
    {
        double rangeLower = -0.5 * rangeValue;
        double rangeUpper = 0.5 * rangeValue;
        double rangeWidth = rangeUpper - rangeLower;
        double distScale = 1.0 / rangeWidth;
        double distTranslate = -rangeLower;

        int w = output.width;
        int h = output.height;
        bool flipY = shape.inverseYAxis;

        int contourCount = shape.contours.Count;

        // Pre-compute winding direction for each contour
        var windings = new int[contourCount];
        for (int i = 0; i < contourCount; ++i)
            windings[i] = shape.contours[i].Winding();

        Parallel.For(0, h, y =>
        {
            int row = flipY ? h - 1 - y : y;

            // Per-contour distance storage (allocated per row for thread safety)
            var contourDists = new ContourMSD[contourCount];

            for (int x = 0; x < w; ++x)
            {
                var p = new Vector2Double(x + 0.5, y + 0.5) / scale - translate;

                // --- Compute per-contour multi-channel distances ---
                // Also track the global nearest edges (for the "shape" fallback)
                SignedDistance shapeRMin = new(), shapeGMin = new(), shapeBMin = new();
                EdgeSegment? shapeREdge = null, shapeGEdge = null, shapeBEdge = null;
                double shapeRParam = 0, shapeGParam = 0, shapeBParam = 0;

                for (int ci = 0; ci < contourCount; ++ci)
                {
                    SignedDistance rMin = new(), gMin = new(), bMin = new();
                    EdgeSegment? rEdge = null, gEdge = null, bEdge = null;
                    double rParam = 0, gParam = 0, bParam = 0;

                    foreach (var edge in shape.contours[ci].edges)
                    {
                        var distance = edge.GetSignedDistance(p, out double param);

                        if (((int)edge.color & (int)EdgeColor.RED) != 0 && distance < rMin)
                        {
                            rMin = distance;
                            rEdge = edge;
                            rParam = param;
                        }
                        if (((int)edge.color & (int)EdgeColor.GREEN) != 0 && distance < gMin)
                        {
                            gMin = distance;
                            gEdge = edge;
                            gParam = param;
                        }
                        if (((int)edge.color & (int)EdgeColor.BLUE) != 0 && distance < bMin)
                        {
                            bMin = distance;
                            bEdge = edge;
                            bParam = param;
                        }

                        // Track global shape-level nearest
                        if (((int)edge.color & (int)EdgeColor.RED) != 0 && distance < shapeRMin)
                        {
                            shapeRMin = distance;
                            shapeREdge = edge;
                            shapeRParam = param;
                        }
                        if (((int)edge.color & (int)EdgeColor.GREEN) != 0 && distance < shapeGMin)
                        {
                            shapeGMin = distance;
                            shapeGEdge = edge;
                            shapeGParam = param;
                        }
                        if (((int)edge.color & (int)EdgeColor.BLUE) != 0 && distance < shapeBMin)
                        {
                            shapeBMin = distance;
                            shapeBEdge = edge;
                            shapeBParam = param;
                        }
                    }

                    // Apply perpendicular distance extension
                    rEdge?.DistanceToPerpendicularDistance(ref rMin, p, rParam);
                    gEdge?.DistanceToPerpendicularDistance(ref gMin, p, gParam);
                    bEdge?.DistanceToPerpendicularDistance(ref bMin, p, bParam);

                    contourDists[ci] = new ContourMSD
                    {
                        r = rMin.distance,
                        g = gMin.distance,
                        b = bMin.distance
                    };
                }

                // Shape-level perpendicular distances (fallback)
                shapeREdge?.DistanceToPerpendicularDistance(ref shapeRMin, p, shapeRParam);
                shapeGEdge?.DistanceToPerpendicularDistance(ref shapeGMin, p, shapeGParam);
                shapeBEdge?.DistanceToPerpendicularDistance(ref shapeBMin, p, shapeBParam);

                var shapeDist = new ContourMSD
                {
                    r = shapeRMin.distance,
                    g = shapeGMin.distance,
                    b = shapeBMin.distance,
                };

                // --- OverlappingContourCombiner logic ---
                // Classify contours into inner (positive winding, point inside)
                // and outer (negative winding, point outside)
                var innerDist = new ContourMSD { r = -double.MaxValue, g = -double.MaxValue, b = -double.MaxValue };
                var outerDist = new ContourMSD { r = -double.MaxValue, g = -double.MaxValue, b = -double.MaxValue };

                for (int ci = 0; ci < contourCount; ++ci)
                {
                    double med = contourDists[ci].Median();
                    if (windings[ci] > 0 && med >= 0)
                        MergeMSD(ref innerDist, contourDists[ci]);
                    if (windings[ci] < 0 && med <= 0)
                        MergeMSD(ref outerDist, contourDists[ci]);
                }

                double innerMed = innerDist.Median();
                double outerMed = outerDist.Median();
                ContourMSD result;

                if (innerMed >= 0 && Math.Abs(innerMed) <= Math.Abs(outerMed))
                {
                    result = innerDist;
                    // Refine: among positive-winding contours, pick the one closest
                    // to the border but still within the outer boundary
                    for (int ci = 0; ci < contourCount; ++ci)
                    {
                        if (windings[ci] > 0)
                        {
                            double cMed = contourDists[ci].Median();
                            if (Math.Abs(cMed) < Math.Abs(outerMed) && cMed > result.Median())
                                result = contourDists[ci];
                        }
                    }
                    // Check opposite-winding contours that might be closer
                    for (int ci = 0; ci < contourCount; ++ci)
                    {
                        if (windings[ci] <= 0)
                        {
                            double cMed = contourDists[ci].Median();
                            double rMed = result.Median();
                            if (cMed * rMed >= 0 && Math.Abs(cMed) < Math.Abs(rMed))
                                result = contourDists[ci];
                        }
                    }
                }
                else if (outerMed <= 0 && Math.Abs(outerMed) < Math.Abs(innerMed))
                {
                    result = outerDist;
                    // Refine: among negative-winding contours, pick the one closest
                    // to the border but still outside the inner boundary
                    for (int ci = 0; ci < contourCount; ++ci)
                    {
                        if (windings[ci] < 0)
                        {
                            double cMed = contourDists[ci].Median();
                            if (Math.Abs(cMed) < Math.Abs(innerMed) && cMed < result.Median())
                                result = contourDists[ci];
                        }
                    }
                    // Check opposite-winding contours that might be closer
                    for (int ci = 0; ci < contourCount; ++ci)
                    {
                        if (windings[ci] >= 0)
                        {
                            double cMed = contourDists[ci].Median();
                            double rMed = result.Median();
                            if (cMed * rMed >= 0 && Math.Abs(cMed) < Math.Abs(rMed))
                                result = contourDists[ci];
                        }
                    }
                }
                else
                {
                    // Fallback: use the global shape distance (simple combiner behavior)
                    result = shapeDist;
                }

                if (result.Median() == shapeDist.Median())
                    result = shapeDist;

                var pixel = output[x, row];
                pixel[0] = (float)(distScale * (result.r + distTranslate));
                pixel[1] = (float)(distScale * (result.g + distTranslate));
                pixel[2] = (float)(distScale * (result.b + distTranslate));
            }
        });
    }

    /// <summary>
    /// Merge multi-channel distance: take the closer (smaller absolute) distance per channel.
    /// This matches msdfgen's MultiDistanceSelector::merge behavior.
    /// </summary>
    private static void MergeMSD(ref ContourMSD target, ContourMSD source)
    {
        if (Math.Abs(source.r) < Math.Abs(target.r) || (Math.Abs(source.r) == Math.Abs(target.r) && source.r > target.r))
            target.r = source.r;
        if (Math.Abs(source.g) < Math.Abs(target.g) || (Math.Abs(source.g) == Math.Abs(target.g) && source.g > target.g))
            target.g = source.g;
        if (Math.Abs(source.b) < Math.Abs(target.b) || (Math.Abs(source.b) == Math.Abs(target.b) && source.b > target.b))
            target.b = source.b;
    }

    /// <summary>
    /// Apply the legacy error correction to the MSDF bitmap.
    /// Detects clashing texels (where interpolation between adjacent pixels would produce
    /// incorrect results) and replaces them with the median of their channels.
    /// </summary>
    public static void ErrorCorrection(MsdfBitmap sdf, Vector2Double threshold)
    {
        int w = sdf.width, h = sdf.height;
        var clashes = new System.Collections.Concurrent.ConcurrentBag<(int x, int y)>();

        // Detect cardinal clashes
        Parallel.For(0, h, y =>
        {
            for (int x = 0; x < w; ++x)
            {
                var px = sdf[x, y];
                if ((x > 0 && DetectClash(px, sdf[x - 1, y], threshold.x)) ||
                    (x < w - 1 && DetectClash(px, sdf[x + 1, y], threshold.x)) ||
                    (y > 0 && DetectClash(px, sdf[x, y - 1], threshold.y)) ||
                    (y < h - 1 && DetectClash(px, sdf[x, y + 1], threshold.y)))
                {
                    clashes.Add((x, y));
                }
            }
        });

        foreach (var (cx, cy) in clashes)
        {
            var pixel = sdf[cx, cy];
            float med = MathF.Max(MathF.Min(pixel[0], pixel[1]), MathF.Min(MathF.Max(pixel[0], pixel[1]), pixel[2]));
            pixel[0] = med;
            pixel[1] = med;
            pixel[2] = med;
        }

        // Detect diagonal clashes
        clashes.Clear();
        Parallel.For(0, h, y =>
        {
            for (int x = 0; x < w; ++x)
            {
                var px = sdf[x, y];
                double diagThreshold = threshold.x + threshold.y;
                if ((x > 0 && y > 0 && DetectClash(px, sdf[x - 1, y - 1], diagThreshold)) ||
                    (x < w - 1 && y > 0 && DetectClash(px, sdf[x + 1, y - 1], diagThreshold)) ||
                    (x > 0 && y < h - 1 && DetectClash(px, sdf[x - 1, y + 1], diagThreshold)) ||
                    (x < w - 1 && y < h - 1 && DetectClash(px, sdf[x + 1, y + 1], diagThreshold)))
                {
                    clashes.Add((x, y));
                }
            }
        });

        foreach (var (cx, cy) in clashes)
        {
            var pixel = sdf[cx, cy];
            float med = MathF.Max(MathF.Min(pixel[0], pixel[1]), MathF.Min(MathF.Max(pixel[0], pixel[1]), pixel[2]));
            pixel[0] = med;
            pixel[1] = med;
            pixel[2] = med;
        }
    }

    private static bool DetectClash(Span<float> a, Span<float> b, double threshold)
    {
        float a0 = a[0], a1 = a[1], a2 = a[2];
        float b0 = b[0], b1 = b[1], b2 = b[2];

        // Sort channels so that pairs go from biggest to smallest absolute difference
        if (MathF.Abs(b0 - a0) < MathF.Abs(b1 - a1))
        {
            (a0, a1) = (a1, a0);
            (b0, b1) = (b1, b0);
        }
        if (MathF.Abs(b1 - a1) < MathF.Abs(b2 - a2))
        {
            (a1, a2) = (a2, a1);
            (b1, b2) = (b2, b1);
            if (MathF.Abs(b0 - a0) < MathF.Abs(b1 - a1))
            {
                (a0, a1) = (a1, a0);
                (b0, b1) = (b1, b0);
            }
        }

        return (MathF.Abs(b1 - a1) >= threshold) &&
            !(b0 == b1 && b0 == b2) && // Ignore if other pixel has been equalized
            MathF.Abs(a2 - 0.5f) >= MathF.Abs(b2 - 0.5f); // Only flag pixel farther from edge
    }
}
