//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Text;

namespace NoZ;

public class Atlas : Asset
{
    internal const ushort Version = 2;

    public struct Frame
    {
        public Rect UV;
        public Vector2Int TrimSize;
    }

    public struct Entry
    {
        public ushort Layer;
        public ushort FrameStart;
        public ushort FrameCount;
    }

    public int Width { get; private set; }
    public int Height { get; private set; }
    public int LayerCount { get; private set; }
    public TextureFormat Format { get; private set; }
    public TextureFilter Filter { get; private set; }
    public TextureClamp Clamp { get; private set; }

    private Frame[] _frames = [];
    private readonly Dictionary<StringId, Entry> _entries = new();

    private Atlas(string name) : base(AssetType.Atlas, name) { }
    public Atlas() : base(AssetType.Atlas) { }

    public bool TryGetEntry(StringId id, out Entry entry) => _entries.TryGetValue(id, out entry);

    public ReadOnlySpan<Frame> GetFrames(in Entry entry) =>
        _frames.AsSpan(entry.FrameStart, entry.FrameCount);

    protected override void Load(BinaryReader reader)
    {
        Format = (TextureFormat)reader.ReadByte();
        Filter = (TextureFilter)reader.ReadByte();
        Clamp = (TextureClamp)reader.ReadByte();
        Width = (int)reader.ReadUInt32();
        Height = (int)reader.ReadUInt32();
        LayerCount = reader.ReadUInt16();

        var bytesPerPixel = GetBytesPerPixel(Format);
        var layerSize = Width * Height * bytesPerPixel;
        var layerData = new byte[LayerCount][];
        for (int i = 0; i < LayerCount; i++)
            layerData[i] = reader.ReadBytes(layerSize);

        if (Native != nuint.Zero)
        {
            Graphics.Driver.DestroyTexture(Native);
            Native = nuint.Zero;
        }
        if (LayerCount > 0 && Graphics.Driver != null)
            Native = Graphics.Driver.CreateTextureArray(Width, Height, layerData, Format, Filter, Name);

        _entries.Clear();
        var entryCount = reader.ReadUInt16();
        var totalFrames = 0;
        for (int i = 0; i < entryCount; i++)
        {
            var nameLength = reader.ReadUInt16();
            var nameBytes = reader.ReadBytes(nameLength);
            var name = Encoding.UTF8.GetString(nameBytes);

            var layer = reader.ReadUInt16();
            var frameCount = reader.ReadUInt16();

            _entries[StringId.Get(name)] = new Entry
            {
                Layer = layer,
                FrameStart = (ushort)totalFrames,
                FrameCount = frameCount,
            };
            totalFrames += frameCount;
        }

        _frames = new Frame[totalFrames];
        for (int i = 0; i < totalFrames; i++)
        {
            var ul = reader.ReadSingle();
            var ut = reader.ReadSingle();
            var ur = reader.ReadSingle();
            var ub = reader.ReadSingle();
            var sx = reader.ReadInt16();
            var sy = reader.ReadInt16();
            _frames[i] = new Frame
            {
                UV = Rect.FromMinMax(ul, ut, ur, ub),
                TrimSize = new Vector2Int(sx, sy),
            };
        }

        NotifyDependentSprites();
    }

    private void NotifyDependentSprites()
    {
        foreach (var asset in GetAllOfType(AssetType.Sprite))
            if (asset is Sprite sprite && ReferenceEquals(sprite.Atlas, this))
                sprite.ResolveFrames();
    }

    private static Atlas? Load(Stream stream, string name)
    {
        var atlas = new Atlas(name);
        using var reader = new BinaryReader(stream);
        atlas.Load(reader);
        return atlas;
    }

    internal static void RegisterDef()
    {
        RegisterDef(new AssetDef(AssetType.Atlas, "Atlas", typeof(Atlas), Load, Version));
    }

    public static Atlas CreatePreview(string name, int width, int height, byte[] layerData)
    {
        var atlas = new Atlas(name)
        {
            Width = width,
            Height = height,
            LayerCount = 1,
            Format = TextureFormat.RGBA8,
            Filter = TextureFilter.Point,
            Clamp = TextureClamp.Clamp,
        };
        if (Graphics.Driver != null)
            atlas.Native = Graphics.Driver.CreateTextureArray(width, height, [layerData], atlas.Format, atlas.Filter, name);
        return atlas;
    }

    public void UpdateLayer(int layer, ReadOnlySpan<byte> data)
    {
        if (Native == nuint.Zero) return;
        Graphics.Driver.UpdateTextureLayer(Native, layer, data);
    }

    public override void Dispose()
    {
        if (Native != nuint.Zero)
        {
            Graphics.Driver.DestroyTexture(Native);
            Native = nuint.Zero;
        }
        _entries.Clear();
        _frames = [];
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
