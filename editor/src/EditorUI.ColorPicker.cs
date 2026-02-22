//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ.Editor;

internal static partial class EditorUI
{
    /// <summary>
    /// Returns true on commit (popup closed or swatch clicked).
    /// Calls onPreview each frame the color changes during drag.
    /// </summary>
    public static bool ColorPickerButton(
        int id,
        ref Color32 color,
        Action<Color32>? onPreview = null,
        Color[]? swatches = null,
        int swatchCount = 0,
        Sprite? icon = null)
    {
        return Widget.ColorPickerButton(id, ref color, onPreview, swatches, swatchCount, icon);
    }
}
