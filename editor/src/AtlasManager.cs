//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace NoZ.Editor;

internal struct AtlasSpriteRect
{
    public SpriteDocument? Source; // null = freed slot, available for reclaim
    public SpriteGroup? ExportGroup; // null = composite document sprite
    public bool IsBasePart;
    public RectInt Rect;
    public ushort FrameIndex;
    public ushort Layer;
}

public static class AtlasManager
{
    public const string GameAtlasName = "sprites";
    public const string EditorAtlasName = "editor_sprites";

    private class Group
    {
        public string Name = "";
        public readonly List<SpriteDocument> Sources = new(64);
        public readonly List<RectPacker> Packers = new(2);
        public readonly List<PixelData<Color32>> Layers = new(2);
        public readonly List<AtlasSpriteRect> Rects = new(128);
        public readonly Atlas Atlas = new();
        public byte[]? LastSerialized;
        public bool NeedsFullRepack = true;
        public readonly HashSet<SpriteDocument> SpritesToRasterize = new();
        public bool UploadPending;
        public bool ExportPending;

        public void DisposeLayers()
        {
            foreach (var layer in Layers) layer.Dispose();
            Layers.Clear();
            Packers.Clear();
            Rects.Clear();
        }
    }

    private static readonly Group _game = new() { Name = GameAtlasName };
    private static readonly Group _editor = new() { Name = EditorAtlasName };

    public static Atlas GameAtlas => _game.Atlas;
    public static Atlas EditorAtlas => _editor.Atlas;

    public static void Init()
    {
        Project.DocumentAdded += OnDocumentAdded;
        Project.DocumentRemoved += OnDocumentRemoved;
        Project.DocumentRenamed += OnDocumentRenamed;
        Project.OnExported += OnExported;

        foreach (var doc in Project.Documents)
        {
            if (doc is not SpriteDocument sprite) continue;
            if (_game.Sources.Contains(sprite) || _editor.Sources.Contains(sprite)) continue;
            GroupOf(sprite).Sources.Add(sprite);
        }

        // Force a fresh pack: a SpriteDocument.Sprite getter hit during LoadAll may have
        // already triggered a premature pack with empty source lists.
        _game.NeedsFullRepack = true;
        _editor.NeedsFullRepack = true;
        Update();
    }

    public static void Shutdown()
    {
        _game.Sources.Clear();
        _game.DisposeLayers();
        _editor.Sources.Clear();
        _editor.DisposeLayers();
    }

    private static Group GroupOf(SpriteDocument sprite) =>
        sprite.ShouldExport ? _game : _editor;

    private static void OnDocumentAdded(Document doc)
    {
        if (doc is not SpriteDocument sprite) return;
        var group = GroupOf(sprite);
        if (group.Sources.Contains(sprite)) return;

        group.Sources.Add(sprite);

        if (group.NeedsFullRepack || group.Layers.Count == 0)
        {
            group.NeedsFullRepack = true;
            Update();
            return;
        }

        sprite.UpdateBounds();
        if (!AllocateSpriteIncremental(group, sprite))
        {
            group.NeedsFullRepack = true;
            Update();
            return;
        }

        group.SpritesToRasterize.Add(sprite);
        group.UploadPending = true;
        Update();
    }

    private static void OnDocumentRemoved(Document doc)
    {
        if (doc is not SpriteDocument sprite) return;
        if (!_game.Sources.Remove(sprite) && !_editor.Sources.Remove(sprite)) return;

        if (FreeRectsFor(_game, sprite))
        {
            _game.UploadPending = true;
            _game.ExportPending = true;
        }
        if (FreeRectsFor(_editor, sprite))
            _editor.UploadPending = true;

        Update();
    }

    private static bool FreeRectsFor(Group group, SpriteDocument sprite)
    {
        var rects = CollectionsMarshal.AsSpan(group.Rects);
        var freed = false;
        for (int i = 0; i < rects.Length; i++)
        {
            if (rects[i].Source != sprite) continue;
            rects[i].Source = null;
            freed = true;
        }
        return freed;
    }

    private static void OnDocumentRenamed(Document doc, string oldName)
    {
        if (doc is not SpriteDocument sprite) return;
        var group = GroupOf(sprite);
        if (!HasRectsForSource(group, sprite)) return;
        group.UploadPending = true;
        group.ExportPending = true;
        Update();
    }

    public static void OnExportChanged(SpriteDocument sprite)
    {
        var newGroup = GroupOf(sprite);
        var oldGroup = newGroup == _game ? _editor : _game;

        if (newGroup.Sources.Contains(sprite))
            return;

        if (oldGroup.Sources.Remove(sprite))
        {
            if (FreeRectsFor(oldGroup, sprite))
            {
                oldGroup.UploadPending = true;
                if (oldGroup == _game) oldGroup.ExportPending = true;
            }
        }

        newGroup.Sources.Add(sprite);
        if (newGroup.NeedsFullRepack || newGroup.Layers.Count == 0)
        {
            newGroup.NeedsFullRepack = true;
        }
        else
        {
            sprite.UpdateBounds();
            if (!AllocateSpriteIncremental(newGroup, sprite))
                newGroup.NeedsFullRepack = true;
            else
                newGroup.SpritesToRasterize.Add(sprite);
        }
        newGroup.UploadPending = true;
        if (newGroup == _game) newGroup.ExportPending = true;

        sprite.MarkSpriteDirty();
        Update();
    }

    private static bool HasRectsForSource(Group group, SpriteDocument sprite)
    {
        foreach (var rect in group.Rects)
            if (rect.Source == sprite) return true;
        return false;
    }

    private static void OnExported(Document doc)
    {
        if (doc is SpriteDocument sprite)
            GroupOf(sprite).ExportPending = true;
    }

    public static void MarkDirty(SpriteDocument sprite) =>
        GroupOf(sprite).SpritesToRasterize.Add(sprite);

    public static void MarkAllDirty()
    {
        _game.NeedsFullRepack = true;
        _editor.NeedsFullRepack = true;
    }

    public static void UpdateSource(SpriteDocument sprite)
    {
        var group = GroupOf(sprite);

        if (group.NeedsFullRepack)
        {
            Update();
            return;
        }

        sprite.UpdateBounds();

        if (!RetargetSpriteRects(group, sprite))
        {
            Update();
            return;
        }

        group.SpritesToRasterize.Add(sprite);
        group.UploadPending = true;
        Update();
    }

    public static void Update()
    {
        if (_game.NeedsFullRepack) FullPack(_game);
        else IncrementalUpdate(_game);

        if (_editor.NeedsFullRepack) FullPack(_editor);
        else IncrementalUpdate(_editor);
    }

    internal static bool TryGetEntry(SpriteDocument sprite, out AtlasSpriteRect[] frames, out ushort layer)
        => TryGetEntry(sprite, new SpriteAtlasPart(null, false), out frames, out layer);

    internal static bool TryGetEntry(
        SpriteDocument sprite,
        SpriteAtlasPart atlasPart,
        out AtlasSpriteRect[] frames,
        out ushort layer)
    {
        var group = GroupOf(sprite);
        var collected = new List<AtlasSpriteRect>(sprite.AtlasFrameCount);
        ushort foundLayer = 0;
        foreach (var rect in group.Rects)
        {
            if (rect.Source != sprite ||
                rect.ExportGroup != atlasPart.Group ||
                rect.IsBasePart != atlasPart.IsBase)
                continue;
            collected.Add(rect);
            foundLayer = rect.Layer;
        }

        if (collected.Count == 0)
        {
            frames = [];
            layer = 0;
            return false;
        }

        collected.Sort((a, b) => a.FrameIndex.CompareTo(b.FrameIndex));
        frames = collected.ToArray();
        layer = foundLayer;
        return true;
    }

    private static bool AllocateSpriteIncremental(Group group, SpriteDocument sprite)
    {
        var atlasSize = EditorApplication.Config.AtlasSize;
        var frameCount = sprite.AtlasFrameCount;

        foreach (var atlasPart in sprite.GetAtlasParts())
        {
            for (ushort frameIndex = 0; frameIndex < frameCount; frameIndex++)
            {
                if (HasRectFor(group, sprite, atlasPart.Group, atlasPart.IsBase, frameIndex)) continue;

                var size = sprite.GetFrameAtlasSize(frameIndex);
                if (size == Vector2Int.Zero) continue;

                if (TryReclaimFreed(group, sprite, atlasPart.Group, atlasPart.IsBase, frameIndex, size)) continue;
                if (TryPackIntoExistingLayer(group, sprite, atlasPart.Group, atlasPart.IsBase, frameIndex, size)) continue;

                // Out of space in existing layers. Repack to consolidate freed slots if any
                // exist; otherwise grow into a new layer.
                if (HasFreedSpace(group))
                {
                    group.NeedsFullRepack = true;
                    return false;
                }

                if (!AddNewLayer(group, atlasSize, sprite, atlasPart.Group, atlasPart.IsBase, frameIndex, size))
                {
                    Log.Error($"Sprite '{sprite.GetAtlasAssetName(atlasPart.Group, atlasPart.IsBase)}' frame {frameIndex} too large to pack ({size.X}x{size.Y})");
                    return false;
                }
            }
        }
        return true;
    }

    private static bool RetargetSpriteRects(Group group, SpriteDocument sprite)
    {
        var frameCount = sprite.AtlasFrameCount;
        var atlasParts = sprite.GetAtlasParts().ToHashSet();
        var rects = CollectionsMarshal.AsSpan(group.Rects);
        for (int i = 0; i < rects.Length; i++)
        {
            ref var rect = ref rects[i];
            if (rect.Source != sprite) continue;
            if (!atlasParts.Contains(new SpriteAtlasPart(rect.ExportGroup, rect.IsBasePart)) ||
                rect.FrameIndex >= frameCount)
            {
                rect.Source = null;
                rect.ExportGroup = null;
                rect.IsBasePart = false;
                continue;
            }
            var size = sprite.GetFrameAtlasSize(rect.FrameIndex);
            if (size.X == 0 || size.Y == 0 || size.X > rect.Rect.Width || size.Y > rect.Rect.Height)
                rect.Source = null;
        }

        return AllocateSpriteIncremental(group, sprite);
    }

    private static bool HasRectFor(
        Group group,
        SpriteDocument sprite,
        SpriteGroup? exportGroup,
        bool isBasePart,
        ushort frameIndex)
    {
        foreach (var rect in group.Rects)
            if (rect.Source == sprite &&
                rect.ExportGroup == exportGroup &&
                rect.IsBasePart == isBasePart &&
                rect.FrameIndex == frameIndex)
                return true;
        return false;
    }

    private static bool HasFreedSpace(Group group)
    {
        foreach (var rect in group.Rects)
            if (rect.Source == null) return true;
        return false;
    }

    private static bool TryReclaimFreed(
        Group group,
        SpriteDocument sprite,
        SpriteGroup? exportGroup,
        bool isBasePart,
        ushort frameIndex,
        in Vector2Int size)
    {
        var rects = CollectionsMarshal.AsSpan(group.Rects);
        var bestIndex = -1;
        var bestArea = int.MaxValue;
        for (int i = 0; i < rects.Length; i++)
        {
            ref readonly var rect = ref rects[i];
            if (rect.Source != null) continue;
            if (rect.Rect.Width < size.X || rect.Rect.Height < size.Y) continue;
            var area = rect.Rect.Width * rect.Rect.Height;
            if (area < bestArea)
            {
                bestArea = area;
                bestIndex = i;
            }
        }
        if (bestIndex < 0) return false;

        ref var slot = ref rects[bestIndex];
        slot.Source = sprite;
        slot.ExportGroup = exportGroup;
        slot.IsBasePart = isBasePart;
        slot.FrameIndex = frameIndex;
        return true;
    }

    private static bool TryPackIntoExistingLayer(
        Group group,
        SpriteDocument sprite,
        SpriteGroup? exportGroup,
        bool isBasePart,
        ushort frameIndex,
        in Vector2Int size)
    {
        for (int i = 0; i < group.Packers.Count; i++)
        {
            if (group.Packers[i].Insert(size, out var rect) >= 0)
            {
                group.Rects.Add(new AtlasSpriteRect
                {
                    Source = sprite,
                    ExportGroup = exportGroup,
                    IsBasePart = isBasePart,
                    Rect = rect,
                    FrameIndex = frameIndex,
                    Layer = (ushort)i,
                });
                return true;
            }
        }
        return false;
    }

    private static bool AddNewLayer(
        Group group,
        int atlasSize,
        SpriteDocument sprite,
        SpriteGroup? exportGroup,
        bool isBasePart,
        ushort frameIndex,
        in Vector2Int size)
    {
        var packer = new RectPacker(atlasSize, atlasSize);
        var image = new PixelData<Color32>(atlasSize, atlasSize);
        group.Packers.Add(packer);
        group.Layers.Add(image);

        if (packer.Insert(size, out var rect) < 0)
            return false;

        group.Rects.Add(new AtlasSpriteRect
        {
            Source = sprite,
            ExportGroup = exportGroup,
            IsBasePart = isBasePart,
            Rect = rect,
            FrameIndex = frameIndex,
            Layer = (ushort)(group.Packers.Count - 1),
        });
        return true;
    }

    private static void IncrementalUpdate(Group group)
    {
        if (group.SpritesToRasterize.Count == 0 && !group.UploadPending) return;

        var padding = EditorApplication.Config.AtlasPadding;
        var rasterCount = group.SpritesToRasterize.Count;

        if (rasterCount > 0)
        {
            var rects = CollectionsMarshal.AsSpan(group.Rects);
            for (int i = 0; i < rects.Length; i++)
            {
                ref readonly var rect = ref rects[i];
                if (rect.Source == null || !group.SpritesToRasterize.Contains(rect.Source))
                    continue;
                group.Layers[rect.Layer].Clear(rect.Rect);
                rect.Source.Rasterize(group.Layers[rect.Layer], in rect, padding);
            }

            foreach (var sprite in group.SpritesToRasterize)
                sprite.MarkSpriteDirty();
            group.SpritesToRasterize.Clear();
        }

        UploadAtlas(group, padding);
        group.UploadPending = false;
    }

    private static void FullPack(Group group)
    {
        var swTotal = Stopwatch.StartNew();
        group.NeedsFullRepack = false;
        group.SpritesToRasterize.Clear();
        group.DisposeLayers();

        var atlasSize = EditorApplication.Config.AtlasSize;
        var padding = EditorApplication.Config.AtlasPadding;

        var swRasterize = new Stopwatch();
        var rectCount = 0;
        foreach (var sprite in group.Sources)
            sprite.UpdateBounds();

        foreach (var sprite in group.Sources)
        {
            foreach (var atlasPart in sprite.GetAtlasParts())
            {
                for (ushort frameIndex = 0; frameIndex < sprite.AtlasFrameCount; frameIndex++)
                {
                    var size = sprite.GetFrameAtlasSize(frameIndex);
                    if (size == Vector2Int.Zero) continue;

                    if (!TryPackIntoExistingLayer(group, sprite, atlasPart.Group, atlasPart.IsBase, frameIndex, size) &&
                        !AddNewLayer(group, atlasSize, sprite, atlasPart.Group, atlasPart.IsBase, frameIndex, size))
                    {
                        Log.Error($"Sprite '{sprite.GetAtlasAssetName(atlasPart.Group, atlasPart.IsBase)}' frame {frameIndex} too large to pack ({size.X}x{size.Y})");
                        continue;
                    }

                    rectCount++;
                    ref readonly var rect = ref CollectionsMarshal.AsSpan(group.Rects)[group.Rects.Count - 1];
                    swRasterize.Start();
                    sprite.Rasterize(group.Layers[rect.Layer], in rect, padding);
                    swRasterize.Stop();
                }
            }
        }

        UploadAtlas(group, padding);
        group.UploadPending = false;

        foreach (var sprite in group.Sources)
            sprite.MarkSpriteDirty();

        swTotal.Stop();
    }

    private static void UploadAtlas(Group group, int padding)
    {
        var atlasSize = EditorApplication.Config.AtlasSize;
        var capacity = atlasSize * atlasSize * 4 * group.Layers.Count + 64 * 1024;

        using var ms = new MemoryStream(capacity);
        SerializeAtlas(group, padding, ms, includeMultiSpriteComposites: true);
        group.LastSerialized = ms.ToArray();

        using var loadStream = new MemoryStream(group.LastSerialized);
        group.Atlas.LoadFromStream(loadStream, group.Name);
    }

    private static void SerializeAtlas(
        Group group,
        int padding,
        Stream output,
        bool includeMultiSpriteComposites)
    {
        var atlasSize = (float)EditorApplication.Config.AtlasSize;

        var frameLists = new Dictionary<string, (ushort Layer, List<Atlas.Frame> Frames)>();
        var orderedNames = new List<string>(group.Sources.Count);
        foreach (var rect in group.Rects)
        {
            if (rect.Source == null) continue;
            if (!includeMultiSpriteComposites &&
                rect.ExportGroup == null &&
                !rect.IsBasePart &&
                rect.Source.IsMultiSprite)
                continue;
            var name = rect.Source.GetAtlasAssetName(rect.ExportGroup, rect.IsBasePart);
            if (!frameLists.TryGetValue(name, out var entry))
            {
                entry = (rect.Layer, new List<Atlas.Frame>());
                frameLists[name] = entry;
                orderedNames.Add(name);
            }

            while (entry.Frames.Count <= rect.FrameIndex)
                entry.Frames.Add(default);

            var u = (rect.Rect.Left + padding) / atlasSize;
            var v = (rect.Rect.Top + padding) / atlasSize;
            var trim = rect.Source.RasterBounds.Size;
            var s = u + trim.X / atlasSize;
            var t = v + trim.Y / atlasSize;
            entry.Frames[rect.FrameIndex] = new Atlas.Frame
            {
                UV = Rect.FromMinMax(u, v, s, t),
                TrimSize = trim,
            };
        }

        var width = EditorApplication.Config.AtlasSize;
        var height = EditorApplication.Config.AtlasSize;

        var writer = new BinaryWriter(output, Encoding.UTF8, leaveOpen: true);
        writer.WriteAssetHeader(AssetType.Atlas, Atlas.Version, 0);
        writer.Write((byte)TextureFormat.RGBA8);
        writer.Write((byte)TextureFilter.Linear);
        writer.Write((byte)TextureClamp.Clamp);
        writer.Write((uint)width);
        writer.Write((uint)height);
        writer.Write((ushort)group.Layers.Count);

        foreach (var layer in group.Layers)
            writer.Write(layer.AsReadonlySpan());

        writer.Write((ushort)orderedNames.Count);
        foreach (var name in orderedNames)
        {
            var entry = frameLists[name];
            var nameBytes = Encoding.UTF8.GetBytes(name);
            writer.Write((ushort)nameBytes.Length);
            writer.Write(nameBytes);
            writer.Write(entry.Layer);
            writer.Write((ushort)entry.Frames.Count);
        }

        foreach (var name in orderedNames)
        {
            var entry = frameLists[name];
            foreach (var f in entry.Frames)
            {
                writer.Write(f.UV.Left);
                writer.Write(f.UV.Top);
                writer.Write(f.UV.Right);
                writer.Write(f.UV.Bottom);
                writer.Write((short)f.TrimSize.X);
                writer.Write((short)f.TrimSize.Y);
            }
        }

        writer.Flush();
    }

    public static void ExportIfNeeded()
    {
        // Only the game atlas ships to disk; the editor-only atlas stays in memory.
        if (_game.ExportPending) ExportGroup(_game);
        _game.ExportPending = false;
        _editor.ExportPending = false;
    }

    private static void ExportGroup(Group group)
    {
        if (group.NeedsFullRepack || group.UploadPending) IncrementalUpdate(group);
        if (group.NeedsFullRepack) FullPack(group);

        var outputDir = Path.Combine(Project.OutputPath, "atlas");
        Directory.CreateDirectory(outputDir);

        using var stream = new MemoryStream();
        SerializeAtlas(
            group,
            EditorApplication.Config.AtlasPadding,
            stream,
            includeMultiSpriteComposites: false);
        File.WriteAllBytes(Path.Combine(outputDir, group.Name), stream.ToArray());
    }
}
