//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace noz;

public readonly struct SoundHandle
{
    public readonly ulong Value;

    public bool IsValid => Value != 0;

    internal SoundHandle(ulong value) => Value = value;

    // Encode: [generation(32) | index(32)]
    internal static SoundHandle Create(uint generation, uint index)
        => new((ulong)generation << 32 | index);

    internal uint Generation => (uint)(Value >> 32);
    internal uint Index => (uint)(Value & 0xFFFFFFFF);
}
