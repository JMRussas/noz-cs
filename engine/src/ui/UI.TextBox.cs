//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ;

public static partial class UI
{
    private static int _lastChangedTextId;
    private static string _lastChangedText = "";

    public static bool TextBox(int id, ReadOnlySpan<char> text, TextBoxStyle style,
        ReadOnlySpan<char> placeholder = default, IChangeHandler? handler = null)
    {
        var value = new string(text);
        var font = style.Font ?? DefaultFont;
        var height = style.Height.IsFixed ? style.Height.Value : style.FontSize * 1.8f;

        var changed = ElementTree.EditableText(id, ref value, font, style.FontSize,
            style.TextColor, style.BackgroundColor, style.FocusBorderColor,
            placeholder.IsEmpty ? "" : new string(placeholder), false,
            height, style.BorderWidth, style.BorderColor, style.BorderRadius,
            style.FocusBorderWidth, style.FocusBorderColor, style.FocusBorderRadius,
            style.Padding, style.PlaceholderColor, style.SelectionColor);

        ref var state = ref ElementTree.GetStateByWidgetId<TextBoxState>(id);

        // Hot system integration for IChangeHandler
        if (ElementTree.HasFocusOn(id))
        {
            SetHot(id, text);
            if (state.PrevTextHash != state.TextHash)
                NotifyChanged(state.TextHash);
        }

        if (changed)
        {
            _lastChangedTextId = id;
            _lastChangedText = value;
        }

        SetLastElement(id);
        HandleChange(handler);
        return changed;
    }

    public static bool TextBox(int id, ReadOnlySpan<char> text, TextBoxStyle style,
        ReadOnlySpan<char> placeholder, out ReadOnlySpan<char> result, IChangeHandler? handler = null)
    {
        var changed = TextBox(id, text, style, placeholder, handler);
        result = changed ? _lastChangedText.AsSpan() : text;
        return changed;
    }

    public static string TextBox(int id, string value, TextBoxStyle style,
        string? placeholder = null, IChangeHandler? handler = null)
    {
        var font = style.Font ?? DefaultFont;
        var height = style.Height.IsFixed ? style.Height.Value : style.FontSize * 1.8f;

        var changed = ElementTree.EditableText(id, ref value, font, style.FontSize,
            style.TextColor, style.BackgroundColor, style.FocusBorderColor,
            placeholder ?? "", false,
            height, style.BorderWidth, style.BorderColor, style.BorderRadius,
            style.FocusBorderWidth, style.FocusBorderColor, style.FocusBorderRadius,
            style.Padding, style.PlaceholderColor, style.SelectionColor);

        ref var state = ref ElementTree.GetStateByWidgetId<TextBoxState>(id);

        if (ElementTree.HasFocusOn(id))
        {
            SetHot(id, value);
            if (state.PrevTextHash != state.TextHash)
                NotifyChanged(state.TextHash);
        }

        if (changed)
        {
            _lastChangedTextId = id;
            _lastChangedText = value;
        }

        SetLastElement(id);
        HandleChange(handler);
        return value;
    }
}
