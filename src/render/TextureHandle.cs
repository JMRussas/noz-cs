//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace noz;

/// <summary>
/// Opaque handle to a GPU texture.
/// Uses ushort (16 bits) to fit in SortKey.
/// </summary>
public readonly struct TextureHandle(ushort id) : IEquatable<TextureHandle>
{
    public readonly ushort Id = id;

    public bool IsValid => Id != 0;

    public bool Equals(TextureHandle other) => Id == other.Id;
    public override bool Equals(object? obj) => obj is TextureHandle other && Equals(other);
    public override int GetHashCode() => Id.GetHashCode();

    public static bool operator ==(TextureHandle left, TextureHandle right) => left.Id == right.Id;
    public static bool operator !=(TextureHandle left, TextureHandle right) => left.Id != right.Id;

    public static readonly TextureHandle Invalid = new(0);
    public static readonly TextureHandle White = new(1); // Built-in 1x1 white texture
}
