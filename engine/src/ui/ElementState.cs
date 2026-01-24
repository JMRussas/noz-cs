//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;
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

internal struct ScrollableState
{
    public float ScrollOffset;
}

[StructLayout(LayoutKind.Explicit)]
internal struct ElementStateData
{
    [FieldOffset(0)] public TextBoxState TextBox;
    [FieldOffset(0)] public ScrollableData Scrollable;
}

internal struct ElementState
{
    public ElementFlags Flags;
    public short Index;
    public Rect Rect;
    public ElementStateData Data;

    public readonly bool HasFocus => (Flags & ElementFlags.Focus) != 0;
    public readonly bool IsHovered => (Flags & ElementFlags.Hovered) != 0;
    public readonly bool IsPressed => (Flags & ElementFlags.Pressed) != 0;
    public readonly bool IsDown => (Flags & ElementFlags.Down) != 0;
    public readonly bool IsDragging => (Flags & ElementFlags.Dragging) != 0;
    public readonly bool IsChanged => (Flags & ElementFlags.Changed) != 0;
    public void SetFlags(ElementFlags mask, ElementFlags flags) =>
        Flags = (Flags & ~mask) | (flags & mask);
}

internal struct CanvasState
{
    public short ElementIndex;
    public Rect WorldBounds;
    public UnsafeSpan<ElementState> ElementStates;
}
