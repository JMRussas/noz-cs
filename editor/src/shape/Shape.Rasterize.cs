//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;

namespace NoZ.Editor;

public sealed partial class Shape
{
    private const float AntiAliasEdgeInner = -0.5f;
    private const float AntiAliasEdgeOuter = 0.5f;

    private const float DefaultStrokeWidth = 0.0f;
    private static readonly Color32 DefaultStrokeColor = Color32.Black;

    public struct RasterizeOptions
    {
        public bool AntiAlias;

        public static readonly RasterizeOptions Default = new() { AntiAlias = false };
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ScanlineIntersection(float x, float xMin, float xMax, int dir)
    {
        public float X = x;         // Intersection at row center (for winding/sorting)
        public float XMin = xMin;   // Min X within this row (for AA)
        public float XMax = xMax;   // Max X within this row (for AA)
        public int Direction = dir;
    }

    public void Rasterize(
        PixelData<Color32> target,
        RectInt targetRect,
        Vector2Int sourceOffset,
        Color[] palette,
        Vector2Int offset)
        => Rasterize(target, targetRect, sourceOffset, palette, RasterizeOptions.Default);

    public void Rasterize(
        PixelData<Color32> target,
        RectInt targetRect,
        Vector2Int sourceOffset,
        Color[] palette,
        RasterizeOptions options)
    {
        if (PathCount == 0) return;

        Span<Vector2> polyVerts = stackalloc Vector2[MaxAnchorsPerPath];
        Span<ScanlineIntersection> intersections = stackalloc ScanlineIntersection[MaxAnchorsPerPath];
        var dpi = EditorApplication.Config.PixelsPerUnit;
        var antiAlias = options.AntiAlias;

        for (ushort pathIndex = 0; pathIndex < PathCount; pathIndex++)
        {
            ref var path = ref _paths[pathIndex];
            if (path.AnchorCount < 3) continue;

            var vertCount = GetRasterVerts(ref path, ref polyVerts, dpi, out var minY, out var maxY);
            if (vertCount < 3) continue;

            var subtract = path.IsSubtract;
            var fillColor = subtract
                ? Color32.Transparent
                : palette[path.FillColor % palette.Length].ToColor32().WithAlpha(path.FillOpacity);
            var rb = RasterBounds;

            RasterizePath(
                target,
                targetRect,
                sourceOffset,
                polyVerts[..vertCount],
                fillColor,
                subtract,
                antiAlias);

            //RasterizeStroke(
            //    target,
            //    targetRect,
            //    sourceOffset,
            //    polyVerts[..vertCount],
            //    DefaultStrokeColor,
            //    DefaultStrokeWidth,
            //    antiAlias);
        }
    }

    private static void RasterizePath(
        PixelData<Color32> target,
        RectInt targetRect,
        Vector2Int sourceOffset,
        Span<Vector2> verts,
        Color32 fillColor,
        bool subtract,
        bool antiAlias)
    {
        var vertCount = verts.Length;

        if (fillColor.A == 0 && !subtract)
            return;

        Span<ScanlineIntersection> intersections = stackalloc ScanlineIntersection[vertCount];
        for (var y = 0; y < targetRect.Height; y++)
        {
            var ty = targetRect.Y + y;
            var sy = -sourceOffset.Y + y;

            var intersectionCount = GetScanlineIntersections(verts, sy + 0.5f, intersections);
            if (intersectionCount == 0) continue;

            RasterizeScanline(
                target,
                targetRect,
                y,
                sourceOffset,
                intersections[..intersectionCount],
                verts,
                fillColor,
                subtract,
                antiAlias);
        }
    }

    private static void RasterizeStroke(
        PixelData<Color32> target,
        RectInt targetRect,
        Vector2Int sourceOffset,
        ReadOnlySpan<Vector2> verts,
        Color32 strokeColor,
        float strokeWidth,
        bool antiAlias)
    {
        if (strokeWidth <= 0f || strokeColor.A == 0)
            return;

        var halfWidth = strokeWidth * 0.5f;
        var aaMargin = antiAlias ? 1f : 0f;
        var maxDist = halfWidth + aaMargin;

        for (var i = 0; i < verts.Length; i++)
        {
            var p0 = verts[i];
            var p1 = verts[(i + 1) % verts.Length];

            var minX = (int)MathF.Floor(MathF.Min(p0.X, p1.X) - maxDist);
            var maxX = (int)MathF.Ceiling(MathF.Max(p0.X, p1.X) + maxDist);
            var minY = (int)MathF.Floor(MathF.Min(p0.Y, p1.Y) - maxDist);
            var maxY = (int)MathF.Ceiling(MathF.Max(p0.Y, p1.Y) + maxDist);

            minX = int.Max(minX, -sourceOffset.X);
            maxX = int.Min(maxX, -sourceOffset.X + targetRect.Width);
            minY = int.Max(minY, -sourceOffset.Y);
            maxY = int.Min(maxY, -sourceOffset.Y + targetRect.Height);

            for (var sy = minY; sy < maxY; sy++)
            {
                for (var sx = minX; sx < maxX; sx++)
                {
                    var sourcePoint = new Vector2(sx + 0.5f, sy + 0.5f);
                    var distSqr = PointToSegmentDistSqrFast(sourcePoint, p0, p1);
                    var dist = MathF.Sqrt(distSqr);

                    if (dist > maxDist)
                        continue;

                    var targetX = sourceOffset.X + sx + targetRect.X;
                    var targetY = sourceOffset.Y + sy + targetRect.Y;

                    if (targetX < targetRect.X || targetX >= targetRect.X + targetRect.Width)
                        continue;
                    if (targetY < targetRect.Y || targetY >= targetRect.Y + targetRect.Height)
                        continue;

                    if (antiAlias)
                    {
                        var signedDist = dist - halfWidth;
                        var alpha = DistanceToAlpha(signedDist);

                        if (alpha <= 0.001f)
                            continue;

                        if (alpha >= 0.999f)
                            target[targetX, targetY] = Color32.Blend(target[targetX, targetY], strokeColor);
                        else
                            AntiAliasPixel(ref target[targetX, targetY], strokeColor, alpha, false);
                    }
                    else
                    {
                        if (dist <= halfWidth)
                            target[targetX, targetY] = Color32.Blend(target[targetX, targetY], strokeColor);
                    }
                }
            }
        }
    }

    private static void RasterizeScanline(
        in PixelData<Color32> target,
        in RectInt targetRect,
        int scanlineY,
        in Vector2Int sourceOffset,
        in ReadOnlySpan<ScanlineIntersection> intersections,
        in ReadOnlySpan<Vector2> verts,
        Color32 color,
        bool subtract,
        bool antiAlias)
    {
        var winding = 0;
        var sourceMin = 0;
        var sourceMinXMin = 0f;
        var sourceMinXMax = 0f;

        for (var i = 0; i < intersections.Length; i++)
        {
            var wasInside = winding != 0;
            winding += intersections[i].Direction;
            var isInside = winding != 0;

            if (wasInside == isInside) continue;

            if (isInside)
            {
                sourceMin = (int)MathF.Ceiling(intersections[i].X - 0.5f);
                sourceMinXMin = intersections[i].XMin;
                sourceMinXMax = intersections[i].XMax;
                continue;
            }

            var sourceMax = (int)MathF.Floor(intersections[i].X + 0.5f);
            var sourceMaxXMin = intersections[i].XMin;
            var sourceMaxXMax = intersections[i].XMax;

            var targetMin = int.Max(targetRect.X, sourceOffset.X + sourceMin + targetRect.X);
            var targetMax = int.Min(targetRect.X + targetRect.Width, sourceOffset.X + sourceMax + targetRect.X);
            var targetWidth = targetMax - targetMin;

            if (targetWidth <= 0)
                continue;

            if (antiAlias)
            {
                var sourceY = -sourceOffset.Y + scanlineY + 0.5f;

                var solidMin = (int)MathF.Ceiling(sourceMinXMax - 0.5f);
                var solidMax = (int)MathF.Floor(sourceMaxXMin + 0.5f);

                var leftAAStart = (int)MathF.Floor(sourceMinXMin - 0.5f);
                var leftAAEnd = (int)MathF.Floor(sourceMinXMax + 0.5f);
                for (var px = leftAAStart; px <= leftAAEnd; px++)
                {
                    var targetX = sourceOffset.X + px + targetRect.X;
                    if (targetX < targetRect.X || targetX >= targetRect.X + targetRect.Width)
                        continue;
                    if (px >= solidMin && px < solidMax)
                        continue;
                    var sourcePoint = new Vector2(px + 0.5f, sourceY);
                    var alpha = GetAntiAliasedAlpha(sourcePoint, verts);
                    if (alpha <= 0.001f)
                        continue;
                    if (alpha >= 0.999f)
                    {
                        if (subtract)
                            target[targetX, targetRect.Y + scanlineY] = Color32.Transparent;
                        else
                            target[targetX, targetRect.Y + scanlineY] = color;
                    }
                    else
                        AntiAliasPixel(ref target[targetX, targetRect.Y + scanlineY], color, alpha, subtract);
                }

                var rightAAStart = (int)MathF.Floor(sourceMaxXMin - 0.5f);
                var rightAAEnd = (int)MathF.Floor(sourceMaxXMax + 0.5f);
                for (var px = rightAAStart; px <= rightAAEnd; px++)
                {
                    var targetX = sourceOffset.X + px + targetRect.X;
                    if (targetX < targetRect.X || targetX >= targetRect.X + targetRect.Width)
                        continue;
                    if (px >= solidMin && px < solidMax)
                        continue;
                    var sourcePoint = new Vector2(px + 0.5f, sourceY);
                    var alpha = GetAntiAliasedAlpha(sourcePoint, verts);
                    if (alpha <= 0.001f)
                        continue;
                    if (alpha >= 0.999f)
                    {
                        if (subtract)
                            target[targetX, targetRect.Y + scanlineY] = Color32.Transparent;
                        else
                            target[targetX, targetRect.Y + scanlineY] = color;
                    }
                    else
                        AntiAliasPixel(ref target[targetX, targetRect.Y + scanlineY], color, alpha, subtract);
                }

                var solidTargetMin = int.Max(targetRect.X, sourceOffset.X + solidMin + targetRect.X);
                var solidTargetMax = int.Min(targetRect.X + targetRect.Width, sourceOffset.X + solidMax + targetRect.X);
                var solidWidth = solidTargetMax - solidTargetMin;

                if (solidWidth > 0)
                {
                    if (subtract)
                        Subtract(target, solidTargetMin, targetRect.Y + scanlineY, solidWidth);
                    else
                        Fill(target, solidTargetMin, targetRect.Y + scanlineY, solidWidth, color);
                }
            }
            else
            {
                if (subtract)
                    Subtract(target, targetMin, targetRect.Y + scanlineY, targetWidth);
                else
                    Fill(target, targetMin, targetRect.Y + scanlineY, targetWidth, color);
            }
        }
    }

    private static int GetScanlineIntersections(
        ReadOnlySpan<Vector2> verts,
        float scanlineY,
        Span<ScanlineIntersection> intersections)
    {
        var rowTop = scanlineY - 0.5f;
        var rowBottom = scanlineY + 0.5f;
        var intersectionCount = 0;

        for (var i = 0; i < verts.Length; i++)
        {
            ref readonly var p0 = ref verts[i];
            ref readonly var p1 = ref verts[(i + 1) % verts.Length];

            // Edge going upward (p0 below, p1 above)
            if (p0.Y <= scanlineY && p1.Y > scanlineY)
            {
                var t = (scanlineY - p0.Y) / (p1.Y - p0.Y);
                var x = p0.X + t * (p1.X - p0.X);
                GetEdgeXRange(p0, p1, rowTop, rowBottom, out var xMin, out var xMax);
                intersections[intersectionCount++] = new ScanlineIntersection(x, xMin, xMax, 1);
                continue;
            }

            // Edge going downward (p0 above, p1 below)
            if (p1.Y <= scanlineY && p0.Y > scanlineY)
            {
                var t = (scanlineY - p0.Y) / (p1.Y - p0.Y);
                var x = p0.X + t * (p1.X - p0.X);
                GetEdgeXRange(p0, p1, rowTop, rowBottom, out var xMin, out var xMax);
                intersections[intersectionCount++] = new ScanlineIntersection(x, xMin, xMax, -1);
                continue;
            }
        }

        if (intersectionCount == 0)
            return 0;

        intersections[..intersectionCount].Sort((a, b) => a.X.CompareTo(b.X));
        return intersectionCount;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void GetEdgeXRange(Vector2 p0, Vector2 p1, float rowTop, float rowBottom, out float xMin, out float xMax)
    {
        var edgeMinY = MathF.Min(p0.Y, p1.Y);
        var edgeMaxY = MathF.Max(p0.Y, p1.Y);

        var clampedTop = MathF.Max(rowTop, edgeMinY);
        var clampedBottom = MathF.Min(rowBottom, edgeMaxY);

        if (MathF.Abs(p1.Y - p0.Y) < 0.0001f)
        {
            xMin = MathF.Min(p0.X, p1.X);
            xMax = MathF.Max(p0.X, p1.X);
            return;
        }

        var tTop = (clampedTop - p0.Y) / (p1.Y - p0.Y);
        var tBottom = (clampedBottom - p0.Y) / (p1.Y - p0.Y);
        var xAtTop = p0.X + tTop * (p1.X - p0.X);
        var xAtBottom = p0.X + tBottom * (p1.X - p0.X);

        xMin = MathF.Min(xAtTop, xAtBottom);
        xMax = MathF.Max(xAtTop, xAtBottom);
    }

    private int GetRasterVerts(
        ref Path path,
        ref Span<Vector2> verts,
        float dpi,
        out float minY,
        out float maxY)
    {
        var vertCount = 0;
        minY = float.MaxValue;
        maxY = float.MinValue;

        for (ushort aIdx = 0; aIdx < path.AnchorCount && vertCount < MaxAnchorsPerPath; aIdx++)
        {
            var anchorIdx = (ushort)(path.AnchorStart + aIdx);
            ref var anchor = ref _anchors[anchorIdx];
            var pixelPos = anchor.Position * dpi;
            verts[vertCount++] = pixelPos;
            minY = MathF.Min(minY, pixelPos.Y);
            maxY = MathF.Max(maxY, pixelPos.Y);

            if (MathF.Abs(anchor.Curve) > 0.0001f)
            {
                var samples = GetSegmentSamples(anchorIdx);
                for (var s = 0; s < MaxSegmentSamples && vertCount < MaxAnchorsPerPath; s++)
                {
                    pixelPos = samples[s] * dpi;
                    verts[vertCount++] = pixelPos;
                    minY = MathF.Min(minY, pixelPos.Y);
                    maxY = MathF.Max(maxY, pixelPos.Y);
                }
            }
        }

        return vertCount;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void AntiAliasPixel(ref Color32 dst, Color32 fillColor, float coverage, bool subtract)
    {
        if (subtract)
        {
            if (dst.A > 0)
            {
                var newAlpha = (byte)(dst.A * (1f - coverage));
                dst = new Color32(dst.R, dst.G, dst.B, newAlpha);
            }
            return;
        }

        var srcAlpha = (byte)(fillColor.A * coverage);
        if (srcAlpha == 0) return;

        if (dst.A == 0)
        {
            dst = new Color32(fillColor.R, fillColor.G, fillColor.B, srcAlpha);
            return;
        }

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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float DistanceToAlpha(float signedDistancePixels)
    {
        if (signedDistancePixels <= AntiAliasEdgeInner)
            return 1f;
        if (signedDistancePixels >= AntiAliasEdgeOuter)
            return 0f;

        var t = (signedDistancePixels - AntiAliasEdgeInner) / (AntiAliasEdgeOuter - AntiAliasEdgeInner);
        return 1f - MathEx.SmoothStep(t);
    }

    private static float GetAntiAliasedAlpha(Vector2 point, ReadOnlySpan<Vector2> verts)
    {
        var minDistSqr = float.MaxValue;
        for (var i = 0; i < verts.Length; i++)
        {
            var distSqr = PointToSegmentDistSqrFast(point, verts[i], verts[(i + 1) % verts.Length]);
            if (distSqr < minDistSqr)
                minDistSqr = distSqr;
        }

        var inside = IsPointInPolygonFast(point, verts);
        return DistanceToAlpha(inside ? -MathF.Sqrt(minDistSqr) : MathF.Sqrt(minDistSqr));
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
    private static bool IsPointInPolygonFast(Vector2 point, ReadOnlySpan<Vector2> verts)
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

    private static void Fill(in PixelData<Color32> target, int x, int y, int width, Color32 color)
    {
        if (color.A == 255)
        {
            for (int i = 0; i < width; i++)
                target[x + i, y] = color;

            return;
        }

        for (int i = 0; i < width; i++)
        {
            ref var dst = ref target[x + i, y];
            if (dst.A == 0)
            {
                dst = color;
                continue;
            }
            target[x + i, y] = Color32.Blend(dst, color);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Subtract(in PixelData<Color32> target, int x, int y, int width)
    {
        for (int i = 0; i < width; i++)
            target[x + i, y] = Color32.Transparent;
    }
}
