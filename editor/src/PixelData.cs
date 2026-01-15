//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Runtime.InteropServices;

namespace NoZ.Editor;

public sealed unsafe class PixelData<T> : IDisposable where T : unmanaged
{
    private void* _memory;
    private readonly UnsafeSpan<T> _pixels;

    public Vector2Int Size { get; }
    public int Width => Size.X;
    public int Height => Size.Y;

    public PixelData(Vector2Int size)
    {
        Size = size;
        var totalPixels = size.X * size.Y;
        var totalSize = sizeof(T) * totalPixels;

        _memory = NativeMemory.AllocZeroed((nuint)totalSize);
        _pixels = new UnsafeSpan<T>((T*)_memory, totalPixels);
    }

    public PixelData(int width, int height) : this(new Vector2Int(width, height))
    {
    }

    ~PixelData()
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

    public ref T this[int index] => ref _pixels[index];

    public ref T this[int x, int y] => ref _pixels[y * Size.X + x];

    public void Clear(T value = default)
    {
        var total = Size.X * Size.Y;
        for (var i = 0; i < total; i++)
            _pixels[i] = value;
    }

    public void Set(int x, int y, T value)
    {
        _pixels[y * Size.X + x] = value;
    }

    public void Set(in Vector2Int position, T value)
    {
        _pixels[position.Y * Size.X + position.X] = value;
    }

    public void Set(in RectInt rect, T value)
    {
        var xe = rect.X + rect.Width;
        var ye = rect.Y + rect.Height;
        for (var y = rect.Y; y < ye; y++)
            for (var x = rect.X; x < xe; x++)
                _pixels[y * Size.X + x] = value;
    }

    public ReadOnlySpan<byte> AsBytes() => new(_memory, Size.X * Size.Y * sizeof(T));
}
