//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Globalization;
using System.Numerics;
using System.Text;

namespace noz.editor;

public static class SpriteConstants
{
    public const int MaxFrames = 64;
}

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
    public readonly SpriteFrame[] Frames = new SpriteFrame[SpriteConstants.MaxFrames];
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
        DocumentDef.Register(new DocumentDef(
            AssetType.Sprite,
            ".sprite",
            () => new SpriteDocument()
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
        shape.AddAnchorToPath(pathIndex, new Vector2(x, y), curve);
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
        SaveToStringBuilder(sb);
        File.WriteAllText(path, sb.ToString());
    }

    private void SaveToStringBuilder(StringBuilder sb)
    {
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

        Render.DrawQuad(
            Position.X + Bounds.X,
            Position.Y + Bounds.Y,
            size.X, size.Y,
            new Color32(200, 200, 200, 255),
            layer: 64
        );
    }

    public override bool CanEdit() => true;

    public override void BeginEdit()
    {
        _currentFrame = 0;
    }

    public override void EndEdit()
    {
    }

    public override void UpdateEdit()
    {
    }

    public override void DrawEdit()
    {
        if (FrameCount <= 0)
            return;

        var frame = Frames[_currentFrame];
        var shape = frame.Shape;

        for (ushort pIdx = 0; pIdx < shape.PathCount; pIdx++)
        {
            var path = shape.GetPath(pIdx);
            if (path.AnchorCount < 2)
                continue;

            for (ushort aIdx = 0; aIdx < path.AnchorCount; aIdx++)
            {
                var anchorIdx = (ushort)(path.AnchorStart + aIdx);
                var nextIdx = (ushort)(path.AnchorStart + (aIdx + 1) % path.AnchorCount);
                var a0 = shape.GetAnchor(anchorIdx);
                var a1 = shape.GetAnchor(nextIdx);
                var samples = shape.GetSegmentSamples(anchorIdx);

                var v0 = a0.Position + Position;
                for (var i = 0; i < samples.Length; i++)
                {
                    var sample = samples[i] + Position;
                    EditorRender.DrawLine(v0, sample, EditorStyle.EdgeColor);
                    v0 = sample;
                }
                EditorRender.DrawLine(v0, a1.Position + Position, EditorStyle.EdgeColor);
            }

            for (ushort aIdx = 0; aIdx < path.AnchorCount; aIdx++)
            {
                var anchor = shape.GetAnchor((ushort)(path.AnchorStart + aIdx));
                EditorRender.DrawVertex(anchor.Position + Position, EditorStyle.EdgeColor);
            }
        }
    }

    private ushort _currentFrame;

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
