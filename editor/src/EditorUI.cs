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
            {
                ShortcutText(InputCode.KeyLeftCtrl, selected);
                ShortcutText("+", selected);
            }
            if (alt)
            {
                ShortcutText(InputCode.KeyLeftAlt, selected);
                ShortcutText("+", selected);
            }
            if (shift)
            {
                ShortcutText(InputCode.KeyLeftShift, selected);
                ShortcutText("+", selected);
            }
            ShortcutText(code, selected);
        }
    }

    public static void Shortcut(Command command, bool selected=false) =>
        Shortcut(command.Key, command.Ctrl, command.Alt, command.Shift, selected);

    public static bool Button(byte id, string text, bool selected = false)
    {
        bool pressed = false;
        using (UI.BeginContainer(EditorStyle.Confirm.Button, id: id))
        {
            UI.Container(UI.IsHovered() ? EditorStyle.Button.HoverFill : EditorStyle.Button.Fill);
            UI.Label(text, EditorStyle.Button.Text);
            pressed = UI.WasPressed();
        }

        return pressed;
    }
}
