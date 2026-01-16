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
        LoadFromTokenizer(ref tk);
        UpdateBounds();
        Loaded = true;
    }

    private void LoadFromTokenizer(ref Tokenizer tk)
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

        for (ushort fi = 0; fi < FrameCount; fi++)
        {
            Frames[fi].Shape.UpdateSamples();
            Frames[fi].Shape.UpdateBounds();
        }
    }

    private static void ParsePath(SpriteFrame f, ref Tokenizer tk)
    {
        var pathIndex = f.Shape.AddPath();
        byte fillColor = 0;

        while (!tk.IsEOF)
        {
            if (tk.ExpectIdentifier("c"))
            {
                fillColor = (byte)tk.ExpectInt();
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
    }

    public override void Save(string path)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"c {Palette}");
        sb.AppendLine();

        for (ushort frameIndex = 0; frameIndex < FrameCount; frameIndex++)
        {
            var f = GetFrame(frameIndex);

            if (FrameCount > 1 || f.Hold > 0)
            {
                sb.Append('f');
                if (f.Hold > 0)
                    sb.Append($" h {f.Hold}");
                sb.AppendLine();
            }

            SaveFrame(f, sb);

            if (frameIndex < FrameCount - 1)
                sb.AppendLine();
        }        
        File.WriteAllText(path, sb.ToString());
    }
    
    private static void SaveFrame(SpriteFrame f, StringBuilder sb)
    {
        var shape = f.Shape;

        for (ushort pIdx = 0; pIdx < shape.PathCount; pIdx++)
        {
            var path = shape.GetPath(pIdx);

            sb.AppendLine($"p c {path.FillColor}");

            for (ushort aIdx = 0; aIdx < path.AnchorCount; aIdx++)
            {
                var anchor = shape.GetAnchor((ushort)(path.AnchorStart + aIdx));
                sb.Append(string.Format(CultureInfo.InvariantCulture, "a {0} {1}", anchor.Position.X, anchor.Position.Y));
                if (MathF.Abs(anchor.Curve) > float.Epsilon)
                    sb.Append(string.Format(CultureInfo.InvariantCulture, " {0}", anchor.Curve));
                sb.AppendLine();
            }

            sb.AppendLine();
        }
    }

    public override void Draw()
    {
        // TODO: Draw from atlas when available
        // For now, draw a simple placeholder quad at the sprite's bounds
        var size = Bounds.Size;
        if (size.X <= 0 || size.Y <= 0)
            return;

        Render.SetColor(new Color(200/255f, 200/255f, 200/255f, 1f));
        Render.Draw(
            Position.X + Bounds.X,
            Position.Y + Bounds.Y,
            size.X, size.Y
        );
    }

    public override void Import(string outputPath, PropertySet config, PropertySet meta)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(outputPath) ?? "");

        using var writer = new BinaryWriter(File.Create(outputPath));

        writer.Write(Constants.AssetSignature);
        writer.Write((byte)AssetType.Sprite);
        writer.Write((ushort)Sprite.Version);
        writer.Write((ushort)0);
    }
}
