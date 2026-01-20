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

    void BeginFrame();
    void EndFrame();

    void Clear(Color color);
    void SetViewport(int x, int y, int width, int height);
    void SetScissor(int x, int y, int width, int height);
    void DisableScissor();

    nuint CreateMesh<T>(int maxVertices, int maxIndices, BufferUsage usage, string name = "") where T : IVertex;
    void DestroyMesh(nuint handle);
    void BindMesh(nuint handle);
    void UpdateMesh(nuint handle, ReadOnlySpan<byte> vertexData, ReadOnlySpan<ushort> indexData);

    nuint CreateUniformBuffer(int sizeInBytes, BufferUsage usage, string name = "");
    void DestroyBuffer(nuint handle);
    void UpdateUniformBuffer(nuint buffer, int offsetBytes, ReadOnlySpan<byte> data);
    void BindUniformBuffer(nuint buffer, int slot);

    nuint CreateTexture(int width, int height, ReadOnlySpan<byte> data, TextureFormat format = TextureFormat.RGBA8, TextureFilter filter = TextureFilter.Linear);
    void UpdateTexture(nuint handle, int width, int height, ReadOnlySpan<byte> data);
    void DestroyTexture(nuint handle);
    void BindTexture(nuint handle, int slot);

    nuint CreateTextureArray(int width, int height, int layers);
    nuint CreateTextureArray(int width, int height, byte[][] layerData, TextureFormat format, TextureFilter filter, string? name=null);
    void UpdateTextureLayer(nuint handle, int layer, ReadOnlySpan<byte> data);

    nuint CreateShader(string name, string vertexSource, string fragmentSource);
    void DestroyShader(nuint handle);
    void BindShader(nuint handle);
    void SetUniformMatrix4x4(string name, in Matrix4x4 value);
    void SetUniformInt(string name, int value);
    void SetUniformFloat(string name, float value);
    void SetUniformVec2(string name, Vector2 value);
    void SetUniformVec4(string name, Vector4 value);

    void SetBlendMode(BlendMode mode);

    void DrawElements(int firstIndex, int indexCount, int baseVertex = 0);

    nuint CreateFence();
    void WaitFence(nuint fence);
    void DeleteFence(nuint fence);

    void ResizeOffscreenTarget(int width, int height, int msaaSamples);
    void BeginScenePass(Color clearColor);
    void EndScenePass();
    void Composite(nuint compositeShader);
}
