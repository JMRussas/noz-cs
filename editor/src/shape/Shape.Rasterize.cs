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

            var isHole = (path.Flags & PathFlags.Hole) != 0;
            var fillColor = isHole
                ? Color32.Transparent
                : palette[path.FillColor % palette.Length].ToColor32();
            var rb = RasterBounds;

            RasterizePath(pixels, polyVerts[..vertexCount], fillColor, offset, rb, isHole);
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
        var dpi = EditorApplication.Config.PixelsPerUnit;
        var rb = RasterBounds;

        for (var y = 0; y < rb.Height; y++)
        {
            var py = offset.Y + rb.Y + y;
            if (py < 0 || py >= pixels.Height) continue;

            for (var x = 0; x < rb.Width; x++)
            {
                var px = offset.X + rb.X + x;
                if (px < 0 || px >= pixels.Width) continue;

                var pixelX = rb.X + x + 0.5f;
                var pixelY = rb.Y + y + 0.5f;
                var worldPoint = new Vector2(pixelX / dpi, pixelY / dpi);

                var (fillColor, alpha) = ComputePixelCoverage(worldPoint, palette, dpi);

                if (alpha <= 0f) continue;

                ref var dst = ref pixels[px, py];
                var srcAlpha = (byte)(fillColor.A * alpha);
                var srcColor = new Color32(fillColor.R, fillColor.G, fillColor.B, srcAlpha);

                if (srcAlpha == 255 || dst.A == 0)
                    dst = srcColor;
                else if (srcAlpha > 0)
                    dst = Color32.Blend(dst, srcColor);
            }
        }
    }

    private (Color32 fillColor, float alpha) ComputePixelCoverage(Vector2 worldPoint, Color[] palette, float dpi)
    {
        var resultAlpha = 0f;
        var resultColor = Color32.Transparent;

        for (ushort pIdx = 0; pIdx < PathCount; pIdx++)
        {
            ref var path = ref _paths[pIdx];
            if (path.AnchorCount < 3) continue;

            var signedDist = GetPathSignedDistance(worldPoint, pIdx);
            var pixelDist = signedDist * dpi;
            var pathAlpha = DistanceToAlpha(pixelDist);

            if (pathAlpha <= 0f) continue;

            var isHole = (path.Flags & PathFlags.Hole) != 0;

            if (isHole)
            {
                resultAlpha *= (1f - pathAlpha);
            }
            else
            {
                var pathColor = palette[path.FillColor % palette.Length].ToColor32();

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
