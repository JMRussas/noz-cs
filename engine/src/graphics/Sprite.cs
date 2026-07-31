//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ;

public readonly struct SpriteFrame(Rect uv, Vector2Int offset = default, Vector2Int size = default)
{
    public readonly Rect UV = uv;
    public readonly Vector2Int Offset = offset;
    public readonly Vector2Int Size = size;
}

public class Sprite : Asset, IImage
{
    float IImage.ImageWidth => Bounds.Width;
    float IImage.ImageHeight => Bounds.Height;

    public const ushort Version = 14;
    public const int MaxFrames = 64;

    public RectInt Bounds { get; private set; }
    public Vector2Int Size => Bounds.Size;
    public int FrameCount { get; private set; }
    public int AtlasIndex { get; private set; }
    public int BoneIndex { get; private set; }
    public float PixelsPerUnit { get; private set; } = 64.0f;
    public float PixelsPerUnitInv { get; private set; }
    public float FrameRate { get; private set; } = 12.0f;
    public SpriteFrame[] Frames { get; private set; } = [];
    public EdgeInsets Edges { get; private set; } = EdgeInsets.Zero;
    public ushort SliceMask { get; private set; }
    public ushort SortOrder { get; private set; }
    public TextureFilter Filter { get; private set; } = TextureFilter.Linear;
    public bool IsSliced => SliceMask != 0 || !Edges.IsZero;
    public Rect UV => Frames.Length > 0 ? Frames[0].UV : Rect.Zero;
    public Atlas? Atlas { get; set; }

    private Vector2Int[] _frameOffsets = [];

    private Sprite(string name) : base(AssetType.Sprite, name) { }
    public Sprite() : base(AssetType.Sprite) { }

    public void Load(string name, Atlas atlas)
    {
        Atlas = atlas;
        Load(name);
        ResolveFrames();
    }

    public static Sprite Create(
        string name,
        RectInt bounds,
        float pixelsPerUnit,
        int boneIndex,
        SpriteFrame[] frames,
        float frameRate = 12.0f,
        EdgeInsets edges = default,
        ushort sliceMask = 0,
        int atlasIndex = 0,
        Atlas? atlas = null,
        TextureFilter filter = TextureFilter.Linear)
    {
        return new Sprite(name)
        {
            Bounds = bounds,
            FrameCount = frames.Length,
            AtlasIndex = atlasIndex,
            Atlas = atlas,
            PixelsPerUnit = pixelsPerUnit,
            PixelsPerUnitInv = 1.0f / pixelsPerUnit,
            FrameRate = frameRate,
            BoneIndex = boneIndex,
            Frames = frames,
            Edges = edges,
            SliceMask = sliceMask,
            Filter = filter,
        };
    }

    protected override void Load(BinaryReader reader)
    {
        var ppu = (int)reader.ReadUInt16();
        var l = reader.ReadInt16();
        var t = reader.ReadInt16();
        var r = reader.ReadInt16();
        var b = reader.ReadInt16();
        var sortOrder = reader.ReadUInt16();
        var boneIndex = (int)reader.ReadByte();

        var et = reader.ReadInt16();
        var el = reader.ReadInt16();
        var eb = reader.ReadInt16();
        var er = reader.ReadInt16();
        var edges = new EdgeInsets(et, el, eb, er);
        var sliceMask = reader.ReadUInt16();

        var frameCount = reader.ReadUInt16();
        var frameRate = reader.ReadByte();
        var offsets = new Vector2Int[frameCount];
        for (int i = 0; i < frameCount; i++)
        {
            var ox = reader.ReadInt16();
            var oy = reader.ReadInt16();
            offsets[i] = new Vector2Int(ox, oy);
        }

        var filter = (TextureFilter)reader.ReadByte();

        Bounds = RectInt.FromMinMax(l, t, r, b);
        FrameCount = frameCount;
        BoneIndex = boneIndex == 255 ? -1 : boneIndex;
        PixelsPerUnit = ppu;
        PixelsPerUnitInv = 1.0f / ppu;
        FrameRate = frameRate;
        _frameOffsets = offsets;
        Edges = edges;
        SliceMask = sliceMask;
        SortOrder = sortOrder;
        Filter = filter;

        ResolveFrames();
    }

    internal void ResolveFrames()
    {
        if (_frameOffsets.Length == 0) return;

        if (Atlas == null)
        {
            Frames = [];
            AtlasIndex = 0;
            return;
        }

        if (!Atlas.TryGetEntry(Id, out var entry))
        {
            Log.Error($"Sprite '{Name}' not found in atlas '{Atlas.Name}'");
            Frames = [];
            AtlasIndex = 0;
            return;
        }

        AtlasIndex = entry.Layer;
        var atlasFrames = Atlas.GetFrames(entry);
        var resolved = new SpriteFrame[_frameOffsets.Length];
        for (int i = 0; i < _frameOffsets.Length; i++)
        {
            var atlasFrame = i < atlasFrames.Length ? atlasFrames[i] : default;
            resolved[i] = new SpriteFrame(atlasFrame.UV, _frameOffsets[i], atlasFrame.TrimSize);
        }
        Frames = resolved;
    }

    public override void Reload()
    {
        if (string.IsNullOrEmpty(Name)) return;
        Dispose();
        Load(Name);
    }

    private static Asset Load(Stream stream, string name)
    {
        var sprite = new Sprite(name);
        var reader = new BinaryReader(stream);
        sprite.Load(reader);
        return sprite;
    }

    public static ushort CalculateSliceMask(RectInt bounds, EdgeInsets edges)
    {
        if (edges.IsZero)
            return 0;

        var centerW = bounds.Width - edges.L - edges.R;
        var centerH = bounds.Height - edges.T - edges.B;

        bool[] colActive = [edges.L > 0, centerW > 0, edges.R > 0];
        bool[] rowActive = [edges.T > 0, centerH > 0, edges.B > 0];

        ushort mask = 0;
        for (int row = 0; row < 3; row++)
            for (int col = 0; col < 3; col++)
                if (rowActive[row] && colActive[col])
                    mask |= (ushort)(1 << (row * 3 + col));

        return mask;
    }

    internal static void RegisterDef()
    {
        RegisterDef(new AssetDef(AssetType.Sprite, "Sprite", typeof(Sprite), Load, Version));
    }
}
