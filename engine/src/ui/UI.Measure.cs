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
        Vector2 size = ResolveSize(in e, in p, e.Data.Container.Size);
        if (e.Data.Container.Size.Width.Mode is SizeMode.Percent or SizeMode.Default)
            size.X -= (e.Data.Container.Margin.L + e.Data.Container.Margin.R);

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
        ElementType.Flex when p.Type is ElementType.Row => MeasureRowFlex(in e, in p),
        ElementType.Flex when p.Type is ElementType.Column => MeasureColumnFlex(in e, in p),
        _ => ResolveSize(in e, in p, Size2.Default)
    };

    private static Vector2 FitLabel(ref readonly Element e, ref readonly Element p)
    {
        return TextRender.Measure(
            new ReadOnlySpan<char>(_textBuffer, e.Data.Label.TextStart, e.Data.Label.TextLength),
            e.Font!,
            e.Data.Label.FontSize);
    }

    private static Vector2 FitContainer(ref readonly Element e, ref readonly Element p, bool margins=false)
    {
        Vector2 fit = Vector2.Zero;

        if (e.Data.Container.Size.Width.IsFixed && e.Data.Container.Size.Height.IsFixed)
            return new Vector2(e.Data.Container.Size.Width.Value, e.Data.Container.Size.Height.Value);

        var elementIndex = e.Index + 1;
        if (e.Type == ElementType.Container)
        {
            for (var childIndex = 0; childIndex < e.ChildCount; childIndex++)
            {
                ref readonly var child = ref GetElement(elementIndex);
                var childSize = FitElement(in child, in e, margins :true);
                fit.X = Math.Max(fit.X, childSize.X);
                fit.Y = Math.Max(fit.Y, childSize.Y);
                elementIndex = child.NextSiblingIndex;
            }
        } 
        else if (e.Type == ElementType.Column)
        {
            float spacing = 0;
            for (var childIndex = 0; childIndex < e.ChildCount; childIndex++)
            {
                ref readonly var child = ref GetElement(elementIndex);
                var childSize = FitElement(in child, in e, margins: true);
                fit.X = Math.Max(fit.X, childSize.X);
                fit.Y += childSize.Y + spacing;
                elementIndex = child.NextSiblingIndex;
                spacing = e.Data.Container.Spacing;
            }
        }
        else if (e.Type == ElementType.Row)
        {
            float spacing = 0;
            for (var childIndex = 0; childIndex < e.ChildCount; childIndex++)
            {
                ref readonly var child = ref GetElement(elementIndex);
                var childSize = FitElement(in child, in e, margins: true);
                fit.X += childSize.X + spacing;
                fit.Y = Math.Max(fit.Y, childSize.Y);
                elementIndex = child.NextSiblingIndex;
                spacing = e.Data.Container.Spacing;
            }
        }

        if (e.Data.Container.Size.Width.IsFixed)
            fit.X = e.Data.Container.Size.Width.Value;

        if (e.Data.Container.Size.Height.IsFixed)
            fit.Y = e.Data.Container.Size.Height.Value;

        if (e.Data.Container.IsAutoWidth)
            fit.X = p.ContentRect.Width;

        if (e.Data.Container.IsAutoHeight)
            fit.Y += e.Data.Container.Padding.Vertical;

        if (margins)
        {
            fit.X += e.Data.Container.Margin.Horizontal;
            fit.Y += e.Data.Container.Margin.Vertical;
        }

        return fit;
    }

    private static Vector2 FitElement(ref readonly Element e, ref readonly Element p, bool margins = false) => e.Type switch
    {
        ElementType.Container => FitContainer(in e, in p, margins),
        ElementType.Column => FitContainer(in e, in p, margins),
        ElementType.Row => FitContainer(in e, in p, margins),
        ElementType.Label => FitLabel(in e, in p),
        _ => Vector2.Zero
    };
}
