//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Diagnostics;
using System.Runtime.InteropServices;

namespace NoZ;

public unsafe struct NativeArray<T> : IDisposable
    where T : unmanaged
{
    [StructLayout(LayoutKind.Sequential)]
    private struct Header
    {
        public int Length;
        public int Capacity;
    }

    private void* _ptr;
    public readonly bool IsCreated => _ptr != null;

    public readonly T* Ptr => (T*)(((Header*)_ptr) + 1);

    public readonly int Length
    {
        get
        {
            if (!IsCreated) return 0;
            return ((Header*)_ptr)->Length;
        }
        private set
        {
            Debug.Assert(Length <= Capacity);
            ((Header*)_ptr)->Length = value;
        }
    }
    public readonly int Capacity
    {
        get
        {
            if (!IsCreated) return 0;
            return ((Header*)_ptr)->Capacity;
        }
        private set
        {
            Debug.Assert(IsCreated);
            ((Header*)_ptr)->Capacity = value;
        }
    }

    public NativeArray(int capacity, int length = 0)
    {
        Debug.Assert(capacity >= 0);
        Debug.Assert(length >= 0 && length <= capacity);
        _ptr = NativeMemory.Alloc((nuint)(sizeof(Header) + sizeof(T) * capacity));
        ((Header*)_ptr)->Capacity = capacity;
        ((Header*)_ptr)->Length = length;
    }

    public NativeArray(NativeArray<T> other) : this(other.Capacity, other.Length)
    {
        Debug.Assert(IsCreated);

        if (other.Length == 0)
            return;

        fixed (T* otherPtr = other.AsSpan())
            NativeMemory.Copy(otherPtr, Ptr, (nuint)(sizeof(Header) + other.Length * sizeof(T)));
    }

    public static implicit operator ReadOnlySpan<T>(NativeArray<T> a) => a.AsReadonlySpan();

    public readonly Span<T> AsSpan() => new(Ptr, Length);
    public readonly Span<T> AsSpan(int start, int count) => new(Ptr + start, count);
    public readonly Span<byte> AsByteSpan() => new((byte*)Ptr, Length * sizeof(T));
    public readonly ReadOnlySpan<T> AsReadonlySpan() => IsCreated ? new(Ptr, Length) : [];
    public readonly ReadOnlySpan<T> AsReadonlySpan(int start, int count) => new(Ptr + start, count);
    public readonly ReadOnlySpan<T> AsReadonlyByteSpan() => new(Ptr, Length);
    
    public readonly UnsafeSpan<T> AsUnsafeSpan(int start, int count) => new(Ptr + start, count);

    public readonly bool CheckCapacity(int additionalCount)
    {
        Debug.Assert(IsCreated);
        return Length + additionalCount <= Capacity;
    }

    public ref T Add()
    {
        Debug.Assert(IsCreated);
        Debug.Assert(Length < Capacity, "NativeArray has reached its capacity.");
        return ref Ptr[Length++];
    }
    
    public void Add(in T item)
    {
        Debug.Assert(IsCreated);
        Debug.Assert(Length < Capacity, "NativeArray has reached its capacity.");
        Ptr[Length++] = item;
    }

    public UnsafeSpan<T> AddRange(in ReadOnlySpan<T> elements)
    {
        Debug.Assert(IsCreated);
        Debug.Assert(Length + elements.Length <= Capacity, "UnsafeList has reached its capacity.");
        fixed(T* elementsPtr = elements)
            NativeMemory.Copy(elementsPtr, Ptr + Length, (nuint)(elements.Length * sizeof(T)));
        var span = new UnsafeSpan<T>(Ptr + Length, elements.Length);
        Length += elements.Length;
        return span;
    }

    public UnsafeSpan<T> AddRange(int count)
    {
        Debug.Assert(IsCreated);
        Debug.Assert(Length + count <= Capacity, "UnsafeList has reached its capacity.");
        var span = new UnsafeSpan<T>(Ptr + Length, count);
        Length += count;
        return span;
    }

    public void RemoveAt(int index)
    {
        Debug.Assert(IsCreated);
        Debug.Assert(index >= 0 && index < Length, "Index is out of range.");
        if (index < Length - 1)
            NativeMemory.Copy(Ptr + index, Ptr + index + 1, (nuint)((Length - index - 1) * sizeof(T)));
        Length--;
    }

    public void RemoveLast(int count)
    {
        Debug.Assert(IsCreated);
        Debug.Assert(Length >= count, "NativeArray is empty.");
        Length-=count;
    }

    public void Clear()
    {
        Length = 0;
    }

    public ref T this[int index]
    {
        get
        {
            Debug.Assert(IsCreated);
            Debug.Assert(index >= 0 && index < Length, "Index is out of range.");
            return ref Ptr[index];
        }
    }
    
    public void Dispose()
    {
        if (!IsCreated) return;
        NativeMemory.Free(_ptr);
        _ptr = null;
    }
}