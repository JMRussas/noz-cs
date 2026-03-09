//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ;

public struct BorderStyle
{
    public BorderRadius Radius;
    public float Width;
    public Color Color;

    public static readonly BorderStyle None = new() { Radius = BorderRadius.Zero, Width = 0, Color = Color.Transparent };
}

public struct ContainerStyle()
{
    public Size2 Size = new(NoZ.Size.Default, NoZ.Size.Default);
    public float MinWidth = 0;
    public float MinHeight = 0;
    public float MaxWidth = float.MaxValue;
    public float MaxHeight = float.MaxValue;
    public Align2 Align = NoZ.Align.Min;
    public EdgeInsets Margin = EdgeInsets.Zero;
    public EdgeInsets Padding = EdgeInsets.Zero;
    public Color Color = NoZ.Color.Transparent;
    public BorderRadius BorderRadius = BorderRadius.Zero;
    public float BorderWidth;
    public Color BorderColor = NoZ.Color.Transparent;
    public float Spacing = 0;
    public bool Clip = false;
    public ushort Order = 0;
    public Func<ContainerStyle, WidgetFlags, ContainerStyle>? Resolve;

    public BorderStyle Border
    {
        readonly get => new() { Radius = BorderRadius, Width = BorderWidth, Color = BorderColor };
        set { BorderRadius = value.Radius; BorderWidth = value.Width; BorderColor = value.Color; }
    }

    public Size Width { readonly get => Size.Width; set => Size.Width = value; }
    public Size Height { readonly get => Size.Height; set => Size.Height = value; }
    public Align AlignX { readonly get => Align.X; set => Align.X = value; }
    public Align AlignY { readonly get => Align.Y; set => Align.Y = value; }

    public static readonly ContainerStyle Default = new();
    public static readonly ContainerStyle Fit = new() { Size = Size2.Fit };
    public static readonly ContainerStyle Center = new() { Size = Size2.Fit, Align = NoZ.Align.Center };
}

public enum TextOverflow : byte
{
    Overflow,
    Ellipsis,
    Scale,
    Wrap,
}

public struct LabelStyle()
{
    public float FontSize = 16;
    public Color Color = NoZ.Color.White;
    public Align2 Align = new(NoZ.Align.Min, NoZ.Align.Center);
    public Font? Font = null;
    public ushort Order = 2;
    public TextOverflow Overflow = TextOverflow.Overflow;
    public Func<LabelStyle, WidgetFlags, LabelStyle>? Resolve;

    public Align AlignX { readonly get => Align.X; set => Align.X = value; }
    public Align AlignY { readonly get => Align.Y; set => Align.Y = value; }

    public static readonly LabelStyle Default = new();
    public static readonly LabelStyle Centered = new() { Align = NoZ.Align.Center };
}

public struct ImageStyle()
{
    public Size2 Size = Size2.Default;
    public ImageStretch Stretch = ImageStretch.Uniform;
    public Align2 Align = NoZ.Align.Min;
    public float Scale = 1.0f;
    public Color Color = NoZ.Color.White;
    public BorderRadius BorderRadius = BorderRadius.Zero;
    public ushort Order = 1;
    public Func<ImageStyle, WidgetFlags, ImageStyle>? Resolve;

    public Size Width { readonly get => Size.Width; set => Size.Width = value; }
    public Size Height { readonly get => Size.Height; set => Size.Height = value; }
    public Align AlignX { readonly get => Align.X; set => Align.X = value; }
    public Align AlignY { readonly get => Align.Y; set => Align.Y = value; }

    public static readonly ImageStyle Default = new();
    public static readonly ImageStyle Center = new() { Align = NoZ.Align.Center };
    public static readonly ImageStyle Fill = new() { Stretch = ImageStretch.Fill };
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

public struct GridStyle()
{
    public float Spacing = 0;
    public int Columns = 3;
    public float CellWidth = 100;
    public float CellHeight = 100;
    public float CellMinWidth = 0;
    public float CellHeightOffset = 0;
    public int VirtualCount = 0;
    public int StartIndex = 0;
}

public struct PopupStyle()
{
    public Align2 Anchor = Align.Min;
    public Align2 PopupAlign = Align.Min;
    public float Spacing = 0;
    public bool ClampToScreen = false;
    public Rect AnchorRect = Rect.Zero;
    public float MinWidth = 0;
    public bool AutoClose = true;
    public bool Interactive = true;
    public bool ShowChecked = false;
    public bool ShowIcons = false;   

    public Align AnchorX { readonly get => Anchor.X; set => Anchor.X = value; }
    public Align AnchorY { readonly get => Anchor.Y; set => Anchor.Y = value; }
    public Align PopupAlignX { readonly get => PopupAlign.X; set => PopupAlign.X = value; }
    public Align PopupAlignY { readonly get => PopupAlign.Y; set => PopupAlign.Y = value; }
}

public enum PlaceholderMode
{
    Inline,
    FloatingLabel,
}

public struct TextInputStyle()
{
    public Size Width = Size.Percent(1);
    public Size Height = Size.Default;
    public float FontSize = 16;
    public Font? Font = null;
    public Color BackgroundColor = Color.Transparent;
    public Color TextColor = Color.White;
    public Color PlaceholderColor = new(0.4f, 0.4f, 0.4f, 1f);
    public Color SelectionColor = new(0.2f, 0.4f, 0.8f, 0.5f);
    public BorderRadius BorderRadius = BorderRadius.Zero;
    public float BorderWidth = 0;
    public Color BorderColor = Color.Transparent;
    public EdgeInsets Padding = EdgeInsets.Zero;
    public bool IsPassword = false;
    public InputScope Scope = InputScope.All;
    public bool MultiLine = false;
    public PlaceholderMode PlaceholderMode = PlaceholderMode.Inline;
    public float LabelFontSize = 9;
    public Color LabelColor = new(0.47f, 0.47f, 0.47f, 1f);
    public float IconSize = 14;
    public Color IconColor = new(0.6f, 0.6f, 0.6f, 1f);

    public Func<TextInputStyle, WidgetFlags, TextInputStyle>? Resolve;
}

public struct TextAreaStyle()
{
    public Size Height = Size.Default;
    public float FontSize = 16;
    public Font? Font = null;
    public Color BackgroundColor = Color.Transparent;
    public Color TextColor = Color.White;
    public Color PlaceholderColor = new(0.4f, 0.4f, 0.4f, 1f);
    public Color SelectionColor = new(0.2f, 0.4f, 0.8f, 0.5f);
    public BorderRadius BorderRadius = BorderRadius.Zero;
    public float BorderWidth = 0;
    public Color BorderColor = Color.Transparent;
    public BorderRadius FocusBorderRadius = BorderRadius.Zero;
    public float FocusBorderWidth = 0;
    public Color FocusBorderColor = Color.Transparent;
    public EdgeInsets Padding = EdgeInsets.Zero;
    public InputScope Scope = InputScope.All;
    public bool CommitOnEnter = false;

    public BorderStyle Border
    {
        readonly get => new() { Radius = BorderRadius, Width = BorderWidth, Color = BorderColor };
        set { BorderRadius = value.Radius; BorderWidth = value.Width; BorderColor = value.Color; }
    }

    public BorderStyle FocusBorder
    {
        readonly get => new() { Radius = FocusBorderRadius, Width = FocusBorderWidth, Color = FocusBorderColor };
        set { FocusBorderRadius = value.Radius; FocusBorderWidth = value.Width; FocusBorderColor = value.Color; }
    }

}

public struct ScrollableStyle()
{
    public float ScrollSpeed = 30f;
}

public struct SceneStyle()
{
    public Size2 Size = Size2.Default;
    public Color Color = Color.Transparent;
    public int SampleCount = 1;
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
        style.Align = align;
        return style;
    }

    public static ContainerStyle WithAlignX(this ContainerStyle style, Align alignX)
    {
        style.Align.X = alignX;
        return style;
    }

    public static ContainerStyle WithAlignY(this ContainerStyle style, Align alignY)
    {
        style.Align.Y = alignY;
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
