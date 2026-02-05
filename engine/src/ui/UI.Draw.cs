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
            case ElementType.Canvas:
                DrawCanvas(ref e);
                break;

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

        // First pass: draw everything except popups
        for (int elementIndex = 0; elementIndex < _elementCount;)
        {
            ref var canvas = ref _elements[elementIndex];
            Debug.Assert(canvas.Type == ElementType.Canvas, "Expected canvas element");
            DrawElement(elementIndex, false);
            elementIndex = canvas.NextSiblingIndex;
        }

        // Second pass: draw popups (outside any scissor regions)
        for (var i = 0; i < _popupCount; i++)
        {
            var popupIndex = _popups[i];
            DrawElement(popupIndex, true);
        }
    }

    private static void DrawCanvas(ref Element e)
    {
        ref var style = ref e.Data.Canvas;
        if (style.Color.IsTransparent)
            return;

        var topLeft = Vector2.Transform(e.Rect.Position, e.LocalToWorld);
        var bottomRight = Vector2.Transform(e.Rect.Position + e.Rect.Size, e.LocalToWorld);
        UIRender.DrawRect(new Rect(topLeft.X, topLeft.Y, bottomRight.X - topLeft.X, bottomRight.Y - topLeft.Y), style.Color);
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
            style.Border.Color
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
        var font = e.Font ?? _defaultFont!;
        var text = e.Data.Label.Text.AsReadOnlySpan();
        var textOffset = GetTextOffset(text, font, e.Data.Label.FontSize, e.Rect.Size, e.Data.Label.AlignX, e.Data.Label.AlignY);
        var transform = Matrix3x2.CreateTranslation(e.Rect.Position + textOffset) * e.LocalToWorld;

        using (Graphics.PushState())
        {
            Graphics.SetColor(e.Data.Label.Color);
            Graphics.SetTransform(transform);
            TextRender.Draw(text, font, e.Data.Label.FontSize);
        }
    }

    private static void DrawImage(ref Element e)
    {
        ref var img = ref e.Data.Image;
        if (e.Sprite == null) return;

        var sprite = e.Sprite;
        var spriteSize = sprite.Size;
        var imgSize = e.Rect.Size;
        var scale = new Vector2(
            imgSize.X / spriteSize.X,
            imgSize.Y / spriteSize.Y);

        switch (img.Stretch)
        {
            case ImageStretch.None:
                scale = Vector2.One;
                break;
            case ImageStretch.Uniform:
                var uniformScale = MathF.Min(scale.X, scale.Y);
                scale = new Vector2(uniformScale, uniformScale);
                break;
            case ImageStretch.Fill:
                // scaleX and scaleY are already set correctly
                break;
        }

        var scaledSize = scale * spriteSize;

        var offset = new Vector2(
            e.Rect.X + (imgSize.X - scaledSize.X) * img.AlignX.ToFactor() - sprite.Bounds.X * scale.X,
            e.Rect.Y + (imgSize.Y - scaledSize.Y) * img.AlignY.ToFactor() - sprite.Bounds.Y * scale.Y
        );

        LogUI(e, "      ", values: [
            ("Scale", scale, true),
            ("Offset", offset, offset != Vector2.Zero),
            ("Color", img.Color, img.Color != Color.White),
            ]);

        using (Graphics.PushState())
        {
            var transform = Matrix3x2.CreateScale(scale * sprite.PixelsPerUnit) * Matrix3x2.CreateTranslation(offset) * e.LocalToWorld;
            Graphics.SetTransform(transform);
            Graphics.SetColor(img.Color);
            Graphics.DrawFlat(e.Sprite, order: 0, bone: -1);
        }
    }

    private static void DrawScene(ref Element e)
    {
        ref var scene = ref e.Data.Scene;
        if (scene.CallbackIndex < 0)
            return;

        ref readonly var callback = ref _sceneCallbacks[scene.CallbackIndex];
        if (callback.Camera == null || callback.Draw == null)
            return;

        var topLeft = Vector2.Transform(e.Rect.Position, e.LocalToWorld);
        var bottomRight = Vector2.Transform(e.Rect.Position + e.Rect.Size, e.LocalToWorld);
        var screenTopLeft = Camera!.WorldToScreen(topLeft);
        var screenBottomRight = Camera!.WorldToScreen(bottomRight);

        var viewportX = (int)screenTopLeft.X;
        var viewportY = (int)screenTopLeft.Y;
        var viewportW = (int)(screenBottomRight.X - screenTopLeft.X);
        var viewportH = (int)(screenBottomRight.Y - screenTopLeft.Y);

        using var _ = Graphics.PushState();
        Graphics.SetViewport(viewportX, viewportY, viewportW, viewportH);
        Graphics.ClearScissor();
        callback.Camera.Viewport = new Rect(viewportX, viewportY, viewportW, viewportH);
        Graphics.SetCamera(callback.Camera);

        callback.Draw();
    }
}
