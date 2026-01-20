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
    public Vector2Int Size { get; private set; }
    public int FrameCount { get; private set; }

    private Sprite(string name) : base(AssetType.Sprite, name) { }

    private static Asset Load(Stream stream, string name) 
    {
        var sprite = new Sprite(name);
        var reader = new BinaryReader(stream);

        var frameCount = reader.ReadUInt16();
        var l = reader.ReadUInt16();
        var t = reader.ReadUInt16();
        var r = reader.ReadUInt16();
        var b = reader.ReadUInt16();
        var ul = reader.ReadSingle();
        var ut = reader.ReadSingle();
        var ur = reader.ReadSingle();
        var ub = reader.ReadSingle();

        sprite.Bounds = RectInt.FromMinMax(l, t, r, b);
        sprite.UV = Rect.FromMinMax(ul, ut, ur, ub);
        sprite.FrameCount = frameCount;

        return sprite;
    }

    internal static void RegisterDef()
    {
        RegisterDef(new AssetDef(AssetType.Sprite, typeof(Sprite), Load));
    }
}