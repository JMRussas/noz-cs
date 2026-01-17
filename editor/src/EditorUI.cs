//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ.Editor;

internal static class EditorUI
{
    public static void Shortcut(InputCode code)
    {
        using (UI.BeginContainer(EditorStyle.Shortcut.RootContainer))
            UI.Label(code.ToDisplayString(), style: EditorStyle.Shortcut.TextStyle);
    }
    
    public static void Shortcut(Command command)
    {
        using (UI.BeginRow(EditorStyle.Shortcut.ListContainer))
        {
            if (command.Ctrl) Shortcut(InputCode.KeyLeftCtrl);
            if (command.Alt) Shortcut(InputCode.KeyLeftAlt);
            if (command.Shift) Shortcut(InputCode.KeyLeftShift);
            Shortcut(command.Key);
        }
    }
}
