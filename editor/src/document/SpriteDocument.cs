//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Globalization;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace NoZ.Editor;

public enum DocumentLayerType : byte
{
    Vector,
    Generated,
}

public class DocumentLayer
{
    public string Name = "";
    public DocumentLayerType Type;
    public bool Visible = true;
    public bool Locked;
    public float Opacity = 1.0f;
    public byte SortOrder;
    public StringId Bone;

    // Per-layer frame timeline
    public readonly LayerFrame[] Frames = new LayerFrame[Sprite.MaxFrames];
    public ushort FrameCount = 1;

    // Generated layer properties (only used when Type == Generated)
    public string Prompt = "";
    public string NegativePrompt = "";
    public string Style = "";
    public long Seed;
    public bool Auto;
    public Texture? GeneratedTexture;
    public bool IsGenerating;

    public bool HasGeneration => Type == DocumentLayerType.Generated && !string.IsNullOrEmpty(Prompt);

    /// <summary>Total time slots this layer occupies (each frame = 1 + Hold slots).</summary>
    public int TotalTimeSlots
    {
        get
        {
            var total = 0;
            for (var i = 0; i < FrameCount; i++)
                total += 1 + Frames[i].Hold;
            return total;
        }
    }

    public DocumentLayer()
    {
        for (var i = 0; i < Frames.Length; i++)
            Frames[i] = new LayerFrame();
    }

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

    public DocumentLayer Clone()
    {
        var clone = new DocumentLayer
        {
            Name = Name,
            Type = Type,
            Visible = Visible,
            Locked = Locked,
            Opacity = Opacity,
            SortOrder = SortOrder,
            Bone = Bone,
            Prompt = Prompt,
            NegativePrompt = NegativePrompt,
            Style = Style,
            Seed = Seed,
            Auto = Auto,
            FrameCount = FrameCount,
        };
        for (var i = 0; i < FrameCount; i++)
        {
            clone.Frames[i].Shape.CopyFrom(Frames[i].Shape);
            clone.Frames[i].Hold = Frames[i].Hold;
        }
        return clone;
    }
}

public class LayerFrame : IDisposable
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

    public sealed class MeshSlot(byte sortOrder, StringId bone)
    {
        public readonly byte SortOrder = sortOrder;
        public readonly StringId Bone = bone;
        public readonly List<ushort> PathIndices = new();
        public readonly List<byte> DocLayers = new();  // which doc layers contribute to this slot
    }

    public const int MaxDocumentLayers = 32;

    private readonly List<DocumentLayer> _documentLayers = new();
    private readonly List<Rect> _atlasUV = new();
    private Sprite? _sprite;
    private readonly Shape _compositeShape = new();

    public float Depth;
    public RectInt RasterBounds { get; private set; }
    public EdgeInsets Edges { get; set; } = EdgeInsets.Zero;

    public Color32 CurrentFillColor = Color32.White;
    public Color32 CurrentStrokeColor = new(0, 0, 0, 0);
    public byte CurrentStrokeWidth = 1;
    public int CurrentDocumentLayer;
    public PathOperation CurrentOperation;

    public IReadOnlyList<DocumentLayer> DocumentLayers => _documentLayers;

    /// <summary>Total time slots across the longest layer.</summary>
    public int GlobalTimeSlots
    {
        get
        {
            var max = 1;
            foreach (var layer in _documentLayers)
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
            foreach (var layer in _documentLayers)
                max = Math.Max(max, layer.FrameCount);
            return max;
        }
    }

    /// <summary>Given a global time slot, return which frame index a specific layer is showing.</summary>
    public int GetLayerFrameAtTimeSlot(int layerIndex, int globalTimeSlot)
    {
        var layer = _documentLayers[layerIndex];
        var accumulated = 0;
        for (var f = 0; f < layer.FrameCount; f++)
        {
            var slots = 1 + layer.Frames[f].Hold;
            if (accumulated + slots > globalTimeSlot)
                return f;
            accumulated += slots;
        }
        // Past the end: hold last frame
        return layer.FrameCount - 1;
    }

    /// <summary>Build a composite shape from all layers at the given time slot.</summary>
    public Shape GetCompositeShape(int timeSlot)
    {
        _compositeShape.Clear();
        for (var layerIdx = 0; layerIdx < _documentLayers.Count; layerIdx++)
        {
            var layer = _documentLayers[layerIdx];
            var frameIdx = GetLayerFrameAtTimeSlot(layerIdx, timeSlot);
            _compositeShape.AppendFrom(layer.Frames[frameIdx].Shape, (byte)layerIdx);
        }
        _compositeShape.UpdateBounds();
        return _compositeShape;
    }

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

    private string _generationHash = "";

    /// <summary>Returns true if any document layer is a Generated layer with a prompt.</summary>
    public bool HasGeneration => _documentLayers.Any(l => l.HasGeneration);

    /// <summary>Returns true if any generated layer is currently generating.</summary>
    public bool IsGenerating => _documentLayers.Any(l => l.IsGenerating);

    public void EnsureDefaultLayer()
    {
        if (_documentLayers.Count == 0)
            _documentLayers.Add(new DocumentLayer { Name = "Layer 1" });
    }

    public DocumentLayer? GetCurrentDocumentLayer() =>
        CurrentDocumentLayer >= 0 && CurrentDocumentLayer < _documentLayers.Count
            ? _documentLayers[CurrentDocumentLayer]
            : null;

    public int AddDocumentLayer(DocumentLayerType type = DocumentLayerType.Vector)
    {
        if (_documentLayers.Count >= MaxDocumentLayers)
            return -1;

        var name = type == DocumentLayerType.Generated
            ? $"Generated {_documentLayers.Count(l => l.Type == DocumentLayerType.Generated) + 1}"
            : $"Layer {_documentLayers.Count + 1}";

        _documentLayers.Add(new DocumentLayer { Name = name, Type = type });
        CurrentDocumentLayer = _documentLayers.Count - 1;
        MarkModified();
        return CurrentDocumentLayer;
    }

    public void DeleteDocumentLayer(int index)
    {
        if (index < 0 || index >= _documentLayers.Count || _documentLayers.Count <= 1)
            return;

        _documentLayers.RemoveAt(index);

        if (CurrentDocumentLayer >= _documentLayers.Count)
            CurrentDocumentLayer = _documentLayers.Count - 1;

        MarkModified();
        UpdateBounds();
    }

    public void MoveDocumentLayer(int fromIndex, int toIndex)
    {
        if (fromIndex < 0 || fromIndex >= _documentLayers.Count ||
            toIndex < 0 || toIndex >= _documentLayers.Count ||
            fromIndex == toIndex)
            return;

        var layer = _documentLayers[fromIndex];
        _documentLayers.RemoveAt(fromIndex);
        _documentLayers.Insert(toIndex, layer);

        // Track the selected layer
        if (CurrentDocumentLayer == fromIndex)
            CurrentDocumentLayer = toIndex;
        else if (fromIndex < toIndex && CurrentDocumentLayer > fromIndex && CurrentDocumentLayer <= toIndex)
            CurrentDocumentLayer--;
        else if (fromIndex > toIndex && CurrentDocumentLayer >= toIndex && CurrentDocumentLayer < fromIndex)
            CurrentDocumentLayer++;

        MarkModified();
        UpdateBounds();
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
            foreach (var layer in doc._documentLayers)
            {
                if (layer.Bone == oldBoneName)
                {
                    layer.Bone = newBoneName;
                    modified = true;
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
            foreach (var layer in doc._documentLayers)
            {
                if (layer.Bone == removedBoneName)
                {
                    layer.Bone = StringId.None;
                    modified = true;
                }
            }

            if (modified)
            {
                doc.MarkModified();
                Notifications.Add($"Sprite '{doc.Name}' bone bindings updated (bone '{removedName}' deleted)");
            }
        }
    }

    /// <summary>Get the current document layer's shape for a given layer frame index.</summary>
    public Shape GetLayerShape(int layerIndex, int frameIndex) =>
        _documentLayers[layerIndex].Frames[frameIndex].Shape;

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
        _documentLayers.Clear();

        // Re-read and re-parse the .sprite file
        var contents = File.ReadAllText(Path);
        var tk = new Tokenizer(contents);
        Load(ref tk);

        // Resolve skeleton binding
        Binding.Resolve();

        // Update bounds and mark sprite dirty
        UpdateBounds();
    }

    private void Load(ref Tokenizer tk)
    {
        // Track legacy per-path layers/bones for migration
        var legacyPaths = new List<(ushort frameIndex, ushort pathIndex, byte sortOrder, StringId bone)>();
        var hasDocLayers = false;
        var legacyFrameCount = (ushort)0;
        var legacyFrames = new LayerFrame[Sprite.MaxFrames];
        for (var i = 0; i < legacyFrames.Length; i++)
            legacyFrames[i] = new LayerFrame();

        // Track if any non-layer-0 layer has holds (old format detection)
        var layer0HoldsOnly = true;

        // Parse header (edges, skeleton) and then layer blocks
        while (!tk.IsEOF)
        {
            if (tk.ExpectIdentifier("layer"))
            {
                // Layer is the top-level concept — everything after belongs to this layer
                var currentLayerIndex = _documentLayers.Count;
                ParseDocumentLayer(ref tk);
                hasDocLayers = true;
                var layer = _documentLayers[currentLayerIndex];

                // Parse frames/paths within this layer
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
            else
            {
                tk.ExpectToken(out var badToken);
                Log.Error($"SpriteDocument.Load: Unexpected token '{tk.GetString(badToken)}'");
                break;
            }
        }

        // Migrate legacy per-path layers/bones to document layers
        if (!hasDocLayers && legacyFrameCount > 0)
        {
            MigrateLegacyLayers(legacyPaths, legacyFrames, legacyFrameCount);
        }

        // Old format migration: if only layer 0 had holds, copy them to all layers
        if (hasDocLayers && layer0HoldsOnly && _documentLayers.Count > 1)
        {
            var layer0 = _documentLayers[0];
            for (var li = 1; li < _documentLayers.Count; li++)
            {
                var layer = _documentLayers[li];
                var minFrames = Math.Min(layer0.FrameCount, layer.FrameCount);
                for (var fi = 0; fi < minFrames; fi++)
                    layer.Frames[fi].Hold = layer0.Frames[fi].Hold;
            }
        }

        EnsureDefaultLayer();
    }

    private void ParseDocumentLayer(ref Tokenizer tk)
    {
        var layer = new DocumentLayer();
        layer.Name = tk.ExpectQuotedString() ?? $"Layer {_documentLayers.Count + 1}";

        // Parse optional flags: sort N, generated, locked, hidden, bone "name"
        while (!tk.IsEOF)
        {
            if (tk.ExpectIdentifier("sort"))
                layer.SortOrder = (byte)tk.ExpectInt();
            else if (tk.ExpectIdentifier("generated"))
                layer.Type = DocumentLayerType.Generated;
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

        _documentLayers.Add(layer);
    }

    private void MigrateLegacyLayers(
        List<(ushort frameIndex, ushort pathIndex, byte sortOrder, StringId bone)> legacyPaths,
        LayerFrame[] legacyFrames,
        ushort legacyFrameCount)
    {
        // Group by (sortOrder, bone) to create document layers
        var layerMap = new Dictionary<(byte sortOrder, StringId bone), byte>();

        foreach (var (_, _, sortOrder, bone) in legacyPaths)
        {
            var key = (sortOrder, bone);
            if (!layerMap.ContainsKey(key))
            {
                var docLayerIndex = (byte)_documentLayers.Count;
                var name = $"Layer {_documentLayers.Count + 1}";
                if (EditorApplication.Config.TryGetSortOrder(sortOrder, out var sortDef))
                    name = sortDef.Label;
                _documentLayers.Add(new DocumentLayer
                {
                    Name = name,
                    SortOrder = sortOrder,
                    Bone = bone,
                });
                layerMap[key] = docLayerIndex;
            }
        }

        // Mark each path's target layer
        foreach (var (frameIndex, pathIndex, sortOrder, bone) in legacyPaths)
        {
            var key = (sortOrder, bone);
            legacyFrames[frameIndex].Shape.SetPathDocLayer(pathIndex, layerMap[key]);
        }

        // If no legacy layer info, create a default layer
        if (_documentLayers.Count == 0)
            _documentLayers.Add(new DocumentLayer { Name = "Layer 1" });

        // Distribute paths from legacy frames to per-layer frames
        DistributeLegacyFrames(legacyFrames, legacyFrameCount);
    }

    private void DistributeLegacyFrames(LayerFrame[] legacyFrames, ushort legacyFrameCount)
    {
        // Set frame counts
        foreach (var layer in _documentLayers)
            layer.FrameCount = Math.Max((ushort)1, legacyFrameCount);

        for (ushort fi = 0; fi < legacyFrameCount; fi++)
        {
            var srcShape = legacyFrames[fi].Shape;

            // Copy hold to all layers
            foreach (var layer in _documentLayers)
                layer.Frames[fi].Hold = legacyFrames[fi].Hold;

            // Distribute paths to per-layer shapes
            for (ushort pi = 0; pi < srcShape.PathCount; pi++)
            {
                ref readonly var path = ref srcShape.GetPath(pi);
                var layerIdx = Math.Min(path.DocLayer, (byte)(_documentLayers.Count - 1));
                var dstShape = _documentLayers[layerIdx].Frames[fi].Shape;

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

    private void ParsePathInLayer(LayerFrame f, ref Tokenizer tk)
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
    private void ParseLegacyPath(LayerFrame f, ushort frameIndex, ref Tokenizer tk,
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

        if (_documentLayers.Count == 0)
        {
            Bounds = new Rect(-0.5f, -0.5f, 1f, 1f);
            return;
        }

        // Update samples and bounds for all layer frames
        var first = true;
        var bounds = Rect.Zero;
        RasterBounds = RectInt.Zero;

        foreach (var layer in _documentLayers)
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

        if (_documentLayers.Count > 0)
            writer.WriteLine();

        // Layers are the top-level concept — each layer contains its own frames/paths
        for (var layerIndex = 0; layerIndex < _documentLayers.Count; layerIndex++)
        {
            var layer = _documentLayers[layerIndex];

            // Write layer definition with properties
            writer.Write($"layer \"{layer.Name}\"");
            if (layer.SortOrder != 0)
                writer.Write($" sort {layer.SortOrder}");
            if (layer.Type == DocumentLayerType.Generated)
                writer.Write(" generated");
            if (layer.Locked)
                writer.Write(" locked");
            if (!layer.Visible)
                writer.Write(" hidden");
            if (!layer.Bone.IsNone)
                writer.Write($" bone \"{layer.Bone}\"");
            if (layer.Opacity < 1.0f)
                writer.Write(string.Format(CultureInfo.InvariantCulture, " opacity {0}", layer.Opacity));
            writer.WriteLine();

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

            if (layerIndex < _documentLayers.Count - 1)
                writer.WriteLine();
        }
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

        var composite = GetCompositeShape(0);
        if (composite.PathCount == 0)
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
        CurrentDocumentLayer = src.CurrentDocumentLayer;

        Edges = src.Edges;
        Binding.CopyFrom(src.Binding);

        _documentLayers.Clear();
        _documentLayers.AddRange(src._documentLayers.Select(l => l.Clone()));
    }

    public override void LoadMetadata(PropertySet meta)
    {
        ShowInSkeleton = meta.GetBool("sprite", "show_in_skeleton", false);
        ShowTiling = meta.GetBool("sprite", "show_tiling", false);
        ShowSkeletonOverlay = meta.GetBool("sprite", "show_skeleton_overlay", false);
        ConstrainedSize = ParseConstrainedSize(meta.GetString("sprite", "constrained_size", ""));

        // Load per-layer generation params from meta (new format: [generate.layer0], [generate.layer1], ...)
        for (var i = 0; i < _documentLayers.Count; i++)
        {
            var layer = _documentLayers[i];
            if (layer.Type != DocumentLayerType.Generated)
                continue;
            var section = $"generate.layer{i}";
            layer.Prompt = meta.GetString(section, "prompt", layer.Prompt);
            layer.NegativePrompt = meta.GetString(section, "negative_prompt", layer.NegativePrompt);
            layer.Style = meta.GetString(section, "style", layer.Style);
            layer.Seed = meta.GetLong(section, "seed", layer.Seed);
            layer.Auto = meta.GetBool(section, "auto", layer.Auto);
        }

        // Legacy migration: old [generate] section at document level
        var legacyPrompt = meta.GetString("generate", "prompt", "");
        if (!string.IsNullOrEmpty(legacyPrompt) && !_documentLayers.Any(l => l.HasGeneration))
        {
            // Find or create a generated layer for the legacy config
            var genLayer = _documentLayers.FirstOrDefault(l => l.Type == DocumentLayerType.Generated);
            if (genLayer == null)
            {
                genLayer = new DocumentLayer { Name = "Generated", Type = DocumentLayerType.Generated };
                _documentLayers.Insert(0, genLayer); // insert at bottom
            }
            genLayer.Prompt = legacyPrompt;
            genLayer.NegativePrompt = meta.GetString("generate", "negative_prompt", "");
            genLayer.Style = meta.GetString("generate", "style", "");
            genLayer.Seed = meta.GetLong("generate", "seed", 0);
            genLayer.Auto = meta.GetBool("generate", "auto", false);
        }
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
        meta.ClearGroup("generate");  // Legacy cleanup - generation now per-layer

        // Save per-layer generation params
        for (var i = 0; i < _documentLayers.Count; i++)
        {
            var layer = _documentLayers[i];
            var section = $"generate.layer{i}";
            if (layer.Type == DocumentLayerType.Generated && layer.HasGeneration)
            {
                meta.SetString(section, "prompt", layer.Prompt);
                if (!string.IsNullOrEmpty(layer.NegativePrompt))
                    meta.SetString(section, "negative_prompt", layer.NegativePrompt);
                if (!string.IsNullOrEmpty(layer.Style))
                    meta.SetString(section, "style", layer.Style);
                if (layer.Seed != 0)
                    meta.SetLong(section, "seed", layer.Seed);
                if (layer.Auto)
                    meta.SetBool(section, "auto", true);
            }
            else
            {
                meta.ClearGroup(section);
            }
        }
    }

    public override void PostLoad()
    {
        Binding.Resolve();
        LoadGeneratedTextures();
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

    #region AI Generation

    private string GetGeneratedImagePath(int layerIndex) => Path + $".layer{layerIndex}.gen";

    /// <summary>Legacy path for migration.</summary>
    private string LegacyGeneratedImagePath => Path + ".gen";

    internal void LoadGeneratedTextures()
    {
        for (var i = 0; i < _documentLayers.Count; i++)
        {
            var layer = _documentLayers[i];
            if (layer.Type != DocumentLayerType.Generated)
                continue;

            layer.GeneratedTexture?.Dispose();
            layer.GeneratedTexture = null;

            // Try new path first, then legacy path for migration
            var genPath = GetGeneratedImagePath(i);
            if (!File.Exists(genPath))
            {
                // Migration: try legacy .gen file for the first generated layer
                var legacyPath = LegacyGeneratedImagePath;
                if (File.Exists(legacyPath))
                    genPath = legacyPath;
                else
                    continue;
            }

            try
            {
                using var srcImage = SixLabors.ImageSharp.Image.Load<Rgba32>(genPath);
                var w = srcImage.Width;
                var h = srcImage.Height;
                var pixels = new byte[w * h * 4];
                srcImage.CopyPixelDataTo(pixels);
                layer.GeneratedTexture = Texture.Create(w, h, pixels, TextureFormat.RGBA8, TextureFilter.Linear, $"{Name}_gen{i}");
                Log.Info($"Loaded generated texture for '{Name}' layer {i} ({w}x{h})");
            }
            catch (Exception ex)
            {
                Log.Error($"Failed to load generated texture for '{Name}' layer {i}: {ex.Message}");
            }
        }
    }

    private string ComputeGenerationHash(DocumentLayer layer)
    {
        var input = $"{layer.Prompt}|{layer.NegativePrompt}|{layer.Style}|{layer.Seed}";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes);
    }



    private bool TryBlitGeneratedImage(int layerIndex, PixelData<Color32> image, in AtlasSpriteRect rect, int padding)
    {
        var genPath = GetGeneratedImagePath(layerIndex);
        if (!File.Exists(genPath))
        {
            // Migration: try legacy path
            genPath = LegacyGeneratedImagePath;
        }
        if (!File.Exists(genPath))
            return false;

        try
        {
            using var srcImage = SixLabors.ImageSharp.Image.Load<Rgba32>(genPath);
            var padding2 = padding * 2;

            // Resize to fit the atlas rect (sprite's raster bounds + padding)
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

            // Blit pixels into the atlas image
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    var pixel = srcImage[x, y];
                    image[rasterRect.X + x, rasterRect.Y + y] = new Color32(pixel.R, pixel.G, pixel.B, pixel.A);
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
            Log.Error($"Failed to load generated image '{genPath}': {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Rasterizes the sprite's vector paths (colored silhouette) to a PNG byte array for the generation API.
    /// </summary>
    private byte[] RasterizeSilhouetteToPng()
    {
        UpdateBounds();
        var dpi = EditorApplication.Config.PixelsPerUnit;
        var w = RasterBounds.Size.X;
        var h = RasterBounds.Size.Y;
        if (w <= 0 || h <= 0)
            return [];

        using var pixels = new PixelData<Color32>(w, h);
        var targetRect = new RectInt(0, 0, w, h);
        var sourceOffset = -RasterBounds.Position;

        var compositeShape = GetCompositeShape(0);
        var slots = GetMeshSlots(0);
        foreach (var slot in slots)
        {
            if (slot.PathIndices.Count > 0)
                RasterizeSlot(compositeShape, slot, pixels, targetRect, sourceOffset, dpi);
        }

        // Convert to ImageSharp image: composite path colors over white background
        using var img = new Image<Rgba32>(w, h);
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                var c = pixels[x, y];
                if (c.A == 0)
                {
                    img[x, y] = new Rgba32(255, 255, 255, 255);
                }
                else
                {
                    float a = c.A / 255f;
                    byte r = (byte)(c.R * a + 255 * (1 - a));
                    byte g = (byte)(c.G * a + 255 * (1 - a));
                    byte b = (byte)(c.B * a + 255 * (1 - a));
                    img[x, y] = new Rgba32(r, g, b, 255);
                }
            }
        }

        using var ms = new MemoryStream();
        img.SaveAsPng(ms);
        var pngBytes = ms.ToArray();

        // Debug: write silhouette to tmp folder for inspection
        try
        {
            var tmpDir = System.IO.Path.Combine(EditorApplication.ProjectPath, "tmp");
            Directory.CreateDirectory(tmpDir);
            File.WriteAllBytes(System.IO.Path.Combine(tmpDir, $"{Name}_silhouette.png"), pngBytes);
        }
        catch { }

        return pngBytes;
    }

    public void GenerateAsync(int layerIndex = -1)
    {
        // Find the target generated layer
        if (layerIndex < 0)
        {
            layerIndex = CurrentDocumentLayer;
            if (layerIndex < 0 || layerIndex >= _documentLayers.Count ||
                _documentLayers[layerIndex].Type != DocumentLayerType.Generated)
            {
                // Find first generated layer
                layerIndex = _documentLayers.FindIndex(l => l.Type == DocumentLayerType.Generated);
            }
        }

        if (layerIndex < 0 || layerIndex >= _documentLayers.Count)
        {
            Log.Error($"No generated layer found for '{Name}'");
            return;
        }

        var genLayer = _documentLayers[layerIndex];
        if (genLayer.IsGenerating)
            return;

        if (!ConstrainedSize.HasValue)
        {
            Log.Error($"Generation requires a sprite size constraint for '{Name}'");
            return;
        }

        genLayer.IsGenerating = true;

        // Rasterize silhouette on the main thread (needs access to shape data)
        var silhouetteBytes = RasterizeSilhouetteToPng();
        if (silhouetteBytes.Length == 0)
        {
            Log.Error("Cannot generate: sprite has no visible shapes");
            genLayer.IsGenerating = false;
            return;
        }

        var silhouetteBase64 = $"data:image/png;base64,{Convert.ToBase64String(silhouetteBytes)}";

        // Build the request inputs
        var inputs = new Dictionary<string, string>
        {
            ["sketch"] = silhouetteBase64,
            ["prompt"] = genLayer.Prompt
        };

        // Load style reference texture for ipadapter if specified
        if (!string.IsNullOrEmpty(genLayer.Style))
        {
            var styleDoc = DocumentManager.Find(AssetType.Texture, genLayer.Style) as TextureDocument;
            if (styleDoc != null && File.Exists(styleDoc.Path))
            {
                var styleBytes = File.ReadAllBytes(styleDoc.Path);
                inputs["style_ref"] = $"data:image/png;base64,{Convert.ToBase64String(styleBytes)}";
            }
            else
            {
                Log.Warning($"Style texture '{genLayer.Style}' not found");
            }
        }

        // Build pipeline: generate → remove_background
        var nodes = new List<GenerationNode>
        {
            new()
            {
                Id = "gen",
                Type = "generate",
                Properties = BuildGenerateNodeProperties(genLayer)
            },
            new()
            {
                Id = "result",
                Type = "remove_background",
                Properties = new Dictionary<string, JsonElement>
                {
                    ["image"] = JsonSerializer.SerializeToElement("@gen")
                }
            }
        };

        var server = EditorApplication.Config?.GenerationServer ?? "http://127.0.0.1:7860";
        var request = new GenerationRequest
        {
            Server = server,
            Nodes = nodes,
            Output = "@result",
            Inputs = inputs
        };

        var capturedLayerIndex = layerIndex;
        Log.Info($"Starting generation for '{Name}' layer {capturedLayerIndex} on {server}...");

        GenerationClient.Generate(request, response =>
        {
            genLayer.IsGenerating = false;

            if (response == null)
            {
                Log.Error($"Generation failed for '{Name}'");
                return;
            }

            try
            {
                // Decode base64 image and save to .gen file
                var imageBytes = Convert.FromBase64String(response.Image);
                File.WriteAllBytes(GetGeneratedImagePath(capturedLayerIndex), imageBytes);

                // Debug: write to tmp folder for inspection
                var tmpDir = System.IO.Path.Combine(EditorApplication.ProjectPath, "tmp");
                Directory.CreateDirectory(tmpDir);
                var debugPath = System.IO.Path.Combine(tmpDir, $"{Name}_gen{capturedLayerIndex}.png");
                File.WriteAllBytes(debugPath, imageBytes);
                Log.Info($"Debug: wrote generated image to {debugPath} ({imageBytes.Length} bytes)");

                // Update seed if it was random
                if (genLayer.Seed == 0 && response.Seed != 0)
                {
                    genLayer.Seed = response.Seed;
                    MarkMetaModified();
                }

                Log.Info($"Generation complete for '{Name}' ({response.Width}x{response.Height}, seed={response.Seed})");

                // Load the generated image as a standalone texture for editor preview
                LoadGeneratedTextures();
            }
            catch (Exception ex)
            {
                Log.Error($"Failed to save generated image for '{Name}': {ex.Message}");
            }
        });
    }

    private Dictionary<string, JsonElement> BuildGenerateNodeProperties(DocumentLayer genLayer)
    {
        var props = new Dictionary<string, JsonElement>
        {
            ["model"] = JsonSerializer.SerializeToElement("sdxl-base"),
            ["prompt"] = JsonSerializer.SerializeToElement("@input.prompt"),
            ["negative_prompt"] = JsonSerializer.SerializeToElement(
                string.IsNullOrEmpty(genLayer.NegativePrompt)
                    ? "blurry, low quality, 3d render, photorealistic"
                    : genLayer.NegativePrompt),
        };

        var controlnet = new Dictionary<string, object>
        {
            ["model"] = "scribble",
            ["image"] = "@input.sketch",
            ["strength"] = 0.3
        };
        props["controlnet"] = JsonSerializer.SerializeToElement(controlnet);

        if (!string.IsNullOrEmpty(genLayer.Style))
        {
            var ipadapter = new Dictionary<string, object>
            {
                ["image"] = "@input.style_ref",
                ["strength"] = 0.7
            };
            props["ipadapter"] = JsonSerializer.SerializeToElement(ipadapter);
        }

        if (genLayer.Seed != 0)
            props["seed"] = JsonSerializer.SerializeToElement(genLayer.Seed);

        return props;
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

        var composite = GetCompositeShape(frameIndex);
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

            // Check if any document layer in this slot is a generated layer
            var blittedGenerated = false;
            foreach (var docLayerIdx in slot.DocLayers)
            {
                if (docLayerIdx < _documentLayers.Count && _documentLayers[docLayerIdx].Type == DocumentLayerType.Generated)
                {
                    if (TryBlitGeneratedImage(docLayerIdx, image, rect, padding))
                    {
                        blittedGenerated = true;
                        break;
                    }
                }
            }

            if (!blittedGenerated && slot.PathIndices.Count > 0)
                RasterizeSlot(composite, slot, image, targetRect, sourceOffset, dpi);

            // Bleed RGB into transparent pixels to prevent fringing with linear filtering.
            image.BleedColors(targetRect);

            xOffset += slotWidth;
        }
    }

    private static void RasterizeSlot(
        Shape shape,
        MeshSlot slot,
        PixelData<Color32> image,
        RectInt targetRect,
        Vector2Int sourceOffset,
        int dpi)
    {
        // Collect subtract paths with their indices — each only affects paths below it
        List<(ushort PathIndex, Clipper2Lib.PathsD Contours)>? subtractEntries = null;
        foreach (var pi in slot.PathIndices)
        {
            ref readonly var path = ref shape.GetPath(pi);
            if (!path.IsSubtract || path.AnchorCount < 3) continue;

            var subShape = new Msdf.Shape();
            Msdf.ShapeClipper.AppendContour(subShape, shape, pi);
            var subContours = Msdf.ShapeClipper.ShapeToPaths(subShape, 8);
            if (subContours.Count > 0)
            {
                subtractEntries ??= new();
                subtractEntries.Add((pi, subContours));
            }
        }

        // Rasterize each non-subtract path: stroke first, then fill on top
        // Track accumulated geometry for clip operations
        Clipper2Lib.PathsD? accumulatedPaths = null;

        foreach (var pi in slot.PathIndices)
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
                // Clip: intersect with accumulated geometry below
                if (accumulatedPaths is not { Count: > 0 }) continue;
                contours = Clipper2Lib.Clipper.BooleanOp(Clipper2Lib.ClipType.Intersection,
                    contours, accumulatedPaths, Clipper2Lib.FillRule.NonZero, precision: 6);
                if (contours.Count == 0) continue;
            }
            else
            {
                // Normal path: add fill area to accumulated geometry for future clips
                // Use contracted contours (excluding stroke) so clip paths don't cover strokes
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

            // Apply subtract paths that are above this path (higher index = on top)
            if (subtractEntries != null)
            {
                Clipper2Lib.PathsD? subtractPaths = null;
                foreach (var (subIdx, subContours) in subtractEntries)
                {
                    if (subIdx <= pi) continue;
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
                    // Stroke behind fill: rasterize full shape with stroke color
                    Rasterizer.Fill(contours, image, targetRect, sourceOffset, dpi, path.StrokeColor);
                    // Then fill with contracted shape on top
                    if (contracted.Count > 0)
                        Rasterizer.Fill(contracted, image, targetRect, sourceOffset, dpi, path.FillColor);
                }
                else
                {
                    // Stroke only: rasterize just the ring
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
                var composite = GetCompositeShape(ts);
                var slotBounds = composite.GetRasterBoundsFor(slot.DocLayers);
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
        var composite = GetCompositeShape(timeSlot);
        foreach (var slot in slots)
            result.Add(composite.GetRasterBoundsFor(slot.DocLayers));
        return result;
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
        var shape = GetCompositeShape(timeSlot);

        EnsureDefaultLayer();

        // Collect subtract paths first — they'll be appended to all slots
        var subtractPaths = new List<ushort>();
        for (ushort i = 0; i < shape.PathCount; i++)
        {
            if (shape.GetPath(i).IsSubtract)
                subtractPaths.Add(i);
        }

        // Iterate document layers in order, collecting paths per layer.
        // Auto-merge adjacent layers with same (SortOrder, Bone) into one MeshSlot.
        MeshSlot? currentSlot = null;

        for (var layerIdx = 0; layerIdx < _documentLayers.Count; layerIdx++)
        {
            var docLayer = _documentLayers[layerIdx];
            var sortOrder = docLayer.SortOrder;
            var bone = docLayer.Bone;

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

            currentSlot.DocLayers.Add((byte)layerIdx);

            // Add paths belonging to this document layer
            for (ushort pi = 0; pi < shape.PathCount; pi++)
            {
                ref readonly var path = ref shape.GetPath(pi);
                if (path.IsSubtract) continue; // handled separately
                if (path.DocLayer != layerIdx) continue;
                currentSlot.PathIndices.Add(pi);
            }
        }

        // Append subtract paths to all slots
        foreach (var slot in slots)
        {
            foreach (var subtractIdx in subtractPaths)
                slot.PathIndices.Add(subtractIdx);
        }

        return slots;
    }
}
