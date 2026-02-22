//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ;

public static partial class Widget
{
    public static bool Tab(int id, Sprite icon, string text, bool isChecked = false, bool isEnabled = true)
    {
        var isPressed = false;
        using (UI.BeginContainer(id, WidgetStyle._root))
        {
            isPressed = isEnabled && UI.WasPressed();
            var isHovered = isEnabled && UI.IsHovered();

            if (isHovered && OnHover != null && UI.HoverChanged())
                OnHover();

            if (isChecked && isHovered)
                UI.Container(WidgetStyle.Tab._fillCheckedHovered);
            else if (isChecked)
                UI.Container(WidgetStyle.Tab._fillChecked);
            else if (isHovered)
                UI.Container(WidgetStyle.Tab._fillHovered);

            if (isChecked)
                UI.Container(WidgetStyle.Tab._underline);

            using (UI.BeginRow(WidgetStyle.Tab._content))
            {
                if (isChecked)
                    UI.Image(icon, WidgetStyle._iconChecked);
                else if (isHovered)
                    UI.Image(icon, WidgetStyle._iconHovered);
                else
                    UI.Image(icon, WidgetStyle._icon);

                if (!isEnabled)
                    UI.Label(text, WidgetStyle._textDisabled);
                else if (isChecked)
                    UI.Label(text, WidgetStyle._textChecked);
                else if (isHovered)
                    UI.Label(text, WidgetStyle._textHovered);
                else
                    UI.Label(text, WidgetStyle._text);
            }
        }

        return isPressed;
    }
}
