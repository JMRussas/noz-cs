//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ.Editor;

internal static class EditorUI
{
    private static bool _controlHovered = false;
    private static bool _controlSelected = false;
    private static bool _controlDisabled = false;

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

    private static void ShortcutText(string text, bool selected = false)
    {
        UI.Label(text, style: selected ? EditorStyle.Control.Text : EditorStyle.Shortcut.Text);
    }

    private static void ShortcutText(InputCode code, bool selected = false) =>
        ShortcutText(code.ToDisplayString(), selected: selected);

    public static void Shortcut(InputCode code, bool ctrl, bool alt, bool shift, bool selected = false, Align align = Align.Min)
    {
        using (UI.BeginRow(EditorStyle.Shortcut.ListContainer with { AlignX = align }))
        {
            if (ctrl)
                ShortcutText(InputCode.KeyLeftCtrl, selected);
            if (alt)
                ShortcutText(InputCode.KeyLeftAlt, selected);
            if (shift)
                ShortcutText(InputCode.KeyLeftShift, selected);
            ShortcutText(code, selected);
        }
    }

    public static void Shortcut(Command command, bool selected = false) =>
        Shortcut(command.Key, command.Ctrl, command.Alt, command.Shift, selected);

    public static void ButtonFill(bool selected, bool hovered, bool disabled, bool toolbar = false)
    {
        if (disabled && toolbar)
            return;
        if (disabled)
            UI.Container(EditorStyle.Button.DisabledFill);
        else if (selected && hovered)
            UI.Container(EditorStyle.Button.SelectedHoverFill);
        else if (selected)
            UI.Container(EditorStyle.Button.SelectedFill);
        else if (hovered)
            UI.Container(EditorStyle.Button.HoverFill);
        else if (!toolbar)
            UI.Container(EditorStyle.Button.Fill);
    }

    public static bool Button(ElementId id, string text, bool selected = false, bool disabled = false, bool toolbar = false)
    {
        bool pressed = false;
        using (UI.BeginContainer(id, EditorStyle.Button.Root))
        {
            ButtonFill(selected, UI.IsHovered(), disabled, toolbar: toolbar);
            using (UI.BeginContainer(EditorStyle.Button.TextContent))
                UI.Label(text, disabled ? EditorStyle.Control.DisabledText : EditorStyle.Control.Text);
            pressed = !disabled && UI.WasPressed();
        }

        return pressed;
    }

    private static void ButtonIcon(Sprite icon)
    {
        using var _ = UI.BeginContainer(EditorStyle.Button.IconContent);
        if (_controlDisabled)
            UI.Image(icon, EditorStyle.Control.DisabledIcon);
        else if (_controlSelected)
            UI.Image(icon, EditorStyle.Control.SelectedIcon);
        else if (_controlHovered)
            UI.Image(icon, EditorStyle.Control.HoveredIcon);
        else
            UI.Image(icon, EditorStyle.Control.Icon);

    }

    public static bool Button(ElementId id, Sprite icon, bool selected = false, bool disabled = false, bool toolbar = false)
    {
        bool pressed = false;
        using (UI.BeginContainer(id, EditorStyle.Button.RootWithIcon))
        {
            _controlDisabled = disabled;
            _controlHovered = UI.IsHovered();
            _controlSelected = selected;

            ButtonFill(selected, UI.IsHovered(), disabled, toolbar: toolbar);
            ButtonIcon(icon);
            pressed = UI.WasPressed();
        }

        return pressed;
    }

    public static bool Button(ElementId id, Action content, bool selected = false, bool disabled = false, bool toolbar = false)
    {
        bool pressed = false;
        using (UI.BeginContainer(id, EditorStyle.Button.RootWithContent))
        {
            ButtonFill(selected, UI.IsHovered(), disabled, toolbar: toolbar);
            using (UI.BeginContainer(EditorStyle.Button.Content))
                content.Invoke();
            pressed = UI.WasPressed();
        }

        return pressed;
    }

    public static void PopupItemFill()
    {
        if (_controlSelected && _controlHovered)
            UI.Container(EditorStyle.Button.SelectedHoverFill);
        else if (_controlSelected)
            UI.Container(EditorStyle.Button.SelectedFill);
        else if (_controlHovered)
            UI.Container(EditorStyle.Button.HoverFill);
    }

    public static void PopupIcon(Sprite icon, bool hovered = false, bool selected = false, bool disabled = false)
    {
        using (UI.BeginContainer(EditorStyle.Control.IconContainer))
            UI.Image(
                icon,
                style: disabled
                    ? EditorStyle.Control.DisabledIcon
                    : selected
                        ? EditorStyle.Control.SelectedIcon
                        : hovered
                            ? EditorStyle.Control.HoveredIcon
                            : EditorStyle.Control.Icon);
    }

    public static void PopupText(string text, bool hovered = false, bool selected = false, bool disabled = false)
    {
        UI.Label(
            text,
            style: disabled
                ? EditorStyle.Control.DisabledText
                : selected
                    ? EditorStyle.Control.SelectedText
                    : hovered
                        ? EditorStyle.Control.HoveredText
                        : EditorStyle.Control.Text);
    }

    public static bool PopupItem(ElementId id, string text, Action? content = null, bool selected = false, bool disabled = false) =>
        PopupItem(id, null, text, content, selected, disabled, showIcon: false);

    public static bool PopupItem(ElementId id, Sprite? icon, string text, Action? content = null, bool selected = false, bool disabled = false, bool showIcon=true)
    {
        var pressed = false;
        using (UI.BeginContainer(id, EditorStyle.Popup.Item))
        { 
            SetState(selected, disabled);

            ControlFill(ignoreDefaultFill: true);

            using (UI.BeginRow(EditorStyle.Popup.ItemContent))
            {
                ControlIcon(EditorStyle.Popup.CheckContent, selected ? EditorAssets.Sprites.IconCheck : null);
                if (showIcon) ControlIcon(icon);
                ControlText(text);

                if (content != null)
                {
                    UI.Spacer(EditorStyle.Control.Spacing);
                    UI.Flex();
                    content?.Invoke();
                }
            }

            pressed = UI.WasPressed();
        }

        ClearState();

        return pressed;
    }

    public static void ControlFill(bool ignoreDefaultFill = false)
    {
        if (_controlDisabled)
            UI.Container(EditorStyle.Control.DisabledFill);
        else if (_controlSelected)
            UI.Container(EditorStyle.Control.SelectedFill);
        else if (_controlHovered)
            UI.Container(EditorStyle.Control.HoverFill);
        else if (!ignoreDefaultFill)
            UI.Container(EditorStyle.Control.Fill);
    }

    public static bool Control(ElementId id, Action content, bool selected = false, bool disabled = false, bool toolbar = false)
    {
        bool pressed = false;
        using (UI.BeginContainer(id, EditorStyle.Control.Root))
        {
            SetState(selected, disabled);
            ControlFill(ignoreDefaultFill: toolbar);

            using (UI.BeginContainer(EditorStyle.Control.Content))
                content.Invoke();

            pressed = !disabled && UI.WasPressed();
        }

        ClearState();

        return pressed;
    }

    public static void ControlPlaceholderText(string text)
    {
        UI.Label(
            text,
            style: _controlSelected
                ? EditorStyle.Control.PlaceholderSelectedText
                : _controlHovered
                    ? EditorStyle.Control.PlaceholderHoverText
                    : EditorStyle.Control.PlaceholderText);
    }

    public static void ControlText(string text)
    {
        UI.Label(
            text,
            style: _controlDisabled
                ? EditorStyle.Control.DisabledText
                : _controlSelected
                    ? EditorStyle.Control.SelectedText
                    : _controlHovered
                        ? EditorStyle.Control.HoveredText
                        : EditorStyle.Control.Text);
    }

    private static void ControlIcon (in ContainerStyle style, Sprite? icon)
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

    public static void ToolbarSpacer()
    {
        UI.Container(EditorStyle.Toolbar.Spacer);
    }
}
