//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ;

public static class TextRender
{
    private static Shader? _textShader;

    public static void Init(Shader textShader)
    {
        _textShader = textShader;
    }

    public static Vector2 Measure(string text, Font font, float fontSize)
    {
        if (font == null || string.IsNullOrEmpty(text))
            return Vector2.Zero;

        var totalWidth = 0f;

        for (var i = 0; i < text.Length; i++)
        {
            var glyph = font.GetGlyph(text[i]);
            totalWidth += glyph.Advance * fontSize;

            if (i + 1 < text.Length)
                totalWidth += font.GetKerning(text[i], text[i + 1]) * fontSize;
        }

        var totalHeight = font.LineHeight * fontSize;
        return new Vector2(totalWidth, totalHeight);
    }

    public static void Draw(string text, Font font, float fontSize, float x, float y, ushort order = 0)
    {
        Draw(text, font, fontSize, x, y, Matrix3x2.Identity, order);
    }

    public static void Draw(string text, Font font, float fontSize, float x, float y, in Matrix3x2 transform, ushort order = 0)
    {
        if (font == null || string.IsNullOrEmpty(text) || _textShader == null)
            return;

        var atlasTexture = font.AtlasTexture;
        if (atlasTexture == null)
            return;

        Render.SetShader(_textShader);
        Render.SetTexture(atlasTexture);
        Render.SetTransform(transform);

        var currentX = x;
        var baselineY = y + (font.LineHeight - font.Baseline) * fontSize;

        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            var glyph = font.GetGlyph(ch);

            // Only draw if glyph has valid UVs
            if (glyph.UVMax.X > glyph.UVMin.X && glyph.UVMax.Y > glyph.UVMin.Y)
            {
                var glyphX = currentX + glyph.Bearing.X * fontSize;
                var glyphY = baselineY + glyph.Bearing.Y * fontSize - glyph.Size.Y * fontSize;
                var glyphWidth = glyph.Size.X * fontSize;
                var glyphHeight = glyph.Size.Y * fontSize;

                Render.DrawQuad(
                    glyphX, glyphY,
                    glyphWidth, glyphHeight,
                    glyph.UVMin.X, glyph.UVMin.Y,
                    glyph.UVMax.X, glyph.UVMax.Y,
                    order
                );
            }

            currentX += glyph.Advance * fontSize;

            if (i + 1 < text.Length)
                currentX += font.GetKerning(ch, text[i + 1]) * fontSize;
        }
    }

    public static void Draw(string text, Font font, float fontSize, Vector2 position, ushort order = 0)
    {
        Draw(text, font, fontSize, position.X, position.Y, order);
    }

    public static void Draw(string text, Font font, float fontSize, Vector2 position, in Matrix3x2 transform, ushort order = 0)
    {
        Draw(text, font, fontSize, position.X, position.Y, transform, order);
    }
}
