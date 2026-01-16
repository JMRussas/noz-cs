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
    internal const byte Version = 1;
    private const int MaxGlyphs = 256;
    private const int MaxKerningPairs = MaxGlyphs * MaxGlyphs;

    public int FontSize { get; private init; }
    public int AtlasWidth { get; private init; }
    public int AtlasHeight { get; private init; }
    public float Ascent { get; private init; }
    public float Descent { get; private init; }
    public float LineHeight { get; private init; }
    public float Baseline { get; private init; }

    private readonly FontGlyph[] _glyphs = new FontGlyph[MaxGlyphs];
    private readonly ushort[] _kerningIndex = new ushort[MaxKerningPairs];
    private float[] _kerningValues = [];
    private Texture? _atlasTexture;

    public Texture? AtlasTexture => _atlasTexture;

    private Font(string name) : base(AssetType.Font, name)
    {
        for (var i = 0; i < MaxKerningPairs; i++)
            _kerningIndex[i] = 0xFFFF;
    }

    public FontGlyph GetGlyph(char c)
    {
        var index = (int)c;
        if (index < MaxGlyphs && _glyphs[index].Advance > 0)
            return _glyphs[index];

        // Fallback to unknown glyph (ASCII DEL)
        const int unknownChar = 0x7F;
        if (_glyphs[unknownChar].Advance > 0)
            return _glyphs[unknownChar];

        return default;
    }

    public float GetKerning(char first, char second)
    {
        var f = (byte)first;
        var s = (byte)second;
        var index = f * MaxGlyphs + s;
        var valueIndex = _kerningIndex[index];

        if (valueIndex != 0xFFFF && valueIndex < _kerningValues.Length)
            return _kerningValues[valueIndex];

        return 0f;
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

        var font = new Font(name)
        {
            FontSize = fontSize,
            AtlasWidth = atlasWidth,
            AtlasHeight = atlasHeight,
            Ascent = ascent,
            Descent = descent,
            LineHeight = lineHeight,
            Baseline = baseline
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

            if (codepoint < MaxGlyphs)
            {
                font._glyphs[codepoint] = new FontGlyph
                {
                    UVMin = new Vector2(uvMinX, uvMinY),
                    UVMax = new Vector2(uvMaxX, uvMaxY),
                    Size = new Vector2(sizeX, sizeY),
                    Advance = advance,
                    Bearing = new Vector2(bearingX, bearingY)
                };
            }
        }

        // Read kerning
        var kerningCount = reader.ReadUInt16();
        if (kerningCount > 0)
        {
            font._kerningValues = new float[kerningCount];
            for (var i = 0; i < kerningCount; i++)
            {
                var first = reader.ReadUInt32();
                var second = reader.ReadUInt32();
                var amount = reader.ReadSingle();

                if (first < MaxGlyphs && second < MaxGlyphs)
                {
                    var index = first * MaxGlyphs + second;
                    font._kerningIndex[index] = (ushort)i;
                    font._kerningValues[i] = amount;
                }
            }
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
