//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ;

public readonly struct AssetType : IEquatable<AssetType>
{
    public readonly uint Value;

    public AssetType(uint value) => Value = value;

    public static AssetType FromString(string fourCC)
    {
        if (fourCC.Length != 4)
            throw new ArgumentException("AssetType FourCC must be exactly 4 characters", nameof(fourCC));
        return new AssetType(
            (uint)fourCC[0] | ((uint)fourCC[1] << 8) |
            ((uint)fourCC[2] << 16) | ((uint)fourCC[3] << 24));
    }

    public static readonly AssetType Unknown   = default;
    public static readonly AssetType Texture   = FromString("TEXR");
    public static readonly AssetType Sprite    = FromString("SPRT");
    public static readonly AssetType Sound     = FromString("SOND");
    public static readonly AssetType Shader    = FromString("SHDR");
    public static readonly AssetType Font      = FromString("FONT");
    public static readonly AssetType Animation = FromString("ANIM");
    public static readonly AssetType Skeleton  = FromString("SKEL");
    public static readonly AssetType Atlas     = FromString("ATLS");
    public static readonly AssetType Vfx       = FromString("VFX_");
    public static readonly AssetType Lua       = FromString("LUA_");
    public static readonly AssetType Event     = FromString("EVNT");
    public static readonly AssetType Bin       = FromString("BIN_");
    public static readonly AssetType Bundle    = FromString("BNDL");

    public bool Equals(AssetType other) => Value == other.Value;
    public override bool Equals(object? obj) => obj is AssetType other && Equals(other);
    public override int GetHashCode() => (int)Value;
    public static bool operator ==(AssetType left, AssetType right) => left.Value == right.Value;
    public static bool operator !=(AssetType left, AssetType right) => left.Value != right.Value;

    public override string ToString()
    {
        if (Value == 0) return "Unknown";
        Span<char> chars = stackalloc char[4];
        chars[0] = (char)(Value & 0xFF);
        chars[1] = (char)((Value >> 8) & 0xFF);
        chars[2] = (char)((Value >> 16) & 0xFF);
        chars[3] = (char)((Value >> 24) & 0xFF);
        return new string(chars).TrimEnd('_');
    }
}
