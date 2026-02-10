//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

// #define NOZ_ATLAS_DEBUG

using System.Diagnostics;
using System.Runtime.InteropServices;

namespace NoZ.Editor;

internal struct AtlasSpriteRect
{
    public string Name;
    public SpriteDocument? Sprite;
    public RectInt Rect;
    public ushort FrameIndex;
    public bool Dirty;
}

internal class AtlasDocument : Document
{
    private readonly List<AtlasSpriteRect> _rects = new(128);
    private Texture _texture = null!;
    private PixelData<Color32> _image = null!;
    private RectPacker _packer = null!;
    
    public float PixelsPerUnit { get; private set; }
    public int Padding { get; private set; }
    public Texture Texture => _texture;
    public int RectCount => _rects.Count;
    public int Index { get; private set;  }
    public ReadOnlySpan<AtlasSpriteRect> Rects => CollectionsMarshal.AsSpan(_rects);

    public AtlasDocument()
    {
    }

    public static void RegisterDef()
    {
        DocumentManager.RegisterDef(new DocumentDef
        {
            Type = AssetType.Atlas,
            Extension = ".atlas",
            Factory = () => new AtlasDocument(),
            NewFile = NewFile,
            Icon = () => EditorAssets.Sprites.AssetIconAtlas
        });
    }

    private static void NewFile(StreamWriter writer)
    {
        writer.WriteLine($"w {EditorApplication.Config.AtlasSize}");
        writer.WriteLine($"h {EditorApplication.Config.AtlasSize}");
        writer.WriteLine($"d {Graphics.PixelsPerUnit}");
        writer.WriteLine($"p {EditorApplication.Config.AtlasPadding}");
    }

    private void Load(ref Tokenizer tk)
    {
        var size = new Vector2Int(EditorApplication.Config!.AtlasSize, EditorApplication.Config!.AtlasSize);
        while (!tk.IsEOF)
        {
            if (tk.ExpectIdentifier("w"))
            {
                size.X = tk.ExpectInt();
            }
            else if (tk.ExpectIdentifier("h"))
            {
                size.Y = tk.ExpectInt();
            }
            else if (tk.ExpectIdentifier("d"))
            {
                PixelsPerUnit = tk.ExpectInt();
            }
            else if (tk.ExpectIdentifier("p"))
            {
                Padding = tk.ExpectInt();
            }
            else if (tk.ExpectIdentifier("r"))
            {
                var name = tk.ExpectQuotedString();

                RectInt rect;
                rect.X = tk.ExpectInt();
                rect.Y = tk.ExpectInt();
                rect.Width = tk.ExpectInt();
                rect.Height = tk.ExpectInt();

                ushort frameIndex = (ushort)tk.ExpectInt(0);

                if (!string.IsNullOrEmpty(name))
                    _rects.Add(new AtlasSpriteRect { Name = name, Rect = rect, FrameIndex = frameIndex, Dirty = true });
            }
            else
            {
                throw new Exception();
            }

            Index = AtlasManager.GetAtlasIndex(Name);
        }

        var atlasSize = EditorApplication.Config.AtlasSize;
        var configPadding = EditorApplication.Config.AtlasPadding;
        if (size.X != atlasSize || size.Y != atlasSize ||
            PixelsPerUnit != EditorApplication.Config.PixelsPerUnit ||
            Padding != configPadding)
        {
            Clear();
            size.X = atlasSize;
            size.Y = atlasSize;
            PixelsPerUnit = EditorApplication.Config.PixelsPerUnit;
            Padding = configPadding;
            MarkModified();
        }

        _image = new PixelData<Color32>(size.X, size.Y);
        _packer = RectPacker.FromRects(size, _rects.Select(r => r.Rect));
        Bounds = new Rect(
            -size.X * 0.5f,
            -size.Y * 0.5f,
            size.X,
            size.Y).Scale(TextureDocument.PixelsPerUnitInv);
        Loaded = true;
    }

    public override void Load()
    {
        var contents = File.ReadAllText(Path);
        var tk = new Tokenizer(contents);
        Load(ref tk);
    }

    public override void Save(StreamWriter writer)
    {
        writer.WriteLine($"w {_image.Width}");
        writer.WriteLine($"h {_image.Height}");
        writer.WriteLine($"d {PixelsPerUnit}");
        writer.WriteLine($"p {Padding}");
        writer.WriteLine();
        foreach (var rect in _rects)
            writer.WriteLine($"r \"{rect.Name}\" {rect.Rect.X} {rect.Rect.Y} {rect.Rect.Width} {rect.Rect.Height} {rect.FrameIndex}");
    }

    public override void PostLoad()
    {
        _texture = Texture.Create(
            _image.Width,
            _image.Height,
            _image.AsByteSpan(),
            TextureFormat.RGBA8,
            TextureFilter.Point,
            Name);
        
        base.PostLoad();
    }

    internal void Clear()
    {
        var cleared = new HashSet<SpriteDocument>();
        var span = CollectionsMarshal.AsSpan(_rects);
        for (int i = 0; i < span.Length; i++)
        {
            ref var rect = ref span[i];
            if (rect.Sprite == null) continue;
            if (cleared.Add(rect.Sprite))
            {
                rect.Sprite.Atlas = null;
                rect.Sprite.ClearAtlasUVs();
                rect.Sprite.Reimport();
            }
        }

        _rects.Clear();
        var atlasSize = EditorApplication.Config!.AtlasSize;
        _packer = new RectPacker(atlasSize, atlasSize);
        Padding = EditorApplication.Config.AtlasPadding;
    }

    internal Rect ToUV(in AtlasSpriteRect rect, int slotIndex, Vector2Int slotSize, int slotXOffset)
    {
        if (rect.Sprite == null)
            return Rect.Zero;

        var ts = (float)EditorApplication.Config.AtlasSize;
        var u = (rect.Rect.Left + Padding + slotXOffset) / ts;
        var v = (rect.Rect.Top + Padding) / ts;
        var s = u + slotSize.X / ts;
        var t = v + slotSize.Y / ts;
        return Rect.FromMinMax(u, v, s, t);
    }

    internal void UpdateSpriteUVs(SpriteDocument sprite)
    {
        sprite.ClearAtlasUVs();
        var padding2 = Padding * 2;
        int uvIndex = 0;

        for (ushort frameIndex = 0; frameIndex < sprite.FrameCount; frameIndex++)
        {
            var rectIndex = GetRectIndex(sprite, frameIndex);
            if (rectIndex == -1) return;

            ref readonly var rect = ref CollectionsMarshal.AsSpan(_rects)[rectIndex];
            var slots = sprite.GetMeshSlots(frameIndex);
            var slotBounds = sprite.GetMeshSlotBounds(frameIndex);
            var xOffset = 0;

            for (int slotIndex = 0; slotIndex < slots.Count; slotIndex++)
            {
                var bounds = slotBounds[slotIndex];
                var slotSize = (bounds.Width > 0 && bounds.Height > 0)
                    ? bounds.Size
                    : sprite.RasterBounds.Size;

                sprite.SetAtlasUV(uvIndex, ToUV(rect, slotIndex, slotSize, xOffset));
                uvIndex++;
                xOffset += slotSize.X + padding2;
            }
        }
    }

    internal void ResolveSprites()
    {
        var resolved = new HashSet<SpriteDocument>();
        var span = CollectionsMarshal.AsSpan(_rects);
        for (int i = 0; i < span.Length; i++)
        {
            ref var rect = ref span[i];
            if (rect.Sprite != null)
                continue;

            if (string.IsNullOrEmpty(rect.Name))
                continue;

            rect.Sprite = DocumentManager.Find(AssetType.Sprite, rect.Name) as SpriteDocument;
            if (rect.Sprite == null)
                continue;

            // Check that this frame's rect is large enough
            if (rect.FrameIndex < rect.Sprite.FrameCount)
            {
                var frameSize = rect.Sprite.GetFrameAtlasSize(rect.FrameIndex);
                if (frameSize.X > rect.Rect.Size.X || frameSize.Y > rect.Rect.Size.Y)
                {
                    rect.Name = "";
                    rect.Sprite = null;
                    MarkModified();
                    continue;
                }
            }

            rect.Sprite.Atlas = this;
            resolved.Add(rect.Sprite);
        }

        foreach (var sprite in resolved)
            UpdateSpriteUVs(sprite);
    }

    public override void Dispose()
    {
        _texture?.Dispose();
        _image?.Dispose();
        _texture = null!;
        _image = null!;
        _rects.Clear();
        base.Dispose();
    }

    private int GetFreeRectIndex(Vector2Int size)
    {
        var rects = CollectionsMarshal.AsSpan(_rects);
        for (int i = 0; i < _rects.Count; i++)
        {
            ref var rect = ref rects[i];
            if (rect.Sprite != null) continue;
            if (size.X > rect.Rect.Width || size.Y > rect.Rect.Height) continue;
            return i;
        }
        return -1;
    }

    private int GetRectIndex(SpriteDocument sprite, ushort frameIndex)
    {
        var rects = CollectionsMarshal.AsSpan(_rects);
        for (int i = 0; i < _rects.Count; i++)
        {
            ref var rect = ref rects[i];
            if (rect.Sprite == sprite && rect.FrameIndex == frameIndex)
                return i;
        }
        return -1;
    }

    private int GetRectCount(SpriteDocument sprite)
    {
        int count = 0;
        var rects = CollectionsMarshal.AsSpan(_rects);
        for (int i = 0; i < _rects.Count; i++)
        {
            if (rects[i].Sprite == sprite)
                count++;
        }
        return count;
    }

    private void RemoveSpriteRects(SpriteDocument sprite)
    {
        var rects = CollectionsMarshal.AsSpan(_rects);
        for (int i = 0; i < _rects.Count; i++)
        {
            ref var rect = ref rects[i];
            if (rect.Sprite == sprite)
            {
                rect.Sprite = null;
                rect.Name = "";
                rect.Dirty = true;
            }
        }
    }

    internal bool TryAddSprite(SpriteDocument sprite)
    {
        for (ushort frameIndex = 0; frameIndex < sprite.FrameCount; frameIndex++)
        {
            var size = sprite.GetFrameAtlasSize(frameIndex);

            // Try to reclaim an empty rect
            var freeRectIndex = GetFreeRectIndex(size);
            if (freeRectIndex != -1)
            {
                ref var rect = ref CollectionsMarshal.AsSpan(_rects)[freeRectIndex];
                rect.Name = sprite.Name;
                rect.Sprite = sprite;
                rect.FrameIndex = frameIndex;
                rect.Dirty = true;
            }
            else
            {
                // Pack a new rect
                var rectIndex = _packer.Insert(size, out var packedRect);
                if (rectIndex == -1)
                {
                    // Failed — roll back frames already added
                    RemoveSpriteRects(sprite);
                    return false;
                }
                Debug.Assert(rectIndex == _rects.Count);
                _rects.Add(new AtlasSpriteRect
                {
                    Name = sprite.Name,
                    Sprite = sprite,
                    Rect = packedRect,
                    FrameIndex = frameIndex,
                    Dirty = true
                });
            }
        }

        sprite.Atlas = this;
        UpdateSpriteUVs(sprite);
        sprite.Reimport();
        return true;
    }

    internal bool TryUpdate(SpriteDocument sprite)
    {
        // If frame count changed, remove all and re-add
        var currentRectCount = GetRectCount(sprite);
        if (currentRectCount != sprite.FrameCount)
        {
            RemoveSpriteRects(sprite);
            return TryAddSprite(sprite);
        }

        // Check each frame fits in its rect
        for (ushort fi = 0; fi < sprite.FrameCount; fi++)
        {
            var rectIndex = GetRectIndex(sprite, fi);
            if (rectIndex == -1)
            {
                RemoveSpriteRects(sprite);
                return TryAddSprite(sprite);
            }

            var size = sprite.GetFrameAtlasSize(fi);
            ref var rect = ref CollectionsMarshal.AsSpan(_rects)[rectIndex];
            if (size.X > rect.Rect.Width || size.Y > rect.Rect.Height)
            {
                RemoveSpriteRects(sprite);
                return TryAddSprite(sprite);
            }

            rect.Dirty = true;
        }

        UpdateSpriteUVs(sprite);
        MarkModified();
        return true;
    }

    private void UpdateInternal(bool rebuild)
    {
        var rects = CollectionsMarshal.AsSpan(_rects);
        RectInt? updateRect = null;
        var dirtySprites = new HashSet<SpriteDocument>();

        for (int i = 0; i < _rects.Count; ++i)
        {
            ref var rect = ref rects[i];
            if (!rect.Dirty) continue;

            updateRect = updateRect.HasValue
                ? RectInt.Union(updateRect.Value, rect.Rect)
                : rect.Rect;

            rect.Dirty = false;

            if (!rebuild)
                _image.Clear(rect.Rect);

            if (rect.Sprite == null) continue;

            var palette = PaletteManager.GetPalette(rect.Sprite.Palette);
            if (palette == null) continue;

            dirtySprites.Add(rect.Sprite);

            // Each rect is one frame — use that frame's own slots and bounds
            var frameIndex = rect.FrameIndex;
            var frame = rect.Sprite.GetFrame(frameIndex);
            var slots = rect.Sprite.GetMeshSlots(frameIndex);
            var slotBounds = rect.Sprite.GetMeshSlotBounds(frameIndex);
            var padding2 = Padding * 2;
            var xOffset = 0;

            for (int slotIndex = 0; slotIndex < slots.Count; slotIndex++)
            {
                var slot = slots[slotIndex];
                var slotRasterBounds = slotBounds[slotIndex];
                if (slotRasterBounds.Width <= 0 || slotRasterBounds.Height <= 0)
                    slotRasterBounds = rect.Sprite.RasterBounds;

                var slotWidth = slotRasterBounds.Size.X + padding2;

                AtlasManager.LogAtlas($"Rasterize: Name={rect.Name} Frame={frameIndex} Layer={slot.Layer} Bone={slot.Bone} Rect={rect.Rect} SlotBounds={slotRasterBounds}");

                var outerRect = new RectInt(
                    rect.Rect.Position + new Vector2Int(xOffset, 0),
                    new Vector2Int(slotWidth, slotRasterBounds.Size.Y + padding2));
                var rasterRect = new RectInt(
                    outerRect.Position + new Vector2Int(Padding, Padding),
                    slotRasterBounds.Size);

                if (slot.PathIndices.Count > 0)
                {
                    frame.Shape.Rasterize(
                        _image,
                        rasterRect,
                        -slotRasterBounds.Position,
                        palette.Colors,
                        CollectionsMarshal.AsSpan(slot.PathIndices),
                        rect.Sprite.IsAntiAliased);
                }

                _image.BleedColors(rasterRect);
                for (int p = Padding - 1; p >= 0; p--)
                {
                    var padRect = new RectInt(
                        outerRect.Position + new Vector2Int(p, p),
                        outerRect.Size - new Vector2Int(p * 2, p * 2));
                    _image.ExtrudeEdges(padRect);
                }

                xOffset += slotWidth;
            }
        }

        // Update UVs once per dirty sprite
        foreach (var sprite in dirtySprites)
            UpdateSpriteUVs(sprite);

        if (updateRect != null)
            _texture?.Update(_image.AsByteSpan(), updateRect.Value, _image.Width);
    }

    public void Update() => UpdateInternal(false);

    public void Rebuild()
    {
        // Gather unique sprite names from existing rects
        var spriteNames = new HashSet<string>();
        foreach (var rect in _rects)
        {
            if (!string.IsNullOrEmpty(rect.Name))
                spriteNames.Add(rect.Name);
        }

        foreach (var rect in _rects)
            rect.Sprite?.Atlas = null;

        // Clear all rects and reset packer
        _rects.Clear();
        var atlasSize = EditorApplication.Config!.AtlasSize;
        _packer = new RectPacker(atlasSize, atlasSize);
        Padding = EditorApplication.Config.AtlasPadding;
        _image.Clear();

        // Re-add all sprites
        foreach (var name in spriteNames)
        {
            var sprite = DocumentManager.Find(AssetType.Sprite, name) as SpriteDocument;
            if (sprite == null)
            {
                Log.Warning($"Rebuild: sprite '{name}' not found");
                continue;
            }

            sprite.UpdateBounds();
            if (!TryAddSprite(sprite))
                Log.Warning($"Rebuild: failed to add sprite '{name}'");
        }

        // Update texture
        UpdateInternal(true);
        MarkModified();
    }

    public override void Draw()
    {
        base.Draw();

        using (Graphics.PushState())
        {
            Graphics.SetShader(EditorAssets.Shaders.Texture);
            Graphics.SetTexture(_texture);
            Graphics.SetColor(Color.White);
            Graphics.Draw(Bounds);
        }
    }

    public override void Import(string outputPath, PropertySet meta)
    {
        using var writer = new BinaryWriter(File.Create(outputPath));
        writer.WriteAssetHeader(AssetType.Atlas, Atlas.Version, 0);

        var format = TextureFormat.RGBA8;
        var filter = TextureFilter.Point;
        var clamp = TextureClamp.Clamp;

        writer.Write((byte)format);
        writer.Write((byte)filter);
        writer.Write((byte)clamp);
        writer.Write((uint)_image.Width);
        writer.Write((uint)_image.Height);
        writer.Write(_image.AsByteSpan());
    }
}
