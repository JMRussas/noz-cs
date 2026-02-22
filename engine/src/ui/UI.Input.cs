//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

// #define NOZ_UI_DEBUG

using System.Numerics;

namespace NoZ;

public static partial class UI
{
    private static int _mouseLeftElementId;
    private static int _mouseDoubleClickElementId;
    private static int _mouseRightElementId;
    private static bool _mouseLeftPressed;
    private static bool _mouseRightPressed;
    private static bool _mouseLeftDown;
    private static bool _mouseLeftDoubleClickPressed;

    public static Vector2 MouseWorldPosition { get; private set; }

    private static void HandleInput()
    {
        var mouse = Camera!.ScreenToWorld(Input.MousePosition);
        MouseWorldPosition = mouse;
        _mouseLeftPressed = Input.WasButtonPressedRaw(InputCode.MouseLeft);
        _mouseLeftDown = Input.IsButtonDownRaw(InputCode.MouseLeft);
        _mouseLeftDoubleClickPressed = Input.WasButtonPressedRaw(InputCode.MouseLeftDoubleClick);
        _mouseLeftElementId = 0;
        _mouseRightPressed = Input.WasButtonPressedRaw(InputCode.MouseRight);
        _mouseRightElementId = 0;
        _mouseDoubleClickElementId = 0;

        LogUI("Input:", values: [("FocusElementId", _focusElementId, true)]);

        HandlePopupAutoClose(mouse);

        // When multiple popups are open, determine which popup the mouse is over
        // so that child popups block input to elements in parent popups
        _inputPopupIndex = -1;
        if (_activePopupCount > 1)
        {
            for (var i = _popupCount - 1; i >= 0; i--)
            {
                ref var popup = ref _elements[_popups[i]];
                if (!popup.Data.Popup.Interactive) continue;
                var localMouse = Vector2.Transform(mouse, popup.WorldToLocal);
                if (popup.Rect.Contains(localMouse))
                {
                    _inputPopupIndex = _popups[i];
                    break;
                }
            }
        }

        // Process all elements
        HandleElementInput(mouse);

        // Process scrollbar input BEFORE consuming buttons
        HandleScrollbarInput(mouse);

        // Don't consume mouse buttons when hovering over a Scene element (pass-through),
        // but still consume when popups are open or scrollbar is being used.
        if (!_mouseOverScene || _activePopupCount > 0)
        {
            if (_mouseLeftElementId != 0 || _activePopupCount > 0 || _scrollbarDragging)
                Input.ConsumeButton(InputCode.MouseLeft);

            if (_mouseDoubleClickElementId != 0 || _activePopupCount > 0)
                Input.ConsumeButton(InputCode.MouseLeftDoubleClick);

            if (_mouseRightElementId != 0 || _activePopupCount > 0)
                Input.ConsumeButton(InputCode.MouseRight);
        }
        HandleScrollableDrag(mouse);
        HandleMouseWheelScroll(mouse);
        HandleCursor();
    }

    private static void HandlePopupAutoClose(Vector2 mouse)
    {
        _closePopups = false;
        if (_popupCount == 0) return;

        var anyAutoClose = false;
        for (var i = 0; i < _popupCount; i++)
        {
            ref var popup = ref _elements[_popups[i]];
            if (popup.Data.Popup.AutoClose) { anyAutoClose = true; break; }
        }
        if (!anyAutoClose) return;

        // Escape closes auto-close popups
        if (Input.WasButtonPressedRaw(InputCode.KeyEscape))
        {
            _closePopups = true;
            Input.ConsumeButton(InputCode.KeyEscape);
        }

        // Click outside closes auto-close popups
        if (_mouseLeftPressed)
        {
            var clickInsideAutoClosePopup = false;
            for (var i = 0; i < _popupCount; i++)
            {
                ref var popup = ref _elements[_popups[i]];
                if (!popup.Data.Popup.AutoClose) continue;
                var localMouse = Vector2.Transform(mouse, popup.WorldToLocal);
                if (popup.Rect.Contains(localMouse))
                {
                    clickInsideAutoClosePopup = true;
                    break;
                }
            }

            if (!clickInsideAutoClosePopup)
            {
                _closePopups = true;
                _mouseLeftPressed = false;
                Input.ConsumeButton(InputCode.MouseLeft);
            }
        }
    }

    private static bool GetScrollbarThumbRect(ref Element e, out Rect thumbRect)
    {
        thumbRect = Rect.Zero;
        ref var s = ref e.Data.Scrollable;

        var viewportHeight = e.Rect.Height;
        var maxScroll = Math.Max(0, s.ContentHeight - viewportHeight);

        if (s.ScrollbarVisibility == ScrollbarVisibility.Never || maxScroll <= 0)
            return false;

        var pos = Vector2.Transform(e.Rect.Position, e.LocalToWorld);
        var trackX = pos.X + e.Rect.Width - s.ScrollbarWidth - s.ScrollbarPadding;
        var trackY = pos.Y + s.ScrollbarPadding;
        var trackH = viewportHeight - s.ScrollbarPadding * 2;

        var thumbHeightRatio = viewportHeight / s.ContentHeight;
        var thumbH = Math.Max(s.ScrollbarMinThumbHeight, trackH * thumbHeightRatio);
        var scrollRatio = s.Offset / maxScroll;
        var thumbY = trackY + scrollRatio * (trackH - thumbH);

        thumbRect = new Rect(trackX, thumbY, s.ScrollbarWidth, thumbH);
        return true;
    }

    private static bool GetScrollbarTrackRect(ref Element e, out Rect trackRect)
    {
        trackRect = Rect.Zero;
        ref var s = ref e.Data.Scrollable;

        var viewportHeight = e.Rect.Height;
        var maxScroll = Math.Max(0, s.ContentHeight - viewportHeight);

        if (s.ScrollbarVisibility == ScrollbarVisibility.Never)
            return false;
        if (s.ScrollbarVisibility == ScrollbarVisibility.Auto && maxScroll <= 0)
            return false;

        var pos = Vector2.Transform(e.Rect.Position, e.LocalToWorld);
        var trackX = pos.X + e.Rect.Width - s.ScrollbarWidth - s.ScrollbarPadding;
        var trackY = pos.Y + s.ScrollbarPadding;
        var trackH = viewportHeight - s.ScrollbarPadding * 2;

        trackRect = new Rect(trackX, trackY, s.ScrollbarWidth, trackH);
        return true;
    }

    private static void HandleScrollbarInput(Vector2 mouse)
    {
        // Handle ongoing scrollbar drag
        if (_scrollbarDragging)
        {
            if (!_mouseLeftDown)
            {
                _scrollbarDragging = false;
                return;
            }

            ref var es = ref GetElementState(_scrollbarDragElementId);
            ref var e = ref _elements[es.Index];

            ref var s = ref e.Data.Scrollable;
            var viewportHeight = e.Rect.Height;
            var maxScroll = Math.Max(0, s.ContentHeight - viewportHeight);
            if (maxScroll <= 0) return;

            var trackH = viewportHeight - s.ScrollbarPadding * 2;
            var thumbHeightRatio = viewportHeight / s.ContentHeight;
            var thumbH = Math.Max(s.ScrollbarMinThumbHeight, trackH * thumbHeightRatio);
            var availableTrackSpace = trackH - thumbH;

            if (availableTrackSpace > 0)
            {
                var mouseDeltaY = mouse.Y - _scrollbarDragStartMouseY;
                var scrollDelta = (mouseDeltaY / availableTrackSpace) * maxScroll;
                var newOffset = Math.Clamp(_scrollbarDragStartOffset + scrollDelta, 0, maxScroll);

                s.Offset = newOffset;
                es.Data.Scrollable.Offset = newOffset;
            }
            return;
        }

        // Check for new scrollbar interaction
        if (!_mouseLeftPressed) return;

        for (var i = _elementCount - 1; i >= 0; i--)
        {
            ref var e = ref _elements[i];
            if (e.Type != ElementType.Scrollable || e.Id == 0)
                continue;

            // When popups are open, only interact with scrollbars inside popups
            if (_activePopupCount > 0 && !IsInsidePopup(i))
                continue;

            // Check if mouse is over the scrollbar thumb
            var hasThumb = GetScrollbarThumbRect(ref e, out var thumbRect);
            var thumbContains = hasThumb && thumbRect.Contains(mouse);

            if (thumbContains)
            {
                _scrollbarDragging = true;
                _scrollbarDragElementId = e.Id;
                _scrollbarDragStartOffset = e.Data.Scrollable.Offset;
                _scrollbarDragStartMouseY = mouse.Y;
                ClearElementPress();
                return;
            }

            // Check if mouse is over the scrollbar track (page scroll)
            if (GetScrollbarTrackRect(ref e, out var trackRect) && trackRect.Contains(mouse))
            {
                ref var s = ref e.Data.Scrollable;
                var viewportHeight = e.Rect.Height;
                var maxScroll = Math.Max(0, s.ContentHeight - viewportHeight);

                if (maxScroll > 0)
                {
                    var trackY = trackRect.Y;
                    var clickRelativeY = mouse.Y - trackY;
                    var clickRatio = clickRelativeY / trackRect.Height;

                    // Scroll towards click position by one page
                    var targetOffset = clickRatio * maxScroll;
                    var pageAmount = viewportHeight * 0.9f;
                    var newOffset = s.Offset < targetOffset
                        ? Math.Min(s.Offset + pageAmount, targetOffset)
                        : Math.Max(s.Offset - pageAmount, targetOffset);
                    newOffset = Math.Clamp(newOffset, 0, maxScroll);

                    s.Offset = newOffset;
                    ref var state = ref GetElementState(e.Id);
                    state.Data.Scrollable.Offset = newOffset;
                }
                ClearElementPress();
                Input.ConsumeButton(InputCode.MouseLeft);
                return;
            }
        }
    }

    private static void ClearElementPress()
    {
        if (_mouseLeftElementId != 0)
        {
            ref var es = ref GetElementState(_mouseLeftElementId);
            es.SetFlags(ElementFlags.Pressed, ElementFlags.None);
            _mouseLeftElementId = 0;
        }
    }

    private static bool IsInsideNonInteractivePopup(int elementIndex)
    {
        for (var i = 0; i < _popupCount; i++)
        {
            var popupIndex = _popups[i];
            ref var popup = ref _elements[popupIndex];
            if (!popup.Data.Popup.Interactive && elementIndex >= popupIndex && elementIndex < popup.NextSiblingIndex)
                return true;
        }
        return false;
    }

    private static bool IsInsidePopup(int elementIndex)
    {
        // When the mouse is over a specific popup, only that popup receives input
        if (_inputPopupIndex >= 0)
        {
            ref var popup = ref _elements[_inputPopupIndex];
            return elementIndex >= _inputPopupIndex && elementIndex < popup.NextSiblingIndex;
        }

        for (var i = 0; i < _popupCount; i++)
        {
            var popupIndex = _popups[i];
            ref var popup = ref _elements[popupIndex];
            if (!popup.Data.Popup.Interactive) continue;
            if (elementIndex >= popupIndex && elementIndex < popup.NextSiblingIndex)
                return true;
        }
        return false;
    }

    private static bool _mouseOverScene;

    private static void HandleElementInput(Vector2 mouse)
    {
        _mouseOverScene = false;

        // Process elements in reverse order so later elements (on top) get input priority
        for (var elementIndex = _elementCount - 1; elementIndex >= 0; elementIndex--)
        {
            ref var e = ref _elements[elementIndex];
            if (e.Id == 0) continue;

            ref var es = ref GetElementState(e.Id);
            es.Rect = e.Rect;
            es.LocalToWorld = e.LocalToWorld;

            // Non-interactive popups and their children don't process input
            if (IsInsideNonInteractivePopup(elementIndex))
            {
                es.SetFlags(ElementFlags.Hovered | ElementFlags.Down | ElementFlags.Pressed | ElementFlags.DoubleClick | ElementFlags.RightClick | ElementFlags.HoverChanged, ElementFlags.None);
                continue;
            }

            // When popups are open, only process input for elements inside popups
            if (_activePopupCount > 0 && !IsInsidePopup(elementIndex))
            {
                es.SetFlags(ElementFlags.Hovered | ElementFlags.Down | ElementFlags.Pressed | ElementFlags.DoubleClick | ElementFlags.RightClick | ElementFlags.HoverChanged, ElementFlags.None);
                continue;
            }

            if (e.Type == ElementType.TextBox)
                UpdateTextBoxState(ref e);
            else if (e.Type == ElementType.TextArea)
                UpdateTextAreaState(ref e);

            var localMouse = Vector2.Transform(mouse, e.WorldToLocal);
            var mouseOver = e.Rect.Contains(localMouse);

            // Check if the element is clipped by a scrollable ancestor
            if (mouseOver)
            {
                var parentIdx = e.ParentIndex;
                while (parentIdx >= 0)
                {
                    ref var parent = ref _elements[parentIdx];
                    if (parent.Type == ElementType.Scrollable)
                    {
                        var parentLocalMouse = Vector2.Transform(mouse, parent.WorldToLocal);
                        if (!parent.Rect.Contains(parentLocalMouse))
                        {
                            mouseOver = false;
                            break;
                        }
                    }
                    parentIdx = parent.ParentIndex;
                }
            }

            var wasHovered = es.IsHovered;
            es.SetFlags(ElementFlags.Hovered, mouseOver ? ElementFlags.Hovered : ElementFlags.None);
            es.SetFlags(ElementFlags.HoverChanged, wasHovered != es.IsHovered ? ElementFlags.HoverChanged : ElementFlags.None);
            es.SetFlags(ElementFlags.Down, mouseOver && _mouseLeftDown ? ElementFlags.Down : ElementFlags.None);

            // Scene elements pass input through to the application; when the mouse
            // is over a Scene, prevent parent elements from consuming input too.
            if (mouseOver && e.Type == ElementType.Scene)
                _mouseOverScene = true;

            var consumesInput = e.Type != ElementType.Scene && !_mouseOverScene;

            if (mouseOver && _mouseLeftDoubleClickPressed && _mouseDoubleClickElementId == 0 && consumesInput)
            {
                _mouseDoubleClickElementId = e.Id;
                es.SetFlags(ElementFlags.DoubleClick, ElementFlags.DoubleClick);
            }
            else
                es.SetFlags(ElementFlags.DoubleClick, ElementFlags.None);

            if (mouseOver && _mouseLeftPressed && _mouseLeftElementId == 0 && consumesInput)
            {
                es.SetFlags(ElementFlags.Pressed, ElementFlags.Pressed);
                _mouseLeftElementId = e.Id;
                _pendingFocusElementId = e.Id;
            }
            else if (es.IsPressed)
            {
                es.SetFlags(ElementFlags.Pressed, ElementFlags.None);
            }

            if (mouseOver && _mouseRightPressed && _mouseRightElementId == 0 && consumesInput)
            {
                es.SetFlags(ElementFlags.RightClick, ElementFlags.RightClick);
                _mouseRightElementId = e.Id;
            }
            else if (es.IsPressed)
            {
                es.SetFlags(ElementFlags.RightClick, ElementFlags.None);
            }
        }
    }

    private static void HandleScrollableDrag(Vector2 mouse)
    {
        // Don't start/continue content drag if scrollbar is being dragged
        if (_scrollbarDragging)
            return;

        if (!_mouseLeftDown)
        {
            _activeScrollId = 0;
        }
        else if (_activeScrollId != 0)
        {
            var deltaY = _lastScrollMouseY - mouse.Y;
            _lastScrollMouseY = mouse.Y;

            for (var i = 0; i < _elementCount; i++)
            {
                ref var e = ref _elements[i];
                if (e.Type == ElementType.Scrollable && e.Id == _activeScrollId)
                {
                    ref var state = ref GetElementState(e.Id);

                    var newOffset = e.Data.Scrollable.Offset + deltaY;
                    var maxScroll = Math.Max(0, e.Data.Scrollable.ContentHeight - e.Rect.Height);
                    newOffset = Math.Clamp(newOffset, 0, maxScroll);

                    e.Data.Scrollable.Offset = newOffset;
                    state.Data.Scrollable.Offset = newOffset;
                    break;
                }
            }
        }
        else if (_mouseLeftPressed)
        {
            for (var i = _elementCount; i > 0; i--)
            {
                ref var e = ref _elements[i - 1];
                if (e.Type == ElementType.Scrollable && e.Id != 0)
                {
                    // When popups are open, only allow starting scroll inside popups
                    if (_activePopupCount > 0 && !IsInsidePopup(i - 1))
                        continue;

                    ref var state = ref GetElementState(e.Id);
                    if ((state.Flags & ElementFlags.Pressed) != 0)
                    {
                        _activeScrollId = e.Id;
                        _lastScrollMouseY = mouse.Y;
                        break;
                    }
                }
            }
        }
    }

    private static void HandleMouseWheelScroll(Vector2 mouse)
    {
        var scrollDelta = Input.GetAxisValue(InputCode.MouseScrollY);
        if (scrollDelta == 0) return;

        for (var i = _elementCount; i > 0; i--)
        {
            ref var e = ref _elements[i - 1];
            if (e.Type != ElementType.Scrollable || e.Id == 0)
                continue;

            // When popups are open, only allow scrolling inside popups
            if (_activePopupCount > 0 && !IsInsidePopup(i - 1))
                continue;

            var localMouse = Vector2.Transform(mouse, e.WorldToLocal);
            if (!e.Rect.Contains(localMouse))
                continue;

            ref var state = ref GetElementState(e.Id);

            var scrollSpeed = e.Data.Scrollable.ScrollSpeed;
            var newOffset = e.Data.Scrollable.Offset - scrollDelta * scrollSpeed;
            var maxScroll = Math.Max(0, e.Data.Scrollable.ContentHeight - e.Rect.Height);
            newOffset = Math.Clamp(newOffset, 0, maxScroll);

            e.Data.Scrollable.Offset = newOffset;
            state.Data.Scrollable.Offset = newOffset;
            Input.ConsumeScroll();
            break;
        }
    }

    private static bool _uiCursorActive;
    private static Sprite? _savedCursorSprite;
    private static SystemCursor _savedSystemCursor;

    private static void HandleCursor()
    {
        short cursorIndex = -1;

        for (short i = 0; i < _elementCount; i++)
        {
            ref var e = ref _elements[i];
            if (e.Type != ElementType.Cursor)
                continue;

            if (_activePopupCount > 0 && !IsInsidePopup(i))
                continue;

            var localMouse = Vector2.Transform(MouseWorldPosition, e.WorldToLocal);
            if (!e.Rect.Contains(localMouse))
                continue;

            cursorIndex = i;
        }

        if (cursorIndex >= 0)
        {
            if (!_uiCursorActive)
            {
                _savedCursorSprite = Cursor.ActiveSprite;
                _savedSystemCursor = Cursor.ActiveSystemCursor;
                _uiCursorActive = true;
            }

            ref var e = ref _elements[cursorIndex];
            if (e.Asset is Sprite sprite)
                Cursor.Set(sprite);
            else
                Cursor.Set(e.Data.Cursor.SystemCursor);
        }
        else if (_uiCursorActive)
        {
            _uiCursorActive = false;
            if (_savedCursorSprite != null)
                Cursor.Set(_savedCursorSprite);
            else
                Cursor.Set(_savedSystemCursor);
        }
    }
}
