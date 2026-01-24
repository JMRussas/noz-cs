//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Diagnostics;
using System.Runtime.InteropServices;

namespace NoZ.Editor
{
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
        private int _dpi;
        private Texture _texture = null!;
        private PixelData<Color32> _image = null!;
        private RectPacker _packer = null!;
        
        public int Index { get; internal set; }
        public Texture Texture => _texture;
        public int RectCount => _rects.Count;
        public ReadOnlySpan<AtlasSpriteRect> Rects => CollectionsMarshal.AsSpan(_rects);

        public AtlasDocument()
        {
            IsVisible = false;
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
                    _dpi = tk.ExpectInt();
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
            }

            _image = new PixelData<Color32>(size.X, size.Y);
            _packer = RectPacker.FromRects(size, _rects.Select(r => r.Rect));
            Bounds = new Rect(
                -size.X * 0.5f,
                -size.Y * 0.5f,
                size.X,
                size.Y).Scale(TextureDocument.PixelsPerUnitInv);
        }

        public override void Load()
        {
            var contents = File.ReadAllText(Path);
            var tk = new Tokenizer(contents);
            Load(ref tk);
        }

        public override void Save(StreamWriter writer)
        {
            writer.WriteLine($"w {EditorApplication.Config!.AtlasSize}");
            writer.WriteLine($"h {EditorApplication.Config!.AtlasSize}");
            writer.WriteLine();
            foreach (var rect in _rects)
                writer.WriteLine($"r \"{rect.Name}\" {rect.Rect.X} {rect.Rect.Y} {rect.Rect.Width} {rect.Rect.Height}");
        }

        public override void PostLoad()
        {
            _texture = Texture.Create(
                EditorApplication.Config!.AtlasSize,
                EditorApplication.Config.AtlasSize,
                _image.AsByteSpan(),
                TextureFormat.RGBA8,
                TextureFilter.Point,
                Name);
            
            base.PostLoad();
        }

        private static Rect ToUV(in AtlasSpriteRect rect)
        {
            if (rect.Sprite == null)
                return Rect.Zero;

            var size = rect.Sprite.RasterBounds.Size;
            var ts = (float)EditorApplication.Config.AtlasSize;
            var u = (rect.Rect.Left + 1)/ ts;
            var v = (rect.Rect.Top + 1) / ts;
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

                rect.Sprite = DocumentManager.Find(AssetType.Sprite, rect.Name) as SpriteDocument;
                if (rect.Sprite == null)
                    continue;
                
                ref var frame0 = ref rect.Sprite.Frames[0];
                if (frame0.Shape.RasterBounds.Size.X > rect.Rect.Size.X ||
                    frame0.Shape.RasterBounds.Size.Y > rect.Rect.Size.Y )
                {
                    rect.Sprite = null;
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

        internal bool TryAddSprite(SpriteDocument sprite)
        {
            // Try to reclaim an empty rect
            var rects = CollectionsMarshal.AsSpan(_rects);
            var size = sprite.RasterBounds.Size;
            for (int i = 0; i < _rects.Count; i++)
            {
                ref var rect = ref rects[i]; 
                if (rect.Sprite != null) continue;
                if (size.X > rect.Rect.Width || size.Y > rect.Rect.Height) continue;

                rect.Name = sprite.Name;
                rect.Sprite = sprite;
                rect.FrameCount = sprite.FrameCount;

                sprite.Atlas = this;
                sprite.AtlasUV = ToUV(rect);

                return true;
            }

            // Pack a new one
            var rectIndex = _packer.Insert(size + Vector2Int.One * 2, out var packedRect);
            if (rectIndex == -1)
                return false;
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

            return true;
        }

        public void Update()
        {
            var rects = CollectionsMarshal.AsSpan(_rects);
            for (int i = 0; i < _rects.Count; ++i)
            {
                ref var rect = ref rects[i];
                if (!rect.Dirty || rect.Sprite == null) continue;

                rect.Dirty = false;
                
                var palette = PaletteManager.GetPalette(rect.Sprite.Palette);
                if (palette == null) continue;

                for (int frameIndex = 0; frameIndex < rect.FrameCount; frameIndex++)
                {
                    var frame = rect.Sprite.GetFrame((ushort)frameIndex);
                    frame.Shape.Rasterize(
                        _image,
                        palette.Colors,
                        rect.Rect.Position + Vector2Int.One - frame.Shape.RasterBounds.Position);
                }
            }

            if (_texture != null)
                _texture.Update(_image.AsByteSpan());
        }

        public void Rebuild()
        {
            // Gather sprite names from existing rects
            var spriteNames = new List<string>();
            foreach (var rect in _rects)
            {
                if (!string.IsNullOrEmpty(rect.Name))
                    spriteNames.Add(rect.Name);
            }

            // Clear sprites from their atlas reference
            foreach (var rect in _rects)
            {
                if (rect.Sprite != null)
                    rect.Sprite.Atlas = null;
            }

            // Clear all rects and reset packer
            _rects.Clear();
            var atlasSize = EditorApplication.Config!.AtlasSize;
            _packer = new RectPacker(atlasSize, atlasSize);
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
            Update();
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

        public override void Import(string outputPath, PropertySet config, PropertySet meta)
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
}
