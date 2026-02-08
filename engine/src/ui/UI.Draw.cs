//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

// #define NOZ_UI_DEBUG

using System.Diagnostics;
using System.Numerics;

namespace NoZ;

public static partial class UI
{
    // Draw pass
    private static void DrawElement(int elementIndex, bool isPopup)
    {
        ref var e = ref _elements[elementIndex];

        LogUI(e, $"{e.Type}:{Log.Params([
            ("Index", e.Index, true),
            ("Rect", e.Rect, true)
        ])}");

        var setScissor = false;

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

            case ElementType.Scene:
                DrawScene(ref e);
                return;

            case ElementType.Popup when !isPopup:
                return;

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
        UIRender.DrawRect(
            new Rect(trackX, trackY, s.ScrollbarWidth, trackH),
            s.ScrollbarTrackColor,
            s.ScrollbarBorderRadius
        );

        // Draw thumb if there's content to scroll
        if (maxScroll > 0 && s.ContentHeight > 0)
        {
            var thumbHeightRatio = viewportHeight / s.ContentHeight;
            var thumbH = Math.Max(s.ScrollbarMinThumbHeight, trackH * thumbHeightRatio);
            var scrollRatio = s.Offset / maxScroll;
            var thumbY = trackY + scrollRatio * (trackH - thumbH);

            UIRender.DrawRect(
                new Rect(trackX, thumbY, s.ScrollbarWidth, thumbH),
                s.ScrollbarThumbColor,
                s.ScrollbarBorderRadius
            );
        }
    }

    private static void DrawElements()
    {
        LogUI("Draw", condition: () => _elementCount > 0);

        if (_elementCount == 0) return;

        Graphics.SetBlendMode(BlendMode.Alpha);
        Graphics.SetLayer(Config.UILayer);
        Graphics.SetSortGroup(0);

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
        UIRender.DrawRect(
            new Rect(topLeft.X, topLeft.Y, bottomRight.X - topLeft.X, bottomRight.Y - topLeft.Y),
            style.Color,
            style.Border.Radius,
            style.Border.Width,
            style.Border.Color,
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
            Graphics.SetColor(color);
            Graphics.SetTransform(transform);
            TextRender.Draw(text, font, fontSize);
        }
    }

    // :label
    private static void DrawLabel(ref Element e)
    {
        var font = (e.Asset as Font) ?? _defaultFont!;
        var text = e.Data.Label.Text.AsReadOnlySpan();
        var textOffset = GetTextOffset(text, font, e.Data.Label.FontSize, e.Rect.Size, e.Data.Label.AlignX, e.Data.Label.AlignY);
        var transform = Matrix3x2.CreateTranslation(e.Rect.Position + textOffset) * e.LocalToWorld;

        using (Graphics.PushState())
        {
            Graphics.SetColor(e.Data.Label.Color);
            Graphics.SetTransform(transform);
            TextRender.Draw(text, font, e.Data.Label.FontSize, order: e.Data.Label.Order);
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
            Graphics.SetColor(img.Color);
            Graphics.SetTextureFilter(sprite.TextureFilter);
            Graphics.DrawFlat(sprite, order: img.Order, bone: -1);
        }
        else if (e.Asset is Texture texture)
        {
            var topLeft = Vector2.Transform(offset, e.LocalToWorld);
            var bottomRight = Vector2.Transform(offset + scaledSize, e.LocalToWorld);
            UIRender.DrawImage(
                new Rect(topLeft.X, topLeft.Y, bottomRight.X - topLeft.X, bottomRight.Y - topLeft.Y),
                texture,
                img.Color,
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
        //camera.Viewport = new Rect(screenTopLeft.X, screenTopLeft.Y, screenBottomRight.X - screenTopLeft.X, screenBottomRight.Y - screenTopLeft.Y);
        draw();
        Graphics.EndPass();
        Graphics.SetCamera(Camera);

        // Draw the RT to screen
        using (Graphics.PushState())
        {
            Graphics.SetTextureFilter(TextureFilter.Point);
            Graphics.Draw(rt, topLeft, bottomRight);
        }
    }
}
