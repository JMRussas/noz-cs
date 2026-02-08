//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ.Editor;

public class FontDocument : Document
{
    private const string DefaultCharacters = " !\"#$%&'()*+,-./0123456789:;<=>?@ABCDEFGHIJKLMNOPQRSTUVWXYZ[\\]^_`abcdefghijklmnopqrstuvwxyz{|}~";

    public int FontSize { get; set; } = 48;
    public string Characters { get; set; } = DefaultCharacters;
    public float SdfRange { get; set; } = 8f;
    public int Padding { get; set; } = 1;

    public static void RegisterDef()
    {
        DocumentManager.RegisterDef(new DocumentDef
        {
            Type = AssetType.Font,
            Extension = ".ttf",
            Factory = () => new FontDocument()
        });
    }

    public override void LoadMetadata(PropertySet meta)
    {
        FontSize = meta.GetInt("font", "size", 48);
        Characters = meta.GetString("font", "characters", DefaultCharacters);
        SdfRange = meta.GetFloat("sdf", "range", 8f);
        Padding = meta.GetInt("font", "padding", 1);

        var ranges = meta.GetString("font", "ranges", "");
        if (!string.IsNullOrEmpty(ranges))
            Characters = MergeCharacterRanges(ranges, Characters);
    }

    public override void SaveMetadata(PropertySet meta)
    {
        if (FontSize != 48) meta.SetInt("font", "size", FontSize);
        if (Characters != DefaultCharacters) meta.SetString("font", "characters", Characters);
        if (Math.Abs(SdfRange - 8f) > 0.001f) meta.SetFloat("sdf", "range", SdfRange);
        if (Padding != 1) meta.SetInt("font", "padding", Padding);
    }

    private bool ImportAll => Characters == "*";

    public override void Import(string outputPath, PropertySet meta)
    {
        var ttf = TrueTypeFont.Load(Path, FontSize, ImportAll ? null : Characters);
        if (ttf == null)
            throw new ImportException($"Failed to load TTF file: {Path}");

        var glyphs = BuildGlyphList(ttf);
        if (glyphs.Count == 0)
            throw new ImportException("No glyphs to import");

        var atlasSize = PackGlyphs(glyphs);
        using var atlas = new PixelData<byte>(atlasSize.X, atlasSize.Y);

        RenderGlyphs(glyphs, atlas);

        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(outputPath) ?? "");
        WriteFontData(outputPath, ttf, glyphs, atlas, atlasSize);
    }

    private struct ImportGlyph
    {
        public TrueTypeFont.Glyph Ttf;
        public Vector2Double Size;
        public Vector2Double Bearing;
        public double Advance;
        public Vector2Int PackedSize;
        public RectInt PackedRect;
        public char Codepoint;
    }

    private List<ImportGlyph> BuildGlyphList(TrueTypeFont ttf)
    {
        var glyphs = new List<ImportGlyph>();

        // When importing all, iterate every glyph the TTF reader loaded.
        // Otherwise, iterate the Characters filter string.
        var source = ImportAll
            ? ttf.Glyphs
            : Characters.Select(c => ttf.GetGlyph(c)).Where(g => g != null)!;

        foreach (var ttfGlyph in source)
        {
            var glyph = new ImportGlyph
            {
                Codepoint = ttfGlyph!.codepoint,
                Ttf = ttfGlyph,
                Size = ttfGlyph.size + new Vector2Double(SdfRange * 2, SdfRange * 2),
                Bearing = ttfGlyph.bearing - new Vector2Double(SdfRange, SdfRange),
                Advance = ttfGlyph.advance,
                PackedSize = new Vector2Int(
                    (int)Math.Ceiling(ttfGlyph.size.x + SdfRange * 2 + Padding * 2),
                    (int)Math.Ceiling(ttfGlyph.size.y + SdfRange * 2 + Padding * 2)
                )
            };

            glyphs.Add(glyph);
        }

        return glyphs;
    }

    private Vector2Int PackGlyphs(List<ImportGlyph> glyphs)
    {
        var minHeight = (int)NextPowerOf2((uint)(FontSize + 2 + SdfRange * 2 + Padding * 2));
        var packer = new RectPacker(minHeight, minHeight);

        while (packer.IsEmpty)
        {
            for (var i = 0; i < glyphs.Count; i++)
            {
                var glyph = glyphs[i];

                // Skip glyphs with no contours (spaces, etc.)
                if (glyph.Ttf.contours == null || glyph.Ttf.contours.Length == 0)
                    continue;

                if (packer.Insert(glyph.PackedSize, out var packedRect) == -1)
                {
                    // Need to resize
                    var size = packer.Size;
                    if (size.X <= size.Y)
                        packer.Resize(size.X * 2, size.Y);
                    else
                        packer.Resize(size.X, size.Y * 2);
                    break;
                }

                glyph.PackedRect = packedRect;
                glyphs[i] = glyph;
            }
        }

        return packer.Size;
    }

    private void RenderGlyphs(List<ImportGlyph> glyphs, PixelData<byte> atlas)
    {
        foreach (var glyph in glyphs)
        {
            if (glyph.Ttf.contours == null || glyph.Ttf.contours.Length == 0)
                continue;

            var outputPosition = new Vector2Int(
                glyph.PackedRect.X + Padding,
                glyph.PackedRect.Y + Padding
            );

            var outputSize = new Vector2Int(
                glyph.PackedRect.Width - Padding * 2,
                glyph.PackedRect.Height - Padding * 2
            );

            var translate = new Vector2Double(
                -glyph.Ttf.bearing.x + SdfRange,
                glyph.Ttf.size.y - glyph.Ttf.bearing.y + SdfRange
            );

            MSDF.RenderGlyph(
                glyph.Ttf,
                atlas,
                outputPosition,
                outputSize,
                SdfRange * 0.5,
                new Vector2Double(1, 1),
                translate
            );
        }
    }

    private void WriteFontData(
        string outputPath,
        TrueTypeFont ttf,
        List<ImportGlyph> glyphs,
        PixelData<byte> atlas,
        Vector2Int atlasSize)
    {
        var fontSizeInv = 1.0f / FontSize;

        using var writer = new BinaryWriter(File.Create(outputPath));

        writer.WriteAssetHeader(AssetType.Font, Font.Version);
        writer.Write((uint)FontSize);
        writer.Write((uint)atlasSize.X);
        writer.Write((uint)atlasSize.Y);
        writer.Write((float)(ttf.Ascent * fontSizeInv));
        writer.Write((float)(ttf.Descent * fontSizeInv));
        writer.Write((float)(ttf.Height * fontSizeInv));
        writer.Write((float)(ttf.Ascent * fontSizeInv));
        writer.Write((float)(ttf.InternalLeading * fontSizeInv));

        // Font family name
        writer.Write((ushort)ttf.FamilyName.Length);
        if (ttf.FamilyName.Length > 0)
            writer.Write(ttf.FamilyName.ToCharArray());

        // Write glyph count and data
        writer.Write((ushort)glyphs.Count);
        foreach (var glyph in glyphs)
        {
            writer.Write((uint)glyph.Codepoint);

            // Glyphs with no contours (space, etc.) have no atlas entry - write zero UVs
            var hasContours = glyph.Ttf.contours != null && glyph.Ttf.contours.Length > 0;
            if (hasContours)
            {
                // UV coordinates (offset by Padding to exclude padding from sampling)
                writer.Write((float)(glyph.PackedRect.X + Padding) / atlasSize.X);
                writer.Write((float)(glyph.PackedRect.Y + Padding) / atlasSize.Y);
                writer.Write((float)(glyph.PackedRect.X + Padding + glyph.Size.x) / atlasSize.X);
                writer.Write((float)(glyph.PackedRect.Y + Padding + glyph.Size.y) / atlasSize.Y);

                // Size
                writer.Write((float)(glyph.Size.x * fontSizeInv));
                writer.Write((float)(glyph.Size.y * fontSizeInv));
            }
            else
            {
                // No visual representation - zero UVs and size
                writer.Write(0f);
                writer.Write(0f);
                writer.Write(0f);
                writer.Write(0f);
                writer.Write(0f);
                writer.Write(0f);
            }

            // Advance
            writer.Write((float)(glyph.Advance * fontSizeInv));

            // Bearing
            writer.Write((float)(glyph.Bearing.x * fontSizeInv));
            writer.Write((float)((glyph.Ttf.size.y - glyph.Ttf.bearing.y) * fontSizeInv));
        }

        // Write kerning data
        var kerning = ttf._kerning;
        var kerningCount = kerning?.Count ?? 0;
        writer.Write((ushort)kerningCount);

        if (kerning != null)
        {
            foreach (var (key, value) in kerning)
            {
                var left = (uint)(key >> 16);
                var right = (uint)(key & 0xFFFF);
                writer.Write(left);
                writer.Write(right);
                writer.Write(value * fontSizeInv);
            }
        }

        var bytes = atlas.AsByteSpan();
        writer.Write(bytes);
    }

    private static string MergeCharacterRanges(string rangesStr, string existingChars)
    {
        var chars = new HashSet<char>(existingChars);
        foreach (var range in rangesStr.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = range.Split('-', 2);
            if (parts.Length == 2)
            {
                var start = Convert.ToInt32(parts[0].Trim(), 16);
                var end = Convert.ToInt32(parts[1].Trim(), 16);
                for (int c = start; c <= end && c <= 0xFFFF; c++)
                    chars.Add((char)c);
            }
            else if (parts.Length == 1)
            {
                var c = Convert.ToInt32(parts[0].Trim(), 16);
                if (c <= 0xFFFF)
                    chars.Add((char)c);
            }
        }
        return new string(chars.OrderBy(c => c).ToArray());
    }

    private static uint NextPowerOf2(uint v)
    {
        v--;
        v |= v >> 1;
        v |= v >> 2;
        v |= v >> 4;
        v |= v >> 8;
        v |= v >> 16;
        v++;
        return v;
    }

    public override void Draw()
    {
        using (Graphics.PushState())
        {
            Graphics.SetLayer(EditorLayer.Document);
            Graphics.SetColor(Color.White);
            Graphics.Draw(EditorAssets.Sprites.AssetIconFont);
        }
    }
}
