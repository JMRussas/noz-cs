//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ.Editor;

public abstract partial class SpriteDocument
{
    internal void Rasterize(PixelData<Color32> image, in AtlasSpriteRect rect, int padding)
    {
        if (rect.ExportGroup is { Visible: false })
            return;
        RasterizeCore(image, rect, padding);
    }

    public override void Export(string outputPath, PropertySet meta)
    {
        Skeleton.Resolve();
        ResolveBone();
        UpdateBounds();

        var parts = GetExportParts();
        if (parts.Count == 0)
        {
            if (Directory.Exists(outputPath))
                Directory.Delete(outputPath, recursive: true);

            WriteSprite(outputPath, BoneIndex, SortOrder);
            return;
        }

        if (File.Exists(outputPath))
            File.Delete(outputPath);
        if (Directory.Exists(outputPath))
            Directory.Delete(outputPath, recursive: true);
        Directory.CreateDirectory(outputPath);

        foreach (var part in parts)
        {
            var boneIndex = part.Group != null ? ResolveGroupBone(part.Group) : BoneIndex;
            var sortOrder = part.Group != null ? ResolveGroupSortOrder(part.Group) : SortOrder;
            WriteSprite(System.IO.Path.Combine(outputPath, part.FileName), boneIndex, sortOrder);
        }
    }

    private int ResolveGroupBone(SpriteGroup group)
    {
        if (group.BoneName != null && Skeleton.Value is { } skeleton)
            return skeleton.FindBoneIndex(group.BoneName);
        return BoneIndex;
    }

    private byte ResolveGroupSortOrder(SpriteGroup group)
    {
        if (group.SortOrderId != null &&
            EditorApplication.Config.TryGetSortOrder(group.SortOrderId, out var sortOrder))
            return sortOrder.SortOrder;
        return SortOrder;
    }

    private void WriteSprite(string outputPath, int boneIndex, byte sortOrder)
    {
        var frameCount = (ushort)TotalTimeSlots;

        using var writer = new BinaryWriter(File.Create(outputPath));
        writer.WriteAssetHeader(AssetType.Sprite, Sprite.Version, 0);

        writer.Write((ushort)PixelsPerUnit);
        writer.Write((short)RasterBounds.Left);
        writer.Write((short)RasterBounds.Top);
        writer.Write((short)RasterBounds.Right);
        writer.Write((short)RasterBounds.Bottom);
        writer.Write((ushort)sortOrder);
        writer.Write((byte)(boneIndex == -1 ? 255 : boneIndex));

        var ppu = PixelsPerUnit;
        var et = (short)MathF.Round(Edges.T * ppu);
        var el = (short)MathF.Round(Edges.L * ppu);
        var eb = (short)MathF.Round(Edges.B * ppu);
        var er = (short)MathF.Round(Edges.R * ppu);
        writer.Write(et);
        writer.Write(el);
        writer.Write(eb);
        writer.Write(er);
        writer.Write(Sprite.CalculateSliceMask(RasterBounds, new EdgeInsets(et, el, eb, er)));

        writer.Write(frameCount);
        writer.Write((byte)DefaultFrameRate);
        for (ushort frameIndex = 0; frameIndex < frameCount; frameIndex++)
        {
            // Per-frame offset is sprite metadata; UV/size live in the atlas.
            // RasterBounds is currently the same for all frames, so use its X/Y as offset.
            writer.Write((short)RasterBounds.X);
            writer.Write((short)RasterBounds.Y);
        }

        writer.Write((byte)TextureFilter);
    }
}
