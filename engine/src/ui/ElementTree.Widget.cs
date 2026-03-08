//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace NoZ;

public static unsafe partial class ElementTree
{
    internal static bool HasCurrentWidget => _currentWidget != 0;
    internal static ElementFlags CurrentWidgetFlags => CurrentWidget.Data.Widget.Flags;

    private static ref Element CurrentWidget
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            Debug.Assert(_currentWidget != 0);
            return ref GetWidget(_currentWidget);    
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsValidWidgetId(int id) => id > 0 && id <= MaxId && CurrentBuffer.Widgets[id] != 0;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ref Element GetWidget(int id) =>
        ref GetElement(CurrentBuffer.Widgets[id]);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool WasWidgetInPrevFrame(int id)
    {
        Debug.Assert(id != 0 && id <= MaxId);
        return PreviousBuffer.Widgets[id] != 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ref Element GetWidgetFromPrevFrame(int id) =>
        ref GetElementFromPrevFrame(PreviousBuffer.Widgets[id]);

    internal static void SetWidgetFlag(ElementFlags flag, bool value)
    {
        ref var e = ref CurrentWidget;
        if (value) e.Data.Widget.Flags |= flag;
        else e.Data.Widget.Flags &= ~flag;
    }

    public static bool IsHovered() => CurrentWidgetFlags.HasFlag(ElementFlags.Hovered);
    public static bool IsDisabled() => CurrentWidgetFlags.HasFlag(ElementFlags.Disabled);
    public static bool IsChecked() => CurrentWidgetFlags.HasFlag(ElementFlags.Checked);
    public static bool WasPressed() => CurrentWidgetFlags.HasFlag(ElementFlags.Pressed);
    public static bool IsDown() => CurrentWidgetFlags.HasFlag(ElementFlags.Down);
    public static bool HoverEnter() { var f = CurrentWidgetFlags; return f.HasFlag(ElementFlags.HoverChanged) && f.HasFlag(ElementFlags.Hovered); }
    public static bool HoverExit() { var f = CurrentWidgetFlags; return f.HasFlag(ElementFlags.HoverChanged) && !f.HasFlag(ElementFlags.Hovered); }
    public static bool HoverChanged() => CurrentWidgetFlags.HasFlag(ElementFlags.HoverChanged);
    public static bool HasFocus() => _focusId == CurrentWidget.Data.Widget.Id;
    public static bool HasFocusOn(int id) => _focusId == id;

    internal static bool IsHovered(int id)
    {
        if (!IsValidWidgetId(id)) return false;
        ref var e = ref GetWidget(id);
        return e.Data.Widget.Flags.HasFlag(ElementFlags.Hovered);
    }

    internal static bool WasPressed(int id)
    {
        if (!IsValidWidgetId(id)) return false;
        ref var e = ref GetWidget(id);
        return e.Data.Widget.Flags.HasFlag(ElementFlags.Pressed);
    }

    internal static bool IsDown(int id)
    {
        if (!IsValidWidgetId(id)) return false;
        ref var e = ref GetWidget(id);
        return e.Data.Widget.Flags.HasFlag(ElementFlags.Down);
    }

    internal static bool HoverChanged(int id)
    {
        if (!IsValidWidgetId(id)) return false;
        ref var e = ref GetWidget(id);
        return e.Data.Widget.Flags.HasFlag(ElementFlags.HoverChanged);
    }

    internal static bool IsParentRow() => GetElement(_stack[^1]).Type == ElementType.Row;
    internal static bool IsParentColumn() => GetElement(_stack[^1]).Type == ElementType.Column;

    internal static Rect GetWidgetWorldRect(int id)
    {
        if (!IsValidWidgetId(id)) return Rect.Zero;
        ref var e = ref GetWidget(id);
        ref var ltw = ref e.Transform;
        var topLeft = Vector2.Transform(e.Rect.Position, ltw);
        var bottomRight = Vector2.Transform(e.Rect.Position + e.Rect.Size, ltw);
        return new Rect(topLeft.X, topLeft.Y, bottomRight.X - topLeft.X, bottomRight.Y - topLeft.Y);
    }

    internal static Rect GetWidgetRect(int id)
    {
        if (!IsValidWidgetId(id)) return Rect.Zero;
        return GetWidget(id).Rect;
    }

    public static bool HasCapture()
    {
        ref var e = ref CurrentWidget;
        return _captureId != 0 && _captureId == e.Data.Widget.Id;
    }

    public static void SetFocus()
    {
        ref var e = ref CurrentWidget;
        _focusId = e.Data.Widget.Id;
    }

    internal static void SetFocusById(int id) => _focusId = id;

    public static void ClearFocus()
    {
        _focusId = 0;
    }

    public static void SetCapture()
    {
        ref var e = ref CurrentWidget;
        _captureId = e.Data.Widget.Id;
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

    public static ref T GetWidgetState<T>() where T : unmanaged
    {
        ref var w = ref CurrentWidget;
        Debug.Assert(w.Data.Widget.Id != 0);
        return ref *(T*)(w.Data.Widget.State.GetUnsafePtr());
    }

    public static Vector2 GetLocalMousePosition()
    {
        ref var e = ref GetElement(_currentWidget);
        Matrix3x2.Invert(e.Transform, out var inv);
        return Vector2.Transform(MouseWorldPosition, inv);
    }

    public static ref T BeginWidget<T>(int id) where T : unmanaged
    {
        ref var buffer = ref CurrentBuffer;
        var data = AllocData(sizeof(T));

        if (WasWidgetInPrevFrame(id))
        {
            ref var prev_e = ref GetWidgetFromPrevFrame(id);
            *((T*)data.GetUnsafePtr()) = *((T*)prev_e.Data.Widget.State.GetUnsafePtr());
        }

        BeginWidget(id);
        ref var e = ref GetElement(_currentWidget);
        e.Data.Widget.State = data;
        return ref *(T*)data.GetUnsafePtr();
    }

    public static int BeginWidget(int id)
    {
        Debug.Assert(id >= 0);
        Debug.Assert(id <= MaxId, $"Widget ID {id} exceeds maximum of {MaxId}");

        ref var e = ref BeginElement(ElementType.Widget);
        e = default;

        if (WasWidgetInPrevFrame(id))
        {
            ref var prev_e = ref GetWidgetFromPrevFrame(id);
            e.Data.Widget = prev_e.Data.Widget;
        }

        e.Data.Widget.Id = (ushort)id;
        e.Data.Widget.LastFrame = _frame;

        CurrentBuffer.Widgets[id] = e.Index;
        _currentWidget = e.Index;
        return _currentWidget;
    }

    public static void EndWidget()
    {
        EndElement(ElementType.Widget);

        _currentWidget = 0;
        for (int i = _stack.Length - 1; i >= 0; i--)
        {
            ref var e = ref GetElement(_stack[i]);
            if (e.Type == ElementType.Widget)
            {
                _currentWidget = _stack[i];
                break;
            }
        }
    }

    public static bool Button(int id, ReadOnlySpan<char> text, Font font, float fontSize,
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
        Text(text, font, fontSize, textColor);
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
