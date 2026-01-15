//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;
using System.Runtime.InteropServices;
using Microsoft.JSInterop;
using noz.Platform;

namespace noz;

public class WebGLRender : IRender
{
    private readonly IJSRuntime _js;
    private IJSObjectReference? _module;
    private RenderBackendConfig _config = null!;

    public string ShaderExtension => ".gles";

    public WebGLRender(IJSRuntime js)
    {
        _js = js;
    }

    public void Init(RenderBackendConfig config)
    {
        _config = config;
        // JS module initialization happens async in InitAsync
    }

    public async Task InitAsync(RenderBackendConfig config)
    {
        _config = config;
        _module = await _js.InvokeAsync<IJSObjectReference>("import", "./js/noz/noz-webgl.js");
        await _module.InvokeVoidAsync("init");
    }

    public void Shutdown()
    {
        _module?.InvokeVoidAsync("shutdown");
    }

    public void BeginFrame()
    {
        _module?.InvokeVoidAsync("beginFrame");
    }

    public void EndFrame()
    {
        _module?.InvokeVoidAsync("endFrame");
    }

    public void Clear(Color color)
    {
        _module?.InvokeVoidAsync("clear", color.R, color.G, color.B, color.A);
    }

    public void SetViewport(int x, int y, int width, int height)
    {
        _module?.InvokeVoidAsync("setViewport", x, y, width, height);
    }

    // === Buffer Management ===

    public BufferHandle CreateVertexBuffer(int sizeInBytes, BufferUsage usage)
    {
        if (_module == null) return BufferHandle.Invalid;
        var id = _module.InvokeAsync<uint>("createVertexBuffer", sizeInBytes, (int)usage).AsTask().Result;
        return new BufferHandle(id);
    }

    public BufferHandle CreateIndexBuffer(int sizeInBytes, BufferUsage usage)
    {
        if (_module == null) return BufferHandle.Invalid;
        var id = _module.InvokeAsync<uint>("createIndexBuffer", sizeInBytes, (int)usage).AsTask().Result;
        return new BufferHandle(id);
    }

    public void DestroyBuffer(BufferHandle handle)
    {
        _module?.InvokeVoidAsync("destroyBuffer", handle.Id);
    }

    public void UpdateVertexBufferRange(BufferHandle buffer, int offsetBytes, ReadOnlySpan<MeshVertex> data)
    {
        if (_module == null) return;

        // Convert MeshVertex span to byte array for JS interop
        var bytes = MemoryMarshal.AsBytes(data).ToArray();
        _module.InvokeVoidAsync("updateVertexBufferRange", buffer.Id, offsetBytes, bytes);
    }

    public void UpdateIndexBufferRange(BufferHandle buffer, int offsetBytes, ReadOnlySpan<ushort> data)
    {
        if (_module == null) return;

        // Convert ushort span to byte array for JS interop
        var bytes = MemoryMarshal.AsBytes(data).ToArray();
        _module.InvokeVoidAsync("updateIndexBufferRange", buffer.Id, offsetBytes, bytes);
    }

    public void BindVertexBuffer(BufferHandle buffer)
    {
        _module?.InvokeVoidAsync("bindVertexBuffer", buffer.Id);
    }

    public void BindIndexBuffer(BufferHandle buffer)
    {
        _module?.InvokeVoidAsync("bindIndexBuffer", buffer.Id);
    }

    // === Texture Management ===

    public TextureHandle CreateTexture(int width, int height, ReadOnlySpan<byte> data)
    {
        if (_module == null) return TextureHandle.Invalid;
        var id = _module.InvokeAsync<ushort>("createTexture", width, height, data.ToArray()).AsTask().Result;
        return new TextureHandle(id);
    }

    public void UpdateTexture(TextureHandle handle, int width, int height, ReadOnlySpan<byte> data)
    {
        _module?.InvokeVoidAsync("updateTexture", handle.Id, width, height, data.ToArray());
    }

    public void DestroyTexture(TextureHandle handle)
    {
        _module?.InvokeVoidAsync("destroyTexture", handle.Id);
    }

    public void BindTexture(int slot, TextureHandle handle)
    {
        _module?.InvokeVoidAsync("bindTexture", slot, handle.Id);
    }

    // === Texture Array Management ===

    public TextureHandle CreateTextureArray(int width, int height, int layers)
    {
        if (_module == null) return TextureHandle.Invalid;
        var id = _module.InvokeAsync<ushort>("createTextureArray", width, height, layers).AsTask().Result;
        return new TextureHandle(id);
    }

    public void UpdateTextureArrayLayer(TextureHandle handle, int layer, ReadOnlySpan<byte> data)
    {
        _module?.InvokeVoidAsync("updateTextureArrayLayer", handle.Id, layer, data.ToArray());
    }

    public void BindTextureArray(int slot, TextureHandle handle)
    {
        _module?.InvokeVoidAsync("bindTextureArray", slot, handle.Id);
    }

    // === Shader Management ===

    public ShaderHandle CreateShader(string name, string vertexSource, string fragmentSource)
    {
        if (_module == null) return ShaderHandle.Invalid;
        var id = _module.InvokeAsync<byte>("createShader", name, vertexSource, fragmentSource).AsTask().Result;
        return new ShaderHandle(id);
    }

    public void DestroyShader(ShaderHandle handle)
    {
        _module?.InvokeVoidAsync("destroyShader", handle.Id);
    }

    public void BindShader(ShaderHandle handle)
    {
        _module?.InvokeVoidAsync("bindShader", handle.Id);
    }

    public void SetUniformMatrix4x4(string name, in Matrix4x4 value)
    {
        if (_module == null) return;

        // Convert to column-major array for WebGL
        float[] data =
        [
            value.M11, value.M21, value.M31, value.M41,
            value.M12, value.M22, value.M32, value.M42,
            value.M13, value.M23, value.M33, value.M43,
            value.M14, value.M24, value.M34, value.M44
        ];
        _module.InvokeVoidAsync("setUniformMatrix4x4", name, data);
    }

    public void SetUniformInt(string name, int value)
    {
        _module?.InvokeVoidAsync("setUniformInt", name, value);
    }

    public void SetUniformFloat(string name, float value)
    {
        _module?.InvokeVoidAsync("setUniformFloat", name, value);
    }

    public void SetUniformVec2(string name, Vector2 value)
    {
        _module?.InvokeVoidAsync("setUniformVec2", name, value.X, value.Y);
    }

    public void SetUniformVec4(string name, Vector4 value)
    {
        _module?.InvokeVoidAsync("setUniformVec4", name, value.X, value.Y, value.Z, value.W);
    }

    // === State Management ===

    public void SetBlendMode(BlendMode mode)
    {
        _module?.InvokeVoidAsync("setBlendMode", (int)mode);
    }

    // === Drawing ===

    public void DrawIndexedRange(int firstIndex, int indexCount, int baseVertex = 0)
    {
        _module?.InvokeVoidAsync("drawIndexedRange", firstIndex, indexCount, baseVertex);
    }

    // === Synchronization ===

    public FenceHandle CreateFence()
    {
        if (_module == null) return FenceHandle.Invalid;
        var id = _module.InvokeAsync<ulong>("createFence").AsTask().Result;
        return new FenceHandle(id);
    }

    public void WaitFence(FenceHandle fence)
    {
        _module?.InvokeVoidAsync("waitFence", fence.Id);
    }

    public void DeleteFence(FenceHandle fence)
    {
        _module?.InvokeVoidAsync("deleteFence", fence.Id);
    }

    // === Render Passes (stubs - to be implemented) ===

    public void ResizeOffscreenTarget(int width, int height, int msaaSamples)
    {
        // TODO: Implement offscreen rendering for WebGL
    }

    public void BeginScenePass(Color clearColor)
    {
        // For now, just clear - no offscreen rendering
        Clear(clearColor);
    }

    public void EndScenePass()
    {
        // TODO: Implement MSAA resolve for WebGL
    }

    public void Composite(ShaderHandle compositeShader)
    {
        // TODO: Implement composite for WebGL
    }
}
