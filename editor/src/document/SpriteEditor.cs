//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ.Editor;

public class SpriteEditor : DocumentEditor
{
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
    private const byte PalettePopupId = 12;
    private const byte FirstPaletteId = 64;
    private const byte FirstPaletteColorId = 128;
    private static readonly string[] OpacityStrings = ["0%", "10%", "20%", "30%", "40%", "50%", "60%", "70%", "80%", "90%", "100%"];
   
    public new SpriteDocument Document => (SpriteDocument)base.Document;

    private ushort _currentFrame;
    private bool _isPlaying;
    private float _playTimer;
    private readonly PixelData<Color32> _pixelData = new(
        EditorApplication.Config!.AtlasSize,
        EditorApplication.Config!.AtlasSize);
    private readonly Texture _rasterTexture;
    private bool _rasterDirty = true;
    private bool _showTiling;
    private bool _showOpacityPopup;
    private bool _showPalettePopup;

    public SpriteEditor(SpriteDocument document) : base(document)
    {
        _rasterTexture = Texture.Create(
            _pixelData.Width,
            _pixelData.Height,
            _pixelData.AsByteSpan(),
            TextureFormat.RGBA8,
            TextureFilter.Point,
            "SpriteEditor");

        var deleteCommand = new Command { Name = "Delete", Handler = DeleteSelected, Key = InputCode.KeyX, Icon = EditorAssets.Sprites.IconDelete };
        var exitEditCommand = new Command { Name = "Exit Edit Mode", Handler = Workspace.ToggleEdit, Key = InputCode.KeyTab };
        var moveCommand = new Command { Name = "Move", Handler = BeginMoveTool, Key = InputCode.KeyG, Icon = EditorAssets.Sprites.IconMove };
        var rotateCommand = new Command { Name = "Rotate", Handler = BeginRotateTool, Key = InputCode.KeyR };
        var scaleCommand = new Command { Name = "Scale", Handler = BeginScaleTool, Key = InputCode.KeyS };
        var bindCommand = new Command { Name = "Select Bone", Handler = HandleSelectBone, Key = InputCode.KeyB };
        var unbindCommand = new Command { Name = "Clear Bone", Handler = ClearBoneBinding, Key = InputCode.KeyB, Alt = true };
     
        Commands =
        [
            deleteCommand,
            exitEditCommand,
            moveCommand,
            rotateCommand,
            scaleCommand,
            bindCommand,
            unbindCommand,
            new Command { Name = "Toggle Playback", Handler = TogglePlayback, Key = InputCode.KeySpace },
            new Command { Name = "Previous Frame", Handler = PreviousFrame, Key = InputCode.KeyQ },
            new Command { Name = "Next Frame", Handler = NextFrame, Key = InputCode.KeyE },
            new Command { Name = "Curve", Handler = BeginCurveTool, Key = InputCode.KeyC },
            new Command { Name = "Center", Handler = CenterShape, Key = InputCode.KeyC, Shift = true },
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

        ContextMenu = new ContextMenuDef
        {
            Title = "Sprite",
            Items = [
                ContextMenuItem.FromCommand(deleteCommand, enabled: HasSelection),
                ContextMenuItem.FromCommand(moveCommand, enabled: HasSelection),
                ContextMenuItem.FromCommand(rotateCommand, enabled: HasSelection),
                ContextMenuItem.FromCommand(scaleCommand, enabled: HasSelection),
                ContextMenuItem.Separator(),
                ContextMenuItem.FromCommand(bindCommand),
                ContextMenuItem.FromCommand(unbindCommand, enabled: () => Document.Binding.IsBound),
                ContextMenuItem.Separator(),
                ContextMenuItem.FromCommand(exitEditCommand),
            ]
        };  
    }

    // Selection
    private byte _selectionColor;
    private byte _selectionOpacity = 10;
    private bool _selectionSubtract = false;

    // Tool state
    private readonly Vector2[] _savedPositions = new Vector2[Shape.MaxAnchors];
    private readonly float[] _savedCurves = new float[Shape.MaxAnchors];

    public ushort CurrentFrame => _currentFrame;
    public bool IsPlaying => _isPlaying;
    public byte SelectionColor => _selectionColor;
    public byte SelectionOpacity => _selectionOpacity;

    public override void Dispose()
    {
        if (Document.IsModified)
            AtlasManager.UpdateSprite(Document);

        _rasterTexture.Dispose();
        _pixelData.Dispose();
        base.Dispose();
    }

    public override void OnUndoRedo()
    {
        Document.UpdateBounds();
        MarkRasterDirty();
    }

    public override void Update()
    {
        UpdateAnimation();
        UpdateInput();

        if (_rasterDirty)
            UpdateRaster();

        var shape = Document.GetFrame(_currentFrame).Shape;

        DrawRaster(shape);

        using (Gizmos.PushState(EditorLayer.DocumentEditor))
        {
            Graphics.SetTransform(Document.Transform);
            Document.DrawOrigin();
            DrawSegments(shape);
            DrawAnchors(shape);
        }
    }

    public override void LateUpdate()
    {
        if (Workspace.DragStarted && Workspace.DragButton == InputCode.MouseLeft)
            HandleDragStart();
        else if (Input.WasButtonReleased(InputCode.MouseLeft))
            HandleLeftClick();
        else if (Input.WasButtonPressed(InputCode.MouseLeftDoubleClick))
            HandleDoubleClick();
    }

    public override void UpdateUI()
    {
        using (UI.BeginCanvas(id: EditorStyle.CanvasId.DocumentEditor))
        using (UI.BeginColumn(RootId, EditorStyle.SpriteEditor.Root))
        {
            // Toolbar
            using (UI.BeginRow(EditorStyle.Overlay.Toolbar))
            {
                UI.Flex();

                BoneBindingUI();

                using (UI.BeginFlex())
                using (UI.BeginRow(new ContainerStyle { Spacing = EditorStyle.Control.Spacing }))
                {
                    UI.Flex();

                    if (EditorUI.Button(
                        AntiAliasButtonId,
                        Document.IsAntiAliased ? EditorAssets.Sprites.IconAntialiasOn : EditorAssets.Sprites.IconAntialiasOff,
                        Document.IsAntiAliased,
                        toolbar: true))
                    {
                        Undo.Record(Document);
                        MarkRasterDirty();
                        Document.MarkModified();
                        Document.IsAntiAliased = !Document.IsAntiAliased;
                    }

                    if (EditorUI.Button(TileButtonId, EditorAssets.Sprites.IconTiling, _showTiling, toolbar: true))
                        _showTiling = !_showTiling;

                    using (UI.BeginContainer(ContainerStyle.Fit))
                    {
                        if (EditorUI.Button(PaletteButtonId, EditorAssets.Sprites.IconPalette, _showPalettePopup, disabled: PaletteManager.Palettes.Count < 2, toolbar: true))
                            _showPalettePopup = !_showPalettePopup;

                        PalettePopupUI();
                    }
                }
            }

            using (UI.BeginContainer(EditorStyle.Overlay.Content))
                ColorPickerUI();
        }
    }

    private void BoneBindingUI()
    {
        var binding = Document.Binding;
        var selected = Workspace.ActiveTool is BoneSelectTool;

        if (EditorUI.Control(BoneBindButtonId, () =>
        {
            using (UI.BeginRow())
            {
                EditorUI.ControlIcon(EditorAssets.Sprites.IconBone);

                if (binding.IsBound)
                {
                    EditorUI.ControlText(binding.SkeletonName);
                    EditorUI.ControlText(".");
                    EditorUI.ControlText(binding.BoneName);

                    UI.Spacer(EditorStyle.Control.Spacing);

                    using (UI.BeginContainer(BoneUnbindButtonId, EditorStyle.Button.IconContent with { Padding = EdgeInsets.All(4)}))
                    {
                        UI.Image(
                            EditorAssets.Sprites.IconDelete,
                            UI.IsHovered()
                                ? EditorStyle.Button.SelectedIcon
                                : EditorStyle.Button.Icon);

                        if (UI.WasPressed())
                            ClearBoneBinding();
                    }
                }
                else
                {
                    UI.Label("Select Bone...", EditorStyle.Button.DisabledText);
                    UI.Spacer(EditorStyle.Control.Spacing);
                }                
            }
        }, selected: selected))
            HandleSelectBone();

        if (EditorUI.Button(PreviewButtonId, EditorAssets.Sprites.IconPreview, selected: Document.ShowInSkeleton, disabled: !Document.Binding.IsBound, toolbar: true))
        {
            Undo.Record(Document);
            Document.ShowInSkeleton = !Document.ShowInSkeleton;
            Document.Binding.Skeleton?.UpdateSprites();
            Document.MarkMetaModified();
        }
    }

    private void ColorPickerUI()
    {
        var palette = PaletteManager.GetPalette(Document.Palette);
        if (palette == null)
            return;

        using (UI.BeginColumn(EditorStyle.SpriteEditor.ColorPicker))
        using (UI.BeginRow())
        {
            PaletteUI(palette, showSelection: !_isPlaying);
            OpacityButtonUI();
        }
    }

    private void GetSelectedColors(Span<bool> selectedColors)
    {
        var shape = Document.GetFrame(_currentFrame).Shape;
        var hasSelectedPaths = false;

        for (ushort p = 0; p < shape.PathCount; p++)
        {
            var path = shape.GetPath(p);
            if (!PathHasSelectedAnchor(shape, path))
                continue;

            hasSelectedPaths = true;
            if (path.FillColor < PaletteDef.ColorCount)
                selectedColors[path.FillColor] = true;
        }

        if (!hasSelectedPaths)
            selectedColors[_selectionColor] = true;
    }

    private void UpdateSelectionColor()
    {
        var shape = Document.GetFrame(_currentFrame).Shape;
        _selectionSubtract = false;
        _selectionOpacity = 0;

        for (ushort p = (ushort)(shape.PathCount - 1); p < shape.PathCount; p--)
        {
            var path = shape.GetPath(p);
            if (PathHasSelectedAnchor(shape, path))
            {
                _selectionColor = path.FillColor;
                _selectionSubtract |= path.IsSubtract;
                _selectionOpacity = Math.Max((byte)(path.FillOpacity * 10), _selectionOpacity);
                return;
            }
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

    private void PaletteUI(PaletteDef palette, bool showSelection)
    {
        Span<bool> selectedColors = stackalloc bool[PaletteDef.ColorCount];
        if (showSelection)
            GetSelectedColors(selectedColors);

        using (UI.BeginColumn(EditorStyle.SpriteEditor.Palette))
        {
            const int columns = 32;
            var rowCount = (PaletteDef.ColorCount + columns - 1) / columns;
            for (var row = 0; row < rowCount; row++)
            {
                using var _ = UI.BeginRow();
                for (var col = 0; col < columns; col++)
                {
                    var colorIndex = row * columns + col;
                    var isSelected = showSelection && selectedColors[colorIndex];
                    PaletteColorUI((byte)colorIndex, palette.Colors[colorIndex], isSelected);
                }
            }
        }
    }

    private void PaletteColorUI(byte colorIndex, Color color, bool selected)
    {
        using (UI.BeginContainer((byte)(FirstPaletteColorId + colorIndex), EditorStyle.SpriteEditor.PaletteColor))
        {
            if (selected)
                UI.Container(EditorStyle.SpriteEditor.PaletteSelectedColor);

            var displayColor = color.A > 0 ? color : EditorStyle.SpriteEditor.UndefinedColor;
            UI.Container(EditorStyle.SpriteEditor.PaletteDisplayColor with { Color = displayColor });

            if (UI.WasPressed() && color.A > 0)
                SetSelectionColor(colorIndex);
        }
    }

    private void OpacityButtonUI()
    {
        using (UI.BeginContainer())
        {
            using (UI.BeginContainer(OpacityButtonId, EditorStyle.SpriteEditor.OpacityButtonRoot))
            {
                EditorUI.ButtonFill(_showOpacityPopup, UI.IsHovered(), false);

                using (UI.BeginContainer(EditorStyle.SpriteEditor.OpacityButtonIconContainer))
                {
                    if (_selectionSubtract)
                    {
                        UI.Image(EditorAssets.Sprites.IconSubtract, EditorStyle.Button.Icon);
                    }
                    else
                    {
                        UI.Image(EditorAssets.Sprites.IconOpacity, EditorStyle.Button.Icon);
                        UI.Image(
                            EditorAssets.Sprites.IconOpacityOverlay,
                            EditorStyle.Button.Icon with { Color = Color.White.WithAlpha(_selectionOpacity / 10.0f) });
                    }
                }

                if (UI.WasPressed())
                    _showOpacityPopup = !_showOpacityPopup;
            }

            if (_showOpacityPopup)
                OpacityPopupUI();
        }
    }

    private void OpacityPopupUI()
    {
        var buttonRect = UI.GetElementRect(EditorStyle.CanvasId.DocumentEditor, OpacityButtonId);

        using (UI.BeginPopup(OpacityPopupId, EditorStyle.SpriteEditor.OpacityPopup with { AnchorRect = buttonRect }))
        {
            if (UI.IsClosed())
            {
                _showOpacityPopup = false;
                return;
            }

            using (UI.BeginContainer(EditorStyle.SpriteEditor.OpacityPopupRoot))
            using (UI.BeginColumn(ContainerStyle.Fit with { Spacing = EditorStyle.Control.Spacing }))
            {
                // Subtract
                using (UI.BeginContainer(SubtractButtonId, EditorStyle.Popup.Item))
                {
                    EditorUI.PopupItemFill(_selectionSubtract, UI.IsHovered());
                    using (UI.BeginRow(new ContainerStyle { Spacing = EditorStyle.Control.Spacing } ))
                    {
                        using (UI.BeginContainer(EditorStyle.Popup.IconContainer))
                            UI.Image(EditorAssets.Sprites.IconSubtract, style: EditorStyle.Popup.Icon);

                        UI.Label("Subtract", EditorStyle.Popup.Text);
                        UI.Spacer(EditorStyle.Control.Spacing);
                    }

                    if (UI.WasPressed())
                    {
                        SetSubtract();
                        _showOpacityPopup = false;
                    }
                }

                for (int i=0; i<=10; i++)
                {
                    using (UI.BeginContainer(FirstOpacityId + i, EditorStyle.Popup.Item))
                    {
                        EditorUI.PopupItemFill(_selectionOpacity == i, UI.IsHovered());
                        using (UI.BeginRow(new ContainerStyle { Spacing = EditorStyle.Control.Spacing }))
                        {
                            using (UI.BeginContainer(EditorStyle.Popup.IconContainer))
                            {
                                UI.Image(EditorAssets.Sprites.IconOpacity, style: EditorStyle.Popup.Icon);
                                UI.Image(
                                    EditorAssets.Sprites.IconOpacityOverlay,
                                    EditorStyle.Popup.Icon with { Color = Color.White.WithAlpha(i / 10.0f) });
                            }

                            UI.Label(OpacityStrings[i], EditorStyle.Popup.Text);
                            UI.Spacer(EditorStyle.Control.Spacing);
                        }

                        if (UI.WasPressed())
                        {
                            SetOpacity(i / 10.0f);
                            _showOpacityPopup = false;
                        }
                    }
                }
            }
        }
    }

    private void PalettePopupUI()
    {
        if (!_showPalettePopup) return;

        var buttonRect = UI.GetElementRect(EditorStyle.CanvasId.DocumentEditor, PaletteButtonId);

        using (UI.BeginPopup(PalettePopupId, EditorStyle.SpriteEditor.PalettePopup with { AnchorRect = buttonRect }))
        {
            if (UI.IsClosed())
            {
                _showPalettePopup = false;
                return;
            }

            using (UI.BeginContainer(EditorStyle.SpriteEditor.OpacityPopupRoot))
            using (UI.BeginColumn(ContainerStyle.Fit with { Spacing = EditorStyle.Control.Spacing }))
            {
                for (int i=0; i<PaletteManager.Palettes.Count; i++)
                {
                    using (UI.BeginContainer(FirstPaletteId + i, EditorStyle.Popup.Item))
                    {
                        var selected = Document.Palette == i;
                        var hovered = UI.IsHovered();
                        EditorUI.PopupItemFill(selected, UI.IsHovered());
                        using (UI.BeginRow(new ContainerStyle { Spacing = EditorStyle.Control.Spacing }))
                        {
                            EditorUI.PopupIcon(EditorAssets.Sprites.IconPalette, hovered, selected);
                            UI.Label(PaletteManager.Palettes[i].Name, EditorStyle.Popup.Text);
                            UI.Spacer(EditorStyle.Control.Spacing);
                        }

                        if (UI.WasPressed())
                        {
                            Undo.Record(Document);
                            Document.Palette = (byte)i;
                            Document.MarkModified();
                            MarkRasterDirty();
                            _showPalettePopup = false;
                        }
                    }
                }
            }
        }
    }

    private void SetOpacity(float value)
    {
        var shape = Document.GetFrame(_currentFrame).Shape;
        Undo.Record(Document);

        _selectionOpacity = (byte)(value * 10);
        _selectionSubtract = false;

        for (ushort p = 0; p < shape.PathCount; p++)
            if (PathHasSelectedAnchor(shape, shape.GetPath(p)))
            {
                shape.SetPathSubtract(p, false);
                shape.SetPathFillOpacity(p, value);
            }                

        Document.MarkModified();
        MarkRasterDirty();
    }

    private void SetSubtract()
    {
        var shape = Document.GetFrame(_currentFrame).Shape;
        Undo.Record(Document);

        _selectionSubtract = true;

        for (ushort p = 0; p < shape.PathCount; p++)
            if (PathHasSelectedAnchor(shape, shape.GetPath(p)))
                shape.SetPathSubtract(p, true);

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

        var offset = new Vector2Int(-bounds.X, -bounds.Y) + Vector2Int.One;
        _pixelData.Clear(new RectInt(0,0,size.X+2, size.Y + 2));
        var palette = PaletteManager.GetPalette(Document.Palette);
        if (palette != null)
            shape.Rasterize(
                _pixelData,
                palette.Colors,
                offset,
                options: new Shape.RasterizeOptions { AntiAlias = Document.IsAntiAliased });

        _rasterTexture.Update(_pixelData.AsByteSpan(), new RectInt(0, 0, size.X + 2, size.Y +2), _pixelData.Width);
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

    public void SetSelectionColor(byte color)
    {
        _selectionColor = color;
        ApplyColorToSelection();
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
            var newPathIndex = shape.AddPath(srcPath.FillColor);
            if (newPathIndex == ushort.MaxValue)
                break;

            if (srcPath.IsSubtract)
                shape.SetPathSubtract(newPathIndex, true);

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
        shape.ClearAnchorSelection();

        clipboardData.PasteInto(shape);

        Document.MarkModified();
        Document.UpdateBounds();
        MarkRasterDirty();
        UpdateSelectionColor();

        BeginMoveTool();
    }

    private void CenterShape()
    {
        var shape = Document.GetFrame(_currentFrame).Shape;
        if (shape.AnchorCount == 0)
            return;

        Undo.Record(Document);
        shape.CenterOnOrigin();
        Document.MarkModified();
        Document.UpdateBounds();
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
        var shape = Document.GetFrame(_currentFrame).Shape;
        var shift = Input.IsShiftDown();

        // Prioritize anchors/segments in focused paths over path selection
        Matrix3x2.Invert(Document.Transform, out var invTransform);
        var hit = shape.HitTest(
            Vector2.Transform(Workspace.MouseWorldPosition, invTransform),
            EditorStyle.Shape.AnchorHitSize / Workspace.Zoom,
            EditorStyle.Shape.SegmentHitSize / Workspace.Zoom);

        if (hit.AnchorIndex != ushort.MaxValue)
        {
            SelectAnchor(hit.AnchorIndex, shift);
            return;
        }

        if (hit.SegmentIndex != ushort.MaxValue)
        {
            SelectSegment(hit.SegmentIndex, shift);
            return;
        }

        // Click empty: clear anchor selection only, keep focus
        if (!shift)
            shape.ClearAnchorSelection();
    }

    private void HandleDoubleClick()
    {
        Matrix3x2.Invert(Document.Transform, out var invTransform);
        var hit = Document.GetFrame(_currentFrame).Shape.HitTest(
            Vector2.Transform(Workspace.MouseWorldPosition, invTransform),
            EditorStyle.Shape.AnchorHitSize / Workspace.Zoom,
            EditorStyle.Shape.SegmentHitSize / Workspace.Zoom);

        if (hit.PathIndex == ushort.MaxValue)
            return;

        SelectPath(hit.PathIndex, Input.IsShiftDown());
        Input.ConsumeButton(InputCode.MouseLeft);
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
                shape.TranslateAnchors(delta, _savedPositions, Input.IsCtrlDown());
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
                shape.UpdateSamples();
                shape.UpdateBounds();
                MarkRasterDirty();
            },
            commit: _ =>
            {
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
        var shift = Input.IsShiftDown();

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
        Workspace.BeginTool(new PenTool(this, shape, _selectionColor));
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
            _selectionColor,
            ShapeType.Rectangle,
            opacity: _selectionOpacity / 10.0f,
            subtract: _selectionSubtract));
    }

    private void BeginCircleTool()
    {
        var shape = Document.GetFrame(_currentFrame).Shape;
        Workspace.BeginTool(new ShapeTool(
            this,
            shape,
            _selectionColor,
            ShapeType.Circle,
            opacity: _selectionOpacity / 10.0f,
            subtract: _selectionSubtract));
    }

    private void InsertAnchorAtHover()
    {
        Matrix3x2.Invert(Document.Transform, out var invTransform);
        var hit = Document.GetFrame(_currentFrame).Shape.HitTest(
            Vector2.Transform(Workspace.MouseWorldPosition, invTransform),
            EditorStyle.Shape.AnchorHitSize / Workspace.Zoom,
            EditorStyle.Shape.SegmentHitSize / Workspace.Zoom);

        if (hit.SegmentIndex == ushort.MaxValue)
            return;

        Undo.Record(Document);

        var localMousePos = Vector2.Transform(Workspace.MouseWorldPosition, invTransform);
        var shape = Document.GetFrame(_currentFrame).Shape;
        shape.ClearSelection();
        shape.SplitSegmentAtPoint(hit.SegmentIndex, localMousePos);

        var newAnchorIdx = (ushort)(hit.SegmentIndex + 1);
        if (newAnchorIdx < shape.AnchorCount)
            shape.SetAnchorSelected(newAnchorIdx, true);

        Document.UpdateBounds();
        MarkRasterDirty();

        for (ushort i = 0; i < shape.AnchorCount; i++)
            _savedPositions[i] = shape.GetAnchor(i).Position;

        Workspace.BeginTool(new MoveTool(
            update: delta =>
            {
                var snap = Input.IsCtrlDown();
                shape.TranslateAnchors(delta, _savedPositions, snap);
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
                Undo.Cancel();
                MarkRasterDirty();
            }
        ));
    }

    private void ApplyColorToSelection()
    {
        Undo.Record(Document);

        var shape = Document.GetFrame(_currentFrame).Shape;

        for (ushort p = 0; p < shape.PathCount; p++)
        {
            var path = shape.GetPath(p);
            var hasSelectedAnchor = false;

            for (ushort a = 0; a < path.AnchorCount; a++)
            {
                var anchor = shape.GetAnchor((ushort)(path.AnchorStart + a));
                if ((anchor.Flags & Shape.AnchorFlags.Selected) != 0)
                {
                    hasSelectedAnchor = true;
                    break;
                }
            }

            if (hasSelectedAnchor)
            {
                shape.SetPathFillColor(p, _selectionColor);
            }
        }

        Document.MarkModified();
        MarkRasterDirty();
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

    private void DrawRaster(Shape shape)
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

        var texSizeInv = 1.0f / (float)_pixelData.Width;

        var uv = new Rect(
            1.0f * texSizeInv,
            1.0f * texSizeInv,
            rb.Width * texSizeInv,
            rb.Height * texSizeInv);

        using (Graphics.PushState())
        {
            Graphics.SetLayer(EditorLayer.DocumentEditor);
            Graphics.SetShader(EditorAssets.Shaders.Texture);
            Graphics.SetTransform(Document.Transform);
            Graphics.SetTexture(_rasterTexture);
            Graphics.SetColor(Color.White);
            Graphics.Draw(quad, uv);

            if (_showTiling)
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
            Gizmos.SetColor(EditorStyle.Shape.SegmentColor);
            for (ushort anchorIndex = 0; anchorIndex < shape.AnchorCount; anchorIndex++)
            {
                if (!shape.IsSegmentSelected(anchorIndex))
                {
                    var pathIndex = FindPathForAnchor(shape, anchorIndex);
                    if (pathIndex != ushort.MaxValue)
                        DrawSegment(shape, pathIndex, anchorIndex, EditorStyle.Shape.SegmentLineWidth, 1);
                }
            }

            Gizmos.SetColor(EditorStyle.Shape.SelectedSegmentColor);
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
        Gizmos.SetColor(EditorStyle.Shape.AnchorColor);
        Gizmos.DrawRect(worldPosition, EditorStyle.Shape.AnchorSize, order: 4);
    }

    private static void DrawSelectedAnchor(Vector2 worldPosition)
    {
        Gizmos.SetColor(EditorStyle.Shape.SelectedAnchorColor);
        Gizmos.DrawRect(worldPosition, EditorStyle.Shape.AnchorSize, order: 5);
    }

    private static void DrawAnchors(Shape shape)
    {
        // default
        using (Gizmos.PushState(EditorLayer.DocumentEditor))
        {
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
    }

    #region Bone Binding

    private void HandleSelectBone()
    {
        Workspace.BeginTool(new BoneSelectTool(CommitBoneBinding));
    }

    private void CommitBoneBinding(SkeletonDocument skeleton, int boneIndex)
    {
        Undo.Record(Document);
        Document.SetBoneBinding(skeleton, boneIndex);
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

    #endregion
}
