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
        _hotCanvasId = ElementId.None;

        LogUI("Input:", values: [("HotCanvasId", _hotCanvasId, true)]);

        // Handle popup close detection
        _closePopups = false;
        if (_mouseLeftPressed && _popupCount > 0)
        {
            var clickInsidePopup = false;
            for (var i = 0; i < _popupCount; i++)
            {
                ref var popup = ref _elements[_popups[i]];
                var localMouse = Vector2.Transform(mouse, popup.WorldToLocal);
                if (new Rect(0, 0, popup.Rect.Width, popup.Rect.Height).Contains(localMouse))
                {
                    clickInsidePopup = true;
                    break;
                }
            }

            if (!clickInsidePopup)
            {
                _closePopups = true;
                return;
            }
        }

        for (var c = 0; c < _activeCanvasIds.Length; c++)
        {
            ref var cid = ref _activeCanvasIds[c];
            ref var cs = ref GetCanvasState(cid);
            var isHotCanvas = cid == _hotCanvasId;
            HandleCanvasInput(cid, mouse, isHotCanvas);
        }

        if (_mouseLeftElementId != ElementId.None)
            Input.ConsumeButton(InputCode.MouseLeft);

        if (_mouseDoubleClickElementId != ElementId.None)
            Input.ConsumeButton(InputCode.MouseLeftDoubleClick);

        if (_mouseRightElementId != ElementId.None)
            Input.ConsumeButton(InputCode.MouseRight);

        HandleScrollableDrag(mouse);
        HandleMouseWheelScroll(mouse);
    }

    private static void HandleCanvasInput(CanvasId canvasId, Vector2 mouse, bool isHotCanvas)
    {
        ref var cs = ref _canvasStates[canvasId];
        ref var c = ref _elements[cs.ElementIndex];

        for (var elementIndex=c.NextSiblingIndex - 1; elementIndex > c.Index; elementIndex--)
        {
            ref var e = ref _elements[elementIndex];
            if (e.Id == ElementId.None) continue;

            Debug.Assert(e.CanvasId == c.CanvasId);

            if (e.Type == ElementType.TextBox)
                HandleTextBoxInput(ref e);

            ref var es = ref cs.ElementStates[e.Id];
            es.Rect = e.Rect;
            es.LocalToWorld = e.LocalToWorld;
            var localMouse = Vector2.Transform(mouse, e.WorldToLocal);
            var mouseOver = new Rect(0, 0, e.Rect.Width, e.Rect.Height).Contains(localMouse);

            es.SetFlags(ElementFlags.Hovered, mouseOver ? ElementFlags.Hovered : ElementFlags.None);
            es.SetFlags(ElementFlags.Down, mouseOver && _mouseLeftDown ? ElementFlags.Down : ElementFlags.None);

            if (mouseOver && _mouseLeftDoubleClickPressed && _mouseDoubleClickElementId == ElementId.None)
            {
                _mouseDoubleClickElementId = e.Id;
                es.SetFlags(ElementFlags.DoubleClick, ElementFlags.DoubleClick);
            }
            else
                es.SetFlags(ElementFlags.DoubleClick, ElementFlags.None);

            if (mouseOver && _mouseLeftPressed && _mouseLeftElementId == ElementId.None)
            {
                es.SetFlags(ElementFlags.Pressed, ElementFlags.Pressed);
                _mouseLeftElementId = e.Id;
                _pendingFocusId = e.Id;
                _pendingFocusCanvasId = canvasId;
            }
            else if (es.IsPressed)
            {
                es.SetFlags(ElementFlags.Pressed, ElementFlags.None);
            }

            if (mouseOver && _mouseRightPressed && _mouseRightElementId == ElementId.None)
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
                if (e.Type == ElementType.Scrollable && e.Id != ElementId.None && e.CanvasId == _hotCanvasId)
                {
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

            var localMouse = Vector2.Transform(mouse, e.WorldToLocal);
            if (!new Rect(0, 0, e.Rect.Width, e.Rect.Height).Contains(localMouse))
                continue;

            ref var cs = ref _canvasStates[e.CanvasId];
            ref var state = ref cs.ElementStates[e.Id];

            var scrollSpeed = 30f;
            var newOffset = e.Data.Scrollable.Offset - scrollDelta * scrollSpeed;
            var maxScroll = Math.Max(0, e.Data.Scrollable.ContentHeight - e.Rect.Height);
            newOffset = Math.Clamp(newOffset, 0, maxScroll);

            e.Data.Scrollable.Offset = newOffset;
            state.Data.Scrollable.Offset = newOffset;
            break;
        }
    }
}
