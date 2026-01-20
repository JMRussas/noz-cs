//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;
using System.Runtime.InteropServices;
using NoZ.Platform;

namespace NoZ;

[StructLayout(LayoutKind.Sequential)]
public struct MeshVertex(float x, float y, float u, float v, Color color, int atlas = 0)
    : IVertex
{
    public Vector2 Position = new(x, y);
    public Vector2 UV = new(u, v);
    public Vector2 Normal = Vector2.Zero;
    public Color Color = color;
    public int Bone = 0;
    public int Atlas = atlas;
    public int FrameCount = 1;
    public float FrameWidth = 0;
    public float FrameRate = 0;
    public float AnimStartTime = 0;

    public static readonly int SizeInBytes = Marshal.SizeOf<MeshVertex>();

    public static VertexFormatDescriptor GetFormatDescriptor() => new()
    {
        Stride = SizeInBytes,
        Attributes =
        [
            new VertexAttribute(
                0,
                2,
                VertexAttribType.Float,
                (int)Marshal.OffsetOf<MeshVertex>(nameof(Position))),

            new VertexAttribute(
                1,
                2,
                VertexAttribType.Float,
                (int)Marshal.OffsetOf<MeshVertex>(nameof(UV))),

            new VertexAttribute(
                2,
                2,
                VertexAttribType.Float,
                (int)Marshal.OffsetOf<MeshVertex>(nameof(Normal))),

            new VertexAttribute(
                3,
                4,
                VertexAttribType.Float,
                (int)Marshal.OffsetOf<MeshVertex>(nameof(Color))),

            new VertexAttribute(
                4,
                1,
                VertexAttribType.Int,
                (int)Marshal.OffsetOf<MeshVertex>(nameof(Bone))),

            new VertexAttribute(
                5,
                1,
                VertexAttribType.Int,
                (int)Marshal.OffsetOf<MeshVertex>(nameof(Atlas))),

            new VertexAttribute(
                6,
                1,
                VertexAttribType.Int,
                (int)Marshal.OffsetOf<MeshVertex>(nameof(FrameCount))),

            new VertexAttribute(
                7,
                1,
                VertexAttribType.Float,
                (int)Marshal.OffsetOf<MeshVertex>(nameof(FrameWidth))),

            new VertexAttribute(
                8,
                1,
                VertexAttribType.Float,
                (int)Marshal.OffsetOf<MeshVertex>(nameof(FrameRate))),

            new VertexAttribute(
                9,
                1,
                VertexAttribType.Float,
                (int)Marshal.OffsetOf<MeshVertex>(nameof(AnimStartTime)))
        ]
    };
}
