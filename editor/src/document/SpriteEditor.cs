//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;
using System.Runtime.InteropServices;

namespace NoZ.Editor;

public partial class SpriteEditor : DocumentEditor
{
    [ElementId("Root")]
    [ElementId("TileButton")]
    [ElementId("BoneBindButton")]
    [ElementId("BoneUnbindButton")]
    [ElementId("SubtractButton")]
    [ElementId("FirstOpacity")]
    [ElementId("PreviewButton")]
    [ElementId("SkeletonOverlayButton")]
    [ElementId("DopeSheet")]
    [ElementId("ConstraintsButton")]
    [ElementId("FillColorButton")]
    [ElementId("StrokeColorButton")]
    [ElementId("LayerButton")]
    [ElementId("BonePathButton")]
    [ElementId("StrokeWidth")]
    [ElementId("AddFrameButton")]
    [ElementId("GenerateButton")]
    [ElementId("PlayButton")]
    [ElementId("LayerPanel")]
    [ElementId("LayerPanelScroll")]
    [ElementId("LayerItem", 32)]
    [ElementId("LayerVisibility", 32)]
    [ElementId("LayerLock", 32)]
    [ElementId("LayerSortOrder", 32)]
    [ElementId("AddVectorLayerBtn")]
    [ElementId("AddGeneratedLayerBtn")]
    [ElementId("DeleteLayerBtn")]
    private static partial class ElementId { }

    public new SpriteDocument Document => (SpriteDocument)base.Document;

    private int _currentTimeSlot;
    private bool _isPlaying;
    private float _playTimer;
    private bool _rasterDirty = true;
    private readonly Vector2[] _savedPositions = new Vector2[Shape.MaxAnchors];
    private readonly float[] _savedCurves = new float[Shape.MaxAnchors];
    private Action<Color32>? _previewFillColor;
    private Action<Color32>? _previewStrokeColor;

    /// <summary>The current layer's frame index resolved from the global time slot.</summary>
    private int CurrentLayerFrameIndex =>
        Document.GetLayerFrameAtTimeSlot(Document.CurrentDocumentLayer, _currentTimeSlot);

    /// <summary>The current layer's current frame's shape (for editing).</summary>
    private Shape CurrentShape =>
        Document.DocumentLayers[Document.CurrentDocumentLayer].Frames[CurrentLayerFrameIndex].Shape;

    /// <summary>The current document layer.</summary>
    private DocumentLayer CurrentLayer =>
        Document.DocumentLayers[Document.CurrentDocumentLayer];

    private bool IsPathOnVisibleLayer(Shape.Path path)
    {
        var layers = Document.DocumentLayers;
        return path.DocLayer < layers.Count && layers[path.DocLayer].Visible;
    }

    private bool IsPathOnLockedLayer(Shape.Path path)
    {
        var layers = Document.DocumentLayers;
        return path.DocLayer < layers.Count && layers[path.DocLayer].Locked;
    }

    private bool IsCurrentLayerLocked()
    {
        var layer = Document.GetCurrentDocumentLayer();
        return layer is { Locked: true };
    }

    public SpriteEditor(SpriteDocument doc) : base(doc)
    {
        Workspace.XrayModeChanged += OnXrayModeChanged;

        var deleteCommand = new Command { Name = "Delete", Handler = DeleteSelected, Key = InputCode.KeyX, Icon = EditorAssets.Sprites.IconDelete };
        var exitEditCommand = new Command { Name = "Exit Edit Mode", Handler = Workspace.EndEdit, Key = InputCode.KeyTab };
        var moveCommand = new Command { Name = "Move", Handler = BeginMoveTool, Key = InputCode.KeyG, Icon = EditorAssets.Sprites.IconMove };
        var originToCenterCommand = new Command { Name = "Origin to Center", Handler = CenterShape, Key = InputCode.KeyC, Shift = true };
        var rotateCommand = new Command { Name = "Rotate", Handler = BeginRotateTool, Key = InputCode.KeyR };
        var scaleCommand = new Command { Name = "Scale", Handler = OnScale, Key = InputCode.KeyS };
        var flipHorizontalCommand = new Command { Name = "Flip Horizontal", Handler = FlipHorizontal };
        var flipVerticalCommand = new Command { Name = "Flip Vertical", Handler = FlipVertical };
        var copyCommand = new Command { Name = "Copy", Handler = CopySelected, Key = InputCode.KeyC, Ctrl = true };
        var pasteCommand = new Command { Name = "Paste", Handler = PasteSelected, Key = InputCode.KeyV, Ctrl = true };
        var bringForwardCommand = new Command { Name = "Bring Forward", Handler = MovePathUp, Key = InputCode.KeyLeftBracket };
        var sendBackwardCommand = new Command { Name = "Send Backwar", Handler = MovePathDown, Key = InputCode.KeyRightBracket };

        Commands =
        [
            deleteCommand,
            exitEditCommand,
            moveCommand,
            originToCenterCommand,
            rotateCommand,
            scaleCommand,
            flipHorizontalCommand,
            flipVerticalCommand,
            copyCommand,
            pasteCommand,
            bringForwardCommand,
            sendBackwardCommand,
            new Command { Name = "Toggle Playback", Handler = TogglePlayback, Key = InputCode.KeySpace },
            new Command { Name = "Previous Frame", Handler = PreviousFrame, Key = InputCode.KeyQ },
            new Command { Name = "Next Frame", Handler = NextFrame, Key = InputCode.KeyE },
            new Command { Name = "Insert Frame Before", Handler = InsertFrameBefore, Key = InputCode.KeyI },
            new Command { Name = "Insert Frame After", Handler = InsertFrameAfter, Key = InputCode.KeyO },
            new Command { Name = "Delete Frame", Handler = DeleteCurrentFrame, Key = InputCode.KeyX, Shift = true },
            new Command { Name = "Add Hold", Handler = AddHoldFrame, Key = InputCode.KeyH },
            new Command { Name = "Remove Hold", Handler = RemoveHoldFrame, Key = InputCode.KeyH, Ctrl = true },
            new Command { Name = "Curve", Handler = BeginCurveTool, Key = InputCode.KeyC },
            new Command { Name = "Select All", Handler = SelectAll, Key = InputCode.KeyA },
            new Command { Name = "Insert Anchor", Handler = InsertAnchorAtHover, Key = InputCode.KeyV },
            new Command { Name = "Pen Tool", Handler = BeginPenTool, Key = InputCode.KeyP },
            new Command { Name = "Knife Tool", Handler = BeginKnifeTool, Key = InputCode.KeyK },
            new Command { Name = "Rectangle Tool", Handler = BeginRectangleTool, Key = InputCode.KeyR, Ctrl = true },
            new Command { Name = "Circle Tool", Handler = BeginCircleTool, Key = InputCode.KeyO, Ctrl = true },
            new Command { Name = "Duplicate", Handler = DuplicateSelected, Key = InputCode.KeyD, Ctrl = true },
            new Command { Name = "Copy", Handler = CopySelected, Key = InputCode.KeyC, Ctrl = true },
            new Command { Name = "Paste", Handler = PasteSelected, Key = InputCode.KeyV, Ctrl = true },
            new Command { Name = "Generate", Handler = () => Document.GenerateAsync(), Key = InputCode.KeyG, Ctrl = true },
        ];

        bool HasSelection() => CurrentShape.HasSelection();
        bool HasSelectedPaths() => CurrentShape.HasSelectedPaths();

        ContextMenu = new PopupMenuDef
        {
            Title = "Sprite",
            Items =
            [
                PopupMenuItem.FromCommand(copyCommand, enabled: HasSelection),
                PopupMenuItem.FromCommand(pasteCommand, enabled: () => Clipboard.Is<PathClipboardData>()),
                PopupMenuItem.Separator(),

                PopupMenuItem.FromCommand(moveCommand, enabled: HasSelection),
                PopupMenuItem.FromCommand(rotateCommand, enabled: HasSelection),
                PopupMenuItem.FromCommand(scaleCommand, enabled: HasSelection),
                PopupMenuItem.FromCommand(flipHorizontalCommand, enabled: HasSelectedPaths),
                PopupMenuItem.FromCommand(flipVerticalCommand, enabled: HasSelectedPaths),
                PopupMenuItem.Separator(),

                PopupMenuItem.Submenu("Arrange"),
                PopupMenuItem.FromCommand(bringForwardCommand, enabled: HasSelectedPaths, level: 1),
                PopupMenuItem.FromCommand(sendBackwardCommand, enabled: HasSelectedPaths, level: 1),

                PopupMenuItem.Submenu("Set Origin"),
                PopupMenuItem.FromCommand(originToCenterCommand, level: 1),
                PopupMenuItem.Separator(),
                PopupMenuItem.FromCommand(deleteCommand, enabled: HasSelection),
                PopupMenuItem.Separator(),
                PopupMenuItem.FromCommand(exitEditCommand),
            ]
        };  
    }

    public int CurrentTimeSlot => _currentTimeSlot;
    public bool IsPlaying => _isPlaying;

    public override void Dispose()
    {
        ClearSelection();
        EditorUI.ClosePopup();

        Workspace.XrayModeChanged -= OnXrayModeChanged;

        if (Document.IsModified)
            AtlasManager.UpdateSource(Document);

        base.Dispose();
    }

    public override void OnUndoRedo()
    {
        Document.UpdateBounds();
        MarkRasterDirty();
    }

    private void OnXrayModeChanged(bool _) => MarkRasterDirty();

    public override void Update()
    {
        UpdateAnimation();
        UpdateInput();

        if (_rasterDirty)
            UpdateRaster();

        var shape = CurrentShape;

        DrawRaster();

        using (Gizmos.PushState(EditorLayer.DocumentEditor))
        {
            Graphics.SetTransform(Document.Transform);
            Graphics.SetSortGroup(5);
            Document.DrawOrigin();
            Graphics.SetSortGroup(4);
            DrawSegments(shape);
            DrawAnchors(shape);
        }

        if (Document.ShowSkeletonOverlay)
            DrawSkeletonOverlay();

        if (!Document.Edges.IsZero && Document.ConstrainedSize.HasValue)
            DrawEdges();
    }

    public override void LateUpdate()
    {
        if (Workspace.DragStarted && Workspace.DragButton == InputCode.MouseLeft)
            HandleDragStart();
        else if (Input.WasButtonReleased(InputCode.MouseLeft))
            HandleLeftClick();
    }

    private void ToolbarUI()
    {
        using var _ = UI.BeginRow(EditorStyle.Toolbar.Root);

        // Fill color picker
        var fillColor = Document.CurrentFillColor;
        if (EditorUI.ColorPickerButton(
            ElementId.FillColorButton,
            ref fillColor,
            onPreview: _previewFillColor ??= PreviewFillColor,
            icon: EditorAssets.Sprites.IconFill))
        {
            SetFill(fillColor);
        }

        // Stroke color picker
        var strokeColor = Document.CurrentStrokeColor;
        if (EditorUI.ColorPickerButton(
            ElementId.StrokeColorButton,
            ref strokeColor,
            onPreview: _previewStrokeColor ??= PreviewStrokeColor,
            icon: EditorAssets.Sprites.IconStroke))
        {
            SetStroke(strokeColor);
        }

        StrokeWidthButtonUI();

        {
            var opIcon = Document.CurrentOperation == PathOperation.Clip
                ? EditorAssets.Sprites.IconClip
                : EditorAssets.Sprites.IconSubtract;
            var opSelected = Document.CurrentOperation != PathOperation.Normal;
            if (EditorUI.Button(ElementId.SubtractButton, opIcon, selected: opSelected, toolbar: true))
                CyclePathOperation();
        }

        EditorUI.ToolbarSpacer();

        LayerButtonUI();

        BoneBindingUI();

        if (EditorUI.Button(ElementId.AddFrameButton, EditorAssets.Sprites.IconKeyframe, toolbar: true))
            InsertFrameAfter();

        if (EditorUI.Button(ElementId.PlayButton, EditorAssets.Sprites.IconPlay, selected: _isPlaying, toolbar: true))
            TogglePlayback();

        // AI Generation button — only shown when [generate] section exists in meta
        if (Document.HasGeneration)
        {
            EditorUI.ToolbarSpacer();
            if (EditorUI.Button(ElementId.GenerateButton, EditorAssets.Sprites.IconPlay, selected: Document.IsGenerating, toolbar: true))
                Document.GenerateAsync();
        }

        UI.Flex();

        if (EditorUI.Button(ElementId.TileButton, EditorAssets.Sprites.IconTiling, Document.ShowTiling, toolbar: true))
        {
            Document.ShowTiling = !Document.ShowTiling;
            Document.MarkMetaModified();
        }

        EditorUI.ToolbarSpacer();

        SkeletonBindingUI();
        ConstraintsButtonUI();
    }

    public override void UpdateUI()
    {
        using (UI.BeginColumn(ElementId.Root, EditorStyle.DocumentEditor.Root))
        {
            ToolbarUI();
            LayerDopeSheetUI();
            UI.Spacer(EditorStyle.Control.Spacing);
        }
    }

    private void LayerDopeSheetUI()
    {
        var layers = Document.DocumentLayers;
        var maxSlots = Sprite.MaxFrames;

        using (UI.BeginColumn(new ContainerStyle { Padding = EdgeInsets.LeftRight(2) }))
        {
            // Time ruler header
            UI.Container(EditorStyle.Dopesheet.LayerSeparator);
            using (UI.BeginRow(EditorStyle.Dopesheet.HeaderContainer))
            {
                // Left column spacer for layer names
                UI.Container(new ContainerStyle { Width = LayerNameColumnWidth });
                UI.Container(EditorStyle.Dopesheet.FrameSeparator);

                var blockCount = maxSlots / 4;
                for (var blockIndex = 0; blockIndex < blockCount; blockIndex++)
                {
                    using (UI.BeginContainer(EditorStyle.Dopesheet.TimeBlock))
                        UI.Label(EditorUI.FrameTimeStrings[blockIndex], EditorStyle.Dopesheet.TimeText);
                    UI.Container(EditorStyle.Dopesheet.FrameSeparator);
                }
            }

            UI.Container(EditorStyle.Dopesheet.LayerSeparator);

            // Layer rows (reverse order — highest layer at top)
            for (int i = layers.Count - 1; i >= 0; i--)
            {
                var layer = layers[i];
                var isSelectedLayer = Document.CurrentDocumentLayer == i;

                using (UI.BeginRow(ElementId.LayerItem + i, new ContainerStyle
                    { Height = EditorStyle.Dopesheet.FrameHeight, Color = Color.FromRgb(0x2f2f2f) }))
                {
                    // Layer name column
                    using (UI.BeginRow(new ContainerStyle { Width = LayerNameColumnWidth }))
                    {
                        if (isSelectedLayer)
                            UI.Container(LayerItemSelected);
                        else if (UI.IsHovered())
                            UI.Container(LayerItemHover);

                        // Visibility toggle
                        if (EditorUI.Button(ElementId.LayerVisibility + i, EditorAssets.Sprites.IconPreview,
                            selected: layer.Visible, toolbar: true))
                        {
                            Undo.Record(Document);
                            layer.Visible = !layer.Visible;
                            Document.MarkModified();
                            MarkRasterDirty();
                        }

                        // Lock toggle
                        if (EditorUI.Button(ElementId.LayerLock + i, EditorAssets.Sprites.IconConstraint,
                            selected: layer.Locked, toolbar: true))
                        {
                            Undo.Record(Document);
                            layer.Locked = !layer.Locked;
                            Document.MarkModified();
                        }

                        // Layer name
                        using (UI.BeginContainer(new ContainerStyle { AlignY = Align.Center }))
                        {
                            var nameStyle = isSelectedLayer
                                ? EditorStyle.Control.SelectedText
                                : EditorStyle.Control.Text;
                            UI.Label(layer.Name, nameStyle);
                        }

                        // Click row to select layer
                        if (UI.WasPressed())
                            Document.CurrentDocumentLayer = i;
                    }

                    UI.Container(EditorStyle.Dopesheet.FrameSeparator);

                    // Frame cells for this layer
                    var slotIndex = 0;
                    for (ushort fi = 0; fi < layer.FrameCount && slotIndex < maxSlots; fi++)
                    {
                        var isCurrentSlot = IsTimeSlotInRange(i, fi, _currentTimeSlot);

                        using (UI.BeginRow(ElementId.DopeSheet + i * Sprite.MaxFrames + fi))
                        {
                            if (UI.WasPressed())
                            {
                                Document.CurrentDocumentLayer = i;
                                _currentTimeSlot = TimeSlotForLayerFrame(i, fi);
                                MarkRasterDirty();
                            }

                            using (UI.BeginContainer(isCurrentSlot
                                ? EditorStyle.Dopesheet.SelectedFrame
                                : EditorStyle.Dopesheet.Frame))
                            {
                                UI.Container(isCurrentSlot
                                    ? EditorStyle.Dopesheet.SelectedFrameDot
                                    : EditorStyle.Dopesheet.FrameDot);
                            }

                            slotIndex++;

                            // Hold cells
                            var hold = layer.Frames[fi].Hold;
                            for (int h = 0; h < hold && slotIndex < maxSlots; h++, slotIndex++)
                            {
                                using (UI.BeginContainer(isCurrentSlot
                                    ? EditorStyle.Dopesheet.SelectedFrame
                                    : EditorStyle.Dopesheet.Frame))
                                {
                                }

                                if (h < hold - 1)
                                    UI.Container(isCurrentSlot
                                        ? EditorStyle.Dopesheet.SelectedHoldSeparator
                                        : EditorStyle.Dopesheet.HoldSeparator);
                            }

                            UI.Container(EditorStyle.Dopesheet.FrameSeparator);
                        }
                    }

                    // Empty cells after the layer's frames
                    for (; slotIndex < maxSlots; slotIndex++)
                    {
                        UI.Container(slotIndex % 4 == 0
                            ? EditorStyle.Dopesheet.FourthEmptyFrame
                            : EditorStyle.Dopesheet.EmptyFrame);
                        UI.Container(EditorStyle.Dopesheet.FrameSeparator);
                    }
                }

                UI.Container(EditorStyle.Dopesheet.LayerSeparator);
            }

            // Footer with add/delete buttons
            using (UI.BeginRow(new ContainerStyle { Height = 28f, Padding = EdgeInsets.LeftRight(8), Spacing = 2f }))
            {
                if (EditorUI.Button(ElementId.AddVectorLayerBtn, EditorAssets.Sprites.IconLayer, toolbar: true))
                {
                    Undo.Record(Document);
                    Document.AddDocumentLayer(DocumentLayerType.Vector);
                    MarkRasterDirty();
                }

                if (EditorUI.Button(ElementId.AddGeneratedLayerBtn, EditorAssets.Sprites.IconPlay, toolbar: true))
                {
                    Undo.Record(Document);
                    Document.AddDocumentLayer(DocumentLayerType.Generated);
                    MarkRasterDirty();
                }

                UI.Flex();

                if (layers.Count > 1)
                {
                    if (EditorUI.Button(ElementId.DeleteLayerBtn, EditorAssets.Sprites.IconDelete, toolbar: true))
                    {
                        Undo.Record(Document);
                        Document.DeleteDocumentLayer(Document.CurrentDocumentLayer);
                        MarkRasterDirty();
                    }
                }
            }
        }
    }

    private const float LayerNameColumnWidth = 140f;

    /// <summary>Check if a global time slot falls within a specific layer frame.</summary>
    private bool IsTimeSlotInRange(int layerIndex, int frameIndex, int timeSlot)
    {
        var layer = Document.DocumentLayers[layerIndex];
        var accumulated = 0;
        for (var f = 0; f < layer.FrameCount; f++)
        {
            var slots = 1 + layer.Frames[f].Hold;
            if (f == frameIndex)
                return timeSlot >= accumulated && timeSlot < accumulated + slots;
            accumulated += slots;
        }
        return false;
    }


    private void SkeletonBindingUI()
    {
        var binding = Document.Binding;

        void SkeletonBindingContent()
        {
            EditorUI.ControlIcon(EditorAssets.Sprites.IconBone);

            if (!binding.IsBound)
            {
                EditorUI.ControlPlaceholderText("Select Skeleton...");
                UI.Spacer(EditorStyle.Control.Spacing);
                return;
            }

            EditorUI.ControlText(binding.SkeletonName.ToString());

            using (UI.BeginContainer(ElementId.BoneUnbindButton, EditorStyle.Button.IconContent with { Padding = EdgeInsets.All(4) }))
            {
                UI.Image(
                    EditorAssets.Sprites.IconDelete,
                    UI.IsHovered()
                        ? EditorStyle.Control.SelectedIcon
                        : EditorStyle.Control.Icon);

                if (UI.WasPressed())
                    ClearSkeletonBinding();
            }

            UI.Spacer(EditorStyle.Control.Spacing);
        }

        if (EditorUI.Control(ElementId.BoneBindButton, SkeletonBindingContent, selected: EditorUI.IsPopupOpen(ElementId.BoneBindButton)))
            EditorUI.TogglePopup(ElementId.BoneBindButton);

        SkeletonBindingPopupUI();

        if (EditorUI.Button(ElementId.PreviewButton, EditorAssets.Sprites.IconPreview, selected: Document.ShowInSkeleton, disabled: !Document.Binding.IsBound, toolbar: true))
        {
            Undo.Record(Document);
            Document.ShowInSkeleton = !Document.ShowInSkeleton;
            Document.Binding.Skeleton?.UpdateSprites();
            Document.MarkMetaModified();
        }

        if (EditorUI.Button(ElementId.SkeletonOverlayButton, EditorAssets.Sprites.IconBone, selected: Document.ShowSkeletonOverlay, disabled: !Document.Binding.IsBound, toolbar: true))
        {
            Document.ShowSkeletonOverlay = !Document.ShowSkeletonOverlay;
            Document.MarkMetaModified();
        }
    }

    private void SkeletonBindingPopupUI()
    {
        void Content()
        {
            foreach (var doc in DocumentManager.Documents)
            {
                if (doc is not SkeletonDocument skeleton || skeleton.BoneCount == 0)
                    continue;

                var isSelected = Document.Binding.Skeleton == skeleton;
                if (EditorUI.PopupItem(skeleton.Name, selected: isSelected))
                {
                    CommitSkeletonBinding(skeleton);
                    EditorUI.ClosePopup();
                }
            }
        }

        EditorUI.Popup(ElementId.BoneBindButton, Content);
    }

    private void UpdateSelection()
    {
        var shape = CurrentShape;

        for (ushort p = (ushort)(shape.PathCount - 1); p < shape.PathCount; p--)
        {
            ref readonly var path = ref shape.GetPath(p);
            if (!path.IsSelected) continue;

            Document.CurrentFillColor = path.FillColor;
            Document.CurrentStrokeColor = path.StrokeColor;
            Document.CurrentDocumentLayer = path.DocLayer;
            Document.CurrentStrokeWidth = (byte)int.Max(1, (int)path.StrokeWidth);
            Document.CurrentOperation = path.Operation;
            return;
        }
    }

    private static bool PathHasSelectedAnchor(Shape shape, Shape.Path path)
    {
        for (ushort a = 0; a < path.AnchorCount; a++)
        {
            var anchor = shape.GetAnchor((ushort)(path.AnchorStart + a));
            if (anchor.IsSelected)
                return true;
        }
        return false;
    }


    private void ConstraintsButtonUI()
    {
        using (UI.BeginContainer(ContainerStyle.Fit))
        {
            var sizes = EditorApplication.Config.SpriteSizes;
            var constraint = Document.ConstrainedSize;

            void ConstraintsButtonContent()
            {
                EditorUI.ControlIcon(EditorAssets.Sprites.IconConstraint);
                if (constraint.HasValue)
                    EditorUI.ControlText($"{constraint.Value.X}x{constraint.Value.Y}");
                else
                    EditorUI.ControlPlaceholderText("None");
                UI.Spacer(EditorStyle.Control.Spacing);
            }

            if (EditorUI.Control(
                ElementId.ConstraintsButton,
                ConstraintsButtonContent,
                selected: EditorUI.IsPopupOpen(ElementId.ConstraintsButton),
                disabled: sizes.Length == 0))
                EditorUI.TogglePopup(ElementId.ConstraintsButton);

            ConstraintsPopupUI();            
        }
    }

    private void ConstraintsPopupUI()
    {
        void Content()
        {
            var sizes = EditorApplication.Config.SpriteSizes;

            for (int i = 0; i < sizes.Length; i++)
            {
                var size = sizes[i];
                var selected = Document.ConstrainedSize.HasValue &&
                                Document.ConstrainedSize.Value == size;
                if (EditorUI.PopupItem($"{size.X}x{size.Y}", selected: selected))
                {
                    Undo.Record(Document);
                    Document.ConstrainedSize = size;
                    Document.UpdateBounds();
                    Document.MarkModified();
                    Document.MarkMetaModified();
                    MarkRasterDirty();
                    EditorUI.ClosePopup();
                }
            }

            if (EditorUI.PopupItem("None", selected: !Document.ConstrainedSize.HasValue))
            {
                Undo.Record(Document);
                Document.ConstrainedSize = null;
                Document.UpdateBounds();
                Document.MarkModified();
                Document.MarkMetaModified();
                MarkRasterDirty();
                EditorUI.ClosePopup();
            }
        }

        EditorUI.Popup(ElementId.ConstraintsButton, Content);
    }

    private void LayerButtonUI()
    {
        using (UI.BeginContainer(ContainerStyle.Fit))
        {
            void ButtonContent()
            {
                EditorUI.ControlIcon(EditorAssets.Sprites.IconLayer);
                var currentLayer = Document.GetCurrentDocumentLayer();
                if (currentLayer != null)
                    EditorUI.ControlText(currentLayer.Name);
                else
                    EditorUI.ControlPlaceholderText("Layer 1");
                UI.Spacer(EditorStyle.Control.Spacing);
            }

            if (EditorUI.Control(
                ElementId.LayerButton,
                ButtonContent,
                selected: EditorUI.IsPopupOpen(ElementId.LayerButton)))
                EditorUI.TogglePopup(ElementId.LayerButton);

            LayerPopupUI();
        }
    }

    private void LayerPopupUI()
    {
        void Content()
        {
            var layers = Document.DocumentLayers;
            for (int i = 0; i < layers.Count; i++)
            {
                var layer = layers[i];
                var selected = Document.CurrentDocumentLayer == i;

                void ItemContent()
                {
                    EditorUI.ControlText(layer.Name);
                    if (layer.SortOrder != 0)
                        EditorUI.ControlPlaceholderText($"({layer.SortOrder})");
                }

                if (EditorUI.PopupItem(ItemContent, selected: selected))
                {
                    Document.CurrentDocumentLayer = i;
                    EditorUI.ClosePopup();
                }
            }
        }

        EditorUI.Popup(ElementId.LayerButton, Content);
    }

    private static readonly ContainerStyle LayerItemSelected = new()
    {
        Color = Color.FromRgba(0x4488FF40),
        BorderRadius = 4f
    };

    private static readonly ContainerStyle LayerItemHover = new()
    {
        Color = Color.FromRgba(0xFFFFFF10),
        BorderRadius = 4f
    };

    private void SortOrderPopupUI(int layerIndex)
    {
        void Content()
        {
            var layer = Document.DocumentLayers[layerIndex];
            var sortOrders = EditorApplication.Config.SortOrders;

            // Default (0) option
            if (EditorUI.PopupItem("Default (0)", selected: layer.SortOrder == 0))
            {
                Undo.Record(Document);
                layer.SortOrder = 0;
                Document.MarkModified();
                MarkRasterDirty();
                EditorUI.ClosePopup();
            }

            foreach (var so in sortOrders)
            {
                if (EditorUI.PopupItem($"{so.Label} ({so.SortOrder})", selected: layer.SortOrder == so.SortOrder))
                {
                    Undo.Record(Document);
                    layer.SortOrder = so.SortOrder;
                    Document.MarkModified();
                    MarkRasterDirty();
                    EditorUI.ClosePopup();
                }
            }
        }

        EditorUI.Popup(ElementId.LayerSortOrder + layerIndex, Content);
    }

    private void BoneBindingUI()
    {
        if (!Document.Binding.IsBound) return;

        using var _ = UI.BeginContainer(ContainerStyle.Fit);

        var currentLayer = Document.GetCurrentDocumentLayer();
        var currentBone = currentLayer?.Bone ?? StringId.None;

        void ButtonContent()
        {
            EditorUI.ControlIcon(EditorAssets.Sprites.IconBone);
            if (!currentBone.IsNone)
                EditorUI.ControlText(currentBone.ToString());
            else
                EditorUI.ControlPlaceholderText("Root");
            UI.Spacer(EditorStyle.Control.Spacing);
        }

        if (EditorUI.Control(
            ElementId.BonePathButton,
            ButtonContent,
            selected: EditorUI.IsPopupOpen(ElementId.BonePathButton)))
            EditorUI.TogglePopup(ElementId.BonePathButton);

        BoneBindingPopupUI();
    }

    private void BoneBindingPopupUI()
    {
        void Content()
        {
            var skeleton = Document.Binding.Skeleton;
            if (skeleton == null)
                return;

            var currentLayer = Document.GetCurrentDocumentLayer();
            var currentBone = currentLayer?.Bone ?? StringId.None;

            // Root option (None = root bone)
            if (EditorUI.PopupItem("Root", selected: currentBone.IsNone))
            {
                SetLayerBone(StringId.None);
                EditorUI.ClosePopup();
            }

            // List all bones from the skeleton
            for (int i = 0; i < skeleton.BoneCount; i++)
            {
                var boneName = skeleton.Bones[i].Name;
                var boneNameValue = StringId.Get(boneName);
                if (EditorUI.PopupItem(boneName, selected: currentBone == boneNameValue))
                {
                    SetLayerBone(boneNameValue);
                    EditorUI.ClosePopup();
                }
            }
        }

        EditorUI.Popup(ElementId.BonePathButton, Content);
    }

    public void SetLayerBone(StringId bone)
    {
        var currentLayer = Document.GetCurrentDocumentLayer();
        if (currentLayer == null) return;

        Undo.Record(Document);
        currentLayer.Bone = bone;
        Document.UpdateBounds();
        Document.MarkModified();
        MarkRasterDirty();
    }

    private void UpdateRaster()
    {
        // Update samples for the current editing layer
        CurrentShape.UpdateSamples();
        CurrentShape.UpdateBounds();

        Document.UpdateBounds();
        var bounds = Document.RasterBounds;
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            _meshSlots.Clear();
            _rasterDirty = false;
            return;
        }

        // Build composite shape for rendering
        var composite = Document.GetCompositeShape(_currentTimeSlot);
        UpdateMeshSDF(composite);
        _rasterDirty = false;
    }

    public void MarkRasterDirty()
    {
        _rasterDirty = true;
    }

    public void SetCurrentTimeSlot(int timeSlot)
    {
        var maxSlots = Document.GlobalTimeSlots;
        var newSlot = Math.Clamp(timeSlot, 0, maxSlots - 1);
        if (newSlot != _currentTimeSlot)
        {
            _currentTimeSlot = newSlot;
            MarkRasterDirty();
        }
    }

    private void TogglePlayback()
    {
        _isPlaying = !_isPlaying;
        _playTimer = 0;
    }

    private void NextFrame()
    {
        var layer = CurrentLayer;
        if (layer.FrameCount <= 1)
            return;

        // Navigate to the next keyframe in the current layer
        var fi = CurrentLayerFrameIndex;
        fi = (fi + 1) % layer.FrameCount;

        // Compute time slot for this frame
        _currentTimeSlot = TimeSlotForLayerFrame(Document.CurrentDocumentLayer, fi);
        MarkRasterDirty();
    }

    private void PreviousFrame()
    {
        var layer = CurrentLayer;
        if (layer.FrameCount <= 1)
            return;

        var fi = CurrentLayerFrameIndex;
        fi = fi == 0 ? layer.FrameCount - 1 : fi - 1;

        _currentTimeSlot = TimeSlotForLayerFrame(Document.CurrentDocumentLayer, fi);
        MarkRasterDirty();
    }

    /// <summary>Compute the first time slot that corresponds to a given layer frame index.</summary>
    private int TimeSlotForLayerFrame(int layerIndex, int frameIndex)
    {
        var layer = Document.DocumentLayers[layerIndex];
        var slot = 0;
        for (var f = 0; f < frameIndex && f < layer.FrameCount; f++)
            slot += 1 + layer.Frames[f].Hold;
        return slot;
    }

    private void InsertFrameBefore()
    {
        Undo.Record(Document);
        var layer = CurrentLayer;
        var fi = CurrentLayerFrameIndex;
        var newFrame = layer.InsertFrame(fi);
        if (newFrame >= 0)
        {
            _currentTimeSlot = TimeSlotForLayerFrame(Document.CurrentDocumentLayer, newFrame);
            MarkRasterDirty();
            Document.MarkModified();
            AtlasManager.UpdateSource(Document);
        }
    }

    private void InsertFrameAfter()
    {
        Undo.Record(Document);
        var layer = CurrentLayer;
        var fi = CurrentLayerFrameIndex;
        var newFrame = layer.InsertFrame(fi + 1);
        if (newFrame >= 0)
        {
            _currentTimeSlot = TimeSlotForLayerFrame(Document.CurrentDocumentLayer, newFrame);
            MarkRasterDirty();
            Document.MarkModified();
            AtlasManager.UpdateSource(Document);
        }
    }

    private void DeleteCurrentFrame()
    {
        var layer = CurrentLayer;
        if (layer.FrameCount <= 1) return;
        Undo.Record(Document);
        var fi = layer.DeleteFrame(CurrentLayerFrameIndex);
        _currentTimeSlot = TimeSlotForLayerFrame(Document.CurrentDocumentLayer, fi);
        MarkRasterDirty();
        Document.MarkModified();
        AtlasManager.UpdateSource(Document);
    }

    private void AddHoldFrame()
    {
        Undo.Record(Document);
        CurrentLayer.Frames[CurrentLayerFrameIndex].Hold++;
        Document.MarkModified();
    }

    private void RemoveHoldFrame()
    {
        var frame = CurrentLayer.Frames[CurrentLayerFrameIndex];
        if (frame.Hold <= 0)
            return;

        Undo.Record(Document);
        frame.Hold = Math.Max(0, frame.Hold - 1);
        Document.MarkModified();
    }

    private void PreviewFillColor(Color32 color)
    {
        var shape = CurrentShape;
        for (ushort p = 0; p < shape.PathCount; p++)
        {
            ref readonly var path = ref shape.GetPath(p);
            if (!path.IsSelected) continue;
            shape.SetPathFillColor(p, color);
        }
        MarkRasterDirty();
    }

    private void PreviewStrokeColor(Color32 color)
    {
        var shape = CurrentShape;
        for (ushort p = 0; p < shape.PathCount; p++)
        {
            ref readonly var path = ref shape.GetPath(p);
            if (!path.IsSelected) continue;
            shape.SetPathStroke(p, color, Document.CurrentStrokeWidth);
        }
        MarkRasterDirty();
    }

    public void SetFill(Color32 color)
    {
        Document.CurrentFillColor = color;

        Undo.Record(Document);

        var shape = CurrentShape;
        for (ushort p = 0; p < shape.PathCount; p++)
        {
            ref readonly var path = ref shape.GetPath(p);
            if (!path.IsSelected) continue;
            shape.SetPathFillColor(p, color);
        }

        Document.MarkModified();
        MarkRasterDirty();
    }

    public void SetStroke(Color32 color)
    {
        Document.CurrentStrokeColor = color;

        Undo.Record(Document);

        var shape = CurrentShape;
        for (ushort p = 0; p < shape.PathCount; p++)
        {
            ref readonly var path = ref shape.GetPath(p);
            if (!path.IsSelected) continue;
            shape.SetPathStroke(p, color, Document.CurrentStrokeWidth);
        }

        Document.MarkModified();
        MarkRasterDirty();
    }

    private void SetStrokeWidth(byte width)
    {
        Document.CurrentStrokeWidth = width;

        Undo.Record(Document);

        var shape = CurrentShape;
        for (ushort p = 0; p < shape.PathCount; p++)
        {
            ref readonly var path = ref shape.GetPath(p);
            if (!path.IsSelected) continue;
            shape.SetPathStroke(p, path.StrokeColor, width);
        }

        Document.MarkModified();
        MarkRasterDirty();
    }

    private void CyclePathOperation()
    {
        // Cycle: Normal → Subtract → Clip → Normal
        Document.CurrentOperation = Document.CurrentOperation switch
        {
            PathOperation.Normal => PathOperation.Subtract,
            PathOperation.Subtract => PathOperation.Clip,
            _ => PathOperation.Normal,
        };

        var shape = CurrentShape;
        var anySelected = false;
        for (ushort p = 0; p < shape.PathCount; p++)
        {
            ref readonly var path = ref shape.GetPath(p);
            if (!path.IsSelected) continue;
            if (!anySelected)
            {
                Undo.Record(Document);
                anySelected = true;
            }
            shape.SetPathOperation(p, Document.CurrentOperation);
        }

        if (anySelected)
        {
            Document.MarkModified();
            MarkRasterDirty();
        }
    }

    private void StrokeWidthButtonUI()
    {
        void ButtonContent()
        {
            EditorUI.ControlText($"{Document.CurrentStrokeWidth}px");
        }

        if (EditorUI.Control(
            ElementId.StrokeWidth,
            ButtonContent,
            selected: EditorUI.IsPopupOpen(ElementId.StrokeWidth)))
            EditorUI.TogglePopup(ElementId.StrokeWidth);

        StrokeWidthPopupUI();
    }

    private void StrokeWidthPopupUI()
    {
        void Content()
        {
            for (var i = 1; i <= 8; i++)
            {
                if (EditorUI.PopupItem($"{i}px", selected: Document.CurrentStrokeWidth == i))
                {
                    SetStrokeWidth((byte)i);
                    EditorUI.ClosePopup();
                }
            }
        }

        EditorUI.Popup(ElementId.StrokeWidth, Content);
    }

    public void SetPathDocLayer(byte docLayer)
    {
        Undo.Record(Document);

        var shape = CurrentShape;
        for (ushort p = 0; p < shape.PathCount; p++)
        {
            ref readonly var path = ref shape.GetPath(p);
            if (!path.IsSelected) continue;
            shape.SetPathDocLayer(p, docLayer);
        }

        Document.UpdateBounds();
        Document.MarkModified();
        MarkRasterDirty();
    }

    public void DeleteSelected()
    {
        Undo.Record(Document);
        var shape = CurrentShape;
        shape.DeleteAnchors();
        Document.MarkModified();
        Document.UpdateBounds();
        MarkRasterDirty();
    }

    private void DuplicateSelected()
    {
        var shape = CurrentShape;
        if (!shape.HasSelection())
            return;

        Undo.Record(Document);

        Span<ushort> pathsToDuplicate = stackalloc ushort[Shape.MaxPaths];
        var pathCount = 0;

        for (ushort p = 0; p < shape.PathCount; p++)
            if (PathHasSelectedAnchor(shape, shape.GetPath(p)))
                pathsToDuplicate[pathCount++] = p;

        if (pathCount == 0)
            return;

        shape.ClearAnchorSelection();

        var firstNewAnchor = shape.AnchorCount;

        for (var i = 0; i < pathCount; i++)
        {
            var srcPath = shape.GetPath(pathsToDuplicate[i]);
            var newPathIndex = shape.AddPath(
                fillColor: srcPath.FillColor,
                strokeColor: srcPath.StrokeColor,
                strokeWidth: srcPath.StrokeWidth,
                docLayer: srcPath.DocLayer,
                operation: srcPath.Operation);
            if (newPathIndex == ushort.MaxValue)
                break;

            for (ushort a = 0; a < srcPath.AnchorCount; a++)
            {
                var srcAnchor = shape.GetAnchor((ushort)(srcPath.AnchorStart + a));
                shape.AddAnchor(newPathIndex, srcAnchor.Position, srcAnchor.Curve);
            }
        }

        for (var i = firstNewAnchor; i < shape.AnchorCount; i++)
            shape.SetAnchorSelected((ushort)i, true);

        shape.UpdateSamples();
        shape.UpdateBounds();
        Document.MarkModified();
        Document.UpdateBounds();
        MarkRasterDirty();

        BeginMoveTool();
    }

    private void CopySelected()
    {
        var shape = CurrentShape;
        if (!shape.HasSelection())
            return;

        var data = new PathClipboardData(shape);
        if (data.Paths.Length == 0)
            return;

        Clipboard.Copy(data);
    }

    private void PasteSelected()
    {
        var clipboardData = Clipboard.Get<PathClipboardData>();
        if (clipboardData == null)
            return;

        Undo.Record(Document);

        var shape = CurrentShape;
        shape.ClearSelection();

        clipboardData.PasteInto(shape);

        Document.MarkModified();
        Document.UpdateBounds();
        MarkRasterDirty();
        UpdateSelection();
    }

    private void CenterShape()
    {
        Undo.Record(Document);

        foreach (var layer in Document.DocumentLayers)
            for (ushort fi = 0; fi < layer.FrameCount; fi++)
                layer.Frames[fi].Shape.CenterOnOrigin();

        Document.UpdateBounds();
        Document.MarkModified();
        MarkRasterDirty();
        Notifications.Add("origin → center");
    }

    private void FlipHorizontal()
    {
        var shape = CurrentShape;
        var pivot = shape.GetSelectedPathsCenter();
        if (!pivot.HasValue)
            return;

        Undo.Record(Document);
        shape.FlipSelectedPathsHorizontal(pivot.Value);
        shape.UpdateSamples();
        shape.UpdateBounds();
        Document.UpdateBounds();
        Document.MarkModified();
        MarkRasterDirty();
    }

    private void FlipVertical()
    {
        var shape = CurrentShape;
        var pivot = shape.GetSelectedPathsCenter();
        if (!pivot.HasValue)
            return;

        Undo.Record(Document);
        shape.FlipSelectedPathsVertical(pivot.Value);
        shape.UpdateSamples();
        shape.UpdateBounds();
        Document.UpdateBounds();
        Document.MarkModified();
        MarkRasterDirty();
    }

    private void MovePathUp()
    {
        var shape = CurrentShape;
        if (!shape.HasSelectedPaths())
            return;

        Undo.Record(Document);
        if (!shape.MoveSelectedPathUp())
            return;

        Document.MarkModified();
        MarkRasterDirty();
    }

    private void MovePathDown()
    {
        var shape = CurrentShape;
        if (!shape.HasSelectedPaths())
            return;

        Undo.Record(Document);
        if (!shape.MoveSelectedPathDown())
            return;

        Document.MarkModified();
        MarkRasterDirty();
    }

    private void SelectAll()
    {
        var shape = CurrentShape;
        for (ushort i = 0; i < shape.AnchorCount; i++)
            shape.SetAnchorSelected(i, true);
        UpdateSelection();
    }

    private void UpdateAnimation()
    {
        if (!_isPlaying || Document.GlobalTimeSlots <= 1)
            return;

        _playTimer += Time.DeltaTime;
        var slotDuration = 1f / 12f;

        if (_playTimer >= slotDuration)
        {
            _playTimer = 0;
            _currentTimeSlot = (_currentTimeSlot + 1) % Document.GlobalTimeSlots;
            MarkRasterDirty();
        }
    }

    private void UpdateInput()
    {
        if (Input.WasButtonPressed(InputCode.KeyDelete))
            DeleteSelected();
    }

    private void HandleLeftClick()
    {
        Matrix3x2.Invert(Document.Transform, out var invTransform);
        var localMousePos = Vector2.Transform(Workspace.MouseWorldPosition, invTransform);
        var shape = CurrentShape;
        var shift = Input.IsShiftDown(InputScope.All);

        Span<Shape.HitResult> hits = stackalloc Shape.HitResult[Shape.MaxAnchors];
        var hitCount = shape.HitTestAll(localMousePos, hits);

        // Separate hits by type and sort by path index descending (back to front, higher = on top)
        Span<ushort> anchorHits = stackalloc ushort[hitCount];
        Span<ushort> segmentHits = stackalloc ushort[hitCount];
        Span<ushort> pathHits = stackalloc ushort[Shape.MaxPaths];
        var anchorCount = 0;
        var segmentCount = 0;
        var pathCount = shape.GetPathsContainingPoint(localMousePos, pathHits);

        for (var i = 0; i < hitCount; i++)
        {
            // Skip hits on hidden or locked layers
            var hitPathIndex = hits[i].PathIndex;
            if (hitPathIndex != ushort.MaxValue)
            {
                ref readonly var hitPath = ref shape.GetPath(hitPathIndex);
                if (!IsPathOnVisibleLayer(hitPath) || IsPathOnLockedLayer(hitPath))
                    continue;
            }

            if (hits[i].AnchorIndex != ushort.MaxValue)
                anchorHits[anchorCount++] = hits[i].AnchorIndex;
            else if (hits[i].SegmentIndex != ushort.MaxValue)
                segmentHits[segmentCount++] = hits[i].SegmentIndex;
        }

        // Filter path hits for hidden/locked layers
        var filteredPathCount = 0;
        for (var i = 0; i < pathCount; i++)
        {
            ref readonly var hitPath = ref shape.GetPath(pathHits[i]);
            if (IsPathOnVisibleLayer(hitPath) && !IsPathOnLockedLayer(hitPath))
                pathHits[filteredPathCount++] = pathHits[i];
        }
        pathCount = filteredPathCount;

        // Sort by path index descending (higher path index = drawn on top)
        SortByPathIndexDescending(shape, anchorHits[..anchorCount]);
        SortByPathIndexDescending(shape, segmentHits[..segmentCount]);
        pathHits[..pathCount].Reverse();

        // Priority: anchors > segments > paths
        if (anchorCount > 0)
        {
            if (shift)
                ShiftSelectNext(anchorHits[..anchorCount], shape.IsAnchorSelected, shape.SetAnchorSelected);
            else
            {
                var nextIdx = FindNextInCycle(anchorHits[..anchorCount], shape.IsAnchorSelected);
                SelectAnchor(anchorHits[nextIdx], toggle: false);
            }
        }
        else if (segmentCount > 0)
        {
            if (shift)
                ShiftSelectNextSegment(shape, segmentHits[..segmentCount]);
            else
            {
                var nextIdx = FindNextInCycle(segmentHits[..segmentCount], shape.IsSegmentSelected);
                SelectSegment(segmentHits[nextIdx], toggle: false);
            }
        }
        else if (pathCount > 0)
        {
            if (shift)
                ShiftSelectNext(pathHits[..pathCount], shape.IsPathSelected, i => shape.SetPathSelected(i, true), i => shape.SetPathSelected(i, false));
            else
            {
                var nextIdx = FindNextInCycle(pathHits[..pathCount], shape.IsPathSelected);
                SelectPath(pathHits[nextIdx], toggle: false);
            }
        }
        else
        {
            if (!shift)
                shape.ClearAnchorSelection();
            return;
        }

        UpdateSelection();
    }

    private static int FindNextInCycle(Span<ushort> items, Func<ushort, bool> isSelected)
    {
        for (var i = 0; i < items.Length; i++)
        {
            if (isSelected(items[i]))
                return (i + 1) % items.Length;
        }
        return 0;
    }

    private static void ShiftSelectNext(Span<ushort> items, Func<ushort, bool> isSelected, Action<ushort, bool> setSelected)
    {
        // Find first unselected and select it, or deselect all if all selected
        for (var i = 0; i < items.Length; i++)
        {
            if (!isSelected(items[i]))
            {
                setSelected(items[i], true);
                return;
            }
        }
        for (var i = 0; i < items.Length; i++)
            setSelected(items[i], false);
    }

    private static void ShiftSelectNext(Span<ushort> items, Func<ushort, bool> isSelected, Action<ushort> select, Action<ushort> deselect)
    {
        for (var i = 0; i < items.Length; i++)
        {
            if (!isSelected(items[i]))
            {
                select(items[i]);
                return;
            }
        }
        for (var i = 0; i < items.Length; i++)
            deselect(items[i]);
    }

    private static void ShiftSelectNextSegment(Shape shape, Span<ushort> items)
    {
        for (var i = 0; i < items.Length; i++)
        {
            if (!shape.IsSegmentSelected(items[i]))
            {
                shape.SetAnchorSelected(items[i], true);
                shape.SetAnchorSelected(shape.GetNextAnchorIndex(items[i]), true);
                return;
            }
        }
        for (var i = 0; i < items.Length; i++)
        {
            shape.SetAnchorSelected(items[i], false);
            shape.SetAnchorSelected(shape.GetNextAnchorIndex(items[i]), false);
        }
    }

    private static void SortByPathIndexDescending(Shape shape, Span<ushort> indices)
    {
        indices.Sort((a, b) => shape.GetAnchor(b).Path.CompareTo(shape.GetAnchor(a).Path));
    }

    private void BeginMoveTool()
    {
        var shape = CurrentShape;
        if (!shape.HasSelection())
            return;

        for (ushort i = 0; i < shape.AnchorCount; i++)
            _savedPositions[i] = shape.GetAnchor(i).Position;

        Undo.Record(Document);

        Workspace.BeginTool(new MoveTool(
            update: delta =>
            {
                shape.TranslateAnchors(delta, _savedPositions, Input.IsCtrlDown(InputScope.All));
                shape.UpdateSamples();
                shape.UpdateBounds();
                MarkRasterDirty();
            },
            commit: _ =>
            {
                Document.MarkModified();
                Document.UpdateBounds();
                MarkRasterDirty();
            },
            cancel: () =>
            {
                shape.RestoreAnchorPositions(_savedPositions);
                shape.UpdateSamples();
                shape.UpdateBounds();
                Undo.Cancel();
            }
        ));
    }

    private void BeginRotateTool()
    {
        var shape = CurrentShape;
        var localPivot = GetLocalTransformPivot(shape);
        if (!localPivot.HasValue)
            return;

        var worldPivot = Vector2.Transform(localPivot.Value, Document.Transform);
        var worldOrigin = Vector2.Transform(Vector2.Zero, Document.Transform);
        Matrix3x2.Invert(Document.Transform, out var invTransform);

        Undo.Record(Document);

        for (ushort i = 0; i < shape.AnchorCount; i++)
            _savedPositions[i] = shape.GetAnchor(i).Position;

        Workspace.BeginTool(new RotateTool(
            worldPivot,
            localPivot.Value,
            worldOrigin,
            Vector2.Zero,
            invTransform,
            update: angle =>
            {
                var pivot = Input.IsShiftDown() ? Vector2.Zero : localPivot.Value;
                shape.RotateAnchors(pivot, angle, _savedPositions);
                if (Input.IsCtrlDown(InputScope.All))
                    shape.SnapSelectedAnchorsToPixelGrid();
                shape.UpdateSamples();
                shape.UpdateBounds();
                MarkRasterDirty();
            },
            commit: _ =>
            {
                if (Input.IsCtrlDown(InputScope.All))
                    shape.SnapSelectedAnchorsToPixelGrid();
                shape.UpdateSamples();
                shape.UpdateBounds();
                Document.MarkModified();
                Document.UpdateBounds();
                MarkRasterDirty();
            },
            cancel: () =>
            {
                shape.RestoreAnchorPositions(_savedPositions);
                shape.UpdateSamples();
                shape.UpdateBounds();
                Undo.Cancel();
            }
        ));
    }

    private void OnScale()
    {
        var shape = CurrentShape;
        var localPivot = GetLocalTransformPivot(shape);
        if (!localPivot.HasValue)
            return;

        var worldPivot = Vector2.Transform(localPivot.Value, Document.Transform);
        var worldOrigin = Vector2.Transform(Vector2.Zero, Document.Transform);

        Undo.Record(Document);

        for (ushort i = 0; i < shape.AnchorCount; i++)
        {
            _savedPositions[i] = shape.GetAnchor(i).Position;
            _savedCurves[i] = shape.GetAnchor(i).Curve;
        }

        Workspace.BeginTool(new ScaleTool(
            worldPivot,
            worldOrigin,
            update: scale =>
            {
                var pivot = Input.IsShiftDown(InputScope.All) ? Vector2.Zero : localPivot.Value;
                shape.ScaleAnchors(pivot, scale, _savedPositions, _savedCurves);
                if (Input.IsCtrlDown(InputScope.All))
                    shape.SnapSelectedAnchorsToPixelGrid();
                shape.UpdateSamples();
                shape.UpdateBounds();
                MarkRasterDirty();
            },
            commit: _ =>
            {
                if (Input.IsCtrlDown(InputScope.All))
                    shape.SnapSelectedAnchorsToPixelGrid();
                shape.UpdateSamples();
                shape.UpdateBounds();
                Document.MarkModified();
                Document.UpdateBounds();
                MarkRasterDirty();
            },
            cancel: () =>
            {
                shape.RestoreAnchorPositions(_savedPositions);
                shape.RestoreAnchorCurves(_savedCurves);
                shape.UpdateSamples();
                shape.UpdateBounds();
                Undo.Cancel();
            }
        ));
    }

    private Vector2? GetLocalTransformPivot(Shape shape)
    {
        return shape.GetSelectedAnchorsCentroid();
    }

    private void HandleDragStart()
    {
        Workspace.BeginTool(new BoxSelectTool(CommitBoxSelectAnchors));
    }

    private void BeginCurveTool()
    {
        var shape = CurrentShape;

        var hasSelectedSegment = false;
        for (ushort i = 0; i < shape.AnchorCount; i++)
        {
            if (shape.IsSegmentSelected(i))
            {
                hasSelectedSegment = true;
                break;
            }
        }

        if (!hasSelectedSegment)
            return;

        for (ushort i = 0; i < shape.AnchorCount; i++)
            _savedCurves[i] = shape.GetAnchor(i).Curve;

        Undo.Record(Document);

        Workspace.BeginTool(new CurveTool(
            shape,
            Document.Transform,
            _savedCurves,
            update: () => MarkRasterDirty(),
            commit: () =>
            {
                Document.MarkModified();
                Document.UpdateBounds();
                MarkRasterDirty();
            },
            cancel: () =>
            {
                Undo.Cancel();
                MarkRasterDirty();
            }
        ));
    }

    private void CommitBoxSelectAnchors(Rect bounds)
    {
        var shape = CurrentShape;
        var shift = Input.IsShiftDown(InputScope.All);

        if (!shift)
            shape.ClearAnchorSelection();

        Matrix3x2.Invert(Document.Transform, out var invTransform);
        var minLocal = Vector2.Transform(bounds.Min, invTransform);
        var maxLocal = Vector2.Transform(bounds.Max, invTransform);
        var localRect = Rect.FromMinMax(minLocal, maxLocal);
        shape.SelectAnchors(localRect);

        // Deselect anchors on hidden or locked layers
        for (ushort i = 0; i < shape.AnchorCount; i++)
        {
            if (!shape.IsAnchorSelected(i)) continue;
            var pathIndex = FindPathForAnchor(shape, i);
            if (pathIndex == ushort.MaxValue) continue;
            ref readonly var path = ref shape.GetPath(pathIndex);
            if (!IsPathOnVisibleLayer(path) || IsPathOnLockedLayer(path))
                shape.SetAnchorSelected(i, false);
        }

        UpdateSelection();
    }

    private void SelectAnchor(ushort anchorIndex, bool toggle)
    {
        var shape = CurrentShape;

        if (toggle)
        {
            var anchor = shape.GetAnchor(anchorIndex);
            var isSelected = (anchor.Flags & Shape.AnchorFlags.Selected) != 0;
            shape.SetAnchorSelected(anchorIndex, !isSelected);
        }
        else
        {
            shape.ClearSelection();
            shape.SetAnchorSelected(anchorIndex, true);
        }

        UpdateSelection();
    }

    private void SelectSegment(ushort anchorIndex, bool toggle)
    {
        var shape = CurrentShape;
        var pathIdx = FindPathForAnchor(shape, anchorIndex);
        if (pathIdx == ushort.MaxValue)
            return;

        var path = shape.GetPath(pathIdx);
        var nextAnchor = (ushort)(path.AnchorStart + ((anchorIndex - path.AnchorStart + 1) % path.AnchorCount));

        if (toggle)
        {
            var anchor1 = shape.GetAnchor(anchorIndex);
            var anchor2 = shape.GetAnchor(nextAnchor);
            var bothSelected = (anchor1.Flags & Shape.AnchorFlags.Selected) != 0 &&
                               (anchor2.Flags & Shape.AnchorFlags.Selected) != 0;
            shape.SetAnchorSelected(anchorIndex, !bothSelected);
            shape.SetAnchorSelected(nextAnchor, !bothSelected);
        }
        else
        {
            shape.ClearSelection();
            shape.SetAnchorSelected(anchorIndex, true);
            shape.SetAnchorSelected(nextAnchor, true);
        }

        UpdateSelection();
    }

    private void SelectPath(ushort pathIndex, bool toggle)
    {
        var shape = CurrentShape;
        var path = shape.GetPath(pathIndex);

        if (toggle)
        {
            var allSelected = true;
            for (ushort a = 0; a < path.AnchorCount; a++)
            {
                var anchor = shape.GetAnchor((ushort)(path.AnchorStart + a));
                if ((anchor.Flags & Shape.AnchorFlags.Selected) == 0)
                {
                    allSelected = false;
                    break;
                }
            }
            for (ushort a = 0; a < path.AnchorCount; a++)
                shape.SetAnchorSelected((ushort)(path.AnchorStart + a), !allSelected);
        }
        else
        {
            shape.ClearSelection();
            for (ushort a = 0; a < path.AnchorCount; a++)
                shape.SetAnchorSelected((ushort)(path.AnchorStart + a), true);
        }

        UpdateSelection();
    }

    private void SplitSegment(ushort anchorIndex)
    {
        var shape = CurrentShape;
        shape.ClearSelection();
        shape.SplitSegment(anchorIndex);

        var newAnchorIdx = (ushort)(anchorIndex + 1);
        if (newAnchorIdx < shape.AnchorCount)
            shape.SetAnchorSelected(newAnchorIdx, true);

        Document.MarkModified();
        Document.UpdateBounds();
        MarkRasterDirty();
    }

    private void BeginPenTool()
    {
        var shape = CurrentShape;
        Workspace.BeginTool(new PenTool(this, shape, Document.CurrentFillColor, Document.CurrentOperation));
    }

    private void BeginKnifeTool()
    {
        var shape = CurrentShape;
        Workspace.BeginTool(new KnifeTool(this, shape, commit: () =>
        {
            shape.UpdateSamples();
            shape.UpdateBounds();
            MarkRasterDirty();
        }));
    }

    private void BeginRectangleTool()
    {
        var shape = CurrentShape;
        Workspace.BeginTool(new ShapeTool(this, shape, Document.CurrentFillColor, ShapeType.Rectangle, Document.CurrentOperation));
    }

    private void BeginCircleTool()
    {
        var shape = CurrentShape;
        Workspace.BeginTool(new ShapeTool(this, shape, Document.CurrentFillColor, ShapeType.Circle, Document.CurrentOperation));
    }

    private void InsertAnchorAtHover()
    {
        Matrix3x2.Invert(Document.Transform, out var invTransform);
        var hit = CurrentShape.HitTest(
            Vector2.Transform(Workspace.MouseWorldPosition, invTransform));

        if (hit.SegmentIndex == ushort.MaxValue)
            return;

        Undo.Record(Document);

        var shape = CurrentShape;
        shape.ClearSelection();
        shape.SplitSegmentAtPoint(hit.SegmentIndex, hit.SegmentPosition);

        var newAnchorIdx = (ushort)(hit.SegmentIndex + 1);
        if (newAnchorIdx < shape.AnchorCount)
            shape.SetAnchorSelected(newAnchorIdx, true);

        Document.UpdateBounds();
        MarkRasterDirty();

        for (ushort i = 0; i < shape.AnchorCount; i++)
            _savedPositions[i] = shape.GetAnchor(i).Position;

        BeginMoveTool();
    }

    private static ushort FindPathForAnchor(Shape shape, ushort anchorIndex)
    {
        for (ushort p = 0; p < shape.PathCount; p++)
        {
            var path = shape.GetPath(p);
            if (anchorIndex >= path.AnchorStart && anchorIndex < path.AnchorStart + path.AnchorCount)
                return p;
        }
        return ushort.MaxValue;
    }

    private void DrawRaster()
    {
        var rb = Document.RasterBounds;
        if (rb.Width <= 0 || rb.Height <= 0)
            return;

        // Show generated layer images behind the vector paths when editing
        foreach (var layer in Document.DocumentLayers)
        {
            if (layer.Type != DocumentLayerType.Generated) continue;
            if (!layer.Visible || layer.GeneratedTexture == null) continue;

            using (Graphics.PushState())
            {
                Graphics.SetSortGroup(2);
                Graphics.SetLayer(EditorLayer.DocumentEditor);
                Graphics.SetTransform(Document.Transform);
                Graphics.SetShader(EditorAssets.Shaders.Texture);
                Graphics.SetTexture(layer.GeneratedTexture);
                Graphics.SetColor(new Color(1, 1, 1, layer.Opacity * Workspace.XrayAlpha));
                Graphics.Draw(Document.Bounds, new Rect(0, 0, 1, 1));
            }
        }

        DrawMeshSDF();
    }

    private static void DrawSegment(Shape shape, ushort pathIndex, ushort segmentIndex, float width, ushort order = 0)
    {
        var samples = shape.GetSegmentSamples(segmentIndex);
        ref readonly var anchor = ref shape.GetAnchor(segmentIndex);

        var prev = anchor.Position;
        foreach (var sample in samples)
        {
            Gizmos.DrawLine(prev, sample, width, order: order);
            prev = sample;
        }

        ref readonly var nextAnchor = ref shape.GetNextAnchor(segmentIndex);
        Gizmos.DrawLine(prev, nextAnchor.Position, width, order: order);
    }

    private void DrawSegments(Shape shape)
    {
        using (Gizmos.PushState(EditorLayer.DocumentEditor))
        {
            Gizmos.SetColor(EditorStyle.Workspace.LineColor);
            for (ushort anchorIndex = 0; anchorIndex < shape.AnchorCount; anchorIndex++)
            {
                if (!shape.IsSegmentSelected(anchorIndex))
                {
                    var pathIndex = FindPathForAnchor(shape, anchorIndex);
                    if (pathIndex != ushort.MaxValue && IsPathOnVisibleLayer(shape.GetPath(pathIndex)))
                        DrawSegment(shape, pathIndex, anchorIndex, EditorStyle.Shape.SegmentLineWidth, 1);
                }
            }

            Gizmos.SetColor(EditorStyle.Workspace.SelectionColor);
            for (ushort anchorIndex = 0; anchorIndex < shape.AnchorCount; anchorIndex++)
            {
                if (shape.IsSegmentSelected(anchorIndex))
                {
                    var pathIndex = FindPathForAnchor(shape, anchorIndex);
                    if (pathIndex != ushort.MaxValue && IsPathOnVisibleLayer(shape.GetPath(pathIndex)))
                        DrawSegment(shape, pathIndex, anchorIndex, EditorStyle.Shape.SegmentLineWidth, 2);
                }
            }
        }
    }

    private static void DrawAnchor(Vector2 worldPosition)
    {
        Gizmos.SetColor(EditorStyle.Workspace.LineColor);
        Gizmos.DrawRect(worldPosition, EditorStyle.Shape.AnchorSize, order: 4);
    }

    private static void DrawSelectedAnchor(Vector2 worldPosition)
    {
        Gizmos.SetColor(EditorStyle.Workspace.SelectionColor);
        Gizmos.DrawRect(worldPosition, EditorStyle.Shape.AnchorSize, order: 5);
    }

    private void DrawAnchors(Shape shape)
    {
        using var _ = Gizmos.PushState(EditorLayer.DocumentEditor);
        for (ushort i = 0; i < shape.AnchorCount; i++)
        {
            ref readonly var anchor = ref shape.GetAnchor(i);
            if (anchor.IsSelected) continue;
            var pathIndex = FindPathForAnchor(shape, i);
            if (pathIndex != ushort.MaxValue && !IsPathOnVisibleLayer(shape.GetPath(pathIndex)))
                continue;
            DrawAnchor(anchor.Position);
        }

        for (ushort i = 0; i < shape.AnchorCount; i++)
        {
            ref readonly var anchor = ref shape.GetAnchor(i);
            if (!anchor.IsSelected) continue;
            var pathIndex = FindPathForAnchor(shape, i);
            if (pathIndex != ushort.MaxValue && !IsPathOnVisibleLayer(shape.GetPath(pathIndex)))
                continue;
            DrawSelectedAnchor(anchor.Position);
        }
    }

    private void DrawEdges()
    {
        var bounds = Document.Bounds;
        var edges = Document.Edges;
        var ppu = 1.0f / EditorApplication.Config.PixelsPerUnit;

        var edgeL = edges.L * ppu;
        var edgeR = edges.R * ppu;
        var edgeT = edges.T * ppu;
        var edgeB = edges.B * ppu;

        using (Gizmos.PushState(EditorLayer.DocumentEditor))
        {
            Graphics.SetTransform(Document.Transform);
            Graphics.SetSortGroup(6);
            Gizmos.SetColor(new Color(0.2f, 0.9f, 0.2f, 0.8f));

            var lineWidth = EditorStyle.Workspace.DocumentBoundsLineWidth;

            // Top edge
            if (edgeT > 0)
            {
                var y = bounds.Top + edgeT;
                Gizmos.DrawLine(new Vector2(bounds.Left, y), new Vector2(bounds.Right, y), lineWidth);
            }

            // Bottom edge
            if (edgeB > 0)
            {
                var y = bounds.Bottom - edgeB;
                Gizmos.DrawLine(new Vector2(bounds.Left, y), new Vector2(bounds.Right, y), lineWidth);
            }

            // Left edge
            if (edgeL > 0)
            {
                var x = bounds.Left + edgeL;
                Gizmos.DrawLine(new Vector2(x, bounds.Top), new Vector2(x, bounds.Bottom), lineWidth);
            }

            // Right edge
            if (edgeR > 0)
            {
                var x = bounds.Right - edgeR;
                Gizmos.DrawLine(new Vector2(x, bounds.Top), new Vector2(x, bounds.Bottom), lineWidth);
            }
        }
    }

    private void DrawSkeletonOverlay()
    {
        var skeleton = Document.Binding.Skeleton;
        if (skeleton == null)
            return;

        // Skinned sprites work in skeleton space - draw other sprites at their positions
        using (Graphics.PushState())
        {
            Graphics.SetSortGroup(0);
            Graphics.SetLayer(EditorLayer.DocumentEditor);
            foreach (var sprite in skeleton.Sprites)
            {
                if (sprite == Document) continue;
                Graphics.SetBlendMode(BlendMode.Alpha);
                Graphics.SetTransform(Document.Transform);
                sprite.DrawSprite(alpha: 0.3f);
            }
        }

        using (Gizmos.PushState(EditorLayer.DocumentEditor))
        {
            Graphics.SetSortGroup(6);
            Graphics.SetTransform(Document.Transform);

            for (var boneIndex = 0; boneIndex < skeleton.BoneCount; boneIndex++)
            {
                var boneName = StringId.Get(skeleton.Bones[boneIndex].Name);
                var currentBone = Document.GetCurrentDocumentLayer()?.Bone ?? StringId.None;
                Gizmos.DrawBoneAndJoints(skeleton, boneIndex, selected: boneName == currentBone);
            }
        }
    }

    #region Skeleton Binding

    private void CommitSkeletonBinding(SkeletonDocument skeleton)
    {
        Undo.Record(Document);
        Document.SetSkeletonBinding(skeleton);
        skeleton.UpdateSprites();
        Notifications.Add($"bound to skeleton '{skeleton.Name}'");
    }

    private void ClearSkeletonBinding()
    {
        if (!Document.Binding.IsBound)
        {
            Notifications.Add("sprite has no skeleton binding");
            return;
        }

        Undo.Record(Document);
        Document.ClearSkeletonBinding();
        Notifications.Add("skeleton binding cleared");
    }

    private void ClearSelection()
    {
        foreach (var layer in Document.DocumentLayers)
            for (ushort fi = 0; fi < layer.FrameCount; fi++)
                layer.Frames[fi].Shape.ClearSelection();
    }

    #endregion
}
