//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ;

public static partial class UI
{
    private static Vector2 ResolveSize(ref readonly Element e, ref readonly Element p, in Size2 size)
    {
        var hasFit = false;
        var fit = Vector2.Zero;
        var result = Vector2.Zero;

        for (int axis = 0; axis < 2; axis++)
        {
            Size resolveSize = size[axis];
            if (resolveSize.Mode == SizeMode.Default)
            {
                if (p.Type == ElementType.Column && axis == 1)
                    resolveSize = Size.Fit;
                else if (p.Type == ElementType.Row && axis == 0)
                    resolveSize = Size.Fit;
                else
                    resolveSize = Size.Percent();
            }

            if (resolveSize.Mode == SizeMode.Fit)
            {
                if (!hasFit)
                {
                    fit = FitElement(in e, in p);
                    hasFit = true;  
                }

                resolveSize = fit[axis];
            }

            result[axis] = resolveSize.Mode switch
            {
                SizeMode.Fixed => resolveSize.Value,
                SizeMode.Percent => (p.ContentRect.GetSize(axis)) * resolveSize.Value,
                _ => 0.0f,
            };
        }

        return result;
    }

    private static Vector2 MeasureContainer(ref readonly Element e, ref readonly Element p)
    {
        var size = ResolveSize(in e, in p, e.Data.Container.Size);

        // Only subtract margin when size resolves to Percent (filling parent space)
        // Default resolves to Fit (not Percent) for width in Row, height in Column
        var widthResolvesToPercent = e.Data.Container.Size.Width.Mode == SizeMode.Percent ||
            (e.Data.Container.Size.Width.Mode == SizeMode.Default && p.Type != ElementType.Row);
        var heightResolvesToPercent = e.Data.Container.Size.Height.Mode == SizeMode.Percent ||
            (e.Data.Container.Size.Height.Mode == SizeMode.Default && p.Type != ElementType.Column);

        if (widthResolvesToPercent)
            size.X -= e.Data.Container.Margin.Horizontal;
        if (heightResolvesToPercent)
            size.Y -= e.Data.Container.Margin.Vertical;

        if (e.Data.Container.MinWidth > 0) size.X = Math.Max(size.X, e.Data.Container.MinWidth);
        if (e.Data.Container.MinHeight > 0) size.Y = Math.Max(size.Y, e.Data.Container.MinHeight);
        if (e.Data.Container.MaxWidth > 0) size.X = Math.Min(size.X, e.Data.Container.MaxWidth);
        if (e.Data.Container.MaxHeight > 0) size.Y = Math.Min(size.Y, e.Data.Container.MaxHeight);

        return size;
    }        
    private static Vector2 MeasureRowFlex(ref readonly Element e, ref readonly Element p)
    {
        Vector2 size = ResolveSize(in e, in p, Size2.Default);
        size.X = 0;
        return size;
    }

    private static Vector2 MeasureColumnFlex(ref readonly Element e, ref readonly Element p)
    {
        Vector2 size = ResolveSize(in e, in p, Size2.Default);
        size.Y = 0;
        return size;
    }

    private static Vector2 MeasureElement(ref readonly Element e, ref readonly Element p) => e.Type switch
    {
        ElementType.Container => MeasureContainer(in e, in p),
        ElementType.Column => MeasureContainer(in e, in p),
        ElementType.Row => MeasureContainer(in e, in p),
        ElementType.Popup => MeasurePopup(in e, in p),
        ElementType.Flex when p.Type is ElementType.Row => MeasureRowFlex(in e, in p),
        ElementType.Flex when p.Type is ElementType.Column => MeasureColumnFlex(in e, in p),
        _ => ResolveSize(in e, in p, Size2.Default)
    };

    private static Vector2 MeasurePopup(ref readonly Element e, ref readonly Element p)
    {
        // Popup fits to its content
        return FitPopup(in e, in p);
    }

    private static Vector2 FitPopup(ref readonly Element e, ref readonly Element p)
    {
        var fit = Vector2.Zero;
        var elementIndex = e.Index + 1;
        for (var childIndex = 0; childIndex < e.ChildCount; childIndex++)
        {
            ref readonly var child = ref GetElement(elementIndex);
            var childFit = FitElement(in child, in e);
            fit.X = Math.Max(fit.X, childFit.X);
            fit.Y = Math.Max(fit.Y, childFit.Y);
            elementIndex = child.NextSiblingIndex;
        }
        return fit;
    }

    private static Vector2 FitLabel(ref readonly Element e, ref readonly Element p)
    {
        return TextRender.Measure(
            e.Data.Label.Text.AsReadOnlySpan(),
            e.Font!,
            e.Data.Label.FontSize);
    }

    private static Vector2 GetOuterSize(ref readonly Element e, in Vector2 intrinsicSize)
    {
        if (!e.IsContainer)
            return intrinsicSize;
        return new Vector2(
            intrinsicSize.X + e.Data.Container.Margin.Horizontal,
            intrinsicSize.Y + e.Data.Container.Margin.Vertical);
    }

    private static Vector2 FitContainer(ref readonly Element e, ref readonly Element p)
    {
        if (e.Data.Container.Size.Width.IsFixed && e.Data.Container.Size.Height.IsFixed)
            return new Vector2(e.Data.Container.Size.Width.Value, e.Data.Container.Size.Height.Value);

        var fit = Vector2.Zero;
        var elementIndex = e.Index + 1;

        if (e.Type == ElementType.Container)
        {
            for (var childIndex = 0; childIndex < e.ChildCount; childIndex++)
            {
                ref readonly var child = ref GetElement(elementIndex);
                var childOuter = GetOuterSize(in child, FitElement(in child, in e));
                fit.X = Math.Max(fit.X, childOuter.X);
                fit.Y = Math.Max(fit.Y, childOuter.Y);
                elementIndex = child.NextSiblingIndex;
            }
        }
        else if (e.Type == ElementType.Column)
        {
            var prevWasNonFlex = false;
            for (var childIndex = 0; childIndex < e.ChildCount; childIndex++)
            {
                ref readonly var child = ref GetElement(elementIndex);
                if (child.Type == ElementType.Flex)
                {
                    prevWasNonFlex = false;
                    elementIndex = child.NextSiblingIndex;
                    continue;
                }

                if (prevWasNonFlex)
                    fit.Y += e.Data.Container.Spacing;

                var childOuter = GetOuterSize(in child, FitElement(in child, in e));
                fit.X = Math.Max(fit.X, childOuter.X);
                fit.Y += childOuter.Y;
                prevWasNonFlex = true;
                elementIndex = child.NextSiblingIndex;
            }
        }
        else if (e.Type == ElementType.Row)
        {
            var prevWasNonFlex = false;
            for (var childIndex = 0; childIndex < e.ChildCount; childIndex++)
            {
                ref readonly var child = ref GetElement(elementIndex);
                if (child.Type == ElementType.Flex)
                {
                    prevWasNonFlex = false;
                    elementIndex = child.NextSiblingIndex;
                    continue;
                }

                if (prevWasNonFlex)
                    fit.X += e.Data.Container.Spacing;

                var childOuter = GetOuterSize(in child, FitElement(in child, in e));
                fit.X += childOuter.X;
                fit.Y = Math.Max(fit.Y, childOuter.Y);
                prevWasNonFlex = true;
                elementIndex = child.NextSiblingIndex;
            }
        }

        if (e.Data.Container.Size.Width.IsFixed)
            fit.X = e.Data.Container.Size.Width.Value;
        else
            fit.X += e.Data.Container.Padding.Horizontal;

        if (e.Data.Container.Size.Height.IsFixed)
            fit.Y = e.Data.Container.Size.Height.Value;
        else
            fit.Y += e.Data.Container.Padding.Vertical;

        if (e.Data.Container.MinWidth > 0) fit.X = Math.Max(fit.X, e.Data.Container.MinWidth);
        if (e.Data.Container.MinHeight > 0) fit.Y = Math.Max(fit.Y, e.Data.Container.MinHeight);
        if (e.Data.Container.MaxWidth > 0) fit.X = Math.Min(fit.X, e.Data.Container.MaxWidth);
        if (e.Data.Container.MaxHeight > 0) fit.Y = Math.Min(fit.Y, e.Data.Container.MaxHeight);

        return fit;
    }

    private static Vector2 FitElement(ref readonly Element e, ref readonly Element p) => e.Type switch
    {
        ElementType.Container => FitContainer(in e, in p),
        ElementType.Column => FitContainer(in e, in p),
        ElementType.Row => FitContainer(in e, in p),
        ElementType.Popup => FitPopup(in e, in p),
        ElementType.Label => FitLabel(in e, in p),
        _ => Vector2.Zero
    };
}
