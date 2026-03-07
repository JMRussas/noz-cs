//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ;

public static partial class UI
{
    public static bool TextArea(int id, ReadOnlySpan<char> text, TextAreaStyle style,
        ReadOnlySpan<char> placeholder = default, IChangeHandler? handler = null)
    {
        var value = new string(text);
        var font = style.Font ?? DefaultFont;
        var height = style.Height.IsFixed ? style.Height.Value : 100f;

        var changed = ElementTree.EditableText(id, ref value, font, style.FontSize,
            style.TextColor, style.BackgroundColor, style.FocusBorderColor,
            placeholder.IsEmpty ? "" : new string(placeholder), true,
            height, style.BorderWidth, style.BorderColor, style.BorderRadius,
            style.FocusBorderWidth, style.FocusBorderColor, style.FocusBorderRadius,
            style.Padding, style.PlaceholderColor, style.SelectionColor,
            style.CommitOnEnter);

        ref var state = ref ElementTree.GetStateByWidgetId<TextBoxState>(id);

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

    public static string TextArea(int id, string value, TextAreaStyle style,
        string? placeholder = null, IChangeHandler? handler = null)
    {
        var font = style.Font ?? DefaultFont;
        var height = style.Height.IsFixed ? style.Height.Value : 100f;

        var changed = ElementTree.EditableText(id, ref value, font, style.FontSize,
            style.TextColor, style.BackgroundColor, style.FocusBorderColor,
            placeholder ?? "", true,
            height, style.BorderWidth, style.BorderColor, style.BorderRadius,
            style.FocusBorderWidth, style.FocusBorderColor, style.FocusBorderRadius,
            style.Padding, style.PlaceholderColor, style.SelectionColor,
            style.CommitOnEnter);

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
