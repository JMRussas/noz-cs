//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ.Editor;

public partial class SpriteEditor : DocumentEditor
{
    private const int Padding = 8;

    [ElementId("Root")]
    [ElementId("PaletteButton")]
    [ElementId("TileButton")]
    [ElementId("BoneBindButton")]
    [ElementId("BoneUnbindButton")]
    [ElementId("OpacityButton")]
    [ElementId("OpacityPopup")]
    [ElementId("SubtractButton")]
    [ElementId("AntiAliasButton")]
    [ElementId("FirstOpacity")]
    [ElementId("PreviewButton")]
    [ElementId("SkeletonOverlayButton")]
    [ElementId("DopeSheet")]
    [ElementId("ConstraintsButton")]
    [ElementId("FillColorButton")]
    [ElementId("StrokeColorButton")]
    [ElementId("LayerButton")]
    [ElementId("BonePathButton")]
    private static partial class ElementId { }

    public new SpriteDocument Document => (SpriteDocument)base.Document;

    private ushort _currentFrame;
    private bool _isPlaying;
    private float _playTimer;
    private readonly PixelData<Color32> _image = new(
        EditorApplication.Config!.AtlasSize,
        EditorApplication.Config!.AtlasSize);
    private readonly Texture _rasterTexture;
    private bool _rasterDirty = true;
    private readonly Vector2[] _savedPositions = new Vector2[Shape.MaxAnchors];
    private readonly float[] _savedCurves = new float[Shape.MaxAnchors];

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
                PopupMenuItem.Separator(),
                PopupMenuItem.FromCommand(deleteCommand, enabled: HasSelection),
                PopupMenuItem.Separator(),
                PopupMenuItem.FromCommand(exitEditCommand),
            ]
        };  
    }

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

        var color = (int)Document.CurrentFillColor;
        var opacity = Document.CurrentFillOpacity;

        if (EditorUI.ColorButton(ElementId.FillColorButton, Document.Palette, ref color, ref opacity, EditorAssets.Sprites.IconFill))
            SetFillColor((byte)color, opacity);

        var strokeColor = (int)Document.CurrentStrokeColor;
        var strokeOpacity = Document.CurrentStrokeOpacity;
        if (EditorUI.ColorButton(ElementId.StrokeColorButton, Document.Palette, ref strokeColor, ref strokeOpacity, EditorAssets.Sprites.IconStroke))
            SetStrokeColor((byte)strokeColor, strokeOpacity);

        // Palette 
        PaletteButtonUI();

        EditorUI.ToolbarSpacer();

        LayerButtonUI();

        BoneBindingUI();

        UI.Flex();

        if (EditorUI.Button(
            ElementId.AntiAliasButton,
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

            using (UI.BeginContainer(new ContainerStyle { Padding = EdgeInsets.LeftRight(2) }))
            {
                Span<EditorUI.DopeSheetFrame> frames = stackalloc EditorUI.DopeSheetFrame[Document.FrameCount];
                for (ushort i = 0; i < Document.FrameCount; i++)
                    frames[i] = new EditorUI.DopeSheetFrame { Hold = Document.Frames[i].Hold, };
                var currentFrame = 0;
                EditorUI.DopeSheet(ElementId.DopeSheet, frames, ref currentFrame, Sprite.MaxFrames, false);
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
            ElementId.PaletteButton,
            ButtonContent,
            selected: EditorUI.IsPopupOpen(ElementId.PaletteButton)))
            EditorUI.TogglePopup(ElementId.PaletteButton);

        PalettePopupUI();
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

    private void UpdateSelectionColor()
    {
        var shape = Document.GetFrame(_currentFrame).Shape;

        for (ushort p = (ushort)(shape.PathCount - 1); p < shape.PathCount; p--)
        {
            ref readonly var path = ref shape.GetPath(p);
            if (!path.IsSelected) continue;

            Document.CurrentFillColor = path.FillColor;
            Document.CurrentFillOpacity = path.FillOpacity;
            Document.CurrentStrokeColor = path.StrokeColor;
            Document.CurrentLayer = path.Layer;
            Document.CurrentBone = path.Bone;
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

        EditorUI.Popup(ElementId.PaletteButton, Content);
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
        var spriteLayers = EditorApplication.Config.SpriteLayers;
        using (UI.BeginContainer(ContainerStyle.Fit))
        {
            void ButtonContent()
            {
                EditorUI.ControlIcon(EditorAssets.Sprites.IconLayer);
                if (EditorApplication.Config.TryGetSpriteLayer(Document.CurrentLayer, out var spriteLayer))
                {
                    EditorUI.ControlText(spriteLayer.Label);
                    EditorUI.ControlPlaceholderText(spriteLayer.LayerLabel);
                }
                else
                    EditorUI.ControlPlaceholderText("None");
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
            var layers = EditorApplication.Config.SpriteLayers;
            for (int i = 0; i < layers.Length; i++)
            {
                ref readonly var layerDef = ref layers[i];
                var selected = Document.CurrentLayer == layerDef.Layer;
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

            if (EditorUI.PopupItem("None", selected: Document.CurrentLayer == 0))
            {
                SetPathLayer(0);
                EditorUI.ClosePopup();
            }
        }

        EditorUI.Popup(ElementId.LayerButton, Content);
    }

    private void BoneBindingUI()
    {
        if (!Document.Binding.IsBound) return;

        using var _ = UI.BeginContainer(ContainerStyle.Fit);

        void ButtonContent()
        {
            EditorUI.ControlIcon(EditorAssets.Sprites.IconBone);
            if (!Document.CurrentBone.IsNone)
                EditorUI.ControlText(Document.CurrentBone.ToString());
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

            // Root option (None = root bone)
            if (EditorUI.PopupItem("Root", selected: Document.CurrentBone.IsNone))
            {
                SetPathBone(StringId.None);
                EditorUI.ClosePopup();
            }

            // List all bones from the skeleton
            for (int i = 0; i < skeleton.BoneCount; i++)
            {
                var boneName = skeleton.Bones[i].Name;
                var boneNameValue = StringId.Get(boneName);
                if (EditorUI.PopupItem(boneName, selected: Document.CurrentBone == boneNameValue))
                {
                    SetPathBone(boneNameValue);
                    EditorUI.ClosePopup();
                }
            }
        }

        EditorUI.Popup(ElementId.BonePathButton, Content);
    }

    public void SetPathBone(StringId bone)
    {
        Document.CurrentBone = bone;

        Undo.Record(Document);

        var shape = Document.GetFrame(_currentFrame).Shape;
        for (ushort p = 0; p < shape.PathCount; p++)
        {
            ref readonly var path = ref shape.GetPath(p);
            if (!path.IsSelected) continue;
            shape.SetPathBone(p, bone);
        }

        Document.UpdateBounds();
        Document.MarkModified();
        MarkRasterDirty();
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
        Document.CurrentFillColor = color;
        Document.CurrentFillOpacity = opacity;

        Undo.Record(Document);

        var shape = Document.GetFrame(_currentFrame).Shape;
        for (ushort p = 0; p < shape.PathCount; p++)
        {
            ref readonly var path = ref shape.GetPath(p);
            if (!path.IsSelected) continue;
            shape.SetPathFillColor(p, Document.CurrentFillColor, Document.CurrentFillOpacity);
        }

        Document.MarkModified();
        MarkRasterDirty();
    }

    public void SetStrokeColor(byte color, float opacity)
    {
        Document.CurrentStrokeColor = color;
        Document.CurrentStrokeOpacity = opacity;

        Undo.Record(Document);

        var shape = Document.GetFrame(_currentFrame).Shape;
        for (ushort p = 0; p < shape.PathCount; p++)
        {
            ref readonly var path = ref shape.GetPath(p);
            if (!path.IsSelected) continue;
            shape.SetPathStrokeColor(p, Document.CurrentStrokeColor, Document.CurrentStrokeOpacity);
        }

        Document.MarkModified();
        MarkRasterDirty();
    }

    public void SetPathLayer(byte layer)
    {
        Document.CurrentLayer = layer;

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

    private void OnScale()
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
        Workspace.BeginTool(new PenTool(this, shape, Document.CurrentFillColor));
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
            Document.CurrentFillColor,
            ShapeType.Rectangle,
            opacity: Document.CurrentFillOpacity));
    }

    private void BeginCircleTool()
    {
        var shape = Document.GetFrame(_currentFrame).Shape;
        Workspace.BeginTool(new ShapeTool(
            this,
            shape,
            Document.CurrentFillColor,
            ShapeType.Circle,
            opacity: Document.CurrentFillOpacity));
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
                Gizmos.DrawBoneAndJoints(skeleton, boneIndex, selected: boneName == Document.CurrentBone);
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
        for (int i=0; i<Document.FrameCount; i++)
            Document.GetFrame((ushort)i).Shape.ClearSelection();
    }

    #endregion
}
