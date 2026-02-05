//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;
using System.Diagnostics;

namespace NoZ;

[DebuggerDisplay("{Value}")]
public struct CanvasId : IEquatable<CanvasId>
{
    public byte Value;

    public CanvasId(byte value)
    {
        Debug.Assert(value <= MaxValue, "CanvasId value exceeds maximum");
        Value = value;
    }

    public readonly bool Equals(CanvasId other) => other.Value == Value;
    public override readonly bool Equals(object? obj) =>
        obj is CanvasId canvasId && Equals(canvasId);

    public static bool operator ==(CanvasId a, CanvasId b) => a.Value == b.Value;
    public static bool operator !=(CanvasId a, CanvasId b) => a.Value != b.Value;
    public readonly override int GetHashCode() => Value;
    public static implicit operator byte(CanvasId id) => id.Value;
    public static implicit operator CanvasId(byte value) => new(value);
    public static implicit operator CanvasId(int value) => new((byte)value);

    public const byte None = 0;
    public const byte MaxValue = 64;
}

[DebuggerDisplay("{Value}")]
public struct ElementId : IEquatable<ElementId>
{
    public byte Value;

    public ElementId(byte value)
    {
        Debug.Assert(value <= MaxValue, "ElementId value exceeds maximum");
        Value = value;
    }

    public readonly bool Equals(ElementId other) => other.Value == Value;
    public override readonly bool Equals(object? obj) =>
        obj is ElementId elementId && Equals(elementId);

    public static bool operator ==(ElementId a, ElementId b) => a.Value == b.Value;
    public static bool operator !=(ElementId a, ElementId b) => a.Value != b.Value;
    public readonly override int GetHashCode() => Value;
    public static implicit operator byte(ElementId id) => id.Value;
    public static implicit operator ElementId(byte value) => new(value);
    public static implicit operator ElementId(int value) => new((byte)value);
    public const byte None = 0;
    public const byte MaxValue = 255;
}

internal enum ElementType : ushort
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
    Scene,
    Spacer,
    Transform,
    Popup,
    TextBox
}

internal struct Element
{
    public ElementType Type;
    public ElementId Id;
    public CanvasId CanvasId;
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
    public Font? Font;
    public Sprite? Sprite;

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
