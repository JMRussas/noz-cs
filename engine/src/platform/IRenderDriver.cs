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

public class RenderDriverConfig
{
    public bool VSync { get; set; } = true;
}

public interface IRenderDriver
{
    string ShaderExtension { get; }

    void Init(RenderDriverConfig config);
    void Shutdown();

    void BeginFrame();
    void EndFrame();

    void Clear(Color color);
    void SetViewport(int x, int y, int width, int height);
    void SetScissor(int x, int y, int width, int height);
    void DisableScissor();

    nuint CreateVertexBuffer(int sizeInBytes, BufferUsage usage, string name = "");
    nuint CreateIndexBuffer(int sizeInBytes, BufferUsage usage, string name = "");
    nuint CreateUniformBuffer(int sizeInBytes, BufferUsage usage, string name = "");
    void DestroyBuffer(nuint handle);

    void UpdateVertexBuffer(nuint buffer, int offsetBytes, ReadOnlySpan<byte> data);
    void UpdateIndexBuffer(nuint buffer, int offsetBytes, ReadOnlySpan<ushort> data);
    void UpdateUniformBuffer(nuint buffer, int offsetBytes, ReadOnlySpan<byte> data);

    nuint CreateVertexFormat(in VertexFormatDescriptor descriptor, string? name=null);
    void DestroyVertexFormat(nuint handle);
    void BindVertexFormat(nuint format);

    void BindVertexBuffer(nuint buffer);
    void BindIndexBuffer(nuint buffer);
    void BindUniformBuffer(nuint buffer, int slot);

    nuint CreateTexture(int width, int height, ReadOnlySpan<byte> data, TextureFormat format = TextureFormat.RGBA8);
    void UpdateTexture(nuint handle, int width, int height, ReadOnlySpan<byte> data);
    void DestroyTexture(nuint handle);
    void BindTexture(nuint handle, int slot);

    nuint CreateTextureArray(int width, int height, int layers);
    void UpdateTextureArrayLayer(nuint handle, int layer, ReadOnlySpan<byte> data);
    void BindTextureArray(int slot, nuint handle);

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
