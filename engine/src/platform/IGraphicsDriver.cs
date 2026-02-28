//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ.Platform;

public struct VertexFormatDescriptor
{
    public VertexAttribute[] Attributes;
    public int Stride;
}

public enum BufferUsage
{
    Static,     // Data set once
    Dynamic,    // Data updated frequently
    Stream      // Data updated every frame
}

public class GraphicsDriverConfig
{
    public required IPlatform Platform { get; set; }
    public bool VSync { get; set; } = true;
}

public interface IGraphicsDriver
{
    string ShaderExtension { get; }

    void Init(GraphicsDriverConfig config);
    void Shutdown();

    bool BeginFrame();
    void EndFrame();

    void SetViewport(in RectInt viewport);
    void SetScissor(in RectInt scissor);
    void ClearScissor();

    nuint CreateMesh<T>(int maxVertices, int maxIndices, BufferUsage usage, string name = "") where T : IVertex;
    void DestroyMesh(nuint handle);
    void BindMesh(nuint handle);
    void UpdateMesh(nuint handle, ReadOnlySpan<byte> vertexData, ReadOnlySpan<ushort> indexData);

    nuint CreateUniformBuffer(int sizeInBytes, BufferUsage usage, string name = "");
    void DestroyBuffer(nuint handle);
    void UpdateUniformBuffer(nuint buffer, int offsetBytes, ReadOnlySpan<byte> data);
    void BindUniformBuffer(nuint buffer, int slot);

    nuint CreateTexture(int width, int height, ReadOnlySpan<byte> data, TextureFormat format = TextureFormat.RGBA8, TextureFilter filter = TextureFilter.Linear, string? name=null);
    void UpdateTexture(nuint handle, in Vector2Int size, ReadOnlySpan<byte> data);
    void UpdateTextureRegion(nuint handle, in RectInt region, ReadOnlySpan<byte> data, int srcWidth = -1);
    void DestroyTexture(nuint handle);
    void BindTexture(nuint handle, int slot, TextureFilter filter = TextureFilter.Point);

    nuint CreateTextureArray(int width, int height, int layers);
    nuint CreateTextureArray(int width, int height, byte[][] layerData, TextureFormat format, TextureFilter filter, string? name=null);
    void UpdateTextureLayer(nuint handle, int layer, ReadOnlySpan<byte> data);

    nuint CreateShader(string name, string vertexSource, string fragmentSource, List<ShaderBinding> bindings);
    void DestroyShader(nuint handle);
    void BindShader(nuint handle);
    void SetShaderFlags(nuint handle, ShaderFlags flags) { }

    void SetBlendMode(BlendMode mode);
    void SetTextureFilter(TextureFilter filter);
    void SetUniform(string name, ReadOnlySpan<byte> data);

    void SetGlobalsCount(int count);
    void SetGlobals(int index, ReadOnlySpan<byte> data);
    void BindGlobals(int index);

    void DrawElements(int firstIndex, int indexCount, int baseVertex = 0);

    void SetVSync(bool vsync) { }

    nuint CreateFence();
    void WaitFence(nuint fence);
    void DeleteFence(nuint fence);

    void BeginScenePass(Color clearColor);
    void ResumeScenePass();
    void EndScenePass();

    // Render Texture support (BGRA8 default matches swap chain format for pipeline compatibility)
    nuint CreateRenderTexture(int width, int height, TextureFormat format = TextureFormat.BGRA8, int sampleCount = 1, string? name = null);
    void DestroyRenderTexture(nuint handle);
    void BeginRenderTexturePass(nuint renderTexture, Color clearColor);
    void EndRenderTexturePass();
    Task<byte[]> ReadRenderTexturePixelsAsync(nuint renderTexture);
}
