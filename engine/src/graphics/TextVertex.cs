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
    public Color32 Color;
    public Color32 OutlineColor;
    public float OutlineWidth;
    public float OutlineSoftness;

    public static VertexFormatDescriptor GetFormatDescriptor() => new()
    {
        Stride = SizeInBytes,
        Attributes =
        [
            new VertexAttribute(0, 2, VertexAttribType.Float, (int)Marshal.OffsetOf<TextVertex>(nameof(Position))),
            new VertexAttribute(1, 2, VertexAttribType.Float, (int)Marshal.OffsetOf<TextVertex>(nameof(UV))),
            new VertexAttribute(2, 4, VertexAttribType.UByte, (int)Marshal.OffsetOf<TextVertex>(nameof(Color)), true),
            new VertexAttribute(3, 4, VertexAttribType.UByte, (int)Marshal.OffsetOf<TextVertex>(nameof(OutlineColor)), true),
            new VertexAttribute(4, 1, VertexAttribType.Float, (int)Marshal.OffsetOf<TextVertex>(nameof(OutlineWidth))),
            new VertexAttribute(5, 1, VertexAttribType.Float, (int)Marshal.OffsetOf<TextVertex>(nameof(OutlineSoftness))),
        ]
    };
}
