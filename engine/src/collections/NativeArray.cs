//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Diagnostics;
using System.Runtime.InteropServices;

namespace NoZ;

public unsafe struct NativeArray<T>(int capacity) : IDisposable
    where T : unmanaged
{
    private T* _ptr = (T*)NativeMemory.Alloc((nuint)sizeof(T), (nuint)capacity);
    public int Length { get; private set; } = 0;
    public int Capacity { get; private set; } = capacity;

    public Span<T> AsSpan() => new(_ptr, Length);
    public Span<byte> AsByteSpan() => new((byte*)_ptr, Length * sizeof(T));
    public ReadOnlySpan<T> AsReadonlySpan() => new(_ptr, Length);
    public ReadOnlySpan<T> AsReadonlySpan(int start, int count) => new(_ptr + start, count);
    public ReadOnlySpan<T> AsReadonlyByteSpan() => new(_ptr, Length);

    public ref T Add()
    {
        Debug.Assert(_ptr != null);
        Debug.Assert(Length < Capacity, "NativeArray has reached its capacity.");
        return ref _ptr[Length++];
    }
    
    public void Add(in T item)
    {
        Debug.Assert(_ptr != null);
        Debug.Assert(Length < Capacity, "NativeArray has reached its capacity.");
        _ptr[Length++] = item;
    }

    public void AddRange(in ReadOnlySpan<T> elements)
    {
        Debug.Assert(_ptr != null);
        Debug.Assert(Length + elements.Length <= Capacity, "UnsafeList has reached its capacity.");
        fixed(T* elementsPtr = elements)
            NativeMemory.Copy(elementsPtr, _ptr + Length, (nuint)(elements.Length * sizeof(T)));
        Length += elements.Length;
    }

    public void RemoveAt(int index)
    {
        Debug.Assert(_ptr != null);
        Debug.Assert(index >= 0 && index < Length, "Index is out of range.");
        if (index < Length - 1)
            NativeMemory.Copy(_ptr + index, _ptr + index + 1, (nuint)((Length - index - 1) * sizeof(T)));
        Length--;
    }

    public void Clear()
    {
        Length = 0;
    }

    public ref T this[int index]
    {
        get
        {
            Debug.Assert(_ptr != null);
            Debug.Assert(index >= 0 && index < Length, "Index is out of range.");
            return ref _ptr[index];
        }
    }
    
    public void Dispose()
    {
        NativeMemory.Free(_ptr);
        _ptr = null;
        Capacity = 0;
        Length = 0;
    }
}