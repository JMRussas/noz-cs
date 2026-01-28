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

    private static Vector2 AlignElement(ref Element e, ref readonly Element p) => e.IsContainer
        ? new Vector2(
            ResolveAlign(ref e, in p, e.Data.Container.AlignX, 0),
            ResolveAlign(ref e, in p, e.Data.Container.AlignY, 1))
        : Vector2.Zero;

    private static void LayoutGrid(ref Element e)
    {
        ref readonly var grid = ref e.Data.Grid;
        var cellSize = new Vector2(grid.CellWidth, grid.CellHeight);
        var elementIndex = e.Index + 1;

        for (var childIndex = 0; childIndex < e.ChildCount; childIndex++)
        {
            var col = childIndex % grid.Columns;
            var row = childIndex / grid.Columns;
            var offset = new Vector2(
                col * (grid.CellWidth + grid.Spacing),
                row * (grid.CellHeight + grid.Spacing));

            ref var child = ref GetElement(elementIndex);
            LayoutElement(elementIndex, offset, cellSize);
            elementIndex = child.NextSiblingIndex;
        }
    }

    private static void LayoutRowColumn(ref Element e, ref readonly Element p, int axis)
    {
        var offset = Vector2.Zero;
        var fixedSize = 0.0f;
        var flexTotal = 0.0f;
        var sizeOverride = e.ContentRect.Size;
        sizeOverride[axis] = AutoSize.X;

        // First pass: layout non-flex children, spacing only between adjacent non-flex elements
        var prevWasNonFlex = false;
        var elementIndex = e.Index + 1;
        for (var childIndex = 0; childIndex < e.ChildCount; childIndex++)
        {
            ref var child = ref GetElement(elementIndex);
            if (child.Type == ElementType.Flex)
            {
                flexTotal += child.Data.Flex.Flex;
                prevWasNonFlex = false;
                elementIndex = child.NextSiblingIndex;
                continue;
            }

            if (prevWasNonFlex)
                offset[axis] += e.Data.Container.Spacing;

            LayoutElement(elementIndex, offset, sizeOverride);

            var childRelativeEnd = (child.Rect[axis] - e.ContentRect[axis]) + child.Rect.GetSize(axis);
            offset[axis] = childRelativeEnd + child.MarginMax[axis];
            fixedSize = offset[axis];
            prevWasNonFlex = true;
            elementIndex = child.NextSiblingIndex;
        }

        if (flexTotal > float.Epsilon && fixedSize < e.ContentRect.Size[axis])
        {
            var flexAvailable = e.ContentRect.Size[axis] - fixedSize;
            var flexOffset = Vector2.Zero;
            prevWasNonFlex = false;
            elementIndex = e.Index + 1;
            for (var childIndex = 0; childIndex < e.ChildCount; childIndex++)
            {
                ref var child = ref GetElement(elementIndex);
                if (child.Type != ElementType.Flex)
                {
                    if (prevWasNonFlex)
                        flexOffset[axis] += e.Data.Container.Spacing;

                    var align = child.MarginMin[axis];
                    child.Rect[axis] = e.ContentRect[axis] + flexOffset[axis] + align;
                    flexOffset[axis] += align + child.Rect.GetSize(axis) + child.MarginMax[axis];
                    prevWasNonFlex = true;
                }
                else
                {
                    var flex = (child.Data.Flex.Flex / flexTotal) * flexAvailable;
                    var flexSizeOverride = sizeOverride;
                    flexSizeOverride[axis] = flex;
                    LayoutElement(child.Index, flexOffset, flexSizeOverride);
                    flexOffset[axis] += flex;
                    prevWasNonFlex = false;
                }

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

        var align = AlignElement(ref e, in p);
        e.Rect.X = align.X + offset.X + p.ContentRect.X;
        e.Rect.Y = align.Y + offset.Y + p.ContentRect.Y;

        // Position popup relative to anchor (in parent-local space)
        // Transform pass will convert to canvas space
        if (e.Type == ElementType.Popup)
        {
            ref var popup = ref e.Data.Popup;

            // Anchor rect is in parent-local space, position popup relative to it
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
    }

    private static void UpdateTransforms(ref Element e, ref readonly Element p)
    {
        // Parent's pivot offset - distance from parent's top-left to parent's LocalToWorld origin
        var parentPivotOffset = new Vector2(p.Rect.Width * p.Pivot.X, p.Rect.Height * p.Pivot.Y);

        // Child position relative to parent's LocalToWorld origin
        var childPos = new Vector2(e.Rect.X, e.Rect.Y) - parentPivotOffset;

        // Clamp popup to screen bounds if requested
        if (e.Type == ElementType.Popup && e.Data.Popup.ClampToScreen)
        {
            var popupWorldPos = Vector2.Transform(childPos, p.LocalToWorld);
            var clampedX = Math.Clamp(popupWorldPos.X, 0, ScreenSize.X - e.Rect.Width);
            var clampedY = Math.Clamp(popupWorldPos.Y, 0, ScreenSize.Y - e.Rect.Height);
            childPos = Vector2.Transform(new Vector2(clampedX, clampedY), p.WorldToLocal);
        }

        Matrix3x2 localTransform;
        if (e.Type == ElementType.Transform)
        {
            ref var t = ref e.Data.Transform;
            e.Pivot = t.Pivot;

            // Pivot point offset from element's top-left
            var pivot = new Vector2(e.Rect.Width * e.Pivot.X, e.Rect.Height * e.Pivot.Y);

            // LocalToWorld positions at the pivot point, with scale/rotate applied
            // Children use -parentPivotOffset which maps them to top-left relative positions
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

        e.LocalToWorld = localTransform * p.LocalToWorld;
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

    private static void LayoutCanvas(int elementIndex)
    {
        ref var e = ref _elements[elementIndex++];
        Debug.Assert(e.Type == ElementType.Canvas);

        LogUI(e, $"{e.Type}: Index={e.Index} Parent={e.ParentIndex} Sibling={e.NextSiblingIndex}");

        e.Rect = new Rect(0, 0, ScreenSize.X, ScreenSize.Y);
        e.ContentRect = e.Rect;
        e.Pivot = Vector2.Zero;
        e.LocalToWorld = Matrix3x2.Identity;
        e.WorldToLocal = Matrix3x2.Identity;

        for (var childIndex = 0; childIndex < e.ChildCount; childIndex++)
        {
            ref var child = ref _elements[elementIndex];
            LayoutElement(elementIndex, Vector2.Zero, AutoSize);
            elementIndex = child.NextSiblingIndex;
            UpdateTransforms(ref child, ref e);
        }
    }

    private static void LayoutElements()
    {
        LogUI("Layout", condition: () => _elementCount > 0);

        for (int elementIndex = 0; elementIndex < _elementCount;)
        {
            ref var canvas = ref _elements[elementIndex];
            Debug.Assert(canvas.Type == ElementType.Canvas);
            LayoutCanvas(elementIndex);
            elementIndex = canvas.NextSiblingIndex;
        }            
    }
}
