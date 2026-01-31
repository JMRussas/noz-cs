//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Diagnostics;
using System.Numerics;
using NoZ.Platform;

namespace NoZ;

internal static class TextRender
{
    private const int MaxVertices = 8192;
    private const int MaxIndices = 8192 / 4 * 6;

    private static Shader? _textShader;
    private static nuint _mesh;
    private static NativeArray<TextVertex> _vertices = new NativeArray<TextVertex>(MaxVertices);
    private static NativeArray<ushort> _indices = new NativeArray<ushort>(MaxIndices);

    public static Color32 OutlineColor { get; private set; } = Color32.Transparent;
    public static float OutlineWidth { get; private set; }
    public static float OutlineSoftness { get; private set; }

    public static void SetOutline(Color color, float width, float softness = 0f)
    {
        OutlineColor = color.ToColor32();
        OutlineWidth = width;
        OutlineSoftness = softness;
    }

    public static void ClearOutline()
    {
        OutlineColor = Color32.Transparent;
        OutlineWidth = 0f;
        OutlineSoftness = 0f;
    }

    public static void Init(ApplicationConfig config)
    {
        _textShader = Asset.Get<Shader>(AssetType.Shader, config.TextShader);
        if (_textShader == null) throw new Exception($"Failed to load text shader '{config.TextShader}'");

        _vertices = new NativeArray<TextVertex>(MaxVertices);
        _mesh = Graphics.Driver.CreateMesh<TextVertex>(
            MaxVertices,
            MaxIndices,
            BufferUsage.Dynamic,
            "TextRender"
        );
    }

    public static void Shutdown()
    {
        _vertices.Dispose();
        Graphics.Driver.DestroyMesh(_mesh);
        _mesh = nuint.Zero;
        _textShader = null;
    }
    
    public static void Flush()
    {
        if (_vertices.Length == 0 && _indices.Length == 0)
            return;

        Graphics.Driver.BindMesh(_mesh);
        Graphics.Driver.UpdateMesh(_mesh, _vertices.AsByteSpan(), _indices.AsSpan());
        _vertices.Clear();
        _indices.Clear();
    }
    
    public static Vector2 Measure(ReadOnlySpan<char> text, Font font, float fontSize)
    {
        if (text.Length == 0) return Vector2.Zero;

        var totalWidth = 0f;
        for (var i = 0; i < text.Length; i++)
        {
            var glyph = font.GetGlyph(text[i]);
            var advance = glyph.Advance * fontSize;

            if (i + 1 < text.Length)
                advance += font.GetKerning(text[i], text[i + 1]) * fontSize;

            totalWidth += advance;
        }

        var totalHeight = font.LineHeight * fontSize;
        return new Vector2(totalWidth, totalHeight);
    }

    public static Vector2 Measure(string text, Font font, float fontSize)
    {
        if (string.IsNullOrEmpty(text))
            return Vector2.Zero;

        var totalWidth = 0f;
        for (var i = 0; i < text.Length; i++)
        {
            var glyph = font.GetGlyph(text[i]);
            var advance = glyph.Advance * fontSize;

            if (i + 1 < text.Length)
                advance += font.GetKerning(text[i], text[i + 1]) * fontSize;

            totalWidth += advance;
        }

        var totalHeight = font.LineHeight * fontSize;
        return new Vector2(totalWidth, totalHeight);
    }

    public static void Draw(in ReadOnlySpan<char> text, Font font, float fontSize, int order = 0)
    {
        Debug.Assert(order >= 0 && order <= ushort.MaxValue);

        if (text.Length == 0 || _textShader == null)
            return;

        var atlasTexture = font.AtlasTexture;
        if (atlasTexture == null)
            return;

        using var _ = Graphics.PushState();
        Graphics.SetShader(_textShader);
        Graphics.SetTexture(atlasTexture);
        Graphics.SetMesh(_mesh);

        var currentX = 0f;
        var baselineY = (font.Baseline + font.InternalLeading * 0.5f) * fontSize;
        var baseIndex = _indices.Length;

        var color = Graphics.Color.ToColor32();
        var outlineColor = OutlineColor;
        var outlineWidth = OutlineWidth;
        var outlineSoftness = OutlineSoftness;

        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            var glyph = font.GetGlyph(ch);

            if (glyph.UVMax.X > glyph.UVMin.X && glyph.UVMax.Y > glyph.UVMin.Y)
            {
                var x0 = currentX + glyph.Bearing.X * fontSize;
                var x1 = x0 + glyph.Size.X * fontSize;
                var y0 = baselineY + glyph.Bearing.Y * fontSize - glyph.Size.Y * fontSize;
                var y1 = y0 + glyph.Size.Y * fontSize;

                var baseVertex = _vertices.Length;

                _vertices.Add(new TextVertex
                {
                    Position = Vector2.Transform(new Vector2(x0, y0), Graphics.Transform),
                    UV = glyph.UVMin,
                    Color = color,
                    OutlineColor = outlineColor,
                    OutlineWidth = outlineWidth,
                    OutlineSoftness = outlineSoftness
                });

                _vertices.Add(new TextVertex
                {
                    Position = Vector2.Transform(new Vector2(x1, y0), Graphics.Transform),
                    UV = new Vector2(glyph.UVMax.X, glyph.UVMin.Y),
                    Color = color,
                    OutlineColor = outlineColor,
                    OutlineWidth = outlineWidth,
                    OutlineSoftness = outlineSoftness
                });

                _vertices.Add(new TextVertex
                {
                    Position = Vector2.Transform(new Vector2(x1, y1), Graphics.Transform),
                    UV = glyph.UVMax,
                    Color = color,
                    OutlineColor = outlineColor,
                    OutlineWidth = outlineWidth,
                    OutlineSoftness = outlineSoftness
                });

                _vertices.Add(new TextVertex
                {
                    Position = Vector2.Transform(new Vector2(x0, y1), Graphics.Transform),
                    UV = new Vector2(glyph.UVMin.X, glyph.UVMax.Y),
                    Color = color,
                    OutlineColor = outlineColor,
                    OutlineWidth = outlineWidth,
                    OutlineSoftness = outlineSoftness
                });

                _indices.Add((ushort)(baseVertex + 0));
                _indices.Add((ushort)(baseVertex + 1));
                _indices.Add((ushort)(baseVertex + 2));
                _indices.Add((ushort)(baseVertex + 2));
                _indices.Add((ushort)(baseVertex + 3));
                _indices.Add((ushort)(baseVertex + 0));
                Graphics.DrawElements(6, baseIndex, (ushort)order);

                baseIndex += 6;
            }

            var advance = glyph.Advance * fontSize;
            if (i + 1 < text.Length)
                advance += font.GetKerning(ch, text[i + 1]) * fontSize;

            currentX += advance;
        }
    }
}
