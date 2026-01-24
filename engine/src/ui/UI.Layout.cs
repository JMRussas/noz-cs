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

    private static void LayoutRowColumn(ref Element e, ref readonly Element p, int axis)
    {
        var offset = Vector2.Zero;
        var fixedSize = 0.0f;
        var flexTotal = 0.0f;
        var sizeOverride = e.ContentRect.Size;
        sizeOverride[axis] = AutoSize.X;

        var elementIndex = e.Index + 1;
        for (var childIndex = 0; childIndex < e.ChildCount; childIndex++)
        {
            ref var child = ref GetElement(elementIndex);
            if (child.Type == ElementType.Flex)
            {
                flexTotal += child.Data.Flex.Flex;
                elementIndex = child.NextSiblingIndex;
                continue;
            }

            LayoutElement(elementIndex, offset, sizeOverride);

            var childRelativeEnd = (child.Rect[axis] - e.ContentRect[axis]) + child.Rect.GetSize(axis);
            offset[axis] = childRelativeEnd + child.MarginMax[axis] + e.Data.Container.Spacing;
            fixedSize = offset[axis];
            elementIndex = child.NextSiblingIndex;
        }

        if (flexTotal > float.Epsilon && fixedSize < e.ContentRect.Size[axis])
        {
            var flexAvailable = e.ContentRect.Size[axis] - fixedSize;
            var flexOffset = Vector2.Zero;
            elementIndex = e.Index + 1;
            for (var childIndex = 0; childIndex < e.ChildCount; childIndex++)
            {
                ref var child = ref GetElement(elementIndex);
                if (child.Type != ElementType.Flex)
                {
                    var alignOffset = child.MarginMin[axis];
                    child.Rect[axis] = e.ContentRect[axis] + flexOffset[axis] + alignOffset;
                    flexOffset[axis] += alignOffset + child.Rect.GetSize(axis) + child.MarginMax[axis] + e.Data.Container.Spacing;
                }
                else
                {
                    var flex = (child.Data.Flex.Flex / flexTotal) * flexAvailable;
                    var flexSizeOverride = sizeOverride;
                    flexSizeOverride[axis] = flex;
                    LayoutElement(child.Index, flexOffset, flexSizeOverride);
                    flexOffset[axis] += flex;
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
            ( "Padding", e.Data.Container.Padding, e.IsContainer && !e.Data.Container.Padding.IsZero )
            ]);
        LogUI(e, $"Position: ({e.Rect.X}, {e.Rect.Y})", depth: 1, values: [
            ( "Offset", offset + p.ContentRect.Position, offset + p.ContentRect.Position != Vector2.Zero ),
            ( "Align", align, align != Vector2.Zero ),
            ( "AlignX", e.Data.Container.AlignX, e.IsContainer ),
            ( "AlignY", e.Data.Container.AlignY, e.IsContainer ),
            ( "Margin", e.Data.Container.Margin, e.IsContainer && !e.Data.Container.Margin.IsZero)
            ]);
        LogUI(e, $"Content: {e.ContentRect}", depth: 1, condition: () => contentRect != baseContentRect);

        e.LocalToWorld = p.LocalToWorld * Matrix3x2.CreateTranslation(e.Rect.X, e.Rect.Y);
        Matrix3x2.Invert(e.LocalToWorld, out e.WorldToLocal);
        //var localTransform =
        //    Matrix3x2.CreateTranslation(t.Translate + new Vector2(e.Rect.X, e.Rect.Y)) *
        //    Matrix3x2.CreateTranslation(pivot) *
        //    Matrix3x2.CreateRotation(t.Rotate) *
        //    Matrix3x2.CreateScale(t.Scale) *
        //    Matrix3x2.CreateTranslation(-pivot);

        if (e.Type == ElementType.Column)
        {
            LayoutRowColumn(ref e, in p, 1);
        }
        else if (e.Type == ElementType.Row)
        {
            LayoutRowColumn(ref e, in p, 0);
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

    private static void LayoutCanvas(int elementIndex)
    {
        ref var e = ref _elements[elementIndex++];
        Debug.Assert(e.Type == ElementType.Canvas);

        LogUI(e, $"{e.Type}: Index={e.Index} Parent={e.ParentIndex} Sibling={e.NextSiblingIndex}");

        e.Rect = new Rect(0, 0, ScreenSize.X, ScreenSize.Y);
        e.ContentRect = e.Rect;

        for (var childIndex = 0; childIndex < e.ChildCount; childIndex++)
        {
            ref var child = ref _elements[elementIndex];
            LayoutElement(elementIndex, Vector2.Zero, AutoSize);
            elementIndex = child.NextSiblingIndex;
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
