//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ;

public static partial class Widget
{
    private const float TooltipDelay = 0.5f;

    private static int _tooltipId;
    private static float _tooltipTimer;
    private static bool _tooltipVisible;
    private static int _tooltipSuppressedId;

    public static void ToolTip(string text)
    {
        var id = UI.GetElementId();
        if (id == 0) return;

        if (_popupId >= 0)
        {
            if (_tooltipId != 0)
            {
                _tooltipId = 0;
                _tooltipTimer = 0;
                _tooltipVisible = false;
            }
            return;
        }

        if (UI.IsHovered())
        {
            if (UI.WasPressed())
            {
                _tooltipSuppressedId = id;
                _tooltipId = 0;
                _tooltipTimer = 0;
                _tooltipVisible = false;
                return;
            }

            if (_tooltipSuppressedId == id)
                return;

            if (_tooltipId != id)
            {
                _tooltipId = id;
                _tooltipTimer = 0;
                _tooltipVisible = false;
            }

            _tooltipTimer += Time.DeltaTime;
            if (_tooltipTimer >= TooltipDelay)
                _tooltipVisible = true;
        }
        else if (_tooltipId == id)
        {
            _tooltipId = 0;
            _tooltipTimer = 0;
            _tooltipVisible = false;
            _tooltipSuppressedId = 0;
        }
        else if (_tooltipSuppressedId == id)
        {
            _tooltipSuppressedId = 0;
        }

        if (!_tooltipVisible || _tooltipId != id) return;

        ShowToolTip(text, UI.GetElementWorldRect(id));
    }

    public static void ShowToolTip(string text, Rect anchorRect)
    {
        var showBelow = anchorRect.Y < 30;
        var style = new PopupStyle
        {
            AnchorX = Align.Center,
            AnchorY = showBelow ? Align.Max : Align.Min,
            PopupAlignX = Align.Center,
            PopupAlignY = showBelow ? Align.Min : Align.Max,
            Spacing = 4,
            ClampToScreen = true,
            AnchorRect = anchorRect,
            AutoClose = false,
            Interactive = false
        };

        using (UI.BeginPopup(ElementId.Tooltip, style))
        using (UI.BeginContainer(WidgetStyle.ToolTip._root))
            UI.Label(text, WidgetStyle.ToolTip._text);
    }
}
