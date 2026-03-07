//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;
using System.Runtime.CompilerServices;

namespace NoZ;

internal struct EditableTextElement
{
    public UnsafeSpan<char> Text;
    public float FontSize;
    public Color TextColor;
    public Color CursorColor;
    public Color SelectionColor;
    public int WidgetId;
    public bool MultiLine;
    public ushort FontAssetIndex;
    public TextOverflow Overflow;
}

internal struct TextBoxState
{
    public int CursorIndex;
    public int SelectionStart;
    public float ScrollOffset;
    public float BlinkTimer;
    public int TextHash;
    public int PrevTextHash;
    public UnsafeSpan<char> EditText;
    public byte Focused;
    public byte FocusEntered;
    public byte FocusExited;
    public byte WasCancelled;
}

public static unsafe partial class ElementTree
{
    public static bool EditableText(int id, ref string value, Font font, float fontSize,
        Color textColor, Color cursorColor, Color selectionColor,
        string placeholder, Color placeholderColor,
        bool multiLine, bool commitOnEnter = false, InputScope scope = default)
    {
        ref var state = ref GetState<TextBoxState>();
        var focused = HasFocus();

        // Focus on press
        if (WasPressed() && !focused)
        {
            SetFocus();
            focused = true;
            state.Focused = 1;
            state.FocusEntered = 1;
            state.EditText = Text(value);
            state.TextHash = string.GetHashCode(value.AsSpan());
            state.PrevTextHash = state.TextHash;
            state.CursorIndex = value.Length;
            state.SelectionStart = 0;
            state.BlinkTimer = 0;
        }

        // Mouse click-to-position and drag-to-select
        if (focused && IsDown())
        {
            var localMouse = GetLocalMousePosition();
            var editText = state.EditText.AsReadOnlySpan();
            var widgetRect = GetWidgetRect(id);
            var charIndex = HitTestCharIndex(editText, font, fontSize,
                multiLine, widgetRect.Width, localMouse.X, localMouse.Y);
            state.CursorIndex = charIndex;
            if (WasPressed())
                state.SelectionStart = charIndex;
            state.BlinkTimer = 0;
        }

        // Keyboard input when focused
        if (focused)
        {
            state.BlinkTimer += Time.DeltaTime;
            HandleTextInput(ref state, font, fontSize, multiLine, scope, commitOnEnter);
        }

        // Resolve display text
        var editSpan = focused ? state.EditText.AsReadOnlySpan() : value.AsSpan();
        var showPlaceholder = editSpan.Length == 0 && placeholder.Length > 0;
        var displayText = showPlaceholder ? placeholder.AsSpan() : editSpan;
        var displayColor = showPlaceholder ? placeholderColor : textColor;
        var overflow = multiLine ? TextOverflow.Wrap : TextOverflow.Overflow;

        // Create the leaf element
        ref var e = ref CreateLeafElement<EditableTextElement>(ElementType.EditableText, withTransform: true);
        ref var d = ref GetElementData<EditableTextElement>(ref e);
        d.Text = Text(displayText);
        d.FontSize = fontSize;
        d.TextColor = displayColor;
        d.CursorColor = cursorColor;
        d.SelectionColor = selectionColor;
        d.WidgetId = id;
        d.MultiLine = multiLine;
        d.FontAssetIndex = AddAsset(font);
        d.Overflow = overflow;

        // Detect focus loss → commit
        var changed = false;
        if (state.FocusExited != 0)
        {
            if (state.WasCancelled == 0)
            {
                var finalText = state.EditText.AsReadOnlySpan();
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

        return changed;
    }

    private static void HandleTextInput(ref TextBoxState state, Font font, float fontSize,
        bool multiLine, InputScope scope, bool commitOnEnter)
    {
        var editText = state.EditText.AsReadOnlySpan();
        var ctrl = Input.IsCtrlDown();
        var shift = Input.IsShiftDown();

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
                RemoveSelected(ref state);
                editText = state.EditText.AsReadOnlySpan();
                state.EditText = UI.InsertText(editText, state.CursorIndex, "\n");
                state.CursorIndex++;
                state.SelectionStart = state.CursorIndex;
                state.TextHash = string.GetHashCode(state.EditText.AsReadOnlySpan());
                state.BlinkTimer = 0;
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
                RemoveSelected(ref state);
                state.TextHash = string.GetHashCode(state.EditText.AsReadOnlySpan());
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
                editText = state.EditText.AsReadOnlySpan();
                state.EditText = UI.InsertText(editText, state.CursorIndex, clipboard);
                state.CursorIndex += clipboard.Length;
                state.SelectionStart = state.CursorIndex;
                state.TextHash = string.GetHashCode(state.EditText.AsReadOnlySpan());
                state.BlinkTimer = 0;
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
                state.EditText = UI.RemoveText(state.EditText.AsReadOnlySpan(), state.CursorIndex - 1, 1);
                state.CursorIndex--;
                state.SelectionStart = state.CursorIndex;
            }
            state.TextHash = string.GetHashCode(state.EditText.AsReadOnlySpan());
            state.BlinkTimer = 0;
            return;
        }

        // Delete
        if (Input.WasButtonPressed(InputCode.KeyDelete, true, scope))
        {
            editText = state.EditText.AsReadOnlySpan();
            if (state.CursorIndex != state.SelectionStart)
            {
                RemoveSelected(ref state);
            }
            else if (state.CursorIndex < editText.Length)
            {
                state.EditText = UI.RemoveText(editText, state.CursorIndex, 1);
            }
            state.TextHash = string.GetHashCode(state.EditText.AsReadOnlySpan());
            state.BlinkTimer = 0;
            return;
        }

        // Left arrow
        if (Input.WasButtonPressed(InputCode.KeyLeft, true, scope))
        {
            if (state.CursorIndex > 0)
                state.CursorIndex--;
            if (!shift)
                state.SelectionStart = state.CursorIndex;
            state.BlinkTimer = 0;
            return;
        }

        // Right arrow
        if (Input.WasButtonPressed(InputCode.KeyRight, true, scope))
        {
            editText = state.EditText.AsReadOnlySpan();
            if (state.CursorIndex < editText.Length)
                state.CursorIndex++;
            if (!shift)
                state.SelectionStart = state.CursorIndex;
            state.BlinkTimer = 0;
            return;
        }

        // Home
        if (Input.WasButtonPressed(InputCode.KeyHome, true, scope))
        {
            state.CursorIndex = 0;
            if (!shift)
                state.SelectionStart = state.CursorIndex;
            state.BlinkTimer = 0;
            return;
        }

        // End
        if (Input.WasButtonPressed(InputCode.KeyEnd, true, scope))
        {
            state.CursorIndex = state.EditText.AsReadOnlySpan().Length;
            if (!shift)
                state.SelectionStart = state.CursorIndex;
            state.BlinkTimer = 0;
            return;
        }

        // Text input (typed characters)
        var textInput = Input.GetTextInput(scope);
        if (textInput.Length > 0)
        {
            RemoveSelected(ref state);
            editText = state.EditText.AsReadOnlySpan();
            state.EditText = UI.InsertText(editText, state.CursorIndex, textInput);
            state.CursorIndex += textInput.Length;
            state.SelectionStart = state.CursorIndex;
            state.TextHash = string.GetHashCode(state.EditText.AsReadOnlySpan());
            state.BlinkTimer = 0;
        }
    }

    internal static void RemoveSelected(ref TextBoxState state)
    {
        if (state.CursorIndex == state.SelectionStart) return;
        var start = Math.Min(state.CursorIndex, state.SelectionStart);
        var length = Math.Abs(state.CursorIndex - state.SelectionStart);
        state.EditText = UI.RemoveText(state.EditText.AsReadOnlySpan(), start, length);
        state.CursorIndex = start;
        state.SelectionStart = start;
    }

    public static ReadOnlySpan<char> GetEditableText(int widgetId)
    {
        if (!HasFocusOn(widgetId)) return default;
        ref var state = ref GetStateByWidgetId<TextBoxState>(widgetId);
        if (state.Focused == 0) return default;
        return state.EditText.AsReadOnlySpan();
    }

    public static void SetEditableText(int widgetId, ReadOnlySpan<char> value, bool selectAll = false)
    {
        ref var state = ref GetStateByWidgetId<TextBoxState>(widgetId);
        state.EditText = Text(value);
        state.TextHash = string.GetHashCode(value);
        state.CursorIndex = value.Length;
        state.SelectionStart = selectAll ? 0 : value.Length;
        _focusId = widgetId;
        state.Focused = 1;
    }

    private static float FitEditableTextAxis(ref BaseElement e, int axis)
    {
        ref var d = ref GetElementData<EditableTextElement>(ref e);
        var font = (Font)_assets[d.FontAssetIndex]!;
        var text = d.Text.AsReadOnlySpan();

        if (axis == 1 && d.Overflow == TextOverflow.Wrap && e.Rect.Width > 0)
            return TextRender.MeasureWrapped(text, font, d.FontSize, e.Rect.Width).Y;

        var measure = TextRender.Measure(text, font, d.FontSize);
        return measure[axis];
    }

    private static float LayoutEditableTextAxis(ref BaseElement e, int axis, float available)
    {
        ref var d = ref GetElementData<EditableTextElement>(ref e);
        var font = (Font)_assets[d.FontAssetIndex]!;
        var text = d.Text.AsReadOnlySpan();

        if (axis == 1 && d.Overflow == TextOverflow.Wrap && e.Rect.Width > 0)
            return TextRender.MeasureWrapped(text, font, d.FontSize, e.Rect.Width).Y;

        var measure = TextRender.Measure(text, font, d.FontSize);
        return measure[axis];
    }

    private static void DrawEditableText(ref BaseElement e)
    {
        ref var d = ref GetElementData<EditableTextElement>(ref e);
        var font = (Font)_assets[d.FontAssetIndex]!;
        var text = d.Text.AsReadOnlySpan();
        var fontSize = d.FontSize;

        ref var state = ref GetStateByWidgetId<TextBoxState>(d.WidgetId);
        var focused = state.Focused != 0;

        if (text.Length > 0)
        {
            if (d.Overflow == TextOverflow.Wrap && e.Rect.Width > 0)
            {
                var transform = Matrix3x2.CreateTranslation(e.Rect.Position) * GetTransform(ref e);

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
                var transform = Matrix3x2.CreateTranslation(e.Rect.Position + textOffset) * GetTransform(ref e);

                using (Graphics.PushState())
                {
                    Graphics.SetColor(ApplyOpacity(d.TextColor));
                    Graphics.SetTransform(transform);
                    TextRender.Draw(text, font, fontSize);
                }
            }
        }

        if (!focused) return;

        var editText = state.EditText.AsReadOnlySpan();
        var lineHeight = font.LineHeight * fontSize;

        // Draw selection highlight
        if (state.CursorIndex != state.SelectionStart)
        {
            var selStart = Math.Min(state.CursorIndex, state.SelectionStart);
            var selEnd = Math.Max(state.CursorIndex, state.SelectionStart);

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
                DrawTexturedRect(selRect, GetTransform(ref e), null, ApplyOpacity(d.SelectionColor));
            }
        }

        // Draw cursor (blink)
        if (state.BlinkTimer % 1.0f < 0.5f)
        {
            float cursorX, cursorY;

            if (d.MultiLine && e.Rect.Width > 0)
            {
                GetCursorPositionWrapped(editText, font, fontSize, e.Rect.Width, state.CursorIndex, out cursorX, out cursorY);
            }
            else
            {
                cursorX = MeasureTextWidth(editText[..state.CursorIndex], font, fontSize);
                cursorY = (e.Rect.Height - lineHeight) * 0.5f;
            }

            var cursorRect = new Rect(e.Rect.X + cursorX, e.Rect.Y + cursorY, 1.5f, lineHeight);
            DrawTexturedRect(cursorRect, GetTransform(ref e), null, ApplyOpacity(d.CursorColor));
        }
    }

    private static void DrawMultilineSelection(ref BaseElement e, ref EditableTextElement d,
        Font font, ReadOnlySpan<char> text, float fontSize, float lineHeight, int selStart, int selEnd)
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
            DrawTexturedRect(selRect, GetTransform(ref e), null, ApplyOpacity(d.SelectionColor));
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
