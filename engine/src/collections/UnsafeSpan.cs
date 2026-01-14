//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Diagnostics;

namespace noz;

public readonly unsafe struct UnsafeSpan<T> where T : unmanaged
{
    private readonly T* _ptr;

    public int Length { get; }

    public ref T this[int index] => ref *(_ptr + index);

    public T* GetUnsafePtr() => _ptr;

    public UnsafeSpan(in UnsafeSpan<T> span, int start, int length)
    {
        Debug.Assert(start >= 0 && start + length <= span.Length);
        _ptr = span._ptr + start;
        Length = length;
    }

    public UnsafeSpan(T* ptr, int length)
    {
        _ptr = ptr;
        Length = length;
    }

    public UnsafeSpan(ref byte* ptr, int length)
    {
        _ptr = (T*)ptr;
        Length = length;
        ptr += sizeof(T) * length;
    }

    public UnsafeSpan<T> Slice (int start, int length)
    {
        Debug.Assert(start >= 0 && start + length <= Length);
        return new UnsafeSpan<T>(_ptr + start, length);
    }

    public Enumerator GetEnumerator() => new(_ptr, Length);

    public ref struct Enumerator
    {
        private readonly T* _ptr;
        private readonly int _length;
        private int _index;

        internal Enumerator(T* ptr, int length)
        {
            _ptr = ptr;
            _length = length;
            _index = -1;
        }

        public readonly ref T Current => ref *(_ptr + _index);

        public bool MoveNext()
        {
            _index++;
            return _index < _length;
        }
    }
}

