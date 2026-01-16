//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;
using System.Runtime.InteropServices;
using NoZ.Platform;

namespace NoZ;

[StructLayout(LayoutKind.Sequential)]
public struct TextVertex : IVertex
{
    public static readonly int SizeInBytes = Marshal.SizeOf<TextVertex>();
    
    public Vector2 Position;
    public Vector2 UV;
    public Color Color;

    public static VertexFormatDescriptor GetFormatDescriptor() => new()
    {
        Stride = SizeInBytes,
        Attributes =
        [
            new VertexAttribute(0, 2, VertexAttribType.Float, (int)Marshal.OffsetOf<TextVertex>(nameof(TextVertex.Position))),
            new VertexAttribute(1, 2, VertexAttribType.Float, (int)Marshal.OffsetOf<TextVertex>(nameof(TextVertex.UV))),
            new VertexAttribute(2, 4, VertexAttribType.Float, (int)Marshal.OffsetOf<TextVertex>(nameof(TextVertex.Color))),
        ]
    };
}
