//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ;

public class Texture : Asset
{
    internal const byte Version = 1;

    public int Width { get; private init; }
    public int Height { get; private init; }
    public TextureFormat Format { get; private set; }
    public TextureFilter Filter { get; private set; }
    public TextureClamp Clamp { get; private set; }
    public byte[] Data { get; private init; } = [];

    internal nuint Handle { get; private set; }

    private Texture(string name) : base(AssetType.Texture, name)
    {
    }

    public static Texture Create(
        int width,
        int height,
        ReadOnlySpan<byte> data,
        TextureFormat format = TextureFormat.RGBA8,
        TextureFilter filter = TextureFilter.Linear,
        string name = "")
    {
        var texture = new Texture(name)
        {
            Width = width,
            Height = height,
            Format = format,
            Filter = filter,
            Clamp = TextureClamp.Clamp,
            Data = data.ToArray()
        };
        texture.Upload();
        return texture;
    }

    private static Texture? Load(Stream stream, string name)
    {
        using var reader = new BinaryReader(stream);

        var format = (TextureFormat)reader.ReadByte();
        var filter = (TextureFilter)reader.ReadByte();
        var clamp = (TextureClamp)reader.ReadByte();
        var width = (int)reader.ReadUInt32();
        var height = (int)reader.ReadUInt32();

        var dataSize = width * height * GetBytesPerPixel(format);
        var data = reader.ReadBytes(dataSize);

        var texture = new Texture(name)
        {
            Width = width,
            Height = height,
            Format = format,
            Filter = filter,
            Clamp = clamp,
            Data = data
        };

        texture.Upload();

        return texture;
    }

    public void Upload()
    {
        if (Handle != nuint.Zero)
            Render.Driver.DestroyTexture(Handle);
        Handle = Render.Driver.CreateTexture(Width, Height, Data, Format, Filter);
    }

    internal static void RegisterDef()
    {
        RegisterDef(new AssetDef(AssetType.Texture, typeof(Texture), Load));
    }

    public override void Dispose()
    {
        if (Handle != nuint.Zero)
        {
            Render.Driver.DestroyTexture(Handle);
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
