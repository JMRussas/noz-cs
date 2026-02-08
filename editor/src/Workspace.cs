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
    [ElementId("Toolbar")]
    [ElementId("XrayButton")]
    [ElementId("CollectionButton")]
    [ElementId("Scene")]
    private static partial class ElementId { }

    private const float ZoomMin = 0.2f;
    private const float ZoomMax = 200f;
    private const float ZoomStep = 0.1f;
    private const float ZoomDefault = 1f;
    private const float DefaultDpi = 72f;
    private const float DragMinDistance = 5f;
    private const float UIScaleMin = 0.5f;
    private const float UIScaleMax = 3f;
    private const float UIScaleStep = 0.1f;

    public static DocumentEditor? ActiveEditor => _activeEditor;

    private static Camera _camera = null!;

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
    private static bool _wasDragging;
    private static InputCode _dragButton;
    private static bool _clearSelectionOnRelease;
    private static WorkspaceState _pendingState;

    private static Document? _activeDocument;
    private static DocumentEditor? _activeEditor;
    private static PopupMenuDef _workspaceContextMenu;

    public static Camera Camera => _camera;
    public static bool XrayMode { get; set; }
    public static float XrayAlpha => XrayMode ? EditorStyle.Workspace.XrayAlpha : 1f;
    public static float Zoom => _zoom;
    public static bool ShowGrid => _showGrid;
    public static bool ShowNames => _showNames;
    public static bool ShowHidden { get; set; }
    public static Vector2 MousePosition => _mousePosition;
    public static Vector2 MouseWorldPosition => _mouseWorldPosition;
    public static bool IsDragging => _isDragging;
    public static bool WasDragging => _wasDragging;
    public static bool DragStarted => _dragStarted;
    public static InputCode DragButton => _dragButton;
    public static Vector2 DragDelta { get; private set; }
    public static Vector2 DragWorldDelta { get; private set; }
    public static Vector2 DragWorldPosition { get; private set; }
    public static WorkspaceState State { get; private set; } = WorkspaceState.Default;
    public static int SelectedCount { get; private set; }
    public static Tool? ActiveTool { get; private set; }

    public static event Action<bool>? XrayModeChanged;

    public static float GetUIScale() => Application.Platform.DisplayScale * _userUIScale;

    public static void IncreaseUIScale()
    {
        _userUIScale = Math.Clamp(UI.UserScale + UIScaleStep, UIScaleMin, UIScaleMax);
        UI.UserScale = _userUIScale;
    }

    public static void DecreaseUIScale()
    {
        _userUIScale = Math.Clamp(UI.UserScale - UIScaleStep, UIScaleMin, UIScaleMax);
        UI.UserScale = _userUIScale;
    }

    public static void ResetUIScale()
    {
        _userUIScale = 0.66666f;
        UI.UserScale = _userUIScale;
    }

    public static void Init()
    {
        _camera = new Camera();
        _zoom = ZoomDefault;
        _dpi = DefaultDpi;
        _uiScale = 1f;
        _showGrid = true;
        _pendingState = WorkspaceState.Default;

        InitCommands();
        UpdateCamera();

        Graphics.ClearColor = EditorStyle.Workspace.FillColor;
    }

    public static void Shutdown()
    {
    }

    private static void GenerateManifest()
    {
        AssetManifest.Generate(force: true);
        Notifications.Add("Asset manifest generated");
    }   

    private static void ReimportAll()
    {
        foreach (var doc in DocumentManager.Documents)
            Importer.Queue(doc, true);

        AssetManifest.Generate(force: true);
    }

    private static void CreateNewDocument(AssetType assetType)
    {
        var position = PopupMenu.WorldPosition;
        var doc = DocumentManager.New(assetType, null, position);
        if (doc != null)
        {
            doc.CollectionId = CollectionManager.GetVisibleId();
            doc.MarkMetaModified();
            ClearSelection();
            SetSelected(doc, true);
            Notifications.Add($"created {assetType.ToString().ToLowerInvariant()} '{doc.Name}'");
        }
    }

    private static void RebuildAtlas()
    {
        AtlasManager.Rebuild();
        DocumentManager.SaveAll();
    }

    private static void InitCommands()
    {
        var renameCommand = new Command { Name = "Rename", Handler = BeginRenameTool, Key = InputCode.KeyF2 };
        var deleteCommand = new Command { Name = "Delete", Handler = DeleteSelected, Key = InputCode.KeyX, Icon = EditorAssets.Sprites.IconDelete };
        var duplicateCommand = new Command { Name = "Duplicate", Handler = DuplicateSelected, Key = InputCode.KeyD, Ctrl = true, Icon = EditorAssets.Sprites.IconDuplicate };
        var editCommand = new Command { Name = "Edit", Handler = BeginEdit, Key = InputCode.KeyTab, Icon = EditorAssets.Sprites.IconEdit };
        var moveCommand = new Command { Name = "Move", Handler = BeginMoveTool, Key = InputCode.KeyG, Icon = EditorAssets.Sprites.IconMove };
        var hideCommand = new Command { Name = "Hide", Handler = HandleHide, Key = InputCode.KeyH, Icon = EditorAssets.Sprites.IconPreview };
        var unhideAllCommand = new Command { Name = "Unhide All", Handler = HandleUnhideAll, Key = InputCode.KeyH, Ctrl = true, Icon = EditorAssets.Sprites.IconPreview };

        CommandManager.RegisterCommon([
            new Command { Name = "Save All", Handler = DocumentManager.SaveAll, Key = InputCode.KeyS, Ctrl = true },
            new Command { Name = "Undo", Handler = () => Undo.DoUndo(), Key = InputCode.KeyZ, Ctrl = true },
            new Command { Name = "Redo", Handler = () => Undo.DoRedo(), Key = InputCode.KeyY, Ctrl = true },
            new Command { Name = "Increase UI Scale", Handler = IncreaseUIScale, Key = InputCode.KeyEquals, Ctrl = true },
            new Command { Name = "Decrease UI Scale", Handler = DecreaseUIScale, Key = InputCode.KeyMinus, Ctrl = true },
            new Command { Name = "Reset UI Scale", Handler = ResetUIScale, Key = InputCode.Key0, Ctrl = true },
            new Command { Name = "Command Palette", Handler = CommandPalette.Open, Key = InputCode.KeyP, Ctrl = true, Shift = true },
            new Command { Name = "Toggle Grid", Handler = ToggleGrid, Key = InputCode.KeyQuote, Ctrl = true },
            new Command { Name = "Toggle Names", Handler = ToggleNames, Key = InputCode.KeyN, Alt = true },
        ]);

        var workspaceCommands = new List<Command>
        {
            editCommand,
            renameCommand,
            deleteCommand,
            duplicateCommand,
            moveCommand,
            hideCommand,
            unhideAllCommand,
            new() { Name = "Select All", Handler = SelectAll, Key = InputCode.KeyA },
            new() { Name = "Frame", Handler = FrameSelected, Key = InputCode.KeyF },
            new() { Name = "Reimport All", Handler = ReimportAll },
            new() { Name = "Generate Manifest", Handler = GenerateManifest },
            new() { Name = "Play/Stop", Handler = Play, Key = InputCode.KeySpace },
            new() { Name = "Show/Hide Hidden Assets", Handler = ToggleShowHidden },
            new() { Name = "Rebuild Atlas", Handler = RebuildAtlas },
        };

        for (var i = 1; i <= 9; i++)
        {
            var index = i;
            workspaceCommands.Add(new Command { Name = $"Collection {i}", Handler = () => SetCollection(index), Key = InputCode.Key0 + i });
            workspaceCommands.Add(new Command { Name = $"Move to Collection {i}", Handler = () => MoveSelectedToCollection(index), Key = InputCode.Key0 + i, Ctrl = true });
        }

        CommandManager.RegisterWorkspace([.. workspaceCommands]);

        var items = new List<PopupMenuItem>();
        var creatableDefs = DocumentManager.GetCreatableDefs().ToArray();
        if (creatableDefs.Length > 0)
        {
            items.Add(PopupMenuItem.Submenu("New"));
            foreach (var def in creatableDefs.OrderBy(d => d.Type.ToString()))
            {
                var assetType = def.Type;
                items.Add(PopupMenuItem.Item(
                    def.Type.ToString(),
                    () => CreateNewDocument(assetType),
                    level: 1,
                    icon: def.Icon?.Invoke()));
            }
            items.Add(PopupMenuItem.Separator());
        }

        if (CollectionManager.Collections.Count > 0)
        {
            items.Add(PopupMenuItem.Submenu("Move to Collection", showChecked: true, showIcons: false));
            foreach (var collection in CollectionManager.Collections)
            {
                var idx = collection.Index;
                items.Add(PopupMenuItem.Item(collection.Name, () => MoveSelectedToCollection(idx), level: 1, isChecked: () => idx == CollectionManager.VisibleIndex));
            }
            items.Add(PopupMenuItem.Separator());
        }

        items.Add(PopupMenuItem.FromCommand(editCommand, enabled: () => SelectedCount == 1));
        items.Add(PopupMenuItem.FromCommand(duplicateCommand));
        items.Add(PopupMenuItem.FromCommand(renameCommand));
        items.Add(PopupMenuItem.FromCommand(deleteCommand));
        items.Add(PopupMenuItem.FromCommand(moveCommand));
        _workspaceContextMenu = new PopupMenuDef([.. items], "Asset");
    }

    public static void LoadUserSettings(PropertySet props)
    {
        _showGrid = props.GetBool("workspace", "show_grid", true);
        _showNames = props.GetBool("workspace", "show_names", false);
        _userUIScale = props.GetFloat("workspace", "ui_scale", 1f);
        UI.UserScale = _userUIScale;

        // Restore camera position and zoom from visible collection
        var collection = CollectionManager.VisibleCollection;
        if (collection != null)
        {
            _camera.Position = collection.CameraPosition;
            _zoom = collection.CameraZoom;
        }

        UpdateCamera();
    }

    public static void SaveUserSettings(PropertySet props)
    {
        var collection = CollectionManager.VisibleCollection;
        if (collection != null)
        {
            collection.CameraPosition = _camera.Position;
            collection.CameraZoom = _zoom;
        }

        props.SetBool("workspace", "show_grid", _showGrid);
        props.SetBool("workspace", "show_names", _showNames);
        props.SetFloat("workspace", "ui_scale", UI.UserScale);
    }
    
    public static void Update()
    {
        UpdateState();
        UpdateCamera();

        if (!CommandPalette.IsOpen && !PopupMenu.IsVisible && !ConfirmDialog.IsVisible)
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

            if (Input.WasButtonReleased(InputCode.MouseRight) && !_wasDragging)
                OpenPopupMenu();

            ActiveTool?.Update();
        }

        UpdateCulling();
    }

    private static void DrawScene()
    {
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
            if (!CollectionManager.IsDocumentVisible(doc))
                continue;

            var docBounds = doc.Bounds.Translate(doc.Position);
            if (bounds.Intersects(docBounds))
                SetSelected(doc, true);
        }
    }

    // :toolbar
    private static void ToolbarUI()
    {
        using var _ = UI.BeginContainer(new ContainerStyle
        {
            Height = Size.Fit,
            Color = EditorStyle.Panel.Root.Color
        });
        using var __ = UI.BeginRow(new ContainerStyle { 
            Padding = EdgeInsets.Symmetric(4, EditorStyle.Control.Spacing),
            Spacing = EditorStyle.Control.Spacing
        });

        CollectionUI();

        UI.Flex();
        if (EditorUI.Button(ElementId.XrayButton, EditorAssets.Sprites.IconXray, toolbar: true, selected: XrayMode))
        {
            XrayMode = !XrayMode;
            XrayModeChanged?.Invoke(XrayMode);
        }
    }

    private static void CollectionUI()
    {
        static void OpenPopup()
        {
            var items = new List<PopupMenuItem>();
            foreach (var collection in CollectionManager.Collections)
            {
                items.Add(PopupMenuItem.Item(
                    collection.Name,
                    handler: () => SetCollection(collection.Index),
                    isChecked: () => CollectionManager.VisibleIndex == collection.Index));
            }

            if (items.Count == 0) return;

            var buttonRect = UI.GetElementWorldRect(ElementId.CollectionButton);
            var popupStyle = new PopupStyle
            {
                AnchorX = Align.Min,
                AnchorY = Align.Max,
                PopupAlignX = Align.Min,
                PopupAlignY = Align.Min,
                ClampToScreen = true,
                AnchorRect = buttonRect,
                MinWidth = buttonRect.Width,
            };

            PopupMenu.Open([.. items], null, popupStyle, showChecked: true, showIcons: false);
        }

        static void Content()
        {
            using var _ = UI.BeginRow(new ContainerStyle{
                Spacing = EditorStyle.Control.Spacing,
                Padding = EdgeInsets.Right(EditorStyle.Control.Spacing),
                MinWidth = 150
            });
            EditorUI.ControlIcon(EditorAssets.Sprites.IconCollection);
            EditorUI.ControlText(CollectionManager.VisibleCollection?.Name ?? "None");
            UI.Spacer(EditorStyle.Control.Spacing);
        }

        if (EditorUI.Control(
            ElementId.CollectionButton,
            selected: EditorUI.IsPopupOpen(ElementId.CollectionButton),
            toolbar: false,
            content: Content))
            OpenPopup();
    }

    public static void UpdateUI()
    {
        using (UI.BeginColumn(ElementId.Toolbar))
        {
            ToolbarUI();
            UI.Container(new ContainerStyle { Height = 1, Color = EditorStyle.Panel.Root.Border.Color });

            using (UI.BeginFlex())
                UI.Scene(ElementId.Scene, Camera, DrawScene, new SceneStyle { Color = EditorStyle.Workspace.FillColor, SampleCount = 4 });
        }

        ActiveEditor?.UpdateUI();
        ActiveTool?.UpdateUI();
    }

    public static void LateUpdate()
    {
        Workspace.ActiveEditor?.LateUpdate();
    }

    private static void UpdateCulling()
    {
        var cameraBounds = _camera.WorldBounds;
        foreach (var doc in DocumentManager.Documents)
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

        foreach (var doc in DocumentManager.Documents)
        {
            if (!doc.Loaded || !doc.PostLoaded)
                continue;
            if (doc.IsEditing || doc.IsClipped)
                continue;
            if (!ShowHidden && !doc.IsVisible)
                continue;
            if (doc.IsHiddenInWorkspace)
                continue;
            if (!CollectionManager.IsDocumentVisible(doc))
                continue;

            if (doc.IsSelected)
                doc.DrawBounds(selected:true);

            Graphics.SetTransform(doc.Transform);
            doc.Draw();
        }

        ActiveEditor?.Document.DrawBounds();

        Graphics.PopState();
    }

    private static void DrawNames()
    {
        using var _ = Graphics.PushState();

        var font = EditorAssets.Fonts.Seguisb;
        var fontSize = EditorStyle.Workspace.NameSize * Gizmos.ZoomRefScale;
        var padding = EditorStyle.Workspace.NamePadding * Gizmos.ZoomRefScale;
        var renamingDoc = (ActiveTool as RenameTool)?.Target as Document;

        Graphics.SetLayer(EditorLayer.Names);
        TextRender.SetOutline(EditorStyle.Workspace.NameOutlineColor, EditorStyle.Workspace.NameOutline, 0.5f); 

        foreach (var doc in DocumentManager.Documents)
        {
            if (!doc.Loaded || !doc.PostLoaded) continue;
            if (!ShowHidden && !doc.IsVisible) continue;
            if (doc.IsClipped) continue;
            if (doc.IsHiddenInWorkspace) continue;
            if (!CollectionManager.IsDocumentVisible(doc)) continue;
            if (doc == renamingDoc) continue;

            var bounds = doc.Bounds.Translate(doc.Position);
            var textSize = TextRender.Measure(doc.Name, font, fontSize);
            var textX = bounds.Center.X - textSize.X * 0.5f;
            var textY = bounds.Bottom + padding - textSize.Y * 0.5f;
            Graphics.SetTransform(Matrix3x2.CreateTranslation(textX, textY));
            Graphics.SetColor(doc.IsSelected ? EditorStyle.Workspace.SelectionColor : EditorStyle.Workspace.NameColor);
            TextRender.Draw(doc.Name, font, fontSize, order: doc.IsSelected ? 1 : 0);
        }

        TextRender.ClearOutline();
    }

    private static void BeginMoveTool()
    {
        if (SelectedCount == 0 || ActiveTool != null || State != WorkspaceState.Default)
            return;

        Undo.BeginGroup();
        foreach (var doc in DocumentManager.Documents)
        {
            if (doc.IsSelected)
            {
                Undo.Record(doc);
                doc.SavedPosition = doc.Position;
            }
        }
        Undo.EndGroup();

        BeginTool(new MoveTool(
            update: delta =>
            {
                foreach (var doc in DocumentManager.Documents)
                {
                    if (!doc.IsSelected)
                        continue;
                    var newPos = doc.SavedPosition + delta;
                    if (Input.IsCtrlDown(InputScope.All))
                        newPos = Grid.SnapToGrid(newPos);
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

    private static void ToggleGrid()
    {
        _showGrid = !_showGrid;
    }

    private static void ToggleNames()
    {
        _showNames = !_showNames;
    }

    private static void BeginRenameTool()
    {
        if (SelectedCount != 1 || ActiveTool != null || State != WorkspaceState.Default)
            return;

        var doc = GetFirstSelected();
        if (doc == null)
            return;

        BeginTool(CreateRenameToolForDocument(doc));
    }

    private static RenameTool CreateRenameToolForDocument(Document doc)
    {
        return new RenameTool(
            doc.Name,
            () =>
            {
                var padding = EditorStyle.Workspace.NamePadding / Zoom;
                var bounds = doc.Bounds.Translate(doc.Position);
                return new Vector2(bounds.Center.X, bounds.Bottom + padding);
            },
            newName =>
            {
                if (DocumentManager.Rename(doc, newName))
                    Notifications.Add($"renamed to '{newName}'");
                else
                    Notifications.AddError("rename failed");
            }
        ) { Target = doc };
    }

    private static void UpdateCamera()
    {
        if (UI.Camera == null)
            return;

        var effectiveDpi = _dpi * _uiScale * _userUIScale * _zoom;

        var sceneWorldRect = UI.GetElementWorldRect(ElementId.Scene);
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
        _mousePosition = Input.MousePosition;
        _mouseWorldPosition = _camera.ScreenToWorld(_mousePosition);
        _dragStarted = false;
        _wasDragging = false;

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
        _wasDragging = true;
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

    public static void FrameRect(Rect bounds)
    {
        const float padding = 1.25f;

        var center = bounds.Center;
        var size = bounds.Size * padding;

        if (size.X < ZoomMin) size.X = ZoomMin;
        if (size.Y < ZoomMin) size.Y = ZoomMin;

        var screenSize = Application.WindowSize;
        var baseScale = _dpi * _uiScale * _userUIScale;

        // Calculate zoom needed to fit width and height, use the smaller one
        // effectiveDpi = baseScale * zoom, so zoom = screenSize / (size * baseScale)
        var zoomForWidth = screenSize.X / (size.X * baseScale);
        var zoomForHeight = screenSize.Y / (size.Y * baseScale);
        _zoom = Math.Clamp(MathF.Min(zoomForWidth, zoomForHeight), ZoomMin, ZoomMax);

        _camera.Position = center;
        UpdateCamera();
    }

    public static void Play()
    {
        foreach (var doc in DocumentManager.Documents)
            if (doc.IsSelected)
                doc.TogglePlay();
    }

    private static void DuplicateSelected()
    {
        if (SelectedCount == 0)
        {
            Notifications.AddError("no asset selected");
            return;
        }

        var selected = DocumentManager.Documents.Where(d => d.IsSelected).ToArray();

        ClearSelection();

        foreach (var source in selected)
        {
            var dup = DocumentManager.Duplicate(source);
            if (dup == null)
            {
                Notifications.AddError("duplicate failed");
                continue;
            }
            SetSelected(dup, true);
        }

        Input.ConsumeButton(InputCode.KeyLeftCtrl);
        Input.ConsumeButton(InputCode.KeyRightCtrl);
        BeginMoveTool();
    }

    private static void DeleteSelected()
    {
        if (SelectedCount == 0)
            return;

        var message = SelectedCount == 1 ? "Delete selectd assets?" : $"Delete {SelectedCount} assets?";
        ConfirmDialog.Show(message, () =>
        {
            var toDelete = new List<Document>();
            foreach (var doc in DocumentManager.Documents)
            {
                if (doc.IsSelected)
                    toDelete.Add(doc);
            }

            foreach (var doc in toDelete)
                DocumentManager.Delete(doc);

            SelectedCount = 0;
            Notifications.Add($"deleted {toDelete.Count} asset(s)");
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
        foreach (var doc in DocumentManager.Documents)
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

        for (var i = DocumentManager.Documents.Count - 1; i >= 0; i--)
        {
            var doc = DocumentManager.Documents[i];
            if (!doc.Loaded || !doc.PostLoaded) continue;
            if (doc.IsHiddenInWorkspace) continue;
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

    private static void SelectAll()
    {
        ClearSelection();
        foreach (var doc in DocumentManager.Documents)
        {
            if (!doc.Loaded || !doc.PostLoaded)
                continue;
            if (!doc.IsVisible && !ShowHidden)
                continue;
            if (!CollectionManager.IsDocumentVisible(doc))
                continue;
            SetSelected(doc, true);
        }
    }

    private static void ToggleShowHidden()
    {
        ShowHidden = !ShowHidden;
    }

    private static void SetCollection(int index)
    {
        var collection = CollectionManager.GetByIndex(index);
        if (collection == null)
        {
            Notifications.AddError($"Collection {index} not defined");
            return;
        }

        if (CollectionManager.VisibleIndex == index)
            return;

        // Save current camera position and zoom to current collection
        var current = CollectionManager.VisibleCollection;
        if (current != null)
        {
            current.CameraPosition = _camera.Position;
            current.CameraZoom = _zoom;
        }

        CollectionManager.SetVisible(index);

        // Restore camera position and zoom from new collection
        _camera.Position = collection.CameraPosition;
        _zoom = collection.CameraZoom;
        UpdateCamera();

        Notifications.Add($"Collection: {collection.Name}");
    }

    private static void MoveSelectedToCollection(int index)
    {
        var collection = CollectionManager.GetByIndex(index);
        if (collection == null)
        {
            Notifications.AddError($"Collection {index} not defined");
            return;
        }

        var count = 0;
        foreach (var doc in DocumentManager.Documents)
        {
            if (!doc.IsSelected || doc.CollectionId == collection.Id)
                continue;
            doc.CollectionId = collection.Id;
            doc.MarkMetaModified();
            count++;
        }

        if (count > 0)
        {
            Notifications.Add($"Moved {count} asset(s) to '{collection.Name}'");
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

            CancelTool();
            CommandManager.RegisterEditor(null);

            _activeEditor?.Dispose();
            _activeEditor = null;
            _activeDocument.IsEditing = false;
            _activeDocument = null;
            State = WorkspaceState.Default;

            DocumentManager.SaveAll();
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

        _activeDocument = doc;
        _activeEditor = doc.Def.EditorFactory!(doc);
        doc.IsEditing = true;
        State = WorkspaceState.Edit;

        CommandManager.RegisterEditor(_activeEditor.Commands);
    }

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

        ActiveTool.Dispose();
        ActiveTool = null;
        Cursor.SetDefault();
    }

    public static void CancelTool()
    {
        if (ActiveTool == null)
            return;

        ActiveTool.Cancel();
        ActiveTool.Dispose();
        ActiveTool = null;
        Cursor.SetDefault();
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

    private static void OpenPopupMenu()
    {
        var menu = GetPopupMenu();
        if (menu != null && menu.Value.Items.Length > 0)
            PopupMenu.Open(menu.Value);
    }

    public static PopupMenuDef? GetPopupMenu()
    {
        if (_activeEditor != null)
            return _activeEditor.ContextMenu;
        return _workspaceContextMenu;
    }

    private static void HandleHide()
    {
        foreach (var doc in DocumentManager.SelectedDocuments)
            doc.IsHiddenInWorkspace = true;

        ClearSelection();
    }

    private static void HandleUnhideAll()
    {
        foreach (var doc in DocumentManager.Documents)
            doc.IsHiddenInWorkspace = false;
    }
}
