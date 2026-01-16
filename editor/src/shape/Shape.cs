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
        Selected = 1 << 0,
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

    public void UpdateBounds(float dpi = 1f)
    {
        if (AnchorCount == 0)
        {
            Bounds = Rect.Zero;
            RasterBounds = RectInt.Zero;
            return;
        }

        var min = new Vector2(float.MaxValue, float.MaxValue);
        var max = new Vector2(float.MinValue, float.MinValue);

        for (ushort p = 0; p < PathCount; p++)
        {
            ref var path = ref _paths[p];
            for (ushort a = 0; a < path.AnchorCount; a++)
            {
                var anchorIdx = (ushort)(path.AnchorStart + a);
                ref var anchor = ref _anchors[anchorIdx];

                min = Vector2.Min(min, anchor.Position);
                max = Vector2.Max(max, anchor.Position);

                if (MathF.Abs(anchor.Curve) > 0.0001f)
                {
                    var samples = GetSegmentSamples(anchorIdx);
                    for (var s = 0; s < MaxSegmentSamples; s++)
                    {
                        min = Vector2.Min(min, samples[s]);
                        max = Vector2.Max(max, samples[s]);
                    }
                }
            }
        }

        Bounds = Rect.FromMinMax(min, max);

        var xMin = (int)MathF.Floor(min.X * dpi);
        var yMin = (int)MathF.Floor(min.Y * dpi);
        var xMax = (int)MathF.Ceiling(max.X * dpi);
        var yMax = (int)MathF.Ceiling(max.Y * dpi);

        RasterBounds = new RectInt(xMin, yMin, xMax - xMin, yMax - yMin);
    }

    public HitResult HitTest(Vector2 point, float anchorRadius = 5f, float segmentRadius = 3f)
    {
        var result = HitResult.Empty;
        var anchorRadiusSqr = anchorRadius * anchorRadius;
        var segmentRadiusSqr = segmentRadius * segmentRadius;

        for (ushort p = 0; p < PathCount; p++)
        {
            ref var path = ref _paths[p];

            for (ushort a = 0; a < path.AnchorCount; a++)
            {
                var anchorIdx = path.AnchorStart + a;
                ref var anchor = ref _anchors[anchorIdx];

                var distSqr = Vector2.DistanceSquared(point, anchor.Position);
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

                
                var distSqr = PointToSegmentDistSqr(point, a0.Position, samples[0]);
                for (var s = 0; s < MaxSegmentSamples - 1; s++)
                    distSqr = MathF.Min(distSqr, PointToSegmentDistSqr(point, samples[s], samples[s + 1]));
                distSqr = MathF.Min(distSqr, PointToSegmentDistSqr(point, samples[MaxSegmentSamples - 1], a1.Position));

                if (distSqr >= segmentRadiusSqr || distSqr >= result.SegmentDistSqr) continue;

                result.SegmentIndex = a0Idx;
                result.SegmentDistSqr = distSqr;
                if (result.PathIndex == ushort.MaxValue)
                    result.PathIndex = p;
            }

            if (result.PathIndex == ushort.MaxValue && IsPointInPath(point, p))
                result.PathIndex = p;
        }

        return result;
    }

    public void ClearSelection()
    {
        for (var i = 0; i < AnchorCount; i++)
            _anchors[i].Flags &= ~AnchorFlags.Selected;

        for (var i = 0; i < PathCount; i++)
            _paths[i].Flags &= ~PathFlags.Selected;
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

    public void Rasterize(PixelData<Color32> pixels, Color[] palette, Vector2Int offset, float dpi = 1f)
    {
        if (PathCount == 0) return;

        const int maxPolyVerts = 256;
        Span<Vector2> polyVerts = stackalloc Vector2[maxPolyVerts];

        for (ushort pIdx = 0; pIdx < PathCount; pIdx++)
        {
            ref var path = ref _paths[pIdx];
            if (path.AnchorCount < 3) continue;

            var vertexCount = 0;
            for (ushort aIdx = 0; aIdx < path.AnchorCount && vertexCount < maxPolyVerts; aIdx++)
            {
                var anchorIdx = (ushort)(path.AnchorStart + aIdx);
                ref var anchor = ref _anchors[anchorIdx];

                polyVerts[vertexCount++] = anchor.Position * dpi;

                if (MathF.Abs(anchor.Curve) > 0.0001f)
                {
                    var samples = GetSegmentSamples(anchorIdx);
                    for (var s = 0; s < MaxSegmentSamples && vertexCount < maxPolyVerts; s++)
                    {
                        polyVerts[vertexCount++] = samples[s] * dpi;
                    }
                }
            }

            if (vertexCount < 3) continue;

            var fillColor = palette[path.FillColor % palette.Length].ToColor32();

            var rb = RasterBounds;
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
                    if (IsPointInPolygon(new Vector2(sampleX, sampleY), polyVerts[..vertexCount]))
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
