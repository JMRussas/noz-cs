//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;
using System.Runtime.InteropServices;
using NoZ.Platform;

namespace NoZ;

[StructLayout(LayoutKind.Sequential)]
public struct MeshVertex : IVertex
{
    public Vector2 Position;    // 8 bytes
    public Vector2 UV;          // 8 bytes
    public Vector2 Normal;      // 8 bytes
    public Color Color;         // 16 bytes (RGBA floats 0-1)
    public int Bone;            // 4 bytes (bone index for skinning, 0 = identity)
    public int Atlas;           // 4 bytes (texture array index, 0 = white)
    public int FrameCount;      // 4 bytes (animation frame count, 1 = static)
    public float FrameWidth;    // 4 bytes (frame width in UV space)
    public float FrameRate;     // 4 bytes (frames per second)
    public float AnimStartTime; // 4 bytes (animation start time offset)

    public const int SizeInBytes = 64;

    public MeshVertex(Vector2 position, Vector2 uv, Color color, int atlas = 0)
    {
        Position = position;
        UV = uv;
        Normal = Vector2.Zero;
        Color = color;
        Bone = 0;
        Atlas = atlas;
        FrameCount = 1;
        FrameWidth = 0;
        FrameRate = 0;
        AnimStartTime = 0;
    }

    public MeshVertex(float x, float y, float u, float v, Color color, int atlas = 0)
    {
        Position = new Vector2(x, y);
        UV = new Vector2(u, v);
        Normal = Vector2.Zero;
        Color = color;
        Bone = 0;
        Atlas = atlas;
        FrameCount = 1;
        FrameWidth = 0;
        FrameRate = 0;
        AnimStartTime = 0;
    }

    public static readonly MeshVertex Default = new()
    {
        Position = Vector2.Zero,
        UV = Vector2.Zero,
        Normal = Vector2.Zero,
        Color = Color.White,
        Bone = 0,
        Atlas = 0,
        FrameCount = 1,
        FrameWidth = 0,
        FrameRate = 0,
        AnimStartTime = 0
    };

    public static VertexFormatDescriptor GetFormatDescriptor() => new()
    {
        Stride = SizeInBytes,
        Attributes =
        [
            new VertexAttribute(0, 2, VertexAttribType.Float, 0),      // Position
            new VertexAttribute(1, 2, VertexAttribType.Float, 8),      // UV
            new VertexAttribute(2, 2, VertexAttribType.Float, 16),     // Normal
            new VertexAttribute(3, 4, VertexAttribType.Float, 24),     // Color
            new VertexAttribute(4, 1, VertexAttribType.Int, 40),       // Bone
            new VertexAttribute(5, 1, VertexAttribType.Int, 44),       // Atlas
            new VertexAttribute(6, 1, VertexAttribType.Int, 48),       // FrameCount
            new VertexAttribute(7, 1, VertexAttribType.Float, 52),     // FrameWidth
            new VertexAttribute(8, 1, VertexAttribType.Float, 56),     // FrameRate
            new VertexAttribute(9, 1, VertexAttribType.Float, 60),     // AnimStartTime
        ]
    };
}
