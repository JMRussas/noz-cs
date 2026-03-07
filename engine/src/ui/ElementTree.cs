//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using NoZ.Platform;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace NoZ;

internal enum ElementType : byte
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
    EditableText,
    Popup,
    Cursor,
    Transform,
    Grid,
    Scene,
    Scrollable,
}

internal struct BaseElement
{
    public ElementType Type;
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
    public BorderRadius Radius;
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

internal struct GridElement
{
    public float Spacing;
    public int Columns;
    public float CellWidth;
    public float CellHeight;
    public float CellMinWidth;
    public float CellHeightOffset;
    public int VirtualCount;
    public int StartIndex;
}

internal struct TransformElement
{
    public Vector2 Pivot;
    public Vector2 Translate;
    public float Rotate;
    public Vector2 Scale;
}

internal struct SceneElement
{
    public Size2 Size;
    public Color ClearColor;
    public int SampleCount;
    public ushort AssetIndex; // stores (Camera, Action) tuple
}

internal struct ScrollableElement
{
    public float ScrollSpeed;
    public ScrollbarVisibility ScrollbarVisibility;
    public float ScrollbarWidth;
    public float ScrollbarMinThumbHeight;
    public Color ScrollbarTrackColor;
    public Color ScrollbarThumbColor;
    public Color ScrollbarThumbHoverColor;
    public float ScrollbarPadding;
    public float ScrollbarBorderRadius;
    public int WidgetId;
}

internal struct ScrollableState
{
    public float Offset;
    public float ContentHeight;
}

internal struct CursorElement
{
    public SystemCursor SystemCursor;
    public ushort AssetIndex; // 0 = no sprite, use SystemCursor
    public bool IsSprite;
}

internal struct PopupElement
{
    public Rect AnchorRect;
    public float AnchorFactorX;
    public float AnchorFactorY;
    public float PopupAlignFactorX;
    public float PopupAlignFactorY;
    public float Spacing;
    public bool ClampToScreen;
    public bool AutoClose;
    public bool Interactive;
}

public static unsafe partial class ElementTree
{
    private const int MaxStateSize = 65535;
    private const int MaxElementSize = 65535;
    private const int MaxElementDepth = 64;
    private const int MaxId = 32000;
    private const int MaxAssets = 1024;
    private const int MaxVertices = 16384;
    private const int MaxIndices = 32768;
    private const int MaxPopups = 4;

    private static NativeArray<byte> _elements;
    private static NativeArray<byte>[] _statePools = null!;
    private static int _currentStatePool;
    private static NativeArray<ushort> _elementStack;
    private static NativeArray<ushort> _widgets;
    private static int _elementStackCount;
    private static ushort _frame;
    private static ushort _nextSibling;
    private static ushort _currentWidget;

    private static readonly object?[] _assets = new object?[MaxAssets];
    private static int _assetCount;

    // Popup tracking
    private static readonly int[] _popupOffsets = new int[MaxPopups];
    private static int _popupCount;
    private static int _activePopupCount;
    internal static bool ClosePopups { get; private set; }
    internal static int ActivePopupCount => _activePopupCount;

    // Input state
    private static int _focusId;
    private static int _captureId;
    private static readonly WidgetInputState[] _widgetStates = new WidgetInputState[MaxId];

    // Drawing state (self-contained, not shared with UI)
    private static RenderMesh _mesh;
    private static NativeArray<UIVertex> _vertices;
    private static NativeArray<ushort> _indices;
    private static Shader _shader = null!;
    private static float _drawOpacity = 1.0f;

    internal struct WidgetInputState
    {
        public ElementFlags Flags;
        public ElementFlags PrevFlags;
        public ushort LastFrame;
        public int StateOffset;
        public ushort StateSize;
    }

    public static void Init()
    {
        _elements = new NativeArray<byte>(MaxElementSize);
        _statePools = [
            new NativeArray<byte>(MaxStateSize),
            new NativeArray<byte>(MaxStateSize)
        ];
        _elementStack = new NativeArray<ushort>(MaxElementDepth, MaxElementDepth);
        _widgets = new NativeArray<ushort>(MaxId, MaxId);

        _vertices = new NativeArray<UIVertex>(MaxVertices);
        _indices = new NativeArray<ushort>(MaxIndices);
        _mesh = Graphics.CreateMesh<UIVertex>(MaxVertices, MaxIndices, BufferUsage.Dynamic, "ElementTreeMesh");
        _shader = Asset.Get<Shader>(AssetType.Shader, "ui")!;
    }

    internal static void Shutdown()
    {
        _vertices.Dispose();
        _indices.Dispose();
        Graphics.Driver.DestroyMesh(_mesh.Handle);
    }

    internal static void Begin()
    {
        _frame++;
        _layoutCycleLogged = false;
        _currentStatePool ^= 1;
        _statePools[_currentStatePool].Clear();
        _assetCount = 0;
        _elements.Clear();
        _elementStackCount = 0;
        _currentWidget = 0;
        _popupCount = 0;
        _activePopupCount = 0;
        ClosePopups = false;
    }

    internal static void End()
    {
        if (_elements.Length == 0) return;

        LayoutAxis(0, 0, ScreenSize.X, 0, -1);  // Width pass
        LayoutAxis(0, 0, ScreenSize.Y, 1, -1);  // Height pass
        UpdateTransforms(0, Matrix3x2.Identity, Vector2.Zero);
        HandleInput();
    }

    internal static ref BaseElement GetElement(int offset) =>
        ref *(BaseElement*)(_elements.Ptr + offset);

    private static UnsafeRef<T> GetElementData<T>(int offset) where T : unmanaged =>
        new((T*)(_elements.Ptr + offset + sizeof(BaseElement)));

    internal static ref T GetElementData<T>(ref BaseElement element) where T : unmanaged =>
         ref *(T*)((byte*)Unsafe.AsPointer(ref element) + sizeof(BaseElement));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Matrix3x2 GetWorldToLocal(ref BaseElement e)
    {
        if (e.WorldToLocal.M11 == 0 && e.WorldToLocal.M22 == 0)
            Matrix3x2.Invert(e.LocalToWorld, out e.WorldToLocal);
        return e.WorldToLocal;
    }

    private static ref BaseElement AllocElement<T>(ElementType type) where T : unmanaged
    {
        var size = sizeof(T) + sizeof(BaseElement);
        if (!_elements.CheckCapacity(size))
            throw new InvalidOperationException($"Element tree exceeded maximum size of {MaxElementSize} bytes.");

        return ref *(BaseElement*)_elements.AddRange(size).GetUnsafePtr();
    }

    private static ref BaseElement BeginElement<T>(ElementType type) where T : unmanaged
    {
        ref var e = ref AllocElement<T>(type);
        BeginElementInternal(type, ref e);
        _elementStack[_elementStackCount++] = (ushort)GetOffset(ref e);
        return ref e;
    }

    private static void EndElement(ElementType type)
    {
        Debug.Assert(_elementStackCount > 0);
        var elementOffset = _elementStack[--_elementStackCount];
        _nextSibling = elementOffset;
        ref var e = ref GetElement(elementOffset);
        e.NextSibling = (ushort)_elements.Length;
        Debug.Assert(e.Type == type);
    }

    internal static void EndElement()
    {
        Debug.Assert(_elementStackCount > 0);
        var elementOffset = _elementStack[--_elementStackCount];
        _nextSibling = elementOffset;
        ref var e = ref GetElement(elementOffset);
        e.NextSibling = (ushort)_elements.Length;
    }

    internal static bool HasCurrentWidget() => _currentWidget != 0;

    private static void BeginElementInternal(ElementType type, ref BaseElement e)
    {
        e.Type = type;
        e.Parent = _elementStackCount > 0 ? _elementStack[_elementStackCount - 1] : (ushort)0;
        e.NextSibling = 0;
        e.ChildCount = 0;
        e.FirstChild = 0;
        if (_elementStackCount > 0)
        {
            ref var p = ref GetElement(e.Parent);
            p.ChildCount++;
            if (p.FirstChild == 0)
                p.FirstChild = (ushort)((byte*)Unsafe.AsPointer(ref e) - _elements.Ptr);
        }

    }

    internal static int GetOffset(ref BaseElement element)
    {
        var offset = (byte*)Unsafe.AsPointer(ref element) - _elements.Ptr;
        Debug.Assert(offset >= 0);
        Debug.Assert(offset < MaxElementSize);
        return (int)offset;
    }

    internal static ref BaseElement CreateLeafElement<T>(ElementType type) where T : unmanaged
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

    public static UnsafeSpan<char> Text(ReadOnlySpan<char> text) => UI.AddText(text);
}
