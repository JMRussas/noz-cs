//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Diagnostics;
using System;
using System.Runtime.InteropServices;

namespace noz;

public unsafe struct UnsafeList<T> : IDisposable where T : unmanaged
{
    private T* _ptr;
    private int _capacity;

    public int Length { get; private set; }

    public UnsafeList(T* ptr, int capacity)
    {
        _ptr = ptr;
        _capacity = capacity;
        Length = 0;
    }

    public UnsafeList(ref byte* ptr, int capacity)
    {
        _ptr = (T*)ptr;
        _capacity = capacity;
        Length = 0;

        ptr += sizeof(T) * capacity;
    }

    public UnsafeSpan<T> AsSpan() => new(_ptr, Length);

    public void Add(in T item)
    {
        Debug.Assert(Length < _capacity, "UnsafeList has reached its capacity.");
        _ptr[Length] = item;
        Length++;
    }

    public void Add()
    {
        Debug.Assert(Length < _capacity, "UnsafeList has reached its capacity.");
        Length++;
    }

    public void AddRange(UnsafeSpan<T> items)
    {
        Debug.Assert(Length + items.Length <= _capacity, "UnsafeList has reached its capacity.");
        NativeMemory.Copy(_ptr + Length, items.GetUnsafePtr(), (nuint)(items.Length * sizeof(T)));
        Length += items.Length;
    }

    public void RemoveAt(int index)
    {
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
            Debug.Assert(index >= 0 && index < Length, "Index is out of range.");
            return ref _ptr[index];
        }
    }

    public T* GetUnsafePtr() => _ptr;

    public void Dispose()
    {
        _ptr = null;
        _capacity = 0;
    }
}