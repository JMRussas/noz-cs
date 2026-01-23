//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ;

public class Atlas : Asset
{
    internal const ushort Version = 1;

    public int Width { get; private init; }
    public int Height { get; private init; }
    public TextureFormat Format { get; private set; }
    public TextureFilter Filter { get; private set; }
    public TextureClamp Clamp { get; private set; }
    public byte[] Data { get; private init; } = [];
    public int Index { get; internal set; }

    private Atlas(string name) : base(AssetType.Atlas, name)
    {
    }

    private static Atlas? Load(Stream stream, string name)
    {
        using var reader = new BinaryReader(stream);

        var format = (TextureFormat)reader.ReadByte();
        var filter = (TextureFilter)reader.ReadByte();
        var clamp = (TextureClamp)reader.ReadByte();
        var width = (int)reader.ReadUInt32();
        var height = (int)reader.ReadUInt32();

        var dataSize = width * height * GetBytesPerPixel(format);
        var data = reader.ReadBytes(dataSize);

        var atlas = new Atlas(name)
        {
            Width = width,
            Height = height,
            Format = format,
            Filter = filter,
            Clamp = clamp,
            Data = data
        };

        return atlas;
    }

    internal static void RegisterDef()
    {
        RegisterDef(new AssetDef(AssetType.Atlas, typeof(Atlas), Load));
    }

    public override void Dispose()
    {
        if (Handle != nuint.Zero)
        {
            Graphics.Driver.DestroyTexture(Handle);
            Handle = nuint.Zero;
        }

        base.Dispose();
    }

    private static int GetBytesPerPixel(TextureFormat format) => format switch
    {
        TextureFormat.RGBA8 => 4,
        TextureFormat.RGB8 => 3,
        TextureFormat.RG8 => 2,
        TextureFormat.R8 => 1,
        _ => 4
    };
}
