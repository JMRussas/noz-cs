//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Diagnostics;
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
        public StringId SkeletonName;
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
            SkeletonName = StringId.Get(skeleton.Name);
        }

        public void Clear()
        {
            Skeleton = null;
            SkeletonName = StringId.None;
        }

        public void CopyFrom(SkeletonBinding src)
        {
            SkeletonName = src.SkeletonName;
            Skeleton = src.Skeleton;
        }

        public void Resolve()
        {
            Skeleton = DocumentManager.Find(AssetType.Skeleton, SkeletonName.ToString()) as SkeletonDocument;
        }
    }

    /// <summary>
    /// Represents a mesh slot - a run of paths with the same layer and bone.
    /// </summary>
    public sealed class MeshSlot(byte layer, StringId bone)
    {
        public readonly byte Layer = layer;
        public readonly StringId Bone = bone;
        public readonly List<ushort> PathIndices = new();
    }

    private BitMask256 _layers = new();
    private readonly List<Rect> _atlasUV = new();
    private Sprite? _sprite;

    public readonly SpriteFrame[] Frames = new SpriteFrame[Sprite.MaxFrames];
    public ushort FrameCount;
    public byte Palette;
    public float Depth;
    public RectInt RasterBounds { get; private set; }

    public byte CurrentFillColor = 0;
    public byte CurrentStrokeColor = 0;
    public float CurrentFillOpacity = 1.0f;
    public float CurrentStrokeOpacity = 0.0f;
    public byte CurrentLayer = 0;
    public StringId CurrentBone;

    public ref readonly BitMask256 Layers => ref _layers;

    /// <summary>
    /// Gets mesh slots for a specific frame. Each slot is a run of consecutive paths
    /// (after sorting by layer) with the same layer and bone.
    /// </summary>
    public List<MeshSlot> GetMeshSlots(ushort frameIndex)
    {
        var slots = new List<MeshSlot>();
        var shape = Frames[frameIndex].Shape;
        if (shape.PathCount == 0) return slots;

        // Get paths sorted by layer, then by index
        Span<ushort> sortedPaths = stackalloc ushort[shape.PathCount];
        for (ushort i = 0; i < shape.PathCount; i++)
            sortedPaths[i] = i;

        sortedPaths.Sort((a, b) =>
        {
            var layerA = shape.GetPath(a).Layer;
            var layerB = shape.GetPath(b).Layer;
            if (layerA != layerB) return layerA.CompareTo(layerB);
            return a.CompareTo(b);
        });

        // Build runs - new slot whenever layer or bone changes
        MeshSlot? currentSlot = null;

        foreach (var pathIndex in sortedPaths)
        {
            ref readonly var path = ref shape.GetPath(pathIndex);

            if (currentSlot == null || path.Layer != currentSlot.Layer || path.Bone != currentSlot.Bone)
            {
                // Start new slot
                currentSlot = new MeshSlot(path.Layer, path.Bone);
                slots.Add(currentSlot);
            }

            currentSlot.PathIndices.Add(pathIndex);
        }

        return slots;
    }

    /// <summary>
    /// Gets mesh slots for frame 0 (used for atlas layout).
    /// </summary>
    public List<MeshSlot> GetMeshSlots() => GetMeshSlots(0);

    public int MeshSlotCount => Math.Max(1, GetMeshSlots().Count);

    /// <summary>
    /// Computes tight bounds for each mesh slot, unioned across all frames.
    /// Returns a list aligned with GetMeshSlots() indices.
    /// </summary>
    public List<RectInt> GetMeshSlotBounds()
    {
        var slots = GetMeshSlots();
        var result = new List<RectInt>(slots.Count);

        foreach (var slot in slots)
        {
            var bounds = RectInt.Zero;
            for (ushort fi = 0; fi < FrameCount; fi++)
            {
                var shape = Frames[fi].Shape;
                var slotBounds = shape.GetRasterBoundsFor(slot.Layer, slot.Bone);
                if (slotBounds.Width <= 0 || slotBounds.Height <= 0)
                    continue;
                bounds = bounds.Width <= 0 ? slotBounds : RectInt.Union(bounds, slotBounds);
            }
            result.Add(bounds);
        }
        return result;
    }

    public Vector2Int AtlasSize
    {
        get
        {
            var padding2 = EditorApplication.Config.AtlasPadding * 2;
            var slotBounds = GetMeshSlotBounds();

            // If no slot bounds, fall back to full raster bounds
            if (slotBounds.Count == 0)
                return new((RasterBounds.Size.X + padding2) * FrameCount, RasterBounds.Size.Y + padding2);

            var totalWidth = 0;
            var maxHeight = 0;
            foreach (var bounds in slotBounds)
            {
                totalWidth += (bounds.Size.X + padding2) * FrameCount;
                maxHeight = Math.Max(maxHeight, bounds.Size.Y + padding2);
            }
            return new(totalWidth, maxHeight);
        }
    }
    public bool ShowInSkeleton { get; set; }
    public bool ShowTiling { get; set; }
    public bool ShowSkeletonOverlay { get; set; }
    public Vector2Int? ConstrainedSize { get; set; }

    internal AtlasDocument? Atlas;

    public readonly SkeletonBinding Binding = new();

    public Rect AtlasUV => GetAtlasUV(0);

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
        writer.WriteLine("antialias true");
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
            if (tk.ExpectIdentifier("path"))
            {
                ParsePath(f, ref tk);
            }
            else if (tk.ExpectIdentifier("palette"))
            {
                var paletteId = tk.ExpectQuotedString();
                Palette = PaletteManager.TryGetPalette(paletteId, out var paletteDef)
                    ? (byte)paletteDef.Row
                    : (byte)0;
            }
            else if (tk.ExpectIdentifier("frame"))
            {
                if (tk.ExpectIdentifier("hold"))
                    f.Hold = tk.ExpectInt();
                f = Frames[FrameCount++];
            }
            else if (tk.ExpectIdentifier("skeleton"))
            {
                Binding.SkeletonName = StringId.Get(tk.ExpectQuotedString());
            }
            else if (tk.ExpectIdentifier("antialias"))
            {
                IsAntiAliased = tk.ExpectBool();
            }
            else
            {
                Log.Error($"SpriteDocument.Load: Unexpected token '{tk.GetString()}'");
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
            if (tk.ExpectIdentifier("fill"))
            {
                fillColor = (byte)tk.ExpectInt();
                fillOpacity = MathEx.Clamp01(tk.ExpectFloat(fillOpacity));
            }
            else if (tk.ExpectIdentifier("stroke"))
            {
                strokeColor = (byte)tk.ExpectInt();
                strokeOpacity = MathEx.Clamp01(tk.ExpectFloat(strokeOpacity));
            }
            else if (tk.ExpectIdentifier("subtract"))
            {
                if (tk.ExpectBool())
                    fillOpacity = float.MinValue;
            }
            else if (tk.ExpectIdentifier("layer"))
                layer = EditorApplication.Config.TryGetSpriteLayer(tk.ExpectQuotedString(), out var sg)
                    ? sg.Layer
                    : (byte)0;
            else if (tk.ExpectIdentifier("bone"))
            {
                // Bone name - stored directly as Name
                var boneName = tk.ExpectQuotedString();
                if (!string.IsNullOrEmpty(boneName))
                    bone = StringId.Get(boneName);
            }
            else if (tk.ExpectIdentifier("anchor"))
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
        if (Binding.IsBound)
            writer.WriteLine($"skeleton \"{Binding.SkeletonName}\"");

        writer.WriteLine($"antialias {(IsAntiAliased ? "true" : "false")}");

        if (PaletteManager.TryGetPaletteByRow(Palette, out var paletteDef))
            writer.WriteLine($"palette \"{paletteDef.Id}\"");

        writer.WriteLine();

        for (ushort frameIndex = 0; frameIndex < FrameCount; frameIndex++)
        {
            var f = GetFrame(frameIndex);

            if (FrameCount > 1 || f.Hold > 0)
            {
                writer.WriteLine("frame");
                if (f.Hold > 0)
                    writer.WriteLine($"hold {f.Hold}");
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
            writer.WriteLine("path");
            if (path.IsSubtract)
                writer.WriteLine("subtract true");
            else
                writer.WriteLine($"fill {path.FillColor} {path.FillOpacity}");

            if (path.StrokeOpacity > float.Epsilon)
                writer.WriteLine($"stroke {path.StrokeColor} {path.StrokeOpacity}");

            if (EditorApplication.Config.TryGetSpriteLayer(path.Layer, out var layerDef))
                writer.WriteLine($"layer \"{layerDef.Id}\"");

            if (Binding.IsBound)
                writer.WriteLine($"bone \"{path.Bone}\"");

            for (ushort aIdx = 0; aIdx < path.AnchorCount; aIdx++)
            {
                ref readonly var anchor = ref shape.GetAnchor((ushort)(path.AnchorStart + aIdx));
                writer.Write(string.Format(CultureInfo.InvariantCulture, "anchor {0} {1}", anchor.Position.X, anchor.Position.Y));
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

            foreach (ref readonly var mesh in sprite.Meshes.AsSpan())
            {
                // Use per-mesh bounds if available, otherwise fall back to sprite bounds
                Rect bounds;
                if (mesh.Size.X > 0 && mesh.Size.Y > 0)
                {
                    bounds = new Rect(
                        mesh.Offset.X * Graphics.PixelsPerUnitInv,
                        mesh.Offset.Y * Graphics.PixelsPerUnitInv,
                        mesh.Size.X * Graphics.PixelsPerUnitInv,
                        mesh.Size.Y * Graphics.PixelsPerUnitInv).Translate(offset);
                }
                else
                {
                    bounds = RasterBounds.ToRect().Scale(Graphics.PixelsPerUnitInv).Translate(offset);
                }

                Graphics.Draw(bounds, mesh.UV, order: (ushort)mesh.SortOrder);
            }
        }
    }

    public void DrawSprite(ReadOnlySpan<Matrix3x2> bindPose, ReadOnlySpan<Matrix3x2> animatedPose, in Matrix3x2 baseTransform)
    {
        if (Atlas == null) return;

        var sprite = Sprite;
        if (sprite == null) return;

        using (Graphics.PushState())
        {
            Graphics.SetTexture(Atlas.Texture);
            Graphics.SetShader(EditorAssets.Shaders.Texture);
            Graphics.SetColor(Color.White);
            Graphics.SetTextureFilter(sprite.TextureFilter);

            foreach (ref readonly var mesh in sprite.Meshes.AsSpan())
            {
                // Use per-mesh bounds if available, otherwise fall back to sprite bounds
                Rect bounds;
                if (mesh.Size.X > 0 && mesh.Size.Y > 0)
                {
                    bounds = new Rect(
                        mesh.Offset.X * Graphics.PixelsPerUnitInv,
                        mesh.Offset.Y * Graphics.PixelsPerUnitInv,
                        mesh.Size.X * Graphics.PixelsPerUnitInv,
                        mesh.Size.Y * Graphics.PixelsPerUnitInv);
                }
                else
                {
                    bounds = RasterBounds.ToRect().Scale(Graphics.PixelsPerUnitInv);
                }

                var boneIndex = mesh.BoneIndex >= 0 ? mesh.BoneIndex : 0;
                var transform = bindPose[boneIndex] * animatedPose[boneIndex] * baseTransform;
                Graphics.SetTransform(transform);
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
        meta.ClearGroup("skeleton");  // Legacy cleanup - skeleton now in .sprite file
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

    internal void SetAtlasUV(int slotIndex, Rect uv)
    {
        while (_atlasUV.Count <= slotIndex)
            _atlasUV.Add(Rect.Zero);
        _atlasUV[slotIndex] = uv;
        MarkSpriteDirty();
    }

    internal Rect GetAtlasUV(int slotIndex) =>
        slotIndex < _atlasUV.Count ? _atlasUV[slotIndex] : Rect.Zero;

    private void UpdateSprite()
    {
        var slots = GetMeshSlots();
        if (Atlas == null || slots.Count == 0)
        {
            _sprite = null;
            return;
        }

        var slotBounds = GetMeshSlotBounds();
        var meshes = new SpriteMesh[slots.Count];
        for (int idx = 0; idx < slots.Count; idx++)
        {
            var slot = slots[idx];
            var uv = GetAtlasUV(idx);
            if (uv == Rect.Zero)
            {
                _sprite = null;
                return;
            }

            // Get per-slot bounds (list is aligned with slots by index)
            var bounds = slotBounds[idx];
            if (bounds.Width <= 0 || bounds.Height <= 0)
                bounds = RasterBounds;

            // Look up bone index by name (None = root bone 0, else find by name)
            var boneIndex = (short)-1;
            if (Binding.IsBound && Binding.Skeleton != null)
                boneIndex = slot.Bone.IsNone ? (short)0 : (short)Binding.Skeleton.FindBoneIndex(slot.Bone.ToString());

            meshes[idx] = new SpriteMesh(
                uv,
                (short)slot.Layer,
                boneIndex,
                bounds.Position,
                bounds.Size);
        }

        _sprite = Sprite.Create(
            name: Name,
            bounds: RasterBounds,
            pixelsPerUnit: EditorApplication.Config.PixelsPerUnit,
            filter: TextureFilter.Point,
            boneIndex: -1,
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
        var slotBounds = GetMeshSlotBounds();

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

        for (int idx = 0; idx < slots.Count; idx++)
        {
            var slot = slots[idx];
            var uv = GetAtlasUV(idx);
            writer.Write(uv.Left);
            writer.Write(uv.Top);
            writer.Write(uv.Right);
            writer.Write(uv.Bottom);
            writer.Write((short)slot.Layer);  // Sort order
            // Look up bone index by name (None = root bone 0, else find by name)
            var boneIndex = (short)-1;
            if (Binding.IsBound && Binding.Skeleton != null)
                boneIndex = slot.Bone.IsNone ? (short)0 : (short)Binding.Skeleton.FindBoneIndex(slot.Bone.ToString());
            writer.Write(boneIndex);

            // Write per-slot offset and size (list is aligned with slots by index)
            var bounds = slotBounds[idx];
            if (bounds.Width <= 0 || bounds.Height <= 0)
                bounds = RasterBounds;
            writer.Write((short)bounds.X);
            writer.Write((short)bounds.Y);
            writer.Write((short)bounds.Width);
            writer.Write((short)bounds.Height);
        }
    }

    public override void OnUndoRedo()
    {
        UpdateBounds();
        AtlasManager.UpdateSprite(this);
        base.OnUndoRedo();
    }
}
