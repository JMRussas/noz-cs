//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ;

public static partial class UI
{
    public static Vector2 MouseWorldPosition { get; private set; }

    private static void HandleInput()
    {
        var mouse = Camera!.ScreenToWorld(Input.MousePosition);
        MouseWorldPosition = mouse;

        // Clear hot focus when clicking outside the hot element
        var mousePressed = Input.WasButtonPressedRaw(InputCode.MouseLeft);
        if (mousePressed && _hotId != 0 && !ElementTree.WasPressed(_hotId))
            ElementTree.ClearFocus();

        // Don't consume mouse buttons when hovering over a Scene element (pass-through),
        // but still consume when popups are open or scrollbar is being used.
        var popupCount = ElementTree.ActivePopupCount;
        if (!ElementTree.MouseOverScene || popupCount > 0)
        {
            if (popupCount > 0 || ElementTree.ScrollbarDragging)
                Input.ConsumeButton(InputCode.MouseLeft);

            if (popupCount > 0)
            {
                Input.ConsumeButton(InputCode.MouseLeftDoubleClick);
                Input.ConsumeButton(InputCode.MouseRight);
            }
        }
    }
}
