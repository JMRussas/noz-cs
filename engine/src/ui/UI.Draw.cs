//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

#define NOZ_UI_DEBUG

using System.Numerics;

namespace NoZ.Engine.UI;

public static partial class UI
{

    // Draw pass
    private static int DrawElement(int elementIndex, bool isPopup)
    {
        ref var e = ref _elements[elementIndex++];

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
                TextBoxElement.Draw(ref e);
                break;

            case ElementType.Popup when !isPopup:
                return e.NextSiblingIndex;
        }

        var useScissor = e.Type == ElementType.Scrollable;
        if (useScissor)
        {
            var pos = Vector2.Transform(Vector2.Zero, e.LocalToWorld);
            var screenPos = Camera!.WorldToScreen(pos);
            var scale = Application.WindowSize.Y / _size.Y;
            var screenHeight = Application.WindowSize.Y;

            // OpenGL scissor Y is from bottom, need to flip
            var scissorX = (int)screenPos.X;
            var scissorY = (int)(screenHeight - screenPos.Y - e.Rect.Height * scale);
            var scissorW = (int)(e.Rect.Width * scale);
            var scissorH = (int)(e.Rect.Height * scale);

            Render.SetScissor(scissorX, scissorY, scissorW, scissorH);
        }

        for (var i = 0; i < e.ChildCount; i++)
            elementIndex = DrawElement(elementIndex, false);

        if (useScissor)
        {
            Render.DisableScissor();
        }

        return elementIndex;
    }

    private static void DrawCanvas(ref Element e)
    {
        ref var style = ref e.Data.Canvas;
        if (style.Color.IsTransparent)
            return;

        var pos = Vector2.Transform(Vector2.Zero, e.LocalToWorld);
        UIRender.DrawRect(pos.X, pos.Y, e.Rect.Width, e.Rect.Height, style.Color);
    }

    private static void DrawContainer(ref Element e)
    {
        ref var style = ref e.Data.Container;
        if (style.Color.IsTransparent && style.Border.Width <= 0)
            return;

        var pos = Vector2.Transform(Vector2.Zero, e.LocalToWorld);
        UIRender.DrawRect(
            pos.X, pos.Y, e.Rect.Width, e.Rect.Height,
            style.Color,
            style.Border.Radius,
            style.Border.Width,
            style.Border.Color
        );

        LogUI(e, $"{e.Type}: Rect={new Rect(pos.X, pos.Y, e.Rect.Width, e.Rect.Height)} Color={e.Data.Container.Color}");
    }

    private static Vector2 GetTextOffset(string text, Font font, float fontSize, in Vector2 containerSize, Align alignX, Align alignY)
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

        Render.PushState();
        Render.SetColor(color);
        Render.SetTransform(transform);
        TextRender.Draw(text, font, fontSize);
        Render.PopState();
    }

    private static void DrawLabel(ref Element e)
    {
        var font = e.Font ?? _defaultFont!;
        var text = new string(GetText(e.Data.Label.TextStart, e.Data.Label.TextLength));
        var offset = GetTextOffset(text, font, e.Data.Label.FontSize, e.Rect.Size, e.Data.Label.AlignX, e.Data.Label.AlignY);
        var transform = e.LocalToWorld * Matrix3x2.CreateTranslation(offset);

        LogUI(e, $"{e.Type}: Offset={offset} AlignX={e.Data.Label.AlignX}  AlignY={e.Data.Label.AlignY}");

        Render.PushState();
        Render.SetColor(e.Data.Label.Color);
        Render.SetTransform(transform);
        TextRender.Draw(text, font, e.Data.Label.FontSize);
        Render.PopState();
    }

    private static void DrawImage(ref Element e)
    {
        ref var img = ref e.Data.Image;
        if (img.Texture == nuint.Zero) return;

        var pos = Vector2.Transform(Vector2.Zero, e.LocalToWorld);
        Render.SetColor(img.Color);
        Render.SetTexture(img.Texture);
        Render.Draw(
            pos.X, pos.Y, e.Rect.Width, e.Rect.Height,
            img.UV0.X, img.UV0.Y, img.UV1.X, img.UV1.Y
        );
    }
}
