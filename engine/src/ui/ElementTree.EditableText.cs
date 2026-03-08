//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;
using System.Runtime.CompilerServices;

namespace NoZ;


public struct EditableTextState 
{
    public int CursorIndex;
    public int SelectionStart;
    public float ScrollOffset;
    public int TextHash;
    public int PrevTextHash;
    public byte Focused;
    public byte FocusEntered;
    public byte FocusExited;
    public byte WasCancelled;
}

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
        var focused = HasFocus();

        // Focus on press
        if (WasPressed() && !focused)
        {
            SetFocus();
            focused = true;
            state.Focused = 1;
            state.FocusEntered = 1;
            state.TextHash = string.GetHashCode(value.AsSpan());
            state.PrevTextHash = state.TextHash;
            state.CursorIndex = value.Length;
            state.SelectionStart = 0;

            d.Text = Text(value);
        }

        // Mouse click-to-position and drag-to-select
        if (focused && IsDown())
        {
            var localMouse = GetLocalMousePosition();
            var editText = d.Text.AsReadOnlySpan();
            var widgetRect = e.Rect;
            var charIndex = HitTestCharIndex(editText, font, fontSize,
                multiLine, widgetRect.Width, localMouse.X, localMouse.Y);
            state.CursorIndex = charIndex;
            if (WasPressed())
                state.SelectionStart = charIndex;
        }

        // Resolve display text (before allocating element so we know what to display)
        var editSpan = focused ? d.Text.AsReadOnlySpan() : value.AsSpan();
        var showPlaceholder = editSpan.Length == 0 && placeholder.Length > 0;
        var displayText = showPlaceholder ? placeholder.AsSpan() : editSpan;
        var displayColor = showPlaceholder ? placeholderColor : textColor;
        var overflow = multiLine ? TextOverflow.Wrap : TextOverflow.Overflow;

        d.Text = Text(displayText);
        d.FontSize = fontSize;
        d.TextColor = displayColor;
        d.CursorColor = cursorColor;
        d.SelectionColor = selectionColor;
        d.MultiLine = multiLine;
        d.Font = AddObject(font);

        // Keyboard input when focused — text edits allocate after the element struct
        if (focused)
            HandleTextInput(ref d, ref state, font, fontSize, multiLine, scope, commitOnEnter);

        // Detect focus loss → commit
        var changed = false;
        if (state.FocusExited != 0)
        {
            if (state.WasCancelled == 0)
            {
                var finalText = d.Text.AsReadOnlySpan();
                var finalHash = string.GetHashCode(finalText);
                if (finalHash != state.PrevTextHash)
                {
                    value = new string(finalText);
                    changed = true;
                }
            }
            state.Focused = 0;
            state.FocusExited = 0;
            state.WasCancelled = 0;
        }

        if (focused && !HasFocus())
        {
            state.FocusExited = 1;
            state.Focused = 0;
        }

        d.SelectionStart = state.SelectionStart;
        d.CursorIndex = state.CursorIndex;

        return value;
    }

    private static void HandleTextInput(
        ref EditableTextElement d,
        ref EditableTextState state,
        Font font,
        float fontSize,
        bool multiLine,
        InputScope scope,
        bool commitOnEnter)
    {
        var editText = d.Text.AsReadOnlySpan();
        var ctrl = Input.IsCtrlDown();
        var shift = Input.IsShiftDown();
        ref var text = ref d.Text;

        // Escape — cancel
        if (Input.WasButtonPressed(InputCode.KeyEscape, true, scope))
        {
            state.WasCancelled = 1;
            ClearFocus();
            return;
        }

        // Tab — commit and defocus
        if (Input.WasButtonPressed(InputCode.KeyTab, true, scope))
        {
            ClearFocus();
            return;
        }

        // Enter
        if (Input.WasButtonPressed(InputCode.KeyEnter, true, scope))
        {
            if (multiLine && !commitOnEnter)
            {
                RemoveSelected(ref d, ref state);
                editText = text.AsReadOnlySpan();
                text = InsertText(editText, state.CursorIndex, "\n");
                state.CursorIndex++;
                state.SelectionStart = state.CursorIndex;
                state.TextHash = string.GetHashCode(text.AsReadOnlySpan());
            }
            else
            {
                ClearFocus();
            }
            return;
        }

        // Ctrl+A — select all
        if (ctrl && Input.WasButtonPressed(InputCode.KeyA, true, scope))
        {
            state.SelectionStart = 0;
            state.CursorIndex = editText.Length;
            return;
        }

        // Ctrl+C — copy
        if (ctrl && Input.WasButtonPressed(InputCode.KeyC, true, scope))
        {
            if (state.CursorIndex != state.SelectionStart)
            {
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
                var selStart = Math.Min(state.CursorIndex, state.SelectionStart);
                var selEnd = Math.Max(state.CursorIndex, state.SelectionStart);
                Application.Platform.SetClipboardText(new string(editText[selStart..selEnd]));
                RemoveSelected(ref d, ref state);
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
                RemoveSelected(ref d, ref state);
                editText = text.AsReadOnlySpan();
                text = InsertText(editText, state.CursorIndex, clipboard);
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
                RemoveSelected(ref d, ref state);
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
            editText = text.AsReadOnlySpan();
            if (state.CursorIndex != state.SelectionStart)
            {
                RemoveSelected(ref d, ref state);
            }
            else if (state.CursorIndex < editText.Length)
            {
                text = RemoveText(editText, state.CursorIndex, 1);
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
            editText = text.AsReadOnlySpan();
            if (state.CursorIndex < editText.Length)
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
            RemoveSelected(ref d, ref state);
            editText = text.AsReadOnlySpan();
            text = InsertText(editText, state.CursorIndex, textInput);
            state.CursorIndex += textInput.Length;
            state.SelectionStart = state.CursorIndex;
            state.TextHash = string.GetHashCode(text.AsReadOnlySpan());
        }
    }

    internal static void RemoveSelected(ref EditableTextElement d, ref EditableTextState state)
    {
        if (state.CursorIndex == state.SelectionStart) return;
        var start = Math.Min(state.CursorIndex, state.SelectionStart);
        var length = Math.Abs(state.CursorIndex - state.SelectionStart);
        ref var text = ref d.Text;
        text = RemoveText(text.AsReadOnlySpan(), start, length);
        state.CursorIndex = start;
        state.SelectionStart = start;
    }

    #if false
    public static ReadOnlySpan<char> GetEditableText(int widgetId)
    {
        if (!HasFocusOn(widgetId)) return default;
        ref var state = ref GetWidgetData<EditableTextState>(widgetId);
        if (d.Focused == 0) return default;
        return d.EditText.AsReadOnlySpan();
    }

    public static void SetEditableText(int widgetId, ReadOnlySpan<char> value, bool selectAll = false)
    {
        ref var state = ref GetWidgetData<EditableTextState>(widgetId);
        d.EditText = Text(value);
        d.TextHash = string.GetHashCode(value);
        d.CursorIndex = value.Length;
        d.SelectionStart = selectAll ? 0 : value.Length;
        _focusId = widgetId;
        d.Focused = 1;
    }
    #endif

    private static float FitEditableTextAxis(ref Element e, int axis)
    {
        ref var d = ref e.Data.EditableText;
        var font = (Font)_assets[d.Font]!;
        var text = d.Text.AsReadOnlySpan();

        if (axis == 1 && d.MultiLine && e.Rect.Width > 0)
            return TextRender.MeasureWrapped(text, font, d.FontSize, e.Rect.Width).Y;

        var measure = TextRender.Measure(text, font, d.FontSize);
        return measure[axis];
    }

    private static float LayoutEditableTextAxis(ref Element e, int axis, float available)
    {
        ref var d = ref e.Data.EditableText;
        var font = (Font)_assets[d.Font]!;
        var text = d.Text.AsReadOnlySpan();

        if (axis == 1 && d.MultiLine && e.Rect.Width > 0)
            return TextRender.MeasureWrapped(text, font, d.FontSize, e.Rect.Width).Y;

        var measure = TextRender.Measure(text, font, d.FontSize);
        return measure[axis];
    }

    private static void DrawEditableText(ref Element e)
    {
        ref var d = ref e.Data.EditableText;
        ref var t = ref e.Transform;
        var font = (Font)_assets[d.Font]!;
        var text = d.Text.AsReadOnlySpan();
        var fontSize = d.FontSize;

        var focused = false; //  d.Focused != 0;

        // text
        if (text.Length > 0)
        {
            if (d.MultiLine && e.Rect.Width > 0)
            {
                var transform = Matrix3x2.CreateTranslation(e.Rect.Position) * t;

                using (Graphics.PushState())
                {
                    Graphics.SetColor(ApplyOpacity(d.TextColor));
                    Graphics.SetTransform(transform);
                    TextRender.DrawWrapped(text, font, fontSize, e.Rect.Width,
                        e.Rect.Width, 0f, e.Rect.Height);
                }
            }
            else
            {
                var textOffset = GetTextOffset(text, font, fontSize, e.Rect.Size, Align.Min, Align.Center);
                var transform = Matrix3x2.CreateTranslation(e.Rect.Position + textOffset) * t;

                using (Graphics.PushState())
                {
                    Graphics.SetColor(ApplyOpacity(d.TextColor));
                    Graphics.SetTransform(transform);
                    TextRender.Draw(text, font, fontSize);
                }
            }
        }

        if (!focused) return;

        var editText = text;
        var lineHeight = font.LineHeight * fontSize;

        // selection
        if (d.CursorIndex != d.SelectionStart)
        {
            var selStart = Math.Min(d.CursorIndex, d.SelectionStart);
            var selEnd = Math.Max(d.CursorIndex, d.SelectionStart);

            if (d.MultiLine && e.Rect.Width > 0)
            {
                DrawMultilineSelection(ref e, ref d, font, editText, fontSize, lineHeight, selStart, selEnd);
            }
            else
            {
                var textOffsetY = (e.Rect.Height - lineHeight) * 0.5f;
                var x0 = MeasureTextWidth(editText[..selStart], font, fontSize);
                var x1 = MeasureTextWidth(editText[..selEnd], font, fontSize);
                var selRect = new Rect(e.Rect.X + x0, e.Rect.Y + textOffsetY, x1 - x0, lineHeight);
                DrawTexturedRect(selRect, t, null, ApplyOpacity(d.SelectionColor));
            }
        }

        // cursor
        if (Time.TotalTime % 1.0f < 0.5f)
        {
            float cursorX, cursorY;

            if (d.MultiLine && e.Rect.Width > 0)
            {
                GetCursorPositionWrapped(editText, font, fontSize, e.Rect.Width, d.CursorIndex, out cursorX, out cursorY);
            }
            else
            {
                cursorX = MeasureTextWidth(editText[..d.CursorIndex], font, fontSize);
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
