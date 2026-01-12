//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;
using System.Runtime.InteropServices;

namespace noz;

/// <summary>
/// Vertex format for mesh batching. Must be blittable for GPU upload.
/// Matches C++ MeshVertex layout (56 bytes):
///   location 0: vec2 position
///   location 1: vec2 uv
///   location 2: vec2 normal
///   location 3: vec4 color
///   location 4: float opacity
///   location 5: float depth
///   location 6: int bone
///   location 7: int atlas
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct MeshVertex
{
    public Vector2 Position;    // 8 bytes
    public Vector2 UV;          // 8 bytes
    public Vector2 Normal;      // 8 bytes
    public Vector4 Color;       // 16 bytes (RGBA floats 0-1)
    public float Opacity;       // 4 bytes
    public float Depth;         // 4 bytes
    public int Bone;            // 4 bytes (bone index for skinning)
    public int Atlas;           // 4 bytes (texture array index)

    // Total: 56 bytes per vertex
    public const int SizeInBytes = 56;

    public MeshVertex(Vector2 position, Vector2 uv, Vector4 color, float depth = 0, int atlas = 0)
    {
        Position = position;
        UV = uv;
        Normal = Vector2.Zero;
        Color = color;
        Opacity = 1.0f;
        Depth = depth;
        Bone = 0;
        Atlas = atlas;
    }

    public MeshVertex(float x, float y, float u, float v, Vector4 color, float depth = 0, int atlas = 0)
    {
        Position = new Vector2(x, y);
        UV = new Vector2(u, v);
        Normal = Vector2.Zero;
        Color = color;
        Opacity = 1.0f;
        Depth = depth;
        Bone = 0;
        Atlas = atlas;
    }

    /// <summary>
    /// Create a vertex with Color32 (will be converted to float color).
    /// </summary>
    public MeshVertex(float x, float y, float u, float v, Color32 color, float depth = 0, int atlas = 0)
    {
        Position = new Vector2(x, y);
        UV = new Vector2(u, v);
        Normal = Vector2.Zero;
        Color = new Vector4(color.R / 255f, color.G / 255f, color.B / 255f, color.A / 255f);
        Opacity = 1.0f;
        Depth = depth;
        Bone = 0;
        Atlas = atlas;
    }

    public static readonly MeshVertex Default = new()
    {
        Position = Vector2.Zero,
        UV = Vector2.Zero,
        Normal = Vector2.Zero,
        Color = Vector4.One,
        Opacity = 1.0f,
        Depth = 0,
        Bone = 0,
        Atlas = 0
    };
}
