//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ;

public struct BundleEntry
{
    public StringId Name;
    public AssetType Type;
    public Asset Asset;
}

public class AssetBundle : Asset
{
    public static readonly AssetType Type = AssetType.Bundle;
    public const ushort Version = 1;

    public BundleEntry[] Entries { get; private set; } = [];

    private AssetBundle(string name) : base(Type, name) { }

    public T? Get<T>(StringId name) where T : Asset
    {
        for (var i = 0; i < Entries.Length; i++)
        {
            if (Entries[i].Name == name)
                return Entries[i].Asset as T;
        }
        return null;
    }

    public T? Get<T>(string name) where T : Asset
    {
        return Get<T>(StringId.Get(name));
    }

    public override void PostLoad()
    {
        for (var i = 0; i < Entries.Length; i++)
            Entries[i].Asset.PostLoad();
    }

    private static Asset? Load(Stream stream, string name)
    {
        using var reader = new BinaryReader(stream);
        var bundle = new AssetBundle(name);

        var entryCount = reader.ReadInt32();
        bundle.Entries = new BundleEntry[entryCount];

        for (var i = 0; i < entryCount; i++)
        {
            var entryName = reader.ReadString();
            var assetType = new AssetType(reader.ReadUInt32());
            var dataSize = reader.ReadInt32();
            var data = reader.ReadBytes(dataSize);

            using var assetStream = new MemoryStream(data);
            var scopedName = $"{name}/{entryName}";
            var asset = Asset.LoadFromStream(assetType, assetStream, scopedName);

            bundle.Entries[i] = new BundleEntry
            {
                Name = StringId.Get(entryName),
                Type = assetType,
                Asset = asset!
            };
        }

        return bundle;
    }

    public static void RegisterDef()
    {
        RegisterDef(new AssetDef(Type, "AssetBundle", typeof(AssetBundle), Load, Version));
    }
}
