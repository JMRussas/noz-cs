//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Globalization;
using System.Numerics;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace NoZ.Editor;

public partial class GenSpriteDocument : Document, IShapeDocument
{
    public static readonly AssetType AssetTypeGenSprite = AssetType.FromString("GNSP");

    public override bool CanSave => true;

    public const int MaxDocumentLayers = 32;

    private readonly List<GenSpriteLayer> _layers = new();

    public Vector2Int ConstrainedSize { get; set; } = new(256, 256);
    public GenerationImage Generation { get; } = new();
    public GenerationConfig? Refine;
    public string? StyleName;
    public GenStyleDocument? Style;
    public int ActiveLayerIndex;

    public IReadOnlyList<GenSpriteLayer> Layers => _layers;
    public GenSpriteLayer ActiveLayer => _layers[ActiveLayerIndex];
    public bool IsActiveLayerLocked => false; // GenSprite layers are never locked

    public bool HasGeneration => _layers.Any(l => l.Generation.HasPrompt);
    public bool IsGenerating => Generation.IsGenerating;

    public static void RegisterDef()
    {
        DocumentManager.RegisterDef(new DocumentDef
        {
            Type = AssetTypeGenSprite,
            Name = "GenSprite",
            Extension = ".gensprite",
            Factory = () => new GenSpriteDocument(),
            EditorFactory = doc => new GenSpriteEditor((GenSpriteDocument)doc),
            NewFile = NewFile,
            Icon = () => EditorAssets.Sprites.AssetIconSprite
        });
    }

    public GenSpriteDocument()
    {
        IsEditorOnly = true;
        _layers.Add(new GenSpriteLayer { Name = "Layer 1", Index = 0 });
    }

    private static void NewFile(StreamWriter writer)
    {
        writer.WriteLine("layer \"Layer 1\"");
        writer.WriteLine("generate");
        writer.WriteLine("prompt \"\"");
    }

    #region Load / Save

    public override void Load()
    {
        var contents = File.ReadAllText(Path);
        var tk = new Tokenizer(contents);
        Parse(ref tk);
        UpdateBounds();
        Loaded = true;
    }

    public override void PostLoad()
    {
        LoadGeneratedTexture();

        if (!string.IsNullOrEmpty(StyleName))
            Style = DocumentManager.Find(GenStyleDocument.AssetTypeGenStyle, StyleName) as GenStyleDocument;
    }

    private void Parse(ref Tokenizer tk)
    {
        _layers.Clear();

        while (!tk.IsEOF)
        {
            if (tk.ExpectIdentifier("layer"))
            {
                ParseLayer(ref tk);
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
                Log.Error($"GenSpriteDocument.Load: Unexpected token '{tk.GetString(badToken)}'");
                break;
            }
        }

        if (_layers.Count == 0)
            _layers.Add(new GenSpriteLayer { Name = "Layer 1", Index = 0 });
    }

    private void ParseLayer(ref Tokenizer tk)
    {
        var layer = new GenSpriteLayer
        {
            Name = tk.ExpectQuotedString() ?? $"Layer {_layers.Count + 1}",
            Index = _layers.Count,
        };
        _layers.Add(layer);

        // Parse generation config (mandatory for gensprite layers)
        if (tk.ExpectIdentifier("generate"))
            layer.Generation = ParseGenerationConfig(ref tk);

        // Parse mask paths
        while (!tk.IsEOF && tk.ExpectIdentifier("path"))
            ParsePath(layer.Shape, ref tk);
    }

    private static void ParsePath(Shape shape, ref Tokenizer tk)
    {
        var pathIndex = shape.AddPath(Color32.White);
        var operation = PathOperation.Normal;

        while (!tk.IsEOF)
        {
            if (tk.ExpectIdentifier("subtract"))
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
            {
                var x = tk.ExpectFloat();
                var y = tk.ExpectFloat();
                var curve = tk.ExpectFloat();
                shape.AddAnchor(pathIndex, new Vector2(x, y), curve);
            }
            else
                break;
        }

        if (operation != PathOperation.Normal)
            shape.SetPathOperation(pathIndex, operation);
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

    public override void Save(StreamWriter writer)
    {
        for (var layerIndex = 0; layerIndex < _layers.Count; layerIndex++)
        {
            var layer = _layers[layerIndex];

            writer.WriteLine($"layer \"{layer.Name}\"");

            // Generation config (always present)
            writer.WriteLine("generate");
            WriteGenerationConfig(writer, layer.Generation);

            // Mask paths
            SaveMaskPaths(layer.Shape, writer);

            if (layerIndex < _layers.Count - 1)
                writer.WriteLine();
        }

        // Document-level generation image
        if (Generation.HasImageData)
        {
            writer.WriteLine();
            writer.WriteLine($"gen_image \"{Convert.ToBase64String(Generation.ImageData!)}\"");
        }

        // Refine config
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

    private static void SaveMaskPaths(Shape shape, StreamWriter writer)
    {
        for (ushort pIdx = 0; pIdx < shape.PathCount; pIdx++)
        {
            ref readonly var path = ref shape.GetPath(pIdx);

            writer.WriteLine("path");
            if (path.IsSubtract)
                writer.WriteLine("subtract true");
            if (path.IsClip)
                writer.WriteLine("clip true");

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

    #endregion

    #region Metadata

    public override void LoadMetadata(PropertySet meta)
    {
        var sizeStr = meta.GetString("gensprite", "constrained_size", "256x256");
        var parts = sizeStr.Split('x');
        if (parts.Length == 2 &&
            int.TryParse(parts[0], out var w) &&
            int.TryParse(parts[1], out var h))
        {
            ConstrainedSize = new Vector2Int(w, h);
        }

        var style = meta.GetString("gensprite", "style", "");
        StyleName = string.IsNullOrEmpty(style) ? null : style;
    }

    public override void SaveMetadata(PropertySet meta)
    {
        meta.SetString("gensprite", "constrained_size", $"{ConstrainedSize.X}x{ConstrainedSize.Y}");

        if (!string.IsNullOrEmpty(StyleName))
            meta.SetString("gensprite", "style", StyleName);
    }

    #endregion

    #region Bounds

    public void UpdateBounds()
    {
        var cs = ConstrainedSize;
        var ppu = EditorApplication.Config.PixelsPerUnitInv;
        Bounds = new Rect(
            cs.X * ppu * -0.5f,
            cs.Y * ppu * -0.5f,
            cs.X * ppu,
            cs.Y * ppu);

        // Update all layer shape samples
        foreach (var layer in _layers)
        {
            layer.Shape.UpdateSamples();
            layer.Shape.UpdateBounds();
        }
    }

    #endregion

    #region Layers

    public int AddLayer()
    {
        if (_layers.Count >= MaxDocumentLayers)
            return -1;

        var name = $"Layer {_layers.Count + 1}";
        _layers.Add(new GenSpriteLayer { Name = name, Index = _layers.Count });
        ActiveLayerIndex = _layers.Count - 1;
        return ActiveLayerIndex;
    }

    public void RemoveLayer(int index)
    {
        if (index < 0 || index >= _layers.Count || _layers.Count <= 1)
            return;

        _layers[index].Dispose();
        _layers.RemoveAt(index);

        for (var i = index; i < _layers.Count; i++)
            _layers[i].Index = i;

        if (ActiveLayerIndex >= _layers.Count)
            ActiveLayerIndex = _layers.Count - 1;

        UpdateBounds();
    }

    public void MoveLayer(int fromIndex, int toIndex)
    {
        if (fromIndex < 0 || fromIndex >= _layers.Count ||
            toIndex < 0 || toIndex >= _layers.Count ||
            fromIndex == toIndex)
            return;

        var layer = _layers[fromIndex];
        _layers.RemoveAt(fromIndex);
        _layers.Insert(toIndex, layer);

        for (var i = 0; i < _layers.Count; i++)
            _layers[i].Index = i;

        if (ActiveLayerIndex == fromIndex)
            ActiveLayerIndex = toIndex;
        else if (fromIndex < toIndex && ActiveLayerIndex > fromIndex && ActiveLayerIndex <= toIndex)
            ActiveLayerIndex--;
        else if (fromIndex > toIndex && ActiveLayerIndex >= toIndex && ActiveLayerIndex < fromIndex)
            ActiveLayerIndex++;

        UpdateBounds();
    }

    #endregion

    #region Generation

    internal void LoadGeneratedTexture()
    {
        Generation.Dispose();

        if (!Generation.HasImageData) return;

        try
        {
            using var ms = new MemoryStream(Generation.ImageData!);
            using var srcImage = SixLabors.ImageSharp.Image.Load<Rgba32>(ms);

            var cs = ConstrainedSize;
            if (srcImage.Width != cs.X || srcImage.Height != cs.Y)
                srcImage.Mutate(x => x.Resize(cs.X, cs.Y));

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

    private byte[] RasterizeMaskToPng(int targetLayerIndex)
    {
        UpdateBounds();
        var dpi = EditorApplication.Config.PixelsPerUnit;
        var cs = ConstrainedSize;
        var w = cs.X;
        var h = cs.Y;
        if (w <= 0 || h <= 0)
            return [];

        using var pixels = new PixelData<Color32>(w, h);
        pixels.Clear(new Color32(0, 0, 0, 255));
        var targetRect = new RectInt(0, 0, w, h);
        var sourceOffset = new Vector2Int(cs.X / 2, cs.Y / 2);
        var white = new Color32(255, 255, 255, 255);

        var layer = _layers[targetLayerIndex];
        var shape = layer.Shape;

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

        try
        {
            var tmpDir = System.IO.Path.Combine(EditorApplication.ProjectPath, "tmp");
            Directory.CreateDirectory(tmpDir);
            File.WriteAllBytes(System.IO.Path.Combine(tmpDir, $"{Name}_mask_L{targetLayerIndex}.png"), pngBytes);
        }
        catch { }

        return pngBytes;
    }

    private GenerationConfig? GetStyleRefineConfig()
    {
        if (Style == null) return null;
        return new GenerationConfig
        {
            Prompt = Style.RefinePrompt,
            NegativePrompt = Style.RefineNegativePrompt,
            Steps = Style.RefineSteps,
            Strength = Style.RefineStrength,
            GuidanceScale = Style.RefineGuidanceScale,
        };
    }

    public void GenerateAsync()
    {
        var genLayers = new List<(int Index, GenSpriteLayer Layer)>();
        for (int i = 0; i < _layers.Count; i++)
        {
            if (_layers[i].Generation.HasPrompt)
                genLayers.Add((i, _layers[i]));
        }

        if (genLayers.Count == 0)
        {
            Log.Error($"No generated layers with prompts found for '{Name}'");
            return;
        }

        if (Generation.IsGenerating)
            return;

        var globalPrompt = Style?.Prompt ?? "";
        var globalNegPrompt = Style?.NegativePrompt ?? "";

        var shapes = new List<GenerationShape>();
        foreach (var (layerIndex, layer) in genLayers)
        {
            var gen = layer.Generation;
            var maskBytes = RasterizeMaskToPng(layerIndex);

            var prompt = string.IsNullOrEmpty(globalPrompt) ? gen.Prompt : $"{gen.Prompt}, {globalPrompt}";
            var negPrompt = gen.NegativePrompt;
            if (!string.IsNullOrEmpty(globalNegPrompt))
                negPrompt = string.IsNullOrEmpty(negPrompt) ? globalNegPrompt : $"{negPrompt}, {globalNegPrompt}";

            shapes.Add(new GenerationShape
            {
                Mask = maskBytes.Length > 0 ? $"data:image/png;base64,{Convert.ToBase64String(maskBytes)}" : null,
                Prompt = prompt,
                NegativePrompt = string.IsNullOrEmpty(negPrompt) ? null : negPrompt,
                Strength = gen.Strength,
                Steps = gen.Steps,
                GuidanceScale = gen.GuidanceScale,
            });
        }

        var server = EditorApplication.Config?.GenerationServer ?? "http://127.0.0.1:7860";
        var primaryGen = genLayers[0].Layer.Generation;
        var refine = Refine ?? GetStyleRefineConfig() ?? primaryGen;

        var refinePrompt = string.IsNullOrEmpty(globalPrompt) ? refine.Prompt : $"{refine.Prompt}, {globalPrompt}";
        var refineNegPrompt = refine.NegativePrompt;
        if (!string.IsNullOrEmpty(globalNegPrompt))
            refineNegPrompt = string.IsNullOrEmpty(refineNegPrompt) ? globalNegPrompt : $"{refineNegPrompt}, {globalNegPrompt}";

        var request = new GenerationRequest
        {
            Server = server,
            Shapes = shapes,
            Refine = new GenerationRefine
            {
                Prompt = refinePrompt,
                NegativePrompt = string.IsNullOrEmpty(refineNegPrompt) ? null : refineNegPrompt,
                Strength = refine.Strength,
                Steps = refine.Steps,
                GuidanceScale = refine.GuidanceScale,
            },
            Seed = primaryGen.Seed == 0 ? Random.Shared.NextInt64(1, long.MaxValue) : primaryGen.Seed,
            StyleReferences = Style != null ? GenerationClient.LoadStyleReferences(Style.StyleReferences) : null,
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

                        var cs = ConstrainedSize;
                        if (srcImage.Width != cs.X || srcImage.Height != cs.Y)
                            srcImage.Mutate(x => x.Resize(cs.X, cs.Y));

                        using var outMs = new MemoryStream();
                        srcImage.SaveAsPng(outMs);
                        genImage.ImageData = outMs.ToArray();

                        genImage.Dispose();
                        var rw = srcImage.Width;
                        var rh = srcImage.Height;
                        var px = new byte[rw * rh * 4];
                        srcImage.CopyPixelDataTo(px);
                        genImage.Texture = Texture.Create(rw, rh, px, TextureFormat.RGBA8, TextureFilter.Linear, $"{Name}_gen");

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

    #region Drawing

    public override void Draw()
    {
        DrawOrigin();

        var texture = Generation.Texture;
        if (texture != null)
        {
            var ppu = EditorApplication.Config.PixelsPerUnitInv;
            var cs = ConstrainedSize;
            var rect = new Rect(
                cs.X * ppu * -0.5f,
                cs.Y * ppu * -0.5f,
                cs.X * ppu,
                cs.Y * ppu);

            using (Graphics.PushState())
            {
                Graphics.SetTransform(Transform);
                Graphics.SetTexture(texture);
                Graphics.SetShader(EditorAssets.Shaders.Texture);
                Graphics.SetColor(Color.White.WithAlpha(Workspace.XrayAlpha));
                Graphics.Draw(rect);
            }
        }
        else
        {
            DrawBounds();
        }
    }

    #endregion

    public override void Dispose()
    {
        Generation.Dispose();
        foreach (var layer in _layers)
            layer.Dispose();
        base.Dispose();
    }

    public override void Clone(Document source)
    {
        if (source is not GenSpriteDocument src) return;

        _layers.Clear();
        foreach (var layer in src._layers)
            _layers.Add(layer.Clone());

        ActiveLayerIndex = src.ActiveLayerIndex;
        ConstrainedSize = src.ConstrainedSize;
        Refine = src.Refine?.Clone();

        if (src.Generation.HasImageData)
            Generation.ImageData = (byte[])src.Generation.ImageData!.Clone();
    }
}
