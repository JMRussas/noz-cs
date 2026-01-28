//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ;

public class Sprite : Asset
{
    public const ushort Version = 3;
    public const int MaxFrames = 64;

    public RectInt Bounds { get; private set; }
    public Rect UV { get; private set;  }
    public Vector2Int Size => Bounds.Size;
    public int FrameCount { get; private set; }
    public int AtlasIndex { get; private set; }
    public float PixelsPerUnit { get; private set; } = 64.0f;
    public float PixelsPerUnitInv { get; private set; }
    public TextureFilter TextureFilter { get; set; } = TextureFilter.Point;
    public ushort Order { get; private set; }
    public Vector2 Offset { get; private set; }

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
        var ul = reader.ReadSingle();
        var ut = reader.ReadSingle();
        var ur = reader.ReadSingle();
        var ub = reader.ReadSingle();
        var ppu = reader.ReadSingle();
        var filter = (TextureFilter)reader.ReadByte();

        // Version 2+: read Order (backwards compatible - check if more data available)
        ushort order = 0;
        if (stream.Position + 2 <= stream.Length)
            order = reader.ReadUInt16();

        // Version 3+: read Offset
        var offset = Vector2.Zero;
        if (stream.Position + 8 <= stream.Length)
        {
            offset.X = reader.ReadSingle();
            offset.Y = reader.ReadSingle();
        }

        sprite.Bounds = RectInt.FromMinMax(l, t, r, b);
        sprite.UV = Rect.FromMinMax(ul, ut, ur, ub);
        sprite.FrameCount = frameCount;
        sprite.AtlasIndex = atlasIndex;
        sprite.PixelsPerUnit = ppu;
        sprite.PixelsPerUnitInv = 1.0f / ppu;
        sprite.TextureFilter = filter;
        sprite.Order = order;
        sprite.Offset = offset;

        // todo: fix
        sprite.TextureFilter = TextureFilter.Point;

        return sprite;
    }

    internal static void RegisterDef()
    {
        RegisterDef(new AssetDef(AssetType.Sprite, typeof(Sprite), Load));
    }
}