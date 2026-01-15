//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using NoZ.Platform;

namespace NoZ;

[Flags]
public enum ElementFlags : uint
{
    None = 0,
    Hovered = 1 << 0,
    Pressed = 1 << 1,
    Down = 1 << 2
}

public enum Align
{
    None,
    Top,
    Left,
    Bottom,
    Right,
    TopLeft,
    TopRight,
    TopCenter,
    CenterLeft,
    CenterRight,
    Center,
    BottomLeft,
    BottomRight,
    BottomCenter
}

public enum ElementType : byte
{
    None = 0,
    Canvas,
    Column,
    Container,
    Expanded,
    Grid,
    Image,
    Label,
    Row,
    Scrollable,
    Spacer,
    Transform,
    Popup,
    TextBox
}

public enum ImageStretch : byte
{
    None,
    Fill,
    Uniform
}

public enum CanvasType : byte
{
    Screen,
    World
}

public readonly struct EdgeInsets(float top, float left, float bottom, float right)
{
    public readonly float Top = top;
    public readonly float Left = left;
    public readonly float Bottom = bottom;
    public readonly float Right = right;

    public EdgeInsets(float all) : this(all, all, all, all)
    {
    }

    public float Horizontal => Left + Right;
    public float Vertical => Top + Bottom;

    public static EdgeInsets All(float v) => new(v, v, v, v);
    public static EdgeInsets TopOnly(float v) => new(v, 0, 0, 0);
    public static EdgeInsets BottomOnly(float v) => new(0, 0, v, 0);
    public static EdgeInsets LeftOnly(float v) => new(0, v, 0, 0);
    public static EdgeInsets RightOnly(float v) => new(0, 0, 0, v);
    public static EdgeInsets TopBottom(float v) => new(v, 0, v, 0);
    public static EdgeInsets LeftRight(float v) => new(0, v, 0, v);
    public static EdgeInsets LeftRight(float l, float r) => new(0, l, 0, r);

    public static EdgeInsets Symmetric(float vertical, float horizontal) =>
        new(vertical, horizontal, vertical, horizontal);

    public static readonly EdgeInsets Zero = new(0, 0, 0, 0);
}

public struct BorderStyle
{
    public float Radius;
    public float Width;
    public Color Color;

    public static readonly BorderStyle None = new() { Radius = 0, Width = 0, Color = Color.Transparent };
}

// Element-specific data structs
public struct ContainerData
{
    public float Width;
    public float Height;
    public float MinWidth;
    public float MinHeight;
    public Align Align;
    public EdgeInsets Margin;
    public EdgeInsets Padding;
    public Color Color;
    public BorderStyle Border;
    public float Spacing;
    public bool Clip;

    public static ContainerData Default => new()
    {
        Width = float.MaxValue,
        Height = float.MaxValue,
        MinWidth = 0,
        MinHeight = 0,
        Align = Align.None,
        Margin = EdgeInsets.Zero,
        Padding = EdgeInsets.Zero,
        Color = Color.Transparent,
        Border = BorderStyle.None,
        Spacing = 0,
        Clip = false
    };
}

public struct LabelData
{
    public float FontSize;
    public Color Color;
    public Align Align;
    public int TextStart;
    public int TextLength;

    public static LabelData Default => new()
    {
        FontSize = 16,
        Color = Color.White,
        Align = Align.None,
        TextStart = 0,
        TextLength = 0
    };
}

public struct ImageData
{
    public ImageStretch Stretch;
    public Align Align;
    public float Scale;
    public Color Color;
    public nuint Texture;
    public Vector2 UV0;
    public Vector2 UV1;
    public float Width;
    public float Height;

    public static ImageData Default => new()
    {
        Stretch = ImageStretch.Uniform,
        Align = Align.None,
        Scale = 1.0f,
        Color = Color.White,
        Texture = nuint.Zero,
        UV0 = Vector2.Zero,
        UV1 = Vector2.One,
        Width = 0,
        Height = 0
    };
}

public struct ExpandedData
{
    public float Flex;
    public int Axis;

    public static ExpandedData Default => new()
    {
        Flex = 1.0f,
        Axis = 0
    };
}

public struct ScrollableData
{
    public float Offset;
    public float ContentHeight;

    public static ScrollableData Default => new()
    {
        Offset = 0,
        ContentHeight = 0
    };
}

public struct GridData
{
    public float Spacing;
    public int Columns;
    public float CellWidth;
    public float CellHeight;
    public int VirtualCount;
    public int StartRow;

    public static GridData Default => new()
    {
        Spacing = 0,
        Columns = 3,
        CellWidth = 100,
        CellHeight = 100,
        VirtualCount = 0,
        StartRow = 0
    };
}

public struct TransformData
{
    public Vector2 Origin;
    public Vector2 Translate;
    public float Rotate;
    public Vector2 Scale;

    public static TransformData Default => new()
    {
        Origin = new Vector2(0.5f, 0.5f),
        Translate = Vector2.Zero,
        Rotate = 0,
        Scale = Vector2.One
    };
}

public struct PopupData
{
    public Align Anchor;
    public Align PopupAlign;
    public EdgeInsets Margin;

    public static PopupData Default => new()
    {
        Anchor = Align.TopLeft,
        PopupAlign = Align.TopLeft,
        Margin = EdgeInsets.Zero
    };
}

public struct TextBoxData
{
    public float Height;
    public float FontSize;
    public Color BackgroundColor;
    public Color TextColor;
    public Color PlaceholderColor;
    public BorderStyle Border;
    public BorderStyle FocusBorder;
    public bool Password;
    public int TextStart;
    public int TextLength;

    public static TextBoxData Default => new()
    {
        Height = 28f,
        FontSize = 16,
        BackgroundColor = new Color(0.22f, 0.22f, 0.22f, 1f),
        TextColor = Color.White,
        PlaceholderColor = new Color(0.4f, 0.4f, 0.4f, 1f),
        Border = BorderStyle.None,
        FocusBorder = BorderStyle.None,
        Password = false,
        TextStart = 0,
        TextLength = 0
    };
}

public struct SpacerData
{
    public Vector2 Size;
}

public struct CanvasData
{
    public CanvasType Type;
    public Color Color;
    public Vector2Int ColorOffset;
    public Vector2 WorldPosition;
    public Vector2 WorldSize;

    public static CanvasData Default => new()
    {
        Type = CanvasType.Screen,
        Color = Color.Transparent,
        ColorOffset = Vector2Int.Zero,
        WorldPosition = Vector2.Zero,
        WorldSize = Vector2.Zero
    };
}

[StructLayout(LayoutKind.Explicit)]
public struct ElementData
{
    [FieldOffset(0)] public ContainerData Container;
    [FieldOffset(0)] public LabelData Label;
    [FieldOffset(0)] public ImageData Image;
    [FieldOffset(0)] public ExpandedData Expanded;
    [FieldOffset(0)] public ScrollableData Scrollable;
    [FieldOffset(0)] public GridData Grid;
    [FieldOffset(0)] public TransformData Transform;
    [FieldOffset(0)] public PopupData Popup;
    [FieldOffset(0)] public SpacerData Spacer;
    [FieldOffset(0)] public CanvasData Canvas;
    [FieldOffset(0)] public TextBoxData TextBox;
}

public struct Element
{
    public ElementType Type;
    public byte Id;
    public byte CanvasId;
    public int Index;
    public int NextSiblingIndex;
    public int ChildCount;
    public Rect Rect;
    public Vector2 MeasuredSize;
    public Matrix3x2 LocalToWorld;
    public Matrix3x2 WorldToLocal;
    public ElementData Data;
}

public struct ContainerStyle()
{
    public float Width = float.MaxValue;
    public float Height = float.MaxValue;
    public float MinWidth = 0;
    public float MinHeight = 0;
    public Align Align = Align.None;
    public EdgeInsets Margin = EdgeInsets.Zero;
    public EdgeInsets Padding = EdgeInsets.Zero;
    public Color Color = Color.Transparent;
    public BorderStyle Border = BorderStyle.None;
    public float Spacing = 0;
    public byte Id = 0;
    public bool Clip = false;

    public ContainerData ToData() => new()
    {
        Width = Width,
        Height = Height,
        MinWidth = MinWidth,
        MinHeight = MinHeight,
        Align = Align,
        Margin = Margin,
        Padding = Padding,
        Color = Color,
        Border = Border,
        Spacing = Spacing,
        Clip = Clip
    };
}

public struct LabelStyle()
{
    public float FontSize = 16;
    public Color Color = Color.White;
    public Align Align = Align.None;
}

public struct ImageStyle()
{
    public ImageStretch Stretch = ImageStretch.Uniform;
    public Align Align = Align.None;
    public float Scale = 1.0f;
    public Color Color = Color.White;
}

public struct RectangleStyle()
{
    public float Width = float.MaxValue;
    public float Height = float.MaxValue;
    public Color Color = Color.White;
}

public struct ExpandedStyle()
{
    public float Flex = 1.0f;
}

public struct TransformStyle()
{
    public Vector2 Origin = new(0.5f, 0.5f);
    public Vector2 Translate = Vector2.Zero;
    public float Rotate = 0;
    public Vector2 Scale = Vector2.One;
}

public struct ScrollableStyle()
{
    public byte Id = 0;
}

public struct GridStyle()
{
    public float Spacing = 0;
    public int Columns = 3;
    public float CellWidth = 100;
    public float CellHeight = 100;
    public int VirtualCount = 0;
    public byte ScrollId = 0;
    public Action<int, int>? VirtualCellFunc = null;
    public Action<int, int>? VirtualRangeFunc = null;
}

public struct PopupStyle()
{
    public Align Anchor = Align.TopLeft;
    public Align PopupAlign = Align.TopLeft;
    public EdgeInsets Margin = EdgeInsets.Zero;
}

public struct CanvasStyle()
{
    public CanvasType Type = CanvasType.Screen;
    public Color Color = Color.Transparent;
    public Vector2Int ColorOffset = Vector2Int.Zero;
    public Vector2 WorldPosition = Vector2.Zero;
    public Vector2 WorldSize = Vector2.Zero;
    public Camera? WorldCamera = null;
    public byte Id = 0;

    public CanvasData ToData() => new()
    {
        Type = Type,
        Color = Color,
        ColorOffset = ColorOffset,
        WorldPosition = WorldPosition,
        WorldSize = WorldSize
    };
}

public struct TextBoxStyle()
{
    public float Height = 28f;
    public float FontSize = 16;
    public Color BackgroundColor = new(0.22f, 0.22f, 0.22f, 1f);
    public Color TextColor = Color.White;
    public Color PlaceholderColor = new(0.4f, 0.4f, 0.4f, 1f);
    public BorderStyle Border = BorderStyle.None;
    public BorderStyle FocusBorder = BorderStyle.None;
    public bool IsPassword = false;
    public byte Id = 0;

    public TextBoxData ToData() => new()
    {
        Height = Height,
        FontSize = FontSize,
        BackgroundColor = BackgroundColor,
        TextColor = TextColor,
        PlaceholderColor = PlaceholderColor,
        Border = Border,
        FocusBorder = FocusBorder,
        Password = IsPassword,
        TextStart = 0,
        TextLength = 0
    };
}

public static class UI
{
    private const int MaxElements = 4096;
    private const int MaxElementStack = 128;
    private const int MaxPopups = 4;
    private const int MaxTextBuffer = 64 * 1024;
    private const byte ElementIdNone = 0;
    private const byte ElementIdMax = 255;

    private static Font? _font;
    private static Shader? _textShader;
    private static Shader? _uiShader;

    public static UIConfig Config { get; private set; } = new();

    private static readonly AlignInfo[] AlignInfoTable =
    [
        new(false, 0.0f, false, 0.0f), // None
        new(false, 0.0f, true, 0.0f), // Top
        new(true, 0.0f, false, 0.0f), // Left
        new(false, 0.0f, true, 1.0f), // Bottom
        new(true, 1.0f, false, 0.0f), // Right
        new(true, 0.0f, true, 0.0f), // TopLeft
        new(true, 1.0f, true, 0.0f), // TopRight
        new(true, 0.5f, true, 0.0f), // TopCenter
        new(true, 0.0f, true, 0.5f), // CenterLeft
        new(true, 1.0f, true, 0.5f), // CenterRight
        new(true, 0.5f, true, 0.5f), // Center
        new(true, 0.0f, true, 1.0f), // BottomLeft
        new(true, 1.0f, true, 1.0f), // BottomRight
        new(true, 0.5f, true, 1.0f), // BottomCenter
    ];

    private readonly struct AlignInfo(bool hasX, float x, bool hasY, float y)
    {
        public readonly bool HasX = hasX;
        public readonly float X = x;
        public readonly bool HasY = hasY;
        public readonly float Y = y;
    }

    private struct ElementState
    {
        public ElementFlags Flags;
        public int Index;
        public float ScrollOffset;
        public Rect Rect;
        public string Text;
    }

    private struct CanvasState
    {
        public int ElementIndex;
        public Rect WorldBounds;
        public ElementState[]? ElementStates;
    }

    // Element storage
    private static readonly Element[] _elements = new Element[MaxElements];
    private static readonly int[] _elementStack = new int[MaxElementStack];
    private static readonly int[] _popups = new int[MaxPopups];
    private static readonly ElementState[] _elementStates = new ElementState[ElementIdMax + 1];
    private static readonly char[] _textBuffer = new char[MaxTextBuffer];

    // Canvas state (keyed by canvas ID for persistence across frames)
    private const int MaxActiveCanvases = 16;
    private static readonly CanvasState[] _canvasStates = new CanvasState[ElementIdMax + 1];
    private static readonly byte[] _activeCanvasIds = new byte[MaxActiveCanvases];
    private static int _activeCanvasCount;
    private static byte _hotCanvasId;
    private static byte _currentCanvasId;
    private static bool _mouseLeftPressed;
    private static Vector2 _mousePosition;

    private static int _elementCount;
    private static int _elementStackCount;
    private static int _popupCount;
    private static int _textBufferUsed;
    private static byte _focusId;
    private static byte _pendingFocusId;
    private static byte _focusCanvasId;
    private static byte _pendingFocusCanvasId;
    private static Vector2 _orthoSize;
    private static Vector2Int _refSize;
    private static Camera _camera = null!;
    private static byte _activeScrollId;
    private static float _lastScrollMouseY;
    private static bool _closePopups;
    private static byte _textboxFocusId;
    private static byte _textboxFocusCanvasId;
    private static bool _textboxVisible;
    private static bool _textboxRenderedThisFrame;
    public static Vector2 Size => _orthoSize;

    public static float UserScale { get; set; } = 1.0f;

    public static float GetUIScale() => Application.Platform.DisplayScale * UserScale;

    public static Vector2Int GetRefSize()
    {
        var screenSize = Application.WindowSize;
        var scale = GetUIScale();
        return new Vector2Int(
            (int)(screenSize.X / scale),
            (int)(screenSize.Y / scale)
        );
    }

    public static void Init(UIConfig? config = null)
    {
        Config = config ?? new UIConfig();
        _camera = new Camera { FlipY = true };
    }

    internal static void ResolveAssets()
    {
        if (!string.IsNullOrEmpty(Config.TextShader))
        {
            _textShader = Asset.Get<Shader>(AssetType.Shader, Config.TextShader);
            if (_textShader != null)
                TextRender.Init(_textShader);
        }

        if (!string.IsNullOrEmpty(Config.UIShader))
        {
            _uiShader = Asset.Get<Shader>(AssetType.Shader, Config.UIShader);
            if (_uiShader != null)
                UIRender.Init(_uiShader);
        }

        if (!string.IsNullOrEmpty(Config.DefaultFont))
        {
            _font = Asset.Get<Font>(AssetType.Font, Config.DefaultFont);
        }
    }

    public static void SetFont(Font font)
    {
        _font = font;
    }

    public static void Shutdown()
    {
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsAuto(float v) => v >= float.MaxValue;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsContainerType(ElementType type) =>
        type is ElementType.Container or ElementType.Column or ElementType.Row;

    private static ref Element CreateElement(ElementType type)
    {
        ref var element = ref _elements[_elementCount];
        element.Type = type;
        element.Id = 0;
        element.CanvasId = _currentCanvasId;
        element.Index = _elementCount;
        element.NextSiblingIndex = 0;
        element.ChildCount = 0;
        element.Rect = Rect.Zero;
        element.MeasuredSize = Vector2.Zero;
        element.LocalToWorld = Matrix3x2.Identity;
        element.WorldToLocal = Matrix3x2.Identity;

        if (_elementStackCount > 0)
            _elements[_elementStack[_elementStackCount - 1]].ChildCount++;

        _elementCount++;
        return ref element;
    }

    private static ref Element GetCurrentElement()
    {
        if (_elementStackCount <= 0)
            throw new InvalidOperationException("No current element");
        return ref _elements[_elementStack[_elementStackCount - 1]];
    }

    private static bool HasCurrentElement() => _elementStackCount > 0;

    private static void SetId(ref Element e, byte id)
    {
        if (id == ElementIdNone) return;
        e.Id = id;

        // Store in canvas-scoped state if we're inside a canvas
        if (e.CanvasId != ElementIdNone)
        {
            ref var canvasState = ref _canvasStates[e.CanvasId];
            if (canvasState.ElementStates != null)
                canvasState.ElementStates[id].Index = e.Index;
        }

        // Also store in global state for backward compatibility
        _elementStates[id].Index = e.Index;
    }

    private static void PushElement(int index)
    {
        if (_elementStackCount >= MaxElementStack) return;
        _elementStack[_elementStackCount++] = index;
    }

    private static void PopElement()
    {
        if (_elementStackCount == 0) return;
        _elements[_elementStack[_elementStackCount - 1]].NextSiblingIndex = _elementCount;
        _elementStackCount--;
    }

    private static void EndElement(ElementType expectedType)
    {
        if (!HasCurrentElement())
            throw new InvalidOperationException("No current element to end");
        ref var current = ref GetCurrentElement();
        if (current.Type != expectedType)
            throw new InvalidOperationException($"Expected {expectedType} but got {current.Type}");
        PopElement();
    }

    private static int AddText(ReadOnlySpan<char> text)
    {
        if (_textBufferUsed + text.Length > MaxTextBuffer)
            return -1;
        var start = _textBufferUsed;
        text.CopyTo(_textBuffer.AsSpan(_textBufferUsed));
        _textBufferUsed += text.Length;
        return start;
    }

    public static ReadOnlySpan<char> GetText(int start, int length) =>
        _textBuffer.AsSpan(start, length);

    public static bool CheckElementFlags(ElementFlags flags)
    {
        if (_elementStackCount <= 0) return false;
        ref var e = ref _elements[_elementStack[_elementStackCount - 1]];
        if (e.Id == ElementIdNone) return false;

        // Use canvas-scoped state if element belongs to a canvas
        if (e.CanvasId != ElementIdNone)
        {
            ref var cs = ref _canvasStates[e.CanvasId];
            if (cs.ElementStates != null)
                return (cs.ElementStates[e.Id].Flags & flags) == flags;
        }

        // Fallback to global state
        return (_elementStates[e.Id].Flags & flags) == flags;
    }

    public static bool IsHovered() => CheckElementFlags(ElementFlags.Hovered);
    public static bool WasPressed() => CheckElementFlags(ElementFlags.Pressed);
    public static bool IsDown() => CheckElementFlags(ElementFlags.Down);

    public static bool WasClicked()
    {
        if (!_mouseLeftPressed) return false;
        if (_elementStackCount <= 0) return false;
        ref var e = ref _elements[_elementStack[_elementStackCount - 1]];
        if (e.CanvasId != _hotCanvasId) return false;
        var localMouse = Vector2.Transform(_mousePosition, e.WorldToLocal);
        return new Rect(0, 0, e.Rect.Width, e.Rect.Height).Contains(localMouse);
    }

    public static byte GetElementId() => HasCurrentElement() ? GetCurrentElement().Id : (byte)0;

    public static Rect GetElementRect(byte id)
    {
        if (id == ElementIdNone) return Rect.Zero;

        // Check canvas-scoped state first if we're in a canvas context
        if (_currentCanvasId != ElementIdNone)
        {
            ref var cs = ref _canvasStates[_currentCanvasId];
            if (cs.ElementStates != null)
                return cs.ElementStates[id].Rect;
        }

        return _elementStates[id].Rect;
    }

    public static float GetScrollOffset(byte id, byte canvasId = ElementIdNone)
    {
        if (id == ElementIdNone) return 0;

        var effectiveCanvasId = canvasId != ElementIdNone ? canvasId : _currentCanvasId;
        if (effectiveCanvasId != ElementIdNone)
        {
            ref var cs = ref _canvasStates[effectiveCanvasId];
            if (cs.ElementStates != null)
                return cs.ElementStates[id].ScrollOffset;
        }

        return _elementStates[id].ScrollOffset;
    }

    public static void SetScrollOffset(byte id, float offset, byte canvasId = ElementIdNone)
    {
        if (id == ElementIdNone) return;

        if (canvasId != ElementIdNone)
        {
            ref var cs = ref _canvasStates[canvasId];
            if (cs.ElementStates != null)
            {
                cs.ElementStates[id].ScrollOffset = offset;
                return;
            }
        }

        _elementStates[id].ScrollOffset = offset;
    }

    public static Vector2 ScreenToUI(Vector2 screenPos) =>
        screenPos / Application.WindowSize * _orthoSize;

    public static Vector2 ScreenToElement(Vector2 screen)
    {
        if (!HasCurrentElement()) return Vector2.Zero;
        ref var e = ref GetCurrentElement();
        return Vector2.Transform(_camera.ScreenToWorld(screen), e.WorldToLocal);
    }

    public static bool HasFocus()
    {
        if (!HasCurrentElement()) return false;
        ref var e = ref GetCurrentElement();
        return _focusId != 0 && e.Id == _focusId && e.CanvasId == _focusCanvasId;
    }

    public static void SetFocus(byte elementId)
    {
        _focusId = elementId;
        _pendingFocusId = elementId;

        // Set canvas ID from current element context
        if (HasCurrentElement())
        {
            ref var e = ref GetCurrentElement();
            _focusCanvasId = e.CanvasId;
            _pendingFocusCanvasId = e.CanvasId;
        }
    }

    public static void SetFocus(byte elementId, byte canvasId)
    {
        _focusId = elementId;
        _pendingFocusId = elementId;
        _focusCanvasId = canvasId;
        _pendingFocusCanvasId = canvasId;
    }
    
    public static void ClearFocus()
    {
        UI.SetFocus(0, 0);
    }

    public static bool IsClosed() =>
        HasCurrentElement() && GetCurrentElement().Type == ElementType.Popup && _closePopups;

    /// <summary>
    /// Returns true if the current canvas is the "hot" canvas (topmost under mouse).
    /// Only the hot canvas receives press/down events.
    /// </summary>
    public static bool IsHotCanvas() => _currentCanvasId != ElementIdNone && _currentCanvasId == _hotCanvasId;

    /// <summary>
    /// Returns the ID of the hot canvas (topmost under mouse), or 0 if none.
    /// </summary>
    public static byte GetHotCanvasId() => _hotCanvasId;

    // Begin/End methods
    internal static void Begin()
    {
        _refSize = GetRefSize();
        _elementStackCount = 0;
        _elementCount = 0;
        _popupCount = 0;
        _textBufferUsed = 0;

        // Reset canvas tracking for new frame
        _activeCanvasCount = 0;
        _hotCanvasId = ElementIdNone;
        _currentCanvasId = ElementIdNone;

        var screenSize = Application.WindowSize;
        var rw = (float)_refSize.X;
        var rh = (float)_refSize.Y;
        var sw = screenSize.X;
        var sh = screenSize.Y;

        if (rw > 0 && rh > 0)
        {
            var aspectRef = rw / rh;
            var aspectScreen = sw / sh;

            if (aspectScreen >= aspectRef)
            {
                _orthoSize.Y = rh;
                _orthoSize.X = rh * aspectScreen;
            }
            else
            {
                _orthoSize.X = rw;
                _orthoSize.Y = rw / aspectScreen;
            }
        }
        else if (rw > 0)
        {
            _orthoSize.X = rw;
            _orthoSize.Y = rw * (sh / sw);
        }
        else if (rh > 0)
        {
            _orthoSize.Y = rh;
            _orthoSize.X = rh * (sw / sh);
        }
        else
        {
            _orthoSize.X = sw;
            _orthoSize.Y = sh;
        }

        _camera.SetExtents(new Rect(0, 0, _orthoSize.X, _orthoSize.Y));
        _camera.Update();

        // Apply pending focus (element ID + canvas ID)
        _focusId = _pendingFocusId;
        _focusCanvasId = _pendingFocusCanvasId;
    }

    public static void BeginCanvas(CanvasStyle style = default)
    {
        ref var e = ref CreateElement(ElementType.Canvas);
        e.Data.Canvas = style.ToData();

        // Register canvas by ID for input tracking
        if (style.Id != ElementIdNone && _activeCanvasCount < MaxActiveCanvases)
        {
            _currentCanvasId = style.Id;
            _activeCanvasIds[_activeCanvasCount++] = style.Id;
            _canvasStates[style.Id].ElementIndex = e.Index;
            _canvasStates[style.Id].ElementStates ??= new ElementState[ElementIdMax + 1];
        }

        e.CanvasId = _currentCanvasId;
        SetId(ref e, style.Id);
        PushElement(e.Index);
    }

    public static void EndCanvas()
    {
        EndElement(ElementType.Canvas);
        _currentCanvasId = ElementIdNone;
    }

    public static void BeginContainer()
    {
        ref var e = ref CreateElement(ElementType.Container);
        e.Data.Container = ContainerData.Default;
        PushElement(e.Index);
    }

    public static void BeginContainer(ContainerStyle style)
    {
        ref var e = ref CreateElement(ElementType.Container);
        e.Data.Container = style.ToData();
        SetId(ref e, style.Id);
        PushElement(e.Index);
    }

    public static void EndContainer() => EndElement(ElementType.Container);

    public static void Container()
    {
        BeginContainer();
        EndContainer();
    }

    public static void Container(ContainerStyle style)
    {
        BeginContainer(style);
        EndContainer();
    }

    public static void BeginColumn()
    {
        ref var e = ref CreateElement(ElementType.Column);
        e.Data.Container = ContainerData.Default;
        PushElement(e.Index);
    }

    public static void BeginColumn(ContainerStyle style)
    {
        ref var e = ref CreateElement(ElementType.Column);
        e.Data.Container = style.ToData();
        PushElement(e.Index);
    }

    public static void EndColumn() => EndElement(ElementType.Column);

    public static void BeginRow()
    {
        ref var e = ref CreateElement(ElementType.Row);
        e.Data.Container = ContainerData.Default;
        PushElement(e.Index);
    }

    public static void BeginRow(ContainerStyle style)
    {
        ref var e = ref CreateElement(ElementType.Row);
        e.Data.Container = style.ToData();
        PushElement(e.Index);
    }

    public static void EndRow() => EndElement(ElementType.Row);

    public static void BeginCenter()
    {
        ref var e = ref CreateElement(ElementType.Container);
        e.Data.Container = ContainerData.Default;
        e.Data.Container.Align = Align.Center;
        PushElement(e.Index);
    }

    public static void EndCenter() => EndElement(ElementType.Container);

    public static void BeginExpanded(ExpandedStyle style = default)
    {
        if (!HasCurrentElement())
            throw new InvalidOperationException("Expanded must be inside a Row or Column");
        ref var parent = ref GetCurrentElement();
        if (parent.Type != ElementType.Row && parent.Type != ElementType.Column)
            throw new InvalidOperationException("Expanded must be inside a Row or Column");

        ref var e = ref CreateElement(ElementType.Expanded);
        e.Data.Expanded.Flex = style.Flex;
        e.Data.Expanded.Axis = parent.Type == ElementType.Row ? 0 : 1;
        PushElement(e.Index);
    }

    public static void EndExpanded() => EndElement(ElementType.Expanded);

    public static void Expanded(ExpandedStyle style = default)
    {
        BeginExpanded(style);
        EndExpanded();
    }

    public static void Spacer(float size)
    {
        if (!HasCurrentElement())
            throw new InvalidOperationException("Spacer must be inside a Row or Column");
        ref var parent = ref GetCurrentElement();
        if (parent.Type != ElementType.Row && parent.Type != ElementType.Column)
            throw new InvalidOperationException("Spacer must be inside a Row or Column");

        ref var e = ref CreateElement(ElementType.Spacer);
        e.Data.Spacer.Size = parent.Type == ElementType.Row ? new Vector2(size, 0) : new Vector2(0, size);
    }

    public static void BeginBorder(BorderStyle style)
    {
        ref var e = ref CreateElement(ElementType.Container);
        e.Data.Container = ContainerData.Default;
        e.Data.Container.Border = style;
        PushElement(e.Index);
    }

    public static void EndBorder() => EndElement(ElementType.Container);

    public static void BeginTransformed(TransformStyle style)
    {
        ref var e = ref CreateElement(ElementType.Transform);
        e.Data.Transform = new TransformData
        {
            Origin = style.Origin,
            Translate = style.Translate,
            Rotate = style.Rotate,
            Scale = style.Scale
        };
        PushElement(e.Index);
    }

    public static void EndTransformed() => EndElement(ElementType.Transform);

    public static float BeginScrollable(float offset = 0, ScrollableStyle style = default)
    {
        ref var e = ref CreateElement(ElementType.Scrollable);
        e.Data.Scrollable.ContentHeight = 0;
        SetId(ref e, style.Id);

        if (style.Id != ElementIdNone)
        {
            if (_currentCanvasId != ElementIdNone)
            {
                ref var cs = ref _canvasStates[_currentCanvasId];
                if (cs.ElementStates != null)
                    e.Data.Scrollable.Offset = cs.ElementStates[style.Id].ScrollOffset;
                else
                    e.Data.Scrollable.Offset = _elementStates[style.Id].ScrollOffset;
            }
            else
            {
                e.Data.Scrollable.Offset = _elementStates[style.Id].ScrollOffset;
            }
        }
        else
        {
            e.Data.Scrollable.Offset = offset;
        }

        PushElement(e.Index);
        return e.Data.Scrollable.Offset;
    }

    public static void EndScrollable() => EndElement(ElementType.Scrollable);

    public static void BeginGrid(GridStyle style)
    {
        ref var e = ref CreateElement(ElementType.Grid);
        e.Data.Grid = new GridData
        {
            Spacing = style.Spacing,
            Columns = style.Columns,
            CellWidth = style.CellWidth,
            CellHeight = style.CellHeight,
            VirtualCount = style.VirtualCount,
            StartRow = 0
        };
        PushElement(e.Index);

        if (style.VirtualCount == 0) return;

        var containerHeight = GetFixedParentHeight();
        if (IsAuto(containerHeight)) return;

        var rowHeight = style.CellHeight + style.Spacing;
        var scrollOffset = GetScrollOffset(style.ScrollId);

        var startRow = Math.Max(0, (int)MathF.Floor(scrollOffset / rowHeight));
        var visibleRows = (int)MathF.Ceiling(containerHeight / rowHeight) + 1;
        var endRow = startRow + visibleRows;

        var startIndex = startRow * style.Columns;
        var endIndex = Math.Min(endRow * style.Columns, style.VirtualCount);

        e.Data.Grid.StartRow = startRow;

        style.VirtualRangeFunc?.Invoke(startIndex, endIndex);

        for (var virtualIndex = startIndex; virtualIndex < endIndex; virtualIndex++)
        {
            var cellIndex = virtualIndex - startIndex;
            style.VirtualCellFunc?.Invoke(cellIndex, virtualIndex);
        }
    }

    public static void EndGrid() => EndElement(ElementType.Grid);

    public static void BeginPopup(PopupStyle style)
    {
        ref var e = ref CreateElement(ElementType.Popup);
        e.Data.Popup = new PopupData
        {
            Anchor = style.Anchor,
            PopupAlign = style.PopupAlign,
            Margin = style.Margin
        };
        PushElement(e.Index);
        _popups[_popupCount++] = e.Index;
    }

    public static void EndPopup() => EndElement(ElementType.Popup);

    private static float GetFixedParentHeight()
    {
        for (var i = _elementStackCount - 1; i >= 0; i--)
        {
            ref var parent = ref _elements[_elementStack[i]];
            if (IsContainerType(parent.Type) && !IsAuto(parent.Data.Container.Height))
                return parent.Data.Container.Height;
        }

        return float.MaxValue;
    }

    // Drawing elements
    public static void Label(string text, LabelStyle style = default)
    {
        ref var e = ref CreateElement(ElementType.Label);
        var textStart = AddText(text);
        e.Data.Label = new LabelData
        {
            FontSize = style.FontSize > 0 ? style.FontSize : 16,
            Color = style.Color,
            Align = style.Align,
            TextStart = textStart,
            TextLength = text.Length
        };
    }

    public static void Image(Sprite sprite, ImageStyle style = default)
    {
        ref var e = ref CreateElement(ElementType.Image);
        e.Data.Image = new ImageData
        {
            Stretch = style.Stretch,
            Align = style.Align,
            Scale = style.Scale,
            Color = style.Color,
            Texture = nuint.Zero, // sprite.Texture,
            UV0 = sprite.UV0,
            UV1 = sprite.UV1,
            Width = sprite.Width,
            Height = sprite.Height
        };
    }

    public static void Image(Texture texture, float width, float height, ImageStyle style = default)
    {
        ref var e = ref CreateElement(ElementType.Image);
        e.Data.Image = new ImageData
        {
            Stretch = style.Stretch,
            Align = style.Align,
            Scale = style.Scale,
            Color = style.Color,
            Texture = texture.Handle,
            UV0 = Vector2.Zero,
            UV1 = Vector2.One,
            Width = width,
            Height = height
        };
    }

    public static bool TextBox(ref string text, TextBoxStyle style = default)
    {
        ref var e = ref CreateElement(ElementType.TextBox);
        var textStart = AddText(text);
        e.Data.TextBox = style.ToData();
        e.Data.TextBox.TextStart = textStart;
        e.Data.TextBox.TextLength = text.Length;
        SetId(ref e, style.Id);

        var textChanged = false;
        var isFocused = _focusId != 0 && style.Id == _focusId;

        if (isFocused && style.Id != 0)
        {
            ref var state = ref _elementStates[style.Id];
            if (state.Text != null && state.Text != text)
            {
                text = state.Text;
                textChanged = true;
            }
        }

        return textChanged;
    }

    public static string GetTextBoxText(byte id)
    {
        if (id == 0) return string.Empty;
        return _elementStates[id].Text ?? string.Empty;
    }

    internal static void End()
    {
        // Measure pass
        var measureIndex = 0;
        while (measureIndex < _elementCount)
            measureIndex = MeasureElement(measureIndex, _orthoSize);

        // Layout pass
        var layoutIndex = 0;
        while (layoutIndex < _elementCount)
            layoutIndex = LayoutElement(layoutIndex, _orthoSize);

        // Transform pass
        var transformIndex = 0;
        while (transformIndex < _elementCount)
            transformIndex = CalculateTransforms(transformIndex, Matrix3x2.Identity);

        HandleInput();

        // Flush any pending world-space rendering before drawing UI
        // SetCamera also sets u_projection uniform which UIRender will use
        Render.SetCamera(_camera);

        // Reset textbox tracking before draw pass
        _textboxRenderedThisFrame = false;

        var elementIndex = 0;
        while (elementIndex < _elementCount)
            elementIndex = DrawElement(elementIndex, false);

        for (var popupIndex = 0; popupIndex < _popupCount; popupIndex++)
        {
            var pIdx = _popups[popupIndex];
            ref var p = ref _elements[pIdx];
            for (var idx = pIdx; idx < p.NextSiblingIndex;)
                idx = DrawElement(idx, true);
        }

        // Flush any remaining UI rendering
        UIRender.Flush();

        // Hide textbox if it wasn't rendered this frame (element no longer exists)
        if (_textboxVisible && !_textboxRenderedThisFrame)
        {
            Application.Platform.HideTextbox();
            _textboxFocusId = 0;
            _textboxFocusCanvasId = 0;
            _textboxVisible = false;
        }
    }

    // Measure pass
    private static int MeasureElement(int elementIndex, Vector2 availableSize)
    {
        ref var e = ref _elements[elementIndex++];

        switch (e.Type)
        {
            case ElementType.Canvas:
                e.MeasuredSize = availableSize;
                for (var i = 0; i < e.ChildCount; i++)
                    elementIndex = MeasureElement(elementIndex, e.MeasuredSize);
                break;

            case ElementType.Container:
            case ElementType.Column:
            case ElementType.Row:
                elementIndex = MeasureContainer(ref e, elementIndex, availableSize);
                break;

            case ElementType.Expanded:
                elementIndex = MeasureExpanded(ref e, elementIndex, availableSize);
                break;

            case ElementType.Label:
                MeasureLabel(ref e);
                break;

            case ElementType.Image:
                MeasureImage(ref e);
                break;

            case ElementType.Spacer:
                e.MeasuredSize = e.Data.Spacer.Size;
                break;

            case ElementType.TextBox:
                e.MeasuredSize = new Vector2(availableSize.X, e.Data.TextBox.Height);
                break;

            case ElementType.Grid:
                elementIndex = MeasureGrid(ref e, elementIndex, availableSize);
                break;

            case ElementType.Scrollable:
                elementIndex = MeasureScrollable(ref e, elementIndex, availableSize);
                break;

            case ElementType.Transform:
                elementIndex = MeasureTransform(ref e, elementIndex, availableSize);
                break;

            case ElementType.Popup:
                elementIndex = MeasurePopup(ref e, elementIndex);
                break;
        }

        return elementIndex;
    }

    private static int MeasureContainer(ref Element e, int elementIndex, Vector2 availableSize)
    {
        ref var style = ref e.Data.Container;
        var isAutoWidth = IsAuto(style.Width);
        var isAutoHeight = IsAuto(style.Height);

        var contentSize = Vector2.Zero;
        if (isAutoWidth)
            contentSize.X = availableSize.X - style.Margin.Left - style.Margin.Right;
        else
            contentSize.X = style.Width;

        if (isAutoHeight)
            contentSize.Y = availableSize.Y - style.Margin.Top - style.Margin.Bottom;
        else
            contentSize.Y = style.Height;

        contentSize.X -= style.Padding.Left + style.Padding.Right + style.Border.Width * 2;
        contentSize.Y -= style.Padding.Top + style.Padding.Bottom + style.Border.Width * 2;

        var maxContentSize = Vector2.Zero;

        if (e.Type == ElementType.Container)
        {
            for (var i = 0; i < e.ChildCount; i++)
            {
                ref var child = ref _elements[elementIndex];
                elementIndex = MeasureElement(elementIndex, contentSize);
                maxContentSize = Vector2.Max(maxContentSize, child.MeasuredSize);
            }
        }
        else
        {
            var axis = e.Type == ElementType.Row ? 0 : 1;
            var crossAxis = 1 - axis;
            elementIndex =
                MeasureRowColumnContent(ref e, elementIndex, contentSize, axis, crossAxis, ref maxContentSize);
        }

        if (!isAutoWidth)
            e.MeasuredSize.X = style.Width;
        else
            e.MeasuredSize.X = Math.Min(availableSize.X,
                maxContentSize.X + style.Padding.Left + style.Padding.Right + style.Border.Width * 2);

        if (!isAutoHeight)
            e.MeasuredSize.Y = style.Height;
        else
            e.MeasuredSize.Y = Math.Min(availableSize.Y,
                maxContentSize.Y + style.Padding.Top + style.Padding.Bottom + style.Border.Width * 2);

        e.MeasuredSize.X = Math.Max(e.MeasuredSize.X, style.MinWidth);
        e.MeasuredSize.Y = Math.Max(e.MeasuredSize.Y, style.MinHeight);

        return elementIndex;
    }

    private static int MeasureRowColumnContent(
        ref Element e,
        int elementIndex,
        Vector2 availableSize,
        int axis,
        int crossAxis,
        ref Vector2 maxContentSize)
    {
        var childElementIndex = elementIndex;
        var flexTotal = 0f;

        for (var i = 0; i < e.ChildCount; i++)
        {
            ref var child = ref _elements[elementIndex];
            if (child.Type == ElementType.Expanded)
            {
                var childAvail = Vector2.Zero;
                SetComponent(ref childAvail, crossAxis, GetComponent(availableSize, crossAxis));
                elementIndex = MeasureElement(elementIndex, childAvail);
            }
            else
            {
                elementIndex = MeasureElement(elementIndex, availableSize);
            }

            SetComponent(ref maxContentSize, crossAxis,
                Math.Max(GetComponent(maxContentSize, crossAxis), GetComponent(child.MeasuredSize, crossAxis)));
            SetComponent(ref maxContentSize, axis,
                GetComponent(maxContentSize, axis) + GetComponent(child.MeasuredSize, axis));

            if (child.Type == ElementType.Expanded)
                flexTotal += child.Data.Expanded.Flex;
        }

        var spacing = e.ChildCount > 1 ? e.Data.Container.Spacing * (e.ChildCount - 1) : 0;

        if (flexTotal >= float.Epsilon)
        {
            var flexAvailable =
                Math.Max(0, GetComponent(availableSize, axis) - GetComponent(maxContentSize, axis)) -
                spacing;
            SetComponent(ref maxContentSize, axis,
                Math.Max(GetComponent(maxContentSize, axis), GetComponent(availableSize, axis)));

            var childAvailSize = Vector2.Zero;
            SetComponent(ref childAvailSize, crossAxis, GetComponent(maxContentSize, crossAxis));

            for (var i = 0; i < e.ChildCount; i++)
            {
                ref var child = ref _elements[childElementIndex];
                if (child.Type == ElementType.Expanded)
                {
                    SetComponent(ref childAvailSize, axis, child.Data.Expanded.Flex / flexTotal * flexAvailable);
                    MeasureElement(childElementIndex, childAvailSize);
                }

                childElementIndex = child.NextSiblingIndex;
            }
        }

        if (e.ChildCount > 1)
            SetComponent(ref maxContentSize, axis, GetComponent(maxContentSize, axis) + spacing);

        return elementIndex;
    }

    private static int MeasureExpanded(ref Element e, int elementIndex, Vector2 availableSize)
    {
        var maxContentSize = Vector2.Zero;
        for (var i = 0; i < e.ChildCount; i++)
        {
            ref var child = ref _elements[elementIndex];
            elementIndex = MeasureElement(elementIndex, availableSize);
            maxContentSize = Vector2.Max(maxContentSize, child.MeasuredSize);
        }

        e.MeasuredSize = maxContentSize;
        SetComponent(ref e.MeasuredSize, e.Data.Expanded.Axis, GetComponent(availableSize, e.Data.Expanded.Axis));
        return elementIndex;
    }

    private static void MeasureLabel(ref Element e)
    {
        var fontSize = e.Data.Label.FontSize;
        if (_font != null)
        {
            var text = GetText(e.Data.Label.TextStart, e.Data.Label.TextLength);
            e.MeasuredSize = TextRender.Measure(new string(text), _font, fontSize);
        }
        else
        {
            e.MeasuredSize = new Vector2(e.Data.Label.TextLength * fontSize * 0.6f, fontSize * 1.2f);
        }
    }

    private static void MeasureImage(ref Element e)
    {
        ref var img = ref e.Data.Image;
        e.MeasuredSize = new Vector2(img.Width, img.Height) * img.Scale;
    }

    private static int MeasureGrid(ref Element e, int elementIndex, Vector2 availableSize)
    {
        ref var grid = ref e.Data.Grid;
        var requestedWidth = grid.Columns * grid.CellWidth + grid.Spacing * (grid.Columns - 1);

        var availChildSize = new Vector2(grid.CellWidth, grid.CellHeight);

        for (var i = 0; i < e.ChildCount; i++)
            elementIndex = MeasureElement(elementIndex, availChildSize);

        var rowCount = grid.VirtualCount > 0
            ? (grid.VirtualCount + grid.Columns - 1) / grid.Columns
            : (e.ChildCount + grid.Columns - 1) / grid.Columns;

        e.MeasuredSize.X = requestedWidth;
        e.MeasuredSize.Y = rowCount * grid.CellHeight + grid.Spacing * (rowCount - 1);

        return elementIndex;
    }

    private static int MeasureScrollable(ref Element e, int elementIndex, Vector2 availableSize)
    {
        var contentHeight = 0f;
        var maxWidth = 0f;

        for (var i = 0; i < e.ChildCount; i++)
        {
            ref var child = ref _elements[elementIndex];
            elementIndex = MeasureElement(elementIndex, availableSize);
            contentHeight += child.MeasuredSize.Y;
            maxWidth = Math.Max(maxWidth, child.MeasuredSize.X);
        }

        e.Data.Scrollable.ContentHeight = contentHeight;
        e.MeasuredSize = availableSize;
        e.MeasuredSize.X = maxWidth;

        return elementIndex;
    }

    private static int MeasureTransform(ref Element e, int elementIndex, Vector2 availableSize)
    {
        var maxContentSize = Vector2.Zero;
        for (var i = 0; i < e.ChildCount; i++)
        {
            ref var child = ref _elements[elementIndex];
            elementIndex = MeasureElement(elementIndex, availableSize);
            maxContentSize = Vector2.Max(maxContentSize, child.MeasuredSize);
        }

        e.MeasuredSize = maxContentSize;
        return elementIndex;
    }

    private static int MeasurePopup(ref Element e, int elementIndex)
    {
        var maxContentSize = Vector2.Zero;
        for (var i = 0; i < e.ChildCount; i++)
        {
            ref var child = ref _elements[elementIndex];
            elementIndex = MeasureElement(elementIndex, _orthoSize);
            maxContentSize = Vector2.Max(maxContentSize, child.MeasuredSize);
        }

        e.MeasuredSize = maxContentSize;
        return elementIndex;
    }

    // Layout pass
    private static int LayoutElement(int elementIndex, Vector2 size)
    {
        ref var e = ref _elements[elementIndex++];
        e.Rect = new Rect(0, 0, e.MeasuredSize.X, e.MeasuredSize.Y);

        switch (e.Type)
        {
            case ElementType.Canvas:
                for (var i = 0; i < e.ChildCount; i++)
                    elementIndex = LayoutElement(elementIndex, e.Rect.Size);
                break;

            case ElementType.Container:
            case ElementType.Column:
            case ElementType.Row:
                elementIndex = LayoutContainer(ref e, elementIndex, size);
                break;

            case ElementType.Expanded:
                for (var i = 0; i < e.ChildCount; i++)
                    elementIndex = LayoutElement(elementIndex, size);
                break;

            case ElementType.Label:
                e.Rect.Width = size.X;
                e.Rect.Height = size.Y;
                break;

            case ElementType.Image:
                e.Rect.Width = size.X;
                e.Rect.Height = size.Y;
                break;

            case ElementType.Spacer:
                break;

            case ElementType.TextBox:
                e.Rect.Width = size.X;
                e.Rect.Height = e.Data.TextBox.Height;
                break;

            case ElementType.Grid:
                elementIndex = LayoutGrid(ref e, elementIndex, size);
                break;

            case ElementType.Scrollable:
                e.Rect.Width = size.X;
                e.Rect.Height = size.Y;
                for (var i = 0; i < e.ChildCount; i++)
                    elementIndex = LayoutElement(elementIndex, e.Rect.Size);
                break;

            case ElementType.Transform:
                e.Rect.Width = size.X;
                e.Rect.Height = size.Y;
                for (var i = 0; i < e.ChildCount; i++)
                    elementIndex = LayoutElement(elementIndex, e.Rect.Size);
                break;

            case ElementType.Popup:
                elementIndex = LayoutPopup(ref e, elementIndex, size);
                break;
        }

        return elementIndex;
    }

    private static int LayoutContainer(ref Element e, int elementIndex, Vector2 size)
    {
        ref var style = ref e.Data.Container;
        ref readonly var alignInfo = ref AlignInfoTable[(int)style.Align];
        var subtractMarginX = false;
        var subtractMarginY = false;

        if (alignInfo.HasX)
        {
            var availWidth = size.X - e.Rect.Width - style.Margin.Left - style.Margin.Right;
            e.Rect.X = availWidth * alignInfo.X;
        }
        else if (IsAuto(style.Width))
        {
            e.Rect.Width = size.X;
            subtractMarginX = true;
        }

        if (alignInfo.HasY)
        {
            var availHeight = size.Y - e.Rect.Height - style.Margin.Top - style.Margin.Bottom;
            e.Rect.Y = availHeight * alignInfo.Y;
        }
        else if (IsAuto(style.Height))
        {
            e.Rect.Height = size.Y;
            subtractMarginY = true;
        }

        var childOffset = new Vector2(
            style.Padding.Left + style.Border.Width,
            style.Padding.Top + style.Border.Width
        );

        var contentSize = e.Rect.Size;
        contentSize.X -= style.Padding.Left + style.Padding.Right + style.Border.Width * 2;
        contentSize.Y -= style.Padding.Top + style.Padding.Bottom + style.Border.Width * 2;

        if (e.Type == ElementType.Container)
        {
            for (var i = 0; i < e.ChildCount; i++)
            {
                ref var child = ref _elements[elementIndex];
                elementIndex = LayoutElement(elementIndex, contentSize);
                child.Rect.X += childOffset.X;
                child.Rect.Y += childOffset.Y;
            }
        }
        else if (e.Type == ElementType.Column)
        {
            for (var i = 0; i < e.ChildCount; i++)
            {
                ref var child = ref _elements[elementIndex];
                contentSize.Y = child.MeasuredSize.Y;
                elementIndex = LayoutElement(elementIndex, contentSize);
                child.Rect.Y += childOffset.Y;
                child.Rect.X += childOffset.X;
                childOffset.Y += child.Rect.Height + style.Spacing;
            }
        }
        else if (e.Type == ElementType.Row)
        {
            for (var i = 0; i < e.ChildCount; i++)
            {
                ref var child = ref _elements[elementIndex];
                contentSize.X = child.MeasuredSize.X;
                elementIndex = LayoutElement(elementIndex, contentSize);
                child.Rect.X += childOffset.X;
                child.Rect.Y += childOffset.Y;
                childOffset.X += child.Rect.Width + style.Spacing;
            }
        }

        e.Rect.X += style.Margin.Left;
        e.Rect.Y += style.Margin.Top;

        if (subtractMarginX)
            e.Rect.Width -= style.Margin.Left + style.Margin.Right;
        if (subtractMarginY)
            e.Rect.Height -= style.Margin.Top + style.Margin.Bottom;

        return elementIndex;
    }

    private static int LayoutGrid(ref Element e, int elementIndex, Vector2 size)
    {
        e.Rect.Width = size.X;
        e.Rect.Height = size.Y;

        ref var grid = ref e.Data.Grid;
        var startIndex = grid.StartRow * grid.Columns;

        for (var i = 0; i < e.ChildCount; i++)
        {
            var virtualIndex = startIndex + i;
            var col = virtualIndex % grid.Columns;
            var row = virtualIndex / grid.Columns;

            ref var child = ref _elements[elementIndex];
            elementIndex = LayoutElement(elementIndex, size);
            child.Rect.X = col * (grid.CellWidth + grid.Spacing);
            child.Rect.Y = row * (grid.CellHeight + grid.Spacing);
        }

        return elementIndex;
    }

    private static int LayoutPopup(ref Element e, int elementIndex, Vector2 size)
    {
        var contentSize = e.Rect.Size;
        for (var i = 0; i < e.ChildCount; i++)
            elementIndex = LayoutElement(elementIndex, contentSize);

        ref var popup = ref e.Data.Popup;
        ref readonly var anchorInfo = ref AlignInfoTable[(int)popup.Anchor];
        ref readonly var alignInfo = ref AlignInfoTable[(int)popup.PopupAlign];

        var anchorX = anchorInfo.HasX ? size.X * anchorInfo.X : 0;
        var anchorY = anchorInfo.HasY ? size.Y * anchorInfo.Y : 0;

        var popupOffsetX = alignInfo.HasX ? e.Rect.Width * alignInfo.X : 0;
        var popupOffsetY = alignInfo.HasY ? e.Rect.Height * alignInfo.Y : 0;

        e.Rect.X = anchorX - popupOffsetX + popup.Margin.Left;
        e.Rect.Y = anchorY - popupOffsetY + popup.Margin.Top;

        return elementIndex;
    }

    // Transform calculation
    private static int CalculateTransforms(int elementIndex, Matrix3x2 parentTransform)
    {
        ref var e = ref _elements[elementIndex++];

        if (e.Type == ElementType.Transform)
        {
            ref var t = ref e.Data.Transform;
            var pivot = new Vector2(e.Rect.Width * t.Origin.X, e.Rect.Height * t.Origin.Y);

            var localTransform =
                Matrix3x2.CreateTranslation(t.Translate + new Vector2(e.Rect.X, e.Rect.Y)) *
                Matrix3x2.CreateTranslation(pivot) *
                Matrix3x2.CreateRotation(t.Rotate) *
                Matrix3x2.CreateScale(t.Scale) *
                Matrix3x2.CreateTranslation(-pivot);

            e.LocalToWorld = parentTransform * localTransform;
        }
        else
        {
            e.LocalToWorld = parentTransform * Matrix3x2.CreateTranslation(e.Rect.X, e.Rect.Y);
        }

        Matrix3x2.Invert(e.LocalToWorld, out e.WorldToLocal);

        if (e.Type == ElementType.Scrollable)
        {
            var scrollTransform = e.LocalToWorld * Matrix3x2.CreateTranslation(0, -e.Data.Scrollable.Offset);
            for (var i = 0; i < e.ChildCount; i++)
                elementIndex = CalculateTransforms(elementIndex, scrollTransform);
        }
        else
        {
            for (var i = 0; i < e.ChildCount; i++)
                elementIndex = CalculateTransforms(elementIndex, e.LocalToWorld);
        }

        return elementIndex;
    }

    // Input handling
    private static void HandleInput()
    {
        var mouse = _camera.ScreenToWorld(Input.MousePosition);
        var mouseLeftPressed = Input.WasButtonPressedRaw(InputCode.MouseLeft);
        var buttonDown = Input.IsButtonDownRaw(InputCode.MouseLeft);

        _mousePosition = mouse;
        _mouseLeftPressed = mouseLeftPressed;

        // Update canvas world bounds and find hot canvas (topmost under mouse)
        _hotCanvasId = ElementIdNone;
        for (var c = 0; c < _activeCanvasCount; c++)
        {
            var canvasId = _activeCanvasIds[c];
            ref var cs = ref _canvasStates[canvasId];
            ref var canvasElement = ref _elements[cs.ElementIndex];
            var pos = Vector2.Transform(Vector2.Zero, canvasElement.LocalToWorld);
            cs.WorldBounds = new Rect(pos.X, pos.Y, canvasElement.Rect.Width, canvasElement.Rect.Height);

            // Later canvases are on top, so last one containing mouse wins
            if (cs.WorldBounds.Contains(mouse))
                _hotCanvasId = canvasId;
        }

        // Handle popup close detection
        _closePopups = false;
        if (mouseLeftPressed && _popupCount > 0)
        {
            var clickInsidePopup = false;
            for (var i = 0; i < _popupCount; i++)
            {
                ref var popup = ref _elements[_popups[i]];
                var localMouse = Vector2.Transform(mouse, popup.WorldToLocal);
                if (new Rect(0, 0, popup.Rect.Width, popup.Rect.Height).Contains(localMouse))
                {
                    clickInsidePopup = true;
                    break;
                }
            }

            if (!clickInsidePopup)
            {
                _closePopups = true;
                return;
            }
        }

        // Process each canvas independently for hover, but only hot canvas gets press/down
        for (var c = 0; c < _activeCanvasCount; c++)
        {
            var canvasId = _activeCanvasIds[c];
            var isHotCanvas = canvasId == _hotCanvasId;
            ProcessCanvasInput(canvasId, mouse, mouseLeftPressed, buttonDown, isHotCanvas);
        }

        // Handle scrollable drag
        HandleScrollableDrag(mouse, buttonDown, mouseLeftPressed);

        // Handle mouse wheel scroll
        HandleMouseWheelScroll(mouse);
    }

    private static void ProcessCanvasInput(byte canvasId, Vector2 mouse, bool mouseLeftPressed, bool buttonDown, bool isHotCanvas)
    {
        ref var cs = ref _canvasStates[canvasId];
        if (cs.ElementStates == null) return;

        var focusElementPressed = false;

        // Iterate elements belonging to this canvas in reverse order (topmost first)
        for (var idx = _elementCount; idx > 0; idx--)
        {
            ref var e = ref _elements[idx - 1];
            if (e.CanvasId != canvasId) continue;
            if (e.Id == ElementIdNone) continue;

            ref var state = ref cs.ElementStates[e.Id];
            state.Rect = e.Rect;
            var localMouse = Vector2.Transform(mouse, e.WorldToLocal);
            var mouseOver = new Rect(0, 0, e.Rect.Width, e.Rect.Height).Contains(localMouse);

            // HOVER: Independent per canvas - all canvases track hover
            if (mouseOver)
                state.Flags |= ElementFlags.Hovered;
            else
                state.Flags &= ~ElementFlags.Hovered;

            // PRESSED: Only hot canvas receives press events
            if (isHotCanvas && mouseOver && mouseLeftPressed && !focusElementPressed)
            {
                state.Flags |= ElementFlags.Pressed;
                focusElementPressed = true;
                _pendingFocusId = e.Id;
                _pendingFocusCanvasId = canvasId;
            }
            else
            {
                state.Flags &= ~ElementFlags.Pressed;
            }

            // DOWN: Only hot canvas
            if (isHotCanvas && mouseOver && buttonDown)
                state.Flags |= ElementFlags.Down;
            else
                state.Flags &= ~ElementFlags.Down;
        }
    }

    private static void HandleScrollableDrag(Vector2 mouse, bool buttonDown, bool mouseLeftPressed)
    {
        if (!buttonDown)
        {
            _activeScrollId = ElementIdNone;
        }
        else if (_activeScrollId != ElementIdNone)
        {
            var deltaY = _lastScrollMouseY - mouse.Y;
            _lastScrollMouseY = mouse.Y;

            for (var i = 0; i < _elementCount; i++)
            {
                ref var e = ref _elements[i];
                if (e.Type == ElementType.Scrollable && e.Id == _activeScrollId && e.CanvasId != ElementIdNone)
                {
                    ref var cs = ref _canvasStates[e.CanvasId];
                    if (cs.ElementStates == null) continue;
                    ref var state = ref cs.ElementStates[e.Id];

                    var newOffset = e.Data.Scrollable.Offset + deltaY;
                    var maxScroll = Math.Max(0, e.Data.Scrollable.ContentHeight - e.Rect.Height);
                    newOffset = Math.Clamp(newOffset, 0, maxScroll);

                    e.Data.Scrollable.Offset = newOffset;
                    state.ScrollOffset = newOffset;
                    break;
                }
            }
        }
        else if (mouseLeftPressed)
        {
            for (var i = _elementCount; i > 0; i--)
            {
                ref var e = ref _elements[i - 1];
                if (e.Type == ElementType.Scrollable && e.Id != ElementIdNone && e.CanvasId == _hotCanvasId)
                {
                    ref var cs = ref _canvasStates[e.CanvasId];
                    if (cs.ElementStates == null) continue;
                    ref var state = ref cs.ElementStates[e.Id];
                    if ((state.Flags & ElementFlags.Pressed) != 0)
                    {
                        _activeScrollId = e.Id;
                        _lastScrollMouseY = mouse.Y;
                        break;
                    }
                }
            }
        }
    }

    private static void HandleMouseWheelScroll(Vector2 mouse)
    {
        var scrollDelta = Input.GetAxisValue(InputCode.MouseScrollY);
        if (scrollDelta == 0) return;

        for (var i = _elementCount; i > 0; i--)
        {
            ref var e = ref _elements[i - 1];
            if (e.Type != ElementType.Scrollable || e.Id == ElementIdNone || e.CanvasId == ElementIdNone)
                continue;

            var localMouse = Vector2.Transform(mouse, e.WorldToLocal);
            if (!new Rect(0, 0, e.Rect.Width, e.Rect.Height).Contains(localMouse))
                continue;

            ref var cs = ref _canvasStates[e.CanvasId];
            if (cs.ElementStates == null) continue;
            ref var state = ref cs.ElementStates[e.Id];

            var scrollSpeed = 30f;
            var newOffset = e.Data.Scrollable.Offset - scrollDelta * scrollSpeed;
            var maxScroll = Math.Max(0, e.Data.Scrollable.ContentHeight - e.Rect.Height);
            newOffset = Math.Clamp(newOffset, 0, maxScroll);

            e.Data.Scrollable.Offset = newOffset;
            state.ScrollOffset = newOffset;
            break;
        }
    }

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
                DrawTextBox(ref e);
                break;

            case ElementType.Popup when !isPopup:
                return e.NextSiblingIndex;
        }

        var useScissor = e.Type == ElementType.Scrollable;
        if (useScissor)
        {
            // Flush pending UI draws before changing scissor state
            UIRender.Flush();

            var pos = Vector2.Transform(Vector2.Zero, e.LocalToWorld);
            var screenPos = _camera.WorldToScreen(pos);
            var scale = Application.WindowSize.Y / _orthoSize.Y;
            var screenHeight = Application.WindowSize.Y;

            // OpenGL scissor Y is from bottom, need to flip
            var scissorX = (int)screenPos.X;
            var scissorY = (int)(screenHeight - screenPos.Y - e.Rect.Height * scale);
            var scissorW = (int)(e.Rect.Width * scale);
            var scissorH = (int)(e.Rect.Height * scale);

            Render.Driver.SetScissor(scissorX, scissorY, scissorW, scissorH);
        }

        for (var i = 0; i < e.ChildCount; i++)
            elementIndex = DrawElement(elementIndex, false);

        if (useScissor)
        {
            UIRender.Flush();
            Render.Driver.DisableScissor();
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
    }

    private static void DrawLabel(ref Element e)
    {
        if (_font == null || _textShader == null)
            return;

        var text = new string(GetText(e.Data.Label.TextStart, e.Data.Label.TextLength));

        Render.PushState();
        Render.SetColor(e.Data.Label.Color);
        Render.SetTransform(e.LocalToWorld);
        TextRender.Draw(text, _font, e.Data.Label.FontSize);
        Render.PopState();
    }

    private static void DrawImage(ref Element e)
    {
        ref var img = ref e.Data.Image;
        if (img.Texture == nuint.Zero) return;

        var pos = Vector2.Transform(Vector2.Zero, e.LocalToWorld);
        Render.SetColor(img.Color);
        Render.SetTexture(img.Texture);
        Render.DrawQuad(
            pos.X, pos.Y, e.Rect.Width, e.Rect.Height,
            img.UV0.X, img.UV0.Y, img.UV1.X, img.UV1.Y
        );
    }

    private static void DrawTextBox(ref Element e)
    {
        ref var tb = ref e.Data.TextBox;
        // Focus requires matching both element ID and canvas ID
        var isFocused = e.Id != 0 && _focusId == e.Id && _focusCanvasId == e.CanvasId;
        var border = isFocused ? tb.FocusBorder : tb.Border;

        // Draw background with border
        var pos = Vector2.Transform(Vector2.Zero, e.LocalToWorld);
        UIRender.DrawRect(
            pos.X, pos.Y, e.Rect.Width, e.Rect.Height,
            tb.BackgroundColor,
            border.Radius,
            border.Width,
            border.Color
        );

        // Convert UI coordinates to screen coordinates for the native textbox
        var screenPos = _camera.WorldToScreen(pos);
        var scale = GetUIScale();
        var screenRect = new Rect(
            screenPos.X,
            screenPos.Y,
            e.Rect.Width * scale,
            e.Rect.Height * scale
        );

        var scaledFontSize = (int)(tb.FontSize * scale);

        // Mark that this textbox was rendered this frame (for cleanup detection)
        _textboxRenderedThisFrame = _textboxRenderedThisFrame || (_textboxFocusId == e.Id && _textboxFocusCanvasId == e.CanvasId);

        if (isFocused)
        {
            if (_textboxFocusId != e.Id || _textboxFocusCanvasId != e.CanvasId)
            {
                // Focus just changed to this textbox
                if (_textboxVisible)
                    Application.Platform.HideTextbox();

                var initialText = e.Data.TextBox.TextLength > 0
                    ? new string(GetText(e.Data.TextBox.TextStart, e.Data.TextBox.TextLength))
                    : string.Empty;

                Application.Platform.ShowTextbox(screenRect, initialText, new Platform.NativeTextboxStyle
                {
                    BackgroundColor = tb.BackgroundColor.ToColor32(),
                    TextColor = tb.TextColor.ToColor32(),
                    FontSize = scaledFontSize,
                    Password = tb.Password
                });
                _textboxFocusId = e.Id;
                _textboxFocusCanvasId = e.CanvasId;
                _textboxVisible = true;
                _textboxRenderedThisFrame = true;
                _elementStates[e.Id].Text = initialText;
            }
            else
            {
                // Update rect position
                Application.Platform.UpdateTextboxRect(screenRect, scaledFontSize);

                // Get updated text from native textbox
                var currentText = _elementStates[e.Id].Text ?? string.Empty;
                if (Application.Platform.UpdateTextboxText(ref currentText))
                    _elementStates[e.Id].Text = currentText;
            }
        }
        else if (_textboxFocusId == e.Id && _textboxFocusCanvasId == e.CanvasId)
        {
            // Lost focus - hide textbox
            Application.Platform.HideTextbox();
            _textboxFocusId = 0;
            _textboxFocusCanvasId = 0;
            _textboxVisible = false;
        }
    }

    // Utility methods
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float GetComponent(Vector2 v, int axis) => axis == 0 ? v.X : v.Y;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void SetComponent(ref Vector2 v, int axis, float value)
    {
        if (axis == 0) v.X = value;
        else v.Y = value;
    }
}
