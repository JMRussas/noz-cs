//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ.Editor;

public class SpriteEditor : DocumentEditor
{
    private const int Padding = 8;

    private const byte RootId = 1;
    private const byte PaletteButtonId = 2;
    private const byte TileButtonId = 3;
    private const byte BoneBindButtonId = 4;
    private const byte BoneUnbindButtonId = 5;
    private const byte OpacityButtonId = 6;
    private const byte OpacityPopupId = 7;
    private const byte SubtractButtonId = 8;
    private const byte AntiAliasButtonId = 9;
    private const byte FirstOpacityId = 10;
    private const byte PreviewButtonId = 11;
    private const byte SkeletonOverlayButtonId = 13;
    private const byte DopeSheetId = 14;
    private const byte ConstraintsButtonId = 24;
    private const byte FillColorButtonId = 27;
    private const byte StrokeColorButtonId = 28;
    private const byte LayerButtonId = 29;
   
    public new SpriteDocument Document => (SpriteDocument)base.Document;

    private ushort _currentFrame;
    private bool _isPlaying;
    private float _playTimer;
    private readonly PixelData<Color32> _image = new(
        EditorApplication.Config!.AtlasSize,
        EditorApplication.Config!.AtlasSize);
    private readonly Texture _rasterTexture;
    private bool _rasterDirty = true;

    public SpriteEditor(SpriteDocument doc) : base(doc)
    {
        _rasterTexture = Texture.Create(
            _image.Width,
            _image.Height,
            _image.AsByteSpan(),
            TextureFormat.RGBA8,
            TextureFilter.Point,
            "SpriteEditor");

        Workspace.XrayModeChanged += OnXrayModeChanged;

        var deleteCommand = new Command { Name = "Delete", Handler = DeleteSelected, Key = InputCode.KeyX, Icon = EditorAssets.Sprites.IconDelete };
        var exitEditCommand = new Command { Name = "Exit Edit Mode", Handler = Workspace.EndEdit, Key = InputCode.KeyTab };
        var moveCommand = new Command { Name = "Move", Handler = BeginMoveTool, Key = InputCode.KeyG, Icon = EditorAssets.Sprites.IconMove };
        var moveBoneOriginCommand = new Command { Name = "Move Bone Origin", Handler = BeginMoveBoneOrigin, Key = InputCode.KeyG, Shift = true };
        var boneOriginToOriginCommand = new Command { Name = "Bone Origin to Origin", Handler = BoneOriginToOrigin, Key = InputCode.KeyB, Alt = true };
        var boneOriginToBoneCommand = new Command { Name = "Bone Origin to Bone", Handler = BoneOriginToBone, Key = InputCode.KeyB, Alt = true, Shift = true };
        var originToBoneOriginCommand = new Command { Name = "Origin to Bone Origin", Handler = OriginToBoneOrigin };
        var originToCenterCommand = new Command { Name = "Origin to Center", Handler = CenterShape, Key = InputCode.KeyC, Shift = true };
        var rotateCommand = new Command { Name = "Rotate", Handler = BeginRotateTool, Key = InputCode.KeyR };
        var scaleCommand = new Command { Name = "Scale", Handler = BeginScaleTool, Key = InputCode.KeyS };
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
            moveBoneOriginCommand,
            boneOriginToOriginCommand,
            boneOriginToBoneCommand,
            originToBoneOriginCommand,
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
        ];

        bool HasSelection() => Document.GetFrame(_currentFrame).Shape.HasSelection();
        bool HasSelectedPaths() => Document.GetFrame(_currentFrame).Shape.HasSelectedPaths();

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
                PopupMenuItem.FromCommand(originToBoneOriginCommand, enabled: () => Document.Binding.IsBound, level: 1),
                PopupMenuItem.Submenu("Set Bone Origin"),
                PopupMenuItem.FromCommand(moveBoneOriginCommand, enabled: () => Document.Binding.IsBound, level: 1),
                PopupMenuItem.FromCommand(boneOriginToOriginCommand, enabled: () => Document.Binding.IsBound, level: 1),
                PopupMenuItem.FromCommand(boneOriginToBoneCommand, enabled: () => Document.Binding.IsBound, level: 1),
                PopupMenuItem.Separator(),
                PopupMenuItem.FromCommand(deleteCommand, enabled: HasSelection),
                PopupMenuItem.Separator(),
                PopupMenuItem.FromCommand(exitEditCommand),
            ]
        };  
    }

    // Selection
    private byte _currentFillColor = 0;
    private byte _currentStrokeColor = 0;
    private float _currentFillOpacity = 1.0f;
    private float _currentStrokeOpacity = 0.0f;
    private byte _selectionLayer = 0;

    // Tool state
    private readonly Vector2[] _savedPositions = new Vector2[Shape.MaxAnchors];
    private readonly float[] _savedCurves = new float[Shape.MaxAnchors];


    public ushort CurrentFrame => _currentFrame;
    public bool IsPlaying => _isPlaying;

    public override void Dispose()
    {
        ClearSelection();

        Workspace.XrayModeChanged -= OnXrayModeChanged;

        if (Document.IsModified)
            AtlasManager.UpdateSprite(Document);

        _rasterTexture.Dispose();
        _image.Dispose();
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

        var shape = Document.GetFrame(_currentFrame).Shape;

        DrawRaster();

        using (Gizmos.PushState(EditorLayer.DocumentEditor))
        {
            Graphics.SetTransform(Document.Transform);
            Graphics.SetSortGroup(5);
            Document.DrawOrigin();
            DrawBoneOrigin();
            Graphics.SetSortGroup(4);
            DrawSegments(shape);
            DrawAnchors(shape);
        }

        if (Document.ShowSkeletonOverlay)
            DrawSkeletonOverlay();
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

        var color = (int)_currentFillColor;
        var opacity = _currentFillOpacity;

        if (EditorUI.ColorButton(FillColorButtonId, Document.Palette, ref color, ref opacity, EditorAssets.Sprites.IconFill))
            SetFillColor((byte)color, opacity);

        var strokeColor = (int)_currentStrokeColor;
        var strokeOpacity = _currentStrokeOpacity;
        if (EditorUI.ColorButton(StrokeColorButtonId, Document.Palette, ref strokeColor, ref strokeOpacity, EditorAssets.Sprites.IconStroke))
            SetStrokeColor((byte)strokeColor, strokeOpacity);

        // Palette 
        PaletteButtonUI();

        EditorUI.ToolbarSpacer();

        LayerButtonUI();

        BoneBindingButtonUI();

        UI.Flex();

        if (EditorUI.Button(
            AntiAliasButtonId,
            Document.IsAntiAliased
                ? EditorAssets.Sprites.IconAntialiasOn
                : EditorAssets.Sprites.IconAntialiasOff,
            Document.IsAntiAliased,
            toolbar: true))
        {
            Undo.Record(Document);
            MarkRasterDirty();
            Document.MarkModified();
            Document.IsAntiAliased = !Document.IsAntiAliased;
        }

        if (EditorUI.Button(TileButtonId, EditorAssets.Sprites.IconTiling, Document.ShowTiling, toolbar: true))
        {
            Document.ShowTiling = !Document.ShowTiling;
            Document.MarkMetaModified();
        }

        ConstraintsButtonUI();
    }

    public override void UpdateUI()
    {
        using (UI.BeginCanvas(id: EditorStyle.CanvasId.DocumentEditor))
        using (UI.BeginColumn(RootId, EditorStyle.DocumentEditor.Root))
        {
            ToolbarUI();

            using (UI.BeginContainer(new ContainerStyle { Padding = EdgeInsets.LeftRight(2) }))
            {
                Span<EditorUI.DopeSheetFrame> frames = stackalloc EditorUI.DopeSheetFrame[Document.FrameCount];
                for (ushort i = 0; i < Document.FrameCount; i++)
                    frames[i] = new EditorUI.DopeSheetFrame { Hold = Document.Frames[i].Hold, };
                var currentFrame = 0;
                EditorUI.DopeSheet(DopeSheetId, frames, ref currentFrame, Sprite.MaxFrames, false);
            }

            UI.Spacer(EditorStyle.Control.Spacing);
        }
    }

    private void PaletteButtonUI()
    {
        if (PaletteManager.Palettes.Count < 2) return;

        void ButtonContent()
        {
            EditorUI.ControlIcon(EditorAssets.Sprites.IconPalette);
            EditorUI.ControlText(PaletteManager.GetPalette(Document.Palette).Label);
            UI.Spacer(EditorStyle.Control.Spacing);
        }

        if (EditorUI.Control(
            PaletteButtonId,
            ButtonContent,
            selected: EditorUI.IsPopupOpen(PaletteButtonId)))
            EditorUI.TogglePopup(PaletteButtonId);

        PalettePopupUI();
    }

    private void BoneBindingButtonUI()
    {
        var binding = Document.Binding;
        var selected = Workspace.ActiveTool is BoneSelectTool;

        void BoneBindingContent()
        {
            EditorUI.ControlIcon(EditorAssets.Sprites.IconBone);

            if (!binding.IsBound)
            {
                EditorUI.ControlPlaceholderText("Select Bone...");
                UI.Spacer(EditorStyle.Control.Spacing);
                return;
            }

            using (UI.BeginRow())
            {
                EditorUI.ControlText(binding.SkeletonName);
                EditorUI.ControlText(".");
                EditorUI.ControlText(binding.BoneName);
            }

            using (UI.BeginContainer(BoneUnbindButtonId, EditorStyle.Button.IconContent with { Padding = EdgeInsets.All(4) }))
            {
                UI.Image(
                    EditorAssets.Sprites.IconDelete,
                    UI.IsHovered()
                        ? EditorStyle.Control.SelectedIcon
                        : EditorStyle.Control.Icon);

                if (UI.WasPressed())
                    ClearBoneBinding();
            }

            UI.Spacer(EditorStyle.Control.Spacing);
        }

        if (EditorUI.Control(BoneBindButtonId, BoneBindingContent, selected: selected))
            OpenBonePopupMenu();

        if (EditorUI.Button(PreviewButtonId, EditorAssets.Sprites.IconPreview, selected: Document.ShowInSkeleton, disabled: !Document.Binding.IsBound, toolbar: true))
        {
            Undo.Record(Document);
            Document.ShowInSkeleton = !Document.ShowInSkeleton;
            Document.Binding.Skeleton?.UpdateSprites();
            Document.MarkMetaModified();
        }

        if (EditorUI.Button(SkeletonOverlayButtonId, EditorAssets.Sprites.IconBone, selected: Document.ShowSkeletonOverlay, disabled: !Document.Binding.IsBound, toolbar: true))
        {
            Document.ShowSkeletonOverlay = !Document.ShowSkeletonOverlay;
            Document.MarkMetaModified();
        }
    }

    private void OpenBonePopupMenu()
    {
        var items = new List<PopupMenuItem>();
        var skeletonIndex = 0;

        foreach (var doc in DocumentManager.Documents)
        {
            if (doc is not SkeletonDocument skeleton || skeleton.BoneCount == 0)
                continue;

            items.Add(PopupMenuItem.Submenu(skeleton.Name, showIcons: false, showChecked: true, isChecked: () => Document.Binding.Skeleton == skeleton));

            for (var i = 0; i < skeleton.BoneCount; i++)
            {
                var skel = skeleton;
                var boneIndex = i;
                var boneName = skeleton.Bones[i].Name;
                var isBound = Document.Binding.IsBound && Document.Binding.Skeleton == skeleton && Document.Binding.BoneIndex == i;
                items.Add(PopupMenuItem.Item(boneName, () => CommitBoneBinding(skel, boneIndex), level: 1, isChecked: () => isBound));
            }

            skeletonIndex++;
        }

        if (items.Count == 0)
        {
            Notifications.AddError("no skeletons available");
            return;
        }

        var buttonRect = UI.GetElementRectInCanvas(EditorStyle.CanvasId.DocumentEditor, BoneBindButtonId);
        var popupStyle = new PopupStyle
        {
            AnchorX = Align.Min,
            AnchorY = Align.Min,
            PopupAlignX = Align.Min,
            PopupAlignY = Align.Max,
            ClampToScreen = true,
            AnchorRect = buttonRect,
            MinWidth = buttonRect.Width,
        };

        Editor.PopupMenu.Open([.. items], null, popupStyle, showChecked: true, showIcons: false);
    }

    private void UpdateSelectionColor()
    {
        var shape = Document.GetFrame(_currentFrame).Shape;

        for (ushort p = (ushort)(shape.PathCount - 1); p < shape.PathCount; p--)
        {
            ref readonly var path = ref shape.GetPath(p);
            if (!path.IsSelected) continue;

            _currentFillColor = path.FillColor;
            _currentFillOpacity = path.FillOpacity;
            _currentStrokeColor = path.StrokeColor;
            _selectionLayer = path.Layer;
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

    private void PalettePopupUI()
    {
        void Content()
        {
            for (int i=0; i<PaletteManager.Palettes.Count; i++)
            {
                if (EditorUI.PopupItem(
                    PaletteManager.Palettes[i].Label,
                    selected: PaletteManager.Palettes[i].Row == Document.Palette))
                {
                    Undo.Record(Document);
                    Document.Palette = (byte)PaletteManager.Palettes[i].Row;
                    Document.MarkModified();
                    MarkRasterDirty();
                    EditorUI.ClosePopup();
                }
            }
        }

        EditorUI.Popup(PaletteButtonId, Content);
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
                ConstraintsButtonId,
                ConstraintsButtonContent,
                selected: EditorUI.IsPopupOpen(ConstraintsButtonId),
                disabled: sizes.Length == 0))
                EditorUI.TogglePopup(ConstraintsButtonId);

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
                Document.MarkMetaModified();
                MarkRasterDirty();
                EditorUI.ClosePopup();
            }
        }

        EditorUI.Popup(ConstraintsButtonId, Content);
    }

    private void LayerButtonUI()
    {
        var spriteLayers = EditorApplication.Config.SpriteLayers;
        using (UI.BeginContainer(ContainerStyle.Fit))
        {
            void ButtonContent()
            {
                EditorUI.ControlIcon(EditorAssets.Sprites.IconLayer);
                if (EditorApplication.Config.TryGetSpriteLayer(_selectionLayer, out var spriteLayer))
                {
                    EditorUI.ControlText(spriteLayer.Label);
                    EditorUI.ControlPlaceholderText(spriteLayer.LayerLabel);
                }
                else
                    EditorUI.ControlPlaceholderText("None");
               UI.Spacer(EditorStyle.Control.Spacing);
            }

            if (EditorUI.Control(
                LayerButtonId,
                ButtonContent,
                selected: EditorUI.IsPopupOpen(LayerButtonId)))
                EditorUI.TogglePopup(LayerButtonId);

            LayerPopupUI();
        }
    }

    private void LayerPopupUI()
    {
        void Content()
        {
            var layers = EditorApplication.Config.SpriteLayers;
            for (int i = 0; i < layers.Length; i++)
            {
                ref readonly var layerDef = ref layers[i];
                var selected = _selectionLayer == layerDef.Layer;
                var label = layerDef.Label;
                var layerLabel = layerDef.LayerLabel;

                void ItemContent()
                {
                    EditorUI.ControlText($"{label}");
                    EditorUI.ControlPlaceholderText($"{layerLabel}");
                }

                if (EditorUI.PopupItem(ItemContent, selected: selected))
                {
                    SetPathLayer(layerDef.Layer);
                    EditorUI.ClosePopup();
                }
            }

            if (EditorUI.PopupItem("None", selected: _selectionLayer == 0))
            {
                SetPathLayer(0);
                EditorUI.ClosePopup();
            }
        }

        EditorUI.Popup(LayerButtonId, Content);
    }
   
    private void UpdateRaster()
    {
        var shape = Document.GetFrame(_currentFrame).Shape;
        shape.UpdateSamples();
        shape.UpdateBounds();

        Document.UpdateBounds();
        var bounds = Document.RasterBounds;
        var size = bounds.Size;
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            _rasterDirty = false;
            return;
        }

        var rasterRect = new RectInt(0, 0, size.X + Padding * 2, size.Y + Padding * 2);
        _image.Clear(rasterRect);
        var palette = PaletteManager.GetPalette(Document.Palette);
        if (palette != null)
            shape.Rasterize(
                _image,
                rasterRect.Expand(-Padding),
                -Document.RasterBounds.Position,
                palette.Colors,
                options: new Shape.RasterizeOptions {
                    AntiAlias = Document.IsAntiAliased,
                    Color = Color.White.WithAlpha(Workspace.XrayAlpha)
                });

        for (int p = Padding - 1; p >= 0; p--)
            _image.ExtrudeEdges(new RectInt(
                p,
                p,
                size.X + (Padding - p) * 2, size.Y + (Padding - p) * 2));

        _rasterTexture.Update(_image.AsByteSpan(), rasterRect, _image.Width);
        _rasterDirty = false;
    }

    public void MarkRasterDirty()
    {
        _rasterDirty = true;
    }

    public void SetCurrentFrame(ushort frame)
    {
        var newFrame = (ushort)Math.Min(frame, Document.FrameCount - 1);
        if (newFrame != _currentFrame)
        {
            _currentFrame = newFrame;
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
        if (Document.FrameCount == 0)
            return;

        _currentFrame = (ushort)((_currentFrame + 1) % Document.FrameCount);
        MarkRasterDirty();
    }

    private void PreviousFrame()
    {
        if (Document.FrameCount == 0)
            return;

        _currentFrame = _currentFrame == 0 ? (ushort)(Document.FrameCount - 1) : (ushort)(_currentFrame - 1);
        MarkRasterDirty();
    }

    public void SetFillColor(byte color, float opacity)
    {
        _currentFillColor = color;
        _currentFillOpacity = opacity;

        Undo.Record(Document);

        var shape = Document.GetFrame(_currentFrame).Shape;
        for (ushort p = 0; p < shape.PathCount; p++)
        {
            ref readonly var path = ref shape.GetPath(p);
            if (!path.IsSelected) continue;
            shape.SetPathFillColor(p, _currentFillColor, _currentFillOpacity);
        }

        Document.MarkModified();
        MarkRasterDirty();
    }

    public void SetStrokeColor(byte color, float opacity)
    {
        _currentStrokeColor = color;
        _currentStrokeOpacity = opacity;

        Undo.Record(Document);

        var shape = Document.GetFrame(_currentFrame).Shape;
        for (ushort p = 0; p < shape.PathCount; p++)
        {
            ref readonly var path = ref shape.GetPath(p);
            if (!path.IsSelected) continue;
            shape.SetPathStrokeColor(p, _currentStrokeColor, _currentStrokeOpacity);
        }

        Document.MarkModified();
        MarkRasterDirty();
    }

    public void SetPathLayer(byte layer)
    {
        _selectionLayer = layer;

        Undo.Record(Document);

        var shape = Document.GetFrame(_currentFrame).Shape;
        for (ushort p = 0; p < shape.PathCount; p++)
        {
            ref readonly var path = ref shape.GetPath(p);
            if (!path.IsSelected) continue;
            shape.SetPathLayer(p, layer);
        }

        Document.UpdateBounds();
        Document.MarkModified();
        MarkRasterDirty();
    }

    public void DeleteSelected()
    {
        Undo.Record(Document);
        var shape = Document.GetFrame(_currentFrame).Shape;
        shape.DeleteAnchors();
        Document.MarkModified();
        Document.UpdateBounds();
        MarkRasterDirty();
    }

    private void DuplicateSelected()
    {
        var shape = Document.GetFrame(_currentFrame).Shape;
        if (!shape.HasSelection())
            return;

        Undo.Record(Document);

        Span<ushort> pathsToDuplicate = stackalloc ushort[Shape.MaxPaths];
        var pathCount = 0;

        for (ushort p = 0; p < shape.PathCount; p++)
        {
            if (PathHasSelectedAnchor(shape, shape.GetPath(p)))
                pathsToDuplicate[pathCount++] = p;
        }

        if (pathCount == 0)
            return;

        shape.ClearAnchorSelection();

        var firstNewAnchor = shape.AnchorCount;

        for (var i = 0; i < pathCount; i++)
        {
            var srcPath = shape.GetPath(pathsToDuplicate[i]);
            var newPathIndex = shape.AddPath(
                fillColor: srcPath.FillColor,
                fillOpacity: srcPath.FillOpacity,
                strokeColor: srcPath.StrokeColor,
                layer: srcPath.Layer);
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
        var shape = Document.GetFrame(_currentFrame).Shape;
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

        var shape = Document.GetFrame(_currentFrame).Shape;
        shape.ClearSelection();

        clipboardData.PasteInto(shape);

        Document.MarkModified();
        Document.UpdateBounds();
        MarkRasterDirty();
        UpdateSelectionColor();
    }

    private void CenterShape()
    {
        if (Document.FrameCount == 0)
            return;

        Undo.Record(Document);

        for (ushort fi = 0; fi < Document.FrameCount; fi++)
            Document.Frames[fi].Shape.CenterOnOrigin();

        Document.UpdateBounds();
        Document.MarkModified();
        MarkRasterDirty();
        Notifications.Add("origin → center");
    }

    private void FlipHorizontal()
    {
        var shape = Document.GetFrame(_currentFrame).Shape;
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
        var shape = Document.GetFrame(_currentFrame).Shape;
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
        var shape = Document.GetFrame(_currentFrame).Shape;
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
        var shape = Document.GetFrame(_currentFrame).Shape;
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
        var shape = Document.GetFrame(_currentFrame).Shape;
        for (ushort i = 0; i < shape.AnchorCount; i++)
            shape.SetAnchorSelected(i, true);
        UpdateSelectionColor();
    }

    private void UpdateAnimation()
    {
        if (!_isPlaying || Document.FrameCount <= 1)
            return;

        _playTimer += Time.DeltaTime;
        var frame = Document.GetFrame(_currentFrame);
        var holdTime = Math.Max(1, frame.Hold) / 12f;

        if (_playTimer >= holdTime)
        {
            _playTimer = 0;
            NextFrame();
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
        var shape = Document.GetFrame(_currentFrame).Shape;
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
            if (hits[i].AnchorIndex != ushort.MaxValue)
                anchorHits[anchorCount++] = hits[i].AnchorIndex;
            else if (hits[i].SegmentIndex != ushort.MaxValue)
                segmentHits[segmentCount++] = hits[i].SegmentIndex;
        }

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

        UpdateSelectionColor();
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
        var shape = Document.GetFrame(_currentFrame).Shape;
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

    private void BeginMoveBoneOrigin()
    {
        if (!Document.Binding.IsBound) return;

        var savedOffset = Document.Binding.Offset;

        Workspace.BeginTool(new MoveTool(
            update: delta =>
            {
                // Moving the bone origin gizmo changes the offset
                // Offset is where the sprite origin should be in skeleton space
                Document.Binding.Offset = savedOffset + delta;
            },
            commit: _ =>
            {
                Document.MarkMetaModified();
            },
            cancel: () =>
            {
                Document.Binding.Offset = savedOffset;
            }
        ));
    }

    private void BeginRotateTool()
    {
        var shape = Document.GetFrame(_currentFrame).Shape;
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

    private void BeginScaleTool()
    {
        var shape = Document.GetFrame(_currentFrame).Shape;
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
                var pivot = Input.IsShiftDown() ? Vector2.Zero : localPivot.Value;
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
        var shape = Document.GetFrame(_currentFrame).Shape;

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
        var shape = Document.GetFrame(_currentFrame).Shape;
        var shift = Input.IsShiftDown(InputScope.All);

        if (!shift)
            shape.ClearAnchorSelection();

        Matrix3x2.Invert(Document.Transform, out var invTransform);
        var minLocal = Vector2.Transform(bounds.Min, invTransform);
        var maxLocal = Vector2.Transform(bounds.Max, invTransform);
        var localRect = Rect.FromMinMax(minLocal, maxLocal);
        shape.SelectAnchors(localRect);

        UpdateSelectionColor();
    }

    private void SelectAnchor(ushort anchorIndex, bool toggle)
    {
        var shape = Document.GetFrame(_currentFrame).Shape;

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

        UpdateSelectionColor();
    }

    private void SelectSegment(ushort anchorIndex, bool toggle)
    {
        var shape = Document.GetFrame(_currentFrame).Shape;
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

        UpdateSelectionColor();
    }

    private void SelectPath(ushort pathIndex, bool toggle)
    {
        var shape = Document.GetFrame(_currentFrame).Shape;
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

        UpdateSelectionColor();
    }

    private void SplitSegment(ushort anchorIndex)
    {
        var shape = Document.GetFrame(_currentFrame).Shape;
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
        var shape = Document.GetFrame(_currentFrame).Shape;
        Workspace.BeginTool(new PenTool(this, shape, _currentFillColor));
    }

    private void BeginKnifeTool()
    {
        var shape = Document.GetFrame(_currentFrame).Shape;
        Workspace.BeginTool(new KnifeTool(this, shape, commit: () =>
        {
            shape.UpdateSamples();
            shape.UpdateBounds();
            MarkRasterDirty();
        }));
    }

    private void BeginRectangleTool()
    {
        var shape = Document.GetFrame(_currentFrame).Shape;
        Workspace.BeginTool(new ShapeTool(
            this,
            shape,
            _currentFillColor,
            ShapeType.Rectangle,
            opacity: _currentFillOpacity));
    }

    private void BeginCircleTool()
    {
        var shape = Document.GetFrame(_currentFrame).Shape;
        Workspace.BeginTool(new ShapeTool(
            this,
            shape,
            _currentFillColor,
            ShapeType.Circle,
            opacity: _currentFillOpacity));
    }

    private void InsertAnchorAtHover()
    {
        Matrix3x2.Invert(Document.Transform, out var invTransform);
        var hit = Document.GetFrame(_currentFrame).Shape.HitTest(
            Vector2.Transform(Workspace.MouseWorldPosition, invTransform));

        if (hit.SegmentIndex == ushort.MaxValue)
            return;

        Undo.Record(Document);

        var shape = Document.GetFrame(_currentFrame).Shape;
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

        var dpi = EditorApplication.Config.PixelsPerUnit;
        var invDpi = 1f / dpi;
        var quad = new Rect(
            rb.X * invDpi,
            rb.Y * invDpi,
            rb.Width * invDpi,
            rb.Height * invDpi);

        var texSizeInv = 1.0f / (float)_image.Width;

        var uv = new Rect(
            Padding * texSizeInv,
            Padding * texSizeInv,
            rb.Width * texSizeInv,
            rb.Height * texSizeInv);

        using (Graphics.PushState())
        {
            Graphics.SetSortGroup(3);
            Graphics.SetLayer(EditorLayer.DocumentEditor);
            Graphics.SetShader(EditorAssets.Shaders.Texture);
            Graphics.SetTransform(Document.Transform);
            Graphics.SetTexture(_rasterTexture);
            Graphics.SetColor(Color.White);
            Graphics.Draw(quad, uv);

            if (Document.ShowTiling)
            {
                var tileSize = new Vector2(rb.Width * invDpi, rb.Height * invDpi);
                ReadOnlySpan<Vector2> offsets =
                [
                    // Outer ring (5x5)
                    new(-2 * tileSize.X, -2 * tileSize.Y),
                    new(-tileSize.X, -2 * tileSize.Y),
                    new(0f, -2 * tileSize.Y),
                    new(tileSize.X, -2 * tileSize.Y),
                    new(2 * tileSize.X, -2 * tileSize.Y),
                    new(-2 * tileSize.X, -tileSize.Y),
                    new(2 * tileSize.X, -tileSize.Y),
                    new(-2 * tileSize.X, 0f),
                    new(2 * tileSize.X, 0f),
                    new(-2 * tileSize.X, tileSize.Y),
                    new(2 * tileSize.X, tileSize.Y),
                    new(-2 * tileSize.X, 2 * tileSize.Y),
                    new(-tileSize.X, 2 * tileSize.Y),
                    new(0f, 2 * tileSize.Y),
                    new(tileSize.X, 2 * tileSize.Y),
                    new(2 * tileSize.X, 2 * tileSize.Y),
                    // Inner ring (3x3)
                    new(-tileSize.X, -tileSize.Y),
                    new(0f, -tileSize.Y),
                    new(tileSize.X, -tileSize.Y),
                    new(-tileSize.X, 0f),
                    new(tileSize.X, 0f),
                    new(-tileSize.X, tileSize.Y),
                    new(0f, tileSize.Y),
                    new(tileSize.X, tileSize.Y),
                ];

                Graphics.SetTextureFilter(TextureFilter.Point);
                Graphics.SetColor(Color.White.WithAlpha(0.85f));
                foreach (var offset in offsets)
                    Graphics.Draw(new Rect(quad.X + offset.X, quad.Y + offset.Y, quad.Width, quad.Height), uv, order: 2);
            }
        }
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

    private static void DrawSegments(Shape shape)
    {
        using (Gizmos.PushState(EditorLayer.DocumentEditor))
        {
            Gizmos.SetColor(EditorStyle.Workspace.LineColor);
            for (ushort anchorIndex = 0; anchorIndex < shape.AnchorCount; anchorIndex++)
            {
                if (!shape.IsSegmentSelected(anchorIndex))
                {
                    var pathIndex = FindPathForAnchor(shape, anchorIndex);
                    if (pathIndex != ushort.MaxValue)
                        DrawSegment(shape, pathIndex, anchorIndex, EditorStyle.Shape.SegmentLineWidth, 1);
                }
            }

            Gizmos.SetColor(EditorStyle.Workspace.SelectionColor);
            for (ushort anchorIndex = 0; anchorIndex < shape.AnchorCount; anchorIndex++)
            {
                if (shape.IsSegmentSelected(anchorIndex))
                {
                    var pathIndex = FindPathForAnchor(shape, anchorIndex);
                    if (pathIndex != ushort.MaxValue)
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

    private static void DrawAnchors(Shape shape)
    {
        using var _ = Gizmos.PushState(EditorLayer.DocumentEditor);
        for (ushort i = 0; i < shape.AnchorCount; i++)
        {
            ref readonly var anchor = ref shape.GetAnchor(i);
            if (anchor.IsSelected) continue;
            DrawAnchor(anchor.Position);
        }

        for (ushort i = 0; i < shape.AnchorCount; i++)
        {
            ref readonly var anchor = ref shape.GetAnchor(i);
            if (!anchor.IsSelected) continue;
            DrawSelectedAnchor(anchor.Position);
        }
    }

    private void DrawSkeletonOverlay()
    {
        var skeleton = Document.Binding.Skeleton;
        if (skeleton == null)
            return;

        // Calculate offset to position skeleton so bound bone aligns with bone origin gizmo
        var skeletonOffset = Document.Binding.Offset;

        using (Graphics.PushState())
        {
            Graphics.SetSortGroup(0);
            Graphics.SetLayer(EditorLayer.DocumentEditor);
            foreach (var sprite in skeleton.Sprites)
            {
                if (sprite == Document) continue;
                Graphics.SetBlendMode(BlendMode.Alpha);
                Graphics.SetTransform(Matrix3x2.CreateTranslation(skeletonOffset - sprite.Binding.Offset) * Document.Transform);
                sprite.DrawSprite(alpha: 0.3f);
            }
        }

        using (Gizmos.PushState(EditorLayer.DocumentEditor))
        {
            Graphics.SetSortGroup(6);
            Graphics.SetTransform(Document.Transform * Matrix3x2.CreateTranslation(skeletonOffset));

            for (var boneIndex = 0; boneIndex < skeleton.BoneCount; boneIndex++)
                Gizmos.DrawBoneAndJoints(skeleton, boneIndex, selected: boneIndex == Document.Binding.BoneIndex);
        }
    }

    #region Bone Binding

    private void DrawBoneOrigin()
    {
        if (!Document.Binding.IsBound) return;
        using var _ = Graphics.PushState();
        Graphics.SetTransform(Document.Transform * Matrix3x2.CreateTranslation(Document.Binding.Offset));
        Gizmos.DrawOrigin(EditorStyle.SpriteEditor.BoneOriginColor);
    }


    private void CommitBoneBinding(SkeletonDocument skeleton, int boneIndex)
    {
        Undo.Record(Document);

        var wasBound = Document.Binding.IsBound;

        Vector2 offset;
        if (wasBound)
        {
            var boneWorldPos = Vector2.Transform(Vector2.Zero, skeleton.LocalToWorld[boneIndex]);
            var spriteWorldPos = Document.Position - skeleton.Position;
            offset = spriteWorldPos - boneWorldPos;
        }
        else
        {
            offset = -Vector2.Transform(Vector2.Zero, skeleton.LocalToWorld[boneIndex]);
        }

        Document.SetBoneBinding(skeleton, boneIndex);
        Document.Binding.Offset = offset;
        skeleton.UpdateSprites();
        var boneName = skeleton.Bones[boneIndex].Name;
        Notifications.Add($"bound to {skeleton.Name}:{boneName}");
    }

    private void ClearBoneBinding()
    {
        if (!Document.Binding.IsBound)
        {
            Notifications.Add("sprite has no bone binding");
            return;
        }

        Undo.Record(Document);
        Document.ClearBoneBinding();
        Notifications.Add("bone binding cleared");
    }

    private void BoneOriginToOrigin()
    {
        if (!Document.Binding.IsBound) return;
        Undo.Record(Document);
        Document.Binding.Offset = Vector2.Zero;
        Document.MarkMetaModified();
        Notifications.Add("bone origin → origin");
    }

    private void BoneOriginToBone()
    {
        var skeleton = Document.Binding.Skeleton;
        if (skeleton == null) return;
        Undo.Record(Document);
        var bonePos = Vector2.Transform(Vector2.Zero, skeleton.LocalToWorld[Document.Binding.BoneIndex]);
        Document.Binding.Offset = -bonePos;
        Document.MarkMetaModified();
        Notifications.Add("bone origin → bone");
    }

    private void OriginToBoneOrigin()
    {
        if (!Document.Binding.IsBound) return;
        var boneOriginPos = Document.Binding.Offset;
        if (boneOriginPos == Vector2.Zero) return;

        Undo.Record(Document);

        for (ushort fi = 0; fi < Document.FrameCount; fi++)
            Document.Frames[fi].Shape.SetOrigin(boneOriginPos);

        Document.Binding.Offset = Vector2.Zero;
        Document.UpdateBounds();
        Document.MarkModified();
        Document.MarkMetaModified();
        MarkRasterDirty();
        Notifications.Add("origin → bone origin");
    }

    private void ClearSelection()
    {
        for (int i=0; i<Document.FrameCount; i++)
            Document.GetFrame((ushort)i).Shape.ClearSelection();
    }

    #endregion
}
