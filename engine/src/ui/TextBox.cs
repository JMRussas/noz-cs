//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;
using NoZ.Platform;

namespace NoZ.Engine.UI;

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
    public bool Password;
    public int TextStart;
    public int TextLength;
    public int PlaceholderStart;
    public int PlaceholderLength;

    public static TextBoxData Default => new()
    {
        Height = 28f,
        FontSize = 16,
        BackgroundColor = new Color(0.22f, 0.22f, 0.22f, 1f),
        TextColor = Color.White,
        PlaceholderColor = new Color(0.4f, 0.4f, 0.4f, 1f),
        SelectionColor = new Color(0.2f, 0.4f, 0.8f, 0.5f),
        PlaceholderStart = 0,
        PlaceholderLength = 0,
        Border = BorderStyle.None,
        FocusBorder = BorderStyle.None,
        Password = false,
        TextStart = 0,
        TextLength = 0
    };
}

internal static class TextBoxElement
{
    private class State
    {
        public int CursorIndex;
        public int SelectionStart;
        public float ScrollOffset;
        public float BlinkTimer;
        public bool Dragging;
        public bool HasFocus;
    }

    private static readonly string PasswordMask = new('*', 64);
    private static readonly Dictionary<int, State> _states = new();
    
    // Native textbox state
    private static byte _focusId;
    private static byte _focusCanvasId;
    private static bool _visible;
    private static bool _renderedThisFrame;

    private static int GetStateKey(byte canvasId, byte elementId) => (canvasId << 8) | elementId;

    private static State GetState(byte canvasId, byte elementId)
    {
        var key = GetStateKey(canvasId, elementId);
        if (!_states.TryGetValue(key, out var state))
        {
            state = new State();
            _states[key] = state;
        }
        return state;
    }

    public static void Measure(ref Element e, Vector2 availableSize)
    {
        //e.MeasuredSize = new Vector2(availableSize.X, e.Data.TextBox.Height);
    }

    public static void Layout(ref Element e, Vector2 size)
    {
        e.Rect.Width = size.X;
        //e.Rect.Height = e.Data.TextBox.Height;
    }

    public static void Draw(ref Element e)
    {
        ref var tb = ref e.Data.TextBox;
        
        // Focus requires matching both element ID and canvas ID
        var isFocused = e.Id != 0 && UI.IsFocus(e.Id, e.CanvasId);
        var border = isFocused ? tb.FocusBorder : tb.Border;

        // Draw background with border
        var pos = Vector2.Transform(Vector2.Zero, e.LocalToWorld);
        UIRender.DrawRect(
            pos.X, pos.Y, e.Rect.Width, e.Rect.Height,
            tb.BackgroundColor,
            border.Radius,
            border.Width,
            border.Color
        );

        // Get the current text (from element state if available, otherwise from the buffer)
        var text = UI.GetElementText(e.Id);
        if (text == null && tb.TextLength > 0)
        {
            text = new string(UI.GetText(tb.TextStart, tb.TextLength));
            UI.SetElementText(e.Id, text);
        }
        text ??= string.Empty;

        var font = UI.DefaultFont;
        if (font == null) return;
        
        var fontSize = tb.FontSize;

        // Custom TextBox Implementation
        if (isFocused)
        {
            var state = GetState(e.CanvasId, e.Id);
            
            // Handle Input
            HandleInput(ref e, state, ref text, font, fontSize);
            
            // Scrolling
            UpdateScroll(ref e, state, text, font, fontSize);

            // Draw Selection
            DrawSelection(ref e, state, text, font, fontSize);

            // Draw Text
            DrawText(ref e, state, text, font, fontSize, tb.TextColor, tb.Password);

            // Draw Cursor
            DrawCursor(ref e, state, text, font, fontSize);

            // Draw Placeholder if empty (even when focused)
            if (string.IsNullOrEmpty(text) && tb.PlaceholderLength > 0)
            {
                var placeholder = new string(UI.GetText(tb.PlaceholderStart, tb.PlaceholderLength));
                var placeholderOffset = new Vector2(-state.ScrollOffset, (e.Rect.Height - font.LineHeight * fontSize) * 0.5f);
                var transform = e.LocalToWorld * Matrix3x2.CreateTranslation(placeholderOffset);
                
                Render.PushState();
                Render.SetColor(tb.PlaceholderColor);
                Render.SetTransform(transform);
                TextRender.Draw(placeholder, font, fontSize);
                Render.PopState();
            }
            
            // Save modified text back
            UI.SetElementText(e.Id, text);
            
            _focusId = e.Id;
            _focusCanvasId = e.CanvasId;
            _renderedThisFrame = true;
            
            // Ensure native textbox is hidden if we are using custom
            if (_visible)
            {
                Application.Platform.HideTextbox();
                _visible = false;
            }
        }
        else
        {
            // Clear focus state if not focused
            var state = GetState(e.CanvasId, e.Id);
            state.HasFocus = false;
            state.Dragging = false;
            state.ScrollOffset = 0;

            // Draw Placeholder if empty
            if (string.IsNullOrEmpty(text) && tb.PlaceholderLength > 0)
            {
                var placeholder = new string(UI.GetText(tb.PlaceholderStart, tb.PlaceholderLength));
                var placeholderOffset = new Vector2(0, (e.Rect.Height - font.LineHeight * fontSize) * 0.5f);
                var transform = e.LocalToWorld * Matrix3x2.CreateTranslation(placeholderOffset);
                
                Render.PushState();
                Render.SetColor(tb.PlaceholderColor);
                Render.SetTransform(transform);
                TextRender.Draw(placeholder, font, fontSize);
                Render.PopState();
            }
            else
            {
                // Draw Text (consistent with focused state)
                DrawText(ref e, state, text, font, fontSize, tb.TextColor, tb.Password);
            }
        }
    }

    private static void HandleInput(ref Element e, State state, ref string text, Font font, float fontSize)
    {
        var control = Input.IsCtrlDown();
        var shift = Input.IsShiftDown();

        // Mouse Input
        var mousePos = UI.Camera!.ScreenToWorld(Input.MousePosition);
        var localMouse = Vector2.Transform(mousePos, e.WorldToLocal);
        var isMouseOver = new Rect(0, 0, e.Rect.Width, e.Rect.Height).Contains(localMouse);
        var isHotCanvas = e.CanvasId == UI.GetHotCanvasId();
        
        // Check if we just got focus this frame
        if (!state.HasFocus)
        {
            state.HasFocus = true;
            // If mouse is down inside, start dragging immediately
            // We verify hot canvas to ensure we don't steal focus/drag if obscured, 
            // though UI system usually handles the focus switch.
            if (isMouseOver && Input.IsButtonDown(InputCode.MouseLeft) && isHotCanvas)
            {
                var mouseIndex = GetCharacterIndexAtPosition(ref e, state, text, font, fontSize, mousePos);
                state.CursorIndex = mouseIndex;
                state.SelectionStart = mouseIndex;
                state.Dragging = true;
                state.BlinkTimer = 0;
            }
        }

        // Double Click to Select All
        if (isMouseOver && Input.WasButtonPressed(InputCode.MouseLeftDoubleClick) && isHotCanvas)
        {
            state.SelectionStart = 0;
            state.CursorIndex = text.Length;
            state.Dragging = false; // Stop dragging to prevent overwrite on next frame
            state.BlinkTimer = 0;
            return; // Skip standard click handling
        }

        // Standard Mouse Input
        if (isMouseOver && Input.WasButtonPressed(InputCode.MouseLeft) && isHotCanvas)
        {
            var mouseIndex = GetCharacterIndexAtPosition(ref e, state, text, font, fontSize, mousePos);
            state.CursorIndex = mouseIndex;
            state.SelectionStart = mouseIndex;
            state.Dragging = true;
            state.BlinkTimer = 0;
        }
        else if (state.Dragging)
        {
            if (Input.IsButtonDown(InputCode.MouseLeft))
            {
                var mouseIndex = GetCharacterIndexAtPosition(ref e, state, text, font, fontSize, mousePos);
                state.CursorIndex = mouseIndex;
                // Scroll if dragging outside bounds?
                // For now just update cursor.
            }
            else
            {
                state.Dragging = false;
            }
        }

        // Keyboard Navigation
        if (Input.WasButtonPressed(InputCode.KeyLeft, true))
        {
            if (control)
            {
                // Move by word
                state.CursorIndex = FindPrevWordStart(text, state.CursorIndex);
            }
            else if (state.CursorIndex > 0)
            {
                state.CursorIndex--;
            }
            else if (!shift && state.CursorIndex != state.SelectionStart)
            {
                 // Left at start with selection clears selection to start
                 state.CursorIndex = Math.Min(state.CursorIndex, state.SelectionStart);
            }

            if (!shift) state.SelectionStart = state.CursorIndex;
            state.BlinkTimer = 0;
        }
        else if (Input.WasButtonPressed(InputCode.KeyRight, true))
        {
            if (control)
            {
                // Move by word
                state.CursorIndex = FindNextWordStart(text, state.CursorIndex);
            }
            else if (state.CursorIndex < text.Length)
            {
                state.CursorIndex++;
            }
            else if (!shift && state.CursorIndex != state.SelectionStart)
            {
                 // Right at end with selection clears selection to end
                 state.CursorIndex = Math.Max(state.CursorIndex, state.SelectionStart);
            }

            if (!shift) state.SelectionStart = state.CursorIndex;
            state.BlinkTimer = 0;
        }
        else if (Input.WasButtonPressed(InputCode.KeyHome))
        {
            state.CursorIndex = 0;
            if (!shift) state.SelectionStart = state.CursorIndex;
            state.BlinkTimer = 0;
        }
        else if (Input.WasButtonPressed(InputCode.KeyEnd))
        {
            state.CursorIndex = text.Length;
            if (!shift) state.SelectionStart = state.CursorIndex;
            state.BlinkTimer = 0;
        }
        else if (control && Input.WasButtonPressed(InputCode.KeyA))
        {
            state.SelectionStart = 0;
            state.CursorIndex = text.Length;
        }
        else if (control && Input.WasButtonPressed(InputCode.KeyC))
        {
            if (state.CursorIndex != state.SelectionStart)
            {
                var start = Math.Min(state.CursorIndex, state.SelectionStart);
                var length = Math.Abs(state.CursorIndex - state.SelectionStart);
                Application.Platform.SetClipboardText(text.Substring(start, length));
            }
        }
        else if (control && Input.WasButtonPressed(InputCode.KeyV))
        {
            var clipboard = Application.Platform.GetClipboardText();
            if (!string.IsNullOrEmpty(clipboard))
            {
                if (state.CursorIndex != state.SelectionStart)
                    DeleteSelection(ref text, state);
                
                text = text.Insert(state.CursorIndex, clipboard);
                state.CursorIndex += clipboard.Length;
                state.SelectionStart = state.CursorIndex;
                state.BlinkTimer = 0;
            }
        }
        else if (control && Input.WasButtonPressed(InputCode.KeyX))
        {
            if (state.CursorIndex != state.SelectionStart)
            {
                var start = Math.Min(state.CursorIndex, state.SelectionStart);
                var length = Math.Abs(state.CursorIndex - state.SelectionStart);
                Application.Platform.SetClipboardText(text.Substring(start, length));
                DeleteSelection(ref text, state);
                state.BlinkTimer = 0;
            }
        }
        
        // Text Modification
        if (Input.WasButtonPressed(InputCode.KeyBackspace, true))
        {
            if (state.CursorIndex != state.SelectionStart)
            {
                DeleteSelection(ref text, state);
            }
            else if (state.CursorIndex > 0)
            {
                var removeCount = 1;
                if (control)
                {
                    var prevWord = FindPrevWordStart(text, state.CursorIndex);
                    removeCount = state.CursorIndex - prevWord;
                }
                
                text = text.Remove(state.CursorIndex - removeCount, removeCount);
                state.CursorIndex -= removeCount;
                state.SelectionStart = state.CursorIndex;
            }
            state.BlinkTimer = 0;
        }
        else if (Input.WasButtonPressed(InputCode.KeyDelete, true))
        {
            if (state.CursorIndex != state.SelectionStart)
            {
                DeleteSelection(ref text, state);
            }
            else if (state.CursorIndex < text.Length)
            {
                var removeCount = 1;
                if (control)
                {
                    var nextWord = FindNextWordStart(text, state.CursorIndex);
                    removeCount = nextWord - state.CursorIndex;
                }
                text = text.Remove(state.CursorIndex, removeCount);
            }
            state.BlinkTimer = 0;
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
            if (state.CursorIndex != state.SelectionStart)
            {
                DeleteSelection(ref text, state);
            }
            
            // Filter non-printable? 
            // Input.GetTextInput usually returns printable chars.
            text = text.Insert(state.CursorIndex, input);
            state.CursorIndex += input.Length;
            state.SelectionStart = state.CursorIndex;
            state.BlinkTimer = 0;
        }
    }

    private static void DeleteSelection(ref string text, State state)
    {
        var start = Math.Min(state.CursorIndex, state.SelectionStart);
        var end = Math.Max(state.CursorIndex, state.SelectionStart);
        text = text.Remove(start, end - start);
        state.CursorIndex = start;
        state.SelectionStart = start;
    }

    private static int FindPrevWordStart(string text, int index)
    {
        if (index <= 0) return 0;
        index--;
        while (index > 0 && char.IsWhiteSpace(text[index])) index--;
        while (index > 0 && !char.IsWhiteSpace(text[index - 1])) index--;
        return index;
    }

    private static int FindNextWordStart(string text, int index)
    {
        if (index >= text.Length) return text.Length;
        while (index < text.Length && !char.IsWhiteSpace(text[index])) index++;
        while (index < text.Length && char.IsWhiteSpace(text[index])) index++;
        return index;
    }

    private static void UpdateScroll(ref Element e, State state, string text, Font font, float fontSize)
    {
        // Calculate X position of cursor relative to text start
        var cursorX = MeasureTextRange(text, 0, state.CursorIndex, font, fontSize);
        
        var viewportWidth = e.Rect.Width;
        var cursorScreenX = cursorX - state.ScrollOffset;

        if (cursorScreenX < 0)
        {
            state.ScrollOffset = cursorX;
        }
        else if (cursorScreenX > viewportWidth)
        {
            state.ScrollOffset = cursorX - viewportWidth;
        }
        
        // Clamp scroll so we don't show empty space at start if text fits
        var totalWidth = MeasureTextRange(text, 0, text.Length, font, fontSize);
        if (totalWidth < viewportWidth)
        {
            state.ScrollOffset = 0;
        }
        else
        {
             state.ScrollOffset = Math.Clamp(state.ScrollOffset, 0, totalWidth - viewportWidth);
        }
    }

    private static void DrawSelection(ref Element e, State state, string text, Font font, float fontSize)
    {
        if (state.CursorIndex == state.SelectionStart) return;

        var start = Math.Min(state.CursorIndex, state.SelectionStart);
        var end = Math.Max(state.CursorIndex, state.SelectionStart);

        var startX = MeasureTextRange(text, 0, start, font, fontSize) - state.ScrollOffset;
        var endX = MeasureTextRange(text, 0, end, font, fontSize) - state.ScrollOffset;
        
        // Clip to element rect
        var rectX = 0f;
        var rectW = e.Rect.Width;
        var drawX = Math.Max(rectX, startX);
        var drawW = Math.Min(rectW, endX) - drawX;

        if (drawW > 0)
        {
            var pos = Vector2.Transform(new Vector2(drawX, 0), e.LocalToWorld);
            UIRender.DrawRect(pos.X, pos.Y, drawW, e.Rect.Height, e.Data.TextBox.SelectionColor);
        }
    }

    private static void DrawText(ref Element e, State state, string text, Font font, float fontSize, Color color, bool password)
    {
        if (string.IsNullOrEmpty(text)) return;
        
        // TODO: Proper Scissor clipping for text that scrolls out
        // For now relying on simple offset rendering, but it might bleed outside element rect if not scissored.
        // UIRender doesn't seem to support setting scissor directly easily without a batch break.
        // UI.DrawElement uses scissor for Scrollables. We can use Render.SetScissor.
        
        var textOffset = new Vector2(-state.ScrollOffset, (e.Rect.Height - font.LineHeight * fontSize) * 0.5f);
        
        // Scissor
        var scale = UI.GetUIScale();
        var screenPos = UI.Camera!.WorldToScreen(Vector2.Transform(Vector2.Zero, e.LocalToWorld));
        var screenHeight = Application.WindowSize.Y;
        var scissorX = (int)screenPos.X;
        var scissorY = (int)(screenHeight - screenPos.Y - e.Rect.Height * scale); // Flip Y
        var scissorW = (int)(e.Rect.Width * scale);
        var scissorH = (int)(e.Rect.Height * scale);
        
        Render.SetScissor(scissorX, scissorY, scissorW, scissorH);
        
        var textToRender = password
             ? PasswordMask[..Math.Min(text.Length, PasswordMask.Length)]
             : text;
             
        // Use direct TextRender.Draw with custom transform
        // UI.DrawText uses Align logic which we want to override/bypass for scrolling
        
        var transform = e.LocalToWorld * Matrix3x2.CreateTranslation(textOffset);
        
        Render.PushState();
        Render.SetColor(color);
        Render.SetTransform(transform);
        TextRender.Draw(textToRender, font, fontSize);
        Render.PopState();
        
        Render.DisableScissor();
    }

    private static void DrawCursor(ref Element e, State state, string text, Font font, float fontSize)
    {
        if (state.CursorIndex != state.SelectionStart) return;

        state.BlinkTimer += Time.DeltaTime;
        if ((int)(state.BlinkTimer * 2) % 2 == 1 && !state.Dragging) return; // Blink off
        
        var cursorX = MeasureTextRange(text, 0, state.CursorIndex, font, fontSize) - state.ScrollOffset;
        
        // Clip
        if (cursorX < 0 || cursorX > e.Rect.Width) return;
        
        var cursorW = 1f; // 1 pixel width
        var cursorH = font.LineHeight * fontSize;
        var cursorY = (e.Rect.Height - cursorH) * 0.5f;

        var pos = Vector2.Transform(new Vector2(cursorX, cursorY), e.LocalToWorld);
        
        UIRender.DrawRect(pos.X, pos.Y, cursorW, cursorH, Color.White);
    }

    private static float MeasureTextRange(string text, int start, int length, Font font, float fontSize)
    {
        if (length <= 0) return 0;
        // Optimization: TextRender.Measure iterates chars.
        // We could optimize this by not creating substring.
        var sub = text.Substring(start, length); 
        return TextRender.Measure(sub, font, fontSize).X;
    }

    private static int GetCharacterIndexAtPosition(ref Element e, State state, string text, Font font, float fontSize, Vector2 worldMousePos)
    {
        var localMouse = Vector2.Transform(worldMousePos, e.WorldToLocal);
        var x = localMouse.X + state.ScrollOffset;
        
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

    public static void EndFrame()
    {
        // Hide textbox if it wasn't rendered this frame (element no longer exists)
        if (_visible && !_renderedThisFrame)
        {
            Application.Platform.HideTextbox();
            _focusId = 0;
            _focusCanvasId = 0;
            _visible = false;
        }
        
        // Reset for next frame
        _renderedThisFrame = false;
    }
}
