//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace noz;

/// <summary>
/// Represents a coalesced batch of draw commands that share the same state.
/// Generated after sorting DrawCommands.
/// </summary>
public struct RenderBatch
{
    public int FirstIndex;      // First index in the global index buffer
    public int IndexCount;      // Total indices in this batch
    public TextureHandle Texture;
    public ShaderHandle Shader;
    public BlendMode Blend;
}
