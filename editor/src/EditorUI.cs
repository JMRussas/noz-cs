//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ.Editor;

internal static class EditorUI
{
    private static bool _controlHovered = false;
    private static bool _controlSelected = false;
    private static bool _controlDisabled = false;

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
                UI.Label(text, disabled ? EditorStyle.Button.DisabledText : EditorStyle.Button.Text);
            pressed = !disabled && UI.WasPressed();
        }

        return pressed;
    }

    public static bool Button(ElementId id, Sprite icon, bool selected = false, bool disabled = false, bool toolbar = false)
    {
        bool pressed = false;
        using (UI.BeginContainer(id, EditorStyle.Button.RootWithIcon))
        {
            ButtonFill(selected, UI.IsHovered(), disabled, toolbar: toolbar);
            using (UI.BeginContainer(EditorStyle.Button.IconContent))
                UI.Image(
                    icon,
                    disabled
                        ? EditorStyle.Button.DisabledIcon
                        : (selected ? EditorStyle.Button.SelectedIcon : EditorStyle.Button.Icon));
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

    public static void PopupItemFill(bool selected, bool hovered)
    {
        if (selected && hovered)
            UI.Container(EditorStyle.Button.SelectedHoverFill);
        else if (selected)
            UI.Container(EditorStyle.Button.SelectedFill);
        else if (hovered)
            UI.Container(EditorStyle.Button.HoverFill);
    }

    public static void PopupIcon(Sprite icon, bool hovered = false, bool selected = false, bool disabled = false)
    {
        using (UI.BeginContainer(EditorStyle.Popup.IconContainer))
            UI.Image(
                icon,
                style: disabled
                    ? EditorStyle.Popup.DisabledIcon
                    : selected
                        ? EditorStyle.Popup.SelectedIcon
                        : hovered
                            ? EditorStyle.Popup.HoveredIcon
                            : EditorStyle.Popup.Icon);
    }

    public static void PopupText(string text, bool hovered = false, bool selected = false, bool disabled = false)
    {
        UI.Label(
            text,
            style: disabled
                ? EditorStyle.Popup.DisabledText
                : selected
                    ? EditorStyle.Popup.SelectedText
                    : hovered
                        ? EditorStyle.Popup.HoveredText
                        : EditorStyle.Popup.Text);
    }

    public static void ControlFill(bool selected, bool hovered, bool disabled, bool toolbar = false)
    {
        if (disabled)
            UI.Container(EditorStyle.Control.DisabledFill);
        else if (selected && hovered)
            UI.Container(EditorStyle.Control.SelectedHoverFill);
        else if (selected)
            UI.Container(EditorStyle.Control.SelectedFill);
        else if (hovered)
            UI.Container(EditorStyle.Control.HoverFill);
        else
            UI.Container(EditorStyle.Control.Fill);
    }

    public static bool Control(ElementId id, Action content, bool selected = false, bool disabled = false, bool toolbar = false)
    {
        bool pressed = false;
        using (UI.BeginContainer(id, EditorStyle.Control.Root))
        {
            _controlHovered = UI.IsHovered();
            _controlSelected = selected;
            _controlDisabled = disabled;

            ControlFill(_controlSelected, hovered: _controlHovered, _controlDisabled, toolbar: toolbar);

            using (UI.BeginContainer(EditorStyle.Control.Content))
                content.Invoke();

            pressed = !disabled && UI.WasPressed();
        }
        return pressed;
    }

    public static void ControlText(string text)
    {
        UI.Label(
            text,
            style: _controlDisabled
                ? EditorStyle.Popup.DisabledText
                : _controlSelected
                    ? EditorStyle.Popup.SelectedText
                    : _controlHovered
                        ? EditorStyle.Popup.HoveredText
                        : EditorStyle.Popup.Text);
    }

    public static void ControlIcon(Sprite icon)
    {
        using (UI.BeginContainer(EditorStyle.Popup.IconContainer))
            UI.Image(
                icon,
                style: _controlDisabled
                    ? EditorStyle.Popup.DisabledIcon
                    : _controlSelected
                        ? EditorStyle.Popup.SelectedIcon
                        : _controlHovered
                            ? EditorStyle.Popup.HoveredIcon
                            : EditorStyle.Popup.Icon);
    }
}