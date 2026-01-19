//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ.Engine.UI;

public struct BorderStyle
{
    public float Radius;
    public float Width;
    public Color Color;

    public static readonly BorderStyle None = new() { Radius = 0, Width = 0, Color = Color.Transparent };
}

public struct ContainerStyle()
{
    public Size Width = Size.Default;
    public Size Height = Size.Default;
    public float MinWidth = 0;
    public float MinHeight = 0;
    public float MaxWidth = float.MaxValue;
    public float MaxHeight = float.MaxValue;
    public Align AlignX = Align.Min;
    public Align AlignY = Align.Min;
    public EdgeInsets Margin = EdgeInsets.Zero;
    public EdgeInsets Padding = EdgeInsets.Zero;
    public Color Color = Color.Transparent;
    public BorderStyle Border = BorderStyle.None;
    public float Spacing = 0;
    public bool Clip = false;

    internal ContainerData ToData() => new()
    {
        Size = new Size2(Width, Height),
        MinWidth = MinWidth,
        MinHeight = MinHeight,
        MaxWidth = MaxWidth,
        MaxHeight = MaxHeight,
        AlignX = AlignX,
        AlignY = AlignY,
        Margin = Margin,
        Padding = Padding,
        Color = Color,
        Border = Border,
        Spacing = Spacing,
        Clip = Clip
    };

    public static readonly ContainerStyle Default = new();
}

public struct LabelStyle()
{
    public float FontSize = 16;
    public Color Color = Color.White;
    public Align AlignX = Align.Min;
    public Align AlignY = Align.Center;
    public Font? Font = null;
}

public struct ImageStyle()
{
    public ImageStretch Stretch = ImageStretch.Uniform;
    public Align AlignX = Align.Min;
    public Align AlignY = Align.Min;
    public float Scale = 1.0f;
    public Color Color = Color.White;
}

public struct RectangleStyle()
{
    public float Width = 0;
    public float Height = 0;
    public Color Color = Color.White;
}

public struct TransformStyle()
{
    public Vector2 Origin = new(0.5f, 0.5f);
    public Vector2 Translate = Vector2.Zero;
    public float Rotate = 0;
    public Vector2 Scale = Vector2.One;
}

public struct ScrollableStyle()
{
}

public struct GridStyle()
{
    public float Spacing = 0;
    public int Columns = 3;
    public float CellWidth = 100;
    public float CellHeight = 100;
    public int VirtualCount = 0;
    public byte ScrollId = 0;
    public Action<int, int>? VirtualCellFunc = null;
    public Action<int, int>? VirtualRangeFunc = null;
}

public struct PopupStyle()
{
    public Align AnchorX = Align.Min;
    public Align AnchorY = Align.Min;
    public Align PopupAlignX = Align.Min;
    public Align PopupAlignY = Align.Min;
    public EdgeInsets Margin = EdgeInsets.Zero;
}

public struct CanvasStyle()
{
    public Color Color = Color.Transparent;
    public Vector2Int ColorOffset = Vector2Int.Zero;
    public Vector2 WorldPosition = Vector2.Zero;
    public Vector2 WorldSize = Vector2.Zero;
    public Camera? WorldCamera = null;

    internal CanvasData ToData() => new()
    {
        Color = Color,
        ColorOffset = ColorOffset,
        WorldPosition = WorldPosition,
        WorldSize = WorldSize
    };
}

public struct TextBoxStyle()
{
    public Size Height = 28f;
    public float FontSize = 16;
    public Color BackgroundColor = Color.Transparent;
    public Color TextColor = Color.White;
    public Color PlaceholderColor = new(0.4f, 0.4f, 0.4f, 1f);
    public Color SelectionColor = new(0.2f, 0.4f, 0.8f, 0.5f);
    public BorderStyle Border = BorderStyle.None;

    public BorderStyle FocusBorder = BorderStyle.None;
    public bool IsPassword = false;

    internal TextBoxData ToData() => new()
    {
        Height = Height,
        FontSize = FontSize,
        BackgroundColor = BackgroundColor,
        TextColor = TextColor,
        PlaceholderColor = PlaceholderColor,
        SelectionColor = SelectionColor,
        Border = Border,

        FocusBorder = FocusBorder,
        Password = IsPassword,
        TextStart = 0,
        TextLength = 0
    };
}

public static class ElementStyle
{
    public static ContainerStyle WithColor(this ContainerStyle style, Color color)
    {
        style.Color = color;
        return style;
    }

    public static ContainerStyle WithSize(this ContainerStyle style, float width=0, float height=0)
    {
        style.Width = width;
        style.Height = height;
        return style;
    }

    public static ContainerStyle WithAlign(this ContainerStyle style, Align align)
    {
        style.AlignX = align;
        style.AlignY = align;
        return style;
    }

    public static ContainerStyle WithAlignX(this ContainerStyle style, Align alignX)
    {
        style.AlignX = alignX;
        return style;
    }

    public static ContainerStyle WithAlignY(this ContainerStyle style, Align alignY)
    {
        style.AlignY = alignY;
        return style;
    }

    public static ContainerStyle WithMargin(this ContainerStyle style, in EdgeInsets margin)
        {
        style.Margin = margin;
        return style;
    }

    public static ContainerStyle WithMinSize(this ContainerStyle style, float minWidth=0, float minHeight=0)
    {
        style.MinWidth = minWidth;
        style.MinHeight = minHeight;
        return style;
    }

    public static ContainerStyle WithMaxSize(this ContainerStyle style, float maxWidth=float.MaxValue, float maxHeight=float.MaxValue)
    {
        style.MaxWidth = maxWidth;
        style.MaxHeight = maxHeight;
        return style;
    }

    public static LabelStyle WithColor(this LabelStyle style, Color color)
    {
        style.Color = color;
        return style;
    }
}
