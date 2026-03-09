//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;
using System.Runtime.CompilerServices;

namespace NoZ;

public static unsafe partial class ElementTree
{
    public static string EditableText(
        ref EditableTextState state,
        string value,
        Font font,
        float fontSize,
        Color textColor,
        Color cursorColor,
        Color selectionColor,
        string? placeholder,
        Color placeholderColor,
        bool multiLine = false,
        bool commitOnEnter = false,
        InputScope scope = default)
    {
        ref var e = ref BeginElement(ElementType.EditableText);
        ref var d = ref e.Data.EditableText;

        // Store state pointer for input phase
        d.State = (EditableTextState*)Unsafe.AsPointer(ref state);

        // Handle commit from previous frame's focus exit
        if (state.FocusExited != 0)
        {
            if (state.WasCancelled == 0)
            {
                var finalHash = string.GetHashCode(state.EditText.AsReadOnlySpan());
                if (finalHash != state.PrevTextHash)
                    value = new string(state.EditText.AsReadOnlySpan());
            }
            state.Focused = 0;
            state.FocusExited = 0;
            state.WasCancelled = 0;
        }

        // Detect external defocus: state thinks we're focused but we weren't hot last frame
        var focused = state.Focused != 0;
        if (focused && _prevHotId != _currentWidget)
        {
            // Commit immediately to avoid one-frame flash of old value
            if (state.WasCancelled == 0)
            {
                var finalHash = string.GetHashCode(state.EditText.AsReadOnlySpan());
                if (finalHash != state.PrevTextHash)
                    value = new string(state.EditText.AsReadOnlySpan());
            }
            state.Focused = 0;
            state.WasCancelled = 0;
            focused = false;
        }

        // Populate element for layout and draw
        if (focused)
        {
            // Re-alloc editing buffer from previous frame's (still valid) pool into current pool
            state.EditText = AllocString(state.EditText.AsReadOnlySpan());
            d.Text = state.EditText;
        }
        else
        {
            d.Text = AllocString(value.AsSpan());
        }

        d.FontSize = fontSize;
        d.TextColor = textColor;
        d.CursorColor = cursorColor;
        d.SelectionColor = selectionColor;
        d.PlaceholderColor = placeholderColor;
        d.Placeholder = placeholder != null ? AllocString(placeholder.AsSpan()) : default;
        d.MultiLine = multiLine;
        d.CommitOnEnter = commitOnEnter;
        d.Focused = focused;
        d.Font = AddObject(font);
        d.CursorIndex = state.CursorIndex;
        d.SelectionStart = state.SelectionStart;
        d.Scope = scope;

        EndElement(ElementType.EditableText);

        return value;
    }

    internal static void HandleEditableTextInput(ref Element e)
    {
        ref var d = ref e.Data.EditableText;
        ref var state = ref *d.State;
        var font = (Font)_assets[d.Font]!;
        var focused = state.Focused != 0;

        // Hit test against parent widget rect (covers padding/border area)
        var widgetId = FindParentWidgetId(e.Index);
        ref var widgetEl = ref GetWidget(widgetId);
        Matrix3x2.Invert(widgetEl.Transform, out var widgetInv);
        var widgetMouse = Vector2.Transform(MouseWorldPosition, widgetInv);
        var mouseInside = widgetEl.Rect.Contains(widgetMouse);

        // Local mouse relative to EditableText element (for cursor positioning)
        Matrix3x2.Invert(e.Transform, out var inv);
        var localMouse = Vector2.Transform(MouseWorldPosition, inv);

        // Focus enter
        if (_inputMousePressed && mouseInside && !focused)
        {
            _hotId = widgetId;
            state.Focused = 1;
            state.JustFocused = 1;
            state.EditText = AllocString(d.Text.AsReadOnlySpan());
            state.PrevTextHash = string.GetHashCode(state.EditText.AsReadOnlySpan());
            state.TextHash = state.PrevTextHash;

            // Select all on focus enter — drag will override if mouse moves to a different char
            var clickIndex = HitTestCharIndex(state.EditText.AsReadOnlySpan(), font, d.FontSize,
                d.MultiLine, e.Rect.Width, localMouse.X - e.Rect.X, localMouse.Y - e.Rect.Y);
            state.FocusClickIndex = clickIndex;
            state.SelectionStart = 0;
            state.CursorIndex = state.EditText.AsReadOnlySpan().Length;

            d.Focused = true;
            d.Text = state.EditText;
            d.CursorIndex = state.CursorIndex;
            d.SelectionStart = state.SelectionStart;
            return;
        }

        if (!focused) return;

        // Mouse drag-to-select
        if (_inputMouseDown && mouseInside)
        {
            var charIndex = HitTestCharIndex(state.EditText.AsReadOnlySpan(), font, d.FontSize,
                d.MultiLine, e.Rect.Width, localMouse.X - e.Rect.X, localMouse.Y - e.Rect.Y);

            if (state.JustFocused != 0)
            {
                // Only switch from select-all to drag-selection if mouse moved to a different char
                if (charIndex != state.FocusClickIndex)
                {
                    state.SelectionStart = state.FocusClickIndex;
                    state.CursorIndex = charIndex;
                    state.JustFocused = 0;
                }
                // Otherwise keep select-all
            }
            else
            {
                state.CursorIndex = charIndex;
            }
        }
        else
        {
            state.JustFocused = 0;
        }

        // Keyboard and text input
        HandleTextInput(ref state, font, d.FontSize, d.MultiLine, d.Scope, d.CommitOnEnter);

        // Update element for draw
        d.Text = state.EditText;
        d.CursorIndex = state.CursorIndex;
        d.SelectionStart = state.SelectionStart;
        d.Focused = state.Focused != 0;
    }

    private static WidgetId FindParentWidgetId(int elementIndex)
    {
        var current = elementIndex;
        while (current != 0)
        {
            ref var el = ref GetElement(current);
            if (el.Type == ElementType.Widget)
                return el.Data.Widget.Id;
            current = el.Parent;
        }
        return WidgetId.None;
    }

    private static void HandleTextInput(
        ref EditableTextState state,
        Font font,
        float fontSize,
        bool multiLine,
        InputScope scope,
        bool commitOnEnter)
    {
        var ctrl = Input.IsCtrlDown();
        var shift = Input.IsShiftDown();
        ref var text = ref state.EditText;

        // Escape — cancel
        if (Input.WasButtonPressed(InputCode.KeyEscape, true, scope))
        {
            state.WasCancelled = 1;
            state.FocusExited = 1;
            state.Focused = 0;
            ClearHot();
            return;
        }

        // Tab — commit and defocus
        if (Input.WasButtonPressed(InputCode.KeyTab, true, scope))
        {
            state.FocusExited = 1;
            state.Focused = 0;
            ClearHot();
            return;
        }

        // Enter
        if (Input.WasButtonPressed(InputCode.KeyEnter, true, scope))
        {
            if (multiLine && !commitOnEnter)
            {
                RemoveSelected(ref state);
                text = InsertText(text.AsReadOnlySpan(), state.CursorIndex, "\n");
                state.CursorIndex++;
                state.SelectionStart = state.CursorIndex;
                state.TextHash = string.GetHashCode(text.AsReadOnlySpan());
            }
            else
            {
                state.FocusExited = 1;
                state.Focused = 0;
                ClearHot();
            }
            return;
        }

        // Ctrl+A — select all
        if (ctrl && Input.WasButtonPressed(InputCode.KeyA, true, scope))
        {
            state.SelectionStart = 0;
            state.CursorIndex = text.AsReadOnlySpan().Length;
            return;
        }

        // Ctrl+C — copy
        if (ctrl && Input.WasButtonPressed(InputCode.KeyC, true, scope))
        {
            if (state.CursorIndex != state.SelectionStart)
            {
                var editText = text.AsReadOnlySpan();
                var selStart = Math.Min(state.CursorIndex, state.SelectionStart);
                var selEnd = Math.Max(state.CursorIndex, state.SelectionStart);
                Application.Platform.SetClipboardText(new string(editText[selStart..selEnd]));
            }
            return;
        }

        // Ctrl+X — cut
        if (ctrl && Input.WasButtonPressed(InputCode.KeyX, true, scope))
        {
            if (state.CursorIndex != state.SelectionStart)
            {
                var editText = text.AsReadOnlySpan();
                var selStart = Math.Min(state.CursorIndex, state.SelectionStart);
                var selEnd = Math.Max(state.CursorIndex, state.SelectionStart);
                Application.Platform.SetClipboardText(new string(editText[selStart..selEnd]));
                RemoveSelected(ref state);
                state.TextHash = string.GetHashCode(text.AsReadOnlySpan());
            }
            return;
        }

        // Ctrl+V — paste
        if (ctrl && Input.WasButtonPressed(InputCode.KeyV, true, scope))
        {
            var clipboard = Application.Platform.GetClipboardText();
            if (clipboard != null && clipboard.Length > 0)
            {
                RemoveSelected(ref state);
                text = InsertText(text.AsReadOnlySpan(), state.CursorIndex, clipboard);
                state.CursorIndex += clipboard.Length;
                state.SelectionStart = state.CursorIndex;
                state.TextHash = string.GetHashCode(text.AsReadOnlySpan());
            }
            return;
        }

        // Backspace
        if (Input.WasButtonPressed(InputCode.KeyBackspace, true, scope))
        {
            if (state.CursorIndex != state.SelectionStart)
            {
                RemoveSelected(ref state);
            }
            else if (state.CursorIndex > 0)
            {
                text = RemoveText(text.AsReadOnlySpan(), state.CursorIndex - 1, 1);
                state.CursorIndex--;
                state.SelectionStart = state.CursorIndex;
            }
            state.TextHash = string.GetHashCode(text.AsReadOnlySpan());
            return;
        }

        // Delete
        if (Input.WasButtonPressed(InputCode.KeyDelete, true, scope))
        {
            if (state.CursorIndex != state.SelectionStart)
            {
                RemoveSelected(ref state);
            }
            else if (state.CursorIndex < text.AsReadOnlySpan().Length)
            {
                text = RemoveText(text.AsReadOnlySpan(), state.CursorIndex, 1);
            }
            state.TextHash = string.GetHashCode(text.AsReadOnlySpan());
            return;
        }

        // Left arrow
        if (Input.WasButtonPressed(InputCode.KeyLeft, true, scope))
        {
            if (state.CursorIndex > 0)
                state.CursorIndex--;
            if (!shift)
                state.SelectionStart = state.CursorIndex;
            return;
        }

        // Right arrow
        if (Input.WasButtonPressed(InputCode.KeyRight, true, scope))
        {
            if (state.CursorIndex < text.AsReadOnlySpan().Length)
                state.CursorIndex++;
            if (!shift)
                state.SelectionStart = state.CursorIndex;
            return;
        }

        // Home
        if (Input.WasButtonPressed(InputCode.KeyHome, true, scope))
        {
            state.CursorIndex = 0;
            if (!shift)
                state.SelectionStart = state.CursorIndex;
            return;
        }

        // End
        if (Input.WasButtonPressed(InputCode.KeyEnd, true, scope))
        {
            state.CursorIndex = text.AsReadOnlySpan().Length;
            if (!shift)
                state.SelectionStart = state.CursorIndex;
            return;
        }

        // Text input (typed characters)
        var textInput = Input.GetTextInput(scope);
        if (textInput.Length > 0)
        {
            RemoveSelected(ref state);
            text = InsertText(text.AsReadOnlySpan(), state.CursorIndex, textInput);
            state.CursorIndex += textInput.Length;
            state.SelectionStart = state.CursorIndex;
            state.TextHash = string.GetHashCode(text.AsReadOnlySpan());
        }
    }

    private static void RemoveSelected(ref EditableTextState state)
    {
        if (state.CursorIndex == state.SelectionStart) return;
        var start = Math.Min(state.CursorIndex, state.SelectionStart);
        var length = Math.Abs(state.CursorIndex - state.SelectionStart);
        state.EditText = RemoveText(state.EditText.AsReadOnlySpan(), start, length);
        state.CursorIndex = start;
        state.SelectionStart = start;
    }

    private static float FitEditableTextAxis(ref Element e, int axis)
    {
        ref var d = ref e.Data.EditableText;
        var font = (Font)_assets[d.Font]!;
        var text = d.Text.AsReadOnlySpan();
        var lineHeight = font.LineHeight * d.FontSize;

        if (axis == 1 && d.MultiLine && e.Rect.Width > 0)
            return Math.Max(TextRender.MeasureWrapped(text, font, d.FontSize, e.Rect.Width).Y, lineHeight);

        if (axis == 1)
            return Math.Max(TextRender.Measure(text, font, d.FontSize).Y, lineHeight);

        return TextRender.Measure(text, font, d.FontSize).X;
    }

    private static float LayoutEditableTextAxis(ref Element e, int axis, float available)
    {
        ref var d = ref e.Data.EditableText;
        var font = (Font)_assets[d.Font]!;
        var text = d.Text.AsReadOnlySpan();
        var lineHeight = font.LineHeight * d.FontSize;

        if (available > 0)
            return Math.Max(available, lineHeight);

        if (axis == 1)
            return Math.Max(TextRender.Measure(text, font, d.FontSize).Y, lineHeight);

        return TextRender.Measure(text, font, d.FontSize).X;
    }

    private static void DrawEditableText(ref Element e)
    {
        ref var d = ref e.Data.EditableText;
        ref var t = ref e.Transform;
        var font = (Font)_assets[d.Font]!;
        var text = d.Text.AsReadOnlySpan();
        var fontSize = d.FontSize;

        var focused = d.Focused;
        var lineHeight = font.LineHeight * fontSize;

        // selection (drawn before text so text is visible on top)
        if (focused && d.CursorIndex != d.SelectionStart)
        {
            var selStart = Math.Min(d.CursorIndex, d.SelectionStart);
            var selEnd = Math.Max(d.CursorIndex, d.SelectionStart);

            if (d.MultiLine && e.Rect.Width > 0)
            {
                DrawMultilineSelection(ref e, ref d, font, text, fontSize, lineHeight, selStart, selEnd);
            }
            else
            {
                var x0 = MeasureTextWidth(text[..selStart], font, fontSize);
                var x1 = MeasureTextWidth(text[..selEnd], font, fontSize);
                var selY = e.Rect.Y + (e.Rect.Height - lineHeight) * 0.5f;
                var selRect = new Rect(e.Rect.X + x0, selY, x1 - x0, lineHeight);
                DrawTexturedRect(selRect, t, null, ApplyOpacity(d.SelectionColor));
            }
        }

        // text or placeholder
        var showPlaceholder = text.Length == 0 && !focused && d.Placeholder.Length > 0;
        var displayText = showPlaceholder ? d.Placeholder.AsReadOnlySpan() : text;
        var displayColor = showPlaceholder ? d.PlaceholderColor : d.TextColor;

        if (displayText.Length > 0)
        {
            if (d.MultiLine && e.Rect.Width > 0)
            {
                var transform = Matrix3x2.CreateTranslation(e.Rect.Position) * t;

                using (Graphics.PushState())
                {
                    Graphics.SetColor(ApplyOpacity(displayColor));
                    Graphics.SetTransform(transform);
                    TextRender.DrawWrapped(displayText, font, fontSize, e.Rect.Width,
                        e.Rect.Width, 0f, e.Rect.Height);
                }
            }
            else
            {
                var textOffset = GetTextOffset(displayText, font, fontSize, e.Rect.Size, Align.Min, Align.Center);
                var transform = Matrix3x2.CreateTranslation(e.Rect.Position + textOffset) * t;

                using (Graphics.PushState())
                {
                    Graphics.SetColor(ApplyOpacity(displayColor));
                    Graphics.SetTransform(transform);
                    TextRender.Draw(displayText, font, fontSize);
                }
            }
        }

        if (!focused) return;

        // cursor (only when no selection)
        if (d.CursorIndex == d.SelectionStart && Time.TotalTime % 1.0f < 0.5f)
        {
            float cursorX, cursorY;

            if (d.MultiLine && e.Rect.Width > 0)
            {
                GetCursorPositionWrapped(text, font, fontSize, e.Rect.Width, d.CursorIndex, out cursorX, out cursorY);
            }
            else
            {
                cursorX = MeasureTextWidth(text[..d.CursorIndex], font, fontSize);
                cursorY = (e.Rect.Height - lineHeight) * 0.5f;
            }

            var cursorRect = new Rect(e.Rect.X + cursorX, e.Rect.Y + cursorY, 1.5f, lineHeight);
            DrawTexturedRect(cursorRect, t, null, ApplyOpacity(d.CursorColor));
        }
    }

    private static void DrawMultilineSelection(
        ref Element e,
        ref EditableTextElement d,
        Font font,
        ReadOnlySpan<char> text,
        float fontSize,
        float lineHeight,
        int selStart,
        int selEnd)
    {
        Span<TextRender.CachedLine> lines = stackalloc TextRender.CachedLine[64];
        var lineCount = TextRender.GetWrapLines(text, font, fontSize, e.Rect.Width, 0, lines);
        if (lineCount == 0) return;

        for (var i = 0; i < lineCount; i++)
        {
            var line = lines[i];
            if (line.End <= selStart || line.Start >= selEnd) continue;

            var lineSelStart = Math.Max(selStart, line.Start) - line.Start;
            var lineSelEnd = Math.Min(selEnd, line.End) - line.Start;
            var lineText = text[line.Start..line.End];

            var x0 = MeasureTextWidth(lineText[..lineSelStart], font, fontSize);
            var x1 = MeasureTextWidth(lineText[..lineSelEnd], font, fontSize);
            var y = i * lineHeight;

            var selRect = new Rect(e.Rect.X + x0, e.Rect.Y + y, x1 - x0, lineHeight);
            DrawTexturedRect(selRect, e.Transform, null, ApplyOpacity(d.SelectionColor));
        }
    }

    private static void GetCursorPositionWrapped(ReadOnlySpan<char> text, Font font, float fontSize,
        float maxWidth, int cursorIndex, out float x, out float y)
    {
        Span<TextRender.CachedLine> lines = stackalloc TextRender.CachedLine[64];
        var lineCount = TextRender.GetWrapLines(text, font, fontSize, maxWidth, 0, lines);
        var lineHeight = font.LineHeight * fontSize;

        if (lineCount == 0)
        {
            x = 0;
            y = 0;
            return;
        }

        for (var i = 0; i < lineCount; i++)
        {
            var line = lines[i];
            if (cursorIndex <= line.End || i == lineCount - 1)
            {
                var posInLine = Math.Clamp(cursorIndex - line.Start, 0, line.End - line.Start);
                var lineText = text[line.Start..line.End];
                x = MeasureTextWidth(lineText[..posInLine], font, fontSize);
                y = i * lineHeight;
                return;
            }
        }

        x = 0;
        y = (lineCount - 1) * lineHeight;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float MeasureTextWidth(ReadOnlySpan<char> text, Font font, float fontSize)
    {
        return TextRender.Measure(text, font, fontSize).X;
    }

    internal static int HitTestCharIndex(ReadOnlySpan<char> text, Font font, float fontSize,
        bool multiLine, float contentWidth, float relX, float relY)
    {
        if (text.Length == 0) return 0;

        if (multiLine && contentWidth > 0)
        {
            Span<TextRender.CachedLine> lines = stackalloc TextRender.CachedLine[64];
            var lineCount = TextRender.GetWrapLines(text, font, fontSize, contentWidth, 0, lines);
            if (lineCount == 0) return 0;

            var lineHeight = font.LineHeight * fontSize;
            var lineIndex = Math.Clamp((int)(relY / lineHeight), 0, lineCount - 1);
            var line = lines[lineIndex];
            var lineText = text[line.Start..line.End];
            return line.Start + FindCharIndexAtX(lineText, font, fontSize, relX);
        }

        return FindCharIndexAtX(text, font, fontSize, relX);
    }

    private static int FindCharIndexAtX(ReadOnlySpan<char> text, Font font, float fontSize, float targetX)
    {
        var x = 0f;
        for (var i = 0; i < text.Length; i++)
        {
            var glyph = font.GetGlyph(text[i]);
            var advance = glyph.Advance * fontSize;
            if (i + 1 < text.Length)
                advance += font.GetKerning(text[i], text[i + 1]) * fontSize;
            if (x + advance * 0.5f > targetX)
                return i;
            x += advance;
        }
        return text.Length;
    }
}
