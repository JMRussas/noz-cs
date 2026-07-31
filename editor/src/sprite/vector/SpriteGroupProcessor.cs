//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//
//  Shared layer-scoped boolean operation logic used by both
//  rasterization (SpriteDocument) and tessellation (SpriteEditor.Mesh).
//
//  The MSDF bridge in SpritePathClipper exists because Clipper2 only
//  operates on linear segments, while MSDF's Shape handles quadratic
//  beziers natively — preserving curve fidelity during self-union.
//

using Clipper2Lib;

namespace NoZ.Editor;

internal readonly struct LayerPathResult(
    PathsD Contours, Color32 Color, bool IsStroke)
{
    public readonly PathsD Contours = Contours;
    public readonly Color32 Color = Color;
    public readonly bool IsStroke = IsStroke;
}

internal static class SpriteGroupProcessor
{
    internal const int ClipperPrecision = 6;

    private static bool HasClipPaths(SpriteGroup layer)
    {
        for (int i = 0; i < layer.Children.Count; i++)
            if (layer.Children[i] is SpritePath { IsClip: true })
                return true;
        return false;
    }

    internal static void ProcessLayer(
        SpriteGroup layer,
        List<LayerPathResult> output,
        bool excludeExportGroups = false)
    {
        if (!layer.Visible) return;

        var needsAccumulation = HasClipPaths(layer);
        PathsD? accumulatedPaths = null;
        var results = new List<LayerPathResult>();

        // Single reverse pass: first-in-list renders on top (matches outliner order).
        // Paths and child layers are interleaved so layer ordering is preserved.
        for (int ci = layer.Children.Count - 1; ci >= 0; ci--)
        {
            var child = layer.Children[ci];

            // Child layer: capture output into results so parent subtract/clip can affect it
            if (child is SpriteGroup childLayer)
            {
                if (excludeExportGroups && childLayer.IsSprite)
                    continue;
                ProcessLayer(childLayer, results, excludeExportGroups);
                continue;
            }

            if (child is not SpritePath path) continue;
            if (!path.Visible) continue;
            if (path.TotalAnchorCount < 3) continue;

            // Subtract: retroactively cut from all already-emitted results below
            if (path.IsSubtract)
            {
                var subContours = path.GetClipperPaths();
                if (subContours.Count > 0 && results.Count > 0)
                {
                    for (int i = results.Count - 1; i >= 0; i--)
                    {
                        var ri = results[i];
                        var diff = Clipper.BooleanOp(ClipType.Difference,
                            ri.Contours, subContours, FillRule.NonZero, precision: ClipperPrecision);
                        if (diff.Count > 0)
                            results[i] = new LayerPathResult(diff, ri.Color, ri.IsStroke);
                        else
                            results.RemoveAt(i);
                    }

                    if (needsAccumulation && accumulatedPaths is { Count: > 0 })
                        accumulatedPaths = Clipper.BooleanOp(ClipType.Difference,
                            accumulatedPaths, subContours, FillRule.NonZero, precision: ClipperPrecision);
                }
                continue;
            }

            var contours = path.GetClipperPaths();
            if (contours.Count == 0) continue;

            // Contracted paths for stroked shapes -- computed once, reused for
            // both accumulation (clip ops use fill boundary) and stroke emission.
            PathsD? contracted = null;

            // Clip: intersect with accumulated paths below (already processed due to reverse iteration)
            if (path.IsClip)
            {
                if (accumulatedPaths is not { Count: > 0 }) continue;
                contours = Clipper.BooleanOp(ClipType.Intersection,
                    contours, accumulatedPaths, FillRule.NonZero, precision: ClipperPrecision);
                if (contours.Count == 0) continue;
            }
            else
            {
                if (needsAccumulation)
                {
                    var accContours = contours;
                    if (path.StrokeColor.A > 0 && path.StrokeWidth > 0)
                    {
                        contracted = Clipper.InflatePaths(contours,
                            -(path.StrokeWidth * SpritePath.StrokeScale),
                            ToClipperJoinType(path.StrokeJoin), EndType.Polygon, precision: ClipperPrecision);
                        if (contracted.Count > 0)
                            accContours = contracted;
                    }

                    if (accumulatedPaths == null)
                        accumulatedPaths = new PathsD(accContours);
                    else
                        accumulatedPaths = Clipper.BooleanOp(ClipType.Union,
                            accumulatedPaths, accContours, FillRule.NonZero, precision: ClipperPrecision);
                }
            }

            var hasStroke = path.StrokeColor.A > 0 && path.StrokeWidth > 0;
            var hasFill = path.FillColor.A > 0;

            if (hasStroke)
            {
                contracted ??= Clipper.InflatePaths(contours,
                    -(path.StrokeWidth * SpritePath.StrokeScale),
                    ToClipperJoinType(path.StrokeJoin), EndType.Polygon, precision: ClipperPrecision);

                if (hasFill)
                {
                    if (contracted.Count > 0)
                    {
                        var ring = Clipper.BooleanOp(ClipType.Difference,
                            contours, contracted, FillRule.NonZero, precision: ClipperPrecision);
                        if (ring.Count > 0)
                            results.Add(new LayerPathResult(ring, path.StrokeColor, true));
                        results.Add(new LayerPathResult(contracted, path.FillColor, false));
                    }
                    else
                    {
                        results.Add(new LayerPathResult(contours, path.StrokeColor, true));
                    }
                }
                else
                {
                    if (contracted.Count > 0)
                    {
                        var ring = Clipper.BooleanOp(ClipType.Difference,
                            contours, contracted, FillRule.NonZero, precision: ClipperPrecision);
                        if (ring.Count > 0)
                            results.Add(new LayerPathResult(ring, path.StrokeColor, true));
                    }
                    else
                    {
                        results.Add(new LayerPathResult(contours, path.StrokeColor, true));
                    }
                }
            }
            else if (hasFill)
            {
                results.Add(new LayerPathResult(contours, path.FillColor, false));
            }
        }

        output.AddRange(results);
    }

    internal static JoinType ToClipperJoinType(SpriteStrokeJoin join) => join switch
    {
        SpriteStrokeJoin.Miter => JoinType.Miter,
        SpriteStrokeJoin.Bevel => JoinType.Bevel,
        _ => JoinType.Round,
    };
}
