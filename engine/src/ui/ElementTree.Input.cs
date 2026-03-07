//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ;

public static unsafe partial class ElementTree
{
    private static void HandlePopupAutoClose()
    {
        ClosePopups = false;
        if (_popupCount == 0) return;

        var anyAutoClose = false;
        for (var i = 0; i < _popupCount; i++)
        {
            ref var pe = ref GetElement(_popupOffsets[i]);
            ref var pd = ref GetElementData<PopupElement>(ref pe);
            if (pd.AutoClose) { anyAutoClose = true; break; }
        }
        if (!anyAutoClose) return;

        if (Input.WasButtonPressedRaw(InputCode.KeyEscape))
        {
            ClosePopups = true;
            Input.ConsumeButton(InputCode.KeyEscape);
        }

        if (_inputMousePressed)
        {
            var clickInsideAutoClosePopup = false;
            for (var i = 0; i < _popupCount; i++)
            {
                ref var pe = ref GetElement(_popupOffsets[i]);
                ref var pd = ref GetElementData<PopupElement>(ref pe);
                if (!pd.AutoClose) continue;

                Matrix3x2.Invert(GetTransform(ref pe), out var popupInv);
                var localMouse = Vector2.Transform(MouseWorldPosition, popupInv);
                if (pe.Rect.Contains(localMouse))
                {
                    clickInsideAutoClosePopup = true;
                    break;
                }
            }

            if (!clickInsideAutoClosePopup)
            {
                ClosePopups = true;
                _inputMousePressed = false;
                Input.ConsumeButton(InputCode.MouseLeft);
            }
        }
    }

    private static bool IsInsidePopup(int offset)
    {
        for (var i = 0; i < _popupCount; i++)
        {
            ref var pe = ref GetElement(_popupOffsets[i]);
            ref var pd = ref GetElementData<PopupElement>(ref pe);
            if (!pd.Interactive) continue;
            if (IsDescendantOf(offset, _popupOffsets[i]))
                return true;
        }
        return false;
    }

    private static bool IsInsideNonInteractivePopup(int offset)
    {
        for (var i = 0; i < _popupCount; i++)
        {
            ref var pe = ref GetElement(_popupOffsets[i]);
            ref var pd = ref GetElementData<PopupElement>(ref pe);
            if (!pd.Interactive && IsDescendantOf(offset, _popupOffsets[i]))
                return true;
        }
        return false;
    }

    private static bool IsDescendantOf(int offset, int ancestorOffset)
    {
        var current = offset;
        while (current != 0)
        {
            if (current == ancestorOffset)
                return true;
            ref var e = ref GetElement(current);
            current = e.Parent;
        }
        return false;
    }

    // Cursor state
    private static bool _cursorActive;
    private static Sprite? _savedCursorSprite;
    private static SystemCursor _savedSystemCursor;

    private static void HandleInput()
    {
        _inputMousePressed = Input.WasButtonPressedRaw(InputCode.MouseLeft);
        _inputMouseDown = Input.IsButtonDownRaw(InputCode.MouseLeft);
        MouseOverScene = false;

        if (_captureId != 0 && !_inputMouseDown)
        {
            _captureId = 0;
            Input.ReleaseMouseCapture();
        }

        HandlePopupAutoClose();
        HandleInputElement(0);
        HandleScrollableInput();
        HandleSceneInput(0);
        HandleCursor(0);
    }

    private static void HandleCursor(int offset)
    {
        int cursorOffset = -1;
        FindCursorUnderMouse(0, ref cursorOffset);

        if (cursorOffset >= 0)
        {
            if (!_cursorActive)
            {
                _savedCursorSprite = Cursor.ActiveSprite;
                _savedSystemCursor = Cursor.ActiveSystemCursor;
                _cursorActive = true;
            }

            ref var ce = ref GetElement(cursorOffset);
            ref var cd = ref GetElementData<CursorElement>(ref ce);
            if (cd.IsSprite)
                Cursor.Set((Sprite)_assets[cd.AssetIndex]!);
            else
                Cursor.Set(cd.SystemCursor);
        }
        else if (_cursorActive)
        {
            _cursorActive = false;
            if (_savedCursorSprite != null)
                Cursor.Set(_savedCursorSprite);
            else
                Cursor.Set(_savedSystemCursor);
        }
    }

    private static void FindCursorUnderMouse(int offset, ref int foundOffset)
    {
        ref var e = ref GetElement(offset);

        if (e.Type == ElementType.Cursor)
        {
            Matrix3x2.Invert(GetTransform(ref e), out var cursorInv);
            var localMouse = Vector2.Transform(MouseWorldPosition, cursorInv);
            if (e.Rect.Contains(localMouse))
                foundOffset = offset;
        }

        var childOffset = (int)e.FirstChild;
        for (int i = 0; i < e.ChildCount; i++)
        {
            ref var child = ref GetElement(childOffset);
            FindCursorUnderMouse(childOffset, ref foundOffset);
            childOffset = child.NextSibling;
        }
    }

    internal static bool ScrollbarDragging => _scrollbarDragging;
    private static bool _scrollbarDragging;
    private static int _scrollbarDragWidgetId;
    private static float _scrollbarDragStartOffset;
    private static float _scrollbarDragStartMouseY;
    private static int _activeScrollWidgetId;
    private static float _lastScrollMouseY;

    private static void HandleScrollableInput()
    {
        HandleScrollbarDrag();
        HandleScrollableContentDrag();
        HandleScrollableMouseWheel();
    }

    private static void HandleScrollbarDrag()
    {
        if (_scrollbarDragging)
        {
            if (!_inputMouseDown)
            {
                _scrollbarDragging = false;
                Input.ReleaseMouseCapture();
                return;
            }

            ref var state = ref GetStateByWidgetId<ScrollableState>(_scrollbarDragWidgetId);
            var viewportHeight = GetScrollableViewportHeight(_scrollbarDragWidgetId);
            var maxScroll = Math.Max(0, state.ContentHeight - viewportHeight);
            if (maxScroll <= 0) return;

            // Find the scrollable element to get style data
            var scrollOffset = FindScrollableOffset(_scrollbarDragWidgetId);
            if (scrollOffset < 0) return;
            ref var e = ref GetElement(scrollOffset);
            ref var d = ref GetElementData<ScrollableElement>(ref e);

            var trackH = viewportHeight - d.ScrollbarPadding * 2;
            var thumbHeightRatio = viewportHeight / state.ContentHeight;
            var thumbH = Math.Max(d.ScrollbarMinThumbHeight, trackH * thumbHeightRatio);
            var availableTrackSpace = trackH - thumbH;

            if (availableTrackSpace > 0)
            {
                var mouseDeltaY = MouseWorldPosition.Y - _scrollbarDragStartMouseY;
                var scrollDelta = (mouseDeltaY / availableTrackSpace) * maxScroll;
                state.Offset = Math.Clamp(_scrollbarDragStartOffset + scrollDelta, 0, maxScroll);
            }
            return;
        }

        if (!_inputMousePressed) return;

        // Check for new scrollbar interactions
        FindScrollbarInteraction(0);
    }

    private static void FindScrollbarInteraction(int offset)
    {
        ref var e = ref GetElement(offset);

        if (e.Type == ElementType.Scrollable)
        {
            ref var d = ref GetElementData<ScrollableElement>(ref e);
            if (d.WidgetId > 0)
            {
                ref var state = ref GetStateByWidgetId<ScrollableState>(d.WidgetId);
                var viewportHeight = e.Rect.Height;
                var maxScroll = Math.Max(0, state.ContentHeight - viewportHeight);

                // Check thumb hit
                if (GetScrollbarThumbRect(ref e, ref d, ref state, out var thumbRect) && thumbRect.Contains(MouseWorldPosition))
                {
                    _scrollbarDragging = true;
                    _scrollbarDragWidgetId = d.WidgetId;
                    _scrollbarDragStartOffset = state.Offset;
                    _scrollbarDragStartMouseY = MouseWorldPosition.Y;
                    Input.CaptureMouse();
                    return;
                }

                // Check track hit (page scroll)
                if (GetScrollbarTrackRect(ref e, ref d, ref state, out var trackRect) && trackRect.Contains(MouseWorldPosition))
                {
                    if (maxScroll > 0)
                    {
                        var clickRelativeY = MouseWorldPosition.Y - trackRect.Y;
                        var clickRatio = clickRelativeY / trackRect.Height;
                        var targetOffset = clickRatio * maxScroll;
                        var pageAmount = viewportHeight * 0.9f;
                        var newOffset = state.Offset < targetOffset
                            ? Math.Min(state.Offset + pageAmount, targetOffset)
                            : Math.Max(state.Offset - pageAmount, targetOffset);
                        state.Offset = Math.Clamp(newOffset, 0, maxScroll);
                    }
                    Input.ConsumeButton(InputCode.MouseLeft);
                    return;
                }
            }
        }

        var childOffset = (int)e.FirstChild;
        for (int i = 0; i < e.ChildCount; i++)
        {
            ref var child = ref GetElement(childOffset);
            FindScrollbarInteraction(childOffset);
            childOffset = child.NextSibling;
        }
    }

    private static bool GetScrollbarThumbRect(ref BaseElement e, ref ScrollableElement d, ref ScrollableState state, out Rect thumbRect)
    {
        thumbRect = Rect.Zero;
        var viewportHeight = e.Rect.Height;
        var maxScroll = Math.Max(0, state.ContentHeight - viewportHeight);
        if (d.ScrollbarVisibility == ScrollbarVisibility.Never || maxScroll <= 0) return false;

        var pos = Vector2.Transform(e.Rect.Position, GetTransform(ref e));
        var trackX = pos.X + e.Rect.Width - d.ScrollbarWidth - d.ScrollbarPadding;
        var trackY = pos.Y + d.ScrollbarPadding;
        var trackH = viewportHeight - d.ScrollbarPadding * 2;
        var thumbHeightRatio = viewportHeight / state.ContentHeight;
        var thumbH = Math.Max(d.ScrollbarMinThumbHeight, trackH * thumbHeightRatio);
        var scrollRatio = state.Offset / maxScroll;
        var thumbY = trackY + scrollRatio * (trackH - thumbH);

        thumbRect = new Rect(trackX, thumbY, d.ScrollbarWidth, thumbH);
        return true;
    }

    private static bool GetScrollbarTrackRect(ref BaseElement e, ref ScrollableElement d, ref ScrollableState state, out Rect trackRect)
    {
        trackRect = Rect.Zero;
        var viewportHeight = e.Rect.Height;
        var maxScroll = Math.Max(0, state.ContentHeight - viewportHeight);
        if (d.ScrollbarVisibility == ScrollbarVisibility.Never) return false;
        if (d.ScrollbarVisibility == ScrollbarVisibility.Auto && maxScroll <= 0) return false;

        var pos = Vector2.Transform(e.Rect.Position, GetTransform(ref e));
        var trackX = pos.X + e.Rect.Width - d.ScrollbarWidth - d.ScrollbarPadding;
        var trackY = pos.Y + d.ScrollbarPadding;
        var trackH = viewportHeight - d.ScrollbarPadding * 2;

        trackRect = new Rect(trackX, trackY, d.ScrollbarWidth, trackH);
        return true;
    }

    private static void HandleScrollableContentDrag()
    {
        if (_scrollbarDragging) return;

        if (!_inputMouseDown)
        {
            if (_activeScrollWidgetId != 0)
                Input.ReleaseMouseCapture();
            _activeScrollWidgetId = 0;
        }
        else if (_activeScrollWidgetId != 0)
        {
            var deltaY = _lastScrollMouseY - MouseWorldPosition.Y;
            _lastScrollMouseY = MouseWorldPosition.Y;

            ref var state = ref GetStateByWidgetId<ScrollableState>(_activeScrollWidgetId);
            var viewportHeight = GetScrollableViewportHeight(_activeScrollWidgetId);
            var maxScroll = Math.Max(0, state.ContentHeight - viewportHeight);
            state.Offset = Math.Clamp(state.Offset + deltaY, 0, maxScroll);
        }
        else if (_inputMousePressed)
        {
            FindScrollableContentDragStart(0);
        }
    }

    private static void FindScrollableContentDragStart(int offset)
    {
        ref var e = ref GetElement(offset);

        if (e.Type == ElementType.Scrollable)
        {
            ref var d = ref GetElementData<ScrollableElement>(ref e);
            if (d.WidgetId > 0)
            {
                ref var ws = ref _widgetStates[d.WidgetId];
                if ((ws.Flags & ElementFlags.Pressed) != 0)
                {
                    _activeScrollWidgetId = d.WidgetId;
                    _lastScrollMouseY = MouseWorldPosition.Y;
                    Input.CaptureMouse();
                    return;
                }
            }
        }

        var childOffset = (int)e.FirstChild;
        for (int i = 0; i < e.ChildCount; i++)
        {
            ref var child = ref GetElement(childOffset);
            FindScrollableContentDragStart(childOffset);
            childOffset = child.NextSibling;
        }
    }

    private static void HandleScrollableMouseWheel()
    {
        var scrollDelta = Input.GetAxisValue(InputCode.MouseScrollY);
        if (scrollDelta == 0) return;

        FindScrollableForWheel(0, scrollDelta);
    }

    private static bool FindScrollableForWheel(int offset, float scrollDelta)
    {
        ref var e = ref GetElement(offset);

        // Check children first (deeper scrollables take priority)
        var childOffset = (int)e.FirstChild;
        for (int i = 0; i < e.ChildCount; i++)
        {
            ref var child = ref GetElement(childOffset);
            if (FindScrollableForWheel(childOffset, scrollDelta)) return true;
            childOffset = child.NextSibling;
        }

        if (e.Type == ElementType.Scrollable)
        {
            ref var d = ref GetElementData<ScrollableElement>(ref e);
            if (d.WidgetId > 0)
            {
                Matrix3x2.Invert(GetTransform(ref e), out var scrollInv);
                var localMouse = Vector2.Transform(MouseWorldPosition, scrollInv);
                if (e.Rect.Contains(localMouse))
                {
                    ref var state = ref GetStateByWidgetId<ScrollableState>(d.WidgetId);
                    var maxScroll = Math.Max(0, state.ContentHeight - e.Rect.Height);
                    state.Offset = Math.Clamp(state.Offset - scrollDelta * d.ScrollSpeed, 0, maxScroll);
                    Input.ConsumeScroll();
                    return true;
                }
            }
        }

        return false;
    }

    private static int FindScrollableOffset(int widgetId)
    {
        return FindScrollableOffsetRecursive(0, widgetId);
    }

    private static int FindScrollableOffsetRecursive(int offset, int widgetId)
    {
        ref var e = ref GetElement(offset);
        if (e.Type == ElementType.Scrollable)
        {
            ref var d = ref GetElementData<ScrollableElement>(ref e);
            if (d.WidgetId == widgetId) return offset;
        }
        var childOffset = (int)e.FirstChild;
        for (int i = 0; i < e.ChildCount; i++)
        {
            ref var child = ref GetElement(childOffset);
            var found = FindScrollableOffsetRecursive(childOffset, widgetId);
            if (found >= 0) return found;
            childOffset = child.NextSibling;
        }
        return -1;
    }

    private static float GetScrollableViewportHeight(int widgetId)
    {
        var offset = FindScrollableOffset(widgetId);
        if (offset < 0) return 0;
        ref var e = ref GetElement(offset);
        return e.Rect.Height;
    }

    internal static float GetScrollOffset(int widgetId)
    {
        if (widgetId <= 0) return 0;
        ref var state = ref GetStateByWidgetId<ScrollableState>(widgetId);
        return state.Offset;
    }

    internal static void SetScrollOffset(int widgetId, float offset)
    {
        if (widgetId <= 0) return;
        ref var state = ref GetStateByWidgetId<ScrollableState>(widgetId);
        state.Offset = offset;
    }

    private static void HandleSceneInput(int offset)
    {
        ref var e = ref GetElement(offset);

        if (e.Type == ElementType.Scene)
        {
            Matrix3x2.Invert(GetTransform(ref e), out var sceneInv);
            var localMouse = Vector2.Transform(MouseWorldPosition, sceneInv);
            if (e.Rect.Contains(localMouse))
                MouseOverScene = true;
        }

        var childOffset = (int)e.FirstChild;
        for (int i = 0; i < e.ChildCount; i++)
        {
            ref var child = ref GetElement(childOffset);
            HandleSceneInput(childOffset);
            childOffset = child.NextSibling;
        }
    }

    private static bool _inputMousePressed;
    private static bool _inputMouseDown;

    private static void HandleInputElement(int offset)
    {
        ref var e = ref GetElement(offset);

        if (e.Type == ElementType.Widget)
        {
            ref var d = ref GetElementData<WidgetElement>(ref e);
            if (d.Id > 0 && d.Id < MaxId)
            {
                ref var ws = ref _widgetStates[d.Id];
                ws.PrevFlags = ws.Flags;
                ws.Flags = ElementFlags.None;

                // Block input for widgets outside popups when popups are open
                if (_activePopupCount > 0 && !IsInsidePopup(offset))
                {
                    ws.LastFrame = _frame;
                    goto recurse;
                }
                if (IsInsideNonInteractivePopup(offset))
                {
                    ws.LastFrame = _frame;
                    goto recurse;
                }

                Matrix3x2.Invert(GetTransform(ref e), out var widgetInv);
                var localMouse = Vector2.Transform(MouseWorldPosition, widgetInv);
                var hovered = e.Rect.Contains(localMouse);

                if (hovered)
                    ws.Flags |= ElementFlags.Hovered;

                if (hovered && _inputMousePressed && (_captureId == 0 || _captureId == d.Id))
                    ws.Flags |= ElementFlags.Pressed;

                var isCaptured = _captureId != 0 && _captureId == d.Id;
                if (isCaptured ? _inputMouseDown : (hovered && _inputMouseDown))
                    ws.Flags |= ElementFlags.Down;

                if (_focusId == d.Id)
                    ws.Flags |= ElementFlags.Focus;

                if ((ws.Flags & ElementFlags.Hovered) != (ws.PrevFlags & ElementFlags.Hovered))
                    ws.Flags |= ElementFlags.HoverChanged;

                ws.LastFrame = _frame;
            }
        }

        recurse:
        var childOffset = (int)e.FirstChild;
        for (int i = 0; i < e.ChildCount; i++)
        {
            ref var child = ref GetElement(childOffset);
            HandleInputElement(childOffset);
            childOffset = child.NextSibling;
        }
    }
}
