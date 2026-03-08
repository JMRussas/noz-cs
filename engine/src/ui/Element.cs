//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;
using System.Runtime.InteropServices;

namespace NoZ;

internal enum ElementType : byte
{
    Root,
    Widget,
    Size,
    Padding,
    Fill,
    Border,
    Margin,
    Row,
    Column,
    Flex,
    Align,
    Clip,
    Spacer,
    Opacity,
    Text,
    Image,
    EditableText,
    Popup,
    Cursor,
    Transform,
    Grid,
    Scene,
    Scroll,
}

internal struct Element
{
    public ElementType Type;
    public ushort Index;
    public ushort Parent;
    public ushort NextSibling;
    public ushort FirstChild;
    public ushort ChildCount;
    public Rect Rect;
    public ElementData Data;
    public Matrix3x2 Transform;
}

[StructLayout(LayoutKind.Explicit)]
internal struct ElementData
{
    [FieldOffset(0)] public Size2 Size;
    [FieldOffset(0)] public WidgetElement Widget;
    [FieldOffset(0)] public FillElement Fill;
    [FieldOffset(0)] public EditableTextElement EditableText;
    [FieldOffset(0)] public BorderElement Border;
    [FieldOffset(0)] public EdgeInsets Margin;
    [FieldOffset(0)] public EdgeInsets Padding;
    [FieldOffset(0)] public Align2 Align;
    [FieldOffset(0)] public ClipElement Clip;
    [FieldOffset(0)] public float Opacity;
    [FieldOffset(0)] public CursorElement Cursor;
    [FieldOffset(0)] public TransformElement Transform;
    [FieldOffset(0)] public GridElement Grid;
    [FieldOffset(0)] public ScrollElement Scroll;
    [FieldOffset(0)] public float Spacing;
    [FieldOffset(0)] public float Flex;
    [FieldOffset(0)] public PopupElement Popup;
    [FieldOffset(0)] public TextElement Text;
    [FieldOffset(0)] public ImageElement Image;
    [FieldOffset(0)] public SceneElement Scene;
}

internal struct WidgetElement
{
    public ushort Id;
    public UnsafeSpan<byte> State;
    public ElementFlags Flags;
    public ushort LastFrame;
}

internal struct FillElement
{
    public Color Color;
    public BorderRadius Radius;
}

internal struct BorderElement
{
    public float Width;
    public Color Color;
    public BorderRadius Radius;
}

internal struct FlexElement
{
    
}

internal struct ClipElement
{
    public BorderRadius Radius;
}

internal struct SpacerElement
{
    public Vector2 Size;
}

internal struct TextElement
{
    public UnsafeSpan<char> Text;
    public float FontSize;
    public Color Color;
    public Align2 Align;
    public TextOverflow Overflow;
    public ushort Font;
}

internal struct ImageElement
{
    public Size2 Size;
    public ImageStretch Stretch;
    public Align2 Align;
    public float Scale;
    public Color Color;
    public float Width;
    public float Height;
    public ushort Asset;
}

internal struct GridElement
{
    public float Spacing;
    public int Columns;
    public float CellWidth;
    public float CellHeight;
    public float CellMinWidth;
    public float CellHeightOffset;
    public int VirtualCount;
    public int StartIndex;
}

internal struct TransformElement
{
    public Vector2 Pivot;
    public Vector2 Translate;
    public float Rotate;
    public Vector2 Scale;
}

internal struct SceneElement
{
    public Size2 Size;
    public Color ClearColor;
    public int SampleCount;
    public ushort AssetIndex; // stores (Camera, Action) tuple
}

internal struct ScrollElement
{
    public float ScrollSpeed;
    public ScrollbarVisibility ScrollbarVisibility;
    public float ScrollbarWidth;
    public float ScrollbarMinThumbHeight;
    public Color ScrollbarTrackColor;
    public Color ScrollbarThumbColor;
    public Color ScrollbarThumbHoverColor;
    public float ScrollbarPadding;
    public float ScrollbarBorderRadius;
    public int WidgetId;
}

internal struct ScrollableState
{
    public float Offset;
    public float ContentHeight;
}

internal struct CursorElement
{
    public SystemCursor SystemCursor;
    public ushort AssetIndex; // 0 = no sprite, use SystemCursor
    public bool IsSprite;
}

internal struct PopupElement
{
    public Rect AnchorRect;
    public float AnchorFactorX;
    public float AnchorFactorY;
    public float PopupAlignFactorX;
    public float PopupAlignFactorY;
    public float Spacing;
    public bool ClampToScreen;
    public bool AutoClose;
    public bool Interactive;
}

internal struct EditableTextElement
{
    public UnsafeSpan<char> Text;
    public float FontSize;
    public Color TextColor;
    public Color CursorColor;
    public Color SelectionColor;
    public bool MultiLine;
    public ushort Font;
    public int CursorIndex;
    public int SelectionStart;
}
