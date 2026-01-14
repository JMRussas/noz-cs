//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace noz;

public readonly struct BufferHandle(uint id) : IEquatable<BufferHandle>
{
    public readonly uint Id = id;

    public bool IsValid => Id != 0;

    public bool Equals(BufferHandle other) => Id == other.Id;
    public override bool Equals(object? obj) => obj is BufferHandle other && Equals(other);
    public override int GetHashCode() => Id.GetHashCode();

    public static bool operator ==(BufferHandle left, BufferHandle right) => left.Id == right.Id;
    public static bool operator !=(BufferHandle left, BufferHandle right) => left.Id != right.Id;

    public static readonly BufferHandle Invalid = new(0);
}
