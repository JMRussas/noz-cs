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

    public static HitResult Empty => new()
    {
        AnchorIndex = ushort.MaxValue,
        SegmentIndex = ushort.MaxValue,
        PathIndex = ushort.MaxValue,
        AnchorDistSqr = float.MaxValue,
        SegmentDistSqr = float.MaxValue,
    };
}

public sealed unsafe class Shape : IDisposable
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

        // Transform properties (anchors are in local space)
        public Vector2 Position;
        public float Rotation;
        public Vector2 Scale;

        public bool IsSelected => (Flags & PathFlags.Selected) != 0;

        public static Path CreateDefault() => new()
        {
            Scale = Vector2.One
        };
    }

    public struct RasterizeOptions
    {
        public bool AntiAlias;

        public static readonly RasterizeOptions Default = new() { AntiAlias = false };
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
            {
                UpdateSamples(p, a);
            }
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
            var transform = GetPathTransform(p);

            for (ushort a = 0; a < path.AnchorCount; a++)
            {
                var anchorIdx = (ushort)(path.AnchorStart + a);
                ref var anchor = ref _anchors[anchorIdx];

                var worldPos = Vector2.Transform(anchor.Position, transform);
                min = Vector2.Min(min, worldPos);
                max = Vector2.Max(max, worldPos);

                if (MathF.Abs(anchor.Curve) > 0.0001f)
                {
                    var samples = GetSegmentSamples(anchorIdx);
                    for (var s = 0; s < MaxSegmentSamples; s++)
                    {
                        var sampleWorld = Vector2.Transform(samples[s], transform);
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
            var transform = GetPathTransform(p);
                
            for (ushort a = 0; a < path.AnchorCount; a++)
            {
                var anchorIdx = path.AnchorStart + a;
                ref var anchor = ref _anchors[anchorIdx];

                var worldPos = Vector2.Transform(anchor.Position, transform);
                var distSqr = Vector2.DistanceSquared(point, worldPos);
                if (distSqr >= anchorRadiusSqr || distSqr >= result.AnchorDistSqr) continue;
                result.AnchorIndex = (ushort)anchorIdx;
                result.AnchorDistSqr = distSqr;
                result.PathIndex = p;
            }

            for (ushort a = 0; a < path.AnchorCount; a++)
            {
                var a0Idx = (ushort)(path.AnchorStart + a);
                var a1Idx = (ushort)(path.AnchorStart + ((a + 1) % path.AnchorCount));
                ref var a0 = ref _anchors[a0Idx];
                ref var a1 = ref _anchors[a1Idx];
                var samples = GetSegmentSamples(a0Idx);

                var a0World = Vector2.Transform(a0.Position, transform);
                var a1World = Vector2.Transform(a1.Position, transform);
                var sample0World = Vector2.Transform(samples[0], transform);

                var distSqr = PointToSegmentDistSqr(point, a0World, sample0World);
                for (var s = 0; s < MaxSegmentSamples - 1; s++)
                {
                    var sWorld = Vector2.Transform(samples[s], transform);
                    var sNextWorld = Vector2.Transform(samples[s + 1], transform);
                    distSqr = MathF.Min(distSqr, PointToSegmentDistSqr(point, sWorld, sNextWorld));
                }
                var lastSampleWorld = Vector2.Transform(samples[MaxSegmentSamples - 1], transform);
                distSqr = MathF.Min(distSqr, PointToSegmentDistSqr(point, lastSampleWorld, a1World));

                if (distSqr >= segmentRadiusSqr || distSqr >= result.SegmentDistSqr) continue;

                result.SegmentIndex = a0Idx;
                result.SegmentDistSqr = distSqr;
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

    public Matrix3x2 GetPathTransform(ushort pathIndex)
    {
        ref var path = ref _paths[pathIndex];
        return Matrix3x2.CreateScale(path.Scale) *
               Matrix3x2.CreateRotation(path.Rotation) *
               Matrix3x2.CreateTranslation(path.Position);
    }

    public Vector2 TransformPoint(ushort pathIndex, Vector2 localPoint)
    {
        var transform = GetPathTransform(pathIndex);
        return Vector2.Transform(localPoint, transform);
    }

    public Vector2 GetPathCentroid(ushort pathIndex)
    {
        if (pathIndex >= PathCount)
            return Vector2.Zero;

        ref var path = ref _paths[pathIndex];
        if (path.AnchorCount == 0)
            return Vector2.Zero;

        var sum = Vector2.Zero;
        for (ushort a = 0; a < path.AnchorCount; a++)
        {
            var anchorIdx = (ushort)(path.AnchorStart + a);
            sum += _anchors[anchorIdx].Position;
        }

        var localCentroid = sum / path.AnchorCount;
        return TransformPoint(pathIndex, localCentroid);
    }

    public Rect? GetPathBounds(ushort pathIndex)
    {
        if (pathIndex >= PathCount)
            return null;

        ref var path = ref _paths[pathIndex];
        if (path.AnchorCount == 0)
            return null;

        var transform = GetPathTransform(pathIndex);
        var min = new Vector2(float.MaxValue, float.MaxValue);
        var max = new Vector2(float.MinValue, float.MinValue);

        for (ushort a = 0; a < path.AnchorCount; a++)
        {
            var anchorIdx = (ushort)(path.AnchorStart + a);
            ref var anchor = ref _anchors[anchorIdx];

            var worldPos = Vector2.Transform(anchor.Position, transform);
            min = Vector2.Min(min, worldPos);
            max = Vector2.Max(max, worldPos);

            if (MathF.Abs(anchor.Curve) > 0.0001f)
            {
                var samples = GetSegmentSamples(anchorIdx);
                for (var s = 0; s < MaxSegmentSamples; s++)
                {
                    var sampleWorld = Vector2.Transform(samples[s], transform);
                    min = Vector2.Min(min, sampleWorld);
                    max = Vector2.Max(max, sampleWorld);
                }
            }
        }

        return Rect.FromMinMax(min, max);
    }

    public Rect? GetSelectedPathsBounds()
    {
        var min = new Vector2(float.MaxValue, float.MaxValue);
        var max = new Vector2(float.MinValue, float.MinValue);
        var hasSelected = false;

        for (ushort p = 0; p < PathCount; p++)
        {
            if (!_paths[p].IsSelected)
                continue;

            var pathBounds = GetPathBounds(p);
            if (!pathBounds.HasValue)
                continue;

            hasSelected = true;
            min = Vector2.Min(min, pathBounds.Value.Min);
            max = Vector2.Max(max, pathBounds.Value.Max);
        }

        return hasSelected ? Rect.FromMinMax(min, max) : null;
    }

    public float? GetSelectedPathsCommonRotation()
    {
        float? commonRotation = null;

        for (ushort p = 0; p < PathCount; p++)
        {
            if (!_paths[p].IsSelected)
                continue;

            var rotation = _paths[p].Rotation;

            if (!commonRotation.HasValue)
            {
                commonRotation = rotation;
            }
            else if (MathF.Abs(commonRotation.Value - rotation) > 0.0001f)
            {
                return null;
            }
        }

        return commonRotation;
    }

    public (Rect bounds, Vector2 center, float rotation)? GetSelectedPathsRotatedBounds()
    {
        var commonRotation = GetSelectedPathsCommonRotation();
        if (!commonRotation.HasValue)
            return null;

        var rotation = commonRotation.Value;
        if (MathF.Abs(rotation) < 0.0001f)
            return null;

        var invRotation = Matrix3x2.CreateRotation(-rotation);

        var min = new Vector2(float.MaxValue, float.MaxValue);
        var max = new Vector2(float.MinValue, float.MinValue);
        var hasSelected = false;

        for (ushort p = 0; p < PathCount; p++)
        {
            if (!_paths[p].IsSelected)
                continue;

            ref var path = ref _paths[p];
            var pathTransform = GetPathTransform(p);

            for (ushort a = 0; a < path.AnchorCount; a++)
            {
                var anchorIdx = (ushort)(path.AnchorStart + a);
                ref var anchor = ref _anchors[anchorIdx];

                var worldPos = Vector2.Transform(anchor.Position, pathTransform);
                var unrotatedPos = Vector2.Transform(worldPos, invRotation);
                min = Vector2.Min(min, unrotatedPos);
                max = Vector2.Max(max, unrotatedPos);
                hasSelected = true;

                if (MathF.Abs(anchor.Curve) > 0.0001f)
                {
                    var samples = GetSegmentSamples(anchorIdx);
                    for (var s = 0; s < MaxSegmentSamples; s++)
                    {
                        var sampleWorld = Vector2.Transform(samples[s], pathTransform);
                        var sampleUnrotated = Vector2.Transform(sampleWorld, invRotation);
                        min = Vector2.Min(min, sampleUnrotated);
                        max = Vector2.Max(max, sampleUnrotated);
                    }
                }
            }
        }

        if (!hasSelected)
            return null;

        var localBounds = Rect.FromMinMax(min, max);
        var localCenter = localBounds.Center;
        var worldCenter = Vector2.Transform(localCenter, Matrix3x2.CreateRotation(rotation));

        return (localBounds, worldCenter, rotation);
    }

    public Rect? GetSelectedAnchorsBounds()
    {
        var min = new Vector2(float.MaxValue, float.MaxValue);
        var max = new Vector2(float.MinValue, float.MinValue);
        var hasSelected = false;

        for (ushort i = 0; i < AnchorCount; i++)
        {
            if (!_anchors[i].IsSelected)
                continue;

            hasSelected = true;
            var pathIndex = _anchors[i].Path;
            var worldPos = TransformPoint(pathIndex, _anchors[i].Position);
            min = Vector2.Min(min, worldPos);
            max = Vector2.Max(max, worldPos);
        }

        return hasSelected ? Rect.FromMinMax(min, max) : null;
    }

    public Vector2? GetSelectedPathsCentroid()
    {
        var sum = Vector2.Zero;
        var count = 0;

        for (ushort p = 0; p < PathCount; p++)
        {
            if (!_paths[p].IsSelected)
                continue;

            var centroid = GetPathCentroid(p);
            sum += centroid;
            count++;
        }

        return count > 0 ? sum / count : null;
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
            var worldPos = TransformPoint(pathIndex, _anchors[i].Position);
            sum += worldPos;
            count++;
        }

        return count > 0 ? sum / count : null;
    }

    public void SelectPathsInRect(Rect rect)
    {
        for (ushort p = 0; p < PathCount; p++)
        {
            var pathBounds = GetPathBounds(p);
            if (pathBounds.HasValue && rect.Intersects(pathBounds.Value))
                _paths[p].Flags |= PathFlags.Selected;
        }
    }

    public void SelectAnchors(Rect rect)
    {
        for (ushort p = 0; p < PathCount; p++)
        {
            ref var path = ref _paths[p];
            var transform = GetPathTransform(p);

            for (ushort a = 0; a < path.AnchorCount; a++)
            {
                var anchorIdx = (ushort)(path.AnchorStart + a);
                var worldPos = Vector2.Transform(_anchors[anchorIdx].Position, transform);
                if (rect.Contains(worldPos))
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
#if false        
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

        ref var anchor = ref _anchors[anchorIndex];
        var midpoint = anchor.Midpoint;
        var newCurve = anchor.Curve * 0.5f;

        InsertAnchor(anchorIndex, midpoint, newCurve);
        anchor.Curve = newCurve;

        UpdateSamples();
        UpdateBounds();
#endif
    }

    public void DeleteSelectedAnchors()
    {
        for (ushort p = 0; p < PathCount; p++)
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
            AnchorCount -= (ushort)removed;

            for (var np = p + 1; np < PathCount; np++)
                _paths[np].AnchorStart -= (ushort)removed;
        }

        var pathWrite = 0;
        for (var p = 0; p < PathCount; p++)
        {
            if (_paths[p].AnchorCount > 0)
            {
                if (pathWrite != p)
                    _paths[pathWrite] = _paths[p];
                pathWrite++;
            }
        }
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
            Flags = PathFlags.None,
            Position = Vector2.Zero,
            Rotation = 0f,
            Scale = Vector2.One,
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

    private bool IsPointInPath(Vector2 point, ushort pathIndex)
    {
        ref var path = ref _paths[pathIndex];
        if (path.AnchorCount < 3) return false;

        var transform = GetPathTransform(pathIndex);
        var verts = new List<Vector2>();

        for (var a = 0; a < path.AnchorCount; a++)
        {
            var anchorIdx = (ushort)(path.AnchorStart + a);
            ref var anchor = ref _anchors[anchorIdx];

            verts.Add(Vector2.Transform(anchor.Position, transform));

            if (MathF.Abs(anchor.Curve) > 0.0001f)
            {
                var samples = GetSegmentSamples(anchorIdx);
                for (var s = 0; s < MaxSegmentSamples; s++)
                    verts.Add(Vector2.Transform(samples[s], transform));
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
            {
                newPos.X = MathF.Round(newPos.X);
                newPos.Y = MathF.Round(newPos.Y);
            }
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

    public void MoveAnchorsInSelectedPaths(Vector2 delta, Vector2[] savedPositions, bool snap)
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

    public void RestoreAnchorsInSelectedPaths(Vector2[] savedPositions)
    {
        for (ushort p = 0; p < PathCount; p++)
        {
            if (!_paths[p].IsSelected)
                continue;

            ref var path = ref _paths[p];
            for (ushort a = 0; a < path.AnchorCount; a++)
            {
                var anchorIdx = (ushort)(path.AnchorStart + a);
                _anchors[anchorIdx].Position = savedPositions[anchorIdx];
            }
        }
    }

    public void RotateSelectedAnchors(Vector2 pivot, float angle, Vector2[] savedPositions)
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

    public void RotateAnchorsInSelectedPaths(Vector2 pivot, float angle, Vector2[] savedPositions)
    {
        var cos = MathF.Cos(angle);
        var sin = MathF.Sin(angle);

        for (ushort p = 0; p < PathCount; p++)
        {
            if (!_paths[p].IsSelected)
                continue;

            ref var path = ref _paths[p];
            for (ushort a = 0; a < path.AnchorCount; a++)
            {
                var anchorIdx = (ushort)(path.AnchorStart + a);
                var offset = savedPositions[anchorIdx] - pivot;
                var rotated = new Vector2(
                    offset.X * cos - offset.Y * sin,
                    offset.X * sin + offset.Y * cos
                );
                _anchors[anchorIdx].Position = pivot + rotated;
            }
        }
    }

    public void ScaleSelectedAnchors(Vector2 pivot, Vector2 scale, Vector2[] savedPositions)
    {
        for (ushort i = 0; i < AnchorCount; i++)
        {
            if (!_anchors[i].IsSelected)
                continue;

            var offset = savedPositions[i] - pivot;
            _anchors[i].Position = pivot + offset * scale;
        }
    }

    public void ScaleAnchorsInSelectedPaths(Vector2 pivot, Vector2 scale, Vector2[] savedPositions)
    {
        for (ushort p = 0; p < PathCount; p++)
        {
            if (!_paths[p].IsSelected)
                continue;

            ref var path = ref _paths[p];
            for (ushort a = 0; a < path.AnchorCount; a++)
            {
                var anchorIdx = (ushort)(path.AnchorStart + a);
                var offset = savedPositions[anchorIdx] - pivot;
                _anchors[anchorIdx].Position = pivot + offset * scale;
            }
        }
    }

    public void SetPathTransform(ushort pathIndex, Vector2 position, float rotation, Vector2 scale)
    {
        if (pathIndex >= PathCount)
            return;

        _paths[pathIndex].Position = position;
        _paths[pathIndex].Rotation = rotation;
        _paths[pathIndex].Scale = scale;
    }

    public void RotateSelectedPaths(Vector2 pivot, float angle, Vector2[] savedCentroids, float[] savedRotations)
    {
        var cos = MathF.Cos(angle);
        var sin = MathF.Sin(angle);

        for (ushort p = 0; p < PathCount; p++)
        {
            if (!_paths[p].IsSelected)
                continue;

            // Rotate saved world centroid around pivot
            var offset = savedCentroids[p] - pivot;
            var rotatedOffset = new Vector2(
                offset.X * cos - offset.Y * sin,
                offset.X * sin + offset.Y * cos
            );
            var newWorldCentroid = pivot + rotatedOffset;

            // Update rotation
            _paths[p].Rotation = savedRotations[p] + angle;

            // Calculate new position so that the local centroid ends up at the new world position
            var localCentroid = GetPathLocalCentroid(p);
            var newTransform = Matrix3x2.CreateScale(_paths[p].Scale) *
                               Matrix3x2.CreateRotation(_paths[p].Rotation);
            var transformedLocalCentroid = Vector2.Transform(localCentroid, newTransform);
            _paths[p].Position = newWorldCentroid - transformedLocalCentroid;
        }
    }

    public void ScaleSelectedPaths(Vector2 pivot, Vector2 scale, Vector2[] savedCentroids, Vector2[] savedScales)
    {
        for (ushort p = 0; p < PathCount; p++)
        {
            if (!_paths[p].IsSelected)
                continue;

            // Scale saved world centroid relative to pivot
            var offset = savedCentroids[p] - pivot;
            var scaledOffset = offset * scale;
            var newWorldCentroid = pivot + scaledOffset;

            // Update scale
            _paths[p].Scale = savedScales[p] * scale;

            // Calculate new position so that the local centroid ends up at the new world position
            var localCentroid = GetPathLocalCentroid(p);
            var newTransform = Matrix3x2.CreateScale(_paths[p].Scale) *
                               Matrix3x2.CreateRotation(_paths[p].Rotation);
            var transformedLocalCentroid = Vector2.Transform(localCentroid, newTransform);
            _paths[p].Position = newWorldCentroid - transformedLocalCentroid;
        }
    }

    public void RestoreSelectedPathTransforms(Vector2[] savedPositions, float[] savedRotations, Vector2[] savedScales)
    {
        for (ushort p = 0; p < PathCount; p++)
        {
            if (!_paths[p].IsSelected)
                continue;

            _paths[p].Position = savedPositions[p];
            _paths[p].Rotation = savedRotations[p];
            _paths[p].Scale = savedScales[p];
        }
    }

    public void SaveSelectedPathTransforms(Vector2[] outPositions, float[] outRotations, Vector2[] outScales)
    {
        for (ushort p = 0; p < PathCount; p++)
        {
            if (!_paths[p].IsSelected)
                continue;

            outPositions[p] = _paths[p].Position;
            outRotations[p] = _paths[p].Rotation;
            outScales[p] = _paths[p].Scale;
        }
    }

    public void SaveSelectedPathCentroids(Vector2[] outCentroids)
    {
        for (ushort p = 0; p < PathCount; p++)
        {
            if (!_paths[p].IsSelected)
                continue;

            outCentroids[p] = GetPathCentroid(p);
        }
    }

    public Vector2 GetPathLocalCentroid(ushort pathIndex)
    {
        if (pathIndex >= PathCount)
            return Vector2.Zero;

        ref var path = ref _paths[pathIndex];
        if (path.AnchorCount == 0)
            return Vector2.Zero;

        var sum = Vector2.Zero;
        for (ushort a = 0; a < path.AnchorCount; a++)
        {
            var anchorIdx = (ushort)(path.AnchorStart + a);
            sum += _anchors[anchorIdx].Position;
        }

        return sum / path.AnchorCount;
    }

    public void ApplyTransformsToSelectedPaths()
    {
        for (ushort p = 0; p < PathCount; p++)
        {
            if (!_paths[p].IsSelected)
                continue;

            ref var path = ref _paths[p];
            var transform = GetPathTransform(p);

            for (ushort a = 0; a < path.AnchorCount; a++)
            {
                var anchorIdx = (ushort)(path.AnchorStart + a);
                _anchors[anchorIdx].Position = Vector2.Transform(_anchors[anchorIdx].Position, transform);
            }

            path.Position = Vector2.Zero;
            path.Rotation = 0f;
            path.Scale = Vector2.One;
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

    public void Rasterize(PixelData<Color32> pixels, Color[] palette, Vector2Int offset)
        => Rasterize(pixels, palette, offset, RasterizeOptions.Default);

    public void Rasterize(PixelData<Color32> pixels, Color[] palette, Vector2Int offset, RasterizeOptions options)
    {
        if (PathCount == 0) return;

        const int maxPolyVerts = 256;
        Span<Vector2> polyVerts = stackalloc Vector2[maxPolyVerts];
        var dpi = EditorApplication.Config.PixelsPerUnit;

        for (ushort pIdx = 0; pIdx < PathCount; pIdx++)
        {
            ref var path = ref _paths[pIdx];
            if (path.AnchorCount < 3) continue;

            var transform = GetPathTransform(pIdx);
            var vertexCount = 0;

            for (ushort aIdx = 0; aIdx < path.AnchorCount && vertexCount < maxPolyVerts; aIdx++)
            {
                var anchorIdx = (ushort)(path.AnchorStart + aIdx);
                ref var anchor = ref _anchors[anchorIdx];

                var worldPos = Vector2.Transform(anchor.Position, transform);
                polyVerts[vertexCount++] = worldPos * dpi;

                if (MathF.Abs(anchor.Curve) > 0.0001f)
                {
                    var samples = GetSegmentSamples(anchorIdx);
                    for (var s = 0; s < MaxSegmentSamples && vertexCount < maxPolyVerts; s++)
                    {
                        var sampleWorld = Vector2.Transform(samples[s], transform);
                        polyVerts[vertexCount++] = sampleWorld * dpi;
                    }
                }
            }

            if (vertexCount < 3) continue;

            var fillColor = palette[path.FillColor % palette.Length].ToColor32();
            var rb = RasterBounds;

            if (options.AntiAlias)
            {
                RasterizePathAA(pixels, polyVerts[..vertexCount], fillColor, offset, rb);
            }
            else
            {
                RasterizePathSimple(pixels, polyVerts[..vertexCount], fillColor, offset, rb);
            }
        }
    }

    private static void RasterizePathSimple(PixelData<Color32> pixels, Span<Vector2> polyVerts,
        Color32 fillColor, Vector2Int offset, RectInt rb)
    {
        for (var y = 0; y < rb.Height; y++)
        {
            var py = offset.Y + rb.Y + y;
            if (py < 0 || py >= pixels.Height) continue;

            var sampleY = rb.Y + y + 0.5f;

            for (var x = 0; x < rb.Width; x++)
            {
                var px = offset.X + rb.X + x;
                if (px < 0 || px >= pixels.Width) continue;

                var sampleX = rb.X + x + 0.5f;
                if (IsPointInPolygon(new Vector2(sampleX, sampleY), polyVerts))
                {
                    ref var dst = ref pixels[px, py];
                    if (fillColor.A == 255 || dst.A == 0)
                    {
                        dst = fillColor;
                    }
                    else if (fillColor.A > 0)
                    {
                        dst = Color32.Blend(dst, fillColor);
                    }
                }
            }
        }
    }

    private static void RasterizePathAA(PixelData<Color32> pixels, Span<Vector2> polyVerts,
        Color32 fillColor, Vector2Int offset, RectInt rb)
    {
        const float edgeMarker = -1f;
        var bufferSize = rb.Width * rb.Height;

        Span<float> coverage = bufferSize <= 4096
            ? stackalloc float[bufferSize]
            : new float[bufferSize];
        coverage.Clear();

        var vertexCount = polyVerts.Length;

        for (var y = 0; y < rb.Height; y++)
        {
            var sampleY = rb.Y + y + 0.5f;
            for (var x = 0; x < rb.Width; x++)
            {
                var sampleX = rb.X + x + 0.5f;
                if (IsPointInPolygon(new Vector2(sampleX, sampleY), polyVerts))
                    coverage[y * rb.Width + x] = 1f;
            }
        }

        for (var i = 0; i < vertexCount; i++)
        {
            var p0 = polyVerts[i];
            var p1 = polyVerts[(i + 1) % vertexCount];

            var minX = (int)MathF.Floor(MathF.Min(p0.X, p1.X));
            var maxX = (int)MathF.Floor(MathF.Max(p0.X, p1.X));
            var minY = (int)MathF.Floor(MathF.Min(p0.Y, p1.Y));
            var maxY = (int)MathF.Floor(MathF.Max(p0.Y, p1.Y));

            for (var py = minY; py <= maxY; py++)
            {
                var localY = py - rb.Y;
                if (localY < 0 || localY >= rb.Height) continue;

                for (var px = minX; px <= maxX; px++)
                {
                    var localX = px - rb.X;
                    if (localX < 0 || localX >= rb.Width) continue;

                    if (LineIntersectsRect(p0, p1, px, py, px + 1, py + 1))
                        coverage[localY * rb.Width + localX] = edgeMarker;
                }
            }
        }

        const int aaSamples = 4;
        Span<float> sampleOffsets = [0.125f, 0.375f, 0.625f, 0.875f];

        for (var idx = 0; idx < bufferSize; idx++)
        {
            if (coverage[idx] != edgeMarker) continue;

            var localX = idx % rb.Width;
            var localY = idx / rb.Width;
            float px = rb.X + localX;
            float py = rb.Y + localY;

            var insideCount = 0;
            for (var sy = 0; sy < aaSamples; sy++)
            {
                for (var sx = 0; sx < aaSamples; sx++)
                {
                    var samplePos = new Vector2(px + sampleOffsets[sx], py + sampleOffsets[sy]);
                    if (IsPointInPolygon(samplePos, polyVerts))
                        insideCount++;
                }
            }

            coverage[idx] = insideCount / (float)(aaSamples * aaSamples);
        }

        for (var y = 0; y < rb.Height; y++)
        {
            var py = offset.Y + rb.Y + y;
            if (py < 0 || py >= pixels.Height) continue;

            for (var x = 0; x < rb.Width; x++)
            {
                var px = offset.X + rb.X + x;
                if (px < 0 || px >= pixels.Width) continue;

                var cov = coverage[y * rb.Width + x];
                if (cov <= 0) continue;

                byte finalAlpha;
                if (cov >= 0.5f)
                    finalAlpha = fillColor.A;
                else
                    finalAlpha = (byte)(cov * fillColor.A);

                if (finalAlpha == 0) continue;

                ref var dst = ref pixels[px, py];
                if (dst.A == 0)
                {
                    dst = new Color32(fillColor.R, fillColor.G, fillColor.B, finalAlpha);
                }
                else if (finalAlpha == 255 && fillColor.A == 255)
                {
                    dst = fillColor;
                }
                else
                {
                    var srcColor = fillColor.WithAlpha(finalAlpha);
                    dst = Color32.Blend(dst, srcColor);
                }
            }
        }
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

    private static bool LineIntersectsRect(Vector2 p0, Vector2 p1,
        float xMin, float yMin, float xMax, float yMax)
    {
        var dx = p1.X - p0.X;
        var dy = p1.Y - p0.Y;

        var tMin = 0f;
        var tMax = 1f;

        Span<float> p = [- dx, dx, -dy, dy];
        Span<float> q = [p0.X - xMin, xMax - p0.X, p0.Y - yMin, yMax - p0.Y];

        for (var i = 0; i < 4; i++)
        {
            if (MathF.Abs(p[i]) < 0.0001f)
            {
                if (q[i] < 0)
                    return false;
            }
            else
            {
                var t = q[i] / p[i];
                if (p[i] < 0)
                    tMin = MathF.Max(tMin, t);
                else
                    tMax = MathF.Min(tMax, t);
            }
        }

        return tMin <= tMax;
    }

    private static int ClipPolygonToRect(Span<Vector2> poly, int count,
        float xMin, float yMin, float xMax, float yMax,
        Span<Vector2> output)
    {
        if (count < 3)
            return 0;

        Span<Vector2> temp1 = stackalloc Vector2[count + 4];
        Span<Vector2> temp2 = stackalloc Vector2[count + 4];

        poly[..count].CopyTo(temp1);
        var currentCount = count;

        currentCount = ClipPolygonAgainstEdge(temp1, currentCount, temp2, xMin, true, true);
        if (currentCount < 3) return 0;

        currentCount = ClipPolygonAgainstEdge(temp2, currentCount, temp1, xMax, true, false);
        if (currentCount < 3) return 0;

        currentCount = ClipPolygonAgainstEdge(temp1, currentCount, temp2, yMin, false, true);
        if (currentCount < 3) return 0;

        currentCount = ClipPolygonAgainstEdge(temp2, currentCount, output, yMax, false, false);
        return currentCount;
    }

    private static int ClipPolygonAgainstEdge(Span<Vector2> input, int inputCount,
        Span<Vector2> output, float edgeValue, bool isVertical, bool isMin)
    {
        var outputCount = 0;

        for (var i = 0; i < inputCount; i++)
        {
            var current = input[i];
            var next = input[(i + 1) % inputCount];

            var currentVal = isVertical ? current.X : current.Y;
            var nextVal = isVertical ? next.X : next.Y;

            var currentInside = isMin ? currentVal >= edgeValue : currentVal <= edgeValue;
            var nextInside = isMin ? nextVal >= edgeValue : nextVal <= edgeValue;

            if (currentInside)
            {
                if (outputCount < output.Length)
                    output[outputCount++] = current;

                if (!nextInside)
                {
                    var intersection = ComputeEdgeIntersection(current, next, edgeValue, isVertical);
                    if (outputCount < output.Length)
                        output[outputCount++] = intersection;
                }
            }
            else if (nextInside)
            {
                var intersection = ComputeEdgeIntersection(current, next, edgeValue, isVertical);
                if (outputCount < output.Length)
                    output[outputCount++] = intersection;
            }
        }

        return outputCount;
    }

    private static Vector2 ComputeEdgeIntersection(Vector2 p0, Vector2 p1, float edgeValue, bool isVertical)
    {
        if (isVertical)
        {
            var t = (edgeValue - p0.X) / (p1.X - p0.X);
            return new Vector2(edgeValue, p0.Y + t * (p1.Y - p0.Y));
        }
        else
        {
            var t = (edgeValue - p0.Y) / (p1.Y - p0.Y);
            return new Vector2(p0.X + t * (p1.X - p0.X), edgeValue);
        }
    }

    private static float CalculatePolygonArea(Span<Vector2> verts, int count)
    {
        if (count < 3)
            return 0f;

        var area = 0f;
        for (var i = 0; i < count; i++)
        {
            var j = (i + 1) % count;
            area += verts[i].X * verts[j].Y;
            area -= verts[j].X * verts[i].Y;
        }

        return MathF.Abs(area) * 0.5f;
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
