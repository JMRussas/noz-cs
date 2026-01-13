//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Runtime.InteropServices;

namespace noz.editor;

public sealed unsafe class PixelData : IDisposable
{
    private void* _memory;
    private readonly UnsafeSpan<Color32> _pixels;

    public Vector2Int Size { get; }
    public int Width => Size.X;
    public int Height => Size.Y;

    public PixelData(Vector2Int size)
    {
        Size = size;
        var totalPixels = size.X * size.Y;
        var totalSize = sizeof(Color32) * totalPixels;

        _memory = NativeMemory.AllocZeroed((nuint)totalSize);
        _pixels = new UnsafeSpan<Color32>((Color32*)_memory, totalPixels);
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

    public ref Color32 this[int index] => ref _pixels[index];

    public ref Color32 this[int x, int y] => ref _pixels[y * Size.X + x];

    public Color32* GetUnsafePtr() => _pixels.GetUnsafePtr();

    public void Clear(Color32 color = default)
    {
        var total = Size.X * Size.Y;
        for (var i = 0; i < total; i++)
            _pixels[i] = color;
    }

    public void Set(RectInt rect, Color32 color)
    {
        var xe = rect.X + rect.Width;
        var ye = rect.Y + rect.Height;
        for (var y = rect.Y; y < ye; y++)
        {
            for (var x = rect.X; x < xe; x++)
            {
                _pixels[y * Size.X + x] = color;
            }
        }
    }

    public ReadOnlySpan<byte> AsBytes()
    {
        return new ReadOnlySpan<byte>(_memory, Size.X * Size.Y * sizeof(Color32));
    }
}
