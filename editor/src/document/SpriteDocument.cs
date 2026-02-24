//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Globalization;
using System.Numerics;
using System.Runtime.InteropServices;

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

public class SpriteDocument : Document, ISpriteSource
{
    public override bool CanSave => true;

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

    public sealed class MeshSlot(byte layer, StringId bone, Color32 fillColor = default)
    {
        public readonly byte Layer = layer;
        public readonly StringId Bone = bone;
        public readonly Color32 FillColor = fillColor;
        public Color32 StrokeColor;
        public byte StrokeWidth;
        public bool HasStroke => StrokeColor.A > 0 && StrokeWidth > 0;
        public bool HasFill => FillColor.A > 0;
        public bool IsStrokeOnly => HasStroke && !HasFill;
        public readonly List<ushort> PathIndices = new();
    }

    private BitMask256 _layers = new();
    private readonly List<Rect> _atlasUV = new();
    private Sprite? _sprite;
    private static Shader? _textureSdfShader;

    public readonly SpriteFrame[] Frames = new SpriteFrame[Sprite.MaxFrames];
    public ushort FrameCount;
    public float Depth;
    public RectInt RasterBounds { get; private set; }

    public Color32 CurrentFillColor = Color32.White;
    public Color32 CurrentStrokeColor = new(0, 0, 0, 0);
    public byte CurrentStrokeWidth = 1;
    public byte CurrentLayer = 0;
    public StringId CurrentBone;
    public bool CurrentSubtract;

    public ref readonly BitMask256 Layers => ref _layers;

    public int MeshSlotCount
    {
        get
        {
            var slots = GetMeshSlots();
            var count = slots.Count;
            foreach (var slot in slots)
                if (slot.HasStroke && slot.HasFill) count++;
            return Math.Max(1, count);
        }
    }
    
    public bool ShowInSkeleton { get; set; }
    public bool ShowTiling { get; set; }
    public bool ShowSkeletonOverlay { get; set; }
    public Vector2Int? ConstrainedSize { get; set; }

    ushort ISpriteSource.FrameCount => FrameCount;
    AtlasDocument? ISpriteSource.Atlas { get => Atlas; set => Atlas = value; }
    internal AtlasDocument? Atlas { get; set; }

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
            Name = "Sprite",
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

    public int InsertFrame(int insertAt)
    {
        if (FrameCount >= Sprite.MaxFrames)
            return -1;

        FrameCount++;
        var copyFrame = Math.Max(0, insertAt - 1);

        for (var i = FrameCount - 1; i > insertAt; i--)
        {
            Frames[i].Shape.CopyFrom(Frames[i - 1].Shape);
            Frames[i].Hold = Frames[i - 1].Hold;
        }

        if (copyFrame >= 0 && copyFrame < FrameCount)
            Frames[insertAt].Shape.CopyFrom(Frames[copyFrame].Shape);

        Frames[insertAt].Hold = 0;
        return insertAt;
    }

    public int DeleteFrame(int frameIndex)
    {
        if (FrameCount <= 1)
            return frameIndex;

        for (var i = frameIndex; i < FrameCount - 1; i++)
        {
            Frames[i].Shape.CopyFrom(Frames[i + 1].Shape);
            Frames[i].Hold = Frames[i + 1].Hold;
        }

        Frames[FrameCount - 1].Shape.Clear();
        Frames[FrameCount - 1].Hold = 0;
        FrameCount--;
        return Math.Min(frameIndex, FrameCount - 1);
    }

    private static void NewFile(StreamWriter writer)
    {
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
        SpriteFrame? f = null;

        while (!tk.IsEOF)
        {
            if (tk.ExpectIdentifier("path"))
            {
                f ??= Frames[FrameCount++];
                ParsePath(f, ref tk);
            }
            else if (tk.ExpectIdentifier("palette"))
            {
                // Legacy: palette keyword is ignored, colors are stored directly
                tk.ExpectQuotedString();
            }
            else if (tk.ExpectIdentifier("frame"))
            {
                f = Frames[FrameCount++];
                if (tk.ExpectIdentifier("hold"))
                    f.Hold = tk.ExpectInt();
            }
            else if (tk.ExpectIdentifier("skeleton"))
            {
                Binding.SkeletonName = StringId.Get(tk.ExpectQuotedString());
            }
            else if (tk.ExpectIdentifier("antialias"))
            {
                // Legacy: silently consume
                tk.ExpectBool();
            }
            else if (tk.ExpectIdentifier("sdf"))
            {
                // Legacy: silently consume
                tk.ExpectBool();
            }
            else
            {
                tk.ExpectToken(out var badToken);
                Log.Error($"SpriteDocument.Load: Unexpected token '{tk.GetString(badToken)}'");
                break;
            }
        }

        if (FrameCount == 0)
            FrameCount = 1;
    }

    private void ParsePath(SpriteFrame f, ref Tokenizer tk)
    {
        var pathIndex = f.Shape.AddPath(Color32.White);
        var fillColor = Color32.White;
        var strokeColor = new Color32(0, 0, 0, 0);
        var strokeWidth = 1;
        var subtract = false;
        byte layer = 0;
        var bone = StringId.None;

        while (!tk.IsEOF)
        {
            if (tk.ExpectIdentifier("fill"))
            {
                // Support: rgba(r,g,b,a), #RRGGBB, #RRGGBBAA, or legacy int palette index
                if (tk.ExpectColor(out var color))
                {
                    fillColor = color.ToColor32();
                }
                else
                {
                    fillColor = PaletteManager.GetColor(0, tk.ExpectInt()).ToColor32();
                    // Legacy format had separate opacity float after the index
                    var legacyOpacity = tk.ExpectFloat(1.0f);
                    fillColor = fillColor.WithAlpha(legacyOpacity);
                }
            }
            else if (tk.ExpectIdentifier("stroke"))
            {
                if (tk.ExpectColor(out var color))
                {
                    strokeColor = color.ToColor32();
                }
                else
                {
                    strokeColor = PaletteManager.GetColor(0, tk.ExpectInt()).ToColor32();
                    var legacyOpacity = tk.ExpectFloat(0.0f);
                    strokeColor = strokeColor.WithAlpha(legacyOpacity);
                }
                strokeWidth = tk.ExpectInt(strokeWidth);
            }
            else if (tk.ExpectIdentifier("subtract"))
            {
                subtract = tk.ExpectBool();
            }
            else if (tk.ExpectIdentifier("layer"))
                layer = EditorApplication.Config.TryGetSpriteLayer(tk.ExpectQuotedString(), out var sg)
                    ? sg.Layer
                    : (byte)0;
            else if (tk.ExpectIdentifier("bone"))
            {
                var boneName = tk.ExpectQuotedString();
                if (!string.IsNullOrEmpty(boneName))
                    bone = StringId.Get(boneName);
            }
            else if (tk.ExpectIdentifier("anchor"))
                ParseAnchor(f.Shape, pathIndex, ref tk);
            else
                break;
        }

        f.Shape.SetPathFillColor(pathIndex, fillColor);
        f.Shape.SetPathStroke(pathIndex, strokeColor, (byte)strokeWidth);
        if (subtract)
            f.Shape.SetPathSubtract(pathIndex, true);
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
            writer.WriteLine($"fill {FormatColor(path.FillColor)}");

            if (path.StrokeColor.A > 0)
                writer.WriteLine($"stroke {FormatColor(path.StrokeColor)} {path.StrokeWidth}");

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

    private static string FormatColor(Color32 c)
    {
        if (c.A < 255)
            return $"rgba({c.R},{c.G},{c.B},{c.A / 255f:G})";
        return $"#{c.R:X2}{c.G:X2}{c.B:X2}";
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

    public void DrawSprite(in Vector2 offset = default, float alpha = 1.0f, int frame = 0)
    {
        if (Atlas == null) return;

        var sprite = Sprite;
        if (sprite == null) return;

        using (Graphics.PushState())
        {
            Graphics.SetTexture(Atlas.Texture);
            Graphics.SetShader(GetTextureSdfShader());
            Graphics.SetColor(Color.White.WithAlpha(alpha * Workspace.XrayAlpha));
            Graphics.SetTextureFilter(sprite.TextureFilter);

            var fi = sprite.FrameTable[frame];
            for (int i = fi.MeshStart; i < fi.MeshStart + fi.MeshCount; i++)
            {
                ref readonly var mesh = ref sprite.Meshes[i];

                Graphics.SetColor(mesh.FillColor.WithAlpha(mesh.FillColor.A * alpha * Workspace.XrayAlpha));

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

    public void DrawSprite(ReadOnlySpan<Matrix3x2> bindPose, ReadOnlySpan<Matrix3x2> animatedPose, in Matrix3x2 baseTransform, int frame = 0, Color? tint = null)
    {
        if (Atlas == null) return;

        var sprite = Sprite;
        if (sprite == null) return;

        using (Graphics.PushState())
        {
            Graphics.SetTexture(Atlas.Texture);
            Graphics.SetShader(GetTextureSdfShader());
            Graphics.SetColor(tint ?? Color.White);
            Graphics.SetTextureFilter(sprite.TextureFilter);

            var fi = sprite.FrameTable[frame];
            for (int i = fi.MeshStart; i < fi.MeshStart + fi.MeshCount; i++)
            {
                ref readonly var mesh = ref sprite.Meshes[i];

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

                Graphics.SetColor(mesh.FillColor);

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
        Depth = src.Depth;
        Bounds = src.Bounds;
        CurrentFillColor = src.CurrentFillColor;
        CurrentStrokeColor = src.CurrentStrokeColor;
        CurrentStrokeWidth = src.CurrentStrokeWidth;
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
        else
            meta.RemoveKey("sprite", "constrained_size");
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

    void ISpriteSource.ClearAtlasUVs() => ClearAtlasUVs();

    internal void ClearAtlasUVs()
    {
        _atlasUV.Clear();
        MarkSpriteDirty();
    }

    void ISpriteSource.Rasterize(PixelData<Color32> image, in AtlasSpriteRect rect, int padding)
    {
        var frameIndex = rect.FrameIndex;
        var dpi = EditorApplication.Config.PixelsPerUnit;

        var frame = GetFrame(frameIndex);
        var slots = GetMeshSlots(frameIndex);
        var slotBounds = GetMeshSlotBounds(frameIndex);
        var padding2 = padding * 2;
        var xOffset = 0;

        for (int slotIndex = 0; slotIndex < slots.Count; slotIndex++)
        {
            var slot = slots[slotIndex];
            var slotRasterBounds = slotBounds[slotIndex];
            if (slotRasterBounds.Width <= 0 || slotRasterBounds.Height <= 0)
                slotRasterBounds = RasterBounds;

            var slotWidth = slotRasterBounds.Size.X + padding2;

            AtlasManager.LogAtlas($"Rasterize: Name={rect.Name} Frame={frameIndex} Layer={slot.Layer} Bone={slot.Bone} Rect={rect.Rect} SlotBounds={slotRasterBounds}");

            var outerRect = new RectInt(
                rect.Rect.Position + new Vector2Int(xOffset, 0),
                new Vector2Int(slotWidth, slotRasterBounds.Size.Y + padding2));

            if (slot.PathIndices.Count > 0)
            {
                var sourceOffset = -slotRasterBounds.Position + new Vector2Int(padding, padding);

                if (slot.IsStrokeOnly)
                {
                    // Stroke-only: rasterize the stroke ring as a single MSDF
                    var msdfShape = Msdf.MsdfSprite.BuildShape(frame.Shape, CollectionsMarshal.AsSpan(slot.PathIndices));
                    if (msdfShape != null)
                    {
                        var paths = Msdf.ShapeClipper.ShapeToPaths(msdfShape, 8);
                        var halfStroke = slot.StrokeWidth * Shape.StrokeScale;
                        var contracted = Clipper2Lib.Clipper.InflatePaths(paths, -halfStroke,
                            Clipper2Lib.JoinType.Round, Clipper2Lib.EndType.Polygon, precision: 6);
                        var ringPaths = Clipper2Lib.Clipper.BooleanOp(Clipper2Lib.ClipType.Difference,
                            paths, contracted, Clipper2Lib.FillRule.NonZero, precision: 6);
                        var ringShape = ClipperPathsToMsdfShape(ringPaths);
                        if (ringShape != null)
                            Msdf.MsdfSprite.RasterizeMSDF(ringShape, image, outerRect, sourceOffset, dpi);
                    }
                }
                else
                {
                    // Rasterize full shape MSDF
                    frame.Shape.RasterizeMSDF(
                        image,
                        outerRect,
                        sourceOffset,
                        CollectionsMarshal.AsSpan(slot.PathIndices));

                    // For stroked+filled slots, rasterize a contracted shape as a second MSDF sub-rect
                    if (slot.HasStroke)
                    {
                        xOffset += slotWidth;
                        var outerRect2 = new RectInt(
                            rect.Rect.Position + new Vector2Int(xOffset, 0),
                            new Vector2Int(slotWidth, slotRasterBounds.Size.Y + padding2));

                        var halfStroke = slot.StrokeWidth * Shape.StrokeScale;
                        var msdfShape = Msdf.MsdfSprite.BuildShape(frame.Shape, CollectionsMarshal.AsSpan(slot.PathIndices));
                        if (msdfShape != null)
                        {
                            var paths = Msdf.ShapeClipper.ShapeToPaths(msdfShape, 8);
                            var contracted = Clipper2Lib.Clipper.InflatePaths(paths, -halfStroke,
                                Clipper2Lib.JoinType.Round, Clipper2Lib.EndType.Polygon, precision: 6);
                            var contractedShape = ClipperPathsToMsdfShape(contracted);
                            if (contractedShape != null)
                                Msdf.MsdfSprite.RasterizeMSDF(contractedShape, image, outerRect2, sourceOffset, dpi);
                        }
                    }
                }
            }

            xOffset += slotWidth;
        }
    }

    private static Msdf.Shape? ClipperPathsToMsdfShape(Clipper2Lib.PathsD paths)
    {
        var shape = new Msdf.Shape();
        foreach (var path in paths)
        {
            if (path.Count < 3) continue;
            var contour = shape.AddContour();
            for (int j = 0; j < path.Count; j++)
            {
                int next = (j + 1) % path.Count;
                contour.AddEdge(new Msdf.LinearSegment(
                    new Vector2Double(path[j].x, path[j].y),
                    new Vector2Double(path[next].x, path[next].y)));
            }
        }
        return shape.contours.Count > 0 ? shape : null;
    }

    void ISpriteSource.UpdateAtlasUVs(AtlasDocument atlas, ReadOnlySpan<AtlasSpriteRect> allRects, int padding)
    {
        ClearAtlasUVs();
        var padding2 = padding * 2;
        int uvIndex = 0;
        var ts = (float)EditorApplication.Config.AtlasSize;

        for (ushort frameIndex = 0; frameIndex < FrameCount; frameIndex++)
        {
            // Find the rect for this frame
            int rectIndex = -1;
            for (int i = 0; i < allRects.Length; i++)
            {
                if (allRects[i].Source == (ISpriteSource)this && allRects[i].FrameIndex == frameIndex)
                {
                    rectIndex = i;
                    break;
                }
            }
            if (rectIndex == -1) return;

            ref readonly var rect = ref allRects[rectIndex];
            var slots = GetMeshSlots(frameIndex);
            var slotBounds = GetMeshSlotBounds(frameIndex);
            var xOffset = 0;

            for (int slotIndex = 0; slotIndex < slots.Count; slotIndex++)
            {
                var bounds = slotBounds[slotIndex];
                var slotSize = (bounds.Width > 0 && bounds.Height > 0)
                    ? bounds.Size
                    : RasterBounds.Size;
                var slotWidth = slotSize.X + padding2;

                var u = (rect.Rect.Left + padding + xOffset) / ts;
                var v = (rect.Rect.Top + padding) / ts;
                var s = u + slotSize.X / ts;
                var t = v + slotSize.Y / ts;
                SetAtlasUV(uvIndex++, Rect.FromMinMax(u, v, s, t));
                xOffset += slotWidth;

                // Stroked+filled slots have a second sub-rect for the contracted fill
                if (slots[slotIndex].HasStroke && slots[slotIndex].HasFill)
                {
                    u = (rect.Rect.Left + padding + xOffset) / ts;
                    s = u + slotSize.X / ts;
                    SetAtlasUV(uvIndex++, Rect.FromMinMax(u, v, s, t));
                    xOffset += slotWidth;
                }
            }
        }
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
        if (Atlas == null || GetMeshSlots().Count == 0)
        {
            _sprite = null;
            return;
        }

        var dpi = EditorApplication.Config.PixelsPerUnit;
        var allMeshes = new List<SpriteMesh>();
        var frameTable = new SpriteFrameInfo[FrameCount];
        int uvIndex = 0;

        for (int frameIndex = 0; frameIndex < FrameCount; frameIndex++)
        {
            var frameSlots = GetMeshSlots((ushort)frameIndex);
            var frameSlotBounds = GetMeshSlotBounds((ushort)frameIndex);
            var meshStart = (ushort)allMeshes.Count;

            for (int slotIndex = 0; slotIndex < frameSlots.Count; slotIndex++)
            {
                var slot = frameSlots[slotIndex];
                var uv = GetAtlasUV(uvIndex++);
                if (uv == Rect.Zero)
                {
                    _sprite = null;
                    return;
                }

                var bounds = frameSlotBounds[slotIndex];
                if (bounds.Width <= 0 || bounds.Height <= 0)
                    bounds = RasterBounds;

                var boneIndex = (short)-1;
                if (Binding.IsBound && Binding.Skeleton != null)
                    boneIndex = slot.Bone.IsNone ? (short)0 : (short)Binding.Skeleton.FindBoneIndex(slot.Bone.ToString());

                var firstPath = Frames[frameIndex].Shape.GetPath(slot.PathIndices[0]);
                var fillColor = firstPath.FillColor.ToColor();

                if (slot.IsStrokeOnly)
                {
                    // Stroke-only: single mesh with stroke ring MSDF
                    allMeshes.Add(new SpriteMesh(
                        uv,
                        (short)slot.Layer,
                        boneIndex,
                        bounds.Position,
                        bounds.Size,
                        slot.StrokeColor.ToColor()));
                }
                else if (slot.HasStroke)
                {
                    var fillUV = GetAtlasUV(uvIndex++);

                    // Stroke mesh (drawn first, behind) — full shape MSDF
                    allMeshes.Add(new SpriteMesh(
                        uv,
                        (short)slot.Layer,
                        boneIndex,
                        bounds.Position,
                        bounds.Size,
                        slot.StrokeColor.ToColor()));

                    // Fill mesh (drawn second, on top) — contracted shape MSDF
                    allMeshes.Add(new SpriteMesh(
                        fillUV,
                        (short)slot.Layer,
                        boneIndex,
                        bounds.Position,
                        bounds.Size,
                        fillColor));
                }
                else
                {
                    allMeshes.Add(new SpriteMesh(
                        uv,
                        (short)slot.Layer,
                        boneIndex,
                        bounds.Position,
                        bounds.Size,
                        fillColor));
                }
            }

            frameTable[frameIndex] = new SpriteFrameInfo(meshStart, (ushort)(allMeshes.Count - meshStart));
        }

        _sprite = Sprite.Create(
            name: Name,
            bounds: RasterBounds,
            pixelsPerUnit: EditorApplication.Config.PixelsPerUnit,
            filter: TextureFilter.Linear,
            boneIndex: -1,
            meshes: allMeshes.ToArray(),
            frameTable: frameTable,
            frameRate: 12.0f,
            isSDF: true);
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

        var dpi = EditorApplication.Config.PixelsPerUnit;

        // Count total meshes across all frames (stroked+filled slots produce 2 meshes, stroke-only produces 1)
        ushort totalMeshes = 0;
        for (ushort fi = 0; fi < FrameCount; fi++)
        {
            var slots = GetMeshSlots(fi);
            foreach (var slot in slots)
                totalMeshes += (slot.HasStroke && slot.HasFill) ? (ushort)2 : (ushort)1;
        }

        using var writer = new BinaryWriter(File.Create(outputPath));
        writer.WriteAssetHeader(AssetType.Sprite, Sprite.Version, 0);
        writer.Write(FrameCount);
        writer.Write((ushort)(Atlas?.Index ?? 0));
        writer.Write((short)RasterBounds.Left);
        writer.Write((short)RasterBounds.Top);
        writer.Write((short)RasterBounds.Right);
        writer.Write((short)RasterBounds.Bottom);
        writer.Write((float)EditorApplication.Config.PixelsPerUnit);
        writer.Write((byte)TextureFilter.Linear);
        writer.Write((short)-1);  // Legacy bone index field (no longer used)
        writer.Write(totalMeshes);
        writer.Write(12.0f);  // Frame rate
        writer.Write((byte)1);  // SDF: always 1

        // Write all meshes per frame
        int uvIndex = 0;
        var meshStarts = new ushort[FrameCount];
        var meshCounts = new ushort[FrameCount];
        ushort meshOffset = 0;

        for (ushort frameIndex = 0; frameIndex < FrameCount; frameIndex++)
        {
            var frameSlots = GetMeshSlots(frameIndex);
            var frameSlotBounds = GetMeshSlotBounds(frameIndex);
            meshStarts[frameIndex] = meshOffset;
            ushort frameMeshCount = 0;

            for (int slotIndex = 0; slotIndex < frameSlots.Count; slotIndex++)
            {
                var slot = frameSlots[slotIndex];
                var uv = GetAtlasUV(uvIndex++);
                var bounds = frameSlotBounds[slotIndex];
                if (bounds.Width <= 0 || bounds.Height <= 0)
                    bounds = RasterBounds;

                var boneIndex = (short)-1;
                if (Binding.IsBound && Binding.Skeleton != null)
                    boneIndex = slot.Bone.IsNone ? (short)0 : (short)Binding.Skeleton.FindBoneIndex(slot.Bone.ToString());

                var firstPath = Frames[frameIndex].Shape.GetPath(slot.PathIndices[0]);

                if (slot.IsStrokeOnly)
                {
                    // Stroke-only: single mesh with stroke ring MSDF
                    WriteMesh(writer, uv, (short)slot.Layer, boneIndex, bounds, slot.StrokeColor);
                    frameMeshCount += 1;
                }
                else if (slot.HasStroke)
                {
                    var fillUV = GetAtlasUV(uvIndex++);

                    // Stroke mesh — full shape MSDF
                    WriteMesh(writer, uv, (short)slot.Layer, boneIndex, bounds, slot.StrokeColor);
                    // Fill mesh — contracted shape MSDF
                    WriteMesh(writer, fillUV, (short)slot.Layer, boneIndex, bounds, firstPath.FillColor);
                    frameMeshCount += 2;
                }
                else
                {
                    WriteMesh(writer, uv, (short)slot.Layer, boneIndex, bounds, firstPath.FillColor);
                    frameMeshCount += 1;
                }
            }

            meshCounts[frameIndex] = frameMeshCount;
            meshOffset += frameMeshCount;
        }

        // Write frame table
        for (int frameIndex = 0; frameIndex < FrameCount; frameIndex++)
        {
            writer.Write(meshStarts[frameIndex]);
            writer.Write(meshCounts[frameIndex]);
        }
    }

    private static void WriteMesh(BinaryWriter writer, Rect uv, short sortOrder, short boneIndex, RectInt bounds, Color32 fillColor)
    {
        writer.Write(uv.Left);
        writer.Write(uv.Top);
        writer.Write(uv.Right);
        writer.Write(uv.Bottom);
        writer.Write(sortOrder);
        writer.Write(boneIndex);
        writer.Write((short)bounds.X);
        writer.Write((short)bounds.Y);
        writer.Write((short)bounds.Width);
        writer.Write((short)bounds.Height);
        writer.Write(fillColor.R);
        writer.Write(fillColor.G);
        writer.Write(fillColor.B);
        writer.Write(fillColor.A);
    }

    public override void OnUndoRedo()
    {
        UpdateBounds();

        if (!IsEditing)
            AtlasManager.UpdateSource(this);

        base.OnUndoRedo();
    }

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
                var slotBounds = shape.GetRasterBoundsFor(slot.Layer, slot.Bone, slot.FillColor);
                if (slotBounds.Width <= 0 || slotBounds.Height <= 0)
                    continue;
                bounds = bounds.Width <= 0 ? slotBounds : RectInt.Union(bounds, slotBounds);
            }
            result.Add(bounds);
        }
        return result;
    }

    public List<RectInt> GetMeshSlotBounds(ushort frameIndex)
    {
        var slots = GetMeshSlots(frameIndex);
        var result = new List<RectInt>(slots.Count);
        var shape = Frames[frameIndex].Shape;
        foreach (var slot in slots)
            result.Add(shape.GetRasterBoundsFor(slot.Layer, slot.Bone, slot.FillColor));
        return result;
    }

    public Vector2Int GetFrameAtlasSize(ushort frameIndex)
    {
        var padding2 = EditorApplication.Config.AtlasPadding * 2;
        var slotBounds = GetMeshSlotBounds(frameIndex);

        if (slotBounds.Count == 0)
            return new(RasterBounds.Size.X + padding2, RasterBounds.Size.Y + padding2);

        var slots = GetMeshSlots(frameIndex);
        var totalWidth = 0;
        var maxHeight = 0;
        for (int i = 0; i < slotBounds.Count; i++)
        {
            var bounds = slotBounds[i];
            var slotWidth = (bounds.Width > 0 ? bounds.Size.X : RasterBounds.Size.X) + padding2;
            var slotHeight = (bounds.Height > 0 ? bounds.Size.Y : RasterBounds.Size.Y) + padding2;
            // Stroked+filled slots need two sub-rects (full shape + contracted fill)
            totalWidth += (slots[i].HasStroke && slots[i].HasFill) ? slotWidth * 2 : slotWidth;
            maxHeight = Math.Max(maxHeight, slotHeight);
        }
        return new(totalWidth, maxHeight);
    }

    internal static Shader GetTextureSdfShader() =>
        _textureSdfShader ??= Asset.Get<Shader>(AssetType.Shader, "texture_sdf")!;

    public List<MeshSlot> GetMeshSlots() => GetMeshSlots(0);

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

        // Build runs - new slot whenever layer, bone, or fill color changes.
        // When a subtract path is encountered, it is appended to all slots that
        // were created before it (subtracts carve holes in everything above them).
        MeshSlot? currentSlot = null;

        foreach (var pathIndex in sortedPaths)
        {
            ref readonly var path = ref shape.GetPath(pathIndex);

            if (path.IsSubtract)
            {
                // Apply this subtract to all existing slots
                foreach (var slot in slots)
                    slot.PathIndices.Add(pathIndex);
                continue;
            }

            if (currentSlot == null || path.Layer != currentSlot.Layer || path.Bone != currentSlot.Bone
                || path.FillColor != currentSlot.FillColor)
            {
                // Start new slot
                currentSlot = new MeshSlot(path.Layer, path.Bone, path.FillColor);
                currentSlot.StrokeColor = path.StrokeColor;
                currentSlot.StrokeWidth = path.StrokeWidth;
                slots.Add(currentSlot);
            }

            currentSlot.PathIndices.Add(pathIndex);
        }

        return slots;
    }
}
