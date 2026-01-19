//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ.Engine.UI;

internal enum ElementType : byte
{
    None = 0,
    Canvas,
    Column,
    Container,
    Flex,
    Grid,
    Image,
    Label,
    Row,
    Scrollable,
    Spacer,
    Transform,
    Popup,
    TextBox
}

internal struct Element
{
    public ElementType Type;
    public byte Id;
    public byte CanvasId;
    public int Index;
    public int ParentIndex;
    public int NextSiblingIndex;
    public int ChildCount;
    public Rect Rect;
    public Rect ContentRect;
    public Vector2 MeasuredSize;
    public Vector2 AllocatedSize;
    public Matrix3x2 LocalToWorld;
    public Matrix3x2 WorldToLocal;
    public ElementData Data;
    public Font? Font;

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
