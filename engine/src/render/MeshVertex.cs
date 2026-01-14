//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;
using System.Runtime.InteropServices;

namespace noz;

/// <summary>
/// Vertex format for mesh batching. Must be blittable for GPU upload.
/// Layout (68 bytes):
///   location 0: vec2 position
///   location 1: vec2 uv
///   location 2: vec2 normal
///   location 3: vec4 color
///   location 4: float opacity
///   location 5: int bone
///   location 6: int atlas
///   location 7: int frameCount
///   location 8: float frameWidth
///   location 9: float frameRate
///   location 10: float animStartTime
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct MeshVertex
{
    public Vector2 Position;    // 8 bytes
    public Vector2 UV;          // 8 bytes
    public Vector2 Normal;      // 8 bytes
    public Vector4 Color;       // 16 bytes (RGBA floats 0-1)
    public float Opacity;       // 4 bytes
    public int Bone;            // 4 bytes (bone index for skinning, 0 = identity)
    public int Atlas;           // 4 bytes (texture array index, 0 = white)
    public int FrameCount;      // 4 bytes (animation frame count, 1 = static)
    public float FrameWidth;    // 4 bytes (frame width in UV space)
    public float FrameRate;     // 4 bytes (frames per second)
    public float AnimStartTime; // 4 bytes (animation start time offset)

    public const int SizeInBytes = 68;

    public MeshVertex(Vector2 position, Vector2 uv, Vector4 color, int atlas = 0)
    {
        Position = position;
        UV = uv;
        Normal = Vector2.Zero;
        Color = color;
        Opacity = 1.0f;
        Bone = 0;
        Atlas = atlas;
        FrameCount = 1;
        FrameWidth = 0;
        FrameRate = 0;
        AnimStartTime = 0;
    }

    public MeshVertex(float x, float y, float u, float v, Vector4 color, int atlas = 0)
    {
        Position = new Vector2(x, y);
        UV = new Vector2(u, v);
        Normal = Vector2.Zero;
        Color = color;
        Opacity = 1.0f;
        Bone = 0;
        Atlas = atlas;
        FrameCount = 1;
        FrameWidth = 0;
        FrameRate = 0;
        AnimStartTime = 0;
    }

    public MeshVertex(float x, float y, float u, float v, Color32 color, int atlas = 0)
    {
        Position = new Vector2(x, y);
        UV = new Vector2(u, v);
        Normal = Vector2.Zero;
        Color = new Vector4(color.R / 255f, color.G / 255f, color.B / 255f, color.A / 255f);
        Opacity = 1.0f;
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
        Color = Vector4.One,
        Opacity = 1.0f,
        Bone = 0,
        Atlas = 0,
        FrameCount = 1,
        FrameWidth = 0,
        FrameRate = 0,
        AnimStartTime = 0
    };
}
