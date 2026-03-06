//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Diagnostics.Contracts;
using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace NoZ.Editor;

public partial class SpriteDocument : Document, ISpriteSource
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

    public sealed class MeshSlot(byte sortOrder, StringId bone)
    {
        public readonly byte SortOrder = sortOrder;
        public readonly StringId Bone = bone;
        public readonly List<(byte LayerIndex, Shape Shape)> LayerShapes = new();
    }

    public const int MaxDocumentLayers = 32;

    private readonly List<SpriteLayer> _layers = new();
    private readonly List<Rect> _atlasUV = new();
    private Sprite? _sprite;
    public float Depth;
    public RectInt RasterBounds { get; private set; }
    public EdgeInsets Edges { get; set; } = EdgeInsets.Zero;

    public Color32 CurrentFillColor = Color32.White;
    public Color32 CurrentStrokeColor = new(0, 0, 0, 0);
    public byte CurrentStrokeWidth = 1;
    public int ActiveLayerIndex;
    public PathOperation CurrentOperation;

    public IReadOnlyList<SpriteLayer> Layers => _layers;

    public SpriteLayer ActiveLayer => _layers[ActiveLayerIndex];

    /// <summary>Total time slots across the longest layer.</summary>
    public int GlobalTimeSlots
    {
        get
        {
            var max = 1;
            foreach (var layer in _layers)
                max = Math.Max(max, layer.TotalTimeSlots);
            return max;
        }
    }

    /// <summary>Maximum frame count across all layers.</summary>
    public ushort MaxFrameCount
    {
        get
        {
            ushort max = 1;
            foreach (var layer in _layers)
                max = Math.Max(max, layer.FrameCount);
            return max;
        }
    }

    public static int GetLayerFrameAtTimeSlot(SpriteLayer layer, int globalTimeSlot)
    {
        var accumulated = 0;
        for (var f = 0; f < layer.FrameCount; f++)
        {
            var slots = 1 + layer.Frames[f].Hold;
            if (accumulated + slots > globalTimeSlot)
                return f;
            accumulated += slots;
        }

        return layer.FrameCount - 1;
    }

    public int GetLayerFrameAtTimeSlot(int layerIndex, int globalTimeSlot) =>
        GetLayerFrameAtTimeSlot(_layers[layerIndex], globalTimeSlot);

    public int MeshSlotCount
    {
        get
        {
            var slots = GetMeshSlots();
            return Math.Max(1, slots.Count);
        }
    }

    public bool ShowInSkeleton { get; set; }
    public bool ShowTiling { get; set; }
    public bool ShowSkeletonOverlay { get; set; }
    public Vector2Int? ConstrainedSize { get; set; }

    public GenerationImage Generation { get; } = new();
    public GenerationConfig? Refine;

    public bool HasGeneration => _layers.Any(l => l.HasGeneration);

    public bool IsGenerating => Generation.IsGenerating;

    public void EnsureDefaultLayer()
    {
        if (_layers.Count == 0)
            _layers.Add(new SpriteLayer { Name = "Layer 1" });
    }

    ushort ISpriteSource.FrameCount => (ushort)GlobalTimeSlots;
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
            foreach (var layer in doc._layers)
            {
                if (layer.Bone == oldBoneName)
                {
                    layer.Bone = newBoneName;
                    modified = true;
                }
            }

            if (modified)
                doc.IncrementVersion();
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
            foreach (var layer in doc._layers)
            {
                if (layer.Bone == removedBoneName)
                {
                    layer.Bone = StringId.None;
                    modified = true;
                }
            }

            if (modified)
            {
                doc.IncrementVersion();
                Notifications.Add($"Sprite '{doc.Name}' bone bindings updated (bone '{removedName}' deleted)");
            }
        }
    }

    /// <summary>Get the current document layer's shape for a given layer frame index.</summary>
    public Shape GetLayerShape(int layerIndex, int frameIndex) =>
        _layers[layerIndex].Frames[frameIndex].Shape;

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

    public override void Reload()
    {
        Edges = EdgeInsets.Zero;
        Binding.Clear();

        // Dispose old generated textures before clearing layers
        foreach (var layer in _layers)
            for (var fi = 0; fi < layer.FrameCount; fi++)
                layer.Frames[fi].Dispose();

        _layers.Clear();

        // Re-read and re-parse the .sprite file
        var contents = File.ReadAllText(Path);
        var tk = new Tokenizer(contents);
        Load(ref tk);

        // Resolve skeleton binding and recreate generated textures
        Binding.Resolve();
        LoadGeneratedTexture();

        // Update bounds and mark sprite dirty
        UpdateBounds();
    }

    private void Load(ref Tokenizer tk)
    {
        // Track legacy per-path layers/bones for migration
        var legacyPaths = new List<(ushort frameIndex, ushort pathIndex, byte sortOrder, StringId bone)>();
        var hasDocLayers = false;
        var legacyFrameCount = (ushort)0;
        var legacyFrames = new SpriteFrame[Sprite.MaxFrames];
        for (var i = 0; i < legacyFrames.Length; i++)
            legacyFrames[i] = new SpriteFrame();

        // Track if any non-layer-0 layer has holds (old format detection)
        var layer0HoldsOnly = true;

        // Parse header (edges, skeleton) and then layer blocks
        while (!tk.IsEOF)
        {
            if (tk.ExpectIdentifier("layer"))
            {
                // Layer is the top-level concept — everything after belongs to this layer
                var currentLayerIndex = _layers.Count;
                ParseDocumentLayer(ref tk);
                hasDocLayers = true;
                var layer = _layers[currentLayerIndex];

                while (!tk.IsEOF)
                {
                    if (tk.ExpectIdentifier("frame"))
                    {
                        var fi = layer.FrameCount;
                        if (fi >= Sprite.MaxFrames)
                            break;

                        // First frame already exists (FrameCount starts at 1), subsequent ones increment
                        if (fi > 0 || layer.Frames[0].Shape.PathCount > 0)
                            layer.FrameCount = (ushort)(fi + 1);
                        else
                            layer.FrameCount = 1;

                        var f = layer.Frames[layer.FrameCount - 1];

                        if (tk.ExpectIdentifier("hold"))
                        {
                            var hold = tk.ExpectInt();
                            f.Hold = hold;
                            if (currentLayerIndex > 0 && hold > 0)
                                layer0HoldsOnly = false;
                        }


                        // Parse paths within this frame
                        while (!tk.IsEOF && tk.ExpectIdentifier("path"))
                            ParsePathInLayer(f, ref tk);

                        // If this was the first frame and we didn't increment, ensure count is 1
                        if (layer.FrameCount == 0)
                            layer.FrameCount = 1;
                    }
                    else if (tk.ExpectIdentifier("image"))
                    {
                        // Legacy per-frame image — load into document-level GenerationImage
                        if (layer.FrameCount == 0)
                            layer.FrameCount = 1;

                        var base64 = tk.ExpectQuotedString();
                        if (!string.IsNullOrEmpty(base64))
                            Generation.ImageData = Convert.FromBase64String(base64);
                    }
                    else if (tk.ExpectIdentifier("path"))
                    {
                        // Path outside frame block — add to first frame
                        if (layer.FrameCount == 0)
                            layer.FrameCount = 1;
                        ParsePathInLayer(layer.Frames[layer.FrameCount - 1], ref tk);
                    }
                    else
                    {
                        break;
                    }
                }

                if (layer.FrameCount == 0)
                    layer.FrameCount = 1;
            }
            else if (tk.ExpectIdentifier("frame"))
            {
                // Legacy format: frames at top level (no layer blocks)
                var f = legacyFrames[legacyFrameCount++];
                var frameIndex = (ushort)(legacyFrameCount - 1);
                if (tk.ExpectIdentifier("hold"))
                    f.Hold = tk.ExpectInt();

                while (!tk.IsEOF && tk.ExpectIdentifier("path"))
                    ParseLegacyPath(f, frameIndex, ref tk, legacyPaths);
            }
            else if (tk.ExpectIdentifier("path"))
            {
                // Legacy: path at top level with no frame
                if (legacyFrameCount == 0)
                    legacyFrameCount = 1;
                ParseLegacyPath(legacyFrames[0], 0, ref tk, legacyPaths);
            }
            else if (tk.ExpectIdentifier("edges"))
            {
                if (tk.ExpectVec4(out var edgesVec))
                    Edges = new EdgeInsets(edgesVec.X, edgesVec.Y, edgesVec.Z, edgesVec.W);
            }
            else if (tk.ExpectIdentifier("skeleton"))
            {
                Binding.SkeletonName = StringId.Get(tk.ExpectQuotedString());
            }
            else if (tk.ExpectIdentifier("palette"))
            {
                // Legacy: palette keyword is ignored
                tk.ExpectQuotedString();
            }
            else if (tk.ExpectIdentifier("gen_image"))
            {
                var base64 = tk.ExpectQuotedString();
                if (!string.IsNullOrEmpty(base64))
                    Generation.ImageData = Convert.FromBase64String(base64);
            }
            else if (tk.ExpectIdentifier("refine"))
            {
                Refine = ParseGenerationConfig(ref tk);
            }
            else
            {
                tk.ExpectToken(out var badToken);
                Log.Error($"SpriteDocument.Load: Unexpected token '{tk.GetString(badToken)}'");
                break;
            }
        }

        for (int i = 0; i < _layers.Count; i++)
            _layers[i].Index = i;

        // Migrate legacy per-path layers/bones to document layers
        if (!hasDocLayers && legacyFrameCount > 0)
        {
            MigrateLegacyLayers(legacyPaths, legacyFrames, legacyFrameCount);
        }

        // Old format migration: if only layer 0 had holds, copy them to all layers
        if (hasDocLayers && layer0HoldsOnly && _layers.Count > 1)
        {
            var layer0 = _layers[0];
            for (var li = 1; li < _layers.Count; li++)
            {
                var layer = _layers[li];
                var minFrames = Math.Min(layer0.FrameCount, layer.FrameCount);
                for (var fi = 0; fi < minFrames; fi++)
                    layer.Frames[fi].Hold = layer0.Frames[fi].Hold;
            }
        }

        EnsureDefaultLayer();
    }

    private static GenerationConfig ParseGenerationConfig(ref Tokenizer tk)
    {
        var config = new GenerationConfig();
        while (!tk.IsEOF)
        {
            if (tk.ExpectIdentifier("prompt"))
                config.Prompt = tk.ExpectQuotedString() ?? "";
            else if (tk.ExpectIdentifier("prompt_neg"))
                config.NegativePrompt = tk.ExpectQuotedString() ?? "";
            else if (tk.ExpectIdentifier("seed"))
                config.Seed = tk.ExpectInt();
            else if (tk.ExpectIdentifier("strength"))
                config.Strength = tk.ExpectFloat(0.8f);
            else if (tk.ExpectIdentifier("steps"))
                config.Steps = tk.ExpectInt();
            else if (tk.ExpectIdentifier("guidance_scale"))
                config.GuidanceScale = tk.ExpectFloat(6.0f);
            else
                break;
        }
        return config;
    }

    private static void ParseGenerate(ref Tokenizer tk, SpriteLayer layer)
    {
        layer.Generation = ParseGenerationConfig(ref tk);
    }

    private void ParseDocumentLayer(ref Tokenizer tk)
    {
        var layer = new SpriteLayer
        {
            Name = tk.ExpectQuotedString() ?? $"Layer {_layers.Count + 1}"
        };

        // Parse optional flags: sort N, generated, locked, hidden, bone "name"
        while (!tk.IsEOF)
        {
            if (tk.ExpectIdentifier("sort"))
                layer.SortOrder = (byte)tk.ExpectInt();
            else if (tk.ExpectIdentifier("generate"))
                ParseGenerate(ref tk, layer);
            else if (tk.ExpectIdentifier("locked"))
                layer.Locked = true;
            else if (tk.ExpectIdentifier("hidden"))
                layer.Visible = false;
            else if (tk.ExpectIdentifier("bone"))
            {
                var boneName = tk.ExpectQuotedString();
                if (!string.IsNullOrEmpty(boneName))
                    layer.Bone = StringId.Get(boneName);
            }
            else if (tk.ExpectIdentifier("opacity"))
                layer.Opacity = tk.ExpectFloat(1.0f);
            else
                break;
        }

        _layers.Add(layer);
    }

    private void MigrateLegacyLayers(
        List<(ushort frameIndex, ushort pathIndex, byte sortOrder, StringId bone)> legacyPaths,
        SpriteFrame[] legacyFrames,
        ushort legacyFrameCount)
    {
        // Group by (sortOrder, bone) to create document layers
        var layerMap = new Dictionary<(byte sortOrder, StringId bone), byte>();

        foreach (var (_, _, sortOrder, bone) in legacyPaths)
        {
            var key = (sortOrder, bone);
            if (!layerMap.ContainsKey(key))
            {
                var docLayerIndex = (byte)_layers.Count;
                var name = $"Layer {_layers.Count + 1}";
                if (EditorApplication.Config.TryGetSortOrder(sortOrder, out var sortDef))
                    name = sortDef.Label;
                _layers.Add(new SpriteLayer
                {
                    Name = name,
                    SortOrder = sortOrder,
                    Bone = bone,
                });
                layerMap[key] = docLayerIndex;
            }
        }

        // Build per-path layer mapping
        var pathLayerMap = new Dictionary<(ushort frameIndex, ushort pathIndex), byte>();
        foreach (var (frameIndex, pathIndex, sortOrder, bone) in legacyPaths)
        {
            var key = (sortOrder, bone);
            pathLayerMap[(frameIndex, pathIndex)] = layerMap[key];
        }

        // If no legacy layer info, create a default layer
        if (_layers.Count == 0)
            _layers.Add(new SpriteLayer { Name = "Layer 1" });

        // Distribute paths from legacy frames to per-layer frames
        DistributeLegacyFrames(legacyFrames, legacyFrameCount, pathLayerMap);
    }

    private void DistributeLegacyFrames(SpriteFrame[] legacyFrames, ushort legacyFrameCount,
        Dictionary<(ushort frameIndex, ushort pathIndex), byte> pathLayerMap)
    {
        // Set frame counts
        foreach (var layer in _layers)
            layer.FrameCount = Math.Max((ushort)1, legacyFrameCount);

        for (ushort fi = 0; fi < legacyFrameCount; fi++)
        {
            var srcShape = legacyFrames[fi].Shape;

            // Copy hold to all layers
            foreach (var layer in _layers)
                layer.Frames[fi].Hold = legacyFrames[fi].Hold;

            // Distribute paths to per-layer shapes
            for (ushort pi = 0; pi < srcShape.PathCount; pi++)
            {
                ref readonly var path = ref srcShape.GetPath(pi);
                byte layerIdx = 0;
                if (pathLayerMap.TryGetValue((fi, pi), out var mapped))
                    layerIdx = Math.Min(mapped, (byte)(_layers.Count - 1));

                var dstShape = _layers[layerIdx].Frames[fi].Shape;

                // Copy path and its anchors to the destination layer shape
                var dstPathIdx = dstShape.AddPath(path.FillColor, path.StrokeColor, path.StrokeWidth, operation: path.Operation);
                if (dstPathIdx == ushort.MaxValue) continue;

                for (ushort ai = 0; ai < path.AnchorCount; ai++)
                {
                    ref readonly var anchor = ref srcShape.GetAnchor((ushort)(path.AnchorStart + ai));
                    dstShape.AddAnchor(dstPathIdx, anchor.Position, anchor.Curve);
                }
            }
        }
    }

    private void ParsePathInLayer(SpriteFrame f, ref Tokenizer tk)
    {
        var pathIndex = f.Shape.AddPath(Color32.White);
        var fillColor = Color32.White;
        var strokeColor = new Color32(0, 0, 0, 0);
        var strokeWidth = 1;
        var operation = PathOperation.Normal;

        while (!tk.IsEOF)
        {
            if (tk.ExpectIdentifier("fill"))
            {
                if (tk.ExpectColor(out var color))
                    fillColor = color.ToColor32();
                else
                {
                    fillColor = PaletteManager.GetColor(0, tk.ExpectInt()).ToColor32();
                    var legacyOpacity = tk.ExpectFloat(1.0f);
                    fillColor = fillColor.WithAlpha(legacyOpacity);
                }
            }
            else if (tk.ExpectIdentifier("stroke"))
            {
                if (tk.ExpectColor(out var color))
                    strokeColor = color.ToColor32();
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
                if (tk.ExpectBool())
                    operation = PathOperation.Subtract;
            }
            else if (tk.ExpectIdentifier("clip"))
            {
                if (tk.ExpectBool())
                    operation = PathOperation.Clip;
            }
            else if (tk.ExpectIdentifier("anchor"))
                ParseAnchor(f.Shape, pathIndex, ref tk);
            else
                break;
        }

        f.Shape.SetPathFillColor(pathIndex, fillColor);
        f.Shape.SetPathStroke(pathIndex, strokeColor, (byte)strokeWidth);
        if (operation != PathOperation.Normal)
            f.Shape.SetPathOperation(pathIndex, operation);
    }

    /// <summary>Parse a path in legacy format (no document layers). Used for backwards compatibility.</summary>
    private void ParseLegacyPath(SpriteFrame f, ushort frameIndex, ref Tokenizer tk,
        List<(ushort, ushort, byte, StringId)> legacyPaths)
    {
        var pathIndex = f.Shape.AddPath(Color32.White);
        var fillColor = Color32.White;
        var strokeColor = new Color32(0, 0, 0, 0);
        var strokeWidth = 1;
        var operation = PathOperation.Normal;
        byte legacySortOrder = 0;
        var legacyBone = StringId.None;
        var hasLegacyLayer = false;
        var hasLegacyBone = false;

        while (!tk.IsEOF)
        {
            if (tk.ExpectIdentifier("fill"))
            {
                if (tk.ExpectColor(out var color))
                    fillColor = color.ToColor32();
                else
                {
                    fillColor = PaletteManager.GetColor(0, tk.ExpectInt()).ToColor32();
                    var legacyOpacity = tk.ExpectFloat(1.0f);
                    fillColor = fillColor.WithAlpha(legacyOpacity);
                }
            }
            else if (tk.ExpectIdentifier("stroke"))
            {
                if (tk.ExpectColor(out var color))
                    strokeColor = color.ToColor32();
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
                if (tk.ExpectBool())
                    operation = PathOperation.Subtract;
            }
            else if (tk.ExpectIdentifier("clip"))
            {
                if (tk.ExpectBool())
                    operation = PathOperation.Clip;
            }
            else if (tk.ExpectIdentifier("layer"))
            {
                var layerId = tk.ExpectQuotedString();
                if (EditorApplication.Config.TryGetSortOrder(layerId, out var sg))
                    legacySortOrder = sg.SortOrder;
                hasLegacyLayer = true;
            }
            else if (tk.ExpectIdentifier("bone"))
            {
                var boneName = tk.ExpectQuotedString();
                if (!string.IsNullOrEmpty(boneName))
                {
                    legacyBone = StringId.Get(boneName);
                    hasLegacyBone = true;
                }
            }
            else if (tk.ExpectIdentifier("anchor"))
                ParseAnchor(f.Shape, pathIndex, ref tk);
            else
                break;
        }

        f.Shape.SetPathFillColor(pathIndex, fillColor);
        f.Shape.SetPathStroke(pathIndex, strokeColor, (byte)strokeWidth);
        if (operation != PathOperation.Normal)
            f.Shape.SetPathOperation(pathIndex, operation);

        if (hasLegacyLayer || hasLegacyBone)
            legacyPaths.Add((frameIndex, pathIndex, legacySortOrder, legacyBone));
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

        if (_layers.Count == 0)
        {
            Bounds = new Rect(-0.5f, -0.5f, 1f, 1f);
            return;
        }

        // Update samples and bounds for all layer frames
        var first = true;
        var bounds = Rect.Zero;
        RasterBounds = RectInt.Zero;

        foreach (var layer in _layers)
        {
            for (ushort fi = 0; fi < layer.FrameCount; fi++)
            {
                var shape = layer.Frames[fi].Shape;
                shape.UpdateSamples();
                shape.UpdateBounds();

                if (shape.AnchorCount == 0)
                    continue;

                if (first)
                {
                    bounds = shape.Bounds;
                    RasterBounds = shape.RasterBounds;
                    first = false;
                }
                else
                {
                    var fb = shape.Bounds;
                    var minX = MathF.Min(bounds.X, fb.X);
                    var minY = MathF.Min(bounds.Y, fb.Y);
                    var maxX = MathF.Max(bounds.Right, fb.Right);
                    var maxY = MathF.Max(bounds.Bottom, fb.Bottom);
                    bounds = Rect.FromMinMax(new Vector2(minX, minY), new Vector2(maxX, maxY));
                    RasterBounds = RasterBounds.Union(shape.RasterBounds);
                }
            }
        }

        if (first)
        {
            Bounds = new Rect(-0.5f, -0.5f, 1f, 1f);
            return;
        }

        Bounds = bounds;

        if (Bounds.Width <= 0 || Bounds.Height <= 0)
        {
            Bounds = new Rect(-0.5f, -0.5f, 1f, 1f);
            return;
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


    // :save
    public override void Save(StreamWriter writer)
    {
        if (!Edges.IsZero)
            writer.WriteLine($"edges ({Edges.T},{Edges.L},{Edges.B},{Edges.R})");

        if (Binding.IsBound)
            writer.WriteLine($"skeleton \"{Binding.SkeletonName}\"");

        if (_layers.Count > 0)
            writer.WriteLine();

        // Layers are the top-level concept — each layer contains its own frames/paths
        for (var layerIndex = 0; layerIndex < _layers.Count; layerIndex++)
        {
            var layer = _layers[layerIndex];

            // Write layer definition with properties
            writer.Write($"layer \"{layer.Name}\"");
            if (layer.SortOrder != 0)
                writer.Write($" sort {layer.SortOrder}");
            if (layer.Locked)
                writer.Write(" locked");
            if (!layer.Visible)
                writer.Write(" hidden");
            if (!layer.Bone.IsNone)
                writer.Write($" bone \"{layer.Bone}\"");
            if (layer.Opacity < 1.0f)
                writer.Write(string.Format(CultureInfo.InvariantCulture, " opacity {0}", layer.Opacity));
            writer.WriteLine();

            // Write generation settings (stored in the .sprite file, not .meta)
            if (layer.IsGenerated)
            {
                writer.WriteLine("generate");
                WriteGenerationConfig(writer, layer.Generation!);
            }

            // Write this layer's frames
            for (ushort frameIndex = 0; frameIndex < layer.FrameCount; frameIndex++)
            {
                var f = layer.Frames[frameIndex];
                var shape = f.Shape;

                // Always write frame markers for multi-frame layers or if hold > 0
                if (layer.FrameCount > 1 || f.Hold > 0)
                {
                    writer.Write("frame");
                    if (f.Hold > 0)
                        writer.Write($" hold {f.Hold}");
                    writer.WriteLine();
                }

                if (shape.PathCount > 0)
                    SaveLayerFrame(shape, writer);
            }

            if (layerIndex < _layers.Count - 1)
                writer.WriteLine();
        }

        // Write document-level generation image
        if (Generation.HasImageData)
        {
            writer.WriteLine();
            writer.WriteLine($"gen_image \"{Convert.ToBase64String(Generation.ImageData!)}\"");
        }

        // Write refine config
        if (Refine != null)
        {
            writer.WriteLine();
            writer.WriteLine("refine");
            WriteGenerationConfig(writer, Refine);
        }
    }

    private static void WriteGenerationConfig(StreamWriter writer, GenerationConfig config)
    {
        if (!string.IsNullOrEmpty(config.Prompt))
            writer.WriteLine($"prompt \"{config.Prompt.Replace("\"", "\\\"")}\"");
        if (!string.IsNullOrEmpty(config.NegativePrompt))
            writer.WriteLine($"prompt_neg \"{config.NegativePrompt.Replace("\"", "\\\"")}\"");
        writer.WriteLine(string.Format(CultureInfo.InvariantCulture, "seed {0}", config.Seed));
        writer.WriteLine(string.Format(CultureInfo.InvariantCulture, "strength {0}", config.Strength));
        writer.WriteLine(string.Format(CultureInfo.InvariantCulture, "steps {0}", config.Steps));
        writer.WriteLine(string.Format(CultureInfo.InvariantCulture, "guidance_scale {0}", config.GuidanceScale));
    }

    private static void SaveLayerFrame(Shape shape, StreamWriter writer)
    {
        for (ushort pIdx = 0; pIdx < shape.PathCount; pIdx++)
        {
            ref readonly var path = ref shape.GetPath(pIdx);

            writer.WriteLine("path");
            if (path.IsSubtract)
                writer.WriteLine("subtract true");
            if (path.IsClip)
                writer.WriteLine("clip true");
            writer.WriteLine($"fill {FormatColor(path.FillColor)}");

            if (path.StrokeColor.A > 0)
                writer.WriteLine($"stroke {FormatColor(path.StrokeColor)} {path.StrokeWidth}");

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

        var hasContent = _layers.Any(l => l.Frames[0].Shape.PathCount > 0);
        if (!hasContent)
        {
            DrawBounds();
            return;
        }

        DrawSprite();
    }

    public void DrawSprite(in Vector2 offset = default, float alpha = 1.0f, int frame = 0)
    {
        if (Atlas?.Texture == null) return;

        var sprite = Sprite;
        if (sprite == null) return;

        using (Graphics.PushState())
        {
            Graphics.SetTexture(Atlas.Texture);
            Graphics.SetShader(EditorAssets.Shaders.Texture);
            Graphics.SetColor(Color.White.WithAlpha(alpha * Workspace.XrayAlpha));
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
        if (Atlas?.Texture == null) return;

        var sprite = Sprite;
        if (sprite == null) return;

        using (Graphics.PushState())
        {
            Graphics.SetTexture(Atlas.Texture);
            Graphics.SetShader(EditorAssets.Shaders.Texture);
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

                Graphics.SetColor(Color.White);

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
        Depth = src.Depth;
        Bounds = src.Bounds;
        CurrentFillColor = src.CurrentFillColor;
        CurrentStrokeColor = src.CurrentStrokeColor;
        CurrentStrokeWidth = src.CurrentStrokeWidth;
        ActiveLayerIndex = src.ActiveLayerIndex;

        Edges = src.Edges;
        Binding.CopyFrom(src.Binding);

        _layers.Clear();
        _layers.AddRange(src._layers.Select(l => l.Clone()));
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
    }

    public override void PostLoad()
    {
        Binding.Resolve();
        LoadGeneratedTexture();
    }

    #region AI Generation

    /// <summary>
    /// Create GPU texture from document-level generated image for editor preview.
    /// </summary>
    internal void LoadGeneratedTexture()
    {
        Generation.Dispose();

        if (!Generation.HasImageData) return;

        try
        {
            using var ms = new MemoryStream(Generation.ImageData!);
            using var srcImage = SixLabors.ImageSharp.Image.Load<Rgba32>(ms);

            if (ConstrainedSize.HasValue)
            {
                var cs = ConstrainedSize.Value;
                if (srcImage.Width != cs.X || srcImage.Height != cs.Y)
                    srcImage.Mutate(x => x.Resize(cs.X, cs.Y));
            }

            var w = srcImage.Width;
            var h = srcImage.Height;
            var pixels = new byte[w * h * 4];
            srcImage.CopyPixelDataTo(pixels);
            Generation.Texture = Texture.Create(w, h, pixels, TextureFormat.RGBA8, TextureFilter.Linear, $"{Name}_gen");
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to create generated texture for '{Name}': {ex.Message}");
        }
    }

    private string ComputeGenerationHash(GenerationConfig gen)
    {
        var input = $"{gen.Prompt}|{gen.NegativePrompt}|{gen.Seed}|{gen.Strength}|{gen.Steps}|{gen.GuidanceScale}";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes);
    }



    private bool TryBlitGeneratedImage(PixelData<Color32> image, in AtlasSpriteRect rect, int padding)
    {
        if (!Generation.HasImageData)
            return false;

        try
        {
            using var ms = new MemoryStream(Generation.ImageData!);
            using var srcImage = SixLabors.ImageSharp.Image.Load<Rgba32>(ms);
            var padding2 = padding * 2;

            var targetW = rect.Rect.Width - padding2;
            var targetH = rect.Rect.Height - padding2;
            if (targetW <= 0 || targetH <= 0)
                return false;

            if (srcImage.Width != targetW || srcImage.Height != targetH)
                srcImage.Mutate(x => x.Resize(targetW, targetH));

            var w = srcImage.Width;
            var h = srcImage.Height;

            var rasterRect = new RectInt(
                rect.Rect.Position + new Vector2Int(padding, padding),
                new Vector2Int(w, h));

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    var pixel = srcImage[x, y];
                    var src = new Color32(pixel.R, pixel.G, pixel.B, pixel.A);
                    var dst = image[rasterRect.X + x, rasterRect.Y + y];
                    image[rasterRect.X + x, rasterRect.Y + y] = Color32.Blend(src, dst);
                }
            }

            var outerRect = new RectInt(rect.Rect.Position, new Vector2Int(w + padding2, h + padding2));
            image.BleedColors(rasterRect);
            for (int p = padding - 1; p >= 0; p--)
            {
                var padRect = new RectInt(
                    outerRect.Position + new Vector2Int(p, p),
                    outerRect.Size - new Vector2Int(p * 2, p * 2));
                image.ExtrudeEdges(padRect);
            }

            return true;
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to blit generated image: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Rasterizes a layer's vector paths as a filled mask (white shapes on black background)
    /// for use as inpainting mask in the generation API.
    /// Subtract paths carve black holes in the white shapes.
    /// </summary>
    private byte[] RasterizeMaskToPng(int targetLayerIndex)
    {
        UpdateBounds();
        var dpi = EditorApplication.Config.PixelsPerUnit;
        var w = RasterBounds.Size.X;
        var h = RasterBounds.Size.Y;
        if (w <= 0 || h <= 0)
            return [];

        using var pixels = new PixelData<Color32>(w, h);
        pixels.Clear(new Color32(0, 0, 0, 255)); // Opaque black background
        var targetRect = new RectInt(0, 0, w, h);
        var sourceOffset = -RasterBounds.Position;
        var white = new Color32(255, 255, 255, 255);

        var layer = _layers[targetLayerIndex];
        var shape = layer.Frames[0].Shape;

        // Collect positive (fill) and negative (subtract) contours
        var positivePaths = new Clipper2Lib.PathsD();
        var negativePaths = new Clipper2Lib.PathsD();

        for (ushort pi = 0; pi < shape.PathCount; pi++)
        {
            ref readonly var path = ref shape.GetPath(pi);
            if (path.AnchorCount < 3) continue;

            var pathShape = new Msdf.Shape();
            Msdf.ShapeClipper.AppendContour(pathShape, shape, pi);
            pathShape = Msdf.ShapeClipper.Union(pathShape);
            var contours = Msdf.ShapeClipper.ShapeToPaths(pathShape, 8);
            if (contours.Count == 0) continue;

            if (path.IsSubtract)
                negativePaths.AddRange(contours);
            else
                positivePaths.AddRange(contours);
        }

        if (positivePaths.Count == 0)
            return [];

        // Apply subtract paths: positive - negative = final mask
        Clipper2Lib.PathsD maskPaths;
        if (negativePaths.Count > 0)
        {
            maskPaths = Clipper2Lib.Clipper.BooleanOp(Clipper2Lib.ClipType.Difference,
                positivePaths, negativePaths, Clipper2Lib.FillRule.NonZero, precision: 6);
        }
        else
        {
            maskPaths = positivePaths;
        }

        if (maskPaths.Count > 0)
            Rasterizer.Fill(maskPaths, pixels, targetRect, sourceOffset, dpi, white);

        // Convert to ImageSharp image (background is already opaque black, shapes are white)
        using var img = new Image<Rgba32>(w, h);
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                var c = pixels[x, y];
                img[x, y] = new Rgba32(c.R, c.G, c.B, 255);
            }
        }

        using var ms = new MemoryStream();
        img.SaveAsPng(ms);
        var pngBytes = ms.ToArray();

        // Debug: write mask to tmp folder for inspection
        try
        {
            var tmpDir = System.IO.Path.Combine(EditorApplication.ProjectPath, "tmp");
            Directory.CreateDirectory(tmpDir);
            File.WriteAllBytes(System.IO.Path.Combine(tmpDir, $"{Name}_mask_L{targetLayerIndex}.png"), pngBytes);
        }
        catch { }

        return pngBytes;
    }

    public void GenerateAsync()
    {
        // Collect all generated layers (bottom to top)
        var genLayers = new List<(int Index, SpriteLayer Layer)>();
        for (int i = 0; i < _layers.Count; i++)
        {
            if (_layers[i].IsGenerated)
                genLayers.Add((i, _layers[i]));
        }

        if (genLayers.Count == 0)
        {
            Log.Error($"No generated layers found for '{Name}'");
            return;
        }

        // Check if any layer is already generating
        if (Generation.IsGenerating)
            return;

        if (!ConstrainedSize.HasValue)
        {
            Log.Error($"Generation requires a sprite size constraint for '{Name}'");
            return;
        }

        // Build shapes array from all generated layers (bottom to top)
        var shapes = new List<GenerationShape>();
        foreach (var (layerIndex, layer) in genLayers)
        {
            var gen = layer.Generation!;
            var maskBytes = RasterizeMaskToPng(layerIndex);
            if (maskBytes.Length == 0)
            {
                Log.Warning($"Layer '{layer.Name}' has no visible shapes, skipping");
                continue;
            }

            shapes.Add(new GenerationShape
            {
                Mask = $"data:image/png;base64,{Convert.ToBase64String(maskBytes)}",
                Prompt = gen.Prompt,
                NegativePrompt = string.IsNullOrEmpty(gen.NegativePrompt) ? null : gen.NegativePrompt,
                Strength = gen.Strength,
                Steps = gen.Steps,
                GuidanceScale = gen.GuidanceScale,
            });
        }

        if (shapes.Count == 0)
        {
            Log.Error("Cannot generate: no layers have visible shapes");
            return;
        }

        var server = EditorApplication.Config?.GenerationServer ?? "http://127.0.0.1:7860";
        var primaryGen = genLayers[0].Layer.Generation!;
        var refine = Refine ?? primaryGen;
        var request = new GenerationRequest
        {
            Server = server,
            Shapes = shapes,
            Refine = new GenerationRefine
            {
                Prompt = refine.Prompt,
                NegativePrompt = string.IsNullOrEmpty(refine.NegativePrompt) ? null : refine.NegativePrompt,
                Strength = refine.Strength,
                Steps = refine.Steps,
                GuidanceScale = refine.GuidanceScale,
            },
            Seed = primaryGen.Seed == 0 ? null : primaryGen.Seed,
        };

        Log.Info($"Starting generation for '{Name}' ({shapes.Count} shapes) on {server}...");

        var genImage = Generation;
        genImage.IsGenerating = true;
        genImage.GenerationError = null;

        var cts = new System.Threading.CancellationTokenSource();
        genImage.CancellationSource = cts;

        GenerationClient.Generate(request, status =>
        {
            if (genImage.CancellationSource == null)
                return;

            switch (status.State)
            {
                case GenerationState.Queued:
                    genImage.GenerationState = GenerationState.Queued;
                    genImage.QueuePosition = status.QueuePosition;
                    genImage.GenerationProgress = 0f;
                    Log.Info($"Generation queued for '{Name}' (position {status.QueuePosition})");
                    break;

                case GenerationState.Running:
                    genImage.GenerationState = GenerationState.Running;
                    genImage.CurrentStep = status.CurrentStep;
                    genImage.TotalSteps = status.TotalSteps;
                    genImage.GenerationProgress = status.Progress;
                    break;

                case GenerationState.Completed:
                    genImage.IsGenerating = false;
                    genImage.CancellationSource = null;
                    genImage.GenerationState = GenerationState.Completed;
                    genImage.GenerationProgress = 1f;

                    if (status.Result == null)
                    {
                        Log.Error($"Generation completed but no result for '{Name}'");
                        return;
                    }

                    try
                    {
                        var imageBytes = Convert.FromBase64String(status.Result.Image);

                        using var ms = new MemoryStream(imageBytes);
                        using var srcImage = SixLabors.ImageSharp.Image.Load<Rgba32>(ms);

                        var cs = ConstrainedSize!.Value;
                        if (srcImage.Width != cs.X || srcImage.Height != cs.Y)
                            srcImage.Mutate(x => x.Resize(cs.X, cs.Y));

                        using var outMs = new MemoryStream();
                        srcImage.SaveAsPng(outMs);
                        genImage.ImageData = outMs.ToArray();

                        // Recreate GPU texture
                        genImage.Dispose();
                        var w = srcImage.Width;
                        var h = srcImage.Height;
                        var px = new byte[w * h * 4];
                        srcImage.CopyPixelDataTo(px);
                        genImage.Texture = Texture.Create(w, h, px, TextureFormat.RGBA8, TextureFilter.Linear, $"{Name}_gen");

                        try
                        {
                            var tmpDir = System.IO.Path.Combine(EditorApplication.ProjectPath, "tmp");
                            Directory.CreateDirectory(tmpDir);
                            File.WriteAllBytes(System.IO.Path.Combine(tmpDir, $"{Name}_gen.png"), genImage.ImageData);
                        }
                        catch { }

                        if (primaryGen.Seed == 0 && status.Result.Seed != 0)
                            primaryGen.Seed = status.Result.Seed;

                        Log.Info($"Generation complete for '{Name}' ({status.Result.Width}x{status.Result.Height}, seed={status.Result.Seed})");
                        IncrementVersion();
                    }
                    catch (Exception ex)
                    {
                        Log.Error($"Failed to process generated image for '{Name}': {ex.Message}");
                    }
                    break;

                case GenerationState.Failed:
                    genImage.IsGenerating = false;
                    genImage.CancellationSource = null;
                    genImage.GenerationState = GenerationState.Failed;
                    genImage.GenerationError = status.Error;
                    Log.Error($"Generation failed for '{Name}': {status.Error}");
                    break;
            }
        }, cts.Token);
    }

    #endregion

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

        // Blit the generated composite image first (below all vector layers).
        TryBlitGeneratedImage(image, rect, padding);

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

            AtlasManager.LogAtlas($"Rasterize: Name={rect.Name} Frame={frameIndex} SortOrder={slot.SortOrder} Bone={slot.Bone} Rect={rect.Rect} SlotBounds={slotRasterBounds}");

            var targetRect = new RectInt(
                rect.Rect.Position + new Vector2Int(xOffset, 0),
                new Vector2Int(slotWidth, slotRasterBounds.Size.Y + padding2));
            var sourceOffset = -slotRasterBounds.Position + new Vector2Int(padding, padding);

            // Rasterize vector layers only (generated layers already blitted above)
            var hasVectorContent = false;
            var hasAnyLayer = false;
            foreach (var (layerIdx, layerShape) in slot.LayerShapes)
            {
                if (layerIdx >= _layers.Count) continue;
                var docLayer = _layers[layerIdx];
                hasAnyLayer = true;

                if (docLayer.IsGenerated)
                    continue; // Skip — generated image already composited below

                if (layerShape.PathCount > 0)
                {
                    var singleSlot = new MeshSlot(slot.SortOrder, slot.Bone);
                    singleSlot.LayerShapes.Add((layerIdx, layerShape));
                    RasterizeSlot(singleSlot, image, targetRect, sourceOffset, dpi);
                    hasVectorContent = true;
                }
            }

            // Fallback: if no per-layer content was composited and no layers were processed individually
            if (!hasVectorContent && !hasAnyLayer && HasPaths(slot))
                RasterizeSlot(slot, image, targetRect, sourceOffset, dpi);

            // Bleed RGB into transparent pixels to prevent fringing with linear filtering.
            image.BleedColors(targetRect);

            xOffset += slotWidth;
        }
    }

    private static bool HasPaths(MeshSlot slot)
    {
        foreach (var (_, shape) in slot.LayerShapes)
        {
            if (shape.PathCount > 0) return true;
        }
        return false;
    }

    private static void RasterizeSlot(
        MeshSlot slot,
        PixelData<Color32> image,
        RectInt targetRect,
        Vector2Int sourceOffset,
        int dpi)
    {
        // Collect all subtract paths across all layers in this slot.
        // Each entry tracks (layerIndex, pathIndex) for ordering.
        List<(int LayerIndex, ushort PathIndex, Clipper2Lib.PathsD Contours)>? subtractEntries = null;
        foreach (var (layerIdx, shape) in slot.LayerShapes)
        {
            for (ushort pi = 0; pi < shape.PathCount; pi++)
            {
                ref readonly var path = ref shape.GetPath(pi);
                if (!path.IsSubtract || path.AnchorCount < 3) continue;

                var subShape = new Msdf.Shape();
                Msdf.ShapeClipper.AppendContour(subShape, shape, pi);
                var subContours = Msdf.ShapeClipper.ShapeToPaths(subShape, 8);
                if (subContours.Count > 0)
                {
                    subtractEntries ??= new();
                    subtractEntries.Add((layerIdx, pi, subContours));
                }
            }
        }

        // Rasterize each layer's paths in order (lower layer index = drawn first = behind)
        // Track accumulated geometry for clip operations across all layers
        Clipper2Lib.PathsD? accumulatedPaths = null;

        foreach (var (layerIdx, shape) in slot.LayerShapes)
        {
            // Snapshot accumulated paths at layer start for clip (cross-layer only)
            var lowerLayerPaths = accumulatedPaths;

            for (ushort pi = 0; pi < shape.PathCount; pi++)
            {
                ref readonly var path = ref shape.GetPath(pi);
                if (path.IsSubtract || path.AnchorCount < 3) continue;

                // Build contours for this path
                var pathShape = new Msdf.Shape();
                Msdf.ShapeClipper.AppendContour(pathShape, shape, pi);
                pathShape = Msdf.ShapeClipper.Union(pathShape);
                var contours = Msdf.ShapeClipper.ShapeToPaths(pathShape, 8);
                if (contours.Count == 0) continue;

                if (path.IsClip)
                {
                    // Clip: intersect with accumulated geometry from lower layers only
                    if (lowerLayerPaths is not { Count: > 0 }) continue;
                    contours = Clipper2Lib.Clipper.BooleanOp(Clipper2Lib.ClipType.Intersection,
                        contours, lowerLayerPaths, Clipper2Lib.FillRule.NonZero, precision: 6);
                    if (contours.Count == 0) continue;
                }
                else
                {
                    // Normal path: add fill area to accumulated geometry for future clips
                    var accContours = contours;
                    if (path.StrokeColor.A > 0 && path.StrokeWidth > 0)
                    {
                        var halfStroke = path.StrokeWidth * Shape.StrokeScale;
                        var contracted = Clipper2Lib.Clipper.InflatePaths(contours, -halfStroke,
                            Clipper2Lib.JoinType.Round, Clipper2Lib.EndType.Polygon, precision: 6);
                        if (contracted.Count > 0)
                            accContours = contracted;
                    }

                    if (accumulatedPaths == null)
                        accumulatedPaths = new Clipper2Lib.PathsD(accContours);
                    else
                        accumulatedPaths = Clipper2Lib.Clipper.BooleanOp(Clipper2Lib.ClipType.Union,
                            accumulatedPaths, accContours, Clipper2Lib.FillRule.NonZero, precision: 6);
                }

                // Apply subtract paths from same layer only (higher path index subtracts from lower)
                if (subtractEntries != null)
                {
                    Clipper2Lib.PathsD? subtractPaths = null;
                    foreach (var (subLayerIdx, subPi, subContours) in subtractEntries)
                    {
                        if (subLayerIdx != layerIdx) continue;
                        if (subPi <= pi) continue;
                        subtractPaths ??= new Clipper2Lib.PathsD();
                        subtractPaths.AddRange(subContours);
                    }

                    if (subtractPaths is { Count: > 0 })
                    {
                        contours = Clipper2Lib.Clipper.BooleanOp(Clipper2Lib.ClipType.Difference,
                            contours, subtractPaths, Clipper2Lib.FillRule.NonZero, precision: 6);
                        if (contours.Count == 0) continue;
                    }
                }

                var hasStroke = path.StrokeColor.A > 0 && path.StrokeWidth > 0;
                var hasFill = path.FillColor.A > 0;

                // Stroke: rasterize the ring (full shape minus contracted interior)
                if (hasStroke)
                {
                    var halfStroke = path.StrokeWidth * Shape.StrokeScale;
                    var contracted = Clipper2Lib.Clipper.InflatePaths(contours, -halfStroke,
                        Clipper2Lib.JoinType.Round, Clipper2Lib.EndType.Polygon, precision: 6);

                    if (hasFill)
                    {
                        Rasterizer.Fill(contours, image, targetRect, sourceOffset, dpi, path.StrokeColor);
                        if (contracted.Count > 0)
                            Rasterizer.Fill(contracted, image, targetRect, sourceOffset, dpi, path.FillColor);
                    }
                    else
                    {
                        var ring = Clipper2Lib.Clipper.BooleanOp(Clipper2Lib.ClipType.Difference,
                            contours, contracted, Clipper2Lib.FillRule.NonZero, precision: 6);
                        if (ring.Count > 0)
                            Rasterizer.Fill(ring, image, targetRect, sourceOffset, dpi, path.StrokeColor);
                    }
                }
                else if (hasFill)
                {
                    Rasterizer.Fill(contours, image, targetRect, sourceOffset, dpi, path.FillColor);
                }
            }
        }
    }

    void ISpriteSource.UpdateAtlasUVs(AtlasDocument atlas, ReadOnlySpan<AtlasSpriteRect> allRects, int padding)
    {
        ClearAtlasUVs();
        var padding2 = padding * 2;
        int uvIndex = 0;
        var ts = (float)EditorApplication.Config.AtlasSize;

        var totalSlots = (ushort)GlobalTimeSlots;
        for (ushort frameIndex = 0; frameIndex < totalSlots; frameIndex++)
        {
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

        var allMeshes = new List<SpriteMesh>();
        var totalSlots = GlobalTimeSlots;
        var frameTable = new SpriteFrameInfo[totalSlots];
        int uvIndex = 0;

        for (int frameIndex = 0; frameIndex < totalSlots; frameIndex++)
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

                allMeshes.Add(new SpriteMesh(
                    uv,
                    (short)slot.SortOrder,
                    boneIndex,
                    bounds.Position,
                    bounds.Size));
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
            edges: ConstrainedSize.HasValue ? Edges : EdgeInsets.Zero,
            sliceMask: Sprite.CalculateSliceMask(RasterBounds, ConstrainedSize.HasValue ? Edges : EdgeInsets.Zero));
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

        // One mesh per (layer, bone) slot — colors are baked into the bitmap
        var totalSlots = (ushort)GlobalTimeSlots;
        ushort totalMeshes = 0;
        for (ushort fi = 0; fi < totalSlots; fi++)
            totalMeshes += (ushort)GetMeshSlots(fi).Count;

        using var writer = new BinaryWriter(File.Create(outputPath));
        writer.WriteAssetHeader(AssetType.Sprite, Sprite.Version, 0);
        writer.Write(totalSlots);
        writer.Write((ushort)(Atlas?.Index ?? 0));
        writer.Write((short)RasterBounds.Left);
        writer.Write((short)RasterBounds.Top);
        writer.Write((short)RasterBounds.Right);
        writer.Write((short)RasterBounds.Bottom);
        writer.Write((float)EditorApplication.Config.PixelsPerUnit);
        writer.Write((byte)TextureFilter.Linear);
        writer.Write((short)-1);  // Legacy bone index field
        writer.Write(totalMeshes);
        writer.Write(12.0f);  // Frame rate

        // 9-slice edges (version 10) — only active with a constrained size
        var activeEdges = ConstrainedSize.HasValue ? Edges : EdgeInsets.Zero;
        writer.Write((short)activeEdges.T);
        writer.Write((short)activeEdges.L);
        writer.Write((short)activeEdges.B);
        writer.Write((short)activeEdges.R);
        writer.Write(Sprite.CalculateSliceMask(RasterBounds, activeEdges));

        int uvIndex = 0;
        var meshStarts = new ushort[totalSlots];
        var meshCounts = new ushort[totalSlots];
        ushort meshOffset = 0;

        for (ushort frameIndex = 0; frameIndex < totalSlots; frameIndex++)
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

                WriteMesh(writer, uv, (short)slot.SortOrder, boneIndex, bounds);
                frameMeshCount += 1;
            }

            meshCounts[frameIndex] = frameMeshCount;
            meshOffset += frameMeshCount;
        }

        for (int frameIndex = 0; frameIndex < totalSlots; frameIndex++)
        {
            writer.Write(meshStarts[frameIndex]);
            writer.Write(meshCounts[frameIndex]);
        }
    }

    private static void WriteMesh(BinaryWriter writer, Rect uv, short sortOrder, short boneIndex, RectInt bounds)
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
        var totalTimeSlots = GlobalTimeSlots;

        foreach (var slot in slots)
        {
            var bounds = RectInt.Zero;
            for (var ts = 0; ts < totalTimeSlots; ts++)
            {
                var slotBounds = GetSlotBounds(slot, ts);
                if (slotBounds.Width <= 0 || slotBounds.Height <= 0)
                    continue;
                bounds = bounds.Width <= 0 ? slotBounds : RectInt.Union(bounds, slotBounds);
            }
            result.Add(bounds);
        }
        return result;
    }

    public List<RectInt> GetMeshSlotBounds(ushort timeSlot)
    {
        var slots = GetMeshSlots(timeSlot);
        var result = new List<RectInt>(slots.Count);
        foreach (var slot in slots)
            result.Add(GetSlotBounds(slot, timeSlot));
        return result;
    }

    private RectInt GetSlotBounds(MeshSlot slot, int timeSlot)
    {
        var bounds = RectInt.Zero;
        var first = true;
        foreach (var (layerIdx, _) in slot.LayerShapes)
        {
            var frameIdx = GetLayerFrameAtTimeSlot(layerIdx, timeSlot);
            var shape = _layers[layerIdx].Frames[frameIdx].Shape;
            var sb = shape.GetRasterBounds();
            if (sb.Width <= 0 || sb.Height <= 0) continue;
            bounds = first ? sb : RectInt.Union(bounds, sb);
            first = false;
        }
        return bounds;
    }

    public Vector2Int GetFrameAtlasSize(ushort timeSlot)
    {
        var padding2_ = EditorApplication.Config.AtlasPadding * 2;
        var slotBounds = GetMeshSlotBounds(timeSlot);

        if (slotBounds.Count == 0)
            return new(RasterBounds.Size.X + padding2_, RasterBounds.Size.Y + padding2_);

        var totalWidth = 0;
        var maxHeight = 0;
        for (int i = 0; i < slotBounds.Count; i++)
        {
            var bounds = slotBounds[i];
            var slotWidth = (bounds.Width > 0 ? bounds.Size.X : RasterBounds.Size.X) + padding2_;
            var slotHeight = (bounds.Height > 0 ? bounds.Size.Y : RasterBounds.Size.Y) + padding2_;
            totalWidth += slotWidth;
            maxHeight = Math.Max(maxHeight, slotHeight);
        }
        return new(totalWidth, maxHeight);
    }

    public List<MeshSlot> GetMeshSlots() => GetMeshSlots(0);

    public List<MeshSlot> GetMeshSlots(ushort timeSlot)
    {
        var slots = new List<MeshSlot>();

        EnsureDefaultLayer();

        // Iterate document layers in order, grouping by (SortOrder, Bone).
        // Adjacent layers with same (SortOrder, Bone) auto-merge into one MeshSlot.
        MeshSlot? currentSlot = null;

        for (var layerIdx = 0; layerIdx < _layers.Count; layerIdx++)
        {
            var docLayer = _layers[layerIdx];
            var sortOrder = docLayer.SortOrder;
            var bone = docLayer.Bone;
            var frameIdx = GetLayerFrameAtTimeSlot(layerIdx, timeSlot);
            var shape = docLayer.Frames[frameIdx].Shape;

            // Auto-merge: extend current slot if same sort order and bone
            if (currentSlot != null && currentSlot.SortOrder == sortOrder && currentSlot.Bone == bone)
            {
                // Merge into existing slot
            }
            else
            {
                currentSlot = new MeshSlot(sortOrder, bone);
                slots.Add(currentSlot);
            }

            currentSlot.LayerShapes.Add(((byte)layerIdx, shape));
        }

        return slots;
    }
}
