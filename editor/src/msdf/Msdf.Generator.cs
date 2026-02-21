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

    // --- Modern MSDF Error Correction (port of msdfgen's MSDFErrorCorrection) ---

    private const byte STENCIL_ERROR = 1;
    private const byte STENCIL_PROTECTED = 2;
    private const double ARTIFACT_T_EPSILON = 0.01;
    private const double PROTECTION_RADIUS_TOLERANCE = 1.001;
    private const double DEFAULT_MIN_DEVIATION_RATIO = 1.11111111111111111;

    /// <summary>
    /// Modern error correction with corner and edge protection.
    /// Port of msdfgen's MSDFErrorCorrection (EDGE_PRIORITY mode, DO_NOT_CHECK_DISTANCE).
    /// </summary>
    public static void ErrorCorrection(
        MsdfBitmap sdf,
        Shape shape,
        Vector2Double scale,
        Vector2Double translate,
        double rangeValue)
    {
        int w = sdf.width, h = sdf.height;
        if (w == 0 || h == 0)
            return;

        var stencil = new byte[w * h];

        ProtectCorners(stencil, w, h, shape, scale, translate);
        ProtectEdges(stencil, sdf, w, h, scale, rangeValue);
        FindErrors(stencil, sdf, w, h, scale, rangeValue);
        ApplyCorrection(stencil, sdf, w, h);
    }

    /// <summary>
    /// Flags texels near edge color-change corners as PROTECTED.
    /// Port of MSDFErrorCorrection::protectCorners.
    /// </summary>
    private static void ProtectCorners(
        byte[] stencil, int w, int h,
        Shape shape,
        Vector2Double scale,
        Vector2Double translate)
    {
        bool flipY = shape.inverseYAxis;

        foreach (var contour in shape.contours)
        {
            if (contour.edges.Count == 0)
                continue;

            EdgeSegment prevEdge = contour.edges[^1];
            foreach (var edge in contour.edges)
            {
                int commonColor = (int)prevEdge.color & (int)edge.color;
                // If the common color has at most one bit set, this is a corner
                // (the color changes from prevEdge to edge)
                if ((commonColor & (commonColor - 1)) == 0)
                {
                    // Project the corner point (edge start) to pixel space
                    var shapePoint = edge.Point(0);
                    double px = scale.x * (shapePoint.x + translate.x) - 0.5;
                    double py = scale.y * (shapePoint.y + translate.y) - 0.5;

                    // When inverseYAxis is set, the generator flipped output rows.
                    // The stencil is indexed in flipped bitmap space, so remap y.
                    if (flipY)
                        py = h - 1 - py;

                    int l = (int)Math.Floor(px);
                    int b = (int)Math.Floor(py);
                    int r = l + 1;
                    int t = b + 1;

                    // Mark the 4 surrounding texels as protected
                    if (l < w && b < h && r >= 0 && t >= 0)
                    {
                        if (l >= 0 && b >= 0) stencil[b * w + l] |= STENCIL_PROTECTED;
                        if (r < w && b >= 0) stencil[b * w + r] |= STENCIL_PROTECTED;
                        if (l >= 0 && t < h) stencil[t * w + l] |= STENCIL_PROTECTED;
                        if (r < w && t < h) stencil[t * w + r] |= STENCIL_PROTECTED;
                    }
                }
                prevEdge = edge;
            }
        }
    }

    /// <summary>
    /// Returns a bitmask of which channels contribute to a shape edge between texels a and b.
    /// Port of edgeBetweenTexels from MSDFErrorCorrection.cpp.
    /// </summary>
    private static int EdgeBetweenTexels(Span<float> a, Span<float> b)
    {
        int mask = 0;
        for (int ch = 0; ch < 3; ch++)
        {
            double denom = a[ch] - b[ch];
            if (denom == 0) continue;
            double t = (a[ch] - 0.5) / denom;
            if (t > 0 && t < 1)
            {
                // Interpolate all channels at t
                float c0 = (float)(a[0] + t * (b[0] - a[0]));
                float c1 = (float)(a[1] + t * (b[1] - a[1]));
                float c2 = (float)(a[2] + t * (b[2] - a[2]));
                float med = MathF.Max(MathF.Min(c0, c1), MathF.Min(MathF.Max(c0, c1), c2));
                // This is only a real edge if the zero-distance channel is the median
                float cCh = ch == 0 ? c0 : ch == 1 ? c1 : c2;
                if (med == cCh)
                    mask |= (1 << ch);
            }
        }
        return mask;
    }

    /// <summary>
    /// Marks a texel as protected if one of its non-median channels is present in the edge mask.
    /// Port of protectExtremeChannels from MSDFErrorCorrection.cpp.
    /// </summary>
    private static void ProtectExtremeChannels(byte[] stencil, int idx, Span<float> msd, float m, int mask)
    {
        if ((mask & 1) != 0 && msd[0] != m ||
            (mask & 2) != 0 && msd[1] != m ||
            (mask & 4) != 0 && msd[2] != m)
        {
            stencil[idx] |= STENCIL_PROTECTED;
        }
    }

    /// <summary>
    /// Flags texels that contribute to shape edges as PROTECTED.
    /// Port of MSDFErrorCorrection::protectEdges.
    /// </summary>
    private static void ProtectEdges(
        byte[] stencil, MsdfBitmap sdf, int w, int h,
        Vector2Double scale, double rangeValue)
    {
        // Radius: how close to 0.5 a texel pair needs to be to contain an edge.
        // This is PROTECTION_RADIUS_TOLERANCE * (1 texel of distance change in shape space) mapped back.
        // In our framework: 1 texel apart in the bitmap = 1/scale shape units.
        // The distance range maps [-rangeValue/2, rangeValue/2] -> [0, 1].
        // So 1 unit of shape-space distance = 1/rangeValue in the [0,1] output.
        // 1 texel of distance = (1/scale) / rangeValue... but that's the distance per texel.
        // Actually, the radius check is: |lm - 0.5| + |rm - 0.5| < radius
        // where radius represents one texel's worth of distance change in the normalized output.
        // One texel horizontal: shape distance = 1/scale.x, normalized = (1/scale.x) * (1/rangeValue) * scale.x...
        // Wait: distScale = 1/rangeValue, and for horizontal, one texel spans 1/scale.x in shape space,
        // so normalized distance change per texel = (1/scale.x) / rangeValue... no.
        // The normalized distance at a pixel encodes the shape-space distance to edge:
        //   normalizedDist = distScale * (shapeDist + distTranslate)
        //   distScale = 1/rangeValue, distTranslate = rangeValue/2
        // So dNorm/dShape = 1/rangeValue.
        // One texel apart horizontally = 1/scale.x in shape space.
        // So the expected max change in normalized distance between adjacent texels = (1/scale.x) / rangeValue.
        // But msdfgen computes: unprojectVector(distanceMapping(Delta(1)), 0).length()
        //   distanceMapping(Delta(1)) maps a delta of 1 in [0,1] space to shape space = rangeValue
        //   unprojectVector divides by scale: rangeValue / scale.x
        //   So the radius is 1.001 * rangeValue / scale.x
        // That doesn't match... Let me re-read msdfgen's distance mapping.
        // Actually distanceMapping(Delta(1)) maps a 1-pixel distance delta.
        // The DistanceMapping in msdfgen is: output = scale * distance + translate
        // where scale = 1/(upper-lower) and translate = -lower/(upper-lower).
        // Delta(1) → just the scale part: 1 * distMappingScale = 1/(upper-lower).
        // Wait no — Delta(1) means 1 unit of *output* distance (in [0,1] space),
        // which maps to 1/distMappingScale = (upper-lower) = rangeValue in shape space.
        // Hmm, actually the DistanceMapping operator()(Delta d) returns d.value * 1/scale = d * rangeValue.
        // Then unprojectVector(Vector2(rangeValue, 0)) = Vector2(rangeValue/scale.x, 0).
        // So radius = 1.001 * rangeValue / scale.x.
        // But we're comparing |lm - 0.5| + |rm - 0.5| which are in [0, 0.5] range each.
        // This seems too large. Let me re-check...

        // Actually, looking more carefully at the msdfgen code:
        //   radius = float(PROTECTION_RADIUS_TOLERANCE*transformation.unprojectVector(
        //       Vector2(transformation.distanceMapping(DistanceMapping::Delta(1)), 0)).length());
        // distanceMapping has scale = 1/rangeWidth and translate = -rangeLower/rangeWidth
        // distanceMapping(Delta(1)) means: 1 * (1/distMapScale) = 1 * rangeWidth = rangeWidth.
        // Wait no — DistanceMapping::operator()(Delta d) = scale * d.value.
        // So distanceMapping(Delta(1)) = scale * 1 = 1/rangeWidth.
        // Then unprojectVector(Vector2(1/rangeWidth, 0)) = Vector2(1/(rangeWidth * scale.x), 0).
        // length = 1/(rangeWidth * scale.x).
        // radius = 1.001 / (rangeWidth * scale.x).
        // In our case rangeWidth = rangeValue, so:
        float hRadius = (float)(PROTECTION_RADIUS_TOLERANCE / (rangeValue * scale.x));
        float vRadius = (float)(PROTECTION_RADIUS_TOLERANCE / (rangeValue * scale.y));
        float dRadius = (float)(PROTECTION_RADIUS_TOLERANCE / (rangeValue * Math.Sqrt(1.0 / (scale.x * scale.x) + 1.0 / (scale.y * scale.y))));

        // Wait — for diagonal, msdfgen uses:
        //   radius = PROTECTION_RADIUS_TOLERANCE * transformation.unprojectVector(
        //       Vector2(transformation.distanceMapping(DistanceMapping::Delta(1)))).length()
        // Note: Vector2(scalar) means both components are the same value.
        // So: unprojectVector(Vector2(1/rangeWidth, 1/rangeWidth)) = Vector2(1/(rangeWidth*scale.x), 1/(rangeWidth*scale.y))
        // length = sqrt((1/(rW*sx))^2 + (1/(rW*sy))^2) = (1/rW) * sqrt(1/sx^2 + 1/sy^2)
        // With uniform scale (sx == sy): = (1/rW) * (1/sx) * sqrt(2) ≈ hRadius * 1.414
        // Actually for our case scale is uniform, so let's simplify:
        dRadius = (float)(PROTECTION_RADIUS_TOLERANCE * Math.Sqrt(1.0 / (rangeValue * rangeValue * scale.x * scale.x) + 1.0 / (rangeValue * rangeValue * scale.y * scale.y)));

        // Horizontal texel pairs
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w - 1; x++)
            {
                var left = sdf[x, y];
                var right = sdf[x + 1, y];
                float lm = MathF.Max(MathF.Min(left[0], left[1]), MathF.Min(MathF.Max(left[0], left[1]), left[2]));
                float rm = MathF.Max(MathF.Min(right[0], right[1]), MathF.Min(MathF.Max(right[0], right[1]), right[2]));
                if (MathF.Abs(lm - 0.5f) + MathF.Abs(rm - 0.5f) < hRadius)
                {
                    int mask = EdgeBetweenTexels(left, right);
                    ProtectExtremeChannels(stencil, y * w + x, left, lm, mask);
                    ProtectExtremeChannels(stencil, y * w + x + 1, right, rm, mask);
                }
            }
        }
        // Vertical texel pairs
        for (int y = 0; y < h - 1; y++)
        {
            for (int x = 0; x < w; x++)
            {
                var bottom = sdf[x, y];
                var top = sdf[x, y + 1];
                float bm = MathF.Max(MathF.Min(bottom[0], bottom[1]), MathF.Min(MathF.Max(bottom[0], bottom[1]), bottom[2]));
                float tm = MathF.Max(MathF.Min(top[0], top[1]), MathF.Min(MathF.Max(top[0], top[1]), top[2]));
                if (MathF.Abs(bm - 0.5f) + MathF.Abs(tm - 0.5f) < vRadius)
                {
                    int mask = EdgeBetweenTexels(bottom, top);
                    ProtectExtremeChannels(stencil, y * w + x, bottom, bm, mask);
                    ProtectExtremeChannels(stencil, (y + 1) * w + x, top, tm, mask);
                }
            }
        }
        // Diagonal texel pairs
        for (int y = 0; y < h - 1; y++)
        {
            for (int x = 0; x < w - 1; x++)
            {
                var lb = sdf[x, y];
                var rb = sdf[x + 1, y];
                var lt = sdf[x, y + 1];
                var rt = sdf[x + 1, y + 1];
                float mlb = MathF.Max(MathF.Min(lb[0], lb[1]), MathF.Min(MathF.Max(lb[0], lb[1]), lb[2]));
                float mrb = MathF.Max(MathF.Min(rb[0], rb[1]), MathF.Min(MathF.Max(rb[0], rb[1]), rb[2]));
                float mlt = MathF.Max(MathF.Min(lt[0], lt[1]), MathF.Min(MathF.Max(lt[0], lt[1]), lt[2]));
                float mrt = MathF.Max(MathF.Min(rt[0], rt[1]), MathF.Min(MathF.Max(rt[0], rt[1]), rt[2]));
                if (MathF.Abs(mlb - 0.5f) + MathF.Abs(mrt - 0.5f) < dRadius)
                {
                    int mask = EdgeBetweenTexels(lb, rt);
                    ProtectExtremeChannels(stencil, y * w + x, lb, mlb, mask);
                    ProtectExtremeChannels(stencil, (y + 1) * w + x + 1, rt, mrt, mask);
                }
                if (MathF.Abs(mrb - 0.5f) + MathF.Abs(mlt - 0.5f) < dRadius)
                {
                    int mask = EdgeBetweenTexels(rb, lt);
                    ProtectExtremeChannels(stencil, y * w + x + 1, rb, mrb, mask);
                    ProtectExtremeChannels(stencil, (y + 1) * w + x, lt, mlt, mask);
                }
            }
        }
    }

    /// <summary>
    /// Checks if interpolating between two adjacent texels creates an artifact at a point
    /// where two color channels cross (creating a median extremum).
    /// Port of hasLinearArtifactInner from MSDFErrorCorrection.cpp.
    /// </summary>
    private static bool HasLinearArtifactInner(double span, bool isProtected, float am, float bm, Span<float> a, Span<float> b, float dA, float dB)
    {
        if (dA == dB) return false;
        double t = (double)dA / (dA - dB);
        if (t > ARTIFACT_T_EPSILON && t < 1 - ARTIFACT_T_EPSILON)
        {
            float xm = MedianInterpolated(a, b, t);
            int flags = RangeTest(span, isProtected, 0, 1, t, am, bm, xm);
            return (flags & 2) != 0; // CLASSIFIER_FLAG_ARTIFACT
        }
        return false;
    }

    /// <summary>
    /// Checks if a linear interpolation artifact occurs between two adjacent texels.
    /// Port of hasLinearArtifact from MSDFErrorCorrection.cpp.
    /// </summary>
    private static bool HasLinearArtifact(double span, bool isProtected, float am, Span<float> a, Span<float> b)
    {
        float bm = MathF.Max(MathF.Min(b[0], b[1]), MathF.Min(MathF.Max(b[0], b[1]), b[2]));
        return MathF.Abs(am - 0.5f) >= MathF.Abs(bm - 0.5f) && (
            HasLinearArtifactInner(span, isProtected, am, bm, a, b, a[1] - a[0], b[1] - b[0]) ||
            HasLinearArtifactInner(span, isProtected, am, bm, a, b, a[2] - a[1], b[2] - b[1]) ||
            HasLinearArtifactInner(span, isProtected, am, bm, a, b, a[0] - a[2], b[0] - b[2]));
    }

    /// <summary>
    /// Checks if a bilinear interpolation artifact occurs between two diagonally adjacent texels.
    /// Port of hasDiagonalArtifact / hasDiagonalArtifactInner from MSDFErrorCorrection.cpp.
    /// </summary>
    private static bool HasDiagonalArtifact(double span, bool isProtected, float am, Span<float> a, Span<float> b, Span<float> c, Span<float> d)
    {
        float dm = MathF.Max(MathF.Min(d[0], d[1]), MathF.Min(MathF.Max(d[0], d[1]), d[2]));
        if (MathF.Abs(am - 0.5f) < MathF.Abs(dm - 0.5f))
            return false;

        Span<float> abc = stackalloc float[3];
        Span<float> l = stackalloc float[3];
        Span<float> q = stackalloc float[3];
        Span<double> tEx = stackalloc double[3];
        for (int i = 0; i < 3; i++)
        {
            abc[i] = a[i] - b[i] - c[i];
            l[i] = -a[i] - abc[i];
            q[i] = d[i] + abc[i];
            tEx[i] = q[i] != 0 ? -0.5 * l[i] / q[i] : -1;
        }

        return
            HasDiagonalArtifactInner(span, isProtected, am, dm, a, l, q, a[1] - a[0], b[1] - b[0] + c[1] - c[0], d[1] - d[0], tEx[0], tEx[1]) ||
            HasDiagonalArtifactInner(span, isProtected, am, dm, a, l, q, a[2] - a[1], b[2] - b[1] + c[2] - c[1], d[2] - d[1], tEx[1], tEx[2]) ||
            HasDiagonalArtifactInner(span, isProtected, am, dm, a, l, q, a[0] - a[2], b[0] - b[2] + c[0] - c[2], d[0] - d[2], tEx[2], tEx[0]);
    }

    private static bool HasDiagonalArtifactInner(double span, bool isProtected, float am, float dm, Span<float> a, Span<float> l, Span<float> q, float dA, float dBC, float dD, double tEx0, double tEx1)
    {
        Span<double> t = stackalloc double[2];
        int solutions = MsdfMath.SolveQuadratic(t, dD - dBC + dA, dBC - dA - dA, dA);
        for (int i = 0; i < solutions; i++)
        {
            if (t[i] > ARTIFACT_T_EPSILON && t[i] < 1 - ARTIFACT_T_EPSILON)
            {
                float xm = MedianInterpolatedQuadratic(a, l, q, t[i]);
                int rangeFlags = RangeTest(span, isProtected, 0, 1, t[i], am, dm, xm);

                if (tEx0 > 0 && tEx0 < 1)
                {
                    double tEnd0 = 0, tEnd1 = 1;
                    float em0 = am, em1 = dm;
                    if (tEx0 > t[i]) { tEnd1 = tEx0; em1 = MedianInterpolatedQuadratic(a, l, q, tEx0); }
                    else { tEnd0 = tEx0; em0 = MedianInterpolatedQuadratic(a, l, q, tEx0); }
                    rangeFlags |= RangeTest(span, isProtected, tEnd0, tEnd1, t[i], em0, em1, xm);
                }
                if (tEx1 > 0 && tEx1 < 1)
                {
                    double tEnd0 = 0, tEnd1 = 1;
                    float em0 = am, em1 = dm;
                    if (tEx1 > t[i]) { tEnd1 = tEx1; em1 = MedianInterpolatedQuadratic(a, l, q, tEx1); }
                    else { tEnd0 = tEx1; em0 = MedianInterpolatedQuadratic(a, l, q, tEx1); }
                    rangeFlags |= RangeTest(span, isProtected, tEnd0, tEnd1, t[i], em0, em1, xm);
                }

                if ((rangeFlags & 2) != 0)
                    return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Port of BaseArtifactClassifier::rangeTest from MSDFErrorCorrection.cpp.
    /// </summary>
    private static int RangeTest(double span, bool isProtected, double at, double bt, double xt, float am, float bm, float xm)
    {
        // For protected texels, only consider inversion artifacts.
        // For unprotected, any out-of-range median is sufficient.
        if ((am > 0.5f && bm > 0.5f && xm <= 0.5f) ||
            (am < 0.5f && bm < 0.5f && xm >= 0.5f) ||
            (!isProtected && Median(am, bm, xm) != xm))
        {
            double axSpan = (xt - at) * span;
            double bxSpan = (bt - xt) * span;
            if (!(xm >= am - axSpan && xm <= am + axSpan && xm >= bm - bxSpan && xm <= bm + bxSpan))
                return 3; // CANDIDATE | ARTIFACT
            return 1; // CANDIDATE only
        }
        return 0;
    }

    private static float Median(float a, float b, float c)
    {
        return MathF.Max(MathF.Min(a, b), MathF.Min(MathF.Max(a, b), c));
    }

    private static float MedianInterpolated(Span<float> a, Span<float> b, double t)
    {
        float c0 = (float)(a[0] + t * (b[0] - a[0]));
        float c1 = (float)(a[1] + t * (b[1] - a[1]));
        float c2 = (float)(a[2] + t * (b[2] - a[2]));
        return MathF.Max(MathF.Min(c0, c1), MathF.Min(MathF.Max(c0, c1), c2));
    }

    private static float MedianInterpolatedQuadratic(Span<float> a, Span<float> l, Span<float> q, double t)
    {
        float c0 = (float)(t * (t * q[0] + l[0]) + a[0]);
        float c1 = (float)(t * (t * q[1] + l[1]) + a[1]);
        float c2 = (float)(t * (t * q[2] + l[2]) + a[2]);
        return MathF.Max(MathF.Min(c0, c1), MathF.Min(MathF.Max(c0, c1), c2));
    }

    /// <summary>
    /// Finds interpolation artifacts in the MSDF, respecting PROTECTED flags.
    /// Port of MSDFErrorCorrection::findErrors (SDF-only analysis).
    /// </summary>
    private static void FindErrors(
        byte[] stencil, MsdfBitmap sdf, int w, int h,
        Vector2Double scale, double rangeValue)
    {
        // Compute expected span (max distance change per texel) for each direction
        double hSpan = DEFAULT_MIN_DEVIATION_RATIO / (rangeValue * scale.x);
        double vSpan = DEFAULT_MIN_DEVIATION_RATIO / (rangeValue * scale.y);
        double dSpan = DEFAULT_MIN_DEVIATION_RATIO * Math.Sqrt(1.0 / (rangeValue * rangeValue * scale.x * scale.x) + 1.0 / (rangeValue * rangeValue * scale.y * scale.y));

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                var c = sdf[x, y];
                float cm = MathF.Max(MathF.Min(c[0], c[1]), MathF.Min(MathF.Max(c[0], c[1]), c[2]));
                bool isProtected = (stencil[y * w + x] & STENCIL_PROTECTED) != 0;

                bool isError =
                    (x > 0 && HasLinearArtifact(hSpan, isProtected, cm, c, sdf[x - 1, y])) ||
                    (y > 0 && HasLinearArtifact(vSpan, isProtected, cm, c, sdf[x, y - 1])) ||
                    (x < w - 1 && HasLinearArtifact(hSpan, isProtected, cm, c, sdf[x + 1, y])) ||
                    (y < h - 1 && HasLinearArtifact(vSpan, isProtected, cm, c, sdf[x, y + 1])) ||
                    (x > 0 && y > 0 && HasDiagonalArtifact(dSpan, isProtected, cm, c, sdf[x - 1, y], sdf[x, y - 1], sdf[x - 1, y - 1])) ||
                    (x < w - 1 && y > 0 && HasDiagonalArtifact(dSpan, isProtected, cm, c, sdf[x + 1, y], sdf[x, y - 1], sdf[x + 1, y - 1])) ||
                    (x > 0 && y < h - 1 && HasDiagonalArtifact(dSpan, isProtected, cm, c, sdf[x - 1, y], sdf[x, y + 1], sdf[x - 1, y + 1])) ||
                    (x < w - 1 && y < h - 1 && HasDiagonalArtifact(dSpan, isProtected, cm, c, sdf[x + 1, y], sdf[x, y + 1], sdf[x + 1, y + 1]));

                if (isError)
                    stencil[y * w + x] |= STENCIL_ERROR;
            }
        }
    }

    /// <summary>
    /// Applies the error correction: sets all ERROR-flagged texels to single-channel median.
    /// Port of MSDFErrorCorrection::apply.
    /// </summary>
    private static void ApplyCorrection(byte[] stencil, MsdfBitmap sdf, int w, int h)
    {
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                if ((stencil[y * w + x] & STENCIL_ERROR) != 0)
                {
                    var pixel = sdf[x, y];
                    float med = MathF.Max(MathF.Min(pixel[0], pixel[1]), MathF.Min(MathF.Max(pixel[0], pixel[1]), pixel[2]));
                    pixel[0] = med;
                    pixel[1] = med;
                    pixel[2] = med;
                }
            }
        }
    }
}
