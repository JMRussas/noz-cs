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

            case ElementType.Popup when !isPopup:
                return;

            case ElementType.Scrollable:
            {
                var pos = Vector2.Transform(Vector2.Zero, e.LocalToWorld);
                var screenPos = Camera!.WorldToScreen(pos);
                var scale = Application.WindowSize.Y / _size.Y;
                var screenHeight = Application.WindowSize.Y;
                var scissorX = (int)screenPos.X;
                var scissorY = (int)(screenHeight - screenPos.Y - e.Rect.Height * scale);
                var scissorW = (int)(e.Rect.Width * scale);
                var scissorH = (int)(e.Rect.Height * scale);
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
            Graphics.DisableScissor();
    }

    private static void DrawElements() 
    {
        LogUI("Draw", condition: () => _elementCount > 0);

        for (int elementIndex = 0; elementIndex < _elementCount;)
        {
            ref var canvas = ref _elements[elementIndex];
            Debug.Assert(canvas.Type == ElementType.Canvas, "Expected canvas element");
            DrawElement(elementIndex, true);
            elementIndex = canvas.NextSiblingIndex;
        }            
    }

    private static void DrawCanvas(ref Element e)
    {
        ref var style = ref e.Data.Canvas;
        if (style.Color.IsTransparent)
            return;

        var pos = Vector2.Transform(Vector2.Zero, e.LocalToWorld);
        UIRender.DrawRect(new Rect(pos.X, pos.Y, e.Rect.Width, e.Rect.Height), style.Color);
    }

    private static void DrawContainer(ref Element e)
    {
        ref var style = ref e.Data.Container;
        if (style.Color.IsTransparent && style.Border.Width <= 0)
            return;

        var pos = Vector2.Transform(Vector2.Zero, e.LocalToWorld);
        UIRender.DrawRect(
            new Rect(pos.X, pos.Y, e.Rect.Width, e.Rect.Height),
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
        var offset = GetTextOffset(text, font, e.Data.Label.FontSize, e.Rect.Size, e.Data.Label.AlignX, e.Data.Label.AlignY);
        var transform = e.LocalToWorld * Matrix3x2.CreateTranslation(offset);

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
            (imgSize.X - scaledSize.X) * img.AlignX.ToFactor() - sprite.Bounds.X * scale.X,
            (imgSize.Y - scaledSize.Y) * img.AlignY.ToFactor() - sprite.Bounds.Y * scale.Y
        );

        LogUI(e, "      ", values: [
            ("Scale", scale, true),
            ("Offset", offset, offset != Vector2.Zero),
            ("Color", img.Color, img.Color != Color.White),
            ]);

        using (Graphics.PushState())
        {
            Graphics.SetTransform(Matrix3x2.CreateScale(scale * sprite.PixelsPerUnit) * Matrix3x2.CreateTranslation(offset) * e.LocalToWorld);
            Graphics.SetColor(img.Color);
            Graphics.Draw(e.Sprite);
        }
    }
}
