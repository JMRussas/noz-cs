//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

#define NOZ_KNIFE_DEBUG

using System.Diagnostics;
using System.Numerics;

namespace NoZ.Editor;

public class KnifeTool : Tool
{
    private const int MaxPoints = 128;
    private const float AnchorHitScale = 2.0f;
    private const float SegmentHitScale = 6.0f;
    private const float HoverAnchorScale = 1.5f;
    private const float IntersectionAnchorScale = 1.2f;

    private struct KnifePoint
    {
        public Vector2 Position;
        public bool Intersection;
    }

    private NativeArray<KnifePoint> _points = new(MaxPoints);
    private readonly SpriteEditor _editor;
    private readonly Shape _shape;
    private Vector2 _hoverPosition;
    private bool _hoverPositionValid;
    private bool _hoverIsClose;
    private bool _hoverIsIntersection;
    private int _pointCount;

    public KnifeTool(SpriteEditor editor, Shape shape)
    {
        _editor = editor;
        _shape = shape;
    }

    public override void Dispose()
    {
        _points.Dispose();
        base.Dispose();
    }

    public override void Begin()
    {
        Cursor.SetCrosshair();
    }

    public override void Update()
    {
        if (Input.WasButtonPressed(InputCode.KeyEscape))
        {
            Cancel();
            return;
        }

        if (Input.WasButtonPressed(InputCode.KeyEnter))
        {
            if (_points.Length >= 2)
                Commit();
            else
                Cancel();

            return;
        }

        if (Input.WasButtonPressed(InputCode.MouseRight))
        {
            if (_points.Length > 0)
                RemoveLastPoint();
            else
                Cancel();

            return;
        }

        UpdateHover();

        if (Input.WasButtonPressed(InputCode.MouseLeft))
            AddPoint();
    }

    public override void Cancel()
    {
        Finish();
    }

    private bool DoesIntersectSelf(in Vector2 position)
    {
        if (_pointCount < 2)
            return false;

        var end = _points[_pointCount - 1].Position;
        for (var i = 0; i < _pointCount - 2; i++)
        {
            if (Physics.OverlapLine(
                end,
                position,
                _points[i].Position,
                _points[i + 1].Position,
                out _))
                return true;
        }

        return false;
    }

    private void AddPoint()
    {
        if (!_hoverPositionValid)
            return;

        // First point needs to be added directly (no line to intersect yet)
        if (_pointCount == 0)
            _points.Add(new KnifePoint { Position = _hoverPosition, Intersection = _hoverIsIntersection });

        _pointCount = _points.Length;

        if (_hoverIsClose)
            Commit();
    }

    private void RemoveLastPoint()
    {
    }

    private void Commit()
    {
        Finish();
    }
    
    private void UpdateHover()
    {
        Matrix3x2.Invert(_editor.Document.Transform, out var invTransform);
        var mouseLocal = Vector2.Transform(Workspace.MouseWorldPosition, invTransform);
        if (_hoverPosition != mouseLocal)
        {
            _hoverPosition = mouseLocal;
            _hoverPositionValid = !DoesIntersectSelf(_hoverPosition);

            var anchorHitSize = EditorStyle.Shape.AnchorSize * AnchorHitScale / Workspace.Zoom;
            var anchorHitSizeSqr = anchorHitSize * anchorHitSize;
            var segmentHitSize = EditorStyle.Shape.SegmentLineWidth * SegmentHitScale / Workspace.Zoom;
            var result = _shape.HitTest(_hoverPosition, anchorHitSize, segmentHitSize);

            _hoverIsClose = _points.Length > 0 && Vector2.DistanceSquared(_hoverPosition, _points[0].Position) < anchorHitSizeSqr;
            _hoverIsIntersection = false;
            if (_hoverIsClose)
            {
                _hoverPosition = _points[0].Position;
                _hoverPositionValid = true;
            }
            else if (result.AnchorIndex != ushort.MaxValue)
            {
                _hoverPosition = result.AnchorPoint;
                _hoverIsIntersection = true;
            }
            else if (result.SegmentIndex != ushort.MaxValue)
            {
                _hoverPosition = result.SegmentPoint;
                _hoverIsIntersection = true;
            }

            if (_pointCount > 0)
                UpdateHoverIntersections(_hoverPosition, _points[_pointCount-1].Position);
        }
    }

    public override void Draw()
    {
        using (Gizmos.PushState(EditorLayer.Tool))
        {
            Graphics.SetTransform(_editor.Document.Transform);

            Gizmos.SetColor(EditorStyle.KnifeTool.SegmentColor);
            for (var i = 0; i < _points.Length - 1; i++)
                Gizmos.DrawLine(_points[i].Position, _points[i + 1].Position, EditorStyle.Shape.SegmentLineWidth);

            if (_points.Length > 0)
            {
                Gizmos.SetColor(_hoverPositionValid
                    ? EditorStyle.KnifeTool.SegmentColor
                    : EditorStyle.KnifeTool.InvalidSegmentColor);
                Gizmos.DrawLine(_points[^1].Position, _hoverPosition, EditorStyle.Shape.SegmentLineWidth);
            }

            for (var i = 0; i < _points.Length; i++)
            {
                ref var point = ref _points[i];
                Gizmos.SetColor(point.Intersection
                    ? EditorStyle.KnifeTool.IntersectionColor
                    : EditorStyle.KnifeTool.AnchorColor);
                Gizmos.DrawRect(point.Position, EditorStyle.Shape.AnchorSize * IntersectionAnchorScale);
            }

            Gizmos.SetColor(EditorStyle.KnifeTool.HoverColor);
            Gizmos.DrawRect(_hoverPosition, EditorStyle.Shape.AnchorSize * HoverAnchorScale);
        }
    }

    private void UpdateHoverIntersections(in Vector2 from, in Vector2 to)
    {
        _points.RemoveLast(_points.Length - _pointCount);

        if (!_hoverPositionValid)
            return;

        // Add the hover position as a non-intersection (will be deduped if on a segment intersection)
        _points.Add(new KnifePoint { Position = from, Intersection = false });

        // Find all intersections with shape segments
        for (ushort p = 0; p < _shape.PathCount; p++)
        {
            ref readonly var path = ref _shape.GetPath(p);
            for (ushort a = 0; a < path.AnchorCount; a++)
            {
                var a0Idx = (ushort)(path.AnchorStart + a);
                ref readonly var a0 = ref _shape.GetAnchor(a0Idx);
                ref readonly var a1 = ref _shape.GetNextAnchor(a0Idx);
                var samples = _shape.GetSegmentSamples(a0Idx);

                // Check from anchor0 to first sample
                if (Physics.OverlapLine(from, to, a0.Position, samples[0], out var intersection))
                    _points.Add(new KnifePoint { Position = intersection, Intersection = true });

                // Check between samples
                for (var s = 0; s < Shape.MaxSegmentSamples - 1; s++)
                {
                    if (Physics.OverlapLine(from, to, samples[s], samples[s + 1], out intersection))
                        _points.Add(new KnifePoint { Position = intersection, Intersection = true });
                }

                // Check from last sample to anchor1
                if (Physics.OverlapLine(from, to, samples[Shape.MaxSegmentSamples - 1], a1.Position, out intersection))
                    _points.Add(new KnifePoint { Position = intersection, Intersection = true });
            }
        }

        // Sort by distance from 'to' (last committed point) so intersections are in order along the line
        var hoverCount = _points.Length - _pointCount;
        if (hoverCount <= 1)
            return;

        var origin = to;
        _points.AsSpan(_pointCount, hoverCount).Sort((a, b) =>
            Vector2.DistanceSquared(origin, a.Position).CompareTo(Vector2.DistanceSquared(origin, b.Position)));

        // Remove duplicates (also check against last committed point)
        const float duplicateThreshold = 0.0001f;
        var duplicateThresholdSqr = duplicateThreshold * duplicateThreshold;
        var lastCommitted = _points[_pointCount - 1].Position;
        var writeIdx = _pointCount;

        for (var i = _pointCount; i < _points.Length; i++)
        {
            // Skip if too close to the last committed point
            if (Vector2.DistanceSquared(_points[i].Position, lastCommitted) < duplicateThresholdSqr)
                continue;

            // Check against other hover points
            var duplicateIdx = -1;
            for (var j = _pointCount; j < writeIdx; j++)
            {
                if (Vector2.DistanceSquared(_points[i].Position, _points[j].Position) < duplicateThresholdSqr)
                {
                    duplicateIdx = j;
                    break;
                }
            }

            if (duplicateIdx < 0)
            {
                if (writeIdx != i)
                    _points[writeIdx] = _points[i];
                writeIdx++;
            }
            else if (_points[i].Intersection && !_points[duplicateIdx].Intersection)
            {
                _points[duplicateIdx] = _points[i];
            }
        }

        _points.RemoveLast(_points.Length - writeIdx);
    }

    private void Finish()
    {
        Workspace.EndTool();
        Input.ConsumeButton(InputCode.MouseLeft);
        Input.ConsumeButton(InputCode.MouseRight);
    }

    [Conditional("NOZ_KNIFE_DEBUG")]
    private void LogKnife(string msg)
    {
        Log.Debug($"[KNIFE] {msg}");
    }
}
