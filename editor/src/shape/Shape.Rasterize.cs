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

        const int maxPolyVerts = 256;
        Span<Vector2> polyVerts = stackalloc Vector2[maxPolyVerts];

        // Process each path independently, compositing in order
        for (ushort pIdx = 0; pIdx < PathCount; pIdx++)
        {
            ref var path = ref _paths[pIdx];
            if (path.AnchorCount < 3) continue;

            // Build polygon vertices and compute path bounds
            var vertCount = 0;
            var minX = float.MaxValue;
            var minY = float.MaxValue;
            var maxX = float.MinValue;
            var maxY = float.MinValue;

            for (ushort aIdx = 0; aIdx < path.AnchorCount && vertCount < maxPolyVerts; aIdx++)
            {
                var anchorIdx = (ushort)(path.AnchorStart + aIdx);
                ref var anchor = ref _anchors[anchorIdx];
                polyVerts[vertCount++] = anchor.Position;

                minX = MathF.Min(minX, anchor.Position.X);
                minY = MathF.Min(minY, anchor.Position.Y);
                maxX = MathF.Max(maxX, anchor.Position.X);
                maxY = MathF.Max(maxY, anchor.Position.Y);

                if (MathF.Abs(anchor.Curve) > 0.0001f)
                {
                    var samples = GetSegmentSamples(anchorIdx);
                    for (var s = 0; s < MaxSegmentSamples && vertCount < maxPolyVerts; s++)
                    {
                        polyVerts[vertCount++] = samples[s];
                        minX = MathF.Min(minX, samples[s].X);
                        minY = MathF.Min(minY, samples[s].Y);
                        maxX = MathF.Max(maxX, samples[s].X);
                        maxY = MathF.Max(maxY, samples[s].Y);
                    }
                }
            }

            if (vertCount < 3) continue;

            // Convert path bounds to pixel coordinates with AA margin
            var aaMargin = 1f / dpi;
            var pathMinX = (int)MathF.Floor((minX - aaMargin) * dpi) - rb.X;
            var pathMinY = (int)MathF.Floor((minY - aaMargin) * dpi) - rb.Y;
            var pathMaxX = (int)MathF.Ceiling((maxX + aaMargin) * dpi) - rb.X;
            var pathMaxY = (int)MathF.Ceiling((maxY + aaMargin) * dpi) - rb.Y;

            // Clamp to raster bounds
            pathMinX = Math.Max(0, pathMinX);
            pathMinY = Math.Max(0, pathMinY);
            pathMaxX = Math.Min(rb.Width - 1, pathMaxX);
            pathMaxY = Math.Min(rb.Height - 1, pathMaxY);

            if (pathMinX > pathMaxX || pathMinY > pathMaxY) continue;

            var pathVerts = polyVerts[..vertCount];
            var isHole = (path.Flags & PathFlags.Subtract) != 0;
            var fillColor = isHole
                ? Color32.Transparent
                : palette[path.FillColor % palette.Length].ToColor32().WithAlpha(path.FillOpacity);

            // Render only within this path's bounds
            for (var y = pathMinY; y <= pathMaxY; y++)
            {
                var py = offset.Y + rb.Y + y;
                if (py < 0 || py >= pixels.Height) continue;

                var pixelY = (rb.Y + y + 0.5f) / dpi;

                for (var x = pathMinX; x <= pathMaxX; x++)
                {
                    var px = offset.X + rb.X + x;
                    if (px < 0 || px >= pixels.Width) continue;

                    var pixelX = (rb.X + x + 0.5f) / dpi;
                    var worldPoint = new Vector2(pixelX, pixelY);

                    // Compute signed distance to this path's edge
                    var signedDist = GetSignedDistanceToPolygon(worldPoint, pathVerts);
                    var pixelDist = signedDist * dpi;
                    var coverage = DistanceToAlpha(pixelDist);

                    if (coverage <= 0f) continue;

                    ref var dst = ref pixels[px, py];

                    if (isHole)
                    {
                        // Subtract: reduce alpha of existing pixel
                        if (dst.A > 0)
                        {
                            var newAlpha = (byte)(dst.A * (1f - coverage));
                            dst = new Color32(dst.R, dst.G, dst.B, newAlpha);
                        }
                    }
                    else
                    {
                        // Composite this path's color with coverage
                        var srcAlpha = (byte)(fillColor.A * coverage);
                        if (srcAlpha == 0) continue;

                        if (dst.A == 0)
                        {
                            dst = new Color32(fillColor.R, fillColor.G, fillColor.B, srcAlpha);
                        }
                        else
                        {
                            // Alpha compositing: src over dst
                            var srcA = srcAlpha / 255f;
                            var dstA = dst.A / 255f;
                            var outA = srcA + dstA * (1f - srcA);

                            if (outA > 0f)
                            {
                                var r = (fillColor.R * srcA + dst.R * dstA * (1f - srcA)) / outA;
                                var g = (fillColor.G * srcA + dst.G * dstA * (1f - srcA)) / outA;
                                var b = (fillColor.B * srcA + dst.B * dstA * (1f - srcA)) / outA;
                                dst = new Color32((byte)r, (byte)g, (byte)b, (byte)(outA * 255f));
                            }
                        }
                    }
                }
            }
        }
    }

    private static float GetSignedDistanceToPolygon(Vector2 point, Span<Vector2> verts)
    {
        var vertCount = verts.Length;
        if (vertCount < 3) return float.MaxValue;

        // Find minimum distance to polygon edges
        var minDistSqr = float.MaxValue;
        for (var i = 0; i < vertCount; i++)
        {
            var distSqr = PointToSegmentDistSqrFast(point, verts[i], verts[(i + 1) % vertCount]);
            if (distSqr < minDistSqr)
                minDistSqr = distSqr;
        }

        var inside = IsPointInPolygonFast(point, verts);
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
