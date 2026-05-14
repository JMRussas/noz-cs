//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ;

public static unsafe partial class ElementTree
{
    private static bool _inputMousePressed;
    private static bool _inputMouseDown;
    private static WidgetId _hoveredWidget;
    private static int _cursorOffset; 

    private static void HandlePopupAutoClose()
    {
        ClosePopups = false;
        CloseFromPopupLevel = -1;
        if (_popupCount == 0) return;

        var anyAutoClose = false;
        for (var i = 0; i < _popupCount; i++)
        {
            ref var pe = ref GetElement(_popups[i]);
            ref var pd = ref pe.Data.Popup;
            if (pd.AutoClose) { anyAutoClose = true; break; }
        }
        if (!anyAutoClose) return;

        if (Input.WasButtonPressedRaw(InputCode.KeyEscape))
        {
            ClosePopups = true;
            CloseFromPopupLevel = 0;
            Input.ConsumeButton(InputCode.KeyEscape);
        }

        if (_inputMousePressed)
        {
            int innermostContainingLevel = -1;

            if (_hoveredWidget != WidgetId.None && _widgets.ContainsKey(_hoveredWidget))
            {
                var widgetIdx = _widgets[_hoveredWidget].Ptr->Index;
                for (var i = _popupCount - 1; i >= 0; i--)
                {
                    if (IsDescendantOf(widgetIdx, _popups[i]))
                    {
                        innermostContainingLevel = i;
                        break;
                    }
                }
            }

            if (innermostContainingLevel < 0)
            {
                for (var i = _popupCount - 1; i >= 0; i--)
                {
                    ref var pe = ref GetElement(_popups[i]);
                    Matrix3x2.Invert(pe.Transform, out var popupInv);
                    var localMouse = Vector2.Transform(MouseWorldPosition, popupInv);
                    if (pe.Rect.Contains(localMouse))
                    {
                        innermostContainingLevel = i;
                        break;
                    }
                }
            }

            int closeFromLevel = -1;
            if (innermostContainingLevel < 0)
            {
                closeFromLevel = 0;
                for (var i = 0; i < _popupCount; i++)
                {
                    ref var pd2 = ref GetElement(_popups[i]).Data.Popup;
                    if (!pd2.AutoClose) { closeFromLevel = i + 1; }
                    else break;
                }
                if (closeFromLevel >= _popupCount) closeFromLevel = -1;
            }
            else if (innermostContainingLevel < _popupCount - 1)
            {
                closeFromLevel = innermostContainingLevel + 1;
                for (var i = closeFromLevel; i < _popupCount; i++)
                {
                    ref var pd2 = ref GetElement(_popups[i]).Data.Popup;
                    if (!pd2.AutoClose) { closeFromLevel = i + 1; }
                    else break;
                }
                if (closeFromLevel >= _popupCount) closeFromLevel = -1;
            }

            if (closeFromLevel >= 0)
            {
                CloseFromPopupLevel = closeFromLevel;
                if (closeFromLevel == 0)
                    ClosePopups = true;
                _inputMousePressed = false;
                Input.ConsumeButton(InputCode.MouseLeft);
                Input.ConsumeButton(InputCode.Pen);
            }
        }
    }

    private static bool IsInsidePopup(int index)
    {
        for (var i = 0; i < _popupCount; i++)
        {
            ref var pe = ref GetElement(_popups[i]);
            ref var pd = ref pe.Data.Popup;
            if (!pd.Interactive) continue;
            if (IsDescendantOf(index, _popups[i]))
                return true;
        }
        return false;
    }

    private static bool IsInsideNonInteractivePopup(int offset)
    {
        for (var i = 0; i < _popupCount; i++)
        {
            ref var pe = ref GetElement(_popups[i]);
            ref var pd = ref pe.Data.Popup;
            if (!pd.Interactive && IsDescendantOf(offset, _popups[i]))
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

    internal static void HandleInput()
    {
        if (_elements.Length < 2) return;
        using var _m = s_markerInput.Begin();

        _inputMousePressed =
            Input.WasButtonPressedRaw(InputCode.MouseLeft) ||
            Input.WasButtonPressedRaw(InputCode.Pen);
        _inputMouseDown =
            Input.IsButtonDownRaw(InputCode.MouseLeft) ||
            Input.IsButtonDownRaw(InputCode.Pen);

        if (_captureId != 0 && !_inputMouseDown)
        {
            _captureId = WidgetId.None;
            Input.ReleaseMouseCapture();
        }

        _hoveredWidget = WidgetId.None;
        _cursorOffset = -1;
        _tabNavigationTarget = -1;
        FindHoveredWidget(0);
        HandlePopupAutoClose();
        HandleInputElement(0);

        // Apply deferred tab navigation
        if (_tabNavigationTarget >= 0)
            FocusEditableText(_tabNavigationTarget);

        // Reconcile platform textbox to whichever EditableText (if any) ended up focused —
        // inline Show/Hide calls during tree traversal are order-dependent and can race
        // (e.g. focus-enter on the new field calls Show, then defocus of the old field calls Hide).
        SyncTextboxToFocus();

        HandleScrollableInput();

        if (_cursorOffset >= 0)
        {
            ref var ce = ref GetElement(_cursorOffset);
            ref var cd = ref ce.Data.Cursor;            
            if (cd.IsSprite)
            {
                Cursor.Set(new SpriteCursor(
                    (Sprite)_assets[cd.AssetIndex]!,
                    scale: 1.0f,
                    rotation: cd.Rotation,
                    hotspot:new Vector2(cd.HotspotX, cd.HotspotY)));
            }
            else
                Cursor.Set(cd.SystemCursor);
        }
        else
        {
            Cursor.SetDefault();
        }
    }

    private static void HandleScrollableInput()
    {
        HandleScrollableMouseWheel();
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

        // Children first (deeper scrollables take priority)
        var childOffset = (int)e.FirstChild;
        for (int i = 0; i < e.ChildCount; i++)
        {
            ref var child = ref GetElement(childOffset);
            if (FindScrollableForWheel(childOffset, scrollDelta)) return true;
            childOffset = child.NextSibling;
        }

        if (e.Type == ElementType.Scroll)
        {
            ref var d = ref e.Data.Scroll;
            if (d.State != null)
            {
                Matrix3x2.Invert(e.Transform, out var scrollInv);
                var localMouse = Vector2.Transform(MouseWorldPosition, scrollInv);
                if (e.Rect.Contains(localMouse))
                {
                    ref var state = ref *d.State;
                    var maxScroll = Math.Max(0, state.ContentHeight - e.Rect.Height);
                    state.Offset = Math.Clamp(state.Offset - scrollDelta * d.ScrollSpeed, 0, maxScroll);
                    Input.ConsumeScroll();
                    return true;
                }
            }
        }

        return false;
    }

    internal static float GetScrollOffset(WidgetId id)
    {
        if (id == 0 || !_widgets.ContainsKey(id)) return 0;
        ref var state = ref GetWidgetState<ScrollState>(id);
        return state.Offset;
    }

    internal static void SetScrollOffset(WidgetId id, float offset)
    {
        if (id == 0 || !_widgets.ContainsKey(id)) return;
        ref var state = ref GetWidgetState<ScrollState>(id);
        state.Offset = offset;
    }

    private static void HandleFlexSplitterInput(ref Element e)
    {
        ref var d = ref e.Data.FlexSplitter;
        if (d.State == null || d.PrevFlex == 0 || d.NextFlex == 0) return;

        if (WasPressed(d.Id) && !HasCaptureById(d.Id))
            SetCaptureById(d.Id);

        ref var state = ref *d.State;
        var axis = (int)d.Axis;
        ref var prevFlex = ref GetElement(d.PrevFlex);
        ref var nextFlex = ref GetElement(d.NextFlex);

        var prevSize = prevFlex.Rect.GetSize(axis);
        var nextSize = nextFlex.Rect.GetSize(axis);
        var available = prevSize + nextSize;
        if (available <= 0) return;

        state.AvailableSpace = available;

        if (state.FixedSize <= 0)
            state.FixedSize = state.FixedPane == 2 ? nextSize : prevSize;

        if (HasCaptureById(d.Id))
        {
            var prevWorldPos = Vector2.Transform(prevFlex.Rect.Position, prevFlex.Transform);
            var mouseOnAxis = axis == 0 ? MouseWorldPosition.X : MouseWorldPosition.Y;
            var pane1Size = mouseOnAxis - (axis == 0 ? prevWorldPos.X : prevWorldPos.Y);
            var fixedSize = state.FixedPane == 2 ? available - pane1Size : pane1Size;

            fixedSize = Math.Clamp(fixedSize, d.MinSize, d.MaxSize);
            var otherSize = available - fixedSize;
            if (otherSize < d.MinSize2) fixedSize = available - d.MinSize2;
            if (d.MaxSize2 < float.MaxValue && otherSize > d.MaxSize2) fixedSize = available - d.MaxSize2;
            state.FixedSize = Math.Max(fixedSize, 0);
        }

        state.Ratio = Math.Clamp(state.FixedSize / available, 0.001f, 0.999f);
    }

    private static void HandleTrackInput(ref Element e)
    {
        ref var d = ref e.Data.Track;
        var widgetId = d.Id;

        if (WasPressed(widgetId) && !HasCaptureById(widgetId))
            SetCaptureById(widgetId);

        if (!HasCaptureById(widgetId))
            return;

        var worldRect = GetWidgetWorldRect(widgetId);
        ref var state = ref *d.State;

        if (d.ThumbSize.X > 0)
        {
            var trackSize = worldRect.Width;
            if (trackSize > 0)
            {
                var usable = trackSize - d.ThumbSize.X;
                if (usable > 0)
                    state.X = MathEx.Clamp01((MouseWorldPosition.X - worldRect.X - d.ThumbSize.X / 2) / usable);
            }
        }

        if (d.ThumbSize.Y > 0)
        {
            var trackSize = worldRect.Height;
            if (trackSize > 0)
            {
                var usable = trackSize - d.ThumbSize.Y;
                if (usable > 0)
                    state.Y = MathEx.Clamp01((MouseWorldPosition.Y - worldRect.Y - d.ThumbSize.Y / 2) / usable);
            }
        }
    }

    private static void FindHoveredWidget(int index)
    {
        ref var e = ref GetElement(index);

        if (e.Type == ElementType.Widget)
        {
            var skipHover = false;

            if (_activePopupCount > 0 && !IsInsidePopup(e.Index))
                skipHover = true;
            else if (IsInsideNonInteractivePopup(e.Index))
                skipHover = true;

            if (!skipHover && (_captureId == 0 || _captureId == e.Data.Widget.Id))
            {
                Matrix3x2.Invert(e.Transform, out var inv);
                var localMouse = Vector2.Transform(MouseWorldPosition, inv);
                if (e.Rect.Contains(localMouse))
                {
                    _hoveredWidget = e.Data.Widget.Id;
                }
            }
        }
        else if (e.Type == ElementType.Cursor)
        {
            Matrix3x2.Invert(e.Transform, out var inv);
            var localMouse = Vector2.Transform(MouseWorldPosition, inv);
            if (e.Rect.Contains(localMouse))
                _cursorOffset = index;
        }

        var childIndex = (int)e.FirstChild;
        for (int i = 0; i < e.ChildCount; i++)
        {
            ref var child = ref GetElement(childIndex);
            FindHoveredWidget(childIndex);
            childIndex = child.NextSibling;
        }
    }

    private static void HandleWidgetInput(ref Element e)
    {
        ref var d = ref e.Data.Widget;
        ref var state = ref GetWidgetState(d.Id);
        var prevFlags = state.Flags;

        const WidgetFlags InputFlags = 
            WidgetFlags.Hovered |
            WidgetFlags.Pressed |
            WidgetFlags.Down |
            WidgetFlags.HoverChanged |
            WidgetFlags.DoubleClick |
            WidgetFlags.RightClick;
        state.Flags &= ~InputFlags;

        if (_activePopupCount > 0 && !IsInsidePopup(e.Index))
            return;

        if (IsInsideNonInteractivePopup(e.Index))
            return;

        Matrix3x2.Invert(e.Transform, out var inv);
        var localMouse = Vector2.Transform(MouseWorldPosition, inv);
        var isHovered = !Application.IsTablet && e.Rect.Contains(localMouse);
        var isDeepHovered = _hoveredWidget == d.Id;

        if (isHovered && (_captureId == 0 || _captureId == d.Id))
            state.Flags |= WidgetFlags.Hovered;

        var isCaptured = _captureId != 0 && _captureId == d.Id;

        if (isDeepHovered && _inputMousePressed && (_captureId == 0 || _captureId == d.Id))
        {
            state.Flags |= WidgetFlags.Pressed;
            if (d.IsInteractive)
            {
                Input.ConsumeButton(InputCode.MouseLeft);
                Input.ConsumeButton(InputCode.Pen);
            }
        }

        if (isCaptured ? _inputMouseDown : (isDeepHovered && _inputMouseDown && _captureId == 0))
            state.Flags |= WidgetFlags.Down;

        if ((state.Flags & WidgetFlags.Hovered) != (prevFlags & WidgetFlags.Hovered))
            state.Flags |= WidgetFlags.HoverChanged;
    }

    private static void HandleInputElement(int index)
    {
        ref var e = ref GetElement(index);

        if (e.Type == ElementType.Widget)
            HandleWidgetInput(ref e);

        if (e.Type == ElementType.Track)
            HandleTrackInput(ref e);

        if (e.Type == ElementType.FlexSplitter)
            HandleFlexSplitterInput(ref e);

        if (e.Type == ElementType.EditableText)
            HandleEditableTextInput(ref e);

        var childIndex = (int)e.FirstChild;
        for (int i = 0; i < e.ChildCount; i++)
        {
            ref var child = ref GetElement(childIndex);
            HandleInputElement(childIndex);
            childIndex = child.NextSibling;
        }
    }
}
