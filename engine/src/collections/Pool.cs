//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace NoZ;

public unsafe struct NativePool<T> : IDisposable
    where T : unmanaged
{
    private struct Element
    {
        public bool IsActive;
        public T Value;
    }

    private Element* _elements;
    private readonly int _capacity;
    private int _count;

    public readonly int Capacity => _capacity;
    public readonly int Count => _count;
    public readonly bool CanAdd(int count = 1) => _count + count <= _capacity;

    public NativePool(int capacity)
    {
        _capacity = capacity;
        _count = 0;
        _elements = (Element*)NativeMemory.AllocZeroed((nuint)(capacity * sizeof(Element)));
    }

    public readonly ref T this[int index] => ref _elements[index].Value;

    public ref T Add()
    {
        for (int i = 0; i < _capacity; i++)
        {
            if (!_elements[i].IsActive)
            {
                _elements[i].IsActive = true;
                _elements[i].Value = default;
                _count++;
                return ref _elements[i].Value;
            }
        }

        throw new OverflowException();
    }

    public void Release(ref T item)
    {
        // item is a ref into an Element's Value field within the native buffer.
        // Compute index via pointer arithmetic: offset from buffer start / element stride.
        var byteOffset = (nint)Unsafe.AsPointer(ref item) - (nint)_elements;
        var index = (int)byteOffset / sizeof(Element);
        _elements[index].IsActive = false;
        _elements[index].Value = default;
        _count--;
    }

    public void Clear()
    {
        NativeMemory.Clear(_elements, (nuint)(_capacity * sizeof(Element)));
        _count = 0;
    }

    public readonly Enumerator GetEnumerator() => new(_elements, _capacity);

    public ref struct Enumerator
    {
        private readonly void* _elements;
        private readonly int _capacity;
        private int _index;

        internal Enumerator(void* elements, int capacity)
        {
            _elements = elements;
            _capacity = capacity;
            _index = -1;
        }

        public readonly ref T Current => ref ((Element*)_elements)[_index].Value;

        public bool MoveNext()
        {
            var elements = (Element*)_elements;
            while (++_index < _capacity)
                if (elements[_index].IsActive)
                    return true;

            return false;
        }
    }

    public void Dispose()
    {
        if (_elements == null) return;
        NativeMemory.Free(_elements);
        _elements = null;
    }
}
