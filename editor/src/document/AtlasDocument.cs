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
    public int FrameCount;
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
        DocumentManager.RegisterDef(new DocumentDef(
            AssetType.Atlas,
            ".atlas",
            () => new AtlasDocument(),
            editorFactory: doc => new AtlasEditor((AtlasDocument)doc),
            newFile: NewFile
        ));
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

                int frameCount = tk.ExpectInt(1);

                if (!string.IsNullOrEmpty(name))
                    _rects.Add(new AtlasSpriteRect { Name = name, Rect = rect, FrameCount = frameCount, Dirty = true });
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
            writer.WriteLine($"r \"{rect.Name}\" {rect.Rect.X} {rect.Rect.Y} {rect.Rect.Width} {rect.Rect.Height}");
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
        var span = CollectionsMarshal.AsSpan(_rects);
        for (int i = 0; i < span.Length; i++)
        {
            ref var rect = ref span[i];
            if (rect.Sprite == null) continue;
            rect.Sprite.Atlas = null;
            rect.Sprite.AtlasUV = Rect.Zero;
            rect.Sprite.Reimport();
        }

        _rects.Clear();
        var atlasSize = EditorApplication.Config!.AtlasSize;
        _packer = new RectPacker(atlasSize, atlasSize);
        Padding = EditorApplication.Config.AtlasPadding;
    }

    private Rect ToUV(in AtlasSpriteRect rect)
    {
        if (rect.Sprite == null)
            return Rect.Zero;

        var size = rect.Sprite.RasterBounds.Size;
        var ts = (float)EditorApplication.Config.AtlasSize;
        var u = (rect.Rect.Left + Padding) / ts;
        var v = (rect.Rect.Top + Padding) / ts;
        var s = u + size.X / ts;
        var t = v + size.Y / ts;
        return Rect.FromMinMax(u, v, s, t);
    }

    internal void ResolveSprites()
    {
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
            
            var rasterBounds = rect.Sprite.RasterBounds;
            if (rasterBounds.Size.X > rect.Rect.Size.X ||
                rasterBounds.Size.Y > rect.Rect.Size.Y )
            {
                rect.Name = "";
                rect.Sprite = null;
                MarkModified();
                continue;
            }
                
            rect.Sprite.Atlas = this;
            rect.Sprite.AtlasUV = ToUV(rect);
        }
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

    private int GetRectIndex(SpriteDocument sprite)
    {
        var rects = CollectionsMarshal.AsSpan(_rects);
        for (int i = 0; i < _rects.Count; i++)
        {
            ref var rect = ref rects[i];
            if (rect.Sprite == sprite)
                return i;
        }
        return -1;
    }

    internal bool TryAddSprite(SpriteDocument sprite)
    {
        // Try to reclaim an empty rect
        var rects = CollectionsMarshal.AsSpan(_rects);
        var size = sprite.AtlasSize;
        var freeRectIndex = GetFreeRectIndex(size);
        if (freeRectIndex != -1)
        {
            ref var rect = ref rects[freeRectIndex];
            rect.Name = sprite.Name;
            rect.Sprite = sprite;
            rect.FrameCount = sprite.FrameCount;
            rect.Dirty = true;
            sprite.Atlas = this;
            sprite.AtlasUV = ToUV(rect);
            rect.Sprite.Reimport();
            return true;
        }

        // Pack a new one (AtlasSize already includes per-frame padding)
        var rectIndex = _packer.Insert(size, out var packedRect);
        if (rectIndex == -1) return false;
        Debug.Assert(rectIndex == _rects.Count);
        _rects.Add(new AtlasSpriteRect
        {
            Name = sprite.Name,
            Sprite = sprite,
            Rect = packedRect,
            FrameCount = sprite.FrameCount,
            Dirty = true
        });

        sprite.Atlas = this;
        sprite.AtlasUV = ToUV(_rects[^1]);
        sprite.Reimport();

        return true;
    }

    internal bool TryUpdate(SpriteDocument sprite)
    { 
        var rectIndex = GetRectIndex(sprite);
        if (rectIndex == -1)
            return false;

        var rects = CollectionsMarshal.AsSpan(_rects);
        ref var rect = ref rects[rectIndex];
        var size = sprite.AtlasSize;

        if (size.X > rect.Rect.Width || size.Y > rect.Rect.Height)
        {
            rect.Sprite = null;
            rect.Dirty = true;
            return TryAddSprite(sprite);
        }

        rect.Dirty = true;
        MarkModified();
        return true;
    }

    private void UpdateInternal(bool rebuild)
    {
        var rects = CollectionsMarshal.AsSpan(_rects);
        RectInt? updateRect = null;
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

            for (int frameIndex = 0; frameIndex < rect.FrameCount; frameIndex++)
            {
                var frame = rect.Sprite.GetFrame((ushort)frameIndex);

                AtlasManager.LogAtlas($"Rasterize: Name={rect.Name} Rect={rect.Rect} Size={rect.Sprite.AtlasSize}");

                var rasterBounds = rect.Sprite.RasterBounds;
                var padding2 = Padding * 2;
                var frameStride = rasterBounds.Size.X + padding2;
                var outerRect = new RectInt(
                    rect.Rect.Position + new Vector2Int(frameIndex * frameStride, 0),
                    new Vector2Int(frameStride, rasterBounds.Size.Y + padding2));
                var rasterRect = new RectInt(
                    outerRect.Position + new Vector2Int(Padding, Padding),
                    rasterBounds.Size);
                frame.Shape.Rasterize(
                    _image,
                    rasterRect,
                    -rect.Sprite.RasterBounds.Position,
                    palette.Colors,
                    new Shape.RasterizeOptions { Name = rect.Sprite.Name, AntiAlias = rect.Sprite.IsAntiAliased });

                _image.BleedColors(rasterRect);
                for (int p = Padding - 1; p >= 0; p--)
                {
                    var padRect = new RectInt(
                        outerRect.Position + new Vector2Int(p, p),
                        outerRect.Size - new Vector2Int(p * 2, p * 2));
                    _image.ExtrudeEdges(padRect);
                }
            }

            rect.Sprite.AtlasUV = ToUV(rect);
        }

        if (updateRect != null)
            _texture?.Update(_image.AsByteSpan(), updateRect.Value, _image.Width);
    }

    public void Update() => UpdateInternal(false);

    public void Rebuild()
    {
        // Gather sprite names from existing rects
        var spriteNames = new List<string>();
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
