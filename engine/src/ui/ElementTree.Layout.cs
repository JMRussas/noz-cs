//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ;

public static unsafe partial class ElementTree
{
    private static float EdgeInset(in EdgeInsets ei, int axis) => axis == 0 ? ei.Horizontal : ei.Vertical;
    private static float EdgeMin(in EdgeInsets ei, int axis) => axis == 0 ? ei.L : ei.T;

    private static float FitAxis(int offset, int axis, int layoutAxis)
    {
        ref var e = ref GetElement(offset);
        switch (e.Type)
        {
            case ElementType.Size:
            {
                ref var d = ref GetElementData<SizeElement>(ref e);
                var mode = d.Size[axis].Mode;
                if (mode == SizeMode.Default)
                    mode = SizeMode.Fit;
                return mode switch
                {
                    SizeMode.Fixed => d.Size[axis].Value,
                    SizeMode.Fit => e.ChildCount > 0 ? FitAxis(e.FirstChild, axis, layoutAxis) : 0,
                    SizeMode.Percent => 0,
                    _ => 0
                };
            }

            case ElementType.Padding:
            {
                ref var d = ref GetElementData<PaddingElement>(ref e);
                var child = e.ChildCount > 0 ? FitAxis(e.FirstChild, axis, layoutAxis) : 0;
                return child + EdgeInset(d.Padding, axis);
            }

            case ElementType.Margin:
            {
                ref var d = ref GetElementData<MarginElement>(ref e);
                var child = e.ChildCount > 0 ? FitAxis(e.FirstChild, axis, layoutAxis) : 0;
                return child + EdgeInset(d.Margin, axis);
            }

            case ElementType.Border:
            {
                ref var d = ref GetElementData<BorderElement>(ref e);
                var child = e.ChildCount > 0 ? FitAxis(e.FirstChild, axis, layoutAxis) : 0;
                return child + d.Width * 2;
            }

            case ElementType.Fill:
            case ElementType.Clip:
            case ElementType.Opacity:
            case ElementType.Cursor:
            case ElementType.Transform:
            case ElementType.Scrollable:
            case ElementType.Widget:
            case ElementType.Align:
                return e.ChildCount > 0 ? FitAxis(e.FirstChild, axis, layoutAxis) : 0;

            case ElementType.Row:
            {
                ref var d = ref GetElementData<RowElement>(ref e);
                return FitRowColumn(ref e, axis, 0, d.Spacing);
            }

            case ElementType.Column:
            {
                ref var d = ref GetElementData<ColumnElement>(ref e);
                return FitRowColumn(ref e, axis, 1, d.Spacing);
            }

            case ElementType.Flex:
                return e.ChildCount > 0 ? FitAxis(e.FirstChild, axis, layoutAxis) : 0;

            case ElementType.Grid:
                return 0;

            case ElementType.Spacer:
            {
                ref var d = ref GetElementData<SpacerElement>(ref e);
                return d.Size[axis];
            }

            case ElementType.Label:
            {
                ref var d = ref GetElementData<LabelElement>(ref e);
                var font = (Font)_assets[d.AssetIndex]!;
                if (axis == 1 && d.Overflow == TextOverflow.Wrap && e.Rect.Width > 0)
                    return TextRender.MeasureWrapped(d.Text.AsReadOnlySpan(), font, d.FontSize, e.Rect.Width).Y;
                var measure = TextRender.Measure(d.Text.AsReadOnlySpan(), font, d.FontSize);
                return measure[axis];
            }

            case ElementType.Image:
            {
                ref var d = ref GetElementData<ImageElement>(ref e);
                if (d.Size[axis].IsFixed) return d.Size[axis].Value;
                return (axis == 0 ? d.Width : d.Height) * d.Scale;
            }

            case ElementType.Scene:
            {
                ref var d = ref GetElementData<SceneElement>(ref e);
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

    private static float FitRowColumn(ref BaseElement e, int axis, int containerAxis, float spacing)
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

    private static int _layoutDepth;
    private static bool _layoutCycleLogged;

    private static void LayoutAxis(int offset, float position, float available, int axis, int layoutAxis)
    {
        if (_layoutDepth > 200)
        {
            if (!_layoutCycleLogged)
            {
                _layoutCycleLogged = true;
                var sb = new System.Text.StringBuilder();
                sb.AppendLine($"ElementTree: LayoutAxis depth > 200 at offset {offset}, axis={axis}, layoutAxis={layoutAxis}");
                sb.AppendLine($"Tree has {_elements.Length} bytes. Linear dump:");
                DebugDumpLinear(sb);
                Log.Error(sb.ToString());
            }
            return;
        }
        _layoutDepth++;
        LayoutAxisImpl(offset, position, available, axis, layoutAxis);
        _layoutDepth--;
    }

    private static void DebugDumpLinear(System.Text.StringBuilder sb)
    {
        var offset = 0;
        var count = 0;
        while (offset < _elements.Length && count < 200)
        {
            ref var e = ref GetElement(offset);
            var elemSize = GetElementSize(e.Type);
            sb.Append($"  [{offset}] {e.Type} parent={e.Parent} first={e.FirstChild} next={e.NextSibling} children={e.ChildCount}");
            if (e.Type == ElementType.Widget)
            {
                ref var d = ref GetElementData<WidgetElement>(ref e);
                sb.Append($" id={d.Id}");
            }
            sb.AppendLine();
            offset += elemSize;
            count++;
        }
    }

    private static int GetElementSize(ElementType type) => type switch
    {
        ElementType.Widget => sizeof(BaseElement) + sizeof(WidgetElement),
        ElementType.Size => sizeof(BaseElement) + sizeof(SizeElement),
        ElementType.Padding => sizeof(BaseElement) + sizeof(PaddingElement),
        ElementType.Fill => sizeof(BaseElement) + sizeof(FillElement),
        ElementType.Border => sizeof(BaseElement) + sizeof(BorderElement),
        ElementType.Margin => sizeof(BaseElement) + sizeof(MarginElement),
        ElementType.Row => sizeof(BaseElement) + sizeof(RowElement),
        ElementType.Column => sizeof(BaseElement) + sizeof(ColumnElement),
        ElementType.Flex => sizeof(BaseElement) + sizeof(FlexElement),
        ElementType.Align => sizeof(BaseElement) + sizeof(AlignElement),
        ElementType.Clip => sizeof(BaseElement) + sizeof(ClipElement),
        ElementType.Spacer => sizeof(BaseElement) + sizeof(SpacerElement),
        ElementType.Opacity => sizeof(BaseElement) + sizeof(OpacityElement),
        ElementType.Label => sizeof(BaseElement) + sizeof(LabelElement),
        ElementType.Image => sizeof(BaseElement) + sizeof(ImageElement),
        ElementType.EditableText => sizeof(BaseElement) + sizeof(EditableTextElement),
        ElementType.Popup => sizeof(BaseElement) + sizeof(PopupElement),
        ElementType.Cursor => sizeof(BaseElement) + sizeof(CursorElement),
        ElementType.Transform => sizeof(BaseElement) + sizeof(TransformElement),
        ElementType.Grid => sizeof(BaseElement) + sizeof(GridElement),
        ElementType.Scene => sizeof(BaseElement) + sizeof(SceneElement),
        ElementType.Scrollable => sizeof(BaseElement) + sizeof(ScrollableElement),
        _ => sizeof(BaseElement)
    };

    private static void LayoutAxisImpl(int offset, float position, float available, int axis, int layoutAxis)
    {
        ref var e = ref GetElement(offset);
        float size;

        switch (e.Type)
        {
            case ElementType.Size:
            {
                ref var d = ref GetElementData<SizeElement>(ref e);
                var mode = d.Size[axis].Mode;
                var isDefault = mode == SizeMode.Default;
                if (isDefault)
                    mode = (layoutAxis == axis) ? SizeMode.Fit : SizeMode.Percent;
                size = mode switch
                {
                    SizeMode.Fixed => d.Size[axis].Value,
                    SizeMode.Percent => available * (isDefault ? 1.0f : d.Size[axis].Value),
                    SizeMode.Fit => e.ChildCount > 0 ? FitAxis(e.FirstChild, axis, layoutAxis) : 0,
                    _ => 0
                };
                break;
            }

            case ElementType.Padding:
            {
                ref var d = ref GetElementData<PaddingElement>(ref e);
                var inset = EdgeInset(d.Padding, axis);
                size = layoutAxis != axis
                    ? available
                    : (e.ChildCount > 0 ? FitAxis(e.FirstChild, axis, layoutAxis) : 0) + inset;
                break;
            }

            case ElementType.Margin:
            {
                ref var d = ref GetElementData<MarginElement>(ref e);
                var inset = EdgeInset(d.Margin, axis);
                size = layoutAxis != axis
                    ? available
                    : (e.ChildCount > 0 ? FitAxis(e.FirstChild, axis, layoutAxis) : 0) + inset;
                break;
            }

            case ElementType.Border:
            {
                ref var d = ref GetElementData<BorderElement>(ref e);
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
            case ElementType.Scrollable:
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
            {
                ref var d = ref GetElementData<SpacerElement>(ref e);
                size = d.Size[axis];
                break;
            }

            case ElementType.Label:
            {
                ref var d = ref GetElementData<LabelElement>(ref e);
                var font = (Font)_assets[d.AssetIndex]!;
                if (axis == 1 && d.Overflow == TextOverflow.Wrap && e.Rect.Width > 0)
                    size = TextRender.MeasureWrapped(d.Text.AsReadOnlySpan(), font, d.FontSize, e.Rect.Width).Y;
                else
                    size = TextRender.Measure(d.Text.AsReadOnlySpan(), font, d.FontSize)[axis];
                break;
            }

            case ElementType.Image:
            {
                ref var d = ref GetElementData<ImageElement>(ref e);
                if (d.Size[axis].IsFixed)
                    size = d.Size[axis].Value;
                else
                    size = (axis == 0 ? d.Width : d.Height) * d.Scale;
                break;
            }

            case ElementType.Scene:
            {
                ref var d = ref GetElementData<SceneElement>(ref e);
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
                ref var d = ref GetElementData<GridElement>(ref e);
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
            ref var pd = ref GetElementData<PopupElement>(ref e);
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
                ref var d = ref GetElementData<PaddingElement>(ref e);
                var inset = EdgeInset(d.Padding, axis);
                var childPos = e.Rect[axis] + EdgeMin(d.Padding, axis);
                var childAvail = Math.Max(0, size - inset);
                LayoutChildrenAxis(ref e, childPos, childAvail, axis, layoutAxis);
                break;
            }
            case ElementType.Margin:
            {
                ref var d = ref GetElementData<MarginElement>(ref e);
                var inset = EdgeInset(d.Margin, axis);
                var childPos = e.Rect[axis] + EdgeMin(d.Margin, axis);
                var childAvail = Math.Max(0, size - inset);
                LayoutChildrenAxis(ref e, childPos, childAvail, axis, layoutAxis);
                break;
            }
            case ElementType.Border:
            {
                ref var d = ref GetElementData<BorderElement>(ref e);
                var childPos = e.Rect[axis] + d.Width;
                var childAvail = Math.Max(0, size - d.Width * 2);
                LayoutChildrenAxis(ref e, childPos, childAvail, axis, layoutAxis);
                break;
            }
            case ElementType.Size:
            {
                ref var d = ref GetElementData<SizeElement>(ref e);
                var mode = d.Size[axis].Mode;
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
        if (e.Type == ElementType.Scrollable && axis == 1)
        {
            ref var sd = ref GetElementData<ScrollableElement>(ref e);
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

                ref var state = ref GetStateByWidgetId<ScrollableState>(sd.WidgetId);
                state.ContentHeight = contentHeight;

                var maxScroll = Math.Max(0, contentHeight - size);
                if (state.Offset > maxScroll)
                    state.Offset = maxScroll;
            }
        }
    }

    private static void LayoutGridAxis(ref BaseElement e, int axis)
    {
        ref var d = ref GetElementData<GridElement>(ref e);
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

    private static void LayoutChildrenAxis(ref BaseElement e, float childPos, float childAvail, int axis, int layoutAxis)
    {
        var childOffset = (int)e.FirstChild;
        for (int i = 0; i < e.ChildCount; i++)
        {
            ref var child = ref GetElement(childOffset);
            LayoutAxis(childOffset, childPos, childAvail, axis, layoutAxis);
            childOffset = child.NextSibling;
        }
    }

    private static void LayoutAlignAxis(ref BaseElement e, int axis)
    {
        if (e.ChildCount == 0) return;
        ref var d = ref GetElementData<AlignElement>(ref e);
        var childFit = FitAxis(e.FirstChild, axis, -1);
        var alignFactor = (axis == 0 ? d.Align.X : d.Align.Y).ToFactor();
        var childPos = e.Rect[axis] + (e.Rect.GetSize(axis) - childFit) * alignFactor;
        LayoutAxis(e.FirstChild, childPos, childFit, axis, -1);
    }

    private static void LayoutCrossAxis(ref BaseElement e, int axis)
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

    private static void LayoutRowColumnAxis(ref BaseElement e, int axis, int containerAxis)
    {
        float spacing;
        if (e.Type == ElementType.Row)
        {
            ref var d = ref GetElementData<RowElement>(ref e);
            spacing = d.Spacing;
        }
        else
        {
            ref var d = ref GetElementData<ColumnElement>(ref e);
            spacing = d.Spacing;
        }

        var fixedTotal = 0f;
        var flexTotal = 0f;
        var childCount = 0;
        var childOffset = (int)e.FirstChild;
        for (int i = 0; i < e.ChildCount; i++)
        {
            ref var child = ref GetElement(childOffset);
            if (child.Type == ElementType.Flex)
            {
                ref var fd = ref GetElementData<FlexElement>(ref child);
                flexTotal += fd.Flex;
            }
            else
            {
                fixedTotal += FitAxis(childOffset, axis, containerAxis);
            }
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
                ref var fd = ref GetElementData<FlexElement>(ref child);
                var flexSize = flexTotal > 0 ? (fd.Flex / flexTotal) * remaining : 0;
                LayoutAxis(childOffset, childPos, flexSize, axis, containerAxis);
                offset += flexSize;
            }
            else
            {
                LayoutAxis(childOffset, childPos, e.Rect.GetSize(axis), axis, containerAxis);
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
            ref var d = ref GetElementData<TransformElement>(ref e);
            var pivot = new Vector2(e.Rect.Width * d.Pivot.X, e.Rect.Height * d.Pivot.Y);
            localTransform =
                Matrix3x2.CreateScale(d.Scale) *
                Matrix3x2.CreateRotation(MathEx.Deg2Rad * d.Rotate) *
                Matrix3x2.CreateTranslation(e.Rect.X + pivot.X + d.Translate.X,
                                             e.Rect.Y + pivot.Y + d.Translate.Y);
            worldTransform = localTransform * parentTransform;
            d.LocalToWorld = worldTransform;

            // Rect becomes element-local (relative to pivot)
            e.Rect.X = -e.Rect.Width * d.Pivot.X;
            e.Rect.Y = -e.Rect.Height * d.Pivot.Y;
        }
        else
        {
            localTransform = Matrix3x2.CreateTranslation(e.Rect.X, e.Rect.Y);
            worldTransform = localTransform * parentTransform;

            // Store for types that need it in Draw/Input
            StoreLocalToWorld(ref e, in worldTransform);

            e.Rect.X = 0;
            e.Rect.Y = 0;
        }

        // Apply scroll offset for Scrollable elements
        float scrollOffset = 0;
        if (e.Type == ElementType.Scrollable)
        {
            ref var sd = ref GetElementData<ScrollableElement>(ref e);
            if (sd.WidgetId > 0)
            {
                ref var state = ref GetStateByWidgetId<ScrollableState>(sd.WidgetId);
                scrollOffset = state.Offset;
            }
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
