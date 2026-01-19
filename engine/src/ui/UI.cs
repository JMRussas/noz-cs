//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

#define NOZ_UI_DEBUG

using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace NoZ.Engine.UI;

public enum ImageStretch : byte
{
    None,
    Fill,
    Uniform
}

public static partial class UI
{
    private const int MaxElements = 4096;
    private const int MaxElementStack = 128;
    private const int MaxPopups = 4;
    private const int MaxTextBuffer = 64 * 1024;
    private const byte ElementIdNone = 0;
    private const byte ElementIdMax = 255;
    private static Font? _defaultFont;

    public static Font? DefaultFont => _defaultFont;
    public static Config Config { get; private set; } = new();

    public struct AutoCanvas : IDisposable { readonly void IDisposable.Dispose() => EndCanvas(); }
    public struct AutoContainer : IDisposable { readonly void IDisposable.Dispose() => EndContainer(); }
    public struct AutoColumn : IDisposable { readonly void IDisposable.Dispose() => EndColumn(); }
    public struct AutoRow : IDisposable { readonly void IDisposable.Dispose() => EndRow(); }
    public struct AutoScrollable: IDisposable { readonly void IDisposable.Dispose() => EndScrollable(); }
    public struct AutoFlex : IDisposable { readonly void IDisposable.Dispose() => EndFlex(); }

    // Element storage
    private static readonly Element[] _elements = new Element[MaxElements];
    private static readonly int[] _elementStack = new int[MaxElementStack];
    private static readonly int[] _popups = new int[MaxPopups];
    internal static readonly ElementState[] _elementStates = new ElementState[ElementIdMax + 1];
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
    private static Vector2 _size;
    private static Vector2Int _refSize;
    private static byte _activeScrollId;
    private static float _lastScrollMouseY;
    private static bool _closePopups;
    private static bool _elementWasPressed;
    public static Vector2 ScreenSize => _size;

    public static float UserScale { get; set; } = 1.0f;

    public static float GetUIScale() => Application.Platform.DisplayScale * UserScale;

    public static Camera? Camera { get; private set; } = null!;

    public static Vector2Int GetRefSize()
    {
        var screenSize = Application.WindowSize;
        var scale = GetUIScale();
        return new Vector2Int(
            (int)(screenSize.X / scale),
            (int)(screenSize.Y / scale)
        );
    }

    public static void Init(Config? config = null)
    {
        Config = config ?? new Config();
        Camera = new Camera { FlipY = true };

        UIRender.Init(Config);

        _defaultFont = Asset.Get<Font>(AssetType.Font, Config.DefaultFont);
    }

    public static void Shutdown()
    {
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsAuto(float v) => v <= 0;

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
        element.ParentIndex = _elementStackCount > 0 ? _elementStack[_elementStackCount - 1] : -1;
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

    private static int GetDepth(ref readonly Element e)
    {
        int depth = 0;
        var parentIndex = e.ParentIndex;
        while (parentIndex != -1)
        {
            depth++;
            parentIndex = _elements[parentIndex].ParentIndex;
        }
        return depth;
    }

    private static ref Element GetParent() => 
        ref _elements[_elementStackCount - 2];

    private static ref Element GetParent(ref readonly Element e) =>
        ref GetElement(e.ParentIndex);

    private static ref Element GetSelf() =>
        ref _elements[_elementStack[_elementStackCount - 1]];

    private static ref Element GetElement(int index) =>
        ref _elements[index];

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
        ref var current = ref GetSelf();
        if (current.Type != expectedType)
            throw new InvalidOperationException($"Expected {expectedType} but got {current.Type}");
        PopElement();
    }

    internal static int AddText(ReadOnlySpan<char> text)
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

    internal static bool CheckElementFlags(ElementFlags flags)
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

#if false
    public static bool WasClicked()
    {
        ref readonly e = ref GetCurrentElement();

        if (!_mouseLeftPressed) return false;
        if (_elementStackCount <= 0) return false;
        ref var e = ref _elements[_elementStack[_elementStackCount - 1]];
        if (e.CanvasId != _hotCanvasId) return false;
        var localMouse = Vector2.Transform(_mousePosition, e.WorldToLocal);
        return new Rect(0, 0, e.Rect.Width, e.Rect.Height).Contains(localMouse);
    }
#endif

    public static byte GetElementId() => HasCurrentElement() ? GetSelf().Id : (byte)0;

    public static Rect GetElementRect(byte id, byte canvasId = ElementIdNone)
    {
        if (id == ElementIdNone) return Rect.Zero;

        var effectiveCanvasId = canvasId != ElementIdNone ? canvasId : _currentCanvasId;
        if (effectiveCanvasId != ElementIdNone)
        {
            ref var cs = ref _canvasStates[effectiveCanvasId];
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
        screenPos / Application.WindowSize * _size;

    public static Vector2 ScreenToElement(Vector2 screen)
    {
        if (!HasCurrentElement()) return Vector2.Zero;
        ref var e = ref GetSelf();
        return Vector2.Transform(Camera!.ScreenToWorld(screen), e.WorldToLocal);
    }

    public static bool HasFocus()
    {
        if (!HasCurrentElement()) return false;
        ref var e = ref GetSelf();
        return _focusId != 0 && e.Id == _focusId && e.CanvasId == _focusCanvasId;
    }

    public static void SetFocus(byte elementId)
    {
        _focusId = elementId;
        _pendingFocusId = elementId;

        // Set canvas ID from current element context
        if (HasCurrentElement())
        {
            ref var e = ref GetSelf();
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
        HasCurrentElement() && GetSelf().Type == ElementType.Popup && _closePopups;

    public static bool IsFocus(byte id, byte canvasId) => _focusId != 0 && _focusId == id && _focusCanvasId == canvasId;
    public static string? GetElementText(byte id) => id != 0 ? _elementStates[id].Text : null;
    public static void SetElementText(byte id, string text)
    {
        if (id != 0) _elementStates[id].Text = text;
    }

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
                _size.Y = rh;
                _size.X = rh * aspectScreen;
            }
            else
            {
                _size.X = rw;
                _size.Y = rw / aspectScreen;
            }
        }
        else if (rw > 0)
        {
            _size.X = rw;
            _size.Y = rw * (sh / sw);
        }
        else if (rh > 0)
        {
            _size.Y = rh;
            _size.X = rh * (sw / sh);
        }
        else
        {
            _size.X = sw;
            _size.Y = sh;
        }

        Camera!.SetExtents(new Rect(0, 0, _size.X, _size.Y));
        Camera!.Update();

        // Apply pending focus (element ID + canvas ID)
        _focusId = _pendingFocusId;
        _focusCanvasId = _pendingFocusCanvasId;
    }

    public static AutoCanvas BeginCanvas(CanvasStyle style = default, byte id = 0)
    {
        ref var e = ref CreateElement(ElementType.Canvas);
        e.Data.Canvas = style.ToData();

        if (id != ElementIdNone && _activeCanvasCount < MaxActiveCanvases)
        {
            _currentCanvasId = id;
            _activeCanvasIds[_activeCanvasCount++] = id;
            _canvasStates[id].ElementIndex = e.Index;
            _canvasStates[id].ElementStates ??= new ElementState[ElementIdMax + 1];
        }

        e.CanvasId = _currentCanvasId;
        SetId(ref e, id);
        PushElement(e.Index);
        return new AutoCanvas();
    }

    public static void EndCanvas()
    {
        EndElement(ElementType.Canvas);
        _currentCanvasId = ElementIdNone;
    }

    public static AutoContainer BeginContainer(byte id=0)
    {
        ref var e = ref CreateElement(ElementType.Container);
        e.Data.Container = ContainerData.Default;
        SetId(ref e, id);
        PushElement(e.Index);
        return new AutoContainer();
    }

    public static AutoContainer BeginContainer(in ContainerStyle style, byte id=0)
    {
        ref var e = ref CreateElement(ElementType.Container);
        e.Data.Container = style.ToData();
        SetId(ref e, id);
        PushElement(e.Index);
        return new AutoContainer();
    }

    public static void EndContainer() => EndElement(ElementType.Container);

    public static void Container(byte id=0)
    {
        BeginContainer(id:id);
        EndContainer();
    }

    public static void Container(ContainerStyle style, byte id=0)
    {
        BeginContainer(style, id:id);
        EndContainer();
    }

    public static AutoColumn BeginColumn()
    {
        ref var e = ref CreateElement(ElementType.Column);
        e.Data.Container = ContainerData.Default;
        PushElement(e.Index);
        return new AutoColumn();
    }

    public static AutoColumn BeginColumn(ContainerStyle style)
    {
        ref var e = ref CreateElement(ElementType.Column);
        e.Data.Container = style.ToData();
        PushElement(e.Index);
        return new AutoColumn();
    }

    public static void EndColumn() => EndElement(ElementType.Column);

    public static AutoRow BeginRow()
    {
        ref var e = ref CreateElement(ElementType.Row);
        e.Data.Container = ContainerData.Default;
        PushElement(e.Index);
        return new AutoRow();
    }

    public static AutoRow BeginRow(ContainerStyle style)
    {
        ref var e = ref CreateElement(ElementType.Row);
        e.Data.Container = style.ToData();
        PushElement(e.Index);
        return new AutoRow();
    }

    public static void EndRow() => EndElement(ElementType.Row);

    public static void BeginCenter()
    {
        ref var e = ref CreateElement(ElementType.Container);
        e.Data.Container = ContainerData.Default;
        e.Data.Container.AlignX = Align.Center;
        e.Data.Container.AlignY = Align.Center;
        PushElement(e.Index);
    }

    public static void EndCenter() => EndElement(ElementType.Container);

    public static AutoFlex BeginFlex() => BeginFlex(1.0f);
    public static AutoFlex BeginFlex(float flex)
    {
        if (!HasCurrentElement())
            throw new InvalidOperationException("Flex must be inside a Row or Column");
        ref var parent = ref GetSelf();
        if (parent.Type != ElementType.Row && parent.Type != ElementType.Column)
            throw new InvalidOperationException("Flex must be inside a Row or Column");

        ref var e = ref CreateElement(ElementType.Flex);
        e.Data.Flex.Flex = flex;
        e.Data.Flex.Axis = parent.Type == ElementType.Row ? 0 : 1;
        PushElement(e.Index);
        return new AutoFlex();
    }

    public static void EndFlex() => EndElement(ElementType.Flex);

    public static void Flex() => Flex(1.0f);
    public static void Flex(float flex)
    {
        BeginFlex(flex);
        EndFlex();
    }

    public static void Spacer(float size)
    {
        if (!HasCurrentElement())
            throw new InvalidOperationException("Spacer must be inside a Row or Column");
        ref var parent = ref GetSelf();
        if (parent.Type != ElementType.Row && parent.Type != ElementType.Column)
            throw new InvalidOperationException("Spacer must be inside a Row or Column");

        ref var e = ref CreateElement(ElementType.Spacer);
        e.Data.Spacer.Size = parent.Type == ElementType.Row ? new Vector2(size, 0) : new Vector2(0, size);

        PushElement(e.Index);
        PopElement();
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

    public static AutoScrollable BeginScrollable(float offset = 0, byte id = 0, ScrollableStyle style = default)
    {
        ref var e = ref CreateElement(ElementType.Scrollable);
        e.Data.Scrollable.ContentHeight = 0;
        SetId(ref e, id);

        if (id != ElementIdNone)
        {
            if (_currentCanvasId != ElementIdNone)
            {
                ref var cs = ref _canvasStates[_currentCanvasId];
                if (cs.ElementStates != null)
                    e.Data.Scrollable.Offset = cs.ElementStates[id].ScrollOffset;
                else
                    e.Data.Scrollable.Offset = _elementStates[id].ScrollOffset;
            }
            else
            {
                e.Data.Scrollable.Offset = _elementStates[id].ScrollOffset;
            }
        }
        else
        {
            e.Data.Scrollable.Offset = offset;
        }

        PushElement(e.Index);
        return new AutoScrollable();
    }

    public static void EndScrollable() => EndElement(ElementType.Scrollable);

    public static void BeginGrid(GridStyle style)
    {
#if false
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
#endif
    }

    public static void EndGrid() => EndElement(ElementType.Grid);

    public static void BeginPopup(PopupStyle style)
    {
        ref var e = ref CreateElement(ElementType.Popup);
        e.Data.Popup = new PopupData
        {
            AnchorX = style.AnchorX,
            AnchorY = style.AnchorY,
            PopupAlignX = style.PopupAlignX,
            PopupAlignY = style.PopupAlignY,
            Margin = style.Margin
        };
        PushElement(e.Index);
        _popups[_popupCount++] = e.Index;
    }

    public static void EndPopup() => EndElement(ElementType.Popup);

    public static void Label(string text, LabelStyle style = default)
    {
        ref var e = ref CreateElement(ElementType.Label);
        var textStart = AddText(text);
        e.Font = style.Font ?? _defaultFont;
        e.Data.Label = new LabelData
        {
            FontSize = style.FontSize > 0 ? style.FontSize : 16,
            Color = style.Color,
            AlignX = style.AlignX,
            AlignY = style.AlignY,
            TextStart = textStart,
            TextLength = text.Length
        };

        PushElement(e.Index);
        PopElement();
    }

    public static void Image(Sprite sprite, ImageStyle style = default)
    {
        ref var e = ref CreateElement(ElementType.Image);
        e.Data.Image = new ImageData
        {
            Stretch = style.Stretch,
            AlignX = style.AlignX,
            AlignY = style.AlignY,
            Scale = style.Scale,
            Color = style.Color,
            Texture = nuint.Zero, // sprite.Texture,
            UV0 = sprite.UV0,
            UV1 = sprite.UV1,
            Width = sprite.Width,
            Height = sprite.Height
        };

        PushElement(e.Index);
        PopElement();
    }

    public static void Image(Texture texture, float width, float height, ImageStyle style = default)
    {
        ref var e = ref CreateElement(ElementType.Image);
        e.Data.Image = new ImageData
        {
            Stretch = style.Stretch,
            AlignX = style.AlignX,
            AlignY = style.AlignY,
            Scale = style.Scale,
            Color = style.Color,
            Texture = texture.Handle,
            UV0 = Vector2.Zero,
            UV1 = Vector2.One,
            Width = width,
            Height = height
        };

        PushElement(e.Index);
        PopElement();
    }

    public static bool TextBox(ref string text, TextBoxStyle style = default, byte id = 0, string? placeholder = null)
    {
        ref var e = ref CreateElement(ElementType.TextBox);
        var textStart = AddText(text);
        e.Data.TextBox = style.ToData();
        e.Data.TextBox.TextStart = textStart;
        e.Data.TextBox.TextLength = text.Length;

        if (!string.IsNullOrEmpty(placeholder))
        {
            e.Data.TextBox.PlaceholderStart = AddText(placeholder);
            e.Data.TextBox.PlaceholderLength = placeholder.Length;
        }

        SetId(ref e, id);

        var textChanged = false;
        var isFocused = _focusId != 0 && id == _focusId;

        if (isFocused && id != 0)
        {
            ref var state = ref _elementStates[id];
            if (state.Text != null && state.Text != text)
            {
                text = state.Text;
                textChanged = true;
            }
        }

        PushElement(e.Index);
        PopElement();

        return textChanged;
    }

    public static string GetTextBoxText(byte id)
    {
        if (id == 0) return string.Empty;
        return _elementStates[id].Text ?? string.Empty;
    }

    internal static void End()
    {
        LogUI("Layout", condition: () => _elementCount > 0);
        for (int elementIndex=0; elementIndex < _elementCount;) 
            elementIndex = LayoutCanvas(elementIndex);

        Render.SetCamera(Camera);

        LogUI("Draw", condition: () => _elementCount > 0);
        for (int elementIndex = 0; elementIndex < _elementCount;)
            elementIndex = DrawElement(elementIndex, false);

#if false
        // Pass 1: Measure (bottom-up, intrinsic sizes)
        var measureIndex = 0;
        while (measureIndex < _elementCount)
            measureIndex = MeasureElement(measureIndex, _size);

        // Pass 2: Allocate (top-down, apply Fill/Flex/constraints)
        var allocateIndex = 0;
        while (allocateIndex < _elementCount)
            allocateIndex = AllocateElement(allocateIndex, _size);

        // Pass 3: Layout (top-down, positioning only)
        var layoutIndex = 0;
        while (layoutIndex < _elementCount)
            layoutIndex = LayoutElement(layoutIndex);

        // Transform pass
        var transformIndex = 0;
        while (transformIndex < _elementCount)
            transformIndex = CalculateTransforms(transformIndex, Matrix3x2.Identity);

        HandleInput();

        // Flush any pending world-space rendering before drawing UI
        // SetCamera also sets u_projection uniform which UIRender will use
        Render.SetCamera(Camera);

        // Reset textbox tracking before draw pass
        // _textboxRenderedThisFrame = false;

        for (int elementIndex = 0; elementIndex < _elementCount; )
            elementIndex = DrawElement(elementIndex, false);

        for (var popupIndex = 0; popupIndex < _popupCount; popupIndex++)
        {
            var pIdx = _popups[popupIndex];
            ref var p = ref _elements[pIdx];
            for (var idx = pIdx; idx < p.NextSiblingIndex;)
                idx = DrawElement(idx, true);
        }

        TextBoxElement.EndFrame();
#endif

    }


#if false
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

            case ElementType.Flex:
                elementIndex = MeasureFlex(ref e, elementIndex, availableSize);
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
                TextBoxElement.Measure(ref e, availableSize);
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
#endif

#if false
    private static int MeasureContainer(ref Element e, int elementIndex, Vector2 availableSize)
    {
        ref var style = ref e.Data.Container;
        var isAutoWidth = IsAuto(style.Width);
        var isAutoHeight = IsAuto(style.Height);

        LogUI($"MeasureContainer: Type={e.Type} Id={e.Id} Available=({availableSize.X:F1},{availableSize.Y:F1}) AutoW={isAutoWidth} AutoH={isAutoHeight} AlignX={style.AlignX} AlignY={style.AlignY}");

        var contentSize = Vector2.Zero;
        if (isAutoWidth)
            contentSize.X = availableSize.X - style.Margin.L - style.Margin.R;
        else
            contentSize.X = style.Width;

        if (isAutoHeight)
            contentSize.Y = availableSize.Y - style.Margin.T - style.Margin.B;
        else
            contentSize.Y = style.Height;

        contentSize.X -= style.Padding.L + style.Padding.R + style.Border.Width * 2;
        contentSize.Y -= style.Padding.T + style.Padding.B + style.Border.Width * 2;

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

        // Measure = intrinsic size (fit-to-content)
        // Preferred size (Width/Height) is applied in Allocate pass
        e.MeasuredSize.X = maxContentSize.X + style.Padding.L + style.Padding.R + style.Border.Width * 2 + style.Margin.L + style.Margin.R;
        e.MeasuredSize.Y = maxContentSize.Y + style.Padding.T + style.Padding.B + style.Border.Width * 2 + style.Margin.T + style.Margin.B;

        LogUI($"  -> Measured: Type={e.Type} Id={e.Id} Size=({e.MeasuredSize.X:F1},{e.MeasuredSize.Y:F1}) MaxContent=({maxContentSize.X:F1},{maxContentSize.Y:F1})");

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
        for (var i = 0; i < e.ChildCount; i++)
        {
            ref var child = ref _elements[elementIndex];
            // All children get full available size during measure
            // Flex distribution happens during allocation
            elementIndex = MeasureElement(elementIndex, availableSize);

            SetComponent(ref maxContentSize, crossAxis,
                Math.Max(GetComponent(maxContentSize, crossAxis), GetComponent(child.MeasuredSize, crossAxis)));
            SetComponent(ref maxContentSize, axis,
                GetComponent(maxContentSize, axis) + GetComponent(child.MeasuredSize, axis));
        }

        if (e.ChildCount > 1)
        {
            var spacing = e.Data.Container.Spacing * (e.ChildCount - 1);
            SetComponent(ref maxContentSize, axis, GetComponent(maxContentSize, axis) + spacing);
        }

        return elementIndex;
    }

    private static int MeasureFlex(ref Element e, int elementIndex, Vector2 availableSize)
    {
        var maxContentSize = Vector2.Zero;
        for (var i = 0; i < e.ChildCount; i++)
        {
            ref var child = ref _elements[elementIndex];
            elementIndex = MeasureElement(elementIndex, availableSize);
            maxContentSize = Vector2.Max(maxContentSize, child.MeasuredSize);
        }

        // Report full content size during measure so parent can apply min/max constraints
        // The parent Row/Column will distribute flex space during allocation
        e.MeasuredSize = maxContentSize;
        return elementIndex;
    }

    private static void MeasureLabel(ref Element e)
    {
        var fontSize = e.Data.Label.FontSize;
        var font = e.Font ?? _defaultFont!;
        var text = GetText(e.Data.Label.TextStart, e.Data.Label.TextLength);
        e.MeasuredSize = TextRender.Measure(new string(text), font, fontSize);
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

        // Give children unlimited height so they can expand to their natural size
        var childAvailableSize = new Vector2(availableSize.X, float.MaxValue);

        for (var i = 0; i < e.ChildCount; i++)
        {
            ref var child = ref _elements[elementIndex];
            elementIndex = MeasureElement(elementIndex, childAvailableSize);
            contentHeight += child.MeasuredSize.Y;
            maxWidth = Math.Max(maxWidth, child.MeasuredSize.X);
        }

        e.Data.Scrollable.ContentHeight = contentHeight;
        e.MeasuredSize.X = maxWidth;
        // Report content height as measured size so parent can shrink-wrap or constrain with MaxHeight
        e.MeasuredSize.Y = Math.Min(contentHeight, availableSize.Y);

        return elementIndex;
    }

    private static void FinalizeScrollable(ref Element e)
    {
        // Called after layout when final rect size is known
        var maxScroll = Math.Max(0, e.Data.Scrollable.ContentHeight - e.Rect.Height);
        e.Data.Scrollable.Offset = Math.Clamp(e.Data.Scrollable.Offset, 0, maxScroll);

        if (e.Id != ElementIdNone && e.CanvasId != ElementIdNone)
        {
            ref var cs = ref _canvasStates[e.CanvasId];
            if (cs.ElementStates != null)
                cs.ElementStates[e.Id].ScrollOffset = e.Data.Scrollable.Offset;
        }
        else if (e.Id != ElementIdNone)
        {
            _elementStates[e.Id].ScrollOffset = e.Data.Scrollable.Offset;
        }
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
            elementIndex = MeasureElement(elementIndex, _size);
            maxContentSize = Vector2.Max(maxContentSize, child.MeasuredSize);
        }

        e.MeasuredSize = maxContentSize;
        return elementIndex;
    }

    private static int AllocateElement(int elementIndex, Vector2 availableSize)
    {
        ref var e = ref _elements[elementIndex++];

        e.AllocatedSize = e.MeasuredSize;

        switch (e.Type)
        {
            case ElementType.Canvas:
                e.AllocatedSize = availableSize;
                for (var i = 0; i < e.ChildCount; i++)
                    elementIndex = AllocateElement(elementIndex, availableSize);
                break;

            case ElementType.Container:
            case ElementType.Row:
            case ElementType.Column:
                elementIndex = AllocateContainer(ref e, elementIndex, availableSize);
                break;

            case ElementType.Flex:
                elementIndex = AllocateFlex(ref e, elementIndex, availableSize);
                break;

            case ElementType.Scrollable:
                elementIndex = AllocateScrollable(ref e, elementIndex, availableSize);
                break;

            case ElementType.Grid:
                elementIndex = AllocateGrid(ref e, elementIndex);
                break;

            case ElementType.Transform:
                elementIndex = AllocateTransform(ref e, elementIndex, availableSize);
                break;

            case ElementType.Popup:
                elementIndex = AllocatePopup(ref e, elementIndex);
                break;

            case ElementType.TextBox:
                // TextBox fills available width, uses fixed height
                e.AllocatedSize = new Vector2(availableSize.X, e.Data.TextBox.Height);
                break;

            default:
                // Labels, Images, Spacers, etc. just use measured size
                break;
        }

        return elementIndex;
    }

    private static int AllocateContainer(ref Element e, int elementIndex, Vector2 availableSize)
    {
#if false
        ref var style = ref e.Data.Container;

        // Sizing formula: size = max(content, min(preferred, available))
        // If preferred <= 0: fit to content (measured size)
        // If preferred > 0: try preferred, but shrink if available is smaller, never smaller than content
        // Fill overrides preferred and uses available space

        if (style.AlignX == Align.Fill)
            e.AllocatedSize.X = availableSize.X;
        else if (style.Width <= 0)
            e.AllocatedSize.X = e.MeasuredSize.X;
        else
            e.AllocatedSize.X = Math.Max(e.MeasuredSize.X, Math.Min(style.Width + style.Margin.L + style.Margin.R, availableSize.X));

        if (style.AlignY == Align.Fill)
            e.AllocatedSize.Y = availableSize.Y;
        else if (style.Height <= 0)
            e.AllocatedSize.Y = e.MeasuredSize.Y;
        else
            e.AllocatedSize.Y = Math.Max(e.MeasuredSize.Y, Math.Min(style.Height + style.Margin.T + style.Margin.B, availableSize.Y));

        // Apply min/max constraints
        e.AllocatedSize.X = Math.Clamp(e.AllocatedSize.X, style.MinWidth, style.MaxWidth);
        e.AllocatedSize.Y = Math.Clamp(e.AllocatedSize.Y, style.MinHeight, style.MaxHeight);

        // Calculate content area for children
        var contentSize = new Vector2(
            e.AllocatedSize.X - style.Margin.L - style.Margin.R - style.Padding.L - style.Padding.R - style.Border.Width * 2,
            e.AllocatedSize.Y - style.Margin.T - style.Margin.B - style.Padding.T - style.Padding.B - style.Border.Width * 2
        );

        if (e.Type == ElementType.Container)
        {
            // Plain container: allocate all children with full content size
            for (var i = 0; i < e.ChildCount; i++)
                elementIndex = AllocateElement(elementIndex, contentSize);
        }
        else
        {
            // Row/Column: distribute flex space
            var axis = e.Type == ElementType.Row ? 0 : 1;
            elementIndex = AllocateRowColumnChildren(ref e, elementIndex, contentSize, axis);
        }

        return elementIndex;
#else
        return 0;
#endif
    }

    private static int AllocateRowColumnChildren(ref Element e, int elementIndex, Vector2 contentSize, int axis)
    {
        ref var style = ref e.Data.Container;

        // Calculate fixed children size and flex total
        // For non-flex children, estimate their allocated size (considering preferred size)
        var fixedSize = e.ChildCount > 1 ? style.Spacing * (e.ChildCount - 1) : 0f;
        var flexTotal = 0f;
        var childIndex = elementIndex;

        for (var i = 0; i < e.ChildCount; i++)
        {
            ref var child = ref _elements[childIndex];
            if (child.Type == ElementType.Flex)
            {
                flexTotal += child.Data.Flex.Flex;
            }
            else
            {
                // Estimate what this child will allocate to on the main axis
                var childSize = GetComponent(child.MeasuredSize, axis);
                if (IsContainerType(child.Type))
                {
                    ref var childStyle = ref child.Data.Container;
                    var preferred = axis == 0 ? childStyle.Width : childStyle.Height;
                    var margins = axis == 0
                        ? childStyle.Margin.L + childStyle.Margin.R
                        : childStyle.Margin.T + childStyle.Margin.B;
                    var minSize = axis == 0 ? childStyle.MinWidth : childStyle.MinHeight;

                    // If preferred > 0, child will try to be that size
                    if (preferred > 0)
                        childSize = Math.Max(childSize, preferred + margins);

                    // Apply min constraint
                    childSize = Math.Max(childSize, minSize);
                }
                fixedSize += childSize;
            }
            childIndex = child.NextSiblingIndex;
        }

        // Calculate flex space
        var flexSpace = Math.Max(0, GetComponent(contentSize, axis) - fixedSize);

        // Allocate each child
        for (var i = 0; i < e.ChildCount; i++)
        {
            ref var child = ref _elements[elementIndex];
            var childAvail = contentSize;

            if (child.Type == ElementType.Flex && flexTotal > 0)
            {
                // Flex child gets proportional share of flex space
                var flexShare = child.Data.Flex.Flex / flexTotal * flexSpace;
                SetComponent(ref childAvail, axis, flexShare);
            }
            // Non-flex children get full content size so they can apply
            // their own preferred size / min / max constraints

            elementIndex = AllocateElement(elementIndex, childAvail);
        }

        return elementIndex;
    }

    private static int AllocateFlex(ref Element e, int elementIndex, Vector2 availableSize)
    {
        // Flex element fills the available space given by parent
        e.AllocatedSize = availableSize;

        LogUI($"AllocateFlex: Available=({availableSize.X:F1},{availableSize.Y:F1})");

        // Allocate children with the full space
        for (var i = 0; i < e.ChildCount; i++)
            elementIndex = AllocateElement(elementIndex, availableSize);

        return elementIndex;
    }

    private static int AllocateScrollable(ref Element e, int elementIndex, Vector2 availableSize)
    {
        // Scrollable takes available space
        e.AllocatedSize = availableSize;

        LogUI($"AllocateScrollable: Id={e.Id} Available=({availableSize.X:F1},{availableSize.Y:F1}) ContentHeight={e.Data.Scrollable.ContentHeight:F1}");

        // Children get the scrollable width and unlimited height for scrolling
        var childAvail = new Vector2(availableSize.X, float.MaxValue);

        for (var i = 0; i < e.ChildCount; i++)
            elementIndex = AllocateElement(elementIndex, childAvail);

        return elementIndex;
    }

    private static int AllocateGrid(ref Element e, int elementIndex)
    {
        ref var grid = ref e.Data.Grid;
        e.AllocatedSize = e.MeasuredSize;

        var cellSize = new Vector2(grid.CellWidth, grid.CellHeight);
        for (var i = 0; i < e.ChildCount; i++)
            elementIndex = AllocateElement(elementIndex, cellSize);

        return elementIndex;
    }

    private static int AllocateTransform(ref Element e, int elementIndex, Vector2 availableSize)
    {
        e.AllocatedSize = e.MeasuredSize;

        for (var i = 0; i < e.ChildCount; i++)
            elementIndex = AllocateElement(elementIndex, availableSize);

        return elementIndex;
    }

    private static int AllocatePopup(ref Element e, int elementIndex)
    {
        e.AllocatedSize = e.MeasuredSize;

        for (var i = 0; i < e.ChildCount; i++)
            elementIndex = AllocateElement(elementIndex, _size);

        return elementIndex;
    }
#endif

#if false
    // Layout pass - positioning only (uses AllocatedSize from Allocate pass)
    private static int LayoutElement(int elementIndex)
    {
        ref var e = ref _elements[elementIndex++];

        // Use AllocatedSize for rect dimensions
        e.Rect = new Rect(0, 0, e.AllocatedSize.X, e.AllocatedSize.Y);

        switch (e.Type)
        {
            case ElementType.Canvas:
                for (var i = 0; i < e.ChildCount; i++)
                {
                    ref var child = ref _elements[elementIndex];
                    elementIndex = LayoutElement(elementIndex);
                    PositionChild(ref child, e.Rect.Size);
                }
                break;

            case ElementType.Container:
            case ElementType.Column:
            case ElementType.Row:
                elementIndex = LayoutContainer(ref e, elementIndex);
                break;

            case ElementType.Flex:
                e.Rect.Width = e.AllocatedSize.X;
                e.Rect.Height = e.AllocatedSize.Y;
                for (var i = 0; i < e.ChildCount; i++)
                    elementIndex = LayoutElement(elementIndex);
                break;

            case ElementType.Label:
            case ElementType.Image:
            case ElementType.Spacer:
                // Already set from AllocatedSize
                break;

            case ElementType.TextBox:
                TextBoxElement.Layout(ref e, e.AllocatedSize);
                break;

            case ElementType.Grid:
                elementIndex = LayoutGrid(ref e, elementIndex);
                break;

            case ElementType.Scrollable:
                FinalizeScrollable(ref e);
                for (var i = 0; i < e.ChildCount; i++)
                    elementIndex = LayoutElement(elementIndex);
                break;

            case ElementType.Transform:
                for (var i = 0; i < e.ChildCount; i++)
                    elementIndex = LayoutElement(elementIndex);
                break;

            case ElementType.Popup:
                elementIndex = LayoutPopup(ref e, elementIndex);
                break;
        }

        return elementIndex;
    }

    private static int LayoutContainer(ref Element e, int elementIndex)
    {
        ref var style = ref e.Data.Container;

        // AllocatedSize includes margin, Rect dimensions should not
        e.Rect.Width = e.AllocatedSize.X - style.Margin.L - style.Margin.R;
        e.Rect.Height = e.AllocatedSize.Y - style.Margin.T - style.Margin.B;

        LogUI($"LayoutContainer: Type={e.Type} Id={e.Id} Allocated=({e.AllocatedSize.X:F1},{e.AllocatedSize.Y:F1}) Rect=({e.Rect.Width:F1},{e.Rect.Height:F1})");

        // Position at origin + margin (parent will adjust our position)
        e.Rect.X = style.Margin.L;
        e.Rect.Y = style.Margin.T;

        // Calculate content area for children
        var contentSize = new Vector2(
            e.Rect.Width - style.Padding.L - style.Padding.R - style.Border.Width * 2,
            e.Rect.Height - style.Padding.T - style.Padding.B - style.Border.Width * 2
        );

        // Calculate content offset for children
        var childOffset = new Vector2(
            style.Padding.L + style.Border.Width,
            style.Padding.T + style.Border.Width
        );

        // Position children sequentially using their AllocatedSize
        if (e.Type == ElementType.Container)
        {
            for (var i = 0; i < e.ChildCount; i++)
            {
                ref var child = ref _elements[elementIndex];
                elementIndex = LayoutElement(elementIndex);
                PositionChild(ref child, contentSize);
                child.Rect.X += childOffset.X;
                child.Rect.Y += childOffset.Y;
            }
        }
        else if (e.Type == ElementType.Column)
        {
            for (var i = 0; i < e.ChildCount; i++)
            {
                ref var child = ref _elements[elementIndex];
                LogUI($"  -> Column child {i}: AllocatedSize=({child.AllocatedSize.X:F1},{child.AllocatedSize.Y:F1})");
                elementIndex = LayoutElement(elementIndex);
                // Column: child fills allocated width, position X based on alignment
                PositionChildX(ref child, contentSize.X);
                child.Rect.X += childOffset.X;
                child.Rect.Y += childOffset.Y;
                childOffset.Y += child.AllocatedSize.Y + style.Spacing;
            }
        }
        else if (e.Type == ElementType.Row)
        {
            for (var i = 0; i < e.ChildCount; i++)
            {
                ref var child = ref _elements[elementIndex];
                LogUI($"  -> Row child {i}: AllocatedSize=({child.AllocatedSize.X:F1},{child.AllocatedSize.Y:F1})");
                elementIndex = LayoutElement(elementIndex);
                // Row: child fills allocated height, position Y based on alignment
                PositionChildY(ref child, contentSize.Y);
                child.Rect.X += childOffset.X;
                child.Rect.Y += childOffset.Y;
                childOffset.X += child.AllocatedSize.X + style.Spacing;
            }
        }

        return elementIndex;
    }

    private static void PositionChild(ref Element child, Vector2 parentContentSize)
    {
        PositionChildX(ref child, parentContentSize.X);
        PositionChildY(ref child, parentContentSize.Y);
    }

    private static void PositionChildX(ref Element child, float parentWidth)
    {
        Align alignX;
        if (IsContainerType(child.Type))
            alignX = child.Data.Container.AlignX;
        else if (child.Type == ElementType.Label)
            alignX = child.Data.Label.AlignX;
        else
            return;

        var availWidth = parentWidth - child.AllocatedSize.X;
        child.Rect.X += alignX switch
        {
            Align.Center => availWidth * 0.5f,
            Align.Max => availWidth,
            _ => 0 // Min or Fill
        };
    }

    private static void PositionChildY(ref Element child, float parentHeight)
    {
        Align alignY;
        if (IsContainerType(child.Type))
            alignY = child.Data.Container.AlignY;
        else if (child.Type == ElementType.Label)
            alignY = child.Data.Label.AlignY;
        else
            return;

        var availHeight = parentHeight - child.AllocatedSize.Y;
        child.Rect.Y += alignY switch
        {
            Align.Center => availHeight * 0.5f,
            Align.Max => availHeight,
            _ => 0 // Min or Fill
        };
    }

    private static int LayoutGrid(ref Element e, int elementIndex)
    {
        e.Rect.Width = e.AllocatedSize.X;
        e.Rect.Height = e.AllocatedSize.Y;

        ref var grid = ref e.Data.Grid;
        var startIndex = grid.StartRow * grid.Columns;

        for (var i = 0; i < e.ChildCount; i++)
        {
            var virtualIndex = startIndex + i;
            var col = virtualIndex % grid.Columns;
            var row = virtualIndex / grid.Columns;

            ref var child = ref _elements[elementIndex];
            elementIndex = LayoutElement(elementIndex);
            child.Rect.X = col * (grid.CellWidth + grid.Spacing);
            child.Rect.Y = row * (grid.CellHeight + grid.Spacing);
        }

        return elementIndex;
    }

    private static int LayoutPopup(ref Element e, int elementIndex)
    {
        e.Rect.Width = e.AllocatedSize.X;
        e.Rect.Height = e.AllocatedSize.Y;

        for (var i = 0; i < e.ChildCount; i++)
            elementIndex = LayoutElement(elementIndex);

        ref var popup = ref e.Data.Popup;

        // Get parent's allocated size for anchor positioning
        var parentSize = _size;
        if (e.Index > 0)
        {
            // Find parent by looking for element whose child range includes this one
            for (var i = e.Index - 1; i >= 0; i--)
            {
                ref var candidate = ref _elements[i];
                if (candidate.NextSiblingIndex > e.Index)
                {
                    parentSize = candidate.AllocatedSize;
                    break;
                }
            }
        }

        var anchorX = GetAlignFactor(popup.AnchorX) * parentSize.X;
        var anchorY = GetAlignFactor(popup.AnchorY) * parentSize.Y;

        var popupOffsetX = GetAlignFactor(popup.PopupAlignX) * e.Rect.Width;
        var popupOffsetY = GetAlignFactor(popup.PopupAlignY) * e.Rect.Height;

        e.Rect.X = anchorX - popupOffsetX + popup.Margin.L;
        e.Rect.Y = anchorY - popupOffsetY + popup.Margin.T;

        return elementIndex;
    }
#endif

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
        var mouse = Camera!.ScreenToWorld(Input.MousePosition);
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
        _elementWasPressed = false;
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

        // Consume MouseLeft if any UI element was pressed
        if (_elementWasPressed)
            Input.ConsumeButton(InputCode.MouseLeft);

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
                _elementWasPressed = true;
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


    // Utility methods
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float GetComponent(Vector2 v, int axis) => axis == 0 ? v.X : v.Y;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void SetComponent(ref Vector2 v, int axis, float value)
    {
        if (axis == 0) v.X = value;
        else v.Y = value;
    }

    [Conditional("NOZ_UI_DEBUG")]
    private static void LogUI(string msg, int depth=0, Func<bool>? condition = null, (string name, object? value, bool condition)[]? values = null)
    {
        if (condition == null || condition())
            Log.Debug($"[UI] {new string(' ', depth)}{msg}{Log.Params(values)}");
    }

    [Conditional("NOZ_UI_DEBUG")]
    private static void LogUI(in Element e, string msg, int depth=0, Func<bool>? condition = null, (string name, object? value, bool condition)[]? values = null)
    {
        if (condition == null || condition())
            Log.Debug($"[UI]   {new string(' ', GetDepth(in e) * 2 + depth * 2)}{msg}{Log.Params(values)}");
    }
}
