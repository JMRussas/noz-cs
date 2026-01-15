//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ.Editor;

public class Shortcut
{
    public required string Name { get; init; }
    public required InputCode Code { get; init; }
    public bool Ctrl { get; init; }
    public bool Alt { get; init; }
    public bool Shift { get; init; }
    public required Action Action { get; init; }

    public static bool Update(Shortcut[] shortcuts)
    {
        var shiftPressed = Input.IsShiftDown();
        var ctrlPressed = Input.IsCtrlDown();
        var altPressed = Input.IsAltDown();

        foreach (var shortcut in shortcuts)
        {
            if (!Input.WasButtonPressed(shortcut.Code)) continue;
            if (shortcut.Ctrl != ctrlPressed) continue;
            if (shortcut.Alt != altPressed) continue;
            if (shortcut.Shift != shiftPressed) continue;
            shortcut.Action();
            return true;
        }

        return false;
    }
}
