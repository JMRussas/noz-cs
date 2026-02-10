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

    private static Vector2 MeasureImage(ref readonly Element e, ref readonly Element p) =>
        ResolveSize(in e, in p, e.Data.Image.Size);

    private static Vector2 MeasureElement(ref readonly Element e, ref readonly Element p) => e.Type switch
    {
        ElementType.Container => MeasureContainer(in e, in p),
        ElementType.Column => MeasureContainer(in e, in p),
        ElementType.Row => MeasureContainer(in e, in p),
        ElementType.Grid => MeasureGrid(in e, in p),
        ElementType.Popup => MeasurePopup(in e, in p),
        ElementType.Scene => MeasureScene(in e, in p),
        ElementType.TextBox => MeasureTextBox(in e, in p),
        ElementType.Image => MeasureImage(in e, in p),
        ElementType.Flex when p.Type is ElementType.Row => MeasureRowFlex(in e, in p),
        ElementType.Flex when p.Type is ElementType.Column => MeasureColumnFlex(in e, in p),
        _ => ResolveSize(in e, in p, Size2.Default)
    };

    private static Vector2 MeasureGrid(ref readonly Element e, ref readonly Element p)
    {
        ref readonly var grid = ref e.Data.Grid;
        // Use VirtualCount for total items if set, otherwise use actual child count
        var totalItems = grid.VirtualCount > 0 ? grid.VirtualCount : e.ChildCount;
        var rowCount = (totalItems + grid.Columns - 1) / grid.Columns;
        var width = grid.Columns * grid.CellWidth + (grid.Columns - 1) * grid.Spacing;
        var height = rowCount * grid.CellHeight + Math.Max(0, rowCount - 1) * grid.Spacing;
        return new Vector2(width, height);
    }

    private static Vector2 MeasureTextBox(ref readonly Element e, ref readonly Element p)
    {
        var widthMode = p.Type == ElementType.Row ? Size.Fit : Size.Percent();
        return ResolveSize(in e, in p, new Size2(widthMode, e.Data.TextBox.Height));
    }

    private static Vector2 MeasureScene(ref readonly Element e, ref readonly Element p)
    {
        return ResolveSize(in e, in p, e.Data.Scene.Size);
    }

    private static Vector2 MeasurePopup(ref readonly Element e, ref readonly Element p)
    {
        if (e.Data.Popup.AnchorRect == Rect.Zero)
            return ScreenSize;
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

    private static Vector2 FitTransform(ref readonly Element e, ref readonly Element p)
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
        var text = e.Data.Label.Text.AsReadOnlySpan();
        var font = (Font)e.Asset!;
        var fontSize = e.Data.Label.FontSize;

        if (e.Data.Label.Wrap)
        {
            var availableWidth = p.ContentRect.Width;
            if (availableWidth <= 0 && p.IsContainer && p.Data.Container.Size.Width.IsFixed)
                availableWidth = p.Data.Container.Size.Width.Value - p.Data.Container.Padding.Horizontal;
            if (availableWidth > 0)
                return TextRender.MeasureWrapped(text, font, fontSize, availableWidth);
        }

        return TextRender.Measure(text, font, fontSize);
    }

    private static Vector2 FitTextBox(ref readonly Element e, ref readonly Element p)
    {
        // Use the style's height (same as MeasureTextBox)
        var height = e.Data.TextBox.Height.Mode == SizeMode.Fixed
            ? e.Data.TextBox.Height.Value
            : 0;

        var font = (e.Asset as Font) ?? DefaultFont;
        var width = 0f;

        if (font != null)
        {
            var fontSize = e.Data.TextBox.FontSize;

            // Use actual text if available, otherwise placeholder
            var text = e.Id != 0
                ? GetElementState(e.Id).Data.TextBox.Text.AsReadOnlySpan()
                : ReadOnlySpan<char>.Empty;

            if (text.Length == 0)
                text = e.Data.TextBox.Placeholder.AsReadOnlySpan();

            width = text.Length > 0
                ? TextRender.Measure(text, font, fontSize).X
                : 0;
        }

        var padding = e.Data.TextBox.Padding;
        return new Vector2(width + padding.Horizontal, height);
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
                if (child.Type == ElementType.Popup)
                {
                    elementIndex = child.NextSiblingIndex;
                    continue;
                }

                var childOuter = GetOuterSize(in child, FitElement(in child, in e));
                fit.X = Math.Max(fit.X, childOuter.X);
                fit.Y = Math.Max(fit.Y, childOuter.Y);
                elementIndex = child.NextSiblingIndex;
            }
        }
        else if (e.Type == ElementType.Column)
        {
            var prevWasChild = false;
            for (var childIndex = 0; childIndex < e.ChildCount; childIndex++)
            {
                ref readonly var child = ref GetElement(elementIndex);
                if (child.Type == ElementType.Popup)
                {
                    elementIndex = child.NextSiblingIndex;
                    continue;
                }

                if (prevWasChild)
                    fit.Y += e.Data.Container.Spacing;

                if (child.Type != ElementType.Flex)
                {
                    var childOuter = GetOuterSize(in child, FitElement(in child, in e));
                    fit.X = Math.Max(fit.X, childOuter.X);
                    fit.Y += childOuter.Y;
                }
                prevWasChild = true;
                elementIndex = child.NextSiblingIndex;
            }
        }
        else if (e.Type == ElementType.Row)
        {
            var prevWasChild = false;
            for (var childIndex = 0; childIndex < e.ChildCount; childIndex++)
            {
                ref readonly var child = ref GetElement(elementIndex);
                if (child.Type == ElementType.Popup)
                {
                    elementIndex = child.NextSiblingIndex;
                    continue;
                }

                if (prevWasChild)
                    fit.X += e.Data.Container.Spacing;

                if (child.Type != ElementType.Flex)
                {
                    var childOuter = GetOuterSize(in child, FitElement(in child, in e));
                    fit.X += childOuter.X;
                    fit.Y = Math.Max(fit.Y, childOuter.Y);
                }
                prevWasChild = true;
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

    private static Vector2 FitImage(ref readonly Element e)
    {
        var fit = new Vector2(e.Data.Image.Width * e.Data.Image.Scale, e.Data.Image.Height * e.Data.Image.Scale);
        if (e.Data.Image.Size.Width.IsFixed)
            fit.X = e.Data.Image.Size.Width.Value;
        if (e.Data.Image.Size.Height.IsFixed)
            fit.Y = e.Data.Image.Size.Height.Value;
        return fit;
    }

    private static Vector2 FitElement(ref readonly Element e, ref readonly Element p) => e.Type switch
    {
        ElementType.Container => FitContainer(in e, in p),
        ElementType.Column => FitContainer(in e, in p),
        ElementType.Row => FitContainer(in e, in p),
        ElementType.Grid => MeasureGrid(in e, in p),
        ElementType.Popup => FitPopup(in e, in p),
        ElementType.Transform => FitTransform(in e, in p),
        ElementType.Image => FitImage(in e),
        ElementType.Label => FitLabel(in e, in p),
        ElementType.TextBox => FitTextBox(in e, in p),
        ElementType.Spacer => e.Data.Spacer.Size,
        _ => Vector2.Zero
    };
}
