//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ.Editor;

public static class Clipboard
{
    private static object? _content;
    private static Type? _contentType;

    public static bool HasContent => _content != null;
    public static Type? ContentType => _contentType;

    public static void Copy<T>(T content) where T : class
    {
        if (_content is IDisposable disposable)
            disposable.Dispose();

        _content = content;
        _contentType = typeof(T);
    }

    public static T? Get<T>() where T : class
    {
        if (_content is T typed)
            return typed;
        return null;
    }

    public static bool Is<T>() where T : class => _content is T;

    public static void Clear()
    {
        if (_content is IDisposable disposable)
            disposable.Dispose();

        _content = null;
        _contentType = null;
    }
}

public sealed class PathClipboardData
{
    public struct PathData
    {
        public byte FillColor;
        public float FillOpacity;
        public bool IsSubtract;
        public Vector2[] Anchors;
        public float[] Curves;
    }

    public PathData[] Paths { get; }
    public Vector2 Center { get; }

    public PathClipboardData(Shape shape)
    {
        var pathList = new List<PathData>();
        var allPositions = new List<Vector2>();

        for (ushort p = 0; p < shape.PathCount; p++)
        {
            ref readonly var srcPath = ref shape.GetPath(p);

            var selectedAnchors = new List<Vector2>();
            var selectedCurves = new List<float>();

            for (ushort a = 0; a < srcPath.AnchorCount; a++)
            {
                ref readonly var srcAnchor = ref shape.GetAnchor((ushort)(srcPath.AnchorStart + a));
                if (!srcAnchor.IsSelected)
                    continue;

                selectedAnchors.Add(srcAnchor.Position);
                selectedCurves.Add(srcAnchor.Curve);
            }

            if (selectedAnchors.Count < 3)
                continue;

            pathList.Add(new PathData
            {
                FillColor = srcPath.FillColor,
                FillOpacity = srcPath.FillOpacity,
                IsSubtract = srcPath.IsSubtract,
                Anchors = selectedAnchors.ToArray(),
                Curves = selectedCurves.ToArray()
            });

            allPositions.AddRange(selectedAnchors);
        }

        Paths = pathList.ToArray();

        if (allPositions.Count > 0)
        {
            var sum = Vector2.Zero;
            foreach (var pos in allPositions)
                sum += pos;
            Center = sum / allPositions.Count;
        }
    }

    public void PasteInto(Shape shape)
    {
        var firstNewAnchor = shape.AnchorCount;

        foreach (var pathData in Paths)
        {
            var newPathIndex = shape.AddPath(pathData.FillColor, opacity: pathData.FillOpacity, subract: pathData.IsSubtract);
            if (newPathIndex == ushort.MaxValue)
                break;

            for (var a = 0; a < pathData.Anchors.Length; a++)
                shape.AddAnchor(newPathIndex, pathData.Anchors[a], pathData.Curves[a]);
        }

        for (var i = firstNewAnchor; i < shape.AnchorCount; i++)
            shape.SetAnchorSelected((ushort)i, true);

        shape.UpdateSamples();
        shape.UpdateBounds();
    }
}
