//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.ColorSpaces;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace NoZ.Editor;

public class PaletteDef
{
    public const int ColorCount = 64;
    public const int CellSize = 8;

    public string Name { get; }
    public int Id { get; }
    public Color[] Colors { get; } = new Color[ColorCount];

    public PaletteDef(string name, int id)
    {
        Name = name;
        Id = id;

        for (int i = 0; i < ColorCount; i++)
            Colors[i] = Color.Purple;
    }

    public void SampleColors(Image<Rgba32> image)
    {
        if (image.Width != 512 || image.Height != 512) return;

        image.ProcessPixelRows(accessor =>
        {
            int y = Id * CellSize + CellSize / 2;
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
            }
        });
    }
}

public static class PaletteManager
{
    private static readonly List<PaletteDef> _palettes = [];
    private static readonly Dictionary<int, int> _paletteMap = [];
    private static string? _paletteTextureName;
    private static TextureDocument? _paletteTexture;

    public static IReadOnlyList<PaletteDef> Palettes => _palettes;

    public static void Init(EditorConfig config)
    {
        _palettes.Clear();
        _paletteMap.Clear();

        _paletteTextureName = config.Palette;

        foreach (var name in config.GetPaletteNames())
        {
            int id = config.GetPaletteIndex(name);
            _paletteMap[id] = _palettes.Count;
            _palettes.Add(new PaletteDef(name, id));
        }

        ReloadPaletteColors();
    }

    public static void Shutdown()
    {
        _palettes.Clear();
        _paletteMap.Clear();
        _paletteTextureName = null;
        _paletteTexture = null;
    }

    public static PaletteDef? GetPalette(int id)
    {
        return _paletteMap.TryGetValue(id, out var index) ? _palettes[index] : null;
    }

    public static PaletteDef? GetPalette(string name)
    {
        return _palettes.FirstOrDefault(p => p.Name == name);
    }

    public static Color GetColor(int paletteId, int colorId)
    {
        var palette = GetPalette(paletteId);
        if (palette == null || colorId < 0 || colorId >= PaletteDef.ColorCount)
            return Color.White;
        return palette.Colors[colorId];
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
