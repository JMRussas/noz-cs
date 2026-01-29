//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

// #define NOZ_KNIFE_DEBUG

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
        public bool IsFree;
    }

    private struct KnifeSegment
    {
        public int Start;
        public int End;
    }

    private NativeArray<KnifePoint> _points = new(MaxPoints);
    private readonly SpriteEditor _editor;
    private readonly Shape _shape;
    private Vector2 _hoverPosition;
    private bool _hoverPositionValid;
    private bool _hoverIsClose;
    private bool _hoverIsIntersection;
    private bool _hoverIsFree;
    private ushort _hoverAnchorIndex = ushort.MaxValue;
    private int _pointCount;
    private Action? _commit;
    private Action? _cancel;

    public KnifeTool(SpriteEditor editor, Shape shape, Action? commit = null, Action? cancel = null)
    {
        _editor = editor;
        _shape = shape;
        _commit = commit;
        _cancel = cancel;
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
        _cancel?.Invoke();
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
        {
            LogKnife("AddPoint: hover position invalid");
            return;
        }

        LogKnife($"AddPoint: pos={_hoverPosition}, isIntersection={_hoverIsIntersection}, isClose={_hoverIsClose}, pointCount={_pointCount}");

        // First point needs to be added directly (no line to intersect yet)
        if (_pointCount == 0)
            _points.Add(new KnifePoint
            {
                Position = _hoverPosition,
                Intersection = _hoverIsIntersection
            });

        _pointCount = _points.Length;
        LogKnife($"AddPoint: new pointCount={_pointCount}, total points={_points.Length}");

        if (_hoverIsClose)
            Commit();
    }

    private void RemoveLastPoint()
    {
    }

    private int GetKnifeSegments(ref Span<KnifeSegment> segments)
    {
        // Skip all points before the first intersection
        var pointIndex = 0;
        for (;  pointIndex < _pointCount; pointIndex++)
        {
            ref var point = ref _points[pointIndex];
            if (point.Intersection) break;
        }

        var segmentCount = 0;
        while (pointIndex < _pointCount - 1)
        {
            ref var segment = ref segments[segmentCount++];
            segment.Start = pointIndex;

            var free = false;
            for (pointIndex++;  pointIndex < _pointCount; pointIndex++)
            {
                ref var point = ref _points[pointIndex];
                free |= point.IsFree;
                if (point.Intersection) break;
            }

            if (free)
            {
                segmentCount--;
                continue;
            }

            if (pointIndex >= _pointCount)
            {
                segmentCount--;
                break;
            }

            segment.End = pointIndex;
        }

        return segmentCount;
    }

    private void Commit()
    {
        LogKnife($"Commit: start with {_points.Length} points, pointCount={_pointCount}");

        _points.RemoveLast(_points.Length - _pointCount);

        if (_points.Length < 2)
        {
            LogKnife("Commit: not enough points, finishing");
            Finish();
            return;
        }

        LogKnife($"Commit: {_points.Length} points after trim");
        for (var i = 0; i < _points.Length; i++)
            LogKnife($"  Point[{i}]: pos={_points[i].Position}, intersection={_points[i].Intersection}, isFree={_points[i].IsFree}");

        Undo.Record(_editor.Document);

        Span<HitResult> headHits = stackalloc HitResult[Shape.MaxAnchors];
        Span<HitResult> tailHits = stackalloc HitResult[Shape.MaxAnchors];
        Span<KnifeSegment> knifeSegments = stackalloc KnifeSegment[MaxPoints / 2];

        var knifeSegmentCount = GetKnifeSegments(ref knifeSegments);
        var anchorHitSize = EditorStyle.Shape.AnchorHitSize / Workspace.Zoom;
        var segmentHitSize = EditorStyle.Shape.SegmentHitSize / Workspace.Zoom;

        LogKnife($"Commit: {knifeSegmentCount} knife segments, anchorHitSize={anchorHitSize}, segmentHitSize={segmentHitSize}");

        for (int knifeSegmentIndex=0; knifeSegmentIndex < knifeSegmentCount; knifeSegmentIndex++)
        {
            ref var knifeSegment = ref knifeSegments[knifeSegmentIndex];
            LogKnife($"Commit: processing knife segment {knifeSegmentIndex}: start={knifeSegment.Start}, end={knifeSegment.End}");

            Span<KnifePoint> knifePoints = _points.AsSpan(knifeSegment.Start, knifeSegment.End - knifeSegment.Start + 1);
            var intermediatePoints = knifePoints[1..^1];

            // find the shared path
            ref readonly var headPoint = ref knifePoints[0];
            ref readonly var tailPoint = ref knifePoints[^1];
            var headPos = headPoint.Position;
            var tailPos = tailPoint.Position;

            LogKnife($"Commit: headPos={headPos}, tailPos={tailPos}");

            var headCount = _shape.HitTestAll(headPos, headHits, anchorHitSize, segmentHitSize);
            var tailCount = _shape.HitTestAll(tailPos, tailHits, anchorHitSize, segmentHitSize);

            LogKnife($"Commit: headCount={headCount}, tailCount={tailCount}");
            for (var i = 0; i < headCount; i++)
                LogKnife($"  HeadHit[{i}]: path={headHits[i].PathIndex}, anchor={headHits[i].AnchorIndex}, segment={headHits[i].SegmentIndex}, anchorPos={headHits[i].AnchorPosition}, segmentPos={headHits[i].SegmentPosition}");
            for (var i = 0; i < tailCount; i++)
                LogKnife($"  TailHit[{i}]: path={tailHits[i].PathIndex}, anchor={tailHits[i].AnchorIndex}, segment={tailHits[i].SegmentIndex}, anchorPos={tailHits[i].AnchorPosition}, segmentPos={tailHits[i].SegmentPosition}");

            // todo: we should find commong with intermedite points too
            // todo: if there are non intermedia points then we check the mid point and use that for common path finding
            var commonPath = FindSharedPath(
                headHits[..headCount],
                tailHits[..tailCount],
                out var headHit,
                out var tailHit);

            LogKnife($"Commit: commonPath={commonPath}");

            if (commonPath == ushort.MaxValue)
            {
                LogKnife("Commit: no common path found, skipping");
                continue;
            }

            LogKnife($"Commit: headHit.SegmentIndex={headHit.SegmentIndex}, tailHit.SegmentIndex={tailHit.SegmentIndex}");

            if (headHit.SegmentIndex == ushort.MaxValue || tailHit.SegmentIndex == ushort.MaxValue)
            {
                LogKnife("Commit: segment index invalid, skipping");
                continue;
            }

            if (headHit.SegmentIndex == tailHit.SegmentIndex)
            {
                LogKnife("Commit: same segment - calling CutNotch");
                CutNotch(commonPath, headHit, tailHit, intermediatePoints);
            }
            else
            {
                LogKnife("Commit: different segments - calling CutPath");
                CutPath(commonPath, headHit, tailHit, intermediatePoints);
            }

            _shape.UpdateSamples();
        }

        _shape.UpdateBounds();

        LogKnife("Commit: finished");
        Finish();

        _commit?.Invoke();
    }

    private static ushort FindSharedPath(
        ReadOnlySpan<HitResult> headHits,
        ReadOnlySpan<HitResult> tailHits,
        out HitResult headHit,
        out HitResult tailHit)
    {
        // Find first matching path, but prefer hits with valid anchor indices
        for (var h = 0; h < headHits.Length; h++)
        {
            for (var t = 0; t < tailHits.Length; t++)
            {
                if (headHits[h].PathIndex != tailHits[t].PathIndex)
                    continue;

                var pathIndex = headHits[h].PathIndex;

                // Find best head hit for this path (prefer anchor hits)
                headHit = headHits[h];
                for (var h2 = h + 1; h2 < headHits.Length; h2++)
                {
                    if (headHits[h2].PathIndex == pathIndex && headHits[h2].AnchorIndex != ushort.MaxValue)
                    {
                        headHit = headHits[h2];
                        break;
                    }
                }

                // Find best tail hit for this path (prefer anchor hits)
                tailHit = tailHits[t];
                for (var t2 = t + 1; t2 < tailHits.Length; t2++)
                {
                    if (tailHits[t2].PathIndex == pathIndex && tailHits[t2].AnchorIndex != ushort.MaxValue)
                    {
                        tailHit = tailHits[t2];
                        break;
                    }
                }

                return pathIndex;
            }
        }

        headHit = default;
        tailHit = default;
        return ushort.MaxValue;
    }

    private static ushort FindSegmentForPath(ReadOnlySpan<HitResult> hits, ushort pathIndex)
    {
        for (var i = 0; i < hits.Length; i++)
        {
            if (hits[i].PathIndex == pathIndex)
                return hits[i].SegmentIndex;
        }
        return ushort.MaxValue;
    }

    private void CutNotch(
        ushort pathIndex,
        HitResult headHit,
        HitResult tailHit,
        ReadOnlySpan<KnifePoint> intermediatePoints)
    {
        LogKnife($"CutNotch: pathIndex={pathIndex}, headSegment={headHit.SegmentIndex}, tailSegment={tailHit.SegmentIndex}");
        LogKnife($"CutNotch: headPos={headHit.SegmentPosition}, tailPos={tailHit.SegmentPosition}");

        ref readonly var a = ref _shape.GetAnchor(headHit.SegmentIndex);
        ref readonly var aNext = ref _shape.GetNextAnchor(headHit.SegmentIndex);
        LogKnife($"CutNotch: segment anchor a0={a.Position}, a1={aNext.Position}");

        var headDistToA = Vector2.DistanceSquared(headHit.SegmentPosition, a.Position);
        var tailDistToA = Vector2.DistanceSquared(tailHit.SegmentPosition, a.Position);
        var reversed = headDistToA > tailDistToA;
        LogKnife($"CutNotch: headDistToA={headDistToA}, tailDistToA={tailDistToA}, reversed={reversed}");

        if (reversed)
            (headHit, tailHit) = (tailHit, headHit);

        _shape.BeginEdit();

        // Split segment at head position (preserves curve)
        LogKnife($"CutNotch: splitting at head position {headHit.SegmentPosition}");
        var currentIdx = _shape.SplitSegmentAtPoint(headHit.SegmentIndex, headHit.SegmentPosition);
        if (currentIdx == ushort.MaxValue)
        {
            LogKnife("CutNotch: head split failed!");
            _shape.EndEdit();
            return;
        }
        LogKnife($"CutNotch: head split created anchor at index {currentIdx}");
        _shape.UpdateSamples();

        // Split at tail position (preserves curve for the remaining segment)
        var headIdx = currentIdx;
        LogKnife($"CutNotch: splitting at tail position {tailHit.SegmentPosition}");
        var tailIdx = _shape.SplitSegmentAtPoint(currentIdx, tailHit.SegmentPosition);
        if (tailIdx == ushort.MaxValue)
        {
            LogKnife("CutNotch: tail split failed!");
            _shape.EndEdit();
            return;
        }
        LogKnife($"CutNotch: tail split created anchor at index {tailIdx}");

        // The segment from head to tail (or first intermediate) is the knife cut - make it straight
        _shape.SetAnchorCurve(headIdx, 0);

        // Intermediate anchors (knife line - straight, curve=0)
        Span<Vector2> intermediatePositions = stackalloc Vector2[intermediatePoints.Length];
        if (reversed)
        {
            for (var i = 0; i < intermediatePoints.Length; i++)
                intermediatePositions[i] = intermediatePoints[intermediatePoints.Length - i - 1].Position;
        }
        else
        {
            for (var i = 0; i < intermediatePoints.Length; i++)
                intermediatePositions[i] = intermediatePoints[i].Position;
        }

        LogKnife($"CutNotch: inserting {intermediatePositions.Length} intermediate anchors");
        for (var i = 0; i < intermediatePositions.Length; i++)
        {
            currentIdx = _shape.InsertAnchor(currentIdx, intermediatePositions[i]);
            if (currentIdx == ushort.MaxValue)
            {
                LogKnife($"CutNotch: intermediate insert {i} failed!");
                break;
            }
        }

        _shape.EndEdit();

        ref readonly var path = ref _shape.GetPath(pathIndex);
        LogKnife($"CutNotch: creating new path from existing path {pathIndex}");
        var newShapeIndex = _shape.AddPath(path.FillColor, opacity: path.FillOpacity, subract: path.IsSubtract);
        _shape.BeginEdit();
        _shape.AddAnchor(newShapeIndex, headHit.SegmentPosition);
        _shape.AddAnchors(newShapeIndex, intermediatePositions);
        _shape.AddAnchor(newShapeIndex, tailHit.SegmentPosition);
        _shape.EndEdit();
        LogKnife("CutNotch: done");
    }

    private void CutPath(ushort pathIndex,
        HitResult headHit,
        HitResult tailHit,
        ReadOnlySpan<KnifePoint> intermediatePoints)
    {
        ref readonly var path = ref _shape.GetPath(pathIndex);
        LogKnife($"CutPath: pathIndex={pathIndex}, pathAnchorStart={path.AnchorStart}, pathAnchorCount={path.AnchorCount}");
        LogKnife($"CutPath: headHit segment={headHit.SegmentIndex}, anchor={headHit.AnchorIndex}, pos={headHit.SegmentPosition}");
        LogKnife($"CutPath: tailHit segment={tailHit.SegmentIndex}, anchor={tailHit.AnchorIndex}, pos={tailHit.SegmentPosition}");

        var reverseIntermediates = headHit.SegmentIndex > tailHit.SegmentIndex;
        LogKnife($"CutPath: reverseIntermediates={reverseIntermediates} (head.seg={headHit.SegmentIndex} > tail.seg={tailHit.SegmentIndex})");

        if (reverseIntermediates)
        {
            (headHit, tailHit) = (tailHit, headHit);
            LogKnife("CutPath: swapped head and tail");
        }

        _shape.BeginEdit();

        // Get or create anchor at head position
        var headAnchorIndex = headHit.AnchorIndex;
        if (headAnchorIndex == ushort.MaxValue)
        {
            LogKnife($"CutPath: head has no anchor, splitting segment {headHit.SegmentIndex} at {headHit.SegmentPosition}");
            headAnchorIndex = _shape.SplitSegmentAtPoint(headHit.SegmentIndex, headHit.SegmentPosition);
            LogKnife($"CutPath: head split created anchor {headAnchorIndex}");
            if (tailHit.AnchorIndex != ushort.MaxValue) tailHit.AnchorIndex++;
            tailHit.SegmentIndex++;
            LogKnife($"CutPath: adjusted tail: anchor={tailHit.AnchorIndex}, segment={tailHit.SegmentIndex}");
            _shape.UpdateSamples();
        }
        else
        {
            LogKnife($"CutPath: head already has anchor {headAnchorIndex}");
        }

        var tailAnchorIndex = tailHit.AnchorIndex;
        if (tailAnchorIndex == ushort.MaxValue)
        {
            LogKnife($"CutPath: tail has no anchor, splitting segment {tailHit.SegmentIndex} at {tailHit.SegmentPosition}");
            tailAnchorIndex = _shape.SplitSegmentAtPoint(tailHit.SegmentIndex, tailHit.SegmentPosition);
            LogKnife($"CutPath: tail split created anchor {tailAnchorIndex}");
        }
        else
        {
            LogKnife($"CutPath: tail already has anchor {tailAnchorIndex}");
        }

        _shape.EndEdit();

        Span<Vector2> intermediatePositions = stackalloc Vector2[intermediatePoints.Length];
        for (var i = 0; i < intermediatePoints.Length; i++)
            intermediatePositions[i] = intermediatePoints[i].Position;

        LogKnife($"CutPath: calling SplitPathAtAnchors(path={pathIndex}, head={headAnchorIndex}, tail={tailAnchorIndex}, intermediates={intermediatePositions.Length}, reverse={reverseIntermediates})");
        _shape.SplitPathAtAnchors(pathIndex, headAnchorIndex, tailAnchorIndex, intermediatePositions, reverseIntermediates);
        LogKnife("CutPath: done");
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
            _hoverAnchorIndex = ushort.MaxValue;
            _hoverIsFree = result.PathIndex != ushort.MaxValue;
            if (_hoverIsClose)
            {
                _hoverPosition = _points[0].Position;
                _hoverPositionValid = true;
            }
            else if (result.AnchorIndex != ushort.MaxValue)
            {
                _hoverPosition = result.AnchorPosition;
                _hoverIsIntersection = true;
                _hoverAnchorIndex = result.AnchorIndex;
            }
            else if (result.SegmentIndex != ushort.MaxValue)
            {
                _hoverPosition = result.SegmentPosition;
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

            Gizmos.SetColor(EditorStyle.Tool.LineColor);
            for (var i = 0; i < _points.Length - 1; i++)
                Gizmos.DrawLine(_points[i].Position, _points[i + 1].Position, EditorStyle.Shape.SegmentLineWidth);

            if (_points.Length > 0)
            {
                Gizmos.SetColor(_hoverPositionValid
                    ? EditorStyle.Tool.LineColor
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

            Gizmos.SetColor(EditorStyle.Tool.PointColor);
            Gizmos.DrawCircle(_hoverPosition, EditorStyle.Shape.AnchorSize * HoverAnchorScale);
        }
    }

    private void UpdateHoverIntersections(in Vector2 from, in Vector2 to)
    {
        _points.RemoveLast(_points.Length - _pointCount);

        if (!_hoverPositionValid)
            return;

        // Add the hover position (will be deduped if on a segment intersection)
        _points.Add(new KnifePoint { Position = from, Intersection = _hoverIsIntersection, IsFree = !_hoverIsFree});

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
                    _points.Add(new KnifePoint { Position = intersection, Intersection = true});

                // Check between samples
                for (var s = 0; s < Shape.MaxSegmentSamples - 1; s++)
                {
                    if (Physics.OverlapLine(from, to, samples[s], samples[s + 1], out intersection))
                        _points.Add(new KnifePoint { Position = intersection, Intersection = true});
                }

                // Check from last sample to anchor1
                if (Physics.OverlapLine(from, to, samples[Shape.MaxSegmentSamples - 1], a1.Position, out intersection))
                    _points.Add(new KnifePoint { Position = intersection, Intersection = true});
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
        var duplicateThreshold = 0.5f / EditorApplication.Config.PixelsPerUnit;
        var duplicateThresholdSqr = duplicateThreshold * duplicateThreshold;
        var writeIdx = _pointCount;

        for (var readIdx = _pointCount; readIdx < _points.Length; readIdx++)
        {
            if (Vector2.DistanceSquared(_points[readIdx].Position, _points[writeIdx - 1].Position) < duplicateThresholdSqr)
            {
                if (_points[readIdx].Intersection)
                    _points[writeIdx] = _points[readIdx];
            }
            else
            {
                _points[writeIdx++] = _points[readIdx];
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
