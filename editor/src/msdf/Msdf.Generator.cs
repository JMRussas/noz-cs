//
//  Faithful port of msdfgen by Viktor Chlumsky
//  https://github.com/Chlumsky/msdfgen
//

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using static NoZ.Editor.Msdf.MsdfMath;

namespace NoZ.Editor.Msdf;

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

// Port of msdfgen's PerpendicularDistanceSelectorBase.
internal struct PerpendicularDistanceSelectorBase
{
    public SignedDistance minTrueDistance;
    public double minNegativePerpendicularDistance;
    public double minPositivePerpendicularDistance;
    public EdgeSegment? nearEdge;
    public double nearEdgeParam;

    public void Init()
    {
        minTrueDistance = new SignedDistance();
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

// Port of msdfgen's MultiDistanceSelector — one PerpendicularDistanceSelector per R/G/B channel.
internal struct MultiDistanceSelector
{
    public PerpendicularDistanceSelectorBase r, g, b;

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
    // Port of msdfgen's OverlappingContourCombiner + MultiDistanceSelector.
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

        var windings = new int[contourCount];
        for (int i = 0; i < contourCount; ++i)
            windings[i] = invertWinding ? -shape.contours[i].Winding() : shape.contours[i].Winding();

        Parallel.For(0, h, y =>
        {
            int row = flipY ? h - 1 - y : y;
            var contourSelectors = new MultiDistanceSelector[contourCount];

            for (int x = 0; x < w; ++x)
            {
                var p = new Vector2Double(x + 0.5, y + 0.5) / scale - translate;

                for (int ci = 0; ci < contourCount; ++ci)
                    contourSelectors[ci].Init();

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

                // OverlappingContourCombiner::distance()
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
                    result = shapeDistance;
                    var px0 = output[x, row];
                    px0[0] = (float)(distScale * (result.r + distTranslate));
                    px0[1] = (float)(distScale * (result.g + distTranslate));
                    px0[2] = (float)(distScale * (result.b + distTranslate));
                    continue;
                }

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

    // Simple nearest-edge-per-channel MSDF (no overlapping contour combiner).
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

    // Basic nearest-edge-per-channel MSDF using only true signed distance.
    // No perpendicular distance extension — avoids artifacts between separated contours.
    public static void GenerateMSDFBasic(
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
            for (int x = 0; x < w; ++x)
            {
                var p = new Vector2Double(x + 0.5, y + 0.5) / scale - translate;

                var minDistR = new SignedDistance();
                var minDistG = new SignedDistance();
                var minDistB = new SignedDistance();

                foreach (var contour in shape.contours)
                {
                    foreach (var edge in contour.edges)
                    {
                        SignedDistance dist = edge.GetSignedDistance(p, out _);

                        if (((int)edge.color & (int)EdgeColor.RED) != 0 && dist < minDistR)
                            minDistR = dist;
                        if (((int)edge.color & (int)EdgeColor.GREEN) != 0 && dist < minDistG)
                            minDistG = dist;
                        if (((int)edge.color & (int)EdgeColor.BLUE) != 0 && dist < minDistB)
                            minDistB = dist;
                    }
                }

                var pixel = output[x, y];
                pixel[0] = (float)(distScale * (minDistR.distance + distTranslate));
                pixel[1] = (float)(distScale * (minDistG.distance + distTranslate));
                pixel[2] = (float)(distScale * (minDistB.distance + distTranslate));
            }
        });
    }

    // Scanline-based sign correction. Flips MSDF pixels where the sign disagrees
    // with the non-zero winding fill state.
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

        // +1 = matched, -1 = flipped, 0 = ambiguous
        var matchMap = new sbyte[w * h];

        bool ambiguous = false;

        Span<double> ix = stackalloc double[3];
        Span<int> idy = stackalloc int[3];

        for (int y = 0; y < h; ++y)
        {
            int row = flipY ? h - 1 - y : y;
            double shapeY = (y + 0.5) / scale.y - translate.y;

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

                int winding = 0;
                for (int j = intersections.Count - 1; j >= 0; --j)
                {
                    if (intersections[j].x <= shapeX)
                    {
                        winding = intersections[j].direction;
                        break;
                    }
                }
                bool fill = winding != 0;

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

        // Resolve ambiguous pixels by neighbor majority
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

    // --- Error Correction (port of msdfgen's MSDFErrorCorrection) ---

    private const byte STENCIL_ERROR = 1;
    private const byte STENCIL_PROTECTED = 2;
    private const double ARTIFACT_T_EPSILON = 0.01;
    private const double PROTECTION_RADIUS_TOLERANCE = 1.001;
    private const double DEFAULT_MIN_DEVIATION_RATIO = 1.11111111111111111;

    // Modern error correction with corner and edge protection.
    // EDGE_PRIORITY mode, DO_NOT_CHECK_DISTANCE.
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
                // Color change = corner (at most one bit set in common)
                if ((commonColor & (commonColor - 1)) == 0)
                {
                    var shapePoint = edge.Point(0);
                    double px = scale.x * (shapePoint.x + translate.x) - 0.5;
                    double py = scale.y * (shapePoint.y + translate.y) - 0.5;

                    if (flipY)
                        py = h - 1 - py;

                    int l = (int)Math.Floor(px);
                    int b = (int)Math.Floor(py);
                    int r = l + 1;
                    int t = b + 1;

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

    // Bitmask of which channels contribute to a shape edge between two texels.
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
                float c0 = (float)(a[0] + t * (b[0] - a[0]));
                float c1 = (float)(a[1] + t * (b[1] - a[1]));
                float c2 = (float)(a[2] + t * (b[2] - a[2]));
                float med = MathF.Max(MathF.Min(c0, c1), MathF.Min(MathF.Max(c0, c1), c2));
                float cCh = ch == 0 ? c0 : ch == 1 ? c1 : c2;
                if (med == cCh)
                    mask |= (1 << ch);
            }
        }
        return mask;
    }

    private static void ProtectExtremeChannels(byte[] stencil, int idx, Span<float> msd, float m, int mask)
    {
        if ((mask & 1) != 0 && msd[0] != m ||
            (mask & 2) != 0 && msd[1] != m ||
            (mask & 4) != 0 && msd[2] != m)
        {
            stencil[idx] |= STENCIL_PROTECTED;
        }
    }

    private static void ProtectEdges(
        byte[] stencil, MsdfBitmap sdf, int w, int h,
        Vector2Double scale, double rangeValue)
    {
        // Radius = normalized distance change per texel in each direction.
        // Derived from msdfgen: distanceMapping(Delta(1)) = 1/rangeValue,
        // then unprojectVector divides by scale.
        float hRadius = (float)(PROTECTION_RADIUS_TOLERANCE / (rangeValue * scale.x));
        float vRadius = (float)(PROTECTION_RADIUS_TOLERANCE / (rangeValue * scale.y));
        float dRadius = (float)(PROTECTION_RADIUS_TOLERANCE * Math.Sqrt(1.0 / (rangeValue * rangeValue * scale.x * scale.x) + 1.0 / (rangeValue * rangeValue * scale.y * scale.y)));

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

    private static bool HasLinearArtifactInner(double span, bool isProtected, float am, float bm, Span<float> a, Span<float> b, float dA, float dB)
    {
        if (dA == dB) return false;
        double t = (double)dA / (dA - dB);
        if (t > ARTIFACT_T_EPSILON && t < 1 - ARTIFACT_T_EPSILON)
        {
            float xm = MedianInterpolated(a, b, t);
            int flags = RangeTest(span, isProtected, 0, 1, t, am, bm, xm);
            return (flags & 2) != 0;
        }
        return false;
    }

    private static bool HasLinearArtifact(double span, bool isProtected, float am, Span<float> a, Span<float> b)
    {
        float bm = MathF.Max(MathF.Min(b[0], b[1]), MathF.Min(MathF.Max(b[0], b[1]), b[2]));
        return MathF.Abs(am - 0.5f) >= MathF.Abs(bm - 0.5f) && (
            HasLinearArtifactInner(span, isProtected, am, bm, a, b, a[1] - a[0], b[1] - b[0]) ||
            HasLinearArtifactInner(span, isProtected, am, bm, a, b, a[2] - a[1], b[2] - b[1]) ||
            HasLinearArtifactInner(span, isProtected, am, bm, a, b, a[0] - a[2], b[0] - b[2]));
    }

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

    private static int RangeTest(double span, bool isProtected, double at, double bt, double xt, float am, float bm, float xm)
    {
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

    private static void FindErrors(
        byte[] stencil, MsdfBitmap sdf, int w, int h,
        Vector2Double scale, double rangeValue)
    {
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

    // Replace error-flagged texels with single-channel median.
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
