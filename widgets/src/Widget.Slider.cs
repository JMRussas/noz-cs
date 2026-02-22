//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ;

public static partial class Widget
{
    public static bool Slider(int id, ref float value, float min = 0f, float max = 1f)
    {
        var changed = false;
        var t = max > min ? Math.Clamp((value - min) / (max - min), 0f, 1f) : 0f;
        var rect = UI.GetElementRect(id);
        var trackWidth = rect.Width;

        using (UI.BeginContainer(id, WidgetStyle._root with { MinWidth = 100 }))
        {
            UI.Container(WidgetStyle.Slider._track);

            if (trackWidth > 0)
            {
                var fillWidth = Math.Max(0, trackWidth * t);
                UI.Container(WidgetStyle.Slider._fill with { Width = fillWidth });

                var thumbOffset = Math.Clamp(
                    trackWidth * t - WidgetStyle.Slider.ThumbSize / 2,
                    0,
                    trackWidth - WidgetStyle.Slider.ThumbSize);
                UI.Container(WidgetStyle.Slider._thumb with { Margin = EdgeInsets.Left(thumbOffset) });
            }

            if (UI.IsDown())
            {
                var worldRect = UI.GetElementWorldRect(id);
                if (worldRect.Width > 0)
                {
                    var mouse = UI.MouseWorldPosition;
                    var localX = Math.Clamp((mouse.X - worldRect.X) / worldRect.Width, 0f, 1f);
                    var newValue = min + localX * (max - min);
                    newValue = MathF.Round(newValue * 20f) / 20f;
                    newValue = Math.Clamp(newValue, min, max);

                    if (newValue != value)
                    {
                        value = newValue;
                        changed = true;
                    }
                }
            }
        }

        return changed;
    }
}
