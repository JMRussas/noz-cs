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
        DocumentManager.RegisterDef(new DocumentDef(
            AssetType.Font,
            ".ttf",
            () => new FontDocument()
        ));
    }

    public override void LoadMetadata(PropertySet meta)
    {
        FontSize = meta.GetInt("font", "size", 48);
        Characters = meta.GetString("font", "characters", DefaultCharacters);
        SdfRange = meta.GetFloat("sdf", "range", 8f);
        Padding = meta.GetInt("font", "padding", 1);
    }

    public override void SaveMetadata(PropertySet meta)
    {
        if (FontSize != 48) meta.SetInt("font", "size", FontSize);
        if (Characters != DefaultCharacters) meta.SetString("font", "characters", Characters);
        if (Math.Abs(SdfRange - 8f) > 0.001f) meta.SetFloat("sdf", "range", SdfRange);
        if (Padding != 1) meta.SetInt("font", "padding", Padding);
    }

    public override void Import(string outputPath, PropertySet config, PropertySet meta)
    {
        var ttf = TrueTypeFont.Load(Path, FontSize, Characters);
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
        public char Ascii;
    }

    private List<ImportGlyph> BuildGlyphList(TrueTypeFont ttf)
    {
        var glyphs = new List<ImportGlyph>();

        foreach (var c in Characters)
        {
            var ttfGlyph = ttf.GetGlyph(c);
            if (ttfGlyph == null)
                continue;

            var glyph = new ImportGlyph
            {
                Ascii = c,
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

                if (packer.Insert(glyph.PackedSize, RectPacker.Method.BestLongSideFit, out var packedRect) == -1)
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

        // Asset header
        writer.Write(Constants.AssetSignature);
        writer.Write((byte)AssetType.Font);
        writer.Write(Font.Version);
        writer.Write((ushort)0); // flags

        // Font metrics
        writer.Write((uint)FontSize);
        writer.Write((uint)atlasSize.X);
        writer.Write((uint)atlasSize.Y);
        writer.Write((float)(ttf.Ascent * fontSizeInv));
        writer.Write((float)(ttf.Descent * fontSizeInv));
        writer.Write((float)((ttf.Height + ttf.Descent) * fontSizeInv));
        writer.Write(0.0f); // baseline

        // Write glyph count and data
        writer.Write((ushort)glyphs.Count);
        foreach (var glyph in glyphs)
        {
            writer.Write((uint)glyph.Ascii);

            // UV coordinates
            writer.Write((float)glyph.PackedRect.X / atlasSize.X);
            writer.Write((float)glyph.PackedRect.Y / atlasSize.Y);
            writer.Write((float)(glyph.PackedRect.X + glyph.PackedRect.Width) / atlasSize.X);
            writer.Write((float)(glyph.PackedRect.Y + glyph.PackedRect.Height) / atlasSize.Y);

            // Size
            writer.Write((float)(glyph.Size.x * fontSizeInv));
            writer.Write((float)(glyph.Size.y * fontSizeInv));

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
            foreach (var k in kerning)
            {
                var pair = k.Item1;
                var left = (uint)(pair >> 8);
                var right = (uint)(pair & 0xFF);
                writer.Write(left);
                writer.Write(right);
                writer.Write(k.Item2 * fontSizeInv);
            }
        }

        // Write atlas data (R8 format)
        var bytes = atlas.AsBytes();
        writer.Write(bytes);
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
}
