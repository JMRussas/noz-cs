//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Diagnostics;
using System.Numerics;

namespace NoZ;

public static partial class UI
{
    private static readonly string PasswordMask = new('*', 64);

    private static void DrawTextBox(ref Element e)
    {
        Debug.Assert(e.Id != 0, "TextBox element must have a valid Id");

        ref var data = ref e.Data.TextBox;
        var isFocused = e.Id != 0 && IsFocused(ref e);
        var border = isFocused ? data.FocusBorder : data.Border;

        UIRender.DrawRect(
            new Rect(Vector2.Transform(e.Rect.Position, e.LocalToWorld), e.Rect.Size),
            data.BackgroundColor,
            border.Radius,
            border.Width,
            border.Color
        );

        if (e.Id == 0)
            return;

        var font = (e.Asset as Font) ?? DefaultFont;
        if (font == null) return;

        ref var es = ref GetElementState(ref e);
        var text = es.Data.TextBox.Text.AsReadOnlySpan();
        DrawTextBoxSelection(ref e, text, font);
        DrawTextBoxText(ref e, text, font, data.FontSize, data.TextColor, data.Password);
        DrawTextBoxPlaceholder(ref e, text, font);
        DrawTextBoxCursor(ref e, text, font);            
    }

    private static void UpdateTextBoxState(ref Element e)
    {
        Debug.Assert(e.Id != 0, "TextBox element must have a valid Id");

        ref var es = ref GetElementState(e.Id);
        if (!IsFocused(ref e))
        {
            es.SetFlags(ElementFlags.Focus | ElementFlags.Dragging, ElementFlags.None);
            es.Data.TextBox.ScrollOffset = 0.0f;
            return;
        }

        HandleTextBoxInput(ref e);
        UpdateTextBoxScroll(ref e);
    }

    private static ReadOnlySpan<char> SetTextBoxText(ref ElementState es, in UnsafeSpan<char> text)
    {
        ref var tb = ref es.Data.TextBox;
        var oldHash = tb.TextHash;
        tb.Text = text;
        tb.TextHash = string.GetHashCode(text.AsReadOnlySpan());
        es.SetFlags(ElementFlags.Changed, oldHash != tb.TextHash ? ElementFlags.Changed : ElementFlags.None);
        return tb.Text.AsReadOnlySpan();
    }

    private static void RemoveSelectedTextBoxText(ref ElementState es)
    {
        ref var tb = ref es.Data.TextBox;
        if (tb.CursorIndex == tb.SelectionStart) return;
        var start = Math.Min(tb.CursorIndex, tb.SelectionStart);
        var end = Math.Max(tb.CursorIndex, tb.SelectionStart);
        SetTextBoxText(ref es, RemoveText(tb.Text.AsReadOnlySpan(), start, end - start));
        tb.CursorIndex = start;
        tb.SelectionStart = start;
    }

    private static void HandleTextBoxInput(ref Element e)
    {
        var scope = e.Data.TextBox.Scope;
        var control = Input.IsCtrlDown(scope);
        var shift = Input.IsShiftDown(scope);
        var mousePos = UI.Camera!.ScreenToWorld(Input.MousePosition);
        var localMouse = Vector2.Transform(mousePos, e.WorldToLocal);
        var isMouseOver = e.Rect.Contains(localMouse);
        var fontSize = e.Data.TextBox.FontSize;
        var font = (Font)e.Asset!;

        ref var es = ref GetElementState(ref e);
        ref var tb = ref es.Data.TextBox;

        if (!es.HasFocus)
        {
            es.SetFlags(ElementFlags.Focus, ElementFlags.Focus);

            // If mouse is down inside, start dragging immediately
            if (isMouseOver && Input.IsButtonDown(InputCode.MouseLeft, scope))
            {
                var mouseIndex = GetTextBoxPosition(ref e, tb.Text.AsReadOnlySpan(), font, fontSize, mousePos);
                tb.CursorIndex = mouseIndex;
                tb.SelectionStart = mouseIndex;
                tb.BlinkTimer = 0;
                es.SetFlags(ElementFlags.Dragging, ElementFlags.Dragging);
            }
        }

        // Double Click to Select All
        if (isMouseOver && Input.WasButtonPressed(InputCode.MouseLeftDoubleClick, scope))
        {
            tb.SelectionStart = 0;
            tb.CursorIndex = tb.Text.AsReadOnlySpan().Length;
            es.SetFlags(ElementFlags.Dragging, ElementFlags.None);
            tb.BlinkTimer = 0;
            return; // Skip standard click handling
        }

        // Standard Mouse Input
        if (isMouseOver && Input.WasButtonPressed(InputCode.MouseLeft, scope))
        {
            var mouseIndex = GetTextBoxPosition(ref e, tb.Text.AsReadOnlySpan(), font, fontSize, mousePos);
            tb.CursorIndex = mouseIndex;
            tb.SelectionStart = mouseIndex;
            es.SetFlags(ElementFlags.Dragging, ElementFlags.Dragging);
            tb.BlinkTimer = 0;
        }
        else if (es.IsDragging)
        {
            if (Input.IsButtonDownRaw(InputCode.MouseLeft))
            {
                var mouseIndex = GetTextBoxPosition(ref e, tb.Text.AsReadOnlySpan(), font, fontSize, mousePos);
                tb.CursorIndex = mouseIndex;
                // Scroll if dragging outside bounds?
                // For now just update cursor.
            }
            else
            {
                es.SetFlags(ElementFlags.Dragging, ElementFlags.None);
            }
        }

        // Keyboard Navigation
        if (Input.WasButtonPressed(InputCode.KeyLeft, true, scope))
        {
            if (control)
            {
                // Move by word
                tb.CursorIndex = FindPrevWordStart(tb.Text.AsReadOnlySpan(), tb.CursorIndex);
            }
            else if (tb.CursorIndex > 0)
            {
                tb.CursorIndex--;
            }
            else if (!shift && tb.CursorIndex != tb.SelectionStart)
            {
                 // Left at start with selection clears selection to start
                 tb.CursorIndex = Math.Min(tb.CursorIndex, tb.SelectionStart);
            }

            if (!shift) tb.SelectionStart = tb.CursorIndex;
            tb.BlinkTimer = 0;
        }
        else if (Input.WasButtonPressed(InputCode.KeyRight, true, scope))
        {
            if (control)
            {
                // Move by word
                tb.CursorIndex = FindNextWordStart(tb.Text.AsReadOnlySpan(), tb.CursorIndex);
            }
            else if (tb.CursorIndex < tb.Text.AsReadOnlySpan().Length)
            {
                tb.CursorIndex++;
            }
            else if (!shift && tb.CursorIndex != tb.SelectionStart)
            {
                 // Right at end with selection clears selection to end
                 tb.CursorIndex = Math.Max(tb.CursorIndex, tb.SelectionStart);
            }

            if (!shift) tb.SelectionStart = tb.CursorIndex;
            tb.BlinkTimer = 0;
        }
        else if (Input.WasButtonPressed(InputCode.KeyHome, scope))
        {
            tb.CursorIndex = 0;
            if (!shift) tb.SelectionStart = tb.CursorIndex;
            tb.BlinkTimer = 0;
        }
        else if (Input.WasButtonPressed(InputCode.KeyEnd, scope))
        {
            tb.CursorIndex = tb.Text.AsReadOnlySpan().Length;
            if (!shift) tb.SelectionStart = tb.CursorIndex;
            tb.BlinkTimer = 0;
        }
        else if (control && Input.WasButtonPressed(InputCode.KeyA, scope))
        {
            tb.SelectionStart = 0;
            tb.CursorIndex = tb.Text.AsReadOnlySpan().Length;
        }
        else if (control && Input.WasButtonPressed(InputCode.KeyC, scope))
        {
            if (tb.CursorIndex != tb.SelectionStart)
            {
                var start = Math.Min(tb.CursorIndex, tb.SelectionStart);
                var length = Math.Abs(tb.CursorIndex - tb.SelectionStart);
                Application.Platform.SetClipboardText(new string(tb.Text.AsReadOnlySpan().Slice(start, length)));
            }
        }
        else if (control && Input.WasButtonPressed(InputCode.KeyV, scope))
        {
            var clipboard = Application.Platform.GetClipboardText();
            if (!string.IsNullOrEmpty(clipboard))
            {
                RemoveSelectedTextBoxText(ref es);
                SetTextBoxText(ref es, InsertText(tb.Text.AsReadOnlySpan(), tb.CursorIndex, clipboard));                
                tb.CursorIndex += clipboard.Length;
                tb.SelectionStart = tb.CursorIndex;
                tb.BlinkTimer = 0;
            }
        }
        else if (control && Input.WasButtonPressed(InputCode.KeyX, scope))
        {
            if (tb.CursorIndex != tb.SelectionStart)
            {
                var start = Math.Min(tb.CursorIndex, tb.SelectionStart);
                var length = Math.Abs(tb.CursorIndex - tb.SelectionStart);
                Application.Platform.SetClipboardText(new string(tb.Text.AsReadOnlySpan().Slice(start, length)));
                RemoveSelectedTextBoxText(ref es);
                tb.BlinkTimer = 0;
            }
        }
        else if (Input.WasButtonPressed(InputCode.KeyBackspace, true, scope))
        {
            if (tb.CursorIndex != tb.SelectionStart)
            {
                RemoveSelectedTextBoxText(ref es);
            }
            else if (tb.CursorIndex > 0)
            {
                var removeCount = 1;
                if (control)
                {
                    var prevWord = FindPrevWordStart(tb.Text.AsReadOnlySpan(), tb.CursorIndex);
                    removeCount = tb.CursorIndex - prevWord;
                }

                SetTextBoxText(ref es, RemoveText(tb.Text.AsReadOnlySpan(), tb.CursorIndex - removeCount, removeCount));
                tb.CursorIndex -= removeCount;
                tb.SelectionStart = tb.CursorIndex;
            }
            tb.BlinkTimer = 0;
        }
        else if (Input.WasButtonPressed(InputCode.KeyDelete, true, scope))
        {
            if (tb.CursorIndex != tb.SelectionStart)
            {
                RemoveSelectedTextBoxText(ref es);
            }
            else if (tb.CursorIndex < tb.Text.AsReadOnlySpan().Length)
            {
                var removeCount = 1;
                if (control)
                {
                    var nextWord = FindNextWordStart(tb.Text.AsReadOnlySpan(), tb.CursorIndex);
                    removeCount = nextWord - tb.CursorIndex;
                }
                SetTextBoxText(ref es, RemoveText(tb.Text.AsReadOnlySpan(), tb.CursorIndex, removeCount));
            }
            tb.BlinkTimer = 0;
        }
        else if (Input.WasButtonPressed(InputCode.KeyEnter, scope) || Input.WasButtonPressed(InputCode.KeyEscape, scope))
        {
            UI.ClearFocus();
            return;
        }

        // Character Input
        var input = Input.GetTextInput(scope);
        if (!string.IsNullOrEmpty(input))
        {
            RemoveSelectedTextBoxText(ref es);
            SetTextBoxText(ref es, InsertText(tb.Text.AsReadOnlySpan(), tb.CursorIndex, input));
            tb.CursorIndex += input.Length;
            tb.SelectionStart = tb.CursorIndex;
            tb.BlinkTimer = 0;
        }
    }

    private static int FindPrevWordStart(in ReadOnlySpan<char> text, int index)
    {
        if (index <= 0) return 0;
        index--;
        while (index > 0 && char.IsWhiteSpace(text[index])) index--;
        while (index > 0 && !char.IsWhiteSpace(text[index - 1])) index--;
        return index;
    }

    private static int FindNextWordStart(in ReadOnlySpan<char> text, int index)
    {
        if (index >= text.Length) return text.Length;
        while (index < text.Length && !char.IsWhiteSpace(text[index])) index++;
        while (index < text.Length && char.IsWhiteSpace(text[index])) index++;
        return index;
    }

    private static void UpdateTextBoxScroll(ref Element e)
    {
        ref var es = ref GetElementState(ref e);
        ref var tb = ref es.Data.TextBox;
        var font = (Font)e.Asset!;
        var text = es.Data.TextBox.Text.AsReadOnlySpan();
        var padding = e.Data.TextBox.Padding;
        var cursorX = MeasureText(text, 0, tb.CursorIndex, font, e.Data.TextBox.FontSize);
        var viewportWidth = e.Rect.Width - padding.Horizontal;
        var cursorScreenX = cursorX - tb.ScrollOffset;

        if (cursorScreenX < 0)
            tb.ScrollOffset = cursorX;
        else if (cursorScreenX > viewportWidth)
            tb.ScrollOffset = cursorX - viewportWidth;

        var totalWidth = MeasureText(text, 0, text.Length, font, e.Data.TextBox.FontSize);
        if (totalWidth < viewportWidth)
            tb.ScrollOffset = 0;
        else
            tb.ScrollOffset = Math.Clamp(tb.ScrollOffset, 0, totalWidth - viewportWidth);
    }

    private static void DrawTextBoxSelection(ref Element e, in ReadOnlySpan<char> text, Font font)
    {
        ref var es = ref GetElementState(ref e);
        ref var tb = ref es.Data.TextBox;

        if (!es.HasFocus) return;
        if (tb.CursorIndex == tb.SelectionStart) return;

        var padding = e.Data.TextBox.Padding;
        var start = Math.Min(tb.CursorIndex, tb.SelectionStart);
        var end = Math.Max(tb.CursorIndex, tb.SelectionStart);
        var startX = MeasureText(text, 0, start, font, e.Data.TextBox.FontSize) - tb.ScrollOffset + padding.L;
        var endX = MeasureText(text, 0, end, font, e.Data.TextBox.FontSize) - tb.ScrollOffset + padding.L;
        var rectX = padding.L;
        var rectW = e.Rect.Width - padding.R;
        var drawX = Math.Max(rectX, startX);
        var drawW = Math.Min(rectW, endX) - drawX;

        if (drawW <= 0) return;

        var pos = Vector2.Transform(new Vector2(drawX + e.Rect.X, e.Rect.Y + padding.T), e.LocalToWorld);
        var selectionHeight = e.Rect.Height - padding.Vertical;
        UIRender.DrawRect(new Rect(pos.X, pos.Y, drawW, selectionHeight), e.Data.TextBox.SelectionColor);
    }

    private static void DrawTextBoxText(
        ref Element e,
        in ReadOnlySpan<char> text,
        Font font,
        float fontSize,
        Color color,
        bool password)
    {
        if (text.Length == 0) return;

        ref var es = ref GetElementState(ref e);
        ref var tb = ref es.Data.TextBox;
        var padding = e.Data.TextBox.Padding;

        var scale = UI.GetUIScale();
        var screenPos = UI.Camera!.WorldToScreen(Vector2.Transform(e.Rect.Position + new Vector2(padding.L, padding.T), e.LocalToWorld));
        var screenHeight = Application.WindowSize.Y;
        var contentWidth = e.Rect.Width - padding.Horizontal;
        var contentHeight = e.Rect.Height - padding.Vertical;
        var scissor = new RectInt(
            (int)screenPos.X,
            (int)(screenHeight - screenPos.Y - contentHeight * scale),
            (int)(contentWidth * scale),
            (int)(contentHeight * scale));

        var textOffset = new Vector2(
            -tb.ScrollOffset + e.Rect.X + padding.L,
            (contentHeight - font.LineHeight * fontSize) * 0.5f + e.Rect.Y + padding.T);
        var textToRender = password
             ? PasswordMask.AsSpan()[..Math.Min(text.Length, PasswordMask!.Length)]
             : text;

        using (Graphics.PushState())
        {
            Graphics.SetScissor(scissor);
            Graphics.SetColor(color);
            Graphics.SetTransform(Matrix3x2.CreateTranslation(textOffset) * e.LocalToWorld);
            TextRender.Draw(textToRender, font, fontSize);
            Graphics.ClearScissor();
        }
    }

    private static void DrawTextBoxPlaceholder(ref Element e, in ReadOnlySpan<char> text, Font font)
    {
        ref var data = ref e.Data.TextBox;
        if (text.Length > 0 || data.Placeholder.Length == 0)
            return;

        ref var state = ref GetElementState(ref e).Data.TextBox;
        var padding = data.Padding;
        var fontSize = data.FontSize;
        var placeholder = new string(data.Placeholder.AsReadOnlySpan());
        var contentHeight = e.Rect.Height - padding.Vertical;
        var placeholderOffset = new Vector2(
            -state.ScrollOffset + e.Rect.X + padding.L,
            (contentHeight - font.LineHeight * fontSize) * 0.5f + e.Rect.Y + padding.T);
        var transform = Matrix3x2.CreateTranslation(placeholderOffset) * e.LocalToWorld;

        using (Graphics.PushState())
        {
            Graphics.SetColor(data.PlaceholderColor);
            Graphics.SetTransform(transform);
            TextRender.Draw(placeholder, font, fontSize);
        }
    }

    private static void DrawTextBoxCursor(ref Element e, in ReadOnlySpan<char> text, Font font)
    {
        ref var es = ref GetElementState(ref e);
        ref var tb = ref es.Data.TextBox;

        if (!es.HasFocus) return;
        if (tb.CursorIndex != tb.SelectionStart) return;

        tb.BlinkTimer += Time.DeltaTime;
        if ((int)(tb.BlinkTimer * 2) % 2 == 1 && !es.IsDragging) return; // Blink off

        var padding = e.Data.TextBox.Padding;
        var fontSize = e.Data.TextBox.FontSize;
        var cursorX = MeasureText(text, 0, tb.CursorIndex, font, fontSize) - tb.ScrollOffset + padding.L;
        var viewportWidth = e.Rect.Width - padding.Horizontal;

        if (cursorX < padding.L || cursorX > padding.L + viewportWidth) return;

        var cursorW = 1f; // 1 pixel width
        var cursorH = font.LineHeight * fontSize;
        var contentHeight = e.Rect.Height - padding.Vertical;
        var cursorY = (contentHeight - cursorH) * 0.5f + padding.T;

        var pos = Vector2.Transform(new Vector2(cursorX + e.Rect.X, cursorY + e.Rect.Y), e.LocalToWorld);
        UIRender.DrawRect(new Rect(pos.X, pos.Y, cursorW, cursorH), Color.White);
    }

    private static float MeasureText(
        in ReadOnlySpan<char> text,
        int start,
        int length,
        Font font,
        float fontSize)
    {
        if (length <= 0) return 0;
        return TextRender.Measure(text.Slice(start, length), font, fontSize).X;
    }

    private static int GetTextBoxPosition(ref Element e, in ReadOnlySpan<char> text, Font font, float fontSize, Vector2 worldMousePos)
    {
        ref var es = ref GetElementState(ref e);
        ref var tb = ref es.Data.TextBox;
        var padding = e.Data.TextBox.Padding;
        var localMouse = Vector2.Transform(worldMousePos, e.WorldToLocal);
        var x = localMouse.X - e.Rect.X - padding.L + tb.ScrollOffset;

        if (x <= 0) return 0;

        var currentX = 0f;

        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            var glyph = font.GetGlyph(ch);
            var advance = glyph.Advance * fontSize;
            if (i + 1 < text.Length)
                advance += font.GetKerning(ch, text[i + 1]) * fontSize;

            if (x < currentX + advance * 0.5f)
                return i;

            currentX += advance;
        }

        return text.Length;
    }

    public static ReadOnlySpan<char> GetTextBoxText(int elementId)
    {
        ref var es = ref GetElementState(elementId);
        ref var e = ref GetElement(es.Index);
        Debug.Assert(e.Type == ElementType.TextBox, "Element is not a TextBox");
        return es.Data.TextBox.Text.AsReadOnlySpan();
    }

    public static void SetTextBoxText(int elementId, string text, bool selectAll = false)
    {
        ref var es = ref GetElementState(elementId);
        ref var tb = ref es.Data.TextBox;
        tb.Text = AddText(text);
        tb.TextHash = string.GetHashCode(text);

        if (selectAll)
        {
            tb.SelectionStart = 0;
            tb.CursorIndex = text.Length;
        }
        else
        {
            tb.CursorIndex = text.Length;
            tb.SelectionStart = text.Length;
        }
    }

    public static void TextBoxEndFrame()
    {
    }
}
