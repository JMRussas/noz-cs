//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//
// TODO: Migrate popup system to UI.PopupMenu and delete this file

using System.Numerics;

namespace NoZ.Editor;

internal static partial class EditorUI
{
    private static partial class ElementId
    {
        public static partial WidgetId Popup { get; }
        public static partial WidgetId PopupItem { get; }
    }

    private static bool _controlHovered = false;
    private static bool _controlSelected = false;
    private static bool _controlDisabled = false;
    private static WidgetId _popupId;
    private static WidgetId _nextPopupItemId;

    private static void SetState(bool selected, bool disabled)
    {
        _controlHovered = UI.IsHovered();
        _controlSelected = selected;
        _controlDisabled = disabled;
    }

    private static void ClearState()
    {
        _controlDisabled = false;
        _controlHovered = false;
        _controlSelected = false;
    }

    // --- Popup System ---

    public static void OpenPopup(WidgetId id) => _popupId = id;

    public static void ClosePopup() => _popupId = WidgetId.None;

    public static void TogglePopup(WidgetId id) =>
        _popupId = _popupId == id ? WidgetId.None : id;

    public static bool IsPopupOpen(WidgetId id) => _popupId == id;

    public static bool Popup(WidgetId id, Action content, PopupStyle? style = null, Vector2 offset = default)
    {
        if (_popupId != id) return false;

        _nextPopupItemId = ElementId.PopupItem;

        var anchorRect = UI.GetElementWorldRect(id).Translate(offset);
        using var _ = UI.BeginPopup(
            ElementId.Popup,
            style ?? new PopupStyle
            {
                AnchorX = Align.Min,
                AnchorY = Align.Min,
                PopupAlignX = Align.Min,
                PopupAlignY = Align.Max,
                Spacing = 2,
                ClampToScreen = true,
                AnchorRect = anchorRect,
                MinWidth = anchorRect.Width
            });

        if (UI.IsClosed())
        {
            _popupId = WidgetId.None;
            return false;
        }

        using var __ = UI.BeginColumn(EditorStyle.Popup.Root);

        content.Invoke();

        return _popupId == id;
    }

    // --- Popup Items ---

    public static bool PopupItem(string text, Action? content = null, bool selected = false, bool disabled = false) =>
        PopupItem(_nextPopupItemId++, text, content, selected, disabled);

    public static bool PopupItem(
        WidgetId id,
        string text,
        Action? content = null,
        bool selected = false,
        bool disabled = false) =>
        PopupItem(id, null, text, content, selected, disabled, showIcon: false);

    public static bool PopupItem(
        Sprite? icon,
        string text,
        Action? content = null,
        bool selected = false,
        bool disabled = false,
        bool showIcon = true) =>
        PopupItem(_nextPopupItemId++, icon, text, content, selected, disabled, showIcon);

    public static bool PopupItem(
        WidgetId id,
        Sprite? icon,
        string? text,
        Action? content = null,
        bool selected = false,
        bool disabled = false,
        bool showIcon = true,
        bool showChecked = true)
    {
        var pressed = false;
        using (UI.BeginContainer(id, EditorStyle.Popup.Item))
        {
            SetState(selected, disabled);

            if (_controlHovered)
                UI.Container(EditorStyle.Control.HoverFill);

            using (UI.BeginRow(EditorStyle.Popup.ItemContent))
            {
                if (showChecked)
                    ControlIcon(EditorStyle.Popup.CheckContent, selected ? EditorAssets.Sprites.IconCheck : null);
                if (showIcon)
                    ControlIcon(icon);
                if (text != null)
                    ControlText(text);

                content?.Invoke();
            }

            pressed = UI.WasPressed();
        }

        ClearState();

        return pressed;
    }

    // --- Control helpers (used by popup items and VFX CurveTypeButton) ---

    public static bool Control(
        WidgetId id,
        Action content,
        bool selected = false,
        bool disabled = false,
        bool toolbar = false,
        bool padding = true)
    {
        var pressed = false;
        var hovered = UI.IsHovered(id);
        using (UI.BeginContainer(id, hovered ? EditorStyle.Control.RootHovered : EditorStyle.Control.Root))
        {
            SetState(selected, disabled);

            using (UI.BeginRow())
                content.Invoke();

            pressed = !disabled && UI.WasPressed();
        }

        ClearState();

        return pressed;
    }

    public static void ControlText(string text)
    {
        UI.Text(
            text,
            style: _controlDisabled
                ? EditorStyle.Control.DisabledText
                : _controlSelected
                    ? EditorStyle.Control.SelectedText
                    : _controlHovered
                        ? EditorStyle.Control.HoveredText
                        : EditorStyle.Control.Text);
    }

    private static void ControlIcon(in ContainerStyle style, Sprite? icon)
    {
        using var _ = UI.BeginContainer(style);
        if (icon == null) return;

        UI.Image(
            icon,
            style: _controlDisabled
                ? EditorStyle.Control.DisabledIcon
                : _controlSelected
                    ? EditorStyle.Control.SelectedIcon
                    : _controlHovered
                        ? EditorStyle.Control.HoveredIcon
                        : EditorStyle.Control.Icon);
    }

    public static void ControlIcon(Sprite? icon) =>
        ControlIcon(EditorStyle.Control.IconContainer, icon);
}
