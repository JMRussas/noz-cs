//
//  Faithful port of msdfgen by Viktor Chlumsky
//  https://github.com/Chlumsky/msdfgen
//

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using static NoZ.Editor.Msdf.MsdfMath;

namespace NoZ.Editor.Msdf;

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

/// <summary>
/// Port of msdfgen's PerpendicularDistanceSelectorBase.
/// Tracks true distance, positive/negative perpendicular distances, and nearest edge.
/// </summary>
internal struct PerpendicularDistanceSelectorBase
{
    public SignedDistance minTrueDistance;
    public double minNegativePerpendicularDistance;
    public double minPositivePerpendicularDistance;
    public EdgeSegment? nearEdge;
    public double nearEdgeParam;

    /// <summary>
    /// Initialize to default state matching msdfgen's constructor.
    /// minTrueDistance defaults to (-DBL_MAX, 0), perpendicular bounds derived from that.
    /// </summary>
    public void Init()
    {
        minTrueDistance = new SignedDistance(); // distance = -DBL_MAX, dot = 0
        minNegativePerpendicularDistance = -Math.Abs(minTrueDistance.distance);
        minPositivePerpendicularDistance = Math.Abs(minTrueDistance.distance);
        nearEdge = null;
        nearEdgeParam = 0;
    }

    public void AddEdgeTrueDistance(EdgeSegment edge, SignedDistance distance, double param)
    {
        if (distance < minTrueDistance)
        {
            minTrueDistance = distance;
            nearEdge = edge;
            nearEdgeParam = param;
        }
    }

    public void AddEdgePerpendicularDistance(double distance)
    {
        if (distance <= 0 && distance > minNegativePerpendicularDistance)
            minNegativePerpendicularDistance = distance;
        if (distance >= 0 && distance < minPositivePerpendicularDistance)
            minPositivePerpendicularDistance = distance;
    }

    public void Merge(in PerpendicularDistanceSelectorBase other)
    {
        if (other.minTrueDistance < minTrueDistance)
        {
            minTrueDistance = other.minTrueDistance;
            nearEdge = other.nearEdge;
            nearEdgeParam = other.nearEdgeParam;
        }
        if (other.minNegativePerpendicularDistance > minNegativePerpendicularDistance)
            minNegativePerpendicularDistance = other.minNegativePerpendicularDistance;
        if (other.minPositivePerpendicularDistance < minPositivePerpendicularDistance)
            minPositivePerpendicularDistance = other.minPositivePerpendicularDistance;
    }

    public double ComputeDistance(Vector2Double p)
    {
        double minDistance = minTrueDistance.distance < 0
            ? minNegativePerpendicularDistance
            : minPositivePerpendicularDistance;
        if (nearEdge != null)
        {
            SignedDistance distance = minTrueDistance;
            nearEdge.DistanceToPerpendicularDistance(ref distance, p, nearEdgeParam);
            if (Math.Abs(distance.distance) < Math.Abs(minDistance))
                minDistance = distance.distance;
        }
        return minDistance;
    }

    public static bool GetPerpendicularDistance(ref double distance, Vector2Double ep, Vector2Double edgeDir)
    {
        double ts = Dot(ep, edgeDir);
        if (ts > 0)
        {
            double perpendicularDistance = Cross(ep, edgeDir);
            if (Math.Abs(perpendicularDistance) < Math.Abs(distance))
            {
                distance = perpendicularDistance;
                return true;
            }
        }
        return false;
    }
}

/// <summary>
/// Port of msdfgen's MultiDistanceSelector.
/// Three perpendicular distance selectors (one per R/G/B channel).
/// </summary>
internal struct MultiDistanceSelector
{
    public PerpendicularDistanceSelectorBase r, g, b;

    /// <summary>
    /// Initialize all three channel selectors to default state.
    /// </summary>
    public void Init()
    {
        r.Init();
        g.Init();
        b.Init();
    }

    public void AddEdge(EdgeSegment prevEdge, EdgeSegment curEdge, EdgeSegment nextEdge, Vector2Double p)
    {
        double param;
        SignedDistance distance = curEdge.GetSignedDistance(p, out param);

        if (((int)curEdge.color & (int)EdgeColor.RED) != 0)
            r.AddEdgeTrueDistance(curEdge, distance, param);
        if (((int)curEdge.color & (int)EdgeColor.GREEN) != 0)
            g.AddEdgeTrueDistance(curEdge, distance, param);
        if (((int)curEdge.color & (int)EdgeColor.BLUE) != 0)
            b.AddEdgeTrueDistance(curEdge, distance, param);

        Vector2Double ap = p - curEdge.Point(0);
        Vector2Double bp = p - curEdge.Point(1);
        Vector2Double aDir = NormalizeAllowZero(curEdge.Direction(0));
        Vector2Double bDir = NormalizeAllowZero(curEdge.Direction(1));
        Vector2Double prevDir = NormalizeAllowZero(prevEdge.Direction(1));
        Vector2Double nextDir = NormalizeAllowZero(nextEdge.Direction(0));
        double add = Dot(ap, NormalizeAllowZero(prevDir + aDir));
        double bdd = -Dot(bp, NormalizeAllowZero(bDir + nextDir));

        if (add > 0)
        {
            double pd = distance.distance;
            if (PerpendicularDistanceSelectorBase.GetPerpendicularDistance(ref pd, ap,
                    new Vector2Double(-aDir.x, -aDir.y)))
            {
                pd = -pd;
                if (((int)curEdge.color & (int)EdgeColor.RED) != 0)
                    r.AddEdgePerpendicularDistance(pd);
                if (((int)curEdge.color & (int)EdgeColor.GREEN) != 0)
                    g.AddEdgePerpendicularDistance(pd);
                if (((int)curEdge.color & (int)EdgeColor.BLUE) != 0)
                    b.AddEdgePerpendicularDistance(pd);
            }
        }
        if (bdd > 0)
        {
            double pd = distance.distance;
            if (PerpendicularDistanceSelectorBase.GetPerpendicularDistance(ref pd, bp, bDir))
            {
                if (((int)curEdge.color & (int)EdgeColor.RED) != 0)
                    r.AddEdgePerpendicularDistance(pd);
                if (((int)curEdge.color & (int)EdgeColor.GREEN) != 0)
                    g.AddEdgePerpendicularDistance(pd);
                if (((int)curEdge.color & (int)EdgeColor.BLUE) != 0)
                    b.AddEdgePerpendicularDistance(pd);
            }
        }
    }

    public void Merge(in MultiDistanceSelector other)
    {
        r.Merge(other.r);
        g.Merge(other.g);
        b.Merge(other.b);
    }

    public MultiDistance Distance(Vector2Double p)
    {
        return new MultiDistance
        {
            r = r.ComputeDistance(p),
            g = g.ComputeDistance(p),
            b = b.ComputeDistance(p)
        };
    }
}

internal struct MultiDistance
{
    public double r, g, b;

    public double Median() => MsdfMath.Median(r, g, b);
}

internal static class MsdfGenerator
{
    /// <summary>
    /// Generate a multi-channel signed distance field with overlap support.
    /// Faithful port of msdfgen's OverlappingContourCombiner + MultiDistanceSelector
    /// with ShapeDistanceFinder pattern.
    /// </summary>
    public static void GenerateMSDF(
        MsdfBitmap output,
        Shape shape,
        double rangeValue,
        Vector2Double scale,
        Vector2Double translate,
        bool invertWinding = false)
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

        // Pre-compute winding direction for each contour.
        // When invertWinding is true (e.g. because Y was negated during shape construction),
        // negate the computed windings so the combiner's inner/outer classification is correct.
        var windings = new int[contourCount];
        for (int i = 0; i < contourCount; ++i)
            windings[i] = invertWinding ? -shape.contours[i].Winding() : shape.contours[i].Winding();

        Parallel.For(0, h, y =>
        {
            int row = flipY ? h - 1 - y : y;

            // Per-contour selectors (allocated per row for thread safety)
            var contourSelectors = new MultiDistanceSelector[contourCount];

            for (int x = 0; x < w; ++x)
            {
                var p = new Vector2Double(x + 0.5, y + 0.5) / scale - translate;

                // Reset selectors for this pixel
                for (int ci = 0; ci < contourCount; ++ci)
                    contourSelectors[ci].Init();

                // Feed edges to per-contour selectors with prev/next edge context
                for (int ci = 0; ci < contourCount; ++ci)
                {
                    var edges = shape.contours[ci].edges;
                    if (edges.Count == 0)
                        continue;

                    ref var selector = ref contourSelectors[ci];

                    EdgeSegment prevEdge = edges.Count >= 2 ? edges[^2] : edges[0];
                    EdgeSegment curEdge = edges[^1];
                    for (int ei = 0; ei < edges.Count; ++ei)
                    {
                        EdgeSegment nextEdge = edges[ei];
                        selector.AddEdge(prevEdge, curEdge, nextEdge, p);
                        prevEdge = curEdge;
                        curEdge = nextEdge;
                    }
                }

                // --- OverlappingContourCombiner::distance() ---
                // Merge all contour selectors into shape/inner/outer
                var shapeSelector = new MultiDistanceSelector();
                shapeSelector.Init();
                var innerSelector = new MultiDistanceSelector();
                innerSelector.Init();
                var outerSelector = new MultiDistanceSelector();
                outerSelector.Init();

                for (int ci = 0; ci < contourCount; ++ci)
                {
                    MultiDistance edgeDistance = contourSelectors[ci].Distance(p);
                    shapeSelector.Merge(contourSelectors[ci]);
                    if (windings[ci] > 0 && edgeDistance.Median() >= 0)
                        innerSelector.Merge(contourSelectors[ci]);
                    if (windings[ci] < 0 && edgeDistance.Median() <= 0)
                        outerSelector.Merge(contourSelectors[ci]);
                }

                MultiDistance shapeDistance = shapeSelector.Distance(p);
                MultiDistance innerDistance = innerSelector.Distance(p);
                MultiDistance outerDistance = outerSelector.Distance(p);
                double innerScalarDistance = innerDistance.Median();
                double outerScalarDistance = outerDistance.Median();

                MultiDistance result;
                result.r = -double.MaxValue;
                result.g = -double.MaxValue;
                result.b = -double.MaxValue;

                int winding = 0;
                if (innerScalarDistance >= 0 && Math.Abs(innerScalarDistance) <= Math.Abs(outerScalarDistance))
                {
                    result = innerDistance;
                    winding = 1;
                    for (int ci = 0; ci < contourCount; ++ci)
                    {
                        if (windings[ci] > 0)
                        {
                            MultiDistance contourDistance = contourSelectors[ci].Distance(p);
                            if (Math.Abs(contourDistance.Median()) < Math.Abs(outerScalarDistance)
                                && contourDistance.Median() > result.Median())
                                result = contourDistance;
                        }
                    }
                }
                else if (outerScalarDistance <= 0 && Math.Abs(outerScalarDistance) < Math.Abs(innerScalarDistance))
                {
                    result = outerDistance;
                    winding = -1;
                    for (int ci = 0; ci < contourCount; ++ci)
                    {
                        if (windings[ci] < 0)
                        {
                            MultiDistance contourDistance = contourSelectors[ci].Distance(p);
                            if (Math.Abs(contourDistance.Median()) < Math.Abs(innerScalarDistance)
                                && contourDistance.Median() < result.Median())
                                result = contourDistance;
                        }
                    }
                }
                else
                {
                    // Fallback: return shape distance
                    result = shapeDistance;
                    // Write and continue
                    var px0 = output[x, row];
                    px0[0] = (float)(distScale * (result.r + distTranslate));
                    px0[1] = (float)(distScale * (result.g + distTranslate));
                    px0[2] = (float)(distScale * (result.b + distTranslate));
                    continue;
                }

                // Check opposite-winding contours
                for (int ci = 0; ci < contourCount; ++ci)
                {
                    if (windings[ci] != winding)
                    {
                        MultiDistance contourDistance = contourSelectors[ci].Distance(p);
                        if (contourDistance.Median() * result.Median() >= 0
                            && Math.Abs(contourDistance.Median()) < Math.Abs(result.Median()))
                            result = contourDistance;
                    }
                }

                if (result.Median() == shapeDistance.Median())
                    result = shapeDistance;

                var pixel = output[x, row];
                pixel[0] = (float)(distScale * (result.r + distTranslate));
                pixel[1] = (float)(distScale * (result.g + distTranslate));
                pixel[2] = (float)(distScale * (result.b + distTranslate));
            }
        });
    }

    /// <summary>
    /// Generate MSDF using simple nearest-edge-per-channel approach (no overlapping contour combiner).
    /// Suitable for shapes with non-overlapping contours that follow the non-zero winding rule.
    /// </summary>
    public static void GenerateMSDFSimple(
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

        Parallel.For(0, h, y =>
        {
            var selector = new MultiDistanceSelector();

            for (int x = 0; x < w; ++x)
            {
                var p = new Vector2Double(x + 0.5, y + 0.5) / scale - translate;

                selector.Init();

                foreach (var contour in shape.contours)
                {
                    var edges = contour.edges;
                    if (edges.Count == 0)
                        continue;

                    EdgeSegment prevEdge = edges.Count >= 2 ? edges[^2] : edges[0];
                    EdgeSegment curEdge = edges[^1];
                    for (int ei = 0; ei < edges.Count; ++ei)
                    {
                        EdgeSegment nextEdge = edges[ei];
                        selector.AddEdge(prevEdge, curEdge, nextEdge, p);
                        prevEdge = curEdge;
                        curEdge = nextEdge;
                    }
                }

                var dist = selector.Distance(p);

                var pixel = output[x, y];
                pixel[0] = (float)(distScale * (dist.r + distTranslate));
                pixel[1] = (float)(distScale * (dist.g + distTranslate));
                pixel[2] = (float)(distScale * (dist.b + distTranslate));
            }
        });
    }

    /// <summary>
    /// Scanline-based sign correction pass. For each pixel, uses scanline intersection
    /// to determine if the point is filled (non-zero winding rule), then flips the MSDF
    /// distance if it disagrees with the fill state. This is critical for shapes with
    /// overlapping contours where the OverlappingContourCombiner may produce correct
    /// distances but incorrect signs.
    /// Matches msdfgen's distanceSignCorrection (multiDistanceSignCorrection).
    /// </summary>
    public static void DistanceSignCorrection(
        MsdfBitmap sdf,
        Shape shape,
        Vector2Double scale,
        Vector2Double translate)
    {
        int w = sdf.width, h = sdf.height;
        if (w == 0 || h == 0)
            return;

        bool flipY = shape.inverseYAxis;
        float sdfZeroValue = 0.5f;
        float doubleSdfZeroValue = 1.0f;

        // matchMap: +1 = matched, -1 = flipped, 0 = ambiguous (exactly at edge)
        var matchMap = new sbyte[w * h];

        bool ambiguous = false;

        Span<double> ix = stackalloc double[3];
        Span<int> idy = stackalloc int[3];

        for (int y = 0; y < h; ++y)
        {
            int row = flipY ? h - 1 - y : y;
            double shapeY = (y + 0.5) / scale.y - translate.y;

            // Gather all scanline intersections at this Y
            var intersections = new List<(double x, int direction)>();
            foreach (var contour in shape.contours)
            {
                foreach (var edge in contour.edges)
                {
                    int n = edge.ScanlineIntersections(ix, idy, shapeY);
                    for (int k = 0; k < n; ++k)
                        intersections.Add((ix[k], idy[k]));
                }
            }

            // Sort by x, then compute cumulative winding direction
            intersections.Sort((a, b) => a.x.CompareTo(b.x));
            int totalDirection = 0;
            for (int j = 0; j < intersections.Count; ++j)
            {
                totalDirection += intersections[j].direction;
                intersections[j] = (intersections[j].x, totalDirection);
            }

            for (int x = 0; x < w; ++x)
            {
                double shapeX = (x + 0.5) / scale.x - translate.x;

                // Determine fill: find the last intersection with x <= shapeX
                // and check if its cumulative winding is non-zero
                int winding = 0;
                for (int j = intersections.Count - 1; j >= 0; --j)
                {
                    if (intersections[j].x <= shapeX)
                    {
                        winding = intersections[j].direction;
                        break;
                    }
                }
                bool fill = winding != 0; // non-zero fill rule

                var pixel = sdf[x, row];
                float sd = MathF.Max(
                    MathF.Min(pixel[0], pixel[1]),
                    MathF.Min(MathF.Max(pixel[0], pixel[1]), pixel[2]));

                int mapIndex = y * w + x;
                if (sd == sdfZeroValue)
                {
                    ambiguous = true;
                }
                else if ((sd > sdfZeroValue) != fill)
                {
                    // Sign disagrees with fill — flip
                    pixel[0] = doubleSdfZeroValue - pixel[0];
                    pixel[1] = doubleSdfZeroValue - pixel[1];
                    pixel[2] = doubleSdfZeroValue - pixel[2];
                    matchMap[mapIndex] = -1;
                }
                else
                {
                    matchMap[mapIndex] = 1;
                }
            }
        }

        // Resolve ambiguous pixels by looking at neighbors
        if (ambiguous)
        {
            for (int y = 0; y < h; ++y)
            {
                int row = flipY ? h - 1 - y : y;
                for (int x = 0; x < w; ++x)
                {
                    int idx = y * w + x;
                    if (matchMap[idx] == 0)
                    {
                        int neighborMatch = 0;
                        if (x > 0) neighborMatch += matchMap[idx - 1];
                        if (x < w - 1) neighborMatch += matchMap[idx + 1];
                        if (y > 0) neighborMatch += matchMap[idx - w];
                        if (y < h - 1) neighborMatch += matchMap[idx + w];
                        if (neighborMatch < 0)
                        {
                            var pixel = sdf[x, row];
                            pixel[0] = doubleSdfZeroValue - pixel[0];
                            pixel[1] = doubleSdfZeroValue - pixel[1];
                            pixel[2] = doubleSdfZeroValue - pixel[2];
                        }
                    }
                }
            }
        }
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
