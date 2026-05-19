//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;
using System.Runtime.CompilerServices;

namespace NoZ;

public static unsafe partial class ElementTree
{
    public static int BeginSize(Size width, Size height, float minWidth, float maxWidth, float minHeight, float maxHeight)
        => BeginSize(new Size2(width, height), minWidth, maxWidth, minHeight, maxHeight);

    public static int BeginSize(Size width, Size height)
        => BeginSize(new Size2(width, height), 0, float.MaxValue, 0, float.MaxValue);

    public static int BeginSize(Size2 size, float minWidth = 0, float maxWidth = float.MaxValue, float minHeight = 0, float maxHeight = float.MaxValue)
    {
        ref var e = ref BeginElement(ElementType.Size);
        e.Data.Size = new SizeElement
        {
            Size = size,
            MinWidth = minWidth,
            MaxWidth = maxWidth,
            MinHeight = minHeight,
            MaxHeight = maxHeight,
        };
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

    public static void Fill(in BackgroundStyle background, BorderRadius radius = default, float borderWidth = 0, Color borderColor = default, ushort order = 0)
    {
        BeginFill(background, radius, borderWidth, borderColor, order);
        EndFill();
    }

    public static void Fill(Color color, BorderRadius radius = default, float borderWidth = 0, Color borderColor = default, ushort order = 0)
    {
        BeginFill(color, radius, borderWidth, borderColor, order);
        EndFill();
    }

    public static int BeginFill(in BackgroundStyle background, BorderRadius radius = default, float borderWidth = 0, Color borderColor = default, ushort order = 0)
    {
        ref var e = ref BeginElement(ElementType.Fill);
        e.Data.Fill.Color = background.Color;
        e.Data.Fill.GradientColor = background.GradientColor;
        e.Data.Fill.GradientAngle = background.GradientAngle;
        e.Data.Fill.HasGradient = background.HasGradient;
        e.Data.Fill.HasImage = background.HasImage;
        e.Data.Fill.ImageColor = background.ImageColor;
        e.Data.Fill.ImageStretch = background.ImageStretch;
        e.Data.Fill.ImageAsset = background.HasImage ? AddObject(background.Image!) : (ushort)0;
        e.Data.Fill.Radius = radius;
        e.Data.Fill.BorderWidth = borderWidth;
        e.Data.Fill.BorderColor = borderColor;
        e.Data.Fill.Order = order;
        return e.Index;
    }

    public static int BeginFill(Color color, BorderRadius radius = default, float borderWidth = 0, Color borderColor = default, ushort order = 0)
    {
        ref var e = ref BeginElement(ElementType.Fill);
        e.Data.Fill.Color = color;
        e.Data.Fill.HasImage = false;
        e.Data.Fill.HasGradient = false;
        e.Data.Fill.Radius = radius;
        e.Data.Fill.BorderWidth = borderWidth;
        e.Data.Fill.BorderColor = borderColor;
        e.Data.Fill.Order = order;
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

    public static int BeginAlign(Align alignX, Align alignY) => BeginAlign(new Align2(alignX, alignY));

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

    public static int BeginCursor(SpriteCursor cursor)
    {
        ref var e = ref BeginElement(ElementType.Cursor);
        ref var d = ref e.Data.Cursor;
        d.IsSprite = true;
        d.AssetIndex = AddObject(cursor.Sprite);
        d.Rotation = cursor.Rotation;
        d.HotspotX = cursor.Hotspot.X;
        d.HotspotY = cursor.Hotspot.Y;
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

    public static int BeginTransform(Vector2? pivot = null, Vector2? translate = null, float rotate = 0.0f, Vector2? scale = null)
    {        
        ref var e = ref BeginElement(ElementType.Transform);
        ref var d = ref e.Data.Transform;
        d.Pivot = pivot ?? new Vector2(0.5f, 0.5f);
        d.Translate = translate ?? Vector2.Zero;
        d.Rotate = rotate;
        d.Scale = scale ?? Vector2.One;
        return e.Index;
    }

    public static void EndTransform() => EndElement(ElementType.Transform);

    public static int BeginCollection(float spacing, int columns, float itemWidth, float itemHeight,
        int virtualCount, int startIndex)
    {
        ref var e = ref BeginElement(ElementType.Collection);
        ref var d = ref e.Data.Collection;
        d.Spacing = spacing;
        d.Columns = columns;
        d.ItemWidth = itemWidth;
        d.ItemHeight = itemHeight;
        d.VirtualCount = virtualCount;
        d.StartIndex = startIndex;
        return e.Index;
    }

    public static void EndCollection() => EndElement(ElementType.Collection);

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
        d.ThumbSize.X = thumbSizeX;
        d.ThumbSize.Y = thumbSizeY;
        d.State = (TrackState*)Unsafe.AsPointer(ref state);
    }

    public static void EndTrack() => EndElement(ElementType.Track);

    public static void BeginFlexSplitter(ref FlexSplitterState state, WidgetId id, float barSize,
        float minSize = 0, float maxSize = float.MaxValue, float minSize2 = 0, float maxSize2 = float.MaxValue)
    {
        ref var e = ref BeginElement(ElementType.FlexSplitter);
        ref var d = ref e.Data.FlexSplitter;
        d.Id = id;
        d.BarSize = barSize;
        d.MinSize = minSize;
        d.MaxSize = maxSize;
        d.MinSize2 = minSize2;
        d.MaxSize2 = maxSize2;
        d.State = (FlexSplitterState*)Unsafe.AsPointer(ref state);
    }

    public static void EndFlexSplitter() => EndElement(ElementType.FlexSplitter);

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
        TextOverflow overflow = TextOverflow.Overflow,
        Color outlineColor = default,
        float outlineWidth = 0,
        float outlineSoftness = 0)
    {
        ref var e = ref BeginElement(ElementType.Text);
        ref var d = ref e.Data.Text;
        d.Text = AllocString(value);
        d.FontSize = fontSize;
        d.Color = color;
        d.Align = align;
        d.Overflow = overflow;
        d.Font = AddObject(font);
        d.OutlineColor = outlineColor;
        d.OutlineWidth = outlineWidth;
        d.OutlineSoftness = outlineSoftness;

        EndElement(ElementType.Text);

        return e.Index;
    }

    public static int Image(
        IImage image,
        Size2 size = default,
        ImageStretch stretch = ImageStretch.Uniform,
        Color color = default,
        float scale = 1.0f,
        Align2 align = default,
        Color overlayColor = default)
    {
        ref var e = ref BeginElement(ElementType.Image);
        ref var d = ref e.Data.Image;
        d.Size = size;
        d.Stretch = stretch;
        d.Align = align;
        d.Scale = scale;
        d.Color = color.IsTransparent ? Color.White : color;
        d.OverlayColor = overlayColor;
        d.Width = image.ImageWidth;
        d.Height = image.ImageHeight;
        d.Asset = AddObject(image);
        d.UV = new Rect(0, 0, 1, 1);
        EndElement(ElementType.Image);
        return e.Index;
    }

    public static int Image(
        ITexture image,
        Rect sourceRect,
        Size2 size = default,
        ImageStretch stretch = ImageStretch.Uniform,
        Color color = default,
        float scale = 1.0f,
        Align2 align = default,
        Color overlayColor = default)
    {
        ref var e = ref BeginElement(ElementType.Image);
        ref var d = ref e.Data.Image;
        d.Size = size;
        d.Stretch = stretch;
        d.Align = align;
        d.Scale = scale;
        d.Color = color.IsTransparent ? Color.White : color;
        d.OverlayColor = overlayColor;
        d.Width = image.ImageWidth;
        d.Height = image.ImageHeight;
        d.UV = new Rect(
            sourceRect.X / image.ImageWidth,
            sourceRect.Y / image.ImageHeight,
            sourceRect.Width / image.ImageWidth,
            sourceRect.Height / image.ImageHeight);
        d.Asset = AddObject(image);
        EndElement(ElementType.Image);
        return e.Index;
    }

    public static int Scene(Camera camera, Action draw, Size2 size, Color clearColor, int sampleCount, bool pixelPerfect)
    {
        ref var e = ref BeginElement(ElementType.Scene);
        ref var d = ref e.Data.Scene;
        d.Size = size;
        d.ClearColor = clearColor;
        d.SampleCount = sampleCount;
        d.PixelPerfect = pixelPerfect;
        d.Camera = AddObject(camera);
        d.DrawCallback = AddObject(draw);
        EndElement(ElementType.Scene);
        return e.Index;
    }
}
