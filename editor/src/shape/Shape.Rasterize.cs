//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ.Editor;

public sealed partial class Shape
{
    private const float AntiAliasEdgeInner = -0.5f;
    private const float AntiAliasEdgeOuter = 0.5f;

    public struct RasterizeOptions
    {
        public bool AntiAlias;

        public static readonly RasterizeOptions Default = new() { AntiAlias = false };
    }

    public void Rasterize(PixelData<Color32> pixels, Color[] palette, Vector2Int offset)
        => Rasterize(pixels, palette, offset, RasterizeOptions.Default);

    public void Rasterize(PixelData<Color32> pixels, Color[] palette, Vector2Int offset, RasterizeOptions options)
    {
        if (PathCount == 0) return;

        if (options.AntiAlias)
        {
            RasterizeAA(pixels, palette, offset);
            return;
        }

        const int maxPolyVerts = 256;
        Span<Vector2> polyVerts = stackalloc Vector2[maxPolyVerts];
        var dpi = EditorApplication.Config.PixelsPerUnit;

        for (ushort pIdx = 0; pIdx < PathCount; pIdx++)
        {
            ref var path = ref _paths[pIdx];
            if (path.AnchorCount < 3) continue;

            var vertexCount = 0;

            for (ushort aIdx = 0; aIdx < path.AnchorCount && vertexCount < maxPolyVerts; aIdx++)
            {
                var anchorIdx = (ushort)(path.AnchorStart + aIdx);
                ref var anchor = ref _anchors[anchorIdx];

                var worldPos = anchor.Position;
                polyVerts[vertexCount++] = worldPos * dpi;

                if (MathF.Abs(anchor.Curve) > 0.0001f)
                {
                    var samples = GetSegmentSamples(anchorIdx);
                    for (var s = 0; s < MaxSegmentSamples && vertexCount < maxPolyVerts; s++)
                    {
                        var sampleWorld = samples[s];
                        polyVerts[vertexCount++] = sampleWorld * dpi;
                    }
                }
            }

            if (vertexCount < 3) continue;

            var isSubtract = path.IsSubtract;
            var fillColor = isSubtract
                ? Color32.Transparent
                : palette[path.FillColor % palette.Length].ToColor32().WithAlpha(path.FillOpacity);
            var rb = RasterBounds;

            RasterizePath(pixels, polyVerts[..vertexCount], fillColor, offset, rb, isSubtract);
        }
    }

    private static void RasterizePath(
        PixelData<Color32> pixels,
        Span<Vector2> polyVerts,
        Color32 fillColor,
        Vector2Int offset,
        RectInt rb,
        bool isHole = false)
    {
        var vertCount = polyVerts.Length;

        // Each edge can generate at most one intersection per scanline
        // Store X position and direction (+1 upward, -1 downward) for winding rule
        Span<(float x, int dir)> intersections = vertCount <= 32
            ? stackalloc (float, int)[vertCount]
            : new (float, int)[vertCount];

        for (var y = 0; y < rb.Height; y++)
        {
            var py = offset.Y + rb.Y + y;
            if (py < 0 || py >= pixels.Height) continue;

            var scanlineY = rb.Y + y + 0.5f;
            var intersectionCount = 0;

            for (var i = 0; i < vertCount; i++)
            {
                var p0 = polyVerts[i];
                var p1 = polyVerts[(i + 1) % vertCount];

                // Edge going upward (p0 below, p1 above)
                if (p0.Y <= scanlineY && p1.Y > scanlineY)
                {
                    var t = (scanlineY - p0.Y) / (p1.Y - p0.Y);
                    intersections[intersectionCount++] = (p0.X + t * (p1.X - p0.X), 1);
                }
                // Edge going downward (p0 above, p1 below)
                else if (p1.Y <= scanlineY && p0.Y > scanlineY)
                {
                    var t = (scanlineY - p0.Y) / (p1.Y - p0.Y);
                    intersections[intersectionCount++] = (p0.X + t * (p1.X - p0.X), -1);
                }
            }

            if (intersectionCount == 0) continue;

            // Sort by X coordinate
            var span = intersections[..intersectionCount];
            span.Sort((a, b) => a.x.CompareTo(b.x));

            // Fill using non-zero winding rule
            // Pixel at local x has center at (rb.X + x + 0.5), fill if center is inside polygon
            var winding = 0;
            var entryX = 0;

            for (var i = 0; i < intersectionCount; i++)
            {
                var wasInside = winding != 0;
                winding += intersections[i].dir;
                var isInside = winding != 0;

                if (!wasInside && isInside)
                {
                    // Entering polygon: first pixel where center > intersection
                    // center = rb.X + x + 0.5 > intersectionX  =>  x > intersectionX - rb.X - 0.5
                    entryX = (int)MathF.Ceiling(intersections[i].x - rb.X - 0.5f);
                }
                else if (wasInside && !isInside)
                {
                    // Exiting polygon: last pixel where center < intersection
                    // center = rb.X + x + 0.5 < intersectionX  =>  x < intersectionX - rb.X - 0.5
                    var exitX = (int)MathF.Ceiling(intersections[i].x - rb.X - 0.5f) - 1;

                    var xStart = Math.Max(entryX, 0);
                    var xEnd = Math.Min(exitX, rb.Width - 1);

                    for (var x = xStart; x <= xEnd; x++)
                    {
                        var px = offset.X + rb.X + x;
                        if (px < 0 || px >= pixels.Width) continue;

                        ref var dst = ref pixels[px, py];
                        if (isHole)
                            dst = Color32.Transparent;
                        else if (fillColor.A == 255 || dst.A == 0)
                            dst = fillColor;
                        else if (fillColor.A > 0)
                            dst = Color32.Blend(dst, fillColor);
                    }
                }
            }
        }
    }

    private void RasterizeAA(PixelData<Color32> pixels, Color[] palette, Vector2Int offset)
    {
        if (PathCount == 0) return;

        var dpi = EditorApplication.Config.PixelsPerUnit;
        var rb = RasterBounds;
        var rbWidth = rb.Width;
        var rbHeight = rb.Height;

        // Pre-build polygon data for all paths to avoid repeated allocations
        const int maxPolyVerts = 256;
        Span<int> pathVertCounts = stackalloc int[PathCount];
        Span<Vector2> allPolyVerts = stackalloc Vector2[PathCount * maxPolyVerts];

        for (ushort pIdx = 0; pIdx < PathCount; pIdx++)
        {
            ref var path = ref _paths[pIdx];
            if (path.AnchorCount < 3)
            {
                pathVertCounts[pIdx] = 0;
                continue;
            }

            var vertOffset = pIdx * maxPolyVerts;
            var vertCount = 0;

            for (ushort aIdx = 0; aIdx < path.AnchorCount && vertCount < maxPolyVerts; aIdx++)
            {
                var anchorIdx = (ushort)(path.AnchorStart + aIdx);
                ref var anchor = ref _anchors[anchorIdx];
                allPolyVerts[vertOffset + vertCount++] = anchor.Position;

                if (MathF.Abs(anchor.Curve) > 0.0001f)
                {
                    var samples = GetSegmentSamples(anchorIdx);
                    for (var s = 0; s < MaxSegmentSamples && vertCount < maxPolyVerts; s++)
                        allPolyVerts[vertOffset + vertCount++] = samples[s];
                }
            }
            pathVertCounts[pIdx] = vertCount;
        }

        // Allocate coverage buffer for edge detection
        var coverageSize = rbWidth * rbHeight;
        Span<byte> insideMask = coverageSize <= 4096
            ? stackalloc byte[coverageSize]
            : new byte[coverageSize];
        insideMask.Clear();

        // First pass: mark interior pixels using fast point-in-polygon
        for (var y = 0; y < rbHeight; y++)
        {
            var pixelY = (rb.Y + y + 0.5f) / dpi;

            for (var x = 0; x < rbWidth; x++)
            {
                var pixelX = (rb.X + x + 0.5f) / dpi;
                var worldPoint = new Vector2(pixelX, pixelY);
                var maskIdx = y * rbWidth + x;
                byte inside = 0;

                for (ushort pIdx = 0; pIdx < PathCount; pIdx++)
                {
                    var vertCount = pathVertCounts[pIdx];
                    if (vertCount < 3) continue;

                    var vertOffset = pIdx * maxPolyVerts;
                    var polyVerts = allPolyVerts.Slice(vertOffset, vertCount);

                    if (IsPointInPolygonFast(worldPoint, polyVerts))
                    {
                        ref var path = ref _paths[pIdx];
                        var isHole = (path.Flags & PathFlags.Subtract) != 0;
                        inside = isHole ? (byte)0 : (byte)(pIdx + 1);
                    }
                }
                insideMask[maskIdx] = inside;
            }
        }

        // Second pass: render with AA only on edge pixels
        for (var y = 0; y < rbHeight; y++)
        {
            var py = offset.Y + rb.Y + y;
            if (py < 0 || py >= pixels.Height) continue;

            for (var x = 0; x < rbWidth; x++)
            {
                var px = offset.X + rb.X + x;
                if (px < 0 || px >= pixels.Width) continue;

                var maskIdx = y * rbWidth + x;
                var currentMask = insideMask[maskIdx];

                // Check if this is an edge pixel (any 8-connected neighbor has different mask value)
                var isEdge = false;
                var hasLeft = x > 0;
                var hasRight = x < rbWidth - 1;
                var hasUp = y > 0;
                var hasDown = y < rbHeight - 1;

                if (hasLeft && insideMask[maskIdx - 1] != currentMask) isEdge = true;
                else if (hasRight && insideMask[maskIdx + 1] != currentMask) isEdge = true;
                else if (hasUp && insideMask[maskIdx - rbWidth] != currentMask) isEdge = true;
                else if (hasDown && insideMask[maskIdx + rbWidth] != currentMask) isEdge = true;
                else if (hasLeft && hasUp && insideMask[maskIdx - rbWidth - 1] != currentMask) isEdge = true;
                else if (hasRight && hasUp && insideMask[maskIdx - rbWidth + 1] != currentMask) isEdge = true;
                else if (hasLeft && hasDown && insideMask[maskIdx + rbWidth - 1] != currentMask) isEdge = true;
                else if (hasRight && hasDown && insideMask[maskIdx + rbWidth + 1] != currentMask) isEdge = true;

                ref var dst = ref pixels[px, py];

                if (!isEdge)
                {
                    // Interior or exterior pixel - no AA needed
                    if (currentMask > 0)
                    {
                        var pathIdx = (ushort)(currentMask - 1);
                        ref var path = ref _paths[pathIdx];
                        var fillColor = palette[path.FillColor % palette.Length].ToColor32().WithAlpha(path.FillOpacity);

                        if (fillColor.A == 255 || dst.A == 0)
                            dst = fillColor;
                        else if (fillColor.A > 0)
                            dst = Color32.Blend(dst, fillColor);
                    }
                }
                else
                {
                    // Edge pixel - compute accurate SDF coverage
                    var pixelX = rb.X + x + 0.5f;
                    var pixelY = rb.Y + y + 0.5f;
                    var worldPoint = new Vector2(pixelX / dpi, pixelY / dpi);

                    // For inside edge pixels, we want full coverage to avoid dark fringes
                    // Only compute partial alpha for outside edge pixels
                    if (currentMask > 0)
                    {
                        // Inside edge pixel - use full coverage with the stored path color
                        var pathIdx = (ushort)(currentMask - 1);
                        ref var path = ref _paths[pathIdx];
                        var fillColor = palette[path.FillColor % palette.Length].ToColor32().WithAlpha(path.FillOpacity);

                        if (fillColor.A == 255 || dst.A == 0)
                            dst = fillColor;
                        else if (fillColor.A > 0)
                            dst = Color32.Blend(dst, fillColor);
                    }
                    else
                    {
                        // Outside edge pixel - compute partial coverage for AA
                        var (fillColor, alpha) = ComputePixelCoverageOptimized(
                            worldPoint, palette, dpi, allPolyVerts, pathVertCounts, maxPolyVerts);

                        if (alpha <= 0f) continue;

                        // Write the fill color with partial alpha - direct overwrite
                        // The alpha channel handles compositing at display time
                        var srcAlpha = (byte)(fillColor.A * alpha);
                        dst = new Color32(fillColor.R, fillColor.G, fillColor.B, srcAlpha);
                    }
                }
            }
        }
    }

    private (Color32 fillColor, float alpha) ComputePixelCoverageOptimized(
        Vector2 worldPoint, Color[] palette, float dpi,
        Span<Vector2> allPolyVerts, Span<int> pathVertCounts, int maxPolyVerts)
    {
        var resultAlpha = 0f;
        var resultColor = Color32.Transparent;

        for (ushort pIdx = 0; pIdx < PathCount; pIdx++)
        {
            ref var path = ref _paths[pIdx];
            var vertCount = pathVertCounts[pIdx];
            if (vertCount < 3) continue;

            var vertOffset = pIdx * maxPolyVerts;
            var polyVerts = allPolyVerts.Slice(vertOffset, vertCount);

            // Compute signed distance using pre-built polygon
            var signedDist = GetPathSignedDistanceFast(worldPoint, pIdx, polyVerts);
            var pixelDist = signedDist * dpi;
            var pathAlpha = DistanceToAlpha(pixelDist);

            if (pathAlpha <= 0f) continue;

            var isHole = (path.Flags & PathFlags.Subtract) != 0;

            if (isHole)
            {
                resultAlpha *= (1f - pathAlpha);
            }
            else
            {
                var pathColor = palette[path.FillColor % palette.Length].ToColor32().WithAlpha(path.FillOpacity);

                if (resultAlpha <= 0f)
                {
                    resultColor = pathColor;
                    resultAlpha = pathAlpha;
                }
                else
                {
                    var newAlpha = pathAlpha + resultAlpha * (1f - pathAlpha);
                    if (newAlpha > 0f)
                    {
                        var t = pathAlpha / newAlpha;
                        resultColor = new Color32(
                            (byte)(pathColor.R * t + resultColor.R * (1f - t)),
                            (byte)(pathColor.G * t + resultColor.G * (1f - t)),
                            (byte)(pathColor.B * t + resultColor.B * (1f - t)),
                            resultColor.A
                        );
                    }
                    resultAlpha = newAlpha;
                }
            }
        }

        return (resultColor, Math.Clamp(resultAlpha, 0f, 1f));
    }

    private float GetPathSignedDistanceFast(Vector2 point, ushort pathIndex, Span<Vector2> polyVerts)
    {
        ref var path = ref _paths[pathIndex];
        var vertCount = polyVerts.Length;
        if (vertCount < 3) return float.MaxValue;

        // Find minimum distance to polygon edges
        var minDistSqr = float.MaxValue;

        for (var i = 0; i < vertCount; i++)
        {
            var p0 = polyVerts[i];
            var p1 = polyVerts[(i + 1) % vertCount];

            var distSqr = PointToSegmentDistSqrFast(point, p0, p1);
            if (distSqr < minDistSqr)
                minDistSqr = distSqr;
        }

        // Determine inside/outside using winding number
        var inside = IsPointInPolygonFast(point, polyVerts);
        return inside ? -MathF.Sqrt(minDistSqr) : MathF.Sqrt(minDistSqr);
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    private static float PointToSegmentDistSqrFast(Vector2 point, Vector2 a, Vector2 b)
    {
        var abX = b.X - a.X;
        var abY = b.Y - a.Y;
        var apX = point.X - a.X;
        var apY = point.Y - a.Y;

        var abLenSqr = abX * abX + abY * abY;
        if (abLenSqr < 0.0001f)
            return apX * apX + apY * apY;

        var t = (apX * abX + apY * abY) / abLenSqr;
        t = t < 0f ? 0f : (t > 1f ? 1f : t);

        var closestX = a.X + abX * t;
        var closestY = a.Y + abY * t;
        var dx = point.X - closestX;
        var dy = point.Y - closestY;

        return dx * dx + dy * dy;
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    private static bool IsPointInPolygonFast(Vector2 point, Span<Vector2> verts)
    {
        var winding = 0;
        var count = verts.Length;
        var pointX = point.X;
        var pointY = point.Y;

        for (var i = 0; i < count; i++)
        {
            var p0 = verts[i];
            var p1 = verts[(i + 1) % count];

            if (p0.Y <= pointY)
            {
                if (p1.Y > pointY)
                {
                    var cross = (p1.X - p0.X) * (pointY - p0.Y) - (pointX - p0.X) * (p1.Y - p0.Y);
                    if (cross >= 0) winding++;
                }
            }
            else if (p1.Y <= pointY)
            {
                var cross = (p1.X - p0.X) * (pointY - p0.Y) - (pointX - p0.X) * (p1.Y - p0.Y);
                if (cross < 0) winding--;
            }
        }

        return winding != 0;
    }

    private static float DistanceToAlpha(float signedDistancePixels)
    {
        if (signedDistancePixels <= AntiAliasEdgeInner)
            return 1f;
        if (signedDistancePixels >= AntiAliasEdgeOuter)
            return 0f;

        var t = (signedDistancePixels - AntiAliasEdgeInner) / (AntiAliasEdgeOuter - AntiAliasEdgeInner);
        return 1f - MathEx.SmoothStep(t);
    }
}
