//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ;

public struct DropDownStyle()
{
    public Size Width = Size.Fit;
    public Size Height = Style.Widget.Height;
    public Color Color = Style.Palette.Background;
    public Color ContentColor = Style.Palette.Content;
    public Color IconColor = Color.Transparent;
    public float FontSize = Style.Widget.FontSize;
    public float IconSize = Style.Widget.IconSize;
    public float ArrowSize = 12.0f;
    public float Spacing = Style.Widget.Spacing;
    public float BorderRadius = Style.Widget.BorderRadius;
    public float BorderWidth = 0;
    public Color BorderColor = Style.Palette.Border;
    public EdgeInsets Padding = EdgeInsets.Zero;
    public Font? Font = null;
    public Sprite? ArrowIcon = null;
    public Func<DropDownStyle, WidgetFlags, DropDownStyle>? Resolve;

    public static readonly DropDownStyle Default = new();
}

public static partial class UI
{
    public static bool DropDown(
        WidgetId id,
        PopupMenuItem[] items,
        string? text = null,
        Sprite? icon = null,
        DropDownStyle? style = null,
        PopupMenuStyle? menuStyle = null)
    {
        var s = style ?? Style.DropDown;
        var ms = menuStyle ?? Style.PopupMenu;
        var isOpen = IsPopupMenuOpen(id);

        ElementTree.BeginTree();
        ElementTree.BeginWidget(id);

        var flags = ElementTree.GetWidgetFlags();
        if (isOpen)
            flags |= WidgetFlags.Checked;

        if (s.Resolve != null)
            s = s.Resolve(s, flags);

        ElementTree.BeginSize(new Size2(s.Width, s.Height));

        if (s.BorderWidth > 0)
            ElementTree.BeginBorder(s.BorderWidth, s.BorderColor, s.BorderRadius);

        ElementTree.BeginFill(s.Color, s.BorderRadius);

        if (!s.Padding.IsZero)
            ElementTree.BeginPadding(s.Padding);

        var iconColor = s.IconColor.A > 0 ? s.IconColor : s.ContentColor;

        ElementTree.BeginRow(s.Spacing);

        if (icon != null)
        {
            ElementTree.Image(
                icon,
                s.IconSize,
                ImageStretch.Uniform,
                iconColor,
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
                new Align2(Align.Min, Align.Center),
                TextOverflow.Ellipsis);
        }

        ElementTree.Flex();

        if (s.ArrowIcon != null)
        {
            ElementTree.Image(
                s.ArrowIcon,
                s.ArrowSize,
                ImageStretch.Uniform,
                iconColor,
                1.0f,
                new Align2(Align.Center, Align.Center));
        }

        ElementTree.EndTree();

        if (flags.HasFlag(WidgetFlags.Pressed))
        {
            if (isOpen)
            {
                ClosePopupMenu();
            }
            else
            {
                var anchorRect = GetElementWorldRect(id);
                var popupStyle = new PopupStyle
                {
                    AnchorX = Align.Min,
                    AnchorY = Align.Max,
                    PopupAlignX = Align.Min,
                    PopupAlignY = Align.Min,
                    Spacing = 2.0f,
                    ClampToScreen = true,
                    AnchorRect = anchorRect,
                    MinWidth = anchorRect.Width,
                };
                OpenPopupMenu(id, items, ms, popupStyle);
            }
        }

        return flags.HasFlag(WidgetFlags.Pressed);
    }
}
