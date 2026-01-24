//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ;

public class Sprite : Asset
{
    public const ushort Version = 1;
    public const int MaxFrames = 64;

    public RectInt Bounds { get; private set; }
    public Rect UV { get; private set;  }
    public Vector2Int Size => Bounds.Size;
    public int FrameCount { get; private set; }
    public int AtlasIndex { get; private set; }
    public float PixelsPerUnit { get; private set; } = 64.0f;
    public float PixelsPerUnitInv { get; private set; }
    public TextureFilter TextureFilter { get; set; } = TextureFilter.Point;

    private Sprite(string name) : base(AssetType.Sprite, name) { }

    private static Asset Load(Stream stream, string name)
    {
        var sprite = new Sprite(name);
        var reader = new BinaryReader(stream);

        var frameCount = reader.ReadUInt16();
        var atlasIndex = reader.ReadUInt16();
        var l = reader.ReadInt16();
        var t = reader.ReadInt16();
        var r = reader.ReadInt16();
        var b = reader.ReadInt16();
        var ul = reader.ReadSingle() + 0.001f;
        var ut = reader.ReadSingle() + 0.001f;
        var ur = reader.ReadSingle() - 0.001f;
        var ub = reader.ReadSingle() - 0.001f;
        var ppu = reader.ReadSingle();
        var filter = (TextureFilter)reader.ReadByte();

        sprite.Bounds = RectInt.FromMinMax(l, t, r, b);
        sprite.UV = Rect.FromMinMax(ul, ut, ur, ub);
        sprite.FrameCount = frameCount;
        sprite.AtlasIndex = atlasIndex;
        sprite.PixelsPerUnit = ppu;
        sprite.PixelsPerUnitInv = 1.0f / ppu;
        sprite.TextureFilter = filter;

        return sprite;
    }

    internal static void RegisterDef()
    {
        RegisterDef(new AssetDef(AssetType.Sprite, typeof(Sprite), Load));
    }
}