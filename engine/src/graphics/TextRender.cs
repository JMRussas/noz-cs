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

    public static void DrawAtlas(Font font, Rect bounds)
    {
        if (_textShader == null)
            return;

        var atlasTexture = font.AtlasTexture;
        if (atlasTexture == null)
            return;

        using var _ = Graphics.PushState();
        Graphics.SetShader(_textShader);
        Graphics.SetTexture(atlasTexture, filter: TextureFilter.Linear);
        Graphics.SetMesh(_mesh);

        var color = Graphics.Color.ToColor32();
        var baseVertex = _vertices.Length;
        var baseIndex = _indices.Length;

        _vertices.Add(new TextVertex
        {
            Position = Vector2.Transform(new Vector2(bounds.X, bounds.Y), Graphics.Transform),
            UV = new Vector2(0, 0),
            Color = color
        });
        _vertices.Add(new TextVertex
        {
            Position = Vector2.Transform(new Vector2(bounds.X + bounds.Width, bounds.Y), Graphics.Transform),
            UV = new Vector2(1, 0),
            Color = color
        });
        _vertices.Add(new TextVertex
        {
            Position = Vector2.Transform(new Vector2(bounds.X + bounds.Width, bounds.Y + bounds.Height), Graphics.Transform),
            UV = new Vector2(1, 1),
            Color = color
        });
        _vertices.Add(new TextVertex
        {
            Position = Vector2.Transform(new Vector2(bounds.X, bounds.Y + bounds.Height), Graphics.Transform),
            UV = new Vector2(0, 1),
            Color = color
        });

        _indices.Add((ushort)(baseVertex + 0));
        _indices.Add((ushort)(baseVertex + 1));
        _indices.Add((ushort)(baseVertex + 2));
        _indices.Add((ushort)(baseVertex + 2));
        _indices.Add((ushort)(baseVertex + 3));
        _indices.Add((ushort)(baseVertex + 0));
        Graphics.DrawElements(6, baseIndex);
    }

    private const int MaxWrappedLines = 256;

    private struct LineInfo
    {
        public int Start;
        public int End; // exclusive
    }

    private static float MeasureLineWidth(ReadOnlySpan<char> text, Font font, float fontSize)
    {
        var width = 0f;
        for (var i = 0; i < text.Length; i++)
        {
            var glyph = font.GetGlyph(text[i]);
            var advance = glyph.Advance * fontSize;
            if (i + 1 < text.Length)
                advance += font.GetKerning(text[i], text[i + 1]) * fontSize;
            width += advance;
        }
        return width;
    }

    private static int ComputeLineBreaks(
        ReadOnlySpan<char> text, Font font, float fontSize, float maxWidth,
        Span<LineInfo> lines)
    {
        if (text.Length == 0) return 0;

        var lineCount = 0;
        var lineStart = 0;
        var currentX = 0f;
        var wordStart = 0;
        var wordStartX = 0f;

        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];

            if (ch == '\n')
            {
                if (lineCount < lines.Length)
                    lines[lineCount] = new LineInfo { Start = lineStart, End = i };
                lineCount++;
                lineStart = i + 1;
                currentX = 0f;
                wordStart = lineStart;
                wordStartX = 0f;
                continue;
            }

            var glyph = font.GetGlyph(ch);
            var advance = glyph.Advance * fontSize;
            if (i + 1 < text.Length && text[i + 1] != '\n')
                advance += font.GetKerning(ch, text[i + 1]) * fontSize;

            if (ch == ' ')
            {
                currentX += advance;
                wordStart = i + 1;
                wordStartX = currentX;
                continue;
            }

            var nextX = currentX + advance;

            if (nextX > maxWidth && currentX > 0)
            {
                if (wordStart > lineStart)
                {
                    // Word break: break before the current word
                    if (lineCount < lines.Length)
                        lines[lineCount] = new LineInfo { Start = lineStart, End = wordStart };
                    lineCount++;
                    lineStart = wordStart;
                    currentX = nextX - wordStartX;
                }
                else
                {
                    // Character break: single word exceeds line
                    if (lineCount < lines.Length)
                        lines[lineCount] = new LineInfo { Start = lineStart, End = i };
                    lineCount++;
                    lineStart = i;
                    currentX = advance;
                }
                wordStart = lineStart;
                wordStartX = 0f;
            }
            else
            {
                currentX = nextX;
            }
        }

        // Emit final line
        if (lineStart <= text.Length)
        {
            if (lineCount < lines.Length)
                lines[lineCount] = new LineInfo { Start = lineStart, End = text.Length };
            lineCount++;
        }

        return lineCount;
    }

    public static Vector2 MeasureWrapped(ReadOnlySpan<char> text, Font font, float fontSize, float maxWidth)
    {
        if (text.Length == 0) return Vector2.Zero;

        Span<LineInfo> lines = stackalloc LineInfo[MaxWrappedLines];
        var lineCount = ComputeLineBreaks(text, font, fontSize, maxWidth, lines);
        if (lineCount == 0) return Vector2.Zero;

        var maxLineWidth = 0f;
        var count = Math.Min(lineCount, MaxWrappedLines);
        for (var i = 0; i < count; i++)
        {
            var line = lines[i];
            var end = line.End;
            while (end > line.Start && text[end - 1] == ' ') end--;
            var lineWidth = MeasureLineWidth(text[line.Start..end], font, fontSize);
            maxLineWidth = MathF.Max(maxLineWidth, lineWidth);
        }

        return new Vector2(maxLineWidth, lineCount * font.LineHeight * fontSize);
    }

    private static void EmitGlyph(
        in FontGlyph glyph, float x0, float y0, float x1, float y1,
        Color32 color, Color32 outlineColor, float outlineWidth, float outlineSoftness,
        ref int baseIndex, ushort order)
    {
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
        Graphics.DrawElements(6, baseIndex, order);

        baseIndex += 6;
    }

    private static float MeasureEllipsis(Font font, float fontSize)
    {
        var dotGlyph = font.GetGlyph('.');
        var dotAdvance = dotGlyph.Advance * fontSize;
        var kerning = font.GetKerning('.', '.') * fontSize;
        return dotAdvance * 3 + kerning * 2;
    }

    private static void DrawEllipsis(
        Font font, float fontSize, ref float currentX, float offsetY, float baselineY,
        Color32 color, Color32 outlineColor, float outlineWidth, float outlineSoftness,
        ref int baseIndex, ushort order)
    {
        for (var d = 0; d < 3; d++)
        {
            var dotGlyph = font.GetGlyph('.');
            if (dotGlyph.UVMax.X > dotGlyph.UVMin.X && dotGlyph.UVMax.Y > dotGlyph.UVMin.Y)
            {
                var x0 = currentX + dotGlyph.Bearing.X * fontSize;
                var x1 = x0 + dotGlyph.Size.X * fontSize;
                var y0 = offsetY + baselineY + dotGlyph.Bearing.Y * fontSize - dotGlyph.Size.Y * fontSize;
                var y1 = y0 + dotGlyph.Size.Y * fontSize;
                EmitGlyph(in dotGlyph, x0, y0, x1, y1,
                    color, outlineColor, outlineWidth, outlineSoftness,
                    ref baseIndex, order);
            }
            var dotAdvance = dotGlyph.Advance * fontSize;
            if (d + 1 < 3)
                dotAdvance += font.GetKerning('.', '.') * fontSize;
            currentX += dotAdvance;
        }
    }

    public static void DrawWrapped(
        in ReadOnlySpan<char> text, Font font, float fontSize, float maxWidth,
        float containerWidth, float alignXFactor, float maxHeight = 0, int order = 0)
    {
        Debug.Assert(order >= 0 && order <= ushort.MaxValue);

        if (text.Length == 0 || _textShader == null)
            return;

        var atlasTexture = font.AtlasTexture;
        if (atlasTexture == null)
            return;

        Span<LineInfo> lines = stackalloc LineInfo[MaxWrappedLines];
        var lineCount = ComputeLineBreaks(text, font, fontSize, maxWidth, lines);
        if (lineCount == 0) return;

        using var _ = Graphics.PushState();
        Graphics.SetShader(_textShader);
        Graphics.SetTexture(atlasTexture, filter: TextureFilter.Linear);
        Graphics.SetMesh(_mesh);

        var lineHeight = font.LineHeight * fontSize;
        var baselineY = (font.Baseline + font.InternalLeading * 0.5f) * fontSize;
        var baseIndex = _indices.Length;

        var color = Graphics.Color.ToColor32();
        var outlineColor = OutlineColor;
        var outlineWidth = OutlineWidth;
        var outlineSoftness = OutlineSoftness;
        var displayScale = Application.Platform.DisplayScale;
        var count = Math.Min(lineCount, MaxWrappedLines);

        // Determine visible lines based on max height
        var maxLines = maxHeight > 0 ? Math.Max(1, (int)(maxHeight / lineHeight)) : count;
        var visibleCount = Math.Min(count, maxLines);
        var truncated = visibleCount < lineCount;
        var ellipsisWidth = truncated ? MeasureEllipsis(font, fontSize) : 0f;
        var orderU16 = (ushort)order;

        for (var lineIdx = 0; lineIdx < visibleCount; lineIdx++)
        {
            var line = lines[lineIdx];
            var end = line.End;
            while (end > line.Start && text[end - 1] == ' ') end--;

            var isLastTruncated = truncated && lineIdx == visibleCount - 1;

            var lineSlice = text[line.Start..end];
            var lineWidth = isLastTruncated
                ? MeasureLineWidth(lineSlice, font, fontSize) + ellipsisWidth
                : MeasureLineWidth(lineSlice, font, fontSize);
            var alignWidth = isLastTruncated
                ? Math.Min(lineWidth, maxWidth)
                : lineWidth;
            var offsetX = (containerWidth - alignWidth) * alignXFactor;
            var offsetY = lineIdx * lineHeight;

            offsetX = MathF.Round(offsetX * displayScale) / displayScale;

            var currentX = offsetX;
            var drewEllipsis = false;

            for (var i = 0; i < lineSlice.Length; i++)
            {
                var ch = lineSlice[i];
                var glyph = font.GetGlyph(ch);

                var advance = glyph.Advance * fontSize;
                if (i + 1 < lineSlice.Length)
                    advance += font.GetKerning(ch, lineSlice[i + 1]) * fontSize;

                // On the last truncated line, check if we need to stop for ellipsis
                if (isLastTruncated && currentX - offsetX + advance + ellipsisWidth > maxWidth)
                {
                    DrawEllipsis(font, fontSize, ref currentX, offsetY, baselineY,
                        color, outlineColor, outlineWidth, outlineSoftness,
                        ref baseIndex, orderU16);
                    drewEllipsis = true;
                    break;
                }

                if (glyph.UVMax.X > glyph.UVMin.X && glyph.UVMax.Y > glyph.UVMin.Y)
                {
                    var x0 = currentX + glyph.Bearing.X * fontSize;
                    var x1 = x0 + glyph.Size.X * fontSize;
                    var y0 = offsetY + baselineY + glyph.Bearing.Y * fontSize - glyph.Size.Y * fontSize;
                    var y1 = y0 + glyph.Size.Y * fontSize;
                    EmitGlyph(in glyph, x0, y0, x1, y1,
                        color, outlineColor, outlineWidth, outlineSoftness,
                        ref baseIndex, orderU16);
                }

                currentX += advance;
            }

            // If all chars fit but line is truncated, append ellipsis after text
            if (isLastTruncated && !drewEllipsis)
            {
                DrawEllipsis(font, fontSize, ref currentX, offsetY, baselineY,
                    color, outlineColor, outlineWidth, outlineSoftness,
                    ref baseIndex, orderU16);
            }
        }
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
        Graphics.SetTexture(atlasTexture, filter: TextureFilter.Linear);
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
