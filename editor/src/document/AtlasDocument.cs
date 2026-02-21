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
    public ISpriteSource? Source;
    public RectInt Rect;
    public ushort FrameIndex;
    public bool Dirty;
}

internal class AtlasDocument : Document
{
    public override bool CanSave => true;

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

    public override void Reload()
    {
        _texture?.Dispose();
        PostLoad();
    }

    internal void Clear()
    {
        var cleared = new HashSet<ISpriteSource>();
        var span = CollectionsMarshal.AsSpan(_rects);
        for (int i = 0; i < span.Length; i++)
        {
            ref var rect = ref span[i];
            if (rect.Source != null && cleared.Add(rect.Source))
            {
                rect.Source.Atlas = null;
                rect.Source.ClearAtlasUVs();
            }
        }

        _rects.Clear();
        var atlasSize = EditorApplication.Config!.AtlasSize;
        _packer = new RectPacker(atlasSize, atlasSize);
        Padding = EditorApplication.Config.AtlasPadding;
    }

    internal void ResolveSprites()
    {
        var resolved = new HashSet<ISpriteSource>();
        var span = CollectionsMarshal.AsSpan(_rects);
        for (int i = 0; i < span.Length; i++)
        {
            ref var rect = ref span[i];
            if (rect.Source != null)
                continue;

            if (string.IsNullOrEmpty(rect.Name))
                continue;

            // Try resolving as a sprite document first
            ISpriteSource? source = DocumentManager.Find(AssetType.Sprite, rect.Name) as SpriteDocument;

            // Try resolving as a texture-sprite
            if (source == null)
            {
                var texDoc = DocumentManager.Find(AssetType.Texture, rect.Name) as TextureDocument;
                if (texDoc is { IsSprite: true })
                    source = texDoc;
            }

            if (source == null)
                continue;

            // Check that this frame's rect is large enough
            if (rect.FrameIndex < source.FrameCount)
            {
                var frameSize = source.GetFrameAtlasSize(rect.FrameIndex);
                if (frameSize.X > rect.Rect.Size.X || frameSize.Y > rect.Rect.Size.Y)
                {
                    rect.Name = "";
                    MarkModified();
                    continue;
                }
            }

            rect.Source = source;
            source.Atlas = this;
            resolved.Add(source);
        }

        // Verify each source has all frame rects; if not, clear it for rebuild
        foreach (var source in resolved)
        {
            if (GetRectCount(source) != source.FrameCount)
            {
                Remove(source);
                source.Atlas = null;
                MarkModified();
                continue;
            }
            source.UpdateAtlasUVs(this, Rects, Padding);
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
            if (rect.Source != null) continue;
            if (size.X > rect.Rect.Width || size.Y > rect.Rect.Height) continue;
            return i;
        }
        return -1;
    }

    private int GetRectIndex(ISpriteSource source, ushort frameIndex)
    {
        var rects = CollectionsMarshal.AsSpan(_rects);
        for (int i = 0; i < _rects.Count; i++)
        {
            ref var rect = ref rects[i];
            if (rect.Source == source && rect.FrameIndex == frameIndex)
                return i;
        }
        return -1;
    }

    private int GetRectCount(ISpriteSource source)
    {
        int count = 0;
        var rects = CollectionsMarshal.AsSpan(_rects);
        for (int i = 0; i < _rects.Count; i++)
        {
            if (rects[i].Source == source)
                count++;
        }
        return count;
    }

    internal void Remove(ISpriteSource source)
    {
        var rects = CollectionsMarshal.AsSpan(_rects);
        for (int i = 0; i < _rects.Count; i++)
        {
            ref var rect = ref rects[i];
            if (rect.Source == source)
            {
                rect.Source = null;
                rect.Name = "";
                rect.Dirty = true;
            }
        }
    }

    internal bool TryAdd(ISpriteSource source)
    {
        for (ushort frameIndex = 0; frameIndex < source.FrameCount; frameIndex++)
        {
            var size = source.GetFrameAtlasSize(frameIndex);
            if (size == Vector2Int.Zero) return false;

            // Try to reclaim an empty rect
            var freeRectIndex = GetFreeRectIndex(size);
            if (freeRectIndex != -1)
            {
                ref var rect = ref CollectionsMarshal.AsSpan(_rects)[freeRectIndex];
                rect.Name = source.Name;
                rect.Source = source;
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
                    Remove(source);
                    return false;
                }
                Debug.Assert(rectIndex == _rects.Count);
                _rects.Add(new AtlasSpriteRect
                {
                    Name = source.Name,
                    Source = source,
                    Rect = packedRect,
                    FrameIndex = frameIndex,
                    Dirty = true
                });
            }
        }

        source.Atlas = this;
        source.UpdateAtlasUVs(this, Rects, Padding);
        source.Reimport();
        return true;
    }

    internal bool TryUpdate(ISpriteSource source)
    {
        // If frame count changed, remove all and re-add
        var currentRectCount = GetRectCount(source);
        if (currentRectCount != source.FrameCount)
        {
            Remove(source);
            return TryAdd(source);
        }

        // Check each frame fits in its rect
        for (ushort fi = 0; fi < source.FrameCount; fi++)
        {
            var rectIndex = GetRectIndex(source, fi);
            if (rectIndex == -1)
            {
                Remove(source);
                return TryAdd(source);
            }

            var size = source.GetFrameAtlasSize(fi);
            ref var rect = ref CollectionsMarshal.AsSpan(_rects)[rectIndex];
            if (size.X > rect.Rect.Width || size.Y > rect.Rect.Height)
            {
                Remove(source);
                return TryAdd(source);
            }

            rect.Dirty = true;
        }

        source.UpdateAtlasUVs(this, Rects, Padding);
        MarkModified();
        return true;
    }

    private void UpdateInternal(bool rebuild, ISpriteSource? pixelSource = null, PixelData<Color32>?[]? pixels = null)
    {
        var rects = CollectionsMarshal.AsSpan(_rects);
        RectInt? updateRect = null;
        var dirtySources = new HashSet<ISpriteSource>();

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

            if (rect.Source == null) continue;

            dirtySources.Add(rect.Source);

            // Use precomputed pixels if available for this source
            if (rect.Source == pixelSource && pixels != null &&
                rect.FrameIndex < pixels.Length && pixels[rect.FrameIndex] != null)
            {
                var pre = pixels[rect.FrameIndex]!;
                var copyW = Math.Min(pre.Width, rect.Rect.Width);
                var copyH = Math.Min(pre.Height, rect.Rect.Height);
                for (int y = 0; y < copyH; y++)
                    for (int x = 0; x < copyW; x++)
                        _image[rect.Rect.X + x, rect.Rect.Y + y] = pre[x, y];
                pre.Dispose();
                pixels[rect.FrameIndex] = null;
            }
            else
            {
                rect.Source.Rasterize(_image, in rect, Padding);
            }
        }

        // Update UVs once per dirty source
        foreach (var source in dirtySources)
            source.UpdateAtlasUVs(this, Rects, Padding);

        if (updateRect != null)
            _texture?.Update(_image.AsByteSpan(), updateRect.Value, _image.Width);

        // Dispose any unused pixel data
        if (pixels != null)
            foreach (var p in pixels)
                p?.Dispose();
    }

    public void Update() => UpdateInternal(false);

    internal void Update(ISpriteSource source, PixelData<Color32>?[]? pixels)
    {
        if (pixels == null)
            UpdateInternal(false);
        else
            UpdateInternal(false, source, pixels);
    }

    public void Rebuild()
    {
        // Gather unique sources from existing rects
        var sources = new HashSet<ISpriteSource>();
        foreach (var rect in _rects)
        {
            if (rect.Source != null)
                sources.Add(rect.Source);
        }

        foreach (var source in sources)
            source.Atlas = null;

        // Clear all rects and reset packer
        _rects.Clear();
        var atlasSize = EditorApplication.Config!.AtlasSize;
        _packer = new RectPacker(atlasSize, atlasSize);
        Padding = EditorApplication.Config.AtlasPadding;
        _image.Clear();

        // Re-add all sources
        foreach (var source in sources)
        {
            if (source is SpriteDocument sprite)
                sprite.UpdateBounds();

            if (!TryAdd(source))
                Log.Warning($"Rebuild: failed to add '{source.Name}'");
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
