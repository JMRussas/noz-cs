//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Globalization;
using System.Numerics;
using System.Text;

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
    public readonly SpriteFrame[] Frames = new SpriteFrame[Sprite.MaxFrames];
    public ushort FrameCount;
    public byte Palette;
    public float Depth;
    public RectInt RasterBounds { get; private set; }

    internal AtlasDocument? Atlas;
    internal Rect AtlasUV;

    public SpriteDocument()
    {
        for (var i = 0; i < Frames.Length; i++)
            Frames[i] = new SpriteFrame();
    }

    public static void RegisterDef()
    {
        DocumentManager.RegisterDef(new DocumentDef(
            AssetType.Sprite,
            ".sprite",
            () => new SpriteDocument(),
            doc => new SpriteEditor((SpriteDocument)doc)
        ));
    }

    public SpriteFrame GetFrame(ushort frameIndex) => Frames[frameIndex];

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
            }
            else if (tk.ExpectIdentifier("d"))
            {
                tk.ExpectFloat();
            }
            else if (tk.ExpectIdentifier("f"))
            {
                if (tk.ExpectIdentifier("h"))
                    f.Hold = tk.ExpectInt();
                f = Frames[FrameCount++];
            }
            else if (tk.ExpectIdentifier("s"))
            {
                tk.ExpectQuotedString(out _);
            }
            else if (tk.ExpectIdentifier("h"))
            {
                f.Hold = tk.ExpectInt();
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
        var position = Vector2.Zero;
        var rotation = 0f;
        var scale = Vector2.One;

        while (!tk.IsEOF)
        {
            if (tk.ExpectIdentifier("c"))
            {
                fillColor = (byte)tk.ExpectInt();
            }
            else if (tk.ExpectIdentifier("t"))
            {
                // Path transform: t <posX> <posY> <rotation> <scaleX> <scaleY>
                position.X = tk.ExpectFloat();
                position.Y = tk.ExpectFloat();
                rotation = tk.ExpectFloat();
                scale.X = tk.ExpectFloat();
                scale.Y = tk.ExpectFloat();
            }
            else if (tk.ExpectIdentifier("a"))
            {
                ParseAnchor(f.Shape, pathIndex, ref tk);
            }
            else
            {
                break;
            }
        }

        f.Shape.SetPathFillColor(pathIndex, fillColor);
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
        if (FrameCount <= 0)
            return;

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

        RasterBounds = Frames[0].Shape.RasterBounds;

        for (ushort fi = 0; fi < FrameCount; fi++)
        {
            Frames[fi].Shape.UpdateSamples();
            Frames[fi].Shape.UpdateBounds();
            RasterBounds = RasterBounds.Union(Frames[fi].Shape.RasterBounds);
        }


        Bounds = RasterBounds.ToRect().Scale(1.0f / EditorApplication.Config.PixelsPerUnit);
    }

    public override void Save(StreamWriter writer)
    {
        writer.WriteLine($"c {Palette}");
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

            writer.WriteLine($"p c {path.FillColor}");

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
        var size = Bounds.Size;
        if (size.X <= 0 || size.Y <= 0 || Atlas == null)
            return;

        using (Graphics.PushState())
        {
            ref var frame0 = ref Frames[0];
            Graphics.SetTexture(Atlas.Texture);
            Graphics.SetShader(EditorAssets.Shaders.Texture);
            Graphics.SetColor(Color.White);
            Graphics.Draw(
                frame0.Shape.RasterBounds.ToRect().Scale(Graphics.PixelsPerUnitInv),
                AtlasUV);
        }
    }

    public override void Clone(Document source)
    {
        var src = (SpriteDocument)source;
        FrameCount = src.FrameCount;
        Palette = src.Palette;
        Depth = src.Depth;
        Bounds = src.Bounds;

        for (var i = 0; i < src.FrameCount; i++)
        {
            Frames[i].Shape.CopyFrom(src.Frames[i].Shape);
            Frames[i].Hold = src.Frames[i].Hold;
        }

        for (var i = src.FrameCount; i < Sprite.MaxFrames; i++)
            Frames[i].Shape.Clear();
    }

    public override void Import(string outputPath, PropertySet config, PropertySet meta)
    {
        UpdateBounds();

        using var writer = new BinaryWriter(File.Create(outputPath));
        writer.WriteAssetHeader(AssetType.Sprite, Sprite.Version, 0);
        writer.Write(FrameCount);
        writer.Write((ushort)(Atlas?.Index ?? 0));
        writer.Write((short)RasterBounds.Left);
        writer.Write((short)RasterBounds.Top);
        writer.Write((short)RasterBounds.Right);
        writer.Write((short)RasterBounds.Bottom);
        writer.Write(AtlasUV.Left);
        writer.Write(AtlasUV.Top);
        writer.Write(AtlasUV.Right);
        writer.Write(AtlasUV.Bottom);
        writer.Write((float)EditorApplication.Config.PixelsPerUnit);
        writer.Write((byte)TextureFilter.Point);
    }
}
