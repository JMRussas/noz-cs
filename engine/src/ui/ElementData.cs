//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;
using System.Runtime.InteropServices;

namespace NoZ;

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
    public ushort Order;

    public readonly bool IsAutoWidth => Size.Width.Mode is SizeMode.Percent or SizeMode.Default;
    public readonly bool IsAutoHeight => Size.Height.Mode is SizeMode.Percent or SizeMode.Default;

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
    public ushort Order;
    public bool Wrap;
    public UnsafeSpan<char> Text;

    public static LabelData Default => new()
    {
        FontSize = 16,
        Color = Color.White,
        AlignX = Align.Min,
        AlignY = Align.Center,
        Text = new UnsafeSpan<char>(),
    };
}

internal struct ImageData
{
    public Size2 Size;
    public ImageStretch Stretch;
    public Align AlignX;
    public Align AlignY;
    public float Scale;
    public Color Color;
    public ushort Order;
    public nuint Texture;
    public Vector2 UV0;
    public Vector2 UV1;
    public float Width;
    public float Height;
    public int AtlasIndex;
    public BorderRadius BorderRadius;

    public static ImageData Default => new()
    {
        Size = Size2.Default,
        Stretch = ImageStretch.Uniform,
        AlignX = Align.Min,
        AlignY = Align.Min,
        Scale = 1.0f,
        Color = Color.White,
        Texture = nuint.Zero,
        UV0 = Vector2.Zero,
        UV1 = Vector2.One,
        Width = 0,
        Height = 0,
        AtlasIndex = -1,
        BorderRadius = BorderRadius.Zero
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
    // Runtime state
    public float Offset;
    public float ContentHeight;

    // Style configuration
    public float ScrollSpeed;
    public ScrollbarVisibility ScrollbarVisibility;
    public float ScrollbarWidth;
    public float ScrollbarMinThumbHeight;
    public Color ScrollbarTrackColor;
    public Color ScrollbarThumbColor;
    public Color ScrollbarThumbHoverColor;
    public float ScrollbarPadding;
    public float ScrollbarBorderRadius;

    public static ScrollableData Default => new()
    {
        Offset = 0,
        ContentHeight = 0,
        ScrollSpeed = 30f,
        ScrollbarVisibility = ScrollbarVisibility.Auto,
        ScrollbarWidth = 8f,
        ScrollbarMinThumbHeight = 20f,
        ScrollbarTrackColor = new Color(0.15f, 0.15f, 0.15f, 0.5f),
        ScrollbarThumbColor = new Color(0.5f, 0.5f, 0.5f, 0.8f),
        ScrollbarThumbHoverColor = new Color(0.6f, 0.6f, 0.6f, 1f),
        ScrollbarPadding = 2f,
        ScrollbarBorderRadius = 4f
    };
}

internal struct GridData
{
    public float Spacing;
    public int Columns;
    public float CellWidth;
    public float CellHeight;
    public float CellMinWidth;
    public float CellHeightOffset;
    public int VirtualCount;
    public int StartIndex;

    public readonly bool IsAutoWidth => CellWidth <= 0;

    public static GridData Default => new()
    {
        Spacing = 0,
        Columns = 3,
        CellWidth = 100,
        CellHeight = 100,
        CellMinWidth = 0,
        CellHeightOffset = 0,
        VirtualCount = 0,
        StartIndex = 0
    };
}

internal struct TransformData
{
    public Vector2 Pivot;
    public Vector2 Translate;
    public float Rotate;
    public Vector2 Scale;

    public static TransformData Default => new()
    {
        Pivot = new Vector2(0.5f, 0.5f),
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
    public float Spacing;
    public bool ClampToScreen;
    public Rect AnchorRect;
    public float MinWidth;
    public bool AutoClose;

    public static PopupData Default => new()
    {
        AnchorX = Align.Min,
        AnchorY = Align.Min,
        PopupAlignX = Align.Min,
        PopupAlignY = Align.Min,
        Spacing = 0,
        MinWidth = 0,
        ClampToScreen = false,
        AnchorRect = Rect.Zero,
        AutoClose = true
    };
}

internal struct OpacityData
{
    public float Value;
}

internal struct CursorData
{
    public SystemCursor SystemCursor;
}

internal struct SpacerData
{
    public Vector2 Size;
}

internal struct SceneData
{
    public Size2 Size;
    public Color Color;
    public int SampleCount;

    public static SceneData Default => new()
    {
        Size = Size2.Default,
        Color = Color.Transparent,
        SampleCount = 1
    };
}


public struct TextBoxData
{
    public Size Height;
    public float FontSize;
    public Color BackgroundColor;
    public Color TextColor;
    public Color PlaceholderColor;
    public Color SelectionColor;
    public BorderStyle Border;
    public BorderStyle FocusBorder;
    public EdgeInsets Padding;
    public UnsafeSpan<char> Placeholder;
    public bool Password;
    public InputScope Scope;

    public static TextBoxData Default => new()
    {
        Height = 28f,
        FontSize = 16,
        BackgroundColor = new Color(0.22f, 0.22f, 0.22f, 1f),
        TextColor = Color.White,
        PlaceholderColor = new Color(0.4f, 0.4f, 0.4f, 1f),
        SelectionColor = new Color(0.2f, 0.4f, 0.8f, 0.5f),
        Border = BorderStyle.None,
        FocusBorder = BorderStyle.None,
        Padding = EdgeInsets.Zero,
        Password = false,
        Placeholder = UnsafeSpan<char>.Empty
    };
}

public struct TextAreaData
{
    public Size Height;
    public float FontSize;
    public Color BackgroundColor;
    public Color TextColor;
    public Color PlaceholderColor;
    public Color SelectionColor;
    public BorderStyle Border;
    public BorderStyle FocusBorder;
    public EdgeInsets Padding;
    public UnsafeSpan<char> Placeholder;
    public InputScope Scope;

    public static TextAreaData Default => new()
    {
        Height = Size.Default,
        FontSize = 16,
        BackgroundColor = new Color(0.22f, 0.22f, 0.22f, 1f),
        TextColor = Color.White,
        PlaceholderColor = new Color(0.4f, 0.4f, 0.4f, 1f),
        SelectionColor = new Color(0.2f, 0.4f, 0.8f, 0.5f),
        Border = BorderStyle.None,
        FocusBorder = BorderStyle.None,
        Padding = EdgeInsets.Zero,
        Placeholder = UnsafeSpan<char>.Empty
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
    [FieldOffset(0)] public TextBoxData TextBox;
    [FieldOffset(0)] public TextAreaData TextArea;
    [FieldOffset(0)] public SceneData Scene;
    [FieldOffset(0)] public OpacityData Opacity;
    [FieldOffset(0)] public CursorData Cursor;
}
