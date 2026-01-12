//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Runtime.CompilerServices;

namespace noz;

/// <summary>
/// 64-bit sort key for render ordering.
/// Bit layout: Group(16) | Layer(8) | Shader(8) | Blend(4) | Texture(16) | Depth(12)
/// Higher bits = higher priority in sort order.
/// </summary>
public readonly struct SortKey : IComparable<SortKey>, IEquatable<SortKey>
{
    private readonly ulong _value;

    // Bit positions (from LSB)
    private const int DepthShift = 0;
    private const int TextureShift = 12;
    private const int BlendShift = 28;
    private const int ShaderShift = 32;
    private const int LayerShift = 40;
    private const int GroupShift = 48;

    // Bit masks
    private const ulong DepthMask = 0xFFF;           // 12 bits (0-4095)
    private const ulong TextureMask = 0xFFFF;        // 16 bits
    private const ulong BlendMask = 0xF;             // 4 bits
    private const ulong ShaderMask = 0xFF;           // 8 bits
    private const ulong LayerMask = 0xFF;            // 8 bits
    private const ulong GroupMask = 0xFFFF;          // 16 bits

    public SortKey(ulong value) => _value = value;

    public SortKey(ushort group, byte layer, byte shader, byte blend, ushort texture, ushort depth)
    {
        _value = ((ulong)group << GroupShift) |
                 ((ulong)layer << LayerShift) |
                 ((ulong)shader << ShaderShift) |
                 ((ulong)blend << BlendShift) |
                 ((ulong)texture << TextureShift) |
                 ((ulong)(depth & DepthMask) << DepthShift);
    }

    public ushort Group => (ushort)((_value >> GroupShift) & GroupMask);
    public byte Layer => (byte)((_value >> LayerShift) & LayerMask);
    public byte Shader => (byte)((_value >> ShaderShift) & ShaderMask);
    public byte Blend => (byte)((_value >> BlendShift) & BlendMask);
    public ushort Texture => (ushort)((_value >> TextureShift) & TextureMask);
    public ushort Depth => (ushort)((_value >> DepthShift) & DepthMask);

    public ulong Value => _value;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int CompareTo(SortKey other) => _value.CompareTo(other._value);

    public bool Equals(SortKey other) => _value == other._value;
    public override bool Equals(object? obj) => obj is SortKey other && Equals(other);
    public override int GetHashCode() => _value.GetHashCode();

    public static bool operator ==(SortKey left, SortKey right) => left._value == right._value;
    public static bool operator !=(SortKey left, SortKey right) => left._value != right._value;
    public static bool operator <(SortKey left, SortKey right) => left._value < right._value;
    public static bool operator >(SortKey left, SortKey right) => left._value > right._value;
    public static bool operator <=(SortKey left, SortKey right) => left._value <= right._value;
    public static bool operator >=(SortKey left, SortKey right) => left._value >= right._value;

    /// <summary>
    /// Check if two keys can be batched together (same state except depth).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool CanBatchWith(SortKey other)
    {
        // Everything except depth must match
        const ulong stateMask = ~(DepthMask << DepthShift);
        return (_value & stateMask) == (other._value & stateMask);
    }

    public override string ToString() =>
        $"SortKey(Group={Group}, Layer={Layer}, Shader={Shader}, Blend={Blend}, Texture={Texture}, Depth={Depth})";
}
