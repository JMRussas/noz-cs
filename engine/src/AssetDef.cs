//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ;

public delegate Asset? LoadAssetDelegate(Stream stream, string name);

public class AssetDef(AssetType type, string name, Type runtimeType, LoadAssetDelegate load, ushort version = 0)
{
    public AssetType Type { get; } = type;
    public string Name { get; } = name;
    public Type RuntimeType { get; } = runtimeType;
    public ushort Version { get; } = version;
    internal LoadAssetDelegate Load { get; } = load;
}

