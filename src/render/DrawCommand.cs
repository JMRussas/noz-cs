//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace noz;

/// <summary>
/// Represents a single mesh submission before batching.
/// Stored in a flat array and sorted by SortKey.
/// </summary>
public struct DrawCommand
{
    public SortKey Key;
    public int VertexOffset;
    public int VertexCount;
    public int IndexOffset;
    public int IndexCount;

    public TextureHandle Texture;
    public ShaderHandle Shader;
}
