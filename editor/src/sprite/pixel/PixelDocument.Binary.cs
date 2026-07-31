//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Text;

namespace NoZ.Editor;

public partial class PixelDocument
{
    public const string BinaryExtension = ".pixel";

    private const uint BinaryMagic = 0x5850_5A4E; // "NZPX" little-endian
    private const byte BinaryVersion = 2;
    private const byte NodeKindLayer = 0;
    private const byte NodeKindGroup = 1;

    protected override void Save(Stream stream)
    {
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            WriteBinaryHeader(writer);
            WriteBinaryTree(writer);
        }
    }

    public override void Load()
    {
        using var stream = File.OpenRead(Path);
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: false);

        var version = ReadBinaryHeader(reader);
        ReadBinaryTree(reader, version);

        UpdateBounds();
        Loaded = true;
    }

    public override void Reload()
    {
        Edges = EdgeInsets.Zero;
        Skeleton.Clear();
        BoneName = null;
        IsAnimated = false;
        Root.Clear();

        using (var stream = File.OpenRead(Path))
        using (var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: false))
        {
            var version = ReadBinaryHeader(reader);
            ReadBinaryTree(reader, version);
        }

        Skeleton.Resolve();
        ResolveSortOrder();
        ResolveBone();
        UpdateBounds();
    }

    internal static void WriteEmptyPixelSprite(BinaryWriter writer, int canvasW, int canvasH)
    {
        writer.Write(BinaryMagic);
        writer.Write(BinaryVersion);
        writer.Write((byte)0);                  // flags
        writer.Write((ushort)canvasW);
        writer.Write((ushort)canvasH);
        writer.Write(0f); writer.Write(0f); writer.Write(0f); writer.Write(0f); // edges TLBR
        writer.Write("");                       // skeleton
        writer.Write("");                       // bone
        writer.Write("");                       // sort
        writer.Write((byte)0);                  // sprite flags (not animated)

        writer.Write(1);                        // nodeCount
        writer.Write(NodeKindLayer);
        writer.Write(-1);                       // parentIdx
        writer.Write("Layer 1");                // name
        writer.Write(0);                        // hold
        writer.Write((byte)0);                  // hasPixels = false
    }

    private void WriteBinaryHeader(BinaryWriter writer)
    {
        writer.Write(BinaryMagic);
        writer.Write(BinaryVersion);
        writer.Write((byte)0); // flags
        writer.Write((ushort)CanvasSize.X);
        writer.Write((ushort)CanvasSize.Y);
        writer.Write(Edges.T);
        writer.Write(Edges.L);
        writer.Write(Edges.B);
        writer.Write(Edges.R);
        writer.Write(Skeleton.Name ?? "");
        writer.Write(BoneName ?? "");
        writer.Write(SortOrderId ?? "");
        writer.Write((byte)(IsAnimated ? 1 : 0));
    }

    private byte ReadBinaryHeader(BinaryReader reader)
    {
        var magic = reader.ReadUInt32();
        if (magic != BinaryMagic)
            throw new InvalidDataException($"Invalid .pixel magic in {Path}: 0x{magic:X8}");

        var version = reader.ReadByte();
        if (version is < 1 or > BinaryVersion)
            throw new InvalidDataException($"Unsupported .pixel version in {Path}: {version}");

        _ = reader.ReadByte(); // flags, reserved

        var canvasW = reader.ReadUInt16();
        var canvasH = reader.ReadUInt16();
        CanvasSize = new Vector2Int(canvasW, canvasH);

        var et = reader.ReadSingle();
        var el = reader.ReadSingle();
        var eb = reader.ReadSingle();
        var er = reader.ReadSingle();
        Edges = new EdgeInsets(et, el, eb, er);

        var skel = reader.ReadString();
        Skeleton.Name = string.IsNullOrEmpty(skel) ? null : skel;

        var bone = reader.ReadString();
        BoneName = string.IsNullOrEmpty(bone) ? null : bone;

        var sort = reader.ReadString();
        SortOrderId = string.IsNullOrEmpty(sort) ? null : sort;

        var spriteFlags = reader.ReadByte();
        IsAnimated = (spriteFlags & 0x01) != 0;
        return version;
    }

    private void WriteBinaryTree(BinaryWriter writer)
    {
        var nodes = new List<(SpriteNode node, int parentIdx)>();
        CollectNodes(Root, -1, nodes);

        writer.Write(nodes.Count);
        foreach (var (node, parentIdx) in nodes)
        {
            if (node is PixelLayer layer)
            {
                writer.Write(NodeKindLayer);
                writer.Write(parentIdx);
                writer.Write(layer.Name);
                writer.Write(layer.Hold);
                if (layer.Pixels != null)
                {
                    writer.Write((byte)1);
                    writer.Write(layer.Pixels.AsReadonlySpan());
                }
                else
                {
                    writer.Write((byte)0);
                }
            }
            else if (node is SpriteGroup group)
            {
                writer.Write(NodeKindGroup);
                writer.Write(parentIdx);
                writer.Write(group.Name);
                writer.Write(group.Hold);
                writer.Write(group.BoneName ?? "");
                writer.Write(group.SortOrderId ?? "");
            }
        }
    }

    private static void CollectNodes(SpriteNode parent, int parentIdx, List<(SpriteNode, int)> nodes)
    {
        foreach (var child in parent.Children)
        {
            var idx = nodes.Count;
            nodes.Add((child, parentIdx));
            if (child is SpriteGroup)
                CollectNodes(child, idx, nodes);
        }
    }

    private void ReadBinaryTree(BinaryReader reader, byte version)
    {
        Root.Clear();

        var nodeCount = reader.ReadInt32();
        var nodes = new SpriteNode[nodeCount];

        for (var i = 0; i < nodeCount; i++)
        {
            var kind = reader.ReadByte();
            var parentIdx = reader.ReadInt32();
            var name = reader.ReadString();
            var hold = reader.ReadInt32();

            SpriteNode node;
            if (kind == NodeKindLayer)
            {
                var hasPixels = reader.ReadByte();
                var pixels = new PixelData<Color32>(CanvasSize.X, CanvasSize.Y);
                if (hasPixels != 0)
                    reader.BaseStream.ReadExactly(pixels.AsSpan());
                node = new PixelLayer { Name = name, Hold = hold, Pixels = pixels };
            }
            else if (kind == NodeKindGroup)
            {
                var boneName = version >= 2 ? reader.ReadString() : "";
                var sortOrderId = version >= 2 ? reader.ReadString() : "";
                node = new SpriteGroup
                {
                    Name = name,
                    Hold = hold,
                    BoneName = string.IsNullOrEmpty(boneName) ? null : boneName,
                    SortOrderId = string.IsNullOrEmpty(sortOrderId) ? null : sortOrderId,
                };
            }
            else
            {
                throw new InvalidDataException($"Unknown node kind {kind} in {Path}");
            }

            nodes[i] = node;
            var parent = parentIdx < 0 ? Root : nodes[parentIdx];
            parent.Add(node);
        }
    }
}
