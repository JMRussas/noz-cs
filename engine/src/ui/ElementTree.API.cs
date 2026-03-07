//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ;

public static unsafe partial class ElementTree
{
    public static int BeginSize(Size width, Size height) => BeginSize(new Size2(width, height));

    public static int BeginSize(Size2 size)
    {
        ref var e = ref BeginElement<SizeElement>(ElementType.Size);
        ref var d = ref GetElementData<SizeElement>(ref e);
        d.Size = size;
        return GetOffset(ref e);
    }

    public static void EndSize() => EndElement(ElementType.Size);

    public static int BeginPadding(EdgeInsets padding)
    {
        ref var e = ref BeginElement<PaddingElement>(ElementType.Padding);
        ref var d = ref GetElementData<PaddingElement>(ref e);
        d.Padding = padding;
        return GetOffset(ref e);
    }

    public static void EndPadding() => EndElement(ElementType.Padding);

    public static int BeginFill(Color color, BorderRadius radius = default)
    {
        ref var e = ref BeginElement<FillElement>(ElementType.Fill, withTransform: true);
        ref var d = ref GetElementData<FillElement>(ref e);
        d.Color = color;
        d.Radius = radius;
        return GetOffset(ref e);
    }

    public static void EndFill() => EndElement(ElementType.Fill);

    public static int BeginBorder(float width, Color color, BorderRadius radius = default)
    {
        ref var e = ref BeginElement<BorderElement>(ElementType.Border, withTransform: true);
        ref var d = ref GetElementData<BorderElement>(ref e);
        d.Width = width;
        d.Color = color;
        d.Radius = radius;
        return GetOffset(ref e);
    }

    public static void EndBorder() => EndElement(ElementType.Border);

    public static int BeginMargin(EdgeInsets margin)
    {
        ref var e = ref BeginElement<MarginElement>(ElementType.Margin);
        ref var d = ref GetElementData<MarginElement>(ref e);
        d.Margin = margin;
        return GetOffset(ref e);
    }

    public static void EndMargin() => EndElement(ElementType.Margin);

    public static int BeginAlign(Align2 align)
    {
        ref var e = ref BeginElement<AlignElement>(ElementType.Align);
        ref var d = ref GetElementData<AlignElement>(ref e);
        d.Align = align;
        return GetOffset(ref e);
    }

    public static void EndAlign() => EndElement(ElementType.Align);

    public static int BeginClip(BorderRadius radius = default)
    {
        ref var e = ref BeginElement<ClipElement>(ElementType.Clip, withTransform: true);
        ref var d = ref GetElementData<ClipElement>(ref e);
        d.Radius = radius;
        return GetOffset(ref e);
    }

    public static void EndClip() => EndElement(ElementType.Clip);

    public static int BeginOpacity(float opacity)
    {
        ref var e = ref BeginElement<OpacityElement>(ElementType.Opacity);
        ref var d = ref GetElementData<OpacityElement>(ref e);
        d.Opacity = opacity;
        return GetOffset(ref e);
    }

    public static void EndOpacity() => EndElement(ElementType.Opacity);

    internal static int BeginCursor(Sprite sprite)
    {
        ref var e = ref BeginElement<CursorElement>(ElementType.Cursor, withTransform: true);
        ref var d = ref GetElementData<CursorElement>(ref e);
        d.IsSprite = true;
        d.AssetIndex = AddAsset(sprite);
        return GetOffset(ref e);
    }

    internal static int BeginCursor(SystemCursor cursor)
    {
        ref var e = ref BeginElement<CursorElement>(ElementType.Cursor, withTransform: true);
        ref var d = ref GetElementData<CursorElement>(ref e);
        d.IsSprite = false;
        d.SystemCursor = cursor;
        return GetOffset(ref e);
    }

    internal static void EndCursor() => EndElement(ElementType.Cursor);

    internal static int BeginTransform(Vector2 pivot, Vector2 translate, float rotate, Vector2 scale)
    {
        ref var e = ref BeginElement<TransformElement>(ElementType.Transform, withTransform: true);
        ref var d = ref GetElementData<TransformElement>(ref e);
        d.Pivot = pivot;
        d.Translate = translate;
        d.Rotate = rotate;
        d.Scale = scale;
        return GetOffset(ref e);
    }

    internal static void EndTransform() => EndElement(ElementType.Transform);

    internal static int BeginGrid(float spacing, int columns, float cellWidth, float cellHeight,
        float cellMinWidth, float cellHeightOffset, int virtualCount, int startIndex)
    {
        ref var e = ref BeginElement<GridElement>(ElementType.Grid);
        ref var d = ref GetElementData<GridElement>(ref e);
        d.Spacing = spacing;
        d.Columns = columns;
        d.CellWidth = cellWidth;
        d.CellHeight = cellHeight;
        d.CellMinWidth = cellMinWidth;
        d.CellHeightOffset = cellHeightOffset;
        d.VirtualCount = virtualCount;
        d.StartIndex = startIndex;
        return GetOffset(ref e);
    }

    internal static void EndGrid() => EndElement(ElementType.Grid);

    internal static int BeginScrollable(int widgetId, in ScrollableStyle style)
    {
        ref var e = ref BeginElement<ScrollableElement>(ElementType.Scrollable, withTransform: true);
        ref var d = ref GetElementData<ScrollableElement>(ref e);
        d.ScrollSpeed = style.ScrollSpeed;
        d.ScrollbarVisibility = style.Scrollbar;
        d.ScrollbarWidth = style.ScrollbarWidth;
        d.ScrollbarMinThumbHeight = style.ScrollbarMinThumbHeight;
        d.ScrollbarTrackColor = style.ScrollbarTrackColor;
        d.ScrollbarThumbColor = style.ScrollbarThumbColor;
        d.ScrollbarThumbHoverColor = style.ScrollbarThumbHoverColor;
        d.ScrollbarPadding = style.ScrollbarPadding;
        d.ScrollbarBorderRadius = style.ScrollbarBorderRadius;
        d.WidgetId = widgetId;
        return GetOffset(ref e);
    }

    internal static void EndScrollable() => EndElement(ElementType.Scrollable);

    public static int BeginRow(float spacing = 0)
    {
        ref var e = ref BeginElement<RowElement>(ElementType.Row);
        ref var d = ref GetElementData<RowElement>(ref e);
        d.Spacing = spacing;
        return GetOffset(ref e);
    }

    public static void EndRow() => EndElement(ElementType.Row);

    public static int BeginColumn(float spacing = 0)
    {
        ref var e = ref BeginElement<ColumnElement>(ElementType.Column);
        ref var d = ref GetElementData<ColumnElement>(ref e);
        d.Spacing = spacing;
        return GetOffset(ref e);
    }

    public static void EndColumn() => EndElement(ElementType.Column);

    public static int Flex(float flex = 1.0f)
    {
        ref var e = ref CreateLeafElement<FlexElement>(ElementType.Flex);
        ref var d = ref GetElementData<FlexElement>(ref e);
        d.Flex = flex;
        return GetOffset(ref e);
    }

    public static int BeginFlex(float flex = 1.0f)
    {
        ref var e = ref BeginElement<FlexElement>(ElementType.Flex);
        ref var d = ref GetElementData<FlexElement>(ref e);
        d.Flex = flex;
        return GetOffset(ref e);
    }

    public static void EndFlex() => EndElement(ElementType.Flex);

    internal static int BeginPopup(Rect anchorRect, Align2 anchor, Align2 popupAlign, float spacing, bool clampToScreen, bool autoClose = true, bool interactive = true)
    {
        ref var e = ref BeginElement<PopupElement>(ElementType.Popup, withTransform: true);
        ref var d = ref GetElementData<PopupElement>(ref e);
        d.AnchorRect = anchorRect;
        d.AnchorFactorX = anchor.X.ToFactor();
        d.AnchorFactorY = anchor.Y.ToFactor();
        d.PopupAlignFactorX = popupAlign.X.ToFactor();
        d.PopupAlignFactorY = popupAlign.Y.ToFactor();
        d.Spacing = spacing;
        d.ClampToScreen = clampToScreen;
        d.AutoClose = autoClose;
        d.Interactive = interactive;
        var offset = GetOffset(ref e);
        if (_popupCount < MaxPopups)
        {
            _popupOffsets[_popupCount++] = offset;
            if (interactive)
                _activePopupCount++;
        }
        return offset;
    }

    internal static void EndPopup() => EndElement(ElementType.Popup);

    public static int Spacer(float width, float height) => Spacer(new Vector2(width, height));

    public static int Spacer(Vector2 size)
    {
        ref var e = ref CreateLeafElement<SpacerElement>(ElementType.Spacer);
        ref var d = ref GetElementData<SpacerElement>(ref e);
        d.Size = size;
        return GetOffset(ref e);
    }

    public static int Label(UnsafeSpan<char> text, Font font, float fontSize, Color color,
        Align2 align = default, TextOverflow overflow = TextOverflow.Overflow)
    {
        ref var e = ref CreateLeafElement<LabelElement>(ElementType.Label, withTransform: true);
        ref var d = ref GetElementData<LabelElement>(ref e);
        d.Text = text;
        d.FontSize = fontSize;
        d.Color = color;
        d.Align = align;
        d.Overflow = overflow;
        d.AssetIndex = AddAsset(font);
        return GetOffset(ref e);
    }

    public static int Image(Sprite sprite, Size2 size = default, ImageStretch stretch = ImageStretch.Uniform,
        Color color = default, float scale = 1.0f, Align2 align = default)
    {
        ref var e = ref CreateLeafElement<ImageElement>(ElementType.Image, withTransform: true);
        ref var d = ref GetElementData<ImageElement>(ref e);
        d.Size = size;
        d.Stretch = stretch;
        d.Align = align;
        d.Scale = scale;
        d.Color = color.IsTransparent ? Color.White : color;
        d.Width = sprite.Bounds.Width;
        d.Height = sprite.Bounds.Height;
        d.AssetIndex = AddAsset(sprite);
        return GetOffset(ref e);
    }

    public static int Image(Texture texture, Size2 size = default, ImageStretch stretch = ImageStretch.Uniform,
        Color color = default, float scale = 1.0f, Align2 align = default)
    {
        ref var e = ref CreateLeafElement<ImageElement>(ElementType.Image, withTransform: true);
        ref var d = ref GetElementData<ImageElement>(ref e);
        d.Size = size;
        d.Stretch = stretch;
        d.Align = align;
        d.Scale = scale;
        d.Color = color.IsTransparent ? Color.White : color;
        d.Width = texture.Width;
        d.Height = texture.Height;
        d.AssetIndex = AddAsset(texture);
        return GetOffset(ref e);
    }

    public static int Scene(Camera camera, Action draw, Size2 size, Color clearColor, int sampleCount)
    {
        ref var e = ref CreateLeafElement<SceneElement>(ElementType.Scene, withTransform: true);
        ref var d = ref GetElementData<SceneElement>(ref e);
        d.Size = size;
        d.ClearColor = clearColor;
        d.SampleCount = sampleCount;
        d.AssetIndex = AddAsset((camera, draw));
        return GetOffset(ref e);
    }
}
