//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace noz.Platform;

public enum BufferUsage
{
    Static,     // Data set once
    Dynamic,    // Data updated frequently
    Stream      // Data updated every frame
}

public class RenderBackendConfig
{
    public bool VSync { get; set; } = true;
}

public interface IRender
{
    // === Properties ===
    string ShaderExtension { get; }

    // === Lifecycle ===
    void Init(RenderBackendConfig config);
    void Shutdown();

    void BeginFrame();
    void EndFrame();

    // === Basic Operations ===
    void Clear(Color color);
    void SetViewport(int x, int y, int width, int height);

    // === Buffer Management ===
    BufferHandle CreateVertexBuffer(int sizeInBytes, BufferUsage usage);
    BufferHandle CreateIndexBuffer(int sizeInBytes, BufferUsage usage);
    void DestroyBuffer(BufferHandle handle);

    void UpdateVertexBufferRange(BufferHandle buffer, int offsetBytes, ReadOnlySpan<MeshVertex> data);
    void UpdateIndexBufferRange(BufferHandle buffer, int offsetBytes, ReadOnlySpan<ushort> data);

    void BindVertexBuffer(BufferHandle buffer);
    void BindIndexBuffer(BufferHandle buffer);

    // === Texture Management ===
    TextureHandle CreateTexture(int width, int height, ReadOnlySpan<byte> data);
    void DestroyTexture(TextureHandle handle);
    void BindTexture(int slot, TextureHandle handle);

    // === Shader Management ===
    ShaderHandle CreateShader(string vertexSource, string fragmentSource);
    void DestroyShader(ShaderHandle handle);
    void BindShader(ShaderHandle handle);
    void SetUniformMatrix4x4(string name, in Matrix4x4 value);
    void SetUniformInt(string name, int value);

    // === State Management ===
    void SetBlendMode(BlendMode mode);

    // === Drawing ===
    void DrawIndexedRange(int firstIndex, int indexCount, int baseVertex = 0);

    // === Synchronization ===
    FenceHandle CreateFence();
    void WaitFence(FenceHandle fence);
    void DeleteFence(FenceHandle fence);
}
