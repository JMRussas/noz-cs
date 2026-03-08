//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ;

public static unsafe partial class ElementTree
{
    private static int _layoutDepth;
    private static bool _layoutCycleLogged;

    private static float EdgeInset(in EdgeInsets ei, int axis) => axis == 0 ? ei.Horizontal : ei.Vertical;
    private static float EdgeMin(in EdgeInsets ei, int axis) => axis == 0 ? ei.L : ei.T;

    private static float FitAxis(int offset, int axis, int layoutAxis)
    {
        ref var e = ref GetElement(offset);
        switch (e.Type)
        {
            case ElementType.Size:
            {
                var mode = e.Data.Size[axis].Mode;
                if (mode == SizeMode.Default)
                    mode = SizeMode.Fit;
                return mode switch
                {
                    SizeMode.Fixed => e.Data.Size[axis].Value,
                    SizeMode.Fit => e.ChildCount > 0 ? FitAxis(e.FirstChild, axis, layoutAxis) : 0,
                    SizeMode.Percent => 0,
                    _ => 0
                };
            }

            case ElementType.Padding:
            {
                var child = e.ChildCount > 0 ? FitAxis(e.FirstChild, axis, layoutAxis) : 0;
                return child + EdgeInset(e.Data.Padding, axis);
            }

            case ElementType.Margin:
            {
                var child = e.ChildCount > 0 ? FitAxis(e.FirstChild, axis, layoutAxis) : 0;
                return child + EdgeInset(e.Data.Margin, axis);
            }

            case ElementType.Border:
            {
                ref var d = ref e.Data.Border;
                var child = e.ChildCount > 0 ? FitAxis(e.FirstChild, axis, layoutAxis) : 0;
                return child + d.Width * 2;
            }

            case ElementType.Fill:
            case ElementType.Clip:
            case ElementType.Opacity:
            case ElementType.Cursor:
            case ElementType.Transform:
            case ElementType.Scroll:
            case ElementType.Widget:
            case ElementType.Align:
                return e.ChildCount > 0 ? FitAxis(e.FirstChild, axis, layoutAxis) : 0;

            case ElementType.Row:
                return FitRowColumn(ref e, axis, 0, e.Data.Spacing);

            case ElementType.Column:
                return FitRowColumn(ref e, axis, 1, e.Data.Spacing);

            case ElementType.Flex:
                return e.ChildCount > 0 ? FitAxis(e.FirstChild, axis, layoutAxis) : 0;

            case ElementType.Grid:
                return 0;

            case ElementType.Spacer:
                return e.Data.Spacing;

            case ElementType.Text:
            {
                ref var d = ref e.Data.Text;
                var font = (Font)_assets[d.Font]!;
                if (axis == 1 && d.Overflow == TextOverflow.Wrap && e.Rect.Width > 0)
                    return TextRender.MeasureWrapped(d.Text.AsReadOnlySpan(), font, d.FontSize, e.Rect.Width).Y;
                var measure = TextRender.Measure(d.Text.AsReadOnlySpan(), font, d.FontSize);
                return measure[axis];
            }

            case ElementType.Image:
            {
                ref var d = ref e.Data.Image;
                if (d.Size[axis].IsFixed) return d.Size[axis].Value;
                return (axis == 0 ? d.Width : d.Height) * d.Scale;
            }

            case ElementType.Scene:
            {
                ref var d = ref e.Data.Scene;
                return d.Size[axis].IsFixed ? d.Size[axis].Value : 0;
            }

            case ElementType.EditableText:
                return FitEditableTextAxis(ref e, axis);

            case ElementType.Popup:
                return e.ChildCount > 0 ? FitAxis(e.FirstChild, axis, -1) : 0;

            default:
                return 0;
        }
    }

    private static float FitRowColumn(ref Element e, int axis, int containerAxis, float spacing)
    {
        var fit = 0f;
        var childCount = 0;
        var childOffset = (int)e.FirstChild;
        for (int i = 0; i < e.ChildCount; i++)
        {
            ref var child = ref GetElement(childOffset);
            if (child.Type == ElementType.Flex)
            {
                if (axis != containerAxis)
                    fit = Math.Max(fit, FitAxis(childOffset, axis, containerAxis));
            }
            else
            {
                var childFit = FitAxis(childOffset, axis, containerAxis);
                if (axis == containerAxis)
                    fit += childFit;
                else
                    fit = Math.Max(fit, childFit);
            }
            childCount++;
            childOffset = child.NextSibling;
        }
        if (axis == containerAxis && childCount > 1)
            fit += (childCount - 1) * spacing;
        return fit;
    }


    private static void LayoutAxis(int offset, float position, float available, int axis, int layoutAxis)
    {
        if (_layoutDepth > 200)
        {
            if (!_layoutCycleLogged)
            {
                _layoutCycleLogged = true;
                var sb = new System.Text.StringBuilder();
                sb.AppendLine($"ElementTree: LayoutAxis depth > 200 at offset {offset}, axis={axis}, layoutAxis={layoutAxis}");
                sb.AppendLine($"Tree has {CurrentBuffer.Elements.Length} elements. Dump:");
                DebugDumpTree(sb, 0, 0);
                Log.Error(sb.ToString());
            }
            return;
        }
        _layoutDepth++;
        LayoutAxisImpl(offset, position, available, axis, layoutAxis);
        _layoutDepth--;
    }

    private static void DebugDumpTree(System.Text.StringBuilder sb, int offset, int depth)
    {
        if (depth > 100) { sb.AppendLine($"{new string(' ', depth * 2)}... (depth limit)"); return; }
        ref var e = ref GetElement(offset);
        var indent = new string(' ', depth * 2);
        sb.Append($"{indent}[{offset}] {e.Type} parent={e.Parent} first={e.FirstChild} next={e.NextSibling} children={e.ChildCount}");
        if (e.Type == ElementType.Widget)
            sb.Append($" id={e.Data.Widget.Id}");
        sb.AppendLine();
        var childOffset = (int)e.FirstChild;
        for (int i = 0; i < e.ChildCount; i++)
        {
            DebugDumpTree(sb, childOffset, depth + 1);
            ref var child = ref GetElement(childOffset);
            childOffset = child.NextSibling;
        }
    }

    private static void LayoutAxisImpl(int offset, float position, float available, int axis, int layoutAxis)
    {
        ref var e = ref GetElement(offset);
        float size;

        switch (e.Type)
        {
            case ElementType.Size:
            {
                var mode = e.Data.Size[axis].Mode;
                var isDefault = mode == SizeMode.Default;
                if (isDefault)
                    mode = (layoutAxis == axis) ? SizeMode.Fit : SizeMode.Percent;
                size = mode switch
                {
                    SizeMode.Fixed => e.Data.Size[axis].Value,
                    SizeMode.Percent => available * (isDefault ? 1.0f : e.Data.Size[axis].Value),
                    SizeMode.Fit => e.ChildCount > 0 ? FitAxis(e.FirstChild, axis, layoutAxis) : 0,
                    _ => 0
                };
                break;
            }

            case ElementType.Padding:
            {
                var inset = EdgeInset(e.Data.Padding, axis);
                size = layoutAxis != axis
                    ? available
                    : (e.ChildCount > 0 ? FitAxis(e.FirstChild, axis, layoutAxis) : 0) + inset;
                break;
            }

            case ElementType.Margin:
            {
                var inset = EdgeInset(e.Data.Margin, axis);
                size = layoutAxis != axis
                    ? available
                    : (e.ChildCount > 0 ? FitAxis(e.FirstChild, axis, layoutAxis) : 0) + inset;
                break;
            }

            case ElementType.Border:
            {
                ref var d = ref e.Data.Border;
                var inset = d.Width * 2;
                size = layoutAxis != axis
                    ? available
                    : (e.ChildCount > 0 ? FitAxis(e.FirstChild, axis, layoutAxis) : 0) + inset;
                break;
            }

            case ElementType.Fill:
            case ElementType.Clip:
            case ElementType.Opacity:
            case ElementType.Cursor:
            case ElementType.Transform:
            case ElementType.Scroll:
            case ElementType.Widget:
                size = layoutAxis != axis
                    ? available
                    : (e.ChildCount > 0 ? FitAxis(e.FirstChild, axis, layoutAxis) : 0);
                break;

            case ElementType.Align:
                size = available;
                break;

            case ElementType.Row:
                size = layoutAxis != axis
                    ? available
                    : (e.ChildCount > 0 ? FitAxis(offset, axis, 0) : 0);
                break;

            case ElementType.Column:
                size = layoutAxis != axis
                    ? available
                    : (e.ChildCount > 0 ? FitAxis(offset, axis, 1) : 0);
                break;

            case ElementType.Flex:
                size = available;
                break;

            case ElementType.Spacer:
                size = e.Data.Spacing;
                break;

            case ElementType.Text:
            {
                ref var d = ref e.Data.Text;
                var font = (Font)_assets[d.Font]!;
                float contentSize;
                if (axis == 1 && d.Overflow == TextOverflow.Wrap && e.Rect.Width > 0)
                    contentSize = TextRender.MeasureWrapped(d.Text.AsReadOnlySpan(), font, d.FontSize, e.Rect.Width).Y;
                else
                    contentSize = TextRender.Measure(d.Text.AsReadOnlySpan(), font, d.FontSize)[axis];
                size = Math.Max(contentSize, available);
                break;
            }

            case ElementType.Image:
            {
                ref var d = ref e.Data.Image;
                float contentSize;
                if (d.Size[axis].IsFixed)
                    contentSize = d.Size[axis].Value;
                else
                    contentSize = (axis == 0 ? d.Width : d.Height) * d.Scale;
                size = Math.Max(contentSize, available);
                break;
            }

            case ElementType.Scene:
            {
                ref var d = ref e.Data.Scene;
                var mode = d.Size[axis].Mode;
                if (mode == SizeMode.Default) mode = SizeMode.Percent;
                size = mode switch
                {
                    SizeMode.Fixed => d.Size[axis].Value,
                    SizeMode.Percent => available * (d.Size[axis].Mode == SizeMode.Default ? 1.0f : d.Size[axis].Value),
                    _ => 0
                };
                break;
            }

            case ElementType.EditableText:
                size = LayoutEditableTextAxis(ref e, axis, available);
                break;

            case ElementType.Popup:
                size = e.ChildCount > 0 ? FitAxis(e.FirstChild, axis, -1) : 0;
                break;

            case ElementType.Grid:
            {
                ref var d = ref e.Data.Grid;
                if (axis == 0)
                {
                    size = available;
                }
                else
                {
                    var (cols, cw, ch) = UI.ResolveGridCellSize(
                        d.Columns, d.CellWidth, d.CellHeight,
                        d.CellMinWidth, d.CellHeightOffset,
                        d.Spacing, e.Rect.Width);
                    var totalItems = d.VirtualCount > 0 ? d.VirtualCount : e.ChildCount;
                    var rowCount = (totalItems + cols - 1) / cols;
                    size = rowCount * ch + Math.Max(0, rowCount - 1) * d.Spacing;
                }
                break;
            }

            default:
                size = 0;
                break;
        }

        e.Rect[axis] = position;
        e.Rect[axis + 2] = size;

        // Popup: override position to anchor rect after size is known
        if (e.Type == ElementType.Popup)
        {
            ref var pd = ref e.Data.Popup;
            var anchorPos = pd.AnchorRect[axis] + pd.AnchorRect[axis + 2] * (axis == 0 ? pd.AnchorFactorX : pd.AnchorFactorY);
            var popupAlignFactor = axis == 0 ? pd.PopupAlignFactorX : pd.PopupAlignFactorY;
            var anchorFactor = axis == 0 ? pd.AnchorFactorX : pd.AnchorFactorY;
            e.Rect[axis] = anchorPos - size * popupAlignFactor;
            if (anchorFactor != popupAlignFactor)
                e.Rect[axis] += pd.Spacing * (1f - 2f * popupAlignFactor);
            if (pd.ClampToScreen)
                e.Rect[axis] = Math.Clamp(e.Rect[axis], 0, ScreenSize[axis] - size);
        }

        // Recurse children
        switch (e.Type)
        {
            case ElementType.Row when axis == 0:
                LayoutRowColumnAxis(ref e, axis, 0);
                break;
            case ElementType.Row when axis == 1:
                LayoutCrossAxis(ref e, axis);
                break;
            case ElementType.Column when axis == 1:
                LayoutRowColumnAxis(ref e, axis, 1);
                break;
            case ElementType.Column when axis == 0:
                LayoutCrossAxis(ref e, axis);
                break;
            case ElementType.Align:
                LayoutAlignAxis(ref e, axis);
                break;
            case ElementType.Padding:
            {
                ref var d = ref e.Data.Padding;
                var inset = EdgeInset(d, axis);
                var childPos = e.Rect[axis] + EdgeMin(d, axis);
                var childAvail = Math.Max(0, size - inset);
                LayoutChildrenAxis(ref e, childPos, childAvail, axis, layoutAxis);
                break;
            }
            case ElementType.Margin:
            {
                ref var d = ref e.Data.Margin;
                var inset = EdgeInset(d, axis);
                var childPos = e.Rect[axis] + EdgeMin(d, axis);
                var childAvail = Math.Max(0, size - inset);
                LayoutChildrenAxis(ref e, childPos, childAvail, axis, layoutAxis);
                break;
            }
            case ElementType.Border:
            {
                ref var d = ref e.Data.Border;
                var childPos = e.Rect[axis] + d.Width;
                var childAvail = Math.Max(0, size - d.Width * 2);
                LayoutChildrenAxis(ref e, childPos, childAvail, axis, layoutAxis);
                break;
            }
            case ElementType.Size:
            {
                var mode = e.Data.Size[axis].Mode;
                var isFit = mode == SizeMode.Fit || (mode == SizeMode.Default && layoutAxis == axis);
                LayoutChildrenAxis(ref e, e.Rect[axis], size, axis, isFit ? layoutAxis : -1);
                break;
            }
            case ElementType.Flex:
                LayoutChildrenAxis(ref e, e.Rect[axis], size, axis, -1);
                break;
            case ElementType.Popup:
                LayoutChildrenAxis(ref e, e.Rect[axis], size, axis, -1);
                break;
            case ElementType.Grid:
                LayoutGridAxis(ref e, axis);
                break;
            default:
                LayoutChildrenAxis(ref e, e.Rect[axis], size, axis, layoutAxis);
                break;
        }

        // Scrollable: calculate content height and clamp offset after Y layout
        if (e.Type == ElementType.Scroll && axis == 1)
        {
#if false
            ref var sd = ref GetElementData<ScrollElement>(ref e);
            if (sd.WidgetId > 0)
            {
                var contentHeight = 0f;
                var childOffset = (int)e.FirstChild;
                for (int i = 0; i < e.ChildCount; i++)
                {
                    ref var child = ref GetElement(childOffset);
                    var childBottom = child.Rect[axis] + child.Rect[axis + 2] - e.Rect[axis];
                    contentHeight = Math.Max(contentHeight, childBottom);
                    childOffset = child.NextSibling;
                }

                ref var state = ref GetWidgetData<ScrollableState>(sd.WidgetId);
                state.ContentHeight = contentHeight;

                var maxScroll = Math.Max(0, contentHeight - size);
                if (state.Offset > maxScroll)
                    state.Offset = maxScroll;
            }
#endif
        }
    }

    private static void LayoutGridAxis(ref Element e, int axis)
    {
        ref var d = ref e.Data.Grid;
        var (columns, cellWidth, cellHeight) = UI.ResolveGridCellSize(
            d.Columns, d.CellWidth, d.CellHeight,
            d.CellMinWidth, d.CellHeightOffset,
            d.Spacing, e.Rect.Width);

        var childOffset = (int)e.FirstChild;
        for (int i = 0; i < e.ChildCount; i++)
        {
            var virtualIndex = d.StartIndex + i;
            var col = virtualIndex % columns;
            var row = virtualIndex / columns;
            var childPos = axis == 0
                ? e.Rect.X + col * (cellWidth + d.Spacing)
                : e.Rect.Y + row * (cellHeight + d.Spacing);
            var childAvail = axis == 0 ? cellWidth : cellHeight;

            ref var child = ref GetElement(childOffset);
            LayoutAxis(childOffset, childPos, childAvail, axis, -1);
            childOffset = child.NextSibling;
        }
    }

    private static void LayoutChildrenAxis(ref Element e, float childPos, float childAvail, int axis, int layoutAxis)
    {
        var childOffset = (int)e.FirstChild;
        for (int i = 0; i < e.ChildCount; i++)
        {
            ref var child = ref GetElement(childOffset);
            LayoutAxis(childOffset, childPos, childAvail, axis, layoutAxis);
            childOffset = child.NextSibling;
        }
    }

    private static void LayoutAlignAxis(ref Element e, int axis)
    {
        if (e.ChildCount == 0) return;
        var align = e.Data.Align;
        var childFit = FitAxis(e.FirstChild, axis, -1);
        var alignFactor = (axis == 0 ? align.X : align.Y).ToFactor();
        var childPos = e.Rect[axis] + (e.Rect.GetSize(axis) - childFit) * alignFactor;
        LayoutAxis(e.FirstChild, childPos, childFit, axis, -1);
    }

    private static void LayoutCrossAxis(ref Element e, int axis)
    {
        var pos = e.Rect[axis];
        var avail = e.Rect.GetSize(axis);
        var childOffset = (int)e.FirstChild;
        for (int i = 0; i < e.ChildCount; i++)
        {
            ref var child = ref GetElement(childOffset);
            LayoutAxis(childOffset, pos, avail, axis, axis == 0 ? 1 : 0);
            childOffset = child.NextSibling;
        }
    }

    private static void LayoutRowColumnAxis(ref Element e, int axis, int containerAxis)
    {
        var spacing = e.Data.Spacing;
        var fixedTotal = 0f;
        var flexTotal = 0f;
        var childCount = 0;
        var childOffset = (int)e.FirstChild;
        for (int i = 0; i < e.ChildCount; i++)
        {
            ref var child = ref GetElement(childOffset);
            if (child.Type == ElementType.Flex)
                flexTotal += child.Data.Flex;
            else
                fixedTotal += FitAxis(childOffset, axis, containerAxis);

            childCount++;
            childOffset = child.NextSibling;
        }
        if (childCount > 1)
            fixedTotal += (childCount - 1) * spacing;

        var offset = 0f;
        var remaining = e.Rect.GetSize(axis) - fixedTotal;
        childOffset = (int)e.FirstChild;
        for (int i = 0; i < e.ChildCount; i++)
        {
            if (i > 0) offset += spacing;

            ref var child = ref GetElement(childOffset);
            var childPos = e.Rect[axis] + offset;

            if (child.Type == ElementType.Flex)
            {
                var flexSize = flexTotal > 0 ? (child.Data.Flex / flexTotal) * remaining : 0;
                LayoutAxis(childOffset, childPos, flexSize, axis, containerAxis);
                offset += flexSize;
            }
            else
            {
                var childFit = FitAxis(childOffset, axis, containerAxis);
                LayoutAxis(childOffset, childPos, childFit, axis, containerAxis);
                offset += child.Rect.GetSize(axis);
            }

            childOffset = child.NextSibling;
        }
    }

    private static void UpdateTransforms(int offset, in Matrix3x2 parentTransform, Vector2 parentSize)
    {
        ref var e = ref GetElement(offset);

        Matrix3x2 localTransform;
        Matrix3x2 worldTransform;
        if (e.Type == ElementType.Transform)
        {
            ref var d = ref e.Data.Transform;
            var pivot = new Vector2(e.Rect.Width * d.Pivot.X, e.Rect.Height * d.Pivot.Y);
            localTransform =
                Matrix3x2.CreateScale(d.Scale) *
                Matrix3x2.CreateRotation(MathEx.Deg2Rad * d.Rotate) *
                Matrix3x2.CreateTranslation(e.Rect.X + pivot.X + d.Translate.X,
                                             e.Rect.Y + pivot.Y + d.Translate.Y);
            worldTransform = localTransform * parentTransform;
            e.Transform = worldTransform;

            // Rect becomes element-local (relative to pivot)
            e.Rect.X = -e.Rect.Width * d.Pivot.X;
            e.Rect.Y = -e.Rect.Height * d.Pivot.Y;
        }
        else
        {
            localTransform = Matrix3x2.CreateTranslation(e.Rect.X, e.Rect.Y);
            worldTransform = localTransform * parentTransform;

            e.Transform = worldTransform;
            e.Rect.X = 0;
            e.Rect.Y = 0;
        }

        // Apply scroll offset for Scrollable elements
        float scrollOffset = 0;
        if (e.Type == ElementType.Scroll)
        {
            //ref var sd = ref GetElementData<ScrollElement>(ref e);
            //if (sd.WidgetId > 0)
            //{
            //    ref var state = ref GetWidgetData<ScrollableState>(sd.WidgetId);
            //    scrollOffset = state.Offset;
            //}
        }

        var rectSize = e.Rect.Size;
        var childOffset = (int)e.FirstChild;
        for (int i = 0; i < e.ChildCount; i++)
        {
            ref var child = ref GetElement(childOffset);
            var absPos = child.Rect.Position;
            child.Rect.X = absPos.X - worldTransform.M31;
            child.Rect.Y = absPos.Y - worldTransform.M32 - scrollOffset;
            UpdateTransforms(childOffset, worldTransform, rectSize);
            childOffset = child.NextSibling;
        }
    }
}
