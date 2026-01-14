//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace noz;

public delegate Asset? LoadAssetDelegate(Stream stream, string name);

public class AssetDef(AssetType type, Type runtimeType, LoadAssetDelegate load)
{
    public AssetType Type { get; } = type;
    public Type RuntimeType { get; } = runtimeType;
    internal LoadAssetDelegate Load { get; } = load;
}

