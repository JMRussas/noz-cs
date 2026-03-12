//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;
using System.Runtime.CompilerServices;

namespace NoZ;

public static partial class ElementTree
{
    private static int _drawSortGroup;

    private static void SetElementScissor(ref Element e)
    {
        ref var ltw = ref e.Transform;
        var topLeft = Vector2.Transform(e.Rect.Position, ltw);
        var bottomRight = Vector2.Transform(e.Rect.Position + e.Rect.Size, ltw);
        var screenTopLeft = UI.Camera!.WorldToScreen(topLeft);
        var screenBottomRight = UI.Camera!.WorldToScreen(bottomRight);
        var screenHeight = Application.WindowSize.Y;
        Graphics.SetScissor(
            (int)screenTopLeft.X,
            (int)(screenHeight - screenBottomRight.Y),
            (int)(screenBottomRight.X - screenTopLeft.X),
            (int)(screenBottomRight.Y - screenTopLeft.Y));
    }

    internal static void Draw()
    {
        if (_elements.Length < 2) return;

        _drawOpacity = 1.0f;
        _drawSortGroup = 0;
        using var _ = Graphics.PushState();
        Graphics.SetBlendMode(BlendMode.Alpha);
        Graphics.SetShader(_shader);
        Graphics.SetLayer(UI.Config.UILayer);
        Graphics.SetSortGroup(0);
        Graphics.SetTransform(Matrix3x2.Identity);
        Graphics.SetMesh(_mesh);

        DrawElement(0);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Color ApplyOpacity(Color c) => c.WithAlpha(c.A * _drawOpacity);

    private static void DrawElement(int offset)
    {
        ref var e = ref GetElement(offset);
        ref var t = ref e.Transform;
        var previousOpacity = _drawOpacity;
        var setScissor = false;

        switch (e.Type)
        {
            case ElementType.Fill:
            {
                ref var d = ref e.Data.Fill;
                if (!d.Color.IsTransparent || !d.Color2.IsTransparent || d.BorderWidth > 0)
                {
                    if (d.Color2.IsTransparent)
                    {
                        DrawTexturedRect(e.Rect, t, null, ApplyOpacity(d.Color), d.Radius, d.BorderWidth, ApplyOpacity(d.BorderColor));
                    }
                    else
                    {
                        ResolveGradientCorners(d.Color, d.Color2, d.GradientAngle, out var cTL, out var cTR, out var cBR, out var cBL);
                        DrawTexturedRect(e.Rect, t, null, ApplyOpacity(cTL), ApplyOpacity(cTR), ApplyOpacity(cBR), ApplyOpacity(cBL), d.Radius, d.BorderWidth, ApplyOpacity(d.BorderColor));
                    }
                }
                break;
            }

            case ElementType.Text:
                DrawLabel(ref e);
                break;

            case ElementType.Image:
                DrawImage(ref e);
                break;

            case ElementType.Scene:
                DrawScene(ref e);
                break;

            case ElementType.EditableText:
                DrawEditableText(ref e);
                break;

            case ElementType.Clip:
            case ElementType.Scroll:
                SetElementScissor(ref e);
                setScissor = true;
                break;

            case ElementType.Opacity:
                _drawOpacity *= e.Data.Opacity;
                break;
        }

        // Popup: bump sort group so popup content renders on top
        var prevSortGroup = _drawSortGroup;
        if (e.Type == ElementType.Popup)
        {
            _drawSortGroup++;
            Graphics.SetSortGroup(_drawSortGroup);
        }

        // Recurse children
        var childOffset = (int)e.FirstChild;
        for (int i = 0; i < e.ChildCount; i++)
        {
            ref var child = ref GetElement(childOffset);
            DrawElement(childOffset);
            childOffset = child.NextSibling;
        }

        if (e.Type == ElementType.Popup)
        {
            _drawSortGroup = prevSortGroup;
            Graphics.SetSortGroup(_drawSortGroup);
        }

        if (setScissor)
            Graphics.ClearScissor();

        _drawOpacity = previousOpacity;
    }

    private static void DrawTexturedRect(
        in Rect rect,
        in Matrix3x2 transform,
        Texture? texture,
        Color color,
        BorderRadius borderRadius = default,
        float borderWidth = 0,
        Color borderColor = default,
        ushort order = 0)
    {
        DrawTexturedRect(rect, transform, texture, color, color, color, color, borderRadius, borderWidth, borderColor, order);
    }

    private static void DrawTexturedRect(
        in Rect rect,
        in Matrix3x2 transform,
        Texture? texture,
        Color colorTL,
        Color colorTR,
        Color colorBR,
        Color colorBL,
        BorderRadius borderRadius = default,
        float borderWidth = 0,
        Color borderColor = default,
        ushort order = 0)
    {
        var vertexOffset = _vertices.Length;
        var indexOffset = _indices.Length;

        if (!_vertices.CheckCapacity(4) || !_indices.CheckCapacity(6))
            return;

        var w = rect.Width;
        var h = rect.Height;

        var maxR = MathF.Min(w, h) / 2;
        var radii = new Vector4(
            MathF.Min(borderRadius.TopLeft, maxR),
            MathF.Min(borderRadius.TopRight, maxR),
            MathF.Min(borderRadius.BottomLeft, maxR),
            MathF.Min(borderRadius.BottomRight, maxR));
        var rectSize = new Vector2(w, h);

        var p0 = Vector2.Transform(new Vector2(rect.X, rect.Y), transform);
        var p1 = Vector2.Transform(new Vector2(rect.X + w, rect.Y), transform);
        var p2 = Vector2.Transform(new Vector2(rect.X + w, rect.Y + h), transform);
        var p3 = Vector2.Transform(new Vector2(rect.X, rect.Y + h), transform);

        _vertices.Add(new UIVertex
        {
            Position = p0, UV = new Vector2(0, 0), Normal = rectSize,
            Color = colorTL, BorderRatio = borderWidth, BorderColor = borderColor, CornerRadii = radii
        });
        _vertices.Add(new UIVertex
        {
            Position = p1, UV = new Vector2(1, 0), Normal = rectSize,
            Color = colorTR, BorderRatio = borderWidth, BorderColor = borderColor, CornerRadii = radii
        });
        _vertices.Add(new UIVertex
        {
            Position = p2, UV = new Vector2(1, 1), Normal = rectSize,
            Color = colorBR, BorderRatio = borderWidth, BorderColor = borderColor, CornerRadii = radii
        });
        _vertices.Add(new UIVertex
        {
            Position = p3, UV = new Vector2(0, 1), Normal = rectSize,
            Color = colorBL, BorderRatio = borderWidth, BorderColor = borderColor, CornerRadii = radii
        });

        _indices.Add((ushort)vertexOffset);
        _indices.Add((ushort)(vertexOffset + 1));
        _indices.Add((ushort)(vertexOffset + 2));
        _indices.Add((ushort)(vertexOffset + 2));
        _indices.Add((ushort)(vertexOffset + 3));
        _indices.Add((ushort)vertexOffset);

        using var _ = Graphics.PushState();
        Graphics.SetTexture(texture ?? Graphics.WhiteTexture, filter: texture?.Filter ?? TextureFilter.Point);
        Graphics.SetMesh(_mesh);
        Graphics.DrawElements(6, indexOffset, order: order);
    }

    // Angle convention: 0 = left-to-right, 90 = top-to-bottom, 180 = right-to-left.
    // Output order matches vertex winding: TL, TR, BR, BL (clockwise).
    private static void ResolveGradientCorners(Color c1, Color c2, float angleDeg, out Color tl, out Color tr, out Color br, out Color bl)
    {
        var rad = angleDeg * (MathF.PI / 180f);
        var dx = MathF.Cos(rad);
        var dy = MathF.Sin(rad);

        // Project each corner onto the gradient axis, normalize to 0..1
        var dTL = 0f;
        var dTR = dx;
        var dBL = dy;
        var dBR = dx + dy;

        var min = MathF.Min(MathF.Min(dTL, dTR), MathF.Min(dBL, dBR));
        var max = MathF.Max(MathF.Max(dTL, dTR), MathF.Max(dBL, dBR));
        var range = max - min;
        if (range < 0.0001f) range = 1f;

        tl = Color.Mix(c1, c2, (dTL - min) / range);
        tr = Color.Mix(c1, c2, (dTR - min) / range);
        br = Color.Mix(c1, c2, (dBR - min) / range);
        bl = Color.Mix(c1, c2, (dBL - min) / range);
    }

    internal static void Flush()
    {
        if (_indices.Length == 0) return;
        Graphics.Driver.BindMesh(_mesh.Handle);
        Graphics.Driver.UpdateMesh(_mesh.Handle, _vertices.AsByteSpan(), _indices.AsSpan());
        _vertices.Clear();
        _indices.Clear();
    }

    private static void DrawLabel(ref Element e)
    {
        ref var d = ref e.Data.Text;
        ref var t = ref e.Transform;
        var font = (Font)_assets[d.Font]!;
        var text = d.Text.AsReadOnlySpan();
        var fontSize = d.FontSize;

        switch (d.Overflow)
        {
            case TextOverflow.Wrap:
            {
                var wrappedHeight = TextRender.MeasureWrapped(text, font, fontSize, e.Rect.Width).Y;
                var effectiveHeight = wrappedHeight + font.InternalLeading * fontSize;
                var offsetY = (e.Rect.Height - effectiveHeight) * d.Align.Y.ToFactor();
                var displayScale = Application.Platform.DisplayScale;
                offsetY = MathF.Round(offsetY * displayScale) / displayScale;
                var transform = Matrix3x2.CreateTranslation(e.Rect.Position + new Vector2(0, offsetY)) * t;

                using (Graphics.PushState())
                {
                    Graphics.SetColor(ApplyOpacity(d.Color));
                    Graphics.SetTransform(transform);
                    TextRender.DrawWrapped(text, font, fontSize, e.Rect.Width,
                        e.Rect.Width, d.Align.X.ToFactor(), e.Rect.Height);
                }
                break;
            }

            case TextOverflow.Scale:
            {
                var textWidth = TextRender.Measure(text, font, fontSize).X;
                var scaledFontSize = fontSize;
                if (textWidth > e.Rect.Width && e.Rect.Width > 0)
                    scaledFontSize = fontSize * (e.Rect.Width / textWidth);

                var textOffset = GetTextOffset(text, font, scaledFontSize, e.Rect.Size, d.Align.X, d.Align.Y);
                var transform = Matrix3x2.CreateTranslation(e.Rect.Position + textOffset) * t;

                using (Graphics.PushState())
                {
                    Graphics.SetColor(ApplyOpacity(d.Color));
                    Graphics.SetTransform(transform);
                    TextRender.Draw(text, font, scaledFontSize);
                }
                break;
            }

            case TextOverflow.Ellipsis:
            {
                var textOffset = GetTextOffset(text, font, fontSize, e.Rect.Size, d.Align.X, d.Align.Y);
                var transform = Matrix3x2.CreateTranslation(e.Rect.Position + textOffset) * t;

                using (Graphics.PushState())
                {
                    Graphics.SetColor(ApplyOpacity(d.Color));
                    Graphics.SetTransform(transform);
                    TextRender.DrawEllipsized(text, font, fontSize, e.Rect.Width);
                }
                break;
            }

            default:
            {
                var textOffset = GetTextOffset(text, font, fontSize, e.Rect.Size, d.Align.X, d.Align.Y);
                var transform = Matrix3x2.CreateTranslation(e.Rect.Position + textOffset) * t;

                using (Graphics.PushState())
                {
                    Graphics.SetColor(ApplyOpacity(d.Color));
                    Graphics.SetTransform(transform);
                    TextRender.Draw(text, font, fontSize);
                }
                break;
            }
        }
    }

    private static Vector2 GetTextOffset(ReadOnlySpan<char> text, Font font, float fontSize, in Vector2 containerSize, Align alignX, Align alignY)
    {
        var textWidth = TextRender.Measure(text, font, fontSize).X;
        var textHeight = (font.LineHeight + font.InternalLeading) * fontSize;
        var offset = new Vector2(
            (containerSize.X - textWidth) * alignX.ToFactor(),
            (containerSize.Y - textHeight) * alignY.ToFactor()
        );

        var displayScale = Application.Platform.DisplayScale;
        offset.X = MathF.Round(offset.X * displayScale) / displayScale;
        offset.Y = MathF.Round(offset.Y * displayScale) / displayScale;
        return offset;
    }

    private static Vector2 GetImageScale(ImageStretch stretch, Vector2 srcSize, Vector2 dstSize)
    {
        var scale = dstSize / srcSize;
        return stretch switch
        {
            ImageStretch.None => Vector2.One,
            ImageStretch.Uniform => new Vector2(MathF.Min(scale.X, scale.Y)),
            _ => scale
        };
    }

    private static void DrawImage(ref Element e)
    {
        ref var d = ref e.Data.Image;
        ref var t = ref e.Transform;
        var asset = _assets[d.Asset];
        if (asset == null) return;

        var srcSize = new Vector2(d.Width, d.Height);
        var dstSize = new Vector2(
            d.Size.Width.IsPercent ? e.Rect.Width * d.Size.Width.Value : e.Rect.Width,
            d.Size.Height.IsPercent ? e.Rect.Height * d.Size.Height.Value : e.Rect.Height);
        var scale = GetImageScale(d.Stretch, srcSize, dstSize);
        var scaledSize = scale * srcSize;
        var offset = e.Rect.Position + (e.Rect.Size - scaledSize) * new Vector2(d.Align.X.ToFactor(), d.Align.Y.ToFactor());

        if (asset is Sprite sprite)
        {
            using var _ = Graphics.PushState();
            Graphics.SetColor(ApplyOpacity(d.Color));
            Graphics.SetTextureFilter(sprite.TextureFilter);

            if (sprite.IsSliced)
            {
                Graphics.SetTransform(t);
                Graphics.DrawSliced(sprite, new Rect(offset.X, offset.Y, scaledSize.X, scaledSize.Y));
            }
            else
            {
                offset -= new Vector2(sprite.Bounds.X, sprite.Bounds.Y) * scale;
                var transform = Matrix3x2.CreateScale(scale * sprite.PixelsPerUnit) * Matrix3x2.CreateTranslation(offset) * t;
                Graphics.SetTransform(transform);
                Graphics.DrawFlat(sprite, bone: -1);
            }
        }
        else if (asset is Texture texture)
        {
            DrawTexturedRect(
                new Rect(offset.X, offset.Y, scaledSize.X, scaledSize.Y),
                t,
                texture,
                ApplyOpacity(d.Color));
        }
    }

    private static void DrawScene(ref Element e)
    {
        ref var d = ref e.Data.Scene;
        var camera = (Camera)_assets[d.Camera]!;
        var draw = (Action)_assets[d.DrawCallback]!;

        ref var t = ref e.Transform;
        var topLeft = Vector2.Transform(e.Rect.Position, t);
        var bottomRight = Vector2.Transform(e.Rect.Position + e.Rect.Size, t);
        var screenTopLeft = UI.Camera!.WorldToScreen(topLeft);
        var screenBottomRight = UI.Camera!.WorldToScreen(bottomRight);
        var rtW = (int)MathF.Ceiling(screenBottomRight.X - screenTopLeft.X);
        var rtH = (int)MathF.Ceiling(screenBottomRight.Y - screenTopLeft.Y);

        if (rtW <= 0 || rtH <= 0)
        {
            var winSize = Application.WindowSize;
            rtW = winSize.X;
            rtH = winSize.Y;
            if (rtW <= 0 || rtH <= 0)
                return;
        }

        var rt = RenderTexturePool.Acquire(rtW, rtH, d.SampleCount);

        Graphics.BeginPass(rt, d.ClearColor);
        Graphics.SetCamera(camera);
        Graphics.SetTransform(Matrix3x2.Identity);
        Graphics.SetViewport(0, 0, rt.Width, rt.Height);
        UI.SceneViewport = new RectInt(0, 0, rt.Width, rt.Height);
        draw();
        UI.SceneViewport = null;
        Graphics.EndPass();
        Graphics.SetCamera(UI.Camera);

        using (Graphics.PushState())
        {
            Graphics.SetColor(ApplyOpacity(Color.White));
            Graphics.SetTextureFilter(TextureFilter.Point);
            Graphics.Draw(rt, topLeft, bottomRight);
        }
    }


    internal static bool MouseOverScene;

    internal static Vector2 ScreenSize;
    internal static Vector2 MouseWorldPosition;

#if DEBUG
    internal static string DebugDumpTree()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"ElementTree: {_elements.Length} elements, Frame={_frame}, Screen={ScreenSize.X:0}x{ScreenSize.Y:0}");
        sb.AppendLine("───────────────────────────────");

        if (_elements.Length == 0)
        {
            sb.AppendLine("(empty)");
            return sb.ToString();
        }

        DebugDumpElement(sb, 0, 0);
        return sb.ToString();
    }

    private static void DebugDumpElement(System.Text.StringBuilder sb, int index, int depth)
    {
        if (index < 0 || index >= _elements.Length) return;
        if (depth > 100) { sb.AppendLine($"{new string(' ', depth * 2)}... (depth limit)"); return; }

        ref var e = ref GetElement(index);
        var indent = new string(' ', depth * 2);

        sb.Append($"{indent}[{index}] {e.Type}");
        sb.Append($" {e.Rect.X:0},{e.Rect.Y:0} {e.Rect.Width:0}x{e.Rect.Height:0}");
        sb.Append($" children={e.ChildCount} first={e.FirstChild} next={e.NextSibling}");

        switch (e.Type)
        {
            case ElementType.Widget:
            {
                ref var d = ref e.Data.Widget;
                sb.Append($" id={d.Id}");
                break;
            }
            case ElementType.Size:
            {
                sb.Append($" size={e.Data.Size}");
                break;
            }
            case ElementType.Fill:
            {
                ref var d = ref e.Data.Fill;
                sb.Append($" color=#{(int)(d.Color.R*255):X2}{(int)(d.Color.G*255):X2}{(int)(d.Color.B*255):X2}");
                break;
            }
            case ElementType.Padding:
            {
                ref var d = ref e.Data;
                sb.Append($" pad={d.Padding.T:0},{d.Padding.R:0},{d.Padding.B:0},{d.Padding.L:0}");
                break;
            }
            case ElementType.Margin:
            {
                ref var d = ref e.Data;
                sb.Append($" margin={d.Margin.T:0},{d.Margin.R:0},{d.Margin.B:0},{d.Margin.L:0}");
                break;
            }
            case ElementType.Row:
            {
                if (e.Data.Spacing > 0) sb.Append($" spacing={e.Data.Spacing:0}");
                break;
            }
            case ElementType.Column:
            {
                if (e.Data.Spacing > 0) sb.Append($" spacing={e.Data.Spacing:0}");
                break;
            }
            case ElementType.Flex:
            {
                if (e.Data.Flex != 1f) sb.Append($" flex={e.Data.Flex}");
                break;
            }
            case ElementType.Align:
            {
                sb.Append($" align={e.Data.Align.X},{e.Data.Align.Y}");
                break;
            }
            case ElementType.Text:
            {
                ref var d = ref e.Data.Text;
                var text = d.Text.AsReadOnlySpan().ToString();
                if (text.Length > 40) text = text[..37] + "...";
                sb.Append($" \"{text}\" size={d.FontSize:0}");
                break;
            }
            case ElementType.Image:
            {
                ref var d = ref e.Data.Image;
                var asset = _assets[d.Asset];
                if (asset != null) sb.Append($" asset={asset}");
                break;
            }
            case ElementType.Opacity:
            {
                sb.Append($" opacity={e.Data.Opacity:0.##}");
                break;
            }
            case ElementType.Spacer:
            {
                sb.Append($" {e.Data.Spacing}");
                break;
            }
            case ElementType.Popup:
            {
                ref var d = ref e.Data.Popup;
                sb.Append($" anchor={d.AnchorRect.X:0},{d.AnchorRect.Y:0}");
                break;
            }
            case ElementType.Scene:
            {
                ref var d = ref e.Data.Scene;
                sb.Append($" size={d.Size} samples={d.SampleCount}");
                break;
            }
            case ElementType.Scroll:
            {
                ref var d = ref e.Data.Scroll;
                sb.Append($" speed={d.ScrollSpeed}");
                break;
            }
        }

        sb.AppendLine();

        // Recurse children
        var childOffset = (int)e.FirstChild;
        for (int i = 0; i < e.ChildCount; i++)
        {
            if (childOffset <= 0 && i > 0) break; // safety
            if (childOffset >= _elements.Length) break;
            DebugDumpElement(sb, childOffset, depth + 1);
            ref var child = ref GetElement(childOffset);
            childOffset = child.NextSibling;
        }
    }
#endif
}
