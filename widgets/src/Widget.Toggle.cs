//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ;

public static partial class Widget
{
    public static bool Toggle(int id, bool isChecked, bool enabled = true)
    {
        var pressed = false;
        using (UI.BeginContainer(id, WidgetStyle.Toggle._root))
        {
            pressed = enabled && UI.WasPressed();
            if (pressed)
                OnPress?.Invoke();

            if (!enabled)
                UI.Container(WidgetStyle.Toggle._fillDisabled);
            else if (UI.IsHovered())
                UI.Container(WidgetStyle.Toggle._fillHovered);
            else
                UI.Container(WidgetStyle.Toggle._fill);

            if (isChecked && IconCheck != null)
                UI.Image(IconCheck, WidgetStyle.Toggle._checkIcon);
        }

        return pressed;
    }
}
