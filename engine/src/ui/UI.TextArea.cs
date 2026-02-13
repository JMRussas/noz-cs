//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Diagnostics;
using System.Numerics;

namespace NoZ;

public static partial class UI
{
    private static void DrawTextArea(ref Element e)
    {
        Debug.Assert(e.Id != 0, "TextArea element must have a valid Id");

        ref var data = ref e.Data.TextArea;
        var isFocused = e.Id != 0 && IsFocused(ref e);
        var border = isFocused ? data.FocusBorder : data.Border;

        DrawTexturedRect(
            e.Rect, e.LocalToWorld, null,
            ApplyOpacity(data.BackgroundColor),
            border.Radius,
            border.Width,
            ApplyOpacity(border.Color)
        );

        if (e.Id == 0)
            return;

        var font = (e.Asset as Font) ?? DefaultFont;
        if (font == null) return;

        ref var es = ref GetElementState(ref e);
        var text = es.Data.TextArea.Text.AsReadOnlySpan();
        DrawTextAreaSelection(ref e, text, font);
        DrawTextAreaText(ref e, text, font, data.FontSize, ApplyOpacity(data.TextColor));
        DrawTextAreaPlaceholder(ref e, text, font);
        DrawTextAreaCursor(ref e, text, font);
    }

    private static void UpdateTextAreaState(ref Element e)
    {
        Debug.Assert(e.Id != 0, "TextArea element must have a valid Id");

        ref var es = ref GetElementState(e.Id);
        if (!IsFocused(ref e))
        {
            es.SetFlags(ElementFlags.Focus | ElementFlags.Dragging, ElementFlags.None);
            es.Data.TextArea.ScrollOffset = 0.0f;
            return;
        }

        HandleTextAreaInput(ref e);
        UpdateTextAreaScroll(ref e);
    }

    private static ReadOnlySpan<char> SetTextAreaText(ref ElementState es, in UnsafeSpan<char> text)
    {
        ref var ta = ref es.Data.TextArea;
        var oldHash = ta.TextHash;
        ta.Text = text;
        ta.TextHash = string.GetHashCode(text.AsReadOnlySpan());
        es.SetFlags(ElementFlags.Changed, oldHash != ta.TextHash ? ElementFlags.Changed : ElementFlags.None);
        return ta.Text.AsReadOnlySpan();
    }

    private static void RemoveSelectedTextAreaText(ref ElementState es)
    {
        ref var ta = ref es.Data.TextArea;
        if (ta.CursorIndex == ta.SelectionStart) return;
        var start = Math.Min(ta.CursorIndex, ta.SelectionStart);
        var end = Math.Max(ta.CursorIndex, ta.SelectionStart);
        SetTextAreaText(ref es, RemoveText(ta.Text.AsReadOnlySpan(), start, end - start));
        ta.CursorIndex = start;
        ta.SelectionStart = start;
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

    private static int GetTextAreaPosition(ref Element e, ReadOnlySpan<char> text, Font font, float fontSize, Vector2 worldMousePos)
    {
        ref var es = ref GetElementState(ref e);
        ref var ta = ref es.Data.TextArea;
        var padding = e.Data.TextArea.Padding;
        var localMouse = Vector2.Transform(worldMousePos, e.WorldToLocal);
        var contentWidth = e.Rect.Width - padding.Horizontal;
        var lineHeight = font.LineHeight * fontSize;

        Span<TextRender.CachedLine> lines = stackalloc TextRender.CachedLine[TextRender.MaxWrappedLines];
        var lineCount = TextRender.GetWrapLines(text, font, fontSize, contentWidth, e.Id, lines);
        if (lineCount == 0) return 0;

        var relativeY = localMouse.Y - e.Rect.Y - padding.T + ta.ScrollOffset;
        var lineIndex = Math.Clamp((int)(relativeY / lineHeight), 0, lineCount - 1);

        var lineStart = lines[lineIndex].Start;
        var lineEnd = (lineIndex + 1 < lineCount) ? lines[lineIndex + 1].Start : text.Length;
        var relativeX = localMouse.X - e.Rect.X - padding.L;

        return TextAreaCharFromX(text, lineStart, lineEnd, relativeX, font, fontSize);
    }

    private static void HandleTextAreaInput(ref Element e)
    {
        var scope = e.Data.TextArea.Scope;
        var control = Input.IsCtrlDown(scope);
        var shift = Input.IsShiftDown(scope);
        var mousePos = UI.Camera!.ScreenToWorld(Input.MousePosition);
        var localMouse = Vector2.Transform(mousePos, e.WorldToLocal);
        var isMouseOver = e.Rect.Contains(localMouse);
        var fontSize = e.Data.TextArea.FontSize;
        var font = (Font)e.Asset!;
        var padding = e.Data.TextArea.Padding;
        var contentWidth = e.Rect.Width - padding.Horizontal;
        var lineHeight = font.LineHeight * fontSize;

        ref var es = ref GetElementState(ref e);
        ref var ta = ref es.Data.TextArea;

        if (!es.HasFocus)
        {
            es.SetFlags(ElementFlags.Focus, ElementFlags.Focus);
            ta.SelectionStart = 0;
            ta.CursorIndex = ta.Text.AsReadOnlySpan().Length;
            ta.BlinkTimer = 0;
            ta.DesiredColumn = -1;
            return;
        }

        // Double Click to Select All
        if (isMouseOver && Input.WasButtonPressed(InputCode.MouseLeftDoubleClick, scope))
        {
            ta.SelectionStart = 0;
            ta.CursorIndex = ta.Text.AsReadOnlySpan().Length;
            es.SetFlags(ElementFlags.Dragging, ElementFlags.None);
            ta.BlinkTimer = 0;
            ta.DesiredColumn = -1;
            return;
        }

        // Standard Mouse Input
        if (isMouseOver && Input.WasButtonPressed(InputCode.MouseLeft, scope))
        {
            var mouseIndex = GetTextAreaPosition(ref e, ta.Text.AsReadOnlySpan(), font, fontSize, mousePos);
            ta.CursorIndex = mouseIndex;
            ta.SelectionStart = mouseIndex;
            es.SetFlags(ElementFlags.Dragging, ElementFlags.Dragging);
            ta.BlinkTimer = 0;
            ta.DesiredColumn = -1;
        }
        else if (es.IsDragging)
        {
            if (Input.IsButtonDownRaw(InputCode.MouseLeft))
            {
                var mouseIndex = GetTextAreaPosition(ref e, ta.Text.AsReadOnlySpan(), font, fontSize, mousePos);
                ta.CursorIndex = mouseIndex;
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
            {
                ta.ScrollOffset -= scrollDelta * lineHeight * 3;
                // Clamping happens in UpdateTextAreaScroll
            }
        }

        var text = ta.Text.AsReadOnlySpan();

        // Get wrap lines for navigation
        Span<TextRender.CachedLine> lines = stackalloc TextRender.CachedLine[TextRender.MaxWrappedLines];
        var lineCount = TextRender.GetWrapLines(text, font, fontSize, contentWidth, e.Id, lines);

        // Keyboard Navigation
        if (Input.WasButtonPressed(InputCode.KeyUp, true, scope))
        {
            Input.ConsumeButton(InputCode.KeyUp);
            var (line, _) = TextAreaCharToLine(lines, lineCount, ta.CursorIndex);
            if (line > 0)
            {
                if (ta.DesiredColumn < 0)
                    ta.DesiredColumn = TextAreaCursorX(text, lines[line].Start, ta.CursorIndex, font, fontSize);
                var prevLine = line - 1;
                var prevLineEnd = lines[prevLine + 1].Start;
                ta.CursorIndex = TextAreaCharFromX(text, lines[prevLine].Start, prevLineEnd, ta.DesiredColumn, font, fontSize);
            }
            else
            {
                ta.CursorIndex = 0;
                ta.DesiredColumn = -1;
            }
            if (!shift) ta.SelectionStart = ta.CursorIndex;
            ta.BlinkTimer = 0;
        }
        else if (Input.WasButtonPressed(InputCode.KeyDown, true, scope))
        {
            Input.ConsumeButton(InputCode.KeyDown);
            var (line, _) = TextAreaCharToLine(lines, lineCount, ta.CursorIndex);
            if (line < lineCount - 1)
            {
                if (ta.DesiredColumn < 0)
                    ta.DesiredColumn = TextAreaCursorX(text, lines[line].Start, ta.CursorIndex, font, fontSize);
                var nextLine = line + 1;
                var nextLineEnd = (nextLine + 1 < lineCount) ? lines[nextLine + 1].Start : text.Length;
                ta.CursorIndex = TextAreaCharFromX(text, lines[nextLine].Start, nextLineEnd, ta.DesiredColumn, font, fontSize);
            }
            else
            {
                ta.CursorIndex = text.Length;
                ta.DesiredColumn = -1;
            }
            if (!shift) ta.SelectionStart = ta.CursorIndex;
            ta.BlinkTimer = 0;
        }
        else if (Input.WasButtonPressed(InputCode.KeyLeft, true, scope))
        {
            Input.ConsumeButton(InputCode.KeyLeft);
            if (control)
                ta.CursorIndex = FindPrevWordStart(text, ta.CursorIndex);
            else if (ta.CursorIndex > 0)
                ta.CursorIndex--;
            else if (!shift && ta.CursorIndex != ta.SelectionStart)
                ta.CursorIndex = Math.Min(ta.CursorIndex, ta.SelectionStart);

            if (!shift) ta.SelectionStart = ta.CursorIndex;
            ta.BlinkTimer = 0;
            ta.DesiredColumn = -1;
        }
        else if (Input.WasButtonPressed(InputCode.KeyRight, true, scope))
        {
            Input.ConsumeButton(InputCode.KeyRight);
            if (control)
                ta.CursorIndex = FindNextWordStart(text, ta.CursorIndex);
            else if (ta.CursorIndex < text.Length)
                ta.CursorIndex++;
            else if (!shift && ta.CursorIndex != ta.SelectionStart)
                ta.CursorIndex = Math.Max(ta.CursorIndex, ta.SelectionStart);

            if (!shift) ta.SelectionStart = ta.CursorIndex;
            ta.BlinkTimer = 0;
            ta.DesiredColumn = -1;
        }
        else if (Input.WasButtonPressed(InputCode.KeyHome, scope))
        {
            Input.ConsumeButton(InputCode.KeyHome);
            if (control)
            {
                ta.CursorIndex = 0;
            }
            else
            {
                var (line, _) = TextAreaCharToLine(lines, lineCount, ta.CursorIndex);
                ta.CursorIndex = lines[line].Start;
            }
            if (!shift) ta.SelectionStart = ta.CursorIndex;
            ta.BlinkTimer = 0;
            ta.DesiredColumn = -1;
        }
        else if (Input.WasButtonPressed(InputCode.KeyEnd, scope))
        {
            Input.ConsumeButton(InputCode.KeyEnd);
            if (control)
            {
                ta.CursorIndex = text.Length;
            }
            else
            {
                var (line, _) = TextAreaCharToLine(lines, lineCount, ta.CursorIndex);
                var lineEnd = (line + 1 < lineCount) ? lines[line + 1].Start : text.Length;
                // Place cursor at end of line content (before newline if present)
                if (lineEnd > 0 && lineEnd <= text.Length && lineEnd > lines[line].Start && text[lineEnd - 1] == '\n')
                    ta.CursorIndex = lineEnd - 1;
                else
                    ta.CursorIndex = lineEnd;
            }
            if (!shift) ta.SelectionStart = ta.CursorIndex;
            ta.BlinkTimer = 0;
            ta.DesiredColumn = -1;
        }
        else if (control && Input.WasButtonPressed(InputCode.KeyA, scope))
        {
            Input.ConsumeButton(InputCode.KeyA);
            ta.SelectionStart = 0;
            ta.CursorIndex = text.Length;
            ta.DesiredColumn = -1;
        }
        else if (control && Input.WasButtonPressed(InputCode.KeyC, scope))
        {
            Input.ConsumeButton(InputCode.KeyC);
            if (ta.CursorIndex != ta.SelectionStart)
            {
                var start = Math.Min(ta.CursorIndex, ta.SelectionStart);
                var length = Math.Abs(ta.CursorIndex - ta.SelectionStart);
                Application.Platform.SetClipboardText(new string(text.Slice(start, length)));
            }
        }
        else if (control && Input.WasButtonPressed(InputCode.KeyV, scope))
        {
            Input.ConsumeButton(InputCode.KeyV);
            var clipboard = Application.Platform.GetClipboardText();
            if (!string.IsNullOrEmpty(clipboard))
            {
                RemoveSelectedTextAreaText(ref es);
                text = ta.Text.AsReadOnlySpan();
                SetTextAreaText(ref es, InsertText(text, ta.CursorIndex, clipboard));
                ta.CursorIndex += clipboard.Length;
                ta.SelectionStart = ta.CursorIndex;
                ta.BlinkTimer = 0;
                ta.DesiredColumn = -1;
            }
        }
        else if (control && Input.WasButtonPressed(InputCode.KeyX, scope))
        {
            Input.ConsumeButton(InputCode.KeyX);
            if (ta.CursorIndex != ta.SelectionStart)
            {
                var start = Math.Min(ta.CursorIndex, ta.SelectionStart);
                var length = Math.Abs(ta.CursorIndex - ta.SelectionStart);
                Application.Platform.SetClipboardText(new string(text.Slice(start, length)));
                RemoveSelectedTextAreaText(ref es);
                ta.BlinkTimer = 0;
                ta.DesiredColumn = -1;
            }
        }
        else if (Input.WasButtonPressed(InputCode.KeyEnter, scope))
        {
            Input.ConsumeButton(InputCode.KeyEnter);
            RemoveSelectedTextAreaText(ref es);
            text = ta.Text.AsReadOnlySpan();
            SetTextAreaText(ref es, InsertText(text, ta.CursorIndex, "\n"));
            ta.CursorIndex++;
            ta.SelectionStart = ta.CursorIndex;
            ta.BlinkTimer = 0;
            ta.DesiredColumn = -1;
        }
        else if (Input.WasButtonPressed(InputCode.KeyEscape, scope))
        {
            Input.ConsumeButton(InputCode.KeyEscape);
            UI.ClearFocus();
            return;
        }
        else if (Input.WasButtonPressed(InputCode.KeyBackspace, true, scope))
        {
            Input.ConsumeButton(InputCode.KeyBackspace);
            if (ta.CursorIndex != ta.SelectionStart)
            {
                RemoveSelectedTextAreaText(ref es);
            }
            else if (ta.CursorIndex > 0)
            {
                var removeCount = 1;
                if (control)
                {
                    var prevWord = FindPrevWordStart(ta.Text.AsReadOnlySpan(), ta.CursorIndex);
                    removeCount = ta.CursorIndex - prevWord;
                }
                SetTextAreaText(ref es, RemoveText(ta.Text.AsReadOnlySpan(), ta.CursorIndex - removeCount, removeCount));
                ta.CursorIndex -= removeCount;
                ta.SelectionStart = ta.CursorIndex;
            }
            ta.BlinkTimer = 0;
            ta.DesiredColumn = -1;
        }
        else if (Input.WasButtonPressed(InputCode.KeyDelete, true, scope))
        {
            Input.ConsumeButton(InputCode.KeyDelete);
            if (ta.CursorIndex != ta.SelectionStart)
            {
                RemoveSelectedTextAreaText(ref es);
            }
            else if (ta.CursorIndex < ta.Text.AsReadOnlySpan().Length)
            {
                var removeCount = 1;
                if (control)
                {
                    var nextWord = FindNextWordStart(ta.Text.AsReadOnlySpan(), ta.CursorIndex);
                    removeCount = nextWord - ta.CursorIndex;
                }
                SetTextAreaText(ref es, RemoveText(ta.Text.AsReadOnlySpan(), ta.CursorIndex, removeCount));
            }
            ta.BlinkTimer = 0;
            ta.DesiredColumn = -1;
        }

        // Character Input
        var input = Input.GetTextInput(scope);
        if (!string.IsNullOrEmpty(input))
        {
            RemoveSelectedTextAreaText(ref es);
            SetTextAreaText(ref es, InsertText(ta.Text.AsReadOnlySpan(), ta.CursorIndex, input));
            ta.CursorIndex += input.Length;
            ta.SelectionStart = ta.CursorIndex;
            ta.BlinkTimer = 0;
            ta.DesiredColumn = -1;
        }

        // Consume all keyboard buttons to prevent leaking to other systems.
        for (var i = (int)InputCode.KeyA; i <= (int)InputCode.KeyRightSuper; i++)
        {
            if (i >= (int)InputCode.KeyLeftShift && i <= (int)InputCode.KeyRightAlt)
                continue;
            Input.ConsumeButton((InputCode)i);
        }
    }

    private static void UpdateTextAreaScroll(ref Element e)
    {
        ref var es = ref GetElementState(ref e);
        ref var ta = ref es.Data.TextArea;
        var font = (Font)e.Asset!;
        var text = ta.Text.AsReadOnlySpan();
        var padding = e.Data.TextArea.Padding;
        var fontSize = e.Data.TextArea.FontSize;
        var contentWidth = e.Rect.Width - padding.Horizontal;
        var viewportHeight = e.Rect.Height - padding.Vertical;
        var lineHeight = font.LineHeight * fontSize;

        Span<TextRender.CachedLine> lines = stackalloc TextRender.CachedLine[TextRender.MaxWrappedLines];
        var lineCount = TextRender.GetWrapLines(text, font, fontSize, contentWidth, e.Id, lines);
        var (cursorLine, _) = TextAreaCharToLine(lines, lineCount, ta.CursorIndex);

        var cursorY = cursorLine * lineHeight;

        if (cursorY < ta.ScrollOffset)
            ta.ScrollOffset = cursorY;
        else if (cursorY + lineHeight > ta.ScrollOffset + viewportHeight)
            ta.ScrollOffset = cursorY + lineHeight - viewportHeight;

        var totalHeight = lineCount * lineHeight;
        if (totalHeight <= viewportHeight)
            ta.ScrollOffset = 0;
        else
            ta.ScrollOffset = Math.Clamp(ta.ScrollOffset, 0, totalHeight - viewportHeight);
    }

    private static void DrawTextAreaSelection(ref Element e, in ReadOnlySpan<char> text, Font font)
    {
        ref var es = ref GetElementState(ref e);
        ref var ta = ref es.Data.TextArea;

        if (!es.HasFocus) return;
        if (ta.CursorIndex == ta.SelectionStart) return;

        var data = e.Data.TextArea;
        var padding = data.Padding;
        var fontSize = data.FontSize;
        var contentWidth = e.Rect.Width - padding.Horizontal;
        var contentHeight = e.Rect.Height - padding.Vertical;
        var lineHeight = font.LineHeight * fontSize;

        Span<TextRender.CachedLine> lines = stackalloc TextRender.CachedLine[TextRender.MaxWrappedLines];
        var lineCount = TextRender.GetWrapLines(text, font, fontSize, contentWidth, e.Id, lines);

        var selStart = Math.Min(ta.CursorIndex, ta.SelectionStart);
        var selEnd = Math.Max(ta.CursorIndex, ta.SelectionStart);

        var (startLine, _) = TextAreaCharToLine(lines, lineCount, selStart);
        var (endLine, _) = TextAreaCharToLine(lines, lineCount, selEnd);

        var scale = GetUIScale();
        var screenPos = Camera!.WorldToScreen(
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
                var lineY = line * lineHeight - ta.ScrollOffset + padding.T;

                // Skip lines outside visible area
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

                DrawTexturedRect(
                    new Rect(selX0 + padding.L + e.Rect.X, lineY + e.Rect.Y, selX1 - selX0, lineHeight),
                    e.LocalToWorld, null,
                    ApplyOpacity(data.SelectionColor));
            }

            Graphics.ClearScissor();
        }
    }

    private static void DrawTextAreaText(
        ref Element e,
        in ReadOnlySpan<char> text,
        Font font,
        float fontSize,
        Color color)
    {
        if (text.Length == 0) return;

        ref var es = ref GetElementState(ref e);
        ref var ta = ref es.Data.TextArea;
        var padding = e.Data.TextArea.Padding;

        var scale = GetUIScale();
        var screenPos = Camera!.WorldToScreen(
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
            -ta.ScrollOffset + e.Rect.Y + padding.T);

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

    private static void DrawTextAreaPlaceholder(ref Element e, in ReadOnlySpan<char> text, Font font)
    {
        ref var data = ref e.Data.TextArea;
        if (text.Length > 0 || data.Placeholder.Length == 0)
            return;

        ref var state = ref GetElementState(ref e).Data.TextArea;
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
            Graphics.SetColor(ApplyOpacity(data.PlaceholderColor));
            Graphics.SetTransform(transform);
            TextRender.DrawWrapped(placeholder, font, fontSize, contentWidth,
                contentWidth, 0f, maxHeight: 0, order: 2, cacheId: 0);
        }
    }

    private static void DrawTextAreaCursor(ref Element e, in ReadOnlySpan<char> text, Font font)
    {
        ref var es = ref GetElementState(ref e);
        ref var ta = ref es.Data.TextArea;

        if (!es.HasFocus) return;
        if (ta.CursorIndex != ta.SelectionStart) return;

        ta.BlinkTimer += Time.DeltaTime;
        if ((int)(ta.BlinkTimer * 2) % 2 == 1 && !es.IsDragging) return;

        var padding = e.Data.TextArea.Padding;
        var fontSize = e.Data.TextArea.FontSize;
        var contentWidth = e.Rect.Width - padding.Horizontal;
        var contentHeight = e.Rect.Height - padding.Vertical;
        var lineHeight = font.LineHeight * fontSize;

        Span<TextRender.CachedLine> lines = stackalloc TextRender.CachedLine[TextRender.MaxWrappedLines];
        var lineCount = TextRender.GetWrapLines(text, font, fontSize, contentWidth, e.Id, lines);
        var (cursorLine, _) = TextAreaCharToLine(lines, lineCount, ta.CursorIndex);

        var cursorX = TextAreaCursorX(text, lines[cursorLine].Start, ta.CursorIndex, font, fontSize);
        var cursorY = cursorLine * lineHeight - ta.ScrollOffset;

        // Skip if cursor is outside visible area
        if (cursorY + lineHeight < 0 || cursorY > contentHeight) return;

        DrawTexturedRect(
            new Rect(cursorX + padding.L + e.Rect.X, cursorY + padding.T + e.Rect.Y, 1f, lineHeight),
            e.LocalToWorld, null,
            ApplyOpacity(Color.White));
    }

    public static ReadOnlySpan<char> GetTextAreaText(int elementId)
    {
        ref var es = ref GetElementState(elementId);
        ref var e = ref GetElement(es.Index);
        Debug.Assert(e.Type == ElementType.TextArea, "Element is not a TextArea");
        return es.Data.TextArea.Text.AsReadOnlySpan();
    }

    public static void SetTextAreaText(int elementId, string text, bool selectAll = false)
    {
        ref var es = ref GetElementState(elementId);
        ref var ta = ref es.Data.TextArea;
        ta.Text = AddText(text);
        ta.TextHash = string.GetHashCode(text);

        if (selectAll)
        {
            ta.SelectionStart = 0;
            ta.CursorIndex = text.Length;
        }
        else
        {
            ta.CursorIndex = text.Length;
            ta.SelectionStart = text.Length;
        }
    }
}
