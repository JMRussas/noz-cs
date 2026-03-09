//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ;

[Flags]
public enum WidgetFlags : ushort
{
    None = 0,
    Hovered = 1 << 0,
    Pressed = 1 << 1,
    Down = 1 << 2,
    Hot = 1 << 3,
    Dragging = 1 << 4,
    Changed = 1 << 5,
    DoubleClick = 1 << 6,
    RightClick = 1 << 7,
    HoverChanged = 1 << 8,
    Disabled = 1 << 9,
    Checked = 1 << 10,
}

public struct EditableTextState
{
    public UnsafeSpan<char> EditText;
    public int CursorIndex;
    public int SelectionStart;
    public int TextHash;
    public int PrevTextHash;
    public byte Focused;
    public byte FocusExited;
    public byte WasCancelled;
    public byte JustFocused;
    public int FocusClickIndex;
}

public struct ScrollState
{
    public float Offset;
    public float ContentHeight;
}

