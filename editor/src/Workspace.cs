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

public static partial class Workspace
{
    private static partial class WidgetIds
    {
        public static partial WidgetId Menu { get; }
        public static partial WidgetId ProjectView { get; }
        public static partial WidgetId ToggleEditMode { get; }
        public static partial WidgetId Toolbar { get; }
        public static partial WidgetId XrayButton { get; }
        public static partial WidgetId CollectionButton { get; }
        public static partial WidgetId ContextMenu { get; }
        public static partial WidgetId InspectorToggle { get; }
        public static partial WidgetId Scene { get; }
        public static partial WidgetId SceneViewport { get; }
        public static partial WidgetId ReferencesButton { get; }
        public static partial WidgetId SyncButton { get; }
        public static partial WidgetId InspectorSplitter { get; }
        public static partial WidgetId OutlinerSplitter { get; }
        public static partial WidgetId RenameTextBox { get; }
        public static partial WidgetId IsolationButton { get; }
    }

    private const float ZoomMin = 0.2f;
    private const float ZoomMax = 200f;
    private const float ZoomStep = 0.1f;
    private const float ZoomDefault = 1f;
    private const float DefaultDpi = 72f;
    private const float DragMinDistance = 5f;

    public static DocumentEditor? ActiveEditor => _activeEditor;
    public static Document? ActiveDocument => _activeDocument;

    private static Camera _camera = null!;

    private static Vector2 _lastMousePosition;
    private static float _zoom = ZoomDefault;
    private static float _inspectorSize = 300f;
    private static float _outlinerSize = 200f;
    private static float _dpi = DefaultDpi;
    private static float _uiScale = 1f;
    private static bool _showFps;
    private static bool _showGrid = true;
    private static bool _showNames;
    private static bool _showReferences;
    private static bool _showProject = true;
    private static bool _showInspector = true;
    private static bool _isolation;
    private static Vector2 _savedCameraPosition;
    private static float _savedCameraZoom;
    private static float _savedCameraRotation;
    private static bool _hasSavedCamera;
    private static Vector2 _popupWorldPosition;
    private static float _powerModeTimer;

    private static Vector2 _mousePosition;
    private static Vector2 _mouseWorldPosition;
    private static Vector2 _penPosition;
    private static Vector2 _penWorldPosition;
    private static Vector2 _dragPosition;
    private static Vector2 _panPositionCamera;
    private static bool _isDragging;
    private static bool _dragStarted;
    private static bool _wasDragging;
    private static InputCode _dragButton;
    private static bool _clearSelectionOnRelease;
    private static WorkspaceState _pendingState;

    private static Document? _activeDocument;
    private static DocumentEditor? _activeEditor;
    private static PopupMenuItem[] _workspacePopupItems = null!;
    private static PopupMenuItem[] _hamburgerMenuItems = null!;

    public static Camera Camera => _camera;
    public static WidgetId SceneWidgetId => WidgetIds.Scene;
    public static bool XrayMode { get; set; }
    public static float XrayAlpha => XrayMode ? EditorStyle.Workspace.XrayAlpha : 1f;
    public static float Zoom => _zoom;
    public static bool ShowGrid => _showGrid;
    public static bool ShowNames => _showNames;
    public static bool ShowProject => _showProject;
    public static bool ShowInspector => _showInspector;
    public static Vector2 MousePosition => _mousePosition;
    public static Vector2 MouseWorldPosition => _mouseWorldPosition;
    public static Vector2 PenPosition => _penPosition;
    public static Vector2 PenWorldPosition => _penWorldPosition;
    public static bool IsDragging => _isDragging;
    public static bool WasDragging => _wasDragging;
    public static bool DragStarted => _dragStarted;
    public static bool ShowFps => _showFps;

    public static Color32 ReadPixelAtMouse() => ReadPixelAt(MouseWorldPosition);

    public static Color32 ReadPixelAt(Vector2 worldPos)
    {
        // In isolation mode, only sample from the active editor's document
        if (IsIsolationActive)
        {
            if (_activeDocument != null)
                return _activeDocument.GetPixelAt(worldPos);
            return default;
        }

        // Sample from visible documents, front-to-back
        for (var i = Project.Documents.Count - 1; i >= 0; i--)
        {
            var doc = Project.Documents[i];
            if (!doc.Loaded || !doc.PostLoaded) continue;
            if (doc.IsClipped) continue;
            if (!CollectionManager.IsDocumentVisible(doc)) continue;
            if (!doc.Bounds.Translate(doc.Position).Contains(worldPos)) continue;

            var color = doc.GetPixelAt(worldPos);
            if (color.A > 0)
                return color;
        }

        return default;
    }
    public static InputCode DragButton => _dragButton;
    public static Vector2 DragDelta { get; private set; }
    public static Vector2 DragWorldDelta { get; private set; }
    public static Vector2 DragWorldPosition { get; private set; }
    public static WorkspaceState State { get; private set; } = WorkspaceState.Default;

    private static bool IsIsolationActive => State == WorkspaceState.Edit && !_isolation;
    public static bool Isolation => _isolation;
    public static int SelectedCount { get; private set; }

    // Workspace inline drag state (replacing Tool pattern)
    private enum WorkspaceDragMode { None, BoxSelect, MoveDocuments }
    private static WorkspaceDragMode _workspaceDragMode;
    private static bool _wsMoveCommitOnRelease;
    private static Vector2 _wsMoveStartWorld;

    // Inline rename state
    private static bool _isRenaming;
    private static Document? _renameTarget;
    private static string _renameOriginalName = "";
    private static string _renameCurrentText = "";

    public static event Action<bool>? XrayModeChanged;

    public static float GetUIScale() => Application.Platform.DisplayScale * UI.UserScale;

    public static void Init()
    {
        _camera = new Camera();
        _camera.Rotation = 0f;
        _zoom = ZoomDefault;
        _dpi = DefaultDpi;
        _uiScale = 1f;
        _showGrid = true;
        _pendingState = WorkspaceState.Default;
    
        Cursor.Enabled = !Application.IsTablet;

        InitCommands();
        UpdateCamera();

        Project.DocumentAdded += _ => InvalidateProjectView();
        Project.DocumentRemoved += _ => InvalidateProjectView();
        Project.DocumentRenamed += (_, _) => InvalidateProjectView();

        Graphics.ClearColor = EditorStyle.Workspace.FillColor;
    }

    public static void Shutdown()
    {
    }

    private static void GenerateSelected()
    {
        if (SelectedCount == 0)
            return;

        var count = 0;
        foreach (var doc in Project.Documents)
        {
            if (!doc.IsSelected)
                continue;
            if (doc is GeneratedSpriteDocument sprite)
            {
                sprite.GenerateAsync();
                count++;
            }
        }

        if (count == 0)
            Log.Warning("No selected sprites have generation config");
    }

    private static void GenerateSelectedRandomSeed()
    {
        if (SelectedCount == 0)
            return;

        var count = 0;
        foreach (var doc in Project.Documents)
        {
            if (!doc.IsSelected)
                continue;
            if (doc is GeneratedSpriteDocument sprite)
            {
                sprite.Generation.Seed = SpriteGeneration.GenerateRandomSeed();
                sprite.GenerateAsync();
                count++;
            }
        }

        if (count == 0)
            Log.Warning("No selected sprites have generation config");
    }

    private static void GenerateManifest()
    {
        AssetManifest.Generate(force: true);
    }   

    private static void ExportAll()
    {
        foreach (var doc in Project.Documents)
            Project.QueueExport(doc, true);

        AssetManifest.Generate(force: true);
    }

    private static void CreateNewDocument(Document? doc)
    {
        if (doc == null) return;
        doc.CollectionId = CollectionManager.GetVisibleId();
        doc.IncrementVersion();
        ClearSelection();
        SetSelected(doc, true);
    }

    private static void RebuildAtlas()
    {
        // Force a fresh pack of all sprites; export pass will write the binary if dirty.
        AtlasManager.MarkAllDirty();
        AtlasManager.Update();
        Project.SaveAll();
    }

    private static void InitCommands()
    {
        var renameCommand = new Command("Rename", BeginRenameTool, [InputCode.KeyF2]);
        var deleteCommand = new Command("Delete", DeleteSelected, [InputCode.KeyX, InputCode.KeyDelete], EditorAssets.Sprites.IconDelete);
        var duplicateCommand = new Command("Duplicate", DuplicateSelected, [new KeyBinding(InputCode.KeyD, ctrl: true)], EditorAssets.Sprites.IconDuplicate);
        var editCommand = new Command("Edit", BeginEdit, [InputCode.KeyTab], EditorAssets.Sprites.IconEdit);
        var settingsCommand = new Command("Settings", OpenSettings, [new KeyBinding(InputCode.KeyComma, ctrl:true)]);

        CommandManager.RegisterCommon([
            new Command("Save All", Project.SaveAll, [new KeyBinding(InputCode.KeyS, ctrl:true)]),
            new Command("Undo", () => Undo.DoUndo(), [new KeyBinding(InputCode.KeyZ, ctrl:true)]),
            new Command("Redo", () => Undo.DoRedo(), [new KeyBinding(InputCode.KeyY, ctrl:true)]),
            new Command("Increase UI Scale", EditorApplication.IncreaseUIScale, [new KeyBinding(InputCode.KeyEquals, ctrl:true)]),
            new Command("Decrease UI Scale", EditorApplication.DecreaseUIScale, [new KeyBinding(InputCode.KeyMinus, ctrl:true)]),
            new Command("Reset UI Scale", EditorApplication.ResetUIScale, [new KeyBinding(InputCode.Key0, ctrl:true)]),
            new Command("Command Palette", CommandPalette.Open, [new KeyBinding(InputCode.KeyP, ctrl:true, shift:true)]),
            new Command("Asset Browser", () => AssetPalette.Open(), [new KeyBinding(InputCode.KeyP, ctrl:true)], EditorAssets.Sprites.IconSearch),
            new Command("Browse Sprites", () => AssetPalette.OpenSprites()),
            new Command("Toggle Grid", ToggleShowGrid, [new KeyBinding(InputCode.KeyQuote, ctrl:true)]),
            new Command("Toggle Names", ToggleShowNames, [new KeyBinding(InputCode.KeyN, alt:true)]),
            new Command("Toggle Isolation", ToggleIsolation, [new KeyBinding(InputCode.KeySlash)]),
            new Command("Toggle FPS", ToggleShowFps),
            settingsCommand,
        ]);

        var workspaceCommands = new List<Command>
        {
            editCommand,
            renameCommand,
            deleteCommand,
            duplicateCommand,
            new("Select All", SelectAll, [new KeyBinding(InputCode.KeyA)]),
            new("Frame", FrameSelected, [new KeyBinding(InputCode.KeyF)]),
            new("Generate Selected", GenerateSelected, [new KeyBinding(InputCode.KeyG, ctrl:true)]),
            new("Generate Selected (Random Seed)", GenerateSelectedRandomSeed, [new KeyBinding(InputCode.KeyG, ctrl:true, shift:true)]),
            new("Export All", ExportAll),
            new("Generate Manifest", GenerateManifest),
            new("Play/Stop", Play, [new KeyBinding(InputCode.KeySpace)]),
            new("Rebuild Atlas", RebuildAtlas),
        };

        EditorApplication.AppConfig.RegisterCommands?.Invoke(workspaceCommands);

        for (var i = 1; i <= 9; i++)
        {
            var index = i;
            workspaceCommands.Add(new Command($"Collection {i}", () => SetCollection(index), [InputCode.Key0 + i]));
            workspaceCommands.Add(new Command($"Move to Collection {i}", () => MoveSelectedToCollection(index), [new KeyBinding(InputCode.Key0 + i, ctrl:true)]));
        }

        CommandManager.RegisterWorkspace([.. workspaceCommands]);

        _workspacePopupItems = [        
            PopupMenuItem.Submenu("New"),
            PopupMenuItem.Submenu("Sprite", level: 1),
            PopupMenuItem.Item("Vector", () => CreateNewDocument(VectorSpriteDocument.CreateNew(_popupWorldPosition)), level: 2, icon: EditorAssets.Sprites.AssetIconSprite),
            PopupMenuItem.Item("Pixel", () => CreateNewDocument(PixelDocument.CreateNew(_popupWorldPosition)), level: 2, icon: EditorAssets.Sprites.AssetIconSprite),
            PopupMenuItem.Item("Generated", () => CreateNewDocument(GeneratedSpriteDocument.CreateNew(_popupWorldPosition)), level: 2, icon: EditorAssets.Sprites.AssetIconGenstyle),
            PopupMenuItem.Item("Skeleton", () => CreateNewDocument(SkeletonDocument.CreateNew(position: _popupWorldPosition)), level: 1, icon: EditorAssets.Sprites.IconBone),
            PopupMenuItem.Item("Animation", () => CreateNewDocument(AnimationDocument.CreateNew(position: _popupWorldPosition)), level: 1, icon: EditorAssets.Sprites.AssetIconAnimation),
            PopupMenuItem.Item("VFX", () => CreateNewDocument(VfxDocument.CreateNew(position: _popupWorldPosition)), level: 1, icon: EditorAssets.Sprites.AssetIconVfx),
            PopupMenuItem.Item("Sound", () => CreateNewDocument(SoundDocument.CreateNew(position: _popupWorldPosition)), level: 1, icon: EditorAssets.Sprites.AssetIconSound),
            PopupMenuItem.Item("Scene", () => CreateNewDocument(SceneDocument.CreateNew(position: _popupWorldPosition)), level: 1, icon: EditorAssets.Sprites.AssetIconSprite),
            PopupMenuItem.Item("Palette", () => CreateNewDocument(PaletteDocument.CreateNew(position: _popupWorldPosition)), level: 1),
            PopupMenuItem.Item("Gen Config", () => CreateNewDocument(GenerationConfig.CreateNew(position: _popupWorldPosition)), level: 1, icon: EditorAssets.Sprites.AssetIconGenstyle),
            PopupMenuItem.Separator(),
            PopupMenuItem.Submenu("Move to Collection", showChecked: true, showIcons: false),
            ..CollectionManager.Collections.Select(c =>
                PopupMenuItem.Item(c.Name, () => MoveSelectedToCollection(c.Index), level: 1, isChecked: () => c.Index == CollectionManager.VisibleIndex)),
            PopupMenuItem.Separator(),
            editCommand.ToPopupMenuItem(enabled: () => SelectedCount == 1),
            duplicateCommand.ToPopupMenuItem(),
            renameCommand.ToPopupMenuItem(),
            deleteCommand.ToPopupMenuItem()
        ];

        _hamburgerMenuItems = [
            settingsCommand.ToPopupMenuItem(),
        ];
    }

    public static void LoadUserSettings(PropertySet props)
    {
        _showFps = props.GetBool("workspace", "show_fps", false);
        _showGrid = props.GetBool("workspace", "show_grid", true);
        _showNames = props.GetBool("workspace", "show_names", false);
        _showProject = props.GetBool("workspace", "show_project", true);
        _showInspector = props.GetBool("workspace", "show_inspector", true);
        _isolation = props.GetBool("workspace", "isolation", false);
        _inspectorSize = props.GetFloat("workspace", "inspector_size", 300f);
        _outlinerSize = props.GetFloat("workspace", "outliner_size", 200f);

        // Restore camera position and zoom from visible collection
        var collection = CollectionManager.VisibleCollection;
        if (collection != null)
        {
            _camera.Position = collection.CameraPosition;
            _zoom = collection.CameraZoom;
            _camera.Rotation = collection.CameraRotation;
        }

        UpdateCamera();
    }

    public static void SaveUserSettings(PropertySet props)
    {
        var collection = CollectionManager.VisibleCollection;
        if (collection != null)
        {
            collection.CameraPosition = _hasSavedCamera ? _savedCameraPosition : _camera.Position;
            collection.CameraZoom = _hasSavedCamera ? _savedCameraZoom : _zoom;
            collection.CameraRotation = _hasSavedCamera ? _savedCameraRotation : _camera.Rotation;
        }

        props.SetBool("workspace", "show_fps", _showFps);
        props.SetBool("workspace", "show_grid", _showGrid);
        props.SetBool("workspace", "show_names", _showNames);
        props.SetBool("workspace", "show_project", _showProject);
        props.SetBool("workspace", "show_inspector", _showInspector);
        props.SetBool("workspace", "isolation", _isolation);        
        props.SetFloat("workspace", "inspector_size", _inspectorSize);
        props.SetFloat("workspace", "outliner_size", _outlinerSize);
    }
    
    public static void Update()
    {
        UpdatePowerMode();

        ActiveEditor?.PreUpdate();

        UpdateState();
        UpdateCamera();

        if (!CommandPalette.IsOpen && !AssetPalette.IsOpen && !ConfirmDialog.IsVisible)
        {
            if (!UI.HasHot())
                CommandManager.ProcessShortcuts();

            if (Touch.WasThreeFingerTapped)
                Undo.DoRedo();
            else if (Touch.WasTwoFingerTapped)
                Undo.DoUndo();

            if (Touch.WasDoubleTapped)
                BeginEdit();

            UpdateMouse();
            UpdatePan();
            UpdateZoom();

            if (State == WorkspaceState.Default)
            {
                if (_isRenaming)
                    UpdateRenameInput();
                else
                {
                    UpdateDefaultState();
                    if (_workspaceDragMode != WorkspaceDragMode.None)
                        UpdateWorkspaceDrag();
                    else
                        UpdateToolAutoStart();
                    UpdateCursor();
                }
            }

            if (Input.WasButtonReleased(InputCode.MouseRight) && !_wasDragging)
                OpenPopupMenu();

        }

        UpdateCulling();
    }

    private static void UpdatePowerMode()
    {
        var mode = PowerMode.Balanced;

        if (Input.IsButtonDownRaw(InputCode.MouseLeft) ||
            Input.IsButtonDownRaw(InputCode.MouseRight) ||
            Input.IsButtonDownRaw(InputCode.Pen) ||
            _lastMousePosition != _mousePosition ||
            Touch.IsTouching)
            mode = PowerMode.Performance;

        if (ActiveEditor != null && ActiveEditor.PowerMode > mode)
            mode = ActiveEditor.PowerMode;

        if (mode < PowerMode.Performance && VfxSystem.ActiveInstanceCount > 0)
            mode = PowerMode.Performance;

        if (mode == Application.PowerMode)
            return;

        if (mode < Application.PowerMode)
        {
            if (_powerModeTimer <= 0)
            {
                _powerModeTimer = 1.0f;
                return;
            }                    
            else
            {
                _powerModeTimer -= Time.DeltaTime;
                if (_powerModeTimer > 0)
                    return;
            }
        }

        _powerModeTimer = 0.0f;
        Application.PowerMode = mode;
    }

    private static void DrawScene()
    {
        var isolation = IsIsolationActive;

        if (!isolation)
            DrawDocuments();

        if (_showGrid)
            Grid.Draw(_camera);

        if (!isolation && ShowNames)
            DrawNames();

        if (!isolation && _showReferences)
            DrawReferences();

        DrawWorkspaceDrag();

        if (State == WorkspaceState.Edit && ActiveEditor != null)
            ActiveEditor.Update();

        VfxSystem.Draw();

        PostProcess.Bloom(threshold: 1.05f, intensity: 0.5f);
    }

    private static void UpdateToolAutoStart()
    {
        if (_workspaceDragMode != WorkspaceDragMode.None)
            return;

        if (_dragStarted && _dragButton == InputCode.MouseLeft)
        {
            if (HitTestSelected(DragWorldPosition))
            {
                _clearSelectionOnRelease = false;
                BeginInlineDragMove(commitOnRelease: true);
            }
            else
            {
                _workspaceDragMode = WorkspaceDragMode.BoxSelect;
            }
        }
    }

    private static void UpdateWorkspaceDrag()
    {
        switch (_workspaceDragMode)
        {
            case WorkspaceDragMode.BoxSelect:
                if (!_isDragging || Input.WasButtonReleased(InputCode.MouseLeft, InputScope.All))
                {
                    var p0 = DragWorldPosition;
                    var p1 = _mouseWorldPosition;
                    var bounds = Rect.FromMinMax(Vector2.Min(p0, p1), Vector2.Max(p0, p1));
                    Input.ConsumeButton(InputCode.MouseLeft);
                    CommitBoxSelect(bounds);
                    _workspaceDragMode = WorkspaceDragMode.None;
                }
                break;

            case WorkspaceDragMode.MoveDocuments:
                if (Input.WasButtonPressed(InputCode.KeyEscape, InputScope.All) ||
                    Input.WasButtonPressed(InputCode.MouseRight, InputScope.All))
                {
                    // Cancel: restore saved positions
                    foreach (var doc in Project.Documents)
                        if (doc.IsSelected)
                            doc.Position = doc.SavedPosition;
                    Undo.Cancel();
                    _workspaceDragMode = WorkspaceDragMode.None;
                }
                else if (_wsMoveCommitOnRelease
                    ? Input.WasButtonReleased(InputCode.MouseLeft, InputScope.All)
                    : Input.WasButtonPressed(InputCode.MouseLeft, InputScope.All))
                {
                    // Commit
                    UpdateInlineMoveDocuments();
                    Input.ConsumeButton(InputCode.MouseLeft);
                    _workspaceDragMode = WorkspaceDragMode.None;
                }
                else
                {
                    UpdateInlineMoveDocuments();
                }
                break;
        }
    }

    private static void BeginInlineDragMove(bool commitOnRelease = true)
    {
        _wsMoveCommitOnRelease = commitOnRelease;
        _wsMoveStartWorld = _mouseWorldPosition;
        Undo.BeginGroup();
        foreach (var doc in Project.Documents)
        {
            if (doc.IsSelected)
            {
                Undo.Record(doc);
                doc.SavedPosition = doc.Position;
            }
        }
        Undo.EndGroup();
        _workspaceDragMode = WorkspaceDragMode.MoveDocuments;
    }

    private static void UpdateInlineMoveDocuments()
    {
        var delta = _mouseWorldPosition - _wsMoveStartWorld;
        foreach (var doc in Project.Documents)
        {
            if (!doc.IsSelected) continue;
            var newPos = doc.SavedPosition + delta;
            if (Input.IsSnapModifierDown(InputScope.All))
                newPos = Grid.SnapToGrid(newPos);
            doc.Position = Grid.SnapToPixelGrid(newPos);
        }
    }

    private static void DrawWorkspaceDrag()
    {
        if (_workspaceDragMode == WorkspaceDragMode.BoxSelect)
        {
            var p0 = DragWorldPosition;
            var p1 = _mouseWorldPosition;
            var rect = Rect.FromMinMax(Vector2.Min(p0, p1), Vector2.Max(p0, p1));

            using var _ = Gizmos.PushState(EditorLayer.Tool);
            Graphics.SetColor(EditorStyle.BoxSelect.FillColor);
            Graphics.Draw(rect);
            Graphics.SetColor(EditorStyle.BoxSelect.LineColor);
            Gizmos.DrawRect(rect, EditorStyle.BoxSelect.LineWidth);
        }
    }

    private static void UpdateCursor()
    {
        if (_isDragging || _workspaceDragMode != WorkspaceDragMode.None)
            return;

        if (HitTestSelected(_mouseWorldPosition))
            EditorCursor.SetMove();
    }

    private static bool HitTestSelected(Vector2 point)
    {
        foreach (var doc in Project.Documents)
        {
            if (!doc.IsSelected || !doc.Loaded || !doc.PostLoaded)
                continue;
            if (doc.Bounds.Translate(doc.Position).Contains(point))
                return true;
        }
        return false;
    }

    private static void CommitBoxSelect(Rect bounds)
    {
        if (!Input.IsShiftDown())
            ClearSelection();

        foreach (var doc in Project.Documents)
        {
            if (!doc.Loaded || !doc.PostLoaded)
                continue;
            if (!CollectionManager.IsDocumentVisible(doc))
                continue;

            var docBounds = doc.Bounds.Translate(doc.Position);
            if (bounds.Intersects(docBounds))
                SetSelected(doc, true);
        }
    }

    private static void ToolbarUI()
    {
        using var cursor = UI.BeginCursor(new SpriteCursor(EditorAssets.Sprites.CursorArrow));
        using var _ = UI.BeginRow(WidgetIds.Toolbar, EditorStyle.Toolbar.Root);

        if (UI.Button(WidgetIds.Menu, EditorAssets.Sprites.IconMenu, EditorStyle.Button.ToggleIcon, isSelected: UI.IsPopupMenuOpen(WidgetIds.Menu)))  
            UI.OpenPopupMenu(
                WidgetIds.Menu,
                _hamburgerMenuItems,
                style: EditorStyle.ContextMenu.Style,
                popupStyle: EditorStyle.PopupBelow with { AnchorRect = UI.GetElementWorldRect(WidgetIds.Menu) });

        if (ActiveEditor != null)
        {
            ActiveEditor.ToolbarUI();
            return;
        }

        if (UI.Button(WidgetIds.ProjectView, _showProject?EditorAssets.Sprites.IconFolderOpen:EditorAssets.Sprites.IconFolder, EditorStyle.Button.ToggleIcon, isSelected: _showProject))  
            _showProject = !_showProject;               

        using (UI.BeginEnabled(SelectedCount > 0))
            if (UI.Button(WidgetIds.ToggleEditMode, EditorAssets.Sprites.IconEdit, EditorStyle.Button.ToggleIcon))  
                BeginEdit();

        UI.Flex();

        static PopupMenuItem[] GetCollectionItems()
        {
            var items = new PopupMenuItem[CollectionManager.Collections.Count];
            for (int i = 0; i < CollectionManager.Collections.Count; i++)
            {
                var collection = CollectionManager.Collections[i];
                items[i] = PopupMenuItem.Item(
                    collection.Name,
                    handler: () => SetCollection(collection.Index),
                    isChecked: () => CollectionManager.VisibleIndex == collection.Index);
            }

            return items;
        }

        if (UI.Button(WidgetIds.ReferencesButton, EditorAssets.Sprites.IconConnected, EditorStyle.Button.ToggleIcon, isSelected: _showReferences))
            _showReferences = !_showReferences;

        if (UI.Button(WidgetIds.XrayButton, EditorAssets.Sprites.IconXray, EditorStyle.Button.ToggleIcon, isSelected: XrayMode))
        {
            XrayMode = !XrayMode;
            XrayModeChanged?.Invoke(XrayMode);
        }

        var sync = EditorApplication.Sync;
        if (sync != null)
        {
            if (sync.IsSyncing)
            {
                using (UI.BeginContainer(new ContainerStyle { Width = EditorStyle.Control.Height, Height = EditorStyle.Control.Height, Padding = 3}))
                using (UI.BeginTransform(new TransformStyle { Rotate = Time.TotalTime * 360f }))
                    UI.Image(EditorAssets.Sprites.IconRefresh, new ImageStyle { Width = EditorStyle.Icon.LargeSize, Height = EditorStyle.Icon.LargeSize, Color = EditorStyle.Palette.Disabled, Align = Align.Center });
            }
            else
            {
                if (UI.Button(WidgetIds.SyncButton, EditorAssets.Sprites.IconRefresh, EditorStyle.Button.IconOnly))
                    Task.Run(() => sync.SyncAsync());
            }
        }

        UI.DropDown(
            WidgetIds.ContextMenu,
            text: CollectionManager.VisibleCollection?.Name ?? "None",
            icon: EditorAssets.Sprites.IconCollection,
            getItems: GetCollectionItems);

        EditorUI.PanelSeparator();

        if (UI.Button(WidgetIds.InspectorToggle, EditorAssets.Sprites.IconInfo, EditorStyle.Button.ToggleIcon, isSelected: _showInspector))  
            _showInspector = !_showInspector;
    }

    public static void UpdateUI()
    {
        using (UI.BeginContainer())
        {
            EditorCursor.Begin();
            UI.Scene(WidgetIds.Scene, Camera, DrawScene, new SceneStyle
            {
                Color = EditorStyle.Palette.Canvas,
                SampleCount = 4
            });
            EditorCursor.End();
        }

        using (UI.BeginColumn())
        {
            ToolbarUI();
            EditorUI.PanelSeparator();

            using (UI.BeginFlex())
            using (UI.BeginRow())
            {
                if (ActiveEditor?.ShowOutliner ?? ShowProject)
                {                    
                    using (UI.BeginFlex())
                        Outliner.UpdateUI();

                    UI.FlexSplitter(WidgetIds.OutlinerSplitter, ref _outlinerSize,
                        EditorStyle.Inspector.Splitter, fixedPane: 1);
                }

                using (UI.BeginFlex())
                using (UI.BeginRow())
                {
                    // content (center, flexible)
                    using (UI.BeginFlex())
                    {
                        if (_showFps)
                            using (UI.BeginContainer(new ContainerStyle {AlignX=Align.Max}))
                            using (UI.BeginRow(new ContainerStyle { Align = Align.Center, Width = Size.Fit, Spacing = EditorStyle.Control.Spacing }))
                            {
                                UI.Text(Strings.Number((int)Time.AvergeFps), EditorStyle.Text.Disabled);
                                UI.Text("fps", EditorStyle.Text.Disabled);
                            }

                        ElementTree.BeginWidget(WidgetIds.SceneViewport, interactive: false);
                        ActiveEditor?.UpdateUI();
                        ElementTree.EndWidget();
                    }

                    if (ActiveEditor?.ShowInspector ?? ShowInspector)
                    {
                        // inspector (right, fixed width)
                        UI.FlexSplitter(WidgetIds.InspectorSplitter, ref _inspectorSize,
                            EditorStyle.Inspector.Splitter, fixedPane: 2);

                        using (UI.BeginFlex())
                            Inspector.UpdateUI();                        
                    }
                }
            }
        }

        ActiveEditor?.UpdateOverlayUI();
        ColorPicker.Update();
        CurveEditorPopup.Update();
        DrawRenameUI();
        SettingsPopup.Update();
    }

    public static void LateUpdate()
    {
        Workspace.ActiveEditor?.LateUpdate();
    }

    private static void UpdateCulling()
    {
        var cameraBounds = _camera.WorldBounds;
        foreach (var doc in Project.Documents)
        {
            if (!CollectionManager.IsDocumentVisible(doc))
            {
                doc.IsClipped = true;
                continue;
            }

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
        Graphics.PushState();
        Graphics.SetLayer(EditorLayer.Document);

        for (int i = 0, c = Project.Count; i < c; i++)
        {
            var doc = Project.GetAt(i);
            if (!doc.Loaded || !doc.PostLoaded)
                continue;
            if (doc.IsEditing || doc.IsClipped)
                continue;
            if (!CollectionManager.IsDocumentVisible(doc))
                continue;

            if (doc.IsSelected)
                doc.DrawBounds(selected:true);

            Graphics.SetTransform(doc.Transform);
            doc.Draw();
        }

        Graphics.PopState();
    }

    private static readonly List<Document> _refBuffer = [];

    private static void DrawReferences()
    {
        using var _ = Gizmos.PushState(EditorLayer.Names);
        Graphics.SetColor(EditorStyle.Workspace.SelectionColor);

        foreach (var doc in Project.Documents)
        {
            if (!doc.Loaded || !doc.PostLoaded || doc.IsClipped) continue;
            if (!CollectionManager.IsDocumentVisible(doc)) continue;

            _refBuffer.Clear();
            doc.GetReferences(_refBuffer);

            foreach (var refDoc in _refBuffer)
            {
                if (refDoc.IsClipped) continue;
                Gizmos.DrawLine(doc.Position, refDoc.Position, EditorStyle.Workspace.DocumentBoundsLineWidth);
            }
        }
    }

    private static void DrawNames()
    {
        using var _ = Graphics.PushState();

        var font = EditorAssets.Fonts.Seguisb;
        var fontSize = EditorStyle.Workspace.NameSize * Gizmos.ZoomRefScale;
        var padding = EditorStyle.Workspace.NamePadding * Gizmos.ZoomRefScale;
        var renamingDoc = _isRenaming ? _renameTarget : null;

        Graphics.SetLayer(EditorLayer.Names);
        TextRender.SetOutline(EditorStyle.Workspace.NameOutlineColor, EditorStyle.Workspace.NameOutline, 0.5f); 

        for (int i = 0, c = Project.Count; i < c; i++)
        {
            var doc = Project.GetAt(i);
            if (!doc.Loaded || !doc.PostLoaded) continue;
            if (doc.IsClipped) continue;
            if (!CollectionManager.IsDocumentVisible(doc)) continue;
            if (doc == renamingDoc) continue;
            if (doc.IsEditing) continue;

            var bounds = doc.Bounds.Translate(doc.Position);
            var textSize = TextRender.Measure(doc.Name, font, fontSize);
            var textX = bounds.Center.X - textSize.X * 0.5f;
            var textY = bounds.Bottom + padding - textSize.Y * 0.5f;
            Graphics.SetTransform(Matrix3x2.CreateTranslation(textX, textY));
            Graphics.SetColor(doc.IsSelected
                ? EditorStyle.Workspace.SelectedNameColor
                : EditorStyle.Workspace.NameColor);
            TextRender.Draw(doc.Name, font, fontSize, order: doc.IsSelected ? 1 : 0);
        }

        TextRender.ClearOutline();
    }


    public static void ToggleShowFps()
    {
        _showFps = !_showFps;
    }

    public static void ToggleShowGrid()
    {
        _showGrid = !_showGrid;
    }

    public static void ToggleShowNames()
    {
        _showNames = !_showNames;
    }

    public static void ToggleIsolation()
    {
        _isolation = !_isolation;

        if (IsIsolationActive)
        {
            _savedCameraPosition = _camera.Position;
            _savedCameraZoom = _zoom;
            _savedCameraRotation = _camera.Rotation;
            _hasSavedCamera = true;
        }
        else
        {
            _hasSavedCamera = false;
        }
    }

    private static void BeginRenameTool()
    {
        if (SelectedCount != 1 || _isRenaming || State != WorkspaceState.Default)
            return;

        var doc = GetFirstSelected();
        if (doc == null)
            return;

        _isRenaming = true;
        _renameTarget = doc;
        _renameOriginalName = doc.Name;
        _renameCurrentText = doc.Name;
        UI.SetHot(WidgetIds.RenameTextBox);
    }

    private static void CommitRename()
    {
        if (_renameTarget != null && !string.IsNullOrWhiteSpace(_renameCurrentText) && _renameCurrentText != _renameOriginalName)
        {
            if (!Project.Rename(_renameTarget, _renameCurrentText))
                Log.Warning("rename failed");
        }
        EndRename();
    }

    private static void EndRename()
    {
        _isRenaming = false;
        _renameTarget = null;
        UI.ClearHot();
    }

    private static void UpdateRenameInput()
    {
        if (Input.WasButtonPressed(InputCode.KeyEscape, InputScope.All))
        {
            Input.ConsumeButton(InputCode.KeyEscape);
            EndRename();
            return;
        }

        if (Input.WasButtonPressed(InputCode.KeyEnter, InputScope.All))
        {
            Input.ConsumeButton(InputCode.KeyEnter);
            CommitRename();
        }
    }

    private static void DrawRenameUI()
    {
        if (!_isRenaming || _renameTarget == null)
            return;

        var padding = EditorStyle.Workspace.NamePadding / Zoom;
        var bounds = _renameTarget.Bounds.Translate(_renameTarget.Position);
        var worldPos = new Vector2(bounds.Center.X, bounds.Bottom + padding);

        var textStyle = EditorStyle.RenameTool.Text;
        var font = textStyle.Font ?? UI.DefaultFont;
        var textSize = TextRender.Measure(_renameCurrentText.AsSpan(), font, textStyle.FontSize);
        var contentPadding = EditorStyle.RenameTool.Content.Padding;
        var textInputHeight = textStyle.Height.IsFixed ? textStyle.Height.Value : textStyle.FontSize * 1.8f;

        var screenPos = Camera.WorldToScreen(worldPos);
        var uiPos = UI.ScreenToUI(screenPos);
        var border = EditorStyle.RenameTool.Content.BorderWidth;
        uiPos.X -= textSize.X * 0.5f + contentPadding.L + border;
        uiPos.Y -= textInputHeight * 0.5f + contentPadding.T + border;

        using (UI.BeginContainer(EditorStyle.RenameTool.Content with { AlignX = Align.Min, AlignY = Align.Min, Margin = EdgeInsets.TopLeft(uiPos.Y, uiPos.X) }))
        {
            _renameCurrentText = UI.TextInput(WidgetIds.RenameTextBox, _renameCurrentText, textStyle);

            if (UI.HotEnter())
                UI.SetWidgetText(WidgetIds.RenameTextBox, _renameOriginalName, selectAll: true);

            if (UI.HotExit())
                CommitRename();
        }
    }

    private static void UpdateCamera()
    {
        if (UI.Camera == null)
            return;

        var effectiveDpi = _dpi * _uiScale * UI.UserScale * _zoom;

        var sceneWorldRect = UI.GetElementWorldRect(WidgetIds.Scene);
        var sceneScreenRect = UI.Camera!.WorldToScreen(sceneWorldRect);
        var sceneWidth = sceneScreenRect.Width;
        var sceneHeight = sceneScreenRect.Height;

        if (sceneWidth <= 0 || sceneHeight <= 0)
        {
            var screenSize = Application.WindowSize;
            sceneWidth = screenSize.X;
            sceneHeight = screenSize.Y;
            _camera.Viewport = default;
        }
        else
        {
            _camera.Viewport = new Rect(sceneScreenRect.X, sceneScreenRect.Y, sceneWidth, sceneHeight);
        }

        var worldWidth = sceneWidth / effectiveDpi;
        var worldHeight = sceneHeight / effectiveDpi;
        var halfWidth = worldWidth * 0.5f;
        var halfHeight = worldHeight * 0.5f;

        _camera.SetExtents(new Rect(-halfWidth, -halfHeight, worldWidth, worldHeight));
        _camera.Update();
    }

    private static void UpdateMouseDrag()
    {
        if (Input.WasButtonReleased(_dragButton, InputScope.All))
        {
            EndDrag();
            return;
        }
        
        DragDelta = _mousePosition - _dragPosition;
        DragWorldDelta = _mouseWorldPosition - DragWorldPosition;
    }
    
    private static void UpdateMouse()
    {
        _lastMousePosition = _mousePosition;
        _mousePosition = Input.MousePosition;
        _mouseWorldPosition = _camera.ScreenToWorld(_mousePosition);
        if (Application.IsTablet)
        {
            _penPosition = Input.PenPosition;
            _penWorldPosition = _camera.ScreenToWorld(_penPosition);
        }
        else
        {
            _penPosition = _mousePosition;
            _penWorldPosition = _mouseWorldPosition;
        }

        if (Input.IsButtonDown(InputCode.Pen, InputScope.All))
        {
            _mouseWorldPosition = _penWorldPosition;
            _mousePosition = _penPosition;
        }

        _dragStarted = false;
        _wasDragging = false;

        if (_isDragging) {
            UpdateMouseDrag();
            return;
        }

        if (Input.WasButtonPressed(InputCode.MouseLeft) || Input.WasButtonPressed(InputCode.MouseRight, InputScope.All))
        {
            _dragPosition = _mousePosition;
            DragWorldPosition = _mouseWorldPosition;
        }
        else if (Input.IsButtonDown(InputCode.MouseLeft) &&
                 Vector2.Distance(_mousePosition, _dragPosition) >= DragMinDistance)
        {
            BeginDrag(InputCode.MouseLeft);
        }
        else if (Input.IsButtonDown(InputCode.MouseRight, InputScope.All) &&
                 Vector2.Distance(_mousePosition, _dragPosition) >= DragMinDistance)
        {
            BeginDrag(InputCode.MouseRight);
        }
    }

    private static void BeginDrag(InputCode button)
    {
        if (!Input.IsButtonDown(button, InputScope.All))
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
        _wasDragging = true;
        _isDragging = false;
        _dragStarted = false;
        _dragButton = InputCode.None;
    }

    private static void UpdatePan()
    {
        if (Input.WasButtonPressed(InputCode.MouseRight, InputScope.All))
        {
            _panPositionCamera = _camera.Position;
        }

        if (_isDragging && _dragButton == InputCode.MouseRight)
        {
            var worldDelta = _camera.ScreenToWorld(DragDelta) - _camera.ScreenToWorld(Vector2.Zero);
            _camera.Position = _panPositionCamera - worldDelta;
        }

        if (Touch.IsTwoFingerPanning)
        {
            var worldDelta = _camera.ScreenToWorld(Touch.TwoFingerDelta) - _camera.ScreenToWorld(Vector2.Zero);
            _camera.Position -= worldDelta;
            UpdateCamera();
        }
    }

    private static void UpdateZoom()
    {
        var scrollDelta = Input.GetAxis(InputCode.MouseScrollY);
        if (scrollDelta > -0.5f && scrollDelta < 0.5f)
        {
            // Two-finger rotation anchored at the same midpoint as pan/zoom.
            if (Touch.IsTwoFingerPanning && Touch.TwoFingerRotation != 0f)
            {
                var worldUnderCenter = _camera.ScreenToWorld(Touch.TwoFingerCenter);
                _camera.Rotation -= Touch.TwoFingerRotation;
                UpdateCamera();
                var worldUnderCenterAfter = _camera.ScreenToWorld(Touch.TwoFingerCenter);
                _camera.Position += worldUnderCenter - worldUnderCenterAfter;
                UpdateCamera();
            }

            // No scroll wheel — check for a two-finger gesture.
            // Touchscreen: unified pan+zoom from our own finger tracking.
            // Trackpad: SDL3's native pinch recognizer, anchored at the cursor.
            float scale;
            Vector2 pinchScreen;
            if (Touch.IsTwoFingerPanning)
            {
                scale = Touch.TwoFingerScale;
                pinchScreen = Touch.TwoFingerCenter;
            }
            else if (Touch.IsPinching)
            {
                scale = Touch.PinchScale;
                pinchScreen = Input.MousePosition;
            }
            else
            {
                return;
            }

            if (scale == 1f) return;

            var worldUnderPinch = _camera.ScreenToWorld(pinchScreen);

            _zoom *= scale;
            _zoom = Math.Clamp(_zoom, ZoomMin, ZoomMax);

            UpdateCamera();

            var worldUnderPinchAfter = _camera.ScreenToWorld(pinchScreen);
            _camera.Position += worldUnderPinch - worldUnderPinchAfter;

            UpdateCamera();
            return;
        }

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

    public static void FrameRect(Rect bounds)
    {
        const float padding = 1.25f;

        var center = bounds.Center;
        var size = bounds.Size * padding;

        if (size.X < ZoomMin) size.X = ZoomMin;
        if (size.Y < ZoomMin) size.Y = ZoomMin;

        var screenSize = Application.WindowSize;
        var viewportUI = UI.GetElementWorldRect(WidgetIds.SceneViewport);
        Rect viewport;
        if (viewportUI.Width > 0 && viewportUI.Height > 0 && UI.Camera != null)
            viewport = UI.Camera.WorldToScreen(viewportUI);
        else
            viewport = new Rect(0, 0, screenSize.X, screenSize.Y);

        var baseScale = _dpi * _uiScale * UI.UserScale;

        // Fit zoom to the visible viewport, not the full window — panels overlay the scene.
        var zoomForWidth = viewport.Width / (size.X * baseScale);
        var zoomForHeight = viewport.Height / (size.Y * baseScale);
        _zoom = Math.Clamp(MathF.Min(zoomForWidth, zoomForHeight), ZoomMin, ZoomMax);

        _camera.Rotation = 0f;
        _camera.Position = center;
        UpdateCamera();

        // Shift the camera so `center` projects to the viewport center instead of the window center.
        _camera.Position += center - _camera.ScreenToWorld(viewport.Center);
        UpdateCamera();
    }

    public static void Play()
    {
        foreach (var doc in Project.Documents)
            if (doc.IsSelected)
                doc.TogglePlay();
    }

    private static void DuplicateSelected()
    {
        if (SelectedCount == 0)
            return;

        var selected = Project.Documents.Where(d => d.IsSelected).ToArray();

        ClearSelection();

        foreach (var source in selected)
        {
            var dup = Project.Duplicate(source);
            if (dup == null)
                continue;
            dup.Position = source.Position + new Vector2(0.5f, 0.5f);
            SetSelected(dup, true);
        }

        Input.ConsumeButton(InputCode.KeyLeftCtrl);
        Input.ConsumeButton(InputCode.KeyRightCtrl);
    }

    private static void DeleteSelected()
    {
        if (SelectedCount == 0)
            return;

        var message = SelectedCount == 1 ? "Delete selected assets?" : $"Delete {SelectedCount} assets?";
        ConfirmDialog.Show(message, () =>
        {
            var toDelete = new List<Document>();
            foreach (var doc in Project.Documents)
            {
                if (doc.IsSelected)
                    toDelete.Add(doc);
            }

            ClearSelection();

            foreach (var doc in toDelete)
                Project.Delete(doc);
        },
        yes: "Delete",
        no: "Cancel");
    }

    public static void FrameSelected()
    {
        if (ActiveEditor != null)
        {
            FrameRect(ActiveEditor.Document.Bounds.Translate(ActiveEditor.Document.Position));
            return;
        }

        if (SelectedCount == 0) return;

        Rect? bounds = null;
        foreach (var doc in Project.Documents)
        {
            if (!doc.IsSelected) continue;
            var docBounds = doc.Bounds.Translate(doc.Position);
            bounds = bounds == null ? docBounds : Rect.Union(bounds.Value, docBounds);
        }

        if (bounds != null)
            FrameRect(bounds.Value);
    }

    public static Document? HitTestDocuments(Vector2 point)
    {
        Document? firstHit = null;

        var font = EditorAssets.Fonts.Seguisb;
        var scale = 1f / _zoom;
        var fontSize = EditorStyle.Workspace.NameSize * scale;
        var padding = EditorStyle.Workspace.NamePadding * scale;

        for (var i = Project.Documents.Count - 1; i >= 0; i--)
        {
            var doc = Project.Documents[i];
            if (!doc.Loaded || !doc.PostLoaded) continue;
            if (!CollectionManager.IsDocumentVisible(doc)) continue;

            var hitBounds = doc.Bounds.Translate(doc.Position).Contains(point);
            if (ShowNames && !hitBounds)
            {
                var bounds = doc.Bounds.Translate(doc.Position);
                var textSize = TextRender.Measure(doc.Name, font, fontSize);
                var textX = bounds.Center.X - textSize.X * 0.5f;
                var textY = bounds.Bottom + padding - textSize.Y * 0.5f;
                var nameRect = new Rect(textX, textY, textSize.X, textSize.Y);
                hitBounds = nameRect.Contains(point);
            }

            if (!hitBounds) continue;

            firstHit ??= doc;
            if (!doc.IsSelected)
                return doc;
        }
        return firstHit;
    }

    public static void ClearSelection()
    {
        foreach (var doc in Project.Documents)
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
        foreach (var doc in Project.Documents)
        {
            if (doc.IsSelected)
                return doc;
        }
        return null;
    }

    private static void SelectAll()
    {
        ClearSelection();
        foreach (var doc in Project.Documents)
        {
            if (!doc.Loaded || !doc.PostLoaded)
                continue;
            if (!CollectionManager.IsDocumentVisible(doc))
                continue;
            SetSelected(doc, true);
        }
    }

    private static void SetCollection(int index)
    {
        var collection = CollectionManager.GetByIndex(index);
        if (collection == null || CollectionManager.VisibleIndex == index)
            return;

        // Save current camera position and zoom to current collection
        var current = CollectionManager.VisibleCollection;
        if (current != null)
        {
            current.CameraPosition = _camera.Position;
            current.CameraZoom = _zoom;
            current.CameraRotation = _camera.Rotation;
        }

        CollectionManager.SetVisible(index);

        // Restore camera position and zoom from new collection
        _camera.Position = collection.CameraPosition;
        _zoom = collection.CameraZoom;
        _camera.Rotation = collection.CameraRotation;
        UpdateCamera();
    }

    private static void MoveSelectedToCollection(int index)
    {
        var collection = CollectionManager.GetByIndex(index);
        if (collection == null)
            return;

        var count = 0;
        foreach (var doc in Project.Documents)
        {
            if (!doc.IsSelected || doc.CollectionId == collection.Id)
                continue;
            Undo.Record(doc);
            doc.CollectionId = collection.Id;
            count++;
        }

        if (count > 0)
        {
            if (!CollectionManager.IsVisible(index))
                ClearSelection();
        }
    }

    public static void EndEdit()
    {
        _pendingState = WorkspaceState.Default;
    }

    private static void BeginEdit()
    {
        if (SelectedCount != 1) return;

        _pendingState = State == WorkspaceState.Edit
            ? WorkspaceState.Default
            : WorkspaceState.Edit;
    }

    private static void UpdateState()
    {
        if (_pendingState == State)
            return;

        State = _pendingState;

        if (State == WorkspaceState.Default)
        {
            if (_activeDocument == null)
                return;

            if (_hasSavedCamera)
            {
                _camera.Position = _savedCameraPosition;
                _zoom = _savedCameraZoom;
                _camera.Rotation = _savedCameraRotation;
                _hasSavedCamera = false;
                UpdateCamera();
            }

            CommandManager.RegisterEditor(null);

            _activeEditor?.Dispose();
            _activeEditor = null;
            _activeDocument.IsEditing = false;
            _activeDocument = null;
            State = WorkspaceState.Default;

            Project.SaveAll();
            return;
        }

        if (SelectedCount != 1)
            return;

        var doc = GetFirstSelected();
        if (doc == null)
            return;

        if (doc.Def.EditorFactory == null)
            return;

        if (!(doc.Def.CanEdit?.Invoke(doc) ?? true))
            return;

        var savedPosition = _camera.Position;
        var savedZoom = _zoom;
        var savedRotation = _camera.Rotation;

        _activeDocument = doc;
        _activeEditor = doc.Def.EditorFactory!(doc);
        doc.IsEditing = true;
        State = WorkspaceState.Edit;

        CommandManager.RegisterEditor(_activeEditor.Commands);

        if (IsIsolationActive)
        {
            _savedCameraPosition = savedPosition;
            _savedCameraZoom = savedZoom;
            _savedCameraRotation = savedRotation;
            _hasSavedCamera = true;
        }
    }

    public static void UpdateDefaultState()
    {
        if (_workspaceDragMode != WorkspaceDragMode.None)
            return;

        if (Input.WasButtonPressed(InputCode.MouseLeft))
        {
            _clearSelectionOnRelease = false;

            // If clicking over a selected doc, defer selection changes to release
            // so drag-move can happen without switching selection first
            if (HitTestSelected(_mouseWorldPosition))
            {
                _clearSelectionOnRelease = true;
                return;
            }

            var hitDoc = HitTestDocuments(_mouseWorldPosition);
            if (hitDoc != null)
            {
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
            // Clicked a selected doc without dragging — select what's under the cursor
            var hitDoc = HitTestDocuments(_mouseWorldPosition);
            if (hitDoc != null)
            {
                if (!Input.IsShiftDown())
                    ClearSelection();
                SetSelected(hitDoc, true);
            }
            else
            {
                ClearSelection();
            }
        }
    }

    private static void OpenPopupMenu()
    {
        if (_activeEditor != null)
        {
            _activeEditor.OpenContextMenu(WidgetIds.ContextMenu);
            return;
        }

        // Build dynamic popup with type conversion options if applicable
        var items = new List<PopupMenuItem>(_workspacePopupItems);

        if (SelectedCount == 1)
        {
            var selectedDoc = Project.SelectedDocuments.FirstOrDefault();
            if (selectedDoc != null)
            {
                var ext = System.IO.Path.GetExtension(selectedDoc.Path);
                var defs = Project.GetDefs(ext);
                if (defs != null && defs.Count > 1)
                {
                    items.Add(PopupMenuItem.Separator());
                    items.Add(PopupMenuItem.Submenu("Convert to", showChecked: true, showIcons: false));
                    foreach (var def in defs)
                    {
                        var targetDef = def;
                        items.Add(PopupMenuItem.Item(
                            def.Name,
                            () => ConvertDocumentType(selectedDoc, targetDef),
                            level: 1,
                            isChecked: () => selectedDoc.Def == targetDef));
                    }
                }

                if (selectedDoc is SpriteDocument sprite)
                {
                    items.Add(PopupMenuItem.Separator());
                    items.Add(PopupMenuItem.Item(
                        "Create Instance",
                        () => CreateNewDocument(SpriteInstanceDocument.CreateNew(sprite, _popupWorldPosition)),
                        icon: EditorAssets.Sprites.AssetIconSprite));
                }
            }
        }

        _popupWorldPosition = MouseWorldPosition;

        UI.OpenPopupMenu(WidgetIds.ContextMenu, items.ToArray(), EditorStyle.ContextMenu.Style, title: "Asset");
    }

    private static void ConvertDocumentType(Document doc, DocumentDef newDef)
    {
        if (doc.Def == newDef) return;

        var dir = Path.GetDirectoryName(doc.Path) ?? "";
        var stem = Path.GetFileNameWithoutExtension(doc.Path);

        // Delete old companion file (the type-specific file, not the image)
        var oldExt = doc.Def.Extensions[0];
        var oldCompanion = Path.Combine(dir, stem + oldExt);
        if (File.Exists(oldCompanion))
            File.Delete(oldCompanion);

        // Find the image file
        string? imagePath = null;
        string[] imageExts = [".png", ".jpg", ".jpeg", ".tga", ".webp", ".bmp"];
        foreach (var imgExt in imageExts)
        {
            var imgPath = Path.Combine(dir, stem + imgExt);
            if (File.Exists(imgPath))
            {
                imagePath = imgPath;
                break;
            }
        }
        if (imagePath == null) return;

        // Write document_type to meta on the image file
        var metaPath = imagePath + ".meta";
        var meta = PropertySet.LoadFile(metaPath) ?? new PropertySet();
        meta.SetString("editor", "document_type", newDef.Name);
        meta.Save(metaPath);

        // Remove old document from list (don't delete files)
        var position = doc.Position;
        Undo.RemoveDocument(doc);
        Project.Remove(doc);

        // Create new document from the image file
        var newDoc = Project.Create(imagePath);
        if (newDoc != null)
        {
            newDoc.LoadMetadata();
            newDoc.Loaded = true;
            newDoc.Load();
            newDoc.PostLoad();
            newDoc.PostLoaded = true;
            newDoc.Position = Grid.SnapToPixelGrid(position);
            Project.NotifyDocumentAdded(newDoc);
            AssetManifest.IsModified = true;
        }
    }

    private static void OpenSettings()
    {
        SettingsPopup.Open();
    }
}
