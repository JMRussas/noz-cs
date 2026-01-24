//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Diagnostics;
using System.Numerics;

namespace NoZ;

public struct TextBoxData
{
    public Size Height;
    public float FontSize;
    public Color BackgroundColor;
    public Color TextColor;
    public Color PlaceholderColor;
    public Color SelectionColor;
    public BorderStyle Border;
    public BorderStyle FocusBorder;
    public UnsafeSpan<char> Placeholder;
    public bool Password;

    public static TextBoxData Default => new()
    {
        Height = 28f,
        FontSize = 16,
        BackgroundColor = new Color(0.22f, 0.22f, 0.22f, 1f),
        TextColor = Color.White,
        PlaceholderColor = new Color(0.4f, 0.4f, 0.4f, 1f),
        SelectionColor = new Color(0.2f, 0.4f, 0.8f, 0.5f),
        Border = BorderStyle.None,
        FocusBorder = BorderStyle.None,
        Password = false,
        Placeholder = UnsafeSpan<char>.Empty
    };
}

public static partial class UI
{
    private static readonly string PasswordMask = new('*', 64);
    
    //private static byte _focusId;
    //private static byte _focusCanvasId;
    //private static bool _visible;
    //private static bool _renderedThisFrame;

    private static void DrawTextBox(ref Element e)
    {
        Debug.Assert(e.Id != ElementId.None, "TextBox element must have a valid Id");

        ref var data = ref e.Data.TextBox;
        var isHot = e.Id != 0 && IsHotElement(ref e);
        var border = isHot ? data.FocusBorder : data.Border;

        UIRender.DrawRect(
            new Rect(Vector2.Transform(Vector2.Zero, e.LocalToWorld), e.Rect.Size),
            data.BackgroundColor,
            border.Radius,
            border.Width,
            border.Color
        );

        if (e.Id == ElementId.None)
            return;

        var font = e.Font ?? DefaultFont;
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
        Debug.Assert(e.Id != ElementId.None, "TextBox element must have a valid Id");

        ref var es = ref GetElementState(e.CanvasId, e.Id);
        if (!IsHotElement(ref e))
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
        var control = Input.IsCtrlDown();
        var shift = Input.IsShiftDown();
        var mousePos = UI.Camera!.ScreenToWorld(Input.MousePosition);
        var localMouse = Vector2.Transform(mousePos, e.WorldToLocal);
        var isMouseOver = new Rect(0, 0, e.Rect.Width, e.Rect.Height).Contains(localMouse);
        var isHotCanvas = e.CanvasId == _hotCanvasId;
        var fontSize = e.Data.TextBox.FontSize;
        var font = e.Font!;

        ref var es = ref GetElementState(ref e);
        ref var tb = ref es.Data.TextBox;

        if (!es.HasFocus)
        {
            es.SetFlags(ElementFlags.Focus, ElementFlags.Focus);

            // If mouse is down inside, start dragging immediately
            // We verify hot canvas to ensure we don't steal focus/drag if obscured, 
            // though UI system usually handles the focus switch.
            if (isMouseOver && Input.IsButtonDown(InputCode.MouseLeft) && isHotCanvas)
            {
                var mouseIndex = GetTextBoxPosition(ref e, tb.Text.AsReadOnlySpan(), font, fontSize, mousePos);
                tb.CursorIndex = mouseIndex;
                tb.SelectionStart = mouseIndex;
                tb.BlinkTimer = 0;
                es.SetFlags(ElementFlags.Dragging, ElementFlags.Dragging);
            }
        }

        // Double Click to Select All
        if (isMouseOver && Input.WasButtonPressed(InputCode.MouseLeftDoubleClick) && isHotCanvas)
        {
            tb.SelectionStart = 0;
            tb.CursorIndex = tb.Text.AsReadOnlySpan().Length;
            es.SetFlags(ElementFlags.Dragging, ElementFlags.None);
            tb.BlinkTimer = 0;
            return; // Skip standard click handling
        }

        // Standard Mouse Input
        if (isMouseOver && Input.WasButtonPressed(InputCode.MouseLeft) && isHotCanvas)
        {
            var mouseIndex = GetTextBoxPosition(ref e, tb.Text.AsReadOnlySpan(), font, fontSize, mousePos);
            tb.CursorIndex = mouseIndex;
            tb.SelectionStart = mouseIndex;
            es.SetFlags(ElementFlags.Dragging, ElementFlags.Dragging);
            tb.BlinkTimer = 0;
        }
        else if (es.IsDragging)
        {
            if (Input.IsButtonDown(InputCode.MouseLeft))
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
        if (Input.WasButtonPressed(InputCode.KeyLeft, true))
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
        else if (Input.WasButtonPressed(InputCode.KeyRight, true))
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
        else if (Input.WasButtonPressed(InputCode.KeyHome))
        {
            tb.CursorIndex = 0;
            if (!shift) tb.SelectionStart = tb.CursorIndex;
            tb.BlinkTimer = 0;
        }
        else if (Input.WasButtonPressed(InputCode.KeyEnd))
        {
            tb.CursorIndex = tb.Text.AsReadOnlySpan().Length;
            if (!shift) tb.SelectionStart = tb.CursorIndex;
            tb.BlinkTimer = 0;
        }
        else if (control && Input.WasButtonPressed(InputCode.KeyA))
        {
            tb.SelectionStart = 0;
            tb.CursorIndex = tb.Text.AsReadOnlySpan().Length;
        }
        else if (control && Input.WasButtonPressed(InputCode.KeyC))
        {
            if (tb.CursorIndex != tb.SelectionStart)
            {
                var start = Math.Min(tb.CursorIndex, tb.SelectionStart);
                var length = Math.Abs(tb.CursorIndex - tb.SelectionStart);
                Application.Platform.SetClipboardText(new string(tb.Text.AsReadOnlySpan().Slice(start, length)));
            }
        }
        else if (control && Input.WasButtonPressed(InputCode.KeyV))
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
        else if (control && Input.WasButtonPressed(InputCode.KeyX))
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
        else if (Input.WasButtonPressed(InputCode.KeyBackspace, true))
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
        else if (Input.WasButtonPressed(InputCode.KeyDelete, true))
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
        else if (Input.WasButtonPressed(InputCode.KeyEnter) || Input.WasButtonPressed(InputCode.KeyEscape))
        {
            UI.ClearFocus();
            return;
        }

        // Character Input
        var input = Input.GetTextInput();
        if (!string.IsNullOrEmpty(input))
        {
            RemoveSelectedTextBoxText(ref es);
            SetTextBoxText(ref es, InsertText(tb.Text.AsReadOnlySpan(), tb.CursorIndex, input));
            tb.CursorIndex += input.Length;
            tb.SelectionStart = tb.CursorIndex;
            tb.BlinkTimer = 0;
        }

        //// Save modified text back
        //UI.SetElementText(e.Id, text);

        //_renderedThisFrame = true;
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
        var font = e.Font!;
        var text = es.Data.TextBox.Text.AsReadOnlySpan();
        var cursorX = MeasureText(text, 0, tb.CursorIndex, font, e.Data.TextBox.FontSize);
        var viewportWidth = e.Rect.Width;
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

        var start = Math.Min(tb.CursorIndex, tb.SelectionStart);
        var end = Math.Max(tb.CursorIndex, tb.SelectionStart);
        var startX = MeasureText(text, 0, start, font, e.Data.TextBox.FontSize) - tb.ScrollOffset;
        var endX = MeasureText(text, 0, end, font, e.Data.TextBox.FontSize) - tb.ScrollOffset;        
        var rectX = 0f;
        var rectW = e.Rect.Width;
        var drawX = Math.Max(rectX, startX);
        var drawW = Math.Min(rectW, endX) - drawX;

        if (drawW <= 0) return;

        var pos = Vector2.Transform(new Vector2(drawX, 0), e.LocalToWorld);
        UIRender.DrawRect(new Rect(pos.X, pos.Y, drawW, e.Rect.Height), e.Data.TextBox.SelectionColor);
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

        var scale = UI.GetUIScale();
        var screenPos = UI.Camera!.WorldToScreen(Vector2.Transform(Vector2.Zero, e.LocalToWorld));
        var screenHeight = Application.WindowSize.Y;
        var scissor = new RectInt(
            (int)screenPos.X,
            (int)(screenHeight - screenPos.Y - e.Rect.Height * scale),
            (int)(e.Rect.Width * scale),
            (int)(e.Rect.Height * scale));

        var textOffset = new Vector2(-tb.ScrollOffset, (e.Rect.Height - font.LineHeight * fontSize) * 0.5f);
        var textToRender = password
             ? PasswordMask.AsSpan()[..Math.Min(text.Length, PasswordMask!.Length)]
             : text;
             
        using (Graphics.PushState())
        {
            Graphics.SetScissor(scissor);
            Graphics.SetColor(color);
            Graphics.SetTransform(e.LocalToWorld * Matrix3x2.CreateTranslation(textOffset));
            TextRender.Draw(textToRender, font, fontSize);
            Graphics.DisableScissor();
        }
    }

    private static void DrawTextBoxPlaceholder(ref Element e, in ReadOnlySpan<char> text, Font font)
    {
        ref var data = ref e.Data.TextBox;
        if (text.Length > 0 || data.Placeholder.Length == 0)
            return;

        ref var state = ref GetElementState(ref e).Data.TextBox;
        var fontSize = e.Data.TextBox.FontSize;
        var placeholder = new string(data.Placeholder.AsReadOnlySpan());
        var placeholderOffset = new Vector2(-state.ScrollOffset, (e.Rect.Height - font.LineHeight * fontSize) * 0.5f);
        var transform = e.LocalToWorld * Matrix3x2.CreateTranslation(placeholderOffset);

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

        var fontSize = e.Data.TextBox.FontSize;
        var cursorX = MeasureText(text, 0, tb.CursorIndex, font, fontSize) - tb.ScrollOffset;
        
        if (cursorX < 0 || cursorX > e.Rect.Width) return;
        
        var cursorW = 1f; // 1 pixel width
        var cursorH = font.LineHeight * fontSize;
        var cursorY = (e.Rect.Height - cursorH) * 0.5f;

        var pos = Vector2.Transform(new Vector2(cursorX, cursorY), e.LocalToWorld);        
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
        var localMouse = Vector2.Transform(worldMousePos, e.WorldToLocal);
        var x = localMouse.X + tb.ScrollOffset;
        
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

    public static ReadOnlySpan<char> GetTextBoxText(CanvasId canvasId, ElementId elementId)
    {
        ref var es = ref GetElementState(canvasId, elementId);
        ref var e = ref GetElement(es.Index);
        Debug.Assert(e.Type == ElementType.TextBox, "Element is not a TextBox");
        return es.Data.TextBox.Text.AsReadOnlySpan();
    }

    public static void TextBoxEndFrame()
    {
#if false
        // Hide textbox if it wasn't rendered this frame (element no longer exists)
        if (_visible && !_renderedThisFrame)
        {
            Application.Platform.HideTextbox();
            _hotElementId = 0;
            _focusCanvasId = 0;
            _visible = false;
        }
        
        // Reset for next frame
        _renderedThisFrame = false;
#endif
    }
}
