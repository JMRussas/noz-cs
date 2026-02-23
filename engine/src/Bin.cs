//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ;

public class Bin : Asset
{
    internal const ushort Version = 1;

    public byte[] Data { get; private set; } = [];

    private Bin(string name) : base(AssetType.Bin, name) { }

    private static Asset Load(Stream stream, string name)
    {
        using var reader = new BinaryReader(stream);
        var size = reader.ReadInt32();
        return new Bin(name) { Data = reader.ReadBytes(size) };
    }

    internal static void RegisterDef()
    {
        RegisterDef(new AssetDef(AssetType.Bin, "Bin", typeof(Bin), Load, Version));
    }
}
