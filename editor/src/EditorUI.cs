//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ.Editor;

internal static class EditorUI
{
    public static void Shortcut(InputCode code)
    {
        UI.BeginContainer(new ContainerStyle
        {
            MinWidth = EditorStyle.Shortcut.Size,
            Height = EditorStyle.Shortcut.Size,
            Align = Align.Center,
            Padding = EdgeInsets.LeftRight(4),
            Color = EditorStyle.Overlay.AccentTextColor,
            Border = new BorderStyle { Radius = EditorStyle.Shortcut.BorderRadius }
        });
        UI.Label(code.ToDisplayString(),  new LabelStyle
        {
            FontSize = EditorStyle.Shortcut.TextSize,
            Color = EditorStyle.Overlay.TextColor,
            Align = Align.Center
        });
        UI.EndContainer();
    }
    
    public static void Shortcut(Command command)
    {
        UI.BeginContainer(new ContainerStyle{Align=Align.CenterRight});
        UI.BeginRow(new ContainerStyle { Spacing = 4 });
        if (command.Ctrl) Shortcut(InputCode.KeyLeftCtrl);
        if (command.Alt) Shortcut(InputCode.KeyLeftAlt);
        if (command.Shift) Shortcut(InputCode.KeyLeftShift);
        Shortcut(command.Key);
        UI.EndRow();
        UI.EndContainer();
    }
}
