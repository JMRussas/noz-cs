//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;
using System.Runtime.InteropServices;

namespace NoZ.Engine.UI;

internal struct ContainerData
{
    public Size2 Size;
    public float MinWidth;
    public float MinHeight;
    public float MaxWidth;
    public float MaxHeight;
    public Align AlignX;
    public Align AlignY;
    public EdgeInsets Margin;
    public EdgeInsets Padding;
    public Color Color;
    public BorderStyle Border;
    public float Spacing;
    public bool Clip;

    public static ContainerData Default => new()
    {
        Size = Size2.Default,
        MinWidth = 0,
        MinHeight = 0,
        MaxWidth = float.MaxValue,
        MaxHeight = float.MaxValue,
        AlignX = Align.Min,
        AlignY = Align.Min,
        Margin = EdgeInsets.Zero,
        Padding = EdgeInsets.Zero,
        Color = Color.Transparent,
        Border = BorderStyle.None,
        Spacing = 0,
        Clip = false
    };
}

internal struct LabelData
{
    public float FontSize;
    public Color Color;
    public Align AlignX;
    public Align AlignY;
    public int TextStart;
    public int TextLength;

    public static LabelData Default => new()
    {
        FontSize = 16,
        Color = Color.White,
        AlignX = Align.Min,
        AlignY = Align.Center,
        TextStart = 0,
        TextLength = 0
    };
}

internal struct ImageData
{
    public ImageStretch Stretch;
    public Align AlignX;
    public Align AlignY;
    public float Scale;
    public Color Color;
    public nuint Texture;
    public Vector2 UV0;
    public Vector2 UV1;
    public float Width;
    public float Height;

    public static ImageData Default => new()
    {
        Stretch = ImageStretch.Uniform,
        AlignX = Align.Min,
        AlignY = Align.Min,
        Scale = 1.0f,
        Color = Color.White,
        Texture = nuint.Zero,
        UV0 = Vector2.Zero,
        UV1 = Vector2.One,
        Width = 0,
        Height = 0
    };
}

internal struct FlexData
{
    public float Flex;
    public int Axis;

    public static FlexData Default => new()
    {
        Flex = 1.0f,
        Axis = 0
    };
}

internal struct ScrollableData
{
    public float Offset;
    public float ContentHeight;

    public static ScrollableData Default => new()
    {
        Offset = 0,
        ContentHeight = 0
    };
}

internal struct GridData
{
    public float Spacing;
    public int Columns;
    public float CellWidth;
    public float CellHeight;
    public int VirtualCount;
    public int StartRow;

    public static GridData Default => new()
    {
        Spacing = 0,
        Columns = 3,
        CellWidth = 100,
        CellHeight = 100,
        VirtualCount = 0,
        StartRow = 0
    };
}

internal struct TransformData
{
    public Vector2 Origin;
    public Vector2 Translate;
    public float Rotate;
    public Vector2 Scale;

    public static TransformData Default => new()
    {
        Origin = new Vector2(0.5f, 0.5f),
        Translate = Vector2.Zero,
        Rotate = 0,
        Scale = Vector2.One
    };
}

internal struct PopupData
{
    public Align AnchorX;
    public Align AnchorY;
    public Align PopupAlignX;
    public Align PopupAlignY;
    public EdgeInsets Margin;

    public static PopupData Default => new()
    {
        AnchorX = Align.Min,
        AnchorY = Align.Min,
        PopupAlignX = Align.Min,
        PopupAlignY = Align.Min,
        Margin = EdgeInsets.Zero
    };
}

internal struct SpacerData
{
    public Vector2 Size;
}


internal struct CanvasData
{
    public Color Color;
    public Vector2Int ColorOffset;
    public Vector2 WorldPosition;
    public Vector2 WorldSize;

    public static CanvasData Default => new()
    {
        Color = Color.Transparent,
        ColorOffset = Vector2Int.Zero,
        WorldPosition = Vector2.Zero,
        WorldSize = Vector2.Zero
    };
}

[StructLayout(LayoutKind.Explicit)]
internal struct ElementData
{
    [FieldOffset(0)] public ContainerData Container;
    [FieldOffset(0)] public LabelData Label;
    [FieldOffset(0)] public ImageData Image;
    [FieldOffset(0)] public FlexData Flex;
    [FieldOffset(0)] public ScrollableData Scrollable;
    [FieldOffset(0)] public GridData Grid;
    [FieldOffset(0)] public TransformData Transform;
    [FieldOffset(0)] public PopupData Popup;
    [FieldOffset(0)] public SpacerData Spacer;
    [FieldOffset(0)] public CanvasData Canvas;
    [FieldOffset(0)] public TextBoxData TextBox;
}
