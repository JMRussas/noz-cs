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

    public struct RasterizeOptions
    {
        public bool AntiAlias;

        public static readonly RasterizeOptions Default = new() { AntiAlias = false };
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ScanlineIntersection(float x, int dir)
    {
        public float X = x;
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

            Log.Info($"Path: {pathIndex}");

            RasterizePath(
                target,
                targetRect,
                sourceOffset,
                polyVerts[..vertCount],
                fillColor,
                subtract,
                antiAlias);
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

            if (y == 0)
                for (int i = 0; i < intersectionCount; i++)
                {
                    Log.Info($"  {i}: X={intersections[i].X}, Dir={intersections[i].Direction}");
                }

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
        for (var i = 0; i < intersections.Length; i++)
        {
            var wasInside = winding != 0;
            winding += intersections[i].Direction;
            var isInside = winding != 0;

            if (wasInside == isInside) continue;

            if (isInside)
            {
                sourceMin = (int)MathF.Ceiling(intersections[i].X - 0.5f);
                if (scanlineY == 0)
                {
                    Log.Info($"intersection.X0={intersections[i].X}");
                }
                    continue;
            }

            var sourceMax = (int)MathF.Floor(intersections[i].X + 0.5f);
            var targetMin = int.Max(targetRect.X, sourceOffset.X + sourceMin + targetRect.X);
            var targetMax = int.Min(targetRect.X + targetRect.Width, sourceOffset.X + sourceMax + targetRect.X);
            var targetWidth = targetMax - targetMin;

            if (scanlineY == 0)
            {
                Log.Info($"intersection.X1={intersections[i].X}");
                Log.Info($"sourceMin={sourceMin}");
                Log.Info($"sourceMax={sourceMax}");
                Log.Info($"sourceOffset={sourceOffset}");
                Log.Info($"targetRect={targetRect}");
                Log.Info($"targetMin={targetMin}");
                Log.Info($"targetMax={targetMax}");
                Log.Info($"targetWidth={targetWidth}");
            }


            if (targetWidth <= 0)
                continue;

            if (antiAlias)
            {
                var sourceY = -sourceOffset.Y + scanlineY + 0.5f;
                var sourceX = sourceMin + 0.5f;

                if (scanlineY == 0)
                {
                    Log.Info($"sourceX0={sourceX}");
                    Log.Info($"sourceY0={sourceY}");
                    Log.Info($"alpha0={GetAntiAliasedAlpha(new Vector2(sourceX, sourceY), verts)}");
                }

                AntiAliasPixel(
                     ref target[targetMin, targetRect.Y + scanlineY],
                     color,
                     GetAntiAliasedAlpha(new Vector2(sourceX, sourceY), verts),
                     subtract);

                if (targetWidth == 1) continue;

                sourceX = sourceMax - 1 + 0.5f;
                if (scanlineY == 0)
                {
                    Log.Info($"sourceX1={sourceX}");
                    Log.Info($"sourceY1={sourceY}");
                    Log.Info($"alpha1={GetAntiAliasedAlpha(new Vector2(sourceX, sourceY), verts)}");
                }

                AntiAliasPixel(
                     ref target[targetMax - 1, targetRect.Y + scanlineY],
                     color,
                     GetAntiAliasedAlpha(new Vector2(sourceX, sourceY), verts),
                     subtract);

                if (targetWidth == 2) continue;

                targetMin++;
                targetWidth -= 2;
            }
            
            if (subtract)
                Subtract(target, targetMin, targetRect.Y + scanlineY, targetWidth);
            else
                Fill(target, targetMin, targetRect.Y + scanlineY, targetWidth, color);
        }
    }

    private static int GetScanlineIntersections(
        ReadOnlySpan<Vector2> verts,
        float scanlineY,
        Span<ScanlineIntersection> intersections)
    {
        var intersectionCount = 0;
        for (var i = 0; i < verts.Length; i++)
        {
            ref readonly var p0 = ref verts[i];
            ref readonly var p1 = ref verts[(i + 1) % verts.Length];

            // Edge going upward (p0 below, p1 above)
            if (p0.Y <= scanlineY && p1.Y > scanlineY)
            {
                var t = (scanlineY - p0.Y) / (p1.Y - p0.Y);
                intersections[intersectionCount++] = new ScanlineIntersection(p0.X + t * (p1.X - p0.X), 1);
                continue;
            }

            // Edge going downward (p0 above, p1 below)
            if (p1.Y <= scanlineY && p0.Y > scanlineY)
            {
                var t = (scanlineY - p0.Y) / (p1.Y - p0.Y);
                intersections[intersectionCount++] = new ScanlineIntersection(p0.X + t * (p1.X - p0.X), -1);
                continue;
            }
        }

        if (intersectionCount == 0)
            return 0;

        intersections[..intersectionCount].Sort((a, b) => a.X.CompareTo(b.X));
        return intersectionCount;
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
