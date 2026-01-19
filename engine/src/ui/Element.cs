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
    public Vector2 MeasuredSize;
    public Vector2 AllocatedSize;
    public Matrix3x2 LocalToWorld;
    public Matrix3x2 WorldToLocal;
    public ElementData Data;
    public Font Font;
}
