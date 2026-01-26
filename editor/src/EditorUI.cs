//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ.Editor;

internal static class EditorUI
{
    private static void ShortcutText(string text, bool selected = false)
    {
        UI.Label(text, style: selected ? EditorStyle.Control.Text : EditorStyle.Shortcut.Text);
    }

    private static void ShortcutText(InputCode code, bool selected = false) =>
        ShortcutText(code.ToDisplayString(), selected: selected);
    
    public static void Shortcut(InputCode code, bool ctrl, bool alt, bool shift, bool selected = false, Align align = Align.Min)
    {
        using (UI.BeginRow(EditorStyle.Shortcut.ListContainer with { AlignX = align}))
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

    public static void Shortcut(Command command, bool selected=false) =>
        Shortcut(command.Key, command.Ctrl, command.Alt, command.Shift, selected);

    public static void ButtonFill(bool selected, bool hovered, bool disabled)
    {
        if (disabled)
            UI.Container(EditorStyle.Button.DisabledFill);
        else if (selected && hovered)
            UI.Container(EditorStyle.Button.SelectedHoverFill);
        else if (selected)
            UI.Container(EditorStyle.Button.SelectedFill);
        else if (hovered)
            UI.Container(EditorStyle.Button.HoverFill);
        else
            UI.Container(EditorStyle.Button.Fill);
    }

    public static bool Button(ElementId id, string text, bool selected = false, bool disabled = false)
    {
        bool pressed = false;
        using (UI.BeginContainer(id, EditorStyle.Button.Root))
        {
            ButtonFill(selected, UI.IsHovered(), disabled);            
            using (UI.BeginContainer(EditorStyle.Button.TextContent))
                UI.Label(text, disabled ? EditorStyle.Button.DisabledText : EditorStyle.Button.Text);
            pressed = !disabled && UI.WasPressed();
        }

        return pressed;
    }

    public static bool Button(ElementId id, Sprite icon, bool selected = false, bool disabled = false)
    {
        bool pressed = false;
        using (UI.BeginContainer(id, EditorStyle.Button.RootWithIcon))
        {
            ButtonFill(selected, UI.IsHovered(), disabled);
            using (UI.BeginContainer(EditorStyle.Button.IconContent))
                UI.Image(icon, disabled ? EditorStyle.Button.DisabledIcon : EditorStyle.Button.Icon);
            pressed = UI.WasPressed();
        }

        return pressed;
    }

    public static bool Button(ElementId id, Action content, bool selected = false, bool disabled = false)
    {
        bool pressed = false;
        using (UI.BeginContainer(id, EditorStyle.Button.RootWithContent))
        {
            ButtonFill(selected, UI.IsHovered(), disabled);
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
}
