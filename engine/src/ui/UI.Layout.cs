//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

// #define NOZ_UI_DEBUG

using System.Diagnostics;
using System.Numerics;

namespace NoZ;

public static partial class UI
{
    private static readonly Vector2 AutoSize = new(float.MaxValue, float.MaxValue);

    private static float ResolveAlign(ref readonly Element e, ref readonly Element p, Align align, int axis)
    {
        float alignFactor = align.ToFactor();
        float marginMin = e.MarginMin[axis];
        float marginMax = e.MarginMax[axis];
        float extraSpace = p.ContentRect.GetSize(axis) - e.Rect.GetSize(axis) - marginMin - marginMax;
        return alignFactor * extraSpace + marginMin;
    }

    private static Vector2 AlignElement(ref Element e, ref readonly Element p)
    {
        Align alignX, alignY;
        if (e.IsContainer)
        {
            alignX = e.Data.Container.AlignX;
            alignY = e.Data.Container.AlignY;
        }
        else if (e.Type == ElementType.Image)
        {
            alignX = e.Data.Image.AlignX;
            alignY = e.Data.Image.AlignY;
        }
        else
        {
            return Vector2.Zero;
        }

        // For Row children, don't apply AlignX (X position is determined by row layout)
        // For Column children, don't apply AlignY (Y position is determined by column layout)
        var ax = p.Type == ElementType.Row ? 0 : ResolveAlign(ref e, in p, alignX, 0);
        var ay = p.Type == ElementType.Column ? 0 : ResolveAlign(ref e, in p, alignY, 1);
        return new Vector2(ax, ay);
    }

    private static void LayoutGrid(ref Element e)
    {
        ref readonly var grid = ref e.Data.Grid;
        var (columns, cellWidth, cellHeight) = ResolveGridCellSize(
            grid.Columns, grid.CellWidth, grid.CellHeight,
            grid.CellMinWidth, grid.CellHeightOffset,
            grid.Spacing, e.Rect.Width);

        var cellSize = new Vector2(cellWidth, cellHeight);
        var rowHeight = cellHeight + grid.Spacing;
        var elementIndex = e.Index + 1;

        for (var childIndex = 0; childIndex < e.ChildCount; childIndex++)
        {
            var virtualIndex = grid.StartIndex + childIndex;
            var col = virtualIndex % columns;
            var row = virtualIndex / columns;
            var offset = new Vector2(
                col * (cellWidth + grid.Spacing),
                row * rowHeight);

            ref var child = ref GetElement(elementIndex);
            LayoutElement(elementIndex, offset, cellSize);
            elementIndex = child.NextSiblingIndex;
        }
    }

    private static void LayoutRowColumn(ref Element e, ref readonly Element p, int axis)
    {
        var offset = Vector2.Zero;
        var nonFlexSize = 0.0f;
        var totalSpacing = 0.0f;
        var flexTotal = 0.0f;
        var sizeOverride = e.ContentRect.Size;
        sizeOverride[axis] = AutoSize.X;

        // First pass: layout non-flex children and calculate total fixed size (including all spacing)
        var prevWasChild = false;
        var elementIndex = e.Index + 1;
        for (var childIndex = 0; childIndex < e.ChildCount; childIndex++)
        {
            ref var child = ref GetElement(elementIndex);
            if (child.Type == ElementType.Popup)
            {
                LayoutElement(elementIndex, Vector2.Zero, AutoSize);
                elementIndex = child.NextSiblingIndex;
                continue;
            }

            // Count spacing before any child (including flex)
            if (prevWasChild)
            {
                totalSpacing += e.Data.Container.Spacing;
                offset[axis] += e.Data.Container.Spacing;
            }

            if (child.Type == ElementType.Flex)
            {
                flexTotal += child.Data.Flex.Flex;
                prevWasChild = true;
                elementIndex = child.NextSiblingIndex;
                continue;
            }

            // Layout non-flex child
            offset[axis] += child.MarginMin[axis];
            var childOverride = sizeOverride;
            var crossAxis = 1 - axis;
            var crossMode = SizeMode.Default;
            if (child.IsContainer)
                crossMode = child.Data.Container.Size[crossAxis].Mode;
            else if (child.Type == ElementType.Image)
                crossMode = child.Data.Image.Size[crossAxis].Mode;
            if (crossMode is SizeMode.Fit or SizeMode.Fixed)
                childOverride[crossAxis] = AutoSize[crossAxis];
            else
                childOverride[crossAxis] -= child.MarginMin[crossAxis] + child.MarginMax[crossAxis];
            LayoutElement(elementIndex, offset, childOverride);

            var childSize = child.Rect.GetSize(axis) + child.MarginMax[axis];
            nonFlexSize += child.MarginMin[axis] + childSize;
            offset[axis] += childSize;
            prevWasChild = true;
            elementIndex = child.NextSiblingIndex;
        }

        var fixedSize = nonFlexSize + totalSpacing;

        if (flexTotal > float.Epsilon && fixedSize < e.ContentRect.Size[axis])
        {
            var flexAvailable = e.ContentRect.Size[axis] - fixedSize;
            var flexOffset = Vector2.Zero;
            prevWasChild = false;
            elementIndex = e.Index + 1;
            for (var childIndex = 0; childIndex < e.ChildCount; childIndex++)
            {
                ref var child = ref GetElement(elementIndex);
                if (child.Type == ElementType.Popup)
                {
                    elementIndex = child.NextSiblingIndex;
                    continue;
                }

                if (prevWasChild)
                    flexOffset[axis] += e.Data.Container.Spacing;

                if (child.Type != ElementType.Flex)
                {
                    var align = child.MarginMin[axis];
                    child.Rect[axis] = e.ContentRect[axis] + flexOffset[axis] + align;
                    flexOffset[axis] += align + child.Rect.GetSize(axis) + child.MarginMax[axis];
                }
                else
                {
                    var flex = (child.Data.Flex.Flex / flexTotal) * flexAvailable;
                    var flexSizeOverride = sizeOverride;
                    flexSizeOverride[axis] = flex;
                    LayoutElement(child.Index, flexOffset, flexSizeOverride);
                    flexOffset[axis] += flex;
                }

                prevWasChild = true;
                elementIndex = child.NextSiblingIndex;
            }
        }
    }

    private static void LayoutElement(int elementIndex, in Vector2 offset, in Vector2 sizeOverride)
    {
        ref var e = ref _elements[elementIndex++];
        LogUI(e, $"{(e.ChildCount>0?"+":"-")} {e.Type}: Index={e.Index} Parent={e.ParentIndex} Sibling={e.NextSiblingIndex}", depth: -1);

        ref readonly var p = ref GetParent(in e);
        var size = MeasureElement(in e, in p);
        e.Rect.Width = sizeOverride.X < AutoSize.X ? sizeOverride.X : size.X;
        e.Rect.Height = sizeOverride.Y < AutoSize.Y ? sizeOverride.Y : size.Y;

        // Re-measure wrapped label height now that width is known.
        // Only when height resolved via Fit (Column parent, unconstrained Y) — not Percent.
        if (e.Type == ElementType.Label && e.Data.Label.Wrap && e.Rect.Width > 0
            && p.Type == ElementType.Column && sizeOverride.Y >= AutoSize.Y)
        {
            e.Rect.Height = TextRender.MeasureWrapped(
                e.Data.Label.Text.AsReadOnlySpan(),
                (Font)e.Asset!,
                e.Data.Label.FontSize,
                e.Rect.Width,
                cacheId: e.Id).Y;
        }

        var align = AlignElement(ref e, in p);
        e.Rect.X = align.X + offset.X + p.ContentRect.X;
        e.Rect.Y = align.Y + offset.Y + p.ContentRect.Y;

        // Position popup relative to anchor (in canvas space)
        if (e.Type == ElementType.Popup)
        {
            ref var popup = ref e.Data.Popup;

            if (popup.AnchorRect == Rect.Zero)
            {
                // Full-screen popup (used for dialogs)
                e.Rect = new Rect(0, 0, ScreenSize.X, ScreenSize.Y);
            }
            else
            {
                if (popup.MinWidth > 0 && e.Rect.Width < popup.MinWidth)
                    e.Rect.Width = popup.MinWidth;

                // Anchor rect is in canvas space
                var anchorX = popup.AnchorRect.X + popup.AnchorRect.Width * popup.AnchorX.ToFactor();
                var anchorY = popup.AnchorRect.Y + popup.AnchorRect.Height * popup.AnchorY.ToFactor();

                // Position popup so the specified edge/corner aligns with anchor point
                e.Rect.X = anchorX - e.Rect.Width * popup.PopupAlignX.ToFactor();
                e.Rect.Y = anchorY - e.Rect.Height * popup.PopupAlignY.ToFactor();

                // Apply spacing only in directions where anchor and popup alignments differ
                // (where there's actual separation - e.g. popup to right, popup above, etc.)
                if (popup.AnchorX != popup.PopupAlignX)
                {
                    var spacingDirX = 1f - 2f * popup.PopupAlignX.ToFactor();
                    e.Rect.X += popup.Spacing * spacingDirX;
                }
                if (popup.AnchorY != popup.PopupAlignY)
                {
                    var spacingDirY = 1f - 2f * popup.PopupAlignY.ToFactor();
                    e.Rect.Y += popup.Spacing * spacingDirY;
                }
            }
        }

        var padding = e.IsContainer ? e.Data.Container.Padding : EdgeInsets.Zero;
        var baseContentRect = new Rect(0, 0, e.Rect.Width, e.Rect.Height);
        var contentRect = baseContentRect;
        contentRect = baseContentRect;
        contentRect.Width -= padding.Horizontal;
        contentRect.Height -= padding.Vertical;
        contentRect.X += padding.L;
        contentRect.Y += padding.T;
        e.ContentRect = contentRect;

        LogUI(e, $"Size: {e.Rect.Size}", depth: 1, values: [
            ( "Width", e.Data.Container.Size.Width, e.IsContainer ),
            ( "Height", e.Data.Container.Size.Height, e.IsContainer ),
            ( "Padding", e.Data.Container.Padding, e.IsContainer && !e.Data.Container.Padding.IsZero ),
            ( "Spacing", e.Data.Container.Spacing, e.IsContainer && e.Data.Container.Spacing > 0 )
            ]);
        LogUI(e, $"Position: ({e.Rect.X}, {e.Rect.Y})", depth: 1, values: [
            ( "Offset", offset + p.ContentRect.Position, offset + p.ContentRect.Position != Vector2.Zero ),
            ( "Align", align, align != Vector2.Zero ),
            ( "AlignX", e.Data.Container.AlignX, e.IsContainer ),
            ( "AlignY", e.Data.Container.AlignY, e.IsContainer ),
            ( "Margin", e.Data.Container.Margin, e.IsContainer && !e.Data.Container.Margin.IsZero)
            ]);
        LogUI(e, $"Content: {e.ContentRect}", depth: 1, condition: () => contentRect != baseContentRect);

        if (e.Type == ElementType.Column)
        {
            LayoutRowColumn(ref e, in p, 1);
        }
        else if (e.Type == ElementType.Row)
        {
            LayoutRowColumn(ref e, in p, 0);
        }
        else if (e.Type == ElementType.Grid)
        {
            LayoutGrid(ref e);
        }
        else
        {
            for (var childIndex = 0; childIndex < e.ChildCount; childIndex++)
            {
                ref var child = ref GetElement(elementIndex);
                LayoutElement(elementIndex, Vector2.Zero, AutoSize);
                elementIndex = child.NextSiblingIndex;
            }
        }

        // Calculate content height for scrollables
        if (e.Type == ElementType.Scrollable)
        {
            var contentHeight = 0f;
            var childIdx = e.Index + 1;
            for (var i = 0; i < e.ChildCount; i++)
            {
                ref var child = ref GetElement(childIdx);
                var childBottom = child.Rect.Y + child.Rect.Height - e.ContentRect.Y;
                contentHeight = Math.Max(contentHeight, childBottom);
                childIdx = child.NextSiblingIndex;
            }
            e.Data.Scrollable.ContentHeight = contentHeight;
        }
    }

    private static void UpdateTransforms(ref Element e, ref readonly Element p)
    {
        // Parent's pivot offset - distance from parent's top-left to parent's LocalToWorld origin
        var parentPivotOffset = new Vector2(p.Rect.Width * p.Pivot.X, p.Rect.Height * p.Pivot.Y);

        // Child position relative to parent's LocalToWorld origin
        var childPos = new Vector2(e.Rect.X, e.Rect.Y) - parentPivotOffset;

        // Apply scroll offset if parent is scrollable
        if (p.Type == ElementType.Scrollable && p.Id != 0)
        {
            ref var ps = ref GetElementState(p.Id);
            childPos.Y -= ps.Data.Scrollable.Offset;
        }

        // Clamp popup to screen bounds if requested (popup rect is already in canvas space)
        if (e.Type == ElementType.Popup && e.Data.Popup.ClampToScreen)
        {
            e.Rect.X = Math.Clamp(e.Rect.X, 0, ScreenSize.X - e.Rect.Width);
            e.Rect.Y = Math.Clamp(e.Rect.Y, 0, ScreenSize.Y - e.Rect.Height);
        }

        Matrix3x2 localTransform;
        if (e.Type == ElementType.Transform)
        {
            ref var t = ref e.Data.Transform;
            e.Pivot = t.Pivot;

            // Pivot point offset from element's top-left
            var pivot = new Vector2(e.Rect.Width * e.Pivot.X, e.Rect.Height * e.Pivot.Y);

            // LocalToWorld positions at the pivot point, with scale/rotate applied around pivot
            // Matrix order: S * R * T means point is scaled first, then rotated, then translated
            localTransform =
                Matrix3x2.CreateScale(t.Scale) *
                Matrix3x2.CreateRotation(MathEx.Deg2Rad * t.Rotate) *
                Matrix3x2.CreateTranslation(childPos + pivot + t.Translate);
        }
        else
        {
            e.Pivot = Vector2.Zero;
            localTransform = Matrix3x2.CreateTranslation(childPos);
        }

        // Popups use canvas-space positioning, not parent-relative
        if (e.Type == ElementType.Popup)
        {
            e.LocalToWorld = Matrix3x2.CreateTranslation(e.Rect.X, e.Rect.Y);
        }
        else
        {
            e.LocalToWorld = localTransform * p.LocalToWorld;
        }
        Matrix3x2.Invert(e.LocalToWorld, out e.WorldToLocal);

        // Adjust Rect to be element-local (relative to LocalToWorld origin which is at pivot)
        // This allows draw/hit-test code to use Rect directly without pivot calculations
        e.Rect.X = -e.Rect.Width * e.Pivot.X;
        e.Rect.Y = -e.Rect.Height * e.Pivot.Y;

        var elementIndex = e.Index + 1;
        for (var childIndex = 0; childIndex < e.ChildCount; childIndex++)
        {
            ref var child = ref GetElement(elementIndex);
            UpdateTransforms(ref child, in e);
            elementIndex = child.NextSiblingIndex;
        }
    }

    private static void LayoutElements()
    {
        LogUI("Layout", condition: () => _elementCount > 0);

        if (_elementCount == 0) return;

        // Root container is always at index 0
        ref var root = ref _elements[0];
        Debug.Assert(root.Type == ElementType.Container);

        root.Rect = new Rect(0, 0, ScreenSize.X, ScreenSize.Y);
        root.ContentRect = root.Rect;
        root.Pivot = Vector2.Zero;
        root.LocalToWorld = Matrix3x2.Identity;
        root.WorldToLocal = Matrix3x2.Identity;

        var elementIndex = 1;
        for (var childIndex = 0; childIndex < root.ChildCount; childIndex++)
        {
            ref var child = ref _elements[elementIndex];
            LayoutElement(elementIndex, Vector2.Zero, AutoSize);
            UpdateTransforms(ref child, ref root);
            elementIndex = child.NextSiblingIndex;
        }
    }
}
