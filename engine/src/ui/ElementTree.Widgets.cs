//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Diagnostics;
using System.Numerics;

namespace NoZ;

public static unsafe partial class ElementTree
{
    private static ref WidgetElement GetCurrentWidget()
    {
        Debug.Assert(_currentWidget != 0);
        ref var e = ref GetElement(_currentWidget);
        Debug.Assert(e.Type == ElementType.Widget);
        return ref GetElementData<WidgetElement>(ref e);
    }

    private static ref WidgetInputState GetCurrentWidgetState()
    {
        ref var w = ref GetCurrentWidget();
        Debug.Assert(w.Id > 0 && w.Id < MaxId);
        return ref _widgetStates[w.Id];
    }

    internal static ElementFlags GetCurrentWidgetFlags() => GetCurrentWidgetState().Flags;

    internal static void SetWidgetFlag(ElementFlags flag, bool value)
    {
        ref var ws = ref GetCurrentWidgetState();
        if (value) ws.Flags |= flag;
        else ws.Flags &= ~flag;
    }

    public static bool IsHovered() => GetCurrentWidgetState().Flags.HasFlag(ElementFlags.Hovered);
    public static bool WasPressed() => GetCurrentWidgetState().Flags.HasFlag(ElementFlags.Pressed);
    public static bool IsDown() => GetCurrentWidgetState().Flags.HasFlag(ElementFlags.Down);
    public static bool HoverEnter() { ref var ws = ref GetCurrentWidgetState(); return ws.Flags.HasFlag(ElementFlags.HoverChanged) && ws.Flags.HasFlag(ElementFlags.Hovered); }
    public static bool HoverExit() { ref var ws = ref GetCurrentWidgetState(); return ws.Flags.HasFlag(ElementFlags.HoverChanged) && !ws.Flags.HasFlag(ElementFlags.Hovered); }
    public static bool HoverChanged() => GetCurrentWidgetState().Flags.HasFlag(ElementFlags.HoverChanged);
    public static bool HasFocus() => _focusId == GetCurrentWidget().Id;
    public static bool HasFocusOn(int id) => _focusId == id;

    internal static bool IsWidgetId(int id) => id > 0 && id < MaxId && _widgets[id] != 0 && _widgetStates[id].LastFrame >= (ushort)(_frame - 1);

    internal static bool IsHoveredById(int id)
    {
        if (!IsWidgetId(id)) return false;
        return _widgetStates[id].Flags.HasFlag(ElementFlags.Hovered);
    }

    internal static bool WasPressedById(int id)
    {
        if (!IsWidgetId(id)) return false;
        return _widgetStates[id].Flags.HasFlag(ElementFlags.Pressed);
    }

    internal static bool IsDownById(int id)
    {
        if (!IsWidgetId(id)) return false;
        return _widgetStates[id].Flags.HasFlag(ElementFlags.Down);
    }

    internal static bool HoverChangedById(int id)
    {
        if (!IsWidgetId(id)) return false;
        return _widgetStates[id].Flags.HasFlag(ElementFlags.HoverChanged);
    }

    internal static bool IsParentRow()
    {
        for (int i = _elementStackCount - 1; i >= 0; i--)
        {
            ref var e = ref GetElement(_elementStack[i]);
            if (e.Type == ElementType.Row) return true;
            if (e.Type == ElementType.Column) return false;
        }
        return false;
    }

    internal static bool IsParentColumn()
    {
        for (int i = _elementStackCount - 1; i >= 0; i--)
        {
            ref var e = ref GetElement(_elementStack[i]);
            if (e.Type == ElementType.Column) return true;
            if (e.Type == ElementType.Row) return false;
        }
        return false;
    }

    internal static Rect GetWidgetWorldRect(int id)
    {
        if (!IsWidgetId(id)) return Rect.Zero;
        ref var e = ref GetElement(_widgets[id]);
        var topLeft = Vector2.Transform(e.Rect.Position, e.LocalToWorld);
        var bottomRight = Vector2.Transform(e.Rect.Position + e.Rect.Size, e.LocalToWorld);
        return new Rect(topLeft.X, topLeft.Y, bottomRight.X - topLeft.X, bottomRight.Y - topLeft.Y);
    }

    internal static Rect GetWidgetRect(int id)
    {
        if (!IsWidgetId(id)) return Rect.Zero;
        return GetElement(_widgets[id]).Rect;
    }

    public static bool HasCapture()
    {
        ref var w = ref GetCurrentWidget();
        return _captureId != 0 && _captureId == w.Id;
    }

    public static void SetFocus()
    {
        ref var w = ref GetCurrentWidget();
        _focusId = w.Id;
    }

    internal static void SetFocusById(int id) => _focusId = id;

    public static void ClearFocus()
    {
        _focusId = 0;
    }

    public static void SetCapture()
    {
        ref var w = ref GetCurrentWidget();
        _captureId = w.Id;
        Input.CaptureMouse();
    }

    internal static void SetCaptureById(int id)
    {
        _captureId = id;
        Input.CaptureMouse();
    }

    internal static bool HasCaptureById(int id) => _captureId != 0 && _captureId == id;

    public static void ReleaseCapture()
    {
        _captureId = 0;
        Input.ReleaseMouseCapture();
    }

    public static ref T GetState<T>() where T : unmanaged
    {
        ref var w = ref GetCurrentWidget();
        Debug.Assert(w.Id > 0 && w.Id < MaxId);
        ref var ws = ref _widgetStates[w.Id];

        ref var writePool = ref _statePools[_currentStatePool];

        // Already allocated this frame — return existing
        if (ws.LastFrame == _frame)
            return ref *(T*)(writePool.Ptr + ws.StateOffset);

        // Bump-allocate in current frame's pool (8-byte aligned)
        var size = (sizeof(T) + 7) & ~7;
        if (!writePool.CheckCapacity(size))
            throw new InvalidOperationException("Widget state pool exceeded capacity.");

        var offset = writePool.Length;
        writePool.AddRange(size);
        var ptr = writePool.Ptr + offset;

        // Copy from previous frame if widget existed last frame
        if (ws.LastFrame == (ushort)(_frame - 1) && ws.StateSize == size)
        {
            ref var readPool = ref _statePools[_currentStatePool ^ 1];
            System.Runtime.InteropServices.NativeMemory.Copy(
                readPool.Ptr + ws.StateOffset, ptr, (nuint)size);
        }
        else
        {
            System.Runtime.InteropServices.NativeMemory.Clear(ptr, (nuint)size);
        }

        ws.StateOffset = offset;
        ws.StateSize = (ushort)size;

        return ref *(T*)ptr;
    }

    internal static ref T GetStateByWidgetId<T>(int widgetId) where T : unmanaged
    {
        Debug.Assert(widgetId > 0 && widgetId < MaxId);
        ref var ws = ref _widgetStates[widgetId];
        ref var writePool = ref _statePools[_currentStatePool];

        if (ws.LastFrame == _frame)
            return ref *(T*)(writePool.Ptr + ws.StateOffset);

        // No state allocated this frame — return zeroed
        var size = (sizeof(T) + 7) & ~7;
        if (!writePool.CheckCapacity(size))
            throw new InvalidOperationException("Widget state pool exceeded capacity.");

        var offset = writePool.Length;
        writePool.AddRange(size);
        var ptr = writePool.Ptr + offset;

        if (ws.LastFrame == (ushort)(_frame - 1) && ws.StateSize == size)
        {
            ref var readPool = ref _statePools[_currentStatePool ^ 1];
            System.Runtime.InteropServices.NativeMemory.Copy(
                readPool.Ptr + ws.StateOffset, ptr, (nuint)size);
        }
        else
        {
            System.Runtime.InteropServices.NativeMemory.Clear(ptr, (nuint)size);
        }

        ws.StateOffset = offset;
        ws.StateSize = (ushort)size;
        return ref *(T*)ptr;
    }

    public static Vector2 GetLocalMousePosition()
    {
        ref var e = ref GetElement(_currentWidget);
        return Vector2.Transform(MouseWorldPosition, GetWorldToLocal(ref e));
    }

    public static int BeginWidget<T>(int id) where T : unmanaged
    {
        var offset = BeginWidget(id);
        ref var e = ref GetElement(offset);
        ref var d = ref GetElementData<WidgetElement>(ref e);
        var wd = _elements.AddRange(sizeof(T));
        d.Data = (ushort)(wd.GetUnsafePtr() - _elements.Ptr);
        return offset;
    }

    public static int BeginWidget(int id)
    {
        ref var e = ref BeginElement<WidgetElement>(ElementType.Widget);
        ref var d = ref GetElementData<WidgetElement>(ref e);
        var offset = (ushort)GetOffset(ref e);
        d.Id = id;
        d.Data = 0;
        _widgets[id] = offset;
        _currentWidget = offset;
        return offset;
    }

    public static void EndWidget()
    {
        EndElement(ElementType.Widget);

        _currentWidget = 0;
        for (int i = _elementStackCount - 1; i >= 0; i--)
        {
            ref var e = ref GetElement(_elementStack[i]);
            if (e.Type == ElementType.Widget)
            {
                _currentWidget = _elementStack[i];
                break;
            }
        }
    }

    public static bool Button(int id, UnsafeSpan<char> text, Font font, float fontSize,
        Color textColor, Color bgColor, Color hoverColor,
        EdgeInsets padding = default, BorderRadius radius = default)
    {
        BeginWidget(id);

        var hovered = IsHovered();
        var down = IsDown();
        var fillColor = down ? hoverColor : (hovered ? hoverColor : bgColor);

        if (radius.TopLeft > 0 || radius.TopRight > 0 || radius.BottomLeft > 0 || radius.BottomRight > 0)
            BeginBorder(0, Color.Transparent, radius);

        BeginFill(fillColor);
        BeginPadding(padding);
        BeginAlign(Align.Center);
        Label(text, font, fontSize, textColor);
        EndAlign();
        EndPadding();
        EndFill();

        if (radius.TopLeft > 0 || radius.TopRight > 0 || radius.BottomLeft > 0 || radius.BottomRight > 0)
            EndBorder();

        var pressed = WasPressed();
        EndWidget();
        return pressed;
    }

    public static bool Toggle(int id, bool value, Sprite icon, Color color, Color activeColor)
    {
        BeginWidget(id);
        var fillColor = value ? activeColor : (IsHovered() ? activeColor.WithAlpha(0.5f) : Color.Transparent);
        BeginFill(fillColor);
        Image(icon, color: value ? Color.White : color);
        EndFill();
        var toggled = WasPressed();
        EndWidget();
        return toggled;
    }

    public static bool Slider(int id, ref float value, float min = 0f, float max = 1f,
        Color trackColor = default, Color fillColor = default, Color thumbColor = default,
        float height = 20f, float trackHeight = 4f)
    {
        if (trackColor.IsTransparent) trackColor = new Color(0.3f, 0.3f, 0.3f, 1f);
        if (fillColor.IsTransparent) fillColor = new Color(0.4f, 0.6f, 1f, 1f);
        if (thumbColor.IsTransparent) thumbColor = Color.White;

        var changed = false;
        var t = max > min ? Math.Clamp((value - min) / (max - min), 0f, 1f) : 0f;

        BeginWidget(id);
        BeginSize(Size.Percent(1), new Size(height));

        // Track background (centered vertically)
        BeginAlign(new Align2(Align.Min, Align.Center));
        BeginSize(Size.Percent(1), new Size(trackHeight));
        BeginFill(trackColor);
        EndFill();
        EndSize();
        EndAlign();

        // Fill bar
        if (t > 0)
        {
            BeginAlign(new Align2(Align.Min, Align.Center));
            BeginSize(Size.Percent(t), new Size(trackHeight));
            BeginFill(IsDown() ? fillColor : fillColor.WithAlpha(0.8f));
            EndFill();
            EndSize();
            EndAlign();
        }

        // Input: capture on down, update value while captured
        if (IsDown() && !HasCapture())
            SetCapture();

        if (HasCapture())
        {
            var localMouse = GetLocalMousePosition();
            ref var we = ref GetElement(_currentWidget);
            var widgetWidth = we.Rect.Width;
            if (widgetWidth > 0)
            {
                var localX = Math.Clamp(localMouse.X / widgetWidth, 0f, 1f);
                var newValue = min + localX * (max - min);
                newValue = Math.Clamp(newValue, min, max);

                if (newValue != value)
                {
                    value = newValue;
                    changed = true;
                }
            }
        }

        EndSize();
        EndWidget();
        return changed;
    }
}
