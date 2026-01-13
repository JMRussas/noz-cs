//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace noz;

public readonly unsafe struct UnsafeRef<T> where T : unmanaged
{
    private readonly T* _ptr;

    public UnsafeRef(T* ptr)
    {
        _ptr = ptr;
    }

    public UnsafeRef(in UnsafeSpan<T> span, int index)
    {
        Debug.Assert(index >= 0 && index < span.Length, "Index is out of bounds for the given span.");
        _ptr = span.GetUnsafePtr() + index;
    }

    /// <summary>
    /// True if the ref is non null
    /// </summary>
    public bool IsValid => _ptr != null;

    /// <summary>
    /// True if the ref is null
    /// </summary>
    public bool IsNull => _ptr == null;

    /// <summary>
    /// Return the pointer to the underlying data.
    /// </summary>
    public ref T AsRef() => ref Unsafe.AsRef<T>(_ptr);

    /// <summary>
    /// Return an unsafe pointer.
    /// </summary>
    public T* AsUnsafePtr() => _ptr;
}
