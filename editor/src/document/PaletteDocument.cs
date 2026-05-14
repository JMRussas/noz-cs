//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ.Editor;

public class PaletteDocument : Document
{
    public const string Extension = ".pal";

    public static readonly AssetType PaletteAssetType = AssetType.FromString("PAL_");
    public const int MaxColors = 256;

    public override bool CanSave => true;

    public Color[] Colors { get; private set; } = new Color[MaxColors];
    public string?[] ColorNames { get; private set; } = new string?[MaxColors];
    public int ColorCount { get; set; }

    public static void RegisterDef()
    {
        DocumentDef<PaletteDocument>.Register(new DocumentDef
        {
            Type = PaletteAssetType,
            Name = "Palette",
            Extensions = [Extension],
            Factory = _ => new PaletteDocument(),
            EditorFactory = doc => new PaletteEditor((PaletteDocument)doc),
        });
    }

    public static Document? CreateNew(string? name = null, System.Numerics.Vector2? position = null)
    {
        return Project.New(PaletteAssetType, Extension, name, writer =>
        {
            writer.WriteLine("JASC-PAL");
            writer.WriteLine("0100");
            writer.WriteLine("16");
            for (int i = 0; i < 16; i++)
                writer.WriteLine("0 0 0");
        }, position);
    }

    public override void Load()
    {
        ParsePalFile();
    }

    public override void LoadMetadata(PropertySet meta)
    {
    }

    public override void Reload()
    {
        ParsePalFile();
        PaletteManager.ReloadPaletteColors();
    }

    public override void Save(StreamWriter sw)
    {
        PaletteManager.ReloadPaletteColors();

        sw.WriteLine("JASC-PAL");
        sw.WriteLine("0100");
        sw.WriteLine(ColorCount.ToString());
        for (int i = 0; i < ColorCount; i++)
        {
            var c = Colors[i];
            int r = (int)(Math.Clamp(c.R, 0, 1) * 255);
            int g = (int)(Math.Clamp(c.G, 0, 1) * 255);
            int b = (int)(Math.Clamp(c.B, 0, 1) * 255);

            if (!string.IsNullOrEmpty(ColorNames[i]))
                sw.WriteLine($"{r} {g} {b} \"{ColorNames[i]}\"");
            else
                sw.WriteLine($"{r} {g} {b}");
        }
    }

    public override void Clone(Document source)
    {
        var src = (PaletteDocument)source;
        ColorCount = src.ColorCount;
        Array.Copy(src.Colors, Colors, MaxColors);
        Array.Copy(src.ColorNames, ColorNames, MaxColors);
    }

    public override void OnUndoRedo()
    {
        PaletteManager.ReloadPaletteColors();
    }

    public override Color32 GetPixelAt(System.Numerics.Vector2 worldPos)
    {
        const int columns = 8;
        var cellSize = 1f / columns;

        var localX = worldPos.X - Position.X;
        var localY = worldPos.Y - Position.Y;

        var col = (int)((localX + 0.5f) / cellSize);
        var row = (int)((localY + 0.5f) / cellSize);

        if (col < 0 || col >= columns || row < 0) return default;

        var index = row * columns + col;
        if (index < 0 || index >= ColorCount) return default;

        return (Color32)Colors[index];
    }

    public override void Draw()
    {
        const int columns = 8;
        var cellSize = 1f / columns;

        Bounds = new Rect(-0.5f, -0.5f, 1f, 1f);

        using (Graphics.PushState())
        {
            Graphics.SetLayer(EditorLayer.Document);
            Graphics.SetTransform(Transform);
            Graphics.SetShader(EditorAssets.Shaders.Texture);
            Graphics.SetTexture(Graphics.WhiteTexture);

            for (int i = 0; i < ColorCount; i++)
            {
                int col = i % columns;
                int row = i / columns;
                float x = -0.5f + col * cellSize;
                float y = -0.5f + row * cellSize;

                Graphics.SetColor(Colors[i]);
                Graphics.Draw(x, y, cellSize, cellSize);
            }
        }
    }

    private void ParsePalFile()
    {
        if (!File.Exists(Path))
            return;

        var lines = File.ReadAllText(Path).Split('\n');
        if (lines.Length < 3) return;

        if (lines[0].Trim() != "JASC-PAL") return;
        // lines[1] is version "0100" -- skip

        if (!int.TryParse(lines[2].Trim(), out int count)) return;
        count = Math.Min(count, MaxColors);

        ColorCount = 0;
        for (int i = 0; i < count && (i + 3) < lines.Length; i++)
        {
            var line = lines[i + 3].Trim();
            if (string.IsNullOrEmpty(line)) continue;

            var tk = new Tokenizer(line);
            if (!tk.ExpectInt(out int r)) continue;
            if (!tk.ExpectInt(out int g)) continue;
            if (!tk.ExpectInt(out int b)) continue;

            Colors[ColorCount] = new Color(r / 255f, g / 255f, b / 255f, 1f);
            ColorNames[ColorCount] = tk.ExpectQuotedString();
            ColorCount++;
        }
    }
}
