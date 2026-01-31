//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ.Editor;

public class PenTool : Tool
{
    private const int MaxPoints = 64;

    private struct PenPoint
    {
        public Vector2 Position;
        public ushort ExistingAnchor; // ushort.MaxValue if new point
    }

    private readonly SpriteEditor _editor;
    private readonly Shape _shape;
    private readonly byte _fillColor;
    private readonly PenPoint[] _points = new PenPoint[MaxPoints];
    private int _pointCount;

    private bool _hoveringFirstPoint;
    private bool _hoveringExistingAnchor;
    private ushort _hoverAnchorIndex = ushort.MaxValue;
    private Vector2 _hoverSnapPosition;
    private bool _hoveringSegment;
    private ushort _hoverSegmentIndex = ushort.MaxValue;
    private bool _snappingToGrid;
    private Vector2 _gridSnapPosition;

    public PenTool(SpriteEditor editor, Shape shape, byte fillColor)
    {
        _editor = editor;
        _shape = shape;
        _fillColor = fillColor;
    }

    public override void Begin()
    {
        Cursor.SetCrosshair();
    }

    public override void Update()
    {
        var mouseWorld = Workspace.MouseWorldPosition;
        Matrix3x2.Invert(_editor.Document.Transform, out var invTransform);
        var mouseLocal = Vector2.Transform(mouseWorld, invTransform);

        if (Input.WasButtonPressed(InputCode.KeyEscape))
        {
            Cancel();
            return;
        }

        if (Input.WasButtonPressed(InputCode.KeyEnter))
        {
            if (_pointCount >= 3)
                Commit();
            return;
        }

        if (Input.WasButtonPressed(InputCode.MouseRight))
        {
            if (_pointCount > 0)
                _pointCount--;
            return;
        }

        UpdateHover(mouseLocal);

        if (Input.WasButtonPressed(InputCode.MouseLeft))
            HandleLeftClick(mouseLocal);
    }

    private void UpdateHover(Vector2 mouseLocal)
    {
        _hoveringFirstPoint = false;
        if (_pointCount >= 3)
            _hoveringFirstPoint = Shape.HitTestAnchor(mouseLocal, _points[0].Position);

        _hoveringExistingAnchor = false;
        _hoverAnchorIndex = ushort.MaxValue;
        if (!_hoveringFirstPoint)
        {
            var hit = _shape.HitTest(mouseLocal);
            if (hit.AnchorIndex != ushort.MaxValue)
            {
                _hoveringExistingAnchor = true;
                _hoverAnchorIndex = hit.AnchorIndex;
                _hoverSnapPosition = _shape.GetAnchor(hit.AnchorIndex).Position;
            }
        }

        _hoveringSegment = false;
        _hoverSegmentIndex = ushort.MaxValue;
        if (!_hoveringFirstPoint && !_hoveringExistingAnchor)
        {
            var hit = _shape.HitTest(mouseLocal);
            if (hit.SegmentIndex != ushort.MaxValue)
            {
                _hoveringSegment = true;
                _hoverSegmentIndex = hit.SegmentIndex;
                _hoverSnapPosition = GetClosestPointOnSegment(_hoverSegmentIndex, mouseLocal);
            }
        }

        _snappingToGrid = false;
        if (!_hoveringFirstPoint && !_hoveringExistingAnchor && !_hoveringSegment && Input.IsCtrlDown())
        {
            _snappingToGrid = true;
            var worldPos = Vector2.Transform(mouseLocal, _editor.Document.Transform);
            var snapped = Grid.SnapToPixelGrid(worldPos);
            Matrix3x2.Invert(_editor.Document.Transform, out var invTransform);
            _gridSnapPosition = Vector2.Transform(snapped, invTransform);
        }
    }

    private Vector2 GetClosestPointOnSegment(ushort anchorIndex, Vector2 point)
    {
        ref readonly var a0 = ref _shape.GetAnchor(anchorIndex);
        ref readonly var a1 = ref _shape.GetNextAnchor(anchorIndex);

        var samples = _shape.GetSegmentSamples(anchorIndex);
        var bestDist = float.MaxValue;
        var bestPoint = (a0.Position + a1.Position) * 0.5f;

        var prev = a0.Position;
        for (var i = 0; i < Shape.MaxSegmentSamples; i++)
        {
            var closestOnSeg = ClosestPointOnLine(prev, samples[i], point);
            var dist = Vector2.DistanceSquared(closestOnSeg, point);
            if (dist < bestDist)
            {
                bestDist = dist;
                bestPoint = closestOnSeg;
            }
            prev = samples[i];
        }

        var closestLast = ClosestPointOnLine(prev, a1.Position, point);
        var distLast = Vector2.DistanceSquared(closestLast, point);
        if (distLast < bestDist)
            bestPoint = closestLast;

        return bestPoint;
    }

    private static Vector2 ClosestPointOnLine(Vector2 a, Vector2 b, Vector2 p)
    {
        var ab = b - a;
        var lenSq = Vector2.Dot(ab, ab);
        if (lenSq < 0.0001f)
            return a;

        var t = MathF.Max(0, MathF.Min(1, Vector2.Dot(p - a, ab) / lenSq));
        return a + ab * t;
    }

    private void HandleLeftClick(Vector2 mouseLocal)
    {
        if (_hoveringFirstPoint)
        {
            Commit();
            return;
        }

        if (_pointCount > 0)
        {
            var lastPos = _points[_pointCount - 1].Position;
            if (Vector2.Distance(mouseLocal, lastPos) < 0.001f)
                return;
        }

        if (_pointCount >= MaxPoints)
            return;

        if (_hoveringExistingAnchor)
        {
            _points[_pointCount++] = new PenPoint
            {
                Position = _hoverSnapPosition,
                ExistingAnchor = _hoverAnchorIndex
            };
            return;
        }

        if (_hoveringSegment)
        {
            _points[_pointCount++] = new PenPoint
            {
                Position = _hoverSnapPosition,
                ExistingAnchor = ushort.MaxValue
            };
            return;
        }

        var newPosition = _snappingToGrid ? _gridSnapPosition : mouseLocal;
        _points[_pointCount++] = new PenPoint
        {
            Position = newPosition,
            ExistingAnchor = ushort.MaxValue
        };
    }

    public override void Draw()
    {
        using (Gizmos.PushState(EditorLayer.Tool))
        {
            Graphics.SetTransform(_editor.Document.Transform);

            var lineWidth = Gizmos.GetLineWidth();
            var vertexSize = Gizmos.GetVertexSize();

            Gizmos.SetColor(EditorStyle.Tool.LineColor);
            for (var i = 0; i < _pointCount - 1; i++)
                Gizmos.DrawLine(_points[i].Position, _points[i + 1].Position, lineWidth);

            if (_pointCount > 0)
            {
                var lastPoint = _points[_pointCount - 1].Position;
                var target = GetCurrentTarget();
                Gizmos.DrawLine(lastPoint, target, lineWidth);
            }

            if (_pointCount >= 3 && !_hoveringFirstPoint)
            {
                var lastPoint = _points[_pointCount - 1].Position;
                var firstPoint = _points[0].Position;
                Gizmos.SetColor(EditorStyle.Workspace.SelectionColor.WithAlpha(0.3f));
                Gizmos.DrawDashedLine(lastPoint, firstPoint);
            }

            Gizmos.SetColor(EditorStyle.Workspace.SelectionColor);
            for (var i = 0; i < _pointCount; i++)
                Gizmos.DrawRect(_points[i].Position, vertexSize);

            if (_hoveringFirstPoint)
            {
                Gizmos.SetColor(EditorStyle.SelectionColor);
                Gizmos.DrawRect(_points[0].Position, vertexSize * 1.3f);
            }

            if (_hoveringExistingAnchor && !_hoveringFirstPoint)
            {
                Gizmos.SetColor(EditorStyle.SelectionColor);
                Gizmos.DrawRect(_hoverSnapPosition, vertexSize);
            }

            if (_hoveringSegment)
            {
                Gizmos.SetColor(EditorStyle.SelectionColor);
                Gizmos.DrawRect(_hoverSnapPosition, vertexSize);
            }
        }
    }

    private Vector2 GetCurrentTarget()
    {
        if (_hoveringFirstPoint)
            return _points[0].Position;

        if (_hoveringExistingAnchor)
            return _hoverSnapPosition;

        if (_hoveringSegment)
            return _hoverSnapPosition;

        if (_snappingToGrid)
            return _gridSnapPosition;

        Matrix3x2.Invert(_editor.Document.Transform, out var invTransform);
        return Vector2.Transform(Workspace.MouseWorldPosition, invTransform);
    }

    private void Commit()
    {
        if (_pointCount < 3)
        {
            Finish();
            return;
        }

        Undo.Record(_editor.Document);

        var signedArea = 0f;
        for (var i = 0; i < _pointCount; i++)
        {
            var v0 = _points[i].Position;
            var v1 = _points[(i + 1) % _pointCount].Position;
            signedArea += (v1.X - v0.X) * (v1.Y + v0.Y);
        }

        var pathIndex = _shape.AddPath(_fillColor);
        if (pathIndex == ushort.MaxValue)
        {
            Finish();
            return;
        }

        if (signedArea > 0)
        {
            for (var i = _pointCount - 1; i >= 0; i--)
                _shape.AddAnchor(pathIndex, _points[i].Position);
        }
        else
        {
            for (var i = 0; i < _pointCount; i++)
                _shape.AddAnchor(pathIndex, _points[i].Position);
        }

        _shape.UpdateSamples();
        _shape.UpdateBounds();

        _editor.Document.MarkModified();
        _editor.Document.UpdateBounds();
        _editor.MarkRasterDirty();

        Finish();
    }

    private void Finish()
    {
        Workspace.EndTool();
        Input.ConsumeButton(InputCode.MouseLeft);
    }

    public override void Cancel()
    {
        Finish();
    }
}
