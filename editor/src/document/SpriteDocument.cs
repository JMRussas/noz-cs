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
    public class SkeletonBinding
    {
        public string SkeletonName = "";
        public SkeletonDocument? Skeleton;

        public bool IsBound => Skeleton != null;
        public bool IsBoundTo(SkeletonDocument skeleton) => Skeleton == skeleton;

        public void Set(SkeletonDocument? skeleton)
        {
            if (skeleton == null)
            {
                Clear();
                return;
            }

            Skeleton = skeleton;
            SkeletonName = skeleton.Name;
        }

        public void Clear()
        {
            Skeleton = null;
            SkeletonName = "";
        }

        public void CopyFrom(SkeletonBinding src)
        {
            SkeletonName = src.SkeletonName;
            Skeleton = src.Skeleton;
        }

        public void Resolve()
        {
            Skeleton = DocumentManager.Find(AssetType.Skeleton, SkeletonName) as SkeletonDocument;
        }
    }

    private BitMask256 _layers = new();
    private readonly Dictionary<(byte layer, StringId bone), Rect> _atlasUV = new();
    private Sprite? _sprite;

    public readonly SpriteFrame[] Frames = new SpriteFrame[Sprite.MaxFrames];
    public ushort FrameCount;
    public byte Palette;
    public float Depth;
    public ushort Order;
    public RectInt RasterBounds { get; private set; }

    public byte CurrentFillColor = 0;
    public byte CurrentStrokeColor = 0;
    public float CurrentFillOpacity = 1.0f;
    public float CurrentStrokeOpacity = 0.0f;
    public byte CurrentLayer = 0;
    public StringId CurrentBone;

    public ref readonly BitMask256 Layers => ref _layers;

    /// <summary>
    /// Gets the unique (layer, bone) pairs used across all frames.
    /// Each pair represents a separate mesh slot in the atlas.
    /// </summary>
    public List<(byte layer, StringId bone)> GetMeshSlots()
    {
        var slots = new HashSet<(byte layer, StringId bone)>();
        for (ushort fi = 0; fi < FrameCount; fi++)
        {
            var shape = Frames[fi].Shape;
            for (ushort p = 0; p < shape.PathCount; p++)
            {
                ref readonly var path = ref shape.GetPath(p);
                slots.Add((path.Layer, path.Bone));
            }
        }
        // Sort for deterministic ordering
        var result = slots.ToList();
        result.Sort((a, b) =>
        {
            var layerCmp = a.layer.CompareTo(b.layer);
            return layerCmp != 0 ? layerCmp : a.bone.Value.CompareTo(b.bone.Value);
        });
        return result;
    }

    public int MeshSlotCount => Math.Max(1, GetMeshSlots().Count);

    public Vector2Int AtlasSize
    {
        get
        {
            var padding2 = EditorApplication.Config.AtlasPadding * 2;
            var slotCount = MeshSlotCount;
            return new((RasterBounds.Size.X + padding2) * FrameCount * slotCount, RasterBounds.Size.Y + padding2);
        }
    }
    public bool ShowInSkeleton { get; set; }
    public bool ShowTiling { get; set; }
    public bool ShowSkeletonOverlay { get; set; }
    public Vector2Int? ConstrainedSize { get; set; }

    internal AtlasDocument? Atlas;

    public readonly SkeletonBinding Binding = new();

    public Rect AtlasUV => GetAtlasUV(0, StringId.None);

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
        var oldBoneName = StringId.Get(oldName);
        var newBoneName = StringId.Get(newName);

        foreach (var doc in DocumentManager.Documents.OfType<SpriteDocument>())
        {
            if (doc.Binding.Skeleton != skeleton)
                continue;

            var modified = false;
            for (ushort fi = 0; fi < doc.FrameCount; fi++)
            {
                var shape = doc.Frames[fi].Shape;
                for (ushort p = 0; p < shape.PathCount; p++)
                {
                    if (shape.GetPath(p).Bone == oldBoneName)
                    {
                        shape.SetPathBone(p, newBoneName);
                        modified = true;
                    }
                }
            }

            if (modified)
                doc.MarkModified();
        }
    }

    private static void OnSkeletonBoneRemoved(SkeletonDocument skeleton, int removedIndex, string removedName)
    {
        var removedBoneName = StringId.Get(removedName);

        foreach (var doc in DocumentManager.Documents.OfType<SpriteDocument>())
        {
            if (doc.Binding.Skeleton != skeleton)
                continue;

            var modified = false;
            for (ushort fi = 0; fi < doc.FrameCount; fi++)
            {
                var shape = doc.Frames[fi].Shape;
                for (ushort p = 0; p < shape.PathCount; p++)
                {
                    if (shape.GetPath(p).Bone == removedBoneName)
                    {
                        // Reset to root bone (None)
                        shape.SetPathBone(p, StringId.None);
                        modified = true;
                    }
                }
            }

            if (modified)
            {
                doc.MarkModified();
                Notifications.Add($"Sprite '{doc.Name}' bone bindings updated (bone '{removedName}' deleted)");
            }
        }
    }

    public SpriteFrame GetFrame(ushort frameIndex) => Frames[frameIndex];

    private static void NewFile(StreamWriter writer)
    {
        writer.WriteLine("c DEFAULT a");
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
                // Support both old format (row number) and new format (palette ID)
                if (tk.ExpectInt(out var row))
                {
                    // Old format: row number directly
                    Palette = (byte)row;
                }
                else if (tk.ExpectToken(out var token) && token.Type == TokenType.Identifier)
                {
                    // New format: look up row from palette ID
                    var paletteId = tk.GetString(token);
                    Palette = PaletteManager.TryGetPalette(paletteId, out var paletteDef)
                        ? (byte)paletteDef.Row
                        : (byte)0;
                }
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

    private void ParsePath(SpriteFrame f, ref Tokenizer tk)
    {
        var pathIndex = f.Shape.AddPath();
        byte fillColor = 0;
        var fillOpacity = 1.0f;
        byte strokeColor = 0;
        var strokeOpacity = 0.0f;
        byte layer = 0;
        var bone = StringId.None;

        while (!tk.IsEOF)
        {
            if (tk.ExpectIdentifier("c"))
            {
                fillColor = (byte)tk.ExpectInt();
                fillOpacity = MathEx.Clamp01(tk.ExpectFloat(fillOpacity));
            }
            else if (tk.ExpectIdentifier("s"))
            {
                strokeColor = (byte)tk.ExpectInt();
                strokeOpacity = MathEx.Clamp01(tk.ExpectFloat(strokeOpacity));
            }
            else if (tk.ExpectIdentifier("o"))
                fillOpacity = MathEx.Clamp01(tk.ExpectFloat());
            else if (tk.ExpectIdentifier("h"))
                fillOpacity = float.MinValue;
            else if (tk.ExpectIdentifier("l"))
                layer = EditorApplication.Config.TryGetSpriteLayer(tk.ExpectQuotedString(), out var sg)
                    ? sg.Layer
                    : (byte)0;
            else if (tk.ExpectIdentifier("b"))
            {
                // Bone name - stored directly as Name
                var boneName = tk.ExpectQuotedString();
                if (!string.IsNullOrEmpty(boneName))
                    bone = StringId.Get(boneName);
            }
            else if (tk.ExpectIdentifier("a"))
                ParseAnchor(f.Shape, pathIndex, ref tk);
            else
                break;
        }

        f.Shape.SetPathFillColor(pathIndex, fillColor, fillOpacity);
        f.Shape.SetPathStrokeColor(pathIndex, strokeColor, strokeOpacity);
        f.Shape.SetPathLayer(pathIndex, layer);
        f.Shape.SetPathBone(pathIndex, bone);
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
        var paletteId = PaletteManager.TryGetPaletteByRow(Palette, out var paletteDef)
            ? paletteDef.Id
            : "DEFAULT";
        writer.Write($"c {paletteId}");

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
    
    private void SaveFrame(SpriteFrame f, StreamWriter writer)
    {
        var shape = f.Shape;

        for (ushort pIdx = 0; pIdx < shape.PathCount; pIdx++)
        {
            ref readonly var path = ref shape.GetPath(pIdx);
            var opacity = path.IsSubtract
                ? " h"
                : path.FillOpacity < 1
                    ? $" {path.FillOpacity}"
                    : "";

            var layer = EditorApplication.Config.TryGetSpriteLayer(path.Layer, out var layerDef)
                ? $" l \"{layerDef.Id}\""
                : "";

            var stroke = path.StrokeOpacity > float.Epsilon
                ? $" s {path.StrokeColor} {path.StrokeOpacity}"
                : "";

            // Bone: write bone name if not root (None = root/default)
            var bone = "";
            if (!path.Bone.IsNone)
                bone = $" b \"{path.Bone}\"";

            writer.WriteLine($"p c {path.FillColor}{opacity}{stroke}{layer}{bone}");

            for (ushort aIdx = 0; aIdx < path.AnchorCount; aIdx++)
            {
                ref readonly var anchor = ref shape.GetAnchor((ushort)(path.AnchorStart + aIdx));
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
        CurrentFillColor = src.CurrentFillColor;
        CurrentStrokeColor = src.CurrentStrokeColor;
        CurrentFillOpacity = src.CurrentFillOpacity;
        CurrentStrokeOpacity = src.CurrentStrokeOpacity;
        CurrentLayer = src.CurrentLayer;
        CurrentBone = src.CurrentBone;

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
        Binding.SkeletonName = meta.GetString("skeleton", "name", "");
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
            meta.SetString("skeleton", "name", Binding.SkeletonName);
        else
            meta.ClearGroup("skeleton");
        meta.ClearGroup("bone");  // Legacy cleanup
    }

    public override void PostLoad()
    {
        Binding.Resolve();
    }

    public void SetSkeletonBinding(SkeletonDocument? skeleton)
    {
        Binding.Set(skeleton);
        MarkSpriteDirty();
        MarkMetaModified();
    }

    public void ClearSkeletonBinding()
    {
        var skeleton = Binding.Skeleton;
        Binding.Clear();
        MarkSpriteDirty();
        skeleton?.UpdateSprites();
        MarkMetaModified();
    }

    internal void ClearAtlasUVs()
    {
        _atlasUV.Clear();
        MarkSpriteDirty();
    }

    internal void SetAtlasUV(byte layer, StringId bone, Rect uv)
    {
        _atlasUV[(layer, bone)] = uv;
        MarkSpriteDirty();
    }

    internal Rect GetAtlasUV(byte layer, StringId bone) =>
        _atlasUV.TryGetValue((layer, bone), out var uv) ? uv : Rect.Zero;

    private void UpdateSprite()
    {
        var slots = GetMeshSlots();
        if (Atlas == null || slots.Count == 0)
        {
            _sprite = null;
            return;
        }

        var meshes = new SpriteMesh[slots.Count];
        for (int idx = 0; idx < slots.Count; idx++)
        {
            var (layer, bone) = slots[idx];
            var uv = GetAtlasUV(layer, bone);
            if (uv == Rect.Zero)
            {
                _sprite = null;
                return;
            }
            // Look up bone index by name (None = root bone 0, else find by name)
            var boneIndex = (short)-1;
            if (Binding.IsBound && Binding.Skeleton != null)
                boneIndex = bone.IsNone ? (short)0 : (short)Binding.Skeleton.FindBoneIndex(bone.ToString());
            meshes[idx] = new SpriteMesh(uv, (short)layer, boneIndex);
        }

        _sprite = Sprite.Create(
            name: Name,
            bounds: RasterBounds,
            pixelsPerUnit: EditorApplication.Config.PixelsPerUnit,
            filter: TextureFilter.Point,
            boneIndex: -1,  // No longer used at sprite level
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

        var slots = GetMeshSlots();

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
        writer.Write((short)-1);  // Legacy bone index field (no longer used)
        writer.Write((byte)slots.Count);

        foreach (var (layer, bone) in slots)
        {
            var uv = GetAtlasUV(layer, bone);
            writer.Write(uv.Left);
            writer.Write(uv.Top);
            writer.Write(uv.Right);
            writer.Write(uv.Bottom);
            writer.Write((short)layer);  // Sort order
            // Look up bone index by name (None = root bone 0, else find by name)
            var boneIndex = (short)-1;
            if (Binding.IsBound && Binding.Skeleton != null)
                boneIndex = bone.IsNone ? (short)0 : (short)Binding.Skeleton.FindBoneIndex(bone.ToString());
            writer.Write(boneIndex);
        }
    }

    public override void OnUndoRedo()
    {
        UpdateBounds();
        AtlasManager.UpdateSprite(this);
        base.OnUndoRedo();
    }
}
