//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ.Engine.UI;

[Flags]
internal enum ElementFlags : uint
{
    None = 0,
    Hovered = 1 << 0,
    Pressed = 1 << 1,
    Down = 1 << 2
}

internal struct ElementState
{
    public ElementFlags Flags;
    public int Index;
    public float ScrollOffset;
    public Rect Rect;
    public string Text;
}

internal struct CanvasState
{
    public int ElementIndex;
    public Rect WorldBounds;
    public ElementState[]? ElementStates;
}
