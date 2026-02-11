//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ;

internal enum ElementType : ushort
{
    None = 0,
    Column,
    Container,
    Flex,
    Grid,
    Image,
    Label,
    Row,
    Scrollable,
    Scene,
    Spacer,
    Transform,
    Popup,
    TextBox,
    Opacity,
    Cursor
}

internal struct Element
{
    public ElementType Type;
    public int Id;
    public short Index;
    public short ParentIndex;
    public short NextSiblingIndex;
    public short ChildCount;
    public Rect Rect;
    public Rect ContentRect;
    public Vector2 MeasuredSize;
    public Vector2 AllocatedSize;
    public Matrix3x2 LocalToWorld;
    public Matrix3x2 WorldToLocal;
    public Vector2 Pivot;
    public ElementData Data;
    public object? Asset;

    public readonly bool IsContainer =>
        Type == ElementType.Container ||
        Type == ElementType.Column ||
        Type == ElementType.Row;

    public readonly Vector2 MarginMin =>
        IsContainer ? new Vector2(Data.Container.Margin.L, Data.Container.Margin.T) : Vector2.Zero;

    public readonly Vector2 MarginMax =>
        IsContainer ? new Vector2(Data.Container.Margin.R, Data.Container.Margin.B) : Vector2.Zero;

    public readonly Vector2 Padding =>
        IsContainer ? new Vector2(Data.Container.Padding.Horizontal, Data.Container.Padding.Vertical) : Vector2.Zero; 
}
