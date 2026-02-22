//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace NoZ.Editor;

public class PaletteDef
{
    public const int ColorCount = 64;
    public const int CellSize = 8;

    public string Id { get; }
    public int Row { get; }
    public string? DisplayName { get; }
    public string Label { get; }
    public Color[] Colors { get; } = new Color[ColorCount];
    public int Count { get; private set; }

    public PaletteDef(string id, int row, string? displayName)
    {
        Id = id;
        Row = row;
        DisplayName = displayName;
        Label = displayName ?? id;

        for (int i = 0; i < ColorCount; i++)
            Colors[i] = Color.Purple;
    }

    public void SampleColors(Image<Rgba32> image)
    {
        if (image.Width != 512 || image.Height != 512) return;

        int count = 0;
        image.ProcessPixelRows(accessor =>
        {
            int y = Row * CellSize + CellSize / 2;
            Span<Rgba32> row = accessor.GetRowSpan(y);

            for (int c = 0; c < ColorCount; c++)
            {
                ref var p = ref row[c * CellSize + CellSize / 2];
                Colors[c] = new Color(
                    p.R / 255f,
                    p.G / 255f,
                    p.B / 255f,
                    p.A / 255f
                );

                if (p.A > 0)
                    count = c + 1;
            }
        });
        Count = count;
    }
}

public static class PaletteManager
{
    private static readonly List<PaletteDef> _palettes = [];
    private static readonly Dictionary<int, int> _paletteRowMap = [];
    private static readonly Dictionary<string, int> _paletteIdMap = [];
    private static string? _paletteTextureName;
    private static TextureDocument? _paletteTexture;

    public static IReadOnlyList<PaletteDef> Palettes => _palettes;

    public static int DefaultRow => _palettes.Count > 0 ? _palettes[0].Row : 0;

    public static void Init(EditorConfig config)
    {
        _palettes.Clear();
        _paletteRowMap.Clear();
        _paletteIdMap.Clear();

        _paletteTextureName = config.Palette;

        foreach (var id in config.GetKeys("palettes"))
        {
            var value = config.GetString("palettes", id, "0");
            var tk = new Tokenizer(value);

            int row = 0;
            string? displayName = null;

            if (tk.ExpectInt(out var intVal))
                row = intVal;

            displayName = tk.ExpectQuotedString();

            _paletteRowMap[row] = _palettes.Count;
            _paletteIdMap[id.ToLower()] = _palettes.Count;
            _palettes.Add(new PaletteDef(id, row, displayName));
        }

        ReloadPaletteColors();
    }

    public static void Shutdown()
    {
        _palettes.Clear();
        _paletteRowMap.Clear();
        _paletteIdMap.Clear();
        _paletteTextureName = null;
        _paletteTexture = null;
    }

    public static PaletteDef GetPalette(int row) =>
        _paletteRowMap.TryGetValue(row, out var index) ? _palettes[index] : _palettes[0];

    public static PaletteDef? GetPalette(string id) =>
        _paletteIdMap.TryGetValue(id, out var index) ? _palettes[index] : null;

    public static bool TryGetPalette(string? id, out PaletteDef palette)
    {
        if (!string.IsNullOrEmpty(id) && _paletteIdMap.TryGetValue(id.ToLower(), out var index))
        {
            palette = _palettes[index];
            return true;
        }
        palette = _palettes.Count > 0 ? _palettes[0] : null!;
        return false;
    }

    public static bool TryGetPaletteByRow(int row, out PaletteDef palette)
    {
        if (_paletteRowMap.TryGetValue(row, out var index))
        {
            palette = _palettes[index];
            return true;
        }
        palette = _palettes.Count > 0 ? _palettes[0] : null!;
        return false;
    }

    public static Color GetColor(int paletteRow, int colorId)
    {
        var palette = GetPalette(paletteRow);
        if (palette == null || colorId < 0 || colorId >= PaletteDef.ColorCount)
            return Color.White;
        return palette.Colors[colorId];
    }

    public static Color GetColor(string paletteName, int colorId)
    {
        var palette = GetPalette(paletteName);
        if (palette == null || colorId < 0 || colorId >= PaletteDef.ColorCount)
            return Color.White;
        return palette.Colors[colorId];
    }

    public static byte FindColorIndex(int paletteRow, Color32 color)
    {
        var palette = GetPalette(paletteRow);
        for (int i = 0; i < palette.Colors.Length; i++)
        {
            var pc = palette.Colors[i].ToColor32();
            if (pc.R == color.R && pc.G == color.G && pc.B == color.B)
                return (byte)i;
        }
        return 0;
    }

    public static void ReloadPaletteColors()
    {
        if (string.IsNullOrEmpty(_paletteTextureName))
            return;

        _paletteTexture = DocumentManager.Find(AssetType.Texture, _paletteTextureName) as TextureDocument;
        if (_paletteTexture == null)
        {
            Log.Error($"Palette texture not found: {_paletteTextureName}.png");
            return;
        }

        _paletteTexture.IsVisible = false;

        try
        {
            var image = Image.Load<Rgba32>(_paletteTexture.Path);
            foreach (var palette in _palettes)
                palette.SampleColors(image);

            Log.Info($"Loaded palette {_paletteTexture.Path}");
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to load palette texture: {ex.Message}");
        }
    }
}
