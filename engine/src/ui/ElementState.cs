//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Runtime.InteropServices;

namespace NoZ;

[Flags]
internal enum ElementFlags : ushort
{
    None = 0,
    Hovered = 1 << 0,
    Pressed = 1 << 1,
    Down = 1 << 2,
    Focus = 1 << 3,
    Dragging = 1 << 4,
    Changed = 1 << 5,
    DoubleClick = 1 << 6,
    RightClick = 1 << 7,
    HoverChanged = 1 << 8,
}

internal struct TextBoxState
{
    public int CursorIndex;
    public int SelectionStart;
    public float ScrollOffset;
    public float BlinkTimer;
    public int TextHash;
    public UnsafeSpan<char> Text;
}

internal struct TextAreaState
{
    public int CursorIndex;
    public int SelectionStart;
    public float ScrollOffset;
    public float BlinkTimer;
    public int TextHash;
    public float DesiredColumn;
    public UnsafeSpan<char> Text;
}

internal struct ScrollableState
{
    public float ScrollOffset;
}

[StructLayout(LayoutKind.Explicit)]
internal struct ElementStateData
{
    [FieldOffset(0)] public TextBoxState TextBox;
    [FieldOffset(0)] public TextAreaState TextArea;
    [FieldOffset(0)] public ScrollableData Scrollable;
}

internal struct ElementState
{
    public ElementFlags Flags;
    public short Index;
    public Rect Rect;
    public System.Numerics.Matrix3x2 LocalToWorld;
    public ElementStateData Data;
    public ushort LastFrame;
    public Tween Tween;

    public readonly bool HasFocus => (Flags & ElementFlags.Focus) != 0;
    public readonly bool IsHovered => (Flags & ElementFlags.Hovered) != 0;
    public readonly bool IsHoverChanged => (Flags & ElementFlags.HoverChanged) != 0;
    public readonly bool IsPressed => (Flags & ElementFlags.Pressed) != 0;
    public readonly bool IsDown => (Flags & ElementFlags.Down) != 0;
    public readonly bool IsDragging => (Flags & ElementFlags.Dragging) != 0;
    public readonly bool IsChanged => (Flags & ElementFlags.Changed) != 0;
    public void SetFlags(ElementFlags mask, ElementFlags flags) =>
        Flags = (Flags & ~mask) | (flags & mask);
}
