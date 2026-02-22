//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ;

public static partial class Widget
{
    public static bool Dialog(Action content, float padding = 0, float minWidth = 100, bool closeButton = true, Vector2 translate = default)
    {
        var close = false;
        _nextPopupItemId = ElementId.PopupItem;
        using (UI.BeginPopup(ElementId.Popup, WidgetStyle.Dialog._popup))
        using (UI.BeginContainer(WidgetStyle.Dialog._darken))
        using (UI.BeginTransformed(new TransformStyle { Translate = translate }))
        using (UI.BeginContainer(WidgetStyle.Dialog._root with { Padding = padding, MinWidth = minWidth }))
        {
            using (UI.BeginColumn(new ContainerStyle { Spacing = WidgetStyle.Spacing }))
                content();

            if (closeButton && IconClose != null)
                using (UI.BeginContainer(_nextPopupItemId++, WidgetStyle.Dialog._closeButton with { Margin = WidgetStyle.Dialog._closeButton.Margin.T - padding }))
                {
                    UI.Image(
                        IconClose,
                        UI.IsHovered()
                            ? WidgetStyle.Dialog._closeButtonIconHovered
                            : WidgetStyle.Dialog._closeButtonIcon);

                    close = UI.WasPressed();
                }
        }

        if (Input.WasButtonPressed(InputCode.KeyEscape, InputScope.All))
            close = true;

        return close;
    }
}
