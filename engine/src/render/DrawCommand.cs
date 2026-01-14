//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace noz;

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
