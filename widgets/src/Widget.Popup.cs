//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ;

public static partial class Widget
{
    public static void OpenPopup(int id)
    {
        if (_popupId >= 0)
            Input.PopScope(_popupScope);
        _popupId = id;
        _popupScope = Input.PushScope();
    }

    public static void ClosePopup()
    {
        if (_popupId >= 0)
            Input.PopScope(_popupScope);
        _popupId = -1;
    }

    public static void TogglePopup(int id)
    {
        if (_popupId == id)
            ClosePopup();
        else
        {
            if (_popupId >= 0)
                Input.PopScope(_popupScope);
            _popupId = id;
            _popupScope = Input.PushScope();
        }
    }

    public static bool IsPopupOpen(int id) =>
        _popupId == id;

    public static bool Popup(int id, Action content, PopupStyle? style = null, Vector2 offset = default)
    {
        if (_popupId != id) return false;

        _nextPopupItemId = ElementId.PopupItem;

        var anchorRect = UI.GetElementWorldRect(id).Translate(offset);
        var popupStyle = style ?? new PopupStyle
        {
            AnchorX = Align.Min,
            AnchorY = Align.Min,
            PopupAlignX = Align.Min,
            PopupAlignY = Align.Max,
            Spacing = 2,
            ClampToScreen = true,
            AnchorRect = anchorRect,
            MinWidth = anchorRect.Width
        };

        if (style != null)
        {
            popupStyle.AnchorRect = anchorRect;
            popupStyle.MinWidth = anchorRect.Width;
        }

        using var _ = UI.BeginPopup(ElementId.Popup, popupStyle);
        if (UI.IsClosed())
        {
            Input.PopScope(_popupScope);
            _popupId = -1;
            return false;
        }

        using var __ = UI.BeginColumn(WidgetStyle.Popup._root);

        content.Invoke();

        return _popupId == id;
    }

    public static void PopupText(string text, bool hovered = false, bool selected = false, bool disabled = false)
    {
        UI.Label(
            text,
            style: disabled
                ? WidgetStyle._textDisabled
                : selected
                    ? WidgetStyle._textChecked
                    : hovered
                        ? WidgetStyle._textHovered
                        : WidgetStyle._text);
    }

    private static void PopupItemFill()
    {
        if (_hovered)
            UI.Container(WidgetStyle._fillHovered);
    }

    public static bool PopupItem(
        Action? content = null,
        bool selected = false,
        bool enabled = true,
        bool showIcon = false,
        bool showChecked = true) =>
        PopupItem(_nextPopupItemId++, null, null, content, selected, enabled, showIcon: showIcon, showChecked: showChecked);

    public static bool PopupItem(
        string text,
        Action? content = null,
        bool selected = false,
        bool enabled = true,
        bool showChecked = true) =>
        PopupItem(_nextPopupItemId++, text, content, selected, enabled, showChecked: showChecked);

    public static bool PopupItem(
        int id,
        string text,
        Action? content = null,
        bool selected = false,
        bool enabled = true,
        bool showChecked = true) =>
        PopupItem(id, null, text, content, selected, enabled, showIcon: false, showChecked: showChecked);

    public static bool PopupItem(
        Sprite? icon,
        string text,
        Action? content = null,
        bool selected = false,
        bool enabled = true,
        bool showIcon = true,
        bool showChecked = true) =>
        PopupItem(_nextPopupItemId++, icon, text, content, selected, enabled, showIcon, showChecked: showChecked);

    public static bool PopupItem(
        int id,
        Sprite? icon,
        string? text,
        Action? content = null,
        bool selected = false,
        bool enabled = true,
        bool showIcon = true,
        bool showChecked = true)
    {
        var pressed = false;
        using (UI.BeginContainer(id, WidgetStyle.Popup._item))
        {
            BeginWidget(id, selected, enabled);

            if (enabled && OnHover != null && UI.IsHovered() && UI.HoverChanged())
                OnHover();

            PopupItemFill();

            using (UI.BeginRow(WidgetStyle.Popup._itemContent with { Spacing = WidgetStyle.Spacing }))
            {
                if (showChecked)
                    WidgetIcon(selected ? IconCheck : null);
                if (showIcon)
                {
                    WidgetIcon(icon);
                    UI.Spacer(WidgetStyle.Spacing * 0.5f);
                }
                if (text != null)
                    WidgetText(text);

                content?.Invoke();
            }

            pressed = enabled && UI.WasPressed();
            EndWidget();
        }

        return pressed;
    }
}
