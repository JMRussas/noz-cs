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
    public Vector2 Position;
    public Vector2 UV;
    public Vector2 Normal;
    public Color Color;
    public float BorderRatio;
    public Color BorderColor;

    public const int SizeInBytes = 36;

    public static VertexFormatDescriptor GetFormatDescriptor() => new()
    {
        Stride = SizeInBytes,
        Attributes =
        [
            new VertexAttribute(
                0,
                2,
                VertexAttribType.Float,
                (int)Marshal.OffsetOf<UIVertex>(nameof(Position))),
            new VertexAttribute(
                1,
                2,
                VertexAttribType.Float,
                (int)Marshal.OffsetOf<UIVertex>(nameof(UV))),
            new VertexAttribute(
                2,
                2,
                VertexAttribType.Float,
                (int)Marshal.OffsetOf<UIVertex>(nameof(Normal))),
            new VertexAttribute(
                3,
                4,
                VertexAttribType.Float,
                (int)Marshal.OffsetOf<UIVertex>(nameof(Color))),
            new VertexAttribute(
                4,
                1,
                VertexAttribType.Float,
                (int)Marshal.OffsetOf<UIVertex>(nameof(BorderRatio))),
            new VertexAttribute(
                5,
                4,
                VertexAttribType.Float,
                (int)Marshal.OffsetOf<UIVertex>(nameof(BorderColor)))
        ]
    };
}
