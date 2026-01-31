//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ;

public readonly struct SpriteMesh(Rect uv, short order)
{
    public readonly Rect UV = uv;
    public readonly short SortOrder = order;
}

public class Sprite : Asset
{
    public const ushort Version = 4;
    public const int MaxFrames = 64;

    public RectInt Bounds { get; private set; }
    public Vector2Int Size => Bounds.Size;
    public int FrameCount { get; private set; }
    public int AtlasIndex { get; private set; }
    public int BoneIndex { get; private set; }
    public float PixelsPerUnit { get; private set; } = 64.0f;
    public float PixelsPerUnitInv { get; private set; }
    public TextureFilter TextureFilter { get; set; } = TextureFilter.Point;
    public Vector2 BoneOffset { get; private set; }
    public SpriteMesh[] Meshes { get; private set; } = [];
    public Rect UV => Meshes.Length > 0 ? Meshes[0].UV : Rect.Zero;
    public ushort Order => Meshes.Length > 0 ? (ushort)Meshes[0].SortOrder : (ushort)0;

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
        var ppu = reader.ReadSingle();
        var filter = (TextureFilter)reader.ReadByte();
        var boneIndex = reader.ReadInt16();
        var boneOffset = new Vector2(
            reader.ReadSingle(),
            reader.ReadSingle());
        var meshCount = reader.ReadByte();

        var meshes = new SpriteMesh[meshCount];
        for (int i = 0; i < meshCount; i++)
        {
            var ul = reader.ReadSingle();
            var ut = reader.ReadSingle();
            var ur = reader.ReadSingle();
            var ub = reader.ReadSingle();
            var sortOrder = reader.ReadInt16();
            meshes[i] = new SpriteMesh(Rect.FromMinMax(ul, ut, ur, ub), sortOrder);
        }

        sprite.Bounds = RectInt.FromMinMax(l, t, r, b);
        sprite.FrameCount = frameCount;
        sprite.AtlasIndex = atlasIndex;
        sprite.BoneIndex = boneIndex;
        sprite.PixelsPerUnit = ppu;
        sprite.PixelsPerUnitInv = 1.0f / ppu;
        sprite.TextureFilter = filter;
        sprite.BoneOffset = boneOffset;
        sprite.Meshes = meshes;
        return sprite;
    }

    internal static void RegisterDef()
    {
        RegisterDef(new AssetDef(AssetType.Sprite, typeof(Sprite), Load));
    }
}