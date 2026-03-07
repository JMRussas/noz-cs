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
        var changed = TextAreaImpl(id, ref value, style, placeholder.IsEmpty ? "" : new string(placeholder));

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

    public static bool TextArea(int id, ReadOnlySpan<char> text, TextAreaStyle style,
        ReadOnlySpan<char> placeholder, out ReadOnlySpan<char> result, IChangeHandler? handler = null)
    {
        var changed = TextArea(id, text, style, placeholder, handler);
        result = changed ? _lastChangedText.AsSpan() : text;
        return changed;
    }

    public static string TextArea(int id, string value, TextAreaStyle style,
        string? placeholder = null, IChangeHandler? handler = null)
    {
        var changed = TextAreaImpl(id, ref value, style, placeholder ?? "");

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

    private static bool TextAreaImpl(int id, ref string value, TextAreaStyle style, string placeholder)
    {
        var font = style.Font ?? DefaultFont;
        var height = style.Height.IsFixed ? style.Height.Value : 100f;
        var focused = ElementTree.HasFocusOn(id);

        ElementTree.BeginWidget(id);

        // Resolve border: always emit for stable layout
        var borderWidth = focused ? style.FocusBorderWidth : style.BorderWidth;
        var borderColor = focused ? style.FocusBorderColor : style.BorderColor;
        var borderRadius = focused ? style.FocusBorderRadius : style.BorderRadius;
        if (borderWidth <= 0)
        {
            borderWidth = style.BorderWidth > 0 ? style.BorderWidth : 1;
            borderColor = Color.Transparent;
        }

        ElementTree.BeginBorder(borderWidth, borderColor, borderRadius);
        ElementTree.BeginSize(Size.Percent(1), new Size(height));
        ElementTree.BeginFill(style.BackgroundColor, borderRadius);

        var hasPadding = !style.Padding.IsZero;
        if (hasPadding)
            ElementTree.BeginPadding(style.Padding);

        var changed = ElementTree.EditableText(id, ref value, font, style.FontSize,
            style.TextColor, style.TextColor, style.SelectionColor,
            placeholder, style.PlaceholderColor,
            true, style.CommitOnEnter, style.Scope);

        if (hasPadding)
            ElementTree.EndPadding();

        ElementTree.EndFill();
        ElementTree.EndSize();
        ElementTree.EndBorder();

        ElementTree.EndWidget();
        return changed;
    }
}
