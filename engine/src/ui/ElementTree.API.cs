//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;
using System.Runtime.CompilerServices;

namespace NoZ;

public static unsafe partial class ElementTree
{
    public static int BeginSize(Size width, Size height) => BeginSize(new Size2(width, height));

    public static int BeginSize(Size2 size)
    {
        ref var e = ref BeginElement(ElementType.Size);
        e.Data.Size = size;
        return e.Index;
    }

    public static void EndSize() => EndElement(ElementType.Size);

    public static int BeginPadding(EdgeInsets padding)
    {
        ref var e = ref BeginElement(ElementType.Padding);
        e.Data.Padding = padding;
        return e.Index;
    }

    public static void EndPadding() => EndElement(ElementType.Padding);

    public static int BeginFill(Color color, BorderRadius radius = default, float borderWidth = 0, Color borderColor = default)
    {
        ref var e = ref BeginElement(ElementType.Fill);
        e.Data.Fill.Color = color;
        e.Data.Fill.Radius = radius;
        e.Data.Fill.BorderWidth = borderWidth;
        e.Data.Fill.BorderColor = borderColor;
        return e.Index;
    }

    public static int BeginFill(Color color, Color color2, float gradientAngle, BorderRadius radius = default, float borderWidth = 0, Color borderColor = default)
    {
        ref var e = ref BeginElement(ElementType.Fill);
        e.Data.Fill.Color = color;
        e.Data.Fill.Color2 = color2;
        e.Data.Fill.GradientAngle = gradientAngle;
        e.Data.Fill.Radius = radius;
        e.Data.Fill.BorderWidth = borderWidth;
        e.Data.Fill.BorderColor = borderColor;
        return e.Index;
    }

    public static void EndFill() => EndElement(ElementType.Fill);

    public static int BeginMargin(EdgeInsets margin)
    {
        ref var e = ref BeginElement(ElementType.Margin);
        e.Data.Margin = margin;
        return e.Index;
    }

    public static void EndMargin() => EndElement(ElementType.Margin);

    public static int BeginAlign(Align2 align)
    {
        ref var e = ref BeginElement(ElementType.Align);
        e.Data.Align = align;
        return e.Index;
    }

    public static void EndAlign() => EndElement(ElementType.Align);

    public static int BeginClip(BorderRadius radius = default)
    {
        ref var e = ref BeginElement(ElementType.Clip);
        ref var d = ref e.Data.Clip;
        d.Radius = radius;
        return e.Index;
    }

    public static void EndClip() => EndElement(ElementType.Clip);

    public static int BeginOpacity(float opacity)
    {
        ref var e = ref BeginElement(ElementType.Opacity);
        e.Data.Opacity = opacity;
        return e.Index;
    }

    public static void EndOpacity() => EndElement(ElementType.Opacity);

    public static int BeginCursor(Sprite sprite)
    {
        ref var e = ref BeginElement(ElementType.Cursor);
        ref var d = ref e.Data.Cursor;
        d.IsSprite = true;
        d.AssetIndex = AddObject(sprite);
        return e.Index;
    }

    public static int BeginCursor(SystemCursor cursor)
    {
        ref var e = ref BeginElement(ElementType.Cursor);
        ref var d = ref e.Data.Cursor;
        d.IsSprite = false;
        d.SystemCursor = cursor;
        return e.Index;
    }

    public static void EndCursor() => EndElement(ElementType.Cursor);

    public static int BeginTransform(Vector2 pivot, Vector2 translate, float rotate, Vector2 scale)
    {
        ref var e = ref BeginElement(ElementType.Transform);
        ref var d = ref e.Data.Transform;
        d.Pivot = pivot;
        d.Translate = translate;
        d.Rotate = rotate;
        d.Scale = scale;
        return e.Index;
    }

    public static void EndTransform() => EndElement(ElementType.Transform);

    public static int BeginGrid(
        float spacing,
        int columns,
        float cellWidth,
        float cellHeight,
        float cellMinWidth,
        float cellHeightOffset,
        int virtualCount,
        int startIndex)
    {
        ref var e = ref BeginElement(ElementType.Grid);
        ref var d = ref e.Data.Grid;
        d.Spacing = spacing;
        d.Columns = columns;
        d.CellWidth = cellWidth;
        d.CellHeight = cellHeight;
        d.CellMinWidth = cellMinWidth;
        d.CellHeightOffset = cellHeightOffset;
        d.VirtualCount = virtualCount;
        d.StartIndex = startIndex;
        return e.Index;
    }

    public static void EndGrid() => EndElement(ElementType.Grid);

    public static int BeginScrollable(ref ScrollState state, in ScrollableStyle style)
    {
        ref var e = ref BeginElement(ElementType.Scroll);
        ref var d = ref e.Data.Scroll;
        d.ScrollSpeed = style.ScrollSpeed;
        d.State = (ScrollState*)Unsafe.AsPointer(ref state);
        return e.Index;
    }

    public static void EndScrollable() => EndElement(ElementType.Scroll);

    public static void BeginTrack(ref TrackState state, WidgetId id, float thumbSizeX, float thumbSizeY = 0)
    {
        ref var e = ref BeginElement(ElementType.Track);
        ref var d = ref e.Data.Track;
        d.Id = id;
        d.ThumbSizeX = thumbSizeX;
        d.ThumbSizeY = thumbSizeY;
        d.State = (TrackState*)Unsafe.AsPointer(ref state);
    }

    public static void EndTrack() => EndElement(ElementType.Track);

    public static int BeginRow(float spacing = 0)
    {
        ref var e = ref BeginElement(ElementType.Row);
        e.Data.Spacing = spacing;
        return e.Index;
    }

    public static void EndRow() => EndElement(ElementType.Row);

    public static int BeginColumn(float spacing = 0)
    {
        ref var e = ref BeginElement(ElementType.Column);
        e.Data.Spacing = spacing;
        return e.Index;
    }

    public static void EndColumn() => EndElement(ElementType.Column);

    public static int Flex(float flex = 1.0f)
    {
        ref var e = ref BeginElement(ElementType.Flex);
        e.Data.Flex = flex;
        EndElement(ElementType.Flex);
        return e.Index;
    }

    public static int BeginFlex(float flex = 1.0f)
    {
        ref var e = ref BeginElement(ElementType.Flex);
        e.Data.Flex = flex;
        return e.Index;
    }

    public static void EndFlex() => EndElement(ElementType.Flex);

    public static int BeginPopup(
        Rect anchorRect,
        Align2 anchor,
        Align2 popupAlign,
        float spacing = 0.0f,
        bool clampToScreen = true,
        bool autoClose = true,
        bool interactive = true)
    {
        ref var e = ref BeginElement(ElementType.Popup);
        ref var d = ref e.Data.Popup;
        d.AnchorRect = anchorRect;
        d.AnchorFactorX = anchor.X.ToFactor();
        d.AnchorFactorY = anchor.Y.ToFactor();
        d.PopupAlignFactorX = popupAlign.X.ToFactor();
        d.PopupAlignFactorY = popupAlign.Y.ToFactor();
        d.Spacing = spacing;
        d.ClampToScreen = clampToScreen;
        d.AutoClose = autoClose;
        d.Interactive = interactive;

        if (_popupCount < MaxPopups)
        {
            _popups[_popupCount++] = e.Index;
            if (interactive)
                _activePopupCount++;
        }
        
        return e.Index;
    }

    public static void EndPopup() => EndElement(ElementType.Popup);

    public static int Spacer(float size)
    {
        ref var e = ref BeginElement(ElementType.Spacer);
        e.Data.Spacing = size;
        EndElement(ElementType.Spacer);
        return e.Index;
    }

    public static int Text(
        ReadOnlySpan<char> value,
        Font font,
        float fontSize,
        Color color,
        Align2 align = default,
        TextOverflow overflow = TextOverflow.Overflow)
    {
        ref var e = ref BeginElement(ElementType.Text);
        ref var d = ref e.Data.Text;
        d.Text = AllocString(value);
        d.FontSize = fontSize;
        d.Color = color;
        d.Align = align;
        d.Overflow = overflow;
        d.Font = AddObject(font);

        EndElement(ElementType.Text);

        return e.Index;
    }

    public static int Image(
        Sprite sprite,
        Size2 size = default,
        ImageStretch stretch = ImageStretch.Uniform,
        Color color = default,
        float scale = 1.0f,
        Align2 align = default)
    {
        ref var e = ref BeginElement(ElementType.Image);
        ref var d = ref e.Data.Image;
        d.Size = size;
        d.Stretch = stretch;
        d.Align = align;
        d.Scale = scale;
        d.Color = color.IsTransparent ? Color.White : color;
        d.Width = sprite.Bounds.Width;
        d.Height = sprite.Bounds.Height;
        d.Asset = AddObject(sprite);
        EndElement(ElementType.Image);
        return e.Index;
    }

    public static int Image(
        Texture texture,
        Size2 size = default,
        ImageStretch stretch = ImageStretch.Uniform,
        Color color = default,
        float scale = 1.0f,
        Align2 align = default)
    {
        ref var e = ref BeginElement(ElementType.Image);
        ref var d = ref e.Data.Image;
        d.Size = size;
        d.Stretch = stretch;
        d.Align = align;
        d.Scale = scale;
        d.Color = color.IsTransparent ? Color.White : color;
        d.Width = texture.Width;
        d.Height = texture.Height;
        d.Asset = AddObject(texture);
        EndElement(ElementType.Image);
        return e.Index;
    }

    public static int Scene(Camera camera, Action draw, Size2 size, Color clearColor, int sampleCount)
    {
        ref var e = ref BeginElement(ElementType.Scene);
        ref var d = ref e.Data.Scene;
        d.Size = size;
        d.ClearColor = clearColor;
        d.SampleCount = sampleCount;
        d.Camera = AddObject(camera);
        d.DrawCallback = AddObject(draw);
        EndElement(ElementType.Scene);
        return e.Index;
    }
}
