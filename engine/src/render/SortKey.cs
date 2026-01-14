//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Runtime.CompilerServices;

namespace noz;

/// <summary>
/// Sort key for render ordering.
/// Sorting is by Layer > Group > Depth (preserves game's draw order within same depth).
/// - Layer: Major categories (background=0, characters=100, UI=200)
/// - Group: Keeps related parts together (e.g., character body parts)
/// - Depth: Fine ordering within a group
/// Render state (texture/shader/blend) is stored separately for batching but NOT sorting.
/// </summary>
public readonly struct SortKey(ushort group, byte layer, byte shader, byte blend, ushort texture, ushort depth)
    : IComparable<SortKey>, IEquatable<SortKey>
{
    // Sort order value: Layer(8) | Group(16) | Depth(12) = 36 bits
    private readonly ulong _value = ((ulong)group << GroupShift) |
                                        ((ulong)layer << LayerShift) |
                                        ((ulong)(depth & DepthMask) << DepthShift);

    // Render state: Texture(16) | Shader(8) | Blend(8) = 32 bits
    private readonly uint _state = ((uint)texture << StateTextureShift) |
                                   ((uint)shader << StateShaderShift) |
                                   ((uint)blend << StateBlendShift);

    // Bit positions for sort value (from LSB, higher = more priority)
    private const int DepthShift = 0;       // 12 bits
    private const int GroupShift = 12;      // 16 bits
    private const int LayerShift = 28;      // 8 bits

    // Bit positions for state (from LSB)
    private const int StateBlendShift = 0;      // 8 bits
    private const int StateShaderShift = 8;     // 8 bits
    private const int StateTextureShift = 16;   // 16 bits

    // Masks
    private const ulong DepthMask = 0xFFF;      // 12 bits (0-4095)
    private const ulong LayerMask = 0xFF;       // 8 bits
    private const ulong GroupMask = 0xFFFF;     // 16 bits
    private const uint BlendMask = 0xFF;        // 8 bits
    private const uint ShaderMask = 0xFF;       // 8 bits
    private const uint TextureMask = 0xFFFF;    // 16 bits

    // Sort order components
    public ushort Group => (ushort)((_value >> GroupShift) & GroupMask);
    public byte Layer => (byte)((_value >> LayerShift) & LayerMask);
    public ushort Depth => (ushort)((_value >> DepthShift) & DepthMask);

    // Render state components
    public ushort Texture => (ushort)((_state >> StateTextureShift) & TextureMask);
    public byte Shader => (byte)((_state >> StateShaderShift) & ShaderMask);
    public byte Blend => (byte)((_state >> StateBlendShift) & BlendMask);

    // For sorting - compares only Group, Layer, Depth (NOT state)
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int CompareTo(SortKey other) => _value.CompareTo(other._value);

    public bool Equals(SortKey other) => _value == other._value && _state == other._state;
    public override bool Equals(object? obj) => obj is SortKey other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(_value, _state);

    public static bool operator ==(SortKey left, SortKey right) => left._value == right._value;
    public static bool operator !=(SortKey left, SortKey right) => left._value != right._value;
    public static bool operator <(SortKey left, SortKey right) => left._value < right._value;
    public static bool operator >(SortKey left, SortKey right) => left._value > right._value;
    public static bool operator <=(SortKey left, SortKey right) => left._value <= right._value;
    public static bool operator >=(SortKey left, SortKey right) => left._value >= right._value;

    /// <summary>
    /// Check if two keys can be batched together.
    /// Commands can batch if they have the same render state (texture, shader, blend).
    /// Sort order (group, layer, depth) doesn't matter for batching - adjacent commands
    /// with same state can always be merged.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool CanBatchWith(SortKey other) => _state == other._state;

    public override string ToString() =>
        $"SortKey(Group={Group}, Layer={Layer}, Depth={Depth}, Texture={Texture}, Shader={Shader}, Blend={Blend})";
}
