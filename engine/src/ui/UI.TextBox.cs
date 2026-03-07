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
        var changed = TextBoxImpl(id, ref value, style, placeholder.IsEmpty ? "" : new string(placeholder));

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
        var changed = TextBoxImpl(id, ref value, style, placeholder ?? "");

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

    private static bool TextBoxImpl(int id, ref string value, TextBoxStyle style, string placeholder)
    {
        var font = style.Font ?? DefaultFont;
        var height = style.Height.IsFixed ? style.Height.Value : style.FontSize * 1.8f;
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
            false, false, style.Scope);

        if (hasPadding)
            ElementTree.EndPadding();

        ElementTree.EndFill();
        ElementTree.EndSize();
        ElementTree.EndBorder();

        ElementTree.EndWidget();
        return changed;
    }
}
