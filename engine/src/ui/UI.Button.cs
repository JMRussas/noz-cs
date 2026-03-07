//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ;

public struct ButtonStyle()
{
    public Size Width = Size.Fit;
    public Size Height = 30.0f;
    public float MinWidth = 0;
    public Color Color = Color.Transparent;
    public Color ContentColor = Color.White;
    public float FontSize = 12.0f;
    public float IconSize = 16.0f;
    public float Spacing = 6.0f;
    public float BorderRadius = 0;
    public float BorderWidth = 0;
    public Color BorderColor = Color.Transparent;
    public EdgeInsets Padding = EdgeInsets.Zero;
    public Font? Font = null;
    public Func<ButtonStyle, ElementFlags, ButtonStyle>? Resolve;
}

public static partial class UI
{
    public static bool Button(int id, string text, in ButtonStyle style) =>
        Button(id, text, null, style);

    public static bool Button(int id, Sprite icon, in ButtonStyle style) =>
        Button(id, null, icon, style);

    public static bool Button(int id, string? text, Sprite? icon, in ButtonStyle style)
    {
        ElementTree.BeginWidget(id);

        var flags = ElementTree.GetCurrentWidgetFlags();
        var s = style.Resolve != null ? style.Resolve(style, flags) : style;

        ElementTree.BeginSize(new Size2(s.Width, s.Height));

        if (s.BorderWidth > 0)
            ElementTree.BeginBorder(s.BorderWidth, s.BorderColor, s.BorderRadius);

        ElementTree.BeginFill(s.Color, s.BorderRadius);

        var hasBadding = !s.Padding.IsZero;
        if (hasBadding)
            ElementTree.BeginPadding(s.Padding);

        ElementTree.BeginAlign(Align.Center);
        ElementTree.BeginRow(s.Spacing);

        if (icon != null)
        {
            ElementTree.Image(icon, new Size2(s.IconSize, s.IconSize), ImageStretch.Uniform, s.ContentColor, 1.0f,
                new Align2(Align.Min, Align.Center));
        }

        if (text != null)
        {
            var font = s.Font ?? _defaultFont!;
            ElementTree.Label(ElementTree.Text(text), font, s.FontSize, s.ContentColor,
                new Align2(Align.Center, Align.Center), TextOverflow.Overflow);
        }

        ElementTree.EndElement(); // row
        ElementTree.EndElement(); // align

        if (hasBadding)
            ElementTree.EndElement(); // padding

        ElementTree.EndElement(); // fill
        ElementTree.EndElement(); // size

        if (s.BorderWidth > 0)
            ElementTree.EndElement(); // border

        var pressed = ElementTree.WasPressed();
        ElementTree.EndWidget();
        return pressed;
    }
}
