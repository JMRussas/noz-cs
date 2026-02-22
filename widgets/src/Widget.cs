//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ;

public static partial class Widget
{
    [ElementId("Popup")]
    [ElementId("PopupItem", count: 64)]
    [ElementId("Tooltip")]
    private static partial class ElementId { }

    // Sound hooks
    public static Action? OnHover { get; set; }
    public static Action? OnPress { get; set; }

    // Shared widget state
    private static bool _enabled;
    private static bool _checked;
    private static bool _hovered;

    // Popup state
    private static int _popupId = -1;
    private static InputScope _popupScope;
    private static int _nextPopupItemId = -1;

    // Sprites (loaded by Init)
    public static Sprite? IconCheck { get; private set; }
    public static Sprite? IconClose { get; private set; }
    public static Sprite? IconNofill { get; private set; }
    public static Sprite? IconOpacity { get; private set; }
    public static Sprite? IconOpacityOverlay { get; private set; }

    public static void Init()
    {
        IconCheck = (Sprite?)Asset.Load(AssetType.Sprite, "icon_check");
        IconClose = (Sprite?)Asset.Load(AssetType.Sprite, "icon_close");
        IconNofill = (Sprite?)Asset.Load(AssetType.Sprite, "icon_nofill");
        IconOpacity = (Sprite?)Asset.Load(AssetType.Sprite, "icon_opacity");
        IconOpacityOverlay = (Sprite?)Asset.Load(AssetType.Sprite, "icon_opacity_overlay");
    }

    private static void BeginWidget(int id, bool isChecked, bool enabled)
    {
        _enabled = enabled;
        _checked = isChecked;
        _hovered = enabled && UI.IsHovered();
    }

    private static void EndWidget()
    {
        _enabled = false;
        _checked = false;
        _hovered = false;
    }

    public static void WidgetFill(bool toolbar = false)
    {
        if (!_enabled)
        {
            if (!toolbar)
                UI.Container(WidgetStyle._fillDisabled);
            return;
        }

        if (_checked)
            UI.Container(WidgetStyle._fillChecked);
        else if (_hovered)
            UI.Container(WidgetStyle._fillHovered);
        else if (!toolbar)
            UI.Container(WidgetStyle._fill);
    }

    public static void WidgetIcon(Sprite? icon, Align alignX = Align.Min)
    {
        if (icon == null)
        {
            UI.Spacer(WidgetStyle.IconSize);
            return;
        }

        if (!_enabled)
            UI.Image(icon, WidgetStyle._iconDisabled with { AlignX = alignX });
        else if (_checked)
            UI.Image(icon, WidgetStyle._iconChecked with { AlignX = alignX });
        else if (_hovered)
            UI.Image(icon, WidgetStyle._iconHovered with { AlignX = alignX });
        else
            UI.Image(icon, WidgetStyle._icon with { AlignX = alignX });
    }

    public static void WidgetText(string text, Align alignX = Align.Min)
    {
        if (!_enabled)
            UI.Label(text, WidgetStyle._textDisabled with { AlignX = alignX });
        else if (_checked)
            UI.Label(text, WidgetStyle._textChecked with { AlignX = alignX });
        else if (_hovered)
            UI.Label(text, WidgetStyle._textHovered with { AlignX = alignX });
        else
            UI.Label(text, WidgetStyle._text with { AlignX = alignX });
    }

    public static void Separator(float spacing = WidgetStyle.Spacing)
    {
        if (UI.IsRow())
            UI.Container(new ContainerStyle { Width = 1, Color = Color.FromRgb(0x1d1d1d), Margin = EdgeInsets.LeftRight(spacing) });
        else
            UI.Container(new ContainerStyle { Height = 1, Color = Color.FromRgb(0x1d1d1d), Margin = EdgeInsets.TopBottom(spacing) });
    }
}
