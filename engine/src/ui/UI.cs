//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

// #define NOZ_UI_DEBUG
// #define NOZ_UI_DEBUG_LINE_DIFF

using NoZ.Platform;
using System.Diagnostics;
using System.Numerics;

namespace NoZ;

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
    private const int MaxElementId = 32767;

    public struct AutoContainer : IDisposable { readonly void IDisposable.Dispose() => EndContainer(); }
    public struct AutoColumn : IDisposable { readonly void IDisposable.Dispose() => EndColumn(); }
    public struct AutoRow : IDisposable { readonly void IDisposable.Dispose() => EndRow(); }
    public struct AutoScrollable : IDisposable { readonly void IDisposable.Dispose() => EndScrollable(); }
    public struct AutoFlex : IDisposable { readonly void IDisposable.Dispose() => EndFlex(); }
    public struct AutoPopup : IDisposable { readonly void IDisposable.Dispose() => EndPopup(); }
    public struct AutoGrid : IDisposable { readonly void IDisposable.Dispose() => EndGrid(); }
    public struct AutoTransformed : IDisposable { readonly void IDisposable.Dispose() => EndTransformed(); }
    public struct AutoOpacity : IDisposable { readonly void IDisposable.Dispose() => EndOpacity(); }

    private static Font? _defaultFont;
    public static Font? DefaultFont => _defaultFont;
    public static UIConfig Config { get; private set; } = new();

    private static readonly Element[] _elements = new Element[MaxElements];
    private static readonly short[] _elementStack = new short[MaxElementStack];
    private static readonly short[] _popups = new short[MaxPopups];
    private static NativeArray<ElementState> _elementStates = new(MaxElementId + 1, MaxElementId + 1);
    private static NativeArray<char>[] _textBuffers = [new(MaxTextBuffer), new(MaxTextBuffer)];
    private static int _currentTextBuffer;

    private static ushort _frame;
    private static short _elementCount;
    private static short _previousElementCount;
    private static int _elementStackCount;
    private static int _popupCount;
    private static int _focusElementId;
    private static int _pendingFocusElementId;
    private static Vector2 _size;
    private static Vector2Int _refSize;
    private static int _activeScrollId;
    private static float _lastScrollMouseY;
    private static bool _closePopups;

    // Scrollbar drag state
    private static bool _scrollbarDragging;
    private static int _scrollbarDragElementId;
    private static float _scrollbarDragStartOffset;
    private static float _scrollbarDragStartMouseY;
    public static Vector2 ScreenSize => _size;

    public static float UserScale { get; set; } = 1.0f;
    public static UIScaleMode? ScaleMode { get; set; }

    public static float GetUIScale() => Application.Platform.DisplayScale * UserScale;

    public static Camera? Camera { get; private set; } = null!;

    public static Vector2Int GetRefSize()
    {
        var screenSize = Application.WindowSize.ToVector2();
        var displayScale = Application.Platform.DisplayScale;

        switch (ScaleMode ?? Config.ScaleMode)
        {
            case UIScaleMode.ConstantPixelSize:
                return new Vector2Int(
                    (int)(screenSize.X / displayScale / UserScale),
                    (int)(screenSize.Y / displayScale / UserScale)
                );

            case UIScaleMode.ScaleWithScreenSize:
            default:
                var refRes = Config.ReferenceResolution;
                var logWidth = MathF.Log2(screenSize.X / refRes.X);
                var logHeight = MathF.Log2(screenSize.Y / refRes.Y);

                float scaleFactor;
                switch (Config.ScreenMatchMode)
                {
                    case ScreenMatchMode.Expand:
                        scaleFactor = MathF.Pow(2, MathF.Min(logWidth, logHeight));
                        break;
                    case ScreenMatchMode.Shrink:
                        scaleFactor = MathF.Pow(2, MathF.Max(logWidth, logHeight));
                        break;
                    case ScreenMatchMode.MatchWidthOrHeight:
                    default:
                        var logInterp = MathEx.Mix(logWidth, logHeight, Config.MatchWidthOrHeight);
                        scaleFactor = MathF.Pow(2, logInterp);
                        break;
                }

                scaleFactor *= UserScale;

                return new Vector2Int(
                    (int)(screenSize.X / scaleFactor),
                    (int)(screenSize.Y / scaleFactor)
                );
        }
    }

    public static void Init(UIConfig? config = null)
    {
        Config = config ?? new UIConfig();
        Camera = new Camera { FlipY = false };

        _vertices = new NativeArray<UIVertex>(MaxUIVertices);
        _indices = new NativeArray<ushort>(MaxUIIndices);
        _mesh = Graphics.Driver.CreateMesh<UIVertex>(
            MaxUIVertices,
            MaxUIIndices,
            BufferUsage.Dynamic,
            "UIRender"
        );

        _defaultFont = Asset.Get<Font>(AssetType.Font, Config.DefaultFont);
        _shader = Asset.Get<Shader>(AssetType.Shader, Config.Shader)!;
    }

    public static void Shutdown()
    {
        _vertices.Dispose();
        _indices.Dispose();

        Graphics.Driver.DestroyMesh(_mesh);

        _textBuffers[0].Dispose();
        _textBuffers[1].Dispose();
    }

    private static ref Element CreateElement(ElementType type)
    {
        ref var element = ref _elements[_elementCount];
        element.Type = type;
        element.Id = 0;
        element.Index = _elementCount;
        element.NextSiblingIndex = 0;
        element.ParentIndex = _elementStackCount > 0 ? _elementStack[_elementStackCount - 1] : (short)-1;
        element.ChildCount = 0;
        element.Rect = Rect.Zero;
        element.MeasuredSize = Vector2.Zero;
        element.LocalToWorld = Matrix3x2.Identity;
        element.WorldToLocal = Matrix3x2.Identity;
        element.Asset = null;

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

    private static ref ElementState GetElementState(ref Element e)
    {
        Debug.Assert(e.Id > 0 && e.Id <= MaxElementId, $"Invalid ElementId: {e.Id}");
        return ref _elementStates[e.Id];
    }

    private static ref ElementState GetElementState(int elementId)
    {
        Debug.Assert(elementId > 0 && elementId <= MaxElementId, $"Invalid ElementId: {elementId}");
        return ref _elementStates[elementId];
    }

    public static bool IsRow() => GetSelf().Type == ElementType.Row;
    public static bool IsColumn() => GetSelf().Type == ElementType.Row;

    private static ref NativeArray<char> GetTextBuffer() => ref _textBuffers[_currentTextBuffer];

    public static int GetElementId() => HasCurrentElement() ? GetSelf().Id : 0;

    public static Rect GetElementRect(int elementId)
    {
        if (elementId == 0) return Rect.Zero;
        return GetElementState(elementId).Rect;
    }

    public static Rect GetElementWorldRect(int elementId)
    {
        if (elementId == 0)
            return Rect.Zero;

        ref var es = ref GetElementState(elementId);
        var topLeft = Vector2.Transform(es.Rect.Position, es.LocalToWorld);
        var bottomRight = Vector2.Transform(es.Rect.Position + es.Rect.Size, es.LocalToWorld);
        return new Rect(topLeft.X, topLeft.Y, bottomRight.X - topLeft.X, bottomRight.Y - topLeft.Y);
    }

    public static int GetParentElementId() =>
        GetElement(GetElement(GetElementId()).ParentIndex).Id;

    public static Rect GetRelativeElementRect(int elementId, int relativeToId)
    {
        if (elementId == 0)
            return Rect.Zero;

        ref var sourceState = ref GetElementState(elementId);

        var topLeft = Vector2.Transform(sourceState.Rect.Position, sourceState.LocalToWorld);
        var bottomRight = Vector2.Transform(sourceState.Rect.Position + sourceState.Rect.Size, sourceState.LocalToWorld);

        // If no relative element specified, return world-space rect
        if (relativeToId == 0)
            return new Rect(topLeft.X, topLeft.Y, bottomRight.X - topLeft.X, bottomRight.Y - topLeft.Y);

        // Transform to target element's local space using cached transform
        ref var targetState = ref GetElementState(relativeToId);
        Matrix3x2.Invert(targetState.LocalToWorld, out var targetWorldToLocal);

        topLeft = Vector2.Transform(topLeft, targetWorldToLocal);
        bottomRight = Vector2.Transform(bottomRight, targetWorldToLocal);

        return new Rect(topLeft.X, topLeft.Y, bottomRight.X - topLeft.X, bottomRight.Y - topLeft.Y);
    }

    private static bool HasCurrentElement() => _elementStackCount > 0;

    private static void SetId(ref Element e, int elementId)
    {
        if (elementId == 0) return;
        Debug.Assert(elementId > 0 && elementId <= MaxElementId, $"Invalid ElementId: {elementId}");

        e.Id = elementId;

        ref var es = ref GetElementState(elementId);

        // Clear hover for elements not present in the previous frame to prevent
        // stale/garbage flags from triggering false HoverChanged
        if (es.LastFrame != (ushort)(_frame - 1) && es.LastFrame != _frame)
            es.SetFlags(ElementFlags.Hovered, ElementFlags.None);

        es.LastFrame = _frame;
        es.Index = e.Index;
    }

    private static void PushElement(short index)
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

    internal static UnsafeSpan<char> AddText(int length)
    {
        ref var textBuffer = ref GetTextBuffer();
        if (textBuffer.Length + length > textBuffer.Capacity)
            return UnsafeSpan<char>.Empty;
        return textBuffer.AddRange(length);
    }

    internal static UnsafeSpan<char> AddText(ReadOnlySpan<char> text)
    {
        ref var textBuffer = ref GetTextBuffer();
        if (textBuffer.Length + text.Length > textBuffer.Capacity)
            return UnsafeSpan<char>.Empty;
        return textBuffer.AddRange(text);
    }

    private static UnsafeSpan<char> InsertText(ReadOnlySpan<char> text, int start, ReadOnlySpan<char> insert)
    {
        var result = AddText(text.Length + insert.Length);
        for (int i = 0; i < result.Length; i++)
            result[i] = ' ';
        if (start > 0)
            text[..start].CopyTo(result.AsSpan(0, start));
        insert.CopyTo(result.AsSpan(start, insert.Length));
        if (start < text.Length)
            text[start..].CopyTo(result.AsSpan(start + insert.Length, text.Length - start));
        return result;
    }

    public static UnsafeSpan<char> RemoveText(ReadOnlySpan<char> text, int start, int count)
    {
        if (text.Length - count <= 0)
            return UnsafeSpan<char>.Empty;

        var result = AddText(text.Length - count);
        text[..start].CopyTo(result.AsSpan(0, start));
        text[(start + count)..].CopyTo(result.AsSpan(start, text.Length - start - count));
        return result;
    }

    public static bool IsHovered(int elementId) => GetElementState(elementId).IsHovered;
    public static bool IsHovered() => GetElementState(ref GetSelf()).IsHovered;
    public static bool HoverEnter() { ref var es = ref GetElementState(ref GetSelf()); return es.IsHoverChanged && es.IsHovered; }
    public static bool HoverEnter(int elementId) { ref var es = ref GetElementState(elementId); return es.IsHoverChanged && es.IsHovered; }
    public static bool HoverExit() { ref var es = ref GetElementState(ref GetSelf()); return es.IsHoverChanged && !es.IsHovered; }
    public static bool HoverExit(int elementId) { ref var es = ref GetElementState(elementId); return es.IsHoverChanged && !es.IsHovered; }
    public static bool HoverChanged() => GetElementState(ref GetSelf()).IsHoverChanged;
    public static bool HoverChanged(int elementId) => GetElementState(elementId).IsHoverChanged;
    public static bool WasPressed() => GetElementState(ref GetSelf()).IsPressed;
    public static bool IsDown() => GetElementState(ref GetSelf()).IsDown;

    public static ref Tween GetElementTween(int elementId) => ref GetElementState(elementId).Tween;

    public static float GetScrollOffset(int elementId)
    {
        if (elementId == 0) return 0;
        ref var es = ref GetElementState(elementId);
        if (es.Index == 0) return 0;
        ref var e = ref GetElement(es.Index);
        if (e.Id != elementId) return 0;
        Debug.Assert(e.Type == ElementType.Scrollable, "GetScrollOffset called on non-scrollable element");
        return es.Data.Scrollable.Offset;
    }

    public static void SetScrollOffset(int elementId, float offset)
    {
        if (elementId == 0) return;
        ref var es = ref GetElementState(elementId);
        ref var e = ref GetElement(es.Index);
        Debug.Assert(e.Type == ElementType.Scrollable, "SetScrollOffset called on non-scrollable element");
        es.Data.Scrollable.Offset = offset;
    }

    /// <summary>
    /// Calculates the visible index range for a virtualized grid inside a scrollable.
    /// Returns (startIndex, endIndex) where endIndex is exclusive.
    /// </summary>
    public static (int startIndex, int endIndex) GetGridCellRange(
        int scrollId,
        int columns,
        float cellHeight,
        float spacing,
        float viewportHeight,
        int totalCount)
    {
        if (totalCount <= 0) return (0, 0);

        var scrollOffset = GetScrollOffset(scrollId);
        var rowHeight = cellHeight + spacing;

        // Calculate visible row range with 1-row buffer above and below
        var totalRows = (totalCount + columns - 1) / columns;
        var startRow = Math.Max(0, (int)(scrollOffset / rowHeight) - 1);
        var visibleRows = (int)Math.Ceiling(viewportHeight / rowHeight) + 2;
        var endRow = Math.Min(totalRows, startRow + visibleRows);

        var startIndex = startRow * columns;
        var endIndex = Math.Min(totalCount, endRow * columns);

        return (startIndex, endIndex);
    }

    public static Vector2 ScreenToUI(Vector2 screenPos) =>
        screenPos / Application.WindowSize.ToVector2() * _size;

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
        return _focusElementId != 0 && e.Id == _focusElementId;
    }

    public static void SetFocus(int elementId)
    {
        _pendingFocusElementId = elementId;
    }

    public static void ClearFocus()
    {
        _focusElementId = 0;
        _pendingFocusElementId = 0;
    }

    public static bool IsClosed() =>
        HasCurrentElement() && GetSelf().Type == ElementType.Popup && _closePopups;

    private static bool IsFocused(ref Element e) => IsFocused(e.Id);

    public static bool IsFocused(int elementId) =>
        elementId != 0 &&
        _focusElementId == elementId;

    internal static void Begin()
    {
        _frame++;
        _previousElementCount = _elementCount;
        _refSize = GetRefSize();
        _elementStackCount = 0;
        _elementCount = 0;
        _popupCount = 0;
        _currentTextBuffer = 1 - _currentTextBuffer;
        _textBuffers[_currentTextBuffer].Clear();

        var screenSize = Application.WindowSize.ToVector2();
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

        // Apply pending focus
        _focusElementId = _pendingFocusElementId;

        // Create automatic full-screen root container
        ref var root = ref CreateElement(ElementType.Container);
        root.Id = 0;
        root.Data.Container = ContainerData.Default;
        PushElement(root.Index);
    }

    public static AutoContainer BeginContainer(int id=default)
    {
        ref var e = ref CreateElement(ElementType.Container);
        e.Data.Container = ContainerData.Default;
        SetId(ref e, id);
        PushElement(e.Index);
        return new AutoContainer();
    }

    public static AutoContainer BeginContainer(int id, in ContainerStyle style)
    {
        ref var e = ref CreateElement(ElementType.Container);
        e.Data.Container = style.ToData();
        SetId(ref e, id);
        PushElement(e.Index);
        return new AutoContainer();
    }

    public static AutoContainer BeginContainer(in ContainerStyle style) =>
        BeginContainer(0, style);

    public static void EndContainer() => EndElement(ElementType.Container);

    public static void Container(int id=0)
    {
        BeginContainer(id:id);
        EndContainer();
    }

    public static void Container(int id, ContainerStyle style)
    {
        BeginContainer(id, style);
        EndContainer();
    }

    public static void Container(ContainerStyle style) =>
        Container(0, style);

    public static AutoColumn BeginColumn(int id, in ContainerStyle style)
    {
        ref var e = ref CreateElement(ElementType.Column);
        e.Data.Container = style.ToData();
        SetId(ref e, id);
        PushElement(e.Index);
        return new AutoColumn();
    }

    public static AutoColumn BeginColumn(int id) =>
        BeginColumn(id, ContainerStyle.Default);

    public static AutoColumn BeginColumn(in ContainerStyle style) =>
        BeginColumn(0, style);

    public static AutoColumn BeginColumn() =>
        BeginColumn(0, ContainerStyle.Default);

    public static void EndColumn() => EndElement(ElementType.Column);

    public static AutoRow BeginRow(int id, in ContainerStyle style)
    {
        ref var e = ref CreateElement(ElementType.Row);
        e.Data.Container = style.ToData();
        SetId(ref e, id);
        PushElement(e.Index);
        return new AutoRow();
    }

    public static AutoRow BeginRow(int id) =>
        BeginRow(id, ContainerStyle.Default);

    public static AutoRow BeginRow(in ContainerStyle style) =>
        BeginRow(0, style);

    public static AutoRow BeginRow() =>
        BeginRow(0, ContainerStyle.Default);

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

    public static AutoTransformed BeginTransformed(TransformStyle style)
    {
        ref var e = ref CreateElement(ElementType.Transform);
        e.Data.Transform = new TransformData
        {
            Pivot = style.Origin,
            Translate = style.Translate,
            Rotate = style.Rotate,
            Scale = style.Scale
        };
        PushElement(e.Index);
        return new AutoTransformed();
    }

    public static void EndTransformed() => EndElement(ElementType.Transform);

    public static AutoOpacity BeginOpacity(float opacity)
    {
        ref var e = ref CreateElement(ElementType.Opacity);
        e.Data.Opacity = new OpacityData { Value = opacity };
        PushElement(e.Index);
        return new AutoOpacity();
    }

    public static void EndOpacity() => EndElement(ElementType.Opacity);

    public static AutoScrollable BeginScrollable(int id) =>
        BeginScrollable(id, new ScrollableStyle());

    public static AutoScrollable BeginScrollable(int id, in ScrollableStyle style)
    {
        Debug.Assert(id != 0);
        ref var e = ref CreateElement(ElementType.Scrollable);
        e.Data.Scrollable = new ScrollableData
        {
            Offset = 0,
            ContentHeight = 0,
            ScrollSpeed = style.ScrollSpeed,
            ScrollbarVisibility = style.Scrollbar,
            ScrollbarWidth = style.ScrollbarWidth,
            ScrollbarMinThumbHeight = style.ScrollbarMinThumbHeight,
            ScrollbarTrackColor = style.ScrollbarTrackColor,
            ScrollbarThumbColor = style.ScrollbarThumbColor,
            ScrollbarThumbHoverColor = style.ScrollbarThumbHoverColor,
            ScrollbarPadding = style.ScrollbarPadding,
            ScrollbarBorderRadius = style.ScrollbarBorderRadius
        };
        SetId(ref e, id);

        // Restore persisted scroll offset from element state
        ref var es = ref GetElementState(ref e);
        e.Data.Scrollable.Offset = es.Data.Scrollable.Offset;

        PushElement(e.Index);
        return new AutoScrollable();
    }

    public static void EndScrollable() => EndElement(ElementType.Scrollable);

    public static AutoGrid BeginGrid(GridStyle style)
    {
        ref var e = ref CreateElement(ElementType.Grid);
        e.Data.Grid = new GridData
        {
            Spacing = style.Spacing,
            Columns = style.Columns,
            CellWidth = style.CellWidth,
            CellHeight = style.CellHeight,
            VirtualCount = style.VirtualCount,
            StartIndex = style.StartIndex
        };
        PushElement(e.Index);
        return new AutoGrid();
    }

    public static void EndGrid() => EndElement(ElementType.Grid);

    public static void Scene(int id, Camera camera, Action draw, SceneStyle style = default)
    {
        ref var e = ref CreateElement(ElementType.Scene);

        e.Data.Scene = new SceneData
        {
            Size = style.Size,
            Color = style.Color,
            SampleCount = style.SampleCount
        };
        e.Asset = (camera, draw);

        SetId(ref e, id);
    }

    public static void Scene(Camera camera, Action draw, SceneStyle style = default) => Scene(0, camera, draw, style);

    public static AutoPopup BeginPopup(int id, PopupStyle style)
    {
        ref var e = ref CreateElement(ElementType.Popup);
        e.Data.Popup = new PopupData
        {
            AnchorX = style.AnchorX,
            AnchorY = style.AnchorY,
            PopupAlignX = style.PopupAlignX,
            PopupAlignY = style.PopupAlignY,
            Spacing = style.Spacing,
            ClampToScreen = style.ClampToScreen,
            AnchorRect = style.AnchorRect,
            MinWidth = style.MinWidth
        };
        SetId(ref e, id);
        PushElement(e.Index);
        _popups[_popupCount++] = e.Index;
        return new AutoPopup();
    }

    public static void EndPopup() => EndElement(ElementType.Popup);

    // :label
    public static void Label(string text) => Label(text, new LabelStyle());

    public static void Label(string text, LabelStyle style)
    {
        ref var e = ref CreateElement(ElementType.Label);
        e.Asset = style.Font ?? _defaultFont;
        e.Data.Label = new LabelData
        {
            FontSize = style.FontSize > 0 ? style.FontSize : 16,
            Color = style.Color,
            AlignX = style.AlignX,
            AlignY = style.AlignY,
            Order = style.Order,
            Text = AddText(text)
        };

        PushElement(e.Index);
        PopElement();
    }

    // :image
    public static void Image(Sprite? sprite) => Image(sprite, new ImageStyle());

    public static void Image(Sprite? sprite, in ImageStyle style)
    {
        if (sprite == null) return;

        ref var e = ref CreateElement(ElementType.Image);
        e.Asset = sprite;
        e.Data.Image = new ImageData
        {
            Stretch = style.Stretch,
            AlignX = style.AlignX,
            AlignY = style.AlignY,
            Scale = style.Scale,
            Color = style.Color,
            Order = style.Order,
            Texture = Graphics.SpriteAtlas?.Handle ?? nuint.Zero,
            UV0 = sprite.UV.TopLeft,
            UV1 = sprite.UV.BottomRight,
            Width = sprite.Bounds.Width,
            Height = sprite.Bounds.Height,
            AtlasIndex = sprite.AtlasIndex
        };

        PushElement(e.Index);
        PopElement();
    }

    public static void Image(Texture texture) => Image(texture, new ImageStyle());

    public static void Image(Texture texture, in ImageStyle style)
    {
        ref var e = ref CreateElement(ElementType.Image);
        e.Asset = texture;
        e.Data.Image = new ImageData
        {
            Stretch = style.Stretch,
            AlignX = style.AlignX,
            AlignY = style.AlignY,
            Scale = style.Scale,
            Color = style.Color,
            Order = style.Order,
            Texture = texture.Handle,
            UV0 = Vector2.Zero,
            UV1 = Vector2.One,
            Width = texture.Width,
            Height = texture.Height,
            AtlasIndex = -1,
            BorderRadius = style.BorderRadius
        };

        PushElement(e.Index);
        PopElement();
    }

    // :textbox
    public static bool TextBox(
        int id,
        TextBoxStyle style = default,
        string? placeholder = null)
    {
        ref var e = ref CreateElement(ElementType.TextBox);
        e.Data.TextBox = style.ToData();
        e.Asset = style.Font ?? _defaultFont;

        if (!string.IsNullOrEmpty(placeholder))
            e.Data.TextBox.Placeholder = AddText(placeholder);

        SetId(ref e, id);

        ref var es = ref GetElementState(ref e);

        // the ui system uses alternating text buffers so we can access previous
        // text while building new text. Here we copy the previous text into the current buffer.
        es.Data.TextBox.Text = AddText(es.Data.TextBox.Text.AsReadOnlySpan());

        var changed = es.IsChanged;
        es.SetFlags(ElementFlags.Changed, ElementFlags.None);
        PushElement(e.Index);
        PopElement();

        return changed;
    }

    internal static void End()
    {
        // Pop the automatic root container
        PopElement();

        LayoutElements();

        Graphics.SetCamera(Camera);

        HandleInput();

        // Flush any pending world-space rendering before drawing UI
        // SetCamera also sets u_projection uniform which UIRender will use
        Graphics.SetCamera(Camera);

        DrawElements();

        TextBoxEndFrame();

        for (int i = _elementCount; i < _previousElementCount; i++)
        {
            ref var e = ref _elements[i];
            e.Asset = null;

            if (e.Id == 0) continue;
            {
                ref var es = ref GetElementState(e.Id);
                if (es.LastFrame != _frame)
                    es = default;
            }
        }
    }

    public static Rect WorldToSceneLocal(Camera camera, int sceneElementId, Rect worldRect)
    {
        var viewport = camera.Viewport;                        // screen pixels (set by BeginScene)
        var elementRect = UI.GetElementRect(sceneElementId);   // UI units (from previous frame layout)

        var screenRect = camera.WorldToScreen(worldRect);      // world → screen pixels

        // Map from viewport-relative pixels → element-relative UI units
        return new Rect(
            elementRect.X + (screenRect.X - viewport.X) / viewport.Width * elementRect.Width,
            elementRect.Y + (screenRect.Y - viewport.Y) / viewport.Height * elementRect.Height,
            screenRect.Width / viewport.Width * elementRect.Width,
            screenRect.Height / viewport.Height * elementRect.Height
        );
    }

    [Conditional("NOZ_UI_DEBUG")]
    private static void LogUI(string msg, int depth=0, Func<bool>? condition = null, (string name, object? value, bool condition)[]? values = null)
    {
        if (condition == null || condition())
            Log.Info($"[UI] {new string(' ', depth)}{msg}{Log.Params(values)}");
    }

    [Conditional("NOZ_UI_DEBUG")]
    private static void LogUI(in Element e, string msg, int depth=0, Func<bool>? condition = null, (string name, object? value, bool condition)[]? values = null)
    {
        if (condition == null || condition())
            Log.Info($"[UI]   {new string(' ', GetDepth(in e) * 2 + depth * 2)}{msg}{Log.Params(values)}");
    }
}
