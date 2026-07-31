//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;
using SixLabors.ImageSharp.PixelFormats;

namespace NoZ.Editor;

internal readonly record struct SpriteExportPart(
    SpriteGroup? Group,
    string FileName,
    string AssetName);

internal readonly record struct SpriteAtlasPart(
    SpriteGroup? Group,
    bool IsBase);

public abstract partial class SpriteDocument : Document, ISkeletonAttachment
{
    public const string Extension = ".sprite";
    public const float DefaultFrameRate = 12f;

    protected static partial class WidgetIds
    {
        public static partial WidgetId PixelsPerUnit { get; }
        public static partial WidgetId FilterDropDown { get; }
        public static partial WidgetId SkeletonDropDown { get; }
        public static partial WidgetId BoneDropDown { get; }
        public static partial WidgetId ShowInSkeleton { get; }
        public static partial WidgetId ConstraintDropDown { get; }
        public static partial WidgetId SortOrder { get; }
        public static partial WidgetId AnimatedToggle { get; }
    }

    public string? BoneName;
    private string? _sortOrderId;
    private Sprite? _sprite;
    private readonly Dictionary<SpriteAtlasPart, Sprite> _partSprites = [];
    private Texture? _standaloneTexture;
    public float Depth;

    public override bool CanSave => true;

    public byte SortOrder { get; private set; }

    public string? SortOrderId
    {
        get => _sortOrderId;
        set { _sortOrderId = value; ResolveSortOrder(); }
    }

    public SpriteGroup Root { get; } = new() { Name = "Root" };

    private RectInt _rasterBounds;
    private Rect _bounds = new(-0.5f, -0.5f, 1f, 1f);
    private bool _boundsDirty;

    public RectInt RasterBounds
    {
        get
        {
            if (_boundsDirty) EnsureBoundsFresh();
            return _rasterBounds;
        }
        protected set => _rasterBounds = value;
    }

    public override Rect Bounds
    {
        get
        {
            if (_boundsDirty) EnsureBoundsFresh();
            return _bounds;
        }
        set => _bounds = value;
    }

    public void InvalidateBounds() => _boundsDirty = true;

    private void EnsureBoundsFresh()
    {
        _boundsDirty = false;
        UpdateContentBounds();
        ClampToMaxSpriteSize();
    }
    public EdgeInsets Edges { get; set; } = EdgeInsets.Zero;

    public int? PixelsPerUnitOverride { get; set; }
    public TextureFilter? TextureFilterOverride { get; set; }

    protected virtual TextureFilter DefaultTextureFilter => TextureFilter.Linear;

    public virtual int PixelsPerUnit => PixelsPerUnitOverride ?? EditorApplication.Config.PixelsPerUnit;
    public virtual TextureFilter TextureFilter => TextureFilterOverride ?? DefaultTextureFilter;

    public bool IsAnimated { get; set; }

    public int FrameCount => IsAnimated ? Root.Children.Count : 0;

    public bool ShowInSkeleton { get; set; }
    public bool ShowTiling { get; set; }
    public bool TileX { get; set; } = true;
    public bool TileY { get; set; } = true;
    public bool ShowSkeletonOverlay { get; set; }
    public Vector2Int? ConstrainedSize { get; set; }

    public ushort AtlasFrameCount => (ushort)TotalTimeSlots;

    public DocumentRef<SkeletonDocument> Skeleton;
    public int BoneIndex { get; private set; } = -1;

    public int TotalTimeSlots
    {
        get
        {
            if (!IsAnimated || Root.Children.Count == 0) return 1;
            var total = 0;
            foreach (var child in Root.Children)
                total += 1 + child.Hold;
            return total;
        }
    }

    internal IReadOnlyList<SpriteExportPart> GetExportParts()
    {
        var result = new List<SpriteExportPart>();
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        usedNames.Add(Name);
        CollectExportParts(Root, result, usedNames);

        if (result.Count > 0)
            result.Insert(0, new SpriteExportPart(null, Name, $"{Name}/{Name}"));

        return result;
    }

    internal bool IsMultiSprite
    {
        get
        {
            var isMultiSprite = false;
            Root.ForEach(group =>
            {
                if (group != Root && group.IsSprite)
                    isMultiSprite = true;
            });
            return isMultiSprite;
        }
    }

    private void CollectExportParts(
        SpriteNode parent,
        List<SpriteExportPart> result,
        HashSet<string> usedNames)
    {
        foreach (var child in parent.Children)
        {
            if (child is not SpriteGroup group)
                continue;

            if (group.IsSprite)
            {
                var baseName = Project.MakeCanonicalName(group.Name);
                if (string.IsNullOrEmpty(baseName))
                    baseName = "sprite";

                var fileName = baseName;
                for (var suffix = 2; !usedNames.Add(fileName); suffix++)
                    fileName = $"{baseName}_{suffix}";

                result.Add(new SpriteExportPart(group, fileName, $"{Name}/{fileName}"));
            }

            CollectExportParts(group, result, usedNames);
        }
    }

    internal IEnumerable<SpriteAtlasPart> GetAtlasParts()
    {
        // Keep the composite entry in the editor atlas so the document still previews
        // as a whole. Multi-sprite metadata only exposes the assigned group entries.
        yield return new SpriteAtlasPart(null, false);
        foreach (var part in GetExportParts())
            yield return new SpriteAtlasPart(part.Group, part.Group == null);
    }

    internal string GetAtlasAssetName(SpriteGroup? group, bool isBase)
    {
        if (group == null && !isBase)
            return Name;

        foreach (var part in GetExportParts())
            if (part.Group == group)
                return part.AssetName;

        return Name;
    }

    public Sprite? Sprite
    {
        get
        {
            if (_sprite == null) UpdateSprite();
            return _sprite;
        }
    }

    protected abstract void UpdateContentBounds();

    public override Color32 GetPixelAt(Vector2 worldPos) => default;
    internal abstract void RasterizeCore(PixelData<Color32> image, in AtlasSpriteRect rect, int padding);
    protected virtual void SaveContent(StreamWriter writer) { }
    protected abstract void CloneContent(SpriteDocument source);

    public abstract DocumentEditor CreateEditor();

    public int GetFrameAtTimeSlot(int timeSlot)
    {
        if (!IsAnimated || Root.Children.Count == 0) return 0;
        var accumulated = 0;
        for (var i = 0; i < Root.Children.Count; i++)
        {
            var slots = 1 + Root.Children[i].Hold;
            if (timeSlot < accumulated + slots)
                return i;
            accumulated += slots;
        }
        return Root.Children.Count - 1;
    }

    public void EnableAnimation()
    {
        if (IsAnimated) return;
        Undo.Record(this);
        IsAnimated = true;
    }

    public void DisableAnimation()
    {
        if (!IsAnimated) return;
        Undo.Record(this);
        IsAnimated = false;
    }

    public bool ShouldShowInSkeleton(SkeletonDocument skeleton) => ShowInSkeleton && Skeleton.Value == skeleton;

    public void DrawSkinned(
        ReadOnlySpan<Matrix3x2> bindPose,
        ReadOnlySpan<Matrix3x2> animatedPose,
        in Matrix3x2 baseTransform)
    {
        DrawSprite(bindPose, animatedPose, baseTransform);
    }

    public static void RegisterDef()
    {
        DocumentDef<SpriteDocument>.Register(new DocumentDef
        {
            Type = AssetType.Sprite,
            Name = "Sprite",
            Extensions = [Extension],
            Factory = CreateFromFile,
            EditorFactory = doc => ((SpriteDocument)doc)!.CreateEditor(),
            Icon = () => EditorAssets.Sprites.AssetIconSprite
        });

        DocumentDef<ImageSpriteDocument>.Register(new DocumentDef
        {
            Type = AssetType.Sprite,
            Name = "Image",
            Extensions = [".png", ".jpg", ".jpeg", ".tga", ".webp", ".bmp"],
            Factory = _ => new ImageSpriteDocument(),
            Icon = () => EditorAssets.Sprites.AssetIconSprite
        });

        DocumentDef<PixelDocument>.Register(new DocumentDef
        {
            Type = AssetType.Sprite,
            Name = "PixelSprite",
            Extensions = [PixelDocument.BinaryExtension],
            Factory = _ => new PixelDocument(),
            EditorFactory = doc => ((SpriteDocument)doc)!.CreateEditor(),
            Icon = () => EditorAssets.Sprites.AssetIconSprite
        });
    }

    private static SpriteDocument CreateFromFile(string? path)
    {
        if (path == null || !File.Exists(path))
            return new VectorSpriteDocument();

        var content = File.ReadAllText(path);
        foreach (var line in content.AsSpan().EnumerateLines())
        {
            var trimmed = line.Trim();
            if (!trimmed.StartsWith("type "))
                continue;

            return trimmed[5..].Trim().ToString() switch
            {
                "generated" => new GeneratedSpriteDocument(),
                _ => new VectorSpriteDocument(),
            };
        }

        return new VectorSpriteDocument();
    }

    public override void Load()
    {
        var contents = File.ReadAllText(Path);
        var tk = new Tokenizer(contents);
        Load(ref tk);
        UpdateBounds();
        Loaded = true;
    }

    public override void Reload()
    {
        Edges = EdgeInsets.Zero;
        Skeleton.Clear();
        BoneName = null;
        IsAnimated = false;
        Root.Clear();
        var contents = File.ReadAllText(Path);
        var tk = new Tokenizer(contents);
        Load(ref tk);

        Skeleton.Resolve();
        ResolveSortOrder();
        ResolveBone();
        UpdateBounds();
    }

    public void ResolveBone()
    {
        BoneIndex = -1;
        if (BoneName != null && Skeleton.Value is { } skel)
            BoneIndex = skel.FindBoneIndex(BoneName);
    }

    protected void ResolveSortOrder()
    {
        SortOrder = 0;
        if (EditorApplication.Config != null && EditorApplication.Config.TryGetSortOrder(_sortOrderId, out var def))
            SortOrder = def.SortOrder;
    }

    public void UpdateBounds()
    {
        _boundsDirty = false;
        UpdateContentBounds();
        ClampToMaxSpriteSize();
        MarkSpriteDirty();
    }

    protected void SetDefaultBounds()
    {
        if (ConstrainedSize.HasValue)
        {
            var cs = ConstrainedSize.Value;
            RasterBounds = new RectInt(-cs.X / 2, -cs.Y / 2, cs.X, cs.Y);
            var ppu = 1.0f / PixelsPerUnit;
            Bounds = new Rect(
                RasterBounds.X * ppu,
                RasterBounds.Y * ppu,
                RasterBounds.Width * ppu,
                RasterBounds.Height * ppu);
        }
        else
        {
            Bounds = new Rect(-0.5f, -0.5f, 1f, 1f);
        }
    }

    protected static int SnapFloor(float v)
    {
        var r = MathF.Round(v);
        return (int)(MathF.Abs(v - r) < 0.01f ? r : MathF.Floor(v));
    }

    protected static int SnapCeil(float v)
    {
        var r = MathF.Round(v);
        return (int)(MathF.Abs(v - r) < 0.01f ? r : MathF.Ceiling(v));
    }

    private void ClampToMaxSpriteSize()
    {
        var maxSize = EditorApplication.Config.AtlasSize - EditorApplication.Config.AtlasPadding * 2;
        var width = RasterBounds.Width;
        var height = RasterBounds.Height;

        if (width <= maxSize && height <= maxSize)
            return;

        var clampedWidth = Math.Min(width, maxSize);
        var clampedHeight = Math.Min(height, maxSize);

        RasterBounds = new RectInt(
            -clampedWidth / 2,
            -clampedHeight / 2,
            clampedWidth,
            clampedHeight);
    }

    public override void Draw()
    {
        DrawOrigin();

        if (Sprite != null)
            DrawSprite();
        else
            DrawBounds();
    }

    public override bool DrawThumbnail()
    {
        if (Sprite != null)
        {
            UI.Image(Sprite, ImageStyle.Center);
            return true;
        }

        return false;
    }

    protected static Texture CreateTextureFromImage(SixLabors.ImageSharp.Image<Rgba32> image, string name)
    {
        var w = image.Width;
        var h = image.Height;
        var data = new byte[w * h * 4];
        image.CopyPixelDataTo(data);
        return Texture.Create(w, h, data, TextureFormat.RGBA8, TextureFilter.Linear, name);
    }

    protected void DrawTexturedRect(Texture texture, Rect bounds, Color color, Rect? uv = null)
    {
        using (Graphics.PushState())
        {
            Graphics.SetTransform(Transform);
            Graphics.SetTexture(texture);
            Graphics.SetShader(EditorAssets.Shaders.Texture);
            Graphics.SetTextureFilter(TextureFilter);
            Graphics.SetColor(color);
            if (uv.HasValue)
                Graphics.Draw(bounds, uv.Value, order: SortOrder);
            else
                Graphics.Draw(bounds, order: SortOrder);
        }
    }

    public void DrawSprite(in Vector2 offset = default, float alpha = 1.0f, int frame = 0)
    {
        var sprite = Sprite;
        if (sprite == null) return;

        using (Graphics.PushState())
        {
            Graphics.SetTextureFilter(TextureFilter);
            Graphics.SetShader(EditorAssets.Shaders.Sprite);
            Graphics.SetColor(Color.White.WithAlpha(alpha * Workspace.XrayAlpha));
            if (offset != default)
                Graphics.SetTransform(Matrix3x2.CreateTranslation(offset) * Graphics.Transform);
            Graphics.Draw(sprite, SortOrder, frame: frame);
        }
    }

    public void DrawSprite(ReadOnlySpan<Matrix3x2> bindPose, ReadOnlySpan<Matrix3x2> animatedPose, in Matrix3x2 baseTransform, int frame = 0, Color? tint = null)
    {
        if (IsMultiSprite)
        {
            foreach (var part in GetExportParts())
            {
                var atlasPart = new SpriteAtlasPart(part.Group, part.Group == null);
                var spritePart = GetPartSprite(atlasPart, part.AssetName);
                if (spritePart == null)
                    continue;

                var boneIndex = part.Group != null ? ResolveGroupBone(part.Group) : BoneIndex;
                var sortOrder = part.Group != null ? ResolveGroupSortOrder(part.Group) : SortOrder;
                DrawSkinnedSprite(
                    spritePart,
                    boneIndex,
                    sortOrder,
                    bindPose,
                    animatedPose,
                    baseTransform,
                    frame,
                    tint);
            }
            return;
        }

        var sprite = Sprite;
        if (sprite == null)
        {
            Log.Info($"[SKEL DEBUG] '{Name}': Sprite is null");
            return;
        }

        DrawSkinnedSprite(
            sprite,
            BoneIndex,
            SortOrder,
            bindPose,
            animatedPose,
            baseTransform,
            frame,
            tint);
    }

    private void DrawSkinnedSprite(
        Sprite sprite,
        int boneIndex,
        byte sortOrder,
        ReadOnlySpan<Matrix3x2> bindPose,
        ReadOnlySpan<Matrix3x2> animatedPose,
        in Matrix3x2 baseTransform,
        int frame,
        Color? tint)
    {
        using var _ = Graphics.PushState();
        Graphics.SetShader(EditorAssets.Shaders.Sprite);
        Graphics.SetColor(tint ?? Color.White);

        var transform = boneIndex >= 0 &&
                        boneIndex < bindPose.Length &&
                        boneIndex < animatedPose.Length
            ? bindPose[boneIndex] * animatedPose[boneIndex] * baseTransform
            : baseTransform;
        Graphics.SetTextureFilter(TextureFilter);
        Graphics.SetTransform(transform);
        Graphics.Draw(sprite, sortOrder, frame: frame);
    }

    public override void Clone(Document source)
    {
        var src = (SpriteDocument)source;
        Depth = src.Depth;
        Bounds = src.Bounds;
        RasterBounds = src.RasterBounds;
        Edges = src.Edges;
        Skeleton = src.Skeleton;
        BoneName = src.BoneName;
        IsAnimated = src.IsAnimated;
        PixelsPerUnitOverride = src.PixelsPerUnitOverride;
        TextureFilterOverride = src.TextureFilterOverride;

        Root.Dispose();
        Root.Clear();
        foreach (var child in src.Root.Children)
            Root.Add(child.Clone());

        CloneContent(src);
    }

    public override void LoadMetadata(PropertySet meta)
    {
        ShowInSkeleton = meta.GetBool("sprite", "show_in_skeleton", false);
        ShowTiling = meta.GetBool("sprite", "show_tiling", false);
        TileX = meta.GetBool("sprite", "tile_x", true);
        TileY = meta.GetBool("sprite", "tile_y", true);
        ShowSkeletonOverlay = meta.GetBool("sprite", "show_skeleton_overlay", false);
        ConstrainedSize = ParseConstrainedSize(meta.GetString("sprite", "constrained_size", ""));

        var ppu = meta.GetInt("sprite", "ppu", 0);
        PixelsPerUnitOverride = ppu > 0 ? ppu : null;

        var filter = meta.GetInt("sprite", "filter", -1);
        TextureFilterOverride = filter >= 0 ? (TextureFilter)filter : null;

        LoadContentMetadata(meta);

        if (Loaded)
            UpdateBounds();
    }

    protected virtual void LoadContentMetadata(PropertySet meta) { }

    protected static Vector2Int? ParseConstrainedSize(string value)
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
        meta.SetBool("sprite", "tile_x", TileX);
        meta.SetBool("sprite", "tile_y", TileY);
        meta.SetBool("sprite", "show_skeleton_overlay", ShowSkeletonOverlay);

        if (ConstrainedSize.HasValue)
            meta.SetString("sprite", "constrained_size", $"{ConstrainedSize.Value.X}x{ConstrainedSize.Value.Y}");
        else
            meta.RemoveKey("sprite", "constrained_size");

        if (PixelsPerUnitOverride.HasValue)
            meta.SetInt("sprite", "ppu", PixelsPerUnitOverride.Value);
        else
            meta.RemoveKey("sprite", "ppu");

        if (TextureFilterOverride.HasValue)
            meta.SetInt("sprite", "filter", (int)TextureFilterOverride.Value);
        else
            meta.RemoveKey("sprite", "filter");

        SaveContentMetadata(meta);
    }

    protected virtual void SaveContentMetadata(PropertySet meta) { }

    public override void InspectorUI()
    {
        if (Inspector.IsSectionCollapsed)
            return;

        using (Inspector.BeginProperty("Pixels Per Unit"))
        {
            var current = PixelsPerUnitOverride ?? EditorApplication.Config.PixelsPerUnit;
            var label = PixelsPerUnitOverride.HasValue
                ? $"{current}"
                : $"{current} (Default)";
            UI.DropDown(WidgetIds.PixelsPerUnit, () =>
            [
                ..new[] { 8, 16, 32, 64, 128, 256 }.Select(v => new PopupMenuItem
                {
                    Label = v == EditorApplication.Config.PixelsPerUnit ? $"{v} (Default)" : $"{v}",
                    Handler = () =>
                    {
                        Undo.Record(this);
                        PixelsPerUnitOverride = v == EditorApplication.Config.PixelsPerUnit ? null : v;
                        UpdateBounds();
                        AtlasManager.UpdateSource(this);
                        AssetManifest.IsModified = true;
                    }
                })
            ], label);
        }

        using (Inspector.BeginProperty("Filter"))
        {
            var current = TextureFilterOverride ?? DefaultTextureFilter;
            var filterLabel = TextureFilterOverride.HasValue
                ? $"{current}"
                : $"{current} (Default)";
            UI.DropDown(WidgetIds.FilterDropDown, () =>
            [
                new PopupMenuItem
                {
                    Label = DefaultTextureFilter == TextureFilter.Point ? "Point (Default)" : "Point",
                    Handler = () =>
                    {
                        Undo.Record(this);
                        TextureFilterOverride = DefaultTextureFilter == TextureFilter.Point ? null : TextureFilter.Point;
                        MarkSpriteDirty();
                        AssetManifest.IsModified = true;
                    }
                },
                new PopupMenuItem
                {
                    Label = DefaultTextureFilter == TextureFilter.Linear ? "Linear (Default)" : "Linear",
                    Handler = () =>
                    {
                        Undo.Record(this);
                        TextureFilterOverride = DefaultTextureFilter == TextureFilter.Linear ? null : TextureFilter.Linear;
                        MarkSpriteDirty();
                        AssetManifest.IsModified = true;
                    }
                }
            ], filterLabel);
        }

        using (Inspector.BeginProperty("Size"))
        {
            var sizes = EditorApplication.Config.SpriteSizes;
            var constraintLabel = "Auto";
            if (ConstrainedSize.HasValue)
                for (int i = 0; i < sizes.Length; i++)
                    if (ConstrainedSize.Value == sizes[i].Size)
                    {
                        constraintLabel = sizes[i].Label;
                        break;
                    }

            UI.DropDown(WidgetIds.ConstraintDropDown, () =>
            [
                ..EditorApplication.Config.SpriteSizes.Select(s =>
                    new PopupMenuItem { Label = s.Label, Handler = () =>
                    {
                        Undo.Record(this);
                        ConstrainedSize = s.Size;
                        UpdateBounds();
                        AtlasManager.UpdateSource(this);
                        AssetManifest.IsModified = true;
                    }}),
                new PopupMenuItem { Label = "Auto", Handler = () =>
                {
                    Undo.Record(this);
                    ConstrainedSize = null;
                    UpdateBounds();
                    AtlasManager.UpdateSource(this);
                    AssetManifest.IsModified = true;
                }}
            ], constraintLabel, EditorAssets.Sprites.IconConstraint);
        }

        using (Inspector.BeginProperty("Sort Order"))
        {
            EditorUI.SortOrderDropDown(WidgetIds.SortOrder, SortOrderId, id =>
            {
                Undo.Record(this);
                SortOrderId = id;
                AssetManifest.IsModified = true;
            });
        }

        using (Inspector.BeginProperty("Skeleton"))
        {
            var skeleton = EditorUI.SkeletonField(WidgetIds.SkeletonDropDown, Skeleton.Value);
            if (UI.WasChanged())
            {
                Undo.Record(this);
                if (skeleton != null)
                {
                    Skeleton = skeleton;
                    skeleton.UpdateSprites();
                }
                else
                {
                    var old = Skeleton.Value;
                    Skeleton.Clear();
                    BoneName = null;
                    old?.UpdateSprites();
                }
            }
        }

        if (Skeleton.IsResolved)
        {
            using (Inspector.BeginProperty("Bone"))
            {
                var skeleton = Skeleton.Value!;
                var boneLabel = BoneName ?? "None";

                UI.DropDown(WidgetIds.BoneDropDown, () =>
                {
                    var boneItems = new List<PopupMenuItem>();
                    for (var i = 0; i < skeleton.BoneCount; i++)
                    {
                        var boneName = skeleton.Bones[i].Name;
                        boneItems.Add(new PopupMenuItem
                        {
                            Label = boneName,
                            Handler = () =>
                            {
                                Undo.Record(this);
                                BoneName = boneName;
                                ResolveBone();
                                Skeleton.Value?.UpdateSprites();
                            }
                        });
                    }
                    boneItems.Add(new PopupMenuItem
                    {
                        Label = "None",
                        Handler = () =>
                        {
                            Undo.Record(this);
                            BoneName = null;
                            ResolveBone();
                            Skeleton.Value?.UpdateSprites();
                        }
                    });
                    return [.. boneItems];
                }, boneLabel, EditorAssets.Sprites.IconBone);
            }

            using (Inspector.BeginProperty("Show In Skeleton"))
            {
                var showInSkeleton = UI.Toggle(WidgetIds.ShowInSkeleton, ShowInSkeleton, EditorStyle.Inspector.Toggle);
                if (UI.WasChanged())
                {
                    Undo.Record(this);
                    ShowInSkeleton = showInSkeleton;
                    Skeleton.Value?.UpdateSprites();
                }
            }
        }
    }

    public override void PostLoad()
    {
        Skeleton.Resolve();
        ResolveSortOrder();
        ResolveBone();
    }

    private void UpdateSprite()
    {
        if (!AtlasManager.TryGetEntry(this, out var rects, out var layer))
        {
            _sprite = null;
            return;
        }

        _sprite = CreateEditorSprite(Name, rects, layer);
    }

    private Sprite? GetPartSprite(SpriteAtlasPart atlasPart, string assetName)
    {
        if (_partSprites.TryGetValue(atlasPart, out var sprite))
            return sprite;

        if (!AtlasManager.TryGetEntry(this, atlasPart, out var rects, out var layer))
            return null;

        sprite = CreateEditorSprite(assetName, rects, layer);
        _partSprites.Add(atlasPart, sprite);
        return sprite;
    }

    private Sprite CreateEditorSprite(string name, AtlasSpriteRect[] rects, ushort layer)
    {
        var atlasAsset = ShouldExport ? AtlasManager.GameAtlas : AtlasManager.EditorAtlas;
        var atlasSize = (float)EditorApplication.Config.AtlasSize;
        var padding = EditorApplication.Config.AtlasPadding;

        var totalSlots = TotalTimeSlots;
        var frames = new NoZ.SpriteFrame[totalSlots];
        for (int frameIndex = 0; frameIndex < totalSlots; frameIndex++)
        {
            var rect = frameIndex < rects.Length ? rects[frameIndex].Rect : default;
            var u = (rect.Left + padding) / atlasSize;
            var v = (rect.Top + padding) / atlasSize;
            var s = u + RasterBounds.Size.X / atlasSize;
            var t = v + RasterBounds.Size.Y / atlasSize;
            frames[frameIndex] = new NoZ.SpriteFrame(
                Rect.FromMinMax(u, v, s, t),
                RasterBounds.Position,
                RasterBounds.Size);
        }

        var ppu = PixelsPerUnit;
        var pixelEdges = new EdgeInsets(
            MathF.Round(Edges.T * ppu),
            MathF.Round(Edges.L * ppu),
            MathF.Round(Edges.B * ppu),
            MathF.Round(Edges.R * ppu));

        return Sprite.Create(
            name: name,
            bounds: RasterBounds,
            pixelsPerUnit: ppu,
            boneIndex: -1,
            frames: frames,
            frameRate: 12.0f,
            edges: pixelEdges,
            sliceMask: Sprite.CalculateSliceMask(RasterBounds, pixelEdges),
            atlasIndex: layer,
            atlas: atlasAsset,
            filter: TextureFilter);
    }

    internal void MarkSpriteDirty()
    {
        _sprite?.Dispose();
        _sprite = null;
        foreach (var sprite in _partSprites.Values)
            sprite.Dispose();
        _partSprites.Clear();
        _standaloneTexture?.Dispose();
        _standaloneTexture = null;
    }

    public override void OnRenamed(Document doc, string oldName, string newName)
    {
        if (doc is not SkeletonDocument) return;
        if (Skeleton.TryRename(oldName, newName))
            IncrementVersion();
    }

    public override void OnUndoRedo()
    {
        UpdateBounds();
        if (!IsEditing) AtlasManager.MarkDirty(this);
        base.OnUndoRedo();
    }

    public Vector2Int GetFrameAtlasSize(ushort timeSlot)
    {
        var padding2 = EditorApplication.Config.AtlasPadding * 2;
        return new(RasterBounds.Size.X + padding2, RasterBounds.Size.Y + padding2);
    }

    public override void Dispose()
    {
        Root.Dispose();
        _sprite?.Dispose();
        _sprite = null;
        foreach (var sprite in _partSprites.Values)
            sprite.Dispose();
        _partSprites.Clear();
        _standaloneTexture?.Dispose();
        _standaloneTexture = null;
        base.Dispose();
    }
}
