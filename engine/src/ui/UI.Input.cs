//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

// #define NOZ_UI_DEBUG

using System.Diagnostics;
using System.Numerics;

namespace NoZ;

public static partial class UI
{
    private static ElementId _mouseLeftElementId;
    private static ElementId _mouseDoubleClickElementId;
    private static ElementId _mouseRightElementId;
    private static bool _mouseLeftPressed;
    private static bool _mouseRightPressed;
    private static bool _mouseLeftDown;
    private static bool _mouseLeftDoubleClickPressed;
    private static Vector2 _mousePosition;

    private static void HandleInput()
    {
        var mouse = Camera!.ScreenToWorld(Input.MousePosition);        
        _mousePosition = mouse;
        _mouseLeftPressed = Input.WasButtonPressedRaw(InputCode.MouseLeft);
        _mouseLeftDown = Input.IsButtonDownRaw(InputCode.MouseLeft);
        _mouseLeftDoubleClickPressed = Input.WasButtonPressedRaw(InputCode.MouseLeftDoubleClick);
        _mouseLeftElementId = ElementId.None;
        _mouseRightPressed = Input.WasButtonPressedRaw(InputCode.MouseRight);
        _mouseRightElementId = ElementId.None;
        _mouseDoubleClickElementId = ElementId.None;

        LogUI("Input:", values: [("FocusElementId", _focusElementId, true)]);

        // Handle popup close detection
        _closePopups = false;
        if (_mouseLeftPressed && _popupCount > 0)
        {
            var clickInsidePopup = false;
            for (var i = 0; i < _popupCount; i++)
            {
                ref var popup = ref _elements[_popups[i]];
                var localMouse = Vector2.Transform(mouse, popup.WorldToLocal);
                if (popup.Rect.Contains(localMouse))
                {
                    clickInsidePopup = true;
                    break;
                }
            }

            if (!clickInsidePopup)
            {
                _closePopups = true;
                Input.ConsumeButton(InputCode.MouseLeft);
                return;
            }
        }

        // Process canvases in reverse order so later canvases (on top) get input priority
        for (var c = _activeCanvasIds.Length - 1; c >= 0; c--)
        {
            ref var cid = ref _activeCanvasIds[c];
            ref var cs = ref GetCanvasState(cid);
            HandleCanvasInput(cid, mouse);
        }

        // Process scrollbar input BEFORE consuming buttons
        HandleScrollbarInput(mouse);

        // Don't consume mouse button if scrollbar is being dragged
        if ((_mouseLeftElementId != ElementId.None || _popupCount > 0) && !_scrollbarDragging)
            Input.ConsumeButton(InputCode.MouseLeft);

        if (_mouseDoubleClickElementId != ElementId.None || _popupCount > 0)
            Input.ConsumeButton(InputCode.MouseLeftDoubleClick);

        if (_mouseRightElementId != ElementId.None || _popupCount > 0)
            Input.ConsumeButton(InputCode.MouseRight);
        HandleScrollableDrag(mouse);
        HandleMouseWheelScroll(mouse);
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

            ref var es = ref GetElementState(_scrollbarDragCanvasId, _scrollbarDragElementId);
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
            if (e.Type != ElementType.Scrollable || e.Id == ElementId.None || e.CanvasId == ElementId.None)
                continue;

            // When popups are open, only interact with scrollbars inside popups
            if (_popupCount > 0 && !IsInsidePopup(i))
                continue;

            // Check if mouse is over the scrollbar thumb
            var hasThumb = GetScrollbarThumbRect(ref e, out var thumbRect);
            var thumbContains = hasThumb && thumbRect.Contains(mouse);

            if (thumbContains)
            {
                // Start thumb drag (don't consume button - we need _mouseLeftDown to stay true)
                _scrollbarDragging = true;
                _scrollbarDragElementId = e.Id;
                _scrollbarDragCanvasId = e.CanvasId;
                _scrollbarDragStartOffset = e.Data.Scrollable.Offset;
                _scrollbarDragStartMouseY = mouse.Y;
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
                    ref var cs = ref _canvasStates[e.CanvasId];
                    ref var state = ref cs.ElementStates[e.Id];
                    state.Data.Scrollable.Offset = newOffset;
                }
                Input.ConsumeButton(InputCode.MouseLeft);
                return;
            }
        }
    }

    private static bool IsInsidePopup(int elementIndex)
    {
        for (var i = 0; i < _popupCount; i++)
        {
            var popupIndex = _popups[i];
            ref var popup = ref _elements[popupIndex];
            if (elementIndex >= popupIndex && elementIndex < popup.NextSiblingIndex)
                return true;
        }
        return false;
    }

    private static void HandleCanvasInput(CanvasId canvasId, Vector2 mouse)
    {
        ref var cs = ref _canvasStates[canvasId];
        ref var c = ref _elements[cs.ElementIndex];

        for (var elementIndex=c.NextSiblingIndex - 1; elementIndex > c.Index; elementIndex--)
        {
            ref var e = ref _elements[elementIndex];
            if (e.Id == ElementId.None) continue;

            Debug.Assert(e.CanvasId == c.CanvasId);

            ref var es = ref cs.ElementStates[e.Id];
            es.Rect = e.Rect;
            es.LocalToWorld = e.LocalToWorld;

            // When popups are open, only process input for elements inside popups
            if (_popupCount > 0 && !IsInsidePopup(elementIndex))
            {
                es.SetFlags(ElementFlags.Hovered | ElementFlags.Down | ElementFlags.Pressed | ElementFlags.DoubleClick | ElementFlags.RightClick, ElementFlags.None);
                continue;
            }

            if (e.Type == ElementType.TextBox)
                UpdateTextBoxState(ref e);

            var localMouse = Vector2.Transform(mouse, e.WorldToLocal);
            var mouseOver = e.Rect.Contains(localMouse);

            es.SetFlags(ElementFlags.Hovered, mouseOver ? ElementFlags.Hovered : ElementFlags.None);
            es.SetFlags(ElementFlags.Down, mouseOver && _mouseLeftDown ? ElementFlags.Down : ElementFlags.None);

            var consumesInput = e.Type != ElementType.Scene;

            if (mouseOver && _mouseLeftDoubleClickPressed && _mouseDoubleClickElementId == ElementId.None && consumesInput)
            {
                _mouseDoubleClickElementId = e.Id;
                es.SetFlags(ElementFlags.DoubleClick, ElementFlags.DoubleClick);
            }
            else
                es.SetFlags(ElementFlags.DoubleClick, ElementFlags.None);

            if (mouseOver && _mouseLeftPressed && _mouseLeftElementId == ElementId.None && consumesInput)
            {
                es.SetFlags(ElementFlags.Pressed, ElementFlags.Pressed);
                _mouseLeftElementId = e.Id;
                _pendingFocusElementId = e.Id;
                _pendingFocusCanvasId = canvasId;
            }
            else if (es.IsPressed)
            {
                es.SetFlags(ElementFlags.Pressed, ElementFlags.None);
            }

            if (mouseOver && _mouseRightPressed && _mouseRightElementId == ElementId.None && consumesInput)
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
            _activeScrollId = ElementId.None;
        }
        else if (_activeScrollId != ElementId.None)
        {
            var deltaY = _lastScrollMouseY - mouse.Y;
            _lastScrollMouseY = mouse.Y;

            for (var i = 0; i < _elementCount; i++)
            {
                ref var e = ref _elements[i];
                if (e.Type == ElementType.Scrollable && e.Id == _activeScrollId && e.CanvasId != ElementId.None)
                {
                    ref var cs = ref _canvasStates[e.CanvasId];
                    ref var state = ref cs.ElementStates[e.Id];

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
                if (e.Type == ElementType.Scrollable && e.Id != ElementId.None && e.CanvasId != CanvasId.None)
                {
                    // When popups are open, only allow starting scroll inside popups
                    if (_popupCount > 0 && !IsInsidePopup(i - 1))
                        continue;

                    ref var cs = ref _canvasStates[e.CanvasId];
                    ref var state = ref cs.ElementStates[e.Id];
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
            if (e.Type != ElementType.Scrollable || e.Id == ElementId.None || e.CanvasId == ElementId.None)
                continue;

            // When popups are open, only allow scrolling inside popups
            if (_popupCount > 0 && !IsInsidePopup(i - 1))
                continue;

            var localMouse = Vector2.Transform(mouse, e.WorldToLocal);
            if (!e.Rect.Contains(localMouse))
                continue;

            ref var cs = ref _canvasStates[e.CanvasId];
            ref var state = ref cs.ElementStates[e.Id];

            var scrollSpeed = e.Data.Scrollable.ScrollSpeed;
            var newOffset = e.Data.Scrollable.Offset - scrollDelta * scrollSpeed;
            var maxScroll = Math.Max(0, e.Data.Scrollable.ContentHeight - e.Rect.Height);
            newOffset = Math.Clamp(newOffset, 0, maxScroll);

            e.Data.Scrollable.Offset = newOffset;
            state.Data.Scrollable.Offset = newOffset;
            break;
        }
    }
}
