//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ.Editor;

public sealed partial class Shape
{
    public struct RasterizeOptions
    {
        public bool AntiAlias;

        public static readonly RasterizeOptions Default = new() { AntiAlias = false };
    }

    private static bool LineIntersectsRect(
        in Vector2 p0,
        in Vector2 p1,
        float xMin,
        float yMin,
        float xMax,
        float yMax)
    {
        var dx = p1.X - p0.X;
        var dy = p1.Y - p0.Y;

        var tMin = 0f;
        var tMax = 1f;

        Span<float> p = [-dx, dx, -dy, dy];
        Span<float> q = [p0.X - xMin, xMax - p0.X, p0.Y - yMin, yMax - p0.Y];

        for (var i = 0; i < 4; i++)
        {
            if (MathF.Abs(p[i]) < 0.0001f)
            {
                if (q[i] < 0)
                    return false;
            }
            else
            {
                var t = q[i] / p[i];
                if (p[i] < 0)
                    tMin = MathF.Max(tMin, t);
                else
                    tMax = MathF.Min(tMax, t);
            }
        }

        return tMin <= tMax;
    }


    public void Rasterize(PixelData<Color32> pixels, Color[] palette, Vector2Int offset)
        => Rasterize(pixels, palette, offset, RasterizeOptions.Default);

    public void Rasterize(PixelData<Color32> pixels, Color[] palette, Vector2Int offset, RasterizeOptions options)
    {
        if (PathCount == 0) return;

        const int maxPolyVerts = 256;
        Span<Vector2> polyVerts = stackalloc Vector2[maxPolyVerts];
        var dpi = Graphics.PixelsPerUnit;

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

            var fillColor = palette[path.FillColor % palette.Length].ToColor32();
            var rb = RasterBounds;

            if (options.AntiAlias)
                RasterizePathAA(pixels, polyVerts[..vertexCount], fillColor, offset, rb);
            else
                RasterizePathSimple(pixels, polyVerts[..vertexCount], fillColor, offset, rb);
        }
    }

    private static void RasterizePathSimple
        (PixelData<Color32> pixels,
        Span<Vector2> polyVerts,
        Color32 fillColor,
        Vector2Int offset,
        RectInt rb)
    {
        for (var y = 0; y < rb.Height; y++)
        {
            var py = offset.Y + rb.Y + y;
            if (py < 0 || py >= pixels.Height) continue;

            var sampleY = rb.Y + y + 0.5f;

            for (var x = 0; x < rb.Width; x++)
            {
                var px = offset.X + rb.X + x;
                if (px < 0 || px >= pixels.Width) continue;

                var sampleX = rb.X + x + 0.5f;
                if (IsPointInPolygon(new Vector2(sampleX, sampleY), polyVerts))
                {
                    ref var dst = ref pixels[px, py];
                    if (fillColor.A == 255 || dst.A == 0)
                    {
                        dst = fillColor;
                    }
                    else if (fillColor.A > 0)
                    {
                        dst = Color32.Blend(dst, fillColor);
                    }
                }
            }
        }
    }

    private static void RasterizePathAA(
        PixelData<Color32> pixels,
        Span<Vector2> polyVerts,
        Color32 fillColor,
        Vector2Int offset,
        RectInt rb)
    {
        const float edgeMarker = -1f;
        var bufferSize = rb.Width * rb.Height;

        Span<float> coverage = bufferSize <= 4096
            ? stackalloc float[bufferSize]
            : new float[bufferSize];
        coverage.Clear();

        var vertexCount = polyVerts.Length;

        for (var y = 0; y < rb.Height; y++)
        {
            var sampleY = rb.Y + y + 0.5f;
            for (var x = 0; x < rb.Width; x++)
            {
                var sampleX = rb.X + x + 0.5f;
                if (IsPointInPolygon(new Vector2(sampleX, sampleY), polyVerts))
                    coverage[y * rb.Width + x] = 1f;
            }
        }

        for (var i = 0; i < vertexCount; i++)
        {
            var p0 = polyVerts[i];
            var p1 = polyVerts[(i + 1) % vertexCount];

            var minX = (int)MathF.Floor(MathF.Min(p0.X, p1.X));
            var maxX = (int)MathF.Floor(MathF.Max(p0.X, p1.X));
            var minY = (int)MathF.Floor(MathF.Min(p0.Y, p1.Y));
            var maxY = (int)MathF.Floor(MathF.Max(p0.Y, p1.Y));

            for (var py = minY; py <= maxY; py++)
            {
                var localY = py - rb.Y;
                if (localY < 0 || localY >= rb.Height) continue;

                for (var px = minX; px <= maxX; px++)
                {
                    var localX = px - rb.X;
                    if (localX < 0 || localX >= rb.Width) continue;

                    if (LineIntersectsRect(p0, p1, px, py, px + 1, py + 1))
                        coverage[localY * rb.Width + localX] = edgeMarker;
                }
            }
        }

        const int aaSamples = 4;
        Span<float> sampleOffsets = [0.125f, 0.375f, 0.625f, 0.875f];

        for (var idx = 0; idx < bufferSize; idx++)
        {
            if (coverage[idx] != edgeMarker) continue;

            var localX = idx % rb.Width;
            var localY = idx / rb.Width;
            float px = rb.X + localX;
            float py = rb.Y + localY;

            var insideCount = 0;
            for (var sy = 0; sy < aaSamples; sy++)
            {
                for (var sx = 0; sx < aaSamples; sx++)
                {
                    var samplePos = new Vector2(px + sampleOffsets[sx], py + sampleOffsets[sy]);
                    if (IsPointInPolygon(samplePos, polyVerts))
                        insideCount++;
                }
            }

            coverage[idx] = insideCount / (float)(aaSamples * aaSamples);
        }

        for (var y = 0; y < rb.Height; y++)
        {
            var py = offset.Y + rb.Y + y;
            if (py < 0 || py >= pixels.Height) continue;

            for (var x = 0; x < rb.Width; x++)
            {
                var px = offset.X + rb.X + x;
                if (px < 0 || px >= pixels.Width) continue;

                var cov = coverage[y * rb.Width + x];
                if (cov <= 0) continue;

                byte finalAlpha;
                if (cov >= 0.5f)
                    finalAlpha = fillColor.A;
                else
                    finalAlpha = (byte)(cov * fillColor.A);

                if (finalAlpha == 0) continue;

                ref var dst = ref pixels[px, py];
                if (dst.A == 0)
                {
                    dst = new Color32(fillColor.R, fillColor.G, fillColor.B, finalAlpha);
                }
                else if (finalAlpha == 255 && fillColor.A == 255)
                {
                    dst = fillColor;
                }
                else
                {
                    var srcColor = fillColor.WithAlpha(finalAlpha);
                    dst = Color32.Blend(dst, srcColor);
                }
            }
        }
    }
}
