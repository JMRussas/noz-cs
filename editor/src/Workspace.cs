//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ.Editor;

public enum WorkspaceState
{
    Default,
    Edit
}

public static class Workspace
{
    private static void RebuildAll()
    {
        foreach (var doc in DocumentManager.Documents)
            Importer.QueueImport(doc, true);
    }

    private static void RegisterCommands()
    {
        CommandManager.RegisterCommon([
            new Command { Name = "Save All", ShortName = "save", Handler = DocumentManager.SaveAll, Key = InputCode.KeyS, Ctrl = true },
            new Command { Name = "Undo", ShortName = "undo", Handler = () => Undo.DoUndo(), Key = InputCode.KeyZ, Ctrl = true },
            new Command { Name = "Redo", ShortName = "redo", Handler = () => Undo.DoRedo(), Key = InputCode.KeyY, Ctrl = true },
            new Command { Name = "Increase UI Scale", ShortName = "ui+", Handler = IncreaseUIScale, Key = InputCode.KeyEquals, Ctrl = true },
            new Command { Name = "Decrease UI Scale", ShortName = "ui-", Handler = DecreaseUIScale, Key = InputCode.KeyMinus, Ctrl = true },
            new Command { Name = "Reset UI Scale", ShortName = "ui0", Handler = ResetUIScale, Key = InputCode.Key0, Ctrl = true },
            new Command { Name = "Command Palette", ShortName = "palette", Handler = OpenCommandPalette, Key = InputCode.KeyP, Ctrl = true, Shift = true },
            new Command { Name = "Toggle Edit Mode", ShortName = "edit", Handler = ToggleEdit, Key = InputCode.KeyTab },
            new Command { Name = "Toggle Grid", ShortName = "grid", Handler = ToggleGrid, Key = InputCode.KeyQuote, Ctrl = true },
        ]);

        CommandManager.RegisterWorkspace([
            new Command { Name = "Frame Selected", ShortName = "frame", Handler = FrameSelected, Key = InputCode.KeyF },
            new Command { Name = "Move Selected", ShortName = "move", Handler = BeginMoveTool, Key = InputCode.KeyG },
            new Command { Name = "Toggle Names", ShortName = "names", Handler = ToggleNames, Key = InputCode.KeyN, Alt = true },
            new Command { Name = "Rebuild All", ShortName = "build", Handler = RebuildAll },
            new Command { Name = "Frame Selected", ShortName = "origin", Handler = FrameOrigin }
        ]);
    }

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
    private static bool _showNames;

    private static Vector2 _mousePosition;
    private static Vector2 _mouseWorldPosition;
    private static Vector2 _dragPosition;
    private static Vector2 _panPositionCamera;
    private static bool _isDragging;
    private static bool _dragStarted;
    private static InputCode _dragButton;
    private static bool _clearSelectionOnRelease;

    private static Document? _activeDocument;
    private static DocumentEditor? _activeEditor;
    private static Texture? _whiteTexture;

    public static Camera Camera => _camera;
    public static float Zoom => _zoom;
    public static bool ShowGrid => _showGrid;
    public static bool ShowNames => _showNames || Input.IsAltDown();
    public static Vector2 MousePosition => _mousePosition;
    public static Vector2 MouseWorldPosition => _mouseWorldPosition;
    public static bool IsDragging => _isDragging;
    public static bool DragStarted => _dragStarted;
    public static InputCode DragButton => _dragButton;
    public static Vector2 DragDelta { get; private set; }
    public static Vector2 DragWorldDelta { get; private set; }
    public static Vector2 DragWorldPosition { get; private set; }
    public static WorkspaceState State { get; private set; } = WorkspaceState.Default;
    public static int SelectedCount { get; private set; }
    public static Tool? ActiveTool { get; private set; }

    public static float GetUIScale() => Application.Platform.DisplayScale * _userUIScale;

    public static void IncreaseUIScale()
    {
        UI.UserScale = Math.Clamp(UI.UserScale + UIScaleStep, UIScaleMin, UIScaleMax);
    }

    public static void DecreaseUIScale()
    {
        UI.UserScale = Math.Clamp(UI.UserScale - UIScaleStep, UIScaleMin, UIScaleMax);
    }

    public static void ResetUIScale()
    {
        UI.UserScale = 1f;
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

        RegisterCommands();
        UpdateCamera();

        Render.ClearColor = EditorStyle.Workspace.Color;

        // Create a 1x1 white texture for untextured draws
        byte[] white = [255, 255, 255, 255];
        _whiteTexture = Texture.Create(1, 1, white, name: "white");
    }

    public static void Shutdown()
    {
        _whiteTexture?.Dispose();
        _whiteTexture = null;

        Input.PopInputSet();
    }

    public static void LoadUserSettings(PropertySet props)
    {
        _camera.Position = props.GetVector2("view", "camera_position", Vector2.Zero);
        _zoom = props.GetFloat("view", "camera_zoom", ZoomDefault);
        _showGrid = props.GetBool("view", "show_grid", true);
        _showNames = props.GetBool("view", "show_names", false);
        _userUIScale = props.GetFloat("view", "ui_scale", 1f);
        UpdateCamera();
    }

    public static void SaveUserSettings(PropertySet props)
    {
        props.SetVec2("view", "camera_position", _camera.Position);
        props.SetFloat("view", "camera_zoom", _zoom);
        props.SetBool("view", "show_grid", _showGrid);
        props.SetBool("view", "show_names", _showNames);
        props.SetFloat("view", "ui_scale", _userUIScale);
    }

    
    public static void Update()
    {
        UpdateCamera();

        if (!CommandPalette.IsEnabled)
        {
            CommandManager.ProcessShortcuts();

            UpdateMouse();
            UpdatePan();
            UpdateZoom();

            if (State == WorkspaceState.Default)
            {
                UpdateDefaultState();
                UpdateToolAutoStart();
            }

            ActiveTool?.Update();
        }

        UpdateCulling();

        if (EditorAssets.Shaders.Sprite is Shader spriteShader)
            Render.SetShader(spriteShader);

        Render.SetTexture(_whiteTexture!);
        Render.SetBlendMode(BlendMode.Alpha);
        Render.SetCamera(_camera);

        DrawDocuments();

        if (_showGrid)
            Grid.Draw(_camera);

        if (ShowNames)
            DrawNames();

        ActiveTool?.Draw();

        if (Workspace.State == WorkspaceState.Edit && Workspace.ActiveEditor != null)
            Workspace.ActiveEditor.Update();
    }

    private static void UpdateToolAutoStart()
    {
        if (ActiveTool != null)
            return;

        if (_dragStarted && _dragButton == InputCode.MouseLeft)
            BeginTool(new BoxSelectTool(CommitBoxSelect));
    }

    private static void CommitBoxSelect(Rect bounds)
    {
        if (!Input.IsShiftDown())
            ClearSelection();

        foreach (var doc in DocumentManager.Documents)
        {
            if (!doc.Loaded || !doc.PostLoaded)
                continue;

            var docBounds = doc.Bounds.Offset(doc.Position);
            if (bounds.Intersects(docBounds))
                SetSelected(doc, true);
        }
    }

    public static void UpdateUI()
    {
        Workspace.ActiveEditor?.UpdateUI();
    }

    private static void UpdateCulling()
    {
        var cameraBounds = _camera.WorldBounds;
        foreach (var doc in DocumentManager.Documents)
        {
            var docBounds = new Rect(
                doc.Position.X + doc.Bounds.X,
                doc.Position.Y + doc.Bounds.Y,
                doc.Bounds.Width,
                doc.Bounds.Height);
            doc.IsClipped = !cameraBounds.Intersects(docBounds);
        }
    }
    
    private static void DrawDocuments()
    {
        Render.PushState();
        Render.SetLayer(EditorLayer.Document);

        foreach (var doc in DocumentManager.Documents)
        {
            if (!doc.Loaded || !doc.PostLoaded)
                continue;

            if (doc.IsEditing || doc.IsClipped)
                continue;

            if (doc.IsSelected)
            {
                Render.PushState();
                Render.SetLayer(EditorLayer.Selection);
                Render.SetColor(EditorStyle.SelectionColor);
                Render.SetTransform(doc.Transform);
                Gizmos.DrawRect(doc, EditorStyle.Workspace.BoundsLineWidth);
                Render.PopState();
            }

            doc.Draw();
        }

        Render.PopState();
    }

    private static void DrawNames()
    {
        if (EditorAssets.Fonts.Seguisb is not Font font)
            return;

        Render.PushState();
        Render.SetLayer(EditorLayer.Names);
        Render.SetColor(EditorStyle.TextColor);

        var scale = 1f / _zoom;
        var fontSize = EditorStyle.Workspace.NameSize * scale;
        var padding = EditorStyle.Workspace.NamePadding * scale;

        foreach (var doc in DocumentManager.Documents)
        {
            if (!doc.Loaded || !doc.PostLoaded || doc.IsClipped)
                continue;

            var bounds = doc.Bounds.Offset(doc.Position);
            var textSize = TextRender.Measure(doc.Name, font, fontSize);
            var textX = bounds.Center.X - textSize.X * 0.5f;
            var textY = bounds.Bottom + padding;
            Render.SetTransform(Matrix3x2.CreateTranslation(textX, textY));
            TextRender.Draw(doc.Name, font, fontSize);
        }

        Render.PopState();
    }

    private static void BeginMoveTool()
    {
        if (SelectedCount == 0 || ActiveTool != null || State != WorkspaceState.Default)
            return;

        foreach (var doc in DocumentManager.Documents)
        {
            if (doc.IsSelected)
                doc.SavedPosition = doc.Position;
        }

        BeginTool(new MoveTool(
            update: delta =>
            {
                var snap = Input.IsCtrlDown();
                foreach (var doc in DocumentManager.Documents)
                {
                    if (!doc.IsSelected)
                        continue;
                    var newPos = doc.SavedPosition + delta;
                    newPos = snap ? Grid.SnapToGrid(newPos) : Grid.SnapToPixelGrid(newPos);
                    doc.Position = newPos;
                }
            },
            commit: _ =>
            {
                foreach (var doc in DocumentManager.Documents)
                {
                    if (!doc.IsSelected)
                        continue;
                    if (doc.Position != doc.SavedPosition)
                        doc.MarkMetaModified();
                }
            },
            cancel: () =>
            {
                foreach (var doc in DocumentManager.Documents)
                {
                    if (doc.IsSelected)
                        doc.Position = doc.SavedPosition;
                }
            }
        ));
    }

    private static void OpenCommandPalette()
    {
        if (CommandPalette.IsEnabled)
            return;

        CommandPalette.Begin(new CommandInputOptions
        {
            Prefix = ":",
            Placeholder = "Enter command..."
        });
    }

    private static void ToggleGrid()
    {
        _showGrid = !_showGrid;
    }

    private static void ToggleNames()
    {
        _showNames = !_showNames;
    }

    private static void UpdateCamera()
    {
        var effectiveDpi = _dpi * _uiScale * _userUIScale * _zoom;
        var screenSize = Application.WindowSize;
        var worldWidth = screenSize.X / effectiveDpi;
        var worldHeight = screenSize.Y / effectiveDpi;
        var halfWidth = worldWidth * 0.5f;
        var halfHeight = worldHeight * 0.5f;

        _camera.SetExtents(new Rect(-halfWidth, -halfHeight, worldWidth, worldHeight));
        _camera.Update();
    }

    private static void UpdateMouseDrag()
    {
        if (Input.WasButtonReleased(_dragButton))
        {
            EndDrag();
            return;
        }
        
        DragDelta = _mousePosition - _dragPosition;
        DragWorldDelta = _mouseWorldPosition - DragWorldPosition;
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
            DragWorldPosition = _mouseWorldPosition;
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
            DragWorldPosition = _mouseWorldPosition;
        }

        DragDelta = _mousePosition - _dragPosition;
        DragWorldDelta = _mouseWorldPosition - DragWorldPosition;

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
            var worldDelta = _camera.ScreenToWorld(DragDelta) - _camera.ScreenToWorld(Vector2.Zero);
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
        _zoom = Math.Clamp(screenSize.Y / (_dpi * _uiScale * _userUIScale * targetWorldHeight), ZoomMin, ZoomMax);

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
        if (SelectedCount == 0)
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
        SelectedCount = 0;
    }

    public static void SetSelected(Document doc, bool selected)
    {
        if (doc.IsSelected == selected)
            return;

        doc.IsSelected = selected;
        SelectedCount += selected ? 1 : -1;
    }

    public static void ToggleSelected(Document doc)
    {
        doc.IsSelected = !doc.IsSelected;
        SelectedCount += doc.IsSelected ? 1 : -1;
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

    private static void ToggleEdit()
    {
        if (State == WorkspaceState.Edit)
        {
            EndEdit();
            return;
        }

        if (SelectedCount != 1)
            return;

        var doc = GetFirstSelected();
        if (doc == null || !doc.Def.CanEdit)
            return;

        _activeDocument = doc;
        _activeEditor = doc.Def.EditorFactory!(doc);
        doc.IsEditing = true;
        State = WorkspaceState.Edit;

        CommandManager.RegisterEditor(_activeEditor.GetCommands());
    }

    public static void EndEdit()
    {
        if (_activeDocument == null)
            return;

        CommandManager.RegisterEditor(null);

        _activeEditor?.Dispose();
        _activeEditor = null;
        _activeDocument.IsEditing = false;
        _activeDocument = null;
        State = WorkspaceState.Default;

        DocumentManager.SaveAll();
    }

    public static DocumentEditor? ActiveEditor => _activeEditor;

    public static void BeginTool(Tool tool)
    {
        if (ActiveTool != null)
            return;

        ActiveTool = tool;
        ActiveTool.Begin();
    }

    public static void EndTool()
    {
        if (ActiveTool == null)
            return;

        ActiveTool.End();
        ActiveTool = null;
    }

    public static void CancelTool()
    {
        if (ActiveTool == null)
            return;

        ActiveTool.Cancel();
        ActiveTool = null;
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
