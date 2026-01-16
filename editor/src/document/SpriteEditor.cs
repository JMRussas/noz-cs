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
    private const float AnchorSelectionSize = 2.0f;
    private const float SegmentSelectionSize = 6.0f;

    public new SpriteDocument Document => (SpriteDocument)base.Document;

    private readonly InputSet _input = new("SpriteEditor");
    private ushort _currentFrame;
    private bool _isPlaying;
    private float _playTimer;
    private readonly PixelData<Color32> _pixelData = new(
        EditorApplication.Config!.AtlasSize,
        EditorApplication.Config!.AtlasSize);
    private readonly Texture _rasterTexture;
    private bool _rasterDirty = true;

    private readonly Command[] _commands;

    public SpriteEditor(SpriteDocument document) : base(document)
    {
        _rasterTexture = Texture.Create(
            _pixelData.Width,
            _pixelData.Height,
            _pixelData.AsBytes(),
            TextureFormat.RGBA8,
            TextureFilter.Nearest,
            "SpriteEditor");
        Input.PushInputSet(_input, inheritState: true);

        _commands =
        [
            new Command { Name = "Toggle Playback", ShortName = "play", Handler = TogglePlayback, Key = InputCode.KeySpace },
            new Command { Name = "Previous Frame", ShortName = "prev", Handler = PreviousFrame, Key = InputCode.KeyQ },
            new Command { Name = "Next Frame", ShortName = "next", Handler = NextFrame, Key = InputCode.KeyE },
            new Command { Name = "Delete Selected", ShortName = "delete", Handler = DeleteSelected, Key = InputCode.KeyX },
            new Command { Name = "Move Selected", ShortName = "move", Handler = BeginMoveToolFromSelection, Key = InputCode.KeyG },
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

    // Hover state
    private ushort _hoveredAnchor = ushort.MaxValue;
    private ushort _hoveredSegment = ushort.MaxValue;
    private ushort _hoveredPath = ushort.MaxValue;


    public ushort CurrentFrame => _currentFrame;
    public bool IsPlaying => _isPlaying;
    public byte SelectionColor => _selectionColor;
    public byte SelectionOpacity => _selectionOpacity;

    public override void Dispose()
    {
        Input.PopInputSet();
        _rasterTexture.Dispose();
        _pixelData.Dispose();
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

        Render.PushState();
        Render.SetTransform(Document.Transform);
        DrawRaster(shape);
        Render.SetLayer(EditorLayer.Gizmo);
        DrawSegments(shape);
        DrawAnchors(shape);
        Render.PopState();
    }
    
    private void UpdateRaster()
    {
        var dpi = EditorApplication.Config?.AtlasDpi ?? 64f;
        var shape = Document.GetFrame(_currentFrame).Shape;

        shape.UpdateSamples();
        shape.UpdateBounds(dpi);

        var rb = shape.RasterBounds;
        if (rb.Width <= 0 || rb.Height <= 0)
        {
            _rasterDirty = false;
            return;
        }

        _pixelData.Clear();

        var palette = PaletteManager.GetPalette(Document.Palette);
        if (palette != null)
        {
            shape.Rasterize(_pixelData, palette.Colors, new Vector2Int(-rb.X, -rb.Y), dpi);
        }

        Render.Driver.UpdateTexture(
            _rasterTexture!.Handle,
            _pixelData.Width, _pixelData.Height,
            _pixelData.AsBytes());

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
        var shape = Document.GetFrame(_currentFrame).Shape;
        shape.DeleteSelectedAnchors();
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

        if (Input.WasButtonPressed(InputCode.MouseLeft))
            HandleLeftClick();
        else if (Input.WasButtonPressed(InputCode.MouseLeftDoubleClick))
            HandleDoubleClick();

        if (Workspace.DragStarted && Workspace.DragButton == InputCode.MouseLeft)
            HandleDragStart();
    }

    private void UpdateHover()
    {
        Matrix3x2.Invert(Document.Transform, out var invTransform);
        var hit = Document.GetFrame(_currentFrame).Shape.HitTest(
            Vector2.Transform(Workspace.MouseWorldPosition, invTransform),
            EditorStyle.Shape.AnchorSize * AnchorSelectionSize / Workspace.Zoom,
            EditorStyle.Shape.SegmentWidth * SegmentSelectionSize / Workspace.Zoom);

        _hoveredAnchor = hit.AnchorIndex;
        _hoveredSegment = hit.SegmentIndex;
        _hoveredPath = hit.PathIndex;
    }

    private void HandleLeftClick()
    {
        var shape = Document.GetFrame(_currentFrame).Shape;
        var shift = Input.IsShiftDown();

        if (_hoveredAnchor != ushort.MaxValue)
        {
            SelectAnchor(_hoveredAnchor, shift);
            return;
        }

        if (_hoveredSegment != ushort.MaxValue)
        {
            SelectSegment(_hoveredSegment, shift);
            return;
        }

        if (_hoveredPath != ushort.MaxValue)
        {
            SelectPath(_hoveredPath, shift);
            return;
        }

        if (!shift)
            shape.ClearSelection();
    }

    private void HandleDoubleClick()
    {
        if (_hoveredPath == ushort.MaxValue)
            return;

        SelectPath(_hoveredPath, Input.IsShiftDown());
    }

    private void HandleDragStart()
    {
        BeginBoxSelect();
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

    private void BeginMoveToolFromSelection()
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

    private void BeginBoxSelect()
    {
        Workspace.BeginTool(new BoxSelectTool(CommitBoxSelect));
    }

    private void CommitBoxSelect(Rect bounds)
    {
        var shape = Document.GetFrame(_currentFrame).Shape;
        var shift = Input.IsShiftDown();

        if (!shift)
            shape.ClearSelection();

        // Transform selection box from world space to document space
        Matrix3x2.Invert(Document.Transform, out var invTransform);
        var minLocal = Vector2.Transform(bounds.Min, invTransform);
        var maxLocal = Vector2.Transform(bounds.Max, invTransform);
        var localRect = Rect.FromMinMax(minLocal, maxLocal);

        shape.SelectAnchorsInRect(localRect);
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

    private void ApplyColorToSelection()
    {
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

        var dpi = EditorApplication.Config?.AtlasDpi ?? 64f;
        var invDpi = 1f / dpi;
        var quadX = rb.X * invDpi;
        var quadY = rb.Y * invDpi;
        var quadW = rb.Width * invDpi;
        var quadH = rb.Height * invDpi;
        var texSize = (float)_pixelData.Width;
        var u1 = rb.Width / texSize;
        var v1 = rb.Height / texSize;

        Render.PushState();
        Render.SetShader(EditorAssets.Shaders.Texture!);
        Render.SetTexture(_rasterTexture);
        Render.SetColor(Color.White);
        Render.Draw(quadX, quadY, quadW, quadH, 0, 0, u1, v1);
        Render.PopState();
    }

    private static void DrawSegment(Shape shape, ushort segmentIndex, float width, ushort order = 0)
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
        // hover
        if (_hoveredSegment != ushort.MaxValue)
        {
            Render.PushState();
//            Render.SetColor(EditorStyle.Shape.HoverColor);
            DrawSegment(shape, _hoveredSegment, EditorStyle.Shape.SegmentHoverWidth, 0);
            Render.PopState();
        }

        // default
        Render.PushState();
        Render.SetColor(EditorStyle.Shape.SegmentColor);
        for (ushort anchorIndex=0; anchorIndex < shape.AnchorCount; anchorIndex++)
            if (!shape.IsSegmentSelected(anchorIndex))
                DrawSegment(shape, anchorIndex, EditorStyle.Shape.SegmentWidth, 1);
        Render.PopState();

        // selected
        Render.PushState();
        //Render.SetColor(EditorStyle.Shape.SelectionColor);
        for (ushort anchorIndex = 0; anchorIndex < shape.AnchorCount; anchorIndex++)
            if (shape.IsSegmentSelected(anchorIndex))
                DrawSegment(shape, anchorIndex, EditorStyle.Shape.SegmentWidth, 2);
        Render.PopState();        
    }

    private static void DrawAnchor(ref readonly Shape.Anchor anchor)
    {
        Gizmos.SetColor(EditorStyle.Shape.AnchorColor);
        Gizmos.DrawRect(anchor.Position, EditorStyle.Shape.AnchorSize * 0.85f, order: 5);
        Render.SetColor(EditorStyle.Shape.AnchorOutlineColor);
        Gizmos.DrawRect(anchor.Position, EditorStyle.Shape.AnchorSize, order: 4);
    }

    private static void DrawSelectedAnchor(ref readonly Shape.Anchor anchor)
    {
        Render.SetColor(EditorStyle.Shape.AnchorOutlineColor);
        Gizmos.DrawRect(anchor.Position, EditorStyle.Shape.AnchorSize * 0.85f, order: 5);
        Gizmos.SetColor(EditorStyle.Shape.AnchorColor);
        Gizmos.DrawRect(anchor.Position, EditorStyle.Shape.AnchorSize, order: 4);
    }

    private void DrawAnchors(Shape shape)
    {
        // hovered
        if (_hoveredAnchor != ushort.MaxValue)
            DrawSelectedAnchor(in shape.GetAnchor(_hoveredAnchor));
        
        // default
        Render.PushState();

        for (ushort i = 0; i < shape.AnchorCount; i++)
        {
            if (i == _hoveredAnchor) continue;
            ref readonly var anchor = ref shape.GetAnchor(i);
            if (anchor.IsSelected) continue;
            DrawAnchor(in anchor);
        }

        for (ushort i = 0; i < shape.AnchorCount; i++)
        {
            if (i == _hoveredAnchor) continue;
            ref readonly var anchor = ref shape.GetAnchor(i);
            if (!anchor.IsSelected) continue;
            DrawSelectedAnchor(in anchor);
        }
        
        Render.PopState();
    }
}
