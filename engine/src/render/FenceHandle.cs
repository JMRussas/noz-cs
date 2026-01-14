//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace noz;

/// <summary>
/// Opaque handle to a GPU synchronization fence.
/// Used for triple-buffered ring buffer synchronization.
/// </summary>
public readonly struct FenceHandle(ulong id) : IEquatable<FenceHandle>
{
    public readonly ulong Id = id;

    public bool IsValid => Id != 0;

    public bool Equals(FenceHandle other) => Id == other.Id;
    public override bool Equals(object? obj) => obj is FenceHandle other && Equals(other);
    public override int GetHashCode() => Id.GetHashCode();

    public static bool operator ==(FenceHandle left, FenceHandle right) => left.Id == right.Id;
    public static bool operator !=(FenceHandle left, FenceHandle right) => left.Id != right.Id;

    public static readonly FenceHandle Invalid = new(0);
}
