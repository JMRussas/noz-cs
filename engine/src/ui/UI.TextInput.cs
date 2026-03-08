//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ;

public static partial class UI
{
    public static string TextInput(
        int id,
        string value,
        TextInputStyle style,
        string? placeholder = null,
        IChangeHandler? handler = null)
    {
        var font = style.Font ?? DefaultFont;
        var height = style.Height.IsFixed ? style.Height.Value : style.FontSize * 1.8f;

        ElementTree.BeginTree();

        ref var state = ref ElementTree.BeginWidget<EditableTextState>(id);

        if (style.BorderWidth > 0)
            ElementTree.BeginBorder(style.BorderWidth, style.BorderColor, style.BorderRadius);

        ElementTree.BeginSize(Size.Percent(1), new Size(height));

        if (style.BackgroundColor.A > 0)
            ElementTree.BeginFill(style.BackgroundColor, style.BorderRadius);

        var hasPadding = !style.Padding.IsZero;
        if (hasPadding)
            ElementTree.BeginPadding(style.Padding);

        value = ElementTree.EditableText(
            ref state,
            value,
            font,
            style.FontSize,
            style.TextColor,
            style.TextColor,
            style.SelectionColor,
            placeholder,
            style.PlaceholderColor,
            false,
            false,
            style.Scope);

        ElementTree.EndTree();

        return value;
    }
}
