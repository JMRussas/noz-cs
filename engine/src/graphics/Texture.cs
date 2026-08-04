//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Runtime.CompilerServices;

namespace NoZ;

public interface ITexture : IImage
{
    public nuint Handle { get; }
    public TextureFilter Filter { get; }
}

public class Texture : Asset, ITexture
{
    internal const ushort Version = 1;

    public int Width { get; private set; }
    public int Height { get; private set; }
    public TextureFormat Format { get; private set; }
    public TextureFilter Filter { get; private set; }
    public TextureClamp Clamp { get; private set; }
    public NativeArray<byte> Data { get; private set; }
    public bool IsArray { get; private set; }

    float IImage.ImageWidth => Width;
    float IImage.ImageHeight => Height;
    nuint ITexture.Handle => this.Native;

    private Texture(string name, bool isArray) : base(AssetType.Texture, name)
    {
        IsArray = isArray;
    }

    protected Texture(nuint handle, int width, int height, TextureFormat format) : base(AssetType.Texture)
    {
        Width = width;
        Height = height;
        Format = format;
        Native = handle;
    }

    public Texture() : base(AssetType.Texture) { }

    public static Texture Create(
        int width,
        int height,
        ReadOnlySpan<byte> data,
        TextureFormat format = TextureFormat.RGBA8,
        TextureFilter filter = TextureFilter.Linear,
        string name = "")
    {
        var nativeData = new NativeArray<byte>(data.Length, data.Length);
        data.CopyTo(nativeData.AsSpan());

        var texture = new Texture(name, false)
        {
            Width = width,
            Height = height,
            Format = format,
            Filter = filter,
            Clamp = TextureClamp.Clamp,
            Data = nativeData
        };
        texture.Upload();
        texture.Register();
        return texture;
    }

    public static Texture Create(
        int width,
        int height,
        TextureFormat format = TextureFormat.RGBA8,
        TextureFilter filter = TextureFilter.Linear,
        string name = "")
    {
        var bpp = GetBytesPerPixel(format);
        var size = width * height * bpp;
        var nativeData = new NativeArray<byte>(size, size);

        var texture = new Texture(name, false)
        {
            Width = width,
            Height = height,
            Format = format,
            Filter = filter,
            Clamp = TextureClamp.Clamp,
            Data = nativeData
        };
        texture.Upload();
        texture.Register();
        return texture;
    }

    protected override void Load(BinaryReader reader)
    {
        Format = (TextureFormat)reader.ReadByte();
        Filter = (TextureFilter)reader.ReadByte();
        Clamp = (TextureClamp)reader.ReadByte();
        Width = (int)reader.ReadUInt32();
        Height = (int)reader.ReadUInt32();

        var dataSize = Width * Height * GetBytesPerPixel(Format);
        var bytes = reader.ReadBytes(dataSize);
        Data = new NativeArray<byte>(dataSize, dataSize);
        bytes.AsSpan().CopyTo(Data.AsSpan());
        Upload();
    }

    private static Texture? Load(Stream stream, string name)
    {
        var texture = new Texture(name, false);
        using var reader = new BinaryReader(stream);
        texture.Load(reader);
        return texture;
    }

    public void Upload()
    {
        if (Native != nuint.Zero)
            Graphics.Driver.DestroyTexture(Native);
        Native = Graphics.Driver.CreateTexture(Width, Height, Data, Format, Filter, name: Name);
    }

    public static Texture? CreateArray(string name, int width, int height, byte[][] layerData,
        TextureFormat format = TextureFormat.RGBA8, TextureFilter filter = TextureFilter.Linear)
    {
        if (layerData.Length == 0)
            return null;

        var handle = Graphics.Driver.CreateTextureArray(width, height, layerData, format, filter, name);

        var texture = new Texture(name, true)
        {
            Width = width,
            Height = height,
            Format = format,
            Filter = filter,
            Clamp = TextureClamp.Clamp,
            Native = handle
        };

        texture.Register();
        return texture;
    }

    public void Update(ReadOnlySpan<byte> data)
    {
        if (Native == nuint.Zero)
            return;
        Graphics.Driver.UpdateTexture(Native, new Vector2Int(Width, Height), data);
    }

    public void Update(ReadOnlySpan<byte> data, in RectInt region, int srcWidth = -1)
    {
        if (Native == nuint.Zero)
            return;
        Graphics.Driver.UpdateTextureRegion(Native, region, data, srcWidth);
    }

    public void Update(in RectInt region)
    {
        if (Native == nuint.Zero)
            return;
        Graphics.Driver.UpdateTextureRegion(Native, region, Data, Width);
    }

    public void UpdateLayer(int layer, ReadOnlySpan<byte> data)
    {
        if (Native == nuint.Zero)
            return;
        Graphics.Driver.UpdateTextureLayer(Native, layer, data);
    }

    internal static void RegisterDef()
    {
        RegisterDef(new AssetDef(AssetType.Texture, "Texture", typeof(Texture), Load, Version));
    }

    public unsafe Color32 GetPixel(int x, int y)
    {
        if (!Data.IsCreated || (uint)x >= (uint)Width || (uint)y >= (uint)Height)
            return default;

        var bpp = GetBytesPerPixel(Format);
        var ptr = Data.Ptr + (y * Width + x) * bpp;
        return bpp switch
        {
            4 => Unsafe.ReadUnaligned<Color32>(ptr),
            3 => new Color32(ptr[0], ptr[1], ptr[2], 255),
            2 => new Color32(ptr[0], ptr[1], 0, 255),
            1 => new Color32(ptr[0], ptr[0], ptr[0], 255),
            _ => default
        };
    }

    public override void Dispose()
    {
        GC.SuppressFinalize(this);
        Unregister();

        Data.Dispose();

        if (Native != nuint.Zero)
        {
            Graphics.Driver.DestroyTexture(Native);
            Native = nuint.Zero;
        }

        base.Dispose();
    }

    private static int GetBytesPerPixel(TextureFormat format) => format switch
    {
        TextureFormat.RGBA32F => 16,
        TextureFormat.RGBA16F => 8,
        TextureFormat.RGBA8 => 4,
        TextureFormat.RGB8 => 3,
        TextureFormat.RG8 => 2,
        TextureFormat.R8 => 1,
        _ => 4
    };
}
