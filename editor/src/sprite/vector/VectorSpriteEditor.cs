//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ.Editor;

public partial class VectorSpriteEditor : SpriteEditor
{
    private static partial class WidgetIds
    {
        public static partial WidgetId Root { get; }
        public static partial WidgetId LayerToggle { get; }
        public static partial WidgetId ExitEditMode { get; }
        public static partial WidgetId IsolationToggle { get; }
        public static partial WidgetId InspectorToggle { get; }
        public static partial WidgetId TileButton { get; }
        public static partial WidgetId SubtractButton { get; }
        public static partial WidgetId FirstOpacity { get; }
        public static partial WidgetId DopeSheet { get; }
        public static partial WidgetId FillColorButton { get; }
        public static partial WidgetId StrokeColor { get; }
        public static partial WidgetId StrokeWidth { get; }
        public static partial WidgetId OutlineColor { get; }
        public static partial WidgetId OutlineSize { get; }
        public static partial WidgetId StrokeJoinRound { get; }
        public static partial WidgetId StrokeJoinMiter { get; }
        public static partial WidgetId StrokeJoinBevel { get; }
        public static partial WidgetId AnimatedButton { get; }
        public static partial WidgetId PlayButton { get; }
        public static partial WidgetId ShowSkeletonOverlay { get; }
        public static partial WidgetId PathNormal { get; }
        public static partial WidgetId PathSubtract { get; }
        public static partial WidgetId PathClip { get; }
        public static partial WidgetId BoolUnion { get; }
        public static partial WidgetId BoolSubtract { get; }
        public static partial WidgetId BoolIntersect { get; }
        public static partial WidgetId FillColor { get; }
        public static partial WidgetId PenToolButton { get; }
        public static partial WidgetId RectToolButton { get; }
        public static partial WidgetId CircleToolButton { get; }
        public static partial WidgetId DopeSheetToggle { get; }
        public static partial WidgetId VModeButton { get; }
        public static partial WidgetId AModeButton { get; }
        public static partial WidgetId BevelModeButton { get; }
        public static partial WidgetId ContextMenu { get; }
        public static partial WidgetId OnionSkinButton { get; }
        public static partial WidgetId PreviewRasterizeButton { get; }
    }

    private int _currentTimeSlot;
    private bool _isPlaying;
    private bool _onionSkin;
    private float _playTimer;
    private bool _showLayers = true;
    private bool _showInspector = true;
    private readonly int _versionOnOpen;

    public int CurrentTimeSlot => _currentTimeSlot;
    public override bool ShowOutliner => _showLayers;

    public bool IsPlaying => _isPlaying;

    public static List<SpritePath> HitPaths => _hitPaths;

    public override bool ShowInspector => _showInspector;
    public override PowerMode PowerMode => _isPlaying ? PowerMode.Performance : PowerMode.Balanced;

    private int CurrentFrameIndex =>
        Document.GetFrameAtTimeSlot(_currentTimeSlot);

    internal SpriteGroup ActiveRoot
    {
        get
        {
            if (!Document.IsAnimated) return Document.Root;
            var i = CurrentFrameIndex;
            if (i < 0 || i >= Document.Root.Children.Count) return Document.Root;
            return Document.Root.Children[i] as SpriteGroup ?? Document.Root;
        }
    }

    public new VectorSpriteDocument Document => (VectorSpriteDocument)base.Document;

    public VectorSpriteEditor(VectorSpriteDocument doc) : base(doc)
    {
        _versionOnOpen = doc.Version;

        Commands =
        [
            ..GetShapeCommands(),
            new Command("Exit Edit Mode",       Workspace.EndEdit,          [InputCode.KeyTab]),
            new Command("Origin to Center",     CenterShape,                [new KeyBinding(InputCode.KeyC, shift:true)]),
            new Command("Flip Horizontal",      () => FlipAxis(true)),
            new Command("Flip Vertical",        () => FlipAxis(false)),
            new Command("Bring Forward",        () => MovePathInOrder(-1),  [InputCode.KeyRightBracket]),
            new Command("Send Backward",        () => MovePathInOrder(1),   [InputCode.KeyLeftBracket]),
            new Command("Toggle Playback",      TogglePlayback,             [InputCode.KeySpace]),
            new Command("Previous Frame",       PreviousFrame,              [InputCode.KeyQ]),
            new Command("Next Frame",           NextFrame,                  [InputCode.KeyE]),
            new Command("Add Hold",             AddHoldFrame,               [new KeyBinding(InputCode.KeyH)]),
            new Command("Remove Hold",          RemoveHoldFrame,            [new KeyBinding(InputCode.KeyH, ctrl:true)]),
            new Command("Toggle Onion Skin",    ToggleOnionSkin,            [new KeyBinding(InputCode.KeyO, shift:true)]),
            new Command("Eye Dropper",          () => SetMode(SpriteEditMode.EyeDropper), [new KeyBinding(InputCode.KeyI)]),
            new Command("Boolean Union",        BooleanUnion,               [new KeyBinding(InputCode.KeyU, ctrl:true, shift:true)]),
            new Command("Boolean Subtract",     BooleanSubtract,            [new KeyBinding(InputCode.KeyD, ctrl:true, shift:true)]),
            new Command("Boolean Intersect",    BooleanIntersect,           [new KeyBinding(InputCode.KeyI, ctrl:true, shift:true)]),
            new Command("Export to PNG",        ExportToPng,                [new KeyBinding(InputCode.KeyE, ctrl:true, shift:true)]),
            new Command("Toggle Rasterization Preview", TogglePreviewRasterize, [InputCode.KeyF6]),
            new Command("Frame Selection",      FrameSelection,             [new KeyBinding(InputCode.KeyF)]),
        ];

        SetMode(new TransformMode());
    }


    private void FrameSelection()
    {
        if (_selectedPaths.Count == 0)
        {
            Workspace.FrameRect(Document.Bounds.Translate(Document.Position));
            return;
        }

        var rot = Matrix3x2.CreateRotation(_selectionRotation);
        var b = _selectionLocalBounds;
        var p0 = Vector2.Transform(new Vector2(b.Left, b.Top), rot);
        var p1 = Vector2.Transform(new Vector2(b.Right, b.Top), rot);
        var p2 = Vector2.Transform(new Vector2(b.Right, b.Bottom), rot);
        var p3 = Vector2.Transform(new Vector2(b.Left, b.Bottom), rot);
        var min = Vector2.Min(Vector2.Min(p0, p1), Vector2.Min(p2, p3));
        var max = Vector2.Max(Vector2.Max(p0, p1), Vector2.Max(p2, p3));
        Workspace.FrameRect(Rect.FromMinMax(min, max).Translate(Document.Position));
    }

    private void ExportToPng()
    {
        var pngBytes = Document.RasterizeColorToPng();
        if (pngBytes.Length == 0)
        {
            Log.Warning("Nothing to export: sprite has no content.");
            return;
        }

        var defaultName = Document.Name + ".png";
        var path = NativeFileDialog.ShowSaveFileDialog(
            Application.Platform.WindowHandle,
            "PNG Files\0*.png\0All Files\0*.*\0",
            "png",
            defaultName);

        if (path == null) return;

        File.WriteAllBytes(path, pngBytes);
        Log.Info($"Exported PNG to: {path}");
    }

    public override void Dispose()
    {
        ClearSelection();

        if (Document.Version != _versionOnOpen)
            AtlasManager.UpdateSource(Document);

        base.Dispose();
    }

    public override void OnUndoRedo()
    {
        base.OnUndoRedo();
        MarkDirty();
        RebuildSelectedPaths();
    }

    public override void OpenContextMenu(WidgetId popupId)
    {
        // Pen tool uses right-click to remove the last placed point; suppress the context menu in that mode.
        if (CurrentMode == SpriteEditMode.Pen)
            return;

        var vMode = CurrentMode == SpriteEditMode.Transform;
        var hasPath = HasPathSelection && _selectedPaths.Count > 0;
        var hasSelection = hasPath || HasLayerSelection;
        var multiPath = _selectedPaths.Count >= 2 && vMode;
        var singleNode = (HasLayerSelection && _selectedLayers.Count == 1) ||
                         (!HasLayerSelection && _selectedPaths.Count == 1);
        var canDelete = hasPath || HasLayerSelection || (CurrentMode != SpriteEditMode.Transform && GetPathWithSelection() != null);

        var items = new List<PopupMenuItem>
        {
            PopupMenuItem.Item("Cut", CutSelected, new KeyBinding(InputCode.KeyX, ctrl: true),
                enabled: () => hasPath && vMode),
            PopupMenuItem.Item("Copy", CopySelected, new KeyBinding(InputCode.KeyC, ctrl: true),
                enabled: () => hasPath && vMode),
            PopupMenuItem.Item("Paste", PasteSelected, new KeyBinding(InputCode.KeyV, ctrl: true),
                enabled: () => Clipboard.Get<PathClipboardData>() != null || Clipboard.Get<SpriteClipboardData>() != null),
            PopupMenuItem.Item("Duplicate", DuplicateSelected, new KeyBinding(InputCode.KeyD, ctrl: true),
                enabled: () => hasPath && vMode),
            PopupMenuItem.Item("Delete", DeleteSelected, InputCode.KeyX,
                enabled: () => canDelete),

            PopupMenuItem.Separator(),
            PopupMenuItem.Item("Select All", SelectAll, new KeyBinding(InputCode.KeyA, ctrl: true)),

            PopupMenuItem.Separator(),
            PopupMenuItem.Item("Rename", BeginRename, InputCode.KeyF2,
                enabled: () => singleNode),

            PopupMenuItem.Separator(),
            PopupMenuItem.Item("Flip Horizontal", () => FlipAxis(true),
                enabled: () => hasPath && vMode),
            PopupMenuItem.Item("Flip Vertical", () => FlipAxis(false),
                enabled: () => hasPath && vMode),
            PopupMenuItem.Item("Origin to Center", CenterShape, new KeyBinding(InputCode.KeyC, shift: true),
                enabled: () => hasPath && vMode),

            PopupMenuItem.Separator(),
            PopupMenuItem.Item("Bring Forward", () => MovePathInOrder(-1), InputCode.KeyRightBracket,
                enabled: () => hasPath && vMode),
            PopupMenuItem.Item("Send Backward", () => MovePathInOrder(1), InputCode.KeyLeftBracket,
                enabled: () => hasPath && vMode),

            PopupMenuItem.Separator(),
            PopupMenuItem.Item("Group", GroupSelected,
                enabled: () => hasPath && vMode),

            PopupMenuItem.Separator(),
            PopupMenuItem.Submenu("Boolean", showIcons: false),
            PopupMenuItem.Item("Union", BooleanUnion, new KeyBinding(InputCode.KeyU, ctrl: true, shift: true),
                level: 1, enabled: () => multiPath),
            PopupMenuItem.Item("Subtract", BooleanSubtract, new KeyBinding(InputCode.KeyD, ctrl: true, shift: true),
                level: 1, enabled: () => multiPath),
            PopupMenuItem.Item("Intersect", BooleanIntersect, new KeyBinding(InputCode.KeyI, ctrl: true, shift: true),
                level: 1, enabled: () => multiPath),
        };

        UI.OpenPopupMenu(WidgetIds.ContextMenu, items.ToArray(), EditorStyle.ContextMenu.Style);
    }

    private void GroupSelected()
    {
        if (_selectedPaths.Count == 0) return;

        Undo.Record(Document);

        var layer = new SpriteGroup { Name = "Group" };

        // Insert the new layer at the position of the first selected path
        var firstPath = _selectedPaths[0];
        var parent = firstPath.Parent ?? Document.Root;
        var insertIndex = parent.Children.IndexOf(firstPath);
        if (insertIndex < 0) insertIndex = 0;
        parent.Insert(insertIndex, layer);

        // Move all selected paths into the new layer
        foreach (var path in _selectedPaths)
            path.RemoveFromParent();
        foreach (var path in _selectedPaths)
            layer.Add(path);

        // Select the new layer
        Document.Root.ClearSelection();
        Document.Root.ClearSelection();
        layer.IsSelected = true;
        layer.Expanded = true;
        RebuildSelectedPaths();
        MarkDirty();
    }

    public override void Update()
    {
        UpdateHandleCursor();
        UpdateAnimation();

        Mode?.Update();

        using (Gizmos.PushState(EditorLayer.DocumentEditor))
        {
            Graphics.SetTransform(Document.Transform);
            Graphics.SetSortGroup(5);
            Document.DrawOrigin();
            Graphics.SetSortGroup(4);
            DrawWireframe();
        }

        UpdateMeshFromLayers();

        if (PreviewRasterize)
            DrawPreviewQuad();
        else
            DrawMesh();

        if (Document.ShowTiling)
            DrawTiling();

        DrawOnionSkin();

        if (Document.ShowSkeletonOverlay)
            DrawSkeletonOverlay();

        DrawEdges();

        Document.DrawBounds();

        Mode?.Draw();
    }

    public override void UpdateUI() { }


    public override void UpdateOverlayUI()
    {
        using (FloatingToolbar.Begin())
        {
            FloatingToolbarUI();

            if (Document.IsAnimated)
            {
                FloatingToolbar.Row();
                DopeSheetUI();
            }
        }
    }

    private void FloatingToolbarUI()
    {
        // V/A mode toggles
        if (FloatingToolbar.Button(WidgetIds.VModeButton, EditorAssets.Sprites.IconModeTransform, isSelected: CurrentMode == SpriteEditMode.Transform))
            SetMode(SpriteEditMode.Transform);
        EditorUI.Tooltip(WidgetIds.VModeButton, "Transform");

        if (FloatingToolbar.Button(WidgetIds.AModeButton, EditorAssets.Sprites.IconModeAnchor, isSelected: CurrentMode == SpriteEditMode.Anchor))
            SetMode(SpriteEditMode.Anchor);
        EditorUI.Tooltip(WidgetIds.AModeButton, "Edit Anchors");

        FloatingToolbar.Divider();

        // Creation modes: Pen, Rect, Circle
        if (FloatingToolbar.Button(WidgetIds.PenToolButton, EditorAssets.Sprites.IconPenMode, isSelected: CurrentMode == SpriteEditMode.Pen))
            SetMode(SpriteEditMode.Pen);
        EditorUI.Tooltip(WidgetIds.PenToolButton, "Pen Tool");

        if (FloatingToolbar.Button(WidgetIds.RectToolButton, EditorAssets.Sprites.IconRectMode, isSelected: CurrentMode == SpriteEditMode.Rectangle))
            SetMode(SpriteEditMode.Rectangle);
        EditorUI.Tooltip(WidgetIds.RectToolButton, "Rectangle Tool");

        if (FloatingToolbar.Button(WidgetIds.CircleToolButton, EditorAssets.Sprites.IconCircleMode, isSelected: CurrentMode == SpriteEditMode.Circle))
            SetMode(SpriteEditMode.Circle);
        EditorUI.Tooltip(WidgetIds.CircleToolButton, "Circle Tool");

        FloatingToolbar.Divider();

        if (FloatingToolbar.Button(WidgetIds.BevelModeButton, EditorAssets.Sprites.IconStrokeJoinBevel, isSelected: CurrentMode == SpriteEditMode.Bevel))
            SetMode(SpriteEditMode.Bevel);
        EditorUI.Tooltip(WidgetIds.BevelModeButton, "Bevel");

        FloatingToolbar.Divider();

        if (FloatingToolbar.Button(WidgetIds.AnimatedButton, EditorAssets.Sprites.AssetIconAnimation, isSelected: Document.IsAnimated))
            ToggleAnimation();
        EditorUI.Tooltip(WidgetIds.AnimatedButton, "Animation");

        if (Document.IsAnimated)
        {
            if (FloatingToolbar.Button(WidgetIds.OnionSkinButton, EditorAssets.Sprites.IconOnion, isSelected: _onionSkin))
                _onionSkin = !_onionSkin;
            EditorUI.Tooltip(WidgetIds.OnionSkinButton, "Onion Skin");            
        }

        FloatingToolbar.Divider();

        using (UI.BeginEnabled(Document.ConstrainedSize.HasValue))
        {
            if (FloatingToolbar.Button(WidgetIds.TileButton, EditorAssets.Sprites.IconTiling, isSelected: Document.ShowTiling))
                Document.ShowTiling = !Document.ShowTiling;
            EditorUI.Tooltip(WidgetIds.TileButton, "Tiling Preview");
        }


        if (FloatingToolbar.Button(WidgetIds.PreviewRasterizeButton, EditorAssets.Sprites.IconRasterize, isSelected: PreviewRasterize))
            TogglePreviewRasterize();
        EditorUI.Tooltip(WidgetIds.PreviewRasterizeButton, "Rasterization Preview");

        if (Document.Skeleton.IsResolved)
        {
            if (FloatingToolbar.Button(WidgetIds.ShowSkeletonOverlay, EditorAssets.Sprites.IconBone, isSelected: Document.ShowSkeletonOverlay))
            {
                Undo.Record(Document);
                Document.ShowSkeletonOverlay = !Document.ShowSkeletonOverlay;
            }
            EditorUI.Tooltip(WidgetIds.ShowSkeletonOverlay, "Skeleton Overlay");
        }
    }

    private int TotalTimeSlots() => Document.TotalTimeSlots;

    private void DopeSheetUI()
    {
        var maxSlots = Sprite.MaxFrames;
        var usedSlots = TotalTimeSlots();
        // Always show enough time blocks to fill the toolbar width (~5 blocks minimum)
        var blockCount = Math.Max((usedSlots + 3) / 4, 5);

        using (UI.BeginColumn(EditorStyle.Dopesheet.FloatingDopesheet))
        {
            // Header row: frame numbers
            using (UI.BeginRow(EditorStyle.Dopesheet.FloatingHeaderContainer))
            {
                for (var blockIndex = 0; blockIndex < blockCount; blockIndex++)
                {
                    if (blockIndex > 0)
                        UI.Container(EditorStyle.Dopesheet.FloatingTimeTick);

                    using (UI.BeginContainer(EditorStyle.Dopesheet.TimeBlock))
                    {
                        UI.Text(AnimationEditor.FrameTimeStrings[blockIndex], EditorStyle.Dopesheet.TimeText);
                    }
                }

                // Fill remaining width to match toolbar
                UI.Flex();
            }

            // 1px gap (panel bg shows through)
            UI.Spacer(1);

            // Layer row: keyframe cells
            using (UI.BeginRow(EditorStyle.Dopesheet.FloatingLayerRow))
            {
                var slotIndex = 0;
                for (ushort fi = 0; fi < Document.Root.Children.Count && slotIndex < maxSlots; fi++)
                {
                    var frame = Document.Root.Children[fi];
                    var isCurrentSlot = IsTimeSlotInRange(fi, _currentTimeSlot);

                    using (UI.BeginRow(WidgetIds.DopeSheet + fi))
                    {
                        if (UI.WasPressed())
                            SetCurrentTimeSlot(TimeSlotForFrame(fi));

                        // Keyframe cell with dot
                        using (UI.BeginContainer(isCurrentSlot
                            ? EditorStyle.Dopesheet.FloatingSelectedFrame
                            : EditorStyle.Dopesheet.FloatingFrame))
                        {
                            UI.Container(isCurrentSlot
                                ? EditorStyle.Dopesheet.FloatingSelectedFrameDot
                                : EditorStyle.Dopesheet.FloatingFrameDot);
                        }

                        slotIndex++;

                        // Hold cells (no dot, same color as keyframe)
                        var hold = frame.Hold;
                        for (int h = 0; h < hold && slotIndex < maxSlots; h++, slotIndex++)
                        {
                            if (h < hold - 1)
                                UI.Container(isCurrentSlot
                                    ? EditorStyle.Dopesheet.FloatingSelectedHoldSeparator
                                    : EditorStyle.Dopesheet.FloatingHoldSeparator);

                            using (UI.BeginContainer(isCurrentSlot
                                ? EditorStyle.Dopesheet.FloatingSelectedFrame
                                : EditorStyle.Dopesheet.FloatingFrame))
                            {
                            }
                        }
                    }
                }

                // Fill remaining width (panel bg shows through)
                UI.Flex();
            }
        }
    }

    private bool IsTimeSlotInRange(int frameIndex, int timeSlot)
    {
        var accumulated = 0;
        for (var fi = 0; fi < Document.Root.Children.Count; fi++)
        {
            var slots = 1 + Document.Root.Children[fi].Hold;
            if (fi == frameIndex)
                return timeSlot >= accumulated && timeSlot < accumulated + slots;
            accumulated += slots;
        }
        return false;
    }


    private void StrokeWidthButtonUI()
    {
        UI.DropDown(WidgetIds.StrokeWidth, () => [
            ..Enumerable.Range(1, 8).Select(i =>
                new PopupMenuItem { Label = Strings.Number(i), Handler = () => SetStrokeWidth((byte)i) }
            )
        ], Strings.Number(Document.CurrentStrokeWidth), EditorAssets.Sprites.IconStrokeSize);
    }

    private void OutlineSizeButtonUI()
    {
        UI.DropDown(WidgetIds.OutlineSize, () => [
            ..Enumerable.Range(1, 8).Select(i =>
                new PopupMenuItem { Label = Strings.Number(i), Handler = () => SetOutlineSize((byte)i) }
            )
        ], Strings.Number(Document.OutlineSize == 0 ? (byte)1 : Document.OutlineSize), EditorAssets.Sprites.IconStrokeSize);
    }


    public void SetCurrentTimeSlot(int timeSlot)
    {
        var maxSlots = Document.TotalTimeSlots;
        var newSlot = Math.Clamp(timeSlot, 0, maxSlots - 1);
        if (newSlot != _currentTimeSlot)
            _currentTimeSlot = newSlot;
    }


    private void TogglePlayback()
    {
        _isPlaying = !_isPlaying;
        _playTimer = 0;
    }

    private void ToggleOnionSkin()
    {
        _onionSkin = !_onionSkin;
    }

    private void NextFrame()
    {
        var frameCount = Document.FrameCount;
        if (frameCount <= 1) return;

        var fi = CurrentFrameIndex;
        fi = (fi + 1) % frameCount;
        SetCurrentTimeSlot(TimeSlotForFrame(fi));
    }

    private void PreviousFrame()
    {
        var frameCount = Document.FrameCount;
        if (frameCount <= 1) return;

        var fi = CurrentFrameIndex;
        fi = fi == 0 ? frameCount - 1 : fi - 1;
        SetCurrentTimeSlot(TimeSlotForFrame(fi));
    }

    private int TimeSlotForFrame(int frameIndex)
    {
        var slot = 0;
        for (var i = 0; i < frameIndex && i < Document.Root.Children.Count; i++)
            slot += 1 + Document.Root.Children[i].Hold;
        return slot;
    }

    private void ToggleAnimation()
    {
        Undo.Record(Document);
        if (Document.IsAnimated)
        {
            Document.DisableAnimation();
        }
        else
        {
            Document.EnableAnimation();
            SetCurrentTimeSlot(0);
        }
        MarkDirty();
    }

    private void AddHoldFrame()
    {
        if (!Document.IsAnimated || Document.FrameCount == 0) return;
        Undo.Record(Document);
        Document.Root.Children[CurrentFrameIndex].Hold++;
    }

    private void RemoveHoldFrame()
    {
        if (!Document.IsAnimated || Document.FrameCount == 0) return;
        var frame = Document.Root.Children[CurrentFrameIndex];
        if (frame.Hold <= 0) return;
        Undo.Record(Document);
        frame.Hold--;
    }

    private void SetFillColor(Color32 color)
    {
        Document.CurrentFillColor = color;

        if (_selectedPaths.Count == 0) return;
        foreach (var path in _selectedPaths)
            path.FillColor = color;
        MarkDirty();
    }

    private void SetStrokeColor(Color32 color)
    {
        Document.CurrentStrokeColor = color;

        if (_selectedPaths.Count == 0) return;
        foreach (var path in _selectedPaths)
        {
            path.StrokeColor = color;
            path.StrokeWidth = Document.CurrentStrokeWidth;
            path.StrokeJoin = Document.CurrentStrokeJoin;
        }
        MarkDirty();
    }

    private void SetStrokeWidth(byte width)
    {
        Document.CurrentStrokeWidth = width;
        Undo.Record(Document);

        foreach (var path in _selectedPaths)
            path.StrokeWidth = width;

        MarkDirty();
    }

    private void SetStrokeJoin(SpriteStrokeJoin join)
    {
        Document.CurrentStrokeJoin = join;
        Undo.Record(Document);

        foreach (var path in _selectedPaths)
            path.StrokeJoin = join;

        MarkDirty();
    }

    private void SetOutlineColor(Color32 color)
    {
        Document.OutlineColor = color;
        if (Document.OutlineSize == 0 && color.A > 0)
            Document.OutlineSize = 1;
        MarkDirty();
        Document.MarkSpriteDirty();
    }

    private void SetOutlineSize(byte size)
    {
        Undo.Record(Document);
        Document.OutlineSize = size;
        MarkDirty();
        Document.MarkSpriteDirty();
    }

    private void CycleSpritePathOperation()
    {
        var newOp = Document.CurrentOperation switch
        {
            SpritePathOperation.Normal => SpritePathOperation.Subtract,
            SpritePathOperation.Subtract => SpritePathOperation.Clip,
            _ => SpritePathOperation.Normal,
        };

        if (_selectedPaths.Count > 0)
        {
            Undo.Record(Document);
            Document.CurrentOperation = newOp;
            foreach (var path in _selectedPaths)
                path.Operation = newOp;

            MarkDirty();
        }
        else
        {
            Document.CurrentOperation = newOp;
        }
    }

    private void SetSpritePathOperation(SpritePathOperation operation)
    {
        Undo.Record(Document);
        Document.CurrentOperation = operation;

        foreach (var path in _selectedPaths)
            path.Operation = operation;

        MarkDirty();
    }

    private void CenterShape()
    {
        if (_selectedPaths.Count == 0) return;

        // If the selection contains any non-clip paths, ignore clip paths when
        // computing bounds so masks don't skew the center.
        var hasNonClip = false;
        foreach (var p in _selectedPaths)
        {
            if (!p.IsClip) { hasNonClip = true; break; }
        }

        var min = new Vector2(float.MaxValue, float.MaxValue);
        var max = new Vector2(float.MinValue, float.MinValue);
        var hasAnchors = false;

        foreach (var p in _selectedPaths)
        {
            if (hasNonClip && p.IsClip) continue;
            if (p.TotalAnchorCount == 0) continue;
            hasAnchors = true;
            p.UpdateSamples();
            p.UpdateBounds();
            var b = p.Bounds;
            min = Vector2.Min(min, new Vector2(b.X, b.Y));
            max = Vector2.Max(max, new Vector2(b.Right, b.Bottom));
        }

        if (!hasAnchors) return;

        Undo.Record(Document);

        var dpi = EditorApplication.Config.PixelsPerUnit;
        var invDpi = 1f / dpi;
        var centerWorld = (min + max) * 0.5f;
        var center = new Vector2(
            MathF.Round(centerWorld.X * dpi) * invDpi,
            MathF.Round(centerWorld.Y * dpi) * invDpi);

        // Translate every selected path (clips included) so masks stay aligned.
        foreach (var p in _selectedPaths)
        {
            foreach (var contour in p.Contours)
            {
                for (var i = 0; i < contour.Anchors.Count; i++)
                {
                    var a = contour.Anchors[i];
                    a.Position -= center;
                    contour.Anchors[i] = a;
                }
            }
            p.MarkDirty();
        }

        MarkDirty();
    }

    private void FlipAxis(bool horizontal)
    {
        if (_selectedPaths.Count == 0) return;

        Undo.Record(Document);

        var min = new Vector2(float.MaxValue);
        var max = new Vector2(float.MinValue);
        foreach (var path in _selectedPaths)
        {
            var b = path.Bounds;
            min = Vector2.Min(min, b.Min);
            max = Vector2.Max(max, b.Max);
        }
        var sharedCenter = (min + max) * 0.5f;

        foreach (var path in _selectedPaths)
        {
            var oldCenter = path.LocalBounds.Center;
            var oldTransform = path.PathTransform;
            Matrix3x2.Invert(oldTransform, out var inverse);

            foreach (var contour in path.Contours)
            {
                for (var i = 0; i < contour.Anchors.Count; i++)
                {
                    var a = contour.Anchors[i];
                    var world = Vector2.Transform(a.Position, oldTransform);
                    world = horizontal
                        ? new Vector2(2 * sharedCenter.X - world.X, world.Y)
                        : new Vector2(world.X, 2 * sharedCenter.Y - world.Y);
                    a.Position = Vector2.Transform(world, inverse);
                    a.Curve = -a.Curve;
                    contour.Anchors[i] = a;
                }
            }

            path.MarkDirty();
            path.UpdateSamples();
            path.UpdateBounds();
            path.CompensateTranslation(oldCenter);
        }

        MarkDirty();
    }

    #region Boolean Operations

    private void BooleanUnion() => BooleanOp(Clipper2Lib.ClipType.Union);
    private void BooleanSubtract() => BooleanOp(Clipper2Lib.ClipType.Difference);
    private void BooleanIntersect() => BooleanOp(Clipper2Lib.ClipType.Intersection);

    private void BooleanOp(Clipper2Lib.ClipType clipType)
    {
        if (_selectedPaths.Count < 2) return;

        var subject = _selectedPaths[0].GetClipperPaths();
        if (subject.Count == 0) return;

        // Accumulate clip paths from remaining selected paths
        var clip = new Clipper2Lib.PathsD();
        for (var i = 1; i < _selectedPaths.Count; i++)
        {
            var paths = _selectedPaths[i].GetClipperPaths();
            clip.AddRange(paths);
        }
        if (clip.Count == 0) return;

        // Run the boolean operation
        var result = Clipper2Lib.Clipper.BooleanOp(clipType,
            subject, clip, Clipper2Lib.FillRule.NonZero, precision: 6);
        if (result.Count == 0) return;

        Undo.Record(Document);

        // Create result path inheriting properties from the first selected path
        var firstPath = _selectedPaths[0];
        var resultPath = SpritePathClipper.PathsToSpritePath(
            result, firstPath.FillColor, firstPath.StrokeColor, firstPath.StrokeWidth, strokeJoin: firstPath.StrokeJoin);

        // Insert result into the first path's parent at its position
        var parent = firstPath.Parent ?? Document.Root;
        var insertIndex = parent.Children.IndexOf(firstPath);

        // Remove all original paths
        foreach (var path in _selectedPaths)
            path.RemoveFromParent();

        // Insert result
        if (insertIndex >= 0 && insertIndex <= parent.Children.Count)
            parent.Insert(insertIndex, resultPath);
        else
            parent.Add(resultPath);

        resultPath.SelectPath();
        resultPath.UpdateSamples();
        resultPath.UpdateBounds();
        MarkDirty();
        RebuildSelectedPaths();
    }

    #endregion

    private void MovePathInOrder(int direction)
    {
        if (_selectedPaths.Count == 0) return;

        // Group selected paths by parent layer, constrain moves within each layer
        var moved = false;
        Undo.Record(Document);

        // Process in correct order to avoid index conflicts:
        // moving forward (direction > 0) = process from end, backward = process from start
        var ordered = direction > 0
            ? _selectedPaths.OrderByDescending(p => p.Parent?.Children.IndexOf(p) ?? -1)
            : _selectedPaths.OrderBy(p => p.Parent?.Children.IndexOf(p) ?? -1);

        foreach (var path in ordered)
        {
            var parent = path.Parent;
            if (parent == null) continue;

            var idx = parent.Children.IndexOf(path);
            var newIdx = idx + direction;
            if (idx < 0 || newIdx < 0 || newIdx >= parent.Children.Count) continue;

            parent.RemoveAt(idx);
            parent.Insert(newIdx, path);
            moved = true;
        }

        if (!moved)
            Undo.Cancel();
        else
            MarkDirty();
    }

    private void UpdateAnimation()
    {
        if (!_isPlaying || Document.TotalTimeSlots <= 1)
            return;

        _playTimer += Time.DeltaTime;
        var slotDuration = 1f / SpriteDocument.DefaultFrameRate;

        if (_playTimer >= slotDuration)
        {
            _playTimer = 0;
            var nextSlot = (_currentTimeSlot + 1) % Document.TotalTimeSlots;
            SetCurrentTimeSlot(nextSlot);
        }
    }

    private static readonly List<SpritePath> _pathHitResults = [];
    private static readonly List<SpritePath> _hitPaths = [];

    internal bool HandlePathClick(Vector2 localMousePos, bool shift)
    {
        _pathHitResults.Clear();
        ActiveRoot.HitTestPath(localMousePos, _pathHitResults);

        HitPaths.Clear();
        foreach (var p in _pathHitResults)
            HitPaths.Add(p);

        if (HitPaths.Count == 0)
            return false;

        if (!shift)
        {
            // Without shift: select topmost, or cycle if already selected
            var topmost = HitPaths[0];
            if (topmost.IsSelected)
            {
                // Cycle: find next unselected, or wrap
                SpritePath? next = null;
                for (var i = 1; i < HitPaths.Count; i++)
                {
                    if (!HitPaths[i].IsSelected)
                    {
                        next = HitPaths[i];
                        break;
                    }
                }

                Document.Root.ClearSelection();
                (next ?? HitPaths[0]).SelectPath();
            }
            else
            {
                Document.Root.ClearSelection();
                topmost.SelectPath();
            }
        }
        else
        {
            // Shift: add the next unselected path to selection
            SpritePath? nextUnselected = null;
            foreach (var p in HitPaths)
            {
                if (!p.IsSelected)
                {
                    nextUnselected = p;
                    break;
                }
            }

            if (nextUnselected != null)
            {
                nextUnselected.SelectPath();
            }
            else if (HitPaths.Count > 0)
            {
                // All selected — wrap: deselect topmost, it cycles visually
                HitPaths[0].DeselectPath();
            }
        }

        RebuildSelectedPaths();
        return true;
    }

    private void DrawWireframe()
    {
        if (_selectedPaths.Count == 0) return;

        var transform = Document.Transform;

        if (CurrentMode != SpriteEditMode.Transform)
        {
            // Anchor-based modes: draw anchors and edges for selected paths
            foreach (var path in _selectedPaths)
            {
                if (path.TotalAnchorCount < 2) continue;
                path.UpdateSamples();
                var localTransform = path.HasTransform ? path.PathTransform : Matrix3x2.Identity;
                DrawPathSegments(path, localTransform, transform);
                DrawPathAnchors(path, localTransform, transform);
            }
        }
        else
        {
            // V mode: draw path outlines and bounding box around each selected path
            DrawSelectedPathOutlines(transform);
            DrawSelectionBounds(transform);
        }
    }

    private void UpdateHandleCursor()
    {
        _hoverPath = null;
        _hoverAnchorIndex = -1;
        _hoverHandle = SpritePathHandle.None;

        // Modes that manage their own cursor set it from Mode.Update() in LateUpdate.
        // Don't stomp them here or the cursor never reaches the element tree.
        if (CurrentMode != SpriteEditMode.Transform &&
            CurrentMode != SpriteEditMode.Anchor &&
            CurrentMode != SpriteEditMode.Bevel)
            return;

        if (_selectedPaths.Count == 0)
        {
            SetCursor(SpritePathHandle.None);
            return;
        }

        Matrix3x2.Invert(Document.Transform, out var invTransform);
        var localMousePos = Vector2.Transform(Workspace.MouseWorldPosition, invTransform);

        if (CurrentMode == SpriteEditMode.Transform)
        {
            _hoverHandle = HitTestHandles(localMousePos);
            SetCursor(_hoverHandle);
        }
        else if (CurrentMode == SpriteEditMode.Anchor || CurrentMode == SpriteEditMode.Bevel)
        {
            if (Input.IsAltDown(InputScope.All))
            {
                var segHit = ActiveRoot.HitTestSegment(localMousePos, onlySelected: true);
                if (segHit.HasValue)
                {
                    EditorCursor.SetCrosshair();
                    return;
                }
            }

            var hit = ActiveRoot.HitTestAnchor(localMousePos, onlySelected: true);
            if (hit.HasValue)
            {
                _hoverPath = hit.Value.Path;
                _hoverAnchorIndex = hit.Value.AnchorIndex;
                SetCursor(SpritePathHandle.Move);
                return;
            }
            else
                SetCursor(SpritePathHandle.None);
        }
        else
            SetCursor(SpritePathHandle.None);
    }

    private void DrawSelectedPathOutlines(Matrix3x2 docTransform)
    {
        var lineWidth = EditorStyle.SpritePath.SegmentLineWidth * 0.5f;

        using var _ = Gizmos.PushState(EditorLayer.DocumentEditor);
        Graphics.SetTransform(docTransform);
        Gizmos.SetColor(EditorStyle.Palette.Primary);

        foreach (var path in _selectedPaths)
        {
            if (path.TotalAnchorCount < 2) continue;
            path.UpdateSamples();
            var localTransform = path.HasTransform ? path.PathTransform : Matrix3x2.Identity;

            for (var ci = 0; ci < path.Contours.Count; ci++)
            {
                var contour = path.Contours[ci];
                var segmentCount = contour.Open ? contour.Anchors.Count - 1 : contour.Anchors.Count;
                for (var i = 0; i < segmentCount; i++)
                    DrawContourSegment(contour, i, localTransform, lineWidth, 1);
            }
        }
    }

    private void DrawSelectionBounds(Matrix3x2 transform)
    {
        if (_selectedPaths.Count == 0) return;

        UpdateSelectionBounds();

        var bounds = _selectionLocalBounds;
        if (bounds.Width <= 0 && bounds.Height <= 0) return;

        using var _ = Gizmos.PushState(EditorLayer.DocumentEditor);

        // Draw the oriented bounding box
        var selToDoc = Matrix3x2.CreateRotation(_selectionRotation);
        Graphics.SetTransform(selToDoc * transform);

        Gizmos.SetColor(EditorStyle.Palette.Primary);
        var lineWidth = EditorStyle.SpritePath.SegmentLineWidth;
        var tl = new Vector2(bounds.X, bounds.Y);
        var tr = new Vector2(bounds.Right, bounds.Y);
        var br = new Vector2(bounds.Right, bounds.Bottom);
        var bl = new Vector2(bounds.X, bounds.Bottom);
        Gizmos.DrawLine(tl, tr, lineWidth, order: 2);
        Gizmos.DrawLine(tr, br, lineWidth, order: 2);
        Gizmos.DrawLine(br, bl, lineWidth, order: 2);
        Gizmos.DrawLine(bl, tl, lineWidth, order: 2);

        // Draw handles at corners and edge midpoints (in selection space)
        var midX = bounds.X + bounds.Width * 0.5f;
        var midY = bounds.Y + bounds.Height * 0.5f;

        var h = _hoverHandle;
        Gizmos.DrawAnchor(tl, selected: h is SpritePathHandle.ScaleTopLeft, order: 6);
        Gizmos.DrawAnchor(tr, selected: h is SpritePathHandle.ScaleTopRight, order: 6);
        Gizmos.DrawAnchor(br, selected: h is SpritePathHandle.ScaleBottomRight, order: 6);
        Gizmos.DrawAnchor(bl, selected: h is SpritePathHandle.ScaleBottomLeft, order: 6);
        Gizmos.DrawAnchor(new Vector2(midX, bounds.Y), selected: h is SpritePathHandle.ScaleTop, order: 6);
        Gizmos.DrawAnchor(new Vector2(midX, bounds.Bottom), selected: h is SpritePathHandle.ScaleBottom, order: 6);
        Gizmos.DrawAnchor(new Vector2(bounds.X, midY), selected: h is SpritePathHandle.ScaleLeft, order: 6);
        Gizmos.DrawAnchor(new Vector2(bounds.Right, midY), selected: h is SpritePathHandle.ScaleRight, order: 6);

        // Draw rotation handles at offset positions outside corners
        var center = new Vector2(midX, midY);
        var rotScale = EditorStyle.SpritePath.RotateHandleScale;
        Gizmos.DrawAnchor(GetRotateHandleOffset(tl, center), selected: h is SpritePathHandle.RotateTopLeft, scale: rotScale, order: 6);
        Gizmos.DrawAnchor(GetRotateHandleOffset(tr, center), selected: h is SpritePathHandle.RotateTopRight, scale: rotScale, order: 6);
        Gizmos.DrawAnchor(GetRotateHandleOffset(br, center), selected: h is SpritePathHandle.RotateBottomRight, scale: rotScale, order: 6);
        Gizmos.DrawAnchor(GetRotateHandleOffset(bl, center), selected: h is SpritePathHandle.RotateBottomLeft, scale: rotScale, order: 6);
    }

    private static Vector2 GetRotateHandleOffset(Vector2 corner, Vector2 boundsCenter)
    {
        var dir = Vector2.Normalize(corner - boundsCenter);
        var offset = EditorStyle.SpritePath.RotateHandleOffset * Gizmos.ZoomRefScale;
        return corner + dir * offset;
    }

    internal static bool IsScaleHandle(SpritePathHandle hit) => hit >= SpritePathHandle.ScaleTopLeft && hit <= SpritePathHandle.ScaleLeft;
    internal static bool IsRotateHandle(SpritePathHandle hit) => hit >= SpritePathHandle.RotateTopLeft && hit <= SpritePathHandle.RotateBottomLeft;

    // Hit test handles in selection-rotated space
    internal SpritePathHandle HitTestHandles(Vector2 docLocalPos)
    {
        var bounds = _selectionLocalBounds;
        if (bounds.Width <= 0 && bounds.Height <= 0) return SpritePathHandle.None;

        // Transform from document-local to selection-local space
        var selPos = Vector2.Transform(docLocalPos, Matrix3x2.CreateRotation(-_selectionRotation));

        var hitRadius = EditorStyle.SpritePath.AnchorHitRadius;
        var hitRadiusSqr = hitRadius * hitRadius;

        var midX = bounds.X + bounds.Width * 0.5f;
        var midY = bounds.Y + bounds.Height * 0.5f;

        // Corner positions in selection space
        Span<Vector2> corners = stackalloc Vector2[4];
        corners[0] = new Vector2(bounds.X, bounds.Y);
        corners[1] = new Vector2(bounds.Right, bounds.Y);
        corners[2] = new Vector2(bounds.Right, bounds.Bottom);
        corners[3] = new Vector2(bounds.X, bounds.Bottom);

        // Test rotation handles at offset positions outside corners
        var boundsCenter = new Vector2(midX, midY);
        var rotateHitRadius = hitRadius * EditorStyle.SpritePath.RotateHandleScale;
        var rotateHitRadiusSqr = rotateHitRadius * rotateHitRadius;
        for (var i = 0; i < 4; i++)
        {
            var rotPos = GetRotateHandleOffset(corners[i], boundsCenter);
            if (Vector2.DistanceSquared(selPos, rotPos) <= rotateHitRadiusSqr)
                return SpritePathHandle.RotateTopLeft + i;
        }

        // Test corner scale handles
        for (var i = 0; i < 4; i++)
        {
            if (Vector2.DistanceSquared(selPos, corners[i]) <= hitRadiusSqr)
                return SpritePathHandle.ScaleTopLeft + i * 2;
        }

        // Edge midpoint handles
        Span<Vector2> edges = stackalloc Vector2[4];
        edges[0] = new Vector2(midX, bounds.Y);
        edges[1] = new Vector2(bounds.Right, midY);
        edges[2] = new Vector2(midX, bounds.Bottom);
        edges[3] = new Vector2(bounds.X, midY);

        Span<SpritePathHandle> edgeHits = stackalloc SpritePathHandle[4];
        edgeHits[0] = SpritePathHandle.ScaleTop;
        edgeHits[1] = SpritePathHandle.ScaleRight;
        edgeHits[2] = SpritePathHandle.ScaleBottom;
        edgeHits[3] = SpritePathHandle.ScaleLeft;

        for (var i = 0; i < 4; i++)
        {
            if (Vector2.DistanceSquared(selPos, edges[i]) <= hitRadiusSqr)
                return edgeHits[i];
        }

        // Inside oriented bbox = move (test in selection space using axis-aligned check)
        if (bounds.Contains(selPos))
            return SpritePathHandle.Move;

        return SpritePathHandle.None;
    }

    private void SetCursor(SpritePathHandle hit)
    {
        if (hit == SpritePathHandle.None)
            EditorCursor.SetArrow();
        else if (hit == SpritePathHandle.Move)
            EditorCursor.SetMove();
        else if (hit is SpritePathHandle.RotateTopLeft or SpritePathHandle.RotateTopRight or
                 SpritePathHandle.RotateBottomRight or SpritePathHandle.RotateBottomLeft)
            EditorCursor.SetRotate(hit, _selectionRotation);
        else
            EditorCursor.SetScale(hit, _selectionRotation);
    }

    // Get the handle position in selection-local space
    private Vector2 GetHandlePositionInSelSpace(SpritePathHandle hit)
    {
        var b = _selectionLocalBounds;
        var midX = b.X + b.Width * 0.5f;
        var midY = b.Y + b.Height * 0.5f;

        return hit switch
        {
            SpritePathHandle.ScaleTopLeft => new Vector2(b.X, b.Y),
            SpritePathHandle.ScaleTop => new Vector2(midX, b.Y),
            SpritePathHandle.ScaleTopRight => new Vector2(b.Right, b.Y),
            SpritePathHandle.ScaleRight => new Vector2(b.Right, midY),
            SpritePathHandle.ScaleBottomRight => new Vector2(b.Right, b.Bottom),
            SpritePathHandle.ScaleBottom => new Vector2(midX, b.Bottom),
            SpritePathHandle.ScaleBottomLeft => new Vector2(b.X, b.Bottom),
            SpritePathHandle.ScaleLeft => new Vector2(b.X, midY),
            _ => b.Center,
        };
    }

    // Get the pivot (opposite handle) in selection-local space
    internal Vector2 GetOppositePivotInSelSpace(SpritePathHandle hit)
    {
        var b = _selectionLocalBounds;
        var midX = b.X + b.Width * 0.5f;
        var midY = b.Y + b.Height * 0.5f;

        return hit switch
        {
            SpritePathHandle.ScaleTopLeft => new Vector2(b.Right, b.Bottom),
            SpritePathHandle.ScaleTop => new Vector2(midX, b.Bottom),
            SpritePathHandle.ScaleTopRight => new Vector2(b.X, b.Bottom),
            SpritePathHandle.ScaleRight => new Vector2(b.X, midY),
            SpritePathHandle.ScaleBottomRight => new Vector2(b.X, b.Y),
            SpritePathHandle.ScaleBottom => new Vector2(midX, b.Y),
            SpritePathHandle.ScaleBottomLeft => new Vector2(b.Right, b.Y),
            SpritePathHandle.ScaleLeft => new Vector2(b.Right, midY),
            _ => b.Center,
        };
    }

    private void PathInspectorUI()
    {
        if (!HasPathSelection || HasLayerSelection)
            return;

        using (Inspector.BeginSection("PATH"))
        {
            if (Inspector.IsSectionCollapsed)
                return;

            using (Inspector.BeginProperty("Operation"))
            using (UI.BeginRow(EditorStyle.Control.Spacing))
            {
                if (UI.Button(WidgetIds.PathNormal, EditorAssets.Sprites.IconFill, EditorStyle.Button.ToggleIcon, isSelected: Document.CurrentOperation == SpritePathOperation.Normal))
                    SetSpritePathOperation(SpritePathOperation.Normal);

                if (UI.Button(WidgetIds.PathSubtract, EditorAssets.Sprites.IconSubtract, EditorStyle.Button.ToggleIcon, isSelected: Document.CurrentOperation == SpritePathOperation.Subtract))
                    SetSpritePathOperation(SpritePathOperation.Subtract);

                if (UI.Button(WidgetIds.PathClip, EditorAssets.Sprites.IconClip, EditorStyle.Button.ToggleIcon, isSelected: Document.CurrentOperation == SpritePathOperation.Clip))
                    SetSpritePathOperation(SpritePathOperation.Clip);
            }

            if (_selectedPaths.Count >= 2)
            {
                using (Inspector.BeginProperty("Boolean"))
                using (UI.BeginRow(EditorStyle.Control.Spacing))
                {
                    if (UI.Button(WidgetIds.BoolUnion, "Union", EditorStyle.Button.Secondary))
                        BooleanUnion();
                    if (UI.Button(WidgetIds.BoolSubtract, "Subtract", EditorStyle.Button.Secondary))
                        BooleanSubtract();
                    if (UI.Button(WidgetIds.BoolIntersect, "Intersect", EditorStyle.Button.Secondary))
                        BooleanIntersect();
                }
            }

            if (Document.CurrentOperation != SpritePathOperation.Subtract)
            {
                using (Inspector.BeginProperty("Fill"))
                {
                    var fillColor = EditorUI.ColorButton(WidgetIds.FillColor, Document.CurrentFillColor.ToColor());

                    if (UI.WasChangeStarted()) Undo.Record(Document);

                    if (UI.WasChanged())
                        SetFillColor(fillColor.ToColor32());

                    if (UI.WasChangeCancelled()) Undo.Cancel();
                }

                using (Inspector.BeginProperty("Stroke"))
                using (UI.BeginRow(EditorStyle.Control.Spacing))
                {
                    var strokeColor = Document.CurrentStrokeColor.ToColor();
                    var newStrokeColor = EditorUI.ColorButton(WidgetIds.StrokeColor, strokeColor);

                    if (UI.WasChangeStarted()) Undo.Record(Document);
                    if (UI.WasChanged()) SetStrokeColor(newStrokeColor.ToColor32());
                    if (UI.WasChangeCancelled()) Undo.Cancel();

                    if (newStrokeColor.A > 0)
                        StrokeWidthButtonUI();
                }

                if (Document.CurrentStrokeColor.A > 0)
                {
                    using (Inspector.BeginProperty("Join"))
                    using (UI.BeginRow(EditorStyle.Control.Spacing))
                    {
                        if (UI.Button(WidgetIds.StrokeJoinRound, EditorAssets.Sprites.IconStrokeJoinRound, EditorStyle.Button.ToggleIcon, isSelected: Document.CurrentStrokeJoin == SpriteStrokeJoin.Round))
                            SetStrokeJoin(SpriteStrokeJoin.Round);

                        if (UI.Button(WidgetIds.StrokeJoinMiter, EditorAssets.Sprites.IconStrokeJoinMiter, EditorStyle.Button.ToggleIcon, isSelected: Document.CurrentStrokeJoin == SpriteStrokeJoin.Miter))
                            SetStrokeJoin(SpriteStrokeJoin.Miter);

                        if (UI.Button(WidgetIds.StrokeJoinBevel, EditorAssets.Sprites.IconStrokeJoinBevel, EditorStyle.Button.ToggleIcon, isSelected: Document.CurrentStrokeJoin == SpriteStrokeJoin.Bevel))
                            SetStrokeJoin(SpriteStrokeJoin.Bevel);
                    }
                }
            }
        }
    }

    private void OutlineInspectorUI()
    {
        using (Inspector.BeginSection("OUTLINE"))
        {
            if (Inspector.IsSectionCollapsed)
                return;

            using (Inspector.BeginProperty("Color"))
            using (UI.BeginRow(EditorStyle.Control.Spacing))
            {
                var newColor = EditorUI.ColorButton(WidgetIds.OutlineColor, Document.OutlineColor.ToColor());

                if (UI.WasChangeStarted()) Undo.Record(Document);
                if (UI.WasChanged()) SetOutlineColor(newColor.ToColor32());
                if (UI.WasChangeCancelled()) Undo.Cancel();

                if (newColor.A > 0)
                    OutlineSizeButtonUI();
            }
        }
    }

    public override void InspectorUI()
    {
        GroupInspectorUI();
        EdgesInspectorUI();
        OutlineInspectorUI();
        PathInspectorUI();
    }

    protected override void ExitEdgeEditMode()
    {
        // CurrentMode enum still holds the pre-edge value; the loosened early-out
        // in SetMode(SpriteEditMode) lets us re-instantiate that mode here.
        SetMode(CurrentMode);
    }

    internal void ApplyEyeDropperColor(Color32 color, bool stroke)
    {
        Undo.Record(Document);
        if (stroke)
        {
            SetStrokeColor(color);
            if (ColorPicker.IsOpen(WidgetIds.StrokeColor))
                ColorPicker.Open(WidgetIds.StrokeColor, color.ToColor());
        }
        else
        {
            SetFillColor(color);
            if (ColorPicker.IsOpen(WidgetIds.FillColor))
                ColorPicker.Open(WidgetIds.FillColor, color.ToColor());
        }
    }

    internal Color32 GetPixelAt(Vector2 worldPos)
    {
        var size = Document.RasterBounds.Size;
        if (size.X <= 0 || size.Y <= 0)
            return default;

        Matrix3x2.Invert(Document.Transform, out var inv);
        var local = Vector2.Transform(worldPos, inv);

        var bounds = Document.Bounds;
        var px = (int)((local.X - bounds.X) / bounds.Width * size.X);
        var py = (int)((local.Y - bounds.Y) / bounds.Height * size.Y);

        if (px < 0 || px >= size.X || py < 0 || py >= size.Y)
            return default;

        using var pixels = new PixelData<Color32>(size);
        var dpi = Document.PixelsPerUnit;
        var targetRect = new RectInt(Vector2Int.Zero, size);
        var sourceOffset = -Document.RasterBounds.Position;
        VectorSpriteDocument.RasterizeLayer(Document.Root, pixels, targetRect, sourceOffset, dpi, clipRect: null, outlineSource: Document);

        return pixels[px, py];
    }

    public override void ToolbarUI()
    {
        base.ToolbarUI();

        if (UI.Button(WidgetIds.LayerToggle, EditorAssets.Sprites.IconLayer, EditorStyle.Button.ToggleIcon, isSelected: _showLayers))  
            _showLayers = !_showLayers;

        if (UI.Button(WidgetIds.ExitEditMode, EditorAssets.Sprites.IconEdit, EditorStyle.Button.ToggleIcon, isSelected: true))  
            Workspace.EndEdit();

        if (UI.Button(WidgetIds.IsolationToggle, EditorAssets.Sprites.IconIsolate, EditorStyle.Button.ToggleIcon, isSelected: Workspace.Isolation))  
            Workspace.ToggleIsolation();

        UI.Flex();

        if (UI.Button(WidgetIds.InspectorToggle, EditorAssets.Sprites.IconInfo, EditorStyle.Button.ToggleIcon, isSelected: _showInspector))  
            _showInspector = !_showInspector;
    }
}
