//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using Clipper2Lib;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace NoZ.Editor;

public partial class VectorSpriteDocument
{
    private const int RasterizeSize = 1024;

    internal override void RasterizeCore(PixelData<Color32> image, in AtlasSpriteRect rect, int padding)
    {
        var dpi = PixelsPerUnit;
        var padding2 = padding * 2;
        var w = RasterBounds.Size.X;
        var h = RasterBounds.Size.Y;

        var sourceLayer = rect.ExportGroup ?? Root;
        if (rect.ExportGroup == null && IsAnimated)
        {
            var frameIdx = GetFrameAtTimeSlot(rect.FrameIndex);
            if (frameIdx >= 0 && frameIdx < Root.Children.Count &&
                Root.Children[frameIdx] is SpriteGroup frame)
            {
                sourceLayer = frame;
            }
        }

        // Constrained sprites have a fixed rect, so we rasterize into the
        // interior only (overshoot clipped by buffer bounds) and fill the
        // padding ring with wrap or extrude. Unconstrained sprites derive
        // their bounds from content, so we let paths extend into the padding
        // naturally to preserve their AA fringe.
        if (ConstrainedSize.HasValue)
        {
            var rasterRect = new RectInt(
                rect.Rect.Position + new Vector2Int(padding, padding),
                new Vector2Int(w, h));
            var sourceOffset = -RasterBounds.Position;

            float invDpi = 1f / dpi;
            var clipRect = new Rect(
                RasterBounds.X * invDpi,
                RasterBounds.Y * invDpi,
                RasterBounds.Width * invDpi,
                RasterBounds.Height * invDpi);

            RasterizeLayer(
                sourceLayer,
                image,
                rasterRect,
                sourceOffset,
                dpi,
                clipRect,
                this,
                excludeExportGroups: rect.IsBasePart || rect.ExportGroup != null);

            image.BleedColors(rasterRect);

            if (ShowTiling)
            {
                image.WrapEdges(rasterRect, padding);
            }
            else
            {
                for (var p = padding - 1; p >= 0; p--)
                {
                    var padRect = new RectInt(
                        rect.Rect.Position + new Vector2Int(p, p),
                        new Vector2Int(w + padding2, h + padding2) - new Vector2Int(p * 2, p * 2));
                    image.ExtrudeEdges(padRect);
                }
            }
        }
        else
        {
            var targetRect = new RectInt(
                rect.Rect.Position,
                new Vector2Int(w + padding2, h + padding2));
            var sourceOffset = -RasterBounds.Position + new Vector2Int(padding, padding);

            RasterizeLayer(
                sourceLayer,
                image,
                targetRect,
                sourceOffset,
                dpi,
                clipRect: null,
                outlineSource: this,
                excludeExportGroups: rect.IsBasePart || rect.ExportGroup != null);

            image.BleedColors(targetRect);
        }
    }

    internal static void RasterizeLayer(
        SpriteGroup layer,
        PixelData<Color32> image,
        RectInt targetRect,
        Vector2Int sourceOffset,
        int dpi,
        Rect? clipRect = null,
        VectorSpriteDocument? outlineSource = null,
        bool excludeExportGroups = false)
    {
        var results = new List<LayerPathResult>();
        SpriteGroupProcessor.ProcessLayer(layer, results, excludeExportGroups);

        if (outlineSource != null && outlineSource.TryBuildOutlineResult(results, out var outline))
            results.Insert(0, outline);

        PathsD? clipPaths = null;
        if (clipRect.HasValue)
        {
            var cr = clipRect.Value;
            clipPaths = new PathsD { new PathD {
                new PointD(cr.Left, cr.Top),
                new PointD(cr.Right, cr.Top),
                new PointD(cr.Right, cr.Bottom),
                new PointD(cr.Left, cr.Bottom)
            }};
        }

        // 4x MSAA sample accumulator. Paths are blended back-to-front into 4
        // sub-pixel slots per pixel; Resolve() averages and writes the final
        // color into the image. Sized to the target rect, indexed locally.
        using var samples = new PixelData<Sample4>(targetRect.Size);

        // Iterate forward — the results list is already ordered back-to-front
        // (index 0 = bottommost) by SpriteGroupProcessor's reverse child pass.
        // Porter-Duff over per sample composites each path on top of prior ones.
        foreach (var result in results)
        {
            var contours = result.Contours;
            if (clipPaths != null && contours.Count > 0)
            {
                contours = Clipper.BooleanOp(ClipType.Intersection,
                    contours, clipPaths, FillRule.NonZero, precision: 6);
                if (contours.Count == 0) continue;
            }

            Rasterizer.Fill(contours, samples, targetRect, sourceOffset, dpi, result.Color);
        }

        Rasterizer.Resolve(image, samples, targetRect);
    }

    public byte[] RasterizeColorToPng()
    {
        var w = RasterBounds.Width;
        var h = RasterBounds.Height;
        if (w <= 0 || h <= 0) return [];

        if (Root.Children.Count == 0)
            return [];

        var dpi = EditorApplication.Config.PixelsPerUnit;
        var scale = (float)RasterizeSize / MathF.Max(w, h);
        var scaledDpi = (int)MathF.Round(dpi * scale);
        var outW = (int)MathF.Round(w * scale);
        var outH = (int)MathF.Round(h * scale);

        using var pixels = new PixelData<Color32>(outW, outH);

        var targetRect = new RectInt(0, 0, outW, outH);
        var sourceOffset = new Vector2Int(
            (int)MathF.Round(-RasterBounds.X * scale),
            (int)MathF.Round(-RasterBounds.Y * scale));

        Rect? clipRect = null;
        if (ConstrainedSize.HasValue)
        {
            float invDpi = 1f / dpi;
            clipRect = new Rect(
                RasterBounds.X * invDpi,
                RasterBounds.Y * invDpi,
                RasterBounds.Width * invDpi,
                RasterBounds.Height * invDpi);
        }

        RasterizeLayer(Root, pixels, targetRect, sourceOffset, scaledDpi, clipRect, this);

        using var image = SixLabors.ImageSharp.Image.LoadPixelData<Rgba32>(pixels.AsReadonlySpan(), outW, outH);
        using var ms = new MemoryStream();
        image.SaveAsPng(ms);
        return ms.ToArray();
    }
}
