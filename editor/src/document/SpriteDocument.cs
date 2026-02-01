//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Globalization;
using System.Numerics;

namespace NoZ.Editor;

public class SpriteFrame : IDisposable
{
    public readonly Shape Shape = new();
    public int Hold;

    public void Dispose()
    {
        Shape.Dispose();
    }
}

public class SpriteDocument : Document
{
    public class BoneBinding
    {
        public string SkeletonName = "";
        public string BoneName = "";
        public SkeletonDocument? Skeleton;
        public int BoneIndex = -1;
        public Vector2 Offset;

        public bool IsBound => Skeleton != null && BoneIndex >= 0;
        public bool IsBoundTo(SkeletonDocument skeleton) =>
            Skeleton == skeleton && BoneIndex >= 0;

        public void Set(SkeletonDocument? skeleton, int boneIndex)
        {
            if (skeleton == null || boneIndex < 0 || boneIndex >= skeleton.BoneCount)
            {
                Clear();
                return;
            }

            Skeleton = skeleton;
            SkeletonName = skeleton.Name;
            BoneName = skeleton.Bones[boneIndex].Name;
            BoneIndex = boneIndex;
        }

        public void Clear()
        {
            Skeleton = null;
            SkeletonName = "";
            BoneName = "";
            BoneIndex = -1;
            Offset = Vector2.Zero;
        }

        public void CopyFrom(BoneBinding src)
        {
            SkeletonName = src.SkeletonName;
            BoneName = src.BoneName;
            Skeleton = src.Skeleton;
            BoneIndex = src.BoneIndex;
            Offset = src.Offset;
        }

        public void Resolve()
        {
            Skeleton = null;
            BoneIndex = -1;

            if (string.IsNullOrEmpty(SkeletonName))
                return;

            foreach (var doc in DocumentManager.Documents)
            {
                if (doc is SkeletonDocument skel && doc.Name == SkeletonName)
                {
                    Skeleton = skel;
                    if (!string.IsNullOrEmpty(BoneName))
                        BoneIndex = skel.FindBoneIndex(BoneName);
                    break;
                }
            }
        }
    }

    private BitMask256 _layers = new();
    private Rect[] _atlasUV = new Rect[255];
    private Sprite? _sprite;

    public readonly SpriteFrame[] Frames = new SpriteFrame[Sprite.MaxFrames];
    public ushort FrameCount;
    public byte Palette;
    public float Depth;
    public ushort Order;
    public RectInt RasterBounds { get; private set; }

    public ref readonly BitMask256 Layers => ref _layers;

    public Vector2Int AtlasSize
    {
        get
        {
            var padding2 = EditorApplication.Config.AtlasPadding * 2;
            var layerCount = Math.Max(1, _layers.Count);
            return new((RasterBounds.Size.X + padding2) * FrameCount * layerCount, RasterBounds.Size.Y + padding2);
        }
    }
    public bool ShowInSkeleton { get; set; }
    public bool ShowTiling { get; set; }
    public bool ShowSkeletonOverlay { get; set; }
    public Vector2Int? ConstrainedSize { get; set; }

    internal AtlasDocument? Atlas;

    public readonly BoneBinding Binding = new();

    public Rect AtlasUV => _atlasUV[0];

    public Sprite? Sprite
    {
        get
        {
            if (_sprite == null) UpdateSprite();
            return _sprite;
        }
    }

    public SpriteDocument()
    {
        for (var i = 0; i < Frames.Length; i++)
            Frames[i] = new SpriteFrame();
    }

    static SpriteDocument()
    {
        SkeletonDocument.BoneRenamed += OnSkeletonBoneRenamed;
        SkeletonDocument.BoneRemoved += OnSkeletonBoneRemoved;
    }

    public static void RegisterDef()
    {
        DocumentManager.RegisterDef(new DocumentDef
        {
            Type = AssetType.Sprite,
            Extension = ".sprite",
            Factory = () => new SpriteDocument(),
            EditorFactory = doc => new SpriteEditor((SpriteDocument)doc),
            NewFile = NewFile,
            Icon = () => EditorAssets.Sprites.AssetIconSprite
        });
    }

    private static void OnSkeletonBoneRenamed(SkeletonDocument skeleton, int boneIndex, string oldName, string newName)
    {
        foreach (var doc in DocumentManager.Documents.OfType<SpriteDocument>())
        {
            if (doc.Binding.Skeleton != skeleton || doc.Binding.BoneName != oldName)
                continue;

            doc.Binding.BoneName = newName;
            doc.MarkMetaModified();
        }
    }

    private static void OnSkeletonBoneRemoved(SkeletonDocument skeleton, int removedIndex, string removedName)
    {
        foreach (var doc in DocumentManager.Documents.OfType<SpriteDocument>())
        {
            if (doc.Binding.Skeleton != skeleton)
                continue;

            if (doc.Binding.BoneName == removedName)
            {
                doc.Binding.Clear();
                doc.MarkMetaModified();
                Notifications.Add($"Sprite '{doc.Name}' bone binding cleared (bone '{removedName}' deleted)");
            }
            else if (doc.Binding.BoneIndex > removedIndex)
            {
                doc.Binding.BoneIndex--;
            }
        }
    }

    public SpriteFrame GetFrame(ushort frameIndex) => Frames[frameIndex];

    private static void NewFile(StreamWriter writer)
    {
        writer.WriteLine("c 0 a");
    }

    public override void Load()
    {
        var contents = File.ReadAllText(Path);
        var tk = new Tokenizer(contents);
        Load(ref tk);
        UpdateBounds();
        Loaded = true;
    }

    private void Load(ref Tokenizer tk)
    {
        var f = Frames[FrameCount++];

        while (!tk.IsEOF)
        {
            if (tk.ExpectIdentifier("p"))
            {
                ParsePath(f, ref tk);
            }
            else if (tk.ExpectIdentifier("c"))
            {
                Palette = (byte)tk.ExpectInt();
                IsAntiAliased = tk.ExpectIdentifier("a");
            }
            else if (tk.ExpectIdentifier("o"))
            {
                Order = (ushort)tk.ExpectInt();
            }
            else if (tk.ExpectIdentifier("f"))
            {
                if (tk.ExpectIdentifier("h"))
                    f.Hold = tk.ExpectInt();
                f = Frames[FrameCount++];
            }
            else
            {
                break;
            }
        }
    }

    private static void ParsePath(SpriteFrame f, ref Tokenizer tk)
    {
        var pathIndex = f.Shape.AddPath();
        byte fillColor = 0;
        var opacity = 1.0f;
        byte layer = 0;

        while (!tk.IsEOF)
        {
            if (tk.ExpectIdentifier("c"))
                fillColor = (byte)tk.ExpectInt();
            else if (tk.ExpectIdentifier("o"))
                opacity = tk.ExpectFloat();
            else if (tk.ExpectIdentifier("h"))
                opacity = float.MinValue;
            else if (tk.ExpectIdentifier("l"))
                layer = EditorApplication.Config.TryGetSpriteLayer(tk.ExpectQuotedString(), out var sg)
                    ? sg.Layer
                    : (byte)0;
            else if (tk.ExpectIdentifier("a"))
                ParseAnchor(f.Shape, pathIndex, ref tk);
            else
                break;
        }

        f.Shape.SetPathFillColor(pathIndex, fillColor, opacity);
        f.Shape.SetPathLayer(pathIndex, layer);
    }

    private static void ParseAnchor(Shape shape, ushort pathIndex, ref Tokenizer tk)
    {
        var x = tk.ExpectFloat();
        var y = tk.ExpectFloat();
        var curve = tk.ExpectFloat();
        shape.AddAnchor(pathIndex, new Vector2(x, y), curve);
    }

    public void UpdateBounds()
    {
        UpdateLayers();

        if (ConstrainedSize.HasValue)
        {
            var cs = ConstrainedSize.Value;
            var ppu = EditorApplication.Config.PixelsPerUnitInv;
            Bounds = new Rect(
                cs.X * ppu * -0.5f,
                cs.Y * ppu * -0.5f,
                cs.X * ppu,
                cs.Y * ppu);
            RasterBounds = new RectInt(
                -cs.X / 2,
                -cs.Y / 2,
                cs.X,
                cs.Y);

            return;
        }

        if (FrameCount <= 0)
        {
            Bounds = new Rect(-0.5f, -0.5f, 1f, 1f);
            return;
        }

        var bounds = Frames[0].Shape.Bounds;
        for (ushort fi = 1; fi < FrameCount; fi++)
        {
            var fb = Frames[fi].Shape.Bounds;
            var minX = MathF.Min(bounds.X, fb.X);
            var minY = MathF.Min(bounds.Y, fb.Y);
            var maxX = MathF.Max(bounds.Right, fb.Right);
            var maxY = MathF.Max(bounds.Bottom, fb.Bottom);
            bounds = Rect.FromMinMax(new Vector2(minX, minY), new Vector2(maxX, maxY));
        }
        Bounds = bounds;

        if (Bounds.Width <= 0 || Bounds.Height <= 0)
        {
            Bounds = new Rect(-0.5f, -0.5f, 1f, 1f);
            return;
        }

        RasterBounds = Frames[0].Shape.RasterBounds;

        for (ushort fi = 0; fi < FrameCount; fi++)
        {
            Frames[fi].Shape.UpdateSamples();
            Frames[fi].Shape.UpdateBounds();
            RasterBounds = RasterBounds.Union(Frames[fi].Shape.RasterBounds);
        }

        if (ConstrainedSize.HasValue)
        {
            var cs = ConstrainedSize.Value;
            var centerX = RasterBounds.X + RasterBounds.Width / 2;
            var centerY = RasterBounds.Y + RasterBounds.Height / 2;
            RasterBounds = new RectInt(
                centerX - cs.X / 2,
                centerY - cs.Y / 2,
                cs.X,
                cs.Y);
        }

        ClampToMaxSpriteSize();
        Bounds = RasterBounds.ToRect().Scale(1.0f / EditorApplication.Config.PixelsPerUnit);
        MarkSpriteDirty();
    }

    private void ClampToMaxSpriteSize()
    {
        var maxSize = EditorApplication.Config.AtlasMaxSpriteSize;
        var width = RasterBounds.Width;
        var height = RasterBounds.Height;

        if (width <= maxSize && height <= maxSize)
            return;

        var centerX = RasterBounds.X + width / 2;
        var centerY = RasterBounds.Y + height / 2;
        var clampedWidth = Math.Min(width, maxSize);
        var clampedHeight = Math.Min(height, maxSize);

        RasterBounds = new RectInt(
            centerX - clampedWidth / 2,
            centerY - clampedHeight / 2,
            clampedWidth,
            clampedHeight);
    }

    private void UpdateLayers()
    {
        _layers.Clear();
        for (ushort fi = 0; fi < FrameCount; fi++)
            _layers |= Frames[fi].Shape.Layers;
    }

    // :save
    public override void Save(StreamWriter writer)
    {
        writer.WriteLine($"c {Palette}");
        if (IsAntiAliased)
            writer.WriteLine(" a");
        if (Order > 0)
            writer.WriteLine($"o {Order}");
        writer.WriteLine();

        for (ushort frameIndex = 0; frameIndex < FrameCount; frameIndex++)
        {
            var f = GetFrame(frameIndex);

            if (FrameCount > 1 || f.Hold > 0)
            {
                writer.Write('f');
                if (f.Hold > 0)
                    writer.Write($" h {f.Hold}");
                writer.WriteLine();
            }

            SaveFrame(f, writer);

            if (frameIndex < FrameCount - 1)
                writer.WriteLine();
        }        
    }
    
    private static void SaveFrame(SpriteFrame f, StreamWriter writer)
    {
        var shape = f.Shape;

        for (ushort pIdx = 0; pIdx < shape.PathCount; pIdx++)
        {
            var path = shape.GetPath(pIdx);
            var opacity = path.IsSubtract
                ? " h"
                : path.FillOpacity < 1
                    ? $" o {path.FillOpacity}"
                    : "";

            var layer = EditorApplication.Config.TryGetSpriteLayer(path.Layer, out var layerDef)
                ? $" l \"{layerDef.Id}\""
                : "";
            writer.WriteLine($"p c {path.FillColor}{opacity}{layer}");

            for (ushort aIdx = 0; aIdx < path.AnchorCount; aIdx++)
            {
                var anchor = shape.GetAnchor((ushort)(path.AnchorStart + aIdx));
                writer.Write(string.Format(CultureInfo.InvariantCulture, "a {0} {1}", anchor.Position.X, anchor.Position.Y));
                if (MathF.Abs(anchor.Curve) > float.Epsilon)
                    writer.Write(string.Format(CultureInfo.InvariantCulture, " {0}", anchor.Curve));
                writer.WriteLine();
            }

            writer.WriteLine();
        }
    }

    public override void Draw()
    {
        DrawOrigin();

        var size = Bounds.Size;
        if (size.X <= 0 || size.Y <= 0 || Atlas == null)
            return;

        ref var frame0 = ref Frames[0];
        if (frame0.Shape.PathCount == 0)
        {
            DrawBounds();
            return;
        }

        DrawSprite();
    }

    public void DrawSprite(in Vector2 offset = default, float alpha = 1.0f)
    {
        if (Atlas == null) return;

        var sprite = Sprite;
        if (sprite == null) return;

        using (Graphics.PushState())
        {
            Graphics.SetTexture(Atlas.Texture);
            Graphics.SetShader(EditorAssets.Shaders.Texture);
            Graphics.SetColor(Color.White.WithAlpha(alpha * Workspace.XrayAlpha));
            Graphics.SetTextureFilter(sprite.TextureFilter);

            var bounds = RasterBounds.ToRect().Scale(Graphics.PixelsPerUnitInv).Translate(offset);

            foreach (ref readonly var mesh in sprite.Meshes.AsSpan())
            {
                Graphics.Draw(bounds, mesh.UV, order: (ushort)mesh.SortOrder);
            }
        }
    }

    public override void Clone(Document source)
    {
        var src = (SpriteDocument)source;
        FrameCount = src.FrameCount;
        Palette = src.Palette;
        Depth = src.Depth;
        Order = src.Order;
        Bounds = src.Bounds;
        Binding.CopyFrom(src.Binding);

        for (var i = 0; i < src.FrameCount; i++)
        {
            Frames[i].Shape.CopyFrom(src.Frames[i].Shape);
            Frames[i].Hold = src.Frames[i].Hold;
        }

        for (var i = src.FrameCount; i < Sprite.MaxFrames; i++)
            Frames[i].Shape.Clear();
    }

    public override void LoadMetadata(PropertySet meta)
    {
        Binding.SkeletonName = meta.GetString("bone", "skeleton", "");
        Binding.BoneName = meta.GetString("bone", "name", "");
        Binding.Offset = meta.GetVector2("bone", "offset", Vector2.Zero);
        ShowInSkeleton = meta.GetBool("sprite", "show_in_skeleton", false);
        ShowTiling = meta.GetBool("sprite", "show_tiling", false);
        ShowSkeletonOverlay = meta.GetBool("sprite", "show_skeleton_overlay", false);
        ConstrainedSize = ParseConstrainedSize(meta.GetString("sprite", "constrained_size", ""));
    }

    private static Vector2Int? ParseConstrainedSize(string value)
    {
        if (string.IsNullOrEmpty(value))
            return null;
        var parts = value.Split('x');
        if (parts.Length == 2 &&
            int.TryParse(parts[0], out var w) &&
            int.TryParse(parts[1], out var h))
        {
            return new Vector2Int(w, h);
        }
        return null;
    }

    public override void SaveMetadata(PropertySet meta)
    {
        meta.SetBool("sprite", "show_in_skeleton", ShowInSkeleton);
        meta.SetBool("sprite", "show_tiling", ShowTiling);
        meta.SetBool("sprite", "show_skeleton_overlay", ShowSkeletonOverlay);
        if (ConstrainedSize.HasValue)
            meta.SetString("sprite", "constrained_size", $"{ConstrainedSize.Value.X}x{ConstrainedSize.Value.Y}");
        if (Binding.IsBound)
        {
            meta.SetString("bone", "skeleton", Binding.SkeletonName);
            meta.SetString("bone", "name", Binding.BoneName);
            meta.SetVec2("bone", "offset", Binding.Offset);
        }
        else
        {
            meta.ClearGroup("bone");
        }
    }

    public override void PostLoad()
    {
        Binding.Resolve();
    }

    public void SetBoneBinding(SkeletonDocument? skeleton, int boneIndex)
    {
        Binding.Set(skeleton, boneIndex);
        MarkSpriteDirty();
        MarkMetaModified();
    }

    public void ClearBoneBinding()
    {
        var skeleton = Binding.Skeleton;
        Binding.Clear();
        MarkSpriteDirty();
        skeleton?.UpdateSprites();
        MarkMetaModified();
    }

    internal void ClearAtlasUVs()
    {
        for (int i = 0; i < _atlasUV.Length; i++)
            _atlasUV[i] = Rect.Zero;
        MarkSpriteDirty();
    }

    internal void SetAtlasUV(byte layer, Rect uv)
    {
        _atlasUV[layer] = uv;
        MarkSpriteDirty();
    }

    internal Rect GetAtlasUV(byte layer) =>
        _atlasUV[layer];

    private void UpdateSprite()
    {
        if (Atlas == null || _layers.Count == 0)
        {
            _sprite = null;
            return;
        }

        var meshes = new SpriteMesh[_layers.Count];
        int idx = 0;

        for (int layer = 0; layer < 255; layer++)
        {
            if (!_layers[layer]) continue;
            var uv = _atlasUV[layer];
            if (uv == Rect.Zero)
            {
                _sprite = null;
                return;
            }
            meshes[idx++] = new SpriteMesh(uv, (short)layer);
        }

        _sprite = Sprite.Create(
            name: Name,
            bounds: RasterBounds,
            pixelsPerUnit: EditorApplication.Config.PixelsPerUnit,
            filter: TextureFilter.Point,
            boneIndex: Binding.BoneIndex,
            boneOffset: Binding.Offset,
            meshes: meshes);
    }

    internal void MarkSpriteDirty()
    {
        _sprite?.Dispose();
        _sprite = null;
    }

    public override void Import(string outputPath, PropertySet meta)
    {
        Binding.Resolve();
        UpdateBounds();

        using var writer = new BinaryWriter(File.Create(outputPath));
        writer.WriteAssetHeader(AssetType.Sprite, Sprite.Version, 0);
        writer.Write(FrameCount);
        writer.Write((ushort)(Atlas?.Index ?? 0));
        writer.Write((short)RasterBounds.Left);
        writer.Write((short)RasterBounds.Top);
        writer.Write((short)RasterBounds.Right);
        writer.Write((short)RasterBounds.Bottom);
        writer.Write((float)EditorApplication.Config.PixelsPerUnit);
        writer.Write((byte)(IsAntiAliased ? TextureFilter.Linear : TextureFilter.Point));
        writer.Write((short)Binding.BoneIndex);
        writer.Write(Binding.Offset.X);
        writer.Write(Binding.Offset.Y);
        writer.Write((byte)_layers.Count);

        for (int i=0; i<255; i++)
        {
            if (!_layers[i]) continue;
            var uv = _atlasUV[i];
            writer.Write(uv.Left);
            writer.Write(uv.Top);
            writer.Write(uv.Right);
            writer.Write(uv.Bottom);
            writer.Write((short)i);  // Must match Sprite.Load which reads Int16
        }
    }

    public override void OnUndoRedo()
    {
        UpdateBounds();
        AtlasManager.UpdateSprite(this);
        base.OnUndoRedo();
    }
}
