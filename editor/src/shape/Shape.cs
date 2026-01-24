//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace NoZ.Editor;

public struct HitResult
{
    public ushort AnchorIndex;
    public ushort SegmentIndex;
    public ushort PathIndex;
    public float AnchorDistSqr;
    public float SegmentDistSqr;
    public Vector2 AnchorPoint;
    public Vector2 SegmentPoint;

    public static HitResult Empty => new()
    {
        AnchorIndex = ushort.MaxValue,
        SegmentIndex = ushort.MaxValue,
        PathIndex = ushort.MaxValue,
        AnchorDistSqr = float.MaxValue,
        SegmentDistSqr = float.MaxValue,
    };
}

public sealed unsafe partial class Shape : IDisposable
{
    public const int MaxAnchors = 1024;
    public const int MaxPaths = 256;
    public const int MaxSegmentSamples = 8;
    
    private void* _memory;
    private readonly UnsafeSpan<Anchor> _anchors;
    private readonly UnsafeSpan<Vector2> _samples;
    private readonly UnsafeSpan<Path> _paths;

    public ushort AnchorCount { get; private set; }
    public ushort PathCount { get; private set; }
    public Rect Bounds { get; private set; }
    public RectInt RasterBounds { get; private set; }

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
        Selected = 1 << 0
    }

    public struct Anchor
    {
        public Vector2 Position;
        public float Curve;
        public AnchorFlags Flags;
        public ushort Path;

        public bool IsSelected => (Flags & AnchorFlags.Selected) != 0;
    }

    public struct Path
    {
        public ushort AnchorStart;
        public ushort AnchorCount;
        public byte FillColor;
        public PathFlags Flags;

        public bool IsSelected => (Flags & PathFlags.Selected) != 0;

        public static Path CreateDefault() => new() { };
    }

    public Shape()
    {
        var totalSize =
            sizeof(Anchor) * MaxAnchors +
            sizeof(Vector2) * MaxAnchors * MaxSegmentSamples +
            sizeof(Path) * MaxPaths;

        _memory = NativeMemory.AllocZeroed((nuint)totalSize);

        var current = (byte*)_memory;
        _anchors = new UnsafeSpan<Anchor>(ref current, MaxAnchors);
        _samples = new UnsafeSpan<Vector2>(ref current, MaxAnchors * MaxSegmentSamples);
        _paths = new UnsafeSpan<Path>(ref current, MaxPaths);
    }

    ~Shape()
    {
        DisposeInternal();
    }

    public void Dispose()
    {
        DisposeInternal();
        GC.SuppressFinalize(this);
    }

    private void DisposeInternal()
    {
        if (_memory is null)
            return;

        NativeMemory.Free(_memory);
        _memory = null;
    }

    public void UpdateSamples()
    {
        for (ushort p = 0; p < PathCount; p++)
        {
            ref var path = ref _paths[p];
            for (ushort a = 0; a < path.AnchorCount; a++)
                UpdateSamples(p, a);
        }
    }

    public void UpdateSamples(ushort pathIndex, ushort anchorIndex)
    {
        ref var path = ref _paths[pathIndex];
        var a0Index = (ushort)(path.AnchorStart + anchorIndex);
        var a1Index = (ushort)(path.AnchorStart + ((anchorIndex + 1) % path.AnchorCount));

        ref var a0 = ref _anchors[a0Index];
        ref var a1 = ref _anchors[a1Index];

        var p0 = a0.Position;
        var p1 = a1.Position;

        var samples = GetSegmentSamples(a0Index);

        if (MathF.Abs(a0.Curve) < 0.0001f)
        {
            for (var i = 0; i < MaxSegmentSamples; i++)
            {
                var t = (i + 1) / (float)(MaxSegmentSamples + 1);
                samples[i] = Vector2.Lerp(p0, p1, t);
            }
        }
        else
        {
            var mid = (p0 + p1) * 0.5f;
            var dir = p1 - p0;
            var perp = new Vector2(-dir.Y, dir.X);
            perp = Vector2.Normalize(perp);
            var cp = mid + perp * a0.Curve;

            for (var i = 0; i < MaxSegmentSamples; i++)
            {
                var t = (i + 1) / (float)(MaxSegmentSamples + 1);
                var oneMinusT = 1f - t;
                samples[i] = oneMinusT * oneMinusT * p0 + 2f * oneMinusT * t * cp + t * t * p1;
            }
        }
    }

    public void UpdateBounds()
    {
        if (AnchorCount == 0)
        {
            Bounds = Rect.Zero;
            RasterBounds = RectInt.Zero;
            return;
        }

        var dpi = EditorApplication.Config.PixelsPerUnit;
        var min = new Vector2(float.MaxValue, float.MaxValue);
        var max = new Vector2(float.MinValue, float.MinValue);

        for (ushort p = 0; p < PathCount; p++)
        {
            ref var path = ref _paths[p];

            for (ushort a = 0; a < path.AnchorCount; a++)
            {
                var anchorIdx = (ushort)(path.AnchorStart + a);
                ref var anchor = ref _anchors[anchorIdx];

                var worldPos = anchor.Position;
                min = Vector2.Min(min, worldPos);
                max = Vector2.Max(max, worldPos);

                if (MathF.Abs(anchor.Curve) > 0.0001f)
                {
                    var samples = GetSegmentSamples(anchorIdx);
                    for (var s = 0; s < MaxSegmentSamples; s++)
                    {
                        var sampleWorld = samples[s];
                        min = Vector2.Min(min, sampleWorld);
                        max = Vector2.Max(max, sampleWorld);
                    }
                }
            }
        }

        Bounds = Rect.FromMinMax(min, max);

        var xMin = (int)MathF.Floor(min.X * dpi + 0.001f);
        var yMin = (int)MathF.Floor(min.Y * dpi + 0.001f);
        var xMax = (int)MathF.Ceiling(max.X * dpi - 0.001f);
        var yMax = (int)MathF.Ceiling(max.Y * dpi - 0.001f);

        RasterBounds = new RectInt(xMin, yMin, xMax - xMin, yMax - yMin);
    }

    public HitResult HitTest(Vector2 point, float anchorRadius = 5f, float segmentRadius = 3f)
    {
        var result = HitResult.Empty;
        var anchorRadiusSqr = anchorRadius * anchorRadius;
        var segmentRadiusSqr = segmentRadius * segmentRadius;

        // Track the topmost path that contains the point (higher index = drawn on top)
        var topmostContainingPath = ushort.MaxValue;

        for (ushort p = 0; p < PathCount; p++)
        {
            ref var path = ref _paths[p];
                
            for (ushort a = 0; a < path.AnchorCount; a++)
            {
                var anchorIdx = path.AnchorStart + a;
                ref var anchor = ref _anchors[anchorIdx];

                var worldPos = anchor.Position;
                var distSqr = Vector2.DistanceSquared(point, worldPos);
                if (distSqr >= anchorRadiusSqr || distSqr >= result.AnchorDistSqr) continue;
                result.AnchorIndex = (ushort)anchorIdx;
                result.AnchorDistSqr = distSqr;
                result.AnchorPoint = worldPos;
                result.PathIndex = p;
            }

            for (ushort a = 0; a < path.AnchorCount; a++)
            {
                var a0Idx = (ushort)(path.AnchorStart + a);
                var a1Idx = (ushort)(path.AnchorStart + ((a + 1) % path.AnchorCount));
                ref var a0 = ref _anchors[a0Idx];
                ref var a1 = ref _anchors[a1Idx];
                var samples = GetSegmentSamples(a0Idx);

                var a0World = a0.Position;
                var a1World = a1.Position;
                var sample0World = samples[0];

                var bestDistSqr = PointToSegmentDistSqr(point, a0World, sample0World, out var bestClosest);
                for (var s = 0; s < MaxSegmentSamples - 1; s++)
                {
                    var sWorld = samples[s];
                    var sNextWorld = samples[s + 1];
                    var distSqr = PointToSegmentDistSqr(point, sWorld, sNextWorld, out var closest);
                    if (distSqr < bestDistSqr)
                    {
                        bestDistSqr = distSqr;
                        bestClosest = closest;
                    }
                }
                var lastSampleWorld = samples[MaxSegmentSamples - 1];
                var lastDistSqr = PointToSegmentDistSqr(point, lastSampleWorld, a1World, out var lastClosest);
                if (lastDistSqr < bestDistSqr)
                {
                    bestDistSqr = lastDistSqr;
                    bestClosest = lastClosest;
                }

                if (bestDistSqr >= segmentRadiusSqr || bestDistSqr >= result.SegmentDistSqr) continue;

                result.SegmentIndex = a0Idx;
                result.SegmentDistSqr = bestDistSqr;
                result.SegmentPoint = bestClosest;
                if (result.PathIndex == ushort.MaxValue)
                    result.PathIndex = p;
            }

            // Track point-in-path, keeping the topmost (last/highest index)
            if (IsPointInPath(point, p))
                topmostContainingPath = p;
        }

        // If no anchor/segment was hit, use the topmost containing path
        if (result.PathIndex == ushort.MaxValue)
            result.PathIndex = topmostContainingPath;

        return result;
    }

    public void ClearSelection()
    {
        for (var i = 0; i < AnchorCount; i++)
            _anchors[i].Flags &= ~AnchorFlags.Selected;

        for (var i = 0; i < PathCount; i++)
            _paths[i].Flags &= ~PathFlags.Selected;
    }

    public void ClearAnchorSelection()
    {
        for (var i = 0; i < AnchorCount; i++)
            _anchors[i].Flags &= ~AnchorFlags.Selected;
    }

    public void ClearPathSelection()
    {
        for (var i = 0; i < PathCount; i++)
            _paths[i].Flags &= ~PathFlags.Selected;
    }

    public void SetPathSelected(ushort pathIndex, bool selected)
    {
        if (pathIndex >= PathCount)
            return;

        if (selected)
            _paths[pathIndex].Flags |= PathFlags.Selected;
        else
            _paths[pathIndex].Flags &= ~PathFlags.Selected;
    }

    public bool IsPathSelected(ushort pathIndex) =>
        pathIndex < PathCount && _paths[pathIndex].IsSelected;

    public bool HasSelectedPaths()
    {
        for (ushort i = 0; i < PathCount; i++)
            if (_paths[i].IsSelected)
                return true;
        return false;
    }

    public Vector2? GetSelectedAnchorsCentroid()
    {
        var sum = Vector2.Zero;
        var count = 0;

        for (ushort i = 0; i < AnchorCount; i++)
        {
            if (!_anchors[i].IsSelected)
                continue;

            var pathIndex = _anchors[i].Path;
            sum += _anchors[i].Position;
            count++;
        }

        return count > 0 ? sum / count : null;
    }

    public void SelectAnchors(Rect rect)
    {
        for (ushort p = 0; p < PathCount; p++)
        {
            ref var path = ref _paths[p];
            for (ushort a = 0; a < path.AnchorCount; a++)
            {
                var anchorIdx = (ushort)(path.AnchorStart + a);
                if (rect.Contains(_anchors[anchorIdx].Position))
                    _anchors[anchorIdx].Flags |= AnchorFlags.Selected;
            }
        }
    }

    public ushort InsertAnchor(ushort afterAnchorIndex, Vector2 position, float curve = 0f)
    {
        if (AnchorCount >= MaxAnchors) return ushort.MaxValue;

        ushort pathIndex = ushort.MaxValue;
        ushort localIndex = 0;
        for (ushort p = 0; p < PathCount; p++)
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

        for (var i = AnchorCount; i > insertIndex; i--)
            _anchors[i] = _anchors[i - 1];

        _anchors[insertIndex] = new Anchor
        {
            Position = position,
            Curve = curve,
            Flags = AnchorFlags.None,
            Path = pathIndex
        };

        AnchorCount++;
        _paths[pathIndex].AnchorCount++;

        for (var p = pathIndex + 1; p < PathCount; p++)
            _paths[p].AnchorStart++;

        UpdateSamples(pathIndex, (ushort)(localIndex > 0 ? localIndex - 1 : _paths[pathIndex].AnchorCount - 1));
        UpdateSamples(pathIndex, localIndex);
        UpdateBounds();

        return insertIndex;
    }

    public void SplitSegment(ushort anchorIndex)
    {
        ushort pathIndex = ushort.MaxValue;
        for (ushort p = 0; p < PathCount; p++)
        {
            ref var path = ref _paths[p];
            if (anchorIndex >= path.AnchorStart && anchorIndex < path.AnchorStart + path.AnchorCount)
            {
                pathIndex = p;
                break;
            }
        }

        if (pathIndex == ushort.MaxValue) return;

        ref var pathRef = ref _paths[pathIndex];
        var nextLocalIdx = (anchorIndex - pathRef.AnchorStart + 1) % pathRef.AnchorCount;
        var nextAnchorIdx = (ushort)(pathRef.AnchorStart + nextLocalIdx);

        ref var a0 = ref _anchors[anchorIndex];
        ref var a1 = ref _anchors[nextAnchorIdx];

        var p0 = a0.Position;
        var p1 = a1.Position;
        var mid = (p0 + p1) * 0.5f;
        var dir = p1 - p0;
        var dirLen = dir.Length();

        if (dirLen < 0.0001f)
            return;

        var perp = new Vector2(-dir.Y, dir.X) / dirLen;
        var midpoint = mid + perp * (a0.Curve * 0.5f);
        var newCurve = a0.Curve * 0.5f;

        a0.Curve = newCurve;
        InsertAnchor(anchorIndex, midpoint, newCurve);

        UpdateSamples();
        UpdateBounds();
    }

    public void SplitSegmentAtPoint(ushort anchorIndex, Vector2 targetPoint)
    {
        ushort pathIndex = ushort.MaxValue;
        for (ushort p = 0; p < PathCount; p++)
        {
            ref var path = ref _paths[p];
            if (anchorIndex >= path.AnchorStart && anchorIndex < path.AnchorStart + path.AnchorCount)
            {
                pathIndex = p;
                break;
            }
        }

        if (pathIndex == ushort.MaxValue) return;

        ref var pathRef = ref _paths[pathIndex];
        var nextLocalIdx = (anchorIndex - pathRef.AnchorStart + 1) % pathRef.AnchorCount;
        var nextAnchorIdx = (ushort)(pathRef.AnchorStart + nextLocalIdx);

        ref var a0 = ref _anchors[anchorIndex];
        ref var a1 = ref _anchors[nextAnchorIdx];

        var p0 = a0.Position;
        var p1 = a1.Position;
        var curve = a0.Curve;

        var mid = (p0 + p1) * 0.5f;
        var dir = p1 - p0;
        var dirLen = dir.Length();

        if (dirLen < 0.0001f)
            return;

        var perp = new Vector2(-dir.Y, dir.X) / dirLen;
        var cp = mid + perp * curve;

        // Find closest t on the bezier to targetPoint using samples
        var bestT = 0.5f;
        var bestDistSq = float.MaxValue;

        var samples = GetSegmentSamples(anchorIndex);
        for (var i = 0; i < MaxSegmentSamples; i++)
        {
            var t = (i + 1) / (float)(MaxSegmentSamples + 1);
            var distSq = Vector2.DistanceSquared(samples[i], targetPoint);
            if (distSq < bestDistSq)
            {
                bestDistSq = distSq;
                bestT = t;
            }
        }

        // Also check near endpoints
        if (Vector2.DistanceSquared(p0, targetPoint) < bestDistSq)
            bestT = 0.1f;
        else if (Vector2.DistanceSquared(p1, targetPoint) < bestDistSq)
            bestT = 0.9f;

        // Clamp t to avoid inserting too close to existing anchors
        bestT = Math.Clamp(bestT, 0.1f, 0.9f);

        // Calculate split point using bezier formula
        var oneMinusT = 1f - bestT;
        var splitPoint = oneMinusT * oneMinusT * p0 + 2f * oneMinusT * bestT * cp + bestT * bestT * p1;

        // Calculate new control points for each half (de Casteljau)
        var cp1 = oneMinusT * p0 + bestT * cp;
        var cp2 = oneMinusT * cp + bestT * p1;

        // Calculate new curve values for first segment (p0 to splitPoint)
        var mid1 = (p0 + splitPoint) * 0.5f;
        var dir1 = splitPoint - p0;
        var dir1Len = dir1.Length();
        var newCurve1 = 0f;
        if (dir1Len > 0.0001f)
        {
            var perp1 = new Vector2(-dir1.Y, dir1.X) / dir1Len;
            newCurve1 = Vector2.Dot(cp1 - mid1, perp1);
        }

        // Calculate new curve values for second segment (splitPoint to p1)
        var mid2 = (splitPoint + p1) * 0.5f;
        var dir2 = p1 - splitPoint;
        var dir2Len = dir2.Length();
        var newCurve2 = 0f;
        if (dir2Len > 0.0001f)
        {
            var perp2 = new Vector2(-dir2.Y, dir2.X) / dir2Len;
            newCurve2 = Vector2.Dot(cp2 - mid2, perp2);
        }

        a0.Curve = newCurve1;
        InsertAnchor(anchorIndex, splitPoint, newCurve2);

        UpdateSamples();
        UpdateBounds();
    }

    public void DeleteAnchors()
    {
        var totalRemoved = 0;

        for (ushort p = 0; p < PathCount; p++)
        {
            ref var path = ref _paths[p];
            var originalStart = path.AnchorStart;
            var writeIdx = originalStart - totalRemoved;

            for (var a = 0; a < path.AnchorCount; a++)
            {
                var readIdx = originalStart + a;
                if ((_anchors[readIdx].Flags & AnchorFlags.Selected) == 0)
                {
                    _anchors[writeIdx] = _anchors[readIdx];
                    writeIdx++;
                }
            }

            var newAnchorCount = writeIdx - (originalStart - totalRemoved);
            var removed = path.AnchorCount - newAnchorCount;

            path.AnchorStart = (ushort)(originalStart - totalRemoved);
            path.AnchorCount = (ushort)newAnchorCount;

            totalRemoved += removed;
        }

        var anchorWrite = 0;
        var pathWrite = 0;

        for (ushort p = 0; p < PathCount; p++)
        {
            if (_paths[p].AnchorCount >= 3)
            {
                if (anchorWrite != _paths[p].AnchorStart)
                {
                    for (var a = 0; a < _paths[p].AnchorCount; a++)
                        _anchors[anchorWrite + a] = _anchors[_paths[p].AnchorStart + a];
                }

                if (pathWrite != p)
                    _paths[pathWrite] = _paths[p];

                _paths[pathWrite].AnchorStart = (ushort)anchorWrite;

                for (var a = 0; a < _paths[pathWrite].AnchorCount; a++)
                    _anchors[anchorWrite + a].Path = (ushort)pathWrite;

                anchorWrite += _paths[pathWrite].AnchorCount;
                pathWrite++;
            }
        }

        AnchorCount = (ushort)anchorWrite;
        PathCount = (ushort)pathWrite;

        UpdateSamples();
        UpdateBounds();
    }

    public ref readonly Path GetPath(ushort pathIndex) => ref _paths[pathIndex];

    public ref readonly Anchor GetAnchor(ushort anchorIndex) => ref _anchors[anchorIndex];

    public ushort GetNextAnchorIndex(ushort anchorIndex)
    {
        ref readonly var anchor = ref GetAnchor(anchorIndex);
        ref readonly var path = ref GetPath(anchor.Path);
        var localIndex = (ushort)(anchorIndex - path.AnchorStart);
        var nextLocalIndex = (ushort)((localIndex + 1) % path.AnchorCount);
        return (ushort)(path.AnchorStart + nextLocalIndex);
    }
    
    public ref readonly Anchor GetNextAnchor(ushort anchorIndex) =>
        ref GetAnchor(GetNextAnchorIndex(anchorIndex));

    public void SetPathFillColor(ushort pathIndex, byte fillColor)
    {
        _paths[pathIndex].FillColor = fillColor;
    }

    public ushort AddPath(byte fillColor = 0, byte strokeColor = 0)
    {
        if (PathCount >= MaxPaths) return ushort.MaxValue;

        var pathIndex = PathCount++;
        _paths[pathIndex] = new Path
        {
            AnchorStart = AnchorCount,
            AnchorCount = 0,
            FillColor = fillColor,
            Flags = PathFlags.None
        };

        return pathIndex;
    }

    public ushort AddAnchor(ushort pathIndex, Vector2 position, float curve = 0f)
    {
        if (pathIndex >= PathCount || AnchorCount >= MaxAnchors) return ushort.MaxValue;

        ref var path = ref _paths[pathIndex];
        var anchorIndex = (ushort)(path.AnchorStart + path.AnchorCount);

        if (pathIndex < PathCount - 1)
        {
            for (var i = AnchorCount; i > anchorIndex; i--)
                _anchors[i] = _anchors[i - 1];

            for (var p = pathIndex + 1; p < PathCount; p++)
                _paths[p].AnchorStart++;
        }

        _anchors[anchorIndex] = new Anchor
        {
            Position = position,
            Curve = curve,
            Flags = AnchorFlags.None,
            Path = pathIndex
        };

        path.AnchorCount++;
        AnchorCount++;

        if (path.AnchorCount > 1)
        {
            UpdateSamples(pathIndex, (ushort)(path.AnchorCount - 2));
            UpdateSamples(pathIndex, (ushort)(path.AnchorCount - 1));
        }

        UpdateBounds();
        return anchorIndex;
    }

    public bool IsPointInPath(Vector2 point, ushort pathIndex)
    {
        ref var path = ref _paths[pathIndex];
        if (path.AnchorCount < 3) return false;

        var verts = new List<Vector2>();

        for (var a = 0; a < path.AnchorCount; a++)
        {
            var anchorIdx = (ushort)(path.AnchorStart + a);
            ref var anchor = ref _anchors[anchorIdx];

            verts.Add(anchor.Position);

            if (MathF.Abs(anchor.Curve) > 0.0001f)
            {
                var samples = GetSegmentSamples(anchorIdx);
                for (var s = 0; s < MaxSegmentSamples; s++)
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

    private static float PointToSegmentDistSqr(Vector2 point, Vector2 a, Vector2 b, out Vector2 closest)
    {
        var ab = b - a;
        var ap = point - a;
        var t = Vector2.Dot(ap, ab) / Vector2.Dot(ab, ab);
        t = MathF.Max(0, MathF.Min(1, t));
        closest = a + ab * t;
        return Vector2.DistanceSquared(point, closest);
    }

    public void SetAnchorSelected(ushort anchorIndex, bool selected)
    {
        if (anchorIndex >= AnchorCount)
            return;

        if (selected)
            _anchors[anchorIndex].Flags |= AnchorFlags.Selected;
        else
            _anchors[anchorIndex].Flags &= ~AnchorFlags.Selected;
    }

    public void SetAnchorCurve(ushort anchorIndex, float curve)
    {
        if (anchorIndex >= AnchorCount)
            return;

        _anchors[anchorIndex].Curve = curve;
    }

    public void MoveSelectedAnchors(Vector2 delta, Vector2[] savedPositions, bool snap)
    {
        for (ushort i = 0; i < AnchorCount; i++)
        {
            if ((_anchors[i].Flags & AnchorFlags.Selected) == 0)
                continue;

            var newPos = savedPositions[i] + delta;
            if (snap)
                newPos = Grid.SnapToGrid(newPos);
            _anchors[i].Position = newPos;
        }
    }

    public void RestoreAnchorPositions(Vector2[] savedPositions)
    {
        for (ushort i = 0; i < AnchorCount; i++)
        {
            if ((_anchors[i].Flags & AnchorFlags.Selected) != 0)
                _anchors[i].Position = savedPositions[i];
        }
    }

    public void RestoreAnchorCurves(float[] savedCurves)
    {
        for (ushort i = 0; i < AnchorCount; i++)
            _anchors[i].Curve = savedCurves[i];
    }

    public void TranslateAnchors(Vector2 delta, Vector2[] savedPositions, bool snap)
    {
        for (ushort p = 0; p < PathCount; p++)
        {
            if (!_paths[p].IsSelected)
                continue;

            ref var path = ref _paths[p];
            for (ushort a = 0; a < path.AnchorCount; a++)
            {
                var anchorIdx = (ushort)(path.AnchorStart + a);
                var newPos = savedPositions[anchorIdx] + delta;
                if (snap)
                {
                    newPos.X = MathF.Round(newPos.X);
                    newPos.Y = MathF.Round(newPos.Y);
                }
                _anchors[anchorIdx].Position = newPos;
            }
        }
    }

    public void RotateAnchors(Vector2 pivot, float angle, Vector2[] savedPositions)
    {
        var cos = MathF.Cos(angle);
        var sin = MathF.Sin(angle);

        for (ushort i = 0; i < AnchorCount; i++)
        {
            if (!_anchors[i].IsSelected)
                continue;

            var offset = savedPositions[i] - pivot;
            var rotated = new Vector2(
                offset.X * cos - offset.Y * sin,
                offset.X * sin + offset.Y * cos
            );
            _anchors[i].Position = pivot + rotated;
        }
    }

    public void ScaleAnchors(Vector2 pivot, Vector2 scale, Vector2[] savedPositions)
    {
        for (ushort i = 0; i < AnchorCount; i++)
        {
            if (!_anchors[i].IsSelected)
                continue;

            var offset = savedPositions[i] - pivot;
            _anchors[i].Position = pivot + offset * scale;
        }
    }

    public void SelectAnchorsInRect(Rect rect)
    {
        for (ushort i = 0; i < AnchorCount; i++)
        {
            if (rect.Contains(_anchors[i].Position))
                _anchors[i].Flags |= AnchorFlags.Selected;
        }
    }

    public UnsafeSpan<Vector2> GetSegmentSamples(ushort anchorIndex)
    {
        return _samples.Slice(anchorIndex * MaxSegmentSamples, MaxSegmentSamples);
    }

    private static bool IsPointInPolygon(Vector2 point, Span<Vector2> verts)
    {
        var winding = 0;
        var count = verts.Length;

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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsAnchorSelected(ushort anchorIndex) => _anchors[anchorIndex].IsSelected;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsSegmentSelected(ushort anchorIndex) =>
        IsAnchorSelected(anchorIndex) && IsAnchorSelected(GetNextAnchorIndex(anchorIndex));

    public bool HasSelection()
    {
        for (ushort i = 0; i < AnchorCount; i++)
            if (_anchors[i].IsSelected)
                return true;
        return false;
    }

    public void CopyFrom(Shape source)
    {
        AnchorCount = source.AnchorCount;
        PathCount = source.PathCount;
        Bounds = source.Bounds;
        RasterBounds = source.RasterBounds;

        for (var i = 0; i < source.AnchorCount; i++)
            _anchors[i] = source._anchors[i];

        for (var i = 0; i < source.PathCount; i++)
            _paths[i] = source._paths[i];

        for (var i = 0; i < source.AnchorCount * MaxSegmentSamples; i++)
            _samples[i] = source._samples[i];
    }

    public void Clear()
    {
        AnchorCount = 0;
        PathCount = 0;
        Bounds = Rect.Zero;
        RasterBounds = RectInt.Zero;
    }
}
