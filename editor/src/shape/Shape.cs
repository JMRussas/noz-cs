//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;
using System.Runtime.InteropServices;

namespace noz.editor;

public static class ShapeConstants
{
    public const int MaxAnchors = 1024;
    public const int MaxPaths = 256;
    public const int MaxSegmentSamples = 8;
}

public struct HitResult
{
    public ushort AnchorIndex;
    public ushort SegmentIndex;
    public ushort MidpointIndex;
    public ushort PathIndex;
    public float AnchorDistSqr;
    public float SegmentDistSqr;
    public float MidpointDistSqr;

    public static HitResult Empty => new()
    {
        AnchorIndex = ushort.MaxValue,
        SegmentIndex = ushort.MaxValue,
        MidpointIndex = ushort.MaxValue,
        PathIndex = ushort.MaxValue,
        AnchorDistSqr = float.MaxValue,
        SegmentDistSqr = float.MaxValue,
        MidpointDistSqr = float.MaxValue,
    };
}

public unsafe class Shape : IDisposable
{
    private void* _memory;
    private ushort* _anchorCount;
    private ushort* _pathCount;
    private Rect* _bounds;
    private RectInt* _rasterBounds;
    private Anchor* _anchors;
    private Vector2* _samples;
    private Path* _paths;

    public ushort AnchorCount => *_anchorCount;
    public ushort PathCount => *_pathCount;
    public Rect Bounds => *_bounds;
    public RectInt RasterBounds => *_rasterBounds;
    
    [Flags]
    public enum AnchorFlags : ushort
    {
        None = 0,
        Selected = 1 << 0,
    }

    [Flags]
    public enum PathFlags : ushort
    {
        None = 0,
        Selected = 1 << 0,
    }
    
    public struct Anchor
    {
        public Vector2 Position;
        public float Curve;
        public AnchorFlags Flags;
        public Vector2 Midpoint;
    }

    public struct Path
    {
        public ushort AnchorStart;
        public ushort AnchorCount;
        public byte StrokeColor;
        public byte FillColor;
        public PathFlags Flags;
    }
    
    public Shape()
    {
        var totalSize =
            sizeof(ushort) +
            sizeof(ushort) +
            sizeof(Rect) +
            sizeof(RectInt) +
            sizeof(Anchor) * ShapeConstants.MaxAnchors +
            sizeof(Vector2) * ShapeConstants.MaxAnchors * ShapeConstants.MaxSegmentSamples +
            sizeof(Path) * ShapeConstants.MaxPaths;
        
        _memory = NativeMemory.AllocZeroed((nuint)totalSize);

        var current = (byte*)_memory;
        _anchorCount = (ushort*)current; current += sizeof(ushort);
        _pathCount = (ushort*)current; current += sizeof(ushort);
        _bounds = (Rect*)current; current += sizeof(Rect);
        _rasterBounds = (RectInt*)current; current += sizeof(RectInt);
        _anchors = (Anchor*)current; current += sizeof(Anchor) * ShapeConstants.MaxAnchors;
        _samples = (Vector2*)current; current += sizeof(Vector2) * ShapeConstants.MaxAnchors * ShapeConstants.MaxSegmentSamples;
        _paths = (Path*)current;
    }

    ~Shape()
    {
        Dispose(false);
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_memory is null) 
            return;
        
        NativeMemory.Free(_memory);
        _memory = null;
    }

    public void UpdateSamples()
    {
        for (ushort p = 0; p < *_pathCount; p++)
        {
            ref var path = ref _paths[p];
            for (ushort a = 0; a < path.AnchorCount; a++)
            {
                UpdateSamples(p, a);
            }
        }
    }

    public void UpdateSamples(ushort pathIndex, ushort anchorIndex)
    {
        ref var path = ref _paths[pathIndex];
        var a0Index = path.AnchorStart + anchorIndex;
        var a1Index = path.AnchorStart + ((anchorIndex + 1) % path.AnchorCount);

        ref var a0 = ref _anchors[a0Index];
        ref var a1 = ref _anchors[a1Index];

        var p0 = a0.Position;
        var p1 = a1.Position;
        
        var samples = &(_samples[a0Index * ShapeConstants.MaxSegmentSamples]);

        if (MathF.Abs(a0.Curve) < 0.0001f)
        {
            // Linear interpolation
            for (var i = 0; i < ShapeConstants.MaxSegmentSamples; i++)
            {
                var t = (i + 1) / (float)(ShapeConstants.MaxSegmentSamples + 1);
                samples[i] = Vector2.Lerp(p0, p1, t);
            }
            a0.Midpoint = Vector2.Lerp(p0, p1, 0.5f);
        }
        else
        {
            // Quadratic Bézier curve
            var mid = (p0 + p1) * 0.5f;
            var dir = p1 - p0;
            var perp = new Vector2(-dir.Y, dir.X);
            perp = Vector2.Normalize(perp);
            var cp = mid + perp * a0.Curve;

            for (var i = 0; i < ShapeConstants.MaxSegmentSamples; i++)
            {
                var t = (i + 1) / (float)(ShapeConstants.MaxSegmentSamples + 1);
                var oneMinusT = 1f - t;
                samples[i] = oneMinusT * oneMinusT * p0 + 2f * oneMinusT * t * cp + t * t * p1;
            }

            // Midpoint at t=0.5
            a0.Midpoint = 0.25f * p0 + 0.5f * cp + 0.25f * p1;
        }
    }

    public void UpdateBounds()
    {
        if (*_anchorCount == 0)
        {
            *_bounds = Rect.Zero;
            *_rasterBounds = RectInt.Zero;
            return;
        }

        var min = new Vector2(float.MaxValue, float.MaxValue);
        var max = new Vector2(float.MinValue, float.MinValue);

        for (ushort p = 0; p < *_pathCount; p++)
        {
            ref var path = ref _paths[p];
            for (ushort a = 0; a < path.AnchorCount; a++)
            {
                var anchorIdx = path.AnchorStart + a;
                ref var anchor = ref _anchors[anchorIdx];

                min = Vector2.Min(min, anchor.Position);
                max = Vector2.Max(max, anchor.Position);

                if (MathF.Abs(anchor.Curve) > 0.0001f)
                {
                    var samples = &(_samples[anchorIdx * ShapeConstants.MaxSegmentSamples]);
                    for (var s = 0; s < ShapeConstants.MaxSegmentSamples; s++)
                    {
                        min = Vector2.Min(min, samples[s]);
                        max = Vector2.Max(max, samples[s]);
                    }
                }
            }
        }

        *_bounds = Rect.FromMinMax(min, max);
        *_rasterBounds = new RectInt(
            (int)MathF.Floor(min.X),
            (int)MathF.Floor(min.Y),
            (int)MathF.Ceiling(max.X - min.X),
            (int)MathF.Ceiling(max.Y - min.Y)
        );
    }

    public HitResult HitTest(Vector2 point, float anchorRadius = 5f, float segmentRadius = 3f)
    {
        var result = HitResult.Empty;
        var anchorRadiusSqr = anchorRadius * anchorRadius;
        var segmentRadiusSqr = segmentRadius * segmentRadius;

        for (ushort p = 0; p < *_pathCount; p++)
        {
            ref var path = ref _paths[p];

            // Test anchors
            for (ushort a = 0; a < path.AnchorCount; a++)
            {
                var anchorIdx = path.AnchorStart + a;
                ref var anchor = ref _anchors[anchorIdx];

                var distSqr = Vector2.DistanceSquared(point, anchor.Position);
                if (distSqr < anchorRadiusSqr && distSqr < result.AnchorDistSqr)
                {
                    result.AnchorIndex = (ushort)anchorIdx;
                    result.AnchorDistSqr = distSqr;
                    result.PathIndex = p;
                }
            }

            // Test midpoints
            for (ushort a = 0; a < path.AnchorCount; a++)
            {
                var anchorIdx = path.AnchorStart + a;
                ref var anchor = ref _anchors[anchorIdx];

                var distSqr = Vector2.DistanceSquared(point, anchor.Midpoint);
                if (distSqr < anchorRadiusSqr && distSqr < result.MidpointDistSqr)
                {
                    result.MidpointIndex = (ushort)anchorIdx;
                    result.MidpointDistSqr = distSqr;
                    if (result.PathIndex == ushort.MaxValue)
                        result.PathIndex = p;
                }
            }

            // Test segments
            for (ushort a = 0; a < path.AnchorCount; a++)
            {
                var a0Idx = path.AnchorStart + a;
                var a1Idx = path.AnchorStart + ((a + 1) % path.AnchorCount);
                ref var a0 = ref _anchors[a0Idx];
                ref var a1 = ref _anchors[a1Idx];
                var samples = &(_samples[a0Idx * ShapeConstants.MaxSegmentSamples]);

                // Test against segment from a0 to first sample, samples, and last sample to a1
                var distSqr = PointToSegmentDistSqr(point, a0.Position, samples[0]);
                for (var s = 0; s < ShapeConstants.MaxSegmentSamples - 1; s++)
                {
                    distSqr = MathF.Min(distSqr, PointToSegmentDistSqr(point, samples[s], samples[s + 1]));
                }
                distSqr = MathF.Min(distSqr, PointToSegmentDistSqr(point, samples[ShapeConstants.MaxSegmentSamples - 1], a1.Position));

                if (distSqr < segmentRadiusSqr && distSqr < result.SegmentDistSqr)
                {
                    result.SegmentIndex = (ushort)a0Idx;
                    result.SegmentDistSqr = distSqr;
                    if (result.PathIndex == ushort.MaxValue)
                        result.PathIndex = p;
                }
            }

            // Test filled area (if no anchor/segment hit)
            if (result.AnchorIndex == ushort.MaxValue && result.SegmentIndex == ushort.MaxValue)
            {
                if (IsPointInPath(point, p))
                {
                    result.PathIndex = p;
                }
            }
        }

        return result;
    }

    public void ClearSelection()
    {
        for (var i = 0; i < *_anchorCount; i++)
            _anchors[i].Flags &= ~AnchorFlags.Selected;

        for (var i = 0; i < *_pathCount; i++)
            _paths[i].Flags &= ~PathFlags.Selected;
    }

    public ushort InsertAnchor(ushort afterAnchorIndex, Vector2 position, float curve = 0f)
    {
        if (*_anchorCount >= ShapeConstants.MaxAnchors) return ushort.MaxValue;

        // Find which path this anchor belongs to
        ushort pathIndex = ushort.MaxValue;
        ushort localIndex = 0;
        for (ushort p = 0; p < *_pathCount; p++)
        {
            ref var path = ref _paths[p];
            if (afterAnchorIndex >= path.AnchorStart && afterAnchorIndex < path.AnchorStart + path.AnchorCount)
            {
                pathIndex = p;
                localIndex = (ushort)(afterAnchorIndex - path.AnchorStart + 1);
                break;
            }
        }

        if (pathIndex == ushort.MaxValue) return ushort.MaxValue;

        var insertIndex = (ushort)(_paths[pathIndex].AnchorStart + localIndex);

        // Shift anchors after insert point
        for (var i = *_anchorCount; i > insertIndex; i--)
            _anchors[i] = _anchors[i - 1];

        // Insert new anchor
        _anchors[insertIndex] = new Anchor
        {
            Position = position,
            Curve = curve,
            Flags = AnchorFlags.None,
        };

        (*_anchorCount)++;
        _paths[pathIndex].AnchorCount++;

        // Update anchor_start for paths after this one
        for (var p = pathIndex + 1; p < *_pathCount; p++)
            _paths[p].AnchorStart++;

        UpdateSamples(pathIndex, (ushort)(localIndex > 0 ? localIndex - 1 : _paths[pathIndex].AnchorCount - 1));
        UpdateSamples(pathIndex, localIndex);
        UpdateBounds();

        return insertIndex;
    }

    public void SplitSegment(ushort anchorIndex)
    {
        // Find path containing this anchor
        ushort pathIndex = ushort.MaxValue;
        for (ushort p = 0; p < *_pathCount; p++)
        {
            ref var path = ref _paths[p];
            if (anchorIndex >= path.AnchorStart && anchorIndex < path.AnchorStart + path.AnchorCount)
            {
                pathIndex = p;
                break;
            }
        }

        if (pathIndex == ushort.MaxValue) return;

        ref var anchor = ref _anchors[anchorIndex];
        var midpoint = anchor.Midpoint;

        // Calculate new curve values for split segments
        var newCurve = anchor.Curve * 0.5f;

        InsertAnchor(anchorIndex, midpoint, newCurve);
        anchor.Curve = newCurve;

        UpdateSamples();
        UpdateBounds();
    }

    public void DeleteSelectedAnchors()
    {
        for (ushort p = 0; p < *_pathCount; p++)
        {
            ref var path = ref _paths[p];
            var writeIdx = path.AnchorStart;

            for (var a = 0; a < path.AnchorCount; a++)
            {
                var readIdx = path.AnchorStart + a;
                if ((_anchors[readIdx].Flags & AnchorFlags.Selected) == 0)
                {
                    if (writeIdx != readIdx)
                        _anchors[writeIdx] = _anchors[readIdx];
                    writeIdx++;
                }
            }

            var removed = path.AnchorCount - (writeIdx - path.AnchorStart);
            path.AnchorCount = (ushort)(writeIdx - path.AnchorStart);
            *_anchorCount -= (ushort)removed;

            // Update anchor_start for subsequent paths
            for (var np = p + 1; np < *_pathCount; np++)
                _paths[np].AnchorStart -= (ushort)removed;
        }

        // Remove empty paths
        var pathWrite = 0;
        for (var p = 0; p < *_pathCount; p++)
        {
            if (_paths[p].AnchorCount > 0)
            {
                if (pathWrite != p)
                    _paths[pathWrite] = _paths[p];
                pathWrite++;
            }
        }
        *_pathCount = (ushort)pathWrite;

        UpdateSamples();
        UpdateBounds();
    }

    public Path GetPath(ushort pathIndex)
    {
        return _paths[pathIndex];
    }

    public Anchor GetAnchor(ushort anchorIndex)
    {
        return _anchors[anchorIndex];
    }

    public void SetPathFillColor(ushort pathIndex, byte fillColor)
    {
        _paths[pathIndex].FillColor = fillColor;
    }

    public void SetPathStrokeColor(ushort pathIndex, byte strokeColor)
    {
        _paths[pathIndex].StrokeColor = strokeColor;
    }

    public ushort AddPath(byte fillColor = 0, byte strokeColor = 0)
    {
        if (*_pathCount >= ShapeConstants.MaxPaths) return ushort.MaxValue;

        var pathIndex = (*_pathCount)++;
        _paths[pathIndex] = new Path
        {
            AnchorStart = *_anchorCount,
            AnchorCount = 0,
            FillColor = fillColor,
            StrokeColor = strokeColor,
            Flags = PathFlags.None,
        };

        return pathIndex;
    }

    public ushort AddAnchorToPath(ushort pathIndex, Vector2 position, float curve = 0f)
    {
        if (pathIndex >= *_pathCount || *_anchorCount >= ShapeConstants.MaxAnchors) return ushort.MaxValue;

        ref var path = ref _paths[pathIndex];
        var anchorIndex = (ushort)(path.AnchorStart + path.AnchorCount);

        // If this isn't the last path, we need to shift anchors
        if (pathIndex < *_pathCount - 1)
        {
            for (var i = *_anchorCount; i > anchorIndex; i--)
                _anchors[i] = _anchors[i - 1];

            for (var p = pathIndex + 1; p < *_pathCount; p++)
                _paths[p].AnchorStart++;
        }

        _anchors[anchorIndex] = new Anchor
        {
            Position = position,
            Curve = curve,
            Flags = AnchorFlags.None,
        };

        path.AnchorCount++;
        (*_anchorCount)++;

        if (path.AnchorCount > 1)
        {
            UpdateSamples(pathIndex, (ushort)(path.AnchorCount - 2));
            UpdateSamples(pathIndex, (ushort)(path.AnchorCount - 1));
        }

        UpdateBounds();
        return anchorIndex;
    }

    private bool IsPointInPath(Vector2 point, ushort pathIndex)
    {
        ref var path = ref _paths[pathIndex];
        if (path.AnchorCount < 3) return false;

        // Build vertex list from anchors and samples
        var verts = new List<Vector2>();
        for (var a = 0; a < path.AnchorCount; a++)
        {
            var anchorIdx = path.AnchorStart + a;
            ref var anchor = ref _anchors[anchorIdx];

            verts.Add(anchor.Position);

            if (MathF.Abs(anchor.Curve) > 0.0001f)
            {
                var samples = &(_samples[anchorIdx * ShapeConstants.MaxSegmentSamples]);
                for (var s = 0; s < ShapeConstants.MaxSegmentSamples; s++)
                    verts.Add(samples[s]);
            }
        }

        return IsPointInPolygon(point, verts);
    }

    private static bool IsPointInPolygon(Vector2 point, List<Vector2> verts)
    {
        var winding = 0;
        var count = verts.Count;

        for (var i = 0; i < count; i++)
        {
            var j = (i + 1) % count;
            var p0 = verts[i];
            var p1 = verts[j];

            if (p0.Y <= point.Y)
            {
                if (p1.Y > point.Y)
                {
                    var cross = (p1.X - p0.X) * (point.Y - p0.Y) - (point.X - p0.X) * (p1.Y - p0.Y);
                    if (cross >= 0) winding++;
                }
            }
            else if (p1.Y <= point.Y)
            {
                var cross = (p1.X - p0.X) * (point.Y - p0.Y) - (point.X - p0.X) * (p1.Y - p0.Y);
                if (cross < 0) winding--;
            }
        }

        return winding != 0;
    }

    private static float PointToSegmentDistSqr(Vector2 point, Vector2 a, Vector2 b)
    {
        var ab = b - a;
        var ap = point - a;
        var t = Vector2.Dot(ap, ab) / Vector2.Dot(ab, ab);
        t = MathF.Max(0, MathF.Min(1, t));
        var closest = a + ab * t;
        return Vector2.DistanceSquared(point, closest);
    }

    public void SetAnchorSelected(ushort anchorIndex, bool selected)
    {
        if (anchorIndex >= *_anchorCount)
            return;

        if (selected)
            _anchors[anchorIndex].Flags |= AnchorFlags.Selected;
        else
            _anchors[anchorIndex].Flags &= ~AnchorFlags.Selected;
    }

    public void SetAnchorCurve(ushort anchorIndex, float curve)
    {
        if (anchorIndex >= *_anchorCount)
            return;

        _anchors[anchorIndex].Curve = curve;
    }

    public void MoveSelectedAnchors(Vector2 delta, Vector2[] savedPositions, bool snap)
    {
        for (ushort i = 0; i < *_anchorCount; i++)
        {
            if ((_anchors[i].Flags & AnchorFlags.Selected) == 0)
                continue;

            var newPos = savedPositions[i] + delta;
            if (snap)
            {
                newPos.X = MathF.Round(newPos.X);
                newPos.Y = MathF.Round(newPos.Y);
            }
            _anchors[i].Position = newPos;
        }
    }

    public void RestoreAnchorPositions(Vector2[] savedPositions)
    {
        for (ushort i = 0; i < *_anchorCount; i++)
        {
            if ((_anchors[i].Flags & AnchorFlags.Selected) != 0)
                _anchors[i].Position = savedPositions[i];
        }
    }

    public void RestoreAnchorCurves(float[] savedCurves)
    {
        for (ushort i = 0; i < *_anchorCount; i++)
            _anchors[i].Curve = savedCurves[i];
    }

    public void SelectAnchorsInRect(Rect rect)
    {
        for (ushort i = 0; i < *_anchorCount; i++)
        {
            if (rect.Contains(_anchors[i].Position))
                _anchors[i].Flags |= AnchorFlags.Selected;
        }
    }

    public Vector2[] GetSegmentSamples(ushort anchorIndex)
    {
        var result = new Vector2[ShapeConstants.MaxSegmentSamples];
        var samples = &(_samples[anchorIndex * ShapeConstants.MaxSegmentSamples]);
        for (var i = 0; i < ShapeConstants.MaxSegmentSamples; i++)
            result[i] = samples[i];
        return result;
    }
}

