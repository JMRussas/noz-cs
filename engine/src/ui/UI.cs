//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

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

    private static ushort _frame;
    public static ushort Frame => _frame;
    private static Vector2 _size;
    private static Vector2Int _refSize;
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

        ElementTree.Shutdown();
    }

    public static bool IsRow() => ElementTree.IsParentRow();
    public static bool IsColumn() => ElementTree.IsParentColumn();

    public static Rect GetElementRect(WidgetId id)
    {
        if (id == 0) return Rect.Zero;
        return ElementTree.GetWidgetRect(id);
    }

    public static Rect GetElementWorldRect(WidgetId id)
    {
        if (id == 0) return Rect.Zero;
        return ElementTree.GetWidgetWorldRect(id);
    }

    public static bool IsHovered(WidgetId id) => ElementTree.IsHovered(id);
    public static bool IsHovered() => ElementTree.IsHovered();
    public static bool HoverEnter() => ElementTree.HoverEnter();
    public static bool HoverEnter(WidgetId id) => ElementTree.HoverChanged(id) && ElementTree.IsHovered(id);
    public static bool HoverExit() => ElementTree.HoverExit();
    public static bool HoverExit(WidgetId id) => ElementTree.HoverChanged(id) && !ElementTree.IsHovered(id);
    public static bool HoverChanged() => ElementTree.HoverChanged();
    public static bool HoverChanged(WidgetId id) => ElementTree.HoverChanged(id);
    public static bool WasPressed() => ElementTree.WasPressed();
    public static bool WasPressed(WidgetId id) => ElementTree.WasPressed(id);
    public static bool IsDown() => ElementTree.IsDown();

    public static void SetDisabled(bool disabled = true) => ElementTree.SetWidgetFlag(WidgetFlags.Disabled, disabled);
    public static void SetChecked(bool isChecked = true) => ElementTree.SetWidgetFlag(WidgetFlags.Checked, isChecked);
    public static bool IsDisabled() => ElementTree.IsDisabled();
    public static bool IsChecked() => ElementTree.IsChecked();

    public static void SetCapture(WidgetId id) => ElementTree.SetCaptureById(id);
    public static void SetCapture() => ElementTree.SetCapture();
    public static bool HasCapture(WidgetId id) => ElementTree.HasCaptureById(id);
    public static bool HasCapture() => ElementTree.HasCapture();
    public static void ReleaseCapture() => ElementTree.ReleaseCapture();

    public static ReadOnlySpan<char> GetElementText(WidgetId id)
    {
        //if (_lastChangedTextId == id)
        //    return _lastChangedText.AsSpan();

        //var editText = ElementTree.GetEditableText(id);
        //if (editText.Length > 0)
        //    return editText;

        return default;
    }

    public static void SetElementText(WidgetId id, ReadOnlySpan<char> value, bool selectAll = false)
    {
        if (!ElementTree.IsWidgetValid(id))
            return;

        ref var state = ref ElementTree.GetWidgetState<EditableTextState>(id);
        state.EditText = ElementTree.AllocString(value);
        state.PrevTextHash = string.GetHashCode(value);
        state.TextHash = state.PrevTextHash;
        state.Focused = 1;
        state.JustFocused = 1;
        state.FocusExited = 0;
        state.WasCancelled = 0;

        if (selectAll)
        {
            state.SelectionStart = 0;
            state.CursorIndex = value.Length;
        }
        else
        {
            state.CursorIndex = value.Length;
            state.SelectionStart = value.Length;
        }
    }

    public static float GetScrollOffset(WidgetId id) =>
        ElementTree.GetScrollOffset(id);

    public static void SetScrollOffset(WidgetId elementId, float offset) =>
        ElementTree.SetScrollOffset(elementId, offset);

    /// <summary>
    /// Calculates the visible index range for a virtualized grid inside a scrollable.
    /// Returns (startIndex, endIndex) where endIndex is exclusive.
    /// </summary>
    public static (int startIndex, int endIndex) GetGridCellRange(
        WidgetId scrollId,
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
        _prevHotId = ElementTree._hotId;
        ElementTree._prevHotId = ElementTree._hotId;
        ElementTree._hotId = WidgetId.None;
        _valueChanged = false;

        _frame++;
        _refSize = GetRefSize();

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

        ElementTree.Begin(_size);
    }

    internal static void End()
    {
        var mouse = Camera!.ScreenToWorld(Input.MousePosition);
        MouseWorldPosition = mouse;
        ElementTree.MouseWorldPosition = mouse;
        ElementTree.End();

        Graphics.SetCamera(Camera);
        HandleInput();

        ElementTree.Draw();

#if DEBUG
        if (Input.IsButtonDownRaw(InputCode.KeyLeftCtrl) && Input.IsButtonDownRaw(InputCode.KeyF12))
        {
            Directory.CreateDirectory("temp");
            File.WriteAllText("temp/et_dump.txt", ElementTree.DebugDumpTree());
        }
#endif
    }



    public static void BeginCenter()
    {
        BeginContainerImpl(WidgetId.None, ContainerStyle.Center, -1);
    }

    public static void EndCenter() => EndContainerImpl();

    public static AutoFlex BeginFlex() => BeginFlex(1.0f);
    public static AutoFlex BeginFlex(float flex)
    {
        ElementTree.BeginFlex(flex);
        return new AutoFlex();
    }

    public static void EndFlex() => ElementTree.EndFlex();

    public static void Flex() => Flex(1.0f);
    public static void Flex(float flex) => ElementTree.Flex(flex);

    public static void Spacer(float size) => ElementTree.Spacer(size);

    public static void Separator(Color color, float thickness = 1)
    {
        if (ElementTree.IsParentRow())
            Container(new ContainerStyle { Width = thickness, Height = Size.Percent(1), Color = color });
        else
            Container(new ContainerStyle { Width = Size.Percent(1), Height = thickness, Color = color });
    }

    public static void BeginBorder(BorderStyle style)
    {
        int count = 0;
        if (style.Width > 0)
            { ElementTree.BeginBorder(style.Width, style.Color, style.Radius); count++; }
    }

    public static void EndBorder() => EndContainerImpl();

    public static AutoTransformed BeginTransformed(TransformStyle style)
    {
        ElementTree.BeginTransform(style.Origin, style.Translate, style.Rotate, style.Scale);
        return new AutoTransformed();
    }

    public static void EndTransformed() => EndContainerImpl();

    public static AutoOpacity BeginOpacity(float opacity)
    {
        ElementTree.BeginOpacity(opacity);
        return new AutoOpacity();
    }

    public static void EndOpacity() => EndContainerImpl();

    public static AutoCursor BeginCursor(Sprite sprite)
    {
        ElementTree.BeginCursor(sprite);
        return new AutoCursor();
    }

    public static AutoCursor BeginCursor(SystemCursor cursor)
    {
        ElementTree.BeginCursor(cursor);
        return new AutoCursor();
    }

    public static void EndCursor() => EndContainerImpl();

    public static AutoScrollable BeginScrollable(WidgetId id) =>
        BeginScrollable(id, new ScrollableStyle());

    public static AutoScrollable BeginScrollable(WidgetId id, in ScrollableStyle style)
    {
        Debug.Assert(id != 0);
        ref var state = ref ElementTree.BeginWidget<ScrollState>(id);
        ElementTree.BeginScrollable(ref state, in style);
        return new AutoScrollable();
    }

    public static void EndScrollable()
    {
        ElementTree.EndScrollable();
        ElementTree.EndWidget();
    }

    public static AutoGrid BeginGrid(GridStyle style)
    {
        ElementTree.BeginGrid(style.Spacing, style.Columns, style.CellWidth, style.CellHeight,
            style.CellMinWidth, style.CellHeightOffset, style.VirtualCount, style.StartIndex);
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

    public static void Scene(WidgetId id, Camera camera, Action draw) =>
        Scene(id, camera, draw, new SceneStyle());

    public static void Scene(WidgetId id, Camera camera, Action draw, SceneStyle style = default)
    {
        if (id != 0) ElementTree.BeginWidget(id);
        ElementTree.Scene(camera, draw, style.Size, style.Color, style.SampleCount);
        if (id != 0) ElementTree.EndWidget();
    }

    public static void Scene(
        Camera camera,
        Action draw,
        SceneStyle style = default) => Scene(WidgetId.None, camera, draw, style);

    public static AutoPopup BeginPopup(WidgetId id, PopupStyle style)
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

    // :text
    #region Text

    public static void Text(ReadOnlySpan<char> text) =>
        Text(text, new LabelStyle());

    public static void Text(string text) => Text(text.AsSpan(), new LabelStyle());

    public static void Text(string text, LabelStyle style) => Text(text.AsSpan(), style);

    public static void Text(ReadOnlySpan<char> text, LabelStyle style)
    {
        var resolved = style.Resolve != null ? style.Resolve(style, ElementTree.GetWidgetFlags()) : style;
        var font = resolved.Font ?? _defaultFont!;
        var fontSize = resolved.FontSize > 0 ? resolved.FontSize : 16f;
        ElementTree.Text(text, font, fontSize, resolved.Color, resolved.Align, resolved.Overflow);
    }

    #endregion

    // :image
    #region Image

    public static void Image(Sprite? sprite) => Image(sprite, new ImageStyle());

    public static void Image(Sprite? sprite, in ImageStyle style)
    {
        if (sprite == null) return;
        var resolved = style.Resolve != null ? style.Resolve(style, ElementTree.GetWidgetFlags()) : style;
        ElementTree.Image(sprite, resolved.Size, resolved.Stretch, resolved.Color, resolved.Scale, resolved.Align);
    }

    public static void Image(Texture texture) => Image(texture, new ImageStyle());

    public static void Image(Texture texture, in ImageStyle style)
    {
        var resolved = style.Resolve != null ? style.Resolve(style, ElementTree.GetWidgetFlags()) : style;
        ElementTree.Image(texture, resolved.Size, resolved.Stretch, resolved.Color, resolved.Scale, resolved.Align);
    }

    #endregion


    public static Rect WorldToSceneLocal(Camera camera, WidgetId sceneElementId, Rect worldRect)
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
}
