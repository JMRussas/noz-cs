//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ.Widgets;

internal struct TextAreaWidgetState
{
    public TextAreaData Style;
    public int CursorIndex;
    public int SelectionStart;
    public float ScrollOffset;
    public float BlinkTimer;
    public int TextHash;
    public float DesiredColumn;
    public UnsafeSpan<char> Text;
}

public static partial class Widget
{
    public static readonly WidgetType TextAreaType = Register(
        draw: TextAreaDraw,
        measure: TextAreaMeasure,
        input: TextAreaInput,
        getText: TextAreaGetText,
        setText: TextAreaSetTextCallback
    );

    public static bool TextArea(int id, ReadOnlySpan<char> text, TextAreaStyle style,
        ReadOnlySpan<char> placeholder = default, IChangeHandler? handler = null)
    {
        ref var state = ref GetState<TextAreaWidgetState>(id);
        state.Style = style.ToData();
        if (!placeholder.IsEmpty)
            state.Style.Placeholder = UI.AddText(placeholder);

        ref var es = ref UI.GetElementState(id);

        if (es.HasFocus)
        {
            UI.SetHot(id, text);
            state.Text = UI.AddText(state.Text.AsReadOnlySpan());
        }
        else
        {
            state.Text = UI.AddText(text);
        }

        var font = style.Font ?? UI.DefaultFont;
        UI.Widget(id, TextAreaType, font);

        var changed = es.IsChanged;
        es.SetFlags(ElementFlags.Changed, ElementFlags.None);

        if (changed)
            UI.NotifyChanged(state.TextHash);

        UI.SetLastElement(id);
        HandleChange(handler);
        return changed;
    }

    public static string TextArea(int id, string value, TextAreaStyle style,
        string? placeholder = null, IChangeHandler? handler = null)
    {
        if (TextArea(id, (ReadOnlySpan<char>)value, style, placeholder, handler))
            value = new string(UI.GetElementText(id));
        return value;
    }

    // --- GetText / SetText callbacks ---

    private static ReadOnlySpan<char> TextAreaGetText(int id)
    {
        ref var state = ref GetState<TextAreaWidgetState>(id);
        return state.Text.AsReadOnlySpan();
    }

    private static void TextAreaSetTextCallback(int id, ReadOnlySpan<char> value, bool selectAll)
    {
        ref var state = ref GetState<TextAreaWidgetState>(id);
        state.Text = UI.AddText(value);
        state.TextHash = string.GetHashCode(value);
        state.CursorIndex = value.Length;
        state.SelectionStart = selectAll ? 0 : value.Length;
    }

    // --- Measure ---

    private static Vector2 TextAreaMeasure(int id)
    {
        ref var state = ref GetState<TextAreaWidgetState>(id);
        var defaultHeight = state.Style.Height.Mode == SizeMode.Default ? 100f : state.Style.Height.Value;
        return UI.ResolveWidgetSize(id, new Size2(Size.Default, state.Style.Height),
            new Vector2(100f, defaultHeight));
    }

    // --- Draw ---

    private static void TextAreaDraw(int id)
    {
        ref var state = ref GetState<TextAreaWidgetState>(id);
        ref var es = ref UI.GetElementState(id);
        ref var e = ref UI.GetElement(es.Index);
        ref var data = ref state.Style;

        var isFocused = es.HasFocus;
        var borderRadius = isFocused ? data.FocusBorderRadius : data.BorderRadius;
        var borderWidth = isFocused ? data.FocusBorderWidth : data.BorderWidth;
        var borderColor = isFocused ? data.FocusBorderColor : data.BorderColor;

        UI.DrawTexturedRect(
            e.Rect, e.LocalToWorld, null,
            UI.ApplyOpacity(data.BackgroundColor),
            borderRadius,
            borderWidth,
            UI.ApplyOpacity(borderColor)
        );

        var font = (Font?)e.Asset ?? UI.DefaultFont;
        var text = state.Text.AsReadOnlySpan();
        TextAreaDrawSelection(ref e, ref state, text, font);
        TextAreaDrawText(ref e, ref state, text, font, data.FontSize, UI.ApplyOpacity(data.TextColor));
        TextAreaDrawPlaceholder(ref e, ref state, text, font);
        TextAreaDrawCursor(ref e, ref state, text, font);
    }

    private static void TextAreaDrawSelection(ref Element e, ref TextAreaWidgetState state, in ReadOnlySpan<char> text, Font font)
    {
        ref var es = ref UI.GetElementState(e.Id);
        if (!es.HasFocus) return;
        if (state.CursorIndex == state.SelectionStart) return;

        var data = state.Style;
        var padding = data.Padding;
        var fontSize = data.FontSize;
        var contentWidth = e.Rect.Width - padding.Horizontal;
        var contentHeight = e.Rect.Height - padding.Vertical;
        var lineHeight = font.LineHeight * fontSize;

        Span<TextRender.CachedLine> lines = stackalloc TextRender.CachedLine[TextRender.MaxWrappedLines];
        var lineCount = TextRender.GetWrapLines(text, font, fontSize, contentWidth, e.Id, lines);

        var selStart = Math.Min(state.CursorIndex, state.SelectionStart);
        var selEnd = Math.Max(state.CursorIndex, state.SelectionStart);

        var (startLine, _) = TextAreaCharToLine(lines, lineCount, selStart);
        var (endLine, _) = TextAreaCharToLine(lines, lineCount, selEnd);

        var scale = UI.GetUIScale();
        var screenPos = UI.Camera!.WorldToScreen(
            Vector2.Transform(e.Rect.Position + new Vector2(padding.L, padding.T), e.LocalToWorld));
        var screenHeight = Application.WindowSize.Y;
        var scissor = new RectInt(
            (int)screenPos.X,
            (int)(screenHeight - screenPos.Y - contentHeight * scale),
            (int)(contentWidth * scale),
            (int)(contentHeight * scale));

        using (Graphics.PushState())
        {
            Graphics.SetScissor(scissor);

            for (int line = startLine; line <= endLine && line < lineCount; line++)
            {
                var lineY = line * lineHeight - state.ScrollOffset + padding.T;

                if (lineY + lineHeight < padding.T || lineY > padding.T + contentHeight)
                    continue;

                var lineStart = lines[line].Start;
                var lineEnd = (line + 1 < lineCount) ? lines[line + 1].Start : text.Length;

                float selX0, selX1;
                if (line == startLine && line == endLine)
                {
                    selX0 = TextAreaCursorX(text, lineStart, selStart, font, fontSize);
                    selX1 = TextAreaCursorX(text, lineStart, selEnd, font, fontSize);
                }
                else if (line == startLine)
                {
                    selX0 = TextAreaCursorX(text, lineStart, selStart, font, fontSize);
                    selX1 = TextAreaCursorX(text, lineStart, Math.Min(lineEnd, text.Length), font, fontSize);
                }
                else if (line == endLine)
                {
                    selX0 = 0;
                    selX1 = TextAreaCursorX(text, lineStart, selEnd, font, fontSize);
                }
                else
                {
                    selX0 = 0;
                    selX1 = TextAreaCursorX(text, lineStart, Math.Min(lineEnd, text.Length), font, fontSize);
                }

                if (selX1 <= selX0) continue;

                UI.DrawTexturedRect(
                    new Rect(selX0 + padding.L + e.Rect.X, lineY + e.Rect.Y, selX1 - selX0, lineHeight),
                    e.LocalToWorld, null,
                    UI.ApplyOpacity(data.SelectionColor));
            }

            Graphics.ClearScissor();
        }
    }

    private static void TextAreaDrawText(
        ref Element e,
        ref TextAreaWidgetState state,
        in ReadOnlySpan<char> text,
        Font font,
        float fontSize,
        Color color)
    {
        if (text.Length == 0) return;

        var padding = state.Style.Padding;
        var scale = UI.GetUIScale();
        var screenPos = UI.Camera!.WorldToScreen(
            Vector2.Transform(e.Rect.Position + new Vector2(padding.L, padding.T), e.LocalToWorld));
        var screenHeight = Application.WindowSize.Y;
        var contentWidth = e.Rect.Width - padding.Horizontal;
        var contentHeight = e.Rect.Height - padding.Vertical;
        var scissor = new RectInt(
            (int)screenPos.X,
            (int)(screenHeight - screenPos.Y - contentHeight * scale),
            (int)(contentWidth * scale),
            (int)(contentHeight * scale));

        var textOffset = new Vector2(
            e.Rect.X + padding.L,
            -state.ScrollOffset + e.Rect.Y + padding.T);

        using (Graphics.PushState())
        {
            Graphics.SetScissor(scissor);
            Graphics.SetColor(color);
            Graphics.SetTransform(Matrix3x2.CreateTranslation(textOffset) * e.LocalToWorld);
            TextRender.DrawWrapped(text, font, fontSize, contentWidth,
                contentWidth, 0f, maxHeight: 0, order: 2, cacheId: e.Id);
            Graphics.ClearScissor();
        }
    }

    private static void TextAreaDrawPlaceholder(ref Element e, ref TextAreaWidgetState state, in ReadOnlySpan<char> text, Font font)
    {
        ref var data = ref state.Style;
        if (text.Length > 0 || data.Placeholder.Length == 0)
            return;

        var padding = data.Padding;
        var fontSize = data.FontSize;
        var placeholder = new string(data.Placeholder.AsReadOnlySpan());
        var contentWidth = e.Rect.Width - padding.Horizontal;
        var placeholderOffset = new Vector2(
            e.Rect.X + padding.L,
            e.Rect.Y + padding.T);
        var transform = Matrix3x2.CreateTranslation(placeholderOffset) * e.LocalToWorld;

        using (Graphics.PushState())
        {
            Graphics.SetColor(UI.ApplyOpacity(data.PlaceholderColor));
            Graphics.SetTransform(transform);
            TextRender.DrawWrapped(placeholder, font, fontSize, contentWidth,
                contentWidth, 0f, maxHeight: 0, order: 2, cacheId: 0);
        }
    }

    private static void TextAreaDrawCursor(ref Element e, ref TextAreaWidgetState state, in ReadOnlySpan<char> text, Font font)
    {
        ref var es = ref UI.GetElementState(e.Id);
        if (!es.HasFocus) return;
        if (state.CursorIndex != state.SelectionStart) return;

        state.BlinkTimer += Time.DeltaTime;
        if ((int)(state.BlinkTimer * 2) % 2 == 1 && !es.IsDragging) return;

        var padding = state.Style.Padding;
        var fontSize = state.Style.FontSize;
        var contentWidth = e.Rect.Width - padding.Horizontal;
        var contentHeight = e.Rect.Height - padding.Vertical;
        var lineHeight = font.LineHeight * fontSize;

        Span<TextRender.CachedLine> lines = stackalloc TextRender.CachedLine[TextRender.MaxWrappedLines];
        var lineCount = TextRender.GetWrapLines(text, font, fontSize, contentWidth, e.Id, lines);
        var (cursorLine, _) = TextAreaCharToLine(lines, lineCount, state.CursorIndex);

        var cursorX = TextAreaCursorX(text, lines[cursorLine].Start, state.CursorIndex, font, fontSize);
        var cursorY = cursorLine * lineHeight - state.ScrollOffset;

        if (cursorY + lineHeight < 0 || cursorY > contentHeight) return;

        UI.DrawTexturedRect(
            new Rect(cursorX + padding.L + e.Rect.X, cursorY + padding.T + e.Rect.Y, 1f, lineHeight),
            e.LocalToWorld, null,
            UI.ApplyOpacity(Color.White));
    }

    // --- Input ---

    private static void TextAreaInput(int id)
    {
        ref var state = ref GetState<TextAreaWidgetState>(id);
        ref var es = ref UI.GetElementState(id);
        ref var e = ref UI.GetElement(es.Index);

        if (UI.HotId != id)
        {
            es.SetFlags(ElementFlags.Focus | ElementFlags.Dragging, ElementFlags.None);
            state.ScrollOffset = 0.0f;
            return;
        }

        TextAreaHandleInput(ref e, ref state);
        TextAreaUpdateScroll(ref e, ref state);
    }

    private static void TextAreaHandleInput(ref Element e, ref TextAreaWidgetState state)
    {
        var scope = state.Style.Scope;
        var control = Input.IsCtrlDown(scope);
        var shift = Input.IsShiftDown(scope);
        var mousePos = UI.Camera!.ScreenToWorld(Input.MousePosition);
        var localMouse = Vector2.Transform(mousePos, e.WorldToLocal);
        var isMouseOver = e.Rect.Contains(localMouse);
        var fontSize = state.Style.FontSize;
        var font = (Font)e.Asset!;
        var padding = state.Style.Padding;
        var contentWidth = e.Rect.Width - padding.Horizontal;
        var lineHeight = font.LineHeight * fontSize;

        ref var es = ref UI.GetElementState(e.Id);
        var text = state.Text.AsReadOnlySpan();

        if (!es.HasFocus)
        {
            es.SetFlags(ElementFlags.Focus, ElementFlags.Focus);
            state.SelectionStart = 0;
            state.CursorIndex = text.Length;
            state.BlinkTimer = 0;
            state.DesiredColumn = -1;
            return;
        }

        // Double Click to Select All
        if (isMouseOver && Input.WasButtonPressed(InputCode.MouseLeftDoubleClick, scope))
        {
            state.SelectionStart = 0;
            state.CursorIndex = text.Length;
            es.SetFlags(ElementFlags.Dragging, ElementFlags.None);
            state.BlinkTimer = 0;
            state.DesiredColumn = -1;
            return;
        }

        // Standard Mouse Input
        if (isMouseOver && Input.WasButtonPressed(InputCode.MouseLeft, scope))
        {
            var mouseIndex = TextAreaGetPosition(ref e, ref state, text, font, fontSize, mousePos);
            state.CursorIndex = mouseIndex;
            state.SelectionStart = mouseIndex;
            es.SetFlags(ElementFlags.Dragging, ElementFlags.Dragging);
            state.BlinkTimer = 0;
            state.DesiredColumn = -1;
        }
        else if (es.IsDragging)
        {
            if (Input.IsButtonDownRaw(InputCode.MouseLeft))
            {
                var mouseIndex = TextAreaGetPosition(ref e, ref state, text, font, fontSize, mousePos);
                state.CursorIndex = mouseIndex;
            }
            else
            {
                es.SetFlags(ElementFlags.Dragging, ElementFlags.None);
            }
        }

        // Mouse wheel scrolling
        if (isMouseOver)
        {
            var scrollDelta = Input.GetAxis(InputCode.MouseScrollY, scope);
            if (scrollDelta != 0)
                state.ScrollOffset -= scrollDelta * lineHeight * 3;
        }

        text = state.Text.AsReadOnlySpan();

        // Get wrap lines for navigation
        Span<TextRender.CachedLine> lines = stackalloc TextRender.CachedLine[TextRender.MaxWrappedLines];
        var lineCount = TextRender.GetWrapLines(text, font, fontSize, contentWidth, e.Id, lines);

        // Keyboard Navigation
        if (Input.WasButtonPressed(InputCode.KeyUp, true, scope))
        {
            Input.ConsumeButton(InputCode.KeyUp);
            var (line, _) = TextAreaCharToLine(lines, lineCount, state.CursorIndex);
            if (line > 0)
            {
                if (state.DesiredColumn < 0)
                    state.DesiredColumn = TextAreaCursorX(text, lines[line].Start, state.CursorIndex, font, fontSize);
                var prevLine = line - 1;
                var prevLineEnd = lines[prevLine + 1].Start;
                state.CursorIndex = TextAreaCharFromX(text, lines[prevLine].Start, prevLineEnd, state.DesiredColumn, font, fontSize);
            }
            else
            {
                state.CursorIndex = 0;
                state.DesiredColumn = -1;
            }
            if (!shift) state.SelectionStart = state.CursorIndex;
            state.BlinkTimer = 0;
        }
        else if (Input.WasButtonPressed(InputCode.KeyDown, true, scope))
        {
            Input.ConsumeButton(InputCode.KeyDown);
            var (line, _) = TextAreaCharToLine(lines, lineCount, state.CursorIndex);
            if (line < lineCount - 1)
            {
                if (state.DesiredColumn < 0)
                    state.DesiredColumn = TextAreaCursorX(text, lines[line].Start, state.CursorIndex, font, fontSize);
                var nextLine = line + 1;
                var nextLineEnd = (nextLine + 1 < lineCount) ? lines[nextLine + 1].Start : text.Length;
                state.CursorIndex = TextAreaCharFromX(text, lines[nextLine].Start, nextLineEnd, state.DesiredColumn, font, fontSize);
            }
            else
            {
                state.CursorIndex = text.Length;
                state.DesiredColumn = -1;
            }
            if (!shift) state.SelectionStart = state.CursorIndex;
            state.BlinkTimer = 0;
        }
        else if (Input.WasButtonPressed(InputCode.KeyLeft, true, scope))
        {
            Input.ConsumeButton(InputCode.KeyLeft);
            if (control)
                state.CursorIndex = TextBoxFindPrevWordStart(text, state.CursorIndex);
            else if (state.CursorIndex > 0)
                state.CursorIndex--;
            else if (!shift && state.CursorIndex != state.SelectionStart)
                state.CursorIndex = Math.Min(state.CursorIndex, state.SelectionStart);

            if (!shift) state.SelectionStart = state.CursorIndex;
            state.BlinkTimer = 0;
            state.DesiredColumn = -1;
        }
        else if (Input.WasButtonPressed(InputCode.KeyRight, true, scope))
        {
            Input.ConsumeButton(InputCode.KeyRight);
            if (control)
                state.CursorIndex = TextBoxFindNextWordStart(text, state.CursorIndex);
            else if (state.CursorIndex < text.Length)
                state.CursorIndex++;
            else if (!shift && state.CursorIndex != state.SelectionStart)
                state.CursorIndex = Math.Max(state.CursorIndex, state.SelectionStart);

            if (!shift) state.SelectionStart = state.CursorIndex;
            state.BlinkTimer = 0;
            state.DesiredColumn = -1;
        }
        else if (Input.WasButtonPressed(InputCode.KeyHome, scope))
        {
            Input.ConsumeButton(InputCode.KeyHome);
            if (control)
            {
                state.CursorIndex = 0;
            }
            else
            {
                var (line, _) = TextAreaCharToLine(lines, lineCount, state.CursorIndex);
                state.CursorIndex = lines[line].Start;
            }
            if (!shift) state.SelectionStart = state.CursorIndex;
            state.BlinkTimer = 0;
            state.DesiredColumn = -1;
        }
        else if (Input.WasButtonPressed(InputCode.KeyEnd, scope))
        {
            Input.ConsumeButton(InputCode.KeyEnd);
            if (control)
            {
                state.CursorIndex = text.Length;
            }
            else
            {
                var (line, _) = TextAreaCharToLine(lines, lineCount, state.CursorIndex);
                var lineEnd = (line + 1 < lineCount) ? lines[line + 1].Start : text.Length;
                if (lineEnd > 0 && lineEnd <= text.Length && lineEnd > lines[line].Start && text[lineEnd - 1] == '\n')
                    state.CursorIndex = lineEnd - 1;
                else
                    state.CursorIndex = lineEnd;
            }
            if (!shift) state.SelectionStart = state.CursorIndex;
            state.BlinkTimer = 0;
            state.DesiredColumn = -1;
        }
        else if (control && Input.WasButtonPressed(InputCode.KeyA, scope))
        {
            Input.ConsumeButton(InputCode.KeyA);
            state.SelectionStart = 0;
            state.CursorIndex = text.Length;
            state.DesiredColumn = -1;
        }
        else if (control && Input.WasButtonPressed(InputCode.KeyC, scope))
        {
            Input.ConsumeButton(InputCode.KeyC);
            if (state.CursorIndex != state.SelectionStart)
            {
                var start = Math.Min(state.CursorIndex, state.SelectionStart);
                var length = Math.Abs(state.CursorIndex - state.SelectionStart);
                Application.Platform.SetClipboardText(new string(text.Slice(start, length)));
            }
        }
        else if (control && Input.WasButtonPressed(InputCode.KeyV, scope))
        {
            Input.ConsumeButton(InputCode.KeyV);
            var clipboard = Application.Platform.GetClipboardText();
            if (!string.IsNullOrEmpty(clipboard))
            {
                TextAreaRemoveSelected(ref state);
                text = state.Text.AsReadOnlySpan();
                TextAreaSetText(ref state, UI.InsertText(text, state.CursorIndex, clipboard));
                state.CursorIndex += clipboard.Length;
                state.SelectionStart = state.CursorIndex;
                state.BlinkTimer = 0;
                state.DesiredColumn = -1;
            }
        }
        else if (control && Input.WasButtonPressed(InputCode.KeyX, scope))
        {
            Input.ConsumeButton(InputCode.KeyX);
            if (state.CursorIndex != state.SelectionStart)
            {
                var start = Math.Min(state.CursorIndex, state.SelectionStart);
                var length = Math.Abs(state.CursorIndex - state.SelectionStart);
                Application.Platform.SetClipboardText(new string(text.Slice(start, length)));
                TextAreaRemoveSelected(ref state);
                state.BlinkTimer = 0;
                state.DesiredColumn = -1;
            }
        }
        else if (Input.WasButtonPressed(InputCode.KeyEnter, scope))
        {
            Input.ConsumeButton(InputCode.KeyEnter);
            if (state.Style.CommitOnEnter)
            {
                es.SetFlags(ElementFlags.Focus, ElementFlags.None);
                return;
            }
            TextAreaRemoveSelected(ref state);
            text = state.Text.AsReadOnlySpan();
            TextAreaSetText(ref state, UI.InsertText(text, state.CursorIndex, "\n"));
            state.CursorIndex++;
            state.SelectionStart = state.CursorIndex;
            state.BlinkTimer = 0;
            state.DesiredColumn = -1;
        }
        else if (Input.WasButtonPressed(InputCode.KeyEscape, scope))
        {
            Input.ConsumeButton(InputCode.KeyEscape);
            es.SetFlags(ElementFlags.Focus, ElementFlags.None);
            return;
        }
        else if (Input.WasButtonPressed(InputCode.KeyBackspace, true, scope))
        {
            Input.ConsumeButton(InputCode.KeyBackspace);
            if (state.CursorIndex != state.SelectionStart)
            {
                TextAreaRemoveSelected(ref state);
            }
            else if (state.CursorIndex > 0)
            {
                var removeCount = 1;
                if (control)
                {
                    var prevWord = TextBoxFindPrevWordStart(state.Text.AsReadOnlySpan(), state.CursorIndex);
                    removeCount = state.CursorIndex - prevWord;
                }
                TextAreaSetText(ref state, UI.RemoveText(state.Text.AsReadOnlySpan(), state.CursorIndex - removeCount, removeCount));
                state.CursorIndex -= removeCount;
                state.SelectionStart = state.CursorIndex;
            }
            state.BlinkTimer = 0;
            state.DesiredColumn = -1;
        }
        else if (Input.WasButtonPressed(InputCode.KeyDelete, true, scope))
        {
            Input.ConsumeButton(InputCode.KeyDelete);
            if (state.CursorIndex != state.SelectionStart)
            {
                TextAreaRemoveSelected(ref state);
            }
            else if (state.CursorIndex < state.Text.AsReadOnlySpan().Length)
            {
                var removeCount = 1;
                if (control)
                {
                    var nextWord = TextBoxFindNextWordStart(state.Text.AsReadOnlySpan(), state.CursorIndex);
                    removeCount = nextWord - state.CursorIndex;
                }
                TextAreaSetText(ref state, UI.RemoveText(state.Text.AsReadOnlySpan(), state.CursorIndex, removeCount));
            }
            state.BlinkTimer = 0;
            state.DesiredColumn = -1;
        }

        // Character Input
        var input = Input.GetTextInput(scope);
        if (!string.IsNullOrEmpty(input))
        {
            TextAreaRemoveSelected(ref state);
            TextAreaSetText(ref state, UI.InsertText(state.Text.AsReadOnlySpan(), state.CursorIndex, input));
            state.CursorIndex += input.Length;
            state.SelectionStart = state.CursorIndex;
            state.BlinkTimer = 0;
            state.DesiredColumn = -1;
        }

        // Consume all keyboard buttons
        for (var i = (int)InputCode.KeyA; i <= (int)InputCode.KeyRightSuper; i++)
        {
            if (i >= (int)InputCode.KeyLeftShift && i <= (int)InputCode.KeyRightAlt)
                continue;
            Input.ConsumeButton((InputCode)i);
        }
    }

    // --- TextArea helpers ---

    private static ReadOnlySpan<char> TextAreaSetText(ref TextAreaWidgetState state, in UnsafeSpan<char> text)
    {
        var oldHash = state.TextHash;
        state.Text = text;
        state.TextHash = string.GetHashCode(text.AsReadOnlySpan());
        ref var es = ref UI.GetElementState(UI.HotId);
        es.SetFlags(ElementFlags.Changed, oldHash != state.TextHash ? ElementFlags.Changed : ElementFlags.None);
        return text.AsReadOnlySpan();
    }

    private static void TextAreaRemoveSelected(ref TextAreaWidgetState state)
    {
        if (state.CursorIndex == state.SelectionStart) return;
        var start = Math.Min(state.CursorIndex, state.SelectionStart);
        var end = Math.Max(state.CursorIndex, state.SelectionStart);
        TextAreaSetText(ref state, UI.RemoveText(state.Text.AsReadOnlySpan(), start, end - start));
        state.CursorIndex = start;
        state.SelectionStart = start;
    }

    private static void TextAreaUpdateScroll(ref Element e, ref TextAreaWidgetState state)
    {
        var font = (Font)e.Asset!;
        var text = state.Text.AsReadOnlySpan();
        var padding = state.Style.Padding;
        var fontSize = state.Style.FontSize;
        var contentWidth = e.Rect.Width - padding.Horizontal;
        var viewportHeight = e.Rect.Height - padding.Vertical;
        var lineHeight = font.LineHeight * fontSize;

        Span<TextRender.CachedLine> lines = stackalloc TextRender.CachedLine[TextRender.MaxWrappedLines];
        var lineCount = TextRender.GetWrapLines(text, font, fontSize, contentWidth, e.Id, lines);
        var (cursorLine, _) = TextAreaCharToLine(lines, lineCount, state.CursorIndex);

        var cursorY = cursorLine * lineHeight;

        if (cursorY < state.ScrollOffset)
            state.ScrollOffset = cursorY;
        else if (cursorY + lineHeight > state.ScrollOffset + viewportHeight)
            state.ScrollOffset = cursorY + lineHeight - viewportHeight;

        var totalHeight = lineCount * lineHeight;
        if (totalHeight <= viewportHeight)
            state.ScrollOffset = 0;
        else
            state.ScrollOffset = Math.Clamp(state.ScrollOffset, 0, totalHeight - viewportHeight);
    }

    private static (int line, int column) TextAreaCharToLine(
        Span<TextRender.CachedLine> lines, int lineCount, int charIndex)
    {
        for (int i = 0; i < lineCount; i++)
        {
            var lineEnd = (i + 1 < lineCount) ? lines[i + 1].Start : int.MaxValue;
            if (charIndex < lineEnd)
                return (i, charIndex - lines[i].Start);
        }
        return (Math.Max(0, lineCount - 1), 0);
    }

    private static float TextAreaCursorX(
        ReadOnlySpan<char> text, int lineStart, int charIndex, Font font, float fontSize)
    {
        if (charIndex <= lineStart) return 0;
        var len = charIndex - lineStart;
        return TextRender.MeasureLineWidth(text.Slice(lineStart, len), font, fontSize);
    }

    private static int TextAreaCharFromX(
        ReadOnlySpan<char> text, int lineStart, int lineEnd, float targetX, Font font, float fontSize)
    {
        if (targetX <= 0) return lineStart;

        var currentX = 0f;
        var end = Math.Min(lineEnd, text.Length);
        for (var i = lineStart; i < end; i++)
        {
            var ch = text[i];
            if (ch == '\n') return i;
            var glyph = font.GetGlyph(ch);
            var advance = glyph.Advance * fontSize;
            if (i + 1 < end && text[i + 1] != '\n')
                advance += font.GetKerning(ch, text[i + 1]) * fontSize;

            if (targetX < currentX + advance * 0.5f)
                return i;

            currentX += advance;
        }

        return end;
    }

    private static int TextAreaGetPosition(ref Element e, ref TextAreaWidgetState state, ReadOnlySpan<char> text, Font font, float fontSize, Vector2 worldMousePos)
    {
        var padding = state.Style.Padding;
        var localMouse = Vector2.Transform(worldMousePos, e.WorldToLocal);
        var contentWidth = e.Rect.Width - padding.Horizontal;
        var lineHeight = font.LineHeight * fontSize;

        Span<TextRender.CachedLine> lines = stackalloc TextRender.CachedLine[TextRender.MaxWrappedLines];
        var lineCount = TextRender.GetWrapLines(text, font, fontSize, contentWidth, e.Id, lines);
        if (lineCount == 0) return 0;

        var relativeY = localMouse.Y - e.Rect.Y - padding.T + state.ScrollOffset;
        var lineIndex = Math.Clamp((int)(relativeY / lineHeight), 0, lineCount - 1);

        var lineStart = lines[lineIndex].Start;
        var lineEnd = (lineIndex + 1 < lineCount) ? lines[lineIndex + 1].Start : text.Length;
        var relativeX = localMouse.X - e.Rect.X - padding.L;

        return TextAreaCharFromX(text, lineStart, lineEnd, relativeX, font, fontSize);
    }
}
