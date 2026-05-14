//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ.Editor;

internal struct CurveShape
{
    public VfxCurveType CurveType;
    public VfxEaseType EaseType;
    public float WindowBegin;
    public float WindowEnd;

    public readonly bool HasEase => EaseType != VfxEaseType.None;

    public static CurveShape From(in VfxDocFloatCurve c) => new()
    {
        CurveType = c.CurveType,
        EaseType = c.EaseType,
        WindowBegin = c.WindowBegin,
        WindowEnd = c.WindowEnd,
    };

    public static CurveShape From(in VfxDocColorCurve c) => new()
    {
        CurveType = c.CurveType,
        EaseType = c.EaseType,
        WindowBegin = c.WindowBegin,
        WindowEnd = c.WindowEnd,
    };

    public readonly void CopyTo(ref VfxDocFloatCurve c)
    {
        c.CurveType = CurveType;
        c.EaseType = EaseType;
        c.WindowBegin = WindowBegin;
        c.WindowEnd = WindowEnd;
    }

    public readonly void CopyTo(ref VfxDocColorCurve c)
    {
        c.CurveType = CurveType;
        c.EaseType = EaseType;
        c.WindowBegin = WindowBegin;
        c.WindowEnd = WindowEnd;
    }

    public readonly bool ShapeEquals(in CurveShape other) =>
        CurveType == other.CurveType &&
        EaseType == other.EaseType &&
        WindowBegin == other.WindowBegin &&
        WindowEnd == other.WindowEnd;
}

internal static partial class CurveEditorPopup
{
    private static partial class ElementId
    {
        public static partial WidgetId Popup { get; }
        public static partial WidgetId Preview { get; }
        public static partial WidgetId CurveType { get; }
        public static partial WidgetId EaseType { get; }
        public static partial WidgetId WindowBegin { get; }
        public static partial WidgetId WindowEnd { get; }
    }

    private enum DragHandle { None, WindowBegin, WindowEnd }

    private const float PreviewWidth = 320f;
    private const float PreviewHeight = 140f;
    private const float ThumbWidth = 64f;
    private const float ThumbHeight = 22f;
    private const float HandleHitDistance = 6f;
    private const int PolylineSegments = 64;

    private const float PreviewWorldMinX = -0.04f;
    private const float PreviewWorldMaxX = 1.04f;
    private const float PreviewWorldMinY = -0.15f;
    private const float PreviewWorldMaxY = 1.15f;

    private static WidgetId _popupId;
    private static CurveShape _shape;
    private static CurveShape _lastCommitted;
    private static PopupStyle _popupStyle;
    private static DragHandle _activeDrag;
    private static bool _changed;
    private static readonly Camera _previewCamera = new() { FlipY = true };

    private static bool _curveTypeChanged;
    private static VfxCurveType _curveTypeNewValue;
    private static bool _easeTypeChanged;
    private static VfxEaseType _easeTypeNewValue;

    private static readonly (string Name, VfxCurveType Type)[] CurveTypeOptions =
        Enum.GetValues<VfxCurveType>()
            .Select(t => (Enum.GetName(t)!, t))
            .ToArray();

    private static readonly (string Name, VfxEaseType Type)[] EaseTypeOptions =
        Enum.GetValues<VfxEaseType>()
            .Select(t => (Enum.GetName(t)!, t))
            .ToArray();

    public static bool IsOpen(WidgetId id) => _popupId == id;

    private static void Open(WidgetId id, in CurveShape shape)
    {
        _popupId = id;
        _shape = shape;
        _lastCommitted = shape;
        _activeDrag = DragHandle.None;
        _changed = false;
        _popupStyle = EditorStyle.PopupLeft;
        _popupStyle.AnchorRect = UI.GetElementWorldRect(id);
        _popupStyle.AutoClose = true;
        _popupStyle.Interactive = true;
    }

    private static void Close()
    {
        _popupId = WidgetId.None;
        _activeDrag = DragHandle.None;
    }

    public static void Update()
    {
        if (_popupId == WidgetId.None) return;

        if (Input.WasButtonPressed(InputCode.KeyEscape))
        {
            Input.ConsumeButton(InputCode.KeyEscape);
            Close();
            return;
        }

        using var cursor = UI.BeginCursor(new SpriteCursor(EditorAssets.Sprites.CursorArrow));
        using var popup = UI.BeginPopup(ElementId.Popup, _popupStyle);
        if (UI.IsClosed())
        {
            Close();
            return;
        }

        using (UI.BeginContainer(EditorStyle.Popup.Root with { Padding = EdgeInsets.All(8), Width = Size.Fit, Height = Size.Fit }))
        using (UI.BeginColumn(new ContainerStyle { Spacing = 6, Width = PreviewWidth, Height = Size.Fit }))
        {
            DrawPreview();
            EaseTypeDropdown();
            if (_shape.HasEase)
            {
                CurveTypeDropdown();
                WindowRow();
            }
        }

        if (!_shape.ShapeEquals(_lastCommitted))
        {
            _lastCommitted = _shape;
            _changed = true;
        }
    }

    public static bool PollCommit(WidgetId id, out CurveShape shape)
    {
        if (_popupId != id || !_changed)
        {
            shape = default;
            return false;
        }
        _changed = false;
        shape = _lastCommitted;
        return true;
    }

    public static bool Draw(WidgetId id, ref VfxDocFloatCurve curve)
    {
        var snapshot = CurveShape.From(curve);
        DrawButton(id, snapshot);
        if (PollCommit(id, out var commit))
        {
            commit.CopyTo(ref curve);
            return true;
        }
        return false;
    }

    public static bool Draw(WidgetId id, ref VfxDocColorCurve curve)
    {
        var snapshot = CurveShape.From(curve);
        DrawButton(id, snapshot);
        if (PollCommit(id, out var commit))
        {
            commit.CopyTo(ref curve);
            return true;
        }
        return false;
    }

    private static void DrawButton(WidgetId id, CurveShape snapshot)
    {
        ElementTree.BeginTree();
        ElementTree.BeginWidget(id);
        var flags = ElementTree.GetWidgetFlags();
        var bg = (flags & WidgetFlags.Hovered) != 0
            ? EditorStyle.Palette.Active
            : EditorStyle.Palette.Canvas;

        ElementTree.BeginSize(ThumbWidth, ThumbHeight);
        ElementTree.BeginFill(bg, EditorStyle.Control.BorderRadius);

        var shape = IsOpen(id) ? _shape : snapshot;
        UI.Scene(WidgetId.None, _previewCamera, () => DrawThumbScene(shape), new SceneStyle
        {
            Color = Color.Transparent,
            SampleCount = 4,
        });

        ElementTree.EndTree();
        ElementTree.SetLastWidget(id);

        if (flags.HasFlag(WidgetFlags.Pressed) && !IsOpen(id))
            Open(id, snapshot);
    }

    private static void DrawThumbScene(CurveShape shape)
    {
        var extents = Rect.FromMinMax(new Vector2(0f, -0.1f), new Vector2(1f, 1.1f));
        _previewCamera.SetExtents(extents);
        _previewCamera.Update();
        Graphics.SetCamera(_previewCamera);
        Graphics.SetTransform(Matrix3x2.Identity);
        Graphics.SetTexture(Graphics.WhiteTexture);
        Graphics.SetShader(EditorAssets.Shaders.Texture);
        Graphics.SetBlendMode(BlendMode.Alpha);

        var wpp = WorldPerPixel(extents);
        DrawWindowGuidesPx(shape, 1f, EditorStyle.Palette.Disabled, wpp);
        DrawPolylinePx(shape, 1.5f, EditorStyle.Palette.PrimaryHover, wpp);
    }

    private static void DrawPreview()
    {
        ElementTree.BeginTree();
        ElementTree.BeginWidget(ElementId.Preview);
        var flags = ElementTree.GetWidgetFlags();
        ElementTree.BeginSize(PreviewWidth, PreviewHeight);
        ElementTree.BeginFill(EditorStyle.Palette.Canvas, EditorStyle.Control.BorderRadius);

        UI.Scene(WidgetId.None, _previewCamera, DrawPreviewScene, new SceneStyle
        {
            Color = Color.Transparent,
            SampleCount = 4,
        });

        ElementTree.EndTree();

        HandlePreviewDrag(flags);
    }

    private static void DrawPreviewScene()
    {
        var extents = Rect.FromMinMax(
            new Vector2(PreviewWorldMinX, PreviewWorldMinY),
            new Vector2(PreviewWorldMaxX, PreviewWorldMaxY));
        _previewCamera.SetExtents(extents);
        _previewCamera.Update();
        Graphics.SetCamera(_previewCamera);
        Graphics.SetTransform(Matrix3x2.Identity);
        Graphics.SetTexture(Graphics.WhiteTexture);
        Graphics.SetShader(EditorAssets.Shaders.Texture);
        Graphics.SetBlendMode(BlendMode.Alpha);

        var wpp = WorldPerPixel(extents);
        DrawLinePx(new Vector2(0f, 0f), new Vector2(1f, 0f), 1f, EditorStyle.Palette.Separator, wpp);
        DrawLinePx(new Vector2(0f, 1f), new Vector2(1f, 1f), 1f, EditorStyle.Palette.Separator, wpp);

        DrawPolylinePx(_shape, 2.5f, EditorStyle.Palette.PrimaryHover, wpp);

        if (_shape.HasEase)
        {
            DrawHandle(_shape.WindowBegin, _activeDrag == DragHandle.WindowBegin, wpp);
            DrawHandle(_shape.WindowEnd, _activeDrag == DragHandle.WindowEnd, wpp);
        }
    }

    private static void DrawHandle(float t, bool active, Vector2 wpp)
    {
        var color = active ? EditorStyle.Palette.PrimaryHover : EditorStyle.Palette.SecondaryText;
        var width = active ? 2f : 1.5f;
        DrawLinePx(new Vector2(t, -0.05f), new Vector2(t, 1.05f), width, color, wpp);
    }

    private static void HandlePreviewDrag(WidgetFlags flags)
    {
        if (!_shape.HasEase) { _activeDrag = DragHandle.None; return; }

        var rect = UI.GetElementWorldRect(ElementId.Preview);
        if (rect.Width <= 0f) return;

        var mouse = UI.MouseWorldPosition;
        var camWorldWidth = PreviewWorldMaxX - PreviewWorldMinX;
        var rectFrac = (mouse.X - rect.X) / rect.Width;
        var worldX = PreviewWorldMinX + rectFrac * camWorldWidth;

        var hitTol = HandleHitDistance / rect.Width * camWorldWidth;
        var nearBegin = MathF.Abs(worldX - _shape.WindowBegin) < hitTol;
        var nearEnd = MathF.Abs(worldX - _shape.WindowEnd) < hitTol;

        if (flags.HasFlag(WidgetFlags.Pressed) && (nearBegin || nearEnd))
        {
            _activeDrag = nearBegin && (!nearEnd || MathF.Abs(worldX - _shape.WindowBegin) <= MathF.Abs(worldX - _shape.WindowEnd))
                ? DragHandle.WindowBegin
                : DragHandle.WindowEnd;
            UI.SetCapture(ElementId.Preview);
        }

        var capturing = UI.HasCapture(ElementId.Preview);
        var stillDown = capturing && Input.IsButtonDownRaw(InputCode.MouseLeft);
        if (stillDown && _activeDrag != DragHandle.None)
        {
            var t = Math.Clamp(worldX, 0f, 1f);
            if (_activeDrag == DragHandle.WindowBegin)
                _shape.WindowBegin = MathF.Min(t, _shape.WindowEnd);
            else
                _shape.WindowEnd = MathF.Max(t, _shape.WindowBegin);
            EditorCursor.SetSystem(SystemCursor.ResizeEW);
        }
        else
        {
            _activeDrag = DragHandle.None;
            if (flags.HasFlag(WidgetFlags.Hovered) && (nearBegin || nearEnd))
                EditorCursor.SetSystem(SystemCursor.ResizeEW);
        }
    }

    private static void CurveTypeDropdown()
    {
        if (_curveTypeChanged)
        {
            _curveTypeChanged = false;
            _shape.CurveType = _curveTypeNewValue;
        }

        using (Inspector.BeginProperty("Curve"))
        using (UI.BeginRow(new ContainerStyle { Spacing = 4, Height = Size.Fit, MinHeight = EditorStyle.Control.Height }))
        {
            var current = Enum.GetName(_shape.CurveType) ?? "Linear";
            UI.DropDown(ElementId.CurveType, () =>
            {
                var items = new PopupMenuItem[CurveTypeOptions.Length];
                for (var i = 0; i < CurveTypeOptions.Length; i++)
                {
                    var opt = CurveTypeOptions[i];
                    items[i] = PopupMenuItem.Item(opt.Name, () =>
                    {
                        _curveTypeChanged = true;
                        _curveTypeNewValue = opt.Type;
                    });
                }
                return items;
            }, text: current);
        }
    }

    private static void EaseTypeDropdown()
    {
        if (_easeTypeChanged)
        {
            _easeTypeChanged = false;
            _shape.EaseType = _easeTypeNewValue;
        }

        using (Inspector.BeginProperty("Ease"))
        using (UI.BeginRow(new ContainerStyle { Spacing = 4, Height = Size.Fit, MinHeight = EditorStyle.Control.Height }))
        {
            var current = Enum.GetName(_shape.EaseType) ?? "None";
            UI.DropDown(ElementId.EaseType, () =>
            {
                var items = new PopupMenuItem[EaseTypeOptions.Length];
                for (var i = 0; i < EaseTypeOptions.Length; i++)
                {
                    var opt = EaseTypeOptions[i];
                    items[i] = PopupMenuItem.Item(opt.Name, () =>
                    {
                        _easeTypeChanged = true;
                        _easeTypeNewValue = opt.Type;
                    });
                }
                return items;
            }, text: current);
        }
    }

    private static void WindowRow()
    {
        using (Inspector.BeginProperty("Window"))
        using (UI.BeginRow(new ContainerStyle { Spacing = 4, Height = Size.Fit, MinHeight = EditorStyle.Control.Height }))
        {
            float wb, we;
            using (UI.BeginFlex())
                wb = EditorUI.FloatInput(ElementId.WindowBegin, _shape.WindowBegin, EditorStyle.Inspector.TextBox, step: 0.1f, fineStep: 0.01f);
            using (UI.BeginFlex())
                we = EditorUI.FloatInput(ElementId.WindowEnd, _shape.WindowEnd, EditorStyle.Inspector.TextBox, step: 0.1f, fineStep: 0.01f);

            wb = Math.Clamp(wb, 0f, 1f);
            we = Math.Clamp(we, 0f, 1f);
            if (we < wb) we = wb;
            _shape.WindowBegin = wb;
            _shape.WindowEnd = we;
        }
    }

    private static Vector2 WorldPerPixel(Rect extents)
    {
        var screen = UI.SceneViewport?.Size ?? new Vector2Int(1, 1);
        var sx = screen.X > 0 ? screen.X : 1;
        var sy = screen.Y > 0 ? screen.Y : 1;
        return new Vector2(extents.Width / sx, extents.Height / sy);
    }

    private static Vector2 PerpPixel(Vector2 worldDelta, float pixelWidth, Vector2 wpp)
    {
        var ds = new Vector2(worldDelta.X / wpp.X, worldDelta.Y / wpp.Y);
        var len = ds.Length();
        if (len < 1e-4f) return Vector2.Zero;
        ds /= len;
        var perpScreen = new Vector2(-ds.Y, ds.X);
        return new Vector2(perpScreen.X * wpp.X, perpScreen.Y * wpp.Y) * (pixelWidth * 0.5f);
    }

    private static void DrawLinePx(Vector2 a, Vector2 b, float pixelWidth, Color color, Vector2 wpp)
    {
        var perp = PerpPixel(b - a, pixelWidth, wpp);
        if (perp == Vector2.Zero) return;
        Graphics.SetColor(color);
        Graphics.Draw(a - perp, a + perp, b + perp, b - perp);
    }

    private static void DrawPolylinePx(in CurveShape shape, float pixelWidth, Color color, Vector2 wpp)
    {
        if (!shape.HasEase) return;
        Graphics.SetColor(color);
        Vector2 prev = default;
        for (var i = 0; i <= PolylineSegments; i++)
        {
            var t = i / (float)PolylineSegments;
            var y = VfxDocument.SampleAuthored(shape.CurveType, shape.EaseType, shape.WindowBegin, shape.WindowEnd, t);
            var p = new Vector2(t, y);
            if (i > 0)
            {
                var perp = PerpPixel(p - prev, pixelWidth, wpp);
                if (perp != Vector2.Zero)
                    Graphics.Draw(prev - perp, prev + perp, p + perp, p - perp);
            }
            prev = p;
        }
    }

    private static void DrawWindowGuidesPx(in CurveShape shape, float pixelWidth, Color color, Vector2 wpp)
    {
        if (!shape.HasEase) return;
        DrawLinePx(new Vector2(shape.WindowBegin, 0f), new Vector2(shape.WindowBegin, 1f), pixelWidth, color, wpp);
        DrawLinePx(new Vector2(shape.WindowEnd, 0f), new Vector2(shape.WindowEnd, 1f), pixelWidth, color, wpp);
    }
}
