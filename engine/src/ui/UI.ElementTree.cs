//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace NoZ;

internal enum NewElementType : byte
{
    Widget,
    Size,
    Padding,
    Fill,
    Border,
    Margin,
    Row,
    Column,
    Flex,
    Align,
    Clip,
    Spacer,
    Opacity,
    Label,
    Image,
}

internal struct BaseElement
{
    public NewElementType Type;
    public ushort Parent;
    public ushort NextSibling;
    public ushort FirstChild;
    public ushort ChildCount;
    public Rect Rect;
    public Matrix3x2 LocalToWorld;
    public Matrix3x2 WorldToLocal;
}

internal struct SizeElement
{
    public Size2 Size;
}

internal struct PaddingElement
{
    public EdgeInsets Padding;
}

internal struct WidgetElement
{
    public int Id;
    public ushort Data;
    public ElementFlags Flags;
}

internal struct FillElement
{
    public Color Color;
}

internal struct BorderElement
{
    public float Width;
    public Color Color;
    public BorderRadius Radius;
}

internal struct MarginElement
{
    public EdgeInsets Margin;
}

internal struct RowElement
{
    public float Spacing;
}

internal struct ColumnElement
{
    public float Spacing;
}

internal struct FlexElement
{
    public float Flex;
}

internal struct AlignElement
{
    public Align2 Align;
}

internal struct ClipElement
{
    public BorderRadius Radius;
}

internal struct SpacerElement
{
    public Vector2 Size;
}

internal struct OpacityElement
{
    public float Opacity;
}

internal struct LabelElement
{
    public UnsafeSpan<char> Text;
    public float FontSize;
    public Color Color;
    public Align2 Align;
    public TextOverflow Overflow;
    public ushort AssetIndex;
}

internal struct ImageElement
{
    public Size2 Size;
    public ImageStretch Stretch;
    public Align2 Align;
    public float Scale;
    public Color Color;
    public float Width;
    public float Height;
    public ushort AssetIndex;
}

public static partial class UI
{
    public static unsafe class ElementTree
    {
        private const int MaxStateSize = 65535;
        private const int MaxElementSize = 65535;
        private const int MaxElementDepth = 64;
        private const int MaxId = 32000;
        private const int MaxAssets = 1024;

        private static NativeArray<byte> _elements;
        private static NativeArray<byte> _state;
        private static NativeArray<ushort> _elementStack;
        private static NativeArray<ushort> _widgets;
        private static int _elementStackCount;
        private static ushort _frame;
        private static ushort _nextSibling;
        private static int _stateOffset;
        private static ushort _currentWidget;

        private static readonly object?[] _assets = new object?[MaxAssets];
        private static int _assetCount;

        public static void Init()
        {
            _elements = new NativeArray<byte>(MaxElementSize);
            _state = new NativeArray<byte>(MaxStateSize * 2, MaxStateSize * 2);
            _elementStack = new NativeArray<ushort>(MaxElementDepth);
            _widgets = new NativeArray<ushort>(MaxId, MaxId);
        }

        internal static void Begin()
        {
            _frame++;
            _stateOffset = (_frame & 1) * MaxStateSize;
            _assetCount = 0;
        }

        internal static void End()
        {
            if (_elements.Length == 0) return;
            LayoutAxis(0, 0, ScreenSize.X, 0, -1);  // Width pass
            LayoutAxis(0, 0, ScreenSize.Y, 1, -1);  // Height pass
            UpdateTransforms(0, Matrix3x2.Identity, Vector2.Zero);
        }

        private static UnsafeSpan<byte> GetState(int offset) =>
            _state.AsUnsafeSpan(_stateOffset + offset, MaxStateSize - offset);

        private static ref BaseElement GetElement(int offset) =>
            ref *(BaseElement*)(_elements.Ptr + offset);

        private static UnsafeRef<T> GetElementData<T>(int offset) where T : unmanaged =>
            new((T*)(_elements.Ptr + offset + sizeof(BaseElement)));

        private static ref T GetElementData<T>(ref BaseElement element) where T : unmanaged =>
             ref *(T*)((byte*)Unsafe.AsPointer(ref element) + sizeof(BaseElement));

        private static ref BaseElement AllocElement<T>(NewElementType type) where T : unmanaged
        {
            var size = sizeof(T) + sizeof(BaseElement);
            if (!_elements.CheckCapacity(size))
                throw new InvalidOperationException($"Element tree exceeded maximum size of {MaxElementSize} bytes.");

            return ref *(BaseElement*)_elements.AddRange(size).GetUnsafePtr();
        }

        private static ref BaseElement BeginElement<T>(NewElementType type) where T : unmanaged
        {
            ref var e = ref AllocElement<T>(type);
            BeginElementInternal(type, ref e);
            _elementStack.Add((ushort)GetOffset(ref e));
            return ref e;
        }

        private static void EndElement(NewElementType type)
        {
            Debug.Assert(_elementStackCount > 0);
            var elementOffset = _elementStack[--_elementStackCount];
            _nextSibling = elementOffset;
            ref var e = ref GetElement(elementOffset);
            e.NextSibling = (ushort)_elements.Length;
            Debug.Assert(e.Type == type);
        }

        private static void BeginElementInternal(NewElementType type, ref BaseElement e)
        {
            e.Type = type;
            e.Parent = _elementStack.Length > 0 ? _elementStack[^1] : (ushort)0;
            e.NextSibling = 0;
            if (e.Parent != 0)
            {
                ref var p = ref GetElement(e.Parent);
                p.ChildCount++;
                if (p.FirstChild == 0)
                    p.FirstChild = (ushort)((byte*)Unsafe.AsPointer(ref e) - _elements.Ptr);
            }
        }

        private static int GetOffset(ref BaseElement element)
        {
            var offset = (byte*)Unsafe.AsPointer(ref element) - _elements.Ptr;
            Debug.Assert(offset >= 0);
            Debug.Assert(offset < MaxElementSize);
            return (int)offset;
        }

        /// <summary>
        /// Creates a leaf element that is linked into the tree but not pushed onto the stack (no children).
        /// </summary>
        private static ref BaseElement CreateLeafElement<T>(NewElementType type) where T : unmanaged
        {
            ref var e = ref AllocElement<T>(type);
            BeginElementInternal(type, ref e);
            e.NextSibling = (ushort)_elements.Length;
            return ref e;
        }

        private static ushort AddAsset(object asset)
        {
            Debug.Assert(_assetCount < MaxAssets, "Asset array exceeded maximum capacity.");
            var index = (ushort)_assetCount;
            _assets[_assetCount++] = asset;
            return index;
        }

        internal static object? GetAsset(ushort index) => _assets[index];

        // ──────────────────────────────────────────────
        // Size (single-child wrapper)
        // ──────────────────────────────────────────────

        public static int BeginSize(Size width, Size height) => BeginSize(new Size2(width, height));

        public static int BeginSize(Size2 size)
        {
            ref var e = ref BeginElement<SizeElement>(NewElementType.Size);
            ref var d = ref GetElementData<SizeElement>(ref e);
            d.Size = size;
            return GetOffset(ref e);
        }

        public static void EndSize() => EndElement(NewElementType.Size);

        // ──────────────────────────────────────────────
        // Padding (single-child wrapper)
        // ──────────────────────────────────────────────

        public static int BeginPadding(EdgeInsets padding)
        {
            ref var e = ref BeginElement<PaddingElement>(NewElementType.Padding);
            ref var d = ref GetElementData<PaddingElement>(ref e);
            d.Padding = padding;
            return GetOffset(ref e);
        }

        public static void EndPadding() => EndElement(NewElementType.Padding);

        // ──────────────────────────────────────────────
        // Fill (single-child wrapper)
        // ──────────────────────────────────────────────

        public static int BeginFill(Color color)
        {
            ref var e = ref BeginElement<FillElement>(NewElementType.Fill);
            ref var d = ref GetElementData<FillElement>(ref e);
            d.Color = color;
            return GetOffset(ref e);
        }

        public static void EndFill() => EndElement(NewElementType.Fill);

        // ──────────────────────────────────────────────
        // Border (single-child wrapper)
        // ──────────────────────────────────────────────

        public static int BeginBorder(float width, Color color, BorderRadius radius = default)
        {
            ref var e = ref BeginElement<BorderElement>(NewElementType.Border);
            ref var d = ref GetElementData<BorderElement>(ref e);
            d.Width = width;
            d.Color = color;
            d.Radius = radius;
            return GetOffset(ref e);
        }

        public static void EndBorder() => EndElement(NewElementType.Border);

        // ──────────────────────────────────────────────
        // Margin (single-child wrapper)
        // ──────────────────────────────────────────────

        public static int BeginMargin(EdgeInsets margin)
        {
            ref var e = ref BeginElement<MarginElement>(NewElementType.Margin);
            ref var d = ref GetElementData<MarginElement>(ref e);
            d.Margin = margin;
            return GetOffset(ref e);
        }

        public static void EndMargin() => EndElement(NewElementType.Margin);

        // ──────────────────────────────────────────────
        // Align (single-child wrapper)
        // ──────────────────────────────────────────────

        public static int BeginAlign(Align2 align)
        {
            ref var e = ref BeginElement<AlignElement>(NewElementType.Align);
            ref var d = ref GetElementData<AlignElement>(ref e);
            d.Align = align;
            return GetOffset(ref e);
        }

        public static void EndAlign() => EndElement(NewElementType.Align);

        // ──────────────────────────────────────────────
        // Clip (single-child wrapper)
        // ──────────────────────────────────────────────

        public static int BeginClip(BorderRadius radius = default)
        {
            ref var e = ref BeginElement<ClipElement>(NewElementType.Clip);
            ref var d = ref GetElementData<ClipElement>(ref e);
            d.Radius = radius;
            return GetOffset(ref e);
        }

        public static void EndClip() => EndElement(NewElementType.Clip);

        // ──────────────────────────────────────────────
        // Opacity (single-child wrapper)
        // ──────────────────────────────────────────────

        public static int BeginOpacity(float opacity)
        {
            ref var e = ref BeginElement<OpacityElement>(NewElementType.Opacity);
            ref var d = ref GetElementData<OpacityElement>(ref e);
            d.Opacity = opacity;
            return GetOffset(ref e);
        }

        public static void EndOpacity() => EndElement(NewElementType.Opacity);

        // ──────────────────────────────────────────────
        // Row (multi-child container)
        // ──────────────────────────────────────────────

        public static int BeginRow(float spacing = 0)
        {
            ref var e = ref BeginElement<RowElement>(NewElementType.Row);
            ref var d = ref GetElementData<RowElement>(ref e);
            d.Spacing = spacing;
            return GetOffset(ref e);
        }

        public static void EndRow() => EndElement(NewElementType.Row);

        // ──────────────────────────────────────────────
        // Column (multi-child container)
        // ──────────────────────────────────────────────

        public static int BeginColumn(float spacing = 0)
        {
            ref var e = ref BeginElement<ColumnElement>(NewElementType.Column);
            ref var d = ref GetElementData<ColumnElement>(ref e);
            d.Spacing = spacing;
            return GetOffset(ref e);
        }

        public static void EndColumn() => EndElement(NewElementType.Column);

        // ──────────────────────────────────────────────
        // Flex (leaf in tree, parent distributes space)
        // ──────────────────────────────────────────────

        public static int Flex(float flex = 1.0f)
        {
            ref var e = ref CreateLeafElement<FlexElement>(NewElementType.Flex);
            ref var d = ref GetElementData<FlexElement>(ref e);
            d.Flex = flex;
            return GetOffset(ref e);
        }

        // ──────────────────────────────────────────────
        // Spacer (leaf)
        // ──────────────────────────────────────────────

        public static int Spacer(float width, float height) => Spacer(new Vector2(width, height));

        public static int Spacer(Vector2 size)
        {
            ref var e = ref CreateLeafElement<SpacerElement>(NewElementType.Spacer);
            ref var d = ref GetElementData<SpacerElement>(ref e);
            d.Size = size;
            return GetOffset(ref e);
        }

        // ──────────────────────────────────────────────
        // Label (leaf)
        // ──────────────────────────────────────────────

        public static int Label(UnsafeSpan<char> text, Font font, float fontSize, Color color,
            Align2 align = default, TextOverflow overflow = TextOverflow.Overflow)
        {
            ref var e = ref CreateLeafElement<LabelElement>(NewElementType.Label);
            ref var d = ref GetElementData<LabelElement>(ref e);
            d.Text = text;
            d.FontSize = fontSize;
            d.Color = color;
            d.Align = align;
            d.Overflow = overflow;
            d.AssetIndex = AddAsset(font);
            return GetOffset(ref e);
        }

        // ──────────────────────────────────────────────
        // Image (leaf)
        // ──────────────────────────────────────────────

        public static int Image(Sprite sprite, Size2 size = default, ImageStretch stretch = ImageStretch.Uniform,
            Color color = default, float scale = 1.0f)
        {
            ref var e = ref CreateLeafElement<ImageElement>(NewElementType.Image);
            ref var d = ref GetElementData<ImageElement>(ref e);
            d.Size = size;
            d.Stretch = stretch;
            d.Align = NoZ.Align.Center;
            d.Scale = scale;
            d.Color = color.IsTransparent ? Color.White : color;
            d.Width = sprite.Bounds.Width;
            d.Height = sprite.Bounds.Height;
            d.AssetIndex = AddAsset(sprite);
            return GetOffset(ref e);
        }

        // ──────────────────────────────────────────────
        // Widget
        // ──────────────────────────────────────────────

        private static ref WidgetElement GetCurrentWidget()
        {
            Debug.Assert(_currentWidget != 0);
            ref var e = ref GetElement(_currentWidget);
            Debug.Assert(e.Type == NewElementType.Widget);
            return ref GetElementData<WidgetElement>(ref e);
        }

        public static bool IsHovered() => GetCurrentWidget().Flags.HasFlag(ElementFlags.Hovered);
        public static bool WasPressed() => GetCurrentWidget().Flags.HasFlag(ElementFlags.Pressed);

        public static int BeginWidget<T>(int id) where T : unmanaged
        {
            var offset = BeginWidget(id);
            ref var e = ref GetElement(offset);
            ref var d = ref GetElementData<WidgetElement>(ref e);
            var wd = _elements.AddRange(sizeof(T));
            d.Data = (ushort)(wd.GetUnsafePtr() - _elements.Ptr);
            return offset;
        }

        public static int BeginWidget(int id)
        {
            ref var e = ref BeginElement<WidgetElement>(NewElementType.Widget);
            ref var d = ref GetElementData<WidgetElement>(ref e);
            var offset = (ushort)GetOffset(ref e);
            d.Id = id;
            d.Data = 0;
            _widgets[id] = offset;
            _currentWidget = offset;
            return offset;
        }

        public static void EndWidget()
        {
            EndElement(NewElementType.Widget);

            _currentWidget = 0;
            for (int i = _elementStackCount - 1; i >= 0; i--)
            {
                ref var e = ref GetElement(_elementStack[i]);
                if (e.Type == NewElementType.Widget)
                {
                    _currentWidget = _elementStack[i];
                    break;
                }
            }
        }

        // ──────────────────────────────────────────────
        // Layout (axis-independent: width first, then height)
        // ──────────────────────────────────────────────

        private static float EdgeInset(in EdgeInsets ei, int axis) => axis == 0 ? ei.Horizontal : ei.Vertical;
        private static float EdgeMin(in EdgeInsets ei, int axis) => axis == 0 ? ei.L : ei.T;

        /// <summary>
        /// Bottom-up content measurement for a single axis.
        /// In the height pass (axis=1), Rect.Width is already resolved so wrapped labels work.
        /// </summary>
        private static float FitAxis(int offset, int axis, int layoutAxis)
        {
            ref var e = ref GetElement(offset);
            switch (e.Type)
            {
                case NewElementType.Size:
                {
                    ref var d = ref GetElementData<SizeElement>(ref e);
                    var mode = d.Size[axis].Mode;
                    if (mode == SizeMode.Default)
                        mode = SizeMode.Fit; // in unconstrained context, Default always fits
                    return mode switch
                    {
                        SizeMode.Fixed => d.Size[axis].Value,
                        SizeMode.Fit => e.ChildCount > 0 ? FitAxis(e.FirstChild, axis, layoutAxis) : 0,
                        SizeMode.Percent => 0,
                        _ => 0
                    };
                }

                case NewElementType.Padding:
                {
                    ref var d = ref GetElementData<PaddingElement>(ref e);
                    var child = e.ChildCount > 0 ? FitAxis(e.FirstChild, axis, layoutAxis) : 0;
                    return child + EdgeInset(d.Padding, axis);
                }

                case NewElementType.Margin:
                {
                    ref var d = ref GetElementData<MarginElement>(ref e);
                    var child = e.ChildCount > 0 ? FitAxis(e.FirstChild, axis, layoutAxis) : 0;
                    return child + EdgeInset(d.Margin, axis);
                }

                case NewElementType.Border:
                {
                    ref var d = ref GetElementData<BorderElement>(ref e);
                    var child = e.ChildCount > 0 ? FitAxis(e.FirstChild, axis, layoutAxis) : 0;
                    return child + d.Width * 2;
                }

                case NewElementType.Fill:
                case NewElementType.Clip:
                case NewElementType.Opacity:
                case NewElementType.Widget:
                case NewElementType.Align:
                    return e.ChildCount > 0 ? FitAxis(e.FirstChild, axis, layoutAxis) : 0;

                case NewElementType.Row:
                {
                    ref var d = ref GetElementData<RowElement>(ref e);
                    // axis=0 (layout axis for Row): sum children. axis=1: max children.
                    return FitRowColumn(ref e, axis, 0, d.Spacing);
                }

                case NewElementType.Column:
                {
                    ref var d = ref GetElementData<ColumnElement>(ref e);
                    // axis=1 (layout axis for Column): sum children. axis=0: max children.
                    return FitRowColumn(ref e, axis, 1, d.Spacing);
                }

                case NewElementType.Flex:
                    return 0;

                case NewElementType.Spacer:
                {
                    ref var d = ref GetElementData<SpacerElement>(ref e);
                    return d.Size[axis];
                }

                case NewElementType.Label:
                {
                    ref var d = ref GetElementData<LabelElement>(ref e);
                    var font = (Font)_assets[d.AssetIndex]!;
                    // Height pass with wrap: use already-resolved width
                    if (axis == 1 && d.Overflow == TextOverflow.Wrap && e.Rect.Width > 0)
                        return TextRender.MeasureWrapped(d.Text.AsReadOnlySpan(), font, d.FontSize, e.Rect.Width).Y;
                    var measure = TextRender.Measure(d.Text.AsReadOnlySpan(), font, d.FontSize);
                    return measure[axis];
                }

                case NewElementType.Image:
                {
                    ref var d = ref GetElementData<ImageElement>(ref e);
                    if (d.Size[axis].IsFixed) return d.Size[axis].Value;
                    return (axis == 0 ? d.Width : d.Height) * d.Scale;
                }

                default:
                    return 0;
            }
        }

        /// <summary>
        /// FitAxis helper for Row/Column. If axis matches containerAxis, sum children + spacing.
        /// Otherwise, take max of children.
        /// </summary>
        private static float FitRowColumn(ref BaseElement e, int axis, int containerAxis, float spacing)
        {
            var fit = 0f;
            var childCount = 0;
            var childOffset = (int)e.FirstChild;
            for (int i = 0; i < e.ChildCount; i++)
            {
                ref var child = ref GetElement(childOffset);
                if (child.Type != NewElementType.Flex)
                {
                    var childFit = FitAxis(childOffset, axis, containerAxis);
                    if (axis == containerAxis)
                        fit += childFit;
                    else
                        fit = Math.Max(fit, childFit);
                }
                childCount++;
                childOffset = child.NextSibling;
            }
            if (axis == containerAxis && childCount > 1)
                fit += (childCount - 1) * spacing;
            return fit;
        }

        /// <summary>
        /// Top-down recursive pass for a single axis. Called twice: once for width (axis=0),
        /// once for height (axis=1). In the height pass, Rect.Width is already set.
        /// </summary>
        private static void LayoutAxis(int offset, float position, float available, int axis, int layoutAxis)
        {
            ref var e = ref GetElement(offset);
            float size;

            switch (e.Type)
            {
                case NewElementType.Size:
                {
                    ref var d = ref GetElementData<SizeElement>(ref e);
                    var mode = d.Size[axis].Mode;
                    if (mode == SizeMode.Default)
                        mode = (layoutAxis == axis) ? SizeMode.Fit : SizeMode.Percent;
                    size = mode switch
                    {
                        SizeMode.Fixed => d.Size[axis].Value,
                        SizeMode.Percent => available < float.MaxValue ? available * d.Size[axis].Value : 0,
                        SizeMode.Fit => e.ChildCount > 0 ? FitAxis(e.FirstChild, axis, layoutAxis) : 0,
                        _ => 0
                    };
                    break;
                }

                case NewElementType.Padding:
                {
                    ref var d = ref GetElementData<PaddingElement>(ref e);
                    var inset = EdgeInset(d.Padding, axis);
                    size = available < float.MaxValue
                        ? available
                        : (e.ChildCount > 0 ? FitAxis(e.FirstChild, axis, layoutAxis) : 0) + inset;
                    break;
                }

                case NewElementType.Margin:
                {
                    ref var d = ref GetElementData<MarginElement>(ref e);
                    var inset = EdgeInset(d.Margin, axis);
                    size = available < float.MaxValue
                        ? available
                        : (e.ChildCount > 0 ? FitAxis(e.FirstChild, axis, layoutAxis) : 0) + inset;
                    break;
                }

                case NewElementType.Border:
                {
                    ref var d = ref GetElementData<BorderElement>(ref e);
                    var inset = d.Width * 2;
                    size = available < float.MaxValue
                        ? available
                        : (e.ChildCount > 0 ? FitAxis(e.FirstChild, axis, layoutAxis) : 0) + inset;
                    break;
                }

                case NewElementType.Fill:
                case NewElementType.Clip:
                case NewElementType.Opacity:
                case NewElementType.Widget:
                    size = available < float.MaxValue
                        ? available
                        : (e.ChildCount > 0 ? FitAxis(e.FirstChild, axis, layoutAxis) : 0);
                    break;

                case NewElementType.Align:
                    size = available;
                    break;

                case NewElementType.Row:
                    size = available < float.MaxValue
                        ? available
                        : (e.ChildCount > 0 ? FitAxis(offset, axis, 0) : 0);
                    break;

                case NewElementType.Column:
                    size = available < float.MaxValue
                        ? available
                        : (e.ChildCount > 0 ? FitAxis(offset, axis, 1) : 0);
                    break;

                case NewElementType.Flex:
                    size = available;
                    break;

                case NewElementType.Spacer:
                {
                    ref var d = ref GetElementData<SpacerElement>(ref e);
                    size = d.Size[axis];
                    break;
                }

                case NewElementType.Label:
                {
                    ref var d = ref GetElementData<LabelElement>(ref e);
                    var font = (Font)_assets[d.AssetIndex]!;
                    if (axis == 1 && d.Overflow == TextOverflow.Wrap && e.Rect.Width > 0)
                        size = TextRender.MeasureWrapped(d.Text.AsReadOnlySpan(), font, d.FontSize, e.Rect.Width).Y;
                    else
                        size = TextRender.Measure(d.Text.AsReadOnlySpan(), font, d.FontSize)[axis];
                    break;
                }

                case NewElementType.Image:
                {
                    ref var d = ref GetElementData<ImageElement>(ref e);
                    if (d.Size[axis].IsFixed)
                        size = d.Size[axis].Value;
                    else
                        size = (axis == 0 ? d.Width : d.Height) * d.Scale;
                    break;
                }

                default:
                    size = 0;
                    break;
            }

            e.Rect[axis] = position;
            e.Rect[axis + 2] = size;

            // Recurse children
            switch (e.Type)
            {
                case NewElementType.Row when axis == 0:
                    LayoutRowColumnAxis(ref e, axis, 0);
                    break;
                case NewElementType.Row when axis == 1:
                    LayoutCrossAxis(ref e, axis);
                    break;
                case NewElementType.Column when axis == 1:
                    LayoutRowColumnAxis(ref e, axis, 1);
                    break;
                case NewElementType.Column when axis == 0:
                    LayoutCrossAxis(ref e, axis);
                    break;
                case NewElementType.Align:
                    LayoutAlignAxis(ref e, axis);
                    break;
                case NewElementType.Padding:
                {
                    ref var d = ref GetElementData<PaddingElement>(ref e);
                    var inset = EdgeInset(d.Padding, axis);
                    var childPos = e.Rect[axis] + EdgeMin(d.Padding, axis);
                    var childAvail = Math.Max(0, size - inset);
                    LayoutChildrenAxis(ref e, childPos, childAvail, axis, layoutAxis);
                    break;
                }
                case NewElementType.Margin:
                {
                    ref var d = ref GetElementData<MarginElement>(ref e);
                    var inset = EdgeInset(d.Margin, axis);
                    var childPos = e.Rect[axis] + EdgeMin(d.Margin, axis);
                    var childAvail = Math.Max(0, size - inset);
                    LayoutChildrenAxis(ref e, childPos, childAvail, axis, layoutAxis);
                    break;
                }
                case NewElementType.Border:
                {
                    ref var d = ref GetElementData<BorderElement>(ref e);
                    var childPos = e.Rect[axis] + d.Width;
                    var childAvail = Math.Max(0, size - d.Width * 2);
                    LayoutChildrenAxis(ref e, childPos, childAvail, axis, layoutAxis);
                    break;
                }
                default:
                    LayoutChildrenAxis(ref e, e.Rect[axis], size, axis, layoutAxis);
                    break;
            }
        }

        /// <summary>
        /// Recurse all children with the same position and available size on this axis.
        /// </summary>
        private static void LayoutChildrenAxis(ref BaseElement e, float childPos, float childAvail, int axis, int layoutAxis)
        {
            var childOffset = (int)e.FirstChild;
            for (int i = 0; i < e.ChildCount; i++)
            {
                ref var child = ref GetElement(childOffset);
                LayoutAxis(childOffset, childPos, childAvail, axis, layoutAxis);
                childOffset = child.NextSibling;
            }
        }

        /// <summary>
        /// Layout Align child on a single axis: position based on alignment factor.
        /// </summary>
        private static void LayoutAlignAxis(ref BaseElement e, int axis)
        {
            if (e.ChildCount == 0) return;
            ref var d = ref GetElementData<AlignElement>(ref e);
            var childFit = FitAxis(e.FirstChild, axis, -1);
            var alignFactor = (axis == 0 ? d.Align.X : d.Align.Y).ToFactor();
            var childPos = e.Rect[axis] + (e.Rect.GetSize(axis) - childFit) * alignFactor;
            LayoutAxis(e.FirstChild, childPos, childFit, axis, -1);
        }

        /// <summary>
        /// Row/Column cross-axis: give each child the full size, same position.
        /// </summary>
        private static void LayoutCrossAxis(ref BaseElement e, int axis)
        {
            var pos = e.Rect[axis];
            var avail = e.Rect.GetSize(axis);
            var childOffset = (int)e.FirstChild;
            for (int i = 0; i < e.ChildCount; i++)
            {
                ref var child = ref GetElement(childOffset);
                LayoutAxis(childOffset, pos, avail, axis, axis == 0 ? 0 : 1);
                childOffset = child.NextSibling;
            }
        }

        /// <summary>
        /// Row/Column layout-axis: flex distribution along the container's main axis.
        /// </summary>
        private static void LayoutRowColumnAxis(ref BaseElement e, int axis, int containerAxis)
        {
            float spacing;
            if (e.Type == NewElementType.Row)
            {
                ref var d = ref GetElementData<RowElement>(ref e);
                spacing = d.Spacing;
            }
            else
            {
                ref var d = ref GetElementData<ColumnElement>(ref e);
                spacing = d.Spacing;
            }

            // Pass 1: measure non-flex children, accumulate fixed size
            var fixedTotal = 0f;
            var flexTotal = 0f;
            var childCount = 0;
            var childOffset = (int)e.FirstChild;
            for (int i = 0; i < e.ChildCount; i++)
            {
                ref var child = ref GetElement(childOffset);
                if (child.Type == NewElementType.Flex)
                {
                    ref var fd = ref GetElementData<FlexElement>(ref child);
                    flexTotal += fd.Flex;
                }
                else
                {
                    fixedTotal += FitAxis(childOffset, axis, containerAxis);
                }
                childCount++;
                childOffset = child.NextSibling;
            }
            if (childCount > 1)
                fixedTotal += (childCount - 1) * spacing;

            // Pass 2: position children
            var offset = 0f;
            var remaining = e.Rect.GetSize(axis) - fixedTotal;
            childOffset = (int)e.FirstChild;
            for (int i = 0; i < e.ChildCount; i++)
            {
                if (i > 0) offset += spacing;

                ref var child = ref GetElement(childOffset);
                var childPos = e.Rect[axis] + offset;

                if (child.Type == NewElementType.Flex)
                {
                    ref var fd = ref GetElementData<FlexElement>(ref child);
                    var flexSize = flexTotal > 0 ? (fd.Flex / flexTotal) * remaining : 0;
                    LayoutAxis(childOffset, childPos, flexSize, axis, containerAxis);
                    offset += flexSize;
                }
                else
                {
                    LayoutAxis(childOffset, childPos, float.MaxValue, axis, containerAxis);
                    offset += child.Rect.GetSize(axis);
                }

                childOffset = child.NextSibling;
            }
        }

        // ──────────────────────────────────────────────
        // Transforms
        // ──────────────────────────────────────────────

        private static void UpdateTransforms(int offset, in Matrix3x2 parentTransform, Vector2 parentSize)
        {
            ref var e = ref GetElement(offset);

            var localTransform = Matrix3x2.CreateTranslation(e.Rect.X, e.Rect.Y);
            e.LocalToWorld = localTransform * parentTransform;
            Matrix3x2.Invert(e.LocalToWorld, out e.WorldToLocal);

            // Make rect local (0,0 based) after transform is computed
            var rectSize = e.Rect.Size;
            e.Rect.X = 0;
            e.Rect.Y = 0;

            // Recurse children
            var childOffset = (int)e.FirstChild;
            for (int i = 0; i < e.ChildCount; i++)
            {
                ref var child = ref GetElement(childOffset);
                // Child positions are absolute from layout, convert to parent-relative
                var absPos = child.Rect.Position;
                child.Rect.X = absPos.X - e.LocalToWorld.M31;
                child.Rect.Y = absPos.Y - e.LocalToWorld.M32;
                UpdateTransforms(childOffset, e.LocalToWorld, rectSize);
                childOffset = child.NextSibling;
            }
        }

        internal static Vector2 ScreenSize;
    }
}
