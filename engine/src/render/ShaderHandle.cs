//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace noz;

/// <summary>
/// Opaque handle to a GPU shader program.
/// Uses byte (8 bits) to fit in SortKey.
/// </summary>
public readonly struct ShaderHandle : IEquatable<ShaderHandle>
{
    public readonly byte Id;

    public bool IsValid => Id != 0;

    public ShaderHandle(byte id) => Id = id;

    public bool Equals(ShaderHandle other) => Id == other.Id;
    public override bool Equals(object? obj) => obj is ShaderHandle other && Equals(other);
    public override int GetHashCode() => Id.GetHashCode();

    public static bool operator ==(ShaderHandle left, ShaderHandle right) => left.Id == right.Id;
    public static bool operator !=(ShaderHandle left, ShaderHandle right) => left.Id != right.Id;

    public static readonly ShaderHandle Invalid = new(0);
}
