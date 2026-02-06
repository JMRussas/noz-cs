//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

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

    public void Clear(in RectInt rect, T value = default)
    {
        for (var y = rect.Y; y < rect.Y + rect.Height; y++ )
            for (var x = rect.X; x < rect.X + rect.Width; x++)
                _pixels[y * Size.X + x] = value;
    }

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

    public ReadOnlySpan<byte> AsByteSpan() => new(_memory, Size.X * Size.Y * sizeof(T));

    /// <summary>
    /// Extrudes edge pixels into a 1-pixel padding around the given inner rect.
    /// Used for texture atlases to prevent seams with point filtering.
    /// </summary>
    public void ExtrudeEdges(in RectInt rect)
    {
        var x0 = rect.X;
        var y0 = rect.Y;
        var x1 = rect.X + rect.Width - 1;
        var y1 = rect.Y + rect.Height - 1;

        // Top and bottom edges
        for (var x = x0; x <= x1; x++)
        {
            this[x, y0] = this[x, y0 + 1];
            this[x, y1] = this[x, y1 - 1];
        }

        // Left and right edges
        for (var y = y0; y <= y1; y++)
        {
            this[x0, y] = this[x0 + 1, y];
            this[x1, y] = this[x1 - 1, y];
        }

        // Corners
        this[x0, y0] = this[x0 + 1, y0 + 1];
        this[x1, y0] = this[x1 - 1, y0 + 1];
        this[x0, y1] = this[x0 + 1, y1 - 1];
        this[x1, y1] = this[x1 - 1, y1 - 1];
    }
}

public static class PixelDataExtensions
{
    /// <summary>
    /// Bleeds RGB color from non-transparent pixels into neighboring transparent pixels.
    /// This prevents black fringing when using linear filtering with anti-aliased sprites.
    /// Call this BEFORE ExtrudeEdges for proper atlas padding.
    /// </summary>
    public static void BleedColors(this PixelData<Color32> pixels, in RectInt rect, int iterations = 8)
    {
        var x0 = rect.X;
        var y0 = rect.Y;
        var x1 = rect.X + rect.Width;
        var y1 = rect.Y + rect.Height;

        for (var iter = 0; iter < iterations; iter++)
        {
            var changed = 0;

            Parallel.For(y0, y1, y =>
            {
                var localChanged = false;

                for (var x = x0; x < x1; x++)
                {
                    ref var pixel = ref pixels[x, y];
                    if (pixel.A != 0 || (pixel.R | pixel.G | pixel.B) != 0) continue;

                    var sumR = 0;
                    var sumG = 0;
                    var sumB = 0;
                    var count = 0;

                    for (var dy = -1; dy <= 1; dy++)
                    {
                        var ny = y + dy;
                        if (ny < y0 || ny >= y1) continue;

                        for (var dx = -1; dx <= 1; dx++)
                        {
                            if (dx == 0 && dy == 0) continue;
                            var nx = x + dx;
                            if (nx < x0 || nx >= x1) continue;

                            ref var neighbor = ref pixels[nx, ny];
                            if (neighbor.A == 0 && (neighbor.R | neighbor.G | neighbor.B) == 0) continue;

                            sumR += neighbor.R;
                            sumG += neighbor.G;
                            sumB += neighbor.B;
                            count++;
                        }
                    }

                    if (count > 0)
                    {
                        pixel = new Color32(
                            (byte)(sumR / count),
                            (byte)(sumG / count),
                            (byte)(sumB / count),
                            0);
                        localChanged = true;
                    }
                }

                if (localChanged)
                    Interlocked.Exchange(ref changed, 1);
            });

            if (changed == 0) break;
        }
    }
}
