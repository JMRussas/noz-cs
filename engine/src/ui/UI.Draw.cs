//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

// #define NOZ_UI_DEBUG

using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace NoZ;

public static partial class UI
{
    private const int MaxUIVertices = 16384;
    private const int MaxUIIndices = 32768;
    private static nuint _mesh;
    private static NativeArray<UIVertex> _vertices;
    private static NativeArray<ushort> _indices;
    private static Shader _shader = null!;
    private static float _drawOpacity = 1.0f;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Color ApplyOpacity(Color c) => c.WithAlpha(c.A * _drawOpacity);

    // Draw pass
    private static void DrawElement(int elementIndex, bool isPopup)
    {
        ref var e = ref _elements[elementIndex];

        LogUI(e, $"{e.Type}:{Log.Params([
            ("Index", e.Index, true),
            ("Rect", e.Rect, true)
        ])}");

        var setScissor = false;
        var previousOpacity = _drawOpacity;

        switch (e.Type)
        {
            case ElementType.Container:
            case ElementType.Column:
            case ElementType.Row:
                DrawContainer(ref e);
                break;

            case ElementType.Label:
                DrawLabel(ref e);
                break;

            case ElementType.Image:
                DrawImage(ref e);
                break;

            case ElementType.TextBox:
                DrawTextBox(ref e);
                break;

            case ElementType.TextArea:
                DrawTextArea(ref e);
                break;

            case ElementType.Scene:
                DrawScene(ref e);
                return;

            case ElementType.Popup when !isPopup:
                return;

            case ElementType.Opacity:
                _drawOpacity *= e.Data.Opacity.Value;
                break;

            case ElementType.Scrollable:
            {
                var topLeft = Vector2.Transform(e.Rect.Position, e.LocalToWorld);
                var bottomRight = Vector2.Transform(e.Rect.Position + e.Rect.Size, e.LocalToWorld);
                var screenTopLeft = Camera!.WorldToScreen(topLeft);
                var screenBottomRight = Camera!.WorldToScreen(bottomRight);
                var screenHeight = Application.WindowSize.Y;
                var scissorX = (int)screenTopLeft.X;
                var scissorW = (int)(screenBottomRight.X - screenTopLeft.X);
                var scissorH = (int)(screenBottomRight.Y - screenTopLeft.Y);
                var scissorY = (int)(screenHeight - screenBottomRight.Y);
                Graphics.SetScissor(scissorX, scissorY, scissorW, scissorH);
                setScissor = true;
                break;
            }
        }

        var childElementIndex = elementIndex + 1;
        for (var childIndex = 0; childIndex < e.ChildCount; childIndex++)
        {
            ref var child = ref _elements[childElementIndex];
            DrawElement(childElementIndex, isPopup);
            childElementIndex = child.NextSiblingIndex;
        }

        if (setScissor)
            Graphics.ClearScissor();

        // Draw scrollbar after children (outside scissor)
        if (e.Type == ElementType.Scrollable)
            DrawScrollbar(ref e);

        _drawOpacity = previousOpacity;
    }

    private static void DrawScrollbar(ref Element e)
    {
        ref var s = ref e.Data.Scrollable;
        var viewportHeight = e.Rect.Height;
        var maxScroll = Math.Max(0, s.ContentHeight - viewportHeight);

        // Check visibility
        if (s.ScrollbarVisibility == ScrollbarVisibility.Never)
            return;
        if (s.ScrollbarVisibility == ScrollbarVisibility.Auto && maxScroll <= 0)
            return;

        // Calculate track position (right side of scrollable, in world space)
        var pos = Vector2.Transform(e.Rect.Position, e.LocalToWorld);
        var trackX = pos.X + e.Rect.Width - s.ScrollbarWidth - s.ScrollbarPadding;
        var trackY = pos.Y + s.ScrollbarPadding;
        var trackH = viewportHeight - s.ScrollbarPadding * 2;

        // Draw track
        DrawRect(
            new Rect(trackX, trackY, s.ScrollbarWidth, trackH),
            ApplyOpacity(s.ScrollbarTrackColor),
            s.ScrollbarBorderRadius
        );

        // Draw thumb if there's content to scroll
        if (maxScroll > 0 && s.ContentHeight > 0)
        {
            var thumbHeightRatio = viewportHeight / s.ContentHeight;
            var thumbH = Math.Max(s.ScrollbarMinThumbHeight, trackH * thumbHeightRatio);
            var scrollRatio = s.Offset / maxScroll;
            var thumbY = trackY + scrollRatio * (trackH - thumbH);

            DrawRect(
                new Rect(trackX, thumbY, s.ScrollbarWidth, thumbH),
                ApplyOpacity(s.ScrollbarThumbColor),
                s.ScrollbarBorderRadius
            );
        }
    }

    private static void DrawElements()
    {
        LogUI("Draw", condition: () => _elementCount > 0);

        if (_elementCount == 0) return;

        _drawOpacity = 1.0f;
        using var _ = Graphics.PushState();
        Graphics.SetBlendMode(BlendMode.Alpha);
        Graphics.SetShader(_shader);
        Graphics.SetLayer(Config.UILayer);
        Graphics.SetSortGroup(0);
        Graphics.SetTransform(Matrix3x2.Identity);
        Graphics.SetMesh(_mesh);

        DrawElement(0, false);

        // Popups
        for (var i = 0; i < _popupCount; i++)
        {
            Graphics.SetSortGroup(i + 1);
            var popupIndex = _popups[i];
            DrawElement(popupIndex, true);
        }
    }

    private static void DrawContainer(ref Element e)
    {
        ref var style = ref e.Data.Container;
        if (style.Color.IsTransparent && style.Border.Width <= 0)
            return;

        var topLeft = Vector2.Transform(e.Rect.Position, e.LocalToWorld);
        var bottomRight = Vector2.Transform(e.Rect.Position + e.Rect.Size, e.LocalToWorld);
        DrawRect(
            new Rect(topLeft.X, topLeft.Y, bottomRight.X - topLeft.X, bottomRight.Y - topLeft.Y),
            ApplyOpacity(style.Color),
            style.Border.Radius,
            style.Border.Width,
            ApplyOpacity(style.Border.Color),
            order: e.Data.Container.Order
        );
    }

    private static Vector2 GetTextOffset(ReadOnlySpan<char> text, Font font, float fontSize, in Vector2 containerSize, Align alignX, Align alignY)
    {
        var size = new Vector2(TextRender.Measure(text, font, fontSize).X, font.LineHeight * fontSize);
        var offset = new Vector2(
            (containerSize.X - size.X) * alignX.ToFactor(),
            (containerSize.Y - size.Y) * alignY.ToFactor()
        );

        var displayScale = Application.Platform.DisplayScale;
        offset.X = MathF.Round(offset.X * displayScale) / displayScale;
        offset.Y = MathF.Round(offset.Y * displayScale) / displayScale;
        return offset;
    }

    internal static void DrawText(string text, Font font, float fontSize, Color color, Matrix3x2 localToWorld, Vector2 containerSize, Align alignX = Align.Min, Align alignY = Align.Center)
    {
        var offset = GetTextOffset(text, font, fontSize, containerSize, alignX, alignY);
        var transform = localToWorld * Matrix3x2.CreateTranslation(offset);

        using (Graphics.PushState())
        {
            Graphics.SetColor(ApplyOpacity(color));
            Graphics.SetTransform(transform);
            TextRender.Draw(text, font, fontSize);
        }
    }

    // :label
    private static void DrawLabel(ref Element e)
    {
        var font = (e.Asset as Font) ?? _defaultFont!;
        var text = e.Data.Label.Text.AsReadOnlySpan();
        var fontSize = e.Data.Label.FontSize;

        if (e.Data.Label.Wrap)
        {
            // Vertical alignment: offset the whole text block within the element
            var wrappedHeight = TextRender.MeasureWrapped(text, font, fontSize, e.Rect.Width, cacheId: e.Id).Y;
            var offsetY = (e.Rect.Height - wrappedHeight) * e.Data.Label.AlignY.ToFactor();
            var displayScale = Application.Platform.DisplayScale;
            offsetY = MathF.Round(offsetY * displayScale) / displayScale;

            var transform = Matrix3x2.CreateTranslation(e.Rect.Position + new Vector2(0, offsetY)) * e.LocalToWorld;

            using (Graphics.PushState())
            {
                Graphics.SetColor(ApplyOpacity(e.Data.Label.Color));
                Graphics.SetTransform(transform);
                TextRender.DrawWrapped(text, font, fontSize, e.Rect.Width,
                    e.Rect.Width, e.Data.Label.AlignX.ToFactor(), e.Rect.Height,
                    order: e.Data.Label.Order, cacheId: e.Id);
            }
        }
        else
        {
            var textOffset = GetTextOffset(text, font, fontSize, e.Rect.Size, e.Data.Label.AlignX, e.Data.Label.AlignY);
            var transform = Matrix3x2.CreateTranslation(e.Rect.Position + textOffset) * e.LocalToWorld;

            using (Graphics.PushState())
            {
                Graphics.SetColor(ApplyOpacity(e.Data.Label.Color));
                Graphics.SetTransform(transform);
                TextRender.Draw(text, font, fontSize, order: e.Data.Label.Order);
            }
        }
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
        ref var img = ref e.Data.Image;
        if (e.Asset == null) return;

        var srcSize = new Vector2(img.Width, img.Height);
        var scale = GetImageScale(img.Stretch, srcSize, e.Rect.Size);
        var scaledSize = scale * srcSize;
        var offset = e.Rect.Position + (e.Rect.Size - scaledSize) * new Vector2(img.AlignX.ToFactor(), img.AlignY.ToFactor());

        if (e.Asset is Sprite sprite)
        {
            offset -= new Vector2(sprite.Bounds.X, sprite.Bounds.Y) * scale;
            var transform = Matrix3x2.CreateScale(scale * sprite.PixelsPerUnit) * Matrix3x2.CreateTranslation(offset) * e.LocalToWorld;
            using var _ = Graphics.PushState();
            Graphics.SetTransform(transform);
            Graphics.SetColor(ApplyOpacity(img.Color));
            Graphics.SetTextureFilter(sprite.TextureFilter);
            Graphics.DrawFlat(sprite, order: img.Order, bone: -1);
        }
        else if (e.Asset is Texture texture)
        {
            var topLeft = Vector2.Transform(offset, e.LocalToWorld);
            var bottomRight = Vector2.Transform(offset + scaledSize, e.LocalToWorld);
            DrawImage(
                new Rect(topLeft.X, topLeft.Y, bottomRight.X - topLeft.X, bottomRight.Y - topLeft.Y),
                texture,
                ApplyOpacity(img.Color),
                img.BorderRadius
            );
        }
    }

    private static void DrawScene(ref Element e)
    {
        if (e.Asset is not (Camera camera, Action draw))
            return;

        ref var scene = ref e.Data.Scene;

        // Compute element screen rect from current frame's layout (stable)
        var topLeft = Vector2.Transform(e.Rect.Position, e.LocalToWorld);
        var bottomRight = Vector2.Transform(e.Rect.Position + e.Rect.Size, e.LocalToWorld);
        var screenTopLeft = Camera!.WorldToScreen(topLeft);
        var screenBottomRight = Camera!.WorldToScreen(bottomRight);
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

        // Acquire RT and render the scene via callback
        var rt = RenderTexturePool.Acquire(rtW, rtH, scene.SampleCount);

        Graphics.BeginPass(rt, scene.Color);
        Graphics.SetCamera(camera);
        Graphics.SetViewport(0, 0, rt.Width, rt.Height);
        draw();
        Graphics.EndPass();
        Graphics.SetCamera(Camera);

        // Draw the RT to screen
        using (Graphics.PushState())
        {
            Graphics.SetColor(ApplyOpacity(Color.White));
            Graphics.SetTextureFilter(TextureFilter.Point);
            Graphics.Draw(rt, topLeft, bottomRight);
        }
    }

    private static void DrawRect(
        in Rect rect,
        Color color,
        float borderRadius = 0,
        float borderWidth = 0,
        Color borderColor = default,
        ushort order = 0)
    {
        DrawRect(rect, color, BorderRadius.Circular(borderRadius), borderWidth, borderColor, order: order);
    }

    private static void DrawRect(
        in Rect rect,
        Color color,
        BorderRadius borderRadius,
        float borderWidth = 0,
        Color borderColor = default,
        ushort order = 0)
    {
        DrawTexturedRect(rect, Graphics.WhiteTexture, color, borderRadius, borderWidth, borderColor, order: order);
    }

    private static void DrawImage(
        in Rect rect,
        Texture texture,
        Color color,
        BorderRadius borderRadius = default)
    {
        DrawTexturedRect(rect, texture, color, borderRadius, 0, default);
    }

    private static void DrawTexturedRect(
        in Rect rect,
        Texture? texture,
        Color color,
        BorderRadius borderRadius,
        float borderWidth,
        Color borderColor,
        ushort order = 0)
    {
        var vertexOffset = _vertices.Length;
        var indexOffset = _indices.Length;

        if (!_vertices.CheckCapacity(4) || !_indices.CheckCapacity(6))
            return;

        var x = rect.X;
        var y = rect.Y;
        var w = rect.Width;
        var h = rect.Height;

        // Clamp radii to half the rect size to avoid overlap
        var maxR = MathF.Min(w, h) / 2;
        var rTL = MathF.Min(borderRadius.TopLeft, maxR);
        var rTR = MathF.Min(borderRadius.TopRight, maxR);
        var rBL = MathF.Min(borderRadius.BottomLeft, maxR);
        var rBR = MathF.Min(borderRadius.BottomRight, maxR);

        var radii = new Vector4(rTL, rTR, rBL, rBR);
        var rectSize = new Vector2(w, h);

        // Simple 4-vertex quad - shader handles everything
        _vertices.Add(new UIVertex
        {
            Position = new Vector2(x, y),
            UV = new Vector2(0, 0),
            Normal = rectSize,
            Color = color,
            BorderRatio = borderWidth,
            BorderColor = borderColor,
            CornerRadii = radii
        });
        _vertices.Add(new UIVertex
        {
            Position = new Vector2(x + w, y),
            UV = new Vector2(1, 0),
            Normal = rectSize,
            Color = color,
            BorderRatio = borderWidth,
            BorderColor = borderColor,
            CornerRadii = radii
        });
        _vertices.Add(new UIVertex
        {
            Position = new Vector2(x + w, y + h),
            UV = new Vector2(1, 1),
            Normal = rectSize,
            Color = color,
            BorderRatio = borderWidth,
            BorderColor = borderColor,
            CornerRadii = radii
        });
        _vertices.Add(new UIVertex
        {
            Position = new Vector2(x, y + h),
            UV = new Vector2(0, 1),
            Normal = rectSize,
            Color = color,
            BorderRatio = borderWidth,
            BorderColor = borderColor,
            CornerRadii = radii
        });

        AddQuadIndices(vertexOffset);

        using var _ = Graphics.PushState();
        Graphics.SetTexture(texture ?? Graphics.WhiteTexture, filter: texture?.Filter ?? TextureFilter.Point);
        Graphics.SetMesh(_mesh);
        Graphics.DrawElements(6, indexOffset, order: order);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void AddQuadIndices(int baseVertex)
    {
        _indices.Add((ushort)baseVertex);
        _indices.Add((ushort)(baseVertex + 1));
        _indices.Add((ushort)(baseVertex + 2));
        _indices.Add((ushort)(baseVertex + 2));
        _indices.Add((ushort)(baseVertex + 3));
        _indices.Add((ushort)baseVertex);
    }

    public static void Flush()
    {
        if (_indices.Length == 0) return;
        Graphics.Driver.BindMesh(_mesh);
        Graphics.Driver.UpdateMesh(_mesh, _vertices.AsByteSpan(), _indices.AsSpan());
        _vertices.Clear();
        _indices.Clear();
    }
}
