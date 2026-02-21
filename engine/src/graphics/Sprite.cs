//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ;

public readonly struct SpriteMesh(
    Rect uv,
    short order,
    short boneIndex = -1,
    Vector2Int offset = default,
    Vector2Int size = default,
    Color fillColor = default)
{
    public readonly Rect UV = uv;
    public readonly short SortOrder = order;
    public readonly short BoneIndex = boneIndex;  // -1 = unbound, 0+ = bone index
    public readonly Vector2Int Offset = offset;   // Offset from sprite origin in pixels
    public readonly Vector2Int Size = size;       // Tight bounds size in pixels
    public readonly Color FillColor = fillColor;  // Fill color for SDF sprites (vertex color source)
}

public readonly struct SpriteFrameInfo(ushort meshStart, ushort meshCount)
{
    public readonly ushort MeshStart = meshStart;
    public readonly ushort MeshCount = meshCount;
}

public class Sprite : Asset
{
    public const ushort Version = 8;
    public const int MaxFrames = 64;

    public RectInt Bounds { get; private set; }
    public Vector2Int Size => Bounds.Size;
    public int FrameCount { get; private set; }
    public int AtlasIndex { get; private set; }
    public int BoneIndex { get; private set; }
    public float PixelsPerUnit { get; private set; } = 64.0f;
    public float PixelsPerUnitInv { get; private set; }
    public float FrameRate { get; private set; } = 12.0f;
    public bool IsSDF { get; private set; }
    public TextureFilter TextureFilter { get; set; } = TextureFilter.Point;
    public SpriteMesh[] Meshes { get; private set; } = [];
    public SpriteFrameInfo[] FrameTable { get; private set; } = [];
    public Rect UV => Meshes.Length > 0 ? Meshes[0].UV : Rect.Zero;
    public ushort Order => Meshes.Length > 0 ? (ushort)Meshes[0].SortOrder : (ushort)0;

    private Sprite(string name) : base(AssetType.Sprite, name) { }

    internal static Sprite Create(
        string name,
        RectInt bounds,
        float pixelsPerUnit,
        TextureFilter filter,
        int boneIndex,
        SpriteMesh[] meshes,
        SpriteFrameInfo[] frameTable,
        float frameRate = 12.0f,
        bool isSDF = false)
    {
        return new Sprite(name)
        {
            Bounds = bounds,
            FrameCount = frameTable.Length,
            AtlasIndex = 0,
            PixelsPerUnit = pixelsPerUnit,
            PixelsPerUnitInv = 1.0f / pixelsPerUnit,
            FrameRate = frameRate,
            IsSDF = isSDF,
            TextureFilter = filter,
            BoneIndex = boneIndex,
            Meshes = meshes,
            FrameTable = frameTable
        };
    }

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
        var meshCount = reader.ReadUInt16();
        var frameRate = reader.ReadSingle();
        var isSDF = reader.ReadByte() != 0;

        var meshes = new SpriteMesh[meshCount];
        for (int i = 0; i < meshCount; i++)
        {
            var ul = reader.ReadSingle();
            var ut = reader.ReadSingle();
            var ur = reader.ReadSingle();
            var ub = reader.ReadSingle();
            var sortOrder = reader.ReadInt16();
            var meshBoneIndex = reader.ReadInt16();
            var offsetX = reader.ReadInt16();
            var offsetY = reader.ReadInt16();
            var sizeX = reader.ReadInt16();
            var sizeY = reader.ReadInt16();

            var fillColor = Color.White;
            if (isSDF)
            {
                var fr = reader.ReadByte();
                var fg = reader.ReadByte();
                var fb = reader.ReadByte();
                var fa = reader.ReadByte();
                fillColor = new Color(fr / 255f, fg / 255f, fb / 255f, fa / 255f);
            }

            meshes[i] = new SpriteMesh(
                Rect.FromMinMax(ul, ut, ur, ub),
                sortOrder,
                meshBoneIndex,
                new Vector2Int(offsetX, offsetY),
                new Vector2Int(sizeX, sizeY),
                fillColor);
        }

        var frameTable = new SpriteFrameInfo[frameCount];
        for (int i = 0; i < frameCount; i++)
        {
            var meshStart = reader.ReadUInt16();
            var count = reader.ReadUInt16();
            frameTable[i] = new SpriteFrameInfo(meshStart, count);
        }

        sprite.Bounds = RectInt.FromMinMax(l, t, r, b);
        sprite.FrameCount = frameCount;
        sprite.AtlasIndex = atlasIndex;
        sprite.BoneIndex = boneIndex;
        sprite.PixelsPerUnit = ppu;
        sprite.PixelsPerUnitInv = 1.0f / ppu;
        sprite.FrameRate = frameRate;
        sprite.IsSDF = isSDF;
        sprite.TextureFilter = filter;
        sprite.Meshes = meshes;
        sprite.FrameTable = frameTable;

        return sprite;
    }

    internal static void RegisterDef()
    {
        RegisterDef(new AssetDef(AssetType.Sprite, typeof(Sprite), Load, Version));
    }
}
