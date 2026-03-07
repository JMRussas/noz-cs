//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ;

[Flags]
public enum ElementFlags : ushort
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
    Disabled = 1 << 9,
    Checked = 1 << 10,
}
