//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ;

public static partial class UI
{
    public static string TextInput(
        WidgetId id,
        string value,
        TextInputStyle style,
        string? placeholder = null,
        IChangeHandler? handler = null,
        Sprite? icon = null)
    {
        ElementTree.BeginTree();

        ref var state = ref ElementTree.BeginWidget<EditableTextState>(id);

        // Auto-focus when SetHot was called externally (e.g. RenameTool.Begin)
        if (ElementTree._prevHotId == id && state.Focused == 0 && state.FocusExited == 0)
        {
            state.Focused = 1;
            state.JustFocused = 1;
            state.EditText = ElementTree.AllocString(value.AsSpan());
            state.PrevTextHash = string.GetHashCode(value.AsSpan());
            state.TextHash = state.PrevTextHash;
            state.SelectionStart = 0;
            state.CursorIndex = value.Length;
        }

        // Use _prevHotId to resolve style — SetHot happens after defocus check below
        var wasHot = ElementTree._prevHotId == id;
        var flags = ElementTree.GetWidgetFlags();
        if (wasHot)
            flags |= WidgetFlags.Hot;
        var s = style.Resolve != null ? style.Resolve(style, flags) : style;

        var font = s.Font ?? DefaultFont;
        var floatingLabel = s.PlaceholderMode == PlaceholderMode.FloatingLabel && placeholder != null;
        var height = s.Height.IsFixed ? s.Height.Value : s.FontSize * 1.8f;

        if (!floatingLabel)
            ElementTree.BeginSize(s.Width, new Size(height));

        if (s.BorderWidth > 0)
            ElementTree.BeginBorder(s.BorderWidth, s.BorderColor, s.BorderRadius);

        if (s.BackgroundColor.A > 0)
            ElementTree.BeginFill(s.BackgroundColor, s.BorderRadius);

        var hasPadding = !s.Padding.IsZero;
        if (hasPadding)
            ElementTree.BeginPadding(s.Padding);

        var hasIcon = icon != null;
        if (hasIcon)
            ElementTree.BeginRow(4);

        if (hasIcon)
        {
            ElementTree.Image(
                icon!,
                s.IconSize,
                ImageStretch.Uniform,
                s.IconColor,
                1.0f,
                new Align2(Align.Min, Align.Center));
        }

        if (floatingLabel)
        {
            ElementTree.BeginColumn(4);
            ElementTree.Text(placeholder, font, s.LabelFontSize, s.LabelColor);
        }

        value = ElementTree.EditableText(
            ref state,
            value,
            font,
            s.FontSize,
            s.TextColor,
            s.TextColor,
            s.SelectionColor,
            floatingLabel ? null : placeholder,
            s.PlaceholderColor,
            s.MultiLine,
            false,
            s.Scope);

        if (floatingLabel)
            ElementTree.EndColumn();

        if (hasIcon)
            ElementTree.EndRow();

        // Set hot AFTER EditableText's defocus check runs
        if (state.Focused != 0)
            SetHot(id);

        SetLastElement(id);
        ElementTree.EndTree();

        return value;
    }

    public static bool NumberInput(
        WidgetId id,
        ref float value,
        in TextInputStyle style,
        float min = float.MinValue,
        float max = float.MaxValue,
        float step = 0,
        string format = "0.##",
        Sprite? icon = null)
    {
        var text = value.ToString(format);
        var result = TextInput(id, text, style, icon: icon);
        if (result == text)
            return false;

        if (!float.TryParse(result, out var parsed))
            return false;

        if (step > 0)
            parsed = MathF.Round(parsed / step) * step;

        parsed = Math.Clamp(parsed, min, max);

        if (parsed == value)
            return false;

        value = parsed;
        return true;
    }

    public static bool NumberInput(
        WidgetId id,
        ref int value,
        in TextInputStyle style,
        int min = int.MinValue,
        int max = int.MaxValue,
        Sprite? icon = null)
    {
        var text = value.ToString();
        var result = TextInput(id, text, style, icon: icon);
        if (result == text)
            return false;

        if (!int.TryParse(result, out var parsed))
            return false;

        parsed = Math.Clamp(parsed, min, max);

        if (parsed == value)
            return false;

        value = parsed;
        return true;
    }
}
