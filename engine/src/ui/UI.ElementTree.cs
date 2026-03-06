//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace NoZ;

internal enum NewElementType : byte
{
    Widget,
    Size,
    Padding,
}

internal struct BaseElement
{
    public NewElementType Type;
    public ushort Parent;
    public ushort NextSibling;
    public ushort FirstChild;
    public ushort ChildCount;
}

internal struct SizeElement
{
    public Size2 Size;
}

internal struct PaddingElement
{
    public EdgeInsets Padding;
}

internal struct WidgetElement
{
    public int Id;
    public ushort Data;
    public ElementFlags Flags;
}

public static partial class UI
{
    public static unsafe class ElementTree
    {
        private const int MaxStateSize = 65535;
        private const int MaxElementSize = 65535;
        private const int MaxElementDepth = 64;
        private const int MaxId = 32000;

        private static NativeArray<byte> _elements;
        private static NativeArray<byte> _state;
        private static NativeArray<ushort> _elementStack;
        private static NativeArray<ushort> _widgets;
        private static int _elementStackCount;
        private static ushort _frame;
        private static ushort _nextSibling;
        private static int _stateOffset;
        private static ushort _currentWidget;

        public static void Init()
        {
            _elements = new NativeArray<byte>(MaxElementSize);
            _state = new NativeArray<byte>(MaxStateSize * 2, MaxStateSize * 2);
            _elementStack = new NativeArray<ushort>(MaxElementDepth);
            _widgets = new NativeArray<ushort>(MaxId, MaxId);
        }

        internal static void Begin()
        {
            _frame++;
            _stateOffset = (_frame & 1) * MaxStateSize;
        }

        internal static void End()
        {
        }

        private static UnsafeSpan<byte> GetState(int offset) =>
            _state.AsUnsafeSpan(_stateOffset + offset, MaxStateSize - offset);

        private static ref BaseElement GetElement(int offset) =>
            ref *(BaseElement*)(_elements.Ptr + offset);
        
        private static UnsafeRef<T> GetElementData<T>(int offset) where T : unmanaged =>
            new((T*)(_elements.Ptr + offset + sizeof(BaseElement)));

        private static ref T GetElementData<T>(ref BaseElement element) where T : unmanaged =>
             ref *(T*)((byte*)Unsafe.AsPointer(ref element) + sizeof(BaseElement));

        private static ref BaseElement AllocElement<T>(NewElementType type) where T : unmanaged 
        {
            var size = sizeof(T) + sizeof(BaseElement);
            if (!_elements.CheckCapacity(size))
                throw new InvalidOperationException($"Element tree exceeded maximum size of {MaxElementSize} bytes.");

            return ref *(BaseElement*)_elements.AddRange(size).GetUnsafePtr();
        }

        private static ref BaseElement BeginElement<T>(NewElementType type) where T : unmanaged
        {
            ref var e = ref AllocElement<T>(type);
            BeginElementInternal(type, ref e);
            _elementStack.Add((ushort)GetOffset(ref e));
            return ref e;
        }

        private static void EndElement(NewElementType type)
        {
            Debug.Assert(_elementStackCount > 0);
            var elementOffset = _elementStack[--_elementStackCount];
            _nextSibling = elementOffset;
            ref var e = ref GetElement(elementOffset);
            e.NextSibling = (ushort)_elements.Length;
            Debug.Assert(e.Type == type);                
        }

        private static void BeginElementInternal(NewElementType type, ref BaseElement e)
        { 
            e.Type = type;
            e.Parent = _elementStack.Length > 0 ? _elementStack[^1] : (ushort)0;
            e.NextSibling = 0;
            if (e.Parent != 0)
            {
                ref var p = ref GetElement(e.Parent);
                p.ChildCount++;
                if (p.FirstChild == 0)
                    p.FirstChild = (ushort)((byte*)Unsafe.AsPointer(ref e) - _elements.Ptr);
            }
        }

        private static int GetOffset(ref BaseElement element)
        {
            var offset = (byte*)Unsafe.AsPointer(ref element) - _elements.Ptr;
            Debug.Assert(offset >= 0);
            Debug.Assert(offset < MaxElementSize);
            return (int)offset;
        }

        public static int Size(Size width, Size height) => Size(new Size2(width, height));

        public static int Size(Size2 size)
        {
            ref var e = ref BeginElement<SizeElement>(NewElementType.Size);
            ref var d = ref GetElementData<SizeElement>(ref e);
            d.Size = size;
            return GetOffset(ref e);
        }

        public static int BeginPadding(EdgeInsets padding)
        {
            ref var e = ref BeginElement<PaddingElement>(NewElementType.Padding);
            ref var d = ref GetElementData<PaddingElement>(ref e);
            d.Padding = padding;
            return GetOffset(ref e);
        }

        public static void EndPadding()
        {
            
        }

        private static ref WidgetElement GetCurrentWidget()
        {
            Debug.Assert(_currentWidget != 0);
            ref var e = ref GetElement(_currentWidget);
            Debug.Assert(e.Type == NewElementType.Widget);
            return ref GetElementData<WidgetElement>(ref e);
        }

        public static bool IsHovered() => GetCurrentWidget().Flags.HasFlag(ElementFlags.Hovered);
        public static bool WasPressed() => GetCurrentWidget().Flags.HasFlag(ElementFlags.Pressed);

        public static int BeginWidget<T>(int id) where T : unmanaged
        {
            var offset = BeginWidget(id); 
            ref var e = ref GetElement(offset);
            ref var d = ref GetElementData<WidgetElement>(ref e);
            var wd = _elements.AddRange(sizeof(T));
            d.Data = (ushort)(wd.GetUnsafePtr() - _elements.Ptr);
            return offset;
        }

        public static int BeginWidget(int id)
        {
            ref var e = ref BeginElement<WidgetElement>(NewElementType.Widget);   
            ref var d = ref GetElementData<WidgetElement>(ref e);
            var offset = (ushort)GetOffset(ref e);
            d.Id = id;
            d.Data = 0;
            _widgets[id] = offset;
            _currentWidget = offset;
            return offset;
        }

        public static void EndWidget()
        {
            EndElement(NewElementType.Widget);

            _currentWidget = 0;
            for (int i = _elementStackCount - 1; i >= 0; i--)
            {
                ref var e = ref GetElement(_elementStack[i]);
                if (e.Type == NewElementType.Widget)
                {
                    _currentWidget = _elementStack[i];
                    break;
                }
            }
        }

        private static void MeasureSizeElement(ref BaseElement e, Vector2 available)
        {
            ref var d = ref GetElementData<SizeElement>(ref e);
        }

        private static void MeasurePaddingElement(ref BaseElement e, Vector2 available)
        {
            ref var d = ref GetElementData<PaddingElement>(ref e);
        }

        private static void MeasureElement(int elementOffset, Vector2 available)
        {
            ref var e = ref GetElement(elementOffset);
            if (e.Type == NewElementType.Size)
                MeasureSizeElement(ref e, available);
            else if (e.Type == NewElementType.Padding)
                MeasurePaddingElement(ref e, available);
        }
    }
}
