//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;
using System.Runtime.InteropServices;
using NoZ.Platform;

namespace NoZ;

[StructLayout(LayoutKind.Sequential)]
public struct UIVertex : IVertex
{
    public Vector2 Position;    // 8 bytes  - location 0
    public Vector2 UV;          // 8 bytes  - location 1
    public Vector2 Normal;      // 8 bytes  - location 2 (expansion direction)
    public Color32 Color;       // 4 bytes  - location 3 (fill color)
    public float BorderRatio;   // 4 bytes  - location 4 (border_width / border_radius, <0 = no SDF)
    public Color32 BorderColor; // 4 bytes  - location 5 (RGB, A unused)

    public const int SizeInBytes = 36;

    public static VertexFormatDescriptor GetFormatDescriptor() => new()
    {
        Stride = SizeInBytes,
        Attributes =
        [
            new VertexAttribute(0, 2, VertexAttribType.Float, 0),                    // Position
            new VertexAttribute(1, 2, VertexAttribType.Float, 8),                    // UV
            new VertexAttribute(2, 2, VertexAttribType.Float, 16),                   // Normal
            new VertexAttribute(3, 4, VertexAttribType.UByte, 24, normalized: true), // Color
            new VertexAttribute(4, 1, VertexAttribType.Float, 28),                   // BorderRatio
            new VertexAttribute(5, 4, VertexAttribType.UByte, 32, normalized: true), // BorderColor
        ]
    };
}
