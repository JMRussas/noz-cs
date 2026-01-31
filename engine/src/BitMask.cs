//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Numerics;

namespace NoZ;

[StructLayout(LayoutKind.Sequential)]
public unsafe struct BitMask256
{
    public fixed ulong Bits[4];
    public int Count { get; private set; }

    public bool this[int index]
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            Debug.Assert((uint)index < 256);
            return (Bits[index >> 6] & (1UL << index)) != 0;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set
        {
            Debug.Assert((uint)index < 256);
            ref ulong slot = ref Bits[index >> 6];
            ulong mask = 1UL << index;
            ulong oldBit = slot & mask;
            ulong newBit = (ulong)(-(long)Unsafe.As<bool, byte>(ref value)) & mask;
            slot = (slot & ~mask) | newBit;
            Count += (int)((long)(newBit - oldBit) >> (index & 63));
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static BitMask256 operator |(BitMask256 a, BitMask256 b)
    {
        BitMask256 result = default;
        result.Bits[0] = a.Bits[0] | b.Bits[0];
        result.Bits[1] = a.Bits[1] | b.Bits[1];
        result.Bits[2] = a.Bits[2] | b.Bits[2];
        result.Bits[3] = a.Bits[3] | b.Bits[3];
        result.Count = BitOperations.PopCount(result.Bits[0])
                     + BitOperations.PopCount(result.Bits[1])
                     + BitOperations.PopCount(result.Bits[2])
                     + BitOperations.PopCount(result.Bits[3]);
        return result;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static BitMask256 operator &(BitMask256 a, BitMask256 b)
    {
        BitMask256 result = default;
        result.Bits[0] = a.Bits[0] & b.Bits[0];
        result.Bits[1] = a.Bits[1] & b.Bits[1];
        result.Bits[2] = a.Bits[2] & b.Bits[2];
        result.Bits[3] = a.Bits[3] & b.Bits[3];
        result.Count = BitOperations.PopCount(result.Bits[0])
                     + BitOperations.PopCount(result.Bits[1])
                     + BitOperations.PopCount(result.Bits[2])
                     + BitOperations.PopCount(result.Bits[3]);
        return result;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static BitMask256 operator ^(BitMask256 a, BitMask256 b)
    {
        BitMask256 result = default;
        result.Bits[0] = a.Bits[0] ^ b.Bits[0];
        result.Bits[1] = a.Bits[1] ^ b.Bits[1];
        result.Bits[2] = a.Bits[2] ^ b.Bits[2];
        result.Bits[3] = a.Bits[3] ^ b.Bits[3];
        result.Count = BitOperations.PopCount(result.Bits[0])
                     + BitOperations.PopCount(result.Bits[1])
                     + BitOperations.PopCount(result.Bits[2])
                     + BitOperations.PopCount(result.Bits[3]);
        return result;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static BitMask256 operator ~(BitMask256 a)
    {
        BitMask256 result = default;
        result.Bits[0] = ~a.Bits[0];
        result.Bits[1] = ~a.Bits[1];
        result.Bits[2] = ~a.Bits[2];
        result.Bits[3] = ~a.Bits[3];
        result.Count = 256 - a.Count;
        return result;
    }

    public void Clear()
    {
        Count = 0;
        Bits[0] = 0;
        Bits[1] = 0;
        Bits[2] = 0;
        Bits[3] = 0;
    }
}
