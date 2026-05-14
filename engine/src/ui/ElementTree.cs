//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using NoZ.Platform;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace NoZ;



public static unsafe partial class ElementTree
{
    private static readonly ProfilerMarker s_markerLayout = new("UI.Layout");
    private static readonly ProfilerMarker s_markerTransforms = new("UI.Transforms");
    private static readonly ProfilerMarker s_markerDraw = new("UI.Draw");
    private static readonly ProfilerMarker s_markerInput = new("UI.Input");
    private static readonly ProfilerCounter s_counterElements = new("UI.Elements");
    private static readonly ProfilerCounter s_counterWidgets = new("UI.Widgets");
    private static readonly ProfilerCounter s_counterPopups = new("UI.Popups");
    private static readonly ProfilerCounter s_counterFills = new("UI.Fills");
    private static readonly ProfilerCounter s_counterLabels = new("UI.Labels");
    private static readonly ProfilerCounter s_counterImages = new("UI.Images");
    private static readonly ProfilerCounter s_counterScenes = new("UI.Scenes");
    private static readonly ProfilerCounter s_counterVertices = new("UI.Vertices");
    private static readonly ProfilerCounter s_counterIndices = new("UI.Indices");

    private const int MaxElements = 4096;
    private const int MaxDataSize = 8192 * 4;
    private const int MaxWidgets = 1024;
    private const int MaxElementDepth = 512;

    private const int MaxElementSize = 65535;
    private const int MaxAssets = 1024;
    private const int MaxVertices = 16384;
    private const int MaxIndices = 32768;
    private const int MaxPopups = 4;

    private static readonly byte* _zeroState = (byte*)System.Runtime.InteropServices.NativeMemory.AllocZeroed(256);

    private static NativeArray<byte>[] _data = null!;
    private static NativeArray<Element> _elements;
    private static NativeHashMap<UnsafeRef<WidgetState>> _widgets;
    private static NativeHashMap<UnsafeRef<WidgetState>> _widgetsPrev;
    private static NativeArray<ushort> _stack;    
    private static NativeArray<ushort> _trees;
    private static ushort _frame;
    private static ushort _nextSibling;
    private static WidgetId _currentWidget;
    internal static WidgetId _lastWidgetId;

    private static readonly object?[] _assets = new object?[MaxAssets];
    private static int _assetCount;

    // Popup tracking
    private static readonly int[] _popups = new int[MaxPopups];
    private static int _popupCount;
    private static int _activePopupCount;
    internal static bool ClosePopups { get; private set; }
    internal static int CloseFromPopupLevel { get; private set; } = -1;
    internal static int PopupCount => _popupCount;
    internal static int ActivePopupCount => _activePopupCount;

    // Input state
    internal static WidgetId _hotId;
    internal static WidgetId _prevHotId;
    private static WidgetId _captureId;
    private static int _tabNavigationTarget = -1;

    // Drawing state (self-contained, not shared with UI)
    private static RenderMesh _mesh;
    private static NativeArray<UIVertex> _vertices;
    private static NativeArray<ushort> _indices;
    private static Shader _shader = null!;
    private static Shader _spriteShader = null!;
    private static Shader _textureShader = null!;
    private static float _drawOpacity = 1.0f;

    public static void Init()
    {
        _elements = new NativeArray<Element>(MaxElements);
        _widgets = new NativeHashMap<UnsafeRef<WidgetState>>(MaxWidgets);
        _widgetsPrev = new NativeHashMap<UnsafeRef<WidgetState>>(MaxWidgets);
        _data = [
            new NativeArray<byte>(MaxDataSize),
            new NativeArray<byte>(MaxDataSize)
        ];

        _stack = new NativeArray<ushort>(MaxElementDepth);
        _trees = new NativeArray<ushort>(MaxElementDepth);
        _vertices = new NativeArray<UIVertex>(MaxVertices);
        _indices = new NativeArray<ushort>(MaxIndices);
        _mesh = Graphics.CreateMesh<UIVertex>(MaxVertices, MaxIndices, BufferUsage.Dynamic, "ElementTreeMesh");
        _shader = Asset.Get<Shader>(AssetType.Shader, "ui")!;
        _spriteShader = Asset.Get<Shader>(AssetType.Shader, "sprite")!;
        _textureShader = Asset.Get<Shader>(AssetType.Shader, "texture")!;
    }

    internal static void Shutdown()
    {
        _elements.Dispose();
        _widgets.Dispose();
        _widgetsPrev.Dispose();
        _data[0].Dispose();
        _data[1].Dispose();
        _vertices.Dispose();
        _indices.Dispose();
        Graphics.Driver.DestroyMesh(_mesh.Handle);
    }

    internal static void Begin(Vector2 size)
    {
        ScreenSize = size;

        _frame++;
        _layoutCycleLogged = false;

        _elements.Clear();
        _stack.Clear();
        _trees.Clear();
        _data[_frame & 1].Clear();
        _widgetsPrev.SwapAndClear(ref _widgets);

        _assetCount = 0;
        _currentWidget = WidgetId.None;
        _popupCount = 0;
        _activePopupCount = 0;

        ref var e = ref _elements.Add();
        e = default;
        e.Type = ElementType.Size;
        e.Data.Size = new SizeElement
        {
            Size = new Size2(size.X, size.Y),
            MinWidth = 0,
            MaxWidth = float.MaxValue,
            MinHeight = 0,
            MaxHeight = float.MaxValue,
        };
        _stack.Add(0);
    }

    internal static void End()
    {
        Debug.Assert(_stack.Length == 1, "Mismatched Begin/End calls. Stack length: " + _stack.Length);
        Debug.Assert(_trees.Length == 0, "Mismatched BeginTree/EndTree calls. Trees length: " + _trees.Length);

        if (_elements.Length < 2) return;

        using (s_markerLayout.Begin())
        {
            LayoutAxis(0, 0, ScreenSize.X, 0, -1);
            LayoutAxis(0, 0, ScreenSize.Y, 1, -1);
        }

        using (s_markerTransforms.Begin())
            UpdateTransforms(0, Matrix3x2.Identity, Matrix3x2.Identity, Vector2.Zero);

        s_counterElements.Value = _elements.Length;
        s_counterWidgets.Value = _widgets.Count;
        s_counterPopups.Value = _popupCount;
    }

    public static void BeginTree()
    {
        _trees.Add((ushort)_stack.Length);
    }

    public static void EndTree()
    {
        Debug.Assert(_trees.Length > 0);
        var stackIndex = _trees[^1];
        _trees.RemoveLast();

        while (_stack.Length > stackIndex)
        {
            ref var e = ref GetElement(_stack[^1]);
            EndElement(e.Type);
        }
    }

    private static ref Element GetElement(int index) =>
        ref _elements[index];

    private static ref Element BeginElement(ElementType type)
    {
        var index = _elements.Length;
        ref var e = ref _elements.Add();
        e.Type = type;
        e.Parent = _stack[^1];
        e.NextSibling = 0;
        e.ChildCount = 0;
        e.FirstChild = 0;
        e.Index = (ushort)index;
        _stack.Add((ushort)index);

        ref var p = ref GetElement(e.Parent);
        p.ChildCount++;
        if (p.FirstChild == 0)
            p.FirstChild = (ushort)index;

        return ref e;
    }

    private static void EndElement(ElementType type)
    {
        Debug.Assert(_stack.Length > 0);
        var index = _stack[^1];
        _stack.RemoveLast();
        _nextSibling = index;
        
        ref var e = ref GetElement(index);
        e.NextSibling = (ushort)_elements.Length;

        Debug.Assert(e.Type == type);
    }

    private static ushort AddObject(object obj)
    {
        Debug.Assert(_assetCount < MaxAssets, "Asset array exceeded maximum capacity.");
        var index = (ushort)_assetCount;
        _assets[_assetCount++] = obj;
        return index;
    }

    internal static object? GetObject(ushort index) => _assets[index];

    internal static UnsafeSpan<char> AllocString(ReadOnlySpan<char> value)
    {
        var data = AllocString(value.Length);
        value.CopyTo(data.AsSpan());
        return data;
    }

    internal static UnsafeSpan<char> AllocString(int length)
    {
        if (length <= 0) return UnsafeSpan<char>.Empty;
        var data = AllocData(length * sizeof(char));
        return new UnsafeSpan<char>((char*)data.Ptr, length);
    }

    internal static UnsafeSpan<char> InsertText(ReadOnlySpan<char> text, int start, ReadOnlySpan<char> insert)
    {
        var result = AllocString(text.Length + insert.Length);
        if (start > 0)
            text[..start].CopyTo(result.AsSpan(0, start));
        insert.CopyTo(result.AsSpan(start, insert.Length));
        if (start < text.Length)
            text[start..].CopyTo(result.AsSpan(start + insert.Length, text.Length - start));
        return result;
    }

    internal static UnsafeSpan<char> RemoveText(ReadOnlySpan<char> text, int start, int count)
    {
        if (text.Length - count <= 0)
            return UnsafeSpan<char>.Empty;
        var result = AllocString(text.Length - count);
        if (result.Length == 0) return result;
        text[..start].CopyTo(result.AsSpan(0, start));
        text[(start + count)..].CopyTo(result.AsSpan(start, text.Length - start - count));
        return result;
    }

    internal static UnsafeSpan<byte> AllocData(int count)
    {
        if (count <= 0) return UnsafeSpan<byte>.Empty;
        ref var data = ref _data[_frame & 1];
        if (!data.CheckCapacity(count))
            return UnsafeSpan<byte>.Empty;
        return data.AddRange(count);
    }

    internal static ref T AllocData<T>() where T : unmanaged
    {
        var data = AllocData(sizeof(T));
        ref var data_ref = ref Unsafe.AsRef<T>((T*)data.Ptr);
        data_ref = default;
        return ref data_ref;
    }
}
