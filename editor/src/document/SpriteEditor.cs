//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ.Editor;

public enum SpriteEditorTool
{
    None,
    Curve
}

public class SpriteEditor : DocumentEditor
{
    private const byte RootId = 1;
    private const byte PaletteButtonId = 2;
    private const byte TileButtonId = 3;
    private const byte FirstPaletteColorId = 64;
   
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
    private bool _showPalettes;

    private readonly Command[] _commands;

    public SpriteEditor(SpriteDocument document) : base(document)
    {
        _rasterTexture = Texture.Create(
            _pixelData.Width,
            _pixelData.Height,
            _pixelData.AsByteSpan(),
            TextureFormat.RGBA8,
            TextureFilter.Point,
            "SpriteEditor");

        _commands =
        [
            new Command { Name = "Toggle Playback", ShortName = "play", Handler = TogglePlayback, Key = InputCode.KeySpace },
            new Command { Name = "Previous Frame", ShortName = "prev", Handler = PreviousFrame, Key = InputCode.KeyQ },
            new Command { Name = "Next Frame", ShortName = "next", Handler = NextFrame, Key = InputCode.KeyE },
            new Command { Name = "Delete", ShortName = "delete", Handler = DeleteSelected, Key = InputCode.KeyX },
            new Command { Name = "Move", ShortName = "move", Handler = BeginMoveTool, Key = InputCode.KeyG },
            new Command { Name = "Rotate", ShortName = "rotate", Handler = BeginRotateTool, Key = InputCode.KeyR },
            new Command { Name = "Scale", ShortName = "scale", Handler = BeginScaleTool, Key = InputCode.KeyS },
            new Command { Name = "Insert Anchor", ShortName = "insert", Handler = InsertAnchorAtHover, Key = InputCode.KeyV },
            new Command { Name = "Pen Tool", ShortName = "pen", Handler = BeginPenTool, Key = InputCode.KeyP },
            new Command { Name = "Knife Tool", ShortName = "knife", Handler = BeginKnifeTool, Key = InputCode.KeyK },
        ];
    }

    public override Command[]? GetCommands() => _commands;

    // Selection
    private byte _selectionColor;
    private byte _selectionOpacity = 10;

    // Tool state
    private SpriteEditorTool _activeTool = SpriteEditorTool.None;
    private readonly Vector2[] _savedPositions = new Vector2[Shape.MaxAnchors];
    private readonly float[] _savedCurves = new float[Shape.MaxAnchors];
    private ushort _curveAnchor = ushort.MaxValue;

    private ushort _hoveredAnchor = ushort.MaxValue;
    private ushort _hoveredSegment = ushort.MaxValue;
    private ushort _hoveredPath = ushort.MaxValue;

    public ushort CurrentFrame => _currentFrame;
    public bool IsPlaying => _isPlaying;
    public byte SelectionColor => _selectionColor;
    public byte SelectionOpacity => _selectionOpacity;

    public override void Dispose()
    {
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
        using (UI.BeginColumn(EditorStyle.SpriteEditor.Root, id: RootId))
        {
            // Toobar
            using (UI.BeginRow(EditorStyle.Overlay.Toolbar))
            {
                UI.Flex();
                if (EditorUI.Button(TileButtonId, EditorAssets.Sprites.IconTiling, _showTiling))
                    _showTiling = !_showTiling;
                if (EditorUI.Button(PaletteButtonId, EditorAssets.Sprites.IconPalette, _showPalettes, disabled: PaletteManager.Palettes.Count < 2))
                    _showPalettes = !_showPalettes;
            }

            using (UI.BeginContainer(EditorStyle.Overlay.Content))
            {
                PalettePickerUI();
                ColorPickerUI();
            }
        }
    }

    private void PalettePickerUI()
    {
        if (!_showPalettes) return;
        if (PaletteManager.Palettes.Count > 0) return;

        using (UI.BeginColumn(ContainerStyle.Default with
        {
            Padding = EdgeInsets.All(4f),
            Color = EditorStyle.Overlay.ContentColor,
            Spacing = 4.0f
        }))
        {
            for (int i = 0; i < PaletteManager.Palettes.Count; i++)
            {
                if ((byte)i != Document.Palette)
                    PaletteUI(PaletteManager.GetPalette((byte)i)!, showSelection: false);
            }
        }
    }

    private void ColorPickerUI()
    {
        var palette = PaletteManager.GetPalette(Document.Palette);
        if (palette == null)
            return;

        using (UI.BeginColumn(EditorStyle.SpriteEditor.ColorPicker))
        {
            using (UI.BeginRow())
            {
                PaletteUI(palette, showSelection: !_isPlaying);

                using (UI.BeginContainer(ContainerStyle.Default with
                {
                    AlignX = Align.Max,
                    AlignY = Align.Max,
                    Margin = EdgeInsets.All(4f)
                }))
                {
                    OpacityButtonUI();
                }
            }
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

    private void UpdateSelectionColorFromSelection()
    {
        var shape = Document.GetFrame(_currentFrame).Shape;

        for (ushort p = (ushort)(shape.PathCount - 1); p < shape.PathCount; p--)
        {
            var path = shape.GetPath(p);
            if (PathHasSelectedAnchor(shape, path))
            {
                _selectionColor = path.FillColor;
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
            UI.Label(palette.Name, new LabelStyle
            {
                FontSize = EditorStyle.Overlay.TextSize,
                Color = EditorStyle.Overlay.TextColor,
                AlignX = Align.Min
            });

            UI.Spacer(2f);

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
        using (UI.BeginContainer(
            EditorStyle.SpriteEditor.PaletteColor,
            id: (byte)(FirstPaletteColorId + colorIndex)))
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
        var buttonSize = 24.0f; //  EditorStyle.ColorPickerColorSize * 2;
        using (UI.BeginContainer(ContainerStyle.Default with
        {
            Width = buttonSize,
            Height = buttonSize,
            Border = new BorderStyle { Radius = EditorStyle.ButtonBorderRadius }
        }))
        {
            if (UI.IsHovered())
            {
                UI.Container(ContainerStyle.Default with
                {
                    Color = EditorStyle.Control.SelectedFillColor,
                    Border = new BorderStyle { Radius = EditorStyle.ButtonBorderRadius }
                });
            }

            var opacityAlpha = _selectionOpacity / 10f;
            UI.Label($"{(int)(_selectionOpacity * 10)}%", new LabelStyle
            {
                FontSize = EditorStyle.Overlay.TextSize,
                Color = EditorStyle.Overlay.TextColor,
                AlignX = Align.Center,
                AlignY = Align.Center
            });
        }
    }
    
    private void UpdateRaster()
    {
        var shape = Document.GetFrame(_currentFrame).Shape;
        shape.UpdateSamples();
        shape.UpdateBounds();

        var rb = shape.RasterBounds;
        if (rb.Width <= 0 || rb.Height <= 0)
        {
            _rasterDirty = false;
            return;
        }

        var offset = new Vector2Int(-rb.X, -rb.Y) + Vector2Int.One;
        var size = shape.RasterBounds.Size;
        _pixelData.Clear(new RectInt(0,0,size.X+2, size.Y + 2));

        var palette = PaletteManager.GetPalette(Document.Palette);
        if (palette != null)
            shape.Rasterize(_pixelData, palette.Colors, offset, options: new Shape.RasterizeOptions
            {
                AntiAlias = false
            });

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

    public void SetSelectionOpacity(byte opacity)
    {
        _selectionOpacity = opacity;
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
        UpdateHover();

        if (_activeTool != SpriteEditorTool.None)
        {
            UpdateActiveTool();
            return;
        }

        if (Input.WasButtonPressed(InputCode.KeyDelete))
            DeleteSelected();
    }

    private void UpdateHover()
    {
        Matrix3x2.Invert(Document.Transform, out var invTransform);
        var hit = Document.GetFrame(_currentFrame).Shape.HitTest(
            Vector2.Transform(Workspace.MouseWorldPosition, invTransform),
            EditorStyle.Shape.AnchorHitSize / Workspace.Zoom,
            EditorStyle.Shape.SegmentHitSize / Workspace.Zoom);

        _hoveredAnchor = hit.AnchorIndex;
        _hoveredSegment = hit.SegmentIndex;
        _hoveredPath = hit.PathIndex;
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
        if (_hoveredPath == ushort.MaxValue)
            return;

        var shape = Document.GetFrame(_currentFrame).Shape;
        Matrix3x2.Invert(Document.Transform, out var invTransform);
        var localPos = Vector2.Transform(Workspace.MouseWorldPosition, invTransform);

        if (!shape.IsPointInPath(localPos, _hoveredPath))
            return;

        SelectPath(_hoveredPath, Input.IsShiftDown());
        Input.ConsumeButton(InputCode.MouseLeft);
    }

    private void UpdateActiveTool()
    {
        switch (_activeTool)
        {
            case SpriteEditorTool.Curve:
                UpdateCurveTool();
                break;
        }
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
                var snap = Input.IsCtrlDown();
                shape.MoveSelectedAnchors(delta, _savedPositions, snap);
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
        Matrix3x2.Invert(Document.Transform, out var invTransform);

        Undo.Record(Document);

        for (ushort i = 0; i < shape.AnchorCount; i++)
            _savedPositions[i] = shape.GetAnchor(i).Position;

        Workspace.BeginTool(new RotateTool(
            worldPivot,
            localPivot.Value,
            invTransform,
            update: angle =>
            {
                shape.RotateAnchors(localPivot.Value, angle, _savedPositions);
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

    private void BeginScaleTool()
    {
        var shape = Document.GetFrame(_currentFrame).Shape;
        var localPivot = GetLocalTransformPivot(shape);
        if (!localPivot.HasValue)
            return;

        var worldPivot = Vector2.Transform(localPivot.Value, Document.Transform);

        Undo.Record(Document);

        for (ushort i = 0; i < shape.AnchorCount; i++)
            _savedPositions[i] = shape.GetAnchor(i).Position;

        Workspace.BeginTool(new ScaleTool(
            worldPivot,
            update: scale =>
            {
                shape.ScaleAnchors(localPivot.Value, scale, _savedPositions);
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

    private Vector2? GetLocalTransformPivot(Shape shape)
    {
        return shape.GetSelectedAnchorsCentroid();
    }

    private void BeginCurveTool(ushort anchorIndex)
    {
        var shape = Document.GetFrame(_currentFrame).Shape;

        for (ushort i = 0; i < shape.AnchorCount; i++)
        {
            var anchor = shape.GetAnchor(i);
            _savedCurves[i] = anchor.Curve;
        }

        _curveAnchor = anchorIndex;
        _activeTool = SpriteEditorTool.Curve;
    }

    private void UpdateCurveTool()
    {
        if (_curveAnchor == ushort.MaxValue)
            return;

        var shape = Document.GetFrame(_currentFrame).Shape;
        var anchor = shape.GetAnchor(_curveAnchor);
        var pathIdx = FindPathForAnchor(shape, _curveAnchor);
        if (pathIdx == ushort.MaxValue)
            return;

        var path = shape.GetPath(pathIdx);
        var nextAnchorIdx = path.AnchorStart + ((_curveAnchor - path.AnchorStart + 1) % path.AnchorCount);
        var nextAnchor = shape.GetAnchor((ushort)nextAnchorIdx);

        var p0 = anchor.Position;
        var p1 = nextAnchor.Position;
        var dir = p1 - p0;
        var perp = Vector2.Normalize(new Vector2(-dir.Y, dir.X));

        var mouseWorld = Workspace.MouseWorldPosition;
        var midpoint = (p0 + p1) * 0.5f;
        var offset = mouseWorld - midpoint;
        var newCurve = Vector2.Dot(offset, perp);

        shape.SetAnchorCurve(_curveAnchor, newCurve);
        shape.UpdateSamples();
        shape.UpdateBounds();

        if (Input.WasButtonReleased(InputCode.MouseLeft))
        {
            CommitCurveTool();
        }
        else if (Input.WasButtonPressed(InputCode.KeyEscape))
        {
            CancelCurveTool();
        }
    }

    private void CommitCurveTool()
    {
        _activeTool = SpriteEditorTool.None;
        _curveAnchor = ushort.MaxValue;
        Document.MarkModified();
        Document.UpdateBounds();
        MarkRasterDirty();
    }

    private void CancelCurveTool()
    {
        var shape = Document.GetFrame(_currentFrame).Shape;
        shape.RestoreAnchorCurves(_savedCurves);
        shape.UpdateSamples();
        shape.UpdateBounds();

        _activeTool = SpriteEditorTool.None;
        _curveAnchor = ushort.MaxValue;
    }

    private void HandleDragStart()
    {
        Workspace.BeginTool(new BoxSelectTool(CommitBoxSelectAnchors));
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

        UpdateSelectionColorFromSelection();
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

        UpdateSelectionColorFromSelection();
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

        UpdateSelectionColorFromSelection();
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

        UpdateSelectionColorFromSelection();
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

    private void InsertAnchorAtHover()
    {
        if (_hoveredSegment == ushort.MaxValue)
            return;

        Undo.Record(Document);

        Matrix3x2.Invert(Document.Transform, out var invTransform);
        var localMousePos = Vector2.Transform(Workspace.MouseWorldPosition, invTransform);

        var shape = Document.GetFrame(_currentFrame).Shape;
        shape.ClearSelection();
        shape.SplitSegmentAtPoint(_hoveredSegment, localMousePos);

        var newAnchorIdx = (ushort)(_hoveredSegment + 1);
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
                shape.MoveSelectedAnchors(delta, _savedPositions, snap);
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
        var rb = shape.RasterBounds;
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
}
