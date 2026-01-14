//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace noz.editor;

public enum WorkspaceState
{
    Default,
    Edit
}

public static class Workspace
{
    private const float ZoomMin = 0.01f;
    private const float ZoomMax = 200f;
    private const float ZoomStep = 0.1f;
    private const float ZoomDefault = 1f;
    private const float DefaultDpi = 72f;
    private const float DragMinDistance = 5f;
    private const float UIScaleMin = 0.5f;
    private const float UIScaleMax = 3f;
    private const float UIScaleStep = 0.1f;

    private static Camera _camera = null!;
    private static InputSet _input = null!;

    private static float _zoom = ZoomDefault;
    private static float _dpi = DefaultDpi;
    private static float _uiScale = 1f;
    private static float _userUIScale = 1f;
    private static bool _showGrid = true;

    private static Vector2 _mousePosition;
    private static Vector2 _mouseWorldPosition;
    private static Vector2 _dragPosition;
    private static Vector2 _dragWorldPosition;
    private static Vector2 _panPositionCamera;
    private static Vector2 _dragDelta;
    private static Vector2 _dragWorldDelta;
    private static bool _isDragging;
    private static bool _dragStarted;
    private static InputCode _dragButton;
    private static bool _clearSelectionOnRelease;

    private static WorkspaceState _state = WorkspaceState.Default;
    private static Document? _activeDocument;
    private static int _selectedCount;

    public static Camera Camera => _camera;
    public static float Zoom => _zoom;
    public static bool ShowGrid => _showGrid;
    public static Vector2 MousePosition => _mousePosition;
    public static Vector2 MouseWorldPosition => _mouseWorldPosition;
    public static bool IsDragging => _isDragging;
    public static bool DragStarted => _dragStarted;
    public static InputCode DragButton => _dragButton;
    public static Vector2 DragDelta => _dragDelta;
    public static Vector2 DragWorldDelta => _dragWorldDelta;
    public static float UserUIScale => _userUIScale;
    public static WorkspaceState State => _state;
    public static Document? ActiveDocument => _activeDocument;
    public static int SelectedCount => _selectedCount;

    public static float GetUIScale() => Application.Platform.DisplayScale * _userUIScale;

    public static Vector2Int GetRefSize()
    {
        var screenSize = Application.WindowSize;
        var scale = GetUIScale();
        return new Vector2Int(
            (int)(screenSize.X / scale),
            (int)(screenSize.Y / scale)
        );
    }

    public static void IncreaseUIScale()
    {
        _userUIScale = Math.Clamp(_userUIScale + UIScaleStep, UIScaleMin, UIScaleMax);
    }

    public static void DecreaseUIScale()
    {
        _userUIScale = Math.Clamp(_userUIScale - UIScaleStep, UIScaleMin, UIScaleMax);
    }

    public static void ResetUIScale()
    {
        _userUIScale = 1f;
    }

    public static void Init()
    {
        _camera = new Camera();
        _zoom = ZoomDefault;
        _dpi = DefaultDpi;
        _uiScale = 1f;
        _showGrid = true;

        _input = new InputSet("Workspace");
        Input.PushInputSet(_input);

        UpdateCamera();

        Grid.Init();

        Render.ClearColor = EditorStyle.WorkspaceColor;
    }

    public static void Shutdown()
    {
        Input.PopInputSet();
        Grid.Shutdown();
    }

    public static void Update()
    {
        UpdateCamera();
        UpdateMouse();
        UpdatePan();
        UpdateZoom();

        if (_state == WorkspaceState.Default)
            UpdateDefaultState();

        Grid.Update(_camera);
    }

    public static void Draw()
    {
        if (EditorAssets.Shaders.Sprite is Shader spriteShader)
            Render.BindShader(spriteShader);

        Render.BindCamera(_camera);

        if (_showGrid)
            Grid.Draw(_camera);
    }

    public static void DrawOverlay()
    {
        if (_showGrid)
            Grid.DrawPixelGrid(_camera);
    }

    public static void ToggleGrid()
    {
        _showGrid = !_showGrid;
    }

    public static void SetZoom(float zoom)
    {
        _zoom = Math.Clamp(zoom, ZoomMin, ZoomMax);
        UpdateCamera();
    }

    public static void SetDpi(float dpi)
    {
        _dpi = dpi > 0 ? dpi : DefaultDpi;
        Grid.SetDpi((int)_dpi);
        UpdateCamera();
    }

    private static void UpdateCamera()
    {
        var effectiveDpi = _dpi * _uiScale * _zoom;
        var screenSize = Application.WindowSize;
        var worldWidth = screenSize.X / effectiveDpi;
        var worldHeight = screenSize.Y / effectiveDpi;
        var halfWidth = worldWidth * 0.5f;
        var halfHeight = worldHeight * 0.5f;

        _camera.SetExtents(-halfWidth, halfWidth, -halfHeight, halfHeight);
        _camera.Update();
    }

    private static void UpdateMouseDrag()
    {
        if (Input.WasButtonReleased(_dragButton))
        {
            EndDrag();
            return;
        }
        
        _dragDelta = _mousePosition - _dragPosition;
        _dragWorldDelta = _mouseWorldPosition - _dragWorldPosition;
    }
    
    private static void UpdateMouse()
    {
        _mousePosition = Input.MousePosition;
        _mouseWorldPosition = _camera.ScreenToWorld(_mousePosition);
        _dragStarted = false;

        if (_isDragging) {
            UpdateMouseDrag();
            return;
        }

        if (Input.WasButtonPressed(InputCode.MouseLeft) || Input.WasButtonPressed(InputCode.MouseRight))
        {
            _dragPosition = _mousePosition;
            _dragWorldPosition = _mouseWorldPosition;
        }
        else if (Input.IsButtonDown(InputCode.MouseLeft) &&
                 Vector2.Distance(_mousePosition, _dragPosition) >= DragMinDistance)
        {
            BeginDrag(InputCode.MouseLeft);
        }
        else if (Input.IsButtonDown(InputCode.MouseRight) &&
                 Vector2.Distance(_mousePosition, _dragPosition) >= DragMinDistance)
        {
            BeginDrag(InputCode.MouseRight);
        }
    }

    private static void BeginDrag(InputCode button)
    {
        if (!Input.IsButtonDown(button))
        {
            _dragPosition = _mousePosition;
            _dragWorldPosition = _mouseWorldPosition;
        }

        _dragDelta = _mousePosition - _dragPosition;
        _dragWorldDelta = _mouseWorldPosition - _dragWorldPosition;

        _isDragging = true;
        _dragStarted = true;
        _dragButton = button;
    }

    private static void EndDrag()
    {
        _isDragging = false;
        _dragStarted = false;
        _dragButton = InputCode.None;
    }

    private static void UpdatePan()
    {
        if (Input.WasButtonPressed(InputCode.MouseRight))
        {
            _panPositionCamera = _camera.Position;
        }

        if (_isDragging && _dragButton == InputCode.MouseRight)
        {
            var worldDelta = _camera.ScreenToWorld(_dragDelta) - _camera.ScreenToWorld(Vector2.Zero);
            _camera.Position = _panPositionCamera - worldDelta;
        }
    }

    private static void UpdateZoom()
    {
        var scrollDelta = Input.GetAxis(InputCode.MouseScrollY);
        if (scrollDelta > -0.5f && scrollDelta < 0.5f)
            return;

        var mouseScreen = Input.MousePosition;
        var worldUnderCursor = _camera.ScreenToWorld(mouseScreen);

        var zoomFactor = 1f + scrollDelta * ZoomStep;
        _zoom *= zoomFactor;
        _zoom = Math.Clamp(_zoom, ZoomMin, ZoomMax);

        UpdateCamera();

        var worldUnderCursorAfter = _camera.ScreenToWorld(mouseScreen);
        var worldOffset = worldUnderCursor - worldUnderCursorAfter;
        _camera.Position += worldOffset;

        UpdateCamera();
    }

    public static void FrameView(Rect bounds)
    {
        const float frameViewPercentage = 1f / 0.75f;

        var center = bounds.Center;
        var size = bounds.Size;
        var maxDimension = MathF.Max(size.X, size.Y);
        if (maxDimension < ZoomMin)
            maxDimension = ZoomMin;

        var screenSize = Application.WindowSize;
        var targetWorldHeight = maxDimension * frameViewPercentage;
        _zoom = Math.Clamp(screenSize.Y / (_dpi * _uiScale * targetWorldHeight), ZoomMin, ZoomMax);

        _camera.Position = center;
        UpdateCamera();
    }

    public static void FrameOrigin()
    {
        _camera.Position = Vector2.Zero;
        _zoom = ZoomDefault;
        UpdateCamera();
    }

    public static void FrameSelected()
    {
        if (_selectedCount == 0)
            return;

        Rect? bounds = null;
        foreach (var doc in DocumentManager.Documents)
        {
            if (!doc.IsSelected)
                continue;

            var docBounds = doc.Bounds.Offset(doc.Position);
            bounds = bounds == null ? docBounds : Rect.Union(bounds.Value, docBounds);
        }

        if (bounds != null)
            FrameView(bounds.Value);
    }

    public static Document? HitTestDocuments(Vector2 point)
    {
        Document? firstHit = null;
        for (var i = DocumentManager.Documents.Count - 1; i >= 0; i--)
        {
            var doc = DocumentManager.Documents[i];
            if (!doc.Loaded || !doc.PostLoaded)
                continue;

            if (!doc.Bounds.Offset(doc.Position).Contains(point))
                continue;

            firstHit ??= doc;
            if (!doc.IsSelected)
                return doc;
        }
        return firstHit;
    }

    public static void ClearSelection()
    {
        foreach (var doc in DocumentManager.Documents)
            doc.IsSelected = false;
        _selectedCount = 0;
    }

    public static void SetSelected(Document doc, bool selected)
    {
        if (doc.IsSelected == selected)
            return;

        doc.IsSelected = selected;
        _selectedCount += selected ? 1 : -1;
    }

    public static void ToggleSelected(Document doc)
    {
        doc.IsSelected = !doc.IsSelected;
        _selectedCount += doc.IsSelected ? 1 : -1;
    }

    public static Document? GetFirstSelected()
    {
        foreach (var doc in DocumentManager.Documents)
        {
            if (doc.IsSelected)
                return doc;
        }
        return null;
    }

    public static void ToggleEdit()
    {
        if (_state == WorkspaceState.Edit)
        {
            EndEdit();
            return;
        }

        if (_selectedCount != 1)
            return;

        var doc = GetFirstSelected();
        if (doc == null || !doc.CanEdit())
            return;

        _activeDocument = doc;
        doc.IsEditing = true;
        _state = WorkspaceState.Edit;
        doc.BeginEdit();
    }

    public static void EndEdit()
    {
        if (_activeDocument == null)
            return;

        _activeDocument.EndEdit();
        _activeDocument.IsEditing = false;
        _activeDocument = null;
        _state = WorkspaceState.Default;

        DocumentManager.SaveAll();
    }

    public static void UpdateDefaultState()
    {
        if (Input.WasButtonPressed(InputCode.MouseLeft))
        {
            var hitDoc = HitTestDocuments(_mouseWorldPosition);
            if (hitDoc != null)
            {
                _clearSelectionOnRelease = false;
                if (Input.IsShiftDown())
                    ToggleSelected(hitDoc);
                else
                {
                    ClearSelection();
                    SetSelected(hitDoc, true);
                }
                return;
            }
            _clearSelectionOnRelease = !Input.IsShiftDown();
        }

        if (Input.WasButtonReleased(InputCode.MouseLeft) && _clearSelectionOnRelease)
        {
            ClearSelection();
        }
    }
}
