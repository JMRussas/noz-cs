//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

#define NOZ_UI_DEBUG

using System.Diagnostics;
using System.Numerics;

namespace NoZ.Engine.UI;

public static partial class UI
{
    private static readonly float[] AlignTable = [ 0.0f, 0.5f, 1.0f ];

    private static float ResolveAlign(ref readonly Element e, ref readonly Element p, Align align, int axis)
    {
        float alignFactor = AlignTable[(int)align];
        float extraSpace = p.Rect.GetSize(axis) - e.Rect.GetSize(axis);
        return alignFactor * extraSpace;
    }

    private static void AlignElement(ref Element e, ref readonly Element p)
    {
        if (e.Type == ElementType.Container)
        {
            e.Rect.X = ResolveAlign(ref e, in p, e.Data.Container.AlignX, 0);
            e.Rect.Y = ResolveAlign(ref e, in p, e.Data.Container.AlignY, 1);
        }

        //e.Rect.X += ResolveAlign(ref e, ref p, e.HorizontalAlign, 0);
        //e.Rect.Y += ResolveAlign(ref e, ref p, e.VerticalAlign, 1);
    }

    private static int LayoutElement(int elementIndex)
    {
        ref var e = ref _elements[elementIndex++];
        LogUI(e, $"{e.Type}: Index={e.Index} Parent={e.ParentIndex} Sibling={e.NextSiblingIndex}");

        ref readonly var p = ref GetParent(in e);

        var size = MeasureElement(in e, in p);
        e.Rect.Width = size.X;
        e.Rect.Height = size.Y;

#if NOZ_UI_DEBUG
        if (e.Type == ElementType.Container)
            LogUI(e, $"Size: ({size.X}, {size.Y})  Width={e.Data.Container.Width} Height={e.Data.Container.Width}");
        else
            LogUI(e, $"Size: ({size.X}, {size.Y})");
#endif

        AlignElement(ref e, in p);

        LogUI(e, $"Position: ({e.Rect.X}, {e.Rect.Y})");

        e.LocalToWorld = p.LocalToWorld * Matrix3x2.CreateTranslation(e.Rect.X, e.Rect.Y);
        //var localTransform =
        //    Matrix3x2.CreateTranslation(t.Translate + new Vector2(e.Rect.X, e.Rect.Y)) *
        //    Matrix3x2.CreateTranslation(pivot) *
        //    Matrix3x2.CreateRotation(t.Rotate) *
        //    Matrix3x2.CreateScale(t.Scale) *
        //    Matrix3x2.CreateTranslation(-pivot);

        for (var childIndex = 0; childIndex < e.ChildCount; childIndex++)
            elementIndex = LayoutElement(elementIndex);

        return e.NextSiblingIndex;

        //return elementIndex;
    }

    private static int LayoutCanvas(int elementIndex)
    {
        ref var e = ref _elements[elementIndex++];
        Debug.Assert(e.Type == ElementType.Canvas);

        LogUI(e, $"{e.Type}: Index={e.Index} Parent={e.ParentIndex} Sibling={e.NextSiblingIndex}");

        e.Rect = new Rect(0, 0, ScreenSize.X, ScreenSize.Y);

        for (var childIndex = 0; childIndex < e.ChildCount; childIndex++)
        {
            //ref var child = ref _elements[elementIndex];
            elementIndex = LayoutElement(elementIndex);
        }

        return elementIndex;
    }
}
