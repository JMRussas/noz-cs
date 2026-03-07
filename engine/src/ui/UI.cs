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
    private const int MaxTextBuffer = 64 * 1024;

    public struct AutoContainer : IDisposable { readonly void IDisposable.Dispose() => EndContainer(); }
    public struct AutoColumn : IDisposable { readonly void IDisposable.Dispose() => EndColumn(); }
    public struct AutoRow : IDisposable { readonly void IDisposable.Dispose() => EndRow(); }
    public struct AutoScrollable : IDisposable { readonly void IDisposable.Dispose() => EndScrollable(); }
    public struct AutoFlex : IDisposable { readonly void IDisposable.Dispose() => EndFlex(); }
    public struct AutoPopup : IDisposable { readonly void IDisposable.Dispose() => EndPopup(); }
    public struct AutoGrid : IDisposable { readonly void IDisposable.Dispose() => EndGrid(); }
    public struct AutoTransformed : IDisposable { readonly void IDisposable.Dispose() => EndTransformed(); }
    public struct AutoOpacity : IDisposable { readonly void IDisposable.Dispose() => EndOpacity(); }
    public struct AutoCursor : IDisposable { readonly void IDisposable.Dispose() => EndCursor(); }

    private static Font? _defaultFont;
    public static Font DefaultFont => _defaultFont!;
    public static UIConfig Config { get; private set; } = new();

    private static NativeArray<char>[] _textBuffers = [new(MaxTextBuffer), new(MaxTextBuffer)];
    private static int _currentTextBuffer;

    private static ushort _frame;
    public static ushort Frame => _frame;
    private static Vector2 _size;
    private static Vector2Int _refSize;

    // ElementTree wrapper stack for Container/Row/Column decomposition
    // High bit (0x80) = widget was opened, low 7 bits = wrapper element count
    private static readonly byte[] _etWrapperCounts = new byte[128];
    private static int _etWrapperIndex;



    public static Vector2 ScreenSize => _size;

    public static float UserScale { get; set; } = 1.0f;
    public static UIScaleMode? ScaleMode { get; set; }
    public static Vector2Int? ReferenceResolution { get; set; }

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
                var refRes = ReferenceResolution ?? Config.ReferenceResolution;
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
        _mesh = Graphics.CreateMesh<UIVertex>(
            MaxUIVertices,
            MaxUIIndices,
            BufferUsage.Dynamic,
            "UIRender"
        );

        _defaultFont = Asset.Get<Font>(AssetType.Font, Config.DefaultFont);
        _shader = Asset.Get<Shader>(AssetType.Shader, Config.Shader)!;

        ElementTree.Init();
    }

    public static void Shutdown()
    {
        _vertices.Dispose();
        _indices.Dispose();

        Graphics.Driver.DestroyMesh(_mesh.Handle);

        _textBuffers[0].Dispose();
        _textBuffers[1].Dispose();

        ElementTree.Shutdown();
    }

    public static bool IsValidElement(int elementId) => ElementTree.IsWidgetId(elementId);

    public static bool IsRow() => ElementTree.IsParentRow();
    public static bool IsColumn() => ElementTree.IsParentColumn();

    private static ref NativeArray<char> GetTextBuffer() => ref _textBuffers[_currentTextBuffer];

    public static Rect GetElementRect(int elementId)
    {
        if (elementId == 0) return Rect.Zero;
        return ElementTree.GetWidgetRect(elementId);
    }

    public static Rect GetElementWorldRect(int elementId)
    {
        if (elementId == 0) return Rect.Zero;
        return ElementTree.GetWidgetWorldRect(elementId);
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

    internal static UnsafeSpan<char> InsertText(ReadOnlySpan<char> text, int start, ReadOnlySpan<char> insert)
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

    public static bool IsHovered(int elementId) => ElementTree.IsHoveredById(elementId);
    public static bool IsHovered() => ElementTree.IsHovered();
    public static bool HoverEnter() => ElementTree.HoverEnter();
    public static bool HoverEnter(int elementId) => ElementTree.HoverChangedById(elementId) && ElementTree.IsHoveredById(elementId);
    public static bool HoverExit() => ElementTree.HoverExit();
    public static bool HoverExit(int elementId) => ElementTree.HoverChangedById(elementId) && !ElementTree.IsHoveredById(elementId);
    public static bool HoverChanged() => ElementTree.HoverChanged();
    public static bool HoverChanged(int elementId) => ElementTree.HoverChangedById(elementId);
    public static bool WasPressed() => ElementTree.WasPressed();
    public static bool WasPressed(int elementId) => ElementTree.WasPressedById(elementId);
    public static bool IsDown() => ElementTree.IsDown();

    public static void SetDisabled(bool disabled = true) => ElementTree.SetWidgetFlag(ElementFlags.Disabled, disabled);
    public static void SetChecked(bool isChecked = true) => ElementTree.SetWidgetFlag(ElementFlags.Checked, isChecked);
    public static bool IsDisabled() => ElementTree.HasCurrentWidget() && (ElementTree.GetCurrentWidgetFlags() & ElementFlags.Disabled) != 0;
    public static bool IsChecked() => ElementTree.HasCurrentWidget() && (ElementTree.GetCurrentWidgetFlags() & ElementFlags.Checked) != 0;

    public static void SetCapture(int elementId) => ElementTree.SetCaptureById(elementId);
    public static void SetCapture() => ElementTree.SetCapture();
    public static bool HasCapture(int elementId) => ElementTree.HasCaptureById(elementId);
    public static bool HasCapture() => ElementTree.HasCapture();
    public static void ReleaseCapture() => ElementTree.ReleaseCapture();

    public static ref T GetStateByWidgetId<T>(int widgetId) where T : unmanaged =>
        ref ElementTree.GetStateByWidgetId<T>(widgetId);

    public static ReadOnlySpan<char> GetElementText(int elementId)
    {
        if (_lastChangedTextId == elementId)
            return _lastChangedText.AsSpan();

        var editText = ElementTree.GetEditableText(elementId);
        if (editText.Length > 0)
            return editText;

        return default;
    }

    public static void SetElementText(int elementId, ReadOnlySpan<char> value, bool selectAll = false)
    {
        ElementTree.SetEditableText(elementId, value, selectAll);
    }

    public static float GetScrollOffset(int elementId) =>
        ElementTree.GetScrollOffset(elementId);

    public static void SetScrollOffset(int elementId, float offset) =>
        ElementTree.SetScrollOffset(elementId, offset);

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

    public static bool IsClosed() => ElementTree.ClosePopups;

    internal static void Begin()
    {
        _prevHotId = _hotId;
        _hotId = 0;
        _valueChanged = false;

        _frame++;
        _refSize = GetRefSize();
        _etWrapperIndex = 0;
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

        ElementTree.ScreenSize = _size;
        ElementTree.Begin();
        ElementTree.BeginSize(Size.Percent(1), Size.Percent(1));
    }

    // axis: -1=stack(container), 0=row, 1=column
    private static void BeginContainerImpl(int id, in ContainerStyle style, int axis)
    {
        int count = 0;
        bool hasWidget = id != 0;
        if (hasWidget) ElementTree.BeginWidget(id);

        var flags = ElementTree.HasCurrentWidget() ? ElementTree.GetCurrentWidgetFlags() : ElementFlags.None;
        var resolved = style.Resolve != null ? style.Resolve(style, flags) : style;
        var bgColor = resolved.Color;
        var borderColor = resolved.BorderColor;
        var borderWidth = resolved.BorderWidth;

        if (style.Margin.L != 0 || style.Margin.R != 0 || style.Margin.T != 0 || style.Margin.B != 0)
            { ElementTree.BeginMargin(style.Margin); count++; }
        if (borderWidth > 0)
            { ElementTree.BeginBorder(borderWidth, borderColor, style.BorderRadius); count++; }
        ElementTree.BeginSize(style.Size); count++;
        if (!bgColor.IsTransparent)
            { ElementTree.BeginFill(bgColor, style.BorderRadius); count++; }
        if (style.Padding.L != 0 || style.Padding.R != 0 || style.Padding.T != 0 || style.Padding.B != 0)
            { ElementTree.BeginPadding(style.Padding); count++; }
        if (style.Clip)
            { ElementTree.BeginClip(style.BorderRadius); count++; }
        if (style.Align.X != Align.Min || style.Align.Y != Align.Min)
            { ElementTree.BeginAlign(style.Align); count++; }
        if (axis == 0) { ElementTree.BeginRow(style.Spacing); count++; }
        else if (axis == 1) { ElementTree.BeginColumn(style.Spacing); count++; }
        _etWrapperCounts[_etWrapperIndex++] = (byte)(count | (hasWidget ? 0x80 : 0));
    }

    private static void EndContainerImpl()
    {
        var packed = _etWrapperCounts[--_etWrapperIndex];
        var count = packed & 0x7F;
        var hasWidget = (packed & 0x80) != 0;
        for (int i = 0; i < count; i++)
            ElementTree.EndElement();
        if (hasWidget) ElementTree.EndWidget();
    }

    public static AutoContainer BeginContainer(int id=default) =>
        BeginContainer(id, ContainerStyle.Default);

    public static AutoContainer BeginContainer(int id, in ContainerStyle style)
    {
        BeginContainerImpl(id, style, -1);
        return new AutoContainer();
    }

    public static AutoContainer BeginContainer(in ContainerStyle style) =>
        BeginContainer(0, style);


    public static void EndContainer() => EndContainerImpl();

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
        BeginContainerImpl(id, style, 1);
        return new AutoColumn();
    }

    public static AutoColumn BeginColumn(int id) =>
        BeginColumn(id, ContainerStyle.Default);

    public static AutoColumn BeginColumn(in ContainerStyle style) =>
        BeginColumn(0, style);

    public static AutoColumn BeginColumn() =>
        BeginColumn(0, ContainerStyle.Default);


    public static void EndColumn() => EndContainerImpl();

    public static AutoRow BeginRow(int id, in ContainerStyle style)
    {
        BeginContainerImpl(id, style, 0);
        return new AutoRow();
    }

    public static AutoRow BeginRow(int id) =>
        BeginRow(id, ContainerStyle.Default);

    public static AutoRow BeginRow(in ContainerStyle style) =>
        BeginRow(0, style);

    public static AutoRow BeginRow() =>
        BeginRow(0, ContainerStyle.Default);


    public static void EndRow() => EndContainerImpl();

    public static void BeginCenter()
    {
        BeginContainerImpl(0, ContainerStyle.Center, -1);
    }

    public static void EndCenter() => EndContainerImpl();

    public static AutoFlex BeginFlex() => BeginFlex(1.0f);
    public static AutoFlex BeginFlex(float flex)
    {
        ElementTree.BeginFlex(flex);
        _etWrapperCounts[_etWrapperIndex++] = 1;
        return new AutoFlex();
    }

    public static void EndFlex() => EndContainerImpl();

    public static void Flex() => Flex(1.0f);
    public static void Flex(float flex) => ElementTree.Flex(flex);

    public static void Spacer(float size) => ElementTree.Spacer(size, size);

    public static void BeginBorder(BorderStyle style)
    {
        int count = 0;
        if (style.Width > 0)
            { ElementTree.BeginBorder(style.Width, style.Color, style.Radius); count++; }
        _etWrapperCounts[_etWrapperIndex++] = (byte)count;
    }

    public static void EndBorder() => EndContainerImpl();

    public static AutoTransformed BeginTransformed(TransformStyle style)
    {
        ElementTree.BeginTransform(style.Origin, style.Translate, style.Rotate, style.Scale);
        _etWrapperCounts[_etWrapperIndex++] = 1;
        return new AutoTransformed();
    }

    public static void EndTransformed() => EndContainerImpl();

    public static AutoOpacity BeginOpacity(float opacity)
    {
        ElementTree.BeginOpacity(opacity);
        _etWrapperCounts[_etWrapperIndex++] = 1;
        return new AutoOpacity();
    }

    public static void EndOpacity() => EndContainerImpl();

    public static AutoCursor BeginCursor(Sprite sprite)
    {
        ElementTree.BeginCursor(sprite);
        _etWrapperCounts[_etWrapperIndex++] = 1;
        return new AutoCursor();
    }

    public static AutoCursor BeginCursor(SystemCursor cursor)
    {
        ElementTree.BeginCursor(cursor);
        _etWrapperCounts[_etWrapperIndex++] = 1;
        return new AutoCursor();
    }

    public static void EndCursor() => EndContainerImpl();

    public static AutoScrollable BeginScrollable(int id) =>
        BeginScrollable(id, new ScrollableStyle());

    public static AutoScrollable BeginScrollable(int id, in ScrollableStyle style)
    {
        Debug.Assert(id != 0);
        ElementTree.BeginWidget(id);
        ElementTree.BeginScrollable(id, in style);
        _etWrapperCounts[_etWrapperIndex++] = (byte)(1 | 0x80); // 1 element (scrollable), widget flag
        return new AutoScrollable();
    }

    public static void EndScrollable() => EndContainerImpl();

    public static AutoGrid BeginGrid(GridStyle style)
    {
        ElementTree.BeginGrid(style.Spacing, style.Columns, style.CellWidth, style.CellHeight,
            style.CellMinWidth, style.CellHeightOffset, style.VirtualCount, style.StartIndex);
        _etWrapperCounts[_etWrapperIndex++] = 1;
        return new AutoGrid();
    }

    public static (int columns, float cellWidth, float cellHeight) ResolveGridCellSize(
        int columns, float cellWidth, float cellHeight,
        float cellMinWidth, float cellHeightOffset,
        float spacing, float availableWidth)
    {
        if (columns <= 0 && cellMinWidth > 0)
            columns = Math.Max(1, (int)((availableWidth + spacing) / (cellMinWidth + spacing)));
        columns = Math.Max(1, columns);

        if (cellWidth <= 0)
        {
            cellWidth = Math.Max(0, (availableWidth - (columns - 1) * spacing) / columns);
            cellHeight = cellWidth + cellHeightOffset;
        }

        return (columns, cellWidth, cellHeight);
    }

    public static void EndGrid() => EndContainerImpl();

    public static void Scene(int id, Camera camera, Action draw) =>
        Scene(id, camera, draw, new SceneStyle());

    public static void Scene(int id, Camera camera, Action draw, SceneStyle style = default)
    {
        if (id != 0) ElementTree.BeginWidget(id);
        ElementTree.Scene(camera, draw, style.Size, style.Color, style.SampleCount);
        if (id != 0) ElementTree.EndWidget();
    }

    public static void Scene(Camera camera, Action draw, SceneStyle style = default) => Scene(0, camera, draw, style);

    public static AutoPopup BeginPopup(int id, PopupStyle style)
    {
        ElementTree.BeginPopup(
            style.AnchorRect,
            style.Anchor,
            style.PopupAlign,
            style.Spacing,
            style.ClampToScreen,
            style.AutoClose,
            style.Interactive);

        return new AutoPopup();
    }

    public static void EndPopup()
    {
        ElementTree.EndPopup();
    }

    // :label
    public static void Label(ReadOnlySpan<char> text) =>
        Label(text, new LabelStyle());

    public static void Label(ReadOnlySpan<char> text, LabelStyle style)
    {
        var flags = ElementTree.HasCurrentWidget() ? ElementTree.GetCurrentWidgetFlags() : ElementFlags.None;
        var resolved = style.Resolve != null ? style.Resolve(style, flags) : style;
        var font = resolved.Font ?? _defaultFont!;
        var fontSize = resolved.FontSize > 0 ? resolved.FontSize : 16f;
        ElementTree.Label(ElementTree.Text(text), font, fontSize, resolved.Color, resolved.Align, resolved.Overflow);
    }

    public static void Label(string text) => Label(text.AsSpan(), new LabelStyle());

    public static void Label(string text, LabelStyle style) => Label(text.AsSpan(), style);

    public static void WrappedLabel(int id, string text, LabelStyle style)
    {
        var flags = ElementTree.HasCurrentWidget() ? ElementTree.GetCurrentWidgetFlags() : ElementFlags.None;
        var resolved = style.Resolve != null ? style.Resolve(style, flags) : style;
        var font = resolved.Font ?? _defaultFont!;
        var fontSize = resolved.FontSize > 0 ? resolved.FontSize : 16f;
        ElementTree.Label(ElementTree.Text(text), font, fontSize, resolved.Color, resolved.Align, TextOverflow.Wrap);
    }

    // :image
    public static void Image(Sprite? sprite) => Image(sprite, new ImageStyle());

    public static void Image(Sprite? sprite, in ImageStyle style)
    {
        if (sprite == null) return;
        var flags = ElementTree.HasCurrentWidget() ? ElementTree.GetCurrentWidgetFlags() : ElementFlags.None;
        var resolved = style.Resolve != null ? style.Resolve(style, flags) : style;
        ElementTree.Image(sprite, resolved.Size, resolved.Stretch, resolved.Color, resolved.Scale);
    }

    public static void Image(Texture texture) => Image(texture, new ImageStyle());

    public static void Image(Texture texture, in ImageStyle style)
    {
        var flags = ElementTree.HasCurrentWidget() ? ElementTree.GetCurrentWidgetFlags() : ElementFlags.None;
        var resolved = style.Resolve != null ? style.Resolve(style, flags) : style;
        ElementTree.Image(texture, resolved.Size, resolved.Stretch, resolved.Color, resolved.Scale);
    }

    internal static void End()
    {
        ElementTree.EndSize();
        ElementTree.MouseWorldPosition = MouseWorldPosition;
        ElementTree.End();

        Graphics.SetCamera(Camera);
        HandleInput();

        ElementTree.Draw();

#if DEBUG
        if (Input.IsCtrlDown() && Input.WasButtonPressed(InputCode.KeyF12))
        {
            Directory.CreateDirectory("temp");
            File.WriteAllText("temp/et_dump.txt", ElementTree.DebugDumpTree());
        }
#endif
    }

    public static Rect WorldToSceneLocal(Camera camera, int sceneElementId, Rect worldRect)
    {
        var viewport = camera.Viewport;
        var elementRect = UI.GetElementRect(sceneElementId);
        var screenRect = camera.WorldToScreen(worldRect);

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
}
