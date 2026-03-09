//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ;

public struct ButtonStyle()
{
    public Size Width = Size.Fit;
    public Size Height = Style.Widget.Height;
    public float MinWidth = 0;
    public Color Color = Style.Palette.Background;
    public Color ContentColor = Style.Palette.Content;
    public float FontSize = Style.Widget.FontSize;
    public float IconSize = Style.Widget.IconSize;
    public float Spacing = Style.Widget.Spacing;
    public float BorderRadius = Style.Widget.BorderRadius;
    public float BorderWidth = 0;
    public Color BorderColor = Style.Palette.Border;
    public EdgeInsets Padding = EdgeInsets.Zero;
    public Font? Font = null;
    public Func<ButtonStyle, WidgetFlags, ButtonStyle>? Resolve;
}

public static partial class UI
{
    public static bool Button(WidgetId id, string text, in ButtonStyle style) =>
        Button(id, text, null, style);

    public static bool Button(WidgetId id, Sprite icon, in ButtonStyle style) =>
        Button(id, null, icon, style);

    public static bool Button(WidgetId id, string? text, Sprite? icon, in ButtonStyle style)
    {
        ElementTree.BeginTree();
        ElementTree.BeginWidget(id);

        var flags = ElementTree.GetWidgetFlags();
        var s = style.Resolve != null
            ? style.Resolve(style, flags)
            : style;

        ElementTree.BeginSize(new Size2(s.Width, s.Height));

        if (s.BorderWidth > 0)
            ElementTree.BeginBorder(s.BorderWidth, s.BorderColor, s.BorderRadius);

        ElementTree.BeginFill(s.Color, s.BorderRadius);

        var hasPadding = !s.Padding.IsZero;
        if (hasPadding)
            ElementTree.BeginPadding(s.Padding);

        ElementTree.BeginAlign(Align.Center);
        ElementTree.BeginRow(s.Spacing);

        if (icon != null)
        {
            ElementTree.Image(
                icon,
                s.IconSize,
                ImageStretch.Uniform,
                s.ContentColor,
                1.0f,
                new Align2(Align.Min, Align.Center));
        }

        if (text != null)
        {
            var font = s.Font ?? _defaultFont!;
            ElementTree.Text(
                text,
                font,
                s.FontSize,
                s.ContentColor,
                new Align2(Align.Center, Align.Center),
                TextOverflow.Overflow);
        }

        ElementTree.EndTree();

        return flags.HasFlag(WidgetFlags.Pressed);
    }

    public static bool Button(WidgetId id, Action content, in ButtonStyle style)
    {
        ElementTree.BeginTree();
        ElementTree.BeginWidget(id);

        var flags = ElementTree.GetWidgetFlags();
        var s = style.Resolve != null
            ? style.Resolve(style, flags)
            : style;

        ElementTree.BeginSize(new Size2(s.Width, s.Height));

        if (s.BorderWidth > 0)
            ElementTree.BeginBorder(s.BorderWidth, s.BorderColor, s.BorderRadius);

        ElementTree.BeginFill(s.Color, s.BorderRadius);

        var hasPadding = !s.Padding.IsZero;
        if (hasPadding)
            ElementTree.BeginPadding(s.Padding);

        ElementTree.BeginAlign(Align.Center);
        ElementTree.BeginRow(s.Spacing);

        content();

        ElementTree.EndTree();

        return flags.HasFlag(WidgetFlags.Pressed);
    }
}
