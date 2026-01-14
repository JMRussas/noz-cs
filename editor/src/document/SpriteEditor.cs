//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace noz.editor;

public enum SpriteEditorTool
{
    None,
    Move,
    Curve,
    BoxSelect
}

public class SpriteEditor
{
    private const int RasterTextureSize = 256;

    private SpriteDocument? _document;
    private InputSet _input = null!;
    private ushort _currentFrame;
    private bool _isPlaying;
    private float _playTimer;

    // Raster state
    private PixelData? _pixelData;
    private TextureHandle _rasterTexture;
    private bool _rasterDirty = true;

    // Selection
    private byte _selectionColor;
    private byte _selectionOpacity = 10;
    private bool _editFill = true;

    // Tool state
    private SpriteEditorTool _activeTool = SpriteEditorTool.None;
    private Vector2 _dragStartWorld;
    private Vector2 _dragStartScreen;
    private readonly Vector2[] _savedPositions = new Vector2[Shape.MaxAnchors];
    private readonly float[] _savedCurves = new float[Shape.MaxAnchors];
    private ushort _curveAnchor = ushort.MaxValue;
    private ushort _pendingSelectAnchor = ushort.MaxValue;
    private bool _selectOnUp;

    // Hover state
    private ushort _hoveredAnchor = ushort.MaxValue;
    private ushort _hoveredMidpoint = ushort.MaxValue;
    private ushort _hoveredSegment = ushort.MaxValue;
    private ushort _hoveredPath = ushort.MaxValue;

    // Box selection
    private Rect _selectionBox;

    // Rendering constants
    private const float EdgeWidth = 0.02f;
    private const float EdgeSelectedWidth = 0.05f;
    private const float VertexSize = 0.12f;
    private const float MidpointSize = 0.08f;

    public SpriteDocument? Document => _document;
    public ushort CurrentFrame => _currentFrame;
    public bool IsPlaying => _isPlaying;
    public byte SelectionColor => _selectionColor;
    public byte SelectionOpacity => _selectionOpacity;
    public bool EditFill => _editFill;

    public void Begin(SpriteDocument document)
    {
        _document = document;
        _currentFrame = 0;
        _isPlaying = false;
        _playTimer = 0;
        _activeTool = SpriteEditorTool.None;
        _rasterDirty = true;

        _pixelData = new PixelData(RasterTextureSize, RasterTextureSize);
        _rasterTexture = Application.RenderBackend.CreateTexture(
            RasterTextureSize, RasterTextureSize, _pixelData.AsBytes());

        _input = new InputSet("SpriteEditor");
        Input.PushInputSet(_input, inheritState: true);

        Workspace.FrameView(document.Bounds);
    }

    public void End()
    {
        Input.PopInputSet();

        if (_rasterTexture.IsValid)
        {
            Application.RenderBackend.DestroyTexture(_rasterTexture);
            _rasterTexture = default;
        }

        _pixelData?.Dispose();
        _pixelData = null;
        _document = null;
    }

    public void Update()
    {
        if (_document == null)
            return;

        UpdateAnimation();
        UpdateInput();
    }

    public void Draw()
    {
        if (_document == null)
            return;

        if (_rasterDirty)
            UpdateRaster();

        var shape = _document.GetFrame(_currentFrame).Shape;
        var zoom = Workspace.Zoom;
        var zoomScale = 1f / zoom;

        DrawRaster(shape);
        DrawShapeEdges(shape, zoomScale);
        DrawShapeVertices(shape, zoomScale);
        DrawShapeMidpoints(shape, zoomScale);

        if (_activeTool == SpriteEditorTool.BoxSelect)
            DrawSelectionBox();
    }

    private void UpdateRaster()
    {
        if (_document == null || _pixelData == null)
            return;

        var dpi = Editor.Config?.AtlasDpi ?? 64f;
        var shape = _document.GetFrame(_currentFrame).Shape;

        shape.UpdateSamples();
        shape.UpdateBounds(dpi);

        var rb = shape.RasterBounds;
        if (rb.Width <= 0 || rb.Height <= 0)
        {
            _rasterDirty = false;
            return;
        }

        _pixelData.Clear();

        var palette = PaletteManager.GetPalette(_document.Palette);
        if (palette != null)
        {
            shape.Rasterize(_pixelData, palette.Colors, new Vector2Int(-rb.X, -rb.Y), dpi);
        }

        Application.RenderBackend.UpdateTexture(
            _rasterTexture,
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
        if (_document == null)
            return;

        var newFrame = (ushort)Math.Min(frame, _document.FrameCount - 1);
        if (newFrame != _currentFrame)
        {
            _currentFrame = newFrame;
            MarkRasterDirty();
        }
    }

    public void TogglePlayback()
    {
        _isPlaying = !_isPlaying;
        _playTimer = 0;
    }

    public void NextFrame()
    {
        if (_document == null || _document.FrameCount == 0)
            return;

        _currentFrame = (ushort)((_currentFrame + 1) % _document.FrameCount);
        MarkRasterDirty();
    }

    public void PreviousFrame()
    {
        if (_document == null || _document.FrameCount == 0)
            return;

        _currentFrame = _currentFrame == 0 ? (ushort)(_document.FrameCount - 1) : (ushort)(_currentFrame - 1);
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

    public void ToggleEditMode()
    {
        _editFill = !_editFill;
    }

    public void DeleteSelected()
    {
        if (_document == null)
            return;

        var shape = _document.GetFrame(_currentFrame).Shape;
        shape.DeleteSelectedAnchors();
        _document.MarkModified();
        _document.UpdateBounds();
        MarkRasterDirty();
    }

    private void UpdateAnimation()
    {
        if (!_isPlaying || _document == null || _document.FrameCount <= 1)
            return;

        _playTimer += Time.DeltaTime;
        var frame = _document.GetFrame(_currentFrame);
        var holdTime = Math.Max(1, frame.Hold) / 12f;

        if (_playTimer >= holdTime)
        {
            _playTimer = 0;
            NextFrame();
        }
    }

    private void UpdateInput()
    {
        if (_document == null)
            return;

        UpdateHover();

        if (_activeTool != SpriteEditorTool.None)
        {
            UpdateActiveTool();
            return;
        }

        if (Input.WasButtonPressed(InputCode.KeySpace))
            TogglePlayback();

        if (Input.WasButtonPressed(InputCode.KeyQ))
            PreviousFrame();

        if (Input.WasButtonPressed(InputCode.KeyE))
            NextFrame();

        if (Input.WasButtonPressed(InputCode.KeyX) || Input.WasButtonPressed(InputCode.KeyDelete))
            DeleteSelected();

        if (Input.WasButtonPressed(InputCode.KeyH))
            ToggleEditMode();

        if (Input.WasButtonPressed(InputCode.MouseLeft))
            HandleLeftClick();
        else if (Input.WasButtonPressed(InputCode.MouseLeftDoubleClick))
            HandleDoubleClick();

        if (Workspace.DragStarted && Workspace.DragButton == InputCode.MouseLeft)
            HandleDragStart();
    }

    private void UpdateHover()
    {
        if (_document == null)
            return;

        var shape = _document.GetFrame(_currentFrame).Shape;
        var worldPos = Workspace.MouseWorldPosition;
        var zoom = Workspace.Zoom;
        var hitRadius = VertexSize / zoom;

        var hit = shape.HitTest(worldPos, hitRadius, hitRadius * 0.5f);

        _hoveredAnchor = hit.AnchorIndex;
        _hoveredMidpoint = hit.MidpointIndex;
        _hoveredSegment = hit.SegmentIndex;
        _hoveredPath = hit.PathIndex;
    }

    private void HandleLeftClick()
    {
        if (_document == null)
            return;

        var shape = _document.GetFrame(_currentFrame).Shape;
        var shift = Input.IsShiftDown();
        var alt = Input.IsAltDown();

        if (_hoveredMidpoint != ushort.MaxValue && !alt)
        {
            SplitSegment(_hoveredMidpoint);
            return;
        }

        if (_hoveredAnchor != ushort.MaxValue)
        {
            var anchor = shape.GetAnchor(_hoveredAnchor);
            var wasSelected = (anchor.Flags & Shape.AnchorFlags.Selected) != 0;

            if (wasSelected && !shift)
            {
                _selectOnUp = true;
                _pendingSelectAnchor = _hoveredAnchor;
            }
            else
            {
                SelectAnchor(_hoveredAnchor, shift);
            }
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
        if (_document == null || _hoveredPath == ushort.MaxValue)
            return;

        SelectPath(_hoveredPath, Input.IsShiftDown());
    }

    private void HandleDragStart()
    {
        if (_document == null)
            return;

        _dragStartWorld = Workspace.MouseWorldPosition;
        _dragStartScreen = Input.MousePosition;

        var shape = _document.GetFrame(_currentFrame).Shape;
        var alt = Input.IsAltDown();

        if (_hoveredMidpoint != ushort.MaxValue && alt)
        {
            BeginCurveTool(_hoveredMidpoint);
            return;
        }

        if (_hoveredAnchor != ushort.MaxValue)
        {
            var anchor = shape.GetAnchor(_hoveredAnchor);
            if ((anchor.Flags & Shape.AnchorFlags.Selected) == 0)
                SelectAnchor(_hoveredAnchor, Input.IsShiftDown());

            BeginMoveTool();
            return;
        }

        if (_hoveredSegment != ushort.MaxValue)
        {
            var pathIdx = FindPathForAnchor(shape, _hoveredSegment);
            if (pathIdx != ushort.MaxValue)
            {
                var path = shape.GetPath(pathIdx);
                var nextAnchor = (ushort)(path.AnchorStart + ((_hoveredSegment - path.AnchorStart + 1) % path.AnchorCount));

                if (!Input.IsShiftDown())
                    shape.ClearSelection();

                SelectAnchor(_hoveredSegment, true);
                SelectAnchor(nextAnchor, true);
                BeginMoveTool();
                return;
            }
        }

        BeginBoxSelect();
    }

    private void UpdateActiveTool()
    {
        switch (_activeTool)
        {
            case SpriteEditorTool.Move:
                UpdateMoveTool();
                break;
            case SpriteEditorTool.Curve:
                UpdateCurveTool();
                break;
            case SpriteEditorTool.BoxSelect:
                UpdateBoxSelect();
                break;
        }
    }

    private void BeginMoveTool()
    {
        if (_document == null)
            return;

        var shape = _document.GetFrame(_currentFrame).Shape;

        for (ushort i = 0; i < shape.AnchorCount; i++)
        {
            var anchor = shape.GetAnchor(i);
            _savedPositions[i] = anchor.Position;
        }

        _activeTool = SpriteEditorTool.Move;
        _selectOnUp = false;
        _pendingSelectAnchor = ushort.MaxValue;
    }

    private void UpdateMoveTool()
    {
        if (_document == null)
            return;

        var shape = _document.GetFrame(_currentFrame).Shape;
        var delta = Workspace.MouseWorldPosition - _dragStartWorld;
        var snap = Input.IsCtrlDown();

        shape.MoveSelectedAnchors(delta, _savedPositions, snap);
        shape.UpdateSamples();
        shape.UpdateBounds();

        if (Input.WasButtonReleased(InputCode.MouseLeft))
        {
            CommitMoveTool();
        }
        else if (Input.WasButtonPressed(InputCode.KeyEscape))
        {
            CancelMoveTool();
        }
    }

    private void CommitMoveTool()
    {
        if (_document == null)
            return;

        _activeTool = SpriteEditorTool.None;
        _document.MarkModified();
        _document.UpdateBounds();
        MarkRasterDirty();

        if (_selectOnUp && _pendingSelectAnchor != ushort.MaxValue)
        {
            var shape = _document.GetFrame(_currentFrame).Shape;
            shape.ClearSelection();
            SelectAnchor(_pendingSelectAnchor, false);
        }

        _selectOnUp = false;
        _pendingSelectAnchor = ushort.MaxValue;
    }

    private void CancelMoveTool()
    {
        if (_document == null)
            return;

        var shape = _document.GetFrame(_currentFrame).Shape;
        shape.RestoreAnchorPositions(_savedPositions);
        shape.UpdateSamples();
        shape.UpdateBounds();

        _activeTool = SpriteEditorTool.None;
        _selectOnUp = false;
        _pendingSelectAnchor = ushort.MaxValue;
    }

    private void BeginCurveTool(ushort anchorIndex)
    {
        if (_document == null)
            return;

        var shape = _document.GetFrame(_currentFrame).Shape;

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
        if (_document == null || _curveAnchor == ushort.MaxValue)
            return;

        var shape = _document.GetFrame(_currentFrame).Shape;
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
        if (_document == null)
            return;

        _activeTool = SpriteEditorTool.None;
        _curveAnchor = ushort.MaxValue;
        _document.MarkModified();
        _document.UpdateBounds();
        MarkRasterDirty();
    }

    private void CancelCurveTool()
    {
        if (_document == null)
            return;

        var shape = _document.GetFrame(_currentFrame).Shape;
        shape.RestoreAnchorCurves(_savedCurves);
        shape.UpdateSamples();
        shape.UpdateBounds();

        _activeTool = SpriteEditorTool.None;
        _curveAnchor = ushort.MaxValue;
    }

    private void BeginBoxSelect()
    {
        _activeTool = SpriteEditorTool.BoxSelect;
        _selectionBox = new Rect(_dragStartWorld.X, _dragStartWorld.Y, 0, 0);
    }

    private void UpdateBoxSelect()
    {
        if (_document == null)
            return;

        var mouseWorld = Workspace.MouseWorldPosition;
        var minX = MathF.Min(_dragStartWorld.X, mouseWorld.X);
        var minY = MathF.Min(_dragStartWorld.Y, mouseWorld.Y);
        var maxX = MathF.Max(_dragStartWorld.X, mouseWorld.X);
        var maxY = MathF.Max(_dragStartWorld.Y, mouseWorld.Y);
        _selectionBox = Rect.FromMinMax(new Vector2(minX, minY), new Vector2(maxX, maxY));

        if (Input.WasButtonReleased(InputCode.MouseLeft))
        {
            CommitBoxSelect();
        }
        else if (Input.WasButtonPressed(InputCode.KeyEscape))
        {
            _activeTool = SpriteEditorTool.None;
        }
    }

    private void CommitBoxSelect()
    {
        if (_document == null)
            return;

        var shape = _document.GetFrame(_currentFrame).Shape;
        var shift = Input.IsShiftDown();

        if (!shift)
            shape.ClearSelection();

        shape.SelectAnchorsInRect(_selectionBox);

        _activeTool = SpriteEditorTool.None;
    }

    private void SelectAnchor(ushort anchorIndex, bool additive)
    {
        if (_document == null)
            return;

        var shape = _document.GetFrame(_currentFrame).Shape;

        if (!additive)
            shape.ClearSelection();

        shape.SetAnchorSelected(anchorIndex, true);
    }

    private void SelectSegment(ushort anchorIndex, bool additive)
    {
        if (_document == null)
            return;

        var shape = _document.GetFrame(_currentFrame).Shape;
        var pathIdx = FindPathForAnchor(shape, anchorIndex);
        if (pathIdx == ushort.MaxValue)
            return;

        var path = shape.GetPath(pathIdx);
        var nextAnchor = (ushort)(path.AnchorStart + ((anchorIndex - path.AnchorStart + 1) % path.AnchorCount));

        if (!additive)
            shape.ClearSelection();

        shape.SetAnchorSelected(anchorIndex, true);
        shape.SetAnchorSelected(nextAnchor, true);
    }

    private void SelectPath(ushort pathIndex, bool additive)
    {
        if (_document == null)
            return;

        var shape = _document.GetFrame(_currentFrame).Shape;

        if (!additive)
            shape.ClearSelection();

        var path = shape.GetPath(pathIndex);
        for (ushort a = 0; a < path.AnchorCount; a++)
        {
            shape.SetAnchorSelected((ushort)(path.AnchorStart + a), true);
        }
    }

    private void SplitSegment(ushort anchorIndex)
    {
        if (_document == null)
            return;

        var shape = _document.GetFrame(_currentFrame).Shape;
        shape.ClearSelection();
        shape.SplitSegment(anchorIndex);

        var newAnchorIdx = (ushort)(anchorIndex + 1);
        if (newAnchorIdx < shape.AnchorCount)
            shape.SetAnchorSelected(newAnchorIdx, true);

        _document.MarkModified();
        _document.UpdateBounds();
        MarkRasterDirty();

        BeginMoveTool();
    }

    private void ApplyColorToSelection()
    {
        if (_document == null)
            return;

        var shape = _document.GetFrame(_currentFrame).Shape;

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
                if (_editFill)
                    shape.SetPathFillColor(p, _selectionColor);
                else
                    shape.SetPathStrokeColor(p, _selectionColor);
            }
        }

        _document.MarkModified();
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
        if (_pixelData == null || !_rasterTexture.IsValid)
            return;

        var rb = shape.RasterBounds;
        if (rb.Width <= 0 || rb.Height <= 0)
            return;

        if (EditorAssets.Shaders.Texture is Shader textureShader)
            Render.BindShader(textureShader);

        Render.Backend.BindTexture(0, _rasterTexture);

        var dpi = Editor.Config?.AtlasDpi ?? 64f;
        var invDpi = 1f / dpi;

        var quadX = rb.X * invDpi;
        var quadY = rb.Y * invDpi;
        var quadW = rb.Width * invDpi;
        var quadH = rb.Height * invDpi;

        var texSize = (float)_pixelData.Width;
        var u1 = rb.Width / texSize;
        var v1 = rb.Height / texSize;

        Render.DrawQuad(
            quadX,quadY, quadW, quadH,
            0, 0, u1, v1,
            _rasterTexture,
            Color32.White,
            layer: 100
        );
    }

    private void DrawShapeEdges(Shape shape, float zoomScale)
    {
        for (ushort p = 0; p < shape.PathCount; p++)
        {
            var path = shape.GetPath(p);

            for (ushort a = 0; a < path.AnchorCount; a++)
            {
                var a0Idx = (ushort)(path.AnchorStart + a);
                var a1Idx = (ushort)(path.AnchorStart + ((a + 1) % path.AnchorCount));

                var a0 = shape.GetAnchor(a0Idx);
                var a1 = shape.GetAnchor(a1Idx);

                var selected = (a0.Flags & Shape.AnchorFlags.Selected) != 0 ||
                               (a1.Flags & Shape.AnchorFlags.Selected) != 0;

                var color = selected ? EditorStyle.EdgeSelected : EditorStyle.Edge;
                var width = (selected ? EdgeSelectedWidth : EdgeWidth) * zoomScale;

                var samples = shape.GetSegmentSamples(a0Idx);
                var prev = a0.Position;

                foreach (var sample in samples)
                {
                    DrawLine(prev, sample, width, color);
                    prev = sample;
                }

                DrawLine(prev, a1.Position, width, color);
            }
        }
    }

    private void DrawShapeVertices(Shape shape, float zoomScale)
    {
        for (ushort i = 0; i < shape.AnchorCount; i++)
        {
            var anchor = shape.GetAnchor(i);
            var selected = (anchor.Flags & Shape.AnchorFlags.Selected) != 0;
            var hovered = i == _hoveredAnchor;

            var color = selected ? EditorStyle.VertexSelected : EditorStyle.Vertex;
            var size = VertexSize * zoomScale;

            if (hovered)
                size *= 1.2f;

            DrawVertex(anchor.Position, size, color);
        }
    }

    private void DrawShapeMidpoints(Shape shape, float zoomScale)
    {
        for (ushort p = 0; p < shape.PathCount; p++)
        {
            var path = shape.GetPath(p);

            for (ushort a = 0; a < path.AnchorCount; a++)
            {
                var anchorIdx = (ushort)(path.AnchorStart + a);
                var anchor = shape.GetAnchor(anchorIdx);
                var hovered = anchorIdx == _hoveredMidpoint;

                var color = hovered ? EditorStyle.EdgeSelected : new Color(0.5f, 0.5f, 0.5f, 0.5f);
                var size = MidpointSize * zoomScale;

                if (hovered)
                    size *= 1.3f;

                DrawCircle(anchor.Midpoint, size * 0.5f, color);
            }
        }
    }

    private void DrawSelectionBox()
    {
        var color = new Color(EditorStyle.SelectionColor.R, EditorStyle.SelectionColor.G, EditorStyle.SelectionColor.B, 0.2f);
        Render.DrawQuad(_selectionBox.X, _selectionBox.Y, _selectionBox.Width, _selectionBox.Height, color.ToColor32(), layer: 200);

        var borderColor = EditorStyle.SelectionColor;
        var zoom = Workspace.Zoom;
        var lineWidth = 0.02f / zoom;

        DrawLine(new Vector2(_selectionBox.X, _selectionBox.Y), new Vector2(_selectionBox.Right, _selectionBox.Y), lineWidth, borderColor);
        DrawLine(new Vector2(_selectionBox.Right, _selectionBox.Y), new Vector2(_selectionBox.Right, _selectionBox.Bottom), lineWidth, borderColor);
        DrawLine(new Vector2(_selectionBox.Right, _selectionBox.Bottom), new Vector2(_selectionBox.X, _selectionBox.Bottom), lineWidth, borderColor);
        DrawLine(new Vector2(_selectionBox.X, _selectionBox.Bottom), new Vector2(_selectionBox.X, _selectionBox.Y), lineWidth, borderColor);
    }

    private static void DrawLine(Vector2 a, Vector2 b, float width, Color color)
    {
        var dir = b - a;
        var len = dir.Length();
        if (len < 0.0001f)
            return;

        dir /= len;
        var perp = new Vector2(-dir.Y, dir.X) * width * 0.5f;

        var p0 = a - perp;
        var p1 = a + perp;
        var p2 = b + perp;
        var p3 = b - perp;

        var center = (a + b) * 0.5f;
        var transform = Matrix3x2.CreateTranslation(-center) *
                        Matrix3x2.CreateRotation(MathF.Atan2(dir.Y, dir.X)) *
                        Matrix3x2.CreateTranslation(center);

        Render.DrawQuad(
            center.X - len * 0.5f, center.Y - width * 0.5f,
            len, width,
            transform,
            color.ToColor32(),
            layer: 150,
            depth: 0
        );
    }

    private static void DrawVertex(Vector2 pos, float size, Color color)
    {
        var halfSize = size * 0.5f;
        Render.DrawQuad(pos.X - halfSize, pos.Y - halfSize, size, size, color.ToColor32(), layer: 160);
    }

    private static void DrawCircle(Vector2 pos, float radius, Color color)
    {
        Render.DrawQuad(pos.X - radius, pos.Y - radius, radius * 2, radius * 2, color.ToColor32(), layer: 155);
    }
}
