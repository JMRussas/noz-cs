//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Diagnostics;
using System.Numerics;

namespace NoZ.Widgets;

internal struct TextBoxWidgetState
{
    public TextBoxData Style;
    public int CursorIndex;
    public int SelectionStart;
    public float ScrollOffset;
    public float BlinkTimer;
    public int TextHash;
    public UnsafeSpan<char> Text;
}

public static partial class Widget
{
    private static readonly string PasswordMask = new('*', 64);

    public static readonly WidgetType TextBoxType = Register(
        draw: TextBoxDraw,
        measure: TextBoxMeasure,
        input: TextBoxInput,
        getText: TextBoxGetText,
        setText: TextBoxSetText
    );

    public static bool TextBox(int id, ReadOnlySpan<char> text, TextBoxStyle style,
        ReadOnlySpan<char> placeholder = default, IChangeHandler? handler = null)
    {
        ref var state = ref GetState<TextBoxWidgetState>(id);
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
        UI.Widget(id, TextBoxType, font);

        var changed = es.IsChanged;
        es.SetFlags(ElementFlags.Changed, ElementFlags.None);

        if (changed)
            UI.NotifyChanged(state.TextHash);

        UI.SetLastElement(id);
        HandleChange(handler);
        return changed;
    }

    public static bool TextBox(int id, ReadOnlySpan<char> text, TextBoxStyle style,
        ReadOnlySpan<char> placeholder, out ReadOnlySpan<char> result, IChangeHandler? handler = null)
    {
        var changed = TextBox(id, text, style, placeholder, handler);
        result = changed ? UI.GetElementText(id) : text;
        return changed;
    }

    public static string TextBox(int id, string value, TextBoxStyle style,
        string? placeholder = null, IChangeHandler? handler = null)
    {
        if (TextBox(id, (ReadOnlySpan<char>)value, style, placeholder, handler))
            value = new string(UI.GetElementText(id));
        return value;
    }

    // --- GetText / SetText callbacks ---

    private static ReadOnlySpan<char> TextBoxGetText(int id)
    {
        ref var state = ref GetState<TextBoxWidgetState>(id);
        return state.Text.AsReadOnlySpan();
    }

    private static void TextBoxSetText(int id, ReadOnlySpan<char> value, bool selectAll)
    {
        ref var state = ref GetState<TextBoxWidgetState>(id);
        state.Text = UI.AddText(value);
        state.TextHash = string.GetHashCode(value);
        state.CursorIndex = value.Length;
        state.SelectionStart = selectAll ? 0 : value.Length;
    }

    // --- Measure ---

    private static Vector2 TextBoxMeasure(int id)
    {
        ref var state = ref GetState<TextBoxWidgetState>(id);
        ref var es = ref UI.GetElementState(id);
        ref var e = ref UI.GetElement(es.Index);
        var font = (Font?)e.Asset ?? UI.DefaultFont;
        var text = state.Text.AsReadOnlySpan();
        var fitWidth = TextRender.Measure(text, font, state.Style.FontSize).X + state.Style.Padding.Horizontal;
        return UI.ResolveWidgetSize(id, new Size2(Size.Default, state.Style.Height), new Vector2(fitWidth, state.Style.Height.Value));
    }

    // --- Draw ---

    private static void TextBoxDraw(int id)
    {
        ref var state = ref GetState<TextBoxWidgetState>(id);
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
        TextBoxDrawSelection(ref e, ref state, text, font);
        TextBoxDrawText(ref e, ref state, text, font, data.FontSize, UI.ApplyOpacity(data.TextColor), data.Password);
        TextBoxDrawPlaceholder(ref e, ref state, text, font);
        TextBoxDrawCursor(ref e, ref state, text, font);
    }

    private static void TextBoxDrawSelection(ref Element e, ref TextBoxWidgetState state, in ReadOnlySpan<char> text, Font font)
    {
        ref var es = ref UI.GetElementState(e.Id);
        if (!es.HasFocus) return;
        if (state.CursorIndex == state.SelectionStart) return;

        var padding = state.Style.Padding;
        var start = Math.Min(state.CursorIndex, state.SelectionStart);
        var end = Math.Max(state.CursorIndex, state.SelectionStart);
        var startX = TextBoxMeasureText(text, 0, start, font, state.Style.FontSize) - state.ScrollOffset + padding.L;
        var endX = TextBoxMeasureText(text, 0, end, font, state.Style.FontSize) - state.ScrollOffset + padding.L;
        var rectX = padding.L;
        var rectW = e.Rect.Width - padding.R;
        var drawX = Math.Max(rectX, startX);
        var drawW = Math.Min(rectW, endX) - drawX;

        if (drawW <= 0) return;

        var selectionHeight = e.Rect.Height - padding.Vertical;
        UI.DrawTexturedRect(
            new Rect(drawX + e.Rect.X, e.Rect.Y + padding.T, drawW, selectionHeight),
            e.LocalToWorld, null,
            UI.ApplyOpacity(state.Style.SelectionColor));
    }

    private static void TextBoxDrawText(
        ref Element e,
        ref TextBoxWidgetState state,
        in ReadOnlySpan<char> text,
        Font font,
        float fontSize,
        Color color,
        bool password)
    {
        if (text.Length == 0) return;

        var padding = state.Style.Padding;
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
            -state.ScrollOffset + e.Rect.X + padding.L,
            (contentHeight - font.LineHeight * fontSize) * 0.5f + e.Rect.Y + padding.T);
        var textToRender = password
             ? PasswordMask.AsSpan()[..Math.Min(text.Length, PasswordMask.Length)]
             : text;

        using (Graphics.PushState())
        {
            Graphics.SetScissor(scissor);
            Graphics.SetColor(color);
            Graphics.SetTransform(Matrix3x2.CreateTranslation(textOffset) * e.LocalToWorld);
            TextRender.Draw(textToRender, font, fontSize, order: 2);
            Graphics.ClearScissor();
        }
    }

    private static void TextBoxDrawPlaceholder(ref Element e, ref TextBoxWidgetState state, in ReadOnlySpan<char> text, Font font)
    {
        ref var data = ref state.Style;
        if (text.Length > 0 || data.Placeholder.Length == 0)
            return;

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
            Graphics.SetColor(UI.ApplyOpacity(data.PlaceholderColor));
            Graphics.SetTransform(transform);
            TextRender.Draw(placeholder, font, fontSize);
        }
    }

    private static void TextBoxDrawCursor(ref Element e, ref TextBoxWidgetState state, in ReadOnlySpan<char> text, Font font)
    {
        ref var es = ref UI.GetElementState(e.Id);
        if (!es.HasFocus) return;
        if (state.CursorIndex != state.SelectionStart) return;

        state.BlinkTimer += Time.DeltaTime;
        if ((int)(state.BlinkTimer * 2) % 2 == 1 && !es.IsDragging) return;

        var padding = state.Style.Padding;
        var fontSize = state.Style.FontSize;
        var cursorX = TextBoxMeasureText(text, 0, state.CursorIndex, font, fontSize) - state.ScrollOffset + padding.L;
        var viewportWidth = e.Rect.Width - padding.Horizontal;

        if (cursorX < padding.L || cursorX > padding.L + viewportWidth) return;

        var cursorW = 1f;
        var cursorH = font.LineHeight * fontSize;
        var contentHeight = e.Rect.Height - padding.Vertical;
        var cursorY = (contentHeight - cursorH) * 0.5f + padding.T;

        UI.DrawTexturedRect(
            new Rect(cursorX + e.Rect.X, cursorY + e.Rect.Y, cursorW, cursorH),
            e.LocalToWorld, null,
            UI.ApplyOpacity(Color.White));
    }

    // --- Input ---

    private static void TextBoxInput(int id)
    {
        ref var state = ref GetState<TextBoxWidgetState>(id);
        ref var es = ref UI.GetElementState(id);
        ref var e = ref UI.GetElement(es.Index);

        if (UI.HotId != id)
        {
            es.SetFlags(ElementFlags.Focus | ElementFlags.Dragging, ElementFlags.None);
            state.ScrollOffset = 0.0f;
            return;
        }

        TextBoxHandleInput(ref e, ref state);
        TextBoxUpdateScroll(ref e, ref state);
    }

    private static void TextBoxHandleInput(ref Element e, ref TextBoxWidgetState state)
    {
        var scope = state.Style.Scope;
        var control = Input.IsCtrlDown(scope);
        var shift = Input.IsShiftDown(scope);
        var mousePos = UI.Camera!.ScreenToWorld(Input.MousePosition);
        var localMouse = Vector2.Transform(mousePos, e.WorldToLocal);
        var isMouseOver = e.Rect.Contains(localMouse);
        var fontSize = state.Style.FontSize;
        var font = (Font)e.Asset!;

        ref var es = ref UI.GetElementState(e.Id);
        var text = state.Text.AsReadOnlySpan();

        if (!es.HasFocus)
        {
            es.SetFlags(ElementFlags.Focus, ElementFlags.Focus);
            state.SelectionStart = 0;
            state.CursorIndex = text.Length;
            state.BlinkTimer = 0;
            return;
        }

        // Double Click to Select All
        if (isMouseOver && Input.WasButtonPressed(InputCode.MouseLeftDoubleClick, scope))
        {
            state.SelectionStart = 0;
            state.CursorIndex = text.Length;
            es.SetFlags(ElementFlags.Dragging, ElementFlags.None);
            state.BlinkTimer = 0;
            return;
        }

        // Standard Mouse Input
        if (isMouseOver && Input.WasButtonPressed(InputCode.MouseLeft, scope))
        {
            var mouseIndex = TextBoxGetPosition(ref e, ref state, text, font, fontSize, mousePos);
            state.CursorIndex = mouseIndex;
            state.SelectionStart = mouseIndex;
            es.SetFlags(ElementFlags.Dragging, ElementFlags.Dragging);
            state.BlinkTimer = 0;
        }
        else if (es.IsDragging)
        {
            if (Input.IsButtonDownRaw(InputCode.MouseLeft))
            {
                var mouseIndex = TextBoxGetPosition(ref e, ref state, text, font, fontSize, mousePos);
                state.CursorIndex = mouseIndex;
            }
            else
            {
                es.SetFlags(ElementFlags.Dragging, ElementFlags.None);
            }
        }

        // Keyboard Navigation
        if (Input.WasButtonPressed(InputCode.KeyLeft, true, scope))
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
        }
        else if (Input.WasButtonPressed(InputCode.KeyHome, scope))
        {
            Input.ConsumeButton(InputCode.KeyHome);
            state.CursorIndex = 0;
            if (!shift) state.SelectionStart = state.CursorIndex;
            state.BlinkTimer = 0;
        }
        else if (Input.WasButtonPressed(InputCode.KeyEnd, scope))
        {
            Input.ConsumeButton(InputCode.KeyEnd);
            state.CursorIndex = text.Length;
            if (!shift) state.SelectionStart = state.CursorIndex;
            state.BlinkTimer = 0;
        }
        else if (control && Input.WasButtonPressed(InputCode.KeyA, scope))
        {
            Input.ConsumeButton(InputCode.KeyA);
            state.SelectionStart = 0;
            state.CursorIndex = text.Length;
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
                TextBoxRemoveSelected(ref state);
                TextBoxSetText(ref state, UI.InsertText(state.Text.AsReadOnlySpan(), state.CursorIndex, clipboard));
                state.CursorIndex += clipboard.Length;
                state.SelectionStart = state.CursorIndex;
                state.BlinkTimer = 0;
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
                TextBoxRemoveSelected(ref state);
                state.BlinkTimer = 0;
            }
        }
        else if (Input.WasButtonPressed(InputCode.KeyBackspace, true, scope))
        {
            Input.ConsumeButton(InputCode.KeyBackspace);
            if (state.CursorIndex != state.SelectionStart)
            {
                TextBoxRemoveSelected(ref state);
            }
            else if (state.CursorIndex > 0)
            {
                var removeCount = 1;
                if (control)
                {
                    var prevWord = TextBoxFindPrevWordStart(state.Text.AsReadOnlySpan(), state.CursorIndex);
                    removeCount = state.CursorIndex - prevWord;
                }
                TextBoxSetText(ref state, UI.RemoveText(state.Text.AsReadOnlySpan(), state.CursorIndex - removeCount, removeCount));
                state.CursorIndex -= removeCount;
                state.SelectionStart = state.CursorIndex;
            }
            state.BlinkTimer = 0;
        }
        else if (Input.WasButtonPressed(InputCode.KeyDelete, true, scope))
        {
            Input.ConsumeButton(InputCode.KeyDelete);
            if (state.CursorIndex != state.SelectionStart)
            {
                TextBoxRemoveSelected(ref state);
            }
            else if (state.CursorIndex < state.Text.AsReadOnlySpan().Length)
            {
                var removeCount = 1;
                if (control)
                {
                    var nextWord = TextBoxFindNextWordStart(state.Text.AsReadOnlySpan(), state.CursorIndex);
                    removeCount = nextWord - state.CursorIndex;
                }
                TextBoxSetText(ref state, UI.RemoveText(state.Text.AsReadOnlySpan(), state.CursorIndex, removeCount));
            }
            state.BlinkTimer = 0;
        }
        else if (Input.WasButtonPressed(InputCode.KeyEnter, scope) || Input.WasButtonPressed(InputCode.KeyEscape, scope))
        {
            Input.ConsumeButton(InputCode.KeyEnter);
            Input.ConsumeButton(InputCode.KeyEscape);
            es.SetFlags(ElementFlags.Focus, ElementFlags.None);
            return;
        }

        // Character Input
        var input = Input.GetTextInput(scope);
        if (!string.IsNullOrEmpty(input))
        {
            TextBoxRemoveSelected(ref state);
            TextBoxSetText(ref state, UI.InsertText(state.Text.AsReadOnlySpan(), state.CursorIndex, input));
            state.CursorIndex += input.Length;
            state.SelectionStart = state.CursorIndex;
            state.BlinkTimer = 0;
        }

        // Consume all keyboard buttons to prevent leaking to other systems.
        for (var i = (int)InputCode.KeyA; i <= (int)InputCode.KeyRightSuper; i++)
        {
            if (i >= (int)InputCode.KeyLeftShift && i <= (int)InputCode.KeyRightAlt)
                continue;
            Input.ConsumeButton((InputCode)i);
        }
    }

    // --- TextBox helpers ---

    private static ReadOnlySpan<char> TextBoxSetText(ref TextBoxWidgetState state, in UnsafeSpan<char> text)
    {
        var oldHash = state.TextHash;
        state.Text = text;
        state.TextHash = string.GetHashCode(text.AsReadOnlySpan());
        // Flag change on the element state
        ref var es = ref UI.GetElementState(UI.HotId);
        es.SetFlags(ElementFlags.Changed, oldHash != state.TextHash ? ElementFlags.Changed : ElementFlags.None);
        return text.AsReadOnlySpan();
    }

    private static void TextBoxRemoveSelected(ref TextBoxWidgetState state)
    {
        if (state.CursorIndex == state.SelectionStart) return;
        var start = Math.Min(state.CursorIndex, state.SelectionStart);
        var end = Math.Max(state.CursorIndex, state.SelectionStart);
        TextBoxSetText(ref state, UI.RemoveText(state.Text.AsReadOnlySpan(), start, end - start));
        state.CursorIndex = start;
        state.SelectionStart = start;
    }

    private static void TextBoxUpdateScroll(ref Element e, ref TextBoxWidgetState state)
    {
        var font = (Font)e.Asset!;
        var text = state.Text.AsReadOnlySpan();
        var padding = state.Style.Padding;
        var cursorX = TextBoxMeasureText(text, 0, state.CursorIndex, font, state.Style.FontSize);
        var viewportWidth = e.Rect.Width - padding.Horizontal;
        var cursorScreenX = cursorX - state.ScrollOffset;

        if (cursorScreenX < 0)
            state.ScrollOffset = cursorX;
        else if (cursorScreenX > viewportWidth)
            state.ScrollOffset = cursorX - viewportWidth;

        var totalWidth = TextBoxMeasureText(text, 0, text.Length, font, state.Style.FontSize);
        if (totalWidth < viewportWidth)
            state.ScrollOffset = 0;
        else
            state.ScrollOffset = Math.Clamp(state.ScrollOffset, 0, totalWidth - viewportWidth);
    }

    private static float TextBoxMeasureText(in ReadOnlySpan<char> text, int start, int length, Font font, float fontSize)
    {
        if (length <= 0) return 0;
        return TextRender.Measure(text.Slice(start, length), font, fontSize).X;
    }

    private static int TextBoxGetPosition(ref Element e, ref TextBoxWidgetState state, in ReadOnlySpan<char> text, Font font, float fontSize, Vector2 worldMousePos)
    {
        var padding = state.Style.Padding;
        var localMouse = Vector2.Transform(worldMousePos, e.WorldToLocal);
        var x = localMouse.X - e.Rect.X - padding.L + state.ScrollOffset;

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

    private static int TextBoxFindPrevWordStart(in ReadOnlySpan<char> text, int index)
    {
        if (index <= 0) return 0;
        index--;
        while (index > 0 && char.IsWhiteSpace(text[index])) index--;
        while (index > 0 && !char.IsWhiteSpace(text[index - 1])) index--;
        return index;
    }

    private static int TextBoxFindNextWordStart(in ReadOnlySpan<char> text, int index)
    {
        if (index >= text.Length) return text.Length;
        while (index < text.Length && !char.IsWhiteSpace(text[index])) index++;
        while (index < text.Length && char.IsWhiteSpace(text[index])) index++;
        return index;
    }
}
