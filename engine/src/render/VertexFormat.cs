//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using NoZ.Platform;

namespace NoZ;

public interface IVertex
{
    static abstract VertexFormatDescriptor GetFormatDescriptor();
}

public static class VertexFormat<T> where T : unmanaged, IVertex
{
    private static nuint _handle;
    private static bool _initialized;

    public static nuint Handle
    {
        get
        {
            if (!_initialized)
            {
                _handle = Render.Driver.CreateVertexFormat(T.GetFormatDescriptor());
                _initialized = true;
            }
            return _handle;
        }
    }
}

public enum VertexAttribType
{
    Float,
    Int,
    UByte
}

public struct VertexAttribute
{
    public int Location;
    public int Components;
    public VertexAttribType Type;
    public int Offset;
    public bool Normalized;

    public VertexAttribute(int location, int components, VertexAttribType type, int offset, bool normalized = false)
    {
        Location = location;
        Components = components;
        Type = type;
        Offset = offset;
        Normalized = normalized;
    }
}
