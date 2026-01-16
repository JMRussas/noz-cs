//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ;

internal struct RenderCommand : IComparable<RenderCommand>
{
    public long SortKey;
    public int IndexOffset;
    public int IndexCount;
    public ushort BatchState;

    public int CompareTo(RenderCommand x)
    {
        var diff = SortKey - x.SortKey;
        return diff < 0 ? -1 : (diff > 0 ? 1 : 0);
    }
}
