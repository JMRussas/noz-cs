//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ;

public static partial class Widget
{
    public static bool Button(
        int id,
        Sprite icon,
        bool selected = false,
        bool enabled = true,
        bool toolbar = false,
        string? tooltip = null)
    {
        var pressed = false;
        using (UI.BeginContainer(id, WidgetStyle._root))
        {
            if (tooltip != null)
                ToolTip(tooltip);

            if (enabled && OnHover != null && UI.HoverChanged() && UI.IsHovered())
                OnHover();

            pressed = enabled && UI.WasPressed();
            if (pressed)
                OnPress?.Invoke();

            BeginWidget(id, isChecked: selected, enabled: enabled);
            WidgetFill(toolbar);
            WidgetIcon(icon, alignX: Align.Center);
            EndWidget();
        }

        return pressed;
    }

    public static bool Button(
        int id,
        string text,
        bool selected = false,
        bool enabled = true,
        float? minWidth = null,
        string? tooltip = null)
    {
        var pressed = false;
        using (UI.BeginContainer(id, WidgetStyle._root with
        {
            MinWidth = minWidth ?? WidgetStyle._root.MinWidth
        }))
        {
            if (tooltip != null)
                ToolTip(tooltip);

            if (enabled && OnHover != null && UI.HoverChanged() && UI.IsHovered())
                OnHover();

            pressed = enabled && UI.WasPressed();
            if (pressed)
                OnPress?.Invoke();

            BeginWidget(id, isChecked: selected, enabled: enabled);
            WidgetFill();
            using (UI.BeginContainer(WidgetStyle.Button._content))
                WidgetText(text, alignX: Align.Center);
            EndWidget();
        }

        return pressed;
    }

    public static bool Button(
        int id,
        Sprite icon,
        string text,
        bool selected = false,
        bool enabled = true,
        float? minWidth = null,
        string? tooltip = null)
    {
        var pressed = false;
        using (UI.BeginContainer(id, WidgetStyle._root with
        {
            MinWidth = minWidth ?? WidgetStyle._root.MinWidth,
            AlignY = Align.Center,
        }))
        {
            if (tooltip != null)
                ToolTip(tooltip);

            if (enabled && OnHover != null && UI.HoverChanged() && UI.IsHovered())
                OnHover();

            pressed = enabled && UI.WasPressed();
            if (pressed)
                OnPress?.Invoke();

            BeginWidget(id, isChecked: selected, enabled: enabled);
            WidgetFill();
            using (UI.BeginRow(WidgetStyle.Button._content))
            {
                WidgetIcon(icon);
                UI.Spacer(0);
                WidgetText(text);
            }
            EndWidget();
        }

        return pressed;
    }

    public static bool Button(int id, Action content, bool selected = false, bool enabled = true, bool toolbar = false)
    {
        var pressed = false;
        using (UI.BeginContainer(id, WidgetStyle._root))
        {
            pressed = enabled && UI.WasPressed();
            if (pressed)
                OnPress?.Invoke();

            BeginWidget(id, isChecked: selected, enabled: enabled);
            WidgetFill(toolbar);
            content();
            EndWidget();
        }
        return pressed;
    }
}
