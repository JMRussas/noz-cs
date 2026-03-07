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
    public Color PlaceholderColor;
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
    public static bool EditableText(int widgetId, ref string value, Font font, float fontSize,
        Color textColor, Color bgColor, Color cursorColor,
        string placeholder, bool multiLine,
        float height, float borderWidth, Color borderColor, BorderRadius borderRadius,
        float focusBorderWidth, Color focusBorderColor, BorderRadius focusBorderRadius,
        EdgeInsets padding, Color placeholderColor, Color selectionColor,
        bool commitOnEnter = false)
    {
        if (cursorColor.IsTransparent) cursorColor = textColor;
        if (height <= 0) height = multiLine ? fontSize * 4 : fontSize * 1.8f;

        var changed = false;

        BeginWidget(widgetId);
        ref var state = ref GetState<TextBoxState>();

        var focused = HasFocus();
        state.FocusEntered = 0;
        state.FocusExited = 0;
        state.WasCancelled = 0;

        // Click to focus
        if (WasPressed() && !focused)
        {
            SetFocus();
            focused = true;
            state.CursorIndex = value.Length;
            state.SelectionStart = 0;
            state.BlinkTimer = 0;
            state.FocusEntered = 1;
        }

        // Handle keyboard input when focused
        if (focused)
        {
            state.BlinkTimer += Time.DeltaTime;

            if (state.Focused == 0)
            {
                state.EditText = Text(value);
                state.TextHash = value.GetHashCode();
                state.PrevTextHash = state.TextHash;
                state.Focused = 1;
                state.FocusEntered = 1;
            }
            else
            {
                state.EditText = Text(state.EditText.AsReadOnlySpan());
            }

            var scope = InputScope.All;
            var text = state.EditText.AsReadOnlySpan();

            if (Input.WasButtonPressed(InputCode.KeyEscape, scope))
            {
                Input.ConsumeButton(InputCode.KeyEscape);
                ClearFocus();
                state.Focused = 0;
                state.FocusExited = 1;
                state.WasCancelled = 1;
            }
            else if (Input.WasButtonPressed(InputCode.KeyEnter, scope))
            {
                Input.ConsumeButton(InputCode.KeyEnter);
                if (!multiLine || commitOnEnter)
                {
                    var newText = new string(state.EditText.AsReadOnlySpan());
                    if (newText != value)
                    {
                        value = newText;
                        changed = true;
                    }
                    ClearFocus();
                    state.Focused = 0;
                    state.FocusExited = 1;
                }
                else
                {
                    RemoveSelected(ref state);
                    state.EditText = UI.InsertText(state.EditText.AsReadOnlySpan(), state.CursorIndex, "\n");
                    state.CursorIndex++;
                    state.SelectionStart = state.CursorIndex;
                }
            }
            else if (Input.WasButtonPressed(InputCode.KeyTab, scope))
            {
                Input.ConsumeButton(InputCode.KeyTab);
                var newText = new string(state.EditText.AsReadOnlySpan());
                if (newText != value)
                {
                    value = newText;
                    changed = true;
                }
                ClearFocus();
                state.Focused = 0;
                state.FocusExited = 1;
            }
            else
            {
                if (Input.WasButtonPressed(InputCode.KeyLeft, true, scope))
                {
                    Input.ConsumeButton(InputCode.KeyLeft);
                    if (state.CursorIndex > 0) state.CursorIndex--;
                    if (!Input.IsShiftDown(scope)) state.SelectionStart = state.CursorIndex;
                    state.BlinkTimer = 0;
                }
                if (Input.WasButtonPressed(InputCode.KeyRight, true, scope))
                {
                    Input.ConsumeButton(InputCode.KeyRight);
                    if (state.CursorIndex < text.Length) state.CursorIndex++;
                    if (!Input.IsShiftDown(scope)) state.SelectionStart = state.CursorIndex;
                    state.BlinkTimer = 0;
                }
                if (Input.WasButtonPressed(InputCode.KeyHome, scope))
                {
                    Input.ConsumeButton(InputCode.KeyHome);
                    state.CursorIndex = 0;
                    if (!Input.IsShiftDown(scope)) state.SelectionStart = state.CursorIndex;
                }
                if (Input.WasButtonPressed(InputCode.KeyEnd, scope))
                {
                    Input.ConsumeButton(InputCode.KeyEnd);
                    state.CursorIndex = text.Length;
                    if (!Input.IsShiftDown(scope)) state.SelectionStart = state.CursorIndex;
                }

                if (Input.IsCtrlDown(scope) && Input.WasButtonPressed(InputCode.KeyA, scope))
                {
                    Input.ConsumeButton(InputCode.KeyA);
                    state.SelectionStart = 0;
                    state.CursorIndex = text.Length;
                }

                if (Input.IsCtrlDown(scope) && Input.WasButtonPressed(InputCode.KeyC, scope))
                {
                    Input.ConsumeButton(InputCode.KeyC);
                    if (state.CursorIndex != state.SelectionStart)
                    {
                        var start = Math.Min(state.CursorIndex, state.SelectionStart);
                        var length = Math.Abs(state.CursorIndex - state.SelectionStart);
                        Application.Platform.SetClipboardText(new string(text.Slice(start, length)));
                    }
                }

                if (Input.IsCtrlDown(scope) && Input.WasButtonPressed(InputCode.KeyV, scope))
                {
                    Input.ConsumeButton(InputCode.KeyV);
                    var clipboard = Application.Platform.GetClipboardText();
                    if (!string.IsNullOrEmpty(clipboard))
                    {
                        RemoveSelected(ref state);
                        state.EditText = UI.InsertText(state.EditText.AsReadOnlySpan(), state.CursorIndex, clipboard);
                        state.CursorIndex += clipboard.Length;
                        state.SelectionStart = state.CursorIndex;
                    }
                }

                if (Input.IsCtrlDown(scope) && Input.WasButtonPressed(InputCode.KeyX, scope))
                {
                    Input.ConsumeButton(InputCode.KeyX);
                    if (state.CursorIndex != state.SelectionStart)
                    {
                        var start = Math.Min(state.CursorIndex, state.SelectionStart);
                        var length = Math.Abs(state.CursorIndex - state.SelectionStart);
                        Application.Platform.SetClipboardText(new string(text.Slice(start, length)));
                        RemoveSelected(ref state);
                    }
                }

                if (Input.WasButtonPressed(InputCode.KeyBackspace, true, scope))
                {
                    Input.ConsumeButton(InputCode.KeyBackspace);
                    if (state.CursorIndex != state.SelectionStart)
                        RemoveSelected(ref state);
                    else if (state.CursorIndex > 0)
                    {
                        state.EditText = UI.RemoveText(state.EditText.AsReadOnlySpan(), state.CursorIndex - 1, 1);
                        state.CursorIndex--;
                        state.SelectionStart = state.CursorIndex;
                    }
                }

                if (Input.WasButtonPressed(InputCode.KeyDelete, true, scope))
                {
                    Input.ConsumeButton(InputCode.KeyDelete);
                    if (state.CursorIndex != state.SelectionStart)
                        RemoveSelected(ref state);
                    else if (state.CursorIndex < text.Length)
                    {
                        state.EditText = UI.RemoveText(state.EditText.AsReadOnlySpan(), state.CursorIndex, 1);
                    }
                }

                var input = Input.GetTextInput(scope);
                if (!string.IsNullOrEmpty(input))
                {
                    RemoveSelected(ref state);
                    state.EditText = UI.InsertText(state.EditText.AsReadOnlySpan(), state.CursorIndex, input);
                    state.CursorIndex += input.Length;
                    state.SelectionStart = state.CursorIndex;
                }

                for (var i = (int)InputCode.KeyA; i <= (int)InputCode.KeyRightSuper; i++)
                    Input.ConsumeButton((InputCode)i);
            }

            var len = state.EditText.Length;
            state.CursorIndex = Math.Clamp(state.CursorIndex, 0, len);
            state.SelectionStart = Math.Clamp(state.SelectionStart, 0, len);

            // Track per-frame text changes
            state.PrevTextHash = state.TextHash;
            state.TextHash = string.GetHashCode(state.EditText.AsReadOnlySpan());
        }
        else
        {
            if (state.Focused == 1)
                state.FocusExited = 1;
            state.Focused = 0;
        }

        // Visual — always emit border to prevent layout jump
        var activeBorderWidth = focused ? (focusBorderWidth > 0 ? focusBorderWidth : borderWidth) : borderWidth;
        var activeBorderColor = focused ? (!focusBorderColor.IsTransparent ? focusBorderColor : borderColor) : borderColor;
        var activeBorderRadius = focused ? (focusBorderRadius.TopLeft > 0 || focusBorderRadius.TopRight > 0 || focusBorderRadius.BottomLeft > 0 || focusBorderRadius.BottomRight > 0 ? focusBorderRadius : borderRadius) : borderRadius;

        BeginBorder(activeBorderWidth, activeBorderColor, activeBorderRadius);
        BeginSize(Size.Percent(1), new Size(height));
        BeginFill(bgColor);
        BeginPadding(padding);
        {
            var overflow = multiLine ? TextOverflow.Wrap : TextOverflow.Overflow;
            var displayText = focused ? state.EditText : Text(value);

            ref var leaf = ref CreateLeafElement<EditableTextElement>(ElementType.EditableText, withTransform: true);
            ref var d = ref GetElementData<EditableTextElement>(ref leaf);
            d.Text = displayText;
            d.FontSize = fontSize;
            d.TextColor = textColor;
            d.CursorColor = cursorColor;
            d.SelectionColor = selectionColor;
            d.PlaceholderColor = placeholderColor;
            d.WidgetId = widgetId;
            d.MultiLine = multiLine;
            d.FontAssetIndex = AddAsset(font);
            d.Overflow = overflow;

            if (displayText.Length == 0 && placeholder.Length > 0)
            {
                d.Text = Text(placeholder);
                d.TextColor = placeholderColor;
            }
        }
        EndPadding();
        EndFill();
        EndSize();
        EndBorder();

        // Commit on focus loss
        if (state.Focused == 1 && !focused && state.FocusExited == 0)
        {
            var newText = new string(state.EditText.AsReadOnlySpan());
            if (newText != value)
            {
                value = newText;
                changed = true;
            }
            state.Focused = 0;
            state.FocusExited = 1;
        }

        EndWidget();
        return changed;
    }

    private static void RemoveSelected(ref TextBoxState state)
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

        // Draw text (color already set correctly by compound widget — placeholder uses PlaceholderColor)
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

    private static int HitTestCharIndex(ReadOnlySpan<char> text, Font font, float fontSize,
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
