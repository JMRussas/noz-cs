//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ;

public struct ToggleStyle()
{
    public float Size = 18;
    public float IconSize = 14;
    public float Spacing = 8;
    public float FontSize = Style.Widget.FontSize;
    public float BorderRadius = 3;
    public float BorderWidth = 1;
    public Color Color = Style.Palette.Background;
    public Color CheckedColor = Style.Palette.Primary;
    public Color BorderColor = Style.Palette.Border;
    public Color ContentColor = Style.Palette.Content;
    public Color CheckColor = Color.White;
    public Font? Font = null;
    public Func<ToggleStyle, WidgetFlags, ToggleStyle>? Resolve;
}

public static partial class UI
{
    public static bool Toggle(WidgetId id, string label, bool isChecked, in ToggleStyle style, Sprite? checkIcon = null)
    {
        ElementTree.BeginTree();
        ElementTree.BeginWidget(id);
        SetChecked(isChecked);

        var flags = ElementTree.GetWidgetFlags();
        var s = style.Resolve != null
            ? style.Resolve(style, flags)
            : style;

        ElementTree.BeginRow(s.Spacing);
        ElementTree.BeginAlign(new Align2(Align.Min, Align.Center));

        // Check box
        ElementTree.BeginSize(new Size2(s.Size, s.Size));
        if (s.BorderWidth > 0)
            ElementTree.BeginBorder(s.BorderWidth, s.BorderColor, s.BorderRadius);
        ElementTree.BeginFill(isChecked ? s.CheckedColor : s.Color, s.BorderRadius);

        if (isChecked && checkIcon != null)
        {
            ElementTree.BeginAlign(Align.Center);
            ElementTree.Image(
                checkIcon,
                s.IconSize,
                ImageStretch.Uniform,
                s.CheckColor,
                1.0f,
                Align.Center);
        }

        // End the box elements (fill, border, size) — need to close before label
        ElementTree.EndFill();
        if (s.BorderWidth > 0)
            ElementTree.EndBorder();
        ElementTree.EndSize();

        // Label
        var font = s.Font ?? _defaultFont!;
        ElementTree.Text(
            label,
            font,
            s.FontSize,
            s.ContentColor,
            new Align2(Align.Min, Align.Center),
            TextOverflow.Overflow);

        ElementTree.EndTree();

        return flags.HasFlag(WidgetFlags.Pressed);
    }
}
