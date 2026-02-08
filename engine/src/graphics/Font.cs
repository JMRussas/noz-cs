//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ;

public struct FontGlyph
{
    public Vector2 UVMin;
    public Vector2 UVMax;
    public Vector2 Size;
    public float Advance;
    public Vector2 Bearing;
}

public class Font : Asset
{
    internal const ushort Version = 4;

    public int FontSize { get; private init; }
    public int AtlasWidth { get; private init; }
    public int AtlasHeight { get; private init; }
    public float Ascent { get; private init; }
    public float Descent { get; private init; }
    public float LineHeight { get; private init; }
    public float Baseline { get; private init; }
    public float InternalLeading { get; private init; }
    public string FamilyName { get; private init; } = "";

    private readonly Dictionary<int, FontGlyph> _glyphs = new();
    private readonly Dictionary<uint, float> _kerning = new();
    private Texture? _atlasTexture;

    public Texture? AtlasTexture => _atlasTexture;

    private Font(string name) : base(AssetType.Font, name)
    {
    }

    public FontGlyph GetGlyph(char c)
    {
        if (_glyphs.TryGetValue((int)c, out var glyph) && glyph.Advance > 0)
            return glyph;

        // Fallback to unknown glyph (ASCII DEL)
        const int unknownChar = 0x7F;
        if (_glyphs.TryGetValue(unknownChar, out var fallback) && fallback.Advance > 0)
            return fallback;

        return default;
    }

    public float GetKerning(char first, char second)
    {
        var key = ((uint)first << 16) | (uint)second;
        return _kerning.GetValueOrDefault(key, 0f);
    }

    private static Font? Load(Stream stream, string name)
    {
        using var reader = new BinaryReader(stream);

        var fontSize = (int)reader.ReadUInt32();
        var atlasWidth = (int)reader.ReadUInt32();
        var atlasHeight = (int)reader.ReadUInt32();
        var ascent = reader.ReadSingle();
        var descent = reader.ReadSingle();
        var lineHeight = reader.ReadSingle();
        var baseline = reader.ReadSingle();
        var internalLeading = reader.ReadSingle();

        var familyNameLength = reader.ReadUInt16();
        var familyName = familyNameLength > 0
            ? new string(reader.ReadChars(familyNameLength))
            : "";

        var font = new Font(name)
        {
            FontSize = fontSize,
            AtlasWidth = atlasWidth,
            AtlasHeight = atlasHeight,
            Ascent = ascent,
            Descent = descent,
            LineHeight = lineHeight,
            Baseline = baseline,
            InternalLeading = internalLeading,
            FamilyName = familyName
        };

        // Read glyphs
        var glyphCount = reader.ReadUInt16();
        for (var i = 0; i < glyphCount; i++)
        {
            var codepoint = reader.ReadUInt32();
            var uvMinX = reader.ReadSingle();
            var uvMinY = reader.ReadSingle();
            var uvMaxX = reader.ReadSingle();
            var uvMaxY = reader.ReadSingle();
            var sizeX = reader.ReadSingle();
            var sizeY = reader.ReadSingle();
            var advance = reader.ReadSingle();
            var bearingX = reader.ReadSingle();
            var bearingY = reader.ReadSingle();

            font._glyphs[(int)codepoint] = new FontGlyph
            {
                UVMin = new Vector2(uvMinX, uvMinY),
                UVMax = new Vector2(uvMaxX, uvMaxY),
                Size = new Vector2(sizeX, sizeY),
                Advance = advance,
                Bearing = new Vector2(bearingX, bearingY)
            };
        }

        // Read kerning
        var kerningCount = reader.ReadUInt16();
        for (var i = 0; i < kerningCount; i++)
        {
            var first = reader.ReadUInt32();
            var second = reader.ReadUInt32();
            var amount = reader.ReadSingle();
            var key = (first << 16) | second;
            font._kerning[key] = amount;
        }

        // Read atlas texture data (R8 format)
        var atlasDataSize = atlasWidth * atlasHeight;
        var r8Data = reader.ReadBytes(atlasDataSize);

        font._atlasTexture = Texture.Create(atlasWidth, atlasHeight, r8Data, TextureFormat.R8, name: name + "_atlas");

        return font;
    }

    internal static void RegisterDef()
    {
        RegisterDef(new AssetDef(AssetType.Font, typeof(Font), Load));
    }

    public override void Dispose()
    {
        _atlasTexture?.Dispose();
        _atlasTexture = null;
        base.Dispose();
    }
}
